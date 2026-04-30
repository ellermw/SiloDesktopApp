using System.Net;
using System.Text;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task ConcurrentTryRefreshAsync_ReusesSingleRotatingRefreshToken()
    {
        var refreshCalls = 0;
        var handler = new DelegateHandler(async (_, _) =>
        {
            Interlocked.Increment(ref refreshCalls);
            await Task.Delay(25);
            return JsonResponse("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":86400}""");
        });

        var apiClient = new ContinuumApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "old-refresh", 86400);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => authService.TryRefreshAsync()));

        Assert.All(results, Assert.True);
        Assert.Equal(1, refreshCalls);
        Assert.Equal("new-access", apiClient.AccessToken);
        Assert.Equal("new-refresh", authService.RefreshToken);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
