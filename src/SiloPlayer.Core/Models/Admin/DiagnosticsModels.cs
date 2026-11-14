using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Models.Admin;

public sealed record DiagnosticsCapabilities
{
    public string Revision { get; init; } = "";
    public string State { get; init; } = "";
    public bool Allowed { get; init; }
    public string Status { get; init; } = "";
    public string ServerInstanceId { get; init; } = "";
    public long RetentionDays { get; init; }
    public long MaxBundleBytes { get; init; }
    public long MaxManifestBytes { get; init; }
    public long UploadChunkBytes { get; init; }
    public long ConsentNoticeVersion { get; init; }
    public long[] AcceptedSchemaVersions { get; init; } = [];
    public string[] AcceptedCrashSources { get; init; } = [];
}

public record DiagnosticsReportSummary
{
    public string Id { get; init; } = "";
    public string ShortId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string? ProfileId { get; init; }
    public string State { get; init; } = "";
    public DateTimeOffset CapturedAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public string ReportType { get; init; } = "";
    public string Platform { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public string AppBuild { get; init; } = "";
    public string? CrashSummary { get; init; }
    public string[] PlaybackSessionIds { get; init; } = [];
    public long? BlobBytes { get; init; }
    public long? UncompressedBytes { get; init; }
    public string? BlobSha256 { get; init; }
}

public sealed record DiagnosticsReportDetail : DiagnosticsReportSummary
{
    public JsonElement Manifest { get; init; }
}

public sealed record DiagnosticsQuery
{
    public string? UserId { get; init; }
    public string? Platform { get; init; }
    public string? ReportType { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? ShortId { get; init; }
    public bool HasFilters => new[] { UserId, Platform, ReportType, From, To, ShortId }.Any(value => !string.IsNullOrWhiteSpace(value) && value != "all");

    public DiagnosticsQuery Apply()
    {
        var user = UserId?.Trim();
        if (!string.IsNullOrEmpty(user) && (user[0] == '0' || user.Any(character => !char.IsAsciiDigit(character))))
            throw new ArgumentException("Enter a valid user ID.");
        return this with { UserId = user, Platform = Filter(Platform), ReportType = Filter(ReportType), From = Date(From), To = Date(To), ShortId = ShortId?.Trim() };
    }

    public string ToQuery(string? cursor = null)
    {
        var query = new LogsQueryBuilder();
        query.Add("user_id", UserId);
        query.Add("platform", Filter(Platform));
        query.Add("report_type", Filter(ReportType));
        query.Add("from", From);
        query.Add("to", To);
        query.Add("short_id", ShortId);
        query.Add("limit", "25");
        query.Add("cursor", cursor);
        return query.ToString();
    }

    private static string? Filter(string? value) => value?.Trim() is "all" or "" ? null : value?.Trim();
    private static string? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
            throw new ArgumentException("Enter a valid date and time.");
        return date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
