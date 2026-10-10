using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class RequiredPasswordTransitionTests
{
    [Fact]
    public async Task RetrySettlementDoesNotRepeatPasswordWriteAndWaitsForAuthoritativeAccount()
    {
        var writes = 0;
        var reads = 0;
        var client = new SiloApiClient(new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/account/password")
            { writes++; return new(HttpStatusCode.NoContent); }
            if (request.RequestUri.AbsolutePath == "/api/v2/auth/refresh")
                return Json("""{"access_token":"rotated","refresh_token":"renewed","expires_in":86400}""");
            reads++;
            return reads <= 2 ? new(HttpStatusCode.ServiceUnavailable) :
                Json("""{"id":"1","username":"viewer","password_change_required":false}""");
        })));
        client.SetBaseUrl("https://example.test");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("temporary", "refresh", 86400);
        auth.SetCurrentUser(new() { Id = "1", Username = "viewer", PasswordChangeRequired = true });
        var flow = new RequiredPasswordTransition(auth, client);
        await Assert.ThrowsAnyAsync<Exception>(() => flow.SaveAsync("old", "new-password", "new-password"));
        Assert.True(flow.PasswordSaved);
        Assert.True(auth.PasswordChangeRequired);
        Assert.True(await flow.SaveAsync("", "", ""));
        Assert.Equal(1, writes);
        Assert.False(auth.PasswordChangeRequired);
    }

    [Fact]
    public async Task ReplacementSessionCannotBeCompletedByOldPasswordFlow()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new SiloApiClient(new HttpClient(new AsyncHandler(async request =>
        { ready.SetResult(); await release.Task; return new(HttpStatusCode.NoContent); })));
        client.SetBaseUrl("https://example.test");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("old", "old-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "1", PasswordChangeRequired = true });
        var flow = new RequiredPasswordTransition(auth, client);
        var saving = flow.SaveAsync("old", "new-password", "new-password");
        await ready.Task;
        auth.SetTokens("replacement", "replacement-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "2" });
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
        Assert.Equal("2", auth.CurrentUser!.Id);
        Assert.Equal("replacement", client.AccessToken);
    }

    [Theory]
    [InlineData(false, false, "child")]
    [InlineData(true, false, "child")]
    [InlineData(false, true, "other")]
    public void ImportTargetUsesActingProfileUnlessActingAdminOrPrimary(bool admin, bool primary, string expected)
        => Assert.Equal(expected, HistoryImportScope.Target(new() { Role = admin ? "admin" : "user" },
            new() { Id = "child", IsPrimary = primary }, "other"));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(send(r)); }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => send(r); }
}
