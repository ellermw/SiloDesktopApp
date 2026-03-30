# Player Overlay & Mini Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the page-navigation player with a window-level overlay player and a persistent mini bar so browsing pages are never destroyed during playback.

**Architecture:** PlayerService (singleton) owns MpvPlayer lifecycle and state machine (Idle/Expanded/Fullscreen/Minimized). PlayerOverlay and MiniPlayerBar are UserControls in MainWindow that sit above the NavigationView Frame. The Frame never navigates for playback — pages stay alive underneath.

**Tech Stack:** C# / .NET 8 / WinUI 3 / libmpv P/Invoke / CommunityToolkit.Mvvm

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/ContinuumPlayer/Services/PlayerService.cs` | Create | Singleton: MpvPlayer lifecycle, state machine, playback init, server session, frame routing |
| `src/ContinuumPlayer/Controls/PlayerOverlay.xaml` | Create | Full-window player UI (video, controls, overlays) |
| `src/ContinuumPlayer/Controls/PlayerOverlay.xaml.cs` | Create | Overlay code-behind: keyboard, controls, timers, subtitle/audio/quality flyouts |
| `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml` | Create | Bottom bar: mini video, title, transport controls, progress, volume, close |
| `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs` | Create | Bar code-behind: frame rendering, expand, close |
| `src/ContinuumPlayer/MainWindow.xaml` | Modify | Add PlayerOverlay + MiniPlayerBar layers above Frame |
| `src/ContinuumPlayer/MainWindow.xaml.cs` | Modify | Listen to PlayerService.StateChanged, toggle visibility/padding |
| `src/ContinuumPlayer/App.xaml.cs` | Modify | Register PlayerService as singleton, remove PlayerViewModel registration |
| `src/ContinuumPlayer/Views/ItemDetailPage.xaml.cs` | Modify | Replace Navigate<PlayerPage> with PlayerService.PlayAsync |
| `src/ContinuumPlayer/Views/HistoryPage.xaml.cs` | Modify | Replace Navigate<PlayerPage> with PlayerService.PlayAsync |
| `src/ContinuumPlayer/Views/PlayerPage.xaml` | Delete | Replaced by PlayerOverlay |
| `src/ContinuumPlayer/Views/PlayerPage.xaml.cs` | Delete | Logic migrated to PlayerService + PlayerOverlay |
| `src/ContinuumPlayer/ViewModels/PlayerViewModel.cs` | Delete | Properties absorbed into PlayerService |

---

### Task 1: Create PlayerService — State Machine and MpvPlayer Lifecycle

The core singleton that owns everything. No UI — just state, mpv, and server session management. This task creates the service with state machine, mpv lifecycle, and playback initialization. The frame rendering, UI controls, and fullscreen logic come in later tasks.

**Files:**
- Create: `src/ContinuumPlayer/Services/PlayerService.cs`
- Modify: `src/ContinuumPlayer/App.xaml.cs`

- [ ] **Step 1: Create PlayerService.cs**

```csharp
// src/ContinuumPlayer/Services/PlayerService.cs
using System.Runtime.InteropServices;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Player;

namespace ContinuumPlayer.Services;

public enum PlayerState { Idle, Expanded, Fullscreen, Minimized }

