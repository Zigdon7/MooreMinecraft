//! Exploration scheduling: which chunks the server samples into map tiles, and in what order.
//!
//! Two sources feed the map. Chunks around each player that have no tile yet are sampled first,
//! nearest first, so the visible map fills in where the player is standing. Chunk-send events
//! feed a bounded backlog for everything else (terrain seen at a distance, resent chunks).
use std::collections::{BTreeSet, VecDeque};

pub type ChunkKey = (String, i32, i32);
/// Radius, in chunks, of the snapshot the companion draws around a player.
pub const VIEW_RADIUS: i32 = 4;
/// Chunks sampled per scheduler run.
pub const BUDGET: usize = 8;
/// Backlog capacity; one player at view distance 16 is sent 1089 chunks on join.
pub const BACKLOG: usize = 4096;

/// Zoom levels a companion may request: one snapshot tile spans `zoom` x `zoom` chunks.
pub const ZOOMS: [u32; 7] = [1, 2, 4, 8, 16, 32, 64];
pub const MAX_ZOOM: u32 = 64;
/// Widest zoom companions before 0.1.4 accept in a reply; they reject a larger `max_zoom`.
pub const LEGACY_MAX_ZOOM: u32 = 16;
/// Unsupported requests fall back to the closest view.
pub fn valid_zoom(zoom: u32) -> u32 {
    if ZOOMS.contains(&zoom) { zoom } else { 1 }
}
/// Furthest a companion may ask to look from the world origin, in blocks.
pub const MAX_CENTER: i32 = 30_000_000;
/// Parses the optional "<x> <z>" block coordinates of a panned view. Anything malformed or out
/// of range means "follow the player".
pub fn parse_center(text: &str) -> Option<(i32, i32)> {
    let mut words = text.split_whitespace();
    let x = words.next()?.parse::<i32>().ok()?;
    let z = words.next()?.parse::<i32>().ok()?;
    (words.next().is_none() && x.abs() <= MAX_CENTER && z.abs() <= MAX_CENTER).then_some((x, z))
}
/// Snapshot tile containing block (`x`, `z`) at `zoom`.
pub fn center_tile(zoom: u32, x: i32, z: i32) -> (i32, i32) {
    let span = 16 * zoom as i32;
    (x.div_euclid(span), z.div_euclid(span))
}
/// The 4 x 4 pixels of snapshot tile (`tx`, `tz`) at `zoom`, each taken from the mapped chunk
/// pixel under its centre. When a pixel spans several chunks and the centre one is unmapped,
/// the centres of its four quarters are tried, so thin explored trails still show when zoomed
/// far out. Unmapped terrain is 0; a tile with no mapped pixel is `None`.
/// At zoom 1 this is the chunk's own tile.
pub fn zoom_tile<'a>(
    zoom: u32,
    tx: i32,
    tz: i32,
    chunk: impl Fn(i32, i32) -> Option<&'a [u8]>,
) -> Option<Vec<u8>> {
    let zoom = zoom as i32;
    let mut pixels = Vec::with_capacity(16);
    for j in 0..4 {
        for i in 0..4 {
            let x = tx * 16 * zoom + i * 4 * zoom + 2 * zoom;
            let z = tz * 16 * zoom + j * 4 * zoom + 2 * zoom;
            let at = |x: i32, z: i32| {
                let index = (z.rem_euclid(16) / 4 * 4 + x.rem_euclid(16) / 4) as usize;
                chunk(x.div_euclid(16), z.div_euclid(16))
                    .and_then(|p| p.get(index).copied())
                    .filter(|c| *c != 0)
            };
            // A pixel is 4 * zoom blocks wide; from zoom 8 that is more than one chunk.
            let q = zoom;
            let colour = at(x, z).or_else(|| {
                (zoom >= 8)
                    .then(|| {
                        [(-q, -q), (q, -q), (-q, q), (q, q)]
                            .into_iter()
                            .find_map(|(dx, dz)| at(x + dx, z + dz))
                    })
                    .flatten()
            });
            pixels.push(colour.unwrap_or(0));
        }
    }
    pixels.iter().any(|p| *p != 0).then_some(pixels)
}

