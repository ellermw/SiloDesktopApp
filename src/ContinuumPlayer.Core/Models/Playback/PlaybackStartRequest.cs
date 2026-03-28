namespace ContinuumPlayer.Core.Models.Playback;

public class PlaybackStartRequest
{
    public int FileId { get; set; }
    public string ProfileId { get; set; } = "";
    public double? StartPosition { get; set; }
    public int? AudioTrackIndex { get; set; }
    public List<string> CodecsVideo { get; set; } = ["h264", "hevc", "av1", "vp9"];
    public List<string> CodecsAudio { get; set; } = ["aac", "flac", "opus", "eac3", "ac3", "dts", "truehd"];
    public List<string> Containers { get; set; } = ["mp4", "mkv"];
    public string MaxResolution { get; set; } = "2160p";
    public bool Hdr { get; set; } = true;
}
