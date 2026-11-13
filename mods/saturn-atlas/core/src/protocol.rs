//! Minecraft UTF payload framing shared by Atlas snapshots and feedback.
pub const MAX_BYTES: usize = 32000;
pub fn encode(json: &str) -> Result<Vec<u8>, &'static str> {
    if json.len() > MAX_BYTES {
        return Err("Map response exceeds protocol size limit");
    }
    let mut n = json.len() as u32;
    let mut bytes = Vec::with_capacity(json.len() + 3);
    loop {
        let b = (n & 127) as u8;
        n >>= 7;
        bytes.push(if n > 0 { b | 128 } else { b });
        if n == 0 {
            break;
        }
    }
    bytes.extend_from_slice(json.as_bytes());
    Ok(bytes)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn utf8_length_is_bytes_not_characters() {
        let v = encode("é").unwrap();
        assert_eq!(v, vec![2, 0xc3, 0xa9]);
    }
    #[test]
    fn bounds_checked_before_send() {
        assert!(encode(&"é".repeat(16001)).is_err());
        assert!(encode(&"x".repeat(32000)).is_ok());
    }
    #[test]
    fn multi_byte_length_prefix() {
        let v = encode(&"x".repeat(128)).unwrap();
        assert_eq!(&v[..2], &[0x80, 1]);
        assert_eq!(v.len(), 130);
    }
}

pub const CACHE_BATCH: usize = 96;
pub const MAX_CACHE_TILES: usize = 65536;
pub fn cache_begin(world: &str, token: &str, count: usize) -> Result<Vec<u8>, &'static str> {
    if count > MAX_CACHE_TILES || world.len() > 128 || token.len() > 40 {
        return Err("Invalid cache bounds");
    }
    encode(&serde_json::json!({"v":1,"cache":"begin","world":world,"token":token,"count":count,"batches":count.div_ceil(CACHE_BATCH)}).to_string())
}
pub fn cache_batch(
    world: &str,
    token: &str,
    batch: usize,
    tiles: &[(i32, i32, &[u8])],
) -> Result<Vec<u8>, &'static str> {
    if tiles.len() > CACHE_BATCH
        || tiles.iter().any(|(_, _, p)| p.len() != 16)
        || batch >= MAX_CACHE_TILES.div_ceil(CACHE_BATCH)
    {
        return Err("Invalid cache batch");
    }
    let data: Vec<_> = tiles
        .iter()
        .map(|(x, z, p)| serde_json::json!({"x":x,"z":z,"pixels":p}))
        .collect();
    encode(&serde_json::json!({"v":1,"cache":"data","world":world,"token":token,"batch":batch,"tiles":data}).to_string())
}
#[cfg(test)]
mod cache_tests {
    use super::*;
    #[test]
    fn full_batch_respects_actual_minecraft_utf_limit() {
        let pixels = [255; 16];
        let tiles = vec![(-1875000, 1875000, &pixels[..]); CACHE_BATCH];
        let bytes = cache_batch(&"w".repeat(128), &"t".repeat(40), 682, &tiles).unwrap();
        assert!(bytes.len() < MAX_BYTES);
        assert!(cache_begin("world", "1", 65536).is_ok());
        assert!(cache_begin("world", "1", 65537).is_err());
        assert!(cache_batch("world", "1", 683, &tiles).is_err());
        assert!(cache_batch("world", "1", 0, &[(0, 0, &pixels[..15])]).is_err());
    }
}

/// Server-pushed presence only: never requests or refreshes terrain.
pub fn player_roster(
    world: &str,
    viewer: &str,
    players: &[(String, String, String, f64, f64)],
) -> Result<Vec<u8>, &'static str> {
    let points:Vec<_>=players.iter().filter(|(id,_,w,x,z)|id!=viewer && w==world && x.is_finite() && z.is_finite() && x.abs()<=30_000_000.0 && z.abs()<=30_000_000.0).take(128)
        .map(|(id,name,_,x,z)|serde_json::json!({"id":id,"name":name.chars().filter(|c|!c.is_control() && *c!='§').take(40).collect::<String>(),"x":x,"z":z})).collect();
    encode(&serde_json::json!({"v":1,"roster":true,"world":world,"players":points}).to_string())
}
#[cfg(test)]
mod presence_tests {
    use super::*;
    #[test]
    fn roster_is_same_dimension_other_online_players_only() {
        let p = vec![
            ("me".into(), "Me".into(), "world".into(), 0., 0.),
            ("other".into(), "Other".into(), "world".into(), -1., 2.),
            ("nether".into(), "Nether".into(), "nether".into(), 1., 2.),
            ("invalid".into(), "Bad".into(), "world".into(), f64::NAN, 2.),
        ];
        let b = player_roster("world", "me", &p).unwrap();
        let offset = b.iter().position(|v| v & 128 == 0).unwrap() + 1;
        let j: serde_json::Value = serde_json::from_slice(&b[offset..]).unwrap();
        assert_eq!(j["players"].as_array().unwrap().len(), 1);
        assert_eq!(j["players"][0]["id"], "other");
    }
}
