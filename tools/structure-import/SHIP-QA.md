# Production ship test — 2026-11-13

Owner explicitly requested: "can you test it next to me in prod". This authorized
this ship placement, not a server binary change or deployment of the native patch.

- Original generated `ship.schem`, dimensions 9×13×22, 412 non-air blocks.
- SHA256: `73196e2eed2a6baa8c3de5ffb347a996c767d43e9ba977968952f66230f91c5b`.
- Production origin: **X=-1280, Y=63, Z=1052**; center approximately X=-1276, Z=1063.
- Checked all **198/198 water cells** at Y62 before placement.
- Checked all **2,574/2,574 bounding-volume cells** were loaded air before writing.
- Used explicit production target, loaded-only surface mode, default-state blocks,
  `setblock ... keep`, throttling and per-command success acknowledgements.
- **412/412** placements acknowledged, then **412/412** exact block predicates
  passed. Deliberately incorrect diamond-block control failed as expected.
- **198/198** underlying water predicates still passed after placement.
- `save-all flush` confirmation observed. No production restart was performed.
- Pre-build managed quick backup completed and verified, identifier
  `quick-20261113T034019Z`; archive/private data are not committed.
- No production binary/plugin/config replacement, new permissions, teleport or
  force-load changes were made. The static ship does not move as a boat entity.
- Ten local parser/geometry/CLI-boundary checks pass. Tests include negative
  coordinates, loaded-only requirement and rejection of mixed production targets.
- Client visual inspection remains with the owner; no screenshot/rendering proof,
  restart durability, multiplayer stability or Bedrock proof is claimed.

See [sanitized machine-readable evidence](evidence/production-ship.json).
The earlier gatehouse/force-load tests remain separate historical evidence in
[QA.md](QA.md). The village is still unplaced and the native binary switch is still
pending approval; this production test did not silently perform either task.
