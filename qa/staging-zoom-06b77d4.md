# Staging zoom deployment — 2026-11-12
Pulled main 06b77d4; includes Atlas zoom commit d2ce787 and already-published manager 0.1.5.
16 Rust + 42 Java tests passed locally; client JAR and WASM builds passed.
Published staging-zoom-06b77d4 with immutable client/WASM, pinned manifest and checksums.
Anonymous public release install and rollback passed in an isolated profile.
Default staging manifest and components updated to matching release.

Staging was empty; clean stop and complete offline backup taken before replacing WASM.
No binary replacement, world reset, structure import or production deployment.
Existing fs.read.data/fs.write.data authorization reused; no additional capabilities.
Startup: schema1, anchors0, explored_chunks161 restored; loaded successfully on 25566.
Local TCP connection passed; Survival/force_gamemode unchanged.
Actual in-game zoom/rendering acceptance remains pending.
Internal package versions unchanged; release tag, source and hashes identify new bytes.
