using System.Text.Json;

namespace SiloPlayer.Core.Models.Admin;

public sealed class DiagnosticStatus
{
    public string Status { get; set; } = "";
    public string ServerInstanceId { get; set; } = "";
    public List<int> AcceptedSchemaVersions { get; set; } = [];
    public long MaxBundleBytes { get; set; }
    public long MaxManifestBytes { get; set; }
    public int RetentionDays { get; set; }
    public int ConsentNoticeVersion { get; set; }
}

public class DiagnosticReportSummary
{
    public string Id { get; set; } = "";
    public string ShortId { get; set; } = "";
    public int UserId { get; set; }
    public string? ProfileId { get; set; }
    public string State { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public string ReceivedAt { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string Platform { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string AppBuild { get; set; } = "";
    public string? CrashSummary { get; set; }
    public List<string> PlaybackSessionIds { get; set; } = [];
    public string? BlobBucket { get; set; }
    public string? BlobKey { get; set; }
    public long? BlobBytes { get; set; }
    public long? UncompressedBytes { get; set; }
    public string? BlobSha256 { get; set; }
}

public sealed class DiagnosticReport : DiagnosticReportSummary
{
    public JsonElement Manifest { get; set; }
}

public sealed class DiagnosticReportListResponse
{
    public List<DiagnosticReportSummary> Reports { get; set; } = [];
    public string? NextCursor { get; set; }
}
