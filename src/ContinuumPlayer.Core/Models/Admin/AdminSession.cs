namespace ContinuumPlayer.Core.Models.Admin;

public class AdminSession
{
    public string SessionId { get; set; } = "";
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string? ProfileName { get; set; }
    public int MediaFileId { get; set; }
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? SeriesName { get; set; }
    public string? EpisodeName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? PosterUrl { get; set; }
    public string PlayMethod { get; set; } = "";
    public string ReportingNode { get; set; } = "";
    public string? NodeDisplayName { get; set; }
    public double? FileDuration { get; set; }
    public string StartedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public bool IsPaused { get; set; }
    public bool HasPlaybackControl { get; set; }
    public string? ClientIp { get; set; }
    public int AudioTrackIndex { get; set; }
    public bool TranscodeAudio { get; set; }
    public int? StreamBitrateKbps { get; set; }
    public string? TargetResolution { get; set; }
    public string? TargetVideoCodec { get; set; }
    public string? TargetAudioCodec { get; set; }
    public int? TargetBitrateKbps { get; set; }
    public string? SourceContainer { get; set; }
    public int? SourceBitrateKbps { get; set; }
    public string? SourceVideoCodec { get; set; }
    public string? SourceVideoResolution { get; set; }
    public string? SourceAudioCodec { get; set; }
    public int? SourceAudioChannels { get; set; }
    public string? SourceAudioLanguage { get; set; }
    public string? SourceAudioTitle { get; set; }
    public string? SourceAudioLayout { get; set; }
    public string? VideoDecision { get; set; }
    public string? AudioDecision { get; set; }
    public string? ContentId { get; set; }
    public int RequestedMediaFileId { get; set; }
    public string? RequestedVideoCodec { get; set; }
    public string? RequestedVideoResolution { get; set; }
}
