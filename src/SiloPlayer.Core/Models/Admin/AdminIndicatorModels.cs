namespace SiloPlayer.Core.Models.Admin;

/// <summary>Scheduled task row from <c>GET /api/v2/admin/tasks?include_hidden=true</c>.</summary>
public sealed class AdminIndicatorTask
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>idle | running | cancelling</summary>
    public string State { get; set; } = "";
    public double Progress { get; set; }
}

/// <summary>A scan run from the <c>scans</c> realtime channel (no REST list exists).</summary>
public sealed record AdminIndicatorScanRun(
    string Id,
    string LibraryId,
    string Mode,
    string? Path,
    string Status,
    AdminIndicatorScanResult? Result);

public sealed record AdminIndicatorScanResult(long? TotalFiles, long? FilesProcessed, string? Message);

/// <summary>Minimal library identity the indicator needs to name a scan.</summary>
public sealed class AdminIndicatorLibrary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

/// <summary><c>AdminDownloadPreparationsOutputBody</c>.</summary>
public sealed class AdminIndicatorPreparationList
{
    public AdminIndicatorPreparationCounts Counts { get; set; } = new();
    public List<AdminIndicatorPreparation> Items { get; set; } = [];
}

public sealed class AdminIndicatorPreparationCounts
{
    public long Running { get; set; }
    public long Queued { get; set; }
    public long Retrying { get; set; }
    public long Paused { get; set; }
    public long FailedRecent { get; set; }
}

public sealed class AdminIndicatorPreparation
{
    public string Id { get; set; } = "";
    /// <summary>running | queued | retrying | paused | failed</summary>
    public string State { get; set; } = "";
    /// <summary>remux | transcode</summary>
    public string Format { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? SeriesName { get; set; }
    public string? EpisodeName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? FailedAt { get; set; }
    public AdminIndicatorPreparationOutput Output { get; set; } = new();
    public AdminIndicatorPreparationProgress? Progress { get; set; }
    public bool ProgressUnavailable { get; set; }
    public AdminIndicatorPreparationWorker? Worker { get; set; }
}

public sealed class AdminIndicatorPreparationOutput
{
    public string Container { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public string? Resolution { get; set; }
    public long? BitrateKbps { get; set; }
    public string? ToneMapMode { get; set; }
}

public sealed class AdminIndicatorPreparationProgress
{
    public double EncodedSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public double Speed { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public sealed class AdminIndicatorPreparationWorker
{
    /// <summary>server | node</summary>
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NodeId { get; set; }
}

/// <summary><c>AdminIndicatorBuildInfo</c>, shown in the admin sidebar build card.</summary>
public sealed class AdminIndicatorBuildInfo
{
    public string Display { get; set; } = "";
    public string Revision { get; set; } = "";
    public bool Dirty { get; set; }
    public long BuildNumber { get; set; }
    public bool Available { get; set; }
    public string? BuiltAt { get; set; }
}

/// <summary>The part of <c>AdminIndicatorServerStatus</c> the admin shell's restart banner reads.</summary>
public sealed class AdminIndicatorServerStatus
{
    public bool RestartRequired { get; set; }
    public long RestartMarkCount { get; set; }
    public bool RestartRequested { get; set; }
}
