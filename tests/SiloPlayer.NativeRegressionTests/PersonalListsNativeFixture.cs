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
            .AddSingleton(client).AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new ToastService())
            .AddSingleton(new ItemDetailPrefetchCache(catalog, outer.GetRequiredService<AuthService>()))
            .AddSingleton(new PlayerService(new PlaybackApi(client), catalog, outer.GetRequiredService<AuthService>(), client,
                outer.GetRequiredService<SettingsService>(), new SettingsApi(client)))
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
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_CASE") is "notification-states" or "notification-realtime" or "notification-pages")
            {
                await NotificationStatesNativeFixture.RunAsync(parent);
                return;
            }
            await AssertWatchlistOverlayPreferencesAsync(parent, services.GetRequiredService<CardOverlayService>());
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_PERSONAL_PHYSICAL") == "1")
            {
                await PhysicalPersonalPagesAsync(navigation, wire);
                return;
            }
            await AssertCatalogStatesAsync(parent, navigation, wire);
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
                    var catalogTop = ((Grid)page.FindName("HeaderGrid")).TransformToVisual(page).TransformPoint(new()).Y;
                    var expectedCatalogTop = (width >= 1024 ? 32 : 16) + (width >= 640 ? 24 : 16);
                    if (Math.Abs(catalogTop - expectedCatalogTop) > 1) throw new InvalidOperationException($"Catalog omits shared WebUI shell gutter: {catalogTop}, expected {expectedCatalogTop}.");
                    var expectedTitleFont = Math.Clamp(width * .05, 32, 56);
                    if (Math.Abs(((TextBlock)page.FindName("PageTitleText")).FontSize - expectedTitleFont) > .01)
                        throw new InvalidOperationException($"Catalog title must follow viewport clamp(32px,5vw,56px) at {width}px.");
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
                    foreach (var caption in Descendants<TextBlock>(first).Where(t => t.Text == "Fixture series" || t.Text == "SERIES · 2026" || t.Inlines.Count > 0))
                        Program.Log($"Watchlist caption '{caption.Text}': y={caption.TransformToVisual(first).TransformPoint(new()).Y:R}, height={caption.ActualHeight:R}, font={caption.FontSize:R}, line={caption.LineHeight:R}, margin={caption.Margin}.");
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
                    wire.EmptyTitles = true;
                    await (Task)typeof(CatalogPage).GetMethod("LoadWatchlistTitlesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                    page.UpdateLayout();
                    if (((FrameworkElement)page.FindName("ExternalWatchlistHintGrid")).Visibility != Visibility.Collapsed
                        || ((FrameworkElement)page.FindName("ExternalTitlesState")).Visibility != Visibility.Visible
                        || ((TextBlock)page.FindName("ExternalTitlesStateText")).Text != "Nothing waiting for the library"
                        || ((TextBlock)page.FindName("ExternalTitlesStateText")).FontSize != 16
                        || ((FrameworkElement)page.FindName("ExternalTitlesStateOutline")).Visibility != Visibility.Visible)
                        throw new InvalidOperationException("Empty external Watchlist retains the loaded hint or lacks its own bordered title/caption state.");
                    wire.EmptyTitles = false;
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
                    var expectedTop = width >= 1024 ? 64d : 48d;
                    var headerTop = ((Grid)inbox.FindName("HeaderGrid")).TransformToVisual(inbox).TransformPoint(new()).Y;
                    if (Math.Abs(headerTop - expectedTop) > 1)
                        throw new InvalidOperationException($"Notifications header omits shared WebUI page padding: {headerTop}, expected {expectedTop}.");
                    Program.Log($"Notification viewport={inbox.ActualWidth}, content={content.ActualWidth}, header={((Grid)inbox.FindName("HeaderGrid")).ActualWidth}.");
                    if (Math.Abs(content.ActualWidth - Math.Min(width, 768)) > 1) throw new InvalidOperationException("Notifications content shrinks instead of filling the centered current WebUI container.");
                    inboxViewport.Background = inbox.Background;
                    // Capture settled item entrance transitions rather than their faded,
                    // translated intermediate frames.
                    await Task.Delay(500);
                    await MediaParityNativeFixture.CaptureAsync(inboxViewport, $"notifications-{width:0}.png");
                    var preferences = (Microsoft.UI.Xaml.Controls.Flyout)((Button)inbox.FindName("PreferencesButton")).Flyout;
                    preferences.ShowAt((Button)inbox.FindName("PreferencesButton")); await Task.Delay(100);
                    var preferencePanel = (FrameworkElement)inbox.FindName("PreferenceControlsPanel"); preferencePanel.UpdateLayout();
                    await MediaParityNativeFixture.CaptureAsync(preferencePanel, $"notification-preferences-{width:0}.png");
                    var preferenceLabels = Descendants<TextBlock>(preferencePanel).Where(t => t.Text is "Notifications" or "Favorites" or "Watchlist" or "Continue Watching" or "Next Up").ToArray();
                    var labelMeasurements = string.Join(";", preferenceLabels.Select(t => $"{t.Text}:{t.FontSize:R}/{t.FontWeight.Weight}/{t.ActualHeight:R}"));
                    Program.Log($"Inbox preference panel {width}: {preferencePanel.ActualWidth:R}x{preferencePanel.ActualHeight:R}, labels={labelMeasurements}.");
                    if (Math.Abs(preferencePanel.ActualHeight - 251) > 1 || preferenceLabels.Length != 5 || preferenceLabels.Any(t => t.FontSize != 14 || t.FontWeight.Weight != 500 || Math.Abs(t.ActualHeight - 20) > .6))
                        throw new InvalidOperationException("Notification preference label typography and row heights differ from current WebUI.");
                    preferences.Hide(); await Task.Delay(30);
                    var list = (ListView)inbox.FindName("NotificationsList");
                    var firstRow = (ListViewItem)list.ContainerFromItem(notifications.Notifications[0]);
                    var rowRoot = Descendants<Grid>(firstRow).First(g => g.Name == "NotificationRowRoot");
                    var titleElement = Descendants<TextBlock>(rowRoot).First(t => t.Name == "NotificationTitleText");
                    var savedContext = rowRoot.DataContext;
                    rowRoot.DataContext = new SiloPlayer.Core.Models.Notifications.AppNotification {
                        Type = "episode.available", SeriesTitle = "Recycled read fixture", ReadAt = "2026-10-09T12:00:00Z"
                    };
                    if (!Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(rowRoot).Contains("Recycled read fixture")
                        || titleElement.FontWeight != Microsoft.UI.Text.FontWeights.Medium
                        || rowRoot.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush recycledBrush || recycledBrush.Color.A != 0)
                        throw new InvalidOperationException("Recycled notification retains its previous accessible label or unread style.");
                    rowRoot.DataContext = savedContext;
                    var reasons = (ItemsControl)rowRoot.FindName("NotificationReasons");
                    if (reasons.Margin.Top != 4 || Math.Abs(rowRoot.ActualHeight - 91) > 1)
                        throw new InvalidOperationException($"Notification reason badges miss their 6px subtitle gap/91px row: {reasons.Margin.Top}/{rowRoot.ActualHeight}.");
                    Program.Log($"TRACE inbox geometry listY={list.TransformToVisual(inbox).TransformPoint(new()).Y}, rowY={rowRoot.TransformToVisual(inbox).TransformPoint(new()).Y}, titleY={titleElement.TransformToVisual(inbox).TransformPoint(new()).Y}, titleH={titleElement.ActualHeight}, rowH={rowRoot.ActualHeight}");
                    var timestamp = Descendants<TextBlock>(rowRoot).Single(t => t.Text == notifications.Notifications[0].RelativeTime);
                    var timestampRight = timestamp.TransformToVisual(rowRoot).TransformPoint(new()).X + timestamp.ActualWidth;
                    var inlineRead = (Button)rowRoot.FindName("InlineMarkReadButton");
                    var readOrigin = inlineRead.TransformToVisual(rowRoot).TransformPoint(new());
                    var artwork = Descendants<Border>(rowRoot).Single(b => b.Width == 44 && b.Height == 64);
                    // WinUI rounds text origins to device pixels. Allow one
                    // physical pixel, including floating-point representation.
                    var pixelTolerance = 1 / inbox.XamlRoot.RasterizationScale + 0.000001;
                    Program.Log($"Inbox visual {width}: timestampRight={timestampRight:R}/{rowRoot.ActualWidth - 12:R}, inline={readOrigin.X:R},{readOrigin.Y:R} {inlineRead.ActualWidth:R}x{inlineRead.ActualHeight:R}, rowRadius={rowRoot.CornerRadius.TopLeft:R}, posterRadius={artwork.CornerRadius.TopLeft:R}.");
                    if (Math.Abs(timestampRight - (rowRoot.ActualWidth - 12)) > pixelTolerance || rowRoot.CornerRadius.TopLeft != 16 || artwork.CornerRadius.TopLeft != 10
                        || Math.Abs(inlineRead.ActualHeight - 28) > .6 || Math.Abs(inlineRead.ActualWidth - 28) > .6
                        || Math.Abs(readOrigin.X - (rowRoot.ActualWidth - 36)) > .6 || Math.Abs(readOrigin.Y - (rowRoot.ActualHeight - 36)) > .6)
                        throw new InvalidOperationException("Notification timestamp gutter/corners/28px inline action differ from current rendered WebUI.");
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
        var initialized = typeof(CardOverlayService).GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var wasInitialized = initialized.GetValue(service);
        initialized.SetValue(service, true);
        var geometryFailures = new List<string>();
        try
        {
            foreach (var preset in new[] { "classic", "minimal", "vibrant", "pill", "square" })
            {
                var config = new OverlayItemConfig(true, OverlayPosition.BottomRight, "#3b82f6", false);
                field.SetValue(service, new CardOverlayPrefs(2, preset, new[] { "request_status", "year" }, new()
                { ["request_status"] = config, ["year"] = new(true, OverlayPosition.BottomRight, ShowIcon: false) }));
                var title = new SiloPlayer.Core.Models.Requests.WatchlistTitle { Title = "Download fixture", Year = 2026, Request = new() { Download = new() { Phase = "downloading", Percent = 37 } } };
                var card = SiloPlayer.Controls.ExternalTitleCard.Build(title, 185, statusBadge: "Downloading 37%", watchlistCard: true);
                parent.Children.Add(card); await Task.Delay(50); card.UpdateLayout();
                var badges = Descendants<Border>(card).Where(b => Descendants<TextBlock>(b).Any(t => t.Text == "DOWNLOADING 37%")).ToArray();
                if (!badges.Any(b => b.HorizontalAlignment == HorizontalAlignment.Right && b.VerticalAlignment == VerticalAlignment.Bottom))
                    throw new InvalidOperationException($"External watchlist ignores request badge corner/preset: {preset}.");
                if (!Descendants<ProgressBar>(card).Any(b => b.Value == 37))
                    throw new InvalidOperationException("External watchlist omits the enabled request download bar.");
                var badge = badges.Single(b => b.Child is StackPanel);
                var text = Descendants<TextBlock>(badge).Single(t => t.Text == "DOWNLOADING 37%");
                var stack = Descendants<StackPanel>(card).Single(panel => panel.Children.Contains(badge));
                var scale = 183d / 185;
                var expectedHeight = (preset switch { "minimal" => 9, "vibrant" => 14, "pill" => 20, "square" => 13, _ => 16 }) * scale;
                var expectedPadding = (preset switch { "minimal" => 4, "pill" => 10, "square" => 6, _ => 8 }) * scale;
                var expectedGap = (preset is "minimal" or "square" ? 2 : 4) * scale;
                var expectedTracking = preset is "minimal" or "square" ? 100 : 25;
                Program.Log($"Overlay actual {preset}: height={badge.ActualHeight:R}, font={text.FontSize:R}, padding={badge.Padding}, radius={badge.CornerRadius.TopLeft:R}, stack={stack.Spacing:R}, tracking={text.CharacterSpacing}, weight={text.FontWeight.Weight}.");
                if (Math.Abs(badge.ActualHeight - expectedHeight) > 1 || Math.Abs(badge.Padding.Left - expectedPadding) > .1
                    || Math.Abs(text.FontSize - (preset is "minimal" or "square" ? 9 : 10) * scale) > .05
                    || Math.Abs(stack.Spacing - expectedGap) > .1 || text.CharacterSpacing != expectedTracking
                    || text.FontWeight.Weight != (preset is "vibrant" or "square" ? 700 : 600))
                    geometryFailures.Add($"Rendered {preset} badge differs from measured public WebUI geometry.");
                if (preset is "minimal" or "square" or "vibrant"
                    && Math.Abs(badge.CornerRadius.TopLeft - (preset == "vibrant" ? 10 : 8) * scale) > .1)
                    geometryFailures.Add($"Rendered {preset} corner radius differs from measured public WebUI.");
                if (preset is "classic" or "pill")
                {
                    var color = ((Microsoft.UI.Xaml.Media.SolidColorBrush)badge.Background).Color;
                    var expected = preset == "classic" ? new[] { 182, 23, 51, 97 } : new[] { 194, 30, 49, 87 };
                    if (new[] { (int)color.A, color.R, color.G, color.B }.Zip(expected).Any(pair => Math.Abs(pair.First - pair.Second) > 1))
                        geometryFailures.Add($"Rendered {preset} accent does not match measured premultiplied WebUI color-mix.");
                }
                var bottomInset = stack.Margin.Bottom - stack.Margin.Top;
                if (Math.Abs(bottomInset - 8) > .1)
                    geometryFailures.Add($"External progress bar requires the WebUI's 8px bottom badge clearance: actual {bottomInset}.");
                parent.Children.Remove(card);
                foreach (var virtualized in new[] { false, true })
                {
                    var item = new SiloPlayer.Core.Models.Home.MediaItem { ContentId = "corner-fixture", Type = "movie", Title = "Corner fixture", Year = 2026, UserState = new() };
                    FrameworkElement mounted;
                    StackPanel corner;
                    if (virtualized)
                    {
                        var library = new SiloPlayer.Controls.LibraryGridCard();
                        library.SetLayout(185, 277.5, 330); library.Bind(item, null);
                        mounted = library;
                        corner = (StackPanel)typeof(SiloPlayer.Controls.LibraryGridCard).GetField("_overlayBottomRight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(library)!;
                    }
                    else
                    {
                        var poster = new SiloPlayer.Controls.PosterCard { SuppressImageLoading = true };
                        poster.SetCatalogGridLayout(185); poster.MediaItem = item;
                        mounted = poster; corner = (StackPanel)poster.FindName("OverlayBottomRight");
                    }
                    parent.Children.Add(mounted); await Task.Delay(30); mounted.UpdateLayout();
                    var year = Descendants<TextBlock>(corner).Single(t => t.Text == "2026");
                    var inset = 8 * scale + (virtualized ? 0 : 1);
                    Program.Log($"Poster corner {preset}/{virtualized}: margin={corner.Margin}, gap={corner.Spacing:R}, font={year.FontSize:R}.");
                    if (Math.Abs(corner.Margin.Bottom - inset) > .1 || Math.Abs(corner.Margin.Top - inset) > .1
                        || Math.Abs(corner.Spacing - expectedGap) > .1 || Math.Abs(year.FontSize - (preset is "minimal" or "square" ? 9 : 10) * scale) > .05)
                        geometryFailures.Add($"{(virtualized ? "Library" : "Poster")} {preset} corners reserve empty space instead of matching the WebUI's inner border/corner inset.");
                    parent.Children.Remove(mounted);
                    await Task.Delay(30); // Complete native unload before restoring fixture services.
                }
            }
            if (geometryFailures.Count > 0) throw new InvalidOperationException(string.Join(" ", geometryFailures));
            Program.Log("PASS: external watchlist five overlay presets, configured corner/accent and download progress; both native poster paths match inner-border corner placement.");
        }
        finally { field.SetValue(service, original); initialized.SetValue(service, wasInitialized); }
    }

    private static async Task AssertCatalogStatesAsync(StackPanel parent, NavigationService navigation, Wire wire)
    {
        var failures = new List<string>();
        foreach (var width in new[] { 1280d, 900d, 500d })
        foreach (var source in new[] { "favorites", "watchlist" })
        {
            var frame = new Frame { Width = width, Height = 800 };
            var viewport = new Border { Width = width, Height = 800, Child = frame };
            parent.Children.Add(viewport); navigation.Frame = frame;
            try
            {
                wire.CatalogState = "loaded";
                navigation.NavigateImmediately(typeof(CatalogPage), new CatalogNavigation(source,
                    source == "favorites" ? "Favorites" : "Watchlist",
                    source == "favorites" ? "Movies and shows you've marked as favorites." : "Things you've saved to watch later."));
                var page = (CatalogPage)frame.Content;
                var repeater = (ItemsRepeater)page.FindName("ItemsRepeater");
                await UntilAsync(() => page.IsLoaded && repeater.TryGetElement(0) is FrameworkElement);
                page.UpdateLayout();
                if (((TextBlock)page.FindName("CountText")).Text != "1" || ((TextBlock)page.FindName("ResultNounText")).Text != "RESULT"
                    || ((FrameworkElement)page.FindName("OrderCombo")).Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Personal catalog count/list order differs from the source contract.");
                viewport.Background = page.Background;
                await MediaParityNativeFixture.CaptureAsync(viewport, $"personal-{source}-loaded-{width:0}.png");
                var poster = (SiloPlayer.Controls.PosterCard)repeater.TryGetElement(0);
                var menu = SiloPlayer.Controls.MediaItemMenu.Build(poster.MediaItem!, owner: poster);
                menu.ShowAt(poster); await Task.Delay(50);
                var label = source == "favorites" ? "Remove from Favorites" : "Remove from Watchlist";
                var remove = menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == label);
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(remove).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => wire.RemovedSources.Contains(source));
                await UntilAsync(() => repeater.ItemsSource is System.Collections.ICollection items && items.Count == 0);
                if (((TextBlock)page.FindName("CountText")).Text != "0" || ((TextBlock)page.FindName("ResultNounText")).Text != "RESULTS")
                    throw new InvalidOperationException("Personal removal must immediately update both the count and its plural label.");
                menu.Hide();
                wire.CatalogState = "empty";
                await ReloadCatalogAsync(page); page.UpdateLayout();
                var empty = (TextBlock)page.FindName("EmptyText");
                Program.Log($"Catalog empty {source}/{width}: padding={empty.Margin}, font={empty.FontSize}.");
                if (empty.Visibility != Visibility.Visible || empty.Text != "No items found." || Math.Abs(empty.Margin.Top - 48) > .1 || empty.FontSize != 16)
                    failures.Add($"Personal {source}/{width} empty state differs from current WebUI font/py-12 spacing.");
                await MediaParityNativeFixture.CaptureAsync(viewport, $"personal-{source}-empty-{width:0}.png");
                wire.CatalogState = "error";
                await ReloadCatalogAsync(page);
                await Task.Delay(30); page.UpdateLayout();
                var title = (TextBlock)page.FindName("CatalogErrorTitle");
                var panel = (FrameworkElement)page.FindName("CatalogErrorPanel");
                var retryText = string.Join("/", Descendants<TextBlock>((Button)page.FindName("CatalogRetryButton")).Select(t => t.Text));
                Program.Log($"Catalog error {source}/{width}: panel={panel.GetType().Name}/{panel.Visibility}, title='{title.Text}' {title.FontSize}/{title.FontWeight.Weight}, retry='{retryText}', geometry={(panel is Border border ? $"{border.Padding}/{border.CornerRadius}" : "old stack")}.");
                if (((FrameworkElement)page.FindName("CatalogErrorPanel")).Visibility != Visibility.Visible
                    || title.Text != "Could not load catalog results." || title.FontSize != 16 || title.FontWeight.Weight != 500
                    || !Descendants<TextBlock>((Button)page.FindName("CatalogRetryButton")).Any(t => t.Text == "Retry catalog")
                    || page.FindName("CatalogErrorPanel") is not Border { Padding.Top: 64, Padding.Left: 16, CornerRadius.TopLeft: 20 })
                    failures.Add($"Personal {source}/{width} error/retry paint differs from the actual current WebUI.");
                await MediaParityNativeFixture.CaptureAsync(viewport, $"personal-{source}-error-{width:0}.png");
                wire.CatalogState = "loaded";
                Invoke((Button)page.FindName("CatalogRetryButton"));
                await UntilAsync(() => repeater.TryGetElement(0) is FrameworkElement && ((FrameworkElement)page.FindName("CatalogErrorPanel")).Visibility == Visibility.Collapsed);
                wire.CatalogState = "loading"; wire.CatalogHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = ReloadCatalogAsync(page);
                await UntilAsync(() => ((FrameworkElement)page.FindName("CatalogLoadingRepeater")).Visibility == Visibility.Visible);
                await MediaParityNativeFixture.CaptureAsync(viewport, $"personal-{source}-loading-{width:0}.png");
                wire.CatalogState = "loaded"; wire.CatalogHold.SetResult(Wire.CatalogResponse("loaded")); await pending;
                wire.CatalogHold = null;
                if (source == "favorites" && width == 1280)
                {
                    wire.CatalogTotal = 171; wire.RemovedSources.Clear();
                    await ReloadCatalogAsync(page);
                    await UntilAsync(() => ((TextBlock)page.FindName("CountText")).Text == "171" && repeater.TryGetElement(0) is SiloPlayer.Controls.PosterCard);
                    var partialPoster = (SiloPlayer.Controls.PosterCard)repeater.TryGetElement(0);
                    var partialMenu = SiloPlayer.Controls.MediaItemMenu.Build(partialPoster.MediaItem!, owner: partialPoster);
                    partialMenu.ShowAt(partialPoster); await Task.Delay(50);
                    var partialRemove = partialMenu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == "Remove from Favorites");
                    ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(partialRemove).GetPattern(PatternInterface.Invoke)).Invoke();
                    await UntilAsync(() => wire.RemovedSources.Contains(source) && ((TextBlock)page.FindName("CountText")).Text == "170");
                    partialMenu.Hide(); wire.CatalogTotal = 1;
                    Program.Log("PASS: removal retains total count for a partially loaded personal catalog.");
                }
                Program.Log($"Checked native {source}/{width} loaded, real menu removal, empty, error/retry and held-loading transition.");
            }
            finally
            {
                wire.CatalogHold?.TrySetResult(Wire.CatalogResponse("empty")); wire.CatalogHold = null;
                wire.CatalogState = "empty"; wire.CatalogTotal = 1; wire.RemovedSources.Clear();
                frame.Navigate(typeof(Page)); parent.Children.Remove(viewport); navigation.Frame = null;
                await Task.Delay(30);
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" ", failures));
    }

    private static Task ReloadCatalogAsync(CatalogPage page) =>
        (Task)typeof(CatalogPage).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { true })!;

    private static async Task PhysicalPersonalPagesAsync(NavigationService navigation, Wire wire)
    {
        var frame = new Frame(); var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12) };
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = new GridLength(1, GridUnitType.Star) } } };
        root.Children.Add(toolbar); root.Children.Add(frame); Grid.SetRow(frame, 1);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window { Title = "Silo parity — isolated personal pages", Content = root };
        void Button(string label, Action click)
        {
            var button = new Microsoft.UI.Xaml.Controls.Button { Content = label };
            button.Click += (_, _) => click(); toolbar.Children.Add(button);
        }
        void Catalog(string source)
        {
            wire.CatalogState = "loaded";
            navigation.NavigateImmediately(typeof(CatalogPage), new CatalogNavigation(source,
                source == "favorites" ? "Favorites" : "Watchlist",
                source == "favorites" ? "Movies and shows you've marked as favorites." : "Things you've saved to watch later."));
        }
        Button("Fixture Favorites", () => Catalog("favorites"));
        Button("Fixture Watchlist", () => Catalog("watchlist"));
        Button("Fixture Notifications", () => navigation.NavigateImmediately(typeof(NotificationsPage)));
        Button("Fixture Empty", () => { wire.CatalogState = "empty"; if (frame.Content is CatalogPage page) _ = ReloadCatalogAsync(page); });
        Button("Fixture Error", () => { wire.CatalogState = "error"; if (frame.Content is CatalogPage page) _ = ReloadCatalogAsync(page); });
        Button("Finish isolated verification", () => complete.TrySetResult());
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 900)); window.Activate();
            navigation.Frame = frame; Catalog("favorites");
            Program.Log("PHYSICAL_PERSONAL_READY: isolated fake API only; no user state or production mutation.");
            await complete.Task.WaitAsync(TimeSpan.FromMinutes(15));
            Program.Log("PASS: isolated physical personal verification host completed normally.");
        }
        finally { frame.Navigate(typeof(Page)); navigation.Frame = null; window.Close(); await Task.Delay(50); }
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
        internal bool EmptyTitles;
        internal string CatalogState = "empty";
        internal int CatalogTotal = 1;
        internal TaskCompletionSource<HttpResponseMessage>? CatalogHold;
        internal HashSet<string> RemovedSources = new();
        private string _physicalPreferences = "{\"enabled\":true,\"notify_favorites\":true,\"notify_watchlist\":true,\"notify_continue_watching\":true,\"notify_next_up\":true}";
        private bool _allRead;
        private async Task<HttpResponseMessage> SavePhysicalPreferencesAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _physicalPreferences = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(_physicalPreferences) };
        }
        internal static HttpResponseMessage CatalogResponse(string state, int total = 1) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(state == "loaded"
                ? "{\"items\":[{\"content_id\":\"personal-one\",\"title\":\"Alpha Feature\",\"type\":\"movie\",\"year\":2025,\"user_state\":{\"is_favorite\":true,\"in_watchlist\":true}}],\"total\":1,\"total_exact\":true,\"page\":{\"has_more\":false}}".Replace("\"total\":1,", "\"total\":" + total + ",", StringComparison.Ordinal)
                : "{\"items\":[],\"total\":0,\"total_exact\":true,\"page\":{\"has_more\":false}}")
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Program.Log($"Personal fixture {request.Method} {path}");
            if (request.RequestUri.Host != "personal-lists.invalid") throw new InvalidOperationException("Unexpected remote personal request.");
            if (path == "/api/v2/notifications/preferences" && request.Method == HttpMethod.Put)
                return SavePhysicalPreferencesAsync(request, ct);
            if (path == "/api/v2/notifications/read-all" && request.Method == HttpMethod.Post)
                return MarkAllPhysicalAsync(request, ct);
            if (path == "/api/v2/catalog")
                return CatalogState == "loading" ? CatalogHold!.Task : Task.FromResult(CatalogState == "error"
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : CatalogResponse(CatalogState, CatalogTotal));
            if (request.Method == HttpMethod.Delete && path.EndsWith("/personal-one"))
            {
                RemovedSources.Add(path.Contains("favorites") ? "favorites" : "watchlist");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (path.EndsWith("/watchlist/titles") && EmptyTitles) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") });
            if (path.EndsWith("/watchlist/titles") && FailTitles) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var json = path switch
            {
                "/api/v2/requests/status" => "{\"requests_enabled\":true,\"watchlist_titles_supported\":true}",
                "/api/v2/watchlist/titles" => "{\"items\":[{\"media_type\":\"series\",\"tmdb_id\":42,\"title\":\"Fixture series\",\"year\":2026,\"request\":{\"requestable\":true}},{\"media_type\":\"movie\",\"tmdb_id\":43,\"title\":\"Needs attention\",\"status\":\"needs_review\",\"request\":{\"requestable\":false}}]}",
                "/api/v2/notifications" => "{\"items\":[{\"id\":\"n1\",\"type\":\"episode.available\",\"series_id\":\"series-fixture\",\"episode_id\":\"episode-fixture\",\"series_title\":\"Fixture series\",\"episode_title\":\"New episode\",\"reason_flags\":{\"next_up\":true},\"season_number\":2,\"episode_number\":1,\"created_at\":\"2026-10-01T12:00:00Z\"},{\"id\":\"n2\",\"type\":\"request.declined\",\"reason_flags\":{\"title\":\"Request series\",\"media_type\":\"series\",\"tmdb_id\":42},\"created_at\":\"2026-10-01T12:00:00Z\"}],\"read_cutoff\":\"fixture-cutoff\"}",
                "/api/v2/notifications/unread-count" => _allRead ? "{\"count\":0}" : "{\"count\":2}",
                "/api/v2/notifications/preferences" => _physicalPreferences,
                _ when path.EndsWith("/read") => "{}",
                _ when path.EndsWith("/filters") => "{}",
                _ when request.Method == HttpMethod.Get => "{\"items\":[]}",
                _ => throw new InvalidOperationException("Unexpected personal mutation.")
            };
            if (_allRead && path == "/api/v2/notifications") json = request.RequestUri.Query.Contains("status=unread", StringComparison.Ordinal)
                ? "{\"items\":[],\"read_cutoff\":\"fixture-cutoff\"}"
                : json.Replace("\"created_at\":", "\"read_at\":\"2026-10-09T12:00:00Z\",\"created_at\":", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
        private async Task<HttpResponseMessage> MarkAllPhysicalAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.GetProperty("through").GetString() != "fixture-cutoff")
                throw new InvalidOperationException("Physical Mark all did not retain the displayed cutoff.");
            _allRead = true;
            Program.Log("PHYSICAL_MARK_ALL_COMMITTED: matching displayed cutoff, fake API only.");
            return new(HttpStatusCode.NoContent);
        }
    }
}
