using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminInsightsPeoplePresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The People tab (official <c>PeopleTab.tsx</c> at c59798116): KPI values, the sort toggle rules, paging 50 at a time, the table's rows
/// (relative Last seen with its absolute tooltip, badges, the top client) and the Details and History drilldowns. Synthetic data only.
/// </summary>
public sealed class AdminInsightsPeopleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    public AdminInsightsPeopleTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    private static AdminInsightsPeople People() => JsonSerializer.Deserialize<AdminInsightsPeople>(AdminInsightsWire.People)!;

    private static AdminInsightsFilterState Filters(params (string, string)[] query)
        => AdminInsightsFilterState.Parse(query.ToDictionary(pair => pair.Item1, pair => pair.Item2), "UTC", new DateOnly(2026, 11, 26));

    private static AdminInsightsPersonAccount Account(string id, string name, int profiles = 0, bool live = false, string? seen = "2026-11-26T11:55:00Z", string? client = "Silo Desktop")
        => new()
        {
            UserId = id, Username = name, IsLive = live, LastSeenAt = seen, TopClient = client, Plays = 20, DirectRate = 0.5, CompletionRate = 0.08, WatchSeconds = 7200,
            Profiles = Enumerable.Range(1, profiles).Select(index => new AdminInsightsPersonProfile
            {
                ProfileId = "profile-" + id + "-" + index, ProfileName = index == 2 ? "" : "Profile " + index, Plays = index == 3 ? 0 : 10, DirectPlays = index == 1 ? 5 : 1,
                CompletionRate = 0.5, WatchSeconds = 3600, IsLive = index == 1, LastSeenAt = index == 1 ? "2026-11-25T11:00:00Z" : null,
            }).ToList(),
        };

    private static AdminInsightsPeople Page(int total, params AdminInsightsPersonAccount[] accounts)
    {
        var page = People();
        page.Accounts = [.. accounts];
        page.TotalAccounts = total;
        return page;
    }

    // ── KPI cards and notice ──────────────────────────────────

    [Fact]
    public void ThreeKpiCardsCarryTheOfficialValues()
    {
        var kpis = P.Kpis(People());
        Assert.Equal(["Active accounts", "Active profiles", "Total watch time"], kpis.Select(kpi => kpi.Label));
        Assert.Equal(["12", "12", DashboardFormat.WatchTime(3600)], kpis.Select(kpi => kpi.Value));
        Assert.All(kpis, kpi => { Assert.Null(kpi.Subtext); Assert.Null(kpi.Accent); });
    }

    [Fact]
    public void TheWatchTimeNoticeShowsOnlyWhenSomeSessionsWereNotMeasured()
    {
        var data = People();
        Assert.Null(P.Notice(data));
        data.Coverage.MeasuredSessions = 4;
        Assert.Equal("Exact watch time is available for 4 of 12 sessions; older sessions are estimated.", P.Notice(data));
        data.Coverage.TotalSessions = 0;
        Assert.Null(P.Notice(data));
    }

    // ── Sorting ───────────────────────────────────────────────

    [Fact]
    public void TheFiveSortableColumnsAreInHeaderOrderAndTheDefaultIsWatchTimeDescending()
    {
        Assert.Equal(["Watch time", "Plays", "Direct play", "Completion", "Last seen"], P.SortColumns.Select(column => AdminInsightsText.T(P.ColumnLabelKey(column))));
        var request = new AdminInsightsPeopleQuery();
        Assert.Equal(AdminInsightsPeopleSort.WatchTime, request.Sort);
        Assert.True(request.Descending);
    }

    [Fact]
    public void ClickingTheActiveColumnFlipsItsDirectionAndANewColumnStartsDescending()
    {
        var state = (Sort: AdminInsightsPeopleSort.WatchTime, Descending: true);
        state = AdminInsightsRules.NextPeopleSort(state.Sort, state.Descending, AdminInsightsPeopleSort.WatchTime);
        Assert.Equal((AdminInsightsPeopleSort.WatchTime, false), state);
        state = AdminInsightsRules.NextPeopleSort(state.Sort, state.Descending, AdminInsightsPeopleSort.WatchTime);
        Assert.Equal((AdminInsightsPeopleSort.WatchTime, true), state);
        // A new column after an ascending one still starts descending.
        state = AdminInsightsRules.NextPeopleSort(state.Sort, false, AdminInsightsPeopleSort.LastSeen);
        Assert.Equal((AdminInsightsPeopleSort.LastSeen, true), state);
    }

    [Fact]
    public void AHeaderShowsUpAndDownUntilItIsTheActiveColumn()
    {
        Assert.Equal(P.SortIndicator.None, P.IndicatorFor(AdminInsightsPeopleSort.Plays, AdminInsightsPeopleSort.WatchTime, true));
        Assert.Equal(P.SortIndicator.Descending, P.IndicatorFor(AdminInsightsPeopleSort.WatchTime, AdminInsightsPeopleSort.WatchTime, true));
        Assert.Equal(P.SortIndicator.Ascending, P.IndicatorFor(AdminInsightsPeopleSort.WatchTime, AdminInsightsPeopleSort.WatchTime, false));
    }

    [Fact]
    public void TheReadAsksForTheSortSearchAndPageTheTableShows()
    {
        var window = Filters(("range", "7d"), ("tz", "UTC"), ("library_id", "3"), ("user_id", "9")).ToWindow();
        // People lists the accounts, so the account filter never reaches it.
        Assert.Equal("/api/v2/admin/insights/people?range=7d&tz=UTC&library_id=3&search=ka%20t&sort=last_seen&dir=asc&limit=50&offset=0",
            AdminInsightsApi.PeoplePath(window, new AdminInsightsPeopleQuery(" ka t ", AdminInsightsPeopleSort.LastSeen, false, AdminInsightsApi.PeoplePageSize, 0)));
        Assert.Equal("/api/v2/admin/insights/people?range=7d&tz=UTC&sort=watch_time&dir=desc&limit=50&offset=100",
            AdminInsightsApi.PeoplePath(Filters(("range", "7d"), ("tz", "UTC")).ToWindow(), new AdminInsightsPeopleQuery(Offset: 100)));
    }

    // ── Paging ────────────────────────────────────────────────

    [Fact]
    public async Task ShowMoreAccountsLoadsTheNextPageFromTheNumberOfRowsLoadedAndStopsAtTheTotal()
    {
        var offsets = new List<int>();
        var pages = new AdminInsightsPeoplePages(Page(3, Account("1", "a"), Account("2", "b")), (offset, _) =>
        {
            offsets.Add(offset);
            return Task.FromResult(Page(3, Account("3", "c")));
        });
        Assert.True(pages.HasMore);
        Assert.Equal(2, pages.NextOffset);
        Assert.True(await pages.LoadMoreAsync());
        Assert.Equal([2], offsets);
        Assert.Equal(["a", "b", "c"], pages.Accounts.Select(account => account.Username));
        Assert.False(pages.HasMore);
        Assert.False(await pages.LoadMoreAsync());
        Assert.Equal([2], offsets);
    }

    [Fact]
    public async Task APageIsAskedForOnceWhileItIsBeingRead()
    {
        var gate = new TaskCompletionSource<AdminInsightsPeople>();
        var asked = 0;
        var pages = new AdminInsightsPeoplePages(Page(9, Account("1", "a")), (_, _) => { asked++; return gate.Task; });
        var first = pages.LoadMoreAsync();
        var second = pages.LoadMoreAsync();
        Assert.True(pages.IsLoadingMore);
        gate.SetResult(Page(9, Account("2", "b")));
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(1, asked);
        Assert.False(pages.IsLoadingMore);
    }

    [Fact]
    public async Task AnEmptyPageEndsPagingEvenWhenTheTotalSaysThereAreMore()
    {
        var pages = new AdminInsightsPeoplePages(Page(50, Account("1", "a")), (_, _) => Task.FromResult(Page(50)));
        Assert.True(await pages.LoadMoreAsync());
        Assert.False(pages.HasMore);
    }

    [Fact]
    public async Task AFailedPageKeepsTheListAndTheButton()
    {
        var pages = new AdminInsightsPeoplePages(Page(9, Account("1", "a")), (_, _) => throw new HttpRequestException("down"));
        Assert.False(await pages.LoadMoreAsync());
        Assert.IsType<HttpRequestException>(pages.MoreError);
        Assert.True(pages.HasMore);
        Assert.Equal(["a"], pages.Accounts.Select(account => account.Username));
    }

    [Fact]
    public async Task AnAccountThatMovedBetweenPagesAppearsOnce()
    {
        var pages = new AdminInsightsPeoplePages(Page(3, Account("1", "a"), Account("2", "b")), (_, _) => Task.FromResult(Page(3, Account("2", "b"), Account("3", "c"))));
        await pages.LoadMoreAsync();
        Assert.Equal(["a", "b", "c"], pages.Accounts.Select(account => account.Username));
    }

    // ── Rows ──────────────────────────────────────────────────

    [Fact]
    public void AnAccountRowCarriesItsFiguresBadgesAndTopClient()
    {
        var pages = new AdminInsightsPeoplePages(Page(1, Account("7", "dad", profiles: 3, live: true)), (_, _) => throw new InvalidOperationException());
        var row = P.Rows(pages, Filters(("tz", "UTC")), Now).Single();
        Assert.Equal("dad", row.Username);
        Assert.Equal(7, row.AdminUserId);
        Assert.True(row.IsLive);
        Assert.Equal("3 profiles", row.ProfilesBadge);
        Assert.Equal("Silo Desktop", row.TopClient);
        Assert.Equal(DashboardFormat.WatchTime(7200), row.WatchTime);
        Assert.Equal(["20", "50%", "8.0%"], [row.Plays, row.DirectRate, row.CompletionRate]);
        Assert.Equal("Show household profiles for dad", row.ExpandName);
        Assert.Equal("Hide household profiles for dad", row.CollapseName);
        Assert.Equal("Show insights for dad", row.DetailsName);
        Assert.True(row.HasProfiles);
    }

    [Fact]
    public void OneProfileHasNoBadgeAndNoProfilesHasNoChevron()
    {
        var pages = new AdminInsightsPeoplePages(Page(2, Account("1", "solo", profiles: 1, client: null), Account("2", "none")), (_, _) => throw new InvalidOperationException());
        var rows = P.Rows(pages, Filters(), Now);
        Assert.Null(rows[0].ProfilesBadge);
        Assert.Null(rows[0].TopClient);
        Assert.True(rows[0].HasProfiles);
        Assert.False(rows[1].HasProfiles);
    }

    [Fact]
    public void LastSeenIsRelativeWithTheAbsoluteTimeAsItsTooltipAndADashWhenNever()
    {
        var (text, title) = P.LastSeen("2026-11-26T11:55:00Z", Now);
        Assert.Equal("5m ago", text);
        Assert.Equal(AdminInsightsRules.FormatShortTimestamp("2026-11-26T11:55:00Z"), title);
        Assert.Equal("2h ago", P.LastSeen("2026-11-26T09:30:00Z", Now).Text);
        Assert.Equal("2d ago", P.LastSeen("2026-11-24T11:00:00Z", Now).Text);
        Assert.Equal("just now", P.LastSeen("2026-11-26T11:59:40Z", Now).Text);
        Assert.Equal("—", P.LastSeen(null, Now).Text);
        // Something that is not an instant is shown as sent.
        Assert.Equal("not a date", P.LastSeen("not a date", Now).Text);
    }

    [Fact]
    public void ProfileRowsLabelUnnamedProfilesAndComputeTheirOwnDirectPlayRate()
    {
        var pages = new AdminInsightsPeoplePages(Page(1, Account("7", "dad", profiles: 3)), (_, _) => throw new InvalidOperationException());
        var profiles = P.Rows(pages, Filters(), Now).Single().Profiles;
        Assert.Equal(["Profile 1", "Profile profile-", "Profile 3"], profiles.Select(profile => profile.Label));
        // 5 of 10 plays direct; a profile with no plays is 0%, not a division by zero.
        Assert.Equal(["50%", "10%", "0%"], profiles.Select(profile => profile.DirectRate));
        Assert.True(profiles[0].IsLive);
        Assert.False(profiles[1].IsLive);
        Assert.Equal(["1d ago", "—", "—"], profiles.Select(profile => profile.LastSeen));
        Assert.Equal("Show insights for Profile 1", profiles[0].DetailsName);
    }

    [Fact]
    public void DetailsOpensThePersonAndHistoryKeepsTheLibraryAndTheReadsWindow()
    {
        var data = Page(1, Account("7", "dad", profiles: 2));
        data.From = "2026-10-27T00:00:00Z";
        data.To = "2026-11-26T00:00:00Z";
        var pages = new AdminInsightsPeoplePages(data, (_, _) => throw new InvalidOperationException());
        var row = P.Rows(pages, Filters(("library_id", "3")), Now).Single();
        Assert.Equal(new AdminInsightsPersonScope(7), row.Person);
        Assert.Equal("/admin/history?library_id=3&started_after=2026-10-27T00%3A00%3A00Z&started_before=2026-11-26T00%3A00%3A00Z&user_id=7", row.History.Href);
        var profile = row.Profiles[0];
        Assert.Equal(new AdminInsightsPersonScope(7, "profile-7-1"), profile.Person);
        Assert.Equal("/admin/history?library_id=3&profile_id=profile-7-1&started_after=2026-10-27T00%3A00%3A00Z&started_before=2026-11-26T00%3A00%3A00Z&user_id=7", profile.History.Href);
        // Without a library the link is only the account and the window.
        Assert.DoesNotContain("library_id", P.Rows(pages, Filters(), Now).Single().History.Href);
    }

    [Fact]
    public void OpeningAPersonClosesATitleButAPersonSurvivesIntoATitle()
    {
        var request = new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string> { ["tab"] = "people", ["title"] = "m1" });
        var opened = AdminInsightsFilters.WithPerson(AdminInsightsFilters.CloseTitle(request), new AdminInsightsPersonScope(7, "p1"));
        Assert.Equal("/admin/insights?person=7&profile=p1&tab=people", opened.Href);
        Assert.Null(AdminInsightsFilters.ParseDetail(opened).TitleId);
        // A title chosen from the person's panel keeps the person; closing it returns to the person.
        var title = AdminInsightsFilters.WithTitle(opened, "m1");
        Assert.Equal(new AdminInsightsPersonScope(7, "p1"), AdminInsightsFilters.ParseDetail(title).Person);
        Assert.Equal(opened, AdminInsightsFilters.CloseForDetail(title));
    }
}
