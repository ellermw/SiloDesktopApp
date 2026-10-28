using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackManagerTests
{
    [Fact]
    public async Task SubtitleResources_AuthenticateApiRelativePathsAndKeepDistributedBytes()
    {
        var handler = new BlockingPlaybackHandler();
        var api = new SiloApiClient(new HttpClient(handler)); api.SetBaseUrl("https://example.test"); api.SetAccessToken("fixture-account");
        using var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), new AuthService(api, new AuthApi(api)), api);
        try
        {
            await manager.StartSessionAsync(123);
            manager.CurrentSession!.SubtitleUrls = [
                new() { Index = 0, Url = "/stream/session/subtitles/0.vtt?st=fixture%2fquery" },
                new() { Index = 1, Url = "https://node.fixture/stream/subtitles/fixture-signed/1?st=fixture%2fquery" },
                new() { Index = 2, Url = "https://example.test/stream/subtitles/fixture-signed/2?st=fixture%2fquery" },
            ];
            var resources = manager.GetSubtitleUrls();
            Assert.Equal("https://example.test/api/v2/stream/session/subtitles/0.vtt?st=fixture%2fquery&token=fixture-account", resources[0].FullUrl);
            Assert.Equal("https://node.fixture/stream/subtitles/fixture-signed/1?st=fixture%2fquery", resources[1].FullUrl);
            Assert.Equal("https://example.test/stream/subtitles/fixture-signed/2?st=fixture%2fquery", resources[2].FullUrl);
            api.SetAccessToken("fixture-refreshed");
            Assert.EndsWith("&token=fixture-refreshed", manager.GetSubtitleUrls()[0].FullUrl);
        }
        finally { handler.ReleaseProgress(); await manager.StopSessionAsync(); }
    }

    [Theory]
    [InlineData("https://example.test", "")]
    [InlineData("https://example.test", "?st=fixture%2fquery")]
    [InlineData("https://example.test/silo", "")]
    [InlineData("https://example.test/silo", "?st=fixture%2fquery")]
    public async Task AbsoluteIntegratedSubtitles_RetainAccountAuthenticationWithOrWithoutStreamReference(string serverBase, string query)
    {
        var handler = new BlockingPlaybackHandler();
        var api = new SiloApiClient(new HttpClient(handler)); api.SetBaseUrl("https://example.test"); api.SetAccessToken("fixture-account");
        using var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), new AuthService(api, new AuthApi(api)), api);
        try
        {
            await manager.StartSessionAsync(123);
            // The resource resolver uses the configured deployment prefix. No
            // HTTP is dispatched after changing this synthetic fixture base.
            api.SetBaseUrl(serverBase);
            api.SetAccessToken("fixture-account");
            var integrated = serverBase + "/api/v2/stream/session/subtitles/0.vtt" + query;
            manager.CurrentSession!.SubtitleUrls = [
                new() { Index = 0, Url = integrated },
                new() { Index = 1, Url = "https://cdn.fixture/api/v2/stream/session/subtitles/0.vtt" + query },
                new() { Index = 2, Url = "https://example.test/stream/subtitles/fixture-signed/2?st=fixture%2fquery" },
            ];
            var resources = manager.GetSubtitleUrls();
            Assert.Equal(integrated + (query.Length == 0 ? "?" : "&") + "token=fixture-account", resources[0].FullUrl);
            Assert.Equal("https://cdn.fixture/api/v2/stream/session/subtitles/0.vtt" + query, resources[1].FullUrl);
            Assert.Equal("https://example.test/stream/subtitles/fixture-signed/2?st=fixture%2fquery", resources[2].FullUrl);
        }
        finally { handler.ReleaseProgress(); await manager.StopSessionAsync(); }
    }

    [Fact]
    public async Task StartSessionAsync_AdvertisesTheVerifiedBundledMpvProfile()
    {
        var handler = new BlockingPlaybackHandler();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        var manager = new PlaybackManager(
            new PlaybackApi(apiClient),
            new CatalogApi(apiClient),
            new AuthService(apiClient, new AuthApi(apiClient)),
            apiClient);

        try
        {
            await manager.StartSessionAsync(123, forceStartPosition: true);

            Assert.NotNull(handler.LastStartBody);
            Assert.Contains("\"vc1\"", handler.LastStartBody);
            Assert.Contains("\"truehd\"", handler.LastStartBody);
            Assert.Contains("\"hdr10_plus\":true", handler.LastStartBody);
            Assert.Contains("\"dolby_vision_profiles\":[5,7,8]", handler.LastStartBody);
            Assert.Contains("\"protocol_version\":3", handler.LastStartBody);
            Assert.Contains("\"client_features\":[\"playback_plan_v3\",\"plan_invalidated_v1\",\"subrip_sidecar_v1\"]", handler.LastStartBody);
            Assert.Contains("\"video_evidence\":\"declared\"", handler.LastStartBody);
            Assert.Contains("\"audio_evidence\":\"declared\"", handler.LastStartBody);
            Assert.Contains("\"client_playback_context\"", handler.LastStartBody);
            Assert.DoesNotContain("\"audio_passthrough\"", handler.LastStartBody);
        }
        finally
        {
            handler.ReleaseProgress();
            await manager.StopSessionAsync();
            manager.Dispose();
        }
    }

    [Fact]
    public async Task StartSessionAsync_CanDisablePerFileProgressForMultipartAudiobooks()
    {
        var handler = new BlockingPlaybackHandler();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var manager = new PlaybackManager(
            new PlaybackApi(apiClient),
            new CatalogApi(apiClient),
            new AuthService(apiClient, new AuthApi(apiClient)),
            apiClient);

        try
        {
            await manager.StartSessionAsync(
                123,
                startPosition: 17,
                forceStartPosition: true,
                disableProgressPersistence: true);

            Assert.Contains("\"start_position\":17", handler.LastStartBody);
            Assert.Contains("\"progress_persistence\":\"client\"", handler.LastStartBody);
        }
        finally
        {
            handler.ReleaseProgress();
            await manager.StopSessionAsync();
        }
    }

    [Fact]
    public async Task StopSessionAsync_CancelsInFlightProgressBeforeReturning()
    {
        var handler = new BlockingPlaybackHandler();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        var playbackApi = new PlaybackApi(apiClient);
        var catalogApi = new CatalogApi(apiClient);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));
        var manager = new PlaybackManager(playbackApi, catalogApi, authService, apiClient);

        try
        {
            await manager.StartSessionAsync(123, startPosition: 0, forceStartPosition: true);
            await handler.ProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var stopTask = manager.StopSessionAsync();
            var progressCanceledBeforeRelease = await WaitForSignalAsync(
                handler.ProgressCanceled.Task,
                TimeSpan.FromMilliseconds(500));

            handler.ReleaseProgress();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.True(
                progressCanceledBeforeRelease,
                "Stopping playback should cancel any in-flight progress POST before the manager is torn down.");
            Assert.True(handler.StopPlaybackCalled);
        }
        finally
        {
            handler.ReleaseProgress();
            manager.Dispose();
        }
    }

    [Fact]
    public async Task StopSessionAsync_UsesCallerDeadlineAndAlwaysClearsLocalSession()
    {
        var handler = new BlockingPlaybackHandler { BlockStopPlayback = true };
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        var manager = new PlaybackManager(
            new PlaybackApi(apiClient),
            new CatalogApi(apiClient),
            new AuthService(apiClient, new AuthApi(apiClient)),
            apiClient);

        handler.ReleaseProgress();
        await manager.StartSessionAsync(123, forceStartPosition: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await manager.StopSessionAsync(42, true, cts.Token)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Null(manager.SessionId);
        Assert.Null(manager.CurrentSession);
        manager.Dispose();
    }

    [Fact]
    public void StopSessionAsync_GivesStopFinalizationAnIndependentDeadline()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "SiloPlayer.Core", "Services", "PlaybackManager.cs"));

        Assert.DoesNotContain("operationCts.CancelAfter(TimeSpan.FromSeconds(5))", source);
        Assert.Contains("stopCts.CancelAfter(TimeSpan.FromSeconds(15))", source);
    }

    [Fact]
    public async Task StartReplacementSessionAsync_SwapsBeforeSlowPreviousCleanupCompletes()
    {
        var handler = new ReplacementPlaybackHandler();
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        using var manager = new PlaybackManager(
            new PlaybackApi(apiClient),
            new CatalogApi(apiClient),
            new AuthService(apiClient, new AuthApi(apiClient)),
            apiClient);

        await manager.StartSessionAsync(123, forceStartPosition: true);
        var replacement = await manager.StartReplacementSessionAsync(
            456,
            120,
            previousFinalPosition: 120).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("session-2", replacement.SessionId);
        Assert.Equal("session-2", manager.SessionId);
        await handler.PreviousDeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(handler.ReleasePreviousDelete.Task.IsCompleted);

        handler.ReleasePreviousDelete.TrySetResult();
        await manager.StopSessionAsync();
    }

    private static async Task<bool> WaitForSignalAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }

    private sealed class BlockingPlaybackHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _progressStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _progressCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ProgressStarted => _progressStarted;
        public TaskCompletionSource ProgressCanceled => _progressCanceled;
        public bool StopPlaybackCalled { get; private set; }
        public bool BlockStopPlayback { get; set; }
        public string? LastStartBody { get; private set; }

        public void ReleaseProgress()
        {
            _releaseProgress.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v2/playback/capabilities")
                return JsonResponse("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");

            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/start")
            {
                LastStartBody = request.Content == null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return JsonResponse(PlayableDecision("session-1", 123, 0));
            }

            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/session-1/progress")
            {
                _progressStarted.TrySetResult();
                try
                {
                    await _releaseProgress.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _progressCanceled.TrySetResult();
                    throw;
                }

                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");
            }

            if (request.Method == HttpMethod.Delete && path == "/api/v2/playback/session-1")
            {
                StopPlaybackCalled = true;
                if (BlockStopPlayback)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");
            }

            if (request.Method == HttpMethod.Post && path == "/api/v2/sync/progress")
            {
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"error":"not_found","message":"Unexpected test request."}""")
            };
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }

        private static string PlayableDecision(string sessionId, int fileId, double position) => $$"""
            {
              "protocol_version": 3,
              "server_features": ["playback_plan_v3", "plan_source_duration_v1"],
              "outcome": "playable",
              "session_id": "{{sessionId}}",
              "playback_plan": {
                "protocol_version": 3,
                "plan_id": "plan:478677870860e5e5108c18bff749b34b",
                "plan_attempt_key": "v3:f0144c47fa349e3e",
                "session_id": "{{sessionId}}",
                "delivery": "original_http",
                "stream": { "url": "/stream/{{sessionId}}", "protocol": "http_progressive", "headers": {} },
                "timeline": { "source_start_seconds": {{position}}, "stream_origin_seconds": 0, "player_start_seconds": {{position}}, "timeline_offset_seconds": 0, "can_seek_anywhere": true },
                "selected_tracks": { "audio": { "id": "file:{{fileId}}:audio:0", "index": 0 } },
                "effective_recipe": { "video_codec": "hevc", "audio_codec": "aac" },
                "subtitle": { "mode": "off", "inventory": [] },
                "available_qualities": [{ "label": "original", "preserves_source": true }],
                "degradation_warnings": [],
                "requested_media_file_id": {{fileId}},
                "effective_media_file_id": {{fileId}},
                "source": { "media_file_id": {{fileId}}, "duration_seconds": 3600 }
              }
            }
            """;
    }

    private sealed class ReplacementPlaybackHandler : HttpMessageHandler
    {
        private int _startCount;
        public TaskCompletionSource PreviousDeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePreviousDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v2/playback/capabilities")
                return JsonResponse("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/start")
            {
                var number = Interlocked.Increment(ref _startCount);
                var fileId = number == 1 ? 123 : 456;
                var position = number == 1 ? 0 : 120;
                return JsonResponse($$"""
                    {
                      "protocol_version": 3,
                      "outcome": "playable",
                      "session_id": "session-{{number}}",
                      "playback_plan": {
                        "protocol_version": 3,
                        "plan_id": "plan:478677870860e5e5108c18bff749b34b",
                        "plan_attempt_key": "v3:f0144c47fa349e3e",
                        "session_id": "session-{{number}}",
                        "delivery": "original_http",
                        "stream": { "url": "/stream/session-{{number}}", "protocol": "http_progressive" },
                        "timeline": { "source_start_seconds": {{position}}, "stream_origin_seconds": 0, "player_start_seconds": {{position}}, "timeline_offset_seconds": 0, "can_seek_anywhere": true },
                        "selected_tracks": {},
                        "effective_recipe": {},
                        "subtitle": { "mode": "off", "inventory": [] },
                        "available_qualities": [],
                        "degradation_warnings": [],
                        "requested_media_file_id": {{fileId}},
                        "effective_media_file_id": {{fileId}},
                        "source": { "media_file_id": {{fileId}}, "duration_seconds": 3600 }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post && path.Contains("/progress", StringComparison.Ordinal))
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");

            if (request.Method == HttpMethod.Delete && path == "/api/v2/playback/session-1")
            {
                PreviousDeleteStarted.TrySetResult();
                await ReleasePreviousDelete.Task.WaitAsync(cancellationToken);
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");
            }

            if (request.Method == HttpMethod.Delete && path == "/api/v2/playback/session-2")
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");

            if (request.Method == HttpMethod.Post && path == "/api/v2/sync/progress")
                return JsonResponse(request.Method == HttpMethod.Delete ? "{\"outcome\":\"stopped\"}" : "{\"outcome\":\"applied\"}");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
