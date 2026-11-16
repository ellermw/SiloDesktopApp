using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Manage › Libraries › Libraries in the real Admin shell through its production
/// registration hook (<c>AddAdminLibraries</c>), against a state-aware synthetic server and
/// a scripted realtime channel (no live server, account or media; no file picker). Proves the
/// table and cards, status badges, active work and warnings, the scan queue popover and its
/// cancel, every row command (scan, refresh dialog, stop refresh, mount check, delete and
/// cleanup confirmations), keyboard and card-menu reorder, the add/edit editor with its four
/// sections, validation, server folder browser, provider order, translation prewarm and save,
/// the five diagnostics sections (search, paging, ignore/undo, match tool, matcher detail and
/// retry, root override, skipped details/sort, stale IDs), the legacy Autoscan link notice,
/// leave guard, authority retirement, and captures at 1440×900, 1440×600, 1920×600 and 800×600.
/// Dispatch: SILO_NATIVE_TEST_ONLY=admin-libraries.
/// </summary>
internal static class AdminLibrariesNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public int Leases;
        public IDisposable Subscribe(params string[] channels) { if (channels.Contains("scans")) Interlocked.Increment(ref Leases); return new Release(() => { if (channels.Contains("scans")) Interlocked.Decrement(ref Leases); }); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse(channel == "scans" ? Scans : "[]").RootElement.Clone();
            return channel == "scans";
        }
        public string Scans =
            "[{\"id\":\"s1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\",\"trigger\":\"manual\",\"started_at\":\"2026-11-15T09:00:00Z\",\"result\":{\"total_files\":400,\"files_processed\":100,\"message\":\"Scanning files\"}}," +
            "{\"id\":\"s2\",\"library_id\":2,\"mode\":\"subtree\",\"path\":\"/media/shows/A\",\"status\":\"running\",\"trigger\":\"realtime_monitor\"}," +
            string.Join(',', Enumerable.Range(3, 5).Select(i => "{\"id\":\"s" + i + "\",\"library_id\":2,\"mode\":\"file\",\"path\":\"/media/shows/B/e" + i + ".mkv\",\"status\":\"accepted\",\"trigger\":\"autoscan\"}")) + "]";
        public void Event(string channel, string name, string json) => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action release) : IDisposable { private Action? _release = release; public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke(); }
        internal void Silence() { SnapshotReceived?.Invoke("", default); StateChanged?.Invoke(CurrentState); }
    }

    private sealed record Lib(string Id, string Name, string Type, string[] Paths, string? Warning, string? Poster = null);

    /// <summary>A synthetic library server: stateful order, jobs, diagnostics and directory tree.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly object _gate = new();
        public ConcurrentQueue<string> Requests { get; } = new();
        public ConcurrentDictionary<string, string> Bodies { get; } = new();
        public List<Lib> Libraries =
        [
            new("1", "Movies", "movies", ["/media/movies"], null, "/posters/1.png"),
            new("2", "Shows", "series", ["/media/shows", "/media/shows-4k"], null),
            new("3", "Audiobooks", "audiobooks", ["/media/books"], "dead_root"),
            new("4", "Mixed", "mixed", ["/media/mixed"], "empty_root"),
        ];
        public bool RefreshJobCanceling;
        public bool TranslationRunning;
        public readonly HashSet<string> Ignored = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Query(request.RequestUri);
            var line = $"{request.Method} {request.RequestUri.PathAndQuery}";
            Requests.Enqueue(line);
            if (request.Content != null) Bodies[line] = await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate) return Route(request.Method, path, query, line);
        }

        private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : "", StringComparer.Ordinal);

        private HttpResponseMessage Route(HttpMethod method, string path, Dictionary<string, string> query, string line)
        {
            switch (path)
            {
                case "/api/v2/libraries" when method == HttpMethod.Get:
                    return Json("{\"items\":[" + string.Join(',', Libraries.Select(LibraryJson)) + "],\"page\":{\"has_more\":false}}");
                case "/api/v2/libraries" when method == HttpMethod.Post:
                {
                    using var body = JsonDocument.Parse(Bodies[line]);
                    var created = new Lib("9", body.RootElement.GetProperty("name").GetString()!, body.RootElement.GetProperty("type").GetString()!,
                        body.RootElement.GetProperty("paths").EnumerateArray().Select(item => item.GetString()!).ToArray(), null);
                    Libraries.Add(created);
                    return Json(LibraryJson(created), HttpStatusCode.Created);
                }
                case "/api/v2/libraries/reorder":
                {
                    using var body = JsonDocument.Parse(Bodies[line]);
                    var order = body.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("id").GetString()!).ToList();
                    Libraries = order.Select(id => Libraries.First(library => library.Id == id)).ToList();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                case "/api/v2/libraries/capabilities":
                    return Json("{\"allowed\":true,\"box_sets\":true,\"ignore_unmatched\":true,\"realtime_monitoring\":true,\"revision\":\"r1\",\"state\":\"available\",\"trickplay\":true,\"trickplay_supported\":true}");
                case "/api/v2/libraries/realtime-monitoring":
                    return Json("{\"server_enabled\":true,\"libraries\":[{\"library_id\":\"1\",\"enabled\":true,\"state\":\"limit_reached\",\"backend\":\"inotify\",\"detail\":\"inotify watch limit reached\",\"directories\":1200},{\"library_id\":\"2\",\"enabled\":true,\"state\":\"monitoring\",\"backend\":\"inotify\",\"detail\":\"\",\"directories\":12}]}");
                case "/api/v2/admin/trickplay/libraries":
                    return Json("{\"items\":[{\"library_id\":\"1\",\"name\":\"Movies\",\"pending\":10,\"running\":2,\"ready\":8,\"unusable\":1,\"sheet_bytes\":52428800}]}");
                case "/api/v2/admin/jobs":
                    return Json("{\"items\":[{\"id\":\"j2\",\"kind\":\"library_refresh\",\"state\":\"" + (RefreshJobCanceling ? "canceling" : "running") + "\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T09:30:00Z\",\"library_id\":\"2\",\"library_ids\":[\"2\"],\"artifact_size_bytes\":0,\"public_link_supported\":false,\"progress\":{\"current\":40,\"total\":120,\"unit\":\"items\"}}],\"page\":{\"has_more\":true,\"next_cursor\":\"older\"}}");
                case "/api/v2/scan":
                    return Json("{\"library_id\":\"4\",\"mode\":\"library\",\"status\":\"accepted\"}", HttpStatusCode.Accepted);
                case "/api/v2/scan/cancel":
                    return Json("{\"library_id\":\"2\",\"cancelled\":6}");
                case "/api/v2/admin/tasks/scan_libraries/run":
                    return Json("{\"key\":\"scan_libraries\",\"name\":\"Scan libraries\",\"state\":\"running\"}");
                case "/api/v2/library-jobs/j2/cancel":
                    RefreshJobCanceling = true;
                    return Json("{\"id\":\"j2\",\"kind\":\"library_refresh\",\"state\":\"canceling\",\"terminal\":false,\"cancelable\":false,\"created_at\":\"2026-11-15T09:30:00Z\"}", HttpStatusCode.Accepted);
                case "/api/v2/libraries/metadata-match-queue":
                    return Json("{\"items\":[{\"library_id\":\"1\",\"movie_count\":2,\"series_count\":0,\"raw_file_count\":1,\"pending_count\":2,\"parked_count\":1,\"total_count\":3}]}");
                case "/api/v2/libraries/1/metadata-match-queue":
                    return Json("{\"library_id\":\"1\",\"movies\":[{\"library_id\":\"1\",\"media_file_id\":\"51\",\"file_path\":\"/media/movies/Heat.1995.mkv\",\"state\":\"parked\",\"failure_kind\":\"low_confidence\",\"attempt_count\":3,\"failure_detail\":{\"decision\":{\"outcome\":\"candidate_rejected\",\"rejection\":\"below_floor\",\"required_score\":0.9,\"top_candidates\":[{\"title\":\"Heat\",\"year\":1995,\"score\":0.81,\"reasons\":[\"title\"],\"sources\":[\"tmdb\"]}]}}}],\"series\":[],\"raw_files\":[{\"library_id\":\"1\",\"media_file_id\":\"52\",\"file_path\":\"/media/movies/Unknown.mkv\",\"base_title\":\"Unknown\",\"base_year\":2001,\"base_type\":\"movie\"}],\"page\":{\"has_more\":false},\"total_count\":3}");
                case "/api/v2/libraries/1/metadata-match-queue/retry":
                    return Json("{\"library_id\":\"1\",\"status\":\"queued\",\"queue\":{\"library_id\":\"1\",\"movie_count\":2,\"series_count\":0,\"raw_file_count\":1,\"pending_count\":3,\"parked_count\":0,\"total_count\":3}}");
                case "/api/v2/libraries/unmatched-items":
                {
                    var ignored = query.ContainsKey("ignored");
                    var all = Enumerable.Range(1, 23).Select(i => "u" + i).Where(id => ignored ? Ignored.Contains(id) : !Ignored.Contains(id)).ToList();
                    if (query.TryGetValue("q", out var q)) all = all.Where(id => ("Unmatched " + id).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                    var limit = int.Parse(query["limit"]);
                    var offset = query.TryGetValue("cursor", out var cursor) ? int.Parse(cursor[1..]) : 0;
                    var page = all.Skip(offset).Take(limit).ToList();
                    var next = offset + limit < all.Count ? ",\"page\":{\"has_more\":true,\"next_cursor\":\"o" + (offset + limit) + "\"}" : ",\"page\":{\"has_more\":false}";
                    return Json("{\"items\":[" + string.Join(',', page.Select(id => "{\"content_id\":\"" + id + "\",\"content_type\":\"" + (id == "u2" ? "audiobook" : "movie") + "\",\"library_id\":\"1\",\"library_name\":\"Movies\",\"status\":\"unmatched\",\"title\":\"Unmatched " + id + "\",\"year\":2004}")) + "]" + next + ",\"total\":" + all.Count + "}");
                }
                case "/api/v2/libraries/roots":
                    return Json("{\"items\":[" + string.Join(',', Enumerable.Range(1, 12).Select(i => "{\"library_id\":\"" + query["library_id"] + "\",\"library_name\":\"Movies\",\"root_path\":\"/media/movies/Root " + i + "\",\"state\":\"ambiguous\",\"inferred_type\":\"" + (i % 2 == 0 ? "series" : "movie") + "\",\"type_confidence\":\"low\",\"title\":\"Root " + i + "\",\"year\":1999,\"observed_file_count\":" + i + ",\"first_seen_at\":\"2026-11-14T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\",\"evidence_json\":{\"has_folder_ids\":false,\"season_structure_files\":2}" + (i == 1 ? ",\"content_id\":\"c-root-1\",\"active_override\":{\"forced_type\":\"movie\",\"note\":\"checked\"}" : "") + "}")) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"r2\"},\"total\":30}");
                case "/api/v2/libraries/roots/override":
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/api/v2/libraries/skipped-roots":
                    return Json("{\"items\":[" + string.Join(',', new[] { ("/media/movies/Extras", "no media files", 0), ("/media/shows/Specials", "unsupported layout", 4), ("/media/movies/Samples", "sample files only", 2) }
                        .Select(row => "{\"library_id\":\"1\",\"library_name\":\"Movies\",\"root_path\":\"" + row.Item1 + "\",\"reason\":\"" + row.Item2 + "\",\"file_count\":" + row.Item3 + ",\"sample_file_path\":\"" + row.Item1 + "/a.mkv\",\"first_seen_at\":\"2026-11-1" + row.Item3 + "T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\"}")) + "],\"page\":{\"has_more\":false},\"total\":3}");
                case "/api/v2/libraries/stale-ids":
                    return Json("{\"items\":[{\"content_id\":\"st1\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"Movies\",\"provider\":\"tmdb\",\"provider_id\":\"1234\",\"title\":\"Old Film\",\"year\":1987,\"first_seen_at\":\"2026-11-10T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\"}],\"page\":{\"has_more\":false},\"total\":1}");
                case "/api/v2/admin/filesystem/browse":
                {
                    var browse = query["path"];
                    var limit = int.Parse(query["limit"]);
                    var children = browse switch
                    {
                        "/" => new[] { "mnt", "srv" },
                        "/mnt" => new[] { "media" },
                        "/mnt/media" => new[] { "movies", "shows", "music" },
                        _ => Array.Empty<string>(),
                    };
                    if (query.TryGetValue("name_prefix", out var prefix)) children = children.Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var parent = browse == "/" ? "/" : browse[..Math.Max(1, browse.LastIndexOf('/'))];
                    var shown = children.Take(limit).ToArray();
                    var more = children.Length > limit ? "{\"has_more\":true,\"next_cursor\":\"b\"}" : "{\"has_more\":false}";
                    return Json("{\"path\":\"" + browse + "\",\"parent\":\"" + parent + "\",\"items\":[" + string.Join(',', shown.Select(name => "{\"name\":\"" + name + "\",\"path\":\"" + (browse == "/" ? "" : browse) + "/" + name + "\"}")) + "],\"page\":" + more + "}");
                }
                case "/api/v2/libraries/provider-defaults":
                    return Json(Chain(query["library_type"]));
                case "/api/v2/capabilities/metadata-ai":
                    return Json("{\"allowed\":true,\"state\":\"available\",\"on_view\":\"button\",\"revision\":\"r\"}");
            }
            if (path.StartsWith("/api/v2/libraries/", StringComparison.Ordinal))
            {
                var rest = path["/api/v2/libraries/".Length..];
                var id = rest.Split('/')[0];
                var library = Libraries.FirstOrDefault(entry => entry.Id == id);
                if (rest == id && method == HttpMethod.Patch && library != null)
                {
                    using var body = JsonDocument.Parse(Bodies[line]);
                    var updated = library with { Name = body.RootElement.GetProperty("name").GetString()! };
                    Libraries[Libraries.IndexOf(library)] = updated;
                    return Json(LibraryJson(updated));
                }
                if (rest == id && method == HttpMethod.Delete)
                    return Json("{\"id\":\"d" + id + "\",\"kind\":\"delete_library\",\"state\":\"queued\",\"terminal\":false,\"cancelable\":false,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted);
                if (rest == id + "/check-mount")
                    return Json("{\"library_id\":\"" + id + "\",\"library_name\":\"x\",\"healthy\":" + (id == "3" ? "false" : "true") + ",\"status\":\"x\",\"summary\":\"" + (id == "3" ? "1 of 1 roots unreachable" : "All roots reachable") + "\",\"checked_at\":\"2026-11-15T10:00:00Z\",\"roots\":[{\"path\":\"/media/books\",\"reachable\":" + (id == "3" ? "false" : "true") + ",\"suspect_empty\":false,\"error_code\":null,\"error_message\":" + (id == "3" ? "\"no such directory\"" : "null") + "}]}");
                if (rest == id + "/confirm-empty-root-cleanup") return Json("{\"status\":\"queued\",\"message\":\"Cleanup queued\"}");
                if (rest == id + "/refresh-metadata")
                    return Json("{\"id\":\"j" + id + "\",\"kind\":\"library_refresh\",\"state\":\"queued\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted);
                if (rest == id + "/providers" && method == HttpMethod.Get) return Json(Chain(library?.Type ?? "movies"));
                if (rest == id + "/providers" && method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
                if (rest == id + "/poster" && method == HttpMethod.Delete)
                {
                    if (library != null) Libraries[Libraries.IndexOf(library)] = library with { Poster = null };
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (rest.StartsWith("unmatched-items/", StringComparison.Ordinal) && rest.EndsWith("/ignore", StringComparison.Ordinal))
                {
                    var content = rest["unmatched-items/".Length..^"/ignore".Length];
                    if (method == HttpMethod.Put) Ignored.Add(content); else Ignored.Remove(content);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            }
            if (path.StartsWith("/api/v2/admin/libraries/", StringComparison.Ordinal))
            {
                if (path.EndsWith("/metadata-translation", StringComparison.Ordinal)) { TranslationRunning = true; return Json(TranslationJob("pending"), HttpStatusCode.Accepted); }
                if (path.EndsWith("/cancel", StringComparison.Ordinal)) { TranslationRunning = false; return new HttpResponseMessage(HttpStatusCode.NoContent); }
                if (path.EndsWith("/jobs", StringComparison.Ordinal)) return Json("{\"jobs\":[" + (TranslationRunning ? TranslationJob("running") : "") + "]}");
            }
            var json = path switch
            {
                "/api/v2/admin/users" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/sessions" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false,\"restart_mark_count\":0,\"restart_requested\":false}",
                _ => "{}",
            };
            return Json(json);
        }

        private static string TranslationJob(string status) =>
            "{\"id\":\"t1\",\"status\":\"" + status + "\",\"target_language\":\"de\",\"progress_message\":\"Translating descriptions\",\"progress\":0.25,\"fields_done\":25,\"fields_total\":100,\"updated_at\":\"2026-11-15T10:00:00Z\"}";

        private static string Chain(string type) =>
            "{\"levels\":[" + string.Join(',', AdminLibraryPresentation.ContentLevels(type).Select(level => "{\"content_level\":\"" + level + "\",\"entries\":[" +
                "{\"plugin_installation_id\":\"0\",\"capability_id\":\"local\",\"provider_slug\":\"nfo\",\"priority\":0,\"enabled\":true,\"local\":true}," +
                "{\"plugin_installation_id\":\"11\",\"capability_id\":\"metadata\",\"provider_slug\":\"tmdb\",\"priority\":1,\"enabled\":true,\"local\":false}," +
                "{\"plugin_installation_id\":\"12\",\"capability_id\":\"metadata\",\"provider_slug\":\"omdb\",\"priority\":2,\"enabled\":false,\"local\":false}]}")) + "]}";

        private static string LibraryJson(Lib library) =>
            "{\"id\":\"" + library.Id + "\",\"name\":\"" + library.Name + "\",\"type\":\"" + library.Type + "\",\"paths\":[" + string.Join(',', library.Paths.Select(path => "\"" + path + "\"")) +
            "],\"enabled\":" + (library.Id == "3" ? "false" : "true") + ",\"metadata_language\":\"en\",\"auto_translate_metadata\":false,\"chapter_thumbnails_enabled\":false,\"chapter_thumbnails_supported\":true," +
            "\"intro_detection_enabled\":true,\"trickplay_enabled\":false,\"trickplay_supported\":true,\"box_sets_enabled\":false,\"box_set_min_movies\":2,\"box_sets_in_search\":true," +
            "\"trailer_kinds\":[\"trailer\",\"teaser\"],\"realtime_monitoring\":true,\"sort_order\":" + library.Id + ",\"last_scanned_at\":\"2026-11-15T08:00:00Z\"" +
            (library.Poster == null ? "" : ",\"poster_url\":\"" + library.Poster + "\"") +
            (library.Warning == null ? "" : ",\"scan_warning_code\":\"" + library.Warning + "\",\"scan_warning_at\":\"2026-11-15T07:00:00Z\"") + "}";

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    internal static async Task RunAsync(Window window)
    {
        _window = window;
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var monitor = new AdminMonitorApi(client);
        var realtime = new Realtime();
        using var store = new ServerActivityStore(monitor, authority, realtime);
        var navigation = new NavigationService();
        var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitor).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new MediaMaintenanceApi(client))
            .AddSingleton(new AdminLibrariesRealtimeSource(realtime))
            .AddAdminLibraries();
        await using var provider = services.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        field.SetValue(null, provider);
        try
        {
            Resize(window, 1440, 900);
            var outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;

            // ── Entry through the production registration hook ───────────
            var request = new AdminRouteRequest(AdminRoute.Libraries);
            Check(AdminShellPage.CanOpen(request), "the registration hook makes Libraries openable");
            Check(AdminShellPage.Open(request), "Libraries enters Admin");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminLibrariesPage { Controller.State.Loaded: true } && Find<FrameworkElement>(shell, "LibrariesTable") != null);
            var page = (AdminLibrariesPage)inner.Content;
            var controller = page.Controller!;
            Check(controller.State is { Loaded: true, Error: null } && controller.State.Libraries.Count == 4, "the list settles with every synthetic library");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "the page swaps in place without a transition");
            var sidebar = (Grid)Field(shell, "_desktopSidebarHost")!;
            Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Libraries"), "the sidebar shows the registered Libraries leaf");
            foreach (var call in new[] { "GET /api/v2/libraries", "GET /api/v2/libraries/capabilities", "GET /api/v2/libraries/realtime-monitoring", "GET /api/v2/admin/trickplay/libraries",
                         "GET /api/v2/admin/jobs?kind=library_refresh&limit=50", "GET /api/v2/libraries/metadata-match-queue", "GET /api/v2/libraries/unmatched-items?limit=10",
                         "GET /api/v2/libraries/unmatched-items?limit=10&ignored=true" })
                Check(server.Requests.Contains(call), "page open reads " + call);
            Check(!server.Requests.Any(line => line.Contains("roots", StringComparison.Ordinal) || line.Contains("stale-ids", StringComparison.Ordinal)), "closed diagnostics sections read nothing");
            Check(realtime.Leases >= 2, "the page leases the shared scans channel (alongside the activity store)");
            var texts = Texts(shell);
            Check(texts.Contains("Libraries") && texts.Contains(AdminLibrariesText.T("web.admin.libraries.page.scanAll")) && texts.Contains("Add Library"), "header renders WebUI title and actions");
            Check(Find<Button>(shell, "LibrariesCatalogMaintenance") is { IsEnabled: false }, "Catalog maintenance shows as unavailable (no native Configure page yet)");

            // Rows: badges, active work, warnings, per-row actions.
            Check(Texts(shell).Any(text => text.StartsWith("Monitoring: ", StringComparison.Ordinal) || text.Contains("limit", StringComparison.OrdinalIgnoreCase)), "the monitoring attention badge renders");
            Check(Texts(shell).Any(text => text.StartsWith("Previews", StringComparison.Ordinal)), "the seek-preview badge renders");
            Check(Find<Button>(shell, "LibrariesStopRefresh_2") != null && Find<FrameworkElement>(shell, "LibrariesActiveWork_2") != null, "the running refresh job shows with its stop control");
            Check(Find<Button>(shell, "LibrariesScan_1") is { } stopScan && AutomationProperties.GetName(stopScan) == AdminLibrariesText.T("web.admin.libraries.list.stopScans"), "a library with active scans offers Stop");
            Check(Find<FrameworkElement>(shell, "LibrariesWarning_3") != null && Find<FrameworkElement>(shell, "LibrariesWarning_4") != null, "scan warnings render below the table");
            Check(Find<Button>(shell, "LibrariesCleanup_3") != null && Find<Button>(shell, "LibrariesCleanup_4") != null && Find<Button>(shell, "LibrariesCleanup_1") == null,
                "cleanup is offered only for missing or empty roots");
            Check(Find<Button>(shell, "LibrariesEdit_1") is { } edit1 && AutomationProperties.GetName(edit1) == "Edit Movies", "row actions carry WebUI accessible names");
            Check(Find<HyperlinkButton>(shell, "LibrariesScanToggle_2") != null, "more than four scans collapse behind Show all");
            await CaptureAsync(outer, "libraries-table-1440x900.png");

            // ── Scan queue popover ───────────────────────────────────────
            await InvokeAsync(Find<Button>(shell, "LibrariesScanQueue"));
            await Until(() => FindInPopups<FrameworkElement>(window, "LibrariesScanQueuePopover") != null);
            Check(FindInPopups<FrameworkElement>(window, "LibrariesScanQueuePopover") != null, "the scan queue popover opens");
            Check(FindInPopups<HyperlinkButton>(window, "LibrariesQueueToggle_2") != null, "a long queue group collapses after four rows");
            await CaptureAsync(outer, "libraries-scan-queue-1440x900.png");
            await CapturePopupAsync(window, "libraries-scan-queue-1440x900-popup.png");
            await InvokeAsync(FindInPopups<Button>(window, "LibrariesQueueCancel_2"));
            await Until(() => server.Requests.Contains("POST /api/v2/scan/cancel"));
            Check(server.Bodies.TryGetValue("POST /api/v2/scan/cancel", out var cancelBody) && cancelBody == "{\"library_id\":\"2\"}", "Cancel asks the server to stop that library's scans");
            ClosePopups(window);
            realtime.Event("scans", "scan.cancelled", "{\"id\":\"s2\",\"library_id\":2,\"mode\":\"subtree\",\"status\":\"cancelled\"}");
            for (var i = 3; i <= 7; i++) realtime.Event("scans", "scan.cancelled", "{\"id\":\"s" + i + "\",\"library_id\":2,\"mode\":\"file\",\"status\":\"cancelled\"}");
            await Until(() => controller.State.Scans.All(scan => scan.LibraryId != "2"));
            Check(controller.State.Scans.All(scan => scan.LibraryId != "2"), "finished scans leave the queue from channel events");

            // ── Row commands ─────────────────────────────────────────────
            await Until(() => Find<Button>(shell, "LibrariesScan_4") is { IsLoaded: true, IsEnabled: true });
            await InvokeAsync(Find<Button>(shell, "LibrariesScan_4"));
            await Until(() => server.Requests.Contains("POST /api/v2/scan"));
            Check(server.Bodies.GetValueOrDefault("POST /api/v2/scan") == "{\"library_id\":\"4\"}", "Scan library sends one accepted scan request");
            await InvokeAsync(Find<Button>(shell, "LibrariesScanAll"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/tasks/scan_libraries/run"));
            Check(server.Requests.Contains("POST /api/v2/admin/tasks/scan_libraries/run"), "Scan all runs the scan_libraries task");

            await InvokeAsync(Find<Button>(shell, "LibrariesRefresh_1"));
            await Until(() => DialogControl<Button>(window, "LibrariesRefreshMissing") != null);
            Check(DialogControl<Button>(window, "LibrariesRefreshAll") != null, "the refresh dialog offers missing and full refresh");
            await CaptureAsync(outer, "libraries-refresh-dialog-1440x900.png");
            await CapturePopupAsync(window, "libraries-refresh-dialog-1440x900-popup.png");
            await InvokeAsync(DialogControl<Button>(window, "LibrariesRefreshMissing"));
            await Until(() => DialogControl<Button>(window, "LibrariesRefreshMissing") == null);
            Check(server.Bodies.GetValueOrDefault("POST /api/v2/libraries/1/refresh-metadata") == "{\"mode\":\"quick\"}", "the missing-only refresh queues mode quick and closes");

            await InvokeAsync(Find<Button>(shell, "LibrariesStopRefresh_2"));
            await Until(() => server.Requests.Contains("POST /api/v2/library-jobs/j2/cancel"));
            await Until(() => Texts(shell).Contains("Cancellation requested"));
            Check(Texts(shell).Contains("Cancellation requested"), "a canceling job stays visible as cancellation requested, not completed");

            await InvokeAsync(Find<Button>(shell, "LibrariesMount_3"));
            await Until(() => controller.State.MountResults.ContainsKey("3"));
            Check(Find<Button>(shell, "LibrariesMount_3") is { } mount && AutomationProperties.GetName(mount).Contains("no such directory", StringComparison.Ordinal),
                "the mount check result shows on the row");

            await InvokeAsync(Find<Button>(shell, "LibrariesDelete_4"));
            await Until(() => DialogControl<Button>(window, "LibrariesDeleteConfirm") != null);
            await CaptureAsync(outer, "libraries-delete-confirm-1440x900.png");
            await CapturePopupAsync(window, "libraries-delete-confirm-1440x900-popup.png");
            Check(DialogControl<Button>(window, "LibrariesDeleteCancel") != null, "the delete confirmation offers Cancel");
            await InvokeAsync(DialogControl<Button>(window, "LibrariesDeleteConfirm"));
            await Until(() => server.Requests.Contains("DELETE /api/v2/libraries/4"));
            await Task.Delay(200);
            Check(server.Requests.Count(line => line == "DELETE /api/v2/libraries/4") == 1, "exactly one DELETE starts the deletion job");
            Check(controller.State.Libraries.Any(library => library.Id == "4"), "the library stays listed until the server removes it (202 is not completion)");

            await InvokeAsync(Find<Button>(shell, "LibrariesCleanup_3"));
            await Until(() => DialogControl<Button>(window, "LibrariesCleanupConfirm") != null);
            await InvokeAsync(DialogControl<Button>(window, "LibrariesCleanupConfirm"));
            await Until(() => server.Requests.Contains("POST /api/v2/libraries/3/confirm-empty-root-cleanup"));
            Check(server.Requests.Contains("POST /api/v2/libraries/3/confirm-empty-root-cleanup"), "confirming cleanup sends one cleanup request");

            // ── Keyboard reorder ─────────────────────────────────────────
            await Until(() => controller.State.CanReorder && Find<Button>(shell, "LibrariesGrip_1") is { IsEnabled: true });
            var grip = Find<Button>(shell, "LibrariesGrip_1")!;
            Check(AutomationProperties.GetName(grip) == AdminLibrariesText.T("web.admin.libraries.list.dragToReorder"), "the grip has the drag-to-reorder name");
            grip.Focus(FocusState.Keyboard);
            await Task.Delay(100);
            var keyboard = TrySendKey(0x20);
            await Task.Delay(120);
            if (keyboard) { TrySendKey(0x28); await Task.Delay(120); TrySendKey(0x20); }
            await Until(() => server.Requests.Contains("POST /api/v2/libraries/reorder"), 3000);
            Check(keyboard, "keyboard input reaches the focused grip");
            Check(server.Bodies.GetValueOrDefault("POST /api/v2/libraries/reorder") == "{\"entries\":[{\"id\":\"2\",\"position\":0},{\"id\":\"1\",\"position\":1},{\"id\":\"3\",\"position\":2},{\"id\":\"4\",\"position\":3}]}",
                "Space, Down, Space moves the library one place and writes the whole order");
            Check(Find<TextBlock>(shell, "LibrariesAnnouncer")?.Text.Contains("position 2 of 4", StringComparison.Ordinal) == true, "the move is announced with its new position");
            await Until(() => controller.State.CanReorder && controller.State.Libraries[0].Id == "2");
            Check(controller.State.Libraries.Select(library => library.Id).SequenceEqual(["2", "1", "3", "4"]), "the list reloads with the saved order before another move");

            // ── Editor: add ──────────────────────────────────────────────
            await InvokeAsync(Find<Button>(shell, "LibrariesAdd"));
            await Until(() => Find<FrameworkElement>(shell, "LibraryEditor") != null && Find<TextBox>(shell, "LibraryEditorName") != null);
            Check(page.EditorSession?.State.IsEditing == false, "Add library opens an empty editor");
            Check(Find<Button>(shell, "LibraryEditorTab_Advanced") != null, "a movies library offers the Advanced section");
            await CaptureAsync(outer, "libraries-editor-general-1440x900.png");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorSave"));
            await Until(() => Find<TextBlock>(shell, "LibraryEditorError")?.Text.Length > 0);
            Check(Find<TextBlock>(shell, "LibraryEditorError")?.Text.Contains(AdminLibrariesText.T("web.admin.common.libraryEditor.errors.nameRequired"), StringComparison.Ordinal) == true, "an empty create explains what is required");
            Find<TextBox>(shell, "LibraryEditorName")!.Text = "Synthetic Films";
            await InvokeAsync(Find<Button>(shell, "LibraryEditorSave"));
            await Until(() => Find<TextBox>(shell, "LibraryEditorPath_0") != null);
            Check(Find<TextBox>(shell, "LibraryEditorPath_0") != null, "a missing folder switches to the Folders section");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorBrowse"));
            await Until(() => DialogControl<Button>(window, "FolderBrowserEntry_mnt") != null);
            Check(DialogControl<Button>(window, "FolderBrowserEntry_srv") != null, "the folder browser lists the server's root folders");
            await InvokeAsync(DialogControl<Button>(window, "FolderBrowserEntry_mnt"));
            await Until(() => DialogControl<Button>(window, "FolderBrowserEntry_media") != null);
            await InvokeAsync(DialogControl<Button>(window, "FolderBrowserEntry_media"));
            await Until(() => DialogControl<Button>(window, "FolderBrowserSelect_movies") != null);
            await InvokeAsync(DialogControl<Button>(window, "FolderBrowserSelect_movies"));
            await Until(() => DialogControl<Button>(window, "FolderBrowserAccept") is { } accept && ((string)accept.Content).Contains('1'));
            await CaptureAsync(outer, "libraries-folder-browser-1440x900.png");
            await CapturePopupAsync(window, "libraries-folder-browser-1440x900-popup.png");
            await InvokeAsync(DialogControl<Button>(window, "FolderBrowserAccept"));
            await Until(() => page.EditorSession?.State.Paths.Contains("/mnt/media/movies") == true);
            Check(page.EditorSession?.State.Paths.SequenceEqual(["/mnt/media/movies"]) == true, "the chosen server folder becomes the library path");
            Check(server.Requests.Any(line => line.StartsWith("GET /api/v2/admin/filesystem/browse?path=%2Fmnt%2Fmedia&limit=50", StringComparison.Ordinal)), "browsing reads one bounded page per folder");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTab_Metadata"));
            await Until(() => Find<CheckBox>(shell, "LibraryEditorProvider_movie_0") != null);
            Check(Find<ToggleSwitch>(shell, "LibraryEditorLocalMetadata") != null, "local metadata is one pinned switch");
            Check(Find<Button>(shell, "LibraryEditorProviderUp_movie_0") is { IsEnabled: false }, "the first remote provider cannot move up");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorProviderDown_movie_0"));
            await Until(() => page.EditorSession?.State.ChainDirty == true);
            Check(page.EditorSession?.State.RemoteChains["movie"].Select(item => item.ProviderSlug).SequenceEqual(["omdb", "tmdb"]) == true, "Move down reorders remote providers");
            await CaptureAsync(outer, "libraries-editor-metadata-1440x900.png");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTab_Advanced"));
            await Until(() => Find<ToggleSwitch>(shell, "LibraryEditorTrickplay") != null);
            Check(Find<ToggleSwitch>(shell, "LibraryEditorIntroDetection") is { IsOn: false }, "new movie libraries leave credits detection off");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorSave"));
            await Until(() => page.EditorSession == null);
            Check(page.EditorSession == null, "a confirmed create closes the editor");
            var create = server.Bodies.GetValueOrDefault("POST /api/v2/libraries") ?? "";
            Check(create.Contains("\"name\":\"Synthetic Films\"") && !create.Contains("\"enabled\"") && !create.Contains("auto_translate_metadata"), "create sends only LibraryCreate members");
            Check(server.Requests.Contains("PUT /api/v2/libraries/9/providers"), "the edited chain is written for the created library");

            // ── Editor: edit, translation prewarm, leave guard ───────────
            await Until(() => Find<Button>(shell, "LibrariesEdit_1") is { IsEnabled: true });
            await InvokeAsync(Find<Button>(shell, "LibrariesEdit_1"));
            await Until(() => page.EditorSession?.State.IsEditing == true && Find<Button>(shell, "LibraryEditorPosterUpload") != null);
            Check(Find<Button>(shell, "LibraryEditorPosterRemove") != null, "an existing poster offers Replace and Remove");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTab_Metadata"));
            await Until(() => Find<ComboBox>(shell, "LibraryEditorTranslateLanguage") != null, 6000);
            Check(Find<ComboBox>(shell, "LibraryEditorTranslateLanguage") != null, "the translate panel shows while AI translation is available");
            var target = Find<ComboBox>(shell, "LibraryEditorTranslateLanguage")!;
            target.SelectedItem = target.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == "de");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTranslateStart"));
            await Until(() => Find<Button>(shell, "LibraryEditorTranslateStop") != null, 6000);
            Check(server.Bodies.GetValueOrDefault("POST /api/v2/admin/libraries/1/metadata-translation") == "{\"target_language\":\"de\"}", "Translate starts one prewarm for the chosen language");
            Check(Texts(shell).Any(text => text.Contains("Translating descriptions", StringComparison.Ordinal)), "the running job shows its progress");
            await CaptureAsync(outer, "libraries-editor-translate-1440x900.png");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTranslateStop"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/libraries/1/metadata-translation/jobs/t1/cancel"));
            Check(server.Requests.Contains("POST /api/v2/admin/libraries/1/metadata-translation/jobs/t1/cancel"), "Stop cancels the prewarm job");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorTab_General"));
            await Until(() => Find<TextBox>(shell, "LibraryEditorName") is { IsLoaded: true });
            Find<TextBox>(shell, "LibraryEditorName")!.Text = "Movies Renamed";
            await Until(() => page.EditorSession?.State.IsDirty == true);
            Check(shell.RequiresLeaveConfirmation, "an unsaved edit arms the shell leave guard");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorSave"));
            await Until(() => page.EditorSession == null && server.Requests.Contains("PATCH /api/v2/libraries/1"));
            var patch = server.Bodies.GetValueOrDefault("PATCH /api/v2/libraries/1") ?? "";
            Check(patch.Contains("\"name\":\"Movies Renamed\"") && patch.Contains("\"enabled\":true"), "Save changes sends the update body");
            Check(!shell.RequiresLeaveConfirmation, "a confirmed save releases the leave guard");

            // ── Diagnostics ──────────────────────────────────────────────
            await InvokeAsync(Find<Button>(shell, "LibrariesSection_Unmatched"));
            await Until(() => Find<HyperlinkButton>(shell, "UnmatchedItem_u1") != null);
            Check(Find<Button>(shell, "UnmatchedIgnore_u2") == null, "books cannot be ignored");
            Check(Find<TextBlock>(shell, "UnmatchedRange")?.Text.Contains("1", StringComparison.Ordinal) == true, "the pager shows the range");
            await InvokeAsync(Find<Button>(shell, "UnmatchedNext"));
            await Until(() => Find<HyperlinkButton>(shell, "UnmatchedItem_u11") != null);
            Check(server.Requests.Contains("GET /api/v2/libraries/unmatched-items?limit=10&cursor=o10"), "Next reads the next cursor page");
            await InvokeAsync(Find<Button>(shell, "UnmatchedIgnore_u11"));
            await Until(() => server.Requests.Contains("PUT /api/v2/libraries/unmatched-items/u11/ignore") && Find<Button>(shell, "UnmatchedUndo") != null);
            Check(Find<Button>(shell, "UnmatchedUndo") != null, "Ignore offers Undo");
            await CaptureAsync(outer, "libraries-unmatched-1440x900.png");
            await InvokeAsync(Find<Button>(shell, "UnmatchedUndo"));
            await Until(() => server.Requests.Contains("DELETE /api/v2/libraries/unmatched-items/u11/ignore"));
            Check(server.Requests.Contains("DELETE /api/v2/libraries/unmatched-items/u11/ignore"), "Undo restores the item");
            await Until(() => Find<Button>(shell, "UnmatchedMatch_u11") is { IsEnabled: true });
            await InvokeAsync(Find<Button>(shell, "UnmatchedMatch_u11"));
            await Until(() => OpenDialog(window) is SiloPlayer.Controls.MatchItemDialog);
            Check(OpenDialog(window) is SiloPlayer.Controls.MatchItemDialog, "Match opens the existing item match tool");
            OpenDialog(window)?.Hide();
            await Until(() => OpenDialog(window) == null);

            await InvokeAsync(Find<Button>(shell, "LibrariesSection_Matcher"));
            await Until(() => Find<HyperlinkButton>(shell, "MatcherLibrary_1") != null);
            await InvokeAsync(Find<HyperlinkButton>(shell, "MatcherLibrary_1"));
            await Until(() => Find<FrameworkElement>(shell, "MatcherDetail") != null && Texts(shell).Any(text => text.Contains("Heat (1995)", StringComparison.Ordinal)));
            Check(Texts(shell).Any(text => text.Contains("Heat (1995)", StringComparison.Ordinal)), "the matcher detail shows rejected candidates");
            await InvokeAsync(Find<Button>(shell, "MatcherRetry_1"));
            await Until(() => server.Requests.Contains("POST /api/v2/libraries/1/metadata-match-queue/retry"));
            Check(server.Requests.Contains("POST /api/v2/libraries/1/metadata-match-queue/retry"), "Retry now asks the matcher to retry");

            await InvokeAsync(Find<Button>(shell, "LibrariesSection_Ambiguous"));
            await Until(() => Find<Button>(shell, "AmbiguousOverride_" + Uri.EscapeDataString("/media/movies/Root 1")) != null);
            Check(server.Requests.Any(line => line.StartsWith("GET /api/v2/libraries/roots?library_id=2&limit=50&state=ambiguous", StringComparison.Ordinal)), "ambiguous roots read the first library's first page on open");
            Check(Find<Button>(shell, "AmbiguousLoadMore") != null && Find<Button>(shell, "AmbiguousNext") != null, "more roots page locally and load on demand");
            await InvokeAsync(Find<Button>(shell, "AmbiguousOverride_" + Uri.EscapeDataString("/media/movies/Root 1")));
            await Until(() => DialogControl<Button>(window, "RootOverrideSave") != null);
            Check(DialogControl<Button>(window, "RootOverrideRemove") is { IsEnabled: true }, "an active override can be removed");
            await CaptureAsync(outer, "libraries-root-override-1440x900.png");
            await CapturePopupAsync(window, "libraries-root-override-1440x900-popup.png");
            await InvokeAsync(DialogControl<Button>(window, "RootOverrideSave"));
            await Until(() => server.Requests.Contains("PUT /api/v2/libraries/roots/override"));
            Check(server.Bodies.GetValueOrDefault("PUT /api/v2/libraries/roots/override")?.Contains("\"forced_type\":\"movie\"") == true, "Save sends the forced values");

            await InvokeAsync(Find<Button>(shell, "LibrariesSection_Skipped"));
            await Until(() => Find<Button>(shell, "SkippedSort_reason") != null);
            await InvokeAsync(Find<Button>(shell, "SkippedSort_reason"));
            Check(Find<Button>(shell, "SkippedSort_reason") is { } sort && AutomationProperties.GetItemStatus(sort) == "ascending", "a header click sorts ascending");
            await InvokeAsync(Find<Button>(shell, "SkippedRow_" + Uri.EscapeDataString("1:/media/movies/Extras")));
            await Until(() => Texts(shell).Contains("/media/movies/Extras/a.mkv"));
            Check(Texts(shell).Contains("/media/movies/Extras/a.mkv"), "a skipped root expands to its paths");

            await InvokeAsync(Find<Button>(shell, "LibrariesSection_Stale"));
            await Until(() => Find<Button>(shell, "StaleMatch_" + Uri.EscapeDataString("st1:tmdb")) != null);
            Check(Find<Button>(shell, "StaleMatch_" + Uri.EscapeDataString("st1:tmdb")) != null, "stale identifiers offer Match");
            await CaptureAsync(outer, "libraries-diagnostics-1440x900.png");

            // ── Viewports ────────────────────────────────────────────────
            foreach (var (width, height) in new[] { (1440, 600), (1920, 600) })
            {
                Resize(window, width, height);
                await Task.Delay(300);
                ((ScrollViewer)Field(page, "_scroll")!).ChangeView(null, 0, null, true);
                await Task.Delay(100);
                Check(Find<FrameworkElement>(shell, "LibrariesTable") != null, $"the table shows at {width}×{height}");
                await CaptureAsync(outer, $"libraries-{width}x{height}.png");
            }
            Resize(window, 800, 600);
            await Until(() => Find<FrameworkElement>(shell, "LibrariesCards") != null);
            Check(Find<FrameworkElement>(shell, "LibrariesCards") != null && Find<FrameworkElement>(shell, "LibrariesTable") == null, "below 1024 px the list becomes cards");
            ((ScrollViewer)Field(page, "_scroll")!).ChangeView(null, 0, null, true);
            await CaptureAsync(outer, "libraries-cards-800x600.png");
            await Until(() => controller.State.CanReorder && Find<Button>(shell, "LibrariesMenu_1") != null);
            await InvokeAsync(Find<Button>(shell, "LibrariesMenu_1"));
            await Until(() => FindInPopups<Button>(window, "LibrariesMenuUp_1") != null);
            await CaptureAsync(outer, "libraries-card-menu-800x600.png");
            await CapturePopupAsync(window, "libraries-card-menu-800x600-popup.png");
            var reorders = server.Requests.Count(line => line == "POST /api/v2/libraries/reorder");
            await InvokeAsync(FindInPopups<Button>(window, "LibrariesMenuUp_1"));
            await Until(() => server.Requests.Count(line => line == "POST /api/v2/libraries/reorder") == reorders + 1);
            Check(server.Bodies.GetValueOrDefault("POST /api/v2/libraries/reorder")?.StartsWith("{\"entries\":[{\"id\":\"1\",\"position\":0}", StringComparison.Ordinal) == true,
                "the card menu's Move up writes the new order");
            await Until(() => FindInPopups<Button>(window, "LibrariesMenuUp_1") is { IsEnabled: false });
            Check(FindInPopups<Button>(window, "LibrariesMenuUp_1") is { IsEnabled: false }, "the menu stays open and updates after a move");
            ClosePopups(window);
            await InvokeAsync(Find<Button>(shell, "LibrariesAdd"));
            await Until(() => Find<FrameworkElement>(shell, "LibraryEditor") != null);
            await CaptureAsync(outer, "libraries-editor-800x600.png");
            await InvokeAsync(Find<Button>(shell, "LibraryEditorCancel"));
            await Until(() => page.EditorSession == null);
            Check(page.EditorSession == null, "Cancel closes the editor");
            Resize(window, 1440, 900);
            await Until(() => Find<FrameworkElement>(shell, "LibrariesTable") != null);

            // ── Legacy Autoscan link ─────────────────────────────────────
            AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Libraries, new Dictionary<string, string> { ["tab"] = "autoscan", ["view"] = "activity" }));
            await Until(() => inner.Content is AdminLibrariesPage legacy && legacy != page && Texts(shell).Contains(AdminLibrariesText.T("admin.native.libraries.autoscanMoved")));
            Check(Texts(shell).Contains(AdminLibrariesText.T("admin.native.libraries.autoscanMoved")), "an old Autoscan tab link explains that Autoscan is not available yet");
            page = (AdminLibrariesPage)inner.Content;
            await Until(() => page.Controller?.State.Loaded == true);

            // ── Retirement ───────────────────────────────────────────────
            await Until(() => Find<Button>(shell, "LibrariesAdd") is { IsLoaded: true, IsEnabled: true });
            await InvokeAsync(Find<Button>(shell, "LibrariesAdd"));
            await Until(() => page.EditorSession != null);
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => page.Controller == null || page.Controller.State.Owner == null);
            Check(page.Controller == null || page.Controller.State is { Owner: null, Libraries.Count: 0 }, "authority retirement clears the page data");
            await Until(() => page.EditorSession == null && Find<FrameworkElement>(shell, "LibraryEditor") == null, 2000);
            Check(page.EditorSession == null && Find<FrameworkElement>(shell, "LibraryEditor") == null, "authority retirement closes the editor");
            realtime.Silence();
        }
        finally
        {
            window.Content = previousContent;
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Libraries fixture failed: {string.Join("; ", Failures)}");
        Program.Log("PASS: Admin Libraries native fixture completed; synthetic server only, no live server, file picker, phone or visual parity certification.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void Resize(Window window, int width, int height)
    {
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(200);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, $"{name} rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private static Window? _window;

    /// <summary>
    /// Captures the open dialog or flyout element (a page capture does not include the popup layer).
    /// When the host cannot rasterize that layer the run records a NOTE, not a pass.
    /// </summary>
    private static async Task CapturePopupAsync(Window window, string name)
    {
        if (window.Content?.XamlRoot is not { } root) return;
        FrameworkElement? target = null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child == null) continue;
            target = Descendants(popup.Child).OfType<ContentDialog>().FirstOrDefault() as FrameworkElement
                ?? Descendants(popup.Child).OfType<FlyoutPresenter>().FirstOrDefault() ?? target;
        }
        Check(target != null, name + " has an open dialog or flyout");
        if (target == null) return;
        try
        {
            target.UpdateLayout();
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(target);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) { Program.Log("NOTE: " + name + " not captured: the popup rendered no pixels"); return; }
            var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
            var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();
            Program.Log("PASS: " + name + " captured (" + bitmap.PixelWidth + "x" + bitmap.PixelHeight + ")");
        }
        catch (ArgumentException) { Program.Log("NOTE: " + name + " not captured: the popup layer cannot be rendered to a bitmap here"); }
    }

    private static async Task InvokeAsync(ButtonBase? button)
    {
        if (button == null) { Check(false, "expected button exists"); return; }
        var id = AutomationProperties.GetAutomationId(button);
        button.StartBringIntoView();
        await Task.Delay(100);
        // A re-render may have replaced the control after it was found; act on the current one.
        if (!button.IsLoaded && _window?.Content is { } content)
            button = Find<ButtonBase>(content, id) ?? FindInPopups<ButtonBase>(_window, id) ?? button;
        Check(button.IsLoaded && button.IsEnabled, $"{AutomationProperties.GetAutomationId(button)} is rendered and enabled before invocation");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
        if (peer == null) throw new InvalidOperationException("The rendered button has no invocation provider.");
        peer.Invoke();
        await Task.Delay(80);
    }

    private static ContentDialog? OpenDialog(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (popup.Child is { } child && Descendants(child).OfType<ContentDialog>().FirstOrDefault() is { } dialog) return dialog;
        return null;
    }

    private static T? DialogControl<T>(Window window, string automationId) where T : FrameworkElement => FindInPopups<T>(window, automationId);

    private static T? FindInPopups<T>(Window window, string automationId) where T : FrameworkElement
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
            if (popup.Child is { } child && Find<T>(child, automationId) is { } match) return match;
        return null;
    }

    private static void ClosePopups(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root).ToArray())
            if (popup.Child is FlyoutPresenter || (popup.Child != null && Descendants(popup.Child).OfType<FlyoutPresenter>().Any())) popup.IsOpen = false;
    }

    private static bool TrySendKey(nuint key)
    {
        var target = GetFocus();
        GetWindowThreadProcessId(target, out var process);
        if (target == 0 || process != (uint)Environment.ProcessId) return false;
        return PostMessage(target, 0x0100, key, 1) && PostMessage(target, 0x0101, key, unchecked((nint)0xC0000001u));
    }

    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint target, out uint process);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint target, uint message, nuint wParam, nint lParam);

    private static T? Find<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId);

    private static HashSet<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(text => text.Text).ToHashSet(StringComparer.Ordinal);

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
