# Isolated runtime result — 2026-11-11

## Tested artifact

- Pumpkin tag `0.2.2+26.3-26.60`, commit `eda69440e6369c5f8a05c3f69fc809c40dab93a6`.
- Backport of upstream [PR4373](https://github.com/Pumpkin-MC/Pumpkin/pull/4373), author wsuites; no claim that it is merged upstream.
- Binary: `/home/mc/structure-prototype/bin/pumpkin-forceload-26.60` (local only, not committed), unoptimized native debug build.
- SHA256: `a606011e3e4318f405684c50710c0a1ecb9eb7776b01345dbff9aa752e981947`.
- Build passed with one job, incremental disabled, pumpkin-data codegen units 16.
- Runtime requires `RUST_MIN_STACK=16777216` for this debug build: the default worker stack overflowed during generation. Do not deploy without that environment setting.
- Test config: independent world, loopback Java 25567, online authentication retained, plugins disabled, zero players.

## Observed results

- Original binary: `/forceload add 96 96` acknowledged but loaded predicate stayed false.
- Patched binary: fresh positive and negative-coordinate chunks loaded without players.
- Item entity Age advanced from 6 to 73 ticks without any players.
- Duplicate add followed by one remove allowed the chunk to unload: no leaked duplicate ticket in this scenario.
- Remove-all unloaded the negative-coordinate chunk.
- Positive block predicate passed; deliberately wrong diamond-block predicate failed.
- Importer acknowledged all 420 gatehouse placements, issued save-all, and removed its temporary force-load.
- Clean server stop/restart, explicit re-add of loader, then **420/420 exact block predicates passed, zero mismatches**. Negative control failed as expected. Evidence: `evidence/verification-after-restart.json`.
- Six parser/validation tests passed. Changed Rust file passes rustfmt check and git diff whitespace check. Full Rust suite/clippy and client-rendering/performance tests were not run.
- The inspected post-restart runtime log contained no ERROR/panic/overflow entries. The isolated server was then stopped cleanly.

## Known limits

- Forced-chunk registrations **do not persist across restart**. Confirmed in `evidence/restart.json`; loader had to be explicitly re-added. This patch is not a complete restart-safe chunk-loader system.
- Current host setblock parser rejects state arguments. Prototype applies full-cube-only schematics; states, block entities, entities, rotations and terrain adaptation are not delivered.
- No real client visual inspection, no multiplayer/non-op or Bedrock verification, no production stability/performance claim.
- No new plugins installed or permissions granted. Existing production 25565 and shared staging 25566 remain unchanged.

## Proposed shared staging switch — NOT EXECUTED

Owner approval required by workspace AGENTS.md before switching an existing server binary.
Prepared launch: `start-staging-proposed.sh`, preserving shared staging config/world and setting the larger debug stack.
Before switching: check players, announce if needed, clean save/stop and take a fresh staging backup.
Rollback launch remains `/opt/pumpkin/pumpkin` from the existing staging directory.
Never approve existing plugin permission prompts automatically; relay exact requests.
Once shared staging is running, import into a verified empty high-altitude plot and verify there independently. Scratch evidence is not deployment proof for shared staging.
