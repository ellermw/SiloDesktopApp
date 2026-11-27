using System.Globalization;
using System.Text;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Stable admin route identities (WebUI <c>ADMIN_NAV_SECTIONS</c>): the five
/// Monitor pages, Manage's twenty-one and Configure's nineteen sidebar leaves. A route is an

/// identity, not a promise of a page: the shell shows and opens only routes
/// with an <see cref="AdminPageRegistration"/>, so no dead navigation ships.
/// Values are appended only; never renumber.
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

    /// <summary>/admin/libraries (Libraries › Libraries)</summary>
    Libraries,
    /// <summary>/admin/collections, /new, /:id/edit (Libraries › Collections)</summary>
    Collections,
    /// <summary>/admin/sections (Libraries › Home Sections; legacy /admin/home-rows)</summary>
    HomeSections,
    /// <summary>/admin/subtitles (Libraries › Subtitle Files)</summary>
    SubtitleFiles,
    /// <summary>/admin/settings/library (Libraries › Settings)</summary>
    LibrarySettings,
    /// <summary>/admin/autoscan/activity (Autoscan › Activity)</summary>
    AutoscanActivity,
    /// <summary>/admin/autoscan, exact (Autoscan › Sources)</summary>
    AutoscanSources,
    /// <summary>/admin/settings/autoscan (Autoscan › Settings)</summary>
    AutoscanSettings,
    /// <summary>/admin/requests, exact (Requests › Queue)</summary>
    RequestQueue,
    /// <summary>/admin/requests/discover (Requests › Discover)</summary>
    RequestDiscover,
    /// <summary>/admin/settings/requests (Requests › Settings)</summary>
    RequestSettings,
    /// <summary>/admin/users, /:id (Users › Users)</summary>
    Users,
    /// <summary>/admin/access-groups, /:id (Users › Access Groups)</summary>
    AccessGroups,
    /// <summary>/admin/devices, /:userId/:deviceId (Users › Devices)</summary>
    Devices,
    /// <summary>/admin/history-import (Users › History Import)</summary>
    HistoryImport,
    /// <summary>/admin/history (Playback › History)</summary>
    PlaybackHistory,
    /// <summary>/admin/marker-history (Playback › Skip Markers)</summary>
    SkipMarkers,
    /// <summary>/admin/nodes (Playback › Nodes)</summary>
    Nodes,
    /// <summary>/admin/settings/playback (Playback › Settings)</summary>
    PlaybackSettings,
    /// <summary>/admin/downloads (Downloads › Prepared Files; default tab is storage)</summary>
    PreparedFiles,
    /// <summary>/admin/settings/downloads (Downloads › Settings)</summary>
    DownloadSettings,

    /// <summary>/admin/settings/general (Configure › Server › General)</summary>
    GeneralSettings,
    AppearanceSettings,
    StorageSettings,
    SearchSettings,
    NetworkAccessSettings,
    ScheduledTasks,
    Maintenance,
    Backups,
    SignInSettings,
    SecuritySettings,
    ApiKeys,
    Policy,
    Plugins,
    ProviderSettings,
    EmailSettings,
    NotificationSettings,
    AiSettings,
    Recommendations,
    CompatibilitySettings,

    /// <summary>/admin/insights (Monitor › Insights; official c59798116). Appended: the live Activity page it replaces keeps its value. No page is registered for it yet, so the shell neither lists nor opens it.</summary>
    Insights,
}

/// <summary>
/// Navigation parameter every admin page receives: the WebUI location of the
/// route. <see cref="Segments"/> are the descendant path segments below the
/// leaf (<c>["new"]</c>, <c>[id, "edit"]</c>, <c>[userId]</c>,
/// <c>[userId, deviceId]</c>); <see cref="Query"/> mirrors the URL search
/// parameters, so pages own their URL-state parsing exactly as upstream.
/// Equality is by value (route, segments, query), never by instance.
/// </summary>
public sealed record AdminRouteRequest(AdminRoute Route, IReadOnlyDictionary<string, string>? Query = null)
{
    private readonly IReadOnlyList<string> _segments = [];
    private readonly IReadOnlyDictionary<string, string>? _query = CopyQuery(Query);

