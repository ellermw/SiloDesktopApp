using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The Insights rules without a window or server (official Silo c59798116): URL state, range maps, calendar grids, the viewing heatmap,
/// People paging, formatters, the History drilldown and refresh protocol. The cases of official <c>insightsShared.test.ts</c> are ported
/// as named; the rest are the same rules at their edges.
/// </summary>
public sealed class AdminInsightsRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);
    private static readonly CultureInfo En = new("en-US");

    private static IReadOnlyDictionary<string, string> Q(string query) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]), StringComparer.Ordinal);

    private static AdminRouteRequest Request(string query = "") => new(AdminRoute.Insights, query.Length == 0 ? null : Q(query));

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope() => CultureInfo.CurrentCulture = En;
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    // ── URL state (official parseInsightsUrlState) ───────────

    [Fact]
    public void DefaultsToTheLiveActivityTab30DaysAndTheBrowserZone()
    {
        var state = AdminInsightsFilterState.Parse(new Dictionary<string, string>(), "America/New_York", Today);
        Assert.Equal(AdminInsightsTab.Activity, state.Tab);
        Assert.Equal(AdminInsightsRange.Days30, state.Range);
        Assert.Equal("America/New_York", state.Tz);
        Assert.Null(state.LibraryId);
        Assert.Null(state.UserId);
        // The custom dates default to the last 30 calendar days ending today.
        Assert.Equal(("2026-10-28", "2026-11-26"), (state.FromDate, state.ToDate));
        Assert.Equal(state, AdminInsightsFilterState.Parse((IReadOnlyDictionary<string, string>?)null, "America/New_York", Today));
        Assert.Equal(AdminInsightsTab.Activity, AdminInsightsTabs.Default);
        Assert.Equal(AdminInsightsRange.Days30, AdminInsightsRanges.Default);
    }

    [Fact]
    public void ParsesAnExplicitTabCustomRangeZoneLibraryAndAccountAndBuildsTheWindow()
    {
        var state = AdminInsightsFilterState.Parse(Request("tab=devices&range=custom&from=2026-10-01&to=2026-10-15&tz=UTC&library_id=4&user_id=12"), "America/New_York", Today);
        Assert.Equal(new AdminInsightsFilterState(AdminInsightsTab.Devices, AdminInsightsRange.Custom, "2026-10-01", "2026-10-15", "UTC", 4, 12), state);
        Assert.Equal(AdminInsightsApi.DevicesPath(new AdminInsightsWindow { Range = AdminInsightsRange.Custom, Tz = "UTC", FromDate = "2026-10-01", ToDate = "2026-10-15", LibraryId = "4", UserId = "12" }),
            AdminInsightsApi.DevicesPath(state.ToWindow()));
        Assert.Equal("/api/v2/admin/insights/activity?range=custom&tz=UTC&from_date=2026-10-01&to_date=2026-10-15&library_id=4&user_id=12", AdminInsightsApi.ActivityPath(state.ToWindow()));
    }

    [Fact]
    public void InvalidTabRangeAndNonPositiveFilterIdsFallBackToTheirDefaults()
    {
        var state = AdminInsightsFilterState.Parse(Request("tab=bogus&range=999d&library_id=-3&user_id=abc"), "UTC", Today);
        Assert.Equal(AdminInsightsTab.Activity, state.Tab);
        Assert.Equal(AdminInsightsRange.Days30, state.Range);
        Assert.Null(state.LibraryId);
        Assert.Null(state.UserId);
        // Matching is exact: the WebUI's includes() is case-sensitive.
        Assert.Equal(AdminInsightsTab.Activity, AdminInsightsFilterState.Parse(Request("tab=People"), "UTC", Today).Tab);
        Assert.Equal(AdminInsightsRange.Days30, AdminInsightsFilterState.Parse(Request("range=1Y"), "UTC", Today).Range);
        // Blank values are absent: whitespace around a value is trimmed, an all-blank one takes the default.
        var padded = AdminInsightsFilterState.Parse(Request("tz=%20Europe%2FParis%20&from=%20&to=2026-11-02"), "UTC", Today);
        Assert.Equal("Europe/Paris", padded.Tz);
        Assert.Equal("2026-10-28", padded.FromDate);
        Assert.Equal("2026-11-02", padded.ToDate);
    }

    [Theory]
    [InlineData("12", 12L)]
    [InlineData("007", 7L)]
    [InlineData(" 12 ", 12L)]
    [InlineData("1e2", 100L)]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("1.5", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData("9007199254740992", null)]
    [InlineData("9007199254740991", 9007199254740991L)]
    [InlineData("Infinity", null)]
    public void PositiveIdsFollowTheWebUiNumberRule(string raw, long? expected) => Assert.Equal(expected, AdminInsightsFilters.ParsePositiveId(raw));

    [Fact]
    public void TheDetailPanelIsATitleOrAPersonWithAnOptionalProfile()
    {
        Assert.Equal(new AdminInsightsDetailState(), AdminInsightsFilters.ParseDetail(Request("tab=titles")));
        Assert.Equal(new AdminInsightsDetailState("movie-7"), AdminInsightsFilters.ParseDetail(Request("title=%20movie-7%20")));
        var person = AdminInsightsFilters.ParseDetail(Request("person=12&profile=p-a"));
        Assert.Equal(new AdminInsightsPersonScope(12, "p-a"), person.Person);
        Assert.True(person.IsOpen);
        Assert.Null(AdminInsightsFilters.ParseDetail(Request("person=0&profile=p-a")).Person);
        Assert.Null(AdminInsightsFilters.ParseDetail(Request("person=12&profile=")).Person!.ProfileId);
        // A title opened from a person keeps both.
        var both = AdminInsightsFilters.ParseDetail(Request("person=12&title=t1"));
        Assert.Equal(("t1", 12L), (both.TitleId, both.Person!.UserId));
        Assert.False(AdminInsightsFilters.ParseDetail((IReadOnlyDictionary<string, string>?)null).IsOpen);
    }

    // ── URL serialisation: what is omitted, what Reset keeps ──

    [Fact]
    public void TheUrlOmitsEveryDefaultAndRoundTripsEveryNonDefault()
    {
        var defaults = AdminInsightsFilterState.Parse(Request(), "America/New_York", Today);
        var query = defaults.ToQuery("America/New_York");
        Assert.Empty(query.Values);
        Assert.Equal("/admin/insights", query.Href);
        Assert.Null(query.ToRequest().Query);

        var everything = new AdminInsightsFilterState(AdminInsightsTab.People, AdminInsightsRange.Custom, "2026-10-01", "2026-10-15", "UTC", 4, 12);
        var detail = new AdminInsightsDetailState("t1", new AdminInsightsPersonScope(9, "p-a"));
        var full = everything.ToQuery("America/New_York", detail);
        Assert.Equal("/admin/insights?from=2026-10-01&library_id=4&person=9&profile=p-a&range=custom&tab=people&title=t1&to=2026-10-15&tz=UTC&user_id=12", full.Href);
        Assert.Equal(everything, AdminInsightsFilterState.Parse(full.ToRequest(), "America/New_York", Today));
        Assert.Equal(detail, AdminInsightsFilters.ParseDetail(full.ToRequest()));

        // Each default is left out on its own: the live tab, 30d, the browser zone; from and to unless custom.
        Assert.Equal("/admin/insights?range=7d", (defaults with { Range = AdminInsightsRange.Days7, FromDate = "2026-01-01" }).ToQuery("America/New_York").Href);
        Assert.Equal("/admin/insights?tab=bandwidth", (defaults with { Tab = AdminInsightsTab.Bandwidth }).ToQuery("America/New_York").Href);
        Assert.Equal("/admin/insights?tz=UTC", (defaults with { Tz = "UTC" }).ToQuery("America/New_York").Href);
        Assert.Equal("/admin/insights", (defaults with { Tz = "UTC" }).ToQuery("UTC").Href);
        Assert.Equal("/admin/insights?range=all", (defaults with { Range = AdminInsightsRange.All }).ToQuery("America/New_York").Href);
    }

    [Fact]
    public void RoundTripHoldsForEveryTabAndRange()
    {
        foreach (var tab in AdminInsightsTabs.Ordered)
            foreach (var range in AdminInsightsRanges.Ordered)
                foreach (var tz in new[] { "America/New_York", "UTC", "Asia/Tokyo" })
                {
                    var state = AdminInsightsFilterState.Parse(Request(), "America/New_York", Today) with { Tab = tab, Range = range, Tz = tz, LibraryId = 3, UserId = 8 };
                    var parsed = AdminInsightsFilterState.Parse(state.ToQuery("America/New_York").ToRequest(), "America/New_York", Today);
                    Assert.Equal(state.Tab, parsed.Tab);
                    Assert.Equal(state.Range, parsed.Range);
                    Assert.Equal(state.Tz, parsed.Tz);
                    Assert.Equal((state.LibraryId, state.UserId), (parsed.LibraryId, parsed.UserId));
                    Assert.Equal(state, parsed);
                }
    }

    [Fact]
    public void EachControlWritesItsOwnParameterAndKeepsTheRest()
    {
        var start = Request("tab=titles&range=7d&library_id=4&user_id=12&tz=UTC&title=t1&person=9&profile=p-a&other=keep");
        // Tab: the default tab is removed from the URL.
        Assert.Equal("/admin/insights?library_id=4&other=keep&person=9&profile=p-a&range=7d&title=t1&tz=UTC&user_id=12", AdminInsightsFilters.WithTab(start, AdminInsightsTab.Activity).Href);
        Assert.Equal("/admin/insights?tab=people", AdminInsightsFilters.WithTab(Request(), AdminInsightsTab.People).Href);
        // Range: 30d removes range; a preset removes from and to; custom seeds missing dates from today and keeps present ones.
        Assert.Equal("/admin/insights", AdminInsightsFilters.WithRange(Request("range=7d&from=2026-01-01&to=2026-01-02"), AdminInsightsRange.Days30, Today).Href);
        Assert.Equal("/admin/insights?range=90d", AdminInsightsFilters.WithRange(Request("range=custom&from=2026-01-01&to=2026-01-02"), AdminInsightsRange.Days90, Today).Href);
        Assert.Equal("/admin/insights?from=2026-10-28&range=custom&to=2026-11-26", AdminInsightsFilters.WithRange(Request(), AdminInsightsRange.Custom, Today).Href);
        Assert.Equal("/admin/insights?from=2026-01-01&range=custom&to=2026-11-26", AdminInsightsFilters.WithRange(Request("from=2026-01-01"), AdminInsightsRange.Custom, Today).Href);
        // Editing a custom date switches to custom and sets only what is not empty.
        Assert.Equal("/admin/insights?from=2026-02-02&range=custom", AdminInsightsFilters.WithCustomFrom(Request(), "2026-02-02").Href);
        Assert.Equal("/admin/insights?from=2026-01-01&range=custom", AdminInsightsFilters.WithCustomFrom(Request("from=2026-01-01"), "").Href);
        Assert.Equal("/admin/insights?range=custom&to=2026-03-03", AdminInsightsFilters.WithCustomTo(Request("range=7d"), "2026-03-03").Href);
        // Library, account and zone.
        Assert.Equal("/admin/insights?library_id=9", AdminInsightsFilters.WithLibrary(Request(), 9).Href);
        Assert.Equal("/admin/insights", AdminInsightsFilters.WithLibrary(Request("library_id=9"), null).Href);
        Assert.Equal("/admin/insights?user_id=5", AdminInsightsFilters.WithAccount(Request(), 5).Href);
        Assert.Equal("/admin/insights", AdminInsightsFilters.WithAccount(Request("user_id=5"), null).Href);
        Assert.Equal("/admin/insights?tz=UTC", AdminInsightsFilters.WithTimezone(Request(), "UTC", "America/New_York").Href);
        Assert.Equal("/admin/insights", AdminInsightsFilters.WithTimezone(Request("tz=UTC"), "America/New_York", "America/New_York").Href);
        // Every result is still the Insights route.
        Assert.All(new[] { AdminInsightsFilters.WithTab(start, AdminInsightsTab.People), AdminInsightsFilters.CloseDetail(start), AdminInsightsFilters.WithLibrary(start, null) },
            request => Assert.Equal(AdminRoute.Insights, request.Route));
    }

    [Fact]
    public void ClosingAPanelAndResetFollowTheOfficialPage()
    {
        var start = Request("tab=titles&range=7d&title=t1&person=9&profile=p-a&other=keep");
        // Closing a title opened from a person returns to the person; with no person everything closes.
        Assert.Equal("/admin/insights?other=keep&person=9&profile=p-a&range=7d&tab=titles", AdminInsightsFilters.CloseForDetail(start).Href);
        Assert.Equal("/admin/insights?other=keep&range=7d&tab=titles", AdminInsightsFilters.CloseForDetail(Request("tab=titles&range=7d&title=t1&other=keep")).Href);
        Assert.Equal("/admin/insights?other=keep&range=7d&tab=titles", AdminInsightsFilters.CloseDetail(start).Href);
        Assert.Equal("/admin/insights?other=keep&person=9&profile=p-a&range=7d&tab=titles", AdminInsightsFilters.CloseTitle(start).Href);
        Assert.Equal("/admin/insights?person=12&profile=p-b&title=t1", AdminInsightsFilters.WithPerson(AdminInsightsFilters.WithTitle(Request(), "t1"), new AdminInsightsPersonScope(12, "p-b")).Href);
        Assert.Equal("/admin/insights?person=12", AdminInsightsFilters.WithPerson(Request("profile=p-a"), new AdminInsightsPersonScope(12)).Href);
        // Reset keeps only the tab (and the live tab is the default, so a bare URL).
        Assert.Equal("/admin/insights?tab=titles", AdminInsightsFilters.Reset(start, AdminInsightsTab.Titles).Href);
        Assert.Equal("/admin/insights", AdminInsightsFilters.Reset(start, AdminInsightsTab.Activity).Href);
        Assert.Equal(AdminRoute.Insights, AdminInsightsFilters.Reset(start, AdminInsightsTab.Titles).Route);
    }

    [Fact]
    public void ResetShowsOnlyWhenSomethingDiffersAndNeverOnTheLiveTab()
    {
        AdminInsightsFilterState S(string query) => AdminInsightsFilterState.Parse(Request(query), "America/New_York", Today);
        Assert.False(S("tab=titles").HasNonDefaultFilters("America/New_York"));
        Assert.True(S("tab=titles&range=7d").HasNonDefaultFilters("America/New_York"));
        Assert.True(S("tab=titles&tz=UTC").HasNonDefaultFilters("America/New_York"));
        Assert.False(S("tab=titles&tz=UTC").HasNonDefaultFilters("UTC"));
        Assert.True(S("tab=titles&library_id=2").HasNonDefaultFilters("America/New_York"));
        Assert.True(S("tab=people&user_id=2").HasNonDefaultFilters("America/New_York"));
        Assert.False(S("range=7d&library_id=2&user_id=2").HasNonDefaultFilters("America/New_York"));
    }

    [Fact]
    public void ADetailPanelHonoursOnlyTheFiltersTheCurrentTabShows()
    {
        AdminInsightsFilterState S(string tab) => AdminInsightsFilterState.Parse(Request($"tab={tab}&library_id=4&user_id=12"), "UTC", Today);
        (long?, long?) Scope(string tab) => (S(tab).ForDetail().LibraryId, S(tab).ForDetail().UserId);
        Assert.Equal<(long?, long?)>((4, 12), Scope("titles"));
        Assert.Equal<(long?, long?)>((4, null), Scope("people"));
        Assert.Equal<(long?, long?)>((null, null), Scope("bandwidth"));
        Assert.Equal<(long?, long?)>((4, 12), Scope("devices"));
        Assert.Equal<(long?, long?)>((4, 12), Scope("library"));
        Assert.Equal<(long?, long?)>((4, 12), Scope("playback"));
        // Toolbar visibility per tab.
        Assert.False(AdminInsightsTab.Activity.ShowsToolbar());
        Assert.True(AdminInsightsTab.Bandwidth.ShowsToolbar());
        Assert.False(AdminInsightsTab.Bandwidth.ShowsLibraryFilter());
        Assert.False(AdminInsightsTab.Bandwidth.ShowsTimezoneFilter());
        Assert.False(AdminInsightsTab.People.ShowsAccountFilter());
        Assert.True(AdminInsightsTab.People.ShowsLibraryFilter());
        Assert.Equal(["activity", "playback", "titles", "library", "people", "devices", "bandwidth"], AdminInsightsTabs.Ordered.Select(tab => tab.Wire()));
        Assert.Equal(["web.admin.activity.page.title", "web.admin.insights.tabs.activity", "web.admin.insights.tabs.titles", "web.admin.insights.tabs.library",
            "web.admin.insights.tabs.people", "web.admin.insights.tabs.devices", "web.admin.insights.tabs.bandwidth"], AdminInsightsTabs.Ordered.Select(tab => tab.LabelKey()));
    }

    [Fact]
    public void ThePersonScopeReplacesTheAccountFilterAndTheOmissionsDropTheOthers()
    {
        // Official "omits library_id and user_id when callers scope out those filters".
        var state = AdminInsightsFilterState.Parse(Request("range=30d&tz=UTC&library_id=4&user_id=12"), "UTC", Today);
        Assert.Equal(new AdminInsightsWindow { Range = AdminInsightsRange.Days30, Tz = "UTC", LibraryId = "4" }, state.ToWindow(omitUser: true));
        Assert.Equal(new AdminInsightsWindow { Range = AdminInsightsRange.Days30, Tz = "UTC" }, state.ToWindow(omitLibrary: true, omitUser: true));
        var person = state.ToWindow(person: new AdminInsightsPersonScope(77, "p-a"));
        Assert.Equal(("77", "p-a", "4"), (person.UserId, person.ProfileId, person.LibraryId));
        Assert.Equal("77", state.ToWindow(omitUser: true, person: new AdminInsightsPersonScope(77)).UserId);
        Assert.Null(state.ToWindow(person: new AdminInsightsPersonScope(77)).ProfileId);
    }

    [Fact]
    public void HrefWithSetsOrRemovesAndKeepsTheRest()
    {
        Assert.Equal("/admin/insights?range=7d&tab=titles&title=t9", AdminInsightsFilters.HrefWith(Q("tab=titles&range=7d"), new Dictionary<string, string?> { ["title"] = "t9" }));
        Assert.Equal("/admin/insights?tab=titles", AdminInsightsFilters.HrefWith(Q("tab=titles&title=t1"), new Dictionary<string, string?> { ["title"] = null }));
        Assert.Equal("/admin/insights?tab=titles", AdminInsightsFilters.HrefWith(Q("tab=titles&title=t1"), new Dictionary<string, string?> { ["title"] = "" }));
        Assert.Equal("/admin/insights", AdminInsightsFilters.HrefWith(null, new Dictionary<string, string?>()));
    }

    // ── Dashboard ranges and polling ─────────────────────────

    [Fact]
    public void WidgetRangesMapToTheClosestInsightsPreset()
    {
        // Official: hour, day and week are the 7-day view; the longer ranges have their own presets.
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange("hour"));
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange("day"));
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange("week"));
        Assert.Equal(AdminInsightsRange.Days30, AdminInsightsRules.WidgetRangeToInsightsRange("month"));
        Assert.Equal(AdminInsightsRange.Days90, AdminInsightsRules.WidgetRangeToInsightsRange("quarter"));
        Assert.Equal(AdminInsightsRange.Year1, AdminInsightsRules.WidgetRangeToInsightsRange("year"));
        Assert.Equal(AdminInsightsRange.All, AdminInsightsRules.WidgetRangeToInsightsRange("all"));
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange("nonsense"));
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange((string?)null));
        // The dashboard's current enum goes through the same table.
        Assert.Equal(AdminInsightsRange.Days7, AdminInsightsRules.WidgetRangeToInsightsRange(DashboardWidgetRange.Hour));
        Assert.Equal(AdminInsightsRange.Days30, AdminInsightsRules.WidgetRangeToInsightsRange(DashboardWidgetRange.Month));
        // And back: a custom range has no widget range.
        Assert.Equal(["week", "month", "quarter", "year", "all", null], AdminInsightsRanges.Ordered.Select(AdminInsightsRules.InsightsRangeToWidgetRange));
        Assert.Equal(new AdminInsightsWindow { Range = AdminInsightsRange.Year1, Tz = "Asia/Tokyo" }, AdminInsightsRules.DashboardWindow("year", "Asia/Tokyo"));
    }

    [Fact]
    public void ADashboardLinkNamesTheTabAndTheMappedRangeOmittingDefaults()
    {
        Assert.Equal("/admin/insights?range=90d&tab=playback", AdminInsightsRules.InsightsLinkFor(AdminInsightsTab.Playback, "quarter").Href);
        Assert.Equal("/admin/insights?tab=titles", AdminInsightsRules.InsightsLinkFor(AdminInsightsTab.Titles, "month").Href);
        Assert.Equal("/admin/insights?range=7d&tab=people", AdminInsightsRules.InsightsLinkFor(AdminInsightsTab.People, "hour").Href);
        Assert.Equal("/admin/insights?range=all&tab=bandwidth", AdminInsightsRules.InsightsLinkFor(AdminInsightsTab.Bandwidth, "all").Href);
        Assert.Equal("/admin/insights?range=1y", AdminInsightsRules.InsightsLinkFor(AdminInsightsTab.Activity, "year").Href);
    }

    [Fact]
    public void StaleAndPollIntervalsAreTheOfficialOnes()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), AdminInsightsIntervals.TabStale);
        Assert.Equal(TimeSpan.FromMinutes(5), AdminInsightsIntervals.CapabilitiesStale);
        Assert.Equal(TimeSpan.FromSeconds(5), AdminInsightsIntervals.RefreshWindow);
        Assert.Equal(TimeSpan.FromMilliseconds(600), AdminInsightsIntervals.RefreshSpinnerMinVisible);
        Assert.Equal(TimeSpan.FromSeconds(15), AdminInsightsIntervals.HistoryStale);
        Assert.Equal(TimeSpan.FromSeconds(15), AdminInsightsIntervals.HistoryPoll);
        Assert.Equal(TimeSpan.FromSeconds(60), AdminInsightsIntervals.HistoryDetailStale);
        Assert.Equal(TimeSpan.FromSeconds(1), AdminInsightsIntervals.HistoryRefreshSpinnerMinVisible);
        Assert.Equal(TimeSpan.FromSeconds(30), AdminInsightsIntervals.LiveSessionsStale);
        Assert.Equal(TimeSpan.FromMilliseconds(250), AdminInsightsIntervals.SearchDebounce);
        // A dashboard widget over a week or month polls every 5 minutes, everything longer every 15.
        foreach (var short_ in new[] { "week", "month" }) Assert.Equal(TimeSpan.FromMinutes(5), AdminInsightsIntervals.DashboardPoll(short_));
        foreach (var long_ in new[] { "quarter", "year", "all" }) Assert.Equal(TimeSpan.FromMinutes(15), AdminInsightsIntervals.DashboardPoll(long_));
        Assert.Equal(TimeSpan.FromMinutes(5), AdminInsightsIntervals.DashboardPoll(AdminInsightsRange.Days30));
        Assert.Equal(TimeSpan.FromMinutes(15), AdminInsightsIntervals.DashboardPoll(AdminInsightsRange.All));
        Assert.Equal(50, AdminInsightsLimits.PeoplePageSize);
        Assert.Equal(25, AdminInsightsLimits.HistoryDefaultPageSize);
        Assert.Equal([25, 50, 100], AdminInsightsLimits.HistoryPageSizes);
    }

    // ── Refresh protocol ─────────────────────────────────────

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now += span;
    }

    [Fact]
    public void ARefreshMarksFiveSecondsOfReadsAndNothingBefore()
    {
        var time = new ManualTime();
        var gate = new AdminInsightsRefreshGate(time);
        Assert.False(gate.ShouldRefresh);
        gate.Mark();
        Assert.True(gate.ShouldRefresh);
        time.Advance(TimeSpan.FromSeconds(4.9));
        Assert.True(gate.ShouldRefresh);
        time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True(gate.ShouldRefresh); // inclusive, as Date.now() <= until
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(gate.ShouldRefresh);
        gate.Mark();
        Assert.True(gate.ShouldRefresh);
        // The gate drives the request flag.
        Assert.EndsWith("&refresh=true", AdminInsightsApi.ActivityPath(new(), gate.ShouldRefresh));
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.DoesNotContain("refresh", AdminInsightsApi.ActivityPath(new(), gate.ShouldRefresh));
        Assert.False(new AdminInsightsRefreshGate().ShouldRefresh);
    }

    // ── Calendar grids (official buildActivityBucketGrid, buildDailyUtcGrid) ──

    [Fact]
    public void ZeroFillsDailyBucketsInANonUtcZoneAcrossDstWithoutSkippingOrDuplicatingDays()
    {
        // 2026-11-01 00:00 EDT (04:00Z) through 2026-11-04 00:00 EST (05:00Z) spans the Nov 1 DST fallback.
        Assert.Equal(["2026-11-01", "2026-11-02", "2026-11-03"],
            AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T04:00:00Z", "2026-11-04T05:00:00Z", "America/New_York", "day"));
    }

    [Fact]
    public void AlignsWeeklyBucketsToMondayAndEnumeratesMonthsCleanly()
    {
        // 2026-11-04 is a Wednesday, and date_trunc('week') starts on Monday 2026-11-02.
        Assert.Equal(["2026-11-02", "2026-11-09", "2026-11-16"], AdminInsightsRules.BuildActivityBucketGrid("2026-11-04T00:00:00Z", "2026-11-18T00:00:00Z", "UTC", "week"));
        Assert.Equal(["2026-10-01", "2026-11-01", "2026-12-01", "2027-01-01"], AdminInsightsRules.BuildActivityBucketGrid("2026-10-15T00:00:00Z", "2027-01-10T00:00:00Z", "UTC", "month"));
    }

    [Fact]
    public void BucketGridsHandleTheEdges()
    {
        // The window is half-open: a window ending at midnight does not include the next day.
        Assert.Equal(["2026-11-01", "2026-11-02"], AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z", "UTC", "day"));
        Assert.Equal(["2026-11-01"], AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T00:00:00Z", "2026-11-01T00:00:00.001Z", "UTC", "day"));
        Assert.Empty(AdminInsightsRules.BuildActivityBucketGrid("2026-11-05T00:00:00Z", "2026-11-01T00:00:00Z", "UTC", "day"));
        Assert.Empty(AdminInsightsRules.BuildActivityBucketGrid("not a date", "2026-11-01T00:00:00Z", "UTC", "day"));
        Assert.Empty(AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T00:00:00Z", null, "UTC", "day"));
        // An unknown zone reads the dates in UTC, as the WebUI does on a RangeError.
        Assert.Equal(["2026-11-01"], AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", "Not/AZone", "day"));
        // A west-of-UTC zone shifts the first day back.
        Assert.Equal(["2026-10-31", "2026-11-01"], AdminInsightsRules.BuildActivityBucketGrid("2026-11-01T03:00:00Z", "2026-11-02T03:00:00Z", "America/Los_Angeles", "day"));
        // Capped at 4000 buckets.
        Assert.Equal(4000, AdminInsightsRules.BuildActivityBucketGrid("1990-01-01T00:00:00Z", "2026-11-01T00:00:00Z", "UTC", "day").Count);
        Assert.Equal("2026-11-20", AdminInsightsRules.CalendarDateInTz("2026-11-20T10:30:00.000Z", "Asia/Tokyo"));
        Assert.Equal("2026-11-21", AdminInsightsRules.CalendarDateInTz("2026-11-20T16:30:00.000Z", "Asia/Tokyo"));
        Assert.Null(AdminInsightsRules.CalendarDateInTz("nope", "UTC"));
        Assert.Equal(["2026-11-01", "2026-11-02"], AdminInsightsRules.ChartDates("2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z", "UTC", "day", ["x"]));
        // With no usable window the response's own bucket dates are used.
        Assert.Equal(["2026-11-05", "2026-11-06"], AdminInsightsRules.ChartDates("bad", "bad", "UTC", "day", ["2026-11-05", "2026-11-06"]));
    }

    [Fact]
    public void TheDailyUtcGridIsInclusiveAndBounded()
    {
        Assert.Equal(["2026-02-27", "2026-02-28", "2026-03-01"], AdminInsightsRules.BuildDailyUtcGrid("2026-02-27", "2026-03-01"));
        Assert.Equal(["2026-11-01"], AdminInsightsRules.BuildDailyUtcGrid("2026-11-01", "2026-11-01"));
        Assert.Empty(AdminInsightsRules.BuildDailyUtcGrid("2026-11-02", "2026-11-01"));
        Assert.Empty(AdminInsightsRules.BuildDailyUtcGrid("x", "2026-11-01"));
        Assert.Equal(3665, AdminInsightsRules.BuildDailyUtcGrid("2000-01-01", "2026-11-01").Count);
    }

    [Fact]
    public void BucketLabelsStayOnTheirUtcCalendarDayInEveryZone()
    {
        // Official "formats UTC calendar bucket timestamps without shifting a day behind in western timezones".
        var label = AdminInsightsRules.FormatBucketLabel("2026-11-01", "day", En);
        Assert.Equal("Nov 1", label);
        Assert.DoesNotContain("31", label);
        Assert.Contains("2026", AdminInsightsRules.FormatBucketLabel("2026-11-01", "month", En));
        Assert.Equal("Nov 2026", AdminInsightsRules.FormatBucketLabel("2026-11-01", "month", En));
        Assert.Equal("Nov 2", AdminInsightsRules.FormatBucketLabel("2026-11-02", "week", En));
        Assert.Equal("—", AdminInsightsRules.FormatBucketLabel("junk", "day", En));
    }

    [Fact]
    public void WindowDatesAreCalendarDatesInTheWindowsOwnZone()
    {
        // [2026-10-27 00:00 EDT, 2026-11-26 00:00 EST): the last day shown is the 25th, not the 26th the end instant falls on.
        var (from, to) = AdminInsightsRules.FormatWindowDates("2026-10-27T04:00:00Z", "2026-11-26T05:00:00Z", "America/New_York", En);
        Assert.Equal(("Oct 27, 2026", "Nov 25, 2026"), (from, to));
        Assert.Equal(("Nov 1, 2026", "Nov 1, 2026"), AdminInsightsRules.FormatWindowDates("2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", "UTC", En));
        Assert.Equal("Nov 24, 2026", AdminInsightsRules.FormatShortCalendarDate("2026-11-24", En));
        Assert.Equal("—", AdminInsightsRules.FormatShortCalendarDate(null, En));
        Assert.Equal("—", AdminInsightsRules.FormatShortCalendarDate("", En));
        Assert.Equal("not a date", AdminInsightsRules.FormatShortCalendarDate("not a date", En));
        Assert.Equal("—", AdminInsightsRules.FormatShortTimestamp(null, En));
        Assert.Equal("junk", AdminInsightsRules.FormatShortTimestamp("junk", En));
        Assert.NotEqual("—", AdminInsightsRules.FormatShortTimestamp("2026-11-24T12:00:00Z", En));
    }

    // ── Heatmap and weekday / hour totals ────────────────────

    private static AdminInsightsHeatmapCell Cell(long day, long hour, long plays, long watch = 0) => new() { DayOfWeek = day, Hour = hour, Plays = plays, WatchSeconds = watch };

    [Fact]
    public void WeekdayAndHourTotalsSumTheHeatmapMondayFirst()
    {
        AdminInsightsHeatmapCell[] cells = [Cell(0, 9, 3, 100), Cell(0, 21, 2, 50), Cell(6, 21, 5, 400), Cell(7, 1, 99, 99), Cell(2, 24, 99, 99), Cell(-1, 3, 99, 99)];
        // Only the day is range-checked here: the cell at hour 24 still counts for its weekday, the cells on days 7 and -1 do not.
        Assert.Equal([5L, 0, 99, 0, 0, 0, 5], AdminInsightsRules.WeekdayTotals(cells, AdminInsightsRules.ViewingMetric.Plays));
        Assert.Equal([150L, 0, 99, 0, 0, 0, 400], AdminInsightsRules.WeekdayTotals(cells, AdminInsightsRules.ViewingMetric.WatchTime));
        var hours = AdminInsightsRules.HourTotals(cells, AdminInsightsRules.ViewingMetric.Plays);
        Assert.Equal(24, hours.Length);
        Assert.Equal((3L, 7L, 0L), (hours[9], hours[21], hours[0]));
        // Hours are checked on their own: a cell on a day outside the week still counts in its hour, one at hour 24 does not.
        Assert.Equal((99L, 99L), (hours[1], hours[3]));
        Assert.Equal(3 + 7 + 99 + 99, hours.Sum());
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(25, 100, 1)]
    [InlineData(26, 100, 2)]
    [InlineData(50, 100, 2)]
    [InlineData(51, 100, 3)]
    [InlineData(75, 100, 3)]
    [InlineData(76, 100, 4)]
    [InlineData(100, 100, 4)]
    [InlineData(5, 0, 0)]
    [InlineData(-1, 10, 0)]
    public void HeatmapLevelsAreFiveSteps(long plays, long max, int level) => Assert.Equal(level, AdminInsightsRules.HeatmapLevel(plays, max));

    [Fact]
    public void TheHeatmapGridKeepsTheLastCellOfARepeatAndTheMaxOfEverything()
    {
        var grid = AdminInsightsRules.BuildHeatmapGrid([Cell(1, 5, 4, 40), Cell(1, 5, 8, 80), Cell(3, 23, 2, 20), Cell(9, 9, 100, 1)]);
        Assert.Equal((8L, 80L), (grid.Plays(1, 5), grid.WatchSeconds(1, 5)));
        Assert.Equal(0, grid.Plays(0, 0));
        // The busiest cell counts even when it is outside the grid, as the official page's max does.
        Assert.Equal(100, grid.MaxPlays);
        Assert.Equal(1, grid.Level(1, 5)); // 8 of 100 is within the first quarter
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Plays(7, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Plays(0, 24));
        var plain = AdminInsightsRules.BuildHeatmapGrid([Cell(1, 5, 8), Cell(2, 6, 4), Cell(2, 7, 1)]);
        Assert.Equal((4, 2, 1), (plain.Level(1, 5), plain.Level(2, 6), plain.Level(2, 7)));
    }

    [Fact]
    public void WeekdayAndHourLabelsAreMondayFirstAndOnTheCulturesClock()
    {
        Assert.Equal(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"], AdminInsightsRules.WeekdayLabels(En));
        var twelve = AdminInsightsRules.HourLabels(En);
        Assert.Equal((24, "12 AM", "9 AM", "9 PM"), (twelve.Count, twelve[0], twelve[9], twelve[21]));
        var twentyFour = AdminInsightsRules.HourLabels(new CultureInfo("de-DE"));
        Assert.Equal(("00", "21"), (twentyFour[0], twentyFour[21]));
    }

    // ── People paging ────────────────────────────────────────

    private static AdminInsightsPeople PeoplePage(long total, params string[] users) =>
        new() { TotalAccounts = total, Accounts = users.Select(id => new AdminInsightsPersonAccount { UserId = id, Username = "u" + id }).ToList() };

    [Fact]
    public void PeoplePagesContinueByTheLoadedCountUntilEveryAccountIsLoaded()
    {
        Assert.Equal(0, AdminInsightsRules.NextPeopleOffset([]));
        var first = PeoplePage(5, "1", "2", "3");
        Assert.Equal(3, AdminInsightsRules.NextPeopleOffset([first]));
        var second = PeoplePage(5, "4", "5");
        Assert.Null(AdminInsightsRules.NextPeopleOffset([first, second]));
        // An empty page ends the walk even when the total says there is more.
        Assert.Null(AdminInsightsRules.NextPeopleOffset([first, PeoplePage(99)]));
        // A repeated account (rows moved between reads) is listed once, keeping its first row.
        var merged = AdminInsightsRules.MergePeopleAccounts([PeoplePage(5, "1", "2", "3"), PeoplePage(5, "3", "4")]);
        Assert.Equal(["1", "2", "3", "4"], merged.Select(account => account.UserId));
        // The server stops at offset 10000.
        var huge = new AdminInsightsPeople { TotalAccounts = 50_000, Accounts = Enumerable.Range(0, 10_001).Select(i => new AdminInsightsPersonAccount { UserId = i.ToString() }).ToList() };
        Assert.Null(AdminInsightsRules.NextPeopleOffset([huge]));
    }

    [Fact]
    public void ClickingAPeopleHeaderFlipsTheActiveColumnAndStartsANewOneDescending()
    {
        Assert.Equal((AdminInsightsPeopleSort.WatchTime, false), AdminInsightsRules.NextPeopleSort(AdminInsightsPeopleSort.WatchTime, true, AdminInsightsPeopleSort.WatchTime));
        Assert.Equal((AdminInsightsPeopleSort.WatchTime, true), AdminInsightsRules.NextPeopleSort(AdminInsightsPeopleSort.WatchTime, false, AdminInsightsPeopleSort.WatchTime));
        Assert.Equal((AdminInsightsPeopleSort.Plays, true), AdminInsightsRules.NextPeopleSort(AdminInsightsPeopleSort.WatchTime, false, AdminInsightsPeopleSort.Plays));
    }

    // ── Coverage notices ─────────────────────────────────────

    [Fact]
    public void CoverageNoticesAppearOnlyWhenSessionsAreMissingFacts()
    {
        AdminInsightsCoverage C(long total, long measured, long facts) => new() { TotalSessions = total, MeasuredSessions = measured, RouteFactsSessions = facts };
        Assert.False(AdminInsightsRules.ShowWatchTimeNotice(C(0, 0, 0)));
        Assert.False(AdminInsightsRules.ShowWatchTimeNotice(C(10, 10, 0)));
        Assert.True(AdminInsightsRules.ShowWatchTimeNotice(C(10, 9, 0)));
        Assert.Equal(AdminInsightsRules.RouteFactsNoticeKind.Hidden, AdminInsightsRules.RouteFactsNotice(C(0, 0, 0)));
        Assert.Equal(AdminInsightsRules.RouteFactsNoticeKind.None, AdminInsightsRules.RouteFactsNotice(C(10, 10, 0)));
        Assert.Equal(AdminInsightsRules.RouteFactsNoticeKind.Partial, AdminInsightsRules.RouteFactsNotice(C(10, 10, 4)));
        Assert.Equal(AdminInsightsRules.RouteFactsNoticeKind.Hidden, AdminInsightsRules.RouteFactsNotice(C(10, 10, 10)));
        Assert.True(AdminInsightsRules.ShowMetricsStartNotice(new AdminInsightsBandwidth { From = "2026-10-01T00:00:00Z", OldestMetricAt = "2026-10-20T00:00:00Z" }));
        Assert.False(AdminInsightsRules.ShowMetricsStartNotice(new AdminInsightsBandwidth { From = "2026-10-21T00:00:00Z", OldestMetricAt = "2026-10-20T00:00:00Z" }));
        Assert.False(AdminInsightsRules.ShowMetricsStartNotice(new AdminInsightsBandwidth { From = "2026-10-01T00:00:00Z", OldestMetricAt = null }));
    }

    // ── Formatters ───────────────────────────────────────────

    [Fact]
    public void FractionalRatesBelowTenPercentKeepOneDecimalAndClampAtTheBounds()
    {
        // Official "formats fractional rates below 10% with one decimal and clamps bounds".
        Assert.Equal("0", AdminInsightsRules.FormatRatePercentNumber(0, En));
        Assert.Equal("4.2", AdminInsightsRules.FormatRatePercentNumber(0.042, En));
        Assert.Equal("88", AdminInsightsRules.FormatRatePercentNumber(0.876, En));
        Assert.Equal("100", AdminInsightsRules.FormatRatePercentNumber(1.2, En));
        Assert.Equal("0", AdminInsightsRules.FormatRatePercentNumber(null, En));
        Assert.Equal("0", AdminInsightsRules.FormatRatePercentNumber(double.NaN, En));
        Assert.Equal("0", AdminInsightsRules.FormatRatePercentNumber(-0.5, En));
        Assert.Equal("10", AdminInsightsRules.FormatRatePercentNumber(0.1, En));
        Assert.Equal("0.5", AdminInsightsRules.FormatRatePercentNumber(0.005, En));
        Assert.Equal("4,2", AdminInsightsRules.FormatRatePercentNumber(0.042, new CultureInfo("de-DE")));
        Assert.Equal(87.6, AdminInsightsRules.CoverageBarValue(0.876));
        Assert.Equal(0.1, AdminInsightsRules.CoverageBarValue(0.00123));
    }

    [Fact]
    public void CountsReadAsPluralMessagesWithTheNumberInTheLocalesDigits()
    {
        using var _ = new CultureScope();
        Assert.Equal("1 play", AdminInsightsRules.PlayCount(1));
        Assert.Equal("1,234 plays", AdminInsightsRules.PlayCount(1234));
        Assert.Equal("0 plays", AdminInsightsRules.PlayCount(0));
        Assert.Equal("1 session", AdminInsightsRules.SessionCount(1));
        Assert.Equal("3 sessions", AdminInsightsRules.SessionCount(3));
        Assert.Equal("1 viewer", AdminInsightsRules.ViewerCount(1));
        Assert.Equal("2 viewers", AdminInsightsRules.ViewerCount(2));
        Assert.Equal("1 episode", AdminInsightsRules.EpisodeCount(1));
        Assert.Equal("5 episodes", AdminInsightsRules.EpisodeCount(5));
        Assert.Equal("1 title", AdminInsightsRules.TitleCount(1));
        Assert.Equal("12 titles", AdminInsightsRules.TitleCount(12));
    }

    [Fact]
    public void WatchTimeUsesTheDashboardFormat()
    {
        using var _ = new CultureScope();
        Assert.Equal("0m", AdminInsightsRules.WatchTime(0));
        Assert.Equal("0m", AdminInsightsRules.WatchTime(null));
        Assert.Equal("1m", AdminInsightsRules.WatchTime(20));
        Assert.Equal("42m", AdminInsightsRules.WatchTime(2520));
        Assert.Equal("1h", AdminInsightsRules.WatchTime(3600));
        Assert.Equal("12.5h", AdminInsightsRules.WatchTime(45000));
    }

    [Fact]
    public void TitlesShowTheirYearOrTheRemovedTitleLabel()
    {
        Assert.Equal("Arrival (2016)", AdminInsightsRules.FormatTitle("Arrival", 2016));
        Assert.Equal("Arrival", AdminInsightsRules.FormatTitle(" Arrival ", null));
        Assert.Equal("Arrival", AdminInsightsRules.FormatTitle("Arrival", 0));
        Assert.Equal("Removed title", AdminInsightsRules.FormatTitle("  ", null));
        Assert.Equal("Removed title (2001)", AdminInsightsRules.FormatTitle("", 2001));
        Assert.Equal("Profile 12345678", AdminInsightsRules.UnnamedProfileLabel("123456789abc"));
        Assert.Equal("Profile p1", AdminInsightsRules.UnnamedProfileLabel("p1"));
    }

    [Fact]
    public void HardwareAccelerationAndMethodKeysReadAsTheOfficialPageDoes()
    {
        Assert.Equal("Software (CPU)", AdminInsightsRules.HwAccelLabel("none"));
        Assert.Equal("Software (CPU)", AdminInsightsRules.HwAccelLabel("Software"));
        Assert.Equal("Unknown", AdminInsightsRules.HwAccelLabel("unknown"));
        Assert.Equal("Unknown", AdminInsightsRules.HwAccelLabel(""));
        Assert.Equal("Unknown", AdminInsightsRules.HwAccelLabel(null));
        Assert.Equal(["VAAPI", "QSV", "NVENC", "VideoToolbox"], new[] { "vaapi", "QSV", "nvenc", "VideoToolbox" }.Select(AdminInsightsRules.HwAccelLabel));
        Assert.Equal("AMF", AdminInsightsRules.HwAccelLabel("amf"));
        Assert.Equal(["direct", "direct", "remux", "remux", "transcode", "unknown", "unknown"],
            new[] { "direct", "direct_play", "remux", "direct_stream", "transcode", "hls", "" }.Select(AdminInsightsRules.NormalizeMethodKey));
    }

    // ── History drilldown ────────────────────────────────────

    [Fact]
    public void ADrilldownIncludesOnlyTheFacetsThatAreSet()
    {
        Assert.Equal("/admin/history", AdminInsightsRules.BuildHistoryDrilldown(new()).Href);
        Assert.Null(AdminInsightsRules.BuildHistoryDrilldown(new()).Query);
        Assert.Equal("/admin/history?profile_id=p1&user_id=7", AdminInsightsRules.BuildHistoryDrilldown(new() { UserId = "7", ProfileId = "p1" }).Href);
        Assert.Equal("/admin/history?client_name=unknown&device_platform=unknown&play_method=transcode",
            AdminInsightsRules.BuildHistoryDrilldown(new() { PlayMethod = "transcode", ClientName = "unknown", DevicePlatform = "unknown" }).Href);
        Assert.Equal("/admin/history?subtitle_burn_in=false", AdminInsightsRules.BuildHistoryDrilldown(new() { SubtitleBurnIn = false }).Href);
        Assert.Equal("/admin/history?subtitle_burn_in=true", AdminInsightsRules.BuildHistoryDrilldown(new() { SubtitleBurnIn = true }).Href);
        // A zero id is no id, as the official builder tests truthiness.
        Assert.Equal("/admin/history", AdminInsightsRules.BuildHistoryDrilldown(new() { UserId = "0", LibraryId = "0" }).Href);
        Assert.Equal("/admin/history?adaptation_reason=x&library_id=2&media_item_id=m1&series_id=s1&source_video_codec=hevc&started_after=2026-11-01T00%3A00%3A00Z&started_before=2026-11-22T00%3A00%3A00Z&stream_location=remote",
            AdminInsightsRules.BuildHistoryDrilldown(new() { LibraryId = "2", MediaItemId = "m1", SeriesId = "s1", SourceVideoCodec = "hevc", StartedAfter = "2026-11-01T00:00:00Z", StartedBefore = "2026-11-22T00:00:00Z", StreamLocation = "remote", AdaptationReason = "x" }).Href);
        Assert.Equal(AdminRoute.PlaybackHistory, AdminInsightsRules.BuildHistoryDrilldown(new()).Route);
    }

    [Fact]
    public void AllTimeAndPresetWindowsOnlyChangeTheRangeNotTheZone()
    {
        // The widgets read the whole server in the viewer's zone; the link carries the range, the page supplies the zone.
        var window = AdminInsightsRules.DashboardWindow("month", "UTC");
        Assert.Equal("/api/v2/admin/insights/activity?range=30d&tz=UTC", AdminInsightsApi.ActivityPath(window));
        Assert.Equal("/api/v2/admin/insights/people?range=30d&tz=UTC&sort=watch_time&dir=desc&limit=1&offset=0",
            AdminInsightsApi.PeoplePath(window, new AdminInsightsPeopleQuery(Limit: 1)));
        Assert.Equal("/api/v2/admin/insights/titles?range=30d&tz=UTC&sort=plays&limit=8", AdminInsightsApi.TitlesPath(window, AdminInsightsTitleSort.Plays, AdminInsightsLimits.DashboardTitles));
        Assert.NotEmpty(AdminInsightsRules.DetectTimezone());
    }
}
