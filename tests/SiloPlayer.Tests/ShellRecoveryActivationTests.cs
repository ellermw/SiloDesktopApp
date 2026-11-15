using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ShellRecoveryActivationTests
{
    [Theory]
    [InlineData("https://fixture.test/reset-password/abc_123", "https://fixture.test", "abc_123")]
    [InlineData("\"https://fixture.test/silo/reset-password/abc-123\"", "https://fixture.test/silo", "abc-123")]
    [InlineData("https://fixture.test/reset-password#abc_123", "https://fixture.test", "abc_123")]
    [InlineData("\"https://fixture.test/silo/reset-password#abc-123\"", "https://fixture.test/silo", "abc-123")]
    public async Task ActivationCarriesSelectedServerUrlIntoExistingRecoveryLookup(string argument, string server, string token)
    {
        Assert.True(PasswordRecovery.TryParseActivation(argument, server, out var link));
        Assert.Equal(token, PasswordRecovery.ParseToken(link, server));
        string? requested = null, body = null;
        var client = new SiloPlayer.Core.Api.SiloApiClient(new HttpClient(new Wire(request =>
        {
            requested = request.RequestUri!.AbsolutePath;
            body = request.Content?.ReadAsStringAsync().Result;
            Assert.Null(request.Headers.Authorization);
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"username":"fixture","server_name":"Fixture","expires_at":"2030-01-01T00:00:00Z"}""") };
        })));
        client.SetBaseUrl(server);
        using var auth = new AuthService(client, new SiloPlayer.Core.Api.AuthApi(client));
        await new PasswordRecovery(auth, client).LookupAsync(link);
        // The token is redeemed in a body; no path or query ever names it.
        Assert.Equal(new Uri(server.TrimEnd('/') + "/api/v2/password-resets/lookup").AbsolutePath, requested);
        Assert.Equal(token, JsonDocument.Parse(body!).RootElement.GetProperty("token").GetString());
    }

    [Theory]
    [InlineData("abc123", "https://fixture.test")]
    [InlineData("silo://reset-password?token=abc123", "https://fixture.test")]
    [InlineData("https://other.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("https://other.test/reset-password#abc123", "https://fixture.test")]
    [InlineData("http://fixture.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("http://fixture.test/reset-password#abc123", "https://fixture.test")]
    [InlineData("https://user:password@fixture.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("https://user:password@fixture.test/reset-password#abc123", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password/abc/extra", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password#", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password/abc123", "")]
    [InlineData("https://fixture.test/reset-password#abc123", "")]
    [InlineData("https://fixture.test/other/reset-password/abc123", "https://fixture.test/silo")]
    [InlineData("https://fixture.test/other/reset-password#abc123", "https://fixture.test/silo")]
    public void RefusesUnselectedOriginsAndInventedProtocols(string argument, string server)
        => Assert.False(PasswordRecovery.TryParseActivation(argument, server, out _));

    [Theory]
    [InlineData("https://fixture.test/reset-password#abc123", true)]
    [InlineData("https://fixture.test/reset-password/abc123", true)]
    [InlineData("https://fixture.test/silo/reset-password#abc123", true)]
    [InlineData("https://fixture.test/reset-passwords#abc123", false)]
    [InlineData("https://fixture.test/invite#abc123", false)]
    [InlineData("silo://invite?server=https%3A%2F%2Ffixture.test&token=abc123", false)]
    [InlineData("abc123", false)]
    public void ResetRouteIsHeldUntilAServerIsSelected(string argument, bool route)
        => Assert.Equal(route, PasswordRecovery.IsResetLinkRoute(argument));

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(send(request));
    }
}
