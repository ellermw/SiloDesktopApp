using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// Reads and the controller's write rules for the Plugins page, against official 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e.
public sealed class AdminPluginsTests
{
    [Fact]
    public void PluginManagementIdsRoundTripAsOneOpaqueEscapedDescendant()
    {
        var route = new AdminRouteRequest(AdminRoute.Plugins) { Segments = ["vendor/plugin"] };
        Assert.True(AdminRouteCatalog.IsValid(route)); Assert.Equal("/admin/plugins/vendor%2Fplugin", route.Href);
        Assert.True(AdminRouteCatalog.TryResolve(route.Href, out var resolved)); Assert.Equal(AdminRouteResolutionKind.Route, resolved.Kind); Assert.Equal(route, resolved.Request);
        Assert.False(AdminRouteCatalog.IsValid(new(AdminRoute.Users) { Segments = ["vendor/plugin"] }));
    }

    [Theory]
    [InlineData("/admin/plugins")]
    [InlineData("/admin/plugins?tab=catalog")]
    [InlineData("/admin/plugins/fixture.plugin")]
    public void PluginsPagesAreRealRoutesNotUnsupportedOnes(string href)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolved));
        Assert.Equal(AdminRouteResolutionKind.Route, resolved.Kind);
        Assert.Equal(AdminRoute.Plugins, resolved.Request!.Route);
    }

    internal const string Installation = """
        {"id":"7","plugin_id":"fixture.plugin","version":"1.0.0","source_kind":"silo","repository_id":"2","kind":"binary",
         "enabled":true,"update_policy":"notify","updates_paused":false,"connection_check_supported":true,
         "runtime":{"resident":true,"state":"failed","restart_count":2},
         "signature":{"state":"unsigned"},"global_config_schema":[],"capabilities":[],"global_configs":[],"auth_bindings":[],"task_bindings":[],"routes":[]}
        """;
    internal const string CatalogEntry = """
        {"repository_id":"2","plugin_id":"fixture.catalog","version":"2.0.0","source_kind":"external","repository_name":"Synthetic","archive_url":"https://fixture.invalid/a.zip",
         "capabilities":[],"global_config_schema":[],"user_config_schema":[],"routes":[],"assets":[],"metadata":{}}
        """;

    internal static string Page(string rows, bool more = false, string? cursor = null) =>
        JsonSerializer.Serialize(new { items = JsonSerializer.Deserialize<JsonElement>("[" + rows + "]"), page = new { has_more = more, next_cursor = cursor } });

    internal static string TaskJson(string state = "idle") => "{\"key\":\"check_plugin_updates\",\"name\":\"Check plugin updates\",\"description\":\"d\",\"category\":\"system\",\"state\":\"" + state +
        "\",\"progress\":0,\"manual_only\":false,\"triggers\":[],\"execution_scope\":\"process\"}";

    /// <summary>A scripted server for the whole page: reads answer from the tables; every write is recorded and answered by <paramref name="write"/>.</summary>
    internal sealed class Server
    {
        public string Installations { get; set; } = Page(Installation);
        public string Catalog { get; set; } = Page(CatalogEntry);
        public string Repositories { get; set; } = Page("{\"id\":\"2\",\"display_name\":\"Synthetic\",\"url\":\"https://fixture.invalid/index.json\",\"enabled\":true,\"source_kind\":\"external\",\"managed\":false}");
        public bool EnvDeclarations { get; set; }
        public string TaskState { get; set; } = "idle";
        public List<string> Writes { get; } = [];
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write { get; set; }
        public Func<string, Task<HttpResponseMessage>?>? Override { get; set; }
        public Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken _)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get) { Writes.Add(request.Method + " " + path); return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}")); }
            if (Override?.Invoke(path) is { } overridden) return overridden;
            string json = path switch
            {
                "/api/v2/admin/plugins/installations" => Installations,
                "/api/v2/admin/plugins/catalog" => Catalog,
                "/api/v2/admin/plugins/repositories" => Repositories,
                "/api/v2/admin/plugins/catalog-status" => "{\"approved_community_plugin_count\":3,\"installed_community_plugin_count\":1,\"migrated_plugin_count\":0,\"community_updates_paused\":false}",
                "/api/v2/admin/plugins/catalog-settings" => "{\"include_approved_community_plugins\":true}",
                "/api/v2/admin/plugins/capabilities" => "{\"state\":\"available\",\"allowed\":true,\"revision\":\"r\",\"env_declarations\":" + (EnvDeclarations ? "true" : "false") + "}",
                "/api/v2/admin/plugins/provisioning-status" => Page("{\"plugin_id\":\"fixture.managed\",\"version\":\"1.0.0\",\"state\":\"pending\",\"detail\":\"\"}"),
                "/api/v2/admin/tasks/check_plugin_updates" => TaskJson(TaskState),
                _ => "{}",
            };
            var response = AdminMonitorKit.Json(json);
            if (path.EndsWith("/catalog-settings")) response.Headers.ETag = new("\"settings-v1\"");
            return Task.FromResult(response);
        }
    }

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminPluginsController Controller, Server Server) Open(Server? server = null)
    {
        server ??= new();
        var kit = AdminMonitorKit.Create(); var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = server.Respond;
        var controller = new AdminPluginsController(new(kit.Client), new(kit.Client), authority);
        return (kit, authority, controller, server);
    }

    // ── reads ──────────────────────────────────────────────────────────────

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
    public async Task InstallSendsExactlyTheOfficialBody()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Installation, HttpStatusCode.Created));
        await new AdminPluginsApi(kit.Client).InstallAsync(lease, new() { RepositoryId = "2", PluginId = "fixture.plugin", Version = "1.0.0" });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        Assert.Equal(["plugin_id", "repository_id", "version"], body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("repository_id").ValueKind);
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
    public async Task ProvisioningStatusIsReadOnlyWhenTheServerDeclaresIt()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync();
        Assert.Empty(controller.State.Provisioning);
        Assert.DoesNotContain(kit.Http.Requests, r => r.Contains("provisioning-status"));
        server.EnvDeclarations = true; await controller.ReloadAsync();
        Assert.Equal("fixture.managed", Assert.Single(controller.State.Provisioning).PluginId);
    }

    [Fact]
    public async Task ProvisioningCapabilityNotFoundMeansNoProvisioning()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Override = path => path.EndsWith("/capabilities") ? Reply(HttpStatusCode.NotFound) : null;
        await controller.OpenAsync();
        Assert.Empty(controller.State.Provisioning); Assert.DoesNotContain(AdminPluginsSection.Provisioning, controller.State.Failures.Keys);
    }

    [Fact]
    public async Task InstalledDetailWithPresentationDoesNotFetchRemoteCatalog()
    {
        var server = new Server { Installations = Page(Installation.Replace("\"id\":\"7\"", "\"presentation\":{\"display_name\":\"Fixture\",\"summary\":\"\",\"description_markdown\":\"\",\"setup_markdown\":\"\",\"publisher_name\":\"\",\"publisher_url\":\"\",\"license_spdx\":\"\",\"homepage_url\":\"\",\"source_url\":\"\",\"support_url\":\"\",\"changelog_url\":\"\"},\"id\":\"7\"")) };
        var (kit, authority, controller, _) = Open(server); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync("fixture.plugin");
        Assert.Single(controller.State.Installations);
        Assert.DoesNotContain(kit.Http.Requests, r => r.Contains("/catalog"));
        Assert.DoesNotContain(kit.Http.Requests, r => r.Contains("/repositories"));
    }

    [Fact]
    public async Task DetailOfAnUninstalledPluginReadsTheCatalog()
    {
        var (kit, authority, controller, _) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync("fixture.catalog");
        Assert.Contains(kit.Http.Requests, r => r.Contains("/catalog?"));
    }

    [Fact]
    public async Task FailedSectionKeepsEarlierDataSaysSoAndReloadRecovers()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); Assert.Single(controller.State.Catalog);
        server.Override = path => path.EndsWith("/catalog") ? Reply(HttpStatusCode.InternalServerError) : null;
        await controller.ReloadAsync();
        Assert.Single(controller.State.Catalog);
        Assert.True(controller.State.Failures.ContainsKey(AdminPluginsSection.Catalog)); Assert.Single(controller.State.Installations);
        server.Override = null; await controller.ReloadAsync([AdminPluginsSection.Catalog]);
        Assert.Empty(controller.State.Failures);
    }

    [Fact]
    public async Task SupersededReadsLateFailureRaisesNoError()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var first = true;
        server.Override = path => path.EndsWith("/repositories") && first ? gate.Task : null;
        // The first read is held; a second read supersedes it. The held answer then fails late.
        var firstRead = Task.Run(() => controller.OpenAsync());
        await Wait(() => kit.Http.Requests.Any(r => r.Contains("/repositories")));
        first = false;
        await controller.ReloadAsync();
        gate.SetResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
        await firstRead;
        Assert.Empty(controller.State.Failures); Assert.Single(controller.State.Repositories);
    }

    private static Task<HttpResponseMessage> Reply(HttpStatusCode status) => Task.FromResult(AdminMonitorKit.Json("{}", status));

    private static async Task Wait(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    // ── writes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAgainstARowThatChangedSendsNothing()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Installations = Page(Installation.Replace("\"version\":\"1.0.0\"", "\"version\":\"1.1.0\"")); await controller.ReloadAsync();
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task InstallIsPinnedToTheListedEntryAndNotAnAlreadyInstalledPlugin()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var entry = AdminPluginPin.Of(controller.State.Catalog.Single());
        server.Catalog = Page(CatalogEntry.Replace("\"version\":\"2.0.0\"", "\"version\":\"2.1.0\"")); await controller.ReloadAsync();
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.InstallAsync(entry)); Assert.Empty(server.Writes);
        // And a plugin that is already installed is replaced only by an upload or update, never by a stale tile.
        server.Catalog = Page(CatalogEntry.Replace("fixture.catalog", "fixture.plugin")); await controller.ReloadAsync();
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.InstallAsync(AdminPluginPin.Of(controller.State.Catalog.Single()))); Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task LostInstallBlocksThatPluginUntilItIsReadBackAndNeverReplays()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var entry = AdminPluginPin.Of(controller.State.Catalog.Single());
        server.Write = _ => throw new HttpRequestException("synthetic lost answer");
        // The re-read that follows an unknown outcome fails too, so the target stays blocked.
        server.Override = path => server.Writes.Count > 0 && path.EndsWith("/installations") ? Reply(HttpStatusCode.InternalServerError) : null;
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.InstallAsync(entry));
        Assert.Contains("fixture.catalog", controller.State.Unconfirmed);
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.InstallAsync(entry));
        Assert.Single(server.Writes);
        server.Override = null; await controller.ReloadAsync();
        Assert.Empty(controller.State.Unconfirmed);
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.InstallAsync(AdminPluginPin.Of(controller.State.Catalog.Single())));
        Assert.Equal(2, server.Writes.Count);
    }

    [Fact]
    public async Task DefiniteRefusalShowsTheServersReasonAndStaysRetryable()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"urn:silo:problem:validation_failed\",\"title\":\"Unprocessable\",\"status\":422,\"detail\":\"The package checksum did not match.\"}", HttpStatusCode.UnprocessableEntity));
        Assert.Equal(AdminPluginWriteOutcome.Refused, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
        Assert.Contains("checksum", controller.State.Notice); Assert.True(controller.State.NoticeIsError); Assert.Empty(controller.State.Unconfirmed);
        Assert.Equal(AdminPluginWriteOutcome.Refused, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
        Assert.Equal(2, server.Writes.Count);
    }

    [Fact]
    public async Task ServerErrorOrTimeoutIsAnUnknownOutcomeNotARefusal()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.BadGateway));
        server.Override = path => server.Writes.Count > 0 && path.EndsWith("/installations") ? Reply(HttpStatusCode.InternalServerError) : null;
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.CommandAsync(pin, AdminPluginCommand.Uninstall));
        Assert.Contains("fixture.plugin", controller.State.Unconfirmed);
        // An answer that cannot be trusted (200 where 204 is the receipt) is also unknown.
    }

    [Fact]
    public async Task RequestTimeoutAnswerIsUnknownBecauseTheServerMayHaveActed()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.RequestTimeout));
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
    }

    [Fact]
    public async Task UploadOutcomeUnknownBlocksEveryLifecycleWriteUntilInstallationsAreReadBack()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => throw new HttpRequestException("synthetic lost answer");
        server.Override = path => server.Writes.Count > 0 && path.EndsWith("/installations") ? Reply(HttpStatusCode.InternalServerError) : null;
        await using var file = new MemoryStream(new byte[16]);
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.UploadAsync(file, "fixture.zip", 16));
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
        Assert.Single(server.Writes);
    }

    [Fact]
    public async Task ConflictingCatalogSettingNeedsReviewAndIsNotReplayed()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var reviewed = controller.State.CatalogSettings!;
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"urn:silo:problem:precondition_failed\",\"title\":\"Precondition failed\",\"status\":412}", HttpStatusCode.PreconditionFailed));
        Assert.Equal(AdminPluginWriteOutcome.Refused, await controller.SetCommunityAsync(reviewed, false));
        Assert.True(controller.State.CatalogReviewRequired);
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.SetCommunityAsync(reviewed, false));
        Assert.Single(server.Writes);
        await controller.ReloadAsync(); Assert.False(controller.State.CatalogReviewRequired);
    }

    [Fact]
    public async Task TypedSecretIsRedactedFromTheRefusalShown()
    {
        var server = new Server { Installations = Page(Installation.Replace("\"global_config_schema\":[]", "\"global_config_schema\":[{\"key\":\"connection\",\"title\":\"\",\"description\":\"\",\"json_schema\":\"{}\",\"required\":false}]")) };
        var (kit, authority, controller, _) = Open(server); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"urn:silo:problem:validation_failed\",\"title\":\"Unprocessable\",\"status\":422,\"detail\":\"Rejected value synthetic-secret-value for the key.\"}", HttpStatusCode.UnprocessableEntity));
        var write = new AdminPluginConfigWrite { Key = "connection", Value = new() { ["api_key"] = JsonSerializer.SerializeToElement("synthetic-secret-value") } };
        Assert.Equal(AdminPluginWriteOutcome.Refused, await controller.SaveConfigAsync(pin, write));
        Assert.DoesNotContain("synthetic-secret-value", controller.State.Notice); Assert.Contains("[redacted]", controller.State.Notice);
    }

    [Fact]
    public async Task UninstallOfAnEnvironmentManagedPluginIsNeverSent()
    {
        var server = new Server { Installations = Page(Installation.Replace("\"id\":\"7\"", "\"env_managed\":{\"version_pin\":\"1.0.0\",\"config_fields\":{},\"binding_fields\":{}},\"id\":\"7\"")) };
        var (kit, authority, controller, _) = Open(server); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CommandAsync(pin, AdminPluginCommand.Uninstall));
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.SetPolicyAsync(pin, "off"));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task UpdateIsPinnedToTheVersionThatWasOfferedNotWhatTheServerRecordsLater()
    {
        var server = new Server { Installations = Page(Installation.Replace("\"enabled\":true", "\"enabled\":true,\"available_version\":\"1.1.0\"")) };
        var (kit, authority, controller, _) = Open(server); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Installations = Page(Installation.Replace("\"enabled\":true", "\"enabled\":true,\"available_version\":\"1.2.0\"")); await controller.ReloadAsync();
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CommandAsync(pin, AdminPluginCommand.Update));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task SuccessfulWriteReadsEverythingBackOnceAndShowsTheOfficialMessage()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single()); kit.Http.Requests.Clear();
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json(Installation.Replace("\"enabled\":true", "\"enabled\":false")));
        Assert.Equal(AdminPluginWriteOutcome.Done, await controller.CommandAsync(pin, AdminPluginCommand.Disable));
        Assert.Equal("Plugin updated", controller.State.Notice); Assert.False(controller.State.NoticeIsError);
        Assert.Equal(1, kit.Http.Requests.Count(r => r.Contains("/installations?")));
    }

    [Fact]
    public async Task UpdateCheckRunsOnceAndPollingFinishRefreshesTheLists()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); Assert.Equal("idle", controller.State.TaskState);
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json(TaskJson("running")));
        server.TaskState = "running";
        Assert.Equal(AdminPluginWriteOutcome.Done, await controller.CheckUpdatesAsync());
        Assert.Equal("running", controller.State.TaskState);
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CheckUpdatesAsync());
        server.TaskState = "idle"; kit.Http.Requests.Clear(); await controller.PollUpdateCheckAsync();
        Assert.Equal("idle", controller.State.TaskState);
        Assert.Contains(kit.Http.Requests, r => r.Contains("/installations?"));
        Assert.Single(server.Writes);
    }

    [Fact]
    public async Task FailedPollKeepsTheDataAndTheLastTaskState()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync();
        server.Override = path => path.Contains("/tasks/") ? Reply(HttpStatusCode.InternalServerError) : null;
        await controller.PollUpdateCheckAsync();
        Assert.Single(controller.State.Installations); Assert.Empty(controller.State.Notice);
    }

    [Fact]
    public async Task RetiredAuthorityClearsEverythingAndNothingElseIsSent()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        var retired = false; controller.Retired += () => retired = true;
        kit.SetRole("user");
        await Wait(() => retired);
        Assert.Empty(controller.State.Installations);
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.CommandAsync(pin, AdminPluginCommand.Disable)); Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task EachSaveOutcomeSaysWhetherWhatWasTypedIsErasedAndEveryAttemptIsSentAtMostOnce()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        const string typed = "synthetic-typed-secret";
        AdminPluginConfigWrite Write() => new() { Key = "connection", Value = new() { ["api_key"] = JsonSerializer.SerializeToElement(typed) } };
        // A definite refusal: the typed values stay for the corrected retry.
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"urn:silo:problem:validation_failed\",\"title\":\"Unprocessable\",\"status\":422,\"detail\":\"Rejected.\"}", HttpStatusCode.UnprocessableEntity));
        var refused = await controller.SaveConfigAsync(pin, Write()); Assert.Equal(AdminPluginWriteOutcome.Refused, refused); Assert.False(refused.SettlesTypedValues());
        // Never sent (the row the form was drawn for is gone): nothing reached the server, so the typed values stay.
        var notSent = await controller.SaveConfigAsync(pin with { Version = "9.9.9" }, Write()); Assert.Equal(AdminPluginWriteOutcome.NotSent, notSent); Assert.False(notSent.SettlesTypedValues());
        Assert.Single(server.Writes);
        // A lost answer: the server may have taken it, so the typed values are erased.
        server.Write = _ => throw new HttpRequestException("synthetic lost answer");
        var unknown = await controller.SaveConfigAsync(pin, Write()); Assert.Equal(AdminPluginWriteOutcome.Unknown, unknown); Assert.True(unknown.SettlesTypedValues());
        Assert.Equal(2, server.Writes.Count); Assert.DoesNotContain(typed, controller.State.Notice);
        // An accepted save, with the re-read returning exactly the same installation as before.
        server.Write = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        var done = await controller.SaveConfigAsync(pin, Write()); Assert.Equal(AdminPluginWriteOutcome.Done, done); Assert.True(done.SettlesTypedValues());
        Assert.Equal(3, server.Writes.Count);
    }

    [Fact]
    public async Task ASaveWhoseLastAttemptIsUnconfirmedIsNeverSentAndKeepsWhatWasTyped()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => throw new HttpRequestException("synthetic lost answer");
        server.Override = path => server.Writes.Count > 0 && path.EndsWith("/installations") ? Reply(HttpStatusCode.InternalServerError) : null; // the read-back fails: the block stays
        var write = new AdminPluginConfigWrite { Key = "connection", Value = new() { ["api_key"] = JsonSerializer.SerializeToElement("synthetic-typed-secret") } };
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.SaveConfigAsync(pin, write));
        var blocked = await controller.SaveConfigAsync(pin, write);
        Assert.Equal(AdminPluginWriteOutcome.NotSent, blocked); Assert.False(blocked.SettlesTypedValues()); Assert.Single(server.Writes);
    }

    [Fact]
    public async Task ATaskBindingThatCannotBeConfirmedUsesTheTaskBindingMessageAndAnAuthBindingItsOwn()
    {
        var (kit, authority, controller, server) = Open(); using var _k = kit; using var _a = authority; using var _c = controller;
        await controller.OpenAsync(); var pin = AdminPluginPin.Of(controller.State.Installations.Single());
        server.Write = _ => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.BadGateway));
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.SaveTaskBindingAsync(pin, "refresh", true, new Dictionary<string, JsonElement>()));
        Assert.Equal("Task binding could not be confirmed. Reload the plugin before saving again.", controller.State.Notice);
        Assert.DoesNotContain("Auth binding", controller.State.Notice);
        await controller.ReloadAsync(); Assert.Empty(controller.State.Unconfirmed);
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.SaveAuthBindingAsync(pin, new AdminPluginAuthBindingWrite { CapabilityId = "oidc", Enabled = true, DisplayOrder = 1 }));
        Assert.StartsWith("Auth binding could not be confirmed", controller.State.Notice);
        // While the task binding is unconfirmed, the next attempt is refused with the same task-binding message.
        await controller.ReloadAsync();
        server.Override = path => server.Writes.Count > 1 && path.EndsWith("/installations") ? Reply(HttpStatusCode.InternalServerError) : null;
        Assert.Equal(AdminPluginWriteOutcome.Unknown, await controller.SaveTaskBindingAsync(pin, "refresh", false, new Dictionary<string, JsonElement>()));
        Assert.Equal(AdminPluginWriteOutcome.NotSent, await controller.SaveTaskBindingAsync(pin, "refresh", true, new Dictionary<string, JsonElement>()));
        Assert.Equal("Task binding could not be confirmed. Reload the plugin before saving again.", controller.State.Notice);
    }

    [Fact]
    public void FiltersIncludeRuntimeFailureAndExcludePausedUpdates()
    {
        var failed = JsonSerializer.Deserialize<AdminPlugin>(Installation, AdminPluginsApi.WireOptions)!;
        Assert.True(AdminPluginPresentation.MatchesFilter(failed, AdminPluginFilter.Attention));
        failed.AvailableVersion = "2.0"; failed.UpdatesPaused = true;
        Assert.False(AdminPluginPresentation.MatchesFilter(failed, AdminPluginFilter.Update));
    }
}
