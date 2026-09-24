using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public class ApiTransportVersionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitApiRequestsAndAuthRetriesUseConfiguredHttpVersion(bool retry)
    {
        using var handler = new VersionHandler(retry);
        using var http = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        var api = new SiloApiClient(http);
        api.SetBaseUrl("https://fixture.invalid");
        api.BeginAuthenticationSession("fixture-token");
        api.SetTokenRefresher(_ =>
        {
            api.SetAccessToken("fixture-refreshed-token");
            return Task.FromResult(true);
        });
        await api.GetAsync<Dictionary<string, object>>("/api/v1/catalog");

        Assert.Equal(retry ? 2 : 1, handler.Versions.Count);
        Assert.All(handler.Versions, version => Assert.Equal(HttpVersion.Version20, version));
        Assert.All(handler.Policies, policy => Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, policy));
    }

    private sealed class VersionHandler(bool retry) : HttpMessageHandler
    {
        public List<Version> Versions { get; } = [];
        public List<HttpVersionPolicy> Policies { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Versions.Add(request.Version);
            Policies.Add(request.VersionPolicy);
            return Task.FromResult(new HttpResponseMessage(retry && Versions.Count == 1
                ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }
}
