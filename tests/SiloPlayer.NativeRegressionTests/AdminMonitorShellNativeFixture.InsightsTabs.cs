using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Playback, Titles and Library, the title panel and "Add to dashboard" over synthetic reads (official c59798116 schema). The
/// server below answers the Insights reads, the dashboard layout (PUT/GET with validators) and an account's profiles; every flag a case turns
/// on is reset by the case. No live server, account or user data.
/// </summary>
internal static partial class AdminMonitorShellNativeFixture
{
    /// <summary>What the synthetic Insights server answers and records.</summary>
    private sealed class InsightsTabsState
    {
        public bool Fail, FailTitle, FailNextAccounts, Unsupported, Empty, RejectSaves, FullCoverage;
        public int AccountCount;
        public string? Layout;
        public string ETag = "\"L0\"";
        public TaskCompletionSource? HoldTitles, HoldSave;
        /// <summary>Every Insights read and layout call in order, "GET /path?query" or "PUT /path".</summary>
        public readonly List<string> Calls = [];
        public int Saves;
    }

    private static class InsightsTabsRoutes
    {
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problem(HttpStatusCode status, string type) =>
            Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/" + type + "\",\"title\":\"Synthetic\",\"status\":" + (int)status + "}", status);

        public static async Task<HttpResponseMessage?> TryHandleAsync(HttpRequestMessage request, InsightsTabsState state, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            var query = uri.Query;
            if (path == "/api/v2/admin/dashboard/layout")
            {
                lock (state.Calls) state.Calls.Add(request.Method + " " + path);
                if (request.Method == HttpMethod.Put)
                {
                    if (state.HoldSave is { } held) await held.Task.WaitAsync(ct);
                    if (state.RejectSaves) return Problem(HttpStatusCode.PreconditionFailed, "precondition_failed");
                    using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    state.Layout = body.RootElement.GetProperty("layout").GetRawText();
                    state.Saves++;
                    state.ETag = "\"L" + state.Saves + "\"";
                    var saved = new HttpResponseMessage(HttpStatusCode.NoContent);
                    saved.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(state.ETag);
                    return saved;
                }
                var response = Json("{\"layout\":" + (state.Layout ?? "null") + ",\"updated_at\":\"2026-11-25T10:00:00Z\"}");
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(state.ETag);
                return response;
            }
            if (Regex.IsMatch(path, "^/api/v2/admin/users/\\d+/profiles$"))
                return Json("{\"items\":[{\"id\":\"p1\",\"name\":\"Kids\"},{\"id\":\"p2\",\"name\":\"Movie Night\"}],\"page\":{\"has_more\":false}}");
            if (path == "/api/v2/admin/users")
            {
                if (state.FailNextAccounts)
                {
                    state.FailNextAccounts = false;
                    return Problem(HttpStatusCode.BadRequest, "fixture_account_read_failed");
                }
                var start = int.TryParse(Regex.Match(query, "[?&]cursor=page-([0-9]+)").Groups[1].Value, out var offset) ? offset : 0;
                var count = Math.Clamp(state.AccountCount - start, 0, 200);
                var items = Enumerable.Range(start + 1, count).Select(id => new { id = id.ToString(CultureInfo.InvariantCulture), username = "Account " + id });
                var more = start + count < state.AccountCount;
                return Json(System.Text.Json.JsonSerializer.Serialize(new { items, page = new { has_more = more, next_cursor = more ? "page-" + (start + count) : null } }));
            }
            if (path == "/api/v2/admin/playback-history") return Json("{\"items\":[],\"page\":{\"has_more\":false}}");
            if (!path.StartsWith("/api/v2/admin/insights/", StringComparison.Ordinal)) return null;
            lock (state.Calls) state.Calls.Add(request.Method + " " + path + query);
            if (state.Unsupported) return Problem(HttpStatusCode.NotImplemented, "capability_unsupported");
            if (state.Fail) return Json("{\"title\":\"Synthetic read failed\",\"status\":500}", HttpStatusCode.InternalServerError);
            var now = DateTimeOffset.UtcNow;
            if (path.EndsWith("/activity", StringComparison.Ordinal)) return Json(InsightsTabsData.Activity(now, query, state));
            if (path.EndsWith("/titles", StringComparison.Ordinal))
            {
                if (state.HoldTitles is { } hold) await hold.Task.WaitAsync(ct);
                return Json(InsightsTabsData.Titles(now, query, state));
            }
            if (path.EndsWith("/library", StringComparison.Ordinal)) return Json(InsightsTabsData.Library(now, query, state));
            var title = Regex.Match(path, "/titles/([^/]+)$");
            if (title.Success)
            {
                if (state.FailTitle) return Problem(HttpStatusCode.BadRequest, "fixture_title_read_failed");
                var id = Uri.UnescapeDataString(title.Groups[1].Value);
                return id == "gone" ? Problem(HttpStatusCode.NotFound, "not_found") : Json(InsightsTabsData.Title(now, id, query, state));
            }
            return Problem(HttpStatusCode.NotFound, "not_found");
        }
    }

    private static class InsightsTabsData
    {
        private static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private static string Param(string query, string name) =>
            Regex.Match(query, "[?&]" + name + "=([^&]*)") is { Success: true } match ? Uri.UnescapeDataString(match.Groups[1].Value) : "";

        public static string Activity(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var json = JsonNode.Parse(InsightsFixture.Activity(now, query))!.AsObject();
            var from = DateTimeOffset.Parse(json["from"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            var to = DateTimeOffset.Parse(json["to"]!.GetValue<string>(), CultureInfo.InvariantCulture);
            var peaks = new JsonArray();
            var index = 0;
            for (var day = from; day < to && index < 400; day = day.AddDays(1), index++)
            {
                // Quiet days have no row at all, as the server omits them; the chart must leave a gap.
                if (index % 9 == 4) continue;
                var direct = 2 + index % 4;
                var remux = index % 3;
                var transcode = index % 5 == 0 ? 2 : index % 2;
                var total = direct + remux + transcode;
                peaks.Add(new JsonObject
                {
                    ["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["peak_direct"] = direct, ["peak_remux"] = remux, ["peak_transcode"] = transcode,
                    ["peak_streams"] = total, ["peak_streams_at"] = Iso(day.AddHours(20)), ["peak_streams_direct"] = direct, ["peak_streams_remux"] = remux,
                    ["peak_streams_transcode"] = transcode,
                });
            }
            json["peak_streams"] = peaks;
            json["end_reasons"] = new JsonArray(Reason("stopped", 260), Reason("expired", 80), Reason("replaced", 62), Reason("terminated", 12), Reason("aborted", 6), Reason("future_reason", 2));
            if (state.FullCoverage) json["coverage"]!["measured_sessions"] = 420;
            if (state.Empty)
            {
                foreach (var bucket in json["buckets"]!.AsArray())
                    foreach (var name in new[] { "plays", "direct", "remux", "transcode", "movies", "episodes", "other_media", "watch_seconds", "active_accounts", "active_profiles" })
                        bucket![name] = 0;
                json["heatmap"] = new JsonArray();
                json["peak_streams"] = new JsonArray();
                json["end_reasons"] = new JsonArray();
                var summary = json["summary"]!.AsObject();
                foreach (var name in summary.Select(pair => pair.Key).ToArray())
                    summary[name] = name == "peak_concurrent_at" ? (JsonNode?)null : name == "completion_rate" ? JsonValue.Create(0d) : JsonValue.Create(0);
                json["coverage"]!["total_sessions"] = 0;
                json["coverage"]!["measured_sessions"] = 0;
            }
            return json.ToJsonString();
            static JsonObject Reason(string key, int sessions) => new() { ["key"] = key, ["sessions"] = sessions };
        }

        private sealed record TitleSeed(string Id, string Title, string Kind, long Year, long Plays, long Viewers, long Watch, long Episodes, long Completed);

        private static readonly TitleSeed[] Movies =
        [
            new("m0", "Synthetic Feature", "movie", 1990, 40, 3, 40 * 6800, 0, 26), new("m1", "Arrival of the Synthetic Fleet", "movie", 1996, 33, 6, 33 * 6000, 0, 20),
            new("m2", "Heat Haze", "movie", 2002, 26, 2, 26 * 7400, 0, 9), new("m3", "", "movie", 2008, 19, 4, 19 * 5200, 0, 12), new("m4", "Paper Lanterns", "movie", 2014, 12, 5, 12 * 7000, 0, 10),
        ];

        private static readonly TitleSeed[] Shows =
        [
            new("s0", "Synthetic Series", "series", 2008, 52, 4, 52 * 2900, 24, 30), new("s1", "Severance of Ties", "series", 2012, 43, 7, 43 * 2500, 18, 31),
            new("s2", "Andor Station", "series", 2016, 34, 3, 34 * 3100, 10, 20), new("s3", "The Long Quiet With A Remarkably Long Series Name That Must End In An Ellipsis", "series", 2020, 25, 2, 25 * 2200, 8, 18),
        ];

        private static JsonObject Row(TitleSeed seed) => new()
        {
            ["completed_plays"] = seed.Completed, ["content_id"] = seed.Id, ["episodes"] = seed.Episodes, ["kind"] = seed.Kind, ["last_played_at"] = Iso(DateTimeOffset.UtcNow.AddDays(-2)),
            ["plays"] = seed.Plays, ["title"] = seed.Title, ["viewers"] = seed.Viewers, ["watch_seconds"] = seed.Watch, ["year"] = seed.Year,
        };

        public static string Titles(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var sort = Param(query, "sort") is "watch_time" or "viewers" ? Param(query, "sort") : "plays";
            Func<TitleSeed, long> key = sort switch { "watch_time" => seed => seed.Watch, "viewers" => seed => seed.Viewers, _ => seed => seed.Plays };
            JsonArray Rows(IEnumerable<TitleSeed> seeds) => state.Empty ? new JsonArray() : new JsonArray(seeds.OrderByDescending(key).Select(seed => (JsonNode)Row(seed)).ToArray());
            var least = new[] { Movies[2] with { Plays = 9, Completed = 2 }, Movies[4] with { Plays = 7, Completed = 3 }, Shows[0] with { Plays = 12, Completed = 6 } };
            var libraries = new JsonArray(
                new JsonObject { ["library_id"] = "1", ["name"] = "Movies", ["plays"] = 130, ["viewers"] = 9, ["watch_seconds"] = 130 * 6400 },
                new JsonObject { ["library_id"] = "2", ["name"] = "TV Shows", ["plays"] = 154, ["viewers"] = 12, ["watch_seconds"] = 154 * 2700 },
                new JsonObject { ["library_id"] = "3", ["name"] = "Documentaries and Concerts With A Long Name", ["plays"] = 11, ["viewers"] = 2, ["watch_seconds"] = 11 * 5000 });
            var days = Param(query, "range") switch { "7d" => 7, "90d" => 90, "1y" => 364, "all" => 760, _ => 30 };
            return new JsonObject
            {
                ["from"] = Iso(now.AddDays(-days)), ["least_finished"] = Rows(least), ["libraries"] = state.Empty ? new JsonArray() : libraries, ["movies"] = Rows(Movies),
                ["shows"] = Rows(Shows), ["sort"] = sort, ["to"] = Iso(now), ["tz"] = "UTC",
            }.ToJsonString();
        }

        public static string Title(DateTimeOffset now, string id, string query, InsightsTabsState state)
        {
            var series = id.StartsWith('s');
            var seed = (series ? Shows : Movies).FirstOrDefault(item => item.Id == id) ?? (series ? Shows[0] : Movies[0]);
            var scoped = Param(query, "user_id").Length > 0;
            var days = Param(query, "range") switch { "7d" => 7, "90d" => 90, "1y" => 364, "all" => 760, _ => 30 };
            var end = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            var start = end.AddDays(-days);
            var buckets = new JsonArray();
            var total = 0L;
            var step = days > 90 ? 7 : 1;
            for (var day = start; day < end; day = day.AddDays(step))
            {
                var plays = ((day.DayOfYear * 7) % 9 is var p && p > 5 ? p - 5 : 0) * (scoped ? 1 : 2);
                total += plays;
                buckets.Add(new JsonObject { ["bucket_start"] = Iso(day), ["label_date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["plays"] = plays, ["watch_seconds"] = plays * 3200 });
            }
            if (state.Empty) total = 0;
            var viewers = new JsonArray();
            var names = new[] { ("7", "p1", "Kids", "viewer"), ("7", "p2", "Movie Night With A Long Profile Name", "viewer"), ("8", "", "guest", "guest"), ("9", "p9", "Dad", "dad-account"), ("10", "p10", "Mom", "mom-account") };
            foreach (var (user, profile, name, username) in names.Where(item => !scoped || item.Item1 == Param(query, "user_id") && (Param(query, "profile_id").Length == 0 || item.Item2 == Param(query, "profile_id"))))
            {
                var viewer = new JsonObject
                {
                    ["completed_plays"] = 2, ["furthest_position_seconds"] = series ? (JsonNode?)null : JsonValue.Create(4320 + user.Length * 30), ["last_played_at"] = Iso(now.AddDays(-user.Length - 1)), ["plays"] = 3 + user.Length,
                    ["profile_id"] = profile, ["profile_name"] = name, ["user_id"] = user, ["username"] = username, ["watch_seconds"] = 3 * 3200 + user.Length * 600,
                };
                if (series) viewer["last_episode"] = new JsonObject { ["content_id"] = "e" + user, ["episode_number"] = 4, ["season_number"] = 2, ["title"] = "The Long Night" };
                viewers.Add(viewer);
            }
            var episodes = new JsonArray();
            if (series)
                for (var number = 1; number <= 9; number++)
                    episodes.Add(new JsonObject
                    {
                        ["content_id"] = "e" + number, ["episode_number"] = number, ["plays"] = 12 - number, ["season_number"] = number > 6 ? 2 : 1,
                        ["title"] = number == 3 ? "An Episode Title That Is Long Enough To Be Cut Off At The End Of Its Row" : "Episode " + number, ["viewers"] = 1 + number % 3, ["watch_seconds"] = (12 - number) * 2500,
                    });
            return new JsonObject
            {
                ["bucket_unit"] = step == 1 ? "day" : "week", ["buckets"] = buckets, ["content_id"] = seed.Id, ["duration_seconds"] = series ? (JsonNode?)null : JsonValue.Create(7380), ["episodes"] = episodes,
                ["episodes_truncated"] = series && id == "s0", ["from"] = Iso(start), ["kind"] = seed.Kind,
                ["summary"] = new JsonObject
                {
                    ["completed_plays"] = scoped ? 2 : seed.Completed, ["direct_plays"] = scoped ? 2 : seed.Plays / 2, ["plays"] = scoped ? 3 : seed.Plays, ["remux_plays"] = scoped ? 1 : seed.Plays / 4,
                    ["sessions"] = scoped ? 3 : seed.Plays + 2, ["transcode_plays"] = scoped ? 0 : seed.Plays - seed.Plays / 2 - seed.Plays / 4, ["viewers"] = viewers.Count, ["watch_seconds"] = seed.Watch,
                },
                ["title"] = seed.Title, ["to"] = Iso(end), ["tz"] = "UTC", ["viewers"] = viewers, ["viewers_truncated"] = id == "m1", ["year"] = seed.Year,
            }.ToJsonString();
        }

        public static string Library(DateTimeOffset now, string query, InsightsTabsState state)
        {
            var json = JsonNode.Parse(InsightsFixture.Library(now, query))!.AsObject();
            JsonObject Plays(string key, long plays, long titles) => new() { ["key"] = key, ["plays"] = plays, ["titles"] = titles };
            JsonObject Count(string key, long sessions) => new() { ["key"] = key, ["sessions"] = sessions };
            if (state.Empty)
            {
                json["libraries"] = new JsonArray();
                foreach (var name in new[] { "genres", "content_ratings", "decades", "time_to_first_play", "recently_added_unplayed" }) json[name] = new JsonArray();
                var summary = json["summary"]!.AsObject();
                foreach (var name in summary.Select(pair => pair.Key).ToArray()) summary[name] = name == "median_days_to_first_play" ? (JsonNode?)null : name == "played_share" ? JsonValue.Create(0d) : JsonValue.Create(0);
                return json.ToJsonString();
            }
            json["genres"] = new JsonArray(Plays("Drama", 120, 41), Plays("Comedy", 96, 33), Plays("Science Fiction", 71, 22), Plays("Documentary", 40, 11), Plays("unknown", 6, 3));
            json["content_ratings"] = new JsonArray(Plays("TV-MA", 140, 30), Plays("PG-13", 88, 28), Plays("R", 54, 19), Plays("TV-14", 33, 12), Plays("unknown", 2, 1));
            json["decades"] = new JsonArray(Plays("1980", 18, 6), Plays("1990", 44, 15), Plays("2000", 61, 20), Plays("2010", 112, 38), Plays("2020", 98, 31), Plays("unknown", 4, 2));
            json["time_to_first_play"] = new JsonArray(Count("day", 9), Count("week", 11), Count("month", 8), Count("later", 4), Count("not_yet", 22));
            json["recently_added_unplayed"] = new JsonArray(
                Unplayed("u1", "Quiet Harbor", 2025, "Movies", now.AddDays(-2)), Unplayed("u2", "A Documentary With A Remarkably Long Title That Has To Wrap Or End Somewhere", 2024, "Documentaries and Concerts With A Long Name", now.AddDays(-3)),
                Unplayed("u3", "Untitled Concert", 0, "Movies", now.AddDays(-5)));
            return json.ToJsonString();

            static JsonObject Unplayed(string id, string title, long year, string library, DateTimeOffset added) => new()
            {
                ["added_at"] = Iso(added), ["content_id"] = id, ["kind"] = "movie", ["library_id"] = "1", ["library_name"] = library, ["title"] = title, ["year"] = year == 0 ? (JsonNode?)null : JsonValue.Create(year),
            };
        }
    }

    private static readonly BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    private static async Task InsightsTabsAsync(Window window, Server server, AuthService auth, NavigationService navigation, ToastService toast)
    {
        var data = server.Insights;
        server.SessionCount = 3;
        server.RestartRequired = false;
        auth.SetCurrentUser(new SiloPlayer.Core.Models.Auth.UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var root = new Grid { Children = { frame } };
        window.Content = root;
        navigation.Frame = frame;
        AdminRouteRequest Request(params (string, string)[] query) =>
            new(AdminRoute.Insights, query.Length == 0 ? null : query.ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal));
        frame.Navigate(typeof(AdminShellPage), Request(("tab", "playback")));
        var shell = (AdminShellPage)frame.Content;
        var inner = (Frame)Field(shell, "_frame")!;
        var insights = (AdminInsightsPage)inner.Content;

        T? Find<T>(DependencyObject root, string id) where T : DependencyObject =>
            Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
        Button? Named(DependencyObject root, string name) => Descendants(root).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
        // The tree is rebuilt on the UI thread between polls; a walk that catches it half-swapped simply is not true yet.
        async Task Wait(Func<bool> condition, int milliseconds = 4000) => await Until(() => { try { return condition(); } catch (ArgumentException) { return false; } }, milliseconds);
        bool Try(Func<bool> condition) { try { return condition(); } catch (ArgumentException) { return false; } }
        bool Has(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text == text));
        bool HasCI(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => string.Equals(label.Text, text, StringComparison.OrdinalIgnoreCase)));
        bool HasContaining(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text.Contains(text, StringComparison.Ordinal)));
        bool HasStarting(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text.StartsWith(text, StringComparison.Ordinal)));
        Grid? Card(string title) => Descendants(insights).OfType<Grid>().FirstOrDefault(grid => grid.GetType().Name == "AdminInsightsCard" && AutomationProperties.GetName(grid) == title);
        bool Pressed(Button button) => AutomationProperties.GetItemStatus(button) == "pressed";
        Button[] Links(DependencyObject root) => [.. Descendants(root).OfType<Button>().Where(button => button.Tag as string == "insights-link")];
        Button? Add(string widget) => Find<Button>(insights, "AdminInsightsAdd_" + widget);
        Button? OnDashboard(string widget) => Find<Button>(insights, "AdminInsightsOnDashboard_" + widget);
        int ReadsOf(string needle) { lock (data.Calls) return data.Calls.Count(call => call.Contains(needle, StringComparison.Ordinal)); }
        async Task Resize(int width)
        {
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
            await Task.Delay(350);
        }
        async Task Open(AdminRouteRequest request)
        {
            var previous = insights;
            shell.Navigate(request);
            await Wait(() => inner.Content is AdminInsightsPage page && !ReferenceEquals(page, previous));
            insights = (AdminInsightsPage)inner.Content;
        }
        async Task CaptureTab(string name, int width)
        {
            ((ScrollViewer)Field(insights, "_scroll")!).ChangeView(null, 0, null, true);
            await Task.Delay(150);
            await CaptureAsync(frame, $"insights-{name}-{width}.png");
            // Scrolled through, so every card is captured at least once at the real window size.
            var scroll = (ScrollViewer)Field(insights, "_scroll")!;
            var step = Math.Max(200, scroll.ViewportHeight - 120);
            var shot = 1;
            for (var offset = step; shot < 12; offset += step)
            {
                scroll.ChangeView(null, Math.Min(offset, scroll.ScrollableHeight), null, true);
                await Task.Delay(250);
                await CaptureAsync(frame, $"insights-{name}-{width}-s{shot++}.png");
                if (offset >= scroll.ScrollableHeight) break;
            }
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(150);
            // The whole page content in one image.
            await CaptureAsync((FrameworkElement)Field(insights, "_body")!, $"insights-{name}-{width}-full.png");
        }
        async Task CaptureSheetEnd(string name)
        {
            var sheet = Descendants(shell).OfType<Grid>().First(grid => grid.GetType().Name == "LogsDetailSheet");
            var scroller = Descendants(sheet).OfType<ScrollViewer>().First();
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
            await Task.Delay(350);
            await CaptureAsync(frame, name);
        }
        async Task CaptureSheet(string name)
        {
            await Task.Delay(250);
            await CaptureAsync(frame, name);
        }

        // ── Playback ─────────────────────────────────────────
        await Resize(1440);
        await Wait(() => Card("Playback over time") != null && Card("How sessions end") != null);
        Check(new[] { "Playback over time", "Viewing hours", "Peak concurrent streams", "Plays by day of week", "Plays by hour of day", "Active people", "How sessions end" }
            .All(title => Card(title) != null), "Playback shows all seven cards");
        Check(new[] { "Plays", "Watch time", "Completion rate", "Peak concurrent streams" }.All(label => Has(insights, label)) && Has(insights, "Exact for 400 of 420 sessions"),
            "Playback shows the four KPI cards with the measured-sessions subtext");
        Check(Has(insights, "Exact watch time is available for 400 of 420 sessions; older sessions are estimated."), "Playback shows the watch-time banner when some sessions were not measured");
        Check(Has(Card("Peak concurrent streams")!, "Lines") && Has(Card("Peak concurrent streams")!, "Stacked") && Has(Card("Peak concurrent streams")!, "Total"), "Peak concurrent streams offers Lines, Stacked and Total");
        Check(!HasStarting(Card("Peak concurrent streams")!, "Always shows the whole server"), "no scope notice without a library or account filter");
        Check(Has(Card("How sessions end")!, "Stopped") && Has(Card("How sessions end")!, "Timed out") && Has(Card("How sessions end")!, "Not recorded"), "end reasons are labelled and an unknown one is not recorded");
        await Wait(() => new[] { "insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people" }.All(widget => Add(widget) != null), 10000);
        Check(new[] { "insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people" }.All(widget => Add(widget) is { ActualWidth: > 40, Visibility: Visibility.Visible }),
            "Add to dashboard appears on the five official Playback cards once the layout has loaded");
        Check(Add("insights-plays") is { } addPlays && AutomationProperties.GetName(addPlays) == "Add Playback over time to your dashboard", "Add to dashboard carries its official accessible name");
        await CaptureTab("playback", 1440);

        var volume = Card("Playback over time")!;
        Check(Named(volume, "Play method") is { } method && Pressed(method) && Named(volume, "Plays") is { } playsToggle && Pressed(playsToggle), "Playback volume starts on plays by play method");
        Invoke(Named(volume, "Media type")!);
        await Wait(() => Has(Card("Playback over time")!, "Movies"));
        Check(Has(Card("Playback over time")!, "Movies") && Has(Card("Playback over time")!, "Episodes") && Has(Card("Playback over time")!, "Other"), "Media type splits the volume chart into movies, episodes and other");
        await CaptureAsync(Card("Playback over time")!, "insights-playback-volume-media.png");
        Invoke(Named(Card("Playback over time")!, "Watch time")!);
        await Wait(() => Named(Card("Playback over time")!, "Play method") == null);
        Check(Named(Card("Playback over time")!, "Play method") == null && Pressed(Named(Card("Playback over time")!, "Watch time")!), "Watch time hides the split toggle");
        await CaptureAsync(Card("Playback over time")!, "insights-playback-volume-watch.png");
        Invoke(Named(Card("Playback over time")!, "Plays")!);
        await Wait(() => Named(Card("Playback over time")!, "Play method") != null);
        Check(Named(Card("Playback over time")!, "Play method") != null, "Plays brings the split toggle back");

        Check(Pressed(Named(Card("Peak concurrent streams")!, "Show each playback method as a line")!), "peak concurrent starts on lines");
        await CaptureAsync(Card("Peak concurrent streams")!, "insights-playback-peak-lines.png");
        Invoke(Named(Card("Peak concurrent streams")!, "Show playback methods stacked")!);
        await Wait(() => Pressed(Named(Card("Peak concurrent streams")!, "Show playback methods stacked")!));
        await CaptureAsync(Card("Peak concurrent streams")!, "insights-playback-peak-stacked.png");
        Invoke(Named(Card("Peak concurrent streams")!, "Show total streams only")!);
        await Wait(() => Pressed(Named(Card("Peak concurrent streams")!, "Show total streams only")!));
        await CaptureAsync(Card("Peak concurrent streams")!, "insights-playback-peak-total.png");
        Invoke(Named(Card("Peak concurrent streams")!, "Show each playback method as a line")!);

        Invoke(Named(Card("Plays by day of week")!, "Watch time")!);
        await Wait(() => Pressed(Named(Card("Plays by day of week")!, "Watch time")!));
        Check(Pressed(Named(Card("Plays by day of week")!, "Watch time")!) && Pressed(Named(Card("Plays by hour of day")!, "Plays")!), "weekday and hour have their own plays and watch-time switches");
        await CaptureAsync(Card("Plays by day of week")!, "insights-playback-weekday-watch.png");

        var cells = Descendants(Card("Viewing hours")!).OfType<ContentControl>().Where(cell => AutomationProperties.GetName(cell).Contains(" at ", StringComparison.Ordinal)).ToArray();
        Check(cells.Length == 168 && cells.Count(cell => cell.IsTabStop) == 1, "the full heatmap has 7x24 cells and one tab stop");
        Check(cells.All(cell => ToolTipService.GetToolTip(cell) is string tip && tip == AutomationProperties.GetName(cell)) && AutomationProperties.GetName(cells[20]).StartsWith("Mon at ", StringComparison.Ordinal),
            "every heatmap cell has its tooltip as its accessible name");

        var refresh = Find<Button>(insights, "AdminInsightsRefresh")!;
        Invoke(refresh);
        await Wait(() => !refresh.IsEnabled);
        await Wait(() => refresh.IsEnabled);
        string LastRead(string path) { lock (data.Calls) return data.Calls.Last(call => call.Contains(path, StringComparison.Ordinal)); }
        Check(LastRead("/insights/activity").Contains("refresh=true", StringComparison.Ordinal), "Refresh reads with refresh=true");

        await Open(Request(("tab", "playback"), ("library_id", "1")));
        await Wait(() => Card("Peak concurrent streams") != null);
        Check(Has(Card("Peak concurrent streams")!, "Always shows the whole server — account and library filters don’t apply here."), "a library filter adds the peak card's scope notice");
        await CaptureAsync(Card("Peak concurrent streams")!, "insights-playback-peak-scope.png");

        // ── Add to dashboard ─────────────────────────────────
        await Open(Request(("tab", "playback"), ("range", "90d")));
        await Wait(() => Add("insights-plays") != null);
        Invoke(Add("insights-plays")!);
        await Wait(() => OnDashboard("insights-plays") != null);
        var pins = (AdminInsightsDashboardPins)typeof(AdminInsightsPage).GetProperty("Pins", NonPublic)!.GetValue(insights)!;
        Check(OnDashboard("insights-plays") != null && Add("insights-plays") == null, "after an add the card shows On dashboard");
        Check(pins.Sync.Entries.Single(entry => entry.Id == "insights-plays").Range == DashboardWidgetRange.Quarter, "the card's 90d window becomes the widget's quarter range");
        Check(AutomationProperties.GetName(OnDashboard("insights-plays")!) == "Playback over time is on your dashboard. Open the dashboard.", "On dashboard is a link named as official");
        await CaptureAsync(Card("Playback over time")!, "insights-playback-on-dashboard.png");
        await Wait(() => data.Saves >= 1, 5000);
        Check(data.Saves == 1 && data.Layout != null && data.Layout.Contains("insights-plays", StringComparison.Ordinal) && data.Layout.Contains("\"quarter\"", StringComparison.Ordinal),
            "the add is saved once, with its window");

        // A refused save blocks the layout owner: no other card offers an add that could not reach the server.
        data.RejectSaves = true;
        Invoke(Add("insights-weekday")!);
        await Wait(() => pins.Sync.ServerSaveBlocked, 5000);
        await Wait(() => Add("insights-hour-of-day") == null && Add("insights-active-people") == null, 3000);
        Check(pins.Sync.ServerSaveBlocked && Add("insights-hour-of-day") == null && Add("insights-active-people") == null && OnDashboard("insights-weekday") != null && OnDashboard("insights-plays") != null,
            "a refused save hides every other Add to dashboard button and keeps the links");
        data.RejectSaves = false;

        // A new visit adds a card and opens the dashboard at once: the dashboard's first layout read waits for that save.
        await Open(Request(("tab", "playback")));
        await Wait(() => Add("insights-hour-of-day") != null);
        lock (data.Calls) data.Calls.Clear();
        Invoke(Add("insights-hour-of-day")!);
        await Wait(() => OnDashboard("insights-hour-of-day") != null);
        Invoke(OnDashboard("insights-hour-of-day")!);
        await Wait(() => inner.Content is AdminDashboardPage);
        var dashboard = (AdminDashboardPage)inner.Content;
        await Wait(() => dashboard.LayoutSync?.IsLoaded == true, 8000);
        int Index(string prefix)
        {
            lock (data.Calls)
            {
                var matches = data.Calls.Select((call, position) => (call, position)).Where(item => item.call.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                return matches.Length == 0 ? -1 : matches[0].position;
            }
        }
        var put = Index("PUT /api/v2/admin/dashboard/layout");
        var firstGet = Index("GET /api/v2/admin/dashboard/layout");
        Check(put >= 0 && firstGet > put, $"the dashboard's first layout read waits for the pending add's save (PUT at {put}, first GET at {firstGet})");
        Check(dashboard.LayoutSync!.Entries.Any(entry => entry.Id == "insights-hour-of-day") && dashboard.PlacedWidgets().Any(widget => widget.Id == "insights-hour-of-day"),
            "the dashboard shows the widget that was added from Insights");
        Check(data.Layout!.Contains("insights-hour-of-day", StringComparison.Ordinal) && data.Layout.Contains("insights-plays", StringComparison.Ordinal), "the server holds both adds");
        await Task.Delay(600);
        await CaptureAsync(frame, "insights-dashboard-with-added-widget-1440.png");

        // A 412 after following the optimistic link still reports the failure on the Dashboard, without a success toast.
        var toasts = new ToastContainer { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(toasts);
        toast.Register(toasts, root.DispatcherQueue);
        try
        {
            await Open(Request(("tab", "playback")));
            await Wait(() => Add("insights-active-people") != null);
            lock (data.Calls) data.Calls.Clear();
            data.RejectSaves = true;
            var reject = data.HoldSave = new TaskCompletionSource();
            Invoke(Add("insights-active-people")!);
            await Wait(() => OnDashboard("insights-active-people") != null);
            Invoke(OnDashboard("insights-active-people")!);
            await Wait(() => inner.Content is AdminDashboardPage && Index("PUT /api/v2/admin/dashboard/layout") >= 0);
            var rejectedDashboard = (AdminDashboardPage)inner.Content;
            var added = AdminInsightsDashboardPins.AddedMessage("Active people");
            Check(Index("GET /api/v2/admin/dashboard/layout") == -1 && !Has(toasts, added), "the next layout read and add confirmation wait for the PUT");

            reject.SetResult();
            var warning = DashboardText.T("web.admin.dashboard.hooks.dashboardLayout.saveUnconfirmed");
            await Wait(() => rejectedDashboard.LayoutSync?.IsLoaded == true && Has(toasts, warning), 8000);
            Check(Has(toasts, warning) && !Has(toasts, added)
                && rejectedDashboard.LayoutSync?.Entries.All(entry => entry.Id != "insights-active-people") == true,
                "the Dashboard shows the save failure and keeps the server layout after a rejected departure write");
        }
        finally
        {
            data.HoldSave?.TrySetResult();
            data.HoldSave = null;
            data.RejectSaves = false;
            toast.Unregister();
            root.Children.Remove(toasts);
        }

        // ── Titles ───────────────────────────────────────────
        await Open(Request(("tab", "titles")));
        await Wait(() => Card("Top movies") != null && Has(Card("Top movies")!, "Synthetic Feature (1990)"));
        Check(new[] { "Top movies", "Top shows", "Top libraries", "Least finished" }.All(title => Card(title) != null), "Titles shows its four cards");
        Check(Has(Card("Top movies")!, "40 plays") && Has(Card("Top movies")!, "Removed title (2008)"), "Titles ranks by plays and calls a title the library lost a removed title");
        Check(Pressed(Named(insights, "Plays")!) && Named(insights, "Watch time") != null && Named(insights, "Viewers") != null, "the rank-by toggle offers plays, watch time and viewers");
        Check(Has(Card("Least finished")!, "78% unfinished") && Has(Card("Least finished")!, "2 of 9 completed"), "Least finished bars are the share left unfinished");
        var titleReads = ReadsOf("GET /api/v2/admin/insights/titles?");
        Invoke(Find<Button>(insights, "AdminInsightsTab_library")!);
        await Wait(() => Card("Library coverage") != null && Has(Card("Library coverage")!, "65% played"));
        Invoke(Find<Button>(insights, "AdminInsightsTab_titles")!);
        await Wait(() => Card("Top movies") is { } movies && Has(movies, "Synthetic Feature (1990)"));
        Check(Card("Top movies") is { } cached && Has(cached, "Synthetic Feature (1990)")
            && ReadsOf("GET /api/v2/admin/insights/titles?") == titleReads, "switching back to Titles reuses its fresh result without an extra GET");
        await Wait(() => Add("top-titles") != null || OnDashboard("top-titles") != null);
        Check(Add("top-titles") != null || OnDashboard("top-titles") != null, "Titles offers Add to dashboard for Top titles");
        await CaptureTab("titles", 1440);

        // While a new sort loads the old rows stay, keep the formatting they were ranked with, and their links stop working.
        data.HoldTitles = new TaskCompletionSource();
        Invoke(Named(insights, "Viewers")!);
        await Wait(() => ReadsOf("sort=viewers") > 0);
        await Task.Delay(200);
        Check(Has(Card("Top movies")!, "40 plays") && Pressed(Named(insights, "Viewers")!), "while a new sort loads the rows keep the formatting of the sort they were ranked with");
        Check(Links(Card("Top movies")!).Length > 0 && Links(Card("Top movies")!).All(link => !link.IsEnabled), "links in the held result are disabled");
        await CaptureAsync(Card("Top movies")!, "insights-titles-held.png");
        data.HoldTitles.SetResult();
        data.HoldTitles = null;
        await Wait(() => Has(Card("Top movies")!, "6 viewers"));
        Check(Has(Card("Top movies")!, "6 viewers") && Links(Card("Top movies")!).All(link => link.IsEnabled), "the new ranking shows viewers and its links work again");
        Invoke(Named(insights, "Watch time")!);
        await Wait(() => Has(Card("Top shows")!, "41.9h"));
        Check(Has(Card("Top shows")!, "41.9h"), "watch-time ranking formats watch time");

        Invoke(Links(Card("Top libraries")!)[0]);
        await Wait(() => shell.CurrentRequest.Get("library_id") != null);
        Check(shell.CurrentRequest.Get("library_id") == "2" || shell.CurrentRequest.Get("library_id") == "1", "a Top libraries row narrows the page to that library");

        // ── Title panel ──────────────────────────────────────
        await Open(Request(("tab", "titles")));
        await Wait(() => Card("Top movies") != null && Links(Card("Top movies")!).Length > 0);
        Invoke(Links(Card("Top movies")!)[0]);
        await Wait(() => shell.CurrentRequest.Get("title") != null && HasCI(shell, "Who watched"));
        Check(shell.CurrentRequest.Get("title") == "m0" && Has(shell, "Synthetic Feature (1990)") && HasCI(shell, "Plays over time"), "a title row opens the panel");
        Check(Find<Button>(shell, "AdminInsightsTitleOpen") != null && Find<Button>(shell, "AdminInsightsTitleHistory") != null, "the panel offers Open title and View history");
        Check(HasContaining(shell, "Reached 1h 12m of 2h 3m") && !HasCI(shell, "Episodes"), "a movie viewer shows how far they got and a movie has no Episodes section");
        Check(HasStarting(shell, "Movie · "), "the panel describes its kind and window");
        await CaptureSheet("insights-title-panel-movie-1440.png");
        data.FailTitle = true;
        // The modal disables the toolbar; invoke the page's shared refresh action directly while it is open.
        await ((Task)typeof(AdminInsightsPage).GetMethod("RefreshAsync", NonPublic)!.Invoke(insights, null)!).WaitAsync(TimeSpan.FromSeconds(8));
        await Wait(() => Find<Border>(shell, "AdminInsightsSectionError") != null);
        var panelError = Find<Border>(shell, "AdminInsightsSectionError");
        Check(panelError != null && HasCI(shell, "Who watched") && Card("Top movies") != null, "a panel-only refresh failure warns beside retained title data");
        data.FailTitle = false;
        Invoke(Named(panelError!, "Retry")!);
        await Wait(() => Find<Border>(shell, "AdminInsightsSectionError") == null);
        Check(Find<Border>(shell, "AdminInsightsSectionError") == null && HasCI(shell, "Who watched"), "the title panel retry clears the warning");
        Invoke(Find<Button>(shell, "LogsDetailClose")!);
        await Wait(() => shell.CurrentRequest.Get("title") == null);
        Check(shell.CurrentRequest.Get("title") == null, "closing the panel removes the title from the location");

        await Open(Request(("tab", "titles"), ("title", "s0")));
        await Wait(() => HasCI(shell, "Episodes") && Has(shell, "Synthetic Series (2008)"));
        Check(Has(shell, "Showing the first 500 episodes.") && HasContaining(shell, "Last: S02E04 · The Long Night") && Has(shell, "S01E01"),
            "a series panel lists its episodes with the truncation note and each viewer's last episode");
        await CaptureSheet("insights-title-panel-series-1440.png");
        await CaptureSheetEnd("insights-title-panel-series-1440-end.png");

        await Open(Request(("tab", "titles"), ("title", "m1")));
        await Wait(() => Has(shell, "Showing the 100 most recent viewers."));
        Check(Has(shell, "Showing the 100 most recent viewers."), "the viewer truncation note shows when the server truncated");

        await Open(Request(("tab", "titles"), ("title", "s0"), ("person", "7"), ("profile", "p1")));
        await Wait(() => Has(shell, "Plays by Kids"));
        Check(Has(shell, "Plays by Kids") && Find<Button>(shell, "AdminInsightsShowEveryone") != null, "a panel scoped to a person names them and offers Show everyone");
        bool ScopedRead() { lock (data.Calls) return data.Calls.Any(call => call.Contains("/titles/s0", StringComparison.Ordinal) && call.Contains("user_id=7", StringComparison.Ordinal) && call.Contains("profile_id=p1", StringComparison.Ordinal)); }
        Check(ScopedRead(), "the scoped panel reads with the person's account and profile");
        await CaptureSheet("insights-title-panel-person-1440.png");
        Invoke(Find<Button>(shell, "AdminInsightsShowEveryone")!);
        await Wait(() => shell.CurrentRequest.Get("person") == null);
        Check(shell.CurrentRequest.Get("person") == null && shell.CurrentRequest.Get("title") == "s0" && !Has(shell, "Plays by Kids"), "Show everyone keeps the title and drops the person");

        await Open(Request(("tab", "titles"), ("title", "gone")));
        await Wait(() => Find<TextBlock>(shell, "AdminInsightsTitleNotFound") != null);
        Check(Find<TextBlock>(shell, "AdminInsightsTitleNotFound") is { Text: "This title is no longer in your library." }, "a title that is gone says so");
        await CaptureSheet("insights-title-panel-notfound-1440.png");

        await Open(Request(("tab", "titles"), ("range", "custom"), ("from", "invalid"), ("tz", new string('x', AdminInsightsApi.MaxTzLength + 1)), ("title", "m0")));
        await Wait(() => Card("Top movies") is { } movies && Has(movies, "Synthetic Feature (1990)") && HasCI(shell, "Who watched"));
        Check(Find<CalendarDatePicker>(insights, "AdminInsightsFilter_fromDate")?.Date != null
            && LastRead("/insights/titles?").Contains("from_date=" + insights.Filters.FromDate, StringComparison.Ordinal)
            && !LastRead("/insights/titles/m0?").Contains("tz=xxxx", StringComparison.Ordinal)
            && HasCI(shell, "Who watched"), "a malformed deep link uses safe date and zone defaults for the tab and title panel");
        var detailReads = ReadsOf("GET /api/v2/admin/insights/titles/");
        await Open(Request(("tab", "titles"), ("title", new string('x', AdminInsightsApi.MaxContentIdLength + 1))));
        await Wait(() => Card("Top movies") is { } movies && Has(movies, "Synthetic Feature (1990)"));
        Check(Field(insights, "_detailSheet") is FrameworkElement { Visibility: Visibility.Collapsed }
            && Find<Button>(shell, "AdminInsightsTitleHistory") == null && ReadsOf("GET /api/v2/admin/insights/titles/") == detailReads,
            "an oversized title id does not open a loading panel or issue a detail request");

        data.AccountCount = 10_001;
        data.FailNextAccounts = true;
        await Open(Request(("tab", "titles"), ("range", "7d")));
        Button? AccountAction(string name) => Find<Button>(insights, "AdminInsightsFilter_" + name);
        bool AccountOffered(int id) => (AccountAction("allAccounts")?.Flyout as MenuFlyout)?.Items
            .OfType<RadioMenuFlyoutItem>().Any(item => item.Text == "Account " + id) == true;
        await Wait(() => AccountAction("accountsRetry")?.IsEnabled == true);
        Check(Has(insights, "Could not load accounts.") && !AccountOffered(1), "a failed account read is distinguished from an empty account list");
        Invoke(AccountAction("accountsRetry")!);
        await Wait(() => AccountOffered(1) && AccountAction("accountsNext")?.IsEnabled == true);
        Check(AccountOffered(1) && !AccountOffered(201) && AccountAction("accountsRetry") == null, "retry restores the first bounded account page");
        for (var first = 201; first <= data.AccountCount; first += 200)
        {
            if (AccountAction("accountsNext") is not { IsEnabled: true } next) break;
            Invoke(next);
            await Wait(() => AccountOffered(first));
            if (!AccountOffered(first)) break;
        }
        Check(AccountOffered(10_001) && !AccountOffered(1) && AccountAction("accountsNext") == null,
            "paging reaches account 10,001 without holding every earlier page in the picker");
        data.AccountCount = 0;

        await Open(Request(("tab", "titles"), ("title", "s0"), ("library_id", "2")));
        await Wait(() => Find<Button>(shell, "AdminInsightsTitleHistory") != null);
        Invoke(Find<Button>(shell, "AdminInsightsTitleHistory")!);
        await Wait(() => inner.Content is AdminPlaybackHistoryPage);
        var history = (AdminPlaybackHistoryPage)inner.Content;
        Button? HistoryChip(string name) => Find<Button>(history, "AdminHistory" + name + "Filter");
        var chipNames = new[] { "Series", "Library", "StartedAfter", "StartedBefore" };
        await Wait(() => chipNames.All(name => HistoryChip(name)?.Visibility == Visibility.Visible));
        Check(chipNames.All(name => HistoryChip(name)?.Visibility == Visibility.Visible), "History shows the series, library and both date bounds even with no matching plays");
        Invoke(HistoryChip("StartedAfter")!);
        await Wait(() => shell.CurrentRequest.Get("started_after") == null && HistoryChip("StartedAfter")?.Visibility != Visibility.Visible);
        Check(shell.CurrentRequest.Get("started_after") == null && shell.CurrentRequest.Get("started_before") != null
            && shell.CurrentRequest.Get("series_id") == "s0" && shell.CurrentRequest.Get("library_id") == "2", "removing one History bound preserves the rest of the drilldown");

        // ── Library ──────────────────────────────────────────
        await Open(Request(("tab", "library")));
        await Wait(() => Card("Library coverage") != null && Has(Card("Library coverage")!, "Movies"));
        Check(new[] { "Titles played", "Never started", "Added this period", "Wait for a first play" }.All(label => Has(insights, label)), "Library shows its four KPI cards");
        Check(new[] { "Library coverage", "Plays by genre", "Plays by content rating", "Plays by release decade", "Time to first play", "Added and not played yet" }.All(title => Card(title) != null), "Library shows its six cards");
        Check(Has(Card("Time to first play")!, "Within a day") && Has(Card("Time to first play")!, "Not played yet") && Has(Card("Plays by genre")!, "Unknown"), "time to first play and genres use the official labels");
        Check(Has(Card("Library coverage")!, "65% played") && HasContaining(Card("Library coverage")!, "never started"), "coverage shows the played share and the never-started count");
        await Wait(() => Add("insights-library-coverage") != null || OnDashboard("insights-library-coverage") != null);
        await CaptureTab("library", 1440);
        Invoke(Links(Card("Added and not played yet")!)[0]);
        await Wait(() => shell.CurrentRequest.Get("title") != null);
        Check(shell.CurrentRequest.Get("title") == "u1", "an unplayed title opens the title panel");
        Invoke(Find<Button>(shell, "LogsDetailClose")!);

        // ── Errors, retries, empties and an older server ─────
        await Open(Request(("tab", "library"), ("range", "30d")));
        await Wait(() => Card("Library coverage") != null);
        data.Fail = true;
        Invoke(Find<Button>(insights, "AdminInsightsRefresh")!);
        await Wait(() => HasStarting(insights, "Failed to load insights."));
        Check(Card("Library coverage") != null && HasStarting(insights, "Failed to load insights."), "a failed Refresh keeps the last data and says it failed");
        await CaptureAsync(frame, "insights-library-refresh-failed-1440.png");
        await Open(Request(("tab", "titles"), ("range", "7d")));
        await Wait(() => Find<Border>(insights, "AdminInsightsSectionError") != null);
        Check(Find<Border>(insights, "AdminInsightsSectionError") != null && Card("Top movies") == null, "a failed first read shows the load error instead of data");
        await CaptureAsync(frame, "insights-titles-error-1440.png");
        data.Fail = false;
        Invoke(Named(Find<Border>(insights, "AdminInsightsSectionError")!, "Retry")!);
        await Wait(() => Card("Top movies") != null);
        Check(Card("Top movies") != null, "Retry reads again and shows the result");

        data.Unsupported = true;
        await Open(Request(("tab", "playback"), ("range", "7d")));
        await Wait(() => Find<Border>(insights, "AdminInsightsSectionError") != null);
        Check(Has(insights, "This server does not provide Insights for this view."), "a server without the endpoints shows the load error");
        data.Unsupported = false;

        data.Empty = true;
        await Open(Request(("tab", "playback"), ("range", "all")));
        await Wait(() => Card("Playback over time") != null);
        Check(Has(Card("Playback over time")!, "No playback in this period") && Has(Card("Viewing hours")!, "No viewing activity in this period") && Has(Card("Peak concurrent streams")!, "No stream data in this period"),
            "an empty window shows each card's own empty line");
        await CaptureTab("playback-empty", 1440);
        await Open(Request(("tab", "titles"), ("range", "all")));
        await Wait(() => Card("Top movies") != null);
        Check(Has(Card("Top movies")!, "No movies watched in this period") && Has(Card("Least finished")!, "Nothing with 3 or more plays was left unfinished in this period"), "an empty Titles window shows its empty lines");
        await CaptureTab("titles-empty", 1440);
        await Open(Request(("tab", "library"), ("range", "all")));
        await Wait(() => Card("Library coverage") != null);
        Check(Has(Card("Library coverage")!, "No movie or TV libraries yet") && Has(Card("Added and not played yet")!, "Everything added this period has been played"), "an empty Library window shows its empty lines");
        await CaptureTab("library-empty", 1440);
        data.Empty = false;

        // ── Narrow ───────────────────────────────────────────
        await Resize(560);
        foreach (var tab in new[] { "playback", "titles", "library" })
        {
            await Open(Request(("tab", tab)));
            var first = tab switch { "playback" => "Playback over time", "titles" => "Top movies", _ => "Library coverage" };
            await Wait(() => Card(first) != null);
            await Wait(() => Descendants(insights).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button).StartsWith("AdminInsightsAdd_", StringComparison.Ordinal)
                || AutomationProperties.GetAutomationId(button).StartsWith("AdminInsightsOnDashboard_", StringComparison.Ordinal)));
            await Task.Delay(300);
            await CaptureTab(tab, 560);
        }
        await Open(Request(("tab", "titles"), ("title", "s0"), ("person", "7"), ("profile", "p1")));
        await Wait(() => Has(shell, "Plays by Kids") && HasCI(shell, "Episodes"));
        await CaptureSheet("insights-title-panel-series-560.png");
        await CaptureSheetEnd("insights-title-panel-series-560-end.png");
        await Open(Request(("tab", "titles"), ("title", "m0")));
        await Wait(() => HasCI(shell, "Who watched"));
        await CaptureSheet("insights-title-panel-movie-560.png");
        await Resize(1440);

        Check(frame.Navigate(typeof(Page)), "Insights can be left through the outer frame after using the data tabs");
        await Wait(() => !insights.IsLoaded);
        navigation.Frame = null;
        // The cases that follow expect the Monitor shell's own defaults.
        server.RestartRequired = true;
        server.SessionCount = 3;
    }
}
