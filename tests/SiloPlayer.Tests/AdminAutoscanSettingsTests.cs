using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanSettingsTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminAutoscanSettingsController Controller;
        internal readonly AdminAutoscanSettingsApi Api;
        internal bool Enabled = true;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Respond;
        internal Harness()
        {
            Authority = new(Kit.Client, Kit.Auth); Api = new(Kit.Client); Controller = new(Api, Authority);
            Kit.Http.Respond = (request, _) => Respond?.Invoke(request) ?? Task.FromResult(Default(request));
        }
        internal HttpResponseMessage Default(HttpRequestMessage request)
        {
            var requestPath = request.RequestUri!.AbsolutePath;
            if (requestPath.EndsWith("/settings")) return AdminMonitorKit.Json(JsonSerializer.Serialize(new { enabled = Enabled, default_poll_interval_seconds = 300, debounce_seconds = 30 }));
            if (requestPath.EndsWith("/request-integrations")) return AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}""");
            return AdminMonitorKit.Json("""{"items":[{"id":"own","name":"Own server","kind":"sonarr","base_url":"http://localhost:8989","has_api_key":true}],"page":{"has_more":false}}""");
        }
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task SettingsReadLatestEnabledAndNeverUseGenericSettingsOrIfMatch()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync();
        harness.Controller.EditDefaults("600", "0"); harness.Enabled = false;
        string? body = null;
        harness.Respond = async request =>
        {
            if (request.Method == HttpMethod.Get) return harness.Default(request);
            Assert.Equal("/api/v2/admin/autoscan/settings", request.RequestUri!.AbsolutePath); Assert.Empty(request.Headers.IfMatch);
            body = await request.Content!.ReadAsStringAsync();
            return AdminMonitorKit.Json("""{"settings":{"enabled":false,"default_poll_interval_seconds":600,"debounce_seconds":0},"reschedule_state":"failed"}""");
        };
        await harness.Controller.SaveDefaultsAsync(); using var parsed = JsonDocument.Parse(body!);
        Assert.False(parsed.RootElement.GetProperty("enabled").GetBoolean()); Assert.Equal(3, parsed.RootElement.EnumerateObject().Count());
        Assert.False(harness.Controller.State.Dirty); Assert.Equal("failed", harness.Controller.State.RescheduleState);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "-1")]
    [InlineData("2147483648", "0")]
    [InlineData("1.5", "0")]
    public async Task InvalidDefaultsNeverWrite(string interval, string debounce)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync();
        harness.Controller.EditDefaults(interval, debounce); await harness.Controller.SaveDefaultsAsync();
        Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("PUT")); Assert.NotNull(harness.Controller.State.Error);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(409)]
    [InlineData(412)]
    [InlineData(503)]
    public async Task NonRetryableWriteLocksUntilExplicitReload(int status)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"code":"permission_denied","message":"synthetic"}""", (HttpStatusCode)status));
        await harness.Controller.SaveDefaultsAsync(); await harness.Controller.SaveDefaultsAsync();
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("PUT"))); Assert.True(harness.Controller.State.NeedsReview);
        Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.Contains("/auth/refresh", StringComparison.Ordinal));
        harness.Respond = null; await harness.Controller.ReloadAsync();
        Assert.False(harness.Controller.State.NeedsReview); Assert.False(harness.Controller.State.Dirty);
    }

    [Fact]
    public async Task SavedKeyIsOmittedAndTestBodiesFollowOfficialBranches()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor("own");
        var editor = harness.Controller.State.Editor!; Assert.Equal("", editor.ApiKey); Assert.True(editor.CanSave);
        Assert.Equal("own", editor.TestBody().GetValueOrDefault("connection_id")); Assert.False(editor.SaveBody([]).ContainsKey("api_key_ref"));
        harness.Controller.EditConnection(editor with { BaseUrl = "http://changed:8989", ApiKey = " synthetic-key " });
        editor = harness.Controller.State.Editor!; Assert.False(editor.TestBody().ContainsKey("connection_id"));
        Assert.Equal("synthetic-key", editor.TestBody()["api_key_ref"]); Assert.DoesNotContain("synthetic-key", editor.ToString());
        harness.Controller.CloseEditor(); Assert.Null(harness.Controller.State.Editor);
    }

    [Fact]
    public void ReuseDerivesKindOnlyFromServiceKindAndNeverSendsCredentials()
    {
        var editor = new AdminAutoscanConnectionEditor { Reuse = true, IntegrationId = "requests" };
        var body = editor.SaveBody([new("requests", "Movies", "radarr")]);
        Assert.Equal("radarr", body["kind"]); Assert.Equal("Movies", body["name"]);
        Assert.Equal("requests", editor.TestBody()["request_integration_id"]); Assert.False(body.ContainsKey("api_key_ref")); Assert.False(body.ContainsKey("base_url"));
    }

    [Fact]
    public async Task RepeatedCursorFailsClosedWithoutPartialConnections()
    {
        using var harness = new Harness();
        harness.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/connections")
            ? AdminMonitorKit.Json("""{"items":[],"page":{"has_more":true,"next_cursor":"repeat"}}""") : harness.Default(request));
        await harness.Controller.OpenAsync(); Assert.Empty(harness.Controller.State.Connections);
        Assert.False(harness.Controller.State.ConnectionsLoaded); Assert.NotNull(harness.Controller.State.ConnectionsError);
    }

    [Fact]
    public async Task UncertainCreateClearsSecretAndCannotReplay()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        harness.Respond = request => request.Method == HttpMethod.Get ? Task.FromResult(harness.Default(request)) : throw new HttpRequestException("synthetic lost response");
        await harness.Controller.SaveConnectionAsync(); await harness.Controller.SaveConnectionAsync();
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("POST"))); Assert.True(harness.Controller.State.NeedsReview);
        Assert.Equal("", harness.Controller.State.Editor!.ApiKey); await harness.Controller.ReloadAsync(); Assert.Null(harness.Controller.State.Editor);
    }

    [Fact]
    public async Task RetirementClearsDraftAndCannotWrite()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { ApiKey = "synthetic-key" });
        harness.Kit.Auth.SetCurrentUser(new() { Id = "other", Username = "other", Role = "user" });
        Assert.Null(harness.Controller.State.Owner); Assert.Null(harness.Controller.State.Editor); Assert.Empty(harness.Controller.State.Connections);
        await harness.Controller.SaveConnectionAsync(); Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("POST"));
    }

    [Fact]
    public async Task ChangedPollingDefaultsRequireReviewWithoutSendingAWrite()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        harness.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/settings")
            ? AdminMonitorKit.Json("""{"enabled":true,"default_poll_interval_seconds":120,"debounce_seconds":10}""") : harness.Default(request));
        await harness.Controller.SaveDefaultsAsync();
        Assert.True(harness.Controller.State.NeedsReview); Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("PUT"));
        Assert.Equal("600", harness.Controller.State.Interval);
    }

    [Fact]
    public async Task ChangedConnectionDescriptorBlocksDeleteBeforeDispatch()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.RequestDelete("own");
        harness.Respond = request => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"own","name":"Changed","kind":"sonarr","has_api_key":true}],"page":{"has_more":false}}"""));
        await harness.Controller.DeleteConnectionAsync();
        Assert.True(harness.Controller.State.NeedsReview); Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("DELETE"));
    }

    [Theory]
    [InlineData(204, false)]
    [InlineData(404, true)]
    [InlineData(422, true)]
    [InlineData(503, true)]
    public async Task DeleteRequires204AndDoesNotReplay(int status, bool needsReview)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.RequestDelete("own");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"code":"validation_failed"}""", (HttpStatusCode)status));
        await harness.Controller.DeleteConnectionAsync(); await harness.Controller.DeleteConnectionAsync();
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("DELETE")));
        Assert.Equal(needsReview, harness.Controller.State.NeedsReview);
        Assert.Equal(!needsReview, harness.Controller.State.Connections.Count == 0);
    }

    [Fact]
    public async Task MalformedSuccessfulSettingsAcknowledgementIsUnknownNotSuccess()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request) : AdminMonitorKit.Json("{}"));
        await harness.Controller.SaveDefaultsAsync();
        Assert.True(harness.Controller.State.OutcomeUnknown); Assert.Null(harness.Controller.State.Notice); Assert.True(harness.Controller.State.Dirty);
    }

    [Fact]
    public async Task CanonicalRequestDescriptorsUsePluginConfigAndDiscardSecretFields()
    {
        using var harness = new Harness();
        harness.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/request-integrations" => AdminMonitorKit.Json("""{"items":[{"id":"movies"},{"id":"other"}],"page":{"has_more":false}}"""),
            "/api/v2/admin/request-integrations/movies" => AdminMonitorKit.Json("""{"id":"movies","name":"Movies","kind":"sonarr","plugin_config":{"service_kind":"radarr"},"api_key_ref":"synthetic-hidden"}"""),
            "/api/v2/admin/request-integrations/other" => AdminMonitorKit.Json("""{"id":"other","name":"Other","kind":"sonarr","plugin_config":{"service_kind":"other"}}"""),
            _ => harness.Default(request)
        });
        await harness.Controller.OpenAsync(); var server = Assert.Single(harness.Controller.State.Servers);
        Assert.Equal("radarr", server.Kind); Assert.DoesNotContain("synthetic-hidden", server.ToString());
        harness.Controller.OpenEditor(); Assert.True(harness.Controller.State.Editor!.Reuse); Assert.Equal("movies", harness.Controller.State.Editor.IntegrationId);
    }

    [Theory]
    [InlineData(false, false, "", "", false)]
    [InlineData(false, false, "http://host", "key", true)]
    [InlineData(false, true, "http://host", "", true)]
    [InlineData(false, false, "http://host", "", false)]
    [InlineData(true, false, "", "", true)]
    public void ConditionalCredentialStates(bool reuse, bool savedKey, string url, string key, bool canSave)
    {
        var editor = new AdminAutoscanConnectionEditor { Reuse = reuse, HasApiKey = savedKey, IntegrationId = reuse ? "server" : "", Name = "Name", BaseUrl = url, ApiKey = key };
        Assert.Equal(canSave, editor.CanSave);
    }

    [Fact]
    public async Task TestResultIsAdvisoryAndBoundToTheExactEditorRevision()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor("own");
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Respond = request => { arrived.TrySetResult(); return pending.Task; };
        var testing = harness.Controller.TestConnectionAsync(); await arrived.Task;
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Name = "Edited during test" });
        pending.SetResult(AdminMonitorKit.Json("""{"ok":true,"version":"1.2"}""")); await testing;
        Assert.Null(harness.Controller.State.TestResult); Assert.True(harness.Controller.State.Editor!.CanSave);
        Assert.False(harness.Controller.State.NeedsReview);
    }

    [Fact]
    public async Task IdenticalControlEchoDoesNotCancelAnAdvisoryCheck()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor("own");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Respond = _ => pending.Task;
        var checking = harness.Controller.TestConnectionAsync();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { });
        Assert.True(harness.Controller.State.Testing);
        pending.SetResult(AdminMonitorKit.Json("""{"ok":true,"version":"1.2"}""")); await checking;
        Assert.True(harness.Controller.State.TestResult?.Ok); Assert.False(harness.Controller.State.Testing);
    }

    [Fact]
    public async Task ConcurrentSaveSendsOnlyOneCommand()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Respond = request => request.Method == HttpMethod.Get ? Task.FromResult(harness.Default(request)) : Wait();
        Task<HttpResponseMessage> Wait() { arrived.TrySetResult(); return pending.Task; }
        var first = harness.Controller.SaveDefaultsAsync(); await arrived.Task; await harness.Controller.SaveDefaultsAsync();
        pending.SetResult(AdminMonitorKit.Json("""{"settings":{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":30},"reschedule_state":"applied"}""")); await first;
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("PUT")));
    }

    [Fact]
    public async Task CanonicalSavedOwnUpdateTrimsAndOmitsBlankKey()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor("own");
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Name = " Renamed ", ApiKey = "  " });
        string? body = null;
        harness.Respond = async request =>
        {
            if (request.Method == HttpMethod.Get) return harness.Default(request);
            Assert.Equal(HttpMethod.Put, request.Method); Assert.EndsWith("/connections/own", request.RequestUri!.AbsolutePath); Assert.Empty(request.Headers.IfMatch);
            body = await request.Content!.ReadAsStringAsync();
            return AdminMonitorKit.Json("""{"id":"own","name":"Renamed","kind":"sonarr","base_url":"http://localhost:8989","has_api_key":true}""");
        };
        await harness.Controller.SaveConnectionAsync(); using var data = JsonDocument.Parse(body!);
        Assert.Equal("Renamed", data.RootElement.GetProperty("name").GetString()); Assert.False(data.RootElement.TryGetProperty("api_key_ref", out _));
        Assert.Null(harness.Controller.State.Editor); Assert.True(Assert.Single(harness.Controller.State.Connections).HasApiKey);
    }

    [Fact]
    public async Task LateReadFromRetiredOwnerCannotPublish()
    {
        using var harness = new Harness();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Respond = request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/settings")) return Task.FromResult(harness.Default(request));
            arrived.TrySetResult(); return pending.Task;
        };
        var loading = harness.Controller.OpenAsync(); await arrived.Task;
        harness.Kit.Auth.SetCurrentUser(new() { Id = "retired", Username = "retired", Role = "user" });
        pending.SetResult(AdminMonitorKit.Json("""{"enabled":true,"default_poll_interval_seconds":5,"debounce_seconds":0}""")); await loading;
        Assert.Null(harness.Controller.State.Owner); Assert.Null(harness.Controller.State.Saved); Assert.Empty(harness.Controller.State.Connections);
    }

    [Fact]
    public async Task OversizedSettingsResponseFailsClosedWithoutBlockingIndependentConnectionRead()
    {
        using var harness = new Harness();
        harness.Respond = request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/settings")
            ? AdminMonitorKit.Json(new string('x', 1024 * 1024 + 1)) : harness.Default(request));
        await harness.Controller.OpenAsync(); Assert.Null(harness.Controller.State.Saved); Assert.NotNull(harness.Controller.State.Error);
        Assert.True(harness.Controller.State.ConnectionsLoaded);
        await harness.Controller.SaveDefaultsAsync(); Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("PUT"));
    }
}