public class PlayerService : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;

    private MpvPlayer? _mpv;
    private PlaybackManager? _playbackManager;
    private bool _switchingContent;

    public PlayerService(PlaybackApi playbackApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _authService = authService;
        _apiClient = apiClient;
    }

    // ── State ────────────────────────────────────────────────────────────

    public PlayerState State { get; private set; } = PlayerState.Idle;

    public string? ContentId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Subtitle { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public bool IsLoading { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string PlayMethod { get; private set; } = "";
    public string Resolution { get; private set; } = "";

    public MpvPlayer? Mpv => _mpv;
    public PlaybackManager? Manager => _playbackManager;
    public WatchDetailResponse? WatchDetail => _playbackManager?.WatchDetail;
    public List<FileVersion> Versions { get; private set; } = [];

    // ── Events ───────────────────────────────────────────────────────────

    public event Action<PlayerState>? StateChanged;
    public event Action<byte[], int, int, int>? FrameReady;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action? PlaybackEnded;
    public event Action? ContentLoaded; // fired when file is loaded and decoding starts

    // ── State transitions ────────────────────────────────────────────────

    public void SetState(PlayerState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(newState);
    }

    public void Minimize()
    {
        if (State == PlayerState.Expanded || State == PlayerState.Fullscreen)
        {
            if (State == PlayerState.Fullscreen)
                ExitFullscreen();
            SetState(PlayerState.Minimized);
        }
    }

    public void Expand()
    {
        if (State == PlayerState.Minimized)
            SetState(PlayerState.Expanded);
    }

    // ── Fullscreen (Win32) ───────────────────────────────────────────────

    private const int GWL_STYLE = -16;
    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll")] private static extern long GetWindowLongPtrW(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern long SetWindowLongPtrW(IntPtr hWnd, int nIndex, long dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    private long _savedStyle;
    private RECT _savedRect;

    public void EnterFullscreen()
    {
        if (State != PlayerState.Expanded) return;
        var mw = App.MainWindowInstance;
        if (mw == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);

        _savedStyle = GetWindowLongPtrW(hwnd, GWL_STYLE);
        GetWindowRect(hwnd, out _savedRect);

        var monitor = MonitorFromWindow(hwnd, 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref mi);

        SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle & ~WS_OVERLAPPEDWINDOW);
        SetWindowPos(hwnd, IntPtr.Zero,
            mi.rcMonitor.Left, mi.rcMonitor.Top,
            mi.rcMonitor.Right - mi.rcMonitor.Left,
            mi.rcMonitor.Bottom - mi.rcMonitor.Top,
            SWP_NOACTIVATE);

        SetState(PlayerState.Fullscreen);
    }

    public void ExitFullscreen()
    {
        if (State != PlayerState.Fullscreen) return;
        var mw = App.MainWindowInstance;
        if (mw == null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);

        SetWindowLongPtrW(hwnd, GWL_STYLE, _savedStyle);
        SetWindowPos(hwnd, IntPtr.Zero,
            _savedRect.Left, _savedRect.Top,
            _savedRect.Right - _savedRect.Left,
            _savedRect.Bottom - _savedRect.Top,
            SWP_NOACTIVATE | SWP_NOZORDER);

        SetState(PlayerState.Expanded);
    }

    public void ToggleFullscreen()
    {
        if (State == PlayerState.Fullscreen)
            ExitFullscreen();
        else if (State == PlayerState.Expanded)
            EnterFullscreen();
    }

    // ── Playback ─────────────────────────────────────────────────────────

    public async Task PlayAsync(string contentId, bool fromStart = false)
    {
        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;

        try
        {
            // Create PlaybackManager for this session
            _playbackManager = new PlaybackManager(_playbackApi, _authService, _apiClient);

            // Get watch detail
            var watchDetail = await _playbackManager.GetWatchDetailAsync(contentId);

            // Build title
            if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
            {
                Title = $"{watchDetail.SeriesTitle ?? watchDetail.Title} - S{watchDetail.SeasonNumber:D2}E{watchDetail.EpisodeNumber:D2}";
                Subtitle = watchDetail.Title; // episode title
            }
            else
            {
                Title = watchDetail.Title;
                Subtitle = watchDetail.Year > 0 ? watchDetail.Year.ToString() : null;
            }

            // Versions
            Versions = watchDetail.Versions.ToList();

            // Select best version
            var bestVersion = _playbackManager.SelectBestVersion(watchDetail.Versions);
            if (bestVersion == null)
            {
                ErrorMessage = "No playable version found.";
                IsLoading = false;
                return;
            }

            Resolution = bestVersion.Resolution;

            // Determine start position
            double startPosition = 0;
            if (!fromStart && watchDetail.UserData?.PositionSeconds > 0 && watchDetail.UserData.Played != true)
                startPosition = watchDetail.UserData.PositionSeconds!.Value;

            // Start server session
            var session = await _playbackManager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: fromStart);
            PlayMethod = session.PlayMethod;

            if (!fromStart && session.Position > 0 && startPosition == 0)
                startPosition = session.Position;

            // Build stream URL
            var streamUrl = _playbackManager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ErrorMessage = "No stream URL available.";
                IsLoading = false;
                return;
            }

            // HLS transcode fallback
            if (session.PlayMethod == "transcode")
            {
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
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    startPosition = transcodeResponse.PlayerStartSeconds;
                }
                catch (Exception ex)
                {
                    LogToFile("player_transcode_error.txt", ex.ToString());
                }
            }

            // Initialize mpv (lazy — first play only)
            if (_mpv == null)
            {
                _mpv = new MpvPlayer();
                _mpv.Initialize(1920, 1080); // capped at 1080p render, XAML upscales
                WireMpvEvents();
            }

            // Set state to Expanded (shows the overlay)
            SetState(PlayerState.Expanded);

            // Load subtitles
            LoadSubtitles();

            // Build auth
            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            if (session.PlayMethod != "transcode" && token != null)
                streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            // Store resume position — the FileLoaded handler will seek to it
            _resumePosition = startPosition;

            // Load and play
            _mpv.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
            _mpv.Play();

            IsLoading = false;
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            ErrorMessage = $"Failed to start playback: {ex.Message}";
            IsLoading = false;
        }
    }

    private double _resumePosition;

    private void WireMpvEvents()
    {
        if (_mpv == null) return;

        _mpv.FrameReady += (buffer, w, h, stride) => FrameReady?.Invoke(buffer, w, h, stride);

        _mpv.PositionChanged += (pos) =>
        {
            Position = pos;
            _playbackManager?.UpdatePosition(pos, IsPaused);
            PositionChanged?.Invoke(pos);
        };

        _mpv.DurationChanged += (dur) =>
        {
            Duration = dur;
            DurationChanged?.Invoke(dur);
        };

        _mpv.PauseChanged += (paused) =>
        {
            IsPaused = paused;
            PauseChanged?.Invoke(paused);
        };

        _mpv.FileLoaded += () =>
        {
            IsLoading = false;
            ContentLoaded?.Invoke();
            if (_resumePosition > 0)
            {
                _mpv?.Seek(_resumePosition);
                _resumePosition = 0;
            }
        };

        _mpv.PlaybackEnded += () =>
        {
            if (!_switchingContent)
                PlaybackEnded?.Invoke();
        };

        _mpv.Error += (msg) => LogToFile("mpv_error.txt", msg);
    }

    // ── Content switching (version/audio) ────────────────────────────────

    public async Task SwitchVersionAsync(FileVersion version)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;
        _switchingContent = true;
        _mpv.Stop();
        IsLoading = true;

        try
        {
            try { await _playbackManager.StopSessionAsync(); }
            catch { }

            var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
            PlayMethod = session.PlayMethod;
            Resolution = version.Resolution;

            var streamUrl = _playbackManager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
            {
                ErrorMessage = "No stream URL for selected version.";
                IsLoading = false;
                _switchingContent = false;
                return;
            }

            if (session.PlayMethod == "transcode")
            {
                try
                {
                    var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = currentPos,
                        TargetResolution = version.Resolution,
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
                    streamUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";
                    currentPos = transcodeResponse.PlayerStartSeconds;
                }
                catch (Exception ex) { LogToFile("player_transcode_error.txt", ex.ToString()); }
            }

            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            if (session.PlayMethod != "transcode" && token != null)
                streamUrl += (streamUrl.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            _resumePosition = currentPos;
            _mpv.LoadFile(streamUrl, session.PlayMethod == "transcode" ? null : authHeader);
            _mpv.Play();
            LoadSubtitles();
            _switchingContent = false;
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ErrorMessage = $"Failed to switch quality: {ex.Message}";
        }
    }

    public async Task SwitchAudioTrackAsync(int trackIndex)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;
        _switchingContent = true;
        _mpv.Stop();
        IsLoading = true;

        try
        {
            var response = await _playbackApi.ChangeAudioTrackAsync(
                _playbackManager.SessionId!, trackIndex, currentPos);

            var baseUrl = _apiClient.BaseUrl;
            var token = _apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
            if (token != null)
                url += (url.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(token)}";

            PlayMethod = response.PlayMethod;

            _resumePosition = currentPos;
            var authHeader = token != null ? $"Bearer {token}" : null;
            _mpv.LoadFile(url, authHeader);
            _mpv.Play();
            _switchingContent = false;
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_audio_switch_error.txt", ex.ToString());
            ErrorMessage = $"Failed to switch audio: {ex.Message}";
        }
    }

    // ── Subtitles ────────────────────────────────────────────────────────

    private void LoadSubtitles()
    {
        if (_playbackManager?.CurrentSession == null || _mpv == null) return;

        var subtitleUrls = _playbackManager.GetSubtitleUrls();
        foreach (var (track, fullUrl) in subtitleUrls)
        {
            var codec = track.Codec?.ToLowerInvariant() ?? "";
            if (codec is "pgs" or "pgssub" or "dvdsub" or "vobsub")
                continue;

            var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language ?? "Unknown";
            _mpv.AddSubtitle(fullUrl, label, track.Language);
        }
    }

    // ── Close / Dispose ──────────────────────────────────────────────────

    public async Task CloseAsync()
    {
        if (State == PlayerState.Fullscreen)
            ExitFullscreen();

        _mpv?.Stop();

        if (_playbackManager != null)
        {
            try { await _playbackManager.StopSessionAsync(); }
            catch { }
            _playbackManager.Dispose();
            _playbackManager = null;
        }

        ContentId = null;
        Title = "";
        Subtitle = null;
        Position = 0;
        Duration = 0;
        IsPaused = true;
        IsLoading = false;
        ErrorMessage = null;
        Versions = [];

        SetState(PlayerState.Idle);
    }

    public void Dispose()
    {
        if (State != PlayerState.Idle)
        {
            if (State == PlayerState.Fullscreen)
                ExitFullscreen();
            _playbackManager?.Dispose();
        }
        _mpv?.Dispose();
        _mpv = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static string FormatTime(double totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    public static string LanguageCodeToName(string? code)
    {
        if (string.IsNullOrEmpty(code)) return "Unknown";
        return code.ToLowerInvariant() switch
        {
            "eng" or "en" => "English",
            "spa" or "es" => "Spanish",
            "fre" or "fra" or "fr" => "French",
            "ger" or "deu" or "de" => "German",
            "ita" or "it" => "Italian",
            "por" or "pt" => "Portuguese",
            "rus" or "ru" => "Russian",
            "jpn" or "ja" => "Japanese",
            "kor" or "ko" => "Korean",
            "chi" or "zho" or "zh" => "Chinese",
            "ara" or "ar" => "Arabic",
            "hin" or "hi" => "Hindi",
            "tur" or "tr" => "Turkish",
            "pol" or "pl" => "Polish",
            "dut" or "nld" or "nl" => "Dutch",
            "swe" or "sv" => "Swedish",
            "dan" or "da" => "Danish",
            "fin" or "fi" => "Finnish",
            "nob" or "nor" or "no" => "Norwegian",
            "cze" or "ces" or "cs" => "Czech",
            "hun" or "hu" => "Hungarian",
            "rum" or "ron" or "ro" => "Romanian",
            "bul" or "bg" => "Bulgarian",
            "hrv" or "hr" => "Croatian",
            "gre" or "ell" or "el" => "Greek",
            "heb" or "he" => "Hebrew",
            "tha" or "th" => "Thai",
            "vie" or "vi" => "Vietnamese",
            "ind" or "id" => "Indonesian",
            "may" or "msa" or "ms" => "Malay",
            "fil" or "tl" => "Filipino",
            "ukr" or "uk" => "Ukrainian",
            "cat" or "ca" => "Catalan",
            "baq" or "eus" or "eu" => "Basque",
            "glg" or "gl" => "Galician",
            _ => code.ToUpperInvariant()
        };
    }

    private static void LogToFile(string fileName, string content)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, $"{DateTime.Now}\n{content}\n");
        }
        catch { }
    }
}
```

- [ ] **Step 2: Register PlayerService in DI and remove PlayerViewModel**

In `App.xaml.cs`, add `using ContinuumPlayer.Services;` to the usings. Then in `ConfigureServices()`:

After `services.AddSingleton<NavigationService>();` (line 107), add:
```csharp
        // Player service (owns mpv lifecycle, not tied to page navigation)
        services.AddSingleton<PlayerService>();
