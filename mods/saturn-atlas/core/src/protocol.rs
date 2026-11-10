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
