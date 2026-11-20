using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminIntegrationsContractsTests
{
    [Fact]
    public void ProviderDraftDoesNotRenderTheWriteOnlyCredential()
    {
        var draft = new AdminAiProviderDraft("openai", "Synthetic", "https://example.invalid/v1", "fixture-new-api-key", ["chat"]);
        Assert.DoesNotContain("fixture-new-api-key", draft.ToString());
        Assert.DoesNotContain("https://example.invalid", draft.ToString());
    }

    [Fact]
    public async Task CheckResponseCannotEchoTheSubmittedKey()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"success":false,"message":"Rejected fixture-new-api-key","models":[]}"""));
        var result = await new AdminIntegrationsApi(kit.Client).CheckProviderAsync(lease,
            new("openai", "Synthetic", "https://example.invalid/v1", "fixture-new-api-key", ["chat"]));
        Assert.DoesNotContain("fixture-new-api-key", result.Message);
        Assert.Contains("<redacted>", result.Message);
    }

    [Fact]
    public async Task CheckResponseExcludesModelsContainingTheSubmittedCredential()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"success":true,"models":[{"id":"fixture-new-api-key","kind":"chat"},{"id":"safe-model","kind":"fixture-new-api-key"},{"id":"safe-chat","kind":"chat"}]}"""));
        var result = await new AdminIntegrationsApi(kit.Client).CheckProviderAsync(lease,
            new("openai", "Synthetic", "https://example.invalid/v1", "fixture-new-api-key", ["chat"]));
        Assert.Equal(new AdminAiModel("safe-chat", "chat"), Assert.Single(result.Models));
    }

    [Fact]
    public async Task CheckResponseKeepsModelsThatMerelyContainAPlaceholderKey()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"success":true,"models":[{"id":"chat-small","kind":"chat"}]}"""));
        var result = await new AdminIntegrationsApi(kit.Client).CheckProviderAsync(lease,
            new("custom", "Synthetic", "https://example.invalid/v1", "chat", ["chat"]));
        Assert.Equal(new AdminAiModel("chat-small", "chat"), Assert.Single(result.Models));
    }

    [Fact]
    public async Task ProviderDeletionAcceptsTheBodylessAcknowledgementAndSendsOnce()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await new AdminIntegrationsApi(kit.Client).DeleteProviderAsync(lease, "provider-1");
        Assert.Equal("DELETE /api/v2/admin/ai/providers/provider-1", Assert.Single(kit.Http.Requests));
    }

    [Fact]
    public async Task AnUnexpectedSuccessfulDeletionResponseRequiresReconciliation()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}"));
        await Assert.ThrowsAsync<AdminIntegrationsOutcomeUnknownException>(() =>
            new AdminIntegrationsApi(kit.Client).DeleteProviderAsync(lease, "provider-1"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ProviderReadsRefreshAnExpiredTokenUnderTheSameAuthority()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ =>
        {
            refreshes++;
            kit.Client.SetAccessToken("synthetic-renewed-access");
            return Task.FromResult(true);
        });
        kit.Http.Respond = (request, _) => Task.FromResult(request.Headers.Authorization?.Parameter == "synthetic-renewed-access"
            ? AdminMonitorKit.Json("""{"providers":[],"types":[]}""")
            : AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        var result = await new AdminIntegrationsApi(kit.Client).GetProvidersAsync(lease);
        Assert.Empty(result.Providers);
        Assert.Equal(1, refreshes);
        Assert.Equal(2, kit.Http.Requests.Count);
        Assert.True(lease.IsCurrent);
    }

    [Fact]
    public async Task ProviderDeletionDoesNotRefreshOrReplayAfterUnauthorized()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminIntegrationsApi(kit.Client).DeleteProviderAsync(lease, "provider-1"));
        Assert.Equal(0, refreshes);
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public void SettingsOwnershipCoversEveryOfficialConfigureLeafWithoutProviderSecrets()
    {
        Assert.Contains("ai.text_provider_id", AdminIntegrationsSettings.AiKeys);
        Assert.Contains("recommendations.embedding_provider_id", AdminIntegrationsSettings.RecommendationKeys);
        Assert.Contains("jellyfin_compat.web_install_dir", AdminIntegrationsSettings.CompatibilityKeys);
        Assert.Contains("audiobookshelf_compat.enabled", AdminIntegrationsSettings.CompatibilityKeys);
        Assert.Empty(AdminIntegrationsSettings.Secrets);
        Assert.Equal(AdminIntegrationsSettings.AllKeys.Count, AdminIntegrationsSettings.AllKeys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ProviderAndRecommendationReadsUseThePinnedBoundedAdminContracts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminIntegrationsApi(kit.Client);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/ai/providers" => AdminMonitorKit.Json("""{"providers":[{"id":"openai","type":"openai","name":"OpenAI","base_url":"https://example.invalid/v1","has_api_key":true,"capabilities":["chat","embeddings"],"used_by":["text"]}],"types":[{"type":"openai","label":"OpenAI","default_base_url":"https://example.invalid/v1","capabilities":["chat","embeddings"],"key_required":true}]}"""),
            "/api/v2/admin/recommendations/status" => AdminMonitorKit.Json("""{"embeddings":{"running":true,"count":3,"total":10},"taste_profiles":{"running":false,"count":4},"cowatch":{"running":false,"count":6},"recommendations":{"running":false,"count":5},"lock_conflict":"","stale_sweep":{"pending":7}}"""),
            _ => AdminMonitorKit.Json("{}", HttpStatusCode.NotFound),
        });

        using var lease = authority.Capture();
        var providers = await api.GetProvidersAsync(lease);
        var status = await api.GetRecommendationsStatusAsync(lease);

        var provider = Assert.Single(providers.Providers);
        Assert.Equal("openai", provider.Id);
        Assert.True(provider.HasApiKey);
        Assert.Equal(10, status.Embeddings.Total);
        Assert.Equal(7, status.StaleSweepPending);
        Assert.Equal(new[] { "GET /api/v2/admin/ai/providers", "GET /api/v2/admin/recommendations/status" }, kit.Http.Requests);
    }

    [Fact]
    public async Task RecommendationCommandsAreSentOnceAndRejectUnknownKinds()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminIntegrationsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"status":"started"}"""));
        using var lease = authority.Capture();

        await api.TriggerRecommendationJobAsync(lease, RecommendationJobKind.Embeddings);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.TriggerRecommendationJobAsync(lease, (RecommendationJobKind)99));

        Assert.Single(kit.Http.Requests);
        Assert.Equal("POST /api/v2/admin/recommendations/trigger/embeddings", Assert.Single(kit.Http.Requests));
    }
}
