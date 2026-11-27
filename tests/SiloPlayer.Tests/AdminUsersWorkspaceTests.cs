using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersWorkspaceTests
{
    [Fact]
    public async Task IndependentReadsReachTheApiBeforeTheSlowestResponseCompletes()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var generation = workspace.Activate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        kit.Http.Respond = async (request, token) =>
        {
            if (Interlocked.Increment(ref arrivals) == 3) started.TrySetResult();
            if (request.RequestUri!.AbsolutePath.EndsWith("/profiles")) await slow.Task.WaitAsync(token);
            else await Task.Delay(80, token);
            return AdminMonitorKit.Json("{}");
        };
        Task<AdminUsersDocument> Read(string suffix) =>
            workspace.ReadAsync(generation, (lease, token) => api.ReadAsync(lease, "/api/v2/admin/users/2/" + suffix, token));
        var profiles = Read("profiles");
        var usage = Read("watch-summary");
        var devices = Read("devices");
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.WhenAll(usage, devices).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(profiles.IsCompleted);
        }
        finally
        {
            slow.TrySetResult();
            await Task.WhenAll(profiles, usage, devices).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

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
        var writer = workspace.WriteAsync(generation, new AdminUsersApi(kit.Client), null!, new(AdminUsersOperation.ViewAs));
        var queuedEntered = false;
        var queued = workspace.ReadAsync(generation, (_, _) => { queuedEntered = true; return Task.FromResult(2); });
        workspace.Dispose(); workspace.Dispose();
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
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
    [InlineData("{\"access_token\":\"synthetic\",\"user\":true}")]
    public async Task MalformedSuccessfulMutationResponseAlsoRequiresExplicitReadback(string responseBody)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var writes = 0;
        var logouts = new List<string?>();
        kit.Http.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/logout")
            {
                logouts.Add(request.Headers.Authorization?.Parameter);
                return Task.FromResult(AdminMonitorKit.Json("{}"));
            }
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
        Assert.Equal(responseBody.Contains("access_token") ? 1 : 0, logouts.Count);
        Assert.All(logouts, bearer => Assert.Equal("synthetic", bearer));
        await Assert.ThrowsAsync<AdminUsersReviewRequiredException>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task InvalidCustomQuotaLeavesTheWorkspaceEditableWithoutSendingAWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var writes = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) { writes++; return Task.FromResult(AdminMonitorKit.Json("{}")); }
            var reply = AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/capabilities")
                ? """{"available":true,"allowed":true,"state":"available"}"""
                : request.RequestUri.AbsolutePath.EndsWith("/1")
                    ? """{"id":"1","role":"admin","is_owner":false,"enabled":true}"""
                    : """{"id":"2","role":"user","is_owner":false,"enabled":true}""");
            reply.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"quota-v1\"");
            return Task.FromResult(reply);
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        var quota = new JsonObject { ["limit_mode"] = "custom", ["approval_mode"] = "inherit", ["max_requests"] = null, ["window_days"] = 7 };
        await Assert.ThrowsAsync<ArgumentException>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.Quota, quota)));
        Assert.Equal(0, writes);
        Assert.False(workspace.NeedsReview);
        Assert.False(workspace.OutcomeUnknown);
        // The same workspace remains usable for an explicitly chosen, valid write.
        quota["max_requests"] = 5;
        var path = "/api/v2/admin/request-users/2/limit";
        await workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.Quota, quota,
            Readbacks: [new(path, review.User.Copy(), "\"quota-v1\"", true)]));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreSendReadFailureDoesNotBecomeAnUnknownWrite(bool cancelled)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var fail = false;
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (fail)
            {
                if (cancelled) throw new TaskCanceledException("Synthetic GET timeout");
                throw new HttpRequestException("Synthetic GET failure");
            }
            return Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/capabilities")
                ? """{"available":true,"allowed":true,"state":"available"}"""
                : request.RequestUri.AbsolutePath.EndsWith("/1")
                    ? """{"id":"1","role":"admin","is_owner":true,"enabled":true}"""
                    : """{"id":"2","role":"user","is_owner":false,"enabled":true}"""));
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        fail = true;
        await Assert.ThrowsAnyAsync<Exception>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        Assert.True(workspace.IsCurrent(generation));
        Assert.False(workspace.NeedsReview);
        Assert.False(workspace.OutcomeUnknown);
    }

    [Fact]
    public async Task AWaitingWriteBlocksNewReadsAndRouteRetirementDropsQueuedWork()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var generation = workspace.Activate();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = workspace.ReadAsync(generation, async (_, token) => { entered.SetResult(true); await release.Task; token.ThrowIfCancellationRequested(); return 1; });
        await entered.Task;
        var writer = workspace.WriteAsync(generation, new AdminUsersApi(kit.Client), null!, new(AdminUsersOperation.ViewAs));
        Assert.False(writer.IsCompleted);
        var queuedEntered = false;
        var queued = workspace.ReadAsync(generation, (_, _) => { queuedEntered = true; return Task.FromResult(2); });
        Assert.False(queuedEntered);
        workspace.Suspend();
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownWriteOutcomeCannotUnlockTheRetainedActionAfterReadback(bool timeout)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get)
            {
                if (timeout) throw new TaskCanceledException("Synthetic write response timeout");
                return Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            }
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
