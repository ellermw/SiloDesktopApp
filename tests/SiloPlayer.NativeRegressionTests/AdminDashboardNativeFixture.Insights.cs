using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Synthetic Insights reads for the dashboard (official c59798116 schema): a window sized by the request's <c>range</c>, a weekday x hour
/// heatmap busiest on weekend evenings, a removed title among the top titles, and libraries at several played shares. No real data.
/// </summary>
internal static class InsightsFixture
{
    private static readonly string[] Movies = ["Synthetic Feature", "Arrival of the Synthetic Fleet", "Heat Haze", "Moonlit Harbor", "Paper Lanterns"];
    private static readonly string[] Shows = ["Synthetic Series", "Severance of Ties", "Andor Station", "The Long Quiet"];

    private static (int Days, string Unit, string Range) Window(string query)
    {
        var range = query.Contains("range=all", StringComparison.Ordinal) ? "all"
            : query.Contains("range=1y", StringComparison.Ordinal) ? "1y"
            : query.Contains("range=90d", StringComparison.Ordinal) ? "90d"
            : query.Contains("range=7d", StringComparison.Ordinal) ? "7d" : "30d";
        return range switch { "7d" => (7, "day", range), "30d" => (30, "day", range), "90d" => (90, "week", range), "1y" => (364, "week", range), _ => (760, "month", range) };
    }

    private static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static JsonObject Coverage(DateTimeOffset now) => new()
    {
        ["measured_sessions"] = 400, ["oldest_history_at"] = Iso(now.AddDays(-700)), ["oldest_measured_at"] = Iso(now.AddDays(-600)),
        ["oldest_metric_at"] = Iso(now.AddDays(-600)), ["oldest_route_facts_at"] = Iso(now.AddDays(-500)), ["route_facts_sessions"] = 380, ["total_sessions"] = 420,
    };

    public static string Activity(DateTimeOffset now, string query = "")
    {
        var (days, unit, _) = Window(query);
        var end = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var start = end.AddDays(-days);
        // Buckets are labelled by their calendar start: a Monday for a week, the first for a month, as the server's date_trunc does.
        var first = unit == "week" ? start.AddDays(-(((int)start.DayOfWeek + 6) % 7))
            : unit == "month" ? new DateTimeOffset(start.Year, start.Month, 1, 0, 0, 0, TimeSpan.Zero) : start;
        var buckets = new JsonArray();
        var index = 0;
        for (var cursor = first; cursor < end; cursor = unit == "day" ? cursor.AddDays(1) : unit == "week" ? cursor.AddDays(7) : cursor.AddMonths(1), index++)
        {
            var wave = 6 + 5 * Math.Sin(index / 2.3) + (index % 7 == 5 ? 5 : 0);
            // A quiet stretch, so a chart shows its zero-height columns.
            var plays = index % 11 == 4 ? 0 : (int)Math.Round(wave) * (unit == "day" ? 1 : unit == "week" ? 6 : 24);
            var direct = (int)Math.Round(plays * 0.6);
            var remux = (int)Math.Round(plays * 0.25);
            var movies = (int)Math.Round(plays * 0.35);
            var episodes = (int)Math.Round(plays * 0.55);
            buckets.Add(new JsonObject
            {
                ["active_accounts"] = plays == 0 ? 0 : 2 + index % 3, ["active_profiles"] = plays == 0 ? 0 : 3 + index % 5,
                ["bucket_start"] = Iso(cursor), ["direct"] = direct, ["episodes"] = episodes, ["label_date"] = cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["movies"] = movies, ["other_media"] = plays - movies - episodes, ["plays"] = plays, ["remux"] = remux, ["transcode"] = plays - direct - remux,
                ["watch_seconds"] = plays * 3100,
            });
        }
        var heatmap = new JsonArray();
        for (var day = 0; day < 7; day++)
            for (var hour = 0; hour < 24; hour++)
            {
                var evening = Math.Exp(-Math.Pow((hour - 20.5) / 2.6, 2));
                var morning = 0.18 * Math.Exp(-Math.Pow((hour - 9) / 2.0, 2));
                var weekend = day >= 5 ? 1.6 : 1.0;
                var plays = (int)Math.Round(26 * (evening + morning) * weekend);
                if (plays == 0 && (day + hour) % 5 != 0) continue;
                heatmap.Add(new JsonObject { ["day_of_week"] = day, ["hour"] = hour, ["plays"] = plays, ["watch_seconds"] = plays * 3300 });
            }
        return new JsonObject
        {
            ["bucket_unit"] = unit, ["buckets"] = buckets, ["coverage"] = Coverage(now), ["end_reasons"] = new JsonArray(),
            ["from"] = Iso(start), ["heatmap"] = heatmap, ["peak_streams"] = new JsonArray(), ["to"] = Iso(end), ["tz"] = "UTC",
            ["summary"] = new JsonObject
            {
                ["completed_plays"] = 300, ["completion_rate"] = 0.71, ["direct_plays"] = 250, ["episodes"] = 240, ["measured_watch_seconds"] = 900000, ["movies"] = 140,
                ["other_media"] = 40, ["peak_concurrent_at"] = Iso(now.AddDays(-3)), ["peak_concurrent_streams"] = 6, ["plays"] = 420, ["remux_plays"] = 90,
                ["sessions"] = 440, ["transcode_plays"] = 80, ["unique_accounts"] = 5, ["unique_profiles"] = 9, ["watch_seconds"] = 1_100_000,
            },
        }.ToJsonString();
    }