```

Remove the PlayerViewModel registration (line 123):
```csharp
        services.AddTransient<PlayerViewModel>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded. There will be warnings about unused PlayerPage references but no errors since PlayerPage still exists at this point.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/Services/PlayerService.cs src/ContinuumPlayer/App.xaml.cs
git commit -m "feat: add PlayerService singleton with state machine, mpv lifecycle, and playback logic"
```

---

### Task 2: Create PlayerOverlay UserControl

The full-window player UI — video display, control bar, loading/error overlays, skip buttons, stats, keyboard shortcuts. This is the direct replacement for PlayerPage's XAML and most of its code-behind. It subscribes to PlayerService for state and frames.

**Files:**
- Create: `src/ContinuumPlayer/Controls/PlayerOverlay.xaml`
- Create: `src/ContinuumPlayer/Controls/PlayerOverlay.xaml.cs`

- [ ] **Step 1: Create PlayerOverlay.xaml**

The XAML is identical to the current PlayerPage.xaml but as a UserControl, with one addition: a Minimize button next to the Close button, and the GoBack button becomes Minimize.

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="ContinuumPlayer.Controls.PlayerOverlay"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="Black"
    KeyDown="Overlay_KeyDown"
    PointerMoved="Overlay_PointerMoved"
    Tapped="Overlay_Tapped">

    <Grid>
        <!-- Video frame rendered by mpv SW render into WriteableBitmap -->
        <Image x:Name="VideoFrame"
               Stretch="Uniform"
               HorizontalAlignment="Center"
               VerticalAlignment="Center" />

        <!-- Loading overlay -->
        <Grid x:Name="LoadingOverlay" Background="#101722" Visibility="Collapsed">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="16">
                <ProgressRing IsActive="True" Width="48" Height="48" />
                <TextBlock Text="Preparing playback..." Foreground="White" FontSize="16" HorizontalAlignment="Center" />
            </StackPanel>
        </Grid>

        <!-- Error overlay -->
        <Grid x:Name="ErrorOverlay" Background="#101722" Visibility="Collapsed">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="16" MaxWidth="500">
                <FontIcon Glyph="&#xE783;" FontSize="48" Foreground="#EF6B73" />
                <TextBlock x:Name="ErrorText" Foreground="#EF6B73" FontSize="16" TextWrapping="Wrap" TextAlignment="Center" HorizontalAlignment="Center" />
                <Button Content="Close" HorizontalAlignment="Center" Click="ClosePlayer_Click" Padding="20,10" />
            </StackPanel>
        </Grid>

        <!-- Skip Intro button -->
        <Button x:Name="SkipIntroButton" Content="Skip Intro" Visibility="Collapsed"
            HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,24,100"
            Padding="20,10" Style="{StaticResource AccentButtonStyle}" Click="SkipIntro_Click" />

        <!-- Skip Credits button -->
        <Button x:Name="SkipCreditsButton" Content="Skip Credits" Visibility="Collapsed"
            HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="0,0,24,100"
            Padding="20,10" Style="{StaticResource AccentButtonStyle}" Click="SkipCredits_Click" />

        <!-- Stats overlay -->
        <Border x:Name="StatsOverlay" Visibility="Collapsed" Background="#CC000000"
            CornerRadius="8" Padding="16" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="16">
            <StackPanel Spacing="4">
                <TextBlock Text="Video Stats" Foreground="#78AEFC" FontSize="13" FontWeight="SemiBold" />
                <TextBlock x:Name="StatsResolution" Foreground="White" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsCodec" Foreground="White" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsPlayMethod" Foreground="White" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsBitrate" Foreground="White" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsHdr" Foreground="#78AEFC" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsPosition" Foreground="White" FontSize="12" FontFamily="Consolas" />
                <TextBlock x:Name="StatsSession" Foreground="#666666" FontSize="11" FontFamily="Consolas" />
            </StackPanel>
        </Border>

        <!-- Controls overlay -->
        <Grid x:Name="ControlsOverlay" VerticalAlignment="Bottom">
            <Grid.Background>
                <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
                    <GradientStop Color="Transparent" Offset="0.0" />
                    <GradientStop Color="#AA000000" Offset="0.3" />
                    <GradientStop Color="#EE000000" Offset="1.0" />
                </LinearGradientBrush>
            </Grid.Background>

            <StackPanel Padding="24,40,24,20" Spacing="8">
                <!-- Title -->
                <TextBlock x:Name="TitleText" Foreground="White" FontSize="16" FontWeight="SemiBold" TextTrimming="CharacterEllipsis" />

                <!-- Seek bar -->
                <Grid ColumnSpacing="12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBlock x:Name="PositionText" Text="0:00" Foreground="#CCCCCC" FontSize="12" VerticalAlignment="Center" />
                    <Slider x:Name="SeekSlider" Grid.Column="1" Minimum="0" Maximum="1" ValueChanged="SeekSlider_ValueChanged" />
                    <TextBlock x:Name="DurationText" Text="0:00" Foreground="#CCCCCC" FontSize="12" VerticalAlignment="Center" Grid.Column="2" />
                </Grid>

                <!-- Buttons -->
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="4">
                        <Button x:Name="PlayPauseButton" Click="PlayPause_Click" Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon x:Name="PlayPauseIcon" Glyph="&#xE768;" FontSize="20" Foreground="White" />
                        </Button>
                        <Button Click="Mute_Click" Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon x:Name="VolumeIcon" Glyph="&#xE767;" FontSize="14" Foreground="White" />
                        </Button>
                        <Slider x:Name="VolumeSlider" Width="80" Minimum="0" Maximum="100" Value="100" VerticalAlignment="Center" ValueChanged="VolumeSlider_ValueChanged" />
                    </StackPanel>

                    <TextBlock x:Name="PlaybackInfoText" Grid.Column="1" Foreground="#666666" FontSize="11" HorizontalAlignment="Center" VerticalAlignment="Center" />

                    <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="4">
                        <Button Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon Glyph="&#xED1E;" FontSize="14" Foreground="White" />
                            <Button.Flyout>
                                <MenuFlyout x:Name="SubtitleFlyout" />
                            </Button.Flyout>
                        </Button>
                        <Button Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon Glyph="&#xE8D6;" FontSize="14" Foreground="White" />
                            <Button.Flyout>
                                <MenuFlyout x:Name="AudioFlyout" />
                            </Button.Flyout>
                        </Button>
                        <Button Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon Glyph="&#xE9E9;" FontSize="14" Foreground="White" />
                            <Button.Flyout>
                                <MenuFlyout x:Name="QualityFlyout" />
                            </Button.Flyout>
                        </Button>
                        <Button Click="Stats_Click" Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon Glyph="&#xE946;" FontSize="14" Foreground="White" />
                        </Button>
                        <Button Click="Fullscreen_Click" Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon x:Name="FullscreenIcon" Glyph="&#xE740;" FontSize="14" Foreground="White" />
                        </Button>
                        <!-- Minimize to mini bar -->
                        <Button Click="Minimize_Click" Background="Transparent" BorderThickness="0" Padding="8" ToolTipService.ToolTip="Minimize to bar">
                            <FontIcon Glyph="&#xE921;" FontSize="14" Foreground="White" />
                        </Button>
                        <Button Click="ClosePlayer_Click" Background="Transparent" BorderThickness="0" Padding="8">
                            <FontIcon Glyph="&#xE711;" FontSize="14" Foreground="White" />
                        </Button>
                    </StackPanel>
                </Grid>
            </StackPanel>
        </Grid>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create PlayerOverlay.xaml.cs**

This is the code-behind with frame rendering, UI timer, controls auto-hide, keyboard shortcuts, and flyout population. It delegates all state management and mpv commands to PlayerService.

The agent implementing this task must:
1. Read the current `PlayerPage.xaml.cs` in full (it's already been read above in the plan context — the agent should re-read it)
2. Create `PlayerOverlay.xaml.cs` by migrating the following from `PlayerPage.xaml.cs`:
   - `IBufferByteAccess` COM interface declaration (lines 15-21)
   - Frame rendering: `_snapBuffer`, `OnFrameReady`, `PresentFrame` (lines 332-397)
   - UI timer: `UiTimer_Tick`, `UpdateSkipButtons` (lines 401-442)
   - Controls auto-hide: `HideTimer_Tick`, `ShowControls`, `Page_PointerMoved`, `Page_Tapped` (lines 444-478)
   - Keyboard shortcuts: `Page_KeyDown` → `Overlay_KeyDown` (lines 482-538)
   - Button click handlers: all `_Click` methods (lines 666-731)
   - Seek slider: `SeekSlider_ValueChanged` (lines 723-731)
   - Volume: `VolumeSlider_ValueChanged`, `UpdateVolumeIcon`, `ToggleMute`, `AdjustVolume` (lines 556-569, 676-687, 1036-1044)
   - Flyout population: `PopulateQualityFlyout`, `PopulateSubtitleFlyout`, `PopulateAudioFlyout` (lines 735-959)
   - Stats: `ToggleStats`, `UpdateStats` (lines 649-1032)
   - `UpdatePlaybackInfo` (lines 1046-1057)

Key differences from PlayerPage:
- Constructor gets `PlayerService` from DI, NOT PlayerViewModel
- No navigation lifecycle (`OnNavigatedTo`, `Page_Loaded`, `Page_Unloaded`) — instead has `Activate()` and `Deactivate()` methods called by MainWindow when visibility toggles
- `Activate()`: subscribes to `PlayerService.FrameReady`, starts UI timer + hide timer, sets focus, populates flyouts, shows loading overlay if `PlayerService.IsLoading`
- `Deactivate()`: unsubscribes from FrameReady, stops timers
- All mpv commands go through `_playerService.Mpv?.Method()` instead of `_mpvPlayer?.Method()`
- Escape in fullscreen → `_playerService.ExitFullscreen()`, Escape in expanded → `_playerService.Minimize()`
- Close button → `_ = _playerService.CloseAsync()`
- Minimize button → `_playerService.Minimize()`
- Quality switch → `_ = _playerService.SwitchVersionAsync(version)`
- Audio switch → `_ = _playerService.SwitchAudioTrackAsync(trackIndex)`
- No `CleanupAsync` — PlayerService owns cleanup
- No fullscreen Win32 code — PlayerService owns that

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded (PlayerPage still exists, warnings about unused references are fine).

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/Controls/PlayerOverlay.xaml src/ContinuumPlayer/Controls/PlayerOverlay.xaml.cs
git commit -m "feat: add PlayerOverlay UserControl with video rendering, controls, and keyboard shortcuts"
```

