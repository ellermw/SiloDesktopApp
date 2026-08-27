# Player Reliability and Session Lifecycle Implementation Plan

> Execute on `codex/player-reliability-milestone`. Keep the PR unmerged.

**Goal:** Complete protocol-v3 recovery parity and make terminal playback failures recoverable in-place instead of unexpectedly returning Home.

**Architecture:** Keep server planning authoritative. Add a small Core attempt-history module and typed terminal error, then make `PlayerService` the single UI lifecycle coordinator. Extend the existing mpv OSC for persistent recovery actions and add server-pushed plan invalidation to the existing playback WebSocket.

**Tech stack:** .NET 8, WinUI 3, libmpv, Lua OSC, xUnit, Inno Setup 6.

---

## Task 1: Protocol-v3 recovery history and terminal types

**Files:**
- Create: `src/SiloPlayer.Core/Services/PlaybackRecoveryAttemptHistory.cs`
- Create: `src/SiloPlayer.Core/Services/PlaybackPlanTerminalException.cs`
- Modify: `src/SiloPlayer.Core/Services/PlaybackManager.cs`
- Test: `tests/SiloPlayer.Tests/PlaybackRecoveryAttemptHistoryTests.cs`
- Test: `tests/SiloPlayer.Tests/PlaybackManagerTests.cs`

1. Add failing tests for 16-key retention, 8-attempt limit, failure accumulation, user-intent reset, and typed terminal fields.
2. Run the focused tests and confirm the new tests fail for the intended reasons.
3. Implement the attempt-history module and typed exception.
4. Integrate them into start, replan, replacement, and stop lifecycle paths.
5. Run the focused tests to green.

## Task 2: WebUI-parity failure descriptions

**Files:**
- Create: `src/SiloPlayer.Core/Services/PlaybackFailureDescription.cs`
- Test: `tests/SiloPlayer.Tests/PlaybackFailureDescriptionTests.cs`

1. Add table-driven failing tests for every current WebUI terminal reason and transport-upgrade/session-expired cases.
2. Implement the reason-keyed mapping while preserving useful server messages.
3. Run focused tests to green.

## Task 3: Realtime plan invalidation

**Files:**
- Modify: `src/SiloPlayer/Services/PlaybackWebSocket.cs`
- Modify: `src/SiloPlayer/Services/PlayerService.cs`
- Modify: `src/SiloPlayer.Core/Services/PlaybackManager.cs`
- Test: `tests/SiloPlayer.Tests/PlayerServiceSourceTests.cs`
- Test: `tests/SiloPlayer.Tests/ServerContractSourceTests.cs`

1. Add failing contract tests for advertised capability, payload validation, stale-plan no-op, serialized replan, and completed/rejected results.
2. Expose active plan identity safely from `PlaybackManager`.
3. Add async `plan_invalidated` handling through the existing WebSocket command path.
4. Run focused tests to green.

## Task 4: In-player recovery terminal and OSC actions

**Files:**
- Modify: `src/SiloPlayer/Services/PlayerService.cs`
- Modify: `libs/mpv/scripts/silo-osc.lua`
- Test: `tests/SiloPlayer.Tests/PlayerServiceSourceTests.cs`
- Test: `tests/SiloPlayer.Tests/MpvPlayerSourceTests.cs`

1. Add failing tests proving recovery exhaustion and terminal replans no longer call `CloseAsync`, and that Retry/Exit messages are wired.
2. Add a persistent, opaque-enough terminal overlay above the timeline that owns pointer and keyboard input.
3. Preserve position, pause intent, tracks, and display state while terminal UI is visible.
4. Retry through the serialized recovery coordinator; close only on explicit Exit.
5. Clear the terminal overlay after successful adoption/new content.
6. Run focused tests to green.

## Task 5: EOF, stall, autoplay, OSC, fullscreen/PiP hardening

**Files:**
- Modify: `src/SiloPlayer/Services/PlayerService.cs`
- Modify: `src/SiloPlayer.Core/Services/PlaybackRecoveryPolicy.cs`
- Modify: `libs/mpv/scripts/silo-osc.lua`
- Test: `tests/SiloPlayer.Tests/PlaybackRecoveryPolicyTests.cs`
- Test: `tests/SiloPlayer.Tests/PlayerServiceSourceTests.cs`

1. Add failing regressions for duplicate EOF signals, repeated premature EOF, paused playback, and autoplay OSC restoration.
2. Route every non-terminal interruption through one serialized recovery entry point.
3. Replace automatic player teardown with terminal UI on exhausted recovery.
4. Explicitly reset/reinitialize OSC after recovery and Playing Next adoption.
5. Reconcile the fullscreen control with actual native window state after each transition; preserve PiP/window mode.
6. Ensure autoplay does not activate/flash the taskbar window.
7. Run focused tests to green.

## Task 6: Full verification and audit-ready PR

**Files:**
- Modify only if needed: `README.md` (milestone status, not a release link/version)

1. Run the complete Release test suite.
2. Run x64 Release publish.
3. Build the installer with Inno Setup to verify packaging and signing inputs; do not publish a GitHub release.
4. Review runtime logs for secret leakage and inspect the complete diff.
5. Run a standards/spec review against the branch diff.
6. Commit and push `codex/player-reliability-milestone`.
7. Open an unmerged PR against `main`, recording tests, build/package results, exact server reference SHA, and the real-media QA matrix.
