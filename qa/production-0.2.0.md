# Production live-testing rollout — 2026-11-13
Source/build b945b0c5ac765397e184e987df3c3b9e207a1643. Release v0.2.0.
22 Rust, 47 Java, 18 updater tests passed; 7 structure parser tests passed locally.
Windows native default/dark suites passed in CI 43353413474, including page/keyboard/UIA cases.
Inspected native dark checked screenshot. Exact CI artifacts used, not local substitutes.
Exact CI WASM passed disposable three-dimension startup before deployment.

Owner approved production installation following exact fs.read.data/fs.write.data request.
Managed full backup full-20261113T023534Z.tar.gz verified true before clean service stop.
No players online at stop. Installed only Atlas WASM, retained original Pumpkin binary,
configs/dimensions, live world and all player data. No staging terrain/anchor data copied.
Production service active; Atlas 0.2.0 loaded with all three distinct dimension identities.
Console ATLAS_HEALTH_OK tiles=0 queued=0 pending_payments=0.
Java status ping 25565: 26.3/protocol777, online0; no ERROR/panic/trap in startup log.
Production WASM SHA256 776bbaadcafe6d2f25ce6f4df888fb7b498b28c1a4f3279dcfff3b20e0f685a8.

Published production pack/components/guide; staging pack remains unchanged. Its fixed
self-update feed manager entry now points to the uploaded 0.2.0 EXE. Existing saved
staging manifest URL must be reset in manager Settings -> Use default source.
Structure tools, gatehouse/village fixtures and attributed loader patch are in release source ZIP.
No live structure placed: approved coordinates/site survey still pending. No debug binary switch.
Gameplay/rendering/multiplayer/Narrator acceptance remains open; testing release, not stable parity.
