// src/ContinuumPlayer/Services/PlayerService.cs
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Messaging;
using ContinuumPlayer.Player;

namespace ContinuumPlayer.Services;

public enum PlayerState { Idle, Expanded, Fullscreen, Minimized }

public class PlayerService : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly ContinuumApiClient _apiClient;
    private readonly SettingsService _settingsService;

    private MpvPlayer? _mpv;
    private MpvVideoWindow? _videoWindow;
    private PlaybackManager? _playbackManager;
    private volatile bool _switchingContent;
    /// <summary>
    /// Pre-play subtitle selection captured from PlayAsync and applied to mpv
    /// before LoadFile. null = auto, -1 = off, 0+ = 0-based track index.
    /// </summary>
    private int? _pendingSubtitleSelection;
    private volatile bool _qualitySwitchActive;
    private bool _playingNextShown;
    // Premature-EOF loop-breaker. mpv keep-open=yes pauses at EOF; our handler
    // restarts the stream to punch through transient CDN/server drops. Track
    // last attempt time and streak length so rapid duplicate EOFs do not spawn
    // overlapping recovery attempts.
    private volatile bool _prematureEofRecoveryActive;
    private double _prematureEofRecoveryPosition;
    private long _prematureEofLastAttemptMs;
    private int _prematureEofStreak;
    private readonly PlaybackStallDetector _stallDetector = new(
        bufferingTimeout: TimeSpan.FromSeconds(20),
        silentPlaybackTimeout: TimeSpan.FromSeconds(45));
    private readonly PlaybackNaturalEndDetector _naturalEndDetector = new(
        completionDelay: TimeSpan.FromSeconds(4));
    private Timer? _stallWatchdogTimer;
    private long _stallRecoveryLastAttemptMs;
    private volatile bool _naturalEndDispatched;
    private string _activeQualityTier = "original";
    private HlsProxy? _hlsProxy;
    private DirectStreamProxy? _directStreamProxy;
    private PlaybackWebSocket? _webSocket;
    private CancellationTokenSource? _playbackCts;

    // Stored mpv event handlers for proper unsubscription
    private Action<double>? _mpvPositionHandler;
    private Action<double>? _mpvDurationHandler;
    private Action<bool>? _mpvPauseHandler;
    private Action<bool>? _mpvBufferingHandler;
    private Action? _mpvFileLoadedHandler;
    private Action? _mpvPlaybackEndedHandler;
    private Action<string>? _mpvPlaybackErrorHandler;
    private Action<string>? _mpvErrorHandler;

    public PlayerService(
        PlaybackApi playbackApi,
        CatalogApi catalogApi,
        AuthService authService,
        ContinuumApiClient apiClient,
        SettingsService settingsService)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _apiClient = apiClient;
        _settingsService = settingsService;

        // Restore persisted volume + mute so the player starts where the user
        // left it instead of at 100%. These are applied to mpv on
        // EnsureMpvInitialized (see MpvPlayer.SetVolume/SetMute call sites).
        try
        {
            var settings = _settingsService.Load();
            Volume = Math.Clamp(settings.PlayerVolume, 0, 100);
            IsMuted = settings.PlayerMuted;
        }
        catch { /* settings file missing / corrupt — use defaults */ }
    }

    /// <summary>
    /// Persist the current <see cref="Volume"/> + <see cref="IsMuted"/> to
    /// AppSettings. Called from the overlay / mini-bar on every change so
    /// the next launch restores the same level.
    /// </summary>
    public void SaveVolumeState()
    {
        try
        {
            var settings = _settingsService.Load();
            settings.PlayerVolume = Volume;
            settings.PlayerMuted = IsMuted;
            _settingsService.Save(settings);
        }
        catch { /* non-fatal — volume persistence is best-effort */ }
    }

    /// <summary>
    /// True when subtitles are hidden via the C keyboard shortcut. mpv
    /// internally tracks <c>sub-visibility</c>, but we mirror it here so
    /// the overlay can swap the Captions glyph.
    /// </summary>
    public bool SubtitlesHidden { get; private set; }

    /// <summary>
    /// C-key shortcut: flip <c>sub-visibility</c> on mpv. If subtitles
    /// are off entirely (sid=no), this is a no-op.
    /// </summary>
    public void ToggleSubtitleVisibility()
    {
        if (_mpv == null) return;
        SubtitlesHidden = !SubtitlesHidden;
        _mpv.SetProperty("sub-visibility", SubtitlesHidden ? "no" : "yes");
    }

    // ── Transport (used by WatchTogetherCoordinator for synced playback) ──

    /// <summary>Pause / resume the local mpv player.</summary>
    public void SetPaused(bool paused)
    {
        if (_mpv == null) return;
        if (paused) _mpv.Pause();
        else _mpv.Play();
    }

    /// <summary>Seek to an absolute position in media-time seconds.</summary>
    public void SeekTo(double positionSeconds)
    {
        if (_mpv == null) return;
        _mpv.Seek(positionSeconds);
    }

    // ── Subtitle appearance bridge (B55 follow-up) ──────────────────────

    /// <summary>
    /// Cached subtitle appearance settings. Applied to mpv on every fresh
    /// initialize (so the next file starts with the right styling) and
    /// pushed live by <see cref="ApplySubtitleAppearance"/> whenever the
    /// user saves on the SettingsPage.
    /// </summary>
    private Core.Models.Settings.SubtitleAppearance? _subtitleAppearance;

    /// <summary>
    /// Push the given subtitle appearance to mpv's sub-* properties. Safe
    /// to call at any time; if mpv isn't initialized yet the settings are
    /// cached and applied on the next initialization.
    /// </summary>
    public void ApplySubtitleAppearance(Core.Models.Settings.SubtitleAppearance appearance)
    {
        _subtitleAppearance = appearance;
        if (_mpv == null) return;
        PushSubtitleAppearanceToMpv(appearance);
    }

    private void PushSubtitleAppearanceToMpv(Core.Models.Settings.SubtitleAppearance a)
    {
        if (_mpv == null) return;
        try
        {
            // Font size — map the webui step names to mpv pixel sizes.
            // mpv's sub-font-size is a point value (~48 is typical default).
            int fontSize = a.FontSize switch
            {
                "small" => 36,
                "large" => 64,
                "xlarge" => 80,
                _ => 48, // medium / unknown
            };
            _mpv.SetProperty("sub-font-size", fontSize.ToString());

            // Font family — map to mpv's font family names.
            string fontName = a.FontFamily switch
            {
                "serif" => "Serif",
                "monospace" => "Monospace",
                _ => "Sans",
            };
            _mpv.SetProperty("sub-font", fontName);

            // Font color — mpv accepts "#RRGGBB" (alpha defaults to FF).
            _mpv.SetProperty("sub-color", NormalizeHex(a.FontColor));

            // Background color + opacity. mpv uses "#AARRGGBB" where AA is
            // hex alpha (0x00 = transparent, 0xFF = opaque).
            if (a.BackgroundStyle == "box")
            {
                int alpha = Math.Clamp((int)Math.Round(a.BackgroundOpacity * 2.55), 0, 255);
                string bgHex = NormalizeHex(a.BackgroundColor);
                // Strip leading "#" and prepend alpha.
                string argb = $"#{alpha:X2}{bgHex.TrimStart('#')}";
                _mpv.SetProperty("sub-back-color", argb);
                _mpv.SetProperty("sub-border-size", "0");
                _mpv.SetProperty("sub-shadow-offset", "0");
            }
            else if (a.BackgroundStyle == "outline")
            {
                // Fully transparent background, heavy border.
                _mpv.SetProperty("sub-back-color", "#00000000");
                _mpv.SetProperty("sub-border-size", "3");
                _mpv.SetProperty("sub-border-color", "#FF000000");
                _mpv.SetProperty("sub-shadow-offset", "0");
            }
            else if (a.BackgroundStyle == "shadow")
            {
                _mpv.SetProperty("sub-back-color", "#00000000");
                _mpv.SetProperty("sub-border-size", "0");
                _mpv.SetProperty("sub-shadow-offset", "2");
                _mpv.SetProperty("sub-shadow-color", "#80000000");
            }
            else // "none"
            {
                _mpv.SetProperty("sub-back-color", "#00000000");
                _mpv.SetProperty("sub-border-size", "0");
                _mpv.SetProperty("sub-shadow-offset", "0");
            }

            // Independent text outline that stacks with the background style.
            if (a.TextOutline)
            {
                _mpv.SetProperty("sub-border-size", "2");
                _mpv.SetProperty("sub-border-color", "#FF000000");
            }

            // Position (bottom / lower-third / top).
            _mpv.SetProperty("sub-align-y", a.Position == "top" ? "top" : "bottom");
            if (a.Position == "lower-third")
                _mpv.SetProperty("sub-margin-y", "160");
            else
                _mpv.SetProperty("sub-margin-y", "22");
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"ApplySubtitleAppearance error: {ex.Message}"); }
    }

    private static string NormalizeHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return "#FFFFFF";
        if (!hex.StartsWith("#")) hex = "#" + hex;
        return hex.ToUpperInvariant();
    }

    // ── Per-series subtitle / audio preference persistence ─────────────

    /// <summary>
    /// For series episodes the prefs key is the series_id; for movies it's
    /// the content_id. Matches the webui convention and what the server
    /// looks up on the next play.
    /// </summary>
    private string? GetPrefsKey()
    {
        var wd = _playbackManager?.WatchDetail;
        if (wd == null) return ContentId;
        if (!string.IsNullOrEmpty(wd.SeriesId)) return wd.SeriesId;
        return ContentId;
    }

    /// <summary>
    /// Change the active subtitle track AND persist the user's choice so
    /// subsequent episodes / resumes default to the same language + mode.
    /// Pass <c>mpvTrackIndex=0</c> to disable subtitles entirely (mode=off).
    /// </summary>
    public async Task SetSubtitleTrackAndPersistAsync(int mpvTrackIndex, string? language)
        => await SetSubtitleTrackAndPersistAsync(mpvTrackIndex, language, null);

    /// <summary>
    /// Variant that carries the full <see cref="SubtitleTrackInfo"/> so the
    /// server can store a track signature (source/codec/label/forced/HI) —
    /// matches upstream <c>WatchPage.handleSubtitleChanged</c>. On the next
    /// play the server will re-resolve to the same subtitle even if indices
    /// shifted due to a remux/transcode swap.
    /// </summary>
    public async Task SetSubtitleTrackAndPersistAsync(int mpvTrackIndex, string? language, SubtitleTrackInfo? track)
    {
        if (track != null)
            SelectSubtitleTrack(track);
        else
            _mpv?.SetSubtitleTrack(mpvTrackIndex);

        var key = GetPrefsKey();
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            // Mirror derivePersistedSubtitleMode: index null ("Off") → "off";
            // any explicit track → "always". "auto" is never written here —
            // it's the default when no pref exists.
            bool off = mpvTrackIndex <= 0 || track == null;
            var req = new SubtitlePreferenceRequest
            {
                SubtitleLanguage = (off ? "" : track?.Language) ?? "",
                SubtitleTrackIndex = off ? -1 : (track?.Index ?? (mpvTrackIndex - 1)),
                SubtitleMode = off ? "off" : "always",
                TrackSignature = off || track == null ? null : new SubtitleTrackSignature
                {
                    Source = track.Source ?? "embedded",
                    Language = track.Language,
                    Codec = track.Codec,
                    Label = track.Label,
                    Forced = track.Forced,
                    HearingImpaired = track.HearingImpaired,
                },
            };
            await _playbackApi.SaveSubtitlePrefsAsync(key, req);
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"SaveSubtitlePrefs error: {ex.Message}"); }
    }

    /// <summary>
    /// Change the active audio track AND persist the choice. The index is
    /// 1-based to match mpv's <c>aid</c> property.
    /// </summary>
    public async Task SetAudioTrackAndPersistAsync(int mpvTrackIndex, string? language)
    {
        _mpv?.SetAudioTrack(mpvTrackIndex);

        var key = GetPrefsKey();
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            await _catalogApi.SetAudioPrefsAsync(key, new Core.Models.Catalog.AudioPreference
            {
                AudioTrackIndex = mpvTrackIndex,
                AudioLanguage = language,
            });
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"SetAudioPrefs error: {ex.Message}"); }
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

    /// <summary>Fired after a new playback session is created on the server.
    /// Payload is the session UUID. Consumers like
    /// <see cref="WatchTogetherCoordinator"/> use this to attach the session
    /// to any active room so transport commands reach mpv.</summary>
    public event Action<string>? SessionStarted;

    /// <summary>Relays mpv's <c>paused-for-cache</c> state. Raised on the
    /// event-pump thread; overlay consumers must dispatch to UI. The overlay
    /// applies a 500ms debounce before showing any spinner so quick buffer
    /// recoveries don't flash chrome on screen.</summary>
    public event Action<bool>? BufferingChanged;

    public bool IsBufferingForCache => _mpv?.IsBufferingForCache ?? false;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action? PlaybackEnded;
    public event Action? ContentLoaded; // fired when file is loaded and decoding starts
    /// <summary>
    /// Fired instead of <see cref="PlaybackEnded"/> when the current episode
    /// finishes AND a next episode is available (NextEpisode* fields are set).
    /// The PlayerOverlay uses this to show its "Up next" screen. If the user
    /// cancels, call <see cref="CancelPlayingNext"/>; if they accept, call
    /// <see cref="ContinuePlayingNextAsync"/>.
    /// </summary>
    public event Action? ShowPlayingNextRequested;

    // ── Next-episode metadata (set by ItemDetailPage before playback) ────

    /// <summary>Content ID of the next episode to play. Null = no prompt shown at end.</summary>
    public string? NextEpisodeContentId { get; set; }
    public string? NextEpisodeTitle { get; set; }
    public string? NextEpisodeSeriesTitle { get; set; }
    public string? NextEpisodePosterUrl { get; set; }
    public string? NextEpisodeOverview { get; set; }

    // ── State transitions ────────────────────────────────────────────────

    public void SetState(PlayerState newState)
    {
        var threadId = Environment.CurrentManagedThreadId;
        LogToFile("state_trace.txt", $"SetState: {State} -> {newState} (thread={threadId})");

        // Even if the state hasn't changed, we MUST still ensure the video
        // popup is visible when entering Expanded — ContinuePlayingNextAsync
        // hides the popup for the UP NEXT cinematic but the state stays
        // Expanded throughout. Without this, Expanded → Expanded skips the
        // Show() call and the popup stays hidden.
        if (State == newState)
        {
            if (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen)
                _videoWindow?.Show();
            return;
        }
        State = newState;

        if (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen)
        {
            _videoWindow?.Show();
            _mpv?.SendScriptMessage("osc-set-visibility", "true");
        }
        else if (newState == PlayerState.Minimized)
        {
            _mpv?.SendScriptMessage("osc-set-visibility", "false");
            PositionVideoForMiniBar();
        }
        else
            _videoWindow?.Hide();

        try
        {
            StateChanged?.Invoke(newState);
            LogToFile("state_trace.txt", $"StateChanged invoked OK for {newState}");
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"StateChanged THREW for {newState}: {ex}");
        }
    }

    public void Minimize()
    {
        if (State == PlayerState.Expanded || State == PlayerState.Fullscreen)
        {
            if (State == PlayerState.Fullscreen)
                ExitAnyFullscreen();
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

        // Defensive: if _savedStyle/_savedRect were never populated (e.g. the
        // caller accidentally hits this when the popup was the thing in
        // fullscreen, not the main window), don't stomp the main window to a
        // zero-sized styleless rect. Just flip state and bail.
        if (_savedStyle == 0 || (_savedRect.Right - _savedRect.Left) <= 0)
        {
            SetState(PlayerState.Expanded);
            return;
        }

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

    /// <summary>
    /// Exits whichever fullscreen path is currently active.
    ///
    /// There are two independent ways to enter fullscreen:
    ///   1. <see cref="EnterFullscreen"/> — main window Win32 fullscreen. Saves
    ///      <c>_savedStyle</c> / <c>_savedRect</c> on this service.
    ///   2. <see cref="MpvVideoWindow.EnterFullscreen"/> — native popup fullscreen.
    ///      Saves its own rect on the popup object.
    /// Only one of these is ever active at a time. Calling the wrong
    /// <c>ExitFullscreen</c> for the active path restores uninitialized state
    /// (zeroed rect/style) and collapses the main window to (0,0) 0x0 — which is
    /// how the "app disappears, mini bar lands on the wrong monitor" bug used to
    /// manifest when minimizing from popup-fullscreen.
    /// </summary>
    private void ExitAnyFullscreen()
    {
        if (State != PlayerState.Fullscreen) return;
        if (_videoWindow?.IsFullscreen == true)
        {
            _videoWindow.ExitFullscreen();
            SetState(PlayerState.Expanded);
        }
        else
        {
            ExitFullscreen();
        }
    }

    // ── Playback ─────────────────────────────────────────────────────────

    /// <summary>
    /// Start playback for an item. Supports pre-play audio and subtitle selection.
    /// </summary>
    /// <param name="subtitleSelection">
    /// Pre-play subtitle choice. null = auto (let mpv pick default);
    /// -1 = off (no subtitles); 0+ = explicit embedded track index
    /// (0-based, will be translated to mpv's 1-based sid).
    /// </param>
    public async Task PlayAsync(string contentId, bool fromStart = false, int? fileId = null, int? audioTrackIndex = null, int? subtitleSelection = null)
    {
        _pendingSubtitleSelection = subtitleSelection;
        LogToFile("state_trace.txt", $"PlayAsync called: contentId={contentId} fromStart={fromStart} audioTrackIndex={audioTrackIndex?.ToString() ?? "auto"} subtitleSelection={FormatSubtitleSelection(subtitleSelection)} State={State} IsLoading={IsLoading}");

        // CRITICAL: set the "switching content" flag BEFORE stopping the
        // previous mpv session. Without it, _mpv?.Stop() fires end-file →
        // _mpvPlaybackEndedHandler runs the natural-end cleanup path
        // (CloseAsync), which races against the new-session setup below
        // and crashes the player. The flag causes the handler to short-circuit.
        _switchingContent = true;

        // Stop any existing session first (prevents HTTP 400 from server)
        if (_playbackManager != null)
        {
            try
            {
                _mpv?.Stop();
                await _playbackManager.StopSessionAsync();
            }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Stop previous session error: {ex.Message}"); }
            _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
            _playbackManager.Dispose();
            _playbackManager = null;
            await Task.Delay(200);
        }

        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;
        _resumePosition = 0;
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        _prematureEofLastAttemptMs = 0;
        _prematureEofStreak = 0;
        ResetNaturalEndTracking();
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();

        App.MainWindowInstance?.ShowLoadingOverlay();

        try
        {
            _playbackManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient);
            _playbackManager.ProgressReportingFailed += OnProgressReportingFailed;

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

            var session = await _playbackManager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: fromStart, audioTrackIndex: audioTrackIndex);
            PlayMethod = session.PlayMethod;
            try { SessionStarted?.Invoke(session.SessionId); } catch { }

            if (!fromStart && session.Position > 0 && startPosition == 0)
            {
                startPosition = session.Position;
                LogToFile("state_trace.txt", $"Using server session position: {session.Position:F1}");
            }

            var initialStreamUrl = _playbackManager.StreamUrl;
            if (string.IsNullOrEmpty(initialStreamUrl))
            {
                ErrorMessage = "No stream URL available.";
                IsLoading = false;
                return;
            }
            string streamUrl = initialStreamUrl;

            // HLS transcode fallback
            var (transcodeUrl, transcodeStartPos) = await HandleTranscodeFallbackAsync(session, bestVersion, startPosition);
            if (transcodeUrl != null)
            {
                streamUrl = transcodeUrl;
                startPosition = transcodeStartPos!.Value;
            }

            EnsureMpvInitialized();

            SetState(PlayerState.Expanded);

            if (session.PlayMethod != "transcode")
                streamUrl = PrepareDirectStreamForMpv(streamUrl, session.PlayMethod);

            var mpvStartPosition = session.PlayMethod != "transcode" ? startPosition : 0;
            _resumePosition = session.PlayMethod == "transcode" ? startPosition : 0;

            LogToFile("state_trace.txt", $"LoadFile: url={streamUrl.Substring(0, Math.Min(80, streamUrl.Length))}...");

            // Phase 2b: apply pre-play subtitle selection by setting mpv's "sid"
            // property BEFORE loadfile so the initial state is the user's choice.
            // -1 = "no" (off), 0+ = 1-based mpv sid. null = don't touch, let mpv default.
            ApplyPendingSubtitleSelection();

            _mpv!.LoadFile(streamUrl!, null, mpvStartPosition);
            _mpv.Play();
            LogToFile("state_trace.txt", "Play() called");
            IsPaused = false;

            _mpv.SendScriptMessage("osc-set-play-method", session.PlayMethod ?? "direct");
            IsLoading = false;
        }
        catch (Exception ex)
        {
            LogToFile("player_crash.txt", ex.ToString());
            var (errTitle, errDetail) = DescribePlaybackError(ex);
            ErrorMessage = errDetail;
            IsLoading = false;
            _switchingContent = false;

            _videoWindow?.Hide();
            if (_playbackManager != null)
            {
                try { await _playbackManager.StopSessionAsync(); } catch (Exception stopEx) { LogToFile("state_trace.txt", $"StopSession error: {stopEx.Message}"); }
                _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
                _playbackManager.Dispose();
                _playbackManager = null;
            }
            SetState(PlayerState.Idle);
            App.MainWindowInstance?.ShowPlaybackError(errTitle, errDetail);
        }
    }

    // Mirrors webui describePlaybackSessionError (commit 8115bdb). Returns a
    // (title, detail) pair tuned to the failure mode so the user sees a useful
    // message instead of a raw exception string.
    private static (string Title, string Detail) DescribePlaybackError(Exception ex)
    {
        if (ex is ApiException api)
        {
            if (api.StatusCode == 404 && api.ErrorCode == "not_found")
            {
                if (api.Message == "Source media file is missing")
                    return ("This video is no longer available",
                        "The file needed to play it can't be found right now. Go back and try another version if one is available.");
                return ("This item is no longer available",
                    "The file needed to play this item can't be found right now. Go back and try another version if one is available.");
            }
            if (api.StatusCode == 403)
                return ("Playback unavailable", "You do not have permission to play this item.");
            if (api.StatusCode >= 500)
                return ("Playback unavailable", "Continuum could not start playback right now. Please try again.");
            return ("Playback unavailable", string.IsNullOrWhiteSpace(api.Message) ? "Playback could not start." : api.Message);
        }
        if (ex.Message == "No compatible file version found")
            return ("No compatible version found", "Continuum could not find a playable version for this device.");
        if (!string.IsNullOrWhiteSpace(ex.Message))
            return ("Playback unavailable", ex.Message);
        return ("Playback unavailable", "Playback could not start.");
    }

    /// <summary>
    /// Handles <see cref="PlaybackManager.ProgressReportingFailed"/>. Fired
    /// when 3 consecutive progress POSTs fail or time out — the server has
    /// almost certainly reaped the session, so keeping mpv alive just leaves
    /// it chirping the last audio buffer until the user force-closes. Surface
    /// a clear error and tear the session down on the UI thread.
    /// </summary>
    private void OnProgressReportingFailed(string message)
    {
        LogToFile("state_trace.txt", $"ProgressReportingFailed: {message}");
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        Action handle = () =>
        {
            ErrorMessage = message;
            App.MainWindowInstance?.ShowPlaybackError("Connection Lost", message);
            _ = CloseAsync();
        };
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => handle());
        else
            handle();
    }

    private void StartPlaybackStallWatchdog()
    {
        StopPlaybackStallWatchdog();
        _stallDetector.Reset(_mpv?.Position ?? 0, DateTimeOffset.UtcNow);
        _naturalEndDetector.Reset();
        _stallWatchdogTimer = new Timer(_ => CheckPlaybackStall(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopPlaybackStallWatchdog()
    {
        _stallWatchdogTimer?.Dispose();
        _stallWatchdogTimer = null;
        _stallDetector.Reset();
        _naturalEndDetector.Reset();
        _stallRecoveryLastAttemptMs = 0;
    }

    private void ResetNaturalEndTracking()
    {
        _naturalEndDetector.Reset();
        _naturalEndDispatched = false;
    }

    private void CheckPlaybackStall()
    {
        try
        {
            var mpv = _mpv;
            var manager = _playbackManager;
            var recoveryInProgress = _closing ||
                State == PlayerState.Idle ||
                _switchingContent ||
                _qualitySwitchActive ||
                _prematureEofRecoveryActive ||
                manager?.CurrentSession == null;

            if (mpv == null)
                return;

            var now = DateTimeOffset.UtcNow;
            if (!_naturalEndDispatched)
            {
                var naturalEndDecision = _naturalEndDetector.Observe(
                    mpv.Position,
                    mpv.Duration,
                    recoveryInProgress,
                    now);

                if (naturalEndDecision.ShouldComplete)
                {
                    LogToFile("state_trace.txt",
                        $"Logical natural end detected: reason={naturalEndDecision.Reason} pos={naturalEndDecision.Position:F1} dur={naturalEndDecision.Duration:F1}");
                    HandleNaturalPlaybackEnded(naturalEndDecision.Reason);
                    return;
                }
            }

            var decision = _stallDetector.Observe(
                mpv.Position,
                mpv.Duration,
                mpv.IsPaused,
                mpv.IsBufferingForCache,
                recoveryInProgress,
                now);

            if (!decision.ShouldRecover)
                return;

            var nowMs = Environment.TickCount64;
            if (_stallRecoveryLastAttemptMs > 0 && nowMs - _stallRecoveryLastAttemptMs < 30_000)
                return;

            _stallRecoveryLastAttemptMs = nowMs;
            LogToFile("state_trace.txt",
                $"Playback stall detected: reason={decision.Reason} pos={decision.Position:F1} dur={mpv.Duration:F1} paused={mpv.IsPaused} buffering={mpv.IsBufferingForCache}. Restarting stream...");
            _ = RecoverInterruptedStreamAsync(decision.Position, decision.Reason);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Playback stall watchdog error: {ex.Message}");
        }
    }

    private async Task RecoverInterruptedStreamAsync(double currentPosition, string reason)
    {
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        if (_mpv == null || manager == null || session == null)
        {
            LogToFile("state_trace.txt", $"Stream recovery skipped ({reason}): player/session unavailable");
            return;
        }

        var fileId = session.MediaFileId;
        int? audioTrackIndex = session.AudioTrackIndex >= 0 ? session.AudioTrackIndex : null;
        var resumePosition = Math.Max(0, currentPosition - 2);
        var ct = _playbackCts?.Token ?? CancellationToken.None;

        _prematureEofRecoveryActive = true;
        _prematureEofRecoveryPosition = resumePosition;
        _switchingContent = true;
        IsLoading = true;

        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        dispatcher?.TryEnqueue(() => App.MainWindowInstance?.ShowLoadingOverlay());

        try
        {
            LogToFile("state_trace.txt", $"Stream recovery ({reason}): restarting session fileId={fileId} pos={resumePosition:F1} audioTrack={audioTrackIndex?.ToString() ?? "auto"}");

            if (!string.IsNullOrEmpty(manager.SessionId))
            {
                try
                {
                    using var progressCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _playbackApi.ReportProgressAsync(manager.SessionId, resumePosition, false, progressCts.Token);
                }
                catch (Exception ex)
                {
                    LogToFile("state_trace.txt", $"Stream recovery ({reason}) progress sync failed: {ex.Message}");
                }
            }

            try { await manager.StopSessionAsync(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Stream recovery ({reason}) stop-session failed: {ex.Message}"); }

            if (_closing || State == PlayerState.Idle || !ReferenceEquals(_playbackManager, manager) || ct.IsCancellationRequested)
                return;

            var newSession = await manager.StartSessionAsync(fileId, resumePosition, forceStartPosition: true, audioTrackIndex: audioTrackIndex, ct);
            try { SessionStarted?.Invoke(newSession.SessionId); } catch { }
            PlayMethod = newSession.PlayMethod;

            var streamUrl = manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
                throw new InvalidOperationException("No stream URL returned during premature EOF recovery.");

            var version = Versions.FirstOrDefault(v => v.FileId == fileId);
            if (newSession.PlayMethod == "transcode" && version != null)
            {
                var (transcodeUrl, transcodeStartPos) = await HandleTranscodeFallbackAsync(newSession, version, resumePosition);
                if (transcodeUrl != null)
                {
                    streamUrl = transcodeUrl;
                    resumePosition = transcodeStartPos!.Value;
                    _prematureEofRecoveryPosition = resumePosition;
                }
            }

            if (_closing || State == PlayerState.Idle || !ReferenceEquals(_playbackManager, manager) || ct.IsCancellationRequested)
                return;

            if (newSession.PlayMethod != "transcode")
                streamUrl = PrepareDirectStreamForMpv(streamUrl, newSession.PlayMethod);

            var mpvStartPosition = newSession.PlayMethod != "transcode" ? resumePosition : 0;
            _resumePosition = newSession.PlayMethod == "transcode" ? resumePosition : 0;
            _mpv!.LoadFile(streamUrl, null, mpvStartPosition);
            _mpv.Play();
            IsPaused = false;
            _mpv.SendScriptMessage("osc-set-play-method", newSession.PlayMethod ?? "direct");
            LogToFile("state_trace.txt", $"Stream recovery ({reason}) LoadFile issued at pos={resumePosition:F1} playMethod={newSession.PlayMethod}");
        }
        catch (OperationCanceledException) when (_closing || ct.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", $"Stream recovery ({reason}) canceled");
        }
        catch (Exception ex)
        {
            LogToFile("player_recovery_error.txt", ex.ToString());
            _prematureEofRecoveryActive = false;
            _prematureEofRecoveryPosition = 0;
            _switchingContent = false;
            IsLoading = false;

            var message = $"Playback stalled and could not resume: {ex.Message}";
            ErrorMessage = message;
            Action handleFailure = () =>
            {
                App.MainWindowInstance?.HideLoadingOverlay();
                App.MainWindowInstance?.ShowPlaybackError("Playback Stalled", message);
                _ = CloseAsync();
            };

            if (dispatcher != null)
                dispatcher.TryEnqueue(() => handleFailure());
            else
                handleFailure();
        }
    }

    private double _resumePosition;

    /// <summary>
    /// Phase 3b — start playback of the queued next episode after the user
    /// approved the Playing Next prompt (or the countdown expired).
    /// Clears the next-episode state so the new session has no stale hint.
    /// </summary>
    public Task ContinuePlayingNextAsync()
    {
        var nextId = NextEpisodeContentId;
        ClearNextEpisodeHint();
        if (string.IsNullOrEmpty(nextId)) return Task.CompletedTask;

        // PlayAsync handles old-session cleanup internally with
        // _switchingContent = true set BEFORE _mpv.Stop(), which suppresses
        // the end-file handler. Calling CloseAsync here would over-kill
        // the teardown and break transcode sessions (the proxy + manifest
        // get torn down before the new session can start).
        return PlayAsync(nextId);
    }

    /// <summary>
    /// Phase 3b — user dismissed the Playing Next prompt. Clears the next
    /// episode state and fully tears down the player (same cleanup as a
    /// natural end-of-file without a queued next episode).
    /// </summary>
    public void CancelPlayingNext()
    {
        ClearNextEpisodeHint();
        PlaybackEnded?.Invoke();
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => _ = CloseAsync());
        else
            _ = CloseAsync();
    }

    /// <summary>
    /// Hide the mpv popup window without changing player state. Used by
    /// MainWindow when the Playing Next cinematic fires — the main window
    /// needs to be visible so the overlay Grid shows, but we don't want
    /// to fully tear down the player (state stays Expanded) in case the
    /// user hits "Play Now" and we resume straight into the next episode.
    /// </summary>
    public void HideVideoPopup()
    {
        _videoWindow?.Hide();
    }

    /// <summary>Re-show the mpv popup window (complement to <see cref="HideVideoPopup"/>).</summary>
    public void ShowVideoPopup()
    {
        _videoWindow?.Show();
    }

    /// <summary>Reset all next-episode fields to their default null state.</summary>
    private void ClearNextEpisodeHint()
    {
        NextEpisodeContentId = null;
        NextEpisodeTitle = null;
        NextEpisodeSeriesTitle = null;
        NextEpisodePosterUrl = null;
        NextEpisodeOverview = null;
        // Also tell the OSC to drop its "Next Episode" button.
        _mpv?.SendScriptMessage("osc-set-next-episode", "false");
    }

    /// <summary>
    /// Auto-compute the next-episode hint for a series episode that's just
    /// started playing. Runs in the background so it doesn't block
    /// FileLoaded. If the current item is an episode with a known series +
    /// season + episode number, fetch the season's episode list and pick
    /// the next one by number. Set on <see cref="NextEpisodeContentId"/>
    /// so the end-of-file handler can fire the Playing Next cinematic.
    ///
    /// Called from the FileLoaded handler when no caller pre-set a hint.
    /// This makes the Playing Next flow work for playback launched from
    /// anywhere — home sections, cards, swipe decks, ItemDetailPage —
    /// without each launch site having to compute next manually.
    /// </summary>
    private async Task AutoDetectNextEpisodeAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(100, ct); // Let WatchDetail settle
            var wd = _playbackManager?.WatchDetail;
            if (wd == null) return;
            if (string.IsNullOrEmpty(wd.SeriesId)) return;
            if (!wd.SeasonNumber.HasValue || !wd.EpisodeNumber.HasValue) return;

            var episodes = await _catalogApi.GetEpisodesAsync(wd.SeriesId, wd.SeasonNumber.Value, ct);
            if (ct.IsCancellationRequested) return;
            if (episodes.Episodes == null || episodes.Episodes.Count == 0) return;

            int currentNumber = wd.EpisodeNumber.Value;
            var next = episodes.Episodes.FirstOrDefault(ep => ep.EpisodeNumber == currentNumber + 1);
            if (next == null)
            {
                LogToFile("state_trace.txt", $"AutoDetectNextEpisode: no next episode after S{wd.SeasonNumber} E{currentNumber}");
                return;
            }

            NextEpisodeContentId = next.ContentId;
            var label = $"S{next.SeasonNumber} E{next.EpisodeNumber}";
            NextEpisodeTitle = string.IsNullOrEmpty(next.Title) ? label : $"{label} \u00B7 {next.Title}";
            NextEpisodeSeriesTitle = wd.SeriesTitle;
            NextEpisodePosterUrl = next.StillUrl;
            NextEpisodeOverview = next.Overview;
            LogToFile("state_trace.txt", $"AutoDetectNextEpisode: next={next.ContentId} ({NextEpisodeTitle})");

            // Tell the Lua OSC that a next episode is queued so it can
            // show the in-player "Next Episode" button during credits /
            // the final 5%.
            _mpv?.SendScriptMessage("osc-set-next-episode", "true");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogToFile("state_trace.txt", $"AutoDetectNextEpisode error: {ex.Message}"); }
    }

    /// <summary>
    /// Push the pending pre-play subtitle selection into mpv via the "sid"
    /// property. Called before <see cref="MpvPlayer.LoadFile"/> so the initial
    /// subtitle state matches the user's choice. Translates the 0-based track
    /// index from the UI to mpv's 1-based sid.
    /// </summary>
    private void ApplyPendingSubtitleSelection()
    {
        if (_mpv == null) return;
        var sel = _pendingSubtitleSelection;
        if (sel == null) return;              // Auto — let mpv pick default
        try
        {
            if (sel.Value == -1)
            {
                _mpv.SetProperty("sid", "no");
            }
            else if (sel.Value >= 0)
            {
                // UI stores a 0-based embedded track index; mpv sid is 1-based.
                _mpv.SetProperty("sid", (sel.Value + 1).ToString());
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"ApplyPendingSubtitleSelection failed: {ex.Message}");
        }
    }

    private static string FormatSubtitleSelection(int? sel)
    {
        if (sel == null) return "auto";
        if (sel.Value == -1) return "off";
        return $"track#{sel.Value}";
    }

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
        bestVersion ??= _playbackManager!.SelectBestVersion(versions, userData: watchDetail.UserData);
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

    private async Task<(string? streamUrl, double? startPosition)> HandleTranscodeFallbackAsync(PlaybackStartResponse session, FileVersion bestVersion, double startPosition)
    {
        if (session.PlayMethod != "transcode") return (null, null);

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
            var remoteManifestUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";

            // B52: route the initial transcode manifest through the local HLS
            // proxy (same as the quality-switch path). The proxy catches 404s
            // on segments the encoder hasn't produced yet and retries for up
            // to ~45 s — mpv alone just fails. Without this, starting on a
            // transcoded stream sometimes stalls on `seg_NNNNN.m4s` 404s.
            StopDirectStreamProxy();
            _hlsProxy?.Stop();
            _hlsProxy = new HlsProxy(remoteManifestUrl, _apiClient.AccessToken);
            var localUrl = _hlsProxy.Start();
            LogToFile("state_trace.txt", $"Initial transcode via HLS proxy: remote={remoteManifestUrl} local={localUrl} playerStart={transcodeResponse.PlayerStartSeconds}");

            return (localUrl, transcodeResponse.PlayerStartSeconds);
        }
        catch (Exception ex)
        {
            LogToFile("player_transcode_error.txt", ex.ToString());
            throw new InvalidOperationException("Failed to start transcode playback.", ex);
        }
    }

    private string PrepareDirectStreamForMpv(string remoteStreamUrl, string? playMethod)
    {
        if (string.IsNullOrWhiteSpace(remoteStreamUrl))
            throw new InvalidOperationException("No direct stream URL available.");

        StopDirectStreamProxy();
        _hlsProxy?.Stop();
        _hlsProxy = null;

        _directStreamProxy = new DirectStreamProxy(remoteStreamUrl, () => _apiClient.AccessToken);
        var localUrl = _directStreamProxy.Start();
        LogToFile("state_trace.txt", $"Direct/remux stream via local proxy: method={playMethod ?? "direct"} local={localUrl}");
        return localUrl;
    }

    private void StopDirectStreamProxy()
    {
        try
        {
            _directStreamProxy?.Dispose();
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Direct stream proxy stop error: {ex.Message}");
        }
        finally
        {
            _directStreamProxy = null;
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

        // Restore the persisted volume + mute on this fresh mpv instance so
        // the first track obeys the saved level instead of mpv's default.
        try
        {
            _mpv.SetVolume(Volume);
            if (IsMuted) _mpv.SetMute(true);
        }
        catch { /* non-fatal */ }

        // Apply cached subtitle appearance settings (B55 follow-up) so the
        // first track starts with the user's saved sub styling instead of
        // mpv's defaults. If nothing has been cached yet, mpv uses defaults
        // and gets updated on the next SettingsPage save.
        if (_subtitleAppearance != null)
            PushSubtitleAppearanceToMpv(_subtitleAppearance);
    }

    private void UnwireMpvEvents()
    {
        if (_mpv == null) return;
        if (_mpvPositionHandler != null) _mpv.PositionChanged -= _mpvPositionHandler;
        if (_mpvDurationHandler != null) _mpv.DurationChanged -= _mpvDurationHandler;
        if (_mpvPauseHandler != null) _mpv.PauseChanged -= _mpvPauseHandler;
        if (_mpvBufferingHandler != null) _mpv.BufferingChanged -= _mpvBufferingHandler;
        if (_mpvFileLoadedHandler != null) _mpv.FileLoaded -= _mpvFileLoadedHandler;
        if (_mpvPlaybackEndedHandler != null) _mpv.PlaybackEnded -= _mpvPlaybackEndedHandler;
        if (_mpvPlaybackErrorHandler != null) _mpv.PlaybackError -= _mpvPlaybackErrorHandler;
        if (_mpvErrorHandler != null) _mpv.Error -= _mpvErrorHandler;
        _mpv.ScriptMessageReceived -= OnScriptMessage;
    }

    private void HandleNaturalPlaybackEnded(string trigger)
    {
        if (_naturalEndDispatched)
        {
            LogToFile("state_trace.txt", $"  -> Natural end already dispatched (trigger={trigger})");
            return;
        }

        _naturalEndDispatched = true;
        StopPlaybackStallWatchdog();

        // Phase 3b: if the caller set a next-episode hint before playback,
        // show the Playing Next overlay instead of closing the player.
        // The overlay will call ContinuePlayingNextAsync or CancelPlayingNext.
        // Playing Next is intentionally tied to the real/logical end of media.
        // Credits markers are useful for UI markers/skip affordances, but
        // auto-detected credits can be wrong by many minutes; they must not
        // force the video into the mini bar or start the next-episode timer.
        if (!string.IsNullOrEmpty(NextEpisodeContentId))
        {
            if (_playingNextShown)
            {
                LogToFile("state_trace.txt", $"  -> Next-episode prompt already shown (trigger={trigger})");
                return;
            }

            LogToFile("state_trace.txt", $"  -> Next-episode prompt requested (trigger={trigger})");
            _playingNextShown = true;
            ShowPlayingNextRequested?.Invoke();
            return;
        }

        // CRITICAL: end-of-media means the session is DONE. We MUST tear
        // down the mpv/playback-manager state or the next PlayAsync call
        // will race against stale state and crash.
        LogToFile("state_trace.txt", $"  -> Natural end ({trigger}) - dispatching CloseAsync to UI thread");
        PlaybackEnded?.Invoke();
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => _ = CloseAsync());
        else
            _ = CloseAsync();
    }

    private void WireMpvEvents()
    {
        if (_mpv == null) return;
        UnwireMpvEvents();

        _mpvPositionHandler = (pos) =>
        {
            Position = pos;
            _playbackManager?.UpdatePosition(pos, IsPaused);
            PositionChanged?.Invoke(pos);

            if (_prematureEofRecoveryPosition > 0 && pos > _prematureEofRecoveryPosition + 30)
            {
                LogToFile("state_trace.txt", $"Stream recovery confirmed: advanced from {_prematureEofRecoveryPosition:F1} to {pos:F1}");
                _prematureEofRecoveryPosition = 0;
                _prematureEofLastAttemptMs = 0;
                _prematureEofStreak = 0;
            }
        };
        _mpv.PositionChanged += _mpvPositionHandler;

        _mpvDurationHandler = (dur) =>
        {
            Duration = dur;
            DurationChanged?.Invoke(dur);
        };
        _mpv.DurationChanged += _mpvDurationHandler;

        _mpvPauseHandler = (paused) =>
        {
            IsPaused = paused;
            PauseChanged?.Invoke(paused);
        };
        _mpv.PauseChanged += _mpvPauseHandler;

        _mpvBufferingHandler = (buffering) =>
        {
            LogToFile("state_trace.txt", $"BufferingForCache changed: {buffering} pos={_mpv?.Position:F1} paused={_mpv?.IsPaused}");
            BufferingChanged?.Invoke(buffering);
        };
        _mpv.BufferingChanged += _mpvBufferingHandler;

        _mpvFileLoadedHandler = () =>
        {
            var wasPrematureEofRecovery = _prematureEofRecoveryActive;
            IsLoading = false;
            _switchingContent = false; // Safe to receive PlaybackEnded now
            _qualitySwitchActive = false;
            _playingNextShown = false;
            ResetNaturalEndTracking();
            _prematureEofRecoveryActive = false;
            if (!wasPrematureEofRecovery)
            {
                _prematureEofStreak = 0;
                _prematureEofLastAttemptMs = 0;
                _prematureEofRecoveryPosition = 0;
            }
            App.MainWindowInstance?.HideLoadingOverlay();
            LogToFile("state_trace.txt", wasPrematureEofRecovery
                ? "FileLoaded fired (premature EOF recovery)"
                : "FileLoaded fired");
            StartPlaybackStallWatchdog();

            ContentLoaded?.Invoke();

            // Seek to resume position FIRST — before subtitles block the thread
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

            // Send OSC data + load subtitles on background thread with cancellation
            var ct = _playbackCts?.Token ?? CancellationToken.None;
            Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;
                SendTitleToOsc();
                SendMediaInfoToOsc();
                SendSubtitleListToOsc();
                SendQualityInfoToOsc();
                SendMarkersToOsc();
                if (ct.IsCancellationRequested) return;
                LoadSubtitles();
            }, ct);

            // Auto-compute next-episode hint if no caller already set one.
            // This fires for every playback session — including ones launched
            // directly from a card (LandscapeCard, PosterCard) that bypass
            // ItemDetailPage.SetNextEpisodeHintIfApplicable.
            if (string.IsNullOrEmpty(NextEpisodeContentId))
                _ = AutoDetectNextEpisodeAsync(ct);

            // Connect WebSocket for real-time admin control
            try { ConnectWebSocket(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"WebSocket connect failed: {ex.Message}"); }
        };
        _mpv.FileLoaded += _mpvFileLoadedHandler;

        _mpvPlaybackEndedHandler = () =>
        {
            LogToFile("state_trace.txt", $"PlaybackEnded fired: _switchingContent={_switchingContent} _qualitySwitchActive={_qualitySwitchActive} _prematureEofRecoveryActive={_prematureEofRecoveryActive} _closing={_closing} nextEpisode={NextEpisodeContentId ?? "none"} State={State} thread={Environment.CurrentManagedThreadId}");
            if (_switchingContent || _qualitySwitchActive || _prematureEofRecoveryActive)
            {
                LogToFile("state_trace.txt", "  → Suppressed (switching content)");
                return;
            }

            // Guard against end-file events that fire during CloseAsync
            // (e.g. _mpv.Stop() inside CloseAsync triggers end-file, or
            // the player is already torn down to Idle). Without this,
            // each _mpv.Stop() re-queues CloseAsync which races against
            // ContinuePlayingNextAsync → PlayAsync.
            if (_closing || State == PlayerState.Idle)
            {
                LogToFile("state_trace.txt", "  → Suppressed (closing or idle)");
                return;
            }

            // Premature EOF detection: if mpv says "end of file" but we're
            // nowhere near the actual end (>5% remaining), the HTTP connection
            // was dropped by the CDN/server. Instead of closing or seeking in
            // place, restart the server playback session and reload mpv from
            // a fresh stream URL at the same timestamp.
            //
            // The old Seek+Play recovery could land inside mpv's cached EOF
            // range, leaving the play button visible but unable to resume.
            // Keep a loop guard so genuinely stuck files still bail out.
            var pos = _mpv?.Position ?? 0;
            var dur = _mpv?.Duration ?? 0;
            if (dur > 0 && pos > 10 && pos < dur * 0.95)
            {
                var nowMs = Environment.TickCount64;
                var sinceLastAttempt = nowMs - _prematureEofLastAttemptMs;

                if (_prematureEofLastAttemptMs > 0 && sinceLastAttempt < 1500)
                {
                    // Previous restart has not had time to settle. Ignore.
                    LogToFile("state_trace.txt", $"  → Premature EOF at pos={pos:F1} — retry in progress ({sinceLastAttempt}ms since last), ignoring");
                    return;
                }

                // New EOF event (or cooldown elapsed). Count against the streak.
                _prematureEofStreak = (_prematureEofLastAttemptMs > 0 && sinceLastAttempt < 5000)
                    ? _prematureEofStreak + 1
                    : 1;

                if (_prematureEofStreak >= 3)
                {
                    LogToFile("state_trace.txt", $"  → Premature EOF stuck at pos={pos:F1} (streak={_prematureEofStreak}). Giving up, closing player.");
                    _prematureEofStreak = 0;
                    _prematureEofLastAttemptMs = 0;
                    ErrorMessage = "Playback stalled and couldn't resume. The stream may be corrupted at this position.";
                    // Fall through to the normal close path below.
                }
                else
                {
                    _prematureEofLastAttemptMs = nowMs;
                    LogToFile("state_trace.txt", $"  → Premature EOF detected (pos={pos:F1} dur={dur:F1}, {(1 - pos/dur)*100:F0}% remaining, streak={_prematureEofStreak}). Restarting stream...");
                    try
                    {
                        _ = RecoverInterruptedStreamAsync(pos, "premature-eof");
                    }
                    catch (Exception ex)
                    {
                        LogToFile("state_trace.txt", $"  → Premature EOF recovery dispatch failed: {ex.Message}");
                    }
                    return;
                }
            }

            // Phase 3b: if the caller set a next-episode hint before playback,
            // show the Playing Next overlay instead of closing the player.
            // The overlay will call ContinuePlayingNextAsync or CancelPlayingNext.
            // Playing Next is intentionally tied to the real end-of-file.
            // Credits markers are useful for UI markers/skip affordances, but
            // auto-detected credits can be wrong by many minutes; they must not
            // force the video into the mini bar or start the next-episode timer.
            if (!string.IsNullOrEmpty(NextEpisodeContentId))
            {
                if (_playingNextShown)
                {
                    LogToFile("state_trace.txt", "  → Next-episode prompt already shown");
                    return;
                }
                LogToFile("state_trace.txt", "  → Next-episode prompt requested (end-of-file)");
                _playingNextShown = true;
                ShowPlayingNextRequested?.Invoke();
                return;
            }

            // CRITICAL: end-of-file means the session is DONE. We MUST tear
            // down the mpv/playback-manager state or the next PlayAsync call
            // will race against stale state and crash.
            LogToFile("state_trace.txt", "  → Natural end — dispatching CloseAsync to UI thread");
            PlaybackEnded?.Invoke();
            var dispatcher = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null)
                dispatcher.TryEnqueue(() => _ = CloseAsync());
            else
                _ = CloseAsync();
        };
        _mpv.PlaybackEnded += _mpvPlaybackEndedHandler;

        _mpvPlaybackErrorHandler = (msg) =>
        {
            LogToFile("state_trace.txt", $"PlaybackError: {msg} _switchingContent={_switchingContent} _prematureEofRecoveryActive={_prematureEofRecoveryActive}");
            // If we were waiting for a file to load and it failed, show error
            if (_switchingContent)
            {
                _switchingContent = false;
                _qualitySwitchActive = false;
                _prematureEofRecoveryActive = false;
                _prematureEofRecoveryPosition = 0;
                IsLoading = false;
                ErrorMessage = msg;
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
                {
                    _videoWindow?.Hide();
                    SetState(PlayerState.Idle);
                    // Show user-friendly error instead of silently returning
                    var detail = msg.Contains("loading failed")
                        ? "The media file could not be loaded. It may be unavailable or the server may be experiencing issues."
                        : msg;
                    App.MainWindowInstance?.ShowPlaybackError(detail);
                });
            }
        };
        _mpv.PlaybackError += _mpvPlaybackErrorHandler;

        _mpv.ScriptMessageReceived += OnScriptMessage;
        _mpvErrorHandler = (msg) => LogToFile("mpv_error.txt", msg);
        _mpv.Error += _mpvErrorHandler;
    }

    // ── Content switching (version/audio) ────────────────────────────────

    public async Task SwitchVersionAsync(FileVersion version)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;
        _switchingContent = true;
        // Don't call _mpv.Stop() — let current stream keep playing while we set up the new one

        try
        {
            // Run ALL network calls on background thread so UI never blocks
            var result = await Task.Run(async () =>
            {
                try { await _playbackManager.StopSessionAsync(); } catch (Exception ex) { LogToFile("state_trace.txt", $"StopSession error: {ex.Message}"); }
                var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
                try { SessionStarted?.Invoke(session.SessionId); } catch { }
                var streamUrl = _playbackManager.StreamUrl ?? "";

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
                    catch (Exception ex)
                    {
                        LogToFile("player_transcode_error.txt", ex.ToString());
                        throw new InvalidOperationException("Failed to start transcode playback.", ex);
                    }
                }

                return (session, streamUrl, currentPos);
            });

            PlayMethod = result.session.PlayMethod;
            Resolution = version.Resolution;

            if (string.IsNullOrEmpty(result.streamUrl))
            {
                ErrorMessage = "No stream URL for selected version.";
                _switchingContent = false;
                return;
            }

            var finalUrl = result.streamUrl;
            if (result.session.PlayMethod != "transcode")
            {
                finalUrl = PrepareDirectStreamForMpv(finalUrl, result.session.PlayMethod);
            }
            else
            {
                StopDirectStreamProxy();
                _hlsProxy?.Stop();
                _hlsProxy = new HlsProxy(finalUrl, _apiClient.AccessToken);
                finalUrl = _hlsProxy.Start();
            }

            var mpvStartPosition = result.session.PlayMethod != "transcode" ? result.currentPos : 0;
            _resumePosition = result.session.PlayMethod == "transcode" ? result.currentPos : 0;
            _mpv.LoadFile(finalUrl, null, mpvStartPosition);
            _mpv.Play();
            LoadSubtitles();
            _switchingContent = false;
        }
        catch (Exception ex)
        {
            _switchingContent = false;
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
            _playbackManager.ApplyAudioChange(response);

            var baseUrl = _apiClient.BaseUrl;
            var token = _apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";

            PlayMethod = response.PlayMethod;

            if (response.PlayMethod == "transcode")
            {
                var version = Versions.FirstOrDefault(v => v.FileId == _playbackManager.CurrentSession?.MediaFileId)
                    ?? throw new InvalidOperationException("No active version available for audio transcode switch.");

                var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                {
                    SessionId = _playbackManager.SessionId!,
                    SeekSeconds = currentPos,
                    TargetResolution = version.Resolution,
                    TargetCodecVideo = "h264",
                    TargetCodecAudio = "aac",
                    TargetBitrateKbps = 8000,
                    SegmentDuration = 2,
                    SubtitleTrackIndex = -1,
                    SubtitleBurnIn = false
                });

                var manifestPath = transcodeResponse.ManifestUrl;
                if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                    manifestPath = "/api/v1" + manifestPath;
                var remoteManifestUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";

                StopDirectStreamProxy();
                _hlsProxy?.Stop();
                _hlsProxy = new HlsProxy(remoteManifestUrl, token);
                url = _hlsProxy.Start();
                currentPos = transcodeResponse.PlayerStartSeconds;
            }
            else
            {
                url = PrepareDirectStreamForMpv(url, response.PlayMethod);
            }

            var mpvStartPosition = response.PlayMethod != "transcode" ? currentPos : 0;
            _resumePosition = response.PlayMethod == "transcode" ? currentPos : 0;
            _mpv.LoadFile(url, null, mpvStartPosition);
            _mpv.Play();
            _switchingContent = false;

            // Persist the audio-track choice under the series (or content)
            // ID so the next episode / resume defaults to the same track.
            var key = GetPrefsKey();
            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    // Look up the language from the current version's audio tracks.
                    string? language = null;
                    var version = Versions.FirstOrDefault(v => v.FileId == _playbackManager.CurrentSession?.MediaFileId);
                    if (version?.AudioTracks != null && trackIndex >= 0 && trackIndex < version.AudioTracks.Count)
                        language = version.AudioTracks[trackIndex].Language;

                    await _catalogApi.SetAudioPrefsAsync(key, new Core.Models.Catalog.AudioPreference
                    {
                        AudioTrackIndex = trackIndex,
                        AudioLanguage = language,
                    });
                }
                catch (Exception ex) { LogToFile("state_trace.txt", $"SetAudioPrefs error: {ex.Message}"); }
            }
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

    // ── Sliding-window embedded subtitle fetch (webui parity, commit 75ef59b) ──
    //
    // Upstream swapped embedded-subtitle extraction for a streaming fetch
    // bounded by ?duration= (defaults 600s / max 3600s). mpv downloads the
    // URL once so we need to slide the window ourselves — track each loaded
    // window (start + duration + mpv sid + base URL) and sub-remove/sub-add
    // with a new position before the current window runs out. External and
    // downloaded subs are server-full so they don't need sliding.

    private sealed class EmbeddedSubWindow
    {
        public int Sid { get; set; }
        public string BaseUrl { get; set; } = "";
        public string? Label { get; set; }
        public string? Language { get; set; }
        public double WindowStart { get; set; }
        public double WindowDuration { get; set; }
    }

    private const int SubtitleWindowDurationSeconds = 600;  // Match WebUI sliding-window fetch.
    private const int SubtitleSlidePreloadSeconds = 120;    // 2-min lead time.
    private readonly List<EmbeddedSubWindow> _embeddedSubWindows = [];
    private readonly Dictionary<int, int> _loadedExternalSubtitleSids = [];

    private void LoadSubtitles()
    {
        if (_playbackManager?.CurrentSession == null || _mpv == null) return;

        _embeddedSubWindows.Clear();
        _loadedExternalSubtitleSids.Clear();

        // Do not eagerly sub-add every server subtitle URL. mpv already sees
        // embedded text tracks on direct/remux playback, and adding all VTT
        // URLs upfront can fan out dozens of HTTP/ffmpeg subtitle fetches
        // during 4K startup. External/downloaded tracks are loaded on demand
        // when the user selects one, matching the WebUI's active-track model.
    }

    private static bool IsBitmapSubtitle(SubtitleTrackInfo track)
    {
        var codec = track.Codec?.ToLowerInvariant() ?? "";
        return codec is "pgs" or "pgssub" or "dvdsub" or "vobsub";
    }

    private void SelectSubtitleByServerIndex(int serverTrackIndex)
    {
        if (_mpv == null) return;
        if (serverTrackIndex < 0)
        {
            _mpv.SetSubtitleTrack(0);
            return;
        }

        var track = _playbackManager?.CurrentSession?.SubtitleUrls?
            .FirstOrDefault(t => t.Index == serverTrackIndex);
        if (track == null)
        {
            // Pre-play embedded selection still passes an embedded ordinal.
            _mpv.SetSubtitleTrack(serverTrackIndex + 1);
            return;
        }

        SelectSubtitleTrack(track);
    }

    private void SelectSubtitleTrack(SubtitleTrackInfo track)
    {
        if (_mpv == null || IsBitmapSubtitle(track)) return;

        if (string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase))
        {
            var sid = ResolveNativeEmbeddedSid(track);
            _mpv.SetSubtitleTrack(sid > 0 ? sid : track.Index + 1);
            return;
        }

        if (_loadedExternalSubtitleSids.TryGetValue(track.Index, out var loadedSid))
        {
            _mpv.SetSubtitleTrack(loadedSid);
            return;
        }

        var pair = _playbackManager?.GetSubtitleUrls()
            .FirstOrDefault(p => p.Track.Index == track.Index);
        if (pair == null || string.IsNullOrWhiteSpace(pair.Value.FullUrl))
            return;

        var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language ?? "Unknown";
        var guessedSid = ResolveNativeEmbeddedCount() + _loadedExternalSubtitleSids.Count + 1;
        _loadedExternalSubtitleSids[track.Index] = guessedSid;
        _mpv.AddSubtitle(pair.Value.FullUrl, label, track.Language, select: true);
    }

    private int ResolveNativeEmbeddedSid(SubtitleTrackInfo track)
    {
        var embeddedTracks = _playbackManager?.CurrentSession?.SubtitleUrls?
            .Where(t => string.Equals(t.Source, "embedded", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Index)
            .ToList() ?? [];

        for (int i = 0; i < embeddedTracks.Count; i++)
        {
            if (embeddedTracks[i].Index == track.Index)
                return i + 1;
        }

        return 0;
    }

    private int ResolveNativeEmbeddedCount()
        => _playbackManager?.CurrentSession?.SubtitleUrls?
            .Count(t => string.Equals(t.Source, "embedded", StringComparison.OrdinalIgnoreCase)) ?? 0;

    private static string AppendPositionDuration(string url, double position, int durationSeconds)
    {
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}position={position:0.##}&duration={durationSeconds}";
    }

    /// <summary>
    /// Called from the UI tick; if the current playback position is near the
    /// end of any loaded embedded-subtitle window, slide that window forward
    /// by sub-remove + sub-add with a new position-centered URL. Also called
    /// on seek past coverage.
    /// </summary>
    public void TickSubtitleWindows(double currentPositionSeconds)
    {
        if (_embeddedSubWindows.Count == 0 || _mpv == null) return;

        foreach (var win in _embeddedSubWindows)
        {
            var windowEnd = win.WindowStart + win.WindowDuration;
            // Slide when we're within SlidePreloadSeconds of the tail OR when
            // we've seeked past the current window entirely.
            bool approachingTail = currentPositionSeconds >= windowEnd - SubtitleSlidePreloadSeconds;
            bool pastWindow = currentPositionSeconds >= windowEnd || currentPositionSeconds < win.WindowStart;
            if (!(approachingTail || pastWindow)) continue;

            // Fetch the next window centered on the current position. Using
            // currentPos - 30 ensures cues just before the playhead remain
            // visible (captions often start slightly before dialogue).
            var newStart = Math.Max(0, currentPositionSeconds - 30);
            var newUrl = AppendPositionDuration(win.BaseUrl, newStart, SubtitleWindowDurationSeconds);

            try
            {
                _mpv.RemoveSubtitle(win.Sid);
                _mpv.AddSubtitle(newUrl, win.Label, win.Language);
                win.WindowStart = newStart;
                win.WindowDuration = SubtitleWindowDurationSeconds;
            }
            catch
            {
                // If the reload fails the track may disappear; next tick will
                // retry. Non-fatal — playback continues.
            }
        }
    }

    private void SendTitleToOsc()
    {
        if (_mpv == null) return;
        _mpv.SendScriptMessage("osc-set-title", Title ?? "", Subtitle ?? "");
    }

    private void SendMediaInfoToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

        var wd = _playbackManager.WatchDetail;
        var session = _playbackManager.CurrentSession;
        var version = wd.Versions?.FirstOrDefault(v => v.FileId == session.MediaFileId);
        if (version == null) return;

        var audioTrack = version.AudioTracks?.ElementAtOrDefault(session.AudioTrackIndex);

        var info = new Dictionary<string, object?>
        {
            ["container"] = version.Container ?? "",
            ["file_size"] = version.FileSize,
            ["bitrate"] = version.Bitrate,
            ["codec_video"] = version.CodecVideo ?? "",
            ["codec_audio"] = version.CodecAudio ?? "",
            ["hdr"] = version.Hdr,
            ["resolution"] = version.Resolution ?? "",
            ["audio_channels"] = audioTrack?.Channels ?? version.AudioChannels ?? 0,
            ["audio_title"] = audioTrack?.Title ?? audioTrack?.EmbeddedTitle ?? "",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-media-info", json);

        // Send stream info
        var pi = session.PlaybackInfo;
        var playMethodDisplay = session.PlayMethod switch
        {
            "direct" => "Direct Play",
            "remux" => "Direct Streaming",
            "transcode" => "Transcode",
            _ => session.PlayMethod
        };
        var streamType = pi?.StreamType switch
        {
            "hls" => "HLS",
            _ => "Progressive"
        };
        var streamUrl = _playbackManager.StreamUrl ?? "";
        var protocol = streamUrl.StartsWith("https") ? "https" : "http";

        var vcSuffix = session.PlayMethod == "direct" ? "(direct)" : session.PlayMethod == "remux" ? "(copy)" : "(transcoded)";
        var acSuffix = (pi?.TranscodeAudio == true) ? "(transcoded)" : (session.PlayMethod == "direct" ? "(direct)" : "(copy)");
        var vcDisplay = $"{(pi?.VideoCodec ?? version.CodecVideo ?? "").ToUpper()} {vcSuffix}";
        var acDisplay = $"{(pi?.AudioCodec ?? version.CodecAudio ?? "").ToUpper()} {acSuffix}";

        _mpv.SendScriptMessage("osc-set-stream-info", playMethodDisplay, streamType, protocol, vcDisplay, acDisplay);
    }

    private void SendSubtitleListToOsc()
    {
        if (_mpv == null || _playbackManager?.CurrentSession == null) return;

        var tracks = _playbackManager.CurrentSession.SubtitleUrls ?? [];
        var jsonTracks = tracks.Select(t => new Dictionary<string, object?>
        {
            ["index"] = t.Index,
            ["language"] = t.Language ?? "",
            ["label"] = t.Label ?? "",
            ["source"] = t.Source ?? "embedded",
            ["codec"] = t.Codec ?? "",
            ["forced"] = t.Forced
        }).ToArray();

        var json = System.Text.Json.JsonSerializer.Serialize(jsonTracks);
        _mpv.SendScriptMessage("osc-set-subtitles", json);
        _mpv.SendScriptMessage("osc-set-active-subtitle", "-1");
    }

    private void SendMarkersToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null) return;
        var wd = _playbackManager.WatchDetail;

        var markers = new Dictionary<string, double>
        {
            ["intro_start"] = wd.Intro?.Start ?? 0,
            ["intro_end"] = wd.Intro?.End ?? 0,
            ["credits_start"] = wd.Credits?.Start ?? 0,
            ["credits_end"] = wd.Credits?.End ?? 0
        };

        var json = System.Text.Json.JsonSerializer.Serialize(markers);
        _mpv.SendScriptMessage("osc-set-markers", json);
    }

    private void SendQualityInfoToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null || _playbackManager.CurrentSession == null) return;

        var wd = _playbackManager.WatchDetail;
        var session = _playbackManager.CurrentSession;

        var versions = (wd.Versions ?? []).Select(v => new Dictionary<string, object?>
        {
            ["file_id"] = v.FileId,
            ["label"] = v.FileName ?? $"{v.Resolution} {v.CodecVideo}",
            ["resolution"] = v.Resolution ?? ""
        }).ToArray();

        var info = new Dictionary<string, object?>
        {
            ["versions"] = versions,
            ["active_file_id"] = session.MediaFileId,
            ["active_quality"] = _activeQualityTier
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-quality-info", json);
    }

    // ── Video window events ────────────────────────────────────────────

    private void WireVideoWindowEvents()
    {
        if (_videoWindow == null) return;

        _videoWindow.EscapeRequested += () =>
        {
            if (_videoWindow.IsFullscreen)
            {
                _videoWindow.ExitFullscreen();
                // Dispatch to UI thread for XAML state updates
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Expanded));
            }
            else
            {
                _videoWindow.Hide();
                // Run CloseAsync on UI thread so SetState(Idle) updates XAML properly
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => _ = CloseAsync());
            }
        };
        _videoWindow.MinimizeRequested += () =>
        {
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => Minimize());
        };
        _videoWindow.ExpandRequested += () =>
        {
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => Expand());
        };
        _videoWindow.FullscreenToggleRequested += () =>
        {
            if (_videoWindow.IsFullscreen)
            {
                _videoWindow.ExitFullscreen();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Expanded));
                _mpv?.SendScriptMessage("osc-fullscreen-state", "false");
            }
            else
            {
                _videoWindow.EnterFullscreen();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Fullscreen));
                _mpv?.SendScriptMessage("osc-fullscreen-state", "true");
            }
        };
    }


    // ── Script message dispatch (Lua → Host) ─────────────────────────────

    private void OnScriptMessage(string[] args)
    {
        if (args.Length == 0) return;
        // Only handle continuum-* messages (Lua→Host intents).
        // Ignore echo-back of host→Lua messages (osc-mouse-move etc.) to avoid flooding.
        if (!args[0].StartsWith("continuum-")) return;

        LogToFile("state_trace.txt", $"ScriptMessage received: {args[0]} thread={Environment.CurrentManagedThreadId}");

        var dispatch = App.MainWindowInstance?.DispatcherQueue;
        if (dispatch == null) return;

        switch (args[0])
        {
            case "continuum-exit":
                if (State == PlayerState.Idle) return; // Prevent duplicate close
                dispatch.TryEnqueue(() => _ = CloseAsync());
                break;
            case "continuum-fullscreen-toggle":
                dispatch.TryEnqueue(ToggleFullscreenFromOsc);
                break;
            case "continuum-minimize":
                dispatch.TryEnqueue(Minimize);
                break;
            case "continuum-subtitle-select":
                if (args.Length > 1 && int.TryParse(args[1], out var subIdx))
                {
                    SelectSubtitleByServerIndex(subIdx);
                    _mpv?.SendScriptMessage("osc-set-active-subtitle", args[1]);
                }
                break;
            case "continuum-subtitle-search":
                dispatch.TryEnqueue(() => _ = SearchAndDownloadSubtitlesAsync());
                break;
            case "continuum-version-select":
                if (args.Length > 1 && int.TryParse(args[1], out var vFileId))
                {
                    var version = Versions.FirstOrDefault(v => v.FileId == vFileId);
                    if (version != null)
                    {
                        _switchingContent = true; // Set BEFORE dispatch — event thread may fire PlaybackEnded
                        dispatch.TryEnqueue(() => _ = SwitchVersionAndNotifyAsync(version));
                    }
                }
                break;
            case "continuum-quality-select":
                if (args.Length > 1)
                {
                    _switchingContent = true;
                    _qualitySwitchActive = true;
                    LogToFile("state_trace.txt", $"Quality select: tier={args[1]} flags set TRUE");
                    dispatch.TryEnqueue(() => _ = SwitchQualityTierAsync(args[1]));
                }
                break;
            case "continuum-volume-changed":
                if (args.Length > 1 && double.TryParse(args[1], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var newVol))
                {
                    dispatch.TryEnqueue(() =>
                    {
                        Volume = Math.Clamp(newVol, 0, 100);
                        SaveVolumeState();
                    });
                }
                break;
            case "continuum-next-episode":
                // User clicked the in-player Next Episode button. Jump
                // straight to the next episode — ContinuePlayingNextAsync
                // tears down the current session and starts the next one.
                LogToFile("state_trace.txt", "Next Episode button clicked");
                dispatch.TryEnqueue(() => _ = ContinuePlayingNextAsync());
                break;
            case "continuum-cursor-hidden":
                dispatch.TryEnqueue(() => _videoWindow?.SetCursorVisible(false));
                break;
            case "continuum-cursor-visible":
                dispatch.TryEnqueue(() => _videoWindow?.SetCursorVisible(true));
                break;
        }
    }

    private async Task SwitchVersionAndNotifyAsync(FileVersion version)
    {
        await SwitchVersionAsync(version);
        _activeQualityTier = "original";
        SendQualityInfoToOsc();
        SendMediaInfoToOsc();
        _mpv?.SendScriptMessage("osc-set-active-quality", "original");
    }

    private async Task SwitchQualityTierAsync(string tierId)
    {
        if (_mpv == null || _playbackManager == null) return;

        var currentPos = _mpv.Position;

        if (tierId is "auto" or "original")
        {
            // If already on direct play or remux, this is a no-op
            if (PlayMethod is "direct" or "remux")
            {
                _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
                return;
            }

            // Currently transcoding — switch back to direct play
            var currentFileId = _playbackManager.CurrentSession?.MediaFileId;
            var version = Versions.FirstOrDefault(v => v.FileId == currentFileId);
            if (version != null)
            {
                try
                {
                    await _playbackManager.StopSessionAsync();
                    var session = await _playbackManager.StartSessionAsync(version.FileId, currentPos);
                    try { SessionStarted?.Invoke(session.SessionId); } catch { }
                    PlayMethod = session.PlayMethod;

                    var streamUrl = PrepareDirectStreamForMpv(_playbackManager.StreamUrl ?? "", session.PlayMethod);

                    _resumePosition = 0;
                    _mpv.LoadFile(streamUrl, null, currentPos);
                    _mpv.Play();
                    SendMediaInfoToOsc();
                }
                catch (Exception ex)
                {
                    _switchingContent = false;
                    _qualitySwitchActive = false;
                    LogToFile("player_quality_switch_error.txt", ex.ToString());
                    _mpv.ShowOsdText("Quality switch failed", 3000);
                }
            }
        }
        else
        {
            var (resolution, bitrate) = tierId switch
            {
                "1080p-high" => ("1080p", 10000),
                "1080p"      => ("1080p", 6000),
                "720p-high"  => ("720p", 4000),
                "720p"       => ("720p", 2000),
                "480p"       => ("480p", 1500),
                "420p"       => ("420p", 720),
                _ => ("1080p", 6000)
            };

            // Pause and show loading
            _mpv.Pause();
            App.MainWindowInstance?.ShowLoadingOverlay();

            try
            {
                var transcodeResponse = await _playbackApi.StartTranscodeAsync(new TranscodeStartRequest
                {
                    SessionId = _playbackManager.SessionId!,
                    SeekSeconds = currentPos, // Start encoding from user's position
                    TargetResolution = resolution,
                    TargetCodecVideo = "h264",
                    TargetCodecAudio = "aac",
                    TargetBitrateKbps = bitrate,
                    SegmentDuration = 2,
                    SubtitleTrackIndex = -1,
                    SubtitleBurnIn = false
                });

                LogToFile("state_trace.txt", $"Transcode response: status={transcodeResponse.Status} manifest={transcodeResponse.ManifestUrl} switchedFileId={transcodeResponse.SwitchedFileId} playerStart={transcodeResponse.PlayerStartSeconds} duration={transcodeResponse.DurationSeconds}");

                var baseUrl = _apiClient.BaseUrl;
                var manifestPath = transcodeResponse.ManifestUrl;
                if (!manifestPath.StartsWith("http") && !manifestPath.StartsWith("/api/v1"))
                    manifestPath = "/api/v1" + manifestPath;
                var remoteManifestUrl = manifestPath.StartsWith("http") ? manifestPath : $"{baseUrl}{manifestPath}";

                var token = _apiClient.AccessToken;

                // Start local HLS proxy to handle segment retries (CDN returns 404
                // for segments still being encoded; HLS.js retries, mpv doesn't)
                _hlsProxy?.Stop();
                _hlsProxy = new HlsProxy(remoteManifestUrl, token);
                var localUrl = _hlsProxy.Start();

                PlayMethod = "transcode";
                // The server starts encoding from seekSeconds. The manifest is
                // synthetic VOD but the proxy handles segment retries. The
                // playerStartSeconds tells us where playback should begin.
                _resumePosition = transcodeResponse.PlayerStartSeconds;

                LogToFile("state_trace.txt", $"Loading HLS via proxy: {localUrl}");
                _mpv.LoadFile(localUrl);
                _mpv.Play();
                SendMediaInfoToOsc();
            }
            catch (Exception ex)
            {
                _switchingContent = false;
                _qualitySwitchActive = false;
                LogToFile("player_quality_switch_error.txt", ex.ToString());
                _mpv.ShowOsdText("Transcode failed", 3000);
                App.MainWindowInstance?.HideLoadingOverlay();
            }
        }

        // Don't clear _switchingContent/_qualitySwitchActive here —
        // they're cleared in the FileLoaded handler when the new stream loads.
        // Clearing here races with END_FILE from the old stream being killed.
        _activeQualityTier = tierId;
        _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
    }

    private async Task SearchAndDownloadSubtitlesAsync()
    {
        if (_playbackManager?.CurrentSession == null) return;
        var fileId = _playbackManager.CurrentSession.MediaFileId;

        try
        {
            // Use the series/profile-effective subtitle language (set by the
            // server on each watch response) instead of hardcoded English.
            // Falls back to "en" when no preference is set at any level.
            var watchDetail = _playbackManager.WatchDetail;
            var preferred = watchDetail?.EffectiveSubtitleLanguage;
            if (string.IsNullOrWhiteSpace(preferred))
            {
                try
                {
                    var settingsVm = App.Services.GetService<ContinuumPlayer.ViewModels.SettingsViewModel>();
                    preferred = settingsVm?.SubtitleLanguage;
                }
                catch { }
            }
            var languages = !string.IsNullOrWhiteSpace(preferred)
                ? new[] { preferred! }
                : new[] { "en" };

            _mpv?.ShowOsdText($"Searching {languages[0].ToUpperInvariant()} subtitles…", 2000);
            var results = await _playbackApi.SearchSubtitlesAsync(fileId, languages);
            if (results.Results.Count == 0)
            {
                // Widen the search to English if the preferred language found nothing.
                if (languages[0] != "en")
                {
                    _mpv?.ShowOsdText($"No {languages[0].ToUpperInvariant()} subs — trying EN…", 2000);
                    results = await _playbackApi.SearchSubtitlesAsync(fileId, ["en"]);
                }
                if (results.Results.Count == 0)
                {
                    _mpv?.ShowOsdText("No subtitles found", 3000);
                    return;
                }
            }

            // Pick best by score, then prefer matching language, then prefer
            // non-hearing-impaired (matches what upstream's auto-pick would do
            // when no user interaction is possible over the mpv fullscreen).
            var best = results.Results
                .OrderByDescending(r => r.Score)
                .ThenByDescending(r => string.Equals(r.Language, languages[0], StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.HearingImpaired)
                .First();
            await _playbackApi.DownloadSubtitleAsync(fileId, best.Provider, best.SubtitleId, best.Language, best.Format);
            var label = string.IsNullOrEmpty(best.ReleaseName)
                ? $"{best.Language.ToUpperInvariant()} ({best.Provider})"
                : $"{best.Language.ToUpperInvariant()} · {best.ReleaseName}";
            _mpv?.ShowOsdText($"Downloaded: {label}", 3000);

            // Reload subtitles
            _ = Task.Run(LoadSubtitles);
            SendSubtitleListToOsc();
        }
        catch (Exception ex)
        {
            LogToFile("player_subtitle_error.txt", ex.ToString());
            _mpv?.ShowOsdText("Subtitle search failed", 3000);
        }
    }

    private void ToggleFullscreenFromOsc()
    {
        if (_videoWindow == null) return;
        LogToFile("state_trace.txt", $"ToggleFullscreenFromOsc: currently={_videoWindow.IsFullscreen}");

        if (_videoWindow.IsFullscreen)
        {
            _videoWindow.ExitFullscreen();
            SetState(PlayerState.Expanded);
            _mpv?.SendScriptMessage("osc-fullscreen-state", "false");
        }
        else
        {
            _videoWindow.EnterFullscreen();
            SetState(PlayerState.Fullscreen);
            _mpv?.SendScriptMessage("osc-fullscreen-state", "true");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void PositionVideoForMiniBar()
    {
        if (_videoWindow == null) return;
        var mw = App.MainWindowInstance;
        if (mw == null) { _videoWindow.Hide(); return; }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mw);
        GetWindowRect(hwnd, out var windowRect);

        // Get DPI scale factor (96 = 100%, 144 = 150%, 192 = 200%)
        double dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;

        // Mini-bar thumbnail: 200x112 logical pixels (16:9), bar height 132
        int thumbW = (int)(200 * scale);
        int thumbH = (int)(112 * scale);
        int thumbX = windowRect.Left + (int)(12 * scale);
        int thumbY = windowRect.Bottom - (int)(132 * scale) + (int)(10 * scale);

        _videoWindow.PositionAt(thumbX, thumbY, thumbW, thumbH);
    }

    public void HandleWindowResize()
    {
        if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
        else
            _videoWindow?.MatchParentPosition();
    }

    public void HandleWindowMinimized(bool minimized)
    {
        if (State == PlayerState.Idle) return;
        if (minimized)
            _videoWindow?.Hide();
        else if (State == PlayerState.Expanded || State == PlayerState.Fullscreen)
            _videoWindow?.Show();
        else if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
    }

    // ── Close / Dispose ──────────────────────────────────────────────────

    private bool _closing;

    public async Task CloseAsync()
    {
        if (_closing) return; // Prevent duplicate close from spammed exit clicks
        _closing = true;

        LogToFile("state_trace.txt", $"CloseAsync called: State={State} _switchingContent={_switchingContent}");
        StopPlaybackStallWatchdog();
        if (State == PlayerState.Fullscreen)
            ExitAnyFullscreen();

        // Report final position to server BEFORE stopping (so resume works)
        string? closedContentId = ContentId;
        double closedPosition = _mpv?.Position ?? 0;
        double closedDuration = _mpv?.Duration ?? 0;
        if (_mpv != null && _playbackManager?.SessionId != null)
        {
            var finalPos = _mpv.Position;
            if (finalPos > 0)
            {
                try
                {
                    await _playbackApi.ReportProgressAsync(_playbackManager.SessionId, finalPos, true);
                    LogToFile("state_trace.txt", $"Final progress reported: pos={finalPos:F1}");
                }
                catch (Exception ex) { LogToFile("state_trace.txt", $"Final progress error: {ex.Message}"); }
            }
        }

        // B15 + F4: publish PlaybackProgressUpdated so Home / History /
        // ItemDetail view models reflect the new position without waiting
        // for a full refetch. "Completed" = watched past 90% of duration,
        // matching the webui `isCompleted` heuristic.
        if (!string.IsNullOrEmpty(closedContentId) && closedPosition > 0)
        {
            bool completed = closedDuration > 0 && closedPosition >= closedDuration * 0.9;
            try
            {
                WeakReferenceMessenger.Default.Send(
                    new PlaybackProgressUpdated(
                        closedContentId, closedPosition, closedDuration, completed));
            }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Publish progress event error: {ex.Message}"); }
        }

        // Volume persistence: the XAML overlay's VolumeSlider is never the
        // live source of truth during fullscreen playback — mpv's own Lua OSC
        // handles the user's volume drag directly on the popup window and
        // nothing propagates that back to PlayerService.Volume. So before we
        // stop mpv, pull the CURRENT volume/mute from mpv and persist them.
        if (_mpv != null)
        {
            try
            {
                double liveVolume = _mpv.GetPropertyDouble("volume");
                bool liveMute = _mpv.GetMute();
                if (liveVolume > 0 || liveMute)
                {
                    Volume = Math.Clamp(liveVolume, 0, 100);
                    IsMuted = liveMute;
                    SaveVolumeState();
                    LogToFile("state_trace.txt", $"Persisted volume={Volume:F0} muted={IsMuted} on close");
                }
            }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Volume persist error: {ex.Message}"); }
        }

        // Clear the next-episode hint BEFORE stopping mpv. _mpv.Stop()
        // fires end-file → _mpvPlaybackEndedHandler which checks
        // NextEpisodeContentId. If it's still set, the handler shows the
        // Playing Next cinematic — wrong when the user manually clicked X
        // to exit. Clearing first ensures the handler takes the natural-end
        // path (CloseAsync dispatch) instead of the next-episode path.
        ClearNextEpisodeHint();

        _mpv?.Stop();

        if (_playbackManager != null)
        {
            try { await _playbackManager.StopSessionAsync(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Stop previous session error: {ex.Message}"); }
            _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
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
        _switchingContent = false;
        _qualitySwitchActive = false;
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        _prematureEofLastAttemptMs = 0;
        _prematureEofStreak = 0;
        StopDirectStreamProxy();
        _hlsProxy?.Stop();
        _hlsProxy = null;
        DisconnectWebSocket();
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;
        ErrorMessage = null;
        Versions = [];

        // Already cleared above (before _mpv.Stop()) but belt-and-suspenders
        // so a subsequent PlayAsync starts from a known-clean state.
        ClearNextEpisodeHint();

        App.MainWindowInstance?.HideLoadingOverlay();
        _videoWindow?.Hide();
        SetState(PlayerState.Idle);
        _closing = false;
        LogToFile("state_trace.txt", "CloseAsync completed");
    }

    public void Dispose()
    {
        StopPlaybackStallWatchdog();

        // Exit fullscreen BEFORE disposing the video window, so
        // ExitAnyFullscreen can still see which path is active.
        if (State == PlayerState.Fullscreen)
            ExitAnyFullscreen();

        // Persist live mpv volume on app shutdown — the user may have
        // changed it via the Lua OSC and never gone through CloseAsync.
        if (_mpv != null)
        {
            try
            {
                double liveVolume = _mpv.GetPropertyDouble("volume");
                bool liveMute = _mpv.GetMute();
                if (liveVolume > 0 || liveMute)
                {
                    Volume = Math.Clamp(liveVolume, 0, 100);
                    IsMuted = liveMute;
                    SaveVolumeState();
                }
            }
            catch { /* best-effort */ }
        }

        _videoWindow?.Dispose();
        _videoWindow = null;
        StopDirectStreamProxy();
        _hlsProxy?.Stop();
        _hlsProxy = null;
        if (State != PlayerState.Idle)
        {
            _playbackManager?.Dispose();
        }
        _mpv?.Dispose();
        _mpv = null;
    }

    // ── WebSocket session control ───────────────────────────────────────

    private void ConnectWebSocket()
    {
        _webSocket?.Disconnect();
        if (_playbackManager?.SessionId == null) return;

        var baseUrl = _apiClient.BaseUrl;
        var sessionId = _playbackManager.SessionId;
        var token = _apiClient.AccessToken;

        _webSocket = new PlaybackWebSocket(baseUrl, sessionId, token);
        _webSocket.CommandReceived += HandleWebSocketCommand;
        _ = Task.Run(async () =>
        {
            try { await _webSocket.ConnectAsync(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"WebSocket error: {ex.Message}"); }
        });
    }

    private void DisconnectWebSocket()
    {
        if (_webSocket != null)
        {
            _webSocket.CommandReceived -= HandleWebSocketCommand;
            _webSocket.Disconnect();
            _webSocket = null;
        }
    }

    private Task<CommandResult> HandleWebSocketCommand(WebSocketCommand cmd)
    {
        static Task<CommandResult> Complete(CommandResult result) => Task.FromResult(result);

        switch (cmd.Name)
        {
            case "pause":
                _mpv?.Pause();
                return Complete(new CommandResult());

            case "unpause":
                _mpv?.Play();
                return Complete(new CommandResult());

            case "play_pause":
                _mpv?.TogglePause();
                return Complete(new CommandResult());

            case "seek":
                var pos = cmd.GetNumber("position", "position_seconds", "seconds");
                if (pos == null) return Complete(new CommandResult { Status = "rejected", Error = "missing_seek_position" });
                _mpv?.Seek(pos.Value);
                return Complete(new CommandResult());

            case "set_volume":
                var vol = cmd.GetNumber("volume", "level");
                if (vol == null) return Complete(new CommandResult { Status = "rejected", Error = "missing_volume" });
                _mpv?.SetVolume(Math.Min(100, Math.Max(0, vol.Value * 100)));
                return Complete(new CommandResult());

            case "display_message":
                ShowNotice(
                    cmd.GetString("title") ?? "Playback notice",
                    cmd.GetString("message") ?? "A server message was received.",
                    "info");
                return Complete(new CommandResult());

            case "server_restarting":
                ShowNotice(
                    cmd.GetString("title") ?? "Server restarting",
                    cmd.GetString("message") ?? "Playback may end shortly while the server restarts.",
                    "warning");
                return Complete(new CommandResult());

            case "server_shutting_down":
                ShowNotice(
                    cmd.GetString("title") ?? "Server shutting down",
                    cmd.GetString("message") ?? "Playback may end shortly while the server shuts down.",
                    "warning");
                return Complete(new CommandResult());

            case "stop":
            case "terminate":
                var msg = cmd.GetString("message");
                if (msg != null)
                {
                    ShowNotice(
                        cmd.GetString("title") ?? (cmd.Name == "terminate" ? "Playback ended" : "Playback stopping"),
                        msg,
                        "warning");
                }
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => _ = CloseAsync());
                return Complete(new CommandResult());

            default:
                return Complete(new CommandResult { Status = "rejected", Error = "unsupported" });
        }
    }

    private void ShowNotice(string title, string message, string tone)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["title"] = title,
            ["message"] = message,
            ["tone"] = tone
        });
        _mpv?.SendScriptMessage("osc-show-notice", json);
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

    private static readonly System.Collections.Concurrent.ConcurrentQueue<(string File, string Line)> _logQueue = new();
    private static readonly Timer _logFlushTimer = new(_ => FlushLogs(), null, 100, 200);

    private static void LogToFile(string fileName, string content)
    {
        _logQueue.Enqueue((fileName, content));
    }

    private static void FlushLogs()
    {
        var batches = new Dictionary<string, List<string>>();
        while (_logQueue.TryDequeue(out var entry))
        {
            if (!batches.TryGetValue(entry.File, out var list))
            {
                list = new List<string>();
                batches[entry.File] = list;
            }
            list.Add(entry.Line);
        }
        foreach (var (fileName, lines) in batches)
        {
            LocalLog.AppendLines(fileName, lines);
        }
    }
}
