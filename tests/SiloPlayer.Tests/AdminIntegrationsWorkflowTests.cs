using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminIntegrationsWorkflowTests
{
    [Theory]
    [InlineData(false, "capabilities")]
    [InlineData(true, "capabilities")]
    [InlineData(false, "used_by")]
    [InlineData(true, "used_by")]
    public async Task MalformedProviderMutationCollectionsRequireReconciliation(bool update, string field)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var body = new Dictionary<string, object?>
        {
            ["id"] = "provider", ["type"] = "custom", ["name"] = "Synthetic",
            ["base_url"] = "https://example.invalid/v1", ["has_api_key"] = true,
            ["capabilities"] = new[] { "chat" }, ["used_by"] = Array.Empty<string>()
        };
        body[field] = new string?[] { null };
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(body)));
        var api = new AdminIntegrationsApi(kit.Client);
        var draft = new AdminAiProviderDraft("custom", "Synthetic", "https://example.invalid/v1", "fixture-submitted-secret", ["chat"]);
        await Assert.ThrowsAsync<AdminIntegrationsOutcomeUnknownException>(() => update
            ? api.UpdateProviderAsync(lease, "provider", draft) : api.CreateProviderAsync(lease, draft));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("https://user:secret@example.invalid/v1")]
    [InlineData("https://example.invalid/v1?signature=private")]
    [InlineData("https://example.invalid/v1#private")]
    public async Task ProviderEndpointsNeverExposeCredentialsOrSignedQueries(string endpoint)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminIntegrationsApi(kit.Client);
        await Assert.ThrowsAsync<ArgumentException>(() => api.CreateProviderAsync(lease, new("custom", "Synthetic", endpoint, null, ["chat"])));
        Assert.Empty(kit.Http.Requests);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(JsonSerializer.Serialize(new
        {
            providers = new[] { new { id = "p", type = "custom", name = "Synthetic", base_url = endpoint, has_api_key = false, capabilities = new[] { "chat" }, used_by = Array.Empty<string>() } },
            types = Array.Empty<object>()
        })));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetProvidersAsync(lease));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderMutationCannotPublishAnExactSubmittedKeyEcho(bool update)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"provider","type":"custom","name":"fixture-opaque-secret","base_url":"https://example.invalid/v1","has_api_key":true,"capabilities":["chat"],"used_by":[]}"""));
        var api = new AdminIntegrationsApi(kit.Client);
        var draft = new AdminAiProviderDraft("custom", "Synthetic", "https://example.invalid/v1", "fixture-opaque-secret", ["chat"]);
        await Assert.ThrowsAsync<AdminIntegrationsOutcomeUnknownException>(() => update ? api.UpdateProviderAsync(lease, "provider", draft) : api.CreateProviderAsync(lease, draft));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public void AcceptedWebOperationSurvivesStaleReadsUntilMatchingReceiptAndEnvironmentOwnedToggleIsBlocked()
    {
        var accepted = new AdminJellyfinWebStatus(false, false, null, null, null, true, "install", 40, null) { OperationId = "accepted", OperationState = "running" };
        var stale = accepted with { OperationId = "older", OperationRunning = false, OperationState = "succeeded" };
        Assert.Same(accepted, AdminIntegrationPolicy.ReconcileWebOperation(stale, accepted));
        var settled = accepted with { OperationRunning = false, OperationState = "succeeded" };
        Assert.Same(settled, AdminIntegrationPolicy.ReconcileWebOperation(settled, accepted));
        var state = new AdminIntegrationSettingsState { Loaded = true, ManagedByEnvironment = ["jellyfin_compat.web_enabled"] };
        Assert.False(AdminIntegrationPolicy.CanToggleWeb(accepted with { ApiEnabled = true, Installed = true, OperationRunning = false }, state));
        var paused = new AdminChatGptAccount("account", "synthetic@example.invalid", false, false, false, true);
        Assert.True(paused.Selectable);
        Assert.True(paused.Authenticated);
        Assert.False(paused.Ready);
    }

    [Theory]
    [InlineData("", "install", false)]
    [InlineData("unknown", "install", false)]
    [InlineData("succeeded", "remove", false)]
    [InlineData("succeeded", "install", true)]
    public void AcceptedWebOperationRejectsUnprovenSettlement(string state, string kind, bool running)
    {
        var accepted = new AdminJellyfinWebStatus(false, false, null, null, null, true, "install", 40, null) { OperationId = "accepted", OperationState = "running" };
        var observed = accepted with { OperationRunning = running, OperationState = state, Operation = kind };
        Assert.Same(accepted, AdminIntegrationPolicy.ReconcileWebOperation(observed, accepted));
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    public void MatchingTerminalWebOperationSettlesTheAcceptedIdentity(string state)
    {
        var accepted = new AdminJellyfinWebStatus(false, false, null, null, null, true, "install", 40, null) { OperationId = "accepted", OperationState = "running" };
        var observed = accepted with { OperationRunning = false, OperationState = state };
        Assert.Same(observed, AdminIntegrationPolicy.ReconcileWebOperation(observed, accepted));
    }

    [Fact]
    public void RecommendationsCanonicalHrefResolvesToItsRegisteredRoute()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/recommendations", out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(AdminRoute.Recommendations, resolution.Request!.Route);
    }

    [Fact]
    public async Task CompatibilityRejectsWeakGuardsAndForeignFieldsBeforeSending()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminIntegrationsApi(kit.Client);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PatchCompatibilityAsync(lease,
            new Dictionary<string, string> { ["jellyfin_compat.enabled"] = "true" }, "W/\"one\""));
        await Assert.ThrowsAsync<ArgumentException>(() => api.PatchCompatibilityAsync(lease,
            new Dictionary<string, string> { ["audiobookshelf_compat.enabled"] = "true" }, "\"one\""));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task WebCommandRequiresAndRetainsAnAcceptedOperationIdentity()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"web_state":"installing","operation":{"id":"accepted","kind":"install","state":"running","started_at":"2026-11-17T18:00:00Z","progress_percent":40}}""", HttpStatusCode.Accepted));
        var api = new AdminIntegrationsApi(kit.Client);
        var receipt = await api.InstallJellyfinWebAsync(lease, "10.11.0");
        Assert.Equal("accepted", receipt.OperationId);
        Assert.Equal("2026-11-17T18:00:00Z", receipt.OperationStartedAt);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Accepted));
        await Assert.ThrowsAsync<AdminIntegrationsOutcomeUnknownException>(() => api.RemoveJellyfinWebAsync(lease));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", true)]
    [InlineData("fixture-replacement-key", true)]
    public async Task FixedProviderUpdatesOmitKeepAndCapabilitiesButPreserveExplicitClear(string? key, bool included)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(included, body.RootElement.TryGetProperty("api_key", out var secret));
            if (included) Assert.Equal(key, secret.GetString());
            Assert.False(body.RootElement.TryGetProperty("capabilities", out _));
            return AdminMonitorKit.Json("""{"id":"provider-1","type":"openai","name":"Synthetic","base_url":"https://example.invalid/v1","has_api_key":true,"capabilities":["chat"],"used_by":[]}""");
        };
        await new AdminIntegrationsApi(kit.Client).UpdateProviderAsync(lease, "provider-1", new("openai", "Synthetic", "https://example.invalid/v1", key, ["chat"]));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task RetiredDelayedAccountResponseCannotPublishPrivateAccounts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        var read = new AdminIntegrationsApi(kit.Client).GetChatGptStatusAsync(lease);
        kit.SetRole("user");
        pending.SetResult(AdminMonitorKit.Json("""{"accounts":[{"client_id":"private-account","email":"private@example.invalid"}]}"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("complete")]
    [InlineData("select")]
    [InlineData("disconnect")]
    public async Task ChatAccountMutationsNeverRefreshUnauthorizedRequests(string operation)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshed = false;
        kit.Client.SetTokenRefresher(_ => { refreshed = true; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        var api = new AdminIntegrationsApi(kit.Client);
        var command = operation switch
        {
            "login" => (Task)api.StartChatGptLoginAsync(lease),
            "complete" => api.CompleteChatGptLoginAsync(lease, "http://127.0.0.1:51121/auth/callback?state=s&code=c"),
            "select" => api.SelectChatGptAccountAsync(lease, "account-1"),
            _ => api.DisconnectChatGptAccountAsync(lease, "account-1")
        };
        await Assert.ThrowsAsync<ApiException>(() => command);
        Assert.False(refreshed); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public void ReadinessRejectsAnExplicitlyClearedModelAndUnsupportedReasoning()
    {
        AdminAiProvider[] providers = [new("text", "openai", "Text", "https://example.invalid", true, ["chat"], [])];
        var values = new Dictionary<string, string> { ["ai.text_provider_id"] = "text", ["ai.chat_model"] = "", ["ai.auth_mode"] = "api_key" };
        var state = new AdminIntegrationSettingsState { Values = values, DirtyKeys = ["ai.chat_model"] };
        Assert.NotNull(AdminIntegrationPolicy.ValidateAi(state, providers, null, null, []));
        values["ai.chat_model"] = "synthetic-model"; values["ai.reasoning_effort"] = "xhigh";
        Assert.NotNull(AdminIntegrationPolicy.ValidateAi(state, providers, new("synthetic-model", ["low"], ["default", "priority", "ultrafast"], false), null, []));
        Assert.Null(AdminIntegrationPolicy.ValidateAi(state, providers, new("synthetic-model", ["xhigh"], ["default", "priority", "ultrafast"], false), null, []));
    }
    [Fact]
    public async Task CapabilitiesAreModelScopedAndPreserveOfficialReasoningAndTiers()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("?model=model%20%2B%2F", request.RequestUri!.Query);
            return Task.FromResult(AdminMonitorKit.Json("""{"reasoning_levels":["minimal","xhigh"],"openai_service_tiers":["default","priority","ultrafast"],"chatgpt_sign_in":true}"""));
        };
        var result = await new AdminIntegrationsApi(kit.Client).GetCapabilitiesAsync(lease, "model +/");
        Assert.Equal(new[] { "minimal", "xhigh" }, result.ReasoningLevels);
        Assert.Equal(new[] { "default", "priority", "ultrafast" }, result.ServiceTiers);
        Assert.True(result.ChatGptSignIn);
    }

    [Fact]
    public async Task CompatibilityPatchUsesCanonicalStrongGuardAndTypedDedicatedFields()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = async (request, token) =>
        {
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("/api/v2/admin/jellyfin-compat/settings", request.RequestUri!.AbsolutePath);
            Assert.Equal("\"settings-1\"", Assert.Single(request.Headers.IfMatch).ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.False(body.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal("/synthetic/web", body.RootElement.GetProperty("web_dir").GetString());
            Assert.False(body.RootElement.TryGetProperty("values", out _));
            return AdminMonitorKit.Json("{}");
        };
        await new AdminIntegrationsApi(kit.Client).PatchCompatibilityAsync(lease,
            new Dictionary<string, string> { ["jellyfin_compat.enabled"] = "false", ["jellyfin_compat.web_dir"] = "/synthetic/web" }, "\"settings-1\"");
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task CompatibilityNeverRefreshesOrReplaysAMutation(int status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshed = false;
        kit.Client.SetTokenRefresher(_ => { refreshed = true; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", (HttpStatusCode)status));
        var error = await Record.ExceptionAsync(() => new AdminIntegrationsApi(kit.Client).PatchCompatibilityAsync(lease,
            new Dictionary<string, string> { ["jellyfin_compat.enabled"] = "true" }, "\"one\""));
        Assert.NotNull(error);
        if (status == 412) Assert.IsType<AdminSettingsConflictException>(error);
        if (status == 503) Assert.IsType<AdminIntegrationsOutcomeUnknownException>(error);
        Assert.False(refreshed);
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ChatGptUsesManualCallbackAndAccountSpecificOneShotContracts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Equal("account-1", body.RootElement.GetProperty("client_id").GetString());
                return AdminMonitorKit.Json("""{"attempt_id":"attempt-1","expires_at":"2026-11-17T18:10:00Z","authorization_url":"https://auth.openai.com/authorize?state=synthetic-state","callback_uri":"http://127.0.0.1:51121/auth/callback"}""");
            }
            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.Contains("code=synthetic-code", body.RootElement.GetProperty("callback_url").GetString());
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Delete) return AdminMonitorKit.Json("""{"revocation_confirmed":false}""");
            if (path.EndsWith("/models", StringComparison.Ordinal))
            {
                Assert.Equal("?client_id=account-1", request.RequestUri.Query);
                return AdminMonitorKit.Json("""{"models":[{"id":"gpt-synthetic","name":"Synthetic model"}]}""");
            }
            return AdminMonitorKit.Json("""{"active_client_id":"account-1","accounts":[{"client_id":"account-1","email":"synthetic@example.invalid","connected":true,"needs_login":false}],"login_pending":false,"login_result":"connected","attempt_id":"attempt-1"}""");
        };
        var api = new AdminIntegrationsApi(kit.Client);
        var login = await api.StartChatGptLoginAsync(lease, "account-1");
        Assert.DoesNotContain("synthetic-state", login.ToString());
        await api.CompleteChatGptLoginAsync(lease, "http://127.0.0.1:51121/auth/callback?state=synthetic-state&code=synthetic-code");
        Assert.Equal("account-1", (await api.SelectChatGptAccountAsync(lease, "account-1")).ActiveClientId);
        Assert.Equal("gpt-synthetic", Assert.Single(await api.GetChatGptModelsAsync(lease, "account-1")).Id);
        Assert.False(await api.DisconnectChatGptAccountAsync(lease, "account-1"));
        Assert.Equal(5, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task RecommendationStatusAndResetRetainRunTimesErrorsAndAllFourCounts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? AdminMonitorKit.Json("""{"embeddings":{"running":false,"count":3,"last_run":{"status":"failed","started_at":"2026-11-17T18:00:00Z","completed_at":"2026-11-17T18:01:00Z","error":"synthetic failure","result":{"failed":2}},"last_partial_run":{"status":"completed"}},"taste_profiles":{"running":false,"count":2},"cowatch":{"running":false,"count":1},"recommendations":{"running":false,"count":4},"lock_conflict":"model changed","stale_sweep":{"pending":5,"last_run":{"status":"completed"}},"cache_refreshed_at":"2026-11-17T18:02:00Z"}""")
            : AdminMonitorKit.Json("""{"embeddings":11,"taste_profiles":12,"taste_clusters":13,"cached_rows":14}"""));
        var api = new AdminIntegrationsApi(kit.Client);
        var status = await api.GetRecommendationsStatusAsync(lease);
        Assert.Equal("2026-11-17T18:01:00Z", status.Embeddings.LastRun!.CompletedAt);
        Assert.Equal("synthetic failure", status.Embeddings.LastRun.Error);
        Assert.Equal(2, status.Embeddings.LastRun.Counts["failed"]);
        Assert.NotNull(status.Embeddings.LastPartialRun);
        Assert.NotNull(status.StaleSweepLastRun);
        Assert.Equal("2026-11-17T18:02:00Z", status.CacheRefreshedAt);
        Assert.Equal(new AdminRecommendationReset(11, 12, 13, 14), await api.ResetEmbeddingsAsync(lease));
    }

    [Theory]
    [InlineData("http://127.0.0.1:51121/auth/callback?state=s&code=c", true)]
    [InlineData("http://127.0.0.1:51121/auth/callback?state=s&error=denied", true)]
    [InlineData("https://attacker.invalid/auth/callback?state=s&code=c", false)]
    [InlineData("http://user@127.0.0.1:51121/auth/callback?state=s&code=c", false)]
    [InlineData("http://127.0.0.1:51121/auth/callback?code=c", false)]
    [InlineData("http://127.0.0.1:51121/auth/callback?state=s&code=c#fragment", false)]
    public void CallbackIsOnlyAcceptedForTheServerNamedOriginAndPath(string callback, bool accepted) =>
        Assert.Equal(accepted, AdminIntegrationPolicy.IsCallback(callback, "http://127.0.0.1:51121/auth/callback"));

    [Fact]
    public void ReadinessAndDestructiveGatesUseCapabilitiesAndSavedInputs()
    {
        AdminAiProvider[] providers = [new("speech", "custom", "Speech", "https://example.invalid", false, ["speech"], [])];
        Assert.False(AdminIntegrationPolicy.ProviderReady(providers, "speech", "chat", "model"));
        Assert.True(AdminIntegrationPolicy.ProviderReady(providers, "speech", "speech", "model"));
        Assert.Contains("jellyfin_compat.web_dir", AdminIntegrationsSettings.CompatibilityPatchKeys);
        Assert.DoesNotContain("jellyfin_compat.session_ttl", AdminIntegrationsSettings.CompatibilityPatchKeys);
    }
}
