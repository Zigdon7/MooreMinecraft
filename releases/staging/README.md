# Saturn staging setup

Staging only: Java **26.3**, Fabric Loader **0.20.1**, server port **25566**, Survival Overworld. Production is unchanged.

## One-time setup
1. Use a separate test Minecraft game directory/profile. Install Fabric Loader 0.20.1 for Java 26.3 once from https://fabricmc.net/use/installer/.
2. Download **Saturn-Mod-Manager.exe** from this repository's staging release. No separate .NET runtime is required.
3. Close Minecraft. Run the manager, choose the game directory used by that Fabric profile and click **Check releases → Install / Update**.
4. Launch that same Fabric profile and join the usual server host with port **25566**.

The default manifest is already configured. It installs the matching Atlas companion and Fabric API. Old copies of those mods are recognized by Fabric mod ID and backed up; unrelated mods are preserved. No ZIP copying is required for later updates: close Minecraft and use the same manager again.

## Controls
- **M**: open the larger shared Atlas overlay. Zoom with the **-** / **+** buttons or the mouse wheel over the map: five steps from 144 to 2,304 blocks across. Zoom needs both the server plugin and the companion from this change or later; the minimap always stays at the closest view. **M** or **Esc** closes it. Rebind "Open Saturn Atlas" under Controls → Miscellaneous if needed.
- A north-up **minimap appears at top right**, using the same shared exploration, markers and accessible lodestones. Refresh is bounded to once per five seconds, and the server maps the 9 x 9 chunks around each player first, so walked terrain appears on the next refresh; F1 hides it with the rest of the HUD.
- Unknown terrain remains blank. This alpha samples coarse surface terrain, not caves or a full vanilla-quality world renderer.
- `/saturnmap` still opens the same overlay. Vanilla Java clients can use `/atlas view` without the companion.

## Travel
`/lodestone create <name>` registers a placed vanilla lodestone you are looking at. Owners can use `/lodestone access <id> public` to share it. Select an accessible destination, request a quote, then confirm before it expires while near an origin lodestone. **Travel is temporarily free** while lodestone spawning is being fixed; the quote and confirm steps are unchanged. Distance pricing in amethyst shards returns by setting `FREE_TRAVEL` to `false` in `mods/saturn-atlas/core/src/lib.rs`. Free quotes need the companion built from this change or later; an older companion shows "Invalid map reply" while a free quote is active, and `/lodestone confirm` in chat still works.

## Update/rollback boundaries
The updater installs only JARs into the selected profile. It does not modify Minecraft accounts, the launcher, saves/worlds or the production server. It verifies pinned release tag, size, SHA-256 and Fabric mod ID. **Roll back last update** restores the prior managed JAR set; interrupted updates are recovered before another install/rollback.

A Windows startup screenshot/test is not proof of successful installation into every user's launcher. Minecraft rendering, live multiplayer behavior and payment recovery remain in-game test gates; this is not a stable release.
