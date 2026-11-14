using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminDashboardStoreTests : IDisposable
{
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminMonitorApi _api;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeFeed _feed = new();
    private string _capabilities = """{"allowed":true,"state":"available","timeseries":true,"playback_activity":true,"top_activity":true,"downloads_stats":true,"api_clients":true,"log_level_list":true}""";

    public AdminDashboardStoreTests()
    {
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
        _api = new AdminMonitorApi(_kit.Client);
        _kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath)));
    }

    private string Body(string path) => path switch
    {
        "/api/v2/admin/dashboard/capabilities" => _capabilities,
        "/api/v2/admin/stats" => """{"total_users":2,"total_movies":1}""",
        "/api/v2/libraries" or "/api/v2/admin/users" or "/api/v2/admin/nodes" => """{"items":[],"page":{"has_more":false}}""",
        _ => "{}",
    };

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private DashboardDataStore Store()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var store = new DashboardDataStore(_api, _authority.Capture(), _feed, _time);
        store.Start();
        return store;
    }

    private static async Task Settle(DashboardDataStore store, string key)
    {
        for (var i = 0; i < 50 && store.Get(key).IsFetching; i++) await Task.Delay(5);
    }

    private int Count(string pathAndQuery) => _kit.Http.Requests.Count(request => request == "GET " + pathAndQuery);

    [Fact]
    public async Task OnlyMountedWidgetsFetchAndAFreshQueryIsServedFromCache()
    {
        using var store = Store();
        await Settle(store, DashboardDataStore.Capabilities);
        Assert.Equal(["GET /api/v2/admin/dashboard/capabilities"], _kit.Http.Requests);
        var mount = store.Mount(DashboardDataStore.Timeseries(24));
        await Settle(store, DashboardDataStore.Timeseries(24));
        Assert.True(store.Get(DashboardDataStore.Timeseries(24)).IsSuccess);
        using var again = store.Mount(DashboardDataStore.Timeseries(24));
        Assert.Equal(1, Count("/api/v2/admin/stats/timeseries?hours=24"));
        mount.Dispose();
        _time.Advance(TimeSpan.FromSeconds(56));
        using var stale = store.Mount(DashboardDataStore.Timeseries(24));
        await Settle(store, DashboardDataStore.Timeseries(24));
        Assert.Equal(2, Count("/api/v2/admin/stats/timeseries?hours=24"));
    }

    [Fact]
    public async Task UnsupportedCapabilitiesRenderUnavailableWithoutCallingTheEndpoint()
    {
        _capabilities = """{"allowed":true,"state":"available","timeseries":true,"api_clients":false}""";
        using var store = Store();
        using var mount = store.Mount(DashboardDataStore.ApiClients(24));
        await Settle(store, DashboardDataStore.ApiClients(24));
        await Task.Delay(20);
        Assert.IsType<DashboardUnsupportedException>(store.Get(DashboardDataStore.ApiClients(24)).Error);
        Assert.DoesNotContain(_kit.Http.Requests, request => request.Contains("api-clients"));
    }

    [Fact]
    public async Task ManualRefreshReadsStatsWithRefreshTrueThenListsAndHoldsTheSpinnerOneSecond()
    {
        using var store = Store();
        using var widget = store.Mount(DashboardDataStore.Timeseries(1));
        await Settle(store, DashboardDataStore.Timeseries(1));
        _kit.Http.Requests.Clear();
        var refresh = store.RefreshDashboardAsync(manual: true);
        Assert.True(store.ManualRefreshPending);
        await Task.Delay(30);
        Assert.Contains("GET /api/v2/admin/stats?refresh=true", _kit.Http.Requests);
        Assert.Contains("GET /api/v2/libraries", _kit.Http.Requests);
        Assert.Contains("GET /api/v2/admin/users?limit=200", _kit.Http.Requests);
        Assert.Contains("GET /api/v2/admin/stats/timeseries?hours=1", _kit.Http.Requests);
        Assert.Equal(1, _feed.Refreshes);
        Assert.True(store.ManualRefreshPending);
        _time.Advance(TimeSpan.FromMilliseconds(999));
        await Task.Delay(10);
        Assert.True(store.ManualRefreshPending);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);
        Assert.False(store.ManualRefreshPending);
        await refresh;
        Assert.NotNull(store.LastUpdatedAt);
    }

    [Fact]
    public async Task PageLoopAndResourcePollsRunOnlyWhileVisibleAndFocused()
    {
        using var store = Store();
        foreach (var key in new[] { DashboardDataStore.Stats, DashboardDataStore.Libraries, DashboardDataStore.Users, DashboardDataStore.Resources })
            store.Mount(key);
        _feed.Publish(new DashboardActivityFeedSnapshot(true, null, [], true, []));
        await Task.Delay(30);
        store.SetActivity(visible: true, focused: false);
        _kit.Http.Requests.Clear();
        for (var i = 0; i < 70; i++) _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        Assert.DoesNotContain("GET /api/v2/admin/system/resources", _kit.Http.Requests);
        Assert.DoesNotContain("GET /api/v2/admin/stats?refresh=true", _kit.Http.Requests);

        store.SetActivity(visible: true, focused: true);
        await Task.Delay(30);
        Assert.Contains("GET /api/v2/admin/stats?refresh=true", _kit.Http.Requests);
        _kit.Http.Requests.Clear();
        for (var i = 0; i < 16; i++) _time.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        Assert.Contains("GET /api/v2/admin/system/resources", _kit.Http.Requests);
    }

    [Fact]
    public async Task SlowAutomaticRefreshCoalescesTicksAndForcedQueryReads()
    {
        using var store = Store();
        foreach (var key in new[] { DashboardDataStore.Stats, DashboardDataStore.Libraries, DashboardDataStore.Users })
            store.Mount(key);
        _feed.Publish(new(true, null, [], true, []));
        await Settle(store, DashboardDataStore.Stats);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(store.LastUpdatedAt);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kit.Http.Respond = async (request, _) =>
        {
            if (request.RequestUri!.PathAndQuery == "/api/v2/admin/stats?refresh=true") await gate.Task;
            return AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath));
        };
        _kit.Http.Requests.Clear();
        _time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(20);
        _time.Advance(TimeSpan.FromSeconds(10));
        var joined = store.FetchAsync(DashboardDataStore.Stats, force: true, refreshStats: true);
        Assert.Equal(1, Count("/api/v2/admin/stats?refresh=true"));
        gate.SetResult();
        await joined;
        await Settle(store, DashboardDataStore.Users);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Count("/api/v2/admin/stats?refresh=true"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryActionsAllowIndependentLibrariesAndCoalesceSameLibrary(bool cancel)
    {
        using var store = Store();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kit.Http.Respond = async (_, _) => { await gate.Task; return new HttpResponseMessage(HttpStatusCode.NoContent); };
        _kit.Http.Requests.Clear();
        Task Action(string id) => cancel ? store.CancelLibraryScansAsync(id) : store.StartLibraryScanAsync(id);
        var first = Action("12");
        var duplicate = Action("12");
        var second = Action("13");
        Assert.Equal(2, _kit.Http.Requests.Count);
        gate.SetResult();
        await Task.WhenAll(first, duplicate, second);
        await Action("12");
        Assert.Equal(3, _kit.Http.Requests.Count);
    }

    [Fact]
    public async Task SharedFeedPublishesSessionsAndScansAndCoalescesStatsRefresh()
    {
        using var store = Store();
        Assert.Equal(1, _feed.Acquired);
        using var sessions = store.Mount(DashboardDataStore.Sessions);
        using var stats = store.Mount(DashboardDataStore.Stats);
        Assert.Equal(0, _feed.Refreshes);
        _feed.Publish(new(true, null, [DashboardFixtures.Session("a")], true, [DashboardFixtures.Scan("s1", "7")]));
        Assert.Single(store.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()!);
        Assert.Equal(["s1"], store.ActiveScans.Select(scan => scan.Id));
        await Settle(store, DashboardDataStore.Stats);
        _kit.Http.Requests.Clear();
        _feed.Publish(new(true, null, [DashboardFixtures.Session("a"), DashboardFixtures.Session("b", "transcode")], true, [DashboardFixtures.Scan("s1", "7")]));
        _feed.Publish(new(true, null, [DashboardFixtures.Session("b", "transcode")], true, [DashboardFixtures.Scan("s1", "7")]));
        await Task.Delay(30);
        Assert.Equal(1, Count("/api/v2/admin/stats?refresh=false"));
        _time.Advance(DashboardDataStore.RealtimeWindow);
        await Task.Delay(30);
        Assert.Equal(2, Count("/api/v2/admin/stats?refresh=false"));

        using var libraries = store.Mount(DashboardDataStore.Libraries);
        await Settle(store, DashboardDataStore.Libraries);
        _kit.Http.Requests.Clear();
        _feed.Publish(new(true, null, [], true, []));
        await Task.Delay(30);
        Assert.Empty(store.ActiveScans);
        Assert.Equal(1, Count("/api/v2/libraries"));
    }

    [Fact]
    public async Task RetirementDropsLateResponsesAndStopsEverything()
    {
        var gate = new TaskCompletionSource();
        _kit.Http.Respond = async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/stats/downloads")) await gate.Task;
            return AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath));
        };
        var store = Store();
        var published = new List<string>();
        store.Changed += published.Add;
        using var mount = store.Mount(DashboardDataStore.Downloads);
        await Task.Delay(20);
        _kit.SetRole("user");
        gate.SetResult();
        await Task.Delay(30);
        Assert.False(store.IsCurrent);
        Assert.Null(store.Get(DashboardDataStore.Downloads).Data);
        Assert.Null(store.Get(DashboardDataStore.Downloads).Error);
        Assert.DoesNotContain(DashboardDataStore.Downloads, published);
        _kit.Http.Requests.Clear();
        await store.RefreshDashboardAsync(manual: false);
        store.Dispose();
        Assert.Empty(_kit.Http.Requests);
        Assert.Equal(1, _feed.Released);
    }

    [Fact]
    public async Task ActionsReportOutcomesOnlyUnderTheCapturingLease()
    {
        _kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? AdminMonitorKit.Json("""{"title":"Conflict","status":409,"detail":"Library is busy"}""", HttpStatusCode.Conflict)
            : AdminMonitorKit.Json(Body(request.RequestUri!.AbsolutePath)));
        using var store = Store();
        var notices = new List<(DashboardNoticeKind, string)>();
        store.Notice += (kind, message) => notices.Add((kind, message));
        await store.StartLibraryScanAsync("12");
        Assert.Equal((DashboardNoticeKind.Error, "Library is busy"), notices.Single());
        Assert.Null(store.ScanStartingLibraryId);
        _kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.True(await store.RevokeLoginSessionAsync("7", "s1"));
        _kit.SetRole("user");
        Assert.False(await store.RevokeLoginSessionAsync("7", "s1"));
        Assert.Single(notices);
    }

    [Fact]
    public async Task AttentionHidesOnlyDismissibleItemsAndRestoresThroughTheGuardedSetting()
    {
        var hidden = "[\"email\",\"transcoding\"]";
        _kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("\"S1\"", request.Headers.IfMatch.Single().ToString());
                hidden = "";
                var put = AdminMonitorKit.Json("""{"key":"ui.dashboard.hidden_attention","value":""}""");
                put.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"S2\"");
                return Task.FromResult(put);
            }
            if (path == "/api/v2/admin/settings/effective")
            {
                var response = AdminMonitorKit.Json("{\"playback.transcode_enabled\":\"false\",\"ui.dashboard.hidden_attention\":" + System.Text.Json.JsonSerializer.Serialize(hidden) + "}");
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(hidden == "" ? "\"S2\"" : "\"S1\"");
                return Task.FromResult(response);
            }
            if (path == "/api/v2/theme/branding") return Task.FromResult(AdminMonitorKit.Json("""{"storage_available":true}"""));
            if (path == "/api/v2/admin/server/status") return Task.FromResult(AdminMonitorKit.Json("""{"restart_required":false}"""));
            if (path == "/api/v2/admin/settings/sensitive-status") return Task.FromResult(AdminMonitorKit.Json("""{"configured":[]}"""));
            return Task.FromResult(AdminMonitorKit.Json(Body(path)));
        };
        using var store = Store();
        foreach (var mount in store.MountAttention()) { }
        await Task.Delay(50);
        var attention = store.Attention();
        Assert.True(attention.Loaded);
        Assert.Equal(["public-url"], attention.Visible.Select(i => i.Id));
        Assert.Equal(2, attention.HiddenCount);
        store.RestoreAllAttention();
        await Task.Delay(50);
        Assert.Contains("PUT /api/v2/admin/settings/ui.dashboard.hidden_attention", _kit.Http.Requests);
        Assert.Equal(0, store.Attention().HiddenCount);
        Assert.Equal(["transcoding", "public-url", "email"], store.Attention().Visible.Select(i => i.Id));
    }

    [Fact]
    public void SettingsHealthMatchesWebUiStatesAndOnlyOffItemsAreDismissible()
    {
        var items = DashboardPresentation.BuildSettingsHealth(new Dictionary<string, string>
        {
            ["s3.public_bucket"] = "art", ["playback.transcode_enabled"] = "true", ["email.enabled"] = "true",
            ["email.smtp_host"] = "smtp.test", ["email.from_address"] = "a@test", ["server.public_url"] = "https://silo.test",
        }, [], storageAvailable: false, new DashboardServerStatus { RestartRequired = true, RestartRequiredReasons = ["setting:s3.public_bucket"] }, null);
        var storage = items.Single(i => i.Id == "storage");
        Assert.Equal(("warn", "Restart pending", "Fix"), (storage.State, storage.StateText, storage.ActionLabel));
        Assert.False(DashboardPresentation.IsAttentionDismissible(storage));
        Assert.Equal("ok", items.Single(i => i.Id == "transcoding").State);
        Assert.Null(items.Single(i => i.Id == "database").ActionLabel);
        Assert.Equal("SMTP · smtp.test", items.Single(i => i.Id == "email").Detail);
        Assert.Equal(["a", "b"], DashboardPresentation.ParseHiddenAttention("[\"a\",1,\"b\"]"));
        Assert.Empty(DashboardPresentation.ParseHiddenAttention("{bad"));
        Assert.Equal("", DashboardPresentation.EncodeHiddenAttention([]));
    }
}

