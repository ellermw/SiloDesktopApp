namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// Wire shape of <c>GET /api/v2/admin/markers/history</c> (contract
/// <c>AdminMarkerHistory</c>, official Silo e61187daaf72e8accad486eb2f632c753ae211d5):
/// bounded recent marker edits, newest first; empty, never null.
/// </summary>
public sealed class AdminMarkerHistoryWire
{
    public List<AdminMarkerEditWire>? History { get; set; }
}

/// <summary>Wire shape of one <c>AdminMarkerEdit</c>. Optional fields are omitted by the server when absent.</summary>
public sealed class AdminMarkerEditWire
{
    public string? Id { get; set; }
    public string? MediaFileId { get; set; }
    public string? ItemId { get; set; }
    public string? ItemType { get; set; }
    public string? MediaTitle { get; set; }
    public string? FilePath { get; set; }
    public string? Segment { get; set; }
    public string? Action { get; set; }
    public AdminMarkerSegmentWire? Before { get; set; }
    public AdminMarkerSegmentWire? After { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? ImpersonatorUserId { get; set; }
    public string? ImpersonatorUsername { get; set; }
    public string? ApiKeyId { get; set; }
    public string? RequestId { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? CreatedAt { get; set; }
}

/// <summary>Wire shape of a v2 <c>MarkerSegment</c>.</summary>
public sealed class AdminMarkerSegmentWire
{
    public double? StartSeconds { get; set; }
    public double? EndSeconds { get; set; }
    public string? Source { get; set; }
    public string? Provider { get; set; }
    public double? Confidence { get; set; }
    public string? Algorithm { get; set; }
    public string? DetectedAt { get; set; }
}

/// <summary>A marker segment as recorded before or after an edit (WebUI <c>markerSegmentFromV2</c>).</summary>
public sealed record AdminMarkerSegment(double? Start, double? End, string? Source, string? Provider, double? Confidence, string? Algorithm, string? DetectedAt);

/// <summary>
/// One recorded marker edit. Read-only audit data: <see cref="Segment"/> keeps
/// the server's value (an unknown kind is shown as sent), <see cref="Before"/>
/// is null when no marker existed, <see cref="After"/> null when it was cleared.
/// </summary>
public sealed record AdminMarkerEditRow(
    string Id,
    string MediaFileId,
    string? ItemId,
    string? ItemType,
    string? MediaTitle,
    string? FilePath,
    string Segment,
    string Action,
    AdminMarkerSegment? Before,
    AdminMarkerSegment? After,
    string? UserId,
    string? Username,
    string? ImpersonatorUserId,
    string? ImpersonatorUsername,
    string? ApiKeyId,
    string? RequestId,
    string? ClientIp,
    string? UserAgent,
    string CreatedAt);
