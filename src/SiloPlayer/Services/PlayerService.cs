// src/SiloPlayer/Services/PlayerService.cs
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Player;
using SiloPlayer.Services.PlaybackMenus;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Networking.Connectivity;

namespace SiloPlayer.Services;

public enum PlayerState { Idle, Expanded, Fullscreen, Minimized, PictureInPicture }

public sealed record AudiobookChapterInfo(
    int Index,
    string Title,
    double StartSeconds,
    double EndSeconds,
    int FileId);

public partial class PlayerService : IDisposable
{
    private const string AutoSkipIntroSettingKey = "playback.auto_skip_intro";
    private const string AutoSkipCreditsSettingKey = "playback.auto_skip_credits";
    private const string AutoSkipRecapSettingKey = "playback.auto_skip_recap";
    private readonly PlaybackAuthorityScope _localAuthority;
    private PlaybackAuthorityScope _activeAuthority;
    private readonly SemaphoreSlim _authoritySwitchGate = new(1, 1);
    private CancellationTokenSource _authorityLifetimeCts = new();
    private PlaybackApi _playbackApi => _activeAuthority.PlaybackApi;
    private CatalogApi _catalogApi => _activeAuthority.CatalogApi;
    private AuthService _authService => _activeAuthority.Auth;
    private SiloApiClient _apiClient => _activeAuthority.ApiClient;
    private readonly SettingsService _settingsService;
    private SettingsApi _settingsApi => _activeAuthority.SettingsApi;
    public SettingsApi PlaybackSettingsApi => _settingsApi;
    public bool HasRemoteAuthority => !ReferenceEquals(_activeAuthority, _localAuthority);
    public PlaybackAuthorityScope ActiveAuthority => _activeAuthority;
    public event Action<PlaybackAuthorityScope>? RemoteAuthorityEnded;
    private readonly WatchDetailPrefetchCache<WatchDetailResponse> _watchDetailPrefetches =
        new(capacity: 8, lifetime: TimeSpan.FromMinutes(2));
    // Advances on every progress/watched mutation; each /watch response is
    // stamped with the revision current when its server read began, so a
    // snapshot a page still holds is not trusted after a mutation.
    private readonly UserStateRevision _userStateRevision;
    private readonly WatchDetailFreshness<WatchDetailResponse> _watchDetailFreshness;
    private readonly SemaphoreSlim _subtitleAppearanceLoadGate = new(1, 1);
    private readonly SemaphoreSlim _playbackDialogGate = new(1, 1);
    private readonly AutoSkipMarkerIdentity _autoSkipMarkerIdentity = new();

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
    private int? _pendingInitialSubtitleFileId;
    private int? _subtitleMenuFileId;
    private volatile bool _subtitleMenuPending;
    private volatile bool _subtitleVersionSwitchActive;
    private SubtitleAutoSelect.Options? _pendingAutomaticSubtitleSelection;
    private int _activeSubtitleServerIndex = -1;
    private SubtitleTrackInfo? _executedSubtitleTrack;
    private int? _executedSubtitleFileId;
    private int? _pendingSubtitlePreferenceIndex;
    private sealed record SubtitlePreferenceOwner(PlaybackAuthorityScope Authority, ApiRequestContext Context,
        PlaybackManager? Manager, int? MediaFileId, string? Key, CancellationToken Token)
    {
        public PlaybackManager.SubtitleSelectionIntent? PreviousIntent { get; init; }
        public bool FollowsVersionSwitch { get; init; }
    }
    private SubtitlePreferenceOwner? _pendingSubtitlePreferenceOwner;
    private volatile bool _qualitySwitchActive;
    private bool _playingNextShown;
    private bool _postRollActive;
    private bool _postRollVideoEnded;
    private bool _restoreFullscreenAfterPostRollContinue;
    private readonly EpisodeNavigationState _episodeNavigation = new();
    // Premature-EOF loop-breaker. mpv keep-open=yes pauses at EOF; our handler
    // restarts the stream to punch through transient CDN/server drops. Track
    // last attempt time and streak length so rapid duplicate EOFs do not spawn
    // overlapping recovery attempts.
    private volatile bool _prematureEofRecoveryActive;
    private double _prematureEofRecoveryPosition;
    private long _prematureEofLastAttemptMs;
    private int _prematureEofStreak;
    private readonly PlaybackStallDetector _stallDetector =
        DirectPlaybackRecoveryPolicy.CreateStallDetector();
    private Timer? _stallWatchdogTimer;
    private int _stallWatchdogChecking;
    private long _stallRecoveryLastAttemptMs;
    private bool _displayRequestActive;
    private string _activeQualityTier = "original";
    private HlsProxy? _hlsProxy;
    private DirectStreamProxy? _directStreamProxy;
    private PlaybackTransportPlan? _activeTransportPlan;
    private double _timelineOffsetSeconds;
    private double? _transportDurationSeconds;
    private double _audiobookPartOffsetSeconds;
    private double _audiobookTotalDurationSeconds;
    private long _lastAudiobookProgressReportTicks;
    private DateTimeOffset? _audiobookPausedAt;
    private Timer? _audiobookSleepTimer;
    private bool _canSeekAnywhere = true;
    private int? _requestedMediaFileId;

    public string ActiveQualityTier => _playbackManager?.CurrentSession?.ProtocolVersion >= 3
        ? _playbackManager.CurrentSession.ActiveQuality : _activeQualityTier;
    public string SelectedQualityIntent => _playbackManager?.CurrentSession?.ProtocolVersion >= 3
        ? _playbackManager.QualityPreference : PlaybackQualityPreferences.Normalize(_activeQualityTier);
    public string SelectedQualityMenuId => _playbackManager?.CurrentSession?.ProtocolVersion >= 3
        ? _playbackManager.SelectedQualityMenuId : SelectedQualityIntent;
    public bool IsQualitySwitchActive => _qualitySwitchActive;
    public IReadOnlyList<PlaybackQualityV3> AvailableQualities =>
        PlaybackQualityPreferences.MenuOptions(_playbackManager?.CurrentSession?.AvailableQualities ?? []);
    public int? ActiveMediaFileId => _playbackManager?.CurrentSession?.MediaFileId;
    public int? RequestedMediaFileId => _requestedMediaFileId ?? ActiveMediaFileId;

    public Task SelectQualityAsync(string qualityId) => SwitchQualityTierAsync(qualityId);
    private readonly SemaphoreSlim _transportRestartGate = new(1, 1);
    private readonly SemaphoreSlim _streamRecoveryGate = new(1, 1);
    private readonly SemaphoreSlim _planInvalidationGate = new(1, 1);
    private CancellationTokenSource? _seekRestartCts;
    private long _seekRestartGeneration;
    private readonly SemaphoreSlim _playRequestGate = new(1, 1);
    private CancellationTokenSource? _playRequestCts;
    private CancellationTokenSource? _scopedViewerPlayCts;
    private object? _viewerPlayReservation;
    private string? _retiredPlayContentId;
    private long _playRequestGeneration;
    private CancellationTokenSource? _fileLoadTimeoutCts;
    private long _fileLoadGeneration;
    // Actual file ownership survives watchdog cancellation at FILE_LOADED.
    private long _playbackFileGeneration;
    private CancellationTokenSource? _loadedMediaInitCts;
    private long _loadedMediaInitGeneration;
    private int _consecutiveFileLoadTimeouts;
    private bool? _restorePausedAfterLoad;
    private readonly ProgressiveRemuxIdleResume _idleRemuxResume = new();
    private PlaybackWebSocket? _webSocket;
    private sealed record PlaybackTerminalState(
        double Position,
        string Trigger,
        bool WasPaused,
        bool CanRetry,
        PlaybackStartResponse? RestartSession);
    private PlaybackTerminalState? _playbackTerminalState;
    private bool? _recoveryPauseIntentOverride;
    private CancellationTokenSource? _playbackCts;
    // Native output belongs to the last submitted load, not a successor that
    // is still preparing its watch detail/session under a new playback CTS.
    private object? _mpvLoadingOverlayOwner;
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
    private Action? _mpvPlaybackRestartedHandler;
    private Action? _mpvEofReachedHandler;
    private Action? _mpvPlaybackEndedHandler;
    private Action<string>? _mpvPlaybackErrorHandler;
    private Action<string>? _mpvDirectStreamErrorHandler;
    private Action<string>? _mpvErrorHandler;

    public PlayerService(
        PlaybackApi playbackApi,
        CatalogApi catalogApi,
        AuthService authService,
        SiloApiClient apiClient,
        SettingsService settingsService,
        SettingsApi settingsApi)
    {
        _localAuthority = new PlaybackAuthorityScope(apiClient, authService, playbackApi, catalogApi, settingsApi);
        _activeAuthority = _localAuthority;
        _settingsService = settingsService;
        _userStateRevision = DetailCacheInvalidation.AttachUserStateRevision();
        _watchDetailFreshness = new(_userStateRevision, finalSaveBarrier: TimeSpan.FromSeconds(5),
            contentOf: detail => detail.ContentId, seriesOf: detail => detail.SeriesId);
        ObserveSubtitlePreferences();
        InitializeConnectionObservation();

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
    /// AppSettings. Callers invoke this on every slider step, key repeat and
    /// OSC wheel notch, so the synchronous settings write is coalesced to one
    /// write after input settles; close and shutdown flush immediately.
    /// </summary>
    public void SaveVolumeState()
    {
        Interlocked.Increment(ref _volumeSaveRevision);
        ScheduleVolumeSave();
    }

    private void ScheduleVolumeSave()
    {
        if (_volumePersistenceDisposed) { SaveVolumeStateNow(); return; }
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher == null) { SaveVolumeStateNow(); return; }
        if (!dispatcher.HasThreadAccess)
        {
            if (!dispatcher.TryEnqueue(ScheduleVolumeSave)) SaveVolumeStateNow();
            return;
        }
        if (_volumeSaveTimer == null)
        {
            _volumeSaveTimer = dispatcher.CreateTimer();
            _volumeSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _volumeSaveTimer.IsRepeating = false;
            _volumeSaveTimer.Tick += (_, _) => SaveVolumeStateNow();
        }
        _volumeSaveTimer.Stop();
        _volumeSaveTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _volumeSaveTimer;
    private long _volumeSaveRevision;
    private long _savedVolumeRevision;
    private volatile bool _volumePersistenceDisposed;
    private bool HasPendingVolumeSave => Volatile.Read(ref _volumeSaveRevision) != Volatile.Read(ref _savedVolumeRevision);

    private void SaveVolumeStateNow()
    {
        try { _volumeSaveTimer?.Stop(); } catch { /* Shutdown may run off the dispatcher. */ }
        try
        {
            var revision = Volatile.Read(ref _volumeSaveRevision);
            var settings = _settingsService.Load();
            settings.PlayerVolume = Volume;
            settings.PlayerMuted = IsMuted;
            _settingsService.Save(settings);
            Volatile.Write(ref _savedVolumeRevision, revision);
        }
        catch { /* non-fatal — volume persistence is best-effort */ }
    }

    private void FlushVolumeStateOnClose()
    {
        // A deferred explicit value (including zero/unmuted) is authoritative.
        // Native getters return default zero when the engine is unavailable.
        if (HasPendingVolumeSave)
        {
            SaveVolumeStateNow();
            return;
        }
        try
        {
            if (_mpv == null) return;
            var volume = _mpv.GetPropertyDouble("volume");
            var muted = _mpv.GetMute();
            if (volume > 0 || muted)
            {
                Volume = Math.Clamp(volume, 0, 100);
                IsMuted = muted;
                SaveVolumeStateNow();
            }
        }
        catch { /* best-effort during teardown */ }
    }

    /// <summary>
    /// Initializes the hidden native mpv host while the user is browsing so a
    /// cold libmpv startup is not added to the first playback request. This is
    /// intentionally synchronous and must be invoked on the window dispatcher.
    /// A failed warm-up is non-fatal; PlayAsync will retry normal initialization.
    /// </summary>
    public void Prewarm()
    {
        if (_mpv != null)
            return;

        try
        {
            EnsureMpvInitialized();
            LogToFile("state_trace.txt", "Native player prewarm complete");
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Native player prewarm failed (will retry on play): {ex.Message}");
            ResetFailedMpvInitialization();
        }
    }

    /// <summary>
    /// Starts the watch-detail request while the user is deliberately hovering
    /// a playable card. PlayAsync consumes the same in-flight task, removing a
    /// serial network round trip from the common Home/Continue Watching path.
    /// The cache is small, short-lived, and profile-scoped so it cannot leak
    /// playback preferences between profiles or grow during long browsing.
    /// </summary>
    public void PrefetchWatchDetail(string? contentId, int? libraryId = null, int? fileId = null)
        => PrefetchWatchDetailCore(contentId, _localAuthority, libraryId, fileId);

    private void PrefetchWatchDetailCore(string? contentId, PlaybackAuthorityScope authority, int? libraryId = null, int? fileId = null,
        ApiRequestContext? expectedContext = null)
    {
        if (string.IsNullOrWhiteSpace(contentId) || _closing)
            return;

        var profileId = authority.Auth.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
            return;

        var cacheKey = WatchPreparationKey(contentId, libraryId, fileId);
        var task = _watchDetailPrefetches.GetOrAdd(
            cacheKey,
            WatchPrefetchAuthorityKey(authority, profileId),
            () => FetchWatchDetailAfterFinalSaveAsync(authority, contentId, libraryId, fileId, CancellationToken.None, expectedContext),
            DateTimeOffset.UtcNow,
            out var added);
        if (added)
            _ = ObserveWatchDetailPrefetchAsync(cacheKey, task);
    }

    /// <summary>
    /// Returns the same profile-scoped watch-detail task used by hover
    /// prefetching, or starts the request when no prefetch exists. Detail pages
    /// and the playback pipeline use this shared entry point so opening an item
    /// and immediately pressing Play never issue duplicate /watch requests.
    /// </summary>
    public async Task<WatchDetailResponse> GetOrFetchWatchDetailAsync(
        string contentId,
        CancellationToken ct = default,
        int? libraryId = null,
        int? fileId = null)
        => await GetOrFetchWatchDetailCoreAsync(contentId, consumePrefetch: false, ct: ct, libraryId: libraryId, fileId: fileId, authority: _localAuthority)
            .ConfigureAwait(false);

    // Every /watch result is revalidated before it becomes playback input: a
    // cached, in-flight or fresh response whose item changed after its read
    // began is evicted and read again.
    private Task<WatchDetailResponse> GetOrFetchWatchDetailCoreAsync(
        string contentId,
        bool consumePrefetch,
        CancellationToken ct,
        int? libraryId = null,
        int? fileId = null,
        PlaybackAuthorityScope? authority = null,
        ApiRequestContext? expectedContext = null)
        => _watchDetailFreshness.GetCurrentAsync(
            token => GetOrFetchWatchDetailOnceAsync(contentId, consumePrefetch, token, libraryId, fileId, authority, expectedContext),
            _ => _watchDetailPrefetches.Invalidate(WatchPreparationKey(contentId, libraryId, fileId)),
            ct);

    private async Task<WatchDetailResponse> GetOrFetchWatchDetailOnceAsync(
        string contentId,
        bool consumePrefetch,
        CancellationToken ct,
        int? libraryId = null,
        int? fileId = null,
        PlaybackAuthorityScope? authority = null,
        ApiRequestContext? expectedContext = null)
    {
        authority ??= _activeAuthority;
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        var context = expectedContext ?? authority.ApiClient.CaptureContext();
        EnsurePreparationAuthority();

        // Starting through the cache also coalesces callers that arrive before
        // a hover prefetch has fired (for example, Item Detail and Play racing
        // immediately after navigation). Scoped starts retain their captured
        // read owner through both the shared request and the transient retry.
        PrefetchWatchDetailCore(contentId, authority, libraryId, fileId, expectedContext);
        var prefetched = await TryGetPrefetchedWatchDetailAsync(contentId, consumePrefetch, ct, authority, libraryId, fileId)
            .ConfigureAwait(false);
        EnsurePreparationAuthority();
        if (prefetched != null)
            return prefetched;

        try
        {
            var detail = await FetchWatchDetailAfterFinalSaveAsync(authority, contentId, libraryId, fileId, ct, expectedContext).ConfigureAwait(false);
            EnsurePreparationAuthority();
            return detail;
        }
        catch (ApiException ex) when (ex.StatusCode == 400)
        {
            // A just-retired session can briefly hold server-side state used by
            // watch preparation. Match the playback startup retry without
            // imposing this delay on successful requests.
            await Task.Delay(300, ct).ConfigureAwait(false);
            EnsurePreparationAuthority();
            var detail = await FetchWatchDetailAfterFinalSaveAsync(authority, contentId, libraryId, fileId, ct, expectedContext).ConfigureAwait(false);
            EnsurePreparationAuthority();
            return detail;
        }

        void EnsurePreparationAuthority()
        {
            ct.ThrowIfCancellationRequested();
            if (!authority.ApiClient.IsCurrentContext(context))
                throw new OperationCanceledException("Watch preparation authority changed.", ct);
        }
    }

    /// <summary>
    /// Reads /watch after any pending final progress write (bounded), so a
    /// replay right after closing does not resume from the pre-close position,
    /// and stamps the response with the user-state revision of the read.
    /// </summary>
    private Task<WatchDetailResponse> FetchWatchDetailAfterFinalSaveAsync(
        PlaybackAuthorityScope authority, string contentId, int? libraryId, int? fileId, CancellationToken ct,
        ApiRequestContext? expectedContext = null)
        => _watchDetailFreshness.ReadAsync(contentId,
            token => authority.PlaybackApi.GetWatchDetailAsync(contentId, libraryId, fileId, token, expectedContext), ct);

    /// <summary>True when no progress/watched mutation happened since this response's server read.</summary>
    private bool IsCurrentWatchDetail(WatchDetailResponse detail) => _watchDetailFreshness.IsCurrent(detail);

    private async Task ObserveWatchDetailPrefetchAsync(
        string contentId,
        Task<WatchDetailResponse> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            _watchDetailPrefetches.Remove(contentId, task);
        }
    }

