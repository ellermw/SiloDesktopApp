# OSC and native menus — staged design

Status: **Approved for staged implementation** by the owner on November3 through the written-design question. The owner previously selected native menus in stages and authorized the isolated proof. The proof passes its scoped offscreen checks and remains diagnostic, not an approved production host or release candidate. Product implementation follows focused plans and Beta PR gates below.

## Intent

Make the active video controls and menus feel coherent and dependable: readable hover/press/focus states, predictable scrolling and dismissal, clear current/pending/error choices, and no accidental playback action while operating a menu. Take inspiration from Granary's native interactions while retaining Silo's existing playback capabilities and renderer.

The user explicitly excludes startup/playback loading icons and Dashboard. Do not change preparing/buffering/translation loading presentation, introduce a new spinner, restore administration, or redesign the media-detail pages.

## Verified starting point

- Desktop Beta `d37eae35a2e1b6c4f4c26dde6c22713aa12c478f`, freshly fetched; two README-only commits beyond initial `470d69a6889002b9f0c0adcb90d0b5a3d75c6810`. Source stamps `1.2.549-beta.1` and numeric `1.2.548.1`. The pipeline owns subsequent versions.
- Official server/WebUI main `bb1ee4352b1bec38b4a6441ba81e61a92da81d54`, freshly fetched in the established D: reference checkout. Read that exact revision, not its historical working HEAD.
- Granary comparison/source `c5fe057c1b8c43d82975a621d8723f68ad5be33a`; the completed source comparison is at `D:/SiloPlayer/.codex-tmp/granary-comparison-nov02/SiloPlayer/docs/research/granary-full-review-2026-11-02/player.md`. It is research evidence, not code/assets to copy or runtime certification.
- Normal video is `MpvVideoWindow` plus `libs/mpv/scripts/silo-osc.lua`. `MainWindow.OnPlayerStateChanged` deactivates the legacy `PlayerOverlay` for normal video. Editing that dormant control does not deliver an OSC improvement.
- `PlayerService.OnScriptMessage` routes actual Lua actions to existing service/room/track/replan boundaries. Subtitle search/appearance/AI use `PlaybackDialogHost`, which owns a WinUI window above the video HWND; they already have a separate overlap gate and playback UI ownership checks.
- Granary uses composition output with a `SwapChainPanel`; its pinned architecture document describes current SDR output. We will not switch Silo's renderer to make XAML layering easier.

## Approaches

1. **Recommended: native menus in stages, current renderer retained.** First prove a bounded owned WinUI menu above the existing video window. Give the current HUD consistent visual/input states, then move actual menus to native controls one coherent workflow at a time. This provides standard list scrolling, focus, text layout and automation while bounding renderer risk. It requires careful cross-window input, ownership and geometry handling.
2. **Polish Lua/ASS only.** Smaller initial change and one presentation window. It retains bespoke menu hit testing, layout, scrolling and accessibility limits. This is a valid smaller alternative if the owner chooses it; it should not be described as the native-menu outcome.

A composition-renderer replacement was considered and ruled out for this pass: it changes a different subsystem and introduces output/display tradeoffs that menu polish does not justify. No source/asset copying from Granary.

## Scope and presentation

The OSC pass covers the active Exit/Minimize, transport, seek/volume and utility controls, plus audio, subtitles, quality/version, chapters, stats, compact More and current Watch Party menu/panel workflows. Preserve current button order/capabilities unless responsive overflow needs an explicit documented adjustment.

Use a shared state vocabulary: normal, pointer-over, pressed, keyboard/controller focused, active/current, pending, disabled and error. Exit/Minimize receive the same deliberate backdrop treatment as other controls. A disabled control looks disabled and is inert; moving focus or hovering cannot issue a playback command. Pending actions use a clear text/state treatment, without changing loading icons. Keep the current Silo palette and native theme/text preferences. Short transitions must respect existing/system reduced-motion behavior.

Native menus are dark, contained surfaces anchored to their actual opener, with stable headers, bounded scrolling lists and actions that remain reachable. Long names wrap or ellipsize with an accessible full label. Current and pending selections are distinct; failure leaves a clear retry path and the last accepted state. Selected-row focus is established on open, arrows navigate, Enter/Space activate, Home/End reach endpoints, and Escape/controller Back dismisses once before fullscreen/exit behavior is eligible.

This is deliberately a hybrid delivery: the existing mpv HUD receives appearance/input improvements and the menus become native. It is not a promise that the whole video surface, timeline and every HUD control become one WinUI tree. Any later full-native HUD requires its own proven hosting decision. Actual time-specific trickplay, live dock, SMTC, in-player marker editing and new autoplay policy are separate feature work; preserve existing chapter/marker previews and actions here.

## Native-host proof and adoption gate

An isolated local proof must precede adopting a host in product code. Use an owned synthetic video HWND and generated media; never the installed player or real account/media.

