using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class PersonalListsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://personal-lists.invalid");
        var outer = SiloPlayer.App.Services; var catalog = new CatalogApi(client);
        var requests = new RequestsApi(client); var watchlist = new WatchlistViewModel(catalog, requests);
        var notifications = new NotificationsViewModel(new NotificationsApi(client));
        var navigation = new NavigationService();
        using var services = new ServiceCollection()
            .AddSingleton(catalog).AddSingleton(requests).AddSingleton(new CollectionsApi(client))
            .AddSingleton(watchlist).AddSingleton(notifications).AddSingleton(navigation)
            .AddSingleton(new UICustomizationService(new SettingsApi(client)))
            .AddSingleton(new CardOverlayService(new SettingsApi(client)))
            .AddSingleton(outer.GetRequiredService<AuthService>())
            .AddSingleton(outer.GetRequiredService<SettingsService>())
            .AddSingleton(outer.GetRequiredService<ImageService>()).BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, services);
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> trace = (_, e) =>
        { if (e.Exception is System.Runtime.InteropServices.COMException) Program.Log("TRACE personal COM " + e.Exception); };
        AppDomain.CurrentDomain.FirstChanceException += trace;
        try
        {
            await AssertWatchlistOverlayPreferencesAsync(parent, services.GetRequiredService<CardOverlayService>());
            foreach (var width in new[] { 1280d, 900d, 500d })
            {
                var frame = new Frame { Width = width, Height = 800 };
                var viewport = new Border { Width = width, Height = 800, Child = frame };
                parent.Children.Add(viewport); navigation.Frame = frame;
                try
                {
                    navigation.NavigateImmediately(typeof(CatalogPage), new CatalogNavigation("watchlist", "Watchlist"));
                    var page = (CatalogPage)frame.Content;
                    await UntilAsync(() => page.IsLoaded && page.ActualWidth > 0);
                    AssertActualBackdrop(page);
                    await UntilAsync(() => ((FrameworkElement)page.FindName("WatchlistTabs")).Visibility == Visibility.Visible && watchlist.ExternalTitles.Count == 2);
                    Invoke((Button)page.FindName("ExternalWatchlistTab"));
                    await UntilAsync(() => ((FrameworkElement)page.FindName("ExternalWatchlistScroller")).Visibility == Visibility.Visible &&
                        !((ProgressRing)page.FindName("ExternalTitlesLoading")).IsActive);
                    await Task.Delay(60);
                    page.UpdateLayout();
                    var layout = (UniformGridLayout)page.FindName("ExternalTitlesLayout");
                    var ui = services.GetRequiredService<UICustomizationService>();
                    var expectedColumns = width >= 1280 ? 6 : width >= 1024 ? 5 : width >= 768 ? 4 : width >= 640 ? 3 : 2;
                    if (layout.MaximumRowsOrColumns != expectedColumns) throw new InvalidOperationException("External watchlist does not use the WebUI's dedicated large-card grid.");
                    var repeater = (ItemsRepeater)page.FindName("ExternalTitlesRepeater");
                    var first = (FrameworkElement)repeater.TryGetElement(0);
                    var second = (FrameworkElement)repeater.TryGetElement(1);
                    var firstPoint = first.TransformToVisual(repeater).TransformPoint(new());
                    var secondPoint = second.TransformToVisual(repeater).TransformPoint(new());
                    Program.Log($"Watchlist actual grid={repeater.ActualWidth}, min={layout.MinItemWidth}, host={first.ActualWidth}, first={firstPoint.X},{firstPoint.Y}, second={secondPoint.X},{secondPoint.Y}.");
                    if (Math.Abs(firstPoint.Y - secondPoint.Y) > 1 || secondPoint.X <= firstPoint.X)
                        throw new InvalidOperationException("The actual external watchlist collapses its intended columns into separate rows.");
                    // Page/Frame RTB omits unpainted padding; capture the exact
                    // client bounds with the same live page background brush.
                    viewport.Background = page.Background;
                    await MediaParityNativeFixture.CaptureAsync(viewport, $"watchlist-external-{width:0}.png");
                    foreach (var name in new[] { "HeaderGrid", "WatchlistTabs", "ExternalWatchlistHintGrid", "ExternalTitlesRepeater" })
                        if (page.FindName(name) is FrameworkElement measured)
                            Program.Log($"TRACE watchlist {width:0} {name}: y={measured.TransformToVisual(page).TransformPoint(new()).Y}, height={measured.ActualHeight}");
                    wire.FailTitles = true;
                    await (Task)typeof(CatalogPage).GetMethod("LoadWatchlistTitlesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                    if (((FrameworkElement)page.FindName("ExternalTitlesRetry")).Visibility != Visibility.Visible || watchlist.ExternalTitles.Count != 2)
                        throw new InvalidOperationException("External watchlist failure loses retained titles or hides retry.");
                    wire.FailTitles = false;
                    await (Task)typeof(CatalogPage).GetMethod("LoadWatchlistTitlesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                    Program.Log($"TRACE personal retry width={width}, count={watchlist.ExternalTitles.Count}, error={watchlist.ExternalTitlesError}, active={((ProgressRing)page.FindName("ExternalTitlesLoading")).IsActive}, state={((FrameworkElement)page.FindName("ExternalTitlesState")).Visibility}, attached={typeof(CatalogPage).GetField("_watchlistAttached", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)}");
                    await UntilAsync(() => ((FrameworkElement)page.FindName("ExternalTitlesState")).Visibility == Visibility.Collapsed);
                }
                finally { frame.Navigate(typeof(Page)); parent.Children.Remove(viewport); navigation.Frame = null; }

                var inboxFrame = new Frame { Width = width, Height = 800 };
                var inboxViewport = new Border { Width = width, Height = 800, Child = inboxFrame };
                parent.Children.Add(inboxViewport);
                navigation.Frame = inboxFrame; navigation.NavigateImmediately(typeof(NotificationsPage));
                var inbox = (NotificationsPage)inboxFrame.Content;
                try
                {
                    await notifications.ReloadAsync();
                    await UntilAsync(() => inbox.IsLoaded && inbox.ActualWidth > 0);
                    await UntilAsync(() => notifications.Notifications.Count == 2 && notifications.HasLoadedPreferences && !notifications.IsLoading);
                    inbox.UpdateLayout();
                    AssertActualBackdrop(inbox);
                    var content = (Grid)inbox.FindName("PageContent");
                    Program.Log($"Notification viewport={inbox.ActualWidth}, content={content.ActualWidth}, header={((Grid)inbox.FindName("HeaderGrid")).ActualWidth}.");
                    if (Math.Abs(content.ActualWidth - Math.Min(width, 768)) > 1) throw new InvalidOperationException("Notifications content shrinks instead of filling the centered current WebUI container.");
                    inboxViewport.Background = inbox.Background;
                    // Capture settled item entrance transitions rather than their faded,
                    // translated intermediate frames.
                    await Task.Delay(500);
                    await MediaParityNativeFixture.CaptureAsync(inboxViewport, $"notifications-{width:0}.png");
                    var list = (ListView)inbox.FindName("NotificationsList");
                    var firstRow = (ListViewItem)list.ContainerFromItem(notifications.Notifications[0]);
                    var rowRoot = Descendants<Grid>(firstRow).First(g => g.Name == "NotificationRowRoot");
                    var titleElement = Descendants<TextBlock>(rowRoot).First(t => t.Name == "NotificationTitleText");
                    Program.Log($"TRACE inbox geometry listY={list.TransformToVisual(inbox).TransformPoint(new()).Y}, rowY={rowRoot.TransformToVisual(inbox).TransformPoint(new()).Y}, titleY={titleElement.TransformToVisual(inbox).TransformPoint(new()).Y}, titleH={titleElement.ActualHeight}, rowH={rowRoot.ActualHeight}");
                    var visited = new List<(Type Page, object? Parameter)>();
                    navigation.NavigationRequestHandler = (type, parameter) => { visited.Add((type, parameter)); return true; };
                    var item = (ListViewItem)list.ContainerFromItem(notifications.Notifications[1]);
                    if (item == null) throw new InvalidOperationException("Notification row was not realized.");
                    var peer = new ListViewItemAutomationPeer(item);
                    if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException("Notification row does not expose its native invocation.");
                    invoke.Invoke(); await UntilAsync(() => visited.Count > 0);
                    if (visited[^1] is not { Page: var route, Parameter: RequestDetailNavigation { TmdbId: 42, MediaType: "series" } } || route != typeof(RequestDetailPage))
                        throw new InvalidOperationException("Request notification row opens the wrong destination.");
                    navigation.NavigationRequestHandler = null;
                }
                finally { notifications.CancelPendingLoad(); inboxFrame.Navigate(typeof(Page)); parent.Children.Remove(inboxViewport); navigation.Frame = null; }
            }
            Program.Log("PASS: personal list native density, retained-error retry, inbox rows and actual external notification invocation.");
            Program.Log("PERSONAL_LISTS_ACCEPTANCE_COMPLETED");
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= trace; field.SetValue(null, outer); }
    }

    private static async Task AssertWatchlistOverlayPreferencesAsync(StackPanel parent, CardOverlayService service)
    {
        var field = typeof(CardOverlayService).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = field.GetValue(service);
        try
        {
            foreach (var preset in new[] { "classic", "minimal", "vibrant", "pill", "square" })
            {
                var config = new OverlayItemConfig(true, OverlayPosition.BottomRight, "#3b82f6", false);
                field.SetValue(service, new CardOverlayPrefs(2, preset, new[] { "request_status" }, new() { ["request_status"] = config }));
                var title = new SiloPlayer.Core.Models.Requests.WatchlistTitle { Title = "Download fixture", Request = new() { Download = new() { Phase = "downloading", Percent = 37 } } };
                var card = SiloPlayer.Controls.ExternalTitleCard.Build(title, 226, statusBadge: "Downloading 37%", watchlistCard: true);
                parent.Children.Add(card); await Task.Delay(50); card.UpdateLayout();
                var badges = Descendants<Border>(card).Where(b => Descendants<TextBlock>(b).Any(t => t.Text == "DOWNLOADING 37%")).ToArray();
                if (!badges.Any(b => b.HorizontalAlignment == HorizontalAlignment.Right && b.VerticalAlignment == VerticalAlignment.Bottom))
                    throw new InvalidOperationException($"External watchlist ignores request badge corner/preset: {preset}.");
                if (!Descendants<ProgressBar>(card).Any(b => b.Value == 37))
                    throw new InvalidOperationException("External watchlist omits the enabled request download bar.");
                parent.Children.Remove(card);
            }
            Program.Log("PASS: external watchlist five overlay presets, configured corner/accent and download progress.");
        }
        finally { field.SetValue(service, original); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void AssertActualBackdrop(Page page)
    {
        var resource = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"];
        Program.Log($"TRACE actualderived {page.GetType().Name} enabled={PageBackdrop.GetEnabled(page)}, background={page.Background?.GetType().Name}, standardIdentity={ReferenceEquals(page.Background, resource)}, size={page.ActualWidth}x{page.ActualHeight}");
        if (!PageBackdrop.GetEnabled(page) || page.Background is not Microsoft.UI.Xaml.Media.RadialGradientBrush)
            throw new InvalidOperationException("Actual navigated derived Page does not receive the shared body backdrop: " + page.GetType().Name);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task UntilAsync(Func<bool> ready)
    { for (var i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException("Personal list native fixture did not settle."); }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool FailTitles;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Program.Log($"Personal fixture {request.Method} {path}");
            if (request.RequestUri.Host != "personal-lists.invalid") throw new InvalidOperationException("Unexpected remote personal request.");
            if (path.EndsWith("/watchlist/titles") && FailTitles) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json = path switch
            {
                "/api/v2/requests/status" => "{\"requests_enabled\":true,\"watchlist_titles_supported\":true}",
                "/api/v2/watchlist/titles" => "{\"items\":[{\"media_type\":\"series\",\"tmdb_id\":42,\"title\":\"Fixture series\",\"year\":2026,\"request\":{\"requestable\":true}},{\"media_type\":\"movie\",\"tmdb_id\":43,\"title\":\"Needs attention\",\"status\":\"needs_review\",\"request\":{\"requestable\":false}}]}",
                "/api/v2/notifications" => "{\"items\":[{\"id\":\"n1\",\"type\":\"episode.available\",\"series_id\":\"series-fixture\",\"episode_id\":\"episode-fixture\",\"series_title\":\"Fixture series\",\"episode_title\":\"New episode\",\"season_number\":2,\"episode_number\":1,\"created_at\":\"2026-10-01T12:00:00Z\"},{\"id\":\"n2\",\"type\":\"request.declined\",\"reason_flags\":{\"title\":\"Request series\",\"media_type\":\"series\",\"tmdb_id\":42},\"created_at\":\"2026-10-01T12:00:00Z\"}],\"read_cutoff\":\"fixture-cutoff\"}",
                "/api/v2/notifications/unread-count" => "{\"count\":2}",
                "/api/v2/notifications/preferences" => "{\"enabled\":true,\"notify_favorites\":true,\"notify_watchlist\":true}",
                _ when path.EndsWith("/read") => "{}",
                _ when path.EndsWith("/filters") => "{}",
                _ when request.Method == HttpMethod.Get => "{\"items\":[]}",
                _ => throw new InvalidOperationException("Unexpected personal mutation.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
