using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class NotificationStatesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://notification-states.invalid"); client.SetProfile("fixture");
        var vm = new NotificationsViewModel(new NotificationsApi(client));
        wire.Paging = Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") == "notification-pages";
        if (wire.Paging) wire.Preferences.TrySetResult();
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("fixture");
        using var events = new EventChannelClient(client, auth);
        field.SetValue(null, new Services(original, vm, client, events));
        var frame = new Frame { Width = 500, Height = 800 }; parent.Children.Add(frame);
        var differences = new List<string>();
        try
        {
            frame.Navigate(typeof(NotificationsPage)); var page = (NotificationsPage)frame.Content;
            if (wire.Paging)
            {
                await Until(() => page.IsLoaded && vm.HasMore && !vm.IsLoading);
                var more = (Button)page.FindName("LoadMoreButton");
                page.UpdateLayout(); await Task.Delay(60);
                var rows = (ListView)page.FindName("NotificationsList");
                var last = (ListViewItem)rows.ContainerFromIndex(vm.Notifications.Count - 1);
                var lastBottom = last.TransformToVisual(page).TransformPoint(new()).Y + last.ActualHeight;
                var footerGap = more.TransformToVisual(page).TransformPoint(new()).Y - lastBottom;
                if (footerGap < 16 || footerGap > 32)
                    throw new InvalidOperationException($"Load more must follow the actual last notification, not be pinned to the viewport bottom; gap={footerGap}.");
                Invoke(more);
                await Until(() => wire.ContinuationStarted);
                if (more.IsEnabled) throw new InvalidOperationException("Actual Load more stays enabled while its continuation is pending.");
                wire.Continuation.TrySetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"older\"}],\"page\":{\"has_more\":false},\"read_cutoff\":\"older-cutoff\"}") });
                await Until(() => vm.Notifications.Count == 2 && !vm.HasMore);
                if (more.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Completed last-page Load more remains visible.");
                for (var index = 0; index < 100; index++)
                    vm.Notifications.Add(new() { Id = $"long-{index}", SeriesTitle = $"Long inbox {index}" });
                page.UpdateLayout(); await Task.Delay(80);
                var scroll = Descendants(rows).OfType<ScrollViewer>().First();
                if (rows.ActualHeight > frame.ActualHeight + 1 || scroll.ScrollableHeight < 1000)
                    throw new InvalidOperationException("Natural short-list sizing removed the bounded scroll viewport for a long inbox.");
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                await Until(() => scroll.VerticalOffset > 1000);
                rows.UpdateLayout(); await Task.Delay(80);
                var final = (ListViewItem?)rows.ContainerFromIndex(vm.Notifications.Count - 1);
                if (final == null || final.TransformToVisual(page).TransformPoint(new()).Y > frame.ActualHeight)
                    throw new InvalidOperationException("The last notification cannot be reached by scrolling the long inbox.");
                Program.Log("PASS: NOTIFICATION_PAGES_COMPLETED actual pending control disables and final continuation appends/hides control.");
                Program.Log("PASS: NOTIFICATION_FLOW_COMPLETED short-list footer gap24px and bounded virtualized long-list scrolling.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") == "notification-realtime")
            {
                wire.Preferences.TrySetResult();
                await Until(() => page.IsLoaded && vm.Notifications.Count == 1 && !vm.IsLoading);
                await CheckRealtimeAsync(frame, vm, events, wire);
                return;
            }
            await Until(() => page.IsLoaded && vm.Notifications.Count == 1);
            page.UpdateLayout(); await Task.Delay(100);
            if (((FrameworkElement)page.FindName("LoadingSkeleton")).Visibility != Visibility.Collapsed ||
                ((FrameworkElement)page.FindName("NotificationsList")).Visibility != Visibility.Visible)
                differences.Add("Completed inbox remains hidden behind the skeleton while optional preferences are pending.");
            if (page.FindName("PreferenceLoadingPanel") is not FrameworkElement loading || loading.Visibility != Visibility.Visible ||
                ((FrameworkElement)page.FindName("PreferenceLoadFailedPanel")).Visibility != Visibility.Collapsed)
                differences.Add("Pending preferences show failure instead of the original three-row skeleton.");
            await MediaParityNativeFixture.CaptureAsync(frame, "notification-pending-preferences.png");
            wire.Preferences.TrySetResult(); await Until(() => vm.HasLoadedPreferences);
            wire.Mode = "empty"; Invoke((Button)page.FindName("ReloadButton"));
            await Until(() => vm.IsEmpty && !vm.IsLoading); page.UpdateLayout(); await Task.Delay(50);
            var empty = (StackPanel)page.FindName("EmptyState");
            if (empty.Visibility != Visibility.Visible || empty.VerticalAlignment != VerticalAlignment.Top || empty.Margin.Top != 80 || empty.Spacing != 12)
                differences.Add("Empty inbox must follow the tabs with80px vertical padding and12px content gaps rather than centering in unused page height.");
            await MediaParityNativeFixture.CaptureAsync(frame, "notification-empty.png");
            wire.Mode = "error"; Invoke((Button)page.FindName("ReloadButton"));
            await Until(() => !string.IsNullOrWhiteSpace(vm.ErrorMessage)); page.UpdateLayout(); await Task.Delay(50);
            if (empty.Visibility != Visibility.Collapsed || ((Button)page.FindName("ReloadButton")).Visibility != Visibility.Visible)
                differences.Add("A failed inbox request incorrectly paints the successful-empty message alongside its error.");
            await MediaParityNativeFixture.CaptureAsync(frame, "notification-error.png");
            wire.Mode = "loaded"; Invoke((Button)page.FindName("ReloadButton"));
            await Until(() => vm.Notifications.Count == 1 && vm.ErrorMessage == null && !vm.IsLoading);
            Invoke((Button)page.FindName("UnreadButton")); await Until(() => vm.StatusFilter == "unread" && vm.Notifications.Count == 0 && !vm.IsLoading);
            if (((TextBlock)page.FindName("EmptyTitleText")).Text != "No unread notifications")
                differences.Add("Actual unread-tab invocation did not render its filtered-empty message.");
            if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
            Program.Log("PASS: NOTIFICATION_STATES_COMPLETED optional pending/rendered inbox, preferences skeleton, empty spacing, error exclusion, Reload and actual unread filter.");
        }
        finally
        {
            wire.Preferences.TrySetResult(); wire.Continuation.TrySetResult(new(HttpStatusCode.NoContent)); vm.CancelPendingLoad(); frame.Navigate(typeof(Page)); parent.Children.Remove(frame);
            await Task.Delay(100); field.SetValue(null, original);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") == "notification-realtime" &&
                typeof(EventChannelClient).GetField("EventReceived", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(events) != null)
                throw new InvalidOperationException("Unloaded inbox retains its realtime handler.");
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Notification state did not settle."); }
    private static async Task CheckRealtimeAsync(Frame frame, NotificationsViewModel vm, EventChannelClient events, Wire wire)
    {
        void Emit(string name, string json)
        {
            using var data = JsonDocument.Parse(json);
            var callback = (Action<string, string, JsonElement>?)typeof(EventChannelClient)
                .GetField("EventReceived", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(events);
            callback?.Invoke("notifications", name, data.RootElement.Clone());
        }
        var reads = wire.ListReads;
        const string created = "{\"id\":\"arrival\",\"profile_id\":\"fixture\",\"type\":\"episode.available\",\"series_title\":\"New arrival\",\"created_at\":\"2026-10-09T12:05:00Z\"}";
        Emit("notification.created", created); await Task.Delay(100);
        if (vm.Notifications.Count != 2 || vm.Notifications[0].Id != "arrival")
            throw new InvalidOperationException("Mounted inbox does not receive the same realtime created event as the WebUI; it remains stale until navigation/reload.");
        Emit("notification.created", created); await Task.Delay(50);
        Emit("notification.created", "{\"id\":\"foreign\",\"profile_id\":\"different-profile\"}"); await Task.Delay(50);
        if (vm.Notifications.Count != 2 || vm.UnreadCount != 1) throw new InvalidOperationException("Duplicate/foreign-profile arrivals change rows or unread count.");
        Emit("notification.read", "{\"profile_id\":\"fixture\",\"id\":\"arrival\"}"); await Task.Delay(50);
        if (vm.Notifications[0].IsUnread || vm.UnreadCount != 0) throw new InvalidOperationException("Actual read event did not repaint the cached row/count.");
        var list = (ListView)((NotificationsPage)frame.Content).FindName("NotificationsList");
        list.UpdateLayout();
        var realized = (ListViewItem)list.ContainerFromItem(vm.Notifications[0]);
        var title = Descendants(realized).OfType<TextBlock>().Single(text => text.Name == "NotificationTitleText");
        var indicator = Descendants(realized).OfType<Microsoft.UI.Xaml.Shapes.Ellipse>().Single();
        if (title.FontWeight.Weight != 500 || indicator.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Read event updates the VM but leaves its actual realized row styled as unread.");
        Emit("notification.read", "{\"profile_id\":\"fixture\",\"id\":\"arrival\"}"); await Task.Delay(50);
        if (vm.UnreadCount != 0 || wire.ListReads != reads) throw new InvalidOperationException("Ordinary created/read events refetch the inbox or double-decrement.");
        Invoke((Button)((NotificationsPage)frame.Content).FindName("UnreadButton"));
        await Until(() => vm.StatusFilter == "unread" && vm.IsEmpty && !vm.IsLoading);
        Emit("notification.created", created); await Task.Delay(50);
        if (vm.Notifications.Count != 1) throw new InvalidOperationException("Unread inbox does not receive new unread rows.");
        Emit("notification.read", "{\"profile_id\":\"fixture\",\"id\":\"arrival\"}"); await Task.Delay(50);
        if (!vm.IsEmpty || vm.Notifications.Count != 0) throw new InvalidOperationException("Read event does not remove the row from the unread-only filter.");
        wire.UnreadAvailable = true; reads = wire.ListReads;
        using var snapshot = JsonDocument.Parse("[{\"id\":\"one\",\"profile_id\":\"fixture\"}]");
        ((Action<string, JsonElement>?)typeof(EventChannelClient).GetField("SnapshotReceived", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(events))?
            .Invoke("notifications", snapshot.RootElement.Clone());
        await Until(() => wire.ListReads > reads && vm.Notifications.Count == 1);
        Program.Log("PASS: NOTIFICATION_REALTIME_COMPLETED actual subscription created/read/duplicate/foreign profile, all/unread lists, snapshot reconciliation and unload ownership.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class Services(IServiceProvider original, NotificationsViewModel vm, SiloApiClient client, EventChannelClient events) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(NotificationsViewModel) ? vm
            : type == typeof(SiloApiClient) ? client : type == typeof(EventChannelClient) ? events : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal string Mode = "loaded";
        internal int ListReads;
        internal bool UnreadAvailable;
        internal bool Paging, ContinuationStarted;
        internal readonly TaskCompletionSource<HttpResponseMessage> Continuation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Preferences = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "notification-states.invalid") throw new InvalidOperationException("Unexpected notification network request.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/notifications")) ListReads++;
            if (Paging && path.EndsWith("/notifications") && request.RequestUri.Query.Contains("cursor="))
            { ContinuationStarted = true; return await Continuation.Task.WaitAsync(ct); }
            if (path.EndsWith("/preferences")) await Preferences.Task.WaitAsync(ct);
            if (path.EndsWith("/notifications") && Mode == "error") return new(HttpStatusCode.ServiceUnavailable);
            var json = path.EndsWith("/preferences") ? "{\"enabled\":true}"
                : path.EndsWith("/unread-count") ? "{\"count\":0}"
                : Mode == "empty" || request.RequestUri.Query.Contains("status=unread") && !UnreadAvailable ? "{\"items\":[],\"read_cutoff\":\"fixture-cutoff\"}"
                : "{\"items\":[{\"id\":\"one\",\"type\":\"episode.available\",\"series_title\":\"Fixture series\",\"created_at\":\"2026-10-09T12:00:00Z\"}],\"read_cutoff\":\"fixture-cutoff\"}";
            if (Paging && path.EndsWith("/notifications")) json = json[..^1] + ",\"page\":{\"has_more\":true,\"next_cursor\":\"next\"}}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
