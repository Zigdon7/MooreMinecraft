# Saturn UI review preview 0.1.3

Controlled staging review build. **The all-categories 9/10 acceptance gate is still open.**
Production and the normal staging pack manifest are unchanged.

## Windows manager

Download `Saturn-Mod-Manager.exe`, close the old manager and run this copy.
The manager itself is replaced by downloading this EXE; pack manifests install
JARs only and never execute a downloaded program. Windows x64; .NET 10
self-contained; manager version 0.1.3, unsigned.

Its default channel still points at the existing normal staging pack. You can
review the new manager without changing installed mods.

## Optional: test the redesigned Atlas client on staging

Minecraft Java **26.3**, Fabric Loader **0.20.1**, Fabric API **0.165.2+26.3**.
Atlas companion **0.1.3-dev**. No server/backend protocol change.

1. Close Minecraft. Select the game folder used by your Fabric test profile.
2. Expand **Show technical details** and set **Pack manifest URL** to:
   `https://github.com/Zigdon7/MooreMinecraft/releases/download/design-preview-v0.1.3/pack.json`
3. **Check for updates**, then **Install update**. Launch that same Fabric profile
   and join the existing staging server on **25566**.

M opens Atlas. The **Text** button provides a text/narrated map alternative;
**HUD** configures visibility, sizes, corners and text mode. Optional shortcuts
can be assigned in Minecraft Controls. Utility views support 240×150 scaled GUI
units; smaller sizes offer a bounded Back control with resize guidance.

Previous managed files are backed up. **Restore previous mods…** rolls back the
last completed managed update. To return to the normal staging channel, restore
this manifest URL and check/update:
`https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/staging/pack.json`

## Provenance and evidence

Source: `591848db156221ac4488714c6f14630bfa1a3c19`.
CI: https://github.com/Zigdon7/MooreMinecraft/actions/runs/42909115590

- Rust core: 8 passed. Java client: 36 passed. Updater engine: 13 passed.
- Client JAR/WASM and Windows EXE builds passed.
- 14 native Windows cases passed: initial, checked, keyboard, UIA expansion,
  busy, success, error, details, narrow, enlarged text, combined scaling/busy/error,
  and long folder/release values. Direct UIA uses a bounded MTA worker.
- `windows-ui-evidence.zip` contains genuine EXE screenshots, UIA structures,
  keyboard/focus traces and provider results. Offline preview fixtures simulate
  UI states; those images are not proof of a completed real installation.
- Two independent GPT-6.1 Sol reviewers iterated without reading one another's
  reports. Final captured-manager visual scores: hierarchy 9, layout 9,
  readability 9, consistency/polish 8.5, clarity 9. Not rounded to all-9.
- Runtime accessibility remains unverified: Narrator speech, real Windows
  DPI/text-size/contrast themes, actual confirmation/cancel/restore keyboard flows.
  Atlas actual in-game rendering/narration/multiplayer acceptance is also pending.

SHA-256 digests and sizes are pinned in `pack.json`; all release assets have
`SHA256SUMS.txt` entries. No production deployment, world reset or server binary
replacement is part of this preview.
