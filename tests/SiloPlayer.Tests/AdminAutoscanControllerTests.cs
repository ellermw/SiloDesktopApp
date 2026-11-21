using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanControllerTests
{
    [Fact]
    public async Task LoadPublishesSettingsAndSourcesUnderCurrentOwner()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/settings")
            ? """{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":60}"""
            : """{"items":[{"id":"one","delivery_mode":"poll","enabled":true}],"page":{"has_more":false}}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new AdminAutoscanApi(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        Assert.True(controller.State.Settings!.Enabled);
        Assert.Equal("one", Assert.Single(controller.State.Sources).Id);
    }

    [Fact]
    public async Task RetiredPageDiscardsLateSourceRead()
    {
        using var kit = AdminMonitorKit.Create();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/settings")
            ? Task.FromResult(AdminMonitorKit.Json("""{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":60}"""))
            : request.RequestUri.AbsolutePath.EndsWith("/sources") ? pending.Task
            : Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new AdminAutoscanApi(kit.Client), authority);
        var load = controller.OpenAsync();
        controller.Close();
        pending.SetResult(AdminMonitorKit.Json("""{"items":[{"id":"retired"}],"page":{"has_more":false}}"""));
        await load;
        Assert.Empty(controller.State.Sources);
        Assert.Null(controller.State.Owner);
    }
}