    private static JsonObject TitleRow(string id, string title, string kind, long plays, long watch, long year)
        => new()
        {
            ["completed_plays"] = plays / 2, ["content_id"] = id, ["episodes"] = kind == "series" ? 12 : 0, ["kind"] = kind, ["last_played_at"] = Iso(DateTimeOffset.Parse("2026-11-13T21:00:00Z")),
            ["plays"] = plays, ["title"] = title, ["viewers"] = 3, ["watch_seconds"] = watch, ["year"] = year,
        };

    public static string Titles(DateTimeOffset now, string query = "")
    {
        var (days, _, _) = Window(query);
        var movies = new JsonArray();
        for (var i = 0; i < Movies.Length; i++) movies.Add(TitleRow("m" + i, i == 3 ? "" : Movies[i], "movie", 40 - i * 7, (40 - i * 7) * 6800, 1990 + i * 6));
        var shows = new JsonArray();
        for (var i = 0; i < Shows.Length; i++) shows.Add(TitleRow("s" + i, Shows[i], "series", 52 - i * 9, (52 - i * 9) * 2900, 2008 + i * 4));
        return new JsonObject
        {
            ["from"] = Iso(now.AddDays(-days)), ["least_finished"] = new JsonArray(), ["libraries"] = new JsonArray(), ["movies"] = movies, ["shows"] = shows,
            ["sort"] = "plays", ["to"] = Iso(now), ["tz"] = "UTC",
        }.ToJsonString();
    }

    public static string Library(DateTimeOffset now, string query = "")
    {
        var (days, _, _) = Window(query);
        JsonObject Row(string id, string name, long titles, long played, long never, double share) => new()
        {
            ["added_titles"] = 12, ["library_id"] = id, ["name"] = name, ["never_started"] = never, ["played_share"] = share, ["played_titles"] = played, ["titles"] = titles,
        };
        return new JsonObject
        {
            ["content_ratings"] = new JsonArray(), ["decades"] = new JsonArray(), ["from"] = Iso(now.AddDays(-days)), ["genres"] = new JsonArray(),
            ["libraries"] = new JsonArray(Row("1", "Movies", 1240, 806, 310, 0.65), Row("2", "TV Shows", 318, 248, 20, 0.7799), Row("3", "Documentaries and Concerts With A Long Name", 96, 6, 80, 0.0625),
                Row("4", "Anime", 12, 12, 0, 1)),
            ["oldest_history_at"] = Iso(now.AddDays(-700)), ["recently_added_unplayed"] = new JsonArray(), ["time_to_first_play"] = new JsonArray(), ["to"] = Iso(now), ["tz"] = "UTC",
            ["summary"] = new JsonObject
            {
                ["added_played"] = 20, ["added_titles"] = 40, ["median_days_to_first_play"] = 3.5, ["never_started"] = 410, ["played_share"] = 0.66, ["played_titles"] = 1072, ["titles"] = 1666,
            },
        }.ToJsonString();
    }

