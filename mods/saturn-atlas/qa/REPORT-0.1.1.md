# Saturn Atlas 0.1.1-dev — local regression report

## Result
25 automated tests passed, 0 failures: 8 Rust domain/protocol tests + 17 Java tests. Both release artifacts built successfully. Staging smoke checks passed. Production unchanged.

## Regression coverage
- 11 client model cases: valid empty terrain vs waiting, timeout/retry, visible server errors, malformed replies, invalid coordinates/pixels, selection/quote/balance/expiry gating, cleared removed selection, duplicate/injection IDs, tile bounds, disconnect isolation and payload size bounds.
- 2 parameterized-loop layout tests: supported GUI sizes 380x200, 400x220 (screenshot-sized case), 380x240, 400x240, 480x270, 640x360, 960x540, 1280x720; no control/map/sidebar overlap or out-of-screen controls. Compact fallback at 320x180, 400x190, 360x240.
- 4 actual Minecraft client payload-codec tests: decode Rust-generated UTF-8/negative-coordinate/quote fixture, exact encode bytes, truncated payload rejection and oversized declared length rejection.
- 8 Rust tests: cost/dimensions/finite coordinates, access/identity/balance revalidation, remote/expired quote rejection, changed/deleted anchor rejection, marker ownership/persistence, UTF-8 byte lengths, frame bounds and multi-byte length prefix.

## Root cause and fix
The initial plugin registered commands with an empty permission string. Pumpkin namespaces it as `saturn-atlas:` and checks that unregistered node, so normal players could not invoke the map endpoint. Commands now use explicitly registered `saturn-atlas:command.use`, default Allow; creation/access/ownership checks remain server-side.

The UI formerly swallowed invalid replies, waited indefinitely and enabled travel without a selection/quote. The new model has explicit waiting/error/timeout/ready states; actual server command failures are returned on the custom-payload channel. Screen is opaque, status text wraps and controls are state-gated.

Chunk-send handler changed to a blocking lightweight enqueue so guest contention cannot skip the event. Surface sampler checks below heightmap air. Sample queue remains bounded.

## Staging smoke results
- Exact installed Pumpkin binary accepted 0.1.1 WASM with unchanged, owner-approved fs.read.data/fs.write.data permissions.
- Saved schema1 store with one explored tile restored.
- Startup logged registered default-allow command permission.
- Console `/atlas health`: `ATLAS_HEALTH_OK tiles=1 queued=0 pending_payments=0`.
- Console `/atlas sync` reached the plugin and correctly refused a non-player caller (not an unknown-command error).
- Local Java status ping on 25566 reported Minecraft 26.3 / protocol777 / max8.
- No ERROR lines in startup log.

## Not claimed as verified
No local graphical Minecraft client was launched. Real rendering/font appearance, an authenticated non-op player's endpoint invocation, live shared terrain sampling, actual travel/inventory mutation and crash recovery still require in-game confirmation. Headless layout/codec tests are not visual or multiplayer proof. No production deployment or stability certification.

## Reproduce
`tests/run-local-suite.sh` — Rust tests, server-generated wire fixture, Java tests/build, WASM build. XML reports under client/build/test-results/test; consolidated output under qa/local-suite-0.1.1.log.

## Artifact identities
Companion SHA256: 1877ee95a848edf9344f30135e8b1366540ebc1ca5e92bbcb5a77be40c15267e
Staged server SHA256: 1c7e7a8caa0aa32479b8be66fa8a63002b5c520a354a4be3b3f846c8d8354e6c
