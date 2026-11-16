using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersWorkspaceTests
{
    [Fact]
    public void RecreatedWorkspaceCannotAdoptAnEarlierPreparedGeneration()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var previous = new AdminUsersWorkspace(authority);
        var prepared = previous.Activate();
        previous.Dispose();
        previous.Dispose();
        using var current = new AdminUsersWorkspace(authority);
        var generation = current.Activate();
        Assert.NotEqual(prepared, generation);
        Assert.False(current.IsCurrent(prepared));
        Assert.True(current.IsCurrent(generation));
        Assert.ThrowsAny<OperationCanceledException>(() => current.AcceptReviewedReadback(prepared));
    }

    [Fact]
    public async Task DisposalCancelsQueuedWorkAndRetainsGateUntilActiveReadRetires()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var workspace = new AdminUsersWorkspace(authority);
        var generation = workspace.Activate();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = workspace.ReadAsync(generation, async (_, _) => { entered.SetResult(true); await release.Task; return 1; });
        await entered.Task;
        var queuedEntered = false;
        var queued = workspace.ReadAsync(generation, (_, _) => { queuedEntered = true; return Task.FromResult(2); });
        workspace.Dispose(); workspace.Dispose();
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(queuedEntered);
        Assert.True(workspace.Token.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => workspace.Activate());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("malformed-json")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"synthetic\",\"user\":{\"id\":\"3\"}} ")]
    public async Task MalformedSuccessfulMutationResponseAlsoRequiresExplicitReadback(string responseBody)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var writes = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) { writes++; return Task.FromResult(AdminMonitorKit.Json(responseBody)); }
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities")) return Task.FromResult(AdminMonitorKit.Json("""{"available":true,"allowed":true,"state":"available"}"""));
            return Task.FromResult(AdminMonitorKit.Json(request.RequestUri.AbsolutePath.EndsWith("/1")
                ? """{"id":"1","role":"admin","is_owner":true,"enabled":true}"""
                : """{"id":"2","role":"user","is_owner":false,"enabled":true}"""));
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.True(workspace.NeedsReview);
        Assert.Equal(1, writes);
        await Assert.ThrowsAsync<AdminUsersReviewRequiredException>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ForegroundReadsNeverOverlapAndRouteRetirementDropsQueuedWork()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var generation = workspace.Activate();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = workspace.ReadAsync(generation, async (_, token) => { entered.SetResult(true); await release.Task; token.ThrowIfCancellationRequested(); return 1; });
        await entered.Task;
        var queuedEntered = false;
        var queued = workspace.ReadAsync(generation, (_, _) => { queuedEntered = true; return Task.FromResult(2); });
        Assert.False(queuedEntered);
        workspace.Suspend();
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(queuedEntered);
        Assert.False(workspace.IsCurrent(generation));
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.PreconditionRequired)]
    public async Task DefinitivePreconditionRefusalCanBeReviewedWithoutPretendingTheWriteApplied(HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) return Task.FromResult(AdminMonitorKit.Json("{}", status));
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities")) return Task.FromResult(AdminMonitorKit.Json("""{"available":true,"allowed":true,"state":"available"}"""));
            return Task.FromResult(AdminMonitorKit.Json(request.RequestUri.AbsolutePath.EndsWith("/1")
                ? """{"id":"1","role":"admin","is_owner":true,"enabled":true}"""
                : """{"id":"2","role":"user","is_owner":false,"enabled":true}"""));
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.True(workspace.NeedsReview);
        Assert.False(workspace.OutcomeUnknown);
        await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        workspace.AcceptReviewedReadback(generation);
        Assert.False(workspace.NeedsReview);
    }

    [Fact]
    public async Task UnknownWriteOutcomeCannotUnlockTheRetainedActionAfterReadback()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities")) return Task.FromResult(AdminMonitorKit.Json("""{"available":true,"allowed":true,"state":"available"}"""));
            return Task.FromResult(AdminMonitorKit.Json(request.RequestUri.AbsolutePath.EndsWith("/1")
                ? """{"id":"1","role":"admin","is_owner":true,"enabled":true}"""
                : """{"id":"2","role":"user","is_owner":false,"enabled":true}"""));
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.True(workspace.NeedsReview);
        var count = kit.Http.Requests.Count;
        await Assert.ThrowsAsync<AdminUsersReviewRequiredException>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.Equal(count, kit.Http.Requests.Count);
        await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        Assert.True(workspace.OutcomeUnknown);
        Assert.Throws<AdminUsersReviewRequiredException>(() => workspace.AcceptReviewedReadback(generation));
        Assert.True(workspace.NeedsReview);
        workspace.Suspend();
        Assert.ThrowsAny<OperationCanceledException>(() => workspace.AcceptReviewedReadback(generation));
        var refreshed = workspace.Activate();
        Assert.NotEqual(generation, refreshed);
        Assert.False(workspace.NeedsReview);
        Assert.False(workspace.OutcomeUnknown);
    }
}
