using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Models.Admin;

// Manage › Libraries › Libraries wire models (official contracts/api/v2/openapi.json at
// abd85d0cadac28f4ab7db6821dad9634fc114790; unchanged from 7115ee70 for every operation
// below). v2 renders every identifier as an opaque decimal string; the native page keys
// rows, dialogs and caches by that exact string and never converts it to a double.

/// <summary>v2 <c>Library</c>: one configured library as the admin list shows it.</summary>
public sealed class AdminLibrary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public List<string> Paths { get; set; } = [];
    public bool Enabled { get; set; }
    public string MetadataLanguage { get; set; } = "";
    public bool AutoTranslateMetadata { get; set; }
    public bool ChapterThumbnailsEnabled { get; set; }
    public bool ChapterThumbnailsSupported { get; set; }
    public bool IntroDetectionEnabled { get; set; }
    public bool TrickplayEnabled { get; set; }
    public bool TrickplaySupported { get; set; }
    public bool BoxSetsEnabled { get; set; }
    public int BoxSetMinMovies { get; set; }
    public bool BoxSetsInSearch { get; set; }
    public List<string> TrailerKinds { get; set; } = [];
    public bool RealtimeMonitoring { get; set; }
    public long SortOrder { get; set; }
    public string? PosterUrl { get; set; }
    public DateTimeOffset? LastScannedAt { get; set; }
    /// <summary>empty_root | dead_root | partial_walk, or another server code shown as no warning.</summary>
    public string? ScanWarningCode { get; set; }
    public string? ScanWarningMessage { get; set; }
    public DateTimeOffset? ScanWarningAt { get; set; }
}

