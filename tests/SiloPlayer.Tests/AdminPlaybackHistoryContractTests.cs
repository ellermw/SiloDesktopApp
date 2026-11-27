using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The Playback History contract as extended in official Silo c59798116: the new filters, server cursor paging, the new entry fields and the
/// single-session read. The existing page keeps its behaviour (see <see cref="AdminPlaybackAuditTests"/>); these tests add only what is new.
/// </summary>
public sealed class AdminPlaybackHistoryContractTests
{
    private const string Base = "/api/v2/admin/playback-history";
    internal const string LegacyCapabilities = """
        {"allowed":true,"available":true,"client_identity":true,"client_ip":true,"device_identity":true,"end_reason":true,
        "end_reason_values":["stopped"],"ended_before":true,"lifecycle_seq":true,"revision":"legacy","state":"available",
        "stream_decisions":true,"watch_measurement":true}
        """;
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminPlaybackHistoryApi Api) Create()
    {
        var kit = AdminMonitorKit.Create();
        return (kit, new AdminMonitorAuthority(kit.Client, kit.Auth), new AdminPlaybackHistoryApi(kit.Client));
    }

    private static string Page(string[] entries, bool hasMore = false, string? next = null) =>
        "{\"items\":[" + string.Join(",", entries) + "],\"page\":{\"has_more\":" + (hasMore ? "true" : "false") + (next == null ? "" : ",\"next_cursor\":\"" + next + "\"") + "}}";

    /// <summary>An entry as a server older than the rework sends it: none of the new fields.</summary>
    private static string OldEntry(string id) => $$"""
        {"session_id":"{{id}}","user_id":"7","username":"u","profile_id":"p1","profile_name":"P","media_item_id":"m1","media_file_id":"42","media_title":"T","media_type":"movie",
        "play_method":"direct","started_at":"2026-11-01T10:00:00Z","ended_at":"2026-11-01T11:00:00Z","end_position_seconds":3000,"duration_seconds":3600,"completed":true,
        "played_seconds":60,"series_title":"","season_number":null,"episode_number":null}
        """;

    // ── Filters ───────────────────────────────────────────────

    [Fact]
    public void EveryNewFilterIsSentInAFixedOrderWithTheCursorLast()
    {
        var filter = new AdminPlaybackHistoryFilter("7", "p1", AdminPlaybackHistoryCompletion.Completed, "item-9",
            SeriesId: "series-1", LibraryId: "2", Search: "  dune  ", PlayMethod: "transcode", StreamLocation: "remote", SubtitleBurnIn: true,
            ClientName: "Chrome", DevicePlatform: "web", SourceVideoCodec: "hevc", AdaptationReason: "subtitle_burn_in",
            StartedAfter: "2026-11-01T00:00:00Z", StartedBefore: "2026-11-22T00:00:00Z");
        Assert.Equal(Base + "?limit=25&user_id=7&profile_id=p1&media_item_id=item-9&completed=true&series_id=series-1&library_id=2&play_method=transcode"
            + "&stream_location=remote&subtitle_burn_in=true&client_name=Chrome&device_platform=web&source_video_codec=hevc&adaptation_reason=subtitle_burn_in"
            + "&started_after=2026-11-01T00%3A00%3A00Z&started_before=2026-11-22T00%3A00%3A00Z&search=dune&cursor=c%2F1%3D",
            AdminPlaybackHistoryApi.HistoryPath(filter, 25, "c/1="));
        // The old shape is unchanged: no new filter, no cursor.
        Assert.Equal(Base + "?limit=100", AdminPlaybackHistoryApi.HistoryPath(new()));
        Assert.Equal(Base + "?limit=100&user_id=7&completed=false", AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter("7", null, AdminPlaybackHistoryCompletion.Partial)));
        // An empty or oversized cursor is refused, as the other cursor-paged reads do.
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new(), 25, ""));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new(), 25, new string('x', 8193)));
    }

    [Fact]
    public void ADrilldownFromInsightsBecomesTheRightHistoryRequest()
    {
        // Official insightsShared.test.ts: a multi-facet Insights drilldown round-trips into the v2 history query.
        var drilldown = AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown
        {
            UserId = "7", ProfileId = "p-alice", LibraryId = "2", PlayMethod = "transcode", ClientName = "Chrome", DevicePlatform = "web",
            SourceVideoCodec = "hevc", AdaptationReason = "subtitle_burn_in", SubtitleBurnIn = true, StreamLocation = "remote",
            StartedAfter = "2026-11-01T00:00:00Z", StartedBefore = "2026-11-22T00:00:00Z",
        });
        Assert.Equal(AdminRoute.PlaybackHistory, drilldown.Route);
        var filter = AdminPlaybackHistoryFilter.FromQuery(key => drilldown.Get(key));
        Assert.Equal(Base + "?limit=50&user_id=7&profile_id=p-alice&library_id=2&play_method=transcode&stream_location=remote&subtitle_burn_in=true"
            + "&client_name=Chrome&device_platform=web&source_video_codec=hevc&adaptation_reason=subtitle_burn_in"
            + "&started_after=2026-11-01T00%3A00%3A00Z&started_before=2026-11-22T00%3A00%3A00Z",
            AdminPlaybackHistoryApi.HistoryPath(filter, 50));
        // A title filter by series reaches the series read.
        var series = AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown { SeriesId = "series-9" });
        Assert.Equal(Base + "?limit=25&series_id=series-9", AdminPlaybackHistoryApi.HistoryPath(AdminPlaybackHistoryFilter.FromQuery(key => series.Get(key)), 25));
    }

    [Fact]
    public void FilterValuesTheWebUiDoesNotOfferAreNoFilter()
    {
        string? Get(Dictionary<string, string> query, string key) => query.TryGetValue(key, out var value) ? value : null;
        var none = new Dictionary<string, string>
        {
            ["play_method"] = "all", ["stream_location"] = "all", ["subtitle_burn_in"] = "false", ["library_id"] = "abc", ["client_name"] = "  ",
            ["search"] = "", ["series_id"] = "   ",
        };
        var filter = AdminPlaybackHistoryFilter.FromQuery(key => Get(none, key));
        Assert.False(filter.HasRichFilters);
        Assert.Empty(filter.ToQuery());
        Assert.Equal(Base + "?limit=25", AdminPlaybackHistoryApi.HistoryPath(filter, 25));
        // Unknown methods and zones are dropped, so a typo cannot be sent; only "true" turns burn-in on (the WebUI reads === "true").
        var odd = AdminPlaybackHistoryFilter.FromQuery(key => Get(new() { ["play_method"] = "direct_play", ["stream_location"] = "LAN", ["subtitle_burn_in"] = "1", ["library_id"] = "007", ["completed"] = "maybe" }, key));
        Assert.Equal(new Dictionary<string, string> { ["library_id"] = "7" }, odd.ToQuery().ToDictionary(pair => pair.Key, pair => pair.Value));
        Assert.True(AdminPlaybackHistoryFilter.FromQuery(key => Get(new() { ["subtitle_burn_in"] = "true" }, key)).SubtitleBurnIn);
        Assert.Equal("remux", AdminPlaybackHistoryFilter.FromQuery(key => Get(new() { ["play_method"] = " remux " }, key)).RequestPlayMethod);
    }

    [Fact]
    public void TheFilterRoundTripsThroughTheUrlQuery()
    {
        var filter = new AdminPlaybackHistoryFilter("7", "p1", AdminPlaybackHistoryCompletion.Partial, "item-9", "series-1", "2", "dune", "direct", "local", true,
            "Silo", "windows", "av1", "bandwidth_limit", "2026-11-01T00:00:00Z", "2026-11-22T00:00:00Z", "session/+ &");
        var query = filter.ToQuery();
        Assert.Equal(["adaptation_reason", "client_name", "completed", "device_platform", "library_id", "media_item_id", "play_method", "profile_id", "search",
            "series_id", "session", "source_video_codec", "started_after", "started_before", "stream_location", "subtitle_burn_in", "user_id"], query.Keys.Order(StringComparer.Ordinal));
        var request = new AdminRouteRequest(AdminRoute.PlaybackHistory, query);
        Assert.True(AdminRouteCatalog.TryResolve(request.Href, out var parsed));
        Assert.Equal(filter, AdminPlaybackHistoryFilter.FromQuery(parsed!.Request!.Get));
        Assert.DoesNotContain("session=", AdminPlaybackHistoryApi.HistoryPath(filter));
        // Normalising twice changes nothing, and a profile still needs its account.
        Assert.Equal(filter.Normalized(), filter.Normalized().Normalized());
        Assert.Null(new AdminPlaybackHistoryFilter(null, "p1", Search: " x ").Normalized().ProfileId);
        Assert.Equal("x", new AdminPlaybackHistoryFilter(Search: " x ").Normalized().Search);
        // An account-less filter has no rich filters, one with a search has.
        Assert.False(new AdminPlaybackHistoryFilter("7").HasRichFilters);
        Assert.True(new AdminPlaybackHistoryFilter(Search: "x").HasRichFilters);
    }

    [Fact]
    public void FilterValuesLongerThanTheServerAcceptsAreRefusedWithTheirLimits()
    {
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(Search: new string('x', AdminPlaybackHistoryApi.MaxSearchLength + 1))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(ClientName: new string('x', AdminPlaybackHistoryApi.MaxClientNameLength + 1))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(DevicePlatform: new string('x', AdminPlaybackHistoryApi.MaxDevicePlatformLength + 1))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(SourceVideoCodec: new string('x', AdminPlaybackHistoryApi.MaxSourceVideoCodecLength + 1))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(AdaptationReason: new string('x', AdminPlaybackHistoryApi.MaxAdaptationReasonLength + 1))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(SeriesId: new string('x', 1025))));
        Assert.Throws<ArgumentException>(() => AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(StartedAfter: new string('1', AdminPlaybackHistoryApi.MaxInstantLength + 1))));
        // At the limit is fine.
        AdminPlaybackHistoryApi.HistoryPath(new AdminPlaybackHistoryFilter(Search: new string('x', AdminPlaybackHistoryApi.MaxSearchLength)));
    }

    // ── Server cursor paging ──────────────────────────────────

    [Fact]
    public async Task APagerWalksTheServerCursorsWithTheSamePageSize()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (request, _) =>
        {
            var query = request.RequestUri!.Query;
            return Task.FromResult(AdminMonitorKit.Json(
                query.Contains("cursor=c2") ? Page([OldEntry("s5")])
                : query.Contains("cursor=c1") ? Page([OldEntry("s3"), OldEntry("s4")], true, "c2")
                : Page([OldEntry("s1"), OldEntry("s2")], true, "c1")));
        };
        using var lease = authority.Capture();
        var filter = new AdminPlaybackHistoryFilter("7");
        var first = await api.ReadPageAsync(lease, filter, 2, null);
        Assert.Equal(["s1", "s2"], first.Rows.Select(row => row.SessionId));
        Assert.True(first.ServerHasMore);
        Assert.Equal("c1", first.NextCursor);
        Assert.Equal(2, first.RequestedLimit);
        var second = await api.ReadPageAsync(lease, filter, 2, first.NextCursor);
        Assert.Equal(["s3", "s4"], second.Rows.Select(row => row.SessionId));
        Assert.Equal("c2", second.NextCursor);
        var last = await api.ReadPageAsync(lease, filter, 2, second.NextCursor);
        Assert.Equal(["s5"], last.Rows.Select(row => row.SessionId));
        Assert.False(last.ServerHasMore);
        Assert.Null(last.NextCursor);
        Assert.Equal([$"GET {Base}?limit=2&user_id=7", $"GET {Base}?limit=2&user_id=7&cursor=c1", $"GET {Base}?limit=2&user_id=7&cursor=c2"], kit.Http.Requests.ToArray());
        // The first-page read of the existing page is the same call without a cursor.
        var window = await api.ReadWindowAsync(lease, filter, 2);
        Assert.Equal("c1", window.NextCursor);
        Assert.Equal($"GET {Base}?limit=2&user_id=7", kit.Http.Requests.Last());
    }

    [Fact]
    public void APageThatRepeatsItsOwnCursorOrBreaksTheContinuationRulesIsRefused()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        AdminPlaybackHistoryWirePage Wire(string json) => JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        // Official listAdminPlaybackHistory: next_cursor equal to the cursor that was sent would loop a pager forever.
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1")], true, "c1")), 25, "c1"));
        Assert.Equal("c2", AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1")], true, "c2")), 25, "c1").NextCursor);
        // More pages without a cursor, a cursor on the last page, no rows with more to come, and too many rows.
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1")], true)), 25));
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1")], false, "c9")), 25));
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([], true, "c9")), 25));
        Assert.Throws<InvalidDataException>(() => AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1"), OldEntry("s2")])), 1));
        Assert.Null(AdminPlaybackHistoryApi.ValidateWindow(Wire(Page([OldEntry("s1")])), 25).NextCursor);
    }

    // ── Entries ───────────────────────────────────────────────

    [Fact]
    public void AnEntryCarriesEveryNewFieldAndOlderServersGetNeutralDefaults()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var window = AdminPlaybackHistoryApi.ValidateWindow(JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(Page([AdminInsightsWire.HistoryEntry, OldEntry("old")]),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), 25);
        var row = window.Rows[0];
        Assert.Equal(["a"], row.AdaptationReasons);
        Assert.True(row.CompatOrigin);
        Assert.True(row.IsJellyfinClient);
        Assert.Equal(("7", "library name sample"), (row.LibraryId, row.LibraryName));
        Assert.Equal(("output container sample", "output protocol sample", "reporting node sample"), (row.OutputContainer, row.OutputProtocol, row.ReportingNode));
        Assert.True(row.RouteFactsRecorded);
        Assert.Equal(("routing egress sample", "7", "routing egress node name sample"), (row.RoutingEgress, row.RoutingEgressNodeId, row.RoutingEgressNodeName));
        Assert.Equal(("routing execution sample", "7", "routing execution node name sample"), (row.RoutingExecution, row.RoutingExecutionNodeId, row.RoutingExecutionNodeName));
        Assert.Equal((12L, "source audio codec sample", 12L), (row.SourceAudioChannels, row.SourceAudioCodec, row.SourceBitrateKbps));
        Assert.Equal(("source container sample", true, "source resolution sample"), (row.SourceContainer, row.SourceHdr, row.SourceResolution));
        Assert.Equal(("source video codec sample", "source video range type sample"), (row.SourceVideoCodec, row.SourceVideoRangeType));
        Assert.Equal("local", row.StreamLocation);
        Assert.Equal(("burn_in", "embedded", true, true, "7"), (row.Subtitle!.Delivery, row.Subtitle.Source, row.Subtitle.Forced, row.Subtitle.HearingImpaired, row.Subtitle.TrackId));
        Assert.Equal((12L, "target audio codec sample", 12L), (row.TargetAudioChannels, row.TargetAudioCodec, row.TargetBitrateKbps));
        Assert.Equal(("target resolution sample", "target video codec sample", "tone map mode sample", "transcode hw accel sample"),
            (row.TargetResolution, row.TargetVideoCodec, row.ToneMapMode, row.TranscodeHwAccel));

        var old = window.Rows[1];
        Assert.Empty(old.AdaptationReasons);
        Assert.False(old.IsJellyfinClient);
        Assert.False(old.RouteFactsRecorded);
        Assert.Null(old.CompatOrigin); Assert.Null(old.LibraryId); Assert.Null(old.StreamLocation); Assert.Null(old.Subtitle); Assert.Null(old.SourceVideoCodec); Assert.Null(old.TranscodeHwAccel);

        var nulls = AdminPlaybackHistoryApi.ValidateWindow(JsonSerializer.Deserialize<AdminPlaybackHistoryWirePage>(Page([AdminInsightsWire.HistoryEntryNulls]),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), 25).Rows[0];
        Assert.Null(nulls.Subtitle); Assert.Null(nulls.LibraryId); Assert.Null(nulls.RoutingEgressNodeId); Assert.Null(nulls.RoutingExecutionNodeId); Assert.Null(nulls.SourceHdr);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData("007", "7")]
    [InlineData("0", null)]
    [InlineData("-4", null)]
    [InlineData("abc", null)]
    [InlineData("9007199254740992", null)]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("", null)]
    public void NodeAndLibraryIdsFollowTheWebUiNumericIdRule(string value, string? expected) =>
        Assert.Equal(expected, AdminPlaybackHistoryApi.ParseOptionalNumericId(value));

    [Fact]
    public async Task OneSessionIsReadByIdAndAnUnknownOneIsNull()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/gone", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"type\":\"https://siloserver.org/docs/api/v2/problems/not_found\",\"title\":\"Not Found\",\"status\":404}", Encoding.UTF8, "application/problem+json") }
            : AdminMonitorKit.Json(AdminInsightsWire.HistoryEntry));
        using var lease = authority.Capture();
        var row = (await api.GetEntryAsync(lease, "sess/1 a"))!;
        Assert.Equal("7", row.SessionId);
        Assert.True(row.RouteFactsRecorded);
        Assert.Null(await api.GetEntryAsync(lease, "gone"));
        Assert.Equal([$"GET {Base}/sess%2F1%20a", $"GET {Base}/gone"], kit.Http.Requests.ToArray());
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetEntryAsync(lease, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetEntryAsync(lease, new string('x', AdminPlaybackHistoryApi.MaxSessionIdLength + 1)));
    }

    [Fact]
    public async Task ASessionThatFailsTheEntryChecksOrArrivesAfterTheLeaseRetiredIsNotPublished()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        using var _ = AppStrings.UseForCurrentFlow(English);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(OldEntry("s1").Replace("\"user_id\":\"7\"", "\"user_id\":\"abc\"")));
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetEntryAsync(lease, "s1"));
        kit.Http.Respond = (_, _) =>
        {
            kit.Auth.SelectProfile("child", null, new() { Id = "child", IsPrimary = false });
            return Task.FromResult(AdminMonitorKit.Json(OldEntry("s1")));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetEntryAsync(lease, "s1"));
    }

    // ── Capabilities ──────────────────────────────────────────

    [Fact]
    public async Task CapabilitiesAreReadAndAnOlderServerHasNone()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var answer = AdminMonitorKit.Json(AdminInsightsWire.HistoryCapabilities);
        kit.Http.Respond = (_, _) => Task.FromResult(answer);
        using var lease = authority.Capture();
        var capabilities = (await api.GetCapabilitiesAsync(lease))!;
        Assert.True(capabilities.RichFilters && capabilities.RouteFacts && capabilities.SeriesFilter && capabilities.SessionDetail && capabilities.EndedBefore);
        Assert.Equal("rev-1", capabilities.Revision);
        Assert.Equal(["a"], capabilities.EndReasonValues);
        Assert.Equal($"GET {Base}/capabilities", kit.Http.Requests.Last());
        answer = AdminMonitorKit.Json(LegacyCapabilities);
        var legacy = await api.GetCapabilitiesAsync(lease);
        Assert.NotNull(legacy);
        Assert.True(legacy.Available && legacy.EndedBefore);
        Assert.False(legacy.RichFilters || legacy.RouteFacts || legacy.SeriesFilter || legacy.SessionDetail);
        answer = new HttpResponseMessage(HttpStatusCode.NotFound);
        Assert.Null(await api.GetCapabilitiesAsync(lease));
        using var _l = AppStrings.UseForCurrentFlow(English);
        answer = AdminMonitorKit.Json("{\"allowed\":true}");
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetCapabilitiesAsync(lease));
    }
}
