using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;
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
        var api = new NotificationsApi(client); var vm = new NotificationsViewModel(api);
        wire.Paging = Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") == "notification-pages";
        if (wire.Paging) wire.Preferences.TrySetResult();
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("fixture");
        using var events = new EventChannelClient(client, auth);
        field.SetValue(null, new Services(original, vm, client, api, events));
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
                var last = NotificationFixtureUi.Row(page, vm.Notifications[^1].Id);
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
                var final = NotificationFixtureUi.Row(page, vm.Notifications[^1].Id);
                if (final == null || final.TransformToVisual(page).TransformPoint(new()).Y > frame.ActualHeight)
                    throw new InvalidOperationException("The last notification cannot be reached by scrolling the long inbox.");
                Program.Log("PASS: NOTIFICATION_PAGES_COMPLETED actual pending control disables and final continuation appends/hides control.");
                Program.Log("PASS: NOTIFICATION_FLOW_COMPLETED short-list footer gap24px and bounded long-list scrolling.");
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
                ((FrameworkElement)page.FindName("NotificationsList")).Visibility != Visibility.Visible ||
                ((FrameworkElement)page.FindName("InboxGroupsHost")).Visibility != Visibility.Visible ||
                NotificationFixtureUi.Rows(page).Length != 1)
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
            await CheckCurrentInboxAsync(frame, page, vm, wire);
            Program.Log("PASS: NOTIFICATION_STATES_COMPLETED optional pending/rendered inbox, preferences skeleton, empty spacing, error exclusion, Reload and actual unread filter.");
        }
        finally
        {
            wire.Preferences.TrySetResult(); wire.Continuation.TrySetResult(new(HttpStatusCode.NoContent)); vm.CancelPendingLoad();
            await NotificationFixtureUi.DetachAsync(frame, () => { frame.Navigate(typeof(Page)); parent.Children.Remove(frame); });
            field.SetValue(null, original);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") == "notification-realtime" &&
                typeof(EventChannelClient).GetField("EventReceived", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(events) != null)
                throw new InvalidOperationException("Unloaded inbox retains its realtime handler.");
        }
    }
    private static async Task CheckCurrentInboxAsync(Frame frame, NotificationsPage page, NotificationsViewModel vm, Wire wire)
    {
        var now = DateTimeOffset.Now;
        wire.Inbox = [
            new() { Id = "boundary", Type = "request.approved", CreatedAt = now.ToString("O"), ReasonFlags = new() { Title = "Request boundary", Favorite = true } },
            new() { Id = "episode-one", Type = "episode.available", SeriesId = "group", SeriesTitle = "Grouped series", CreatedAt = now.AddDays(-2).ToString("O"), SeasonNumber = 1, EpisodeNumber = 1 },
            new() { Id = "episode-two", Type = "episode.available", SeriesId = "group", SeriesTitle = "Grouped series", CreatedAt = now.AddDays(-2).AddMinutes(-1).ToString("O"), SeasonNumber = 1, EpisodeNumber = 2 }
        ];
        Invoke((Button)page.FindName("AllButton"));
        await Until(() => vm.Notifications.Count == 3 && NotificationFixtureUi.Rows(page).Length == 2);
        var grouped = NotificationFixtureUi.Row(page, "episode-one");
        var expand = Descendants(grouped).OfType<Expander>().Single();
        ((IExpandCollapseProvider)new ExpanderAutomationPeer(expand).GetPattern(PatternInterface.ExpandCollapse)).Expand();
        await Until(() => expand.IsExpanded);
        // The collapsed summary already shows "S1E1 – S1E2"; both episode lines must appear beneath it.
        if (!await Settles(() => { grouped.UpdateLayout(); return Descendants(grouped).OfType<TextBlock>().Count(text => text.Text.StartsWith("S1E", StringComparison.Ordinal)) >= 3; }))
            throw new InvalidOperationException("Expanded group omits its episode lines.");
        foreach (var filter in new[] { "all", "unread" })
        {
            Invoke((Button)page.FindName(filter == "all" ? "AllButton" : "UnreadButton"));
            await Until(() => vm.StatusFilter == filter && !vm.IsLoading && vm.Notifications.Count == 3);
            var previous = vm.Notifications.Single(item => item.Id == "episode-one");
            grouped = NotificationFixtureUi.Row(page, "episode-one");
            await vm.ReloadAsync();
            page.UpdateLayout();
            if (ReferenceEquals(previous, vm.Notifications.Single(item => item.Id == "episode-one")) ||
                !ReferenceEquals(grouped, NotificationFixtureUi.Row(page, "episode-one")) ||
                !Descendants(grouped).OfType<Expander>().Single().IsExpanded)
                throw new InvalidOperationException("Unchanged reload must replace notification objects while retaining the expanded group.");
            var episodeLink = Descendants(grouped).OfType<Button>().Single(button => button.Content is TextBlock text && text.Text.StartsWith("S1E1", StringComparison.Ordinal));
            var episodeLine = (Grid)episodeLink.Parent;
            Invoke(episodeLine.Children.OfType<Button>().Single(button => button != episodeLink));
            await Until(() => !vm.MarkReadCommand.IsRunning && (filter == "unread"
                ? vm.Notifications.All(item => item.Id != "episode-one")
                : !vm.Notifications.Single(item => item.Id == "episode-one").IsUnread));
            page.UpdateLayout();
            if (filter == "all" && !Descendants(grouped).OfType<TextBlock>().Any(text => text.Text == "1 new"))
                throw new InvalidOperationException("Mark-read after unchanged reload leaves the expanded group's unread badge stale.");
            // Restore server data for the next filter and the existing expansion checks.
            wire.Inbox.Single(item => item.Id == "episode-one").ReadAt = null;
        }
        Invoke((Button)page.FindName("AllButton"));
        await Until(() => vm.StatusFilter == "all" && !vm.IsLoading && vm.Notifications.Count == 3);
        Program.Log("PASS: expanded group mark-read resolves reloaded instances in All and removes them from Unread.");
        foreach (var notification in wire.Inbox.Where(item => item.Type == "episode.available")) notification.ReadAt = now.ToString("O");
        Invoke((Button)page.FindName("UnreadButton"));
        await Until(() => vm.StatusFilter == "unread" && !vm.IsLoading && vm.Notifications.Count == 1);
        Invoke((Button)page.FindName("AllButton"));
        await Until(() => vm.StatusFilter == "all" && !vm.IsLoading && vm.Notifications.Count == 3 && NotificationFixtureUi.Rows(page).Length == 2);
        if (!Descendants(NotificationFixtureUi.Row(page, "episode-one")).OfType<Expander>().Single().IsExpanded)
            throw new InvalidOperationException("All/Unread filter discarded a group's saved expansion while the group was absent.");
        Invoke((Button)page.FindName("InboxSelectButton"));
        var requestRow = NotificationFixtureUi.Row(page, "boundary");
        var checkbox = Descendants(requestRow).OfType<CheckBox>().Single();
        ((IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)).Toggle();
        await Until(() => vm.SelectedInboxEntries.Length == 1);
        var summary = ((TextBlock)page.FindName("SelectionSummary")).Text;
        if (!summary.Contains("· 1 notification", StringComparison.Ordinal) || summary.Contains("· 1 notifications", StringComparison.Ordinal))
            throw new InvalidOperationException("Selected single notification uses the wrong count fallback.");
        Invoke((Button)page.FindName("InboxSelectButton"));
        frame.Width = 700; page.UpdateLayout();
        await Until(() => page.ActualWidth >= 640);
        if (!await Settles(() => { page.UpdateLayout(); return Descendants(requestRow).OfType<TextBlock>().Any(text => text.Text == "Request" && text.ActualWidth > 0); }) ||
            Descendants(requestRow).OfType<TextBlock>().Any(text => text.Text == "Favorite"))
            throw new InvalidOperationException("Request row omits its tag or retains obsolete relationship chips.");
        var oldParent = requestRow.Parent;
        var moving = vm.Notifications.Single(item => item.Id == "boundary"); moving.CreatedAt = now.AddDays(-1).ToString("O");
        vm.Notifications[vm.Notifications.IndexOf(moving)] = moving;
        await Until(() => requestRow.IsLoaded && requestRow.Parent != oldParent);
        if (!ReferenceEquals(requestRow, NotificationFixtureUi.Row(page, "boundary")) ||
            !Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Yesterday" && Microsoft.UI.Xaml.Automation.AutomationProperties.GetHeadingLevel(text) == AutomationHeadingLevel.Level2))
            throw new InvalidOperationException("Day rollover failed to move the retained row or lost accessible day headings.");
        await MediaParityNativeFixture.CaptureAsync(frame, "notification-grouped-review.png");
        Program.Log("PASS: grouped expansion survives absent filtered groups; single selection grammar, Request tag, retained row day move and accessible headings.");
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Notification state did not settle."); }
    private static async Task<bool> Settles(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return true; await Task.Delay(20); } return false; }
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
        var realized = NotificationFixtureUi.Row((NotificationsPage)frame.Content, vm.Notifications[0].Id);
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
    private sealed class Services(IServiceProvider original, NotificationsViewModel vm, SiloApiClient client, NotificationsApi api, EventChannelClient events) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(NotificationsViewModel) ? vm
            : type == typeof(SiloApiClient) ? client : type == typeof(NotificationsApi) ? api : type == typeof(EventChannelClient) ? events : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal string Mode = "loaded";
        internal List<AppNotification>? Inbox;
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
            if (request.Method == HttpMethod.Post && path.EndsWith("/read") && Inbox != null)
            {
                var id = Uri.UnescapeDataString(path.Split('/')[^2]);
                Inbox.Single(item => item.Id == id).ReadAt = DateTimeOffset.UtcNow.ToString("O");
                return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/preferences")) await Preferences.Task.WaitAsync(ct);
            if (path.EndsWith("/notifications") && Mode == "error") return new(HttpStatusCode.ServiceUnavailable);
            if (path.EndsWith("/notifications") && Inbox != null)
            {
                var items = request.RequestUri.Query.Contains("status=unread") ? Inbox.Where(item => item.IsUnread).ToArray() : Inbox.ToArray();
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items, page = new { has_more = false } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })) };
            }
            var json = path.EndsWith("/preferences") ? "{\"enabled\":true}"
                : path.EndsWith("/unread-count") ? "{\"count\":0}"
                : Mode == "empty" || request.RequestUri.Query.Contains("status=unread") && !UnreadAvailable ? "{\"items\":[],\"read_cutoff\":\"fixture-cutoff\"}"
                : "{\"items\":[{\"id\":\"one\",\"type\":\"episode.available\",\"series_title\":\"Fixture series\",\"created_at\":\"2026-10-09T12:00:00Z\"}],\"read_cutoff\":\"fixture-cutoff\"}";
            if (Paging && path.EndsWith("/notifications")) json = json[..^1] + ",\"page\":{\"has_more\":true,\"next_cursor\":\"next\"}}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
