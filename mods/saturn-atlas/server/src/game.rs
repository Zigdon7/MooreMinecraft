//! Staging gameplay adapters. All host mutations remain server authoritative.
use crate::Store;
use pumpkin_plugin_api::{
    Context, Server, World,
    command::{
        Arg, ArgumentType, Command, CommandError, CommandNode, CommandSender, ConsumedArgs,
        StringType,
    },
    commands::CommandHandler,
    common::BlockPos,
    data_components::DataComponent,
    events::{
        BlockBreakEvent, BlockPlaceEvent, ChunkSendEvent, EventHandler, EventPriority,
        PlayerChatEvent, PlayerInteractEvent, PlayerJoinEvent,
    },
    events_wit::{
        BlockBreakEventData, BlockPlaceEventData, ChunkSendEventData, InteractAction,
        PlayerChatEventData, PlayerInteractEventData, PlayerJoinEventData,
    },
    item_stack::ItemStack,
    java_packets::{CMapItemData, ClientboundPacket, MapPatch},
    player::Player,
    scheduler::SchedulerExt,
    text::TextComponent,
    uuid,
};
use saturn_atlas_core::{
    Anchor, Marker, Position, Quote, TravelRules,
    explore::{self, ChunkKey, ExploreQueue},
};
use serde::{Deserialize, Serialize};
use std::{
    collections::{BTreeMap, BTreeSet},
    sync::{Arc, Mutex},
    time::{SystemTime, UNIX_EPOCH},
};
const MAP_ID: i32 = 2_000_000;
const MAX_CHUNKS: usize = 65536;
#[derive(Clone, Serialize, Deserialize)]
pub struct Tile {
    pub world: String,
    pub x: i32,
    pub z: i32,
    pub pixels: Vec<u8>,
}
#[derive(Clone, Serialize, Deserialize)]
pub struct Payment {
    pub player: String,
    pub destination: String,
    pub price: u32,
    pub stage: String,
}
pub struct Runtime {
    pub store: Store,
    pub quotes: BTreeMap<String, Quote>,
    pub queue: ExploreQueue,
    pub views: BTreeMap<String, u32>,
    /// Zoom each companion last asked for; snapshots sent after other commands reuse it.
    pub zooms: BTreeMap<String, u32>,
    /// Block the companion's open map is panned to; absent means it follows the player.
    pub centers: BTreeMap<String, (i32, i32)>,
    /// Players asked to type a new lodestone name in chat: player -> (anchor id, expiry ms).
    pub renames: BTreeMap<String, (String, u64)>,
    pub serial: u64,
    pub dirty: bool,
}
pub type Shared = Arc<Mutex<Runtime>>;
fn now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}
pub fn world_key(world: &World) -> String {
    saturn_atlas_core::world_identity(&world.get_id(), &world.get_dimension())
}
fn pid(p: &Player) -> String {
    uuid::to_string(p.get_id())
}
fn position(p: &Player) -> Position {
    let (x, y, z) = p.get_position();
    Position {
        world: world_key(&p.get_world()),
        x,
        y,
        z,
    }
}
fn clean_name(s: &str) -> Result<String, String> {
    let s = s.trim();
    if s.is_empty() || s.len() > 40 || s.chars().any(|c| c.is_control() || c == '§') {
        Err("Use 1–40 printable characters for names".into())
    } else {
        Ok(s.to_owned())
    }
}
fn lock(shared: &Shared) -> Result<std::sync::MutexGuard<'_, Runtime>, String> {
    shared.lock().map_err(|e| e.to_string())
}
fn anchor_block(p: &Player) -> Result<(BlockPos, Position), String> {
    let t = p
        .get_target_block(5)
        .ok_or("Look at a placed lodestone within 5 blocks")?;
    let b = BlockPos {
        x: t.0.floor() as i32,
        y: t.1.floor() as i32,
        z: t.2.floor() as i32,
    };
    if is_lodestone(&p.get_world().get_block_state(b).block_name) {
        return Ok((b, standing_spot(&world_key(&p.get_world()), b)));
    }
    // The ray can report a point on the block's face, which floors to the neighbouring block.
    let near = nearest_lodestone(p).ok_or("Look at or stand next to a placed lodestone")?;
    Ok((near, standing_spot(&world_key(&p.get_world()), near)))
}
fn is_lodestone(name: &str) -> bool {
    saturn_atlas_core::vanilla_block(name) == "lodestone"
}
/// Where a traveller stands: centred on top of the lodestone block.
fn standing_spot(world: &str, b: BlockPos) -> Position {
    Position {
        world: world.to_owned(),
        x: b.x as f64 + 0.5,
        y: b.y as f64 + 1.0,
        z: b.z as f64 + 0.5,
    }
}
/// Closest lodestone block within 3 blocks of the player.
fn nearest_lodestone(p: &Player) -> Option<BlockPos> {
    let (px, py, pz) = p.get_position();
    let (cx, cy, cz) = (px.floor() as i32, py.floor() as i32, pz.floor() as i32);
    let world = p.get_world();
    let mut best: Option<(i32, BlockPos)> = None;
    for dy in -3..=3 {
        for dz in -3..=3 {
            for dx in -3..=3 {
                let d = dx * dx + dy * dy + dz * dz;
                if best.as_ref().is_some_and(|(b, _)| *b <= d) {
                    continue;
                }
                let b = BlockPos {
                    x: cx + dx,
                    y: cy + dy,
                    z: cz + dz,
                };
                if is_lodestone(&world.get_block_state(b).block_name) {
                    best = Some((d, b));
                }
            }
        }
    }
    best.map(|(_, b)| b)
}
/// Registers the lodestone at `b` as a shared waypoint owned by `p`, unless it already is one.
/// Returns the waypoint's name when a new one was created.
fn register_lodestone(shared: &Shared, p: &Player, b: BlockPos) -> Result<Option<String>, String> {
    let world = world_key(&p.get_world());
    let mut r = lock(shared)?;
    if r.store.state.atlas.anchor_at(&world, b.x, b.y, b.z).is_some() {
        return Ok(None);
    }
    if r.store.state.atlas.anchors.len() >= 256 {
        return Err("Lodestone capacity reached; this one was not added as a waypoint".into());
    }
    let key = uuid::to_string(uuid::generate());
    let name = r.store.state.atlas.next_anchor_name();
    r.store.state.atlas.anchors.insert(
        key.clone(),
        Anchor {
            id: key,
            name: name.clone(),
            owner: pid(p),
            position: standing_spot(&world, b),
            public: true,
            enabled: true,
            grants: BTreeSet::new(),
        },
    );
    r.store.save()?;
    Ok(Some(name))
}
fn tell(p: &Player, kind: &str, message: &str) {
    p.send_system_message(TextComponent::text(message), false);
    send_feedback(p, kind, message);
}
fn origin<'a>(atlas: &'a saturn_atlas_core::Atlas, p: &Player) -> Option<&'a Anchor> {
    let at = position(p);
    let id = pid(p);
    atlas.anchors.values().find(|a| {
        a.accessible(&id) && a.position.distance(&at).is_some_and(|d| d <= 4.0) && exists(p, a)
    })
}
fn exists(p: &Player, a: &Anchor) -> bool {
    let w = p.get_world();
    if world_key(&w) != a.position.world {
        return false;
    }
    let b = BlockPos {
        x: a.position.x.floor() as i32,
        y: a.position.y.floor() as i32 - 1,
        z: a.position.z.floor() as i32,
    };
    w.get_chunk(b.x.div_euclid(16), b.z.div_euclid(16))
        .is_some()
        && is_lodestone(&w.get_block_state(b).block_name)
}
fn balance(p: &Player) -> u32 {
    p.get_inventory()
        .as_inventory()
        .count_item("minecraft:amethyst_shard")
}
fn safe_landing(p: &Player, a: &Anchor) -> Result<(f64, f64, f64), String> {
    if !exists(p, a) {
        return Err("Destination lodestone missing, unloaded, or in another dimension".into());
    }
    let w = p.get_world();
    let x = a.position.x.floor() as i32;
    let y = a.position.y.floor() as i32;
    let z = a.position.z.floor() as i32;
    for (dx, dz) in [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)] {
        let at = BlockPos {
            x: x + dx,
            y,
            z: z + dz,
        };
        if w.get_chunk(at.x.div_euclid(16), at.z.div_euclid(16))
            .is_none()
        {
            continue;
        }
        let feet = w.get_block_state(at);
        let head = w.get_block_state(BlockPos {
            x: at.x,
            y: y + 1,
            z: at.z,
        });
        let floor = w.get_block_state(BlockPos {
            x: at.x,
            y: y - 1,
            z: at.z,
        });
        let hazards = [
            "magma_block",
            "campfire",
            "soul_campfire",
            "cactus",
            "sweet_berry_bush",
        ];
        if feet.is_air
            && head.is_air
            && floor.is_full_cube
            && floor.is_solid
            && !hazards.contains(&saturn_atlas_core::vanilla_block(&floor.block_name))
            && w.get_border()
                .contains(at.x as f64 + 0.5, at.z as f64 + 0.5)
        {
            return Ok((at.x as f64 + 0.5, y as f64, at.z as f64 + 0.5));
        }
    }
    Err("No clear safe arrival space at destination".into())
}
fn varint(mut n: u32) -> Vec<u8> {
    let mut b = vec![];
    loop {
        let v = (n & 127) as u8;
        n >>= 7;
        b.push(if n > 0 { v | 128 } else { v });
        if n == 0 {
            break;
        }
    }
    b
}
fn ensure_map(p: &Player) -> Result<(), String> {
    if p.as_java().is_none() {
        return Err(
            "Held Atlas is Java-only in this staging build; Bedrock can use marker/travel commands"
                .into(),
        );
    }
    let inv = p.get_inventory().as_inventory();
    for i in 0..36 {
        if let Some(s) = inv.get_item(i) {
            if s.has_custom_data("saturn", "atlas") {
                return Ok(());
            }
        }
    }
    let slot = (0..36)
        .find(|i| inv.get_item(*i).is_none())
        .ok_or("Make one empty main-inventory slot")?;
    let map = ItemStack::new("minecraft:filled_map", 1);
    map.set_component(DataComponent::MapId, &varint(MAP_ID as u32));
    map.set_custom_name(Some(TextComponent::text("Saturn Shared Atlas")));
    map.set_custom_data(
        "saturn",
        "atlas",
        &pumpkin_plugin_api::common::NbtTree {
            root: 0,
            tags: vec![pumpkin_plugin_api::common::NbtTag::Int(1)],
        },
    );
    inv.set_item(slot, Some(map));
    Ok(())
}
fn render(shared: &Shared, p: &Player) -> Result<(), String> {
    let java = p
        .as_java()
        .ok_or("Held Atlas currently needs Java Edition")?;
    let id = pid(p);
    let at = position(p);
    let runtime = lock(shared)?;
    let zoom = *runtime.views.get(&id).unwrap_or(&1);
    let blocks = 4 * zoom as i32;
    let cx = at.x.floor() as i32;
    let cz = at.z.floor() as i32;
    let sx = cx - 64 * blocks;
    let sz = cz - 64 * blocks;
    let mut pixels = vec![0u8; 16384];
    for py in 0..128 {
        for px in 0..128 {
            let x = sx + px * blocks;
            let z = sz + py * blocks;
            let key = format!("{}:{}:{}", at.world, x.div_euclid(16), z.div_euclid(16));
            if let Some(t) = runtime.store.state.tiles.get(&key) {
                let i = (z.rem_euclid(16) / 4 * 4 + x.rem_euclid(16) / 4) as usize;
                if let Some(c) = t.pixels.get(i) {
                    pixels[(py * 128 + px) as usize] = *c
                }
            }
        }
    }
    let mut point = |x: f64, z: f64, color: u8| {
        let px = ((x - sx as f64) / blocks as f64) as i32;
        let py = ((z - sz as f64) / blocks as f64) as i32;
        if (1..127).contains(&px) && (1..127).contains(&py) {
            for (dx, dz) in [(0, 0), (1, 0), (-1, 0), (0, 1), (0, -1)] {
                pixels[((py + dz) * 128 + px + dx) as usize] = color
            }
        }
    };
    for m in runtime.store.state.atlas.markers_for(&id) {
        if m.position.world == at.world {
            point(m.position.x, m.position.z, 74)
        }
    }
    for a in runtime.store.state.atlas.destinations(&id) {
        if a.position.world == at.world {
            point(a.position.x, a.position.z, 126)
        }
    }
    point(at.x, at.z, 34);
    drop(runtime);
    java.send_packet(&ClientboundPacket::CMapItemData(CMapItemData {
        map_id: MAP_ID,
        scale: 2,
        tracking_position: false,
        locked: true,
        icons: Some(vec![]),
        data: Some(MapPatch {
            columns: 128,
            rows: 128,
            x: 0,
            z: 0,
            data: pixels,
        }),
    }));
    Ok(())
}
struct Cmd {
    shared: Shared,
    kind: bool,
}
impl CommandHandler for Cmd {
    fn handle(
        &self,
        sender: CommandSender,
        server: Server,
        args: ConsumedArgs,
    ) -> Result<i32, CommandError> {
        let words = match args.get_value("args") {
            Arg::Simple(s) | Arg::Msg(s) => s,
            _ => String::new(),
        };
        if self.kind && words.trim() == "diagnostics" && sender.is_console() {
            let r = lock(&self.shared).map_err(|e| CommandError::CommandFailed(TextComponent::text(&e)))?;
            for p in server.get_all_players() {
                let at = position(&p);
                let known = r.store.state.tiles.values().filter(|t|t.world == at.world).count();
                let nearest = r.store.state.atlas.anchors.values().filter_map(|a|a.position.distance(&at)).fold(f64::INFINITY,f64::min);
                sender.send_message(TextComponent::text(&format!("ATLAS_PLAYER name={} world={} shared_tiles={} nearest_anchor={:.1} origin_valid={}",p.get_name(),at.world,known,nearest,origin(&r.store.state.atlas,&p).is_some())));
            }
            return Ok(1);
        }
        if self.kind && words.trim() == "health" && sender.is_console() {
            let r = lock(&self.shared)
                .map_err(|e| CommandError::CommandFailed(TextComponent::text(&e)))?;
            sender.send_message(TextComponent::text(&format!(
                "ATLAS_HEALTH_OK tiles={} queued={} pending_payments={}",
                r.store.state.tiles.len(),
                r.queue.len(),
                r.store.state.pending.len()
            )));
            return Ok(1);
        }
        let is_sync = self.kind && words.split(' ').next() == Some("sync");
        let result = if let Some(p) = sender.as_player() {
            if self.kind {
                atlas_command(&self.shared, &p, &words)
            } else {
                stone_command(&self.shared, &p, &words)
            }
        } else {
            Err("Use this gameplay command in-game".into())
        };
        if let Some(p) = sender.as_player() {
            if !is_sync {
                match &result {
                    Ok(message) => {
                        send_feedback(&p, "notice", message);
                        let _ = sync_companion(&self.shared, &p, None);
                    }
                    Err(message) => send_feedback(&p, "error", message),
                }
            } else if let Err(message) = &result {
                send_feedback(&p, "error", message);
            }
        }
        match result {
            Ok(s) => {
                if !is_sync {
                    sender.send_message(TextComponent::text(&s));
                }
                Ok(1)
            }
            Err(e) => {
                sender.send_error(TextComponent::text(&e));
                Ok(0)
            }
        }
    }
}
struct Help;
impl CommandHandler for Help {
    fn handle(
        &self,
        sender: CommandSender,
        _: Server,
        _: ConsumedArgs,
    ) -> Result<i32, CommandError> {
        sender.send_message(TextComponent::text(&format!("Staging: /atlas view | center | zoom 1..4 | marker add <name> | marker remove <id> | markers; placed lodestones become waypoints; right-click yours to rename it. /lodestone rename <name> | create <name> | list | quote <id> | confirm | access <id> public/private. {} Not production-ready.", if TravelRules::default().free { "Travel is currently free." } else { "Travel currency: amethyst shards." })));
        Ok(1)
    }
}
fn atlas_command(shared: &Shared, p: &Player, args: &str) -> Result<String, String> {
    let id = pid(p);
    let mut words = args.splitn(3, ' ');
    let action = words.next().unwrap_or("");
    let sub = words.next().unwrap_or("");
    let rest = words.next().unwrap_or("");
    match action {
        // A bare sync is the closest view; `sync <zoom>` asks for a wider one.
        // `sync <zoom> <x> <z>` looks at another part of the shared map.
        "sync" => {
            match explore::parse_center(rest) {
                Some(c) => lock(shared)?.centers.insert(id, c),
                None => lock(shared)?.centers.remove(&id),
            };
            sync_companion(shared, p, Some(sub.parse().unwrap_or(1)))
        }
        "view" | "center" => {
            ensure_map(p)?;
            render(shared, p)?;
            Ok("Atlas refreshed; cyan=lodestone, yellow=marker, red=you. Unknown terrain is blank.".into())
        }
        "zoom" => {
            let z = sub.parse::<u32>().map_err(|_| "Zoom must be 1..4")?;
            if !(1..=4).contains(&z) {
                return Err("Zoom must be 1..4".into());
            };
            lock(shared)?.views.insert(id, z);
            render(shared, p)?;
            Ok(format!("Atlas scale: {} blocks/pixel", z * 4))
        }
        "markers" => {
            let r = lock(shared)?;
            Ok(r.store
                .state
                .atlas
                .markers_for(&id)
                .filter(|m| m.position.world == world_key(&p.get_world()))
                .take(50)
                .map(|m| format!("{} — {}", m.id, m.name))
                .collect::<Vec<_>>()
                .join("\n"))
        }
        "marker" if sub == "add" => {
            let name = clean_name(rest)?;
            let key = uuid::to_string(uuid::generate());
            let mut r = lock(shared)?;
            if r.store.state.atlas.markers.len() >= 1024 {
                return Err("Marker capacity reached".into());
            };
            if r.store.state.atlas.markers_for(&id).count() >= 64 {
                return Err("You have 64 markers; remove one first".into());
            };
            r.store.state.atlas.markers.insert(
                key.clone(),
                Marker {
                    id: key.clone(),
                    name,
                    owner: id,
                    position: position(p),
                },
            );
            r.store.save()?;
            Ok(format!("Personal marker created (only you see it): {key}"))
        }
        "marker" if sub == "remove" => {
            let mut r = lock(shared)?;
            r.store
                .state
                .atlas
                .remove_marker(rest, &id, false)
                .map_err(str::to_string)?;
            r.store.save()?;
            Ok("Marker removed".into())
        }
        _ => Err(
            "Use /atlas view, center, zoom 1..4, markers, marker add <name>, marker remove <id>"
                .into(),
        ),
    }
}
fn stone_command(shared: &Shared, p: &Player, args: &str) -> Result<String, String> {
    let id = pid(p);
    let (action, rest) = args.split_once(' ').unwrap_or((args, ""));
    match action {
        "create" => {
            let name = clean_name(rest)?;
            let (_, at) = anchor_block(p)?;
            let mut r = lock(shared)?;
            if r.store.state.atlas.anchors.len() >= 256 {
                return Err("Lodestone capacity reached".into());
            };
            if r.store
                .state
                .atlas
                .anchors
                .values()
                .any(|a| a.position == at)
            {
                return Err("This block already has an anchor".into());
            };
            let key = uuid::to_string(uuid::generate());
            r.store.state.atlas.anchors.insert(
                key.clone(),
                Anchor {
                    id: key.clone(),
                    name,
                    owner: id,
                    position: at,
                    public: true,
                    enabled: true,
                    grants: BTreeSet::new(),
                },
            );
            r.store.save()?;
            Ok(format!(
                "Lodestone registered and shared with all players: {key}. Use /lodestone access {key} private to hide it."
            ))
        }
        "rename" => {
            // `/lodestone rename <name>` renames your nearest lodestone; `<id> <name>` names one.
            let mut r = lock(shared)?;
            let (first, tail) = rest.split_once(' ').unwrap_or((rest, ""));
            let (key, name) = if r.store.state.atlas.anchors.contains_key(first) {
                (first.to_owned(), clean_name(tail)?)
            } else {
                let at = position(p);
                let key = r
                    .store
                    .state
                    .atlas
                    .anchors
                    .values()
                    .filter(|a| a.owner == id)
                    .filter_map(|a| a.position.distance(&at).map(|d| (d, a)))
                    .filter(|(d, _)| *d <= 6.0)
                    .min_by(|a, b| a.0.total_cmp(&b.0))
                    .map(|(_, a)| a.id.clone())
                    .ok_or("Stand within 6 blocks of a lodestone you own")?;
                (key, clean_name(rest)?)
            };
            let a = r.store.state.atlas.anchors.get_mut(&key).ok_or("Unknown anchor")?;
            if a.owner != id {
                return Err("Only the owner can rename a lodestone".into());
            }
            a.name = name.clone();
            r.store.save()?;
            Ok(format!("Lodestone renamed to {name}"))
        }
        "list" => {
            let r = lock(shared)?;
            Ok(r.store
                .state
                .atlas
                .destinations(&id)
                .take(50)
                .map(|a| format!("{} — {}", a.id, a.name))
                .collect::<Vec<_>>()
                .join("\n"))
        }
        "access" => {
            let (key, mode) = rest
                .split_once(' ')
                .ok_or("Use access <id> public/private")?;
            let mut r = lock(shared)?;
            let a = r
                .store
                .state
                .atlas
                .anchors
                .get_mut(key)
                .ok_or("Unknown anchor")?;
            if a.owner != id {
                return Err("Only the owner can change access".into());
            };
            a.public = match mode {
                "public" => true,
                "private" => false,
                _ => return Err("Use public or private".into()),
            };
            r.store.save()?;
            Ok("Access updated".into())
        }
        "quote" => {
            let mut r = lock(shared)?;
            let from = origin(&r.store.state.atlas, p)
                .ok_or("Stand within 4 blocks of an accessible origin lodestone")?
                .id
                .clone();
            if from == rest {
                return Err("Choose another lodestone".into());
            };
            let a = r.store.state.atlas.anchors.get(&from).unwrap();
            let b = r
                .store
                .state
                .atlas
                .anchors
                .get(rest)
                .filter(|a| a.accessible(&id))
                .ok_or("Destination unavailable")?;
            let rules = TravelRules::default();
            let price = rules
                .price(&a.position, &b.position)
                .map_err(str::to_string)?;
            safe_landing(p, b)?;
            r.serial += 1;
            let q = Quote {
                id: r.serial,
                player: id.clone(),
                origin: from,
                destination: rest.into(),
                price,
                expires_ms: now() + rules.quote_lifetime_ms,
            };
            r.quotes.insert(id, q);
            let cost = if price == 0 {
                "free".to_string()
            } else {
                format!("{price} amethyst shard(s)")
            };
            Ok(format!(
                "Price: {cost}. /lodestone confirm within 15 seconds. Stay at the origin."
            ))
        }
        "confirm" => {
            let mut r = lock(shared)?;
            if r.store.state.pending.contains_key(&id) {
                return Err(
                    "Unresolved previous payment; travel locked pending administrator review"
                        .into(),
                );
            };
            let q = r
                .quotes
                .remove(&id)
                .ok_or("No active quote; request a new one")?;
            TravelRules::default()
                .validate_quote(
                    &q,
                    &id,
                    &position(p),
                    &r.store.state.atlas,
                    now(),
                    balance(p),
                )
                .map_err(str::to_string)?;
            let dest = r
                .store
                .state
                .atlas
                .anchors
                .get(&q.destination)
                .ok_or("Destination removed")?;
            let landing = safe_landing(p, dest)?;
            // A free journey takes nothing, so there is no payment to journal or reconcile.
            let paid = q.price > 0;
            if paid {
                r.store.state.pending.insert(
                    id.clone(),
                    Payment {
                        player: id.clone(),
                        destination: q.destination.clone(),
                        price: q.price,
                        stage: "debit-intent".into(),
                    },
                );
                r.store.save()?;
            }
            let inv = p.get_inventory().as_inventory();
            let mut remaining = q.price;
            for slot in 0..36 {
                if remaining == 0 {
                    break;
                }
                if let Some(stack) = inv.get_item(slot) {
                    if stack.get_registry_key() == "minecraft:amethyst_shard" {
                        let n = remaining.min(stack.get_count() as u32);
                        let left = stack.get_count() - n as u8;
                        if left == 0 {
                            inv.set_item(slot, None)
                        } else {
                            stack.set_count(left);
                            inv.set_item(slot, Some(stack))
                        };
                        remaining -= n;
                    }
                }
            }
            if remaining != 0 {
                return Err("Payment interrupted. Travel locked for admin reconciliation; no further charge attempted.".into());
            }
            if paid {
                r.store.state.pending.get_mut(&id).unwrap().stage = "debited".into();
                r.store.save()?;
            }
            drop(r);
            p.teleport(landing, None, None, p.get_world());
            // The host teleport call is synchronous, but a real client ACK is not exposed here.
            // Preserve a durable journal until position verification; ambiguous crash windows fail closed.
            let mut r = lock(shared)?;
            if position(p)
                .distance(&Position {
                    world: world_key(&p.get_world()),
                    x: landing.0,
                    y: landing.1,
                    z: landing.2,
                })
                .is_none_or(|d| d > 1.0)
            {
                return Err(if paid {
                    "Arrival not confirmed; payment journal retained for admin reconciliation"
                } else {
                    "Arrival not confirmed; nothing was charged. Request a new quote."
                }
                .into());
            }
            if paid {
                r.store.state.pending.remove(&id);
                r.store.save()?;
            }
            tracing::info!("SATURN_TRAVEL_COMMITTED player={} fee={}", id, q.price);
            Ok("Travel complete".into())
        }
        _ => Err(
            "Use /lodestone rename <name>, create <name>, list, quote <id>, confirm, access <id> public/private"
                .into(),
        ),
    }
}
/// A placed lodestone becomes a waypoint for the player who placed it.
struct Placed {
    shared: Shared,
}
impl EventHandler<BlockPlaceEvent> for Placed {
    fn handle(&self, _: Server, e: BlockPlaceEventData) -> BlockPlaceEventData {
        if e.cancelled || !e.can_build || !is_lodestone(&e.block_placed) {
            return e;
        }
        let (id, b, shared) = (e.player.get_id(), e.block_pos, self.shared.clone());
        // The event fires before the block exists; register once it is really there.
        pumpkin_plugin_api::scheduler::schedule_delayed_task(2, move |server| {
            let Some(p) = server.get_player_by_uuid(id) else { return };
            if !is_lodestone(&p.get_world().get_block_state(b).block_name) {
                return;
            }
            match register_lodestone(&shared, &p, b) {
                Ok(Some(name)) => {
                    tell(&p, "notice", &format!("Lodestone \"{name}\" added to the map for all players. Right-click it to rename."));
                    let _ = sync_companion(&shared, &p, None);
                }
                Ok(None) => {}
                Err(message) => tell(&p, "error", &message),
            }
        });
        e
    }
}
/// A broken lodestone stops being a waypoint.
struct Broken {
    shared: Shared,
}
impl EventHandler<BlockBreakEvent> for Broken {
    fn handle(&self, _: Server, e: BlockBreakEventData) -> BlockBreakEventData {
        if e.cancelled || !is_lodestone(&e.block) {
            return e;
        }
        let (b, shared) = (e.block_pos, self.shared.clone());
        let breaker = e.player.as_ref().map(|p| p.get_id());
        pumpkin_plugin_api::scheduler::schedule_delayed_task(2, move |server| {
            let worlds = server.get_all_worlds();
            let Ok(mut r) = shared.lock() else { return };
            // The event carries no world, so check every anchor on a block at these coordinates
            // and drop the ones whose lodestone is really gone.
            let gone: Vec<(String, String)> = r
                .store
                .state
                .atlas
                .anchors
                .values()
                .filter(|a| {
                    a.position.x.floor() as i32 == b.x
                        && a.position.y.floor() as i32 - 1 == b.y
                        && a.position.z.floor() as i32 == b.z
                })
                .filter(|a| {
                    worlds
                        .iter()
                        .find(|w| world_key(w) == a.position.world)
                        .is_some_and(|w| !is_lodestone(&w.get_block_state(b).block_name))
                })
                .map(|a| (a.id.clone(), a.name.clone()))
                .collect();
            if gone.is_empty() {
                return;
            }
            for (key, _) in &gone {
                r.store.state.atlas.anchors.remove(key);
                r.renames.retain(|_, (anchor, _)| anchor != key);
            }
            if let Err(e) = r.store.save() {
                tracing::error!("Atlas save after lodestone removal failed: {}", e);
            }
            drop(r);
            if let Some(p) = breaker.and_then(|id| server.get_player_by_uuid(id)) {
                tell(&p, "notice", &format!("Lodestone waypoint \"{}\" removed.", gone[0].1));
                let _ = sync_companion(&shared, &p, None);
            }
        });
        e
    }
}
/// Right-clicking a lodestone: the owner is asked for a new name; an unregistered one is claimed.
struct Touched {
    shared: Shared,
}
impl EventHandler<PlayerInteractEvent> for Touched {
    fn handle(&self, _: Server, mut e: PlayerInteractEventData) -> PlayerInteractEventData {
        let Some(b) = e.clicked_pos else { return e };
        if e.cancelled || !matches!(e.action, InteractAction::RightClickBlock) || !is_lodestone(&e.block) {
            return e;
        }
        let p = &e.player;
        // Leave vanilla compass binding alone.
        let inv = p.get_inventory();
        for hand in [pumpkin_plugin_api::common::Hand::Right, pumpkin_plugin_api::common::Hand::Left] {
            if inv.get_item_in_hand(hand).is_some_and(|s| s.get_registry_key() == "minecraft:compass") {
                return e;
            }
        }
        let (id, world) = (pid(p), world_key(&p.get_world()));
        let found = lock(&self.shared).ok().and_then(|r| {
            r.store.state.atlas.anchor_at(&world, b.x, b.y, b.z).map(|a| (a.id.clone(), a.name.clone(), a.owner.clone()))
        });
        match found {
            None => match register_lodestone(&self.shared, p, b) {
                Ok(Some(name)) => {
                    tell(p, "notice", &format!("Lodestone \"{name}\" added to the map for all players. Right-click it again to rename."));
                    let _ = sync_companion(&self.shared, p, None);
                    e.cancelled = true;
                }
                Ok(None) => {}
                Err(message) => tell(p, "error", &message),
            },
            Some((key, name, owner)) if owner == id => {
                if let Ok(mut r) = lock(&self.shared) {
                    // Both hands can report the same click; ask once.
                    let asked = r.renames.get(&id).is_some_and(|(k, until)| *k == key && *until > now() + 29_000);
                    r.renames.insert(id, (key, now() + 30_000));
                    drop(r);
                    if !asked {
                        tell(p, "notice", &format!("Type a new name for \"{name}\" in chat within 30 seconds, or type cancel."));
                    }
                }
                e.cancelled = true;
            }
            Some((_, name, _)) => tell(p, "notice", &format!("\"{name}\" belongs to another player.")),
        }
        e
    }
}
/// The chat line after a rename prompt is the new name; it is not broadcast.
struct Named {
    shared: Shared,
}
impl EventHandler<PlayerChatEvent> for Named {
    fn handle(&self, _: Server, mut e: PlayerChatEventData) -> PlayerChatEventData {
        if e.cancelled {
            return e;
        }
        let id = pid(&e.player);
        let Ok(mut r) = lock(&self.shared) else { return e };
        let Some((key, until)) = r.renames.remove(&id) else { return e };
        if until < now() {
            return e;
        }
        e.cancelled = true;
        let text = e.message.trim().to_owned();
        if text.eq_ignore_ascii_case("cancel") {
            drop(r);
            tell(&e.player, "notice", "Rename cancelled.");
            return e;
        }
        let result = clean_name(&text).and_then(|name| {
            let a = r.store.state.atlas.anchors.get_mut(&key).ok_or("That lodestone is no longer a waypoint")?;
            a.name = name.clone();
            r.store.save()?;
            Ok(name)
        });
        drop(r);
        match result {
            Ok(name) => {
                tell(&e.player, "notice", &format!("Lodestone renamed to \"{name}\"."));
                let _ = sync_companion(&self.shared, &e.player, None);
            }
            Err(message) => tell(&e.player, "error", &format!("{message}. Right-click the lodestone to try again.")),
        }
        e
    }
}
struct Chunks {
    shared: Shared,
}
impl EventHandler<ChunkSendEvent> for Chunks {
    fn handle(&self, _: Server, e: ChunkSendEventData) -> ChunkSendEventData {
        if !e.cancelled {
            if let Ok(mut r) = self.shared.lock() {
                r.queue
                    .push((world_key(&e.target_world), e.chunk_x, e.chunk_z));
            }
        }
        e
    }
}
fn color(name: &str) -> u8 {
    if name.contains("water") {
        50
    } else if name.contains("sand") {
        10
    } else if name.contains("snow") || name.contains("ice") {
        34
    } else if name.contains("grass") || name.contains("leaves") || name.contains("moss") {
        6
    } else if name.contains("dirt") || name.contains("log") {
        42
    } else if name.contains("lava") {
        18
    } else {
        46
    }
}
struct JoinSnapshot {
    shared: Shared,
}
impl EventHandler<PlayerJoinEvent> for JoinSnapshot {
    fn handle(&self, _: Server, event: PlayerJoinEventData) -> PlayerJoinEventData {
        let id = event.player.get_id();
        let shared = self.shared.clone();
        pumpkin_plugin_api::scheduler::schedule_delayed_task(20, move |server| {
            if let Some(p) = server.get_player_by_uuid(id) {
                if p.as_java().is_some() {
                    if let Ok(mut r) = shared.lock() {
                        r.centers.remove(&uuid::to_string(id));
                    }
                    let _ = sync_companion(&shared, &p, Some(1));
                }
            }
        });
        event
    }
}
pub fn setup(context: &Context, shared: Shared) -> Result<(), String> {
    context.register_event_handler(
        JoinSnapshot {
            shared: shared.clone(),
        },
        EventPriority::Normal,
        true,
    )?;
    context.register_permission(&pumpkin_plugin_api::permission::Permission {
        node: "saturn-atlas:command.use".into(),
        description: "Use the Atlas and own lodestones".into(),
        default: pumpkin_plugin_api::permission::PermissionDefault::Allow,
        children: vec![],
    })?;
    tracing::info!("SATURN_COMMAND_ACCESS node=saturn-atlas:command.use default=allow");
    for (name, kind) in [("atlas", true), ("lodestone", false)] {
        let cmd = Command::new(&[name.into()], "Saturn staging Atlas and Lodestones").execute(Help);
        let arg =
            CommandNode::argument("args", &ArgumentType::String(StringType::Greedy)).execute(Cmd {
                shared: shared.clone(),
                kind,
            });
        let cmd = cmd.then(arg);
        context.register_command(cmd, "saturn-atlas:command.use");
    }
    context.register_event_handler(Placed { shared: shared.clone() }, EventPriority::Normal, true)?;
    context.register_event_handler(Broken { shared: shared.clone() }, EventPriority::Normal, true)?;
    context.register_event_handler(Touched { shared: shared.clone() }, EventPriority::Normal, true)?;
    context.register_event_handler(Named { shared: shared.clone() }, EventPriority::Normal, true)?;
    context.register_event_handler(
        Chunks {
            shared: shared.clone(),
        },
        EventPriority::Highest,
        true,
    )?;
    let processing = shared.clone();
    let runs = std::sync::atomic::AtomicU32::new(0);
    context.schedule_repeating_task(20, 5, move |server| {
        let shared = &processing;
        let worlds = server.get_all_worlds();
        let players = server.get_all_players();
        let at: Vec<ChunkKey> = players
            .iter()
            .map(|p| {
                let (x, _, z) = p.get_position();
                (
                    world_key(&p.get_world()),
                    (x.floor() as i32).div_euclid(16),
                    (z.floor() as i32).div_euclid(16),
                )
            })
            .collect();
        // Terrain around players first, so the visible map follows them; then the backlog of
        // sent chunks. Host calls happen without the lock held.
        let near = {
            let Ok(r) = shared.lock() else { return };
            explore::missing_near(&at, explore::VIEW_RADIUS, |(w, x, z)| {
                r.store.state.tiles.contains_key(&format!("{w}:{x}:{z}"))
            })
        };
        let sample = |key: &ChunkKey| -> Option<Tile> {
            let (world, x, z) = key;
            let chunk = worlds
                .iter()
                .find(|w| world_key(w) == *world)?
                .get_chunk(*x, *z)?;
            let mut pixels = vec![];
            for dz in 0..4 {
                for dx in 0..4 {
                    let bx = dx * 4 + 2;
                    let bz = dz * 4 + 2;
                    let y = chunk.get_top_block_y(bx, bz);
                    let mut state = chunk.get_block_state(BlockPos { x: bx, y, z: bz });
                    if state.is_air && y > -64 {
                        state = chunk.get_block_state(BlockPos {
                            x: bx,
                            y: y - 1,
                            z: bz,
                        });
                    }
                    pixels.push(color(&state.block_name));
                }
            }
            Some(Tile {
                world: world.clone(),
                x: *x,
                z: *z,
                pixels,
            })
        };
        let mut tiles = vec![];
        for key in &near {
            if tiles.len() >= explore::BUDGET {
                break;
            }
            // Not loaded yet: costs no budget and is retried on the next run.
            if let Some(tile) = sample(key) {
                tiles.push(tile);
            }
        }
        while tiles.len() < explore::BUDGET {
            let next = {
                let Ok(mut r) = shared.lock() else { return };
                r.queue.pop()
            };
            let Some(key) = next else { break };
            if let Some(tile) = sample(&key) {
                tiles.push(tile);
            }
        }
        if !tiles.is_empty() {
            let Ok(mut r) = shared.lock() else { return };
            for tile in tiles {
                let name = format!("{}:{}:{}", tile.world, tile.x, tile.z);
                // At capacity, known chunks still refresh; only new ones are refused.
                if r.store.state.tiles.len() < MAX_CHUNKS
                    || r.store.state.tiles.contains_key(&name)
                {
                    r.queue.forget(&(tile.world.clone(), tile.x, tile.z));
                    r.store
                        .state
                        .atlas
                        .record_delivered_chunk(&tile.world, tile.x, tile.z);
                    r.store.state.tiles.insert(name, tile);
                    r.dirty = true;
                }
            }
        }
        if runs.fetch_add(1, std::sync::atomic::Ordering::Relaxed) % 4 != 3 {
            return;
        }
        for p in players {
            let has = p
                .get_inventory()
                .as_inventory()
                .get_all_items()
                .into_iter()
                .flatten()
                .any(|s| s.has_custom_data("saturn", "atlas"));
            if has {
                if let Err(e) = render(&shared, &p) {
                    tracing::debug!("Atlas render: {}", e);
                }
            }
        }
    });
    let persist = shared.clone();
    context.schedule_repeating_task(200, 200, move |_| {
        if let Ok(mut r) = persist.lock() {
            if r.dirty {
                match r.store.save() {
                    Ok(()) => r.dirty = false,
                    Err(e) => tracing::error!("Atlas autosave failed: {}", e),
                }
            }
        }
    });
    Ok(())
}

