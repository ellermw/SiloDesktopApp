# Native Audio menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Open a real bounded native Audio menu from the active OSC and route one current source-track choice through existing playback authority, with correct dismissal and lifecycle ownership.

**Architecture:** Lua hands a scoped request to a PlayerService partial. A cohesive owned WinUI host renders track rows and owns native input/geometry/cleanup; the service validates authority/session/file/load and delegates selection to existing `SwitchAudioTrackAsync`. The existing renderer and video HWND remain intact. A small pure request gate prevents stale open/close callbacks from retiring newer menus.

**Tech Stack:** Existing WinUI3/.NET8, Win32 owned HWND/input messages, Lua OSC and actual-mpv/native fixtures.

**Spec:** `docs/superpowers/specs/2026-11-03-osc-native-menus-design.md`, approved by owner November3.

## Global Constraints

- Fresh Beta base `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f`; PR86 is separate pipeline-owned work, not silently included. Recheck current Beta before submission and verify combined behavior where relevant.
- Keep current renderer/output, transport/replan paths, subtitle/room/SiloCast authority, viewer pause and account ownership. No loading icons/Dashboard changes.
- Official server/WebUI refreshed directly at `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`; reference AudioTrackMenu.tsx: readable title, language/meta, codec/channels/default badges, current check and bounded native scroll/focus behavior. Preserve source zero-based indices.
- No stamp/main/release changes; same-repo non-draft PR into Beta. Pipeline owns integration. Preserve PR86 branch, canonical dirty source, installed player and evidence; all new work on D:.
- Every .NET build/test holds SiloOct18Build after pinned engine04E4D27D... verification; fresh outputs, short D: TEMP and generated/synthetic data only.

## Review Focus

1. An old queued open, selection, Loaded or Closed callback after profile/authority/session/file/load replacement must not display a menu, choose a track or unlock a successor.
2. The outside dismissal gesture must be eaten before native mouse-down/key-up/video-click routing; Escape/controller Back closes once without exiting/fullscreen or changing genuine pause.
3. Native focus/deactivation cannot steal foreground after AltTab; move/resize/hide/PiP/fullscreen/disposal cannot strand a menu, taskbar item, subscription or gate.
4. Long/malformed/empty/duplicate track metadata stays readable and accessible; row positions remain original source indices, not native mpv IDs; zero/one track is unavailable.
5. Audio replan or competing dialog/menu changes ownership during await; only the current request publishes selection/error/closed acknowledgements, without replaying old pause/position.

## Files and boundaries

- Create `src/SiloPlayer/Services/PlaybackMenus/PlaybackMenuRequest.cs`: UI-neutral request parsing, normalized anchor and generation/close ownership. Link the exact production file into unit tests, following existing test-project conventions.
- Create `src/SiloPlayer/Services/PlaybackMenus/PlaybackAudioMenu.cs`: bounded native WinUI Audio presentation with actual rows, scroll/focus, owner geometry and window disposal. Its constructor consumes immutable display rows and delegates intent; it has no API/account/transport access.
- Create `src/SiloPlayer/Services/PlayerService.NativeMenus.cs`: current-context capture/validation, capability token and request gate, shared dialog-gate lease, latest open/close and selection orchestration.
- Modify `PlayerService.cs` narrowly: actual Lua message cases, audio-list capability publication, and retirement at BeginMpvLoad, state/authority/close/renderer teardown and existing subtitle dialog entry points.
- Modify `MpvVideoWindow.cs`: scoped native-menu dismissal hook before WM_MOUSEACTIVATE/left down/up routing; consumed pointer sequences must never enter mpv. No renderer initialization changes.
- Modify `libs/mpv/scripts/silo-osc.lua`: capability-gated Audio handoff, request-scoped input guard/closed acknowledgement, pointer/controller/escape routing and legacy compatibility when capability is absent. No Lua diagnostics/unused host flag shipped.
- Tests: linked production request policy unit tests, actual Lua protocol tests, and explicit native audio fixture through real PlayerService/native controls. Register its selector in Program/run.ps1.

### Task 1: Scoped request and Lua handoff

**Interfaces:** `PlaybackMenuRequest` carries owner token, monotonic request sequence and normalized opener rectangle derived from bounded finite OSD coordinates. `PlaybackMenuRequestGate.ResetOwner(string token)`, `TryOpen(PlaybackMenuRequest request)` and `TryClose(string token,long sequence)` reject stale owner/replay/late close. No credentials in tokens. Host sends `osc-native-audio-scope(token)`; Lua emits `silo-native-audio-open(token,sequence,x,y,w,h,osdWidth,osdHeight)` and keeps its input guard until matching `osc-native-menu-closed(token,sequence,consumeDismissal)`.

