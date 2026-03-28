namespace ContinuumPlayer.Core.Models.Playback;

public class WatchDetailResponse
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public List<FileVersion> Versions { get; set; } = [];
    public List<SubtitleInfo> Subtitles { get; set; } = [];
    public WatchUserData? UserData { get; set; }
    public TimeRange? Intro { get; set; }
    public TimeRange? Credits { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EffectiveSubtitleLanguage { get; set; }
    public string? EffectiveSubtitleMode { get; set; }
    public bool? EffectiveShowForcedSubtitles { get; set; }
}

public class FileVersion
{
    public int FileId { get; set; }
    public string? FileName { get; set; }
    public string Resolution { get; set; } = "";
    public string CodecVideo { get; set; } = "";
    public string CodecAudio { get; set; } = "";
    public bool Hdr { get; set; }
    public string Container { get; set; } = "";
    public long FileSize { get; set; }
    public double Duration { get; set; }
    public int Bitrate { get; set; }
    public int? AudioChannels { get; set; }
    public List<AudioTrackInfo>? AudioTracks { get; set; }
}

public class AudioTrackInfo
{
    public string? Title { get; set; }
    public string? EmbeddedTitle { get; set; }
    public string? Language { get; set; }
    public string? Codec { get; set; }
    public string? Layout { get; set; }
    public int? Channels { get; set; }
    public bool Default { get; set; }
}

public class SubtitleInfo
{
    public string Language { get; set; } = "";
    public string? Codec { get; set; }
    public string? Title { get; set; }
    public string Source { get; set; } = "";
    public bool Forced { get; set; }
}

public class WatchUserData
{
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool Played { get; set; }
    public int? LastFileId { get; set; }
    public string? LastResolution { get; set; }
    public bool? LastHdr { get; set; }
    public string? LastCodecVideo { get; set; }
}

public class TimeRange
{
    public double Start { get; set; }
    public double End { get; set; }
}