---

### Task 3: Create MiniPlayerBar UserControl

The bottom bar with live video thumbnail, title, transport controls, seekable progress bar, volume, and close button.

**Files:**
- Create: `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml`
- Create: `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs`

- [ ] **Step 1: Create MiniPlayerBar.xaml**

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="ContinuumPlayer.Controls.MiniPlayerBar"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Height="64">

    <Border Background="{StaticResource CardBackgroundBrush}"
            BorderBrush="{StaticResource SidebarBorderBrush}"
            BorderThickness="0,1,0,0">
        <Grid ColumnSpacing="12" Padding="8,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />  <!-- Video thumbnail -->
                <ColumnDefinition Width="*" />      <!-- Title + progress -->
                <ColumnDefinition Width="Auto" />   <!-- Transport controls -->
                <ColumnDefinition Width="Auto" />   <!-- Volume -->
                <ColumnDefinition Width="Auto" />   <!-- Close -->
            </Grid.ColumnDefinitions>

            <!-- Live video thumbnail with expand overlay -->
            <Grid Grid.Column="0" Width="90" Height="50" Margin="0,7"
                  Tapped="VideoThumbnail_Tapped" Background="Black" CornerRadius="4">
                <Image x:Name="MiniVideoFrame" Stretch="Uniform" />
                <!-- Expand chevron overlay -->
                <Border Background="#88000000" CornerRadius="4"
                        HorizontalAlignment="Center" VerticalAlignment="Center"
                        Padding="6,2" Opacity="0" x:Name="ExpandOverlay">
                    <FontIcon Glyph="&#xE70E;" FontSize="12" Foreground="White" />
                </Border>
            </Grid>

            <!-- Title + thin seek bar -->
            <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                <TextBlock x:Name="TitleText" FontSize="13" FontWeight="SemiBold"
                           Foreground="{StaticResource PrimaryTextBrush}"
                           TextTrimming="CharacterEllipsis" MaxLines="1" />
                <TextBlock x:Name="SubtitleText" FontSize="11"
                           Foreground="{StaticResource SecondaryTextBrush}"
                           TextTrimming="CharacterEllipsis" MaxLines="1" />
                <Slider x:Name="SeekSlider" Minimum="0" Maximum="1" Height="16"
                        Margin="0,-2,0,0" ValueChanged="SeekSlider_ValueChanged" />
            </StackPanel>

            <!-- Transport controls -->
            <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="0" VerticalAlignment="Center">
                <Button Click="PlayPause_Click" Background="Transparent" BorderThickness="0" Padding="10,8">
                    <FontIcon x:Name="PlayPauseIcon" Glyph="&#xE768;" FontSize="18" Foreground="{StaticResource PrimaryTextBrush}" />
                </Button>
            </StackPanel>

            <!-- Volume -->
            <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="2" VerticalAlignment="Center">
                <Button Click="Mute_Click" Background="Transparent" BorderThickness="0" Padding="6">
                    <FontIcon x:Name="VolumeIcon" Glyph="&#xE767;" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" />
                </Button>
                <Slider x:Name="VolumeSlider" Width="60" Minimum="0" Maximum="100" Value="100"
                        VerticalAlignment="Center" ValueChanged="VolumeSlider_ValueChanged" />
            </StackPanel>

            <!-- Close -->
            <Button Grid.Column="4" Click="Close_Click" Background="Transparent" BorderThickness="0"
                    Padding="10,8" VerticalAlignment="Center" ToolTipService.ToolTip="Stop playback">
                <FontIcon Glyph="&#xE711;" FontSize="14" Foreground="{StaticResource SecondaryTextBrush}" />
            </Button>
        </Grid>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create MiniPlayerBar.xaml.cs**