    private async Task<WatchDetailResponse?> TryGetPrefetchedWatchDetailAsync(
        string contentId,
        bool consume,
        CancellationToken ct,
        PlaybackAuthorityScope authority,
        int? libraryId = null,
        int? fileId = null)
    {
        var profileId = authority.Auth.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
            return null;

        try
        {
            var context = authority.ApiClient.CaptureContext();
            var authorityKey = WatchPrefetchAuthorityKey(authority, profileId);
            var cacheKey = WatchPreparationKey(contentId, libraryId, fileId);
            var watchDetail = consume
                ? await _watchDetailPrefetches.TryTakeAsync(
                    cacheKey, authorityKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false)
                : await _watchDetailPrefetches.TryGetAsync(
                    cacheKey, authorityKey, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            return authority.ApiClient.IsCurrentContext(context) && watchDetail != null &&
                string.Equals(watchDetail.ContentId, contentId, StringComparison.Ordinal)
                ? watchDetail
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string WatchPrefetchAuthorityKey(PlaybackAuthorityScope authority, string profileId)
    {
        var context = authority.ApiClient.CaptureContext();
        return string.Join('\n', authority.ScopeId, context.BaseUrl, context.AuthenticationGeneration,
            context.RequestContextGeneration, authority.ApiClient.LocalizedReads.Generation, profileId);
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

    public bool IsWatchTogetherPlayback { get; private set; }
    public event Action<string, double, bool>? RoomTransportRequested;
    private string? _offeredRoomQuality;
    private string? _offeredFromQuality;
    private MpvPlayer? _roomInputPlayer;
    private bool _roomPreviousDefaultBindings;
    private string? _lastRoomSeekDisplay;
    private MpvPlayer? _roomSeekDisplayPlayer;

    public void SetWatchTogetherPlayback(bool active)
    {
        var changed = active != IsWatchTogetherPlayback;
        var joining = active && !IsWatchTogetherPlayback;
        if (changed)
        {
            Interlocked.Increment(ref _roomPlaybackGeneration);
            CancelPendingSoloVersionSwitch();
        }
        IsWatchTogetherPlayback = active;
        if (joining) SetPaused(true);
        if (!active) _offeredRoomQuality = _offeredFromQuality = null;
        UpdateRoomInputBindings();
        UpdateRoomSeekDisplay();
        if (!active) RestoreVideoPlaybackRate();
        else PublishVideoPlaybackRateStatus();
        if (changed)
        {
            RetireNativeMenus();
            PublishNativeMenuScopes();
        }
    }

    private void UpdateRoomInputBindings()
    {
        if (_mpv == null) return;
        if (IsWatchTogetherPlayback)
        {
            if (!ReferenceEquals(_roomInputPlayer, _mpv))
            {
                _roomInputPlayer = _mpv;
                _roomPreviousDefaultBindings = _mpv.GetPropertyFlag("input-default-bindings");
            }
            // Built-in mpv seek/pause shortcuts bypass our permission-aware
            // script callbacks. Explicit OSC forced bindings remain enabled.
            _mpv.SetProperty("input-default-bindings", "no");
        }
        else if (ReferenceEquals(_roomInputPlayer, _mpv))
        {
            _mpv.SetProperty("input-default-bindings", _roomPreviousDefaultBindings ? "yes" : "no");
            _roomInputPlayer = null;
        }
    }

    /// <summary>Only viewer input enters this boundary. Server commands use SetPaused/SeekRoomTarget.</summary>
    public void RequestUserPauseToggle()
    {
        var paused = !IsPaused;
        if (IsWatchTogetherPlayback)
            RoomTransportRequested?.Invoke(paused ? "pause" : "play", CurrentMediaPosition, paused);
        else SetPaused(paused);
    }

    public void RequestUserSeek(double seconds, bool forceResume = false)
    {
        if (!double.IsFinite(seconds)) return;
        if (IsWatchTogetherPlayback)
            RoomTransportRequested?.Invoke("seek", seconds, IsPaused);
        else SeekFastTo(seconds, forceResume);
    }

    /// <summary>The newest accepted room seek is the origin of a burst of relative skips.
    /// Native Position remains the actual media time used for progress and room reports.</summary>
    public Func<double?>? RoomSeekPosition { private get; set; }
    public Func<double?>? RoomPlaybackStartPosition { private get; set; }
    private bool _roomPlaybackPaused = true;
    private bool _transportHasFirstOutput;
    private double? _transportStartPosition;
    private double PlaybackRecoveryPosition => !_transportHasFirstOutput && _transportStartPosition is { } start
        ? start : CurrentMediaPosition;
    public double UserSeekPosition => IsWatchTogetherPlayback ? RoomSeekPosition?.Invoke() ?? Position : Position;

    public void UpdateRoomSeekDisplay()
    {
        var target = IsWatchTogetherPlayback ? RoomSeekPosition?.Invoke() : null;
        var value = target?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        if (ReferenceEquals(_roomSeekDisplayPlayer, _mpv) && value == _lastRoomSeekDisplay) return;
        _roomSeekDisplayPlayer = _mpv;
        _lastRoomSeekDisplay = value;
        _mpv?.SendScriptMessage("osc-room-seek-position", value);
    }

    public void RequestUserRelativeSeek(double seconds, bool forceResume = false)
    {
        if (!double.IsFinite(seconds)) return;
        if (IsWatchTogetherPlayback) RoomTransportRequested?.Invoke("seek_relative", seconds, IsPaused);
        else RequestUserSeek(Math.Clamp(CurrentMediaPosition + seconds, 0, Math.Max(Duration, CurrentMediaPosition)), forceResume);
    }

    public bool IsNativeSeeking => _mpv?.GetPropertyFlag("seeking") == true;
    public double NativePlaybackRate
    {
        get => _mpv?.GetPropertyDouble("speed") ?? 1;
        set => _mpv?.SetProperty("speed", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool CanSeekRoomTargetLocally(double target)
    {
        var native = PlaybackTimeline.ToPlayerTime(ToSessionPosition(target), _timelineOffsetSeconds);
        return native >= 0 && (_activeTransportPlan?.TransportKind == PlaybackTransportKind.DirectProgressive ||
            (_activeTransportPlan?.IsHls == true && _canSeekAnywhere) ||
            _mpv?.IsPositionCached(native) == true);
    }

    public bool IsRoomTargetBuffered(double target) => _mpv?.IsPositionCached(
        PlaybackTimeline.ToPlayerTime(ToSessionPosition(target), _timelineOffsetSeconds)) == true;

    public void SeekRoomTarget(double target)
    {
        // libmpv can accurately decode/discard up to a buffered keyframe-relative
        // target while paused. Never play muted at an artificial 4x browser rate.
        if (CanSeekRoomTargetLocally(target))
            _mpv?.Seek(PlaybackTimeline.ToPlayerTime(ToSessionPosition(target), _timelineOffsetSeconds));
        else SeekTo(target);
    }

    public void ShowWatchPartyNotice(string message) => ShowNotice("Watch Party", message, "info");

    public void OfferWatchPartyLowerQuality()
    {
        var next = _playbackManager?.GetLowerQualityOption(ActiveVersion?.Bitrate);
        if (next == null) return;
        _offeredRoomQuality = next.Label;
        _offeredFromQuality = ActiveQualityTier;
        _mpv?.SendScriptMessage("osc-room-quality-offer", next.Label);
        NativePartyStateChanged?.Invoke();
        ShowWatchPartyNotice("Playback has stalled repeatedly. Lower quality is available in the Watch Party panel.");
    }

    public async Task AcceptWatchPartyLowerQualityAsync()
    {
        var target = _offeredRoomQuality;
        _offeredRoomQuality = null;
        _mpv?.SendScriptMessage("osc-room-quality-offer", "");
        NativePartyStateChanged?.Invoke();
        if (!IsWatchTogetherPlayback || target == null || _offeredFromQuality != ActiveQualityTier) return;
        await SelectQualityAsync(target);
    }

    /// <summary>Pause / resume the local mpv player.</summary>
    public void SetPaused(bool paused)
    {
        if (IsWatchTogetherPlayback)
        {
            _roomPlaybackPaused = paused;
            if (IsLoading || _switchingContent)
            {
                // A room play may initiate a reanchor. Carry its authority
                // into FileLoaded without releasing the outgoing stream.
                _restorePausedAfterLoad = paused;
                if (paused) _mpv?.Pause();
                return;
            }
        }
        if (_mpv == null) return;
        if (!paused && !_closing && State != PlayerState.Idle) SetConnectionMediaActive(true);
        if (_idleRemuxResume.IsReopening)
        {
            _idleRemuxResume.SetPauseIntent(paused);
            _restorePausedAfterLoad = paused;
            if (paused) _mpv.Pause();
            return;
        }
        if (!paused && TryReopenIdleRemux()) return;
        if (_idleRemuxResume.IsReopening)
        {
            // A native callback may have won the resume race above.
            _idleRemuxResume.SetPauseIntent(paused);
            _restorePausedAfterLoad = paused;
            if (paused) _mpv.Pause();
            return;
        }
        _idleRemuxResume.ObservePause(paused, _switchingContent || IsLoading, DateTimeOffset.UtcNow);
        if (paused) _mpv.Pause();
        else _mpv.Play();
        SynchronizeConnectionMediaActivity(paused);
    }

    private bool TryReopenIdleRemux()
    {
        if (_mpv == null || _activeTransportPlan == null || _playbackManager?.CurrentSession == null
            || _closing || _switchingContent || IsLoading || State == PlayerState.Idle
            || !_idleRemuxResume.TryBeginResume(_activeTransportPlan.TransportKind, _canSeekAnywhere,
                IsWatchTogetherPlayback, DateTimeOffset.UtcNow)) return false;
        var position = CurrentMediaPosition;
        LogToFile("state_trace.txt", $"Idle progressive remux resume: reanchor mediaPos={position:F3}");
        // Freeze the old forward-only response before its stale buffered tail
        // can drain and libavformat reconnects at the old plan's origin.
        _mpv.Pause();
        QueueTransportRestartForSeek(position, forceResume: true);
        return true;
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
        // A remux/HLS seek restart sets _switchingContent while it prepares
        // the new transport. Continue accepting seeks during that one kind of
        // switch so the cancellation generation below can make the latest
        // scrub position win. Real content/version/audio switches stay gated.
        var seekRestartInProgress = Volatile.Read(ref _seekRestartCts) != null;
        if (mpv == null || plan == null ||
            (_switchingContent && !seekRestartInProgress) ||
            _closing)
            return;

        var duration = CurrentMediaDuration;
        var mediaPosition = Math.Max(0, duration > 0
            ? Math.Min(positionSeconds, duration)
            : positionSeconds);
        if (IsAudiobook)
        {
            var targetPart = FindAudiobookPart(mediaPosition);
            var activeFileId = _playbackManager?.CurrentSession?.MediaFileId;
            if (targetPart.HasValue && targetPart.Value.Version.FileId != activeFileId &&
                !string.IsNullOrWhiteSpace(ContentId))
            {
                _ = PlayAsync(
                    ContentId,
                    fileId: targetPart.Value.Version.FileId,
                    startPositionOverride: mediaPosition,
                    libraryId: ActiveLibraryId, authority: _activeAuthority, cancellationToken: _authorityLifetimeCts.Token);
                return;
            }
        }

        var localMediaPosition = ToSessionPosition(mediaPosition);
        var transportPosition = PlaybackTimeline.ToPlayerTime(localMediaPosition, _timelineOffsetSeconds);
        var targetPrecedesTransportWindow = transportPosition < 0;
        var canSeekLocally = plan.TransportKind == PlaybackTransportKind.DirectProgressive ||
            (plan.IsHls && _canSeekAnywhere && !targetPrecedesTransportWindow);

        if (canSeekLocally)
        {
            if (fast)
                mpv.SeekFast(transportPosition);
            else
                mpv.Seek(transportPosition);
            if (forceResume)
                mpv.Play();
            UpdatePlaybackManagerPosition(mediaPosition, forceResume ? false : IsPaused);
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
        var loadingOwner = _playbackCts;
        var gateEntered = false;
        var loadStarted = false;
        var reconcileSubtitles = false;
        PlaybackManager? manager = null;
        PlaybackStartResponse? survivingSession = null;
        try
        {
            await _transportRestartGate.WaitAsync(ct).ConfigureAwait(false);
            gateEntered = true;
            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _seekRestartGeneration))
                return;

            manager = _playbackManager
                ?? throw new InvalidOperationException("The playback session is no longer active.");
            var session = manager.CurrentSession
                ?? throw new InvalidOperationException("The playback session is no longer active.");
            survivingSession = session;
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("The active media version is no longer available.");
            _ = _activeTransportPlan
                ?? throw new InvalidOperationException("The active playback transport is unknown.");
            var mpv = _mpv
                ?? throw new InvalidOperationException("The player is no longer active.");
            var wasPaused = CaptureUserPausedState();

            SetSwitchingContent(true);
            IsLoading = true;
            if (loadingOwner != null)
                App.MainWindowInstance?.ShowLoadingOverlay(loadingOwner);
            var seekPaused = forceResume ? false : wasPaused;
            manager.UpdatePosition(ToSessionPosition(mediaPosition), seekPaused);
            // Progress persistence is not part of the transport dependency
            // chain. Waiting on this request before restarting a remux/HLS
            // stream adds network latency (and up to the full timeout) to
            // every out-of-window seek. Mirror local seeks: report in the
            // background while preparing the replacement transport now.
            _ = ReportSeekProgressAsync(mediaPosition, seekPaused);

            session = await manager.ReplanSeekAsync(mediaPosition, ct).ConfigureAwait(false);
            version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? version;
            var streamUrl = manager.StreamUrl
                ?? throw new InvalidOperationException("The replanned playback stream is unavailable.");
            var prepared = await PreparePlaybackTransportAsync(
                session,
                version,
                streamUrl,
                mediaPosition,
                ct).ConfigureAwait(false);
            PlayMethod = session.PlayMethod;

            ct.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _seekRestartGeneration))
                return;

            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused: _idleRemuxResume.IsReopening
                ? _idleRemuxResume.DesiredPaused : wasPaused && !forceResume);
            loadStarted = true;
            LogToFile("state_trace.txt", $"Seek restart: mediaPos={mediaPosition:F3} transport={prepared.Plan.TransportKind}");
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer seek or playback teardown.
        }
        catch (Exception ex) when (
            (ex is PlaybackPlanTerminalException terminal && PlaybackFailureDescription.IsAccountLimitReason(terminal.Reason)) ||
            (ex is ApiException api && PlaybackFailureDescription.IsAccountLimitReason(api.ErrorCode)))
        {
            if (generation == Volatile.Read(ref _seekRestartGeneration))
            {
                LogToFile("player_seek_error.txt", ex.ToString());
                if (_idleRemuxResume.IsReopening)
                {
                    // The idle response is no longer usable, unlike a refused
                    // seek on a healthy stream. Keep the explicit retry surface.
                    EnterPlaybackTerminalState(ex, mediaPosition, "idle-remux-resume", _idleRemuxResume.DesiredPaused);
                }
                else
                {
                    // A refused replan leaves the current transport and its
                    // pause state intact. Retrying the seek must not report
                    // that healthy plan as a playback failure.
                    SetSwitchingContent(false);
                    IsLoading = false;
                    if (loadingOwner != null)
                        App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
                    var (title, detail) = DescribePlaybackError(ex);
                    ErrorMessage = detail;
                    ShowNotice(title, detail, "error");
                }
            }
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _seekRestartGeneration))
            {
                SetSwitchingContent(false);
                IsLoading = false;
                LogToFile("player_seek_error.txt", ex.ToString());
                if (loadingOwner != null)
                    App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
                    _mpv?.ShowOsdText("Seek failed", 2500));
                if (_idleRemuxResume.IsReopening)
                {
                    _recoveryPauseIntentOverride = _idleRemuxResume.DesiredPaused;
                    _ = RecoverInterruptedStreamAsync(mediaPosition, "idle-remux-resume");
                }
                else reconcileSubtitles = !loadStarted;
            }
        }
        finally
        {
            if (gateEntered)
                _transportRestartGate.Release();
            if (ReferenceEquals(Interlocked.CompareExchange(ref _seekRestartCts, null, ownerCts), ownerCts))
                ownerCts.Dispose();
        }
        // Burn-in reconciliation can re-enter the restart gate and drain the
        // seek owner. Release both before consuming a choice with no load.
        if (reconcileSubtitles && !ct.IsCancellationRequested &&
            generation == Volatile.Read(ref _seekRestartGeneration) && manager != null)
            await ReconcileDeferredSubtitleSelectionAsync(manager, survivingSession);
    }

    private async Task ReportSeekProgressAsync(double mediaPosition, bool paused)
    {
        var manager = _playbackManager;
        if (manager?.SessionId == null)
            return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await manager.ReportProgressNowAsync(ToSessionPosition(mediaPosition), paused, cts.Token)
                .ConfigureAwait(false);
            if (IsAudiobook)
                await ReportAudiobookProgressAsync(mediaPosition, force: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Seek progress report failed: {ex.Message}");
        }
    }

    private void CancelPendingTransportRestarts()
    {
        _idleRemuxResume.Reset();
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
        RetireNativeMenus();
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
        if (HasRemoteAuthority) return;
        ApplySubtitleAppearance(appearance, _localAuthority, _localAuthority.ApiClient.CaptureContext());
    }

    public void ApplySubtitleAppearance(Core.Models.Settings.SubtitleAppearance appearance,
        PlaybackAuthorityScope authority, ApiRequestContext context)
    {
        if (!ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(context)) return;
        _subtitleAppearance = appearance.Clone();
        if (_mpv != null) PushSubtitleAppearanceToMpv(_subtitleAppearance);
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
            var textAlpha = (int)Math.Round(Math.Clamp(a.TextOpacity, 1, 100) * 2.55);
            _mpv.SetProperty("sub-color", $"#{textAlpha:X2}{NormalizeHex(a.FontColor).TrimStart('#')}");

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
        => await TrySetSubtitleTrackAndPersistAsync(mpvTrackIndex, language, track);

    private async Task<bool> TrySetSubtitleTrackAndPersistAsync(int mpvTrackIndex, string? language, SubtitleTrackInfo? track)
    {
        if (_mpv == null || _closing) return false;
        // An open menu still belongs to the outgoing file until its inventory
        // is republished. Never interpret those indices in the replacement file.
        if (mpvTrackIndex > 0 && !IsSubtitleMenuCurrent())
        {
            ShowSubtitleSelectionUnavailable();
            return false;
        }
        if (mpvTrackIndex <= 0) track = null;
        if (track != null && _playbackManager?.CurrentSession?.SubtitleUrls.Contains(track) != true)
        {
            ShowSubtitleSelectionUnavailable();
            return false;
        }
        if (track?.Unplayable == true || track != null &&
            _playbackManager?.CurrentSession?.SubtitleUrls.Any(t => t.Index == track.Index && t.Unplayable) == true) return false;
        if (mpvTrackIndex > 0 && (track == null ||
            !_switchingContent && !IsLoading && !HasSubtitleSelectionRoute(track)))
        {
            ShowSubtitleSelectionUnavailable();
            return false;
        }
        CancelSubtitleSelectionRead();
        var authority = _activeAuthority;
        var manager = _playbackManager;
        var ownerToken = _authorityLifetimeCts.Token;
        var mpv = _mpv;
        var selectionLoadGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
        var selectionFileId = manager?.CurrentSession?.MediaFileId;
        var selectedSubtitleIndex = mpvTrackIndex <= 0 || track == null ? -1 : track.Index;
        var preferenceOwner = new SubtitlePreferenceOwner(authority, authority.ApiClient.CaptureContext(), manager, selectionFileId, GetPrefsKey(), ownerToken);
        var deferLocalSelection = _switchingContent || IsLoading || _subtitleVersionSwitchActive;
        lock (_subtitleTrackStateLock)
        {
            preferenceOwner = preferenceOwner with
            {
                PreviousIntent = _pendingSubtitlePreferenceOwner?.PreviousIntent ?? manager?.CaptureLocalSubtitleIntent(),
                FollowsVersionSwitch = selectedSubtitleIndex < 0 && _subtitleVersionSwitchActive,
            };
            _pendingAutomaticSubtitleSelection = null;
            _pendingSubtitlePreferenceIndex = selectedSubtitleIndex;
            _pendingSubtitlePreferenceOwner = preferenceOwner;
            _pendingInitialSubtitleFileId = selectionFileId;

        }
        bool CurrentReadChoice() => !_closing && !ownerToken.IsCancellationRequested &&
            ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner) && ReferenceEquals(authority, _activeAuthority) &&
            authority.ApiClient.IsCurrentContext(preferenceOwner.Context) && ReferenceEquals(manager, _playbackManager) && ReferenceEquals(mpv, _mpv);
        void RetireReadChoice()
        {
            lock (_subtitleTrackStateLock)
            {
                if (!ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner)) return;
                _pendingSubtitlePreferenceOwner = null; _pendingSubtitlePreferenceIndex = null;
            }
        }
        // Pending text is a draft until readable. It cannot replace the applied
        // manager intent when a busy/error/profile-retired read never executes.
        if (track != null && !IsUnsupportedBitmapSubtitle(track) && !deferLocalSelection)
        {
            while (true)
            {
                var readGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
                var readOutcome = await PrefetchSubtitleSidecarAsync(track);
                if (!CurrentReadChoice()) { RetireReadChoice(); return false; }
                if (readGeneration != Volatile.Read(ref _loadedMediaInitGeneration))
                {
                    while (CurrentReadChoice() && (_switchingContent || IsLoading || Volatile.Read(ref _loadedMediaInitCts) != null))
                        await Task.Delay(50);
                    if (!CurrentReadChoice()) { RetireReadChoice(); return false; }
                    var currentSession = manager?.CurrentSession;
                    if (currentSession?.MediaFileId != selectionFileId) { RetireReadChoice(); return false; }
                    var requested = track;
                    track = currentSession.SubtitleUrls.FirstOrDefault(candidate => !candidate.Unplayable &&
                        (!string.IsNullOrWhiteSpace(requested.TrackId) ? candidate.TrackId == requested.TrackId :
                            candidate.Index == requested.Index && candidate.Source == requested.Source && candidate.Codec == requested.Codec));
                    if (track == null || !HasSubtitleSelectionRoute(track)) { RetireReadChoice(); return false; }
                    selectedSubtitleIndex = track.Index;
                    selectionLoadGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
                    continue;
                }
                if (readOutcome is SubtitlePrefetchOutcome.Busy or SubtitlePrefetchOutcome.Error)
                {
                    RetireReadChoice(); ShowSubtitleReadFailure(readOutcome); return false;
                }
                if (readOutcome == SubtitlePrefetchOutcome.Retired) { RetireReadChoice(); return false; }
                break;
            }
        }
        if (!CurrentReadChoice()) { RetireReadChoice(); return false; }
        lock (_subtitleTrackStateLock)
        {
            _activeSubtitleServerIndex = selectedSubtitleIndex;
            if (manager?.CurrentSession?.ProtocolVersion >= 3)
            {
                manager.RecordLocalSubtitleSelection(selectedSubtitleIndex < 0 ? null : selectedSubtitleIndex);
                // PlaybackManager retains identity and rebinds it to each accepted
                // inventory. A raw pending index must not outrank that resolution.
                _pendingInitialServerSubtitleIndex = null;
            }
            else if (deferLocalSelection || _pendingInitialServerSubtitleIndex.HasValue)
                _pendingInitialServerSubtitleIndex = selectedSubtitleIndex;
        }
        var wasPaused = CaptureUserPausedState();
        var position = CurrentMediaPosition;
        var transportReloadScheduled = false;
        var selectionExecuted = false;

        if (track == null && deferLocalSelection &&
            (manager?.CurrentSession?.IsSubtitleBurnedIn != true || _subtitleVersionSwitchActive))
        {
            // Off has no outgoing-file identity. A version replacement (or an
            // in-flight burn-in request) consumes the latest recorded intent.
            // An already accepted same-file burn-in plan must instead replan
            // now: clearing sid after its pending load cannot remove its pixels.
            transportReloadScheduled = true;
        }
        else if (track != null && IsUnsupportedBitmapSubtitle(track))
        {
            if (IsBurnedInSubtitle(manager?.CurrentSession, track))
                selectionExecuted = true;
            else
            {
                var result = await SetBitmapSubtitleBurnInAsync(track);
                if (result == SubtitleTransportResult.Superseded) return false;
                transportReloadScheduled = result == SubtitleTransportResult.ReloadScheduled;
            }
        }
        else if (track != null)
        {
            // A text/PGS track cannot be layered over a stream that is
            // already burning a different bitmap track into the video. Replan
            // to the requested track and select it locally only
            // after the replacement file has loaded. Selecting it before
            // loadfile settles lets mpv discard the new sid and can briefly
            // resume stale frames from the outgoing transport.
            if (manager?.CurrentSession?.IsSubtitleBurnedIn == true)
            {
                var result = await SetBitmapSubtitleBurnInAsync(null, postLoadSubtitleIndex: track.Index);
                if (result == SubtitleTransportResult.Superseded) return false;
                transportReloadScheduled = result == SubtitleTransportResult.ReloadScheduled;
            }
            else if (deferLocalSelection)
            {
                // The latest manual choice wins over an older pending choice,
                // but only FILE_LOADED may select or restore the new file.
                transportReloadScheduled = true;
            }
            else
            {
                var selected = _activeSubtitleServerIndex;
                var sidecarGeneration = CurrentSubtitleSidecarGeneration();
                var loadedMediaGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
                var readOutcome = await PrefetchSubtitleSidecarAsync(track);
                // A newer selection owns the subtitle state and its preference.
                if (!ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner) ||
                    _activeSubtitleServerIndex != selected || !ReferenceEquals(manager, _playbackManager) ||
                    !ReferenceEquals(mpv, _mpv) || _closing || ownerToken.IsCancellationRequested ||
                    !ReferenceEquals(authority, _activeAuthority)) return false;
                if (CurrentSubtitleSidecarGeneration() != sidecarGeneration ||
                    Volatile.Read(ref _loadedMediaInitGeneration) != loadedMediaGeneration)
                {
                    // A file load replaced the subtitle tracks during the download. It
                    // applies the recorded selection and restores playback itself;
                    // its preference is saved only after that execution.
                    transportReloadScheduled = true;
                }
                else
                {
                    if (readOutcome == SubtitlePrefetchOutcome.Retired) return false;
                    if (readOutcome is SubtitlePrefetchOutcome.Busy or SubtitlePrefetchOutcome.Error)
                    {
                        RestoreExecutedSubtitleSelection(reapply: false);
                        ShowSubtitleReadFailure(readOutcome);
                        return false;
                    }
                    // The viewer may have paused or sought during the download.
                    wasPaused = CaptureUserPausedState();
                    position = CurrentMediaPosition;
                    selectionExecuted = SelectSubtitleTrackForOwner(track,
                        () => ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner));
                }
            }
        }
        else if (manager?.CurrentSession?.IsSubtitleBurnedIn == true)
        {
            var result = await SetBitmapSubtitleBurnInAsync(null);
            if (result == SubtitleTransportResult.Superseded) return false;
            transportReloadScheduled = result == SubtitleTransportResult.ReloadScheduled;
        }
        else
        {
            if (deferLocalSelection) transportReloadScheduled = true;
            else
            {
                lock (_subtitleTrackStateLock)
                {
                    if (!ReferenceEquals(_pendingSubtitlePreferenceOwner, preferenceOwner)) return false;
                    mpv.SetSubtitleTrack(0);
                    selectionExecuted = mpv.GetPropertyDouble("sid") <= 0;
                }
            }
        }

        // BeginMpvLoad deliberately pauses the outgoing stream and FileLoaded
        // restores the captured user state. Never race that authority by
        // issuing Play/Pause while a replacement transport is still loading.
        if (_closing || ownerToken.IsCancellationRequested || !ReferenceEquals(authority, _activeAuthority) ||
            !ReferenceEquals(manager, _playbackManager) ||
            (manager?.CurrentSession?.MediaFileId != selectionFileId && !preferenceOwner.FollowsVersionSwitch) ||
            _activeSubtitleServerIndex != selectedSubtitleIndex) return false;
        if (transportReloadScheduled || _switchingContent || IsLoading ||
            selectionLoadGeneration != Volatile.Read(ref _loadedMediaInitGeneration))
        {
            // The load worker consumes the recorded intent. It may already
            // have finished while prefetch was awaiting: do not refill its slot.
            return true;
        }
        lock (_subtitleTrackStateLock)
        {
            if (!ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner)) return false;
            if (!selectionExecuted)
            {
                RestoreExecutedSubtitleSelection(reapply: false);
                ShowSubtitleSelectionUnavailable();
                return false;
            }
            CommitExecutedSubtitleSelection(track);
            _pendingSubtitlePreferenceIndex = null;
            _pendingSubtitlePreferenceOwner = null;
        }
        RestorePlaybackStateAfterSubtitleChange(wasPaused, position, allowSeek: true);
        await SaveExecutedSubtitlePreferenceAsync(preferenceOwner, track);
        return true;
    }

    private async Task SaveExecutedSubtitlePreferenceAsync(SubtitlePreferenceOwner? owner, SubtitleTrackInfo? track)
    {
        if (owner == null || string.IsNullOrEmpty(owner.Key) || owner.Token.IsCancellationRequested ||
            !ReferenceEquals(owner.Authority, _activeAuthority) || !ReferenceEquals(owner.Manager, _playbackManager) ||
            owner.MediaFileId != _playbackManager?.CurrentSession?.MediaFileId ||
            !owner.Authority.ApiClient.IsCurrentContext(owner.Context)) return;
        try
        {
            // Mirror derivePersistedSubtitleMode: index null ("Off") → "off";
            // any explicit track → "always". "auto" is never written here —
            // it's the default when no pref exists.
            bool off = track == null;
            var req = new SubtitlePreferenceRequest
            {
                SubtitleLanguage = (off ? "" : track?.Language) ?? "",
                SubtitleTrackIndex = track?.Index ?? -1,
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
            await owner.Authority.PlaybackApi.SaveSubtitlePrefsAsync(owner.Context, owner.Key, req, owner.Token);
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"SaveSubtitlePrefs error: {ex.Message}"); }
    }

    private void RestorePlaybackStateAfterSubtitleChange(bool wasPaused, double position, bool allowSeek)
    {
        if (_mpv == null || _closing || State == PlayerState.Idle) return;
        try
        {
            if (allowSeek && position > 0)
            {
                var current = CurrentMediaPosition;
                if (Math.Abs(current - position) > 2.5)
                {
                    var plan = _activeTransportPlan;
                    var canCorrectLocally =
                        plan?.TransportKind == PlaybackTransportKind.DirectProgressive ||
                        (plan?.IsHls == true && _canSeekAnywhere);
                    if (canCorrectLocally)
                    {
                        var sessionPosition = ToSessionPosition(position);
                        var playerPosition = PlaybackTimeline.ToPlayerTime(
                            sessionPosition,
                            _timelineOffsetSeconds);
                        if (playerPosition < 0)
                        {
                            QueueTransportRestartForSeek(position, forceResume: !wasPaused);
                            return;
                        }
                        _mpv.SeekFast(playerPosition);
                    }
                    else
                    {
                        // Sequential remux and incomplete copy-HLS timelines
                        // cannot seek in-place. Re-anchor only when subtitle
                        // attachment actually displaced playback, preserving
                        // the user's prior playing/paused state.
                        QueueTransportRestartForSeek(position, forceResume: !wasPaused);
                        return;
                    }
                }
            }

            if (wasPaused)
                _mpv.Pause();
            else
                _mpv.Play();

            IsPaused = wasPaused;
            UpdateDisplayWakeLock(!wasPaused && !_closing && State != PlayerState.Idle);
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Restore subtitle playback state failed: {ex.Message}");
        }
    }

    /// <summary>
    /// mpv can report a paused property while its network cache is starving.
    /// Preserve only an intentional user pause across transport/dialog work;
    /// otherwise a quality, audio, or subtitle action can freeze a stream that
    /// was playing immediately before the operation.
    /// </summary>
    private bool CaptureUserPausedState()
        => (_mpv?.IsPaused ?? IsPaused) && _mpv?.IsBufferingForCache != true;

    // ── State ────────────────────────────────────────────────────────────

    public PlayerState State { get; private set; } = PlayerState.Idle;

    public string? ContentId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Subtitle { get; private set; }
    public double Position { get; private set; }
    public double Duration { get; private set; }
    public bool IsPaused { get; private set; } = true;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            lock (_playbackBusyLock)
            {
                ++_playbackBusyRevision;
                _isLoading = value;
            }
        }
    }
    public string? ErrorMessage { get; private set; }
    public string PlayMethod { get; private set; } = "";
    public string Resolution { get; private set; } = "";
    public double Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
    public bool IsAudiobook { get; private set; }
    public string? AudiobookPosterUrl { get; private set; }
    public string? AudiobookAuthor { get; private set; }
    public string? AudiobookNarrator { get; private set; }

    public MpvPlayer? Mpv => _mpv;
    public PlaybackManager? Manager => _playbackManager;
    public WatchDetailResponse? WatchDetail => _playbackManager?.WatchDetail;
    public List<FileVersion> Versions { get; private set; } = [];
    public FileVersion? ActiveVersion => Versions.FirstOrDefault(v => v.FileId == (_playbackManager?.CurrentSession?.MediaFileId ?? 0));
    public IReadOnlyList<AudiobookChapterInfo> AudiobookChapters => BuildAudiobookChapters();
    public TimeRange? ActiveIntro => ActiveMarker("intro");
    public TimeRange? ActiveCredits => ActiveMarker("credits");
    public TimeRange? ActiveRecap => ActiveMarker("recap");
    public TimeRange? ActivePreview => ActiveMarker("preview");
    private TimeRange? ActiveMarker(string kind) => CurrentMarkerVersion?.MarkerSegments is { } segments
        ? PlaybackMarkerRanges.Active(segments, kind, Position, MarkerProvenanceFor(kind)) : MarkerTimingFor(kind);

    // ── Events ───────────────────────────────────────────────────────────

    public event Action<PlayerState>? StateChanged;
    public event Action? AudiobookPresentationChanged;
    public event Action<double>? AudiobookPlaybackRateChanged;
    public event Action? AudiobookSleepChanged;

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
        UpdateRoomInputBindings();

        if (room == null || string.Equals(room.Phase, "ended", StringComparison.OrdinalIgnoreCase))
        {
            _mpv.SendScriptMessage("osc-native-party-scope", "");
            _mpv.SendScriptMessage("osc-set-watch-party", "null");
            return;
        }

        _mpv.SendScriptMessage("osc-native-party-scope", _nativeMenuOwner is { } nativeOwner && nativeOwner.IsCurrent() ? nativeOwner.Token : "");
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
            catching_up = room.SelfIgnoreWait || room.Members.Any(m => m.IsSelf && m.IsBuffering),
        };
        _mpv.SendScriptMessage("osc-set-watch-party", JsonSerializer.Serialize(payload));
    }

    /// <summary>Relays mpv's <c>paused-for-cache</c> state. Raised on the
    /// event-pump thread; overlay consumers must dispatch to UI. The overlay
    /// applies a 500ms debounce before showing any spinner so quick buffer
    /// recoveries don't flash chrome on screen.</summary>
    public event Action<bool>? BufferingChanged;

    public bool IsBufferingForCache => _mpv?.IsBufferingForCache ?? false;
    public bool IsPlaybackSurfaceForeground => _videoWindow?.IsForeground == true;
    public event Action<double>? PositionChanged;
    public event Action<double>? DurationChanged;
    public event Action<bool>? PauseChanged;
    public event Action? PlaybackEnded;
    /// <summary>Outstanding final writes from closed and autoplay-retired sessions.</summary>
    public Task PendingProgressSave { get; private set; } = Task.CompletedTask;
    public event Action? ContentLoaded; // fired when file is loaded and decoding starts
    public event Action? MarkersChanged;
    public event Action? ChaptersChanged;
    /// <summary>
    /// Fired instead of <see cref="PlaybackEnded"/> when the current episode
    /// finishes AND a next episode is available (NextEpisode* fields are set).
    /// The PlayerOverlay uses this to show its "Up next" screen. If the user
    /// cancels, call <see cref="CancelPlayingNext"/>; if they accept, call
    /// <see cref="ContinuePlayingNextAsync"/>.
    /// </summary>
    public event Action<bool>? ShowPlayingNextRequested;
    public event Action? PostRollReturnRequested;

    public bool IsPostRollActive => _postRollActive;
    public bool IsPostRollVideoEnded => _postRollVideoEnded;
    public bool IsSwitchingContent => _switchingContent;

    // ── Next-episode metadata (set by ItemDetailPage before playback) ────

    private EpisodeNavigationSnapshot EpisodeNavigation => _episodeNavigation.Snapshot;
    public string? NextEpisodeContentId => IsShufflePlayback ? ShuffleNext?.ContentId : EpisodeNavigation.Next?.ContentId;
    public string? NextEpisodeTitle => IsShufflePlayback ? ShuffleNext?.Title : EpisodeNavigation.Next?.Title;
    public string? NextEpisodeSeriesTitle => IsShufflePlayback ? (ShuffleNext?.Type == "episode" ? ShuffleNext.SeriesTitle : ShuffleNext?.Title) : EpisodeNavigation.Next?.SeriesTitle;
    public string? NextEpisodePosterUrl => IsShufflePlayback ? (ShuffleNext?.Type == "episode" ? ShuffleNext.PosterUrl ?? ShuffleNext.BackdropUrl : ShuffleNext?.BackdropUrl) : EpisodeNavigation.Next?.PosterUrl;
    public string? NextEpisodeOverview => IsShufflePlayback ? ShuffleNext?.Overview : EpisodeNavigation.Next?.Overview;
    public string? NextEpisodeAirDate => IsShufflePlayback ? ShuffleNext?.ReleaseDate : EpisodeNavigation.Next?.AirDate;
    public int NextEpisodeRuntime => IsShufflePlayback ? Math.Clamp(ShuffleNext?.Runtime ?? 0, 0, int.MaxValue / 60) * 60 : EpisodeNavigation.Next?.RuntimeSeconds ?? 0;
    public bool? NextEpisodePlayed => IsShufflePlayback ? ShuffleNext?.UserState?.Played : EpisodeNavigation.Next?.Played;
    public bool? NextEpisodeIsInProgress => IsShufflePlayback ? ShuffleNext?.UserState?.IsInProgress : EpisodeNavigation.Next?.IsInProgress;
    public double? NextEpisodePositionSeconds => IsShufflePlayback ? ShuffleNext?.PositionSeconds : EpisodeNavigation.Next?.PositionSeconds;
    public bool? NextEpisodePosterIsEpisodeStill => IsShufflePlayback ? ShuffleNext?.PosterIsEpisodeStill : EpisodeNavigation.Next?.PosterIsEpisodeStill;
    public string? PreviousEpisodeContentId => EpisodeNavigation.PreviousContentId;

    public bool HasEpisodeNavigationForCurrentPlayback =>
        !IsShufflePlayback && IsCurrentPlaybackSeriesEpisode() &&
        _episodeNavigation.IsOwnedBy(ContentId);

    public bool HasNextEpisodeForCurrentPlayback =>
        IsShufflePlayback ? ShuffleNext != null : IsCurrentPlaybackSeriesEpisode() && _episodeNavigation.HasNextFor(ContentId);

    private bool IsCurrentPlaybackSeriesEpisode()
    {
        var detail = WatchDetail;
        return detail is not null &&
               string.Equals(detail.Type, "episode", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(detail.SeriesId) &&
               string.Equals(detail.ContentId, ContentId, StringComparison.Ordinal);
    }

    public void SetNextEpisodeHint(
        string ownerContentId,
        string nextContentId,
        string? title,
        string? seriesTitle,
        string? posterUrl,
        string? overview,
        string? airDate = null,
        int runtimeSeconds = 0,
        int? seasonNumber = null,
        int? episodeNumber = null,
        bool? played = null,
        bool? isInProgress = null,
        double? positionSeconds = null,
        bool? posterIsEpisodeStill = null)
    {
        _episodeNavigation.SetHint(
            ownerContentId,
            new EpisodeNavigationTarget(
                nextContentId,
                title,
                seriesTitle,
                posterUrl,
                overview,
                airDate,
                runtimeSeconds,
                seasonNumber,
                episodeNumber,
                played, isInProgress, positionSeconds, posterIsEpisodeStill));
    }

    // ── State transitions ────────────────────────────────────────────────

    public void SetAudiobookPresentation(MediaItemDetail item)
    {
        if (!item.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase))
            return;

        AudiobookPosterUrl = item.PosterUrl;
        AudiobookAuthor = JoinPeople(item.Audiobook?.Authors)
            ?? JoinCrew(item.Crew, "author");
        AudiobookNarrator = JoinPeople(item.Audiobook?.Narrators)
            ?? JoinCrew(item.Crew, "narrator");
        InvokeSubscribersSafely(AudiobookPresentationChanged, nameof(AudiobookPresentationChanged));
    }

    private async Task LoadAudiobookPresentationAsync(string contentId, CancellationToken ct)
    {
        try
        {
            var item = await _catalogApi.GetItemDetailAsync(contentId, ActiveLibraryId, ct).ConfigureAwait(false);
            if (!ct.IsCancellationRequested && IsAudiobook &&
                string.Equals(ContentId, contentId, StringComparison.Ordinal))
            {
                SetAudiobookPresentation(item);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Audiobook presentation load failed: {ex.Message}");
        }
    }

    private static string? JoinPeople(IEnumerable<Core.Models.Home.AudiobookPerson>? people)
    {
        var names = people?
            .Select(person => person.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return names?.Count > 0 ? string.Join(", ", names) : null;
    }

    private static string? JoinCrew(IEnumerable<CrewMember> crew, string job)
    {
        var names = crew
            .Where(member => member.Job.Equals(job, StringComparison.OrdinalIgnoreCase))
            .Select(member => member.Name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    private IReadOnlyList<(FileVersion Version, double Start, double End)> BuildAudiobookParts()
    {
        var result = new List<(FileVersion Version, double Start, double End)>();
        var cursor = 0d;
        foreach (var version in Versions)
        {
            var duration = Math.Max(0, version.Duration);
            result.Add((version, cursor, cursor + duration));
            cursor += duration;
        }
        return result;
    }

    private (FileVersion Version, double Start, double End)? FindAudiobookPart(double absolutePosition, int? preferredFileId = null)
    {
        var parts = BuildAudiobookParts();
        if (parts.Count == 0) return null;
        if (preferredFileId.HasValue)
        {
            var preferred = parts.FirstOrDefault(part => part.Version.FileId == preferredFileId.Value);
            if (preferred.Version != null) return preferred;
        }

        var position = Math.Max(0, absolutePosition);
        return parts.FirstOrDefault(part => part.End > part.Start && position < part.End)
            is var match && match.Version != null
                ? match
                : parts[^1];
    }

    // Chapter rows are read once a second by the live audiobook surfaces and
    // twice per CurrentAudiobookChapter. Rebuild only when the parts change.
    private (List<FileVersion> Versions, IReadOnlyList<AudiobookChapterInfo> Chapters)? _audiobookChapterCache;

    private IReadOnlyList<AudiobookChapterInfo> BuildAudiobookChapters()
    {
        if (!IsAudiobook) return [];
        var versions = Versions;
        if (_audiobookChapterCache is { } cached && ReferenceEquals(cached.Versions, versions)) return cached.Chapters;
        var result = new List<AudiobookChapterInfo>();
        foreach (var part in BuildAudiobookParts())
        {
            if (part.Version.Chapters is { Count: > 0 })
            {
                foreach (var chapter in part.Version.Chapters.OrderBy(chapter => chapter.StartSeconds))
                {
                    var absoluteStart = part.Start + Math.Max(0, chapter.StartSeconds);
                    var absoluteEnd = part.Start + Math.Max(chapter.StartSeconds, chapter.EndSeconds);
                    result.Add(new AudiobookChapterInfo(
                        result.Count,
                        string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {result.Count + 1}" : chapter.Title,
                        absoluteStart,
                        absoluteEnd > absoluteStart ? absoluteEnd : absoluteStart + 1,
                        part.Version.FileId));
                }
            }
        }
        _audiobookChapterCache = (versions, result);
        return result;
    }

    public AudiobookChapterInfo? CurrentAudiobookChapter =>
        AudiobookChapters.LastOrDefault(chapter => Position >= chapter.StartSeconds)
        ?? AudiobookChapters.FirstOrDefault();

    public void SeekToPreviousAudiobookChapter()
    {
        var chapters = AudiobookChapters;
        if (chapters.Count == 0) return;
        var currentIndex = -1;
        for (var index = chapters.Count - 1; index >= 0; index--)
        {
            if (Position >= chapters[index].StartSeconds)
            {
                currentIndex = index;
                break;
            }
        }
        if (currentIndex < 0) currentIndex = 0;
        var targetIndex = Position - chapters[currentIndex].StartSeconds > 3
            ? currentIndex
            : Math.Max(0, currentIndex - 1);
        SeekTo(chapters[targetIndex].StartSeconds);
    }

    public void SeekToNextAudiobookChapter()
    {
        var next = AudiobookChapters.FirstOrDefault(chapter => chapter.StartSeconds > Position + 0.5);
        if (next != null) SeekTo(next.StartSeconds);
    }

    public void ToggleAudiobookPlayback()
    {
        if (_mpv == null) return;
        if (!_mpv.IsPaused)
        {
            _mpv.Pause();
            return;
        }

        var settings = _settingsService.Load();
        if (settings.AudiobookSmartRewind && _audiobookPausedAt.HasValue)
        {
            var pausedFor = DateTimeOffset.UtcNow - _audiobookPausedAt.Value;
            var rewind = pausedFor.TotalSeconds switch
            {
                < 10 => 0,
                < 60 => 3,
                < 600 => 10,
                < 3600 => 20,
                _ => 30,
            };
            if (rewind > 0)
                SeekTo(Math.Max(0, Position - rewind));
        }
        _audiobookPausedAt = null;
        _mpv.Play();
    }

    public DateTimeOffset? AudiobookSleepDeadline { get; private set; }
    public double? AudiobookSleepAtPosition { get; private set; }

    public double AudiobookPlaybackRate => SettingsService.ClampAudiobookPlaybackRate(
        _mpv?.GetPropertyDouble("speed") ?? _settingsService.GetAudiobookPlaybackRate(ContentId));

    public double SetAudiobookPlaybackRate(double rate)
    {
        var clamped = IsAudiobook
            ? _settingsService.RememberAudiobookPlaybackRate(ContentId, rate)
            : SettingsService.ClampAudiobookPlaybackRate(rate);
        _mpv?.SetProperty("speed", clamped.ToString(System.Globalization.CultureInfo.InvariantCulture));
        InvokeSubscribersSafely(AudiobookPlaybackRateChanged, clamped, nameof(AudiobookPlaybackRateChanged));
        return clamped;
    }

    public TimeSpan? GetAudiobookSleepRemaining()
    {
        if (AudiobookSleepDeadline.HasValue)
            return TimeSpan.FromSeconds(Math.Max(0, (AudiobookSleepDeadline.Value - DateTimeOffset.UtcNow).TotalSeconds));
        if (AudiobookSleepAtPosition.HasValue)
        {
            var mediaSeconds = Math.Max(0, AudiobookSleepAtPosition.Value - CurrentMediaPosition);
            return TimeSpan.FromSeconds(mediaSeconds / Math.Max(SettingsService.AudiobookRateMinimum, AudiobookPlaybackRate));
        }
        return null;
    }

    public void SetAudiobookSleepTimer(TimeSpan? duration, double? atPosition)
    {
        AudiobookSleepDeadline = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null;
        AudiobookSleepAtPosition = atPosition;
        _audiobookSleepTimer?.Dispose();
        _audiobookSleepTimer = new Timer(_ =>
        {
            var elapsed = AudiobookSleepDeadline.HasValue && DateTimeOffset.UtcNow >= AudiobookSleepDeadline.Value;
            var reached = AudiobookSleepAtPosition.HasValue && CurrentMediaPosition >= AudiobookSleepAtPosition.Value;
            if (!elapsed && !reached) return;
            _mpv?.Pause();
            ClearAudiobookSleepTimer();
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        InvokeSubscribersSafely(AudiobookSleepChanged, nameof(AudiobookSleepChanged));
    }

    public void ClearAudiobookSleepTimer()
    {
        AudiobookSleepDeadline = null;
        AudiobookSleepAtPosition = null;
        Interlocked.Exchange(ref _audiobookSleepTimer, null)?.Dispose();
        InvokeSubscribersSafely(AudiobookSleepChanged, nameof(AudiobookSleepChanged));
    }

    private double ToSessionPosition(double absolutePosition) =>
        IsAudiobook ? Math.Max(0, absolutePosition - _audiobookPartOffsetSeconds) : absolutePosition;

    private void UpdatePlaybackManagerPosition(double absolutePosition, bool paused) =>
        _playbackManager?.UpdatePosition(ToSessionPosition(absolutePosition), paused);

    private async Task ReportAudiobookProgressAsync(double position, bool force = false)
    {
        if (!IsAudiobook || string.IsNullOrWhiteSpace(ContentId) || _audiobookTotalDurationSeconds <= 0)
            return;
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref _lastAudiobookProgressReportTicks);
        if (!force && previous > 0 && now - previous < TimeSpan.FromSeconds(10).Ticks)
            return;
        Interlocked.Exchange(ref _lastAudiobookProgressReportTicks, now);
        try
        {
            await _catalogApi.SyncProgressAsync(AudiobookProgressSync.Create(
                ContentId, position, _audiobookTotalDurationSeconds)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogToFile("progress_error.txt", $"Audiobook progress failed: {ex.Message}");
        }
    }

    public void SetState(PlayerState newState)
    {
        if (State != newState) RetireNativeMenus();
        var threadId = Environment.CurrentManagedThreadId;
        LogToFile("state_trace.txt", $"SetState: {State} -> {newState} (thread={threadId})");

        // PiP is implemented by a native topmost popup flag, independent of
        // this service's logical state. Clear that native flag before every
        // non-PiP layout transition. Otherwise Show() observes the stale flag
        // and immediately re-enters PiP, leaving the OSC state, fullscreen
        // state, and actual window geometry out of sync.
        if (newState != PlayerState.PictureInPicture)
            _videoWindow?.ExitPictureInPicture();

        // Even if the state hasn't changed, we MUST still ensure the video
        // popup is visible when entering Expanded — ContinuePlayingNextAsync
        // hides the popup for the UP NEXT cinematic but the state stays
        // Expanded throughout. Without this, Expanded → Expanded skips the
        // Show() call and the popup stays hidden.
        if (State == newState)
        {
            if (!IsAudiobook && (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen))
            {
                _videoWindow?.Show();
                // Playing Next hides the Lua OSC while leaving the logical
                // player state Expanded. Autoplay therefore reaches this
                // same-state path when the successor file starts. Re-enable
                // the OSC explicitly; otherwise only a later state change
                // (most visibly a fullscreen double-click) makes controls
                // available again.
                _mpv?.SendScriptMessage("osc-set-visibility", "true");
            }
            else if (IsAudiobook)
                _videoWindow?.Hide();
            PublishNativeMenuScopes();
            PublishFullscreenVisualState(newState == PlayerState.Fullscreen);
            PublishPictureInPictureVisualState(newState == PlayerState.PictureInPicture);
            if (newState == PlayerState.Idle)
            {
                UpdateDisplayWakeLock(false);
                EndBufferingQuality();
            }
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
        if (newState == PlayerState.Idle)
        {
            UpdateDisplayWakeLock(false);
            EndBufferingQuality();
        }

        if (!IsAudiobook && (newState == PlayerState.Expanded || newState == PlayerState.Fullscreen))
        {
            _videoWindow?.Show();
            _mpv?.SendScriptMessage("osc-set-visibility", "true");
        }
        else if (newState == PlayerState.Minimized)
        {
            _mpv?.SendScriptMessage("osc-set-visibility", "false");
            if (IsAudiobook)
                _videoWindow?.Hide();
            else
                PositionVideoForMiniBar();
        }
        else if (newState == PlayerState.PictureInPicture)
        {
            _mpv?.SendScriptMessage("osc-set-visibility", "true");
            _videoWindow?.EnterPictureInPicture();
        }
        else
            _videoWindow?.Hide();

        PublishNativeMenuScopes();
        InvokeSubscribersSafely(StateChanged, newState, nameof(StateChanged));
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

    // ── Fullscreen (native playback surface) ─────────────────────────────

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public void EnterFullscreen()
    {
        if (State is not (PlayerState.Expanded or PlayerState.PictureInPicture) ||
            IsAudiobook ||
            _videoWindow == null)
            return;

        _videoWindow.EnterFullscreen();
        var entered = _videoWindow.SynchronizeFullscreenState();
        SetState(entered ? PlayerState.Fullscreen : PlayerState.Expanded);
    }

    public void ExitFullscreen()
    {
        if (State != PlayerState.Fullscreen && _videoWindow?.IsFullscreen != true)
            return;

        _videoWindow?.ExitFullscreen();
        SetState(PlayerState.Expanded);
    }

    public void ToggleFullscreen()
    {
        if (_videoWindow?.IsFullscreen == true || State == PlayerState.Fullscreen)
            ExitFullscreen();
        else if (State is PlayerState.Expanded or PlayerState.PictureInPicture)
            EnterFullscreen();
    }

    /// <summary>
    /// Exits the native popup's fullscreen mode. The WinUI shell deliberately
    /// stays at its existing geometry so its title bar and responsive layout
    /// cannot animate behind the already-resized video surface.
    /// </summary>
    private void ExitAnyFullscreen()
    {
        if (State != PlayerState.Fullscreen) return;
        ExitFullscreen();
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
    public Task PlayAsync(
        string contentId,
        bool fromStart = false,
        int? fileId = null,
        int? audioTrackIndex = null,
        int? subtitleSelection = null,
        double? startPositionOverride = null,
        WatchDetailResponse? prefetchedWatchDetail = null,
        SubtitleTrackSignature? subtitleTrackSignature = null,
        int? libraryId = null,
        string? shuffleId = null,
        PlaybackAuthorityScope? authority = null,
        CancellationToken cancellationToken = default,
        bool? sourceVersionPinned = null,
        ApiRequestContext? expectedContext = null,
        ApiIdentityContext? expectedIdentityContext = null)
        => PlayWithOwnerAsync(contentId, fromStart, fileId, audioTrackIndex, subtitleSelection,
            startPositionOverride, prefetchedWatchDetail, subtitleTrackSignature, libraryId, shuffleId,
            authority, cancellationToken, sourceVersionPinned, expectedContext, expectedIdentityContext);

    // Ordinary restarts keep their accepted identity without acquiring the
    // quick-play policy that supersedes preparation and requires no active room.
    public Task RestartAsync(string contentId, int? libraryId, ApiIdentityContext identity)
        => PlayWithOwnerAsync(contentId, fromStart: true, libraryId: libraryId, ordinaryIdentity: identity);

    private readonly record struct ViewerPlayOwner(ApiIdentityContext Identity, bool SupersedesPreparation);

    private async Task PlayWithOwnerAsync(
        string contentId,
        bool fromStart = false,
        int? fileId = null,
        int? audioTrackIndex = null,
        int? subtitleSelection = null,
        double? startPositionOverride = null,
        WatchDetailResponse? prefetchedWatchDetail = null,
        SubtitleTrackSignature? subtitleTrackSignature = null,
        int? libraryId = null,
        string? shuffleId = null,
        PlaybackAuthorityScope? authority = null,
        CancellationToken cancellationToken = default,
        bool? sourceVersionPinned = null,
        ApiRequestContext? expectedContext = null,
        ApiIdentityContext? expectedIdentityContext = null,
        ApiIdentityContext? ordinaryIdentity = null)
    {
        var closeGeneration = Volatile.Read(ref _closeGeneration);
        var nextAuthority = authority ?? _localAuthority;
        // Cards carry their original identity, not an access revision. Preserve
        // the strict entry check for callers supplying only a legacy read owner.
        ApiIdentityContext? expectedIdentity = expectedIdentityContext ?? (expectedContext.HasValue
            ? nextAuthority.ApiClient.CaptureIdentityContext() : ordinaryIdentity);
        ViewerPlayOwner? expectedOwner = expectedIdentity is { } identity
            ? new(identity, SupersedesPreparation: ordinaryIdentity == null) : null;
        var supersedesPreparation = expectedOwner?.SupersedesPreparation == true;
        if (!expectedIdentityContext.HasValue && expectedContext is { } expected && !nextAuthority.ApiClient.IsCurrentContext(expected)) return;
        bool IsCurrentOwner() => !cancellationToken.IsCancellationRequested &&
            (expectedOwner is not { } owner || IsCurrentViewerPlayOwner(nextAuthority, owner));
        // A scoped UI action may wait behind another authority switch. Do not
        // cancel current playback until that action still owns its request.
        if (expectedOwner.HasValue && !IsCurrentOwner()) return;
        CancellationTokenSource? scopedIntent = null;
        if (supersedesPreparation)
        {
            scopedIntent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = scopedIntent.Token;
        }
        // Accept newer viewer intent immediately, even when the earlier start
        // is still holding the authority gate. Queued requests never cancel
        // established playback merely by reserving their place.
        var reservation = new object();
        Interlocked.Exchange(ref _viewerPlayReservation, reservation);
        if (scopedIntent != null)
        {
            var previousIntent = Interlocked.Exchange(ref _scopedViewerPlayCts, scopedIntent);
            try { previousIntent?.Cancel(); } catch (ObjectDisposedException) { }
        }
        try
        {
            if (!supersedesPreparation)
            {
                _ = FlushSubtitleOffsetAsync();
                // Ordinary starts queue behind accepted still actions; only a
                // newer scoped viewer intent may supersede their preparation.
                if (Volatile.Read(ref _scopedViewerPlayCts) == null)
                    CancelPendingPlayRequest();
            }
            else if (IsCurrentOwner() && Volatile.Read(ref _playRequestCts) != null)
            {
                // A still click supersedes ordinary as well as scoped pending
                // starts before waiting for their authority gate. With no
                // preparation pending, leave established playback untouched.
                CancelPendingPlayRequest();
            }
            await _authoritySwitchGate.WaitAsync(cancellationToken);
            try
            {
                // Close retires queued still actions even after _closing resets.
                // Check before canceling playback or preparing the replacement;
                // an intentional authority-switch close below is not a queued wait.
                if (expectedOwner.HasValue && (!IsCurrentOwner() || (supersedesPreparation && closeGeneration != Volatile.Read(ref _closeGeneration)))) return;
                if (supersedesPreparation)
                {
                    _ = FlushSubtitleOffsetAsync();
                    CancelPendingPlayRequest();
                }
                if (_closing)
                    await CloseAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (expectedOwner.HasValue && (!IsCurrentOwner() || (supersedesPreparation && closeGeneration != Volatile.Read(ref _closeGeneration)))) return;
                if (!ReferenceEquals(_activeAuthority, nextAuthority))
                {
                    await CloseAsync();
                    try { await PendingProgressSave; } catch { /* The outgoing manager reports final-write failures. */ }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (expectedOwner.HasValue && !IsCurrentOwner()) return;
                    _activeAuthority = nextAuthority;
                    _shuffleController = null;
                    _watchDetailPrefetches.Clear();
                    _subtitlePreferences = null;
                    _subtitleAppearance = null;
                    _seekContext = null;
                    Interlocked.Increment(ref _seekGeneration);
                }
                if (_authorityLifetimeCts.IsCancellationRequested)
                {
                    _authorityLifetimeCts.Dispose();
                    _authorityLifetimeCts = new();
                }
                await PlayRequestAsync(contentId, fromStart, fileId, audioTrackIndex,
                    subtitleSelection, startPositionOverride, HasRemoteAuthority ? null : prefetchedWatchDetail,
                    subtitleTrackSignature, libraryId, shuffleId, cancellationToken, sourceVersionPinned, expectedOwner);
            }
            finally { _authoritySwitchGate.Release(); }
        }
        catch (OperationCanceledException) when (scopedIntent?.IsCancellationRequested == true) { }
        finally
        {
            try
            {
                // A reservation may be canceled at the gate or lose its owner
                // before core takes over. Wait for the retired start's cleanup,
                // then repair only the latest abandoned hand-off under the gate.
                // Close owns cleanup after retiring the authority lifetime; a
                // canceled queued request must not wait for that gate again.
                if (!_authorityLifetimeCts.IsCancellationRequested &&
                    ReferenceEquals(reservation, Volatile.Read(ref _viewerPlayReservation)))
                {
                    await _authoritySwitchGate.WaitAsync();
                    try
                    {
                        if (ReferenceEquals(reservation, Volatile.Read(ref _viewerPlayReservation)))
                            CompleteRetiredPlayHandoff();
                    }
                    finally { _authoritySwitchGate.Release(); }
                }
            }
            finally
            {
                if (scopedIntent != null)
                {
                    Interlocked.CompareExchange(ref _scopedViewerPlayCts, null, scopedIntent);
                    scopedIntent.Dispose();
                }
            }
        }
    }

    // Called under _authoritySwitchGate; an entered successor clears this
    // marker before taking ownership of the retired content's state.
    private void CompleteRetiredPlayHandoff()
    {
        var retiredContentId = _retiredPlayContentId;
        _retiredPlayContentId = null;
        if (retiredContentId == null || ContentId != retiredContentId ||
            _playbackManager != null || _playbackCts != null) return;
        SetSwitchingContent(false);
        _activeTransportPlan = null;
        ContentId = null;
        ActiveLibraryId = null;
        _videoWindow?.Hide();
        SetState(PlayerState.Idle);
        SetConnectionMediaActive(false);
    }

    private bool IsCurrentViewerPlayOwner(PlaybackAuthorityScope authority, ViewerPlayOwner owner) =>
        authority.ApiClient.IsCurrentIdentityContext(owner.Identity) &&
        (!owner.SupersedesPreparation || App.Services.GetService<WatchTogetherCoordinator>()?.ActiveRoom == null);

    private async Task PlayRequestAsync(string contentId, bool fromStart, int? fileId,
        int? audioTrackIndex, int? subtitleSelection, double? startPositionOverride,
        WatchDetailResponse? prefetchedWatchDetail, SubtitleTrackSignature? subtitleTrackSignature,
        int? libraryId, string? shuffleId, CancellationToken cancellationToken, bool? sourceVersionPinned,
        ViewerPlayOwner? expectedOwner)
    {
        if (_closing)
            return;

        App.Services.GetService<ThemeMusicService>()?.Interrupt();
        var connectionObserver = PlaybackConnectionService.For(_activeAuthority.PlaybackApi);
        var ownerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _authorityLifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _playRequestCts, ownerCts);
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        previous?.Dispose();
        RetireNativeMenus();
        var generation = Interlocked.Increment(ref _playRequestGeneration);
        var gateEntered = false;

        try
        {
            await _playRequestGate.WaitAsync(ownerCts.Token);
            gateEntered = true;
            if (expectedOwner is { } owner && !IsCurrentViewerPlayOwner(_activeAuthority, owner)) return;
            ResetStartupTelemetry();
            SetConnectionMediaActive(true);
            ownerCts.Token.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _playRequestGeneration))
                return;

            await PlayCoreAsync(
                contentId,
                fromStart,
                fileId,
                audioTrackIndex,
                subtitleSelection,
                subtitleTrackSignature,
                startPositionOverride,
                prefetchedWatchDetail,
                libraryId,
                shuffleId,
                ownerCts.Token,
                sourceVersionPinned, expectedOwner);
        }
        catch (OperationCanceledException) when (ownerCts.IsCancellationRequested ||
            expectedOwner is { } owner && !IsCurrentViewerPlayOwner(_activeAuthority, owner))
        {
            LogToFile("state_trace.txt", $"Play request superseded: contentId={contentId}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _playRequestGeneration) && State == PlayerState.Idle)
            {
                connectionObserver.SetMediaActive(false);
                PlaybackConnectionService.For(_localAuthority.PlaybackApi).SetMediaActive(false);
            }
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
        SubtitleTrackSignature? subtitleTrackSignature,
        double? startPositionOverride,
        WatchDetailResponse? prefetchedWatchDetail,
        int? libraryId,
        string? shuffleId,
        CancellationToken requestToken,
        bool? sourceVersionPinned,
        ViewerPlayOwner? expectedOwner = null)
    {
        var authority = _activeAuthority;
        var requestGeneration = Volatile.Read(ref _playRequestGeneration);
        var scopedIntent = Volatile.Read(ref _scopedViewerPlayCts);
        // Access revisions retire reads, not the accepted account/profile's
        // playback intent. Each preparation attempt captures a fresh read context.
        bool IsCurrentOwner() => expectedOwner is not { } owner || IsCurrentViewerPlayOwner(authority, owner);
        void EnsureCurrentOwner()
        {
            requestToken.ThrowIfCancellationRequested();
            if (!IsCurrentOwner()) throw new OperationCanceledException("Playback request owner departed.", requestToken);
        }
        async Task<T> PrepareReadAsync<T>(Func<ApiRequestContext?, Task<T>> prepare)
        {
            for (var attempt = 0; ; attempt++)
            {
                EnsureCurrentOwner();
                ApiRequestContext? context = expectedOwner.HasValue ? authority.ApiClient.CaptureContext() : null;
                EnsureCurrentOwner();
                try
                {
                    var result = await prepare(context);
                    EnsureCurrentOwner();
                    return result;
                }
                catch (OperationCanceledException ex) when (!requestToken.IsCancellationRequested && IsCurrentOwner() &&
                    context is { } read && !authority.ApiClient.IsCurrentContext(read))
                {
                    // Retry retired reads once, never recapturing a departed
                    // account/profile. Session creation is outside this helper.
                    if (attempt != 0)
                        throw new InvalidOperationException("Your access changed while preparing playback. Please try again.", ex);
                }
            }
        }
        EnsureCurrentOwner();
        // Suppress outgoing EOF throughout the drain, but leave navigation,
        // subtitles and the session intact until the owner is revalidated.
        var wasSwitchingContent = _switchingContent;
        var wasSubtitleMenuPending = _subtitleMenuPending;
        _subtitleMenuPending = true;
        SetSwitchingContent(true);
        try
        {
            await CancelAndDrainTransportRestartsAsync(requestToken);
            SetSwitchingContent(true);
            EnsureCurrentOwner();
        }
        catch
        {
            SetSwitchingContent(wasSwitchingContent);
            _subtitleMenuPending = wasSubtitleMenuPending;
            throw;
        }
        _retiredPlayContentId = null;
        StopSubtitleSync();
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);
        // Fetch the effective device/profile style in parallel with playback
        // preparation so fresh launches render the first subtitle correctly
        // without adding another serial request to startup time.
        ResetSubtitlePreferenceSession();
        ResetVideoPlaybackRateSession();
        var subtitleAppearanceTask = EnsureSubtitleAppearanceLoadedAsync(requestToken);
        _pendingSubtitleSelection = subtitleSelection;
        _pendingInitialServerSubtitleIndex = null;
        _pendingInitialSubtitleFileId = null;
        _subtitleMenuFileId = null;
        _subtitleVersionSwitchActive = false;
        _pendingAutomaticSubtitleSelection = null;
        _activeSubtitleServerIndex = -1;
        _executedSubtitleTrack = null;
        _executedSubtitleFileId = null;
        _pendingSubtitlePreferenceIndex = null;
        _pendingSubtitlePreferenceOwner = null;
        LogToFile("state_trace.txt", $"PlayAsync called: contentId={contentId} fromStart={fromStart} audioTrackIndex={audioTrackIndex?.ToString() ?? "auto"} subtitleSelection={FormatSubtitleSelection(subtitleSelection)} State={State} IsLoading={IsLoading}");
        // Take ownership before clearing the outgoing episode hint. The old
        // file can publish one final position event at the Playing Next
        // countdown boundary; without this guard it can re-enter post-roll
        // while the successor is starting and disable the successor's OSC.
        SetSwitchingContent(true);
        PrepareEpisodeNavigationForContent(contentId);

        // The same guard must remain set before stopping the previous mpv
        // previous mpv session. Without it, _mpv?.Stop() fires end-file →
        // _mpvPlaybackEndedHandler runs the natural-end cleanup path
        // (CloseAsync), which races against the new-session setup below
        // and crashes the player. The flag causes the handler to short-circuit.
        CancelPendingFileLoadTimeout();
        Interlocked.Exchange(ref _consecutiveFileLoadTimeouts, 0);

        // Begin retiring the previous session, but do not hold the next-item
        // startup path behind the server's database/history/scrobble cleanup.
        // The stop endpoint releases the live session before that slower work;
        // if an account has a one-stream limit, the guarded retry below waits
        // for retirement only when the server actually reports that limit.
        Task<bool> retiringSessionTask = Task.FromResult(false);
        var retiringTranscode = _playbackManager?.CurrentSession?.PlayMethod == "transcode";
        if (_playbackManager != null)
        {
            lock (_bufferingGate) FlushBufferingSummaryCore();
            var retiringManager = _playbackManager;
            var finalPosition = CurrentMediaPosition;
            var retiringContentId = ContentId;
            var retiringDuration = CurrentMediaDuration;
            _mpv?.Stop();
            retiringSessionTask = retiringManager.TryRetireSessionAsync(
                finalPosition > 0 ? ToSessionPosition(finalPosition) : null,
                isPaused: true);
            // Switching straight to another item (next/previous episode, a new
            // title) retires this session without CloseCoreAsync; publish the
            // same invalidation so its prefetched detail/watch data is dropped.
            if (!HasRemoteAuthority && !string.IsNullOrEmpty(retiringContentId) && finalPosition > 0)
            {
                // Only a replay of this same item waits for this stop; the
                // next item's preparation proceeds immediately.
                _watchDetailFreshness.TrackFinalSave(retiringContentId, retiringSessionTask);
                try
                {
                    WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated(
                        retiringContentId, finalPosition, retiringDuration,
                        retiringDuration > 0 && finalPosition >= retiringDuration * 0.9)
                    { Persisted = retiringSessionTask });
                }
                catch (Exception ex) { LogToFile("state_trace.txt", $"Publish progress event error: {ex.Message}"); }
            }
            PendingProgressSave = PendingProgressSave.IsCompleted
                ? retiringSessionTask
                : Task.WhenAll(PendingProgressSave, retiringSessionTask);
            retiringManager.ProgressReportingFailed -= OnProgressReportingFailed;
            _playbackManager = null;
            _ = FinishClosingSessionAsync(retiringManager, retiringSessionTask);
        }

        ClearPlaybackTerminalState();
        ErrorMessage = null;
        IsLoading = true;
        // Every user-initiated play request starts a fresh logical watch, even
        // when it replays the same item. Transport reloads bypass PlayCoreAsync
        // and retain their current OSC auto-skip latches.
        _autoSkipMarkerIdentity.Clear();
        ContentId = contentId;
        ActiveLibraryId = libraryId;
        if (string.IsNullOrWhiteSpace(shuffleId)) _shuffleController?.Leave();
        else ShuffleController.Attach(shuffleId);
        _resumePosition = 0;
        _transportStartPosition = null;
        _activeTransportPlan = null;
        _timelineOffsetSeconds = 0;
        _transportDurationSeconds = null;
        _canSeekAnywhere = true;
        _activeQualityTier = "original";
        _roomPlaybackPaused = true;
        _requestedMediaFileId = null;
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        _prematureEofLastAttemptMs = 0;
        _prematureEofStreak = 0;
        _recoveryPauseIntentOverride = null;
        _playingNextShown = false;
        _postRollActive = false;
        _postRollVideoEnded = false;
        ClearChapterThumbnailOverlayCache();
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        var playbackCts = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        _playbackCts = playbackCts;

        var loadingWindow = App.MainWindowInstance;
        // Retain the same surface owner through preparation, native loading,
        // recovery, cancellation and close.
        var loadingOwner = playbackCts;
        loadingWindow?.ShowLoadingOverlay(loadingOwner);

        PlaybackManager? requestManager = null;
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
            _playbackManager = requestManager = new PlaybackManager(_playbackApi, _catalogApi, _authService, _apiClient, passthrough);
            _playbackManager.ProgressReportingFailed += OnProgressReportingFailed;

            // Start the network request before initializing libmpv. The native
            // window remains hidden until SetState below, but first playback
            // no longer pays the watch-detail and mpv startup costs serially.
            // On deliberate hover the task is usually already complete, while
            // direct clicks still benefit from this overlap.
            WatchDetailResponse watchDetail;
            // A page can hold its /watch snapshot across a Reset Progress or
            // watched toggle; only trust it when no mutation happened since.
            if (expectedOwner.HasValue)
            {
                EnsureMpvInitialized();
                watchDetail = await PrepareReadAsync(context => GetOrFetchWatchDetailCoreAsync(contentId, consumePrefetch: true,
                    ct: requestToken, libraryId: libraryId, fileId: fileId, authority: authority, expectedContext: context));
            }
            else if (prefetchedWatchDetail != null &&
                string.Equals(prefetchedWatchDetail.ContentId, contentId, StringComparison.Ordinal) &&
                prefetchedWatchDetail.PreparedLibraryId == libraryId && prefetchedWatchDetail.PreparedFileId == fileId &&
                IsCurrentWatchDetail(prefetchedWatchDetail))
            {
                EnsureMpvInitialized();
                watchDetail = prefetchedWatchDetail;
            }
            else
            {
                // Prefetch owns and observes the network task. The shared core
                // call below consumes it after libmpv initialization finishes.
                PrefetchWatchDetailCore(contentId, _activeAuthority, libraryId, fileId);
                EnsureMpvInitialized();
                watchDetail = await GetOrFetchWatchDetailCoreAsync(
                    contentId,
                    consumePrefetch: true,
                    ct: requestToken,
                    libraryId: libraryId,
                    fileId: fileId);
            }
            EnsureCurrentOwner();
            _playbackManager.UseWatchDetail(watchDetail);
            SetTitleFromWatchDetail(watchDetail);
            IsAudiobook = watchDetail.Type.Equals("audiobook", StringComparison.OrdinalIgnoreCase);
            if (IsAudiobook)
            {
                await RefreshSeekPreferencesAsync(requestToken);
                EnsureCurrentOwner();
                Versions = watchDetail.Versions?.ToList() ?? [];
                _audiobookTotalDurationSeconds = Versions.Sum(version => Math.Max(0, version.Duration));
                _ = LoadAudiobookPresentationAsync(contentId, requestToken);
            }
            else
            {
                _audiobookPartOffsetSeconds = 0;
                _audiobookTotalDurationSeconds = 0;
                AudiobookPosterUrl = null;
                AudiobookAuthor = null;
                AudiobookNarrator = null;
            }

            var absoluteStartPosition = startPositionOverride.HasValue
                ? Math.Max(0, startPositionOverride.Value)
                : DetermineStartPosition(watchDetail, fromStart);
            if (IsAudiobook && !fromStart && !startPositionOverride.HasValue &&
                absoluteStartPosition > 0 && _settingsService.Load().AudiobookSmartRewind)
            {
                absoluteStartPosition = Math.Max(0, absoluteStartPosition - 10);
            }
            var audiobookPart = IsAudiobook
                ? FindAudiobookPart(absoluteStartPosition, fileId)
                : null;
            if (audiobookPart.HasValue)
            {
                fileId = audiobookPart.Value.Version.FileId;
                _audiobookPartOffsetSeconds = audiobookPart.Value.Start;
            }

            SetConnectionMediaActive(true);
            _playbackManager.SourceVersionPinned = PlaybackSourceVersionPolicy.IsPinned(fileId, sourceVersionPinned,
                IsAudiobook, IsWatchTogetherPlayback, HasRemoteAuthority);
            _playbackManager.IgnoreConnectionMeasurements = HasRemoteAuthority;
            // A catalog effective/default track is a preview, not a fresh
            // viewer choice. Omit Auto so the server can honor remembered
            // tracks and refine compatibility on the source it actually plays.
            var selectedAudioTrackIndex = audioTrackIndex;
            var qualityPreferences = await PrepareReadAsync(context =>
                _playbackManager.ResolveQualityPreferencesAsync(requestToken, context));
            EnsureCurrentOwner();
            var candidateVersion = SelectVersion(watchDetail, fileId, qualityPreferences.Quality)
                ?? throw new InvalidOperationException("No playable version found.");
            Resolution = candidateVersion.Resolution;
            _requestedMediaFileId = candidateVersion.FileId;
            var startPosition = IsAudiobook
                ? Math.Max(0, absoluteStartPosition - _audiobookPartOffsetSeconds)
                : absoluteStartPosition;

            // A canceled start may already have reached the server. Only the
            // API delivery layer may retry it, retaining the same attempt ID;
            // a retired read context must never create another start attempt.
            PlaybackStartResponse session;
            try
            {
                session = await _playbackManager.StartSessionAsync(
                    candidateVersion.FileId,
                    startPosition,
                    forceStartPosition: IsAudiobook || fromStart || startPositionOverride.HasValue,
                    audioTrackIndex: selectedAudioTrackIndex,
                    forceDirectAudioSelection: selectedAudioTrackIndex.HasValue,
                    disableProgressPersistence: IsAudiobook,
                    ct: requestToken,
                    preferences: qualityPreferences,
                    expectedIdentityContext: expectedOwner?.Identity);
            }
            catch (Exception ex) when (
                ((ex is ApiException api && api.StatusCode == 429 &&
                    PlaybackFailureDescription.IsAccountLimitReason(api.ErrorCode, includeTranscode: retiringTranscode)) ||
                 (ex is PlaybackPlanTerminalException terminal && terminal.Retryable &&
                    PlaybackFailureDescription.IsAccountLimitReason(terminal.Reason, includeTranscode: retiringTranscode))))
            {
                var retired = await retiringSessionTask.WaitAsync(requestToken);
                EnsureCurrentOwner();
                if (!retired)
                    throw;
                LogToFile("state_trace.txt", "Next playback session reached an account limit; server confirmed retirement, retrying once.");
                session = await _playbackManager.StartSessionAsync(
                    candidateVersion.FileId,
                    startPosition,
                    forceStartPosition: IsAudiobook || fromStart || startPositionOverride.HasValue,
                    audioTrackIndex: selectedAudioTrackIndex,
                    forceDirectAudioSelection: selectedAudioTrackIndex.HasValue,
                    disableProgressPersistence: IsAudiobook,
                    ct: requestToken,
                    preferences: qualityPreferences,
                    expectedIdentityContext: expectedOwner?.Identity);
            }
            EnsureCurrentOwner();
            var bestVersion = watchDetail.Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("The accepted playback source is missing from the version inventory.");
            EnsureVideoPlaybackRateSession();
            Resolution = bestVersion.Resolution;
            ResolveInitialSubtitleSelection(
                watchDetail,
                bestVersion,
                session,
                selectedAudioTrackIndex,
                subtitleSelection,
                subtitleTrackSignature);
            session = await ReconcileInitialSubtitlePlanAsync(session, requestToken);
            EnsureCurrentOwner();
            bestVersion = watchDetail.Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("The accepted playback source is missing from the version inventory.");
            PlayMethod = session.PlayMethod;
            if (session.ProtocolVersion >= 3)
                _activeQualityTier = session.ActiveQuality;
            InvokeSubscribersSafely(SessionStarted, session.SessionId, nameof(SessionStarted));

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
                playbackCts.Token);

            EnsureCurrentOwner();
            if (_closing || !ReferenceEquals(_playbackManager?.CurrentSession, session))
                return;

            _mpv?.SetProperty(
                "speed",
                (IsAudiobook ? _settingsService.GetAudiobookPlaybackRate(ContentId) :
                    IsWatchTogetherPlayback ? 1 : DesiredVideoPlaybackRate)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (subtitleAppearanceTask.IsCompletedSuccessfully)
            {
                PushResolvedSubtitlePreferences();
            }
            else
            {
                _ = ApplySubtitleAppearanceWhenLoadedAsync(subtitleAppearanceTask, _activeAuthority, _apiClient.CaptureContext(), Volatile.Read(ref _playRequestGeneration));
            }

            SetState(IsAudiobook ? PlayerState.Minimized : PlayerState.Expanded);
            ApplyPreparedTransport(prepared);

            LogToFile(
                "state_trace.txt",
                $"LoadFile: transport={prepared.Plan.TransportKind} url={PlaybackUrlRedactor.Redact(prepared.LocalUrl)}");

            // Phase 2b: apply pre-play subtitle selection by setting mpv's "sid"
            // property BEFORE loadfile so the initial state is the user's choice.
            // -1 = "no" (off), 0+ = 1-based mpv sid. null = don't touch, let mpv default.
            ApplyPendingSubtitleSelection();

            EnsureCurrentOwner();
            if (_closing || !ReferenceEquals(_playbackManager?.CurrentSession, session))
                return;
            // Room startup stays held through FileLoaded. Only a due room
            // command may release it after the media becomes playable.
            IsPaused = IsWatchTogetherPlayback;
            BeginMpvLoad(prepared, restorePaused: IsWatchTogetherPlayback);
            LogToFile("state_trace.txt", "Play() called");

            _mpv?.SendScriptMessage("osc-set-play-method", session.PlayMethod ?? "direct");
        }
        catch (Exception) when (requestToken.IsCancellationRequested || !IsCurrentOwner())
        {
            playbackCts.Cancel();
            if (ReferenceEquals(_playbackCts, playbackCts) && ReferenceEquals(_playbackManager, requestManager))
            {
                CancelPendingFileLoadTimeout();
                StopDirectStreamProxy();
                StopHlsProxy();
                _playbackManager = null;
                _playbackCts = null;
                IsLoading = false;
                // Preserve the EOF guard during a reserved hand-off, but leave
                // cleanup for the latest reservation if it never reaches core.
                var superseded = requestGeneration != Volatile.Read(ref _playRequestGeneration) ||
                    !ReferenceEquals(scopedIntent, Volatile.Read(ref _scopedViewerPlayCts));
                _retiredPlayContentId = contentId;
                if (!superseded)
                    CompleteRetiredPlayHandoff();
            }
            loadingWindow?.HideLoadingOverlay(loadingOwner);
            if (requestManager != null)
            {
                try { await requestManager.StopSessionAsync(); }
                catch (Exception stopEx) { LogToFile("state_trace.txt", $"Canceled start cleanup error: {stopEx.Message}"); }
                requestManager.ProgressReportingFailed -= OnProgressReportingFailed;
                requestManager.Dispose();
            }
            playbackCts.Dispose();
            throw new OperationCanceledException("Playback request retired.", requestToken);
        }
        catch (Exception ex)
        {
            CancelPendingFileLoadTimeout();
            LogToFile("player_crash.txt", ex.ToString());
            if (ex is ApiException { StatusCode: 404 })
                _watchDetailPrefetches.Invalidate(WatchPreparationKey(contentId, libraryId, fileId));
            var (errTitle, errDetail) = DescribePlaybackError(ex);
            ErrorMessage = errDetail;
            IsLoading = false;
            SetSwitchingContent(false);

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
        if (ex is PlaybackPlanTerminalException terminal)
        {
            var description = PlaybackFailureDescription.Describe(terminal);
            return (description.Title, description.Message);
        }
        if (ex is ApiException api)
        {
            var transportDescription = PlaybackFailureDescription.DescribeTransport(api);
            if (transportDescription != null)
                return (transportDescription.Title, transportDescription.Message);
            return ("Playback unavailable", string.IsNullOrWhiteSpace(api.Message) ? "Playback could not start." : api.Message);
        }
        if (ex is UserStateChangedException)
            return ("Progress changed", "This title's saved progress changed while playback was starting. Try again.");
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
    /// current media position; if that restart fails, the player remains open
    /// with explicit Retry and Exit actions.
    /// </summary>
    private void OnProgressReportingFailed(string message)
    {
        LogToFile("state_trace.txt", $"ProgressReportingFailed: {message}");
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        Action handle = () =>
        {
            if (_closing || State == PlayerState.Idle || _playbackTerminalState != null || _playbackManager?.CurrentSession == null)
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
        => BeginOwnedMpvLoad(prepared, restorePaused, null);

    private void BeginOwnedMpvLoad(PreparedPlaybackTransport prepared, bool restorePaused, Action<long>? acceptLoad,
        Action<PlaybackBusyOwner>? acceptBusy = null)
    {
        // Hand this attempt's exact identity to the initiating operation before
        // any preparation/native call can fail. A different load invalidates it.
        var fileOwner = Interlocked.Increment(ref _playbackFileGeneration);
        acceptLoad?.Invoke(fileOwner);
        RetireNativeMenus();
        EnsureVideoPlaybackRateSession();
        AdoptBufferingQualityPlan();
        StartPlaybackStallWatchdog();
        _idleRemuxResume.ResetPauseClock();
        var mpv = _mpv ?? throw new InvalidOperationException("The native player is unavailable.");
        var manager = _playbackManager ?? throw new InvalidOperationException("The playback session is unavailable.");
        var sessionId = manager.SessionId
            ?? throw new InvalidOperationException("The playback session ended before media loading began.");
        var loadingOwner = _playbackCts;
        RetireForeignSubtitleSelections();

        // Retire the outgoing file's asynchronous subtitle work before loadfile:
        // the next FILE_LOADED can be delayed by the replacement HTTP stream.
        CancelPendingLoadedMediaInitialization();
        CancelPendingFileLoadTimeout();
        var playbackToken = _playbackCts?.Token ?? CancellationToken.None;
        var ownerCts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        var generation = Interlocked.Increment(ref _fileLoadGeneration);
        _fileLoadTimeoutCts = ownerCts;
        _restorePausedAfterLoad = IsWatchTogetherPlayback ? _roomPlaybackPaused : restorePaused;
        _transportHasFirstOutput = false;
        _transportStartPosition = Math.Max(0, PlaybackTimeline.ToMediaTime(
            prepared.ResumeAfterLoadSeconds > 0 ? prepared.ResumeAfterLoadSeconds : prepared.MpvLoadStartSeconds,
            prepared.TimelineOffsetSeconds)) + _audiobookPartOffsetSeconds;
        var loadBusyOwner = BeginPlaybackBusy();
        acceptBusy?.Invoke(loadBusyOwner);
        SetConnectionMediaActive(true);
        if (loadingOwner != null)
            App.MainWindowInstance?.ShowLoadingOverlay(loadingOwner);

        try
        {
            mpv.SendScriptMessage("osc-set-loading", "true");
            ConfigureAudioOutput(mpv);
            var bufferProfile = prepared.Plan.IsHls
                ? MpvNetworkBufferSizing.Default
                : MpvNetworkBufferSizing.ForBitrateKbps(ActiveVersion?.Bitrate ?? 0);
            mpv.ConfigureNetworkBuffer(
                bufferProfile.MaxMiB,
                bufferProfile.BackMiB,
                bufferProfile.ReadAheadSeconds,
                bufferProfile.StreamMiB);

            // Freeze the outgoing transport before replacing it. Resuming mpv
            // immediately after an asynchronous loadfile command can expose a
            // few seconds from the old stream (or the pre-seek HLS fragment)
            // before the new file's start position is applied, which looks
            // exactly like the opening transcode segment replaying once.
            // FileLoaded is the single authority that restores play/pause.
            mpv.Pause();
            // Do not carry an original file's aid into a single-track remux/HLS load.
            mpv.SetProperty("aid", "auto");
            Volatile.Write(ref _mpvLoadingOverlayOwner, loadingOwner);
            mpv.LoadFile(prepared.LocalUrl, null, prepared.MpvLoadStartSeconds);
        }
        catch
        {
            mpv.SendScriptMessage("osc-set-loading", "false");
            mpv.SendScriptMessage("osc-set-buffering", "false");
            CancelPendingFileLoadTimeout();
            throw;
        }

        _ = MonitorFileLoadAsync(
            generation,
            ownerCts,
            manager,
            sessionId,
            ContentId,
            prepared.Plan.TransportKind == PlaybackTransportKind.DirectProgressive
                ? TimeSpan.FromSeconds(15)
                : TimeSpan.FromSeconds(30));
    }

    private void ConfigureAudioOutput(MpvPlayer mpv)
    {
        var bitstream = _settingsService.Load().AudioBitstreamPassthrough;
        mpv.SetProperty("audio-spdif", bitstream ? "ac3,eac3,dts-hd,truehd" : "");
        mpv.SetProperty("audio-exclusive", bitstream ? "yes" : "no");
        mpv.SetProperty("audio-channels", bitstream ? "auto" : "auto-safe");
        // The video renderer is an owned popup whose presentation cadence can
        // be throttled by DWM when focus moves to another application. Always
        // keep audio as the master clock; otherwise display-resample can burst
        // queued video frames after deactivation while audio stays at 1x.
        // Audio ownership is also required for compressed HDMI passthrough.
        mpv.SetProperty("video-sync", "audio");
    }

    private void UpdateDisplayWakeLock(bool active)
    {
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher == null)
        {
            LogToFile("state_trace.txt", $"Display wake lock skipped (no UI dispatcher), requested={active}");
            return;
        }

        if (!dispatcher.HasThreadAccess)
        {
            if (!dispatcher.TryEnqueue(() => UpdateDisplayWakeLock(active)))
                LogToFile("state_trace.txt", $"Display wake lock dispatch failed, requested={active}");
            return;
        }

        try
        {
            if (active && !_displayRequestActive)
            {
                if (SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                _displayRequestActive = true;
                LogToFile("state_trace.txt", "Display wake lock acquired");
            }
            else if (!active && _displayRequestActive)
            {
                if (SetThreadExecutionState(EsContinuous) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                _displayRequestActive = false;
                LogToFile("state_trace.txt", "Display wake lock released");
            }
        }
        catch (Exception ex)
        {
            _displayRequestActive = false;
            LogToFile("state_trace.txt", $"Display wake lock update failed: {ex.Message}");
        }
    }

    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;
    private const uint EsContinuous = 0x80000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);

    private async Task MonitorFileLoadAsync(
        long generation,
        CancellationTokenSource ownerCts,
        PlaybackManager expectedManager,
        string expectedSessionId,
        string? expectedContentId,
        TimeSpan loadDeadline)
    {
        try
        {
            await Task.Delay(loadDeadline, ownerCts.Token).ConfigureAwait(false);

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
            if (TryBufferingStartupRecovery()) return;
            var mediaPosition = PlaybackRecoveryPosition;
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

    private void StartLoadedMediaInitialization()
    {
        CancelPendingLoadedMediaInitialization();
        var playbackToken = _playbackCts?.Token ?? CancellationToken.None;
        var ownerCts = CancellationTokenSource.CreateLinkedTokenSource(playbackToken);
        var generation = Interlocked.Increment(ref _loadedMediaInitGeneration);
        _loadedMediaInitCts = ownerCts;

        // Always enter the worker, even if cancellation wins immediately, so
        // the owner CTS is deterministically retired in its finally block.
        _ = Task.Run(() => InitializeLoadedMediaAsync(generation, ownerCts));
    }

    private async Task InitializeLoadedMediaAsync(
        long generation,
        CancellationTokenSource ownerCts)
    {
        var ct = ownerCts.Token;
        try
        {
            if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;
            RetireForeignSubtitleSelections();
            // Clearing/replacing the initial choice also retires its automatic
            // refinement; it must not overwrite a newer manager-owned choice.
            if (!_pendingInitialServerSubtitleIndex.HasValue) _pendingAutomaticSubtitleSelection = null;
            SendTitleToOsc();
            SendMediaInfoToOsc();
            SendAudioTrackListToOsc();
            SendChapterListToOsc();
            SendSubtitleListToOsc();
            SendQualityInfoToOsc();
            // Do not let settings from the previous file/profile act during
            // the short asynchronous refresh window for this media load.
            _mpv?.SendScriptMessage("osc-set-auto-skip", "false", "false", "false");
            SendMarkersToOsc();

            // Resolve against this file's inventory before any unrelated
            // settings round trip can delay (or prevent) subtitle selection.
            LoadSubtitles();
            var applyInitialSubtitleSelection = true;
            if (_pendingAutomaticSubtitleSelection is { } automatic && _playbackManager?.CurrentSession is { } session)
            {
                // Only the loaded file can attest native identity. Re-resolve the
                // automatic choice over executable routes, retaining candidate priority.
                var automaticIndex = SubtitleAutoSelect.Resolve(automatic with
                {
                    Tracks = SubtitleAutoSelect.BuildCandidates(session.SubtitleUrls.Where(HasSubtitleSelectionRoute)),
                });
                if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;
                lock (_subtitleTrackStateLock)
                {
                    applyInitialSubtitleSelection = ReferenceEquals(_pendingAutomaticSubtitleSelection, automatic);
                    if (applyInitialSubtitleSelection)
                    {
                        _pendingAutomaticSubtitleSelection = null;
                        _pendingInitialServerSubtitleIndex = automaticIndex ?? -1;
                        if (session.ProtocolVersion >= 3)
                            _playbackManager.RecordLocalSubtitleSelection(automaticIndex, isAutomatic: true);
                    }
                }
            }
            if (applyInitialSubtitleSelection)
            {
                var resolvedSubtitleIndex = _pendingInitialServerSubtitleIndex ??
                    (_playbackManager?.CurrentSession?.ProtocolVersion >= 3 ? _playbackManager.SubtitleSelectionForLoad : (int?)null);
                if (resolvedSubtitleIndex is int initialSubtitleIndex)
                {
                    _pendingInitialServerSubtitleIndex = null;
                    if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;
                    await SelectSubtitleByServerIndexAsync(initialSubtitleIndex, persist: false);
                }
            }

            if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;
            // Effective playback settings are profile/device scoped and may
            // change between sessions; they must not gate subtitle execution.
            await SendAutoSkipSettingsToOscAsync(ct);

            if (!IsLoadedMediaInitializationCurrent(generation, ownerCts)) return;
            await SendSubtitleAiAvailabilityToOscAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer load, playback switch, or close owns the player now.
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Loaded-media initialization failed: {ex}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _loadedMediaInitCts, null, ownerCts);
            ownerCts.Dispose();
        }
    }

    private bool IsLoadedMediaInitializationCurrent(
        long generation,
        CancellationTokenSource ownerCts) =>
        !ownerCts.IsCancellationRequested &&
        generation == Volatile.Read(ref _loadedMediaInitGeneration) &&
        ReferenceEquals(Volatile.Read(ref _loadedMediaInitCts), ownerCts) &&
        !_closing &&
        State != PlayerState.Idle;

    private void CancelPendingLoadedMediaInitialization()
    {
        Interlocked.Increment(ref _loadedMediaInitGeneration);
        CancelSubtitleSelectionRead();
        var cts = Interlocked.Exchange(ref _loadedMediaInitCts, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        // The worker owns disposal so cancellation cannot race its token reads.
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
        // Timer callbacks may overlap when a native read outlasts the period.
        // Own the whole callback, including detector/recovery bookkeeping after
        // the buffering sample, without making UI callers wait for the owner.
        if (Interlocked.CompareExchange(ref _stallWatchdogChecking, 1, 0) != 0) return;
        try
        {
            ObserveBufferingQuality();
            if (Volatile.Read(ref _bufferingActionRunning) != 0) return;
            var mpv = _mpv;
            var manager = _playbackManager;
            var recoveryInProgress = _closing ||
                State == PlayerState.Idle ||
                _switchingContent ||
                _qualitySwitchActive ||
                _prematureEofRecoveryActive ||
                _playbackTerminalState != null ||
                manager?.SessionRetirementPending == true ||
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

            // The loopback direct relay owns transient byte-range reconnects.
            // Do not replace a recoverable direct route with remux while that
            // reconnect is active, but cap the deferral so a hung upstream
            // request cannot suppress the player's route recovery forever.
            if (DirectPlaybackRecoveryPolicy.ShouldDeferRouteEscalation(
                    _directStreamProxy?.RecoveryElapsed))
            {
                return;
            }

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
        finally { Volatile.Write(ref _stallWatchdogChecking, 0); }
    }

    private Task RecoverInterruptedStreamAsync(double currentPosition, string reason, PlaybackStartResponse? restartSession = null)
        => RunStreamRecoveryAsync(currentPosition, reason, null, null, default, restartSession);

    private async Task RunStreamRecoveryAsync(double currentPosition, string reason,
        PlaybackManager? queuedOwner, bool? pauseIntent, CancellationToken ownerToken,
        PlaybackStartResponse? restartSession = null)
    {
        // Keep recovery decisions in the same bounded timeline as the reader and
        // decoder. Only fixed classifications belong here, never error messages.
        var diagnosticReason = reason switch
        {
            "progress-reporting-failed" or "file-load-timeout" or "buffering-stalled" or
            "position-stalled" or "direct-transport-error" or "file-load-error" or
            "playback-error" => reason,
            _ => "other-playback-recovery"
        };
        _mpv?.Diagnostics?.Record("recovery_requested", new { reason = diagnosticReason, position = currentPosition });
        _mpv?.Diagnostics?.Incident(diagnosticReason);
        var retryOwner = restartSession != null ? _playbackManager : null;
        var retryTerminal = restartSession != null ? _playbackTerminalState : null;
        var waitToken = restartSession != null ? _playbackCts?.Token ?? CancellationToken.None : ownerToken;
        if (queuedOwner != null || restartSession != null)
        {
            // A viewer retry must not disappear if the previous recovery is
            // still unwinding after publishing its terminal state.
            try { await _streamRecoveryGate.WaitAsync(waitToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (waitToken.IsCancellationRequested) { return; }
        }
        else if (!await _streamRecoveryGate.WaitAsync(0).ConfigureAwait(false))
        {
            LogToFile("state_trace.txt", $"Stream recovery skipped ({reason}): another recovery is active");
            return;
        }

        try
        {
            if (restartSession != null && (_closing || waitToken.IsCancellationRequested || State == PlayerState.Idle ||
                !ReferenceEquals(retryOwner, _playbackManager) ||
                !ReferenceEquals(retryTerminal, _playbackTerminalState))) return;
            if (queuedOwner != null)
            {
                if (_closing || ownerToken.IsCancellationRequested || State == PlayerState.Idle ||
                    !ReferenceEquals(queuedOwner, _playbackManager)) return;
                // Do not let an earlier recovery consume this switch's pause intent.
                _recoveryPauseIntentOverride = pauseIntent;
                currentPosition = CurrentMediaPosition;
            }
            await RecoverInterruptedStreamCoreAsync(currentPosition, reason, restartSession).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ownerToken.IsCancellationRequested || _closing || _playbackCts?.IsCancellationRequested == true)
        {
            LogToFile("state_trace.txt", $"Stream recovery canceled before restart ({reason})");
        }
        catch (Exception ex)
        {
            LogToFile("player_recovery_error.txt", $"Recovery setup failed ({reason}): {ex}");
            EnterPlaybackTerminalState(
                ex,
                Math.Max(0, currentPosition),
                reason,
                CaptureUserPausedState(),
                restartSession);
        }
        finally
        {
            _streamRecoveryGate.Release();
        }
    }

    private async Task RecoverInterruptedStreamCoreAsync(double currentPosition, string reason, PlaybackStartResponse? restartSession)
    {
        await CancelAndDrainTransportRestartsAsync();
        var manager = _playbackManager;
        var session = manager?.CurrentSession ?? restartSession;
        if (_mpv == null || manager == null || session == null)
            throw new PlaybackPlanTerminalException("session_expired",
                "The playback session is no longer available. Exit the player and start this item again.", retryable: false);

        var fileId = manager.CurrentSession == null || manager.SessionRetirementPending
            ? session.MediaFileId
            : _requestedMediaFileId ?? session.MediaFileId;
        int? audioTrackIndex = session.AudioTrackIndex >= 0 ? session.AudioTrackIndex : null;
        var previousPlan = _activeTransportPlan;
        // A keepalive failure can happen while the user has intentionally
        // paused. Preserve that intent across the replacement session, but do
        // not mistake mpv's network-cache pause for a user pause: buffering
        // recoveries must resume as soon as the replacement stream is ready.
        var restorePaused = _recoveryPauseIntentOverride
            ?? (_mpv.IsPaused && !_mpv.IsBufferingForCache);
        _recoveryPauseIntentOverride = null;
        var roomStartupPosition = IsWatchTogetherPlayback && !_transportHasFirstOutput
            ? RoomPlaybackStartPosition?.Invoke() : null;
        var resumePosition = roomStartupPosition ?? Math.Max(0, currentPosition - (_transportHasFirstOutput ? 2 : 0));
        var loadingOwner = _playbackCts;
        var ct = loadingOwner?.Token ?? CancellationToken.None;

        _prematureEofRecoveryActive = true;
        _prematureEofRecoveryPosition = resumePosition;
        SetSwitchingContent(true);
        IsLoading = true;
        if (restartSession != null)
            ClearPlaybackTerminalState();

        if (loadingOwner != null)
            App.MainWindowInstance?.ShowLoadingOverlay(loadingOwner);

        try
        {
            if (manager.CurrentSession != null && !manager.SessionRetirementPending &&
                session.ProtocolVersion >= 3 &&
                !string.Equals(reason, "progress-reporting-failed", StringComparison.Ordinal) &&
                !string.Equals(reason, "viewer-retry", StringComparison.Ordinal))
            {
                var classification = reason switch
                {
                    "file-load-timeout" => "startup_timeout",
                    "playback-error" => "decoder_or_transport_error",
                    _ => "playback_interrupted",
                };
                var replannedSession = await manager.RecoverPlaybackFailureAsync(
                    resumePosition,
                    classification,
                    reason,
                    ct).ConfigureAwait(false);
                var replannedVersion = Versions.FirstOrDefault(v => v.FileId == replannedSession.MediaFileId)
                    ?? throw new InvalidOperationException("The replanned media version is unavailable.");
                var replannedStreamUrl = manager.StreamUrl
                    ?? throw new InvalidOperationException("No stream URL was returned by playback recovery.");
                var replannedTransport = await PreparePlaybackTransportAsync(
                    replannedSession,
                    replannedVersion,
                    replannedStreamUrl,
                    resumePosition,
                    ct).ConfigureAwait(false);
                if (_closing || State == PlayerState.Idle ||
                    !ReferenceEquals(_playbackManager, manager) ||
                    !ReferenceEquals(manager.CurrentSession, replannedSession) || ct.IsCancellationRequested)
                    return;
                PlayMethod = replannedSession.PlayMethod;
                _activeQualityTier = replannedSession.ActiveQuality;
                ApplyPreparedTransport(replannedTransport);
                BeginMpvLoad(replannedTransport, restorePaused);
                IsPaused = restorePaused;
                _mpv.SendScriptMessage("osc-set-play-method", PlayMethod ?? "direct");
                LogToFile(
                    "state_trace.txt",
                    $"Stream recovery ({reason}) {(ReferenceEquals(session, replannedSession) ? "reopened existing direct" : "adopted replacement")} protocol-v3 plan {replannedSession.PlanId} " +
                    $"at mediaPos={resumePosition:F1}");
                return;
            }

            if (manager.CurrentSession != null && !manager.SessionRetirementPending &&
                previousPlan != null &&
                PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(
                    previousPlan.TransportKind,
                    reason))
            {
                var currentStreamUrl = manager.StreamUrl;
                if (!string.IsNullOrWhiteSpace(currentStreamUrl))
                {
                    var currentVersion = Versions.FirstOrDefault(v => v.FileId == fileId)
                        ?? throw new InvalidOperationException(
                            "The active media version is no longer available.");
                    var reloaded = await PreparePlaybackTransportAsync(
                        session,
                        currentVersion,
                        currentStreamUrl,
                        resumePosition,
                        ct).ConfigureAwait(false);

                    if (_closing ||
                        State == PlayerState.Idle ||
                        !ReferenceEquals(_playbackManager, manager) ||
                        ct.IsCancellationRequested)
                    {
                        return;
                    }

                    ApplyPreparedTransport(reloaded);
                    BeginMpvLoad(reloaded, restorePaused);
                    IsPaused = restorePaused;
                    _mpv.SendScriptMessage("osc-set-play-method", PlayMethod ?? "direct");
                    LogToFile(
                        "state_trace.txt",
                        $"Stream recovery ({reason}) reopened the existing direct session " +
                        $"at mediaPos={resumePosition:F1}");
                    return;
                }
            }

            LogToFile("state_trace.txt", $"Stream recovery ({reason}): restarting session fileId={fileId} pos={resumePosition:F1} audioTrack={audioTrackIndex?.ToString() ?? "auto"}");

            var newSession = await manager.StartReplacementSessionAsync(
                fileId,
                resumePosition,
                forceStartPosition: true,
                audioTrackIndex: audioTrackIndex,
                forceDirectAudioSelection:
                    previousPlan?.TransportKind == PlaybackTransportKind.DirectProgressive,
                previousFinalPosition: resumePosition,
                qualityPreference: manager.QualityPreference,
                ct: ct);
            InvokeSubscribersSafely(SessionStarted, newSession.SessionId, nameof(SessionStarted));
            PlayMethod = newSession.PlayMethod;

            var streamUrl = manager.StreamUrl;
            if (string.IsNullOrEmpty(streamUrl))
                throw new InvalidOperationException("No stream URL returned during premature EOF recovery.");

            var version = Versions.FirstOrDefault(v => v.FileId == fileId)
                ?? throw new InvalidOperationException("The active media version is no longer available.");
            var prepared = await PreparePlaybackTransportAsync(
                newSession,
                version,
                streamUrl,
                resumePosition,
                ct);

            if (_closing || State == PlayerState.Idle || !ReferenceEquals(_playbackManager, manager) || ct.IsCancellationRequested)
                return;

            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, restorePaused);
            IsPaused = restorePaused;
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
            EnterPlaybackTerminalState(ex, resumePosition, reason, restorePaused, manager.CurrentSession ?? session);
        }
    }

    private void EnterPlaybackTerminalState(
        Exception error,
        double position,
        string trigger,
        bool wasPaused,
        PlaybackStartResponse? restartSession = null)
    {
        RetireNativeMenus();
        var loadingOwner = _playbackCts;
        if (_idleRemuxResume.IsReopening) wasPaused = _idleRemuxResume.DesiredPaused;
        _idleRemuxResume.Reset();
        CancelPendingFileLoadTimeout();
        StopPlaybackStallWatchdog();
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        SetSwitchingContent(false);
        _qualitySwitchActive = false;
        IsLoading = false;

        var terminal = error as PlaybackPlanTerminalException;
        var description = terminal != null
            ? PlaybackFailureDescription.Describe(terminal)
            : error is ApiException api && PlaybackFailureDescription.DescribeTransport(api) is { } transport
                ? transport
                : new PlaybackFailurePresentation(
                    "Playback interrupted",
                    string.IsNullOrWhiteSpace(error.Message)
                        ? "Playback could not continue. Try reconnecting or exit the player."
                        : error.Message,
                    CanRetry: true);
        var retainedSession = _playbackManager?.CurrentSession ?? restartSession;
        if (retainedSession == null || _playbackManager == null || _mpv == null)
            description = description with { CanRetry = false };
        ErrorMessage = description.Message;
        // Before first output mpv has no usable video/ASS surface. Sending an
        // OSC error to that surface leaves a blank native popup over the shell.
        // Retire the failed session and use the existing XAML startup error.
        if (!_transportHasFirstOutput && !IsAudiobook)
        {
            ShowStartupPlaybackFailure(description);
            return;
        }
        _playbackTerminalState = new PlaybackTerminalState(
            Math.Max(0, position),
            terminal?.Reason == "direct_recovery_exhausted" ? "viewer-retry" : trigger,
            wasPaused,
            description.CanRetry,
            retainedSession);
        SetConnectionMediaActive(false);

        _mpv?.Pause();
        _mpv?.SendScriptMessage("osc-set-loading", "false");
        _mpv?.SendScriptMessage("osc-set-buffering", "false");
        _mpv?.SendScriptMessage(
            "osc-show-playback-failure",
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["title"] = description.Title,
                ["message"] = description.Message,
                ["can_retry"] = description.CanRetry,
            }));

        if (loadingOwner != null)
            App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
        LogToFile(
            "state_trace.txt",
            $"Playback terminal state: trigger={trigger} position={position:F1} " +
            $"reason={terminal?.Reason ?? error.GetType().Name} retryable={description.CanRetry}");
    }

    private void ShowStartupPlaybackFailure(PlaybackFailurePresentation description)
    {
        var manager = _playbackManager;
        var generation = Volatile.Read(ref _fileLoadGeneration);
        var window = App.MainWindowInstance;
        window?.DispatcherQueue.TryEnqueue(async () =>
        {
            // A queued failure must never close a newer play request.
            if (_closing || State == PlayerState.Idle ||
                generation != Volatile.Read(ref _fileLoadGeneration) ||
                !ReferenceEquals(manager, _playbackManager)) return;
            LogToFile("state_trace.txt", "Startup playback failed before first output; returning to shell error surface.");
            try
            {
                await CloseAsync();
                if (State == PlayerState.Idle && _playbackManager == null && !_switchingContent)
                    window.ShowPlaybackError(description.Title, description.Message);
            }
            catch (Exception ex)
            {
                LogToFile("state_trace.txt", $"Startup failure cleanup error: {ex.GetType().Name}");
                _videoWindow?.Hide();
                SetState(PlayerState.Idle);
                window.ShowPlaybackError(description.Title, description.Message);
            }
        });
    }

    private void ClearPlaybackTerminalState()
    {
        _playbackTerminalState = null;
        _mpv?.SendScriptMessage("osc-clear-playback-failure");
    }

    private async Task RetryPlaybackFromTerminalAsync()
    {
        var terminal = _playbackTerminalState;
        if (terminal == null || !terminal.CanRetry || _closing || State == PlayerState.Idle)
            return;

        _recoveryPauseIntentOverride = terminal.WasPaused;
        ShowNotice(
            "Reconnecting playback",
            "Reopening playback from your current position…",
            "warning");
        LogToFile(
            "state_trace.txt",
            $"Viewer requested playback retry after trigger={terminal.Trigger}");
        await RecoverInterruptedStreamAsync(terminal.Position, terminal.Trigger, terminal.RestartSession)
            .ConfigureAwait(false);
    }

    private double _resumePosition;

    /// <summary>
    /// Phase 3b — start playback of the queued next episode after the user
    /// approved the Playing Next prompt (or the countdown expired).
    /// Clears the next-episode state so the new session has no stale hint.
    /// </summary>
    public async Task ContinuePlayingNextAsync()
    {
        if (IsWatchTogetherPlayback) return;
        if (IsShufflePlayback) { await ContinueShuffleAsync(); return; }
        var nextId = HasNextEpisodeForCurrentPlayback
            ? NextEpisodeContentId
            : null;
        var restoreFullscreen = _restoreFullscreenAfterPostRollContinue
            || State == PlayerState.Fullscreen
            || _videoWindow?.IsFullscreen == true;
        if (string.IsNullOrEmpty(nextId)) return;
        var authority = _activeAuthority;
        var ownerToken = _authorityLifetimeCts.Token;

        // PlayAsync handles old-session cleanup internally with
        // _switchingContent = true set BEFORE _mpv.Stop(), which suppresses
        // the end-file handler. Calling CloseAsync here would over-kill
        // the teardown and break transcode sessions (the proxy + manifest
        // get torn down before the new session can start).
        try
        {
            await PlayAsync(nextId, libraryId: ActiveLibraryId, authority: authority, cancellationToken: ownerToken);
            if (restoreFullscreen && !ownerToken.IsCancellationRequested && ReferenceEquals(authority, _activeAuthority))
                RestoreFullscreenAfterPostRollContinue();
        }
        finally
        {
            _restoreFullscreenAfterPostRollContinue = false;
        }
    }

    public Task PlayPreviousEpisodeAsync()
    {
        if (IsWatchTogetherPlayback || IsShufflePlayback) return Task.CompletedTask;
        var previousId = HasEpisodeNavigationForCurrentPlayback
            ? PreviousEpisodeContentId
            : null;
        return string.IsNullOrEmpty(previousId) ? Task.CompletedTask : PlayAsync(previousId, libraryId: ActiveLibraryId, authority: _activeAuthority, cancellationToken: _authorityLifetimeCts.Token);
    }

    /// <summary>
    /// Phase 3b — user dismissed the Playing Next prompt. Clears the next
    /// episode state and fully tears down the player (same cleanup as a
    /// natural end-of-file without a queued next episode).
    /// </summary>
    public void CancelPlayingNext()
    {
        _shuffleController?.Leave();
        _postRollActive = false;
        _postRollVideoEnded = false;
        _restoreFullscreenAfterPostRollContinue = false;
        ClearNextEpisodeHint();
        InvokeSubscribersSafely(PlaybackEnded, nameof(PlaybackEnded));
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => _ = CloseAsync());
        else
            _ = CloseAsync();
    }

    /// <summary>Reset all next-episode fields to their default null state.</summary>
    private void ClearNextEpisodeHint()
    {
        _playingNextShown = false;
        _postRollActive = false;
        _postRollVideoEnded = false;
        _restoreFullscreenAfterPostRollContinue = false;
        _episodeNavigation.Clear();
        // Also tell the OSC to drop episode navigation and its credits action.
        _mpv?.SendScriptMessage("osc-set-post-roll", "false");
        _mpv?.SendScriptMessage("osc-set-episode-navigation", "false", "false", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode-detail", "null");
    }

    private void PrepareEpisodeNavigationForContent(string contentId)
    {
        _playingNextShown = false;
        _postRollActive = false;
        _postRollVideoEnded = false;
        _restoreFullscreenAfterPostRollContinue = false;
        _episodeNavigation.PrepareFor(contentId);

        // The mpv instance and Lua OSC are reused between files. Clear the
        // previous file's visual/navigation state synchronously before Stop()
        // or LoadFile can emit another event. The current episode lookup will
        // republish the correct state after FILE_LOADED.
        _mpv?.SendScriptMessage("osc-set-post-roll", "false");
        _mpv?.SendScriptMessage("osc-set-episode-navigation", "false", "false", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode", "false");
        _mpv?.SendScriptMessage("osc-set-next-episode-detail", "null");
    }

    private void PrefetchQueuedEpisodeWatchDetail()
    {
        if (!HasNextEpisodeForCurrentPlayback ||
            string.IsNullOrWhiteSpace(NextEpisodeContentId))
            return;

        PrefetchWatchDetailCore(NextEpisodeContentId, _activeAuthority, ActiveLibraryId);
        LogToFile(
            "state_trace.txt",
            $"Prefetching queued episode watch detail: {NextEpisodeContentId}");
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
        if (IsShufflePlayback) return;
        try
        {
            var ownerManager = _playbackManager;
            var wd = ownerManager?.WatchDetail;
            if (wd == null) return;
            if (string.IsNullOrEmpty(wd.SeriesId)) return;
            if (!wd.SeasonNumber.HasValue || !wd.EpisodeNumber.HasValue) return;
            var ownerContentId = wd.ContentId;
            if (string.IsNullOrWhiteSpace(ownerContentId) ||
                !IsEpisodeNavigationLookupCurrent(ownerManager, ownerContentId, ct))
                return;

            var seasonNumbers = new SortedSet<int> { wd.SeasonNumber.Value };
            try
            {
                var seasons = await _catalogApi.GetSeasonsAsync(wd.SeriesId, ActiveLibraryId, ct);
                if (!IsEpisodeNavigationLookupCurrent(ownerManager, ownerContentId, ct)) return;
                if (seasons.Seasons.Any(s => s.SeasonNumber == wd.SeasonNumber.Value + 1))
                    seasonNumbers.Add(wd.SeasonNumber.Value + 1);
                if (seasons.Seasons.Any(s => s.SeasonNumber == wd.SeasonNumber.Value - 1))
                    seasonNumbers.Add(wd.SeasonNumber.Value - 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogToFile("state_trace.txt", $"AutoDetectNextEpisode: seasons lookup failed: {ex.Message}");
            }

            // Current, previous, and next-season episode lists are independent.
            // Fetch them together so the OSC's previous/next buttons and the
            // post-roll metadata do not wait on up to three serial round trips.
            var episodeTasks = seasonNumbers
                .Select(seasonNumber => _catalogApi.GetEpisodesAsync(wd.SeriesId, seasonNumber, ActiveLibraryId, ct))
                .ToArray();
            var episodeResponses = await Task.WhenAll(episodeTasks);
            if (!IsEpisodeNavigationLookupCurrent(ownerManager, ownerContentId, ct)) return;
            var allEpisodes = episodeResponses
                .SelectMany(response => response.Episodes ?? [])
                .ToList();

            if (allEpisodes.Count == 0) return;

            var orderedEpisodes = allEpisodes.OrderBy(ep => ep.SeasonNumber).ThenBy(ep => ep.EpisodeNumber).ToList();
            var currentIndex = orderedEpisodes.FindIndex(ep => ep.ContentId == wd.ContentId ||
                (ep.SeasonNumber == wd.SeasonNumber.Value && ep.EpisodeNumber == wd.EpisodeNumber.Value));
            var previousContentId = currentIndex > 0 ? orderedEpisodes[currentIndex - 1].ContentId : null;

            int currentNumber = wd.EpisodeNumber.Value;
            var next = NextEpisodeResolver.FindNextEpisode(
                orderedEpisodes,
                wd.SeasonNumber.Value,
                currentNumber,
                wd.ContentId);
            var nextTarget = next == null
                ? null
                : new EpisodeNavigationTarget(
                    next.ContentId,
                    string.IsNullOrEmpty(next.Title)
                        ? $"S{next.SeasonNumber}:E{next.EpisodeNumber}"
                        : $"S{next.SeasonNumber}:E{next.EpisodeNumber} \u2014 {next.Title}",
                    wd.SeriesTitle,
                    next.StillUrl,
                    next.Overview,
                    next.AirDate,
                    Math.Clamp(next.Runtime, 0, int.MaxValue / 60) * 60,
                    next.SeasonNumber,
                    next.EpisodeNumber,
                    next.UserData?.Played,
                    next.UserData?.IsInProgress,
                    next.UserData?.PositionSeconds,
                    next.StillIsEpisodeStill);

            if (!IsEpisodeNavigationLookupCurrent(ownerManager, ownerContentId, ct) ||
                !_episodeNavigation.TrySetResolved(ownerContentId, previousContentId, nextTarget))
            {
                LogToFile(
                    "state_trace.txt",
                    $"AutoDetectNextEpisode: discarded stale result for {ownerContentId}");
                return;
            }

            _mpv?.SendScriptMessage(
                "osc-set-episode-navigation",
                "true",
                previousContentId != null ? "true" : "false",
                next != null ? "true" : "false");
            if (next == null)
            {
                _mpv?.SendScriptMessage("osc-set-next-episode-detail", "null");
                _mpv?.SendScriptMessage("osc-set-next-episode", "false");
                LogToFile("state_trace.txt", $"AutoDetectNextEpisode: no next episode after S{wd.SeasonNumber} E{currentNumber}");
                return;
            }

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

    private bool IsEpisodeNavigationLookupCurrent(
        PlaybackManager? ownerManager,
        string ownerContentId,
        CancellationToken ct)
        => !ct.IsCancellationRequested &&
           !_closing &&
           ReferenceEquals(_playbackManager, ownerManager) &&
           string.Equals(ContentId, ownerContentId, StringComparison.Ordinal) &&
           string.Equals(ownerManager?.WatchDetail?.ContentId, ownerContentId, StringComparison.Ordinal);

    private void ResolveInitialSubtitleSelection(
        WatchDetailResponse watchDetail,
        FileVersion version,
        PlaybackStartResponse session,
        int? selectedAudioTrackIndex,
        int? explicitSelection,
        SubtitleTrackSignature? explicitSignature)
    {
        SubtitleTrackInfo? selectedTrack = null;
        _pendingInitialSubtitleFileId = session.MediaFileId;
        _pendingAutomaticSubtitleSelection = null;

        if (explicitSignature != null)
        {
            selectedTrack = session.SubtitleUrls.FirstOrDefault(track =>
                !track.Unplayable &&
                (string.IsNullOrWhiteSpace(explicitSignature.Source) ||
                    string.Equals(track.Source, explicitSignature.Source, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(explicitSignature.Language) ||
                    string.Equals(track.Language, explicitSignature.Language, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(explicitSignature.Codec) ||
                    string.Equals(track.Codec, explicitSignature.Codec, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(explicitSignature.Label) ||
                    string.Equals(track.Label, explicitSignature.Label, StringComparison.OrdinalIgnoreCase)) &&
                track.Forced == explicitSignature.Forced &&
                track.HearingImpaired == explicitSignature.HearingImpaired);

            // Older playback responses may normalize downloaded sidecars to
            // "external". Keep the signature strict first, then allow only
            // that known source alias while preserving every track attribute.
            if (selectedTrack == null &&
                string.Equals(explicitSignature.Source, "downloaded", StringComparison.OrdinalIgnoreCase))
            {
                selectedTrack = session.SubtitleUrls.FirstOrDefault(track =>
                    !track.Unplayable &&
                    string.Equals(track.Source, "external", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(explicitSignature.Language) ||
                        string.Equals(track.Language, explicitSignature.Language, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(explicitSignature.Codec) ||
                        string.Equals(track.Codec, explicitSignature.Codec, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(explicitSignature.Label) ||
                        string.Equals(track.Label, explicitSignature.Label, StringComparison.OrdinalIgnoreCase)) &&
                    track.Forced == explicitSignature.Forced &&
                    track.HearingImpaired == explicitSignature.HearingImpaired);
            }
        }
        else if (explicitSelection is >= 0)
        {
            var requested = version.SubtitleTracks?.ElementAtOrDefault(explicitSelection.Value);
            if (requested != null)
            {
                // Watch-detail positions and combined playback indices are
                // different namespaces. Use an embedded stream identity when
                // supplied, otherwise require a unique source/signature match.
                var requestedSource = requested.External == true ? "external" : "embedded";
                var sourceInventory = session.SubtitleUrls.Where(track =>
                    string.Equals(track.Source, requestedSource, StringComparison.OrdinalIgnoreCase)).ToArray();
                var identities = requested.External != true && requested.Index is int streamIndex
                    ? sourceInventory.Where(track => track.StreamIndex == streamIndex).Take(2).ToArray()
                    : [];
                if (identities.Length > 0)
                {
                    // An unavailable or ambiguous identity cannot be replaced
                    // with a different row that happens to share its metadata.
                    if (identities.Length == 1 && !identities[0].Unplayable) selectedTrack = identities[0];
                }
                else
                {
                    var title = string.IsNullOrWhiteSpace(requested.Title) ? requested.EmbeddedTitle : requested.Title;
                    var matches = sourceInventory.Where(track => !track.Unplayable &&
                        string.Equals(track.Language, requested.Language, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(track.Codec, requested.Codec, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(title) ||
                            string.Equals(track.NativeTitle ?? track.Label, title, StringComparison.OrdinalIgnoreCase)) &&
                        track.Forced == (requested.Forced == true) &&
                        track.HearingImpaired == (requested.HearingImpaired == true)).Take(2).ToArray();
                    if (matches.Length == 1) selectedTrack = matches[0];
                }
            }
        }
        else if (explicitSelection == null)
        {
            var mode = watchDetail.EffectiveSubtitleMode;
            var preferredLanguage = watchDetail.EffectiveSubtitleLanguage;
            var showForced = watchDetail.EffectiveShowForcedSubtitles;
            // Effective response values are authoritative. If omitted, use the
            // active authority's available cached profile; handoff profiles may
            // be absent and this cache is not a fresh settings read.
            if (_authService.SelectedProfile is { } profile)
            {
                if (string.IsNullOrWhiteSpace(mode)) mode = profile.SubtitleMode;
                if (preferredLanguage == null && !string.IsNullOrWhiteSpace(profile.SubtitleLanguage))
                    preferredLanguage = profile.SubtitleLanguage;
                showForced ??= profile.ShowForcedSubtitles;
            }

            string? audioLanguage = null;
            var audioTracks = version.AudioTracks ?? [];
            var audioIndex = session.ProtocolVersion >= 3 ? session.AudioTrackIndex :
                selectedAudioTrackIndex ?? version.EffectiveAudioTrackIndex ?? audioTracks.FindIndex(track => track.Default);
            if (audioIndex < 0) audioIndex = 0;
            if (audioIndex < audioTracks.Count)
                audioLanguage = audioTracks[audioIndex].Language;
            if (string.IsNullOrWhiteSpace(audioLanguage) &&
                version.EffectiveAudioTrackIndex == audioIndex)
                audioLanguage = version.EffectiveAudioLanguage;

            var automatic = new SubtitleAutoSelect.Options(
                Mode: SubtitleAutoSelect.NormalizeSubtitleMode(mode),
                Tracks: SubtitleAutoSelect.BuildCandidates(session.SubtitleUrls),
                PreferredLanguage: preferredLanguage,
                AudioLanguage: audioLanguage,
                ProfileLanguage: _authService.SelectedProfile?.Language,
                ShowForcedSubtitles: showForced ?? true,
                PreferredTrackSignature: watchDetail.EffectiveSubtitleTrackSignature);
            _pendingAutomaticSubtitleSelection = automatic;
            var selectedIndex = SubtitleAutoSelect.Resolve(automatic);
            if (selectedIndex is int index)
                selectedTrack = session.SubtitleUrls.FirstOrDefault(track => track.Index == index);
        }

        if (selectedTrack == null && (explicitSelection is >= 0 || explicitSignature != null))
        {
            // Refusing a missing/unavailable pre-play choice is not an explicit
            // viewer Off. Retain the accepted plan and its automatic provenance.
            _pendingSubtitleSelection = null;
            _pendingInitialServerSubtitleIndex = null;
            ShowSubtitleSelectionUnavailable();
            return;
        }

        if (explicitSelection == -1 || selectedTrack == null || selectedTrack.Unplayable)
        {
            _pendingSubtitleSelection = -1;
            _pendingInitialServerSubtitleIndex = -1;
            if (session.ProtocolVersion >= 3) _playbackManager?.RecordLocalSubtitleSelection(null,
                isAutomatic: explicitSelection == null && explicitSignature == null);
            return;
        }

        _pendingInitialServerSubtitleIndex = selectedTrack.Index;
        if (session.ProtocolVersion >= 3) _playbackManager?.RecordLocalSubtitleSelection(selectedTrack.Index,
            isAutomatic: explicitSelection == null && explicitSignature == null);
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

    private async Task<PlaybackStartResponse> ReconcileInitialSubtitlePlanAsync(
        PlaybackStartResponse session, CancellationToken ct)
    {
        var manager = _playbackManager;
        if (manager == null || !ReferenceEquals(manager.CurrentSession, session) ||
            !session.IsSubtitleBurnedIn || _pendingInitialServerSubtitleIndex is not int requested)
            return session;
        var granted = session.SubtitleUrls.FirstOrDefault(t =>
            !string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)
                ? t.TrackId == session.SelectedSubtitleTrackId : t.Index == session.SelectedSubtitleTrackIndex);
        if (requested >= 0 && granted?.Index == requested) return session;

        // A local sid cannot remove server-composited pixels. Resolve pre-play
        // choices using the accepted inventory, and replan before the first load.
        var initialIntent = manager.CaptureLocalSubtitleIntent();
        PlaybackStartResponse replacement;
        try
        {
            replacement = await manager.ReplanSubtitleAsync(requested < 0 ? null : requested, session.Position, ct,
                isAutomatic: _pendingAutomaticSubtitleSelection != null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(manager.CurrentSession, session))
        {
            // The subtitle request failed before adoption, not the playable start.
            // Drop its tentative intent and load the accepted automatic grant.
            LogToFile("player_subtitle_error.txt", ex.ToString());
            lock (_subtitleTrackStateLock)
            {
                // A newer viewer choice owns its own execution and feedback.
                if (manager.CaptureLocalSubtitleIntent() != initialIntent) return session;
                manager.RestoreLocalSubtitleIntent(new(false, null, session.MediaFileId, true));
                _pendingSubtitleSelection = null;
                _pendingAutomaticSubtitleSelection = null;
                _pendingInitialServerSubtitleIndex = manager.SubtitleSelectionForLoad;
                _activeSubtitleServerIndex = manager.SubtitleSelectionForLoad;
                ShowSubtitleSelectionUnavailable();
            }
            return session;
        }
        ct.ThrowIfCancellationRequested();
        if (ReferenceEquals(manager, _playbackManager) && ReferenceEquals(manager.CurrentSession, replacement))
        {
            _pendingInitialServerSubtitleIndex = manager.SubtitleSelectionForLoad;
            _pendingInitialSubtitleFileId = replacement.MediaFileId;
            _subtitleReplanAttempt = new(replacement, manager.SubtitleSelectionForLoad);
        }
        return replacement;
    }

    /// <summary>Moves the still-playing episode into the WebUI-sized post-roll preview.</summary>
    public void EnterPostRollPreview()
    {
        if (IsAudiobook || _videoWindow == null || _postRollVideoEnded)
            return;

        if (State == PlayerState.Fullscreen || _videoWindow.IsFullscreen)
        {
            _restoreFullscreenAfterPostRollContinue = true;
            _videoWindow.ExitFullscreen();
            SetState(PlayerState.Expanded);
        }

        _videoWindow.SetCursorVisible(true);
        _mpv?.SendScriptMessage("osc-set-post-roll", "true");
        _mpv?.SendScriptMessage("osc-set-visibility", "false");
        _videoWindow.EnterPostRollPreview();
    }

    /// <summary>Hides the preview after true EOF while the post-roll screen remains.</summary>
    public void FinishPostRollPreview()
    {
        _postRollVideoEnded = true;
        if (_videoWindow?.IsFullscreen == true || State == PlayerState.Fullscreen)
        {
            _restoreFullscreenAfterPostRollContinue = true;
            _videoWindow?.ExitFullscreen();
            SetState(PlayerState.Expanded);
        }
        _videoWindow?.SetCursorVisible(true);
        _videoWindow?.Hide();
    }

    private void RestoreFullscreenAfterPostRollContinue()
    {
        if (IsAudiobook || _videoWindow == null || State == PlayerState.Idle)
            return;

        void Restore()
        {
            if (IsAudiobook || _videoWindow == null || State == PlayerState.Idle)
                return;

            _videoWindow.SetCursorVisible(true);
            _videoWindow.EnterFullscreen(activate: false);
            var restored = _videoWindow.SynchronizeFullscreenState();
            SetState(restored ? PlayerState.Fullscreen : PlayerState.Expanded);
            PublishFullscreenVisualState(restored);
            LogToFile(
                "state_trace.txt",
                restored
                    ? "Restored fullscreen after Playing Next transition"
                    : "Fullscreen restore did not reach the monitor bounds; controls synchronized to windowed state");
        }

        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher?.HasThreadAccess == true)
            Restore();
        else if (dispatcher != null)
            dispatcher.TryEnqueue(Restore);
        else
            Restore();
    }

    private void ReturnFromPostRollPreview()
    {
        if (!_postRollActive || _postRollVideoEnded)
            return;

        _postRollActive = false;
        _videoWindow?.Show();
        _mpv?.SendScriptMessage("osc-set-post-roll", "false");
        _mpv?.SendScriptMessage("osc-set-visibility", "true");
        InvokeSubscribersSafely(PostRollReturnRequested, nameof(PostRollReturnRequested));
    }

    /// <summary>
    /// Suppress unverified native subtitles before loading. FILE_LOADED applies
    /// the server choice against the actual inventory of the replacement file.
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
                // Server/UI ordinals do not identify a demuxed subtitle. Keep
                // the outgoing/default subtitle off until FILE_LOADED resolves
                // the requested track against that file's actual inventory.
                _mpv.SetProperty("sid", "no");
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

    private void SetTitleFromWatchDetail(WatchDetailResponse watchDetail)
    {
        if (watchDetail.SeasonNumber.HasValue && watchDetail.EpisodeNumber.HasValue)
        {
            // Match VideoPlayer.tsx's HUD copy exactly: series title on the
            // primary line, followed by "S2 · E10 — Episode title". The
            // previous native-only "Series - S02E10" title made the same
            // episode visibly disagree between WebUI and desktop.
            Title = watchDetail.SeriesTitle ?? watchDetail.Title;
            Subtitle = $"S{watchDetail.SeasonNumber.Value} · E{watchDetail.EpisodeNumber.Value}" +
                (string.IsNullOrWhiteSpace(watchDetail.Title)
                    ? string.Empty
                    : $" \u2014 {watchDetail.Title}");
        }
        else
        {
            Title = watchDetail.Title;
            Subtitle = watchDetail.Year > 0 ? watchDetail.Year.ToString() : null;
        }
    }

    private FileVersion? SelectVersion(WatchDetailResponse watchDetail, int? fileId, string qualityPreference)
    {
        Versions = watchDetail.Versions?.ToList() ?? [];
        var versions = watchDetail.Versions ?? new List<FileVersion>();

        FileVersion? bestVersion = null;
        if (fileId.HasValue)
            bestVersion = versions.FirstOrDefault(v => v.FileId == fileId.Value)
                ?? watchDetail.PlaybackVariants.SelectMany(variant => variant.Parts).SelectMany(part => part.Versions).FirstOrDefault(version => version.FileId == fileId.Value);
        bestVersion ??= _playbackManager!.SelectBestVariantVersion(
            versions,
            watchDetail.PlaybackVariants,
            qualityPreference: qualityPreference,
            userData: watchDetail.UserData,
            preferredEditionKey: watchDetail.EffectiveVersionEditionKey);
        return bestVersion;
    }

    private double DetermineStartPosition(WatchDetailResponse watchDetail, bool fromStart)
    {
        double startPosition = 0;
        // A completed item's watched flag remains latched while a rewatch is in progress.
        // Match the WebUI: any nonzero position is an active resume point, even when
        // Played is still true. Only an explicit "play from beginning" may discard it.
        if (!fromStart && watchDetail.UserData?.PositionSeconds > 0)
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
        bool CanSeekAnywhere);

    /// <summary>
    /// Converts the semantic playback decision returned by /playback/start into
    /// the concrete transport mpv must open. A remux can be either a sequential
    /// progressive stream or HLS, so play_method alone is never treated as the
    /// transport decision.
    /// </summary>
    private Task<PreparedPlaybackTransport> PreparePlaybackTransportAsync(
        PlaybackStartResponse session,
        FileVersion version,
        string remoteStreamUrl,
        double mediaStartSeconds,
        CancellationToken ct = default)
    {
        var plan = PlaybackTransportPlanner.Plan(session);
        var knownDuration = GetKnownDuration(session, version);
        mediaStartSeconds = Math.Max(0, mediaStartSeconds);

        var localUrl = plan.IsHls
            ? PrepareHlsStreamForMpv(NormalizePlaybackUrl(remoteStreamUrl))
            : PrepareDirectStreamForMpv(
                remoteStreamUrl,
                session.PlayMethod,
                supportsRanges: session.CanSeekAnywhere);
        return Task.FromResult(new PreparedPlaybackTransport(
            plan,
            localUrl,
            MpvLoadStartSeconds: plan.TransportKind == PlaybackTransportKind.DirectProgressive
                ? Math.Max(0, PlaybackTimeline.ToPlayerTime(mediaStartSeconds, session.TimelineOffsetSeconds))
                : Math.Max(0, session.PlayerStartSeconds),
            ResumeAfterLoadSeconds: 0,
            TimelineOffsetSeconds: Math.Max(0, session.TimelineOffsetSeconds),
            DurationSeconds: knownDuration,
            CanSeekAnywhere: session.CanSeekAnywhere));
    }

    private static double? GetKnownDuration(PlaybackStartResponse session, FileVersion version)
    {
        if (session.DurationSeconds is > 0)
            return session.DurationSeconds;
        if (version.Duration > 0)
            return version.Duration;
        return null;
    }

    private string NormalizePlaybackUrl(string pathOrUrl)
        => PlaybackDeliveryUrl.Resolve(_apiClient.BaseUrl, pathOrUrl);

    private void ApplyPreparedTransport(PreparedPlaybackTransport prepared)
    {
        _activeTransportPlan = prepared.Plan;
        _timelineOffsetSeconds = prepared.TimelineOffsetSeconds;
        _transportDurationSeconds = prepared.DurationSeconds;
        _canSeekAnywhere = prepared.CanSeekAnywhere;
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
            ? PlaybackTimeline.ToMediaTime(_mpv.Position, _timelineOffsetSeconds) +
                (IsAudiobook ? _audiobookPartOffsetSeconds : 0)
            : Position;

    private double CurrentMediaDuration
    {
        get
        {
            if (IsAudiobook && _audiobookTotalDurationSeconds > 0)
                return _audiobookTotalDurationSeconds;
            if (_mpv == null)
                return Duration;
            var rawDuration = _mpv.Duration;
            return PlaybackTimeline.ResolveMediaDuration(
                rawDuration,
                _timelineOffsetSeconds,
                _transportDurationSeconds);
        }
    }

    private string PrepareDirectStreamForMpv(string remoteStreamUrl, string? playMethod, bool supportsRanges)
    {
        if (string.IsNullOrWhiteSpace(remoteStreamUrl))
            throw new InvalidOperationException("No direct stream URL available.");

        StopDirectStreamProxy();
        StopHlsProxy();

        if (supportsRanges && string.Equals(playMethod, "direct", StringComparison.OrdinalIgnoreCase) &&
            MpvPlayer.CanOpenSignedDirectStream(remoteStreamUrl))
        {
            LogToFile("state_trace.txt", "Direct stream via native mpv HTTP: bounded ranges, no local relay");
            return remoteStreamUrl;
        }

        var streamingApi = _apiClient;
        _directStreamProxy = new DirectStreamProxy(remoteStreamUrl, () => streamingApi.AccessToken, supportsRanges);
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
        var streamingApi = _apiClient;
        _hlsProxy = new HlsProxy(remoteManifestUrl, () => streamingApi.AccessToken);
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
        if (_mpv != null)
        {
            SendThemeToOsc();
            return;
        }

        var mainWindow = App.MainWindowInstance;
        var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);

        _videoWindow = new MpvVideoWindow();
        _videoWindow.Create(parentHwnd);
        WireVideoWindowEvents();

        _mpv = new MpvPlayer();
        _mpv.UpscalingStatusChanged += status => LogToFile("upscaling_log.txt", status);
        var upscalingSettings = _settingsService.Load();
        _mpv.InitializeWithWindow(_videoWindow.Hwnd, upscalingMode: VideoUpscalingPolicy.ResolveMode(
            upscalingSettings.VideoUpscalingMode, upscalingSettings.NvidiaVideoUpscaling));
        _videoWindow.SetMpv(_mpv);
        WireMpvEvents();
        SendThemeToOsc();

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
        PushResolvedSubtitlePreferences();
    }

    private void SendThemeToOsc()
    {
        if (_mpv == null)
            return;

        try
        {
            Windows.UI.Color? accent = null;
            if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentColor", out var colorValue) &&
                colorValue is Windows.UI.Color color)
            {
                accent = color;
            }
            else if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("AccentBrush", out var brushValue) &&
                     brushValue is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            {
                accent = brush.Color;
            }

            if (accent is { } resolved)
            {
                _mpv.SendScriptMessage(
                    "osc-set-theme",
                    $"{resolved.R:X2}{resolved.G:X2}{resolved.B:X2}");
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Send player theme failed: {ex.Message}");
        }
    }

    private void ResetFailedMpvInitialization()
    {
        try { UnwireMpvEvents(); } catch { }
        try { _mpv?.Dispose(); } catch { }
        _mpv = null;
        RetireNativeMenus();
        try { _videoWindow?.Dispose(); } catch { }
        _videoWindow = null;
    }

    private void UnwireMpvEvents()
    {
        if (_mpv == null) return;
        if (_mpvPositionHandler != null) _mpv.PositionChanged -= _mpvPositionHandler;
        if (_mpvDurationHandler != null) _mpv.DurationChanged -= _mpvDurationHandler;
        if (_mpvPauseHandler != null) _mpv.PauseChanged -= _mpvPauseHandler;
        if (_mpvBufferingHandler != null) _mpv.BufferingChanged -= _mpvBufferingHandler;
        if (_mpvFileLoadedHandler != null) _mpv.FileLoaded -= _mpvFileLoadedHandler;
        if (_mpvPlaybackRestartedHandler != null) _mpv.PlaybackRestarted -= _mpvPlaybackRestartedHandler;
        if (_mpvEofReachedHandler != null) _mpv.EofReached -= _mpvEofReachedHandler;
        if (_mpvPlaybackEndedHandler != null) _mpv.PlaybackEnded -= _mpvPlaybackEndedHandler;
        if (_mpvPlaybackErrorHandler != null) _mpv.PlaybackError -= _mpvPlaybackErrorHandler;
        if (_mpvDirectStreamErrorHandler != null) _mpv.DirectStreamError -= _mpvDirectStreamErrorHandler;
        if (_mpvErrorHandler != null) _mpv.Error -= _mpvErrorHandler;
        _mpv.ScriptMessageReceived -= OnScriptMessage;
    }

    private async Task EnsureSubtitleAppearanceLoadedAsync(CancellationToken ct)
    {
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var generation = Volatile.Read(ref _playRequestGeneration);
        await _subtitleAppearanceLoadGate.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(context)
                || generation != Volatile.Read(ref _playRequestGeneration)) return;
            ObserveSubtitlePreferences();
            var state = await authority.SettingsApi.Subtitles.LoadAsync(context, ct);
            if (!ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(context)
                || generation != Volatile.Read(ref _playRequestGeneration)) return;
            _subtitlePreferences = state;
            _subtitlePreferenceAuthority = authority;
            _subtitleAppearance = state.Appearance.Clone();
            if (_subtitleOffsetDraft == null && !_subtitleOffsetSessionOverride) _subtitleOffsetMs = state.OffsetMs;
            if (_subtitleOffsetDraft == null) SubtitlePreferenceError = null;
            PublishSubtitlePreferenceStatus();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !ReferenceEquals(authority, _activeAuthority)
            || !authority.ApiClient.IsCurrentContext(context) || generation != Volatile.Read(ref _playRequestGeneration)) { }
        catch (Exception)
        {
            if (ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context)
                && generation == Volatile.Read(ref _playRequestGeneration))
            { SubtitlePreferenceError = "Subtitle preferences could not load. Reopen playback to retry."; PublishSubtitlePreferenceStatus(); }
        }
        finally { _subtitleAppearanceLoadGate.Release(); }
    }

    private async Task ApplySubtitleAppearanceWhenLoadedAsync(Task loadTask, PlaybackAuthorityScope authority,
        ApiRequestContext context, long generation)
    {
        try
        {
            await loadTask;
            if (ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context)
                && generation == Volatile.Read(ref _playRequestGeneration) && !_closing) PushResolvedSubtitlePreferences();
        }
        catch (OperationCanceledException) { }
    }

    private void HandleMpvEndSignal(string trigger)
    {
        LogToFile("state_trace.txt", $"{trigger} fired: _switchingContent={_switchingContent} _qualitySwitchActive={_qualitySwitchActive} _prematureEofRecoveryActive={_prematureEofRecoveryActive} _closing={_closing} nextEpisode={NextEpisodeContentId ?? "none"} State={State} thread={Environment.CurrentManagedThreadId}");
        if (_switchingContent || _qualitySwitchActive || _prematureEofRecoveryActive || _playbackTerminalState != null)
        {
            LogToFile("state_trace.txt", $"  -> Suppressed {trigger} (switching content or terminal failure)");
            return;
        }

        if (_closing || State == PlayerState.Idle)
        {
            LogToFile("state_trace.txt", $"  -> Suppressed {trigger} (closing or idle)");
            return;
        }

        var pos = CurrentMediaPosition;
        var dur = CurrentMediaDuration;
        if (IsAudiobook)
        {
            var activeFileId = _playbackManager?.CurrentSession?.MediaFileId;
            var parts = BuildAudiobookParts();
            var activeIndex = -1;
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index].Version.FileId == activeFileId)
                {
                    activeIndex = index;
                    break;
                }
            }

            if (activeIndex >= 0 && activeIndex + 1 < parts.Count && !string.IsNullOrWhiteSpace(ContentId))
            {
                var next = parts[activeIndex + 1];
                SetSwitchingContent(true);
                LogToFile("state_trace.txt", $"  -> Audiobook part {activeIndex + 1} complete; continuing file {next.Version.FileId}");
                _ = ContinueAudiobookPartAsync(ContentId, next.Version.FileId, next.Start);
                return;
            }

            Position = dur > 0 ? dur : pos;
            IsPaused = true;
            _mpv?.Pause();
            _ = ReportAudiobookProgressAsync(Position, force: true);
            InvokeSubscribersSafely(PositionChanged, Position, nameof(PositionChanged));
            InvokeSubscribersSafely(PauseChanged, true, nameof(PauseChanged));
            LogToFile("state_trace.txt", "  -> Audiobook complete; retaining the listening surface at the end");
            return;
        }

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
                LogToFile("state_trace.txt", $"  -> Premature {trigger} stuck at pos={pos:F1} (streak={_prematureEofStreak}). Waiting for viewer action.");
                _prematureEofStreak = 0;
                _prematureEofLastAttemptMs = 0;
                EnterPlaybackTerminalState(
                    new InvalidOperationException(
                        "Playback stalled and couldn't resume. The stream may be unavailable at this position."),
                    pos,
                    trigger,
                    CaptureUserPausedState());
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

        if (IsWatchTogetherPlayback)
        {
            // Room lifecycle owns completion. Keep reporting the final position
            // until the room snapshot returns to the lobby; no local episode autoplay.
            SetPaused(true);
            return;
        }
        if (ContentId != null && PlaybackPartSequence.NextFileId(WatchDetail, ActiveMediaFileId) is int nextPartFileId)
        {
            SetSwitchingContent(true);
            _ = ContinueVideoPartAsync(ContentId, nextPartFileId);
            return;
        }
        var isSeriesEpisode = !string.IsNullOrWhiteSpace(WatchDetail?.SeriesId) || IsShufflePlayback;
        if (isSeriesEpisode)
        {
            if (_playingNextShown)
            {
                if (!_postRollVideoEnded)
                {
                    LogToFile("state_trace.txt", "  -> Early post-roll reached true media end");
                    _postRollVideoEnded = true;
                    InvokeSubscribersSafely(ShowPlayingNextRequested, true, nameof(ShowPlayingNextRequested));
                }
                else
                {
                    LogToFile("state_trace.txt", "  -> Series post-roll already ended");
                }
                return;
            }

            LogToFile("state_trace.txt", $"  -> Series post-roll requested at media end ({trigger})");
            _playingNextShown = true;
            RetireNativeMenus();
            _postRollActive = true;
            _postRollVideoEnded = true;
            PrefetchQueuedEpisodeWatchDetail();
            InvokeSubscribersSafely(ShowPlayingNextRequested, true, nameof(ShowPlayingNextRequested));
            return;
        }

        // Current WebUI exits movies and other standalone video directly back
        // to their detail surface. Post-roll is a series-episode experience.
        LogToFile("state_trace.txt", $"  -> Standalone playback completed ({trigger}); closing player");
        InvokeSubscribersSafely(PlaybackEnded, nameof(PlaybackEnded));
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        if (dispatcher != null)
            dispatcher.TryEnqueue(() => _ = CloseAsync());
        else
            _ = CloseAsync();
    }

    private async Task ContinueAudiobookPartAsync(string contentId, int fileId, double absoluteStart)
    {
        var authority = _activeAuthority;
        var libraryId = ActiveLibraryId;
        var ownerToken = _authorityLifetimeCts.Token;
        await ReportAudiobookProgressAsync(absoluteStart, force: true).ConfigureAwait(false);
        if (_closing || ownerToken.IsCancellationRequested || !ReferenceEquals(_activeAuthority, authority)) return;
        await PlayAsync(contentId, fileId: fileId, startPositionOverride: absoluteStart, libraryId: libraryId, authority: authority, cancellationToken: ownerToken).ConfigureAwait(false);
    }

    private static bool IsAtMediaEnd(double position, double duration)
    {
        if (duration <= 0 || position < 0)
            return false;

        // Catalog/plan duration is rounded independently from the final
        // decoded timestamp. A real 3700.1s file was advertised as 3702s;
        // the percentage-based tolerance retried that healthy EOF forever.
        // Bound rounding allowance to three seconds and 2% for short clips.
        var tolerance = Math.Min(duration * 0.02, 3.0);
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

            var mediaPosition = PlaybackTimeline.ToMediaTime(pos, _timelineOffsetSeconds) +
                (IsAudiobook ? _audiobookPartOffsetSeconds : 0);
            Position = mediaPosition;
            UpdatePlaybackManagerPosition(mediaPosition, IsPaused);
            InvokeSubscribersSafely(PositionChanged, mediaPosition, nameof(PositionChanged));
            if (IsAudiobook)
                _ = ReportAudiobookProgressAsync(mediaPosition);

            if (!_playingNextShown &&
                !IsWatchTogetherPlayback &&
                !ShuffleAwaitsLaterPart &&
                !_closing &&
                !_switchingContent &&
                !IsAudiobook &&
                (!string.IsNullOrWhiteSpace(WatchDetail?.SeriesId) || IsShufflePlayback) &&
                CurrentMediaDuration > 0 &&
                mediaPosition > 0 &&
                CurrentMediaDuration - mediaPosition <= 30)
            {
                _playingNextShown = true;
                RetireNativeMenus();
                _postRollActive = true;
                _postRollVideoEnded = false;
                PrefetchQueuedEpisodeWatchDetail();
                LogToFile("state_trace.txt", $"Entering early series post-roll at {mediaPosition:F1}/{CurrentMediaDuration:F1}");
                InvokeSubscribersSafely(ShowPlayingNextRequested, false, nameof(ShowPlayingNextRequested));
            }

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

            var mediaDuration = IsAudiobook && _audiobookTotalDurationSeconds > 0
                ? _audiobookTotalDurationSeconds
                : PlaybackTimeline.ResolveMediaDuration(
                    dur,
                    _timelineOffsetSeconds,
                    _transportDurationSeconds);
            Duration = mediaDuration;
            InvokeSubscribersSafely(DurationChanged, mediaDuration, nameof(DurationChanged));
        };
        _mpv.DurationChanged += _mpvDurationHandler;

        _mpvPauseHandler = (paused) =>
        {
            if (!_idleRemuxResume.IsReopening && !paused && TryReopenIdleRemux()) return;
            _idleRemuxResume.ObservePause(paused, _switchingContent || IsLoading, DateTimeOffset.UtcNow);
            IsPaused = paused;
            SynchronizeConnectionMediaActivity(paused);
            UpdateDisplayWakeLock(!paused && !_closing && State != PlayerState.Idle);
            if (IsAudiobook)
                _audiobookPausedAt = paused ? DateTimeOffset.UtcNow : null;
            var mediaPosition = PlaybackRecoveryPosition;
            UpdatePlaybackManagerPosition(mediaPosition, paused);
            if (!_closing && !_switchingContent && State != PlayerState.Idle)
                _ = ReportSeekProgressAsync(mediaPosition, paused);
            InvokeSubscribersSafely(PauseChanged, paused, nameof(PauseChanged));
        };
        _mpv.PauseChanged += _mpvPauseHandler;

        _mpvBufferingHandler = (buffering) =>
        {
            SynchronizeConnectionMediaActivity(buffering: buffering);
            LogToFile("state_trace.txt", $"BufferingForCache changed: {buffering} pos={_mpv?.Position:F1} paused={_mpv?.IsPaused}");
            _mpv?.SendScriptMessage("osc-set-buffering", buffering ? "true" : "false");
            InvokeSubscribersSafely(BufferingChanged, buffering, nameof(BufferingChanged));
        };
        _mpv.BufferingChanged += _mpvBufferingHandler;

        _mpvFileLoadedHandler = () =>
        {
            var wasPrematureEofRecovery = _prematureEofRecoveryActive;
            var restorePaused = IsWatchTogetherPlayback ? _roomPlaybackPaused : _restorePausedAfterLoad == true;
            // v3 original_http is byte-for-byte source media: the server's
            // selected audio ordinal must be applied locally, before unpausing
            // or publishing the selected language to the OSC.
            try
            {
                if (_mpv is { } mpv && _playbackManager?.CurrentSession is { } session &&
                    _activeTransportPlan is { } plan)
                {
                    var audioIds = mpv.GetAudioTrackIds();
                    NativeAudioSelection.Apply(plan.TransportKind, _playbackManager?.AudioSelectionForLoad ?? session.AudioTrackIndex,
                        audioIds, mpv.SetAudioTrack, () => (int)mpv.GetPropertyDouble("aid"));
                    LogToFile("state_trace.txt", $"Audio selection verified: sourceIndex={session.AudioTrackIndex} " +
                        $"mpvAid={mpv.GetPropertyDouble("aid")} transport={plan.TransportKind} audioTracks={audioIds.Count}");
                }
            }
            catch (Exception ex)
            {
                EnterPlaybackTerminalState(ex, CurrentMediaPosition, "audio-selection-failed", restorePaused);
                return;
            }
            ClearPlaybackTerminalState();
            ReconcilePlaybackSurfaceStateAfterLoad();
            CancelPendingFileLoadTimeout();
            Interlocked.Exchange(ref _consecutiveFileLoadTimeouts, 0);
            _qualitySwitchActive = false;
            _playingNextShown = false;
            _postRollActive = false;
            _postRollVideoEnded = false;
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

            restorePaused = _idleRemuxResume.Complete() ?? restorePaused;
            if (restorePaused)
                _mpv?.Pause();
            else
                _mpv?.Play();

            // Also seed ordinary seek/quality reloads which intentionally
            // finish paused, even when mpv emits no pause transition.
            _idleRemuxResume.ObservePause(restorePaused, false, DateTimeOffset.UtcNow);

            IsPaused = restorePaused;
            UpdateDisplayWakeLock(!restorePaused && !_closing && State != PlayerState.Idle);
            var mediaPosition = CurrentMediaPosition;
            var mediaDuration = CurrentMediaDuration;
            Position = mediaPosition;
            Duration = mediaDuration;
            UpdatePlaybackManagerPosition(mediaPosition, restorePaused);
            InvokeSubscribersSafely(PositionChanged, mediaPosition, nameof(PositionChanged));
            InvokeSubscribersSafely(DurationChanged, mediaDuration, nameof(DurationChanged));

            // Publish readiness only after pause and position settle: the UI
            // room timer must not release play ahead of this load callback.
            SetSwitchingContent(false); // Safe to receive PlaybackEnded now
            IsLoading = false;
            SynchronizeConnectionMediaActivity(restorePaused);
            RestoreVideoPlaybackRate();
            StartPlaybackStallWatchdog();
            InvokeSubscribersSafely(ContentLoaded, nameof(ContentLoaded));

            // Repeated FILE_LOADED events are expected after quality, audio,
            // subtitle burn-in, and recovery reloads. Only the newest load may
            // publish track/marker state or apply the initial subtitle choice.
            StartLoadedMediaInitialization();

            // Auto-compute next-episode hint if no caller already set one.
            // This fires for every playback session — including ones launched
            // directly from a card (LandscapeCard, PosterCard) that bypass
            // ItemDetailPage.SetNextEpisodeHintIfApplicable.
            _ = AutoDetectNextEpisodeAsync(_playbackCts?.Token ?? CancellationToken.None);

            // Connect WebSocket for real-time admin control
            try { ConnectWebSocket(); }
            catch (Exception ex) { LogToFile("state_trace.txt", $"WebSocket connect failed: {ex.Message}"); }
        };
        _mpv.FileLoaded += _mpvFileLoadedHandler;

        _mpvPlaybackRestartedHandler = () =>
        {
            var loadingOwner = Volatile.Read(ref _mpvLoadingOverlayOwner);
            _transportHasFirstOutput = true;
            // MPV_EVENT_PLAYBACK_RESTART is the first-frame-ready boundary for
            // an initial load and fires again after seeks/cache recovery. Keep
            // the opaque startup surface visible through FILE_LOADED so the
            // user never sees an unlabelled black-frame gap.
            _mpv?.SendScriptMessage("osc-set-loading", "false");
            if (loadingOwner != null)
                App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
            LogToFile("state_trace.txt", "PlaybackRestarted fired (video output ready)");
            ReportFirstOutput();
        };
        _mpv.PlaybackRestarted += _mpvPlaybackRestartedHandler;

        _mpvPlaybackEndedHandler = () => HandleMpvEndSignal("end-file");
        _mpv.PlaybackEnded += _mpvPlaybackEndedHandler;

        _mpvEofReachedHandler = () => HandleMpvEndSignal("eof-reached");
        _mpv.EofReached += _mpvEofReachedHandler;

        _mpvPlaybackErrorHandler = HandleMpvPlaybackError;
        _mpv.PlaybackError += _mpvPlaybackErrorHandler;
        _mpvDirectStreamErrorHandler = message => HandleMpvFailure(message, directTransport: true);
        _mpv.DirectStreamError += _mpvDirectStreamErrorHandler;

        _mpv.ScriptMessageReceived += OnScriptMessage;
        _mpvErrorHandler = (msg) => LogToFile("mpv_error.txt", msg);
        _mpv.Error += _mpvErrorHandler;
    }

    private void ReconcilePlaybackSurfaceStateAfterLoad()
    {
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        void Reconcile()
        {
            if (_closing || State == PlayerState.Idle)
                return;

            if (State is PlayerState.Expanded or PlayerState.Fullscreen or PlayerState.PictureInPicture)
                _mpv?.SendScriptMessage("osc-set-visibility", "true");

            var videoWindow = _videoWindow;
            if (videoWindow == null)
                return;
            var confirmedFullscreen = videoWindow.SynchronizeFullscreenState();
            PublishFullscreenVisualState(confirmedFullscreen);
            if (confirmedFullscreen == (State == PlayerState.Fullscreen))
                return;

            SetState(confirmedFullscreen ? PlayerState.Fullscreen : PlayerState.Expanded);
        }

        if (dispatcher?.HasThreadAccess == true)
            Reconcile();
        else if (dispatcher != null)
            dispatcher.TryEnqueue(Reconcile);
        else
            Reconcile();
    }

    private void HandleMpvPlaybackError(string message)
        => HandleMpvFailure(message, directTransport: false);

    private void HandleMpvFailure(string message, bool directTransport)
    {
        LogToFile(
            "state_trace.txt",
            $"PlaybackError: {message} _switchingContent={_switchingContent} " +
            $"_prematureEofRecoveryActive={_prematureEofRecoveryActive}");

        if (_closing || State == PlayerState.Idle || _playbackTerminalState != null)
            return;

        var mediaPosition = PlaybackRecoveryPosition;
        var mediaDuration = CurrentMediaDuration;
        if (!directTransport && IsAtMediaEnd(mediaPosition, mediaDuration))
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
                _ = RecoverInterruptedStreamAsync(mediaPosition, directTransport ? "direct-transport-error" : "file-load-error");
                return;
            }

            SetSwitchingContent(false);
            _qualitySwitchActive = false;
            _prematureEofRecoveryActive = false;
            _prematureEofRecoveryPosition = 0;
            IsLoading = false;
            var detail = message.Contains("loading failed", StringComparison.OrdinalIgnoreCase)
                ? "The media stream could not be loaded after a retry. It may be unavailable or the server may be experiencing issues."
                : $"The media stream could not resume after a retry: {message}";
            ErrorMessage = detail;
            EnterPlaybackTerminalState(
                new InvalidOperationException(detail),
                mediaPosition,
                directTransport ? "direct-transport-error" : "file-load-error",
                CaptureUserPausedState());
            return;
        }

        if (_prematureEofRecoveryActive)
            return;

        // Mark recovery synchronously so a following END_FILE signal is
        // suppressed, then rebuild the transport at canonical media time.
        _prematureEofRecoveryActive = true;
        SetSwitchingContent(true);
        _ = RecoverInterruptedStreamAsync(mediaPosition, directTransport ? "direct-transport-error" : "playback-error");
    }

    // ── Content switching (version/audio) ────────────────────────────────

    public Task SwitchVersionAsync(FileVersion version) => SwitchVersionCoreAsync(version);

    private async Task SwitchVersionCoreAsync(FileVersion version, Func<bool>? acceptSelection = null)
    {
        if (_playbackTerminalState != null) return;
        if (acceptSelection != null && !acceptSelection()) return;
        if (IsWatchTogetherPlayback)
        {
            ShowWatchPartyNotice("The room's shared version is locked. Choose a playback quality instead.");
            return;
        }
        if (_mpv == null || _playbackManager == null) return;
        var roomGeneration = Volatile.Read(ref _roomPlaybackGeneration);
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var manager = _playbackManager;
        var mpv = _mpv;
        var content = ContentId;
        var playGeneration = Volatile.Read(ref _playRequestGeneration);
        var fileGeneration = Volatile.Read(ref _playbackFileGeneration);
        using var switchCts = CancellationTokenSource.CreateLinkedTokenSource(
            _playbackCts?.Token ?? CancellationToken.None, _authorityLifetimeCts.Token);
        var ct = switchCts.Token;
        var previousSwitch = Interlocked.Exchange(ref _soloVersionSwitchCts, switchCts);
        try { previousSwitch?.Cancel(); } catch (ObjectDisposedException) { }
        try
        {
            bool CurrentSwitch() => !ct.IsCancellationRequested && !_closing && _playbackTerminalState == null && !IsWatchTogetherPlayback &&
                roomGeneration == Volatile.Read(ref _roomPlaybackGeneration) &&
                ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
                ReferenceEquals(manager, _playbackManager) && ReferenceEquals(mpv, _mpv) && content == ContentId &&
                playGeneration == Volatile.Read(ref _playRequestGeneration) &&
                fileGeneration == Volatile.Read(ref _playbackFileGeneration);
            await CancelAndDrainTransportRestartsAsync(ct);
            if (!CurrentSwitch() || (acceptSelection != null && !acceptSelection()) ||
                IsWatchTogetherPlayback || (acceptSelection != null && !Versions.Contains(version))) return;
            var wasPaused = CaptureUserPausedState();
            var currentPos = CurrentMediaPosition;
            var previousSession = manager.CurrentSession;
            var replacementAccepted = false;
            var busyOwner = BeginPlaybackBusy(loading: false, claimState: () =>
            {
                _qualitySwitchActive = false;
                _subtitleVersionSwitchActive = true;
            });
            CancelPendingLoadedMediaInitialization();
            // Don't call _mpv.Stop() — let current stream keep playing while we set up the new one

            try
            {
                manager.SourceVersionPinned = true;
                var session = await manager.StartReplacementSessionAsync(
                    version.FileId,
                    currentPos,
                    forceStartPosition: true,
                    previousFinalPosition: currentPos,
                    ct: ct);
                busyOwner = busyOwner with { Session = session };
                if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner)) return;
                replacementAccepted = true;
                _requestedMediaFileId = version.FileId;
                InvokeSubscribersSafely(SessionStarted, session.SessionId, nameof(SessionStarted));
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
                if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner) ||
                    !ReferenceEquals(manager.CurrentSession, session))
                    return;

                PlayMethod = session.PlayMethod;
                Resolution = version.Resolution;
                ApplyPreparedTransport(prepared);
                BeginOwnedMpvLoad(prepared, restorePaused: wasPaused,
                    acceptLoad: generation =>
                    {
                        fileGeneration = generation;
                        busyOwner = busyOwner with { FileGeneration = generation };
                    },
                    acceptBusy: owner => busyOwner = owner);
                if (!CurrentSwitch()) return;
                SendQualityInfoToOsc();
                SendMediaInfoToOsc();
                _mpv?.SendScriptMessage("osc-set-active-quality", SelectedQualityMenuId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogToFile("state_trace.txt", "Version switch canceled");
            }
            catch (Exception ex)
            {
                if (!CurrentSwitch()) return;
                if (ReferenceEquals(manager, _playbackManager) && manager.CurrentSession == null)
                    busyOwner = busyOwner with { Session = null };
                var released = ReleasePlaybackBusy(busyOwner, () => _subtitleVersionSwitchActive = false);
                if (released == null) return;
                RunIfBusyReleaseCurrent(released, () =>
                {
                    if (!CurrentSwitch()) return;
                    LogToFile("player_quality_switch_error.txt", ex.ToString());
                    ErrorMessage = $"Failed to switch quality: {ex.Message}";
                    if (manager.CurrentSession == null || manager.SessionRetirementPending)
                    {
                        // Retirement is the point of no return: the old transport may
                        // no longer run, even when the replacement was never accepted.
                        EnterPlaybackTerminalState(ex, currentPos, "version-switch-failed", wasPaused, previousSession);
                    }
                    else if (replacementAccepted)
                    {
                        _prematureEofRecoveryActive = true;
                        SetSwitchingContent(true);
                        IsLoading = true;
                        ShowNotice(
                            "Reconnecting playback",
                            "The version switch was interrupted. Restoring playback from your current position…",
                            "warning");
                        _ = RecoverInterruptedStreamAsync(currentPos, "version-switch-failed");
                    }
                    else
                    {
                        var (title, detail) = DescribePlaybackError(ex);
                        ShowNotice(title, detail, "error");
                    }
                });
                if (CurrentSwitch() && ReferenceEquals(manager.CurrentSession, previousSession) && !_switchingContent && !IsLoading)
                    await ReconcileDeferredSubtitleSelectionAsync(manager, previousSession);
            }
            finally
            {
                if (!CurrentSwitch())
                {
                    // A slot-limit retry can retire its own outgoing session
                    // before a canceled replacement returns a new one.
                    if (ReferenceEquals(manager, _playbackManager) && manager.CurrentSession == null)
                        busyOwner = busyOwner with { Session = null };
                    ReleasePlaybackBusy(busyOwner, () => _subtitleVersionSwitchActive = false);
                }
            }
        }
        finally { Interlocked.CompareExchange(ref _soloVersionSwitchCts, null, switchCts); }
    }

    public Task SwitchAudioTrackAsync(int trackIndex) => SwitchAudioTrackCoreAsync(trackIndex);

    private async Task SwitchAudioTrackCoreAsync(int trackIndex, Func<bool>? acceptSelection = null)
    {
        if (acceptSelection != null && !acceptSelection()) return;
        var authority = _activeAuthority;
        var manager = _playbackManager;
        if (_mpv == null || manager == null) return;
        // The menu hands off a selection, not permission to complete it after
        // a newer profile/file/load replaces the initiating playback.
        var selectionContext = authority.ApiClient.CaptureContext();
        var selectionVersion = ActiveVersion;
        var selectionTracks = selectionVersion?.AudioTracks?.ToArray();
        var selectionContent = ContentId;
        var selectionMpv = _mpv;
        var selectionVideo = _videoWindow;
        var selectionPlayGeneration = Volatile.Read(ref _playRequestGeneration);
        var selectionFileGeneration = Volatile.Read(ref _playbackFileGeneration);
        bool CurrentSelection() => acceptSelection == null ||
            (authority.ApiClient.IsCurrentContext(selectionContext) && selectionContent == ContentId &&
            ReferenceEquals(selectionVersion, ActiveVersion) &&
            (selectionTracks == null || ActiveVersion?.AudioTracks is { } tracks && tracks.SequenceEqual(selectionTracks)) &&
            ReferenceEquals(selectionMpv, _mpv) && ReferenceEquals(selectionVideo, _videoWindow) &&
            selectionPlayGeneration == Volatile.Read(ref _playRequestGeneration) &&
            selectionFileGeneration == Volatile.Read(ref _playbackFileGeneration));
        var audioPreference = CaptureAudioPreference(trackIndex);
        using var switchCts = CancellationTokenSource.CreateLinkedTokenSource(_playbackCts?.Token ?? CancellationToken.None, _authorityLifetimeCts.Token);
        var ct = switchCts.Token;
        bool CurrentSwitch() => !ct.IsCancellationRequested && !_closing &&
            ReferenceEquals(authority, _activeAuthority) && ReferenceEquals(manager, _playbackManager) && CurrentSelection();
        await CancelAndDrainTransportRestartsAsync(ct);
        if (!CurrentSwitch() || (acceptSelection != null && !acceptSelection())) return;
        var wasPaused = CaptureUserPausedState();
        var currentPos = CurrentMediaPosition;
        if (manager.CurrentSession is { } survivingSession)
        {
            var busyOwner = BeginPlaybackBusy();
            try
            {
                var session = await manager.ReplanAudioAsync(trackIndex, currentPos, ct).ConfigureAwait(false);
                busyOwner = busyOwner with { Session = session };
                if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner)) return;
                var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                    ?? throw new InvalidOperationException("No active version is available for the audio switch.");
                var streamUrl = manager.StreamUrl
                    ?? throw new InvalidOperationException("No stream URL was returned for the audio switch.");
                var prepared = await PreparePlaybackTransportAsync(
                    session,
                    version,
                    streamUrl,
                    currentPos,
                    ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner) ||
                    !ReferenceEquals(manager.CurrentSession, session))
                    return;
                PlayMethod = session.PlayMethod;
                ApplyPreparedTransport(prepared);
                // The callback adopts only this attempt, including an owned
                // load failure. Never borrow a later global generation.
                BeginOwnedMpvLoad(prepared, restorePaused: wasPaused,
                    acceptLoad: generation =>
                    {
                        selectionFileGeneration = generation;
                        busyOwner = busyOwner with { FileGeneration = generation };
                    },
                    acceptBusy: owner => busyOwner = owner);
                if (!CurrentSwitch()) return;
                await PersistAudioPreferenceAsync(audioPreference);
                if (!CurrentSwitch()) return;
                SendAudioTrackListToOsc();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogToFile("state_trace.txt", "Protocol-v3 audio switch canceled");
            }
            catch (Exception ex)
            {
                if (!CurrentSwitch()) return;
                var released = ReleasePlaybackBusy(busyOwner, () =>
                {
                    if (!CurrentSwitch()) return;
                    LogToFile("player_audio_switch_error.txt", ex.ToString());
                    var (title, detail) = DescribePlaybackError(ex);
                    ErrorMessage = detail;
                    ShowNotice(title, detail, "error");
                    SendAudioTrackListToOsc();
                });
                if (released != null && CurrentSwitch())
                    await ReconcileDeferredSubtitleSelectionAsync(manager, survivingSession);
            }
            finally
            {
                if (!CurrentSwitch()) ReleasePlaybackBusy(busyOwner);
            }
            return;
        }

        if (_activeTransportPlan?.TransportKind == PlaybackTransportKind.DirectProgressive)
        {
            // The session has ended, but a native direct stream already contains
            // every audio track, so the switch needs no server.
            _mpv.SetAudioTrack(trackIndex + 1);
            await PersistAudioPreferenceAsync(audioPreference);
            if (!CurrentSwitch()) return;
            SendAudioTrackListToOsc();
            return;
        }

        // Changing a server transport's audio needs the session, which ended.
        var ended = new InvalidOperationException("The playback session is no longer active.");
        LogToFile("player_audio_switch_error.txt", ended.ToString());
        var (endedTitle, endedDetail) = DescribePlaybackError(ended);
        ErrorMessage = endedDetail;
        ShowNotice(endedTitle, endedDetail, "error");
        SendAudioTrackListToOsc();
    }

    // ── Subtitles ────────────────────────────────────────────────────────

    // ── Subtitle sidecars ────────────────────────────────────────────────
    //
    // mpv fetches a sidecar URL once. The server extracts an embedded text
    // track as a bounded window only when position/duration are explicit;
    // without them the URL describes the complete track ("native players fetch
    // once and must retain cues beyond ten minutes and before a resumed
    // playback position"). Every sidecar therefore requests the whole track and
    // is loaded at most once per file, keyed by its server track index.

    private readonly object _subtitleTrackStateLock = new();
    private readonly object _subtitleAddGate = new();
    private readonly Dictionary<int, int> _loadedExternalSubtitleSids = [];
    // Remote text sidecars downloaded for this file, keyed by server track
    // index. The files are deleted with the subtitle-sync files.
    private readonly Dictionary<int, string> _prefetchedSubtitleFiles = [];
    private long _subtitleSidecarGeneration;

    private void LoadSubtitles()
    {
        if (_playbackManager?.CurrentSession == null || _mpv == null) return;

        CancelSubtitleSelectionRead();

        lock (_subtitleSyncGate)
        lock (_subtitleTrackStateLock)
        {
            _loadedExternalSubtitleSids.Clear();
            // Same-session reloads keep the subtitle-sync lifecycle alive, so
            // remove this file's downloads here instead of accumulating them.
            var retained = TakePreparedSubtitleReload();
            foreach (var path in _prefetchedSubtitleFiles.Values)
            {
                if (path == retained?.Path) continue;
                _subtitleSyncFiles.Remove(path);
                try { File.Delete(path); } catch { }
            }
            _prefetchedSubtitleFiles.Clear();
            if (retained != null) _prefetchedSubtitleFiles[retained.Index] = retained.Path;
            _subtitleSidecarGeneration++;
        }

        // Do not eagerly sub-add every server subtitle URL. mpv already sees
        // embedded text tracks on direct/remux playback, and adding all VTT
        // URLs upfront can fan out dozens of HTTP/ffmpeg subtitle fetches
        // during 4K startup. External/downloaded tracks are loaded on demand
        // when the user selects one, matching the WebUI's active-track model.
    }

    private static bool IsBurnedInSubtitle(PlaybackStartResponse? session, SubtitleTrackInfo track)
        => session?.IsSubtitleBurnedIn == true &&
           (!string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)
               ? !string.IsNullOrWhiteSpace(track.TrackId) &&
                 string.Equals(session.SelectedSubtitleTrackId, track.TrackId, StringComparison.Ordinal)
               : session.SelectedSubtitleTrackIndex is int index && track.Index == index);

    private static bool IsUnsupportedBitmapSubtitle(SubtitleTrackInfo track)
    {
        if (string.Equals(track.Delivery, "burn_in_only", StringComparison.OrdinalIgnoreCase))
            return true;
        var codec = track.Codec?.ToLowerInvariant() ?? "";
        return codec is "dvdsub" or "dvd_subtitle" or "vobsub" or "dvbsub" or "dvb_subtitle";
    }

    private static bool IsTranslatableSubtitleSource(SubtitleTrackInfo track)
    {
        var codec = track.Codec?.Trim().ToLowerInvariant() ?? "";
        if (string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase))
        {
            return codec is not (
                "pgs" or "hdmv_pgs_subtitle" or "sup" or
                "dvdsub" or "dvd_subtitle" or "vobsub" or
                "dvbsub" or "dvb_subtitle");
        }

        return codec is "srt" or "subrip" or "vtt" or "webvtt";
    }

    private bool CanUseNativeEmbeddedSubtitleTrack()
        => _activeTransportPlan?.TransportKind == PlaybackTransportKind.DirectProgressive;

    private enum SubtitleTransportResult { Failed, Superseded, ReloadScheduled }
    private sealed record SubtitleReplanAttempt(PlaybackStartResponse Session, int SelectionIndex);
    private volatile SubtitleReplanAttempt? _subtitleReplanAttempt;

    /// <summary>
    /// DVD/VOBSUB and DVB bitmap tracks have no usable sidecar representation.
    /// Restart the current session as HLS with server-side burn-in, preserving
    /// canonical position and pause state. PGS deliberately does not use this
    /// path because libmpv can render the server's .sup sidecar directly.
    /// </summary>
    /// <returns>Distinguishes a retired owner from failure and an accepted reload.</returns>
    private async Task<SubtitleTransportResult> SetBitmapSubtitleBurnInAsync(
        SubtitleTrackInfo? track,
        int? postLoadSubtitleIndex = null)
    {
        var manager = _playbackManager;
        var mpv = _mpv;
        if (mpv == null || manager?.CurrentSession is not { } session) return SubtitleTransportResult.Superseded;
        var authority = _activeAuthority;
        var authorityToken = _authorityLifetimeCts.Token;
        var loadingOwner = _playbackCts;
        var ct = loadingOwner?.Token ?? CancellationToken.None;
        bool CurrentOwner() => !_closing && !ct.IsCancellationRequested && !authorityToken.IsCancellationRequested &&
            ReferenceEquals(authority, _activeAuthority) && ReferenceEquals(manager, _playbackManager) && ReferenceEquals(mpv, _mpv);
        await CancelAndDrainTransportRestartsAsync(ct);
        if (!CurrentOwner()) return SubtitleTransportResult.Superseded;
        // BeginMpvLoad freezes the outgoing stream. A subtitle replan during
        // that load must retain its viewer intent, not the temporary freeze.
        var wasPaused = (_switchingContent || IsLoading) && _restorePausedAfterLoad is bool pendingPause
            ? pendingPause : CaptureUserPausedState();
        int? requestedIndex = track?.Index ?? postLoadSubtitleIndex;
        var transportMutationAttempted = false;
        var reconcileDeferredSelection = false;
        await _transportRestartGate.WaitAsync(ct).ConfigureAwait(false);
        var selectionIntent = manager.CaptureLocalSubtitleIntent();
        var preferenceOwner = _pendingSubtitlePreferenceOwner;
        try
        {
            if (!CurrentOwner() || manager?.CurrentSession != session || _mpv == null ||
                _activeSubtitleServerIndex != (requestedIndex ?? -1)) return SubtitleTransportResult.Superseded;
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? Versions.FirstOrDefault(v => v.FileId == (_requestedMediaFileId ?? 0))
                ?? throw new InvalidOperationException("The subtitle source version is unavailable.");
            var position = CurrentMediaPosition;
            transportMutationAttempted = true;
            SetSwitchingContent(true);
            IsLoading = true;
            _mpv.Pause();
            if (loadingOwner != null)
                App.MainWindowInstance?.ShowLoadingOverlay(loadingOwner);

            PlaybackStartResponse replanned;
            PreparedPlaybackTransport preparedV3;
            while (true)
            {
                replanned = await manager.ReplanSubtitleAsync(requestedIndex, position, ct,
                    isAutomatic: manager.IsSubtitleSelectionAutomatic).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!CurrentOwner() || manager.CurrentSession != replanned) return SubtitleTransportResult.Superseded;
                // The returned inventory may have rebound the same intent to a
                // different combined index. Bound retries using that inventory.
                _subtitleReplanAttempt = new(replanned, manager.SubtitleSelectionForLoad);
                if (_activeSubtitleServerIndex != (requestedIndex ?? -1))
                {
                    // The server accepted the old request. Bring its plan to
                    // the latest choice before loading any burned-in pixels.
                    requestedIndex = _activeSubtitleServerIndex < 0 ? null : _activeSubtitleServerIndex;
                    continue;
                }
                var replannedVersion = Versions.FirstOrDefault(v => v.FileId == replanned.MediaFileId) ?? version;
                var streamUrl = manager.StreamUrl
                    ?? throw new InvalidOperationException("No stream URL was returned for the subtitle switch.");
                preparedV3 = await PreparePlaybackTransportAsync(replanned, replannedVersion, streamUrl, position, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!CurrentOwner() || manager.CurrentSession != replanned) return SubtitleTransportResult.Superseded;
                if (_activeSubtitleServerIndex == (requestedIndex ?? -1)) break;
                // Only this still-current accepted plan owns these unused
                // proxies. A replacement owner retires its own resources.
                StopDirectStreamProxy(); StopHlsProxy();
                requestedIndex = _activeSubtitleServerIndex < 0 ? null : _activeSubtitleServerIndex;
            }
            ApplyPreparedTransport(preparedV3);
            PlayMethod = replanned.PlayMethod;
            var resolvedIndex = manager.SubtitleSelectionForLoad;
            lock (_subtitleTrackStateLock)
            {
                _pendingInitialServerSubtitleIndex = resolvedIndex;
                _pendingInitialSubtitleFileId = replanned.MediaFileId;
                // Preference execution must follow the same rebound identity,
                // not wait for the outgoing inventory's now-obsolete index.
                if (_pendingSubtitlePreferenceIndex == (requestedIndex ?? -1))
                    _pendingSubtitlePreferenceIndex = resolvedIndex;
            }
            PrepareSubtitleFileForReload(session, replanned, resolvedIndex);
            BeginMpvLoad(preparedV3, restorePaused: wasPaused);
            var finalTrack = replanned.SubtitleUrls.FirstOrDefault(candidate => candidate.Index == resolvedIndex);
            if (replanned.IsSubtitleBurnedIn && finalTrack != null &&
                (!string.IsNullOrWhiteSpace(replanned.SelectedSubtitleTrackId)
                    ? finalTrack.TrackId == replanned.SelectedSubtitleTrackId
                    : finalTrack.Index == replanned.SelectedSubtitleTrackIndex))
                _mpv.ShowOsdText($"{finalTrack.Label} · burned in", 2500);
            else if (!replanned.IsSubtitleBurnedIn && (finalTrack == null || !IsUnsupportedBitmapSubtitle(finalTrack)))
                _mpv.ShowOsdText("Bitmap subtitles off", 2500);
            SendQualityInfoToOsc();
            return SubtitleTransportResult.ReloadScheduled;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return SubtitleTransportResult.Superseded; }
        catch (Exception ex)
        {
            if (!CurrentOwner()) return SubtitleTransportResult.Superseded;
            LogToFile("player_subtitle_error.txt", ex.ToString());
            if (transportMutationAttempted && !_closing && ReferenceEquals(_playbackManager, manager))
            {
                if (ReferenceEquals(manager.CurrentSession, session))
                {
                    // No replacement was adopted: the outgoing stream is still
                    // healthy. Refuse the tentative choice, not the playback plan.
                    _prematureEofRecoveryActive = false;
                    _prematureEofRecoveryPosition = 0;
                    SetSwitchingContent(false);
                    IsLoading = false;
                    lock (_subtitleTrackStateLock)
                    {
                        if (ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner) &&
                            manager.CaptureLocalSubtitleIntent() == selectionIntent)
                        {
                            RestoreExecutedSubtitleSelection(reapply: false);
                            if (preferenceOwner?.PreviousIntent == null)
                                manager.RestoreLocalSubtitleIntent(new(true, _executedSubtitleTrack,
                                    session.MediaFileId, selectionIntent.IsAutomatic));
                            _pendingAutomaticSubtitleSelection = null;
                            ShowSubtitleSelectionUnavailable();
                        }
                        else
                        {
                            // A newer deferred pick still needs an executor, but
                            // must not be rolled back with the rejected request.
                            reconcileDeferredSelection = true;
                        }
                    }
                    mpv.SendScriptMessage("osc-set-loading", "false");
                    if (loadingOwner != null)
                        App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
                    RestorePlaybackStateAfterSubtitleChange(wasPaused, CurrentMediaPosition, allowSeek: false);
                    return SubtitleTransportResult.Failed;
                }
                _prematureEofRecoveryActive = true;
                SetSwitchingContent(true);
                IsLoading = true;
                ShowNotice(
                    "Reconnecting playback",
                    "The subtitle transport switch was interrupted. Restoring playback from your current position…",
                    "warning");
                // A concurrent recovery may own the gate. Queue this viewer
                // change with its lifetime and pause intent instead of dropping it.
                _ = RunStreamRecoveryAsync(CurrentMediaPosition, "subtitle-switch-failed", manager, wasPaused, ct);
                // Recovery now owns the transport; callers must not restore
                // the outgoing stream's position or pause state.
                return SubtitleTransportResult.ReloadScheduled;
            }
            else
            {
                SetSwitchingContent(false);
                IsLoading = false;
                if (!wasPaused)
                    _mpv?.Play();
                ShowNotice("Subtitle switch failed", ex.Message, "error");
            }
        }
        finally
        {
            _transportRestartGate.Release();
            if (reconcileDeferredSelection && CurrentOwner())
                await ReconcileDeferredSubtitleSelectionAsync(manager, session);
        }
        return SubtitleTransportResult.Failed;
    }

    private async Task ReconcileDeferredSubtitleSelectionAsync(PlaybackManager manager, PlaybackStartResponse? survivingSession)
    {
        var owner = _pendingSubtitlePreferenceOwner;
        if (_closing || _switchingContent || IsLoading || !ReferenceEquals(manager, _playbackManager) ||
            survivingSession == null ||
            owner == null || owner.Token.IsCancellationRequested || !ReferenceEquals(owner.Authority, _activeAuthority) ||
            !ReferenceEquals(owner.Manager, manager) || owner.MediaFileId != survivingSession.MediaFileId ||
            _pendingSubtitlePreferenceIndex is not int requested) return;

        if (!ReferenceEquals(manager.CurrentSession, survivingSession))
        {
            // A plan was adopted but its transport never loaded. In particular,
            // a burn-in grant is not proof that the surviving video contains it.
            // Roll back the unexecuted choice rather than save a false success
            // or strand intent for an unrelated future reload.
            lock (_subtitleTrackStateLock)
            {
                if (!ReferenceEquals(owner, _pendingSubtitlePreferenceOwner) || _switchingContent || IsLoading) return;
                RestoreExecutedSubtitleSelection(reapply: false);
                ShowSubtitleSelectionUnavailable();
            }
            return;
        }
        // No FILE_LOADED will consume this slot after a rejected/no-op replan.
        _pendingInitialServerSubtitleIndex = null;
        _pendingAutomaticSubtitleSelection = null;
        if (!await SelectSubtitleByServerIndexAsync(requested, persist: false) &&
            ReferenceEquals(owner, _pendingSubtitlePreferenceOwner) &&
            ReferenceEquals(survivingSession, manager.CurrentSession) && !_switchingContent && !IsLoading)
        {
            RestoreExecutedSubtitleSelection(reapply: false);
            ShowSubtitleSelectionUnavailable();
        }
    }

    private async Task<bool> SelectSubtitleByServerIndexAsync(int serverTrackIndex, bool persist = true)
    {
        var mpv = _mpv;
        if (mpv == null || _closing) return false;
        if (persist && serverTrackIndex >= 0 && !IsSubtitleMenuCurrent())
        {
            ShowSubtitleSelectionUnavailable();
            return false;
        }
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        var track = session?.SubtitleUrls.FirstOrDefault(t => t.Index == serverTrackIndex);
        var showUnavailable = persist || _pendingSubtitlePreferenceIndex == serverTrackIndex;
        var authority = _activeAuthority;
        var ownerToken = _authorityLifetimeCts.Token;
        var sidecarGeneration = CurrentSubtitleSidecarGeneration();
        var loadedMediaGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
        bool CurrentSelection() => !_closing && !_switchingContent && !IsLoading && !ownerToken.IsCancellationRequested &&
            ReferenceEquals(mpv, _mpv) && ReferenceEquals(manager, _playbackManager) &&
            ReferenceEquals(session, manager?.CurrentSession) &&
            ReferenceEquals(authority, _activeAuthority) && _activeSubtitleServerIndex == serverTrackIndex &&
            CurrentSubtitleSidecarGeneration() == sidecarGeneration &&
            Volatile.Read(ref _loadedMediaInitGeneration) == loadedMediaGeneration;
        if (!persist)
        {
            lock (_subtitleTrackStateLock) _activeSubtitleServerIndex = serverTrackIndex;
        }
        if (serverTrackIndex >= 0 && (track == null || track.Unplayable ||
            (!persist || !_switchingContent && !IsLoading) && !HasSubtitleSelectionRoute(track)))
        {
            if (!persist)
            {
                if (!CurrentSelection()) return false;
                if (!RestoreExecutedSubtitleSelection(reapply: true, failedTrack: track, currentSelection: CurrentSelection)) return false;
            }
            ShowSubtitleSelectionUnavailable(showUnavailable);
            return false;
        }
        if (persist)
        {
            // This is an enabled server choice, not an mpv SID.
            if (!await TrySetSubtitleTrackAndPersistAsync(serverTrackIndex < 0 ? 0 : 1, track?.Language, track))
                return false;
        }
        else
        {
            if (!CurrentSelection()) return false;
            var burnedTrack = session?.IsSubtitleBurnedIn == true ? session.SubtitleUrls.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(session.SelectedSubtitleTrackId)
                    ? t.TrackId == session.SelectedSubtitleTrackId : t.Index == session.SelectedSubtitleTrackIndex) : null;
            // A playable reply may decline the requested burn-in change. The
            // resulting load must not automatically send that same request again.
            // A new viewer pick still goes through the persistent path and may retry.
            var attempt = _subtitleReplanAttempt;
            var alreadyReplanned = ReferenceEquals(attempt?.Session, session) &&
                attempt?.SelectionIndex == serverTrackIndex;
            if (session?.IsSubtitleBurnedIn == true && (track == null || track.Index != burnedTrack?.Index))
            {
                var result = alreadyReplanned ? SubtitleTransportResult.Failed :
                    await SetBitmapSubtitleBurnInAsync(null, postLoadSubtitleIndex: track?.Index);
                if (result == SubtitleTransportResult.ReloadScheduled) return true;
                if (result == SubtitleTransportResult.Superseded || !CurrentSelection()) return false;
                if (!RestoreExecutedSubtitleSelection(reapply: false, currentSelection: CurrentSelection)) return false;
                ShowSubtitleSelectionUnavailable(showUnavailable);
                return false;
            }
            if (track != null)
            {
                var readOutcome = await PrefetchSubtitleSidecarAsync(track);
                // A newer choice or replacement file owns its own completion.
                if (!CurrentSelection()) return false;
                if (readOutcome == SubtitlePrefetchOutcome.Retired) return false;
                if (readOutcome is SubtitlePrefetchOutcome.Busy or SubtitlePrefetchOutcome.Error)
                {
                    RestoreExecutedSubtitleSelection(reapply: true, failedTrack: track, currentSelection: CurrentSelection);
                    ShowSubtitleReadFailure(readOutcome);
                    return false;
                }
                if (IsUnsupportedBitmapSubtitle(track) && !IsBurnedInSubtitle(session, track))
                {
                    var result = alreadyReplanned ? SubtitleTransportResult.Failed :
                        await SetBitmapSubtitleBurnInAsync(track);
                    // An accepted reload owns its pending index, possibly reconciled
                    // to a newer pick. A retired worker must not overwrite it.
                    if (result == SubtitleTransportResult.ReloadScheduled) return true;
                    if (result == SubtitleTransportResult.Superseded || !CurrentSelection()) return false;
                }
                else
                {
                    var executed = SelectSubtitleTrackForOwner(track, CurrentSelection);
                    if (!CurrentSelection()) return false;
                    if (!executed)
                    {
                        if (!RestoreExecutedSubtitleSelection(reapply: true, failedTrack: track, currentSelection: CurrentSelection)) return false;
                        ShowSubtitleSelectionUnavailable(showUnavailable);
                        return false;
                    }
                    CommitExecutedSubtitleSelection(track);
                    if (_pendingSubtitlePreferenceIndex == serverTrackIndex)
                    {
                        var preferenceOwner = _pendingSubtitlePreferenceOwner;
                        _pendingSubtitlePreferenceIndex = null;
                        _pendingSubtitlePreferenceOwner = null;
                        await SaveExecutedSubtitlePreferenceAsync(preferenceOwner, track);
                    }
                    if (CurrentSelection()) mpv.SendScriptMessage("osc-set-active-subtitle", serverTrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                }
                if (!RestoreExecutedSubtitleSelection(reapply: true, failedTrack: track, currentSelection: CurrentSelection)) return false;
                ShowSubtitleSelectionUnavailable(showUnavailable);
                return false;
            }
            lock (_subtitleTrackStateLock)
            {
                if (!CurrentSelection()) return false;
                mpv.SetSubtitleTrack(0);
                CommitExecutedSubtitleSelection(null);
            }
            if (_pendingSubtitlePreferenceIndex == serverTrackIndex)
            {
                var preferenceOwner = _pendingSubtitlePreferenceOwner;
                _pendingSubtitlePreferenceIndex = null;
                _pendingSubtitlePreferenceOwner = null;
                await SaveExecutedSubtitlePreferenceAsync(preferenceOwner, null);
            }
        }
        // A persistent request can be accepted while loading; only the owner
        // that actually executes it may publish the resulting highlight.
        if (CurrentSelection()) mpv.SendScriptMessage("osc-set-active-subtitle", serverTrackIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>The selected server subtitle-track index, or -1 when subtitles are off.</summary>
    public int ActiveSubtitleServerIndex => _activeSubtitleServerIndex;

    private void CycleSubtitleBackward()
    {
        // Like mpv's local cycle, this gesture neither downloads subtitles nor
        // changes the saved series preference. Only demonstrated, loaded SIDs
        // participate; raw native cycling could select an unavailable row.
        lock (_subtitleTrackStateLock)
        {
            var mpv = _mpv;
            var manager = _playbackManager;
            var session = manager?.CurrentSession;
            if (mpv == null || session == null || _closing || _switchingContent || IsLoading ||
                !IsSubtitleMenuCurrent() || session.IsSubtitleBurnedIn) return;
            var generation = Volatile.Read(ref _loadedMediaInitGeneration);
            var externalIds = mpv.GetSubtitleTrackIds(externalOnly: true).ToHashSet();
            var choices = new List<(SubtitleTrackInfo? Track, int Sid)> { (null, 0) };
            foreach (var track in session.SubtitleUrls.Where(t => !t.Unplayable && !IsUnsupportedBitmapSubtitle(t)))
            {
                var sid = _loadedExternalSubtitleSids.TryGetValue(track.Index, out var cached) && externalIds.Contains(cached)
                    ? cached : string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase)
                        ? ResolveNativeEmbeddedSid(track) : 0;
                if (sid > 0) choices.Add((track, sid));
            }
            if (_closing || _switchingContent || IsLoading || !IsSubtitleMenuCurrent() ||
                !ReferenceEquals(mpv, _mpv) || !ReferenceEquals(manager, _playbackManager) ||
                !ReferenceEquals(session, manager?.CurrentSession) ||
                generation != Volatile.Read(ref _loadedMediaInitGeneration)) return;
            var current = choices.FindIndex(choice => (choice.Track?.Index ?? -1) == _activeSubtitleServerIndex);
            var selected = choices[current > 0 ? current - 1 : choices.Count - 1];
            mpv.SetSubtitleTrack(selected.Sid);
            var actualSid = mpv.GetPropertyDouble("sid");
            if (selected.Sid == 0 ? actualSid > 0 : (int)Math.Round(actualSid) != selected.Sid) return;
            _pendingAutomaticSubtitleSelection = null;
            _pendingInitialServerSubtitleIndex = null;
            _pendingSubtitlePreferenceIndex = null;
            _pendingSubtitlePreferenceOwner = null;
            _activeSubtitleServerIndex = selected.Track?.Index ?? -1;
            if (session.ProtocolVersion >= 3) manager!.RecordLocalSubtitleSelection(selected.Track?.Index,
                isAutomatic: manager.IsSubtitleSelectionAutomatic);
            CommitExecutedSubtitleSelection(selected.Track);
            mpv.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private bool HasSubtitleSelectionRoute(SubtitleTrackInfo track)
    {
        if (track.Unplayable) return false;
        if (IsUnsupportedBitmapSubtitle(track)) return true;
        if (string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase) &&
            CanUseNativeEmbeddedSubtitleTrack() && ResolveNativeEmbeddedSid(track) > 0) return true;
        return _playbackManager?.GetSubtitleUrls().Any(pair => pair.Track.Index == track.Index &&
            Uri.TryCreate(pair.FullUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https") == true;
    }

    private void CommitExecutedSubtitleSelection(SubtitleTrackInfo? track)
    {
        _executedSubtitleTrack = track;
        _executedSubtitleFileId = _playbackManager?.CurrentSession?.MediaFileId;
    }

    private bool RestoreExecutedSubtitleSelection(bool reapply, SubtitleTrackInfo? failedTrack = null, Func<bool>? currentSelection = null)
    {
        var session = _playbackManager?.CurrentSession;
        var previousIntent = _pendingSubtitlePreferenceOwner?.PreviousIntent;
        var previous = _executedSubtitleTrack;
        var retained = previous != null && session != null && session.MediaFileId == _executedSubtitleFileId
            ? session.SubtitleUrls.FirstOrDefault(t => !t.Unplayable &&
                (!string.IsNullOrWhiteSpace(previous.TrackId) ? t.TrackId == previous.TrackId :
                    t.Index == previous.Index && t.Source == previous.Source && t.Codec == previous.Codec &&
                    MediaLanguageCatalog.Normalize(t.Language) == MediaLanguageCatalog.Normalize(previous.Language) &&
                    t.Forced == previous.Forced && t.HearingImpaired == previous.HearingImpaired))
            : null;
        // A retained server burn-in is already rendered even when no local
        // SID has been recorded (for example before the first manual choice).
        if (retained == null && previousIntent?.Track is { } granted && session?.MediaFileId == previousIntent.MediaFileId &&
            IsBurnedInSubtitle(session, granted))
            retained = session.SubtitleUrls.FirstOrDefault(t => t.Index == granted.Index && IsBurnedInSubtitle(session, t));
        // A retained native-only row may have no route in a replacement remux.
        // Do not retain a highlight for subtitles the new file cannot execute.
        if (retained != null && (!HasSubtitleSelectionRoute(retained) ||
            reapply && (retained.Index == failedTrack?.Index || !SelectSubtitleTrackForOwner(retained, currentSelection)))) retained = null;
        // Reapplying a different retained sidecar can itself block in sub-add.
        if (currentSelection != null && !currentSelection()) return false;
        if (retained == null) _mpv?.SetSubtitleTrack(0);
        _activeSubtitleServerIndex = retained?.Index ?? -1;
        _pendingSubtitleSelection = _activeSubtitleServerIndex;
        _pendingInitialServerSubtitleIndex = null;
        if (!reapply || retained != null || previousIntent != null)
        {
            _pendingSubtitlePreferenceIndex = null;
            _pendingSubtitlePreferenceOwner = null;
        }
        // Failing to execute on this file is not a viewer choice of Off.
        // Preserve the requested intent for the next load/transport when no
        // previous route could be restored. A tentative manual pick instead
        // restores its exact prior identity and automatic/explicit provenance.
        if (session?.ProtocolVersion >= 3 && previousIntent != null)
            _playbackManager!.RestoreLocalSubtitleIntent(previousIntent);
        CommitExecutedSubtitleSelection(retained);
        return true;
    }

    private void ShowSubtitleSelectionUnavailable(bool showNotice = true)
    {
        // Caption shortcuts optimistically paint their choice. Reassert the
        // retained choice when it has no executable route for this file.
        _mpv?.SendScriptMessage("osc-set-active-subtitle", _activeSubtitleServerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (showNotice)
            ShowNotice("Subtitle unavailable", "This subtitle cannot be selected in the current stream. Choose another subtitle or turn subtitles off.", "warning");
    }

    private bool SelectSubtitleTrack(SubtitleTrackInfo track) => SelectSubtitleTrackForOwner(track, null);

    private bool SelectSubtitleTrackForOwner(SubtitleTrackInfo track, Func<bool>? selectionOwner)
    {
        if (_mpv == null || track.Unplayable || !HasSubtitleSelectionRoute(track)) return false;
        var mpv = _mpv;
        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        var authority = _activeAuthority;
        var token = _authorityLifetimeCts.Token;
        var activeIndex = _activeSubtitleServerIndex;
        var preferenceOwner = _pendingSubtitlePreferenceOwner;
        var sidecarGeneration = CurrentSubtitleSidecarGeneration();
        var loadGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
        bool CurrentSelection() => (selectionOwner?.Invoke() ?? true) &&
            !_closing && !_switchingContent && !IsLoading && !token.IsCancellationRequested &&
            ReferenceEquals(mpv, _mpv) && ReferenceEquals(manager, _playbackManager) &&
            ReferenceEquals(session, manager?.CurrentSession) && ReferenceEquals(authority, _activeAuthority) &&
            _activeSubtitleServerIndex == activeIndex && ReferenceEquals(preferenceOwner, _pendingSubtitlePreferenceOwner) &&
            sidecarGeneration == CurrentSubtitleSidecarGeneration() &&
            loadGeneration == Volatile.Read(ref _loadedMediaInitGeneration);
        int previousSid;
        lock (_subtitleTrackStateLock)
        {
            if (!CurrentSelection()) return false;
            previousSid = (int)Math.Round(mpv.GetPropertyDouble("sid"));
        }
        if (IsUnsupportedBitmapSubtitle(track))
        {
            // After a burn-in reload, the post-load selection lands here with
            // the track the plan already burns in. Replanning again would
            // reload and reselect it indefinitely.
            if (IsBurnedInSubtitle(session, track))
                return true;
            return false;
        }

        if (string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase) &&
            CanUseNativeEmbeddedSubtitleTrack())
        {
            var sid = ResolveNativeEmbeddedSid(track);
            if (sid > 0)
            {
                lock (_subtitleTrackStateLock)
                {
                    if (!CurrentSelection()) return false;
                    mpv.SetSubtitleTrack(sid);
                    if ((int)Math.Round(mpv.GetPropertyDouble("sid")) == sid) return true;
                }
            }
        }

        // Remux/HLS need the advertised complete sidecar, as do external tracks.
        // mpv fetches it once; cached SIDs remain valid until the next file load.
        if (SelectSubtitleSidecar(track, CurrentSelection)) return true;
        lock (_subtitleTrackStateLock)
        {
            if (CurrentSelection()) mpv.SetSubtitleTrack(previousSid);
        }
        return false;
    }

    private bool SelectSubtitleSidecar(SubtitleTrackInfo track, Func<bool> currentSelection)
    {
        var mpv = _mpv;
        if (mpv == null || track.Unplayable) return false;

        // Serialize adds, not viewer choices. A remote sub-add can block for
        // the download; Off and native selections must still own sid meanwhile.
        lock (_subtitleAddGate)
        {
            string source;
            lock (_subtitleTrackStateLock)
            {
                if (!currentSelection()) return false;
                if (_loadedExternalSubtitleSids.TryGetValue(track.Index, out var loadedSid))
                {
                    if (mpv.GetSubtitleTrackIds(externalOnly: true).Contains(loadedSid))
                    {
                        mpv.SetSubtitleTrack(loadedSid);
                        return (int)Math.Round(mpv.GetPropertyDouble("sid")) == loadedSid;
                    }
                    _loadedExternalSubtitleSids.Remove(track.Index);
                }

                var pair = _playbackManager?.GetSubtitleUrls()
                    .FirstOrDefault(candidate => candidate.Track.Index == track.Index);
                if (pair == null || string.IsNullOrWhiteSpace(pair.Value.FullUrl)) return false;
                source = _prefetchedSubtitleFiles.TryGetValue(track.Index, out var local) ? local : pair.Value.FullUrl;
            }
            var label = !string.IsNullOrEmpty(track.Label) ? track.Label : track.Language ?? "Unknown";
            var fileLoadGeneration = Volatile.Read(ref _fileLoadGeneration);
            var previousIds = mpv.GetSubtitleTrackIds().ToHashSet();
            // mpv's auto flag can select the new track when sid is Off, even
            // when a newer viewer choice retired this blocking add's owner.
            var added = mpv.AddSubtitle(source, label, track.Language, select: false);
            var newIds = mpv.GetSubtitleTrackIds(externalOnly: true).Where(id => !previousIds.Contains(id)).ToArray();
            lock (_subtitleTrackStateLock)
            {
                if (!currentSelection() || !added || newIds.Length != 1)
                {
                    // Undo only this add, never tracks from a replacement load
                    // whose native IDs may have been reused in the meantime.
                    if (!_closing && ReferenceEquals(mpv, _mpv) &&
                        fileLoadGeneration == Volatile.Read(ref _fileLoadGeneration))
                    {
                        if (added && newIds.Length == 1) mpv.RemoveSubtitle(newIds[0]);
                        if (_activeSubtitleServerIndex < 0) mpv.SetSubtitleTrack(0);
                    }
                    return false;
                }
                var sid = newIds[0];
                _loadedExternalSubtitleSids[track.Index] = sid;
                mpv.SetSubtitleTrack(sid);
                return (int)Math.Round(mpv.GetPropertyDouble("sid")) == sid;
            }
        }
    }

    /// <summary>
    /// A synchronous libmpv sub-add of an HTTP URL blocks its caller for the
    /// whole download (the UI thread for player-overlay selections). Download a
    /// remote text sidecar off that thread first; SelectSubtitleSidecar then adds
    /// the local file. Any failure keeps the previous URL path.
    /// </summary>
    private long CurrentSubtitleSidecarGeneration() => Interlocked.Read(ref _subtitleSidecarGeneration);

    private async Task<SubtitlePrefetchOutcome> PrefetchSubtitleSidecarAsync(SubtitleTrackInfo track)
    {
        if (track.Unplayable) return SubtitlePrefetchOutcome.Error;
        if (_mpv == null) return SubtitlePrefetchOutcome.Retired;
        if (IsUnsupportedBitmapSubtitle(track) ||
            string.Equals(track.Source, "embedded", StringComparison.OrdinalIgnoreCase) &&
            CanUseNativeEmbeddedSubtitleTrack() && ResolveNativeEmbeddedSid(track) > 0)
            return SubtitlePrefetchOutcome.Ready;
        long generation;
        var loadedMediaGeneration = Volatile.Read(ref _loadedMediaInitGeneration);
        using var selection = BeginSubtitleRead();
        var manualChoice = _pendingSubtitlePreferenceOwner;
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var manager = _playbackManager;
        var mpv = _mpv;
        Uri url;
        lock (_subtitleTrackStateLock)
        {
            if (_loadedExternalSubtitleSids.ContainsKey(track.Index) || _prefetchedSubtitleFiles.ContainsKey(track.Index)) return SubtitlePrefetchOutcome.Ready;
            var pair = manager?.GetSubtitleUrls().FirstOrDefault(candidate => candidate.Track.Index == track.Index);
            if (pair == null || !Uri.TryCreate(pair.Value.FullUrl, UriKind.Absolute, out var parsed) ||
                parsed.Scheme is not ("http" or "https") || SubtitleSidecarFile.Extension(null, parsed, track.Codec) == null)
                return SubtitlePrefetchOutcome.Fallback;
            url = parsed;
            generation = _subtitleSidecarGeneration;
        }
        bool CurrentOwner() => !_closing && ReferenceEquals(authority, _activeAuthority) &&
            authority.ApiClient.IsCurrentContext(context) && ReferenceEquals(manager, _playbackManager) && ReferenceEquals(mpv, _mpv) &&
            ReferenceEquals(selection, Volatile.Read(ref _subtitleReadOperation)) && !selection.Token.IsCancellationRequested &&
            ReferenceEquals(manualChoice, _pendingSubtitlePreferenceOwner) &&
            CurrentSubtitleSidecarGeneration() == generation && loadedMediaGeneration == Volatile.Read(ref _loadedMediaInitGeneration);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(_authorityLifetimeCts.Token, selection.Token,
            manualChoice?.Token ?? CancellationToken.None);
        using var watchStop = new CancellationTokenSource();
        // Profile changes have no cancellation event. Observe captured context
        // during requests as well as delays so retired reads stop promptly.
        async Task WatchOwnerAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(50, watchStop.Token).ConfigureAwait(false);
                    if (!CurrentOwner()) { bounded.Cancel(); return; }
                }
            }
            catch (OperationCanceledException) when (watchStop.IsCancellationRequested) { }
        }
        var watch = WatchOwnerAsync();
        string? path = null;
        try
        {
            if (!CurrentOwner()) return SubtitlePrefetchOutcome.Retired;
            // Same URL mpv would request; never attach account Authorization
            // to server-advertised CDN/worker sidecars.
            var read = await SubtitleSidecarReader.ReadAsync(SubtitleSyncDownloadClient, url, track.Codec, bounded.Token);
            if (!CurrentOwner()) return SubtitlePrefetchOutcome.Retired;
            if (read.Outcome == SubtitleSidecarReadOutcome.Busy) return SubtitlePrefetchOutcome.Busy;
            if (read.Outcome == SubtitleSidecarReadOutcome.Error) return SubtitlePrefetchOutcome.Error;
            if (read.Outcome == SubtitleSidecarReadOutcome.Fallback) return SubtitlePrefetchOutcome.Fallback;
            path = Path.Combine(Path.GetTempPath(), "silo-subtitle-" + Guid.NewGuid().ToString("N") + read.Extension);
            await File.WriteAllBytesAsync(path, read.Bytes!, bounded.Token);
            lock (_subtitleSyncGate)
            lock (_subtitleTrackStateLock)
            {
                if (generation != _subtitleSidecarGeneration || !CurrentOwner()) return SubtitlePrefetchOutcome.Retired;
                if (_prefetchedSubtitleFiles.ContainsKey(track.Index)) return SubtitlePrefetchOutcome.Ready;
                _prefetchedSubtitleFiles[track.Index] = path;
                _subtitleSyncFiles.Add(path);
                path = null;
            }
            return SubtitlePrefetchOutcome.Ready;
        }
        catch (OperationCanceledException) { return SubtitlePrefetchOutcome.Retired; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            LogToFile("state_trace.txt", $"Subtitle sidecar prefetch skipped: {ex.GetType().Name}");
            return CurrentOwner() ? SubtitlePrefetchOutcome.Error : SubtitlePrefetchOutcome.Retired;
        }
        finally
        {
            watchStop.Cancel();
            await watch;
            if (path != null) try { File.Delete(path); } catch { }
        }
    }

    private int ResolveNativeEmbeddedSid(SubtitleTrackInfo track)
    {
        if (_mpv == null || track.Unplayable || !CanUseNativeEmbeddedSubtitleTrack()) return 0;
        // Pre-v3 rows carry their raw label directly; adapted v3 display labels
        // may instead be synthesized from the language and are not identity.
        var title = track.NativeTitle ?? (_playbackManager?.CurrentSession?.ProtocolVersion < 3 ? track.Label : null);
        return NativeSubtitleSelection.Resolve(new(0, track.StreamIndex, track.ContainerTrackId,
            track.Codec, track.Language, title, track.Forced, track.HearingImpaired),
            _mpv.GetEmbeddedSubtitleTracks(), _mpv.HasFfmpegTrackIndices(), _mpv.HasContainerTrackIds());
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
            ["video_range"] = FormatVideoRangeForHud(version, videoTrack),
            ["color_range"] = videoTrack?.ColorRange ?? "",
            ["audio_bitrate"] = audioTrack?.Bitrate ?? 0,
            ["audio_sample_rate"] = audioTrack?.SampleRate ?? 0,
            ["requested_source"] = BuildRequestedSourceLabel(version),
            ["buffering_limit"] = BufferingQualityPlaybackInfo,
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

        _mpv.SendScriptMessage("osc-set-stream-info", playMethodDisplay, streamType, protocol, vcDisplay, acDisplay,
            (pi?.TargetVideoBitrateKbps ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
            MediaVideoRange.Label(requested)
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatVideoRangeForHud(FileVersion version, VersionVideoTrack? track)
    {
        var compactRange = MediaVideoRange.Label(version);
        if (!string.IsNullOrWhiteSpace(track?.DolbyVision))
        {
            var dolbyVision = track.DolbyVision.StartsWith("Dolby Vision", StringComparison.OrdinalIgnoreCase)
                ? track.DolbyVision
                : $"Dolby Vision {track.DolbyVision}";
            return string.IsNullOrWhiteSpace(track.VideoRange)
                ? dolbyVision
                : $"{dolbyVision} ({track.VideoRange})";
        }

        // Current Silo probes may identify Dolby Vision through dv_profile or
        // a DOVI* video_range_type without populating the display string.
        // Preserve that richer signal instead of collapsing it to HDR10/HLG.
        if (compactRange.StartsWith("DV", StringComparison.Ordinal))
            return compactRange;

        if (!string.IsNullOrWhiteSpace(track?.VideoRange))
            return track.VideoRange;

        return compactRange is { Length: > 0 } range ? range : "SDR";
    }

    private bool IsSubtitleMenuCurrent()
        => !_subtitleMenuPending && !_subtitleVersionSwitchActive &&
           (_subtitleMenuFileId == null || _subtitleMenuFileId == _playbackManager?.CurrentSession?.MediaFileId);

    private void RetireForeignSubtitleSelections()
    {
        lock (_subtitleTrackStateLock)
        {
            var fileId = _playbackManager?.CurrentSession?.MediaFileId;
            if (_pendingInitialSubtitleFileId != fileId)
            {
                _pendingInitialServerSubtitleIndex = null;
                _pendingAutomaticSubtitleSelection = null;
                _pendingInitialSubtitleFileId = null;
            }
            if (_pendingSubtitlePreferenceOwner?.MediaFileId != fileId)
            {
                if (_subtitleVersionSwitchActive && _pendingSubtitlePreferenceIndex == -1 &&
                    _pendingSubtitlePreferenceOwner is { FollowsVersionSwitch: true } owner &&
                    !owner.Token.IsCancellationRequested && ReferenceEquals(owner.Authority, _activeAuthority) &&
                    ReferenceEquals(owner.Manager, _playbackManager))
                {
                    // Off is file-independent, but its preference must be saved
                    // only by the replacement file that actually executes it.
                    _pendingSubtitlePreferenceOwner = owner with { MediaFileId = fileId };
                }
                else
                {
                    _pendingSubtitlePreferenceIndex = null;
                    _pendingSubtitlePreferenceOwner = null;
                }
            }
        }
    }

    private void SendSubtitleListToOsc()
    {
        PublishNativeMenuScopes();
        if (_mpv == null || _playbackManager?.CurrentSession is not { } session) return;

        _subtitleMenuFileId = session.MediaFileId;
        if (!_switchingContent && !IsLoading) _subtitleVersionSwitchActive = false;
        var tracks = session.SubtitleUrls ?? [];
        var jsonTracks = tracks.Select(t => new Dictionary<string, object?>
        {
            ["index"] = t.Index,
            ["language"] = t.Language ?? "",
            ["label"] = t.Label ?? "",
            ["source"] = t.Source ?? "embedded",
            ["codec"] = t.Codec ?? "",
            ["forced"] = t.Forced,
            ["unplayable"] = t.Unplayable,
            ["hearing_impaired"] = t.HearingImpaired,
            ["media_file_id"] = t.MediaFileId
        }).ToArray();

        var json = System.Text.Json.JsonSerializer.Serialize(jsonTracks);
        _mpv.SendScriptMessage("osc-set-subtitles", json,
            session.MediaFileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _subtitleMenuPending = false;
        _mpv.SendScriptMessage(
            "osc-set-active-subtitle",
            (_pendingInitialServerSubtitleIndex ??
                (_executedSubtitleFileId == session.MediaFileId ? _executedSubtitleTrack?.Index ?? -1 : -1))
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
        EnsureSubtitleSync();
        SendSubtitleSyncState();
    }

    private void SendAudioTrackListToOsc()
    {
        PublishNativeMenuScopes();
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
            // The image cache intentionally uses a generic .img extension.
            // Loading that path through Win2D makes codec selection depend on
            // the filename, and the server now supplies chapter previews as
            // WebP. Decode from an IRandomAccessStream so Windows Imaging
            // sniffs the actual file bytes instead.
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var sourceStream = await sourceFile.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(sourceStream);
            token.ThrowIfCancellationRequested();
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);
            token.ThrowIfCancellationRequested();
            var pixels = pixelData.DetachPixelData();
            var width = checked((int)decoder.OrientedPixelWidth);
            var height = checked((int)decoder.OrientedPixelHeight);
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
        lock (_markerDetailsGate)
        {
        if (_mpv == null || _playbackManager?.WatchDetail == null) return;
        var wd = _playbackManager.WatchDetail;

        var markers = new
        {
            content_id = ContentId ?? wd.ContentId,
            file_id = _playbackManager.CurrentSession?.MediaFileId ?? 0,
            load_generation = Volatile.Read(ref _fileLoadGeneration),
            reset_auto_skip = _autoSkipMarkerIdentity.ShouldResetFor(ContentId),
            marker_segments = CurrentMarkerVersion?.MarkerSegments?.Where(segment => segment is not null).ToArray(),
            intro_start = MarkerTimingFor("intro")?.Start ?? 0,
            intro_end = MarkerTimingFor("intro")?.End ?? 0,
            recap_start = MarkerTimingFor("recap")?.Start ?? 0,
            recap_end = MarkerTimingFor("recap")?.End ?? 0,
            credits_start = MarkerTimingFor("credits")?.Start ?? 0,
            credits_end = MarkerTimingFor("credits")?.End ?? 0,
            preview_start = MarkerTimingFor("preview")?.Start ?? 0,
            preview_end = MarkerTimingFor("preview")?.End ?? 0
        };

        var json = System.Text.Json.JsonSerializer.Serialize(markers);
        _mpv.SendScriptMessage("osc-set-markers", json);
        SendMarkerDetailsToOsc();
        }
    }

    private async Task SendAutoSkipSettingsToOscAsync(CancellationToken ct)
    {
        _ = RefreshMarkerDetailsPreferencesAsync(ct);
        await RefreshSeekPreferencesAsync(ct);
        var profileId = _authService.SelectedProfileId;
        var intro = false;
        var credits = false;
        var recap = false;

        if (!string.IsNullOrWhiteSpace(profileId))
        {
            try
            {
                var profiles = await _settingsApi.GetProfilesAsync(ct).ConfigureAwait(false);
                var profile = profiles.Profiles.FirstOrDefault(item => item.Id == profileId);
                intro = profile?.AutoSkipIntro == true;
                credits = profile?.AutoSkipCredits == true;
                recap = profile?.AutoSkipRecap == true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogToFile("state_trace.txt", $"Auto-skip profile settings unavailable: {ex.Message}");
            }

            try
            {
                var effective = await _settingsApi.GetEffectiveSettingsAsync(
                    [AutoSkipIntroSettingKey, AutoSkipCreditsSettingKey, AutoSkipRecapSettingKey],
                    ct).ConfigureAwait(false);
                intro = ResolveEffectiveBool(effective, AutoSkipIntroSettingKey, intro);
                credits = ResolveEffectiveBool(effective, AutoSkipCreditsSettingKey, credits);
                recap = ResolveEffectiveBool(effective, AutoSkipRecapSettingKey, recap);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Older Silo versions may not expose device-effective settings.
                LogToFile("state_trace.txt", $"Auto-skip device settings unavailable: {ex.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();
        try
        {
            var settings = await _settingsApi.GetContractEffectiveSettingsAsync(["playback.intro_skip_mode"], ct: ct);
            var value = settings.Settings.FirstOrDefault()?.Value;
            var mode = value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
            _mpv?.SendScriptMessage("osc-set-intro-mode", mode is "never" or "ask" or "always" ? mode : intro ? "always" : "ask");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { _mpv?.SendScriptMessage("osc-set-intro-mode", intro ? "always" : "ask"); }
        _mpv?.SendScriptMessage(
            "osc-set-auto-skip",
            intro ? "true" : "false",
            recap ? "true" : "false",
            credits ? "true" : "false");
    }

    private static bool ResolveEffectiveBool(
        EffectiveSettingsResponse effective,
        string key,
        bool fallback)
    {
        var setting = effective.Settings.FirstOrDefault(item => item.Key == key);
        if (setting?.HasDeviceOverride != true)
            return fallback;

        var raw = setting.EffectiveValue?.Trim();
        if (string.IsNullOrEmpty(raw))
            return fallback;
        if (bool.TryParse(raw, out var parsed))
            return parsed;
        return int.TryParse(raw, out var numeric) ? numeric != 0 : fallback;
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
            ["active_quality"] = SelectedQualityMenuId,
            ["qualities"] = session.ProtocolVersion >= 3
                ? AvailableQualities.Select(quality => new Dictionary<string, object?>
                {
                    ["id"] = quality.Label,
                    ["label"] = quality.DisplayName ?? quality.Label,
                    ["height"] = quality.Height,
                    ["bitrate_kbps"] = quality.BitrateKbps,
                    ["preserves_source"] = quality.PreservesSource,
                }).ToArray()
                : null,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        _mpv.SendScriptMessage("osc-set-quality-info", json);
        PublishNativeMenuScopes();
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
            else if (State == PlayerState.PictureInPicture)
            {
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(
                    () => SetState(PlayerState.Expanded));
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
            App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() =>
            {
                if (_postRollActive && !_postRollVideoEnded)
                    ReturnFromPostRollPreview();
                else
                    Expand();
            });
        };
        _videoWindow.FullscreenToggleRequested += () =>
        {
            if (_videoWindow.IsFullscreen)
            {
                _videoWindow.ExitFullscreen();
                PublishFullscreenVisualState(false);
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => SetState(PlayerState.Expanded));
            }
            else
            {
                _videoWindow.EnterFullscreen();
                var enteredFullscreen = _videoWindow.SynchronizeFullscreenState();
                PublishFullscreenVisualState(enteredFullscreen);
                App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(
                    () => SetState(enteredFullscreen ? PlayerState.Fullscreen : PlayerState.Expanded));
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

        var dispatch = App.MainWindowInstance?.DispatcherQueue ?? _nativeMenuDispatcher;
        if (dispatch == null) return;

        switch (args[0])
        {
            case "silo-native-subtitle-open":
            case "silo-native-audio-open":
            case "silo-native-stats-open":
            case "silo-native-quality-open":
            case "silo-native-chapters-open":
            case "silo-native-more-open":
            case "silo-native-party-open":
            case "silo-native-speed-open":
                if (PlaybackMenuRequest.TryParse(args, out var menuRequest))
                    dispatch.TryEnqueue(() => _ = HandleNativeMenuOpenAsync(menuRequest));
                break;
            case "silo-native-stats-data":
                if (args.Length == 4)
                    dispatch.TryEnqueue(() => HandleNativeStatsData(args[1], args[2], args[3]));
                break;
            case "silo-native-menu-input":
                if (args.Length == 4)
                    dispatch.TryEnqueue(() => HandleNativeMenuInput(args[1], args[2], args[3]));
                break;
            case "silo-buffering-lower":
                dispatch.TryEnqueue(() => _ = LowerBufferingQualityAsync());
                break;
            case "silo-buffering-restore":
                dispatch.TryEnqueue(() => _ = RestoreBufferingQualityAsync());
                break;
            case "silo-buffering-keep":
                dispatch.TryEnqueue(KeepBufferingQuality);
                break;
            case "silo-buffering-obscured":
                dispatch.TryEnqueue(() => SetBufferingPillObscured(args.Length > 1 && args[1] == "true"));
                break;
            case "silo-exit":
                if (State == PlayerState.Idle) return; // Prevent duplicate close
                dispatch.TryEnqueue(() => _ = CloseAsync());
                break;
            case "silo-playback-retry":
                dispatch.TryEnqueue(() => _ = RetryPlaybackFromTerminalAsync());
                break;
            case "silo-fullscreen-toggle":
                dispatch.TryEnqueue(ToggleFullscreenFromOsc);
                break;
            case "silo-escape-unhandled":
                dispatch.TryEnqueue(HandleUnhandledPlaybackEscape);
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
            case "silo-subtitle-cycle":
                dispatch.TryEnqueue(CycleSubtitleBackward);
                break;
            case "silo-subtitle-search":
                dispatch.TryEnqueue(() => _ = ShowSubtitleSearchDialogAsync());
                break;
            case "silo-subtitle-offset":
                if (args.Length > 1 && int.TryParse(args[1], out var subtitleOffset))
                    _subtitleDispatcher?.TryEnqueue(() => _ = SetSubtitleOffsetAsync(subtitleOffset));
                break;
            case "silo-subtitle-appearance":
                dispatch.TryEnqueue(() => _ = ShowSubtitleAppearanceDialogAsync());
                break;
            case "silo-subtitle-ai":
                dispatch.TryEnqueue(() => _ = ShowSubtitleAiDialogAsync());
                break;
            case "silo-subtitle-sync":
            case "silo-subtitle-reset-timing":
                if (args.Length > 1)
                    _ = SubtitleSyncActionAsync(args[1], args[0] == "silo-subtitle-sync" ? "sync" : "reset");
                break;
            case "silo-subtitle-sync-reload":
                _ = SubtitleSyncActionAsync("", "reload");
                break;
            case "silo-subtitle-sync-refresh":
                _ = SubtitleSyncActionAsync("", "refresh");
                break;
            case "silo-audio-select":
                if (args.Length > 1 && int.TryParse(args[1], out var audioIdx))
                    dispatch.TryEnqueue(() => _ = SwitchAudioTrackAsync(audioIdx));
                break;
            case "silo-version-select":
                if (IsWatchTogetherPlayback) break;
                if (args.Length > 1 && int.TryParse(args[1], out var vFileId))
                {
                    var version = Versions.FirstOrDefault(v => v.FileId == vFileId);
                    if (version != null)
                    {
                        DispatchLegacyPlaybackSwitch(dispatch, quality: false,
                            current => SwitchVersionCoreAsync(version, current));
                    }
                }
                break;
            case "silo-quality-select":
                if (args.Length > 1)
                {
                    var tier = args[1];
                    DispatchLegacyPlaybackSwitch(dispatch, quality: true,
                        current => SwitchQualityTierAsync(tier, acceptSelection: current));
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
                    dispatch.TryEnqueue(() => RequestUserSeek(seekPosition, forceResume));
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
                    dispatch.TryEnqueue(() => RequestUserRelativeSeek(seekDelta, relativeForceResume));
                }
                break;
            case "silo-pause-toggle":
                dispatch.TryEnqueue(RequestUserPauseToggle);
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
                    dispatch.TryEnqueue(() => InvokeSubscribersSafely(
                        WatchTogetherActionRequested,
                        action,
                        nameof(WatchTogetherActionRequested)));
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

    private async Task SwitchQualityTierAsync(string tierId, PlaybackBufferingReplan? bufferingReplan = null,
        PlaybackStartResponse? bufferingExpectedSession = null, Func<bool>? acceptSelection = null)
    {
        if (_playbackTerminalState != null) return;
        if (acceptSelection != null && !acceptSelection()) return;
        var authority = _activeAuthority;
        var manager = _playbackManager;
        if (_mpv == null || manager == null) return;
        var selectionContext = authority.ApiClient.CaptureContext();
        var selectionMpv = _mpv;
        var selectionContent = ContentId;
        var selectionPlayGeneration = Volatile.Read(ref _playRequestGeneration);
        var selectionFileGeneration = Volatile.Read(ref _playbackFileGeneration);
        var loadingOwner = _playbackCts;
        using var switchCts = CancellationTokenSource.CreateLinkedTokenSource(loadingOwner?.Token ?? CancellationToken.None, _authorityLifetimeCts.Token);
        var ct = switchCts.Token;
        bool CurrentSwitch() => !ct.IsCancellationRequested && !_closing &&
            _playbackTerminalState == null && ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(selectionContext) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(selectionMpv, _mpv) && selectionContent == ContentId &&
            selectionPlayGeneration == Volatile.Read(ref _playRequestGeneration) &&
            selectionFileGeneration == Volatile.Read(ref _playbackFileGeneration);
        await CancelAndDrainTransportRestartsAsync(ct);
        if (!CurrentSwitch() || (acceptSelection != null && !acceptSelection())) return;
        if (bufferingExpectedSession != null && !ReferenceEquals(bufferingExpectedSession, manager.CurrentSession)) return;
        _offeredRoomQuality = _offeredFromQuality = null;
        _mpv?.SendScriptMessage("osc-room-quality-offer", "");
        var wasPaused = CaptureUserPausedState();
        var currentPos = CurrentMediaPosition;
        if (bufferingReplan == null)
        {
            lock (_bufferingGate) { _bufferingController?.QualityPicked(); PublishBufferingPillCore(); }
            manager.KeptBufferingEstimateKbps = null;
        }

        // The session has ended; there is nothing to switch.
        var survivingSession = manager.CurrentSession;
        if (survivingSession is null) return;
        var busyOwner = BeginPlaybackBusy(claimState: () => _qualitySwitchActive = true);
        if (loadingOwner != null)
            App.MainWindowInstance?.ShowLoadingOverlay(loadingOwner);
        try
        {
            var quality = tierId.ToLowerInvariant() switch
            {
                "420p" => "480p",
                _ => tierId,
            };
            var session = bufferingReplan == null
                ? await manager.ReplanQualityAsync(quality, currentPos, ct).ConfigureAwait(false)
                : await manager.ReplanBufferingQualityAsync(bufferingReplan, currentPos, ct, bufferingExpectedSession).ConfigureAwait(false);
            busyOwner = busyOwner with { Session = session };
            if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner)) return;
            var version = Versions.FirstOrDefault(v => v.FileId == session.MediaFileId)
                ?? throw new InvalidOperationException("No active version is available for the quality switch.");
            var streamUrl = manager.StreamUrl
                ?? throw new InvalidOperationException("No stream URL was returned for the quality switch.");
            var prepared = await PreparePlaybackTransportAsync(
                session,
                version,
                streamUrl,
                currentPos,
                ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!CurrentSwitch() || !OwnsPlaybackBusy(busyOwner) ||
                !ReferenceEquals(manager.CurrentSession, session))
                return;
            PlayMethod = session.PlayMethod;
            ApplyPreparedTransport(prepared);
            BeginOwnedMpvLoad(prepared, restorePaused: wasPaused,
                acceptLoad: generation =>
                {
                    selectionFileGeneration = generation;
                    busyOwner = busyOwner with { FileGeneration = generation };
                },
                acceptBusy: owner => busyOwner = owner);
            if (!CurrentSwitch()) return;
            // Show the quality actually accepted by the server, including
            // any fallback; a requested tier is not proof of delivery.
            _activeQualityTier = session.ActiveQuality;
            _mpv?.SendScriptMessage("osc-set-active-quality", SelectedQualityMenuId);
            SendQualityInfoToOsc();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogToFile("state_trace.txt", "Protocol-v3 quality switch canceled");
        }
        catch (OperationCanceledException) when (bufferingReplan != null && !ReferenceEquals(bufferingExpectedSession, manager.CurrentSession))
        { LogToFile("state_trace.txt", "Buffering quality action superseded by another plan."); }
        catch (Exception ex)
        {
            if (!CurrentSwitch()) return;
            var released = ReleasePlaybackBusy(busyOwner, () =>
            {
                _qualitySwitchActive = false;
                if (!CurrentSwitch()) return;
                LogToFile("player_quality_switch_error.txt", ex.ToString());
                var (title, detail) = DescribePlaybackError(ex);
                ErrorMessage = detail;
                ShowNotice(title, detail, "error");
                _mpv?.SendScriptMessage("osc-set-active-quality", SelectedQualityMenuId);
            });
            if (released != null && loadingOwner != null)
                App.MainWindowInstance?.HideLoadingOverlay(loadingOwner,
                    collapse => RunIfBusyReleaseCurrent(released, collapse));
            if (released != null && CurrentSwitch())
                await ReconcileDeferredSubtitleSelectionAsync(manager, survivingSession);
        }
        finally
        {
            if (!CurrentSwitch())
            {
                var released = ReleasePlaybackBusy(busyOwner, () => _qualitySwitchActive = false);
                if (released != null && loadingOwner != null)
                    App.MainWindowInstance?.HideLoadingOverlay(loadingOwner,
                        collapse => RunIfBusyReleaseCurrent(released, collapse));
            }
        }
    }

    private async Task ShowSubtitleSearchDialogAsync(PlaybackMenuRequest? nativeRequest = null)
    {
        RetireNativeMenus();
        var session = _playbackManager?.CurrentSession;
        var mainWindow = App.MainWindowInstance;
        if (session == null || mainWindow?.Content?.XamlRoot == null)
            return;

        var snapshot = CapturePlaybackUiSnapshot();
        var preferredLanguage = session.SubtitleUrls
            .Select(track => track.Language)
            .FirstOrDefault(language => !string.IsNullOrWhiteSpace(language));
        var changed = false;
        long? downloadedSubtitleId = null;
        var dialog = new SiloPlayer.Controls.SubtitleSearchDialog(_activeAuthority, session.MediaFileId, preferredLanguage);
        dialog.SubtitleDownloaded += subtitleId =>
        {
            changed = true;
            downloadedSubtitleId = subtitleId;
        };

        if (!await _playbackDialogGate.WaitAsync(0))
            return;
        var restorePlayerInput = false;
        try
        {
            restorePlayerInput = await PlaybackDialogHost.ShowAsync(
                _videoWindow?.Hwnd ?? IntPtr.Zero,
                async (xamlRoot, hostHwnd) =>
                {
                    dialog.XamlRoot = xamlRoot;
                    dialog.HostWindowHandle = hostHwnd;
                    if (snapshot.IsCurrent())
                        await ShowOwnedSubtitleDialogAsync(dialog, snapshot, hostHwnd);
                }, PlaybackDialogHost.IsForegroundOwnedBy(_videoWindow?.Hwnd ?? IntPtr.Zero), snapshot.IsCurrent);
        }
        finally
        {
            _playbackDialogGate.Release();
            RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);
            NotifySubtitleDialogClosed(snapshot, nativeRequest);
            PublishNativeMenuScopes();
        }

        if (!changed || !snapshot.IsCurrent() || _closing || !ReferenceEquals(session, _playbackManager?.CurrentSession))
            return;

        // The refresh-and-select path snapshots the live state at the actual
        // attachment boundary. Do not restore the older dialog-open position:
        // playback is intentionally allowed to continue behind the modal.
        await RefreshSubtitlesAfterAiAsync(session.MediaFileId, downloadedSubtitleId);
        if (downloadedSubtitleId is > 0 && _subtitleSyncCts is { } syncOwner)
            await ReadSubtitleSyncAsync("stored-" + downloadedSubtitleId.Value, syncOwner.Token, watch: true);
    }

    private async Task ShowSubtitleAppearanceDialogAsync(PlaybackMenuRequest? nativeRequest = null)
    {
        RetireNativeMenus();
        var mainWindow = App.MainWindowInstance;
        if (mainWindow?.Content?.XamlRoot == null)
            return;

        var snapshot = CapturePlaybackUiSnapshot();
        var dialog = new SiloPlayer.Controls.SubtitleAppearanceDialog(_activeAuthority);
        if (!await _playbackDialogGate.WaitAsync(0))
            return;
        var restorePlayerInput = false;
        try
        {
            restorePlayerInput = await PlaybackDialogHost.ShowAsync(
                _videoWindow?.Hwnd ?? IntPtr.Zero,
                async (xamlRoot, hostHwnd) =>
                {
                    dialog.XamlRoot = xamlRoot;
                    if (snapshot.IsCurrent())
                        await ShowOwnedSubtitleDialogAsync(dialog, snapshot, hostHwnd);
                }, PlaybackDialogHost.IsForegroundOwnedBy(_videoWindow?.Hwnd ?? IntPtr.Zero), snapshot.IsCurrent);
        }
        finally
        {
            _playbackDialogGate.Release();
            RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);
            NotifySubtitleDialogClosed(snapshot, nativeRequest);
            PublishNativeMenuScopes();
        }
    }

    private void HandleUnhandledPlaybackEscape()
    {
        if (BufferingQualityPill is "prompt" or "undo") { KeepBufferingQuality(); return; }
        if (_videoWindow == null || State == PlayerState.Idle)
            return;

        if (_videoWindow.IsFullscreen)
        {
            _videoWindow.ExitFullscreen();
            PublishFullscreenVisualState(false);
            SetState(PlayerState.Expanded);
            return;
        }

        // The browser WebUI returns the video to its playback page when the
        // native PiP surface is dismissed. Escape/controller Back should do
        // the same here instead of terminating the playback session.
        if (State == PlayerState.PictureInPicture)
        {
            SetState(PlayerState.Expanded);
            return;
        }

        _videoWindow.Hide();
        _ = CloseAsync();
    }

    private async Task ShowSubtitleAiDialogAsync(PlaybackMenuRequest? nativeRequest = null)
    {
        RetireNativeMenus();
        var mainWindow = App.MainWindowInstance;
        if (mainWindow == null)
            return;

        var snapshot = CapturePlaybackUiSnapshot();
        if (!await _playbackDialogGate.WaitAsync(0))
            return;
        var restorePlayerInput = false;
        try
        {
            restorePlayerInput = await PlaybackDialogHost.ShowAsync(
                _videoWindow?.Hwnd ?? IntPtr.Zero,
                (xamlRoot, hostHwnd) => mainWindow.ShowSubtitleAiDialogAsync(xamlRoot, snapshot.IsCurrent, PlaybackDialogHost.IsForegroundOwnedBy(hostHwnd),
                    dialog => ShowOwnedSubtitleDialogAsync(dialog, snapshot, hostHwnd)),
                PlaybackDialogHost.IsForegroundOwnedBy(_videoWindow?.Hwnd ?? IntPtr.Zero), snapshot.IsCurrent);
        }
        finally
        {
            _playbackDialogGate.Release();
            RestorePlaybackUiSnapshot(snapshot, restorePlayerInput);
            NotifySubtitleDialogClosed(snapshot, nativeRequest);
            PublishNativeMenuScopes();
        }
    }

    private sealed record PlaybackUiSnapshot(PlaybackAuthorityScope Authority, PlaybackManager? Manager,
        bool WasFullscreen, bool WasPaused, double Position, Func<bool> IsCurrent);

    private PlaybackUiSnapshot CapturePlaybackUiSnapshot()
    {
        var authority = _activeAuthority; var context = authority.ApiClient.CaptureContext();
        var manager = _playbackManager; var session = manager?.CurrentSession; var mpv = _mpv; var video = _videoWindow;
        var generation = Volatile.Read(ref _playRequestGeneration); var file = Volatile.Read(ref _fileLoadGeneration);
        var roomGeneration = Volatile.Read(ref _roomPlaybackGeneration);
        var playbackFile = Volatile.Read(ref _playbackFileGeneration);
        bool Current() => !_closing && ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(context) &&
            ReferenceEquals(manager, _playbackManager) && ReferenceEquals(session, manager?.CurrentSession) &&
            ReferenceEquals(mpv, _mpv) && ReferenceEquals(video, _videoWindow) &&
            generation == Volatile.Read(ref _playRequestGeneration) && file == Volatile.Read(ref _fileLoadGeneration) &&
            roomGeneration == Volatile.Read(ref _roomPlaybackGeneration) && playbackFile == Volatile.Read(ref _playbackFileGeneration);
        return new(authority, manager, video?.IsFullscreen == true, CaptureUserPausedState(), CurrentMediaPosition, Current);
    }

    private void NotifySubtitleDialogClosed(PlaybackUiSnapshot snapshot, PlaybackMenuRequest? request)
    {
        if (request == null) _mpv?.SendScriptMessage("osc-subtitle-dialog-closed");
        else _mpv?.SendScriptMessage("osc-subtitle-dialog-closed", request.OwnerToken,
            request.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.IsCurrent() ? "true" : "false");
    }

    private void RestorePlaybackUiSnapshot(PlaybackUiSnapshot snapshot, bool activatePlaybackSurface)
    {
        if (!snapshot.IsCurrent() || _closing || !ReferenceEquals(snapshot.Authority, _activeAuthority) ||
            !ReferenceEquals(snapshot.Manager, _playbackManager) || State is PlayerState.Idle or PlayerState.Minimized)
            return;

        try
        {
            if (snapshot.WasFullscreen)
            {
                _videoWindow?.EnterFullscreen(activate: activatePlaybackSurface);
                var restored = _videoWindow?.SynchronizeFullscreenState() == true;
                SetState(restored ? PlayerState.Fullscreen : PlayerState.Expanded);
                PublishFullscreenVisualState(restored);
            }
            else
            {
                if (_videoWindow?.IsFullscreen == true)
                    _videoWindow.ExitFullscreen();
                if (State == PlayerState.Fullscreen)
                    SetState(PlayerState.Expanded);
                else
                    _videoWindow?.Show();
                PublishFullscreenVisualState(false);
            }
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Restore player dialog surface failed: {ex.Message}");
            if (State == PlayerState.Fullscreen)
                SetState(PlayerState.Expanded);
            else
                _videoWindow?.Show();
            PublishFullscreenVisualState(_videoWindow?.IsFullscreen == true);
        }

        RestorePlaybackStateAfterSubtitleChange(snapshot.WasPaused, snapshot.Position, allowSeek: false);
    }

    private async Task SendSubtitleAiAvailabilityToOscAsync(CancellationToken ct)
    {
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var manager = _playbackManager;
        var capabilitySession = manager?.CurrentSession;
        var generation = Volatile.Read(ref _playRequestGeneration);
        var fileGeneration = Volatile.Read(ref _playbackFileGeneration);
        var available = false;
        try
        {
            var status = await authority.PlaybackApi.GetSubtitleAiStatusAsync(ct);
            var session = _playbackManager?.CurrentSession;
            var currentVersion = session == null
                ? null
                : Versions.FirstOrDefault(version => version.FileId == session.MediaFileId);
            var canTranslate = status.Enabled &&
                session?.SubtitleUrls.Any(IsTranslatableSubtitleSource) == true;
            var canTranscribe = status.TranscribeEnabled &&
                currentVersion?.AudioTracks?.Count > 0;
            available = canTranslate || canTranscribe;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"Subtitle AI capability check failed: {ex.Message}");
        }
        if (ct.IsCancellationRequested || !ReferenceEquals(authority, _activeAuthority) ||
            !authority.ApiClient.IsCurrentContext(context) || !ReferenceEquals(manager, _playbackManager) ||
            !ReferenceEquals(capabilitySession, manager?.CurrentSession) || generation != Volatile.Read(ref _playRequestGeneration) ||
            fileGeneration != Volatile.Read(ref _playbackFileGeneration)) return;
        _nativeSubtitleAiCapability = new(authority, context, capabilitySession, fileGeneration, available);
        _mpv?.SendScriptMessage("osc-set-subtitle-ai-available", available ? "true" : "false");
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
            var enteredFullscreen = _videoWindow.SynchronizeFullscreenState();
            SetState(enteredFullscreen ? PlayerState.Fullscreen : PlayerState.Expanded);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void PositionVideoForMiniBar()
    {
        if (_videoWindow == null) return;
        if (IsAudiobook) { _videoWindow.Hide(); return; }
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
        if (IsAudiobook)
            _videoWindow?.Hide();
        else if (_postRollActive && !_postRollVideoEnded)
            _videoWindow?.EnterPostRollPreview();
        else if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
        else if (State == PlayerState.PictureInPicture)
            _videoWindow?.EnterPictureInPicture();
        else
            _videoWindow?.SynchronizePosition();
    }

    public void HandleWindowMinimized(bool minimized)
    {
        if (State == PlayerState.Idle) return;
        if (minimized && State != PlayerState.PictureInPicture)
            _videoWindow?.Hide();
        else if (_postRollActive && !_postRollVideoEnded)
            _videoWindow?.EnterPostRollPreview();
        else if (!IsAudiobook && (State == PlayerState.Expanded || State == PlayerState.Fullscreen))
            _videoWindow?.Show();
        else if (State == PlayerState.Minimized)
            PositionVideoForMiniBar();
        else if (State == PlayerState.PictureInPicture)
            _videoWindow?.EnterPictureInPicture();
    }

    // ── Close / Dispose ──────────────────────────────────────────────────

    private bool _closing;
    private long _closeGeneration;
    private readonly object _closeTaskGate = new();
    private TaskCompletionSource? _closeCompletion;

    public Task CloseAsync()
    {
        lock (_closeTaskGate)
        {
            if (_closeCompletion is { Task.IsCompleted: false }) return _closeCompletion.Task;
            var completion = _closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _closing = true;
            RetireNativeMenus();
            _ = CompleteCloseAsync(completion);
            return completion.Task;
        }
    }

    private async Task CompleteCloseAsync(TaskCompletionSource completion)
    {
        try { await CloseCoreAsync(); _closing = false; completion.TrySetResult(); }
        catch (Exception ex) { _closing = false; completion.TrySetException(ex); }
    }

    private async Task CloseCoreAsync()
    {
        Interlocked.Increment(ref _closeGeneration);
        EndBufferingQuality();
        var closingAuthority = _activeAuthority;
        await Task.WhenAny(FlushSubtitleOffsetAsync(), Task.Delay(2000));
        ResetSubtitlePreferenceSession();
        ResetVideoPlaybackRateSession();
        _authorityLifetimeCts.Cancel();
        _shuffleController?.Leave();
        ActiveLibraryId = null;

        LogToFile("state_trace.txt", $"CloseAsync called: State={State} _switchingContent={_switchingContent}");
        await CancelAndDrainPlayRequestAsync();
        await CancelAndDrainTransportRestartsAsync();
        var loadingOwner = _playbackCts;
        Volatile.Write(ref _mpvLoadingOverlayOwner, null);
        CancelPendingFileLoadTimeout();
        CancelPendingLoadedMediaInitialization();
        StopPlaybackStallWatchdog();
        if (State == PlayerState.Fullscreen)
            ExitAnyFullscreen();

        string? closedContentId = ContentId;
        double closedPosition = PlaybackRecoveryPosition;
        double closedDuration = CurrentMediaDuration;
        var audiobookProgressTask = IsAudiobook
            ? ReportAudiobookProgressAsync(closedPosition, force: true)
            : Task.CompletedTask;
        var closingManager = _playbackManager;
        var sessionStopTask = closingManager != null
            ? closingManager.StopSessionAsync(
                closedPosition > 0 ? ToSessionPosition(closedPosition) : null,
                isPaused: true)
            : Task.CompletedTask;
        var closedFinalSave = Task.WhenAll(sessionStopTask, audiobookProgressTask);
        PendingProgressSave = Task.WhenAll(PendingProgressSave, closedFinalSave);

        // B15 + F4: publish PlaybackProgressUpdated so Home / History /
        // ItemDetail view models reflect the new position without waiting
        // for a full refetch. "Completed" = watched past 90% of duration,
        // matching the webui `isCompleted` heuristic.
        if (!HasRemoteAuthority && !string.IsNullOrEmpty(closedContentId) && closedPosition > 0)
        {
            _watchDetailFreshness.TrackFinalSave(closedContentId, closedFinalSave);
            bool completed = closedDuration > 0 && closedPosition >= closedDuration * 0.9;
            try
            {
                WeakReferenceMessenger.Default.Send(
                    new PlaybackProgressUpdated(
                        closedContentId, closedPosition, closedDuration, completed)
                    { Persisted = closedFinalSave });
            }
            catch (Exception ex) { LogToFile("state_trace.txt", $"Publish progress event error: {ex.Message}"); }
        }

        FlushVolumeStateOnClose();

        // Clear the next-episode hint BEFORE stopping mpv. _mpv.Stop()
        // fires end-file → _mpvPlaybackEndedHandler which checks
        // NextEpisodeContentId. If it's still set, the handler shows the
        // Playing Next cinematic — wrong when the user manually clicked X
        // to exit. Clearing first ensures the handler takes the natural-end
        // path (CloseAsync dispatch) instead of the next-episode path.
        ClearNextEpisodeHint();
        ClearPlaybackTerminalState();
        StopSubtitleSync();
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);
        ClearAudiobookSleepTimer();

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
        await audiobookProgressTask.ConfigureAwait(false);

        ContentId = null;
        Title = "";
        Subtitle = null;
        Position = 0;
        Duration = 0;
        IsPaused = true;
        IsLoading = false;
        SetSwitchingContent(false);
        _qualitySwitchActive = false;
        _prematureEofRecoveryActive = false;
        _prematureEofRecoveryPosition = 0;
        _prematureEofLastAttemptMs = 0;
        _prematureEofStreak = 0;
        _activeTransportPlan = null;
        _recoveryPauseIntentOverride = null;
        _timelineOffsetSeconds = 0;
        _transportStartPosition = null;
        _transportDurationSeconds = null;
        _canSeekAnywhere = true;
        _activeQualityTier = "original";
        _requestedMediaFileId = null;
        IsAudiobook = false;
        AudiobookPosterUrl = null;
        AudiobookAuthor = null;
        AudiobookNarrator = null;
        _audiobookPartOffsetSeconds = 0;
        _audiobookTotalDurationSeconds = 0;
        Interlocked.Exchange(ref _lastAudiobookProgressReportTicks, 0);
        _pendingSubtitleSelection = null;
        _pendingInitialServerSubtitleIndex = null;
        _autoSkipMarkerIdentity.Clear();
        _activeSubtitleServerIndex = -1;
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

        if (loadingOwner != null)
            App.MainWindowInstance?.HideLoadingOverlay(loadingOwner);
        _videoWindow?.Hide();
        SetState(PlayerState.Idle);
        if (!ReferenceEquals(closingAuthority, _localAuthority))
        {
            try { await PendingProgressSave; } catch { /* Final writes are best effort. */ }
            _activeAuthority = _localAuthority;
            _shuffleController = null;
            _watchDetailPrefetches.Clear();
            _subtitlePreferences = null;
            _subtitleAppearance = null;
            _seekContext = null;
            Interlocked.Increment(ref _seekGeneration);
            RemoteAuthorityEnded?.Invoke(closingAuthority);
        }
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
        RetireNativeMenus();
        ResetVideoPlaybackRateSession();
        _volumePersistenceDisposed = true;
        try { _volumeSaveTimer?.Stop(); } catch { /* Dispatcher may be shutting down. */ }
        EndBufferingQuality();
        NetworkInformation.NetworkStatusChanged -= OnPlaybackNetworkChanged;
        _localAuthority.Auth.LoggedOut -= RetireConnectionObservation;
        if (_subtitlePreferenceEvents != null) _subtitlePreferenceEvents.LocalProfileSettingChanged -= OnSubtitlePreferenceChanged;
        if (_markerDetailsEvents != null) _markerDetailsEvents.LocalProfileSettingChanged -= OnMarkerDetailsSettingChanged;
        _subtitleOffsetDelay?.Cancel();
        _closing = true;
        _authorityLifetimeCts.Cancel();
        CancelPendingPlayRequest();
        CancelPendingTransportRestarts();
        CancelPendingFileLoadTimeout();
        CancelPendingLoadedMediaInitialization();
        StopPlaybackStallWatchdog();
        ClearChapterThumbnailOverlayCache();
        StopSubtitleSync();
        ClearLiveSubtitleTranslation(restorePreviousSubtitle: false);
        UpdateDisplayWakeLock(false);
        DisconnectWebSocket();
        try { _playbackCts?.Cancel(); } catch (ObjectDisposedException) { }
        _playbackCts?.Dispose();
        _playbackCts = null;

        // Exit fullscreen BEFORE disposing the video window, so
        // ExitAnyFullscreen can still see which path is active.
        if (State == PlayerState.Fullscreen)
            ExitAnyFullscreen();

        FlushVolumeStateOnClose();

        _videoWindow?.Dispose();
        _videoWindow = null;
        StopDirectStreamProxy();
        StopHlsProxy();
        if (_playbackManager != null)
        {
            _playbackManager.ProgressReportingFailed -= OnProgressReportingFailed;
            _playbackManager.Dispose();
            _playbackManager = null;
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
        var socket = new PlaybackWebSocket(_apiClient, _playbackApi, sessionId);
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

    private sealed record CapturedAudioPreference(PlaybackAuthorityScope Authority, ApiRequestContext Owner,
        string Key, AudioPreference Preference, CancellationToken OwnerToken);

    private CapturedAudioPreference? CaptureAudioPreference(int trackIndex, string? language = null)
    {
        var authority = _activeAuthority;
        var key = GetPrefsKey();
        if (string.IsNullOrEmpty(key)) return null;
        var track = ActiveVersion?.AudioTracks?.ElementAtOrDefault(trackIndex);
        return new(authority, authority.ApiClient.CaptureContext(), key,
            new AudioPreference { AudioTrackIndex = trackIndex, AudioLanguage = track?.Language ?? language,
                TrackSignature = CreateAudioTrackSignature(track) }, _authorityLifetimeCts.Token);
    }

    private async Task PersistAudioPreferenceAsync(CapturedAudioPreference? captured)
    {
        if (captured == null) return;
        try
        {
            await captured.Authority.CatalogApi.SetAudioPrefsAsync(captured.Owner, captured.Key,
                captured.Preference, captured.OwnerToken);
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
            case "subtitle_sync_updated":
                ApplySubtitleSyncEvent(ev.Payload, timingChanged: false);
                break;
            case "subtitle_timing_changed":
                ApplySubtitleSyncEvent(ev.Payload, timingChanged: true);
                break;
            case "chapter_thumbnail_ready":
                ApplyRealtimeChapterThumbnail(ev.Payload);
                break;
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

    private void ApplyRealtimeChapterThumbnail(JsonElement payload)
    {
        if (!TryGetPayloadInt(payload, "file_id", out var fileId) ||
            !TryGetPayloadInt(payload, "chapter_index", out var chapterIndex))
            return;

        var session = _playbackManager?.CurrentSession;
        if (session == null || session.MediaFileId != fileId)
            return;

        var thumbnailUrl = TryGetPayloadString(payload, "thumbnail_url");
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
            return;

        var version = Versions.FirstOrDefault(item => item.FileId == fileId);
        var chapter = version?.Chapters?.FirstOrDefault(item => item.Index == chapterIndex);
        if (chapter == null)
            return;

        var thumbnailThumbhash = TryGetPayloadString(payload, "thumbnail_thumbhash");
        if (string.Equals(chapter.ThumbnailUrl, thumbnailUrl, StringComparison.Ordinal) &&
            string.Equals(chapter.ThumbnailThumbhash, thumbnailThumbhash, StringComparison.Ordinal))
            return;

        chapter.ThumbnailUrl = thumbnailUrl;
        chapter.ThumbnailThumbhash = thumbnailThumbhash;

        // The current WebUI patches its active playback version in response to
        // this event. Keep both native player surfaces in sync without
        // recreating the stream or disturbing play/pause state.
        _mpv?.SendScriptMessage(
            "osc-patch-chapter-thumbnail-url",
            chapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            thumbnailUrl);
        InvokeSubscribersSafely(ChaptersChanged, nameof(ChaptersChanged));
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
            if (_resumeAfterLiveSubtitleBuffer)
                _mpv.Pause();

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiloPlayer", "live-subtitles");
            Directory.CreateDirectory(folder);
            _liveSubtitlePath = Path.Combine(folder, $"{session.SessionId}-{jobId}.vtt");
            WriteLiveSubtitleFileLocked();

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

    public async Task RefreshSubtitlesAfterAiAsync(int mediaFileId, long? preferredSubtitleId = null)
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
                SyncKey = "stored-" + entry.Id,
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
                await SelectSubtitleByServerIndexAsync(preferred.Index).ConfigureAwait(false);
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

            var versions = MarkerVersions().Where(version => version.FileId == fileId).ToArray();
            if (versions.Length == 0) return;

            foreach (var candidate in versions) PlaybackMarkerUpdate.Apply(candidate, payload);
            var version = versions[0];

            var activeFileId = _playbackManager?.CurrentSession?.MediaFileId ?? 0;
            if (activeFileId == fileId && _playbackManager?.WatchDetail != null)
            {
                if (payload.TryGetProperty("intro", out _)) _playbackManager.WatchDetail.Intro = version.Intro;
                if (payload.TryGetProperty("credits", out _)) _playbackManager.WatchDetail.Credits = version.Credits;
                if (payload.TryGetProperty("recap", out _)) _playbackManager.WatchDetail.Recap = version.Recap;
                if (payload.TryGetProperty("preview", out _)) _playbackManager.WatchDetail.Preview = version.Preview;
                SendMarkersToOsc();
                InvokeSubscribersSafely(MarkersChanged, nameof(MarkersChanged));
            }

            LogToFile("state_trace.txt", $"Realtime markers_updated applied: fileId={fileId} intro={FormatMarker(version.Intro)} recap={FormatMarker(version.Recap)} credits={FormatMarker(version.Credits)} preview={FormatMarker(version.Preview)}");
        }
        catch (Exception ex)
        {
            LogToFile("state_trace.txt", $"markers_updated handling failed: {ex.Message}");
        }
    }

    private static string FormatMarker(TimeRange? marker)
        => marker == null ? "none" : $"{marker.Start:F1}-{marker.End:F1}";

    private Task<CommandResult> HandleWebSocketCommand(WebSocketCommand cmd)
    {
        static Task<CommandResult> Complete(CommandResult result) => Task.FromResult(result);

        switch (cmd.Name)
        {
            case "pause":
                SetPaused(true);
                return Complete(new CommandResult());

            case "unpause":
                SetPaused(false);
                return Complete(new CommandResult());

            case "play_pause":
                SetPaused(_idleRemuxResume.IsReopening ? !_idleRemuxResume.DesiredPaused : _mpv?.IsPaused != true);
                return Complete(new CommandResult());

            case "seek":
                var pos = cmd.GetNumber("position", "position_seconds", "seconds");
                if (pos == null) return Complete(new CommandResult { Status = "rejected", Error = "missing_seek_position" });
                SeekFastTo(pos.Value);
                return Complete(new CommandResult());

            case "set_volume":
                var vol = cmd.GetNumber("volume", "level");
                if (vol == null) return Complete(new CommandResult { Status = "rejected", Error = "missing_volume" });
                var normalizedVolume = Math.Min(1, Math.Max(0, vol.Value));
                _mpv?.SetVolume(normalizedVolume * 100);
                // Match the WebUI command contract: a positive remote volume
                // also restores audible output when the player was muted.
                if (normalizedVolume > 0)
                    _mpv?.SetMute(false);
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

            case "plan_invalidated":
                if (!PlaybackPlanInvalidation.TryCreate(cmd.Payload, out var invalidation))
                    return Complete(new CommandResult
                    {
                        Status = "rejected",
                        Error = "invalid_plan_invalidated_payload",
                    });
                StartPlanInvalidationRecovery(invalidation);
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

    private void StartPlanInvalidationRecovery(PlaybackPlanInvalidation invalidation)
        => _ = ObservePlanInvalidationRecoveryAsync(invalidation);

    private async Task ObservePlanInvalidationRecoveryAsync(
        PlaybackPlanInvalidation invalidation)
    {
        var gateAcquired = false;
        try
        {
            await _planInvalidationGate.WaitAsync().ConfigureAwait(false);
            gateAcquired = true;
            var result = await HandlePlanInvalidationAsync(invalidation).ConfigureAwait(false);
            if (!string.Equals(result.Status, "completed", StringComparison.Ordinal))
            {
                LogToFile(
                    "state_trace.txt",
                    $"Plan invalidation recovery rejected: {result.Error ?? "unknown"}");
            }
        }
        catch (Exception ex)
        {
            SetSwitchingContent(false);
            LogToFile(
                "player_recovery_error.txt",
                $"Unhandled plan invalidation recovery failure: {ex}");
        }
        finally
        {
            if (gateAcquired)
                _planInvalidationGate.Release();
        }
    }

    private async Task<CommandResult> HandlePlanInvalidationAsync(
        PlaybackPlanInvalidation invalidation)
    {
        var manager = _playbackManager;
        var currentSession = manager?.CurrentSession;
        if (manager == null || currentSession == null || currentSession.ProtocolVersion < 3 || _mpv == null)
            return new CommandResult { Status = "rejected", Error = "plan_invalidation_unsupported" };

        var currentPosition = CurrentMediaPosition;
        var wasPaused = CaptureUserPausedState();
        var ct = _playbackCts?.Token ?? CancellationToken.None;
        SetSwitchingContent(true);
        try
        {
            var replacement = await manager.ReplanInvalidatedPlanAsync(
                invalidation.PlanId,
                invalidation.Reason,
                currentPosition,
                ct).ConfigureAwait(false);
            if (replacement == null)
            {
                SetSwitchingContent(false);
                await ReconcileDeferredSubtitleSelectionAsync(manager, currentSession);
                return new CommandResult();
            }

            var version = Versions.FirstOrDefault(item => item.FileId == replacement.MediaFileId)
                ?? throw new InvalidOperationException("The replanned media version is unavailable.");
            var streamUrl = manager.StreamUrl
                ?? throw new InvalidOperationException("No stream URL was returned after plan invalidation.");
            var prepared = await PreparePlaybackTransportAsync(
                replacement,
                version,
                streamUrl,
                currentPosition,
                ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (_closing || State == PlayerState.Idle ||
                !ReferenceEquals(_playbackManager, manager) ||
                !ReferenceEquals(manager.CurrentSession, replacement))
            {
                SetSwitchingContent(false);
                return new CommandResult { Status = "rejected", Error = "playback_session_changed" };
            }

            PlayMethod = replacement.PlayMethod;
            _activeQualityTier = replacement.ActiveQuality;
            ApplyPreparedTransport(prepared);
            BeginMpvLoad(prepared, wasPaused);
            _mpv.SendScriptMessage("osc-set-play-method", PlayMethod ?? "direct");
            LogToFile(
                "state_trace.txt",
                $"Realtime plan invalidation adopted plan={replacement.PlanId} " +
                $"reason={invalidation.Reason} position={currentPosition:F1}");
            return new CommandResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetSwitchingContent(false);
            return new CommandResult { Status = "rejected", Error = "playback_canceled" };
        }
        catch (Exception ex)
        {
            LogToFile("player_recovery_error.txt", $"Plan invalidation failed: {ex}");
            EnterPlaybackTerminalState(ex, currentPosition, "plan_invalidated", wasPaused);
            return new CommandResult
            {
                Status = "rejected",
                Error = ex is PlaybackPlanTerminalException terminal
                    ? terminal.Reason
                    : "plan_invalidation_failed",
            };
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

    private static readonly QueuedLocalLog _queuedLog = new();
    private static readonly Timer _logFlushTimer = new(_ => _queuedLog.TryFlush(), null, 100, 200);

    private static void FlushLogs() => _queuedLog.Flush();

    private static void InvokeSubscribersSafely(Action? handlers, string eventName)
    {
        if (handlers == null)
            return;

        foreach (var subscriber in handlers.GetInvocationList().Cast<Action>())
        {
            try { subscriber(); }
            catch (Exception ex)
            {
                LogToFile("state_trace.txt", $"{eventName} subscriber failed: {ex}");
            }
        }
    }

    private static void InvokeSubscribersSafely<T>(Action<T>? handlers, T value, string eventName)
    {
        if (handlers == null)
            return;

        foreach (var subscriber in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try { subscriber(value); }
            catch (Exception ex)
            {
                LogToFile("state_trace.txt", $"{eventName} subscriber failed: {ex}");
            }
        }
    }

    private static void LogToFile(string fileName, string content)
    {
        _queuedLog.Enqueue(fileName, content);
    }

}
