using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
namespace SiloPlayer.Tests;

public sealed class AdminMonitorRestartTests
{
    [Theory]
    [InlineData("restart_requested")]
    [InlineData("already_requested")]
    public async Task RestartSendsOneEmptyIntentAndReturnsItsStatus(string status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(
            $$"""{"status":"{{status}}","message":"synthetic","notified_sessions":2}"""));
        var result = await new AdminMonitorApi(kit.Client).RequestMonitorServerRestartAsync(lease);
        Assert.Equal(status, result.Status);
        Assert.Equal(2, result.NotifiedSessions);
        Assert.Equal(["POST /api/v2/admin/server/restart"], kit.Http.Requests);
        Assert.Equal(["{}"], kit.Http.Bodies);
    }

    [Fact]
    public async Task RejectedRestartDoesNotRefreshCredentialsOrReplayTheIntent()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(
            """{"type":"https://silo.test/problems/unauthorized","title":"Unauthorized","status":401}""", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminMonitorApi(kit.Client).RequestMonitorServerRestartAsync(lease));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ConfirmationAuthorityCannotBeRecapturedAfterRoleLoss()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var confirmationLease = authority.Capture();
        kit.SetRole("user");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminMonitorApi(kit.Client).RequestMonitorServerRestartAsync(confirmationLease));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task CompletionAfterAuthorityRetirementIsDiscarded()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => response.Task;
        var pending = new AdminMonitorApi(kit.Client).RequestMonitorServerRestartAsync(lease);
        kit.SetRole("user");
        response.SetResult(AdminMonitorKit.Json("""{"status":"restart_requested","message":"synthetic","notified_sessions":0}"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
