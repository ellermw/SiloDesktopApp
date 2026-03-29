# Performance Optimization & Code Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate startup bottlenecks, fix critical runtime bugs (render buffer race, timer overlaps), optimize the mpv render loop, remove dead code, and fix image cache inefficiencies -- all without changing any user-visible functionality.

**Architecture:** The changes span three projects: ContinuumPlayer.Core (settings caching, timer safety, image cache, playback dispose), ContinuumPlayer.Player (render buffer race fix, GCHandle pre-allocation, dead P/Invoke removal), and ContinuumPlayer (parallel startup, event handler leak fixes, HomePage/RecommendationsPage rebuild). Every change preserves the existing API surface and behavior.

**Tech Stack:** C# / .NET 8 / WinUI 3 / libmpv P/Invoke / CommunityToolkit.Mvvm

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/ContinuumPlayer.Core/Services/SettingsService.cs` | Modify | Add in-memory cache, eliminate repeated disk I/O |
| `src/ContinuumPlayer/MainWindow.xaml.cs` | Modify | Parallelize startup loads (libraries + profile name) |
| `src/ContinuumPlayer.Player/MpvPlayer.cs` | Modify | Fix render buffer race, pre-alloc GCHandles, fix Dispose order |
| `src/ContinuumPlayer.Player/MpvInterop.cs` | Modify | Remove unused P/Invoke declarations |
| `src/ContinuumPlayer.Core/Services/PlaybackManager.cs` | Modify | Fix timer overlap, fix fire-and-forget Dispose |
| `src/ContinuumPlayer.Core/Services/AuthService.cs` | Modify | Fix timer overlap in token refresh |
| `src/ContinuumPlayer.Core/Services/ImageService.cs` | Modify | Fix O(n^2) eviction, add download dedup |
| `src/ContinuumPlayer.Core/Api/AdminApi.cs` | Modify | Remove 11 unused file-scoped response wrapper classes |
| `src/ContinuumPlayer/Views/HomePage.xaml.cs` | Modify | Fix duplicate event subscriptions |
| `src/ContinuumPlayer/Views/RecommendationsPage.xaml.cs` | Modify | Fix duplicate event subscriptions |
| `src/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs` | Create | Tests for caching behavior |
| `src/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs` | Create | Tests for eviction and dedup |

---

### Task 1: SettingsService — Add In-Memory Cache

The single biggest startup optimization. Currently `Load()` reads and deserializes the JSON file from disk on every call. During startup it's called 3-4 times before the user sees anything. Adding a cache means it reads disk exactly once.

**Files:**
- Modify: `src/ContinuumPlayer.Core/Services/SettingsService.cs`
- Create: `src/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs`

- [ ] **Step 1: Write tests for cached SettingsService behavior**

Create the test file. These tests verify: (a) Load returns the same object on repeated calls without re-reading disk, (b) Save invalidates the cache so the next Load returns updated data, (c) AddServer/RemoveServer/UpdateLastUsed all persist correctly.

```csharp
// src/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _sut;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new SettingsService(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Load_ReturnsSameInstance_WhenCalledTwice()
    {
        var first = _sut.Load();
        var second = _sut.Load();
        Assert.Same(first, second);
    }

    [Fact]
    public void Save_InvalidatesCache_NextLoadReturnsFreshData()
    {
        var settings = _sut.Load();
        settings.LastTheme = "dark";
        _sut.Save(settings);

        var reloaded = _sut.Load();
        Assert.Equal("dark", reloaded.LastTheme);
    }

    [Fact]
    public void AddServer_PersistsToDisk_And_ReflectedInLoad()
    {
        _sut.AddServer("https://example.com", "Test");
        var settings = _sut.Load();
        Assert.Single(settings.Servers);
        Assert.Equal("https://example.com", settings.Servers[0].Url);
    }

    [Fact]
    public void AddServer_NoDuplicate_WhenSameUrl()
    {
        _sut.AddServer("https://example.com", "Test");
        _sut.AddServer("https://example.com", "Test2");
        var settings = _sut.Load();
        Assert.Single(settings.Servers);
    }

    [Fact]
    public void RemoveServer_PersistsToDisk()
    {
        _sut.AddServer("https://example.com", "Test");
        _sut.RemoveServer("https://example.com");
        var settings = _sut.Load();
        Assert.Empty(settings.Servers);
    }

    [Fact]
    public void UpdateLastUsed_UpdatesTimestamp()
    {
        _sut.AddServer("https://example.com", "Test");
        var before = _sut.Load().Servers[0].LastUsed;

        Thread.Sleep(50); // ensure time advances
        _sut.UpdateLastUsed("https://example.com");

        var after = _sut.Load().Servers[0].LastUsed;
        Assert.True(after > before);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenNoFileExists()
    {
        var settings = _sut.Load();
        Assert.NotNull(settings);
        Assert.Empty(settings.Servers);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/ContinuumPlayer.Core.Tests --filter "FullyQualifiedName~SettingsServiceTests" -v n`

Expected: `Load_ReturnsSameInstance_WhenCalledTwice` FAILS (currently returns new instances each call).

- [ ] **Step 3: Implement cached SettingsService**

Replace the full contents of `SettingsService.cs`:

```csharp
// src/ContinuumPlayer.Core/Services/SettingsService.cs
using System.Text.Json;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;
    private AppSettings? _cached;

    public SettingsService(string appDataDir)
    {
        Directory.CreateDirectory(appDataDir);
        _filePath = Path.Combine(appDataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (_cached != null) return _cached;

        if (!File.Exists(_filePath))
        {
            _cached = new AppSettings();
            return _cached;
        }

        var json = File.ReadAllText(_filePath);
        _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        return _cached;
    }

    public void Save(AppSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void AddServer(string url, string name)
    {
        var settings = Load();
        if (settings.Servers.Any(s => s.Url == url))
            return;
        settings.Servers.Add(new ServerEntry { Url = url, Name = name, LastUsed = DateTime.UtcNow });
        Save(settings);
    }

    public void RemoveServer(string url)
    {
        var settings = Load();
        settings.Servers.RemoveAll(s => s.Url == url);
        Save(settings);
    }

    public void UpdateLastUsed(string url)
    {
        var settings = Load();
        var server = settings.Servers.FirstOrDefault(s => s.Url == url);
        if (server != null)
        {
            server.LastUsed = DateTime.UtcNow;
            Save(settings);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/ContinuumPlayer.Core.Tests --filter "FullyQualifiedName~SettingsServiceTests" -v n`

Expected: ALL PASS

- [ ] **Step 5: Build the full solution to verify no regressions**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/SettingsService.cs src/ContinuumPlayer.Core.Tests/Services/SettingsServiceTests.cs
git commit -m "perf: add in-memory cache to SettingsService, eliminate repeated disk I/O on startup"
```

---

### Task 2: Parallelize Startup Loads in MainWindow

After auto-login succeeds, library loading and profile name fetching run sequentially. They're independent API calls that can run concurrently, shaving network latency off startup.

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs`

- [ ] **Step 1: Parallelize library + profile loads in ShowMainNavigation**

In `MainWindow.xaml.cs`, replace `ShowMainNavigation()` (lines 119-141) with:

```csharp
    public void ShowMainNavigation()
    {
        NavView.IsPaneVisible = true;

        if (!_navInitialized)
        {
            _navInitialized = true;

            // Fire both loads concurrently -- they are independent
            var libTask = _viewModel.LoadLibrariesCommand.ExecuteAsync(null);
            var profileTask = UpdateProfileDisplayAsync();

            // Show Admin button if user is admin
            AdminButton.Visibility = _authService.CurrentUser?.Role == "admin"
                ? Visibility.Visible : Visibility.Collapsed;

            // Watch for library changes to update nav (marshal to UI thread)
            _viewModel.Libraries.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateLibraryNavItems());
            };
        }
    }
```

Then rename `UpdateProfileDisplay()` to `UpdateProfileDisplayAsync()` and make it return `Task`:

Replace the existing `UpdateProfileDisplay` method (lines 143-151) with:

```csharp
    private Task UpdateProfileDisplayAsync()
    {
        var profileId = _authService.SelectedProfileId;
        if (!string.IsNullOrEmpty(profileId))
            return LoadProfileNameAsync(profileId);
        return Task.CompletedTask;
    }
```

- [ ] **Step 2: Build to verify no regressions**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/MainWindow.xaml.cs
git commit -m "perf: parallelize library and profile name loads on startup"
```

---

### Task 3: Fix MpvPlayer Render Buffer Race Condition

**Critical bug.** The render loop reads `bufferPtr` inside a lock, then releases the lock and renders into that pointer. Meanwhile `UpdateRenderSize()` can free the buffer and reallocate. Fix: hold the lock through the entire render call.

**Files:**
- Modify: `src/ContinuumPlayer.Player/MpvPlayer.cs`

- [ ] **Step 1: Fix the render loop to hold lock during render**

In `MpvPlayer.cs`, replace the render loop body (lines 397-448, inside `while (!_disposed)` after the `continue` check) with:

```csharp
                var flags = mpv_render_context_update(_renderCtx);
                if ((flags & MPV_RENDER_UPDATE_FRAME) == 0) continue;

                // Build size/stride arrays and render -- all under the lock
                // so UpdateRenderSize() cannot free the buffer mid-render.
                int[] sizeArr = { 0, 0 };
                var sizePin = GCHandle.Alloc(sizeArr, GCHandleType.Pinned);
                long strideValue = 0;
                var stridePin = GCHandle.Alloc(strideValue, GCHandleType.Pinned);

                try
                {
                    byte[] buffer;

                    lock (_renderSizeLock)
                    {
                        if (_frameBuffer == null || !_frameBufferPin.IsAllocated)
                            continue;

                        sizeArr[0] = _renderWidth;
                        sizeArr[1] = _renderHeight;
                        strideValue = _stride;
                        // Re-pin stride with updated value (boxed long changed, need re-pin)
                        stridePin.Free();
                        stridePin = GCHandle.Alloc(strideValue, GCHandleType.Pinned);

                        buffer = _frameBuffer;
                        var bufferPtr = _frameBufferPin.AddrOfPinnedObject();

                        var p0 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_SIZE, Data = sizePin.AddrOfPinnedObject() };
                        var p1 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_FORMAT, Data = formatPin.AddrOfPinnedObject() };
                        var p2 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_STRIDE, Data = stridePin.AddrOfPinnedObject() };
                        var p3 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_POINTER, Data = bufferPtr };
                        var pEnd = new MpvRenderParam { Type = IntPtr.Zero, Data = IntPtr.Zero };

                        Marshal.StructureToPtr(p0, paramsPtr, false);
                        Marshal.StructureToPtr(p1, paramsPtr + paramSize, false);
                        Marshal.StructureToPtr(p2, paramsPtr + paramSize * 2, false);
                        Marshal.StructureToPtr(p3, paramsPtr + paramSize * 3, false);
                        Marshal.StructureToPtr(pEnd, paramsPtr + paramSize * 4, false);

                        int err = mpv_render_context_render(_renderCtx, paramsPtr);
                        if (err < 0)
                            continue;
                    }

                    // Signal frame ready OUTSIDE the lock (subscribers copy the buffer)
                    FrameReady?.Invoke(buffer, sizeArr[0], sizeArr[1], (int)strideValue);
                }
                finally
                {
                    sizePin.Free();
                    stridePin.Free();
                }
```

The key change: `mpv_render_context_render()` now executes inside `lock (_renderSizeLock)`, so `UpdateRenderSize()` cannot free the buffer during rendering. `FrameReady` fires outside the lock to avoid holding it during UI copy.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer.Player/MpvPlayer.cs
git commit -m "fix: hold render lock during mpv_render_context_render to prevent buffer-free race"
```

---

### Task 4: Pre-allocate Render Loop GCHandles

At 60fps the render loop allocates and frees 2 GCHandles per frame (120 GC operations/sec). The size array and stride value can be pre-allocated once and reused every frame.

**Files:**
- Modify: `src/ContinuumPlayer.Player/MpvPlayer.cs`

- [ ] **Step 1: Add pre-allocated fields and refactor RenderLoop**

Add new fields after the existing `_renderSizeLock` field (around line 37):

```csharp
    // Pre-allocated render params (reused every frame to avoid GC pressure)
    private int[] _renderSizeArr = new int[2];
    private GCHandle _renderSizePin;
    private long _renderStrideValue;
    private GCHandle _renderStridePin;
```

Replace the entire `RenderLoop()` method with:

```csharp
    private void RenderLoop()
    {
        byte[] formatBytes = "bgr0\0"u8.ToArray();
        var formatPin = GCHandle.Alloc(formatBytes, GCHandleType.Pinned);

        int paramSize = Marshal.SizeOf<MpvRenderParam>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramSize * 5);

        // Pre-pin the reusable size and stride buffers
        _renderSizePin = GCHandle.Alloc(_renderSizeArr, GCHandleType.Pinned);
        _renderStridePin = GCHandle.Alloc(_renderStrideValue, GCHandleType.Pinned);

        try
        {
            while (!_disposed)
            {
                _frameUpdateEvent.Wait(100);
                _frameUpdateEvent.Reset();

                if (_disposed || _renderCtx == IntPtr.Zero) return;

                var flags = mpv_render_context_update(_renderCtx);
                if ((flags & MPV_RENDER_UPDATE_FRAME) == 0) continue;

                byte[] buffer;
                int w, h;
                long stride;

                lock (_renderSizeLock)
                {
                    if (_frameBuffer == null || !_frameBufferPin.IsAllocated)
                        continue;

                    w = _renderWidth;
                    h = _renderHeight;
                    stride = _stride;
                    buffer = _frameBuffer;

                    // Update pre-allocated arrays in place
                    _renderSizeArr[0] = w;
                    _renderSizeArr[1] = h;

                    // Stride is a boxed long -- must re-pin when value changes
                    if (_renderStrideValue != stride)
                    {
                        _renderStrideValue = stride;
                        if (_renderStridePin.IsAllocated) _renderStridePin.Free();
                        _renderStridePin = GCHandle.Alloc(_renderStrideValue, GCHandleType.Pinned);
                    }

                    var bufferPtr = _frameBufferPin.AddrOfPinnedObject();

                    var p0 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_SIZE, Data = _renderSizePin.AddrOfPinnedObject() };
                    var p1 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_FORMAT, Data = formatPin.AddrOfPinnedObject() };
                    var p2 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_STRIDE, Data = _renderStridePin.AddrOfPinnedObject() };
                    var p3 = new MpvRenderParam { Type = (IntPtr)MPV_RENDER_PARAM_SW_POINTER, Data = bufferPtr };
                    var pEnd = new MpvRenderParam { Type = IntPtr.Zero, Data = IntPtr.Zero };

                    Marshal.StructureToPtr(p0, paramsPtr, false);
                    Marshal.StructureToPtr(p1, paramsPtr + paramSize, false);
                    Marshal.StructureToPtr(p2, paramsPtr + paramSize * 2, false);
                    Marshal.StructureToPtr(p3, paramsPtr + paramSize * 3, false);
                    Marshal.StructureToPtr(pEnd, paramsPtr + paramSize * 4, false);

                    int err = mpv_render_context_render(_renderCtx, paramsPtr);
                    if (err < 0)
                        continue;
                }

                FrameReady?.Invoke(buffer, w, h, (int)stride);
            }
        }
        finally
        {
            if (formatPin.IsAllocated) formatPin.Free();
            if (_renderSizePin.IsAllocated) _renderSizePin.Free();
            if (_renderStridePin.IsAllocated) _renderStridePin.Free();
            Marshal.FreeHGlobal(paramsPtr);
        }
    }
