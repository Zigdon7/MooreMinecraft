//! Server-authoritative domain rules shared by Atlas and Lodestones.
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
pub struct Position {
    pub world: String,
    pub x: f64,
    pub y: f64,
    pub z: f64,
}
impl Position {
    pub fn distance(&self, other: &Self) -> Option<f64> {
        if self.world != other.world
            || ![self.x, self.y, self.z, other.x, other.y, other.z]
                .iter()
                .all(|x| x.is_finite())
        {
            return None;
        }
        Some(
            ((self.x - other.x).powi(2) + (self.y - other.y).powi(2) + (self.z - other.z).powi(2))
                .sqrt(),
        )
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Anchor {
    pub id: String,
    pub name: String,
    pub owner: String,
    pub position: Position,
    pub public: bool,
    pub grants: BTreeSet<String>,
    pub enabled: bool,
}
impl Anchor {
    pub fn accessible(&self, player: &str) -> bool {
        self.enabled && (self.public || self.owner == player || self.grants.contains(player))
    }
}
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Marker {
    pub id: String,
    pub owner: String,
    pub name: String,
    pub position: Position,
}
#[derive(Clone, Debug, Serialize, Deserialize, Default)]
pub struct Atlas {
    pub anchors: BTreeMap<String, Anchor>,
    pub markers: BTreeMap<String, Marker>,
    pub explored: BTreeSet<(String, i32, i32)>,
}
impl Atlas {
    /// Name for a lodestone registered without one: "Lodestone 1", "Lodestone 2", ... skipping
    /// names already in use so two waypoints never look the same in a list.
    pub fn next_anchor_name(&self) -> String {
        (1u32..)
            .map(|n| format!("Lodestone {n}"))
            .find(|name| !self.anchors.values().any(|a| a.name == *name))
            .unwrap_or_else(|| "Lodestone".into())
    }
    /// The anchor registered on the lodestone block at these block coordinates.
    pub fn anchor_at(&self, world: &str, x: i32, y: i32, z: i32) -> Option<&Anchor> {
        self.anchors.values().find(|a| {
            a.position.world == world
                && a.position.x.floor() as i32 == x
                && a.position.y.floor() as i32 - 1 == y
                && a.position.z.floor() as i32 == z
        })
    }
    /// Markers are personal: a player sees only the ones they placed.
    pub fn markers_for<'a>(&'a self, player: &'a str) -> impl Iterator<Item = &'a Marker> {
        self.markers.values().filter(move |m| m.owner == player)
    }
    pub fn destinations<'a>(&'a self, player: &'a str) -> impl Iterator<Item = &'a Anchor> {
        self.anchors.values().filter(move |a| a.accessible(player))
    }
    pub fn remove_marker(
        &mut self,
        id: &str,
        player: &str,
        admin: bool,
    ) -> Result<(), &'static str> {
        let m = self.markers.get(id).ok_or("unknown marker")?;
        if !admin && m.owner != player {
            return Err("not marker owner");
        };
        self.markers.remove(id);
        Ok(())
    }
    pub fn record_delivered_chunk(&mut self, world: &str, x: i32, z: i32) {
        self.explored.insert((world.to_string(), x, z));
    }
}
#[derive(Clone, Debug, PartialEq)]
pub struct Quote {
    pub id: u64,
    pub player: String,
    pub origin: String,
    pub destination: String,
    pub price: u32,
    pub expires_ms: u64,
}
#[derive(Debug)]
pub struct TravelRules {
    /// Every journey costs nothing; the quote, proximity and access checks still apply.
    pub free: bool,
    pub blocks_per_rune: f64,
    pub max_fee: u32,
    pub origin_radius: f64,
    pub quote_lifetime_ms: u64,
}
/// Temporary: travel is free until lodestone spawning is fixed. Set to `false` to restore
/// distance pricing in amethyst shards; nothing else needs to change.
pub const FREE_TRAVEL: bool = true;
impl Default for TravelRules {
    fn default() -> Self {
        Self {
            free: FREE_TRAVEL,
            ..Self::paid()
        }
    }
}
impl TravelRules {
    /// Distance pricing in amethyst shards.
    pub fn paid() -> Self {
        Self {
            free: false,
            blocks_per_rune: 1000.0,
            max_fee: 64,
            origin_radius: 4.0,
            quote_lifetime_ms: 15000,
        }
    }
}
impl TravelRules {
    pub fn price(&self, from: &Position, to: &Position) -> Result<u32, &'static str> {
        if !self.blocks_per_rune.is_finite() || self.blocks_per_rune <= 0.0 || self.max_fee == 0 {
            return Err("invalid pricing config");
        }
        let d = from.distance(to).ok_or("invalid position or dimension")?;
        if self.free {
            return Ok(0);
        }
        let fee = (d / self.blocks_per_rune).ceil().max(1.0);
        if fee > self.max_fee as f64 {
            return Err("journey exceeds maximum fee");
        };
        Ok(fee as u32)
    }
    pub fn validate_quote(
        &self,
        q: &Quote,
        player: &str,
        at: &Position,
        atlas: &Atlas,
        now: u64,
        balance: u32,
    ) -> Result<(), &'static str> {
        if q.player != player {
            return Err("wrong player");
        };
        if now >= q.expires_ms {
            return Err("quote expired");
        }
        let origin = atlas.anchors.get(&q.origin).ok_or("origin removed")?;
        let dest = atlas
            .anchors
            .get(&q.destination)
            .ok_or("destination removed")?;
        if origin.id == dest.id {
            return Err("same anchor");
        }
        if !origin.accessible(player) || !dest.accessible(player) {
            return Err("access denied");
        }
        if !self.origin_radius.is_finite() || self.origin_radius <= 0.0 {
            return Err("invalid proximity config");
        }
        if origin
            .position
            .distance(at)
            .ok_or("invalid origin dimension")?
            > self.origin_radius
        {
            return Err("not at origin");
        }
        if self.price(&origin.position, &dest.position)? != q.price {
            return Err("price changed");
        }
        if balance < q.price {
            return Err("insufficient runes");
        };
        Ok(())
    }
}
#[cfg(test)]
mod anchor_tests {
    use super::*;
    fn anchor(id: &str, name: &str, x: i32, y: i32, z: i32) -> Anchor {
        Anchor {
            id: id.into(),
            name: name.into(),
            owner: "p".into(),
            // Stored as the standing spot on top of the block.
            position: Position {
                world: "w".into(),
                x: x as f64 + 0.5,
                y: y as f64 + 1.0,
                z: z as f64 + 0.5,
            },
            public: false,
            grants: BTreeSet::new(),
            enabled: true,
        }
    }
    #[test]
    fn markers_are_personal() {
        let mut atlas = Atlas::default();
        for (id, owner) in [("m1", "alice"), ("m2", "bob"), ("m3", "alice")] {
            atlas.markers.insert(
                id.into(),
                Marker {
                    id: id.into(),
                    owner: owner.into(),
                    name: id.into(),
                    position: Position { world: "w".into(), x: 0.0, y: 64.0, z: 0.0 },
                },
            );
        }
        let ids = |p: &str| atlas.markers_for(p).map(|m| m.id.clone()).collect::<Vec<_>>();
        assert_eq!(ids("alice"), ["m1", "m3"]);
        assert_eq!(ids("bob"), ["m2"]);
        assert!(ids("carol").is_empty());
    }
    #[test]
    fn default_names_skip_names_in_use() {
        let mut atlas = Atlas::default();
        assert_eq!(atlas.next_anchor_name(), "Lodestone 1");
        atlas.anchors.insert("a".into(), anchor("a", "Lodestone 1", 0, 64, 0));
        atlas.anchors.insert("b".into(), anchor("b", "Home", 5, 64, 5));
        atlas.anchors.insert("c".into(), anchor("c", "Lodestone 3", 9, 64, 9));
        assert_eq!(atlas.next_anchor_name(), "Lodestone 2");
    }
    #[test]
    fn anchor_is_found_by_its_block_including_negative_coordinates() {
        let mut atlas = Atlas::default();
        atlas.anchors.insert("a".into(), anchor("a", "A", -3, 70, -17));
        assert_eq!(atlas.anchor_at("w", -3, 70, -17).map(|a| a.id.as_str()), Some("a"));
        assert!(atlas.anchor_at("w", -3, 71, -17).is_none(), "the block above is not it");
        assert!(atlas.anchor_at("w", -2, 70, -17).is_none());
        assert!(atlas.anchor_at("other", -3, 70, -17).is_none());
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    fn pos(x: f64) -> Position {
        Position {
            world: "overworld".into(),
            x,
            y: 64.0,
            z: 0.0,
        }
    }
    fn anchor(id: &str, x: f64) -> Anchor {
        Anchor {
            id: id.into(),
            name: id.into(),
            owner: "owner".into(),
            position: pos(x),
            public: true,
            grants: BTreeSet::new(),
            enabled: true,
        }
    }
    fn setup() -> (Atlas, Quote) {
        let mut a = Atlas::default();
        a.anchors.insert("a".into(), anchor("a", 0.0));
        a.anchors.insert("b".into(), anchor("b", 1500.0));
        (
            a,
            Quote {
                id: 1,
                player: "p".into(),
                origin: "a".into(),
                destination: "b".into(),
                price: 2,
                expires_ms: 100,
            },
        )
    }
    #[test]
    fn distance_price_and_dimension() {
        let r = TravelRules::paid();
        assert_eq!(r.price(&pos(0.0), &pos(1500.0)), Ok(2));
        let mut b = pos(0.0);
        b.world = "nether".into();
        assert!(r.price(&pos(0.0), &b).is_err());
        assert!(r.price(&pos(f64::NAN), &pos(0.0)).is_err());
    }
    #[test]
    fn free_travel_keeps_every_check_except_payment() {
        let (a, mut q) = setup();
        let r = TravelRules {
            free: true,
            ..TravelRules::paid()
        };
        assert_eq!(r.price(&pos(0.0), &pos(1500.0)), Ok(0));
        assert_eq!(r.price(&pos(0.0), &pos(1e7)), Ok(0), "no fee cap when free");
        let mut b = pos(0.0);
        b.world = "nether".into();
        assert!(r.price(&pos(0.0), &b).is_err());
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 64).is_err(), "paid quote is stale");
        q.price = 0;
        assert_eq!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 0), Ok(()));
        assert!(r.validate_quote(&q, "p", &pos(20.0), &a, 50, 0).is_err());
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 100, 0).is_err());
        assert!(r.validate_quote(&q, "other", &pos(0.0), &a, 50, 0).is_err());
        assert!(TravelRules::paid().validate_quote(&q, "p", &pos(0.0), &a, 50, 9).is_err());
    }
    #[test]
    fn remote_or_expired_quote_rejected() {
        let (a, q) = setup();
        let r = TravelRules::paid();
        assert_eq!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 2), Ok(()));
        assert!(r.validate_quote(&q, "p", &pos(20.0), &a, 50, 2).is_err());
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 100, 2).is_err());
    }
    #[test]
    fn access_payment_identity_rechecked() {
        let (mut a, q) = setup();
        let r = TravelRules::paid();
        assert!(r.validate_quote(&q, "other", &pos(0.0), &a, 50, 2).is_err());
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 1).is_err());
        a.anchors.get_mut("b").unwrap().public = false;
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 2).is_err());
        assert_eq!(a.destinations("p").count(), 1);
    }
    #[test]
    fn changed_price_or_anchor_rejected() {
        let (mut a, q) = setup();
        let r = TravelRules::paid();
        a.anchors.get_mut("b").unwrap().position.x = 3000.0;
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 20).is_err());
        a.anchors.remove("b");
        assert!(r.validate_quote(&q, "p", &pos(0.0), &a, 50, 20).is_err());
    }
    #[test]
    fn marker_ownership_and_persistence() {
        let (mut a, _) = setup();
        a.markers.insert(
            "m".into(),
            Marker {
                id: "m".into(),
                owner: "p".into(),
                name: "Home".into(),
                position: pos(0.0),
            },
        );
        assert!(a.remove_marker("m", "other", false).is_err());
        a.record_delivered_chunk("overworld", -1, 2);
        let b: Atlas = serde_json::from_str(&serde_json::to_string(&a).unwrap()).unwrap();
        assert!(b.explored.contains(&("overworld".into(), -1, 2)));
        assert_eq!(b.markers.len(), 1);
        assert_eq!(a.remove_marker("m", "p", false), Ok(()));
    }
}

pub mod explore;
pub mod protocol;
