using System.Net;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class AuthServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [Fact]
    public async Task LoginAsync_SetsTokenOnApiClient()
    {
        var loginResponse = new LoginResponse
        {
            AccessToken = "access-123", RefreshToken = "refresh-456", ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        };
        var handler = new MockHttpHandler(_ => JsonResponse(loginResponse));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        await authService.LoginAsync("mike", "password");

        Assert.Equal("access-123", apiClient.AccessToken);
        Assert.True(authService.IsLoggedIn);
        Assert.Equal("mike", authService.CurrentUser?.Username);
    }

    [Fact]
    public async Task SelectProfile_SetsProfileOnApiClient()
    {
        var loginResponse = new LoginResponse
        {
            AccessToken = "access-123", RefreshToken = "refresh-456", ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        };
        var handler = new MockHttpHandler(_ => JsonResponse(loginResponse));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        await authService.LoginAsync("mike", "password");
        authService.SelectProfile("profile-uuid-123");

        Assert.Equal("profile-uuid-123", apiClient.ProfileId);
    }

    [Fact]
    public void Logout_ClearsState()
    {
        var handler = new MockHttpHandler(_ => JsonResponse(new LoginResponse
        {
            AccessToken = "token", RefreshToken = "refresh", ExpiresIn = 86400,
            User = new UserInfo { Id = 1, Username = "mike", Role = "admin" }
        }));
        var apiClient = CreateApiClient(handler);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));

        authService.Logout();

        Assert.False(authService.IsLoggedIn);
        Assert.Null(apiClient.AccessToken);
    }

    private static ContinuumApiClient CreateApiClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return new ContinuumApiClient(http);
    }

    private static HttpResponseMessage JsonResponse<T>(T body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
