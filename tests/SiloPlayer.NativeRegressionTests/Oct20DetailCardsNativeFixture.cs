using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class Oct20DetailCardsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://oct20-detail.fixture.invalid"); client.SetProfile("fixture");
        using var auth = new AuthService(client, new AuthApi(client));
        var catalog = new CatalogApi(client); var cache = new ItemDetailPrefetchCache(catalog, auth);
        using var spoilers = new EpisodeSpoilerPreferences(new SettingsApi(client));
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "extra-image-cache"));
        var services = new Services(original, client, auth, catalog, cache, spoilers, images, http);
        slot.SetValue(null, services);
        using var lifetime = new CancellationTokenSource();
        ItemDetailPage? page = null;
        Button? pendingQuick = null;
        var detached = new List<FrameworkElement>();
        var captions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        try
        {
            parent.Children.Add(captions);
            foreach (var width in new[] { 140d, 160d, 240d, 420d })
            {
                captions.Children.Clear();
                var shortCaption = new EpisodeCardCaption(Episode("A short description."), "Feb 18, 2026", false, false, true) { Width = width };
                var longCaption = new EpisodeCardCaption(Episode(string.Join(" ", Enumerable.Repeat("A longer translated description", 25))), "Feb 18, 2026", false, false, false) { Width = width };
                captions.Children.Add(shortCaption); captions.Children.Add(longCaption);
                await Task.Delay(60); captions.UpdateLayout();
                if (shortCaption.TitleText.Text != "S01E02 · Pilot" || AutomationProperties.GetName(shortCaption.TitleText) != "Season 1, Episode 2, Pilot")
                    throw new InvalidOperationException("Shared native episode identity/spoken title differs.");
                if (shortCaption.MetadataText.Text != "Feb 18, 2026\u00a0\u00a0·\u00a0\u00a052m" || shortCaption.OverviewText.MaxLines != 3 ||
                    Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(shortCaption.OverviewText).Height < 54 || Math.Abs(shortCaption.ActualHeight - longCaption.ActualHeight) > .6)
                    throw new InvalidOperationException($"Episode caption lines or equal height differ at {width}px: metadata='{shortCaption.MetadataText.Text}', overview={shortCaption.OverviewText.ActualHeight}, short={shortCaption.ActualHeight}, long={longCaption.ActualHeight}.");
                if (shortCaption.WatchedIndicator.Visibility != Visibility.Visible || shortCaption.MetadataRow.Children.Count != 3)
                    throw new InvalidOperationException("Watched or translated/damaged metadata indicator missing.");
                await MediaParityNativeFixture.CaptureAsync(captions, $"oct20-episode-caption-{width}.png");
            }
            captions.Children.Clear();
            // Use a PNG emitted by WinUI's own encoder above. A tiny embedded
            // PNG can pass some decoders while being rejected by Windows WIC.
            wire.Png = await File.ReadAllBytesAsync(Path.Combine(Program.ResultDirectory, "oct20-episode-caption-420.png"));
            var hidden = new EpisodeCardCaption(Episode("A secret description."), "Feb 18, 2026", true, false, false);
            var compact = new EpisodeCardCaption(Episode("TV description."), "Feb 18, 2026", false, true, false);
            if (hidden.OverviewText.Visibility != Visibility.Collapsed || hidden.Presentation.ShowTranslation || hidden.MetadataRow.Visibility != Visibility.Visible ||
                compact.OverviewText.Visibility != Visibility.Collapsed || compact.MetadataRow.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Spoiler or compact TV caption policy changed.");

            page = new ItemDetailPage { Width = 1280, Height = 800 };
            var owner = new MediaItemDetail { ContentId = "owner", Type = "movie", Title = "Extras fixture" };
            page.ViewModel.Item = owner;
            Set(page, "_navigationCts", lifetime); Set(page, "_currentContentId", owner.ContentId);
            parent.Children.Add(page); await Task.Delay(60);
            // This matrix drives explicit widths on detached sections, so the
            // synthetic page must not reflow them from the host window width.
            var rootSizeHandler = (SizeChangedEventHandler)typeof(ItemDetailPage).GetMethod("OnRootSizeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(SizeChangedEventHandler), page);
            if (page.XamlRoot?.Content is FrameworkElement pageRoot) pageRoot.SizeChanged -= rootSizeHandler;
            page.SizeChanged -= (SizeChangedEventHandler)typeof(ItemDetailPage).GetMethod("ItemDetailPage_SizeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(SizeChangedEventHandler), page);
            foreach (var kind in new[] { "movie", "series" })
            {
                Call(page, "ArrangeCurrentWebUiContentOrder", kind);
                var sections = (StackPanel)page.FindName("DetailContentPanel");
                if (sections.Children.IndexOf((UIElement)page.FindName("ExtrasSection")) >= sections.Children.IndexOf((UIElement)page.FindName("TrailersSection")))
                    throw new InvalidOperationException("Local extras must precede YouTube for both movies and series.");
            }
            var extras = new List<ItemExtra>
            {
                new() { ContentId = "ok", Kind = "featurette", Title = "A local featurette", DurationSeconds = 125, StillUrl = "/ok.png" },
                new() { ContentId = "missing", Kind = "featurette", Title = "No image", DurationSeconds = 60,
                    StillThumbhash = Convert.ToBase64String(new byte[] { 32, 8, 2, 4, 128 }) },
                new() { ContentId = "failed", Kind = "featurette", Title = "Failed image", StillUrl = "/failed.png", StillThumbhash = "bad hash" },
                new() { ContentId = "late", Kind = "featurette", Title = "Late image", StillUrl = "/late.png" },
                new() { ContentId = "other", Kind = "featurette", Title = "Another local extra" },
            };
            var groups = (StackPanel)page.FindName("ExtrasGroupsPanel");
            ((StackPanel)page.FindName("ExtrasSection")).Children.Remove(groups);
            groups.Width = 640; parent.Children.Insert(0, groups); detached.Add(groups);
            await Task.Delay(40);
            Call(page, "BuildExtras", extras);
            var grid = ((StackPanel)groups.Children[0]).Children.OfType<Grid>().Single();
            // Mount the specific section directly. The synthetic detail page
            // has no navigation load to expose its normally collapsed body.
            grid.Width = 640;
            foreach (var button in grid.Children.OfType<Button>()) button.ApplyTemplate();
            await Task.Delay(60); grid.UpdateLayout();
            Program.Log($"TRACE extra mount loaded={grid.IsLoaded} buttons={grid.Children.Count} images={string.Join(';',Find<Image>(grid).Select(image => $"{image.Name}:{image.Source?.GetType().Name}:{image.IsLoaded}"))} requests={string.Join(',', wire.Paths)}");
            await Until(() => Find<Image>(grid).FirstOrDefault(image => image.Name == "ExtraStillImage")?.Source != null);
            await Until(() => Find<DefaultArtwork>(grid.Children[1]).SelectMany(Find<Image>).Any(image => image.Source != null));
            foreach (var (width, columns) in new[] { (390d, 2), (640d, 3), (1024d, 4), (1280d, 5) })
            {
                grid.Width = width;
                typeof(ItemDetailPage).GetMethod("ReflowExtras", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [grid, width]);
                grid.UpdateLayout(); await Task.Delay(50);
                if (grid.ColumnDefinitions.Count != columns) throw new InvalidOperationException($"Extras responsive column count differs at{width}: wanted{columns}, actual{grid.ColumnDefinitions.Count}.");
                foreach (var button in grid.Children.OfType<Button>())
                {
                    var still = ((StackPanel)button.Content).Children[0] as FrameworkElement;
                    if (still == null || Math.Abs(still.ActualHeight - still.ActualWidth * 9 / 16) > .6)
                        throw new InvalidOperationException("Extra still lost its 16:9 aspect.");
                    if (button.Tag is not string || !AutomationProperties.GetName(button).StartsWith("Play "))
                        throw new InvalidOperationException("Extra playback target or keyboard accessibility missing.");
                }
                await MediaParityNativeFixture.CaptureAsync(grid, $"oct20-extras-{width}.png");
            }
            var failed = (Button)grid.Children[2];
            if (Find<Image>(failed).First(image => image.Name == "ExtraStillImage").Source != null ||
                !Find<DefaultArtwork>(failed).Any(fallback => fallback.Visibility == Visibility.Visible))
                throw new InvalidOperationException("Failed extra artwork must retain its fallback.");
            client.SetProfile("replacement"); wire.ReleaseLate.TrySetResult();
            await Task.Delay(100);
            if (Find<Image>(grid.Children[3]).First(image => image.Name == "ExtraStillImage").Source != null)
                throw new InvalidOperationException("Stale-profile extra image was applied.");
            var peer = new ButtonAutomationPeer((Button)grid.Children[0]);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(40);
            if (services.PlaybackRequests != 0) throw new InvalidOperationException("Stale extra click attempted playback.");

            client.SetProfile("fixture"); wire.HideOverviews = true; spoilers.Invalidate(); await spoilers.EnsureLoadedAsync();
            page.ViewModel.Item = new() { ContentId = "viewed", Type = "episode", SeriesId = "series", UserData = new() };
            Set(page, "_currentContentId", "viewed"); page.ViewModel.IsWatched = false;
            var siblings = (List<Episode>)typeof(ItemDetailPage).GetField("_siblingEpisodes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            var other = new Episode { ContentId = "other-episode", SeasonNumber = 1, EpisodeNumber = 2,
                Title = "Sibling", Overview = "The sibling overview.", UserData = new() };
            siblings.Clear(); siblings.Add(new() { ContentId = "viewed", UserData = new() }); siblings.Add(other);
            Call(page, "BuildSiblingEpisodeCards");
            var siblingPanel = (StackPanel)page.FindName("SiblingEpisodesPanel");
            ((ScrollViewer)page.FindName("SiblingEpisodesScrollViewer")).Content = null;
            parent.Children.Remove(groups);
            parent.Children.Insert(0, siblingPanel); detached.Add(siblingPanel);
            foreach (var button in Find<Button>(siblingPanel)) button.ApplyTemplate();
            await Task.Delay(60); siblingPanel.UpdateLayout();
            var quick = Find<Button>(siblingPanel.Children[1]).Single(button => AutomationProperties.GetName(button) == "Mark Episode Watched");
            ((IInvokeProvider)new ButtonAutomationPeer(quick).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => wire.WatchedWrites == 1 && other.UserData?.Played == true);
            await page.ViewModel.ToggleWatchedCommand.ExecuteAsync(null); // Same command as the current episode's header.
            if (wire.WatchedWrites != 2 || !page.ViewModel.IsWatched) throw new InvalidOperationException("Current episode header command did not succeed.");
            Call(page, "BuildSiblingEpisodeCards");
            foreach (var button in Find<Button>(siblingPanel)) button.ApplyTemplate();
            await Task.Delay(60); siblingPanel.UpdateLayout();
            var retained = Find<EpisodeCardCaption>(siblingPanel.Children[1]).Single();
            if (retained.WatchedIndicator.Visibility != Visibility.Visible || retained.OverviewText.Visibility != Visibility.Visible ||
                !Find<Button>(siblingPanel.Children[1]).Any(button => AutomationProperties.GetName(button) == "Mark Episode Unwatched"))
                throw new InvalidOperationException("Sibling watched/spoiler/action state reverted after a different episode/header repaint.");

            page.ViewModel.Item = new() { ContentId = "series", Type = "series", EpisodeCount = 2,
                UserData = new() { UnplayedCount = 2 } };
            page.ViewModel.IsWatched = false; Set(page, "_currentContentId", "series");
            page.ViewModel.IsSeries = true; page.ViewModel.SelectedSeasonNumber = 1;
            page.ViewModel.Seasons.Clear(); page.ViewModel.Episodes.Clear();
            page.ViewModel.Seasons.Add(new() { ContentId = "season", SeasonNumber = 1, EpisodeCount = 2,
                UserData = new() { UnplayedCount = 2 } });
            var gridEpisode = new Episode { ContentId = "grid-child", SeasonNumber = 1, EpisodeNumber = 1,
                Overview = "A guarded grid description.", UserData = new() };
            page.ViewModel.Episodes.Add(gridEpisode); page.ViewModel.Episodes.Add(new() { ContentId = "grid-other", SeasonNumber = 1, EpisodeNumber = 2, UserData = new() });
            Call(page, "LayoutEpisodeGrid");
            var episodesGrid = (Grid)page.FindName("EpisodesPanel");
            ((ScrollViewer)page.FindName("EpisodesScroll")).Content = null;
            parent.Children.Remove(siblingPanel);
            episodesGrid.Width = 900; parent.Children.Insert(0, episodesGrid); detached.Add(episodesGrid);
            foreach (var button in Find<Button>(episodesGrid)) button.ApplyTemplate();
            await Task.Delay(60); episodesGrid.UpdateLayout();
            wire.HoldWatched = true;
            var childQuick = Find<Button>(episodesGrid.Children[0]).Single(button => AutomationProperties.GetName(button) == "Mark Episode Watched");
            pendingQuick = childQuick;
            ((IInvokeProvider)new ButtonAutomationPeer(childQuick).GetPattern(PatternInterface.Invoke)).Invoke();
            await wire.WatchedEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (gridEpisode.UserData!.Played || page.ViewModel.Seasons[0].UserData!.WatchedCount != 0)
                throw new InvalidOperationException("Optimistic card state mutated the view model before the accepted event could compute its count delta.");
            wire.ReleaseWatched.TrySetResult();
            await Until(() => page.ViewModel.Seasons[0].UserData?.WatchedCount == 1);
            if (!gridEpisode.UserData.Played || page.ViewModel.Item.UserData!.WatchedCount != 1 || page.ViewModel.IsWatched)
                throw new InvalidOperationException("Delayed accepted child mark lost the partial parent count.");
            Program.Log("PASS: shared native detail episode captions at140/160/240/420px, reserved3lines/spoken titles/metadata/spoilers/TV; extra2/3/4/5-column16:9 cards, artwork success/failure/stale-profile guard, stale keyboard playback guard and extras-before-YouTube.");
            Program.Log("PASS: shared sibling quick-watched result survives current-header/rail rebuild with watched badge, Unwatched action and revealed spoiler overview.");
            Program.Log("PASS: delayed child quick mark keeps authoritative episode/season state untouched until acknowledgement and then updates partial counts exactly once.");
        }
        finally
        {
            wire.ReleaseLate.TrySetResult(); wire.ReleaseWatched.TrySetResult();
            // An intentional RED can interrupt an async-void quick action.
            // Drain its held HTTP response while its fixture services exist.
            for (var count = 0; count < 100 && pendingQuick?.IsEnabled == false; count++) await Task.Delay(10);
            lifetime.Cancel();
            if (page != null) parent.Children.Remove(page);
            foreach (var element in detached) parent.Children.Remove(element);
            parent.Children.Remove(captions); slot.SetValue(null, original);
        }
    }

    private static Episode Episode(string overview) => new() { SeasonNumber = 1, EpisodeNumber = 2, Title = "Pilot", Runtime = 52,
        Overview = overview, MachineTranslatedFields = ["overview"], UserData = new() { Played = true }, Files = [new() { Unreadable = true }] };
    private static async Task Until(Func<bool> ready) { for (var count = 0; count < 120 && !ready(); count++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Extra artwork did not settle."); }
    private static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(owner, value);
    private static IEnumerable<T> Find<T>(DependencyObject owner) where T : DependencyObject
    {
        if (owner is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(owner); index++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(owner, index))) yield return child;
    }
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog,
        ItemDetailPrefetchCache cache, EpisodeSpoilerPreferences spoilers, ImageService images, HttpClient http) : IServiceProvider
    {
        public int PlaybackRequests;
        public object? GetService(Type type)
        {
            if (type == typeof(PlayerService)) { PlaybackRequests++; throw new InvalidOperationException("Fixture must not begin extra playback."); }
            return type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog :
                type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, cache) : type == typeof(ItemDetailPrefetchCache) ? cache :
                type == typeof(EpisodeSpoilerPreferences) ? spoilers : type == typeof(ImageService) ? images : type == typeof(HttpClient) ? http :
                type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : original.GetService(type);
        }
    }
    private sealed class Wire : HttpMessageHandler
    {
        public readonly TaskCompletionSource ReleaseLate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] Png = [];
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Paths = new();
        public bool HideOverviews;
        public int WatchedWrites;
        public bool HoldWatched;
        public readonly TaskCompletionSource WatchedEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseWatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct20-detail.fixture.invalid") throw new InvalidOperationException("Extra fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            Paths.Enqueue(path);
            if (path.StartsWith("/api/v2/watched/", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref WatchedWrites);
                if (HoldWatched) { WatchedEntered.TrySetResult(); await ReleaseWatched.Task.WaitAsync(ct); }
            }
            if (path == "/late.png") await ReleaseLate.Task;
            if (path is "/ok.png" or "/late.png") return new(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Png) };
            if (path == "/failed.png") return new(HttpStatusCode.NotFound);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = new object[]
                { new { key = "catalog.hide_unwatched_episode_overviews", value = HideOverviews },
                  new { key = "catalog.hide_unwatched_episode_images", value = false } } })) };
        }
    }
}