    public IReadOnlyDictionary<string, string>? Query
    {
        get => _query;
        init => _query = CopyQuery(value);
    }

    private static IReadOnlyDictionary<string, string>? CopyQuery(IReadOnlyDictionary<string, string>? query) =>
        query is null ? null : new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            query.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    /// <summary>Descendant path segments below the leaf (unescaped).</summary>
    public IReadOnlyList<string> Segments
    {
        get => _segments;
        init => _segments = Array.AsReadOnly(value?.ToArray() ?? []);
    }

    public string? Get(string key) => Query != null && Query.TryGetValue(key, out var value) ? value : null;

    /// <summary>The descendant segment at <paramref name="index"/>, or null.</summary>
    public string? Segment(int index) => index >= 0 && index < _segments.Count ? _segments[index] : null;

    /// <summary>The canonical WebUI href of this location.</summary>
    public string Href => AdminRouteCatalog.ToHref(this);

    /// <summary>The href, without throwing for an undefined route value (logs, test names).</summary>
    public override string ToString() => Enum.IsDefined(Route) ? Href : Route.ToString();

    /// <summary>This location with one query value set, or removed when <paramref name="value"/> is null or empty.</summary>
    public AdminRouteRequest WithQuery(string key, string? value)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Query ?? EmptyQuery) query[pair.Key] = pair.Value;
        if (string.IsNullOrEmpty(value)) query.Remove(key);
        else query[key] = value;
        return this with { Query = query.Count > 0 ? query : null };
    }

    /// <summary>/admin/collections/new</summary>
    public static AdminRouteRequest CollectionNew() => new(AdminRoute.Collections) { Segments = ["new"] };
    /// <summary>/admin/collections/:id/edit</summary>
    public static AdminRouteRequest CollectionEditor(string collectionId) => new(AdminRoute.Collections) { Segments = [collectionId, "edit"] };
    /// <summary>/admin/users/:id, with optional URL state such as <c>tab</c>.</summary>
    public static AdminRouteRequest UserDetail(string userId, IReadOnlyDictionary<string, string>? query = null) =>
        new(AdminRoute.Users, query) { Segments = [userId] };
    /// <summary>/admin/access-groups/:id</summary>
    public static AdminRouteRequest AccessGroup(string groupId) => new(AdminRoute.AccessGroups) { Segments = [groupId] };
    /// <summary>/admin/devices/:userId/:deviceId</summary>
    public static AdminRouteRequest Device(string userId, string deviceId) => new(AdminRoute.Devices) { Segments = [userId, deviceId] };

    public bool Equals(AdminRouteRequest? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || Route != other.Route || !_segments.SequenceEqual(other._segments, StringComparer.Ordinal)) return false;
        var left = Query ?? EmptyQuery;
        var right = other.Query ?? EmptyQuery;
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Route);
        foreach (var segment in _segments) hash.Add(segment, StringComparer.Ordinal);
        foreach (var pair in (Query ?? EmptyQuery).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyQuery = new Dictionary<string, string>();
}

public sealed record AdminNavPage(AdminRoute Route, string Label, string Description, string Icon, IReadOnlyList<string> Keywords);

/// <summary>A command-palette group: a section's loose pages, or one topic's pages.</summary>
public sealed record AdminNavSearchGroup(string LabelKey, IReadOnlyList<AdminNavPage> Pages)
{
    public string Label => AdminText.T(LabelKey);
}

/// <summary>Sidebar badge kinds (AdminSidebar): live sessions, a count pill (pending requests), a settings attention dot.</summary>
public enum AdminNavBadgeKind { Live, Count, Attention }

