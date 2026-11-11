using System.Net;
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
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Actual RequestsPage / RequestDetailPage controls over a fake wire for official removeFailedRequest (06bd3370)
/// and LibraryItemLink (a0ae0c34). Only public UI, automation names and the HTTP wire are observed so the same
/// fixture can be run against an earlier publish to reproduce the missing behavior.
/// </summary>
internal static class RequestRemovalNativeFixture
{
    private const string Notice = "This title is in a library this profile can't open.";

    internal static async Task RunAsync(StackPanel parent)
    {
        // SILO_NATIVE_REQUEST_REMOVAL_CASE=links|removal narrows a run (for example to reproduce one half against an earlier publish).
        var only = Environment.GetEnvironmentVariable("SILO_NATIVE_REQUEST_REMOVAL_CASE");
        if (only != "links") { await RemovalAsync(parent); await DetailCancellationAsync(parent); await DetailCancellationRefreshAsync(parent); await RemovalReadInvalidationAsync(parent); await RemovalRouteRefreshAsync(parent); await RemovalFinalizationAsync(parent); await OlderServerAsync(parent); }
        if (only == "removal") { Program.Log("PASS: REQUEST_REMOVAL_ONLY_COMPLETED"); return; }
        await RowLinksAsync(parent);
        await DetailLinkAsync(parent);
        Program.Log("PASS: REQUEST_REMOVAL_LINKS_COMPLETED actual Remove request confirmation/Keep/pending/duplicate/409 race/transient retry/success refresh/profile retirement/cancel separation/older server, and library-link access check on Requests rows and Request detail (open, 404 notice, transient open, double click, departure, profile switch).");
    }

