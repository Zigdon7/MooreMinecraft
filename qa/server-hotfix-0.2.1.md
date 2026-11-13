# Live server hotfix 0.2.1 — 2026-11-13
Origin rejection: host Block.name is unnamespaced "lodestone"; exists() demanded
"minecraft:lodestone" while registration already accepted either. Normalized both
origin/destination validation and landing hazard names; no proximity/access checks removed.

Map report: persisted shared terrain already included both players' areas; coarse zoom
sampled five positions per pixel, hiding thin exploration trails. Wide views now aggregate
every stored chunk, independent of contributor. O(stored chunks), not O(empty view area).
25 Rust +47 Java tests pass; builds pass locally. Tests cover all chunk positions under
zoom8/16/32/64, negative coordinates, combined trails and unknown/bounded views.

Verified managed quick backup 20261113T033520Z before warned clean service restart.
Private pre-upgrade Atlas JSON and WASM retained. No new permissions; existing production grant reused.
Service active on25565, Atlas0.2.1 loaded, health tiles681/queued0/pending0.
All saved terrain keys retained, anchors/markers/payments unchanged (live terrain pixels can refresh).
Zigdon0 rejoined; diagnostic world=world shared_tiles681 nearest_anchor13.9 origin_valid=false,
which is expected outside the preserved 4-block rule. Actual close-origin quote/travel
and two-client graphical confirmation remain pending, not claimed as passed.
Published immutable server-v0.2.1; production server component pointer updated.
Client pack and manager unchanged; no player reinstall needed. Staging unchanged.
