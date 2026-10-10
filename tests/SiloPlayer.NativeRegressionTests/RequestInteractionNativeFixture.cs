using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RequestInteractionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-interaction.invalid");
        var api = new RequestsApi(client);
        var cache = new ItemDetailPrefetchCache((id, _) => Task.FromResult(new MediaItemDetail
        { ContentId = id, Type = "series", Title = "Library series", TmdbId = "42", PlayContentId = null }));
        var catalog = new CatalogApi(client);
        var navigation = new NavigationService();
        var vm = new RequestsViewModel(api) { DataVersion = 1 };
        typeof(RequestsViewModel).GetField("_lastLoadedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, DateTime.UtcNow);
        var outer = SiloPlayer.App.Services;
        using var services = new ServiceCollection().AddSingleton(api).AddSingleton(client).AddSingleton(catalog)
            .AddSingleton(new SettingsApi(client)).AddSingleton(cache).AddSingleton(navigation).AddSingleton(vm)
            .AddTransient<ItemDetailViewModel>().AddSingleton(new UICustomizationService(new SettingsApi(client)))
            .AddSingleton(new CardOverlayService(new SettingsApi(client))).AddSingleton(new ToastService())
            .AddSingleton<WatchTogetherCoordinator>()
            .AddSingleton(outer.GetRequiredService<PlayerService>()).AddSingleton(outer.GetRequiredService<AuthService>())
            .AddSingleton(outer.GetRequiredService<SettingsService>()).AddSingleton(outer.GetRequiredService<ImageService>())
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.SetValue(null, services);
        var failures = new List<string>();
        try
        {
            await HubPresentationAsync(parent, vm);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_REQUEST_HUB_CASE") == "status-help") return;
            var page = new RequestsPage();
            var result = new RequestMediaResult { MediaType = "series", TmdbId = 42, Title = "Interaction fixture", Request = new() { Requestable = true } };
            var card = (Grid)typeof(RequestsPage).GetMethod("BuildMediaPosterCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [result])!;
            parent.Children.Add(card);
            await Task.Delay(100); card.UpdateLayout();
            // DefaultArtwork owns a separate collapsed thumbhash image.
            // Exercise the visible poster, rather than that fallback's decoder surface.
            var image = ((Grid)card.Children.OfType<Border>().First().Child).Children.OfType<Image>().Single();
            var bitmap = new WriteableBitmap(2, 2);
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 4).SelectMany(bytes => bytes).ToArray());
            bitmap.Invalidate(); image.Source = bitmap;
            await Task.Delay(100); card.UpdateLayout();
            var open = card.Children.OfType<Button>().Single(button => button.Tag is RequestDetailNavigation);
            open.ApplyTemplate();
            VisualStateManager.GoToState(open, "Normal", false);
            var before = await RedPixelsAsync(card);
            VisualStateManager.GoToState(open, "PointerOver", false);
            await Task.Delay(40);
            var hover = await RedPixelsAsync(card);
            VisualStateManager.GoToState(open, "Pressed", false);
            var pressed = await RedPixelsAsync(card);
            VisualStateManager.GoToState(open, "Normal", false);
            var restored = await RedPixelsAsync(card);
            Program.Log($"Request poster red pixels: normal={before}, hover={hover}, pressed={pressed}, exit={restored}");
            if (before < 1000 || hover < before * .9 || pressed < before * .9 || restored < before * .9)
                failures.Add("Hover/press obscures the native request poster.");
            parent.Children.Remove(card);

            foreach (var (staged, promoted) in new[] { (false, true), (true, true), (false, false) })
            {
                handler.LibraryAvailable = promoted;
                var frame = new Frame { Width = 1000, Height = 700, CacheSize = 8 };
                navigation.Frame = frame; parent.Children.Add(frame);
                navigation.NavigationRequestHandler = staged ? (type, parameter) =>
                { parent.DispatcherQueue.TryEnqueue(() => navigation.NavigateImmediately(type, parameter)); return true; } : null;
                frame.Navigate(typeof(RequestsPage));
                var original = (RequestsPage)frame.Content;
                ((TextBox)original.FindName("SearchBox")).Text = "Scrubs";
                navigation.Navigate<RequestDetailPage>(new RequestDetailNavigation("series", 42));
                await UntilAsync(() => promoted ? frame.Content is ItemDetailPage : frame.Content is RequestDetailPage &&
                    ((Grid)((RequestDetailPage)frame.Content).FindName("LoadingLayer")).Visibility == Visibility.Collapsed);
                Program.Log($"Request history (staged={staged}, promoted={promoted}): " + string.Join(",", frame.BackStack.Select(entry => entry.SourcePageType.Name)));
                if (promoted)
                {
                    var detail = (ItemDetailPage)frame.Content;
                    var more = (MenuFlyout)detail.FindName("MoreFlyout");
                    // Item is published before the legacy favorite/watchlist reads
                    // and OnNavigatedTo's initialization finish. Invoking the
                    // private loader at that intermediate point races the real
                    // navigation's capability reset. Require the actual page to
                    // load its own action instead of synthesizing that lifecycle.
                    await UntilAsync(() => !detail.ViewModel.IsLoading && detail.ViewModel.Item != null &&
                        more.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Request Seasons"));
                    Program.Log($"PASS: promoted series naturally exposes Request seasons (staged={staged}) after its actual navigation load.");
                    if (!staged)
                    {
                        var requesting = (Task)typeof(ItemDetailPage).GetMethod("RequestSeriesSeasonsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(detail, [detail.ViewModel.Item!, 42])!;
                        ContentDialog? dialog = null;
                        await UntilAsync(() => (dialog = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot)
                            .SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child)))
                            .OfType<ContentDialog>().FirstOrDefault()) != null);
                        ToggleSwitch[] seasonChecks = [];
                        await UntilAsync(() =>
                        {
                            seasonChecks = Descendants(dialog!).OfType<ToggleSwitch>().Where(toggle => toggle.Tag is int).ToArray();
                            return seasonChecks.Any(check => (int)check.Tag == 1) && seasonChecks.Any(check => (int)check.Tag == 2);
                        });
                        var first = seasonChecks.Single(check => (int)check.Tag == 1);
                        var second = seasonChecks.Single(check => (int)check.Tag == 2);
                        if (first.IsEnabled || !second.IsEnabled) throw new InvalidOperationException("Available Season 1 and missing Season 2 have incorrect selection gates.");
                        second.IsOn = false; second.IsOn = true;
                        var primary = Descendants(dialog!).OfType<Button>().Single(button => (string?)button.Content == "Request Season 2");
                        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(primary);
                        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        await requesting;
                        if (handler.LastSeasons == null || !handler.LastSeasons.SequenceEqual(new[] { 2 }))
                            failures.Add("The library missing-season picker did not submit only Season 2.");
                        else Program.Log("PASS: library Request seasons opens the real picker and submits only Season 2 to the isolated fixture.");
                    }
                }
                navigation.GoBack();
                await Task.Delay(200);
                if (frame.Content is not RequestsPage || !ReferenceEquals(original, frame.Content) ||
                    ((TextBox)original.FindName("SearchBox")).Text != "Scrubs")
                    failures.Add($"Back (staged={staged}, promoted={promoted}) returned to {frame.Content?.GetType().Name} instead of the original request search.");
                navigation.NavigationRequestHandler = null;
                frame.BackStack.Clear(); frame.Content = null; parent.Children.Remove(frame); navigation.Frame = null;
            }
            if (failures.Count > 0) throw new InvalidOperationException(string.Join(" ", failures));
            Program.Log("PASS: request posters survive native hover/press/exit and promoted title Back returns to the original search.");
        }
        finally { field.SetValue(null, outer); }
    }

    private static async Task HubPresentationAsync(StackPanel parent, RequestsViewModel vm)
    {
        vm.MyRequests.Clear(); vm.DiscoverySections.Clear(); vm.Genres.Clear();
        foreach (var (state, index) in new[] { ("declined", 1), ("processing", 2), ("available", 3), ("cancelled", 4) })
            vm.MyRequests.Add(new MediaRequest { Id = index.ToString(), Title = "A request title with a deliberately long name to check narrow layout", MediaType = "series", TmdbId = index, State = state, Status = state,
                Year = 2026, Seasons = [1, 2], CreatedAt = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"),
                LastError = state == "declined" ? "This fixture request was declined." : "", Download = state == "processing" ? new() { Phase = "downloading", Percent = 42 } : null });
        vm.DiscoverySections.Add(new() { Key = "trending_movies", Title = "Trending movies", TotalPages = 2,
            Results = Enumerable.Range(1, 8).Select(id => new RequestMediaResult { MediaType = "movie", TmdbId = id, Title = "Fixture movie " + id, Year = 2026, Request = new() { Requestable = true } }).ToList() });
        vm.Genres.Add(new() { Slug = "drama", DisplayName = "Drama", GradientFrom = "#1e3a8a", GradientTo = "#581c87" });
        foreach (var width in new[] { 1280d, 900d, 500d })
        {
            var page = new RequestsPage { Width = width, Height = 800 }; parent.Children.Add(page);
            try
            {
                page.Measure(new Windows.Foundation.Size(width, 800)); page.Arrange(new Windows.Foundation.Rect(0, 0, width, 800));
                await Task.Delay(80); page.UpdateLayout();
                // PageBackdrop replaces Page.Background when the real page
                // loads. RTB does not paint the Page background itself, so
                // project that current brush after Loaded, not its stale seed.
                ((StackPanel)page.FindName("PageContent")).Background = page.Background;
                typeof(RequestsPage).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                await MediaParityNativeFixture.CaptureAsync(page, $"requests-hub-discover-{width:0}.png");
                typeof(RequestsPage).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, "yours");
                typeof(RequestsPage).GetMethod("UpdateTabState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                await Task.Delay(50); page.UpdateLayout();
                {
                    var help = (Button)typeof(RequestsPage).GetField("_mineHelp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                    help.Flyout.ShowAt(help);
                    FlyoutPresenter? presenter = null;
                    await UntilAsync(() => (presenter = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                        .SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child)))
                        .OfType<FlyoutPresenter>().FirstOrDefault()) is { ActualWidth: > 0 });
                    Program.Log($"Actual request status help {width}: outer={presenter!.ActualWidth}, guide={((Border)((Flyout)help.Flyout).Content).ActualWidth}, padding={presenter.Padding}.");
                    if (Math.Abs(presenter.ActualWidth - 320) > 1)
                        throw new InvalidOperationException("Request status help must use the current320px popover and16px insets.");
                    var guide = (Border)((Flyout)help.Flyout).Content;
                    var guideBadges = ((Grid)guide.Child).Children.OfType<Border>().ToArray();
                    if (guideBadges.Length != 8 || guideBadges.Any(badge => badge.ActualWidth >= 120 ||
                        Math.Abs(badge.ActualHeight - 22) > 1 || badge.CornerRadius.TopLeft != 10))
                        throw new InvalidOperationException("Request status badges must fit their labels in22px-high,10px-rounded pills rather than filling the120px guide column.");
                    // FlyoutPresenter itself has no RenderTargetBitmap surface
                    // on this WinUI version; capture its real content subtree.
                    await MediaParityNativeFixture.CaptureAsync((Border)((Flyout)help.Flyout).Content, $"requests-status-help-content-{width:0}.png");
                    help.Flyout.Hide();
                }
                var groups = (StackPanel)page.FindName("MyRequestsList");
                if (groups.Children.Count != 4) throw new InvalidOperationException("Your requests are not separated into the four current status groups.");
                if (!Descendants(groups).OfType<ProgressBar>().Any(bar => bar.Value == 42)) throw new InvalidOperationException("Your request rows omit real download progress.");
                var downloadBar = Descendants(groups).OfType<ProgressBar>().First(bar => bar.Value == 42);
                var fill = Descendants(downloadBar).OfType<Microsoft.UI.Xaml.Shapes.Rectangle>().First(r => r.Name == "DeterminateProgressBarIndicator");
                if (Math.Abs(fill.ActualHeight - 6) > .5 || Math.Abs(fill.ActualWidth / downloadBar.ActualWidth - .42) > .01)
                    throw new InvalidOperationException($"Download paint differs from source6px/42%: fill={fill.ActualWidth}x{fill.ActualHeight}, track={downloadBar.ActualWidth}.");
                var unknownDownload = SiloPlayer.Controls.RequestDownloadProgress.Build(new() { Phase = "queued" });
                if (Descendants(unknownDownload).OfType<ProgressBar>().Any())
                    throw new InvalidOperationException("Unknown download percent invents an indeterminate bar instead of showing its phase alone.");
                await MediaParityNativeFixture.CaptureAsync(page, $"requests-hub-yours-{width:0}.png");
                vm.IsLoading = true;
                typeof(RequestsPage).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                var skeleton = (StackPanel)typeof(RequestsPage).GetField("_mineLoadingSkeleton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                if (skeleton.Visibility != Visibility.Visible || ((StackPanel)page.FindName("LoadingSkeleton")).Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Yours loading uses discovery posters instead of request row skeletons.");
                await MediaParityNativeFixture.CaptureAsync(page, $"requests-hub-yours-loading-{width:0}.png");
                vm.IsLoading = false;
            }
            finally { vm.IsLoading = false; parent.Children.Remove(page); }
        }
        vm.MyRequests.Clear(); vm.DiscoverySections.Clear(); vm.Genres.Clear();
        Program.Log("PASS: request hub wide/narrow discovery, four request groups, row progress, long titles and Yours row skeletons.");
    }

    private static async Task<int> RedPixelsAsync(FrameworkElement element)
    {
        var target = new RenderTargetBitmap(); await target.RenderAsync(element);
        var bytes = (await target.GetPixelsAsync()).ToArray();
        var count = 0;
        for (var i = 0; i + 3 < bytes.Length; i += 4)
            if (bytes[i] < 40 && bytes[i + 1] < 40 && bytes[i + 2] > 210 && bytes[i + 3] > 210) count++;
        return count;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
    private static async Task UntilAsync(Func<bool> ready)
    { var deadline = DateTime.UtcNow.AddSeconds(5); while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Request navigation did not finish."); await Task.Delay(20); } }
    private sealed class Handler : HttpMessageHandler
    {
        internal bool LibraryAvailable = true;
        internal int[]? LastSeasons;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(20, ct);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/requests" && request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastSeasons = body.RootElement.GetProperty("seasons").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            }
            object data = path switch
            {
                "/api/v2/requests/status" => new { requests_enabled = true, allowed = true, season_requests_supported = true, missing_seasons_requestable = true },
                "/api/v2/requests/detail/series/42" => new { media_type = "series", tmdb_id = 42, title = "Interaction fixture", library_content_id = LibraryAvailable ? "library-series" : null, availability = "partial", request = new { requestable = true }, seasons = new[] { new { season_number = 1, availability = "available", episode_count = 8 }, new { season_number = 2, availability = "missing", episode_count = 8 } } },
                _ => new { items = Array.Empty<object>(), settings = Array.Empty<object>() }
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data)) };
        }
    }
}
