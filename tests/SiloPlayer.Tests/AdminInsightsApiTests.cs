using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Admin Insights reads (official Silo c59798116). Request shape per endpoint, response parsing from fixtures generated from the official
/// schema, the models' coverage of every wire field, older-server behaviour, deadlines and authority retirement. Synthetic data only.
/// </summary>
public sealed class AdminInsightsApiTests
{
    private const string Base = "/api/v2/admin/insights";

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminInsightsApi Api) Create(TimeSpan? deadline = null)
    {
        var kit = AdminMonitorKit.Create();
        return (kit, new AdminMonitorAuthority(kit.Client, kit.Auth), new AdminInsightsApi(kit.Client, deadline));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail = "Problem") =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail }),
                Encoding.UTF8, "application/problem+json"),
        };

    private static AdminInsightsWindow Window(AdminInsightsRange range = AdminInsightsRange.Days30, string tz = "America/New_York") => new() { Range = range, Tz = tz };

    // ── Request shape ─────────────────────────────────────────

    [Fact]
    public void EveryReadSendsOnlyTheParametersItAccepts()
    {
        var scoped = new AdminInsightsWindow { Range = AdminInsightsRange.Days7, Tz = "America/New_York", LibraryId = "4", UserId = "12", ProfileId = "p 1" };
        // activity, library: window + library + account + profile; refresh only when asked.
        Assert.Equal(Base + "/activity?range=30d&tz=UTC", AdminInsightsApi.ActivityPath(new()));
        Assert.Equal(Base + "/activity?range=7d&tz=America%2FNew_York&library_id=4&user_id=12&profile_id=p%201&refresh=true", AdminInsightsApi.ActivityPath(scoped, refresh: true));
        Assert.Equal(Base + "/library?range=7d&tz=America%2FNew_York&library_id=4&user_id=12&profile_id=p%201", AdminInsightsApi.LibraryPath(scoped));
        // titles: + sort and limit (1 to 50).
        Assert.Equal(Base + "/titles?range=7d&tz=America%2FNew_York&library_id=4&user_id=12&profile_id=p%201&sort=watch_time&limit=8",
            AdminInsightsApi.TitlesPath(scoped, AdminInsightsTitleSort.WatchTime, 8));
        Assert.Equal(Base + "/titles?range=30d&tz=UTC&sort=plays&limit=10", AdminInsightsApi.TitlesPath(new()));
        Assert.EndsWith("&sort=viewers&limit=50&refresh=true", AdminInsightsApi.TitlesPath(new(), AdminInsightsTitleSort.Viewers, 500, refresh: true));
        Assert.EndsWith("&limit=1", AdminInsightsApi.TitlesPath(new(), limit: 0));
        // one title: no refresh flag, the id is escaped into the path.
        Assert.Equal(Base + "/titles/movie%3A1%2F2?range=7d&tz=America%2FNew_York&library_id=4&user_id=12&profile_id=p%201", AdminInsightsApi.TitlePath("movie:1/2", scoped));
        // devices: + min_sessions (1 to 1000).
        Assert.Equal(Base + "/devices?range=7d&tz=America%2FNew_York&library_id=4&user_id=12&profile_id=p%201&min_sessions=1&refresh=true", AdminInsightsApi.DevicesPath(scoped, 1, refresh: true));
        Assert.EndsWith("&min_sessions=5", AdminInsightsApi.DevicesPath(new()));
        Assert.EndsWith("&min_sessions=1000", AdminInsightsApi.DevicesPath(new(), 5000));
        // people: the library but no account or profile.
        Assert.Equal(Base + "/people?range=7d&tz=America%2FNew_York&library_id=4&sort=watch_time&dir=desc&limit=50&offset=0", AdminInsightsApi.PeoplePath(scoped));
        Assert.Equal(Base + "/people?range=7d&tz=America%2FNew_York&library_id=4&search=an%20na&sort=last_seen&dir=asc&limit=100&offset=150&refresh=true",
            AdminInsightsApi.PeoplePath(scoped, new AdminInsightsPeopleQuery("  an na ", AdminInsightsPeopleSort.LastSeen, false, 500, 150), refresh: true));
        Assert.Equal(Base + "/people?range=30d&tz=UTC&sort=direct_pct&dir=desc&limit=1&offset=10000",
            AdminInsightsApi.PeoplePath(new(), new AdminInsightsPeopleQuery(Sort: AdminInsightsPeopleSort.DirectPct, Limit: 0, Offset: 99999)));
        Assert.Contains("sort=completion_pct", AdminInsightsApi.PeoplePath(new(), new AdminInsightsPeopleQuery(Sort: AdminInsightsPeopleSort.CompletionPct)));
        Assert.DoesNotContain("search=", AdminInsightsApi.PeoplePath(new(), new AdminInsightsPeopleQuery("   ")));
        // bandwidth: the range and zone only, even when the window carries a scope.
        Assert.Equal(Base + "/bandwidth?range=7d&tz=America%2FNew_York", AdminInsightsApi.BandwidthPath(scoped));
        Assert.Equal(Base + "/bandwidth?range=all&tz=UTC&refresh=true", AdminInsightsApi.BandwidthPath(new() { Range = AdminInsightsRange.All }, refresh: true));
        Assert.Equal(Base + "/capabilities", AdminInsightsApi.CapabilitiesPath());
    }

    [Fact]
    public void AllRangesAndTheCustomDatesUseTheirWireNames()
    {
        foreach (var (range, wire) in new[] { (AdminInsightsRange.Days7, "7d"), (AdminInsightsRange.Days30, "30d"), (AdminInsightsRange.Days90, "90d"), (AdminInsightsRange.Year1, "1y"), (AdminInsightsRange.All, "all") })
        {
            Assert.Equal(wire, range.Wire());
            // from and to are sent only for a custom range, whatever the window carries.
            Assert.Equal($"{Base}/activity?range={wire}&tz=UTC", AdminInsightsApi.ActivityPath(new() { Range = range, FromDate = "2026-01-01", ToDate = "2026-01-31" }));
        }
        Assert.Equal(Base + "/activity?range=custom&tz=UTC&from_date=2026-10-01&to_date=2026-10-15",
            AdminInsightsApi.ActivityPath(new() { Range = AdminInsightsRange.Custom, FromDate = " 2026-10-01 ", ToDate = "2026-10-15" }));
        Assert.Equal(Base + "/bandwidth?range=custom&tz=UTC&from_date=2026-10-01&to_date=2026-10-15",
            AdminInsightsApi.BandwidthPath(new() { Range = AdminInsightsRange.Custom, FromDate = "2026-10-01", ToDate = "2026-10-15" }));
        Assert.Equal("custom", AdminInsightsRange.Custom.Wire());
        Assert.True(AdminInsightsRanges.TryParse("1y", out var parsed) && parsed == AdminInsightsRange.Year1);
        Assert.False(AdminInsightsRanges.TryParse("1Y", out _));
        Assert.False(AdminInsightsRanges.TryParse(null, out _));
        // An empty zone is UTC, the server default.
        Assert.Equal(Base + "/activity?range=30d&tz=UTC", AdminInsightsApi.ActivityPath(new() { Tz = "  " }));
    }

    [Fact]
    public void ImpossibleRequestsAreRefusedBeforeTheyAreSent()
    {
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { Range = AdminInsightsRange.Custom }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { Range = AdminInsightsRange.Custom, FromDate = "2026-10-01", ToDate = "10/15/2026" }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { LibraryId = "0" }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { UserId = "-3" }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { UserId = "abc" }));
        // Profile ids repeat across accounts, so a profile needs its account (the server answers 400 otherwise).
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { ProfileId = "p1" }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { UserId = "7", ProfileId = new string('x', 1025) }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.ActivityPath(new() { Tz = new string('x', 129) }));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.PeoplePath(new(), new AdminInsightsPeopleQuery(new string('x', 129))));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.TitlePath("", new()));
        Assert.Throws<ArgumentException>(() => AdminInsightsApi.TitlePath(new string('x', 1025), new()));
        // People and Bandwidth ignore a scope they do not take rather than refusing it.
        Assert.StartsWith(Base + "/people?", AdminInsightsApi.PeoplePath(new() { ProfileId = "p1" }));
        Assert.StartsWith(Base + "/bandwidth?", AdminInsightsApi.BandwidthPath(new() { UserId = "abc", LibraryId = "0" }));
    }

    [Fact]
    public async Task EachMethodRequestsItsPathUnderTheCapturedAuthority()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath switch
        {
            Base + "/capabilities" => AdminInsightsWire.Capabilities,
            Base + "/activity" => AdminInsightsWire.Activity,
            Base + "/titles" => AdminInsightsWire.Titles,
            Base + "/library" => AdminInsightsWire.Library,
            Base + "/people" => AdminInsightsWire.People,
            Base + "/devices" => AdminInsightsWire.Devices,
            Base + "/bandwidth" => AdminInsightsWire.Bandwidth,
            _ => AdminInsightsWire.Title,
        }));
        using var lease = authority.Capture();
        var window = Window(AdminInsightsRange.Days90);
        Assert.NotNull(await api.GetCapabilitiesAsync(lease));
        await api.GetActivityAsync(lease, window, refresh: true);
        await api.GetTitlesAsync(lease, window, AdminInsightsTitleSort.Viewers, 8);
        Assert.NotNull(await api.GetTitleAsync(lease, "movie-1", window));
        await api.GetLibraryAsync(lease, window);
        await api.GetPeopleAsync(lease, window, new AdminInsightsPeopleQuery(Offset: 50));
        await api.GetDevicesAsync(lease, window, 1);
        await api.GetBandwidthAsync(lease, window, refresh: true);
        Assert.Equal(
        [
            "GET " + AdminInsightsApi.CapabilitiesPath(),
            "GET " + AdminInsightsApi.ActivityPath(window, true),
            "GET " + AdminInsightsApi.TitlesPath(window, AdminInsightsTitleSort.Viewers, 8),
            "GET " + AdminInsightsApi.TitlePath("movie-1", window),
            "GET " + AdminInsightsApi.LibraryPath(window),
            "GET " + AdminInsightsApi.PeoplePath(window, new AdminInsightsPeopleQuery(Offset: 50)),
            "GET " + AdminInsightsApi.DevicesPath(window, 1),
            "GET " + AdminInsightsApi.BandwidthPath(window, true),
        ], kit.Http.Requests.ToArray());
    }

    // ── Responses ─────────────────────────────────────────────

    [Theory]
    [InlineData("heatmap", "[null]")]
    [InlineData("buckets", "null")]
    [InlineData("summary", "null")]
    public async Task MalformedResponsesFailTheFirstReadAndDoNotReplaceRetainedData(string field, string invalidValue)
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        using var lease = authority.Capture();
        using var reader = new AdminInsightsReader<AdminInsightsActivity>(lease, keepPrevious: true);
        var invalid = JsonNode.Parse(AdminInsightsWire.Activity)!.AsObject();
        invalid[field] = JsonNode.Parse(invalidValue);
        var malformed = invalid.ToJsonString();
        var reply = malformed;
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(reply));
        Task Read() => reader.Request("activity", ct => api.GetActivityAsync(lease, Window(), ct: ct), force: true);

        await Read();
        Assert.True(reader.View.ShowsError);
        Assert.IsType<InvalidDataException>(reader.View.Error);

        reply = AdminInsightsWire.Activity;
        await Read();
        var retained = Assert.IsType<AdminInsightsActivity>(reader.View.Data);
        reply = malformed;
        await Read();
        Assert.Same(retained, reader.View.Data);
        Assert.IsType<InvalidDataException>(reader.View.RefreshError);
    }

    [Fact]
    public void EveryModelFieldIsAWireFieldOfTheSchemaAndTheOtherWayRound()
    {
        // The fixtures are generated from schema.ts at c59798116, so equality here means the models have exactly the schema's wire names.
        foreach (var (json, type) in new (string, Type)[]
        {
            (AdminInsightsWire.Capabilities, typeof(AdminInsightsCapabilities)),
            (AdminInsightsWire.Activity, typeof(AdminInsightsActivity)),
            (AdminInsightsWire.ActivityNulls, typeof(AdminInsightsActivity)),
            (AdminInsightsWire.Titles, typeof(AdminInsightsTitles)),
            (AdminInsightsWire.TitlesNulls, typeof(AdminInsightsTitles)),
            (AdminInsightsWire.Title, typeof(AdminInsightsTitle)),
            (AdminInsightsWire.TitleNulls, typeof(AdminInsightsTitle)),
            (AdminInsightsWire.Library, typeof(AdminInsightsLibrary)),
            (AdminInsightsWire.LibraryNulls, typeof(AdminInsightsLibrary)),
            (AdminInsightsWire.People, typeof(AdminInsightsPeople)),
            (AdminInsightsWire.PeopleNulls, typeof(AdminInsightsPeople)),
            (AdminInsightsWire.Devices, typeof(AdminInsightsDevices)),
            (AdminInsightsWire.DevicesNulls, typeof(AdminInsightsDevices)),
            (AdminInsightsWire.Bandwidth, typeof(AdminInsightsBandwidth)),
            (AdminInsightsWire.BandwidthNulls, typeof(AdminInsightsBandwidth)),
            (AdminInsightsWire.HistoryCapabilities, typeof(AdminPlaybackHistoryCapabilities)),
        })
        {
            using var document = JsonDocument.Parse(json);
            AssertCovers(document.RootElement, type, type.Name, ignoredModelOnly: []);
        }
        using var entry = JsonDocument.Parse(AdminInsightsWire.HistoryEntry);
        // The older server's name for the end position is read as well and is not in the current schema.
        AssertCovers(entry.RootElement, typeof(AdminPlaybackHistoryWireEntry), "history entry", ignoredModelOnly: ["watched_seconds"]);
        using var entryNulls = JsonDocument.Parse(AdminInsightsWire.HistoryEntryNulls);
        AssertCovers(entryNulls.RootElement, typeof(AdminPlaybackHistoryWireEntry), "history entry (nulls)", ignoredModelOnly: ["watched_seconds"]);
    }

    private static string WireName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);

    private static void AssertCovers(JsonElement json, Type type, string path, string[] ignoredModelOnly)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(WireName, StringComparer.Ordinal);
        var wire = json.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(wire.SetEquals(properties.Keys.Except(ignoredModelOnly)),
            $"{path}: wire only [{string.Join(", ", wire.Except(properties.Keys))}], model only [{string.Join(", ", properties.Keys.Except(wire).Except(ignoredModelOnly))}]");
        foreach (var field in json.EnumerateObject())
        {
            var member = Nullable.GetUnderlyingType(properties[field.Name].PropertyType) ?? properties[field.Name].PropertyType;
            if (field.Value.ValueKind == JsonValueKind.Object && !member.IsGenericType) AssertCovers(field.Value, member, path + "." + field.Name, []);
            else if (field.Value.ValueKind == JsonValueKind.Array && member.IsGenericType && field.Value.GetArrayLength() > 0 && field.Value[0].ValueKind == JsonValueKind.Object)
                AssertCovers(field.Value[0], member.GetGenericArguments()[0], path + "." + field.Name + "[]", []);
        }
    }

    [Fact]
    public async Task EveryResponseParsesWithItsNullsAndValues()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        string body = "";
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        using var lease = authority.Capture();
        var window = Window();

        body = AdminInsightsWire.Capabilities;
        var capabilities = (await api.GetCapabilitiesAsync(lease))!;
        Assert.True(capabilities.Allowed && capabilities.Breakdowns && capabilities.ProfileFilter && capabilities.NodeEgressDaily && capabilities.Titles && capabilities.Library);
        Assert.Equal("available", capabilities.State);
        Assert.Equal("rev-1", capabilities.Revision);

        body = AdminInsightsWire.Activity;
        var activity = await api.GetActivityAsync(lease, window);
        Assert.Equal("day", activity.BucketUnit);
        Assert.Equal("America/New_York", activity.Tz);
        Assert.Equal("2026-11-20T10:30:00.000Z", activity.From);
        var bucket = Assert.Single(activity.Buckets);
        Assert.Equal((12, 12, "2026-11-20"), (bucket.ActiveAccounts, bucket.ActiveProfiles, bucket.LabelDate));
        Assert.Equal(3600, bucket.WatchSeconds);
        Assert.Equal(12, Assert.Single(activity.Heatmap).Plays);
        Assert.Equal((2, 20), (activity.Heatmap[0].DayOfWeek, activity.Heatmap[0].Hour));
        Assert.Equal(0.75, activity.Summary.CompletionRate);
        Assert.Equal("2026-11-20T10:30:00.000Z", activity.Summary.PeakConcurrentAt);
        Assert.Equal("2026-11-20T10:30:00.000Z", Assert.Single(activity.PeakStreams).PeakStreamsAt);
        Assert.Single(activity.EndReasons);
        Assert.Equal(12, activity.Coverage.TotalSessions);
        Assert.NotNull(activity.Coverage.OldestRouteFactsAt);

        body = AdminInsightsWire.ActivityNulls;
        activity = await api.GetActivityAsync(lease, window);
        Assert.Null(activity.Summary.PeakConcurrentAt);
        Assert.Null(activity.PeakStreams[0].PeakStreamsAt);
        Assert.Null(activity.Coverage.OldestHistoryAt);
        Assert.Null(activity.Coverage.OldestMeasuredAt);
        Assert.Null(activity.Coverage.OldestMetricAt);
        Assert.Null(activity.Coverage.OldestRouteFactsAt);

        body = AdminInsightsWire.Titles;
        var titles = await api.GetTitlesAsync(lease, window);
        Assert.Equal("plays", titles.Sort);
        var row = Assert.Single(titles.Movies);
        Assert.Equal(("7", "movie", 2016L), (row.ContentId, row.Kind, row.Year));
        Assert.Equal("2026-11-20T10:30:00.000Z", row.LastPlayedAt);
        Assert.Single(titles.Shows); Assert.Single(titles.LeastFinished);
        Assert.Equal("7", Assert.Single(titles.Libraries).LibraryId);
        body = AdminInsightsWire.TitlesNulls;
        titles = await api.GetTitlesAsync(lease, window);
        Assert.Null(titles.Movies[0].Year); Assert.Null(titles.Movies[0].LastPlayedAt);

        body = AdminInsightsWire.Title;
        var title = (await api.GetTitleAsync(lease, "7", window))!;
        Assert.Equal("7", title.ContentId);
        Assert.Equal(0.75, title.DurationSeconds);
        Assert.Single(title.Buckets); Assert.Single(title.Episodes);
        var viewer = Assert.Single(title.Viewers);
        Assert.Equal(0.75, viewer.FurthestPositionSeconds);
        Assert.Equal(12, viewer.LastEpisode!.EpisodeNumber);
        Assert.True(title.EpisodesTruncated);
        body = AdminInsightsWire.TitleNulls;
        title = (await api.GetTitleAsync(lease, "7", window))!;
        Assert.Null(title.DurationSeconds); Assert.Null(title.Year);
        Assert.Null(title.Viewers[0].FurthestPositionSeconds); Assert.Null(title.Viewers[0].LastPlayedAt);

        body = AdminInsightsWire.Library;
        var library = await api.GetLibraryAsync(lease, window);
        Assert.Single(library.Genres); Assert.Single(library.ContentRatings); Assert.Single(library.Decades); Assert.Single(library.TimeToFirstPlay);
        Assert.Equal(0.75, Assert.Single(library.Libraries).PlayedShare);
        Assert.Equal("7", Assert.Single(library.RecentlyAddedUnplayed).LibraryId);
        Assert.Equal(0.75, library.Summary.MedianDaysToFirstPlay);
        body = AdminInsightsWire.LibraryNulls;
        library = await api.GetLibraryAsync(lease, window);
        Assert.Null(library.OldestHistoryAt); Assert.Null(library.Summary.MedianDaysToFirstPlay); Assert.Null(library.RecentlyAddedUnplayed[0].Year);

        body = AdminInsightsWire.People;
        var people = await api.GetPeopleAsync(lease, window);
        var account = Assert.Single(people.Accounts);
        Assert.Equal("7", account.UserId);
        Assert.Single(account.Profiles);
        Assert.Equal("top client sample", account.TopClient);
        Assert.Single(people.TopProfiles);
        Assert.Equal(12, people.TotalAccounts);
        body = AdminInsightsWire.PeopleNulls;
        people = await api.GetPeopleAsync(lease, window);
        Assert.Null(people.Accounts[0].LastSeenAt); Assert.Null(people.Accounts[0].TopClient); Assert.Null(people.Accounts[0].Profiles[0].LastSeenAt);

        body = AdminInsightsWire.Devices;
        var devices = await api.GetDevicesAsync(lease, window);
        Assert.Equal("h264", Assert.Single(devices.Codecs));
        var matrix = Assert.Single(devices.Matrix);
        Assert.Single(matrix.Cells);
        Assert.Single(devices.TopTranscoders); Assert.Single(devices.AdaptationReasons); Assert.Single(devices.ClientVersions); Assert.Single(devices.ProtocolBuckets);
        Assert.Single(devices.Subtitles!.Deliveries); Assert.Single(devices.Audio!.Decisions);
        Assert.Single(devices.ResolutionChanges); Assert.Single(devices.SourceResolutions); Assert.Single(devices.DeliveredResolutions);
        Assert.Equal(12, devices.Summary.TotalSessions);
        body = AdminInsightsWire.DevicesNulls;
        devices = await api.GetDevicesAsync(lease, window);
        Assert.Null(devices.ClientVersions[0].LastSeenAt);

        body = AdminInsightsWire.Bandwidth;
        var bandwidth = await api.GetBandwidthAsync(lease, window);
        Assert.True(bandwidth.HasRemoteNodes);
        var point = Assert.Single(bandwidth.Points);
        Assert.Equal(100, point.BytesBySource["api"]); Assert.Equal(50, point.PeakKbpsBySource["node:3"]);
        var source = Assert.Single(bandwidth.Sources);
        Assert.Equal(("key sample", "api"), (source.Key, source.Kind));
        Assert.Equal("7", source.NodeId);
        Assert.Equal("node type sample", source.NodeType);
        Assert.Equal(0.75, source.ShareOfTotal);
        Assert.Equal("2026-11-20T10:30:00.000Z", bandwidth.Summary.PeakEgressAt);
        body = AdminInsightsWire.BandwidthNulls;
        bandwidth = await api.GetBandwidthAsync(lease, window);
        Assert.Null(bandwidth.OldestMetricAt); Assert.Null(bandwidth.OldestRouteFactsAt); Assert.Null(bandwidth.Summary.PeakEgressAt); Assert.Null(bandwidth.Sources[0].NodeType);
    }

    [Fact]
    public async Task ABreakdownTheServerDoesNotCarryIsAbsentButAnythingElseMissingRefusesTheResponse()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        string body = "";
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        using var lease = authority.Capture();

        // A server whose capabilities say breakdowns=false omits those fields and still answers.
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.Activity)!.AsObject();
            node.Remove("end_reasons");
            node["buckets"]!.AsArray()[0]!.AsObject().Remove("active_accounts");
            node["buckets"]!.AsArray()[0]!.AsObject().Remove("active_profiles");
            body = node.ToJsonString();
            var activity = await api.GetActivityAsync(lease, Window());
            Assert.Empty(activity.EndReasons);
            Assert.Null(activity.Buckets[0].ActiveAccounts);
            Assert.Null(activity.Buckets[0].ActiveProfiles);
        }
        var devices = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.Devices)!.AsObject();
        foreach (var key in new[] { "client_versions", "protocol_buckets", "subtitles", "audio" }) devices.Remove(key);
        body = devices.ToJsonString();
        var read = await api.GetDevicesAsync(lease, Window());
        Assert.Empty(read.ClientVersions); Assert.Empty(read.ProtocolBuckets); Assert.Null(read.Subtitles); Assert.Null(read.Audio);
        var titles = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.Titles)!.AsObject();
        titles.Remove("least_finished");
        body = titles.ToJsonString();
        Assert.Empty((await api.GetTitlesAsync(lease, Window())).LeastFinished);
        var people = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.People)!.AsObject();
        people.Remove("top_profiles");
        body = people.ToJsonString();
        Assert.Empty((await api.GetPeopleAsync(lease, Window())).TopProfiles);

        // A required count, object or list that is absent is never read as zero or empty.
        foreach (var missing in new[] { "summary", "coverage", "heatmap", "buckets", "peak_streams", "tz", "from" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.Activity)!.AsObject();
            node.Remove(missing);
            body = node.ToJsonString();
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetActivityAsync(lease, Window()));
        }
        var summary = System.Text.Json.Nodes.JsonNode.Parse(AdminInsightsWire.Activity)!.AsObject();
        summary["summary"]!.AsObject().Remove("plays");
        body = summary.ToJsonString();
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetActivityAsync(lease, Window()));
        body = "null";
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetBandwidthAsync(lease, Window()));
    }

    // ── Older servers and problems ────────────────────────────

    [Theory]
    [InlineData(404)]
    [InlineData(405)]
    [InlineData(501)]
    public async Task AServerWithoutTheEndpointIsReportedAsUnsupportedNotAsAFailure(int status)
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("not found") });
        using var lease = authority.Capture();
        var window = Window();
        Assert.Null(await api.GetCapabilitiesAsync(lease));
        var error = await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetActivityAsync(lease, window, refresh: true));
        Assert.Equal(Base + "/activity", error.Path);
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetTitlesAsync(lease, window));
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetLibraryAsync(lease, window));
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetPeopleAsync(lease, window));
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetDevicesAsync(lease, window));
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetBandwidthAsync(lease, window));
        // A title read on an older server has no endpoint either (a 404 that is not the title's own not_found).
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetTitleAsync(lease, "movie-1", window));
    }

    [Fact]
    public async Task ADeclaredUnsupportedCapabilityIsUnsupportedAndOtherProblemsKeepTheirStatus()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        HttpResponseMessage response = Problem(HttpStatusCode.NotImplemented, "capability_unsupported");
        kit.Http.Respond = (_, _) => Task.FromResult(response);
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetActivityAsync(lease, Window()));
        response = Problem((HttpStatusCode)409, "capability_unsupported");
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetDevicesAsync(lease, Window()));
        // The store is down: a failure to show and retry, not a missing feature.
        response = Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", "The insights service is not available on this server.");
        var unavailable = await Assert.ThrowsAsync<ApiException>(() => api.GetActivityAsync(lease, Window()));
        Assert.Equal(503, unavailable.StatusCode);
        Assert.Equal("dependency_unavailable", unavailable.ProblemType);
        response = Problem(HttpStatusCode.BadRequest, "validation_failed", "invalid insights query: to_date must be on or after from_date");
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => api.GetBandwidthAsync(lease, Window()))).StatusCode);
        response = Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", "An episode id.");
        Assert.Equal(422, (await Assert.ThrowsAsync<ApiException>(() => api.GetTitleAsync(lease, "episode-1", Window()))).StatusCode);
    }

    [Fact]
    public async Task ATitleThatNoLongerExistsIsNullNotAFailure()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(Problem(HttpStatusCode.NotFound, "not_found", "Title not found."));
        using var lease = authority.Capture();
        Assert.Null(await api.GetTitleAsync(lease, "gone", Window()));
        // Only the title read does this: for the other reads a not_found is a missing endpoint.
        await Assert.ThrowsAsync<AdminInsightsUnsupportedException>(() => api.GetActivityAsync(lease, Window()));
    }

    // ── Authority and deadlines ───────────────────────────────

    [Fact]
    public async Task AReadWhoseLeaseRetiresWhileItIsInFlightPublishesNothing()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var calls = 0;
        kit.Http.Respond = (_, _) =>
        {
            Interlocked.Increment(ref calls);
            // The profile changes while the server is answering.
            kit.Auth.SelectProfile("child", null, new() { Id = "child", IsPrimary = false });
            return Task.FromResult(AdminMonitorKit.Json(AdminInsightsWire.Activity));
        };
        using var lease = authority.Capture();
        var started = api.GetActivityAsync(lease, Window());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => started);
        Assert.Equal(1, calls);
        Assert.False(lease.IsCurrent);
        // The retired lease cannot start another read: nothing is sent.
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.GetBandwidthAsync(lease, Window()));
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.GetCapabilitiesAsync(lease));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ALeaseThatRetiresDuringAnUnsupportedAnswerStillSurfacesAsRetired()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) =>
        {
            kit.Auth.SelectProfile("child", null, new() { Id = "child", IsPrimary = false });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        };
        using var lease = authority.Capture();
        // The page that asked is gone: it must not be told anything about this server, not even "unsupported".
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetActivityAsync(lease, Window()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetCapabilitiesAsync(lease));
    }

    [Fact]
    public async Task ASlowReadGetsItsOwnDeadlineAndCallerCancellationStaysCancellation()
    {
        var (kit, authority, api) = Create(TimeSpan.FromMilliseconds(150));
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return AdminMonitorKit.Json("{}");
        };
        using var lease = authority.Capture();
        var timeout = await Assert.ThrowsAsync<TaskCanceledException>(() => api.GetActivityAsync(lease, Window()));
        Assert.IsType<TimeoutException>(timeout.InnerException);
        Assert.True(lease.IsCurrent);

        using var cancel = new CancellationTokenSource();
        var pending = api.GetPeopleAsync(lease, Window(), ct: cancel.Token);
        cancel.Cancel();
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.IsNotType<TimeoutException>(cancelled.InnerException);
    }

    [Fact]
    public void TheDefaultDeadlineOutlastsTheWebUiAndStaysUnderTheSharedClientTimeout()
    {
        // The WebUI gives a read 30 s; the shared HttpClient would cut a call off at its 100 s default.
        Assert.True(AdminInsightsApi.ReadDeadline > TimeSpan.FromSeconds(30));
        Assert.True(AdminInsightsApi.ReadDeadline < TimeSpan.FromSeconds(100));
    }
}
