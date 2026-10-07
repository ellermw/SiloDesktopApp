using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

// Public Silo main74158b4a8a799192312c13253552b8030af8575c.
public sealed class NetworkSignInContractTests
{
    [Theory]
    [InlineData("Owner", "login", "Owner")]
    [InlineData("", "login", "login")]
    [InlineData("", "", "")]
    public void NetworkOwnerUsesTheCurrentDisplayNameFallback(string name, string username, string expected)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var provider = JsonSerializer.Deserialize<AuthProvider>(JsonSerializer.Serialize(new { mode = "network", installation_id = "5", network_identity = new { display_name = name, username } }), options)!;
        Assert.Equal(5, provider.InstallationId);
        Assert.Equal(expected, provider.NetworkIdentity!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NetworkMutationsDoNotRefreshOrReplayAfter401(bool linking)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid"); client.SetAccessToken("fixture-access");
        var refreshes = 0; client.SetTokenRefresher(_ => { ++refreshes; return Task.FromResult(true); });
        var api = new AuthApi(client);
        await Assert.ThrowsAsync<ApiException>(() => linking
            ? (Task)api.LinkAccountIdentityWithNetworkAsync(client.CaptureContext(), 5, "fixture-password")
            : api.SignInWithNetworkAsync("https://saved-fixture.invalid", 5));
        Assert.Equal(0, refreshes); Assert.Equal(1, wire.Writes);
        Assert.Equal(linking ? "/api/v2/account/identities/link-network" : "/api/v2/auth/network/5/sign-in", wire.Path);
        Assert.Equal(linking ? "fixture.invalid" : "saved-fixture.invalid", wire.Host);
        Assert.Equal(linking, wire.Authenticated);
        using var body = JsonDocument.Parse(wire.Body);
        if (linking)
        {
            Assert.Equal("5", body.RootElement.GetProperty("installation_id").GetString());
            Assert.Equal("fixture-password", body.RootElement.GetProperty("password").GetString());
            Assert.Equal(2, body.RootElement.EnumerateObject().Count());
        }
        else Assert.Empty(body.RootElement.EnumerateObject());
    }

    [Fact]
    public void NetworkRefusalsExplainTheProviderContext()
    {
        Assert.Equal("This account is disabled.", ExternalSignInErrors.DescribeNetwork("permission_denied", "Network"));
        Assert.Contains("Settings → Sign-in", ExternalSignInErrors.DescribeNetwork("email_in_use", "Network"));
        Assert.Contains("this device", ExternalSignInErrors.DescribeNetwork("not_permitted", "Network"));
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Writes; internal bool Authenticated; internal string Path = "", Host = "", Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ++Writes; Path = request.RequestUri!.AbsolutePath; Host = request.RequestUri.Host;
            Authenticated = request.Headers.Authorization is not null; Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
        }
    }
}
