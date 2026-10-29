using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackPersistenceCostTests
{
    [Fact]
    public async Task ImmediateProgressReportsDuringAScrubBurstCollapseToTheLatestPosition()
    {
        var handler = new CountingProgressHandler();
        var api = new SiloApiClient(new HttpClient(handler)); api.SetBaseUrl("https://example.test"); api.SetAccessToken("fixture");
        using var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), new AuthService(api, new AuthApi(api)), api);
        await manager.StartSessionAsync(123, forceStartPosition: true);

        // The first report holds the progress guard; ten more seeks queue behind it.
        var first = manager.ReportProgressNowAsync(10, isPaused: false);
        await handler.FirstProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var burst = Enumerable.Range(1, 10).Select(step => manager.ReportProgressNowAsync(10 + step * 30, isPaused: false)).ToArray();
        handler.ReleaseFirst();
        await Task.WhenAll([first, .. burst]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.ProgressPositions.Count);
        Assert.Equal(10, handler.ProgressPositions[0]);
        Assert.Equal(310, handler.ProgressPositions[^1]);
        handler.ReleaseFirst();
        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task CanceledNewestReportDoesNotSuppressAnEligibleQueuedReport()
    {
        var handler = new CountingProgressHandler();
        var api = new SiloApiClient(new HttpClient(handler)); api.SetBaseUrl("https://example.test"); api.SetAccessToken("fixture");
        using var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), new AuthService(api, new AuthApi(api)), api);
        await manager.StartSessionAsync(123, forceStartPosition: true);
        var first = manager.ReportProgressNowAsync(10, false);
        await handler.FirstProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var eligible = manager.ReportProgressNowAsync(20, false);
        using var cancellation = new CancellationTokenSource();
        var canceled = manager.ReportProgressNowAsync(30, false, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        handler.ReleaseFirst();
        await Task.WhenAll(first, eligible).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, handler.ProgressPositions.Count);
        Assert.Equal(30, handler.ProgressPositions[^1]);
        await manager.StopSessionAsync();
    }

    [Fact]
    public async Task AlreadyCanceledReportDoesNotReplaceThePendingPosition()
    {
        var handler = new CountingProgressHandler();
        var api = new SiloApiClient(new HttpClient(handler)); api.SetBaseUrl("https://example.test"); api.SetAccessToken("fixture");
        using var manager = new PlaybackManager(new PlaybackApi(api), new CatalogApi(api), new AuthService(api, new AuthApi(api)), api);
        await manager.StartSessionAsync(123, forceStartPosition: true);
        var first = manager.ReportProgressNowAsync(10, false);
        // Generous bounds for full-suite load; a passing run ends immediately.
        await handler.FirstProgressStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var eligible = manager.ReportProgressNowAsync(20, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ReportProgressNowAsync(30, false, new CancellationToken(true)));
        handler.ReleaseFirst();
        await Task.WhenAll(first, eligible).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(20, handler.ProgressPositions[^1]);
        await manager.StopSessionAsync();
    }

    [Fact]
    public void PlayerServiceCoalescesVolumeWritesAndCachesAudiobookChapters()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("_volumeSaveTimer.Interval = TimeSpan.FromMilliseconds(500);", source);
        Assert.Contains("_volumeSaveTimer.IsRepeating = false;", source);
        // Close and shutdown persist the final level immediately.
        Assert.Contains("FlushVolumeStateOnClose();", source);
        Assert.Contains("if (HasPendingVolumeSave)", source);
        Assert.Contains("if (_audiobookChapterCache is { } cached && ReferenceEquals(cached.Versions, versions)) return cached.Chapters;", source);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }

    private sealed class CountingProgressHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstProgressStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<double> ProgressPositions { get; } = [];
        public void ReleaseFirst() => _releaseFirst.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path == "/api/v2/playback/capabilities")
                return Json("""{"allowed":true,"state":"available","installation_id":"11111111-1111-4111-8111-111111111111","protocol_versions":[3],"features":["fixed_media_file_v1"]}""");
            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/start")
                return Json(Decision);
            if (request.Method == HttpMethod.Post && path == "/api/v2/playback/session-1/progress")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                lock (ProgressPositions) ProgressPositions.Add(body.RootElement.GetProperty("position").GetDouble());
                if (FirstProgressStarted.TrySetResult()) await _releaseFirst.Task.WaitAsync(cancellationToken);
                return Json("{\"outcome\":\"applied\"}");
            }
            if (request.Method == HttpMethod.Delete && path == "/api/v2/playback/session-1")
                return Json("{\"outcome\":\"stopped\"}");
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        private const string Decision = """
            {
              "protocol_version": 3,
              "server_features": ["playback_plan_v3", "plan_source_duration_v1"],
              "outcome": "playable",
              "session_id": "session-1",
              "playback_plan": {
                "protocol_version": 3,
                "plan_id": "plan:478677870860e5e5108c18bff749b34b",
                "plan_attempt_key": "v3:f0144c47fa349e3e",
                "session_id": "session-1",
                "delivery": "original_http",
                "stream": { "url": "/stream/session-1", "protocol": "http_progressive", "headers": {} },
                "timeline": { "source_start_seconds": 0, "stream_origin_seconds": 0, "player_start_seconds": 0, "timeline_offset_seconds": 0, "can_seek_anywhere": true },
                "selected_tracks": { "audio": { "id": "file:123:audio:0", "index": 0 } },
                "effective_recipe": { "video_codec": "hevc", "audio_codec": "aac" },
                "subtitle": { "mode": "off", "inventory": [] },
                "available_qualities": [{ "label": "original", "preserves_source": true }],
                "degradation_warnings": [],
                "requested_media_file_id": 123,
                "effective_media_file_id": 123,
                "source": { "media_file_id": 123, "duration_seconds": 3600 }
              }
            }
            """;
    }
}
