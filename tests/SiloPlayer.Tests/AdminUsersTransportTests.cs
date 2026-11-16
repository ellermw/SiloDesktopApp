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
