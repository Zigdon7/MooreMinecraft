# Chunk-preload integration — 2026-11-13

Native source content:1d70914962e8f71315af47bc93bfaaa31a06b8d3, eda6944 base + PR4373 ticket fix + legacy ABI PR3721 adaptation + protected eyeblossom mitigation.
Frozen optimized binary SHA256:1ee1ae554186a73014d67dc41685daef3664564ceaa17418578506baee6dbc62.
Atlas0.2.4 WASM SHA256:edae35900d15378247ab27a4d94540ff72f6434b856e613f02ba9f30c52033cf.
Disposable probe SHA256:b61de2588154ff3a2b80bd7806712301a963701e73aa7f6006befeca655c3167.

## Automated evidence
- 27 Rust +57 Java tests, both builds, final guest CI43373512093 green. Component ABI marker guard confirms0.1.0 + load-chunk. WIT generation on final native source1d70914 produced no diff.
- New disposable three-dimension world, loopback25569, zero players, default stack (RUST_MIN_STACK explicitly unset). Exact frozen binary/plugin instantiated successfully.
- PRELOAD_PROBE_OK phases1 and2: positive/negative chunks initially unloaded; explicit load makes readable; marker blocks survive expiration of duplicate independent leases and chunk unload/reload.
- Atlas console preflight succeeds with was_loaded=false for both markers, landings1537.5/201/1537.5 and -1534.5/201/-1534.5.
- Fill all candidate head spaces with stone: REFUSED No clear safe arrival space. Restore air: OK. Remove lodestone: REFUSED Destination lodestone block is missing. Restore lodestone: OK. No player moved or debited.
- Atlas health:tiles0 queued0 pending_payments0; no panic/error logs in first sandbox run.

## Boundaries
These are real server/WASM runtime tests, not a real Minecraft-client gameplay/particle certification. Owner explicitly requested production live testing and approved the combined binary/plugin switch. Keep the existing world/structures and a verified full backup/rollback pair. Probe plugin is destructive fixture code and must NEVER be copied into production. Client0.2.1 and manager0.3.0 are unchanged by the server-only promotion.

## Clean restart and production result
Both fixture lodestones passed preflight again from unloaded chunks after a clean server restart with the probe disabled. Production promotion completed06:03 UTC with the identical frozen hashes. Atlas health passes;2197 terrain records/3 anchors/pending0 preserved byte-for-byte on startup. All3 actual live lodestones then passed safe preflight with was_loaded=false. No player moved/debited by these checks. Fresh full backup full-20261113T055643Z verified. Probe remained only in the disposable world. Real-client gameplay/particle acceptance is still pending.
