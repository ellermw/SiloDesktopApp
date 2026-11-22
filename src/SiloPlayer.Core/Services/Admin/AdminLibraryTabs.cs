using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The Libraries page's three tabs (official <c>LibrariesTab</c> in <c>pages/AdminLibraries.tsx</c>).</summary>
public enum AdminLibrariesTab { Libraries, Activity, Health }

/// <summary>The Catalog Health tab's five tiles (official <c>HealthSectionId</c>).</summary>
public enum AdminHealthSection { Unmatched, Matcher, Ambiguous, Skipped, Stale }

/// <summary>A key that moves between tabs: the arrows follow the strip's direction, Home and End jump.</summary>
public enum AdminTabKey { Previous, Next, First, Last }

/// <summary>One Scan Activity refresh row: a running or queued metadata refresh job and the library it belongs to.</summary>
public sealed record AdminLibraryRefreshEntry(string LibraryId, AdminLibrary? Library, AdminLibraryJob Job);

/// <summary>One Scan Activity library filter chip (official <c>filterEntries</c>).</summary>
public sealed record AdminActivityFilterEntry(string LibraryId, string Name, int Count);

/// <summary>What the Scan Activity tab shows for the current read states (official <c>LibraryActivityPanel</c> early returns).</summary>
public enum AdminActivityPanelKind
{
    /// <summary>Scans or refreshes are active: filter chips, refresh rows, per-library cards, then the footer.</summary>
    Populated,
    /// <summary>Nothing active and the refresh jobs have not arrived yet: the empty state would read as a fact before it is one.</summary>
    Loading,
    /// <summary>Nothing active and the refresh jobs read failed: only the footer (with its restart control) shows.</summary>
    FooterOnly,
    /// <summary>Nothing active: "No active scans or refreshes", then the footer (older jobs).</summary>
    Empty,
}

/// <summary>
/// The rules of the Libraries tabs that are not drawing (official <c>pages/AdminLibraries.tsx</c>,
/// <c>LibraryActivityPanel.tsx</c>, <c>LibraryCatalogHealthPanel.tsx</c>, <c>libraryActivityHelpers.ts</c> at 93794454b).
/// </summary>
public static class AdminLibraryTabs
{
    /// <summary><c>parseLibrariesTab</c>: only "activity" and "health" select another tab; anything else is Libraries.</summary>
    public static AdminLibrariesTab Parse(string? value) => value switch
    {
        "activity" => AdminLibrariesTab.Activity,
        "health" => AdminLibrariesTab.Health,
        _ => AdminLibrariesTab.Libraries,
    };

    /// <summary>The <c>tab</c> query value for a tab; null for the default (the official page deletes the parameter).</summary>
    public static string? Query(AdminLibrariesTab tab) => tab switch
    {
        AdminLibrariesTab.Activity => "activity",
        AdminLibrariesTab.Health => "health",
        _ => null,
    };

    /// <summary><c>parseHealthSectionId</c>: unknown values fall back to Unmatched.</summary>
    public static AdminHealthSection ParseHealth(string? value) => value switch
    {
        "matcher" => AdminHealthSection.Matcher,
        "ambiguous" => AdminHealthSection.Ambiguous,
        "skipped" => AdminHealthSection.Skipped,
        "stale" => AdminHealthSection.Stale,
        _ => AdminHealthSection.Unmatched,
    };

    /// <summary>The roving tab target for an arrow, Home or End (Radix tabs loop at the ends).</summary>
    public static int Navigate(int count, int current, AdminTabKey key)
    {
        if (count <= 0) return -1;
        var from = Math.Clamp(current, 0, count - 1);
        return key switch
        {
            AdminTabKey.First => 0,
            AdminTabKey.Last => count - 1,
            AdminTabKey.Next => (from + 1) % count,
            _ => (from - 1 + count) % count,
        };
    }

    /// <summary>The Catalog Health tab badge: the unmatched and matcher totals added, unknown only while neither is known.</summary>
    public static long? HealthBadge(long? unmatched, long? matcher) =>
        unmatched is null && matcher is null ? null : (unmatched ?? 0) + (matcher ?? 0);

    /// <summary>The unfiltered "awaiting" total of unmatched items (<c>unmatchedQuery.data.pages[0].total</c>), unknown until a read answered.</summary>
    public static long? UnmatchedTotal(AdminUnmatchedState state) => state.AwaitingTotal;

