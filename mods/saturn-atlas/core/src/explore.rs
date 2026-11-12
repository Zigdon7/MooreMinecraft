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
