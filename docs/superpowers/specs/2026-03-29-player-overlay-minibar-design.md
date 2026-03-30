# Player Overlay & Mini Bar Design Spec

## Goal

Replace the current page-navigation player with a window-level overlay player and a persistent mini bar. Playback becomes decoupled from the navigation frame so browsing pages are never destroyed during playback and never need to reload when the user returns.

## Architecture

The player is **not a page**. It is two UI layers in MainWindow that sit above the NavigationView:

```
MainWindow.xaml Grid (3 layers, back to front):

  Layer 1: NavigationView (sidebar + ContentFrame)
    └─ Frame navigates: Home, Library, ItemDetail, Settings, Admin, etc.
    └─ Bottom padding adjusts when mini bar is visible

  Layer 2: PlayerOverlay (UserControl, full-window, initially Collapsed)
    ├─ Video Image (WriteableBitmap from mpv, Stretch=Uniform)
    ├─ Title bar (minimize/back, title, subtitle)
    ├─ Controls overlay (seek bar, play/pause, volume, fullscreen, etc.)
    └─ Supplementary overlays (loading, error, stats, skip intro, next episode)

  Layer 3: MiniPlayerBar (UserControl, bottom-docked, initially Collapsed)
    ├─ Mini video Image (~120x68, live WriteableBitmap, with expand chevron overlay)
    ├─ Title + subtitle text
    ├─ Transport controls (prev, play/pause, next)
    ├─ Seekable progress bar (thin)
    ├─ Volume control
    └─ Close button (stops playback)
```

## Player State Machine

Four states with defined transitions:

```
States: Idle, Expanded, Fullscreen, Minimized

Idle → Expanded:       User clicks Play on any content
Expanded → Minimized:  Back/Escape/minimize button
Expanded → Fullscreen: Fullscreen button or F key
Fullscreen → Expanded: Escape or fullscreen button (do NOT resize app window)
Minimized → Expanded:  Click mini video or expand chevron
Minimized → Idle:      Close button on bar (stops playback, cleans up)
Expanded → Idle:       Close/stop button on player
```

**Escape behavior:**
- In Fullscreen: exits fullscreen → Expanded (player stays full-window at app size)
- In Expanded: minimizes to bar → Minimized
- In Minimized: no effect on player

**Visibility rules per state:**

| State | NavigationView | PlayerOverlay | MiniPlayerBar | Nav bottom padding |
|-------|---------------|---------------|---------------|--------------------|
| Idle | Visible | Collapsed | Collapsed | None |
| Expanded | Hidden (pane) | Visible | Collapsed | None |
| Fullscreen | Hidden (pane) | Visible (Win32 FS) | Collapsed | None |
| Minimized | Visible | Collapsed | Visible | ~64px |

## PlayerService (Singleton)

Central coordinator. Owns MpvPlayer lifecycle and state machine. Registered as singleton in DI.

```
PlayerService
├─ Fields:
│   MpvPlayer _mpv (created on first play, reused until Close)
│   PlaybackManager _playbackManager
│   PlayerState State { get; private set; }
│   string? ContentId, Title, Subtitle, PosterUrl
│   double Position, Duration
│   bool IsPaused
│
├─ Methods:
│   Task PlayAsync(string contentId, bool fromStart = false)
│   void Minimize()       → Expanded/Fullscreen → Minimized
│   void Expand()         → Minimized → Expanded
│   void EnterFullscreen()
│   void ExitFullscreen()
│   void Close()          → any → Idle (disposes session, NOT mpv)
│   void Dispose()        → destroys mpv instance
│
├─ Events:
│   Action<PlayerState> StateChanged
│   Action<byte[], int, int, int> FrameReady (forwarded from MpvPlayer)
│   Action<double> PositionChanged
│   Action<double> DurationChanged
│   Action<bool> PauseChanged
│   Action PlaybackEnded
```

**MpvPlayer lifecycle:**
- Created lazily on first `PlayAsync` call. NOT created at app startup.
- Reused across content switches (`loadfile` command swaps content without recreating mpv).
- Destroyed only on `Dispose()` (app shutdown) or after extended idle.
- `Close()` stops the server session and resets state to Idle but does NOT destroy the mpv instance. This means the next Play is instant -- no mpv initialization overhead.

**Content switching:** If playback is active and `PlayAsync` is called with a different contentId, the service stops the current server session, starts a new one, and calls `mpv.LoadFile(newUrl)`. No mpv teardown/recreate.

## Frame Rendering

**One active render target at a time.** mpv renders at the resolution of whichever target is visible:

- **Expanded/Fullscreen:** mpv renders at overlay dimensions (e.g. 1920x1080). FrameReady copies into the overlay's WriteableBitmap.
- **Minimized:** `UpdateRenderSize(120, 68)` is called. mpv renders at mini resolution. FrameReady copies into the bar's WriteableBitmap.

**On state transition:** Call `UpdateRenderSize` with the new target dimensions BEFORE toggling visibility. This ensures the first frame at the new size is ready.