```csharp
// src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media.Imaging;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.Controls;

[ComImport]
[Guid("905a0fef-bc53-11df-8c49-001e4fc686da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMiniBufferByteAccess
{
    void Buffer(out IntPtr buffer);
}

public sealed partial class MiniPlayerBar : UserControl
{
    private readonly PlayerService _playerService;
    private WriteableBitmap? _miniBitmap;
    private bool _active;
    private bool _suppressSeek;
    private bool _isMuted;

    // Snap buffer for mini frame (render thread → UI thread)
    private byte[]? _snapBuffer;
    private int _snapW, _snapH, _snapStride;
    private volatile bool _snapReady;
    private volatile bool _uiBusy;

    private DispatcherTimer? _uiTimer;

    public MiniPlayerBar()
    {
        _playerService = App.Services.GetRequiredService<PlayerService>();
        this.InitializeComponent();

        // Hover effect on video thumbnail
        var thumbGrid = (Grid)MiniVideoFrame.Parent;
        thumbGrid.PointerEntered += (_, _) => ExpandOverlay.Opacity = 1;
        thumbGrid.PointerExited += (_, _) => ExpandOverlay.Opacity = 0;
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;

        // Tell mpv to render at mini resolution
        _playerService.Mpv?.UpdateRenderSize(160, 90);

        // Subscribe to frames
        _playerService.FrameReady += OnFrameReady;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PauseChanged += OnPauseChanged;

        // Update display
        TitleText.Text = _playerService.Title;
        SubtitleText.Text = _playerService.Subtitle ?? "";
        UpdatePlayPauseIcon();

        // Start UI timer for seek bar
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();
    }

    public void Deactivate()
    {
        if (!_active) return;
        _active = false;

        _playerService.FrameReady -= OnFrameReady;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.PauseChanged -= OnPauseChanged;

        _uiTimer?.Stop();
        _uiTimer = null;
    }

    // ── Frame rendering ──────────────────────────────────────────────────

    private void OnFrameReady(byte[] buffer, int width, int height, int stride)
    {
        if (!_active || _uiBusy) return;

        int size = stride * height;
        if (_snapBuffer == null || _snapBuffer.Length < size)
            _snapBuffer = new byte[size];
        Buffer.BlockCopy(buffer, 0, _snapBuffer, 0, size);
        _snapW = width;
        _snapH = height;
        _snapStride = stride;
        _snapReady = true;

        DispatcherQueue?.TryEnqueue(PresentFrame);
    }

    private void PresentFrame()
    {
        if (!_active || !_snapReady || _snapBuffer == null) return;
        _uiBusy = true;
        _snapReady = false;

        try
        {
            int w = _snapW, h = _snapH, srcStride = _snapStride;
            int dstStride = w * 4;

            if (_miniBitmap == null || _miniBitmap.PixelWidth != w || _miniBitmap.PixelHeight != h)
            {
                _miniBitmap = new WriteableBitmap(w, h);
                MiniVideoFrame.Source = _miniBitmap;
            }

            var pixelBuffer = _miniBitmap.PixelBuffer;
            if (srcStride == dstStride)
            {
                int copyLen = Math.Min(dstStride * h, (int)pixelBuffer.Length);
                System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                    .CopyTo(_snapBuffer, 0, pixelBuffer, 0, copyLen);
            }
            else
            {
                int rowBytes = Math.Min(srcStride, dstStride);
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions
                        .CopyTo(_snapBuffer, y * srcStride, pixelBuffer, (uint)(y * dstStride), rowBytes);
                }
            }
            _miniBitmap.Invalidate();
        }
        finally
        {
            _uiBusy = false;
        }
    }

    // ── UI updates ───────────────────────────────────────────────────────

    private void UiTimer_Tick(object? sender, object e)
    {
        if (!_active) return;

        var pos = _playerService.Position;
        var dur = _playerService.Duration;

        _suppressSeek = true;
        if (dur > 0) SeekSlider.Maximum = dur;
        SeekSlider.Value = pos;
        _suppressSeek = false;
    }

    private void OnPositionChanged(double pos)
    {
        // Handled by UI timer to avoid flooding
    }

    private void OnPauseChanged(bool paused)
    {
        DispatcherQueue?.TryEnqueue(UpdatePlayPauseIcon);
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Glyph = _playerService.IsPaused ? "\uE768" : "\uE769";
    }

    // ── Controls ─────────────────────────────────────────────────────────

    private void VideoThumbnail_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _playerService.Expand();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _playerService.Mpv?.TogglePause();
    }

    private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSeek || _playerService.Mpv == null) return;
        _playerService.Mpv.Seek(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_playerService.Mpv == null) return;
        _isMuted = !_playerService.Mpv.GetMute();
        _playerService.Mpv.SetMute(_isMuted);
        UpdateVolumeIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_playerService.Mpv == null) return;
        _playerService.Mpv.SetVolume(e.NewValue);
        if (_isMuted && e.NewValue > 0)
        {
            _isMuted = false;
            _playerService.Mpv.SetMute(false);
        }
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        if (_isMuted || VolumeSlider.Value <= 0)
            VolumeIcon.Glyph = "\uE74F";
        else if (VolumeSlider.Value < 50)
            VolumeIcon.Glyph = "\uE993";
        else
            VolumeIcon.Glyph = "\uE767";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _ = _playerService.CloseAsync();
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/Controls/MiniPlayerBar.xaml src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs
git commit -m "feat: add MiniPlayerBar with live video thumbnail, transport controls, and seek"
```