    public static string People(DateTimeOffset now, string query = "")
    {
        var (days, _, _) = Window(query);
        JsonObject Profile(string user, string username, string id, string name, long plays, long watch) => new()
        {
            ["plays"] = plays, ["profile_id"] = id, ["profile_name"] = name, ["user_id"] = user, ["username"] = username, ["watch_seconds"] = watch,
        };
        return new JsonObject
        {
            ["accounts"] = new JsonArray(), ["active_accounts"] = 5, ["active_profiles"] = 9, ["coverage"] = Coverage(now), ["from"] = Iso(now.AddDays(-days)), ["live_accounts"] = 1,
            ["to"] = Iso(now), ["top_profiles"] = new JsonArray(
                Profile("7", "viewer", "p1", "Kids", 61, 190000), Profile("8", "guest", "", "", 44, 120000), Profile("7", "viewer", "p2", "Movie Night With A Long Profile Name", 31, 98000),
                Profile("9", "", "p3", "Dad", 12, 21000)),
            ["total_accounts"] = 6, ["total_watch_seconds"] = 900000, ["tz"] = "UTC",
        }.ToJsonString();
    }
}

internal static partial class AdminDashboardNativeFixture
{
    private const string InsightsLayout = """
        {"version":1,"entries":[
        {"id":"insights-plays","span":12,"rows":4,"range":"month","view":"method"},
        {"id":"insights-viewing-hours","span":6,"rows":3,"range":"month"},
        {"id":"insights-weekday","span":6,"rows":3,"range":"month","view":"plays"},
        {"id":"insights-hour-of-day","span":6,"rows":3,"range":"quarter","view":"plays"},
        {"id":"insights-active-people","span":6,"rows":3,"range":"month"},
        {"id":"insights-library-coverage","span":6,"rows":3,"range":"year"},
        {"id":"top-titles","span":6,"rows":3,"range":"week"},
        {"id":"top-profiles","span":6,"rows":3,"range":"all"}
        ]}
        """;

