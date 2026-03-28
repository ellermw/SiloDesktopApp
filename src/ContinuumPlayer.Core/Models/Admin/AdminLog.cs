namespace ContinuumPlayer.Core.Models.Admin;

public class OperationalLogEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Component { get; set; } = "";
    public string Message { get; set; } = "";
    public string? RequestId { get; set; }
    public int? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? PlaybackSessionId { get; set; }
    public string? ClientIp { get; set; }
    public string? NodeId { get; set; }
    public Dictionary<string, object>? Attrs { get; set; }
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string ClientIp { get; set; } = "";
    public int? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? PlaybackSessionId { get; set; }
    public string? RequestId { get; set; }
    public string? NodeId { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public double DurationMs { get; set; }
}

public class OperationalLogListResponse
{
    public List<OperationalLogEntry> Entries { get; set; } = [];
    public string? NextCursor { get; set; }
}

public class AuditLogListResponse
{
    public List<AuditLogEntry> Entries { get; set; } = [];
    public string? NextCursor { get; set; }
}
