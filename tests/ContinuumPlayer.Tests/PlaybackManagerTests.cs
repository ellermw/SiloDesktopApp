using System.Net;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

public sealed class PlaybackManagerTests
{
    [Fact]
    public async Task StopSessionAsync_CancelsInFlightProgressBeforeReturning()
    {
        var handler = new BlockingPlaybackHandler();
        var apiClient = new ContinuumApiClient(new HttpClient(handler));
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
    public async Task ApplyAudioChange_UpdatesCurrentSessionMetadata()
    {
        var handler = new BlockingPlaybackHandler();
        var apiClient = new ContinuumApiClient(new HttpClient(handler));
        apiClient.SetBaseUrl("https://example.test");
        var playbackApi = new PlaybackApi(apiClient);
        var catalogApi = new CatalogApi(apiClient);
        var authService = new AuthService(apiClient, new AuthApi(apiClient));
        using var manager = new PlaybackManager(playbackApi, catalogApi, authService, apiClient);

        await manager.StartSessionAsync(123, startPosition: 0, forceStartPosition: true);

        manager.ApplyAudioChange(new ChangeAudioResponse
        {
            AudioTrackIndex = 2,
            PlayMethod = "remux",
            StreamUrl = "/stream/session-1?audio=2",
            PlaybackInfo = new PlaybackInfo
            {
                StreamType = "progressive",
                TranscodeAudio = true,
                VideoCodec = "hevc",
                AudioCodec = "aac"
            }
        });

        Assert.NotNull(manager.CurrentSession);
        Assert.Equal(2, manager.CurrentSession.AudioTrackIndex);
        Assert.Equal("remux", manager.CurrentSession.PlayMethod);
        Assert.Equal("/stream/session-1?audio=2", manager.CurrentSession.StreamUrl);
        Assert.True(manager.CurrentSession.PlaybackInfo?.TranscodeAudio);
        Assert.Equal("aac", manager.CurrentSession.PlaybackInfo?.AudioCodec);
    }

    private static async Task<bool> WaitForSignalAsync(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        return completed == task;
    }

    private sealed class BlockingPlaybackHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _progressStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _progressCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ProgressStarted => _progressStarted;
        public TaskCompletionSource ProgressCanceled => _progressCanceled;
        public bool StopPlaybackCalled { get; private set; }

        public void ReleaseProgress()
        {
            _releaseProgress.TrySetResult();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";

            if (request.Method == HttpMethod.Post && path == "/api/v1/playback/start")
            {
                return JsonResponse(
                    """
                    {
                      "session_id": "session-1",
                      "media_file_id": 123,
                      "play_method": "direct",
                      "position": 0,
                      "is_paused": false,
                      "stream_url": "/stream/session-1",
                      "audio_track_index": 0,
                      "duration_seconds": 3600,
                      "subtitle_urls": [],
                      "playback_info": {
                        "stream_type": "progressive",
                        "transcode_audio": false,
                        "video_codec": "hevc",
                        "audio_codec": "aac"
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/playback/session-1/progress")
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

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Delete && path == "/api/v1/playback/session-1")
            {
                StopPlaybackCalled = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/sync/progress")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
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
    }
}
