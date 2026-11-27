using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// English text of the Insights area (official Silo c59798116, <c>web/src/locales/en.json</c>): every key the range added in this area is
/// carried by the catalog its domain already uses, renders its official sentence, and no existing string was changed here.
/// </summary>
public sealed class AdminInsightsTextTests
{
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope() => CultureInfo.CurrentCulture = new CultureInfo("en-US");
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    [Fact]
    public void EveryNewKeyIsCarriedByTheCatalogOfItsDomain()
    {
        Assert.Equal(378, AdminInsightsTextKeys.AdminText.Length + AdminInsightsTextKeys.Dashboard.Length + AdminInsightsTextKeys.History.Length + AdminInsightsTextKeys.Insights.Length);
        Assert.All(AdminInsightsTextKeys.AdminText, key => Assert.True(AdminText.Has(key), key));
        Assert.All(AdminInsightsTextKeys.Dashboard, key => Assert.True(DashboardText.Has(key), key));
        Assert.All(AdminInsightsTextKeys.History, key => Assert.True(AdminPlaybackAuditText.Has(key), key));
        Assert.All(AdminInsightsTextKeys.Insights, key => Assert.True(AdminInsightsText.Has(key), key));
        // The Insights catalog holds exactly its own keys, so nothing is carried twice.
        Assert.Equal(AdminInsightsTextKeys.Insights.Order(StringComparer.Ordinal), AdminInsightsText.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(276, AdminInsightsText.Keys.Count);
        Assert.Empty(AdminInsightsTextKeys.Insights.Where(AdminText.Has));
        Assert.Empty(AdminInsightsTextKeys.Insights.Where(DashboardText.Has));
        Assert.Empty(AdminInsightsTextKeys.Insights.Where(AdminPlaybackAuditText.Has));
        Assert.Empty(AdminInsightsTextKeys.History.Where(AdminInsightsText.Has));
    }

    [Fact]
    public void EveryPatternFormatsWithoutErrorsWhateverItsArguments()
    {
        using var _ = new CultureScope();
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in new[] { "count", "count_text", "total_count", "titles_count", "transcode_count", "movie_count", "episode_count", "value", "title", "year", "tz", "date", "since",
            "from", "to", "day", "hour", "plays", "watch_time", "id", "name", "account", "chart", "recorded", "total", "measured", "percent", "client", "codec", "sessions", "transcodes",
            "direct_rate", "position", "duration", "episode", "movies", "episodes", "played", "never", "days", "hw", "sw", "api", "nodes", "remote", "local", "playback", "downloads",
            "direct", "remux", "transcode", "forced", "hearing_impaired", "subtitled", "library", "label", "tz" })
            arguments[name] = name.EndsWith("count", StringComparison.Ordinal) ? 2 : name;
        foreach (var key in AdminInsightsText.Keys) Assert.False(string.IsNullOrWhiteSpace(AdminText.Format(AdminInsightsText.EnglishPattern(key)!, arguments)), key);
        foreach (var key in AdminInsightsTextKeys.History) Assert.False(string.IsNullOrWhiteSpace(AdminText.Format(AdminPlaybackAuditText.EnglishPattern(key)!, arguments)), key);
        foreach (var key in AdminInsightsTextKeys.Dashboard) Assert.False(string.IsNullOrWhiteSpace(DashboardText.T(key)), key);
        foreach (var key in AdminInsightsTextKeys.AdminText) Assert.False(string.IsNullOrWhiteSpace(AdminText.T(key)), key);
    }

