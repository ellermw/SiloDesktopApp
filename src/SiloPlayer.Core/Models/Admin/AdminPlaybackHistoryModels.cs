using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>History response fields stay nullable so missing wire fields cannot become valid empty values.</summary>
public sealed class AdminPlaybackHistoryWirePage
{
    public List<AdminPlaybackHistoryWireEntry>? Items { get; set; }
    public AdminPlaybackHistoryWirePageInfo? Page { get; set; }
}

public sealed class AdminPlaybackHistoryWirePageInfo
{
    public bool? HasMore { get; set; }
    public string? NextCursor { get; set; }
}

/// <summary>
/// Wire shape of one <c>GET /api/v2/admin/playback-history</c> entry
/// (contract schema <c>AdminPlaybackHistoryEntry</c>, official Silo
/// e61187daaf72e8accad486eb2f632c753ae211d5). Required fields are nullable
/// here only so an absent field can be told from a zero/false value and the
/// whole page refused, as WebUI <c>isEntry</c> does.
/// </summary>
public sealed class AdminPlaybackHistoryWireEntry
{
    public string? SessionId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public string? MediaItemId { get; set; }
    public string? MediaFileId { get; set; }
    public string? MediaTitle { get; set; }
    public string? MediaType { get; set; }
    public string? PlayMethod { get; set; }
    public string? StartedAt { get; set; }
    public string? EndedAt { get; set; }
    /// <summary>Where the play ended. Official name since Silo Server 9df62cd42 (#4552); older servers send it as <c>watched_seconds</c>.</summary>
    public double? EndPositionSeconds { get; set; }
    /// <summary>The same value under its name before the rename, still read so an older server keeps working.</summary>
    public double? WatchedSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool? Completed { get; set; }
    public double? PlayedSeconds { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public bool? Measured { get; set; }
    public double? StartPositionSeconds { get; set; }
    public double? PausedSeconds { get; set; }
    public string? MeasuredSince { get; set; }
    public string? MeasuredUntil { get; set; }
    public string? ClientIp { get; set; }
    public string? DeviceId { get; set; }
    public string? DevicePlatform { get; set; }
    public string? ClientName { get; set; }
    public string? ClientVersion { get; set; }
    public string? VideoDecision { get; set; }
    public string? AudioDecision { get; set; }
    public string? EndReason { get; set; }
    public long? LifecycleSeq { get; set; }

    // Added in official c59798116 (Insights and the Playback History rework). Every one may be absent on an older server.
    /// <summary>Canonical reasons playback adapted away from direct play; empty for direct play or pre-upgrade rows.</summary>
    public List<string>? AdaptationReasons { get; set; }
    /// <summary>True when the play originated on the Jellyfin compatibility surface; null on pre-upgrade rows.</summary>
    public bool? CompatOrigin { get; set; }
    /// <summary>True when compat_origin is true or client_name identifies a Jellyfin-ecosystem client.</summary>
    public bool? IsJellyfinClient { get; set; }
    public string? LibraryId { get; set; }
    public string? LibraryName { get; set; }
    public string? OutputContainer { get; set; }
    public string? OutputProtocol { get; set; }
    public string? ReportingNode { get; set; }
    /// <summary>False on pre-upgrade rows: no delivered route facts were captured.</summary>
    public bool? RouteFactsRecorded { get; set; }
    public string? RoutingEgress { get; set; }
    public string? RoutingEgressNodeId { get; set; }
    public string? RoutingEgressNodeName { get; set; }
    public string? RoutingExecution { get; set; }
    public string? RoutingExecutionNodeId { get; set; }
    public string? RoutingExecutionNodeName { get; set; }
    public long? SourceAudioChannels { get; set; }
    public string? SourceAudioCodec { get; set; }
    public long? SourceBitrateKbps { get; set; }
    public string? SourceContainer { get; set; }
    public bool? SourceHdr { get; set; }
    public string? SourceResolution { get; set; }
    public string? SourceVideoCodec { get; set; }
    public string? SourceVideoRangeType { get; set; }
    /// <summary>Negotiated bitrate-policy network classification: <c>local</c> or <c>remote</c>; null when unknown.</summary>
    public string? StreamLocation { get; set; }
    public AdminPlaybackHistorySubtitle? Subtitle { get; set; }
    public long? TargetAudioChannels { get; set; }
    public string? TargetAudioCodec { get; set; }
    public long? TargetBitrateKbps { get; set; }
    public string? TargetResolution { get; set; }
    public string? TargetVideoCodec { get; set; }
    public string? ToneMapMode { get; set; }
    public string? TranscodeHwAccel { get; set; }
}

/// <summary>The subtitle a finished play delivered (official <c>AdminPlaybackHistoryEntry.subtitle</c>); null on the entry when it showed none.</summary>
public sealed class AdminPlaybackHistorySubtitle
{
    [JsonPropertyName("codec")] public string? Codec { get; set; }
    /// <summary><c>burn_in</c>, <c>embedded</c>, <c>hls_rendition</c> or <c>sidecar</c>; clients ignore values they do not know.</summary>
    [JsonPropertyName("delivery")] public string? Delivery { get; set; }
    [JsonPropertyName("forced")] public bool Forced { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("hearing_impaired")] public bool HearingImpaired { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    /// <summary><c>embedded</c>, <c>external</c> or <c>downloaded</c>.</summary>
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("track_id")] public string? TrackId { get; set; }
}

/// <summary>
/// One finalized playback attempt (never a live stream). Identifiers stay the
/// opaque strings the contract sends; <see cref="StartedAt"/>/<see cref="EndedAt"/>
/// keep the raw instant so an unparsable value is shown as sent, as WebUI does.
/// The measurement, client, device, decision and end-reason fields are carried
/// for completeness; the official e611 table renders only the media, account,
/// profile, method, watch time, status, ended/started and logs columns.
/// </summary>
public sealed record AdminPlaybackHistoryRow(
    string SessionId,
    string UserId,
    string Username,
    string ProfileId,
    string ProfileName,
    string MediaItemId,
    string MediaFileId,
    string MediaTitle,
    string MediaType,
    string PlayMethod,
    string StartedAt,
    string EndedAt,
    double WatchedSeconds,
    double? DurationSeconds,
    bool Completed,
    double? PlayedSeconds,
    string SeriesTitle,
    int? SeasonNumber,
    int? EpisodeNumber)
{
    public bool Measured { get; init; }
    public double? StartPositionSeconds { get; init; }
    public double? PausedSeconds { get; init; }
    public string? MeasuredSince { get; init; }
    public string? MeasuredUntil { get; init; }
    public string? ClientIp { get; init; }
    public string? DeviceId { get; init; }
    public string? DevicePlatform { get; init; }
    public string? ClientName { get; init; }
    public string? ClientVersion { get; init; }
    public string? VideoDecision { get; init; }
    public string? AudioDecision { get; init; }
    public string? EndReason { get; init; }
    public long? LifecycleSeq { get; init; }

    // Official c59798116 additions. Absent on an older server: lists are empty, flags false, the rest null.
    public IReadOnlyList<string> AdaptationReasons { get; init; } = [];
    public bool? CompatOrigin { get; init; }
    public bool IsJellyfinClient { get; init; }
    /// <summary>The owning library, or null when the file was removed or the id is not a positive integer.</summary>
    public string? LibraryId { get; init; }
    public string? LibraryName { get; init; }
    public string? OutputContainer { get; init; }
    public string? OutputProtocol { get; init; }
    public string? ReportingNode { get; init; }
    /// <summary>False on pre-upgrade rows (and on a server that does not send it): file details and route are unavailable.</summary>
    public bool RouteFactsRecorded { get; init; }
    public string? RoutingEgress { get; init; }
    /// <summary>A positive integer node id, else null (official <c>parseOptionalNumericId</c>).</summary>
    public string? RoutingEgressNodeId { get; init; }
    public string? RoutingEgressNodeName { get; init; }
    public string? RoutingExecution { get; init; }
    /// <summary>A positive integer node id, else null (official <c>parseOptionalNumericId</c>).</summary>
    public string? RoutingExecutionNodeId { get; init; }
    public string? RoutingExecutionNodeName { get; init; }
    public long? SourceAudioChannels { get; init; }
    public string? SourceAudioCodec { get; init; }
    public long? SourceBitrateKbps { get; init; }
    public string? SourceContainer { get; init; }
    public bool? SourceHdr { get; init; }
    public string? SourceResolution { get; init; }
    public string? SourceVideoCodec { get; init; }
    public string? SourceVideoRangeType { get; init; }
    /// <summary><c>local</c>, <c>remote</c> or null.</summary>
    public string? StreamLocation { get; init; }
    public AdminPlaybackHistorySubtitle? Subtitle { get; init; }
    public long? TargetAudioChannels { get; init; }
    public string? TargetAudioCodec { get; init; }
    public long? TargetBitrateKbps { get; init; }
    public string? TargetResolution { get; init; }
    public string? TargetVideoCodec { get; init; }
    public string? ToneMapMode { get; init; }
    public string? TranscodeHwAccel { get; init; }
}

/// <summary>WebUI completion filter values (<c>completed</c> query parameter).</summary>
public enum AdminPlaybackHistoryCompletion
{
    All,
    Completed,
    Partial,
}

/// <summary>
/// The Playback History URL state (<c>/admin/history?user_id&amp;profile_id&amp;completed&amp;media_item_id</c>).
/// <see cref="UserId"/> is the raw URL value; <see cref="RequestUserId"/> is what is
/// sent (WebUI <c>Number(user_id)</c> truthiness: a positive Int64 only).
/// </summary>
public sealed record AdminPlaybackHistoryFilter(
    string? UserId = null,
    string? ProfileId = null,
    AdminPlaybackHistoryCompletion Completion = AdminPlaybackHistoryCompletion.All,
    string? MediaItemId = null,
    string? SeriesId = null,
    string? LibraryId = null,
    string? Search = null,
    string? PlayMethod = null,
    string? StreamLocation = null,
    bool SubtitleBurnIn = false,
    string? ClientName = null,
    string? DevicePlatform = null,
    string? SourceVideoCodec = null,
    string? AdaptationReason = null,
    string? StartedAfter = null,
    string? StartedBefore = null)
{
    public bool HasUser => RequestUserId != null;

    /// <summary>The account id sent as <c>user_id</c>, canonical decimal, or null when absent or not a positive safe integer.</summary>
    public string? RequestUserId =>
        long.TryParse(UserId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
        && id > 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>The profile sent: only with a chosen account (WebUI drops a profile without one).</summary>
    public string? RequestProfileId => HasUser && !string.IsNullOrEmpty(ProfileId) ? ProfileId : null;

    /// <summary>Trimmed item filter, or null.</summary>
    public string? RequestMediaItemId => string.IsNullOrWhiteSpace(MediaItemId) ? null : MediaItemId.Trim();

    public string CompletedValue => Completion switch
    {
        AdminPlaybackHistoryCompletion.Completed => "true",
        AdminPlaybackHistoryCompletion.Partial => "false",
        _ => "all",
    };

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Trimmed series filter (every episode of one series), or null.</summary>
    public string? RequestSeriesId => Trimmed(SeriesId);

    /// <summary>The library sent as <c>library_id</c>, canonical decimal (WebUI <c>Number()</c> truthiness), or null when absent or not a positive integer.</summary>
    public string? RequestLibraryId =>
        long.TryParse(LibraryId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
        && id > 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public string? RequestSearch => Trimmed(Search);

    /// <summary>One of <c>direct</c>, <c>remux</c>, <c>transcode</c>; anything else (including <c>all</c>) is no filter (WebUI <c>buildAdminPlaybackHistoryQuery</c>).</summary>
    public string? RequestPlayMethod => PlayMethod?.Trim() is "direct" or "remux" or "transcode" ? PlayMethod.Trim() : null;

    /// <summary><c>local</c> or <c>remote</c>; anything else (including <c>all</c>) is no filter.</summary>
    public string? RequestStreamLocation => StreamLocation?.Trim() is "local" or "remote" ? StreamLocation.Trim() : null;

    public string? RequestClientName => Trimmed(ClientName);
    public string? RequestDevicePlatform => Trimmed(DevicePlatform);
    public string? RequestSourceVideoCodec => Trimmed(SourceVideoCodec);
    public string? RequestAdaptationReason => Trimmed(AdaptationReason);
    public string? RequestStartedAfter => Trimmed(StartedAfter);
    public string? RequestStartedBefore => Trimmed(StartedBefore);

    /// <summary>True when any of the filters added with the Insights rework (series, library, search, method, network, burn-in, client, platform, codec, reason, dates) is set.</summary>
    public bool HasRichFilters => RequestSeriesId != null || RequestLibraryId != null || RequestSearch != null || RequestPlayMethod != null
        || RequestStreamLocation != null || SubtitleBurnIn || RequestClientName != null || RequestDevicePlatform != null
        || RequestSourceVideoCodec != null || RequestAdaptationReason != null || RequestStartedAfter != null || RequestStartedBefore != null;

    /// <summary>The canonical filter: a profile without an account is dropped, as the WebUI effect replaces it away.</summary>
    public AdminPlaybackHistoryFilter Normalized() => this with
    {
        UserId = RequestUserId,
        ProfileId = HasUser ? ProfileId : null,
        MediaItemId = RequestMediaItemId,
        SeriesId = RequestSeriesId,
        LibraryId = RequestLibraryId,
        Search = RequestSearch,
        PlayMethod = RequestPlayMethod,
        StreamLocation = RequestStreamLocation,
        ClientName = RequestClientName,
        DevicePlatform = RequestDevicePlatform,
        SourceVideoCodec = RequestSourceVideoCodec,
        AdaptationReason = RequestAdaptationReason,
        StartedAfter = RequestStartedAfter,
        StartedBefore = RequestStartedBefore,
    };

    /// <summary>Reads the WebUI URL search parameters.</summary>
    public static AdminPlaybackHistoryFilter FromQuery(Func<string, string?> get)
    {
        static string? Present(string? value) => string.IsNullOrEmpty(value) || value == "all" ? null : value;
        var completion = get("completed") switch
        {
            "true" => AdminPlaybackHistoryCompletion.Completed,
            "false" => AdminPlaybackHistoryCompletion.Partial,
            _ => AdminPlaybackHistoryCompletion.All,
        };
        return new AdminPlaybackHistoryFilter(Present(get("user_id")), Present(get("profile_id")), completion,
            string.IsNullOrWhiteSpace(get("media_item_id")) ? null : get("media_item_id")!.Trim(),
            SeriesId: get("series_id"), LibraryId: Present(get("library_id")), Search: get("search"),
            PlayMethod: get("play_method"), StreamLocation: get("stream_location"),
            // Only "true" is offered: the WebUI reads subtitle_burn_in === "true" and any other value is no filter.
            SubtitleBurnIn: get("subtitle_burn_in") == "true",
            ClientName: get("client_name"), DevicePlatform: get("device_platform"), SourceVideoCodec: get("source_video_codec"),
            AdaptationReason: get("adaptation_reason"), StartedAfter: get("started_after"), StartedBefore: get("started_before")).Normalized();
    }

    /// <summary>The WebUI URL search parameters for this filter (absent values omitted).</summary>
    public IReadOnlyDictionary<string, string> ToQuery()
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (HasUser) query["user_id"] = RequestUserId!;
        if (HasUser && !string.IsNullOrEmpty(ProfileId)) query["profile_id"] = ProfileId!;
        if (Completion != AdminPlaybackHistoryCompletion.All) query["completed"] = CompletedValue;
        if (RequestMediaItemId is { } item) query["media_item_id"] = item;
        if (RequestSeriesId is { } series) query["series_id"] = series;
        if (RequestLibraryId is { } library) query["library_id"] = library;
        if (RequestPlayMethod is { } method) query["play_method"] = method;
        if (RequestStreamLocation is { } location) query["stream_location"] = location;
        if (SubtitleBurnIn) query["subtitle_burn_in"] = "true";
        if (RequestClientName is { } client) query["client_name"] = client;
        if (RequestDevicePlatform is { } platform) query["device_platform"] = platform;
        if (RequestSourceVideoCodec is { } codec) query["source_video_codec"] = codec;
        if (RequestAdaptationReason is { } reason) query["adaptation_reason"] = reason;
        if (RequestStartedAfter is { } after) query["started_after"] = after;
        if (RequestStartedBefore is { } before) query["started_before"] = before;
        if (RequestSearch is { } search) query["search"] = search;
        return query;
    }
}

/// <summary>
/// The bounded first page WebUI reads (one request, no cursor walk).
/// <see cref="ServerHasMore"/> says the server has older attempts beyond this
/// window; counts derived from <see cref="Rows"/> are visible-window counts, not totals.
/// </summary>
public sealed record AdminPlaybackHistoryWindow(IReadOnlyList<AdminPlaybackHistoryRow> Rows, bool ServerHasMore, int RequestedLimit)
{
    /// <summary>
    /// The cursor of the next page when <see cref="ServerHasMore"/>; null at the end. Always usable: a page that says it has more
    /// without one is refused. The cursor is opaque and only valid with the same filter and page size.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// GET /admin/playback-history/capabilities (official <c>AdminPlaybackHistoryCapabilities</c>). The official pages do not call it;
/// <see cref="RichFilters"/>, <see cref="RouteFacts"/>, <see cref="SeriesFilter"/> and <see cref="SessionDetail"/> are new in c59798116.
/// </summary>
public sealed class AdminPlaybackHistoryCapabilities
{
    [JsonRequired, JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonRequired, JsonPropertyName("available")] public bool Available { get; set; }
    [JsonRequired, JsonPropertyName("client_identity")] public bool ClientIdentity { get; set; }
    [JsonRequired, JsonPropertyName("client_ip")] public bool ClientIp { get; set; }
    [JsonRequired, JsonPropertyName("device_identity")] public bool DeviceIdentity { get; set; }
    [JsonRequired, JsonPropertyName("end_reason")] public bool EndReason { get; set; }
    [JsonRequired, JsonPropertyName("end_reason_values")] public List<string> EndReasonValues { get; set; } = [];
    [JsonRequired, JsonPropertyName("ended_before")] public bool EndedBefore { get; set; }
    [JsonRequired, JsonPropertyName("lifecycle_seq")] public bool LifecycleSeq { get; set; }
    [JsonRequired, JsonPropertyName("revision")] public string Revision { get; set; } = "";
    /// <summary>The list accepts search, play_method, device_platform, client_name, source_video_codec, subtitle_burn_in, stream_location, adaptation_reason, library_id, started_after and started_before.</summary>
    [JsonRequired, JsonPropertyName("rich_filters")] public bool RichFilters { get; set; }
    /// <summary>Entries carry delivered route facts, adaptation reasons and joined source file specs.</summary>
    [JsonRequired, JsonPropertyName("route_facts")] public bool RouteFacts { get; set; }
    /// <summary>The list accepts series_id.</summary>
    [JsonRequired, JsonPropertyName("series_filter")] public bool SeriesFilter { get; set; }
    /// <summary>GET /admin/playback-history/{session_id} is served.</summary>
    [JsonRequired, JsonPropertyName("session_detail")] public bool SessionDetail { get; set; }
    [JsonRequired, JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonRequired, JsonPropertyName("stream_decisions")] public bool StreamDecisions { get; set; }
    [JsonRequired, JsonPropertyName("watch_measurement")] public bool WatchMeasurement { get; set; }
}

/// <summary>Wire shape of the <c>GET /api/v2/admin/users</c> fields the account picker uses.</summary>
public sealed class AdminHistoryUserWire
{
    public string? Id { get; set; }
    public string? Username { get; set; }
}

/// <summary>An account choice in the Playback History user picker.</summary>
public sealed record AdminHistoryUserOption(string Id, string Username);

/// <summary>Bounded account picker read; <see cref="Truncated"/> means more accounts exist than were listed.</summary>
public sealed record AdminHistoryUserList(IReadOnlyList<AdminHistoryUserOption> Items, bool Truncated);

/// <summary>Wire shape of the <c>GET /api/v2/admin/users/{id}/profiles</c> fields the profile picker uses.</summary>
public sealed class AdminHistoryProfileWire
{
    public string? Id { get; set; }
    public string? Name { get; set; }
}

/// <summary>A household profile choice for one account.</summary>
public sealed record AdminHistoryProfileOption(string Id, string Name);

/// <summary>One account's profiles, read once and bounded; <see cref="Truncated"/> when more were returned than retained.</summary>
public sealed record AdminHistoryProfileList(string UserId, IReadOnlyList<AdminHistoryProfileOption> Items, bool Truncated);
