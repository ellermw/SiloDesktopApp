using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLoggingControllerTests
{
    [Fact]
    public async Task RestartRegistryFailureDoesNotBlockSettingsOrDiscardDraft()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/restart-keys") ? AdminMonitorKit.Json("{}", HttpStatusCode.NotFound) : AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri.AbsolutePath));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.Loaded);
        controller.Set("server.log_quiet", "metadata");
        Assert.Single(controller.State.DirtyKeys);
        Assert.True(controller.State.ServerAdvancedOpen);
        Assert.Empty(controller.State.RestartKeys.Keys);
        controller.Discard();
        Assert.Empty(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ConflictKeepsDraftAndCannotOverwriteWithoutExplicitReconciliation()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put ? AdminMonitorKit.Json("{}", HttpStatusCode.PreconditionFailed) : AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri!.AbsolutePath));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("server.log_level", "debug");
        using var action = controller.CaptureAction();
        await controller.SaveAsync(action);
        Assert.True(controller.State.Conflict);
        Assert.Single(controller.State.DirtyKeys);
        Assert.Equal("debug", controller.State.Values["server.log_level"]);
        await controller.SaveAsync(action);
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT")));
        var latest = await controller.PrepareReconciliationAsync(action);
        Assert.NotNull(latest);
        Assert.True(controller.State.Conflict);
        controller.AcceptReconciliation(action, latest!, preserveDraft: true);
        Assert.False(controller.State.Conflict);
        Assert.Single(controller.State.DirtyKeys);
    }

    [Fact]
    public async Task LateSaveCannotRestoreRetiredDraftOrShowSuccess()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var ack = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put ? ack.Task : Task.FromResult(AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri!.AbsolutePath));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("server.log_level", "debug");
        using var action = controller.CaptureAction();
        var save = controller.SaveAsync(action);
        authority.Retire();
        var response = AdminMonitorKit.Json("""{"values":{"server.log_level":"debug"},"restart_required":true}""");
        response.Headers.ETag = new("\"two\"");
        ack.SetResult(response);
        await save;
        Assert.Empty(controller.State.Values);
        Assert.Null(controller.State.Notice);
        Assert.False(controller.State.RestartRequired);
    }
}
