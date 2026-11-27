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
    private const string Legacy = "/api/v2/admin/stats/top-activity";
    private const string LegacyRows = """
        {"days":7,"titles":[{"media_item_id":"m1","title":"Legacy Movie","plays":9,"total_seconds":30000}],
         "profiles":[{"user_id":"7","username":"viewer","profile_id":"p/1","profile_name":"Kids","plays":6,"total_seconds":12000}]}
        """;
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
        // The leaderboards do not read stats/top-activity when Insights works, and no Insights read asks for a refresh on its own.
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
        var key = DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month);
        using var mount = store.Mount(key);
        await Settle(store, key);
        var state = store.Get(key);
        Assert.Null(state.Data);
        Assert.False(state.IsLoading);
        Assert.Equal("insights", Assert.IsType<DashboardUnsupportedException>(state.Error).Capability);
        Assert.Empty(Requests(Legacy));
    }

    [Fact]
    public async Task DefaultLeaderboardsShareTheLegacyWeekWhenInsightsIsUnsupported()
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=7" ? AdminMonitorKit.Json(LegacyRows) : null;
        var layout = DashboardRegistry.DefaultLayout;
        var titles = DashboardDataStore.InsightsTitles(layout.Single(entry => entry.Id == DashboardRegistry.TopTitles).Range!.Value);
        var profiles = DashboardDataStore.InsightsPeople(layout.Single(entry => entry.Id == DashboardRegistry.TopProfiles).Range!.Value);
        using var store = Store();
        using var mounts = new CompositeMount(store.Mount(titles), store.Mount(profiles));
        await Settle(store, titles);
        await Settle(store, profiles);

        var top = Assert.IsType<DashboardTopActivity>(store.Get(titles).Data);
        Assert.Same(top, store.Get(profiles).Data);
        Assert.Null(store.Get(titles).Error);
        Assert.Null(store.Get(profiles).Error);
        Assert.Equal(new DashboardInsightsRow("m1", "Legacy Movie", 9, DashboardFormat.WatchTime(30000), ItemId: "m1"),
            Assert.Single(DashboardInsightsPresentation.LegacyTopTitles(top)));
        Assert.Equal(new DashboardInsightsRow("7:p/1", "Kids · viewer", 6, DashboardFormat.WatchTime(12000), HistoryHref: "/admin/history?user_id=7&profile_id=p%2F1"),
            Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(top)));
        Assert.Equal("GET " + Legacy + "?days=7", Assert.Single(Requests(Legacy)));

        _kit.Http.Requests.Clear();
        await RefreshAsync(store, manual: false);
        Assert.Empty(Requests(Legacy));
        await RefreshAsync(store, manual: true, freshInsights: true);
        await Settle(store, titles);
        await Settle(store, profiles);
        Assert.Equal("GET " + Legacy + "?days=7", Assert.Single(Requests(Legacy)));
    }

    [Theory]
    [InlineData(DashboardWidgetRange.Month)]
    [InlineData(DashboardWidgetRange.Quarter)]
    [InlineData(DashboardWidgetRange.Year)]
    [InlineData(DashboardWidgetRange.All)]
    public async Task LegacyLeaderboardWindowsAreCappedAtThirtyDays(DashboardWidgetRange range)
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotImplemented, "capability_unsupported")
            : path == Legacy + "?days=30" ? AdminMonitorKit.Json(LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal)) : null;
        using var store = Store();
        var titles = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var profiles = DashboardDataStore.InsightsPeople(range);
        using var mounts = new CompositeMount(store.Mount(titles), store.Mount(profiles));
        await Settle(store, titles);
        await Settle(store, profiles);
        Assert.Null(store.Get(titles).Error);
        Assert.Null(store.Get(profiles).Error);
        var top = Assert.IsType<DashboardTopActivity>(store.Get(profiles).Data);
        Assert.Same(top, store.Get(titles).Data);
        Assert.Equal(30L, top.Days);
        Assert.Equal("GET " + Legacy + "?days=30", Assert.Single(Requests(Legacy)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "method_not_allowed")]
    [InlineData(HttpStatusCode.NotImplemented, "not_implemented")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "capability_unsupported")]
    public async Task ADefinitivelyUnsupportedLegacyEndpointPreservesTheUnsupportedInsightsState(HttpStatusCode status, string problem)
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30" ? Problem(status, problem) : null;
        using var store = Store();
        var key = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        using var mount = store.Mount(key);
        await Settle(store, key);
        Assert.Null(store.Get(key).Data);
        Assert.Equal("insights", Assert.IsType<DashboardUnsupportedException>(store.Get(key).Error).Capability);
        Assert.Equal("GET " + Legacy + "?days=30", Assert.Single(Requests(Legacy)));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "validation_failed")]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error")]
    public async Task AFailedLegacyReadDoesNotPublishEmptyData(HttpStatusCode status, string problem)
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotImplemented, "capability_unsupported")
            : path.StartsWith(Legacy, StringComparison.Ordinal) ? Problem(status, problem) : null;
        using var store = Store();
        var key = DashboardDataStore.InsightsPeople(DashboardWidgetRange.Week);
        using var mount = store.Mount(key);
        await Settle(store, key);
        Assert.Null(store.Get(key).Data);
        Assert.Equal((int)status, Assert.IsType<ApiException>(store.Get(key).Error).StatusCode);
        Assert.True(store.Get(key).InsightsUnsupported);
        Assert.Single(Requests(Legacy));
    }

    [Theory]
    [InlineData("titles", "[null]")]
    [InlineData("titles", """[{"plays":5}]""")]
    [InlineData("titles", """[{"media_item_id":null,"title":null,"plays":5}]""")]
    [InlineData("titles", """[{"media_item_id":"  ","title":"  ","plays":5}]""")]
    [InlineData("titles", """[{"title":"Movie","plays":{}}]""")]
    [InlineData("titles", "[false]")]
    [InlineData("profiles", "[null]")]
    [InlineData("profiles", """[{"user_id":null,"profile_id":"p1","plays":5}]""")]
    [InlineData("profiles", """{"user_id":"7","plays":6}""")]
    public async Task MalformedLegacyListsFailOnlyTheirOwnLeaderboard(string malformedList, string badRows)
    {
        var badTitles = malformedList == "titles";
        var goodList = badTitles ? "profiles" : "titles";
        using var rows = JsonDocument.Parse(LegacyRows);
        var goodRows = rows.RootElement.GetProperty(goodList).GetRawText();
        var payload = $$"""{"days":30,"{{malformedList}}":{{badRows}},"{{goodList}}":{{goodRows}} }""";
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30" ? AdminMonitorKit.Json(payload) : null;
        using var store = Store();
        var titles = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var profiles = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        using var mounts = new CompositeMount(store.Mount(titles), store.Mount(profiles));
        await Settle(store, titles);
        await Settle(store, profiles);
        var invalid = store.Get(badTitles ? titles : profiles);
        Assert.Null(invalid.Data);
        Assert.IsType<InvalidDataException>(invalid.Error);
        var valid = store.Get(badTitles ? profiles : titles);
        Assert.Null(valid.Error);
        var top = Assert.IsType<DashboardTopActivity>(valid.Data);
        if (badTitles)
        {
            Assert.Null(top.Titles);
            Assert.Equal("Kids · viewer", Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(top)).Label);
        }
        else
        {
            Assert.Null(top.Profiles);
            Assert.Equal("Legacy Movie", Assert.Single(DashboardInsightsPresentation.LegacyTopTitles(top)).Label);
        }
        Assert.Equal("GET " + Legacy + "?days=30", Assert.Single(Requests(Legacy)));
    }

    [Theory]
    [InlineData("""{"media_item_id":"m1","title":"  "}""", "m1", "m1")]
    [InlineData("""{"media_item_id":"  ","title":"Legacy Movie"}""", "Legacy Movie", null)]
    public async Task LegacyTitleCanUseItsIdentifierOrLabel(string row, string label, string? itemId)
    {
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30" ? AdminMonitorKit.Json($$"""{"days":30,"titles":[{{row}}]}""") : null;
        using var store = Store();
        var titles = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        using var mount = store.Mount(titles);
        await Settle(store, titles);
        Assert.Null(store.Get(titles).Error);
        var top = Assert.IsType<DashboardTopActivity>(store.Get(titles).Data);
        var item = Assert.Single(DashboardInsightsPresentation.LegacyTopTitles(top));
        Assert.Equal((label, itemId), (item.Label, item.ItemId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MonthPollRecoversAndUpdatesTheMountedAllTimeFallbackWithoutPollingIt(bool transientPeopleError)
    {
        string? legacyRows = null;
        var peopleFailed = false;
        _override = path => path.StartsWith(Base + "/people?range=all", StringComparison.Ordinal) && peopleFailed
            ? Problem(HttpStatusCode.InternalServerError, "internal_error")
            : path.StartsWith(Base + "/titles?range=30d", StringComparison.Ordinal) ||
              path.StartsWith(Base + "/people?range=all", StringComparison.Ordinal)
            ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30"
                ? legacyRows == null ? Problem(HttpStatusCode.InternalServerError, "internal_error") : AdminMonitorKit.Json(legacyRows)
                : null;

        using var store = Store();
        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        var native = DashboardDataStore.InsightsPeople(DashboardWidgetRange.Year);
        using var mounts = new CompositeMount(store.Mount(month), store.Mount(all), store.Mount(native));
        foreach (var key in new[] { month, all, native }) await Settle(store, key);
        Assert.IsType<ApiException>(store.Get(month).Error);
        Assert.IsType<ApiException>(store.Get(all).Error);
        var nativeData = Assert.IsType<AdminInsightsPeople>(store.Get(native).Data);

        if (transientPeopleError)
        {
            peopleFailed = true;
            await store.FetchAsync(all, force: true);
            Assert.Null(store.Get(all).Data);
            Assert.Equal(500, Assert.IsType<ApiException>(store.Get(all).Error).StatusCode);
            Assert.True(store.Get(all).InsightsUnsupported);
        }

        var changed = new List<string>();
        store.Changed += changed.Add;
        legacyRows = LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal);
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await Settle(store, month);
        var first = Assert.IsType<DashboardTopActivity>(store.Get(month).Data);
        Assert.Same(first, store.Get(all).Data);
        Assert.Null(store.Get(all).Error);
        Assert.Contains(all, changed);

        changed.Clear();
        legacyRows = legacyRows.Replace("Kids", "Updated profile", StringComparison.Ordinal);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Settle(store, month);
        var latest = Assert.IsType<DashboardTopActivity>(store.Get(month).Data);
        Assert.NotSame(first, latest);
        Assert.Same(latest, store.Get(all).Data);
        Assert.Contains(all, changed);
        Assert.Same(nativeData, store.Get(native).Data);
        Assert.Equal(transientPeopleError ? 2 : 1, Requests("/people?range=all").Count());
    }

    public enum ProbeInterruption { None, Refresh, MalformedSibling }

    [Theory]
    [InlineData(false, false, ProbeInterruption.None)]
    [InlineData(true, false, ProbeInterruption.None)]
    [InlineData(true, true, ProbeInterruption.None)]
    [InlineData(false, false, ProbeInterruption.Refresh)]
    [InlineData(true, true, ProbeInterruption.Refresh)]
    [InlineData(false, false, ProbeInterruption.MalformedSibling)]
    public async Task ASharedPollDuringANativeProbeRecoversMissingOrOldRowsOnlyIfNativeFails(
        bool hadRows, bool nativeSucceeds, ProbeInterruption interruption)
    {
        var legacyRows = LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal);
        var holdPeople = false;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kit.Http.Respond = async (request, _) =>
        {
            var uri = request.RequestUri!;
            var refreshed = uri.Query.Contains("refresh=true", StringComparison.Ordinal);
            var path = uri.AbsolutePath;
            if (path == Base + "/people" && holdPeople)
            {
                if (!refreshed) await release.Task;
                // The superseded probe gives the opposite result, but is no longer authoritative.
                var succeeds = interruption == ProbeInterruption.Refresh && !refreshed ? !nativeSucceeds : nativeSucceeds;
                return succeeds ? AdminMonitorKit.Json(AdminInsightsWire.People)
                    : Problem(HttpStatusCode.InternalServerError, "internal_error");
            }
            if (path == Base + "/titles" && refreshed) return Problem(HttpStatusCode.InternalServerError, "internal_error");
            if (path == Base + "/people" || path == Base + "/titles") return Problem(HttpStatusCode.NotFound, "not_found");
            if (path == Legacy) return hadRows || holdPeople ? AdminMonitorKit.Json(legacyRows)
                : Problem(HttpStatusCode.InternalServerError, "internal_error");
            return AdminMonitorKit.Json(Body(path));
        };

        using var store = Store();
        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        using var mounts = new CompositeMount(store.Mount(month), store.Mount(all));
        await Settle(store, month);
        await Settle(store, all);
        var before = store.Get(all).Data;
        Assert.Equal(hadRows, before != null);
        Assert.True(store.Get(all).InsightsUnsupported);

        holdPeople = true;
        legacyRows = legacyRows.Replace("Kids", "Updated profile", StringComparison.Ordinal);
        var probe = store.FetchAsync(all, force: true);
        DashboardTopActivity latest;
        try
        {
            _time.Advance(TimeSpan.FromMinutes(5));
            await Settle(store, month);
            latest = Assert.IsType<DashboardTopActivity>(store.Get(month).Data);
            Assert.Equal("Updated profile · viewer", Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(latest)).Label);
            Assert.True(store.Get(all).IsFetching);
            Assert.Same(before, store.Get(all).Data);

            if (interruption == ProbeInterruption.Refresh)
            {
                await RefreshAsync(store, manual: true, freshInsights: true);
                await Settle(store, all);
                Assert.Contains(Requests("/people?range=all"), request => request.Contains("refresh=true", StringComparison.Ordinal));
                Assert.False(store.Get(all).IsFetching);
                Assert.False(probe.IsCompleted);
            }
            else if (interruption == ProbeInterruption.MalformedSibling)
            {
                legacyRows = """{"days":30,"titles":[{"title":"Newer title"}],"profiles":[false]}""";
                _time.Advance(TimeSpan.FromSeconds(56));
                var quarter = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Quarter);
                using var switched = store.Mount(quarter);
                await Settle(store, quarter);
                var top = Assert.IsType<DashboardTopActivity>(store.Get(quarter).Data);
                Assert.Equal("Newer title", Assert.Single(DashboardInsightsPresentation.LegacyTopTitles(top)).Label);
                Assert.False(probe.IsCompleted);
            }
        }
        finally
        {
            release.TrySetResult();
            await probe;
        }

        Assert.Null(store.Get(all).Error);
        Assert.Equal(!nativeSucceeds, store.Get(all).InsightsUnsupported);
        if (nativeSucceeds) Assert.IsType<AdminInsightsPeople>(store.Get(all).Data);
        else Assert.Same(latest, store.Get(all).Data);
        Assert.Equal(interruption == ProbeInterruption.Refresh ? 3 : 2, Requests("/people?range=all").Count());
    }

    [Fact]
    public async Task ReturningToAllShowsNewerSharedRowsWithoutARequest()
    {
        var legacyRows = LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal);
        _override = path => path.StartsWith(Base, StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30" ? AdminMonitorKit.Json(legacyRows)
            : path == Legacy + "?days=7" ? AdminMonitorKit.Json(LegacyRows) : null;
        using var store = Store();
        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        var week = DashboardDataStore.InsightsPeople(DashboardWidgetRange.Week);
        using var titles = store.Mount(month);
        using (store.Mount(all))
        {
            await Settle(store, month);
            await Settle(store, all);
            var top = Assert.IsType<DashboardTopActivity>(store.Get(all).Data);
            Assert.Equal("Kids · viewer", Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(top)).Label);
        }

        using (store.Mount(week))
        {
            await Settle(store, week);
            legacyRows = legacyRows.Replace("Kids", "Updated profile", StringComparison.Ordinal);
            _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
            await Settle(store, month);
            await Settle(store, week);
        }

        var requests = _kit.Http.Requests.Count;
        using var remount = store.Mount(all);
        var updated = Assert.IsType<DashboardTopActivity>(store.Get(all).Data);
        Assert.Equal("Updated profile · viewer", Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(updated)).Label);
        Assert.Equal(requests, _kit.Http.Requests.Count);
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
        Assert.Empty(Requests(Legacy));

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

    [Fact]
    public async Task SharedLegacyPollsDoNotPostponeADueNativeProbeOnAWarmVisit()
    {
        var peopleAvailable = false;
        var legacyRows = LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal);
        _override = path => path.StartsWith(Base + "/titles", StringComparison.Ordinal) ||
                            path.StartsWith(Base + "/people", StringComparison.Ordinal) && !peopleAvailable
            ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30" ? AdminMonitorKit.Json(legacyRows) : null;
        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        using (var first = Store(retain: true))
        {
            using var mounts = new CompositeMount(first.Mount(month), first.Mount(all));
            await Settle(first, month);
            await Settle(first, all);
            legacyRows = legacyRows.Replace("Kids", "Updated profile", StringComparison.Ordinal);
            _time.Advance(TimeSpan.FromMinutes(5));
            await Settle(first, month);
            _time.Advance(TimeSpan.FromMinutes(5));
            await Settle(first, month);
            Assert.Single(Requests("/people?range=all"));
        }

        _time.Advance(TimeSpan.FromMinutes(6));
        peopleAvailable = true;
        using var second = Store(retain: true);
        var retained = Assert.IsType<DashboardTopActivity>(second.Get(all).Data);
        Assert.Equal("Updated profile · viewer", Assert.Single(DashboardInsightsPresentation.LegacyTopProfiles(retained)).Label);
        using var mount = second.Mount(all);
        await Settle(second, all);
        Assert.Equal(2, Requests("/people?range=all").Count());
        Assert.IsType<AdminInsightsPeople>(second.Get(all).Data);
        Assert.False(second.Get(all).InsightsUnsupported);
    }

    [Fact]
    public async Task AProbeAbandonedOnDepartureRetriesImmediatelyOnAWarmVisit()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var legacyRows = LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal);
        _kit.Http.Respond = async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == Base + "/people")
            {
                if (++attempts == 1) return Problem(HttpStatusCode.NotFound, "not_found");
                if (attempts == 2)
                {
                    await release.Task;
                    return Problem(HttpStatusCode.InternalServerError, "internal_error");
                }
            }
            return AdminMonitorKit.Json(path == Legacy ? legacyRows : Body(path));
        };

        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        Task? abandoned = null;
        try
        {
            using (var first = Store(retain: true))
            {
                using var mount = first.Mount(all);
                await Settle(first, all);
                Assert.IsType<DashboardTopActivity>(first.Get(all).Data);
                _time.Advance(TimeSpan.FromMinutes(15));
                Assert.Equal(2, attempts);
                Assert.True(first.Get(all).IsFetching);
                abandoned = first.FetchAsync(all);
            }

            _time.Advance(TimeSpan.FromMinutes(1));
            using var second = Store(retain: true);
            Assert.IsType<DashboardTopActivity>(second.Get(all).Data);
            Assert.True(second.Get(all).InsightsUnsupported);
            using var remount = second.Mount(all);
            Assert.Equal(3, attempts);
            await Settle(second, all);
            Assert.IsType<AdminInsightsPeople>(second.Get(all).Data);
            Assert.False(second.Get(all).InsightsUnsupported);
        }
        finally
        {
            release.TrySetResult();
            if (abandoned != null) await abandoned;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWarmVisitUsesSharedRowsForAnEmptyFallbackAndStillRetriesNative(bool probingOnDeparture)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var legacyAvailable = false;
        _kit.Http.Respond = async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == Base + "/people")
            {
                if (++attempts == 1) return Problem(HttpStatusCode.NotFound, "not_found");
                if (attempts == 2 && probingOnDeparture) await release.Task;
                return Problem(HttpStatusCode.InternalServerError, "internal_error");
            }
            if (path == Base + "/titles") return Problem(HttpStatusCode.NotFound, "not_found");
            if (path == Legacy) return legacyAvailable
                ? AdminMonitorKit.Json(LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal))
                : Problem(HttpStatusCode.InternalServerError, "internal_error");
            return AdminMonitorKit.Json(Body(path));
        };

        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        Task? abandoned = null;
        DashboardTopActivity recovered;
        try
        {
            using (var first = Store(retain: true))
            {
                using var titles = first.Mount(month);
                using var people = first.Mount(all);
                await Settle(first, month);
                await Settle(first, all);
                Assert.Equal(500, Assert.IsType<ApiException>(first.Get(all).Error).StatusCode);

                if (probingOnDeparture) abandoned = first.FetchAsync(all, force: true);
                else people.Dispose();
                legacyAvailable = true;
                _time.Advance(TimeSpan.FromMinutes(5));
                await Settle(first, month);
                recovered = Assert.IsType<DashboardTopActivity>(first.Get(month).Data);
                Assert.Null(first.Get(all).Data);
                Assert.Equal(probingOnDeparture, first.Get(all).IsFetching);
            }
        }
        finally
        {
            release.TrySetResult();
            if (abandoned != null) await abandoned;
        }

        _time.Advance(TimeSpan.FromMinutes(1));
        var legacyReads = Requests(Legacy).Count();
        using var second = Store(retain: true);
        using var sibling = second.Mount(month);
        using var remount = second.Mount(all);
        await Settle(second, all);
        Assert.Equal(probingOnDeparture ? 3 : 2, attempts);
        Assert.True(second.Get(all).InsightsUnsupported);
        Assert.Same(recovered, second.Get(all).Data);
        Assert.Equal(legacyReads, Requests(Legacy).Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AWarmVisitRemembersUnsupportedInsightsEvenWhenTheFallbackNeverLoaded(bool leaveWhileLoading)
    {
        var retry = false;
        var peopleStatus = HttpStatusCode.NotFound;
        _override = path => path.StartsWith(Base + "/people", StringComparison.Ordinal) && peopleStatus != HttpStatusCode.OK
            ? Problem(peopleStatus, peopleStatus == HttpStatusCode.NotFound ? "not_found" : "internal_error")
            : path.StartsWith(Base + "/titles", StringComparison.Ordinal) ? Problem(HttpStatusCode.NotFound, "not_found")
            : path == Legacy + "?days=30"
                ? retry ? AdminMonitorKit.Json(LegacyRows.Replace("\"days\":7", "\"days\":30", StringComparison.Ordinal))
                    : Problem(HttpStatusCode.InternalServerError, "internal_error")
                : null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kit.Http.Respond = async (request, _) =>
        {
            var uri = request.RequestUri!;
            if (leaveWhileLoading && !retry && uri.AbsolutePath == Legacy)
            {
                started.TrySetResult();
                await release.Task;
            }
            return _override(uri.PathAndQuery) ?? AdminMonitorKit.Json(Body(uri.AbsolutePath));
        };

        var all = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
        Task? initial = null;
        try
        {
            using var first = Store(retain: true);
            using var mount = first.Mount(all);
            if (leaveWhileLoading)
            {
                initial = first.FetchAsync(all);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(first.Get(all).IsFetching);
            }
            else
            {
                await Settle(first, all);
                Assert.Equal(500, Assert.IsType<ApiException>(first.Get(all).Error).StatusCode);
            }
            Assert.True(first.Get(all).InsightsUnsupported);
            Assert.Null(first.Get(all).Data);
        }
        finally
        {
            release.TrySetResult();
            if (initial != null) await initial;
        }

        _time.Advance(TimeSpan.FromMinutes(1));
        retry = true;
        peopleStatus = HttpStatusCode.InternalServerError;
        using var second = Store(retain: true);
        Assert.True(second.Get(all).InsightsUnsupported);
        using var remount = second.Mount(all);
        await Settle(second, all);
        Assert.Equal(2, Requests("/people?range=all").Count());
        Assert.True(second.Get(all).InsightsUnsupported);
        Assert.Null(second.Get(all).Data);
        Assert.Equal(500, Assert.IsType<ApiException>(second.Get(all).Error).StatusCode);

        var month = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Month);
        using var sibling = second.Mount(month);
        await Settle(second, month);
        Assert.IsType<DashboardTopActivity>(second.Get(all).Data);
        Assert.Null(second.Get(all).Error);
        Assert.True(second.Get(all).InsightsUnsupported);

        peopleStatus = HttpStatusCode.OK;
        await second.FetchAsync(all, force: true);
        Assert.IsType<AdminInsightsPeople>(second.Get(all).Data);
        Assert.False(second.Get(all).InsightsUnsupported);
    }

    private sealed class CompositeMount(params IDisposable[] mounts) : IDisposable
    {
        public void Dispose()
        {
            foreach (var mount in mounts) mount.Dispose();
        }
    }
}
