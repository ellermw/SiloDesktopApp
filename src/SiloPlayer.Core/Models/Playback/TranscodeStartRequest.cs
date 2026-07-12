namespace SiloPlayer.Core.Models.Playback;

public class TranscodeStartRequest
{
    public string SessionId { get; set; } = "";
    public double SeekSeconds { get; set; }
    public string TargetResolution { get; set; } = "1080p";
    public string TargetCodecVideo { get; set; } = "h264";
    public string TargetCodecAudio { get; set; } = "aac";
    public int TargetBitrateKbps { get; set; } = 8000;
    public int SegmentDuration { get; set; } = 2;
    public int SubtitleTrackIndex { get; set; } = -1;
    public int SubtitleMediaFileId { get; set; }
    public bool SubtitleBurnIn { get; set; }
}
