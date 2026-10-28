using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using Windows.Networking.Connectivity;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly object _bufferingGate = new();
    private BufferingQualityController? _bufferingController;
    private readonly NativeMediaThroughputMeter _nativeMediaMeter = new();
    private PlaybackManager? _bufferingManager;
    private PlaybackStartResponse? _bufferingSession;
    private long _bufferingLastDataAt;
    private int _bufferingActionRunning;
    private double? _bufferingLastPosition;
    private long _bufferingLastPositionAt;
    private bool _bufferingObscured;
    // Samples are taken outside _bufferingGate, so they need their own
    // ordering: one sample at a time, discarded if a plan was adopted or
    // ended while libmpv was being read.
    private int _bufferingSampling;
    private long _bufferingPlanGeneration;
    private string? _publishedBufferingPill;
    private sealed record BufferingActionOwner(PlaybackManager Manager, PlaybackStartResponse Session,
        PlaybackAuthorityScope Authority, BufferingQualityController Controller);
    public event Action? BufferingQualityChanged;
    public string? BufferingQualityPill { get { lock (_bufferingGate) return _bufferingObscured || IsWatchTogetherPlayback ? null : _bufferingController?.Pill; } }
    public string BufferingQualityPlaybackInfo => _playbackManager?.ConnectionPlan?.BandwidthLimit is { } limit && limit.ExpiresAt > DateTimeOffset.UtcNow
        ? $"{limit.Kbps / 1000d:0.#} Mbps after buffering · expires {limit.ExpiresAt.ToLocalTime():g}" : "";
    public void RefreshConnectionObservation()
    {
        _ = RefreshConnectionObservationCoreAsync();
    }
    private async Task RefreshConnectionObservationCoreAsync()
    {
        try
        {
            if (!_localAuthority.Auth.IsLoggedIn || _localAuthority.Auth.SelectedProfileId == null) return;
            var network = PlaybackConnectionNetwork.Capture();
            await PlaybackConnectionService.For(_localAuthority.PlaybackApi).RefreshAsync(network.Key, network.Metered, network.Transport, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Connection observation unavailable: {ex.GetType().Name}"); }
    }
    private void InitializeConnectionObservation()
    {
        NetworkInformation.NetworkStatusChanged += OnPlaybackNetworkChanged;
        _localAuthority.Auth.LoggedOut += RetireConnectionObservation;
    }
    private void OnPlaybackNetworkChanged(object sender) => RefreshConnectionObservation();
    private void RetireConnectionObservation() => PlaybackConnectionService.For(_localAuthority.PlaybackApi).Retire();
    private void SetConnectionMediaActive(bool active)
    {
        PlaybackConnectionService.For(_localAuthority.PlaybackApi).SetMediaActive(active);
        if (!ReferenceEquals(_localAuthority.PlaybackApi, _activeAuthority.PlaybackApi))
            PlaybackConnectionService.For(_activeAuthority.PlaybackApi).SetMediaActive(active);
    }
    private void SynchronizeConnectionMediaActivity(bool? paused = null, bool? buffering = null)
    {
        // A user pause frees the link for Settings' test. A source load or
        // paused-for-cache stall still owns it, and resuming cancels a test
        // before playback competes with the transfer.
        var active = !_closing && _playbackTerminalState == null &&
            (IsLoading || _switchingContent || State != PlayerState.Idle &&
                ((buffering ?? _mpv?.IsBufferingForCache) == true || !(paused ?? IsPaused)));
        SetConnectionMediaActive(active);
    }
    private void AdoptBufferingQualityPlan()
    {
        lock (_bufferingGate)
        {
            _bufferingPlanGeneration++;
            FlushBufferingSummaryCore();
            var manager = _playbackManager;
            if (manager?.CurrentSession is not { } session || !manager.SupportsBufferingQuality) { _bufferingController = null; PublishBufferingPillCore(); return; }
            if (!ReferenceEquals(_bufferingManager, manager) || _bufferingSession?.PlaybackAttemptId != session.PlaybackAttemptId)
                _bufferingController = new(manager.BufferingQualityMode);
            _bufferingController ??= new(manager.BufferingQualityMode);
            _bufferingManager = manager; _bufferingSession = session;
            var plan = manager.ConnectionPlan;
            var delivered = plan?.EffectiveRecipe.BitrateKbps is > 0 && session.Delivery == "server_transcode_hls"
                ? plan.EffectiveRecipe.BitrateKbps.Value : plan?.Source.BitrateKbps ?? ActiveVersion?.Bitrate ?? 0;
            _bufferingController.Mode = manager.BufferingQualityMode;
            _bufferingController.Adopt(new(session.PlanId ?? "", Guid.NewGuid().ToString(), manager.QualityPreference,
                session.ActiveQuality, delivered, session.AvailableQualities,
                plan?.DegradationWarnings.Any(w => w.Code == "quality_reduction_unavailable") == true), Environment.TickCount64);
            _nativeMediaMeter.Reset(); _bufferingLastDataAt = Environment.TickCount64; _bufferingLastPosition = null;
            PublishBufferingPillCore();
        }
    }
    private void ObserveBufferingQuality()
    {
        // A libmpv read can outlast the 1s watchdog period; a later tick must
        // not overtake this sample and feed the controller older state.
        if (Interlocked.Exchange(ref _bufferingSampling, 1) != 0) return;
        BufferingQualityAction? action = null;
        BufferingActionOwner? actionOwner = null;
        try
        {
            var mpv = _mpv;
            if (Volatile.Read(ref _bufferingController) == null || mpv == null || _closing || State == PlayerState.Idle) return;
            var plan = Interlocked.Read(ref _bufferingPlanGeneration);
            // libmpv reads can wait for a busy player core. Take them before the
            // gate that UI-thread pill reads and key handling also take.
            var now = Environment.TickCount64;
            var sample = mpv.ReadMediaNetworkObservation();
            var paused = _restorePausedAfterLoad ?? CaptureUserPausedState();
            var position = CurrentMediaPosition;
            var cacheWait = mpv.IsBufferingForCache;
            lock (_bufferingGate)
            {
                if (plan != _bufferingPlanGeneration || _bufferingController == null || !ReferenceEquals(_mpv, mpv) || _closing || State == PlayerState.Idle || !ReferenceEquals(_bufferingManager, _playbackManager)) return;
                var jump = _bufferingLastPosition is double previous && Math.Abs(position - previous) > Math.Max(4, (now - _bufferingLastPositionAt) / 1000d * 3);
                _bufferingLastPosition = position; _bufferingLastPositionAt = now;
                var seeking = sample.Seeking || jump || _qualitySwitchActive || (_switchingContent && _transportHasFirstOutput) || !double.IsFinite(sample.BufferedSeconds);
                var rate = _nativeMediaMeter.Observe(now, sample.Bytes, paused, seeking);
                // Native HLS/remux has no monotonic HTTP byte counter. Use mpv's media-network layer estimate only while the reader is active.
                if (sample.Bytes == null && !paused && !seeking && sample.InputBytesPerSecond is > 0)
                {
                    var kbps = sample.InputBytesPerSecond.Value * 8 / 1000;
                    rate = kbps >= 100 ? (int)Math.Min(kbps, 1000000) : null;
                }
                if (rate != null) _bufferingLastDataAt = now;
                var wait = cacheWait || (!_transportHasFirstOutput && IsLoading);
                var silence = sample.SinceLastByteMs ?? now - _bufferingLastDataAt;
                var segmented = _bufferingSession?.Delivery is "server_transcode_hls" or "server_remux_hls";
                var outage = NativeBufferingSignalPolicy.IsOutage(wait, sample.TransportFailing, silence, segmented);
                action = _bufferingController.Observe(new(now, wait, paused, seeking, outage, IsWatchTogetherPlayback,
                    sample.BufferedSeconds, rate, _bufferingObscured));
                actionOwner = CaptureBufferingActionOwnerCore();
                if (_bufferingManager != null) _bufferingManager.KeptBufferingEstimateKbps = _bufferingController.KeptEstimateKbps;
                PublishBufferingPillCore();
            }
        }
        finally
        {
            Volatile.Write(ref _bufferingSampling, 0);
        }
        if (action != null && actionOwner != null) DispatchBufferingAction(action, actionOwner);
    }
    public void SetBufferingPillObscured(bool obscured)
    {
        lock (_bufferingGate) { _bufferingObscured = obscured; PublishBufferingPillCore(); }
    }
    public void KeepBufferingQuality()
    {
        lock (_bufferingGate) { _bufferingController?.Keep(); PublishBufferingPillCore(); }
    }
    public Task LowerBufferingQualityAsync() => BufferingPillActionAsync(false);
    public Task RestoreBufferingQualityAsync() => BufferingPillActionAsync(true);
    private bool TryBufferingStartupRecovery()
    {
        BufferingQualityAction? action;
        BufferingActionOwner? owner;
        lock (_bufferingGate)
        {
            if (IsWatchTogetherPlayback) return false;
            action = _bufferingController?.StartDeadlinePassed(Environment.TickCount64);
            owner = CaptureBufferingActionOwnerCore();
        }
        if (action == null || owner == null) return false;
        DispatchBufferingAction(action, owner); return true;
    }
    private Task BufferingPillActionAsync(bool restore)
    {
        BufferingQualityAction? action;
        BufferingActionOwner? owner;
        lock (_bufferingGate)
        {
            if (IsWatchTogetherPlayback) return Task.CompletedTask;
            action = restore ? _bufferingController?.Restore(Environment.TickCount64) : _bufferingController?.Accept(Environment.TickCount64);
            owner = CaptureBufferingActionOwnerCore();
            PublishBufferingPillCore();
        }
        return action?.Replan is { } replan && owner != null ? ApplyBufferingQualityAsync(replan, owner) : Task.CompletedTask;
    }
    private BufferingActionOwner? CaptureBufferingActionOwnerCore() => _bufferingManager != null && _bufferingSession != null && _bufferingController != null
        ? new(_bufferingManager, _bufferingSession, _activeAuthority, _bufferingController) : null;
    private bool IsBufferingActionCurrent(BufferingActionOwner owner) => !_closing && !IsWatchTogetherPlayback &&
        ReferenceEquals(owner.Manager, _playbackManager) && ReferenceEquals(owner.Session, owner.Manager.CurrentSession) &&
        ReferenceEquals(owner.Authority, _activeAuthority) && ReferenceEquals(owner.Controller, _bufferingController);
    private void DispatchBufferingAction(BufferingQualityAction action, BufferingActionOwner owner)
    {
        var dispatcher = App.MainWindowInstance?.DispatcherQueue;
        dispatcher?.TryEnqueue(() =>
        {
            if (!IsBufferingActionCurrent(owner)) return;
            if (action.Replan is { } replan) _ = ApplyBufferingQualityAsync(replan, owner);
            else if (action.Kind == "cant_lower") ShowNotice("Playback keeps buffering", "Can't lower quality for this video", "info");
        });
    }
    private async Task ApplyBufferingQualityAsync(PlaybackBufferingReplan replan, BufferingActionOwner actionOwner)
    {
        if (Interlocked.Exchange(ref _bufferingActionRunning, 1) != 0) return;
        var manager = _playbackManager; var owner = _activeAuthority;
        try
        {
            if (!IsBufferingActionCurrent(actionOwner) || manager == null) return;
            manager.KeptBufferingEstimateKbps = replan.LearnedLimit == "clear" ? null : replan.BandwidthEstimateKbps;
            await SwitchQualityTierAsync(replan.QualityPreference, replan, actionOwner.Session);
            if (!ReferenceEquals(owner, _activeAuthority) || !ReferenceEquals(manager, _playbackManager)) return;
            if (!ReferenceEquals(_bufferingSession, manager.CurrentSession))
                lock (_bufferingGate) _bufferingController?.ReplanFailed(unsent: false);
        }
        finally { Interlocked.Exchange(ref _bufferingActionRunning, 0); }
    }
    private void PublishBufferingPillCore()
    {
        var pill = _bufferingObscured || IsWatchTogetherPlayback ? null : _bufferingController?.Pill;
        if (_publishedBufferingPill == pill) return;
        _publishedBufferingPill = pill;
        _mpv?.SendScriptMessage("osc-buffering-quality", pill ?? "");
        App.MainWindowInstance?.DispatcherQueue?.TryEnqueue(() => BufferingQualityChanged?.Invoke());
    }
    private void FlushBufferingSummaryCore()
    {
        if (_bufferingController?.Plan is not { } plan || _bufferingManager == null || _bufferingSession == null) return;
        var summary = _bufferingController.Stop(Environment.TickCount64);
        if (IsWatchTogetherPlayback) summary.BufferingAction = "none";
        _ = _bufferingManager.ReportRouteEventAsync(_bufferingSession, "plan_summary", summary.Diagnostics(plan.DeliveredKbps));
    }
    private void EndBufferingQuality()
    {
        lock (_bufferingGate) { _bufferingPlanGeneration++; FlushBufferingSummaryCore(); _bufferingController = null; _bufferingManager = null; _bufferingSession = null; PublishBufferingPillCore(); }
        SetConnectionMediaActive(false);
    }
}
