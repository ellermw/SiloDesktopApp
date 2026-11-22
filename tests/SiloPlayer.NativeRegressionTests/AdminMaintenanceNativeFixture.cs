using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
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
/// Production Configure › Server › Maintenance registration, shell and Frame entry with synthetic
/// server data only. External launch and clipboard are intercepted: no browser opens and the
/// owner's clipboard is never read or written. Physical OS dialogs are not exercised.
/// </summary>
internal static class AdminMaintenanceNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var elapsed = 0; elapsed < milliseconds && !condition(); elapsed += 25) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private const string Signed = "/api/v2/admin/jobs/e-done/artifact?sig=fixture-signature";
    private const string Published = "https://bucket.fixture.invalid/seed?X-Amz-Signature=fixture-published";

    private static string Job(string id, string kind, string state, string extra = "") =>
        $$"""{"id":"{{id}}","kind":"{{kind}}","state":"{{state}}","terminal":{{(state is "succeeded" or "failed" or "canceled" ? "true" : "false")}},"cancelable":false,"created_at":"2026-11-16T10:00:00Z","library_ids":[],"artifact_size_bytes":0{{extra}}}""";
    private static string Page(string? next, params string[] rows) =>
        "{\"items\":[" + string.Join(',', rows) + "],\"page\":{\"has_more\":" + (next == null ? "false}}" : "true,\"next_cursor\":\"" + next + "\"}}");

    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<string> Bodies = new();
        internal volatile bool LinkPublished, SyncLost, EmptyCatalog;
        internal volatile bool EmptyLocalSources = true;
        internal TaskCompletionSource? QueuedGate;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.PathAndQuery;
            Requests.Enqueue(request.Method + " " + query);
            Bodies.Enqueue(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (request.Headers.Authorization != null && query.Contains("artifact", StringComparison.Ordinal)) return Json("{}", HttpStatusCode.Forbidden);
            switch (request.Method.Method + " " + request.RequestUri.AbsolutePath)
            {
                case "POST /api/v2/admin/catalog/export-jobs/e-done/publish":
                    LinkPublished = true;
                    return Json("""{"job_id":"e-done","url":"https://bucket.fixture.invalid/new?sig=fixture"}""");
                case "POST /api/v2/admin/catalog/import-jobs":
                    if (QueuedGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                    var accepted = Json(Job("i-new", "catalog_import", "queued"), HttpStatusCode.Accepted);
                    accepted.Headers.Location = new Uri("/api/v2/admin/jobs/i-new", UriKind.Relative);
                    return accepted;
                case "POST /api/v2/admin/catalog/import":
                    if (SyncLost) throw new HttpRequestException("synthetic lost answer");
                    return Json("""{"items_created":2,"files_created":3}""");
            }
            if (EmptyCatalog && request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/v2/admin/jobs"
                && request.RequestUri.Query.Contains("kind=catalog_", StringComparison.Ordinal)) return Json(Page(null));
            return query switch
            {
                "/api/v2/admin/jobs?kind=catalog_export&limit=10" => Json(Page(null,
                    Job("e-done", "catalog_export", "succeeded", ",\"download_url\":\"" + Signed + "\",\"public_link_supported\":true,\"catalog_result\":{\"items_exported\":5,\"files_exported\":9}"
                        + (LinkPublished ? ",\"public_url\":\"https://bucket.fixture.invalid/new?sig=fixture\"" : "")),
                    Job("e-pub", "catalog_export", "succeeded", ",\"public_url\":\"" + Published + "\",\"public_link_supported\":true"),
                    Job("e-run", "catalog_export", "running", ",\"progress\":{\"current\":3,\"total\":10,\"unit\":\"items\"}"))),
                "/api/v2/admin/jobs?kind=catalog_import&limit=10" => Json(Page(null,
                    Job("i-1", "catalog_import", "failed", ",\"source_label\":\"seed.json.gz\",\"failure\":{\"type\":\"https://fixture.invalid/docs/api/v2/problems/job_failed\",\"title\":\"Job failed\",\"detail\":\"The operation could not finish. Inspect administrator diagnostics before submitting new work.\",\"retryable\":false}"),
                    Job("i-2", "catalog_import", "queued", ",\"source_label\":\"https://user:pw@seeds.fixture.invalid/a.json.gz?sig=secret\""))),
                "/api/v2/admin/jobs?limit=30" => Json(Page("history-2",
                    Job("h-1", "image_cache_cleanup", "canceled", ",\"library_name\":\"Movies\",\"library_result\":{\"deleted_prefixes\":2,\"deleted_s3_objects\":41}"),
                    Job("h-2", "future_kind", "paused"),
                    Job("h-3", "library_refresh", "running", ",\"progress\":{\"current\":1,\"total\":4,\"unit\":\"items\"}"))),
                "/api/v2/admin/jobs?limit=30&cursor=history-2" => Json(Page(null, Job("h-4", "delete_library", "succeeded", ",\"library_id\":\"7\",\"library_result\":{\"deleted_media_files\":1}"))),
                "/api/v2/admin/catalog/local-import-sources?limit=50" => Json(EmptyLocalSources ? Page(null) : Page(null, """{"key":"/catalog-seeds/a.json.gz","size_bytes":10}""")),
                "/api/v2/admin/catalog/import-sources?limit=50" => Json(Page("bucket-2", """{"key":"catalog-seeds/one.json.gz","size_bytes":10,"last_modified":"2026-11-15T10:00:00Z"}""")),
                "/api/v2/admin/catalog/import-sources?limit=50&cursor=bucket-2" => Json(Page(null, """{"key":"catalog-seeds/two.json.gz","size_bytes":10}""")),
                "/api/v2/admin/server/status" => Json("""{"restart_required":false,"restart_mark_count":0,"restart_requested":false}"""),
                "/api/v2/admin/system/build" => Json("""{"display":"synthetic","available":true,"build_number":1}"""),
                _ => Json("{}"),
            };
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
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
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        // The same registration entry point as App, never a test-only page.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        Frame? outer = null;
        var launched = new ConcurrentQueue<Uri>();
        var copied = new ConcurrentQueue<string>();
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/maintenance");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route }) throw new InvalidOperationException("Maintenance is not available through the production Configure registration.");
            await Until(() => outer.Content is AdminShellPage, "shell");
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminMaintenancePage { IsLoaded: true }, "page loaded");
            var page = (AdminMaintenancePage)inner.Content;
            page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            page.CopyText = text => { copied.Enqueue(text); return true; };
            await Until(() => page.Controller?.State is { Exports.Loaded: true, Imports.Loaded: true, History.Loaded: true, BucketSources.Loaded: true, LocalSources.Loaded: true },
                "five lists loaded");
            await Until(() => Find<Button>(page, "MaintenanceDownload_e-done") is { IsLoaded: true }, "export rows rendered");
            var controller = page.Controller!;
            Check(true, "Maintenance activates through production registration, shell and Frame");
            Check(shell.CurrentRoute == AdminRoute.Maintenance, "shell reports the Maintenance route");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "page navigation keeps transitions suppressed");
            var texts = Descendants(outer).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(!texts.Any(text => text.StartsWith("web.", StringComparison.Ordinal) || text.StartsWith("admin.native.", StringComparison.Ordinal)), "every rendered label has catalog text");
            Check(texts.Contains("Maintenance") && texts.Contains("Catalog Import & Export") && texts.Contains("Recent Catalog Imports")
                && texts.Contains("Recent Catalog Exports") && texts.Contains("Job History"), "the header, both catalog lists and Job History are shown");
            Check(!texts.Any(text => text.Contains("fixture-signature", StringComparison.Ordinal) || text.Contains("fixture-published", StringComparison.Ordinal) || text.Contains("sig=secret", StringComparison.Ordinal)),
                "signed URLs and URL secrets are never rendered");
            Check(texts.Contains("https://seeds.fixture.invalid/a.json.gz"), "a remote source label is shown without user info or query");
            Check(texts.Contains("The operation could not finish. Inspect administrator diagnostics before submitting new work."), "the fixed safe failure sentence is shown");
            Check(texts.Contains("Canceled after deleting 41 cached objects; 2 prefixes fully cleaned"), "a cancelled image cache cleanup keeps its partial totals");
            Check(texts.Contains("future_kind") && texts.Contains("paused"), "an unknown future job kind keeps a generic row");

            // Empty non-polling catalog lists retain read-only, independent header refresh actions.
            server.EmptyCatalog = true;
            await Task.WhenAll(controller.RefreshAsync(AdminMaintenanceListKind.Imports), controller.RefreshAsync(AdminMaintenanceListKind.Exports));
            await Until(() => Find<TextBlock>(page, "MaintenanceImportsEmpty") is { IsLoaded: true }
                && Find<TextBlock>(page, "MaintenanceExportsEmpty") is { IsLoaded: true }, "empty catalog lists rendered");
            var importRefresh = Find<Button>(page, "MaintenanceImportsRefresh");
            var exportRefresh = Find<Button>(page, "MaintenanceExportsRefresh");
            Check(importRefresh is { IsEnabled: true } && IsShown(importRefresh)
                && exportRefresh is { IsEnabled: true } && IsShown(exportRefresh), "both empty catalog lists keep header Refresh actions");
            var historyReads = server.Requests.Count(sent => sent == "GET /api/v2/admin/jobs?limit=30");
            var posts = server.Requests.Count(sent => sent.StartsWith("POST ", StringComparison.Ordinal));
            server.EmptyCatalog = false;
            Invoke(importRefresh!);
            await Until(() => controller.State.Imports.Items.Count == 2 && !controller.State.Imports.IsFetching, "imports discovered by header refresh");
            Check(controller.State.Exports.Items.Count == 0, "import header refresh leaves the export chain unchanged");
            Invoke(exportRefresh!);
            await Until(() => controller.State.Exports.Items.Count == 3 && !controller.State.Exports.IsFetching, "exports discovered by header refresh");
            await Until(() => Find<Button>(page, "MaintenanceDownload_e-done") is { IsLoaded: true }, "refreshed exports rendered");
            Check(controller.State.CompletedExports.Count == 2, "export header refresh updates completed-export choices");
            Check(server.Requests.Count(sent => sent == "GET /api/v2/admin/jobs?limit=30") == historyReads
                && server.Requests.Count(sent => sent.StartsWith("POST ", StringComparison.Ordinal)) == posts, "catalog header refreshes only read their own lists");

            // Export actions: Download for an artifact, Create link only when unpublished, Copy URL only when published.
            Check(Find<Button>(page, "MaintenanceDownload_e-done") is { Visibility: Visibility.Visible, IsEnabled: true }, "Download is offered for an export artifact");
            Check(Find<Button>(page, "MaintenanceCreateLink_e-done") is { IsEnabled: true }, "Create seven-day link is offered for a completed unpublished export");
            Check(Find<Button>(page, "MaintenanceCreateLink_e-pub") == null && Find<Button>(page, "MaintenanceCopyUrl_e-pub") != null, "a published export offers Copy URL, not Create link");
            Check(Find<Button>(page, "MaintenanceCreateLink_e-run") == null && Find<Button>(page, "MaintenanceDownload_e-run") == null, "a running export offers no link or download");
            Invoke(Find<Button>(page, "MaintenanceDownload_e-done")!);
            await Until(() => launched.Count == 1, "download launched");
            launched.TryPeek(out var download);
            Check(download?.AbsoluteUri == "https://fixture.invalid" + Signed, "Download opens the signed route resolved against the connected server");
            Check(!server.Requests.Any(sent => sent.Contains("artifact", StringComparison.Ordinal)), "the app never fetches the signed artifact with account credentials");
            Invoke(Find<Button>(page, "MaintenanceCopyUrl_e-pub")!);
            await Until(() => copied.Count == 1, "copied");
            Check(copied.Single() == Published, "Copy URL places exactly the published link on the intercepted clipboard");
            Invoke(Find<Button>(page, "MaintenanceCreateLink_e-done")!);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/catalog/export-jobs/e-done/publish") && controller.State.PublishingJobId == null, "publish settled");
            await Until(() => Find<Button>(page, "MaintenanceCopyUrl_e-done") is { IsLoaded: true }, "published link row");
            Check(Find<Button>(page, "MaintenanceCreateLink_e-done") == null, "after publishing the row offers Copy URL instead of Create link");
            Check(server.Requests.Count(sent => sent.EndsWith("/publish", StringComparison.Ordinal)) == 1, "publish is sent once");

            // Job History: Load older appends the next page.
            var older = Find<Button>(page, "MaintenanceHistoryLoadOlder")!;
            await Until(() => older.IsLoaded && older.IsEnabled, "Load older ready");
            Invoke(older);
            await Until(() => controller.State.History.Items.Count == 4 && !controller.State.History.HasMore, "older jobs appended");
            await Until(() => Find<StackPanel>(page, "MaintenanceHistoryRow_h-4") is { IsLoaded: true }, "older row rendered");
            Check(Find<Button>(page, "MaintenanceHistoryLoadOlder") == null, "Load older disappears at the end of the history");
            var reads = server.Requests.Count;
            await Task.Delay(500);
            Check(server.Requests.Count == reads, "the page does not poll");

            // Import dialog: all four sources, partial rewrite validation and a held queued import.
            var dialog = await OpenImportAsync(page);
            var source = Find<ComboBox>(dialog, "MaintenanceImportSource")!;
            var submit = Find<Button>(dialog, "MaintenanceImportSubmit")!;
            Check(source.SelectedIndex == 0 && Find<TextBox>(dialog, "MaintenanceImportLocalPath")!.Text == "/catalog-seeds/" && submit.IsEnabled, "the dialog opens on Local File with the official default path");
            // An initially empty local source list can discover a new seed without closing the dialog or losing edits.
            var localRefresh = Find<Button>(dialog, "MaintenanceImportLocalRefresh")!;
            var localChoices = Find<ComboBox>(dialog, "MaintenanceImportLocalChoices")!;
            await Until(() => localRefresh.IsEnabled && IsShown(localRefresh), "empty local source Refresh ready");
            Check(controller.State.LocalSources.Items.Count == 0 && !IsShown(localChoices), "an empty local source list hides only the picker");
            var localPath = Find<TextBox>(dialog, "MaintenanceImportLocalPath")!;
            localPath.Text = "/catalog-seeds/manual.json.gz";
            await Until(() => controller.State.Draft.LocalPath == localPath.Text, "local path draft staged");
            var draftBeforeRefresh = controller.State.Draft;
            var localReads = server.Requests.Count(sent => sent == "GET /api/v2/admin/catalog/local-import-sources?limit=50");
            var postsBeforeRefresh = server.Requests.Count(sent => sent.StartsWith("POST ", StringComparison.Ordinal));
            server.EmptyLocalSources = false;
            Invoke(localRefresh);
            await Until(() => controller.State.LocalSources.Items.Count == 1 && !controller.State.LocalSources.IsFetching, "local seed discovered by visible Refresh");
            await Until(() => localChoices.Items.Count == 1 && IsShown(localChoices), "refreshed local picker shown");
            Check(ReferenceEquals(page.Controller, controller) && controller.State.DialogOpen && ReferenceEquals(controller.State.Draft, draftBeforeRefresh)
                && localPath.Text == "/catalog-seeds/manual.json.gz", "local Refresh preserves the page, open dialog and draft");
            Check(server.Requests.Count(sent => sent == "GET /api/v2/admin/catalog/local-import-sources?limit=50") == localReads + 1
                && server.Requests.Count(sent => sent.StartsWith("POST ", StringComparison.Ordinal)) == postsBeforeRefresh, "local Refresh rereads sources without sending a command");
            localChoices.SelectedIndex = 0;
            await Until(() => controller.State.Draft.LocalPath == "/catalog-seeds/a.json.gz", "newly discovered local seed chosen");
            // The draft changes synchronously but File Path is rendered by a queued controller render. Typing before that
            // render would be overwritten by it (a user cannot type in the same dispatcher turn as the pick).
            await Until(() => localPath.Text == "/catalog-seeds/a.json.gz", "chosen seed rendered into File Path");
            localPath.Text = AdminCatalogImportDraft.DefaultLocalPath;
            await Until(() => controller.State.Draft.LocalPath == AdminCatalogImportDraft.DefaultLocalPath, "default local path restored");
            Check(IsShown(localChoices), "detected local files are offered");
            foreach (var (index, panel, enabled) in new[] { (1, "MaintenanceImportExportChoices", false), (2, "MaintenanceImportBucketChoices", false), (3, "MaintenanceImportRemoteUrl", false), (0, "MaintenanceImportLocalPath", true) })
            {
                source.SelectedIndex = index;
                await Until(() => (int)controller.State.Draft.Source == index, "source " + index);
                await Until(() => Find<FrameworkElement>(dialog, panel) is { } element && IsShown(element), "panel " + panel);
                Check(submit.IsEnabled == enabled, $"source {index} submit state follows its own value");
            }
            source.SelectedIndex = 2;
            await Until(() => Find<Button>(dialog, "MaintenanceImportBucketMore") is { IsLoaded: true, IsEnabled: true } more && IsShown(more), "bucket Load more");
            Invoke(Find<Button>(dialog, "MaintenanceImportBucketMore")!);
            await Until(() => controller.State.BucketSources.Items.Count == 2 && !controller.State.BucketSources.HasMore, "bucket page appended");
            var bucket = Find<ComboBox>(dialog, "MaintenanceImportBucketChoices")!;
            await Until(() => bucket.Items.Count == 2, "bucket choices");
            bucket.SelectedIndex = 1;
            await Until(() => controller.State.Draft.ArtifactKey == "catalog-seeds/two.json.gz", "bucket key chosen");
            await Until(() => submit.IsEnabled, "bucket submit enabled");
            source.SelectedIndex = 0;
            await Until(() => controller.State.Draft.Source == AdminCatalogImportSourceKind.LocalPath, "back to local");
            await Until(() => Find<TextBox>(dialog, "MaintenanceImportLocalPath") is { IsLoaded: true, IsEnabled: true } local && IsShown(local), "local field rendered");
            var rewriteId = controller.State.Draft.Rewrites[0].Id;
            var from = Find<TextBox>(dialog, "MaintenanceRewriteFrom_" + rewriteId)!;
            await Until(() => from.IsLoaded && from.IsEnabled && IsShown(from), "rewrite field ready");
            from.Text = "/srv/media";
            await Until(() => controller.State.Draft.PartialRewriteIds.Count == 1, "partial row staged");
            await Until(() => Find<TextBlock>(dialog, "MaintenanceRewriteError_" + rewriteId) is { } error && IsShown(error) && !submit.IsEnabled, "partial row error");
            Check(true, "a partially filled rewrite row shows its field error and blocks Import");
            Find<TextBox>(dialog, "MaintenanceRewriteTo_" + rewriteId)!.Text = "/media";
            await Until(() => controller.State.Draft.PartialRewriteIds.Count == 0 && submit.IsEnabled, "row completed");
            Check(page.RequiresLeaveConfirmation, "an open dirty draft is protected by the leave guard");

            server.QueuedGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            Invoke(submit);
            await Until(() => controller.State.ImportPending && !submit.IsEnabled && AutomationProperties.GetName(submit) == "Importing...", "queued import held");
            Check(page.RequiresLeaveConfirmation, "an import in flight is protected by the leave guard");
            server.QueuedGate.SetResult();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await Until(() => !controller.State.ImportPending && !controller.State.Draft.IsDirty, "queued import settled");
            Check(await page.CanLeaveAsync(), "after success the leave guard completes without a prompt");
            var importBody = server.Bodies.ElementAt(server.Requests.ToList().IndexOf("POST /api/v2/admin/catalog/import-jobs"));
            Check(importBody == """{"local_path":"/catalog-seeds/","conflict_mode":"skip_existing","path_rewrites":[{"from":"/srv/media","to":"/media"}]}""",
                "the queued import sends exactly the local source, conflict mode and complete rewrite");

            // Import and wait with a lost answer: an unknown-outcome block survives close and reopen until acknowledged.
            server.SyncLost = true;
            dialog = await OpenImportAsync(page);
            Find<ComboBox>(dialog, "MaintenanceImportExecution")!.SelectedIndex = 1;
            await Until(() => controller.State.Draft.Execution == AdminCatalogImportExecution.Synchronous, "synchronous staged");
            submit = Find<Button>(dialog, "MaintenanceImportSubmit")!;
            await Until(() => submit.IsEnabled && submit.IsLoaded, "sync submit ready");
            Invoke(submit);
            await Until(() => controller.State.ImportOutcomeUnknown, "unknown outcome");
            await Until(() => Find<Border>(dialog, "MaintenanceImportUnknown") is { } unknown && IsShown(unknown) && !submit.IsEnabled, "unknown block shown");
            Check(server.Requests.Count(sent => sent == "POST /api/v2/admin/catalog/import") == 1, "a lost synchronous import is never resent");
            closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Hide();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            dialog = await OpenImportAsync(page);
            await Until(() => Find<Border>(dialog, "MaintenanceImportUnknown") is { } kept && IsShown(kept), "unknown block kept after reopen");
            Check(!Find<Button>(dialog, "MaintenanceImportSubmit")!.IsEnabled, "Import stays blocked after reopening");
            Invoke(Find<Button>(dialog, "MaintenanceImportAcknowledge")!);
            await Until(() => !controller.State.ImportOutcomeUnknown && Find<Button>(dialog, "MaintenanceImportSubmit")!.IsEnabled, "acknowledged");
            Check(true, "acknowledging the check re-enables Import");
            closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Hide();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Check(await page.CanLeaveAsync(), "closing the dialog leaves no draft for the leave guard");

            server.SyncLost = false;
            server.QueuedGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog = await OpenImportAsync(page);
            submit = Find<Button>(dialog, "MaintenanceImportSubmit")!;
            await Until(() => submit.IsLoaded && submit.IsEnabled && IsShown(submit), "departure submit ready");
            Invoke(submit);
            await Until(() => controller.State.ImportPending && server.Requests.Count(sent => sent == "POST /api/v2/admin/catalog/import-jobs") == 2, "departure import on wire");
            closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            page.Unloaded += (_, _) => pageUnloaded.TrySetResult();
            shell.Unloaded += (_, _) => shellUnloaded.TrySetResult();
            Check(outer.Navigate(typeof(Page)), "outer navigation retires the pending Maintenance page");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await pageUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            await shellUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            server.QueuedGate.TrySetResult();
            Check(AdminShellPage.TryOpenHref("/admin/maintenance").Kind == AdminRouteResolutionKind.Route, "Maintenance reopens through production routing");
            await Until(() => outer.Content is AdminShellPage, "replacement shell");
            shell = (AdminShellPage)outer.Content;
            inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminMaintenancePage { IsLoaded: true }, "replacement page");
            page = (AdminMaintenancePage)inner.Content;
            page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            page.CopyText = text => { copied.Enqueue(text); return true; };
            await Until(() => page.Controller?.State is { Exports.Loaded: true, Imports.Loaded: true, History.Loaded: true, ImportOutcomeUnknown: true }, "departure block restored");
            controller = page.Controller!;
            dialog = await OpenImportAsync(page);
            await Until(() => Find<Border>(dialog, "MaintenanceImportUnknown") is { } unknown && IsShown(unknown), "departure block rendered");
            Check(!Find<Button>(dialog, "MaintenanceImportSubmit")!.IsEnabled, "page departure and recreation cannot replay an unresolved import");
            Check(server.Requests.Count(sent => sent == "POST /api/v2/admin/catalog/import-jobs") == 2, "reopening sends no additional import");
            var acknowledge = Find<Button>(dialog, "MaintenanceImportAcknowledge")!;
            await Until(() => acknowledge.IsLoaded && acknowledge.IsEnabled && IsShown(acknowledge), "departure acknowledgment ready");
            Invoke(acknowledge);
            await Until(() => !controller.State.ImportOutcomeUnknown, "departure outcome acknowledged");
            closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Hide();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));

            // Authority retirement clears rows; a retained Download cannot act for a replacement admin.
            var retained = Find<Button>(page, "MaintenanceDownload_e-done")!;
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" });
            await Until(() => page.Controller?.State.Owner == null, "retired");
            await Until(() => page.Controller?.State.Exports.Items.Count == 0 && Find<Button>(page, "MaintenanceDownload_e-done") == null, "rows cleared");
            Check(true, "authority retirement clears every list");
            auth.SetCurrentUser(new UserInfo { Id = "2", Role = "admin", Username = "replacement-admin" });
            await Until(() => page.Controller?.State is { Owner.AccountId: "2", Exports.Loaded: true }, "replacement admin loaded");
            var before = launched.Count;
            Invoke(retained);
            await Task.Delay(200);
            Check(launched.Count == before, "a retained Download from the retired owner does not launch for the replacement admin");
        }
        finally
        {
            if (outer != null)
            {
                var shell = outer.Content as FrameworkElement;
                var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                if (shell is { IsLoaded: true }) shell.Unloaded += (_, _) => unloaded.TrySetResult(); else unloaded.TrySetResult();
                outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null;
                await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            }
            window.Content = previousContent;
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Maintenance fixture failures: " + string.Join("; ", Failures));
    }

    /// <summary>Opens Import Catalog through its button and returns the shown dialog once its content is loaded.</summary>
    private static async Task<ContentDialog> OpenImportAsync(AdminMaintenancePage page)
    {
        var button = Find<Button>(page, "MaintenanceImportCatalog")!;
        await Until(() => button.IsLoaded && button.IsEnabled, "Import Catalog ready");
        Invoke(button);
        ContentDialog? dialog = null;
        await Until(() => (dialog = OpenDialog(page)) != null && Find<Button>(dialog, "MaintenanceImportSubmit") is { IsLoaded: true }, "import dialog shown");
        await Until(() => page.Controller?.State.DialogOpen == true, "dialog state open");
        return dialog!;
    }

    private static ContentDialog? OpenDialog(FrameworkElement page) => VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
        .Select(popup => popup.Child).Where(child => child != null).SelectMany(Descendants).OfType<ContentDialog>().FirstOrDefault();

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is UIElement ui; current = VisualTreeHelper.GetParent(current))
            if (ui.Visibility != Visibility.Visible) return false;
        return element.IsLoaded;
    }

    private static T? Find<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == automationId);

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
