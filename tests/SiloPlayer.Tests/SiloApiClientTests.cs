using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SiloApiClientTests
{
    [Fact]
    public async Task DismissalHttpDiagnostics_CorrelateStatusWithoutLoggingIdentityOrCredentials()
    {
        var action = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "SiloPlayer.Tests", action);
        using var scope = SiloPlayer.Core.Services.LocalLog.BeginOperation(action, directory);
        var client = CreateClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));
        client.SetAccessToken("diagnostic-secret-token");
        await new HomeApi(client).DismissItemAsync("next_up", "diagnostic-private-item", new { series_id = "diagnostic-private-series" });
        var path = Path.Combine(directory, "api_diagnostics.txt");
        Assert.True(File.Exists(path), "Dismissal HTTP result was not persisted.");
        var lines = File.ReadAllLines(path).Where(line => line.Contains(action)).ToArray();
        Assert.Contains(lines, line => line.Contains("status=204") && line.Contains("attempt=1") && line.Contains("elapsedMs="));
        Assert.Contains(lines, line => line.Contains("route=home.dismissals.next_up"));
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain("diagnostic-secret-token", line);
            Assert.DoesNotContain("diagnostic-private-item", line);
            Assert.DoesNotContain("diagnostic-private-series", line);
        });
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }

    [Fact]
    public async Task SimilarCardsUseTheCurrentTwelveItemContract()
    {
        string? path = null;
        var handler = new DelegateHandler((request, _) =>
        {
            path = request.RequestUri!.PathAndQuery;
            return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.OK, """{"items":[{"content_id":"movie-1","play_content_id":"play-1","title":"A recommended title","poster_url":"https://fixture.invalid/poster"}]}"""));
        });
        var result = await new CatalogApi(CreateClient(handler)).GetSimilarAsync("movie:source");
        Assert.Equal("/api/v2/recommendations/similar/movie%3Asource?limit=12", path);
        Assert.Equal("play-1", Assert.Single(result.Items).PlayContentId);
    }
    [Fact]
    public void ResolveServerUrl_HandlesRootRelativeAndAbsoluteBrandingAssets()
    {
        var client = CreateClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        client.SetBaseUrl("https://silo.example.test");

        Assert.Equal(
            "https://silo.example.test/api/v2/branding/assets/mark?v=one",
            client.ResolveServerUrl("/api/v2/branding/assets/mark?v=one"));
        Assert.Equal(
            "https://cdn.example.test/brand.webp",
            client.ResolveServerUrl("https://cdn.example.test/brand.webp"));
        Assert.Null(client.ResolveServerUrl(" "));
    }

    [Theory]
    [InlineData("/profile-avatars/avatar-3.svg", "https://silo.example.test/profile-avatars/avatar-3.svg")]
    [InlineData("//cdn.example.test/avatar.png", "https://cdn.example.test/avatar.png")]
    [InlineData("https://api.dicebear.com/9.x/thumbs/svg?seed=fox", "https://api.dicebear.com/9.x/thumbs/svg?seed=fox")]
    public void ResolveServerUrl_TreatsRootAndProtocolRelativePathsAsServerUrlsOnEveryPlatform(string value, string expected)
    {
        // Unix parses "/path" as file:///path, Windows parses "//host" as a UNC path.
        var client = CreateClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        client.SetBaseUrl("https://silo.example.test");

        Assert.Equal(expected, client.ResolveServerUrl(value));
    }

    [Fact]
    public async Task ProfilesLeaveTheApiWithAbsoluteAvatarUrls()
    {
        const string profile = """{"id":"p1","name":"Preset","avatar_url":"/profile-avatars/avatar-3.svg"}""";
        var handler = new DelegateHandler((request, _) => Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.OK,
            request.Method == HttpMethod.Get
                ? "{\"items\":[" + profile + """,{"id":"p2","name":"Generated","avatar_url":"https://api.dicebear.com/9.x/thumbs/svg?seed=fox"},{"id":"p3","name":"None"}]}"""
                : profile)));
        var client = CreateClient(handler);
        client.SetBaseUrl("https://silo.example.test");
        client.SetAccessToken("access");
        var auth = new AuthApi(client);
        var settings = new SettingsApi(client);

        foreach (var profiles in new[] { (await auth.GetProfilesAsync()).Profiles, (await settings.GetProfilesAsync()).Profiles })
        {
            Assert.Equal("https://silo.example.test/profile-avatars/avatar-3.svg", profiles[0].AvatarUrl);
            Assert.Equal("https://api.dicebear.com/9.x/thumbs/svg?seed=fox", profiles[1].AvatarUrl);
            Assert.Null(profiles[2].AvatarUrl);
        }
        foreach (var updated in new[]
                 {
                     await auth.CreateProfileAsync("Preset"),
                     await auth.UpdateProfileAsync("p1", "Preset", null),
                     await auth.DeleteProfileAvatarAsync("p1"),
                     await settings.UpdateProfileAsync("p1", new { name = "Preset" }),
                 })
            Assert.Equal("https://silo.example.test/profile-avatars/avatar-3.svg", updated.AvatarUrl);
    }

    [Theory]
    [InlineData("https://silo.example.test/profile-avatars/avatar-3.svg", true)]
    [InlineData("https://silo.example.test/profile-avatars/AVATAR-3.SVG?v=2", true)]
    [InlineData("https://api.dicebear.com/9.x/thumbs/svg?seed=fox&size=128", true)]
    [InlineData("https://s3.example.test/avatars/1/p-owner/256.jpg", false)]
    [InlineData("https://other.example.test/9.x/thumbs/svg", false)]
    public void SvgArtwork_IdentifiesBundledAndGeneratedAvatars(string url, bool expected)
        => Assert.Equal(expected, SvgArtwork.IsSvgUrl(new Uri(url)));

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
        string? clientFamily = null;
        string? clientVersion = null;
        string? path = null;
        var handler = new DelegateHandler((request, _) =>
        {
            path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.OK, "{\"allowed\":true,\"state\":\"available\",\"installation_id\":\"installation\",\"protocol_versions\":[3]}"));
            clientName = request.Headers.TryGetValues("X-Silo-Client", out var names)
                ? names.SingleOrDefault()
                : null;
            clientFamily = request.Headers.TryGetValues("X-Silo-Client-Family", out var families)
                ? families.SingleOrDefault()
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

        await api.StartPlaybackV3Async(new PlaybackStartRequestV3
        {
            FileId = 7,
            ProfileId = "profile-1",
        });

        Assert.Equal("/api/v2/playback/start", path);
        Assert.Equal("Silo for Windows", clientName);
        Assert.Equal("desktop", clientFamily);
        Assert.Null(clientVersion);
    }

    [Fact]
    public async Task AudioReplan_DeserializesAuthoritativeRestartTimeline()
    {
        var handler = new DelegateHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
                return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.OK, """{"allowed":true,"state":"available","installation_id":"installation","protocol_versions":[3]}"""));
            if (request.RequestUri.AbsolutePath.EndsWith("/start"))
                return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.Created, """{"outcome":"playable","session_id":"session-1"}"""));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/playback/session-1/replan", request.RequestUri.AbsolutePath);
            return Task.FromResult<HttpResponseMessage>(JsonResponse(HttpStatusCode.OK,
                """
                {
                  "outcome":"playable",
                  "playback_plan": {
                    "delivery":"remux",
                    "timeline": {
                      "player_start_seconds":1.75,
                      "stream_origin_seconds":418.25,
                      "timeline_offset_seconds":418.25
                    }
                  }
                }
                """));
        });
        var api = new PlaybackApi(CreateClient(handler));

        await api.StartPlaybackV3Async(new() { FileId = 7 });
        var response = await api.ReplanPlaybackV3Async("session-1", new() { Operation = "audio", PositionSeconds = 420 });
        Assert.Equal("remux", response.PlaybackPlan!.Delivery);
        Assert.Equal(1.75, response.PlaybackPlan.Timeline.PlayerStartSeconds);
        Assert.Equal(418.25, response.PlaybackPlan.Timeline.StreamOriginSeconds);
        Assert.Equal(418.25, response.PlaybackPlan.Timeline.TimelineOffsetSeconds);
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
                ? JsonResponse(HttpStatusCode.OK, """{"items":[{"content_id":"movie-1"},{"content_id":"movie-2"}],"page":{"has_more":true,"next_cursor":"next-page"}}""")
                : JsonResponse(HttpStatusCode.OK, """{"added":2}""");
        });
        var api = new RecommendationsApi(CreateClient(handler));

        var page = await api.GetTasteSeedItemsAsync(2, 0);
        var result = await api.SubmitTasteSeedAsync(["movie-1", "movie-2"]);

        Assert.Equal(2, page.NextOffset);
        Assert.Equal(2, result.Added);
        Assert.Equal("/api/v2/recommendations/taste-seed/items?limit=2", requests[0].Path);
        Assert.Equal("/api/v2/recommendations/taste-seed", requests[1].Path);
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
