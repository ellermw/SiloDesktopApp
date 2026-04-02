namespace ContinuumPlayer.Core.Models.HistoryImport;

public class HistoryImportRun
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string ConnectionMode { get; set; } = "";
    public string Status { get; set; } = "";
    public int Fetched { get; set; }
    public int Matched { get; set; }
    public int Unmatched { get; set; }
    public int ProgressUpdated { get; set; }
    public int HistoryCreated { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<HistoryImportUnmatchedSample> UnmatchedSamples { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
}

public class HistoryImportRunsResponse
{
    public List<HistoryImportRun> Runs { get; set; } = [];
}
