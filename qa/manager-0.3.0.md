# Mod Manager 0.3.0 — Higdon launcher profiles and Atlas channels

- Target: Windows x64; Minecraft Java 26.3; Fabric Loader 0.20.1.
- Manager version is independent of Atlas: production client/server 0.2.0; existing staging client 0.1.3-dev/server 0.1.2-dev.
- 26 core regression tests pass locally: profile/account preservation, standard/Store launcher config, idempotent setup, Loader changes, malformed/locked file refusal, version metadata/legacy compatibility, separate prod/stage profiles and cross-channel install rejection.
- Live official Fabric metadata fetch → isolated launcher fixture passed. This is config proof, not an actual signed-in game launch.
- Windows x64 cross-publish passed locally.
- Windows CI passed: https://github.com/Zigdon7/MooreMinecraft/actions/runs/43355681979 (source c803db128fbb2f9b3073ee81b545132c6da1d04e). 26 core tests, live public download/config setup fixture, native Windows default/dark UI smoke and Atlas regressions passed.
- Published normal production release: https://github.com/Zigdon7/MooreMinecraft/releases/tag/manager-v0.3.0
- CI EXE: 51,712,378 bytes; SHA-256 c931cce31febe2aa6df7f2ce18b9e5f99a6d3d4229a7aa2991c6fc777e042a7a.
- Anonymous public EXE re-download matched the exact CI size/hash before either updater feed was changed.
- Production manager discovery uses production/components.json. Legacy staging manager entry points to the same manager 0.3.0 to bootstrap existing users. Atlas assets/versions remain independent by channel.
- Windows offline Settings screenshot visually inspected: manager 0.3.0, production channel dropdown, Fabric/profile setup guidance present. These captures are actual EXE rendering of offline fixtures, not live gameplay proof.
- No main/staging server configuration, world, plugin or binary changes.
- First Play/official launcher download and in-game rendering require real player acceptance; not claimed as verified.
