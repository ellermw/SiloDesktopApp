using System.Globalization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Interface text for Admin › Playback › History and Skip Markers. The shared
/// silo-translations catalog excludes <c>web.admin.*</c>, so those keys and
/// their English ICU patterns are copied verbatim from official Silo
/// e61187daaf72e8accad486eb2f632c753ae211d5 <c>web/src/locales/en.json</c>, as
/// the other Admin domains do. Every other key (<c>web.markers.kind.*</c>,
/// <c>web.common.*</c>) is read from the shipped catalog in the interface
/// language through <see cref="AppStrings"/>. <c>admin.native.*</c> keys are
/// native-only English for accessibility, bounded pickers and server compatibility.
/// </summary>
public static class AdminPlaybackAuditText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.activity.page.refresh"] = "Refresh",
        ["web.admin.activity.page.refreshing"] = "Refreshing...",
        ["web.admin.activity.row.ffmpegLogs"] = "FFmpeg Logs",
        ["web.admin.activity.row.viewLogs"] = "View Logs",
        ["web.admin.activity.userFallback"] = "User #{id}",
        ["web.admin.errors.playbackHistoryInvalid"] = "Invalid playback history page. Reload the page.",
        ["web.admin.errors.userPaginationInvalid"] = "Invalid user pagination. Reload the list.",
        ["web.admin.history.column.ended"] = "Ended",
        ["web.admin.history.column.logs"] = "Actions",
        ["web.admin.history.column.media"] = "Media",
        ["web.admin.history.column.method"] = "Method",
        ["web.admin.history.column.profile"] = "Profile",
        ["web.admin.history.column.status"] = "Status",
        ["web.admin.history.column.user"] = "Account",
        ["web.admin.history.column.watchTime"] = "Watch time",
        ["web.admin.history.duration.hoursMinutes"] = "{hours}h {minutes}m",
        ["web.admin.history.duration.minutesSeconds"] = "{minutes}m {seconds}s",
        ["web.admin.history.duration.none"] = "0m",
        ["web.admin.history.duration.seconds"] = "{seconds}s",
        ["web.admin.history.empty.body"] = "No sessions match these filters. Try clearing or adjusting a filter.",
        ["web.admin.history.empty.title"] = "No playback history",
        ["web.admin.history.filter.allAttempts"] = "All statuses",
        ["web.admin.history.filter.allProfiles"] = "All profiles",
        ["web.admin.history.filter.allUsers"] = "All accounts",
        ["web.admin.history.filter.chooseUserFirst"] = "Choose an account first",
        ["web.admin.history.filter.completed"] = "Completed",
        ["web.admin.history.filter.partial"] = "Partial",
        ["web.admin.history.filter.reset"] = "Reset",
        ["web.admin.history.itemFilterActive"] = "Title:",
        ["web.admin.history.loadError"] = "Failed to load playback history",
        ["web.admin.history.mediaType.episode"] = "episode",
        ["web.admin.history.mediaType.movie"] = "movie",
        ["web.admin.history.mediaType.series"] = "series",
        ["web.admin.history.mediaType.unknown"] = "unknown",
        ["web.admin.history.pagination.next"] = "Next",
        ["web.admin.history.pagination.pageSize"] = "{count, plural, one {# row} other {# rows}}",
        ["web.admin.history.pagination.previous"] = "Previous",
        ["web.admin.history.recent.autoRefreshing"] = "Auto-refreshing",
        ["web.admin.history.recent.heading"] = "Recent Playback",
        ["web.admin.history.row.completed"] = "Completed",
        ["web.admin.history.row.estimatedDuration"] = "≈ {duration}",
        ["web.admin.history.row.estimatedTitle"] = "Estimated (recorded before exact watch time was tracked)",
        ["web.admin.history.row.meta"] = "{media_type} · session {session}",
        ["web.admin.history.row.ofDuration"] = "of {duration}",
        ["web.admin.history.row.partial"] = "Partial",
        ["web.admin.history.row.started"] = "started {time}",
        ["web.admin.history.stats.completed"] = "Completed",
        ["web.admin.history.stats.partial"] = "Partial",
        ["web.admin.history.stats.visibleRows"] = "Shown",
        ["web.admin.history.subtitle"] = "Past playback sessions across all accounts and profiles.",
        ["web.admin.history.title"] = "Playback History",
        ["web.admin.markers.history.actionCleared"] = "Cleared",
        ["web.admin.markers.history.actionSet"] = "Set",
        ["web.admin.markers.history.column.actor"] = "Actor",
        ["web.admin.markers.history.column.change"] = "Change",
        ["web.admin.markers.history.column.item"] = "Item",
        ["web.admin.markers.history.column.request"] = "Request",
        ["web.admin.markers.history.column.segment"] = "Segment",
        ["web.admin.markers.history.column.when"] = "When",
        ["web.admin.markers.history.empty"] = "No marker edits recorded.",
        ["web.admin.markers.history.fileFallback"] = "File {id}",
        ["web.admin.markers.history.impersonatedVia"] = "via {name}",
        ["web.admin.markers.history.itemWithType"] = "{title} ({type})",
        ["web.admin.markers.history.limitLabel"] = "History limit",
        ["web.admin.markers.history.limitOption"] = "{count, plural, one {# row} other {# rows}}",
        ["web.admin.markers.history.loadFailed"] = "Failed to load marker history.",
        ["web.admin.markers.history.mediaFileFallback"] = "Media file {id}",
        ["web.admin.markers.history.noRequestId"] = "No request id",
        ["web.admin.markers.history.rangeChange"] = "{before} -> {after}",
        ["web.admin.markers.history.rangeNone"] = "none",
        ["web.admin.markers.history.refresh"] = "Refresh",
        ["web.admin.markers.history.subtitle"] = "Recent manual intro, recap, credits, and preview marker edits across the server.",
        ["web.admin.markers.history.title"] = "Marker History",
        ["web.admin.markers.history.unknownUser"] = "Unknown user",
        ["web.admin.users.detail.common.episodeCode"] = "S{season}E{episode}",

        // Native-only text for accessible controls, picker truncation and server compatibility.
        ["admin.native.playbackAudit.usersTruncated"] = "Only the first {count_text} accounts are listed.",
        ["admin.native.playbackAudit.profilesTruncated"] = "Only the first {count_text} profiles are listed.",
        ["admin.native.playbackAudit.userFilterLabel"] = "Account",
        ["admin.native.playbackAudit.profileFilterLabel"] = "Profile",
        ["admin.native.playbackAudit.completionFilterLabel"] = "Status",
        ["admin.native.playbackAudit.seriesChip"] = "Series: {value}",
        ["admin.native.playbackAudit.libraryChip"] = "Library: {value}",
        ["admin.native.playbackAudit.pageSizeLabel"] = "Rows per page",
        ["admin.native.playbackAudit.loading"] = "Loading",
        ["admin.native.playbackAudit.richFiltersUnavailable"] = "The requested Playback History filters are unavailable on this server. Reset the filters to view Playback History.",
        ["admin.native.playbackAudit.seriesFilterUnavailable"] = "Filtering Playback History by series is unavailable on this server. Reset the filters to view Playback History.",
        ["admin.native.playbackAudit.detailUnavailable"] = "This server does not provide session details for sessions outside the current page.",
        // Official Silo c59798116 (web/src/locales/en.json, compiled catalog rendered back to ICU): Playback History rework (columns, filters, paging, session detail, end reasons).
        ["web.admin.history.column.client"] = "Client",
        ["web.admin.history.detail.action.ffmpegLogs"] = "FFmpeg logs",
        ["web.admin.history.detail.action.openMedia"] = "Open title",
        ["web.admin.history.detail.action.sessionLogs"] = "Session logs",
        ["web.admin.history.detail.column.delivered"] = "Delivered",
        ["web.admin.history.detail.column.source"] = "Source",
        ["web.admin.history.detail.column.stream"] = "Detail",
        ["web.admin.history.detail.field.account"] = "Account",
        ["web.admin.history.detail.field.client"] = "Client",
        ["web.admin.history.detail.field.clientIp"] = "Client IP",
        ["web.admin.history.detail.field.endPosition"] = "Stopped at",
        ["web.admin.history.detail.field.endReason"] = "How it ended",
        ["web.admin.history.detail.field.ended"] = "Ended",
        ["web.admin.history.detail.field.library"] = "Library",
        ["web.admin.history.detail.field.network"] = "Network",
        ["web.admin.history.detail.field.paused"] = "Paused",
        ["web.admin.history.detail.field.platform"] = "Platform",
        ["web.admin.history.detail.field.profile"] = "Profile",
        ["web.admin.history.detail.field.route"] = "Route",
        ["web.admin.history.detail.field.sessionId"] = "Session ID",
        ["web.admin.history.detail.field.started"] = "Started",
        ["web.admin.history.detail.field.watched"] = "Watch time",
        ["web.admin.history.detail.keyboardHint"] = "Use ↑ and ↓ to move between sessions",
        ["web.admin.history.detail.loadFailed"] = "Failed to load session details.",
        ["web.admin.history.detail.loading"] = "Loading session details…",
        ["web.admin.history.detail.nextSession"] = "Next session",
        ["web.admin.history.detail.notFound"] = "This playback session could not be found.",
        ["web.admin.history.detail.preUpgradeNotice"] = "Recorded before detailed playback info was tracked. File details and server route aren’t available for this session.",
        ["web.admin.history.detail.previousSession"] = "Previous session",
        ["web.admin.history.detail.row.audio"] = "Audio",
        ["web.admin.history.detail.row.bitrate"] = "Bitrate",
        ["web.admin.history.detail.row.container"] = "Container",
        ["web.admin.history.detail.row.subtitles"] = "Subtitles",
        ["web.admin.history.detail.row.video"] = "Video",
        ["web.admin.history.detail.section.clientRoute"] = "Account, device & route",
        ["web.admin.history.detail.section.reasons"] = "Why it didn’t direct play",
        ["web.admin.history.detail.section.streams"] = "Source vs. delivered",
        ["web.admin.history.detail.section.timing"] = "Timing & watch progress",
        ["web.admin.history.detail.subtitlesBurnIn"] = "Burned in",
        ["web.admin.history.detail.subtitlesEmbedded"] = "Embedded",
        ["web.admin.history.detail.subtitlesHls"] = "Streamed (HLS)",
        ["web.admin.history.detail.subtitlesNone"] = "None",
        ["web.admin.history.detail.subtitlesSidecar"] = "External file",
        ["web.admin.history.detail.title"] = "Session details",
        ["web.admin.history.endReason.aborted"] = "Ended by server",
        ["web.admin.history.endReason.expired"] = "Timed out",
        ["web.admin.history.endReason.replaced"] = "Switched stream",
        ["web.admin.history.endReason.stopped"] = "Stopped",
        ["web.admin.history.endReason.terminated"] = "Stopped by admin",
        ["web.admin.history.filter.activeChip.client"] = "Client: {value}",
        ["web.admin.history.filter.activeChip.codec"] = "Codec: {value}",
        ["web.admin.history.filter.activeChip.dateRange"] = "Date: {from} – {to}",
        ["web.admin.history.filter.activeChip.fromDate"] = "From {from}",
        ["web.admin.history.filter.activeChip.platform"] = "Platform: {value}",
        ["web.admin.history.filter.activeChip.reason"] = "Reason: {value}",
        ["web.admin.history.filter.activeChip.toDate"] = "Until {to}",
        ["web.admin.history.filter.allLibraries"] = "All libraries",
        ["web.admin.history.filter.allLocations"] = "All networks",
        ["web.admin.history.filter.allMethods"] = "All methods",
        ["web.admin.history.filter.burnInOnly"] = "Subtitle burn-in",
        ["web.admin.history.filter.locationLocal"] = "Local",
        ["web.admin.history.filter.locationRemote"] = "Remote",
        ["web.admin.history.filter.removeChip"] = "Remove {label} filter",
        ["web.admin.history.filter.searchPlaceholder"] = "Search titles, accounts, clients, or session IDs…",
        ["web.admin.history.pagination.firstPage"] = "First page",
        ["web.admin.history.pagination.summary"] = "{count, plural, one {Showing {count_text} row} other {Showing {count_text} rows}}",
    };

    /// <summary>Keys from the shared catalog this domain shows (localized there).</summary>
    public static IReadOnlyList<string> CatalogKeys { get; } =
    [
        "web.markers.kind.intro", "web.markers.kind.recap", "web.markers.kind.credits", "web.markers.kind.preview",
        "web.common.activity.title.file", "web.common.date.justNow", "web.common.date.minutesAgo", "web.common.date.hoursAgo",
        "web.common.date.daysAgo",
    ];

    /// <summary>Keys copied from the official English catalog (excluding native-only keys).</summary>
    public static IReadOnlyCollection<string> OfficialKeys { get; } = English.Keys.Where(key => key.StartsWith("web.", StringComparison.Ordinal)).ToArray();

    public static bool Has(string key) => English.ContainsKey(key);

    /// <summary>The English pattern of a copied key (catalog-coverage tests), or null.</summary>
    public static string? EnglishPattern(string key) => English.TryGetValue(key, out var value) ? value : null;

    /// <summary>Formats a domain key; any other key is read from the shared catalog in the interface language.</summary>
    public static string T(string key, params (string Name, object? Value)[] values)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values) arguments[name] = value;
        if (English.TryGetValue(key, out var pattern)) return AdminText.Format(pattern, arguments);
        return AppStrings.Get(key, arguments, fallback: key);
    }

    /// <summary>A count with its interface-locale text, as WebUI <c>localeCount</c>.</summary>
    public static (string, object?)[] Count(long count) => [("count", count), ("count_text", count.ToString("N0", CultureInfo.CurrentCulture))];

    /// <summary>WebUI <c>formatRelativeTime(value, {rounding: "floor"})</c>; null when unparsable.</summary>
    public static string? Relative(string? value, DateTimeOffset now)
    {
        if (!TryParseInstant(value, out var date)) return null;
        var diff = (now - date).TotalMilliseconds;
        var minutes = Math.Floor(diff / 60_000);
        if (minutes < 1) return T("web.common.date.justNow");
        if (minutes < 60) return T("web.common.date.minutesAgo", Count((long)minutes));
        var hours = Math.Floor(diff / 3_600_000);
        if (hours < 24) return T("web.common.date.hoursAgo", Count((long)hours));
        return T("web.common.date.daysAgo", Count((long)Math.Floor(diff / 86_400_000)));
    }

    /// <summary>Parses an RFC 3339 instant from the server; false for anything else.</summary>
    public static bool TryParseInstant(string? value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out instant)
        && !string.IsNullOrWhiteSpace(value);
}
