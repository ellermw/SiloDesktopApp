using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Wire contract of Configure › Scheduled Tasks (official internal/apiv2/admin_tasks.go fixtures). Synthetic data only.</summary>
public sealed class AdminTasksApiTests
{
    internal const string TaskJson = """
        {"key":"fixture","name":"Fixture task","description":"","category":"system","state":"idle","progress":0,"manual_only":false,
         "triggers":[{"type":"interval","interval_ms":1000}],"execution_scope":"process"}
        """;
    internal const string TasksJson = "{\"items\":[" + TaskJson + "]}";
    internal const string HistoryJson = """
        {"items":[
          {"task_key":"fixture","started_at":"2026-01-02T03:04:05.678Z","completed_at":"2026-01-02T03:04:05.678Z","status":"failed","duration_ms":0,
           "error_message":"Task failed. Inspect administrator diagnostics for details.","id":"3"},
          {"task_key":"fixture","started_at":"2026-01-02T03:04:05.678Z","completed_at":"2026-01-02T03:04:05.678Z","status":"failed","duration_ms":0,
           "error_message":"Task failed. Inspect administrator diagnostics for details.","id":"2"}],
         "page":{"next_cursor":"cursor-2","has_more":true}}
        """;
    internal const string MetricsJson = """{"total":0,"due":0,"leased":0,"reason_counts":[],"attempt_buckets":[],"due_samples":[],"recent_errors":[]}""";
    internal const string ScheduleJson = """{"task_key":"fixture","triggers":[{"type":"interval","interval_ms":1000}]}""";

    private sealed class Fixture : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly AdminTasksApi Api;
        public readonly List<HttpRequestMessage> Sent = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => AdminMonitorKit.Json(TaskJson);