fn sync_companion(shared: &Shared, p: &Player, requested: Option<u32>) -> Result<String, String> {
    let java = p.as_java().ok_or("Companion protocol is Java-only")?;
    let at = position(p);
    let id = pid(p);
    let mut r = lock(shared)?;
    let zoom = match requested {
        Some(z) => {
            let z = explore::valid_zoom(z);
            r.zooms.insert(id.clone(), z);
            z
        }
        None => *r.zooms.get(&id).unwrap_or(&1),
    };
    let r = r;
    // Tiles are in units of `zoom` chunks, centred on the tile the player stands in, or on the
    // block the open map was dragged to.
    let (view_x, view_z) = r
        .centers
        .get(&id)
        .copied()
        .unwrap_or((at.x.floor() as i32, at.z.floor() as i32));
    let (cx, cz) = explore::center_tile(zoom, view_x, view_z);
    let mut tiles = vec![];
    if zoom >= 8 {
        // Aggregate actual shared records rather than sampling five positions that can miss
        // another player's entire explored trail at wide zoom. No player-ID filter here.
        let overview = explore::shared_overview(zoom, cx, cz,
            r.store.state.tiles.values().filter(|t| t.world == at.world)
                .map(|t| (t.x, t.z, t.pixels.as_slice())));
        for ((tx, tz), pixels) in overview {
            tiles.push(serde_json::json!({"world":at.world,"x":tx,"z":tz,"pixels":pixels}));
        }
    } else {
    for tz in cz - explore::VIEW_RADIUS..=cz + explore::VIEW_RADIUS {
        for tx in cx - explore::VIEW_RADIUS..=cx + explore::VIEW_RADIUS {
            let pixels = explore::zoom_tile(zoom, tx, tz, |x, z| {
                r.store
                    .state
                    .tiles
                    .get(&format!("{}:{x}:{z}", at.world))
                    .map(|t| &t.pixels[..])
            });
            if let Some(pixels) = pixels {
                tiles.push(serde_json::json!({"world":at.world,"x":tx,"z":tz,"pixels":pixels}));
            }
        }
    }
    }
    let anchors: Vec<_> = r
        .store
        .state
        .atlas
        .destinations(&id)
        .filter(|a| a.position.world == at.world)
        .take(40)
        .map(|a| serde_json::json!({"id":a.id,"name":a.name,"x":a.position.x,"z":a.position.z}))
        .collect();
    let markers: Vec<_> = r
        .store
        .state
        .atlas
        .markers_for(&id)
        .filter(|m| m.position.world == at.world)
        .take(40)
        .map(|m| serde_json::json!({"id":m.id,"name":m.name,"x":m.position.x,"z":m.position.z}))
        .collect();
    let text=serde_json::to_string(&serde_json::json!({"v":1,"world":at.world,"x":at.x,"z":at.z,"zoom":zoom,"max_zoom":explore::MAX_ZOOM.min(explore::LEGACY_MAX_ZOOM.max(zoom)),"zoom_limit":explore::MAX_ZOOM,"cx":view_x,"cz":view_z,"tiles":tiles,"anchors":anchors,"markers":markers,"balance":balance(p),"quote":r.quotes.get(&id).filter(|q|q.expires_ms>now()).map(|q|serde_json::json!({"destination":q.destination,"price":q.price,"remaining_ms":q.expires_ms.saturating_sub(now()).min(15000)}))})).map_err(|e|e.to_string())?;
    if text.len() > 32000 {
        return Err("Map snapshot exceeds protocol size limit".into());
    }
    let bytes = saturn_atlas_core::protocol::encode(&text).map_err(str::to_string)?;
    drop(r);
    java.send_custom_payload("saturn:atlas_v1", &bytes);
    Ok("Atlas companion snapshot sent".into())
}

fn send_feedback(p: &Player, kind: &str, message: &str) {
    if let Some(java) = p.as_java() {
        let short: String = message
            .chars()
            .map(|c| if c.is_control() { ' ' } else { c })
            .take(256)
            .collect();
        let value = if kind == "error" {
            serde_json::json!({"v":1,"error":short})
        } else {
            serde_json::json!({"v":1,"notice":short})
        };
        let json = value.to_string();
        let Ok(bytes) = saturn_atlas_core::protocol::encode(&json) else {
            return;
        };
        java.send_custom_payload("saturn:atlas_v1", &bytes);
    }
}
