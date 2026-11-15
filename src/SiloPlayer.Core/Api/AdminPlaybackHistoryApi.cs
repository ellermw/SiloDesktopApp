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
/// checked before sending and after the response, never recaptured. History is
/// one bounded page (no cursor walk); the account picker is a bounded walk that
/// reports truncation; one account's profiles are a single bounded read.
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

    private static readonly Regex PositiveInteger = new("^[1-9][0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>The request path for <paramref name="filter"/>: limit first, then the filters WebUI sends.</summary>
    public static string HistoryPath(AdminPlaybackHistoryFilter filter, int limit = PageLimit)
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
        return builder.ToString();
    }

    private static string? Bounded(string? value) =>
        value != null && value.Length > MaxFilterLength ? throw new ArgumentException("Filter value is too long.", nameof(value)) : value;

    /// <summary>
    /// Reads the first page of finalized attempts matching <paramref name="filter"/>.
    /// An inconsistent page (missing fields, has_more without a usable cursor,
    /// a cursor without has_more, more rows than asked for) is refused whole.
    /// </summary>
    public async Task<AdminPlaybackHistoryWindow> ReadWindowAsync(MonitorAuthorityLease lease, AdminPlaybackHistoryFilter filter,
        int limit = PageLimit, CancellationToken ct = default)
    {
        limit = AdminPaging.ClampLimit(limit, MaxLimit);
        var path = HistoryPath(filter, limit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        lease.ThrowIfRetired();
        var page = await client.SendRequestAsync<AdminPlaybackHistoryWirePage>(
            lease.Context, HttpMethod.Get, path, null, linked.Token).ConfigureAwait(false);
        lease.ThrowIfRetired();
        linked.Token.ThrowIfCancellationRequested();
        return ValidateWindow(page, limit);
    }

    /// <summary>WebUI <c>listAdminPlaybackHistory</c> page validation, plus the requested-limit bound.</summary>
    public static AdminPlaybackHistoryWindow ValidateWindow(AdminPlaybackHistoryWirePage? page, int limit)
    {
        if (page?.Page?.HasMore == null || page.Items == null) throw Invalid();
        var items = page.Items;
        if (items.Count > limit) throw Invalid();
        if (page.Page.HasMore.Value && (items.Count == 0 || !AdminPaging.IsValidCursor(page.Page.NextCursor))) throw Invalid();
        if (!page.Page.HasMore.Value && !string.IsNullOrEmpty(page.Page.NextCursor)) throw Invalid();
        var rows = new AdminPlaybackHistoryRow[items.Count];
        for (var index = 0; index < items.Count; index++) rows[index] = Project(items[index] ?? throw Invalid());
        return new AdminPlaybackHistoryWindow(Array.AsReadOnly(rows), page.Page.HasMore.Value, limit);
    }

    private static InvalidDataException Invalid() => new(AdminPlaybackAuditText.T("web.admin.errors.playbackHistoryInvalid"));

    private static AdminPlaybackHistoryRow Project(AdminPlaybackHistoryWireEntry entry)
    {
        if (string.IsNullOrEmpty(entry.SessionId) || !PositiveInteger.IsMatch(entry.UserId ?? "") || !PositiveInteger.IsMatch(entry.MediaFileId ?? "")
            || entry.StartedAt == null || entry.EndedAt == null || entry.WatchedSeconds is not { } watched || entry.Completed is not { } completed)
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
