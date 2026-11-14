namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The window a metric widget covers. Named by period, not hours: one stored
/// value means one hour on a sampled chart and one day on a leaderboard.
/// </summary>
public enum DashboardWidgetRange { Hour, Day, Week, Month }

public sealed record DashboardRangeOptions(IReadOnlyList<DashboardWidgetRange> Allowed, DashboardWidgetRange Default);

/// <summary>
/// A widget's identity and the box it may occupy. Columns and rows are
/// independent axes; rows are 100px grid rows that only apply from the 1024px
/// breakpoint up. <see cref="CollapsedRows"/> is the height an idle widget
/// hands back outside customize mode.
/// </summary>
public sealed record DashboardWidgetDefinition(
    string Id,
    string TitleKey,
    string DescriptionKey,
    int MinSpan,
    int MaxSpan,
    int DefaultSpan,
    int MinRows,
    int MaxRows,
    int DefaultRows,
    int? CollapsedRows = null,
    DashboardRangeOptions? Ranges = null)
{
    public string Title => DashboardText.T(TitleKey);
    public string Description => DashboardText.T(DescriptionKey);
    public bool CanResize => MinSpan != MaxSpan || MinRows != MaxRows;
}

/// <summary>One placed widget. Range is present only for widgets that offer ranges.</summary>
public sealed record DashboardLayoutEntry(string Id, int Span, int Rows, DashboardWidgetRange? Range = null);

/// <summary>The 28 selectable widgets of the pinned WebUI registry, in registry order.</summary>
public static class DashboardRegistry
{
    public const string StatActiveStreams = "stat-active-streams";
    public const string StatEgressNow = "stat-egress-now";
    public const string StatTranscodeShare = "stat-transcode-share";
    public const string StatProfilesActive = "stat-profiles-active";
    public const string StatMovies = "stat-movies";
    public const string StatShows = "stat-shows";
    public const string StatAdvisoryCoverage = "stat-advisory-coverage";
    public const string StatUsers = "stat-users";
    public const string StatStorage = "stat-storage";
    public const string HealthStrip = "health-strip";
    public const string ServerResources = "server-resources";
    public const string Playback24h = "playback-24h";
    public const string ConcurrentStreams24h = "concurrent-streams-24h";
    public const string Egress24h = "egress-24h";
    public const string PlaybackReliability = "playback-reliability";
    public const string TopTitles = "top-titles";
    public const string TopProfiles = "top-profiles";
    public const string WatchProviders = "watch-providers";
    public const string NowPlaying = "now-playing";
    public const string TranscodeNodes = "transcode-nodes";
    public const string Scanner = "scanner";
    public const string Libraries = "libraries";
    public const string Users = "users";
    public const string Downloads = "downloads";
    public const string ScanActivity = "scan-activity";
    public const string RecentErrors = "recent-errors";
    public const string RecentActivity = "recent-activity";
    public const string ApiClients = "api-clients";

    /// <summary>Sampled charts read a month of samples and offer every window down to an hour.</summary>
    public static readonly DashboardRangeOptions SampledRanges = new(
        [DashboardWidgetRange.Hour, DashboardWidgetRange.Day, DashboardWidgetRange.Week, DashboardWidgetRange.Month],
        DashboardWidgetRange.Day);

    /// <summary>Leaderboards start at a day; the endpoint's window is measured in days.</summary>
    public static readonly DashboardRangeOptions LeaderboardRanges = new(
        [DashboardWidgetRange.Day, DashboardWidgetRange.Week, DashboardWidgetRange.Month],
        DashboardWidgetRange.Week);

    private const string Catalog = "web.admin.dashboard.widgetCatalog.";

    private static DashboardWidgetDefinition Stat(string id, string key) =>
        new(id, Catalog + key + ".title", Catalog + key + ".description", 2, 4, 2, 1, 2, 1);

    public static readonly IReadOnlyList<DashboardWidgetDefinition> Widgets =
    [
        Stat(StatActiveStreams, "statActiveStreams"),
        Stat(StatEgressNow, "statEgressNow"),
        Stat(StatTranscodeShare, "statTranscodeShare"),
        Stat(StatProfilesActive, "statProfilesActive"),
        Stat(StatMovies, "statMovies"),
        Stat(StatShows, "statShows"),
        Stat(StatAdvisoryCoverage, "statAdvisoryCoverage"),
        Stat(StatUsers, "statUsers"),
        Stat(StatStorage, "statStorage"),
        new(HealthStrip, Catalog + "healthStrip.title", Catalog + "healthStrip.description", 6, 12, 12, 1, 2, 1),
        new(ServerResources, Catalog + "serverResources.title", Catalog + "serverResources.description", 6, 12, 6, 2, 4, 2),
        new(Playback24h, Catalog + "playback24h.title", Catalog + "playback24h.description", 6, 12, 6, 2, 5, 3, Ranges: SampledRanges),
        new(ConcurrentStreams24h, Catalog + "concurrentStreams24h.title", Catalog + "concurrentStreams24h.description", 4, 12, 6, 2, 5, 3, Ranges: SampledRanges),
        new(Egress24h, Catalog + "egress24h.title", Catalog + "egress24h.description", 4, 12, 6, 2, 5, 3, Ranges: SampledRanges),
        new(PlaybackReliability, Catalog + "playbackReliability.title", Catalog + "playbackReliability.description", 4, 8, 6, 2, 4, 3, Ranges: SampledRanges),
        new(TopTitles, Catalog + "topTitles.title", Catalog + "topTitles.description", 4, 8, 6, 2, 5, 3, Ranges: LeaderboardRanges),
        new(TopProfiles, Catalog + "topProfiles.title", Catalog + "topProfiles.description", 4, 8, 6, 2, 5, 3, Ranges: LeaderboardRanges),
        new(WatchProviders, Catalog + "watchProviders.title", Catalog + "watchProviders.description", 6, 12, 12, 1, 3, 1),
        new(NowPlaying, Catalog + "nowPlaying.title", Catalog + "nowPlaying.description", 6, 12, 12, 2, 8, 3, CollapsedRows: 1),
        new(TranscodeNodes, Catalog + "transcodeNodes.title", Catalog + "transcodeNodes.description", 4, 12, 6, 2, 6, 3),
        new(Scanner, Catalog + "scanner.title", Catalog + "scanner.description", 4, 12, 4, 2, 6, 3),
        new(Libraries, Catalog + "libraries.title", Catalog + "libraries.description", 6, 12, 7, 2, 8, 4),
        new(Users, Catalog + "users.title", Catalog + "users.description", 4, 8, 5, 2, 8, 4),
        new(Downloads, Catalog + "downloads.title", Catalog + "downloads.description", 3, 8, 4, 2, 5, 3),
        new(ScanActivity, Catalog + "scanActivity.title", Catalog + "scanActivity.description", 6, 12, 8, 2, 6, 3),
        new(RecentErrors, Catalog + "recentErrors.title", Catalog + "recentErrors.description", 6, 12, 12, 2, 6, 3),
        new(RecentActivity, Catalog + "recentActivity.title", Catalog + "recentActivity.description", 6, 12, 12, 2, 8, 4),
        new(ApiClients, Catalog + "apiClients.title", Catalog + "apiClients.description", 6, 12, 6, 2, 6, 3, Ranges: SampledRanges),
    ];

