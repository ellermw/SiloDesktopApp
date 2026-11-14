using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum ActivitySortField { Username, Media, Method, Node, Started }

public sealed record ActivityMethodCount(ActivityMethodMeta Meta, int Count);

public sealed record ActivityRouteNodeCount(ActivityRouteNode Node, int Count);

/// <summary>
/// The Activity page's local search, filters and sort (WebUI AdminActivity.tsx).
/// Everything is client-side over the drained session list; no server filter or
/// sort argument is invented. Sort keys and search use English text plus the
/// shown text, so a language change neither reorders rows nor loses a query.
/// </summary>
public sealed class AdminActivityListState
{
    public string Search { get; set; } = "";
    public string? MethodFilter { get; set; }
    public string? NodeFilter { get; set; }
    /// <summary>"movie" or "series".</summary>
    public string? TypeFilter { get; set; }
    public ActivitySortField SortField { get; private set; } = ActivitySortField.Started;
    public bool SortAscending { get; private set; }

    public int ActiveFilterCount => new[] { MethodFilter, NodeFilter, TypeFilter }.Count(value => value != null);
    public bool ShowsFilterStatus => Search.Length > 0 || ActiveFilterCount > 0;

    /// <summary>Same field flips direction; a new field starts ascending except Time, which starts descending.</summary>
    public void ToggleSort(ActivitySortField field)
    {
        if (SortField == field) { SortAscending = !SortAscending; return; }
        SortField = field;
        SortAscending = field != ActivitySortField.Started;
    }

    public void ToggleMethod(string method) => MethodFilter = MethodFilter == method ? null : method;
    public void ToggleNode(string key) => NodeFilter = NodeFilter == key ? null : key;
    public void ToggleType(string type) => TypeFilter = TypeFilter == type ? null : type;

    /// <summary>"Clear filters" resets method, node and type, not the search text.</summary>
    public void ClearFilters()
    {
        MethodFilter = null;
        NodeFilter = null;
        TypeFilter = null;
    }

    public static IReadOnlyList<ActivityMethodCount> MethodCounts(IEnumerable<AdminActivitySession> sessions) =>
        sessions.GroupBy(AdminActivityPresentation.ClassifyActivityMethod)
            .OrderBy(group => group.Key, Comparer<string>.Create(AdminActivityPresentation.CompareActivityMethods))
            .Select(group => new ActivityMethodCount(AdminActivityPresentation.MethodMeta(group.Key), group.Count()))
            .ToArray();

    /// <summary>Route nodes across sessions, first-seen order (the summary chips sort by count).</summary>
    public static IReadOnlyList<ActivityRouteNodeCount> RouteNodeCounts(IEnumerable<AdminActivitySession> sessions)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, (ActivityRouteNode Node, int Count)>(StringComparer.Ordinal);
        foreach (var session in sessions)
        foreach (var node in AdminActivityPresentation.GetSessionRouteNodes(session))
        {
            if (!counts.TryGetValue(node.Key, out var current)) { order.Add(node.Key); current = (node, 0); }
            counts[node.Key] = (node, current.Count + 1);
        }
        return order.Select(key => new ActivityRouteNodeCount(counts[key].Node, counts[key].Count)).ToArray();
    }

    public IReadOnlyList<AdminActivitySession> Apply(IReadOnlyList<AdminActivitySession> sessions)
    {
        IEnumerable<AdminActivitySession> result = sessions;
        if (Search.Length > 0)
        {
            var q = Search.ToLower(CultureInfo.CurrentCulture);
            result = result.Where(s => Matches(s, q));
        }
        if (MethodFilter != null) result = result.Where(s => AdminActivityPresentation.ClassifyActivityMethod(s) == MethodFilter);
        if (NodeFilter != null) result = result.Where(s => AdminActivityPresentation.GetSessionRouteNodes(s).Any(n => n.Key == NodeFilter));
        if (TypeFilter != null) result = result.Where(s => s.MediaType == TypeFilter);
        var list = result.ToList();
        // Stable, as Array.prototype.sort is.
        var indexed = list.Select((session, index) => (session, index)).ToList();
        indexed.Sort((a, b) =>
        {
            var cmp = Compare(a.session, b.session);
            if (!SortAscending) cmp = -cmp;
            return cmp != 0 ? cmp : a.index.CompareTo(b.index);
        });
        return indexed.Select(item => item.session).ToArray();
    }

    private static bool Contains(string? value, string q) =>
        value != null && value.ToLower(CultureInfo.CurrentCulture).Contains(q, StringComparison.Ordinal);

    private static bool Matches(AdminActivitySession s, string q) =>
        Contains(s.Username, q) ||
        Contains(s.MediaTitle, q) ||
        Contains(s.SeriesName, q) ||
        Contains(s.EpisodeName, q) ||
        Contains(AdminActivityPresentation.GetSessionClientLabelFull(s), q) ||
        Contains(s.ClientUserAgent, q) ||
        Contains(s.ClientIp, q) ||
        AdminActivityPresentation.GetSessionRouteNodes(s).Any(node => Contains($"{node.Label} {node.Name}", q));

    private int Compare(AdminActivitySession a, AdminActivitySession b) => SortField switch
    {
        ActivitySortField.Username => string.Compare(a.Username ?? "", b.Username ?? "", StringComparison.CurrentCulture),
        ActivitySortField.Media => string.Compare(AdminActivityPresentation.GetPlaybackSessionTitle(a),
            AdminActivityPresentation.GetPlaybackSessionTitle(b), StringComparison.CurrentCulture),
        ActivitySortField.Method => AdminActivityPresentation.CompareActivityMethods(
            AdminActivityPresentation.ClassifyActivityMethod(a), AdminActivityPresentation.ClassifyActivityMethod(b)),
        ActivitySortField.Node => string.Compare(RouteSortValue(a), RouteSortValue(b), StringComparison.CurrentCulture),
        _ => StartedMs(a).CompareTo(StartedMs(b)),
    };

    private static string RouteSortValue(AdminActivitySession s) =>
        string.Join(" ", AdminActivityPresentation.GetSessionRouteNodes(s).Select(node => $"{node.Label} {node.Name}"));

    private static long StartedMs(AdminActivitySession s) =>
        DateTimeOffset.TryParse(s.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
            ? started.ToUnixTimeMilliseconds() : 0;

    /// <summary>Header subtitle: "No active streams" or "{n} active streams across {m} nodes".</summary>
    public static string Subtitle(IReadOnlyList<AdminActivitySession> sessions) => sessions.Count == 0
        ? AdminText.T("web.admin.activity.page.noStreams")
        : AdminText.T("web.admin.activity.page.subtitle", new Dictionary<string, object?>
        {
            ["count"] = sessions.Count,
            ["count_text"] = AdminText.FormatNumber(sessions.Count),
            ["nodes"] = RouteNodeCounts(sessions).Count,
            ["nodes_text"] = AdminText.FormatNumber(RouteNodeCounts(sessions).Count),
        });

    public static string LiveCount(int count) =>
        AdminText.T("web.admin.activity.page.liveCount", AdminText.Count(count));

    public static string ShowingText(int shown, int total) =>
        AdminText.T("web.admin.activity.filter.showing", new Dictionary<string, object?>
        {
            ["total"] = total,
            ["shown"] = shown,
            ["total_text"] = AdminText.FormatNumber(total),
        });
}
