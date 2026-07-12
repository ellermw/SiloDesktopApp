namespace SiloPlayer.Core.Models.Admin;

public sealed class MarkerHistoryResponse
{
    public List<MarkerHistoryEntry> History { get; set; } = [];
}

public sealed class MarkerHistoryEntry
{
    public long Id { get; set; }
    public int MediaFileId { get; set; }
    public string? ItemId { get; set; }
    public string? ItemType { get; set; }
    public string? MediaTitle { get; set; }
    public string? FilePath { get; set; }
    public string Segment { get; set; } = "";
    public string Action { get; set; } = "";
    public MarkerHistorySegment? Before { get; set; }
    public MarkerHistorySegment? After { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public int? ImpersonatorUserId { get; set; }
    public string? ImpersonatorUsername { get; set; }
    public int? ApiKeyId { get; set; }
    public string? RequestId { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MarkerHistorySegment
{
    public double? Start { get; set; }
    public double? End { get; set; }
    public string? Source { get; set; }
    public string? Provider { get; set; }
    public double? Confidence { get; set; }
    public string? Algorithm { get; set; }
    public DateTimeOffset? DetectedAt { get; set; }
}