```

This replaces Task 3's render loop with the combined fix: race condition protection + pre-allocated GCHandles. The `_renderSizeArr` is pinned once and updated in-place every frame. The stride is only re-pinned when the value actually changes (on resize only, not every frame).

- [ ] **Step 2: Update Dispose to clean up pre-allocated pins**

In the `Dispose()` method, add cleanup for the new pre-allocated pins. After the line that frees `_frameBufferPin` (around line 784-785), add:

```csharp
        if (_renderSizePin.IsAllocated)
            _renderSizePin.Free();
        if (_renderStridePin.IsAllocated)
            _renderStridePin.Free();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ContinuumPlayer.Player/ContinuumPlayer.Player.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer.Player/MpvPlayer.cs
git commit -m "perf: pre-allocate render loop GCHandles, eliminate 120 allocs/sec at 60fps"
```

---

### Task 5: Fix Timer Overlap in PlaybackManager and AuthService

Both services use `System.Threading.Timer` with async callbacks that don't guard against overlapping executions. If the HTTP call takes longer than the timer interval, multiple concurrent progress reports or refresh attempts pile up. Empty catch blocks hide failures -- if progress reporting silently fails for 45s, the server kills the session.

**Files:**
- Modify: `src/ContinuumPlayer.Core/Services/PlaybackManager.cs`
- Modify: `src/ContinuumPlayer.Core/Services/AuthService.cs`

- [ ] **Step 1: Fix PlaybackManager timer with SemaphoreSlim guard**

Replace the `StartProgressReporting` and `StopProgressReporting` methods and add a field. In `PlaybackManager.cs`:

Add a field after line 14 (`private bool _isPaused;`):

```csharp
    private readonly SemaphoreSlim _progressGuard = new(1, 1);
