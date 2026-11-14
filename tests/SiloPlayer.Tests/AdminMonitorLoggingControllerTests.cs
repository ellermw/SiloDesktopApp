using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLoggingControllerTests
{
    [Fact]
    public async Task SettingsTimeoutSettlesLoadingAndAllowsReload()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.False(controller.State.Loading);
        Assert.False(controller.State.Loaded);
        Assert.NotNull(controller.State.Error);
        Assert.True(controller.State.Owner!.IsCurrent);

        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri!.AbsolutePath));
        using var action = controller.CaptureAction();
        var latest = await controller.PrepareReconciliationAsync(action);
        Assert.NotNull(latest);
        controller.AcceptReconciliation(action, latest!, false);
        Assert.True(controller.State.Loaded);
        Assert.Null(controller.State.Error);
    }

    [Fact]
    public async Task SaveAndReconciliationTimeoutsKeepDraftAndSettleBusyState()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri!.AbsolutePath));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("server.log_level", "debug");
        using var action = controller.CaptureAction();
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        await controller.SaveAsync(action);
        Assert.False(controller.State.Saving);
        Assert.NotNull(controller.State.Error);
        Assert.Single(controller.State.DirtyKeys);
        Assert.Equal("debug", controller.State.Values["server.log_level"]);
        Assert.Null(await controller.PrepareReconciliationAsync(action));
        Assert.NotNull(controller.State.Error);
        await controller.SaveAsync(action);
        Assert.Equal(2, kit.Http.Requests.Count(request => request.StartsWith("PUT")));
    }

    [Fact]
    public async Task TransportCatchUpKeepsTheDraftAndAllowsSavingUnderTheNewContext()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorDiagnosticsControllerTests.Reply(request.RequestUri!.AbsolutePath));
        using var controller = new LoggingController(new(kit.Client), authority);
        await controller.OpenAsync();
        controller.Set("server.log_level", "debug");
        var owner = controller.State.Owner!;
        using var action = controller.CaptureAction();
        Assert.True(kit.Client.TryInvalidateAccessContext(owner.Context, out var updated, transportCatchUp: true));
        Assert.Same(owner, controller.State.Owner);
        Assert.Single(controller.State.DirtyKeys);
        Assert.Equal("debug", controller.State.Values["server.log_level"]);
        Assert.Equal(updated, action.Context);
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json("""{"values":{"server.log_level":"debug"},"restart_required":false}""");
            response.Headers.ETag = new("\"two\"");
            return Task.FromResult(response);
        };
        await controller.SaveAsync(action);
        Assert.False(controller.State.Saving);
        Assert.Empty(controller.State.DirtyKeys);
        Assert.NotNull(controller.State.Notice);
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT")));
    }

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
