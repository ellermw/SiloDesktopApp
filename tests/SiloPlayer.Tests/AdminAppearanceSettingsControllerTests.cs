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
    public async Task AFailedManagedStatusOnlyEndsTheWaitBecauseNoAppearanceKeyIsEnvManaged()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var result = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#10b981"},"restart_required":false}""");
                result.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
                return Task.FromResult(result);
            }
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/sensitive-status", StringComparison.Ordinal)
                ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        Assert.Null(controller.State.Error);
        controller.Set("branding.accent_color", "#10b981");
        await controller.SaveAsync();
        Assert.Single(kit.Http.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task OpeningAndRestagingStoredValuesNeverMarksEditsOrSends()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        // A control reporting its loaded value as a change stages the stored value back.
        controller.Set("branding.accent_color", "#4f46e5");
        controller.Set("overlays.enabled", "true");
        controller.Set("defaults.ui.title_art", "true");
        controller.Set("defaults.card_overlays", "");
        Assert.Empty(controller.State.DirtyKeys);
        await controller.SaveAsync();
        Assert.DoesNotContain(kit.Http.Requests, request => !request.StartsWith("GET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedSaveKeepsTheTypedValueAndAllowsACorrectedRetry()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var puts = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Put) return Task.FromResult(Read(request));
            if (++puts == 1)
                return Task.FromResult(AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","title":"The request did not pass validation; see errors.","status":422}""", HttpStatusCode.UnprocessableEntity));
            var result = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#0ea5e9"},"restart_required":false}""");
            result.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return Task.FromResult(result);
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("branding.accent_color", "#10b981");
        await controller.SaveAsync();
        Assert.NotNull(controller.State.Error);
        Assert.False(controller.State.OutcomeUnknown);
        Assert.False(controller.State.Conflict);
        Assert.Equal("#10b981", controller.State.Values["branding.accent_color"]);
        controller.Set("branding.accent_color", "#0ea5e9");
        await controller.SaveAsync();
        Assert.Equal(2, puts);
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ARefusedImageUploadShowsTheServersReasonAndAnExplicitRetrySendsAgain()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/payload_too_large","title":"The image exceeds the maximum size for this asset.","status":413}""", HttpStatusCode.RequestEntityTooLarge)
            : Read(request));
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.False(await controller.UploadAssetAsync("wordmark", [1, 2, 3], "image/png"));
        Assert.Equal("The image exceeds the maximum size for this asset.", controller.State.AssetError);
        Assert.False(controller.State.AssetsUnknown);
        Assert.True(controller.State.CanWriteImages);
        Assert.False(await controller.UploadAssetAsync("wordmark", [1, 2, 3], "image/png"));
        Assert.Equal(2, kit.Http.Requests.Count(request => request.StartsWith("POST", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnUnknownImageOutcomeBlocksImageWritesButNotSettingsUntilAReload()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.BadGateway));
            if (request.Method == HttpMethod.Put)
            {
                var result = AdminMonitorKit.Json("""{"values":{"branding.accent_color":"#10b981"},"restart_required":false}""");
                result.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
                return Task.FromResult(result);
            }
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.False(await controller.UploadAssetAsync("mark", [1], "image/png"));
        Assert.True(controller.State.AssetsUnknown);
        Assert.NotNull(controller.State.Error);
        Assert.False(await controller.UploadAssetAsync("mark", [1], "image/png"));
        Assert.False(await controller.DeleteAssetAsync("mark"));
        Assert.Single(kit.Http.Requests, request => !request.StartsWith("GET", StringComparison.Ordinal));
        controller.Set("branding.accent_color", "#10b981");
        Assert.True(controller.State.CanSave);
        // A settings save in between must not hide the explanation that offers Reload, the only way to lift the image block.
        await controller.SaveAsync();
        Assert.True(controller.State.AssetsUnknown); Assert.NotNull(controller.State.Error); Assert.False(controller.State.CanWriteImages);
        controller.Set("branding.accent_color", "#059669");
        await controller.ReloadAsync();
        Assert.False(controller.State.AssetsUnknown);
        Assert.True(controller.State.CanWriteImages);
        Assert.Contains("branding.accent_color", controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AFailedOverlayOrBrandingReadIsVisibleBlocksThoseImagesAndReloadRecovers()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var fail = true;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (fail && (path.EndsWith("/settings/overlay-config", StringComparison.Ordinal) || path.EndsWith("/theme/branding", StringComparison.Ordinal)))
                return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        Assert.NotNull(controller.State.Error);
        Assert.Null(controller.State.OverlayConfig);
        Assert.Null(controller.State.Branding);
        Assert.False(await controller.DeleteOverlayImageAsync(new string('a', 64) + ".webp"));
        Assert.False(await controller.DeleteAssetAsync("mark"));
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
        fail = false;
        await controller.ReloadAsync();
        Assert.Null(controller.State.Error);
        Assert.NotNull(controller.State.OverlayConfig);
        Assert.NotNull(controller.State.Branding);
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(25, true)]
    public async Task TheManifestRevisionDecidesStylesAndRatingSourcesComeFromTheCapability(int revision, bool styles)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/settings/contract/capabilities", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("{\"api_version\":1,\"manifest_revision\":" + revision + "}"));
            if (path.EndsWith("/capabilities/ratings", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("""{"state":"available","sources":[{"source":"imdb"},{"source":"rt_critic"}]}"""));
            if (path.EndsWith("/settings/values/effective", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json("""{"items":[{"key":"ui.card_presentation","value":{"poster_size":"large"},"source":"profile"}],"revision":1}"""));
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority, new SettingsApi(kit.Client), new CatalogApi(kit.Client));
        await controller.OpenAsync();
        Assert.Equal(styles, controller.State.SupportsStyles);
        Assert.Contains("rt_critic", controller.State.ShownRatingSources);
        Assert.DoesNotContain("rt_audience", controller.State.ShownRatingSources);
        Assert.Equal("large", controller.State.PosterSize);
        Assert.Null(controller.State.Error);
    }

    [Fact]
    public async Task DeletingALibraryImageRereadsTheLibraryAndLeavesStoredReferencesForTheAdmin()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var reference = new string('b', 64) + ".webp";
        var deleted = false;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete) { deleted = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/settings/overlay-config", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json(deleted ? """{"enabled":true,"images":[],"image_uploads_available":true}"""
                    : $$"""{"enabled":true,"images":[{"ref":"{{reference}}","name":"4K","url":"/api/v2/overlay-images/{{reference}}","width":300,"height":120}],"image_uploads_available":true}"""));
            if (path.EndsWith("/settings/effective", StringComparison.Ordinal))
            {
                var stored = AdminMonitorKit.Json("""{"branding.accent_color":"","overlays.enabled":"true","defaults.card_overlays":"{\"version\":2,\"preset\":\"classic\",\"items\":{\"resolution\":{\"enabled\":true,\"image\":\"REF\"}}}","defaults.ui.title_art":""}""".Replace("REF", reference));
                stored.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
                return Task.FromResult(stored);
            }
            return Task.FromResult(Read(request));
        };
        using var controller = new AdminAppearanceSettingsController(new(kit.Client), new(kit.Client), authority);
        await controller.OpenAsync();
        var before = controller.State.Values["defaults.card_overlays"];
        Assert.Single(controller.State.OverlayConfig!.Images);
        Assert.True(await controller.DeleteOverlayImageAsync(reference));
        Assert.Empty(controller.State.OverlayConfig!.Images);
        Assert.Equal(before, controller.State.Values["defaults.card_overlays"]);
        Assert.Contains(reference, before);
        Assert.Empty(controller.State.DirtyKeys);
        Assert.Single(kit.Http.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
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
