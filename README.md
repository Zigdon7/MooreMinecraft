# MooreMinecraft
Saturn / Pumpkin Minecraft mods, client tools and versioned release information.

## Projects
- `mods/saturn-atlas`: Pumpkin WASM plugin + optional Fabric Java 26.3 companion. Shared Atlas, lodestone travel, M-key overlay and top-right minimap. **Staging alpha only.**
- `tools/mod-manager`: portable Windows x64 updater. Pinned public GitHub release assets, SHA-256/size/mod-ID verification, targeted profile updates, backups, rollback and interrupted-update recovery.
- `tools/structure-import`: isolated-tested schematic importer and original gatehouse fixture; 420/420 blocks verified after restart. Full-cube prototype only, not deployed to shared staging or production. [Results and limitations](tools/structure-import/QA.md).
- `server-patches/pumpkin`: attributed GPL-3.0 force-load ticket backport for the pinned Pumpkin release; playerless loading/ticking tested, restart persistence still missing.
- `releases/staging/pack.json`: versioned dependency manifest consumed by the updater (published with staging releases).

Production has not been deployed. Staging uses port **25566**, an independent Survival Overworld and online authentication.

## Testing
Atlas: `mods/saturn-atlas/tests/run-local-suite.sh` (Rust domain/protocol tests, real Minecraft payload-codec tests, client layout/state/keybinding/minimap tests, client and WASM builds).
Updater: `dotnet run --project tools/mod-manager/Tests/Saturn.Manager.Tests.csproj` (isolated fixture installs, verification failures, migration, rollback and recovery; no live downloads).
Windows build: `dotnet publish tools/mod-manager/Windows/Saturn.ModManager.csproj -c Release -r win-x64 --self-contained -o artifacts/manager`.

The Windows updater changes only managed JARs in the chosen game directory. It does not change the launcher, Minecraft accounts, worlds or unrelated mods. Install the matching Fabric Loader once using the official installer.

Build success and headless tests are not proof of graphical rendering, native Windows startup or multiplayer stability. Release notes distinguish verified checks from remaining in-game tests.
