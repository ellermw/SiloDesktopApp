namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Text for Configure › Server › Maintenance. Keys and English values are printed
/// verbatim from the official WebUI catalog (<c>web/src/locales/en.json</c> at
/// 92ee051878c2a791940ec9598e2bdd0f04890b3e, compiled ICU ASTs printed back to message
/// patterns). Keys the Libraries domain already owns (catalog progress words, job page
/// controls, catalog toasts) are read from <see cref="AdminLibrariesText"/> rather than
/// copied. The <c>admin.native.maintenance.*</c> keys describe native-only safety states
/// (unconfirmed command outcomes, partial rewrite rows, safe failure categories); English only.
/// </summary>
public static class AdminMaintenanceText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.maintenance.page.subtitle"] = "Operational tools that affect the whole catalog live here. Use this page for bulk import/export workflows and other future maintenance actions.",
        ["web.admin.maintenance.page.title"] = "Maintenance",
        ["web.admin.catalogMaintenance.count.files"] = "{count, plural, one {{count_text} file} other {{count_text} files}}",
        ["web.admin.catalogMaintenance.count.items"] = "{count, plural, one {{count_text} item} other {{count_text} items}}",
        ["web.admin.catalogMaintenance.exports.allLibraries"] = "All libraries",
        ["web.admin.catalogMaintenance.exports.body"] = "Export jobs run in the background and upload finished seeds to the private internal S3 bucket.",
        ["web.admin.catalogMaintenance.exports.copied"] = "Copied to clipboard",
        ["web.admin.catalogMaintenance.exports.copyFailed"] = "Failed to copy to clipboard",
        ["web.admin.catalogMaintenance.exports.copyUrl"] = "Copy URL",
        ["web.admin.catalogMaintenance.exports.createLink"] = "Create seven-day link",
        ["web.admin.catalogMaintenance.exports.download"] = "Download",
        ["web.admin.catalogMaintenance.exports.empty"] = "No catalog export jobs yet.",
        ["web.admin.catalogMaintenance.exports.jobFallback"] = "Catalog export job",
        ["web.admin.catalogMaintenance.exports.libraryCount"] = "{count, plural, one {{count_text} library} other {{count_text} libraries}}",
        ["web.admin.catalogMaintenance.exports.loadOlder"] = "Load older exports",
        ["web.admin.catalogMaintenance.exports.result"] = "Exported {items} and {files}",
        ["web.admin.catalogMaintenance.exports.title"] = "Recent Catalog Exports",
        ["web.admin.catalogMaintenance.header.body"] = "Queue full catalog exports, import seeds from files or S3, and watch background job progress in one place.",
        ["web.admin.catalogMaintenance.header.importCatalog"] = "Import Catalog",
        ["web.admin.catalogMaintenance.header.startExport"] = "Start Export",
        ["web.admin.catalogMaintenance.header.title"] = "Catalog Import & Export",
        ["web.admin.catalogMaintenance.import.addRewrite"] = "Add Rewrite",
        ["web.admin.catalogMaintenance.import.bucketHelp"] = "This reads any detected `catalog-seeds/*.json.gz` object in the private internal S3 bucket, including exports from other installs.",
        ["web.admin.catalogMaintenance.import.bucketLabel"] = "Detected Bucket Artifacts",
        ["web.admin.catalogMaintenance.import.chooseBucketSeed"] = "Choose a catalog seed from the bucket",
        ["web.admin.catalogMaintenance.import.chooseExport"] = "Choose a completed export job",
        ["web.admin.catalogMaintenance.import.completedExportLabel"] = "Completed Export",
        ["web.admin.catalogMaintenance.import.conflictLabel"] = "Conflict Mode",
        ["web.admin.catalogMaintenance.import.conflictOverwrite"] = "Overwrite Existing",
        ["web.admin.catalogMaintenance.import.conflictSkip"] = "Skip Existing",
        ["web.admin.catalogMaintenance.import.description"] = "Import a catalog seed with optional path rewrites.",
        ["web.admin.catalogMaintenance.import.detectedFiles"] = "Detected Files",
        ["web.admin.catalogMaintenance.import.executionLabel"] = "Execution",
        ["web.admin.catalogMaintenance.import.executionQueued"] = "Background job",
        ["web.admin.catalogMaintenance.import.executionQueuedHelp"] = "A worker reads the source later. Local files must be available on that worker.",
        ["web.admin.catalogMaintenance.import.executionSynchronous"] = "Import and wait",
        ["web.admin.catalogMaintenance.import.executionSynchronousHelp"] = "Keep this request open until the import commits. If the connection is lost, check the catalog before trying again.",
        ["web.admin.catalogMaintenance.import.exportJobHelp"] = "Silo will load the selected seed directly from the configured operational S3 bucket.",
        ["web.admin.catalogMaintenance.import.filePathHelp"] = "Enter the absolute path to a <code>.json.gz</code> catalog seed file on the server, or select a detected file from <code>/catalog-seeds/</code>.",
        ["web.admin.catalogMaintenance.import.filePathLabel"] = "File Path",
        ["web.admin.catalogMaintenance.import.importing"] = "Importing...",
        ["web.admin.catalogMaintenance.import.loadMoreBucket"] = "Load more bucket files",
        ["web.admin.catalogMaintenance.import.loadMoreLocal"] = "Load more local files",
        ["web.admin.catalogMaintenance.import.noBucketSeeds"] = "No catalog seed objects found",
        ["web.admin.catalogMaintenance.import.noCompletedExports"] = "No completed exports yet",
        ["web.admin.catalogMaintenance.import.remoteUrlHelp"] = "Paste a public <code>.json.gz</code> catalog seed URL. Silo will download it server-side before importing.",
        ["web.admin.catalogMaintenance.import.remoteUrlLabel"] = "Remote URL",
        ["web.admin.catalogMaintenance.import.removeRewrite"] = "Remove path rewrite",
        ["web.admin.catalogMaintenance.import.retryBucket"] = "Retry bucket files",
        ["web.admin.catalogMaintenance.import.retryLocal"] = "Retry local files",
        ["web.admin.catalogMaintenance.import.rewritesHelp"] = "Rewrites use prefix matching. Mapping <code>/srv/media</code> to <code>/media</code> rewrites every nested library and file path under that root.",
        ["web.admin.catalogMaintenance.import.rewritesLabel"] = "Path Rewrites",
        ["web.admin.catalogMaintenance.import.selectDetectedFile"] = "Select a detected file",
        ["web.admin.catalogMaintenance.import.sourceBucketArtifact"] = "Bucket Artifact",
        ["web.admin.catalogMaintenance.import.sourceExportJob"] = "Local Export Job",
        ["web.admin.catalogMaintenance.import.sourceLabel"] = "Import Source",
        ["web.admin.catalogMaintenance.import.sourceLocalFile"] = "Local File",
        ["web.admin.catalogMaintenance.import.sourceRemoteUrl"] = "Remote URL",
        ["web.admin.catalogMaintenance.import.submit"] = "Import Catalog",
        ["web.admin.catalogMaintenance.import.title"] = "Import Catalog Seed",
        ["web.admin.catalogMaintenance.import.validationNote"] = "Import validates the rewritten library roots before writing anything, so missing or incomplete rewrites will fail fast instead of seeding broken paths.",
        ["web.admin.catalogMaintenance.imports.body"] = "Imports run in the background so progress stays visible while validation and writes are in flight.",
        ["web.admin.catalogMaintenance.imports.empty"] = "No catalog import jobs yet.",
        ["web.admin.catalogMaintenance.imports.jobFallback"] = "Catalog import job",
        ["web.admin.catalogMaintenance.imports.loadOlder"] = "Load older imports",
        ["web.admin.catalogMaintenance.imports.result"] = "Imported {items} and {files}",
        ["web.admin.catalogMaintenance.imports.seedFallback"] = "Catalog seed",
        ["web.admin.catalogMaintenance.imports.title"] = "Recent Catalog Imports",
        ["web.admin.catalogMaintenance.jobs.finished"] = "Finished: {date}",
        ["web.admin.catalogMaintenance.jobs.progress"] = "Progress: {progress}",
        ["web.admin.catalogMaintenance.jobs.refreshing"] = "Refreshing",
        ["web.admin.catalogMaintenance.jobs.requested"] = "requested {date}",
        ["web.admin.catalogMaintenance.refresh"] = "Refresh",
        ["web.admin.common.jobHistory.empty"] = "No jobs yet.",
        ["web.admin.common.jobHistory.finished"] = "Finished: {date}",
        ["web.admin.common.jobHistory.jobType.backupRestore"] = "Backup Restore",
        ["web.admin.common.jobHistory.jobType.catalogExport"] = "Catalog Export",
        ["web.admin.common.jobHistory.jobType.catalogImport"] = "Catalog Import",
        ["web.admin.common.jobHistory.jobType.deleteLibrary"] = "Library Delete",
        ["web.admin.common.jobHistory.jobType.imageCacheCleanup"] = "Image Cache Cleanup",
        ["web.admin.common.jobHistory.jobType.itemRefresh"] = "Item Refresh",
        ["web.admin.common.jobHistory.jobType.libraryRefresh"] = "Library Refresh",
        ["web.admin.common.jobHistory.jobType.storageTransition"] = "Storage Transition",
        ["web.admin.common.jobHistory.jobType.templateBundleApply"] = "Template Bundle Apply",
        ["web.admin.common.jobHistory.progress"] = "Progress: {progress}",
        ["web.admin.common.jobHistory.refresh"] = "Refresh job history",
        ["web.admin.common.jobHistory.refreshing"] = "Refreshing",
        ["web.admin.common.jobHistory.result.cachedObjects"] = "{count, plural, one {{count_text} cached object} other {{count_text} cached objects}}",
        ["web.admin.common.jobHistory.result.cleanupCanceled"] = "Canceled after deleting {objects}; {prefixes} fully cleaned",
        ["web.admin.common.jobHistory.result.cleanupDone"] = "Deleted {objects} across {prefixes}",
        ["web.admin.common.jobHistory.result.cleanupQueued"] = "{count, plural, one {queued cache cleanup for {count_text} directory} other {queued cache cleanup for {count_text} directories}}",
        ["web.admin.common.jobHistory.result.deleted"] = "Deleted {parts}",
        ["web.admin.common.jobHistory.result.deletedEmpty"] = "Deleted (empty)",
        ["web.admin.common.jobHistory.result.exported"] = "Exported {items}, {files}",
        ["web.admin.common.jobHistory.result.files"] = "{count, plural, one {{count_text} file} other {{count_text} files}}",
        ["web.admin.common.jobHistory.result.imported"] = "Imported {items}, {files}",
        ["web.admin.common.jobHistory.result.items"] = "{count, plural, one {{count_text} item} other {{count_text} items}}",
        ["web.admin.common.jobHistory.result.nothingToRefresh"] = "No library items to refresh",
        ["web.admin.common.jobHistory.result.prefixes"] = "{count, plural, one {{count_text} prefix} other {{count_text} prefixes}}",
        ["web.admin.common.jobHistory.result.refreshSummary"] = "Total {total}, {with_ids} direct, {without_ids} unmatched, direct {refreshed_ok} ok/{refreshed_failed} failed, pipeline {pipeline_ok} ok/{pipeline_failed} failed",
        ["web.admin.common.jobHistory.subtitle"] = "Recent background jobs across all types.",
        ["web.admin.common.jobHistory.target.allLibraries"] = "All libraries",
        ["web.admin.common.jobHistory.target.catalogSeed"] = "Catalog seed",
        ["web.admin.common.jobHistory.target.library"] = "\"{name}\"",
        ["web.admin.common.jobHistory.target.libraryCount"] = "{count, plural, one {{count_text} library} other {{count_text} libraries}}",
        ["web.admin.common.jobHistory.target.libraryNumber"] = "Library #{id}",
        ["web.admin.common.jobHistory.title"] = "Job History",
        ["web.admin.common.taskStatus.cancelled"] = "cancelled",
        ["web.admin.common.taskStatus.completed"] = "completed",
        ["web.admin.common.taskStatus.failed"] = "failed",
        ["web.admin.common.taskStatus.queued"] = "queued",
        ["web.admin.common.taskStatus.running"] = "running",

        ["admin.native.maintenance.conflict"] = "A catalog job is already queued or running.",
        ["admin.native.maintenance.exportUnknown"] = "The export request was sent, but its answer could not be confirmed. The export list was refreshed instead of sending it again.",
        ["admin.native.maintenance.publishUnknown"] = "The link request was sent, but its answer could not be confirmed. The export list was refreshed instead of sending it again.",
        ["admin.native.maintenance.importUnknown.title"] = "Import result unknown",
        ["admin.native.maintenance.importUnknown.body"] = "The import request was sent, but its answer could not be confirmed, so it may have been applied. Check the catalog and the import jobs below before importing again.",
        ["admin.native.maintenance.importUnknown.acknowledge"] = "I have checked the catalog",
        ["admin.native.maintenance.rewritePartial"] = "Enter both a source and a destination, or clear the row.",
        ["admin.native.maintenance.rewriteRequired"] = "Add path rewrites for these source roots:",
        ["admin.native.maintenance.sourcesFailed"] = "Detected files could not be loaded.",
        ["admin.native.maintenance.downloadFailed"] = "The download could not be opened.",
        ["admin.native.maintenance.error.sessionExpired"] = "Sign in again to continue.",
        ["admin.native.maintenance.error.forbidden"] = "You do not have permission to do this.",
        ["admin.native.maintenance.error.demo"] = "This action is not available in demo mode.",
        ["admin.native.maintenance.error.sourceUnavailable"] = "The selected catalog import source could not be loaded.",
        ["admin.native.maintenance.error.invalidImport"] = "The catalog seed or its import settings were rejected.",
        ["admin.native.maintenance.error.storageUnavailable"] = "Catalog storage is unavailable on this server.",
        ["admin.native.maintenance.error.notFound"] = "The job no longer exists.",
        ["admin.native.maintenance.error.rateLimited"] = "Too many requests. Wait a moment and try again.",
        ["admin.native.maintenance.error.server"] = "The server could not complete the request.",
        ["admin.native.maintenance.error.transport"] = "The server could not be reached.",
        ["admin.native.maintenance.error.invalidResponse"] = "The server sent an unexpected response.",
    };

    /// <summary>Every key this domain owns, for catalog-coverage tests.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.ToArray();

    public static bool Has(string key) => English.ContainsKey(key) || AdminLibrariesText.Has(key);

    /// <summary>Formats this domain's key, a Libraries-owned key, or any key of the localized interface catalog.</summary>
    public static string T(string key, params (string Name, object? Value)[] values)
    {
        if (!English.TryGetValue(key, out var pattern)) return AdminLibrariesText.T(key, values);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values) arguments[name] = value;
        return AdminText.Format(pattern, arguments);
    }

    /// <summary>The message with its rich-text tags removed.</summary>
    public static string Plain(string formatted) => AdminLibrariesText.Plain(formatted);
}
