using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Api;

public class PlaybackApi(ContinuumApiClient client)
{
    public Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<WatchDetailResponse>($"/api/v1/watch/{contentId}", ct);

    public Task<PlaybackStartResponse> StartPlaybackAsync(PlaybackStartRequest request, CancellationToken ct = default)
        => client.PostAsync<PlaybackStartResponse>("/api/v1/playback/start", request, ct);

    public Task ReportProgressAsync(string sessionId, double position, bool isPaused, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/playback/{sessionId}/progress",
            new { position, is_paused = isPaused }, ct);

    public Task StopPlaybackAsync(string sessionId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/playback/{sessionId}", ct);

    public Task<TranscodeStartResponse> StartTranscodeAsync(TranscodeStartRequest request, CancellationToken ct = default)
        => client.PostAsync<TranscodeStartResponse>("/api/v1/playback/transcode/start", request, ct);

    public Task<ChangeAudioResponse> ChangeAudioTrackAsync(string sessionId, int trackIndex, double position, CancellationToken ct = default)
        => client.PatchAsync<ChangeAudioResponse>($"/api/v1/playback/{sessionId}/audio",
            new { audio_track_index = trackIndex, position }, ct);
}

public class ChangeAudioResponse
{
    public int AudioTrackIndex { get; set; }
    public string PlayMethod { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string SwitchMode { get; set; } = "";
    public PlaybackInfo? PlaybackInfo { get; set; }
}
