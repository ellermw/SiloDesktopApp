using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Views.Admin;

internal static partial class AdminLibrariesNativeFixture
{
    private static async Task RunRemediationAsync(Window window)
    {
        var previous = window.Content;
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://owner-a.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigationRequests = new List<object?>();
        var navigation = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (_, parameter) => { navigationRequests.Add(parameter); return true; } };
        await using var provider = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(navigation)
            .AddSingleton(new MediaMaintenanceApi(client)).BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousServices = servicesField.GetValue(null);
        servicesField.SetValue(null, provider);
        var realtime = new Realtime();
        var page = new AdminLibrariesPage(new AdminLibrariesApi(client), authority, realtime);
        var host = new Grid();
        var retainedHost = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Orientation = Orientation.Horizontal };
        var retainedParents = new Dictionary<FrameworkElement, DependencyObject>();
        void RememberParent(FrameworkElement element)
        {
            var parent = element.Parent ?? VisualTreeHelper.GetParent(element);
            if (!element.IsLoaded || parent == null)
                throw new InvalidOperationException("Retained control must have a loaded parent before retirement: " + AutomationProperties.GetAutomationId(element));
            retainedParents[element] = parent;
        }
        void SwitchOwner(string serverUrl)
        {
            auth.ConfigureServer(serverUrl);
            auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        }
        host.Children.Add(page); host.Children.Add(retainedHost);
        window.Content = host;
        try
        {
            Resize(window, 1440, 900);
            await Until(() => page.Controller?.State is { Loaded: true } && Find<Button>(page, "LibrariesGrip_2") != null);
            var controller = page.Controller!;
            await PointerDropRegressionAsync(window, page, server, realtime);
            await SnapshotReplacementDragRegressionAsync(page, server);
            await TranslationCapabilityRetryRegressionAsync(window, page, server);
            await ReadRecoveryControlsRegressionAsync(window, page, server);
            await MatchDiagnosticsRefreshRegressionAsync(window, page, server);
            await Until(() => controller.State.CanReorder && Find<Button>(page, "LibrariesGrip_2") is { IsLoaded: true });
            var retained = new List<ButtonBase>();
            var unmatched = (AdminUnmatchedPanel)Field(page, "_unmatched")!;
            await unmatched.SetOpenAsync(true);
            var ambiguous = (AdminAppendList<AdminLibraryRoot>)Field(page, "_ambiguous")!;
            await ambiguous.SetOpenAsync(true);
            var resolveId = "AmbiguousResolve_" + Uri.EscapeDataString("/media/movies/Root 1");
            await Until(() => Find<HyperlinkButton>(page, "UnmatchedItem_u1") is { IsLoaded: true } && Find<Button>(page, resolveId) is { IsLoaded: true });
            foreach (var button in new ButtonBase[] { Find<HyperlinkButton>(page, "UnmatchedItem_u1")!, Find<Button>(page, resolveId)! })
            {
                RememberParent(button);
                retained.Add(button);
            }
            foreach (var id in new[] { "LibrariesScan_1", "LibrariesScan_4", "LibrariesStopRefresh_2", "LibrariesRefresh_4", "LibrariesMount_1",
                         "LibrariesEdit_1", "LibrariesDelete_1", "LibrariesCleanup_3", "LibrariesAdd", "LibrariesScanAll", "LibrariesScanQueue" })
                if (Find<ButtonBase>(page, id) is { } button) { RememberParent(button); retained.Add(button); } else Check(false, "retained callback exists: " + id);
            var grip = Find<Button>(page, "LibrariesGrip_2")!;
            RememberParent(grip);
            await InvokeAsync(Find<Button>(page, "LibrariesRefresh_4"));
            await Until(() => DialogControl<Button>(window, "LibrariesRefreshMissing") != null);
            var oldRefreshOption = DialogControl<Button>(window, "LibrariesRefreshMissing");
            var oldRefreshDialog = OpenDialog(window)!;
            if (oldRefreshOption != null) RememberParent(oldRefreshOption);
            var refreshClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            oldRefreshDialog.Closed += (_, _) => refreshClosed.TrySetResult();
            oldRefreshDialog.Hide();
            await refreshClosed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Until(() => Field(page, "_dialog") == null);
            oldRefreshDialog.Content = null;
            if (oldRefreshOption != null) retained.Add(oldRefreshOption);
            Resize(window, 800, 600);
            await Until(() => Find<Button>(page, "LibrariesMenu_1") != null);
            await InvokeAsync(Find<Button>(page, "LibrariesMenu_1"));
            await Until(() => FindInPopups<Button>(window, "LibrariesMenuEdit_1") != null);
            foreach (var id in new[] { "LibrariesMenuRefresh_1", "LibrariesMenuMount_1", "LibrariesMenuEdit_1", "LibrariesMenuDown_1", "LibrariesMenuDelete_1" })
                if (FindInPopups<Button>(window, id) is { } button) { RememberParent(button); retained.Add(button); } else Check(false, "retained menu callback exists: " + id);
            ClosePopups(window);
            Resize(window, 1440, 900);
            var ownerA = controller.State.Owner;
            var ownerALibraries = controller.State.Libraries;
            SwitchOwner("https://owner-b.invalid");
            await Until(() => controller.State is { Loaded: true, Owner.IsCurrent: true } && !ReferenceEquals(ownerA, controller.State.Owner) &&
                Find<Button>(page, "LibrariesEdit_1") is { IsLoaded: true } current && !retained.Contains(current));
            Check(!ReferenceEquals(ownerA, controller.State.Owner) && controller.State.Libraries.Any(library => library.Id == "1"), "replacement owner fully opens colliding library IDs");
            await ((AdminUnmatchedPanel)Field(page, "_unmatched")!).SetOpenAsync(true);
            await ((AdminAppendList<AdminLibraryRoot>)Field(page, "_ambiguous")!).SetOpenAsync(true);
            await Until(() => Find<HyperlinkButton>(page, "UnmatchedItem_u1") is { IsLoaded: true } && Find<Button>(page, resolveId) is { IsLoaded: true });
            var navigationBefore = navigationRequests.Count;
            await InvokeAsync(Find<HyperlinkButton>(page, "UnmatchedItem_u1"));
            await InvokeAsync(Find<Button>(page, resolveId));
            Check(navigationRequests.Skip(navigationBefore).SequenceEqual(new object?[] { "u1", "c-root-1" }), "current diagnostic title and Resolve navigate colliding IDs under owner B");
            navigationBefore = navigationRequests.Count;
            var writes = server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal));
            foreach (var button in retained)
            {
                await MountRetainedAsync(button, retainedHost, retainedParents[button]);
                var id = AutomationProperties.GetAutomationId(button);
                await Until(() => button.IsLoaded);
                Check(button.IsLoaded && button.IsEnabled, "retained actual callback is mounted and enabled: " + id);
                if (button.IsEnabled)
                {
                    var reached = 0;
                    RoutedEventHandler observed = (_, _) => reached++;
                    button.Click += observed;
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                    Check(peer != null, "retained actual callback has invocation provider: " + id);
                    peer?.Invoke();
                    await Until(() => reached > 0);
                    Check(reached == 1, "retained actual Click handler is reached once: " + id);
                    Check(navigationRequests.Count == navigationBefore, "retained owner-A callback cannot navigate owner B: " + id);
                    Check(page.EditorSession == null && OpenDialog(window) == null && FindInPopups<FrameworkElement>(window, "LibrariesScanQueuePopover") == null,
                        "retained actual callback does not open replacement-owner UI: " + id);
                    Check(server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == writes,
                        "retained actual callback cannot mutate replacement owner: " + id);
                    OpenDialog(window)?.Hide(); ClosePopups(window);
                    if (page.EditorSession != null) await InvokeAsync(Find<Button>(host, "LibraryEditorCancel"));
                    button.Click -= observed;
                }
                retainedHost.Children.Clear();
            }
            await MountRetainedAsync(grip, retainedHost, retainedParents[grip]);
            await Until(() => grip.IsLoaded);
            var keyReached = 0;
            grip.PreviewKeyDown += (_, _) => keyReached++;
            var beginPointer = typeof(AdminLibrariesPage).GetMethod("BeginPointerDrag", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(!(bool)beginPointer.Invoke(page, [grip, controller, ownerA!, ownerALibraries, "2", new Windows.Foundation.Point(0, 0)])!,
                "retained pointer entry rejects its rendered owner after replacement fully opens");
            grip.Focus(FocusState.Keyboard);
            Check(TrySendKey(0x20), "retained grip receives the real keyboard entry event");
            await Task.Delay(100);
            TrySendKey(0x28); await Task.Delay(50); TrySendKey(0x20); await Task.Delay(150);
            Check(keyReached >= 3, "retained keyboard grip actual PreviewKeyDown handlers are reached");
            Check(server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == writes, "retained keyboard grip cannot reorder replacement owner");
            retainedHost.Children.Clear();
            await InvokeAsync(Find<Button>(page, "LibrariesEdit_1"));
            await Until(() => page.EditorSession != null && Find<Button>(host, "LibraryEditorCancel") is { IsLoaded: true });
            var retiredCancel = Find<Button>(host, "LibraryEditorCancel")!;
            RememberParent(retiredCancel);
            var ownerB = controller.State.Owner;
            SwitchOwner("https://owner-c.invalid");
            await Until(() => controller.State.Loaded && !ReferenceEquals(ownerB, controller.State.Owner) && page.EditorSession == null &&
                Find<Button>(page, "LibrariesEdit_1") is { IsLoaded: true });
            await InvokeAsync(Find<Button>(page, "LibrariesEdit_1"));
            await Until(() => page.EditorSession != null && Find<Button>(host, "LibraryEditorCancel") is { IsLoaded: true });
            var currentEditor = page.EditorSession;
            await InvokeRetainedAsync(retiredCancel, retainedHost, retainedParents[retiredCancel]);
            Check(ReferenceEquals(currentEditor, page.EditorSession), "retained editor Cancel cannot close the replacement editor");
            retainedHost.Children.Clear();
            await InvokeAsync(Find<Button>(host, "LibraryEditorCancel"));
            await Until(() => page.EditorSession == null);
            await InvokeAsync(Find<Button>(page, "LibrariesDelete_1"));
            await Until(() => DialogControl<Button>(window, "LibrariesDeleteConfirm") != null);
            var retiredConfirm = DialogControl<Button>(window, "LibrariesDeleteConfirm")!;
            RememberParent(retiredConfirm);
            var retiredDialog = OpenDialog(window)!;
            var ownerC = controller.State.Owner;
            var confirmationClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retiredDialog.Closed += (_, _) => confirmationClosed.TrySetResult();
            SwitchOwner("https://owner-d.invalid");
            await Until(() => controller.State.Loaded && !ReferenceEquals(ownerC, controller.State.Owner) && Field(page, "_dialog") == null);
            await confirmationClosed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            retiredDialog.Content = null;
            await InvokeRetainedAsync(retiredConfirm, retainedHost, retainedParents[retiredConfirm]);
            Check(server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == writes,
                "confirmation already open before replacement cannot submit against colliding IDs");
            retainedHost.Children.Clear();
        }
        finally { window.Content = previous; servicesField.SetValue(null, previousServices); }
    }

    private static async Task InvokeRetainedAsync(ButtonBase button, Panel host, DependencyObject originalParent)
    {
        await MountRetainedAsync(button, host, originalParent);
        await Until(() => button.IsLoaded);
        var reached = 0;
        RoutedEventHandler observed = (_, _) => reached++;
        button.Click += observed;
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
        Check(button.IsLoaded && button.IsEnabled && peer != null, "retained lifecycle callback is reachable: " + AutomationProperties.GetAutomationId(button));
        peer?.Invoke();
        await Until(() => reached > 0);
        Check(reached == 1, "retained lifecycle Click is reached once: " + AutomationProperties.GetAutomationId(button));
        button.Click -= observed;
    }

    private static async Task MountRetainedAsync(FrameworkElement element, Panel host, DependencyObject originalParent)
    {
        var id = AutomationProperties.GetAutomationId(element);
        Program.Log("TRACE retained detach " + id + " original=" + originalParent.GetType().Name + " current=" + (element.Parent ?? VisualTreeHelper.GetParent(element))?.GetType().Name);
        if (originalParent is Panel panel && panel.Children.Contains(element)) panel.Children.Remove(element);
        else if (originalParent is ContentControl content && ReferenceEquals(content.Content, element)) content.Content = null;
        else if (originalParent is Border border && ReferenceEquals(border.Child, element)) border.Child = null;
        else if (originalParent is ContentPresenter presenter && ReferenceEquals(presenter.Content, element)) presenter.Content = null;
        else throw new InvalidOperationException("Retained original parent no longer owns the actual control: " + id);
        await Until(() => element.Parent == null && VisualTreeHelper.GetParent(element) == null);
        Check(element.Parent == null && VisualTreeHelper.GetParent(element) == null, "retained actual control detached before mounting: " + id);
        host.Children.Add(element);
    }

    private static async Task TranslationCapabilityRetryRegressionAsync(Window window, AdminLibrariesPage page, Server server)
    {
        server.MetadataAiReadFails = true;
        await InvokeAsync(Find<Button>(page, "LibrariesEdit_1"));
        await Until(() => page.EditorSession != null);
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorTab_Metadata"));
        await Until(() => Find<Button>(window.Content, "LibraryEditorTranslationCapabilityRetry") is { IsLoaded: true, IsEnabled: true });
        Check(Find<ComboBox>(window.Content, "LibraryEditorTranslateLanguage") == null, "failed translation capability does not expose mutation controls");
        var writes = server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal));
        var reads = server.Requests.Count(request => request == "GET /api/v2/capabilities/metadata-ai");
        server.MetadataAiReadFails = false;
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorTranslationCapabilityRetry"));
        await Until(() => Find<ComboBox>(window.Content, "LibraryEditorTranslateLanguage") is { IsLoaded: true });
        Check(server.Requests.Count(request => request == "GET /api/v2/capabilities/metadata-ai") == reads + 1, "translation Retry re-reads the failed capability exactly once");
        Check(server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == writes, "translation capability Retry is read-only");
        Check(Find<Button>(window.Content, "LibraryEditorTranslationCapabilityRetry") == null, "available capability replaces the failure with translation controls");
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorCancel"));
        await Until(() => page.EditorSession == null);
    }

    private static async Task ReadRecoveryControlsRegressionAsync(Window window, AdminLibrariesPage page, Server server)
    {
        var writes = server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal));
        var unmatched = (AdminUnmatchedPanel)Field(page, "_unmatched")!;
        await unmatched.SetOpenAsync(true);
        server.UnmatchedReadFails = true;
        await unmatched.LoadTotalsAsync();
        await Until(() => Find<Button>(page, "UnmatchedTotalsRetry") is { IsLoaded: true, IsEnabled: true });
        Check(!unmatched.State.Hidden && unmatched.State.TotalsError != null, "failed unmatched totals retain a visible diagnostics section and actual Retry control");
        server.UnmatchedReadFails = false;
        await InvokeAsync(Find<Button>(page, "UnmatchedTotalsRetry"));
        await Until(() => !unmatched.State.TotalsLoading && unmatched.State.TotalsError == null);
        Check(unmatched.State.AwaitingTotal == 23 && unmatched.State.IgnoredTotal == 0, "actual totals Retry recovers both counts");
        await unmatched.SetOpenAsync(false);

        server.TranslationJobsReadFails = true;
        await InvokeAsync(Find<Button>(page, "LibrariesEdit_1"));
        await Until(() => page.EditorSession != null);
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorTab_Metadata"));
        await Until(() => Find<Button>(window.Content, "LibraryEditorTranslationJobsRetry") is { IsLoaded: true, IsEnabled: true });
        Check(Find<Button>(window.Content, "LibraryEditorTranslateStart") is { IsEnabled: false }, "unknown translation jobs cannot expose an enabled Translate action");
        server.TranslationJobsReadFails = false;
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorTranslationJobsRetry"));
        await Until(() => Find<Button>(window.Content, "LibraryEditorTranslateStart") is { IsLoaded: true, IsEnabled: true });
        Check(Find<Button>(window.Content, "LibraryEditorTranslationJobsRetry") == null, "actual translation jobs Retry restores known idle state");
        Check(server.Requests.Count(request => !request.StartsWith("GET ", StringComparison.Ordinal)) == writes, "both diagnostics recovery controls issue reads only");
        await InvokeAsync(Find<Button>(window.Content, "LibraryEditorCancel"));
        await Until(() => page.EditorSession == null);
    }

    private static async Task MatchDiagnosticsRefreshRegressionAsync(Window window, AdminLibrariesPage page, Server server)
    {
        var unmatched = (AdminUnmatchedPanel)Field(page, "_unmatched")!;
        var stale = (AdminAppendList<AdminStaleMediaId>)Field(page, "_stale")!;
        foreach (var originUnmatched in new[] { true, false })
        foreach (var otherOpen in new[] { true, false })
        {
            server.DiagnosticMatchApplied = false;
            await unmatched.SetOpenAsync(true);
            await unmatched.FirstAsync();
            await unmatched.LoadTotalsAsync();
            await stale.SetOpenAsync(true);
            await stale.ReloadAsync();
            if (!otherOpen)
            {
                if (originUnmatched) await stale.SetOpenAsync(false); else await unmatched.SetOpenAsync(false);
            }
            var unmatchedOpen = unmatched.State.Open;
            var staleOpen = stale.State.Open;
            var buttonId = originUnmatched ? "UnmatchedMatch_u1" : "StaleMatch_" + Uri.EscapeDataString("st1:tmdb");
            await Until(() => Find<Button>(page, buttonId) is { IsLoaded: true });
            var unmatchedBefore = server.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/unmatched-items?", StringComparison.Ordinal));
            var staleBefore = server.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/stale-ids?", StringComparison.Ordinal));
            await InvokeAsync(Find<Button>(page, buttonId));
            await Until(() => OpenDialog(window) is SiloPlayer.Controls.MatchItemDialog);
            var dialog = (SiloPlayer.Controls.MatchItemDialog)OpenDialog(window)!;
            server.DiagnosticMatchApplied = true;
            typeof(SiloPlayer.Controls.MatchItemDialog).GetProperty(nameof(SiloPlayer.Controls.MatchItemDialog.HasAppliedMatch))!.SetValue(dialog, true);
            dialog.Hide();
            await Until(() => Field(page, "_dialog") == null && unmatched.State.AwaitingTotal == 22 && (!staleOpen || stale.State.Total == 0));
            var label = $"origin={(originUnmatched ? "unmatched" : "stale")}, otherOpen={otherOpen}";
            Check(server.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/unmatched-items?", StringComparison.Ordinal)) - unmatchedBefore == (unmatchedOpen ? 3 : 2),
                "confirmed dialog result refreshes unmatched rows only when open and always both totals: " + label);
            Check(server.Requests.Count(request => request.StartsWith("GET /api/v2/libraries/stale-ids?", StringComparison.Ordinal)) - staleBefore == (staleOpen ? 1 : 0),
                "confirmed dialog result refreshes stale rows only when open: " + label);
            Check(unmatched.State.IgnoredTotal == 0 && (!unmatchedOpen || unmatched.State.Items.All(item => item.ContentId != "u1")),
                "match refresh removes the obsolete unmatched row and updates ignored total: " + label);
            if (!staleOpen)
            {
                await stale.SetOpenAsync(true);
                Check(stale.State.Items.Count == 0 && stale.State.Total == 0, "closed stale cache refreshes lazily on its next open");
            }
        }
        server.DiagnosticMatchApplied = false;
        await unmatched.FirstAsync();
        await unmatched.LoadTotalsAsync();
        await unmatched.SetOpenAsync(false);
        await stale.SetOpenAsync(false);
    }

    private static async Task SnapshotReplacementDragRegressionAsync(AdminLibrariesPage page, Server server)
    {
        var controller = page.Controller!;
        var original = server.Libraries.ToDictionary(library => library.Id);
        object? Handle(string name, params object[] arguments) => typeof(AdminLibrariesPage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);
        foreach (var keyboard in new[] { false, true })
        {
            server.Libraries = new[] { "1", "2", "3", "4" }.Select(id => original[id]).ToList();
            var previousGrip = Find<Button>(page, "LibrariesGrip_1");
            await controller.ReloadAsync();
            var currentLibraries = controller.State.Libraries;
            await Until(() => controller.State.CanReorder &&
                Field(page, "_renderedListKey") is object?[] renderedKey && ReferenceEquals(renderedKey[1], currentLibraries) &&
                Find<Button>(page, "LibrariesGrip_1") is { IsLoaded: true, IsEnabled: true } currentGrip &&
                !ReferenceEquals(previousGrip, currentGrip) && Field(page, "_dragFrom") is -1);
            var grip = Find<Button>(page, "LibrariesGrip_1")!;
            var target = Find<Button>(page, "LibrariesGrip_3")!;
            var list = (UIElement)Field(page, "_list")!;
            var renderedLibraries = controller.State.Libraries;
            var owner = controller.State.Owner;
            if (keyboard)
            {
                grip.StartBringIntoView();
                Check(grip.Focus(FocusState.Keyboard), "snapshot regression focuses the current rendered grip");
                await Until(() => ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(page.XamlRoot), grip));
                Check(TrySendKey(0x20), "snapshot regression starts a real keyboard reorder");
                await Until(() => Field(page, "_keyboardDrag") is true);
                TrySendKey(0x28); await Task.Delay(50); TrySendKey(0x28);
                await Until(() => Field(page, "_dragOver") is 2);
            }
            else
            {
                var start = grip.TransformToVisual(list).TransformPoint(new(grip.ActualWidth / 2, grip.ActualHeight / 2));
                var end = target.TransformToVisual(list).TransformPoint(new(target.ActualWidth / 2, target.ActualHeight / 2));
                Check((bool)Handle("BeginPointerDrag", grip, controller, owner!, renderedLibraries, "1", start)!, "snapshot regression begins dragging A from rendered [A,B,C]");
                Check((bool)Handle("MovePointerDrag", grip, end)!, "snapshot regression targets C before a same-owner reload");
            }
            var writes = server.Requests.Count(request => request == "POST /api/v2/libraries/reorder");
            server.Libraries = new[] { "2", "1", "3", "4" }.Select(id => original[id]).ToList();
            await controller.ReloadAsync();
            Check(ReferenceEquals(owner, controller.State.Owner) && !ReferenceEquals(renderedLibraries, controller.State.Libraries), "[A,B,C] to [B,A,C] replaces the list without retiring its authority");
            if (keyboard) Handle("CommitDrag"); else Handle("ReleasePointerDrag", grip, (Action)(() => { }));
            await Task.Delay(150);
            Check(server.Requests.Count(request => request == "POST /api/v2/libraries/reorder") == writes, "snapshot replacement cancels the pending " + (keyboard ? "keyboard" : "pointer") + " reorder rather than moving B");
            Check(server.Libraries.Select(library => library.Id).SequenceEqual(new[] { "2", "1", "3", "4" }), "snapshot replacement keeps the server's B,A,C identities untouched");
            Check(!(bool)Handle("BeginPointerDrag", grip, controller, owner!, renderedLibraries, "1", new Windows.Foundation.Point(0, 0))!, "retained same-owner grip cannot start against a replaced list snapshot");
        }
        server.Libraries = new[] { "1", "3", "2", "4" }.Select(id => original[id]).ToList();
        await controller.ReloadAsync();
    }

    private static async Task PointerDropRegressionAsync(Window window, AdminLibrariesPage page, Server server, Realtime realtime)
    {
        var grip = Find<Button>(page, "LibrariesGrip_2")!;
        var target = Find<Button>(page, "LibrariesGrip_3")!;
        var list = (UIElement)Field(page, "_list")!;
        var start = grip.TransformToVisual(list).TransformPoint(new(grip.ActualWidth / 2, grip.ActualHeight / 2));
        var end = target.TransformToVisual(list).TransformPoint(new(target.ActualWidth / 2, target.ActualHeight / 2));
        object? Handle(string name, params object[] arguments) => typeof(AdminLibrariesPage).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, arguments);
        var before = server.Requests.Count(request => request == "POST /api/v2/libraries/reorder");
        Check((bool)Handle("BeginPointerDrag", grip, page.Controller!, page.Controller!.State.Owner!, page.Controller.State.Libraries, "2", start)!, "reachable pointer press handler admits the rendered owner");
        Check((bool)Handle("MovePointerDrag", grip, end)!, "reachable pointer move handler selects a real rendered target");
        var libraries = page.Controller.State.Libraries;
        var scans = page.Controller.State.Scans;
        realtime.Event("scans", "scan.progress", "{\"id\":\"s2\",\"library_id\":2,\"mode\":\"subtree\",\"status\":\"running\",\"result\":{\"total_files\":100,\"files_processed\":42}}");
        await Until(() => !ReferenceEquals(scans, page.Controller.State.Scans));
        Handle("Render", page.Controller.State);
        await Task.Delay(100); // Allow queued renders to run before release.
        Check(ReferenceEquals(libraries, page.Controller.State.Libraries), "scan progress keeps the drag's library snapshot current");
        Check(ReferenceEquals(grip, Find<Button>(page, "LibrariesGrip_2")) && grip.IsLoaded && ReferenceEquals(Field(page, "_dragGrip"), grip),
            "scan progress preserves the mounted pointer grip and pending drop");
        Handle("LosePointerDragCapture", grip);
        Handle("ReleasePointerDrag", grip, (Action)(() => Handle("LosePointerDragCapture", grip)));
        await Until(() => server.Requests.Count(request => request == "POST /api/v2/libraries/reorder") > before, 1500);
        Check(server.Requests.Count(request => request == "POST /api/v2/libraries/reorder") == before + 1, "reachable release commits exactly one reorder despite capture loss before and during release");
        await Until(() => page.Controller!.State.CanReorder);
        await Until(() => Find<Button>(page, "LibrariesGrip_2") is { IsLoaded: true });
        grip = Find<Button>(page, "LibrariesGrip_2")!;
        target = Find<Button>(page, "LibrariesGrip_1")!;
        start = grip.TransformToVisual(list).TransformPoint(new(grip.ActualWidth / 2, grip.ActualHeight / 2));
        end = target.TransformToVisual(list).TransformPoint(new(target.ActualWidth / 2, target.ActualHeight / 2));
        Check((bool)Handle("BeginPointerDrag", grip, page.Controller!, page.Controller!.State.Owner!, page.Controller.State.Libraries, "2", start)!, "cancellation case begins a current-owner gesture");
        Check((bool)Handle("MovePointerDrag", grip, end)!, "cancellation case has a pending nontrivial drop");
        Handle("CancelPointerDrag", grip);
        Handle("ReleasePointerDrag", grip, (Action)(() => Handle("LosePointerDragCapture", grip)));
        await Task.Delay(100);
        Check(server.Requests.Count(request => request == "POST /api/v2/libraries/reorder") == before + 1, "reachable pointer cancellation never commits a pending drop");
        grip = Find<Button>(page, "LibrariesGrip_2")!;
        Handle("BeginPointerDrag", grip, page.Controller!, page.Controller!.State.Owner!, page.Controller.State.Libraries, "2", start);
        Handle("MovePointerDrag", grip, end);
        Handle("LosePointerDragCapture", grip);
        await Task.Delay(100);
        Handle("ReleasePointerDrag", grip, (Action)(() => Handle("LosePointerDragCapture", grip)));
        Check(server.Requests.Count(request => request == "POST /api/v2/libraries/reorder") == before + 1, "capture loss without a matching release cancels instead of committing later");
    }
}
