using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminDashboardApiTests : IDisposable
{
    private readonly AdminMonitorKit _kit = AdminMonitorKit.Create();
    private readonly AdminMonitorAuthority _authority;
    private readonly AdminMonitorApi _api;

    public AdminDashboardApiTests()
    {
        _authority = new AdminMonitorAuthority(_kit.Client, _kit.Auth);
        _api = new AdminMonitorApi(_kit.Client);
    }

    public void Dispose()
    {
        _authority.Dispose();
        _kit.Dispose();
    }

    private static HttpResponseMessage WithETag(HttpResponseMessage response, string etag)
    {
        response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
        return response;
    }

    [Fact]
    public async Task ReadsUseTheAllowlistedPathsAndServerClamps()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));
        using var lease = _authority.Capture();
        await _api.GetDashboardStatsAsync(lease, refresh: true);
        await _api.GetDashboardTimeseriesAsync(lease, 720);
        await _api.GetDashboardTimeseriesAsync(lease, 5000);
        await _api.GetDashboardPlaybackActivityAsync(lease, 0);
        await _api.GetDashboardTopActivityAsync(lease, 90);
        await _api.GetDashboardDownloadsStatsAsync(lease);
        await _api.GetDashboardApiClientsAsync(lease, 744);
        await _api.GetDashboardServerStatusAsync(lease);
        await _api.GetDashboardResourcesAsync(lease);
        await _api.GetDashboardAutoscanStatusAsync(lease);
        await _api.ListDashboardScanHistoryAsync(lease);
        await _api.GetDashboardCapabilitiesAsync(lease);
        Assert.Equal(
        [
            "GET /api/v2/admin/stats?refresh=true",
            "GET /api/v2/admin/stats/timeseries?hours=720",
            "GET /api/v2/admin/stats/timeseries?hours=744",
            "GET /api/v2/admin/stats/playback-activity?hours=1",
            "GET /api/v2/admin/stats/top-activity?days=30",
            "GET /api/v2/admin/stats/downloads?limit=10",
            "GET /api/v2/admin/stats/api-clients?hours=720&limit=10",
            "GET /api/v2/admin/server/status",
            "GET /api/v2/admin/system/resources",
            "GET /api/v2/admin/autoscan/status",
            "GET /api/v2/admin/autoscan/scans?limit=8",
            "GET /api/v2/admin/dashboard/capabilities",
        ], _kit.Http.Requests);
    }

    [Fact]
    public async Task RecentErrorsAskForBothLevelsAndEndpoints404IsAnEmptyDetail()
    {
        _kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/endpoints")
            ? AdminMonitorKit.Json("""{"title":"Not Found","status":404}""", HttpStatusCode.NotFound)
            : AdminMonitorKit.Json("""{"items":[{"id":"9007199254740993","level":"error","component":"scan","message":"m"}],"page":{"has_more":false}}"""));
        using var lease = _authority.Capture();
        var errors = await _api.ListDashboardRecentErrorsAsync(lease);
        Assert.Equal("9007199254740993", errors.Single().Id);
        var endpoints = await _api.GetDashboardApiClientEndpointsAsync(lease, "a/b", 168);
        Assert.Empty(endpoints.Endpoints!);
        Assert.Equal(["GET /api/v2/admin/logs/app?level=error%2Cwarn&limit=8", "GET /api/v2/admin/stats/api-clients/a%2Fb/endpoints?hours=24"], _kit.Http.Requests);
    }

    [Fact]
    public async Task DrainsNodesAndUsersWithStrictCursorsAndNoPartialPublication()
    {
        var pages = new Queue<string>([
            """{"items":[{"id":"1","name":"a"}],"page":{"has_more":true,"next_cursor":"c1"}}""",
            """{"items":[],"page":{"has_more":true,"next_cursor":"c2"}}""",
            """{"items":[{"id":"1","name":"dup"},{"id":"2","name":"b"}],"page":{"has_more":false}}""",
        ]);
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(pages.Dequeue()));
        using var lease = _authority.Capture();
        var nodes = await _api.ListDashboardNodesAsync(lease);
        Assert.Equal(["1", "2"], nodes.Select(n => n.Id));
        Assert.Equal("GET /api/v2/admin/nodes?limit=200&cursor=c2", _kit.Http.Requests.Last());

        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"1"}],"page":{"has_more":true,"next_cursor":"same"}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.ListDashboardUsersAsync(lease));
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"1"}],"page":{"has_more":true}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.ListDashboardLibrariesAsync(lease));
    }

    [Fact]
    public async Task LayoutReadsAreConditionalAndRequireAStrongValidator()
    {
        _kit.Http.Respond = (request, _) => Task.FromResult(request.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : WithETag(AdminMonitorKit.Json("""{"layout":{"version":1,"entries":[]},"updated_at":"2026-11-14T00:00:00Z"}"""), "\"L1\""));
        using var lease = _authority.Capture();
        var read = await _api.GetDashboardLayoutAsync(lease, null);
        Assert.Equal("\"L1\"", read.Value!.ETag);
        Assert.Equal(1, read.Value.Layout!.Value.GetProperty("version").GetInt32());
        Assert.True((await _api.GetDashboardLayoutAsync(lease, "\"L1\"")).NotModified);

        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"layout":null,"updated_at":null}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.GetDashboardLayoutAsync(lease, null));
        _kit.Http.Respond = (_, _) => Task.FromResult(WithETag(AdminMonitorKit.Json("""{"layout":null,"updated_at":null}"""), "W/\"weak\""));
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.GetDashboardLayoutAsync(lease, null));
    }

    [Fact]
    public async Task LayoutSaveSendsIfMatchAndReturnsTheCommittedValidatorFromA204()
    {
        _kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("\"A\"", request.Headers.IfMatch.Single().ToString());
            return Task.FromResult(WithETag(new HttpResponseMessage(HttpStatusCode.NoContent), "\"B\""));
        };
        using var lease = _authority.Capture();
        Assert.Equal("\"B\"", await _api.SaveDashboardLayoutAsync(lease, """{"version":1,"entries":[]}""", "\"A\""));
        Assert.Equal("""{"layout":{"version":1,"entries":[]}}""", _kit.Http.Bodies.Last());

        _kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Assert.ThrowsAsync<InvalidDataException>(() => _api.SaveDashboardLayoutAsync(lease, "{}", "\"A\""));
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SaveDashboardLayoutAsync(lease, "{}", "*"));
        await Assert.ThrowsAsync<ArgumentException>(() => _api.SaveDashboardLayoutAsync(lease, "{}", "W/\"A\""));
    }

    [Theory]
    [InlineData(412, "\"C\"")]
    [InlineData(428, null)]
    public async Task LayoutPreconditionFailuresSurfaceTheCurrentValidatorWithoutRetrying(int status, string? current)
    {
        _kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json("""{"title":"Stale","status":412}""", (HttpStatusCode)status);
            if (current != null) WithETag(response, current);
            return Task.FromResult(response);
        };
        using var lease = _authority.Capture();
        var error = await Assert.ThrowsAsync<DashboardLayoutConflictException>(() => _api.SaveDashboardLayoutAsync(lease, "{}", "\"A\""));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(current, error.CurrentETag);
        Assert.Single(_kit.Http.Requests);
    }

    [Fact]
    public async Task ResetTreatsAnAbsentLayoutAsSuccessAndActionsUseExactBodies()
    {
        _kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Delete && request.RequestUri!.AbsolutePath.EndsWith("/layout")
            ? AdminMonitorKit.Json("""{"title":"Not Found","status":404}""", HttpStatusCode.NotFound)
            : AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.Contains("login-sessions") ? "" : "{}",
                request.RequestUri.AbsolutePath.Contains("login-sessions") ? HttpStatusCode.NoContent : HttpStatusCode.OK));
        using var lease = _authority.Capture();
        await _api.ResetDashboardLayoutAsync(lease);
        await _api.StartDashboardLibraryScanAsync(lease, "12");
        await _api.CancelDashboardLibraryScansAsync(lease, "12");
        await _api.ScanAllDashboardLibrariesAsync(lease);
        await _api.RevokeDashboardLoginSessionAsync(lease, "7", "s/1");
        Assert.Equal(
        [
            "DELETE /api/v2/admin/dashboard/layout",
            "POST /api/v2/scan",
            "POST /api/v2/scan/cancel",
            "POST /api/v2/admin/tasks/scan_libraries/run",
            "DELETE /api/v2/admin/users/7/login-sessions/s%2F1",
        ], _kit.Http.Requests);
        Assert.Equal(["", """{"library_id":"12"}""", """{"library_id":"12"}""", "", ""], _kit.Http.Bodies);
    }

    [Fact]
    public async Task AttentionSettingsProjectOnlyTheReadKeysAndTheHiddenNoticeWriteIsGuarded()
    {
        _kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? WithETag(AdminMonitorKit.Json("""{"key":"ui.dashboard.hidden_attention","value":"[\"email\"]"}"""), "\"S2\"")
            : WithETag(AdminMonitorKit.Json("""{"email.smtp_host":"smtp.test","oauth.secret":"redacted-synthetic","server.public_url":""}"""), "\"S1\""));
        using var lease = _authority.Capture();
        var settings = await _api.GetDashboardAttentionSettingsAsync(lease);
        Assert.Equal("\"S1\"", settings.ETag);
        Assert.Equal(["email.smtp_host", "server.public_url"], settings.Values.Keys.Order());
        await _api.SetDashboardHiddenAttentionAsync(lease, "[\"email\"]", settings.ETag);
        Assert.Equal("PUT /api/v2/admin/settings/ui.dashboard.hidden_attention", _kit.Http.Requests.Last());
        using var body = System.Text.Json.JsonDocument.Parse(_kit.Http.Bodies.Last());
        Assert.Equal("[\"email\"]", body.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task AttentionSettingsProjectMixedScalarsAndIgnoreNonScalarOrUnrelatedValues()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(WithETag(AdminMonitorKit.Json(
            """{"database.max_connections":100,"playback.transcode_enabled":true,"email.enabled":false,"email.smtp_host":"smtp.test","server.public_url":"","s3.public_bucket":null,"s3.private_bucket":{},"redis.url":[],"unrelated.number":42,"unrelated.boolean":true,"unrelated.string":"ignored"}"""), "\"S1\""));
        using var lease = _authority.Capture();
        var settings = await _api.GetDashboardAttentionSettingsAsync(lease);
        Assert.Equal("\"S1\"", settings.ETag);
        Assert.Equal(5, settings.Values.Count);
        Assert.Equal("100", settings.Values["database.max_connections"]);
        Assert.Equal("true", settings.Values["playback.transcode_enabled"]);
        Assert.Equal("false", settings.Values["email.enabled"]);
        Assert.Equal("smtp.test", settings.Values["email.smtp_host"]);
        Assert.Equal("", settings.Values["server.public_url"]);
    }

    [Fact]
    public async Task AuthorityLostDuringAResponseNeverPublishes()
    {
        var gate = new TaskCompletionSource();
        _kit.Http.Respond = async (_, _) =>
        {
            await gate.Task;
            return AdminMonitorKit.Json("""{"total_users":3}""");
        };
        using var lease = _authority.Capture();
        var read = _api.GetDashboardStatsAsync(lease, refresh: false);
        _kit.SetRole("user");
        gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.False(_authority.IsAvailable);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _api.GetDashboardStatsAsync(lease, refresh: false));
        Assert.Single(_kit.Http.Requests);
    }

    [Fact]
    public async Task OrdinaryUsersAndUnresolvedProfilesCannotCaptureDashboardAuthority()
    {
        using var user = AdminMonitorKit.Create(role: "user");
        using var userAuthority = new AdminMonitorAuthority(user.Client, user.Auth);
        Assert.ThrowsAny<OperationCanceledException>(() => userAuthority.Capture());
        using var unresolved = AdminMonitorKit.Create(resolvedProfile: false);
        using var unresolvedAuthority = new AdminMonitorAuthority(unresolved.Client, unresolved.Auth);
        Assert.ThrowsAny<OperationCanceledException>(() => unresolvedAuthority.Capture());
        using var secondary = AdminMonitorKit.Create(primary: false);
        using var secondaryAuthority = new AdminMonitorAuthority(secondary.Client, secondary.Auth);
        Assert.ThrowsAny<OperationCanceledException>(() => secondaryAuthority.Capture());
        Assert.Empty(user.Http.Requests);
    }

    [Fact]
    public async Task DashboardModelsKeepStringIdsAndNullableReadings()
    {
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(
            """{"available":true,"stale":false,"system":{"cpu_pct":12,"cores":8,"load1":1.5,"mem_used_mb":512,"mem_total_mb":2048,"disks":[]},"gpu":[]}"""));
        using var lease = _authority.Capture();
        var resources = await _api.GetDashboardResourcesAsync(lease);
        Assert.Null(resources.System!.NetRxBps);
        Assert.Equal(12, resources.System.CpuPct);
        _kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(
            """{"egress_bytes":5,"egress_sources":[{"key":"node:9007199254740993","kind":"node","name":"n","node_id":"9007199254740993"}],"points":[]}"""));
        var series = await _api.GetDashboardTimeseriesAsync(lease, 24);
        Assert.Equal("9007199254740993", series.EgressSources![0].NodeId);
        Assert.Null(series.DownloadEgressBytes);
    }
}
