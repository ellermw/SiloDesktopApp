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
            new Dictionary<string, object?>
            {
                ["position"] = position,
                ["is_paused"] = isPaused,
            }, ct);

    public Task StopPlaybackAsync(string sessionId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/playback/{sessionId}", ct);

    public Task<TranscodeStartResponse> StartTranscodeAsync(TranscodeStartRequest request, CancellationToken ct = default)
        => client.PostAsync<TranscodeStartResponse>("/api/v1/playback/transcode/start", request, ct);

    public Task<ChangeAudioResponse> ChangeAudioTrackAsync(string sessionId, int trackIndex, double position, CancellationToken ct = default)
        => client.PatchAsync<ChangeAudioResponse>($"/api/v1/playback/{sessionId}/audio",
            new Dictionary<string, object?>
            {
                ["audio_track_index"] = trackIndex,
                ["position"] = position,
            }, ct);

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
        return client.PutNoContentAsync($"/api/v1/subtitle-prefs/{Uri.EscapeDataString(seriesId)}", body, ct);
    }

    // ===== Home Dismissals =====

    public Task DismissContinueWatchingAsync(string itemId, string progressUpdatedAt, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/home/dismissals/continue_watching/{Uri.EscapeDataString(itemId)}",
            new Dictionary<string, object?> { ["progress_updated_at"] = progressUpdatedAt }, ct);

    public Task DismissNextUpAsync(string itemId, string seriesId, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/home/dismissals/next_up/{Uri.EscapeDataString(itemId)}",
            new Dictionary<string, object?> { ["series_id"] = seriesId }, ct);

    // ===== Subtitles =====

    public Task<SubtitleListResponse> GetSubtitlesAsync(int mediaFileId, CancellationToken ct = default)
        => client.GetAsync<SubtitleListResponse>($"/api/v1/subtitles/{mediaFileId}", ct);

    public Task DeleteSubtitleAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/subtitles/{id}", ct);

    public Task<SubtitleSearchResponse> SearchSubtitlesAsync(int mediaFileId, string[] languages, CancellationToken ct = default)
        => client.PostAsync<SubtitleSearchResponse>("/api/v1/subtitles/search",
            new Dictionary<string, object?>
            {
                ["media_file_id"] = mediaFileId,
                ["languages"] = languages,
            }, ct);

    public Task<SubtitleDownloadResponse> DownloadSubtitleAsync(int mediaFileId, string provider, string subtitleId, string language, string format, CancellationToken ct = default)
        => client.PostAsync<SubtitleDownloadResponse>("/api/v1/subtitles/download",
            new Dictionary<string, object?>
            {
                ["media_file_id"] = mediaFileId,
                ["provider"] = provider,
                ["subtitle_id"] = subtitleId,
                ["language"] = language,
                ["format"] = format,
            }, ct);

    // ===== Watch Together (Watch Party) =====
    // Thin wrappers around /api/v1/watch-together/*. Mirrors webui's lib/watchTogether.ts.
    // The room_token is an opaque JWT returned by create/join; for suggestion endpoints it
    // travels as a query param (not the session token). Room write endpoints (selection,
    // policy, close) are profile-scoped and authenticated via the usual Bearer header.

    public Task<WatchTogetherRoomResponse> CreateWatchTogetherRoomAsync(
        string selectionMode = "host_pick",
        int? fileId = null,
        int? libraryId = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["selection_mode"] = selectionMode,
        };
        if (fileId.HasValue && libraryId.HasValue)
        {
            body["file_id"] = fileId.Value;
            body["library_id"] = libraryId.Value;
        }
        return client.PostAsync<WatchTogetherRoomResponse>("/api/v1/watch-together/rooms", body, ct);
    }

    public Task<WatchTogetherRoomResponse> JoinWatchTogetherRoomAsync(string? code, string? joinToken, CancellationToken ct = default)
    {
        var body = !string.IsNullOrEmpty(joinToken)
            ? new Dictionary<string, object?> { ["join_token"] = joinToken! }
            : new Dictionary<string, object?> { ["code"] = code ?? "" };
        return client.PostAsync<WatchTogetherRoomResponse>("/api/v1/watch-together/join", body, ct);
    }

    public Task<WatchTogetherRoomResponse> GetWatchTogetherRoomAsync(string roomId, string roomToken, CancellationToken ct = default)
        => client.GetAsync<WatchTogetherRoomResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}?room_token={Uri.EscapeDataString(roomToken)}", ct);

    public Task<WatchTogetherRoomResponse> UpdateWatchTogetherRoomPolicyAsync(string roomId, string guestControlPolicy, CancellationToken ct = default)
        => client.PatchAsync<WatchTogetherRoomResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/policy",
            new Dictionary<string, object?> { ["guest_control_policy"] = guestControlPolicy }, ct);

    public Task<WatchTogetherRoomResponse> SelectWatchTogetherRoomItemAsync(
        string roomId, string contentId, int? fileId = null, int? libraryId = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content_id"] = contentId,
        };
        if (fileId.HasValue)
            body["file_id"] = fileId.Value;
        if (libraryId.HasValue)
            body["library_id"] = libraryId.Value;

        return client.PutAsync<WatchTogetherRoomResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/selection", body, ct);
    }

    public Task CloseWatchTogetherRoomAsync(string roomId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}", ct);

    public Task<WatchTogetherSuggestionsResponse> ListWatchTogetherSuggestionsAsync(string roomId, string roomToken, CancellationToken ct = default)
        => client.GetAsync<WatchTogetherSuggestionsResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions?room_token={Uri.EscapeDataString(roomToken)}", ct);

    public Task<WatchTogetherSuggestionsResponse> CreateWatchTogetherSuggestionAsync(
        string roomId, string roomToken,
        string contentId, string contentType, string title,
        string? subtitle = null, string? posterUrl = null, string? note = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content_id"] = contentId,
            ["content_type"] = contentType,
            ["title"] = title,
            ["subtitle"] = subtitle ?? "",
            ["poster_url"] = posterUrl ?? "",
            ["note"] = note ?? "",
        };
        return client.PostAsync<WatchTogetherSuggestionsResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions?room_token={Uri.EscapeDataString(roomToken)}",
            body, ct);
    }

    public Task<WatchTogetherSuggestionsResponse> DeleteWatchTogetherSuggestionAsync(
        string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
    {
        // server returns the updated suggestions list, so we can't use DeleteAsync (no return).
        // Use a manual Patch-style call isn't available either — fall back to PutAsync with a
        // pseudo body? No — the proper approach is to keep a distinct delete-with-response helper.
        // Simpler: fire the DELETE, then re-fetch the list.
        return DeleteAndRefetchSuggestionsAsync(roomId, roomToken, suggestionId, ct);
    }

    private async Task<WatchTogetherSuggestionsResponse> DeleteAndRefetchSuggestionsAsync(
        string roomId, string roomToken, string suggestionId, CancellationToken ct)
    {
        await client.DeleteAsync(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions/{Uri.EscapeDataString(suggestionId)}?room_token={Uri.EscapeDataString(roomToken)}", ct);
        return await ListWatchTogetherSuggestionsAsync(roomId, roomToken, ct);
    }

    public Task<WatchTogetherSuggestionsResponse> VoteWatchTogetherSuggestionAsync(
        string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => client.PostAsync<WatchTogetherSuggestionsResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions/{Uri.EscapeDataString(suggestionId)}/vote?room_token={Uri.EscapeDataString(roomToken)}",
            new Dictionary<string, object?>(), ct);

    public async Task<WatchTogetherSuggestionsResponse> UnvoteWatchTogetherSuggestionAsync(
        string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
    {
        await client.DeleteAsync(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions/{Uri.EscapeDataString(suggestionId)}/vote?room_token={Uri.EscapeDataString(roomToken)}", ct);
        return await ListWatchTogetherSuggestionsAsync(roomId, roomToken, ct);
    }

    public Task<WatchTogetherRoomResponse> PromoteWatchTogetherSuggestionAsync(
        string roomId, string roomToken, string suggestionId, CancellationToken ct = default)
        => client.PostAsync<WatchTogetherRoomResponse>(
            $"/api/v1/watch-together/rooms/{Uri.EscapeDataString(roomId)}/suggestions/promote?room_token={Uri.EscapeDataString(roomToken)}",
            new Dictionary<string, object?> { ["suggestion_id"] = suggestionId }, ct);
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
