using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Text and presentation rules for Configure › Scheduled Tasks (official <c>AdminTasks.tsx</c>,
/// <c>AdminTaskDetail.tsx</c>, <c>taskTrigger.ts</c>, <c>taskText.ts</c>, <c>taskProgress.ts</c> and
/// <c>TaskStatusBadge.tsx</c>). Keys and English values are copied from the official catalog
/// (<c>web/src/locales/en.json</c> at 92ee051878c2a791940ec9598e2bdd0f04890b3e, decompiled from its
/// message AST to ICU text). The generated interface catalogs exclude <c>web.admin.*</c>, so this
/// domain owns its English table like <see cref="AdminText"/>; other keys (<c>common.*</c>,
/// <c>calendar.*</c>, <c>web.common.*</c>) come from <see cref="AppStrings"/>. The few
/// <c>configure.native.tasks.*</c> keys describe native-only failure states; they are English only.
/// </summary>
public static partial class AdminTasksText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["configure.native.tasks.authenticationRequired"] = "Sign in again, then try again.",
        ["configure.native.tasks.forbidden"] = "Permission to manage scheduled tasks is required.",
        ["configure.native.tasks.profileVerification"] = "Verify the selected profile, then try again.",
        ["configure.native.tasks.unavailable"] = "This server does not currently offer scheduled tasks.",
        ["configure.native.tasks.rateLimited"] = "The server asked you to wait. Try again shortly.",
        ["configure.native.tasks.validation"] = "The server rejected this schedule. Review the triggers and try again.",
        ["configure.native.tasks.invalidResponse"] = "The server returned an invalid task response.",
        ["configure.native.tasks.transport"] = "The task request did not finish. Check the connection and try again.",
        ["configure.native.tasks.failed"] = "The task request failed.",
        ["configure.native.tasks.alreadyRunning"] = "Task is already running on this process",
        ["configure.native.tasks.notRunning"] = "Task is not running on this process",
        ["configure.native.tasks.listFailed"] = "Scheduled tasks could not be loaded.",
        ["configure.native.tasks.removeTrigger"] = "Remove trigger",
        ["configure.native.tasks.dayOfWeek"] = "Day of week",
        ["configure.native.tasks.time"] = "Time",
        ["configure.native.tasks.interval"] = "Interval",
        ["configure.native.tasks.intervalUnit"] = "Interval unit",
        ["configure.native.tasks.triggerType"] = "Trigger type",
        ["configure.native.tasks.intervalInvalid"] = "Enter a whole interval of at least 1 millisecond.",
        ["configure.native.tasks.runtimeInvalid"] = "Enter a number of minutes, or leave it empty for no limit.",
        ["web.admin.common.taskStatus.cancelled"] = "cancelled",
        ["web.admin.common.taskStatus.completed"] = "completed",
        ["web.admin.common.taskStatus.completedWithErrors"] = "Completed with errors",
        ["web.admin.common.taskStatus.failed"] = "failed",
        ["web.admin.common.taskStatus.queued"] = "queued",
        ["web.admin.common.taskStatus.running"] = "running",
        ["web.admin.errors.scheduleRevisionMissing"] = "Schedule revision unavailable. Reload before editing.",
        ["web.admin.tasks.actions.runNow"] = "Run Now",
        ["web.admin.tasks.category.library"] = "Library",
        ["web.admin.tasks.category.metadata"] = "Metadata",
        ["web.admin.tasks.category.system"] = "System",
        ["web.admin.tasks.detail.addTrigger"] = "Add Trigger",
        ["web.admin.tasks.detail.allTasks"] = "All tasks",
        ["web.admin.tasks.detail.attemptBucket"] = "Attempts {range}",
        ["web.admin.tasks.detail.breadcrumb.admin"] = "Admin",
        ["web.admin.tasks.detail.breadcrumb.label"] = "Breadcrumb",
        ["web.admin.tasks.detail.breadcrumb.tasks"] = "Scheduled Tasks",
        ["web.admin.tasks.detail.cancelRun"] = "Cancel",
        ["web.admin.tasks.detail.columns.attempts"] = "Attempts",
        ["web.admin.tasks.detail.columns.item"] = "Item",
        ["web.admin.tasks.detail.columns.lastAttempt"] = "Last attempt",
        ["web.admin.tasks.detail.columns.nextRefresh"] = "Next refresh",
        ["web.admin.tasks.detail.currentSavedSchedule"] = "Current saved schedule:",
        ["web.admin.tasks.detail.dueSamples"] = "Due samples",
        ["web.admin.tasks.detail.editSchedule"] = "Edit Schedule",
        ["web.admin.tasks.detail.executionHistory"] = "Execution history",
        ["web.admin.tasks.detail.history.columns.duration"] = "Duration",
        ["web.admin.tasks.detail.history.columns.error"] = "Error",
        ["web.admin.tasks.detail.history.columns.started"] = "Started",
        ["web.admin.tasks.detail.history.columns.status"] = "Status",
        ["web.admin.tasks.detail.history.empty"] = "No execution history.",
        ["web.admin.tasks.detail.history.failedSteps"] = "Failed steps: {steps}",
        ["web.admin.tasks.detail.history.loadError"] = "History could not load. <restart>Restart history</restart>",
        ["web.admin.tasks.detail.history.loadOlder"] = "Load older executions",
        ["web.admin.tasks.detail.intervalUnit.hours"] = "Hours",
        ["web.admin.tasks.detail.intervalUnit.minutes"] = "Minutes",
        ["web.admin.tasks.detail.intervalUnit.seconds"] = "Seconds",
        ["web.admin.tasks.detail.itemFallback"] = "item",
        ["web.admin.tasks.detail.loadErrorDescription"] = "Something went wrong while loading it. Try again in a moment.",
        ["web.admin.tasks.detail.loadErrorTitle"] = "Couldn't load this task",
        ["web.admin.tasks.detail.loading"] = "Loading...",
        ["web.admin.tasks.detail.manualOnly"] = "Manual only. Select Run Now to start this task.",
        ["web.admin.tasks.detail.maxRuntime"] = "Max runtime (not enforced):",
        ["web.admin.tasks.detail.maxRuntimeLabel"] = "Max runtime:",
        ["web.admin.tasks.detail.maxRuntimeNone"] = "None",
        ["web.admin.tasks.detail.maxRuntimeUnit"] = "minutes",
        ["web.admin.tasks.detail.noDueItems"] = "No due items right now.",
        ["web.admin.tasks.detail.noQueuedDebt"] = "No queued debt.",
        ["web.admin.tasks.detail.noRecentErrors"] = "No recent queue errors.",
        ["web.admin.tasks.detail.noTriggers"] = "No triggers configured.",
        ["web.admin.tasks.detail.notFoundDescription"] = "No scheduled task uses this key. It may have been removed, or the link may be wrong.",
        ["web.admin.tasks.detail.notFoundTitle"] = "Task not found",
        ["web.admin.tasks.detail.queueHealth"] = "Queue health",
        ["web.admin.tasks.detail.reasonBreakdown"] = "Reason breakdown",
        ["web.admin.tasks.detail.recentErrorAttempts"] = "{count, plural, one {{type} · attempts {count_text}} other {{type} · attempts {count_text}}}",
        ["web.admin.tasks.detail.recentErrors"] = "Recent errors",
        ["web.admin.tasks.detail.reviewSchedule"] = "Review current schedule",
        ["web.admin.tasks.detail.scanLibrariesNote"] = "This task history records how long it took to queue per-library scan runs. Actual scan work continues in the background and is tracked from <link>Admin Libraries</link> and Server Activity.",
        ["web.admin.tasks.detail.schedule"] = "Schedule",
        ["web.admin.tasks.detail.triggerType.daily"] = "Daily",
        ["web.admin.tasks.detail.triggerType.interval"] = "Interval",
        ["web.admin.tasks.detail.triggerType.startup"] = "On Startup",
        ["web.admin.tasks.detail.triggerType.weekly"] = "Weekly",
        ["web.admin.tasks.detail.useRevision"] = "Use this revision and keep my draft",
        ["web.admin.tasks.duration.hoursMinutes"] = "{hours}h {minutes}m",
        ["web.admin.tasks.duration.milliseconds"] = "{ms}ms",
        ["web.admin.tasks.duration.minutesSeconds"] = "{minutes}m {seconds}s",
        ["web.admin.tasks.duration.seconds"] = "{seconds}s",
        ["web.admin.tasks.duration.underOneMs"] = "<1ms",
        ["web.admin.tasks.hooks.tasks.cancelRequested"] = "Cancellation requested on this server",
        ["web.admin.tasks.hooks.tasks.outcomeUnknown"] = "The outcome is unknown. Check current task state before trying again.",
        ["web.admin.tasks.hooks.tasks.scheduleChanged"] = "The schedule changed. Your draft is kept. Review the current schedule before submitting a new edit.",
        ["web.admin.tasks.hooks.tasks.scheduleSaved"] = "Schedule saved and applied on this server. Other servers load it on restart.",
        ["web.admin.tasks.hooks.tasks.started"] = "Task started on this server",
        ["web.admin.tasks.lib.taskProgress.percent"] = "{percent}%",
        ["web.admin.tasks.nextRun.inDays"] = "in {days}d",
        ["web.admin.tasks.nextRun.inHours"] = "in {hours}h",
        ["web.admin.tasks.nextRun.inMinutes"] = "in {minutes}m",
        ["web.admin.tasks.nextRun.overdue"] = "overdue",
        ["web.admin.tasks.onDemand.description"] = "These never run on a schedule. Use them for repairs and one-off maintenance.",
        ["web.admin.tasks.onDemand.label"] = "On demand",
        ["web.admin.tasks.page.loading"] = "Loading tasks...",
        ["web.admin.tasks.page.subtitle"] = "View and manage background tasks. You can trigger tasks manually or adjust their schedules, including whether a task runs on server startup. Internal workers that need no attention run automatically and are not listed.",
        ["web.admin.tasks.page.title"] = "Scheduled Tasks",
        ["web.admin.tasks.progress.cancelling"] = "Cancelling...",
        ["web.admin.tasks.progress.running"] = "Running",
        ["web.admin.tasks.refreshMetrics.backlog"] = "Refresh Backlog",
        ["web.admin.tasks.refreshMetrics.due"] = "Due for Refresh",
        ["web.admin.tasks.refreshMetrics.nextClaimTimeout"] = "Next Claim Timeout",
        ["web.admin.tasks.refreshMetrics.processing"] = "Processing",
        ["web.admin.tasks.refreshMetrics.reasonCount"] = "{reason}: {count}",
        ["web.admin.tasks.refreshMetrics.waitingSince"] = "Waiting Since",
        ["web.admin.tasks.refreshReason.coreMetadataIncomplete"] = "Core metadata incomplete",
        ["web.admin.tasks.refreshReason.episodeIncomplete"] = "Episode incomplete",
        ["web.admin.tasks.refreshReason.providerArtwork"] = "Provider artwork",
        ["web.admin.tasks.refreshReason.providerIdIncomplete"] = "Provider ID incomplete",
        ["web.admin.tasks.refreshReason.refreshFailure"] = "Refresh failure",
        ["web.admin.tasks.refreshReason.staleProviderId"] = "Stale provider ID",
        ["web.admin.tasks.refreshReason.trailersRequested"] = "Trailers requested",
        ["web.admin.tasks.row.lastRun"] = "· Last run: {time}",
        ["web.admin.tasks.row.lastRunWithDuration"] = "· Last run: {time} · Duration: {duration}",
        ["web.admin.tasks.row.manualOnly"] = "Manual only",
        ["web.admin.tasks.row.neverRun"] = "· Never run",
        ["web.admin.tasks.row.next"] = "· Next: {next}",
        ["web.admin.tasks.row.noSchedule"] = "No schedule",
        ["web.admin.tasks.row.overdue"] = "Overdue",
        ["web.admin.tasks.row.starting"] = "Starting...",
        ["web.admin.tasks.row.stop"] = "Stop",
        ["web.admin.tasks.row.stopTitle"] = "Stop this task run",
        ["web.admin.tasks.row.stopping"] = "Stopping...",
        ["web.admin.tasks.task.autoscan_poll.description"] = "Poll installed scan-source providers for changes and enqueue rescans",
        ["web.admin.tasks.task.autoscan_poll.name"] = "Autoscan poll",
        ["web.admin.tasks.task.autoscan_webhook_retry.description"] = "Retry durably accepted Autoscan webhook deliveries",
        ["web.admin.tasks.task.autoscan_webhook_retry.name"] = "Autoscan webhook retry",
        ["web.admin.tasks.task.backfill_ebook_metadata.description"] = "Enriches the legacy ebook backlog without competing with scheduled metadata sync",
        ["web.admin.tasks.task.backfill_ebook_metadata.name"] = "Backfill Ebook Metadata",
        ["web.admin.tasks.task.backfill_hdr10_plus.description"] = "Reads the first frame of HDR video scanned before Silo detected HDR10+, once per file, so HDR10+ files are labeled HDR10+ instead of HDR10.",
        ["web.admin.tasks.task.backfill_hdr10_plus.name"] = "Detect HDR10+ Video",
        ["web.admin.tasks.task.backfill_matroska_track_numbers.description"] = "Reads the track list of MKV files with text subtitles scanned before Silo recorded subtitle track numbers, once per file, so apps can show embedded subtitles without server extraction.",
        ["web.admin.tasks.task.backfill_matroska_track_numbers.name"] = "Record Matroska Subtitle Track Numbers",
        ["web.admin.tasks.task.backfill_media_item_aliases.description"] = "Seeds searchable aliases from existing original and localized media titles in resumable batches.",
        ["web.admin.tasks.task.backfill_media_item_aliases.name"] = "Backfill Media Item Aliases",
        ["web.admin.tasks.task.backfill_metadata_images.description"] = "Manually discovers and caches missing provider artwork across the full catalog",
        ["web.admin.tasks.task.backfill_metadata_images.name"] = "Backfill Metadata Images",
        ["web.admin.tasks.task.backup.description"] = "Backs up the database and uploaded files to the backup destination, then deletes backups beyond the configured count",
        ["web.admin.tasks.task.backup.name"] = "Back Up Server",
        ["web.admin.tasks.task.bulk_metadata_enrichment.description"] = "Looks up movies and series with enrichment providers that batch requests, such as MDBList, many titles at a time",
        ["web.admin.tasks.task.bulk_metadata_enrichment.name"] = "Bulk Metadata Enrichment",
        ["web.admin.tasks.task.cache_metadata_images.description"] = "Processes only artwork already queued by scans, refreshes, and metadata changes",
        ["web.admin.tasks.task.cache_metadata_images.name"] = "Cache Metadata Images",
        ["web.admin.tasks.task.chapter_thumbnail_backfill.description"] = "Generates missing chapter thumbnails for opted-in libraries",
        ["web.admin.tasks.task.chapter_thumbnail_backfill.name"] = "Chapter Thumbnail Backfill",
        ["web.admin.tasks.task.check_plugin_updates.description"] = "Fetches plugin repositories and applies or records available plugin updates",
        ["web.admin.tasks.task.check_plugin_updates.name"] = "Check Plugin Updates",
        ["web.admin.tasks.task.cleanup_activity_log.description"] = "Prunes expired activity log rows and partitions",
        ["web.admin.tasks.task.cleanup_activity_log.name"] = "Cleanup Activity Log",
        ["web.admin.tasks.task.cleanup_artwork_revisions.description"] = "Deletes unpublished or displaced immutable artwork revisions after a grace period when no catalog record references them.",
        ["web.admin.tasks.task.cleanup_artwork_revisions.name"] = "Clean Artwork Revisions",
        ["web.admin.tasks.task.cleanup_auth_sessions.description"] = "Deletes login sessions whose expiry has passed, revoked or not",
        ["web.admin.tasks.task.cleanup_auth_sessions.name"] = "Cleanup Expired Login Sessions",
        ["web.admin.tasks.task.cleanup_autoscan_events.description"] = "Prunes old autoscan poll and webhook events from the Activity history",
        ["web.admin.tasks.task.cleanup_autoscan_events.name"] = "Cleanup Autoscan Events",
        ["web.admin.tasks.task.cleanup_catalog_search_index_events.description"] = "Prunes acknowledged catalog search outbox events after the recovery window.",
        ["web.admin.tasks.task.cleanup_catalog_search_index_events.name"] = "Cleanup Catalog Search Index Events",
        ["web.admin.tasks.task.cleanup_chapter_thumbnail_originals.description"] = "Deletes full-size chapter thumbnail images stored by earlier versions. Clients only load the 300px thumbnail; the task stops once storage holds none.",
        ["web.admin.tasks.task.cleanup_chapter_thumbnail_originals.name"] = "Clean Up Full-Size Chapter Thumbnails",
        ["web.admin.tasks.task.cleanup_client_diagnostics.description"] = "Prunes expired client diagnostic reports and orphaned bundles",
        ["web.admin.tasks.task.cleanup_client_diagnostics.name"] = "Cleanup Client Diagnostics",
        ["web.admin.tasks.task.cleanup_device_logins.description"] = "Deletes device sign-in requests long past their expiry",
        ["web.admin.tasks.task.cleanup_device_logins.name"] = "Cleanup Expired Device Sign-in Requests",
        ["web.admin.tasks.task.cleanup_oauth_flows.description"] = "Deletes OAuth sign-in flows long past their expiry",
        ["web.admin.tasks.task.cleanup_oauth_flows.name"] = "Cleanup Expired OAuth Sign-in Flows",
        ["web.admin.tasks.task.cleanup_operational_log.description"] = "Prunes expired operational log rows and partitions",
        ["web.admin.tasks.task.cleanup_operational_log.name"] = "Cleanup Operational Log",
        ["web.admin.tasks.task.cleanup_orphaned_media_items.description"] = "Deletes detached provisional media items that have no files, library memberships, or durable user/sync references.",
        ["web.admin.tasks.task.cleanup_orphaned_media_items.name"] = "Clean Orphaned Media Items",
        ["web.admin.tasks.task.cleanup_policy_decision_log.description"] = "Prunes expired policy decision log rows and partitions",
        ["web.admin.tasks.task.cleanup_policy_decision_log.name"] = "Cleanup Policy Decision Log",
        ["web.admin.tasks.task.cleanup_removed_media_images.description"] = "Deletes the chapter thumbnails and seek previews of media files removed from the catalog, and seek previews that were replaced, after a grace period.",
        ["web.admin.tasks.task.cleanup_removed_media_images.name"] = "Clean Removed Media Images",
        ["web.admin.tasks.task.cleanup_task_history.description"] = "Prunes old background task execution history",
        ["web.admin.tasks.task.cleanup_task_history.name"] = "Cleanup Task History",
        ["web.admin.tasks.task.contribute_markers.description"] = "Sends eligible intros and credits detected on this server to providers with automatic sharing enabled, and manual corrections a save could not deliver to providers that accept corrections.",
        ["web.admin.tasks.task.contribute_markers.name"] = "Share detected markers",
        ["web.admin.tasks.task.database_maintenance.description"] = "Prunes expired activity and policy logs, task history, autoscan events, login sessions, device sign-in requests, OAuth sign-in flows, search index events, and notifications, and prepares upcoming log partitions",
        ["web.admin.tasks.task.database_maintenance.name"] = "Database Maintenance",
        ["web.admin.tasks.task.detect_intro_markers.description"] = "Analyzes files for intros and credits in libraries with marker detection enabled.",
        ["web.admin.tasks.task.detect_intro_markers.name"] = "Detect markers on this server",
        ["web.admin.tasks.task.encode_download_artifacts.description"] = "Encodes queued remux/transcode download artifacts, recovers stranded jobs, and evicts old artifacts",
        ["web.admin.tasks.task.encode_download_artifacts.name"] = "Prepare Download Artifacts",
        ["web.admin.tasks.task.generate_extra_stills.description"] = "Takes a frame from each local extra that has no image file beside it",
        ["web.admin.tasks.task.generate_extra_stills.name"] = "Generate Extra Thumbnails",
        ["web.admin.tasks.task.match_media.description"] = "Matches unmatched media files to metadata from providers",
        ["web.admin.tasks.task.match_media.name"] = "Match Media Files",
        ["web.admin.tasks.task.notifications_retention.description"] = "Prunes old notifications and processed release events according to the retention settings.",
        ["web.admin.tasks.task.notifications_retention.name"] = "Clean Up Notifications",
        ["web.admin.tasks.task.queue_seek_previews.description"] = "Queues seek-bar previews for the video files of libraries that have them turned on, and requeues previews that no longer match their file or settings.",
        ["web.admin.tasks.task.queue_seek_previews.name"] = "Queue Seek Previews",
        ["web.admin.tasks.task.rebuild_catalog_search_index.description"] = "Rebuilds the configured Meilisearch catalog index from PostgreSQL.",
        ["web.admin.tasks.task.rebuild_catalog_search_index.name"] = "Rebuild Catalog Search Index",
        ["web.admin.tasks.task.rebuild_release_interest.description"] = "Recomputes which profiles care about which series (favorites, watchlist, watch progress) for new-episode notifications.",
        ["web.admin.tasks.task.rebuild_release_interest.name"] = "Rebuild Notification Interest",
        ["web.admin.tasks.task.recheck_external_identities.description"] = "Asks the external sign-in provider about accounts that hold API keys or idle sessions, and revokes the access of accounts the provider no longer admits",
        ["web.admin.tasks.task.recheck_external_identities.name"] = "Re-check External Sign-in Accounts",
        ["web.admin.tasks.task.reconcile_artwork_cache.description"] = "Manually verifies cached artwork against object storage; missing records may be reset across the full library and require an explicit metadata image backfill",
        ["web.admin.tasks.task.reconcile_artwork_cache.name"] = "Reconcile Artwork Cache",
        ["web.admin.tasks.task.reconcile_requests.description"] = "Checks approved and active media requests against Radarr, Sonarr, and the Silo catalog",
        ["web.admin.tasks.task.reconcile_requests.name"] = "Reconcile Requests",
        ["web.admin.tasks.task.reconcile_watch_history.description"] = "Re-resolves orphaned watch history rows against the current media catalog using stored stable identities.",
        ["web.admin.tasks.task.reconcile_watch_history.name"] = "Reconcile Watch History",
        ["web.admin.tasks.task.refresh_all_library_metadata.description"] = "Re-fetches provider metadata for every item in all enabled libraries. Runs only when started manually; large libraries can take hours and make many provider requests",
        ["web.admin.tasks.task.refresh_all_library_metadata.name"] = "Refresh All Library Metadata",
        ["web.admin.tasks.task.refresh_anime_ids.description"] = "Downloads the AniDB-based anime list that request routing uses to recognize anime TMDB does not tag",
        ["web.admin.tasks.task.refresh_anime_ids.name"] = "Refresh Anime List",
        ["web.admin.tasks.task.refresh_metadata.description"] = "Refreshes stale metadata from providers for existing media items",
        ["web.admin.tasks.task.refresh_metadata.name"] = "Refresh Metadata",
        ["web.admin.tasks.task.refresh_request_discover_lists.description"] = "Downloads the TMDB and MDBList lists behind the Requests Discover page's list rows",
        ["web.admin.tasks.task.refresh_request_discover_lists.name"] = "Refresh Request Discover Lists",
        ["web.admin.tasks.task.refresh_request_downloads.description"] = "Refreshes the download progress of media requests that Radarr, Sonarr, or another request plugin is downloading",
        ["web.admin.tasks.task.refresh_request_downloads.name"] = "Refresh Request Downloads",
        ["web.admin.tasks.task.refresh_seasonal_lists.description"] = "Downloads the curated MDBList lists behind Seasonal Picks home rows ahead of each holiday",
        ["web.admin.tasks.task.refresh_seasonal_lists.name"] = "Refresh Seasonal Lists",
        ["web.admin.tasks.task.refresh_series_air_dates.description"] = "Updates the latest-aired episode date of series whose next known episode has aired.",
        ["web.admin.tasks.task.refresh_series_air_dates.name"] = "Refresh Series Air Dates",
        ["web.admin.tasks.task.refresh_trending_discover.description"] = "Refreshes the persisted external trending lists (TMDB) for Trending Discover home sections and the calendar's Trending view",
        ["web.admin.tasks.task.refresh_trending_discover.name"] = "Refresh Trending Discover",
        ["web.admin.tasks.task.release_stored_artwork.description"] = "While provider artwork is not saved to storage, points stored artwork back at its providers and queues refreshes for artwork only a provider can replace",
        ["web.admin.tasks.task.release_stored_artwork.name"] = "Release Stored Artwork",
        ["web.admin.tasks.task.repair_provider_id_integrity.description"] = "Repairs drift between media_items provider ID columns and normalized provider ID rows.",
        ["web.admin.tasks.task.repair_provider_id_integrity.name"] = "Repair Provider ID Integrity",
        ["web.admin.tasks.task.scan_libraries.description"] = "Queues durable full-library scans for all enabled libraries; actual scan progress continues in Admin Libraries and Server Activity",
        ["web.admin.tasks.task.scan_libraries.name"] = "Queue Media Library Scans",
        ["web.admin.tasks.task.seed_content_availability.description"] = "Records the existing episode and movie back-catalog as already-released so new-content notifications only fire for items that arrive afterwards.",
        ["web.admin.tasks.task.seed_content_availability.name"] = "Seed Content Availability",
        ["web.admin.tasks.task.setting_mutations_retention.description"] = "Deletes expired settings-mutation idempotency receipts from every user's store.",
        ["web.admin.tasks.task.setting_mutations_retention.name"] = "Clean Up Setting Mutation Receipts",
        ["web.admin.tasks.task.summarize_api_usage.description"] = "Counts API requests per sign-in session for the dashboard's API clients card",
        ["web.admin.tasks.task.summarize_api_usage.name"] = "Summarize API Usage",
        ["web.admin.tasks.task.sweep_artwork_storage.description"] = "Deletes cached artwork objects that no catalog record references, reclaiming revisions the artwork garbage collector never saw",
        ["web.admin.tasks.task.sweep_artwork_storage.name"] = "Sweep Artwork Storage",
        ["web.admin.tasks.task.sweep_orphaned_media_images.description"] = "Finds stored chapter thumbnails and seek previews that nothing references any more and schedules them for deletion.",
        ["web.admin.tasks.task.sweep_orphaned_media_images.name"] = "Sweep Orphaned Media Images",
        ["web.admin.tasks.task.sync_audiobook_metadata.description"] = "Fetches metadata (cover art, overview, narrator) for audiobooks that have not yet been enriched",
        ["web.admin.tasks.task.sync_audiobook_metadata.name"] = "Sync Audiobook Metadata",
        ["web.admin.tasks.task.sync_catalog_search_index.description"] = "Builds or upgrades the Meilisearch catalog index when needed, then syncs pending catalog changes.",
        ["web.admin.tasks.task.sync_catalog_search_index.name"] = "Sync Catalog Search Index",
        ["web.admin.tasks.task.sync_collections.description"] = "Syncs collections that have a scheduled automatic sync",
        ["web.admin.tasks.task.sync_collections.name"] = "Sync Collections",
        ["web.admin.tasks.task.sync_ebook_metadata.description"] = "Fetches metadata for new ebooks and ebooks due for a recurring refresh",
        ["web.admin.tasks.task.sync_ebook_metadata.name"] = "Sync Ebook Metadata",
        ["web.admin.tasks.task.sync_manga_metadata.description"] = "Fetches metadata (cover art, overview, authors) for manga that have not yet been enriched",
        ["web.admin.tasks.task.sync_manga_metadata.name"] = "Sync Manga Metadata",
        ["web.admin.tasks.task.sync_markers.description"] = "Fetches missing markers from enabled online providers and refreshes saved markers. Requires Save to library.",
        ["web.admin.tasks.task.sync_markers.name"] = "Sync online markers",
        ["web.admin.tasks.task.sync_movie_box_sets.description"] = "Looks up which TMDB collection each movie belongs to in libraries with box sets on, and refreshes the collections' names, artwork, and order.",
        ["web.admin.tasks.task.sync_movie_box_sets.name"] = "Sync Movie Box Sets",
        ["web.admin.tasks.task.sync_podcast_feeds.description"] = "Refreshes RSS podcast feeds that are due for polling and upserts new episodes",
        ["web.admin.tasks.task.sync_podcast_feeds.name"] = "Sync Podcast Feeds",
        ["web.admin.tasks.task.sync_user_collections.description"] = "Refreshes profile-owned imported collections (TMDB, MDBList) on their schedule",
        ["web.admin.tasks.task.sync_user_collections.name"] = "Sync User Collections",
        ["web.admin.tasks.task.sync_watch_providers.description"] = "Checks linked watch providers for due automatic sync work",
        ["web.admin.tasks.task.sync_watch_providers.name"] = "Sync Watch Providers",
        ["web.admin.tasks.triggerText.daily"] = "Daily at {time}",
        ["web.admin.tasks.triggerText.everyDays"] = "{count, plural, one {Every {count_text} day} other {Every {count_text} day(s)}}",
        ["web.admin.tasks.triggerText.everyDaysShort"] = "{count, plural, one {Every {count_text}d} other {Every {count_text}d}}",
        ["web.admin.tasks.triggerText.everyHours"] = "{count, plural, one {Every {count_text} hour} other {Every {count_text} hour(s)}}",
        ["web.admin.tasks.triggerText.everyHoursShort"] = "{count, plural, one {Every {count_text}h} other {Every {count_text}h}}",
        ["web.admin.tasks.triggerText.everyMilliseconds"] = "{count, plural, one {Every {count_text} millisecond} other {Every {count_text} millisecond(s)}}",
        ["web.admin.tasks.triggerText.everyMillisecondsShort"] = "{count, plural, one {Every {count_text}ms} other {Every {count_text}ms}}",
        ["web.admin.tasks.triggerText.everyMinutes"] = "{count, plural, one {Every {count_text} minute} other {Every {count_text} minute(s)}}",
        ["web.admin.tasks.triggerText.everyMinutesShort"] = "{count, plural, one {Every {count_text}m} other {Every {count_text}m}}",
        ["web.admin.tasks.triggerText.everySeconds"] = "{count, plural, one {Every {count_text} second} other {Every {count_text} second(s)}}",
        ["web.admin.tasks.triggerText.everySecondsShort"] = "{count, plural, one {Every {count_text}s} other {Every {count_text}s}}",
        ["web.admin.tasks.triggerText.startup"] = "On server startup",
        ["web.admin.tasks.triggerText.startupShort"] = "On startup",
        ["web.admin.tasks.triggerText.weekly"] = "{day, select, 0 {Sunday} 1 {Monday} 2 {Tuesday} 3 {Wednesday} 4 {Thursday} 5 {Friday} 6 {Saturday} other {Day {day}}} at {time}",
        ["web.admin.tasks.triggerText.weeklyShort"] = "{day, select, 0 {Sun} 1 {Mon} 2 {Tue} 3 {Wed} 4 {Thu} 5 {Fri} 6 {Sat} other {Day {day}}} at {time}",
    };

    private static readonly IReadOnlyDictionary<string, string> CategoryKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["library"] = "web.admin.tasks.category.library",
        ["metadata"] = "web.admin.tasks.category.metadata",
        ["system"] = "web.admin.tasks.category.system",
    };

    private static readonly IReadOnlyDictionary<string, string> ReasonKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["episode_incomplete"] = "web.admin.tasks.refreshReason.episodeIncomplete",
        ["stale_provider_id"] = "web.admin.tasks.refreshReason.staleProviderId",
        ["provider_id_incomplete"] = "web.admin.tasks.refreshReason.providerIdIncomplete",
        ["refresh_failure"] = "web.admin.tasks.refreshReason.refreshFailure",
        ["core_metadata_incomplete"] = "web.admin.tasks.refreshReason.coreMetadataIncomplete",
        ["trailers_requested"] = "web.admin.tasks.refreshReason.trailersRequested",
        ["provider_artwork"] = "web.admin.tasks.refreshReason.providerArtwork",
    };

    private static readonly IReadOnlyDictionary<string, string> StatusKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["completed"] = "web.admin.common.taskStatus.completed",
        ["failed"] = "web.admin.common.taskStatus.failed",
        ["cancelled"] = "web.admin.common.taskStatus.cancelled",
    };

    /// <summary>Official CATEGORY_ORDER; tasks of any other category are not grouped.</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } = ["library", "metadata", "system"];

    /// <summary>Interval units, largest first, with their short and long messages (official <c>UNITS</c>).</summary>
    private static readonly (long Size, string Short, string Long)[] Units =
    [
        (86_400_000, "web.admin.tasks.triggerText.everyDaysShort", "web.admin.tasks.triggerText.everyDays"),
        (3_600_000, "web.admin.tasks.triggerText.everyHoursShort", "web.admin.tasks.triggerText.everyHours"),
        (60_000, "web.admin.tasks.triggerText.everyMinutesShort", "web.admin.tasks.triggerText.everyMinutes"),
        (1000, "web.admin.tasks.triggerText.everySecondsShort", "web.admin.tasks.triggerText.everySeconds"),
    ];

    public const string EmDash = "—";

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogKey();

    public static bool Has(string key) => English.ContainsKey(key);
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.ToArray();

    /// <summary>Formats a task message; keys outside this table come from the shared interface catalog.</summary>
    public static string Message(string key, object? values = null)
    {
        var arguments = Arguments(values);
        if (English.TryGetValue(key, out var pattern)) return AdminText.Format(pattern, arguments);
        if (AdminText.Has(key)) return AdminText.Format(AdminText.Pattern(key), arguments);
        return AppStrings.Get(key, arguments, fallback: key);
    }

    /// <summary>The unformatted pattern, for callers that split <c>&lt;tag&gt;</c> markup before inserting values.</summary>
    public static string Pattern(string key) => English.TryGetValue(key, out var pattern) ? pattern : key;

    /// <summary>
    /// A rich catalog message split on its single <c>&lt;tag&gt;…&lt;/tag&gt;</c> run before formatting, so
    /// server values can never introduce a link (official <c>scanLibrariesNote</c>, <c>history.loadError</c>).
    /// </summary>
    public static IReadOnlyList<AdminTextRun> Rich(string key)
    {
        var pattern = Pattern(key);
        var open = pattern.IndexOf('<');
        var close = open < 0 ? -1 : pattern.IndexOf('>', open);
        if (close <= open + 1) return [new("", pattern)];
        var tag = pattern[(open + 1)..close];
        var end = pattern.IndexOf("</" + tag + ">", close, StringComparison.Ordinal);
        if (end < 0 || !tag.All(char.IsLetter)) return [new("", pattern)];
        var runs = new List<AdminTextRun>();
        if (open > 0) runs.Add(new("", pattern[..open]));
        runs.Add(new(tag, pattern[(close + 1)..end]));
        var rest = pattern[(end + tag.Length + 3)..];
        if (rest.Length > 0) runs.Add(new("", rest));
        return runs;
    }

    /// <summary>
    /// Official <c>taskText</c>: Silo's own tasks use their catalog entry keyed by the stable id
    /// (<c>-</c> written as <c>_</c>); a plugin task, or one without an entry, shows the server's English.
    /// </summary>
    public static string TaskName(AdminTaskInfo task) => TaskText(task.Key, "name", task.Name);
    public static string TaskDescription(AdminTaskInfo task) => TaskText(task.Key, "description", task.Description);

    private static string TaskText(string key, string field, string english)
    {
        if (!CatalogKey().IsMatch(key)) return english;
        return English.TryGetValue("web.admin.tasks.task." + key.Replace('-', '_') + "." + field, out var text) ? text : english;
    }

    public static string Category(string category) => CategoryKeys.TryGetValue(category, out var key) ? Message(key) : category;

    /// <summary>Official <c>refreshReasonLabel</c>: an unpublished reason is shown as sent.</summary>
    public static string Reason(string reason) => ReasonKeys.TryGetValue(reason, out var key) ? Message(key) : reason;

    /// <summary>Official <c>describeTrigger</c>: long form on detail pages, short form in the list.</summary>
    public static string DescribeTrigger(AdminTaskTrigger trigger, bool shortStyle = false)
    {
        switch (trigger.Type)
        {
            case AdminTaskTriggerTypes.Interval:
            {
                var milliseconds = trigger.IntervalMs ?? 0;
                foreach (var (size, shortKey, longKey) in Units)
                {
                    if (milliseconds >= size && milliseconds % size == 0 && (shortStyle || size < 86_400_000))
                        return Message(shortStyle ? shortKey : longKey, AdminText.Count(milliseconds / size));
                }
                if (milliseconds == 0)
                    return Message(shortStyle ? "web.admin.tasks.triggerText.everySecondsShort" : "web.admin.tasks.triggerText.everySeconds", AdminText.Count(0));
                return Message(shortStyle ? "web.admin.tasks.triggerText.everyMillisecondsShort" : "web.admin.tasks.triggerText.everyMilliseconds", AdminText.Count(milliseconds));
            }
            case AdminTaskTriggerTypes.Daily:
                return Message("web.admin.tasks.triggerText.daily", new { time = trigger.TimeOfDay ?? "00:00" });
            case AdminTaskTriggerTypes.Weekly:
                return Message(shortStyle ? "web.admin.tasks.triggerText.weeklyShort" : "web.admin.tasks.triggerText.weekly",
                    new { day = (trigger.DayOfWeek ?? 0).ToString(CultureInfo.InvariantCulture), time = trigger.TimeOfDay ?? "00:00" });
            case AdminTaskTriggerTypes.Startup:
                return Message(shortStyle ? "web.admin.tasks.triggerText.startupShort" : "web.admin.tasks.triggerText.startup");
            default:
                return trigger.Type;
        }
    }

    /// <summary>Official <c>describeSchedule</c>: the short triggers joined as a list, or null for none.</summary>
    public static string? DescribeSchedule(IReadOnlyList<AdminTaskTrigger> triggers) =>
        triggers.Count == 0 ? null : JoinList(triggers.Select(trigger => DescribeTrigger(trigger, shortStyle: true)).ToArray());

    /// <summary>English conjunction list (<c>Intl.ListFormat</c> "long"/"conjunction"): "a", "a and b", "a, b, and c".</summary>
    public static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => items[0] + " and " + items[1],
        _ => string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1],
    };

    /// <summary>The list page's duration (official AdminTasks <c>formatDuration</c>, with &lt;1ms and hours).</summary>
    public static string ListDuration(long milliseconds)
    {
        if (milliseconds <= 0) return Message("web.admin.tasks.duration.underOneMs");
        if (milliseconds < 1000) return Message("web.admin.tasks.duration.milliseconds", new { ms = milliseconds });
        var totalSeconds = milliseconds / 1000;
        if (totalSeconds < 60) return Message("web.admin.tasks.duration.seconds", new { seconds = totalSeconds });
        var minutes = totalSeconds / 60;
        if (minutes < 60) return Message("web.admin.tasks.duration.minutesSeconds", new { minutes, seconds = totalSeconds - minutes * 60 });
        return Message("web.admin.tasks.duration.hoursMinutes", new { hours = minutes / 60, minutes = minutes % 60 });
    }

    /// <summary>The detail history's duration (official AdminTaskDetail <c>formatDuration</c>: no hours tier).</summary>
    public static string DetailDuration(long milliseconds)
    {
        if (milliseconds < 1000) return Message("web.admin.tasks.duration.milliseconds", new { ms = milliseconds });
        var totalSeconds = milliseconds / 1000;
        if (totalSeconds < 60) return Message("web.admin.tasks.duration.seconds", new { seconds = totalSeconds });
        var minutes = totalSeconds / 60;
        return Message("web.admin.tasks.duration.minutesSeconds", new { minutes, seconds = totalSeconds - minutes * 60 });
    }

    public static bool IsOverdue(DateTimeOffset nextRunAt, DateTimeOffset now) => nextRunAt < now;

    /// <summary>Official <c>formatNextRun</c>: floored minutes, hours, days, or "overdue".</summary>
    public static string NextRun(DateTimeOffset nextRunAt, DateTimeOffset now)
    {
        var difference = nextRunAt - now;
        if (difference < TimeSpan.Zero) return Message("web.admin.tasks.nextRun.overdue");
        var minutes = (long)Math.Floor(difference.TotalMinutes);
        if (minutes < 60) return Message("web.admin.tasks.nextRun.inMinutes", new { minutes });
        var hours = minutes / 60;
        if (hours < 24) return Message("web.admin.tasks.nextRun.inHours", new { hours });
        return Message("web.admin.tasks.nextRun.inDays", new { days = hours / 24 });
    }

    /// <summary>Official <c>formatRelativeTime(completedAt, {rounding: "floor"})</c> with no absolute cut-off.</summary>
    public static string LastRun(DateTimeOffset completedAt, DateTimeOffset now)
    {
        var delta = now - completedAt;
        var minutes = (long)Math.Floor(delta.TotalMinutes);
        if (minutes < 1) return AppStrings.Get("web.common.date.justNow", fallback: "just now");
        if (minutes < 60) return Ago("web.common.date.minutesAgo", minutes, "m ago");
        var hours = (long)Math.Floor(delta.TotalHours);
        if (hours < 24) return Ago("web.common.date.hoursAgo", hours, "h ago");
        return Ago("web.common.date.daysAgo", (long)Math.Floor(delta.TotalDays), "d ago");

        static string Ago(string key, long count, string suffix) =>
            AppStrings.Get(key, new Dictionary<string, object?> { ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) },
                fallback: count.ToString(AppStrings.FormatCulture) + suffix);
    }

    /// <summary>The list row's last-run summary: with duration when the run reports one (it always does on this API).</summary>
    public static string LastRunSummary(AdminTaskExecution execution, DateTimeOffset now) =>
        Message("web.admin.tasks.row.lastRunWithDuration", new { time = LastRun(execution.CompletedAt, now), duration = ListDuration(execution.DurationMs) });

    /// <summary>Official <c>clampTaskProgress</c>.</summary>
    public static double ClampProgress(double progress) => double.IsFinite(progress) ? Math.Clamp(progress, 0, 100) : 0;

    /// <summary>Official <c>formatTaskProgress</c>: whole numbers as is, otherwise one decimal never shown as 100.0.</summary>
    public static string Progress(double progress)
    {
        var clamped = ClampProgress(progress);
        if (clamped == Math.Floor(clamped)) return Message("web.admin.tasks.lib.taskProgress.percent", new { percent = ((long)clamped).ToString(AppStrings.FormatCulture) });
        var shown = Math.Round(clamped, 1, MidpointRounding.AwayFromZero) >= 100 ? 99.9 : clamped;
        return Message("web.admin.tasks.lib.taskProgress.percent", new { percent = shown.ToString("0.0", AppStrings.FormatCulture) });
    }

    /// <summary>The bar width the official rows draw: at least 2% so a fresh run is visible.</summary>
    public static double ProgressBarPercent(double progress) => Math.Max(ClampProgress(progress), 2);

    /// <summary>Official TaskStatusBadge: a completed <c>contribute_markers</c> run that failed some items.</summary>
    public static bool CompletedWithErrors(AdminTaskExecution execution) =>
        execution.Status == "completed" && execution.TaskKey == "contribute_markers" && execution.Result is { Failed: > 0 };

    /// <summary>The badge label; a status this build does not know is shown as sent.</summary>
    public static string StatusLabel(AdminTaskExecution execution) => CompletedWithErrors(execution)
        ? Message("web.admin.common.taskStatus.completedWithErrors")
        : StatusKeys.TryGetValue(execution.Status, out var key) ? Message(key) : execution.Status;

    /// <summary>The history Error column: failed step names first, else the server's safe message, else an em dash.</summary>
    public static string HistoryError(AdminTaskExecution execution)
    {
        var failed = execution.Steps.Where(step => step.Status == "failed").Select(step => step.Name).ToArray();
        if (failed.Length > 0) return Message("web.admin.tasks.detail.history.failedSteps", new { steps = JoinList(failed) });
        return string.IsNullOrEmpty(execution.ErrorMessage) ? EmDash : execution.ErrorMessage;
    }

    /// <summary>The expanded history row: the typed marker result as the official <c>JSON.stringify(…, null, 2)</c>.</summary>
    public static string? ResultText(AdminTaskExecution execution)
    {
        if (execution.Result is not { } result) return null;
        var text = new StringBuilder();
        text.Append("{\n");
        text.Append("  \"submitted\": ").Append(result.Submitted.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        text.Append("  \"skipped\": ").Append(result.Skipped.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        text.Append("  \"invalid\": ").Append(result.Invalid.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        text.Append("  \"failed\": ").Append(result.Failed.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        text.Append("  \"retry_after_seconds\": ").Append(result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append('}');
        return text.ToString();
    }

    /// <summary>The localized weekday the schedule editor offers; index is the trigger's <c>day_of_week</c> (0 = Sunday).</summary>
    public static string Weekday(int dayOfWeek) => Message("calendar.weekday." + (dayOfWeek == 0 ? 7 : dayOfWeek).ToString(CultureInfo.InvariantCulture));

    /// <summary>A command or schedule failure for a toast or alert. Holds no server text.</summary>
    public static string Failure(AdminFailureKind? failure, bool run = false, bool cancel = false) => failure switch
    {
        AdminFailureKind.Unauthorized => Message("configure.native.tasks.authenticationRequired"),
        AdminFailureKind.Forbidden or AdminFailureKind.DemoRestricted => Message("configure.native.tasks.forbidden"),
        AdminFailureKind.ProfileVerificationRequired => Message("configure.native.tasks.profileVerification"),
        AdminFailureKind.CapabilityUnavailable => Message("configure.native.tasks.unavailable"),
        AdminFailureKind.RateLimited => Message("configure.native.tasks.rateLimited"),
        AdminFailureKind.Validation => Message("configure.native.tasks.validation"),
        AdminFailureKind.InvalidResponse => Message("configure.native.tasks.invalidResponse"),
        AdminFailureKind.Transport or AdminFailureKind.Cancelled => Message("configure.native.tasks.transport"),
        AdminFailureKind.NotFound => Message("web.admin.tasks.detail.notFoundTitle"),
        AdminFailureKind.Conflict when run => Message("configure.native.tasks.alreadyRunning"),
        AdminFailureKind.Conflict when cancel => Message("configure.native.tasks.notRunning"),
        AdminFailureKind.PreconditionFailed => Message("web.admin.tasks.hooks.tasks.scheduleChanged"),
        _ => Message("configure.native.tasks.failed"),
    };

    private static IReadOnlyDictionary<string, object?> Arguments(object? values)
    {
        if (values is null) return new Dictionary<string, object?>();
        if (values is IReadOnlyDictionary<string, object?> ready) return ready;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties()) result[property.Name] = property.GetValue(values);
        return result;
    }
}