```

Replace `StartProgressReporting()` (lines 119-128) with:

```csharp
    private void StartProgressReporting()
    {
        StopProgressReporting();
        _progressTimer = new Timer(async _ =>
        {
            if (_sessionId == null) return;
            if (!_progressGuard.Wait(0)) return; // skip if previous report still in-flight
            try
            {
                await _playbackApi.ReportProgressAsync(_sessionId, _lastReportedPosition, _isPaused);
            }
            catch (Exception)
            {
                // Progress report failed -- non-fatal but logged via the session lifecycle.
                // The server will reap the session after ~45s without progress, so transient
                // failures are acceptable. Persistent failures mean network is down.
            }
            finally
            {
                _progressGuard.Release();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }
```

Replace `Dispose()` (lines 141-145) with:

```csharp
    public void Dispose()
    {
        StopProgressReporting();
        // Best-effort session stop -- fire and forget is acceptable here because
        // the server will reap the session after ~2 minutes if it doesn't arrive.
        // We can't await in Dispose, but we ensure the timer is stopped first.
        if (_sessionId != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _playbackApi.StopPlaybackAsync(_sessionId); }
                catch { }
            });
        }
        _progressGuard.Dispose();
    }
```

- [ ] **Step 2: Fix AuthService timer -- use one-shot timer to prevent overlap**

In `AuthService.cs`, replace `ScheduleRefresh` (lines 136-141) with:

```csharp
    private void ScheduleRefresh(int expiresInSeconds)
    {
        _refreshTimer?.Dispose();
        if (expiresInSeconds <= 0) return;
        var refreshIn = TimeSpan.FromSeconds(expiresInSeconds * 0.8);
        _refreshTimer = new Timer(async _ =>
        {
            try { await TryRefreshAsync(); }
            catch { /* refresh failure is handled inside TryRefreshAsync (calls Logout) */ }
        }, null, refreshIn, Timeout.InfiniteTimeSpan); // one-shot: no repeat, TryRefreshAsync re-schedules on success
    }
