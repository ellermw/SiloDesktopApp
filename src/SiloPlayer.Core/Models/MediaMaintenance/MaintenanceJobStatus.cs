using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.MediaMaintenance;

/// <summary>
/// The parts of <c>GET /api/v2/admin/jobs/{id}</c> (AdminTaskJob) that following
/// an item metadata refresh needs. Other result shapes are ignored.
/// </summary>
public sealed class MaintenanceJobStatus
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>queued, running, canceling, succeeded, failed or canceled.</summary>
    public string State { get; set; } = "";
    public bool Terminal { get; set; }
    public MaintenanceJobFailure? Failure { get; set; }
    public MaintenanceJobItemResult? ItemResult { get; set; }

    [JsonIgnore] public bool Succeeded => State == "succeeded";
    [JsonIgnore] public bool Canceled => State == "canceled";
    // A terminal job in a state this client does not know did not succeed.
    [JsonIgnore] public bool Failed => State == "failed" || (Terminal && !Succeeded && !Canceled);
    [JsonIgnore] public bool IsFinished => Succeeded || Canceled || Failed;
}

public sealed class MaintenanceJobFailure
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public bool Retryable { get; set; }
}

/// <summary>An item refresh job's result (AdminTaskJobItemResult).</summary>
public sealed class MaintenanceJobItemResult
{
    public string? RequestedContentId { get; set; }
    public string? RefreshContentId { get; set; }
    /// <summary>Set when the refresh rebuilt the item under another detail ID.</summary>
    public string? DetailContentId { get; set; }
    public long NewFiles { get; set; }
    public long MatchedFiles { get; set; }
    public bool ArtworkCacheIncomplete { get; set; }
}

/// <summary>One job read and the server's <c>Retry-After</c> poll hint, if it sent one.</summary>
public sealed record MaintenanceJobRead(MaintenanceJobStatus Job, double? RetryAfterSeconds);