/// Bounded first-in first-out backlog without duplicates. When full, the oldest entry is
/// dropped: a newly sent chunk is more likely to still be loaded and near a player.
pub struct ExploreQueue {
    cap: usize,
    order: VecDeque<ChunkKey>,
    queued: BTreeSet<ChunkKey>,
}
impl ExploreQueue {
    pub fn new(cap: usize) -> Self {
        Self {
            cap: cap.max(1),
            order: VecDeque::new(),
            queued: BTreeSet::new(),
        }
    }
    pub fn len(&self) -> usize {
        self.order.len()
    }
    pub fn is_empty(&self) -> bool {
        self.order.is_empty()
    }
    pub fn push(&mut self, key: ChunkKey) {
        if !self.queued.insert(key.clone()) {
            return;
        }
        if self.order.len() >= self.cap {
            if let Some(old) = self.order.pop_front() {
                self.queued.remove(&old);
            }
        }
        self.order.push_back(key);
    }
    pub fn pop(&mut self) -> Option<ChunkKey> {
        let key = self.order.pop_front()?;
        self.queued.remove(&key);
        Some(key)
    }
    /// Forget a chunk that was sampled through another path.
    pub fn forget(&mut self, key: &ChunkKey) {
        if self.queued.remove(key) {
            self.order.retain(|k| k != key);
        }
    }
}
impl Default for ExploreQueue {
    fn default() -> Self {
        Self::new(BACKLOG)
    }
}