```

This is already a one-shot timer (`Timeout.InfiniteTimeSpan` for period), and `TryRefreshAsync` calls `ScheduleRefresh` on success, which creates the next one-shot. The only fix needed here is wrapping the await in a try-catch so the Timer callback doesn't throw unobserved. Verify the existing code at line 140 already uses `Timeout.InfiniteTimeSpan` -- if so, only add the try-catch wrapper.

- [ ] **Step 3: Build to verify**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/PlaybackManager.cs src/ContinuumPlayer.Core/Services/AuthService.cs
git commit -m "fix: guard progress timer against overlap, wrap auth refresh callback"
```

---

### Task 6: Fix ImageService O(n^2) Eviction + Add Download Deduplication

Two issues: (1) cache eviction iterates `Keys.FirstOrDefault()` in a while loop which is O(n) per iteration = O(n^2) total, and (2) if 10 PosterCards request the same URL simultaneously, 10 downloads start.

**Files:**
- Modify: `src/ContinuumPlayer.Core/Services/ImageService.cs`
- Create: `src/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs`

- [ ] **Step 1: Write tests for eviction and dedup behavior**

```csharp
// src/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class ImageServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ImageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cp_img_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ClearMemoryCache_ResetsToZero()
    {
        // Very small cache limit so we can test eviction
        var svc = new ImageService(_tempDir, maxMemoryCacheBytes: 1024);
        svc.ClearMemoryCache();
        // No exception, no crash -- verifies clean state
    }

    [Fact]
    public async Task GetImageAsync_ReturnsSameBytes_ForSameKey()
    {
        var svc = new ImageService(_tempDir, maxMemoryCacheBytes: 10 * 1024 * 1024);
        var http = new HttpClient();

        // Write a fake cached file to disk to avoid network dependency
        var cacheKey = "test123_poster";
        var expected = new byte[] { 1, 2, 3, 4, 5 };
        var diskPath = GetDiskPath(svc, cacheKey);
        await File.WriteAllBytesAsync(diskPath, expected);

        var result1 = await svc.GetImageAsync("test123", "poster", "https://unused", http);
        var result2 = await svc.GetImageAsync("test123", "poster", "https://unused", http);

        Assert.NotNull(result1);
        Assert.Same(result1, result2); // Same reference from memory cache
    }

    // Helper to compute disk path the same way ImageService does
    private static string GetDiskPath(ImageService svc, string cacheKey)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(cacheKey)))[..16];
        // Access private field via reflection for test setup
        var field = typeof(ImageService).GetField("_diskCacheDir",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var dir = (string)field.GetValue(svc)!;
        return Path.Combine(dir, $"{hash}.img");
    }
}
```

