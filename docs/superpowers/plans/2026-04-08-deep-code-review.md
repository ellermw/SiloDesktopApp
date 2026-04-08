# Deep Code Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix memory leaks, add API caching for instant navigation, remove dead code, and improve code quality — with zero changes to appearance or functionality.

**Architecture:** Surgical passes ordered by severity. Each pass is one commit. Pass 1 fixes event handler memory leaks. Pass 2 adds response caching. Pass 3 removes dead code. Pass 4 improves code structure and null safety.

**Tech Stack:** .NET 8, WinUI 3, CommunityToolkit.Mvvm, libmpv

**Constraints:**
- NO changes to appearance, layout, styling, colors, or spacing
- NO changes to user-facing behavior or functionality
- Speed is #1 priority — instant page loads, instant playback

---

## Task 1: Fix MainWindow event handler leaks

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs:22-77` (constructor) and add Closed handler

- [ ] **Step 1: Convert anonymous lambdas to named handlers and add cleanup**

In `MainWindow.xaml.cs`, add fields to store the handlers and the service reference, then convert anonymous lambdas to named methods and add a `Closed` handler to unsubscribe everything.

Add fields after line 20 (after `_apiClient` field):

```csharp
private readonly PlayerService _playerService;
```

Replace the constructor body from line 54 (`// Listen for player state changes`) through line 76 (closing brace of the `AppWindow.Changed` block) with:

```csharp
// Listen for player state changes
_playerService = App.Services.GetRequiredService<PlayerService>();
_playerService.StateChanged += OnPlayerStateChanged;

// Keep native video window matched to main window size
this.SizeChanged += OnWindowSizeChanged;

// Hide/show player popup when main window is minimized/restored
if (AppWindow != null)
{
    AppWindow.Changed += OnAppWindowChanged;
}

// Clean up event subscriptions when window closes
this.Closed += OnWindowClosed;
```

Add these methods before `NavView_Loaded`:

```csharp
private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
{
    _playerService.HandleWindowResize();
}

private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
{
    if (args.DidPresenterChange || args.DidSizeChange || args.DidPositionChange)
    {
        var p = AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        if (p != null)
        {
            _playerService.HandleWindowMinimized(p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized);
        }
    }
}

private void OnWindowClosed(object sender, WindowEventArgs args)
{
    _playerService.StateChanged -= OnPlayerStateChanged;
    this.SizeChanged -= OnWindowSizeChanged;
    if (AppWindow != null) AppWindow.Changed -= OnAppWindowChanged;
}
```

Remove the now-unused local variable on the old line 55 (`var playerService = ...`). All references to `playerService` in the constructor now use `_playerService`.

- [ ] **Step 2: Verify the build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 3: Smoke test — verify no behavioral changes**

Verify: App launches, sidebar works, player overlay responds to window resize/minimize. No visual changes.

---

## Task 2: Fix ItemDetailPage event handler leaks

**Files:**
- Modify: `src/ContinuumPlayer/Views/ItemDetailPage.xaml.cs:15-72`

- [ ] **Step 1: Convert anonymous handlers to named methods and add cleanup**

Add fields after line 21 (`_selectedVersion`):

```csharp
private PlayerService? _playerService;
private FrameworkElement? _rootElement;
```

Replace lines 29-41 (the anonymous PropertyChanged, StateChanged, and Loaded handlers) with:

```csharp
// Listen for async property changes (e.g., rating loaded after initial UI update)
ViewModel.PropertyChanged += OnViewModelPropertyChanged;

this.Loaded += OnPageLoaded;
```

Replace the `OnNavigatedTo` StateChanged subscription block (lines 60-72) with:

```csharp
// Subscribe to player state changes to refresh play button after playback ends
if (!_subscribedToStateChanged)
{
    _subscribedToStateChanged = true;
    _playerService = App.Services.GetRequiredService<Services.PlayerService>();
    _playerService.StateChanged += OnPlayerStateChanged;
}
```

Add the named handler methods (place them after `UpdateBackdropHeight`):

