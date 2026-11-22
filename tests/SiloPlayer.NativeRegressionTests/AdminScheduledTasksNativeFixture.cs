using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
using Windows.Storage.Streams;

/// <summary>
/// Production Configure › Scheduled Tasks registration, Admin shell and Frame entry using synthetic server
/// data and a synthetic tasks channel only. No real server, account or installed-player state is touched.
/// </summary>
internal static class AdminScheduledTasksNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var attempt = 0; attempt < milliseconds / 25 && !condition(); attempt++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Channel : IAdminRealtimeChannel
    {
        internal int Leases;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels)
        {
            Interlocked.Increment(ref Leases);
            return new Release(() => Interlocked.Decrement(ref Leases));
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        internal void Updated(string key, string state) =>
            EventReceived?.Invoke("tasks", "task.updated", JsonDocument.Parse($$"""{"key":"{{key}}","state":"{{state}}"}""").RootElement.Clone());
        internal void Snapshot() => SnapshotReceived?.Invoke("tasks", JsonDocument.Parse("[]").RootElement.Clone());
        private sealed class Release(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<(string? IfMatch, string Body)> Writes = new();
        internal readonly Dictionary<string, string> States = new(StringComparer.Ordinal) { ["cleanup_activity_log"] = "idle", ["scan_libraries"] = "idle" };
        internal int ScheduleRevision = 1;
        internal bool ConflictNext;
        internal bool FailListReads;
        internal TaskCompletionSource? CancelRelease;
        internal static readonly string Past = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
        internal static readonly string Future = DateTimeOffset.UtcNow.AddMinutes(150).ToString("O");

        internal string TaskJson(string key, string category, bool manual = false, string triggers = "[]", string extra = "") =>
            $$"""{"key":"{{key}}","name":"{{key}}","description":"Synthetic {{key}}","category":"{{category}}","state":"{{States.GetValueOrDefault(key, "idle")}}","progress":{{(States.GetValueOrDefault(key) == "running" ? "42.5" : "0")}},"manual_only":{{(manual ? "true" : "false")}},"triggers":{{triggers}}{{extra}},"execution_scope":"process"}""";

        private string Detail(string key) => key switch
        {
            "scan_libraries" => TaskJson(key, "library", triggers: """[{"type":"interval","interval_ms":10800000}]""", extra: $$""","next_run_at":"{{Future}}","last_execution":{"task_key":"scan_libraries","started_at":"{{Past}}","completed_at":"{{Past}}","status":"failed","duration_ms":61000,"error_message":"Task failed. Inspect administrator diagnostics for details."}"""),
            "refresh_metadata" => TaskJson(key, "metadata", triggers: """[{"type":"startup"}]""", extra: $$""","next_run_at":"{{Past}}" """),
            "cleanup_activity_log" => TaskJson(key, "system", triggers: """[{"type":"daily","time_of_day":"05:00"}]"""),
            "repair_provider_id_integrity" => TaskJson(key, "metadata", manual: true),
            "rebuild_catalog_search_index" => TaskJson(key, "system", triggers: """[{"type":"weekly","time_of_day":"03:00","day_of_week":0}]"""),
            _ => "",
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(request.Method + " " + request.RequestUri.PathAndQuery);
            const string prefix = "/api/v2/admin/tasks";
            if (path == prefix && FailListReads) return Json("{}", HttpStatusCode.ServiceUnavailable);
            if (path == prefix)
                return Json("{\"items\":[" + string.Join(",", new[] { "scan_libraries", "refresh_metadata", "cleanup_activity_log", "repair_provider_id_integrity" }.Select(Detail)) + "]}");
            if (!path.StartsWith(prefix + "/", StringComparison.Ordinal)) return Json(path switch
            {
                "/api/v2/admin/server/status" => """{"restart_required":false,"restart_mark_count":0,"restart_requested":false}""",
                "/api/v2/admin/system/build" => """{"display":"synthetic","available":true,"build_number":1}""",
                _ => "{}",
            });
            var rest = path[(prefix.Length + 1)..].Split('/');
            var key = Uri.UnescapeDataString(rest[0]);
            var detail = Detail(key);
            if (detail.Length == 0) return Json("""{"type":"https://siloserver.org/docs/api/v2/problems/not_found","title":"Task not found","status":404}""", HttpStatusCode.NotFound);
            var operation = rest.Length > 1 ? rest[1] : "";
            switch (request.Method.Method + " " + operation)
            {
                case "GET ": return Json(detail);
                case "POST run": States[key] = "running"; return Json(Detail(key));
                case "POST cancel":
                    States[key] = "cancelling";
                    if (CancelRelease is { } release) await release.Task.WaitAsync(cancellationToken);
                    return Json(Detail(key));
                case "GET metrics":
                    return Json(MetricsJson);
                case "GET history":
                    return Json(key == "refresh_metadata" ? RefreshHistory(key) : $$$"""{"items":[{"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"completed","duration_ms":1500,"id":"7"}],"page":{"has_more":false}}""");
                case "GET triggers" when key == "rebuild_catalog_search_index":
                    return Tagged($$"""{"task_key":"{{key}}","triggers":[{"type":"weekly","time_of_day":"03:00","day_of_week":0,"max_runtime_ms":5400000},{"type":"daily","time_of_day":"05:30"},{"type":"interval","interval_ms":3600000}]}""", "\"weekly-1\"");
                case "GET triggers":
                    return Tagged($$"""{"task_key":"{{key}}","triggers":[{"type":"interval","interval_ms":{{(ScheduleRevision == 1 ? 1000 : 60000)}}}]}""", "\"schedule-" + ScheduleRevision + "\"");
                case "PUT triggers":
                    Writes.Enqueue((request.Headers.IfMatch.SingleOrDefault()?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
                    if (ConflictNext)
                    {
                        ConflictNext = false; ScheduleRevision++;
                        return Json("""{"type":"https://siloserver.org/docs/api/v2/problems/precondition_failed","title":"Precondition failed","status":412}""", HttpStatusCode.PreconditionFailed);
                    }
                    ScheduleRevision++;
                    return Tagged($$"""{"task_key":"{{key}}","triggers":[{"type":"interval","interval_ms":3000}]}""", "\"schedule-" + ScheduleRevision + "\"");
            }
            return Json("{}", HttpStatusCode.NotFound);
        }

        // The refresh task's queue metrics: titles the way the server now sends them (the target's own title plus optional
        // series, season and episode parts), item types as English data (known, unknown and empty) and the fixed failure sentence.
        internal const string MetricsJson = """
            {"total":12,"due":3,"leased":1,"reason_counts":[{"reason":"refresh_failure","count":2},{"reason":"provider_artwork","count":0}],
             "attempt_buckets":[{"label":"1-2","count":5}],
             "due_samples":[
              {"target_type":"item","content_id":"101","title":"Synthetic Movie","type":"movie","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":1,"last_error":""},
              {"target_type":"item","content_id":"102","title":"Season 2","type":"season","series_title":"Synthetic Show","season_number":2,"reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
              {"target_type":"item","content_id":"103","title":"Pilot","type":"episode","series_title":"Synthetic Show","season_number":1,"episode_number":2,"reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":3,"last_error":""},
              {"target_type":"item","content_id":"104","title":"","type":"podcast","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""},
              {"target_type":"item","content_id":"105","title":"A synthetic title that is long enough to need wrapping inside its table cell at a narrow width without widening the page","type":"","reason_mask":1,"next_refresh_at":"2026-11-14T11:00:00Z","attempt_count":0,"last_error":""}],
             "recent_errors":[
              {"target_type":"item","content_id":"201","title":"Specials","type":"season","series_title":"Synthetic Show","season_number":0,"reason_mask":8,"next_refresh_at":"2026-11-14T11:00:00Z","last_attempt_at":"2026-11-14T10:00:00Z","attempt_count":2,"last_error":"Refresh failed. Inspect administrator diagnostics for details."},
              {"target_type":"item","content_id":"202","title":"Finale","type":"episode","season_number":3,"episode_number":10,"reason_mask":8,"next_refresh_at":"2026-11-14T11:00:00Z","last_attempt_at":"2026-11-14T10:00:00Z","attempt_count":1,"last_error":"Refresh failed. Inspect administrator diagnostics for details."},
              {"target_type":"item","content_id":"203","title":"A synthetic title that is long enough to run past two lines in a narrow panel so that it must be clamped to two lines and never widen the panel around it","type":"ebook","reason_mask":8,"next_refresh_at":"2026-11-14T11:00:00Z","last_attempt_at":"2026-11-14T10:00:00Z","attempt_count":4,"last_error":"Refresh failed. Inspect administrator diagnostics for details."}]}
            """;

        private string RefreshHistory(string key) => $$$"""
            {"items":[
              {"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"failed","duration_ms":2500,"id":"8","error_message":"raw server cause that the client must not show",
               "steps":[{"key":"cleanup_activity_log","name":"English step name","status":"failed"},{"key":"plugin:7:feed","name":"Plugin step","status":"failed"},{"key":"scan_libraries","name":"Done","status":"completed"}]},
              {"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"failed","duration_ms":1200,"id":"9","error_message":"another raw server cause"},
              {"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"cancelled","duration_ms":800,"id":"10","error_message":"Cancelled by an administrator"},
              {"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"completed","duration_ms":1500,"id":"7"}],
             "page":{"has_more":false}}
            """;

        internal int Count(string prefix) => Requests.Count(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Tagged(string body, string etag)
        {
            var response = Json(body);
            response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
            return response;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        var channel = new Channel();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var themeService = new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client));
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(themeService)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton<IAdminRealtimeChannel>(channel)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        themeService.ApplyTheme("midnight-cinema");
        Frame? outer = null;
        AdminShellPage? shownShell = null;
        AdminScheduledTasksPage? shownPage = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/tasks");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Scheduled Tasks are not available through the production Configure registration.");
            await Until(() => outer.Content is AdminShellPage, "shell");
            var shell = (AdminShellPage)outer.Content;
            shownShell = shell;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminScheduledTasksPage { ListController.State.Loaded: true }, "list page");
            var page = (AdminScheduledTasksPage)inner.Content;
            shownPage = page;
            await Until(() => page.ListController!.State.Metrics != null, "inline metrics");
            await Task.Delay(150);
            Check(true, "Scheduled Tasks activate through production registration, shell and Frame");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "task navigation keeps page transitions suppressed");
            Check(!Descendants(outer).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal) || text.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered task text and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(text => !IsInside(text, page)).Select(text => text.Text).ToArray();
            Check(shellText.Contains("Server") && shellText.Contains("Scheduled Tasks"), "sidebar shows the Server topic with its Scheduled Tasks leaf");
            Check(AdminNavigation.SearchGroups("scheduler", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(group => group.Pages).Any(entry => entry.Route == AdminRoute.ScheduledTasks),
                "registered Scheduled Tasks are searchable through their official keywords");
            Check(Texts(page).Contains("Scheduled Tasks") && Texts(page).Any(text => text.StartsWith("View and manage background tasks.", StringComparison.Ordinal)), "header shows the official title and subtitle");
            var headings = Descendants(page).OfType<TextBlock>().Where(text => AutomationProperties.GetHeadingLevel(text) == AutomationHeadingLevel.Level2).Select(text => text.Text).ToArray();
            Check(headings.SequenceEqual(["LIBRARY", "METADATA", "SYSTEM", "ON DEMAND"]), "groups follow library, metadata, system, then On demand");
            Check(Texts(page).Contains("These never run on a schedule. Use them for repairs and one-off maintenance."), "On demand carries its official description");
            Check(Texts(page).Contains("Queue Media Library Scans") && Texts(page).Contains("Refresh Metadata") && Texts(page).Contains("Cleanup Activity Log") && Texts(page).Contains("Repair Provider ID Integrity"),
                "Silo task names come from the official catalog");
            Check(Texts(page).Contains("Every 3h") && Texts(page).Contains("· Next: in 2h") && Texts(page).Contains("Daily at 05:00") && Texts(page).Contains("Manual only"),
                "row summaries show short triggers, next run and manual-only state");
            Check(Texts(page).Contains("· Never run"), "a task with no schedule and no run says it never ran");
            Check(Descendants(page).OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == "TaskOverdue_refresh_metadata"), "an overdue next run is flagged");
            Check(Texts(page).Any(text => text.StartsWith("· Last run:", StringComparison.Ordinal) && text.Contains("Duration: 1m 1s", StringComparison.Ordinal)), "last run shows relative time and duration");
            Check(Texts(page).Contains("failed"), "the last failed run shows its status badge");
            Check(Texts(page).Contains("Refresh Backlog") && Texts(page).Contains("Refresh failure: 2") && !Texts(page).Contains("Provider artwork: 0"),
                "refresh_metadata shows inline queue metrics with only non-zero reasons");
            const string FailedDetails = "Task failed. Inspect administrator diagnostics for details.";
            var listBadge = Descendants(page).OfType<Border>().First(border => AutomationProperties.GetName(border) == "failed" && IsLaidOut(border));
            Check(ToolTipService.GetToolTip(listBadge) as string == FailedDetails, "a failed run's badge explains itself with the fixed catalog sentence");
            CheckDestructiveBadge(listBadge, "a failed run's badge on the list");
            var rowViews = (System.Collections.IDictionary)Field(page, "_rows")!;
            var scanRow = rowViews["scan_libraries"]!;
            const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
            bool Underlined() => (bool)scanRow.GetType().GetProperty("NameUnderlined", Internal)!.GetValue(scanRow)!;
            Check(!Underlined(), "a task link is not underlined at rest");
            // WinUI cannot raise a pointer event from code, so the real pointer is not driven. The page records which element its pointer
            // handlers were attached to and with what callback; this finds that callback through the production wiring and calls it.
            var nameLink = (HyperlinkButton)scanRow.GetType().GetField("Name", Internal)!.GetValue(scanRow)!;
            var nameText = (TextBlock)scanRow.GetType().GetField("NameText", Internal)!.GetValue(scanRow)!;
            Action<bool>? HoverOf(UIElement element) => (Action<bool>?)typeof(AdminScheduledTasksPage).GetMethod("HoverSetter", BindingFlags.Static | Internal | BindingFlags.Public)!.Invoke(null, [element]);
            var rowHover = HoverOf(nameLink);
            Check(rowHover != null && HoverOf(nameText) == null && HoverOf(Button(page, "TaskAction_scan_libraries")) == null, "the pointer handlers of a task link are attached to the link and to no other element of the row");
            rowHover?.Invoke(true);
            Check(Underlined(), "a task link underlines while the pointer is over it");
            rowHover?.Invoke(false);
            Check(!Underlined(), "a task link stops underlining when the pointer leaves");
            CheckInsideCorners(nameLink, nameLink.Padding, "a task link");
            await CaptureWidthsAsync(window, page, "list", null);

            var listReads = server.Requests.Count(entry => entry == "GET /api/v2/admin/tasks");
            await InvokeAsync(Button(page, "TaskAction_cleanup_activity_log"));
            await Until(() => server.Count("POST /api/v2/admin/tasks/cleanup_activity_log/run") == 1, "run sent");
            await Until(() => AutomationProperties.GetName(Button(page, "TaskAction_cleanup_activity_log")) is "Starting..." or "Stop", "starting feedback");
            channel.Updated("cleanup_activity_log", "running");
            await Until(() => server.Requests.Count(entry => entry == "GET /api/v2/admin/tasks") > listReads, "event invalidated the list");
            await Until(() => AutomationProperties.GetName(Button(page, "TaskAction_cleanup_activity_log")) == "Stop" && Button(page, "TaskAction_cleanup_activity_log").IsEnabled, "stop shown");
            Check(Descendants(page).OfType<FrameworkElement>().Any(element => AutomationProperties.GetAutomationId(element) == "TaskProgress_cleanup_activity_log" && element.Visibility == Visibility.Visible),
                "a running task shows its progress bar");
            Check(server.Count("POST /api/v2/admin/tasks/cleanup_activity_log/run") == 1, "Run Now is sent exactly once");
            server.CancelRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await InvokeAsync(Button(page, "TaskAction_cleanup_activity_log"));
            await Until(() => server.Count("POST /api/v2/admin/tasks/cleanup_activity_log/cancel") == 1, "cancel sent");
            channel.Updated("cleanup_activity_log", "cancelling");
            await Until(() => AutomationProperties.GetName(Button(page, "TaskAction_cleanup_activity_log")) == "Stopping..." && !Button(page, "TaskAction_cleanup_activity_log").IsEnabled, "stopping shown");
            Check(true, "Stop requests cancellation and shows Stopping...");
            server.States["cleanup_activity_log"] = "idle";
            channel.Updated("cleanup_activity_log", "idle");
            await Until(() => AutomationProperties.GetName(Button(page, "TaskAction_cleanup_activity_log")) == "Run Now", "idle again");
            Check(page.ListController!.State.Stopping.Contains("cleanup_activity_log") && !Button(page, "TaskAction_cleanup_activity_log").IsEnabled,
                "an idle realtime refresh cannot enable Run Now while Stop is in flight");
            await page.ListController.RunAsync("cleanup_activity_log", page.ListController.State.Owner);
            Check(server.Count("POST /api/v2/admin/tasks/cleanup_activity_log/run") == 1, "the list controller rejects Run during a held Stop");
            server.CancelRelease.TrySetResult();
            server.CancelRelease = null;
            await Until(() => Button(page, "TaskAction_cleanup_activity_log").IsEnabled, "cancellation settled");

            server.FailListReads = true;
            channel.Updated("cleanup_activity_log", "idle");
            await Until(() => page.ListController!.State.Failure != null && Button(page, "TasksRetry").IsEnabled && IsLaidOut(Button(page, "TasksRetry")), "loaded-list Retry");
            Check(page.ListController!.State.Loaded, "a refresh failure retains the loaded list and exposes Retry");
            server.FailListReads = false;
            await InvokeAsync(Button(page, "TasksRetry"));
            await Until(() => page.ListController!.State.Failure == null && !IsLaidOut(Button(page, "TasksRetry")), "list recovered");
            Check(server.Count("POST /api/v2/admin/tasks/cleanup_activity_log/cancel") == 1, "Retry refreshes the list without replaying Stop");

            page.SetHostActivity(visible: true, focused: false);
            Check(!page.ListController!.IsTicking, "an inactive window stops the 30 s tick");
            page.SetHostActivity(visible: true, focused: true);
            Check(page.ListController!.IsTicking, "activation restarts the tick");

            Invoke(Link(page, "TaskLink_scan_libraries"));
            await Until(() => inner.Content is AdminScheduledTasksPage { DetailController.State.Task: not null } detailPage && !ReferenceEquals(detailPage, page), "detail page");
            var detail = (AdminScheduledTasksPage)inner.Content;
            shownPage = detail;
            await Until(() => detail.DetailController!.State.HistoryLoaded, "history");
            await Task.Delay(150);
            Check(Text(detail, "TaskTitle").Text == "Queue Media Library Scans" && Texts(detail).Contains("Scheduled Tasks") && Texts(detail).Contains("Admin"),
                "detail shows the breadcrumb and catalog title");
            Check(Texts(detail).Contains("Library") && !Texts(detail).Contains("library"), "detail shows the translated category label, not the key the server sends");
            Check(AutomationProperties.GetName(Descendants(detail).OfType<Border>().First(border => AutomationProperties.GetAutomationId(border) == "TaskCategory")) == "Library", "the category badge's accessible name is the translated label");
            Check(Texts(detail).Contains("Every 3 hour(s)"), "detail shows long trigger text");
            Check(Texts(detail).Any(text => text.StartsWith("This task history records how long it took to queue per-library scan runs.", StringComparison.Ordinal)) &&
                !Descendants(detail).OfType<TextBlock>().Any(text => text.Inlines.OfType<Microsoft.UI.Xaml.Documents.Hyperlink>().Any()),
                "the scan_libraries note has no Libraries link while Libraries is unregistered");
            Check(Texts(detail).Contains("Execution history") && Texts(detail).Contains("1s") && Texts(detail).Contains("completed"), "history shows its first page");
            Check(!Descendants(detail).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "TaskHistoryLoadOlder" && IsLaidOut(button)), "Load older is hidden without more history");

            // The schedule buttons hold a wrapping text block instead of a string: their accessible names are set on the buttons themselves.
            var scheduleButtons = new (string Id, string Name)[]
            {
                ("TaskEditSchedule", "Edit Schedule"), ("TaskAddTrigger", "Add Trigger"), ("TaskReviewSchedule", "Review current schedule"),
                ("TaskUseRevision", "Use this revision and keep my draft"),
            };
            foreach (var (id, expectedName) in scheduleButtons)
            {
                var scheduleButton = Button(detail, id);
                Check(scheduleButton.Content is TextBlock && AutomationProperties.GetName(scheduleButton) == expectedName, $"{id} keeps its accessible name \"{expectedName}\" with a wrapping label");
                Check(scheduleButton.Padding.Left >= scheduleButton.CornerRadius.TopLeft && scheduleButton.Padding.Right >= scheduleButton.CornerRadius.TopRight,
                    $"{id} keeps its label out of its rounded corners (side padding {scheduleButton.Padding.Left:0} of radius {scheduleButton.CornerRadius.TopLeft:0})");
            }
            // Edit Schedule sits in a column that can be narrower than its label: measured in 170 px the label wraps onto a second line.
            var editButton = Button(detail, "TaskEditSchedule");
            var scheduleHeader = (Grid)VisualTreeHelper.GetParent(editButton);
            scheduleHeader.Measure(new Windows.Foundation.Size(170, double.PositiveInfinity));
            Check(editButton.DesiredSize.Height > 40, $"the Edit Schedule label wraps when its column is narrow (button {editButton.DesiredSize.Width:0} x {editButton.DesiredSize.Height:0} in 170 px)");
            scheduleHeader.InvalidateMeasure();
            detail.UpdateLayout();
            CheckInsideCorners(Crumb(detail, "TaskCrumbTasks"), Crumb(detail, "TaskCrumbTasks").Padding, "a breadcrumb link");

            // The Libraries link of the scan_libraries note: Libraries is unregistered here, so the page shows no link; build the linked form
            // the page builds when it is registered, show it in the page and test it.
            var buildNote = typeof(AdminScheduledTasksPage).GetMethod("BuildScanNote", BindingFlags.Static | BindingFlags.NonPublic)!;
            var noteHost = (Border)Field(detail, "_scanNote")!;
            var originalNote = noteHost.Child;
            var linkedNote = (TextBlock)buildNote.Invoke(null, [true])!;
            var noteLink = linkedNote.Inlines.OfType<Hyperlink>().Single();
            var noteRun = noteLink.Inlines.OfType<Run>().Single();
            var noteHover = (Action<bool>?)typeof(AdminScheduledTasksPage).GetMethod("HoverSetter", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, [linkedNote]);
            Check(noteHover != null && noteLink.UnderlineStyle == UnderlineStyle.None, "the Libraries link of the note is attached to pointer handlers on its text and is not underlined at rest");
            noteHover?.Invoke(true);
            Check(noteLink.UnderlineStyle == UnderlineStyle.Single, "the Libraries link underlines while the pointer is over it");
            noteHover?.Invoke(false);
            Check(noteLink.UnderlineStyle == UnderlineStyle.None, "the Libraries link stops underlining when the pointer leaves");
            var plainNote = (TextBlock)buildNote.Invoke(null, [false])!;
            Check(!plainNote.Inlines.OfType<Hyperlink>().Any() && (Action<bool>?)typeof(AdminScheduledTasksPage).GetMethod("HoverSetter", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, [plainNote]) == null,
                "without a registered Libraries page the note has no link and no pointer handlers");
            noteHost.Child = linkedNote;
            try
            {
                detail.UpdateLayout();
                await Task.Delay(150);
                var isOverRun = typeof(AdminScheduledTasksPage).GetMethod("IsOverRun", BindingFlags.Static | BindingFlags.NonPublic)!;
                var runStart = noteRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var runEnd = noteRun.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Windows.Foundation.Point Inside() => new(runStart.Left + 3, runStart.Top + runStart.Height / 2);
                Check(runStart.Width >= 0 && (bool)isOverRun.Invoke(null, [noteRun, Inside()])!, "a point on the Libraries link text hits it");
                Check(!(bool)isOverRun.Invoke(null, [noteRun, new Windows.Foundation.Point(runStart.Left + 3, runStart.Top - 6)])!, "a point above the line does not");
                Check(!(bool)isOverRun.Invoke(null, [noteRun, new Windows.Foundation.Point(runEnd.Right + 40, runEnd.Top + runEnd.Height / 2)])!, "a point beyond the end of the link text does not");
            }
            finally { noteHost.Child = originalNote; detail.UpdateLayout(); }

            await InvokeAsync(Button(detail, "TaskEditSchedule"));
            await Until(() => detail.DetailController!.State.Editing, "editing");
            await Task.Delay(100);
            var interval = Box(detail, "TriggerInterval_0");
            Check(interval.Text == "1" && Combo(detail, "TriggerUnit_0").SelectedIndex == 0, "the captured interval shows in seconds");
            await TypeWithQueuedRenderAsync(detail, interval, "3", () => detail.DetailController!.State.Draft!.Rows[0].IntervalText, "interval");
            var runtime = Box(detail, "TriggerRuntime_0");
            await TypeWithQueuedRenderAsync(detail, runtime, "2", () => detail.DetailController!.State.Draft!.Rows[0].RuntimeText, "runtime");
            runtime.Text = "";
            await Until(() => detail.DetailController!.State.Dirty, "dirty");
            server.ConflictNext = true;
            await InvokeAsync(Button(detail, "TaskSaveSchedule"));
            await Until(() => detail.DetailController!.State.NeedsReview, "conflict");
            await Task.Delay(100);
            Check(Text(detail, "TaskScheduleError").Text == "The schedule changed. Your draft is kept. Review the current schedule before submitting a new edit." &&
                !Button(detail, "TaskSaveSchedule").IsEnabled && interval.Text == "3", "a 412 keeps the draft, explains itself and blocks Save");
            Check(server.Writes.Single().IfMatch == "\"schedule-1\"", "the save used the validator captured when editing began");
            await InvokeAsync(Button(detail, "TaskReviewSchedule"));
            await Until(() => detail.DetailController!.State.Candidate != null, "candidate");
            await Task.Delay(100);
            Check(Texts(detail).Contains("Current saved schedule:") && Texts(detail).Contains("Every 1 minute"), "review shows the current saved schedule");
            await CaptureWidthsAsync(window, detail, "schedule-review", "TaskScheduleEditor");
            await InvokeAsync(Button(detail, "TaskUseRevision"));
            await Until(() => !detail.DetailController!.State.NeedsReview, "adopted");
            Check(server.Writes.Count == 1 && interval.Text == "3", "adoption keeps the draft and sends nothing");
            await InvokeAsync(Button(detail, "TaskSaveSchedule"));
            await Until(() => !detail.DetailController!.State.Editing, "saved");
            Check(server.Writes.Count == 2 && server.Writes.Last().IfMatch == "\"schedule-2\"" && server.Writes.Last().Body == """{"triggers":[{"type":"interval","interval_ms":3000}]}""",
                "the second save is guarded by the adopted revision");

            await InvokeAsync(Button(detail, "TaskEditSchedule"));
            await Until(() => detail.DetailController!.State.Editing, "editing again");
            await InvokeAsync(Button(detail, "TaskAddTrigger"));
            await Until(() => detail.DetailController!.State.Dirty, "added trigger");
            await Task.Delay(100);
            Check(Box(detail, "TriggerInterval_1").Text == "1" && Combo(detail, "TriggerUnit_1").SelectedIndex == 2, "Add Trigger defaults to an hourly interval");
            Check(shell.RequiresLeaveConfirmation, "a dirty schedule draft requires leave confirmation");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(detail, "_dialog") is ContentDialog { IsLoaded: true }, "discard dialog");
            var dialog = (ContentDialog)Field(detail, "_dialog")!;
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            await InvokeAsync(DialogButton(dialog, "Cancel"));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);
            Check(ReferenceEquals(inner.Content, detail) && detail.DetailController!.State.Dirty, "Cancel keeps the page and its draft");
            Invoke(Crumb(detail, "TaskCrumbTasks"));
            await Until(() => Field(detail, "_dialog") is ContentDialog { IsLoaded: true }, "second discard dialog");
            dialog = (ContentDialog)Field(detail, "_dialog")!;
            closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            await InvokeAsync(DialogButton(dialog, "Discard"));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Until(() => inner.Content is AdminScheduledTasksPage { ListController: not null }, "back to list");
            Check(detail.DetailController == null, "leaving retires the detail controller and its draft");
            shownPage = (AdminScheduledTasksPage)inner.Content;

            AdminShellPage.TryOpenHref("/admin/tasks/rebuild_catalog_search_index");
            await Until(() => inner.Content is AdminScheduledTasksPage { DetailController.State.Task: not null }, "hidden detail");
            var hidden = (AdminScheduledTasksPage)inner.Content;
            shownPage = hidden;
            await Task.Delay(150);
            Check(Text(hidden, "TaskTitle").Text == "Rebuild Catalog Search Index" && Button(hidden, "TaskEditSchedule").Visibility == Visibility.Visible &&
                Texts(hidden).Contains("Sunday at 03:00"), "a hidden search task opens by key with an editable weekly schedule");
            await InvokeAsync(Button(hidden, "TaskEditSchedule"));
            await Until(() => hidden.DetailController!.State.Editing, "weekly editing");
            await Task.Delay(200);
            Check(Descendants(hidden).OfType<TimePicker>().Count(picker => IsLaidOut(picker)) == 2 && Descendants(hidden).OfType<ComboBox>().Count(combo => IsLaidOut(combo)) >= 4,
                "the weekly, daily and interval triggers each show their own fields");
            await CaptureWidthsAsync(window, hidden, "schedule-edit", "TaskScheduleEditor");
            await InvokeAsync(Button(hidden, "TaskCancelSchedule"));
            await Until(() => !hidden.DetailController!.State.Editing, "weekly editing closed");

            AdminShellPage.TryOpenHref("/admin/tasks/refresh_metadata");
            await Until(() => inner.Content is AdminScheduledTasksPage { DetailController.State: { Task: not null, Metrics: not null, HistoryLoaded: true } } refreshPage && !ReferenceEquals(refreshPage, hidden), "refresh task page");
            var refresh = (AdminScheduledTasksPage)inner.Content;
            shownPage = refresh;
            await Task.Delay(300);
            var refreshTexts = Texts(refresh);
            Check(refreshTexts.Contains("Metadata") && !refreshTexts.Contains("metadata"), "the refresh task's category badge shows the translated label");
            Check(refreshTexts.Contains("Synthetic Movie") && refreshTexts.Contains("Synthetic Show - Season 2") && refreshTexts.Contains("Synthetic Show - S01E02 Pilot") && refreshTexts.Contains("104"),
                "due samples are named from their series, season and episode, an untitled one by its content ID");
            Check(refreshTexts.Contains("Synthetic Show - Specials") && refreshTexts.Contains("S03E10 Finale"), "recent errors are named the same way");
            Check(refreshTexts.Contains("Movie") && refreshTexts.Contains("Season") && refreshTexts.Contains("Episode") && refreshTexts.Contains("podcast") && refreshTexts.Contains("item"),
                "due sample item types are translated, an unknown type is shown as sent and an empty type is item");
            Check(refreshTexts.Contains("Season · attempts 2") && refreshTexts.Contains("Episode · attempts 1") && refreshTexts.Contains("Ebook · attempts 4"), "recent error item types are translated");
            Check(!refreshTexts.Contains("movie") && !refreshTexts.Contains("season") && !refreshTexts.Contains("episode") && !refreshTexts.Contains("ebook"), "no raw item type key is shown");
            Check(refreshTexts.Count(text => text == "Refresh failed. Inspect administrator diagnostics for details.") == 3, "each recent error shows the fixed failure sentence");
            var longError = Descendants(refresh).OfType<TextBlock>().First(text => text.Text.StartsWith("A synthetic title that is long enough to run past two lines", StringComparison.Ordinal) && IsLaidOut(text));
            Check(longError.TextWrapping == TextWrapping.Wrap && longError.MaxLines == 2 && longError.ActualHeight > 20, "a long recent-error title wraps and clamps to two lines");
            var longSample = Descendants(refresh).OfType<TextBlock>().First(text => text.Text.StartsWith("A synthetic title that is long enough to need wrapping", StringComparison.Ordinal) && IsLaidOut(text));
            Check(longSample.TextWrapping == TextWrapping.Wrap && longSample.MaxLines == 0, "a long due-sample title wraps inside its cell");
            Check(refreshTexts.Contains("Failed steps: Cleanup Activity Log and Plugin step"), "failed step names go through the task catalog by step key");
            Check(!refreshTexts.Any(text => text.Contains("raw server cause", StringComparison.Ordinal) || text.Contains("English step name", StringComparison.Ordinal)), "a failed run never shows the server's raw cause or an English step name that has a catalog entry");
            Check(refreshTexts.Count(text => text == FailedDetails) == 1, "a failed run without failed steps shows the fixed sentence in its history cell");
            Check(refreshTexts.Contains("Cancelled by an administrator"), "a cancelled run keeps the server's message");
            var failedBadges = Descendants(refresh).OfType<Border>().Where(border => AutomationProperties.GetName(border) == "failed" && IsLaidOut(border)).ToArray();
            Check(failedBadges.Length == 2 && failedBadges.All(badge => ToolTipService.GetToolTip(badge) as string == FailedDetails), "failed history badges explain with the fixed sentence");
            foreach (var badge in failedBadges) CheckDestructiveBadge(badge, "a failed run's badge in the history");
            var cancelledBadge = Descendants(refresh).OfType<Border>().First(border => AutomationProperties.GetName(border) == "cancelled" && IsLaidOut(border));
            Check(ToolTipService.GetToolTip(cancelledBadge) as string == "Cancelled by an administrator", "a cancelled run's badge shows the server's trimmed message");
            // The history grid (three columns that fit their content and one of at least 120) must fit the panel it is given, also between 640 and 720.
            var historyPanel = Descendants(refresh).OfType<FrameworkElement>().First(element => AutomationProperties.GetAutomationId(element) == "TaskHistoryTable" && IsLaidOut(element));
            foreach (var panelWidth in new double[] { 640, 680, 720 })
            {
                historyPanel.Measure(new Windows.Foundation.Size(panelWidth, double.PositiveInfinity));
                Check(historyPanel.DesiredSize.Width <= panelWidth, $"the history table fits a panel {panelWidth:0} px wide (needs {historyPanel.DesiredSize.Width:0})");
            }
            historyPanel.InvalidateMeasure();
            refresh.UpdateLayout();
            await CaptureWidthsAsync(window, refresh, "detail", null, [1440, 1024, 800, 640, 560]);

            AdminShellPage.TryOpenHref("/admin/tasks/repair_provider_id_integrity");
            await Until(() => inner.Content is AdminScheduledTasksPage { DetailController.State.Task.ManualOnly: true }, "manual detail");
            var manual = (AdminScheduledTasksPage)inner.Content;
            shownPage = manual;
            await Task.Delay(150);
            Check(Button(manual, "TaskEditSchedule").Visibility == Visibility.Collapsed && Texts(manual).Contains("Manual only. Select Run Now to start this task."),
                "a manual-only task shows its sentence and no editor");

            await InvokeAsync(Button(manual, "TaskCommand"));
            // The controller state flips before the queued render re-labels the button; a stale enabled "Run Now" would send a second run.
            await Until(() => manual.DetailController!.State.Task!.IsActive && AutomationProperties.GetName(Button(manual, "TaskCommand")) == "Cancel" && Button(manual, "TaskCommand").IsEnabled, "manual task running");
            server.CancelRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await InvokeAsync(Button(manual, "TaskCommand"));
            await Until(() => manual.DetailController!.State.CancelPending && server.States.GetValueOrDefault("repair_provider_id_integrity") == "cancelling", "detail cancellation held");
            server.States["repair_provider_id_integrity"] = "idle";
            channel.Updated("repair_provider_id_integrity", "idle");
            await Until(() => manual.DetailController!.State.Task!.State == "idle" && AutomationProperties.GetName(Button(manual, "TaskCommand")) == "Run Now", "detail idle refresh");
            Check(!Button(manual, "TaskCommand").IsEnabled, "detail Run Now stays disabled during a held Cancel");
            await manual.DetailController!.RunAsync(manual.DetailController.State.Owner);
            Check(server.Count("POST /api/v2/admin/tasks/repair_provider_id_integrity/run") == 1, "the detail controller rejects Run during a held Cancel");
            server.CancelRelease.TrySetResult();
            server.CancelRelease = null;
            await Until(() => Button(manual, "TaskCommand").IsEnabled, "detail cancellation settled");

            AdminShellPage.TryOpenHref("/admin/tasks/gone_task");
            await Until(() => inner.Content is AdminScheduledTasksPage { DetailController.State.NotFound: true }, "not found");
            var missing = (AdminScheduledTasksPage)inner.Content;
            shownPage = missing;
            await Task.Delay(150);
            Check(Text(missing, "TaskUnavailableTitle").Text == "Task not found" && Button(missing, "TaskAllTasks").Visibility == Visibility.Visible &&
                Button(missing, "TaskRetry").Visibility == Visibility.Collapsed, "an unknown key shows Task not found with All tasks");
            await InvokeAsync(Button(missing, "TaskAllTasks"));
            await Until(() => inner.Content is AdminScheduledTasksPage { ListController.State.Loaded: true }, "all tasks");
            var list = (AdminScheduledTasksPage)inner.Content;
            shownPage = list;
            Check(true, "All tasks returns to the list");

            Check(channel.Leases >= 1, "the page leases the tasks channel");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => list.ListController?.State.Tasks.Count is null or 0, "retired");
            await Task.Delay(150);
            Check(!Texts(list).Contains("Queue Media Library Scans"), "authority retirement clears every task row");
            string[] requests = server.Requests.ToArray();
            Check(requests.All(entry => entry.Contains("/api/v2/admin/", StringComparison.Ordinal)), "only admin endpoints are used");
            Check(requests.Count(entry => entry.StartsWith("PUT ", StringComparison.Ordinal)) == 2, "schedule writes are never replayed");
        }
        finally
        {
            await CloseDialogAsync(shownPage);
            var unloaded = new List<Task>();
            if (shownPage is { IsLoaded: true }) unloaded.Add(UnloadedAsync(shownPage));
            if (shownShell is { IsLoaded: true }) unloaded.Add(UnloadedAsync(shownShell));
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            try { await Task.WhenAll(unloaded).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { Check(false, "page and shell unloaded before the service provider was restored"); }
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Scheduled Tasks fixture failures: " + string.Join("; ", Failures));
    }

    private static async Task TypeWithQueuedRenderAsync(AdminScheduledTasksPage page, TextBox box, string text, Func<string> draftText, string label)
    {
        // Queue a controller render before input, then inspect the draft at the synchronous input boundary,
        // before TextChanged can run. Drain the queued renders to check that the edit also remains visible.
        page.DetailController!.UpdateDraft(draft => draft);
        var capturedSynchronously = false;
        void OnChanging(TextBox sender, TextBoxTextChangingEventArgs args) => capturedSynchronously = draftText() == text;
        box.TextChanging += OnChanging;
        try { box.Text = text; }
        finally { box.TextChanging -= OnChanging; }
        Check(capturedSynchronously, label + " is captured before asynchronous TextChanged and queued renders");
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.DispatcherQueue.TryEnqueue(() => rendered.TrySetResult());
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(box.Text == text && draftText() == text, label + " survives the queued controller render");
    }

    private static void CheckDestructiveBadge(Border badge, string label)
    {
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
        {
            Check(badge.Background is SolidColorBrush, label + " keeps a solid system-color fill in a contrast theme");
            return;
        }
        Check(badge.Background is SolidColorBrush { Color: { A: 255, R: 0xDC, G: 0x26, B: 0x26 } } && badge.Child is TextBlock { Foreground: SolidColorBrush { Color: { A: 255, R: 255, G: 255, B: 255 } } },
            label + " is the official solid destructive fill (#dc2626) under white text");
    }

    /// <summary>Resizes to the four widths the parity record compares, checks that nothing runs past its container, and captures the page at each.</summary>
    private static async Task CaptureWidthsAsync(Window window, AdminScheduledTasksPage page, string name, string? containerId, int[]? widths = null)
    {
        try
        {
            foreach (var width in widths ?? [1440, 1024, 800, 560])
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
                await Task.Delay(400);
                page.UpdateLayout();
                FrameworkElement container = containerId == null ? page.ScrollContent
                    : Descendants(page).OfType<FrameworkElement>().First(element => AutomationProperties.GetAutomationId(element) == containerId && IsLaidOut(element));
                var offenders = Overflowing(container);
                Check(offenders.Length == 0, $"{name} at {width} px keeps every control and text inside its container" + (offenders.Length == 0 ? "" : ": " + string.Join(", ", offenders)));
                var origin = container.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0, 0));
                Check(origin.X >= -1 && origin.X + container.ActualWidth <= page.ActualWidth + 1, $"{name} at {width} px fits inside the page ({container.ActualWidth:0} of {page.ActualWidth:0})");
                if (name == "detail") CheckDueSamples(page, width);
                await CaptureAsync(page.ScrollContent, $"tasks-{name}-{width}.png");
            }
        }
        finally
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            await Task.Delay(400);
        }
    }

    /// <summary>
    /// The due-samples table at one window width: no sideways scroller, every cell and the last column inside the panel and the page, and
    /// below 768 px of page width wrapping dates (each word whole) instead of one-line dates.
    /// </summary>
    private static void CheckDueSamples(AdminScheduledTasksPage page, int width)
    {
        var panel = Descendants(page).OfType<FrameworkElement>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "TaskDueSamples" && IsLaidOut(element));
        var wrap = page.ActualWidth < 768;
        var label = $"due samples at {width} px (page {page.ActualWidth:0}, dates {(wrap ? "wrap" : "stay on one line")})";
        if (panel == null) { Check(false, label + " are shown"); return; }
        Check(!Descendants(panel).OfType<ScrollViewer>().Any(), label + " have no sideways scroller");
        var panelOrigin = panel.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0, 0));
        Check(panelOrigin.X >= -1 && panelOrigin.X + panel.ActualWidth <= page.ActualWidth + 1, label + " fit inside the page");
        var blocks = Descendants(panel).OfType<TextBlock>().Where(IsLaidOut).ToArray();
        var outside = blocks.Where(block =>
        {
            var origin = block.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0));
            return origin.X < -1 || origin.X + block.ActualWidth > panel.ActualWidth + 1;
        }).Select(block => block.Text).ToArray();
        Check(outside.Length == 0, label + " keep every text inside the panel" + (outside.Length == 0 ? "" : ": " + string.Join(", ", outside)));
        var lastHeader = blocks.FirstOrDefault(block => block.Text == "Last attempt");
        var lastCell = lastHeader == null ? null : VisualTreeHelper.GetParent(lastHeader) as FrameworkElement;
        var lastRight = lastCell == null ? double.NaN : lastCell.TransformToVisual(panel).TransformPoint(new Windows.Foundation.Point(0, 0)).X + lastCell.ActualWidth;
        Check(lastCell != null && lastRight <= panel.ActualWidth + 1, label + " keep the right edge of the last column inside the panel (" + lastRight.ToString("0") + " of " + panel.ActualWidth.ToString("0") + ")");
        var dateBlocks = blocks.Where(block => VisualTreeHelper.GetParent(block) is Border cell && Grid.GetRow(cell) > 0 && Grid.GetColumn(cell) is 1 or 3 && Grid.GetColumnSpan(cell) == 1).ToArray();
        Check(dateBlocks.Length == 10 && dateBlocks.All(block => block.TextWrapping == (wrap ? TextWrapping.Wrap : TextWrapping.NoWrap)), label + " use the matching wrapping");
        if (!wrap) return;
        var probe = new TextBlock { FontSize = dateBlocks[0].FontSize, FontFamily = dateBlocks[0].FontFamily };
        var broken = new List<string>();
        foreach (var block in dateBlocks)
            foreach (var word in block.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                probe.Text = word;
                probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe.DesiredSize.Width > block.ActualWidth + 1) broken.Add(word);
            }
        Check(broken.Count == 0, label + " break only between words" + (broken.Count == 0 ? "" : ": " + string.Join(", ", broken.Distinct())));
    }

    /// <summary>Text inside a button's own rounded corners: padding at least the corner radius on every side.</summary>
    private static void CheckInsideCorners(Control control, Thickness padding, string label) =>
        Check(padding.Left >= control.CornerRadius.TopLeft && padding.Top >= control.CornerRadius.TopLeft && padding.Right >= control.CornerRadius.TopRight && padding.Bottom >= control.CornerRadius.BottomRight &&
            control.Margin.Left == -padding.Left && control.Margin.Top == -padding.Top,
            $"{label} keeps its text out of its rounded corners (padding {padding.Left:0}, radius {control.CornerRadius.TopLeft:0}, margin {control.Margin.Left:0})");

    /// <summary>Controls and free text whose box leaves the container sideways. Parts inside a control's own template are the control's business.</summary>
    private static string[] Overflowing(FrameworkElement container)
    {
        var width = container.ActualWidth;
        var found = new List<string>();
        foreach (var element in Descendants(container).OfType<FrameworkElement>())
        {
            if (ReferenceEquals(element, container) || element is not (Control or TextBlock) || element.ActualWidth <= 0 || !IsLaidOut(element)) continue;
            var insideControl = false;
            for (var parent = VisualTreeHelper.GetParent(element); parent != null && !ReferenceEquals(parent, container); parent = VisualTreeHelper.GetParent(parent))
                if (parent is Control and not ScrollViewer) { insideControl = true; break; }
            if (insideControl) continue;
            var origin = element.TransformToVisual(container).TransformPoint(new Windows.Foundation.Point(0, 0));
            // A button padded out of its own rounded corners carries an equal negative margin: its text is where it was, only the button box reaches past by that margin.
            var left = Math.Max(0, -element.Margin.Left); var right = Math.Max(0, -element.Margin.Right);
            if (origin.X < -1 - left || origin.X + element.ActualWidth > width + 1 + right)
                found.Add((AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name) + $"@{origin.X:0}+{element.ActualWidth:0}/{width:0}");
        }
        return found.ToArray();
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(150);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, $"{name} rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private static async Task CloseDialogAsync(AdminScheduledTasksPage? page)
    {
        if (page == null || Field(page, "_dialog") is not ContentDialog dialog) return;
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Hide();
        try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { Check(false, "an open dialog closed before teardown"); }
    }

    private static Task UnloadedAsync(FrameworkElement element)
    {
        var done = new TaskCompletionSource();
        element.Unloaded += (_, _) => done.TrySetResult();
        return done.Task;
    }

    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(text => IsLaidOut(text)).Select(text => text.Text).ToArray();
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static bool IsLaidOut(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is UIElement ui; current = VisualTreeHelper.GetParent(current))
            if (ui.Visibility != Visibility.Visible) return false;
        return true;
    }
    private static Button Button(DependencyObject root, string id) => Descendants(root).OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == id);
    private static HyperlinkButton Link(DependencyObject root, string id) => Descendants(root).OfType<HyperlinkButton>().First(link => AutomationProperties.GetAutomationId(link) == id);
    private static HyperlinkButton Crumb(DependencyObject root, string id) => Link(root, id);
    private static TextBlock Text(DependencyObject root, string id) => Descendants(root).OfType<TextBlock>().First(text => AutomationProperties.GetAutomationId(text) == id);
    private static TextBox Box(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().First(box => AutomationProperties.GetAutomationId(box) == id);
    private static ComboBox Combo(DependencyObject root, string id) => Descendants(root).OfType<ComboBox>().First(combo => AutomationProperties.GetAutomationId(combo) == id);
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(button => button.Content?.ToString() == label || AutomationProperties.GetName(button) == label);
    private static async Task InvokeAsync(Button button)
    {
        await Until(() => button.IsLoaded && button.IsEnabled && IsLaidOut(button), "enabled visible button " + AutomationProperties.GetAutomationId(button));
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static void Invoke(HyperlinkButton link) => ((IInvokeProvider)new HyperlinkButtonAutomationPeer(link).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
