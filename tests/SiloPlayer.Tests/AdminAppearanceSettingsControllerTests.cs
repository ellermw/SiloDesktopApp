using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAppearanceSettingsControllerTests
{
    private static HttpResponseMessage Read(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/settings/effective", StringComparison.Ordinal))
        {
            var response = AdminMonitorKit.Json("""{"branding.accent_color":"#4f46e5","overlays.enabled":"true","defaults.card_overlays":"","defaults.ui.title_art":"true"}""");
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            return response;
        }
        if (path.EndsWith("/settings/sensitive-status", StringComparison.Ordinal)) return AdminMonitorKit.Json("""{"configured":[],"managed_by_env":[]}""");
        if (path.EndsWith("/settings/restart-keys", StringComparison.Ordinal)) return AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
        if (path.EndsWith("/theme/branding", StringComparison.Ordinal)) return AdminMonitorKit.Json("""{"storage_available":true}""");
        if (path.EndsWith("/settings/overlay-config", StringComparison.Ordinal)) return AdminMonitorKit.Json("""{"enabled":true,"images":[]}""");
        throw new InvalidOperationException("Unexpected synthetic Appearance read.");
    }

    [Fact]
    public async Task AcknowledgedSaveDoesNotEraseANewerDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? validator = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                validator = request.Headers.IfMatch.ToString();
                return pending.Task;
            }
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        controller.Set("branding.accent_color", "#10b981");
        var saving = controller.SaveAsync();
        Assert.True(controller.State.Saving);
        controller.Set("branding.accent_color", "#0ea5e9");
        var response = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#10b981"},"restart_required":false}""");
        response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
        pending.SetResult(response);
        await saving;
        Assert.Equal("\"v1\"", validator);
        Assert.Equal("#0ea5e9", controller.State.Values["branding.accent_color"]);
        Assert.Contains("branding.accent_color", controller.State.DirtyKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageDeletionWithoutStoragePreservesDirtyFieldConflictBarrier(bool concurrentChange)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var removed = false;
        string? savedValidator = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                removed = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (request.Method == HttpMethod.Put)
            {
                savedValidator = request.Headers.IfMatch.ToString();
                var result = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#10b981"},"restart_required":false}""");
                result.Headers.ETag = new EntityTagHeaderValue("\"v3\"");
                return Task.FromResult(result);
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/theme/branding", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("""{"storage_available":false}"""));
            if (removed && request.RequestUri.AbsolutePath.EndsWith("/settings/effective", StringComparison.Ordinal))
            {
                var result = AdminMonitorKit.Json(concurrentChange
                    ? """{"branding.accent_color":"#ff0000","overlays.enabled":"true","defaults.card_overlays":"","defaults.ui.title_art":"true"}"""
                    : """{"branding.accent_color":"#4f46e5","overlays.enabled":"true","defaults.card_overlays":"","defaults.ui.title_art":"true"}""");
                result.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
                return Task.FromResult(result);
            }
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("branding.accent_color", "#10b981");
        await controller.DeleteAssetAsync("mark");
        Assert.True(removed);
        Assert.Equal("#10b981", controller.State.Values["branding.accent_color"]);
        Assert.Equal(concurrentChange, controller.State.Conflict);
        await controller.SaveAsync();
        Assert.Equal(concurrentChange ? null : "\"v2\"", savedValidator);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task ConflictOrUnknownOutcomeKeepsDraftAndBlocksReplayUntilReload(HttpStatusCode failure, bool conflict)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? AdminMonitorKit.Json("{}", failure) : Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("branding.accent_color", "#10b981");
        await controller.SaveAsync();
        Assert.Equal(conflict, controller.State.Conflict);
        Assert.Equal(!conflict, controller.State.OutcomeUnknown);
        Assert.Equal("#10b981", controller.State.Values["branding.accent_color"]);
        await controller.SaveAsync();
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT", StringComparison.Ordinal)));
        await controller.ReloadAsync();
        Assert.False(controller.State.Conflict);
        Assert.False(controller.State.OutcomeUnknown);
        Assert.Equal("#10b981", controller.State.Values["branding.accent_color"]);
        Assert.Contains("branding.accent_color", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task MissingManagedStatusBlocksSettingsWrites()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/sensitive-status", StringComparison.Ordinal)
            ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.StatusUnavailable);
        controller.Set("branding.accent_color", "#10b981");
        await controller.SaveAsync();
        Assert.Empty(controller.State.DirtyKeys);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidAccentNeverReachesTheServer()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("branding.accent_color", "not-a-color");
        await controller.SaveAsync();
        Assert.NotNull(controller.State.Error);
        Assert.False(controller.State.Saving);
        Assert.Contains("branding.accent_color", controller.State.DirtyKeys);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuspendClearsDraftAndRejectsAnOldSaveCompletion()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put ? pending.Task : Task.FromResult(Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("branding.accent_color", "#10b981");
        var saving = controller.SaveAsync();
        controller.Suspend();
        var response = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#10b981"},"restart_required":false}""");
        response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
        pending.SetResult(response);
        await saving;
        Assert.Null(controller.State.Owner);
        Assert.Empty(controller.State.Values);
        Assert.Empty(controller.State.DirtyKeys);
    }
}
