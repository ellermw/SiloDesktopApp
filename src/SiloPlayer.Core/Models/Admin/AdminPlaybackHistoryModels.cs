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
    string? MediaItemId = null)
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

    /// <summary>The canonical filter: a profile without an account is dropped, as the WebUI effect replaces it away.</summary>
    public AdminPlaybackHistoryFilter Normalized() => this with
    {
        UserId = RequestUserId,
        ProfileId = HasUser ? ProfileId : null,
        MediaItemId = RequestMediaItemId,
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
            string.IsNullOrWhiteSpace(get("media_item_id")) ? null : get("media_item_id")!.Trim()).Normalized();
    }

    /// <summary>The WebUI URL search parameters for this filter (absent values omitted).</summary>
    public IReadOnlyDictionary<string, string> ToQuery()
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (HasUser) query["user_id"] = RequestUserId!;
        if (HasUser && !string.IsNullOrEmpty(ProfileId)) query["profile_id"] = ProfileId!;
        if (Completion != AdminPlaybackHistoryCompletion.All) query["completed"] = CompletedValue;
        if (RequestMediaItemId is { } item) query["media_item_id"] = item;
        return query;
    }
}

/// <summary>
/// The bounded first page WebUI reads (one request, no cursor walk).
/// <see cref="ServerHasMore"/> says the server has older attempts beyond this
/// window; counts derived from <see cref="Rows"/> are visible-window counts, not totals.
/// </summary>
public sealed record AdminPlaybackHistoryWindow(IReadOnlyList<AdminPlaybackHistoryRow> Rows, bool ServerHasMore, int RequestedLimit);

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
