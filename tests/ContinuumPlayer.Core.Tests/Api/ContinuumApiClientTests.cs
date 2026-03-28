using System.Net;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Tests.Api;

public class ContinuumApiClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public async Task GetAsync_AddsAuthorizationHeader()
    {
        string? capturedAuthHeader = null;
        var handler = new MockHttpHandler(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });
        var client = CreateClient(handler);
        client.SetAccessToken("test-token-123");
        await client.GetAsync<List<object>>("/api/v1/user/libraries");
        Assert.Equal("Bearer test-token-123", capturedAuthHeader);
    }

    [Fact]
    public async Task GetAsync_AddsProfileHeaders()
    {
        string? capturedProfileId = null;
        string? capturedProfileToken = null;
        var handler = new MockHttpHandler(request =>
        {
            capturedProfileId = request.Headers.TryGetValues("X-Profile-Id", out var vals) ? vals.First() : null;
            capturedProfileToken = request.Headers.TryGetValues("X-Profile-Token", out var vals2) ? vals2.First() : null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });
        var client = CreateClient(handler);
        client.SetAccessToken("token");
        client.SetProfile("profile-uuid", "profile-jwt");
        await client.GetAsync<List<object>>("/api/v1/home/layout");
        Assert.Equal("profile-uuid", capturedProfileId);
        Assert.Equal("profile-jwt", capturedProfileToken);
    }

    [Fact]
    public async Task GetAsync_ThrowsApiException_On4xx()
    {
        var handler = new MockHttpHandler(_ =>
        {
            var errorJson = JsonSerializer.Serialize(new ApiError { Error = "unauthorized", Message = "Bad token" }, JsonOptions);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = CreateClient(handler);
        client.SetAccessToken("bad-token");
        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<object>("/api/v1/test"));
        Assert.Equal("unauthorized", ex.ErrorCode);
    }

    private static ContinuumApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var client = new ContinuumApiClient(httpClient);
        client.SetBaseUrl("https://test.example.com");
        return client;
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
