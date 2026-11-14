using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLogsApiTests
{
    [Fact]
    public async Task AuthorityRetirementDuringJsonBodyReadDiscardsTheParsedResult()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        using var body = new LogsRetiringBodyStream(System.Text.Encoding.UTF8.GetBytes("{\"items\":[],\"page\":{\"has_more\":false}}"), authority.Retire);
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminMonitorApi(kit.Client).LogsGetOperationalAsync(lease, new()));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task DiagnosticDownloadRejectsTruncatedDeclaredLength()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x1f, 0x8b, 8, 0]) };
            response.Content.Headers.ContentType = new("application/gzip");
            response.Content.Headers.ContentLength = 30;
            return Task.FromResult(response);
        };
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminMonitorApi(kit.Client).DiagnosticsDownloadReportAsync(lease, "r1", destination, 1024));
    }

    [Fact]
    public async Task HistoryUsesExactV2FiltersAndRequiresCursorMetadata()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminMonitorApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"9007199254740993","timestamp":"2026-11-14T12:00:00Z","level":"info","component":"scanner","message":"Scanning"}],"page":{"has_more":false}}"""));
        var page = await api.LogsGetOperationalAsync(lease, new() { Message = "a&b", Component = "ffmpeg" }, "opaque/next", 50);
        Assert.Equal("9007199254740993", Assert.Single(page.Items).Id);
        Assert.Equal("GET /api/v2/admin/logs/app?q=a%26b&component=ffmpeg&cursor=opaque%2Fnext&limit=50", Assert.Single(kit.Http.Requests));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":true}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.LogsGetOperationalAsync(lease, new()));
    }

    [Fact]
    public async Task TicketMintIsSingleAttemptAndNeverRefreshesAuthentication()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"type":"https://silo.test/docs/api/v2/problems/unauthorized","title":"Unauthorized","status":401}""", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminMonitorApi(kit.Client).LogsCreateSocketTicketAsync(lease));
        Assert.Equal(0, refreshes);
        Assert.Equal("POST /api/v2/admin/logs/ws-ticket", Assert.Single(kit.Http.Requests));
    }

    [Fact]
    public async Task RetiredAuthorityCannotPublishAResponseBodyOrIssueAnotherRequest()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            kit.SetRole("user");
            return Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        };
        var api = new AdminMonitorApi(kit.Client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.LogsGetAuditAsync(lease, new()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.LogsGetAuditAsync(lease, new()));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task LoggingBatchWritesOnlyAllowedDirtyKeysWithDisplayedRevision()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminMonitorApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("\"original\"", request.Headers.IfMatch.Single().ToString());
            var response = AdminMonitorKit.Json("""{"values":{"server.log_level":"debug"},"restart_required":false}""");
            response.Headers.ETag = new EntityTagHeaderValue("\"ack\"");
            return Task.FromResult(response);
        };
        var result = await api.LoggingSaveAsync(lease, new(new Dictionary<string, string> { ["server.log_level"] = "debug" }, "\"original\"", new Dictionary<string, long>()));
        Assert.Equal("\"ack\"", result.ETag);
        Assert.Equal("PUT /api/v2/admin/settings", Assert.Single(kit.Http.Requests));
        Assert.Equal("""{"values":{"server.log_level":"debug"}}""", Assert.Single(kit.Http.Bodies));
    }

    [Fact]
    public async Task SettingsConflictDoesNotReplayAndPreservesServerValidator()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed);
            response.Headers.ETag = new EntityTagHeaderValue("\"newer\"");
            return Task.FromResult(response);
        };
        var error = await Assert.ThrowsAsync<LoggingSettingsConflictException>(() => new AdminMonitorApi(kit.Client).DiagnosticsSetUploadsEnabledAsync(lease, false, "\"old\""));
        Assert.Equal("\"newer\"", error.CurrentETag);
        Assert.Single(kit.Http.Requests);
        Assert.Equal("""{"value":"false"}""", Assert.Single(kit.Http.Bodies));
    }

    [Theory]
    [InlineData("application/json", new byte[] { 123, 125 })]
    [InlineData("application/gzip", new byte[] { 123, 125 })]
    public async Task DiagnosticDownloadRejectsJsonAndInvalidGzip(string contentType, byte[] bytes)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new(contentType);
            return Task.FromResult(response);
        };
        using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminMonitorApi(kit.Client).DiagnosticsDownloadReportAsync(lease, "report/1", destination, 1024));
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task DiagnosticDownloadStreamsBoundedBytesAndRetirementCancelsPublication()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        byte[] bytes = [0x1f, 0x8b, 0x08, 1, 2, 3];
        kit.Http.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new("application/gzip");
            return Task.FromResult(response);
        };
        using var destination = new MemoryStream();
        await new AdminMonitorApi(kit.Client).DiagnosticsDownloadReportAsync(lease, "r1", destination, 1024);
        Assert.Equal(bytes, destination.ToArray());
        using var tooSmall = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminMonitorApi(kit.Client).DiagnosticsDownloadReportAsync(lease, "r1", tooSmall, 5));
        Assert.Equal(0, tooSmall.Length);
        authority.Retire();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminMonitorApi(kit.Client).DiagnosticsDownloadReportAsync(lease, "r1", destination, 1024));
        Assert.Equal(2, kit.Http.Requests.Count);
    }
}

internal sealed class LogsRetiringBodyStream(byte[] bytes, Action retire) : MemoryStream(bytes)
{
    private bool _retired;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = base.ReadAsync(buffer, cancellationToken);
        if (!_retired) { _retired = true; retire(); }
        return read;
    }
}