    [Fact]
    public void PluralAndNestedMessagesRenderTheirOfficialSentences()
    {
        using var _ = new CultureScope();
        string T(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);
        Assert.Equal("1 movie · 2 episodes", T("web.admin.insights.activity.kpi.playsBreakdown", ("movie_count", 1), ("movies", "1"), ("episode_count", 2), ("episodes", "2")));
        Assert.Equal("2 movies · 1 episode", T("web.admin.insights.activity.kpi.playsBreakdown", ("movie_count", 2), ("movies", "2"), ("episode_count", 1), ("episodes", "1")));
        Assert.Equal("Exact for 1 of 2 sessions", T("web.admin.insights.activity.kpi.watchTimeMeasured", ("total_count", 2), ("measured", "1"), ("total", "2")));
        Assert.Equal("Exact for 1 of 1 session", T("web.admin.insights.activity.kpi.watchTimeMeasured", ("total_count", 1), ("measured", "1"), ("total", "1")));
        Assert.Equal("Chrome · HEVC: 40% direct (5 sessions; 1 transcode)", T("web.admin.insights.devices.matrix.cellTooltip", ("transcode_count", 1), ("client", "Chrome"),
            ("codec", "HEVC"), ("direct_rate", "40"), ("sessions", "5 sessions"), ("transcodes", "1")));
        Assert.Equal("Chrome · HEVC: 40% direct (5 sessions; 2 transcodes)", T("web.admin.insights.devices.matrix.cellTooltip", ("transcode_count", 2), ("client", "Chrome"),
            ("codec", "HEVC"), ("direct_rate", "40"), ("sessions", "5 sessions"), ("transcodes", "2")));
        Assert.Equal("across 1 active day", T("web.admin.insights.bandwidth.kpi.dailyAverageSub", ("count", 1)));
        Assert.Equal("across 7 active days", T("web.admin.insights.bandwidth.kpi.dailyAverageSub", ("count", 7)));
        Assert.Equal("1 profile", T("web.admin.insights.people.profilesCount", ("count", 1)));
        Assert.Equal("3 profiles", T("web.admin.insights.people.profilesCount", ("count", 3)));
        Assert.Equal("1 of 1 title · 0 never started", T("web.admin.insights.library.coverage.detail", ("titles_count", 1), ("played", "1"), ("titles", "1"), ("never", "0")));
        Assert.Equal("4 of 12 titles · 2 never started", T("web.admin.insights.library.coverage.detail", ("titles_count", 12), ("played", "4"), ("titles", "12"), ("never", "2")));
        Assert.Equal("Detailed playback info is available for 4 of 10 sessions (tracked since Oct 1, 2026).",
            T("web.admin.insights.coverage.routeFactsPartial", ("total_count", 10), ("recorded", "4"), ("total", "10"), ("since", "Oct 1, 2026")));
        Assert.Equal("Exact watch time is available for 9 of 10 sessions; older sessions are estimated.",
            T("web.admin.insights.coverage.watchTimePartial", ("total_count", 10), ("measured", "9"), ("total", "10")));
        Assert.Equal("33% of sessions showed subtitles (3 of 9 sessions with detailed playback info)",
            T("web.admin.insights.devices.subtitles.summary", ("total_count", 9), ("percent", "33"), ("subtitled", "3"), ("total", "9")));
        Assert.Equal("1 play", T("web.admin.insights.playCount", ("count", 1), ("count_text", "1")));
        Assert.Equal("40% unfinished", T("web.admin.insights.titles.leastFinished.unfinishedShare", ("value", "40")));
        Assert.Equal("America/New_York (local)", T("web.admin.insights.filter.timezoneBrowser", ("tz", "America/New_York")));
        Assert.Equal("Mon at 9 AM: 12 plays (3h)", T("web.admin.insights.activity.heatmap.cellTooltip", ("day", "Mon"), ("hour", "9 AM"), ("plays", "12 plays"), ("watch_time", "3h")));
        Assert.Equal("Reached 1:20:00 of 2:10:00", T("web.admin.insights.titleDetail.viewers.furthest", ("position", "1:20:00"), ("duration", "2:10:00")));
        Assert.Equal("Last: S02E04 Pilot", T("web.admin.insights.titleDetail.viewers.lastEpisode", ("episode", "S02E04 Pilot")));
        Assert.Equal("1990s", T("web.admin.insights.library.decades.label", ("year", "1990")));
        Assert.Equal("12 d", T("web.admin.insights.library.kpi.firstPlayDays", ("days", "12")));
        Assert.Equal("Oct 1, 2026 – Oct 31, 2026", T("web.admin.insights.detail.window", ("from", "Oct 1, 2026"), ("to", "Oct 31, 2026")));
        Assert.Equal("Profile ab12cd34", T("web.admin.insights.people.unnamedProfile", ("id", "ab12cd34")));
    }