/// <summary>One sidebar badge. <see cref="AccessibleLabel"/> is the already-localized screen-reader text.</summary>
public sealed record AdminNavBadge(AdminNavBadgeKind Kind, long Count, string AccessibleLabel);

/// <summary>Shell navigation facts shared by the sidebar, phone header, window title and search.</summary>
public static class AdminNavigation
{
    public const string LogsTopicId = "logs";

    /// <summary>The topic a route sits under, or null for a top-level item.</summary>
    public static string? TopicOf(AdminRoute route) => AdminRouteCatalog.TopicOf(route)?.Id;

    /// <summary>The phone header's page name (WebUI <c>resolveAdminHeaderTitle</c>).</summary>
    public static string HeaderTitle(AdminRoute route) => HeaderTitle(new AdminRouteRequest(route));

    /// <summary>The window title (WebUI tab title, before the server name).</summary>
    public static string TabTitle(AdminRoute route) => TabTitle(new AdminRouteRequest(route));

    /// <summary>The phone header's name for a location, including descendant pages (WebUI <c>adminTitle</c>).</summary>
    public static string HeaderTitle(AdminRouteRequest request) => AdminText.T(TitleKeys(request).Header);

    /// <summary>The window title for a location, including descendant pages.</summary>
    public static string TabTitle(AdminRouteRequest request) => AdminText.T(TitleKeys(request).Tab);

    /// <summary>Message keys of a location's titles (descendants: collection new/edit, account detail).</summary>
    public static (string Header, string Tab) TitleKeys(AdminRouteRequest request)
    {
        // documentTitle checks "new" before ":id/edit", so /collections/new/edit is titled as new.
        if (request.Route == AdminRoute.Collections && request.Segment(0) == "new")
            return ("web.admin.common.title.tab.collectionNew", "web.admin.common.title.tab.collectionNew");
        if (request.Route == AdminRoute.Collections && request.Segment(1) == "edit")
            return ("web.admin.common.title.tab.collectionEdit", "web.admin.common.title.tab.collectionEdit");
        if (request.Route == AdminRoute.Users && request.Segments.Count > 0)
            return ("web.admin.common.title.header.user", "web.admin.common.title.tab.user");
        if (request.Route == AdminRoute.ScheduledTasks && request.Segments.Count > 0)
            return ("web.admin.common.title.header.task", "web.admin.common.title.tab.task");
        if (request.Route == AdminRoute.Plugins && request.Segments.Count > 0)
            return ("web.admin.common.title.header.plugin", "web.admin.common.title.tab.plugin");

        var leaf = AdminRouteCatalog.Leaf(request.Route);
        return (leaf.HeaderTitleKey, leaf.TabTitleKey);
    }

    /// <summary>The sidebar label of a route (sub-items use the shorter topic names).</summary>
    public static string SidebarLabel(AdminRoute route) => AdminText.T(AdminRouteCatalog.Leaf(route).SidebarLabelKey);

    /// <summary>Routes the shell always provides without a registration.</summary>
    public static IReadOnlyList<AdminRoute> MonitorRoutes { get; } =
        [AdminRoute.Dashboard, AdminRoute.Activity, AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings];

    private static bool IsMonitor(AdminRoute route) => MonitorRoutes.Contains(route);

    /// <summary>The Monitor pages, as the scoped search lists them with no other registrations.</summary>
    public static IReadOnlyList<AdminNavPage> SearchPages => SearchGroups(null, IsMonitor).SelectMany(group => group.Pages).ToArray();

    /// <summary>Search over the Monitor pages only (no Manage registrations).</summary>
    public static IReadOnlyList<AdminNavPage> Search(string? query) => SearchGroups(query, IsMonitor).SelectMany(group => group.Pages).ToArray();

