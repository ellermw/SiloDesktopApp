using System.Globalization;
using System.Reflection;
using System.Text;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Message-key text for the Admin Monitor shell, server-activity indicator and
/// Activity page. The keys and English values are copied verbatim from the
/// pinned Silo WebUI catalog (<c>web/src/locales/en.json</c>, upstream
/// 1a99f7beffcea3850f067df000c319f2704fd23b) so a later localization
/// foundation can swap this table for the shared catalog without touching the
/// callers. This helper is deliberately isolated: Beta has no localization
/// foundation yet and this must not import one.
/// <para>ICU subset: <c>{arg}</c>, <c>{arg, plural, =0 {..} one {..} other {..}}</c>
/// with <c>#</c>, and <c>{arg, select, a {..} other {..}}</c>.</para>
/// </summary>
public static class AdminText
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // General navigation: official WebUI c11b0a96.
        ["web.admin.settings.search.general.description"] = "The server\u0027s name and public address, the proxies in front of it, and Redis.",
        ["web.admin.settings.search.general.label"] = "General",
        ["web.admin.settings.search.general.setting.checkConnection"] = "Check connection",
        ["web.admin.settings.search.general.setting.connectionUrl"] = "Connection URL",
        ["web.admin.settings.search.general.setting.database"] = "Database",
        ["web.admin.settings.search.general.setting.databaseNumber"] = "Database number",
        ["web.admin.settings.search.general.setting.identity"] = "Identity",
        ["web.admin.settings.search.general.setting.localNetworkAddress"] = "Local network address",
        ["web.admin.settings.search.general.setting.loginSubtitle"] = "Login subtitle",
        ["web.admin.settings.search.general.setting.maximumPostgresConnections"] = "Maximum Postgres connections",
        ["web.admin.settings.search.general.setting.network"] = "Network",
        ["web.admin.settings.search.general.setting.redis"] = "Redis",
        ["web.admin.settings.search.general.setting.serverName"] = "Server name",
        ["web.admin.settings.search.general.setting.showOnTheLocalNetwork"] = "Show on the local network",
        ["web.admin.settings.search.general.setting.siloPublicUrl"] = "Silo public URL",
        ["web.admin.settings.search.general.setting.trustedProxies"] = "Trusted proxies",
        ["web.admin.settings.search.general.setting.useRedis"] = "Use Redis",
        ["web.admin.shell.nav.section.configure"] = "Configure",
        ["web.admin.shell.nav.topic.server"] = "Server",
        // Manage navigation: official WebUI 1452ea2d; keys and ICU patterns preserved.
        ["web.admin.common.title.header.accessGroups"] = "Access Groups",
        ["web.admin.common.title.header.autoscan"] = "Autoscan",
        ["web.admin.common.title.header.autoscanActivity"] = "Autoscan Activity",
        ["web.admin.common.title.header.collections"] = "Collections",
        ["web.admin.common.title.header.devices"] = "Devices",
        ["web.admin.common.title.header.downloads"] = "Downloads",
        ["web.admin.common.title.header.history"] = "Playback History",
        ["web.admin.common.title.header.historyImport"] = "History Import",
        ["web.admin.common.title.header.libraries"] = "Libraries",
        ["web.admin.common.title.header.markerHistory"] = "Marker History",
        ["web.admin.common.title.header.nodes"] = "Nodes",
        ["web.admin.common.title.header.requests"] = "Requests",
        ["web.admin.common.title.header.sections"] = "Sections",
        ["web.admin.common.title.header.subtitles"] = "Subtitles",
        ["web.admin.common.title.header.user"] = "User",
        ["web.admin.common.title.header.users"] = "Users",
        ["web.admin.common.title.tab.accessGroups"] = "Admin Access Groups",
        ["web.admin.common.title.tab.autoscan"] = "Admin Autoscan",
        ["web.admin.common.title.tab.autoscanActivity"] = "Admin Autoscan Activity",
        ["web.admin.common.title.tab.collectionEdit"] = "Edit Admin Collection",
        ["web.admin.common.title.tab.collectionNew"] = "New Admin Collection",
        ["web.admin.common.title.tab.collections"] = "Admin Collections",
        ["web.admin.common.title.tab.devices"] = "Admin Devices",
        ["web.admin.common.title.tab.downloads"] = "Admin Downloads",
        ["web.admin.common.title.tab.history"] = "Admin Playback History",
        ["web.admin.common.title.tab.historyImport"] = "Admin History Import",
        ["web.admin.common.title.tab.libraries"] = "Admin Libraries",
        ["web.admin.common.title.tab.markerHistory"] = "Admin Marker History",
        ["web.admin.common.title.tab.nodes"] = "Admin Nodes",
        ["web.admin.common.title.tab.requests"] = "Admin Requests",
        ["web.admin.common.title.tab.sections"] = "Admin Sections",
        ["web.admin.common.title.tab.subtitles"] = "Admin Subtitles",
        ["web.admin.common.title.tab.user"] = "Admin User",
        ["web.admin.common.title.tab.users"] = "Admin Users",
        ["web.admin.requests.page.overridesMoved.description"] = "Open an account to set its request approval and limit. Access groups set them for everyone in the group.",
        ["web.admin.requests.page.overridesMoved.title"] = "Request limits moved to each account",
        ["web.admin.settings.search.autoscan.description"] = "How often Autoscan checks polled sources, and the servers its sources connect to.",
        ["web.admin.settings.search.autoscan.label"] = "Autoscan",
        ["web.admin.settings.search.autoscan.setting.addConnection"] = "Add connection",
        ["web.admin.settings.search.autoscan.setting.connections"] = "Connections",
        ["web.admin.settings.search.autoscan.setting.debounce"] = "Debounce",
        ["web.admin.settings.search.autoscan.setting.defaultCheckInterval"] = "Default check interval",
        ["web.admin.settings.search.autoscan.setting.pollingDefaults"] = "Polling defaults",
        ["web.admin.settings.search.downloads.description"] = "Offline downloads, per-user and server-wide limits, and prepared copies.",
        ["web.admin.settings.search.downloads.label"] = "Downloads",
        ["web.admin.settings.search.downloads.setting.allowDownloads"] = "Allow downloads",
        ["web.admin.settings.search.downloads.setting.defaultStorageBudgetPerLocation"] = "Default storage budget per location",
        ["web.admin.settings.search.downloads.setting.diskCeiling"] = "Disk ceiling (%)",
        ["web.admin.settings.search.downloads.setting.downloads"] = "Downloads",
        ["web.admin.settings.search.downloads.setting.downloadsAtOncePerUser"] = "Downloads at once per user",
        ["web.admin.settings.search.downloads.setting.downloadsPerPeriod"] = "Downloads per period",
        ["web.admin.settings.search.downloads.setting.filesPreparedAtOnce"] = "Files prepared at once",
        ["web.admin.settings.search.downloads.setting.keepCachedFilesFor"] = "Keep cached files for",
        ["web.admin.settings.search.downloads.setting.periodLength"] = "Period length",
        ["web.admin.settings.search.downloads.setting.perUser"] = "Per user",
        ["web.admin.settings.search.downloads.setting.perUserBandwidth"] = "Per-user bandwidth",
        ["web.admin.settings.search.downloads.setting.prepareDeviceFriendlyCopies"] = "Prepare device-friendly copies",
        ["web.admin.settings.search.downloads.setting.preparedFileDirectory"] = "Prepared file directory",
        ["web.admin.settings.search.downloads.setting.prepareLocallyWhenWorkersAreUnavailable"] = "Prepare locally when workers are unavailable",
        ["web.admin.settings.search.downloads.setting.serverBandwidth"] = "Server bandwidth",
        ["web.admin.settings.search.downloads.setting.wholeServer"] = "Whole server",
        ["web.admin.settings.search.library.description"] = "Which versions a title lists, scanning, maturity limits, ratings, and subtitle sync.",
        ["web.admin.settings.search.library.label"] = "Library & Metadata",
        ["web.admin.settings.search.library.setting.concurrentSyncsPerTranscodeNode"] = "Concurrent syncs per transcode node",
        ["web.admin.settings.search.library.setting.general"] = "General",
        ["web.admin.settings.search.library.setting.imageEncodingWorkers"] = "Image encoding workers",
        ["web.admin.settings.search.library.setting.matcherBatchSize"] = "Matcher batch size",
        ["web.admin.settings.search.library.setting.matcherWorkers"] = "Matcher workers",
        ["web.admin.settings.search.library.setting.maturityLimits"] = "Maturity limits",
        ["web.admin.settings.search.library.setting.personLookupsPerMinute"] = "Person lookups per minute",
        ["web.admin.settings.search.library.setting.ratings"] = "Ratings",
        ["web.admin.settings.search.library.setting.scannerWorkers"] = "Scanner workers",
        ["web.admin.settings.search.library.setting.scanning"] = "Scanning",
        ["web.admin.settings.search.library.setting.showAllLibraryVersionsOnDetailPage"] = "Show all library versions on detail page",
        ["web.admin.settings.search.library.setting.subtitleSync"] = "Subtitle sync",
        ["web.admin.settings.search.library.setting.syncSubtitlesAutomatically"] = "Sync subtitles automatically",
        ["web.admin.settings.search.library.setting.titlesWithNoAgeRating"] = "Titles with no age rating",
        ["web.admin.settings.search.library.setting.whereToAnalyzeAudio"] = "Where to analyze audio",
        ["web.admin.settings.search.playback.description"] = "Watch thresholds, transcoding, chapter thumbnails and seek previews, and skip markers.",
        ["web.admin.settings.search.playback.label"] = "Playback",
        ["web.admin.settings.search.playback.setting.allow4kTranscoding"] = "Allow 4K transcoding",
        ["web.admin.settings.search.playback.setting.allowHevcEncoding"] = "Allow HEVC encoding",
        ["web.admin.settings.search.playback.setting.bufferAheadSeconds"] = "Buffer ahead (seconds)",
        ["web.admin.settings.search.playback.setting.chapterThumbnailWorkers"] = "Chapter thumbnail workers",
        ["web.admin.settings.search.playback.setting.detectCredits"] = "Detect credits",
        ["web.admin.settings.search.playback.setting.detectIntros"] = "Detect intros",
        ["web.admin.settings.search.playback.setting.detectionWorkers"] = "Detection workers",
        ["web.admin.settings.search.playback.setting.detectMarkersOnThisServer"] = "Detect markers on this server",
        ["web.admin.settings.search.playback.setting.enableHardwareHdrToneMapping"] = "Enable Hardware HDR Tone Mapping",
        ["web.admin.settings.search.playback.setting.enableSoftwareHdrToneMapping"] = "Enable Software HDR Tone Mapping",
        ["web.admin.settings.search.playback.setting.ffmpegPath"] = "FFmpeg path",
        ["web.admin.settings.search.playback.setting.findMarkersOnPlayback"] = "Find markers on playback",
        ["web.admin.settings.search.playback.setting.generateChapterThumbnailsOn"] = "Generate chapter thumbnails on",
        ["web.admin.settings.search.playback.setting.generateSeekPreviewsOn"] = "Generate seek previews on",
        ["web.admin.settings.search.playback.setting.gpuDevices"] = "GPU devices",
        ["web.admin.settings.search.playback.setting.hardwareAcceleration"] = "Hardware acceleration",
        ["web.admin.settings.search.playback.setting.hdrChapterThumbnails"] = "HDR chapter thumbnails",
        ["web.admin.settings.search.playback.setting.hdrHandling"] = "HDR handling",
        ["web.admin.settings.search.playback.setting.markerSource"] = "Marker source",
        ["web.admin.settings.search.playback.setting.markWatchedAt"] = "Mark watched at (%)",
        ["web.admin.settings.search.playback.setting.previewImages"] = "Preview images",
        ["web.admin.settings.search.playback.setting.previewImageWidth"] = "Preview image width",
        ["web.admin.settings.search.playback.setting.saveOnlineMarkers"] = "Save online markers",
        ["web.admin.settings.search.playback.setting.seekPreviewInterval"] = "Seek preview interval",
        ["web.admin.settings.search.playback.setting.seekPreviewWorkers"] = "Seek preview workers",
        ["web.admin.settings.search.playback.setting.shareDetectedMarkers"] = "Share detected markers",
        ["web.admin.settings.search.playback.setting.showInContinueWatchingAfter"] = "Show in Continue Watching after (%)",
        ["web.admin.settings.search.playback.setting.skipMarkers"] = "Skip markers",
        ["web.admin.settings.search.playback.setting.softwareHdrToneMapping"] = "Software HDR tone mapping",
        ["web.admin.settings.search.playback.setting.syncOnlineMarkers"] = "Sync online markers",
        ["web.admin.settings.search.playback.setting.throttleTranscoding"] = "Throttle transcoding",
        ["web.admin.settings.search.playback.setting.transcodeBackBuffer"] = "Transcode back buffer",
        ["web.admin.settings.search.playback.setting.transcodeDirectory"] = "Transcode directory",
        ["web.admin.settings.search.playback.setting.transcoding"] = "Transcoding",
        ["web.admin.settings.search.playback.setting.videoTranscoding"] = "Video transcoding",
        ["web.admin.settings.search.playback.setting.watchBehavior"] = "Watch behavior",
        ["web.admin.settings.search.requests.description"] = "Request limits and approval, the Sonarr and Radarr servers, and where requests go.",
        ["web.admin.settings.search.requests.label"] = "Requests",
        ["web.admin.settings.search.requests.setting.4kServer"] = "4K server",
        ["web.admin.settings.search.requests.setting.addRule"] = "Add rule",
        ["web.admin.settings.search.requests.setting.addServer"] = "Add server",
        ["web.admin.settings.search.requests.setting.advanced"] = "Advanced",
        ["web.admin.settings.search.requests.setting.allowRequests"] = "Allow requests",
        ["web.admin.settings.search.requests.setting.alsoRequestA4kVersionOfEveryTitle"] = "Also request a 4K version of every title",
        ["web.admin.settings.search.requests.setting.apiKey"] = "API key",
        ["web.admin.settings.search.requests.setting.approval"] = "Approval",
        ["web.admin.settings.search.requests.setting.everythingElse"] = "Everything else",
        ["web.admin.settings.search.requests.setting.general"] = "General",
        ["web.admin.settings.search.requests.setting.limitWindow"] = "Limit window",
        ["web.admin.settings.search.requests.setting.requestLimit"] = "Request limit",
        ["web.admin.settings.search.requests.setting.requestQueue"] = "Request queue",
        ["web.admin.settings.search.requests.setting.requestTitlesAddedToAWatchlist"] = "Request titles added to a watchlist",
        ["web.admin.settings.search.requests.setting.servers"] = "Servers",
        ["web.admin.settings.search.requests.setting.standard"] = "Standard",
        ["web.admin.settings.search.requests.setting.tryATitle"] = "Try a title",
        ["web.admin.settings.search.requests.setting.whereRequestsGo"] = "Where requests go",
        ["web.admin.shell.nav.item.history"] = "History",
        ["web.admin.shell.nav.item.homeSections"] = "Home Sections",
        ["web.admin.shell.nav.item.preparedFiles"] = "Prepared Files",
        ["web.admin.shell.nav.item.queue"] = "Queue",
        ["web.admin.shell.nav.item.skipMarkers"] = "Skip Markers",
        ["web.admin.shell.nav.page.accessGroups.description"] = "Shared access defaults: libraries, downloads, streams, requests, permissions.",
        ["web.admin.shell.nav.page.accessGroups.label"] = "Access Groups",
        ["web.admin.shell.nav.page.autoscanActivity.description"] = "Changes Autoscan received and the scans they started.",
        ["web.admin.shell.nav.page.autoscanActivity.label"] = "Activity",
        ["web.admin.shell.nav.page.autoscanSources.description"] = "What Autoscan watches for changes: folders, webhooks, and servers.",
        ["web.admin.shell.nav.page.autoscanSources.label"] = "Sources",
        ["web.admin.shell.nav.page.collections.description"] = "Curated and smart collection management.",
        ["web.admin.shell.nav.page.collections.label"] = "Collections",
        ["web.admin.shell.nav.page.devices.description"] = "Registered devices, overrides, and per-device settings.",
        ["web.admin.shell.nav.page.devices.label"] = "Devices",
        ["web.admin.shell.nav.page.downloads.description"] = "Prepared download storage on the server and nodes, and device copies.",
        ["web.admin.shell.nav.page.downloads.label"] = "Downloads",
        ["web.admin.shell.nav.page.historyImport.description"] = "Admin history import mappings and bulk import runs.",
        ["web.admin.shell.nav.page.historyImport.label"] = "History Import",
        ["web.admin.shell.nav.page.libraries.description"] = "Media libraries, paths, scanning, and catalog import.",
        ["web.admin.shell.nav.page.libraries.label"] = "Libraries",
        ["web.admin.shell.nav.page.markers.description"] = "Intro, recap, and credits marker history.",
        ["web.admin.shell.nav.page.markers.label"] = "Markers",
        ["web.admin.shell.nav.page.nodes.description"] = "Stream nodes, remote worker status, and where playback work runs.",
        ["web.admin.shell.nav.page.nodes.label"] = "Nodes",
        ["web.admin.shell.nav.page.nodes.setting.directPlayEgress"] = "Direct play egress",
        ["web.admin.shell.nav.page.nodes.setting.remuxEgress"] = "Remux egress",
        ["web.admin.shell.nav.page.nodes.setting.remuxExecution"] = "Remux execution",
        ["web.admin.shell.nav.page.nodes.setting.routingPreset"] = "Routing preset",
        ["web.admin.shell.nav.page.nodes.setting.videoTranscodeEgress"] = "Video transcode egress",
        ["web.admin.shell.nav.page.nodes.setting.videoTranscodeExecution"] = "Video transcode execution",
        ["web.admin.shell.nav.page.playbackHistory.description"] = "Historical playback events across users and profiles.",
        ["web.admin.shell.nav.page.playbackHistory.label"] = "Playback History",
        ["web.admin.shell.nav.page.requestDiscover.description"] = "The rows on the Requests page's Discover tab: charts, filters, and lists.",
        ["web.admin.shell.nav.page.requestDiscover.label"] = "Discover Page",
        ["web.admin.shell.nav.page.requests.description"] = "Review, approve, and decline media requests.",
        ["web.admin.shell.nav.page.requests.label"] = "Requests",
        ["web.admin.shell.nav.page.sections.description"] = "Rows on Home and on library pages.",
        ["web.admin.shell.nav.page.sections.label"] = "Sections",
        ["web.admin.shell.nav.page.subtitleFiles.description"] = "Downloaded subtitle records and subtitle admin tools.",
        ["web.admin.shell.nav.page.subtitleFiles.label"] = "Subtitle Files",
        ["web.admin.shell.nav.page.users.description"] = "Accounts, roles, profile settings, and access.",
        ["web.admin.shell.nav.page.users.label"] = "Users",
        ["web.admin.shell.nav.section.manage"] = "Manage",
        ["web.admin.shell.nav.topic.autoscan"] = "Autoscan",
        ["web.admin.shell.nav.topic.downloads"] = "Downloads",
        ["web.admin.shell.nav.topic.libraries"] = "Libraries",
        ["web.admin.shell.nav.topic.playback"] = "Playback",
        ["web.admin.shell.nav.topic.requests"] = "Requests",
        ["web.admin.shell.nav.topic.users"] = "Users",
        ["web.admin.shell.sidebar.pendingRequests"] = "{count, plural, one {, {count_text} needs approval} other {, {count_text} need approval}}",
        ["web.admin.shell.sidebar.settingAttention"] = "{label} {state}",
        ["web.shell.sideNav.attention"] = ", {label}",

        // App sidebar footer
        ["web.shell.sidebar.admin"] = "Admin",
        ["common.cancel"] = "Cancel",
        ["common.retry"] = "Retry",
        ["web.ui.close"] = "Close",


        // Admin shell
        ["web.admin.shell.drawer.close"] = "Close admin navigation",
        ["web.admin.shell.drawer.title"] = "Admin Navigation",
        ["web.admin.shell.mobileHeader.dashboardTitle"] = "Dashboard",
        ["web.admin.shell.mobileHeader.eyebrow"] = "Admin",
        ["web.admin.shell.mobileHeader.openNavigation"] = "Open admin navigation",
        ["web.admin.shell.nav.item.clientDiagnostics"] = "Client Diagnostics",
        ["web.admin.shell.nav.item.serverLog"] = "Server Log",
        ["web.admin.shell.nav.item.settings"] = "Settings",
        ["web.admin.shell.nav.page.activity.description"] = "Live streams and current playback sessions.",
        ["web.admin.shell.nav.page.activity.label"] = "Activity",
        ["web.admin.shell.nav.page.dashboard.description"] = "Live sessions, content health, and server activity.",
        ["web.admin.shell.nav.page.dashboard.label"] = "Dashboard",
        ["web.admin.shell.nav.page.diagnostics.description"] = "Uploaded client crash reports, device context, and debug bundles.",
        ["web.admin.shell.nav.page.diagnostics.label"] = "Diagnostics",
        ["web.admin.shell.nav.page.logs.description"] = "Server log stream and operational output.",
        ["web.admin.shell.nav.page.logs.label"] = "Logs",
        ["web.admin.shell.nav.section.monitor"] = "Monitor",
        ["web.admin.settings.search.logging.description"] = "What the server logs, and how much log history the database keeps.",
        ["web.admin.settings.search.logging.label"] = "Logging",
        ["web.admin.shell.nav.topic.logs"] = "Logs",
        ["web.admin.shell.search.label"] = "Search admin sections",
        ["web.admin.shell.search.title"] = "Search admin sections ({shortcut})",
        ["web.admin.shell.sidebar.backToApp"] = "Back to App",
        ["web.admin.shell.sidebar.build.builtAt"] = "Built {built_at}",
        ["web.admin.shell.sidebar.build.dev"] = "dev build",
        ["web.admin.shell.sidebar.build.heading"] = "Build",
        ["web.admin.shell.sidebar.build.loadFailed"] = "load failed",
        ["web.admin.shell.sidebar.build.loading"] = "loading...",
        ["web.admin.shell.sidebar.home"] = "Go to app home",
        ["web.admin.shell.sidebar.liveSessions"] = "{count, plural, one {{count_text} live} other {{count_text} live}}",
        ["web.admin.shell.sidebar.navigation"] = "Admin navigation",
        ["web.admin.shell.sidebar.search"] = "Search",
        ["web.admin.shell.skipToContent"] = "Skip to content",
        ["web.admin.common.title.tab.admin"] = "Admin",
        ["web.admin.common.title.tab.activity"] = "Admin Activity",
        ["web.admin.common.title.tab.logs"] = "Admin Logs",
        ["web.admin.common.title.tab.diagnostics"] = "Admin Client Diagnostics",
        ["web.admin.common.title.tab.settings"] = "Admin Settings",
        ["web.admin.common.title.header.activity"] = "Activity",
        ["web.admin.common.title.header.logs"] = "Logs",
        ["web.admin.common.title.header.diagnostics"] = "Client Diagnostics",
        ["web.admin.common.title.header.settings"] = "Settings",
        ["web.admin.common.restartBanner.description"] = "Saved settings that are only read at startup take effect after a restart. Playback sessions will reconnect.",
        ["web.admin.common.restartBanner.later"] = "Later",
        ["web.admin.common.restartBanner.restart"] = "Restart server",
        ["web.admin.common.restartBanner.title"] = "Restart required",
        ["web.admin.common.restartServer.alreadyRequested"] = "Server restart is already in progress.",
        ["web.admin.common.restartServer.confirm"] = "Restart",
        ["web.admin.common.restartServer.confirmDescription"] = "Silo will shut down. It comes back on its own only if a service manager such as Docker or systemd restarts it; otherwise, start it again yourself. Active streams will be interrupted.",
        ["web.admin.common.restartServer.confirmTitle"] = "Restart server?",
        ["web.admin.common.restartServer.failed"] = "Could not confirm the restart. Check server status or restart manually.",
        ["web.admin.common.restartServer.shuttingDown"] = "Silo is shutting down to restart.",

        ["web.admin.homeRows.commandDialog.searchPlaceholder"] = "Search admin sections...",
        ["web.admin.homeRows.commandDialog.resultsLabel"] = "Admin sections",
        ["web.admin.homeRows.commandDialog.noMatches"] = "No matching admin sections",
        ["web.admin.homeRows.commandDialog.matchCount"] = "{count, plural, one {# match} other {# matches}}",
        ["web.admin.homeRows.commandDialog.sectionCount"] = "{count, plural, one {# admin section} other {# admin sections}}",

        // Server activity indicator
        ["web.admin.common.serverActivity.connecting"] = "Connecting…",
        ["web.admin.common.serverActivity.disconnected"] = "Disconnected",
        ["web.admin.common.serverActivity.empty"] = "No active server activity",
        ["web.admin.common.serverActivity.failedRecent"] = "{count, plural, one {{count_text} failed in the last 24 hours} other {{count_text} failed in the last 24 hours}}",
        ["web.admin.common.serverActivity.libraryFallback"] = "Library #{id}",
        ["web.admin.common.serverActivity.moreRunning"] = "{count, plural, one {{count_text} more running} other {{count_text} more running}}",
        ["web.admin.common.serverActivity.moreScans"] = "{count, plural, one {{count_text} more scan queued} other {{count_text} more scans queued}}",
        ["web.admin.common.serverActivity.moreScansAtLeast"] = "{count, plural, one {At least {count_text} more scan queued} other {At least {count_text} more scans queued}}",
        ["web.admin.common.serverActivity.noPreparations"] = "No downloads being prepared",
        ["web.admin.common.serverActivity.noScans"] = "No active scans",
        ["web.admin.common.serverActivity.noStreams"] = "No active streams",
        ["web.admin.common.serverActivity.noTasks"] = "No running tasks",
        ["web.admin.common.serverActivity.paused"] = "{count, plural, one {{count_text} paused} other {{count_text} paused}}",
        ["web.admin.common.serverActivity.percent"] = "{percent}%",
        ["web.admin.common.serverActivity.preparing"] = "Preparing downloads",
        ["web.admin.common.serverActivity.processingFiles"] = "Processing files",
        ["web.admin.common.serverActivity.progressLabel"] = "{title} progress",
        ["web.admin.common.serverActivity.queued"] = "{count, plural, one {{count_text} queued} other {{count_text} queued}}",
        ["web.admin.common.serverActivity.retrying"] = "{count, plural, one {{count_text} waiting to retry} other {{count_text} waiting to retry}}",
        ["web.admin.common.serverActivity.running"] = "Running",
        ["web.admin.common.serverActivity.scanMode.file"] = "Single file",
        ["web.admin.common.serverActivity.scanMode.library"] = "Full library",
        ["web.admin.common.serverActivity.scanMode.subtree"] = "Subtree",
        ["web.admin.common.serverActivity.scanProgress"] = "{message} · {processed} / {total} ({percent}%)",
        ["web.admin.common.serverActivity.scanQueued"] = "Queued",
        ["web.admin.common.serverActivity.scanning"] = "Scanning…",
        ["web.admin.common.serverActivity.scans"] = "Scans",
        ["web.admin.common.serverActivity.streamCount"] = "{count, plural, one {{count_text} {method}} other {{count_text} {method}}}",
        ["web.admin.common.serverActivity.streams"] = "Streams",
        ["web.admin.common.serverActivity.tasks"] = "Tasks",
        ["web.admin.common.serverActivity.title"] = "Server Activity",
        ["web.admin.common.serverActivity.trigger.active"] = "{count, plural, one {Server activity: {count_text} active} other {Server activity: {count_text} active}}",
        ["web.admin.common.serverActivity.trigger.atLeast"] = "{count, plural, one {Server activity: at least {count_text} active} other {Server activity: at least {count_text} active}}",
        ["web.admin.common.serverActivity.trigger.idle"] = "Server activity",
        ["web.admin.common.serverActivity.viewAll"] = "View all",
        ["web.admin.tasks.lib.taskProgress.percent"] = "{percent}%",
        ["web.admin.downloads.preparation.compactEpisodeTitle"] = "{series} {code}",
        ["web.admin.downloads.preparation.episodeCode"] = "S{season}E{episode}",
        ["web.admin.downloads.preparation.fileFallback"] = "File #{id}",
        ["web.admin.downloads.preparation.kind.remux"] = "Remux",
        ["web.admin.downloads.preparation.kind.transcode"] = "Transcode",
        ["web.admin.downloads.preparation.kind.transcodeTarget"] = "Transcode {target}",
        ["web.admin.downloads.preparation.running.encoding"] = "Encoding",
        ["web.admin.downloads.preparation.running.remuxing"] = "Remuxing",
        ["web.admin.downloads.preparation.remaining.hours"] = "{hours, plural, one {about {hours_text} h left} other {about {hours_text} h left}}",
        ["web.admin.downloads.preparation.remaining.hoursMinutes"] = "{hours, plural, one {about {hours_text} h {minutes} min left} other {about {hours_text} h {minutes} min left}}",
        ["web.admin.downloads.preparation.remaining.minutes"] = "{minutes, plural, one {about # min left} other {about # min left}}",
        ["web.admin.downloads.preparation.remaining.underOneMinute"] = "under 1 min left",
        ["web.admin.downloads.preparation.worker.localServer"] = "Local server",
        ["web.admin.downloads.preparation.worker.node"] = "Node",
        ["web.admin.downloads.preparation.worker.nodeWithId"] = "Node {id}",
        ["web.admin.downloads.preparation.worker.server"] = "Server",

        // Activity page
        ["web.admin.activity.client.agent"] = "Agent",
        ["web.admin.activity.client.app"] = "App",
        ["web.admin.activity.client.heading"] = "Client",
        ["web.admin.activity.client.unknown"] = "Unknown client",
        ["web.admin.activity.column.actions"] = "Actions",
        ["web.admin.activity.column.playback"] = "Playback",
        ["web.admin.activity.column.route"] = "Route",
        ["web.admin.activity.column.stream"] = "Stream",
        ["web.admin.activity.column.time"] = "Time",
        ["web.admin.activity.column.user"] = "User",
        ["web.admin.activity.decision.copy"] = "Copy",
        ["web.admin.activity.decision.direct"] = "Direct",
        ["web.admin.activity.decision.remux"] = "Remux",
        ["web.admin.activity.decision.transcode"] = "Transcode",
        ["web.admin.activity.decision.unknown"] = "Unknown",
        ["web.admin.activity.detail.audioOutput"] = "→ {output}",
        ["web.admin.activity.detail.autoSwitched"] = "Auto-switched from {source}",
        ["web.admin.activity.detail.conversion"] = "{source} → {output}",
        ["web.admin.activity.detail.copied"] = "Copied without re-encoding",
        ["web.admin.activity.detail.delivered"] = "Delivered",
        ["web.admin.activity.detail.detail"] = "Detail",
        ["web.admin.activity.detail.mode"] = "Mode",
        ["web.admin.activity.detail.noAudioConversion"] = "No audio conversion",
        ["web.admin.activity.detail.noVideoConversion"] = "No video conversion",
        ["web.admin.activity.detail.originalContainer"] = "Original container",
        ["web.admin.activity.detail.output"] = "Output → {output}",
        ["web.admin.activity.detail.repackaged"] = "Repackaged for streaming; output container not reported",
        ["web.admin.activity.detail.source"] = "Source",
        ["web.admin.activity.detail.toneMapping"] = "Tone mapping",
        ["web.admin.activity.detail.transcoding"] = "Transcoding",
        ["web.admin.activity.empty.noMatches"] = "No streams match your filters",
        ["web.admin.activity.filter.clear"] = "Clear filters",
        ["web.admin.activity.filter.movies"] = "Movies",
        ["web.admin.activity.filter.placeholder"] = "Filter by user, media, client, or node...",
        ["web.admin.activity.filter.series"] = "Series",
        ["web.admin.activity.filter.showing"] = "{total, plural, one {Showing {shown} of {total_text} stream} other {Showing {shown} of {total_text} streams}}",
        ["web.admin.activity.ipLookup.column.firstSeen"] = "First Seen",
        ["web.admin.activity.ipLookup.column.lastSeen"] = "Last Seen",
        ["web.admin.activity.ipLookup.column.requests"] = "Requests",
        ["web.admin.activity.ipLookup.column.user"] = "User",
        ["web.admin.activity.ipLookup.empty"] = "No users found for {ip} in the last 30 days.",
        ["web.admin.activity.ipLookup.heading"] = "IP Lookup",
        ["web.admin.activity.ipLookup.loadError"] = "Could not load IP history.",
        ["web.admin.activity.ipLookup.loadMore"] = "Load more",
        ["web.admin.activity.ipLookup.loadMoreError"] = "Could not load more history.",
        ["web.admin.activity.ipLookup.placeholder"] = "IP lookup (e.g. 203.0.113.50)",
        ["web.admin.activity.ipLookup.reload"] = "Reload history",
        ["web.admin.activity.ipLookup.searching"] = "Searching...",
        ["web.admin.activity.ipLookup.submit"] = "Lookup",
        ["web.admin.activity.page.liveCount"] = "{count, plural, one {{count_text} live} other {{count_text} live}}",
        ["web.admin.activity.page.loading"] = "Loading activity...",
        ["web.admin.activity.page.loadFailed"] = "Could not load activity.",
        ["web.admin.activity.page.stale"] = "Could not refresh activity. Showing previously loaded streams; they may be out of date.",
        ["web.admin.activity.page.noStreams"] = "No active streams",
        ["web.admin.activity.page.refresh"] = "Refresh",
        ["web.admin.activity.page.refreshing"] = "Refreshing...",
        ["web.admin.activity.page.subtitle"] = "{count, plural, one {{count_text} active stream} other {{count_text} active streams}} across {nodes, plural, one {{nodes_text} node} other {{nodes_text} nodes}}",
        ["web.admin.activity.page.title"] = "Activity",
        ["web.admin.activity.panel.event"] = "event",
        ["web.admin.activity.panel.heading"] = "Playback",
        ["web.admin.activity.panel.loadingFfmpeg"] = "Loading ffmpeg output…",
        ["web.admin.activity.panel.noFfmpegRows"] = "No ffmpeg rows yet for this session. If the session is direct play or remux without a transcode worker, nothing will appear here.",
        ["web.admin.activity.panel.openFfmpegLogs"] = "Open full FFmpeg logs",
        ["web.admin.activity.panel.refreshing"] = "Refreshing…",
        ["web.admin.activity.panel.restart"] = "restart {count}",
        ["web.admin.activity.panel.stderr"] = "stderr",
        ["web.admin.activity.panel.streamDetails"] = "Stream details",
        ["web.admin.activity.panel.streamDetailsWithConsole"] = "Stream details and live transcode console",
        ["web.admin.activity.realtime.connecting"] = "Connecting",
        ["web.admin.activity.realtime.disconnected"] = "Disconnected",
        ["web.admin.activity.realtime.heading"] = "Realtime Updates",
        ["web.admin.activity.realtime.live"] = "Live",
        ["web.admin.activity.route.localServer"] = "Local server",
        ["web.admin.activity.route.node"] = "Node",
        ["web.admin.activity.route.nodeId"] = "Node #{id}",
        ["web.admin.activity.route.proxy"] = "Proxy",
        ["web.admin.activity.route.server"] = "Server",
        ["web.admin.activity.route.transcode"] = "Transcode",
        ["web.admin.activity.route.unknownProxyNode"] = "Unknown proxy node",
        ["web.admin.activity.route.unknownTranscodeNode"] = "Unknown transcode node",
        ["web.admin.activity.row.audio"] = "Audio",
        ["web.admin.activity.row.container"] = "Container",
        ["web.admin.activity.row.details"] = "Details",
        ["web.admin.activity.row.ffmpeg"] = "FFmpeg",
        ["web.admin.activity.row.ffmpegLogs"] = "FFmpeg Logs",
        ["web.admin.activity.row.hideFfmpeg"] = "Hide FFmpeg",
        ["web.admin.activity.row.methodBadge"] = "Playback method: {method}",
        ["web.admin.activity.row.paused"] = "Paused",
        ["web.admin.activity.row.playing"] = "Playing",
        ["web.admin.activity.row.sessionActive"] = "Session active {elapsed}",
        ["web.admin.activity.row.video"] = "Video",
        ["web.admin.activity.row.viewLogs"] = "View Logs",
        ["web.admin.activity.sort.group"] = "Sort streams",
        ["web.admin.activity.sort.heading"] = "Sort",
        ["web.admin.activity.summary.byNode"] = "By Routing Node",
        ["web.admin.activity.summary.hlsOutput"] = "{container} (HLS)",
        ["web.admin.activity.summary.methodDistribution"] = "Playback method distribution",
        ["web.admin.activity.summary.methodFilter"] = "{method} {count}",
        ["web.admin.activity.summary.methodHeading"] = "Playback Method",
        ["web.admin.activity.summary.methodSegment"] = "{method}: {count}",
        ["web.admin.activity.summary.unknownOutputContainer"] = "Unknown output container",
        ["web.admin.activity.summary.unknownSource"] = "Unknown source",
        ["web.admin.activity.toneMap.hardware"] = "Hardware",
        ["web.admin.activity.toneMap.hardwareBadge"] = "HW Tone map",
        ["web.admin.activity.toneMap.software"] = "Software",
        ["web.admin.activity.toneMap.softwareBadge"] = "SW Tone map",
        ["web.admin.activity.transcodeMode.audioTranscode"] = "Audio Transcode",
        ["web.admin.activity.transcodeMode.hardware"] = "HW {accel}",
        ["web.admin.activity.transcodeMode.pending"] = "HW/SW pending",
        ["web.admin.activity.transcodeMode.software"] = "SW",
        ["web.admin.activity.transcodeMode.unknown"] = "HW/SW unknown",
        ["web.admin.activity.userFallback"] = "User #{id}",
        ["web.admin.errors.activityCursorRepeated"] = "Activity cursor repeated. Reload history.",
        ["web.admin.errors.activityItemsInvalid"] = "Invalid activity items. Reload history.",
        ["web.admin.errors.activityPageInvalid"] = "Invalid activity page. Reload history.",
        ["web.admin.errors.sessionsTooMany"] = "Too many sessions to display. Reload the list.",
        ["web.common.activity.method.direct.description"] = "The original file is delivered without modification.",
        ["web.common.activity.method.direct.label"] = "Direct Play",
        ["web.common.activity.method.directStream.description"] = "Video is copied without re-encoding; audio is transcoded.",
        ["web.common.activity.method.directStream.label"] = "Direct Stream",
        ["web.common.activity.method.remux.description"] = "Audio and video are copied into a streaming container without re-encoding.",
        ["web.common.activity.method.remux.label"] = "Remux",
        ["web.common.activity.method.transcode.description"] = "Video is re-encoded; audio may be copied or transcoded.",
        ["web.common.activity.method.transcode.label"] = "Transcode",
        ["web.common.activity.method.unknown.description"] = "The server has not reported a recognized playback method.",
        ["web.common.activity.method.unknown.label"] = "Unknown",
        // Common catalog on purpose (upstream 5a2be566): Activity and the dashboard tile share the line.
        ["web.common.activity.subtitle.episode"] = "{code} · {series}",
        ["web.detail.episodeCode"] = "S{season}E{episode}",
        ["web.common.activity.subtitle.movie"] = "Movie",
        ["web.common.activity.subtitle.series"] = "Series",
        ["web.common.activity.title.file"] = "File #{id}",
        ["web.player.jellyfinSession.label"] = "JF",
        ["web.player.jellyfinSession.title"] = "Jellyfin client",
        ["web.player.routeBadge.apiServer"] = "API server",
        ["web.player.routeBadge.apiServerNodeTitle"] = "{serves, select, yes {API server: {node} (serves media)} other {API server: {node}}}",
        ["web.player.routeBadge.apiServerTitle"] = "{serves, select, yes {API server (serves media)} other {API server}}",
        ["web.player.routeBadge.defaultNetwork"] = "Default",
        ["web.player.routeBadge.defaultNetworkTitle"] = "Playback uses the default network (LAN, public URL, or reverse proxy).",
        ["web.player.routeBadge.local"] = "Local",
        ["web.player.routeBadge.locationAria"] = "Stream location: {location, select, local {Local} other {Remote}}",
        ["web.player.routeBadge.locationTitle"] = "The server classified this stream as {location, select, local {local} remote {remote} other {{location}}} for bitrate policy.",
        ["web.player.routeBadge.network"] = "Network",
        ["web.player.routeBadge.networkAria"] = "Network: {name}",
        ["web.player.routeBadge.networkTitle"] = "Playback uses the {name} network.",
        ["web.player.routeBadge.nodeTitle"] = "{label}: {name}",
        ["web.player.routeBadge.remote"] = "Remote",
        ["web.player.routeBadge.unknownNetwork"] = "Unknown",
        ["web.player.routeBadge.unknownNetworkTitle"] = "This session did not report its access network.",

        // Session actions
        ["web.admin.sessions.playback.action.message"] = "Message…",
        ["web.admin.sessions.playback.action.pause"] = "Pause",
        ["web.admin.sessions.playback.action.resume"] = "Resume",
        ["web.admin.sessions.playback.action.stop"] = "Stop",
        ["web.admin.sessions.playback.action.terminate"] = "Terminate",
        ["web.admin.sessions.playback.failure.conflictReason"] = "the session state changed",
        ["web.admin.sessions.playback.failure.ended"] = "This playback session has already ended.",
        ["web.admin.sessions.playback.failure.generic"] = "Failed to send session command",
        ["web.admin.sessions.playback.failure.notApplied"] = "The {action, select, pause {pause} resume {resume} stop {stop} message {message} other {terminate}} command was not applied: {reason}",
        ["web.admin.sessions.playback.failure.staleAuthority"] = "Your session changed. Reload and try again.",
        ["web.admin.sessions.playback.failure.unavailable"] = "Playback control is unavailable on this server.",
        ["web.admin.sessions.playback.menu.ariaLabel"] = "Session actions",
        ["web.admin.sessions.playback.menu.titleLimited"] = "Limited Playback Actions",
        ["web.admin.sessions.playback.menu.titlePlayback"] = "Playback Actions",
        ["web.admin.sessions.playback.menu.titleSession"] = "Session Actions",
        ["web.admin.sessions.playback.messageDialog.description"] = "Send a custom message to {name} during playback.",
        ["web.admin.sessions.playback.messageDialog.placeholder"] = "Server restart in 5 minutes. Please finish this episode soon.",
        ["web.admin.sessions.playback.messageDialog.send"] = "Send Message",
        ["web.admin.sessions.playback.messageDialog.title"] = "Send Message",
        ["web.admin.sessions.playback.messageDialog.userFallback"] = "User #{id}",
        ["web.admin.sessions.playback.toast.messageReplayed"] = "Your earlier message request was already accepted. Send again to send a new message.",
        ["web.admin.sessions.playback.toast.messageRequired"] = "Message is required",
        ["web.admin.sessions.playback.toast.messageSent"] = "Message sent",
        ["web.admin.sessions.playback.toast.pauseFallback"] = "Pause could not reach the player directly. Silo will end the session shortly instead.",
        ["web.admin.sessions.playback.toast.pauseSent"] = "Pause command sent",
        ["web.admin.sessions.playback.toast.replayed"] = "{action, select, pause {Your earlier pause request was already accepted. Choose pause again to send a new command.} resume {Your earlier resume request was already accepted. Choose resume again to send a new command.} other {Your earlier stop request was already accepted. Choose stop again to send a new command.}}",
        ["web.admin.sessions.playback.toast.replayedFallback"] = "{action, select, pause {Your earlier pause request was already accepted. Silo could not reach the player and scheduled the session to end. Choose pause again to send a new command.} resume {Your earlier resume request was already accepted. Silo could not reach the player and scheduled the session to end. Choose resume again to send a new command.} other {Your earlier stop request was already accepted. Silo could not reach the player and scheduled the session to end. Choose stop again to send a new command.}}",
        ["web.admin.sessions.playback.toast.resumeFallback"] = "Resume could not reach the player directly. Silo will end the session shortly instead.",
        ["web.admin.sessions.playback.toast.resumeSent"] = "Resume command sent",
        ["web.admin.sessions.playback.toast.stopFallback"] = "Stop could not reach the player directly. Silo will end the session shortly instead.",
        ["web.admin.sessions.playback.toast.stopSent"] = "Stop command sent",
        ["web.admin.sessions.playback.toast.terminateFallback"] = "Terminate could not reach the player directly. Silo will end the session shortly instead.",
        ["web.admin.sessions.playback.toast.terminateReceipt"] = "{revoked, select, already {Playback authority was already revoked} revoked {Playback authority revoked} other {Playback authority was not revoked}}; {notified, select, yes {the player was told to stop} other {the player could not be reached and will stop when it next contacts the server}}.",
        ["web.admin.sessions.playback.toast.terminateSent"] = "Terminate command sent",
        ["web.admin.sessions.playback.unsupportedControl"] = "This session does not support live pause, resume, or messages. Stop and Terminate can still end playback.",
    };

    /// <summary>Every key this helper owns, for catalog-coverage tests.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = English.Keys.ToArray();

    public static bool Has(string key) => English.ContainsKey(key);

    /// <summary>Formats <paramref name="key"/> with values read from an anonymous object or dictionary.</summary>
    public static string T(string key, object? values = null)
    {
        var pattern = English.TryGetValue(key, out var text) ? text : key;
        return Format(pattern, ToDictionary(values));
    }

    /// <summary>Count values printed in the interface locale's digits, as WebUI's <c>localeCount</c>.</summary>
    public static object Count(long count) => new Dictionary<string, object?>
    {
        ["count"] = count,
        ["count_text"] = FormatNumber(count),
    };

    public static string FormatNumber(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static IReadOnlyDictionary<string, object?> ToDictionary(object? values)
    {
        if (values is null) return new Dictionary<string, object?>();
        if (values is IReadOnlyDictionary<string, object?> ready) return ready;
        if (values is IDictionary<string, object?> dictionary) return new Dictionary<string, object?>(dictionary);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            result[property.Name] = property.GetValue(values);
        return result;
    }

    /// <summary>Formats an ICU message pattern (the subset the admin catalog uses).</summary>
    public static string Format(string pattern, IReadOnlyDictionary<string, object?> values)
    {
        var position = 0;
        return FormatUntil(pattern, ref position, values, pound: null, stopAtBrace: false);
    }

    private static string FormatUntil(string pattern, ref int position, IReadOnlyDictionary<string, object?> values,
        string? pound, bool stopAtBrace)
    {
        var output = new StringBuilder();
        while (position < pattern.Length)
        {
            var c = pattern[position];
            if (c == '}' && stopAtBrace) return output.ToString();
            if (c == '#' && pound != null) { output.Append(pound); position++; continue; }
            if (c != '{') { output.Append(c); position++; continue; }
            position++; // past '{'
            var name = ReadToken(pattern, ref position);
            SkipSpace(pattern, ref position);
            if (position < pattern.Length && pattern[position] == '}')
            {
                position++;
                output.Append(ValueText(values.TryGetValue(name, out var simple) ? simple : null));
                continue;
            }
            Expect(pattern, ref position, ',');
            var kind = ReadToken(pattern, ref position);
            SkipSpace(pattern, ref position);
            if (kind is "number")
            {
                Expect(pattern, ref position, '}');
                output.Append(ValueText(values.TryGetValue(name, out var number) ? number : null));
                continue;
            }
            Expect(pattern, ref position, ',');
            values.TryGetValue(name, out var raw);
            string? chosen = null;
            var chosenStart = -1;
            var otherStart = -1;
            while (true)
            {
                SkipSpace(pattern, ref position);
                if (position >= pattern.Length) throw new FormatException("Unterminated ICU argument.");
                if (pattern[position] == '}') { position++; break; }
                var selector = ReadToken(pattern, ref position);
                SkipSpace(pattern, ref position);
                Expect(pattern, ref position, '{');
                var start = position;
                SkipBalanced(pattern, ref position);
                if (selector == "other") otherStart = start;
                if (chosen == null && Matches(kind, selector, raw)) { chosen = selector; chosenStart = start; }
            }
            var bodyStart = chosenStart >= 0 ? chosenStart : otherStart;
            if (bodyStart < 0) continue;
            var nested = bodyStart;
            var nestedPound = kind is "plural" or "selectordinal" ? ValueText(raw) : pound;
            output.Append(FormatUntil(pattern, ref nested, values, nestedPound, stopAtBrace: true));
        }
        return output.ToString();
    }

    private static bool Matches(string kind, string selector, object? raw)
    {
        if (kind is "plural" or "selectordinal")
        {
            var number = ToDouble(raw);
            if (selector.StartsWith('=')) return double.TryParse(selector[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) && number == exact;
            // English cardinal rules: "one" is exactly 1 with no fraction.
            return selector == "one" && number == 1;
        }
        if (kind == "select") return selector != "other" && string.Equals(selector, ValueText(raw), StringComparison.Ordinal);
        return false;
    }

    private static double ToDouble(object? value) => value switch
    {
        null => 0,
        IConvertible convertible => double.TryParse(Convert.ToString(convertible, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
        _ => 0,
    };

    private static string ValueText(object? value) => value switch
    {
        null => "",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    private static string ReadToken(string pattern, ref int position)
    {
        SkipSpace(pattern, ref position);
        var start = position;
        while (position < pattern.Length && pattern[position] is not (',' or '}' or '{') && !char.IsWhiteSpace(pattern[position]))
            position++;
        return pattern[start..position];
    }

    private static void SkipSpace(string pattern, ref int position)
    {
        while (position < pattern.Length && char.IsWhiteSpace(pattern[position])) position++;
    }

    private static void Expect(string pattern, ref int position, char expected)
    {
        SkipSpace(pattern, ref position);
        if (position >= pattern.Length || pattern[position] != expected)
            throw new FormatException($"Expected '{expected}' at {position} in ICU message.");
        position++;
    }

    // Leaves position just after the '}' that closes the option body.
    private static void SkipBalanced(string pattern, ref int position)
    {
        var depth = 1;
        while (position < pattern.Length)
        {
            var c = pattern[position++];
            if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return;
        }
        throw new FormatException("Unbalanced ICU option.");
    }
}
