using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminPluginsCommandTests
{
    [Theory]
    [InlineData(AdminPluginCommand.Enable, "PUT", "/7", "enabled", "true")]
    [InlineData(AdminPluginCommand.Disable, "PUT", "/7", "enabled", "false")]
    [InlineData(AdminPluginCommand.PolicyAuto, "PUT", "/7", "update_policy", "auto")]
    [InlineData(AdminPluginCommand.PolicyNotify, "PUT", "/7", "update_policy", "notify")]
    [InlineData(AdminPluginCommand.PolicyOff, "PUT", "/7", "update_policy", "off")]
    [InlineData(AdminPluginCommand.Update, "POST", "/7/update", "", "")]
    [InlineData(AdminPluginCommand.Restart, "POST", "/7/restart", "", "")]
    public async Task LifecycleMatchesPinnedMethodBodyAndReturnedIdentity(AdminPluginCommand command, string method, string suffix, string key, string value)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation.Replace("\"enabled\":true", command == AdminPluginCommand.Disable ? "\"enabled\":false" : "\"enabled\":true").Replace("\"update_policy\":\"notify\"", "\"update_policy\":\"" + (command == AdminPluginCommand.PolicyAuto ? "auto" : command == AdminPluginCommand.PolicyOff ? "off" : "notify") + "\"")));
        await new AdminPluginsApi(kit.Client).InstallationAsync(lease, "7", command);
        Assert.Equal(method + " /api/v2/admin/plugins/installations" + suffix, Assert.Single(kit.Http.Requests));
        if (key.Length > 0) { using var document = JsonDocument.Parse(Assert.Single(kit.Http.Bodies)); Assert.Equal(value, document.RootElement.GetProperty(key).ToString().ToLowerInvariant()); }
    }
    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("../users")] [InlineData("7/restart")] [InlineData("9223372036854775808")]
    public async Task UnsafeIdsNeverSend(string id)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminPluginsApi(kit.Client).UninstallAsync(lease, id)); Assert.Empty(kit.Http.Requests);
    }
    [Fact]
    public async Task ManagedRepositoryCannotBeMutated()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminPluginsApi(kit.Client).RepositoryAsync(lease, new() { Id = "2", Managed = true }, "", "", false, true)); Assert.Empty(kit.Http.Requests);
    }
    [Fact]
    public async Task TaskBindingEncodesCapabilityAndReportsRestartRequired()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"restart_required\":true}"));
        Assert.True(await new AdminPluginsApi(kit.Client).TaskBindingAsync(lease, "7", new() { CapabilityId = "refresh:all", Enabled = false, Trigger = new() { ["type"] = JsonSerializer.SerializeToElement("startup") } }));
        Assert.Contains("/task-bindings/refresh%3Aall", Assert.Single(kit.Http.Requests), StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies)); Assert.False(body.RootElement.GetProperty("enabled").GetBoolean()); Assert.False(body.RootElement.TryGetProperty("capability_id", out _));
    }
    [Fact]
    public async Task ConnectionCheckFalseIsACompletedFailedCheck()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"success\":false,\"message\":\"Synthetic refusal\"}"));
        Assert.False((await new AdminPluginsApi(kit.Client).TestConfigAsync(lease, "7", new() { Key = "connection" })).Success); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task UnauthorizedMutationDoesNotRefreshOrReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminPluginsApi(kit.Client).UninstallAsync(lease, "7")); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task InventoryWalkStopsAtExplicitPageCapWithoutPublishingPartialRows()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var page = 0;
        kit.Http.Respond = (_, _) =>
        {
            var offset = page++ * 100;
            var rows = string.Join(',', Enumerable.Range(1, 100).Select(i => AdminPluginsTests.Installation.Replace("\"id\":\"7\"", "\"id\":\"" + (offset + i) + "\"").Replace("fixture.plugin", "fixture.plugin" + (offset + i))));
            return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + rows + "],\"page\":{\"has_more\":true,\"next_cursor\":\"page" + page + "\"}}"));
        };
        var error = await Assert.ThrowsAsync<AdminPluginsInventoryLimitException>(() => new AdminPluginsApi(kit.Client).InstallationsAsync(lease));
        Assert.Equal(1000, error.Limit); Assert.Equal(AdminPluginsApi.MaxPages, kit.Http.Requests.Count);
    }
    [Fact]
    public async Task OversizedReadFailsBeforeInventoryPublication()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(new string(' ', AdminPluginsApi.MaxResponseBytes + 1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginsApi(kit.Client).CatalogAsync(lease));
    }
    [Fact]
    public async Task UploadChunksAndCompletionHaveIndependentSingleSendReceipts()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var calls = new List<string>(); const int chunk = 512 * 1024; const int size = chunk + 123;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath; calls.Add(request.Method + " " + path);
            if (path.EndsWith("/complete")) return Task.FromResult(AdminMonitorKit.Json(AdminPluginsTests.Installation, HttpStatusCode.Created));
            var received = request.Method == HttpMethod.Post ? 0 : path.EndsWith("/0") ? 1 : 2;
            return Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { upload_id = "synthetic-session", filename = "fixture.zip", size_bytes = size, chunk_size = chunk, total_chunks = 2, received_chunks = received, received_bytes = received == 0 ? 0 : received == 1 ? chunk : size }), request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK));
        };
        await using var source = new MemoryStream(new byte[size]); await new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size);
        Assert.Equal(4, calls.Count); Assert.EndsWith("/chunks/0", calls[1]); Assert.EndsWith("/chunks/1", calls[2]); Assert.EndsWith("/complete", calls[3]);
    }
    [Fact]
    public async Task LostUploadCompletionIsNeverReplayedOrReportedAsSuccess()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        const int chunk = 512 * 1024; const int size = chunk + 1; var completes = 0;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/complete")) { completes++; throw new HttpRequestException("synthetic uncertain completion"); }
            var received = request.Method == HttpMethod.Post ? 0 : path.EndsWith("/0") ? 1 : 2;
            return Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new { upload_id = "synthetic-session", size_bytes = size, chunk_size = chunk, total_chunks = 2, received_chunks = received, received_bytes = received == 0 ? 0 : received == 1 ? chunk : size }), request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK));
        };
        await using var source = new MemoryStream(new byte[size]); await Assert.ThrowsAsync<HttpRequestException>(() => new AdminPluginsApi(kit.Client).UploadAsync(lease, source, "fixture.zip", size));
        Assert.Equal(1, completes); Assert.Equal(4, kit.Http.Requests.Count);
    }
}