**Performance rules:**
- Never copy frames into a hidden bitmap.
- Never render at full resolution when minimized.
- The IBufferByteAccess COM trick (direct pointer copy) from current PlayerPage is preserved for zero-copy bitmap updates.

## Mini Bar Layout

```
┌──────────────────────────────────────────────────────────────────────┐
│ [live video 120x68] │ Title - S01E03         │ ◄◄  ▶❚❚  ►► │ ━━●━━ │ 🔊 │ ✕ │
│   (^ expand icon)   │ Subtitle / Show Name   │   controls   │ seek  │vol │close│
└──────────────────────────────────────────────────────────────────────┘
Height: ~64px
```

- Click the video thumbnail → Expand to full player
- Expand chevron (^) overlay on the video → same action
- Close (✕) → stops playback entirely, state → Idle
- All controls are interactive (play/pause, seek, volume, prev/next)

## Navigation Integration

**Starting playback:** Delete `PlayerPage.xaml` and `PlayerPage.xaml.cs`. Every call site that currently does `Navigate<PlayerPage>(contentId)` changes to `PlayerService.PlayAsync(contentId)`. Examples:
- ItemDetailPage "Play" button
- HistoryPage "Resume" button
- Home "Continue Watching" cards
- Any "Play from start" action

**The Frame never navigates for playback.** If you're on ItemDetailPage and hit Play, the Frame stays on ItemDetailPage. The overlay appears on top. Minimize → ItemDetailPage is right there, no reload.

**NavigationView bottom padding:** When MiniPlayerBar is visible, the NavigationView's content area and sidebar need bottom margin so the bar doesn't overlap the profile/settings/admin buttons. MainWindow listens to `PlayerService.StateChanged` and toggles a margin.

## Fullscreen

Uses the Win32 approach (already implemented):
- `SetWindowLongPtrW` to strip `WS_OVERLAPPEDWINDOW`
- `SetWindowPos` to cover full monitor including taskbar
- Restore saved style + rect on exit
- Escape in fullscreen → Expanded (NOT Minimized)
- Fullscreen logic moves from PlayerPage into PlayerService (or a FullscreenHelper)

## Performance Constraints

- **MpvPlayer created lazily** -- zero cost until first Play.
- **MpvPlayer reused** -- no teardown/recreate between content switches.
- **Single bitmap copy per frame** -- only the visible target receives frames.
- **Mini render at mini resolution** -- mpv renders 120x68 when minimized, not 1080p downscaled.
- **IBufferByteAccess direct pointer** -- no managed array copy for bitmap updates.
- **Pre-allocated GCHandles** in render loop (already implemented).
- **Render lock held during mpv_render_context_render** to prevent buffer races (already implemented).
- **No page navigation for player** -- Frame pages are never destroyed/recreated by playback.
- **StateChanged triggers minimal UI work** -- toggle Visibility, update padding, nothing more.
- **PlayerOverlay and MiniPlayerBar are lightweight UserControls** -- no heavy initialization, no data loading, just display elements bound to PlayerService state.

## File Structure

### New files:
- `src/ContinuumPlayer/Services/PlayerService.cs` -- singleton state machine + mpv coordinator
- `src/ContinuumPlayer/Controls/PlayerOverlay.xaml` + `.xaml.cs` -- full-window player UI
- `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml` + `.xaml.cs` -- bottom bar with live video

### Modified files:
- `src/ContinuumPlayer/MainWindow.xaml` -- add PlayerOverlay and MiniPlayerBar layers
- `src/ContinuumPlayer/MainWindow.xaml.cs` -- listen to PlayerService.StateChanged, toggle visibility/padding
- `src/ContinuumPlayer/App.xaml.cs` -- register PlayerService as singleton
- All call sites that navigate to PlayerPage (ItemDetailPage, HistoryPage, HomePage, etc.)

### Deleted files:
- `src/ContinuumPlayer/Views/PlayerPage.xaml`
- `src/ContinuumPlayer/Views/PlayerPage.xaml.cs`
- `src/ContinuumPlayer/ViewModels/PlayerViewModel.cs` (logic absorbed by PlayerService)

## Migration Path

PlayerPage.xaml.cs currently contains ~600 lines of:
- Playback initialization (watch detail, version selection, session start)
- MpvPlayer setup (Initialize, FrameReady handler, bitmap copy)
- UI timer (position updates to controls)
- Hide timer (auto-hide controls on inactivity)
- Fullscreen logic (Win32 style/position)
- Keyboard shortcuts
- Subtitle/audio track switching
- Version switching
- Skip intro/credits logic
- Stats overlay

This migrates as follows:
- **Playback init, mpv lifecycle, progress reporting, content switching** → PlayerService
- **Video display, control overlays, keyboard handling, hide timer, stats** → PlayerOverlay
- **Fullscreen Win32 logic** → PlayerService (it owns the window state)
- **Mini transport controls, mini video, expand/close** → MiniPlayerBar
- **PlayerViewModel properties** → absorbed into PlayerService (Position, Duration, IsPaused, etc.)
