# Client hotfix publication — 2026-11-12
Source 523fe7cf95d62a0fa132c9730fcd523c1de200fa.
Local 39 Java + 14 Rust tests passed, client JAR and WASM builds passed.
GitHub Atlas job also passed: actions/runs/43171504943.
Published client-hotfix-523fe7c prerelease with JAR, pinned manifest and checksums.
Anonymous public-release download, verified install and rollback passed in isolated profile.
Default staging manifest updated, Fabric API pin unchanged. Components metadata now
links the current Windows system-theme manager 0.1.4 as well as the hotfix JAR.
Manager source and server source are unchanged by this upstream commit; existing
manager-preview-v0.1.4 and staging server remain in use without restart.
In-game mouse-grab/keybinding acceptance still needs a real client test.