- [x] Add failing production-policy tests for old owner, replay, newer replacement, stale close and invalid/NaN/oversized coordinates; show a meaningful red before implementation.
- [x] Add actual-Lua tests for desktop and compact/controller opens, no legacy menu overlap, exact scoped handoff, blocked underlay pause/seek/volume while pending, matching vs stale close and generation reset. Preserve legacy behavior without capability.
- [x] Implement the minimal request/anchor policy and Lua protocol, then run focused tests green under the guarded runner.
- [x] Commit only when the integrated caller in Task3 exists; no dormant framework PR.

### Task 2: Native Audio surface and dismissal

**Interfaces:** immutable `PlaybackAudioChoice(int SourceIndex,string Title,string Metadata,string Codec,string Channels,bool IsDefault,bool IsCurrent)`; native menu consumes owner video HWND, normalized anchor, current-request predicate and selected/closed callbacks. `ShowAsync(...)` waits for its actual XamlRoot with cancellation/deadline; `CloseAsync(reason)` destroys its owned window and finishes exactly once. Closed reasons distinguish selection/Escape/owner retirement from deactivation/outside pointer dismissal.

- [x] Write native red: current service/Lua open cannot produce a native menu. Capture actual missing behavior against preserved original app; no synthetic menu drawn by the fixture as a substitute.
- [x] Implement menu-sized borderless owned window, tool-window/no-app-window styles, Silo theme/icon, bounded rows and current check/badges. Ensure a long list uses native scrolling and keyboard/controller focus, Home/End, Enter/Space and Escape.
- [x] Observe actual owner AppWindow bounds/visibility and clamp opener-relative menu geometry with explicit physical/DPI conversion. Close on owner loss/hide/mode retirement; unsubscribe all events/handlers in Dispose.
- [x] Consume native activation dismissal using WM_MOUSEACTIVATE's eat return; guard left down/up when video was already active, suppress the whole sequence, and clear only this request's hook. Keep UI failures inside managed/native boundaries. No full-screen transparent input layer.
- [x] Verify actual owned HWND z-order, no extra taskbar style, UIA row/selection/scroll, native input messages on owned fixture HWNDs, pause/position continuity and cleanup. Offscreen tests must not activate user windows or imply physical input/HDR/DPI observation.

### Task 3: Real PlayerService integration

**Interfaces:** partial captures authority + API context, manager/current session, mpv/video identity, content/file, play-request and file-load generations, original track references and request lease. Every queued UI callback and await revalidates this context. The only audio action is existing `SwitchAudioTrackAsync(sourceIndex)` after current inventory validation.

- [x] Add failing real-service native cases for the open intent, out-of-order opens, stale selection/close and replan/load/authority replacement. Use synthetic API and actual owned renderer; never initialize the owner's account.
- [x] Publish/rotate the native capability scope with the current audio inventory. Close and drain the old host before acquiring the existing playback-dialog semaphore; one presentation owns one release. Reject while an unrelated dialog owns it. No old finally block may release a successor's input/gate.
- [x] Wire actual OSC open/navigation/activate/cancel messages to the native host, and delegate original source indices after full validation. Opening/closing never pauses or seeks; selection retains existing backend replan and current viewer/room intent. Recheck context before publishing any completion/error.
- [x] Retire at earliest BeginMpvLoad, authority/play replacement, minimized/idle/post-roll modes, native teardown and existing subtitle-dialog handoff. Revoke Lua scope/guard on current-owner retirement; stale acknowledgements cannot reopen or unlock another menu.
- [x] Run scoped native/lifecycle/protocol tests green and exercise actual rendered menu over synthetic video, current/pending/failure/empty/long labels and selected-row focus/scroll. Document physical focus/input limits precisely.

### Task 4: Verification, compatibility and Beta PR

- [x] Run full unit suite, one fresh Release publish and explicit native Audio fixture under SiloOct18Build; seal input/payload/fixture hashes and visually inspect captures. Reuse unchanged product evidence only when fingerprints match.
- [x] Check PR75/77/86 latest heads. Do not copy their backend fixes or push to their branches. Verify a separate combined tree when Audio overlaps PR86's current Lua HUD/input work; keep per-branch vs combined claims distinct.
- [x] Independent Standards/Spec review of the exact source, focused red/green corrections and final re-review. Preserve meaningful evidence rather than source-string heuristics.
- [ ] Inspect fresh remote target/head/discussion/inline thread state separately before normal push; new same-repo PR targets Beta with no stamp edits. Attach PR and leave its branch to the pipeline.

## Self-review and execution rulings

The owner approved this stage through the master design; this plan requires no repeat architecture/implementation permission. Execute inline, review independently before submission. Each task contributes to the one real Audio caller; no generic future-menu scaffolding is delivered alone. The pure request gate isolates ownership decisions without putting window code in Core; the service owns identity/authority, the native surface owns presentation, and Lua owns input acknowledgement. Task4 tests any interaction with PR86 instead of assuming separate passing branches compose.

Official native references: [WM_MOUSEACTIVATE](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate) and [AppWindowChangedEventArgs](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindowchangedeventargs?view=windows-app-sdk-1.8). These APIs support the design; actual fixture evidence must establish the implementation.
