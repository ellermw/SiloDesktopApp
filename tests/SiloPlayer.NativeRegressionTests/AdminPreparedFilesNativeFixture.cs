using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
/// Real Frame/controls/dialog coverage for Manage &gt; Downloads &gt; Prepared Files on a synthetic server: every tab in its loading,
/// empty, failed-read (with a reachable Reload), populated and live states, and every dialog and write including a refused, a
/// conflicting and an unknown-outcome write. Official reference: Silo Server/WebUI 66b836f82.
/// </summary>
internal static class AdminPreparedFilesNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool value, string message) { if (!value) Failures.Add(message); }

    internal static async Task RunAsync(Window window)
    {
        Failures.Clear();
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var monitorApi = new AdminMonitorApi(client);
        var realtime = new Realtime();
        using var activity = new ServerActivityStore(monitorApi, authority, realtime);
        var navigation = new NavigationService();
        var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitorApi).AddSingleton(activity)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new AdminPreparedFilesRealtimeSource(realtime))
            .AddAdminPreparedFiles();
        await using var provider = services.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var priorServices = servicesField.GetValue(null);
        var priorContent = window.Content;
        var outer = new Frame();
        servicesField.SetValue(null, provider);
        try
        {
            Resize(window, 1440, 900);
            window.Content = outer;
            navigation.Frame = outer;
            var request = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["tab"] = "files", ["location"] = "node:9" });
            Check(AdminShellPage.CanOpen(request), "registration makes Prepared Files openable");
            Check(!AdminRouteCatalog.TryResolve("/admin/downloads", out var resolved) || resolved.Kind != AdminRouteResolutionKind.Unsupported, "the route is not listed as unsupported");
            Check(AdminShellPage.Open(request), "registered route enters the Admin shell");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var frame = (Frame)Field(shell, "_frame")!;
            await Until(() => frame.Content is AdminPreparedFilesPage page && page.Controller.State.Files.Loaded && page.Controller.State.Storage.Loaded && page.Controller.State.PreparationsLoaded);
            var prepared = (AdminPreparedFilesPage)frame.Content;
            Check(frame.ContentTransitions == null || frame.ContentTransitions.Count == 0, "page navigation has no slide transition");
            Check(server.Requests.Contains("GET /api/v2/admin/downloads/storage/files?location=node%3A9&sort=size&limit=50"), "the URL location opens the official filtered file query");
            Check(server.Requests.Any(r => r.StartsWith("GET /api/v2/admin/downloads/preparations?limit=200", StringComparison.Ordinal)), "the queue is read on open for the tab count");
            Check(realtime.Subscribed.SequenceEqual(["download_preparations", "downloads"]) && realtime.Leases == 1, "the page leases both official realtime channels once");
            Check(!Texts(prepared).Any(t => t.StartsWith("web.", StringComparison.Ordinal) || t.StartsWith("configure.native", StringComparison.Ordinal)), "no unresolved message keys are rendered");
            var tabs = new[] { "storage", "preparation", "files", "devices", "history" };
            Check(tabs.All(t => Find<Button>(shell, "PreparedFilesTab_" + t) != null), "all five official tabs render");
            Check(Texts(prepared).Contains("Downloads") && Texts(prepared).Contains("Prepared files") && Texts(prepared).Contains("Device copies"), "official title and tab labels render");
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesRow_art-cached") != null && Find<FrameworkElement>(shell, "PreparedFilesRow_art-use") != null);
            Check(Texts(prepared).Contains("Dune") && Texts(prepared).Contains("1080p H264") && Texts(prepared).Contains("10 Mbps"), "title and recipe columns render with the official labels");
            await CaptureAsync(outer, "admin-prepared-files-wide.png");

            // ---- Leaving a filtered tab never waits on, or starts, a read of it -----------------------------------
            int FileReads() { lock (server.Requests) return server.Requests.Count(r => r.StartsWith("GET /api/v2/admin/downloads/storage/files", StringComparison.Ordinal)); }
            server.StallFiles = true;
            var fileReadsBefore = FileReads();
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await Until(() => prepared.Controller.State.ActiveTab == "history" && Find<FrameworkElement>(shell, "PreparedFilesHistoryRow_event-1") != null, 4000);
            Check(FileReads() == fileReadsBefore && prepared.Controller.State.FileQuery.Location == null, "leaving a filtered Files tab resets it without a read and without waiting for one");
            server.StallFiles = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_files"));
            await Until(() => prepared.Controller.State.Files.Loaded && Find<FrameworkElement>(shell, "PreparedFilesRow_art-cached") != null);

            // ---- A live refresh keeps the rows, the controls and the selection the administrator is using ---------
            var keepBox = await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-cached");
            var keepRow = Find<FrameworkElement>(shell, "PreparedFilesRow_art-cached");
            await ToggleAsync(keepBox);
            Check(keepBox!.IsChecked == true, "a check box toggles once");
            var readsBeforeRefresh = FileReads();
            realtime.Raise("downloads", "download.changed", "{}");
            await Until(() => FileReads() > readsBeforeRefresh && !prepared.Controller.State.Files.Loading);
            Check(ReferenceEquals(Find<CheckBox>(shell, "PreparedFilesSelect_art-cached"), keepBox) && keepBox.IsChecked == true && ReferenceEquals(Find<FrameworkElement>(shell, "PreparedFilesRow_art-cached"), keepRow),
                "a live refresh keeps the row, its check box and the selection");
            Check(keepBox.Focus(FocusState.Keyboard), "the check box takes focus");
            realtime.Raise("downloads", "download.changed", "{}");
            await Task.Delay(2600);
            realtime.Raise("downloads", "download.changed", "{}");
            await Task.Delay(400);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), keepBox), "a refresh leaves keyboard focus on the check box");
            await ToggleAsync(keepBox);
            Check(keepBox.IsChecked != true, "the same check box toggles back");

            // ---- Titles are shown exactly as they are in the confirmation that names them --------------------------
            var angleBox = await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-angle");
            await ToggleAsync(angleBox);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeleteSelected"));
            await Until(() => DialogButton(window, "PrimaryButton") != null);
            Check(PopupRunTexts(window).Any(t => t.Contains("Alien <Directors>", StringComparison.Ordinal)), "a title with angle brackets is shown exactly in the delete confirmation");
            await CloseDialogAsync(window);
            await ToggleAsync(angleBox);

            // ---- Prepared files: selection, delete dialog, refusal, unknown outcome, failed read -------------------
            await ToggleAsync(await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-cached"));
            await Until(() => Find<Button>(shell, "PreparedFilesDeleteSelected") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeleteSelected"));
            Button? confirm = null;
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(PopupTexts(window).Contains("Delete 1 prepared file?"), "delete dialog carries the official title");
            Check(PopupTexts(window).Any(t => t.Contains("This can't be undone", StringComparison.Ordinal)), "delete dialog states that it cannot be undone");
            server.DeleteMode = "refuse";
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/files/delete") && !prepared.Controller.State.ActionPending);
            Check(server.Bodies.Last(b => b.Contains("include_in_use", StringComparison.Ordinal)) == "{\"ids\":[\"art-cached\"],\"include_in_use\":false}", "delete sends exactly the confirmed id");
            await Until(() => NoticeText(shell).Contains("Synthetic refusal detail.", StringComparison.Ordinal));
            Check(!prepared.Controller.State.DestructiveBlocked, "a refused delete does not block a retry");
            Check(server.Requests.Count(r => r == "POST /api/v2/admin/downloads/storage/files/delete") == 1, "delete is sent once");

            server.DeleteMode = "unknown";
            // The re-read after an unknown outcome fails too, so the block stays until the administrator reloads.
            server.FailFiles = true; server.FailStorage = true;
            await ToggleAsync(await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-cached"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeleteSelected"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            await InvokeAsync(confirm);
            await Until(() => prepared.Controller.State.DestructiveBlocked && !prepared.Controller.State.ActionPending);
            Check(server.Requests.Count(r => r == "POST /api/v2/admin/downloads/storage/files/delete") == 2, "an unknown outcome is not replayed");
            Check(prepared.Controller.State.LastAction?.Outcome == AdminActionOutcome.Unknown && Find<FrameworkElement>(shell, "PreparedFilesBlocked") != null, "an unknown outcome is announced with a Reload");
            await ToggleAsync(await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-cached"));
            var blockedDelete = Find<Button>(shell, "PreparedFilesDeleteSelected");
            Check(blockedDelete is { IsEnabled: false } || blockedDelete == null, "destructive actions stay off until the list is read again");
            server.DeleteMode = "ok"; server.FailFiles = false; server.FailStorage = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesBlockedReload"));
            await Until(() => !prepared.Controller.State.DestructiveBlocked);

            // Some deleted and some failed is an error notice (the official error toast), not a success.
            server.DeleteMode = "partial";
            await InvokeAsync(Find<Button>(shell, "PreparedFilesClearSelection"));
            await ToggleAsync(await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-cached"));
            await ToggleAsync(await WaitFind<CheckBox>(shell, "PreparedFilesSelect_art-angle"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeleteSelected"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Count(r => r == "POST /api/v2/admin/downloads/storage/files/delete") == 3 && !prepared.Controller.State.ActionPending);
            await Until(() => NoticeText(shell).Contains("could not be deleted", StringComparison.Ordinal));
            Check(prepared.Controller.State.LastAction is { Outcome: AdminActionOutcome.Partial, IsError: true } && NoticeIsError(shell), "a partly failed delete is shown with the error icon");
            server.DeleteMode = "ok";
            await InvokeAsync(Find<Button>(shell, "PreparedFilesNoticeDismiss"));

            server.FailFiles = true;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_files"));
            await Until(() => prepared.Controller.State.Files.Error != null && Find<Button>(shell, "PreparedFilesFilesReload") != null);
            Check(Texts(prepared).Contains("Couldn't load prepared files.") || Texts(prepared).Any(t => t.Contains("load", StringComparison.OrdinalIgnoreCase)), "a failed file read names the failure");
            server.FailFiles = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesFilesReload"));
            await Until(() => prepared.Controller.State.Files.Error == null && prepared.Controller.State.Files.Items.Count > 0);

            // ---- Storage -----------------------------------------------------------------------------------------
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_storage"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesStorageCard_node:9") != null);
            Check(Texts(prepared).Contains("On disk") && Texts(prepared).Contains("Untracked") && Texts(prepared).Contains("Freed, 30 days"), "storage tiles carry the official labels");
            Check(Find<FrameworkElement>(shell, "PreparedFilesWarningBanner_fill:node:9") != null && Find<FrameworkElement>(shell, "PreparedFilesWarningBanner_untracked:node:9") != null
                && Find<FrameworkElement>(shell, "PreparedFilesWarningBanner_stale") != null, "disk, untracked and stale-device warnings show");
            Check(Find<FrameworkElement>(shell, "PreparedFilesMeter") != null, "the disk meter renders");
            await CaptureAsync(outer, "admin-prepared-files-storage.png");
            await InvokeAsync(Find<Button>(shell, "PreparedFilesCleanup_node:9"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/locations/node%3A9/cleanup") && !prepared.Controller.State.ActionPending);
            Check(NoticeText(shell).StartsWith("Freed ", StringComparison.Ordinal) || NoticeText(shell).StartsWith("Nothing to clean up", StringComparison.Ordinal), "clean-up reports its official result");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesWarning_untracked:node:9"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(PopupTexts(window).Any(t => t.Contains("synthetic-node", StringComparison.Ordinal)), "untracked dialog names the location");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/locations/node%3A9/untracked/delete") && !prepared.Controller.State.ActionPending);

            // Node location editor: only changed fields, refusal, stale validator and unknown outcome.
            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_node:9"));
            await Until(() => FindDialog<TextBox>(window, "PreparedFilesNodeDirectory") != null);
            await CloseDialogAsync(window);
            Check(!server.Requests.Any(r => r.StartsWith("PUT ", StringComparison.Ordinal)), "an untouched editor sends nothing");
            server.NodeMode = "refuse";
            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_node:9"));
            await Until(() => FindDialog<TextBox>(window, "PreparedFilesNodeDirectory") != null);
            FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")!.Text = "relative";
            await Task.Delay(100);
            Check(DialogButton(window, "PrimaryButton") is { IsEnabled: false }, "a relative directory cannot be saved");
            FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")!.Text = "/prepared/new";
            await Until(() => DialogButton(window, "PrimaryButton") is { IsEnabled: true });
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 1 && !prepared.Controller.State.ActionPending);
            Check(server.Bodies.Last(b => b.Contains("download_artifact_dir_override", StringComparison.Ordinal)) == "{\"download_artifact_dir_override\":\"/prepared/new\"}", "node save sends only the changed field");
            Check(server.LastIfMatch == "\"node-v1\"", "node save uses the exact strong validator");
            await Until(() => FindDialog<TextBlock>(window, "PreparedFilesNodeSaveError")?.Text.Contains("Synthetic node refusal.", StringComparison.Ordinal) == true);
            Check(FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")?.Text == "/prepared/new", "a refused node save keeps the draft");

            server.NodeMode = "stale";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 2 && !prepared.Controller.State.ActionPending);
            await Until(() => FindDialog<Button>(window, "PreparedFilesNodeReload") is { Visibility: Visibility.Visible });
            Check(FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")?.Text == "/prepared/new", "a stale validator keeps the draft and offers Reload");

            server.NodeMode = "unknown"; server.FailStorage = true;
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 3 && !prepared.Controller.State.ActionPending);
            Check(prepared.Controller.State.LastAction?.Outcome == AdminActionOutcome.Unknown, "an unknown node outcome is reported");
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Task.Delay(150);
            Check(server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 3, "a node write is not replayed after an unknown outcome");
            server.NodeMode = "ok"; server.FailStorage = false;
            await CloseDialogAsync(window);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesBlockedReload"));
            await Until(() => !prepared.Controller.State.DestructiveBlocked);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_node:9"));
            await Until(() => FindDialog<TextBox>(window, "PreparedFilesNodeDirectory") != null);
            FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")!.Text = "/prepared/new";
            await Until(() => DialogButton(window, "PrimaryButton") is { IsEnabled: true });
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 4);
            await Until(() => DialogButton(window, "PrimaryButton") == null && DialogButton(window, "CloseButton") == null);
            await Task.Delay(400);

            // Dismissing the editor while its save is in flight: an unknown outcome that blocks writes, and the page is never stuck.
            server.NodeMode = "hang";
            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_node:9"));
            await Until(() => FindDialog<TextBox>(window, "PreparedFilesNodeDirectory") != null);
            FindDialog<TextBox>(window, "PreparedFilesNodeDirectory")!.Text = "/prepared/dismissed";
            await Until(() => DialogButton(window, "PrimaryButton") is { IsEnabled: true });
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Count(r => r == "PUT /api/v2/admin/nodes/9") == 5 && prepared.Controller.State.ActionPending);
            (Field(prepared, "_dialog") as ContentDialog)?.Hide();
            // The dismissal aborts the request at once; should WinUI hold the dialog open until its pending save answers, the page's own 30 s deadline ends the same way.
            await Until(() => !prepared.Controller.State.ActionPending && prepared.Controller.State.DestructiveBlocked, 40000);
            if (DialogButton(window, "CloseButton") != null) await CloseDialogAsync(window);
            await Until(() => DialogButton(window, "PrimaryButton") == null && DialogButton(window, "CloseButton") == null);
            Check(prepared.Controller.State.LastAction?.Outcome == AdminActionOutcome.Unknown && Find<FrameworkElement>(shell, "PreparedFilesBlocked") != null, "a save aborted by dismissing the editor is an unknown outcome with a reachable Reload");
            Check(Find<Button>(shell, "PreparedFilesCleanup_node:9") is { IsEnabled: false }, "writes stay off until the nodes are read again");
            server.NodeMode = "ok";
            await InvokeAsync(Find<Button>(shell, "PreparedFilesBlockedReload"));
            await Until(() => !prepared.Controller.State.DestructiveBlocked);
            Check(Find<Button>(shell, "PreparedFilesCleanup_node:9") is { IsEnabled: true }, "reloading lifts the block and the page is usable again");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_server"));
            await Until(() => PopupTexts(window).Contains("The server's prepared-file directory and the default budget are server settings."));
            await CloseDialogAsync(window);

            server.FailStorage = true;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_storage"));
            server.FailStorage = false;

            // ---- Preparation -------------------------------------------------------------------------------------
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_preparation"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running") != null);
            Check(Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-queued") != null && Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-failed") != null, "running, queued and failed jobs render");
            Check(Texts(prepared).Contains("Encoding") && Texts(prepared).Contains("Queued") && Texts(prepared).Contains("Failed (24 h)"), "state filters use the official labels");
            Check(Texts(prepared).Any(t => t.StartsWith("25%", StringComparison.Ordinal)), "running progress shows its percent");
            var workerChip = Find<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(shell, "PreparedFilesPreparationWorker_node:9");
            Check(workerChip != null && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(workerChip) == "Node synthetic-node 1",
                "the worker chip is announced as separate words with its job count, not run together (was: " + (workerChip == null ? "missing" : Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(workerChip)) + ")");
            await CaptureAsync(outer, "admin-prepared-files-preparation.png");
                        var filter = Find<TextBox>(shell, "PreparedFilesPreparationSearch")!;
            await Task.Delay(200);
                        Check(filter.Focus(FocusState.Programmatic), "the filter takes focus");
            filter.Text = "queued";
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running") == null);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), filter), "typing in the filter keeps keyboard focus (focused: " + FocusManager.GetFocusedElement(window.Content.XamlRoot)?.GetType().Name + ", loaded: " + filter.IsLoaded + ")");
            filter.Text = "";
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running") != null);

            realtime.Raise("download_preparations", "download_preparation.progress", "{\"id\":\"art-running\",\"progress\":{\"encoded_seconds\":75,\"duration_seconds\":100,\"speed\":2,\"updated_at\":\"2026-10-08T09:31:00Z\"}}");
            await Until(() => Texts(prepared).Any(t => t.StartsWith("75%", StringComparison.Ordinal)));
            Check(true, "a progress event patches the running row in place");

            // ---- Progress readings never replace the control being used --------------------------------------------
            var pauseButton = Find<Button>(shell, "PreparedFilesPause_art-running")!;
            var cancelButton = Find<Button>(shell, "PreparedFilesCancel_art-running")!;
            var detailsButton = Find<Button>(shell, "PreparedFilesPreparationDetails_art-running")!;
            var selectBox = Find<CheckBox>(shell, "PreparedFilesPreparationSelect_art-running")!;
            var runningRow = Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running")!;
            string Progress(int encoded) => "{\"id\":\"art-running\",\"progress\":{\"encoded_seconds\":" + encoded + ",\"duration_seconds\":100,\"speed\":2,\"updated_at\":\"2026-10-08T09:31:00Z\"}}";
            Check(pauseButton.Focus(FocusState.Keyboard), "the Pause button takes focus");
            for (var encoded = 76; encoded < 100; encoded++)
            {
                realtime.Raise("download_preparations", "download_preparation.progress", Progress(encoded));
                await Task.Delay(35);
            }
            await Until(() => Texts(prepared).Any(t => t.StartsWith("99%", StringComparison.Ordinal)));
            Check(ReferenceEquals(Find<Button>(shell, "PreparedFilesPause_art-running"), pauseButton) && ReferenceEquals(Find<Button>(shell, "PreparedFilesCancel_art-running"), cancelButton)
                && ReferenceEquals(Find<Button>(shell, "PreparedFilesPreparationDetails_art-running"), detailsButton) && ReferenceEquals(Find<CheckBox>(shell, "PreparedFilesPreparationSelect_art-running"), selectBox)
                && ReferenceEquals(Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running"), runningRow), "24 progress readings replace no control and no row");
            Check(ReferenceEquals(FocusManager.GetFocusedElement(window.Content.XamlRoot), pauseButton), "keyboard focus stays on the Pause button while progress arrives");

            using (var storm = new CancellationTokenSource())
            {
                var readings = Task.Run(async () =>
                {
                    for (var n = 0; !storm.IsCancellationRequested; n++)
                    {
                        realtime.Raise("download_preparations", "download_preparation.progress", Progress(76 + n % 23));
                        await Task.Delay(20);
                    }
                });
                await ToggleAsync(selectBox);
                Check(selectBox.IsChecked == true && prepared.Controller.State.Preparations.Count > 0 && Find<FrameworkElement>(shell, "PreparedFilesPreparationSelection") != null, "a check box toggle lands while progress arrives");
                await InvokeAsync(Find<Button>(shell, "PreparedFilesCancelSelected"));
                await Until(() => DialogButton(window, "PrimaryButton") != null);
                Check(PopupTexts(window).Any(t => t.Contains("Encoding stops and its progress is lost.", StringComparison.Ordinal)), "Cancel selected opens its confirmation while progress arrives");
                await CloseDialogAsync(window);
                await InvokeAsync(cancelButton);
                await Until(() => DialogButton(window, "PrimaryButton") != null);
                Check(PopupTexts(window).Any(t => t.Contains("Encoding stops and its progress is lost.", StringComparison.Ordinal)), "the row's Cancel opens its confirmation while progress arrives");
                await CloseDialogAsync(window);
                await ToggleAsync(selectBox);
                storm.Cancel();
                await readings;
            }
            Check(selectBox.IsChecked != true && ReferenceEquals(Find<Button>(shell, "PreparedFilesCancel_art-running"), cancelButton), "the same controls are still in use after the readings stop");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesPreparationDetails_art-running"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesPreparationDetailsPanel_art-running") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesPreparationFfmpeg_art-running"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesPreparationConsole_art-running") != null && Texts(prepared).Contains("synthetic ffmpeg line"));
            Check(server.Requests.Any(r => r.Contains("/api/v2/admin/logs/app?playback_session_id=prepare-1&component=ffmpeg&limit=12", StringComparison.Ordinal)), "the console reads the 12 newest FFmpeg rows of the job");
            await InvokeAsync(Find<Button>(shell, "PreparedFilesPreparationDetails_art-running"));

            await InvokeAsync(Find<Button>(shell, "PreparedFilesPause_art-running"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/preparations/pause") && !prepared.Controller.State.ActionPending);
            Check(server.Bodies.Last(b => b.StartsWith("{\"ids\":", StringComparison.Ordinal)) == "{\"ids\":[\"art-running\"]}", "pause sends exactly the row's job");
            await InvokeAsync(Find<Button>(shell, "PreparedFilesCancel_art-failed"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(PopupTexts(window).Any(t => t.StartsWith("Remove the failed job for", StringComparison.Ordinal)), "a failed job offers the official remove confirmation");
            await CloseDialogAsync(window);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesCancel_art-running"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(PopupTexts(window).Any(t => t.Contains("Encoding stops and its progress is lost.", StringComparison.Ordinal)), "cancel confirmation names lost encoding work");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/preparations/cancel") && !prepared.Controller.State.ActionPending);

            server.FailPreparations = true;
            realtime.Raise("download_preparations", "download_preparation.queued", "{}");
            await Task.Delay(300);
            Check(Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-queued") != null, "a failed background refresh keeps the rows on screen");
            Check(Find<Button>(shell, "PreparedFilesPreparationReload") != null, "the failed refresh offers a reachable Reload");
            server.FailPreparations = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesPreparationReload"));
            await Until(() => prepared.Controller.State.PreparationsError == null);

            // ---- Device copies -----------------------------------------------------------------------------------
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_devices"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesDeviceExpand_7_p1_phone") != null);
            Check(Texts(prepared).Contains("Pixel") && Texts(prepared).Contains("android"), "device rows render");
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeviceExpand_7_p1_phone"));
            await Until(() => Find<CheckBox>(shell, "PreparedFilesEntrySelect_dl-1") != null);
            Check(Find<CheckBox>(shell, "PreparedFilesEntrySelect_dl-2") is { IsEnabled: false }, "a revoked entry cannot be selected again");
            await InvokeAsync(Find<HyperlinkButton>(shell, "PreparedFilesRevokeEntry_dl-1"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(PopupTexts(window).Any(t => t.Contains("Dune", StringComparison.Ordinal)), "revoke confirmation names the download");
            server.RevokeMode = "unknown"; server.FailStorage = true;
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Count(r => r == "POST /api/v2/admin/downloads/revoke") == 1 && !prepared.Controller.State.ActionPending);
            Check(prepared.Controller.State.DestructiveBlocked && prepared.Controller.State.LastAction?.Outcome == AdminActionOutcome.Unknown, "an unknown revoke blocks further destructive actions");
            server.RevokeMode = "ok"; server.FailStorage = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesBlockedReload"));
            await Until(() => !prepared.Controller.State.DestructiveBlocked);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesRevokeAll_7_p1_phone"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) != null);
            Check(FindDialog<CheckBox>(window, "PreparedFilesPauseMonitors") is { IsChecked: true }, "whole-device revoke pauses monitors by default");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Count(r => r == "POST /api/v2/admin/downloads/revoke") == 2 && !prepared.Controller.State.ActionPending);
            Check(server.Bodies.Last(b => b.Contains("pause_monitors", StringComparison.Ordinal)) == "{\"user_id\":\"7\",\"profile_id\":\"p1\",\"device_id\":\"phone\",\"pause_monitors\":true,\"reason\":\"\"}", "device revoke sends the official body");

            server.FailDevices = true;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_devices"));
            await Until(() => prepared.Controller.State.Devices.Error != null && Find<Button>(shell, "PreparedFilesDevicesReload") != null);
            server.FailDevices = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDevicesReload"));
            await Until(() => prepared.Controller.State.Devices.Error == null && prepared.Controller.State.Devices.Items.Count > 0);

            // Stale devices open pre-filtered from the Storage warning.
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_storage"));
            await Until(() => Find<Button>(shell, "PreparedFilesWarning_stale") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesWarning_stale"));
            await Until(() => prepared.Controller.State.ActiveTab == "devices" && prepared.Controller.State.DeviceQuery.Stale);
            Check(server.Requests.Any(r => r.Contains("/api/v2/admin/downloads/devices?stale=true", StringComparison.Ordinal)), "the stale warning opens Device copies with the stale filter on");

            // ---- History -----------------------------------------------------------------------------------------
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesHistoryRow_event-1") != null);
            Check(Texts(prepared).Contains("Expired from cache") && Texts(prepared).Contains("Automatic"), "history renders the official reason and actor labels");
            Check(server.Requests.Any(r => r.Contains("/api/v2/admin/downloads/storage/events?days=30&limit=50", StringComparison.Ordinal)), "history defaults to the last 30 days");
            server.FailHistory = true;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_files"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesTab_history"));
            await Until(() => prepared.Controller.State.History.Error != null && Find<Button>(shell, "PreparedFilesHistoryReload") != null);
            server.FailHistory = false;
            await InvokeAsync(Find<Button>(shell, "PreparedFilesHistoryReload"));
            await Until(() => prepared.Controller.State.History.Error == null);

            Resize(window, 560, 900);
            await Task.Delay(250);
            await CaptureAsync(outer, "admin-prepared-files-compact.png");
            Resize(window, 1440, 900);

            // ---- Authority change --------------------------------------------------------------------------------
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "admin", Role = "user" });
            await Until(() => prepared.Controller.State.Owner == null);
            Check(prepared.Controller.State.Files.Items.Count == 0 && prepared.Controller.State.Devices.Items.Count == 0 && prepared.Controller.State.Preparations.Count == 0, "authority retirement clears every retained row");
            await Until(() => realtime.Leases == 0);
            Check(realtime.Leases == 0, "the realtime leases are released when authority is lost");
        }
        finally
        {
            window.Content = priorContent;
            for (var waited = 0; waited < 1000 && outer.IsLoaded; waited += 25) await Task.Delay(25);
            servicesField.SetValue(null, priorServices);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Admin Prepared Files fixture failed: " + string.Join("; ", Failures));
        Program.Log("PASS: Admin Prepared Files native fixture completed; synthetic server only, no live server or complete visual parity certification.");
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        private int _leases;
        public int Leases => Volatile.Read(ref _leases);
        public string[] Subscribed { get; private set; } = [];
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels)
        {
            // Other consumers (the activity store) lease their own channels; only this page's pair is recorded.
            if (channels.Contains("downloads")) { Subscribed = channels; Interlocked.Increment(ref _leases); return new Release(() => Interlocked.Decrement(ref _leases)); }
            return new Release(() => { });
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        public void Raise(string channel, string name, string json) => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    private const string Storage = """{"locations":[{"key":"server","kind":"server","name":"Server","enabled":true,"online":true,"dir":"/srv/prepared","dir_source":"setting","in_use_files":1,"in_use_bytes":10000000000,"cached_files":1,"cached_bytes":20000000000,"waiting_downloads":1,"stale_waiting_bytes":0,"untracked_files":0,"untracked_bytes":0,"budget_bytes":100000000000,"budget_source":"setting","cleanup_backlog":0,"storage_full":false,"replicas_disagree":false,"usage":{"measured_at":"2026-10-08T09:30:00Z","files":2,"bytes":30000000000,"partial_files":0,"partial_bytes":0,"other_bytes":0,"fs_used_bytes":60000000000,"fs_total_bytes":100000000000,"shares_scratch":false,"ephemeral":false,"stale":false}},{"key":"node:9","kind":"node","node_id":"9","name":"synthetic-node","enabled":true,"online":true,"dir":"","dir_source":"default","in_use_files":0,"in_use_bytes":0,"cached_files":1,"cached_bytes":5000000000,"waiting_downloads":0,"stale_waiting_bytes":2000000000,"untracked_files":2,"untracked_bytes":3000000,"budget_bytes":0,"budget_source":"none","cleanup_backlog":0,"storage_full":false,"replicas_disagree":false,"usage":{"measured_at":"2026-10-08T09:30:00Z","files":3,"bytes":5003000000,"partial_files":0,"partial_bytes":0,"other_bytes":0,"fs_used_bytes":90000000000,"fs_total_bytes":100000000000,"shares_scratch":true,"ephemeral":false,"stale":false}}],"cache_hours":72,"disk_ceiling_percent":85,"default_budget_bytes":100000000000,"stale_device_days":14,"preparing_jobs":1,"freed_last_30_days_bytes":5000000000}""";
    private const string Files = """{"items":[{"id":"art-cached","state":"cached","format":"transcode","location":"node:9","location_name":"synthetic-node","media_file_id":"42","title":"Dune","media_type":"movie","year":2021,"container":"mp4","video_codec":"h264","audio_codec":"aac","resolution":"1080p","bitrate_kbps":10000,"bytes":20000000,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","expires_at":"2099-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0},{"id":"art-use","state":"in_use","format":"remux","location":"server","location_name":"Server","media_file_id":"43","title":"Heat","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","bytes":30000000,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","waiting":1,"downloading":0,"finished":0,"stale_waiting":1,"oldest_waiting":{"device_name":"Pixel","username":"maya"}},{"id":"art-angle","state":"cached","format":"remux","location":"server","location_name":"Server","media_file_id":"45","title":"Alien <Directors>","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","bytes":40000000,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","expires_at":"2099-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0}],"page":{"has_more":false}}""";
    private const string Preparations = """{"counts":{"running":1,"queued":1,"retrying":0,"paused":0,"failed_recent":1},"items":[{"id":"art-running","state":"running","format":"transcode","log_session_id":"prepare-1","media_file_id":"42","content_id":"movie:dune","media_title":"Dune","media_type":"movie","attempts":1,"max_attempts":3,"created_at":"2026-10-08T09:30:00Z","started_at":"2026-10-08T09:30:00Z","progress_unavailable":false,"progress":{"encoded_seconds":25,"duration_seconds":100,"speed":2,"updated_at":"2026-10-08T09:30:30Z"},"source":{"container":"mkv","video_codec":"hevc","resolution":"2160p","hdr":true,"audio_tracks":[{"codec":"truehd","channels":8}],"file_size":2147483648,"duration_seconds":100},"output":{"container":"mp4","video_codec":"h264","resolution":"1080p","bitrate_kbps":10000,"audio_codec":"aac","all_audio_tracks":false},"worker":{"kind":"node","node_id":"9","name":"synthetic-node"},"requesters":[{"user_id":"7","username":"maya","profile_id":"p1","profile_name":"Maya","device_id":"phone","device_name":"Pixel","status":"preparing"}]},{"id":"art-queued","state":"queued","format":"remux","log_session_id":"prepare-2","media_file_id":"43","media_title":"Heat","media_type":"movie","queue_position":1,"attempts":0,"max_attempts":3,"created_at":"2026-10-08T09:30:00Z","progress_unavailable":false,"source":{"container":"mkv","audio_tracks":[],"file_size":1000,"duration_seconds":100},"output":{"container":"mp4","video_codec":"copy","audio_codec":"copy","all_audio_tracks":false},"requesters":[]},{"id":"art-failed","state":"failed","format":"transcode","log_session_id":"prepare-3","media_file_id":"44","media_title":"Alien","media_type":"movie","attempts":3,"max_attempts":3,"error":"synthetic encoder failure","created_at":"2026-10-08T09:30:00Z","failed_at":"__FAILED_AT__","progress_unavailable":false,"source":{"container":"mkv","audio_tracks":[],"file_size":1000,"duration_seconds":100},"output":{"container":"mp4","video_codec":"h264","audio_codec":"aac","all_audio_tracks":false},"requesters":[]}]}""";
    private const string Devices = """{"items":[{"user_id":"7","username":"maya","profile_id":"p1","profile_name":"Maya","device_id":"phone","device_name":"Pixel","platform":"android","last_seen_at":"2026-09-08T09:30:00Z","stale":true,"copies":2,"finished":1,"waiting":1,"failed":0,"revoked":1,"bytes_on_device":20000000,"monitors":1}],"page":{"has_more":false}}""";
    private const string Entries = """{"items":[{"id":"dl-1","profile_id":"p1","device_id":"phone","content_id":"movie:dune","title":"Dune","media_type":"movie","status":"completed","quality":"10mbps","effective_quality":"10mbps","delivery_format":"transcode","target_bitrate_kbps":10000,"file_size":20000000,"created_at":"2026-10-08T09:30:00Z","updated_at":"2026-10-08T09:30:00Z","completed_at":"2026-10-08T09:30:00Z","status_event_at":null,"user_id":"7","username":"maya","device_name":"Pixel","location":"node:9","location_name":"synthetic-node","episode":null,"created_client_ip":null,"last_client_ip":null},{"id":"dl-2","profile_id":"p1","device_id":"phone","content_id":"movie:heat","title":"Heat","media_type":"movie","status":"revoked","quality":"original","effective_quality":"original","delivery_format":"remux","target_bitrate_kbps":0,"file_size":30000000,"created_at":"2026-10-08T09:30:00Z","updated_at":"2026-10-08T09:30:00Z","completed_at":null,"status_event_at":null,"user_id":"7","username":"maya","device_name":"Pixel","episode":null,"created_client_ip":null,"last_client_ip":null}],"page":{"has_more":false}}""";
    private const string Events = """{"items":[{"id":"event-1","reason":"cache_expired","location":"node:9","location_name":"synthetic-node","occurred_at":"2026-10-08T09:30:00Z","count":1,"bytes":20,"titles":["Dune"]}],"page":{"has_more":false}}""";
    private const string Node = """{"id":"9","config_etag":"\"node-v1\"","name":"synthetic-node","type":"transcode","url":"https://node.invalid","enabled":true,"healthy":true,"active_jobs":0,"egress_kbps":0,"created_at":"2026-01-01T00:00:00Z","env_managed":[]}""";

    private sealed class Server : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal List<string> Bodies { get; } = [];
        internal string? LastIfMatch { get; private set; }
        internal string DeleteMode { get; set; } = "ok";
        internal string NodeMode { get; set; } = "ok";
        internal string RevokeMode { get; set; } = "ok";
        internal bool FailStorage, FailFiles, FailPreparations, FailDevices, FailHistory, StallFiles;

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) =>
            Json("{\"type\":\"https://fixture.invalid/problems/" + type + "\",\"title\":\"Synthetic\",\"status\":" + (int)status + ",\"detail\":\"" + detail + "\"}", status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) { Requests.Add(request.Method + " " + path + request.RequestUri.Query); Bodies.Add(body); }
            if (request.Headers.IfMatch.SingleOrDefault() is { } match) LastIfMatch = match.ToString();
            var write = request.Method != HttpMethod.Get;
            // A read that never answers, and a save that is still in flight when its editor is dismissed.
            if (StallFiles && path == "/api/v2/admin/downloads/storage/files") await Task.Delay(Timeout.Infinite, ct);
            if (NodeMode == "hang" && write && path == "/api/v2/admin/nodes/9") await Task.Delay(Timeout.Infinite, ct);
            HttpResponseMessage response = path switch
            {
                "/api/v2/admin/downloads/storage" => FailStorage ? Problem(HttpStatusCode.InternalServerError, "internal", "synthetic read failure") : Json(Storage),
                "/api/v2/admin/downloads/storage/files" => FailFiles ? Problem(HttpStatusCode.InternalServerError, "internal", "synthetic read failure") : Json(Files),
                "/api/v2/admin/downloads/preparations" => FailPreparations ? Problem(HttpStatusCode.InternalServerError, "internal", "synthetic read failure") : Json(Preparations.Replace("__FAILED_AT__", DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))),
                "/api/v2/admin/downloads/devices" => FailDevices ? Problem(HttpStatusCode.InternalServerError, "internal", "synthetic read failure") : Json(Devices),
                "/api/v2/admin/downloads/entries" => Json(Entries),
                "/api/v2/admin/downloads/storage/events" => FailHistory ? Problem(HttpStatusCode.InternalServerError, "internal", "synthetic read failure") : Json(Events),
                "/api/v2/admin/logs/app" => Json("""{"items":[{"id":"1","timestamp":"2026-10-08T09:30:00Z","level":"info","component":"ffmpeg","message":"ffmpeg stderr","attrs":{"ffmpeg_line":"synthetic ffmpeg line"}}],"page":{"has_more":false}}"""),
                "/api/v2/admin/nodes" when !write => Json("{\"items\":[" + Node + "],\"page\":{\"has_more\":false}}"),
                "/api/v2/admin/nodes/9" => NodeMode switch
                {
                    "refuse" => Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", "Synthetic node refusal."),
                    "stale" => Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", "Synthetic stale validator."),
                    "unknown" => throw new IOException("connection closed after dispatch"),
                    _ => NodeAck(),
                },
                "/api/v2/admin/downloads/storage/files/delete" => DeleteMode switch
                {
                    "refuse" => Problem(HttpStatusCode.Forbidden, "forbidden", "Synthetic refusal detail."),
                    "unknown" => throw new IOException("connection closed after dispatch"),
                    "partial" => PartialDelete(body),
                    _ => Json("""{"results":[{"id":"art-cached","outcome":"deleted","bytes":20000000}]}"""),
                },
                "/api/v2/admin/downloads/storage/locations/node%3A9/cleanup" => Json("""{"freed_bytes":0}"""),
                "/api/v2/admin/downloads/storage/locations/node%3A9/untracked/delete" => Json("""{"files":2,"bytes":3000000,"failed_files":0}"""),
                "/api/v2/admin/downloads/preparations/pause" or "/api/v2/admin/downloads/preparations/cancel" => Json("""{"results":[{"id":"art-running","outcome":"applied"}]}"""),
                "/api/v2/admin/downloads/revoke" => RevokeMode == "unknown" ? throw new IOException("connection closed after dispatch")
                    : Json("""{"revoked":1,"bytes":20000000,"paused_monitors":1,"download_ids":["dl-1"]}"""),
                _ => Json("{}"),
            };
            return response;
        }

        /// <summary>The first requested file is deleted and every other one fails: the official "deleted N, M failed" error toast.</summary>
        private static HttpResponseMessage PartialDelete(string body)
        {
            using var request = JsonDocument.Parse(body);
            var results = request.RootElement.GetProperty("ids").EnumerateArray().Select((id, index) =>
                "{\"id\":" + JsonSerializer.Serialize(id.GetString()) + ",\"outcome\":\"" + (index == 0 ? "deleted" : "failed") + "\",\"bytes\":" + (index == 0 ? 20000000 : 0) + "}");
            return Json("{\"results\":[" + string.Join(',', results) + "]}");
        }

        private static HttpResponseMessage NodeAck()
        {
            var response = Json("""{"id":"9","config_etag":"\"node-v2\"","name":"synthetic-node","type":"transcode","url":"https://node.invalid","enabled":true,"healthy":true,"active_jobs":0,"egress_kbps":0,"created_at":"2026-01-01T00:00:00Z","download_artifact_dir_override":"/prepared/new","env_managed":[]}""");
            response.Headers.ETag = new EntityTagHeaderValue("\"node-v2\"");
            return response;
        }
    }

    private static string NoticeText(DependencyObject root) => Find<TextBlock>(root, "PreparedFilesNoticeText")?.Text ?? "";

    /// <summary>Whether the result notice is drawn with the error icon (the success icon uses the success brush).</summary>
    private static bool NoticeIsError(DependencyObject root)
    {
        var notice = Find<FrameworkElement>(root, "PreparedFilesNotice");
        var icon = Descendants(notice).OfType<Microsoft.UI.Xaml.Shapes.Path>().FirstOrDefault();
        return icon?.Stroke is SolidColorBrush stroke && Application.Current.Resources["ErrorBrush"] is SolidColorBrush error && stroke.Color == error.Color;
    }

    /// <summary>The words of every text block open in a dialog, including the runs of rich text.</summary>
    private static List<string> PopupRunTexts(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return [];
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<TextBlock>()
            .Select(t => t.Inlines.Count == 0 ? t.Text : string.Concat(t.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(r => r.Text))).ToList();
    }

    private static void Resize(Window window, int width, int height)
    {
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private static async Task Until(Func<bool> ready, int timeout = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!ready() && DateTime.UtcNow < until) await Task.Delay(25);
        if (!ready()) throw new TimeoutException("Prepared Files fixture timed out at: " + new System.Diagnostics.StackTrace(true).GetFrame(1)?.GetFileLineNumber());
    }

    /// <summary>Activates a button the way a keyboard or screen reader does: through its invoke provider.</summary>
    private static async Task InvokeAsync(Microsoft.UI.Xaml.Controls.Primitives.ButtonBase? button)
    {
        if (button == null) throw new InvalidOperationException("Expected fixture button at line " + new System.Diagnostics.StackTrace(true).GetFrame(1)?.GetFileLineNumber());
        button.StartBringIntoView();
        await Task.Delay(50);
        (FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
        await Task.Delay(80);
    }

    private static async Task ToggleAsync(CheckBox? box)
    {
        if (box == null) throw new InvalidOperationException("Expected fixture check box at line " + new System.Diagnostics.StackTrace(true).GetFrame(1)?.GetFileLineNumber());
        box.StartBringIntoView();
        await Task.Delay(50);
        (FrameworkElementAutomationPeer.CreatePeerForElement(box) as Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)?.Toggle();
        await Task.Delay(80);
    }

    private static Button? DialogButton(Window window, string name)
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<Button>().FirstOrDefault(b => b.Name == name);
    }

    private static HashSet<string> PopupTexts(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return [];
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<TextBlock>().Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task CloseDialogAsync(Window window)
    {
        await InvokeAsync(DialogButton(window, "CloseButton"));
        await Until(() => DialogButton(window, "PrimaryButton") == null && DialogButton(window, "CloseButton") == null);
        await Task.Delay(300);
    }
    private static async Task<T?> WaitFind<T>(DependencyObject root, string id) where T : FrameworkElement
    {
        for (var i = 0; i < 120 && Find<T>(root, id) == null; i++) await Task.Delay(25);
        return Find<T>(root, id);
    }
    private static T? FindDialog<T>(Window window, string id) where T : FrameworkElement
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    }
    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static HashSet<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root == null) yield break;
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