    /// <summary>
    /// The command palette (WebUI <c>buildAdminCommandNavSections</c> +
    /// <c>filterSettingsSearchGroups</c>): each section's loose pages under the
    /// section, each topic as its own group, so a result reads "Requests" over
    /// "Settings". Only <paramref name="isAvailable"/> routes are listed. A query
    /// matching a group's name lists the whole group; otherwise a page matches
    /// when every token is a word prefix (or, from four letters, a word
    /// substring) of its group, label, description, keywords or indexed settings.
    /// </summary>
    public static IReadOnlyList<AdminNavSearchGroup> SearchGroups(string? query, Func<AdminRoute, bool> isAvailable)
    {
        var tokens = SearchTokens(query ?? "");
        var groups = new List<AdminNavSearchGroup>();
        foreach (var section in AdminRouteCatalog.Sections)
        {
            var loose = section.Entries.OfType<AdminNavLeaf>().Where(leaf => isAvailable(leaf.Route)).ToArray();
            if (loose.Length > 0) groups.Add(Filter(section.LabelKey, loose));
            foreach (var topic in section.Entries.OfType<AdminNavTopic>())
            {
                var leaves = topic.Leaves.Where(leaf => isAvailable(leaf.Route)).ToArray();
                if (leaves.Length > 0) groups.Add(Filter(topic.LabelKey, leaves));
            }
        }
        return groups.Where(group => group.Pages.Count > 0).ToArray();

        AdminNavSearchGroup Filter(string groupKey, IReadOnlyList<AdminNavLeaf> leaves)
        {
            var groupLabel = AdminText.T(groupKey);
            var groupMatches = tokens.Length == 0 || Matches(Normalize(groupLabel), tokens);
            var pages = leaves.Where(leaf => groupMatches || Matches(Normalize(SearchText(groupLabel, leaf)), tokens)).Select(ToPage).ToArray();
            return new AdminNavSearchGroup(groupKey, pages);
        }
    }

    private static AdminNavPage ToPage(AdminNavLeaf leaf) => new(leaf.Route, SidebarLabel(leaf.Route), AdminText.T(leaf.DescriptionKey),
        leaf.Icon, [.. leaf.Keywords, .. leaf.KeywordKeys.Select(key => AdminText.T(key))]);

    private static string SearchText(string groupLabel, AdminNavLeaf leaf) => string.Join(' ',
        new[] { groupLabel, SidebarLabel(leaf.Route), AdminText.T(leaf.DescriptionKey) }
            .Concat(leaf.Keywords).Concat(leaf.KeywordKeys.Select(key => AdminText.T(key))).Concat(leaf.SettingKeys.Select(key => AdminText.T(key))));

    private static string[] SearchTokens(string query)
    {
        var normalized = Normalize(query);
        return normalized.Length == 0 ? [] : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>settingsSearch <c>normalizeSearchText</c>: lower case, accents removed, anything but letters/digits/marks becomes a space.</summary>
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLower(CultureInfo.CurrentCulture).Normalize(NormalizationForm.FormKD))
        {
            if (ch is >= '̀' and <= 'ͯ') continue;
            var category = char.GetUnicodeCategory(ch);
            var wordChar = char.IsLetterOrDigit(ch) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
            builder.Append(wordChar ? ch : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Matches(string text, string[] tokens)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(token => words.Any(word => word.StartsWith(token, StringComparison.Ordinal)
            || (token.Length >= 4 && word.Contains(token, StringComparison.Ordinal))));
    }

    /// <summary>A closed topic shows the first badge among its shown pages, so a pending request is never folded away.</summary>
    public static AdminNavBadge? TopicBadge(AdminNavTopic topic, bool open, Func<AdminRoute, bool> isShown, Func<AdminRoute, AdminNavBadge?> badgeOf) =>
        open ? null : topic.Leaves.Where(leaf => isShown(leaf.Route)).Select(leaf => badgeOf(leaf.Route)).FirstOrDefault(badge => badge != null);

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
