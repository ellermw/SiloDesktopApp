using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ShellRecoveryActivationTests
{
    [Theory]
    [InlineData("https://fixture.test/reset-password/abc_123", "https://fixture.test")]
    [InlineData("\"https://fixture.test/silo/reset-password/abc-123\"", "https://fixture.test/silo")]
    public async Task ActivationCarriesSelectedServerUrlIntoExistingRecoveryLookup(string argument, string server)
    {
        Assert.True(PasswordRecovery.TryParseActivation(argument, server, out var link));
        var token = PasswordRecovery.ParseToken(link, server);
        string? requested = null;
        var client = new SiloPlayer.Core.Api.SiloApiClient(new HttpClient(new Wire(request =>
        {
            requested = request.RequestUri!.AbsolutePath;
            Assert.Null(request.Headers.Authorization);
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"username":"fixture","server_name":"Fixture","expires_at":"2030-01-01T00:00:00Z"}""") };
        })));
        client.SetBaseUrl(server);
        using var auth = new AuthService(client, new SiloPlayer.Core.Api.AuthApi(client));
        await new PasswordRecovery(auth, client).LookupAsync(link);
        Assert.Equal(new Uri(server.TrimEnd('/') + $"/api/v2/password-resets/{token}").AbsolutePath, requested);
    }

    [Theory]
    [InlineData("abc123", "https://fixture.test")]
    [InlineData("silo://reset-password?token=abc123", "https://fixture.test")]
    [InlineData("https://other.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("http://fixture.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("https://user:password@fixture.test/reset-password/abc123", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password/abc/extra", "https://fixture.test")]
    [InlineData("https://fixture.test/reset-password/abc123", "")]
    [InlineData("https://fixture.test/other/reset-password/abc123", "https://fixture.test/silo")]
    public void RefusesUnselectedOriginsAndInventedProtocols(string argument, string server)
        => Assert.False(PasswordRecovery.TryParseActivation(argument, server, out _));

    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(send(request));
    }
}
