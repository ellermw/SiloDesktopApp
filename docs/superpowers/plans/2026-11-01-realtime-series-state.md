# Realtime series state Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this approved task inline; no subagents.

**Goal:** Refresh open related details after another device changes an uncached episode.

**Architecture:** Add captured-context, bounded realtime invalidation batches alongside existing local scalar messages. Reuse DetailCacheInvalidation and ItemDetailViewModel retained-state seams; connect shell lifecycle and existing reconnect catch-up.

**Tech Stack:** C#/.NET 8, MVVM messenger, TimeProvider timers, xUnit and synthetic HTTP.

**Spec:** ../specs/2026-11-01-realtime-series-state.md

## Global Constraints

Keep source 1.2.519; hold SiloOct18Build for all .NET commands; no installed player/account/production operations. Work only in assigned checkout.

## Review Focus

- Unknown episodes must refresh named series/season counts without changing sibling scalar state.
- Bursts retain multiple series, including series omitted in subsequent events.
- Queued old profile/subscription/window callbacks must have no effects.
- Reconnect refresh and navigation/profile changes during reads must respect ownership.
- Events arriving during a read need one trailing read, without overlapping request storms.

## Task 1: Captured bounded invalidation and authoritative detail refresh

Files: Messaging/MediaSurfaceMessages.cs, Services/RealtimeMediaStateRefresh.cs, Services/DetailCacheInvalidation.cs, MainWindow.xaml.cs, ViewModels/ItemDetailViewModel.cs, unit/native fixtures.

- [x] Add regressions for unknown series/season, scalar isolation, lifecycle rejection, coalescing/deadline and catch-up; run focused red under mutex.
- [x] Add batch message and coalescer; route shell events and lifecycle into it; refresh retained aggregate/list state with stale-read guards and serial trailing work.
- [x] Run focused green and complete SiloPlayer.Tests using parent verification runner.
- [x] Add synthetic native route if feasible; parent runs final Release publish/native to reduce mutex contention.
- [x] Self-review, diff check, append task handoff, commit assigned branch and report exact evidence.

Ruling: Explicit authoritative play targets join aggregate refresh (parent confirmed) because remote progress changes Resume/Play Next routing; preserve omitted legacy targets. No playback command is sent. The new regression first failed both explicit replacement and explicit null cases in `realtime/red-target`, then passed in `realtime/green-focused`.

Execution evidence: `realtime/red-detail` 2 expected failures; `realtime/green-detail` 26 passed; `realtime/red-target` 2 expected target failures, 25 passed; `realtime/green-focused` 83 passed. All commands use the external Invoke-Verification.ps1 runner and SiloOct18Build. Native route added; parent performs isolated Release/native verification to avoid duplicate mutex work.

Self-review ruling: retain consumer cancellation and distinguish immediate remote revisions from bounded list-refresh revisions. Reproduced missing consumer cancellation in `red-cancellation`, initial list retry pressure in `red-lifecycle`, and queued-input timer starvation in `red-deadline`. Corrected with per-read cancellation, separate remote generation, and inline deadline expiry. First full suite passed 2,827 before these refinements; final focused/full verification follows.

Final concrete feedback regression: a pending local watched write lost its accepted cascade when a remote sweep advanced its mutation revision. `red-local-feedback` reproduced one failure. Defer that sweep until the local command settles, with navigation-owned cancellation; `green-local-feedback` passed 88 focused tests. No further general changes are planned; `full-local-feedback` is the final full-suite gate.

Final suite result: 2,830 passed, one unchanged update-cleanup setup failure at UpdateTransactionLifecycleTests.cs:215 (inventory unknown); source/update code untouched. Parent requested one isolated method rerun, no unrelated update fixes, and authorized commit with exact disclosure rather than another full run. Parent owns final publish/native and independent review.

Isolated rerun passed all four update-cleanup variants. Parent final Release publish succeeded with unchanged product inputs and app SHA256 1E4BF6311780D62BEC54A160A5ECF8D5459054A78E0D33889C6515FDDB0996F0. Parent runs final native routes and independent reviews; no native success is claimed before their receipts exist.

## Independent review correction pass (2431f182 baseline)

Standards four findings and Spec six findings overlap in six grounded behaviors. Preserve all earlier evidence, including parent correction bc6e4660 and published candidate 66CF8CA5…, as historical candidates.

- [x] Reproduce sustained raw plus coalesced traffic using actual HTTP snapshots, production cache/revision registrations and mounted VM recipients; both default and library-scoped reads fail before correction.
- [x] Reproduce active episode/catch-up scope loss, initial scoped null-Item invalidation loss, local final-progress overwrite and sibling /watch retry exhaustion against actual API clients.
- [x] Make mounted reads owned catalog requests; preserve unioned active/trailing scope; hold initial invalidations through a bounded read; give local progress its own revision/save barrier; distinguish aggregate series scope from changed playback-leaf scope.
- [x] Reproduce real native season target null→playable failure using the preserved 66CF candidate, then recompute season action on mounted item/episode changes and test both target transitions.
- [x] Migrate earlier cache-delegate detail fixtures to delayed HTTP responses so they exercise the owned-read boundary. Keep global speculative cache invalidation enabled.
- [x] Run full unit, fresh Release publish, updated native realtime route and existing local-watched route; record exact receipts and commit the focused correction.

Evidence so far: review-red-owned 7 expected behavioral failures; review-green-owned 7 passed; review-red-season-native actual hidden Play failure; review-focused-migration exposed 15 old cache-only fixture failures; after HTTP fixture migration, review-green-focused passed 122. Full and fresh Release receipts follow. No further generic review loop is requested by the parent.

Final correction evidence: review-full-unit 2,839/2,839 passed (zero failures/skips); review-release-app succeeded, DLL637F11276FDA9055728ED073B76BB9CD317EAA44B210C268F8B73F6A9202DA88. review-green-season-native and review-native-watched both pass this same output. The updated native fixture checks explicit season target null→playable→null→playable and existing sibling isolation. review-final-source-binding.json hashes 1,006 tracked product inputs, all unchanged since publish began; final commit is bound after commit. No push, install or installed app control.

## Failed local-save barrier follow-up (dc8a7c13 baseline)

Parent identified a concrete failure chain: Task.WhenAll retains a failed local final-save task forever, preventing later remote reads even after a successful local save. Preserve the existing 637F app/output and all prior evidence.

- [x] Reproduce failed save → newer successful save → raw/coalesced event using real messenger/cache registrations and an older held HTTP episode snapshot.
- [x] Track final-save settlement independently from success/failure; keep local ownership revisions and wait for all current save barriers before taking the next owned snapshot.
- [x] Run focused red/green, complete suite, fresh Release and same-output native routes; bind final source and commit only this material correction.

`barrier-red` reproduced the material concern: expected aggregate2, actual0 at the final backend snapshot despite the newer successful save. The correction observes each save's settlement without propagating its failure into the read barrier, retires completed predecessor barriers, and rechecks whether another local save joined while awaiting. Existing local ownership generation checks still reject the held pre-save episode snapshot. No broader review work is included.

Final barrier evidence: focused123/123, full2,840/2,840 with zero failures/skips, fresh Release5169E2C32429414CACC83E9BADE6D1D56DD88E54CC106953BC8C304D9DF6810C; barrier-native-series and barrier-native-watched pass the same app. barrier-final-source-binding.json records 1,006 product hashes unchanged since Publish began. The prior637F output remains frozen; no push or installed app action.
