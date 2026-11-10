# Saturn UI design review candidate

This is an **unapproved design/accessibility review candidate**, not a 9/10
sign-off and not a production deployment. The normal staging manifest remains
unchanged until a tested update is deliberately selected.

## Targets

- Windows x64 manager 0.1.3 (.NET 10 self-contained, unsigned).
- Minecraft Java 26.3, Fabric Loader 0.20.1, Fabric API 0.165.2+26.3.
- Atlas companion 0.1.3-dev; unchanged Saturn Atlas server protocol/backend.
- The Pumpkin production binary, configuration, world and plugins are untouched.

## Improvements

Manager: concise player-facing workflow, selected-folder summary, responsive
review cards, persistent actions/progress/status, intentional empty/error/success
states, explicit install/restore confirmation, accessible labels/live status,
keyboard focus/mnemonics, system-color/contrast-theme fallback and collapsed
technical details.

Atlas: compact destination/travel workflow, map clipping and distinct marker
shapes, full-name tooltips/narration, focus preservation, actionable quote states,
text/narrated map alternative and local HUD visibility/size/corner/text settings.
Utility views support 240×150 scaled GUI units; smaller views offer a bounded
Back control rather than overlapping interactive controls.

## Evidence

See `tools/mod-manager/docs/DESIGN-REVIEW.md` and
`mods/saturn-atlas/qa/ACCESSIBILITY-REVIEW.md`. Current client suite has 36 tests;
Rust core 8; updater engine 13. Native Windows CI captures real window rendering,
UIA structure, visible actions and keyboard regressions. Offline fixture images
are not evidence that a real network/install/restore action was completed.

## Outstanding acceptance

Independent reviewers must inspect final native screenshots and genuine in-game
rendering. Narrator speech, real Windows 150/200% DPI/text scaling and contrast
themes, actual Minecraft GUI scales/keyboard/narration and multiplayer stability
still require interactive verification. Do not relabel source checks as proof of
those outcomes, and do not inflate review scores to meet the owner's target.
