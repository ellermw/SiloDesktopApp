using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class NotificationSeriesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://series-native.invalid"); client.SetProfile("viewer");
        var api = new NotificationsApi(client); var vm = new NotificationsViewModel(api);
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("viewer");
        using var events = new EventChannelClient(client, auth);
        var toast = new ToastService(); var navigation = new NavigationService();
        using var owned = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(vm)
            .AddSingleton(http).AddSingleton(new NotificationSettingsViewModel(api)).AddSingleton(auth).AddSingleton(events)
            .AddSingleton(toast).AddSingleton(navigation).BuildServiceProvider();
        field.SetValue(null, new Services(owned, previous));
        var toasts = new ToastContainer(); parent.Children.Add(toasts); toast.Register(toasts, parent.DispatcherQueue);
        var frame = new Frame { Width = 600, Height = 850 }; parent.Children.Add(frame); navigation.Frame = frame;
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SERIES_CASE") == "page-timeout") wire.CapabilityTimeout = true;
            frame.Navigate(typeof(NotificationsPage)); await Task.Delay(400);
            var page = (NotificationsPage)frame.Content;
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SERIES_CASE") == "page-timeout")
            {
                Require(wire.InboxReads > 0 && !vm.IsLoading, "current-owner capability timeout must still load the existing inbox");
                wire.CapabilityTimeout = false; wire.HoldCapability = true; frame.Navigate(typeof(Page)); await Task.Delay(60); frame.Navigate(typeof(NotificationsPage));
                await Until(() => wire.CapabilityEntered); var readCount = wire.InboxReads;
                frame.Navigate(typeof(Page)); client.SetProfile("replacement"); wire.CapabilityRelease.TrySetResult(); await Task.Delay(200);
                Require(wire.InboxReads == readCount, "unmounted stale capability completion stays quiet and cannot initiate replacement inbox reads");
                Program.Log("PASS: actual capability preflight timeout falls back to usable inbox; stale unmounted completion stays quiet."); return;
            }
            Require(page.FindName("SeriesButton") is Button, "Actual notification page must expose its Series navigation control.");
            await Until(() => ((Button)page.FindName("SeriesButton")).Visibility == Visibility.Visible);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SERIES_CASE") == "cap-retry")
            {
                wire.CapabilityFailures = 1; Invoke((Button)page.FindName("SeriesButton")); var retrySeries = (NotificationSeriesControl)page.FindName("SeriesView");
                await Until(() => retrySeries.State.Error != null); var before = wire.CapabilityReads;
                Invoke((Button)retrySeries.FindName("SeriesRetry")); await Task.Delay(250);
                Require(wire.CapabilityReads > before && retrySeries.State.Available && retrySeries.State.Error == null && retrySeries.State.Active.Count > 0, "initial capability failure Retry must reinitialize capability and load the list");
                Program.Log("PASS: actual initial-capability failure Retry recovers the Series list."); return;
            }
            Invoke((Button)page.FindName("SeriesButton")); var series = (NotificationSeriesControl)page.FindName("SeriesView");
            series.Loaded += (_, _) => Program.Log("lifecycle Series loaded " + Gates());
            series.Unloaded += (_, _) => Program.Log("lifecycle Series unloaded " + Gates());
            string Gates() => $"opened={typeof(NotificationSeriesControl).GetField("_opened", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series)}, ready={typeof(NotificationSeriesControl).GetField("_ready", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series)}, current={series.State.Context is { } owner && series.State.IsCurrent(owner)}, loaded={series.IsLoaded}";
            await Until(() => series.State.Active.Count == 1 && !series.State.IsLoading);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SERIES_CASE") == "undo-timeout")
            {
                Invoke(Action(series, "Mute One Series"));
                await Until(() => wire.Muted && Descendants(toasts).OfType<Button>().Any(button => button.Content as string == "Undo"));
                wire.TimeoutWrite = true; Invoke(Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo")); await Task.Delay(250);
                Require(Descendants(toasts).OfType<TextBlock>().Any(text => text.Text == "Failed to unmute the series"), "current-owner Undo timeout must show the existing visible unmute error");
                wire.TimeoutWrite = false; await series.LoadMoreAsync(); Invoke(Action(series, "Unmute One Series")); await Until(() => !wire.Muted && series.State.Active.Any(row => row.Item.ContentId == "one"));
                Invoke(Action(series, "Mute One Series")); await Until(() => wire.Muted && Descendants(toasts).OfType<Button>().Any(button => button.Content as string == "Undo" && button.IsEnabled));
                var failures = Descendants(toasts).OfType<TextBlock>().Count(text => text.Text == "Failed to unmute the series");
                wire.HoldUnmute = true; Invoke(Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo" && button.IsEnabled)); await Until(() => wire.UnmuteEntered);
                client.SetProfile("replacement"); var reads = wire.Queries.Count; wire.UnmuteRelease.TrySetResult(); await Task.Delay(200);
                Require(Descendants(toasts).OfType<TextBlock>().Count(text => text.Text == "Failed to unmute the series") == failures && wire.Queries.Count == reads, "stale Undo timeout stays quiet without replacement list reads");
                Program.Log("PASS: actual Undo current-owner timeout reports visible unmute error; stale-context timeout stays quiet."); return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SERIES_CASE") == "scroll-retry")
            {
                frame.Height = 330; page.UpdateLayout(); await Task.Delay(100);
                var scroll = Descendants(page).OfType<ScrollViewer>().First(); wire.FailMore = true;
                Invoke((Button)series.FindName("SeriesLoadMore")); await Until(() => series.State.Error != null); var requests = wire.Continuations.Count;
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Task.Delay(150);
                scroll.ChangeView(null, Math.Max(0, scroll.ScrollableHeight - 30), null, true); await Task.Delay(150);
                Require(wire.Continuations.Count == requests, "tail scroll after a paging error must wait for explicit Retry instead of resubmitting failed GET");
                wire.FailMore = false; Invoke((Button)series.FindName("SeriesRetry")); await Until(() => series.State.Active.Count == 2 && series.State.Error == null);
                Program.Log("PASS: actual tail scroll stays quiet after page error; explicit Retry uses the original cursor and succeeds."); return;
            }
            Require(((Button)page.FindName("MarkAllButton")).Visibility == Visibility.Collapsed, "Series hides mark-all read");
            Require(((FrameworkElement)series.FindName("InactiveSection")).Visibility == Visibility.Collapsed, "inactive waits until active continuation exhausts");
            wire.FailMore = true; wire.TimeoutMore = true; Invoke((Button)series.FindName("SeriesLoadMore")); await Until(() => series.State.Error != null);
            Require(series.State.Active.Count == 1 && ((Button)series.FindName("SeriesRetry")).IsEnabled, "paging error retains existing rows with usable Retry");
            wire.FailMore = false; wire.TimeoutMore = false; Invoke((Button)series.FindName("SeriesRetry")); await Until(() => series.State.ShowInactive && series.State.Active.Count == 2);
            Require(wire.Continuations.TakeLast(2).Distinct().Count() == 1, "failed continuation retries the original cursor");
            Require(((FrameworkElement)series.FindName("InactiveSection")).Visibility == Visibility.Visible, "silenced section opens after active completes");
            Program.Log("PASS: actual Series navigation, active-first continuation, retained-row paging error and same-cursor Retry.");
            ((ComboBox)series.FindName("ReasonCombo")).SelectedIndex = 4;
            ((ComboBox)series.FindName("SortCombo")).SelectedIndex = 4;
            ((TextBox)series.FindName("SearchBox")).Text = " bear & fox ";
            await Until(() => series.State.Options == new SiloPlayer.Core.Models.Notifications.NotificationSeriesOptions("bear & fox", "next_up", "-year") && !series.State.IsLoading);
            Require(wire.Queries.Last().Contains("q=bear%20%26%20fox") && wire.Queries.Last().Contains("reason=next_up") && wire.Queries.Last().Contains("sort=-year"), "actual toolbar sends its complete options");
            navigation.NavigationRequestHandler = (type, parameter) => { Require(type == typeof(ItemDetailPage) && parameter as string == "one", "actual row links the catalog item"); frame.Navigate(typeof(Page)); return true; };
            Invoke(Link(series, "One Series")); await Task.Delay(100); frame.GoBack();
            // State's query finishes before control.OpenAsync restores its toolbar.
            // The page assigns _scroll only after awaiting that complete operation.
            await Until(() => series.IsLoaded && !series.State.IsLoading && series.State.Active.Count > 0 &&
                series.State.Context is { } owner && series.State.IsCurrent(owner) &&
                typeof(NotificationSeriesControl).GetField("_opened", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(series) is true &&
                typeof(NotificationsPage).GetField("_scroll", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page) != null);
            Require(((TextBox)series.FindName("SearchBox")).Text == "bear & fox" && ((ComboBox)series.FindName("SortCombo")).SelectedIndex == 4, "detail/back preserves query controls");
            navigation.NavigationRequestHandler = null;
            ((TextBox)series.FindName("SearchBox")).Text = ""; ((ComboBox)series.FindName("ReasonCombo")).SelectedIndex = 0; ((ComboBox)series.FindName("SortCombo")).SelectedIndex = 0;
            try { await Until(() => series.State.Options == new SiloPlayer.Core.Models.Notifications.NotificationSeriesOptions() && !series.State.IsLoading); }
            catch
            {
                Program.Log($"filter diagnostic: options={series.State.Options}, search={((TextBox)series.FindName("SearchBox")).Text}, reason={((ComboBox)series.FindName("ReasonCombo")).SelectedIndex}, sort={((ComboBox)series.FindName("SortCombo")).SelectedIndex}, loading={series.State.IsLoading}, {Gates()}, error={series.State.Error}");
                Program.Log("filter diagnostic queries=" + string.Join(";", wire.Queries.TakeLast(8))); throw;
            }
            wire.FailWrite = true; Invoke(Action(series, "Mute One Series")); await Until(() => series.State.MutationError != null);
            Require(!series.State.Active[0].Muted && Action(series, "Mute One Series").IsEnabled, "failed mute rolls back and retains retryable action");
            wire.FailWrite = false; wire.TimeoutWrite = true; Invoke(Action(series, "Mute One Series")); await Until(() => series.State.MutationError != null);
            Require(!series.State.Active[0].Muted && Action(series, "Mute One Series").IsEnabled, "current-owner HTTP timeout rolls back and shows actionable error"); wire.TimeoutWrite = false;
            wire.FailWrite = false; wire.HoldWrite = true; Invoke(Action(series, "Mute One Series"));
            await Until(() => wire.WriteEntered); Require(!Action(series, "Unmute One Series").IsEnabled, "only pending series action disables while request awaits");
            wire.FailRead = true; wire.WriteRelease.TrySetResult(); await Until(() => series.State.Error != null && Descendants(toasts).OfType<Button>().Any(button => button.Content as string == "Undo"));
            Require(series.State.MutationError == null && wire.Muted, "accepted mute retains success and Undo when refresh fails");
            var writes = wire.Writes; Invoke((Button)series.FindName("SeriesRetry")); await Task.Delay(100); Require(wire.Writes == writes, "read retry never repeats accepted mute");
            wire.FailRead = false; Invoke((Button)series.FindName("SeriesRetry")); await Until(() => !series.State.IsLoading && series.State.Error == null);
            Invoke(Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo")); await Until(() => !wire.Muted && series.State.Active.Any(row => row.Item.ContentId == "one"));
            Program.Log("PASS: actual pending row, mutation rollback, accepted204/list-failure/read-only Retry and actionable captured Undo.");
            wire.HoldWrite = false; Invoke(Action(series, "Mute One Series")); await Until(() => wire.Muted && Descendants(toasts).OfType<Button>().Any(button => button.Content as string == "Undo"));
            await Until(() => Descendants(toasts).OfType<Button>().Any(button => button.Content as string == "Undo" && button.IsEnabled));
            writes = wire.Writes; client.SetProfile("other"); Invoke(Descendants(toasts).OfType<Button>().First(button => button.Content as string == "Undo" && button.IsEnabled)); await Task.Delay(120);
            Require(wire.Writes == writes, "stale Undo cannot unmute replacement profile"); client.SetProfile("viewer");
            await series.OpenAsync();
            await series.LoadMoreAsync();
            await Until(() => Descendants(series).OfType<Image>().Any(image => image.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap && bitmap.PixelWidth > 0));
            foreach (var width in new[] { 400, 600, 1080, 1920, 3440, 3840 })
            {
                var height = width == 3440 ? 1440 : width == 3840 ? 2160 : 1080;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height)); frame.Width = width; frame.Height = height;
                page.UpdateLayout(); await Task.Delay(100);
                var longTitle = Descendants(series).OfType<TextBlock>().First(text => text.Text.StartsWith("Second Series"));
                Require(longTitle.IsTextTrimmed, "long title visibly ellipsizes before its action");
                Program.Log($"layout width={width} root={parent.XamlRoot.Size.Width} frame={frame.ActualWidth} page={page.ActualWidth} content={series.ActualWidth}");
                await MediaParityNativeFixture.CaptureAsync(frame, "notification-series-" + width + ".png");
                foreach (var button in Descendants(series).OfType<Button>().Where(button => AutomationProperties.GetName(button).StartsWith("Mute ") || AutomationProperties.GetName(button).StartsWith("Unmute ")))
                {
                    var point = button.TransformToVisual(series).TransformPoint(new());
                    Require(point.X >= 0 && point.X + button.ActualWidth <= series.ActualWidth + 1, "series action stays inside narrow/wide content bounds");
                }
                foreach (var chips in Descendants(series).OfType<WrapPanel>())
                    foreach (var chip in chips.Children.OfType<FrameworkElement>())
                    {
                        var point = chip.TransformToVisual(chips).TransformPoint(new());
                        Require(point.X >= 0 && point.X + chip.ActualWidth <= chips.ActualWidth + 1 && point.Y + chip.ActualHeight <= chips.ActualHeight + 1, "all four relationship pills wrap inside their actual allocated row");
                    }
            }
            var search = (TextBox)series.FindName("SearchBox"); search.Text = "none";
            await Until(() => series.State.Options.Q == "none" && series.State.IsEmpty);
            Require(((TextBlock)series.FindName("SeriesEmptyTitle")).Text.StartsWith("No series match"), "search has a distinct filtered-empty state");
            frame.Navigate(typeof(Page)); await Task.Delay(150);
            // The real Settings host supplies this preexisting component resource.
            Application.Current.Resources["SettingsGroupStyle"] = new Style { TargetType = typeof(Border) };
            var settings = new NotificationSettingsControl(); parent.Children.Add(settings); await Until(() => ((Button)settings.FindName("NotificationSeriesLink")).Visibility == Visibility.Visible);
            Invoke((Button)settings.FindName("NotificationSeriesLink")); await Until(() => frame.Content is NotificationsPage newPage && ((FrameworkElement)newPage.FindName("SeriesView")).Visibility == Visibility.Visible);
            parent.Children.Remove(settings); await Task.Delay(100);
            wire.Supported = false; frame.Navigate(typeof(Page)); await Task.Delay(100); frame.Navigate(typeof(NotificationsPage), "series");
            await Task.Delay(200); Require(((Button)((NotificationsPage)frame.Content).FindName("SeriesButton")).Visibility == Visibility.Collapsed, "older server omits Series action and falls back to inbox");
            Program.Log("PASS: detail/back option retention, stale Undo refusal, Settings Series routing, old capability omission, filtered empty, real synthetic PNG decoding and400/600/1080/1920/3440/3840 native row/chip/action captures.");
        }
        finally
        {
            vm.CancelPendingLoad(); frame.Navigate(typeof(Page)); parent.Children.Remove(frame); parent.Children.Remove(toasts);
            wire.CapabilityRelease.TrySetResult(); wire.UnmuteRelease.TrySetResult(); wire.WriteRelease.TrySetResult();
            await Task.Delay(200); toast.Unregister(); navigation.Frame = null; field.SetValue(null, previous);
        }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static Button Action(NotificationSeriesControl root, string name) => Descendants(root).OfType<Button>().First(button => AutomationProperties.GetName(button) == name);
    private static Button Link(NotificationSeriesControl root, string title) => Descendants(root).OfType<Button>().First(button => AutomationProperties.GetName(button).StartsWith(title + ","));
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var attempt = 0; attempt < 200; attempt++) { if (ready()) return; await Task.Delay(20); } throw new InvalidOperationException("Notification Series native state timed out."); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private sealed class Services(IServiceProvider owned, IServiceProvider previous) : IServiceProvider
    { public object? GetService(Type type) => owned.GetService(type) ?? previous.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool Muted, FailWrite, FailRead, FailMore, HoldWrite, WriteEntered, TimeoutMore, TimeoutWrite, Supported = true;
        internal int Writes, InboxReads;
        internal bool CapabilityTimeout;
        internal bool HoldCapability, CapabilityEntered, HoldUnmute, UnmuteEntered;
        internal TaskCompletionSource CapabilityRelease = new(TaskCreationOptions.RunContinuationsAsynchronously), UnmuteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CapabilityFailures, CapabilityReads;
        internal List<string> Queries = [], Continuations = [];
        internal TaskCompletionSource WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("/notifications")) InboxReads++;
            if (path.EndsWith("capabilities") && HoldCapability) { CapabilityEntered = true; await CapabilityRelease.Task; }
            if (path.EndsWith("capabilities") && CapabilityTimeout) throw new TaskCanceledException("Current-owner fixture capability timeout.");
            if (path == "/poster.png") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(Program.AppDirectory, "Assets", "silo-icon-1024.png"), ct)) };
            if (path.EndsWith("capabilities")) { CapabilityReads++; if (CapabilityFailures-- > 0) return new(HttpStatusCode.BadRequest); }
            if (path.EndsWith("/mute"))
            {
                if (r.Method == HttpMethod.Delete && HoldUnmute) { UnmuteEntered = true; await UnmuteRelease.Task; throw new TaskCanceledException("Stale-owner fixture Undo timeout."); }
                if (TimeoutWrite) throw new TaskCanceledException("Current-owner fixture mute timeout.");
                Writes++; if (FailWrite) return new(HttpStatusCode.BadRequest);
                if (HoldWrite && r.Method == HttpMethod.Put) { WriteEntered = true; await WriteRelease.Task.WaitAsync(ct); }
                Muted = r.Method == HttpMethod.Put; return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/series"))
            {
                Queries.Add(r.RequestUri.Query);
                if (FailRead) return new(HttpStatusCode.BadRequest);
                var more = r.RequestUri.Query.Contains("cursor="); if (more) { Continuations.Add(r.RequestUri.Query); if (TimeoutMore) throw new TaskCanceledException("Current-owner fixture continuation timeout."); if (FailMore) return new(HttpStatusCode.BadRequest); }
                var active = r.RequestUri.Query.Contains("status=active"); var none = r.RequestUri.Query.Contains("q=none");
                var cards = new List<object>();
                object Row(string id, string title, bool muted, bool next = false) => new {
                    item = new { content_id = id, type = "series", title, poster_url = "https://series-native.invalid/poster.png" },
                    reasons = new { favorite = !next, watchlist = !next, continue_watching = !next, next_up = true },
                    active_reasons = new { favorite = !next, watchlist = !next, continue_watching = !next, next_up = true }, notifies = !muted, muted,
                    next_up_after = new { season_number = 2, episode_number = 8 } };
                if (!none && active && !Muted && !more) cards.Add(Row("one", "One Series", false));
                if (!none && active && more) cards.Add(Row("two", "Second Series with a very long title that must visibly truncate before its action at every supported size", false, true));
                if (!none && !active) { cards.Add(new { item = new { content_id = "old", type = "series", title = "Muted with no remaining reasons" }, reasons = new { }, active_reasons = new { }, notifies = false, muted = true }); if (Muted) cards.Add(Row("one", "One Series", true)); }
                var hasMore = !none && active && !more;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = cards, page = new { has_more = hasMore, next_cursor = hasMore ? "next-active" : null } })) };
            }
            var body = path.EndsWith("capabilities") ? Supported ? """{"series":{"available":true,"mute":true}}""" : "{}"
                : path.EndsWith("preferences") ? """{"enabled":true,"notify_favorites":true,"notify_next_up":true}"""
                : path.EndsWith("unread-count") ? "{\"count\":0}" : """{"items":[],"page":{"has_more":false},"read_cutoff":"fixture-cutoff"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
