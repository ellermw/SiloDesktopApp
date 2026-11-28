using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The pages of one People read (official <c>useAdminInsightsPeopleInfinite</c>): the first page and every page "Show more accounts" added, for
/// one window, search and sort. The accounts of all pages are merged once each, the first page's figures (<c>total_accounts</c>, KPIs,
/// coverage, the window) stand for the whole list, and the next page starts at the number of rows loaded.
/// <para>
/// One instance is the cached result of one reader key, so going back to a sort already read shows the pages it had. Not thread-safe: call
/// it from the UI thread; <see cref="LoadMoreAsync"/> completes back on the caller's context.
/// </para>
/// </summary>
public sealed class AdminInsightsPeoplePages
{
    private readonly List<AdminInsightsPeople> _pages;
    private readonly Func<int, CancellationToken, Task<AdminInsightsPeople>> _fetch;
    private IReadOnlyList<AdminInsightsPersonAccount>? _accounts;
    private Task<bool>? _loading;

    /// <param name="first">The first page.</param>
    /// <param name="fetchPage">Reads the page that starts at an offset, under the lease the first page was read under.</param>
    public AdminInsightsPeoplePages(AdminInsightsPeople first, Func<int, CancellationToken, Task<AdminInsightsPeople>> fetchPage)
    {
        _pages = [first];
        _fetch = fetchPage;
    }

    public AdminInsightsPeople First => _pages[0];

    /// <summary>Every loaded account once, in page order.</summary>
    public IReadOnlyList<AdminInsightsPersonAccount> Accounts => _accounts ??= AdminInsightsRules.MergePeopleAccounts(_pages);

    /// <summary>The offset of the next page, or null when every account is loaded or a page came back empty.</summary>
    public int? NextOffset => AdminInsightsRules.NextPeopleOffset(_pages);

    public bool HasMore => NextOffset != null;

    public bool IsLoadingMore => _loading is { IsCompleted: false };

    /// <summary>The last "Show more accounts" failed; the list is as it was and the button reads again.</summary>
    public Exception? MoreError { get; private set; }

    /// <summary>
    /// Reads the next page and adds it. True when a page was added; false when there is none, one is already being read (the same read is
    /// awaited instead of asking twice) or the read failed (<see cref="MoreError"/> says why).
    /// </summary>
    public Task<bool> LoadMoreAsync(CancellationToken ct = default)
    {
        if (_loading is { IsCompleted: false } running) return running;
        if (NextOffset is not { } offset) return Task.FromResult(false);
        MoreError = null;
        return _loading = ReadAsync(offset, ct);
    }

    private async Task<bool> ReadAsync(int offset, CancellationToken ct)
    {
        try
        {
            var page = await _fetch(offset, ct);
            _pages.Add(page);
            _accounts = null;
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            MoreError = error;
            return false;
        }
    }
}

/// <summary>The People tab (official <c>PeopleTab.tsx</c>, c59798116) shaped from its pages: notice, KPI cards, table rows and drilldowns.</summary>
public static class AdminInsightsPeoplePresentation
{
    /// <summary>The sortable columns in header order (Account is not sortable).</summary>
    public static IReadOnlyList<AdminInsightsPeopleSort> SortColumns { get; } =
        [AdminInsightsPeopleSort.WatchTime, AdminInsightsPeopleSort.Plays, AdminInsightsPeopleSort.DirectPct, AdminInsightsPeopleSort.CompletionPct, AdminInsightsPeopleSort.LastSeen];

    public static string ColumnLabelKey(AdminInsightsPeopleSort sort) => sort switch
    {
        AdminInsightsPeopleSort.WatchTime => "web.admin.insights.people.column.watchTime",
        AdminInsightsPeopleSort.Plays => "web.admin.insights.people.column.plays",
        AdminInsightsPeopleSort.DirectPct => "web.admin.insights.people.column.directRate",
        AdminInsightsPeopleSort.CompletionPct => "web.admin.insights.people.column.completionRate",
        _ => "web.admin.insights.people.column.lastSeen",
    };

    /// <summary>What a header's icon shows: nothing chosen yet (up and down), or the direction of the active column.</summary>
    public enum SortIndicator { None, Descending, Ascending }

    public static SortIndicator IndicatorFor(AdminInsightsPeopleSort column, AdminInsightsPeopleSort active, bool descending)
        => column != active ? SortIndicator.None : descending ? SortIndicator.Descending : SortIndicator.Ascending;

