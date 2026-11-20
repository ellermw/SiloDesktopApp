using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Models.Admin;

// Configure > Server > Backups wire models (official contracts/api/v2/openapi.json and
// internal/apiv2/admin_backup*.go at 92ee051878c2a791940ec9598e2bdd0f04890b3e). Every
// response is validated by AdminBackupValidation before a controller sees it.

public sealed record AdminBackupCapabilities
{
    public string? State { get; init; }
    public bool? Allowed { get; init; }
    public string[]? Destinations { get; init; }
    public bool? Encryption { get; init; }
    public bool? Upload { get; init; }
    public long? MaxUploadBytes { get; init; }
    public bool? SelectiveRestore { get; init; }
    public bool? Download { get; init; }
    public string? Revision { get; init; }
    public bool Available => State == "available";
}

public sealed record AdminBackupObject
{
    public string? Name { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record AdminBackupMissingBlob
{
    public string? Store { get; init; }
    public string? Key { get; init; }
    public string? Source { get; init; }
}

public sealed record AdminBackupOmitted
{
    public string? Section { get; init; }
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public string? Cause { get; init; }
    public string? Error { get; init; }
}

public sealed record AdminBackup
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long? SizeBytes { get; init; }
    public string? SiloVersion { get; init; }
    public string? Location { get; init; }
    public long? BlobCount { get; init; }
    public AdminBackupMissingBlob[]? MissingBlobs { get; init; }
    public long? MissingBlobsTotal { get; init; }
    public AdminBackupOmitted[]? RestoreSetOmitted { get; init; }
    public AdminBackupObject[]? Objects { get; init; }
    public bool? Encrypted { get; init; }
    public string? KeyCheck { get; init; }
    public bool? KeyMatches { get; init; }
    public bool? Clean { get; init; }
}

public sealed record AdminBackupActive
{
    public string? Operation { get; init; }
    public string? BackupId { get; init; }
    public string? UploadId { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public double? Progress { get; init; }
}

public sealed record AdminBackupLastRun
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string? Error { get; init; }
}

public sealed record AdminBackupWarning
{
    public string? Code { get; init; }
    public string? Message { get; init; }
}

public sealed record AdminBackupList
{
    public string? ServerId { get; init; }
    public string? Destination { get; init; }
    public bool? DestinationAvailable { get; init; }
    public bool? Encrypted { get; init; }
    public string? KeyCheck { get; init; }
    public bool? KeyConfirmed { get; init; }
    public bool? Enabled { get; init; }
    public bool? Busy { get; init; }
    public AdminBackupActive? Active { get; init; }
    public long? Keep { get; init; }
    public AdminBackupLastRun? LastRun { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public AdminBackup[]? Items { get; init; }
    public string[]? OtherServerIds { get; init; }
    public AdminBackupWarning[]? Warnings { get; init; }
}

public sealed record AdminBackupStarted
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
}

public sealed record AdminBackupDownloadLink
{
    public string? Name { get; init; }
    public long? SizeBytes { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public override string ToString() => nameof(AdminBackupDownloadLink);
}

public sealed record AdminBackupS3Test
{
    public string Endpoint { get; init; } = "";
    public string Region { get; init; } = "";
    public string Bucket { get; init; } = "";
    public string KeyPrefix { get; init; } = "";
    public string AccessKey { get; init; } = "";
    /// <summary>Write-only; empty keeps the stored secret. Never echoed or logged.</summary>
    public string SecretKey { get; init; } = "";
    public bool PathStyle { get; init; }
    public override string ToString() => nameof(AdminBackupS3Test);
}

public sealed record AdminBackupS3TestResult(bool Ok, string? Step, string Message);

public sealed record AdminBackupUploadSource
{
    public string? ServerId { get; init; }
    public string? ServerName { get; init; }
    public string? SiloVersion { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public long? FormatVersion { get; init; }
}

public sealed record AdminBackupUploadRejection
{
    public string? Reason { get; init; }
    public string? Message { get; init; }
}

public sealed record AdminBackupUpload
{
    public string? Id { get; init; }
    public string? Status { get; init; }
    public long? SizeBytes { get; init; }
    public long? PartSize { get; init; }
    public long? PartCount { get; init; }
    public long? ReceivedParts { get; init; }
    public long? ReceivedBytes { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Digest { get; init; }
    public AdminBackupUploadSource? Source { get; init; }
    public AdminBackupUploadRejection? Rejection { get; init; }
    public string? Location { get; init; }
}

// ── Restore set (preview) ────────────────────────────────────────────

public sealed record AdminBackupRestoreSetting
{
    public string? Key { get; init; }
    public string? Class { get; init; }
    public string? Status { get; init; }
    public bool? Masked { get; init; }
    public string? BackupValue { get; init; }
    public string? CurrentValue { get; init; }
    public bool? RestartRequired { get; init; }
}

public sealed record AdminBackupRestorePlugin
{
    public string? Version { get; init; }
    public string? InstalledVersion { get; init; }
    public bool? Enabled { get; init; }
    public string[]? ConfigKeys { get; init; }
    public bool? ConfigRestorable { get; init; }
}

public sealed record AdminBackupRestoreLibrary
{
    public string? Type { get; init; }
    public string[]? Paths { get; init; }
    public string? LocalId { get; init; }
    public string[]? MissingPaths { get; init; }
}

public sealed record AdminBackupRestoreCollection
{
    public string[]? LibraryIds { get; init; }
    public long? Items { get; init; }
    public long? MissingItems { get; init; }
    public long? UnknownItems { get; init; }
    public string? LocalId { get; init; }
    public string? TemplateId { get; init; }
    public string? BundleId { get; init; }
    public string[]? Changes { get; init; }
}

public sealed record AdminBackupRestoreHomeSection
{
    public string? Scope { get; init; }
    public string? LibraryId { get; init; }
    public string? SectionType { get; init; }
    public string? LocalId { get; init; }
    public string[]? Changes { get; init; }
}

public sealed record AdminBackupRestorePolicyDifference
{
    public string? Field { get; init; }
    public string? Backup { get; init; }
    public string? Here { get; init; }
}

public sealed record AdminBackupRestoreDataType
{
    public string? Id { get; init; }
    public long? Count { get; init; }
    public long? Pending { get; init; }
}

public sealed record AdminBackupRestoreProfile
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Status { get; init; }
    public bool? Selectable { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public string? Match { get; init; }
    public string? Target { get; init; }
    public string? LocalId { get; init; }
    public bool? IsPrimary { get; init; }
    public bool? Restricted { get; init; }
    public long? LibrariesLeftOut { get; init; }
    public bool? NoLibraryAccess { get; init; }
    public bool? HasPin { get; init; }
    public AdminBackupRestoreDataType[]? DataTypes { get; init; }
}

public sealed record AdminBackupRestoreAccount
{
    public string? Username { get; init; }
    public string? Email { get; init; }
    public string? Role { get; init; }
    public bool? Admin { get; init; }
    public string? Match { get; init; }
    public string? Target { get; init; }
    public string? LocalId { get; init; }
    public string? LocalUsername { get; init; }
    public string? Group { get; init; }
    public AdminBackupRestorePolicyDifference[]? PolicyDifferences { get; init; }
    public long? LibrariesLeftOut { get; init; }
    public long? ProfileRoom { get; init; }
    public AdminBackupRestoreProfile[]? Profiles { get; init; }
}

public sealed record AdminBackupRestoreItem
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Label { get; init; }
    public string? Status { get; init; }
    public bool? Selectable { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public string[]? Requires { get; init; }
    public bool? RestartRequired { get; init; }
    public bool? ExternalService { get; init; }
    public string[]? MissingBlobs { get; init; }
    public AdminBackupRestoreSetting[]? Settings { get; init; }
    public AdminBackupRestorePlugin? Plugin { get; init; }
    public AdminBackupRestoreLibrary? Library { get; init; }
    public AdminBackupRestoreCollection? Collection { get; init; }
    public AdminBackupRestoreAccount? Account { get; init; }
    public AdminBackupRestoreHomeSection? HomeSection { get; init; }
}

public sealed record AdminBackupRestoreSection
{
    public string? Id { get; init; }
    public long? Count { get; init; }
    public AdminBackupRestoreItem[]? Items { get; init; }
}

public sealed record AdminBackupLibraryMapping
{
    public string? BackupId { get; init; }
    public string? Name { get; init; }
    public string? LocalId { get; init; }
    public string? Status { get; init; }
    public bool? Selectable { get; init; }
}

public sealed record AdminBackupRestoreSet
{
    public string? BackupId { get; init; }
    public string? UploadId { get; init; }
    public string? Digest { get; init; }
    public long? FormatVersion { get; init; }
    public string? ServerId { get; init; }
    public string? ServerName { get; init; }
    public string? SiloVersion { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public bool? SameServer { get; init; }
    public bool? KeyMatches { get; init; }
    public bool? Authenticated { get; init; }
    public string[]? MissingBlobs { get; init; }
    public AdminBackupOmitted[]? Omitted { get; init; }
    public string[]? NeverRestoredSettings { get; init; }
    public AdminBackupLibraryMapping[]? LibraryMap { get; init; }
    public AdminBackupRestoreSection[]? Sections { get; init; }
}

// ── Restore request ──────────────────────────────────────────────────

public sealed record AdminBackupRestoreProfileSelection(string Profile, string Target, IReadOnlyList<string> DataTypes);
public sealed record AdminBackupRestoreUserSelection(string Account, string Target, IReadOnlyList<AdminBackupRestoreProfileSelection> Profiles);

public sealed record AdminBackupRestoreSelection
{
    public IReadOnlyList<string> Libraries { get; init; } = [];
    public IReadOnlyList<string> Plugins { get; init; } = [];
    public IReadOnlyList<string> Settings { get; init; } = [];
    public IReadOnlyList<AdminBackupRestoreUserSelection> Users { get; init; } = [];
    public IReadOnlyList<string> Collections { get; init; } = [];
    public IReadOnlyList<string> HomeSections { get; init; } = [];
    public bool IsEmpty => Libraries.Count + Plugins.Count + Settings.Count + Users.Count + Collections.Count + HomeSections.Count == 0;
}

// ── Restore job and report ───────────────────────────────────────────

public sealed record AdminBackupRestoreCounts
{
    public long? Restored { get; init; }
    public long? Unchanged { get; init; }
    public long? LeftOut { get; init; }
    public long? Failed { get; init; }
}

public sealed record AdminBackupRestoreReportData
{
    public string? Type { get; init; }
    public long? Added { get; init; }
    public long? Updated { get; init; }
    public long? Unchanged { get; init; }
    public long? LeftOut { get; init; }
    public string[]? Notes { get; init; }
}

public sealed record AdminBackupRestoreReportProfile
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public string? LocalId { get; init; }
    public bool? Created { get; init; }
    public string? Outcome { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public string[]? Notes { get; init; }
    public AdminBackupRestoreReportData[]? Data { get; init; }
}

public sealed record AdminBackupRestoreReportAccount
{
    public string? LocalId { get; init; }
    public bool? Created { get; init; }
    public AdminBackupRestoreReportProfile[]? Profiles { get; init; }
    public string[]? Unmatched { get; init; }
    public string[]? Hidden { get; init; }
}

public sealed record AdminBackupRestoreReportItem
{
    public string? Section { get; init; }
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Label { get; init; }
    public string? Outcome { get; init; }
    public string? Reason { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public bool? RestartRequired { get; init; }
    public string[]? RestartKeys { get; init; }
    public string[]? Notes { get; init; }
    public string[]? MissingBlobs { get; init; }
    public AdminBackupRestoreReportAccount? Account { get; init; }
}

public sealed record AdminBackupRestoreReport
{
    public string? BackupId { get; init; }
    public string? UploadId { get; init; }
    public string? BeforeRestoreBackupId { get; init; }
    public AdminBackupLibraryMapping[]? LibraryMap { get; init; }
    public AdminBackupRestoreReportItem[]? Items { get; init; }
    public AdminBackupRestoreCounts? Counts { get; init; }
    public bool? Interrupted { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record AdminBackupRestoreSource
{
    public string? BackupId { get; init; }
    public string? UploadId { get; init; }
}

public sealed record AdminBackupJobProgress
{
    public long? Current { get; init; }
    public long? Total { get; init; }
    public string? Unit { get; init; }
}

public sealed record AdminBackupJobFailure
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public bool? Retryable { get; init; }
}

/// <summary>The subset of <c>AdminTaskJob</c> a backup restore reads.</summary>
public sealed record AdminBackupRestoreJob
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? State { get; init; }
    public bool? Terminal { get; init; }
    public bool? Cancelable { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public AdminBackupJobProgress? Progress { get; init; }
    public AdminBackupJobFailure? Failure { get; init; }
    public AdminBackupRestoreReport? BackupRestoreResult { get; init; }
    public AdminBackupRestoreSource? BackupRestoreSource { get; init; }
}

/// <summary>A local library name for restore grouping (official useAdminLibraries).</summary>
public sealed record AdminBackupLocalLibrary(string Id, string Name);

/// <summary>What a restore reads: a backup of this server, or an uploaded restore set.</summary>
public sealed record AdminBackupRestoreSourceRef(bool Upload, string Id)
{
    public static AdminBackupRestoreSourceRef Backup(string id) => new(false, id);
    public static AdminBackupRestoreSourceRef FromUpload(string id) => new(true, id);
}

/// <summary>Identifier and value shapes the official handlers accept.</summary>
public static partial class AdminBackupIdentifiers
{
    public const long MaxUploadBytes = 2L << 30;
    public const long MinPartSize = 1L << 20;
    public const long MaxPartSize = 16L << 20;
    public const long DefaultPartSize = 4L << 20;
    public const int MaxPartIndex = 4095;

    [GeneratedRegex("^[0-9]{8}T[0-9]{6}(\\.[0-9]{3})?Z-[a-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupIdPattern();
    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex UploadIdPattern();
    [GeneratedRegex("^[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyCheckPattern();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
    [GeneratedRegex("^[A-Za-z0-9._~-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex JobIdPattern();

    public static bool IsBackupId(string? value) => value is { Length: > 0 and <= 64 } && BackupIdPattern().IsMatch(value);
    public static bool IsUploadId(string? value) => value != null && UploadIdPattern().IsMatch(value);
    public static bool IsKeyCheck(string? value) => value != null && KeyCheckPattern().IsMatch(value);
    public static bool IsDigest(string? value) => value != null && DigestPattern().IsMatch(value);
    public static bool IsJobId(string? value) => value != null && JobIdPattern().IsMatch(value);

    /// <summary>Stored object names the signed object route serves (official enum).</summary>
    public static readonly IReadOnlySet<string> ObjectNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "manifest.json", "db.dump", "blobs.tar", "restore-set.tar", "db.dump.age", "blobs.tar.age", "restore-set.tar.age",
    };
}
