using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// Now Playing position, episode line and width-driven columns (WebUI #3726,
/// #4144 at upstream 7115ee70), from the v2 wire through the shared activity
/// snapshot to the tile text.
/// </summary>
public sealed class AdminDashboardNowPlayingTests
{
    private static string SessionJson(string position, string duration, string extra = "") =>
        "{\"session_id\":\"s1\",\"user_id\":\"7\",\"username\":\"u\",\"profile_id\":\"p\",\"media_file_id\":\"1\",\"requested_media_file_id\":\"1\"," +
        "\"media_title\":\"T\",\"media_type\":\"episode\",\"play_method\":\"direct\",\"reporting_node\":\"\",\"file_duration\":" + duration + "," +
        "\"started_at\":\"2026-11-14T10:00:00Z\",\"updated_at\":\"2026-11-14T10:00:00Z\",\"position_seconds\":" + position + "," +
        "\"is_paused\":false,\"has_playback_control\":true,\"stream_location\":\"local\",\"audio_track_index\":0,\"transcode_audio\":false," +
        "\"stream_bitrate_kbps\":null,\"target_bitrate_kbps\":null,\"source_bitrate_kbps\":null,\"source_audio_channels\":null" + extra + "}";

    private static async Task<DashboardSession> ReadThroughSharedSnapshot(string row)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminMonitorApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + row + "],\"page\":{\"has_more\":false}}"));
        using var lease = authority.Capture();
        var sessions = await api.ListActivitySessionsAsync(lease);
        var snapshot = ServerActivityDashboardFeed.Map(ServerActivitySnapshot.Empty with { SessionsLoaded = true, Sessions = sessions });
        return Assert.Single(snapshot.Sessions);
    }

    // file_duration is int64 | null and position_seconds a required double (openapi AdminActivitySession);
    // null or a non-positive duration prints the position alone (formatPlaybackPosition).
    [Theory]
    [InlineData("2832.9", "8980", 2832.9, 8980L, "47:12 / 2:29:40")]
    [InlineData("3429", "36000", 3429d, 36000L, "57:09 / 10:00:00")]
    [InlineData("600", "null", 600d, null, "10:00")]
    [InlineData("600", "0", 600d, 0L, "10:00")]
    [InlineData("600", "-5", 600d, -5L, "10:00")]
    [InlineData("0", "7200", 0d, 7200L, "0:00 / 2:00:00")]
    [InlineData("59.999", "60", 59.999, 60L, "0:59 / 1:00")]
    public async Task PositionAndDurationTravelFromTheWireThroughTheSharedSnapshot(
        string positionJson, string durationJson, double position, long? duration, string expected)
    {
        var session = await ReadThroughSharedSnapshot(SessionJson(positionJson, durationJson));
        Assert.Equal(position, session.PositionSeconds);
        Assert.Equal(duration, session.FileDuration);
        Assert.Equal(expected, P.FormatPlaybackPosition(session));
    }

    [Fact]
    public async Task TileAndActivityFormatTheSameSessionIdentically()
    {
        var row = SessionJson("2832", "8980", ",\"series_name\":\"Show\",\"season_number\":3,\"episode_number\":13,\"episode_name\":\"The One\"");
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + row + "],\"page\":{\"has_more\":false}}"));
        using var lease = authority.Capture();
        var activity = Assert.Single(await new AdminMonitorApi(kit.Client).ListActivitySessionsAsync(lease));
        var tile = ServerActivityDashboardFeed.MapSession(activity);

        Assert.Equal("S3E13 · Show", P.GetPlaybackSessionSubtitle(tile));
        Assert.Equal(P.GetPlaybackSessionSubtitle(activity), P.GetPlaybackSessionSubtitle(tile));
        Assert.Equal("47:12 / 2:29:40", P.FormatPlaybackPosition(tile));
        Assert.Equal(P.FormatPlaybackPosition(activity), P.FormatPlaybackPosition(tile));
    }

    [Fact]
    public void DashboardSessionReadsBothMembersByTheirWireNames()
    {
        var present = JsonSerializer.Deserialize<DashboardSession>("""{"session_id":"a","position_seconds":12.5,"file_duration":90}""")!;
        Assert.Equal<(double, long?)>((12.5, 90L), (present.PositionSeconds, present.FileDuration));
        var unknown = JsonSerializer.Deserialize<DashboardSession>("""{"session_id":"a","position_seconds":12.5,"file_duration":null}""")!;
        Assert.Null(unknown.FileDuration);
        Assert.Equal("0:12", P.FormatPlaybackPosition(unknown));
        var zero = JsonSerializer.Deserialize<DashboardSession>("""{"session_id":"a","position_seconds":0,"file_duration":0}""")!;
        Assert.Equal<(double, long?)>((0d, 0L), (zero.PositionSeconds, zero.FileDuration));
        Assert.Equal("0:00", P.FormatPlaybackPosition(zero));
        // An older server row without the members reads as an unknown length at 0:00, never a crash.
        var absent = JsonSerializer.Deserialize<DashboardSession>("""{"session_id":"a"}""")!;
        Assert.Equal<(double, long?)>((0d, null), (absent.PositionSeconds, absent.FileDuration));
        Assert.Equal("0:00", P.FormatPlaybackPosition(absent));
    }

    [Theory]
    [InlineData(double.NaN, 100L, "0:00 / 1:40")]
    [InlineData(double.PositiveInfinity, null, "0:00")]
    [InlineData(double.NegativeInfinity, 0L, "0:00")]
    [InlineData(-30d, 3600L, "0:00 / 1:00:00")]
    [InlineData(3599.99, 3600L, "59:59 / 1:00:00")]
    [InlineData(360000d, 360001L, "100:00:00 / 100:00:01")]
    public void PositionFormatterMatchesFormatClockEdges(double position, long? duration, string expected)
        => Assert.Equal(expected, P.FormatPlaybackPosition(position, duration));

    [Theory]
    [InlineData(3L, 13L, "Show", "S3E13 · Show")]
    [InlineData(0L, 1L, "Show", "S0E1 · Show")]
    [InlineData(10L, 120L, "A · B", "S10E120 · A · B")]
    public void EpisodeLineIsTheSharedActivityRule(long season, long episode, string series, string expected)
    {
        var tile = new DashboardSession { SeriesName = series, SeasonNumber = season, EpisodeNumber = episode, MediaType = "episode" };
        Assert.Equal(expected, P.GetPlaybackSessionSubtitle(tile));
        Assert.Equal(expected, P.FormatEpisodeSubtitle(season, episode, series));
        Assert.DoesNotContain(" — ", P.GetPlaybackSessionSubtitle(tile));
        Assert.DoesNotContain("S" + season + " · E", P.GetPlaybackSessionSubtitle(tile));
    }

    [Fact]
    public void EpisodeLineNestsTheCodeMessageInsideTheSubtitleMessage()
    {
        Assert.True(AdminText.Has("web.detail.episodeCode"));
        Assert.True(AdminText.Has("web.common.activity.subtitle.episode"));
        var asked = new List<string>();
        string Format(string key, object? values)
        {
            asked.Add(key);
            var bag = values!.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(values)?.ToString());
            return key == "web.detail.episodeCode" ? $"<{bag["season"]}|{bag["episode"]}>" : $"[{bag["code"]}/{bag["series"]}]";
        }
        Assert.Equal("[<3|13>/Show]", P.FormatEpisodeSubtitle(3, 13, "Show", Format));
        Assert.Equal(["web.detail.episodeCode", "web.common.activity.subtitle.episode"], asked);
    }

    [Fact]
    public void NonEpisodeSessionsKeepTheSharedMovieAndSeriesFallbacks()
    {
        Assert.Equal("Movie", P.GetPlaybackSessionSubtitle(new DashboardSession { MediaType = "movie" }));
        Assert.Equal("Series", P.GetPlaybackSessionSubtitle(new DashboardSession { MediaType = "series" }));
        Assert.Null(P.GetPlaybackSessionSubtitle(new DashboardSession { MediaType = "audiobook" }));
        // A season number without a series name is not an episode line.
        Assert.Equal("Movie", P.GetPlaybackSessionSubtitle(new DashboardSession { MediaType = "movie", SeasonNumber = 1, EpisodeNumber = 2 }));
        Assert.Equal("S2E1", P.GetPlaybackSessionTitle(AdminActivityPresentationTests.Session(x => { x.SeriesName = "S"; x.SeasonNumber = 2; x.EpisodeNumber = 1; x.EpisodeName = ""; })));
    }

    [Theory]
    [InlineData(0d, 1)]
    [InlineData(671.99, 1)]
    [InlineData(672d, 2)]
    [InlineData(1200d, 2)]
    [InlineData(double.NaN, 1)]
    public void NowPlayingColumnsFollowTheWidgetsOwnWidthAt42Rem(double width, int columns)
        => Assert.Equal(columns, DashboardGridPlacement.NowPlayingColumns(width));

    [Fact]
    public void NowPlayingColumnsIgnoreTheViewportMode()
    {
        // 1440px viewport, span 6 of a ~1100px grid: one column (upstream used two from lg).
        Assert.Equal(DashboardGridMode.Twelve, DashboardGridPlacement.ModeFor(1440));
        Assert.Equal(1, DashboardGridPlacement.NowPlayingColumns(DashboardGridPlacement.ColumnWidth(1100, 12) * 6 + DashboardGridPlacement.Gap * 5));
        // 800px stacked viewport with a 752px widget: two columns (upstream used one below lg).
        Assert.NotEqual(DashboardGridMode.Twelve, DashboardGridPlacement.ModeFor(800));
        Assert.Equal(2, DashboardGridPlacement.NowPlayingColumns(752));
    }
}

