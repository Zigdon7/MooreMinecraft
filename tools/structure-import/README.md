# Structure import and force-load prototype

**Gatehouse: isolated-tested. Village: prepared only. Ship: owner-authorized production placement verified on 2026-11-13.**
See [QA.md](QA.md) and [sanitized evidence](evidence/) for observed results and limitations.
See [SHIP-QA.md](SHIP-QA.md) for the separate production ship run. The native
force-load patch has not been deployed to production by this work.

## What was fixed

Pumpkin `0.2.2+26.3-26.60` recorded `/forceload` chunks in the active/ticking set
without issuing the tickets that actually load them. The attributed upstream
[PR4373 backport](../../server-patches/pumpkin/README.md) adds and removes those
tickets, including duplicate-add protection and remove-all cleanup.

On an isolated server with zero players, the backport loaded fresh positive and
negative-coordinate chunks, ticked an item entity, and released chunks after
removal. Forced-chunk registrations **still do not persist across restart**.

The default native build was killed with SIGKILL in generated `pumpkin-data`.
Disabling incremental compilation and reducing that crate's codegen units to 16
allowed a one-job debug build to finish in the 3 GiB container. Chunk generation
then overflowed the default debug worker stack; `RUST_MIN_STACK=16777216` allowed
the isolated runtime probes to pass. These are build/test workarounds, not claims
of production performance or general vanilla parity.

## Importer

### Ship example (production placement verified)

`python3 make_ship.py` generates the original static `ship.schem`: a tapered
wooden hull, deck, stern cabin, mast, white sails and red pennant, 9×13×22 / 412
blocks. This is a decorative build, not a movable boat entity.

On 2026-11-13 the owner explicitly requested a test next to them in production.
The ship was placed at origin **(-1280,63,1052)**, above water Y62. A managed,
verified pre-build backup was taken. The 198-cell water footprint and all 2,574
loaded airspace cells were checked before any placement. `keep` was used for all
412 placements; 412/412 exact block checks and 198/198 underlying water checks
passed afterward, then `save-all flush` was confirmed. No binary/plugin, player
teleport, or force-load registration changes were made for the production build.
No client screenshot or restart test is claimed for this ship run.

Production is opt-in: `--apply-production` requires `--loaded-only`, rejects
staging/scratch combinations, and must only be used for an owner-authorized live
placement. `--surface-site --loaded-only` permits surveyed lower-altitude plots;
it preflights the **entire** loaded, empty bounding volume before writing. It does
not survey water automatically: the operator must check the actual water surface
and place the hull immediately above it, without replacing water or existing builds.
Negative world coordinates are supported. An unloaded chunk reading as air is
not accepted: loaded and air conditions are checked together.

`verify_structure.py FILE --target production --origin X Y Z --water-y Y` is a
read-only verifier for any supported fixture and the water footprint, with an
explicit target choice; its output defaults to ignored `runtime/verification.json`.

### Small village example (prepared, not yet placed)

`python3 make_village.py` generates the original `village.schem`: three cottages,
open doorways, paths/plaza and a decorative covered well, 24×10×24 / 1,259 blocks.
It uses only base block names, with no NPCs or block-entity inventories. The well
uses blue glass rather than flowing water. A test checks decode compatibility and
walkable door openings. No live village placement has been claimed.

Requested destination is near the shared testing spawn at X=474, Z=219. As checked
on 2026-11-12, staging still runs the original binary and both spawn and a nearby
candidate plot are unloaded with zero players. **An air predicate on an unloaded
chunk is not an empty-site survey.** Wait for a player to load the area, or obtain
approval for the already-tested staging-only binary switch, then survey the site,
back up, import without replacing blocks and verify the result. Do not put this
in production or force a binary switch to satisfy the example request.

Original `gatehouse.schem`: 13×9×7, 420 non-air full-cube blocks. The importer
acknowledged 420 placements, saved the world, and all **420/420 block predicates
passed after a clean restart**, with a deliberately incorrect negative control.

Supported parsing: Sponge v2/v3 `.schem` and Java structure `.nbt`. Air is skipped;
entity/block-entity data is rejected. Prototype limits: 10,000 non-air blocks,
128 blocks per axis, high-altitude by default (explicit surveyed surface mode as
above). Origin means the normalized minimum
corner; original schematic offsets are not applied.

Current host `/setblock` does not parse state properties, so apply rejects them
instead of silently losing stair/door orientation. Rotation, terrain adaptation,
containers, entities, undo and transactional/resumable placement are not delivered.

## Local checks (no server changes)

From this directory:

```bash
python3 -m unittest -v test_import
python3 import_structure.py gatehouse.schem --origin 96 200 96
```

Python uses only its standard library. The dry run writes ignored `last-plan.json`.
Tests regenerate the deterministic original fixture. Ten parser/validation checks
pass; those tests alone are not live placement or graphical-rendering proof.

## Operator-only live commands

These commands mutate a world. They are documentation, not an automatic deployment.
Confirm the target and take a backup before live work. The isolated test server was
stopped cleanly after verification; it is not currently a public viewing endpoint.

- Default target: existing shared staging `tmux -L saturn-stage`, session `atlas`,
  port 25566. `--apply-staging` enables writes; `--test-scratch` changes the target
  only to the isolated `structure-test:prototype` console, loopback port 25567.
- Production requires the separate explicit flag and loaded-only preflight described above; the default target is not production.
- `--loaded-only` never adds/removes force-load registrations, avoiding changes to other operators' loaders.
- Placement uses `keep`, throttles commands, and requires success acknowledgement.
  Failure leaves already placed blocks intact; there is no automatic rollback.
- `probe_loader.py patched` exercises only the isolated console; it creates a
  marker/item, changes force-load registrations and removes the test item.
- `verify_build.py --label after-restart` explicitly adds a force-load for the
  gatehouse plot and checks every expected block plus a negative control.
- Runtime JSON/text outputs are ignored. The checked-in evidence is a historical,
  sanitized snapshot, not a substitute for deployment verification.

Example isolated import, only after starting the approved scratch server:

```bash
python3 import_structure.py gatehouse.schem --origin 96 200 96 --apply-staging --test-scratch
python3 verify_build.py --label after-restart
```

`start-staging-proposed.sh` is an **unexecuted proposal** for this host: it preserves
the existing staging directory and sets the debug stack environment. The binary
at `/home/mc/structure-prototype/bin/pumpkin-forceload-26.60` is not committed.
Owner approval for the existing-server binary switch is still pending. Any plugin
permission prompt remains manual. Do not modify `/opt/pumpkin/pumpkin` to deploy
this prototype: production also uses it.

No binaries, world/player data, auth configuration, backups or raw runtime logs
are included here. New tool code and the original gatehouse, village and ship use the repository's
MIT license; the separate upstream Pumpkin patch retains GPL-3.0 and attribution.
