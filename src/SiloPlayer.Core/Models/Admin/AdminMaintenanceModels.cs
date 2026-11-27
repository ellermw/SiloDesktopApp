using System.Text.Json.Serialization;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// v2 <c>AdminTaskJob</c> as Configure › Maintenance reads it (official
/// <c>internal/apiv2/admin_task_jobs.go</c> at 92ee0518). Only the members the
/// page shows are declared; any other member is ignored. A 202 receipt is never
/// completion: <see cref="State"/> and <see cref="Terminal"/> say what happened.
/// </summary>
public sealed class AdminMaintenanceJob
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>queued | running | canceling | succeeded | failed | canceled, or a newer server state.</summary>
    public string State { get; set; } = "";
    public bool Terminal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public AdminJobProgress? Progress { get; set; }
    /// <summary>Fixed safe terminal failure text; never provider diagnostics.</summary>
    public AdminJobFailure? Failure { get; set; }
    public string? LibraryId { get; set; }
    public string? LibraryName { get; set; }
    public List<string>? LibraryIds { get; set; }
    public string? SourceLabel { get; set; }
    public string? SourceExportJobId { get; set; }
    public AdminMaintenanceLibraryResult? LibraryResult { get; set; }
    public AdminMaintenanceCatalogResult? CatalogResult { get; set; }
    public AdminMaintenanceStorageTransitionResult? StorageTransitionResult { get; set; }
    public long ArtifactSizeBytes { get; set; }
    /// <summary>Self-authorizing signed download (presigned object or server-signed route); never sent with account credentials.</summary>
    public string? DownloadUrl { get; set; }
    public DateTimeOffset? DownloadExpiresAt { get; set; }
    /// <summary>A published seven-day signed link; shown only through Copy URL.</summary>
    public string? PublicUrl { get; set; }
    /// <summary>Absent on non-catalog rows; WebUI hides Create link only when this is explicitly false.</summary>
    public bool? PublicLinkSupported { get; set; }
}

/// <summary>v2 <c>AdminTaskJobLibraryResult</c> (library refresh, library delete, image cache cleanup).</summary>
public sealed class AdminMaintenanceLibraryResult
{
    public long TotalItems { get; set; }
    public long ItemsWithIds { get; set; }
    public long ItemsWithoutIds { get; set; }
    public long RefreshedOk { get; set; }
    public long RefreshedFailed { get; set; }
    public long PipelineOk { get; set; }
    public long PipelineFailed { get; set; }
    public long DeletedMediaFiles { get; set; }
    public long DeletedOrphanedItems { get; set; }
    public bool ImageCleanupQueued { get; set; }
    public long ImageCleanupDirs { get; set; }
    public long DeletedPrefixes { get; set; }
    [JsonPropertyName("deleted_s3_objects")]
    public long DeletedS3Objects { get; set; }
}

/// <summary>v2 <c>AdminTaskJobCatalogResult</c>: present on completed catalog export and import jobs.</summary>
public sealed class AdminMaintenanceCatalogResult
{
    public long? ItemsExported { get; set; }
    public long? FilesExported { get; set; }
    public long? ItemsCreated { get; set; }
    public long? FilesCreated { get; set; }
}

/// <summary>v2 <c>AdminTaskJobStorageTransitionResult</c>: safe phase and failure category only.</summary>
public sealed class AdminMaintenanceStorageTransitionResult
{
    public string Phase { get; set; } = "";
    public string? FailureCategory { get; set; }
}

/// <summary>v2 <c>CollectionAdminTaskJob</c>.</summary>
public sealed class AdminMaintenanceJobCollection
{
    public List<AdminMaintenanceJob>? Items { get; set; }
    public ApiPageInfo? Page { get; set; }
}

/// <summary>v2 <c>AdminCatalogSource</c>: an opaque server key, never a local Windows path.</summary>
public sealed class AdminCatalogSource
{
    public string Key { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTimeOffset? LastModified { get; set; }
}

/// <summary>v2 <c>CollectionAdminCatalogSource</c>.</summary>
public sealed class AdminCatalogSourceCollection
{
    public List<AdminCatalogSource>? Items { get; set; }
    public ApiPageInfo? Page { get; set; }
}

/// <summary>v2 <c>ImportResult</c>: a synchronous import committed with these counts.</summary>
public sealed class AdminCatalogImportResult
{
    [JsonRequired]
    public long ItemsCreated { get; set; }
    [JsonRequired]
    public long FilesCreated { get; set; }
}

/// <summary>v2 <c>AdminCatalogPublished</c>. The URL is a signed capability; it is never logged or shown.</summary>
public sealed class AdminCatalogPublished
{
    public string JobId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
}
