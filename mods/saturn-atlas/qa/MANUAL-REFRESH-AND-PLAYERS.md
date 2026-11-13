# Atlas client 0.2.1 / server 0.2.3

## Contract
- Terrain is fetched only by opening the map or pressing Refresh. A two-second guard coalesces rapid explicit refreshes. Zoom, drag, Center, close and HUD ticks send no sync commands.
- One explicit request streams all stored same-dimension shared terrain in UTF-bounded batches of 96 chunks, atomically swapping the completed cache. Local LODs preserve remote shared trails; malformed/incomplete transfers retain the previous cache.
- Other online players appear as named green squares on the full map and minimap, and in Text map pages. The server pushes positions once per second, independent of terrain; it filters out self and other dimensions. Positions disappear when absent from the next roster, expire after five seconds if updates stop, and clear on disconnect/world change.
- No anti-spam setting is weakened, no new plugin capabilities, no world data migration. Existing stock-host ABI retained. The separate destination-preload build is NOT included.

## Automated evidence
Run `mods/saturn-atlas/tests/run-local-suite.sh`: Rust core tests, actual Rust-generated cache/presence packets decoded by the Minecraft client codec, Java model/navigation tests, client JAR and WASM builds.
Navigation policy tests exercise 10,000 repeated zoom/pan/Center/close/tick events without a refresh. Cache tests cover atomicity, bounds, sequence, duplicate tiles, stale tokens, world changes, timeouts and local LODs. Presence tests cover dimension/self/invalid-position exclusion, no terrain replacement, expiry and disconnect.

## Live acceptance (not claimed from headless tests)
1. Update with Mod Manager, relaunch, press M. Shared terrain should load once.
2. Zoom and drag repeatedly for at least a minute: smooth local view; no sync spam or anti-spam kick. Refresh should update explored terrain.
3. Join with two clients in the same dimension: green named player markers move on both maps within about a second. Other dimension/quit removes the marker. Text map includes players.
4. Inspect crowded names, narrow GUI, high GUI Scale and Narrator. Rendered Minecraft evidence remains pending real-client testing.