    private static readonly Dictionary<string, DashboardWidgetDefinition> ById =
        Widgets.ToDictionary(widget => widget.Id, StringComparer.Ordinal);

    /// <summary>Renamed ids resolve to their replacement instead of being dropped as unknown.</summary>
    private static readonly IReadOnlyDictionary<string, string> LegacyIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["trakt-sync"] = WatchProviders,
    };

    public static DashboardWidgetDefinition? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var resolved = LegacyIds.TryGetValue(id, out var replacement) ? replacement : id;
        return ById.GetValueOrDefault(resolved);
    }

    public static DashboardWidgetDefinition Get(string id)
        => Find(id) is { } widget && widget.Id == id ? widget : throw new ArgumentException("Unknown dashboard widget: " + id, nameof(id));

    /// <summary>Live numbers, then the charts that explain them, then operational surfaces.</summary>
    public static readonly IReadOnlyList<string> DefaultLayoutOrder =
    [
        StatActiveStreams, StatEgressNow, StatProfilesActive, StatMovies, StatShows, StatStorage, HealthStrip,
        NowPlaying,
        Playback24h, Egress24h, TopTitles, TopProfiles,
        Libraries, Users, RecentActivity,
    ];

    /// <summary>Every default widget at its own default size and window.</summary>
    public static IReadOnlyList<DashboardLayoutEntry> DefaultLayout { get; } = DefaultLayoutOrder
        .Select(id => DefaultEntry(Get(id)))
        .ToArray();

    public static DashboardLayoutEntry DefaultEntry(DashboardWidgetDefinition widget)
        => new(widget.Id, widget.DefaultSpan, widget.DefaultRows, widget.Ranges?.Default);
}

/// <summary>How a range turns into request windows and words; every ranged widget reads it here.</summary>
public static class DashboardRanges
{
    public static readonly IReadOnlyList<DashboardWidgetRange> Order =
        [DashboardWidgetRange.Hour, DashboardWidgetRange.Day, DashboardWidgetRange.Week, DashboardWidgetRange.Month];

    /// <summary>Hours for timeseries/playback/api-client reads. A month is 720 hours, not the 744 API maximum.</summary>
    public static int Hours(DashboardWidgetRange range) => range switch
    {
        DashboardWidgetRange.Hour => 1,
        DashboardWidgetRange.Day => 24,
        DashboardWidgetRange.Week => 168,
        _ => 720,
    };

    /// <summary>Days for the top-activity endpoint; an hour collapses onto one day.</summary>
    public static int Days(DashboardWidgetRange range) => range switch
    {
        DashboardWidgetRange.Hour or DashboardWidgetRange.Day => 1,
        DashboardWidgetRange.Week => 7,
        _ => 30,
    };

    public static string WireName(DashboardWidgetRange range) => range switch
    {
        DashboardWidgetRange.Hour => "hour",
        DashboardWidgetRange.Day => "day",
        DashboardWidgetRange.Week => "week",
        _ => "month",
    };

    public static DashboardWidgetRange? Parse(string? value) => value switch
    {
        "hour" => DashboardWidgetRange.Hour,
        "day" => DashboardWidgetRange.Day,
        "week" => DashboardWidgetRange.Week,
        "month" => DashboardWidgetRange.Month,
        _ => null,
    };

    public static string Label(DashboardWidgetRange range) => DashboardText.T("web.admin.dashboard.range.label." + WireName(range));
    public static string Phrase(DashboardWidgetRange range) => DashboardText.T("web.admin.dashboard.range.phrase." + WireName(range));
    public static string ShowLabel(DashboardWidgetRange range) => DashboardText.T("web.admin.dashboard.rangePicker.show." + WireName(range));
    public static string Title(string title, DashboardWidgetRange range)
        => DashboardText.T("web.admin.dashboard.range.title." + WireName(range), ("title", title));
    public static (string Start, string End) EdgeLabels(DashboardWidgetRange range)
        => (DashboardText.T("web.admin.dashboard.range.edgeStart." + WireName(range)), DashboardText.T("web.admin.dashboard.range.edgeEnd"));
}
