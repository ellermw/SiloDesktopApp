# Player Reliability and Session Lifecycle Design

## Reference

- Desktop baseline: `9b684c069f85c9fcca3d06ebf54e5471f355f8d4` (`v1.1.91`)
- Authoritative Silo Server/WebUI: `8164fd594b9fdd8c1944bb0b6251f2d00e4a24ca` from public GitHub `main`
- Branch: `codex/player-reliability-milestone`

## Goal

Make playback survive recoverable transport, session, source, and output changes without silently stopping, returning Home, losing the viewer's position, or desynchronizing the player window and controls. Match the current protocol-v3 WebUI contract while preserving the native desktop player's direct-play, HDR, lossless-audio, and low-latency advantages.

## Observed failure

The August 27 runtime trace captured a direct-play response ending prematurely. The relay correctly refused to splice a different source entity into the same byte stream, later requests returned 404, and protocol-v3 replan returned `source_unavailable`. The desktop then treated that terminal decision as a generic exception and automatically called `CloseAsync`, collapsing fullscreen and returning Home.

That behavior has two client defects:

1. Failure replans always send one attempted plan key with `attempt_count = 1`, so the newer server cannot exhaust alternate delivery/version candidates as the WebUI does.
2. A refused recovery automatically tears down the entire player instead of keeping the playback surface and presenting an actionable terminal state.

## Design

### 1. Protocol-v3 attempt chain

`PlaybackRecoveryAttemptHistory` is the single owner of recovery-loop state for a playback attempt.

- Failure recovery appends the active plan attempt key, retaining the newest 16 unique entries.
- Failure recovery sends a monotonically increasing attempt count capped after 8 attempts.
- Quality, track, output, and seek-intent replans reset the failed-route history because no route failed.
- A fresh playback session or replacement content resets the history and playback-attempt identity.
- The history is committed after the server responds, matching the WebUI's sequencing.

This keeps route selection in the server. The desktop records only what failed; it does not guess which alternate file, remux, or transcode route should be selected.

### 2. Typed terminal decisions

Protocol-v3 terminal decisions become `PlaybackPlanTerminalException`, preserving:

- reason
- server message
- retryability

`PlaybackFailureDescription` maps those reasons to the same user-facing titles and guidance used by the current WebUI. Unknown reasons retain the server message.

### 3. Recovery lifecycle

Recovery is serialized. Each recovery either:

- adopts a replacement plan and reloads the active player at the preserved position;
- safely reloads the same direct session when policy allows it; or
- enters an in-player terminal state.

Entering a terminal state does not call `CloseAsync`. It pauses only if necessary, preserves the last position, active media, tracks, fullscreen/window/PiP state, and player surface, and presents Retry and Exit actions in the OSC. Retry re-enters the same serialized recovery path. Exit is the explicit action that tears down playback.

Repeated premature EOF or mpv errors also enter this terminal state instead of automatically navigating Home.

### 4. Server-pushed plan invalidation

The desktop advertises `plan_invalidated_v1` and handles `plan_invalidated` commands.

- Validate `plan_id` and `reason`.
- Ignore a late command when its plan ID is no longer active.
- Replan as ordinary `failure_recovery`, so the invalidated plan key is excluded.
- Return `completed` only if a replacement plan was adopted.
- Return `rejected` for malformed, unsupported, or unsuccessful invalidation so the server can retire the unsafe session.

The existing replan semaphore prevents simultaneous server/client replans. Plan identity is checked after the semaphore is acquired so a late invalidation cannot evict a newer plan.

### 5. OSC and window state

The native OSC owns the terminal overlay because the video surface is a separate native window and can cover WinUI dialogs. The overlay:

- consumes pointer input;
- remains visible when the ordinary OSC fades;
- supports Retry, Exit, Escape/controller Back, and keyboard focus;
- does not affect the seek bar;
- is cleared only by retry, successful playback adoption, explicit exit, or a new item.

After recovery or autoplay loads a new file, the desktop explicitly reinitializes OSC state and synchronizes actual fullscreen state before publishing the new playback state. It does not activate or flash the taskbar window during episode-to-episode autoplay.

### 6. Verification boundary

Automated coverage must prove protocol request history, reset rules, terminal mapping, invalidation validation, terminal-state source integration, and no automatic close on recovery exhaustion. The complete test suite, x64 Release publish, and Inno Setup packaging must pass. The PR records the exact server commit audited. Long-duration real-media validation remains an explicit QA matrix and is not replaced by build success.

## Non-goals

- Reimplementing server route selection in the client.
- Relaxing direct-stream entity validation and risking byte-stream corruption.
- Changing admin pages or unrelated browse UI.
- Publishing a main release before CodeRabbit and user approval.