    private static async Task RemovalAsync(StackPanel parent)
    {
        using var rig = new Rig(parent, remove: true);
        await rig.OpenAsync("yours");
        Wire wire = rig.Wire;
        var removable = RemoveNames(rig.Requests);
        if (!removable.SequenceEqual(new[] { "Remove request for Failed one", "Remove request for Failed two", "Remove request for Failed three" }))
            throw new InvalidOperationException("Remove is offered for: " + string.Join(", ", removable) + ". Expected only failed requests that delivered nothing.");
        if (!ButtonNames(rig.Requests).Contains("Cancel request for Retryable") || ButtonNames(rig.Requests).Any(name => name.StartsWith("Cancel request for Failed")))
            throw new InvalidOperationException("Cancel must stay on the active request and never replace Remove on a failed one.");
        if (!GuideTexts(rig.Requests).Any(text => text.Text.Contains("You can remove it if nothing was delivered.")))
            throw new InvalidOperationException("Failed status help omits the removal guidance a supporting server advertises.");
        await MediaParityNativeFixture.CaptureAsync(rig.Requests, "request-removal-yours-900.png");

        // Keep: the confirmation names the title and nothing is sent.
        Click(Remove(rig.Requests, "Failed one"));
        var dialog = await rig.DialogAsync();
        var body = string.Join(" ", Descendants(dialog).OfType<TextBlock>().Select(text => text.Text));
        if ((dialog.Title as string) != "Remove this request?" || dialog.PrimaryButtonText != "Remove request" || dialog.CloseButtonText != "Keep request" ||
            dialog.DefaultButton != ContentDialogButton.Close || !body.Contains("Your failed request for \"Failed one\" will be deleted. You can request the title again later."))
            throw new InvalidOperationException("Remove confirmation differs from the official dialog: " + dialog.Title + " / " + body);
        await MediaParityNativeFixture.CaptureAsync(dialog, "request-removal-confirm.png");
        // Re-enter the handler directly so modal input routing cannot hide duplicate activations.
        InvokePrivate(rig.Requests, "RemoveRequest_Click", Remove(rig.Requests, "Failed one"), new RoutedEventArgs());
        InvokePrivate(rig.Requests, "RemoveRequest_Click", Remove(rig.Requests, "Failed two"), new RoutedEventArgs());
        InvokePrivate(rig.Requests, "CancelRequest_Click", Descendants(rig.Requests).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancel request for Retryable"), new RoutedEventArgs());
        await Task.Delay(150);
        if (!ReferenceEquals(rig.Dialog(), dialog) || wire.Writes.Count != 0)
            throw new InvalidOperationException("A second activation while confirmation is open replaced the dialog or sent a mutation.");
        Click(Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == "Keep request"));
        await Task.Delay(120);
        if (wire.Writes.Count != 0 || !Remove(rig.Requests, "Failed one").IsEnabled) throw new InvalidOperationException("Keeping the request sends a mutation or disables Remove.");

        // Race: while the DELETE is pending an admin retries it. Pending state blocks a duplicate, the 409 leaves the row, and
        // a refreshed row offers Cancel rather than Remove.
        wire.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.DeleteMode = HttpStatusCode.Conflict;
        wire.OnDelete = id => wire.Mine["f1"] = Row("f1", "Failed one", status: "approved", outcome: "active");
        var reads = wire.MineReads;
        Click(Remove(rig.Requests, "Failed one")); Primary(await rig.DialogAsync());
        await Until(() => wire.Writes.Count == 1);
        var pending = Remove(rig.Requests, "Failed one");
        if (pending.IsEnabled || pending.Content is not ProgressRing { IsActive: true }) throw new InvalidOperationException("Pending removal exposes an enabled duplicate-submit target.");
        InvokePrivate(rig.Requests, "RemoveRequest_Click", pending, new RoutedEventArgs());
        await Task.Delay(150);
        if (wire.Writes.Count != 1 || rig.Dialog() != null) throw new InvalidOperationException("A second click while pending reopened confirmation or sent another DELETE.");
        wire.DeleteGate.SetResult();
        await Until(() => wire.MineReads > reads && !ButtonNames(rig.Requests).Contains("Remove request for Failed one"));
        await rig.ToastAsync("This request is no longer failed.");
        var first = wire.Writes.Single();
        if (first.Method != "DELETE" || first.Path != "/api/v2/requests/f1" || first.HadBody) throw new InvalidOperationException($"Removal wire was {first.Method} {first.Path} body={first.HadBody}.");
        if (!ButtonNames(rig.Requests).Contains("Cancel request for Failed one") || wire.Writes.Any(write => write.Path.EndsWith("/cancel")))
            throw new InvalidOperationException("After the race the retried request must be cancellable by the server's new state, without removal using cancel.");
        wire.DeleteGate = null;

        // Transient failure: recoverable. Then a retry succeeds and the list, discovery and title state are read again.
        wire.DeleteMode = HttpStatusCode.ServiceUnavailable; wire.OnDelete = null;
        Click(Remove(rig.Requests, "Failed two")); Primary(await rig.DialogAsync());
        await Until(() => wire.Writes.Count == 2 && Remove(rig.Requests, "Failed two") is { IsEnabled: true });
        await rig.ToastAsync(null);
        wire.DeleteMode = HttpStatusCode.NoContent; wire.OnDelete = id => wire.Mine.Remove(id);
        reads = wire.MineReads; var discoverReads = wire.DiscoverReads;
        Click(Remove(rig.Requests, "Failed two")); Primary(await rig.DialogAsync());
        await Until(() => wire.Writes.Count == 3 && !ButtonNames(rig.Requests).Contains("Remove request for Failed two"));
        await rig.ToastAsync("Request removed");
        if (wire.MineReads <= reads || wire.DiscoverReads <= discoverReads) throw new InvalidOperationException("Removal did not re-read the account list and discovery state from the server.");
        if (wire.Writes.Select(write => write.Path).Except(["/api/v2/requests/f1", "/api/v2/requests/f2"]).Any())
            throw new InvalidOperationException("A DELETE was sent for a request that is not removable: " + string.Join(",", wire.Writes.Select(write => write.Path)));
        await MediaParityNativeFixture.CaptureAsync(rig.Requests, "request-removal-after-900.png");

        // Cancellation remains its own POST and never DELETE.
        var deletes = wire.Writes.Count(write => write.Method == "DELETE");
        var cancel = Descendants(rig.Requests).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancel request for Retryable");
        var writes = wire.Writes.Count;
        Click(cancel);
        dialog = await rig.DialogAsync();
        InvokePrivate(rig.Requests, "CancelRequest_Click", cancel, new RoutedEventArgs());
        InvokePrivate(rig.Requests, "CancelRequest_Click", Descendants(rig.Requests).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancel request for Failed one"), new RoutedEventArgs());
        InvokePrivate(rig.Requests, "RemoveRequest_Click", Remove(rig.Requests, "Failed three"), new RoutedEventArgs());
        await Task.Delay(150);
        if (!ReferenceEquals(rig.Dialog(), dialog) || wire.Writes.Count != writes)
            throw new InvalidOperationException("A second Cancel or Remove activation replaced the cancel confirmation or sent a mutation.");
        Click(Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == "Keep request"));
        await Until(() => rig.Dialog() == null);
        await Task.Delay(120);
        Click(cancel);
        Primary(await rig.DialogAsync());
        await Until(() => wire.Writes.Any(write => write.Path == "/api/v2/requests/r1/cancel"));
        if (wire.Writes.Count(write => write.Method == "DELETE") != deletes || wire.Writes.Single(write => write.Path.EndsWith("/cancel")).Method != "POST")
            throw new InvalidOperationException("Cancel and Remove are not separate requests.");

        // The owner retires with a profile switch: no stale success is reported and no list is read for the old owner.
        wire.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(Remove(rig.Requests, "Failed three")); Primary(await rig.DialogAsync());
        await Until(() => wire.Writes.Count(write => write.Path == "/api/v2/requests/f3") == 1);
        await rig.NoToastAsync("Request removed"); reads = wire.MineReads;
        rig.Client.SetProfile("profile-b");
        wire.DeleteGate.SetResult();
        await Task.Delay(400);
        if (rig.ToastTexts().Any(text => text.Contains("Request removed")) || wire.MineReads != reads)
            throw new InvalidOperationException("A removal started by the old profile reported success or refreshed lists for the new owner.");
        Program.Log("PASS: Remove request Keep/pending/duplicate/409/transient/success/cancel separation/profile retirement on the actual Requests page.");
    }

