using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>The dashboard's Insights reads: keys, poll and stale intervals, the shared read, Refresh, retention and unsupported servers.</summary>
public sealed class AdminDashboardInsightsStoreTests : IDisposable
{
    private const string Base = "/api/v2/admin/insights";
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminMonitorApi _api;
    private readonly AdminInsightsApi _insights;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeFeed _feed = new();
    private readonly AdminInsightsRefreshGate _gate;
    private Func<string, HttpResponseMessage?> _override = _ => null;

    public AdminDashboardInsightsStoreTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
        _api = new AdminMonitorApi(_kit.Client);
        _insights = new AdminInsightsApi(_kit.Client);
        _gate = new AdminInsightsRefreshGate(_time);
        _kit.Http.Respond = (request, _) => Task.FromResult(_override(request.RequestUri!.PathAndQuery) ?? AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath)));
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private static string Body(string path) => path switch
    {
        "/api/v2/admin/dashboard/capabilities" => """{"allowed":true,"state":"available","timeseries":true,"playback_activity":true,"downloads_stats":true,"api_clients":true,"log_level_list":true}""",
        "/api/v2/admin/stats" => """{"total_users":2,"total_movies":1}""",
        "/api/v2/libraries" or "/api/v2/admin/users" or "/api/v2/admin/nodes" => """{"items":[],"page":{"has_more":false}}""",
        Base + "/activity" => AdminInsightsWire.Activity,
        Base + "/titles" => AdminInsightsWire.Titles,
        Base + "/library" => AdminInsightsWire.Library,
        Base + "/people" => AdminInsightsWire.People,
        _ => "{}",
    };

    private DashboardDataStore Store(bool retain = false, bool withApi = true)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var store = new DashboardDataStore(_api, _authority.Capture(), _feed, _time, retain, withApi ? _insights : null, _gate, "UTC");
        store.Start();
        return store;
    }

    private IEnumerable<string> Requests(string fragment) => _kit.Http.Requests.Where(request => request.Contains(fragment, StringComparison.Ordinal));

    /// <summary>A manual refresh holds its spinner for a second of the manual clock; this lets the reads go out, then runs the clock out.</summary>
    private async Task RefreshAsync(DashboardDataStore store, bool manual, bool freshInsights = false)
    {
        var refresh = store.RefreshDashboardAsync(manual, freshInsights);
        await Task.Delay(60);
        if (manual) _time.Advance(TimeSpan.FromSeconds(1));
        await refresh;
    }

    private static async Task Settle(DashboardDataStore store, string key)
    {
        for (var i = 0; i < 100 && store.Get(key).IsFetching; i++) await Task.Delay(5);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail = "Problem" }),
                Encoding.UTF8, "application/problem+json"),
        };

    // ── Keys ─────────────────────────────────────────────────

    [Fact]
    public void KeysAreTheMappedInsightsWindowSoWidgetsOnOneWindowShareARead()
    {
        Assert.Equal("insights:activity:7d", DashboardDataStore.InsightsActivity(DashboardWidgetRange.Week));
        Assert.Equal("insights:activity:30d", DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month));
        Assert.Equal("insights:activity:90d", DashboardDataStore.InsightsActivity(DashboardWidgetRange.Quarter));
        Assert.Equal("insights:activity:1y", DashboardDataStore.InsightsActivity(DashboardWidgetRange.Year));
        Assert.Equal("insights:activity:all", DashboardDataStore.InsightsActivity(DashboardWidgetRange.All));
        Assert.Equal("insights:library:1y", DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year));
        Assert.Equal("insights:titles:7d:8", DashboardDataStore.InsightsTitles(DashboardWidgetRange.Week));
        Assert.Equal("insights:people:all:1", DashboardDataStore.InsightsPeople(DashboardWidgetRange.All));
        // A window shorter than a week reads the week, as the official map does.
        Assert.Equal(DashboardDataStore.InsightsActivity(DashboardWidgetRange.Week), DashboardDataStore.InsightsActivity(DashboardWidgetRange.Day));
    }

    [Fact]
    public async Task EveryInsightsReadAsksForTheServerWideWindowInTheConfiguredZoneAndOneRowOfPeople()
    {
        using var store = Store();
        using var mounts = new CompositeMount(
            store.Mount(DashboardDataStore.InsightsActivity(DashboardWidgetRange.Quarter)),
            store.Mount(DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month)),
            store.Mount(DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year)),
            store.Mount(DashboardDataStore.InsightsPeople(DashboardWidgetRange.All)));
        foreach (var key in new[]
        {
            DashboardDataStore.InsightsActivity(DashboardWidgetRange.Quarter), DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month),
            DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year), DashboardDataStore.InsightsPeople(DashboardWidgetRange.All),
        })
            await Settle(store, key);
        Assert.Contains("GET " + Base + "/activity?range=90d&tz=UTC", _kit.Http.Requests);
        Assert.Contains("GET " + Base + "/titles?range=30d&tz=UTC&sort=plays&limit=8", _kit.Http.Requests);
        Assert.Contains("GET " + Base + "/library?range=1y&tz=UTC", _kit.Http.Requests);
        Assert.Contains("GET " + Base + "/people?range=all&tz=UTC&sort=watch_time&dir=desc&limit=1&offset=0", _kit.Http.Requests);
        Assert.NotNull(store.Get(DashboardDataStore.InsightsActivity(DashboardWidgetRange.Quarter)).As<AdminInsightsActivity>());
        Assert.NotNull(store.Get(DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month)).As<AdminInsightsTitles>());
        Assert.NotNull(store.Get(DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year)).As<AdminInsightsLibrary>());
        Assert.NotNull(store.Get(DashboardDataStore.InsightsPeople(DashboardWidgetRange.All)).As<AdminInsightsPeople>());
        // The leaderboards no longer read stats/top-activity, and no Insights read asks for a refresh on its own.
        Assert.Empty(Requests("top-activity"));
        Assert.Empty(Requests("refresh=true"));
    }

    [Fact]
    public async Task SixWidgetsOnOneWindowMakeOneRequest()
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        using var mounts = new CompositeMount(Enumerable.Range(0, 5).Select(_ => store.Mount(key)).ToArray());
        await Settle(store, key);
        Assert.Single(Requests("/activity"));
    }

    // ── Pacing ───────────────────────────────────────────────

    [Theory]
    [InlineData(DashboardWidgetRange.Week, 5)]
    [InlineData(DashboardWidgetRange.Month, 5)]
    [InlineData(DashboardWidgetRange.Quarter, 15)]
    [InlineData(DashboardWidgetRange.Year, 15)]
    [InlineData(DashboardWidgetRange.All, 15)]
    public async Task AMountedReadPollsEveryFiveMinutesForWeekAndMonthAndFifteenForLongerWindows(DashboardWidgetRange range, int minutes)
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(range);
        using var mount = store.Mount(key);
        await Settle(store, key);
        Assert.Single(Requests("/activity"));
        _time.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(2));
        await Settle(store, key);
        Assert.Single(Requests("/activity"));
        _time.Advance(TimeSpan.FromSeconds(3));
        await Settle(store, key);
        Assert.Equal(2, Requests("/activity").Count());
    }

    [Fact]
    public async Task ARemountInsideTheIntervalServesFromTheReadAndOneAfterItReads()
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        using (store.Mount(key)) await Settle(store, key);
        _time.Advance(TimeSpan.FromMinutes(4));
        using (store.Mount(key)) await Settle(store, key);
        Assert.Single(Requests("/activity"));
        _time.Advance(TimeSpan.FromMinutes(2));
        using (store.Mount(key)) await Settle(store, key);
        Assert.Equal(2, Requests("/activity").Count());
    }

    [Fact]
    public async Task TheSixtySecondLoopAndPageReturnNeverTouchInsightsButStillRefreshOtherWidgets()
    {
        using var store = Store();
        var activity = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        var titles = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Week);
        using var mounts = new CompositeMount(store.Mount(activity), store.Mount(titles), store.Mount(DashboardDataStore.Timeseries(24)),
            store.Mount(DashboardDataStore.Stats));
        foreach (var key in new[] { activity, titles, DashboardDataStore.Timeseries(24), DashboardDataStore.Stats }) await Settle(store, key);
        _kit.Http.Requests.Clear();
        await RefreshAsync(store, manual: false);
        await RefreshAsync(store, manual: true);
        Assert.Empty(Requests(Base));
        Assert.NotEmpty(Requests("/timeseries"));
        Assert.False(store.Get(activity).Invalidated);
        Assert.False(store.Get(titles).Invalidated);
    }

    // ── Refresh ──────────────────────────────────────────────

    [Fact]
    public async Task ManualRefreshReadsMountedInsightsWithRefreshTrueAndMarksTheOthersStale()
    {
        using var store = Store();
        var mounted = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        var unmounted = DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Month);
        using (store.Mount(unmounted)) await Settle(store, unmounted);
        using var mount = store.Mount(mounted);
        await Settle(store, mounted);
        _kit.Http.Requests.Clear();

        await RefreshAsync(store, manual: true, freshInsights: true);
        await Settle(store, mounted);
        Assert.Contains("GET " + Base + "/activity?range=30d&tz=UTC&refresh=true", _kit.Http.Requests);
        Assert.Empty(Requests("/library"));
        Assert.True(store.Get(unmounted).Invalidated);

        // Mounting the stale one reads it, still inside the window the click opened, so it asks for fresh data too.
        using var remount = store.Mount(unmounted);
        await Settle(store, unmounted);
        Assert.Contains("GET " + Base + "/library?range=30d&tz=UTC&refresh=true", _kit.Http.Requests);
    }

    [Fact]
    public async Task APollAfterTheRefreshWindowClosesIsAnOrdinaryRead()
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Week);
        using var mount = store.Mount(key);
        await Settle(store, key);
        await RefreshAsync(store, manual: true, freshInsights: true);
        await Settle(store, key);
        _kit.Http.Requests.Clear();
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Settle(store, key);
        // The page's own 60 s loop ran meanwhile; only the Insights request is the subject.
        Assert.Equal(["GET " + Base + "/activity?range=7d&tz=UTC"], Requests(Base).ToArray());
    }

    [Fact]
    public async Task ARefreshWhileAnOrdinaryReadIsOutstandingStillReachesTheServerWithRefreshTrue()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        _kit.Http.Respond = async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == Base + "/activity" && first) { first = false; await release.Task; }
            return AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath));
        };
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        using var mount = store.Mount(key);
        var ordinary = store.FetchAsync(key);
        var refresh = store.RefreshDashboardAsync(manual: true, freshInsights: true);
        release.SetResult();
        await Task.Delay(60);
        _time.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(ordinary, refresh);
        await Settle(store, key);
        Assert.Contains("GET " + Base + "/activity?range=30d&tz=UTC&refresh=true", _kit.Http.Requests);
        Assert.NotNull(store.Get(key).Data);
    }

    // ── Failure ──────────────────────────────────────────────

    [Fact]
    public async Task AServerWithoutTheEndpointsIsUnsupportedAndNeverZero()
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found") : null;
        using var store = Store();
        foreach (var key in new[]
        {
            DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month),
            DashboardDataStore.InsightsTitles(DashboardWidgetRange.Week),
            DashboardDataStore.InsightsPeople(DashboardWidgetRange.Week),
        })
        {
            using var mount = store.Mount(key);
            await Settle(store, key);
            var state = store.Get(key);
            Assert.Null(state.Data);
            Assert.False(state.IsLoading);
            Assert.Equal("insights", Assert.IsType<DashboardUnsupportedException>(state.Error).Capability);
        }
    }

    [Fact]
    public async Task WithoutAnInsightsApiTheReadsAreUnsupportedToo()
    {
        using var store = Store(withApi: false);
        var key = DashboardDataStore.InsightsPeople(DashboardWidgetRange.Week);
        using var mount = store.Mount(key);
        await Settle(store, key);
        Assert.IsType<DashboardUnsupportedException>(store.Get(key).Error);
        Assert.Empty(Requests(Base));
    }

    [Fact]
    public async Task AFailedPollKeepsTheLastDataAndRaisesTheError()
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        using var mount = store.Mount(key);
        await Settle(store, key);
        var before = store.Get(key).Data;
        Assert.NotNull(before);

        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.BadRequest, "validation_failed") : null;
        var changed = new List<string>();
        store.Changed += changed.Add;
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Settle(store, key);
        Assert.Same(before, store.Get(key).Data);
        Assert.NotNull(store.Get(key).Error);
        Assert.Contains(key, changed);

        _override = _ => null;
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Settle(store, key);
        Assert.Null(store.Get(key).Error);
    }

    [Fact]
    public async Task AnEqualPollPublishesNothing()
    {
        using var store = Store();
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Week);
        using var mount = store.Mount(key);
        await Settle(store, key);
        var data = store.Get(key).Data;
        var changed = new List<string>();
        store.Changed += changed.Add;
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Settle(store, key);
        Assert.Equal(2, Requests("/activity").Count());
        Assert.Same(data, store.Get(key).Data);
        Assert.Empty(changed);
    }

    // ── Retention ────────────────────────────────────────────

    [Fact]
    public async Task InsightsReadsAreRetainedAcrossVisitsLikeOtherKeysAndAWarmVisitShowsThemAtOnce()
    {
        var activity = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Quarter);
        var people = DashboardDataStore.InsightsPeople(DashboardWidgetRange.Quarter);
        using (var first = Store(retain: true))
        {
            using var mounts = new CompositeMount(first.Mount(activity), first.Mount(people));
            await Settle(first, activity);
            await Settle(first, people);
        }
        _time.Advance(TimeSpan.FromMinutes(2));
        _kit.Http.Requests.Clear();
        using var second = Store(retain: true);
        Assert.True(second.IsWarmVisit);
        Assert.NotNull(second.Get(activity).As<AdminInsightsActivity>());
        Assert.NotNull(second.Get(people).As<AdminInsightsPeople>());
        // Mounting inside the 15-minute window paints the retained data without a request.
        using var mount = second.Mount(activity);
        await Settle(second, activity);
        Assert.Empty(Requests("/activity"));
        _time.Advance(DashboardQueryCache.UnusedLifetime);
        using var expired = Store(retain: true);
        Assert.False(expired.IsWarmVisit);
        Assert.True(expired.Get(activity).IsLoading);
    }

    private sealed class CompositeMount(params IDisposable[] mounts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var mount in mounts) mount.Dispose();
        }
    }
}
