using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminIntegrationSettingsControllerTests
{
    [Fact]
    public async Task SaveUsesStrongEtagAndRetainsAnEditMadeWhileTheFirstWriteIsInFlight()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, AdminIntegrationsSettings.Secrets);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.RecommendationKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();

        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put ? pending.Task : Task.FromResult(Read(request));
        controller.Set("recommendations.embedding_model", "first");
        var save = controller.SaveAsync();
        controller.Set("recommendations.embedding_model", "later");
        var response = AdminMonitorKit.Json("""{"values":{"recommendations.embedding_model":"first"},"restart_required":false}""");
        response.Headers.ETag = new("\"two\""); pending.SetResult(response);
        await save;

        Assert.Equal("later", controller.State.Values["recommendations.embedding_model"]);
        Assert.Equal("first", controller.State.PersistedValues["recommendations.embedding_model"]);
        Assert.Contains("recommendations.embedding_model", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task EnvironmentManagedCompatibilityKeyCannotBeStagedOrWritten()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, AdminIntegrationsSettings.Secrets);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.CompatibilityKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("sensitive-status")
            ? AdminMonitorKit.Json("""{"configured":[],"managed_by_env":["jellyfin_compat.enabled"]}""") : Read(request));
        await controller.OpenAsync();

        controller.Set("jellyfin_compat.enabled", "true");
        await controller.SaveAsync();

        Assert.Empty(controller.State.DirtyKeys);
        Assert.DoesNotContain(kit.Http.Requests, entry => entry.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FieldCommitWritesOnlyThatFieldAndPreservesOtherDrafts()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, []);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.RecommendationKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();
        controller.Set("recommendations.embedding_model", "staged");
        controller.Set("recommendations.diversity_lambda", "0.8");
        kit.Http.Respond = async (request, token) =>
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var values = body.RootElement.GetProperty("values");
            Assert.Single(values.EnumerateObject());
            Assert.Equal("0.8", values.GetProperty("recommendations.diversity_lambda").GetString());
            return WithTag(AdminMonitorKit.Json("""{"values":{"recommendations.diversity_lambda":"0.8"},"restart_required":false}"""));
        };
        await controller.SaveFieldsAsync(["recommendations.diversity_lambda"]);
        Assert.Equal("staged", controller.State.Values["recommendations.embedding_model"]);
        Assert.Contains("recommendations.embedding_model", controller.State.DirtyKeys);
        Assert.DoesNotContain("recommendations.diversity_lambda", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ConflictBlocksReplaysAndReloadCanPreserveThenDiscardTheDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, []);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.RecommendationKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed) : Read(request));
        await controller.OpenAsync();
        controller.Set("recommendations.embedding_model", "private draft");
        await controller.SaveAsync();
        Assert.True(controller.State.Conflict);
        await controller.SaveAsync();
        Assert.Single(kit.Http.Requests.Where(entry => entry.StartsWith("PUT", StringComparison.Ordinal)));
        await controller.ReloadAsync(true);
        Assert.False(controller.State.Conflict);
        Assert.Equal("private draft", controller.State.Values["recommendations.embedding_model"]);
        await controller.ReloadAsync(false);
        Assert.Empty(controller.State.DirtyKeys);
        controller.Suspend();
        Assert.Empty(controller.State.Values);
        Assert.Null(controller.State.Owner);
    }

    [Fact]
    public async Task MixedCompatibilitySavePatchesFirstAndReReadsTheCanonicalGuardBeforeGenericPut()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, []);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.CompatibilityKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        var patched = false;
        kit.Http.Respond = async (request, token) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patched = true;
                return AdminMonitorKit.Json("{}");
            }
            if (request.Method == HttpMethod.Put)
            {
                Assert.True(patched);
                Assert.Equal("\"after-patch\"", Assert.Single(request.Headers.IfMatch).ToString());
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Assert.False(body.RootElement.GetProperty("values").TryGetProperty("jellyfin_compat.enabled", out _));
                return WithTag(AdminMonitorKit.Json("""{"values":{"audiobookshelf_compat.enabled":"true"},"restart_required":false}"""));
            }
            if (patched && request.RequestUri!.AbsolutePath.EndsWith("/effective", StringComparison.Ordinal))
            {
                var response = AdminMonitorKit.Json("""{"jellyfin_compat.enabled":"true"}""");
                response.Headers.ETag = new("\"after-patch\"");
                return response;
            }
            return Read(request);
        };
        await controller.OpenAsync();
        controller.Set("jellyfin_compat.enabled", "true");
        controller.Set("audiobookshelf_compat.enabled", "true");
        await controller.SaveAsync();
        Assert.True(patched);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Theory]
    [InlineData("jellyfin_compat.server_id", "generated-server-id")]
    [InlineData("jellyfin_compat.web_dir", "/synthetic/current")]
    public async Task CompatibilityIdentityAndManagedDirectoryRemainReadOnly(string key, string value)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminIntegrationsSettings.AllKeys, []);
        using var controller = new AdminIntegrationSettingsController(AdminIntegrationsSettings.CompatibilityKeys, api, store, authority, new AdminIntegrationsApi(kit.Client));
        kit.Http.Respond = (request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/effective", StringComparison.Ordinal)) return Task.FromResult(Read(request));
            var response = AdminMonitorKit.Json(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { [key] = value }));
            response.Headers.ETag = new("\"one\"");
            return Task.FromResult(response);
        };
        await controller.OpenAsync();
        Assert.True(controller.State.Values.TryGetValue(key, out var loaded));
        Assert.Equal(value, loaded);
        controller.Set(key, "untrusted replacement");
        Assert.Empty(controller.State.DirtyKeys);
        await controller.SaveAsync();
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal) || request.StartsWith("PATCH", StringComparison.Ordinal));
    }

    private static HttpResponseMessage WithTag(HttpResponseMessage response)
    {
        response.Headers.ETag = new("\"controller-after\"");
        return response;
    }

    private static HttpResponseMessage Read(HttpRequestMessage request)
    {
        var response = request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("effective") => AdminMonitorKit.Json("""{"recommendations.enabled":"true","recommendations.embedding_model":"baseline","jellyfin_compat.enabled":"false"}"""),
            var path when path.EndsWith("sensitive-status") => AdminMonitorKit.Json("""{"configured":[],"managed_by_env":[]}"""),
            _ => AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}"""),
        };
        response.Headers.ETag = new("\"one\""); return response;
    }
}
