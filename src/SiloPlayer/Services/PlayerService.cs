// src/SiloPlayer/Services/PlayerService.cs
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public enum PlayerState { Idle, Expanded, Fullscreen, Minimized, PictureInPicture }

public class PlayerService : IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly SiloApiClient _apiClient;
    private readonly SettingsService _settingsService;
    private readonly SettingsApi _settingsApi;
    private readonly SemaphoreSlim _subtitleAppearanceLoadGate = new(1, 1);
    private string? _subtitleAppearanceProfileId;

    private MpvPlayer? _mpv;
    private MpvVideoWindow? _videoWindow;
    private PlaybackManager? _playbackManager;
    private volatile bool _switchingContent;
    /// <summary>
    /// Pre-play subtitle selection captured from PlayAsync and applied to mpv
    /// before LoadFile. null = auto, -1 = off, 0+ = 0-based track index.
    /// </summary>
    private int? _pendingSubtitleSelection;
    private int? _pendingInitialServerSubtitleIndex;
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
    private Timer? _stallWatchdogTimer;
    private long _stallRecoveryLastAttemptMs;
    private string _activeQualityTier = "original";
    private HlsProxy? _hlsProxy;
    private DirectStreamProxy? _directStreamProxy;
    private PlaybackTransportPlan? _activeTransportPlan;
    private double _timelineOffsetSeconds;
    private double? _transportDurationSeconds;
    private bool _canSeekAnywhere = true;
    private TranscodeStartRequest? _activeHlsRecipe;
    private PlaybackTransportPlan? _preBitmapBurnInPlan;
    private string? _preBitmapBurnInQualityTier;
    private int? _requestedMediaFileId;
    private readonly SemaphoreSlim _transportRestartGate = new(1, 1);
    private readonly SemaphoreSlim _streamRecoveryGate = new(1, 1);
    private CancellationTokenSource? _seekRestartCts;
    private long _seekRestartGeneration;
    private readonly SemaphoreSlim _playRequestGate = new(1, 1);
    private CancellationTokenSource? _playRequestCts;
    private long _playRequestGeneration;
    private CancellationTokenSource? _fileLoadTimeoutCts;
    private long _fileLoadGeneration;
    private int _consecutiveFileLoadTimeouts;
    private bool? _restorePausedAfterLoad;
    private PlaybackWebSocket? _webSocket;
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _chapterThumbnailCts;
    private readonly ConcurrentDictionary<int, byte> _chapterThumbnailRequests = new();
    private readonly object _chapterThumbnailFileLock = new();
    private readonly HashSet<string> _chapterThumbnailRawFiles = [];
    private readonly object _liveSubtitleLock = new();
    private readonly List<LiveSubtitleCue> _liveSubtitleCues = [];
    private CancellationTokenSource? _liveSubtitleResumeCts;
    private long _liveSubtitleJobId;
    private int _liveSubtitleFileId;
    private string? _liveSubtitlePath;
    private int _liveSubtitleSid;
    private int _preLiveSubtitleSid;
    private bool _resumeAfterLiveSubtitleBuffer;

    // Stored mpv event handlers for proper unsubscription
    private Action<double>? _mpvPositionHandler;
    private Action<double>? _mpvDurationHandler;
    private Action<bool>? _mpvPauseHandler;
    private Action<bool>? _mpvBufferingHandler;
    private Action? _mpvFileLoadedHandler;
    private Action? _mpvEofReachedHandler;
    private Action? _mpvPlaybackEndedHandler;
    private Action<string>? _mpvPlaybackErrorHandler;
    private Action<string>? _mpvErrorHandler;

    public PlayerService(
        PlaybackApi playbackApi,
        CatalogApi catalogApi,
        AuthService authService,
        SiloApiClient apiClient,
        SettingsService settingsService,
        SettingsApi settingsApi)
    {
        _playbackApi = playbackApi;
        _catalogApi = catalogApi;
        _authService = authService;
        _apiClient = apiClient;
        _settingsService = settingsService;
        _settingsApi = settingsApi;

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
        SeekCore(positionSeconds, fast: false, forceResume: false);
    }

    /// <summary>
    /// Seek using a keyframe boundary when the active transport supports local
    /// seeking. Sequential remuxes and out-of-window copy HLS are restarted at
    /// the requested canonical media position.
    /// </summary>
    public void SeekFastTo(double positionSeconds, bool forceResume = false)
    {
        SeekCore(positionSeconds, fast: true, forceResume);
    }

    private void SeekCore(double positionSeconds, bool fast, bool forceResume)
    {
        var mpv = _mpv;
        var plan = _activeTransportPlan;
        if (mpv == null || plan == null || _switchingContent || _closing)
            return;

        var duration = CurrentMediaDuration;
        var mediaPosition = Math.Max(0, duration > 0
            ? Math.Min(positionSeconds, duration)
            : positionSeconds);
        var transportPosition = PlaybackTimeline.ToPlayerTime(mediaPosition, _timelineOffsetSeconds);
        var insideCopyHlsWindow = plan.IsHls &&
            !_canSeekAnywhere &&
            PlaybackTimeline.IsInsideExposedWindow(
                mediaPosition,
                _timelineOffsetSeconds,
                mpv.Duration);
        var canSeekLocally = plan.TransportKind == PlaybackTransportKind.DirectProgressive ||
            (plan.IsHls && (_canSeekAnywhere || insideCopyHlsWindow));

        if (canSeekLocally)
        {
            if (fast)
                mpv.SeekFast(transportPosition);
            else
                mpv.Seek(transportPosition);
            if (forceResume)
                mpv.Play();
            _playbackManager?.UpdatePosition(mediaPosition, forceResume ? false : IsPaused);
            _ = ReportSeekProgressAsync(mediaPosition, forceResume ? false : IsPaused);
            return;
        }

        QueueTransportRestartForSeek(mediaPosition, forceResume);
    }

    private void QueueTransportRestartForSeek(double mediaPosition, bool forceResume)
    {
        var playbackToken = _playbackCts?.Token ?? CancellationToken.None;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        var previous = Interlocked.Exchange(ref _seekRestartCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        var generation = Interlocked.Increment(ref _seekRestartGeneration);
        _ = RestartTransportForSeekAsync(mediaPosition, forceResume, generation, cts);
    }

    private async Task RestartTransportForSeekAsync(
        double mediaPosition,
        bool forceResume,
        long generation,
        CancellationTokenSource ownerCts)
    {
        var ct = ownerCts.Token;
        var gateEntered = false;
        try
        {
            await _transportRestartGate.WaitAsync(ct).ConfigureAwait(false);
            gateEntered = true;
            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _seekRestartGeneration))
                return;

            var manager = _playbackManager
                ?? throw new InvalidOperationException("The playback session is no longer active.");
            var session = manager.CurrentSession
                ?? throw new InvalidOperationException("The playback session is no longer active.");
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("The active media version is no longer available.");
            var plan = _activeTransportPlan
                ?? throw new InvalidOperationException("The active playback transport is unknown.");
            var mpv = _mpv
                ?? throw new InvalidOperationException("The player is no longer active.");
            var wasPaused = mpv.IsPaused;

            _switchingContent = true;
            IsLoading = true;
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(
                () => App.MainWindowInstance?.ShowLoadingOverlay());
            var seekPaused = forceResume ? false : wasPaused;
            manager.UpdatePosition(mediaPosition, seekPaused);
            await ReportSeekProgressAsync(mediaPosition, seekPaused).ConfigureAwait(false);

            PreparedPlaybackTransport prepared;
            if (plan.TransportKind == PlaybackTransportKind.RemuxProgressive)
            {
                var remuxStreamUrl = manager.StreamUrl;
                if (string.IsNullOrWhiteSpace(remuxStreamUrl))
                    remuxStreamUrl = NormalizePlaybackUrl(session.StreamUrl);
                var remoteUrl = SetSeekQueryParameter(remuxStreamUrl, mediaPosition);
                var localUrl = PrepareDirectStreamForMpv(remoteUrl, session.PlayMethod, supportsRanges: false);
                prepared = new PreparedPlaybackTransport(
                    plan,
                    localUrl,
                    MpvLoadStartSeconds: 0,
                    ResumeAfterLoadSeconds: 0,
                    TimelineOffsetSeconds: mediaPosition,
                    DurationSeconds: GetKnownDuration(session, version) ?? CurrentMediaDuration,
                    CanSeekAnywhere: false);
            }
            else if (plan.IsHls)
            {
                var recipe = _activeHlsRecipe
                    ?? throw new InvalidOperationException("The active HLS recipe is unavailable.");
                prepared = await PrepareHlsTransportAsync(
                    plan,
                    session,
                    CloneTranscodeRecipe(recipe, session.SessionId, mediaPosition),
                    CurrentMediaDuration,
                    ct).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException($"Transport {plan.TransportKind} cannot be restarted for seeking.");
            }

            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _seekRestartGeneration))
                return;

            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused: wasPaused && !forceResume);
            LogToFile("state_trace.txt", $"Seek restart: mediaPos={mediaPosition:F3} transport={prepared.Plan.TransportKind}");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer seek or playback teardown.
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _seekRestartGeneration))
            {
                _switchingContent = false;
                IsLoading = false;
                LogToFile("player_seek_error.txt", ex.ToString());
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
                {
                    App.MainWindowInstance?.HideLoadingOverlay();
                    _mpv?.ShowOsdText("Seek failed", 2500);
                });
            }
        }
        finally
        {
            if (gateEntered)
                _transportRestartGate.Release();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _seekRestartCts, null, ownerCts), ownerCts))
                ownerCts.Dispose();
        }
    }

    private async Task ReportSeekProgressAsync(double mediaPosition, bool paused)
    {
        var manager = _playbackManager;
        if (manager?.SessionId == null)
            return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await manager.ReportProgressNowAsync(mediaPosition, paused, cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Seek progress report failed: {ex.Message}");
        }
    }

    private void CancelPendingTransportRestarts()
    {
        Interlocked.Increment(ref _seekRestartGeneration);
        var cts = Interlocked.Exchange(ref _seekRestartCts, null);
        if (cts == null)
            return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private async Task CancelAndDrainTransportRestartsAsync(CancellationToken ct = default)
    {
        CancelPendingTransportRestarts();
        await _transportRestartGate.WaitAsync(ct).ConfigureAwait(false);
        _transportRestartGate.Release();
    }

    private void CancelPendingPlayRequest()
    {
        Interlocked.Increment(ref _playRequestGeneration);
        var cts = Interlocked.Exchange(ref _playRequestCts, null);
        if (cts == null)
            return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private async Task CancelAndDrainPlayRequestAsync()
    {
        CancelPendingPlayRequest();
        await _playRequestGate.WaitAsync().ConfigureAwait(false);
        _playRequestGate.Release();
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
                "xxlarge" => 96,
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
                _mpv.SetProperty("sub-border-color", $"#FF{NormalizeHex(a.TextOutlineColor).TrimStart('#')}");
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
                _mpv.SetProperty("sub-border-color", $"#FF{NormalizeHex(a.TextOutlineColor).TrimStart('#')}");
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
        if (track != null && IsUnsupportedBitmapSubtitle(track))
            await SetBitmapSubtitleBurnInAsync(track);
        else if (track != null)
        {
            // A text/PGS track cannot be layered over an HLS stream that is
            // already burning a different bitmap track into the video. Drop
            // the burn-in recipe first, wait for the replacement transport,
            // then select the requested native/sidecar track.
            if (_activeHlsRecipe?.SubtitleBurnIn == true)
                await SetBitmapSubtitleBurnInAsync(null);
            SelectSubtitleTrack(track);
        }
        else if (_activeHlsRecipe?.SubtitleBurnIn == true)
            await SetBitmapSubtitleBurnInAsync(null);
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
            var trackIndex = Math.Max(0, mpvTrackIndex - 1);
            var track = ActiveVersion?.AudioTracks?.ElementAtOrDefault(trackIndex);
            await _catalogApi.SetAudioPrefsAsync(key, new Core.Models.Catalog.AudioPreference
            {
                AudioTrackIndex = trackIndex,
                AudioLanguage = track?.Language ?? language,
                TrackSignature = CreateAudioTrackSignature(track),
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
    public FileVersion? ActiveVersion => Versions.FirstOrDefault(v => v.FileId == (_playbackManager?.CurrentSession?.MediaFileId ?? 0));
    public TimeRange? ActiveIntro => ActiveVersion?.Intro ?? WatchDetail?.Intro;
    public TimeRange? ActiveCredits => ActiveVersion?.Credits ?? WatchDetail?.Credits;
    public TimeRange? ActiveRecap => ActiveVersion?.Recap ?? WatchDetail?.Recap;
    public TimeRange? ActivePreview => ActiveVersion?.Preview ?? WatchDetail?.Preview;

    public void ApplyMarkerEdits(TimeRange? intro, TimeRange? recap, TimeRange? credits, TimeRange? preview)
    {
        if (ActiveVersion != null)
        {
            ActiveVersion.Intro = intro;
            ActiveVersion.Recap = recap;
            ActiveVersion.Credits = credits;
            ActiveVersion.Preview = preview;
        }
        if (WatchDetail != null)
        {
            WatchDetail.Intro = intro;
            WatchDetail.Recap = recap;
            WatchDetail.Credits = credits;
            WatchDetail.Preview = preview;
        }
        try { MarkersChanged?.Invoke(); } catch { }
    }

    // ── Events ───────────────────────────────────────────────────────────

    public event Action<PlayerState>? StateChanged;

    /// <summary>Fired after a new playback session is created on the server.
    /// Payload is the session UUID. Consumers like
    /// <see cref="WatchTogetherCoordinator"/> use this to attach the session
    /// to any active room so transport commands reach mpv.</summary>
    public event Action<string>? SessionStarted;

    /// <summary>Raised when the native OSC Watch Party panel requests one of
    /// the same host actions exposed by the WebUI panel.</summary>
    public event Action<string>? WatchTogetherActionRequested;

    public void SetWatchTogetherOverlay(
        WatchTogetherRoomSnapshot? room,
        string connectionState = "disconnected")
    {
        if (_mpv == null)
            return;

        if (room == null || string.Equals(room.Phase, "ended", StringComparison.OrdinalIgnoreCase))
        {
            _mpv.SendScriptMessage("osc-set-watch-party", "null");
            return;
        }

        var payload = new
        {
            visible = true,
            code = room.Code,
            member_count = room.MemberCount,
            connection_state = connectionState,
            playback_state = room.PlaybackState,
            guest_control_policy = room.GuestControlPolicy,
            is_host = room.SelfCanManageRoom,
            can_control_transport = room.SelfCanControlTransport,
        };
        _mpv.SendScriptMessage("osc-set-watch-party", JsonSerializer.Serialize(payload));
    }

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
    public event Action? MarkersChanged;
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
    public string? NextEpisodeAirDate { get; set; }
    public int NextEpisodeRuntime { get; set; }
    public string? PreviousEpisodeContentId { get; private set; }

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
            PublishFullscreenVisualState(newState == PlayerState.Fullscreen);
            PublishPictureInPictureVisualState(newState == PlayerState.PictureInPicture);
            return;
        }
        State = newState;

        // Fullscreen can be entered through the main Win32 window or through
        // the native mpv popup. Keep the Lua OSC driven by this authoritative
        // state transition instead of relying on individual click/keyboard
        // handlers to remember to update its icon. This covers Escape,
        // minimize, close, double-click, F, and the XAML fullscreen button.
        PublishFullscreenVisualState(newState == PlayerState.Fullscreen);
        PublishPictureInPictureVisualState(newState == PlayerState.PictureInPicture);

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
        else if (newState == PlayerState.PictureInPicture)
        {
            _mpv?.SendScriptMessage("osc-set-visibility", "true");
            _videoWindow?.EnterPictureInPicture();
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
        if (State is PlayerState.Minimized or PlayerState.PictureInPicture)
            SetState(PlayerState.Expanded);
    }

    public void EnterPictureInPicture()
    {
        if (State == PlayerState.Idle) return;
        if (State == PlayerState.Fullscreen) ExitAnyFullscreen();
        SetState(PlayerState.PictureInPicture);
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
        else if (State == PlayerState.PictureInPicture)
        {
            SetState(PlayerState.Expanded);
            EnterFullscreen();
        }
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
    public async Task PlayAsync(
        string contentId,
        bool fromStart = false,
        int? fileId = null,
        int? audioTrackIndex = null,
        int? subtitleSelection = null,
        double? startPositionOverride = null)
    {
        if (_closing)
            return;

        var ownerCts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _playRequestCts, ownerCts);
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        previous?.Dispose();
        var generation = Interlocked.Increment(ref _playRequestGeneration);
        var gateEntered = false;

        try
        {
            await _playRequestGate.WaitAsync(ownerCts.Token);
            gateEntered = true;
            ownerCts.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _playRequestGeneration))
                return;

            await PlayCoreAsync(
                contentId,
                fromStart,
                fileId,
                audioTrackIndex,
                subtitleSelection,
                startPositionOverride,
                ownerCts.Token);
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", $"Play request superseded: contentId={contentId}");
        }
        finally
        {
            if (gateEntered)
                _playRequestGate.Release();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _playRequestCts, null, ownerCts), ownerCts))
                ownerCts.Dispose();
        }
    }

    private void PublishFullscreenVisualState(bool isFullscreen)
        => _mpv?.SendScriptMessage("osc-fullscreen-state", isFullscreen ? "true" : "false");

    private void PublishPictureInPictureVisualState(bool isPictureInPicture)
        => _mpv?.SendScriptMessage("osc-pip-state", isPictureInPicture ? "true" : "false");

    private async Task PlayCoreAsync(
        string contentId,
        bool fromStart,
        int? fileId,
        int? audioTrackIndex,
        int? subtitleSelection,
        double? startPositionOverride,
        CancellationToken requestToken)
    {
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);
        // Fetch the effective device/profile style in parallel with playback
        // preparation so fresh launches render the first subtitle correctly
        // without adding another serial request to startup time.
        var subtitleAppearanceTask = EnsureSubtitleAppearanceLoadedAsync(requestToken);
        _pendingSubtitleSelection = subtitleSelection;
        _pendingInitialServerSubtitleIndex = null;
        LogToFile("state_trace.txt", $"PlayAsync called: contentId={contentId} fromStart={fromStart} audioTrackIndex={audioTrackIndex?.ToString() ?? "auto"} subtitleSelection={FormatSubtitleSelection(subtitleSelection)} State={State} IsLoading={IsLoading}");

        // CRITICAL: set the "switching content" flag BEFORE stopping the
        // previous mpv session. Without it, _mpv?.Stop() fires end-file →
        // _mpvPlaybackEndedHandler runs the natural-end cleanup path
        // (CloseAsync), which races against the new-session setup below
        // and crashes the player. The flag causes the handler to short-circuit.
        _switchingContent = true;
        CancelPendingFileLoadTimeout();
        Interlocked.Exchange(ref _consecutiveFileLoadTimeouts, 0);
        await CancelAndDrainTransportRestartsAsync(requestToken);

        // Begin retiring the previous session, but do not hold the next-item
        // startup path behind the server's database/history/scrobble cleanup.
        // The stop endpoint releases the live session before that slower work;
        // if an account has a one-stream limit, the guarded retry below waits
        // for retirement only when the server actually reports that limit.
        Task retiringSessionTask = Task.CompletedTask;
        if (_playbackManager != null)
        {
            var retiringManager = _playbackManager;
            var finalPosition = CurrentMediaPosition;
            _mpv?.Stop();
            retiringSessionTask = retiringManager.StopSessionAsync(
                finalPosition > 0 ? finalPosition : null,
                isPaused: true);
            retiringManager.ProgressReportingFailed -= OnProgressReportingFailed;
            _playbackManager = null;
            _ = FinishClosingSessionAsync(retiringManager, retiringSessionTask);
        }

        ErrorMessage = null;
        IsLoading = true;
        ContentId = contentId;
        _resumePosition = 0;
        _activeTransportPlan = null;
        _timelineOffsetSeconds = 0;
        _transportDurationSeconds = null;
        _canSeekAnywhere = true;
        _activeHlsRecipe = null;
        _preBitmapBurnInPlan = null;
        _preBitmapBurnInQualityTier = null;
        _requestedMediaFileId = null;
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        _prematureEofLastAttemptMs = 0;
        _prematureEofStreak = 0;
        ClearChapterThumbnailOverlayCache();
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);

        App.MainWindowInstance?.ShowLoadingOverlay();

        try
        {
            var passthrough = _settingsService.Load().AudioBitstreamPassthrough
                ? new AudioPassthroughCapabilities
                {
                    PassthroughCodecs = ["ac3", "eac3", "dts", "truehd"],
                    SpatializerEnabled = true,
                    MaxChannels = 8,
                }
                : null;
            _playbackManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient, passthrough);
            _playbackManager.ProgressReportingFailed += OnProgressReportingFailed;

            var watchDetail = await FetchWatchDetailAsync(contentId, requestToken);
            SetTitleFromWatchDetail(watchDetail);

            var bestVersion = SelectVersion(watchDetail, fileId);
            if (bestVersion == null)
            {
                throw new InvalidOperationException("No playable version found.");
            }

            Resolution = bestVersion.Resolution;
            _requestedMediaFileId = bestVersion.FileId;
            var startPosition = startPositionOverride.HasValue
                ? Math.Max(0, startPositionOverride.Value)
                : DetermineStartPosition(watchDetail, fromStart);
            var selectedAudioTrackIndex = audioTrackIndex ?? bestVersion.EffectiveAudioTrackIndex;

            PlaybackStartResponse session;
            try
            {
                session = await _playbackManager.StartSessionAsync(
                    bestVersion.FileId,
                    startPosition,
                    forceStartPosition: fromStart || startPositionOverride.HasValue,
                    audioTrackIndex: selectedAudioTrackIndex,
                    forceDirectAudioSelection: selectedAudioTrackIndex.HasValue,
                    ct: requestToken);
            }
            catch (ApiException ex) when (
                ex.StatusCode == 429 &&
                ex.ErrorCode == "too_many_streams" &&
                !retiringSessionTask.IsCompleted)
            {
                LogToFile("state_trace.txt", "Next playback session reached a one-stream limit; waiting for the retiring session and retrying once.");
                await retiringSessionTask.WaitAsync(requestToken);
                session = await _playbackManager.StartSessionAsync(
                    bestVersion.FileId,
                    startPosition,
                    forceStartPosition: fromStart || startPositionOverride.HasValue,
                    audioTrackIndex: selectedAudioTrackIndex,
                    forceDirectAudioSelection: selectedAudioTrackIndex.HasValue,
                    ct: requestToken);
            }
            ResolveInitialSubtitleSelection(
                watchDetail,
                bestVersion,
                session,
                selectedAudioTrackIndex,
                subtitleSelection);
            PlayMethod = session.PlayMethod;
            try { SessionStarted?.Invoke(session.SessionId); } catch { }

            if (Math.Abs(session.Position - startPosition) > 0.001)
            {
                LogToFile("state_trace.txt", $"Using authoritative server session position: local={startPosition:F3} server={session.Position:F3}");
            }
            startPosition = Math.Max(0, session.Position);

            var initialStreamUrl = _playbackManager.StreamUrl;
            if (string.IsNullOrEmpty(initialStreamUrl))
            {
                throw new InvalidOperationException("No stream URL available.");
            }
            var prepared = await PreparePlaybackTransportAsync(
                session,
                bestVersion,
                initialStreamUrl,
                startPosition,
                _playbackCts.Token);

            requestToken.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_playbackManager?.CurrentSession, session))
                return;

            EnsureMpvInitialized();
            if (subtitleAppearanceTask.IsCompletedSuccessfully)
            {
                if (_subtitleAppearance != null) PushSubtitleAppearanceToMpv(_subtitleAppearance);
            }
            else
            {
                _ = ApplySubtitleAppearanceWhenLoadedAsync(subtitleAppearanceTask);
            }

            SetState(PlayerState.Expanded);
            ApplyPreparedTransport(prepared);

            LogToFile(
                "state_trace.txt",
                $"LoadFile: transport={prepared.Plan.TransportKind} url={PlaybackUrlRedactor.Redact(prepared.LocalUrl)}");

            // Phase 2b: apply pre-play subtitle selection by setting mpv's "sid"
            // property BEFORE loadfile so the initial state is the user's choice.
            // -1 = "no" (off), 0+ = 1-based mpv sid. null = don't touch, let mpv default.
            ApplyPendingSubtitleSelection();

            requestToken.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_playbackManager?.CurrentSession, session))
                return;
            BeginMpvLoad(prepared, restorePaused: false);
            LogToFile("state_trace.txt", "Play() called");
            IsPaused = false;

            _mpv?.SendScriptMessage("osc-set-play-method", session.PlayMethod ?? "direct");
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            CancelPendingFileLoadTimeout();
            StopDirectStreamProxy();
            StopHlsProxy();
            if (_playbackManager != null)
            {
                try { await _playbackManager.StopSessionAsync(); }
                catch (Exception stopEx) { LogToFile("state_trace.txt", $"Canceled start cleanup error: {stopEx.Message}"); }
                _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
                _playbackManager.Dispose();
                _playbackManager = null;
            }
            _playbackCts?.Dispose();
            _playbackCts = null;
            throw;
        }
        catch (Exception ex)
        {
            CancelPendingFileLoadTimeout();
            LogToFile("player_crash.txt", ex.ToString());
            var (errTitle, errDetail) = DescribePlaybackError(ex);
            ErrorMessage = errDetail;
            IsLoading = false;
            _switchingContent = false;

            _playbackCts?.Cancel();
            StopDirectStreamProxy();
            StopHlsProxy();
            _videoWindow?.Hide();
            if (_playbackManager != null)
            {
                try { await _playbackManager.StopSessionAsync(); } catch (Exception stopEx) { LogToFile("state_trace.txt", $"StopSession error: {stopEx.Message}"); }
                _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
                _playbackManager.Dispose();
                _playbackManager = null;
            }
            _playbackCts?.Dispose();
            _playbackCts = null;
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
            if (api.StatusCode == 403 && api.ErrorCode == "transcoding_disabled")
                return ("Transcoding is disabled",
                    "Transcoding is disabled for your user. Ask your server administrator for access.");
            if (api.StatusCode == 403 && api.ErrorCode == "audio_transcoding_disabled")
                return ("Audio transcoding is disabled",
                    "This item requires audio conversion, but audio transcoding is disabled for your user.");
            if (api.StatusCode == 403)
                return ("Playback unavailable", "You do not have permission to play this item.");
            if (api.StatusCode == 429 && api.ErrorCode == "too_many_streams")
                return ("Stream limit reached", "This account has reached its active stream limit. Stop another stream and try again.");
            if (api.StatusCode == 429 && api.ErrorCode == "too_many_transcodes")
                return ("Transcode limit reached", "This account has reached its active transcode limit. Try direct play or stop another transcode.");
            if (api.StatusCode >= 500)
                return ("Playback unavailable", "Silo could not start playback right now. Please try again.");
            return ("Playback unavailable", string.IsNullOrWhiteSpace(api.Message) ? "Playback could not start." : api.Message);
        }
        if (ex.Message == "No compatible file version found")
            return ("No compatible version found", "Silo could not find a playable version for this device.");
        if (!string.IsNullOrWhiteSpace(ex.Message))
            return ("Playback unavailable", ex.Message);
        return ("Playback unavailable", "Playback could not start.");
    }

    /// <summary>
    /// Handles <see cref="PlaybackManager.ProgressReportingFailed"/>. A short
    /// CDN, Wi-Fi, or server interruption must not immediately throw the user
    /// out of a healthy local decode. Recreate the server session at the
    /// current media position; the shared recovery path only closes playback
    /// if that restart also fails.
    /// </summary>
    private void OnProgressReportingFailed(string message)
    {
        LogToFile("state_trace.txt", $"ProgressReportingFailed: {message}");
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        Action handle = () =>
        {
            if (_closing || State == PlayerState.Idle || _playbackManager?.CurrentSession == null)
                return;

            ErrorMessage = message;
            ShowNotice(
                "Reconnecting playback",
                "The server connection was interrupted. Resuming from your current position…",
                "warning");
            _ = RecoverInterruptedStreamAsync(CurrentMediaPosition, "progress-reporting-failed");
        };
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => handle());
        else
            handle();
    }

    private void StartPlaybackStallWatchdog()
    {
        StopPlaybackStallWatchdog();
        _stallDetector.Reset(CurrentMediaPosition, DateTimeOffset.UtcNow);
        _stallWatchdogTimer = new Timer(_ => CheckPlaybackStall(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void BeginMpvLoad(PreparedPlaybackTransport prepared, bool restorePaused)
    {
        var mpv = _mpv ?? throw new InvalidOperationException("The native player is unavailable.");
        var manager = _playbackManager ?? throw new InvalidOperationException("The playback session is unavailable.");
        var sessionId = manager.SessionId
            ?? throw new InvalidOperationException("The playback session ended before media loading began.");

        CancelPendingFileLoadTimeout();
        var playbackToken = _playbackCts?.Token ?? CancellationToken.None;
        var ownerCts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        var generation = Interlocked.Increment(ref _fileLoadGeneration);
        _fileLoadTimeoutCts = ownerCts;
        _restorePausedAfterLoad = restorePaused;
        _switchingContent = true;
        IsLoading = true;
        App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(
            () => App.MainWindowInstance?.ShowLoadingOverlay());

        try
        {
            ConfigureAudioOutput(mpv);
            mpv.LoadFile(prepared.LocalUrl, null, prepared.MpvLoadStartSeconds);
            if (restorePaused)
                mpv.Pause();
            else
                mpv.Play();
        }
        catch
        {
            CancelPendingFileLoadTimeout();
            throw;
        }

        _ = MonitorFileLoadAsync(
            generation,
            ownerCts,
            manager,
            sessionId,
            ContentId);
    }

    private void ConfigureAudioOutput(MpvPlayer mpv)
    {
        var bitstream = _settingsService.Load().AudioBitstreamPassthrough;
        mpv.SetProperty("audio-spdif", bitstream ? "ac3,eac3,dts-hd,truehd" : "");
        mpv.SetProperty("audio-exclusive", bitstream ? "yes" : "no");
        mpv.SetProperty("audio-channels", bitstream ? "auto" : "auto-safe");
    }

    private async Task MonitorFileLoadAsync(
        long generation,
        CancellationTokenSource ownerCts,
        PlaybackManager expectedManager,
        string expectedSessionId,
        string? expectedContentId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ownerCts.Token).ConfigureAwait(false);

            if (!ReferenceEquals(
                    Interlocked.CompareExchange(ref _fileLoadTimeoutCts, null, ownerCts),
                    ownerCts))
                return;

            if (generation != Volatile.Read(ref _fileLoadGeneration) ||
                _closing ||
                !_switchingContent ||
                !IsLoading ||
                !ReferenceEquals(_playbackManager, expectedManager) ||
                !string.Equals(expectedManager.SessionId, expectedSessionId, StringComparison.Ordinal) ||
                !string.Equals(ContentId, expectedContentId, StringComparison.Ordinal))
                return;

            var attempt = Interlocked.Increment(ref _consecutiveFileLoadTimeouts);
            var mediaPosition = CurrentMediaPosition;
            LogToFile(
                "state_trace.txt",
                $"File load timed out: attempt={attempt} " +
                $"mediaPos={mediaPosition:F1} transport={_activeTransportPlan?.TransportKind}");

            if (attempt == 1)
            {
                await RecoverInterruptedStreamAsync(mediaPosition, "file-load-timeout")
                    .ConfigureAwait(false);
                return;
            }

            const string detail = "The media stream did not become ready after two attempts.";
            ErrorMessage = detail;
            var dispatcher = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(async () =>
                {
                    await CloseAsync();
                    App.MainWindowInstance?.ShowPlaybackError("Playback timed out", detail);
                });
            }
            else
            {
                await CloseAsync();
            }
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested)
        {
            // The expected file loaded, playback changed, or teardown began.
        }
        catch (Exception ex)
        {
            LogToFile("player_recovery_error.txt", $"File-load watchdog failed: {ex}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _fileLoadTimeoutCts, null, ownerCts);
            ownerCts.Dispose();
        }
    }

    private void CancelPendingFileLoadTimeout()
    {
        Interlocked.Increment(ref _fileLoadGeneration);
        var cts = Interlocked.Exchange(ref _fileLoadTimeoutCts, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        cts?.Dispose();
        _restorePausedAfterLoad = null;
    }

    private void StopPlaybackStallWatchdog()
    {
        _stallWatchdogTimer?.Dispose();
        _stallWatchdogTimer = null;
        _stallDetector.Reset();
        _stallRecoveryLastAttemptMs = 0;
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
            var mediaPosition = CurrentMediaPosition;
            var mediaDuration = CurrentMediaDuration;

            var decision = _stallDetector.Observe(
                mediaPosition,
                mediaDuration,
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
                $"Playback stall detected: reason={decision.Reason} mediaPos={decision.Position:F1} " +
                $"mediaDur={mediaDuration:F1} paused={mpv.IsPaused} " +
                $"buffering={mpv.IsBufferingForCache}. Restarting stream...");
            _ = RecoverInterruptedStreamAsync(decision.Position, decision.Reason);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Playback stall watchdog error: {ex.Message}");
        }
    }

    private async Task RecoverInterruptedStreamAsync(double currentPosition, string reason)
    {
        if (!await _streamRecoveryGate.WaitAsync(0).ConfigureAwait(false))
        {
            LogToFile("state_trace.txt", $"Stream recovery skipped ({reason}): another recovery is active");
            return;
        }

        try
        {
            await RecoverInterruptedStreamCoreAsync(currentPosition, reason).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closing || _playbackCts?.IsCancellationRequested == true)
        {
            LogToFile("state_trace.txt", $"Stream recovery canceled before restart ({reason})");
        }
        catch (Exception ex)
        {
            _prematureEofRecoveryActive = false;
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_recovery_error.txt", $"Recovery setup failed ({reason}): {ex}");
            var message = $"Playback stalled and could not resume: {ex.Message}";
            ErrorMessage = message;
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
            {
                App.MainWindowInstance?.HideLoadingOverlay();
                App.MainWindowInstance?.ShowPlaybackError("Playback Stalled", message);
                _ = CloseAsync();
            });
        }
        finally
        {
            _streamRecoveryGate.Release();
        }
    }

    private async Task RecoverInterruptedStreamCoreAsync(double currentPosition, string reason)
    {
        await CancelAndDrainTransportRestartsAsync();
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        if (_mpv == null || manager == null || session == null)
        {
            LogToFile("state_trace.txt", $"Stream recovery skipped ({reason}): player/session unavailable");
            return;
        }

        var fileId = _requestedMediaFileId ?? session.MediaFileId;
        int? audioTrackIndex = session.AudioTrackIndex >= 0 ? session.AudioTrackIndex : null;
        var previousPlan = _activeTransportPlan;
        var previousRecipe = _activeHlsRecipe;
        var previousQualityTier = _activeQualityTier;
        var previousDuration = CurrentMediaDuration;
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

            var newSession = await manager.StartReplacementSessionAsync(
                fileId,
                resumePosition,
                forceStartPosition: true,
                audioTrackIndex: audioTrackIndex,
                forceDirectAudioSelection:
                    previousPlan?.TransportKind == PlaybackTransportKind.DirectProgressive,
                previousFinalPosition: resumePosition,
                ct: ct);
            try { SessionStarted?.Invoke(newSession.SessionId); } catch { }
            PlayMethod = newSession.PlayMethod;

            var streamUrl = manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
                throw new InvalidOperationException("No stream URL returned during premature EOF recovery.");

            var version = Versions.FirstOrDefault(v => v.FileId == fileId)
                ?? throw new InvalidOperationException("The active media version is no longer available.");
            PreparedPlaybackTransport prepared;
            if (previousRecipe != null && previousQualityTier is not ("auto" or "original"))
            {
                prepared = await PrepareHlsTransportAsync(
                    new PlaybackTransportPlan(
                        PlaybackSemanticMethod.Transcode,
                        PlaybackTransportKind.TranscodeHls,
                        RequiresTranscodeStartPreparation: true),
                    newSession,
                    CloneTranscodeRecipe(previousRecipe, newSession.SessionId, resumePosition),
                    previousDuration,
                    ct);
                PlayMethod = "transcode";
            }
            else
            {
                prepared = await PreparePlaybackTransportAsync(
                    newSession,
                    version,
                    streamUrl,
                    resumePosition,
                    ct);
            }

            if (_closing || State == PlayerState.Idle || !ReferenceEquals(_playbackManager, manager) || ct.IsCancellationRequested)
                return;

            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused: false);
            IsPaused = false;
            _mpv.SendScriptMessage("osc-set-play-method", PlayMethod ?? "direct");
            LogToFile("state_trace.txt", $"Stream recovery ({reason}) LoadFile issued at mediaPos={resumePosition:F1} transport={prepared.Plan.TransportKind}");
        }
        catch (OperationCanceledException) when (_closing || ct.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", $"Stream recovery ({reason}) canceled");
        }
        catch (Exception ex)
        {
            CancelPendingFileLoadTimeout();
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

    public Task PlayPreviousEpisodeAsync()
    {
        var previousId = PreviousEpisodeContentId;
        ClearNextEpisodeHint();
        return string.IsNullOrEmpty(previousId) ? Task.CompletedTask : PlayAsync(previousId);
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
        NextEpisodeAirDate = null;
        NextEpisodeRuntime = 0;
        PreviousEpisodeContentId = null;
        // Also tell the OSC to drop episode navigation and its credits action.
        _mpv?.SendScriptMessage("osc-set-episode-navigation", "false", "false", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode-detail", "null");
    }

    /// <summary>
    /// Auto-compute the next-episode hint for a series episode that's just
    /// started playing. Runs in the background so it doesn't block
    /// FileLoaded. Mirrors the WebUI player hook by fetching the current
    /// season and, when present, the next season so cross-season autoplay
    /// works. Set on <see cref="NextEpisodeContentId"/> so the end-of-file
    /// handler can fire the Playing Next cinematic.
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

            var seasonNumbers = new SortedSet<int> { wd.SeasonNumber.Value };
            try
            {
                var seasons = await _catalogApi.GetSeasonsAsync(wd.SeriesId, ct);
                if (ct.IsCancellationRequested) return;
                if (seasons.Seasons.Any(s => s.SeasonNumber == wd.SeasonNumber.Value + 1))
                    seasonNumbers.Add(wd.SeasonNumber.Value + 1);
                if (seasons.Seasons.Any(s => s.SeasonNumber == wd.SeasonNumber.Value - 1))
                    seasonNumbers.Add(wd.SeasonNumber.Value - 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogToFile("state_trace.txt", $"AutoDetectNextEpisode: seasons lookup failed: {ex.Message}");
            }

            var allEpisodes = new List<Episode>();
            foreach (var seasonNumber in seasonNumbers)
            {
                var episodes = await _catalogApi.GetEpisodesAsync(wd.SeriesId, seasonNumber, ct);
                if (ct.IsCancellationRequested) return;
                if (episodes.Episodes != null)
                    allEpisodes.AddRange(episodes.Episodes);
            }

            if (ct.IsCancellationRequested) return;
            if (allEpisodes.Count == 0) return;

            var orderedEpisodes = allEpisodes.OrderBy(ep => ep.SeasonNumber).ThenBy(ep => ep.EpisodeNumber).ToList();
            var currentIndex = orderedEpisodes.FindIndex(ep => ep.ContentId == wd.ContentId ||
                (ep.SeasonNumber == wd.SeasonNumber.Value && ep.EpisodeNumber == wd.EpisodeNumber.Value));
            PreviousEpisodeContentId = currentIndex > 0 ? orderedEpisodes[currentIndex - 1].ContentId : null;

            int currentNumber = wd.EpisodeNumber.Value;
            var next = NextEpisodeResolver.FindNextEpisode(
                orderedEpisodes,
                wd.SeasonNumber.Value,
                currentNumber,
                wd.ContentId);
            _mpv?.SendScriptMessage(
                "osc-set-episode-navigation",
                "true",
                PreviousEpisodeContentId != null ? "true" : "false",
                next != null ? "true" : "false");
            if (next == null)
            {
                LogToFile("state_trace.txt", $"AutoDetectNextEpisode: no next episode after S{wd.SeasonNumber} E{currentNumber}");
                return;
            }

            NextEpisodeContentId = next.ContentId;
            var label = $"S{next.SeasonNumber}:E{next.EpisodeNumber}";
            NextEpisodeTitle = string.IsNullOrEmpty(next.Title) ? label : $"{label} \u2014 {next.Title}";
            NextEpisodeSeriesTitle = wd.SeriesTitle;
            NextEpisodePosterUrl = next.StillUrl;
            NextEpisodeOverview = next.Overview;
            NextEpisodeAirDate = next.AirDate;
            NextEpisodeRuntime = next.Runtime;
            LogToFile("state_trace.txt", $"AutoDetectNextEpisode: next={next.ContentId} ({NextEpisodeTitle})");

            _mpv?.SendScriptMessage("osc-set-next-episode-detail", JsonSerializer.Serialize(new
            {
                season_number = next.SeasonNumber,
                episode_number = next.EpisodeNumber,
                title = next.Title ?? string.Empty,
            }));
            _mpv?.SendScriptMessage("osc-set-next-episode", "true");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogToFile("state_trace.txt", $"AutoDetectNextEpisode error: {ex.Message}"); }
    }

    private void ResolveInitialSubtitleSelection(
        WatchDetailResponse watchDetail,
        FileVersion version,
        PlaybackStartResponse session,
        int? selectedAudioTrackIndex,
        int? explicitSelection)
    {
        SubtitleTrackInfo? selectedTrack = null;

        if (explicitSelection is >= 0)
        {
            var requested = version.SubtitleTracks?.ElementAtOrDefault(explicitSelection.Value);
            if (requested != null)
            {
                selectedTrack = session.SubtitleUrls.FirstOrDefault(track =>
                    string.Equals(track.Language, requested.Language, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(track.Codec, requested.Codec, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(requested.Title) ||
                        string.Equals(track.Label, requested.Title, StringComparison.OrdinalIgnoreCase)));
            }
        }
        else if (explicitSelection == null)
        {
            var mode = watchDetail.EffectiveSubtitleMode;
            var preferredLanguage = watchDetail.EffectiveSubtitleLanguage;
            var showForced = watchDetail.EffectiveShowForcedSubtitles;
            if (string.IsNullOrWhiteSpace(mode) || preferredLanguage == null || showForced == null)
            {
                try
                {
                    var settings = App.Services.GetService<ViewModels.SettingsViewModel>();
                    if (string.IsNullOrWhiteSpace(mode)) mode = settings?.SubtitleMode;
                    if (preferredLanguage == null) preferredLanguage = settings?.SubtitleLanguage;
                    if (showForced == null) showForced = settings?.ShowForcedSubtitles;
                }
                catch { }
            }

            string? audioLanguage = null;
            var audioTracks = version.AudioTracks ?? [];
            var audioIndex = selectedAudioTrackIndex ?? version.EffectiveAudioTrackIndex ??
                audioTracks.FindIndex(track => track.Default);
            if (audioIndex < 0) audioIndex = 0;
            if (audioIndex < audioTracks.Count)
                audioLanguage = audioTracks[audioIndex].Language;
            if (string.IsNullOrWhiteSpace(audioLanguage) &&
                version.EffectiveAudioTrackIndex == audioIndex)
                audioLanguage = version.EffectiveAudioLanguage;

            var selectedIndex = SubtitleAutoSelect.Resolve(new SubtitleAutoSelect.Options(
                Mode: SubtitleAutoSelect.NormalizeSubtitleMode(mode),
                Tracks: SubtitleAutoSelect.BuildCandidates(session.SubtitleUrls),
                PreferredLanguage: preferredLanguage,
                AudioLanguage: audioLanguage,
                ProfileLanguage: null,
                ShowForcedSubtitles: showForced ?? true,
                PreferredTrackSignature: watchDetail.EffectiveSubtitleTrackSignature));
            if (selectedIndex is int index)
                selectedTrack = session.SubtitleUrls.FirstOrDefault(track => track.Index == index);
        }

        if (selectedTrack == null && explicitSelection is >= 0)
        {
            _pendingSubtitleSelection = explicitSelection;
            _pendingInitialServerSubtitleIndex = null;
            return;
        }

        if (explicitSelection == -1 || selectedTrack == null)
        {
            _pendingSubtitleSelection = -1;
            _pendingInitialServerSubtitleIndex = -1;
            return;
        }

        _pendingInitialServerSubtitleIndex = selectedTrack.Index;
        if (string.Equals(selectedTrack.Source, "embedded", StringComparison.OrdinalIgnoreCase))
        {
            var embedded = session.SubtitleUrls
                .Where(track => string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase))
                .OrderBy(track => track.Index)
                .ToList();
            var ordinal = embedded.FindIndex(track => track.Index == selectedTrack.Index);
            _pendingSubtitleSelection = ordinal >= 0 ? ordinal : null;
        }
        else
        {
            // Sidecars are added by URL once FileLoaded fires.
            _pendingSubtitleSelection = null;
        }
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

    private async Task<WatchDetailResponse> FetchWatchDetailAsync(
        string contentId,
        CancellationToken ct)
    {
        try
        {
            return await _playbackManager!.GetWatchDetailAsync(contentId, ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 400)
        {
            // Retry once if server hasn't processed previous session stop
            await Task.Delay(300, ct);
            return await _playbackManager!.GetWatchDetailAsync(contentId, ct);
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
        bestVersion ??= _playbackManager!.SelectBestVariantVersion(
            versions,
            watchDetail.PlaybackVariants,
            userData: watchDetail.UserData,
            preferredEditionKey: watchDetail.EffectiveVersionEditionKey);
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

    private sealed record PreparedPlaybackTransport(
        PlaybackTransportPlan Plan,
        string LocalUrl,
        double MpvLoadStartSeconds,
        double ResumeAfterLoadSeconds,
        double TimelineOffsetSeconds,
        double? DurationSeconds,
        bool CanSeekAnywhere,
        TranscodeStartRequest? HlsRecipe = null);

    /// <summary>
    /// Converts the semantic playback decision returned by /playback/start into
    /// the concrete transport mpv must open. A remux can be either a sequential
    /// progressive stream or HLS, so play_method alone is never treated as the
    /// transport decision.
    /// </summary>
    private async Task<PreparedPlaybackTransport> PreparePlaybackTransportAsync(
        PlaybackStartResponse session,
        FileVersion version,
        string remoteStreamUrl,
        double mediaStartSeconds,
        CancellationToken ct = default)
    {
        var plan = PlaybackTransportPlanner.Plan(session);
        var knownDuration = GetKnownDuration(session, version);
        mediaStartSeconds = Math.Max(0, mediaStartSeconds);

        if (!plan.RequiresTranscodeStartPreparation)
        {
            var supportsRanges = plan.TransportKind == PlaybackTransportKind.DirectProgressive;
            var effectiveRemoteUrl = plan.TransportKind == PlaybackTransportKind.RemuxProgressive
                ? SetSeekQueryParameter(remoteStreamUrl, mediaStartSeconds)
                : remoteStreamUrl;
            var localUrl = PrepareDirectStreamForMpv(effectiveRemoteUrl, session.PlayMethod, supportsRanges);

            if (plan.TransportKind == PlaybackTransportKind.RemuxProgressive)
            {
                // The server's local ffmpeg remux is a forward-only pipe. It has
                // already applied ?seek=mediaStartSeconds, so mpv starts at zero.
                return new PreparedPlaybackTransport(
                    plan,
                    localUrl,
                    MpvLoadStartSeconds: 0,
                    ResumeAfterLoadSeconds: 0,
                    TimelineOffsetSeconds: mediaStartSeconds,
                    DurationSeconds: knownDuration,
                    CanSeekAnywhere: false);
            }

            return new PreparedPlaybackTransport(
                plan,
                localUrl,
                MpvLoadStartSeconds: mediaStartSeconds,
                ResumeAfterLoadSeconds: 0,
                TimelineOffsetSeconds: 0,
                DurationSeconds: knownDuration,
                CanSeekAnywhere: true);
        }

        try
        {
            var copyVideo = plan.TransportKind == PlaybackTransportKind.RemuxHls;
            var copyAudio = copyVideo && session.PlaybackInfo?.TranscodeAudio != true;
            var recipe = new TranscodeStartRequest
            {
                SessionId = session.SessionId,
                SeekSeconds = mediaStartSeconds,
                TargetResolution = copyVideo ? "" : version.Resolution,
                TargetCodecVideo = copyVideo ? "copy" : "h264",
                TargetCodecAudio = copyAudio ? "copy" : "aac",
                TargetBitrateKbps = copyVideo ? 0 : 8000,
                SegmentDuration = 2,
                SubtitleTrackIndex = -1,
                SubtitleBurnIn = false
            };
            return await PrepareHlsTransportAsync(plan, session, recipe, knownDuration, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogToFile("player_transcode_error.txt", ex.ToString());
            throw new InvalidOperationException("Failed to prepare HLS playback.", ex);
        }
    }

    private async Task<PreparedPlaybackTransport> PrepareHlsTransportAsync(
        PlaybackTransportPlan plan,
        PlaybackStartResponse session,
        TranscodeStartRequest recipe,
        double? fallbackDuration,
        CancellationToken ct)
    {
        var transcodeResponse = await _playbackApi.StartTranscodeAsync(recipe, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (transcodeResponse.SwitchedFileId is int switchedFileId)
        {
            session.MediaFileId = switchedFileId;
            LogToFile("state_trace.txt", $"Server switched transcode source to fileId={switchedFileId}");
        }

        var remoteManifestUrl = NormalizePlaybackUrl(transcodeResponse.ManifestUrl);
        var localUrl = PrepareHlsStreamForMpv(remoteManifestUrl);
        var duration = transcodeResponse.DurationSeconds is > 0
            ? transcodeResponse.DurationSeconds
            : fallbackDuration;

        LogToFile(
            "state_trace.txt",
            $"Prepared {plan.TransportKind}: remote={PlaybackUrlRedactor.Redact(remoteManifestUrl)} " +
            $"local={PlaybackUrlRedactor.Redact(localUrl)} " +
            $"playerStart={transcodeResponse.PlayerStartSeconds:F3} origin={transcodeResponse.StreamOriginSeconds:F3} " +
            $"offset={transcodeResponse.TimelineOffsetSeconds:F3} canSeekAnywhere={transcodeResponse.CanSeekAnywhere}");

        return new PreparedPlaybackTransport(
            plan,
            localUrl,
            MpvLoadStartSeconds: transcodeResponse.CanSeekAnywhere
                ? Math.Max(0, transcodeResponse.PlayerStartSeconds)
                : 0,
            ResumeAfterLoadSeconds: 0,
            TimelineOffsetSeconds: Math.Max(0, transcodeResponse.TimelineOffsetSeconds),
            DurationSeconds: duration,
            CanSeekAnywhere: transcodeResponse.CanSeekAnywhere,
            HlsRecipe: recipe);
    }

    private static double? GetKnownDuration(PlaybackStartResponse session, FileVersion version)
    {
        if (session.DurationSeconds is > 0)
            return session.DurationSeconds;
        if (version.Duration > 0)
            return version.Duration;
        return null;
    }

    private static TranscodeStartRequest CloneTranscodeRecipe(
        TranscodeStartRequest source,
        string sessionId,
        double seekSeconds)
    {
        return new TranscodeStartRequest
        {
            SessionId = sessionId,
            SeekSeconds = seekSeconds,
            TargetResolution = source.TargetResolution,
            TargetCodecVideo = source.TargetCodecVideo,
            TargetCodecAudio = source.TargetCodecAudio,
            TargetBitrateKbps = source.TargetBitrateKbps,
            SegmentDuration = source.SegmentDuration,
            SubtitleTrackIndex = source.SubtitleTrackIndex,
            SubtitleMediaFileId = source.SubtitleMediaFileId,
            SubtitleBurnIn = source.SubtitleBurnIn
        };
    }

    private static string SetSeekQueryParameter(string url, double seekSeconds)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            throw new InvalidOperationException("The remux stream URL is invalid.");

        var builder = new UriBuilder(absolute);
        var queryParts = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var separator = part.IndexOf('=');
                var key = separator >= 0 ? part[..separator] : part;
                return !Uri.UnescapeDataString(key).Equals("seek", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        queryParts.Add("seek=" + seekSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        builder.Query = string.Join("&", queryParts);
        return builder.Uri.AbsoluteUri;
    }

    private string NormalizePlaybackUrl(string pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            throw new InvalidOperationException("The server returned an empty playback URL.");

        var value = pathOrUrl.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.ToString();

        if (!value.StartsWith('/'))
            value = "/" + value;
        if (!value.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
            value = "/api/v1" + value;
        return $"{_apiClient.BaseUrl.TrimEnd('/')}{value}";
    }

    private void ApplyPreparedTransport(PreparedPlaybackTransport prepared)
    {
        _activeTransportPlan = prepared.Plan;
        _timelineOffsetSeconds = prepared.TimelineOffsetSeconds;
        _transportDurationSeconds = prepared.DurationSeconds;
        _canSeekAnywhere = prepared.CanSeekAnywhere;
        _activeHlsRecipe = prepared.HlsRecipe;
        _resumePosition = prepared.ResumeAfterLoadSeconds;

        var effectiveVersion = ActiveVersion;
        if (effectiveVersion != null)
            Resolution = effectiveVersion.Resolution;

        _mpv?.SendScriptMessage(
            "osc-set-timeline",
            _timelineOffsetSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            (_transportDurationSeconds ?? 0).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _canSeekAnywhere ? "true" : "false");
    }

    private double CurrentMediaPosition
        => _mpv != null
            ? PlaybackTimeline.ToMediaTime(_mpv.Position, _timelineOffsetSeconds)
            : Position;

    private double CurrentMediaDuration
    {
        get
        {
            if (_mpv == null)
                return Duration;
            var rawDuration = _mpv.Duration;
            return PlaybackTimeline.ResolveMediaDuration(
                rawDuration,
                _timelineOffsetSeconds,
                _transportDurationSeconds);
        }
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
            var localUrl = PrepareHlsStreamForMpv(remoteManifestUrl);
            LogToFile(
                "state_trace.txt",
                $"Initial transcode via HLS proxy: remote={PlaybackUrlRedactor.Redact(remoteManifestUrl)} " +
                $"local={PlaybackUrlRedactor.Redact(localUrl)} playerStart={transcodeResponse.PlayerStartSeconds}");

            return (localUrl, transcodeResponse.PlayerStartSeconds);
        }
        catch (Exception ex)
        {
            LogToFile("player_transcode_error.txt", ex.ToString());
            throw new InvalidOperationException("Failed to start transcode playback.", ex);
        }
    }

    private string PrepareDirectStreamForMpv(string remoteStreamUrl, string? playMethod, bool supportsRanges)
    {
        if (string.IsNullOrWhiteSpace(remoteStreamUrl))
            throw new InvalidOperationException("No direct stream URL available.");

        StopDirectStreamProxy();
        StopHlsProxy();

        _directStreamProxy = new DirectStreamProxy(remoteStreamUrl, () => _apiClient.AccessToken, supportsRanges);
        var localUrl = _directStreamProxy.Start();
        LogToFile(
            "state_trace.txt",
            $"Direct/remux stream via local proxy: method={playMethod ?? "direct"} " +
            $"ranges={supportsRanges} local={PlaybackUrlRedactor.Redact(localUrl)}");
        return localUrl;
    }

    private string PrepareHlsStreamForMpv(string remoteManifestUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteManifestUrl))
            throw new InvalidOperationException("No HLS manifest URL available.");

        StopDirectStreamProxy();
        StopHlsProxy();
        _hlsProxy = new HlsProxy(remoteManifestUrl, () => _apiClient.AccessToken);
        var localUrl = _hlsProxy.Start();
        LogToFile("state_trace.txt", $"HLS stream via local proxy: local={PlaybackUrlRedactor.Redact(localUrl)}");
        return localUrl;
    }

    private void StopHlsProxy()
    {
        try
        {
            _hlsProxy?.Dispose();
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"HLS proxy stop error: {ex.Message}");
        }
        finally
        {
            _hlsProxy = null;
        }
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
        if (_mpvEofReachedHandler != null) _mpv.EofReached -= _mpvEofReachedHandler;
        if (_mpvPlaybackEndedHandler != null) _mpv.PlaybackEnded -= _mpvPlaybackEndedHandler;
        if (_mpvPlaybackErrorHandler != null) _mpv.PlaybackError -= _mpvPlaybackErrorHandler;
        if (_mpvErrorHandler != null) _mpv.Error -= _mpvErrorHandler;
        _mpv.ScriptMessageReceived -= OnScriptMessage;
    }

    private async Task EnsureSubtitleAppearanceLoadedAsync(CancellationToken ct)
    {
        var profileId = _authService.SelectedProfileId;
        if (_subtitleAppearance != null && string.Equals(_subtitleAppearanceProfileId, profileId, StringComparison.Ordinal))
            return;

        await _subtitleAppearanceLoadGate.WaitAsync(ct);
        try
        {
            if (_subtitleAppearance != null && string.Equals(_subtitleAppearanceProfileId, profileId, StringComparison.Ordinal))
                return;

            try
            {
                var response = await _settingsApi.GetEffectiveSettingsAsync(["subtitle_appearance"], ct);
                _subtitleAppearance = Core.Models.Settings.SubtitleAppearance.Parse(
                    response.Settings.FirstOrDefault(setting => setting.Key == "subtitle_appearance")?.EffectiveValue);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogToFile("state_trace.txt", $"Subtitle appearance load failed: {ex.Message}");
                _subtitleAppearance = new Core.Models.Settings.SubtitleAppearance();
            }
            _subtitleAppearanceProfileId = profileId;
        }
        finally
        {
            _subtitleAppearanceLoadGate.Release();
        }
    }

    private async Task ApplySubtitleAppearanceWhenLoadedAsync(Task loadTask)
    {
        try
        {
            await loadTask;
            if (_subtitleAppearance != null && _mpv != null)
                PushSubtitleAppearanceToMpv(_subtitleAppearance);
        }
        catch (OperationCanceledException) { }
    }

    private void HandleMpvEndSignal(string trigger)
    {
        LogToFile("state_trace.txt", $"{trigger} fired: _switchingContent={_switchingContent} _qualitySwitchActive={_qualitySwitchActive} _prematureEofRecoveryActive={_prematureEofRecoveryActive} _closing={_closing} nextEpisode={NextEpisodeContentId ?? "none"} State={State} thread={Environment.CurrentManagedThreadId}");
        if (_switchingContent || _qualitySwitchActive || _prematureEofRecoveryActive)
        {
            LogToFile("state_trace.txt", $"  -> Suppressed {trigger} (switching content)");
            return;
        }

        if (_closing || State == PlayerState.Idle)
        {
            LogToFile("state_trace.txt", $"  -> Suppressed {trigger} (closing or idle)");
            return;
        }

        var pos = CurrentMediaPosition;
        var dur = CurrentMediaDuration;
        // A direct stream can be interrupted during its opening seconds just
        // as easily as later in the movie. The old ten-second floor treated
        // those early EOFs as natural completion and closed the player. The
        // same bounded recovery/streak guard is safe from position zero.
        if (dur > 0 && pos >= 0 && !IsAtMediaEnd(pos, dur))
        {
            var nowMs = Environment.TickCount64;
            var sinceLastAttempt = nowMs - _prematureEofLastAttemptMs;

            if (_prematureEofLastAttemptMs > 0 && sinceLastAttempt < 1500)
            {
                LogToFile("state_trace.txt", $"  -> Premature {trigger} at pos={pos:F1} - retry in progress ({sinceLastAttempt}ms since last), ignoring");
                return;
            }

            _prematureEofStreak = (_prematureEofLastAttemptMs > 0 && sinceLastAttempt < 5000)
                ? _prematureEofStreak + 1
                : 1;

            if (_prematureEofStreak >= 3)
            {
                LogToFile("state_trace.txt", $"  -> Premature {trigger} stuck at pos={pos:F1} (streak={_prematureEofStreak}). Giving up, closing player.");
                _prematureEofStreak = 0;
                _prematureEofLastAttemptMs = 0;
                ErrorMessage = "Playback stalled and couldn't resume. The stream may be corrupted at this position.";
                PlaybackEnded?.Invoke();
                var recoveryDispatcher = App.MainWindowInstance?.DispatcherQueue;
                if (recoveryDispatcher != null)
                    recoveryDispatcher.TryEnqueue(() => _ = CloseAsync());
                else
                    _ = CloseAsync();
                return;
            }
            else
            {
                _prematureEofLastAttemptMs = nowMs;
                LogToFile("state_trace.txt", $"  -> Premature {trigger} detected (pos={pos:F1} dur={dur:F1}, {(1 - pos / dur) * 100:F0}% remaining, streak={_prematureEofStreak}). Restarting stream...");
                try
                {
                    _ = RecoverInterruptedStreamAsync(pos, trigger);
                }
                catch (Exception ex)
                {
                    LogToFile("state_trace.txt", $"  -> Premature {trigger} recovery dispatch failed: {ex.Message}");
                }
                return;
            }
        }

        StopPlaybackStallWatchdog();

        if (!string.IsNullOrEmpty(NextEpisodeContentId))
        {
            if (_playingNextShown)
            {
                LogToFile("state_trace.txt", "  -> Next-episode prompt already shown");
                return;
            }

            LogToFile("state_trace.txt", $"  -> Next-episode prompt requested ({trigger})");
            _playingNextShown = true;
            ShowPlayingNextRequested?.Invoke();
            return;
        }

        if (_playingNextShown)
        {
            LogToFile("state_trace.txt", "  -> Finished post-roll already shown");
            return;
        }

        // The current WebUI keeps the post-roll surface for end-of-series and
        // standalone playback too, showing Finished plus an On Deck carousel.
        LogToFile("state_trace.txt", $"  -> Finished post-roll requested ({trigger})");
        _playingNextShown = true;
        ShowPlayingNextRequested?.Invoke();
    }

    private static bool IsAtMediaEnd(double position, double duration)
    {
        if (duration <= 0 || position < 0)
            return false;

        var tolerance = Math.Clamp(duration * 0.0005, 0.75, 3.0);
        return position >= duration - tolerance;
    }

    private void WireMpvEvents()
    {
        if (_mpv == null) return;
        UnwireMpvEvents();

        _mpvPositionHandler = (pos) =>
        {
            if (_switchingContent)
                return;

            var mediaPosition = PlaybackTimeline.ToMediaTime(pos, _timelineOffsetSeconds);
            Position = mediaPosition;
            _playbackManager?.UpdatePosition(mediaPosition, IsPaused);
            PositionChanged?.Invoke(mediaPosition);

            if (_prematureEofRecoveryPosition > 0 && mediaPosition > _prematureEofRecoveryPosition + 30)
            {
                LogToFile("state_trace.txt", $"Stream recovery confirmed: advanced from {_prematureEofRecoveryPosition:F1} to {mediaPosition:F1}");
                _prematureEofRecoveryPosition = 0;
                _prematureEofLastAttemptMs = 0;
                _prematureEofStreak = 0;
            }
        };
        _mpv.PositionChanged += _mpvPositionHandler;

        _mpvDurationHandler = (dur) =>
        {
            if (_switchingContent)
                return;

            var mediaDuration = PlaybackTimeline.ResolveMediaDuration(
                dur,
                _timelineOffsetSeconds,
                _transportDurationSeconds);
            Duration = mediaDuration;
            DurationChanged?.Invoke(mediaDuration);
        };
        _mpv.DurationChanged += _mpvDurationHandler;

        _mpvPauseHandler = (paused) =>
        {
            IsPaused = paused;
            var mediaPosition = CurrentMediaPosition;
            _playbackManager?.UpdatePosition(mediaPosition, paused);
            if (!_closing && !_switchingContent && State != PlayerState.Idle)
                _ = ReportSeekProgressAsync(mediaPosition, paused);
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
            var restorePaused = _restorePausedAfterLoad == true;
            CancelPendingFileLoadTimeout();
            Interlocked.Exchange(ref _consecutiveFileLoadTimeouts, 0);
            IsLoading = false;
            _switchingContent = false; // Safe to receive PlaybackEnded now
            _qualitySwitchActive = false;
            _playingNextShown = false;
            _prematureEofRecoveryActive = false;
            if (!wasPrematureEofRecovery)
            {
                _prematureEofStreak = 0;
                _prematureEofLastAttemptMs = 0;
                _prematureEofRecoveryPosition = 0;
            }
            LogToFile("state_trace.txt", wasPrematureEofRecovery
                ? "FileLoaded fired (premature EOF recovery)"
                : "FileLoaded fired");

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

            if (restorePaused)
                _mpv?.Pause();
            else
                _mpv?.Play();

            IsPaused = restorePaused;
            var mediaPosition = CurrentMediaPosition;
            var mediaDuration = CurrentMediaDuration;
            Position = mediaPosition;
            Duration = mediaDuration;
            _playbackManager?.UpdatePosition(mediaPosition, restorePaused);
            PositionChanged?.Invoke(mediaPosition);
            DurationChanged?.Invoke(mediaDuration);

            App.MainWindowInstance?.HideLoadingOverlay();
            StartPlaybackStallWatchdog();
            ContentLoaded?.Invoke();

            // Send OSC data + load subtitles on background thread with cancellation
            var ct = _playbackCts?.Token ?? CancellationToken.None;
            Task.Run(async () =>
            {
                if (ct.IsCancellationRequested) return;
                SendTitleToOsc();
                SendMediaInfoToOsc();
                SendAudioTrackListToOsc();
                SendChapterListToOsc();
                SendSubtitleListToOsc();
                SendQualityInfoToOsc();
                SendMarkersToOsc();
                SendMarkerEditAvailabilityToOsc();
                if (ct.IsCancellationRequested) return;
                LoadSubtitles();
                if (_pendingInitialServerSubtitleIndex is int initialSubtitleIndex)
                {
                    _pendingInitialServerSubtitleIndex = null;
                    await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false);
                }
                await SendSubtitleAiAvailabilityToOscAsync(ct);
            }, ct);

            // Auto-compute next-episode hint if no caller already set one.
            // This fires for every playback session — including ones launched
            // directly from a card (LandscapeCard, PosterCard) that bypass
            // ItemDetailPage.SetNextEpisodeHintIfApplicable.
            _ = AutoDetectNextEpisodeAsync(ct);

            // Connect WebSocket for real-time admin control
            try { ConnectWebSocket(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"WebSocket connect failed: {ex.Message}"); }
        };
        _mpv.FileLoaded += _mpvFileLoadedHandler;

        _mpvPlaybackEndedHandler = () => HandleMpvEndSignal("end-file");
        _mpv.PlaybackEnded += _mpvPlaybackEndedHandler;

        _mpvEofReachedHandler = () => HandleMpvEndSignal("eof-reached");
        _mpv.EofReached += _mpvEofReachedHandler;

        _mpvPlaybackErrorHandler = HandleMpvPlaybackError;
        _mpv.PlaybackError += _mpvPlaybackErrorHandler;

        _mpv.ScriptMessageReceived += OnScriptMessage;
        _mpvErrorHandler = (msg) => LogToFile("mpv_error.txt", msg);
        _mpv.Error += _mpvErrorHandler;
    }

    private void HandleMpvPlaybackError(string message)
    {
        LogToFile(
            "state_trace.txt",
            $"PlaybackError: {message} _switchingContent={_switchingContent} " +
            $"_prematureEofRecoveryActive={_prematureEofRecoveryActive}");

        if (_closing || State == PlayerState.Idle)
            return;

        var mediaPosition = CurrentMediaPosition;
        var mediaDuration = CurrentMediaDuration;
        if (IsAtMediaEnd(mediaPosition, mediaDuration))
            return;

        if (_switchingContent)
        {
            CancelPendingFileLoadTimeout();
            var attempt = Interlocked.Increment(ref _consecutiveFileLoadTimeouts);
            LogToFile(
                "state_trace.txt",
                $"File load failed: attempt={attempt} mediaPos={mediaPosition:F1} message={message}");

            if (attempt == 1)
            {
                _qualitySwitchActive = false;
                _prematureEofRecoveryActive = true;
                _prematureEofRecoveryPosition = mediaPosition;
                IsLoading = true;
                ShowNotice(
                    "Reconnecting playback",
                    "The media stream was interrupted. Resuming from your current position…",
                    "warning");
                _ = RecoverInterruptedStreamAsync(mediaPosition, "file-load-error");
                return;
            }

            _switchingContent = false;
            _qualitySwitchActive = false;
            _prematureEofRecoveryActive = false;
            _prematureEofRecoveryPosition = 0;
            IsLoading = false;
            var detail = message.Contains("loading failed", StringComparison.OrdinalIgnoreCase)
                ? "The media stream could not be loaded after a retry. It may be unavailable or the server may be experiencing issues."
                : $"The media stream could not resume after a retry: {message}";
            ErrorMessage = detail;
            var dispatcher = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(async () =>
                {
                    await CloseAsync();
                    App.MainWindowInstance?.ShowPlaybackError("Playback failed", detail);
                });
            }
            else
            {
                _ = CloseAsync();
            }
            return;
        }

        if (_prematureEofRecoveryActive)
            return;

        // Mark recovery synchronously so a following END_FILE signal is
        // suppressed, then rebuild the transport at canonical media time.
        _prematureEofRecoveryActive = true;
        _switchingContent = true;
        _ = RecoverInterruptedStreamAsync(mediaPosition, "playback-error");
    }

    // ── Content switching (version/audio) ────────────────────────────────

    public async Task SwitchVersionAsync(FileVersion version)
    {
        if (_mpv == null || _playbackManager == null) return;
        await CancelAndDrainTransportRestartsAsync();

        var manager = _playbackManager;
        var ct = _playbackCts?.Token ?? CancellationToken.None;
        var wasPaused = _mpv.IsPaused;
        var currentPos = CurrentMediaPosition;
        _switchingContent = true;
        // Don't call _mpv.Stop() — let current stream keep playing while we set up the new one

        try
        {
            var session = await manager.StartReplacementSessionAsync(
                version.FileId,
                currentPos,
                forceStartPosition: true,
                previousFinalPosition: currentPos,
                ct: ct);
            try { SessionStarted?.Invoke(session.SessionId); } catch { }
            var streamUrl = manager.StreamUrl;
            if (string.IsNullOrWhiteSpace(streamUrl))
                throw new InvalidOperationException("No stream URL for selected version.");

            var prepared = await PreparePlaybackTransportAsync(
                session,
                version,
                streamUrl,
                currentPos,
                ct);

            ct.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_playbackManager, manager) ||
                !ReferenceEquals(manager.CurrentSession, session))
                return;

            _requestedMediaFileId = version.FileId;
            PlayMethod = session.PlayMethod;
            Resolution = version.Resolution;
            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused: wasPaused);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", "Version switch canceled");
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            LogToFile("player_quality_switch_error.txt", ex.ToString());
            ErrorMessage = $"Failed to switch quality: {ex.Message}";
            if (!_closing && ReferenceEquals(_playbackManager, manager))
            {
                await CloseAsync();
                App.MainWindowInstance?.ShowPlaybackError("Version switch failed", ex.Message);
            }
        }
    }

    public async Task SwitchAudioTrackAsync(int trackIndex)
    {
        if (_mpv == null || _playbackManager == null) return;
        await CancelAndDrainTransportRestartsAsync();

        var manager = _playbackManager;
        var ct = _playbackCts?.Token ?? CancellationToken.None;
        var wasPaused = _mpv.IsPaused;
        var currentPos = CurrentMediaPosition;
        if (_activeTransportPlan?.TransportKind == PlaybackTransportKind.DirectProgressive)
        {
            // A native direct stream already contains every audio track. Keep
            // it direct instead of asking the browser-oriented PATCH /audio
            // resolver, which can unnecessarily turn lossless audio into a
            // remux/transcode.
            _mpv.SetAudioTrack(trackIndex + 1);
            if (manager.CurrentSession != null)
                manager.CurrentSession.AudioTrackIndex = trackIndex;
            await PersistAudioPreferenceAsync(trackIndex);
            SendAudioTrackListToOsc();
            return;
        }

        var previousPlan = _activeTransportPlan;
        var previousCanSeekAnywhere = _canSeekAnywhere;
        var previousRecipe = _activeHlsRecipe;
        var previousDuration = CurrentMediaDuration;
        _switchingContent = true;
        IsLoading = true;

        try
        {
            var expectedSessionId = manager.SessionId
                ?? throw new InvalidOperationException("The playback session is no longer active.");
            var response = await _playbackApi.ChangeAudioTrackAsync(
                expectedSessionId,
                trackIndex,
                currentPos,
                ct);
            ct.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_playbackManager, manager) ||
                !string.Equals(manager.SessionId, expectedSessionId, StringComparison.Ordinal))
                return;
            manager.ApplyAudioChange(response);

            PlayMethod = response.PlayMethod;
            var session = manager.CurrentSession
                ?? throw new InvalidOperationException("The playback session ended during the audio switch.");
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("No active version available for the audio switch.");
            var remoteUrl = NormalizePlaybackUrl(response.StreamUrl);
            PreparedPlaybackTransport prepared;

            if (PlaybackTransportPlanner.IsHlsStreamUrl(response.StreamUrl) ||
                string.Equals(response.PlaybackInfo?.StreamType, "hls", StringComparison.OrdinalIgnoreCase))
            {
                // PATCH /audio has already restarted/prepared an HLS transport.
                // Calling /transcode/start again here would tear that stream down.
                var plan = previousPlan?.IsHls == true
                    ? previousPlan
                    : response.PlayMethod.Equals("remux", StringComparison.OrdinalIgnoreCase)
                        ? new PlaybackTransportPlan(
                            PlaybackSemanticMethod.Remux,
                            PlaybackTransportKind.RemuxHls,
                            RequiresTranscodeStartPreparation: true)
                        : new PlaybackTransportPlan(
                            PlaybackSemanticMethod.Transcode,
                            PlaybackTransportKind.TranscodeHls,
                            RequiresTranscodeStartPreparation: true);
                var copyWindow = previousPlan?.IsHls == true
                    ? !previousCanSeekAnywhere
                    : plan.TransportKind == PlaybackTransportKind.RemuxHls;
                var localUrl = PrepareHlsStreamForMpv(remoteUrl);
                var recipe = previousRecipe == null
                    ? new TranscodeStartRequest
                    {
                        SessionId = session.SessionId,
                        SeekSeconds = currentPos,
                        TargetResolution = copyWindow ? "" : version.Resolution,
                        TargetCodecVideo = copyWindow ? "copy" : "h264",
                        TargetCodecAudio = response.PlaybackInfo?.TranscodeAudio == true ? "aac" : "copy",
                        TargetBitrateKbps = copyWindow ? 0 : 8000,
                        SegmentDuration = 2,
                        SubtitleTrackIndex = -1,
                        SubtitleBurnIn = false
                    }
                    : CloneTranscodeRecipe(previousRecipe, session.SessionId, currentPos);
                recipe.TargetCodecAudio = response.PlaybackInfo?.TranscodeAudio == true
                    ? "aac"
                    : "copy";

                prepared = new PreparedPlaybackTransport(
                    plan,
                    localUrl,
                    MpvLoadStartSeconds: copyWindow ? 0 : currentPos,
                    ResumeAfterLoadSeconds: 0,
                    TimelineOffsetSeconds: copyWindow ? currentPos : 0,
                    DurationSeconds: previousDuration > 0 ? previousDuration : GetKnownDuration(session, version),
                    CanSeekAnywhere: !copyWindow,
                    HlsRecipe: recipe);
            }
            else
            {
                prepared = await PreparePlaybackTransportAsync(
                    session,
                    version,
                    remoteUrl,
                    currentPos,
                    ct);
            }

            ct.ThrowIfCancellationRequested();
            if (_closing || !ReferenceEquals(_playbackManager, manager) ||
                !ReferenceEquals(manager.CurrentSession, session))
                return;
            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused: wasPaused);

            await PersistAudioPreferenceAsync(trackIndex);
            SendAudioTrackListToOsc();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", "Audio switch canceled");
        }
        catch (Exception ex)
        {
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_audio_switch_error.txt", ex.ToString());
            var (title, detail) = DescribePlaybackError(ex);
            ErrorMessage = detail;
            ShowNotice(title, detail, "error");
            SendAudioTrackListToOsc();
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

    private static bool IsUnsupportedBitmapSubtitle(SubtitleTrackInfo track)
    {
        var codec = track.Codec?.ToLowerInvariant() ?? "";
        return codec is "dvdsub" or "dvd_subtitle" or "vobsub" or "dvbsub" or "dvb_subtitle";
    }

    /// <summary>
    /// DVD/VOBSUB and DVB bitmap tracks have no usable sidecar representation.
    /// Restart the current session as HLS with server-side burn-in, preserving
    /// canonical position and pause state. PGS deliberately does not use this
    /// path because libmpv can render the server's .sup sidecar directly.
    /// </summary>
    private async Task SetBitmapSubtitleBurnInAsync(SubtitleTrackInfo? track)
    {
        if (_mpv == null || _playbackManager?.CurrentSession is not { } session) return;
        await CancelAndDrainTransportRestartsAsync();
        var ct = _playbackCts?.Token ?? CancellationToken.None;
        await _transportRestartGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manager = _playbackManager;
            if (manager?.CurrentSession != session || _mpv == null) return;
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? Versions.FirstOrDefault(v => v.FileId == (_requestedMediaFileId ?? 0))
                ?? throw new InvalidOperationException("The subtitle source version is unavailable.");
            var wasPaused = _mpv.IsPaused;
            var position = CurrentMediaPosition;
            if (track is not null && _activeHlsRecipe?.SubtitleBurnIn != true)
            {
                _preBitmapBurnInPlan = _activeTransportPlan;
                _preBitmapBurnInQualityTier = _activeQualityTier;
            }
            _switchingContent = true;
            IsLoading = true;
            _mpv.Pause();
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(
                () => App.MainWindowInstance?.ShowLoadingOverlay());

            // Bitmap burn-in is a temporary transport override. If it was
            // entered from progressive direct/remux playback, turning it off
            // must reconstruct the native transport rather than keep an
            // unnecessary H.264 transcode alive for the remainder of playback.
            if (track is null && _activeHlsRecipe?.SubtitleBurnIn == true &&
                _preBitmapBurnInPlan is { IsHls: false })
            {
                var audioTrackIndex = session.AudioTrackIndex >= 0 ? session.AudioTrackIndex : (int?)null;
                await manager.StopSessionAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var restoredSession = await manager.StartSessionAsync(
                    version.FileId,
                    position,
                    forceStartPosition: true,
                    audioTrackIndex: audioTrackIndex,
                    forceDirectAudioSelection: audioTrackIndex.HasValue,
                    ct: ct).ConfigureAwait(false);
                try { SessionStarted?.Invoke(restoredSession.SessionId); } catch { }
                var remoteUrl = manager.StreamUrl;
                if (string.IsNullOrWhiteSpace(remoteUrl))
                    throw new InvalidOperationException("No stream URL was returned while restoring native playback.");
                var restored = await PreparePlaybackTransportAsync(
                    restoredSession, version, remoteUrl, position, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (_closing || manager.CurrentSession != restoredSession) return;
                ApplyPreparedTransport(restored);
                PlayMethod = restoredSession.PlayMethod;
                _activeQualityTier = _preBitmapBurnInQualityTier ?? "original";
                _preBitmapBurnInPlan = null;
                _preBitmapBurnInQualityTier = null;
                BeginMpvLoad(restored, restorePaused: wasPaused);
                _mpv.ShowOsdText("Bitmap subtitles off · direct playback restored", 2500);
                SendQualityInfoToOsc();
                return;
            }

            var recipe = _activeHlsRecipe is null
                ? new TranscodeStartRequest
                {
                    SessionId = session.SessionId,
                    TargetResolution = version.Resolution,
                    TargetCodecVideo = "h264",
                    TargetCodecAudio = "aac",
                    TargetBitrateKbps = 8000,
                    SegmentDuration = 2,
                }
                : CloneTranscodeRecipe(_activeHlsRecipe, session.SessionId, position);
            recipe.SeekSeconds = position;
            recipe.SubtitleTrackIndex = track?.Index ?? -1;
            recipe.SubtitleMediaFileId = track?.MediaFileId ?? 0;
            recipe.SubtitleBurnIn = track is not null;
            if (recipe.SubtitleBurnIn && recipe.TargetCodecVideo.Equals("copy", StringComparison.OrdinalIgnoreCase))
            {
                recipe.TargetCodecVideo = "h264";
                recipe.TargetCodecAudio = "aac";
                recipe.TargetResolution = version.Resolution;
                recipe.TargetBitrateKbps = 8000;
            }

            var plan = new PlaybackTransportPlan(
                PlaybackSemanticMethod.Transcode,
                PlaybackTransportKind.TranscodeHls,
                RequiresTranscodeStartPreparation: true);
            var prepared = await PrepareHlsTransportAsync(
                plan, session, recipe, CurrentMediaDuration, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (_closing || manager.CurrentSession != session) return;
            ApplyPreparedTransport(prepared);
            PlayMethod = "transcode";
            if (track is null)
            {
                _preBitmapBurnInPlan = null;
                _preBitmapBurnInQualityTier = null;
            }
            BeginMpvLoad(prepared, restorePaused: wasPaused);
            _mpv.ShowOsdText(track is null ? "Bitmap subtitles off" : $"{track.Label} · burned in", 2500);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _switchingContent = false;
            IsLoading = false;
            LogToFile("player_subtitle_error.txt", ex.ToString());
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
                App.MainWindowInstance?.ShowPlaybackError("Subtitle switch failed", ex.Message));
        }
        finally { _transportRestartGate.Release(); }
    }

    private async Task SelectSubtitleByServerIndexAsync(int serverTrackIndex, bool persist = true)
    {
        if (_mpv == null) return;
        if (serverTrackIndex < 0)
        {
            if (persist)
                await SetSubtitleTrackAndPersistAsync(0, null, null);
            else
                _mpv.SetSubtitleTrack(0);
            _mpv.SendScriptMessage("osc-set-active-subtitle", "-1");
            return;
        }

        var track = _playbackManager?.CurrentSession?.SubtitleUrls?
            .FirstOrDefault(t => t.Index == serverTrackIndex);
        if (track == null)
        {
            // Pre-play embedded selection still passes an embedded ordinal.
            _mpv.SetSubtitleTrack(serverTrackIndex + 1);
            _mpv.SendScriptMessage(
                "osc-set-active-subtitle",
                serverTrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        var mpvTrackIndex = string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(1, ResolveNativeEmbeddedSid(track))
            : Math.Max(1, track.Index + 1);
        if (persist)
            await SetSubtitleTrackAndPersistAsync(mpvTrackIndex, track.Language, track);
        else
            SelectSubtitleTrack(track);
        _mpv.SendScriptMessage(
            "osc-set-active-subtitle",
            serverTrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void SelectSubtitleTrack(SubtitleTrackInfo track)
    {
        if (_mpv == null) return;
        if (IsUnsupportedBitmapSubtitle(track))
        {
            _ = SetBitmapSubtitleBurnInAsync(track);
            return;
        }

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
        _mpv.AddSubtitle(pair.Value.FullUrl, label, track.Language, select: true);
        var selectedSid = Math.Max(1, (int)Math.Round(_mpv.GetPropertyDouble("sid")));
        _loadedExternalSubtitleSids[track.Index] = selectedSid;
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
        var videoTrack = version.VideoTracks?.FirstOrDefault();

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
            ["video_profile"] = videoTrack?.Profile ?? "",
            ["video_bitrate"] = videoTrack?.Bitrate ?? 0,
            ["video_range"] = !string.IsNullOrWhiteSpace(videoTrack?.DolbyVision)
                ? string.IsNullOrWhiteSpace(videoTrack.VideoRange)
                    ? videoTrack.DolbyVision
                    : $"{videoTrack.DolbyVision} ({videoTrack.VideoRange})"
                : videoTrack?.VideoRange ?? videoTrack?.VideoRangeType ?? (version.Hdr ? "HDR" : "SDR"),
            ["audio_bitrate"] = audioTrack?.Bitrate ?? 0,
            ["audio_sample_rate"] = audioTrack?.SampleRate ?? 0,
            ["requested_source"] = BuildRequestedSourceLabel(version),
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-media-info", json);

        // Send stream info
        var pi = session.PlaybackInfo;
        var playMethodDisplay = session.PlayMethod switch
        {
            "direct" => "Direct Play",
            "remux" => "Remux",
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

    private string BuildRequestedSourceLabel(FileVersion currentVersion)
    {
        if (!_requestedMediaFileId.HasValue || _requestedMediaFileId.Value == currentVersion.FileId)
            return "";
        var requested = Versions.FirstOrDefault(item => item.FileId == _requestedMediaFileId.Value);
        if (requested == null) return "";
        return string.Join(" ", new[]
        {
            requested.Resolution,
            requested.CodecVideo?.ToUpperInvariant(),
            requested.Hdr ? "HDR" : null
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
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
            ["forced"] = t.Forced,
            ["hearing_impaired"] = t.HearingImpaired,
            ["media_file_id"] = t.MediaFileId
        }).ToArray();

        var json = System.Text.Json.JsonSerializer.Serialize(jsonTracks);
        _mpv.SendScriptMessage("osc-set-subtitles", json);
        _mpv.SendScriptMessage(
            "osc-set-active-subtitle",
            (_pendingInitialServerSubtitleIndex ?? -1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void SendAudioTrackListToOsc()
    {
        if (_mpv == null)
            return;

        var tracks = ActiveVersion?.AudioTracks ?? [];
        var payload = tracks.Select((track, index) => new
        {
            index,
            title = track.Title,
            embedded_title = track.EmbeddedTitle,
            language = track.Language,
            codec = track.Codec,
            layout = track.Layout,
            channels = track.Channels,
            bitrate = track.Bitrate,
            sample_rate = track.SampleRate,
            bit_depth = track.BitDepth,
            @default = track.Default,
        });
        _mpv.SendScriptMessage("osc-set-audio-tracks", JsonSerializer.Serialize(payload));
        _mpv.SendScriptMessage(
            "osc-set-active-audio",
            (_playbackManager?.CurrentSession?.AudioTrackIndex ?? -1)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void SendChapterListToOsc()
    {
        if (_mpv == null)
            return;

        var chapters = ActiveVersion?.Chapters ?? [];
        var payload = chapters.Select(chapter => new
        {
            index = chapter.Index,
            title = chapter.Title,
            start_seconds = chapter.StartSeconds,
            end_seconds = chapter.EndSeconds,
            thumbnail_url = chapter.ThumbnailUrl,
        });
        _mpv.SendScriptMessage("osc-set-chapters", JsonSerializer.Serialize(payload));
    }

    private async Task LoadChapterThumbnailForOscAsync(int chapterIndex)
    {
        var chapter = ActiveVersion?.Chapters?.FirstOrDefault(item => item.Index == chapterIndex);
        var url = chapter?.ThumbnailUrl;
        var mpv = _mpv;
        var contentId = ContentId;
        if (chapter == null || string.IsNullOrWhiteSpace(url) || mpv == null || string.IsNullOrWhiteSpace(contentId))
            return;

        if (!_chapterThumbnailRequests.TryAdd(chapterIndex, 0))
            return;

        var ownerCts = _chapterThumbnailCts;
        if (ownerCts == null)
        {
            var created = _playbackCts == null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(_playbackCts.Token);
            ownerCts = Interlocked.CompareExchange(ref _chapterThumbnailCts, created, null);
            if (ownerCts == null)
                ownerCts = created;
            else
                created.Dispose();
        }
        var token = ownerCts.Token;

        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var sourcePath = await imageService.GetImageDiskPathAsync(
                $"chapter_{contentId}_{chapterIndex}",
                "chapter",
                url,
                httpClient,
                token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;

            token.ThrowIfCancellationRequested();
            using var bitmap = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), sourcePath);
            token.ThrowIfCancellationRequested();
            var pixels = bitmap.GetPixelBytes();
            var width = checked((int)bitmap.SizeInPixels.Width);
            var height = checked((int)bitmap.SizeInPixels.Height);
            if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
                return;

            var rawDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiloPlayer",
                "ChapterThumbnailOverlay");
            Directory.CreateDirectory(rawDir);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..20];
            var rawPath = Path.Combine(rawDir, $"{hash}-{width}x{height}.bgra");
            if (!File.Exists(rawPath) || new FileInfo(rawPath).Length != pixels.Length)
                await File.WriteAllBytesAsync(rawPath, pixels, token).ConfigureAwait(false);

            lock (_chapterThumbnailFileLock)
                _chapterThumbnailRawFiles.Add(rawPath);

            if (token.IsCancellationRequested || !ReferenceEquals(_mpv, mpv) || ContentId != contentId)
                return;

            mpv.SendScriptMessage(
                "osc-set-chapter-thumbnail",
                chapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                rawPath,
                width.ToString(System.Globalization.CultureInfo.InvariantCulture),
                height.ToString(System.Globalization.CultureInfo.InvariantCulture),
                checked(width * 4).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Chapter thumbnail overlay load failed: {ex.Message}");
        }
        finally
        {
            _chapterThumbnailRequests.TryRemove(chapterIndex, out _);
        }
    }

    private void ClearChapterThumbnailOverlayCache()
    {
        var cts = Interlocked.Exchange(ref _chapterThumbnailCts, null);
        cts?.Cancel();
        cts?.Dispose();
        _chapterThumbnailRequests.Clear();
        _mpv?.SendScriptMessage("osc-clear-chapter-thumbnail");

        string[] files;
        lock (_chapterThumbnailFileLock)
        {
            files = [.. _chapterThumbnailRawFiles];
            _chapterThumbnailRawFiles.Clear();
        }
        if (files.Length == 0)
            return;

        _ = Task.Run(() =>
        {
            foreach (var file in files)
            {
                try { File.Delete(file); }
                catch { }
            }
        });
    }

    private void SendMarkersToOsc()
    {
        if (_mpv == null || _playbackManager?.WatchDetail == null) return;
        var wd = _playbackManager.WatchDetail;

        var markers = new Dictionary<string, double>
        {
            ["intro_start"] = wd.Intro?.Start ?? 0,
            ["intro_end"] = wd.Intro?.End ?? 0,
            ["recap_start"] = wd.Recap?.Start ?? 0,
            ["recap_end"] = wd.Recap?.End ?? 0,
            ["credits_start"] = wd.Credits?.Start ?? 0,
            ["credits_end"] = wd.Credits?.End ?? 0,
            ["preview_start"] = wd.Preview?.Start ?? 0,
            ["preview_end"] = wd.Preview?.End ?? 0
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
            ["label"] = $"{v.Resolution} {v.CodecVideo.ToUpperInvariant()}{(v.Hdr ? " HDR" : "")}".Trim(),
            ["resolution"] = v.Resolution ?? ""
        }).ToArray();

        var info = new Dictionary<string, object?>
        {
            ["versions"] = versions,
            ["active_file_id"] = session.MediaFileId,
            ["requested_file_id"] = _requestedMediaFileId ?? session.MediaFileId,
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
                // The state transition is queued to the XAML thread, but the
                // OSC lives in the native popup and should repaint immediately.
                PublishFullscreenVisualState(false);
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
            }
            else
            {
                _videoWindow.EnterFullscreen();
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Fullscreen));
            }
        };
    }


    // ── Script message dispatch (Lua → Host) ─────────────────────────────

    private void OnScriptMessage(string[] args)
    {
        if (args.Length == 0) return;
        // Only handle silo-* messages (Lua→Host intents).
        // Ignore echo-back of host→Lua messages (osc-mouse-move etc.) to avoid flooding.
        if (!args[0].StartsWith("silo-")) return;

        LogToFile("state_trace.txt", $"ScriptMessage received: {args[0]} thread={Environment.CurrentManagedThreadId}");

        var dispatch = App.MainWindowInstance?.DispatcherQueue;
        if (dispatch == null) return;

        switch (args[0])
        {
            case "silo-exit":
                if (State == PlayerState.Idle) return; // Prevent duplicate close
                dispatch.TryEnqueue(() => _ = CloseAsync());
                break;
            case "silo-fullscreen-toggle":
                dispatch.TryEnqueue(ToggleFullscreenFromOsc);
                break;
            case "silo-pip-toggle":
                dispatch.TryEnqueue(() =>
                {
                    if (State == PlayerState.PictureInPicture)
                        SetState(PlayerState.Expanded);
                    else
                        EnterPictureInPicture();
                });
                break;
            case "silo-minimize":
                dispatch.TryEnqueue(Minimize);
                break;
            case "silo-subtitle-select":
                if (args.Length > 1 && int.TryParse(args[1], out var subIdx))
                {
                    _ = SelectSubtitleByServerIndexAsync(subIdx);
                }
                break;
            case "silo-subtitle-search":
                dispatch.TryEnqueue(() => _ = ShowSubtitleSearchDialogAsync());
                break;
            case "silo-subtitle-appearance":
                dispatch.TryEnqueue(() => _ = ShowSubtitleAppearanceDialogAsync());
                break;
            case "silo-subtitle-ai":
                dispatch.TryEnqueue(() => _ = ShowSubtitleAiDialogAsync());
                break;
            case "silo-marker-edit":
                dispatch.TryEnqueue(() => _ = ShowMarkerEditDialogAsync());
                break;
            case "silo-audio-select":
                if (args.Length > 1 && int.TryParse(args[1], out var audioIdx))
                    dispatch.TryEnqueue(() => _ = SwitchAudioTrackAsync(audioIdx));
                break;
            case "silo-version-select":
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
            case "silo-quality-select":
                if (args.Length > 1)
                {
                    _switchingContent = true;
                    _qualitySwitchActive = true;
                    LogToFile("state_trace.txt", $"Quality select: tier={args[1]} flags set TRUE");
                    dispatch.TryEnqueue(() => _ = SwitchQualityTierAsync(args[1]));
                }
                break;
            case "silo-seek-absolute":
                if (args.Length > 1 && double.TryParse(
                        args[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seekPosition))
                {
                    var forceResume = args.Length > 2 &&
                        bool.TryParse(args[2], out var parsedResume) &&
                        parsedResume;
                    dispatch.TryEnqueue(() => SeekFastTo(seekPosition, forceResume));
                }
                break;
            case "silo-seek-relative":
                if (args.Length > 1 && double.TryParse(
                        args[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var seekDelta))
                {
                    var relativeForceResume = args.Length > 2 &&
                        bool.TryParse(args[2], out var relativeParsedResume) &&
                        relativeParsedResume;
                    dispatch.TryEnqueue(() => SeekFastTo(CurrentMediaPosition + seekDelta, relativeForceResume));
                }
                break;
            case "silo-volume-changed":
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
            case "silo-next-episode":
                // User clicked the in-player Next Episode button. Jump
                // straight to the next episode — ContinuePlayingNextAsync
                // tears down the current session and starts the next one.
                LogToFile("state_trace.txt", "Next Episode button clicked");
                dispatch.TryEnqueue(() => _ = ContinuePlayingNextAsync());
                break;
            case "silo-prev-episode":
                LogToFile("state_trace.txt", "Previous Episode button clicked");
                dispatch.TryEnqueue(() => _ = PlayPreviousEpisodeAsync());
                break;
            case "silo-chapter-thumbnail-request":
                if (args.Length > 1 && int.TryParse(args[1], out var chapterIndex))
                    _ = LoadChapterThumbnailForOscAsync(chapterIndex);
                break;
            case "silo-watch-party-action":
                if (args.Length > 1)
                {
                    var action = args[1];
                    dispatch.TryEnqueue(() =>
                    {
                        try { WatchTogetherActionRequested?.Invoke(action); }
                        catch (Exception ex) { LogToFile("state_trace.txt", $"Watch Party action failed: {ex.Message}"); }
                    });
                }
                break;
            case "silo-cursor-hidden":
                dispatch.TryEnqueue(() => _videoWindow?.SetCursorVisible(false));
                break;
            case "silo-cursor-visible":
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
        await CancelAndDrainTransportRestartsAsync();

        var manager = _playbackManager;
        var ct = _playbackCts?.Token ?? CancellationToken.None;
        var wasPaused = _mpv.IsPaused;
        var currentPos = CurrentMediaPosition;
        var switchSucceeded = false;

        if (tierId == "original")
        {
            // If already on direct play or remux, this is a no-op
            if (PlayMethod is "direct" or "remux")
            {
                _activeQualityTier = tierId;
                _switchingContent = false;
                _qualitySwitchActive = false;
                _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
                return;
            }

            // Currently transcoding — switch back to direct play
            var currentFileId = _requestedMediaFileId ?? manager.CurrentSession?.MediaFileId;
            var version = Versions.FirstOrDefault(v => v.FileId == currentFileId);
            if (version == null)
            {
                _switchingContent = false;
                _qualitySwitchActive = false;
                _mpv.ShowOsdText("Original version is unavailable", 3000);
                return;
            }

            if (version != null)
            {
                try
                {
                    var session = await manager.StartReplacementSessionAsync(
                        version.FileId,
                        currentPos,
                        forceStartPosition: true,
                        previousFinalPosition: currentPos,
                        ct: ct);
                    try { SessionStarted?.Invoke(session.SessionId); } catch { }
                    PlayMethod = session.PlayMethod;
                    var remoteUrl = manager.StreamUrl;
                    if (string.IsNullOrWhiteSpace(remoteUrl))
                        throw new InvalidOperationException("No stream URL was returned for original quality.");
                    var prepared = await PreparePlaybackTransportAsync(
                        session,
                        version,
                        remoteUrl,
                        currentPos,
                        ct);

                    ct.ThrowIfCancellationRequested();
                    if (_closing || !ReferenceEquals(_playbackManager, manager) ||
                        !ReferenceEquals(manager.CurrentSession, session))
                        return;

                    ApplyPreparedTransport(prepared);
                    BeginMpvLoad(prepared, restorePaused: wasPaused);
                    SendMediaInfoToOsc();
                    switchSucceeded = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    LogToFile("state_trace.txt", "Quality reset canceled");
                }
                catch (Exception ex)
                {
                    _switchingContent = false;
                    _qualitySwitchActive = false;
                    LogToFile("player_quality_switch_error.txt", ex.ToString());
                    await CloseAsync();
                    App.MainWindowInstance?.ShowPlaybackError("Quality switch failed", ex.Message);
                }
            }
        }
        else
        {
            var transportReplaced = false;
            var (resolution, bitrate) = tierId switch
            {
                "auto"       => ("", 0),
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
                var expectedSessionId = manager.SessionId
                    ?? throw new InvalidOperationException("The playback session is no longer active.");
                var recipe = new TranscodeStartRequest
                {
                    SessionId = expectedSessionId,
                    SeekSeconds = currentPos, // Start encoding from user's position
                    TargetResolution = resolution,
                    TargetCodecVideo = "h264",
                    TargetCodecAudio = "aac",
                    TargetBitrateKbps = bitrate,
                    SegmentDuration = 2,
                    SubtitleTrackIndex = -1,
                    SubtitleBurnIn = false
                };
                var transcodeResponse = await _playbackApi.StartTranscodeAsync(recipe, ct);
                ct.ThrowIfCancellationRequested();
                if (_closing || !ReferenceEquals(_playbackManager, manager) ||
                    !string.Equals(manager.SessionId, expectedSessionId, StringComparison.Ordinal))
                    return;

                LogToFile(
                    "state_trace.txt",
                    $"Transcode response: status={transcodeResponse.Status} " +
                    $"manifest={PlaybackUrlRedactor.Redact(transcodeResponse.ManifestUrl)} " +
                    $"switchedFileId={transcodeResponse.SwitchedFileId} " +
                    $"playerStart={transcodeResponse.PlayerStartSeconds} duration={transcodeResponse.DurationSeconds}");

                if (transcodeResponse.SwitchedFileId is int switchedFileId &&
                    manager.CurrentSession != null)
                {
                    manager.CurrentSession.MediaFileId = switchedFileId;
                }

                var remoteManifestUrl = NormalizePlaybackUrl(transcodeResponse.ManifestUrl);
                transportReplaced = true;
                var localUrl = PrepareHlsStreamForMpv(remoteManifestUrl);
                var prepared = new PreparedPlaybackTransport(
                    new PlaybackTransportPlan(
                        PlaybackSemanticMethod.Transcode,
                        PlaybackTransportKind.TranscodeHls,
                        RequiresTranscodeStartPreparation: true),
                    localUrl,
                    MpvLoadStartSeconds: transcodeResponse.CanSeekAnywhere
                        ? Math.Max(0, transcodeResponse.PlayerStartSeconds)
                        : 0,
                    ResumeAfterLoadSeconds: 0,
                    TimelineOffsetSeconds: Math.Max(0, transcodeResponse.TimelineOffsetSeconds),
                    DurationSeconds: transcodeResponse.DurationSeconds is > 0
                        ? transcodeResponse.DurationSeconds
                        : CurrentMediaDuration,
                    CanSeekAnywhere: transcodeResponse.CanSeekAnywhere,
                    HlsRecipe: recipe);

                ApplyPreparedTransport(prepared);
                PlayMethod = "transcode";

                LogToFile("state_trace.txt", $"Loading HLS via proxy: {PlaybackUrlRedactor.Redact(localUrl)}");
                BeginMpvLoad(prepared, restorePaused: wasPaused);
                SendMediaInfoToOsc();
                switchSucceeded = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogToFile("state_trace.txt", "Quality transcode canceled");
            }
            catch (Exception ex)
            {
                _switchingContent = false;
                _qualitySwitchActive = false;
                LogToFile("player_quality_switch_error.txt", ex.ToString());
                if (!transportReplaced)
                {
                    IsLoading = false;
                    App.MainWindowInstance?.HideLoadingOverlay();
                    if (!wasPaused)
                        _mpv?.Play();
                    var (title, detail) = DescribePlaybackError(ex);
                    ErrorMessage = detail;
                    ShowNotice(title, detail, "error");
                    _mpv?.SendScriptMessage("osc-set-active-quality", _activeQualityTier);
                }
                else
                {
                    _prematureEofRecoveryActive = true;
                    _switchingContent = true;
                    ShowNotice(
                        "Reconnecting playback",
                        "The quality switch was interrupted. Restoring playback from your current position…",
                        "warning");
                    _ = RecoverInterruptedStreamAsync(currentPos, "quality-switch-failed");
                }
            }
        }

        // Don't clear _switchingContent/_qualitySwitchActive here —
        // they're cleared in the FileLoaded handler when the new stream loads.
        // Clearing here races with END_FILE from the old stream being killed.
        if (switchSucceeded)
        {
            _activeQualityTier = tierId;
            _mpv?.SendScriptMessage("osc-set-active-quality", tierId);
        }
    }

    private async Task ShowSubtitleSearchDialogAsync()
    {
        var session = _playbackManager?.CurrentSession;
        var mainWindow = App.MainWindowInstance;
        if (session == null || mainWindow?.Content?.XamlRoot == null)
            return;

        var preferredLanguage = session.SubtitleUrls
            .Select(track => track.Language)
            .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language));
        var changed = false;
        var dialog = new SiloPlayer.Controls.SubtitleSearchDialog(session.MediaFileId, preferredLanguage)
        {
            XamlRoot = mainWindow.Content.XamlRoot,
        };
        dialog.SubtitleDownloaded += () => changed = true;

        _videoWindow?.Hide();
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            if (State is not PlayerState.Idle and not PlayerState.Minimized)
                _videoWindow?.Show();
        }

        if (!changed || _closing || !ReferenceEquals(session, _playbackManager?.CurrentSession))
            return;

        var version = Versions.FirstOrDefault(candidate => candidate.FileId == session.MediaFileId);
        if (version != null)
            await SwitchVersionAsync(version);
    }

    private async Task ShowSubtitleAppearanceDialogAsync()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow?.Content?.XamlRoot == null)
            return;

        var dialog = new SiloPlayer.Controls.SubtitleAppearanceDialog
        {
            XamlRoot = mainWindow.Content.XamlRoot,
        };
        _videoWindow?.Hide();
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            if (State is not PlayerState.Idle and not PlayerState.Minimized)
                _videoWindow?.Show();
        }
    }

    private async Task ShowSubtitleAiDialogAsync()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow == null)
            return;

        _videoWindow?.Hide();
        try
        {
            await mainWindow.ShowSubtitleAiDialogAsync();
        }
        finally
        {
            if (State is not PlayerState.Idle and not PlayerState.Minimized)
                _videoWindow?.Show();
        }
    }

    private async Task ShowMarkerEditDialogAsync()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow == null)
            return;

        _videoWindow?.Hide();
        try
        {
            await mainWindow.ShowMarkerEditDialogAsync();
        }
        finally
        {
            if (State is not PlayerState.Idle and not PlayerState.Minimized)
                _videoWindow?.Show();
        }
    }

    private void SendMarkerEditAvailabilityToOsc()
    {
        var available = AuthorizationPolicy.CanEditMarkers(_authService);
        _mpv?.SendScriptMessage("osc-set-marker-edit-available", available ? "true" : "false");
    }

    private async Task SendSubtitleAiAvailabilityToOscAsync(CancellationToken ct)
    {
        var available = false;
        try
        {
            var status = await _playbackApi.GetSubtitleAiStatusAsync(ct);
            available = status.Enabled || status.TranscribeEnabled;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Subtitle AI capability check failed: {ex.Message}");
        }
        _mpv?.SendScriptMessage("osc-set-subtitle-ai-available", available ? "true" : "false");
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
                    var settingsVm = App.Services.GetService<SiloPlayer.ViewModels.SettingsViewModel>();
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
            await _playbackApi.DownloadSubtitleAsync(fileId, best);
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
        }
        else
        {
            _videoWindow.EnterFullscreen();
            SetState(PlayerState.Fullscreen);
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
        else if (State == PlayerState.PictureInPicture)
            _videoWindow?.EnterPictureInPicture();
        else
            _videoWindow?.MatchParentPosition();
    }

    public void HandleWindowMinimized(bool minimized)
    {
        if (State == PlayerState.Idle) return;
        if (minimized && State != PlayerState.PictureInPicture)
            _videoWindow?.Hide();
        else if (State == PlayerState.Expanded || State == PlayerState.Fullscreen)
            _videoWindow?.Show();
        else if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
        else if (State == PlayerState.PictureInPicture)
            _videoWindow?.EnterPictureInPicture();
    }

    // ── Close / Dispose ──────────────────────────────────────────────────

    private bool _closing;

    public async Task CloseAsync()
    {
        if (_closing) return; // Prevent duplicate close from spammed exit clicks
        _closing = true;

        LogToFile("state_trace.txt", $"CloseAsync called: State={State} _switchingContent={_switchingContent}");
        await CancelAndDrainPlayRequestAsync();
        await CancelAndDrainTransportRestartsAsync();
        CancelPendingFileLoadTimeout();
        StopPlaybackStallWatchdog();
        if (State == PlayerState.Fullscreen)
            ExitAnyFullscreen();

        string? closedContentId = ContentId;
        double closedPosition = CurrentMediaPosition;
        double closedDuration = CurrentMediaDuration;
        var closingManager = _playbackManager;
        var sessionStopTask = closingManager != null
            ? closingManager.StopSessionAsync(
                closedPosition > 0 ? closedPosition : null,
                isPaused: true)
            : Task.CompletedTask;

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
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);

        _mpv?.Stop();
        StopDirectStreamProxy();
        StopHlsProxy();

        if (closingManager != null)
        {
            closingManager.ProgressReportingFailed -= OnProgressReportingFailed;
            if (ReferenceEquals(_playbackManager, closingManager))
                _playbackManager = null;
            _ = FinishClosingSessionAsync(closingManager, sessionStopTask);
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
        _activeTransportPlan = null;
        _timelineOffsetSeconds = 0;
        _transportDurationSeconds = null;
        _canSeekAnywhere = true;
        _activeHlsRecipe = null;
        _preBitmapBurnInPlan = null;
        _preBitmapBurnInQualityTier = null;
        _requestedMediaFileId = null;
        _pendingSubtitleSelection = null;
        _pendingInitialServerSubtitleIndex = null;
        ClearChapterThumbnailOverlayCache();
        StopDirectStreamProxy();
        StopHlsProxy();
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

    private async Task FinishClosingSessionAsync(PlaybackManager manager, Task sessionStopTask)
    {
        try
        {
            await sessionStopTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Stop previous session error: {ex.Message}");
        }
        finally
        {
            manager.Dispose();
        }
    }

    public void Dispose()
    {
        CancelPendingPlayRequest();
        CancelPendingTransportRestarts();
        CancelPendingFileLoadTimeout();
        StopPlaybackStallWatchdog();
        ClearChapterThumbnailOverlayCache();
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);

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
        StopHlsProxy();
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
        DisconnectWebSocket();
        if (_playbackManager?.SessionId == null) return;

        var baseUrl = _apiClient.BaseUrl;
        var sessionId = _playbackManager.SessionId;
        var token = _apiClient.AccessToken;

        var socket = new PlaybackWebSocket(baseUrl, sessionId, token);
        socket.CommandReceived += HandleWebSocketCommand;
        socket.EventReceived += HandleWebSocketEvent;
        _webSocket = socket;
        _ = Task.Run(async () =>
        {
            try
            {
                await socket.ConnectAsync();
                if (!ReferenceEquals(_webSocket, socket))
                    socket.Disconnect();
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_webSocket, socket))
                    LogToFile("state_trace.txt", $"WebSocket error: {ex.Message}");
            }
        });
    }

    private void DisconnectWebSocket()
    {
        if (_webSocket != null)
        {
            _webSocket.CommandReceived -= HandleWebSocketCommand;
            _webSocket.EventReceived -= HandleWebSocketEvent;
            _webSocket.Disconnect();
            _webSocket = null;
        }
    }

    private async Task PersistAudioPreferenceAsync(int trackIndex)
    {
        var key = GetPrefsKey();
        if (string.IsNullOrEmpty(key))
            return;

        try
        {
            var version = Versions.FirstOrDefault(v => v.FileId == _playbackManager?.CurrentSession?.MediaFileId);
            var track = version?.AudioTracks?.ElementAtOrDefault(trackIndex);

            await _catalogApi.SetAudioPrefsAsync(key, new Core.Models.Catalog.AudioPreference
            {
                AudioTrackIndex = trackIndex,
                AudioLanguage = track?.Language,
                TrackSignature = CreateAudioTrackSignature(track),
            });
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"SetAudioPrefs error: {ex.Message}");
        }
    }

    private static Core.Models.Catalog.AudioTrackSignature? CreateAudioTrackSignature(AudioTrackInfo? track)
    {
        if (track == null)
            return null;

        return new Core.Models.Catalog.AudioTrackSignature
        {
            Language = track.Language,
            Title = track.Title,
            EmbeddedTitle = track.EmbeddedTitle,
            Codec = track.Codec,
            Layout = track.Layout,
            Channels = Math.Max(0, track.Channels ?? 0),
            Default = track.Default,
        };
    }

    private void HandleWebSocketEvent(PlaybackRealtimeEvent ev)
    {
        switch (ev.Name)
        {
            case "markers_updated":
                ApplyRealtimeMarkersUpdated(ev.Payload);
                break;
            case "subtitle_translation_started":
                ApplySubtitleTranslationStarted(ev.Payload);
                break;
            case "subtitle_translation_cues":
                ApplySubtitleTranslationCues(ev.Payload);
                break;
            case "subtitle_translation_completed":
                ApplySubtitleTranslationCompleted(ev.Payload);
                break;
            case "subtitle_translation_failed":
                ApplySubtitleTranslationFailed(ev.Payload);
                break;
            case "subtitle_ready":
                if (TryGetPayloadInt(ev.Payload, "file_id", out var readyFileId))
                    _ = RefreshSubtitlesAfterAiAsync(readyFileId);
                break;
        }
    }

    public void PrepareLiveSubtitleTranslation(long jobId, int mediaFileId, string language, string label)
    {
        if (jobId <= 0 || mediaFileId <= 0 || _mpv == null) return;
        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId == jobId) return;
        }
        BeginLiveSubtitleTranslation(jobId, mediaFileId, language, label);
    }

    private void ApplySubtitleTranslationStarted(JsonElement payload)
    {
        if (!TryGetPayloadLong(payload, "job_id", out var jobId) ||
            !TryGetPayloadInt(payload, "file_id", out var fileId)) return;
        var language = TryGetPayloadString(payload, "language") ?? "";
        var label = TryGetPayloadString(payload, "label");
        BeginLiveSubtitleTranslation(jobId, fileId, language,
            string.IsNullOrWhiteSpace(label) ? "translated" : label);
    }

    private void BeginLiveSubtitleTranslation(long jobId, int fileId, string language, string label)
    {
        var session = _playbackManager?.CurrentSession;
        if (_mpv == null || session == null || session.MediaFileId != fileId) return;

        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId == jobId) return;
            ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: true);
            _liveSubtitleJobId = jobId;
            _liveSubtitleFileId = fileId;
            _liveSubtitleCues.Clear();
            _preLiveSubtitleSid = Math.Max(0, (int)Math.Round(_mpv.GetPropertyDouble("sid")));
            _resumeAfterLiveSubtitleBuffer = !_mpv.IsPaused;

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiloPlayer", "live-subtitles");
            Directory.CreateDirectory(folder);
            _liveSubtitlePath = Path.Combine(folder, $"{session.SessionId}-{jobId}.vtt");
            WriteLiveSubtitleFileLocked();

            if (_resumeAfterLiveSubtitleBuffer) _mpv.Pause();
            SendTranslationBufferingState(true, label);

            _liveSubtitleResumeCts = new CancellationTokenSource();
            var timeoutToken = _liveSubtitleResumeCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), timeoutToken).ConfigureAwait(false);
                    ResumeAfterLiveSubtitleBuffer(jobId);
                }
                catch (OperationCanceledException) { }
            });
        }
    }

    private void ApplySubtitleTranslationCues(JsonElement payload)
    {
        if (!TryGetPayloadLong(payload, "job_id", out var jobId) ||
            !payload.TryGetProperty("cues", out var cuesElement) ||
            cuesElement.ValueKind != JsonValueKind.Array) return;

        var receivedCue = false;
        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId != jobId || _mpv == null || string.IsNullOrWhiteSpace(_liveSubtitlePath)) return;
            foreach (var cueElement in cuesElement.EnumerateArray())
            {
                if (!cueElement.TryGetProperty("start", out var startEl) || !startEl.TryGetDouble(out var start) ||
                    !cueElement.TryGetProperty("end", out var endEl) || !endEl.TryGetDouble(out var end) ||
                    !cueElement.TryGetProperty("text", out var textEl)) continue;
                var text = textEl.GetString();
                if (string.IsNullOrWhiteSpace(text) || end <= start) continue;
                _liveSubtitleCues.Add(new LiveSubtitleCue(start, end, text));
                receivedCue = true;
            }
            if (!receivedCue) return;

            _liveSubtitleCues.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            WriteLiveSubtitleFileLocked();
            if (_liveSubtitleSid <= 0)
            {
                _mpv.AddSubtitle(_liveSubtitlePath, "AI translation", null, select: true);
                _liveSubtitleSid = Math.Max(1, (int)Math.Round(_mpv.GetPropertyDouble("sid")));
            }
            else
            {
                _mpv.ReloadSubtitle(_liveSubtitleSid);
                _mpv.SetSubtitleTrack(_liveSubtitleSid);
            }
        }

        ResumeAfterLiveSubtitleBuffer(jobId);
    }

    private void ApplySubtitleTranslationCompleted(JsonElement payload)
    {
        if (!TryGetPayloadLong(payload, "job_id", out var jobId)) return;
        ResumeAfterLiveSubtitleBuffer(jobId);
        var subtitleId = TryGetPayloadInt(payload, "subtitle_id", out var id) ? id : (int?)null;
        var fileId = TryGetPayloadInt(payload, "file_id", out var payloadFileId)
            ? payloadFileId
            : _liveSubtitleFileId;
        _ = RefreshSubtitlesAfterAiAsync(fileId, subtitleId);
    }

    private void ApplySubtitleTranslationFailed(JsonElement payload)
    {
        if (!TryGetPayloadLong(payload, "job_id", out var jobId)) return;
        var message = TryGetPayloadString(payload, "message");
        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId != jobId) return;
            ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: true);
        }
        try
        {
            App.Services.GetService<ToastService>()?.Error(
                string.IsNullOrWhiteSpace(message) ? "Subtitle translation failed" : $"Translation failed: {message}");
        }
        catch { }
    }

    public void FailLiveSubtitleTranslation(long jobId, string? message)
    {
        if (jobId <= 0) return;
        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId != jobId) return;
            ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle: true);
        }
        try
        {
            App.Services.GetService<ToastService>()?.Error(
                string.IsNullOrWhiteSpace(message) ? "Subtitle translation failed" : $"Translation failed: {message}");
        }
        catch { }
    }

    private void ResumeAfterLiveSubtitleBuffer(long jobId)
    {
        lock (_liveSubtitleLock)
        {
            if (_liveSubtitleJobId != jobId) return;
            try { _liveSubtitleResumeCts?.Cancel(); } catch { }
            _liveSubtitleResumeCts?.Dispose();
            _liveSubtitleResumeCts = null;
            SendTranslationBufferingState(false, null);
            if (_resumeAfterLiveSubtitleBuffer && _mpv?.IsPaused == true)
                _mpv.Play();
            _resumeAfterLiveSubtitleBuffer = false;
        }
    }

    private void SendTranslationBufferingState(bool active, string? label)
    {
        if (_mpv == null) return;
        _mpv.SendScriptMessage("osc-set-translation-buffering", active
            ? JsonSerializer.Serialize(new { active = true, label = string.IsNullOrWhiteSpace(label) ? "translated" : label })
            : "false");
    }

    public async Task RefreshSubtitlesAfterAiAsync(int mediaFileId, int? preferredSubtitleId = null)
    {
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        if (manager == null || session == null || session.MediaFileId != mediaFileId) return;
        try
        {
            var response = await _playbackApi.GetSubtitlesAsync(mediaFileId).ConfigureAwait(false);
            if (!ReferenceEquals(_playbackManager, manager) || manager.CurrentSession != session) return;

            var existing = session.SubtitleUrls
                .Where(track => !string.Equals(track.Source, "downloaded", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var baseIndex = existing.Count == 0 ? 0 : existing.Max(track => track.Index) + 1;
            var downloaded = response.Subtitles.Select((entry, offset) => new SubtitleTrackInfo
            {
                Id = entry.Id,
                Index = baseIndex + offset,
                MediaFileId = mediaFileId,
                Language = entry.Language,
                Codec = string.IsNullOrWhiteSpace(entry.Format) ? entry.Codec : entry.Format,
                Label = string.IsNullOrWhiteSpace(entry.ReleaseName)
                    ? (entry.Title ?? entry.Language)
                    : $"{entry.ReleaseName} ({entry.Provider})",
                Source = "downloaded",
                HearingImpaired = entry.HearingImpaired,
                Forced = entry.Forced,
                Url = $"/stream/{session.SessionId}/subtitles/{baseIndex + offset}",
            }).ToList();
            session.SubtitleUrls = [.. existing, .. downloaded];
            SendSubtitleListToOsc();

            var preferred = preferredSubtitleId.HasValue
                ? downloaded.FirstOrDefault(track => track.Id == preferredSubtitleId)
                : null;
            if (preferred != null && _mpv != null)
            {
                SelectSubtitleTrack(preferred);
                var completedJobId = _liveSubtitleJobId;
                if (completedJobId > 0) ResumeAfterLiveSubtitleBuffer(completedJobId);
                lock (_liveSubtitleLock)
                {
                    if (_liveSubtitleSid > 0)
                    {
                        try { _mpv.RemoveSubtitle(_liveSubtitleSid); } catch { }
                        _liveSubtitleSid = 0;
                    }
                    DeleteLiveSubtitleFileLocked();
                }
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"RefreshSubtitlesAfterAiAsync failed: {ex.Message}");
        }
    }

    private void WriteLiveSubtitleFileLocked()
    {
        if (string.IsNullOrWhiteSpace(_liveSubtitlePath)) return;
        var builder = new StringBuilder("WEBVTT\n\n");
        for (var index = 0; index < _liveSubtitleCues.Count; index++)
        {
            var cue = _liveSubtitleCues[index];
            builder.Append(index + 1).Append('\n')
                .Append(FormatVttTime(cue.Start)).Append(" --> ").Append(FormatVttTime(cue.End)).Append('\n')
                .Append(cue.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
                .Append("\n\n");
        }
        File.WriteAllText(_liveSubtitlePath, builder.ToString(), new UTF8Encoding(false));
    }

    private static string FormatVttTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    private void ClearLiveSubtitleTranslation(bool restorePreviousSubtitle)
    {
        lock (_liveSubtitleLock)
            ClearLiveSubtitleTranslationLocked(restorePreviousSubtitle);
    }

    private void ClearLiveSubtitleTranslationLocked(bool restorePreviousSubtitle)
    {
        try { _liveSubtitleResumeCts?.Cancel(); } catch { }
        _liveSubtitleResumeCts?.Dispose();
        _liveSubtitleResumeCts = null;
        SendTranslationBufferingState(false, null);
        if (_mpv != null)
        {
            if (_liveSubtitleSid > 0)
            {
                try { _mpv.RemoveSubtitle(_liveSubtitleSid); } catch { }
            }
            if (restorePreviousSubtitle)
            {
                try { _mpv.SetSubtitleTrack(_preLiveSubtitleSid); } catch { }
            }
            if (_resumeAfterLiveSubtitleBuffer && _mpv.IsPaused)
            {
                try { _mpv.Play(); } catch { }
            }
        }
        DeleteLiveSubtitleFileLocked();
        _liveSubtitleCues.Clear();
        _liveSubtitleJobId = 0;
        _liveSubtitleFileId = 0;
        _liveSubtitleSid = 0;
        _preLiveSubtitleSid = 0;
        _resumeAfterLiveSubtitleBuffer = false;
    }

    private void DeleteLiveSubtitleFileLocked()
    {
        if (!string.IsNullOrWhiteSpace(_liveSubtitlePath))
        {
            try { File.Delete(_liveSubtitlePath); } catch { }
        }
        _liveSubtitlePath = null;
    }

    private static bool TryGetPayloadInt(JsonElement payload, string name, out int value)
    {
        value = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var element) && element.TryGetInt32(out value);
    }

    private static bool TryGetPayloadLong(JsonElement payload, string name, out long value)
    {
        value = 0;
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(name, out var element) && element.TryGetInt64(out value);
    }

    private static string? TryGetPayloadString(JsonElement payload, string name)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private sealed record LiveSubtitleCue(double Start, double End, string Text);

    private void ApplyRealtimeMarkersUpdated(JsonElement payload)
    {
        try
        {
            if (payload.ValueKind != JsonValueKind.Object) return;
            if (!payload.TryGetProperty("file_id", out var fileIdEl) || !fileIdEl.TryGetInt32(out var fileId))
                return;

            var version = Versions.FirstOrDefault(v => v.FileId == fileId);
            if (version == null) return;

            if (payload.TryGetProperty("intro", out var introEl))
                version.Intro = ReadMarkerRange(introEl);
            if (payload.TryGetProperty("credits", out var creditsEl))
                version.Credits = ReadMarkerRange(creditsEl);
            if (payload.TryGetProperty("recap", out var recapEl))
                version.Recap = ReadMarkerRange(recapEl);
            if (payload.TryGetProperty("preview", out var previewEl))
                version.Preview = ReadMarkerRange(previewEl);

            var activeFileId = _playbackManager?.CurrentSession?.MediaFileId ?? 0;
            if (activeFileId == fileId && _playbackManager?.WatchDetail != null)
            {
                _playbackManager.WatchDetail.Intro = version.Intro;
                _playbackManager.WatchDetail.Credits = version.Credits;
                _playbackManager.WatchDetail.Recap = version.Recap;
                _playbackManager.WatchDetail.Preview = version.Preview;
                MarkersChanged?.Invoke();
            }

            LogToFile("state_trace.txt", $"Realtime markers_updated applied: fileId={fileId} intro={FormatMarker(version.Intro)} recap={FormatMarker(version.Recap)} credits={FormatMarker(version.Credits)} preview={FormatMarker(version.Preview)}");
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"markers_updated handling failed: {ex.Message}");
        }
    }

    private static TimeRange? ReadMarkerRange(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (!element.TryGetProperty("start", out var startEl) || !startEl.TryGetDouble(out var start))
            return null;
        if (!element.TryGetProperty("end", out var endEl) || !endEl.TryGetDouble(out var end))
            return null;
        return end > start ? new TimeRange { Start = start, End = end } : null;
    }

    private static string FormatMarker(TimeRange? marker)
        => marker == null ? "none" : $"{marker.Start:F1}-{marker.End:F1}";

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
                SeekFastTo(pos.Value);
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
            "slo" or "slk" or "sk" => "Slovak",
            "slv" or "sl" => "Slovenian",
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
            "tam" or "ta" => "Tamil",
            "tel" or "te" => "Telugu",
            "ben" or "bn" => "Bengali",
            "per" or "fas" or "fa" => "Persian",
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
