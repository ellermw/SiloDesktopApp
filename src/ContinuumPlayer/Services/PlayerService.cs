// src/ContinuumPlayer/Services/PlayerService.cs
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Helpers;
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
    private string _activeQualityTier = "original";
    private HlsProxy? _hlsProxy;
    private PlaybackWebSocket? _webSocket;
    private CancellationTokenSource? _playbackCts;

    // Stored mpv event handlers for proper unsubscription
    private Action<double>? _mpvPositionHandler;
    private Action<double>? _mpvDurationHandler;
    private Action<bool>? _mpvPauseHandler;
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
    {
        _mpv?.SetSubtitleTrack(mpvTrackIndex);

        var key = GetPrefsKey();
        if (string.IsNullOrEmpty(key)) return;

        try
        {
            string mode = mpvTrackIndex <= 0 ? "off" : "manual";
            await _playbackApi.SaveSubtitlePrefsAsync(key, language ?? "", mode);
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
            _playbackManager.Dispose();
            _playbackManager = null;
            await Task.Delay(200);
        }

        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;
        _resumePosition = 0;
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();

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

            var session = await _playbackManager.StartSessionAsync(bestVersion.FileId, startPosition, forceStartPosition: fromStart, audioTrackIndex: audioTrackIndex);
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
            var (transcodeUrl, transcodeStartPos) = await HandleTranscodeFallbackAsync(session, bestVersion, startPosition);
            if (transcodeUrl != null)
            {
                streamUrl = transcodeUrl;
                startPosition = transcodeStartPos!.Value;
            }

            EnsureMpvInitialized();

            SetState(PlayerState.Expanded);

            // Build auth
            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            if (session.PlayMethod != "transcode")
                streamUrl = UrlHelper.AppendToken(streamUrl, token);

            _resumePosition = startPosition;

            LogToFile("state_trace.txt", $"LoadFile: url={streamUrl?.Substring(0, Math.Min(80, streamUrl?.Length ?? 0))}...");

            // Phase 2b: apply pre-play subtitle selection by setting mpv's "sid"
            // property BEFORE loadfile so the initial state is the user's choice.
            // -1 = "no" (off), 0+ = 1-based mpv sid. null = don't touch, let mpv default.
            ApplyPendingSubtitleSelection();

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
            _hlsProxy?.Stop();
            _hlsProxy = new HlsProxy(remoteManifestUrl, _apiClient.AccessToken);
            var localUrl = _hlsProxy.Start();
            LogToFile("state_trace.txt", $"Initial transcode via HLS proxy: remote={remoteManifestUrl} local={localUrl} playerStart={transcodeResponse.PlayerStartSeconds}");

            return (localUrl, transcodeResponse.PlayerStartSeconds);
        }
        catch (Exception ex)
        {
            LogToFile("player_transcode_error.txt", ex.ToString());
            return (null, null);
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
        if (_mpvFileLoadedHandler != null) _mpv.FileLoaded -= _mpvFileLoadedHandler;
        if (_mpvPlaybackEndedHandler != null) _mpv.PlaybackEnded -= _mpvPlaybackEndedHandler;
        if (_mpvPlaybackErrorHandler != null) _mpv.PlaybackError -= _mpvPlaybackErrorHandler;
        if (_mpvErrorHandler != null) _mpv.Error -= _mpvErrorHandler;
        _mpv.ScriptMessageReceived -= OnScriptMessage;
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

        _mpvFileLoadedHandler = () =>
        {
            IsLoading = false;
            _switchingContent = false; // Safe to receive PlaybackEnded now
            _qualitySwitchActive = false;
            App.MainWindowInstance?.HideLoadingOverlay();
            LogToFile("state_trace.txt", "FileLoaded fired");

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
            LogToFile("state_trace.txt", $"PlaybackEnded fired: _switchingContent={_switchingContent} _qualitySwitchActive={_qualitySwitchActive} _closing={_closing} nextEpisode={NextEpisodeContentId ?? "none"} State={State} thread={Environment.CurrentManagedThreadId}");
            if (_switchingContent || _qualitySwitchActive)
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

            // Phase 3b: if the caller set a next-episode hint before playback,
            // show the Playing Next overlay instead of closing the player.
            // The overlay will call ContinuePlayingNextAsync or CancelPlayingNext.
            if (!string.IsNullOrEmpty(NextEpisodeContentId))
            {
                LogToFile("state_trace.txt", "  → Next-episode prompt requested");
                ShowPlayingNextRequested?.Invoke();
                return;
            }

            // CRITICAL: end-of-file means the session is DONE. We MUST tear
            // down the mpv/playback-manager state or the next PlayAsync call
            // will race against stale state and crash. Previously this code
            // just hid the window and invoked PlaybackEnded — but the only
            // listener (PlayerOverlay.OnPlaybackEnded) was gated behind an
            // _isActive flag that was never set, so nothing actually
            // cleaned up. Call CloseAsync ourselves, on the UI thread.
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
            LogToFile("state_trace.txt", $"PlaybackError: {msg} _switchingContent={_switchingContent}");
            // If we were waiting for a file to load and it failed, show error
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
                    catch (Exception ex) { LogToFile("player_transcode_error.txt", ex.ToString()); }
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

            var token = _apiClient.AccessToken;
            var authHeader = token != null ? $"Bearer {token}" : null;
            var finalUrl = result.streamUrl;
            if (result.session.PlayMethod != "transcode")
                finalUrl = UrlHelper.AppendToken(finalUrl, token);

            _resumePosition = result.currentPos;
            _mpv.LoadFile(finalUrl, result.session.PlayMethod == "transcode" ? null : authHeader);
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

            var baseUrl = _apiClient.BaseUrl;
            var token = _apiClient.AccessToken;
            var streamPath = response.StreamUrl;
            if (!streamPath.StartsWith("http") && !streamPath.StartsWith("/api/v1"))
                streamPath = "/api/v1" + streamPath;
            var url = streamPath.StartsWith("http") ? streamPath : $"{baseUrl}{streamPath}";
            url = UrlHelper.AppendToken(url, token);

            PlayMethod = response.PlayMethod;

            _resumePosition = currentPos;
            var authHeader = token != null ? $"Bearer {token}" : null;
            _mpv.LoadFile(url, authHeader);
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

    /// <summary>
    /// Downloads the HLS manifest, strips segments before the seek position,
    /// writes a trimmed manifest to a temp file, and returns the file path.
    /// </summary>
    private async Task<string?> TrimHlsManifestAsync(string manifestUrl, string? authHeader, double playerStart, double seekPos)
    {
        try
        {
            var http = new HttpClient(); // Short-lived for manifest fetch (can't reuse singleton — need custom auth header)
            if (_apiClient.AccessToken != null)
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiClient.AccessToken);

            var manifest = await http.GetStringAsync(manifestUrl);
            var lines = manifest.Split('\n');

            // Base URL for resolving relative segment paths
            var baseUrl = manifestUrl;
            var lastSlash = baseUrl.LastIndexOf('/');
            if (lastSlash > 0) baseUrl = baseUrl.Substring(0, lastSlash + 1);
            // Strip query from base URL (token is in manifest URL, not base)
            var qIdx = baseUrl.IndexOf('?');
            if (qIdx > 0) baseUrl = baseUrl.Substring(0, qIdx);

            // Skip segments before the seek position (2s each)
            int startSeg = Math.Max(0, (int)(seekPos / 2) - 2);

            var sb = new System.Text.StringBuilder();
            int segIndex = 0;
            bool skipNextSegUrl = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (line.Length == 0) { sb.AppendLine(); continue; }

                if (line.StartsWith("#EXTINF:"))
                {
                    if (segIndex < startSeg)
                    {
                        skipNextSegUrl = true;
                        segIndex++;
                        continue;
                    }
                    segIndex++;
                    sb.AppendLine(line);
                }
                else if (skipNextSegUrl && !line.StartsWith("#"))
                {
                    // This is the segment URL line after a skipped EXTINF
                    skipNextSegUrl = false;
                    continue;
                }
                else if (!line.StartsWith("#"))
                {
                    // Segment URL — make absolute
                    if (!line.StartsWith("http"))
                        sb.AppendLine(baseUrl + line);
                    else
                        sb.AppendLine(line);
                }
                else if (line.StartsWith("#EXT-X-MAP:"))
                {
                    // init segment — make URI absolute
                    var uriStart = line.IndexOf("URI=\"");
                    if (uriStart >= 0)
                    {
                        uriStart += 5;
                        var uriEnd = line.IndexOf('"', uriStart);
                        if (uriEnd > uriStart)
                        {
                            var uri = line.Substring(uriStart, uriEnd - uriStart);
                            if (!uri.StartsWith("http"))
                                uri = baseUrl + uri;
                            sb.AppendLine($"#EXT-X-MAP:URI=\"{uri}\"");
                            continue;
                        }
                    }
                    sb.AppendLine(line);
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            var tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, "transcode_manifest.m3u8");
            await File.WriteAllTextAsync(tempPath, sb.ToString());

            LogToFile("state_trace.txt", $"Trimmed manifest: skipped {startSeg} segments, wrote to {tempPath}");
            return tempPath;
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Manifest trim error: {ex.Message}");
            return null;
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
                    _mpv?.SetSubtitleTrack(subIdx <= 0 ? 0 : subIdx);
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
                    PlayMethod = session.PlayMethod;

                    var token = _apiClient.AccessToken;
                    var authHeader = token != null ? $"Bearer {token}" : null;
                    var streamUrl = UrlHelper.AppendToken(_playbackManager.StreamUrl ?? "", token);

                    _resumePosition = currentPos;
                    _mpv.LoadFile(streamUrl, authHeader);
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
            var results = await _playbackApi.SearchSubtitlesAsync(fileId, ["en"]);
            if (results.Results.Count == 0)
            {
                _mpv?.ShowOsdText("No subtitles found", 3000);
                return;
            }

            var best = results.Results[0];
            await _playbackApi.DownloadSubtitleAsync(fileId, best.Provider, best.SubtitleId, best.Language, best.Format);
            _mpv?.ShowOsdText($"Downloaded: {best.Language} subtitle", 3000);

            // Reload subtitles
            Task.Run(() => LoadSubtitles());
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

    private async Task<CommandResult> HandleWebSocketCommand(WebSocketCommand cmd)
    {
        switch (cmd.Name)
        {
            case "pause":
                _mpv?.Pause();
                return new CommandResult();

            case "unpause":
                _mpv?.Play();
                return new CommandResult();

            case "play_pause":
                _mpv?.TogglePause();
                return new CommandResult();

            case "seek":
                var pos = cmd.GetNumber("position", "position_seconds", "seconds");
                if (pos == null) return new CommandResult { Status = "rejected", Error = "missing_seek_position" };
                _mpv?.Seek(pos.Value);
                return new CommandResult();

            case "set_volume":
                var vol = cmd.GetNumber("volume", "level");
                if (vol == null) return new CommandResult { Status = "rejected", Error = "missing_volume" };
                _mpv?.SetVolume(Math.Min(100, Math.Max(0, vol.Value * 100)));
                return new CommandResult();

            case "display_message":
                ShowNotice(
                    cmd.GetString("title") ?? "Playback notice",
                    cmd.GetString("message") ?? "A server message was received.",
                    "info");
                return new CommandResult();

            case "server_restarting":
                ShowNotice(
                    cmd.GetString("title") ?? "Server restarting",
                    cmd.GetString("message") ?? "Playback may end shortly while the server restarts.",
                    "warning");
                return new CommandResult();

            case "server_shutting_down":
                ShowNotice(
                    cmd.GetString("title") ?? "Server shutting down",
                    cmd.GetString("message") ?? "Playback may end shortly while the server shuts down.",
                    "warning");
                return new CommandResult();

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
                return new CommandResult();

            default:
                return new CommandResult { Status = "rejected", Error = "unsupported" };
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
        _logQueue.Enqueue((fileName, $"[{DateTime.Now:HH:mm:ss.fff}] {content}\n"));
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
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ContinuumPlayer", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, string.Concat(lines));
            }
            catch { }
        }
    }
}
