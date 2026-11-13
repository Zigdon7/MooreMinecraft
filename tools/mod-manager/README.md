# Saturn Mod Manager 0.3.0 (Windows x64)
One portable EXE configures Fabric, a named official Minecraft Launcher profile and the matching Atlas mods.

1. Have the official Minecraft Launcher installed and sign in with your own Minecraft account.
2. Close Minecraft **and its Launcher**, download and run **Saturn-Mod-Manager.exe**.
3. Click **Install**. No separate Fabric installer or Java/.NET installation is needed.
4. Open Minecraft Launcher, select **Higdon Server**, and click **Play**. The launcher downloads the Minecraft runtime, game and Fabric libraries on first Play.
5. Join the usual server host on **25565**. **M** opens Atlas.

For testing: **Settings → Atlas channel → Staging**, then Install. Launch **Higdon Server (Staging)** and join **25566**.
Fresh setups use separate `%APPDATA%\.minecraft\higdon-server` and `higdon-server-staging` game folders.
The manager preserves existing launcher profiles, accounts, settings and worlds. Existing manager settings
are retained; **Use default source** restores the production channel/default isolated folder.
Unsupported/custom launchers are not configured automatically.

## Independent versions and channels
- Manager: **0.3.0** (production release, not an Atlas version).
- Production Atlas client/server: **0.2.0**; matching Fabric API **0.165.2+26.3**.
- Existing staging Atlas client: **0.1.3-dev**, server: **0.1.2-dev**, independently pinned in staging feeds. This manager release does not change either running server or promote old staging code to production.
- Mod rows show installed → available versions, with version metadata persisted in the install record.
- Pack manifests include `channel`, `serverPluginVersion` (expected compatibility, not a live query), and each mod's own `version`. Legacy records/manifests without these fields remain readable.
- Channel switches select separate folders and launcher profiles. Installs refuse a managed folder belonging to the other channel, including identifiable legacy staging installs.
- Hashes and mod IDs, not version strings alone, determine whether a file is current.

## Launcher setup
Exact Minecraft/Loader versions come from the selected pinned pack. The manager fetches the matching
profile JSON directly from the official `meta.fabricmc.net` API, validates its identity, and adds a
custom version under the official launcher's `versions` directory. No Fabric installer EXE/JAR is executed.
The official launcher fetches game/library/runtime files itself when Play is clicked, as it does for
normal Fabric installations. Existing launcher JSON is merged while exclusively open; a uniquely
named `.higdon-backup-*` copy is kept before changes. Both standard and Microsoft Store profile files
are supported. Close the Launcher first so it cannot overwrite the new profiles from stale memory.

Setup never changes accounts. Malformed/locked launcher files and linked setup paths are refused.
If mods install but profile setup is interrupted, completed mods remain and Install can be retried.
Restore previous mods restores mods only, not launcher profiles or account state.

![Update available, with a manager update offered](docs/manager-0.2.0-update.png)
![Up to date](docs/manager-0.2.0-current.png)

Screenshots are local captures of the offline `--ui-preview` fixtures on Windows 11 at 150%
scaling in dark mode. They are not proof of a real install.

## Layout

- **Left:** Mods, Settings and Activity pages, and the manager's own version.
- **Top of the Mods page:** pack status, the installed version beside the latest version, the game
  folder, and the one main button. The button reads Install, Update, Try again or Reinstall
  according to the state, and is de-emphasised when there is nothing to do.
- **Below:** every mod in the release with its own status: up to date, update available or not
  installed. Offline, the list shows what the folder has installed instead.
- **Settings:** game folder, release source, restore the previous mods.
- **Activity:** the timestamped log, including the reason for any failure.

The window follows the Windows light/dark app preference. A Windows contrast theme replaces every
colour with system colours. Buttons, text boxes and navigation are real Windows controls with
accessible names, so keyboard and UI Automation work; status badges always carry their meaning as
text, not only as colour.

## What it changes, and what it never touches

Default source: `https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/production/pack.json`.
The production manifest pins exact public GitHub release tags/assets, sizes, SHA-256 hashes and Fabric
mod IDs. From a pack manifest the manager installs JARs only. It does not replace the launcher, sign into accounts, or change worlds. Game and runtime downloads are performed by the official launcher on Play.