    /// <summary>The Unmatched tile shows its error mark only while no total is known (<c>isError &amp;&amp; data === undefined</c>).</summary>
    public static bool UnmatchedTileError(AdminUnmatchedState state) => state.TotalsError != null && state.AwaitingTotal == null;

    /// <summary>The summed matcher queue total, unknown until a read succeeded (an empty list is a known zero).</summary>
    public static long? MatcherTotal(AdminMatcherState state) => state.HasData ? state.Total : null;

    /// <summary>The Metadata Matcher tile shows its error mark only while no read has succeeded.</summary>
    public static bool MatcherTileError(AdminMatcherState state) => state.Error != null && !state.HasData;

    /// <summary>
    /// The Ambiguous Roots tile's count. The list reads one library at a time, so a count is
    /// published to the catalog-wide tile only when there is exactly one library; with several
    /// the tile stays on "Inspect" instead of presenting one library's count as the catalog's.
    /// </summary>
    public static long? AmbiguousTileCount(int libraryCount, bool countUnknown, long totalRoots) =>
        libraryCount == 1 && !countUnknown ? totalRoots : null;

    /// <summary>The Scan Activity panel's early returns, in the official order: error first, then loading.</summary>
    public static AdminActivityPanelKind PanelKind(int totalActive, bool refreshJobsError, bool refreshJobsLoading)
    {
        if (totalActive > 0) return AdminActivityPanelKind.Populated;
        if (refreshJobsError) return AdminActivityPanelKind.FooterOnly;
        return refreshJobsLoading ? AdminActivityPanelKind.Loading : AdminActivityPanelKind.Empty;
    }

    /// <summary><c>scanProgressPercent</c>: null without a positive total or a processed count; otherwise the rounded percent in 0–100.</summary>
    public static int? ScanProgressPercent(AdminLibraryScanRun scan)
    {
        if (scan.TotalFiles is not > 0 || scan.FilesProcessed is not { } processed) return null;
        return (int)Math.Max(0, Math.Min(100, Math.Round(processed * 100d / scan.TotalFiles.Value, MidpointRounding.AwayFromZero)));
    }

    /// <summary><c>libraryScanProgressPercent</c>: the mean percent over a library's running scans that report one, or null.</summary>
    public static int? LibraryScanProgressPercent(IEnumerable<AdminLibraryScanRun> scans)
    {
        var percents = scans.Where(scan => scan.IsRunning).Select(ScanProgressPercent).OfType<int>().ToArray();
        return percents.Length == 0 ? null : (int)Math.Round(percents.Average(), MidpointRounding.AwayFromZero);
    }

    /// <summary>Running or queued scans plus active refresh jobs: the Scan Activity tab's count and the summary strip.</summary>
    public static AdminActivityTotals Totals(IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes)
    {
        var scans = groups.Sum(group => group.Scans.Count);
        var running = groups.Sum(group => group.Running);
        var queued = groups.Sum(group => group.Queued);
        var runningRefreshes = refreshes.Count(entry => AdminLibraryPresentation.JobStatus(entry.Job) == "running");
        return new AdminActivityTotals(scans, running, queued, refreshes.Count, runningRefreshes);
    }

    /// <summary>The library filter chips: groups first (their scan counts), then refresh-only libraries; a library in both adds one per refresh.</summary>
    public static IReadOnlyList<AdminActivityFilterEntry> FilterEntries(IReadOnlyList<AdminScanQueueGroup> groups, IReadOnlyList<AdminLibraryRefreshEntry> refreshes, Func<string, AdminLibrary?, string> name)
    {
        var order = new List<string>();
        var map = new Dictionary<string, (string Name, int Count)>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            if (!map.ContainsKey(group.LibraryId)) order.Add(group.LibraryId);
            map[group.LibraryId] = (group.Name, group.Scans.Count);
        }
        foreach (var entry in refreshes)
        {
            if (map.TryGetValue(entry.LibraryId, out var existing)) map[entry.LibraryId] = (existing.Name, existing.Count + 1);
            else { order.Add(entry.LibraryId); map[entry.LibraryId] = (name(entry.LibraryId, entry.Library), 1); }
        }
        return order.Select(id => new AdminActivityFilterEntry(id, map[id].Name, map[id].Count)).ToArray();
    }

    /// <summary>The selected library filter, or null when it names a library that no longer has active work.</summary>
    public static string? EffectiveFilter(string? selected, IReadOnlyList<AdminActivityFilterEntry> entries) =>
        selected != null && entries.Any(entry => entry.LibraryId == selected) ? selected : null;
}

