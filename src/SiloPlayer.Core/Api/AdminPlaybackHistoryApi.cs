using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Admin › Playback › History reads (official Silo
/// e61187daaf72e8accad486eb2f632c753ae211d5: <c>api/v2/adminPlaybackHistory.ts</c>,
/// <c>hooks/queries/admin/history.ts</c>, <c>internal/apiv2/admin_playback_history.go</c>).
/// Every call runs under the caller's captured <see cref="MonitorAuthorityLease"/>:
/// checked before sending and after the response, never recaptured. A history read is
/// one server page (<see cref="ReadWindowAsync"/> the first, <see cref="ReadPageAsync"/> any page by cursor, as the c59798116 WebUI
/// walks it); the account picker is a bounded walk that reports truncation; one
/// account's profiles are a single bounded read.
/// Separate from <see cref="AdminMonitorApi"/> by the foundation contract.
/// </summary>
public sealed class AdminPlaybackHistoryApi(SiloApiClient client)
{
    /// <summary>The v2 page ceiling (<c>ADMIN_PLAYBACK_HISTORY_MAX_LIMIT</c>).</summary>
    public const int MaxLimit = 200;

    /// <summary>What the official page asks for (<c>AdminPlaybackHistory.tsx</c> passes <c>limit: 100</c>).</summary>
    public const int PageLimit = 100;

    /// <summary>Accounts per picker page (WebUI <c>listAdminUsers</c> asks for 200).</summary>
    public const int UserPageSize = 200;

    /// <summary>Most picker pages read. WebUI walks every account; the native picker stops and says so.</summary>
    public const int UserMaxPages = 5;

    /// <summary>Most accounts retained for the picker.</summary>
    public const int UserMaxItems = 1000;

    /// <summary>Most profiles retained for one account's picker.</summary>
    public const int ProfileMaxItems = 100;

    /// <summary>Longest accepted filter value (contract <c>maxLength</c> for profile and item filters).</summary>
    public const int MaxFilterLength = 1024;

    // Server maxLength limits of the filters added with the Insights rework (official c59798116, internal/apiv2/admin_playback_history.go).
    public const int MaxSearchLength = 256;
    public const int MaxClientNameLength = 256;
    public const int MaxDevicePlatformLength = 128;
    public const int MaxSourceVideoCodecLength = 64;
    public const int MaxAdaptationReasonLength = 128;
    /// <summary>Longest session identifier the detail read accepts.</summary>
    public const int MaxSessionIdLength = 256;
    /// <summary>Longest instant accepted for the started_after / started_before filters (an RFC 3339 instant is far shorter).</summary>
    public const int MaxInstantLength = 64;

    private static readonly Regex PositiveInteger = new("^[1-9][0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The request path for <paramref name="filter"/>: limit first, then the filters WebUI sends, then the opaque <paramref name="cursor"/>
    /// of the page to read (null for the first page). A cursor is only valid with the filter and page size it came from.
    /// </summary>
    public static string HistoryPath(AdminPlaybackHistoryFilter filter, int limit = PageLimit, string? cursor = null)
    {
        var builder = new StringBuilder("/api/v2/admin/playback-history?limit=")
            .Append(AdminPaging.ClampLimit(limit, MaxLimit).ToString(CultureInfo.InvariantCulture));
        void Add(string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            builder.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
        Add("user_id", filter.RequestUserId);
        Add("profile_id", Bounded(filter.RequestProfileId));
        Add("media_item_id", Bounded(filter.RequestMediaItemId));
        if (filter.Completion != AdminPlaybackHistoryCompletion.All) Add("completed", filter.CompletedValue);
        Add("series_id", Bounded(filter.RequestSeriesId));
        Add("library_id", filter.RequestLibraryId);
        Add("play_method", filter.RequestPlayMethod);
        Add("stream_location", filter.RequestStreamLocation);
        if (filter.SubtitleBurnIn) Add("subtitle_burn_in", "true");
        Add("client_name", Bounded(filter.RequestClientName, MaxClientNameLength));
        Add("device_platform", Bounded(filter.RequestDevicePlatform, MaxDevicePlatformLength));
        Add("source_video_codec", Bounded(filter.RequestSourceVideoCodec, MaxSourceVideoCodecLength));
        Add("adaptation_reason", Bounded(filter.RequestAdaptationReason, MaxAdaptationReasonLength));
        Add("started_after", Bounded(filter.RequestStartedAfter, MaxInstantLength));
        Add("started_before", Bounded(filter.RequestStartedBefore, MaxInstantLength));
        Add("search", Bounded(filter.RequestSearch, MaxSearchLength));
        if (cursor != null)
        {
            if (!AdminPaging.IsValidCursor(cursor)) throw new ArgumentException("Invalid continuation cursor.", nameof(cursor));
            Add("cursor", cursor);
        }
        return builder.ToString();
    }

    private static string? Bounded(string? value, int maxLength = MaxFilterLength) =>
        value != null && value.Length > maxLength ? throw new ArgumentException("Filter value is too long.", nameof(value)) : value;

    /// <summary>
    /// Reads the first page of finalized attempts matching <paramref name="filter"/>.
    /// An inconsistent page (missing fields, has_more without a usable cursor,
    /// a cursor without has_more, more rows than asked for) is refused whole.
    /// </summary>
    public Task<AdminPlaybackHistoryWindow> ReadWindowAsync(MonitorAuthorityLease lease, AdminPlaybackHistoryFilter filter,
        int limit = PageLimit, CancellationToken ct = default)
        => ReadPageAsync(lease, filter, limit, null, ct);

    /// <summary>
    /// Reads one server page (the WebUI keyset walk): <paramref name="limit"/> is the page size and <paramref name="cursor"/> the
    /// <see cref="AdminPlaybackHistoryWindow.NextCursor"/> of the previous page, null for the first. The same checks as
    /// <see cref="ReadWindowAsync"/> apply, and a page that hands back the cursor it was asked for is refused (official
    /// <c>listAdminPlaybackHistory</c>), so a stuck server cannot loop a pager.
    /// </summary>
    public async Task<AdminPlaybackHistoryWindow> ReadPageAsync(MonitorAuthorityLease lease, AdminPlaybackHistoryFilter filter,
        int limit, string? cursor, CancellationToken ct = default)
    {
        limit = AdminPaging.ClampLimit(limit, MaxLimit);
        var path = HistoryPath(filter, limit, cursor);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        var page = await client.SendRequestAsync<AdminPlaybackHistoryWirePage>(
            lease.Context, HttpMethod.Get, path, null, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        return ValidateWindow(page, limit, cursor);
    }

    /// <summary>
    /// Reads one finalized attempt (GET /admin/playback-history/{session_id}), for a <c>?session=</c> link whose row is not on the
    /// current page. Null when the server answers 404 (an unknown session, or a server without the endpoint: see
    /// <see cref="AdminPlaybackHistoryCapabilities.SessionDetail"/>). An entry that fails the list's validation is refused.
    /// </summary>
    public async Task<AdminPlaybackHistoryRow?> GetEntryAsync(MonitorAuthorityLease lease, string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaxSessionIdLength)
            throw new ArgumentException("Expected a session identifier.", nameof(sessionId));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        AdminPlaybackHistoryWireEntry? entry;
        try
        {
            entry = await client.SendRequestAsync<AdminPlaybackHistoryWireEntry?>(lease.Context, HttpMethod.Get,
                "/api/v2/admin/playback-history/" + Uri.EscapeDataString(sessionId), null, linked.Token).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            lease.ThrowIfRetired();
            return null;
        }
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        return Project(entry ?? throw Invalid());
    }

    /// <summary>
    /// Reads what this server supports (GET /admin/playback-history/capabilities). Returns null when the endpoint is unavailable
    /// (404, 405 or 501). Older servers that serve the endpoint may omit the flags added by the rework; those flags default to false.
    /// </summary>
    public async Task<AdminPlaybackHistoryCapabilities?> GetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        try
        {
            var capabilities = await client.SendRequestAsync<AdminPlaybackHistoryCapabilities?>(lease.Context, HttpMethod.Get,
                "/api/v2/admin/playback-history/capabilities", null, linked.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            return capabilities ?? throw Invalid();
        }
        catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501)
        {
            lease.ThrowIfRetired();
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            lease.ThrowIfRetired();
            throw Invalid();
        }
    }

    /// <summary>WebUI <c>listAdminPlaybackHistory</c> page validation, plus the requested-limit bound and the repeated-cursor refusal.</summary>
    public static AdminPlaybackHistoryWindow ValidateWindow(AdminPlaybackHistoryWirePage? page, int limit, string? requestCursor = null)
    {
        if (page?.Page?.HasMore == null || page.Items == null) throw Invalid();
        var items = page.Items;
        if (items.Count > limit) throw Invalid();
        if (page.Page.HasMore.Value && (items.Count == 0 || !AdminPaging.IsValidCursor(page.Page.NextCursor)
            || (requestCursor != null && string.Equals(page.Page.NextCursor, requestCursor, StringComparison.Ordinal)))) throw Invalid();
        if (!page.Page.HasMore.Value && !string.IsNullOrEmpty(page.Page.NextCursor)) throw Invalid();
        var rows = new AdminPlaybackHistoryRow[items.Count];
        for (var index = 0; index < items.Count; index++) rows[index] = Project(items[index] ?? throw Invalid());
        return new AdminPlaybackHistoryWindow(Array.AsReadOnly(rows), page.Page.HasMore.Value, limit)
        {
            NextCursor = page.Page.HasMore.Value ? page.Page.NextCursor : null,
        };
    }

    /// <summary>Official <c>parseOptionalNumericId</c>: a positive safe integer as canonical decimal, else null.</summary>
    internal static string? ParseOptionalNumericId(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id is > 0 and <= 9007199254740991
            ? id.ToString(CultureInfo.InvariantCulture) : null;

    private static InvalidDataException Invalid() => new(AdminPlaybackAuditText.T("web.admin.errors.playbackHistoryInvalid"));

    private static AdminPlaybackHistoryRow Project(AdminPlaybackHistoryWireEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SessionId) || !PositiveInteger.IsMatch(entry.UserId ?? "") || !PositiveInteger.IsMatch(entry.MediaFileId ?? "")
            || entry.StartedAt == null || entry.EndedAt == null || (entry.EndPositionSeconds ?? entry.WatchedSeconds) is not { } watched || entry.Completed is not { } completed)
            throw Invalid();
        return new AdminPlaybackHistoryRow(entry.SessionId, entry.UserId!, entry.Username ?? "", entry.ProfileId ?? "", entry.ProfileName ?? "",
            entry.MediaItemId ?? "", entry.MediaFileId!, entry.MediaTitle ?? "", entry.MediaType ?? "", entry.PlayMethod ?? "",
            entry.StartedAt, entry.EndedAt, watched, entry.DurationSeconds, completed, entry.PlayedSeconds, entry.SeriesTitle ?? "",
            entry.SeasonNumber, entry.EpisodeNumber)
        {
            Measured = entry.Measured == true,
            StartPositionSeconds = entry.StartPositionSeconds,
            PausedSeconds = entry.PausedSeconds,
            MeasuredSince = entry.MeasuredSince,
            MeasuredUntil = entry.MeasuredUntil,
            ClientIp = entry.ClientIp,
            DeviceId = entry.DeviceId,
            DevicePlatform = entry.DevicePlatform,
            ClientName = entry.ClientName,
            ClientVersion = entry.ClientVersion,
            VideoDecision = entry.VideoDecision,
            AudioDecision = entry.AudioDecision,
            EndReason = entry.EndReason,
            LifecycleSeq = entry.LifecycleSeq,
            AdaptationReasons = entry.AdaptationReasons is { } reasons ? Array.AsReadOnly(reasons.Where(reason => reason != null).ToArray()) : [],
            CompatOrigin = entry.CompatOrigin,
            IsJellyfinClient = entry.IsJellyfinClient == true,
            LibraryId = ParseOptionalNumericId(entry.LibraryId),
            LibraryName = entry.LibraryName,
            OutputContainer = entry.OutputContainer,
            OutputProtocol = entry.OutputProtocol,
            ReportingNode = entry.ReportingNode,
            RouteFactsRecorded = entry.RouteFactsRecorded == true,
            RoutingEgress = entry.RoutingEgress,
            RoutingEgressNodeId = ParseOptionalNumericId(entry.RoutingEgressNodeId),
            RoutingEgressNodeName = entry.RoutingEgressNodeName,
            RoutingExecution = entry.RoutingExecution,
            RoutingExecutionNodeId = ParseOptionalNumericId(entry.RoutingExecutionNodeId),
            RoutingExecutionNodeName = entry.RoutingExecutionNodeName,
            SourceAudioChannels = entry.SourceAudioChannels,
            SourceAudioCodec = entry.SourceAudioCodec,
            SourceBitrateKbps = entry.SourceBitrateKbps,
            SourceContainer = entry.SourceContainer,
            SourceHdr = entry.SourceHdr,
            SourceResolution = entry.SourceResolution,
            SourceVideoCodec = entry.SourceVideoCodec,
            SourceVideoRangeType = entry.SourceVideoRangeType,
            StreamLocation = entry.StreamLocation,
            Subtitle = entry.Subtitle,
            TargetAudioChannels = entry.TargetAudioChannels,
            TargetAudioCodec = entry.TargetAudioCodec,
            TargetBitrateKbps = entry.TargetBitrateKbps,
            TargetResolution = entry.TargetResolution,
            TargetVideoCodec = entry.TargetVideoCodec,
            ToneMapMode = entry.ToneMapMode,
            TranscodeHwAccel = entry.TranscodeHwAccel,
        };
    }

    /// <summary>
    /// The account picker: at most <see cref="UserMaxPages"/> pages of
    /// <see cref="UserPageSize"/> and <see cref="UserMaxItems"/> accounts, with
    /// repeated or missing continuations refused (foundation walk). A capped read
    /// is returned as <see cref="AdminHistoryUserList.Truncated"/>, never as complete.
    /// </summary>
    public async Task<AdminHistoryUserList> ListUsersAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var read = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("admin-users", "playback-history-picker"),
            new AdminWalkLimits(UserPageSize, UserMaxPages, UserMaxItems),
            (cursor, limit, token) => client.SendRequestAsync<ApiCollectionPage<AdminHistoryUserWire>?>(
                lease.Context, HttpMethod.Get, AdminPaging.WithPage("/api/v2/admin/users", limit, cursor), null, token),
            user => user?.Id, ct).ConfigureAwait(false);
        var items = new List<AdminHistoryUserOption>(read.Items.Count);
        foreach (var user in read.Items)
        {
            if (!PositiveInteger.IsMatch(user.Id ?? "")) throw new InvalidDataException(AdminPlaybackAuditText.T("web.admin.errors.userPaginationInvalid"));
            items.Add(new AdminHistoryUserOption(user.Id!, user.Username ?? ""));
        }
        return new AdminHistoryUserList(items.AsReadOnly(), !read.IsComplete);
    }

    /// <summary>
    /// One account's profiles (WebUI <c>useAdminUserProfiles</c>): a single
    /// read; <c>has_more</c> is refused as an incomplete listing. At most
    /// <see cref="ProfileMaxItems"/> are retained and the rest reported truncated.
    /// </summary>
    public async Task<AdminHistoryProfileList> ListProfilesAsync(MonitorAuthorityLease lease, string userId, CancellationToken ct = default)
    {
        if (!PositiveInteger.IsMatch(userId ?? "")) throw new ArgumentException("Expected a positive account identifier.", nameof(userId));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        var page = await client.SendRequestAsync<ApiCollectionPage<AdminHistoryProfileWire>?>(lease.Context, HttpMethod.Get,
            $"/api/v2/admin/users/{Uri.EscapeDataString(userId!)}/profiles", null, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        if (page?.Page == null || page.Items == null || page.Page.HasMore) throw new InvalidDataException("Incomplete profile listing");
        var items = new List<AdminHistoryProfileOption>(Math.Min(page.Items.Count, ProfileMaxItems));
        foreach (var profile in page.Items)
        {
            if (string.IsNullOrEmpty(profile?.Id)) throw new InvalidDataException("Incomplete profile listing");
            if (items.Count < ProfileMaxItems) items.Add(new AdminHistoryProfileOption(profile.Id, profile.Name ?? ""));
        }
        return new AdminHistoryProfileList(userId!, items.AsReadOnly(), page.Items.Count > ProfileMaxItems);
    }
}