Updates download and verify before changing mods. Existing manually installed versions are migrated
only when their declared Fabric IDs match a managed mod. Unrelated mods remain untouched. Managed
local edits and unexpected filename collisions are refused. Backups and a journal support rollback
and interrupted-install recovery. **Settings → Restore previous mods** puts back the previous
managed mod set. If Minecraft is running, the install stops before changing anything.

## Updating the manager itself

When a newer manager is published, a bar appears at the top of the window with **Install now**.
One click downloads the new EXE, verifies it, swaps it in and restarts the manager.

- The manager's release is read from the `windows-updater` entry of a **fixed** feed,
  `releases/production/components.json` on `main` of this repository. The feed address is compiled in:
  the pack manifest and the Release source setting cannot change it. The legacy staging manager entry also points to 0.3.0 so older managers can upgrade once; 0.3.0 and later discover updates from production.
- The entry must pin a version, size and SHA-256, and its download must be exactly
  `https://github.com/Zigdon7/MooreMinecraft/releases/download/<tag>/Saturn-Mod-Manager.exe`.
  Anything else is rejected.
- Only a strictly higher `major.minor.patch` version is offered. The feed cannot downgrade.
- The download is checked against the pinned size and hash before the running program is touched.
  The previous EXE is kept as `.old` until the new one has started, then removed.
- The EXE is unsigned. Its integrity rests on HTTPS to GitHub plus the pinned hash in this
  repository, so publishing a `windows-updater` entry is a release action: upload the asset first,
  then update `components.json` with its exact size and SHA-256.
- 0.1.6 and earlier cannot update themselves. Download 0.2.0 once by hand.

The EXE is self-contained (no separate .NET installation required) and must be in a folder you
can write to, such as Downloads or the Desktop, for the self-update to work.

## Developer checks
`dotnet run --project Tests/Saturn.Manager.Tests.csproj`

`dotnet publish Windows/Saturn.ModManager.csproj -c Release -r win-x64 --self-contained -o publish/win-x64`

`scripts/windows-smoke.ps1` (from the repository root, after publishing to `artifacts/manager`)
drives the native window through 18 offline fixtures: every state, both extra pages, keyboard-only
navigation, UI Automation page switching from a separate MTA client, the minimum window size and
150% text. It checks that the named controls exist and are inside the window.

`--ui-preview <state>` is an **offline developer fixture**, not a normal mode. States: `initial`,
`checked`, `busy`, `success`, `error`, `offline`, `manager`, `settings`, `activity`; modifiers
`--narrow`, `--large-text`, `--long-values`. Fixtures do not fetch or modify anything. They are not
proof of Narrator behaviour, real desktop DPI scaling or a real install. Earlier review history is
in [docs/DESIGN-REVIEW.md](docs/DESIGN-REVIEW.md).

## 0.2.0

- **Redesign.** Launcher-style layout described above, replacing the single scrolling form.
- **One-click install.** Install/Update no longer asks for confirmation. Restore still does.
- **Self-update.** Described above. Verified locally on Windows 11: a build labelled 0.1.5 offered
  the published 0.1.6, and one click left the EXE byte-identical to the published asset
  (SHA-256 `5ad63c13…77aacd`) and restarted into it.
- **Folder picker.** “Change…” on the Mods page opens the picker directly. The picker is still not
  driven by CI.
- Carried over from 0.1.6: STA UI thread (asserted in CI with `--check-apartment`), check on
  launch, per-mod comparison by file hash.

## 0.3.0 verification
26 core regression tests cover launcher profile/account preservation, repeat installs, Store profiles, Loader upgrades, locked/malformed files, channel isolation and independent plugin versions. Live Fabric metadata was fetched and applied to an isolated launcher fixture. Windows CI builds and native UI smoke evidence are linked in the release. Fixture tests are not proof of an actual signed-in Minecraft launch; first-Play/client rendering still requires a player test.

Official implementation reference: <https://github.com/FabricMC/fabric-installer/blob/master/src/main/java/net/fabricmc/installer/client/ClientInstaller.java> (the official launcher can download Fabric libraries itself).
