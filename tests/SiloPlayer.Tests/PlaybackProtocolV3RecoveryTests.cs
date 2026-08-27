using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackProtocolV3RecoveryTests
{
    [Fact]
    public async Task FailureReplansCarryTheCompleteAttemptChain()
    {
        var handler = new RecoveryPlaybackHandler();
        using var manager = CreateManager(handler);

        await manager.StartSessionAsync(42, forceStartPosition: true);
        await manager.ReplanFailureAsync(10, "network_error");
        await manager.ReplanFailureAsync(11, "network_error");

        Assert.Equal(2, handler.ReplanBodies.Count);
        AssertRequest(handler.ReplanBodies[0], 1, ["v3:0000000000000001"]);
        AssertRequest(handler.ReplanBodies[1], 2,
            ["v3:0000000000000001", "v3:0000000000000002"]);

        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task UserIntentReplanResetsTheFailureAttemptChain()
    {
        var handler = new RecoveryPlaybackHandler();
        using var manager = CreateManager(handler);

        await manager.StartSessionAsync(42, forceStartPosition: true);
        await manager.ReplanFailureAsync(10, "network_error");
        await manager.ReplanQualityAsync("720p", 10);
        await manager.ReplanFailureAsync(11, "network_error");

        AssertRequest(handler.ReplanBodies[1], 1, []);
        AssertRequest(handler.ReplanBodies[2], 1, ["v3:0000000000000003"]);

        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task TerminalDecisionPreservesStructuredReasonMessageAndRetryability()
    {
        var handler = new RecoveryPlaybackHandler { ReturnTerminalOnStart = true };
        using var manager = CreateManager(handler);

        var error = await Assert.ThrowsAsync<PlaybackPlanTerminalException>(
            () => manager.StartSessionAsync(42, forceStartPosition: true));

        Assert.Equal("source_unavailable", error.Reason);
        Assert.Equal("The effective media source is unavailable.", error.ServerMessage);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task PlanInvalidationReplansOnlyTheNamedActivePlan()
    {
        var handler = new RecoveryPlaybackHandler();
        using var manager = CreateManager(handler);
        await manager.StartSessionAsync(42, forceStartPosition: true);

        var stale = await manager.ReplanInvalidatedPlanAsync(
            "plan:ffffffffffffffffffffffffffffffff",
            "video_copy_unsafe",
            15);
        var adopted = await manager.ReplanInvalidatedPlanAsync(
            "plan:00000000000000000000000000000001",
            "video_copy_unsafe",
            15);

        Assert.Null(stale);
        Assert.NotNull(adopted);
        Assert.Single(handler.ReplanBodies);
        using var document = JsonDocument.Parse(handler.ReplanBodies[0]);
        Assert.Equal(
            "video_copy_unsafe",
            document.RootElement.GetProperty("failure").GetProperty("classification").GetString());
        await manager.StopSessionAsync();
    }

    private static PlaybackManager CreateManager(HttpMessageHandler handler)
    {
        var apiClient = new SiloApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        return new PlaybackManager(
            new PlaybackApi(apiClient),
            new CatalogApi(apiClient),
            new AuthService(apiClient, new AuthApi(apiClient)),
            apiClient);
    }

    private static void AssertRequest(string json, int attemptCount, string[] attemptedKeys)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(attemptCount, root.GetProperty("attempt_count").GetInt32());
        Assert.Equal(
            attemptedKeys,
            root.GetProperty("attempted_plan_keys").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    private sealed class RecoveryPlaybackHandler : HttpMessageHandler
    {
        private int _planNumber;
        public bool ReturnTerminalOnStart { get; set; }
        public List<string> ReplanBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Post && path == "/api/v1/playback/start")
            {
                if (ReturnTerminalOnStart)
                {
                    return JsonResponse("""
                        {
                          "protocol_version": 3,
                          "outcome": "terminal",
                          "terminal": {
                            "reason": "source_unavailable",
                            "message": "The effective media source is unavailable.",
                            "retryable": true
                          }
                        }
                        """);
                }

                return JsonResponse(PlayableDecision(NextPlanNumber()));
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/playback/session-1/replan")
            {
                ReplanBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return JsonResponse(PlayableDecision(NextPlanNumber()));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/progress", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Delete && path == "/api/v1/playback/session-1")
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post && path == "/api/v1/sync/progress")
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private int NextPlanNumber() => Interlocked.Increment(ref _planNumber);

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        private static string PlayableDecision(int number) => $$"""
            {
              "protocol_version": 3,
              "server_features": ["playback_plan_v3", "plan_invalidated_v1"],
              "outcome": "playable",
              "session_id": "session-1",
              "playback_plan": {
                "protocol_version": 3,
                "plan_id": "plan:{{number:x32}}",
                "plan_attempt_key": "v3:{{number:x16}}",
                "session_id": "session-1",
                "delivery": "original_http",
                "stream": { "url": "/stream/session-1", "protocol": "http_progressive", "headers": {} },
                "timeline": { "source_start_seconds": 0, "stream_origin_seconds": 0, "player_start_seconds": 0, "timeline_offset_seconds": 0, "can_seek_anywhere": true },
                "selected_tracks": { "audio": { "id": "file:42:audio:0", "index": 0 } },
                "effective_recipe": { "video_codec": "hevc", "audio_codec": "aac" },
                "subtitle": { "mode": "off", "inventory": [] },
                "available_qualities": [{ "label": "original", "preserves_source": true }],
                "degradation_warnings": [],
                "requested_media_file_id": 42,
                "effective_media_file_id": 42,
                "source": { "media_file_id": 42, "duration_seconds": 3600 }
              }
            }
            """;
    }
}
