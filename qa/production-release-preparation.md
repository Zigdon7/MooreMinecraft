# Live-testing release preparation 0.2.0
Owner requested production rollout 2026-11-13. Production is not yet deployed.

- Added dimension-aware world identity (host level ID is shared across dimensions).
  Existing Overworld ID retained; Nether/End get dimension suffixes. Fail-closed duplicate check retained.
- Isolated three-dimension startup rehearsal on loopback 25568 loaded Atlas successfully.
- Versions: Atlas client/server 0.2.0, manager 0.2.0. Default manager pack is production channel;
  existing saved staging URL must be reset via Settings -> Use default source.
- Self-update feed remains fixed to staging/components.json per repository authorization;
  do not change its pointer until release assets are uploaded and rollout is ready.
- Managed full production backup completed 2026-11-13T02:35:34Z, verified true.
- Structure importer/fixtures/force-load patch carried as source assets, not world data.
  Existing prototype supports full cubes, not stateful blocks/entities or automatic worldgen.

Blocking live actions: explicit production fs.read.data/fs.write.data approval (prior grant
is staging-only); structure destination coordinates/loaded-site survey. No production
binary swap planned: use loaded chunks rather than the debug forceload prototype.
No dimensions disabled, no production world reset/import, no player data copied from staging.