/// <summary>
/// Official <c>PathText</c>: a path that reads left to right, folder by folder, in any language,
/// while each folder name keeps its own direction (a name in Arabic reads right to left without
/// reversing the folders around it). Native text has no <c>&lt;bdi&gt;</c>, so each name is
/// wrapped in a first-strong isolate (U+2068 … U+2069), which is what <c>&lt;bdi&gt;</c> is.
/// For display only: never send, compare or copy this form.
/// </summary>
public static class AdminPathText
{
    /// <summary>Only the folders before the first this-many separators are isolated; the rest stays one run, so a request path with thousands of separators stays cheap.</summary>
    public const int MaxSeparators = 32;

    public const char FirstStrongIsolate = '⁨';
    public const char PopDirectionalIsolate = '⁩';

    /// <summary>Splits on "/", or on "\" in a Windows path with no "/", and keeps every character as written.</summary>
    public static string Isolate(string path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        var separator = path.Contains('/') ? '/' : '\\';
        var text = new System.Text.StringBuilder(path.Length + 16);
        var start = 0;
        for (var separators = 0; separators < MaxSeparators; separators++)
        {
            var end = path.IndexOf(separator, start);
            if (end < 0) break;
            if (end > start) Append(text, path.AsSpan(start, end - start));
            text.Append(separator);
            start = end + 1;
        }
        var rest = path.AsSpan(start);
        if (rest.Length > 0)
        {
            if (rest.Contains(separator)) text.Append(rest); else Append(text, rest);
        }
        return text.ToString();
    }

    private static void Append(System.Text.StringBuilder text, ReadOnlySpan<char> name) =>
        text.Append(FirstStrongIsolate).Append(name).Append(PopDirectionalIsolate);
}

/// <summary>
/// Ambiguous Roots labels (official #4994, <c>5e26ca784</c>): the API sends a root's inferred type and
/// confidence as English data, so a non-English admin read English words beside translated labels.
/// </summary>
public static class AdminRootLabels
{
    /// <summary><c>itemTypeLabel</c>: the localized kind, or the value as the server sent it when there is no label.</summary>
    public static string ItemType(string type) => type switch
    {
        "movie" => AppStrings.Get("web.search.typeMovie", fallback: "Movie"),
        "series" => AppStrings.Get("web.search.typeSeries", fallback: "Series"),
        "season" => AppStrings.Get("web.search.typeSeason", fallback: "Season"),
        "episode" => AppStrings.Get("web.search.typeEpisode", fallback: "Episode"),
        "ebook" => AppStrings.Get("web.search.typeEbook", fallback: "Ebook"),
        "audiobook" => AppStrings.Get("web.search.typeAudiobook", fallback: "Audiobook"),
        "manga" => AppStrings.Get("web.search.typeManga", fallback: "Manga"),
        _ => type,
    };

    /// <summary>The type column: the localized kind, or "Unknown" when the scanner sent none.</summary>
    public static string RootType(string inferredType) =>
        inferredType.Length > 0 ? ItemType(inferredType) : AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.unknownType");

    /// <summary>The confidence column: low, medium and high are translated; a level the scanner does not send shows as sent.</summary>
    public static string Confidence(string confidence) => confidence switch
    {
        "low" => AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.confidence.low"),
        "medium" => AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.confidence.medium"),
        "high" => AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.confidence.high"),
        _ => confidence,
    };
}

/// <summary>Counts over the active scans and refresh jobs.</summary>
public sealed record AdminActivityTotals(int Scans, int RunningScans, int QueuedScans, int Refreshes, int RunningRefreshes)
{
    /// <summary>Scans plus refreshes: the Scan Activity tab's "N active".</summary>
    public int Active => Scans + Refreshes;
    /// <summary>The tab's badge and the summary strip are tinted live while anything runs.</summary>
    public bool HasRunning => RunningScans > 0 || RunningRefreshes > 0;
}
