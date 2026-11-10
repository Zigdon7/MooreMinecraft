# Player-facing UI review

Owner requested independent GPT-6.1 Sol blind reviews and iterative fixes toward
9/10 in every design/accessibility category. Manager is priority; Atlas is also
in scope. A target score is not permission to manufacture a passing score.

## Baseline (checkout 7ff076a)

Two independent read-only reviewers inspected source without reading one
another's reports. Both inspected the existing native Windows screenshot;
neither had genuine in-game Atlas rendering evidence.

Manager visual scores: hierarchy 4, layout/scaling 5, typography 5,
consistency/polish 3, information clarity 4 (out of 10).
Manager accessibility scores: keyboard/focus 6, semantics 5, contrast/non-color
6, scaling/clipping 5, discoverability/feedback/error prevention 6.

Key findings: dark disabled controls, mixed native/dark styling, developer
metadata overwhelming the next action, fixed heights/columns, no intentional
empty/progress/success states, missing explicit accessible labels/status and
focus restoration. Atlas findings are tracked alongside its client QA notes.

## Iterations

1. Native system palette and numbered setup/review/install sections, friendly
   release summary, collapsed technical details, meaningful status, progress,
   accessible names/notifications, mnemonic/tab navigation and focus restoration.
2. Review-found fixes: label mnemonics, normalized folder validation, explicit
   installed-record errors, completion guidance invalidation after edits/restore,
   truthful cancellation wording, consistent success fixtures, uniform rather
   than depth-compounded text-enlargement fixtures.
3. Actual native Windows screenshots of iteration 1 exposed below-fold install
   controls on the runner's small desktop. Setup now reflows between two columns
   and one; install/recovery/status remain in a persistent bottom region.

## Evidence and limits

- Core updater transaction suite: 13 passed, zero failed. UI work does not change
  the transaction engine, hash checks, migration, recovery or executable rules.
- Windows project cross-build: zero warnings/errors.
- CI runs the **real Windows EXE**, captures native screenshots/UI Automation for
  initial, checked, busy, success, error, details, narrow and enlarged-text states.
  Fixtures are offline and cannot fetch/install/restore. They are rendering
  fixtures, not a claim of a real installation completing in every pictured state.
- Windows smoke tests inspect named controls, visible primary action/cancellation
  and keyboard Tab reachability. They do not prove full Narrator speech or every
  assistive technology, actual 150/200% desktop DPI, or Windows contrast themes.
- Enlarged-text fixture scales existing effective fonts once by 1.5; it does not
  alter desktop DPI. Native controls use Windows system colors.
- No 9/10 or accessibility certification until reviewers inspect updated native
  captures and the outstanding interactive evidence is obtained.

## Manual acceptance still required

Keyboard-only check/install/cancel/restore and edited/missing profile/source;
Narrator labels, progress/result announcements and error dialog focus; actual
100/150/200% Windows DPI/text scaling; contrast themes; long names/paths and
window resizing. Atlas requires actual Minecraft render/narrator tests, not just
headless layout checks. Production remains separate from staging.

## Native keyboard regression discovered during review

The condensed-folder variant failed the real Windows Tab test: after expanding
Change folder, Browse was not reached and repeated focus stayed on the path
field. The test was retained and strengthened, not removed. Responsive geometry
updates are now deferred/coalesced, label bounds are assigned only on change,
Dock.Top width is left to docking, cards stay parented while their cells reflow,
and expansion deliberately focuses the path field. Preview-only traces record
geometry passes and Enter/Leave events for diagnosis. Final CI must establish
whether the correction fixes the regression; the hypothesis is not a passing
result by itself.

The first trace disproved a geometry storm (three passes only); the synchronous
UIA toggle stalled UI dispatch. Structural visibility changes are now deferred
until after the toggle callback, and keyboard tests expand with Space. A separate,
bounded MTA worker retains direct UIA expand/collapse coverage for both editors.
The a50a8b7 native run passed both paths. This follows Microsoft's
[UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading),
but that guidance and the test result do not by themselves identify every cause
of the earlier hang or prove Narrator speech quality.

## Final review build: 591848d

[Native CI 42909115590](https://github.com/Zigdon7/MooreMinecraft/actions/runs/42909115590)
passed all 14 Windows scenarios, including the four combined font/narrow/busy/
error/long-value stress captures. Keyboard expansion/navigation preserves the
profile path, detects focus traps and reaches the key actions. The isolated MTA
worker passes direct folder/details expansion/collapse. Core suites: Java 36,
Rust 8, updater 13; all zero failures. The early provider/focus hypothesis was
investigated rather than removing a failing accessibility assertion.

Final independent visual review of 8 base + 4 stress captures: hierarchy 9,
layout 9, typography 9, consistency/polish **8.5**, clarity 9. No blocking visual
issue. Reviewer recommends user runtime testing rather than speculative redesign.
The half-point is not rounded up. Accessibility reviewer rates the evidenced
manager keyboard/semantics/contrast/scaling readiness at 8, feedback/prevention 9;
Atlas source-based readiness remains 7–8 without an actual game render.

[Downloadable review preview](https://github.com/Zigdon7/MooreMinecraft/releases/tag/design-preview-v0.1.3)
includes the EXE, client JAR, pinned opt-in manifest, checksums and genuine Windows
evidence archive. This is not an all-categories-9 acceptance or a production
deployment. The normal staging manifest is unchanged. See
[review instructions](../../../releases/design-preview/README.md).

Remaining owner/runtime gate: real Minecraft screenshots and keyboard/narrator
checks; actual Windows Narrator, real 150/200% DPI/text enlargement and contrast
themes; keyboard activation of install/restore confirmation and cancellation.
Owner indicated they will test later. Resume the same review against that
runtime evidence; do not announce the target reached before it is supported.

Post-publication delivery checks passed: anonymous EXE download matched the
51,692,565-byte CI executable and SHA-256; the anonymous manager engine fetched
the public preview manifest, downloaded/verified/installed both pinned JARs into
an isolated empty fixture profile, then successfully rolled back to empty. The
release tag resolves to tested source 591848db156221ac4488714c6f14630bfa1a3c19.
These are delivery/transaction checks, not user-PC or in-game rendering proof.
