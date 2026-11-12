# Atlas staging deployment — 2026-11-12
Pulled main 320d470: lodestone placement/break/rename, shared waypoints,
personal markers, map panning/Center, zoom to 9,216 blocks.
Local checks: 21 Rust + 47 Java tests pass; JAR/WASM builds pass.
Published immutable staging-atlas-320d470 with pinned manifest and SHA256SUMS.
Anonymous download/verified install/rollback passed in isolated updater profile.
Updated default staging pack and component metadata; manager remains 0.1.6.

Staging empty before clean stop. Full offline staging backup retained before WASM replacement.
No world reset, server binary replacement, production changes, or new capabilities.
Reused existing owner authorization for fs.read.data/fs.write.data.
Loaded schema1 with 4627 terrain tiles/0 anchors, startup successful on 25566;
local TCP probe passes. Survival/force_gamemode unchanged.
Real gameplay events, map dragging/rendering and multiplayer acceptance still need player testing.