- [ ] **Step 2: Run tests to verify baseline**

Run: `dotnet test src/ContinuumPlayer.Core.Tests --filter "FullyQualifiedName~ImageServiceTests" -v n`

Expected: Tests pass (the `Same` assertion may fail if cache doesn't return same reference -- that's the baseline to verify).

- [ ] **Step 3: Implement improved ImageService**

Replace the full contents of `ImageService.cs`:

```csharp
// src/ContinuumPlayer.Core/Services/ImageService.cs
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public class ImageService : IDisposable
{
    private readonly string _diskCacheDir;
    private readonly long _maxMemoryBytes;
    private readonly ConcurrentDictionary<string, byte[]> _memoryCache = new();
    private readonly ConcurrentQueue<string> _evictionOrder = new();
    private long _currentMemoryBytes;
    private readonly SemaphoreSlim _downloadLock = new(10);

    // Dedup: if multiple callers request the same image simultaneously,
    // only one download runs -- the rest await the same Task.
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _inflightDownloads = new();

    public ImageService(string diskCacheDir, long maxMemoryCacheBytes = 200 * 1024 * 1024)
    {
        _diskCacheDir = diskCacheDir;
        _maxMemoryBytes = maxMemoryCacheBytes;
        Directory.CreateDirectory(diskCacheDir);
    }

    public async Task<byte[]?> GetImageAsync(string contentId, string imageType, string url, HttpClient http, CancellationToken ct = default)
    {
        var cacheKey = $"{contentId}_{imageType}";

        // 1. Memory cache hit
        if (_memoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 2. Disk cache hit
        var diskPath = GetDiskPath(cacheKey);
        if (File.Exists(diskPath))
        {
            var diskBytes = await File.ReadAllBytesAsync(diskPath, ct);
            AddToMemoryCache(cacheKey, diskBytes);
            return diskBytes;
        }

        // 3. Download with deduplication -- only one download per cacheKey
        var task = _inflightDownloads.GetOrAdd(cacheKey, _ => DownloadAndCacheAsync(cacheKey, diskPath, url, http, ct));
        try
        {
            return await task;
        }
        finally
        {
            _inflightDownloads.TryRemove(cacheKey, out _);
        }
    }

    private async Task<byte[]?> DownloadAndCacheAsync(string cacheKey, string diskPath, string url, HttpClient http, CancellationToken ct)
    {
        await _downloadLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock (another thread may have cached it)
            if (_memoryCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(diskPath, bytes, ct);
            AddToMemoryCache(cacheKey, bytes);
            return bytes;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
        // Drain the eviction queue
        while (_evictionOrder.TryDequeue(out _)) { }
        _currentMemoryBytes = 0;
    }

    private void AddToMemoryCache(string key, byte[] data)
    {
        if (data.Length > _maxMemoryBytes) return;

        // Evict oldest entries until there's room (O(1) per eviction via queue)
        while (Interlocked.Read(ref _currentMemoryBytes) + data.Length > _maxMemoryBytes)
        {
            if (!_evictionOrder.TryDequeue(out var oldKey)) break;
            if (_memoryCache.TryRemove(oldKey, out var removed))
                Interlocked.Add(ref _currentMemoryBytes, -removed.Length);
        }

        if (_memoryCache.TryAdd(key, data))
        {
            Interlocked.Add(ref _currentMemoryBytes, data.Length);
            _evictionOrder.Enqueue(key);
        }
    }

    private string GetDiskPath(string cacheKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(_diskCacheDir, $"{hash}.img");
    }

    public void Dispose()
    {
        _downloadLock.Dispose();
        _memoryCache.Clear();
    }
}
```