    private static Button? ToggleOption(FrameworkElement widget, string accessibleName)
        => Descendants(widget).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == accessibleName);

    private static bool HasText(FrameworkElement scope, string text) => Descendants(scope).OfType<TextBlock>().Any(block => block.Text == text);

    /// <summary>Header-gated "Insights ›" links under a widget: shown, hidden by a narrow header, or absent when unavailable.</summary>
    private static (int Present, int Visible) InsightsLinks(FrameworkElement widget)
    {
        var gates = Descendants(widget).Where(element => element.GetType().Name == "DashboardHeaderGate").ToArray();
        return (gates.Length, gates.Count(gate => gate.Visibility == Visibility.Visible && gate.ActualWidth > 0));
    }

    private static string? StoredEntryField(string? document, string id, string field)
    {
        if (document == null) return null;
        using var json = JsonDocument.Parse(document);
        foreach (var entry in json.RootElement.GetProperty("entries").EnumerateArray())
            if (entry.GetProperty("id").GetString() == id)
                return entry.TryGetProperty(field, out var value) ? value.GetString() : null;
        return null;
    }

    private static async Task InsightsAsync(Window window, Server server, DashboardMemoryLayoutCache cache)
    {
        server.PersistLayout = true;
        server.LayoutETag = "\"I1\"";
        server.StoredLayout = InsightsLayout;
        server.SessionRows = [];
        server.Reads.Clear();
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var (frame, page) = OpenDashboard(window);
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            void ResizeLogical(int width, int height) => window.AppWindow.ResizeClient(
                new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            await Until(() => page.Store?.Get(DashboardDataStore.InsightsPeople(DashboardWidgetRange.All)).IsSuccess == true
                && page.Store.Get(DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year)).IsSuccess);
            await Task.Delay(800);
            string[] ids = ["insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people", "insights-library-coverage", "top-titles", "top-profiles"];
            Check(page.PlacedWidgets().Select(w => w.Id).SequenceEqual(ids), "insights: the stored WebUI layout places the six Insights widgets and both leaderboards");
            Check(page.WidgetView("insights-plays") == "method" && page.WidgetView("insights-weekday") == "plays", "insights: stored views reach the widgets");

            // ---- One shared Activity read per window; the leaderboards read Insights, not stats/top-activity ----
            string[] reads;
            lock (server.Reads) reads = server.Reads.ToArray();
            Check(reads.Count(path => path.StartsWith("/api/v2/admin/insights/activity?range=30d", StringComparison.Ordinal)) == 1,
                "insights: four widgets on the 30-day window make one Activity read");
            Check(reads.Count(path => path.StartsWith("/api/v2/admin/insights/activity?range=90d", StringComparison.Ordinal)) == 1, "insights: the quarter window reads once");
            Check(reads.Any(path => path.StartsWith("/api/v2/admin/insights/titles?range=7d&tz=", StringComparison.Ordinal) && path.Contains("sort=plays&limit=8", StringComparison.Ordinal)),
                "insights: Top titles reads Insights titles, sorted by plays, eight rows");
            Check(reads.Any(path => path.StartsWith("/api/v2/admin/insights/people?range=all", StringComparison.Ordinal) && path.Contains("limit=1", StringComparison.Ordinal)),
                "insights: Top profiles reads Insights people with one account row");
            Check(reads.Any(path => path.StartsWith("/api/v2/admin/insights/library?range=1y", StringComparison.Ordinal)), "insights: Library coverage reads the year window");
            Check(!reads.Any(path => path.Contains("/stats/top-activity", StringComparison.Ordinal)), "insights: the dashboard does not read stats/top-activity when Insights works");
            Check(!reads.Any(path => path.Contains("refresh=true", StringComparison.Ordinal)), "insights: no read asks for fresh data before Refresh is pressed");

            // ---- Titles carry the window; ranges are the five Insights presets ----
            string[] weekToAll = ["7d", "30d", "90d", "1y", "All"];
            foreach (var id in ids)
            {
                var widget = WidgetFrame(page, id);
                var labels = Descendants(widget).OfType<Button>().Select(button => AutomationProperties.GetName(button)).ToArray();
                var shown = new[] { "Show the last 7 days", "Show the last 30 days", "Show the last 90 days", "Show the last 365 days", "Show all recorded history" };
                Check(shown.All(name => labels.Contains(name)), $"insights: {id} offers week, 30 days, 90 days, year and all time");
                Check(!labels.Contains("Show the last 24 hours") && !labels.Contains("Show the last hour"), $"insights: {id} offers no hour or day window");
            }
            Check(HasText(WidgetFrame(page, "insights-hour-of-day"), DashboardRanges.Title("Plays by hour of day", DashboardWidgetRange.Quarter)), "insights: a quarter card is titled with its window");
            Check(HasText(WidgetFrame(page, "top-profiles"), DashboardRanges.Title("Top profiles", DashboardWidgetRange.All)) || Descendants(WidgetFrame(page, "top-profiles")).OfType<TextBlock>().Any(t => t.Text.Contains("all time")),
                "insights: an all-time leaderboard is titled all time");

            // ---- The Insights link is registered with its real destination ----
            foreach (var id in ids)
                Check(InsightsLinks(WidgetFrame(page, id)).Present == 1, $"insights: {id} renders an Insights link now that the page can open");
            Check(HasText(WidgetFrame(page, "top-titles"), "Insights ›") == (InsightsLinks(WidgetFrame(page, "top-titles")).Visible == 1),
                "insights: the top titles link text follows header visibility");

            await CaptureAsync(frame, "insights-dashboard-1440x900.png");
            await CaptureWidgetsAsync(page, ids, 1440);

            // ---- View toggles ----
            var plays = WidgetFrame(page, "insights-plays");
            string[] playViews = ["Media type", "Watch time", "Play method"];
            string[] expectedSeries = ["Movies", "Watch time", "Direct play"];
            for (var i = 0; i < playViews.Length; i++)
            {
                var before = page.WidgetRenderCount("insights-plays");
                if (ToggleOption(plays, playViews[i]) is { } option) Invoke(option); else Check(false, $"insights: Playback over time offers {playViews[i]}");
                await Task.Delay(300);
                var expectedView = i == 0 ? "media" : i == 1 ? "watch_time" : "method";
                Check(page.WidgetView("insights-plays") == expectedView, $"insights: picking {playViews[i]} stores the {expectedView} view");
                plays = WidgetFrame(page, "insights-plays");
                Check(page.WidgetRenderCount("insights-plays") > before, $"insights: {playViews[i]} draws");
                Check(HasText(plays, expectedSeries[i]) || i == 1, $"insights: the {playViews[i]} view's legend or series is on screen");
                await CaptureAsync(plays, $"insights-plays-{expectedView}-1440.png");
            }
            foreach (var id in new[] { "insights-weekday", "insights-hour-of-day" })
            {
                var widget = WidgetFrame(page, id);
                if (ToggleOption(widget, "Watch time") is { } watch) Invoke(watch); else Check(false, $"insights: {id} offers Watch time");
                await Task.Delay(300);
                Check(page.WidgetView(id) == "watch_time", $"insights: {id} switches to watch time");
                await CaptureAsync(WidgetFrame(page, id), $"{id}-watch-time-1440.png");
                if (ToggleOption(WidgetFrame(page, id), "Plays") is { } back) Invoke(back);
                await Task.Delay(300);
                Check(page.WidgetView(id) == "plays", $"insights: {id} switches back to plays");
            }
            await Task.Delay(1400);
            Check(StoredEntryField(LastLayoutBody(server), "insights-plays", "view") == "method", "insights: the saved layout carries the Playback over time view");
            Check(StoredEntryField(LastLayoutBody(server), "insights-plays", "range") == "month" && StoredEntryField(LastLayoutBody(server), "top-profiles", "range") == "all",
                "insights: the saved layout keeps every Insights window as stored");

            // ---- Windows: a longer window reads once, shares across widgets, and the card follows ----
            var activityReads = server.Reads.Count(path => path.StartsWith("/api/v2/admin/insights/activity", StringComparison.Ordinal));
            page.LayoutSync!.SetWidgetRange("insights-weekday", DashboardWidgetRange.Quarter);
            await Task.Delay(500);
            Check(server.Reads.Count(path => path.StartsWith("/api/v2/admin/insights/activity", StringComparison.Ordinal)) == activityReads,
                "insights: moving a widget to a window another widget already reads makes no request");
            page.LayoutSync.SetWidgetRange("insights-plays", DashboardWidgetRange.All);
            page.LayoutSync.SetWidgetRange("insights-weekday", DashboardWidgetRange.Year);
            page.LayoutSync.SetWidgetRange("top-titles", DashboardWidgetRange.Quarter);
            await Until(() => page.Store?.Get(DashboardDataStore.InsightsActivity(DashboardWidgetRange.All)).IsSuccess == true
                && page.Store.Get(DashboardDataStore.InsightsTitles(DashboardWidgetRange.Quarter)).IsSuccess);
            await Task.Delay(500);
            Check(server.Reads.Any(path => path.StartsWith("/api/v2/admin/insights/activity?range=all", StringComparison.Ordinal)), "insights: the all-time window is read with range=all");
            Check(server.Reads.Any(path => path.StartsWith("/api/v2/admin/insights/titles?range=90d", StringComparison.Ordinal)), "insights: a quarter leaderboard reads range=90d");
            Check(HasText(WidgetFrame(page, "insights-plays"), DashboardRanges.Title("Playback over time", DashboardWidgetRange.All)), "insights: the card is retitled for all time");
            var allTimeChart = Descendants(WidgetFrame(page, "insights-plays")).First(element => element.GetType().Name == "DashboardStackedColumnChart");
            var allTimeBars = (Grid)allTimeChart.GetType().GetField("_bars", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(allTimeChart)!;
            Check(Descendants(allTimeBars).OfType<Border>().Count(border => border.Height > 4 && double.IsFinite(border.Height)) > 10, "insights: the all-time window draws its monthly columns");
            await CaptureAsync(WidgetFrame(page, "insights-plays"), "insights-plays-all-time-1440.png");
            await CaptureAsync(WidgetFrame(page, "top-titles"), "insights-top-titles-quarter-1440.png");

            // ---- Refresh: one click asks for fresh data, once ----
            page.LayoutSync.SetWidgetRange("insights-plays", DashboardWidgetRange.Month);
            page.LayoutSync.SetWidgetRange("insights-weekday", DashboardWidgetRange.Month);
            page.LayoutSync.SetWidgetRange("top-titles", DashboardWidgetRange.Week);
            await Task.Delay(700);
            server.Reads.Clear();
            await page.Store!.RefreshDashboardAsync(manual: true);
            await Task.Delay(300);
            Check(!server.Reads.Any(path => path.Contains("/insights/", StringComparison.Ordinal)), "insights: the page-return refresh and the 60 s loop never read Insights");
            await page.Store.RefreshDashboardAsync(manual: true, freshInsights: true);
            await Until(() => server.Reads.Any(path => path.Contains("/insights/activity", StringComparison.Ordinal) && path.Contains("refresh=true", StringComparison.Ordinal)));
            Check(server.Reads.Any(path => path.Contains("/insights/activity?range=30d", StringComparison.Ordinal) && path.Contains("refresh=true", StringComparison.Ordinal)),
                "insights: Refresh re-reads the Activity window with refresh=true");
            Check(server.Reads.Any(path => path.Contains("/insights/people", StringComparison.Ordinal) && path.Contains("refresh=true", StringComparison.Ordinal)),
                "insights: Refresh re-reads Top profiles with refresh=true");

            // ---- No rebuild on equal data; scroll positions kept ----
            var rendersBefore = ids.ToDictionary(id => id, id => page.WidgetRenderCount(id));
            await page.Store.FetchAsync(DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month), force: true);
            await page.Store.FetchAsync(DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year), force: true);
            await Task.Delay(400);
            Check(ids.All(id => page.WidgetRenderCount(id) == rendersBefore[id]), "insights: an equal poll does not rebuild any widget");
            var coverage = WidgetFrame(page, "insights-library-coverage");
            var scroller = Descendants(coverage).OfType<ScrollViewer>().First();
            page.LayoutSync.ResizeWidget("insights-library-coverage", null, 2);
            await Task.Delay(500);
            scroller = Descendants(WidgetFrame(page, "insights-library-coverage")).OfType<ScrollViewer>().First();
            scroller.ChangeView(null, 30, null, disableAnimation: true);
            await Task.Delay(200);
            var offset = scroller.VerticalOffset;
            await page.Store.FetchAsync(DashboardDataStore.InsightsLibrary(DashboardWidgetRange.Year), force: true);
            page.LayoutSync.SetWidgetRange("insights-library-coverage", DashboardWidgetRange.Year);
            await Task.Delay(400);
            Check(scroller.ScrollableHeight <= 0 || Math.Abs(Descendants(WidgetFrame(page, "insights-library-coverage")).OfType<ScrollViewer>().First().VerticalOffset - offset) < 1,
                "insights: a poll leaves the list's scroll position alone");
            page.LayoutSync.ResizeWidget("insights-library-coverage", null, 3);
            await Task.Delay(400);

            // ---- Narrow ----
            foreach (var width in new[] { 800, 560 })
            {
                ResizeLogical(width, 900);
                page.ViewportWidthOverride = width;
                await Task.Delay(700);
                await CaptureAsync(frame, $"insights-dashboard-{width}x900.png");
                await CaptureWidgetsAsync(page, ids, width);
            }
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Task.Delay(500);

            // ---- A warm revisit shows every Insights widget's data at once ----
            window.Content = null;
            await Task.Delay(400);
            server.Reads.Clear();
            (frame, page) = OpenDashboard(window);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.Store != null);
            Check(page.Store?.IsWarmVisit == true, "insights: the revisit is a warm visit");
            await Until(() => page.LayoutSync?.IsLoaded == true);
            await Task.Delay(500);
            Check(page.Store?.Get(DashboardDataStore.InsightsActivity(DashboardWidgetRange.Month)).IsSuccess == true
                && page.Store.Get(DashboardDataStore.InsightsPeople(DashboardWidgetRange.All)).IsSuccess, "insights: the warm visit holds the retained Insights data");
            Check(!server.Reads.Any(path => path.Contains("/insights/", StringComparison.Ordinal)), "insights: a warm revisit inside the interval makes no Insights request");
            await CaptureAsync(frame, "insights-dashboard-warm-1440x900.png");
        }
        finally
        {
            server.PersistLayout = false;
            server.StoredLayout = null;
        }
    }

    /// <summary>With an Insights page registered the cards show "Insights ›" where the official header is wide enough, and the link opens it.</summary>
    private static async Task InsightsLinksAsync(Window window, Server server)
    {
        server.PersistLayout = true;
        server.LayoutETag = "\"I3\"";
        server.StoredLayout = InsightsLayout;
        server.SessionRows = [];
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var (frame, page) = OpenDashboard(window);
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            void ResizeLogical(int width, int height) => window.AppWindow.ResizeClient(
                new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            await Task.Delay(900);
            string[] ids = ["insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people", "insights-library-coverage", "top-titles", "top-profiles"];
            foreach (var id in ids)
            {
                var widget = WidgetFrame(page, id);
                var (present, visible) = InsightsLinks(widget);
                // The official container query: the chart cards show it from a 512px header, the leaderboards from 384px.
                var minimum = id.StartsWith("top-", StringComparison.Ordinal) ? 384 : 512;
                var headerWidth = widget.ActualWidth - 50;
                Check(present == 1 && (visible == 1) == (headerWidth >= minimum), $"insights links: {id} shows its Insights link by its header width when the page can open (header {headerWidth:0}px, {present} present, {visible} visible)");
                Check(HasText(widget, "Insights ›") == (visible == 1), $"insights links: {id} reads Insights › exactly while the link shows");
            }
            await CaptureAsync(frame, "insights-dashboard-with-links-1440x900.png");
            ResizeLogical(800, 900);
            page.ViewportWidthOverride = 800;
            await Task.Delay(800);
            // A card narrower than the official @lg header (512px) hides its link; the leaderboards' @sm (384px) is lower.
            foreach (var id in new[] { "insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people", "insights-library-coverage" })
            {
                var widget = WidgetFrame(page, id);
                var (present, visible) = InsightsLinks(widget);
                var headerWidth = widget.ActualWidth - 48;
                Check(present == 1 && (visible == 1) == (headerWidth >= 512), $"insights links: {id} follows its header width at 800px (header {headerWidth:0}px, visible {visible})");
            }
            await CaptureAsync(frame, "insights-dashboard-with-links-800x900.png");
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Task.Delay(500);
            // The link opens the Insights page in the shell.
            // The shell opens it through the app's navigation frame, which this fixture's host has to supply.
            var navigation = (SiloPlayer.Helpers.NavigationService)((IServiceProvider)typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                .GetService(typeof(SiloPlayer.Helpers.NavigationService))!;
            navigation.Frame = frame;
            try
            {
                var link = Descendants(WidgetFrame(page, "top-titles")).OfType<Button>().First(button => AutomationProperties.GetName(button) == "Insights ›");
                Invoke(link);
                bool OpenedTitles() => Descendants(frame).OfType<AdminInsightsPage>().Any(insights => insights.Filters.Tab == AdminInsightsTab.Titles);
                await Until(OpenedTitles, 3000);
                Check(OpenedTitles(), "insights links: the link opens the registered Insights page on the Titles tab");
            }
            finally
            {
                navigation.Frame = null;
            }
        }
        finally
        {
            server.PersistLayout = false;
            server.StoredLayout = null;
        }
    }

    /// <summary>The Add widget sheet lists the six Insights widgets, in registry order, with the official names and descriptions.</summary>
    private static async Task InsightsAddSheetAsync(Window window, Server server)
    {
        server.PersistLayout = true;
        server.LayoutETag = "\"I4\"";
        server.StoredLayout = """{"version":1,"entries":[{"id":"stat-movies","span":2,"rows":1},{"id":"users","span":6,"rows":3}]}""";
        server.SessionRows = [];
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var (frame, page) = OpenDashboard(window);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            await Task.Delay(500);
            page.ToggleCustomize();
            page.OpenAddSheet();
            await Task.Delay(500);
            Check(page.IsSheetOpen, "insights sheet: Add widget opens");
            var sheet = (FrameworkElement)typeof(AdminDashboardPage).GetField("_sheet", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var titles = Descendants(sheet).OfType<TextBlock>().Select(text => text.Text).ToArray();
            string[] expected = ["Playback over time", "Viewing hours", "Plays by day of week", "Plays by hour of day", "Active people", "Library coverage"];
            var at = Array.IndexOf(titles, "Most active profiles");
            Check(at >= 0 && titles.Skip(at).Where(title => expected.Contains(title)).SequenceEqual(expected), "insights sheet: the six widgets are listed after Most active profiles, in the official order");
            Check(titles.Contains("When people start watching, by day of the week and hour") && titles.Contains("How much of each movie and TV library was played over the chosen window"),
                "insights sheet: descriptions are the official words");
            var target = Descendants(sheet).OfType<Button>().First(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "Playback over time"));
            target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
            await Task.Delay(300);
            await CaptureAsync(frame, "insights-add-widget-sheet-1440x900.png");
            page.ToggleCustomize();
        }
        finally
        {
            server.PersistLayout = false;
            server.StoredLayout = null;
        }
    }

    private static async Task CaptureWidgetsAsync(AdminDashboardPage page, string[] ids, int width)
    {
        foreach (var id in ids)
        {
            var widget = WidgetFrame(page, id);
            widget.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Task.Delay(120);
            await CaptureAsync(widget, $"insights-widget-{id}-{width}.png");
        }
    }

    /// <summary>The unsupported cases: no data when both sources are absent, and legacy leaderboards when only Insights is absent.</summary>
    private static async Task InsightsFailureAsync(Window window, Server server, AuthService auth)
    {
        // A new authority drops the retained reads, so the first Insights read of this visit is the one that fails.
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        server.PersistLayout = true;
        server.LayoutETag = "\"I2\"";
        server.StoredLayout = InsightsLayout;
        server.SessionRows = [];
        server.InsightsUnsupported = true;
        server.LegacyTopActivityFails = true;
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var (frame, page) = OpenDashboard(window);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            var titles = DashboardDataStore.InsightsTitles(DashboardWidgetRange.Week);
            var profiles = DashboardDataStore.InsightsPeople(DashboardWidgetRange.All);
            await Until(() => page.Store is { } store && store.Get(titles).Error != null && store.Get(profiles).Error != null);
            var dashboard = page.Store!;
            foreach (var (id, key, prefix) in new[] { ("top-titles", titles, "topTitles"), ("top-profiles", profiles, "topProfiles") })
            {
                var widget = WidgetFrame(page, id);
                var error = DashboardText.T($"web.admin.dashboard.widgets.{prefix}.loadFailed");
                await Until(() => HasText(widget, error));
                Check(dashboard.Get(key) is { Data: null, Error: ApiException { StatusCode: 500 } } && HasText(widget, error),
                    $"insights: {id} shows a load error when Insights is absent and the legacy read fails");
                Check(InsightsLinks(widget).Present == 0, $"insights: {id} does not link to the absent Insights endpoint on a legacy load error");
            }

            server.LegacyTopActivityFails = false;
            await Task.WhenAll(dashboard.FetchAsync(titles), dashboard.FetchAsync(profiles));
            await Task.Delay(800);
            foreach (var id in new[] { "insights-plays", "insights-viewing-hours", "insights-library-coverage", "top-titles", "top-profiles" })
            {
                var widget = WidgetFrame(page, id);
                var leaderboard = id.StartsWith("top-", StringComparison.Ordinal);
                Check(HasText(widget, DashboardText.T(leaderboard ? "web.admin.dashboard.widgets.insights.unsupported"
                    : "web.admin.dashboard.widgets.insights.loadFailed")), $"insights: {id} says it is unavailable on a server without Insights");
                if (leaderboard) Check(InsightsLinks(widget).Present == 0, $"insights: {id} has no Insights link when both endpoints are absent");
                Check(!HasText(widget, "0"), $"insights: {id} shows no zero for data that is not there");
            }
            await CaptureAsync(frame, "insights-dashboard-unsupported-1440x900.png");

            server.Reads.Clear();
            server.LegacyTopActivityAvailable = true;
            await Task.WhenAll(page.Store!.FetchAsync(titles), page.Store.FetchAsync(profiles));
            var titlesWidget = WidgetFrame(page, "top-titles");
            var profilesWidget = WidgetFrame(page, "top-profiles");
            await Until(() => HasText(titlesWidget, "Legacy Movie") && HasText(profilesWidget, "Legacy Profile · viewer"));
            Check(HasText(titlesWidget, "Legacy Movie"), "insights: Top titles renders a legacy row when Insights is absent");
            Check(HasText(profilesWidget, "Legacy Profile · viewer"), "insights: Top profiles renders a legacy row when Insights is absent");
            Check(server.Reads.Contains("/api/v2/admin/stats/top-activity?days=7"), "insights: the week fallback requests seven days");
            Check(server.Reads.Contains("/api/v2/admin/stats/top-activity?days=30"), "insights: the all-time fallback requests 30 days");
            Check(HasText(profilesWidget, DashboardRanges.Title(DashboardText.T("web.admin.dashboard.widgets.topProfiles.title"), DashboardWidgetRange.Month))
                && page.LayoutSync!.Entries.Single(entry => entry.Id == DashboardRegistry.TopProfiles).Range == DashboardWidgetRange.All,
                "insights: the legacy card labels its 30-day data without replacing the saved all-time range");
            Check(InsightsLinks(titlesWidget).Present == 0 && InsightsLinks(profilesWidget).Present == 0,
                "insights: the legacy leaderboards do not link to unavailable Insights data");
        }
        finally
        {
            server.LegacyTopActivityAvailable = false;
            server.LegacyTopActivityFails = false;
            server.InsightsUnsupported = false;
            server.PersistLayout = false;
            server.StoredLayout = null;
        }
    }
}
