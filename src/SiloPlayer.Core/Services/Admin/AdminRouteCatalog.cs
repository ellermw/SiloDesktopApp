namespace SiloPlayer.Core.Services.Admin;

/// <summary>Which descendant paths a sidebar leaf owns (WebUI <c>App.tsx</c> admin routes).</summary>
public enum AdminRouteDescendants
{
    /// <summary>Only the leaf path itself.</summary>
    None,
    /// <summary><c>/new</c> and <c>/:id/edit</c> (server collection editor, one mounted page).</summary>
    CollectionEditor,
    /// <summary><c>/:id</c> (account detail, access-group selection).</summary>
    Entity,
    /// <summary><c>/:userId/:deviceId</c> (device detail).</summary>
    UserDevice,
    /// <summary>A task key beneath Scheduled Tasks.</summary>
    TaskKey,
    /// <summary>A saved or uploaded backup restore workflow.</summary>
    BackupRestore,

}

/// <summary>A sidebar entry: a leaf page or a collapsible topic.</summary>
public abstract record AdminNavEntry;

/// <summary>
/// One admin sidebar page with its stable route identity. Text is held as
/// message keys and resolved through <see cref="AdminText"/> each time it is
/// shown, as upstream's getters do. <see cref="Keywords"/> are English
/// synonyms that are never shown; <see cref="KeywordKeys"/> and
/// <see cref="SettingKeys"/> are message keys searched in the interface language.
/// </summary>
public sealed record AdminNavLeaf(
    AdminRoute Route,
    string Href,
    bool Exact,
    string Icon,
    string SidebarLabelKey,
    string DescriptionKey,
    string HeaderTitleKey,
    string TabTitleKey,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> KeywordKeys,
    IReadOnlyList<string> SettingKeys,
    AdminRouteDescendants Descendants = AdminRouteDescendants.None) : AdminNavEntry;

/// <summary>A collapsible sidebar topic; <see cref="Id"/> keys the remembered open state.</summary>
public sealed record AdminNavTopic(string Id, string LabelKey, string Icon, IReadOnlyList<AdminNavLeaf> Leaves) : AdminNavEntry;

/// <summary>A sidebar section and its entries in official sidebar order.</summary>

public sealed record AdminNavSection(string Id, string LabelKey, IReadOnlyList<AdminNavEntry> Entries);

/// <summary>Outcome of resolving a WebUI admin href.</summary>
public enum AdminRouteResolutionKind
{
    /// <summary>A native route identity (possibly reached through a WebUI redirect).</summary>
    Route,
    /// <summary>A real WebUI admin page outside this native scope (Configure, tasks, plugins, …).</summary>
    Unsupported,
}

/// <summary>
/// A resolved admin href. <see cref="Redirected"/> mirrors a WebUI
/// <c>&lt;Navigate replace&gt;</c>: the canonical location differs from the
/// requested one. <see cref="NoticeTitleKey"/>/<see cref="NoticeDescriptionKey"/>
/// carry the informational toast a moved tab shows.
/// </summary>
public sealed record AdminRouteResolution(
    AdminRouteResolutionKind Kind,
    AdminRouteRequest? Request,
    string CanonicalHref,
    bool Redirected,
    string? NoticeTitleKey = null,
    string? NoticeDescriptionKey = null,
    bool IsCatchAll = false)
{
    /// <summary>
    /// Whether the href names a page of its own: a route that is not the Dashboard catch-all an unknown or invalid path lands on.
    /// A link that only renders when this is true never closes a popover and opens nothing.
    /// </summary>
    public bool OpensRoute => Kind == AdminRouteResolutionKind.Route && !IsCatchAll && Request != null;
}

/// <summary>
/// The admin sidebar and route table (WebUI <c>lib/adminNavigation.ts</c>
/// <c>ADMIN_NAV_SECTIONS</c>, <c>App.tsx</c> admin routes,
/// <c>lib/documentTitle.ts</c>), pinned to official Silo
/// c11b0a968f0d5980b7005579fb95acc2d11d8321. Manage has six topics and
/// twenty-one leaves; Configure has three topics and nineteen leaves.
/// This table only states identity: whether a leaf is shown

/// or reachable is decided by <see cref="AdminPageRegistry"/>, so a leaf without
/// a native page is never presented.
/// </summary>
public static class AdminRouteCatalog
{
    /// <summary>Longest accepted descendant path segment (ids are opaque server strings).</summary>
    public const int MaxSegmentLength = 256;

    private const string SettingsLabel = "web.admin.shell.nav.item.settings";

    private static AdminNavLeaf Page(AdminRoute route, string href, string icon, string page, string? sidebarLabelKey,
        string title, string[] keywords, string[]? settings = null, bool exact = false,
        AdminRouteDescendants descendants = AdminRouteDescendants.None) =>
        new(route, href, exact, icon, sidebarLabelKey ?? $"web.admin.shell.nav.page.{page}.label",
            $"web.admin.shell.nav.page.{page}.description", $"web.admin.common.title.header.{title}",
            $"web.admin.common.title.tab.{title}", keywords, [], settings ?? [], descendants);

    /// <summary>A settings page under a topic: shown as "Settings", searched by its own name.</summary>
    private static AdminNavLeaf SettingsPage(AdminRoute route, string id, string icon, string[] keywords, string[] settings) =>
        new(route, "/admin/settings/" + id, false, icon, SettingsLabel, $"web.admin.settings.search.{id}.description",
            "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
            ["settings", "configuration", .. keywords], [$"web.admin.settings.search.{id}.label"],
            settings.Select(key => $"web.admin.settings.search.{id}.setting.{key}").ToArray());

