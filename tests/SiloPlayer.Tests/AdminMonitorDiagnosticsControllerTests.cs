using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorDiagnosticsControllerTests
{
    [Fact]
    public async Task ReadTimeoutsSettleLoadingAndAllowRetry()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.False(controller.State.Loading);
        Assert.False(controller.State.StatusLoading);
        Assert.NotNull(controller.State.Error);
        Assert.NotNull(controller.State.StatusError);
        kit.Http.Respond = (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        await controller.RefreshAsync();
        Assert.Null(controller.State.Error);
        Assert.Null(controller.State.StatusError);
        Assert.True(controller.State.CanToggle);

        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        await controller.SelectAsync("1");
        Assert.False(controller.State.DetailLoading);
        Assert.NotNull(controller.State.DetailError);
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("delete")]
    [InlineData("download")]
    public async Task ActionTimeoutsClearBusyFlagsAndReportFailure(string operation)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        await controller.SelectAsync("1");
        using var action = controller.CaptureAction();
        kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout", new TimeoutException()));
        if (operation == "toggle") await controller.SetUploadsEnabledAsync(action, false);
        else if (operation == "delete") await controller.DeleteAsync(action, "1");
        else
        {
            using var destination = new MemoryStream();
            Assert.False(await controller.DownloadAsync(action, new() { Id = "1", State = "ready" }, destination));
        }
        Assert.False(controller.State.ToggleBusy);
        Assert.False(controller.State.Deleting);
        Assert.False(controller.State.Downloading);
        Assert.NotNull(controller.State.ActionError);
        Assert.True(action.IsCurrent);
        kit.Http.Respond = (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        await controller.RefreshAsync();
        Assert.False(controller.State.StatusLoading);
        Assert.True(controller.State.CanToggle);
    }

    [Fact]
    public async Task ManualRetryAfterStaleCursorStartsAtTheFirstPageWithoutReplayingActions()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/reports")
            ? request.RequestUri.Query.Contains("cursor=") ? AdminMonitorKit.Json("""{"type":"about:blank","title":"Invalid cursor","status":400,"code":"invalid_cursor"}""", HttpStatusCode.BadRequest) : AdminMonitorKit.Json("{\"items\":[{\"id\":\"1\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"older\"}}")
            : Reply(request.RequestUri.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        await controller.NextAsync();
        Assert.NotNull(controller.State.Error);
        var count = kit.Http.Requests.Count;
        await controller.RefreshAsync();
        Assert.Equal(1, controller.State.Page);
        Assert.Null(controller.State.Error);
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
        Assert.True(kit.Http.Requests.Count > count);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("DELETE") || request.StartsWith("PUT"));
    }

    [Fact]
    public async Task LoadingANewPageDoesNotExposePreviousRowsAlongsideItsLoadingState()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.RequestUri!.Query.Contains("cursor=") ? pending.Task : Task.FromResult(request.RequestUri.AbsolutePath.EndsWith("/reports")
            ? AdminMonitorKit.Json("{\"items\":[{\"id\":\"1\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"older\"}}") : Reply(request.RequestUri.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.Single(controller.State.Reports);
        var next = controller.NextAsync();
        Assert.True(controller.State.Loading);
        Assert.Empty(controller.State.Reports);
        pending.SetResult(AdminMonitorKit.Json("{\"items\":[],\"page\":{\"has_more\":false}}"));
        await next;
        Assert.False(controller.State.Loading);
    }

    [Fact]
    public async Task LateDetailCannotReplaceAChangedSelectionOrReappearAfterRetirement()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/reports/slow") ? pending.Task : Task.FromResult(Reply(request.RequestUri.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        var slow = controller.SelectAsync("slow");
        await controller.SelectAsync("1");
        pending.SetResult(AdminMonitorKit.Json("{\"id\":\"slow\",\"manifest\":{\"private\":true}}"));
        await slow;
        Assert.Equal("1", controller.State.Detail!.Id);
        authority.Retire();
        Assert.Null(controller.State.Detail);
        Assert.Null(controller.State.Notice);
    }

    [Fact]
    public async Task LateToggleAcknowledgementAfterSameAccountAdminLossDoesNotPublishOrRefetch()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put ? pending.Task : Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        using var action = controller.CaptureAction();
        var update = controller.SetUploadsEnabledAsync(action, false);
        var requests = kit.Http.Requests.Count;
        kit.SetRole("user"); kit.SetRole("admin");
        var response = AdminMonitorKit.Json("{\"value\":\"false\"}"); response.Headers.ETag = new("\"two\""); pending.SetResult(response);
        await update;
        Assert.Null(controller.State.Owner);
        Assert.Null(controller.State.Notice);
        Assert.Equal(requests, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task ToggleWaitsForAcknowledgementAndStorageUnavailableRemainsEnabled()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var write = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disabled = false;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put) { sent.TrySetResult(); return write.Task; }
            return Task.FromResult(Reply(request.RequestUri!.AbsolutePath, disabled ? "disabled" : "storage_unavailable"));
        };
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        Assert.True(controller.State.UploadsEnabled);
        Assert.Equal("storage_unavailable", controller.State.Capabilities!.Status);
        using var action = controller.CaptureAction();
        var pending = controller.SetUploadsEnabledAsync(action, false);
        await sent.Task;
        Assert.True(controller.State.UploadsEnabled);
        Assert.True(controller.State.ToggleBusy);
        disabled = true;
        var response = AdminMonitorKit.Json("""{"key":"diagnostics.uploads_enabled","value":"false"}""");
        write.TrySetResult(response);
        await pending;
        Assert.False(controller.State.UploadsEnabled);
        Assert.False(controller.State.ToggleBusy);
        Assert.Null(controller.State.ActionError);
        Assert.Equal("\"one\"", controller.State.SettingsRevision);
    }

    [Fact]
    public async Task FilterApplyClosesDetailAndStartsAtFreshCursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        await controller.SelectAsync("1");
        Assert.NotNull(controller.State.Detail);
        await controller.ApplyAsync(new() { UserId = "12", Platform = "roku" });
        Assert.Null(controller.State.Detail);
        Assert.Equal(1, controller.State.Page);
        Assert.EndsWith("/reports?user_id=12&platform=roku&limit=25", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task ConfirmationIntentRetiresOnRoleLossAndNeverDeletes()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        using var controller = new DiagnosticsController(new(kit.Client), authority);
        await controller.OpenAsync();
        await controller.SelectAsync("1");
        using var intent = controller.CaptureAction();
        kit.SetRole("user");
        kit.SetRole("admin");
        await controller.DeleteAsync(intent, "1");
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("DELETE"));
        Assert.Empty(controller.State.Reports);
        Assert.Null(controller.State.Detail);
        Assert.Null(controller.State.Capabilities);
    }

    internal static HttpResponseMessage Reply(string path, string status = "available")
    {
        if (path.EndsWith("/effective"))
        {
            var response = AdminMonitorKit.Json("""{"diagnostics.uploads_enabled":"true","server.log_level":"info"}""");
            response.Headers.ETag = new("\"one\"");
            return response;
        }
        if (path.EndsWith("/capabilities")) return AdminMonitorKit.Json("{\"status\":\"" + status + "\",\"state\":\"available\",\"allowed\":true,\"retention_days\":14,\"max_bundle_bytes\":1048576}");
        if (path.EndsWith("/reports/1")) return AdminMonitorKit.Json("""{"id":"1","short_id":"ABC","state":"receiving","manifest":{"extension":{"future":true}}}""");
        return AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}""");
    }
}
