using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// Authored before production implementation. Source-only task: these tests have NOT run.
public sealed class AdminPluginsTests
{
    [Fact]
    public void PluginManagementIdsRoundTripAsOneOpaqueEscapedDescendant()
    {
        var route = new AdminRouteRequest(AdminRoute.Plugins) { Segments = ["vendor/plugin"] };
        Assert.True(AdminRouteCatalog.IsValid(route)); Assert.Equal("/admin/plugins/vendor%2Fplugin", route.Href);
        Assert.True(AdminRouteCatalog.TryResolve(route.Href, out var resolved)); Assert.Equal(route, resolved.Request);
        Assert.False(AdminRouteCatalog.IsValid(new(AdminRoute.Users) { Segments = ["vendor/plugin"] }));
    }
    internal const string Installation = """
        {"id":"7","plugin_id":"fixture.plugin","version":"1.0.0","source_kind":"silo","repository_id":"2",
         "enabled":true,"update_policy":"notify","runtime":{"resident":true,"state":"failed","restart_count":2},
         "signature":{"state":"unsigned"},"global_config_schema":[],"capabilities":[],"global_configs":[]}
        """;
    private static string Page(string rows, bool more = false, string? cursor = null) =>
        JsonSerializer.Serialize(new { items = JsonSerializer.Deserialize<JsonElement>("[" + rows + "]"), page = new { has_more = more, next_cursor = cursor } });

    [Fact]
    public async Task InventoryDrainsCursorAndRejectsDuplicateIdentity()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var api = new AdminPluginsApi(kit.Client);
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.Query.Contains("cursor=") ? Page(Installation) : Page(Installation, true, "next")));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.InstallationsAsync(lease));
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.Contains("limit=100", kit.Http.Requests.First());
    }

    [Theory]
    [InlineData("{\"items\":[]}")]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"x\"}}")]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":false,\"next_cursor\":\"x\"}}")]
    public async Task InvalidEnvelopeCannotBecomeEmptySuccess(string json)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginsApi(kit.Client).InstallationsAsync(lease));
    }

    [Fact]
    public async Task CatalogSettingUsesExactStrongValidatorAndDoesNotReplayConflict()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        string? match = null;
        kit.Http.Respond = (request, _) => { match = request.Headers.IfMatch.Single().ToString(); return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed)); };
        await Assert.ThrowsAsync<ApiException>(() => new AdminPluginsApi(kit.Client).SetCatalogSettingsAsync(lease, new(new() { IncludeApprovedCommunityPlugins = false }, "\"reviewed\""), true));
        Assert.Equal("\"reviewed\"", match); Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(null)] [InlineData("W/\"weak\"")] [InlineData("*")]
    public async Task CatalogSettingWithoutStrongValidatorNeverSends(string? etag)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AdminPluginsApi(kit.Client).SetCatalogSettingsAsync(lease, new(new(), etag), true));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task LostInstallAnswerIsSentOnceWithoutAuthenticationReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic lost answer");
        await Assert.ThrowsAsync<HttpRequestException>(() => new AdminPluginsApi(kit.Client).InstallAsync(lease, new() { RepositoryId = "2", PluginId = "fixture.plugin", Version = "1.0.0" }));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ConfigClearsAreExplicitAndBlankSecretsRemainBlank()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await new AdminPluginsApi(kit.Client).SaveConfigAsync(lease, "7", new() { Key = "connection", Value = new() { ["host"] = JsonSerializer.SerializeToElement<string?>(null), ["password"] = JsonSerializer.SerializeToElement("") }, ClearSecrets = ["old_key"] });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("value").GetProperty("host").ValueKind);
        Assert.Equal("", body.RootElement.GetProperty("value").GetProperty("password").GetString());
        Assert.Equal("old_key", body.RootElement.GetProperty("clear_secrets")[0].GetString());
    }

    [Fact]
    public async Task DeleteRequiresItsOwn204Receipt()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Accepted));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPluginsApi(kit.Client).UninstallAsync(lease, "7"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RetiredReadCannotPublishAndRetiredMutationCannotSend()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => gate.Task;
        var api = new AdminPluginsApi(kit.Client); var read = api.InstallationsAsync(lease);
        kit.SetRole("user"); gate.SetResult(AdminMonitorKit.Json(Page(Installation)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UninstallAsync(lease, "7"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task InstalledDetailWithPresentationDoesNotFetchRemoteCatalogOrUnrelatedEditors()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page(Installation.Replace("\"id\":\"7\"", "\"presentation\":{\"display_name\":\"Fixture\"},\"id\":\"7\""))));
        using var controller = new AdminPluginsController(new(kit.Client), authority); await controller.OpenAsync("fixture.plugin");
        Assert.Single(controller.State.Installations); Assert.Single(kit.Http.Requests);
        Assert.Contains("/installations?", kit.Http.Requests.Single());
    }
    [Fact]
    public async Task ControllerReconcilesLostWriteWithoutReplayingAndRejectsRetainedRow()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var writes = 0;
        kit.Http.Respond = (request, _) => {
            if (request.Method != HttpMethod.Get) { writes++; throw new HttpRequestException("synthetic"); }
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/installations") ? Page(Installation) : path.EndsWith("/repositories") || path.EndsWith("/catalog") ? Page("") : "{}";
            var response = AdminMonitorKit.Json(json); response.Headers.ETag = new("\"v1\""); return Task.FromResult(response);
        };
        using var controller = new AdminPluginsController(new(kit.Client), authority);
        await controller.OpenAsync(); var row = Assert.Single(controller.State.Installations);
        Assert.False(await controller.InstallationCommandAsync(row, AdminPluginCommand.Uninstall));
        Assert.Contains("unconfirmed", controller.State.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, writes);
        Assert.False(await controller.InstallationCommandAsync(row, AdminPluginCommand.Uninstall));
        Assert.Equal(1, writes);
    }

    [Fact]
    public void FiltersIncludeRuntimeFailureAndExcludePausedUpdates()
    {
        var failed = JsonSerializer.Deserialize<AdminPlugin>(Installation, AdminPluginsApi.WireOptions)!;
        Assert.True(AdminPluginPresentation.Matches(failed, "fixture", "attention"));
        failed.AvailableVersion = "2.0"; failed.UpdatesPaused = true;
        Assert.False(AdminPluginPresentation.Matches(failed, "", "update"));
    }
}