    // Monitor keeps the native shell's recorded keyword lists unchanged.
    private static readonly AdminNavLeaf DashboardLeaf = new(AdminRoute.Dashboard, "/admin", true, "layout-dashboard",
        "web.admin.shell.nav.page.dashboard.label", "web.admin.shell.nav.page.dashboard.description",
        "web.admin.shell.mobileHeader.dashboardTitle", "web.admin.common.title.tab.admin",
        ["overview", "stats", "health", "scan all"], [], []);
    private static readonly AdminNavLeaf ActivityLeaf = Page(AdminRoute.Activity, "/admin/activity", "radio", "activity", null, "activity",
        ["streams", "sessions", "now playing", "transcode"]);
    // Official c59798116 PAGES.insights (BarChart3). It has no AdminPageRegistration yet, so it is never shown or opened.
    private static readonly AdminNavLeaf InsightsLeaf = Page(AdminRoute.Insights, "/admin/insights", "bar-chart-3", "insights", null, "insights",
        ["activity", "live", "streams", "sessions", "now playing", "analytics", "insights", "playback", "watch time", "bandwidth", "egress",
         "devices", "codecs", "transcode", "heatmap", "leaderboard"]);
    private static readonly AdminNavLeaf ServerLogLeaf =Page(AdminRoute.ServerLog, "/admin/logs", "scroll-text", "logs",
        "web.admin.shell.nav.item.serverLog", "logs", ["logs", "server logs", "debug", "tail", "events"]);
    private static readonly AdminNavLeaf DiagnosticsLeaf = Page(AdminRoute.ClientDiagnostics, "/admin/diagnostics", "scroll-text", "diagnostics",
        "web.admin.shell.nav.item.clientDiagnostics", "diagnostics", ["diagnostics", "crash reports", "debug bundles", "support"]);
    private static readonly AdminNavLeaf LoggingLeaf = new(AdminRoute.LoggingSettings, "/admin/settings/logging", false, "scroll-text",
        SettingsLabel, "web.admin.settings.search.logging.description", "web.admin.common.title.header.settings",
        "web.admin.common.title.tab.settings",
        ["settings", "configuration", "log level", "log retention", "quiet log prefixes", "permission checks"],
        ["web.admin.settings.search.logging.label"], []);

    /// <summary>The sidebar, in order. Page registration controls visibility.</summary>

