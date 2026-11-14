using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The in-scope Admin Monitor routes (WebUI <c>ADMIN_NAV_SECTIONS[0]</c>). Other
/// admin areas are intentionally absent so no dead navigation can ship.
/// </summary>
public enum AdminRoute
{
    /// <summary>/admin</summary>
    Dashboard,
    /// <summary>/admin/activity</summary>
    Activity,
    /// <summary>/admin/logs (Logs › Server Log)</summary>
    ServerLog,
    /// <summary>/admin/diagnostics (Logs › Client Diagnostics)</summary>
    ClientDiagnostics,
    /// <summary>/admin/settings/logging (Logs › Settings)</summary>
    LoggingSettings,
}

/// <summary>
/// Navigation parameter every admin page receives. <see cref="Query"/> mirrors
/// the WebUI URL search parameters of that route (for Server Log:
/// <c>playback_session_id</c>, <c>focus</c>, <c>component</c>, <c>tab</c>, …) so
/// pages own their URL-state parsing exactly as upstream.
/// </summary>
public sealed record AdminRouteRequest(AdminRoute Route, IReadOnlyDictionary<string, string>? Query = null)
{
    public string? Get(string key) => Query != null && Query.TryGetValue(key, out var value) ? value : null;
}

public sealed record AdminNavPage(AdminRoute Route, string Label, string Description, string Icon, IReadOnlyList<string> Keywords);

/// <summary>Shell navigation facts shared by the sidebar, phone header, window title and search.</summary>
public static class AdminNavigation
{
    public const string LogsTopicId = "logs";

    /// <summary>The topic a route sits under, or null for a top-level item.</summary>
    public static string? TopicOf(AdminRoute route) =>
        route is AdminRoute.ServerLog or AdminRoute.ClientDiagnostics or AdminRoute.LoggingSettings ? LogsTopicId : null;

    /// <summary>The phone header's page name (WebUI <c>resolveAdminHeaderTitle</c>).</summary>
    public static string HeaderTitle(AdminRoute route) => route switch
    {
        AdminRoute.Activity => AdminText.T("web.admin.common.title.header.activity"),
        AdminRoute.ServerLog => AdminText.T("web.admin.common.title.header.logs"),
        AdminRoute.ClientDiagnostics => AdminText.T("web.admin.common.title.header.diagnostics"),
        AdminRoute.LoggingSettings => AdminText.T("web.admin.common.title.header.settings"),
        _ => AdminText.T("web.admin.shell.mobileHeader.dashboardTitle"),
    };

    /// <summary>The window title (WebUI tab title, before the server name).</summary>
    public static string TabTitle(AdminRoute route) => route switch
    {
        AdminRoute.Activity => AdminText.T("web.admin.common.title.tab.activity"),
        AdminRoute.ServerLog => AdminText.T("web.admin.common.title.tab.logs"),
        AdminRoute.ClientDiagnostics => AdminText.T("web.admin.common.title.tab.diagnostics"),
        AdminRoute.LoggingSettings => AdminText.T("web.admin.common.title.tab.settings"),
        _ => AdminText.T("web.admin.common.title.tab.admin"),
    };

    /// <summary>The sidebar label of a route (sub-items use the shorter topic names).</summary>
    public static string SidebarLabel(AdminRoute route) => route switch
    {
        AdminRoute.Activity => AdminText.T("web.admin.shell.nav.page.activity.label"),
        AdminRoute.ServerLog => AdminText.T("web.admin.shell.nav.item.serverLog"),
        AdminRoute.ClientDiagnostics => AdminText.T("web.admin.shell.nav.item.clientDiagnostics"),
        AdminRoute.LoggingSettings => AdminText.T("web.admin.shell.nav.item.settings"),
        _ => AdminText.T("web.admin.shell.nav.page.dashboard.label"),
    };

