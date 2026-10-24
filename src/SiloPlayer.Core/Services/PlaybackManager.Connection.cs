using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public partial class PlaybackManager
{
    private readonly AsyncLocal<PlaybackBufferingReplan?> _bufferingReplanIntent = new();
    private readonly AsyncLocal<PlaybackStartResponse?> _bufferingReplanSession = new();
    private IReadOnlyList<string> _connectionFeatures = [];
    public string BufferingQualityMode { get; private set; } = "never";
    public bool SourceVersionPinned { get; set; } = true;
    public bool IgnoreConnectionMeasurements { get; set; }
    public int? KeptBufferingEstimateKbps { get; set; }
    public PlaybackPlanV3? ConnectionPlan => _currentPlanV3;
    public bool SupportsBufferingQuality => _connectionFeatures.Contains("buffering_quality_v1") && !IgnoreConnectionMeasurements;
    private async Task ConfigureConnectionStartAsync(PlaybackStartRequestV3 request, PlaybackQualityPreferences preferences, CancellationToken ct)
    {
        var owner = _apiClient.CaptureIdentityContext();
        var capability = preferences.CapabilitySnapshot is { } snapshot && snapshot.Owner == owner
            ? snapshot.Capability : await _playbackApi.GetConnectionCapabilityAsync(owner, ct).ConfigureAwait(false);
        request.CapabilitySnapshot = (owner, capability);
        _connectionFeatures = capability.Features.ToArray();
        request.ClientFeatures.AddRange(new[] { "buffering_quality_v1", "connection_speed_v1", "playback_plan_summary_v1" }.Where(capability.Features.Contains));
        BufferingQualityMode = preferences.BufferingQualityMode;
        if (capability.Features.Contains("buffering_quality_v1"))
        {
            if (BufferingQualityMode == "never" || IgnoreConnectionMeasurements) request.LearnedLimit = "ignore";
        }
        // Only Auto source selection may search another same-edition version. Explicit source choices and receiver starts stay pinned.
        request.AllowAlternateVersions = !SourceVersionPinned && request.QualityPreference == "auto" && SupportsBufferingQuality;
        KeptBufferingEstimateKbps = null;
        if (!_apiClient.IsCurrentIdentityContext(owner)) throw new OperationCanceledException("Playback owner changed.", ct);
    }
    private void ConfigureConnectionReplan(PlaybackReplanRequestV3 request)
    {
        request.ClientFeatures.AddRange(new[] { "buffering_quality_v1", "connection_speed_v1", "playback_plan_summary_v1" }.Where(_connectionFeatures.Contains));
        if (!_connectionFeatures.Contains("buffering_quality_v1")) return;
        var intent = request.Operation == "quality_change" ? _bufferingReplanIntent.Value : null;
        request.LearnedLimit = BufferingQualityMode == "never" || IgnoreConnectionMeasurements ? "ignore" : intent?.LearnedLimit;
        if (IgnoreConnectionMeasurements || request.Operation == "seek_reanchor") return;
        request.BandwidthEstimateKbps = intent?.BandwidthEstimateKbps ?? (request.QualityPreference == "auto" ? KeptBufferingEstimateKbps : null);
        request.BandwidthEstimateSource = intent?.BandwidthEstimateSource;
        request.ObservedThroughputKbps = intent?.ObservedThroughputKbps;
    }
    private void EnsureBufferingReplanOwner()
    {
        if (_bufferingReplanSession.Value is { } expected && !ReferenceEquals(CurrentSession, expected))
            throw new OperationCanceledException("The playback plan changed before buffering recovery.");
    }
    public async Task<PlaybackStartResponse> ReplanBufferingQualityAsync(PlaybackBufferingReplan intent, double position, CancellationToken ct,
        PlaybackStartResponse? expectedSession = null)
    {
        if (!SupportsBufferingQuality) throw new NotSupportedException("Buffering quality is unavailable for this playback owner.");
        _bufferingReplanIntent.Value = intent;
        _bufferingReplanSession.Value = expectedSession;
        try { return await ReplanQualityAsync(intent.QualityPreference, position, ct).ConfigureAwait(false); }
        finally { _bufferingReplanIntent.Value = null; _bufferingReplanSession.Value = null; }
    }
}
