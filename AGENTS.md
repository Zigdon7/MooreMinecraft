# MooreMinecraft operator / development instructions

- Owner: Zigdon7. This monorepo stores all Saturn custom mods, updater source and release manifests.
- **After finishing a coherent change batch, run the relevant checks, commit and push it to this repository. Do not leave completed work only in a local checkout.** Push tested milestones throughout long work, not only at the final release. If auth or branch protection blocks a push, report it promptly and preserve the commit.
- Production Pumpkin is separate from staging. Do not deploy to production until explicitly authorized and stable. Current staging: port 25566, independent Survival Overworld, online authentication enabled.
- Never commit credentials, auth configuration, environment secrets, player data/worlds, private backups or local runtime caches.
- Pumpkin loads WASM, not Java server mods. Fabric code here is client-only. Bedrock compatibility must be tested separately.
- Plugin capability approval remains manual: relay any new requested permission and wait for owner approval. Existing owner approval covers fs.read.data and fs.write.data for staging Saturn Atlas only.
- Preserve existing data and rollback paths. Do not reset worlds, wipe player data or replace the production binary without explicit approval and backup.
- Every release must include exact target versions, artifact hashes and test evidence. Do not call headless layout tests actual in-game rendering proof.
- Public updater manifests pin exact release tags/assets, sizes and SHA-256 values. Never install arbitrary executables from a mod manifest; updater payloads are JARs only. Keep staging and production channels separate.
- Atlas tests: mods/saturn-atlas/tests/run-local-suite.sh. Updater tests: dotnet run --project tools/mod-manager/Tests/Saturn.Manager.Tests.csproj.