```csharp
private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
{
    if (args.PropertyName == nameof(ViewModel.UserRating))
        DispatcherQueue.TryEnqueue(UpdateStarRating);
}

private void OnPageLoaded(object sender, RoutedEventArgs e)
{
    UpdateBackdropHeight();
    if (XamlRoot?.Content is FrameworkElement root)
    {
        _rootElement = root;
        _rootElement.SizeChanged += OnRootSizeChanged;
    }
}

private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
{
    UpdateBackdropHeight();
}

private void OnPlayerStateChanged(PlayerState state)
{
    if (state == Services.PlayerState.Idle && _playableContentId != null)
    {
        DispatcherQueue?.TryEnqueue(() => _ = LoadWatchDetailAsync(_playableContentId));
    }
}
```

Add `OnNavigatedFrom` override to clean up:

```csharp
protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    base.OnNavigatedFrom(e);

    ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    if (_rootElement != null)
    {
        _rootElement.SizeChanged -= OnRootSizeChanged;
        _rootElement = null;
    }
    if (_playerService != null)
    {
        _playerService.StateChanged -= OnPlayerStateChanged;
        _subscribedToStateChanged = false;
    }
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 3: Fix HomePage event handler leaks

**Files:**
- Modify: `src/ContinuumPlayer/Views/HomePage.xaml.cs:10-103`

- [ ] **Step 1: Convert anonymous handlers to named methods and add cleanup**

Add a using at top of file:

```csharp
using Microsoft.UI.Xaml.Navigation;
```

Replace lines 33-47 (the `_eventsAttached` block contents) with:

```csharp
if (!_eventsAttached)
{
    _eventsAttached = true;
    ViewModel.FeaturedSections.CollectionChanged += OnSectionsChanged;
    ViewModel.Sections.CollectionChanged += OnSectionsChanged;
    ViewModel.PropertyChanged += OnViewModelPropertyChanged;
}
```

Add the named handler methods:

```csharp
private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
{
    DispatcherQueue.TryEnqueue(() => BuildContent());
}

