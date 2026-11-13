# Mod Manager 0.3.0 — Higdon launcher profiles and Atlas channels

- Target: Windows x64; Minecraft Java 26.3; Fabric Loader 0.20.1.
- Manager version is independent of Atlas: production client/server 0.2.0; existing staging client 0.1.3-dev/server 0.1.2-dev.
- 26 core regression tests pass locally: profile/account preservation, standard/Store launcher config, idempotent setup, Loader changes, malformed/locked file refusal, version metadata/legacy compatibility, separate prod/stage profiles and cross-channel install rejection.
- Live official Fabric metadata fetch → isolated launcher fixture passed. This is config proof, not an actual signed-in game launch.
- Windows x64 cross-publish passed locally.
- Windows CI/native UI smoke and anonymous public-release install evidence: pending publication gate.
- No main/staging server configuration, world, plugin or binary changes.
- First Play/official launcher download and in-game rendering require real player acceptance; not claimed as verified.
