# Saturn 0.2.0 — live testing release

Atlas 0.2.0 is installed on the existing live world (Java port **25565**).
This is a testing release, not a vanilla parity/stability certification.
Production world/player data and all three dimensions are retained; nothing from
staging's world, waypoints or explored terrain was copied into the live world.
A verified managed full backup was taken before installation.

## Install — Mod Manager 0.3.0
1. Download the manager from the **manager-v0.3.0** release. The official Minecraft Launcher and your own signed-in account are required.
2. Close Minecraft and its Launcher, run the manager, and click Install.
3. Launch **Higdon Server** in Minecraft Launcher. Fabric 0.20.1 and Minecraft 26.3 are configured automatically; the launcher downloads game/runtime/libraries on first Play.
4. Join the usual host on **25565**. No separate Fabric installer is needed.

Production Atlas remains **0.2.0**, independently versioned from the manager.
Staging is a separate manager channel, **Higdon Server (Staging)** profile, game folder and port **25566**.
No server/world/plugin deployment is part of the manager release.
Manager self-updates now use the fixed production/components.json feed; legacy managers receive 0.3.0 through their existing staging feed.

## Features
M opens Atlas; drag to pan, Center returns to you; zoom to 9,216 blocks.
Top-right minimap and HUD settings. Placed lodestones register shared waypoints;
right-click your lodestone to rename. Markers are personal. Travel is temporarily
free, but origin proximity, access, expiry and safe-landing checks still apply.
Dimension-safe IDs isolate map and travel data. Bedrock client overlay is not delivered.

## Custom structures
`custom-structures-source.zip` carries the original gatehouse/village schematics,
validated manual importer, tests and attributed force-load backport source.
They are NOT automatic world generation. No structure has been placed on the live
world yet: destination and loaded, empty-site survey remain pending. We have NOT
replaced Pumpkin with the experimental debug force-load binary.

## Evidence / boundaries
Source: b945b0c5ac765397e184e987df3c3b9e207a1643
CI: https://github.com/Zigdon7/MooreMinecraft/actions/runs/43353413474
22 Rust, 47 Java and 18 updater tests pass; 7 structure parser tests pass locally.
Native Windows default/dark smoke and UI Automation checks pass. Evidence ZIP is
actual EXE rendering of offline fixtures, not proof of a real Windows installation.
Exact CI WASM was also rehearsed in a disposable three-dimension world before live
install. Production startup and Atlas health passed. Real client/multiplayer,
Narrator, live travel/structure rendering and payment-recovery acceptance remain open.

SHA256SUMS pins release assets; server WASM and structure source are operator assets,
not files players should place in their Minecraft mods directory.
