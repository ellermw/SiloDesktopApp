namespace SiloPlayer.Core.Models.Playback;

public sealed record SubtitleTiming
{
    public int OffsetMs { get; init; }
    public double Scale { get; init; } = 1;
    public bool IsIdentity => OffsetMs == 0 && Math.Abs(Scale - 1) < 0.000001;
}

public sealed class SubtitleSyncJob
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string? Phase { get; set; }
    public double? Progress { get; set; }
    public string? Failure { get; set; }
    public double? Confidence { get; set; }
    public SubtitleTiming? Result { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class SubtitleSyncState
{
    public string Key { get; set; } = "";
    public int MediaFileId { get; set; }
    public string Source { get; set; } = "";
    public long? StoredSubtitleId { get; set; }
    public string Language { get; set; } = "";
    public string Format { get; set; } = "";
    public string Label { get; set; } = "";
    public SubtitleTiming Timing { get; set; } = new();
    public SubtitleSyncJob? Sync { get; set; }
}

public sealed class SubtitleSyncInventory { public List<SubtitleSyncState> Subtitles { get; set; } = []; }
public sealed class SubtitleSyncResponse { public SubtitleSyncState Subtitle { get; set; } = new(); }
public sealed class SubtitleSyncCapability
{
    public string State { get; set; } = "";
    public bool AutoSync { get; set; }
    public bool External { get; set; }
}
