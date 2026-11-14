# Admin visual parity follow-ups — 2026-11-14

Owner: verify every Admin page introduced by PR #156 against WebUI; leave #156 unchanged and submit grouped follow-up PRs. Reference official main 1a99f7beffcea3850f067df000c319f2704fd23b. Start from immutable PR #156 head 6d9bed5a; Beta is 95075b37 (release stamp only beyond the shared desktop baseline). Current source version 1.2.1017-beta.1 is deliberately retained until dependent work integrates current Beta; pipeline owns versions.

## Evidence contract

Inventory every reachable page, widget and overlay from official source. Track default/loading/empty/error/populated/selected/expanded/dialog/drawer/popup states, keyboard/focus/hover/disabled/dirty/saving/conflict states and their visibility rules. Compare identical synthetic data and theme tokens, not mismatched fixtures. Cover 1440x900, 1280x800, 1024x768, 1023x768 and 800x900, plus narrower widths where the reference changes composition. All 28 Dashboard widgets must have evidence; use focused screenshots below the fold. Different ranges may share a layout but their label/legend/empty/hover states still need checks. Test shared theme-token behavior separately; don't describe representative screenshots as exhaustive pixel parity. Preserve explicit no-navigation-slide preference.

## Groups

1. Shared shell/activity indicator and Activity: sidebar/header placements, desktop/narrow drawer, menu/search, build/restart notices, all popover sections, session table/stacked rows, filters/sort, IP/message/action/FFmpeg/detail states.
2. Dashboard: each of 28 widgets, states, chart axes/tooltips/legends/ranges, stat/health/session presentation, notices, add/customize/remove/reset/conflict/save controls and responsive placement.
3. Logs: Server Log live/history/application/audit/filter/summary/details; Diagnostics list/filter/storage/capabilities/toggle/details/actions; Logging clean/advanced/dirty/saving/conflict/restart/navigation confirmations.

## Execution

- Two independent read-only inventories cover Dashboard and Logs. Lead covers shell/indicator/Activity and integrates a state matrix.
- Capture real extracted official WebUI and actual WinUI controls with matched fixtures. Record per-state evidence, differences, corrections and remaining native constraints.
- Correct presentation in existing native seams. Add meaningful geometry/state assertions for reproduced defects. Keep authority/action behavior intact.
- Serialize all app/native builds and reuse D:/SiloPlayer-build artifacts/publish/reference/host. Before every build/run check C,D >=50GiB and total output <=10GiB. Children do not build. Preserve prior cleanup rejection; no deletion retry.
- Review corrected groups, run appropriate focused/full tests and native matrix, inspect resulting screenshots against reference, and record source/product hashes.
- Use separate follow-up branches/commits/PRs by group. Never push codex/admin-monitor-nov14 or modify PR #156. If #156 is still open, state dependencies and use dependent PR bases rather than disguising the original 108-file implementation as new visual changes. Reconcile/retarget to Beta when prerequisite source is available.

## Completion

No unchecked page/state is called visually verified. Delivery reports every matrix gap explicitly, separate grouped PR links and exact heads, verification and storage. Installed player/settings untouched.