/// <summary>v2 <c>LibraryCollection</c>: every library in sort order (no cursor in practice).</summary>
public sealed class AdminLibraryCollection
{
    public List<AdminLibrary> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

/// <summary>v2 <c>LibraryCapabilities</c>: feature detection for realtime monitoring and seek previews.</summary>
public sealed class AdminLibraryCapabilities
{
    public bool Allowed { get; set; }
    public bool BoxSets { get; set; }
    public bool IgnoreUnmatched { get; set; }
    public bool RealtimeMonitoring { get; set; }
    public string Revision { get; set; } = "";
    /// <summary>available | disabled | not_configured | unsupported</summary>
    public string State { get; set; } = "";
    public bool Trickplay { get; set; }
    public bool TrickplaySupported { get; set; }
}

/// <summary>v2 <c>LibraryRealtimeMonitoring</c>.</summary>
public sealed class AdminLibraryRealtimeMonitoring
{
    public bool ServerEnabled { get; set; }
    public List<AdminLibraryRealtimeMonitoringEntry> Libraries { get; set; } = [];
}

public sealed class AdminLibraryRealtimeMonitoringEntry
{
    public string LibraryId { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>server_disabled … error (see <c>LibraryRealtimeMonitoringEntry.state</c>).</summary>
    public string State { get; set; } = "";
    public string Backend { get; set; } = "";
    public string Detail { get; set; } = "";
    public long Directories { get; set; }
    public string? NodeId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>v2 <c>AdminTrickplayLibrary</c>: seek-preview progress of one library.</summary>
public sealed class AdminTrickplayLibrary
{
    public string LibraryId { get; set; } = "";
    public string Name { get; set; } = "";
    public long Pending { get; set; }
    public long Running { get; set; }
    public long Ready { get; set; }
    public long Unusable { get; set; }
    public long SheetBytes { get; set; }
}

public sealed class AdminTrickplayLibraries
{
    public List<AdminTrickplayLibrary> Items { get; set; } = [];
}

/// <summary>v2 <c>LibraryMountCheck</c>.</summary>
public sealed class AdminLibraryMountCheck
{
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public bool Healthy { get; set; }
    public string Status { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public List<AdminLibraryMountCheckRoot> Roots { get; set; } = [];
}

public sealed class AdminLibraryMountCheckRoot
{
    public string Path { get; set; } = "";
    public bool Reachable { get; set; }
    public bool SuspectEmpty { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>v2 <c>EmptyRootCleanup</c>.</summary>
public sealed class AdminEmptyRootCleanup
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>v2 <c>JobProgress</c>.</summary>
public sealed class AdminJobProgress
{
    public long Current { get; set; }
    public long Total { get; set; }
    public string Unit { get; set; } = "";
}

/// <summary>v2 <c>JobFailure</c>.</summary>
public sealed class AdminJobFailure
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Retryable { get; set; }
}

/// <summary>
/// v2 <c>AdminJob</c> (202 receipts and cancel answers) and the subset of
/// <c>AdminTaskJob</c> (the <c>/admin/jobs</c> listing) this page reads. A 202
/// receipt is never completion: <see cref="Terminal"/> and <see cref="State"/>
/// say what actually happened.
/// </summary>
public sealed class AdminLibraryJob
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>queued | running | canceling | succeeded | failed | canceled (or another server state).</summary>
    public string State { get; set; } = "";
    public bool Terminal { get; set; }
    public bool Cancelable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public AdminJobProgress? Progress { get; set; }
    public AdminJobFailure? Failure { get; set; }
    /// <summary>Present on task-job rows; the refresh job's library.</summary>
    public string? LibraryId { get; set; }
    public string? LibraryName { get; set; }
    public List<string>? LibraryIds { get; set; }
    public AdminLibraryRefreshResult? RefreshResult { get; set; }
}

public sealed class AdminLibraryRefreshResult
{
    public string LibraryId { get; set; } = "";
    public long Refreshed { get; set; }
    public long Failed { get; set; }
}

/// <summary>v2 <c>CollectionAdminTaskJob</c>.</summary>
public sealed class AdminLibraryJobCollection
{
    public List<AdminLibraryJob> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

/// <summary>v2 <c>AdminTask</c>: the scheduled task the Scan all button runs.</summary>
public sealed class AdminLibraryTaskReceipt
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
}

/// <summary>v2 <c>ScanStartOutputBody</c>: accepted, never finished.</summary>
public sealed class AdminScanStartReceipt
{
    public string LibraryId { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Status { get; set; } = "";
}

/// <summary>v2 <c>ScanCancelOutputBody</c>.</summary>
public sealed class AdminScanCancelReceipt
{
    public string LibraryId { get; set; } = "";
    public long Cancelled { get; set; }
}

/// <summary>A scan run from the <c>scans</c> realtime channel (there is no REST list).</summary>
public sealed record AdminLibraryScanRun(
    string Id,
    string LibraryId,
    string Mode,
    string? Path,
    string Status,
    string? Trigger,
    DateTimeOffset? StartedAt,
    long? TotalFiles,
    long? FilesProcessed,
    string? Message)
{
    public bool IsRunning => Status == "running";
    /// <summary>The WebUI keeps accepted and running scans; every other status leaves the list.</summary>
    public bool IsActive => Status is "accepted" or "running";
}

// ── Provider chains ──────────────────────────────────────────────────────────

/// <summary>v2 <c>ProviderChainEntry</c>.</summary>
public sealed class AdminProviderChainEntry
{
    public string PluginInstallationId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string ProviderSlug { get; set; } = "";
    public long Priority { get; set; }
    public bool Enabled { get; set; }
    public bool Local { get; set; }
}

/// <summary>v2 <c>ProviderChainLevel</c>.</summary>
public sealed class AdminProviderChainLevel
{
    public string ContentLevel { get; set; } = "";
    public List<AdminProviderChainEntry> Entries { get; set; } = [];
}

/// <summary>v2 <c>LibraryProviders</c> / <c>LibraryProviderDefaults</c>.</summary>
public sealed class AdminProviderChains
{
    public List<AdminProviderChainLevel> Levels { get; set; } = [];
}

// ── Diagnostics ──────────────────────────────────────────────────────────────

/// <summary>v2 <c>UnmatchedItem</c>.</summary>
public sealed class AdminUnmatchedItem
{
    public string ContentId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public DateTimeOffset? IgnoredAt { get; set; }
}

/// <summary>v2 <c>UnmatchedItemCollection</c>, <c>SkippedRootCollection</c>, … one cursor page and the filtered total.</summary>
public sealed class AdminTotalPage<T>
{
    public List<T> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
    public long Total { get; set; }
}

/// <summary>v2 <c>SkippedRoot</c>.</summary>
public sealed class AdminSkippedRoot
{
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string Reason { get; set; } = "";
    public long FileCount { get; set; }
    public string SampleFilePath { get; set; } = "";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>v2 <c>StaleMediaID</c>.</summary>
public sealed class AdminStaleMediaId
{
    public string ContentId { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>v2 <c>RootOverride</c>.</summary>
public sealed class AdminRootOverride
{
    public string? ForcedType { get; set; }
    public string? ForcedTitle { get; set; }
    public int? ForcedYear { get; set; }
    public string? ForcedTmdbId { get; set; }
    public string? ForcedImdbId { get; set; }
    public string? ForcedTvdbId { get; set; }
    public string? Note { get; set; }
}

/// <summary>v2 <c>LibraryRoot</c>: one observed root and how the scanner classified it.</summary>
public sealed class AdminLibraryRoot
{
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string RootPath { get; set; } = "";
    public string State { get; set; } = "";
    public string InferredType { get; set; } = "";
    public string TypeConfidence { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? TvdbId { get; set; }
    public long ObservedFileCount { get; set; }
    public string? SampleFilePath { get; set; }
    public JsonElement? EvidenceJson { get; set; }
    public string? OverrideSource { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public AdminRootOverride? ActiveOverride { get; set; }
    public string? ContentId { get; set; }
}

/// <summary>v2 <c>MetadataMatchQueueStatus</c>.</summary>
public sealed class AdminMatchQueueStatus
{
    public string LibraryId { get; set; } = "";
    public long MovieCount { get; set; }
    public long SeriesCount { get; set; }
    public long RawFileCount { get; set; }
    public long PendingCount { get; set; }
    public long ParkedCount { get; set; }
    public long TotalCount { get; set; }
}

public sealed class AdminMatchQueueStatusCollection
{
    public List<AdminMatchQueueStatus> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

/// <summary>A movie or series queue entry (v2 <c>MovieMatchQueueEntry</c> / <c>SeriesMatchQueueEntry</c>).</summary>
public sealed class AdminMatchQueueEntry
{
    public string LibraryId { get; set; } = "";
    public string? MediaFileId { get; set; }
    public string? FilePath { get; set; }
    public string? ObservedRootPath { get; set; }
    public string State { get; set; } = "";
    public string? FailureKind { get; set; }
    public JsonElement? FailureDetail { get; set; }
    public string? LastError { get; set; }
    public long AttemptCount { get; set; }
}

/// <summary>v2 <c>RawMatchBacklogEntry</c>.</summary>
public sealed class AdminRawMatchEntry
{
    public string LibraryId { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? BaseTitle { get; set; }
    public string? BaseType { get; set; }
    public int? BaseYear { get; set; }
}

/// <summary>v2 <c>MetadataMatchQueueDetail</c>: one page of a library's matcher backlog.</summary>
public sealed class AdminMatchQueueDetail
{
    public string LibraryId { get; set; } = "";
    public List<AdminMatchQueueEntry> Movies { get; set; } = [];
    public List<AdminMatchQueueEntry> Series { get; set; } = [];
    public List<AdminRawMatchEntry> RawFiles { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
    public long TotalCount { get; set; }
}

/// <summary>v2 <c>MetadataMatchQueueAction</c>.</summary>
public sealed class AdminMatchQueueAction
{
    public string LibraryId { get; set; } = "";
    public string Status { get; set; } = "";
    public long? RawFileRetried { get; set; }
}

/// <summary>v2 <c>AdminFilesystemPage</c>: one page of a server directory.</summary>
public sealed class AdminFilesystemPage
{
    public string Path { get; set; } = "";
    public string Parent { get; set; } = "";
    public List<AdminFilesystemEntry> Items { get; set; } = [];
    public ApiPageInfo? Page { get; set; }
}

public sealed class AdminFilesystemEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>v2 <c>MetadataTranslationJob</c> (library prewarm jobs).</summary>
public sealed class AdminTranslationJob
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
    public string ProgressMessage { get; set; } = "";
    public double Progress { get; set; }
    public long FieldsDone { get; set; }
    public long FieldsTotal { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>WebUI <c>isActiveTranslationJob</c>: pending or running.</summary>
    public bool IsActive => Status is "pending" or "running";
}

public sealed class AdminTranslationJobs
{
    public List<AdminTranslationJob> Jobs { get; set; } = [];
}

/// <summary>v2 <c>MetadataAICapability</c> (profile-scoped).</summary>
public sealed class AdminMetadataAiCapability
{
    public bool Allowed { get; set; }
    public string State { get; set; } = "";
    public string OnView { get; set; } = "";
    public string Revision { get; set; } = "";
}
