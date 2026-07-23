using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class SiloApiClientTests
{
    [Fact]
    public async Task GetAsync_DisposesSuccessfulResponse()
    {
        TrackingResponse? response = null;
        var handler = new DelegateHandler((_, _) =>
        {
            response = JsonResponse(HttpStatusCode.OK, """{"name":"ok"}""");
            return Task.FromResult<HttpResponseMessage>(response);
        });

        var client = CreateClient(handler);

        var result = await client.GetAsync<TestDto>("/api/v1/test");

        Assert.Equal("ok", result.Name);
        Assert.NotNull(response);
        Assert.True(response.IsDisposed);
    }

    [Fact]
    public async Task ConcurrentUnauthorizedResponses_ShareOneRefresh()
    {
        var refreshCount = 0;
        var handler = new DelegateHandler((request, _) =>
        {
            var token = request.Headers.Authorization?.Parameter;
            return Task.FromResult<HttpResponseMessage>(
                token == "new-token"
                    ? JsonResponse(HttpStatusCode.OK, """{"name":"ok"}""")
                    : JsonResponse(HttpStatusCode.Unauthorized, """{"error":"unauthorized","message":"expired"}"""));
        });

        var client = CreateClient(handler);
        client.SetAccessToken("old-token");
        client.SetTokenRefresher(async _ =>
        {
            Interlocked.Increment(ref refreshCount);
            await Task.Delay(25);
            client.SetAccessToken("new-token");
            return true;
        });

        var calls = Enumerable.Range(0, 8)
            .Select(_ => client.GetAsync<TestDto>("/api/v1/test"))
            .ToArray();

        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.Equal("ok", result.Name));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task DictionaryBodyPreservesExplicitSnakeCaseKeys()
    {
        string? sentJson = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            sentJson = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = CreateClient(handler);

        await client.PostNoContentAsync("/api/v1/test", new Dictionary<string, object?>
        {
            ["audio_track_index"] = 2,
            ["is_paused"] = false,
        });

        Assert.Equal("""{"audio_track_index":2,"is_paused":false}""", sentJson);
    }

    [Fact]
    public async Task JsonWithFile_UsesWebCompatibleDataAndPosterParts()
    {
        string? method = null;
        string? mediaType = null;
        string? multipart = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            method = request.Method.Method;
            mediaType = request.Content?.Headers.ContentType?.MediaType;
            multipart = await request.Content!.ReadAsStringAsync(ct);
            return JsonResponse(HttpStatusCode.OK, """{"name":"updated"}""");
        });
        var client = CreateClient(handler);

        var result = await client.PutJsonWithFileAsync<TestDto>(
            "/api/v1/collections/one",
            new Dictionary<string, object?> { ["allowed_profile_ids"] = new[] { "profile-1" } },
            "poster",
            "poster.png",
            [1, 2, 3, 4],
            "image/png");

        Assert.Equal("PUT", method);
        Assert.Equal("multipart/form-data", mediaType);
        Assert.Contains("name=data", multipart!);
        Assert.Contains("{\"allowed_profile_ids\":[\"profile-1\"]}", multipart!);
        Assert.Contains("name=poster", multipart!);
        Assert.Contains("filename=poster.png", multipart!);
        Assert.Equal("updated", result.Name);
    }

    [Fact]
    public async Task PlaybackStart_SendsFriendlyWindowsClientIdentityHeaders()
    {
        string? clientName = null;
        string? clientVersion = null;
        string? path = null;
        var handler = new DelegateHandler((request, _) =>
        {
            path = request.RequestUri!.AbsolutePath;
            clientName = request.Headers.TryGetValues("X-Silo-Client", out var names)
                ? names.SingleOrDefault()
                : null;
            clientVersion = request.Headers.TryGetValues("X-Silo-Client-Version", out var versions)
                ? versions.SingleOrDefault()
                : null;
            return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.Created,
                """{"session_id":"session-1","media_file_id":7,"play_method":"direct","position":0,"is_paused":false,"stream_url":"/stream/session-1","audio_track_index":0,"duration_seconds":120,"subtitle_urls":[]}"""));
        });

        var client = CreateClient(handler);
        client.SetClientMetadata(SiloApiClient.DefaultClientName);
        var api = new PlaybackApi(client);

        await api.StartPlaybackAsync(new PlaybackStartRequest
        {
            FileId = 7,
            ProfileId = "profile-1",
        });

        Assert.Equal("/api/v1/playback/start", path);
        Assert.Equal("Silo for Windows", clientName);
        Assert.Null(clientVersion);
    }

    [Fact]
    public async Task AccessGroupUpdatePreservesExplicitNullMasks()
    {
        string? sentJson = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            sentJson = await request.Content!.ReadAsStringAsync(ct);
            return JsonResponse(HttpStatusCode.OK,
                """{"id":7,"name":"Guests","description":"","library_ids":null,"max_playback_quality":"","download_allowed":false,"download_transcode_allowed":false,"max_streams":0,"max_transcodes":0,"allowed_permissions":null,"requests_allowed":false,"is_default":false,"member_count":0,"created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z"}""");
        });
        var client = CreateClient(handler);
        var api = new AdminApi(client);

        await api.UpdateAccessGroupAsync(7, new UpdateAccessGroupRequest
        {
            Name = "Guests",
            LibraryIds = null,
            AllowedPermissions = null,
        });
        using var document = JsonDocument.Parse(sentJson!);

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("library_ids").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("allowed_permissions").ValueKind);
    }

    [Fact]
    public async Task UserUpdateDistinguishesExplicitNullFromOmittedAccessMasks()
    {
        var bodies = new List<string>();
        var handler = new DelegateHandler(async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return JsonResponse(HttpStatusCode.OK, """{"id":9,"username":"sam"}""");
        });
        var api = new AdminApi(CreateClient(handler));

        await api.UpdateUserAsync(9, new UpdateUserRequest { Enabled = true });
        await api.UpdateUserAsync(9, new UpdateUserRequest
        {
            LibraryIds = null,
            LibraryIdsSpecified = true,
            AccessGroupId = null,
            AccessGroupIdSpecified = true,
        });

        using var omitted = JsonDocument.Parse(bodies[0]);
        Assert.False(omitted.RootElement.TryGetProperty("library_ids", out _));
        Assert.False(omitted.RootElement.TryGetProperty("access_group_id", out _));
        using var explicitNull = JsonDocument.Parse(bodies[1]);
        Assert.Equal(JsonValueKind.Null, explicitNull.RootElement.GetProperty("library_ids").ValueKind);
        Assert.Equal(JsonValueKind.Null, explicitNull.RootElement.GetProperty("access_group_id").ValueKind);
    }

    [Fact]
    public async Task TasteSeedUsesPaginatedCurrentEndpointsAndSnakeCaseBatchBody()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        var handler = new DelegateHandler(async (request, ct) =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return request.Method == HttpMethod.Get
                ? JsonResponse(HttpStatusCode.OK, """{"items":[],"next_offset":30}""")
                : JsonResponse(HttpStatusCode.OK, """{"added":2}""");
        });
        var api = new RecommendationsApi(CreateClient(handler));

        var page = await api.GetTasteSeedItemsAsync(30, 0);
        var result = await api.SubmitTasteSeedAsync(["movie-1", "movie-2"]);

        Assert.Equal(30, page.NextOffset);
        Assert.Equal(2, result.Added);
        Assert.Equal("/api/v1/recommendations/taste-seed/items?limit=30&offset=0", requests[0].Path);
        Assert.Equal("/api/v1/recommendations/taste-seed", requests[1].Path);
        using var body = JsonDocument.Parse(requests[1].Body!);
        Assert.Equal(new[] { "movie-1", "movie-2" },
            body.RootElement.GetProperty("item_ids").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task UnauthenticatedRequest_OmitsAuthHeadersAndNeverInvokesRefresher()
    {
        var refreshCount = 0;
        string? authorization = "not-observed";
        bool profileHeaderPresent = true;
        var handler = new DelegateHandler((request, _) =>
        {
            authorization = request.Headers.Authorization?.ToString();
            profileHeaderPresent = request.Headers.Contains("X-Profile-Id") ||
                request.Headers.Contains("X-Profile-Token");
            return Task.FromResult<HttpResponseMessage>(JsonResponse(
                HttpStatusCode.Unauthorized,
                """{"error":"invalid_token","message":"Invalid"}"""));
        });

        var client = CreateClient(handler);
        client.BeginAuthenticationSession("stale-access");
        client.SetProfile("stale-profile", "stale-profile-token");
        client.SetTokenRefresher(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.FromResult(true);
        });

        await Assert.ThrowsAsync<ApiException>(() => client.PostUnauthenticatedAsync<TestDto>(
            "/api/v1/auth/refresh",
            new Dictionary<string, object?> { ["refresh_token"] = "bad" }));

        Assert.Null(authorization);
        Assert.False(profileHeaderPresent);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public async Task ReplacementSession_DoesNotReplayOldUnauthorizedRequest()
    {
        var requestCount = 0;
        var handler = new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref requestCount);
            return Task.FromResult<HttpResponseMessage>(JsonResponse(
                HttpStatusCode.Unauthorized,
                """{"error":"unauthorized","message":"Expired"}"""));
        });

        var client = CreateClient(handler);
        client.BeginAuthenticationSession("old-user-token");
        client.SetTokenRefresher(_ =>
        {
            client.BeginAuthenticationSession("new-user-token");
            return Task.FromResult(true);
        });

        await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<TestDto>("/api/v1/test"));

        Assert.Equal(1, requestCount);
        Assert.Equal("new-user-token", client.AccessToken);
    }

    [Fact]
    public async Task ReplacementProfile_DoesNotRefreshOrReplayOldRequest()
    {
        var requestCount = 0;
        var refreshCount = 0;
        SiloApiClient? client = null;
        var handler = new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref requestCount);
            client!.SetProfile("profile-2", "profile-token-2");
            return Task.FromResult<HttpResponseMessage>(JsonResponse(
                HttpStatusCode.Unauthorized,
                """{"error":"unauthorized","message":"Expired"}"""));
        });

        client = CreateClient(handler);
        client.BeginAuthenticationSession("access");
        client.SetProfile("profile-1", "profile-token-1");
        client.SetTokenRefresher(_ =>
        {
            Interlocked.Increment(ref refreshCount);
            return Task.FromResult(true);
        });

        await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<TestDto>("/api/v1/test"));

        Assert.Equal(1, requestCount);
        Assert.Equal(0, refreshCount);
        Assert.Equal("profile-2", client.ProfileId);
    }

    [Fact]
    public async Task ProfileUnverified_InvokesProfileHandlerOnly()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult<HttpResponseMessage>(JsonResponse(
            HttpStatusCode.Forbidden,
            """{"error":"profile_unverified","message":"Verify the profile PIN"}""")));
        var client = CreateClient(handler);
        client.BeginAuthenticationSession("access");
        client.SetProfile("profile-1", "expired-profile-token");
        var profileEvents = 0;
        var refreshEvents = 0;
        client.SetProfileVerificationRequiredHandler(_ => profileEvents++);
        client.SetTokenRefresher(_ =>
        {
            refreshEvents++;
            return Task.FromResult(true);
        });

        var exception = await Assert.ThrowsAsync<ApiException>(() => client.GetAsync<TestDto>("/api/v1/test"));

        Assert.Equal("profile_unverified", exception.ErrorCode);
        Assert.Equal(1, profileEvents);
        Assert.Equal(0, refreshEvents);
        Assert.Equal("access", client.AccessToken);
    }

    [Fact]
    public async Task CreateProfile_SerializesPinAndChildStatus()
    {
        string? sentJson = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            sentJson = await request.Content!.ReadAsStringAsync(ct);
            return JsonResponse(HttpStatusCode.OK,
                """{"id":"profile-1","name":"Kids","has_pin":true,"is_child":true}""");
        });

        var client = CreateClient(handler);
        client.SetAccessToken("access");
        var api = new AuthApi(client);

        var profile = await api.CreateProfileAsync("Kids", "1234", isChild: true);
        using var document = JsonDocument.Parse(sentJson!);

        Assert.Equal("profile-1", profile.Id);
        Assert.Equal("Kids", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("1234", document.RootElement.GetProperty("pin").GetString());
        Assert.True(document.RootElement.GetProperty("is_child").GetBoolean());
    }

    private static SiloApiClient CreateClient(HttpMessageHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return client;
    }

    private static TrackingResponse JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new TrackingResponse(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class TestDto
    {
        public string Name { get; set; } = "";
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

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public TrackingResponse(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