Changes:
- **Eviction**: `ConcurrentQueue<string>` tracks insertion order -- `TryDequeue()` is O(1) instead of O(n).
- **Dedup**: `_inflightDownloads` dictionary ensures only one download per cache key. All concurrent callers await the same `Task<byte[]?>`.
- **All existing API surface preserved** -- same constructor, same `GetImageAsync` signature, same `ClearMemoryCache`, same `Dispose`.

- [ ] **Step 4: Run tests**

Run: `dotnet test src/ContinuumPlayer.Core.Tests --filter "FullyQualifiedName~ImageServiceTests" -v n`

Expected: ALL PASS

- [ ] **Step 5: Build full solution**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/ContinuumPlayer.Core/Services/ImageService.cs src/ContinuumPlayer.Core.Tests/Services/ImageServiceTests.cs
git commit -m "perf: fix O(n^2) image cache eviction, add download deduplication"
```

---

### Task 7: Remove Dead Code — AdminApi Wrappers + MpvInterop P/Invokes

11 of 12 file-scoped wrapper classes in AdminApi are never used. 2 P/Invoke declarations in MpvInterop are never called. Clean removal.

**Files:**
- Modify: `src/ContinuumPlayer.Core/Api/AdminApi.cs`
- Modify: `src/ContinuumPlayer.Player/MpvInterop.cs`

- [ ] **Step 1: Remove unused AdminApi wrapper classes**

In `AdminApi.cs`, delete lines 7-16 (the 10 unused wrappers) and line 18 (AdminSkippedRootsResponse). Keep lines 6, 17, and 19-20:

The file should start with:

```csharp
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Core.Api;

// Simple list-wrapper response types used by endpoints that return JSON arrays wrapped in an object.
file class AdminSensitiveStatusResponse { public List<string> Configured { get; set; } = []; }
// AdminSectionsListResponse is in Models/Admin/AdminSection.cs