    private static async Task DetailCancellationAsync(StackPanel parent)
    {
        using var rig = new Rig(parent, remove: true);
        rig.Wire.DetailRequest = "{\"requestable\":false,\"requested_by_viewer\":true,\"request_id\":\"r1\",\"status\":\"pending\"}";
        rig.Navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 7));
        var page = (RequestDetailPage)rig.Frame.Content;
        Button? cancel = null;
        await Until(() => (cancel = Descendants(page).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "cancel")) != null);
        // Keeping the first dialog must release the guard for the next activation.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Click(cancel!);
            var dialog = await rig.DialogAsync();
            Click(cancel!);
            await Task.Delay(150);
            if (!ReferenceEquals(rig.Dialog(), dialog) || rig.Wire.Writes.Count != 0)
                throw new InvalidOperationException("Repeated detail cancellation replaced its confirmation or sent a mutation.");
            Click(Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == "Keep request"));
            await Until(() => rig.Dialog() == null);
            await Task.Delay(120);
        }
        Program.Log("PASS: Request detail cancellation ignores repeated activation and releases its confirmation guard after Keep.");
    }

    private static async Task DetailCancellationRefreshAsync(StackPanel parent)
    {
        foreach (var state in new[] { "eligible", "ineligible", "replaced" })
        {
            using var rig = new Rig(parent, remove: true);
            var wire = rig.Wire;
            wire.Mine["r1"] = Row("r1", "Retryable", status: "approved", outcome: "active");
            wire.DetailRequest = "{\"requestable\":false,\"requested_by_viewer\":true,\"request_id\":\"r1\",\"status\":\"approved\",\"download\":{\"percent\":42}}";
            rig.Navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 7));
            var page = (RequestDetailPage)rig.Frame.Content;
            Button? cancel = null;
            await Until(() => page.IsLoaded && (cancel = Descendants(page).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "cancel")) != null);
            Click(cancel!);
            var dialog = await rig.DialogAsync();
            if (state == "ineligible")
                wire.Mine["r1"] = Row("r1", "Retryable", status: "approved", outcome: "active", targets: "[{\"status\":\"downloading\"}]");
            if (state == "replaced") wire.DetailRequest = wire.DetailRequest.Replace("\"r1\"", "\"r2\"");
            var itemField = typeof(RequestDetailPage).GetField("_item", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var before = itemField.GetValue(page);
            // Drive the same refresh as the 30-second timer while confirmation is still open.
            await (Task)InvokePrivate(page, "RefreshDownloadAsync")!;
            if (ReferenceEquals(before, itemField.GetValue(page)) || !ReferenceEquals(dialog, rig.Dialog()))
                throw new InvalidOperationException("The detail poll did not replace its item while retaining the cancellation confirmation.");
            Primary(dialog);
            if (state == "eligible")
            {
                await Until(() => wire.Writes.Count == 1);
                if (wire.Writes.Single() is not { Method: "POST", Path: "/api/v2/requests/r1/cancel" })
                    throw new InvalidOperationException("Confirmed cancellation after a poll did not send the original request's cancel POST.");
            }
            else
            {
                await rig.ToastAsync("This request has changed and can no longer be cancelled.");
                if (wire.Writes.Count != 0) throw new InvalidOperationException("A changed or ineligible request was cancelled after polling.");
            }
            await Until(() => cancel!.IsEnabled && cancel.Content is not ProgressRing && rig.Dialog() == null);
        }
        Program.Log("PASS: detail cancellation survives download polling, while changed or ineligible requests explain why cancellation was not sent.");
    }

    private static async Task RemovalReadInvalidationAsync(StackPanel parent)
    {
        foreach (var catchUp in new[] { false, true })
        {
            using var rig = new Rig(parent, remove: true);
            await rig.OpenAsync("yours");
            var wire = rig.Wire;
            var vm = rig.Requests.ViewModel;
            var mineReads = wire.MineReads;
            wire.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.OnDelete = id => wire.Mine.Remove(id);
            Click(Remove(rig.Requests, "Failed one")); Primary(await rig.DialogAsync());
            try
            {
                await Until(() => wire.Writes.Count == 1);
                if (catchUp)
                {
                    if (!rig.Client.TryInvalidateAccessContext(rig.Client.CaptureContext(), out _, transportCatchUp: true))
                        throw new InvalidOperationException("Transport catch-up did not invalidate the read context.");
                }
                else rig.Client.InvalidateAccessContext();
            }
            finally { wire.DeleteGate.TrySetResult(); }
            await Until(() => !vm.IsRemovingRequest("f1"));
            await rig.ToastAsync("Request removed");
            if (wire.MineReads <= mineReads || vm.MyRequests.Any(row => row.Id == "f1") ||
                ButtonNames(rig.Requests).Contains("Remove request for Failed one") || wire.Writes.Count != 1)
                throw new InvalidOperationException("Read invalidation swallowed a confirmed removal's outcome or left its deleted row listed.");
        }
        Program.Log("PASS: access invalidation and transport catch-up during DELETE preserve removal feedback and reconcile the current profile's list.");
    }

    private static async Task RemovalRouteRefreshAsync(StackPanel parent)
    {
        foreach (var catchUp in new[] { false, true })
        foreach (var phase in new[] { "confirmation", "confirmation-pending", "missing-row", "delete", "refresh" })
        foreach (var status in new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable })
        {
            if (phase == "missing-row" && status != HttpStatusCode.NoContent) continue;
            using var rig = new Rig(parent, remove: true);
            await rig.OpenAsync("yours");
            var page = rig.Requests;
            var vm = page.ViewModel;
            var wire = rig.Wire;
            wire.DeleteMode = status;
            if (status == HttpStatusCode.NoContent) wire.OnDelete = id => wire.Mine.Remove(id);
            Click(Remove(page, "Failed one"));
            var confirmation = await rig.DialogAsync();
            try
            {
                if (phase is "delete" or "refresh")
                {
                    if (phase == "delete") wire.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    else wire.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var statusReads = wire.StatusReads;
                    Primary(confirmation);
                    await Until(() => wire.Writes.Count == 1 && (phase == "delete" || wire.StatusReads > statusReads));
                }
                if (phase == "missing-row") wire.Mine.Remove("f1");
                if (catchUp)
                {
                    if (!rig.Client.TryInvalidateAccessContext(rig.Client.CaptureContext(), out _, transportCatchUp: true))
                        throw new InvalidOperationException("Transport catch-up did not invalidate the read context.");
                }
                else rig.Client.InvalidateAccessContext();
                var mineReads = wire.MineReads;
                if (phase == "confirmation-pending") wire.MineGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                // Exercise the route refresh used by MainWindow, not just the API generation change.
                if (!rig.Navigation.RefreshCurrentEntry() || !ReferenceEquals(page, rig.Frame.Content))
                    throw new InvalidOperationException("Shell refresh retired the Requests visit during " + phase + ".");
                if (phase is "confirmation" or "confirmation-pending" or "missing-row")
                {
                    await Until(() => wire.MineReads > mineReads &&
                        (phase == "confirmation-pending" ? vm.IsLoadingMine : !vm.LoadCommand.IsRunning));
                    if (!ReferenceEquals(confirmation, rig.Dialog()) || wire.Writes.Count != 0)
                        throw new InvalidOperationException("Shell refresh dismissed or submitted the open removal confirmation.");
                    Primary(confirmation);
                    // Confirm before the reload can repopulate MyRequests: its temporary absence is not a vanished row.
                    if (phase == "confirmation-pending") await Until(() => wire.Writes.Count == 1);
                }
            }
            finally
            {
                wire.DeleteGate?.TrySetResult();
                wire.StatusGate?.TrySetResult();
                wire.MineGate?.TrySetResult();
            }
            var expected = phase == "missing-row" ? "Could not remove the request - the list changed, try again"
                : status == HttpStatusCode.NoContent ? "Request removed"
                : status == HttpStatusCode.Conflict ? "This request is no longer failed." : "Fixture outage";
            await rig.ToastAsync(expected);
            await Until(() => !vm.IsRemovingRequest("f1") && !vm.LoadCommand.IsRunning && !vm.IsLoadingMine);
            if (!ReferenceEquals(page, rig.Frame.Content) || wire.Writes.Count != (phase == "missing-row" ? 0 : 1) ||
                wire.Writes.Any(write => write.Method != "DELETE" || write.Path != "/api/v2/requests/f1" || write.HadBody))
                throw new InvalidOperationException("Shell refresh lost the removal visit or changed its mutation during " + phase + ".");
            var removed = phase == "missing-row" || status == HttpStatusCode.NoContent;
            if (vm.MyRequests.Any(row => row.Id == "f1") == removed ||
                ButtonNames(page).Contains("Remove request for Failed one") == removed ||
                (!removed && !Remove(page, "Failed one").IsEnabled))
                throw new InvalidOperationException("Shell refresh left incorrect removal state during " + phase + ".");
        }
        Program.Log("PASS: shell route refresh during confirmation (including a pending mine read), DELETE and reconciliation preserves success/refusal/failure feedback; a vanished row explains why no DELETE was sent.");
    }

    private static async Task RemovalFinalizationAsync(StackPanel parent)
    {
        foreach (var retirement in new[] { "none", "profile", "departure", "confirmation" })
        {
            using var rig = new Rig(parent, remove: true);
            await rig.OpenAsync("yours");
            var wire = rig.Wire;
            Click(Remove(rig.Requests, "Failed one"));
            var confirmation = await rig.DialogAsync();
            if (retirement == "confirmation")
            {
                rig.Navigation.NavigateImmediately(typeof(Page));
                await Task.Delay(80);
                Primary(confirmation);
                await Task.Delay(200);
                if (wire.Writes.Count != 0) throw new InvalidOperationException("A confirmation abandoned by navigation still deleted the request.");
                continue;
            }
            wire.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var statusReads = wire.StatusReads;
            var mineReads = wire.MineReads;
            wire.OnDelete = id => wire.Mine.Remove(id);
            Primary(confirmation);
            await Until(() => wire.StatusReads > statusReads);
            var vm = rig.Requests.ViewModel;
            var pending = Remove(rig.Requests, "Failed one");
            if (!vm.IsRemovingRequest("f1") || pending.IsEnabled)
                throw new InvalidOperationException("Removal became available while its post-DELETE refresh was pending.");
            InvokePrivate(rig.Requests, "RemoveRequest_Click", pending, new RoutedEventArgs());
            await Task.Delay(80);
            if (wire.Writes.Count != 1 || rig.Dialog() != null) throw new InvalidOperationException("Refresh pending guard admitted a second confirmation or DELETE.");
            if (retirement == "profile") rig.Client.SetProfile("profile-b");
            if (retirement == "departure") rig.Navigation.NavigateImmediately(typeof(Page));
            wire.StatusGate.TrySetResult();
            await Until(() => !vm.IsRemovingRequest("f1"));
            if (retirement == "none") await rig.ToastAsync("Request removed");
            else
            {
                await Task.Delay(150);
                if (wire.MineReads != mineReads || rig.ToastTexts().Any(text => text.Contains("Request removed")))
                    throw new InvalidOperationException("An abandoned removal refresh launched more reads or reported stale success.");
            }
        }
        Program.Log("PASS: removal retains pending state through post-DELETE refresh and retires profile/departure/confirmation continuations.");
    }

    private static async Task OlderServerAsync(StackPanel parent)
    {
        using var rig = new Rig(parent, remove: false);
        await rig.OpenAsync("yours");
        if (RemoveNames(rig.Requests).Any() || GuideTexts(rig.Requests).Any(text => text.Text.Contains("remove it")))
            throw new InvalidOperationException("A server that does not advertise remove_failed_supported offers removal.");
        Program.Log("PASS: older server (no remove_failed_supported) hides Remove request and its status guidance.");
    }

    private static async Task RowLinksAsync(StackPanel parent)
    {
        using var rig = new Rig(parent, remove: true, libraryRows: true);
        await rig.OpenAsync("yours");
        var wire = rig.Wire; RequestsPage page = rig.Requests;
        int Navigated(string id) => rig.Navigations.Count(entry => entry.Page == typeof(ItemDetailPage) && (entry.Parameter as string) == id);
        Button Link(string title) => Descendants(rig.Requests).OfType<Button>().Single(button => AutomationProperties.GetName(button) == $"Open {title} in library");

        Click(Link("Library ok")); await Until(() => Navigated("lib-ok") == 1);
        if (wire.CatalogReads("lib-ok") != 1) throw new InvalidOperationException("The accessible library copy was not read exactly once before navigation.");

        Click(Link("Library missing")); await Until(() => wire.CatalogReads("lib-404") == 1);
        await rig.ToastAsync(Notice);
        await Task.Delay(150);
        if (Navigated("lib-404") != 0 || rig.Frame.Content is not RequestsPage) throw new InvalidOperationException("An inaccessible library copy navigated instead of staying with a notice.");
        if (!Link("Library missing").IsEnabled) throw new InvalidOperationException("The link is not usable again after the access notice.");

        Click(Link("Library broken")); await Until(() => Navigated("lib-500") == 1);

        wire.Gates["lib-slow"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(Link("Library slow")); await Until(() => wire.CatalogReads("lib-slow") == 1);
        Click(Link("Library slow")); await Task.Delay(150);
        wire.Gates["lib-slow"].SetResult();
        await Until(() => Navigated("lib-slow") == 1); await Task.Delay(100);
        if (wire.CatalogReads("lib-slow") != 1 || Navigated("lib-slow") != 1) throw new InvalidOperationException("Double click read or navigated more than once.");

        wire.Gates["lib-access"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(Link("Library access")); await Until(() => wire.CatalogReads("lib-access") == 1);
        rig.Client.InvalidateAccessContext();
        wire.Gates["lib-access"].TrySetResult(); await Task.Delay(250);
        if (Navigated("lib-access") != 0) throw new InvalidOperationException("A library check accepted success from retired access authority.");

        // The viewer leaves while a check is pending: neither a late success nor a late not-found may act.
        await rig.NoToastAsync(Notice);
        wire.Gates["lib-late"] = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.Gates["lib-late404"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(Link("Library late")); Click(Link("Library late missing"));
        await Until(() => wire.CatalogReads("lib-late") == 1 && wire.CatalogReads("lib-late404") == 1);
        rig.Navigation.NavigateImmediately(typeof(Page)); await Task.Delay(120);
        wire.Gates["lib-late"].SetResult(); wire.Gates["lib-late404"].SetResult();
        await Task.Delay(400);
        if (Navigated("lib-late") != 0 || rig.ToastTexts().Any(text => text.Contains(Notice)))
            throw new InvalidOperationException("A check abandoned by navigation still navigated or reported a notice.");

        // Returning mounts the cached page again with a fresh owner; a profile switch then retires the pending check.
        rig.Navigation.GoBackImmediately(); await Until(() => rig.Frame.Content is RequestsPage { IsLoaded: true });
        page = (RequestsPage)rig.Frame.Content;
        Invoke((Button)page.FindName("YoursTabButton"));
        wire.Gates["lib-profile"] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Click(Descendants(page).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Open Library profile in library"));
        await Until(() => wire.CatalogReads("lib-profile") == 1);
        rig.Client.SetProfile("profile-b");
        wire.Gates["lib-profile"].SetResult(); await Task.Delay(400);
        if (Navigated("lib-profile") != 0) throw new InvalidOperationException("A check started by the previous profile navigated for the new one.");
        Program.Log("PASS: Requests rows check library access before navigating (open, not-found notice, transient open, double click, departure, profile retirement).");
    }

    private static async Task DetailLinkAsync(StackPanel parent)
    {
        using var rig = new Rig(parent, remove: true, detail: true);
        var wire = rig.Wire;
        wire.CatalogStatus["lib-detail"] = HttpStatusCode.ServiceUnavailable;
        rig.Navigation.NavigateImmediately(typeof(RequestDetailPage), new RequestDetailNavigation("movie", 7));
        var page = (RequestDetailPage)rig.Frame.Content;
        await Until(() => ((FrameworkElement)page.FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
        int Navigated() => rig.Navigations.Count(entry => entry.Page == typeof(ItemDetailPage) && (entry.Parameter as string) == "lib-detail");
        Button Open() => ((SiloPlayer.Controls.WrapPanel)page.FindName("ActionsPanel")).Children.OfType<Button>().First(button => button.Tag is "lib-detail");
        var initial = wire.CatalogReads("lib-detail");
        if (initial != 1 || Navigated() != 0) throw new InvalidOperationException("Opening the request title should check, but not navigate to, a transiently unreadable library copy.");
        await MediaParityNativeFixture.CaptureAsync(page, "request-detail-library-link.png");

        wire.CatalogStatus["lib-detail"] = HttpStatusCode.NotFound;
        Click(Open()); await Until(() => wire.CatalogReads("lib-detail") == initial + 1);
        await rig.ToastAsync(Notice); await Task.Delay(120);
        if (Navigated() != 0 || rig.Frame.Content is not RequestDetailPage || !Open().IsEnabled) throw new InvalidOperationException("Request detail navigated to a library copy this profile cannot open, or stranded the link.");

        wire.CatalogStatus["lib-detail"] = HttpStatusCode.InternalServerError;
        Click(Open()); await Until(() => Navigated() == 1);

        wire.CatalogStatus.Remove("lib-detail"); rig.Cache.Invalidate("lib-detail");
        wire.Gates["lib-detail"] = new(TaskCreationOptions.RunContinuationsAsynchronously); var reads = wire.CatalogReads("lib-detail");
        Click(Open()); await Until(() => wire.CatalogReads("lib-detail") == reads + 1);
        Click(Open()); await Task.Delay(120);
        wire.Gates["lib-detail"].SetResult(); await Until(() => Navigated() == 2); await Task.Delay(100);
        if (wire.CatalogReads("lib-detail") != reads + 1 || Navigated() != 2) throw new InvalidOperationException("Request detail double click read or navigated more than once.");

        rig.Cache.Invalidate("lib-detail");
        wire.Gates["lib-detail"] = new(TaskCreationOptions.RunContinuationsAsynchronously); reads = wire.CatalogReads("lib-detail");
        Click(Open()); await Until(() => wire.CatalogReads("lib-detail") == reads + 1);
        rig.Client.InvalidateAccessContext();
        wire.Gates["lib-detail"].TrySetResult(); await Task.Delay(250);
        if (Navigated() != 2) throw new InvalidOperationException("Request detail accepted a library read after access authority retired.");

        rig.Cache.Invalidate("lib-detail");
        wire.Gates["lib-detail"] = new(TaskCreationOptions.RunContinuationsAsynchronously); reads = wire.CatalogReads("lib-detail");
        var button = Open(); Click(button); await Until(() => wire.CatalogReads("lib-detail") == reads + 1);
        rig.Navigation.NavigateImmediately(typeof(Page)); await Task.Delay(120);
        wire.Gates["lib-detail"].SetResult(); await Task.Delay(400);
        if (Navigated() != 2) throw new InvalidOperationException("A request-detail check abandoned by navigation still navigated.");
        Program.Log("PASS: Request detail checks library access before navigating (transient link retained, not-found notice, transient open, double click, departure).");
    }

    // ---- harness -------------------------------------------------------------------------------------------------

    private static string Row(string id, string title, string status = "approved", string outcome = "failed", string? library = null, string? targets = null) =>
        $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"media_type\":\"movie\",\"tmdb_id\":{Math.Abs(id.GetHashCode() % 1000)},\"status\":\"{status}\",\"outcome\":\"{outcome}\"" +
        (library == null ? "" : $",\"library_content_id\":\"{library}\"") + (targets == null ? "" : $",\"targets\":{targets}") +
        $",\"created_at\":\"{DateTimeOffset.UtcNow.AddDays(-2):O}\",\"updated_at\":\"{DateTimeOffset.UtcNow.AddMinutes(-9):O}\"" + (outcome == "failed" ? ",\"last_error\":\"Fixture failure\"" : "") + "}";

    private static IEnumerable<TextBlock> GuideTexts(RequestsPage page)
    {
        var help = (Button)typeof(RequestsPage).GetField("_mineHelp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        return ((Grid)((Border)((Flyout)help.Flyout).Content).Child).Children.OfType<TextBlock>();
    }
    private static IEnumerable<string> ButtonNames(DependencyObject root) => Descendants(root).OfType<Button>().Select(AutomationProperties.GetName).Where(name => !string.IsNullOrEmpty(name));
    private static string[] RemoveNames(DependencyObject root) => ButtonNames(root).Where(name => name.StartsWith("Remove request for ")).ToArray();
    private static Button Remove(DependencyObject root, string title) => Descendants(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Remove request for " + title);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Invoke(Button button) => Click(button);
    private static void Primary(ContentDialog dialog) => Click(Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == dialog.PrimaryButtonText));
    private static object? InvokePrivate(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(8);
        while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Request removal fixture did not settle."); await Task.Delay(20); }
    }

    private sealed class Rig : IDisposable
    {
        private readonly StackPanel _parent; private readonly FieldInfo _servicesField; private readonly IServiceProvider _outer;
        private readonly ServiceProvider _services; private readonly HttpClient _http; private readonly ToastContainer _toast;
        internal Wire Wire { get; } = new(); internal SiloApiClient Client { get; } internal NavigationService Navigation { get; } = new();
        internal Frame Frame { get; } internal ItemDetailPrefetchCache Cache { get; }
        internal List<(Type Page, object? Parameter)> Navigations { get; } = [];
        internal RequestsPage Requests => (RequestsPage)Frame.Content;

        internal Rig(StackPanel parent, bool remove, bool libraryRows = false, bool detail = false)
        {
            _parent = parent; Wire.RemoveSupported = remove;
            if (detail) Wire.DetailLibrary = "lib-detail";
            else if (libraryRows)
                foreach (var (id, content, title) in new[] { ("l1", "lib-ok", "Library ok"), ("l2", "lib-404", "Library missing"), ("l3", "lib-500", "Library broken"), ("l4", "lib-slow", "Library slow"),
                    ("l5", "lib-late", "Library late"), ("l6", "lib-late404", "Library late missing"), ("l7", "lib-profile", "Library profile"), ("l8", "lib-access", "Library access") })
                    Wire.Mine[id] = Row(id, title, status: "completed", outcome: "completed", library: content);
            else
            {
                Wire.Mine["f1"] = Row("f1", "Failed one");
                Wire.Mine["f2"] = Row("f2", "Failed two", targets: "[{\"quality\":\"1080p\",\"status\":\"failed\"}]");
                Wire.Mine["f3"] = Row("f3", "Failed three");
                Wire.Mine["p1"] = Row("p1", "Partly delivered", targets: "[{\"quality\":\"1080p\",\"status\":\"completed\"},{\"quality\":\"2160p\",\"status\":\"failed\"}]");
                Wire.Mine["r1"] = Row("r1", "Retryable", status: "pending", outcome: "active");
                Wire.Mine["d1"] = Row("d1", "Declined one", status: "pending", outcome: "declined");
            }
            _http = new HttpClient(Wire);
            Client = new SiloApiClient(_http); Client.SetBaseUrl("https://request-removal.invalid"); Client.SetProfile("profile-a");
            var api = new RequestsApi(Client); var catalog = new CatalogApi(Client); var settings = new SettingsApi(Client);
            Cache = new ItemDetailPrefetchCache((id, ct) => catalog.GetItemDetailAsync(id, ct), contextKey: () => Client.CaptureContext().ToString());
            var vm = new RequestsViewModel(api);
            var toast = new ToastService();
            _outer = SiloPlayer.App.Services;
            _services = new ServiceCollection().AddSingleton(api).AddSingleton(Client).AddSingleton(catalog).AddSingleton(settings).AddSingleton(Cache).AddSingleton(Navigation)
                .AddSingleton(vm).AddSingleton(toast).AddSingleton<WatchlistViewModel>()
                .AddSingleton(new UICustomizationService(settings)).AddSingleton(new CardOverlayService(settings))
                .AddSingleton(_outer.GetRequiredService<AuthService>()).AddSingleton(_outer.GetRequiredService<SettingsService>()).AddSingleton(_outer.GetRequiredService<ImageService>())
                .BuildServiceProvider();
            _servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
            _servicesField.SetValue(null, new Composite(_services, _outer));
            _toast = new ToastContainer(); parent.Children.Add(_toast); toast.Register(_toast, parent.DispatcherQueue);
            Frame = new Frame { Width = 900, Height = 900 }; parent.Children.Add(Frame); Navigation.Frame = Frame;
            Navigation.NavigationRequestHandler = (type, parameter) => { if (type == typeof(ItemDetailPage)) { Navigations.Add((type, parameter)); return true; } return false; };
            Toasts = toast;
        }
        internal ToastService Toasts { get; }

        internal async Task OpenAsync(string tab)
        {
            Navigation.NavigateImmediately(typeof(RequestsPage));
            await Until(() => Requests.IsLoaded && Requests.ViewModel.MyRequests.Count == Wire.Mine.Count && !Requests.ViewModel.IsLoadingMine);
            if (tab == "yours") Invoke((Button)Requests.FindName("YoursTabButton"));
            await Until(() => ((FrameworkElement)Requests.FindName("MyRequestsSection")).Visibility == Visibility.Visible);
        }
        internal ContentDialog? Dialog() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_parent.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child))).OfType<ContentDialog>().FirstOrDefault();
        internal async Task<ContentDialog> DialogAsync() { ContentDialog? dialog = null; await Until(() => (dialog = Dialog()) != null); return dialog!; }
        internal IEnumerable<string> ToastTexts() => Descendants(_toast).OfType<TextBlock>().Select(text => text.Text);
        internal async Task NoToastAsync(string text) => await Until(() => !ToastTexts().Any(value => value.Contains(text)));
        internal async Task ToastAsync(string? text)
        {
            await Until(() => text == null ? ToastTexts().Any(value => !string.IsNullOrWhiteSpace(value)) : ToastTexts().Any(value => value.Contains(text)));
        }
        public void Dispose()
        {
            foreach (var gate in Wire.Gates.Values) gate.TrySetResult();
            Wire.DeleteGate?.TrySetResult();
            Wire.StatusGate?.TrySetResult();
            Wire.MineGate?.TrySetResult();
            Navigation.NavigationRequestHandler = null;
            try { Frame.Content = null; } catch { }
            _parent.Children.Remove(Frame); Navigation.Frame = null; Toasts.Unregister(); _parent.Children.Remove(_toast);
            Task.Delay(120).GetAwaiter().GetResult();
            _servicesField.SetValue(null, _outer); _services.Dispose(); _http.Dispose();
        }
    }

    private sealed class Composite(IServiceProvider local, IServiceProvider outer) : IServiceProvider
    {
        public object? GetService(Type type) => local.GetService(type) ?? outer.GetService(type);
    }

    internal sealed record Write(string Method, string Path, bool HadBody);

    internal sealed class Wire : HttpMessageHandler
    {
        internal bool RemoveSupported = true; internal string? DetailLibrary;
        internal string DetailRequest = "{\"requestable\":false,\"status\":\"\"}";
        internal readonly SortedDictionary<string, string> Mine = new(StringComparer.Ordinal);
        internal HttpStatusCode DeleteMode = HttpStatusCode.NoContent; internal TaskCompletionSource? DeleteGate; internal Action<string>? OnDelete;
        internal readonly List<Write> Writes = []; internal int MineReads, DiscoverReads;
        internal int StatusReads; internal TaskCompletionSource? StatusGate, MineGate;
        internal readonly Dictionary<string, TaskCompletionSource> Gates = [];
        internal readonly Dictionary<string, HttpStatusCode> CatalogStatus = new() { ["lib-404"] = HttpStatusCode.NotFound, ["lib-500"] = HttpStatusCode.InternalServerError };
        private readonly Dictionary<string, int> _catalogReads = [];
        internal int CatalogReads(string id) { lock (_catalogReads) return _catalogReads.GetValueOrDefault(id); }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                Writes.Add(new(request.Method.Method, path, request.Content != null));
                if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v2/requests/"))
                {
                    if (DeleteGate is { } gate) await gate.Task.WaitAsync(ct);
                    var id = path.Split('/').Last();
                    if (DeleteMode == HttpStatusCode.NoContent) { OnDelete?.Invoke(id); return new(HttpStatusCode.NoContent); }
                    OnDelete?.Invoke(id);
                    return Problem(DeleteMode, DeleteMode == HttpStatusCode.Conflict ? "This request is no longer failed." : "Fixture outage");
                }
                if (path == "/api/v2/requests/r1/cancel") { Mine["r1"] = Row("r1", "Retryable", status: "pending", outcome: "cancelled"); return Json("{\"id\":\"r1\",\"status\":\"pending\",\"outcome\":\"cancelled\"}"); }
                throw new InvalidOperationException("Unexpected fixture mutation " + request.Method + " " + path);
            }
            if (path.StartsWith("/api/v2/catalog/items/"))
            {
                var id = Uri.UnescapeDataString(path["/api/v2/catalog/items/".Length..]);
                lock (_catalogReads) _catalogReads[id] = _catalogReads.GetValueOrDefault(id) + 1;
                if (Gates.TryGetValue(id, out var gate)) await gate.Task.WaitAsync(ct);
                if (CatalogStatus.TryGetValue(id, out var status)) return Problem(status, "Fixture library response");
                if (id == "lib-late404") return Problem(HttpStatusCode.NotFound, "Fixture library response");
                return Json($"{{\"content_id\":\"{id}\",\"type\":\"movie\",\"title\":\"Library copy\"}}");
            }
            switch (path)
            {
                case "/api/v2/requests/status":
                    StatusReads++;
                    if (StatusGate is { } statusGate) await statusGate.Task.WaitAsync(ct);
                    return Json($"{{\"requests_enabled\":true,\"allowed\":true,\"remove_failed_supported\":{RemoveSupported.ToString().ToLowerInvariant()}}}");
                case "/api/v2/requests/mine":
                    MineReads++;
                    if (MineGate is { } mineGate) await mineGate.Task.WaitAsync(ct);
                    return Json("{\"items\":[" + string.Join(",", Mine.Values) + "]}");
                case "/api/v2/requests/discover":
                    DiscoverReads++;
                    return Json("{\"items\":[]}");
                case "/api/v2/requests/detail/movie/7":
                    return Json($"{{\"media_type\":\"movie\",\"tmdb_id\":7,\"title\":\"Linked title\",\"availability\":\"available\",\"library_content_id\":\"{DetailLibrary}\",\"request\":{DetailRequest}}}");
                case "/api/v2/requests/r1":
                    return Json(Mine["r1"]);
            }
            return Json("{\"items\":[]}");
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Problem(HttpStatusCode status, string detail) => new(status) { Content = new StringContent(JsonSerializer.Serialize(new { title = detail, detail }), Encoding.UTF8, "application/problem+json") };
    }
}