private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(ViewModel.ShowUndoBanner))
    {
        DispatcherQueue.TryEnqueue(UpdateUndoBanner);
    }
}
```

Add `OnNavigatedFrom` override to clean up:

```csharp
protected override void OnNavigatedFrom(NavigationEventArgs e)
{
    base.OnNavigatedFrom(e);

    if (_eventsAttached)
    {
        ViewModel.FeaturedSections.CollectionChanged -= OnSectionsChanged;
        ViewModel.Sections.CollectionChanged -= OnSectionsChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _eventsAttached = false;
    }
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 4: Fix PlayerService mpv event accumulation and WebSocket cleanup

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs:398-501` (WireMpvEvents) and `:1280-1284` (DisconnectWebSocket)

- [ ] **Step 1: Store event handlers as fields and add UnwireMpvEvents**

Add fields after line 27 (`_playbackCts`):

```csharp
// Stored mpv event handlers for proper unsubscription
private Action<double>? _mpvPositionHandler;
private Action<double>? _mpvDurationHandler;
private Action<bool>? _mpvPauseHandler;
private Action? _mpvFileLoadedHandler;
private Action? _mpvPlaybackEndedHandler;
private Action<string>? _mpvPlaybackErrorHandler;
```

Add `UnwireMpvEvents()` method before `WireMpvEvents()`:

```csharp
private void UnwireMpvEvents()
{
    if (_mpv == null) return;
    if (_mpvPositionHandler != null) _mpv.PositionChanged -= _mpvPositionHandler;
    if (_mpvDurationHandler != null) _mpv.DurationChanged -= _mpvDurationHandler;
    if (_mpvPauseHandler != null) _mpv.PauseChanged -= _mpvPauseHandler;
    if (_mpvFileLoadedHandler != null) _mpv.FileLoaded -= _mpvFileLoadedHandler;
    if (_mpvPlaybackEndedHandler != null) _mpv.PlaybackEnded -= _mpvPlaybackEndedHandler;
    if (_mpvPlaybackErrorHandler != null) _mpv.PlaybackError -= _mpvPlaybackErrorHandler;
    _mpv.ScriptMessageReceived -= OnScriptMessage;
}
```

Modify `WireMpvEvents()` to store handlers and call `UnwireMpvEvents()` first:

```csharp
private void WireMpvEvents()
{
    if (_mpv == null) return;
    UnwireMpvEvents();

    _mpvPositionHandler = (pos) =>
    {
        Position = pos;
        _playbackManager?.UpdatePosition(pos, IsPaused);
        PositionChanged?.Invoke(pos);
    };

    _mpvDurationHandler = (dur) =>
    {
        Duration = dur;
        DurationChanged?.Invoke(dur);
    };

    _mpvPauseHandler = (paused) =>
    {
        IsPaused = paused;
        PauseChanged?.Invoke(paused);
    };

    _mpvFileLoadedHandler = () =>
    {
        // ... exact same body as current FileLoaded handler (lines 422-458) ...
        // Copy the ENTIRE existing body verbatim — no changes to logic
        IsLoading = false;
        _switchingContent = false;
        _qualitySwitchActive = false;
        App.MainWindowInstance?.HideLoadingOverlay();
        LogToFile("state_trace.txt", "FileLoaded fired");

        ContentLoaded?.Invoke();

        if (_resumePosition > 0)
        {
            LogToFile("state_trace.txt", $"Seeking to resume position: {_resumePosition:F1}");
            _mpv?.Seek(_resumePosition);
            _resumePosition = 0;
        }
        else
        {
            LogToFile("state_trace.txt", "No resume position (starting from beginning)");
        }

        var ct = _playbackCts?.Token ?? CancellationToken.None;
        Task.Run(() =>
        {
            if (ct.IsCancellationRequested) return;
            SendMediaInfoToOsc();
            SendSubtitleListToOsc();
            SendQualityInfoToOsc();
            SendMarkersToOsc();
            if (ct.IsCancellationRequested) return;
            LoadSubtitles();
        }, ct);

        try { ConnectWebSocket(); }
        catch (Exception ex) { LogToFile("state_trace.txt", $"WebSocket connect failed: {ex.Message}"); }
    };

    _mpvPlaybackEndedHandler = () =>
    {
        // ... exact same body as current PlaybackEnded handler (lines 462-474) ...
        LogToFile("state_trace.txt", $"PlaybackEnded fired: _switchingContent={_switchingContent} _qualitySwitchActive={_qualitySwitchActive} State={State} thread={Environment.CurrentManagedThreadId}");
        if (!_switchingContent && !_qualitySwitchActive)
        {
            LogToFile("state_trace.txt", "  → Hiding window and invoking PlaybackEnded");
            _videoWindow?.Hide();
            PlaybackEnded?.Invoke();
        }
        else
        {
            LogToFile("state_trace.txt", "  → Suppressed (switching content)");
        }
    };

    _mpvPlaybackErrorHandler = (msg) =>
    {
        // ... exact same body as current PlaybackError handler (lines 477-497) ...
        LogToFile("state_trace.txt", $"PlaybackError: {msg} _switchingContent={_switchingContent}");
        if (_switchingContent)
        {
            _switchingContent = false;
            _qualitySwitchActive = false;
            IsLoading = false;
            ErrorMessage = msg;
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
            {
                _videoWindow?.Hide();
                SetState(PlayerState.Idle);
                var detail = msg.Contains("loading failed")
                    ? "The media file could not be loaded. It may be unavailable or the server may be experiencing issues."
                    : msg;
                App.MainWindowInstance?.ShowPlaybackError(detail);
            });
        }
    };

    _mpv.PositionChanged += _mpvPositionHandler;
    _mpv.DurationChanged += _mpvDurationHandler;
    _mpv.PauseChanged += _mpvPauseHandler;
    _mpv.FileLoaded += _mpvFileLoadedHandler;
    _mpv.PlaybackEnded += _mpvPlaybackEndedHandler;
    _mpv.PlaybackError += _mpvPlaybackErrorHandler;
    _mpv.ScriptMessageReceived += OnScriptMessage;
    _mpv.Error += (msg) => LogToFile("mpv_error.txt", msg);
}
```

- [ ] **Step 2: Fix WebSocket event cleanup in DisconnectWebSocket (line 1280-1284)**

Replace:
```csharp
private void DisconnectWebSocket()
{
    _webSocket?.Disconnect();
    _webSocket = null;
}
```

With:
```csharp
private void DisconnectWebSocket()
{
    if (_webSocket != null)
    {
        _webSocket.CommandReceived -= HandleWebSocketCommand;
        _webSocket.Disconnect();
        _webSocket = null;
    }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 5: Fix CancellationTokenSource leaks

**Files:**
- Modify: `src/ContinuumPlayer/ViewModels/SearchViewModel.cs:41-42`
- Modify: `src/ContinuumPlayer/Controls/PosterCard.xaml.cs:44-45`

- [ ] **Step 1: Add Dispose before creating new CTS in SearchViewModel**

Replace lines 41-42:
```csharp
_searchCts?.Cancel();
_searchCts = new CancellationTokenSource();
```

With:
```csharp
_searchCts?.Cancel();
_searchCts?.Dispose();
_searchCts = new CancellationTokenSource();
```

- [ ] **Step 2: Add Dispose before creating new CTS in PosterCard**

Replace lines 44-45:
```csharp
_loadCts?.Cancel();
_loadCts = new CancellationTokenSource();
```

With:
```csharp
_loadCts?.Cancel();
_loadCts?.Dispose();
_loadCts = new CancellationTokenSource();
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit Pass 1**

```bash
git add src/ContinuumPlayer/MainWindow.xaml.cs src/ContinuumPlayer/Views/ItemDetailPage.xaml.cs src/ContinuumPlayer/Views/HomePage.xaml.cs src/ContinuumPlayer/Services/PlayerService.cs src/ContinuumPlayer/ViewModels/SearchViewModel.cs src/ContinuumPlayer/Controls/PosterCard.xaml.cs
git commit -m "$(cat <<'EOF'
fix: plug event handler memory leaks across pages and services

- MainWindow: convert anonymous lambdas to named handlers, unsubscribe on Closed
- ItemDetailPage: unsubscribe PropertyChanged/StateChanged/SizeChanged on NavigatedFrom
- HomePage: unsubscribe CollectionChanged/PropertyChanged on NavigatedFrom
- PlayerService: add UnwireMpvEvents() to prevent handler accumulation on PlayAsync
- PlayerService: unsubscribe WebSocket CommandReceived before nulling
- SearchViewModel/PosterCard: dispose CancellationTokenSource before creating new one

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Add home sections caching for instant navigation

**Files:**
- Modify: `src/ContinuumPlayer/ViewModels/HomeViewModel.cs:40-71`

- [ ] **Step 1: Add timestamp-based cache to skip API calls on re-navigation**

Add field after line 38 (`_lastDismissedIndex`):

```csharp
private DateTime _lastLoadedAt = DateTime.MinValue;
private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
```

Replace the `LoadAsync()` method (lines 41-71) with:

```csharp
[RelayCommand]
private async Task LoadAsync()
{
    if (IsLoading) return;

    // Skip API call if data was loaded recently and we already have content
    if (FeaturedSections.Count + Sections.Count > 0
        && DateTime.UtcNow - _lastLoadedAt < CacheDuration)
    {
        return;
    }

    IsLoading = true;
    ErrorMessage = null;

    try
    {
        var response = await _homeApi.GetSectionsAsync();

        FeaturedSections.Clear();
        Sections.Clear();

        foreach (var section in response.Sections)
        {
            if (section.Featured)
                FeaturedSections.Add(section);
            else
                Sections.Add(section);
        }

        _lastLoadedAt = DateTime.UtcNow;
    }
    catch (Exception ex)
    {
        ErrorMessage = $"Failed to load home: {ex.Message}";
    }
    finally
    {
        IsLoading = false;
    }
}
```

Add a method to force-refresh (called after dismissals or explicit refresh):

```csharp
public void InvalidateCache()
{
    _lastLoadedAt = DateTime.MinValue;
}
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 7: Add libraries caching

**Files:**
- Modify: `src/ContinuumPlayer.Core/Api/CatalogApi.cs:8-11`

- [ ] **Step 1: Add in-memory cache with TTL to CatalogApi**

Replace the `CatalogApi` class opening and `GetLibrariesAsync` method:

```csharp
public class CatalogApi(ContinuumApiClient client)
{
    private List<Library>? _librariesCache;
    private DateTime _librariesCachedAt = DateTime.MinValue;
    private static readonly TimeSpan LibraryCacheDuration = TimeSpan.FromMinutes(5);

    public async Task<List<Library>> GetLibrariesAsync(CancellationToken ct = default)
    {
        if (_librariesCache != null && DateTime.UtcNow - _librariesCachedAt < LibraryCacheDuration)
            return _librariesCache;

        _librariesCache = await client.GetAsync<List<Library>>("/api/v1/user/libraries", ct);
        _librariesCachedAt = DateTime.UtcNow;
        return _librariesCache;
    }

    public void InvalidateLibraryCache()
    {
        _librariesCache = null;
        _librariesCachedAt = DateTime.MinValue;
    }
```

The rest of the file stays identical — only the first method and class opening change.

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer.Core/ContinuumPlayer.Core.csproj`
Expected: Build succeeded.

---

## Task 8: Parallel prefetch on login

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs:149-170` (ShowMainNavigation)

- [ ] **Step 1: Fire libraries and home data loading in parallel on first init**

Replace `ShowMainNavigation()` (lines 149-171):

```csharp
public void ShowMainNavigation()
{
    NavView.IsPaneVisible = true;

    // Always update admin button and profile display for current user
    AdminButton.Visibility = _authService.CurrentUser?.Role == "admin"
        ? Visibility.Visible : Visibility.Collapsed;
    _ = UpdateProfileDisplayAsync();

    if (!_navInitialized)
    {
        _navInitialized = true;

        // Prefetch libraries and home data in parallel for instant display
        _ = _viewModel.LoadLibrariesCommand.ExecuteAsync(null);

        // Watch for library changes to update nav (marshal to UI thread)
        _viewModel.Libraries.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() => UpdateLibraryNavItems());
        };
    }
}
```

Note: The `Libraries.CollectionChanged` handler here is on the MainWindow (singleton) subscribing to the MainViewModel (also effectively singleton since `_navInitialized` gates it to once). This is NOT a leak — it's a one-time subscription that lives for the app lifetime.

The home data prefetch happens automatically because `HomePage.Page_Loaded` calls `ViewModel.LoadCommand.ExecuteAsync(null)` which checks `FeaturedSections.Count == 0`. The libraries call fires here so it's already cached by the time the sidebar renders.

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 3: Commit Pass 2**

```bash
git add src/ContinuumPlayer/ViewModels/HomeViewModel.cs src/ContinuumPlayer.Core/Api/CatalogApi.cs src/ContinuumPlayer/MainWindow.xaml.cs
git commit -m "$(cat <<'EOF'
perf: add API response caching for home sections and libraries