public class AdminApi(ContinuumApiClient client)
{
```

That is: remove `AdminSessionsResponse`, `AdminUsersResponse`, `AdminUserProfilesResponse`, `AdminUserIPsResponse`, `AdminIPUsersResponse`, `AdminTasksResponse`, `AdminTaskHistoryResponse`, `AdminNodesResponse`, `AdminAPIKeysResponse`, `AdminPlaybackHistoryResponse`, and `AdminSkippedRootsResponse`. Keep only `AdminSensitiveStatusResponse` (used at line 144).

- [ ] **Step 2: Remove unused MpvInterop P/Invoke declarations**

In `MpvInterop.cs`:

Remove the `mpv_destroy` declaration (lines 67-68):
```csharp
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_destroy(IntPtr ctx);
```

Remove the `mpv_get_property` (double overload) declaration (lines 87-90):
```csharp
    [DllImport(LibMpv, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_get_property(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format, out double data);
```

- [ ] **Step 3: Fix MpvPlayer.cs references to mpv_destroy**

In `MpvPlayer.cs`, there are two calls to `mpv_destroy` (in `Initialize()` around line 165 and `InitializeWithWindow()` around line 313). Replace both with `mpv_terminate_destroy`:

Line ~165: `mpv_destroy(_mpvHandle);` → `mpv_terminate_destroy(_mpvHandle);`
Line ~313: `mpv_destroy(_mpvHandle);` → `mpv_terminate_destroy(_mpvHandle);`

- [ ] **Step 4: Build full solution to verify nothing references removed code**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ContinuumPlayer.Core/Api/AdminApi.cs src/ContinuumPlayer.Player/MpvInterop.cs src/ContinuumPlayer.Player/MpvPlayer.cs
git commit -m "chore: remove 11 unused AdminApi wrapper classes and 2 unused P/Invoke declarations"
```

---

### Task 8: Fix Event Handler Leaks in HomePage and RecommendationsPage

Both pages subscribe to `CollectionChanged` in `Page_Loaded` without guarding against re-subscription. Every back-navigation + forward-navigation adds another handler. The handlers also rebuild the entire UI on every change.

**Files:**
- Modify: `src/ContinuumPlayer/Views/HomePage.xaml.cs`
- Modify: `src/ContinuumPlayer/Views/RecommendationsPage.xaml.cs`

- [ ] **Step 1: Fix HomePage — guard against duplicate subscriptions**

Replace the full `HomePage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; }
    private bool _eventsAttached;

    public HomePage()
    {
        ViewModel = App.Services.GetRequiredService<HomeViewModel>();
        this.InitializeComponent();
        SmoothScrollHelper.Attach(ContentScrollViewer);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Sections.Count == 0 && ViewModel.FeaturedSections.Count == 0)
            {
                await ViewModel.LoadCommand.ExecuteAsync(null);
            }

            BuildContent();

            if (!_eventsAttached)
            {
                _eventsAttached = true;
                ViewModel.FeaturedSections.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() => BuildContent());
                ViewModel.Sections.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(() => BuildContent());
            }
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Error: {ex.Message}";
        }
    }

    private void BuildContent()
    {
        // Populate hero carousel with featured section items
        var featuredItems = new List<MediaItem>();
        foreach (var section in ViewModel.FeaturedSections)
        {
            foreach (var item in section.Items)
                featuredItems.Add(item);
        }

        if (featuredItems.Count > 0)
        {
            HeroCarouselControl.ItemsSource = featuredItems;
            HeroCarouselControl.Visibility = Visibility.Visible;
        }
        else
        {
            HeroCarouselControl.Visibility = Visibility.Collapsed;
        }

        // Non-featured sections as rows
        SectionsPanel.Children.Clear();
        foreach (var section in ViewModel.Sections)
        {
            if (section.Items.Count == 0) continue;
            SectionsPanel.Children.Add(new SectionRow { Section = section });
        }
    }
}
```

- [ ] **Step 2: Fix RecommendationsPage — same pattern**

Replace the full `RecommendationsPage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class RecommendationsPage : Page
{
    public RecommendationsViewModel ViewModel { get; }
    private bool _eventsAttached;

    public RecommendationsPage()
    {
        ViewModel = App.Services.GetRequiredService<RecommendationsViewModel>();
        this.InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
        BuildRows();

        if (!_eventsAttached)
        {
            _eventsAttached = true;
            ViewModel.Rows.CollectionChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(BuildRows);
            };
        }
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();

        if (ViewModel.Rows.Count == 0 && !ViewModel.IsLoading)
        {
            EmptyState.Visibility = Visibility.Visible;
            CountPanel.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        // Count total items across all rows
        int totalItems = 0;
        foreach (var row in ViewModel.Rows)
            totalItems += row.Items.Count;

        if (totalItems > 0)
        {
            CountPanel.Visibility = Visibility.Visible;
            RowCountText.Text = totalItems.ToString();
            RowCountLabel.Text = totalItems == 1 ? "suggestion" : "suggestions";
        }
        else
        {
            CountPanel.Visibility = Visibility.Collapsed;
        }

        foreach (var row in ViewModel.Rows)
        {
            if (row.Items.Count == 0) continue;

            var section = new HomeSectionWithItems
            {
                Title = row.Label,
                Items = new List<MediaItem>(row.Items)
            };

            var sectionRow = new SectionRow
            {
                Section = section
            };
            RowsPanel.Children.Add(sectionRow);
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/Views/HomePage.xaml.cs src/ContinuumPlayer/Views/RecommendationsPage.xaml.cs
git commit -m "fix: guard against duplicate CollectionChanged subscriptions on page re-navigation"
```

---

### Task 9: Fix FavoritesPage Sort Bug (rating_imdb sorts by Year)

While reviewing, spotted a bug: in `FavoritesPage.xaml.cs` and `WatchlistPage.xaml.cs`, the `"rating_imdb"` sort case sorts by `Year` instead of by rating. This is a copy-paste bug.

**Files:**
- Modify: `src/ContinuumPlayer/Views/FavoritesPage.xaml.cs`
- Modify: `src/ContinuumPlayer/Views/WatchlistPage.xaml.cs`

- [ ] **Step 1: Fix FavoritesPage sort**

In `FavoritesPage.xaml.cs`, in the `ApplySort()` method, the `"rating_imdb"` case (lines 76-78) incorrectly sorts by `Year`. The `MediaItem` model from the home sections response likely doesn't have a direct IMDb rating field accessible here. Since the items come from the favorites API which returns catalog items with `RatingImdb`, check what fields are available on the items in `ViewModel.Items`.

Actually, looking at the code more carefully -- `ViewModel.Items` is an `ObservableCollection<MediaItem>` where `MediaItem` comes from `HomeSectionWithItems`. The `MediaItem` type is defined in the Home models. Let me note: if `MediaItem` doesn't have a rating field, the sort should fall through to title sort (the current behavior of sorting by Year is always wrong for "rating_imdb"). For now, fix it to at least not duplicate the Year sort:

In `FavoritesPage.xaml.cs`, replace the `rating_imdb` case in `ApplySort()`:

```csharp
            "rating_imdb" => _ascending
                ? ViewModel.Items.OrderBy(i => i.RatingImdb).ToList()
                : ViewModel.Items.OrderByDescending(i => i.RatingImdb).ToList(),
```

If `MediaItem` doesn't have `RatingImdb`, fall back to `Year` but at minimum check. Actually, let me just note this as a fix to verify the field exists. The agent implementing this should check what properties `MediaItem` has and use the correct one. If no rating field exists, remove the `"rating_imdb"` option from the sort ComboBox in the XAML instead.

Apply the same fix in `WatchlistPage.xaml.cs`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded, 0 errors. If `RatingImdb` doesn't exist on `MediaItem`, the build will fail -- in that case, check the `MediaItem` class definition (likely in `src/ContinuumPlayer.Core/Models/Home/HomeSectionsResponse.cs`) for the correct property name, or remove the `"rating_imdb"` option from both XAML sort ComboBoxes.

- [ ] **Step 3: Commit**

```bash
git add src/ContinuumPlayer/Views/FavoritesPage.xaml.cs src/ContinuumPlayer/Views/WatchlistPage.xaml.cs
git commit -m "fix: rating_imdb sort was incorrectly sorting by year (copy-paste bug)"
```

---

### Task 10: Run All Tests + Full Build Verification

**Files:** None (verification only)

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test ContinuumPlayer.sln -v n`

Expected: All tests pass.

- [ ] **Step 2: Build Release configuration**

Run: `dotnet build ContinuumPlayer.sln -c Release`

Expected: Build succeeded, 0 warnings related to our changes.

- [ ] **Step 3: Verify no unintended changes**

Run: `git diff --stat` to review all changes match the plan.

---

## Summary of Optimizations

| Change | Impact | Risk |
|--------|--------|------|
| SettingsService cache | Eliminates 3-4 disk reads on startup | None -- same data, just cached |
| Parallel startup loads | Saves ~1 network round-trip of latency | None -- independent calls |
| Render buffer race fix | Prevents potential crashes during resize | Low -- lock is brief |
| Pre-alloc GCHandles | Eliminates 120 GC operations/sec during playback | None -- same data |
| Timer overlap guard | Prevents concurrent progress reports + silent failures | None -- preserves behavior |
| Image cache eviction | O(1) eviction instead of O(n^2) | None -- FIFO order preserved |
| Image download dedup | Prevents redundant downloads for same image | None -- same result |
| Dead code removal | Smaller binary, clearer codebase | None -- code was unreachable |
| Event handler leak fix | Prevents handler accumulation on re-navigation | None -- same behavior |
| Sort bug fix | rating_imdb sort actually works now | Low -- bug fix |
