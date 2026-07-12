namespace SiloPlayer.Core.Models.Admin;

public class AdminJob
{
    public string Id { get; set; } = "";
    public string JobType { get; set; } = "";
    public string Status { get; set; } = "";
    public int CreatedByUserId { get; set; }
    public Dictionary<string, object>? RequestPayload { get; set; }
    public Dictionary<string, object>? ResultPayload { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public int ProgressCurrent { get; set; }
    public int ProgressTotal { get; set; }
    public long ArtifactSizeBytes { get; set; }
    public string? PublicUrl { get; set; }
    public string RequestedAt { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? HeartbeatAt { get; set; }
    public string? ExpiresAt { get; set; }
    public string? PublishedAt { get; set; }
    public string? DownloadUrl { get; set; }
    public string? DownloadExpiresAt { get; set; }
}

public class AdminJobsResponse
{
    public List<AdminJob> Jobs { get; set; } = [];
}
