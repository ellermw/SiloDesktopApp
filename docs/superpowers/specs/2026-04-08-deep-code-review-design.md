# Deep Code Review — Design Spec

**Date:** 2026-04-08
**Approach:** Surgical Strikes (severity-ordered passes, one commit per pass)

## Constraints (Non-Negotiable)

1. **No appearance changes** — zero visual/layout/styling modifications
2. **No functionality changes** — all user-facing behavior stays identical
3. **Speed is #1 priority** — instant page loads, instant playback start/resume
4. **Stability** — fix memory leaks that degrade long-session performance
5. **Lightweight** — remove dead code and unused dependencies

## Pass 1: Fix Memory Leaks (Critical)

Event handlers attached to long-lived services are never unsubscribed, preventing garbage collection of page instances. Memory grows with every page navigation.

### 1a. MainWindow.xaml.cs

Three leaked subscriptions in the constructor:
- Line 56: `playerService.StateChanged += OnPlayerStateChanged` — never unsubscribed
- Line 59: `this.SizeChanged += anonymous lambda` — can't unsubscribe anonymous
- Line 65: `AppWindow.Changed += anonymous lambda` — same issue

**Fix:** Convert anonymous lambdas to named handlers. Add `this.Closed += (_, _) => { unsubscribe all three };`

### 1b. ItemDetailPage.xaml.cs

Three leaked subscriptions:
- Line 30: `ViewModel.PropertyChanged += anonymous` — added in constructor, never removed
- Line 64: `playerService.StateChanged += anonymous` — flag prevents duplicate *within* one instance but handler lives forever
- Line 40: `root.SizeChanged += anonymous` — attached to XamlRoot, never removed

**Fix:** Convert to named handlers. Add `OnNavigatedFrom` to unsubscribe all three. Store `playerService` as field.

### 1c. HomePage.xaml.cs

Three leaked subscriptions:
- Lines 36-37: `CollectionChanged` on `FeaturedSections` and `Sections`
- Line 40: `ViewModel.PropertyChanged`

**Fix:** Convert to named handlers. Add `OnNavigatedFrom` to unsubscribe.

### 1d. PlayerService.cs — mpv event accumulation

Lines 402-500: `WireMpvEvents()` subscribes 8 events on `_mpv` every time `PlayAsync` runs. Old handlers are never removed.

**Fix:** Store handlers as fields. Add `UnwireMpvEvents()`. Call it before `WireMpvEvents()`.

Line 1272: `_webSocket.CommandReceived += HandleWebSocketCommand` not unsubscribed in `DisconnectWebSocket()`.

**Fix:** Add `-= HandleWebSocketCommand` before nulling.

### 1e. CancellationTokenSource leaks

- SearchViewModel.cs lines 41-42: `_searchCts?.Cancel()` without `Dispose()`
- PosterCard.xaml.cs lines 44-45: `_loadCts?.Cancel()` without `Dispose()`

**Fix:** Add `.Dispose()` before creating new CTS.

## Pass 2: API Response Caching

### 2a. Home sections cache (HomeViewModel)

Line 50: `await _homeApi.GetSectionsAsync()` called on every navigation to Home.

**Fix:** Add timestamp check. If loaded within last 5 minutes, skip API call. Data is already in `FeaturedSections` and `Sections` collections. Add force-refresh for explicit reloads.

### 2b. Libraries cache (CatalogApi)

`GetLibrariesAsync()` called by MainViewModel and SettingsViewModel. Libraries never change during a session.

**Fix:** Add in-memory cache with 5-minute TTL at the CatalogApi level. Add `InvalidateLibraryCache()`.

### 2c. Parallel prefetch on login

After profile selection, fire `GetSectionsAsync()` and `GetLibrariesAsync()` in parallel before navigation animation completes. Store in cache for instant display.

## Pass 3: Dead Code & File Cleanup

### 3a. Delete unused files
- `libs/mpv/scripts/osc.lua` — only `continuum-osc.lua` is loaded (MpvPlayer.cs:289)
- `libs/mpv/scripts/osc-modern.lua` — never referenced
- `tests/ContinuumPlayer.Core.Tests/UnitTest1.cs` — empty placeholder
- `src/ContinuumPlayer/Assets/ContinuumIcon.svg` — zero references

### 3b. Remove unused NuGet dependency
- `Microsoft.Web.WebView2` in ContinuumPlayer.csproj — no usage anywhere in codebase

### 3c. Archive GenerateIcons tool
- Move `tools/GenerateIcons.csproj` to `F:\ContinuumPlayerDocs\tools\`

### 3d. Move planning docs
- Move `docs/superpowers/` (18 files) to `F:\ContinuumPlayerDocs\superpowers\`
- Preserve `plans/` and `specs/` subdirectory structure
- Remove empty `docs/` directory from repo

## Pass 4: Code Quality & Null Safety

### 4a. Split PlayerService.PlayAsync (194 lines)

Extract into focused methods:
- `PrepareWatchDetail()` — fetch and validate watch detail
- `SelectAndConfigureVersion()` — pick file version, handle quality prefs
- `BuildStreamUrl()` — construct URL with token and seek params
- `InitializeMpvPlayback()` — mpv loadfile, subtitle setup, OSC config
- `SetupPostPlaybackHooks()` — wire events, connect WebSocket

Each method stays under 40 lines. Same logic, better structure.

### 4b. Fix Versions null crash

PlayerService.cs line 260: `Versions = watchDetail.Versions.ToList()` — crashes if null.
Line 264: `watchDetail.Versions ?? new List<FileVersion>()` — guards against null.

**Fix:** `Versions = watchDetail.Versions?.ToList() ?? [];`

### 4c. Deduplicate token URL building

Token appended identically in 4 locations:
- PlaybackManager.StartSessionAsync (line 79-80)
- PlaybackManager.GetSubtitleUrls (line 138-139)
- HlsProxy.ServeManifest
- PlaybackWebSocket constructor

**Fix:** Add `UrlHelper.AppendToken(string url, string? token)` static helper in Core.

### 4d. WebSocket cleanup

PlayerService.DisconnectWebSocket() sets `_webSocket = null` without unsubscribing.

**Fix:** Add `_webSocket.CommandReceived -= HandleWebSocketCommand` before nulling.

## Commit Plan

| Order | Pass | Commit | Risk |
|-------|------|--------|------|
| 1 | 1a-1e | `fix: plug event handler memory leaks across pages and services` | Low |
| 2 | 2a-2c | `perf: add API response caching for home sections and libraries` | Low |
| 3 | 3a-3d | `cleanup: remove dead code, unused deps, archive planning docs` | None |
| 4 | 4a-4d | `refactor: split PlayAsync, fix null safety, deduplicate URL building` | Low |

## Success Criteria

- No visual or behavioral changes to any feature
- Returning to Home page is instant (cached)
- Memory usage stays flat during extended browsing sessions
- Playback start path unchanged (already optimized)
- ~200KB smaller deployment (unused scripts + SVG + WebView2 removed)
- Build succeeds on all three platforms (x86, x64, ARM64)