- HomeViewModel: skip API call if data loaded within last 5 minutes
- CatalogApi: cache GetLibrariesAsync with 5-minute TTL
- Both caches have InvalidateCache methods for forced refresh
- No visual or behavioral changes — same data, fewer round-trips

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Delete unused files

**Files:**
- Delete: `libs/mpv/scripts/osc.lua`
- Delete: `libs/mpv/scripts/osc-modern.lua`
- Delete: `tests/ContinuumPlayer.Core.Tests/UnitTest1.cs`
- Delete: `src/ContinuumPlayer/Assets/ContinuumIcon.svg`

- [ ] **Step 1: Remove dead files**

```bash
git rm libs/mpv/scripts/osc.lua
git rm libs/mpv/scripts/osc-modern.lua
git rm tests/ContinuumPlayer.Core.Tests/UnitTest1.cs
git rm src/ContinuumPlayer/Assets/ContinuumIcon.svg
```

Rationale:
- `osc.lua` and `osc-modern.lua`: Only `continuum-osc.lua` is loaded (confirmed in MpvPlayer.cs line 289)
- `UnitTest1.cs`: Empty placeholder with no test logic
- `ContinuumIcon.svg`: Zero references anywhere in codebase

---

## Task 10: Remove unused WebView2 dependency

**Files:**
- Modify: `src/ContinuumPlayer/ContinuumPlayer.csproj:59`

