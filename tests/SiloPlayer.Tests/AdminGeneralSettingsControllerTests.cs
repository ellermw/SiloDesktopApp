using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminGeneralSettingsControllerTests
{
    [Fact]
    public async Task ReopeningRefreshesCachedValuesAndUsesTheFreshETagForSave()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();
        Assert.Equal("Silo", controller.State.Values["branding.server_name"]);
        controller.Suspend();

        string? ifMatch = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                ifMatch = request.Headers.IfMatch.ToString();
                var saved = AdminMonitorKit.Json("""{"values":{"branding.server_name":"Edited"},"restart_required":false}""");
                saved.Headers.ETag = new("\"three\"");
                return Task.FromResult(saved);
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("effective"))
            {
                var updated = AdminMonitorKit.Json("""{"branding.server_name":"Updated elsewhere","redis.db":"2"}""");
                updated.Headers.ETag = new("\"two\"");
                return Task.FromResult(updated);
            }
            return Task.FromResult(Read(request));
        };
        await controller.OpenAsync();
        Assert.Equal(2, kit.Http.Requests.Count(request => request == "GET /api/v2/admin/settings/effective"));
        Assert.Equal("Updated elsewhere", controller.State.Values["branding.server_name"]);
        Assert.Equal("Updated elsewhere", controller.State.PersistedValues["branding.server_name"]);
        controller.Set("branding.server_name", "Edited");
        await controller.SaveAsync();
        Assert.Equal("\"two\"", ifMatch);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AcknowledgedSecretReplacementAndClearUpdatePresenceWithoutRetainingSecret()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        var version = 1;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Put) return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("sensitive-status")
                ? AdminMonitorKit.Json("""{"configured":[],"managed_by_env":[]}""") : Read(request));
            var response = AdminMonitorKit.Json("""{"values":{"redis.db":"2"},"restart_required":false}""");
            response.Headers.ETag = new($"\"saved-{++version}\""); return Task.FromResult(response);
        };
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://synthetic.invalid"); await controller.SaveAsync();
        Assert.Contains("redis.url", controller.State.Configured);
        Assert.Equal("", controller.State.Values["redis.url"]);
        Assert.DoesNotContain("redis.url", controller.State.PersistedValues.Keys);
        controller.StageRedisUrl(""); await controller.SaveAsync();
        Assert.DoesNotContain("redis.url", controller.State.Configured);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task CanonicalValuesStaySeparateFromEditsMadeAfterSaveCapture()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request)); await controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        controller.Set("branding.server_name", "First"); var save = controller.SaveAsync();
        controller.Set("branding.server_name", "Later");
        var response = AdminMonitorKit.Json("""{"values":{"branding.server_name":"Canonical first"},"restart_required":false}""");
        response.Headers.ETag = new("\"two\""); pending.SetResult(response); await save;
        Assert.Equal("Later", controller.State.Values["branding.server_name"]);
        Assert.Equal("Canonical first", controller.State.PersistedValues["branding.server_name"]);
        Assert.Contains("branding.server_name", controller.State.DirtyKeys);
    }
    private static HttpResponseMessage Read(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var response = path.EndsWith("effective") ? AdminMonitorKit.Json("""{"branding.server_name":"Silo","redis.db":"2"}""")
            : path.EndsWith("sensitive-status") ? AdminMonitorKit.Json("""{"configured":["redis.url"],"managed_by_env":[]}""")
            : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
        response.Headers.ETag = new("\"one\"");
        return response;
    }

    [Fact]
    public async Task UnknownSaveBlocksReplayUntilExplicitReloadAndRetainsDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();
        controller.Set("branding.server_name", "Changed");
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException());
        await controller.SaveAsync();
        Assert.True(controller.State.OutcomeUnknown);
        await controller.SaveAsync();
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PUT")));
        Assert.Equal("Changed", controller.State.Values["branding.server_name"]);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.ReloadAsync(preserveDraft: true);
        Assert.False(controller.State.OutcomeUnknown);
        Assert.Contains("branding.server_name", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task SensitiveFailureAllowsNonRedisSaveAndRetiredReplyCannotRestoreDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("sensitive-status") ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Read(request));
        await controller.OpenAsync();
        Assert.True(controller.State.SensitiveStatusError);
        controller.Set("branding.server_name", "Changed");
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        var save = controller.SaveAsync();
        authority.Retire();
        var response = AdminMonitorKit.Json("""{"values":{},"restart_required":false}""");
        response.Headers.ETag = new("\"two\"");
        pending.SetResult(response);
        await save;
        Assert.Empty(controller.State.Values);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ManagedRedisCannotBeEditedButChecksTheSavedEffectiveConfiguration()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("sensitive-status")
            ? AdminMonitorKit.Json("""{"configured":["redis.url"],"managed_by_env":["redis.url","clientip.trusted_proxies"]}""")
            : request.Method == HttpMethod.Post ? AdminMonitorKit.Json("""{"success":true,"message":"Connected"}""") : Read(request));
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://draft");
        controller.Set("redis.db", "7");
        controller.Set("clientip.trusted_proxies", "127.0.0.1");
        Assert.Empty(controller.State.DirtyKeys);
        await controller.CheckRedisAsync();
        Assert.True(controller.State.ConnectionResult!.Success);
        using var body = System.Text.Json.JsonDocument.Parse(kit.Http.Bodies.Last());
        Assert.Empty(body.RootElement.GetProperty("values").EnumerateObject());
        Assert.Empty(body.RootElement.GetProperty("dirty_keys").EnumerateArray());
    }

    [Fact]
    public async Task DelayedCheckCannotLabelLaterEditsAndRedisIsFrozenDuringSave()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://first");
        var checkResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => checkResponse.Task;
        var check = controller.CheckRedisAsync();
        controller.StageRedisUrl("redis://later");
        checkResponse.SetResult(AdminMonitorKit.Json("""{"success":true,"message":"Connected"}"""));
        await check;
        Assert.Null(controller.State.ConnectionResult);
        var saveResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => saveResponse.Task;
        var save = controller.SaveAsync();
        Assert.True(controller.State.Saving);
        controller.StageRedisUrl("redis://ignored");
        controller.Set("redis.db", "9");
        Assert.Equal("redis://later", controller.State.Values["redis.url"]);
        Assert.Equal("2", controller.State.Values["redis.db"]);
        var ack = AdminMonitorKit.Json("""{"values":{"redis.db":"2"},"restart_required":false}"""); ack.Headers.ETag = new("\"two\"");
        saveResponse.SetResult(ack);
        await save;
        Assert.Empty(controller.State.DirtyKeys);
        Assert.Equal("", controller.State.Values["redis.url"]);
    }

    [Fact]
    public async Task ConflictRequiresReloadAndRedisCheckSendsThePairedDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put ? AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed)
            : request.Method == HttpMethod.Post ? AdminMonitorKit.Json("""{"success":true,"message":"Connected"}""") : Read(request));
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://draft");
        await controller.CheckRedisAsync();
        using (var body = System.Text.Json.JsonDocument.Parse(kit.Http.Bodies.Last()))
        {
            Assert.Equal("redis://draft", body.RootElement.GetProperty("values").GetProperty("redis.url").GetString());
            Assert.Equal("2", body.RootElement.GetProperty("values").GetProperty("redis.db").GetString());
            Assert.Equal(2, body.RootElement.GetProperty("dirty_keys").GetArrayLength());
        }
        await controller.SaveAsync();
        Assert.True(controller.State.Conflict);
        await controller.SaveAsync();
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PUT")));
        await controller.ReloadAsync(true);
        Assert.False(controller.State.Conflict);
        Assert.Contains("redis.url", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task SensitiveFailurePreservesRedisDraftButOnlySavesOtherKeys()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var api = new AdminServerSettingsApi(kit.Client);
        using var store = new AdminServerSettingsStore(api, authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets);
        using var controller = new AdminGeneralSettingsController(api, store, authority);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        await controller.OpenAsync();
        controller.StageRedisUrl("redis://draft");
        controller.Set("branding.server_name", "Changed");
        kit.Http.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("sensitive-status")) return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            if (request.Method == HttpMethod.Put)
            {
                var saved = AdminMonitorKit.Json("""{"values":{"branding.server_name":"Changed"},"restart_required":false}""");
                saved.Headers.ETag = new("\"two\"");
                return Task.FromResult(saved);
            }
            return Task.FromResult(Read(request));
        };
        await controller.ReloadAsync(true);
        Assert.True(controller.State.SensitiveStatusError);
        await controller.SaveAsync();
        using var body = System.Text.Json.JsonDocument.Parse(kit.Http.Bodies.Last());
        var values = body.RootElement.GetProperty("values");
        Assert.True(values.TryGetProperty("branding.server_name", out _));
        Assert.False(values.TryGetProperty("redis.url", out _));
        Assert.False(values.TryGetProperty("redis.db", out _));
        Assert.Contains("redis.url", controller.State.DirtyKeys);
        Assert.Equal("redis://draft", controller.State.Values["redis.url"]);
    }
}
