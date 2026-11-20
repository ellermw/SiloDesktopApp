using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Drives the real Autoscan Activity page, through the production registration, the Admin shell and its inner Frame, against the official
/// page at 88b825d57e4b2296050fda69eaf619eefbf208d1: loading, populated scans, numbered pagination, live queue, the poll table, the inspector sheet
/// with its pages, filters, empty and failed reads with a reachable Try again, and a refused and an unknown-outcome cancellation. Synthetic data only.
/// </summary>
internal static class AdminAutoscanActivityNativeFixture
{
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<string> Reads = new();
        internal int Writes;
        internal bool FailHistory, FailStatus, EmptyHistory, UnknownCancel, RefuseCancel;
        internal TaskCompletionSource? HoldHistory;
        internal long ActiveScans = 2;
        internal int Total = 75;
        /// <summary>Status of the rich poll's first scan run, changed between reads to prove the inspector keeps the newest snapshot.</summary>
        internal string FirstRunStatus = "running";
        /// <summary>When set the rich poll is no longer on the page (it moved off after newer polls arrived).</summary>
        internal bool RichPollOffPage;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Reads.Enqueue(request.RequestUri.PathAndQuery);
            if (request.Method == HttpMethod.Post)
            {
                Writes++;
                using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Check(path == "/api/v2/scan/cancel" && data.RootElement.GetProperty("library_id").GetString() == "7", "cancel uses the exact published body");
                if (RefuseCancel) return Json("""{"title":"Synthetic refusal","status":403}""", HttpStatusCode.Forbidden);
                if (UnknownCancel) throw new HttpRequestException("synthetic cancellation uncertainty");
                return Json("""{"library_id":"7","cancelled":2}""");
            }
            if (path.EndsWith("/scans") || path.EndsWith("/events"))
            {
                if (HoldHistory is { } hold) await hold.Task;
                if (FailHistory) return Json("""{"title":"Synthetic unavailable","status":503}""", HttpStatusCode.ServiceUnavailable);
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                var limit = int.Parse(query["limit"] ?? "50"); var cursor = query["cursor"];
                var offset = cursor == null ? 0 : int.Parse(cursor[1..]);
                var total = EmptyHistory ? 0 : string.IsNullOrEmpty(query["q"]) && string.IsNullOrEmpty(query["status"]) ? Total : 3;
                var count = Math.Max(0, Math.Min(limit, total - offset));
                var items = new JsonArray();
                for (var index = 0; index < count; index++) items.Add(path.EndsWith("/scans") ? Scan(offset + index) : Poll(offset + index));
                var more = offset + count < total;
                var page = new JsonObject { ["has_more"] = more }; if (more) page["next_cursor"] = "o" + (offset + count);
                return Json(new JsonObject { ["items"] = items, ["total"] = total, ["page"] = page }.ToJsonString());
            }
            if (path == "/api/v2/admin/autoscan/status" && FailStatus)
                return Json("""{"title":"Synthetic status unavailable","status":503}""", HttpStatusCode.ServiceUnavailable);
            return Json(path switch
            {
                "/api/v2/admin/autoscan/status" => "{\"enabled\":true,\"active_scans\":" + ActiveScans + ",\"accepted_scans\":1,\"running_scans\":1,\"latest_event_at\":\"2026-11-17T12:00:00Z\",\"sources\":[],\"running_polls\":[{\"id\":\"12\",\"source_id\":\"source-1\",\"plugin_id\":\"synthetic\",\"capability_id\":\"files\",\"started_at\":\"2026-11-17T12:00:00Z\",\"elapsed_ms\":500}]}",
                "/api/v2/libraries" => """{"items":[{"id":"7","name":"Synthetic Movies","type":"movies","enabled":true}],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/sources" => """{"items":[{"id":"source-1","label":"Synthetic Source","plugin_id":"synthetic","capability_id":"files"}],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/connections" => """{"items":[],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/scan-source-plugins" => """{"items":[{"plugin_id":"synthetic","capability_id":"files","display_name":"Synthetic Plugin"}],"page":{"has_more":false}}""",
                "/api/v2/admin/server/status" => """{"restart_required":false,"restart_mark_count":0,"restart_requested":false}""",
                _ => "{}"
            });
        }

        private static JsonObject Scan(int index) => new()
        {
            ["id"] = "scan-" + index, ["library_id"] = "7", ["mode"] = "file", ["path"] = "/synthetic/movies/a-long-folder/a-long-title-" + index + ".mkv", ["trigger"] = "autoscan",
            ["status"] = "completed", ["requested_at"] = "2026-11-17T12:00:00Z", ["completed_at"] = "2026-11-17T12:00:01Z", ["source_id"] = "source-1", ["plugin_id"] = "synthetic",
            ["capability_id"] = "files", ["autoscan_event_id"] = "1000", ["event_status"] = "success",
            ["result"] = new JsonObject { ["new"] = 1, ["updated"] = 2, ["unchanged"] = 0, ["missing"] = 0, ["missing_skipped_protected"] = 1, ["files_deleted"] = 0, ["items_deleted"] = 0, ["memberships_removed"] = 0, ["errors"] = 0, ["skipped"] = 0 },
        };

        private JsonObject Poll(int index)
        {
            if (index == 0 && !RichPollOffPage) return RichPoll();
            return new JsonObject
            {
                ["id"] = (2000 + index).ToString(), ["source_id"] = "source-1", ["plugin_id"] = "synthetic", ["capability_id"] = "files", ["started_at"] = "2026-11-17T12:00:00Z",
                ["completed_at"] = "2026-11-17T12:00:01Z", ["duration_ms"] = 1000, ["status"] = "success", ["delivery_mode"] = "poll", ["changes_returned"] = 0, ["changes_resolved"] = 0,
                ["targets_claimed"] = 0, ["scans_created"] = 0, ["scans_reused"] = 0, ["scans_suppressed"] = 0, ["scan_runs"] = new JsonArray(), ["changes"] = new JsonArray(), ["changes_truncated"] = false,
            };
        }

        /// <summary>A webhook poll with the 50-change cap reached (107 reported), 55 created scan runs, an unknown outcome and a very long error.</summary>
        private JsonObject RichPoll()
        {
            static string Number(int value) => value.ToString("000");
            var changes = new JsonArray();
            for (var index = 0; index < 40; index++)
                changes.Add(new JsonObject
                {
                    ["source_path"] = "/synthetic/incoming/movie-" + Number(index + 1) + ".mkv", ["rewritten_path"] = "/synthetic/movies/title-" + Number(index + 1) + "/movie.mkv", ["outcome"] = "queued",
                    ["library_id"] = "7", ["target_mode"] = "subtree", ["target_path"] = "/synthetic/movies/title-" + Number(index + 1), ["scan_run_id"] = "01RUN" + Number(index + 1),
                });
            for (var index = 0; index < 5; index++)
                changes.Add(new JsonObject { ["source_path"] = "/synthetic/elsewhere/orphan-" + index + ".mkv", ["rewritten_path"] = "/synthetic/elsewhere/orphan-" + index + ".mkv", ["outcome"] = "unresolved", ["reason"] = "no_library_match" });
            for (var index = 0; index < 3; index++)
                changes.Add(new JsonObject { ["source_path"] = "/synthetic/incoming/repeat-" + index + ".mkv", ["rewritten_path"] = "/synthetic/incoming/repeat-" + index + ".mkv", ["outcome"] = "suppressed", ["target_path"] = "/synthetic/incoming" });
            changes.Add(new JsonObject { ["source_path"] = "/synthetic/incoming/broken.mkv", ["rewritten_path"] = "/synthetic/incoming/broken.mkv", ["outcome"] = "error", ["reason"] = "resolve_failed", ["detail"] = "synthetic resolver detail" });
            changes.Add(new JsonObject { ["source_path"] = "/synthetic/incoming/future.mkv", ["rewritten_path"] = "/synthetic/incoming/future.mkv", ["outcome"] = "future_outcome", ["reason"] = "future_reason", ["detail"] = "Synthetic newer-server detail" });
            var runs = new JsonArray();
            for (var index = 0; index < 55; index++)
            {
                var run = new JsonObject
                {
                    ["id"] = "01RUN" + Number(index + 1), ["library_id"] = "7", ["mode"] = "subtree", ["path"] = "/synthetic/movies/title-" + Number(index + 1), ["trigger"] = "autoscan",
                    ["status"] = index == 0 ? FirstRunStatus : index == 54 ? "failed" : "completed", ["requested_at"] = "2026-11-17T12:00:00Z",
                };
                if (index == 54) run["error_message"] = "synthetic storage root offline";
                if (index != 0 || FirstRunStatus != "running") run["result"] = new JsonObject { ["new"] = 1, ["updated"] = 0, ["unchanged"] = 0, ["missing"] = 0, ["missing_skipped_protected"] = 0, ["files_deleted"] = 0, ["items_deleted"] = 0, ["memberships_removed"] = 0, ["errors"] = 0, ["skipped"] = 0 };
                if (index != 0 || FirstRunStatus != "running") run["completed_at"] = "2026-11-17T12:00:05Z";
                runs.Add(run);
            }
            return new JsonObject
            {
                ["id"] = "1000", ["source_id"] = "source-1", ["plugin_id"] = "synthetic", ["capability_id"] = "files", ["started_at"] = "2026-11-17T12:00:00Z", ["completed_at"] = "2026-11-17T12:02:05Z",
                ["duration_ms"] = 125000, ["status"] = "error", ["delivery_mode"] = "webhook", ["provider_event_type"] = "Download", ["changes_returned"] = 107, ["changes_resolved"] = 45,
                ["targets_claimed"] = 41, ["scans_created"] = 55, ["scans_reused"] = 3, ["scans_suppressed"] = 3,
                ["error_message"] = string.Concat(Enumerable.Repeat("Synthetic failure detail that keeps going so the box has to scroll. ", 40)).TrimEnd(),
                ["scan_runs"] = runs, ["changes"] = changes, ["changes_truncated"] = true,
            };
        }

        private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status)
            { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    private sealed class Channel : IAdminRealtimeChannel
    {
        internal int Leases;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) { Check(channels.SequenceEqual(["scans"]), "leases only the shared scans channel"); Leases++; return new Release(() => Leases--); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement data) { data = default; return false; }
        /// <summary>The live scans: a running one with progress, a queued one, and (when asked) more queued ones so the queue paginates.</summary>
        internal void Snapshot(int extra = 0)
        {
            var scans = new JsonArray
            {
                new JsonObject { ["id"] = "queued", ["library_id"] = 7, ["mode"] = "library", ["trigger"] = "autoscan", ["status"] = "accepted" },
                new JsonObject { ["id"] = "running", ["library_id"] = 7, ["mode"] = "file", ["path"] = "/synthetic/movie.mkv", ["trigger"] = "autoscan", ["status"] = "running",
                    ["result"] = new JsonObject { ["total_files"] = 10, ["files_processed"] = 4, ["message"] = "Processing files" } },
                new JsonObject { ["id"] = "manual", ["library_id"] = 7, ["mode"] = "file", ["path"] = "/synthetic/manual.mkv", ["trigger"] = "manual", ["status"] = "running" },
            };
            for (var index = 0; index < extra; index++)
                scans.Add(new JsonObject { ["id"] = "more-" + index, ["library_id"] = 7, ["mode"] = "subtree", ["path"] = "/synthetic/more-" + index, ["trigger"] = "autoscan", ["status"] = "accepted" });
            SnapshotReceived?.Invoke("scans", JsonDocument.Parse(scans.ToJsonString()).RootElement.Clone());
        }
        private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    }

    internal static async Task RunAsync(Window window)
    {
        var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var channel = new Channel(); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(navigation)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new AdminAutoscanActivityRealtime(channel));
        collection.AddAdminAutoscanActivity();
        await using var services = collection.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesField.GetValue(null); var previousContent = window.Content; var previousTitle = DocumentTitle.AppName;
        Frame? outer = null; AdminAutoscanActivityPage? page = null; AdminShellPage? shell = null;
        Task outstanding = Task.CompletedTask;
        var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            servicesField.SetValue(null, services);
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/autoscan/activity") is { Kind: AdminRouteResolutionKind.Route }, "Activity resolves through production registration and is not an unsupported route");
            wire.HoldHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Until(() => outer.Content is AdminShellPage, "real Admin shell"); shell = (AdminShellPage)outer.Content;
            shell.Unloaded += (_, _) => shellUnloaded.TrySetResult();
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminAutoscanActivityPage active && active.IsLoaded, "real Activity Frame page"); page = (AdminAutoscanActivityPage)inner.Content;
            page.Unloaded += (_, _) => pageUnloaded.TrySetResult();
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "in-place Frame has no slide transition");

            // Loading, then the populated scan history.
            await Until(() => page.RenderedContent == "loading", "loading state while the first read is held");
            Check(Descendants(page).OfType<Border>().Count(border => border.Height == 64) == 6, "loading shows the six skeleton rows");
            await Capture(page, "admin-autoscan-activity-loading-1440.png");
            wire.HoldHistory.TrySetResult(); wire.HoldHistory = null;
            await Until(() => page.RenderedContent == "table" && TextShown(page, "Synthetic Source"), "scan rows and bounded label lookups");
            Check(!wire.Reads.Any(path => path.StartsWith("/api/v2/admin/autoscan/events")), "inactive Polls history is not read");
            Check(!wire.Reads.Any(path => path.Contains("offset=")), "history uses signed cursors rather than invented offset parameters");
            Check(TextShown(page, "Showing 1–25 of 75 scans"), "official scan page summary");
            Check(Find<Button>(page, "AutoscanActivityPagerPage3") != null && Find<Button>(page, "AutoscanActivityPagerLast").IsEnabled && !Find<Button>(page, "AutoscanActivityPagerFirst").IsEnabled, "numbered pagination with first and last page buttons");
            Check(TextShown(page, "Full library scan") || TextShown(page, "Single file scan"), "scan scope mode is shown");
            channel.Snapshot(); await Until(() => TextShown(page, "Processing files · 4 / 10 (40%)"), "live progress");
            Check(page.Controller!.State.Queue[0].Id == "running" && page.Controller.State.Queue.Count == 2, "live queue sorts running before queued and shows only autoscan scans");
            Check(Find<TextBlock>(page, "AutoscanActivityPagerSummary") != null, "history pager is separate from the queue");
            await Capture(page, "admin-autoscan-activity-scans-1440.png");
            await CaptureBottom(page, "admin-autoscan-activity-scans-history-1440.png");

            // Numbered pages walk the signed cursors; the last page and the size selector.
            Invoke(Find<Button>(page, "AutoscanActivityPagerPage3")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-50" && page.Controller.State.Scans.PageIndex == 2, "third page by number");
            Check(TextShown(page, "Showing 51–75 of 75 scans"), "last page summary");
            Check(!Find<Button>(page, "AutoscanActivityPagerNext").IsEnabled && !Find<Button>(page, "AutoscanActivityPagerLast").IsEnabled, "last page disables Next and Last");
            Invoke(Find<Button>(page, "AutoscanActivityPagerFirst")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-0", "first page button");
            Invoke(Find<Button>(page, "AutoscanActivityPagerLast")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-50", "last page button");
            SelectByTag(Find<ComboBox>(page, "AutoscanActivityPagerSize"), "50");
            await Until(() => page.Controller.State.PageSize == 50 && page.Controller.State.Scans.PageIndex == 0 && page.RenderedIds.Count == 50, "rows per page returns to the first page");
            Check(TextShown(page, "Showing 1–50 of 75 scans"), "summary follows the page size");
            SelectByTag(Find<ComboBox>(page, "AutoscanActivityPagerSize"), "25"); await Until(() => page.Controller.State.PageSize == 25 && page.RenderedIds.Count == 25, "rows per page back to 25");

            // The live queue paginates at eight rows.
            channel.Snapshot(8); await Until(() => TextShown(page, "Showing 1–8 of 10 scans"), "queue pagination summary");
            Invoke(Find<Button>(page, "AutoscanQueuePagerPage2")); await Until(() => TextShown(page, "Showing 9–10 of 10 scans"), "queue second page");
            channel.Snapshot(); await Until(() => page.Controller.State.Queue.Count == 2 && !TextShown(page, "Showing 9–10 of 10 scans"), "queue shrinks back and drops its page control");

            // Polls: table, badges and the inspector sheet.
            Invoke(Find<Button>(page, "AutoscanActivityPolls")); await Until(() => page.RenderedContent == "table" && page.Controller!.State.Selected == AdminAutoscanHistory.Polls && TextShown(page, "Webhook · Download"), "poll table and webhook badge");
            Check(TextShown(page, "Poll log") && TextShown(page, "Diagnostic poll records from scan-source plugins."), "poll heading");
            Check(AutomationProperties.GetName(Find<Button>(page, "AutoscanActivityDetails_1000")) == "107 paths · 55+ linked", "Details button names its paths and linked scans, with a lower-bound mark");
            Check(TextShown(page, "Error") && TextShown(page, "2m 5s"), "error badge and duration");
            Check(TextShown(page, "107 changes") && TextShown(page, "41 targets") && TextShown(page, "55 created") && TextShown(page, "3 reused") && TextShown(page, "3 suppressed"), "poll metric strip");
            Check(!Descendants(page).OfType<Expander>().Any(), "poll details are not an inline disclosure");
            Check(TextShown(page, "Showing 1–25 of 75 polls"), "poll page summary");
            await Capture(page, "admin-autoscan-activity-polls-1440.png");

            var trigger = Find<Button>(page, "AutoscanActivityDetails_1000");
            Invoke(trigger); await Until(() => page.InspectorOpen && page.Inspector != null, "inspector sheet opens from Details");
            var inspector = page.Inspector!;
            Check(inspector.Tab == "paths", "inspector opens on Paths when paths were recorded");
            Check(TextShown(inspector.Root, "Showing the first 50 of 107 changes."), "truncated change log note");
            Check(TextShown(inspector.Root, "Showing 1–15 of 50 items"), "paths page one of four");
            var error = Find<ScrollViewer>(inspector.Root, "AutoscanInspectorError");
            await Until(() => error.ScrollableHeight > 0, "a long error scrolls inside its bounded box");
            Check(error.MaxHeight == 128 && error.IsTabStop, "the error box is bounded and keyboard focusable");
            Check(TextShown(window.Content, "Synthetic Source"), "inspector names the source in its title");
            await Capture(shell, "admin-autoscan-activity-inspector-1440.png");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorPathsNext")); await Until(() => TextShown(inspector.Root, "Showing 16–30 of 50 items"), "paths next page");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorPathsLast")); await Until(() => TextShown(inspector.Root, "Showing 46–50 of 50 items"), "paths last page");
            Check(TextShown(inspector.Root, "future_outcome") && TextShown(inspector.Root, "Synthetic newer-server detail"), "an unknown outcome shows its raw value and detail");
            SelectByTag(Find<ComboBox>(inspector.Root, "AutoscanInspectorPathsSize"), "25"); await Until(() => TextShown(inspector.Root, "Showing 1–25 of 50 items"), "paths page size");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorOutcome_unresolved")); await Until(() => TextShown(inspector.Root, "Showing 1–5 of 5 items"), "outcome chip filters the paths");
            Check(TextShown(inspector.Root, "No Silo library folder contains this path. Add a path mapping for this source or check the library folders."), "official unresolved reason text");
            Find<TextBox>(inspector.Root, "AutoscanInspectorPathsSearch").Text = "orphan-3"; await Until(() => TextShown(inspector.Root, "Showing 1–1 of 1 item"), "paths search");
            Find<TextBox>(inspector.Root, "AutoscanInspectorPathsSearch").Text = "no such path"; await Until(() => TextShown(inspector.Root, "No matches"), "paths search with no match");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorPathsEmptyReset")); await Until(() => TextShown(inspector.Root, "Showing 1–25 of 50 items"), "paths reset restores every path");

            // Jump from a path's scan id to that scan, then the scans' own pages and filters.
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorJump_0")); await Until(() => inspector.Tab == "scans" && TextShown(inspector.Root, "Showing 1–1 of 1 scan"), "scan id pill jumps to that scan");
            Check(Find<TextBox>(inspector.Root, "AutoscanInspectorScansSearch").Text == "01RUN001", "the jump searches for the scan id");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorScansSearchReset")); await Until(() => TextShown(inspector.Root, "Showing 1–20 of 55 scans"), "scans reset shows every run");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorScansLast")); await Until(() => TextShown(inspector.Root, "Showing 41–55 of 55 scans") && TextShown(inspector.Root, "synthetic storage root offline"), "last scans page with the failed run's error");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorStatus_failed")); await Until(() => TextShown(inspector.Root, "Showing 1–1 of 1 scan"), "scan status chip");
            Invoke(Find<Button>(inspector.Root, "AutoscanInspectorStatus_all")); await Until(() => TextShown(inspector.Root, "Showing 1–20 of 55 scans"), "all statuses chip");
            Check(TextShown(inspector.Root, "running") || TextShown(inspector.Root, "Running"), "the running scan shows its status");

            // The inspector keeps the newest snapshot, also after the poll leaves the page.
            Check(inspector.Event.ScanRuns![0].Status == "running", "inspector starts with the running scan");
            wire.FirstRunStatus = "completed"; await page.Controller!.RefreshAsync();
            await Until(() => inspector.Event.ScanRuns![0].Status == "completed" && TextShown(inspector.Root, "1 new"), "a newer read replaces the inspector's snapshot");
            Check(inspector.Tab == "scans" && Find<TextBox>(inspector.Root, "AutoscanInspectorScansSearch").Text == "", "a refresh keeps the admin's tab and filters");
            wire.RichPollOffPage = true; await page.Controller!.RefreshAsync();
            await Until(() => page.Controller!.State.Polls.Items.All(poll => poll.Id != "1000") && !page.Controller.State.Polls.Fetching, "the poll leaves the current page");
            Check(page.InspectorOpen && inspector.Event.Id == "1000" && inspector.Event.ScanRuns![0].Status == "completed" && TextShown(inspector.Root, "1 new"), "the last snapshot stays after the poll leaves the page");
            wire.RichPollOffPage = false; await page.Controller!.RefreshAsync();
            await Until(() => page.Controller!.State.Polls.Items.Any(poll => poll.Id == "1000") && !page.Controller.State.Polls.Fetching, "the poll returns");

            // Closing returns focus to the Details button that opened the sheet.
            Invoke(Find<Button>(window.Content, "LogsDetailClose")); await Until(() => !page.InspectorOpen, "inspector closes");
            await Until(() => FocusedId(page) == "AutoscanActivityDetails_1000", "focus returns to the Details button");
            Check(page.Inspector == null, "closing releases the inspector");

            // Filters: debounced search, status, reset, and the empty filtered state.
            var search = Find<TextBox>(page, "AutoscanActivitySearch"); search.Text = "synthetic filter";
            await Until(() => page.Controller!.State.Query == "synthetic filter" && !page.Controller.State.Polls.Fetching && page.Controller.State.Polls.Loaded, "debounced search is shared by both views");
            Check(TextShown(page, "Showing 1–3 of 3 polls"), "filtered poll count");
            SelectByTag(Find<ComboBox>(page, "AutoscanActivityStatus"), "success");
            await Until(() => page.Controller!.State.PollStatus == "success" && !page.Controller.State.Polls.Fetching, "poll status filter");
            Check(wire.Reads.Any(path => path.StartsWith("/api/v2/admin/autoscan/events") && path.Contains("status=success") && path.Contains("q=synthetic%20filter")), "the poll read carries only the official filter parameters");
            Invoke(Find<Button>(page, "AutoscanActivityScans")); await Until(() => page.Controller!.State.Selected == AdminAutoscanHistory.Scans && page.RenderedContent == "table", "scans view keeps the shared search");
            Check(search.Text == "synthetic filter" && page.Controller.State.ScanStatus == "" && page.Controller.State.Scans.PageIndex == 0, "scans keep the search but not the poll status, from the first page");
            wire.EmptyHistory = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh")); await Until(() => page.RenderedContent == "empty", "filtered empty state");
            Check(TextShown(page, "No autoscan activity matches those filters."), "official filtered empty label");
            Invoke(Find<Button>(page, "AutoscanActivityReset")); await Until(() => page.Controller!.State.Query == "" && search.Text == "" && page.Controller.State.PollStatus == "", "reset clears the search and status filters");
            await Until(() => page.RenderedContent == "empty" && TextShown(page, "No autoscan scans have been created yet."), "unfiltered empty scans label");
            wire.EmptyHistory = false; Invoke(Find<Button>(page, "AutoscanActivityRefresh")); await Until(() => page.RenderedContent == "table", "rows return");

            // Failed reads: the first read shows Try again; a background failure keeps the rows; status failure has its own retry.
            wire.FailHistory = true; Invoke(Find<Button>(page, "AutoscanActivityPolls"));
            await Until(() => page.RenderedContent == "error", "history failure"); wire.FailHistory = false;
            Check(TextShown(page, "Failed to load autoscan activity."), "failed first read shows the official message");
            Invoke(Find<Button>(page, "AutoscanActivityRetry")); await Until(() => page.RenderedContent == "table", "visible Try again recovers");
            wire.FailHistory = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh"));
            await Until(() => page.Controller!.State.Polls.Failure != null && TextShown(page, "Failed to load autoscan activity."), "failed background read is reported");
            Check(page.RenderedContent == "table" && page.RenderedIds.Count > 0, "a failed background read keeps the rows on screen");
            wire.FailHistory = false; Invoke(Find<Button>(page, "AutoscanActivityStaleRetry"));
            await Until(() => page.Controller!.State.Polls.Failure == null && !page.Controller.State.Polls.Fetching, "Try again beside the kept rows recovers");
            wire.FailStatus = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh"));
            await Until(() => page.Controller!.State.StatusFailure != null && TextShown(page, "Autoscan status could not be loaded. Retry to refresh live counts."), "independent live status failure");
            wire.FailStatus = false; Invoke(Find<Button>(page, "AutoscanActivityStatusRetry"));
            await Until(() => page.Controller!.State.StatusFailure == null && page.Controller.State.Status != null, "visible status retry recovers without a write");

            // Cancellation: a refusal can be retried; an unknown outcome blocks Cancel until reconciled and is never replayed.
            Invoke(Find<Button>(page, "AutoscanActivityScans")); await Until(() => page.RenderedContent == "table", "scans again");
            wire.RefuseCancel = true; channel.Snapshot(); await Task.Delay(80);
            Invoke(Find<Button>(page, "AutoscanActivityCancel_running")); await Until(() => TextShown(page, "Synthetic refusal"), "refused cancellation shows the server reason");
            Check(wire.Writes == 1 && page.Controller!.State.CancelReview.Count == 0 && Find<Button>(page, "AutoscanActivityCancel_running").IsEnabled, "a definite refusal leaves Cancel available to retry");
            wire.RefuseCancel = false; wire.UnknownCancel = true;
            Invoke(Find<Button>(page, "AutoscanActivityCancel_running")); await Until(() => page.Controller!.State.CancelReview.ContainsKey("7"), "unknown cancellation review");
            await Until(() => !Find<Button>(page, "AutoscanActivityCancel_running").IsEnabled, "the page disables Cancel behind the review gate");
            Check(wire.Writes == 2, "ambiguous command is sent once and disabled");
            Invoke(Find<Button>(page, "AutoscanReconcile_7")); await Task.Delay(80); Check(wire.Writes == 2, "reconciliation never replays a write");
            channel.Snapshot(); wire.ActiveScans = 0; Invoke(Find<Button>(page, "AutoscanReconcile_7"));
            await Until(() => page.Controller!.State.CancelReview.Count == 0, "explicit authoritative reconciliation");

            // Narrow layouts: cards below 1024, a phone-width column below 640.
            foreach (var width in new[] { 1024, 800, 560 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000)); await Task.Delay(150);
                await Capture(page, "admin-autoscan-activity-scans-" + width + ".png");
                await CaptureBottom(page, "admin-autoscan-activity-scans-history-" + width + ".png");
            }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 1000)); await Task.Delay(150);
            Invoke(Find<Button>(page, "AutoscanActivityPolls")); await Until(() => page.RenderedContent == "table" && page.RenderedIds.Contains("1000"), "poll cards when narrow");
            Check(Find<Button>(page, "AutoscanActivityDetails_1000") != null, "narrow poll cards keep the Details button");
            await Capture(page, "admin-autoscan-activity-polls-800.png");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); await Task.Delay(150);
            Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal)), "controls display resolved labels");
            wire.HoldHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
            outstanding = page.Controller!.RefreshAsync(); await Until(() => page.Controller.State.Polls.Fetching, "held read started");
            outer.Content = null;
            await pageUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); await shellUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.HoldHistory.TrySetResult(); await outstanding.WaitAsync(TimeSpan.FromSeconds(5));
            Check(page.Controller == null && page.RenderedIds.Count == 0 && channel.Leases == 0, "actual page/shell unload retires reads and realtime before restoring globals");
        }
        finally
        {
            wire.HoldHistory?.TrySetResult();
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            if (page is { IsLoaded: true }) await pageUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (shell is { IsLoaded: true }) await shellUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await outstanding.WaitAsync(TimeSpan.FromSeconds(5));
            window.Content = previousContent;
            servicesField.SetValue(null, previousServices); DocumentTitle.SetServerName(previousTitle);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Autoscan Activity: " + message);
        Program.Log("PASS: Autoscan Activity: " + message);
    }
    private static async Task Until(Func<bool> ready, string message)
    {
        for (var attempt = 0; attempt < 240; attempt++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Autoscan Activity: " + message);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static ControlType Find<ControlType>(object root, string id) where ControlType : FrameworkElement =>
        Descendants((DependencyObject)root).OfType<ControlType>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static bool TextShown(DependencyObject root, string value) => Descendants(root).OfType<TextBlock>().Any(text => text.Text == value);
    private static string FocusedId(FrameworkElement page) => FocusManager.GetFocusedElement(page.XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void SelectByTag(ComboBox select, string tag) => select.SelectedItem = select.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == tag);
    private static async Task CaptureBottom(FrameworkElement page, string name)
    {
        var viewer = Descendants(page).OfType<ScrollViewer>().First();
        viewer.ChangeView(null, viewer.ScrollableHeight, null, true); await Task.Delay(250);
        await Capture(page, name);
        viewer.ChangeView(null, 0, null, true); await Task.Delay(100);
    }
    private static async Task Capture(FrameworkElement page, string name)
    {
        page.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(page);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
