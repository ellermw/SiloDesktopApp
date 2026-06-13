namespace ContinuumPlayer.Core.Models.Playback;

public class PlaybackStartRequest
{
    public int FileId { get; set; }
    public string ProfileId { get; set; } = "";
    public string? PlayMethod { get; set; }
    public double? StartPosition { get; set; }
    public int? AudioTrackIndex { get; set; }
    public bool PreserveDirectAudioSelection { get; set; }
    public bool DisableProgressPersistence { get; set; }
    public List<string> CodecsVideo { get; set; } = ["h264", "hevc", "av1", "vp9"];
    public List<string> CodecsAudio { get; set; } = ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"];
    public List<string> Containers { get; set; } = ["mp4", "mkv"];
    public string MaxResolution { get; set; } = "2160p";
    public bool Hdr { get; set; } = true;
    public HdrCapabilityDetails? HdrDetails { get; set; }
    public AudioPassthroughCapabilities? AudioPassthrough { get; set; }
}

public class HdrCapabilityDetails
{
    public bool Hdr10 { get; set; }
    public bool Hlg { get; set; }
    public List<int> DolbyVisionProfiles { get; set; } = [];
}

public class AudioPassthroughCapabilities
{
    public List<string> PassthroughCodecs { get; set; } = [];
    public bool SpatializerEnabled { get; set; }
    public int MaxChannels { get; set; }
}
