using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Tests;

public sealed class AdminUsersTransportTests
{
    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(payload).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(payload));
    }

    private sealed class RetiringReceiptContent(Action retire) : HttpContent
    {
        private static readonly byte[] Payload = System.Text.Encoding.UTF8.GetBytes("""{"access_token":"synthetic-target","refresh_token":"synthetic-refresh","user":{"id":"2"}}""");
        protected override bool TryComputeLength(out long length) { length = Payload.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(Payload).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            retire();
            return Task.FromResult<Stream>(new MemoryStream(Payload));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceivedViewAsReceiptIsRevokedWhenItsOwnerRetiresDuringBodyRead(bool authorityRetires)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var workspace = new AdminUsersWorkspace(authority);
        var api = new AdminUsersApi(kit.Client);
        var logouts = new List<(Uri Uri, string? Bearer, bool Profile, bool Body, bool Cancelled)>();
        kit.Http.Respond = (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
            if (request.RequestUri!.AbsolutePath == "/api/v2/auth/logout")
            {
                logouts.Add((request.RequestUri, request.Headers.Authorization?.Parameter,
                    request.Headers.Contains("X-Profile-Id") || request.Headers.Contains("X-Profile-Token"), request.Content != null, token.IsCancellationRequested));
                return Task.FromResult(AdminMonitorKit.Json("{}"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new RetiringReceiptContent(() => { if (authorityRetires) kit.SetRole("user"); else workspace.Suspend(); })
            });
        };
        var generation = workspace.Activate();
        var review = await workspace.ReadAsync(generation, (lease, token) => api.ReviewAsync(lease, "2", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.WriteAsync(generation, api, review, new(AdminUsersOperation.ViewAs)));
        var logout = Assert.Single(logouts);
        Assert.Equal("https://silo.test/api/v2/auth/logout", logout.Uri.AbsoluteUri);
        Assert.Equal("synthetic-target", logout.Bearer);
        Assert.False(logout.Profile);
        Assert.False(logout.Body);
        Assert.False(logout.Cancelled);
        Assert.Single(kit.Http.Requests.Where(path => path.EndsWith("/impersonate")));
    }

    [Theory]
    [InlineData("https://silo.test", "")]
    [InlineData("https://silo.test/silo", "/silo")]
    [InlineData("https://silo.test/silo/", "/silo")]
    public async Task UnadoptedReceiptUsesOriginalOriginAndTemporaryBearerForOneLogout(string baseUrl, string prefix)
    {
        using var kit = AdminMonitorKit.Create();
        kit.Client.SetBaseUrl(baseUrl);
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var logouts = new List<(Uri Uri, string? Bearer, bool Profile, bool Body, bool Cancelled)>();
        kit.Http.Respond = (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
            if (request.RequestUri!.AbsolutePath == prefix + "/api/v2/auth/logout")
            {
                logouts.Add((request.RequestUri, request.Headers.Authorization?.Parameter,
                    request.Headers.Contains("X-Profile-Id") || request.Headers.Contains("X-Profile-Token"), request.Content != null, token.IsCancellationRequested));
                return Task.FromResult(AdminMonitorKit.Json("{}"));
            }
            return Task.FromResult(AdminMonitorKit.Json("""{"access_token":"synthetic-target","refresh_token":"synthetic-refresh","user":{"id":"2"}}"""));
        };
        var api = new AdminUsersApi(kit.Client);
        var result = await api.WriteAsync(lease, await api.ReviewAsync(lease, "2"), new(AdminUsersOperation.ViewAs));
        Assert.Contains("POST " + prefix + "/api/v2/admin/users/2/impersonate", kit.Http.Requests);
        kit.Client.SetBaseUrl("https://replacement.test");
        await result.DiscardViewAsAsync();
        await result.DiscardViewAsAsync();
        var logout = Assert.Single(logouts);
        Assert.Equal("https://silo.test" + prefix + "/api/v2/auth/logout", logout.Uri.AbsoluteUri);
        Assert.Equal("synthetic-target", logout.Bearer);
        Assert.False(logout.Profile);
        Assert.False(logout.Body);
        Assert.False(logout.Cancelled);
        Assert.False(result.Body!.ContainsKey("access_token"));
        Assert.False(result.Body.ContainsKey("refresh_token"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorResponseUsesTheSameByteBoundBeforeProblemDecoding(bool unknownLength)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"detail\":\"Synthetic bounded failure.\",\"padding\":\"" + new string('x', 4 * 1024 * 1024) + "\"}");
        kit.Http.Respond = (_, _) =>
        {
            HttpContent content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = content });
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminUsersApi(kit.Client).ReadAsync(lease, "/api/v2/admin/users/2"));
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeItCanBecomeAnEditableSnapshot()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1])
        });
        var api = new AdminUsersApi(kit.Client);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ReadAsync(lease, "/api/v2/admin/users/2"));
    }

    [Fact]
    public async Task OversizedUnpagedCollectionIsNotTreatedAsACompleteBaseline()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var rows = new JsonArray();
        for (var index = 0; index <= AdminUsersApi.MaxRows; index++) rows.Add(new JsonObject { ["id"] = index.ToString() });
        var body = new JsonObject { ["items"] = rows }.ToJsonString();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        var api = new AdminUsersApi(kit.Client);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PageAsync(lease, "/api/v2/admin/users/2/profiles", unpaged: true));
    }

    [Fact]
    public async Task MalformedOrRepeatedPageCannotBecomeWritableData()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminUsersApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"2"}],"page":{"has_more":true,"next_cursor":"same"}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PageAsync(lease, "/api/v2/admin/users", 25, "same"));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":true,"next_cursor":"next"}}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.PageAsync(lease, "/api/v2/admin/users", 25));
    }

    [Fact]
    public async Task GuardedAccountWriteSendsReviewedStrongTagOnce()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var writes = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get)
            {
                writes++;
                Assert.Equal("\"account-v1\"", request.Headers.IfMatch.Single().ToString());
                return Task.FromResult(AdminMonitorKit.Json("{}"));
            }
            return Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
        };
        var api = new AdminUsersApi(kit.Client);
        var review = await api.ReviewAsync(lease, "2");
        await api.WriteAsync(lease, review, new(AdminUsersOperation.UpdateAccount, new JsonObject { ["username"] = "Changed" }));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task MutationErrorsNeverReplayOrRefresh(HttpStatusCode status)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var writes = 0;
        var refreshes = 0;
        kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Reply(request.RequestUri!.AbsolutePath));
            writes++;
            return Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","title":"Synthetic refusal","status":412}""", status));
        };
        var api = new AdminUsersApi(kit.Client);
        var review = await api.ReviewAsync(lease, "2");
        await Assert.ThrowsAnyAsync<Exception>(() => api.WriteAsync(lease, review, new(AdminUsersOperation.UpdateAccount, new JsonObject { ["username"] = "Changed" })));
        Assert.Equal(1, writes);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task ChangedRenderedAccountRequiresReviewBeforeAnyWrite()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminUsersApi(kit.Client);
        var changed = false;
        kit.Http.Respond = (request, _) =>
        {
            var reply = Reply(request.RequestUri!.AbsolutePath);
            if (changed && request.RequestUri.AbsolutePath.EndsWith("/2")) reply.Headers.ETag = new EntityTagHeaderValue("\"account-v2\"");
            return Task.FromResult(reply);
        };
        var review = await api.ReviewAsync(lease, "2");
        changed = true;
        await Assert.ThrowsAsync<AdminUsersReviewRequiredException>(() => api.WriteAsync(lease, review, new(AdminUsersOperation.DeleteAccount)));
        Assert.All(kit.Http.Requests, request => Assert.StartsWith("GET ", request));
    }

    [Fact]
    public async Task RetiredAuthorityRejectsLateResponse()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var arrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = async (_, _) => { arrived.SetResult(true); await release.Task; return AdminMonitorKit.Json("{}"); };
        var read = new AdminUsersApi(kit.Client).ReadAsync(lease, "/api/v2/admin/users/2");
        await arrived.Task;
        kit.SetRole("user");
        release.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    private static HttpResponseMessage Reply(string path)
    {
        if (path.EndsWith("/capabilities")) return AdminMonitorKit.Json("""{"available":true,"allowed":true,"state":"available","guarded_configuration":true,"profile_management":true}""");
        var id = path.Split('/').Last();
        var reply = AdminMonitorKit.Json(new JsonObject { ["id"] = id, ["username"] = "Synthetic", ["email"] = "fixture@example.test", ["role"] = "admin", ["is_owner"] = id == "1", ["enabled"] = true }.ToJsonString());
        reply.Headers.ETag = new EntityTagHeaderValue("\"account-v1\"");
        return reply;
    }
}
