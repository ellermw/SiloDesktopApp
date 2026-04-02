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
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;

    private MpvPlayer? _mpv;
    private PlaybackManager? _playbackManager;
    private bool _switchingContent;

    public PlayerService(PlaybackApi playbackApi, CatalogApi catalogApi, AuthService authService, ContinuumApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
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
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }

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
        // HWND_TOPMOST (-1) puts the window above the taskbar
        SetWindowPos(hwnd, (IntPtr)(-1),
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
        // HWND_NOTOPMOST (-2) drops back below the taskbar
        SetWindowPos(hwnd, (IntPtr)(-2),
            _savedRect.Left, _savedRect.Top,
            _savedRect.Right - _savedRect.Left,
            _savedRect.Bottom - _savedRect.Top,
            SWP_NOACTIVATE);

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

    public async Task PlayAsync(string contentId, bool fromStart = false, int? fileId = null)
    {
        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;

        try
        {
            // Create PlaybackManager for this session
            _playbackManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient);

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

            // Select version: use specified fileId if provided, otherwise pick best
            FileVersion? bestVersion = null;
            if (fileId.HasValue)
                bestVersion = watchDetail.Versions.FirstOrDefault(v => v.FileId == fileId.Value);
            bestVersion ??= _playbackManager.SelectBestVersion(watchDetail.Versions);
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
                _mpv.Initialize(1920, 1080);
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
            IsPaused = false; // Ensure progress reports don't say paused before mpv fires PauseChanged

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