public sealed class AdminDashboardPresentationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MethodClassificationKeepsAudioOnlyTranscodesOutOfTheTranscodeShare()
    {
        Assert.Equal("transcode", DashboardPresentation.ClassifyMethod(new DashboardSession { VideoDecision = "transcode" }));
        Assert.Equal("direct_stream", DashboardPresentation.ClassifyMethod(new DashboardSession { VideoDecision = "copy", AudioDecision = "transcode" }));
        Assert.Equal("direct", DashboardPresentation.ClassifyMethod(new DashboardSession { PlayMethod = "direct" }));
        Assert.Equal("remux", DashboardPresentation.ClassifyMethod(new DashboardSession { PlayMethod = "remux" }));
        Assert.Equal("unknown", DashboardPresentation.ClassifyMethod(new DashboardSession { TranscodeAudio = true }));
        Assert.Equal("remux", DashboardPresentation.ClassifyMethod(new DashboardSession { EffectivePlayMethod = "remux", VideoDecision = "transcode" }));
        Assert.Equal("Direct Stream", DashboardPresentation.MethodMeta("direct_stream").Label);
    }

    [Fact]
    public void RouteNodesFollowTheWebUiPresentation()
    {
        Assert.Equal(["legacy:local"], DashboardPresentation.RouteNodes(new DashboardSession()).Select(n => n.Key));
        var nodes = DashboardPresentation.RouteNodes(new DashboardSession
        {
            RoutingExecution = "transcode", RoutingExecutionNodeId = "4", RoutingExecutionNodeName = "gpu-a",
            RoutingEgress = "proxy", RoutingEgressNodeId = "9",
        });
        Assert.Equal([("transcode:4", "gpu-a"), ("proxy:9", "Node #9")], nodes.Select(n => (n.Key, n.Name)));
        Assert.Equal(["server:local"], DashboardPresentation.RouteNodes(new DashboardSession { RoutingEgress = "api", ReportingNode = "local" }).Select(n => n.Key));
    }

    [Fact]
    public void ResourceSamplesNeverInventZerosAndFlagStaleSamples()
    {
        var unavailable = DashboardPresentation.DescribeResourceSample(new DashboardSystemResources { Available = false }, Now);
        Assert.False(unavailable.Available);
        var sample = DashboardPresentation.DescribeResourceSample(new DashboardSystemResources
        {
            Available = true, SampledAt = Now.AddSeconds(-30),
            System = new DashboardSystemStats { CpuPct = 41.6, Cores = 8, MemUsedMb = 1024, MemTotalMb = 4096 },
            Gpu = [new DashboardGpuStats { Device = "gpu0", Source = "nvml", VideoBusyPct = 12, Sessions = 2 }],
            Attribution = new DashboardResourceAttribution { InstanceId = "abcdef0123456789", SampleIntervalSeconds = 5, Process = new DashboardProcessStats { ResidentBytes = 1_048_576 } },
        }, Now);
        Assert.Equal(("42%", 42), (sample.Cpu!.Value, sample.Cpu.Fill));
        Assert.Equal(("25%", "1.0 GiB of 4.0 GiB"), (sample.Memory!.Value, sample.Memory.Detail));
        Assert.Equal("12%", sample.Gpu!.Value);
        Assert.True(sample.Stale);
        Assert.Equal("1.0 MiB", sample.ProcessMemory!.Value);
        Assert.Equal("—", sample.HeapMemory!.Value);
        var noGpu = DashboardPresentation.DescribeResourceSample(new DashboardSystemResources { Available = true, System = new() }, Now);
        Assert.Null(noGpu.Gpu);
        Assert.True(noGpu.Cpu!.Muted);
        Assert.Null(noGpu.Cpu.Fill);
    }

    [Fact]
    public void NodesGroupWorstFirstAndCountOnlyCurrentNodes()
    {
        DashboardNode Node(string id, string group, bool enabled = true, bool healthy = true, long jobs = 0, long? max = null, string type = "transcode")
            => new() { Id = id, Name = "node-" + id, Group = group, Enabled = enabled, Healthy = healthy, ActiveJobs = jobs, MaxJobs = max, Type = type, EgressKbps = 1_000 };
        var nodes = new[] { Node("1", "prod-east-a"), Node("2", "prod-west-b", healthy: false), Node("3", "", enabled: false, jobs: 9), Node("4", "prod-east-a", jobs: 2, max: 2, type: "proxy") };
        var groups = DashboardPresentation.NodeGroups(nodes, null, Now);
        Assert.Equal(["prod-west-b", "prod-east-a", ""], groups.Select(g => g.Name));
        Assert.Equal(["4", "1"], groups[1].Nodes.Select(n => n.Id));
        Assert.Equal(DashboardNodeState.Unhealthy, groups[0].State);
        Assert.Equal(["prod-east-a", "prod-west-b"], DashboardPresentation.GroupLabels(["prod-east-a", "prod-west-b"]).Values.Order());
        Assert.Equal(["cluster-east", "cluster-west"], DashboardPresentation.GroupLabels(["prod-cluster-east", "prod-cluster-west"]).Values.Order());
        Assert.Equal("2 groups · 2/3 healthy · 2 streams · 0 transcodes · 2.0 Mbps", DashboardPresentation.FleetSummary(nodes));
        Assert.True(DashboardPresentation.NodeJobs(nodes[3]).Warning);
    }

    [Fact]
    public void ScansGroupByLibraryWithRunningFirstAndProgressText()
    {
        var groups = DashboardPresentation.GroupActiveScans([
            DashboardFixtures.Scan("a", "1", "accepted"), DashboardFixtures.Scan("b", "1", "running"),
            DashboardFixtures.Scan("c", "2", "completed"),
        ]);
        var group = Assert.Single(groups);
        Assert.Equal(("b", 2), (group.Primary.Id, group.Count));
        Assert.Equal("Scanning: Processing files · 5 / 10 (50%) + 1 more", DashboardPresentation.LibraryScanProgress(group.Primary, group.Count));
        var scan = DashboardPresentation.ParseActiveScan(System.Text.Json.JsonDocument.Parse("""{"id":"x","library_id":12,"status":"running","mode":"file"}""").RootElement);
        Assert.Equal("12", scan!.LibraryId);
        Assert.Equal("Scanning: Single file scan", DashboardPresentation.LibraryScanProgress(scan, 1));
        Assert.Equal("Autoscan", DashboardPresentation.ScanTrigger("autoscan"));
        Assert.Equal("some trigger", DashboardPresentation.ScanTrigger("some_trigger"));
    }

    [Fact]
    public void SharedActivitySessionsMapIntoDashboardRows()
    {
        var mapped = ServerActivityDashboardFeed.MapSession(new AdminActivitySession
        {
            SessionId = "9007199254740993", StartedAt = "2026-11-14T11:59:00Z", SeasonNumber = 2, EpisodeNumber = 3, SeriesName = "S",
            RoutingNetworkProvider = "", EffectivePlayMethod = "transcode",
        });
        Assert.Equal("9007199254740993", mapped.SessionId);
        Assert.Equal(Now.AddMinutes(-1), mapped.StartedAt);
        Assert.True(DashboardPresentation.IsEpisode(mapped));
        Assert.Equal("", mapped.RoutingNetworkProvider);
        Assert.Equal("S2E3", DashboardPresentation.SessionTitle(mapped, "nowPlaying"));
    }
}
