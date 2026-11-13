# Isolated chunk-preload regression probe
DO NOT install on production or shared player worlds: this deliberately writes two high-altitude lodestones.
Install only in a new disposable world, zero players, loopback-only port, matching patched legacy @0.1.0 host.
No filesystem capabilities requested. It logs PRELOAD_PROBE_OK phases 1 and 2 or panics on failure.

Phase1 (20 ticks): assert both distant positive/negative-coordinate chunks unloaded,
load through PR3721 API, write and read markers, take a second independent temporary lease.
Phase2 (800 ticks): assert chunks unloaded after both holds expire; reload and read persisted markers.
This verifies missing-chunk loading, hold cleanup, duplicate lease balancing and data retention,
without moving a player into an unverified chunk. It does not prove actual multiplayer teleport safety.

Build with the local CC shim on this host:
PATH=/home/mc/.local/share/saturn-tools/bin:/home/mc/.cargo/bin:$PATH cargo build --release --manifest-path tools/chunk-preload-probe/Cargo.toml --target wasm32-wasip2 -j1