        public Fixture()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminTasksApi(Kit.Client);
            Kit.Http.Respond = (request, _) =>
            {
                lock (Sent) Sent.Add(request);
                return Task.FromResult(Respond(request));
            };
        }

        public MonitorAuthorityLease Lease() => Authority.Capture();
        public void Dispose() { Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Tagged(string json, string etag, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(json, status);
        response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail = "internal detail with /srv/secret/path") =>
        new(status)
        {
            Content = new StringContent($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"Problem","status":{{(int)status}},"detail":"{{detail}}"}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        };

    [Fact]
    public async Task ListParsesTheOfficialFixtureWithoutHiddenTasks()
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(TasksJson) };
        using var lease = fixture.Lease();
        var tasks = await fixture.Api.ListAsync(lease);
        var task = Assert.Single(tasks);
        Assert.Equal(("fixture", "Fixture task", "system", "idle", false), (task.Key, task.Name, task.Category, task.State, task.ManualOnly));
        Assert.Equal(1000, Assert.Single(task.Triggers).IntervalMs);
        Assert.Equal("GET /api/v2/admin/tasks", Assert.Single(fixture.Kit.Http.Requests));
    }

    [Theory]
    [InlineData("plugin:7:sync-feed", "/api/v2/admin/tasks/plugin%3A7%3Async-feed")]
    [InlineData("rebuild_catalog_search_index", "/api/v2/admin/tasks/rebuild_catalog_search_index")]
    public async Task DetailEscapesTheKeyAsOnePathSegment(string key, string path)
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(TaskJson.Replace("\"fixture\"", JsonSerializer.Serialize(key))) };
        using var lease = fixture.Lease();
        var task = await fixture.Api.GetAsync(lease, key);
        Assert.Equal(key, task.Key);
        Assert.Equal(path, Assert.Single(fixture.Sent).RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("a b")]
    [InlineData("a%2Fb")]
    public async Task UnsafeKeysAreRejectedBeforeAnyRequest(string key)
    {
        using var fixture = new Fixture();
        using var lease = fixture.Lease();
        Assert.False(AdminTasksApi.IsTaskKey(key));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.GetAsync(lease, key));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.RunAsync(lease, key));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.UpdateScheduleAsync(lease, key, [], "\"r1\""));
        Assert.Empty(fixture.Sent);
    }

    [Fact]
    public async Task DuplicateOrMalformedTasksAreRejected()
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json("{\"items\":[" + TaskJson + "," + TaskJson + "]}") };
        using var lease = fixture.Lease();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.ListAsync(lease));
        fixture.Respond = _ => AdminMonitorKit.Json("{\"items\":[{\"key\":\"x/y\",\"name\":\"n\",\"category\":\"system\",\"state\":\"idle\",\"progress\":0,\"triggers\":[]}]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.ListAsync(lease));
        fixture.Respond = _ => AdminMonitorKit.Json("{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.ListAsync(lease));
    }

    [Fact]
    public async Task DetailNotFoundIsAClassifiedProblemWithoutServerText()
    {
        using var fixture = new Fixture { Respond = _ => Problem(HttpStatusCode.NotFound, "not_found") };
        using var lease = fixture.Lease();
        var error = await Assert.ThrowsAsync<ApiException>(() => fixture.Api.GetAsync(lease, "gone"));
        Assert.Equal(AdminFailureKind.NotFound, AdminFailures.Classify(error, lease));
        Assert.DoesNotContain("/srv/secret", error.Message + error.ProblemDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAndCancelPostOnceWithoutAuthenticationReplay()
    {
        using var fixture = new Fixture { Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        using var lease = fixture.Lease();
        await Assert.ThrowsAsync<ApiException>(() => fixture.Api.RunAsync(lease, "fixture"));
        await Assert.ThrowsAsync<ApiException>(() => fixture.Api.CancelAsync(lease, "fixture"));
        Assert.Equal(["POST /api/v2/admin/tasks/fixture/run", "POST /api/v2/admin/tasks/fixture/cancel"], fixture.Kit.Http.Requests);
    }

    [Fact]
    public async Task RunReturnsTheValidatedAcknowledgement()
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(TaskJson.Replace("\"idle\"", "\"running\"")) };
        using var lease = fixture.Lease();
        var task = await fixture.Api.RunAsync(lease, "fixture");
        Assert.Equal(AdminTaskStates.Running, task.State);
    }

    [Fact]
    public async Task ConflictIsARejectionButServerFailuresAndForeignAcknowledgementsAreUnknown()
    {
        using var fixture = new Fixture { Respond = _ => Problem(HttpStatusCode.Conflict, "conflict") };
        using var lease = fixture.Lease();
        var conflict = await Assert.ThrowsAsync<ApiException>(() => fixture.Api.RunAsync(lease, "fixture"));
        Assert.Equal(AdminFailureKind.Conflict, AdminFailures.Classify(conflict, lease));
        fixture.Respond = _ => Problem(HttpStatusCode.BadGateway, "dependency_unavailable");
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.RunAsync(lease, "fixture"));
        fixture.Respond = _ => throw new HttpRequestException("synthetic transport failure");
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.CancelAsync(lease, "fixture"));
        fixture.Respond = _ => AdminMonitorKit.Json(TaskJson.Replace("\"fixture\"", "\"other\""));
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.RunAsync(lease, "fixture"));
        fixture.Respond = _ => AdminMonitorKit.Json("{\"key\":");
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.CancelAsync(lease, "fixture"));
    }

    [Fact]
    public async Task ScheduleReadCapturesOnlyAStrongValidator()
    {
        using var fixture = new Fixture { Respond = _ => Tagged(ScheduleJson, "\"revision-1\"") };
        using var lease = fixture.Lease();
        var schedule = await fixture.Api.GetScheduleAsync(lease, "fixture");
        Assert.Equal("\"revision-1\"", schedule.ETag);
        Assert.Equal(1000, Assert.Single(schedule.Triggers).IntervalMs);
        Assert.DoesNotContain("revision-1", schedule.ToString(), StringComparison.Ordinal);
        fixture.Respond = _ => Tagged(ScheduleJson, "W/\"revision-1\"");
        await Assert.ThrowsAsync<AdminTaskScheduleRevisionException>(() => fixture.Api.GetScheduleAsync(lease, "fixture"));
        fixture.Respond = _ => AdminMonitorKit.Json(ScheduleJson);
        await Assert.ThrowsAsync<AdminTaskScheduleRevisionException>(() => fixture.Api.GetScheduleAsync(lease, "fixture"));
        fixture.Respond = _ => Tagged(ScheduleJson.Replace("\"fixture\"", "\"other\""), "\"revision-1\"");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetScheduleAsync(lease, "fixture"));
    }

    [Fact]
    public async Task ScheduleWriteSendsIfMatchAndOmitsAbsentFields()
    {
        using var fixture = new Fixture { Respond = _ => Tagged("{\"task_key\":\"fixture\",\"triggers\":[]}", "\"revision-2\"") };
        using var lease = fixture.Lease();
        var saved = await fixture.Api.UpdateScheduleAsync(lease, "fixture",
            [new AdminTaskTrigger("interval", 3000, null, null, null), new AdminTaskTrigger("weekly", null, "05:30", 0, 60000)], "\"revision-1\"");
        Assert.Equal("\"revision-2\"", saved.ETag);
        var request = Assert.Single(fixture.Sent);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("\"revision-1\"", Assert.Single(request.Headers.IfMatch).ToString());
        Assert.Equal("""{"triggers":[{"type":"interval","interval_ms":3000},{"type":"weekly","time_of_day":"05:30","day_of_week":0,"max_runtime_ms":60000}]}""",
            fixture.Kit.Http.Bodies.Single());
    }

    [Fact]
    public async Task EmptyScheduleIsSentAsAnEmptyArray()
    {
        using var fixture = new Fixture { Respond = _ => Tagged("{\"task_key\":\"fixture\",\"triggers\":[]}", "\"revision-2\"") };
        using var lease = fixture.Lease();
        await fixture.Api.UpdateScheduleAsync(lease, "fixture", [], "\"revision-1\"");
        Assert.Equal("""{"triggers":[]}""", fixture.Kit.Http.Bodies.Single());
    }

    [Fact]
    public async Task ScheduleWriteNeverReplaysAndClassifiesEveryUncertainty()
    {
        using var fixture = new Fixture { Respond = _ => Problem(HttpStatusCode.PreconditionFailed, "precondition_failed") };
        using var lease = fixture.Lease();
        IReadOnlyList<AdminTaskTrigger> triggers = [new("startup", null, null, null, null)];
        await Assert.ThrowsAsync<AdminTaskScheduleConflictException>(() => fixture.Api.UpdateScheduleAsync(lease, "fixture", triggers, "\"revision-1\""));
        fixture.Respond = _ => Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.UpdateScheduleAsync(lease, "fixture", triggers, "\"revision-1\""));
        fixture.Respond = _ => AdminMonitorKit.Json("{\"task_key\":\"fixture\",\"triggers\":[]}");
        await Assert.ThrowsAsync<AdminTaskOutcomeUnknownException>(() => fixture.Api.UpdateScheduleAsync(lease, "fixture", triggers, "\"revision-1\""));
        fixture.Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<ApiException>(() => fixture.Api.UpdateScheduleAsync(lease, "fixture", triggers, "\"revision-1\""));
        Assert.Equal(4, fixture.Kit.Http.Requests.Count);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Api.UpdateScheduleAsync(lease, "fixture", triggers, "W/\"weak\""));
        Assert.Equal(4, fixture.Kit.Http.Requests.Count);
    }

    [Fact]
    public async Task HistoryReadsCursorPagesAndValidatesTheChain()
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(HistoryJson) };
        using var lease = fixture.Lease();
        var page = await fixture.Api.GetHistoryAsync(lease, "fixture", null);
        Assert.Equal(["3", "2"], page.Items.Select(item => item.Id));
        Assert.True(page.HasMore);
        Assert.Equal("cursor-2", page.NextCursor);
        Assert.Equal("Task failed. Inspect administrator diagnostics for details.", page.Items[0].ErrorMessage);
        Assert.DoesNotContain("cursor-2", page.ToString(), StringComparison.Ordinal);
        await fixture.Api.GetHistoryAsync(lease, "fixture", "cursor-1");
        Assert.Equal(["/api/v2/admin/tasks/fixture/history?limit=20", "/api/v2/admin/tasks/fixture/history?limit=20&cursor=cursor-1"],
            fixture.Sent.Select(request => request.RequestUri!.PathAndQuery));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetHistoryAsync(lease, "fixture", "cursor-2"));
        fixture.Respond = _ => AdminMonitorKit.Json(HistoryJson.Replace("\"id\":\"2\"", "\"id\":\"3\""));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetHistoryAsync(lease, "fixture", null));
        fixture.Respond = _ => AdminMonitorKit.Json(HistoryJson.Replace("\"next_cursor\":\"cursor-2\",", ""));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetHistoryAsync(lease, "fixture", null));
        fixture.Respond = _ => AdminMonitorKit.Json(HistoryJson.Replace("\"task_key\":\"fixture\"", "\"task_key\":\"other\""));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetHistoryAsync(lease, "fixture", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Api.GetHistoryAsync(lease, "fixture", null, 201));
    }

    [Fact]
    public async Task HistoryKeepsTypedResultsAndStepsOnly()
    {
        const string json = """
            {"items":[{"task_key":"contribute_markers","started_at":"2026-01-02T03:04:05Z","completed_at":"2026-01-02T03:05:05Z","status":"completed","duration_ms":60000,
              "id":"9","result_data":{"submitted":4,"skipped":1,"invalid":0,"failed":2,"retry_after_seconds":30},
              "steps":[{"key":"activity","name":"Activity log","status":"failed"}]}],"page":{"has_more":false}}
            """;
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(json) };
        using var lease = fixture.Lease();
        var item = Assert.Single((await fixture.Api.GetHistoryAsync(lease, "contribute_markers", null)).Items);
        Assert.Equal(new AdminTaskMarkerResult(4, 1, 0, 2, 30), item.Result);
        Assert.Equal("Activity log", Assert.Single(item.Steps).Name);
    }

    [Fact]
    public async Task MetricsAreReadOnlyForRefreshMetadataAndCarryNoRawErrors()
    {
        const string json = """
            {"total":12,"due":3,"leased":1,"oldest_due_at":"2026-11-14T10:00:00Z","reason_counts":[{"reason":"refresh_failure","count":2}],
             "attempt_buckets":[{"label":"1-2","count":5}],"due_samples":[{"target_type":"item","content_id":"42","title":"Synthetic","type":"movie",
             "reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":2,"last_error":""}],
             "recent_errors":[{"target_type":"item","content_id":"43","title":"","type":"","reason_mask":8,"next_refresh_at":"2026-11-14T11:00:00Z",
             "attempt_count":1,"last_error":"Refresh failed. Inspect administrator diagnostics for details."}]}
            """;
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(json) };
        using var lease = fixture.Lease();
        var metrics = await fixture.Api.GetMetricsAsync(lease, "refresh_metadata");
        Assert.Equal((12, 3, 1), (metrics.Total, metrics.Due, metrics.Leased));
        Assert.Equal("refresh_failure", Assert.Single(metrics.ReasonCounts).Reason);
        Assert.Equal("42", Assert.Single(metrics.DueSamples).ContentId);
        Assert.Equal("Refresh failed. Inspect administrator diagnostics for details.", Assert.Single(metrics.RecentErrors).LastError);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.GetMetricsAsync(lease, "scan_libraries"));
        Assert.Single(fixture.Sent);
        fixture.Respond = _ => AdminMonitorKit.Json(MetricsJson);
        Assert.Empty((await fixture.Api.GetMetricsAsync(lease, "refresh_metadata")).DueSamples);
    }

    [Fact]
    public async Task MetricSamplesCarryTheOptionalSeriesSeasonAndEpisodeParts()
    {
        const string json = """
            {"total":1,"due":1,"leased":0,"reason_counts":[],"attempt_buckets":[],
             "due_samples":[
              {"target_type":"item","content_id":"1","title":"Pilot","type":"episode","series_title":"Synthetic Show","season_number":2,"episode_number":5,
               "reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
              {"target_type":"item","content_id":"2","title":"","type":"season","series_title":"Synthetic Show","season_number":0,
               "reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
              {"target_type":"item","content_id":"3","title":"Plain","type":"movie",
               "reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
              {"target_type":"item","content_id":"4","title":"Odd","type":"episode","season_number":-1,"episode_number":9999999999,
               "reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""}],
             "recent_errors":[]}
            """;
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(json) };
        using var lease = fixture.Lease();
        var samples = (await fixture.Api.GetMetricsAsync(lease, "refresh_metadata")).DueSamples;
        Assert.Equal("Synthetic Show", samples[0].SeriesTitle);
        Assert.Equal(2, samples[0].SeasonNumber);
        Assert.Equal(5, samples[0].EpisodeNumber);
        Assert.Equal("Synthetic Show", samples[1].SeriesTitle);
        Assert.Equal(0, samples[1].SeasonNumber);
        Assert.Null(samples[1].EpisodeNumber);
        Assert.Equal("", samples[2].SeriesTitle);
        Assert.Null(samples[2].SeasonNumber);
        Assert.Null(samples[2].EpisodeNumber);
        // A number outside the integer range or below zero is treated as absent rather than failing the whole read.
        Assert.Null(samples[3].SeasonNumber);
        Assert.Null(samples[3].EpisodeNumber);
        Assert.Equal("Synthetic Show - S02E05 Pilot", AdminTasksText.SampleTitle(samples[0]));
        Assert.Equal("Synthetic Show - Specials", AdminTasksText.SampleTitle(samples[1]));
        Assert.Equal("Plain", AdminTasksText.SampleTitle(samples[2]));
        Assert.Equal("Odd", AdminTasksText.SampleTitle(samples[3]));
        Assert.Equal("GET", Assert.Single(fixture.Sent).Method.Method);
    }

    private static string SampleJson(string fields) => $$"""
        {"total":1,"due":1,"leased":0,"reason_counts":[],"attempt_buckets":[],
         "due_samples":[{"target_type":"item","content_id":"1",{{fields}},"reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""}],
         "recent_errors":[]}
        """;

    private static async Task<AdminTaskDebtSample> ReadSampleAsync(string fields)
    {
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(SampleJson(fields)) };
        using var lease = fixture.Lease();
        return Assert.Single((await fixture.Api.GetMetricsAsync(lease, "refresh_metadata")).DueSamples);
    }

    [Theory]
    [InlineData("\"title\":\"T\",\"series_title\":5", "", null, null)]
    [InlineData("\"title\":\"T\",\"series_title\":[\"x\"]", "", null, null)]
    [InlineData("\"title\":\"T\",\"series_title\":{\"a\":1}", "", null, null)]
    [InlineData("\"title\":\"T\",\"series_title\":null", "", null, null)]
    [InlineData("\"title\":\"T\",\"series_title\":\"Show\",\"season_number\":2.5", "Show", null, null)]
    [InlineData("\"title\":\"T\",\"season_number\":1e30", "", null, null)]
    [InlineData("\"title\":\"T\",\"season_number\":\"2\",\"episode_number\":3", "", null, 3)]
    [InlineData("\"title\":\"T\",\"season_number\":4,\"episode_number\":{\"n\":1}", "", 4, null)]
    [InlineData("\"title\":\"T\",\"season_number\":4,\"episode_number\":-3", "", 4, null)]
    [InlineData("\"title\":\"T\",\"season_number\":4,\"episode_number\":2.5", "", 4, null)]
    [InlineData("\"title\":\"T\",\"season_number\":true,\"episode_number\":false", "", null, null)]
    [InlineData("\"title\":\"T\",\"season_number\":0,\"episode_number\":7", "", 0, 7)]
    public async Task AMalformedOptionalPartIsAbsentAndDoesNotFailTheMetricsRead(string fields, string series, int? season, int? episode)
    {
        var sample = await ReadSampleAsync(fields);
        Assert.Equal("T", sample.Title);
        Assert.Equal((series, season, episode), (sample.SeriesTitle, sample.SeasonNumber, sample.EpisodeNumber));
    }

    // Each case is the JSON text of one title: the escapes are decoded by the JSON reader, so these are real control and format characters.
    [Theory]
    [InlineData("a\\u0007b", "ab")]                              // control (Cc)
    [InlineData("a\\u0000b", "ab")]
    [InlineData("a\\nb\\tc\\r\\nd", "a b c d")]                    // line breaks and tabs become one space
    [InlineData("a\\u0085b", "a b")]                             // next line
    [InlineData("a\\u2028b", "a b")]                             // line separator (Zl)
    [InlineData("a\\u2029b", "a b")]                             // paragraph separator (Zp)
    [InlineData("a    b   ", "a b")]
    [InlineData("\\u202Eevil\\u202C", "evil")]                   // right-to-left override
    [InlineData("a\\u202Ab\\u202Bc\\u202Dd", "abcd")]            // embeddings and left-to-right override
    [InlineData("a\\u2066b\\u2067c\\u2068d\\u2069e", "abcde")]   // isolates
    [InlineData("a\\u200Eb\\u200Fc\\u061Cd", "abcd")]            // direction marks and Arabic letter mark
    [InlineData("a\\u200Bb\\uFEFFc\\u00ADd\\u2060e", "abcde")]   // zero-width space, byte order mark, soft hyphen, word joiner
    [InlineData("a\\uE000b", "ab")]                              // private use
    [InlineData("Plain title", "Plain title")]
    public async Task ServerTitlesAreOneLineWithNoControlSeparatorOrDirectionCharacter(string json, string expected)
    {
        var sample = await ReadSampleAsync($"\"title\":\"{json}\",\"type\":\"{json}\",\"series_title\":\"{json}\",\"season_number\":1,\"episode_number\":2");
        Assert.Equal(expected, sample.Title);
        Assert.Equal(expected, sample.SeriesTitle);
        Assert.Equal(expected, sample.Type);
        Assert.Equal($"{expected} - S01E02 {expected}", AdminTasksText.SampleTitle(sample));
    }

    [Fact]
    public async Task ServerTitlesCannotInjectADirectionCharacterOrLineBreakIntoTheComposedSentence()
    {
        var sample = await ReadSampleAsync("\"title\":\"x\\u202Ey\\u2028z\",\"series_title\":\"\\u2067s\\u2029\",\"season_number\":1,\"episode_number\":2");
        var composed = AdminTasksText.SampleTitle(sample);
        Assert.Equal("s - S01E02 xy z", composed);
        Assert.DoesNotContain(composed.AsEnumerable(), character => char.IsControl(character) || character is '\u2028' or '\u2029' or (>= '\u200B' and <= '\u200F') or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069'));
    }

    [Fact]
    public async Task AContentIdShownInPlaceOfAMissingTitleIsNeutralizedToo()
    {
        using var fixture = new Fixture
        {
            Respond = _ => AdminMonitorKit.Json("""
                {"total":1,"due":1,"leased":0,"reason_counts":[],"attempt_buckets":[],
                 "due_samples":[{"target_type":"item","content_id":"id\u202E-\u2028one","title":"","type":"movie","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
                                {"target_type":"item","content_id":"\u202E\u2028","title":"","type":"movie","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""}],
                 "recent_errors":[]}
                """),
        };
        using var lease = fixture.Lease();
        // A content ID with nothing left after the reduction is not an identity: the read is refused, as for any other invalid sample.
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Api.GetMetricsAsync(lease, "refresh_metadata"));
        fixture.Respond = _ => AdminMonitorKit.Json("""
            {"total":1,"due":1,"leased":0,"reason_counts":[],"attempt_buckets":[],
             "due_samples":[{"target_type":"item","content_id":"id\u202E-\u2028one","title":"","type":"movie","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""}],
             "recent_errors":[]}
            """);
        var sample = Assert.Single((await fixture.Api.GetMetricsAsync(lease, "refresh_metadata")).DueSamples);
        Assert.Equal("id- one", AdminTasksText.SampleTitle(sample));
    }

    [Theory]
    [InlineData("\\ud83d\\udc68\\u200d\\ud83d\\udc69\\u200d\\ud83d\\udc67", "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67")]   // an emoji family keeps its joiners
    [InlineData("\\u0645\\u06cc\\u200c\\u062e\\u0648\\u0627\\u0647\\u0645", "\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")]       // Persian keeps its non-joiner
    [InlineData("\\u200da\\u200d\\u200db\\u200d", "a\u200Db")]          // a joiner joins only after a visible character, once, never at an end
    [InlineData("a \\u200d b", "a b")]
    [InlineData("\\u200d\\u200c", "")]
    public async Task JoinersThatJoinCharactersAreKeptAndStrayOnesAreDropped(string json, string expected) =>
        Assert.Equal(expected, (await ReadSampleAsync($"\"title\":\"{json}\"")).Title);

    [Fact]
    public async Task ABoundedTitleIsCutAtAWholeCharacterNeverInsideASurrogatePairOrAJoinedSequence()
    {
        var family = "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67";
        var sample = await ReadSampleAsync($"\"title\":\"{new string('a', 299) + family + "tail"}\"");
        Assert.Equal(new string('a', 299) + family + "…", sample.Title);

        sample = await ReadSampleAsync($"\"title\":\"{new string('a', 299) + "\uD83D\uDE00\uD83D\uDE00"}\"");
        Assert.Equal(new string('a', 299) + "\uD83D\uDE00…", sample.Title);
        Assert.Equal(302, sample.Title.Length); // 299 letters, one emoji (two UTF-16 units) and the cut mark

        var exact = new string('a', 299) + "\uD83D\uDE00";
        Assert.Equal(exact, (await ReadSampleAsync($"\"title\":\"{exact}\"")).Title);
    }

    [Fact]
    public async Task BoundNeverSplitsASurrogatePair()
    {
        // 63 letters and an emoji: the 64-unit cut would fall between its two UTF-16 units.
        var target = new string('t', 63) + "\uD83D\uDE00" + "x";
        var sample = await ReadSampleAsync($"\"target_type\":\"{target}\",\"title\":\"T\"");
        Assert.Equal(new string('t', 63) + "…", sample.TargetType);
        Assert.All(sample.TargetType, character => Assert.False(char.IsSurrogate(character)));
        var fits = new string('t', 62) + "\uD83D\uDE00";
        Assert.Equal(fits, (await ReadSampleAsync($"\"target_type\":\"{fits}\",\"title\":\"T\"")).TargetType);
    }

    [Fact]
    public async Task StepNamesAreNeutralizedLikeTitles()
    {
        const string json = """
            {"items":[{"task_key":"fixture","started_at":"2026-01-02T03:04:05Z","completed_at":"2026-01-02T03:05:05Z","status":"failed","duration_ms":1,"id":"9",
              "steps":[{"key":"activity","name":"\u202EActivity\u2028log\u0007","status":"failed"},{"key":"fonts","name":"Fam\uD83D\uDC68\u200D\uD83D\uDC69 \u200B","status":"failed"}]}],
             "page":{"has_more":false}}
            """;
        using var fixture = new Fixture { Respond = _ => AdminMonitorKit.Json(json) };
        using var lease = fixture.Lease();
        var item = Assert.Single((await fixture.Api.GetHistoryAsync(lease, "fixture", null)).Items);
        Assert.Equal(["Activity log", "Fam\uD83D\uDC68\u200D\uD83D\uDC69"], item.Steps.Select(step => step.Name).ToArray());
    }

    [Fact]
    public async Task RetiredLeaseSendsNothing()
    {
        using var fixture = new Fixture();
        var lease = fixture.Lease();
        lease.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Api.ListAsync(lease));
        Assert.Empty(fixture.Sent);
    }
}
