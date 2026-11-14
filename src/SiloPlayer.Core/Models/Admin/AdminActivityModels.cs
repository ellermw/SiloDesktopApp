using System.Text.Json;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// One live playback observation from <c>GET /api/v2/admin/sessions</c>
/// (contract schema <c>AdminActivitySession</c>). Identifiers stay opaque
/// strings exactly as the v2 contract sends them.
/// </summary>
public sealed class AdminActivitySession
{
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string? ProfileName { get; set; }
    public string MediaFileId { get; set; } = "";
    public string RequestedMediaFileId { get; set; } = "";
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? ContentId { get; set; }
    public string? SeriesName { get; set; }
    public string? EpisodeName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? PosterUrl { get; set; }
    public string PlayMethod { get; set; } = "";
    public string? EffectivePlayMethod { get; set; }
    public string? VideoDecision { get; set; }
    public string? AudioDecision { get; set; }
    public string ReportingNode { get; set; } = "";
    public string? NodeDisplayName { get; set; }
    public long? FileDuration { get; set; }
    public string StartedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public double PositionSeconds { get; set; }
    public bool IsPaused { get; set; }
    public bool HasPlaybackControl { get; set; } = true;
    public string? StreamLocation { get; set; }
    public string? ClientIp { get; set; }
    public string? ClientName { get; set; }
    public string? ClientVersion { get; set; }
    public string? ClientBuild { get; set; }
    public string? ClientChannel { get; set; }
    public string? ClientLabel { get; set; }
    public string? ClientLabelFull { get; set; }
    public string? ClientUserAgent { get; set; }
    public string? DeviceId { get; set; }
    public string? DevicePlatform { get; set; }
    public bool? IsJellyfinClient { get; set; }
    public long? LifecycleSeq { get; set; }
    public int AudioTrackIndex { get; set; }
    public bool TranscodeAudio { get; set; }
    public string? TranscodeHwAccel { get; set; }
    public string? ToneMapMode { get; set; }
    public long? StreamBitrateKbps { get; set; }
    public long? TargetBitrateKbps { get; set; }
    public long? SourceBitrateKbps { get; set; }
    public string? SourceContainer { get; set; }
    public string? SourceVideoCodec { get; set; }
    public string? SourceVideoResolution { get; set; }
    public int? SourceVideoHeight { get; set; }
    public string? SourceAudioCodec { get; set; }
    public int? SourceAudioChannels { get; set; }
    public string? SourceAudioLayout { get; set; }
    public string? SourceAudioLanguage { get; set; }
    public string? SourceAudioTitle { get; set; }
    public string? OutputContainer { get; set; }
    public string? OutputProtocol { get; set; }
    public string? TargetVideoCodec { get; set; }
    public string? TargetResolution { get; set; }
    public string? TargetAudioCodec { get; set; }
    public int? TargetAudioChannels { get; set; }
    public string? RequestedVideoCodec { get; set; }
    public string? RequestedVideoResolution { get; set; }
    public string? RoutingWorkload { get; set; }
    public string? RoutingExecution { get; set; }
    public string? RoutingExecutionNodeId { get; set; }
    public string? RoutingExecutionNodeName { get; set; }
    public string? RoutingEgress { get; set; }
    public string? RoutingEgressNodeId { get; set; }
    public string? RoutingEgressNodeName { get; set; }
    /// <summary>Empty means the default network; null means the server did not report it.</summary>
    public string? RoutingNetworkProvider { get; set; }
}

/// <summary>One account seen from an IP (<c>AdminIPUser</c>).</summary>
public sealed class AdminActivityIpUser
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string FirstSeen { get; set; } = "";
    public string LastSeen { get; set; } = "";
    public long RequestCount { get; set; }
}

/// <summary>An operational log row (<c>AdminOperationalLog</c>); the FFmpeg console reads these.</summary>
public sealed class AdminActivityLogRow
{
    public string Id { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Component { get; set; } = "";
    public string Message { get; set; } = "";
    public string? NodeId { get; set; }
    public string? PlaybackSessionId { get; set; }
    public Dictionary<string, JsonElement>? Attrs { get; set; }
}

/// <summary>The ordered identity of one sequenced administrator playback command.</summary>
public readonly record struct AdminActivityCommandIdentity(string CommandId, long Sequence);

/// <summary><c>AdminActivityCommandReceipt</c>.</summary>
public sealed class AdminActivityCommandReceipt
{
    public string CommandId { get; set; } = "";
    public long Sequence { get; set; }
    /// <summary>applied | replayed</summary>
    public string Outcome { get; set; } = "";
    /// <summary>dispatched | fallback_scheduled</summary>
    public string Delivery { get; set; } = "";
}

/// <summary><c>AdminActivityTerminateReceipt</c>.</summary>
public sealed class AdminActivityTerminateReceipt
{
    public string SessionId { get; set; } = "";
    public bool AuthorityRevoked { get; set; }
    public bool AlreadyRevoked { get; set; }
    public string DurableState { get; set; } = "";
    public bool ClientNotified { get; set; }
    public string Delivery { get; set; } = "";
    public string? CommandId { get; set; }
}

/// <summary><c>AdminPlaybackCommandCapabilitiesOutputBody</c>.</summary>
public sealed class AdminActivityCommandCapabilities
{
    public bool Available { get; set; }
    public List<string> Actions { get; set; } = [];
    public bool SequencedCommands { get; set; }
    public bool TerminateRevokesAuthority { get; set; }
    public string Revision { get; set; } = "";
    public string State { get; set; } = "";
    public bool Allowed { get; set; }
}

/// <summary>One page of an IP lookup, already validated.</summary>
public sealed record AdminActivityIpUserPage(IReadOnlyList<AdminActivityIpUser> Items, string? NextCursor);
