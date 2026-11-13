# Plugin load-chunk backport candidate (not deployed)

Source: Pumpkin PR3721, commit 5a28128648777ec5b71c23f498e66f0c6227701a.
Base: eda6944 + tested PR4373 backport95acaca. Host patch GPL-3.0; upstream attribution and license retained. https://github.com/Pumpkin-MC/Pumpkin/pull/3721

## ABI gate — verified from actual WIT
The published SDK crate `pumpkin-plugin-api 0.2.1+26.3-26.51` imports **pumpkin:plugin@0.1.0**, NOT ABI0.2. Never infer ABI from the Rust crate version. The matching host is `pumpkin-wasm-host-v0_1` and WIT directory `v0.1`. The new method is marked async/store/trappable for `pumpkin:plugin/world@0.1.0.[method]world.load-chunk`, implemented in `HostWorldWithStore` using the legacy Access interface and pump_reentry for chunk/event I/O.

Earlier draft targeted v0.2 incorrectly and was not deployed. The optional `plugin-v0_2` feature is NOT required for this guest; that experiment was stopped and its native changes removed. Use `3721-load-chunk-v0.1.patch`. Current complete native source: **1d70914**, including protected eyeblossom mitigation. Source preflight passes; validate actual guest instantiation and runtime before promotion.

## Behavior
Acquire a full-chunk ticket, await fetch/generation, retain the independent ticket for five more seconds, then remove it. Existing get-chunk remains loaded-only, so map scans do not generate terrain. Atlas preloads at BOTH quote and confirm outside the shared mutex, then rechecks expiry, origin proximity, access, balance, real lodestone and safe footing before any payment or teleport. The vendored registry SDK is unchanged except this WIT method; stock host cannot load the new guest.

## Build and evidence
Optimized command, with documented user-owned Zig CC/AR wrappers:
`CARGO_INCREMENTAL=0 cargo build --locked -p pumpkin -j1 --release --config 'profile.release.lto=false' --config 'profile.release.codegen-units=16'`

Guest0.2.4 includes released client0.2.1 cache/player presence changes:27 Rust+57 Java tests pass, fresh-checkout CI green. Runtime uses a disposable three-dimension loopback world on25569, zero players, positive/negative marker chunks, independent lease expiry, unload/reload and console safety preflight. Never install the destructive test probe in shared/prod worlds.

Production still runs stock Pumpkin + Atlas server0.2.3/client0.2.1. Owner explicitly authorized promotion after checks (reconfirmed 'move everything to prod'); full backup and rollback pair must be retained. Real-client gameplay/particle acceptance must be reported separately from automated checks.

Verification: `cargo run --locked -p pumpkin-codegen -- wit` completed in an isolated source worktree with no generated diff. Built guest component header/package markers confirm legacy0.1.0 + load-chunk; this is a boundary marker check, not runtime linking proof. Presence is now subscribed by a compatible cache request and reset on join, preserving older-client packet compatibility. Updated 84-test guest suite passes.
