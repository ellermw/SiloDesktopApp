using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Api;

public partial class PlaybackApi(SiloApiClient client)
{
    public Task<WatchDetailResponse> GetWatchDetailAsync(string contentId, CancellationToken ct = default)
        => client.GetAsync<WatchDetailResponse>($"/api/v2/watch/{Uri.EscapeDataString(contentId)}", ct);

    public Task<PlaybackStartResponse> StartPlaybackAsync(PlaybackStartRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Playback API v2 requires a protocol-v3 playback plan.");

    public Task<PlaybackDecisionResponseV3> StartPlaybackV3Async(PlaybackStartRequestV3 request, CancellationToken ct = default)
        => StartV2Async(request, ct);

    public Task<PlaybackCapabilityV3> GetPlaybackCapabilityAsync(CancellationToken ct = default)
        => client.GetAsync<PlaybackCapabilityV3>("/api/v2/playback/capabilities", ct);

    public Task<PlaybackDecisionResponseV3> ReplanPlaybackV3Async(
        string sessionId,
        PlaybackReplanRequestV3 request,
        CancellationToken ct = default)
        => ReplanV2Async(sessionId, request, ct);

    public Task<PlaybackMutationReceipt> ReportProgressAsync(string sessionId, double position, bool isPaused, CancellationToken ct = default)
        => ReportProgressV2Async(sessionId, position, isPaused, ct);

    public Task<PlaybackMutationReceipt> StopPlaybackAsync(string sessionId, CancellationToken ct = default)
        => StopV2Async(sessionId, ct);
    public Task<TranscodeStartResponse> StartTranscodeAsync(TranscodeStartRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Playback API v2 changes delivery through a playback replan.");

    public Task<ChangeAudioResponse> ChangeAudioTrackAsync(string sessionId, int trackIndex, double position, CancellationToken ct = default)
        => throw new NotSupportedException("Playback API v2 changes audio through a playback replan.");

    public Task<SubtitleAiStatus> GetSubtitleAiStatusAsync(CancellationToken ct = default)
        => client.GetAsync<SubtitleAiStatus>("/api/v2/subtitles/ai/status", ct);

    public Task<SubtitleProviderStatus> GetSubtitleProviderStatusAsync(CancellationToken ct = default)
        => client.GetAsync<SubtitleProviderStatus>("/api/v2/subtitles/providers/status", ct);

    public async Task<bool> CanSearchSubtitlesAsync(CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        bool enabled;
        try
        {
            // Deliberately read afresh for each dialog; availability belongs to
            // the current server/profile, not the previous playback session.
            enabled = (await GetSubtitleProviderStatusAsync(ct))?.Enabled != false;
        }
        catch (Exception)
        {
            enabled = true;
        }
        ct.ThrowIfCancellationRequested();
        if (!client.IsCurrentContext(context))
            throw new OperationCanceledException("Subtitle provider context changed.", ct);
        return enabled;
    }

    public Task<SubtitleAiStartResponse> StartSubtitleAiAsync(SubtitleAiRequest request, CancellationToken ct = default)
        => client.PostAsync<SubtitleAiStartResponse>("/api/v2/subtitles/ai/translate", new Dictionary<string, object?>
        {
            ["media_file_id"] = request.MediaFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["kind"] = request.Kind ?? "translate",
            ["source_index"] = request.SourceIndex,
            ["source_language"] = request.SourceLanguage,
            ["target_language"] = request.TargetLanguage,
            ["session_id"] = request.SessionId,
            ["start_position"] = request.StartPosition,
        }, ct);

    public async Task<SubtitleAiJob> GetSubtitleAiJobAsync(long jobId, CancellationToken ct = default)
        => (await client.GetAsync<SubtitleAiStartResponse>($"/api/v2/subtitles/ai/jobs/{jobId}", ct).ConfigureAwait(false)).Job;

    public Task<SubtitleAiQuota> GetSubtitleAiQuotaAsync(CancellationToken ct = default)
        => client.GetAsync<SubtitleAiQuota>("/api/v2/subtitles/ai/quota", ct);

    // ===== Subtitle Preferences =====

    public Task SaveSubtitlePrefsAsync(string seriesId, string language, string mode, CancellationToken ct = default)
        => SaveSubtitlePrefsAsync(seriesId, new SubtitlePreferenceRequest
        {
            SubtitleLanguage = language,
            SubtitleMode = mode,
            SubtitleTrackIndex = mode == "off" ? -1 : 0,
        }, ct);

    public Task SaveSubtitlePrefsAsync(string seriesId, SubtitlePreferenceRequest request, CancellationToken ct = default)
    {
        // Use Dictionary<string,object?> so the .NET 8 trimmer doesn't strip the
        // anonymous-type properties (established gotcha in this codebase).
        var body = new Dictionary<string, object?>
        {
            ["subtitle_language"] = request.SubtitleLanguage ?? "",
            ["subtitle_track_index"] = request.SubtitleTrackIndex,
            ["subtitle_mode"] = request.SubtitleMode,
        };
        if (!string.IsNullOrEmpty(request.ExternalSubtitlePath))
            body["external_subtitle_path"] = request.ExternalSubtitlePath;
        if (request.TrackSignature != null)
        {
            body["track_signature"] = new Dictionary<string, object?>
            {
                ["source"] = request.TrackSignature.Source,
                ["language"] = request.TrackSignature.Language,
                ["codec"] = request.TrackSignature.Codec,
                ["label"] = request.TrackSignature.Label,
                ["forced"] = request.TrackSignature.Forced,
                ["hearing_impaired"] = request.TrackSignature.HearingImpaired,
            };
        }
        if (request.ShowForcedSubtitles.HasValue)
            body["show_forced_subtitles"] = request.ShowForcedSubtitles.Value;
        return client.PutNoContentAsync($"/api/v2/subtitle-prefs/{Uri.EscapeDataString(seriesId)}", body, ct);
    }

    public Task DeleteSubtitlePrefsAsync(string seriesId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/subtitle-prefs/{Uri.EscapeDataString(seriesId)}", ct);

    // ===== Home Dismissals =====

    public Task DismissContinueWatchingAsync(string itemId, string progressUpdatedAt, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/home/dismissals/continue_watching/{Uri.EscapeDataString(itemId)}",
            new Dictionary<string, object?> { ["progress_updated_at"] = progressUpdatedAt }, ct);

    public Task DismissNextUpAsync(string itemId, string seriesId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/home/dismissals/next_up/{Uri.EscapeDataString(itemId)}",
            new Dictionary<string, object?> { ["series_id"] = seriesId }, ct);

    // ===== Subtitles =====

    public Task<SubtitleListResponse> GetSubtitlesAsync(int mediaFileId, CancellationToken ct = default)
        => client.GetAsync<SubtitleListResponse>($"/api/v2/subtitles/{mediaFileId}", ct);

    public Task DeleteSubtitleAsync(long id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/subtitles/stored/{id}", ct);

    public Task<SubtitleSearchResponse> SearchSubtitlesAsync(int mediaFileId, string[] languages, CancellationToken ct = default)
        => client.PostAsync<SubtitleSearchResponse>("/api/v2/subtitles/search",
            new Dictionary<string, object?>
            {
                ["media_file_id"] = mediaFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["languages"] = languages,
            }, ct);

    public Task<SubtitleDownloadResponse> DownloadSubtitleAsync(int mediaFileId, SubtitleSearchResult result, CancellationToken ct = default)
        => client.PostAsync<SubtitleDownloadResponse>("/api/v2/subtitles/download",
            new Dictionary<string, object?>
            {
                ["media_file_id"] = mediaFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["provider"] = result.Provider,
                ["subtitle_id"] = result.SubtitleId,
                ["language"] = result.Language,
                ["release_name"] = result.ReleaseName,
                ["score"] = result.Score,
                ["hearing_impaired"] = result.HearingImpaired,
            }, ct);

    public Task<SubtitleDownloadResponse> UploadSubtitleAsync(
        int mediaFileId,
        string fileName,
        byte[] fileBytes,
        string contentType,
        string? language = null,
        bool languageOverride = false,
        string? releaseName = null,
        bool hearingImpaired = false,
        CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string?>
        {
            ["media_file_id"] = mediaFileId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["language"] = language,
            ["language_override"] = languageOverride ? "true" : null,
            ["release_name"] = releaseName,
            ["hearing_impaired"] = hearingImpaired ? "true" : null,
        };
        return client.PostMultipartAsync<SubtitleDownloadResponse>(
            "/api/v2/subtitles/upload",
            fields,
            "file",
            fileName,
            fileBytes,
            contentType,
            ct);
    }

    public Task<SubtitleLanguageDetection> DetectSubtitleLanguageAsync(
        string fileName,
        byte[] fileBytes,
        string contentType,
        string? fallbackLanguage = null,
        CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string?>
        {
            ["language"] = fallbackLanguage,
        };
        return client.PostMultipartAsync<SubtitleLanguageDetection>(
            "/api/v2/subtitles/detect-language",
            fields,
            "file",
            fileName,
            fileBytes,
            contentType,
            ct);
    }

}

public class SubtitleListResponse
{
    public List<SubtitleEntry> Subtitles { get; set; } = [];
}

public class SubtitleEntry
{
    public long Id { get; set; }
    public int MediaFileId { get; set; }
    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public double Score { get; set; }
    public bool HearingImpaired { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string Source { get; set; } = "";
    public bool Forced { get; set; }
}

public class SubtitleSearchResponse
{
    public List<SubtitleSearchResult> Results { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class SubtitleSearchResult
{
    public string Provider { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string SubtitleId { get; set; } = "";
    public string Language { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public string Format { get; set; } = "srt";
    public double Score { get; set; }
    public int Downloads { get; set; }
    public bool HearingImpaired { get; set; }
    public string? UploadDate { get; set; }
}

public class SubtitleDownloadResponse
{
    public SubtitleEntry? Subtitle { get; set; }
    public long Id { get; set; }
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
}

public class SubtitleLanguageDetection
{
    public string Language { get; set; } = "";
    public string Source { get; set; } = "";
}

public class ChangeAudioResponse
{
    public int AudioTrackIndex { get; set; }
    public string PlayMethod { get; set; } = "";
    public string StreamUrl { get; set; } = "";
    public string SwitchMode { get; set; } = "";
    public double? PlayerStartSeconds { get; set; }
    public double? StreamOriginSeconds { get; set; }
    public double? TimelineOffsetSeconds { get; set; }
    public bool? CanSeekAnywhere { get; set; }
    public PlaybackInfo? PlaybackInfo { get; set; }
}