---

### Task 4: Wire MainWindow — Add Layers and State Handling

Add the PlayerOverlay and MiniPlayerBar to MainWindow. Listen to PlayerService.StateChanged and toggle visibility/padding.

**Files:**
- Modify: `src/ContinuumPlayer/MainWindow.xaml`
- Modify: `src/ContinuumPlayer/MainWindow.xaml.cs`

- [ ] **Step 1: Modify MainWindow.xaml**

Add `xmlns:controls="using:ContinuumPlayer.Controls"` to the Window element attributes.

Replace the root `<Grid>` content so it contains three layers:

```xml
    <Grid Background="{StaticResource AppBackgroundBrush}">
        <!-- Layer 1: NavigationView with content frame (always present) -->
        <NavigationView
            x:Name="NavView"
            ... (keep ALL existing NavigationView content unchanged) ...
        </NavigationView>

        <!-- Layer 2: Full-window player overlay (above nav, initially hidden) -->
        <controls:PlayerOverlay x:Name="PlayerOverlayControl" Visibility="Collapsed" />

        <!-- Layer 3: Mini player bar (bottom-docked, initially hidden) -->
        <controls:MiniPlayerBar x:Name="MiniPlayerBarControl"
                                 Visibility="Collapsed"
                                 VerticalAlignment="Bottom" />
    </Grid>
```

