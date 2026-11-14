# Continue Watching shows stale/duplicate cards after a refresh

The owner reported the installed 1.2.1017-beta.1 showing the same Goldbergs episode twice, while WebUI showed Goldbergs and Nocturne once each. This independent task starts at Beta cbafac58; version remains 1.2.1017-beta.1 / 1.2.1016.1. Other submitted alignment PRs and preserved local changes are outside this branch.

## Cause and repair

A native fixture with unique episode identities reproduces stale displayed data after a progress-changing reorder: source order changes from Nocturne/Goldbergs/Other to Goldbergs/Nocturne/Other, yet realized card zero still holds Nocturne. The precise original screenshot's full card mapping was not captured, but this exercises the same Move then Replace update pattern. A data deduplication filter would not repair a stale realized card.

The Home model correctly reconciles the server response in place. WinUI can accept the resulting Move and subsequent Replace without updating its realized element's binding. RepeaterItemsSource now keeps its last accepted snapshot during a Move batch and uses its existing deferred Reset to publish the completed model state. Later changes in that batch coalesce into the Reset. This does not remove titles, alter server ordering or mutate the model. Existing owner hooks retain focus intent and restore the row's scroll position.

The native regression runs through HomeSectionReconciler and an actual mounted SectionRow. It verifies every card and visible series title in the three-episode fixture, repeated forward/backward reorders, insertion before a Move, and horizontal scroll retention. The same regression also runs in the existing Home reconciliation suite. The reattachment helper waits for actual Loaded rather than judging a row before WinUI has resumed its guard.

## Verification and review

Baseline native reproduction failed with a stale realized card despite unique source data. OneWay binding and element-prepared/index-change probes did not solve it and were reverted. The deferred Move refresh passed the regression. Related focused units: 214 passed, zero failed/skipped; log D:/SiloPlayer-build/continue-duplicate-focused.log. Release publish and native host compilation succeeded; final extended native results are recorded in the delivery receipt.

Independent Fable review found no P0/P1 or product defect and one P2 in the old mid-layout-shrink fixture. That fixture began with Move, which correctly no longer reaches WinUI; its response now starts with a publishable Replace before shrinking, preserving rejection/index-fallback coverage. Review advisories led to assertions for all three cards, accepted-delta-before-Move and scroll retention. No second independent review is claimed.

The strict home-reconcile run stopped because the hidden host could not take keyboard focus. This is not a passing focus-restoration result. The separate home-reconcile-data group runs the existing non-strict path and logs unmet prerequisites explicitly; native results distinguish rejection coverage from focus skips. The original strict group remains strict.

A reorder recycles the realized row, so physical hover/open-menu continuity, artwork flicker and keyboard focus on an interactive host are not certified here. User-account response data and a live installed-player reproduction were not read or changed. The separate empty finished-player preview is not included in this fix.

## Delivery

Commit, source/payload hashes, final checks and storage metrics are recorded in D:/SiloPlayer-build/continue-duplicate-receipt.json. The Beta pipeline owns review, fixes, merging, version assignment and packaging after this separate PR is submitted. No installation or restart is performed. One reusable build/publish/native payload was used; no cleanup was retried after the prior automatic approval rejection, and recovered space remains 0 GiB on both drives.