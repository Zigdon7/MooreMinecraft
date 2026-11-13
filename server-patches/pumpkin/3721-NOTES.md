# Plugin load-chunk backport candidate (not deployed)
Adapted from upstream Pumpkin PR3721, commit 5a28128648777ec5b71c23f498e66f0c6227701a.
Target: base eda6944 + existing tested PR4373 backport (95acaca).
GPL-3.0 host patch; upstream copyright/license retained. Original author metadata:
https://github.com/Pumpkin-MC/Pumpkin/pull/3721

Original PR targeted v0.1/older layout; adaptation adds world.load-chunk to v0.2,
marks the host import asynchronous, and uses current host resource accessors.
It acquires a full-chunk ticket, awaits fetch/generation, and holds the chunk for five
more seconds. Existing get-chunk remains loaded-only; map scans never generate terrain.
Atlas calls load-chunk before destination validation at BOTH quote and confirm,
then verifies actual lodestone and safe footing before any payment/teleport.
The vendored guest SDK has only this WIT addition; compatible host required.
Do not deploy the new plugin on stock Pumpkin: its new import will not resolve.

Optimized native build and isolated runtime validation pending. Production remains
on stock Pumpkin plus Atlas0.2.1. A production binary switch needs explicit owner approval.
