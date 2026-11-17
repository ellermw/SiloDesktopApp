using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Badge styles the Maintenance rows use (shadcn Badge variants).</summary>
public enum AdminMaintenanceBadge { Default, Secondary, Outline, Destructive }

/// <summary>
/// The Maintenance formatters (official <c>adminCatalogMaintenanceFormatters.ts</c>,
/// <c>AdminJobHistory.tsx</c>, <c>AdminCatalogMaintenance.tsx</c> helpers and
/// <c>adminTaskJobFromV2</c>/<c>adminJobFromV2</c> at 92ee0518). Server text other than
/// fixed safe failure sentences and plain identifiers is never shown.
/// </summary>
public static class AdminMaintenancePresentation
{
    private static readonly Regex KindPattern = new("^[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
    private const string SafeFailureType = "/job_failed";
    private const string RewriteDetailPrefix = "Source root requires a rewrite: ";

    private static string T(string key, params (string Name, object? Value)[] values) => AdminMaintenanceText.T(key, values);
    private static (string, object?)[] Count(long count) => AdminLibrariesText.Count(count);
    private static string Number(long value) => value.ToString("N0", AppStrings.FormatCulture);

    /// <summary>WebUI status words: <c>succeeded→completed</c>, <c>canceled→cancelled</c>, <c>canceling→running</c>.</summary>
    public static string Status(AdminMaintenanceJob job) => job.State switch
    {
        "succeeded" => "completed",
        "canceled" => "cancelled",
        "canceling" => "running",
        var other => other ?? "",
    };

    /// <summary>The raw status word the catalog lists show, limited to a plain identifier.</summary>
    public static string StatusWord(AdminMaintenanceJob job) => Identifier(Status(job));

    /// <summary>Job History's translated status, or the plain status word for a newer state.</summary>
    public static string StatusLabel(AdminMaintenanceJob job) => Status(job) switch
    {
        "queued" => T("web.admin.common.taskStatus.queued"),
        "running" => T("web.admin.common.taskStatus.running"),
        "completed" => T("web.admin.common.taskStatus.completed"),
        "failed" => T("web.admin.common.taskStatus.failed"),
        "cancelled" => T("web.admin.common.taskStatus.cancelled"),
        _ => StatusWord(job),
    };

    /// <summary>Job History badge (<c>statusVariant</c>).</summary>
    public static AdminMaintenanceBadge HistoryBadge(AdminMaintenanceJob job) => Status(job) switch
    {
        "failed" => AdminMaintenanceBadge.Destructive,
        "completed" => AdminMaintenanceBadge.Secondary,
        "running" => AdminMaintenanceBadge.Default,
        _ => AdminMaintenanceBadge.Outline,
    };

    /// <summary>Catalog list badge: failed destructive, completed secondary, otherwise outline.</summary>
    public static AdminMaintenanceBadge CatalogBadge(AdminMaintenanceJob job) => Status(job) switch
    {
        "failed" => AdminMaintenanceBadge.Destructive,
        "completed" => AdminMaintenanceBadge.Secondary,
        _ => AdminMaintenanceBadge.Outline,
    };

    public static bool IsActive(AdminMaintenanceJob job) => Status(job) is "queued" or "running";

    /// <summary>The job's message: only “Cancellation requested” while canceling (v2 carries no operator message).</summary>
    public static string Message(AdminMaintenanceJob job) =>
        job.State == "canceling" ? AppStrings.Get("web.common.job.cancellationRequested", fallback: "Cancellation requested") : "";

    /// <summary>
    /// The failure line: the server's fixed <c>job_failed</c> sentence, a generic “Job failed” for any
    /// other failure shape, and nothing for storage transitions (WebUI hides their message).
    /// </summary>
    public static string? FailureText(AdminMaintenanceJob job)
    {
        if (job.Failure == null || job.Kind == "storage_transition") return null;
        if (job.Failure.Type?.EndsWith(SafeFailureType, StringComparison.Ordinal) == true
            && job.Failure.Detail is { Length: > 0 and <= 512 } detail && !detail.Any(char.IsControl))
            return detail;
        return AppStrings.Get("web.common.job.failed", fallback: "Job failed");
    }

    /// <summary><c>formatJobProgress</c>.</summary>
    public static string ProgressText(AdminMaintenanceJob job)
    {
        if (job.Progress is { Total: > 0 } progress) return Number(progress.Current) + " / " + Number(progress.Total);
        return Status(job) switch
        {
            "queued" => T("web.admin.catalogMaintenance.progress.queued"),
            "completed" => T("web.admin.catalogMaintenance.progress.complete"),
            "cancelled" => T("web.admin.catalogMaintenance.progress.cancelled"),
            _ => T("web.admin.catalogMaintenance.progress.starting"),
        };
    }

    /// <summary><c>formatExportProgressLabel</c>.</summary>
    public static string ExportProgressText(AdminMaintenanceJob job)
    {
        if (job.Progress is { Total: > 0 } progress) return Number(progress.Current) + " / " + Number(progress.Total);
        return Status(job) == "queued" ? T("web.admin.catalogMaintenance.progress.queued") : T("web.admin.catalogMaintenance.progress.waiting");
    }

    /// <summary><c>getJobProgressPercent</c>.</summary>
    public static double ProgressPercent(AdminMaintenanceJob job)
    {
        if (job.Progress is { Total: > 0 } progress) return Math.Min(100, Math.Max(0, progress.Current * 100.0 / progress.Total));
        return Status(job) switch { "completed" => 100, "running" => 12, _ => 4 };
    }

    /// <summary>Job type label; a newer kind shows its plain identifier.</summary>
    public static string KindLabel(string? kind) => kind switch
    {
        "delete_library" => T("web.admin.common.jobHistory.jobType.deleteLibrary"),
        "image_cache_cleanup" => T("web.admin.common.jobHistory.jobType.imageCacheCleanup"),
        "catalog_export" => T("web.admin.common.jobHistory.jobType.catalogExport"),
        "catalog_import" => T("web.admin.common.jobHistory.jobType.catalogImport"),
        "item_refresh" => T("web.admin.common.jobHistory.jobType.itemRefresh"),
        "library_refresh" => T("web.admin.common.jobHistory.jobType.libraryRefresh"),
        "backup_restore" => T("web.admin.common.jobHistory.jobType.backupRestore"),
        "storage_transition" => T("web.admin.common.jobHistory.jobType.storageTransition"),
        "template_bundle_apply" => T("web.admin.common.jobHistory.jobType.templateBundleApply"),
        _ => Identifier(kind),
    };

    /// <summary>Job History target (<c>jobDescription</c>).</summary>
    public static string Description(AdminMaintenanceJob job)
    {
        switch (job.Kind)
        {
            case "delete_library" or "image_cache_cleanup":
                return !string.IsNullOrEmpty(job.LibraryName)
                    ? T("web.admin.common.jobHistory.target.library", ("name", job.LibraryName))
                    : T("web.admin.common.jobHistory.target.libraryNumber", ("id", job.LibraryId ?? ""));
            case "item_refresh" or "library_refresh":
                if (!string.IsNullOrEmpty(job.LibraryName)) return T("web.admin.common.jobHistory.target.library", ("name", job.LibraryName));
                return !string.IsNullOrEmpty(job.LibraryId)
                    ? T("web.admin.common.jobHistory.target.libraryNumber", ("id", job.LibraryId))
                    : T("web.admin.common.jobHistory.target.allLibraries");
            case "catalog_export":
                return job.LibraryIds is { Count: > 0 } ids
                    ? T("web.admin.common.jobHistory.target.libraryCount", Count(ids.Count))
                    : T("web.admin.common.jobHistory.target.allLibraries");
            case "catalog_import":
                return SourceLabel(job) is { Length: > 0 } label ? label : T("web.admin.common.jobHistory.target.catalogSeed");
            default:
                return "";
        }
    }

    /// <summary>Job History result (<c>jobResult</c>), null when the job has none to show.</summary>
    public static string? Result(AdminMaintenanceJob job)
    {
        var status = Status(job);
        var library = job.LibraryResult;
        if (status == "cancelled" && job.Kind == "image_cache_cleanup")
            return T("web.admin.common.jobHistory.result.cleanupCanceled", CleanupTotals(library));
        if (status != "completed") return null;
        switch (job.Kind)
        {
            case "library_refresh":
                if (library == null || library.TotalItems == 0) return T("web.admin.common.jobHistory.result.nothingToRefresh");
                return T("web.admin.common.jobHistory.result.refreshSummary", ("total", library.TotalItems), ("with_ids", library.ItemsWithIds),
                    ("without_ids", library.ItemsWithoutIds), ("refreshed_ok", library.RefreshedOk), ("refreshed_failed", library.RefreshedFailed),
                    ("pipeline_ok", library.PipelineOk), ("pipeline_failed", library.PipelineFailed));
            case "delete_library":
            {
                var parts = new List<string>();
                if (library is { DeletedMediaFiles: > 0 }) parts.Add(T("web.admin.common.jobHistory.result.files", Count(library.DeletedMediaFiles)));
                if (library is { DeletedOrphanedItems: > 0 }) parts.Add(T("web.admin.common.jobHistory.result.items", Count(library.DeletedOrphanedItems)));
                if (library is { ImageCleanupQueued: true, ImageCleanupDirs: > 0 }) parts.Add(T("web.admin.common.jobHistory.result.cleanupQueued", Count(library.ImageCleanupDirs)));
                return parts.Count > 0 ? T("web.admin.common.jobHistory.result.deleted", ("parts", JoinList(parts))) : T("web.admin.common.jobHistory.result.deletedEmpty");
            }
            case "image_cache_cleanup":
                return T("web.admin.common.jobHistory.result.cleanupDone", CleanupTotals(library));
            case "catalog_export" when job.CatalogResult is { ItemsExported: { } exported } catalog:
                return T("web.admin.common.jobHistory.result.exported", ("items", T("web.admin.common.jobHistory.result.items", Count(exported))),
                    ("files", T("web.admin.common.jobHistory.result.files", Count(catalog.FilesExported ?? 0))));
            case "catalog_import" when job.CatalogResult is { ItemsCreated: { } created } catalog:
                return T("web.admin.common.jobHistory.result.imported", ("items", T("web.admin.common.jobHistory.result.items", Count(created))),
                    ("files", T("web.admin.common.jobHistory.result.files", Count(catalog.FilesCreated ?? 0))));
            default:
                return null;
        }
    }

    private static (string, object?)[] CleanupTotals(AdminMaintenanceLibraryResult? result) =>
    [
        ("objects", T("web.admin.common.jobHistory.result.cachedObjects", Count(result?.DeletedS3Objects ?? 0))),
        ("prefixes", T("web.admin.common.jobHistory.result.prefixes", Count(result?.DeletedPrefixes ?? 0))),
    ];

    /// <summary><c>exportScopeLabel</c>.</summary>
    public static string ExportScope(AdminMaintenanceJob job) => job.LibraryIds is { Count: > 0 } ids
        ? T("web.admin.catalogMaintenance.exports.libraryCount", Count(ids.Count))
        : T("web.admin.catalogMaintenance.exports.allLibraries");

    /// <summary><c>describeExportJob</c>: the completed-export choice in the import dialog.</summary>
    public static string ExportChoice(AdminMaintenanceJob job) => ExportScope(job) + " • " + AdminLibraryPresentation.DateTimeText(job.CreatedAt);

    /// <summary><c>describeImportJob</c>.</summary>
    public static string ImportDescription(AdminMaintenanceJob job) =>
        SourceLabel(job) is { Length: > 0 } label ? label : T("web.admin.catalogMaintenance.imports.seedFallback");

    /// <summary>Completed catalog result text for the catalog lists; exports show it only for a non-zero item count (WebUI truthiness).</summary>
    public static string? CatalogListResult(AdminMaintenanceJob job)
    {
        var result = job.CatalogResult;
        if (job.Kind == SiloPlayer.Core.Api.AdminMaintenanceApi.ExportKind)
        {
            var exported = result?.ItemsExported ?? 0;
            return exported > 0
                ? T("web.admin.catalogMaintenance.exports.result", ("items", T("web.admin.catalogMaintenance.count.items", Count(exported))),
                    ("files", T("web.admin.catalogMaintenance.count.files", Count(result?.FilesExported ?? 0))))
                : null;
        }
        if (Status(job) != "completed") return null;
        return T("web.admin.catalogMaintenance.imports.result", ("items", T("web.admin.catalogMaintenance.count.items", Count(result?.ItemsCreated ?? 0))),
            ("files", T("web.admin.catalogMaintenance.count.files", Count(result?.FilesCreated ?? 0))));
    }

    /// <summary><c>describeImportSource</c>: the modification time (or key) and the key.</summary>
    public static string SourceChoice(AdminCatalogSource source) =>
        (source.LastModified is { } modified ? AdminLibraryPresentation.DateTimeText(modified) : source.Key) + " • " + source.Key;

    /// <summary>Whether Create link is offered (completed, unpublished, links not explicitly unsupported).</summary>
    public static bool CanCreateLink(AdminMaintenanceJob job) =>
        Status(job) == "completed" && string.IsNullOrEmpty(job.PublicUrl) && job.PublicLinkSupported != false;

    /// <summary>
    /// A catalog import's source label without secrets: a remote URL keeps only its scheme, host,
    /// port and path (no user info, query or fragment, which can carry signatures).
    /// </summary>
    public static string SourceLabel(AdminMaintenanceJob job)
    {
        var label = job.SourceLabel;
        if (string.IsNullOrWhiteSpace(label) || label.Any(char.IsControl)) return "";
        if (Uri.TryCreate(label, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
        if (label.Contains("://", StringComparison.Ordinal) || label.Contains('?') || label.Contains('#')) return "";
        return label.Length > 512 ? label[..512] : label;
    }

    /// <summary>The roots a <c>path_rewrite_required</c> answer names (the server's fixed sentence carries one root each).</summary>
    public static IReadOnlyList<string> RequiredRewriteRoots(IEnumerable<SiloPlayer.Core.Api.ApiFieldError> errors) => errors
        .Where(error => error.Code == "path_rewrite_required" && error.Location == "body.path_rewrites")
        .Select(error => error.Detail is { } detail && detail.StartsWith(RewriteDetailPrefix, StringComparison.Ordinal) ? detail[RewriteDetailPrefix.Length..] : "")
        .Where(root => root.Length is > 0 and <= AdminCatalogImportDraft.MaxValueLength && !root.Any(char.IsControl))
        .Distinct(StringComparer.Ordinal).ToArray();

    private static string Identifier(string? value) => value != null && KindPattern.IsMatch(value) ? value : "";

    /// <summary>A conjunction list (WebUI <c>joinList</c>, <c>Intl.ListFormat</c> conjunction): English uses “a, b, and c”.</summary>
    internal static string JoinList(IReadOnlyList<string> parts)
    {
        if (parts.Count <= 1) return string.Concat(parts);
        if (AppStrings.FormatCulture.TwoLetterISOLanguageName != "en") return string.Join(", ", parts);
        return parts.Count == 2 ? parts[0] + " and " + parts[1] : string.Join(", ", parts.Take(parts.Count - 1)) + ", and " + parts[^1];
    }
}