/// <summary>
/// The dashboard store republishes the shared snapshot's positions. The session
/// signature (which drives the stats refresh) ignores position, so a read that
/// only moves playback forward must still reach the tile without a stats read.
/// </summary>
public sealed class AdminDashboardSessionPositionStoreTests : IDisposable
{
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeFeed _feed = new();

    public AdminDashboardSessionPositionStoreTests()
    {
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
        _kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/dashboard/capabilities" => """{"allowed":true,"state":"available","timeseries":true,"playback_activity":true,"top_activity":true,"downloads_stats":true,"api_clients":true,"log_level_list":true}""",
            "/api/v2/admin/stats" => """{"total_users":2}""",
            _ => "{}",
        }));
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private static DashboardSession At(double position, long? duration)
    {
        var session = DashboardFixtures.Session("a");
        session.PositionSeconds = position;
        session.FileDuration = duration;
        return session;
    }

    [Fact]
    public async Task APositionOnlyReadRepublishesTheTileWithoutAStatsRefresh()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        using var store = new DashboardDataStore(new AdminMonitorApi(_kit.Client), _authority.Capture(), _feed, _time);
        store.Start();
        using var sessions = store.Mount(DashboardDataStore.Sessions);
        using var stats = store.Mount(DashboardDataStore.Stats);
        _feed.Publish(new(true, null, [At(600, 7200)], true, []));
        for (var i = 0; i < 50 && store.Get(DashboardDataStore.Stats).IsFetching; i++) await Task.Delay(5);
        _kit.Http.Requests.Clear();
        var raised = new List<string>();
        store.Changed += raised.Add;

        _feed.Publish(new(true, null, [At(660, 7200)], true, []));
        var shown = Assert.Single(store.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()!);
        Assert.Equal("11:00 / 2:00:00", P.FormatPlaybackPosition(shown));
        Assert.Contains(DashboardDataStore.Sessions, raised);

        // The duration becoming unknown also reaches the tile.
        _feed.Publish(new(true, null, [At(661, null)], true, []));
        shown = Assert.Single(store.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()!);
        Assert.Equal("11:01", P.FormatPlaybackPosition(shown));

        _time.Advance(DashboardDataStore.RealtimeWindow);
        await Task.Delay(30);
        Assert.DoesNotContain(_kit.Http.Requests, request => request.StartsWith("GET /api/v2/admin/stats", StringComparison.Ordinal));
    }
}

/// <summary>
/// The Egress view as the widget context sees it: the layout entry is the only
/// source, and every path that replaces entries (pick, Reload, Reset, a later
/// visit) yields the view the widget must draw.
/// </summary>
public sealed class AdminDashboardEgressViewWiringTests : IDisposable
{
    private const string Origin = "https://silo.test";
    private const string EgressNodes = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"nodes"}]}""";
    private const string EgressTotal = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"total"}]}""";
    private const string EgressNoView = """{"version":1,"entries":[{"id":"egress-24h","span":6,"rows":3,"range":"day"}]}""";
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly DashboardManualTime _time = new();
    private readonly DashboardFakeLayoutServer _server = new();
    private readonly DashboardMemoryLayoutCache _cache = new();

    public AdminDashboardEgressViewWiringTests()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private async Task<DashboardLayoutSync> Open(string? serverJson, string etag = "\"A\"")
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout(etag, serverJson));
        var sync = new DashboardLayoutSync(_authority.Capture(), _server, _cache, Origin, _time);
        await sync.LoadAsync();
        return sync;
    }

    private static string? Shown(DashboardLayoutSync sync)
        => DashboardLayoutDocument.EffectiveView(sync.Entries.Single(entry => entry.Id == DashboardRegistry.Egress24h));

    private static string? Stored(string document)
    {
        using var json = JsonDocument.Parse(document);
        var egress = json.RootElement.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == DashboardRegistry.Egress24h);
        return egress.TryGetProperty("view", out var view) ? view.GetString() : null;
    }

    private void Debounce() => _time.Advance(DashboardLayoutSync.SaveDebounce + TimeSpan.FromMilliseconds(1));

    [Fact]
    public async Task APickIsEchoedThroughChangedAndPersistsAcrossVisits()
    {
        var sync = await Open(EgressNoView);
        Assert.Equal("total", Shown(sync));
        var changes = 0;
        sync.Changed += () => changes++;
        sync.SetWidgetView(DashboardRegistry.Egress24h, DashboardRegistry.EgressViewNodes);
        Assert.Equal(1, changes);
        Assert.Equal("nodes", Shown(sync));
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Equal(("nodes", "\"A\""), (Stored(save.Document), save.ETag));
        sync.Dispose();

        // The next visit paints the cached view, then adopts the server's stored one.
        var next = await Open(save.Document, "\"S1\"");
        Assert.Equal("nodes", Shown(next));
        next.Dispose();
    }

    [Fact]
    public async Task ReloadReplacesTheShownViewWithTheServersStoredOne()
    {
        var sync = await Open(EgressNodes);
        Assert.Equal("nodes", Shown(sync));
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"B\"", EgressTotal));
        await sync.ReloadServerLayoutAsync();
        Assert.Equal("total", Shown(sync));
        Assert.Equal("total", sync.Entries.Single().View);
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"C\"", EgressNoView));
        await sync.ReloadServerLayoutAsync();
        Assert.Equal("total", Shown(sync));
        Assert.Null(sync.Entries.Single().View);
        sync.Dispose();
    }

    [Fact]
    public async Task ResetDrawsTheDefaultViewAndStoresNone()
    {
        var sync = await Open(EgressNodes);
        Assert.Equal("nodes", Shown(sync));
        _server.OnRead = _ => Task.FromResult(DashboardFakeLayoutServer.Layout("\"R\"", null));
        sync.ResetLayout();
        Assert.Equal("total", Shown(sync));
        Assert.Null(sync.Entries.Single(entry => entry.Id == DashboardRegistry.Egress24h).View);
        await sync.PendingWrites;
        Assert.Equal(1, _server.Resets);
        sync.MoveWidget(DashboardRegistry.Users, DashboardRegistry.StatActiveStreams);
        Debounce();
        await sync.PendingWrites;
        var save = Assert.Single(_server.Saves);
        Assert.Null(Stored(save.Document));
        Assert.Equal("\"R\"", save.ETag);
        sync.Dispose();
    }

    [Fact]
    public async Task PickingTheShownViewOrAnUnofferedOneNeitherEchoesNorSaves()
    {
        var sync = await Open(EgressNodes);
        var changes = 0;
        sync.Changed += () => changes++;
        sync.SetWidgetView(DashboardRegistry.Egress24h, DashboardRegistry.EgressViewNodes);
        sync.SetWidgetView(DashboardRegistry.Egress24h, "bands");
        sync.SetWidgetView(DashboardRegistry.Playback24h, DashboardRegistry.EgressViewNodes);
        Debounce();
        await sync.PendingWrites;
        Assert.Equal(0, changes);
        Assert.Empty(_server.Saves);
        sync.Dispose();
    }

    [Fact]
    public async Task ARetiredLeaseRefusesThePickSoTheWidgetKeepsTheStoredView()
    {
        var sync = await Open(EgressNoView);
        _kit.SetRole("user");
        sync.SetWidgetView(DashboardRegistry.Egress24h, DashboardRegistry.EgressViewNodes);
        Assert.Equal("total", Shown(sync));
        Debounce();
        await sync.PendingWrites;
        Assert.Empty(_server.Saves);
        sync.Dispose();
    }
}
