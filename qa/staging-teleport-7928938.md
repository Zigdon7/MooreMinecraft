# Staging deployment 2026-11-12

Source 7928938: faster nearby terrain sampling and temporary free travel.
Local checks: 14 Rust + 37 Java tests passed, client/WASM builds passed.
Published immutable prerelease teleport-preview-7928938 with client, WASM,
SHA256SUMS and pinned pack.json. Anonymous public download/install/rollback passed.
Default staging pack.json and component metadata updated to the matching artifacts.

Staging cleanly stopped after 30-second player warning; complete offline backup
retained in operator backup directory. Replaced only staging plugin; no server binary,
world reset or production changes. Existing fs.read.data/fs.write.data approval reused.
Restored schema 1, 2 terrain tiles, 0 anchors; no new permissions.
Startup succeeded on 25566; local Java status ping reports 26.3/protocol777.
Console: ATLAS_HEALTH_OK tiles=2 queued=0 pending_payments=0.
Survival and force_gamemode remain enabled. In-game travel/map acceptance pending.
Upstream package versions are unchanged (server 0.1.2-dev/client 0.1.3-dev);
release tag/source SHA and hashes identify these new bytes without mutating old releases.
