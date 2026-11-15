using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminServerSettingsApiTests
{
    [Fact]
    public async Task EffectiveFiltersSecretsAndSaveUsesExactValidatorWithoutReplay()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        string? validator = null;
        kit.Http.Respond = (request, _) =>
        {
            validator = request.Headers.IfMatch.ToString();
            var response = request.Method == HttpMethod.Get
                ? AdminMonitorKit.Json("""{"branding.server_name":"Silo","redis.url":"must-not-retain","unowned":"x"}""")
                : AdminMonitorKit.Json("""{"values":{"branding.server_name":"New","redis.url":"must-not-retain"},"restart_required":true}""");
            response.Headers.ETag = new(request.Method == HttpMethod.Get ? "\"old\"" : "\"new\"");
            return Task.FromResult(response);
        };
        var api = new AdminServerSettingsApi(kit.Client);
        var effective = await api.GetEffectiveAsync(lease, ["branding.server_name", "redis.url"], ["redis.url"]);
        Assert.Single(effective.Values);
        var result = await api.PutAsync(lease, new Dictionary<string, string> { ["branding.server_name"] = "New" }, "\"old\"", ["branding.server_name", "redis.url"], ["redis.url"]);
        Assert.Equal("\"old\"", validator);
        Assert.Equal("\"new\"", result.ETag);
        Assert.Single(result.Values);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PUT")));
    }

    [Fact]
    public async Task ConflictAndMalformedSuccessAreDistinctAndNeverRetried()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminServerSettingsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed));
        await Assert.ThrowsAsync<AdminSettingsConflictException>(() => api.PutAsync(lease, new Dictionary<string, string> { ["redis.db"] = "1" }, "\"v1\"", ["redis.db"], ["redis.url"]));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));
        await Assert.ThrowsAsync<AdminSettingsOutcomeUnknownException>(() => api.PutAsync(lease, new Dictionary<string, string> { ["redis.db"] = "1" }, "\"v1\"", ["redis.db"], ["redis.url"]));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<AdminSettingsOutcomeUnknownException>(() => api.PutAsync(lease, new Dictionary<string, string> { ["redis.db"] = "1" }, "\"v1\"", ["redis.db"], ["redis.url"]));
        Assert.Equal(3, kit.Http.Requests.Count(r => r.StartsWith("PUT")));
    }

    [Fact]
    public async Task EchoedTransportExceptionIsNotWrittenToDiagnostics()
    {
        var directory = Path.Combine(TestRepository.Root, ".codex-tmp", "configure-settings", Guid.NewGuid().ToString("N"));
        using var operation = LocalLog.BeginOperation("settings-transport-fixture", directory);
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture-sensitive-echo"));
        var api = new AdminServerSettingsApi(kit.Client);
        var error = await Assert.ThrowsAsync<AdminSettingsOutcomeUnknownException>(() => api.PutAsync(lease,
            new Dictionary<string, string> { ["redis.url"] = "fixture-sensitive-echo" }, "\"v1\"", ["redis.url"], ["redis.url"]));
        Assert.DoesNotContain("fixture-sensitive-echo", error.ToString());
        Assert.DoesNotContain("fixture-sensitive-echo", File.ReadAllText(Path.Combine(directory, "api_diagnostics.txt")));
    }

    [Fact]
    public async Task ConnectionResponseDoesNotExposeSubmittedSecret()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"success":false,"message":"failed fixture-secret-value"}"""));
        var result = await new AdminServerSettingsApi(kit.Client).CheckConnectionAsync(lease, "redis",
            new Dictionary<string, string> { ["redis.url"] = "fixture-secret-value" }, ["redis.url"]);
        Assert.DoesNotContain("fixture-secret-value", result.Message);
        Assert.DoesNotContain("fixture-secret-value", result.ToString());
    }

    [Fact]
    public async Task UnauthorizedWriteDoesNotRefreshOrReplay()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminServerSettingsApi(kit.Client).PutAsync(lease,
            new Dictionary<string, string> { ["redis.db"] = "1" }, "\"v1\"", ["redis.db"], []));
        Assert.Equal(0, refreshes);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PUT")));
    }

    [Fact]
    public async Task ErrorBodiesAreBoundedAndDetailsDoNotExposeSecret()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminServerSettingsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"code":"permission_denied","detail":"fixture-sensitive-detail"}""", HttpStatusCode.Forbidden));
        var error = await Assert.ThrowsAsync<ApiException>(() => api.PutAsync(lease,
            new Dictionary<string, string> { ["redis.url"] = "fixture-sensitive-detail" }, "\"v1\"", ["redis.url"], ["redis.url"]));
        Assert.Equal("permission_denied", error.ErrorCode);
        Assert.DoesNotContain("fixture-sensitive-detail", error.ToString());
        var readError = await Assert.ThrowsAsync<ApiException>(() => api.GetEffectiveAsync(lease, ["redis.url"], ["redis.url"]));
        Assert.DoesNotContain("fixture-sensitive-detail", readError.ToString());
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new ByteArrayContent(new byte[8 * 1024 * 1024 + 1])
        });
        await Assert.ThrowsAsync<AdminSettingsOutcomeUnknownException>(() => api.PutAsync(lease,
            new Dictionary<string, string> { ["redis.db"] = "1" }, "\"v1\"", ["redis.db"], []));
    }

    [Fact]
    public async Task MissingOrWeakReadValidatorsAndWildcardWriteAreRejected()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminServerSettingsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetEffectiveAsync(lease, ["redis.db"], []));
        kit.Http.Respond = (_, _) =>
        {
            var weak = AdminMonitorKit.Json("{}"); weak.Headers.ETag = new("\"one\"", true); return Task.FromResult(weak);
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetEffectiveAsync(lease, ["redis.db"], []));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PutAsync(lease,
            new Dictionary<string, string> { ["redis.db"] = "1" }, "*", ["redis.db"], []));
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PUT"));
    }
}
