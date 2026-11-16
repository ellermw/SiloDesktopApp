# Server credits autoplay implementation plan

> **For agentic workers:** Use superpowers:executing-plans for this increment. The lead implements it inline; independent request-permission and Admin work use separate T3 worktrees.

**Goal:** Match official Silo #4278: credits Up next counts only while autoplay is enabled and playback is running; manual Play now remains available.

**Architecture:** Keep navigation and playback ownership in PlayerService. Send the effective autoplay preference to the production Lua OSC, using the existing bounded settings refresh. Lua retains the countdown while paused or disabled and hides its numeric label/progress while autoplay is off. Keep the desktop still-watching guard and stale-intent fences.

**Tech stack:** .NET 10, WinUI 3, bundled libmpv/Lua, xUnit runtime fixtures.

**Reference:** Official main `abd85d0cadac28f4ab7db6821dad9634fc114790`; `web/src/player/hooks/useNextEpisode.ts` and `NextEpisodeOverlay.tsx`. Desktop baseline `2c33599f751e1bb7e18a56ee378ffe918710d74d` (1.2.1145-beta.1).

## Constraints and decisions

- Use the existing native OSC and settings APIs; changing navigation infrastructure or adding a second timer/network poll is unnecessary.
- An unresolved setting must not trigger automatic navigation. Missing settings on older servers retain the established enabled default after resolution; explicit false wins.
- Pause/resume and setting changes preserve the seconds remaining. Leaving the credits range closes the card; re-entry starts a fresh count. Cancel suppresses the card for the current episode. Manual navigation remains a viewer intent and fires once.
- Keep the installed player open, preserve source/evidence, and leave versioning/packaging to the Beta pipeline.

## Task 1: Countdown behavior and preference bridge

- [x] Add production-Lua runtime regressions in `tests/SiloPlayer.Tests/MpvOscAutoplayHudRuntimeTests.cs` for unknown/off, pause/resume, enable/disable, range exit/re-entry, cancellation and one automatic/manual intent.
- [x] Run those tests against unchanged Lua and retain the expected failures.
- [x] Update `libs/mpv/scripts/silo-osc.lua` to account only eligible elapsed time and render the plain Up next label without a timer when disabled.
- [x] Publish the resolved preference from `PlayerService` with playback/authority identity checks; reuse the current settings refresh and invalidate pending old results.
- [x] Run related OSC and playback tests, Release publish, and the matching native HUD fixture. Record baseline failures separately.
- [ ] Review the final diff, record precise evidence/limits, commit the scoped increment and open a qualifying Beta PR under standing authorization.

## Review focus

Unknown/slow settings; paused boundary transitions; same-player successor loads and late settings results; changing autoplay at zero; repeated ticks or pointer clicks after navigation. Tests must exercise these through the production Lua/host route rather than duplicate the countdown algorithm.
