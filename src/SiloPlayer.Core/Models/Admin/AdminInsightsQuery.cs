namespace SiloPlayer.Core.Models.Admin;

/// <summary>The Insights time window presets (<c>range</c> query parameter, official <c>INSIGHTS_RANGES</c> order).</summary>
public enum AdminInsightsRange
{
    Days7,
    Days30,
    Days90,
    Year1,
    All,
    Custom,
}

/// <summary>Wire names of <see cref="AdminInsightsRange"/>: <c>7d</c>, <c>30d</c>, <c>90d</c>, <c>1y</c>, <c>all</c>, <c>custom</c>.</summary>
public static class AdminInsightsRanges
{
    /// <summary>The server default and the page default; omitted from the page URL.</summary>
    public const AdminInsightsRange Default = AdminInsightsRange.Days30;

    /// <summary>Every range in the order the toolbar lists them.</summary>
    public static IReadOnlyList<AdminInsightsRange> Ordered { get; } =
        [AdminInsightsRange.Days7, AdminInsightsRange.Days30, AdminInsightsRange.Days90, AdminInsightsRange.Year1, AdminInsightsRange.All, AdminInsightsRange.Custom];

    public static string Wire(this AdminInsightsRange range) => range switch
    {
        AdminInsightsRange.Days7 => "7d",
        AdminInsightsRange.Days30 => "30d",
        AdminInsightsRange.Days90 => "90d",
        AdminInsightsRange.Year1 => "1y",
        AdminInsightsRange.All => "all",
        AdminInsightsRange.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown Insights range."),
    };

    /// <summary>Exact, case-sensitive match as the official <c>INSIGHTS_RANGES.includes</c>; anything else is not a range.</summary>
    public static bool TryParse(string? wire, out AdminInsightsRange range)
    {
        foreach (var candidate in Ordered)
            if (string.Equals(candidate.Wire(), wire, StringComparison.Ordinal)) { range = candidate; return true; }
        range = Default;
        return false;
    }

    /// <summary>The message key of a range button label (<c>web.admin.insights.range.*</c>).</summary>
    public static string LabelKey(this AdminInsightsRange range) => "web.admin.insights.range." + range.Wire();
}

/// <summary>The shared Insights window and row scope sent as query parameters (official <c>buildInsightsWindowQueryParams</c>).</summary>
/// <remarks>
/// <see cref="FromDate"/> and <see cref="ToDate"/> (<c>YYYY-MM-DD</c> calendar dates in <see cref="Tz"/>, inclusive) are sent only for
/// <see cref="AdminInsightsRange.Custom"/>. <see cref="ProfileId"/> needs <see cref="UserId"/> because profile ids repeat across accounts.
/// Ids are the decimal strings of positive integers. Each read sends only the members it accepts: People ignores the account and
/// profile, Bandwidth also ignores the library.
/// </remarks>
public sealed record AdminInsightsWindow
{
    public AdminInsightsRange Range { get; init; } = AdminInsightsRanges.Default;
    /// <summary>IANA time zone; empty is read as UTC (the server default).</summary>
    public string Tz { get; init; } = "UTC";
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public string? LibraryId { get; init; }
    public string? UserId { get; init; }
    public string? ProfileId { get; init; }
}

/// <summary>The Titles ranking (<c>sort</c> query parameter).</summary>
public enum AdminInsightsTitleSort
{
    Plays,
    WatchTime,
    Viewers,
}

public static class AdminInsightsTitleSorts
{
    public static string Wire(this AdminInsightsTitleSort sort) => sort switch
    {
        AdminInsightsTitleSort.Plays => "plays",
        AdminInsightsTitleSort.WatchTime => "watch_time",
        AdminInsightsTitleSort.Viewers => "viewers",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown Insights title sort."),
    };

    /// <summary>The server's own normalisation: <c>watch_time</c> and <c>viewers</c> stand, anything else is <c>plays</c>.</summary>
    public static AdminInsightsTitleSort Parse(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "watch_time" => AdminInsightsTitleSort.WatchTime,
        "viewers" => AdminInsightsTitleSort.Viewers,
        _ => AdminInsightsTitleSort.Plays,
    };

    /// <summary>The label key of the rank-by toggle (<c>web.admin.insights.titles.sort.*</c>).</summary>
    public static string LabelKey(this AdminInsightsTitleSort sort) => sort switch
    {
        AdminInsightsTitleSort.WatchTime => "web.admin.insights.titles.sort.watchTime",
        AdminInsightsTitleSort.Viewers => "web.admin.insights.titles.sort.viewers",
        _ => "web.admin.insights.titles.sort.plays",
    };
}

/// <summary>The People table's sort column (<c>sort</c> query parameter).</summary>
public enum AdminInsightsPeopleSort
{
    WatchTime,
    Plays,
    DirectPct,
    CompletionPct,
    LastSeen,
}

public static class AdminInsightsPeopleSorts
{
    public static string Wire(this AdminInsightsPeopleSort sort) => sort switch
    {
        AdminInsightsPeopleSort.WatchTime => "watch_time",
        AdminInsightsPeopleSort.Plays => "plays",
        AdminInsightsPeopleSort.DirectPct => "direct_pct",
        AdminInsightsPeopleSort.CompletionPct => "completion_pct",
        AdminInsightsPeopleSort.LastSeen => "last_seen",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Unknown Insights people sort."),
    };
}

/// <summary>People paging and ordering (everything but the shared window). Defaults are the official page's first request.</summary>
/// <param name="Search">Case-insensitive match on account usernames and profile names; trimmed, empty means none, at most 128 characters.</param>
/// <param name="Descending">The server default and the page default.</param>
/// <param name="Limit">Accounts per page, 1 to 100; the page asks for 50. The dashboard asks for 1 and reads only <c>top_profiles</c>.</param>
/// <param name="Offset">Accounts to skip, 0 to 10000.</param>
public sealed record AdminInsightsPeopleQuery(
    string? Search = null,
    AdminInsightsPeopleSort Sort = AdminInsightsPeopleSort.WatchTime,
    bool Descending = true,
    int Limit = 50,
    int Offset = 0);
