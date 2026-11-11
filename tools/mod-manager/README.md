# Saturn Mod Manager (Windows x64)
One-time portable EXE; subsequent mod updates are selected in the manager, not by unpacking ZIPs.

1. Install Fabric Loader 0.20.1 for Minecraft Java 26.3 once.
2. Run `Saturn-Mod-Manager.exe`, select the game directory used by that Fabric profile.
3. **Check for updates**, review the target versions, then close Minecraft and choose **Install update**.
4. Launch the same Fabric profile. **M** opens/closes the Atlas; the minimap is at top right.

Default source: `https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/staging/pack.json`.
Technical details (including the manifest URL, exact files and operation log) are collapsed by default. The staging manifest pins exact public GitHub release tags/assets, sizes, SHA-256 hashes and Fabric mod IDs. This manager installs JARs only; it does not download/run an executable from a pack manifest, install Minecraft, change accounts/worlds, or replace the launcher.

Updates download and verify before changing mods. Existing manually installed versions are migrated only when their declared Fabric IDs match a managed mod. Unrelated mods remain untouched. Managed local edits and unexpected filename collisions are refused. Backups and a journal support rollback and interrupted-install recovery. Close Minecraft before updating. **Roll back last update** restores the previous managed mod set.

The EXE is unsigned and self-contained (no separate .NET installation required). CI checks native Windows startup; filesystem/update behavior is exercised by isolated regression tests. Real user installation still needs testing before stable release.

## Developer checks
`dotnet run --project Tests/Saturn.Manager.Tests.csproj`

`dotnet publish Windows/Saturn.ModManager.csproj -c Release -r win-x64 --self-contained -o publish/win-x64`

## UI review and accessibility evidence

The manager follows the Windows light/dark app preference through .NET 10 system color mode,
uses native button rendering (no fixed brand fill), and refreshes card/text/link colors
when system colors change. High-contrast palettes take precedence. Native CI captures
both the default runner preference and a dark Windows app preference.

The redesign uses native Windows controls/system colors, explicit accessible labels,
keyboard mnemonics and live status. Install/status stay visible while setup/details
scroll. Folder editing and technical details are tested with keyboard navigation and
an isolated MTA UI Automation client, including expand/collapse transitions.

CI captures initial, ready, busy, success, retry, details, narrow and 150%-font
fixtures. `--ui-preview <state>` is an **offline developer fixture**, not a normal
installation mode. Fixtures do not fetch or modify game files. Actual Narrator,
contrast themes and desktop DPI still require interactive acceptance; fixture
font enlargement is not desktop-DPI proof. See [review evidence](docs/DESIGN-REVIEW.md).

## Native Windows smoke-test screenshot
![Native Windows updater window](docs/windows-smoke.png)
