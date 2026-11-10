//! Staging gameplay adapters. All host mutations remain server authoritative.
use crate::Store;
use pumpkin_plugin_api::{
    Context, Server,
    command::{
        Arg, ArgumentType, Command, CommandError, CommandNode, CommandSender, ConsumedArgs,
        StringType,
    },
    commands::CommandHandler,
    common::BlockPos,
    data_components::DataComponent,
    events::{ChunkSendEvent, EventHandler, EventPriority, PlayerJoinEvent},
    events_wit::{ChunkSendEventData, PlayerJoinEventData},
    item_stack::ItemStack,
    java_packets::{CMapItemData, ClientboundPacket, MapPatch},
    player::Player,
    scheduler::SchedulerExt,
    text::TextComponent,
    uuid,
};
use saturn_atlas_core::{Anchor, Marker, Position, Quote, TravelRules};
use serde::{Deserialize, Serialize};
use std::{
    collections::{BTreeMap, BTreeSet, VecDeque},
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
    pub queue: VecDeque<(String, i32, i32)>,
    pub queued: BTreeSet<(String, i32, i32)>,
    pub views: BTreeMap<String, u32>,
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
fn pid(p: &Player) -> String {
    uuid::to_string(p.get_id())
}
fn position(p: &Player) -> Position {
    let (x, y, z) = p.get_position();
    Position {
        world: p.get_world().get_id(),
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
    if p.get_world().get_block_state(b).block_name != "minecraft:lodestone" {
        return Err("Look at a placed vanilla lodestone".into());
    }
    let a = Position {
        world: p.get_world().get_id(),
        x: b.x as f64 + 0.5,
        y: b.y as f64 + 1.0,
        z: b.z as f64 + 0.5,
    };
    Ok((b, a))
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
    if w.get_id() != a.position.world {
        return false;
    }
    let b = BlockPos {
        x: a.position.x.floor() as i32,
        y: a.position.y.floor() as i32 - 1,
        z: a.position.z.floor() as i32,
    };
    w.get_chunk(b.x.div_euclid(16), b.z.div_euclid(16))
        .is_some()
        && w.get_block_state(b).block_name == "minecraft:lodestone"
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
            "minecraft:magma_block",
            "minecraft:campfire",
            "minecraft:soul_campfire",
            "minecraft:cactus",
            "minecraft:sweet_berry_bush",
        ];
        if feet.is_air
            && head.is_air
            && floor.is_full_cube
            && floor.is_solid
            && !hazards.contains(&floor.block_name.as_str())
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
    for m in runtime.store.state.atlas.markers.values() {
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
        _server: Server,
        args: ConsumedArgs,
    ) -> Result<i32, CommandError> {
        let words = match args.get_value("args") {
            Arg::Simple(s) | Arg::Msg(s) => s,
            _ => String::new(),
        };
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
            if words.trim() != "sync" {
                match &result {
                    Ok(message) => {
                        send_feedback(&p, "notice", message);
                        let _ = sync_companion(&self.shared, &p);
                    }
                    Err(message) => send_feedback(&p, "error", message),
                }
            } else if let Err(message) = &result {
                send_feedback(&p, "error", message);
            }
        }
        match result {
            Ok(s) => {
                if words.trim() != "sync" {
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
        sender.send_message(TextComponent::text("Staging: /atlas view | center | zoom 1..4 | marker add <name> | marker remove <id> | markers; /lodestone create <name> | list | quote <id> | confirm | access <id> public/private. Travel currency: amethyst shards. Not production-ready."));
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
        "sync" => sync_companion(shared, p),
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
                .markers
                .values()
                .filter(|m| m.position.world == p.get_world().get_id())
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
            Ok(format!("Shared marker created: {key}"))
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
                    public: false,
                    enabled: true,
                    grants: BTreeSet::new(),
                },
            );
            r.store.save()?;
            Ok(format!(
                "Lodestone registered (private): {key}. Use /lodestone access {key} public to share it."
            ))
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
            Ok(format!(
                "Price: {price} amethyst shard(s). /lodestone confirm within 15 seconds. Stay at the origin."
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
            r.store.state.pending.get_mut(&id).unwrap().stage = "debited".into();
            r.store.save()?;
            drop(r);
            p.teleport(landing, None, None, p.get_world());
            // The host teleport call is synchronous, but a real client ACK is not exposed here.
            // Preserve a durable journal until position verification; ambiguous crash windows fail closed.
            let mut r = lock(shared)?;
            if position(p)
                .distance(&Position {
                    world: p.get_world().get_id(),
                    x: landing.0,
                    y: landing.1,
                    z: landing.2,
                })
                .is_none_or(|d| d > 1.0)
            {
                return Err(
                    "Arrival not confirmed; payment journal retained for admin reconciliation"
                        .into(),
                );
            }
            r.store.state.pending.remove(&id);
            r.store.save()?;
            tracing::info!("SATURN_TRAVEL_COMMITTED player={} fee={}", id, q.price);
            Ok("Travel complete".into())
        }
        _ => Err(
            "Use /lodestone create <name>, list, quote <id>, confirm, access <id> public/private"
                .into(),
        ),
    }
}
struct Chunks {
    shared: Shared,
}
impl EventHandler<ChunkSendEvent> for Chunks {
    fn handle(&self, _: Server, e: ChunkSendEventData) -> ChunkSendEventData {
        if !e.cancelled {
            if let Ok(mut r) = self.shared.lock() {
                let key = (e.target_world.get_id(), e.chunk_x, e.chunk_z);
                if r.queue.len() < 256 && r.queued.insert(key.clone()) {
                    r.queue.push_back(key)
                }
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
                    let _ = sync_companion(&shared, &p);
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
    context.register_event_handler(
        Chunks {
            shared: shared.clone(),
        },
        EventPriority::Highest,
        true,
    )?;
    let processing = shared.clone();
    context.schedule_repeating_task(20, 20, move |server| {
        let shared = &processing;
        let next = {
            let Ok(mut r) = shared.lock() else { return };
            let v = r.queue.pop_front();
            if let Some(k) = &v {
                r.queued.remove(k);
            }
            v
        };
        if let Some((world, x, z)) = next {
            if let Some(w) = server
                .get_all_worlds()
                .into_iter()
                .find(|w| w.get_id() == world)
            {
                if let Some(chunk) = w.get_chunk(x, z) {
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
                    if let Ok(mut r) = shared.lock() {
                        if r.store.state.tiles.len() < MAX_CHUNKS {
                            r.store.state.atlas.record_delivered_chunk(&world, x, z);
                            r.store.state.tiles.insert(
                                format!("{world}:{x}:{z}"),
                                Tile {
                                    world,
                                    x,
                                    z,
                                    pixels,
                                },
                            );
                            r.dirty = true;
                        }
                    }
                }
            }
        }
        for p in server.get_all_players() {
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

fn sync_companion(shared: &Shared, p: &Player) -> Result<String, String> {
    let java = p.as_java().ok_or("Companion protocol is Java-only")?;
    let at = position(p);
    let id = pid(p);
    let r = lock(shared)?;
    let cx = (at.x.floor() as i32).div_euclid(16);
    let cz = (at.z.floor() as i32).div_euclid(16);
    let tiles: Vec<_> = r
        .store
        .state
        .tiles
        .values()
        .filter(|t| t.world == at.world && (t.x - cx).abs() <= 4 && (t.z - cz).abs() <= 4)
        .collect();
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
        .markers
        .values()
        .filter(|m| m.position.world == at.world)
        .take(40)
        .map(|m| serde_json::json!({"id":m.id,"name":m.name,"x":m.position.x,"z":m.position.z}))
        .collect();
    let text=serde_json::to_string(&serde_json::json!({"v":1,"world":at.world,"x":at.x,"z":at.z,"tiles":tiles,"anchors":anchors,"markers":markers,"balance":balance(p),"quote":r.quotes.get(&id).filter(|q|q.expires_ms>now()).map(|q|serde_json::json!({"destination":q.destination,"price":q.price,"remaining_ms":q.expires_ms.saturating_sub(now()).min(15000)}))})).map_err(|e|e.to_string())?;
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
