using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

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
                    return Json("""{"total":12,"due":3,"leased":1,"reason_counts":[{"reason":"refresh_failure","count":2},{"reason":"provider_artwork","count":0}],"attempt_buckets":[{"label":"1-2","count":5}],"due_samples":[],"recent_errors":[]}""");
                case "GET history":
                    return Json($$$"""{"items":[{"task_key":"{{{key}}}","started_at":"{{{Past}}}","completed_at":"{{{Past}}}","status":"completed","duration_ms":1500,"id":"7"}],"page":{"has_more":false}}""");
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
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton<IAdminRealtimeChannel>(channel)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
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
            Check(Texts(detail).Contains("library"), "detail shows the server category badge");
            Check(Texts(detail).Contains("Every 3 hour(s)"), "detail shows long trigger text");
            Check(Texts(detail).Any(text => text.StartsWith("This task history records how long it took to queue per-library scan runs.", StringComparison.Ordinal)) &&
                !Descendants(detail).OfType<TextBlock>().Any(text => text.Inlines.OfType<Microsoft.UI.Xaml.Documents.Hyperlink>().Any()),
                "the scan_libraries note has no Libraries link while Libraries is unregistered");
            Check(Texts(detail).Contains("Execution history") && Texts(detail).Contains("1s") && Texts(detail).Contains("completed"), "history shows its first page");
            Check(!Descendants(detail).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "TaskHistoryLoadOlder" && IsLaidOut(button)), "Load older is hidden without more history");

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
