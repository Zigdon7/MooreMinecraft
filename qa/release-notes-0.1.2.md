## Saturn staging alpha — not production
- Press **M** to open/close the larger Atlas; **Esc** also closes it.
- Top-right north-up minimap, shared terrain/markers and five-second refresh.
- Staging uses **Survival**, forced on login, port **25566**. Production is unchanged.
- Portable **Saturn-Mod-Manager.exe** installs/updates the exact compatible companion + Fabric API from this repository's releases, with checksums, backups and rollback. No more manual mod ZIP updates.

### Install/update
Download the EXE once, close Minecraft, choose the game directory of your **Fabric Java 26.3 / Loader 0.20.1** profile, then **Check releases → Install / Update**. Launch that same profile. The manager does not install Minecraft/Fabric Loader or modify accounts/worlds. Existing manually installed versions of these mods are recognized and backed up; unrelated mods remain untouched.

[Full staging instructions](https://github.com/Zigdon7/MooreMinecraft/blob/main/releases/staging/README.md)

### Verification
47 automated tests passed (8 Rust + 26 client + 13 updater). Native Windows startup passed and its actual window screenshot was inspected. JAR, WASM and EXE are exact successful CI artifacts.

[CI evidence](https://github.com/Zigdon7/MooreMinecraft/actions/runs/42900973560) · [QA record](https://github.com/Zigdon7/MooreMinecraft/blob/main/qa/RELEASE-0.1.2.md)

Actual Minecraft rendering/multiplayer/payment recovery remain in-game test gates. This prerelease is not stability certification. Windows updater is unsigned; it is self-contained and does not need a separate .NET installation.

`pack.json` is the client updater manifest; `components.json` lists all component versions/hashes. The `.wasm` is for the Pumpkin server operator, not a client mods folder. Fabric API retains its Apache-2.0 notices; see NOTICE.md and LICENSE.