    [Fact]
    public void HistoryDashboardAndNavigationKeysRenderTheirOfficialText()
    {
        using var _ = new CultureScope();
        Assert.Equal("Showing 1 row", AdminPlaybackAuditText.T("web.admin.history.pagination.summary", ("count", 1), ("count_text", "1")));
        Assert.Equal("Showing 25 rows", AdminPlaybackAuditText.T("web.admin.history.pagination.summary", ("count", 25), ("count_text", "25")));
        Assert.Equal("Date: Nov 1 – Nov 22", AdminPlaybackAuditText.T("web.admin.history.filter.activeChip.dateRange", ("from", "Nov 1"), ("to", "Nov 22")));
        Assert.Equal("Remove Client: Chrome filter", AdminPlaybackAuditText.T("web.admin.history.filter.removeChip", ("label", "Client: Chrome")));
        Assert.Equal("Stopped by admin", AdminPlaybackAuditText.T("web.admin.history.endReason.terminated"));
        Assert.Equal("Why it didn’t direct play", AdminPlaybackAuditText.T("web.admin.history.detail.section.reasons"));
        Assert.Equal("Search titles, accounts, clients, or session IDs…", AdminPlaybackAuditText.T("web.admin.history.filter.searchPlaceholder"));
        Assert.Equal("Top · last 90 d", DashboardText.T("web.admin.dashboard.range.title.quarter", ("title", "Top")));
        Assert.Equal("Top · last 365 d", DashboardText.T("web.admin.dashboard.range.title.year", ("title", "Top")));
        Assert.Equal("Top · all time", DashboardText.T("web.admin.dashboard.range.title.all", ("title", "Top")));
        Assert.Equal(["90d", "1y", "All"], new[] { "quarter", "year", "all" }.Select(range => DashboardText.T("web.admin.dashboard.range.label." + range)));
        Assert.Equal(["the last 90 days", "the last 365 days", "all recorded history"], new[] { "quarter", "year", "all" }.Select(range => DashboardText.T("web.admin.dashboard.range.phrase." + range)));
        Assert.Equal(["90d ago", "1y ago", "First record"], new[] { "quarter", "year", "all" }.Select(range => DashboardText.T("web.admin.dashboard.range.edgeStart." + range)));
        Assert.Equal("Show all recorded history", DashboardText.T("web.admin.dashboard.rangePicker.show.all"));
        Assert.Equal("Insights ›", DashboardText.T("web.admin.dashboard.widgets.openInsights"));
        Assert.Equal("Failed to load Insights.", DashboardText.T("web.admin.dashboard.widgets.insights.loadFailed"));
        Assert.Equal("Playback over time", DashboardText.T("web.admin.dashboard.widgetCatalog.insightsPlays.title"));
        Assert.Equal("Insights", AdminText.T("web.admin.shell.nav.page.insights.label"));
        Assert.Equal("Live streams, watch trends, top titles, accounts, devices, and bandwidth.", AdminText.T("web.admin.shell.nav.page.insights.description"));
        Assert.Equal("Insights", AdminText.T("web.admin.common.title.header.insights"));
        Assert.Equal("Admin Insights", AdminText.T("web.admin.common.title.tab.insights"));
        Assert.Equal(["Layout", "Table", "Cards"], new[] { "label", "table", "cards" }.Select(key => AdminText.T("web.admin.activity.view." + key)));
    }

    [Fact]
    public void PlaybackHistoryUsesTheElevenUpdatedOfficialStrings()
    {
        var updated = new Dictionary<string, string>
        {
            ["web.admin.history.column.logs"] = "Actions",
            ["web.admin.history.column.user"] = "Account",
            ["web.admin.history.column.watchTime"] = "Watch time",
            ["web.admin.history.empty.body"] = "No sessions match these filters. Try clearing or adjusting a filter.",
            ["web.admin.history.filter.allAttempts"] = "All statuses",
            ["web.admin.history.filter.allUsers"] = "All accounts",
            ["web.admin.history.filter.chooseUserFirst"] = "Choose an account first",
            ["web.admin.history.itemFilterActive"] = "Title:",
            ["web.admin.history.row.estimatedTitle"] = "Estimated (recorded before exact watch time was tracked)",
            ["web.admin.history.stats.visibleRows"] = "Shown",
            ["web.admin.history.subtitle"] = "Past playback sessions across all accounts and profiles.",
        };
        Assert.Equal(11, updated.Count);
        foreach (var (key, english) in updated) Assert.Equal(english, AdminPlaybackAuditText.EnglishPattern(key));
    }

    [Fact]
    public void KeysTheRulesAndModelsNameResolve()
    {
        Assert.All(AdminInsightsTabs.Ordered, tab => Assert.True(AdminText.Has(tab.LabelKey()) || AdminInsightsText.Has(tab.LabelKey()) || AdminPlaybackAuditText.Has(tab.LabelKey()), tab.LabelKey()));
        Assert.All(AdminInsightsRanges.Ordered, range => Assert.True(AdminInsightsText.Has(range.LabelKey()), range.LabelKey()));
        Assert.All(new[] { AdminInsightsTitleSort.Plays, AdminInsightsTitleSort.WatchTime, AdminInsightsTitleSort.Viewers },
            sort => Assert.True(AdminInsightsText.Has(sort.LabelKey()), sort.LabelKey()));
        Assert.Equal(["7d", "30d", "90d", "1y", "All time", "Custom"], AdminInsightsRanges.Ordered.Select(range => AdminInsightsText.T(range.LabelKey())));
        Assert.Equal(["Activity", "Playback", "Titles", "Library", "People", "Devices & Quality", "Bandwidth"],
            AdminInsightsTabs.Ordered.Select(tab => tab == AdminInsightsTab.Activity ? AdminText.T(tab.LabelKey()) : AdminInsightsText.T(tab.LabelKey())));
    }
}
