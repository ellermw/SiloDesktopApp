using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Playback;

public sealed class PlaybackCapabilityV3
{
    public bool Enabled { get; set; }
    public List<int> ProtocolVersions { get; set; } = [];
    public List<string> Features { get; set; } = [];
    public List<string> Deliveries { get; set; } = [];
}

/// <summary>
/// Silo's finalized playback protocol v3 request. This is intentionally a
/// separate wire model from the removed legacy request: the v3 schema rejects
/// legacy route-selection fields as additional properties.
/// </summary>
public sealed class PlaybackStartRequestV3
{
    public int ProtocolVersion { get; set; } = 3;
    public List<string> ClientFeatures { get; set; } = ["playback_plan_v3"];
    public int FileId { get; set; }
    public string ProfileId { get; set; } = "";
    public string PlaybackAttemptId { get; set; } = Guid.NewGuid().ToString();
    public string QualityPreference { get; set; } = "original";
    public string SubtitleFidelityPreference { get; set; } = "preserve";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? StartPosition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProgressPersistence { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AudioTrackId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? AudioTrackIndex { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SubtitleTrackId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? SubtitleTrackIndex { get; set; }
    public bool Metered { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BandwidthEstimateKbps { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BandwidthCapKbps { get; set; }
    public PlaybackClientCapabilitiesV3 ClientCapabilities { get; set; } = new();
    public PlaybackClientContextV3 ClientPlaybackContext { get; set; } = new();
}

public sealed class PlaybackClientCapabilitiesV3
{
    public string VideoEvidence { get; set; } = "declared";
    public string AudioEvidence { get; set; } = "declared";
    public List<string> CodecsVideo { get; set; } = [];
    public List<string> CodecsVideoHardware { get; set; } = [];
    public List<string> CodecsAudio { get; set; } = [];
    public List<string> Containers { get; set; } = [];
    public string MaxResolution { get; set; } = "2160p";
    public bool Hdr { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HdrCapabilityDetails? HdrDetails { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AudioPassthroughCapabilitiesV3? AudioPassthrough { get; set; }
}

public sealed class AudioPassthroughCapabilitiesV3
{
    public List<string> PassthroughCodecs { get; set; } = [];
    public int MaxChannels { get; set; }
    public bool SpatializerEnabled { get; set; }
    public List<AudioPassthroughEntryV3> Entries { get; set; } = [];
}

public sealed class AudioPassthroughEntryV3
{
    public string Codec { get; set; } = "";
    public List<int> ChannelCounts { get; set; } = [];
    public List<string> Layouts { get; set; } = [];
}

public sealed class PlaybackClientContextV3
{
    public int ProtocolVersion { get; set; } = 3;
    public string FormFactor { get; set; } = "desktop";
    public string AppVersion { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AppBuild { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AppChannel { get; set; }
    public PlaybackDeviceContextV3 Device { get; set; } = new();
    public PlaybackOutputContextV3 Output { get; set; } = new();
    public Dictionary<string, PlaybackDeliveryCapabilityV3> Deliveries { get; set; } = [];
}

public sealed class PlaybackDeviceContextV3
{
    public string Platform { get; set; } = "windows";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OsVersion { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Manufacturer { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Model { get; set; }
    public Dictionary<string, string> PlatformDetails { get; set; } = [];
}

public sealed class PlaybackOutputContextV3
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OutputContextId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HdrCapabilityDetails? HdrDetails { get; set; }
}

public sealed class PlaybackDeliveryCapabilityV3
{
    public bool Enabled { get; set; } = true;
    public bool SupportedOnDevice { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureReason { get; set; }
    public List<string> Containers { get; set; } = [];
    public List<string> VideoCodecs { get; set; } = [];
    public List<string> AudioDecodeCodecs { get; set; } = [];
    public List<string> AudioPassthroughCodecs { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MaxChannels { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HdrCapabilityDetails? HdrDetails { get; set; }
    public PlaybackSubtitleCapabilitiesV3 Subtitles { get; set; } = new();
    public List<string> Features { get; set; } = [];
    public bool AuthHeaderRefresh { get; set; } = true;
    public List<string> ValidatedClaims { get; set; } = [];
    public List<object> Transformations { get; set; } = [];
}

public sealed class PlaybackSubtitleCapabilitiesV3
{
    public bool EmbeddedText { get; set; } = true;
    public bool SidecarText { get; set; } = true;
    public bool AssStyling { get; set; } = true;
    public bool EmbeddedBitmap { get; set; } = true;
    public bool SidecarBitmap { get; set; } = true;
    public bool FontAttachments { get; set; }
}

public sealed class PlaybackDecisionResponseV3
{
    public int ProtocolVersion { get; set; }
    public List<string> ServerFeatures { get; set; } = [];
    public string Outcome { get; set; } = "";
    public string? SessionId { get; set; }
    public PlaybackPlanV3? PlaybackPlan { get; set; }
    public PlaybackTerminalV3? Terminal { get; set; }
}

public sealed class PlaybackTerminalV3
{
    public string Reason { get; set; } = "adaptation_unavailable";
    public string? Message { get; set; }
    public bool Retryable { get; set; }
}

public sealed class PlaybackPlanV3
{
    public int ProtocolVersion { get; set; }
    public string PlanId { get; set; } = "";
    public string PlanAttemptKey { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string Delivery { get; set; } = "";
    public PlaybackStreamV3 Stream { get; set; } = new();
    public PlaybackTimelineV3 Timeline { get; set; } = new();
    public PlaybackSelectedTracksV3 SelectedTracks { get; set; } = new();
    public PlaybackRecipeV3 EffectiveRecipe { get; set; } = new();
    public PlaybackSubtitlePlanV3 Subtitle { get; set; } = new();
    public List<PlaybackQualityV3> AvailableQualities { get; set; } = [];
    public List<PlaybackDegradationWarningV3> DegradationWarnings { get; set; } = [];
    public string? DecisionReason { get; set; }
    public int RequestedMediaFileId { get; set; }
    public int EffectiveMediaFileId { get; set; }
    public PlaybackSourceV3 Source { get; set; } = new();
}

public sealed class PlaybackStreamV3
{
    public string Url { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string? Container { get; set; }
    public string? MimeType { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? HeaderRefresh { get; set; }
    public string? HeaderRefreshUrl { get; set; }
}

public sealed class PlaybackTimelineV3
{
    public double SourceStartSeconds { get; set; }
    public double StreamOriginSeconds { get; set; }
    public double PlayerStartSeconds { get; set; }
    public double TimelineOffsetSeconds { get; set; }
    public double? SeekWindowStartSeconds { get; set; }
    public double? SeekWindowEndSeconds { get; set; }
    public bool CanSeekAnywhere { get; set; }
    public string? SeekRestoration { get; set; }
}

public sealed class PlaybackSelectedTracksV3
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlaybackTrackIdentityV3? Audio { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlaybackTrackIdentityV3? Subtitle { get; set; }
}

public sealed class PlaybackTrackIdentityV3
{
    public string Id { get; set; } = "";
    public int Index { get; set; }
}

public sealed class PlaybackRecipeV3
{
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? BitrateKbps { get; set; }
    public string? DynamicRange { get; set; }
    public int? AudioChannels { get; set; }
    public string? AudioLayout { get; set; }
}

public sealed class PlaybackSubtitlePlanV3
{
    public string Mode { get; set; } = "off";
    public string? TrackId { get; set; }
    public PlaybackSubtitleArtifactV3? Artifact { get; set; }
    public List<PlaybackSubtitleInventoryV3> Inventory { get; set; } = [];
}

public sealed class PlaybackSubtitleArtifactV3
{
    public string Url { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Format { get; set; } = "";
    public double TimingOriginSeconds { get; set; }
}

public sealed class PlaybackSubtitleInventoryV3
{
    public string TrackId { get; set; } = "";
    public int CombinedIndex { get; set; }
    public string Source { get; set; } = "";
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Label { get; set; }
    public bool Forced { get; set; }
    [JsonPropertyName("default")] public bool IsDefault { get; set; }
    public bool HearingImpaired { get; set; }
    public string Delivery { get; set; } = "";
    public string? Url { get; set; }
    public string? FontBundleUrl { get; set; }
}

public sealed class PlaybackQualityV3
{
    public string Label { get; set; } = "";
    public int? Height { get; set; }
    public int? BitrateKbps { get; set; }
    public bool PreservesSource { get; set; }
}

public sealed class PlaybackDegradationWarningV3
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class PlaybackReplanRequestV3
{
    public int ProtocolVersion { get; set; } = 3;
    public List<string> ClientFeatures { get; set; } = ["playback_plan_v3"];
    public string Operation { get; set; } = "";
    public string PlaybackAttemptId { get; set; } = "";
    public string ReplanRequestId { get; set; } = Guid.NewGuid().ToString();
    public string FailedPlanId { get; set; } = "";
    public string PlanAttemptId { get; set; } = "";
    public string PlanAttemptKey { get; set; } = "";
    public List<string> AttemptedPlanKeys { get; set; } = [];
    public int AttemptCount { get; set; } = 1;
    public string QualityPreference { get; set; } = "original";
    public double PositionSeconds { get; set; }
    public bool Metered { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BandwidthEstimateKbps { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? BandwidthCapKbps { get; set; }
    public PlaybackSelectedTracksV3 SelectedTracks { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PlaybackFailureV3? Failure { get; set; }
    public PlaybackClientCapabilitiesV3 ClientCapabilities { get; set; } = new();
    public PlaybackClientContextV3 ClientPlaybackContext { get; set; } = new();
}

public sealed class PlaybackFailureV3
{
    public string Classification { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DecoderName { get; set; }
}

public sealed class PlaybackSourceV3
{
    public int MediaFileId { get; set; }
    public double? DurationSeconds { get; set; }
    public string? Container { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
}
