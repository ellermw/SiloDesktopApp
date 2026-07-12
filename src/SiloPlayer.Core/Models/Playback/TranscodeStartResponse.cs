namespace SiloPlayer.Core.Models.Playback;

public class TranscodeStartResponse
{
    public string SessionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string ManifestUrl { get; set; } = "";
    public double? DurationSeconds { get; set; }
    public double PlayerStartSeconds { get; set; }
    public double StreamOriginSeconds { get; set; }
    public double TimelineOffsetSeconds { get; set; }
    public bool CanSeekAnywhere { get; set; }
    public int? SwitchedFileId { get; set; }
}