    /// <summary>The scoped admin search: only the five Monitor pages (a recorded deviation from the full palette).</summary>
    public static IReadOnlyList<AdminNavPage> SearchPages { get; } =
    [
        new(AdminRoute.Dashboard, AdminText.T("web.admin.shell.nav.page.dashboard.label"),
            AdminText.T("web.admin.shell.nav.page.dashboard.description"), "layout-dashboard", ["overview", "stats", "health", "scan all"]),
        new(AdminRoute.Activity, AdminText.T("web.admin.shell.nav.page.activity.label"),
            AdminText.T("web.admin.shell.nav.page.activity.description"), "radio", ["streams", "sessions", "now playing", "transcode"]),
        new(AdminRoute.ServerLog, AdminText.T("web.admin.shell.nav.item.serverLog"),
            AdminText.T("web.admin.shell.nav.page.logs.description"), "scroll-text", ["logs", "server logs", "debug", "tail", "events"]),
        new(AdminRoute.ClientDiagnostics, AdminText.T("web.admin.shell.nav.item.clientDiagnostics"),
            AdminText.T("web.admin.shell.nav.page.diagnostics.description"), "scroll-text", ["diagnostics", "crash reports", "debug bundles", "support"]),
        new(AdminRoute.LoggingSettings, AdminText.T("web.admin.shell.nav.item.settings"),
            AdminText.T("web.admin.settings.search.logging.description"), "scroll-text",
            ["settings", "configuration", AdminText.T("web.admin.settings.search.logging.label"), "log level", "log retention", "quiet log prefixes", "permission checks"]),
    ];

    /// <summary>Case-insensitive all-words match over label, description and keywords, as the palette filter.</summary>
    public static IReadOnlyList<AdminNavPage> Search(string? query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return SearchPages;
        return SearchPages.Where(page =>
        {
            var haystack = $"{page.Label} {page.Description} {string.Join(' ', page.Keywords)}";
            return words.All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
        }).ToArray();
    }

    /// <summary>
    /// Build card value (AdminSidebar): "{number} · {display}" when the build
    /// number is positive, the display alone, else dev build / loading / load failed.
    /// </summary>
    public static string BuildDisplay(AdminIndicatorBuildInfo? info, bool loading, bool failed)
    {
        if (loading && info == null) return AdminText.T("web.admin.shell.sidebar.build.loading");
        if (failed) return AdminText.T("web.admin.shell.sidebar.build.loadFailed");
        if (info is { Available: true })
            return info.BuildNumber > 0 ? $"{info.BuildNumber} · {info.Display}" : info.Display;
        return AdminText.T("web.admin.shell.sidebar.build.dev");
    }

    /// <summary>Hover text of the build card, or null without a build time.</summary>
    public static string? BuildTitle(AdminIndicatorBuildInfo? info) =>
        string.IsNullOrEmpty(info?.BuiltAt) ? null : AdminText.T("web.admin.shell.sidebar.build.builtAt", new { built_at = info!.BuiltAt });
}

/// <summary>
/// Which sidebar topics are open (WebUI <c>useAdminNavTopics</c>): the topic
/// holding the current page opens by itself, even if the admin closed it on an
/// earlier visit; every other topic keeps the admin's last choice. A toggle
/// merges into the stored choices instead of overwriting them, so the desktop
/// sidebar and the phone drawer never undo each other.
/// </summary>
public sealed class AdminNavTopicState
{
    private readonly Func<IReadOnlyDictionary<string, bool>> _read;
    private readonly Action<IReadOnlyDictionary<string, bool>> _write;
    private Dictionary<string, bool> _choices;
    private string? _active;

    public AdminNavTopicState(Func<IReadOnlyDictionary<string, bool>> read, Action<IReadOnlyDictionary<string, bool>> write, string? activeTopic)
    {
        _read = read;
        _write = write;
        _active = activeTopic;
        _choices = WithTopicOpen(Snapshot(), activeTopic);
    }

    private Dictionary<string, bool> Snapshot()
    {
        try { return new Dictionary<string, bool>(_read(), StringComparer.Ordinal); }
        catch { return new Dictionary<string, bool>(StringComparer.Ordinal); }
    }

    private static Dictionary<string, bool> WithTopicOpen(Dictionary<string, bool> choices, string? topic)
    {
        if (topic != null && choices.TryGetValue(topic, out var open) && !open) choices[topic] = true;
        return choices;
    }

    /// <summary>Arriving in another topic reopens it if the admin had closed it.</summary>
    public void Arrive(string? activeTopic)
    {
        if (_active == activeTopic) return;
        _active = activeTopic;
        _choices = WithTopicOpen(_choices, activeTopic);
    }

    public bool IsOpen(string topicId) =>
        _choices.TryGetValue(topicId, out var open) ? open : topicId == _active;

    public void Toggle(string topicId)
    {
        var open = !IsOpen(topicId);
        var stored = Snapshot();
        stored[topicId] = open;
        try { _write(stored); } catch { /* the choice lasts for this session */ }
        _choices[topicId] = open;
    }
}
