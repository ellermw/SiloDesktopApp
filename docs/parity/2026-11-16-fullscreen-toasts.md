# Shared toasts above playback

Base: Beta `212333915` (`1.2.1168-beta.1`). Version numbers are left to the pipeline.

## Official reference

Fetched `https://github.com/Silo-Server/silo-server.git main` directly in
`D:/SiloPlayer-work/server-upstream-nov14`; fetched main was
**5b3dcaa2fdfec6de2a537a2e10897352a1078689**. Read official
[#4304](https://github.com/Silo-Server/silo-server/commit/370e014bbde507486c2d7c6ce29ab90d634fd810),
**370e014bbde507486c2d7c6ce29ab90d634fd810**.

Its toast change (`web/src/components/ui/sonner.tsx`) moves the existing toaster
host into `document.fullscreenElement`, or back into `document.body`, without
remounting its contents. Existing notifications therefore survive fullscreen
entry/exit. The subtitle-dialog portion is already represented on desktop by
`PlaybackDialogHost`'s window owned by the playback HWND and is outside this change.

## Desktop behavior

The same registered `ToastContainer`, card objects, running dismissal timers,
action delegates and pending/disabled action buttons move into a lazily created
WinUI window owned by the video HWND. The overlay is used only when there are
live shared toasts and the visible video surface covers the shell (expanded or
fullscreen). Fullscreen entry/exit while still expanded only repositions the
same overlay. New toasts continue through the existing `ToastService` registration;
queued notifications are not dropped by a host re-registration.

`WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `AppWindow.Show(false)`, nonactivating
positioning and `WM_MOUSEACTIVATE` → `MA_NOACTIVATE` keep presentation/clicks
from activating the toast window. Toast buttons suppress pointer-driven focus.
The native window region is the union of rounded toast-card layout rectangles;
the video and gaps between cards remain directly clickable. Physical pixels are
derived from the current XamlRoot rasterization scale. The overlay does not use
topmost positioning and follows video surface events without a polling timer.

Mini preview, PiP, hiding/stopping playback, owner destruction, the last toast's
expiry, explicit toast-window close and shell close return the control to its
original shell layer and close the overlay. HWND disposal is handled before the
video's owner HWND is destroyed when disposal occurs on the UI thread. Queued
geometry callbacks become no-ops after unregister. Event subscriptions, subclass
hooks and temporary GDI regions are released.

Changing XamlRoot can stop a storyboard. Entrance animations now have final
underlying opacity/translation values; retirement uses an independent 200 ms
timer instead of relying on `Storyboard.Completed`. Moving during fade-out
cannot strand an expired card. Dismissal is idempotent and stops the preceding
timer. The existing action guard still disables Undo before awaiting it.
Playback-specific Lua/OSC notices and `PlaybackDialogHost` are unchanged.

## Files changed

- `src/SiloPlayer/Services/ToastHostPlacement.cs`: UI-independent selection and
  idempotent movement of one live host.
- `src/SiloPlayer/Services/PlaybackToastHost.cs`: shell/owned-window routing,
  video events, rollback and lifecycle cleanup.
- `src/SiloPlayer/Services/PlaybackToastWindow.cs`: owned nonactivating window,
  card-only native region, DPI-aware positioning and native resource disposal.
- `src/SiloPlayer/Services/ToastService.cs`: preserve registration while routing;
  dispose the host on unregister/replacement.
- `src/SiloPlayer/Services/MpvVideoWindow.cs`: expose whether its current geometry
  mode covers the shell, excluding mini preview and PiP.
- `src/SiloPlayer/Services/PlayerService.cs`: connect each newly created video
  surface to the shared toast host.
- `src/SiloPlayer/MainWindow.xaml.cs`: unregister on shell close.
- `src/SiloPlayer/Controls/ToastContainer.xaml.cs`: count/layout access for host
  movement, focus behavior and animation-independent retirement.
- `tests/SiloPlayer.Tests/ToastHostPlacementTests.cs`,
  `tests/SiloPlayer.Tests/FullscreenToastSourceTests.cs` and the test `.csproj`:
  linked production placement code, selection/state and source guards.
- `tests/SiloPlayer.NativeRegressionTests/PlaybackToastNativeFixture.cs` and
  `Program.cs`: focused offscreen native selector.
- This parity record.

## Verification

Local results on this source:

- **Unit tests:** all 7,545 pass, including `ToastHostPlacementTests`,
  `FullscreenToastSourceTests`, `FullscreenStateSyncSourceTests` and
  `UserOnlyClientArchitectureTests`.
- **`playback-toasts` native:** passes. It uses a real offscreen video HWND
  with the production toast service and window, and checks the same card and
  timer objects, native ownership, non-activation, an unchanged foreground,
  hit regions, Undo running once across moves, expiry, and owner destruction
  and cleanup.
- **`toast-diagnostics` and `native-subtitle-dialogs` native:** pass.

Not certified:

- real monitor fullscreen with libmpv media;
- physical Undo clicks during fullscreen;
- focus staying on playback on real hardware;
- mixed-DPI and multi-monitor setups.
