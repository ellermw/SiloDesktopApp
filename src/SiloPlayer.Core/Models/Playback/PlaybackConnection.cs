namespace SiloPlayer.Core.Models.Playback;

public sealed class PlaybackConnectionState
{
    public PlaybackConnectionProbe? Probe { get; set; }
    public PlaybackConnectionSample? PlaybackSample { get; set; }
    public int? AutoBudgetKbps { get; set; }
    public string Connection { get; set; } = "";
    public PlaybackLearnedLimit? LearnedLimit { get; set; }
}
public class PlaybackConnectionSample
{
    public int Kbps { get; set; }
    public DateTimeOffset MeasuredAt { get; set; }
}
public sealed class PlaybackConnectionProbe : PlaybackConnectionSample
{
    public bool LowerBound { get; set; }
    public string Transport { get; set; } = "other";
}
public sealed class PlaybackLearnedLimit
{
    public int Kbps { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class PlaybackBandwidthLimit
{
    public int Kbps { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed record PlaybackBufferingReplan(string QualityPreference, int? BandwidthEstimateKbps = null,
    string? BandwidthEstimateSource = null, int? ObservedThroughputKbps = null, string? LearnedLimit = null);

public sealed class PlaybackProbeResult
{
    public int Kbps { get; set; }
    public long Bytes { get; set; }
    public int DurationMs { get; set; }
    public bool LowerBound { get; set; }
    public bool Manual { get; set; }
    public string Transport { get; set; } = "other";
}
