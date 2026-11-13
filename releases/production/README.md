# Saturn Atlas 0.2.1 — live testing release

Atlas client 0.2.1 / server 0.2.4 is installed on the existing live world (Java port **25565**).
This is a testing release, not a vanilla parity/stability certification.
Production world/player data and all three dimensions are retained; nothing from
staging's world, waypoints or explored terrain was copied into the live world.
A verified managed full backup was taken before installation.

## Install — Mod Manager 0.3.0
1. Download the manager from the **manager-v0.3.0** release. The official Minecraft Launcher and your own signed-in account are required.
2. Close Minecraft and its Launcher, run the manager, and click Install.
3. Launch **Higdon Server** in Minecraft Launcher. Fabric 0.20.1 and Minecraft 26.3 are configured automatically; the launcher downloads game/runtime/libraries on first Play.
4. Multiplayer → Add Server → **23.126.62.91:25565**. No separate Fabric installer is needed.

Production Atlas client is **0.2.1**, server **0.2.4**, independently versioned from manager **0.3.0**.

Atlas update: https://github.com/Zigdon7/MooreMinecraft/releases/tag/atlas-v0.2.3
Close Minecraft, open manager, **Check for updates → Install/Update**, then relaunch.
Shared terrain refreshes only on map open or Refresh; zoom/pan stay local. Named green
markers show online players in the same dimension, with server-pushed positions.
Open M once after joining to fill the minimap terrain cache.
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
They are NOT automatic world generation. A manually imported ship has since been verified in production; see
`tools/structure-import/SHIP-QA.md`. Village/gatehouse placement remains pending. We have NOT
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

## Manual-refresh/player-map update evidence
Source ffe6b3ad7af29e1e4571be60a0a3c1bf8fcdc6dc. 27 Rust + 57 Java tests pass locally and in the Atlas CI job; 26 updater tests pass locally. Public-release anonymous install and rollback verified. Stock-host rehearsal and live startup/health pass; persisted store unchanged (2,197 terrain records / 3 anchors). Fresh managed backup quick-20261113T044646Z, verified. Live WASM SHA256 69f9b87a24b94ef2ca66b5ae917281419923ecd57eabea81ec0bf47d8debac55. The separate chunk-preload engine change is not deployed by this patch. Real-client multiplayer/rendering acceptance is pending; please test both map markers and rapid zoom after installing the new client.

## Server0.2.4 / protected engine promotion — 2026-11-13 06:03 UTC
Release: https://github.com/Zigdon7/MooreMinecraft/releases/tag/server-v0.2.4
Native content source1d70914962e8f71315af47bc93bfaaa31a06b8d3, optimized binary SHA2561ee1ae554186a73014d67dc41685daef3664564ceaa17418578506baee6dbc62. Preserves exact protected eyeblossom mitigation, PR4373 tickets and PR3721 legacy0.1 ABI preload. SDK crate semver0.2.1 is not WIT ABI0.2; inspect actual components before selecting host features.

27 Rust+57 Java tests, CI43373512093, component ABI marker gate and clean WIT generation pass. Real zero-player native/WASM tests verify both coordinate signs, duplicate ticket expiry, unload/reload persistence, blocked/missing-stone refusals and a clean restart. All3 live lodestones subsequently passed safe preflight from unloaded chunks; no player moved or debited. Live health passes with2197 terrain records,3 anchors and0 pending payments; persisted Atlas store unchanged on startup. Existing world/structures and all dimensions retained; no probe installed live.

Full backup full-20261113T055643Z verified SHA2562966184d5adc6e4b81175a386819fc6a6fdec8b0e616dea90e09ffaada9b081a. Matching prior binary/plugin rollback retained privately. Client0.2.1 and manager0.3.0 remain current; no additional client build required. Real-client travel/map/particle acceptance remains pending owner-requested live testing. Earlier evidence above describes prior component releases.
