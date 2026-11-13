# Protected local fix: eyeblossom particle disconnect

Owner request, 2026-11-13: remember this incident and do not lose the fix on an update.

## Diagnosis and current status

At 04:07 UTC both players disconnected with `clientbound/minecraft:level_particles`; Pumpkin stayed up. Installed version: `0.2.2+26.3-26.60`, Java protocol 777. Stock binary SHA256: `b2d82f6b22014af45153a209a233b9cedf77cce905bdc042d72fe7f6decb1fe2`.

[Upstream #3065](https://github.com/Pumpkin-MC/Pumpkin/issues/3065) documents eyeblossom transformations sending Trail particles without their required target/color/duration payload. [#3777](https://github.com/Pumpkin-MC/Pumpkin/issues/3777) matches the screenshot and links this cause. Local source confirms the unsafe call. This is a strong diagnosis, not confirmation from the full client report for this incident.

Patch: `eyeblossom-disconnect.patch`, Pumpkin source commit `4555394` (`saturn-eyeblossom-disconnect`), based on stock `eda69440e6369c5f8a05c3f69fc809c40dab93a6`. It suppresses only the broken cosmetic Trail emission. Flower state, sounds, spread scheduling, other particles and player/world data remain unchanged. This is a mitigation, not complete vanilla particle parity. Patch follows Pumpkin GPL-3.0 licensing; see LICENSE alongside it.

2026-11-13 06:03 UTC: owner-approved combined production live-testing build is installed. Source content 1d70914962e8f71315af47bc93bfaaa31a06b8d3; binary SHA256 1ee1ae554186a73014d67dc41685daef3664564ceaa17418578506baee6dbc62. Exact protected source preflight passed; isolated chunk-loading/landing/restart tests and live startup/health passed. The mitigation is included in the deployed binary, but real Java-client eyeblossom transition/particle acceptance is still pending. Do not claim complete particle parity or that the original incident was conclusively reproduced. Verified full backup full-20261113T055643Z and the previous binary/plugin rollback pair are retained.

## Mandatory update gate

1. Include this patch in EVERY future Saturn Pumpkin candidate, including the separate chunk-preload engine. Never overwrite with an unpatched stock release just because its version is newer.
2. Apply from the candidate source root: `git apply /path/to/MooreMinecraft/server-patches/pumpkin/eyeblossom-disconnect.patch`. Do not apply twice.
3. Run `python3 /path/to/MooreMinecraft/server-patches/pumpkin/check-eyeblossom-fix.py /path/to/Pumpkin-source`. It pins the complete reviewed eyeblossom source and fails closed for missing, changed or unpatched code. A changed upstream implementation requires deliberate review/testing and a documented replacement pin; do not simply bypass the gate.
4. Build from the checked source; record source commit, all other local patches, build command and binary SHA256. The source check alone is NOT binary provenance or in-game proof.
5. Test in an isolated world with a real Java 26.3 client: place eyeblossoms and cross day/night transitions. Confirm transformation/sound/propagation and no particle decode disconnect. Bedrock is separate and unverified.
6. Obtain required binary-replacement approval, take managed full backup, preserve rollback binary, warn players and restart once. Verify startup/logs/player list and recheck actual deployed hash. Coordinate with concurrent engine work.
7. Retire the local patch ONLY after a verified upstream fix supplies valid typed Trail data (or another reviewed safe implementation). Record upstream commit, regression evidence and replacement provenance here and in operator memory.

This is an explicit operator/release gate, not an OS-level lock on the binary. Do not claim it prevents arbitrary manual replacement.
