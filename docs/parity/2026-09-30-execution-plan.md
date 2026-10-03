# Remaining parity execution

Goal: finish the September 30 queue in its agreed order, using native Windows controls and the current official WebUI contract.

Reference: official GitHub main, fetched again September 30: `8e2e840474a085c6df6571a5a2850f7eb996810c`.

Architecture: extend existing API adapters, models and pages. Keep capability checks and request display/selection rules in testable Core policies. Reuse title details for external watchlist navigation. Preserve unknown stored settings and server authority; avoid speculative local status transitions. Context changes must cancel stale results.

Constraints: preserve the pending 1.1.106 implementation and unrelated changes; do not operate the installed app or production infrastructure. Publish/install requires its own authorization. Existing user instruction authorizes implementing this queue, so implementation proceeds without repeated design approval questions.

Review focus: authoritative server states; older-server capability fallback; profile/context isolation; preference preservation; reachable native interactions and responsive presentation.

Each package: reproduce missing behavior with focused tests, implement, run relevant checks, record acceptance evidence and remaining live checks. Final verification includes full tests, native fixtures, publish and installer creation. No package is visually complete based solely on source tests.

The checked items below mean implementation and the recorded regression checks passed in local candidate 1.1.107. Live/visual acceptance remains separately documented in [queue progress](2026-09-30-queue-progress.md).

- [x] 1–3: WatchParty, readers, appearance implemented previously; live acceptance remains documented separately.
- [x] 4: Requests and external watchlist. Models/capabilities; seasons; state/outcome/download rendering; guarded cancel/follow; external title tabs; automatic request preference; request badge and preference preservation.
- [x] 5: Detail and error states; series layout/season groups; description expansion; accessible catalog promotion.
- [x] 6: Search paging, people, filters, cancellation, keyboard groups, independent external results and Discover navigation.
- [x] 7: Collections tree preservation, capability templates, list sources and artwork behavior.
- [x] 8: Account password/recovery/session transitions and import profile scope.
- [x] 9: Provider sync, ratings, dropped shows and dismissal/Undo.
- [x] 10: Profile limits, advisory access and revision-gated badges.
- [x] 11: Home recipes/preferences and layout import/export.
- [x] 12: Theme audio lifecycle and ownership.
- [x] 13: Shared seek and intro preferences, overrides and Undo.
- [x] 14: Subtitle opacity/Gray, verified original format support, startup telemetry.
- [x] 15: Live marker replacement and withdrawal.
- [x] 16: Authoritative series play target.
- [x] 17: Remaining labels, empty states and menu polish.

## Package 4 steps

- [x] Add failing wire-contract and policy tests using sanitized fixtures from the reference contract.
- [x] Extend request capabilities, seasons, download and viewer state models; add follow and watchlist title adapters with safe cursor traversal.
- [x] Connect season selection, authoritative status/progress, follow and watchlist actions to title details; correct My Requests cancellation.
- [x] Add watchlist tab for external titles with sort/count/remove/open and capability/error states.
- [x] Add capability/revision-gated auto-request setting and request status badges while preserving future preference keys.
- [x] Verify focused and native behavior, record concrete remaining acceptance, then continue package 5.