Prove a **menu-sized** borderless WinUI host over the existing renderer. It must not need a transparent full-screen WinUI layer, change `vo`/GPU context/output policy, or replace the video HWND. Reuse existing native controls/theme resources where appropriate. The proof's code stays diagnostic until the real menu integration is reviewed; do not ship an unused host, dormant flag or prototype project as a feature PR.

Required observations: actual native rendering above synthetic moving video; ownership/z-order; no extra taskbar entry; bounded placement in small/large owner rectangles; move/resize/fullscreen/PiP/hide/close behavior; native list focus/scroll/activation; menu-to-dialog handoff; and no accidental pause/seek/volume/exit on dismissal. Closing or replacing a load/owner cancels the menu and pending work. If the host cannot meet these conditions, report the observed failure and revise the architecture; do not quietly change the renderer or call Lua-only menus equivalent.

Windows ownership is a useful foundation, not proof of all this behavior: [Win32 window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) and [WinUI multiple windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/multiple-windows). Native dialog roots must follow the [WinUI XamlRoot contract](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs).

### Scoped proof result

Local throwaway branch `codex/nov03-osc-host-proof`, explicit selector `osc-host-prototype`, final run `D:/SiloPlayer/.codex-tmp/osc-menus-nov03/host-proof/host-four` passes: existing native video/renderer output, owned native z-order, taskbar-exclusion styles, actual 72-row list scrolling/UIA activation, clamped small/large geometry, genuine pause retention, continuation after close, HWND cleanup and unchanged foreground ownership. Final initial/bottom native screenshots are readable after suppressing default row entrance animation. See `tests/SiloPlayer.NativeRegressionTests/OSC-HOST-PROTOTYPE.md` for exact boundaries and earlier capture limitations.

The reused diagnostic app is SHA256 `EF8F37B3FF513A8168B81EA64C83E5F3413CB3C71716B3ADA6C4408162C5B1EF`; its video-window, player and OSC source are unchanged against fresh Beta. This is scoped dependency reuse, not certification of the current Beta app: the diagnostic app also contains separate retained-library work and retains its original version. It was not republished or modified.

All windows were offscreen/nonactivating at recorded96DPI. Separate XAML and video captures do not establish composite visual parity. Physical input, production focus/dismissal and menu→dialog integration, real fullscreen/PiP and stale command ownership remain gates for the corresponding production PRs. An initial native audio implementation must prove those boundaries with its actual caller before adoption; the spike does not waive them.

## Presentation ownership and input

- One native menu presentation per active playback owner. A small immutable owner identifies the playback authority, session/file/load and presentation request. It contains no credentials. Validate it before and after every asynchronous boundary, before command dispatch, and before error/focus publication.
- A new menu replaces the old presentation. An old close/finally callback cannot clear the new menu's input guard or restore its focus. Current native-modal dialogs and menus cannot overlap; handing off to search/appearance/AI closes the menu first and reuses the existing authorized dialog flow.
- Keep Lua and host input state in agreement through a request-scoped open/closed handshake. Native-menu clicks and the outside dismissal click are consumed before they can become `osc-video-click`. A stale acknowledgement cannot unlock a successor menu. Repeated clicks, Escape and controller Back perform one action.
- Do not capture and replay an old pause/position merely to open or close a menu. Opening, scrolling, focusing and dismissing a menu issue no pause, seek, replan or new playback start. Track/quality actions may require existing backend replans, retaining genuine viewer/room intent through those flows.
- Text fields/sliders retain their keys. Menu wheel/keyboard input does not also seek or change volume. Auto-hide cannot hide active menus/focused controls or leave invisible hit targets. When a menu closes, restore the logical opener for keyboard/controller use only if the same playback/presentation still owns it; never steal foreground back from another app.
- Geometry follows the actual video HWND/client bounds and current DPI. Convert between physical video coordinates and logical menu layout explicitly, clamp to a usable viewport/work area, and retire queued repositioning after replacement. Mode changes cannot strand a popup or leave permanent subscriptions/timers.
- Keep rendering policy, transport/proxy selection, watchdog ownership, buffering prompts, subtitle loading/selection, SiloCast and room authority unchanged. The menu layer delegates intent; it does not become a second playback coordinator.

## Existing action contracts

Audio/subtitle rows use the service's actual source inventory and scoped identities, not a row position reinterpreted as a native mpv track ID. Subtitle Off keeps the current negative-server-index contract. Revalidate after inventory/file changes; show current/pending choices from accepted backend state. Preserve latest manual choices and busy/retry behavior when the corresponding Beta PRs land. Do not duplicate pending PR75/77 fixes from older branches.

Quality/version choices use existing service plan/replan paths and server-provided capabilities. Room-pinned versions and unauthorized actions remain unavailable with an understandable reason. Keep Auto/cap/active-quality distinctions, buffering quality/Undo and original playback intent.