The NavigationView stays exactly as it is. The two new elements are added AFTER the NavigationView inside the same Grid.

- [ ] **Step 2: Modify MainWindow.xaml.cs**

Add to the constructor, after `NavView.IsPaneVisible = false;`:

```csharp
        // Listen for player state changes
        var playerService = App.Services.GetRequiredService<ContinuumPlayer.Services.PlayerService>();
        playerService.StateChanged += OnPlayerStateChanged;
```

Add the handler method:

```csharp
    private void OnPlayerStateChanged(PlayerState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (state)
            {
                case PlayerState.Idle:
                    PlayerOverlayControl.Visibility = Visibility.Collapsed;
                    PlayerOverlayControl.Deactivate();
                    MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                    MiniPlayerBarControl.Deactivate();
                    NavView.IsPaneVisible = _navInitialized;
                    NavView.Margin = new Thickness(0);
                    break;

                case PlayerState.Expanded:
                case PlayerState.Fullscreen:
                    MiniPlayerBarControl.Deactivate();
                    MiniPlayerBarControl.Visibility = Visibility.Collapsed;
                    NavView.IsPaneVisible = false;
                    NavView.Margin = new Thickness(0);
                    PlayerOverlayControl.Visibility = Visibility.Visible;
                    PlayerOverlayControl.Activate();
                    break;

                case PlayerState.Minimized:
                    PlayerOverlayControl.Deactivate();
                    PlayerOverlayControl.Visibility = Visibility.Collapsed;
                    NavView.IsPaneVisible = _navInitialized;
                    NavView.Margin = new Thickness(0, 0, 0, 64); // room for mini bar
                    MiniPlayerBarControl.Visibility = Visibility.Visible;
                    MiniPlayerBarControl.Activate();
                    break;
            }
        });
    }
```

