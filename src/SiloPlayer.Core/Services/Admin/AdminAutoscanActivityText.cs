using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public static class AdminAutoscanActivityText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["common.tryAgain"] = "Try again",
        ["web.admin.autoscan.activity.activeCount"] = "{count, plural, one {{count_text} active} other {{count_text} active}}",
        ["web.admin.autoscan.activity.change.followUp"] = "A scan of this scope was already running, so it is scanned again when that scan finishes: {scope}",
        ["web.admin.autoscan.activity.change.followUpInLibrary"] = "A scan of this scope was already running, so it is scanned again when that scan finishes: {scope} in {library}",
        ["web.admin.autoscan.activity.change.joined"] = "Added to a scan that was already queued: {scope}",
        ["web.admin.autoscan.activity.change.joinedInLibrary"] = "Added to a scan that was already queued: {scope} in {library}",
        ["web.admin.autoscan.activity.change.notScanned"] = "This path was not scanned.",
        ["web.admin.autoscan.activity.change.processingFailed"] = "Processing failed.",
        ["web.admin.autoscan.activity.change.queued"] = "New scan: {scope}",
        ["web.admin.autoscan.activity.change.queuedInLibrary"] = "New scan: {scope} in {library}",
        ["web.admin.autoscan.activity.change.resolveFailed"] = "Resolving the path failed.",
        ["web.admin.autoscan.activity.change.resolveFailedDetail"] = "Resolving the path failed: {detail}.",
        ["web.admin.autoscan.activity.change.rewrittenTo"] = "Rewritten to {path}",
        ["web.admin.autoscan.activity.change.scanFallback"] = "Scan",
        ["web.admin.autoscan.activity.change.scope"] = "{mode} · {path}",
        ["web.admin.autoscan.activity.change.suppressed"] = "Already requested within the debounce window, so no new scan was requested.",
        ["web.admin.autoscan.activity.change.suppressedPath"] = "Already requested within the debounce window, so no new scan was requested ({path}).",
        ["web.admin.autoscan.activity.change.withLibrary"] = "{text} ({library})",
        ["web.admin.autoscan.activity.changes.none"] = "The source reported no changes.",
        ["web.admin.autoscan.activity.changes.notRecorded"] = "Paths were not recorded for this event.",
        ["web.admin.autoscan.activity.changes.truncated"] = "{count, plural, one {Showing the first {shown} of {count_text} change.} other {Showing the first {shown} of {count_text} changes.}}",
        ["web.admin.autoscan.activity.column.action"] = "Action",
        ["web.admin.autoscan.activity.column.counts"] = "Counts",
        ["web.admin.autoscan.activity.column.details"] = "Details",
        ["web.admin.autoscan.activity.column.duration"] = "Duration",
        ["web.admin.autoscan.activity.column.library"] = "Library",
        ["web.admin.autoscan.activity.column.poll"] = "Poll",
        ["web.admin.autoscan.activity.column.progress"] = "Progress",
        ["web.admin.autoscan.activity.column.result"] = "Result",
        ["web.admin.autoscan.activity.column.scope"] = "Scope",
        ["web.admin.autoscan.activity.column.source"] = "Source",
        ["web.admin.autoscan.activity.column.status"] = "Status",
        ["web.admin.autoscan.activity.column.time"] = "Time",
        ["web.admin.autoscan.activity.delivery.webhook"] = "Webhook",
        ["web.admin.autoscan.activity.delivery.webhookEvent"] = "Webhook · {event_type}",
        ["web.admin.autoscan.activity.details.linkedCount"] = "{count, plural, one {{count_text} linked} other {{count_text} linked}}",
        ["web.admin.autoscan.activity.details.pathCount"] = "{count, plural, one {{count_text} path} other {{count_text} paths}}",
        ["web.admin.autoscan.activity.details.paths"] = "Paths",
        ["web.admin.autoscan.activity.details.scansCreated"] = "Scans created",
        ["web.admin.autoscan.activity.duration.milliseconds"] = "{value}ms",
        ["web.admin.autoscan.activity.duration.minutesSeconds"] = "{minutes}m {seconds}s",
        ["web.admin.autoscan.activity.duration.seconds"] = "{value}s",
        ["web.admin.autoscan.activity.entireLibrary"] = "Entire library",
        ["web.admin.autoscan.activity.filter.allStatuses"] = "All statuses",
        ["web.admin.autoscan.activity.history.clearSearch"] = "Clear history search",
        ["web.admin.autoscan.activity.history.emptyFiltered"] = "No autoscan activity matches those filters.",
        ["web.admin.autoscan.activity.history.emptyPolls"] = "No autoscan polls have been recorded yet.",
        ["web.admin.autoscan.activity.history.emptyScans"] = "No autoscan scans have been created yet.",
        ["web.admin.autoscan.activity.history.loadFailed"] = "Failed to load autoscan activity.",
        ["web.admin.autoscan.activity.history.polls"] = "Polls",
        ["web.admin.autoscan.activity.history.pollsDescription"] = "Diagnostic poll records from scan-source plugins.",
        ["web.admin.autoscan.activity.history.pollsTitle"] = "Poll log",
        ["web.admin.autoscan.activity.history.reset"] = "Reset",
        ["web.admin.autoscan.activity.history.scans"] = "Scans",
        ["web.admin.autoscan.activity.history.scansDescription"] = "Real scan rows created by autoscan, searchable by path, library, source, status, or scan id.",
        ["web.admin.autoscan.activity.history.scansTitle"] = "Scan history",
        ["web.admin.autoscan.activity.history.searchPolls"] = "Search poll log",
        ["web.admin.autoscan.activity.history.searchScans"] = "Search scan history",
        ["web.admin.autoscan.activity.libraryFallback"] = "Library #{id}",
        ["web.admin.autoscan.activity.metrics.changes"] = "{count, plural, one {{count_text} change} other {{count_text} changes}}",
        ["web.admin.autoscan.activity.metrics.created"] = "{count, plural, one {{count_text} created} other {{count_text} created}}",
        ["web.admin.autoscan.activity.metrics.reused"] = "{count, plural, one {{count_text} reused} other {{count_text} reused}}",
        ["web.admin.autoscan.activity.metrics.suppressed"] = "{count, plural, one {{count_text} suppressed} other {{count_text} suppressed}}",
        ["web.admin.autoscan.activity.metrics.targets"] = "{count, plural, one {{count_text} target} other {{count_text} targets}}",
        ["web.admin.autoscan.activity.outcome.error"] = "Error",
        ["web.admin.autoscan.activity.outcome.ignored"] = "Ignored",
        ["web.admin.autoscan.activity.outcome.joined"] = "Joined scan",
        ["web.admin.autoscan.activity.outcome.queued"] = "Queued",
        ["web.admin.autoscan.activity.outcome.suppressed"] = "Suppressed",
        ["web.admin.autoscan.activity.outcome.unresolved"] = "Unresolved",
        ["web.admin.autoscan.activity.page.subtitle"] = "Changes Autoscan received from its sources, and the scans they started.",
        ["web.admin.autoscan.activity.page.title"] = "Autoscan Activity",
        ["web.admin.autoscan.activity.pollStatus.error"] = "Error",
        ["web.admin.autoscan.activity.pollStatus.running"] = "Running",
        ["web.admin.autoscan.activity.pollStatus.success"] = "Success",
        ["web.admin.autoscan.activity.pollStatus.unresolved"] = "Unresolved",
        ["web.admin.autoscan.activity.polls.description"] = "Scan-source plugin calls currently in progress.",
        ["web.admin.autoscan.activity.polls.elapsed"] = "{duration} elapsed",
        ["web.admin.autoscan.activity.polls.started"] = "Started {time}",
        ["web.admin.autoscan.activity.polls.title"] = "Polling now",
        ["web.admin.autoscan.activity.queue.cancelLabel"] = "Cancel scans for this library",
        ["web.admin.autoscan.activity.queue.description"] = "Live scans created by autoscan sources.",
        ["web.admin.autoscan.activity.queue.empty"] = "No autoscan scans are queued or running.",
        ["web.admin.autoscan.activity.queue.pendingDetails"] = "Queue counts are active, but live scan details have not arrived yet.",
        ["web.admin.autoscan.activity.queue.title"] = "Autoscan queue",
        ["web.admin.autoscan.activity.queue.waiting"] = "Waiting for capacity",
        ["web.admin.autoscan.activity.reason.enqueueFailed"] = "The scan could not be queued.",
        ["web.admin.autoscan.activity.reason.libraryDisabled"] = "The matching library is disabled.",
        ["web.admin.autoscan.activity.reason.libraryRootOffline"] = "The library's storage root is not available.",
        ["web.admin.autoscan.activity.reason.noLibraryMatch"] = "No Silo library folder contains this path. Add a path mapping for this source or check the library folders.",
        ["web.admin.autoscan.activity.reason.pathAmbiguous"] = "This path matches more than one library.",
        ["web.admin.autoscan.activity.reason.pathIgnored"] = "The library's ignore files exclude this file, so it was not scanned.",
        ["web.admin.autoscan.activity.reason.pathNotFileOrDir"] = "This path is not a file or directory.",
        ["web.admin.autoscan.activity.reason.pathNotInspectable"] = "Silo could not inspect this path.",
        ["web.admin.autoscan.activity.reason.pathOutsideLibrary"] = "This path is outside the matched library.",
        ["web.admin.autoscan.activity.reason.pathPermissionDenied"] = "Silo is not allowed to read this path.",
        ["web.admin.autoscan.activity.reason.pathRequired"] = "The source sent an empty path.",
        ["web.admin.autoscan.activity.reason.resolvesToLibrary"] = "The file change resolved to a whole library, so it was not scanned.",
        ["web.admin.autoscan.activity.reason.subtreeOutsideLibrary"] = "This path is not below a library root.",
        ["web.admin.autoscan.activity.reason.unsupportedExtension"] = "This file type is not supported for the library.",
        ["web.admin.autoscan.activity.refreshLabel"] = "Refresh autoscan activity",
        ["web.admin.autoscan.activity.result.errors"] = "{count, plural, one {{count_text} error} other {{count_text} errors}}",
        ["web.admin.autoscan.activity.result.filesRemoved"] = "{count, plural, one {{count_text} file removed} other {{count_text} files removed}}",
        ["web.admin.autoscan.activity.result.itemsRemoved"] = "{count, plural, one {{count_text} item removed} other {{count_text} items removed}}",
        ["web.admin.autoscan.activity.result.keptOffline"] = "{count, plural, one {{count_text} kept (storage offline)} other {{count_text} kept (storage offline)}}",
        ["web.admin.autoscan.activity.result.missing"] = "{count, plural, one {{count_text} missing} other {{count_text} missing}}",
        ["web.admin.autoscan.activity.result.new"] = "{count, plural, one {{count_text} new} other {{count_text} new}}",
        ["web.admin.autoscan.activity.result.noChanges"] = "No changes",
        ["web.admin.autoscan.activity.result.noChangesUnchanged"] = "{count, plural, one {No changes · {count_text} unchanged} other {No changes · {count_text} unchanged}}",
        ["web.admin.autoscan.activity.result.skipped"] = "Skipped — overlapping scan in progress",
        ["web.admin.autoscan.activity.result.titlesRemoved"] = "{count, plural, one {{count_text} title removed from library} other {{count_text} titles removed from library}}",
        ["web.admin.autoscan.activity.result.updated"] = "{count, plural, one {{count_text} updated} other {{count_text} updated}}",
        ["web.admin.autoscan.activity.runs.none"] = "No new scan rows were created.",
        ["web.admin.autoscan.activity.runs.noneJoined"] = "No new scan rows were created. The changes joined scans that were already queued or running.",
        ["web.admin.autoscan.activity.scanFilter.cancelled"] = "Cancelled",
        ["web.admin.autoscan.activity.scanFilter.completed"] = "Completed",
        ["web.admin.autoscan.activity.scanFilter.failed"] = "Failed",
        ["web.admin.autoscan.activity.scanFilter.queued"] = "Queued",
        ["web.admin.autoscan.activity.scanFilter.running"] = "Running",
        ["web.admin.autoscan.activity.scanStatus.cancelled"] = "cancelled",
        ["web.admin.autoscan.activity.scanStatus.completed"] = "completed",
        ["web.admin.autoscan.activity.scanStatus.failed"] = "failed",
        ["web.admin.autoscan.activity.scanStatus.queued"] = "queued",
        ["web.admin.autoscan.activity.scanStatus.running"] = "running",
        ["web.admin.autoscan.activity.sourceFallback"] = "Autoscan",
        ["web.admin.autoscan.activity.stats.active"] = "Active",
        ["web.admin.autoscan.activity.stats.latestPoll"] = "Latest poll",
        ["web.admin.autoscan.activity.stats.polling"] = "Polling",
        ["web.admin.autoscan.activity.stats.queued"] = "Queued",
        ["web.admin.autoscan.activity.stats.runningScans"] = "Running scans",
        ["web.admin.autoscan.sources.subtitle.via"] = "via {connection}",
        ["web.admin.common.pagination.polls"] = "{count, plural, one {Showing {from}–{to} of {total} poll} other {Showing {from}–{to} of {total} polls}}",
        ["web.admin.common.pagination.scans"] = "{count, plural, one {Showing {from}–{to} of {total} scan} other {Showing {from}–{to} of {total} scans}}",
        ["web.admin.common.scans.entireLibrary"] = "Entire library",
        ["web.admin.common.scans.mode.file"] = "Single file scan",
        ["web.admin.common.scans.mode.library"] = "Full library scan",
        ["web.admin.common.scans.mode.subtree"] = "Subtree scan",
        ["web.admin.common.scans.processingFiles"] = "Processing files",
        ["web.admin.common.scans.progress"] = "{message} · {processed} / {total} ({percent}%)",
        ["web.admin.common.scans.started"] = "Started",
        ["web.admin.common.scans.startedDaysAgo"] = "{count, plural, one {Started {count_text}d ago} other {Started {count_text}d ago}}",
        ["web.admin.common.scans.startedHoursAgo"] = "{count, plural, one {Started #h ago} other {Started #h ago}}",
        ["web.admin.common.scans.startedJustNow"] = "Started just now",
        ["web.admin.common.scans.startedMinutesAgo"] = "{count, plural, one {Started #m ago} other {Started #m ago}}",
        ["web.admin.common.scans.trigger.autoscan"] = "Autoscan",
        ["web.admin.common.scans.trigger.created"] = "Created",
        ["web.admin.common.scans.trigger.fileChange"] = "File change",
        ["web.admin.common.scans.trigger.manual"] = "Manual",
        ["web.admin.common.scans.trigger.scheduled"] = "Scheduled",
        ["web.admin.common.scans.trigger.updated"] = "Updated",
        ["web.common.scans.cancelFailed"] = "Failed to cancel scans",
        ["web.common.scans.cancelRequested"] = "Scan cancellation requested",
        ["native.cancelConflict"] = "Scan state changed. Reload and review before requesting cancellation again.",
        ["native.cancelUnknown"] = "Cancellation outcome is unknown. Reconcile live scan state; this request will not be replayed.",
        ["native.cancelReviewed"] = "Live state reviewed. Any further cancellation requires a new explicit request.",
        ["native.waitSnapshot"] = "Waiting for a newer live scan snapshot. No cancellation was replayed.",
        ["native.reconcile"] = "Reconcile cancellation",
        ["native.reconcileFailed"] = "Live scan state could not be reloaded. Cancellation remains unresolved; no request was replayed.",
        ["native.restart"] = "Reload from first page",
        ["native.capped"] = "The 100-page history limit was reached. Narrow the search or reload from the first page.",
        ["native.labelsPartial"] = "Some names could not be loaded within the bounded lookup. Identities remain visible.",
        ["native.livePartial"] = "Live scan details exceed the display bound. Counts may include additional scans.",
        ["native.loading"] = "Loading…",
        ["native.readDenied"] = "Access is unavailable. Sign in with an administrator profile and reload.",
        ["native.statusFailed"] = "Autoscan status could not be loaded. Retry to refresh live counts.",
        ["native.page"] = "Page {page}",
    };

    public static string T(string key, object? values = null)
    {
        if (!key.StartsWith("web.", StringComparison.Ordinal) && !key.StartsWith("common.", StringComparison.Ordinal) && !key.StartsWith("native.", StringComparison.Ordinal))
            key = "web.admin.autoscan.activity." + key;
        var arguments = values as IReadOnlyDictionary<string, object?> ?? values?.GetType().GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(values)) ?? new Dictionary<string, object?>();
        var translated = AppStrings.Get(key, arguments);
        return translated != key ? translated : AdminText.Format(English.GetValueOrDefault(key, key), arguments);
    }

    public static string Count(string key, long count) => T(key, AdminText.Count(count));
    public static string Safe(string? text) => LocalLog.RedactDiagnosticText(PlaybackUrlRedactor.RedactSignedCredentials(text ?? ""));
    public static string Duration(long milliseconds)
    {
        if (milliseconds < 1000) return T("duration.milliseconds", new { value = milliseconds });
        if (milliseconds < 60000) return T("duration.seconds", new { value = Math.Round(milliseconds / 1000d, 1, MidpointRounding.AwayFromZero).ToString(AppStrings.FormatCulture) });
        return T("duration.minutesSeconds", new { minutes = milliseconds / 60000, seconds = (long)Math.Round(milliseconds % 60000 / 1000d, MidpointRounding.AwayFromZero) });
    }

    public static string ScanStatus(string status, bool filter = false)
    {
        var label = T((filter ? "scanFilter." : "scanStatus.") + (status == "accepted" ? "queued" : status));
        return !filter && label.Length > 0 ? char.ToUpper(label[0], AppStrings.FormatCulture) + label[1..] : label;
    }
    public static string Mode(string mode) => mode is "library" or "file" or "subtree" ? T("web.admin.common.scans.mode." + mode) : mode;
    public static string Library(string id, AdminAutoscanActivityLabels labels) => labels.Libraries.GetValueOrDefault(id) ?? T("libraryFallback", new { id });

    public static string Source(string? id, string? plugin, string? capability, AdminAutoscanActivityLabels labels, bool poll = false)
    {
        if (string.IsNullOrEmpty(plugin) || string.IsNullOrEmpty(capability))
        {
            if (poll && !string.IsNullOrEmpty(plugin)) return plugin;
            if (poll && !string.IsNullOrEmpty(capability)) return capability;
            return T("sourceFallback");
        }
        var type = labels.Plugins.GetValueOrDefault(plugin + ":" + capability)?.Trim();
        if (string.IsNullOrEmpty(type)) type = capability;
        if (id == null || !labels.Sources.TryGetValue(id, out var source)) return type;
        var provider = source.SourceConfig?.GetValueOrDefault("webhook_provider");
        var webhook = source.DeliveryMode == "webhook";
        var title = !string.IsNullOrWhiteSpace(source.Label) ? source.Label.Trim()
            : webhook && provider is ("sonarr" or "radarr") ? (provider == "sonarr" ? "Sonarr" : "Radarr") : type;
        if (webhook || title != type || source.ConnectionId == null) return title;
        var connection = labels.Connections.GetValueOrDefault(source.ConnectionId)?.Trim();
        return string.IsNullOrEmpty(connection) ? title : title + " · " + T("web.admin.autoscan.sources.subtitle.via", new { connection });
    }

    public static string Result(AdminAutoscanActivityResult? result)
    {
        if (result == null) return "-";
        if (result.Skipped > 0) return T("result.skipped");
        var parts = new List<string>();
        void Add(string key, long count) { if (count > 0) parts.Add(Count("result." + key, count)); }
        Add("new", result.New); Add("updated", result.Updated); Add("missing", result.Missing);
        Add("filesRemoved", result.FilesDeleted); Add("itemsRemoved", result.ItemsDeleted); Add("titlesRemoved", result.MembershipsRemoved);
        Add("keptOffline", result.MissingSkippedProtected); Add("errors", result.Errors);
        return parts.Count > 0 ? string.Join(" · ", parts) : result.Unchanged > 0 ? Count("result.noChangesUnchanged", result.Unchanged) : T("result.noChanges");
    }

    /// <summary>A count printed without digit grouping, as the poll metric strip does.</summary>
    public static string RawCount(string key, long count) => T(key, new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) });
    public static string Number(long value) => AdminText.FormatNumber(value);

    private static readonly IReadOnlyDictionary<string, string> ReasonKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["no_library_match"] = "reason.noLibraryMatch", ["subtree_outside_library"] = "reason.subtreeOutsideLibrary", ["path_outside_library"] = "reason.pathOutsideLibrary",
        ["path_ambiguous"] = "reason.pathAmbiguous", ["library_disabled"] = "reason.libraryDisabled", ["library_root_offline"] = "reason.libraryRootOffline",
        ["unsupported_extension"] = "reason.unsupportedExtension", ["path_permission_denied"] = "reason.pathPermissionDenied", ["path_not_inspectable"] = "reason.pathNotInspectable",
        ["path_not_file_or_dir"] = "reason.pathNotFileOrDir", ["path_required"] = "reason.pathRequired", ["path_ignored"] = "reason.pathIgnored",
        ["resolves_to_library"] = "reason.resolvesToLibrary", ["enqueue_failed"] = "reason.enqueueFailed",
    };

    /// <summary>The plain-language text for one of the resolver's reason codes, or null for a code this client does not know.</summary>
    public static string? ReasonText(string? reason) => reason != null && ReasonKeys.TryGetValue(reason, out var key) ? T(key) : null;
}