Subtitle search/upload, appearance, timing/sync and AI entry points remain reachable according to current capabilities. Preserve the established session/file/temporary-authority rules and existing genuine-pause behavior. Native presentation must not bypass those checks through dormant `PlayerOverlay` actions.

Chapters and stats preserve current data, seek semantics and chapter/marker thumbnail fallback. The stats path should do bounded work only while visible.

Playback Info (stats) is an exception to native presentation: it stays the OSC's own top-left Lua/ASS panel and does not open a native menu. The owner decided that UI Automation announcements are an acceptable accessible representation of this panel. Opening the panel announces every displayed row. Refreshes announce rows whose displayed values changed, and live counters are announced at most once per interval. No focusable per-row native list is provided. To hear the rows again, a screen-reader user closes and reopens the panel. Keyboard and controller users can scroll an overflowing panel. Compact More exposes overflow controls without duplicates or unreachable endpoints. Watch Party keeps existing permissions, stop/leave/end confirmations and room ownership; presentation changes do not grant local authority.

## Video playback speed

Expose normal-video speed in a focused PR using existing `player.playback_speed`: official contract scope `profile_device`, default1, valid range0.25–3.0 with0.05 steps. The WebUI's ordinary presets are0.5,0.75,1,1.25,1.5,1.75,2,2.5,3. A valid stored non-preset value remains representable instead of being silently rewritten.

Separate the viewer's desired rate from transient room synchronization corrections. Do not persist a room drift rate. Disable manual speed in Watch Party while the room owns it. Preserve existing audiobook rate preferences and SiloCast control; never save a remote/temporary playback identity's preference into the desktop account.

Read/write under the active authorized settings scope with current device/profile/load and intent guards. A late read or failed older save cannot replace a newer manual rate or affect a replacement file/profile. Optional preference work must not add a startup barrier or touch the loading presentation. A speed change must not issue a seek or force a pause; preserve desired rate across applicable same-session replans, with clear current-owner save failure feedback. Do not use the raw `NativePlaybackRate` room-correction property as a new unguarded menu action.

## Suggested focused deliveries

After the local host proof, refine exact implementation tasks in the plan. Suggested boundaries:

1. Shared active-HUD visual/input states, including Exit/Minimize, without menu/backend replacement.
2. Native audio menu and the production host/lifecycle/input boundary, integrated with a real caller.
3. Native subtitle menu and existing dialog handoffs, including Off, timing and optional actions.
4. Native quality/version/settings menu with current plan/room feedback.
5. Normal-video speed preference/action/menu integration as an independently testable feature.
6. Native chapters, stats and compact overflow presentation, split further if their evidence differs materially.
7. Native Watch Party presentation and final cross-menu consistency verification.

Each new branch starts from fresh `origin/beta` in a clean D: checkout. Reconcile current pipeline contracts; never overwrite another author's/pipeline's changed head. Submit each compatible tested/reviewed PR to `ellermw/SiloDesktopApp`, base `beta`; no version edits or main/stable promotion. Do not declare the final combined runtime verified from separate branch results.

## Validation and resource policy

Use actual Lua/ASS runtime checks for active-HUD changes and actual owned WinUI controls plus synthetic libmpv output for menus. A source-string assertion or dormant-overlay screenshot is not evidence of delivered behavior. Test small/large/compact/fullscreen/PiP layouts, long tracks/labels, current/pending/failure/disabled/empty states, keyboard/controller policy, text scaling, resize/departure and superseded requests. Cover menu→dialog→player, outside dismissal, repeated input, genuine pause, room commands and profile/file/load replacement.

Record actual hit/focus/action outcomes and native screenshots; compare relevant states over bright/dark synthetic frames. Modelled DPI/text tests are labelled as such. Physical input, real monitor DPI/HDR/DV/audio/phone behavior, complete visual parity and historic buffering remain separate observed-evidence requirements. Retaining the renderer does not itself certify its hardware output.

All .NET builds/tests hold SiloOct18Build after pinned native dependency verification. Use fresh outputs and short D: TEMP. Bound owned test runs, record ownership/results, and never kill unrelated processes. Preserve source/dirty canonical worktree, installed player, releases/current candidates and compact logs/PDB/TRX/screenshots/source/hash receipts. Reuse a product build for fixture-only changes after source binding. Prune superseded diagnostic binaries after preserving bindings; do not accumulate every full publish or overwrite populated outputs. No real credentials/accounts, setup or user-app control.

## Decisions left to the builder

Choose focused class/file names, native list/flyout implementation, existing theme tokens and placement mechanics after the host proof. Keep the host/presentation module cohesive and the playback-intent interface narrow. Exact PR subdivision can follow verified dependency/risk boundaries. These routine choices do not authorize renderer changes, omitted current actions or new unrelated features.
