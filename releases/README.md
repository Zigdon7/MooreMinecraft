# Release channels

`staging/pack.json` is the updater's mutable channel manifest. Each entry pins an immutable, versioned GitHub release tag and exact JAR asset, SHA-256 and size. All mods in a compatible pack are updated together; the updater does not independently chase each component's latest version.

Release artifacts: Fabric companion JAR, Pumpkin server WASM (server operators only), compatible Fabric API JAR (license retained), portable Windows updater EXE, pack.json and SHA256SUMS.txt.

No stable/production channel exists yet. A staging prerelease does not deploy anything to the production server.

After tests/builds, generate a new staging manifest using `scripts/build-manifest.py`, create the versioned prerelease with those exact assets, and push the matching channel manifest. Never retag/reuse an already published version for different bytes.
