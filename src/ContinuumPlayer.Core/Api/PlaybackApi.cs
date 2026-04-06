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

    // ===== Subtitles =====

    public Task<SubtitleListResponse> GetSubtitlesAsync(int mediaFileId, CancellationToken ct = default)
        => client.GetAsync<SubtitleListResponse>($"/api/v1/subtitles/{mediaFileId}", ct);

    public Task DeleteSubtitleAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/subtitles/{id}", ct);

    public Task<SubtitleSearchResponse> SearchSubtitlesAsync(int mediaFileId, string[] languages, CancellationToken ct = default)
        => client.PostAsync<SubtitleSearchResponse>("/api/v1/subtitles/search",
            new { media_file_id = mediaFileId, languages }, ct);

    public Task<SubtitleDownloadResponse> DownloadSubtitleAsync(int mediaFileId, string provider, string subtitleId, string language, string format, CancellationToken ct = default)
        => client.PostAsync<SubtitleDownloadResponse>("/api/v1/subtitles/download",
            new { media_file_id = mediaFileId, provider, subtitle_id = subtitleId, language, format }, ct);
}

public class SubtitleListResponse
{
    public List<SubtitleEntry> Subtitles { get; set; } = [];
}

public class SubtitleEntry
{
    public int Id { get; set; }
    public int MediaFileId { get; set; }
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string Source { get; set; } = "";
    public bool Forced { get; set; }
}

public class SubtitleSearchResponse
{
    public List<SubtitleSearchResult> Results { get; set; } = [];
}

public class SubtitleSearchResult
{
    public string Provider { get; set; } = "";
    public string SubtitleId { get; set; } = "";
    public string Language { get; set; } = "";
    public string? ReleaseName { get; set; }
    public string Format { get; set; } = "srt";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
}

public class SubtitleDownloadResponse
{
    public int Id { get; set; }
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
}

public class ChangeAudioResponse
{
    public int AudioTrackIndex { get; set; }
    public string PlayMethod { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string SwitchMode { get; set; } = "";
    public PlaybackInfo? PlaybackInfo { get; set; }
}