Add `using ContinuumPlayer.Services;` to the usings.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ContinuumPlayer/ContinuumPlayer.csproj`

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ContinuumPlayer/MainWindow.xaml src/ContinuumPlayer/MainWindow.xaml.cs
git commit -m "feat: wire MainWindow with PlayerOverlay and MiniPlayerBar layers, state handling"
```

---

### Task 5: Update Call Sites and Delete PlayerPage

Replace Navigate<PlayerPage> calls with PlayerService.PlayAsync. Delete PlayerPage and PlayerViewModel.

**Files:**
- Modify: `src/ContinuumPlayer/Views/ItemDetailPage.xaml.cs`
- Modify: `src/ContinuumPlayer/Views/HistoryPage.xaml.cs`
- Delete: `src/ContinuumPlayer/Views/PlayerPage.xaml`
- Delete: `src/ContinuumPlayer/Views/PlayerPage.xaml.cs`
- Delete: `src/ContinuumPlayer/ViewModels/PlayerViewModel.cs`

- [ ] **Step 1: Update ItemDetailPage.xaml.cs**

Find the line (around line 385):
```csharp
        nav.Navigate<PlayerPage>(fromStart ? $"{contentId}|fromstart" : contentId);
```

Replace with:
```csharp
        var playerService = App.Services.GetRequiredService<ContinuumPlayer.Services.PlayerService>();
        _ = playerService.PlayAsync(contentId, fromStart: fromStart);
```

Remove the `using ContinuumPlayer.Views;` import if PlayerPage was the only reason for it (check if other page types from Views namespace are used — they likely are, so keep it).

- [ ] **Step 2: Update HistoryPage.xaml.cs**

Find the line (around line 241):
```csharp
            nav.Navigate<PlayerPage>(contentId);
```

Replace with:
```csharp
            var playerService = App.Services.GetRequiredService<ContinuumPlayer.Services.PlayerService>();
            _ = playerService.PlayAsync(contentId);
```

- [ ] **Step 3: Delete PlayerPage and PlayerViewModel**

Delete these files:
- `src/ContinuumPlayer/Views/PlayerPage.xaml`
- `src/ContinuumPlayer/Views/PlayerPage.xaml.cs`
- `src/ContinuumPlayer/ViewModels/PlayerViewModel.cs`

- [ ] **Step 4: Build full solution**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded with 0 errors. There should be no remaining references to PlayerPage or PlayerViewModel.

- [ ] **Step 5: Run tests**

Run: `dotnet test ContinuumPlayer.sln -v n`

Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: replace PlayerPage navigation with PlayerService overlay, delete PlayerPage and PlayerViewModel"
```

---

### Task 6: Integration Testing and Polish

Verify all transitions work, fix any rendering issues, ensure overlay render size switches correctly between expanded and minimized.

**Files:**
- Modify: `src/ContinuumPlayer/Controls/PlayerOverlay.xaml.cs` (if needed)
- Modify: `src/ContinuumPlayer/Controls/MiniPlayerBar.xaml.cs` (if needed)
- Modify: `src/ContinuumPlayer/Services/PlayerService.cs` (if needed)

- [ ] **Step 1: Verify render size switching in PlayerOverlay.Activate**

In `PlayerOverlay.xaml.cs`, the `Activate()` method must call `UpdateRenderSize` with the overlay dimensions:

```csharp
// In Activate(), after subscribing to FrameReady:
var renderW = Math.Min((int)Math.Max(VideoFrame.ActualWidth, 1280), 1920);
var renderH = Math.Min((int)Math.Max(VideoFrame.ActualHeight, 720), 1080);
_playerService.Mpv?.UpdateRenderSize(renderW, renderH);
```

And `MiniPlayerBar.Activate()` already calls `UpdateRenderSize(160, 90)`.

This ensures mpv renders at the correct resolution for whichever target is visible.

- [ ] **Step 2: Build and verify**

Run: `dotnet build ContinuumPlayer.sln`

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run tests**

Run: `dotnet test ContinuumPlayer.sln -v n`

Expected: All pass.

- [ ] **Step 4: Commit and push**

```bash
git add -A
git commit -m "feat: complete player overlay and mini bar integration"
git push
```

---

## Summary

| Task | What it does | Key files |
|------|-------------|-----------|
| 1 | PlayerService singleton — state machine, mpv lifecycle, playback init, fullscreen | `Services/PlayerService.cs`, `App.xaml.cs` |
| 2 | PlayerOverlay — full-window video + controls (migrated from PlayerPage) | `Controls/PlayerOverlay.xaml[.cs]` |
| 3 | MiniPlayerBar — bottom bar with live video, transport, seek, close | `Controls/MiniPlayerBar.xaml[.cs]` |
| 4 | MainWindow wiring — add layers, toggle visibility on state changes | `MainWindow.xaml[.cs]` |
| 5 | Call site updates + delete PlayerPage/PlayerViewModel | `ItemDetailPage`, `HistoryPage`, delete old files |
| 6 | Integration testing, render size switching, polish | Various fixes |
