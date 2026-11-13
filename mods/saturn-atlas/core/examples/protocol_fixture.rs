use std::path::PathBuf;
fn main() {
    let out = PathBuf::from(std::env::args().nth(1).expect("fixture destination"));
    std::fs::create_dir_all(&out).unwrap();
    let id = "11111111-1111-4111-8111-111111111111";
    let fixture=serde_json::json!({"v":1,"world":"world","x":-0.5,"z":-16.5,"zoom":1,"max_zoom":16,"balance":3,"tiles":[{"world":"world","x":-1,"z":-2,"pixels":[6,6,10,10,42,42,50,50,46,46,34,34,18,18,6,6]}],"anchors":[{"id":id,"name":"Café portal","x":2.5,"z":-15.5}],"markers":[],"quote":{"destination":id,"price":2,"remaining_ms":15000}}).to_string();
    std::fs::write(
        out.join("cache-begin.bin"),
        saturn_atlas_core::protocol::cache_begin("world", "1", 2).unwrap(),
    )
    .unwrap();
    std::fs::write(
        out.join("cache-data.bin"),
        saturn_atlas_core::protocol::cache_batch(
            "world",
            "1",
            0,
            &[(-1, -2, &[6; 16]), (500, -500, &[10; 16])],
        )
        .unwrap(),
    )
    .unwrap();
    std::fs::write(
        out.join("players.bin"),
        saturn_atlas_core::protocol::player_roster(
            "world",
            "self",
            &[(id.into(), "Café player".into(), "world".into(), -1.0, -2.0)],
        )
        .unwrap(),
    )
    .unwrap();
    std::fs::write(out.join("atlas.json"), &fixture).unwrap();
    std::fs::write(
        out.join("atlas.bin"),
        saturn_atlas_core::protocol::encode(&fixture).unwrap(),
    )
    .unwrap();
}