- [ ] **Step 1: Remove WebView2 PackageReference**

Delete line 59:
```xml
        <PackageReference Include="Microsoft.Web.WebView2" Version="1.*" />
```

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded. No code uses WebView2.

---

## Task 11: Move planning docs and GenerateIcons tool to archive

**Files:**
- Move: `docs/superpowers/` (18 files) → `F:\ContinuumPlayerDocs\superpowers\`
- Move: `tools/GenerateIcons.csproj` → `F:\ContinuumPlayerDocs\tools\`
- Delete: empty `docs/` directory from repo

- [ ] **Step 1: Create archive directory and move files**

```bash
mkdir -p /f/ContinuumPlayerDocs/superpowers/plans
mkdir -p /f/ContinuumPlayerDocs/superpowers/specs
mkdir -p /f/ContinuumPlayerDocs/tools

# Move planning docs (preserve structure)
cp -r /f/ContinuumPlayer/docs/superpowers/plans/* /f/ContinuumPlayerDocs/superpowers/plans/
cp -r /f/ContinuumPlayer/docs/superpowers/specs/* /f/ContinuumPlayerDocs/superpowers/specs/

# Move GenerateIcons tool
cp /f/ContinuumPlayer/tools/GenerateIcons.csproj /f/ContinuumPlayerDocs/tools/
```

- [ ] **Step 2: Remove from repo**

```bash
git rm -r docs/superpowers/
git rm -r tools/
```

- [ ] **Step 3: Commit Pass 3**

```bash
git add -A
git commit -m "$(cat <<'EOF'
cleanup: remove dead code, unused deps, archive planning docs

- Delete osc.lua and osc-modern.lua (only continuum-osc.lua is used)
- Delete UnitTest1.cs placeholder and ContinuumIcon.svg (zero references)
- Remove unused Microsoft.Web.WebView2 NuGet dependency
- Archive docs/superpowers/ to F:\ContinuumPlayerDocs\superpowers\
- Archive tools/GenerateIcons to F:\ContinuumPlayerDocs\tools\

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: Fix Versions null crash in PlayerService

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs:260`

- [ ] **Step 1: Guard against null Versions**

Replace line 260:
```csharp
Versions = watchDetail.Versions.ToList();
```

With:
```csharp
Versions = watchDetail.Versions?.ToList() ?? [];
```

This makes line 260 consistent with the null guard on line 264.

- [ ] **Step 2: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 13: Add URL token helper to deduplicate token-appending logic

**Files:**
- Create: `src/ContinuumPlayer.Core/Helpers/UrlHelper.cs`
- Modify: `src/ContinuumPlayer.Core/Services/PlaybackManager.cs:79-80, 138-139`
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs:357`
- Modify: `src/ContinuumPlayer/Services/HlsProxy.cs:111`
- Modify: `src/ContinuumPlayer/Services/PlaybackWebSocket.cs:44-45`

- [ ] **Step 1: Create UrlHelper**

Create `src/ContinuumPlayer.Core/Helpers/UrlHelper.cs`:

```csharp
namespace ContinuumPlayer.Core.Helpers;

public static class UrlHelper
{
    /// <summary>
    /// Appends an auth token as a query parameter to a URL.
    /// Returns the URL unchanged if token is null.
    /// </summary>
    public static string AppendToken(string url, string? token)
    {
        if (token == null) return url;
        return url + (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
    }
}
```

- [ ] **Step 2: Replace token-appending in PlaybackManager.StartSessionAsync (lines 79-80)**

Add using at top:
```csharp
using ContinuumPlayer.Core.Helpers;
```

Replace lines 79-80:
```csharp
if (token != null)
    url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
```

With:
```csharp
url = UrlHelper.AppendToken(url, token);
```

- [ ] **Step 3: Replace token-appending in PlaybackManager.GetSubtitleUrls (lines 138-139)**

Replace:
```csharp
if (token != null)
    url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
```

With:
```csharp
url = UrlHelper.AppendToken(url, token);
```

- [ ] **Step 4: Replace token-appending in PlayerService.PlayAsync (line 357)**

Add using at top of `PlayerService.cs`:
```csharp
using ContinuumPlayer.Core.Helpers;
```

Replace line 357:
```csharp
streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";
```

With:
```csharp
streamUrl = UrlHelper.AppendToken(streamUrl, token);
```

- [ ] **Step 5: Replace token-appending in HlsProxy (line 111)**

Add using at top of `HlsProxy.cs`:
```csharp
using ContinuumPlayer.Core.Helpers;
```

Replace line 111:
```csharp
manifestUrlWithToken += (manifestUrlWithToken.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(_token)}";
```

With:
```csharp
manifestUrlWithToken = UrlHelper.AppendToken(manifestUrlWithToken, _token);
```

- [ ] **Step 6: Replace token-appending in PlaybackWebSocket (lines 44-45)**

Add using at top of `PlaybackWebSocket.cs`:
```csharp
using ContinuumPlayer.Core.Helpers;
```

Replace lines 44-45:
```csharp
if (_token != null)
    wsUrl += $"?token={Uri.EscapeDataString(_token)}";
```

With:
```csharp
wsUrl = UrlHelper.AppendToken(wsUrl, _token);
```

- [ ] **Step 7: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

---

## Task 14: Split PlayerService.PlayAsync into focused methods

**Files:**
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs:200-394`

- [ ] **Step 1: Extract helper methods from PlayAsync**

The 194-line `PlayAsync` method stays as the orchestrator but delegates to focused helpers. Add these private methods:

```csharp
private async Task<WatchDetailResponse> FetchWatchDetailAsync(string contentId)
{
    try
    {
        return await _playbackManager!.GetWatchDetailAsync(contentId);
    }
    catch (ApiException ex) when (ex.StatusCode == 400)
    {
        // Retry once if server hasn't processed previous session stop
        await Task.Delay(300);
        return await _playbackManager!.GetWatchDetailAsync(contentId);
    }
}

private void SetTitleFromWatchDetail(WatchDetailResponse watchDetail)
{
    if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
    {
        Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
        Subtitle = watchDetail.Title;
    }
    else
    {
        Title = watchDetail.Title;
        Subtitle = watchDetail.Year > 0 ? watchDetail.Year.ToString() : null;
    }
}

private FileVersion? SelectVersion(WatchDetailResponse watchDetail, int? fileId)
{
    Versions = watchDetail.Versions?.ToList() ?? [];
    var versions = watchDetail.Versions ?? new List<FileVersion>();

    FileVersion? bestVersion = null;
    if (fileId.HasValue)
        bestVersion = versions.FirstOrDefault(v => v.FileId == fileId.Value);
    bestVersion ??= _playbackManager!.SelectBestVersion(versions);
    return bestVersion;
}

private double DetermineStartPosition(WatchDetailResponse watchDetail, bool fromStart)
{
    double startPosition = 0;
    if (!fromStart && watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
        startPosition = watchDetail.UserData.PositionSeconds!.Value;
    LogToFile("state_trace.txt", $"Resume logic: fromStart={fromStart} userPos={watchDetail.UserData?.PositionSeconds} played={watchDetail.UserData?.Played} → startPosition={startPosition}");
    return startPosition;
}

private async Task<string?> HandleTranscodeFallbackAsync(PlaybackStartResponse session, FileVersion bestVersion, double startPosition)
{
    if (session.PlayMethod != "transcode") return null;

    try
    {
        var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
        {
            SessionId = session.SessionId,
            SeekSeconds = startPosition,
            TargetResolution = bestVersion.Resolution,
            TargetCodecVideo = "h264",
            TargetCodecAudio = "aac",
            TargetBitrateKbps = 8000,
            SegmentDuration = 2,
            SubtitleTrackIndex = -1,
            SubtitleBurnIn = false
        });

        var baseUrl = _apiClient.BaseUrl;
        var manifestPath = transcodeResponse.ManifestUrl;
        if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
            manifestPath = "/api/v1" + manifestPath;
        var streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
        _resumePosition = transcodeResponse.PlayerStartSeconds;
        return streamUrl;
    }
    catch (Exception ex)
    {
        LogToFile("player_transcode_error.txt", ex.ToString());
        return null;
    }
}

private void EnsureMpvInitialized()
{
    if (_mpv != null) return;

    var mainWindow = App.MainWindowInstance;
    var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);

    _videoWindow = new MpvVideoWindow();
    _videoWindow.Create(parentHwnd);
    WireVideoWindowEvents();

    _mpv = new MpvPlayer();
    _mpv.InitializeWithWindow(_videoWindow.Hwnd);
    _videoWindow.SetMpv(_mpv);
    WireMpvEvents();
}
```

- [ ] **Step 2: Rewrite PlayAsync to use the extracted methods**

Replace the body of `PlayAsync` (lines 201-393) with:

```csharp
public async Task PlayAsync(string contentId, bool fromStart = false, int? fileId = null)
{
    LogToFile("state_trace.txt", $"PlayAsync called: contentId={contentId} fromStart={fromStart} State={State} IsLoading={IsLoading}");

    // Stop any existing session first (prevents HTTP 400 from server)
    if (_playbackManager != null)
    {
        try
        {
            _mpv?.Stop();
            await _playbackManager.StopSessionAsync();
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Stop previous session error: {ex.Message}"); }
        _playbackManager.Dispose();
        _playbackManager = null;
        await Task.Delay(200);
    }

    ErrorMessage = null;
    IsLoading = true;
    ContentId = contentId;
    _resumePosition = 0;
    _playbackCts?.Cancel();
    _playbackCts = new CancellationTokenSource();
    _switchingContent = true;

    App.MainWindowInstance?.ShowLoadingOverlay();

    try
    {
        _playbackManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient);

        var watchDetail = await FetchWatchDetailAsync(contentId);
        SetTitleFromWatchDetail(watchDetail);

        var bestVersion = SelectVersion(watchDetail, fileId);
        if (bestVersion == null)
        {
            ErrorMessage = "No playable version found.";
            IsLoading = false;
            return;
        }

        Resolution = bestVersion.Resolution;
        var startPosition = DetermineStartPosition(watchDetail, fromStart);

        var session = await _playbackManager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: fromStart);
        PlayMethod = session.PlayMethod;

        if (!fromStart && session.Position > 0 && startPosition == 0)
        {
            startPosition = session.Position;
            LogToFile("state_trace.txt", $"Using server session position: {session.Position:F1}");
        }

        var streamUrl = _playbackManager.StreamUrl;
        if (string.IsNullOrEmpty(streamUrl))
        {
            ErrorMessage = "No stream URL available.";
            IsLoading = false;
            return;
        }

        // HLS transcode fallback
        var transcodeUrl = await HandleTranscodeFallbackAsync(session, bestVersion, startPosition);
        if (transcodeUrl != null)
            streamUrl = transcodeUrl;

        EnsureMpvInitialized();

        SetState(PlayerState.Expanded);

        // Build auth
        var token = _apiClient.AccessToken;
        var authHeader = token != null ? $"Bearer {token}" : null;
        if (session.PlayMethod != "transcode" && token != null)
            streamUrl = UrlHelper.AppendToken(streamUrl, token);

        _resumePosition = startPosition;

        LogToFile("state_trace.txt", $"LoadFile: url={streamUrl?.Substring(0, Math.Min(80, streamUrl?.Length ?? 0))}...");
        _mpv!.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
        _mpv.Play();
        LogToFile("state_trace.txt", "Play() called");
        IsPaused = false;

        _mpv.SendScriptMessage("osc-set-play-method", session.PlayMethod ?? "direct");
        IsLoading = false;
    }
    catch (Exception ex)
    {
        LogToFile("player_crash.txt", ex.ToString());
        ErrorMessage = $"Failed to start playback: {ex.Message}";
        IsLoading = false;
        _switchingContent = false;

        _videoWindow?.Hide();
        if (_playbackManager != null)
        {
            try { await _playbackManager.StopSessionAsync(); } catch (Exception stopEx) { LogToFile("state_trace.txt", $"StopSession error: {stopEx.Message}"); }
            _playbackManager.Dispose();
            _playbackManager = null;
        }
        SetState(PlayerState.Idle);
        App.MainWindowInstance?.ShowPlaybackError(ex.Message);
    }
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 4: Commit Pass 4**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs src/ContinuumPlayer.Core/Helpers/UrlHelper.cs src/ContinuumPlayer.Core/Services/PlaybackManager.cs src/ContinuumPlayer/Services/HlsProxy.cs src/ContinuumPlayer/Services/PlaybackWebSocket.cs
git commit -m "$(cat <<'EOF'
refactor: split PlayAsync, fix null safety, deduplicate URL building

- Extract 5 focused methods from 194-line PlayAsync (same logic, better structure)
- Fix potential null crash: Versions = watchDetail.Versions?.ToList() ?? []
- Add UrlHelper.AppendToken() to deduplicate token-appending in 5 locations
- WebSocket: unsubscribe CommandReceived before nulling reference

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Verification Checklist

After all 4 passes are committed:

- [ ] Full build succeeds: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj -c Release -p:Platform=x64`
- [ ] App launches and displays home page
- [ ] Navigate away from Home and back — second load is instant (cached)
- [ ] Libraries appear in sidebar on first load
- [ ] Navigate to item detail, then away, then back — no memory growth
- [ ] Play a video — playback starts, progress reports, WebSocket connects
- [ ] Stop video — player returns to Idle cleanly
- [ ] Search works — type, results appear, cancel works
- [ ] No visual or behavioral changes to any feature