/// Chunks within `radius` of each player's chunk that `known` does not have, nearest first
/// per player, without duplicates.
pub fn missing_near(
    players: &[ChunkKey],
    radius: i32,
    known: impl Fn(&ChunkKey) -> bool,
) -> Vec<ChunkKey> {
    let mut offsets = vec![];
    for dz in -radius..=radius {
        for dx in -radius..=radius {
            offsets.push((dx, dz));
        }
    }
    offsets.sort_by_key(|(dx, dz)| dx * dx + dz * dz);
    let mut seen = BTreeSet::new();
    let mut out = vec![];
    for (world, cx, cz) in players {
        for (dx, dz) in &offsets {
            let key = (world.clone(), cx + dx, cz + dz);
            if !known(&key) && seen.insert(key.clone()) {
                out.push(key);
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    fn key(x: i32, z: i32) -> ChunkKey {
        ("w".into(), x, z)
    }
    /// Chunks the host sends when a player's chunk becomes (cx, 0) at the given view distance.
    fn sent(cx: i32, view: i32, first: bool) -> Vec<ChunkKey> {
        let mut v = vec![];
        for z in -view..=view {
            for x in cx - view..=cx + view {
                if first || x == cx + view {
                    v.push(key(x, z));
                }
            }
        }
        v
    }
    fn visible(tiles: &BTreeSet<ChunkKey>, cx: i32) -> usize {
        missing_near(&[key(cx, 0)], VIEW_RADIUS, |k| tiles.contains(k)).len()
    }

    #[test]
    fn panned_centres_are_parsed_strictly_and_map_to_tiles() {
        assert_eq!(parse_center("120 -340"), Some((120, -340)));
        assert_eq!(parse_center("  7   8 "), Some((7, 8)));
        for bad in ["", "5", "a b", "1 2 3", "1.5 2", "30000001 0", "0 -30000001"] {
            assert_eq!(parse_center(bad), None, "{bad}");
        }
        assert_eq!(center_tile(1, 0, 0), (0, 0));
        assert_eq!(center_tile(1, -1, 16), (-1, 1));
        assert_eq!(center_tile(4, 63, 64), (0, 1));
        assert_eq!(center_tile(16, -1, -257), (-1, -2));
    }
    #[test]
    fn zoom_one_is_the_chunk_tile_and_unknown_requests_fall_back() {
        let tile: Vec<u8> = (1..=16).collect();
        let got = zoom_tile(1, -3, 7, |x, z| (x == -3 && z == 7).then_some(&tile[..]));
        assert_eq!(got, Some(tile.clone()));
        assert_eq!(zoom_tile(1, 0, 0, |_, _| None), None);
        assert_eq!(
            [valid_zoom(0), valid_zoom(3), valid_zoom(4), valid_zoom(32), valid_zoom(64), valid_zoom(128)],
            [1, 1, 4, 32, 64, 1]
        );
    }

    #[test]
    fn zoomed_tile_samples_the_chunk_under_each_pixel_centre() {
        // Chunk (cx, cz) is filled with one colour identifying it; chunk (-1, -1) is unmapped.
        let colour = |x: i32, z: i32| ((x + 8) * 16 + (z + 8) + 1) as u8;
        let tiles: std::collections::BTreeMap<(i32, i32), Vec<u8>> = (-8..8)
            .flat_map(|x| (-8..8).map(move |z| (x, z)))
            .filter(|k| *k != (-1, -1))
            .map(|(x, z)| ((x, z), vec![colour(x, z); 16]))
            .collect();
        let get = |x, z| tiles.get(&(x, z)).map(|v| &v[..]);
        // Zoom 4: tile (0, 0) spans chunks 0..4, one pixel per chunk.
        let t = zoom_tile(4, 0, 0, get).unwrap();
        assert_eq!(t[0], colour(0, 0));
        assert_eq!(t[3], colour(3, 0));
        assert_eq!(t[12], colour(0, 3));
        // Negative side: tile (-1, -1) spans chunks -4..0; its last pixel is the unmapped chunk.
        let t = zoom_tile(4, -1, -1, get).unwrap();
        assert_eq!(t[0], colour(-4, -4));
        assert_eq!(t[15], 0);
        // Zoom 2: two pixels per chunk along each axis.
        let t = zoom_tile(2, 1, -1, get).unwrap();
        assert_eq!([t[0], t[1], t[2], t[3]], [colour(2, -2), colour(2, -2), colour(3, -2), colour(3, -2)]);
        assert_eq!(zoom_tile(16, 5, 5, get), None, "outside mapped terrain");
    }

    #[test]
    fn far_zoom_shows_a_thin_explored_trail() {
        // One mapped chunk in an otherwise blank world, not under any pixel centre at zoom 64.
        let tile = vec![6u8; 16];
        let get = |x: i32, z: i32| (x == 4 && z == 4).then_some(&tile[..]);
        // Zoom 64: a pixel is 256 blocks (16 chunks); pixel 0 of tile (0, 0) is centred on
        // chunk (8, 8) and its first quarter on chunk (4, 4).
        let t = zoom_tile(64, 0, 0, get).expect("the trail is visible");
        assert_eq!(t[0], 6);
        assert!(t[1..].iter().all(|p| *p == 0));
        // Close views are unchanged: no neighbouring chunk bleeds into an unmapped one.
        assert_eq!(zoom_tile(4, 0, 0, |x, z| (x == 1 && z == 0).then_some(&tile[..])).unwrap()[0], 0);
    }
    #[test]
    fn queue_dedupes_and_drops_oldest_when_full() {
        let mut q = ExploreQueue::new(2);
        q.push(key(0, 0));
        q.push(key(0, 0));
        assert_eq!(q.len(), 1);
        q.push(key(1, 0));
        q.push(key(2, 0));
        assert_eq!(q.pop(), Some(key(1, 0)));
        q.push(key(0, 0));
        q.forget(&key(2, 0));
        assert_eq!(q.pop(), Some(key(0, 0)));
        assert!(q.is_empty() && q.pop().is_none());
    }

    #[test]
    fn missing_near_is_nearest_first_and_skips_known() {
        let all = missing_near(&[key(10, -3)], VIEW_RADIUS, |_| false);
        assert_eq!(all.len(), 81);
        assert_eq!(all[0], key(10, -3));
        assert!(all[1..5].iter().all(|k| (k.1 - 10).abs() + (k.2 + 3).abs() == 1));
        let some = missing_near(&[key(10, -3), key(11, -3)], 1, |k| *k == key(10, -3));
        assert_eq!(some.len(), 11);
        assert!(!some.contains(&key(10, -3)));
    }

    /// The staging defect: one chunk per second from a 256-entry queue that rejected new
    /// chunks when full. After the join burst, a walking player's surroundings never arrived.
    #[test]
    fn previous_policy_left_walked_terrain_blank() {
        let (mut order, mut queued) = (VecDeque::new(), BTreeSet::new());
        let mut tiles = BTreeSet::new();
        let push = |order: &mut VecDeque<ChunkKey>, queued: &mut BTreeSet<ChunkKey>, k: ChunkKey| {
            if order.len() < 256 && queued.insert(k.clone()) {
                order.push_back(k)
            }
        };
        for k in sent(0, 10, true) {
            push(&mut order, &mut queued, k);
        }
        // Walk east one chunk every three seconds (sprinting) for a minute.
        for second in 1..=60 {
            if second % 3 == 0 {
                for k in sent(second / 3, 10, false) {
                    push(&mut order, &mut queued, k);
                }
            }
            if let Some(k) = order.pop_front() {
                queued.remove(&k);
                tiles.insert(k);
            }
        }
        assert_eq!(visible(&tiles, 20), 81, "nothing around the player was mapped");
    }

    #[test]
    fn walked_terrain_is_mapped_within_seconds() {
        let mut q = ExploreQueue::default();
        let mut tiles = BTreeSet::new();
        let run = |q: &mut ExploreQueue, tiles: &mut BTreeSet<ChunkKey>, cx: i32| {
            let mut left = BUDGET;
            for k in missing_near(&[key(cx, 0)], VIEW_RADIUS, |k| tiles.contains(k)) {
                if left == 0 {
                    break;
                }
                q.forget(&k);
                tiles.insert(k);
                left -= 1;
            }
            while left > 0 {
                let Some(k) = q.pop() else { break };
                tiles.insert(k);
                left -= 1;
            }
        };
        for k in sent(0, 10, true) {
            q.push(k);
        }
        // Four scheduler runs per second; the area around a new player fills in three seconds.
        for _ in 0..12 {
            run(&mut q, &mut tiles, 0);
        }
        assert_eq!(visible(&tiles, 0), 0);
        for second in 1..=60 {
            if second % 3 == 0 {
                for k in sent(second / 3, 10, false) {
                    q.push(k);
                }
            }
            for _ in 0..4 {
                run(&mut q, &mut tiles, second / 3);
            }
            assert_eq!(visible(&tiles, second / 3), 0, "second {second}");
        }
        assert!(q.is_empty(), "backlog also drained");
        assert!(tiles.contains(&key(30, 10)), "distant sent terrain mapped too");
    }

    #[test]
    fn every_player_is_served_even_when_one_has_unloadable_chunks() {
        // The caller only spends budget on chunks it could sample; candidates cover all players.
        let near = missing_near(&[key(0, 0), key(100, 0)], VIEW_RADIUS, |_| false);
        assert_eq!(near.len(), 162);
        assert!(near.contains(&key(100, 0)));
    }
}

/// Lossless explored-coverage overview at wide zoom: every known chunk contributes,
/// regardless of which player explored it. Work is bounded by the stored records,
/// not the (potentially enormous) area of an empty view. Input belongs to one world.
pub fn shared_overview<'a>(zoom: u32, cx: i32, cz: i32,
    chunks: impl IntoIterator<Item=(i32,i32,&'a [u8])>)
    -> std::collections::BTreeMap<(i32,i32),Vec<u8>> {
    let mut out = std::collections::BTreeMap::new();
    let span = valid_zoom(zoom) as i32;
    assert!(span >= 8, "overview requires wide zoom");
    for (x,z,pixels) in chunks {
        let (tx,tz)=(x.div_euclid(span),z.div_euclid(span));
        if (tx-cx).abs()>VIEW_RADIUS || (tz-cz).abs()>VIEW_RADIUS { continue; }
        let Some(colour)=pixels.get(10).copied().filter(|v|*v!=0)
            .or_else(||pixels.iter().copied().find(|v|*v!=0)) else { continue; };
        let index=(z.rem_euclid(span)*4/span*4+x.rem_euclid(span)*4/span) as usize;
        let tile=out.entry((tx,tz)).or_insert_with(||vec![0;16]);
        if tile[index]==0 { tile[index]=colour; }
    }
    out
}
#[cfg(test)] mod shared_overview_tests {
    use super::*;
    #[test] fn all_thin_trails_survive_wide_zoom_and_share_one_view() {
        let colour=vec![6;16];
        // Every possible known chunk, including those missed by old centre/quarter sampling.
        for zoom in [8,16,32,64] {
            for x in 0..zoom as i32 { for z in 0..zoom as i32 {
                let map=shared_overview(zoom,0,0,[(x,z,&colour[..])]);
                assert!(map[&(0,0)].iter().any(|v|*v==6));
            }}
        }
        let map=shared_overview(64,0,0,[(-81,67,&colour[..]),(-52,-42,&colour[..])]);
        assert_eq!(map.len(),2,"both players' separated contributions are included");
        assert_eq!(map,shared_overview(64,0,0,[(-52,-42,&colour[..]),(-81,67,&colour[..])]),"separate contributions do not depend on which player is processed first");
    }
    #[test] fn respects_negative_coordinates_bounds_and_unknown_pixels() {
        let c=vec![42;16];let blank=vec![0;16];
        let map=shared_overview(8,0,0,[(-1,-1,&c[..]),(1000,1000,&c[..]),(0,0,&blank[..])]);
        assert_eq!(map.len(),1);assert_eq!(map[&(-1,-1)][15],42);
    }
}
