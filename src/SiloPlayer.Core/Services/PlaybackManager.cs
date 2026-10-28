using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public partial class PlaybackManager : IDisposable
{
    private static readonly string s_appVersion = ResolveAppVersion();

    private readonly PlaybackApi _playbackApi;
    private readonly AuthService _authService;
    private readonly SiloApiClient _apiClient;
    private readonly AudioPassthroughCapabilities? _audioPassthrough;
    private Timer? _progressTimer;
    private CancellationTokenSource? _progressStopCts;
    private string? _sessionId;
    private double _lastReportedPosition;
    private bool _isPaused;
    private readonly SemaphoreSlim _progressGuard = new(1, 1);
    private readonly SemaphoreSlim _replanGuard = new(1, 1);
    private int _consecutiveProgressFailures;
    private PlaybackPlanV3? _currentPlanV3;
    private PlaybackClientCapabilitiesV3? _clientCapabilitiesV3;
    private PlaybackClientContextV3? _clientContextV3;
    private string? _playbackAttemptIdV3;
    private string? _planAttemptIdV3;
    private string _qualityPreferenceV3 = "auto";
    private int? _bandwidthCapKbpsV3;
    private readonly PlaybackRecoveryAttemptHistory _recoveryAttemptHistory = new();
    private PlaybackStartResponse? _directRecoverySession;
    private double _directRecoveryPosition;
    private int _directRecoveryAttempts;

    /// <summary>
    /// Fires when progress reporting has failed 3 consecutive times (network
    /// stall, server-side session reaping, etc.). The server reaps sessions
    /// after ~45s of no progress — once that happens the stream URL 404s and
    /// mpv hangs with an audio buffer loop. Subscribers should replace the
    /// expired session while preserving the viewer's position and pause state.
    /// </summary>
    public event Action<string>? ProgressReportingFailed;

    public PlaybackManager(PlaybackApi playbackApi, CatalogApi catalogApi, AuthService authService, SiloApiClient apiClient, AudioPassthroughCapabilities? audioPassthrough = null)
    {
        _playbackApi = playbackApi;
        _authService = authService;
        _apiClient = apiClient;
        _audioPassthrough = audioPassthrough;
    }

    public string? SessionId => _sessionId;
    public WatchDetailResponse? WatchDetail { get; private set; }
    public PlaybackStartResponse? CurrentSession { get; private set; }
    public string? StreamUrl { get; private set; }
    public string QualityPreference => _qualityPreferenceV3;
    // Auto names the viewer's mode; every fixed checkmark names delivery.
    // Empty means the plan delivers no advertised menu entry.
    public string SelectedQualityMenuId => NormalizeQualityPreference(QualityPreference) == "auto"
        ? "auto"
        : CurrentSession?.AvailableQualities.FirstOrDefault(quality => !quality.PreservesSource &&
            quality.Label == CurrentSession.ActiveQuality)?.Label ?? "";

    public PlaybackQualityV3? GetLowerQualityOption(int? sourceBitrateKbps = null)
    {
        var options = CurrentSession?.AvailableQualities ?? [];
        var active = options.FirstOrDefault(quality => quality.Label == SelectedQualityMenuId);
        var delivered = CurrentSession?.PlaybackInfo?.TargetVideoBitrateKbps ?? sourceBitrateKbps;
        var ceiling = Math.Min(active?.BitrateKbps is > 0 ? active.BitrateKbps.Value : int.MaxValue,
            delivered is > 0 ? delivered.Value : int.MaxValue);
        return options.Where(quality => !quality.PreservesSource && quality.BitrateKbps > 0)
            .OrderByDescending(quality => quality.BitrateKbps)
            .FirstOrDefault(quality => quality.Label != SelectedQualityMenuId &&
                quality.BitrateKbps < ceiling);
    }

    public async Task<PlaybackQualityPreferences> ResolveQualityPreferencesAsync(CancellationToken ct = default)
    {
        var owner = _apiClient.CaptureIdentityContext();
        var capability = await _playbackApi.GetConnectionCapabilityAsync(owner, ct).ConfigureAwait(false);
        var preferences = await PlaybackQualityPreferences.ReadAsync(_apiClient, _authService.SelectedProfile?.QualityPreference,
            ct, capability.Features.Contains("buffering_quality_v1")).ConfigureAwait(false);
        if (!_apiClient.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Playback owner changed.", ct);
        return preferences with { CapabilitySnapshot = (owner, capability) };
    }

    public async Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
    {
        WatchDetail = await _playbackApi.GetWatchDetailAsync(contentId, ct);
        return WatchDetail;
    }

    /// <summary>
    /// Reuses watch data fetched before the user pressed Play. Keeping the
    /// assignment inside PlaybackManager preserves the same session state as a
    /// normal <see cref="GetWatchDetailAsync"/> call while avoiding a duplicate
    /// round trip on the startup path.
    /// </summary>
    public WatchDetailResponse UseWatchDetail(WatchDetailResponse watchDetail)
    {
        ArgumentNullException.ThrowIfNull(watchDetail);
        WatchDetail = watchDetail;
        return watchDetail;
    }

    public FileVersion? SelectBestVersion(
        List<FileVersion> versions,
        string? qualityPreference = null,
        WatchUserData? userData = null)
    {
        // Delegate to the shared ranker (see VersionRanking.SelectDefaultVersion).
        // Respects last-watched file, quality-preference cap, and best-audio-codec.
        return VersionRanking.SelectDefaultVersion(versions, userData, qualityPreference);
    }

    public FileVersion? SelectBestVariantVersion(
        List<FileVersion> versions,
        List<PlaybackVariant>? playbackVariants,
        string? qualityPreference = null,
        WatchUserData? userData = null,
        string? preferredEditionKey = null)
    {
        return VersionRanking.SelectDefaultPlaybackVariantVersion(
            versions,
            playbackVariants,
            userData,
            qualityPreference,
            preferredEditionKey);
    }

    public async Task<PlaybackStartResponse> StartSessionAsync(
        int fileId,
        double startPosition = 0,
        bool forceStartPosition = false,
        int? audioTrackIndex = null,
        bool forceDirectAudioSelection = false,
        bool disableProgressPersistence = false,
        string? qualityPreference = null,
        CancellationToken ct = default,
        PlaybackQualityPreferences? preferences = null,
        int? subtitleTrackIndex = null,
        bool preserveTrackIntent = false)
    {
        preferences ??= await ResolveQualityPreferencesAsync(ct).ConfigureAwait(false);
        if (!_apiClient.IsCurrentContext(preferences.Context)) throw new OperationCanceledException("Playback quality owner changed.", ct);
        if (!preserveTrackIntent) ResetLocalTrackIntent();
        var subtitleRevision = CaptureSubtitleRevision();
        var audioRevision = CaptureAudioRevision();
        var appVersion = s_appVersion;
        var (capabilities, context) = MpvNativePlaybackCapabilities.CreateProtocolV3Profile(
            appVersion,
            _audioPassthrough);
        var request = new PlaybackStartRequestV3
        {
            ProtocolVersion = 3,
            ClientFeatures = ["playback_plan_v3", "plan_invalidated_v1", "subrip_sidecar_v1"],
            FileId = fileId,
            ProfileId = _authService.SelectedProfileId ?? "",
            PlaybackAttemptId = Guid.NewGuid().ToString(),
            QualityPreference = NormalizeQualityPreference(
                qualityPreference ?? preferences.Quality),
            BandwidthCapKbps = preferences.BandwidthCapKbps,
            SubtitleFidelityPreference = "preserve",
            StartPosition = forceStartPosition || disableProgressPersistence
                ? Math.Max(0, startPosition)
                : (startPosition > 0 ? startPosition : null),
            AudioTrackIndex = audioTrackIndex,
            SubtitleTrackIndex = subtitleTrackIndex,
            ProgressPersistence = disableProgressPersistence ? "client" : null,
            Metered = false,
            ClientCapabilities = capabilities,
            ClientPlaybackContext = context,
        };

        LogToStateTrace($"StartSession v3: fileId={fileId}, pos={startPosition}, force={forceStartPosition}, attempt={request.PlaybackAttemptId}, codecs_video=[{string.Join(",", capabilities.CodecsVideo)}], codecs_audio=[{string.Join(",", capabilities.CodecsAudio)}], max_res={capabilities.MaxResolution}, hdr={capabilities.Hdr}");

        await ConfigureConnectionStartAsync(request, preferences, ct).ConfigureAwait(false);
        var decision = await _playbackApi.StartPlaybackV3Async(request, ct).ConfigureAwait(false);
        _connectionFeatures = request.ClientFeatures.ToArray();
        var response = AdoptProtocolV3Decision(decision, request.PlaybackAttemptId, request.QualityPreference, request.BandwidthCapKbps);
        _recoveryAttemptHistory.Reset();
        _currentPlanV3 = decision.PlaybackPlan;
        _clientCapabilitiesV3 = capabilities;
        _clientContextV3 = context;
        _playbackAttemptIdV3 = request.PlaybackAttemptId;
        _planAttemptIdV3 = Guid.NewGuid().ToString();
        _qualityPreferenceV3 = request.QualityPreference;
        _bandwidthCapKbpsV3 = request.BandwidthCapKbps;
        _sessionId = response.SessionId;
        CurrentSession = response;
        AcknowledgeSubtitleIntent(subtitleRevision);
        AcknowledgeAudioIntent(audioRevision);
        _lastReportedPosition = Math.Max(0, response.Position);
        _isPaused = response.IsPaused;

        LogToStateTrace($"StartSession response: play_method={response.PlayMethod}, session={response.SessionId}, position={response.Position:F1}");

        var url = PlaybackDeliveryUrl.Resolve(_apiClient.BaseUrl, response.StreamUrl);
        if (response.ProtocolVersion < 3 &&
            response.PlayMethod == "remux" &&
            response.Position > 0 &&
            !PlaybackTransportPlanner.IsHlsStreamUrl(response.StreamUrl))
            url += (url.Contains('?') ? "&" : "?") + "seek=" +
                response.Position.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);

        StreamUrl = url;
        StartProgressReporting();
        _ = RecordRouteEventAsync(response, "plan_selected");
        return response;
    }

    private static string ResolveAppVersion()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
                return "unknown";
            return System.Diagnostics.FileVersionInfo
                .GetVersionInfo(path)
                .ProductVersion?.Split('+')[0] ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string NormalizeQualityPreference(string? qualityPreference)
        => PlaybackQualityPreferences.Normalize(qualityPreference);

    private static string ResolveActiveQuality(PlaybackPlanV3 plan, string qualityPreference, int? bandwidthCapKbps,
        IReadOnlyCollection<string> serverFeatures)
    {
        // A present field is useful even if an older proxy drops feature names.
        // With the feature advertised, absence explicitly means no listed rung.
        if (plan.ActiveQuality != null || serverFeatures.Contains("plan_active_quality_v1"))
            return plan.AvailableQualities.FirstOrDefault(quality => !quality.PreservesSource &&
                quality.Label == plan.ActiveQuality)?.Label ?? "";

        if (plan.Delivery != "server_transcode_hls")
            return "auto";

        var requested = plan.AvailableQualities.FirstOrDefault(quality =>
            !quality.PreservesSource && quality.Label == qualityPreference);
        var source = plan.AvailableQualities.FirstOrDefault(quality =>
            quality.PreservesSource && quality.Label == "original");
        var sourceHeight = plan.Source?.Height ?? source?.Height;
        var sourceBitrate = plan.Source?.BitrateKbps ?? source?.BitrateKbps;

        // The server advertises nominal tiers, but bounds the actual recipe by
        // source height (including cinema crops) and bitrate. Prefer the user's
        // tier when those bounds explain the recipe; never trust the request alone.
        if (requested?.Height is > 0 && requested.BitrateKbps is > 0 &&
            sourceHeight is > 0 && sourceBitrate is > 0 &&
            plan.EffectiveRecipe.Height == Math.Min(requested.Height.Value, sourceHeight.Value) &&
            plan.EffectiveRecipe.BitrateKbps == Math.Min(Math.Min(requested.BitrateKbps.Value, sourceBitrate.Value), bandwidthCapKbps ?? int.MaxValue))
            return requested.Label;

        return plan.AvailableQualities.FirstOrDefault(quality =>
            !quality.PreservesSource && quality.Height is > 0 &&
            quality.Height == plan.EffectiveRecipe.Height &&
            quality.BitrateKbps is > 0 && quality.BitrateKbps == plan.EffectiveRecipe.BitrateKbps)?.Label ?? "auto";
    }

    private static PlaybackStartResponse AdoptProtocolV3Decision(
        PlaybackDecisionResponseV3 decision,
        string playbackAttemptId,
        string qualityPreference,
        int? bandwidthCapKbps)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (!string.Equals(decision.Outcome, "playable", StringComparison.OrdinalIgnoreCase) ||
            decision.PlaybackPlan == null)
        {
            var terminal = decision.Terminal;
            var detail = terminal?.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = "The server could not find a compatible playback route.";
            throw new PlaybackPlanTerminalException(
                terminal?.Reason ?? decision.Outcome,
                detail,
                terminal?.Retryable ?? false);
        }

        var plan = decision.PlaybackPlan;
        var playMethod = plan.Delivery switch
        {
            "original_http" => "direct",
            "server_remux_progressive" or "server_remux_hls" => "remux",
            "server_transcode_hls" => "transcode",
            _ => throw new InvalidOperationException($"Unsupported Silo playback delivery '{plan.Delivery}'."),
        };
        var hls = string.Equals(plan.Stream.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ||
            plan.Stream.Url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

        return new PlaybackStartResponse
        {
            ProtocolVersion = 3,
            PlaybackAttemptId = playbackAttemptId,
            PlanId = plan.PlanId,
            PlanAttemptKey = plan.PlanAttemptKey,
            Delivery = plan.Delivery,
            SessionId = string.IsNullOrWhiteSpace(plan.SessionId) ? decision.SessionId ?? "" : plan.SessionId,
            MediaFileId = plan.EffectiveMediaFileId,
            PlayMethod = playMethod,
            Position = Math.Max(0, plan.Timeline.SourceStartSeconds),
            IsPaused = false,
            StreamUrl = plan.Stream.Url,
            AudioTrackIndex = plan.SelectedTracks.Audio?.Index ?? 0,
            DurationSeconds = plan.Source.DurationSeconds,
            StreamOriginSeconds = Math.Max(0, plan.Timeline.StreamOriginSeconds),
            PlayerStartSeconds = Math.Max(0, plan.Timeline.PlayerStartSeconds),
            TimelineOffsetSeconds = Math.Max(0, plan.Timeline.TimelineOffsetSeconds),
            CanSeekAnywhere = plan.Timeline.CanSeekAnywhere,
            SelectedSubtitleTrackId = plan.SelectedTracks.Subtitle?.Id ?? plan.Subtitle.TrackId,
            SubtitleMode = plan.Subtitle.Mode,
            SelectedSubtitleArtifactUrl = plan.Subtitle.Artifact?.Url,
            SubtitleTimingOriginSeconds = plan.Subtitle.Artifact?.TimingOriginSeconds ?? 0,
            ActiveQuality = ResolveActiveQuality(plan, qualityPreference, bandwidthCapKbps, decision.ServerFeatures),
            AvailableQualities = plan.AvailableQualities,
            PlaybackInfo = new PlaybackInfo
            {
                TargetVideoBitrateKbps = plan.Delivery == "server_transcode_hls" &&
                    plan.EffectiveRecipe.BitrateKbps is > 0 ? plan.EffectiveRecipe.BitrateKbps : null,
                StreamType = hls ? "hls" : "progressive",
                TranscodeAudio = plan.Delivery is "server_transcode_hls",
                VideoCodec = plan.EffectiveRecipe.VideoCodec ?? "",
                AudioCodec = plan.EffectiveRecipe.AudioCodec ?? "",
            },
            SubtitleUrls = plan.Subtitle.Inventory.Select(track => new SubtitleTrackInfo
            {
                SyncKey = track.SyncKey,
                TrackId = track.TrackId,
                Index = track.CombinedIndex,
                MediaFileId = plan.EffectiveMediaFileId,
                Language = track.Language ?? "",
                Codec = track.Codec,
                Label = string.IsNullOrWhiteSpace(track.Label) ? track.Language ?? "Subtitle" : track.Label,
                Source = track.Source,
                Url = track.Url ?? "",
                FontBundleUrl = track.FontBundleUrl,
                Forced = track.Forced,
                HearingImpaired = track.HearingImpaired,
                Delivery = track.Delivery,
            }).ToList(),
        };
    }

    public Task<PlaybackStartResponse> ReplanQualityAsync(
        string qualityPreference,
        double positionSeconds,
        CancellationToken ct = default)
        => ReplanAsync(
            "quality_change",
            positionSeconds,
            NormalizeQualityPreference(qualityPreference),
            selectedTracks => { },
            failure: null,
            ct);

    public Task<PlaybackStartResponse> ReplanAudioAsync(
        int audioTrackIndex,
        double positionSeconds,
        CancellationToken ct = default)
    {
        RecordLocalAudioSelection(audioTrackIndex);
        return ReplanAsync(
            "track_change",
            positionSeconds,
            _qualityPreferenceV3,
            selectedTracks => { },
            failure: null,
            ct);
    }

    public Task<PlaybackStartResponse> ReplanSubtitleAsync(
        int? subtitleTrackIndex,
        double positionSeconds,
        CancellationToken ct = default)
    {
        RecordLocalSubtitleSelection(subtitleTrackIndex);
        return ReplanAsync(
            "track_change",
            positionSeconds,
            _qualityPreferenceV3,
            selectedTracks => { },
            failure: null,
            ct);
    }

    public Task<PlaybackStartResponse> ReplanSeekAsync(
        double positionSeconds,
        CancellationToken ct = default)
        => ReplanAsync(
            "seek_reanchor",
            positionSeconds,
            _qualityPreferenceV3,
            selectedTracks => { },
            failure: null,
            ct);

    public Task<PlaybackStartResponse> RecoverPlaybackFailureAsync(
        double positionSeconds,
        string classification,
        string? message = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // A byte-stream interruption does not invalidate codec/container support.
        // Replanning marks original_http as failed and unnecessarily escalates to
        // remux. Reopen the same executable direct plan at the requested position.
        if (classification == "playback_interrupted" &&
            CurrentSession is { CanSeekAnywhere: true } session &&
            PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(
                PlaybackTransportPlanner.Plan(session).TransportKind, message ?? ""))
        {
            if (!ReferenceEquals(_directRecoverySession, session) ||
                positionSeconds >= _directRecoveryPosition + 30)
            {
                _directRecoverySession = session;
                _directRecoveryPosition = positionSeconds;
                _directRecoveryAttempts = 0;
            }
            if (_directRecoveryAttempts >= 3)
            {
                throw new PlaybackPlanTerminalException(
                    "direct_recovery_exhausted",
                    "Direct playback could not resume after three attempts. Retry to reopen it at your saved position.",
                    retryable: true);
            }
            _directRecoveryAttempts++;
            return Task.FromResult(session);
        }

        return ReplanAsync(
            "failure_recovery",
            positionSeconds,
            _qualityPreferenceV3,
            selectedTracks => { },
            new PlaybackFailureV3
            {
                Classification = string.IsNullOrWhiteSpace(classification) ? "unknown" : classification,
                Message = message,
            },
            ct);
    }

    /// <summary>
    /// Replans only when the server invalidated the plan that is still active.
    /// A late invalidation for an already-replaced plan is a successful no-op.
    /// </summary>
    public async Task<PlaybackStartResponse?> ReplanInvalidatedPlanAsync(
        string invalidatedPlanId,
        string reason,
        double positionSeconds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invalidatedPlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _replanGuard.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_currentPlanV3 == null ||
                !string.Equals(_currentPlanV3.PlanId, invalidatedPlanId, StringComparison.Ordinal))
            {
                return null;
            }

            var classification = reason.Trim();
            if (classification.Length > 64)
                classification = classification[..64];
            return await ReplanCoreAsync(
                "failure_recovery",
                positionSeconds,
                _qualityPreferenceV3,
                selectedTracks => { },
                new PlaybackFailureV3
                {
                    Classification = classification,
                    Message = "The server invalidated this playback plan.",
                },
                ct).ConfigureAwait(false);
        }
        finally
        {
            _replanGuard.Release();
        }
    }

    private PlaybackTrackIdentityV3 FindSubtitleIdentity(int combinedIndex)
    {
        var track = _currentPlanV3?.Subtitle.Inventory.FirstOrDefault(item => item.CombinedIndex == combinedIndex)
            ?? throw new InvalidOperationException("The selected subtitle is no longer available in the active playback plan.");
        return new PlaybackTrackIdentityV3 { Id = track.TrackId, Index = track.CombinedIndex };
    }

    private async Task<PlaybackStartResponse> ReplanAsync(
        string operation,
        double positionSeconds,
        string qualityPreference,
        Action<PlaybackSelectedTracksV3> mutateSelectedTracks,
        PlaybackFailureV3? failure,
        CancellationToken ct)
    {
        await _replanGuard.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Only track_change gives an omitted subtitle the meaning Off.
            EnsureBufferingReplanOwner();
            // Tell the server before quality_change, which preserves an omitted identity.
            if (operation == "quality_change" && NeedsExplicitSubtitleOff())
                await ReplanCoreAsync("track_change", positionSeconds, _qualityPreferenceV3,
                    selectedTracks => { }, null, ct).ConfigureAwait(false);
            return await ReplanCoreAsync(
                operation,
                positionSeconds,
                qualityPreference,
                mutateSelectedTracks,
                failure,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _replanGuard.Release();
        }
    }

    private async Task<PlaybackStartResponse> ReplanCoreAsync(
        string operation,
        double positionSeconds,
        string qualityPreference,
        Action<PlaybackSelectedTracksV3> mutateSelectedTracks,
        PlaybackFailureV3? failure,
        CancellationToken ct)
    {
        var plan = _currentPlanV3
            ?? throw new InvalidOperationException("The active playback session does not have a protocol-v3 plan.");
        var sessionId = _sessionId
            ?? throw new InvalidOperationException("The playback session is no longer active.");
        var capabilities = _clientCapabilitiesV3
            ?? throw new InvalidOperationException("Playback capabilities are unavailable for replanning.");
        var context = _clientContextV3
            ?? throw new InvalidOperationException("Playback output context is unavailable for replanning.");
        var playbackAttemptId = _playbackAttemptIdV3
            ?? throw new InvalidOperationException("The playback attempt identity is unavailable.");

        var selectedTracks = new PlaybackSelectedTracksV3
        {
            Audio = plan.SelectedTracks.Audio == null ? null : new PlaybackTrackIdentityV3
            {
                Id = plan.SelectedTracks.Audio.Id,
                Index = plan.SelectedTracks.Audio.Index,
            },
            Subtitle = plan.SelectedTracks.Subtitle == null ? null : new PlaybackTrackIdentityV3
            {
                Id = plan.SelectedTracks.Subtitle.Id,
                Index = plan.SelectedTracks.Subtitle.Index,
            },
        };
        var subtitleRevision = ApplyLocalSubtitleIntent(selectedTracks, operation);
        var audioRevision = ApplyLocalAudioIntent(selectedTracks, operation);
        mutateSelectedTracks(selectedTracks);

        var recoveryAttempt = failure == null
            ? null
            : _recoveryAttemptHistory.PrepareFailure(plan.PlanAttemptKey);
        var request = new PlaybackReplanRequestV3
        {
            Operation = operation,
            PlaybackAttemptId = playbackAttemptId,
            ReplanRequestId = Guid.NewGuid().ToString(),
            FailedPlanId = plan.PlanId,
            PlanAttemptId = _planAttemptIdV3 ?? Guid.NewGuid().ToString(),
            PlanAttemptKey = plan.PlanAttemptKey,
            AttemptedPlanKeys = recoveryAttempt?.AttemptedPlanKeys.ToList() ?? [],
            AttemptCount = recoveryAttempt?.AttemptCount ?? 1,
            QualityPreference = qualityPreference,
            BandwidthCapKbps = _bandwidthCapKbpsV3,
            PositionSeconds = Math.Clamp(positionSeconds, 0, 31_536_000),
            Metered = false,
            SelectedTracks = selectedTracks,
            Failure = failure,
            ClientCapabilities = capabilities,
            ClientPlaybackContext = context,
        };

        ConfigureConnectionReplan(request);
        var decision = await _playbackApi.ReplanPlaybackV3Async(sessionId, request, ct).ConfigureAwait(false);
        if (recoveryAttempt != null)
            _recoveryAttemptHistory.CommitFailure(recoveryAttempt);
        else
            _recoveryAttemptHistory.Reset();
        var response = AdoptProtocolV3Decision(decision, playbackAttemptId, request.QualityPreference, request.BandwidthCapKbps);
        _currentPlanV3 = decision.PlaybackPlan;
        _planAttemptIdV3 = Guid.NewGuid().ToString();
        _qualityPreferenceV3 = qualityPreference;
        _sessionId = response.SessionId;
        CurrentSession = response;
        AcknowledgeSubtitleIntent(subtitleRevision);
        AcknowledgeAudioIntent(audioRevision);
        _lastReportedPosition = Math.Max(0, response.Position);
        ApplyStreamUrl(response);
        _ = RecordRouteEventAsync(response, "plan_selected");
        LogToStateTrace(
            $"Replan v3: operation={operation}, requested_quality={qualityPreference}, active_quality={response.ActiveQuality}, plan={response.PlanId}, delivery={response.Delivery}, position={response.Position:F1}");
        return response;
    }

    private void ApplyStreamUrl(PlaybackStartResponse response)
    {
        StreamUrl = PlaybackDeliveryUrl.Resolve(_apiClient.BaseUrl, response.StreamUrl);
    }

    /// <summary>
    /// Starts a replacement session before retiring the active one. This mirrors
    /// the current WebUI handoff: the old stream remains usable until the server
    /// has accepted the new session, so version and quality switches do not sit
    /// behind synchronous history/scrobble cleanup on DELETE.
    /// </summary>
    public async Task<PlaybackStartResponse> StartReplacementSessionAsync(
        int fileId,
        double startPosition,
        bool forceStartPosition = true,
        int? audioTrackIndex = null,
        bool forceDirectAudioSelection = false,
        double? previousFinalPosition = null,
        string? qualityPreference = null,
        CancellationToken ct = default)
    {
        var previousSessionId = _sessionId;
        var subtitleIndex = ReplacementSubtitleIndex(fileId);
        audioTrackIndex ??= ReplacementAudioIndex(fileId);
        var response = await StartSessionAsync(
            fileId,
            startPosition,
            forceStartPosition,
            audioTrackIndex,
            forceDirectAudioSelection,
            qualityPreference: qualityPreference,
            ct: ct,
            subtitleTrackIndex: subtitleIndex,
            preserveTrackIntent: true).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(previousSessionId) &&
            !string.Equals(previousSessionId, response.SessionId, StringComparison.Ordinal))
        {
            _ = RetireSupersededSessionAsync(
                previousSessionId,
                Math.Max(0, previousFinalPosition ?? startPosition));
        }

        return response;
    }

    private async Task RetireSupersededSessionAsync(string sessionId, double finalPosition)
    {
        if (double.IsFinite(finalPosition))
        {
            try
            {
                using var progressCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _playbackApi.ReportProgressAsync(
                    sessionId,
                    finalPosition,
                    false,
                    progressCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine(
                    "progress_error.txt",
                    $"Superseded session final progress failed: {ex.Message}");
            }
        }

        try
        {
            // Server stop finalization includes database history, session sync,
            // and provider scrobbling. It can legitimately outlive the visual
            // handoff, so keep it off the playback-start critical path.
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _playbackApi.StopPlaybackAsync(sessionId, stopCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LocalLog.AppendLine(
                "progress_error.txt",
                $"Superseded playback session cleanup failed: {ex.Message}");
        }
    }

    public void UpdatePosition(double position, bool isPaused)
    {
        _lastReportedPosition = position;
        _isPaused = isPaused;
    }

    public void ApplyAudioChange(ChangeAudioResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (CurrentSession == null) return;

        CurrentSession.AudioTrackIndex = response.AudioTrackIndex;
        if (!string.IsNullOrWhiteSpace(response.PlayMethod))
            CurrentSession.PlayMethod = response.PlayMethod;
        if (!string.IsNullOrWhiteSpace(response.StreamUrl))
        {
            CurrentSession.StreamUrl = response.StreamUrl;
            StreamUrl = PlaybackDeliveryUrl.Resolve(_apiClient.BaseUrl, response.StreamUrl);
        }
        if (response.PlaybackInfo != null)
            CurrentSession.PlaybackInfo = response.PlaybackInfo;
    }

    public async Task ReportProgressNowAsync(
        double position,
        bool isPaused,
        CancellationToken ct = default)
    {
        UpdatePosition(position, isPaused);
        await _progressGuard.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sessionId = _sessionId;
            if (sessionId == null)
                return;
            await _playbackApi.ReportProgressAsync(
                sessionId,
                _lastReportedPosition,
                _isPaused,
                ct).ConfigureAwait(false);
            _consecutiveProgressFailures = 0;
        }
        finally
        {
            _progressGuard.Release();
        }
    }

    public async Task StopSessionAsync(
        double? finalPosition = null,
        bool? isPaused = null,
        CancellationToken ct = default)
    {
        StopProgressReporting();
        var sessionId = _sessionId;
        if (sessionId == null)
            return;

        var position = Math.Max(0, finalPosition ?? _lastReportedPosition);
        var paused = isPaused ?? _isPaused;
        var guardEntered = false;
        // Keep the caller's lifetime cancellation separate from the bounded
        // network operations below. The old five-second shared deadline let a
        // slow final progress POST consume three seconds and then canceled the
        // DELETE less than two seconds later. Silo's stop endpoint also flushes
        // history/session state and can legitimately take longer than that.
        // PlayerService detaches this cleanup from the visual close path, so an
        // independent stop budget improves reliable server cleanup without
        // making the player window wait.
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            await _progressGuard.WaitAsync(operationCts.Token).ConfigureAwait(false);
            guardEntered = true;

            // A newer session may have replaced the captured one while an
            // older progress request was draining. Never tear that session down.
            if (!string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
                return;

            if (double.IsFinite(position))
            {
                try
                {
                    using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(operationCts.Token);
                    progressCts.CancelAfter(TimeSpan.FromSeconds(3));
                    await _playbackApi.ReportProgressAsync(
                        sessionId,
                        position,
                        paused,
                        progressCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    LocalLog.AppendLine("progress_error.txt", "Final progress report timed out before session stop.");
                }
                catch (Exception ex)
                {
                    LocalLog.AppendLine("progress_error.txt", $"Final progress report failed before session stop: {ex.Message}");
                }
            }

            try
            {
                using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(operationCts.Token);
                stopCts.CancelAfter(TimeSpan.FromSeconds(15));
                await _playbackApi.StopPlaybackAsync(sessionId, stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                LocalLog.AppendLine("progress_error.txt", "Playback stop timed out.");
            }
            catch (Exception ex)
            {
                LocalLog.AppendLine("progress_error.txt", $"Playback stop failed: {ex.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            LocalLog.AppendLine(
                "progress_error.txt",
                "Timed out waiting for the active progress request during playback stop.");
        }
        finally
        {
            if (guardEntered)
                _progressGuard.Release();

            if (string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                _sessionId = null;
                CurrentSession = null;
                StreamUrl = null;
            }
        }

        // API v2 stop already persists and synchronizes the final sample.
    }

    public List<(SubtitleTrackInfo Track, string FullUrl)> GetSubtitleUrls()
    {
        if (CurrentSession == null) return [];
        var baseUrl = _apiClient.BaseUrl;
        var token = _apiClient.AccessToken;
        return CurrentSession.SubtitleUrls
            .Where(s => !string.IsNullOrWhiteSpace(s.Url))
            .Select(s =>
        {
            var url = PlaybackDeliveryUrl.Resolve(baseUrl, s.Url);
            // Integrated sidecars require account/viewer authentication even
            // with a reconstruction reference (st). Worker/CDN URLs carry their
            // own authority and must never receive the account credential.
            if (RequiresSubtitleAccountAuthentication(baseUrl, s.Url))
                url = UrlHelper.AppendToken(url, token);
            return (s, url);
        }).ToList();
    }

    private static bool RequiresSubtitleAccountAuthentication(string baseUrl, string resource)
    {
        if (!Uri.TryCreate(resource, UriKind.Absolute, out var absolute) ||
            (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
            return true;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) ||
            absolute.Scheme != origin.Scheme || absolute.IdnHost != origin.IdnHost || absolute.Port != origin.Port ||
            !string.IsNullOrEmpty(absolute.UserInfo))
            return false;
        var prefix = origin.AbsolutePath.TrimEnd('/') + "/api/v2/stream/";
        if (!absolute.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var route = absolute.AbsolutePath[prefix.Length..].Split('/');
        return route.Length is 3 or 4 && route[0].Length > 0 && route[1] == "subtitles" && route[2].Length > 0 &&
            (route.Length == 3 || route[3] == "fonts");
    }

    private void StartProgressReporting()
    {
        StopProgressReporting();
        _progressStopCts = new CancellationTokenSource();
        var stopToken = _progressStopCts.Token;
        _consecutiveProgressFailures = 0;
        _progressTimer = new Timer(async _ =>
        {
            var sessionId = _sessionId; // Capture to avoid race with StopSessionAsync
            if (sessionId == null) return;
            if (stopToken.IsCancellationRequested) return;

            if (!_progressGuard.Wait(0)) return; // skip if previous report still in-flight
            try
            {
                // Per-call 10s timeout. Without this the default HttpClient
                // timeout is 100s — long enough for the server to reap the
                // session (45s) and the stream to start 404'ing while we're
                // still hung on one progress POST. Subsequent Wait(0) checks
                // would then silently skip, so we'd never notice.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, stopToken);
                await _playbackApi.ReportProgressAsync(sessionId, _lastReportedPosition, _isPaused, linkedCts.Token);
                if (!stopToken.IsCancellationRequested)
                    _consecutiveProgressFailures = 0;
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                // Playback is stopping; the in-flight keepalive was intentionally canceled.
            }
            catch (Exception ex)
            {
                _consecutiveProgressFailures++;

                // Log progress failures so we can diagnose session reaping.
                LocalLog.AppendLine(
                    "progress_error.txt",
                    $"session={_sessionId} pos={_lastReportedPosition:F1} paused={_isPaused} consec={_consecutiveProgressFailures} err={ex.Message}");

                if (_consecutiveProgressFailures >= 3)
                {
                    var msg = ex is OperationCanceledException
                        ? "Playback session timed out — the server stopped responding."
                        : $"Playback session lost contact with the server: {ex.Message}";
                    // Stop the timer first so we don't keep firing after the
                    // subscriber tears the session down.
                    StopProgressReporting();
                    try { ProgressReportingFailed?.Invoke(msg); } catch { }
                }
            }
            finally
            {
                _progressGuard.Release();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(7));
    }

    private void StopProgressReporting()
    {
        var stopCts = Interlocked.Exchange(ref _progressStopCts, null);
        try { stopCts?.Cancel(); } catch (ObjectDisposedException) { }
        stopCts?.Dispose();

        Interlocked.Exchange(ref _progressTimer, null)?.Dispose();
    }

    private async Task RecordRouteEventAsync(PlaybackStartResponse session, string eventName)
        => await ReportRouteEventAsync(session, eventName);

    public async Task ReportRouteEventAsync(PlaybackStartResponse session, string eventName, IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _playbackApi.ReportRouteEventAsync(session, eventName, timeout.Token, diagnostics).ConfigureAwait(false);
        }
        catch { /* Server route telemetry is best effort; local diagnostics remain authoritative. */ }
    }
    private static void LogToStateTrace(string msg)
    {
        LocalLog.AppendLine("state_trace.txt", $"PlaybackManager: {msg}");
    }

    public void Dispose()
    {
        StopProgressReporting();
        var sessionId = Interlocked.Exchange(ref _sessionId, null);
        CurrentSession = null;
        StreamUrl = null;
        if (sessionId != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _playbackApi.StopPlaybackAsync(sessionId, cts.Token).ConfigureAwait(false);
                }
                catch { }
            });
        }
    }
}