    /// <summary>The same banner the Playback tab shows: some sessions were recorded before watch time was measured.</summary>
    public static string? Notice(AdminInsightsPeople first)
        => AdminInsightsRules.ShowWatchTimeNotice(first.Coverage)
            ? AdminInsightsText.T("web.admin.insights.coverage.watchTimePartial",
                ("measured", AdminInsightsNumbers.Count(first.Coverage.MeasuredSessions)), ("total", AdminInsightsNumbers.Count(first.Coverage.TotalSessions)),
                ("total_count", first.Coverage.TotalSessions))
            : null;

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsPeople first) =>
    [
        new(AdminInsightsText.T("web.admin.insights.people.kpi.activeAccounts"), AdminInsightsNumbers.Count(first.ActiveAccounts)),
        new(AdminInsightsText.T("web.admin.insights.people.kpi.activeProfiles"), AdminInsightsNumbers.Count(first.ActiveProfiles)),
        new(AdminInsightsText.T("web.admin.insights.people.kpi.totalWatchTime"), DashboardFormat.WatchTime(first.TotalWatchSeconds)),
    ];

    /// <summary>One household profile under an expanded account.</summary>
    public sealed record ProfileRow(string Label, bool IsLive, string WatchTime, string Plays, string DirectRate, string CompletionRate,
        string LastSeen, string LastSeenTitle, string DetailsName, AdminInsightsPersonScope? Person, AdminRouteRequest History);

    /// <summary>One account of the table.</summary>
    public sealed record AccountRow(string UserId, string Username, long? AdminUserId, bool IsLive, string? ProfilesBadge, string? TopClient,
        string WatchTime, string Plays, string DirectRate, string CompletionRate, string LastSeen, string LastSeenTitle,
        string ExpandName, string CollapseName, string DetailsName, AdminInsightsPersonScope? Person, AdminRouteRequest History,
        IReadOnlyList<ProfileRow> Profiles)
    {
        public bool HasProfiles => Profiles.Count > 0;
    }

    private static string Percent(double? rate) => AdminInsightsNumbers.Percent(rate);

    /// <summary>
    /// "5m ago" with the absolute time as its tooltip (official <c>formatRelativeTime(value, {rounding: "floor"}) ?? formatShortTimestamp(value)</c>);
    /// a dash for an account never seen.
    /// </summary>
    public static (string Text, string Title) LastSeen(string? lastSeenAt, DateTimeOffset now)
    {
        var absolute = AdminInsightsRules.FormatShortTimestamp(lastSeenAt);
        if (string.IsNullOrEmpty(lastSeenAt)) return (DashboardFormat.Dash, absolute);
        return (AdminPlaybackAuditText.Relative(lastSeenAt, now) ?? absolute, absolute);
    }

    /// <summary>The rows of the loaded accounts; <paramref name="filters"/> supplies the library every History link keeps.</summary>
    public static IReadOnlyList<AccountRow> Rows(AdminInsightsPeoplePages pages, AdminInsightsFilterState filters, DateTimeOffset now)
    {
        var first = pages.First;
        var library = filters.LibraryId?.ToString(CultureInfo.InvariantCulture);
        AdminRouteRequest History(string userId, string? profileId = null) => AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown
        {
            UserId = userId, ProfileId = profileId, LibraryId = library, StartedAfter = first.From, StartedBefore = first.To,
        });
        return pages.Accounts.Select(account =>
        {
            var (seen, seenTitle) = LastSeen(account.LastSeenAt, now);
            var user = AdminInsightsFilters.ParsePositiveId(account.UserId);
            return new AccountRow(account.UserId, account.Username, user, account.IsLive,
                account.Profiles.Count > 1 ? AdminInsightsText.T("web.admin.insights.people.profilesCount", AdminInsightsText.Count(account.Profiles.Count)) : null,
                string.IsNullOrEmpty(account.TopClient) ? null : account.TopClient,
                DashboardFormat.WatchTime(account.WatchSeconds), AdminInsightsNumbers.Count(account.Plays), Percent(account.DirectRate), Percent(account.CompletionRate),
                seen, seenTitle,
                AdminInsightsText.T("web.admin.insights.people.expandProfiles", ("account", account.Username)),
                AdminInsightsText.T("web.admin.insights.people.collapseProfiles", ("account", account.Username)),
                AdminInsightsText.T("web.admin.insights.people.action.detailsFor", ("name", account.Username)),
                user is { } id ? new AdminInsightsPersonScope(id) : null,
                History(account.UserId),
                account.Profiles.Select(profile =>
                {
                    var (profileSeen, profileSeenTitle) = LastSeen(profile.LastSeenAt, now);
                    var label = string.IsNullOrEmpty(profile.ProfileName) ? AdminInsightsRules.UnnamedProfileLabel(profile.ProfileId) : profile.ProfileName;
                    return new ProfileRow(label, profile.IsLive, DashboardFormat.WatchTime(profile.WatchSeconds), AdminInsightsNumbers.Count(profile.Plays),
                        Percent(profile.Plays > 0 ? (double)profile.DirectPlays / profile.Plays : 0), Percent(profile.CompletionRate), profileSeen, profileSeenTitle,
                        AdminInsightsText.T("web.admin.insights.people.action.detailsFor", ("name", label)),
                        user is { } profileUser ? new AdminInsightsPersonScope(profileUser, profile.ProfileId) : null,
                        History(account.UserId, profile.ProfileId));
                }).ToArray());
        }).ToArray();
    }
}
