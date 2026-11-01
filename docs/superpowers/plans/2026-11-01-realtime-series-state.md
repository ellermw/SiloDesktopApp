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
