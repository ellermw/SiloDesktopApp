namespace ContinuumPlayer.Core.Models.Playback;

public class PlaybackStartResponse
{
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
}

public class SubtitleTrackInfo
{
    public int Index { get; set; }
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string Label { get; set; } = "";
    public string? Source { get; set; }
    public string Url { get; set; } = "";
    public bool Forced { get; set; }
}

public class PlaybackInfo
{
    public string StreamType { get; set; } = "";
    public bool TranscodeAudio { get; set; }
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
}
