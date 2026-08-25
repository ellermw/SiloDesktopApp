using System.Net;
using System.Text;
using System.Collections.Concurrent;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task TokenLifetime_SchedulesProactiveRefreshAtEightyPercentWithout401()
    {
        var scheduledDelay = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCall = 0;
        Task ControlledDelay(TimeSpan dueTime, CancellationToken ct)
        {
            scheduledDelay.TrySetResult(dueTime);
            if (Interlocked.Increment(ref delayCall) == 1)
                return releaseFirstDelay.Task.WaitAsync(ct);
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }

        var handler = new DelegateHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/refresh")
            {
                refreshObserved.TrySetResult();
                return Task.FromResult(JsonResponse(
                    """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":86400}"""));
            }

            return Task.FromResult(JsonResponse(
                """{"id":1,"username":"tester","role":"user","permissions":[]}"""));
        });
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(
            apiClient,
            new AuthApi(apiClient),
            credentialStore: null,
            ControlledDelay);

        authService.SetTokens("old-access", "old-refresh", expiresIn: 100);

        Assert.Equal(TimeSpan.FromSeconds(80), await scheduledDelay.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(refreshObserved.Task.IsCompleted);
        releaseFirstDelay.TrySetResult();
        await refreshObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConcurrentTryRefreshAsync_ReusesSingleRotatingRefreshToken()
    {
        var refreshCalls = 0;
        var handler = new DelegateHandler(async (request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/me")
                return JsonResponse("""{"id":1,"username":"tester","role":"user","permissions":[],"download_allowed":true}""");

            Interlocked.Increment(ref refreshCalls);
            await Task.Delay(25);
            return JsonResponse("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":86400}""");
        });

        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "old-refresh", 86400);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => authService.TryRefreshAsync()));

        Assert.All(results, Assert.True);
        Assert.Equal(1, refreshCalls);
        Assert.Equal("new-access", apiClient.AccessToken);
        Assert.Equal("new-refresh", authService.RefreshToken);
        Assert.Equal("tester", authService.CurrentUser?.Username);
        Assert.True(authService.CurrentUser?.DownloadAllowed);
    }

    [Fact]
    public async Task InvalidRefreshDuringProtectedRequest_CompletesWithoutRecursiveRefresh()
    {
        var refreshCalls = 0;
        var handler = new DelegateHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/refresh")
            {
                Interlocked.Increment(ref refreshCalls);
                return Task.FromResult(JsonResponse(
                    """{"error":"invalid_token","message":"Refresh token is invalid"}""",
                    HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(JsonResponse(
                """{"error":"unauthorized","message":"Expired"}""",
                HttpStatusCode.Unauthorized));
        });

        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "invalid-refresh", 86400);
        apiClient.SetTokenRefresher(ct => authService.TryRefreshAsync(ct));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            apiClient.GetAsync<Dictionary<string, object?>>("/api/v1/protected")
                .WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal(1, refreshCalls);
        Assert.Null(apiClient.AccessToken);
        Assert.Null(authService.RefreshToken);
    }

    [Fact]
    public async Task LogoutDuringDelayedRefresh_PreventsTokenResurrection()
    {
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, _) =>
        {
            refreshStarted.TrySetResult(true);
            await releaseRefresh.Task;
            return JsonResponse("""{"access_token":"late-access","refresh_token":"late-refresh","expires_in":86400}""");
        });

        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "old-refresh", 86400);

        var refreshTask = authService.TryRefreshAsync();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        authService.Logout();
        releaseRefresh.TrySetResult(true);

        Assert.False(await refreshTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Null(apiClient.AccessToken);
        Assert.Null(authService.RefreshToken);
        Assert.Null(authService.CurrentUser);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotLogOutValidSession()
    {
        var handler = new DelegateHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse("{}");
        });

        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "old-refresh", 86400);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            authService.TryRefreshAsync(cancellation.Token));

        Assert.Equal("old-access", apiClient.AccessToken);
        Assert.Equal("old-refresh", authService.RefreshToken);
    }

    [Fact]
    public async Task CancellationDuringAuthoritativeUserLoad_KeepsRotatedTokens()
    {
        var userLoadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (request, ct) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/refresh")
                return JsonResponse("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":86400}""");

            userLoadStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse("{}");
        });
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("old-access", "old-refresh", 86400);
        using var cancellation = new CancellationTokenSource();

        var refreshTask = authService.TryRefreshAsync(cancellation.Token);
        await userLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refreshTask);

        Assert.Equal("new-access", apiClient.AccessToken);
        Assert.Equal("new-refresh", authService.RefreshToken);
        Assert.Equal("new-refresh", store.LoadCredential("https://example.test", "refresh_token"));
    }

    [Fact]
    public async Task TransientRefreshFailure_RetainsSessionForRetry()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(JsonResponse(
            """{"error":"internal_error","message":"Temporary outage"}""",
            HttpStatusCode.InternalServerError)));

        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient));
        authService.SetTokens("old-access", "old-refresh", 86400);

        Assert.False(await authService.TryRefreshAsync());
        Assert.Equal("old-access", apiClient.AccessToken);
        Assert.Equal("old-refresh", authService.RefreshToken);
    }

    [Fact]
    public async Task RotatedRefreshToken_IsPersistedForIssuingServer()
    {
        var store = new MemoryCredentialStore();
        var handler = new DelegateHandler((_, _) => Task.FromResult(JsonResponse(
            """{"access_token":"new-access","refresh_token":"new-refresh","expires_in":86400}""")));
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://server-a.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("old-access", "old-refresh", 86400);

        Assert.True(await authService.TryRefreshAsync());

        Assert.Equal("new-refresh", store.LoadCredential("https://server-a.test", "refresh_token"));
        Assert.Null(store.LoadCredential("https://server-b.test", "refresh_token"));
    }

    [Fact]
    public async Task LateRemoteLogout_DoesNotClearNewSession()
    {
        var logoutStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLogout = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/logout")
            {
                logoutStarted.TrySetResult(true);
                await releaseLogout.Task;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return JsonResponse("{}");
        });
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("old-access", "old-refresh", 86400);

        var logoutTask = authService.LogoutAsync();
        await logoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        authService.SetTokens("new-access", "new-refresh", 86400);
        releaseLogout.TrySetResult(true);
        await logoutTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("new-access", apiClient.AccessToken);
        Assert.Equal("new-refresh", authService.RefreshToken);
        Assert.Equal("new-refresh", store.LoadCredential("https://example.test", "refresh_token"));
    }

    [Fact]
    public void AuthenticationResponseFromPreviousServer_CannotStartSession()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://server-a.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.ConfigureServer("https://server-b.test");

        Assert.Throws<InvalidOperationException>(() => authService.SetTokens(
            "server-a-access",
            "server-a-refresh",
            86400,
            expectedServerUrl: "https://server-a.test"));

        Assert.Null(apiClient.AccessToken);
        Assert.Null(store.LoadCredential("https://server-b.test", "refresh_token"));
    }

    [Fact]
    public void ProtectedProfileSession_IsPersistedAndInvalidatedWithoutAccountLogout()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("access", "refresh", 86400);
        var verificationEvents = 0;
        authService.ProfileVerificationRequired += () => verificationEvents++;

        authService.SelectProfile("profile-1", "profile-token");
        var persisted = authService.LoadPersistedProfileSession("https://example.test");
        authService.ClearSelectedProfile();

        Assert.NotNull(persisted);
        Assert.Equal("profile-1", persisted.Value.ProfileId);
        Assert.Equal("profile-token", persisted.Value.ProfileToken);
        Assert.Equal(1, verificationEvents);
        Assert.Equal("access", apiClient.AccessToken);
        Assert.Equal("refresh", authService.RefreshToken);
        Assert.Null(apiClient.ProfileId);
        Assert.Null(store.LoadCredential("https://example.test", "profile_token"));
    }

    [Fact]
    public async Task StaleProfileUnverifiedResponse_DoesNotClearNewProfile()
    {
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, _) =>
        {
            requestStarted.TrySetResult(true);
            await releaseResponse.Task;
            return JsonResponse(
                """{"error":"profile_unverified","message":"Old profile token expired"}""",
                HttpStatusCode.Forbidden);
        });
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("access", "refresh", 86400);
        authService.SelectProfile("profile-1", "old-profile-token");
        apiClient.SetProfileVerificationRequiredHandler(authService.HandleProfileVerificationRequired);

        var oldRequest = apiClient.GetAsync<Dictionary<string, object?>>("/api/v1/user/libraries");
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        authService.SelectProfile("profile-2", "new-profile-token");
        releaseResponse.TrySetResult(true);

        await Assert.ThrowsAsync<ApiException>(() => oldRequest);
        Assert.Equal("profile-2", authService.SelectedProfileId);
        Assert.Equal("profile-2", apiClient.ProfileId);
        Assert.Equal("new-profile-token", apiClient.ProfileToken);
        Assert.Equal("new-profile-token", store.LoadCredential("https://example.test", "profile_token"));
    }

    [Fact]
    public async Task Impersonation_EndRestoresPreservedAdministratorSession()
    {
        string? endBearer = null;
        var handler = new DelegateHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/impersonation/end")
            {
                endBearer = request.Headers.Authorization?.Parameter;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (request.RequestUri?.AbsolutePath == "/api/v1/auth/me")
                return Task.FromResult(JsonResponse("""{"id":1,"username":"admin","role":"admin"}"""));

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });
        var store = new MemoryCredentialStore();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("admin-access", "admin-refresh", 86400);
        authService.SetCurrentUser(new UserInfo { Id = 1, Username = "admin", Role = "admin" });

        authService.BeginImpersonation(new ImpersonationResponse
        {
            AccessToken = "user-access",
            RefreshToken = "user-refresh",
            ExpiresIn = 86400,
            User = new UserInfo
            {
                Id = 2,
                Username = "viewer",
                Role = "user",
                Impersonation = new ImpersonationInfo
                {
                    Active = true,
                    ImpersonatorUserId = 1,
                    ImpersonatorUsername = "admin",
                },
            },
        }, "/admin/users/2");

        Assert.Equal("user-access", apiClient.AccessToken);
        Assert.Equal("user-refresh", authService.RefreshToken);
        Assert.Equal("admin-refresh", store.LoadCredential(
            "https://example.test",
            "impersonation_admin_refresh_token"));

        var returnPath = await authService.EndImpersonationAsync();

        Assert.Equal("user-access", endBearer);
        Assert.Equal("/admin/users/2", returnPath);
        Assert.Equal("admin-access", apiClient.AccessToken);
        Assert.Equal("admin-refresh", authService.RefreshToken);
        Assert.Equal("admin", authService.CurrentUser?.Username);
        Assert.False(authService.IsImpersonating);
        Assert.Null(store.LoadCredential(
            "https://example.test",
            "impersonation_admin_refresh_token"));
    }

    [Fact]
    public async Task Impersonation_EndRecoversAdminAfterRestartAndNotImpersonatingResponse()
    {
        var store = new MemoryCredentialStore();
        var bootstrapClient = new SiloApiClient(new HttpClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)))));
        bootstrapClient.SetBaseUrl("https://example.test");
        using (var bootstrapAuth = new AuthService(
            bootstrapClient,
            new AuthApi(bootstrapClient),
            store))
        {
            bootstrapAuth.SetTokens("admin-access", "admin-refresh", 86400);
            bootstrapAuth.SetCurrentUser(new UserInfo { Id = 1, Username = "admin", Role = "admin" });
            bootstrapAuth.BeginImpersonation(new ImpersonationResponse
            {
                AccessToken = "user-access",
                RefreshToken = "user-refresh",
                ExpiresIn = 86400,
                User = new UserInfo
                {
                    Id = 2,
                    Username = "viewer",
                    Role = "user",
                    Impersonation = new ImpersonationInfo { Active = true },
                },
            }, "/admin/users/2");
        }

        var refreshCalls = 0;
        var handler = new DelegateHandler((request, _) =>
        {
            return request.RequestUri?.AbsolutePath switch
            {
                "/api/v1/auth/impersonation/end" => Task.FromResult(JsonResponse(
                    """{"error":"not_impersonating","message":"No active impersonation session"}""",
                    HttpStatusCode.BadRequest)),
                "/api/v1/auth/refresh" => Task.FromResult(RefreshResponse()),
                "/api/v1/auth/me" => Task.FromResult(JsonResponse(
                    """{"id":1,"username":"admin","role":"admin"}""")),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
            };

            HttpResponseMessage RefreshResponse()
            {
                Interlocked.Increment(ref refreshCalls);
                return JsonResponse(
                    """{"access_token":"restored-access","refresh_token":"restored-refresh","expires_in":86400}""");
            }
        });
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var authService = new AuthService(apiClient, new AuthApi(apiClient), store);
        authService.SetTokens("user-access", "user-refresh", 86400);
        authService.SetCurrentUser(new UserInfo
        {
            Id = 2,
            Username = "viewer",
            Role = "user",
            Impersonation = new ImpersonationInfo { Active = true },
        });

        var returnPath = await authService.EndImpersonationAsync();

        Assert.Equal(1, refreshCalls);
        Assert.Equal("/admin/users/2", returnPath);
        Assert.Equal("restored-access", apiClient.AccessToken);
        Assert.Equal("restored-refresh", authService.RefreshToken);
        Assert.Equal("admin", authService.CurrentUser?.Username);
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
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

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new();

        public void SaveCredential(string serverUrl, string key, string value) =>
            _values[StoreKey(serverUrl, key)] = value;

        public string? LoadCredential(string serverUrl, string key) =>
            _values.TryGetValue(StoreKey(serverUrl, key), out var value) ? value : null;

        public void DeleteCredential(string serverUrl, string key) =>
            _values.TryRemove(StoreKey(serverUrl, key), out _);

        public void DeleteAllForServer(string serverUrl)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith(serverUrl + "\n", StringComparison.OrdinalIgnoreCase)))
                _values.TryRemove(key, out _);
        }

        private static string StoreKey(string serverUrl, string key) => serverUrl + "\n" + key;
    }
}
