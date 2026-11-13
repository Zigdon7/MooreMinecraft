# Plugin load-chunk backport candidate (not deployed)
Adapted from upstream Pumpkin PR3721, commit 5a28128648777ec5b71c23f498e66f0c6227701a.
Target: base eda6944 + existing tested PR4373 backport (95acaca).
GPL-3.0 host patch; upstream copyright/license retained. Original author metadata:
https://github.com/Pumpkin-MC/Pumpkin/pull/3721

Original PR targeted v0.1/older layout; adaptation adds world.load-chunk to v0.2,
marks the host import asynchronous, and uses current host resource accessors.
It acquires a full-chunk ticket, awaits fetch/generation, and holds the chunk for five
more seconds. Existing get-chunk remains loaded-only; map scans never generate terrain.
Atlas calls load-chunk before destination validation at BOTH quote and confirm,
then verifies actual lodestone and safe footing before any payment/teleport.
The vendored guest SDK has only this WIT addition; compatible host required.
Do not deploy the new plugin on stock Pumpkin: its new import will not resolve.

Optimized native build and isolated runtime validation pending. Production is on stock Pumpkin plus Atlas server 0.2.3 / client 0.2.1. Owner has authorized the tested combined production promotion.

2026-11-13 owner follow-up: "push this to prod for testing once complete" explicitly
approves promoting this patched engine + guest after isolated tests and a fresh backup.
No additional capabilities, world reset or unrelated engine upgrade is authorized.

## Combined candidate (2026-11-13)
Native source commit `fd87921` (base eda6944 + PR4373 + PR3721 adaptation + exact protected eyeblossom mitigation). Source preflight passes. Optimized build: `CARGO_INCREMENTAL=0 cargo build --locked -p pumpkin -j1 --release --config 'profile.release.lto=false' --config 'profile.release.codegen-units=16'` with the documented user-owned Zig wrappers.

Atlas guest 0.2.4 is merged with the released client 0.2.1 manual-cache/player-presence changes; all 27 Rust and 57 Java tests pass. Separate host/guest is still not live until runtime probe succeeds. Candidate runtime uses disposable loopback 25569, zero players, positive/negative marker chunks, unload/reload persistence, duplicate lease expiry, and console `atlas preflight` for lodestone/footing validation without moving/debiting a player. Do not install the destructive test probe in production. Keep the original binary and current 0.2.3 WASM together for rollback; 0.2.4 will not run on stock Pumpkin.
