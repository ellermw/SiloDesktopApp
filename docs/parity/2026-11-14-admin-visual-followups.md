# Admin Monitor visual follow-ups — November 14, 2026

This follow-up covers Dashboard, Activity, Server Log, Client Diagnostics, Logging Settings, the Admin shell and the global activity indicator introduced by PR #156. The original PR/branch was not pushed or edited by this work. Changes are separated into theme, Logs/foundation, Dashboard and shell/Activity review groups.

Official WebUI reference: `1a99f7beffcea3850f067df000c319f2704fd23b`, rechecked from official GitHub main on November 14. The final integration includes original PR source `a2aac850a2b493c1615c208031bc58daf62c7d27` and its Beta merge. Version remains the inherited `1.2.1056-beta.1` / numeric `1.2.1055.1`; no manual version change.

## What the comparisons established

The initial implementation did not have complete visual parity. Independent source/state inventories and screenshot comparisons identified reproducible layout, icon, text, chart and interaction defects. All 28 Dashboard bodies were compared; later inspection reviewed 308 native images and documented 224 primary path pairs. Logs inventory covers 81 state families, and shell/Activity/indicator inventory covers 53. These are distinct families, not every possible combination.

Corrections include populated Playback Activity bars; bounded chart geometry; circle/check/shield icons; rounded pills and meters; wrapping widget headers/legends; narrow stat tiles; Users identity/role allocation; Scan Activity columns; Recent Activity sentence spacing; provider-error alignment; palette tokens; flexible page headers; Diagnostics labels, filters, pagination and detail metrics; Logging skeletons, save feedback and layout; full-shell sheets; search surface; Activity breakpoint, metadata, details and pending Message controls.

The final paired 800px Logs default pages were inspected, as were the corrected narrow Users body, Scan Activity body, 1024px Storage tile, Diagnostics detail sheet and calendar-only crop. The Diagnostics download glyph used the light accent foreground on a light primary button; an actual native color assertion reproduced this, and the shared button helper now uses the primary foreground. Prior comparison reports retain the source/capture hashes of their own historical revisions.

## Verification and evidence boundaries

The final integrated main unit suite passed **6,591/6,591**, zero failed/skipped; the separate focused Logs project passed **77/77** after the glyph-color correction. Release publish and native host build passed. Integrated Admin shell/Activity, all-widget Dashboard and baseline Logs groups passed. All **75 expanded Logs scenarios** passed across bounded runs, yielding named functional evidence in **79/81 Logs families**. S19/S20 shared restart variants remain partial.

The expanded fixture corrected its mock/error assumptions: settings revision conflict is HTTP 412; HTTP 409 remains an ordinary failed write. It separately checks both, draft preservation, explicit reconciliation and retirement. Existing Logs snapshot validation now accepts empty component strings without dropping the whole frame. Fixtures retain manifests, geometry, API presets, screenshot hashes and precise gaps.

Full parity is **not** certified. Remaining limits include smaller native font/line-box and spacing differences, some widget range/tooltip/hover and retained-success query states, full keyboard/focus cycles, live account/server behavior, authority changes in every visual state, real playback-overlay/indicator integration, multiple themes/DPI/locales, and native save-picker behavior. The native calendar is a desktop control; the offscreen renderer cannot capture its popup root, so retained fallback images show the CalendarView alone and omit popup/time-control chrome. Capturing a state or passing an assertion is not a pixel-equivalence verdict.

## Final submission cuts

The source stack also includes pipeline source 31bc6b9b, whose tree exactly matches the PR156 squash merge 74f14630 on Beta. Dashboard cut: app publish, native host build, Dashboard native group and 136 focused tests pass. Final shell cut: publish/build and all three Admin native groups pass; full main run passes 6,592/6,593 with one updater-helper failure, then all three cases of that test pass on isolated rerun. The separate Logs project passes 77/77. No cause for the transient full-run updater failure was established; it is retained in the logs rather than reported as a clean full-suite pass.

## Evidence index

The local evidence root is `D:/SiloPlayer-build/admin-visual-parity/`:

- `audit-matrix.json`: all 162 rows, named references, native assertions/captures and remaining gaps.
- `dashboard-visual-audit.md`, `dashboard-body-comparison.md`, `dashboard-corrected-state-comparison.md`.
- `logs-visual-audit.md`, `reference-logs-complete/inventory.json` and reference report.
- `shell-activity-visual-audit.md`, `shell-current-comparison.md`, `reference-shell-activity-complete/inventory-53.json`.
- `final-correctness-review.md`: independent review; its pending-Message P2 was reproduced red and repaired with per-operation ownership and an explicit reopen guard.
- `final-visual-followup-receipt.json`: final source/payload hashes, checks and storage measurements.

The installed player, application settings and preserved stash remain untouched. One reusable build/publish/native payload is retained. Cleanup was not retried after the earlier automatic approval review rejection; recovered space is 0 GiB on C: and D:.