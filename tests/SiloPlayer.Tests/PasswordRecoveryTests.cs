using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class PasswordRecoveryTests
{
    [Fact]
    public async Task UnconfirmedCompletionRequiresFreshLookupAndNeverReplays()
    {
        var writes = 0;
        var client = new SiloApiClient(new HttpClient(new Wire(request =>
        {
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Post) { writes++; throw new HttpRequestException("Disconnected after saving"); }
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"username":"viewer","server_name":"Fixture","expires_at":"2030-01-01T00:00:00Z"}""", Encoding.UTF8, "application/json") };
        })));
        client.SetBaseUrl("https://example.test");
        using var auth = new AuthService(client, new AuthApi(client));
        var flow = new PasswordRecovery(auth, client);
        await flow.LookupAsync("abc123");
        await Assert.ThrowsAsync<HttpRequestException>(() => flow.CompleteAsync("abc123", "new-password", "new-password"));
        Assert.True(flow.NeedsReload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.CompleteAsync("abc123", "new-password", "new-password"));
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData("https://example.test/reset-password/abc_123", "https://example.test", "abc_123")]
    [InlineData("https://example.test/silo/reset-password/abc-123", "https://example.test/silo", "abc-123")]
    public void LinkMustBelongToSelectedServer(string link, string server, string token)
        => Assert.Equal(token, PasswordRecovery.ParseToken(link, server));

    [Theory]
    [InlineData("https://another.test/reset-password/abc")]
    [InlineData("https://example.test/reset-password/a/extra")]
    [InlineData("https://user:password@example.test/reset-password/abc")]
    public void RefusesOtherOriginsAndInvalidLinks(string link)
        => Assert.Throws<InvalidOperationException>(() => PasswordRecovery.ParseToken(link, "https://example.test"));

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(send(r)); }
}
