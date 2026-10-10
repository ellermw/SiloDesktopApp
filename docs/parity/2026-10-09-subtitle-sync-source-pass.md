# Subtitle synchronization — bounded source pass

**Resumed final acceptance:** the source-stage/unrun statements below are historical. The current official reference is1a7a3970a9928efb0157460c10888832a8a74eb1. [Combined verification](2026-10-09-combined-verification.md) records the finite source/native/physical evidence, exact1.2.241 payload and native/live boundaries. Correction IDs are reconciled in the central ledger; GitHub publication awaits the user's final installer approval.


Reference: official public Silo server commit `22e3a0ba7c1431dda77b957ed1012508d23f2b80`. Read committed `web/src/player/hooks/useSubtitleSync.ts`, `useSubtitleSyncFeedback.ts`, `utils/subtitleSync.ts`, `components/SubtitleMenu.tsx`, `internal/apiv2/subtitle_sync.go`, `subtitle_sync_targets.go`, and `docs/architecture/subtitle-sync.md` with `git show`. The dirty reference checkout and private GitLab were not used.

Difficulty #1 subtitle synchronization source gap is implemented for current stored and external subtitle tracks. This is source work, not visual or physical acceptance. No tests, build, publish, app launch, browser, computer control, installation, production access, or credential inspection were performed. Tests are staged for the coordinator's single combined verification.

## Implemented contracts

- `SubtitleSyncApi` reads capability/inventory/per-key state; starts sync once and resets shared timing using a fresh strong ETag. Conditional mutations use captured authority and do not refresh/replay a rejected write. Server string IDs deserialize through the existing number-reading convention.
- Playback v3 `sync_key` survives inventory-to-session mapping. Only advertised opaque stored/sidecar keys are accepted. The legacy downloaded ID query fallback is bounded to downloaded tracks; external paths, embedded tracks, and live subtitles cannot invent a key. Dynamically downloaded tracks retain their stored key.
- `PlayerService.SubtitleSync.cs` scopes state, pending requests, temporary cue files, capability, and polling to the active session/file/API authority. File/session replacement and close/dispose cancel old work. Inventory reads run together; newer realtime observations win over stale inventory/per-key reads. Per-key reads coalesce into one pending reread. Older job/phase/progress observations cannot overwrite a newer result.
- Pending/running jobs poll every three seconds, skip polling for four seconds after a push, and stop at five minutes or on a read failure. Opening the subtitle menu refreshes inventory, while Reload explicitly restarts status reads. Deadline/failure/missing-subtitle handling clears persistent watched progress feedback. API403 refuses future actions, 422 explains unsupported format, and 412 requests a retry with a new validator.
- Native menu shows Sync to audio, Reset timing, status/result/failure text, and Reload; mouse, keyboard and controller paths share actions. Busy/running actions cannot write. Existing local mpv delay/reset controls remain separate and unchanged.
- Viewer-requested/manual and newly downloaded jobs show their own top-right progress card, phase and percentage, applying state, and terminal success/info/warning result. Initial automatic first-play jobs stay quiet. A terminal push beating the POST response still earns the viewer's terminal notice. Success/info expire after six seconds, warnings after nine.
- Corrected cues download with no-cache headers and an eight-second bound before attachment. Current cues remain loaded until the replacement is attached successfully; only then is the old subtitle removed. Coalescing suppresses duplicate delivery of the same timing within five seconds while allowing a different correction. Subtitle-only replacement preserves the active stream, canonical playback position, pause state, selected server index, and local `sub-delay`. Shared offsets/scales are applied by server delivery, never written to mpv delay. Failed cue replacement keeps the old cues and exposes Reload.

## Files

New: `src/SiloPlayer.Core/Api/SubtitleSyncApi.cs`, `Models/Playback/SubtitleSyncState.cs`, `Services/SubtitleSyncPolicy.cs`; `src/SiloPlayer/Services/PlayerService.SubtitleSync.cs`; `tests/SiloPlayer.Tests/SubtitleSyncBehaviorTests.cs`.

Narrow integrations: `SiloApiClient.cs` captured-context ETag methods, `PlaybackApi.cs` stored timing/job fields, `PlaybackProtocolV3.cs` / `PlaybackStartResponse.cs` / `PlaybackManager.cs` sync key propagation, `PlayerService.cs` lifecycle/menu/realtime/download hooks, `libs/mpv/scripts/silo-osc.lua` subtitle menu and separate sync overlay.

## Combined verification still required

1. Run the staged boundary/API policy tests and full affected test suite; compile Core, Player and WinUI with the rest of this source pass. Check v3 inventory mapping and release serialization/trimming.
2. Fake server: downloaded and external opaque keys; capability unavailable/failure; empty/missing inventory; fresh strong ETag reset; 403/412/422/401; no mutation replay; canceled/in-flight profile/session/file replacement; realtime/poll/inventory ordering; terminal push before POST response; deleted subtitle; duplicate event; different correction within five seconds; five-minute deadline and explicit retry.
3. Native window/fullscreen: menu layout on short/narrow surfaces, wheel-bounded track window, keyboard/controller/mouse Sync/Reset/Reload, busy states, phase and percentage, top-right card spacing, success/no-match/already-synced/failure and 6/9-second expiry.
4. Native mpv: SRT/VTT/ASS/SSA stored and sidecar corrections, positive/negative offsets and frame-rate scaling, reset to original timing, local delay stacking, selected versus unselected job, automatic-first-play silence, user-download automatic progress, foreign timing-change notice, cue fetch/parse failure, fonts, atomic replacement, stale selection during download, unchanged direct/remux/HLS playback position/pause and recovery behavior.

Scoped `git diff --check` produced no diagnostics. It is formatting evidence only; no executable verification or acceptance claim is made.

## Independent review follow-up

Addressed the reviewer’s stale-404 and delayed-mutation observations: forget/reset/start answers now compare their captured observation revision, with a newer push retaining state; a stale 404 schedules a fresh read. The POST still records its watched job so a terminal push arriving before its response earns feedback. Corrected-cue attachment can hand off terminal feedback to that watched job, including already-loaded/coalesced cues. Applying feedback has an eight-second ownership-bound fallback; cue failure reports a warning, and another notice/session prevents the fallback from replacing it. Added policy regressions for revision guards and the terminal-push/late-202 handoff.

The subtitle submenu now scales only its own minimum layout when the fixed sync controls plus one visible track would extend below the seek rail. Transport placement/hit coordinates are unchanged. Short-window verification must include 1920×360 with Sync, Reset, Reload and two status lines. These follow-up source edits remain unexecuted.

Inventory removals invalidate the discarded observation owner, and pending reads/actions additionally require current entry identity, preventing stale-response resurrection. Applying handoff is limited to the currently owned pending notice and the job's matching result timing; completion retires it, so a later reset/foreign correction cannot repeat the old “Subtitles synced” notice.

Coordinator verification: the final full-suite1,524 tests and published15 playback checks pass. Additional actual headless-mpv tests at360/500/720px pass; removing the fitting repair reproduces timeline overlap at360px, restoring it passes3/3. This verifies the rendered menu's action bounds; physical input, corrected-cue attachment and live job feedback remain in the final acceptance queue. See [combined record](2026-10-09-combined-verification.md).
