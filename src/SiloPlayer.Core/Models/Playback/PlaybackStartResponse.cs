namespace SiloPlayer.Core.Models.Playback;

public class PlaybackStartResponse
{
    public int ProtocolVersion { get; set; }
    public string? PlaybackAttemptId { get; set; }
    public string? PlanId { get; set; }
    public string? PlanAttemptKey { get; set; }
    public string? Delivery { get; set; }
    public string SessionId { get; set; } = "";
    public int MediaFileId { get; set; }
    public string PlayMethod { get; set; } = "";
    public double Position { get; set; }
    public bool IsPaused { get; set; }
    public string StreamUrl { get; set; } = "";
    public int AudioTrackIndex { get; set; }
    public double? DurationSeconds { get; set; }
    public List<SubtitleTrackInfo> SubtitleUrls { get; set; } = [];
    public PlaybackInfo? PlaybackInfo { get; set; }
    public double StreamOriginSeconds { get; set; }
    public double PlayerStartSeconds { get; set; }
    public double TimelineOffsetSeconds { get; set; }
    public bool CanSeekAnywhere { get; set; } = true;
    public string? SelectedSubtitleTrackId { get; set; }
    public string SubtitleMode { get; set; } = "off";
    public string? SelectedSubtitleArtifactUrl { get; set; }
    public double SubtitleTimingOriginSeconds { get; set; }
    public string ActiveQuality { get; set; } = "original";
    public List<PlaybackQualityV3> AvailableQualities { get; set; } = [];
}

public class SubtitleTrackInfo
{
    public string? TrackId { get; set; }
    public int? Id { get; set; }
    public int Index { get; set; }
    public int MediaFileId { get; set; }
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string Label { get; set; } = "";
    public string? Source { get; set; }
    public string Url { get; set; } = "";
    public string? Delivery { get; set; }
    public string? FontBundleUrl { get; set; }
    public bool Forced { get; set; }
    public bool HearingImpaired { get; set; }
}

/// <summary>
/// Body for <c>PUT /api/v1/subtitle-prefs/{series_id}</c>. Mirrors upstream
/// <c>setSubtitlePrefRequest</c> in <c>internal/api/handlers/subtitle_prefs.go</c>.
/// </summary>
public class SubtitlePreferenceRequest
{
    public string SubtitleLanguage { get; set; } = "";
    public int SubtitleTrackIndex { get; set; } = -1;
    public string? ExternalSubtitlePath { get; set; }
    public string SubtitleMode { get; set; } = "auto"; // "off" | "auto" | "always"
    public SubtitleTrackSignature? TrackSignature { get; set; }
    public bool? ShowForcedSubtitles { get; set; }
}

/// <summary>
/// Identifies a specific subtitle track for cross-version matching on the
/// server side (e.g. when a user's preferred track needs to be reapplied
/// after a remux or transcode swap).
/// </summary>
public class SubtitleTrackSignature
{
    public string Source { get; set; } = ""; // "embedded" | "external" | "downloaded"
    public string? Language { get; set; }
    public string? Codec { get; set; }
    public string? Label { get; set; }
    public bool Forced { get; set; }
    public bool HearingImpaired { get; set; }
}

public class PlaybackInfo
{
    // Server encoder target, in kbps; not a measurement or source-file bitrate.
    public int? TargetVideoBitrateKbps { get; set; }
    public string StreamType { get; set; } = "";
    public bool TranscodeAudio { get; set; }
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
}
