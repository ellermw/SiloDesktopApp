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

/// <summary>A sidebar section (Monitor, Manage) and its entries in sidebar order.</summary>
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
    bool IsCatchAll = false);

/// <summary>
/// The admin sidebar and route table (WebUI <c>lib/adminNavigation.ts</c>
/// <c>ADMIN_NAV_SECTIONS</c>, <c>App.tsx</c> admin routes,
/// <c>lib/documentTitle.ts</c>), pinned to official Silo
/// 1452ea2d10efbe7d3ea5ce7140fb49756b493670. Manage is exactly six topics and
/// twenty-one leaves. This table only states identity: whether a leaf is shown
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
    private static readonly AdminNavLeaf ServerLogLeaf = Page(AdminRoute.ServerLog, "/admin/logs", "scroll-text", "logs",
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
                    ["watchBehavior", "markWatchedAt", "showInContinueWatchingAfter", "transcoding", "videoTranscoding", "hardwareAcceleration",
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
                    ["settings", "configuration", "server name", "login subtitle", "branding name", "public url", "lan discovery", "local network", "local address", "lan address", "multicast", "reverse proxy", "trusted proxies", "x-forwarded-for", "client ip", "redis", "postgres", "database", "pool"],
                    [], new[] { "identity", "serverName", "loginSubtitle", "network", "siloPublicUrl", "showOnTheLocalNetwork", "localNetworkAddress", "trustedProxies", "database", "maximumPostgresConnections", "redis", "useRedis", "connectionUrl", "databaseNumber", "checkConnection" }.Select(key => "web.admin.settings.search.general.setting." + key).ToArray()),
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
        if (segments.Any(segment => !IsValidSegment(segment))) return false;
        return entry.Leaf.Descendants switch
        {
            AdminRouteDescendants.CollectionEditor => segments.Count == 0 || (segments.Count == 1 && segments[0] == "new")
                || (segments.Count == 2 && segments[1] == "edit"),
            AdminRouteDescendants.Entity => segments.Count <= 1,
            AdminRouteDescendants.UserDevice => segments.Count is 0 or 2,
            _ => segments.Count == 0,
        };
    }

    private static bool IsValidSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment) && segment.Length <= MaxSegmentLength && !segment.Any(char.IsControl) && !segment.Contains('/');

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

    private static readonly HashSet<string> ManagedSettingsIds = ["logging", "library", "autoscan", "requests", "playback", "downloads", "general"];

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
        ["maintenance"], ["backups"], ["backups", "*", "restore"], ["backups", "uploads", "*", "restore"], ["plugins"], ["plugins", "*"],
        ["policy"], ["recommendations"], ["api-keys"], ["tasks"], ["tasks", "*"],
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
        if (!ManagedSettingsIds.Contains(id)) return new(AdminRouteResolutionKind.Unsupported, null, canonical, redirected);
        var leaf = Leaves.Single(candidate => candidate.Href == canonical);
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
