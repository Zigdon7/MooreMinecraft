# Staging 0.1.2 verification

GitHub CI: https://github.com/Zigdon7/MooreMinecraft/actions/runs/42900973560
Tested source: fc780856a6bac3ccdb489705893860648b2a64ee.

- 8 Rust domain/protocol tests passed.
- 26 Java tests passed: real Minecraft payload-codec fixtures, reply/control states, layout bounds, actual M-key mapping/rebinding, larger aspect-correct viewport, top-right minimap placement and bounded polling.
- 13 updater transaction/recovery tests passed on Windows, including checksum and mod-ID mismatch rejection, migration by mod ID, unrelated-file preservation, traversal rejection, interrupted-update recovery and refusal to overwrite local edits.
- Portable EXE built on Windows and created a real native window. Captured screenshot inspected; see tools/mod-manager/docs/windows-smoke.png.
- Release JAR/WASM/EXE are the exact successful CI artifact downloads, not untested local replacements.

Remaining gates: public GitHub release download/install/rollback probe; final staging WASM load/status check; real in-game M/minimap rendering and multiplayer/payment tests. No production deployment or stable-release claim.

## Post-publication probe
The updater core fetched the real public GitHub channel manifest with no credentials, resolved the pinned public release assets, downloaded/verified both JARs, installed them into an isolated test profile and rolled back to that profile's prior empty mod set. Passed. See public-release-e2e-0.1.2.log.