    public static IReadOnlyList<AdminNavSection> Sections { get; } =
    [
        new("monitor", "web.admin.shell.nav.section.monitor",
        [
            DashboardLeaf,
            ActivityLeaf,
            InsightsLeaf,
            new AdminNavTopic(AdminNavigation.LogsTopicId, "web.admin.shell.nav.topic.logs", "scroll-text", [ServerLogLeaf, DiagnosticsLeaf, LoggingLeaf]),
        ]),
        new("manage", "web.admin.shell.nav.section.manage",
        [
            new AdminNavTopic("libraries", "web.admin.shell.nav.topic.libraries", "library",
            [
                Page(AdminRoute.Libraries, "/admin/libraries", "library", "libraries", null, "libraries",
                    ["library", "paths", "scan", "catalog", "seed"]),
                Page(AdminRoute.Collections, "/admin/collections", "layout-panel-top", "collections", null, "collections",
                    ["collection groups", "templates", "smart collections"], descendants: AdminRouteDescendants.CollectionEditor),
                Page(AdminRoute.HomeSections, "/admin/sections", "rows-2", "sections", "web.admin.shell.nav.item.homeSections", "sections",
                    ["home rows", "rows", "rails", "featured sections"]),
                Page(AdminRoute.SubtitleFiles, "/admin/subtitles", "captions", "subtitleFiles", null, "subtitles",
                    ["captions", "subtitle downloads", "providers"]),
                SettingsPage(AdminRoute.LibrarySettings, "library", "library",
                    ["scanner workers", "image encoding workers", "image workers", "cpu", "matcher", "batch size", "versions", "unrated",
                     "age rating", "parental", "rotten tomatoes", "metadata", "subtitle sync", "subtitle timing"],
                    ["general", "showAllLibraryVersionsOnDetailPage", "scanning", "scannerWorkers", "matcherWorkers", "imageEncodingWorkers",
                     "personLookupsPerMinute", "matcherBatchSize", "maturityLimits", "titlesWithNoAgeRating", "ratings", "subtitleSync",
                     "syncSubtitlesAutomatically", "whereToAnalyzeAudio", "concurrentSyncsPerTranscodeNode"]),
            ]),
            new AdminNavTopic("autoscan", "web.admin.shell.nav.topic.autoscan", "radar",
            [
                Page(AdminRoute.AutoscanActivity, "/admin/autoscan/activity", "radar", "autoscanActivity", null, "autoscanActivity",
                    ["autoscan", "scan queue", "scan events", "scan history"]),
                // Activity is its own page below this path.
                Page(AdminRoute.AutoscanSources, "/admin/autoscan", "radar", "autoscanSources", null, "autoscan",
                    ["autoscan", "scan sources", "polling", "webhook source", "run now"], exact: true),
                SettingsPage(AdminRoute.AutoscanSettings, "autoscan", "radar",
                    ["autoscan", "polling", "poll interval", "check interval", "debounce", "connections", "saved servers", "sonarr", "radarr"],
                    ["pollingDefaults", "defaultCheckInterval", "debounce", "connections", "addConnection"]),
            ]),
            new AdminNavTopic("requests", "web.admin.shell.nav.topic.requests", "send",
            [
                // The Discover page editor is its own page below this path.
                Page(AdminRoute.RequestQueue, "/admin/requests", "send", "requests", "web.admin.shell.nav.item.queue", "requests",
                    ["requested media", "approvals", "request queue", "decline"], exact: true),
                Page(AdminRoute.RequestDiscover, "/admin/requests/discover", "compass", "requestDiscover", null, "requests",
                    ["discover rows", "request rows", "mdblist", "tmdb list", "trending", "discover filters"]),
                SettingsPage(AdminRoute.RequestSettings, "requests", "send",
                    ["requests", "media requests", "sonarr", "radarr", "arr", "request servers", "routing", "routes", "rules", "anime", "quota",
                     "request limit", "approval", "auto approve", "4k", "root folder", "quality profile", "overseerr", "jellyseerr", "watchlist"],
                    ["general", "allowRequests", "approval", "requestLimit", "limitWindow", "requestTitlesAddedToAWatchlist",
                     "alsoRequestA4kVersionOfEveryTitle", "servers", "addServer", "apiKey", "4kServer", "whereRequestsGo", "standard",
                     "advanced", "addRule", "everythingElse", "tryATitle", "requestQueue"]),
            ]),
            new AdminNavTopic("users", "web.admin.shell.nav.topic.users", "users",
            [
                Page(AdminRoute.Users, "/admin/users", "users", "users", null, "users",
                    ["accounts", "profiles", "roles", "permissions"], descendants: AdminRouteDescendants.Entity),
                Page(AdminRoute.AccessGroups, "/admin/access-groups", "users-round", "accessGroups", null, "accessGroups",
                    ["groups", "roles", "permissions", "library access", "downloads", "limits", "request limits"],
                    descendants: AdminRouteDescendants.Entity),
                Page(AdminRoute.Devices, "/admin/devices", "monitor-smartphone", "devices", null, "devices",
                    ["clients", "device overrides", "sessions"], descendants: AdminRouteDescendants.UserDevice),
                Page(AdminRoute.HistoryImport, "/admin/history-import", "download", "historyImport", null, "historyImport",
                    ["emby", "imports", "mappings", "watch history"]),
            ]),
            new AdminNavTopic("playback", "web.admin.shell.nav.topic.playback", "play-circle",
            [
                Page(AdminRoute.PlaybackHistory, "/admin/history", "history", "playbackHistory", "web.admin.shell.nav.item.history", "history",
                    ["history", "watched", "progress", "plays"]),
                Page(AdminRoute.SkipMarkers, "/admin/marker-history", "skip-forward", "markers", "web.admin.shell.nav.item.skipMarkers", "markerHistory",
                    ["intro markers", "credits", "recaps", "chapters"]),
                Page(AdminRoute.Nodes, "/admin/nodes", "server", "nodes", null, "nodes",
                    ["stream nodes", "workers", "transcode nodes", "proxy nodes", "routing", "egress", "gpu offload", "central egress"],
                    ["web.admin.shell.nav.page.nodes.setting.routingPreset", "web.admin.shell.nav.page.nodes.setting.directPlayEgress",
                     "web.admin.shell.nav.page.nodes.setting.remuxExecution", "web.admin.shell.nav.page.nodes.setting.remuxEgress",
                     "web.admin.shell.nav.page.nodes.setting.videoTranscodeExecution", "web.admin.shell.nav.page.nodes.setting.videoTranscodeEgress"]),
                SettingsPage(AdminRoute.PlaybackSettings, "playback", "play-circle",
                    ["ffmpeg", "transcode", "hardware acceleration", "gpu", "hevc", "tone mapping", "chapter thumbnails", "trickplay",
                     "seek preview", "watched threshold", "resume", "4k", "intro", "credits", "recap", "markers", "marker detection",
                     "credits detection", "skip credits", "TheIntroDB", "online marker storage", "on-demand only", "store markers locally",
                     "populate markers", "contribute markers", "scheduled online sync"],
                    ["watchBehavior", "markWatchedAt", "showInContinueWatchingAfter", "pausedPlayback", "endPausedPlaybackAfter", "transcoding", "videoTranscoding", "hardwareAcceleration",
                     "allowHevcEncoding", "allow4kTranscoding", "ffmpegPath", "transcodeDirectory", "gpuDevices", "enableHardwareHdrToneMapping",
                     "enableSoftwareHdrToneMapping", "throttleTranscoding", "bufferAheadSeconds", "transcodeBackBuffer", "previewImages",
                     "chapterThumbnailWorkers", "generateChapterThumbnailsOn", "hdrHandling", "softwareHdrToneMapping", "previewImageWidth",
                     "seekPreviewInterval", "seekPreviewWorkers", "generateSeekPreviewsOn", "hdrChapterThumbnails", "skipMarkers",
                     "markerSource", "detectIntros", "detectCredits", "saveOnlineMarkers", "findMarkersOnPlayback", "detectionWorkers",
                     "syncOnlineMarkers", "detectMarkersOnThisServer", "shareDetectedMarkers"]),
            ]),
            new AdminNavTopic("downloads", "web.admin.shell.nav.topic.downloads", "hard-drive-download",
            [
                Page(AdminRoute.PreparedFiles, "/admin/downloads", "hard-drive-download", "downloads", "web.admin.shell.nav.item.preparedFiles", "downloads",
                    ["offline downloads", "prepared files", "artifacts", "disk space", "storage", "revoke", "devices"]),
                SettingsPage(AdminRoute.DownloadSettings, "downloads", "download",
                    ["downloads", "offline", "bandwidth", "mbps", "throttle", "quota", "concurrency", "prepared copies", "artifacts",
                     "storage budget", "cache", "disk ceiling"],
                    ["downloads", "allowDownloads", "perUserBandwidth", "perUser", "downloadsAtOncePerUser", "downloadsPerPeriod",
                     "periodLength", "wholeServer", "serverBandwidth", "prepareDeviceFriendlyCopies", "prepareLocallyWhenWorkersAreUnavailable",
                     "preparedFileDirectory", "filesPreparedAtOnce", "defaultStorageBudgetPerLocation", "keepCachedFilesFor", "diskCeiling"]),
            ]),
        ]),
        new("configure", "web.admin.shell.nav.section.configure",
        [
            new AdminNavTopic("server", "web.admin.shell.nav.topic.server", "settings-2",
            [
                new AdminNavLeaf(AdminRoute.GeneralSettings, "/admin/settings/general", false, "settings-2",
                    "web.admin.settings.search.general.label", "web.admin.settings.search.general.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "server name", "login subtitle", "branding name", "public url", "lan discovery", "local network", "local address", "lan address", "multicast", "reverse proxy", "trusted proxies", "x-forwarded-for", "client ip", "redis", "postgres", "database", "pool"], [], ["web.admin.settings.search.general.setting.identity", "web.admin.settings.search.general.setting.serverName", "web.admin.settings.search.general.setting.loginSubtitle", "web.admin.settings.search.general.setting.network", "web.admin.settings.search.general.setting.siloPublicUrl", "web.admin.settings.search.general.setting.showOnTheLocalNetwork", "web.admin.settings.search.general.setting.localNetworkAddress", "web.admin.settings.search.general.setting.trustedProxies", "web.admin.settings.search.general.setting.database", "web.admin.settings.search.general.setting.maximumPostgresConnections", "web.admin.settings.search.general.setting.redis", "web.admin.settings.search.general.setting.useRedis", "web.admin.settings.search.general.setting.connectionUrl", "web.admin.settings.search.general.setting.databaseNumber", "web.admin.settings.search.general.setting.checkConnection"]),
                new AdminNavLeaf(AdminRoute.AppearanceSettings, "/admin/settings/appearance", false, "paintbrush",
                    "web.admin.settings.search.appearance.label", "web.admin.settings.search.appearance.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "logo", "wordmark", "favicon", "login background", "white label", "brand", "accent color", "app name", "pwa", "overlays", "poster badges", "title art", "logo art", "title logo"], [], ["web.admin.settings.search.appearance.setting.logosAndIcons", "web.admin.settings.search.appearance.setting.logoWordmark", "web.admin.settings.search.appearance.setting.logoIcon", "web.admin.settings.search.appearance.setting.favicon", "web.admin.settings.search.appearance.setting.loginBackground", "web.admin.settings.search.appearance.setting.colors", "web.admin.settings.search.appearance.setting.accentColor", "web.admin.settings.search.appearance.setting.customAccentColor", "web.admin.settings.search.appearance.setting.cardOverlays", "web.admin.settings.search.appearance.setting.showBadgesOnPosterArt", "web.admin.settings.search.appearance.setting.badgeStyle", "web.admin.settings.search.appearance.setting.restoreDefaults", "web.admin.settings.search.appearance.setting.titlePages", "settingsManifest.ui.title_art.label"]),
                new AdminNavLeaf(AdminRoute.StorageSettings, "/admin/settings/storage", false, "database",
                    "web.admin.settings.search.storage.label", "web.admin.settings.search.storage.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "s3", "bucket", "storage", "local storage path", "artwork.storage_backend", "artwork.local_path", "endpoint", "region", "access key", "secret key", "infrastructure", "artwork", "image caching", "cache images", "cloudflare", "presigned"], [], ["web.admin.settings.search.storage.setting.storage", "web.admin.settings.search.storage.setting.backend", "web.admin.settings.search.storage.setting.localStoragePath", "web.admin.settings.search.storage.setting.saveProviderArtworkToStorage", "web.admin.settings.search.storage.setting.reviewTransition", "web.admin.settings.search.storage.setting.whatShouldMove", "web.admin.settings.search.storage.setting.publicStorage", "web.admin.settings.search.storage.setting.privateStorage", "web.admin.settings.search.storage.setting.checkConnection", "web.admin.settings.search.storage.setting.endpoint", "web.admin.settings.search.storage.setting.region", "web.admin.settings.search.storage.setting.bucket", "web.admin.settings.search.storage.setting.accessKey", "web.admin.settings.search.storage.setting.secretKey", "web.admin.settings.search.storage.setting.putTheBucketNameInTheUrlPath", "web.admin.settings.search.storage.setting.folderInsideTheBucket", "web.admin.settings.search.storage.setting.howAssetLinksAreAuthorized", "web.admin.settings.search.storage.setting.addressClientsDownloadFrom", "web.admin.settings.search.storage.setting.tokenSecret", "web.admin.settings.search.storage.setting.tokenQueryParameter", "web.admin.settings.search.storage.setting.linkLifetimeSeconds"]),
                new AdminNavLeaf(AdminRoute.SearchSettings, "/admin/settings/search", false, "search",
                    "web.admin.settings.search.search.label", "web.admin.settings.search.search.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "meilisearch", "postgres search", "semantic", "typo", "search index", "reindex"], [], ["web.admin.settings.search.search.setting.search", "web.admin.settings.search.search.setting.searchEngine", "web.admin.settings.search.search.setting.meilisearchUrl", "web.admin.settings.search.search.setting.meilisearchApiKey", "web.admin.settings.search.search.setting.checkConnection", "web.admin.settings.search.search.setting.whenASearchHasSeveralWords", "web.admin.settings.search.search.setting.matchByMeaningAsWellAsWords", "web.admin.settings.search.search.setting.indexNamePrefix", "web.admin.settings.search.search.setting.queryTimeoutMs", "web.admin.settings.search.search.setting.itemsSentToTheIndexPerBatch", "web.admin.settings.search.search.setting.meaningBasedShareOfResults", "web.admin.settings.search.search.setting.searchStatus"]),
                new AdminNavLeaf(AdminRoute.NetworkAccessSettings, "/admin/settings/network-access", false, "network",
                    "web.admin.settings.search.networkAccess.label", "web.admin.settings.search.networkAccess.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "tailscale", "netbird", "overlay", "tailnet", "vpn", "remote access", "port forwarding", "network access provider", "plugin", "connect", "authorization"], [], []),
                new AdminNavLeaf(AdminRoute.ScheduledTasks, "/admin/tasks", false, "calendar-clock",
                    "web.admin.shell.nav.page.scheduledTasks.label", "web.admin.shell.nav.page.scheduledTasks.description",
                    "web.admin.common.title.header.tasks", "web.admin.common.title.tab.tasks",
                    ["tasks", "jobs", "scheduler", "sync"], [], [], AdminRouteDescendants.TaskKey),
                new AdminNavLeaf(AdminRoute.Maintenance, "/admin/maintenance", false, "wrench",
                    "web.admin.shell.nav.page.maintenance.label", "web.admin.shell.nav.page.maintenance.description",
                    "web.admin.common.title.header.maintenance", "web.admin.common.title.tab.maintenance",
                    ["repair", "cleanup", "system maintenance"], [], []),
                new AdminNavLeaf(AdminRoute.Backups, "/admin/backups", false, "database-backup",
                    "web.admin.shell.nav.page.backups.label", "web.admin.shell.nav.page.backups.description",
                    "web.admin.common.title.header.backups", "web.admin.common.title.tab.backups",
                    ["backup", "back up now", "restore", "recovery", "database dump", "pg_dump", "disaster recovery", "copies to keep", "retention"], [], [], AdminRouteDescendants.BackupRestore),
            ]),
            new AdminNavTopic("access", "web.admin.shell.nav.topic.access", "shield",
            [
                new AdminNavLeaf(AdminRoute.SignInSettings, "/admin/settings/sign-in", false, "key-round",
                    "web.admin.settings.search.signIn.label", "web.admin.settings.search.signIn.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "sso", "single sign-on", "oidc", "openid connect", "ldap", "active directory", "authentik", "authelia", "keycloak", "pocket id", "redirect uri", "callback url", "break-glass", "local password", "auto-create accounts", "email match", "provider logout", "rp-initiated logout", "bind dn", "base dn", "issuer", "client id", "signup", "invite", "forgot password", "password reset", "tailscale"], [], ["web.admin.settings.search.signIn.setting.siloPasswords", "web.admin.settings.search.signIn.setting.allowPasswordSignIn", "web.admin.settings.search.signIn.setting.publicSignups", "web.admin.settings.search.signIn.setting.selfServicePasswordReset", "web.admin.settings.search.signIn.setting.manageInviteCodes", "web.admin.settings.search.signIn.setting.singleSignOn", "web.admin.settings.search.signIn.setting.matchExistingAccountsByEmail", "web.admin.settings.search.signIn.setting.redirectUri", "web.admin.settings.search.signIn.setting.postLogoutRedirectUri", "web.admin.settings.search.signIn.setting.issuerUrl", "web.admin.settings.search.signIn.setting.clientId", "web.admin.settings.search.signIn.setting.clientSecret", "web.admin.settings.search.signIn.setting.bindDn", "web.admin.settings.search.signIn.setting.userSearchBaseDn", "web.admin.settings.search.signIn.setting.allowedGroups", "web.admin.settings.search.signIn.setting.adminGroups", "web.admin.settings.search.signIn.setting.createAccountsOnFirstSignIn", "web.admin.settings.search.signIn.setting.testTheConnection", "web.admin.settings.search.signIn.setting.loginButton", "web.admin.settings.search.signIn.setting.networkSignIn", "web.admin.settings.search.signIn.setting.howRolesAndSharingWork"]),
                new AdminNavLeaf(AdminRoute.SecuritySettings, "/admin/settings/security", false, "shield",
                    "web.admin.settings.search.security.label", "web.admin.settings.search.security.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "private destinations", "ssrf", "rate limit", "throttle", "429", "api key tier", "security"], [], ["web.admin.settings.search.security.setting.localNetwork", "web.admin.settings.search.security.setting.localServersForEveryAccount", "web.admin.settings.search.security.setting.rateLimiting", "web.admin.settings.search.security.setting.enableRateLimiting", "web.admin.settings.search.security.setting.whereCountersAreKept", "web.admin.settings.search.security.setting.wholeServerLimit", "web.admin.settings.search.security.setting.perAddressAndAccount", "web.admin.settings.search.security.setting.apiKeyTiers", "web.admin.settings.search.security.setting.standardApiKeys", "web.admin.settings.search.security.setting.elevatedApiKeys", "web.admin.settings.search.security.setting.signInAndWebhookEndpoints"]),
                new AdminNavLeaf(AdminRoute.ApiKeys, "/admin/api-keys", false, "key-round",
                    "web.admin.shell.nav.page.apiKeys.label", "web.admin.shell.nav.page.apiKeys.description",
                    "web.admin.common.title.header.apiKeys", "web.admin.common.title.tab.apiKeys",
                    ["tokens", "keys", "access", "scopes", "rate limit tier"], [], []),
                new AdminNavLeaf(AdminRoute.Policy, "/admin/policy", false, "shield-check",
                    "web.admin.shell.nav.page.policy.label", "web.admin.shell.nav.page.policy.description",
                    "web.admin.common.title.header.policy", "web.admin.common.title.tab.policy",
                    ["opa", "rego", "authorization", "decision log", "access policy"], [], []),
            ]),
            new AdminNavTopic("integrations", "web.admin.shell.nav.topic.integrations", "puzzle",
            [
                new AdminNavLeaf(AdminRoute.Plugins, "/admin/plugins", false, "blocks",
                    "web.admin.shell.nav.page.plugins.label", "web.admin.shell.nav.page.plugins.description",
                    "web.admin.common.title.header.plugins", "web.admin.common.title.tab.plugins",
                    ["extensions", "plugin catalog", "repositories"], [], [], AdminRouteDescendants.Entity),
                new AdminNavLeaf(AdminRoute.ProviderSettings, "/admin/settings/providers", false, "blocks",
                    "web.admin.settings.search.providers.label", "web.admin.settings.search.providers.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "simkl", "scrobble", "watch providers", "sync", "plugin", "opensubtitles", "subdl", "subsource", "mdblist", "lists", "subtitles", "captions", "api key", "provider credentials", "integrations", "theintrodb", "introdb", "marker providers", "contributions", "markers", "intro", "credits", "metadata"], [], ["web.admin.settings.search.providers.setting.watchProviders", "web.admin.settings.search.providers.setting.subtitleProviders", "web.admin.settings.search.providers.setting.opensubtitles", "web.admin.settings.search.providers.setting.subdl", "web.admin.settings.search.providers.setting.subsource", "web.admin.settings.search.providers.setting.username", "web.admin.settings.search.providers.setting.password", "web.admin.settings.search.providers.setting.apiKey", "web.admin.settings.search.providers.setting.lists", "web.admin.settings.search.providers.setting.mdblist", "web.admin.settings.search.providers.setting.markerProviders", "web.admin.settings.search.providers.setting.theintrodb", "web.admin.settings.search.providers.setting.getMarkersFromThisProvider", "web.admin.settings.search.providers.setting.providerPriority", "web.admin.settings.search.providers.setting.allowSharingWithThisProvider", "web.admin.settings.search.providers.setting.automaticallyShareDetectedIntrosAndCredits", "web.admin.settings.search.providers.setting.minimumConfidenceForAutomaticSharing"]),
                new AdminNavLeaf(AdminRoute.EmailSettings, "/admin/settings/email", false, "mail",
                    "web.admin.settings.search.email.label", "web.admin.settings.search.email.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "email", "smtp", "mail", "mail server", "invitations", "password reset", "test email"], [], ["web.admin.settings.search.email.setting.mailServer", "web.admin.settings.search.email.setting.sendEmailFromThisServer", "web.admin.settings.search.email.setting.fromAddress", "web.admin.settings.search.email.setting.fromName", "web.admin.settings.search.email.setting.mailServerAddress", "web.admin.settings.search.email.setting.port", "web.admin.settings.search.email.setting.encryption", "web.admin.settings.search.email.setting.username", "web.admin.settings.search.email.setting.password", "web.admin.settings.search.email.setting.testEmailRecipient", "web.admin.settings.search.email.setting.sendTest"]),
                new AdminNavLeaf(AdminRoute.NotificationSettings, "/admin/settings/notifications", false, "bell",
                    "web.admin.settings.search.notifications.label", "web.admin.settings.search.notifications.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "release events", "new episode", "silo push relay", "mobile push", "apns", "fcm", "discord", "web push", "webhooks", "server channels", "digest", "retention", "email"], [], ["web.admin.settings.search.notifications.setting.deliveryChannels", "web.admin.settings.search.notifications.setting.inApp", "web.admin.settings.search.notifications.setting.webPush", "web.admin.settings.search.notifications.setting.siloPushRelay", "web.admin.settings.search.notifications.setting.applePushApns", "web.admin.settings.search.notifications.setting.androidPushFcm", "web.admin.settings.search.notifications.setting.relayUrl", "web.admin.settings.search.notifications.setting.deploymentId", "web.admin.settings.search.notifications.setting.email", "web.admin.settings.search.notifications.setting.letPeoplePickAnEmailPerEpisode", "web.admin.settings.search.notifications.setting.dailySummaryHour", "web.admin.settings.search.notifications.setting.discord", "web.admin.settings.search.notifications.setting.setupGuide", "web.admin.settings.search.notifications.setting.clientId", "web.admin.settings.search.notifications.setting.clientSecret", "web.admin.settings.search.notifications.setting.botToken", "web.admin.settings.search.notifications.setting.letPeoplePickADmPerEpisode", "web.admin.settings.search.notifications.setting.artwork", "web.admin.settings.search.notifications.setting.personalWebhooks", "web.admin.settings.search.notifications.setting.webhooksPerPerson", "web.admin.settings.search.notifications.setting.deliveriesPerPerson", "web.admin.settings.search.notifications.setting.allowWebhooksToPrivateAddresses", "web.admin.settings.search.notifications.setting.serverChannels", "web.admin.settings.search.notifications.setting.addServerChannel", "web.admin.settings.search.notifications.setting.batchWindow", "web.admin.settings.search.notifications.setting.mentionTheRequesterOnDiscord", "web.admin.settings.search.notifications.setting.processing", "web.admin.settings.search.notifications.setting.noticeNewContent", "web.admin.settings.search.notifications.setting.workOutWhoWantsIt", "web.admin.settings.search.notifications.setting.groupingAndFloodControl", "web.admin.settings.search.notifications.setting.settleWindow", "web.admin.settings.search.notifications.setting.maxMessagesPerShow", "web.admin.settings.search.notifications.setting.maxContentAge", "web.admin.settings.search.notifications.setting.retention", "web.admin.settings.search.notifications.setting.readNotifications", "web.admin.settings.search.notifications.setting.unreadNotifications", "web.admin.settings.search.notifications.setting.sentHistory"]),
                new AdminNavLeaf(AdminRoute.AiSettings, "/admin/settings/ai", false, "sparkles",
                    "web.admin.settings.search.ai.label", "web.admin.settings.search.ai.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "openai", "ollama", "groq", "openrouter", "gemini", "whisper", "chatgpt", "provider", "api key", "llm", "model", "translation", "transcription", "subtitle translation", "embeddings", "integrations"], [], ["web.admin.settings.search.ai.setting.providers", "web.admin.settings.search.ai.setting.addProvider", "web.admin.settings.search.ai.setting.baseUrl", "web.admin.settings.search.ai.setting.apiKey", "web.admin.settings.search.ai.setting.models", "web.admin.settings.search.ai.setting.textModel", "web.admin.settings.search.ai.setting.provider", "web.admin.settings.search.ai.setting.model", "web.admin.settings.search.ai.setting.reasoningLevel", "web.admin.settings.search.ai.setting.serviceTier", "web.admin.settings.search.ai.setting.fastPriority", "web.admin.settings.search.ai.setting.ultrafast", "web.admin.settings.search.ai.setting.speechToText", "web.admin.settings.search.ai.setting.embeddings", "web.admin.settings.search.ai.setting.features", "web.admin.settings.search.ai.setting.translateSubtitles", "web.admin.settings.search.ai.setting.createSubtitlesFromAudio", "web.admin.settings.search.ai.setting.translateDescriptions", "web.admin.settings.search.ai.setting.descriptionTranslationForViewers", "web.admin.settings.search.ai.setting.serverWideTuning", "web.admin.settings.search.ai.setting.jobsRunningAtOnce", "web.admin.settings.search.ai.setting.subtitleLinesPerRequest", "web.admin.settings.search.ai.setting.surroundingLinesSentForContext", "web.admin.settings.search.ai.setting.audioPerRequest", "web.admin.settings.search.ai.setting.perAccountLimits", "web.admin.settings.search.ai.setting.transcriptionsPerAccount", "web.admin.settings.search.ai.setting.allowanceResets"]),
                new AdminNavLeaf(AdminRoute.Recommendations, "/admin/recommendations", false, "bot",
                    "web.admin.shell.nav.page.recommendations.label", "web.admin.shell.nav.page.recommendations.description",
                    "web.admin.common.title.header.recommendations", "web.admin.common.title.tab.recommendations",
                    ["taste", "ranking", "recommendation seeds"], [], []),
                new AdminNavLeaf(AdminRoute.CompatibilitySettings, "/admin/settings/compatibility", false, "plug",
                    "web.admin.settings.search.compatibility.label", "web.admin.settings.search.compatibility.description",
                    "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings",
                    ["settings", "configuration", "jellyfin", "audiobookshelf", "abs", "proxy", "public url", "server id", "session ttl", "web player"], [], ["web.admin.settings.search.compatibility.setting.jellyfin", "web.admin.settings.search.compatibility.setting.allowJellyfinAppsToConnect", "web.admin.settings.search.compatibility.setting.addressJellyfinAppsShouldUse", "web.admin.settings.search.compatibility.setting.jellyfinWebPlayer", "web.admin.settings.search.compatibility.setting.downloadDetails", "web.admin.settings.search.compatibility.setting.webPlayerVersionToInstall", "web.admin.settings.search.compatibility.setting.webPlayerInstallFolder", "web.admin.settings.search.compatibility.setting.nameShownToJellyfinApps", "web.admin.settings.search.compatibility.setting.serverId", "web.admin.settings.search.compatibility.setting.jellyfinVersionToReport", "web.admin.settings.search.compatibility.setting.staySignedInFor", "web.admin.settings.search.compatibility.setting.endPlaybackSessionsAfter", "web.admin.settings.search.compatibility.setting.audiobookshelf", "web.admin.settings.search.compatibility.setting.allowAudiobookshelfAppsToConnect"]),
            ]),
        ]),

    ];

    private static readonly IReadOnlyDictionary<AdminRoute, (AdminNavLeaf Leaf, AdminNavTopic? Topic, AdminNavSection Section)> Index = BuildIndex();

    private static Dictionary<AdminRoute, (AdminNavLeaf, AdminNavTopic?, AdminNavSection)> BuildIndex()
    {
        var index = new Dictionary<AdminRoute, (AdminNavLeaf, AdminNavTopic?, AdminNavSection)>();
        foreach (var section in Sections)
            foreach (var entry in section.Entries)
                switch (entry)
                {
                    case AdminNavLeaf leaf: index.Add(leaf.Route, (leaf, null, section)); break;
                    case AdminNavTopic topic: foreach (var leaf in topic.Leaves) index.Add(leaf.Route, (leaf, topic, section)); break;
                }
        return index;
    }

    /// <summary>Every leaf in sidebar order.</summary>
    public static IEnumerable<AdminNavLeaf> Leaves => Sections.SelectMany(section => section.Entries)
        .SelectMany(entry => entry is AdminNavTopic topic ? topic.Leaves : [(AdminNavLeaf)entry]);

    public static AdminNavLeaf Leaf(AdminRoute route) =>
        Index.TryGetValue(route, out var entry) ? entry.Leaf : throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown admin route.");

    public static AdminNavTopic? TopicOf(AdminRoute route) => Index.TryGetValue(route, out var entry) ? entry.Topic : null;

    public static AdminNavSection SectionOf(AdminRoute route) => Index[route].Section;

    /// <summary>Every message key a route's sidebar entry, titles (including descendant pages) and search index use.</summary>
    public static IEnumerable<string> TextKeys(AdminRoute route)
    {
        var (leaf, topic, section) = Index[route];
        yield return section.LabelKey;
        if (topic != null) yield return topic.LabelKey;
        yield return leaf.SidebarLabelKey;
        yield return leaf.DescriptionKey;
        yield return leaf.HeaderTitleKey;
        yield return leaf.TabTitleKey;
        foreach (var key in leaf.KeywordKeys.Concat(leaf.SettingKeys)) yield return key;
        if (route == AdminRoute.Collections)
        {
            yield return "web.admin.common.title.tab.collectionNew";
            yield return "web.admin.common.title.tab.collectionEdit";
        }
        if (route == AdminRoute.Users)
        {
            yield return "web.admin.common.title.header.user";
            yield return "web.admin.common.title.tab.user";
        }
    }

    /// <summary>Keys of <see cref="TextKeys"/> the admin text catalog cannot render yet; a route must have none before it is registered.</summary>
    public static IReadOnlyList<string> MissingTextKeys(AdminRoute route) => TextKeys(route).Where(key => !AdminText.Has(key)).Distinct().ToArray();

    // ── Descendant validation ─────────────────────────────────

    /// <summary>Whether a request names its leaf and a descendant path that leaf really owns.</summary>
    public static bool IsValid(AdminRouteRequest? request)
    {
        if (request == null || !Index.TryGetValue(request.Route, out var entry)) return false;
        var segments = request.Segments;
        // Manifest plugin IDs are opaque (pluginPagePath percent-encodes the entire ID).
        // An encoded slash remains one management descendant, never a plugin-app route.
        if (request.Route == AdminRoute.Plugins)
        {
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment.Length > MaxSegmentLength || segment.Any(char.IsControl))) return false;
        }
        else if (segments.Any(segment => !IsValidSegment(segment))) return false;
        return entry.Leaf.Descendants switch
        {
            AdminRouteDescendants.CollectionEditor => segments.Count == 0 || (segments.Count == 1 && segments[0] == "new")
                || (segments.Count == 2 && segments[1] == "edit"),
            AdminRouteDescendants.Entity => segments.Count <= 1,
            AdminRouteDescendants.UserDevice => segments.Count is 0 or 2,
            AdminRouteDescendants.TaskKey => segments.Count <= 1,
            AdminRouteDescendants.BackupRestore => segments.Count == 0 || (segments.Count == 2 && segments[1] == "restore")
                || (segments.Count == 3 && segments[0] == "uploads" && segments[2] == "restore"),

            _ => segments.Count == 0,
        };
    }

    // "." and ".." are dot-segments: a browser normalises them away, and an escaped one is still "..", so they never name a record.
    private static bool IsValidSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) && segment.Length <= MaxSegmentLength && !segment.Any(char.IsControl) && !segment.Contains('/')
        && segment is not ("." or "..");

    // ── Href formatting and parsing ───────────────────────────

    /// <summary>The canonical WebUI href: leaf path, escaped descendants, query in ordinal key order.</summary>
    public static string ToHref(AdminRouteRequest request)
    {
        var path = Leaf(request.Route).Href + string.Concat(request.Segments.Select(segment => "/" + Uri.EscapeDataString(segment)));
        return path + QueryString(request.Query);
    }

    private static string QueryString(IReadOnlyDictionary<string, string>? query) =>
        query is not { Count: > 0 } ? "" : "?" + string.Join('&', query.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));



    /// <summary>WebUI <c>ADMIN_SETTINGS_NAV</c> ids, first page first.</summary>
    private static readonly string[] SettingsPageIds =
    [
        "general", "appearance", "storage", "logging", "sign-in", "security", "network-access", "library", "search", "autoscan",
        "playback", "downloads", "requests", "providers", "ai", "email", "notifications", "compatibility",
    ];

    private static readonly IReadOnlyDictionary<string, string> LegacySettingsAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["branding"] = "appearance", ["theming"] = "appearance", ["overlays"] = "appearance", ["rate-limiting"] = "security",
        ["scanner"] = "library", ["intro"] = "playback", ["subtitles"] = "providers", ["integrations"] = "providers",
        ["watch-providers"] = "providers", ["watch-sync"] = "providers", ["jellyfin"] = "compatibility",
        ["compatibility-proxies"] = "compatibility", ["database"] = "general", ["infrastructure"] = "storage", ["log-retention"] = "logging",
    };

    /// <summary>Admin pages that exist in the WebUI but are outside this native scope (route pattern segments; "*" is a parameter).</summary>
    private static readonly string[][] UnsupportedPatterns =
    [
    ];

    /// <summary>
    /// Resolves a WebUI admin href (<c>/admin/...</c>, optional query) the way
    /// the WebUI router would, including its redirects. Returns false for a
    /// location outside <c>/admin</c>. An unknown admin path redirects to the
    /// Dashboard, as WebUI's catch-all does.
    /// </summary>
    public static bool TryResolve(string? href, out AdminRouteResolution resolution)
    {
        resolution = null!;
        if (string.IsNullOrWhiteSpace(href)) return false;
        var text = href.Trim();
        var hash = text.IndexOf('#');
        if (hash >= 0) text = text[..hash];
        var queryStart = text.IndexOf('?');
        var pathText = queryStart >= 0 ? text[..queryStart] : text;
        var query = ParseQuery(queryStart >= 0 ? text[(queryStart + 1)..] : "");
        if (!pathText.StartsWith('/')) return false;
        string[] segments;
        try { segments = pathText.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray(); }
        catch (UriFormatException) { return false; }
        if (segments.Length == 0 || segments[0] != "admin") return false;
        var rest = segments[1..];
        resolution = Resolve(rest, query, text);
        return true;
    }

    private static AdminRouteResolution Resolve(string[] rest, Dictionary<string, string> query, string requested)
    {
        // Redirected only where the WebUI router itself navigates elsewhere; query order or escaping is not a redirect.
        AdminRouteResolution Resolved(AdminRouteRequest request, bool forceRedirect = false, string? title = null, string? description = null) =>
            new(AdminRouteResolutionKind.Route, request, ToHref(request), forceRedirect, title, description);
        AdminRouteResolution Dashboard(bool unknown = false) => Resolved(new AdminRouteRequest(AdminRoute.Dashboard), forceRedirect: true) with { IsCatchAll = unknown };

        if (rest.Length == 0) return Resolved(new AdminRouteRequest(AdminRoute.Dashboard, query.Count > 0 ? query : null));
        // Legacy redirects (App.tsx, LegacyAdminHomeRowsRedirect).
        if (rest is ["stats"]) return Dashboard();
        if (rest is ["home-rows"])
            return Resolved(new AdminRouteRequest(AdminRoute.HomeSections, query.Count > 0 ? query : null), forceRedirect: true);
        if (rest[0] == "settings") return ResolveSettings(rest[1..], query);
        // AdminRequests MOVED_TABS: the moved tab's link lands on its new page, without the old query.
        if (rest is ["requests"] && query.TryGetValue("tab", out var tab))
        {
            if (tab is "settings" or "integrations") return Resolved(new AdminRouteRequest(AdminRoute.RequestSettings), forceRedirect: true);
            if (tab == "overrides")
                return Resolved(new AdminRouteRequest(AdminRoute.Users), true,
                    "web.admin.requests.page.overridesMoved.title", "web.admin.requests.page.overridesMoved.description");
        }
        foreach (var pattern in UnsupportedPatterns)
            if (pattern.Length == rest.Length && pattern.Zip(rest).All(pair => pair.First == "*" || pair.First == pair.Second))
                return new(AdminRouteResolutionKind.Unsupported, null, requested, false);

        AdminNavLeaf? owner = null;
        string[] ownerBase = [];
        foreach (var leaf in Leaves)
        {
            if (leaf.Href.StartsWith("/admin/settings/", StringComparison.Ordinal) || leaf.Route == AdminRoute.Dashboard) continue;
            var leafSegments = leaf.Href.Split('/', StringSplitOptions.RemoveEmptyEntries)[1..];
            if (leafSegments.Length > rest.Length || leafSegments.Length <= ownerBase.Length) continue;
            if (leafSegments.Zip(rest).All(pair => pair.First == pair.Second)) { owner = leaf; ownerBase = leafSegments; }
        }
        if (owner == null) return Dashboard(unknown: true);
        var request = new AdminRouteRequest(owner.Route, query.Count > 0 ? query : null) { Segments = rest[ownerBase.Length..] };
        return IsValid(request) ? Resolved(request) : Dashboard(unknown: true);
    }

    /// <summary>AdminSettingsLayout: aliases and old <c>?tab=</c> ids become canonical page URLs without a query; unknown ids open the first page.</summary>
    private static AdminRouteResolution ResolveSettings(string[] rest, Dictionary<string, string> query)
    {
        var raw = rest.Length == 0 ? null : string.Join('/', rest);
        var requestedId = raw ?? (query.TryGetValue("tab", out var tab) ? tab : null);
        string? id = null;
        if (requestedId != null)
            id = SettingsPageIds.Contains(requestedId) ? requestedId : LegacySettingsAliases.TryGetValue(requestedId, out var alias) ? alias : null;
        id ??= SettingsPageIds[0];
        var canonical = "/admin/settings/" + Uri.EscapeDataString(id);
        var redirected = raw != id || query.Count > 0;
        var leaf = Leaves.SingleOrDefault(candidate => candidate.Href == canonical);
        if (leaf == null) return new(AdminRouteResolutionKind.Unsupported, null, canonical, redirected);

        return new(AdminRouteResolutionKind.Route, new AdminRouteRequest(leaf.Route), canonical, redirected);
    }

    private static Dictionary<string, string> ParseQuery(string text)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            string Decode(string value)
            {
                try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
                catch (UriFormatException) { return value; }
            }
            var key = Decode(equals >= 0 ? part[..equals] : part);
            // URLSearchParams.get returns the first value for a repeated key.
            if (key.Length > 0) query.TryAdd(key, Decode(equals >= 0 ? part[(equals + 1)..] : ""));
        }
        return query;
    }
}
