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
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task UnknownOutcomeWriteLocksUntilExplicitReload(int status)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"code":"dependency_unavailable","message":"synthetic"}""", (HttpStatusCode)status));
        await harness.Controller.SaveDefaultsAsync(); await harness.Controller.SaveDefaultsAsync();
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("PUT"))); Assert.True(harness.Controller.State.NeedsReview);
        Assert.True(harness.Controller.State.OutcomeUnknown);
        Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.Contains("/auth/refresh", StringComparison.Ordinal));
        harness.Respond = null; await harness.Controller.ReloadAsync();
        Assert.False(harness.Controller.State.NeedsReview); Assert.False(harness.Controller.State.Dirty);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    public async Task DefiniteRefusalShowsTheServerReasonAndAllowsACorrectedRetry(int status)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"type":"https://silo.test/docs/api/v2/problems/validation_failed","title":"Invalid","detail":"Invalid autoscan interval or debounce."}""", (HttpStatusCode)status));
        await harness.Controller.SaveDefaultsAsync();
        var state = harness.Controller.State;
        Assert.False(state.NeedsReview); Assert.False(state.OutcomeUnknown); Assert.True(state.Dirty); Assert.Equal("Invalid autoscan interval or debounce.", state.Error);
        Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.Contains("/auth/refresh", StringComparison.Ordinal));
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"settings":{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":30},"reschedule_state":"applied"}"""));
        await harness.Controller.SaveDefaultsAsync();
        Assert.Equal(2, harness.Kit.Http.Requests.Count(request => request.StartsWith("PUT")));
        Assert.False(harness.Controller.State.Dirty); Assert.Equal("Autoscan settings saved", harness.Controller.State.Notice);
    }

    [Fact]
    public async Task EverySettingsAndConnectionBodyIsSentAsJsonWithTheOfficialFieldNames()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.EditDefaults("600", "30");
        var bodies = new List<(string Method, string Path, string Body)>();
        harness.Respond = async request =>
        {
            if (request.Method == HttpMethod.Get) return harness.Default(request);
            bodies.Add((request.Method.Method, request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync()));
            return request.RequestUri.AbsolutePath switch
            {
                "/api/v2/admin/autoscan/settings" => AdminMonitorKit.Json("""{"settings":{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":30},"reschedule_state":"applied"}"""),
                "/api/v2/admin/autoscan/connections/test" => AdminMonitorKit.Json("""{"ok":true,"version":"4.0.1"}"""),
                _ => AdminMonitorKit.Json("""{"id":"new","name":"New","kind":"radarr","base_url":"http://new:7878","has_api_key":true}""", HttpStatusCode.Created),
            };
        };
        await harness.Controller.SaveDefaultsAsync();
        harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = " New ", Kind = "radarr", BaseUrl = " http://new:7878 ", ApiKey = " synthetic-key " });
        await harness.Controller.TestConnectionAsync(); await harness.Controller.SaveConnectionAsync();
        Assert.Equal(3, bodies.Count);
        Assert.Equal(new[] { "default_poll_interval_seconds", "debounce_seconds", "enabled" }.Order(), Keys(bodies[0].Body).Order());
        Assert.Equal(new[] { "api_key_ref", "base_url" }.Order(), Keys(bodies[1].Body).Order());
        Assert.Equal("/api/v2/admin/autoscan/connections/test", bodies[1].Path);
        Assert.Equal(new[] { "api_key_ref", "base_url", "kind", "name" }.Order(), Keys(bodies[2].Body).Order());
        using var create = JsonDocument.Parse(bodies[2].Body);
        Assert.Equal("New", create.RootElement.GetProperty("name").GetString()); Assert.Equal("radarr", create.RootElement.GetProperty("kind").GetString());
        Assert.Equal("synthetic-key", create.RootElement.GetProperty("api_key_ref").GetString());
        Assert.Equal(600, JsonDocument.Parse(bodies[0].Body).RootElement.GetProperty("default_poll_interval_seconds").GetInt32());
        Assert.DoesNotContain("Comparer", bodies[2].Body); Assert.DoesNotContain("\"Count\"", bodies[2].Body);

        static IEnumerable<string> Keys(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.EnumerateObject().Select(property => property.Name).ToArray(); }
    }

    [Fact]
    public async Task ALaterConnectionResultIsNotHiddenByAnEarlierRescheduleWarning()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync();
        harness.Controller.EditDefaults("600", "0");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : request.RequestUri!.AbsolutePath.EndsWith("/settings")
                ? AdminMonitorKit.Json("""{"settings":{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":0},"reschedule_state":"failed"}""")
                : AdminMonitorKit.Json("""{"id":"new","name":"New","kind":"sonarr","base_url":"http://new:8989","has_api_key":true}""", HttpStatusCode.Created));
        await harness.Controller.SaveDefaultsAsync(); Assert.Equal("failed", harness.Controller.State.RescheduleState);
        harness.Controller.OpenEditor(); Assert.Null(harness.Controller.State.RescheduleState);
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        await harness.Controller.SaveConnectionAsync();
        Assert.Null(harness.Controller.State.RescheduleState); Assert.Equal("Autoscan connection created", harness.Controller.State.Notice);
    }

    [Fact]
    public async Task ADroppedConnectionIsLoggedOnlyAsAFixedCategory()
    {
        var directory = Path.Combine(TestRepository.Root, ".codex-tmp", "autoscan-settings-transport", Guid.NewGuid().ToString("N"));
        using var operation = LocalLog.BeginOperation("autoscan-settings-transport-fixture", directory);
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        harness.Respond = request => request.Method == HttpMethod.Get ? Task.FromResult(harness.Default(request))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic-host.invalid:8989 refused synthetic-key"));
        await harness.Controller.SaveConnectionAsync();
        Assert.True(harness.Controller.State.NeedsReview);
        var diagnostics = File.ReadAllText(Path.Combine(directory, "api_diagnostics.txt"));
        Assert.Contains("error=autoscan_transport_failed", diagnostics);
        Assert.DoesNotContain("synthetic-key", diagnostics); Assert.DoesNotContain("synthetic-host.invalid", diagnostics);
    }

    [Fact]
    public async Task ARefusedConnectionSaveKeepsTheTypedKeyAndTheCorrectedRetrySendsIt()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        var bodies = new List<string>();
        harness.Respond = async request =>
        {
            if (request.Method == HttpMethod.Get) return harness.Default(request);
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return bodies.Count == 1
                ? AdminMonitorKit.Json("""{"type":"https://silo.test/docs/api/v2/problems/validation_failed","detail":"Rejected value synthetic-key for the base URL."}""", HttpStatusCode.UnprocessableEntity)
                : AdminMonitorKit.Json("""{"id":"new","name":"New","kind":"sonarr","base_url":"http://new:8989","has_api_key":true}""", HttpStatusCode.Created);
        };
        await harness.Controller.SaveConnectionAsync();
        var refused = harness.Controller.State;
        Assert.False(refused.NeedsReview); Assert.False(refused.Busy); Assert.Equal("synthetic-key", refused.Editor!.ApiKey);
        Assert.DoesNotContain("synthetic-key", refused.Error); Assert.Contains("<redacted>", refused.Error);
        harness.Controller.EditConnection(refused.Editor with { BaseUrl = "http://corrected:8989" });
        Assert.Equal("synthetic-key", harness.Controller.State.Editor!.ApiKey); Assert.Null(harness.Controller.State.Error);
        await harness.Controller.SaveConnectionAsync();
        Assert.Equal(2, bodies.Count); Assert.Contains("\"api_key_ref\":\"synthetic-key\"", bodies[1]); Assert.Contains("http://corrected:8989", bodies[1]);
        Assert.Null(harness.Controller.State.Editor); Assert.Equal("Autoscan connection created", harness.Controller.State.Notice);
    }

    [Fact]
    public void RefusalTextIsRedactedBeforeItIsTruncated()
    {
        var secret = "synthetic-secret-key-value";
        // The secret straddles the 500-character cut; no fragment of it may survive.
        var text = new string('x', 490) + secret + " trailing";
        var safe = AdminAutoscanSettingsApi.Safe(text, secret);
        Assert.DoesNotContain("synthetic", safe); Assert.DoesNotContain("secret-key", safe); Assert.Contains("<redacted>", safe);
        Assert.True(safe.Length <= 501);
        Assert.DoesNotContain("\n", AdminAutoscanSettingsApi.Safe("one\r\ntwo", null));
        Assert.DoesNotContain("https://", AdminAutoscanSettingsApi.Safe("failed at https://host.invalid/path?apikey=abc", null));
    }

    [Theory]
    [InlineData("", "300")]
    [InlineData("0", "300")]
    [InlineData("abc", "300")]
    [InlineData(" 45 ", "45")]
    [InlineData("1e3", "1000")]
    [InlineData("-5", "-5")]
    [InlineData("1.5", "1.5")]
    [InlineData("99999999999999999999999", "1E+23")]
    public void PollingInputsFollowTheOfficialNumberCoercion(string typed, string expected) =>
        Assert.Equal(expected, AdminAutoscanNumber.Coerce(typed, 300));

    [Fact]
    public void DebounceZeroAndFallbackFollowTheOfficialRules() => Assert.Equal("0", AdminAutoscanNumber.Coerce("", 0));

    [Fact]
    public async Task AFailedConnectionTestShowsTheRedactedServerReasonAndNeverBlocksSave()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"ok":false,"error":"Connection could not be verified; key synthetic-key was refused."}"""));
        await harness.Controller.TestConnectionAsync();
        var state = harness.Controller.State;
        Assert.False(state.TestResult!.Ok); Assert.DoesNotContain("synthetic-key", state.TestResult.Error); Assert.Contains("Connection could not be verified", state.TestResult.Error);
        Assert.True(state.Editor!.CanSave); Assert.True(state.CanWrite);
    }

    [Fact]
    public async Task ARefusedConnectionTestShowsTheServerReasonAndATransportFailureTheOfficialText()
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor("own");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json("""{"type":"https://silo.test/docs/api/v2/problems/not_found","detail":"Connection not found."}""", HttpStatusCode.NotFound));
        await harness.Controller.TestConnectionAsync();
        Assert.Equal("Connection not found.", harness.Controller.State.TestError);
        harness.Respond = _ => throw new HttpRequestException("synthetic transport loss");
        await harness.Controller.TestConnectionAsync();
        Assert.Equal("Connection test failed", harness.Controller.State.TestError); Assert.True(harness.Controller.State.CanWrite);
    }

    [Fact]
    public void StoredValuesThatFailAClientCheckStayInTheEditorAndSaveUnchanged()
    {
        var saved = new AdminAutoscanSavedConnection("odd", "Odd", "lidarr", "sonarr.internal:8989/path", null, true);
        var editor = new AdminAutoscanConnectionEditor { Id = saved.Id, Name = saved.Name, Kind = saved.Kind, BaseUrl = saved.BaseUrl!, HasApiKey = saved.HasApiKey };
        var body = editor.SaveBody([]);
        Assert.Equal("lidarr", body["kind"]); Assert.Equal("sonarr.internal:8989/path", body["base_url"]); Assert.False(body.ContainsKey("api_key_ref"));
    }

    [Fact]
    public void SavedUrlsAreRedactedForDisplayOnlyAndNeverLoseTheStoredValue()
    {
        Assert.Equal("http://host:8989/path", AdminAutoscanConnectionEditor.DisplayUrl("http://user:synthetic-pass@host:8989/path?apikey=synthetic#frag"));
        Assert.Equal("http://localhost:8989", AdminAutoscanConnectionEditor.DisplayUrl("http://localhost:8989"));
        Assert.Equal("not a url", AdminAutoscanConnectionEditor.DisplayUrl("not a url"));
    }

    [Fact]
    public void ReuseSaveOfAVanishedRequestsServerKeepsTheSavedNameAndKind()
    {
        var editor = new AdminAutoscanConnectionEditor { Id = "c", Reuse = true, IntegrationId = "gone", Name = "Saved name", Kind = "radarr" };
        var body = editor.SaveBody([new("other", "Other", "sonarr")]);
        Assert.Equal("Saved name", body["name"]); Assert.Equal("radarr", body["kind"]); Assert.Equal("gone", body["request_integration_id"]);
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

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"id":"","name":"New","kind":"sonarr","has_api_key":true}""")]
    [InlineData("""{"id":"new","name":"New","kind":"sonarr"}""")]
    public async Task ACreateAcknowledgedWithoutAUsableRecordIsAnUnknownOutcomeNotNothingSent(string acknowledgement)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.OpenEditor();
        harness.Controller.EditConnection(harness.Controller.State.Editor! with { Reuse = false, Name = "New", BaseUrl = "http://new:8989", ApiKey = "synthetic-key" });
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request) : AdminMonitorKit.Json(acknowledgement, HttpStatusCode.Created));
        await harness.Controller.SaveConnectionAsync();
        var state = harness.Controller.State;
        Assert.True(state.NeedsReview); Assert.True(state.OutcomeUnknown); Assert.Equal("", state.Editor!.ApiKey);
        Assert.Equal("Connection creation could not be confirmed. Refresh connections before submitting again.", state.Error);
        await harness.Controller.SaveConnectionAsync();
        Assert.Single(harness.Kit.Http.Requests.Where(request => request.StartsWith("POST")));
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
    [InlineData(204, false, 1)]
    [InlineData(500, true, 1)]
    [InlineData(503, true, 1)]
    public async Task DeleteRequires204AndDoesNotReplayAnUnknownOutcome(int status, bool needsReview, int deletes)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.RequestDelete("own");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : status == 204 ? new HttpResponseMessage(HttpStatusCode.NoContent) : AdminMonitorKit.Json("""{"code":"dependency_unavailable"}""", (HttpStatusCode)status));
        await harness.Controller.DeleteConnectionAsync(); await harness.Controller.DeleteConnectionAsync();
        Assert.Equal(deletes, harness.Kit.Http.Requests.Count(request => request.StartsWith("DELETE")));
        Assert.Equal(needsReview, harness.Controller.State.NeedsReview); Assert.Equal(needsReview, harness.Controller.State.OutcomeUnknown);
        Assert.Equal(!needsReview, harness.Controller.State.Connections.Count == 0);
    }

    [Theory]
    [InlineData(404, "Autoscan connection not found.")]
    [InlineData(422, "Connection is referenced by a scan source.")]
    public async Task ARefusedDeleteKeepsTheRowShowsTheReasonAndMayBeRetried(int status, string detail)
    {
        using var harness = new Harness(); await harness.Controller.OpenAsync(); harness.Controller.RequestDelete("own");
        harness.Respond = request => Task.FromResult(request.Method == HttpMethod.Get ? harness.Default(request)
            : AdminMonitorKit.Json($$"""{"type":"https://silo.test/docs/api/v2/problems/validation_failed","detail":"{{detail}}"}""", (HttpStatusCode)status));
        await harness.Controller.DeleteConnectionAsync();
        var state = harness.Controller.State;
        Assert.False(state.NeedsReview); Assert.False(state.Busy); Assert.Equal(detail, state.Error); Assert.Single(state.Connections); Assert.NotNull(state.DeleteTarget);
        await harness.Controller.DeleteConnectionAsync();
        Assert.Equal(2, harness.Kit.Http.Requests.Count(request => request.StartsWith("DELETE")));
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
        await harness.Controller.OpenAsync(); Assert.Null(harness.Controller.State.Saved); Assert.Equal("Autoscan settings could not be loaded. Reload before editing.", harness.Controller.State.SettingsError);
        Assert.True(harness.Controller.State.ConnectionsLoaded);
        await harness.Controller.SaveDefaultsAsync(); Assert.DoesNotContain(harness.Kit.Http.Requests, request => request.StartsWith("PUT"));
    }
}
