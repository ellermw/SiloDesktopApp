namespace ContinuumPlayer.Core.Models.Admin;

public class ScanRequest
{
    public int? LibraryId { get; set; }
    public string? Path { get; set; }
}

public class ScanResponse
{
    public string Status { get; set; } = "";
    public string Mode { get; set; } = "";
    public int LibraryId { get; set; }
}

public class CreateLibraryRequest
{
    public List<string> Paths { get; set; } = [];
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool? Enabled { get; set; }
    public bool? IntroDetectionEnabled { get; set; }
}

public class UpdateLibraryRequest
{
    public List<string>? Paths { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public bool? IntroDetectionEnabled { get; set; }
}

/// <summary>
/// Active scan run streamed over the <c>scans</c> event channel. No REST endpoint
/// backs this — snapshot arrives on subscribe, then live events as state changes.
/// Shape mirrors continuum-server/internal/events/scan_registry.go ScanRun.
/// </summary>
public class AdminScanRun
{
    public string Id { get; set; } = "";
    public int LibraryId { get; set; }
    public string Mode { get; set; } = ""; // "library" | "subtree" | "file"
    public string? Path { get; set; }
    public string Trigger { get; set; } = "";
    public string Status { get; set; } = ""; // "accepted" | "running" | "completed" | "failed" | "cancelled"
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public AdminScanResult? Result { get; set; }
}

public class AdminScanResult
{
    public int New { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Missing { get; set; }
    public int FilesDeleted { get; set; }
    public int MembershipsRemoved { get; set; }
    public int ItemsDeleted { get; set; }
    public int MatchedFiles { get; set; }
    public int RetriedItems { get; set; }
    public int StillUnmatchedWarnings { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Phase { get; set; }
    public string? Message { get; set; }
    public string? CurrentScope { get; set; }
    public int TotalFiles { get; set; }
    public int FilesDiscovered { get; set; }
    public int FilesProcessed { get; set; }
}
