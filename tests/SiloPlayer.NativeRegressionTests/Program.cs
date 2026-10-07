using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class Program
{
    internal static string AppDirectory => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_APP_DIRECTORY")!;
    internal static string ResultDirectory => Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_RESULT_DIRECTORY")!;

    [STAThread]
    private static void Main()
    {
        // Tests load the real published controls without constructing Silo.App,
        // starting its auth/navigation, or using the user's application data.
        Environment.SetEnvironmentVariable(LocalLog.LogDirectoryEnvironmentVariable, ResultDirectory);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(AppDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject.ToString()!);
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                new RegressionApp();
            });
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            Environment.ExitCode = 1;
        }
    }

    private static readonly object LogGate = new();
    internal static void Log(string message)
    {
        lock (LogGate)
            File.AppendAllText(Path.Combine(ResultDirectory, "results.txt"), message + Environment.NewLine);
    }
}

internal sealed class RegressionApp : Application, IXamlMetadataProvider
{
    private readonly SiloPlayer.SiloPlayer_XamlTypeInfo.XamlMetaDataProvider _provider = new();
    private Window? _window;
    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);
    public IXamlType GetXamlType(string name) => _provider.GetXamlType(name);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();

    public RegressionApp()
    {
        UnhandledException += (_, e) =>
        {
            Program.Log(e.Exception.ToString());
            Environment.ExitCode = 1;
            e.Handled = true;
            Exit();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(
                File.ReadAllText(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_THEME_PATH")!)));

            using var imageService = new ImageService(Path.Combine(Program.ResultDirectory, "image-cache"));
            using var http = new HttpClient(new FixtureHandler());
            var api = new SiloApiClient(http);
            api.SetBaseUrl("https://fixture.invalid");
            var overlays = new CardOverlayService(new SettingsApi(api));
            // Overlay preferences are unrelated to artwork and must not issue
            // requests. This is an isolated fixture service, not user state.
            typeof(CardOverlayService).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(overlays, true);
            using var services = new ServiceCollection()
                .AddSingleton(new SettingsService(Program.ResultDirectory))
                .AddSingleton(imageService)
                .AddSingleton(http)
                .AddSingleton(new CatalogApi(api))
                .AddSingleton(new MediaMaintenanceApi(api))
                .AddSingleton(new AuthService(api, new AuthApi(api)))
                .AddSingleton(new ToastService())
                .AddSingleton<WatchTogetherCoordinator>()
                .AddSingleton(new UICustomizationService(new SettingsApi(api)))
                .AddSingleton(new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected detail fetch")))
                .AddTransient<ItemDetailViewModel>()
                .AddTransient(_ => new CalendarViewModel(new CatalogApi(api), new SettingsService(Program.ResultDirectory)) { HasLoaded = true })
                .AddSingleton(new PlayerService(new PlaybackApi(api), new CatalogApi(api),
                    new AuthService(api, new AuthApi(api)), api, new SettingsService(Program.ResultDirectory), new SettingsApi(api)))
                .AddSingleton(overlays)
                .AddAccountSettingsFixture()
                .AddMediaParityFixture()
                .BuildServiceProvider();
            typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, services);

            var parent = new StackPanel();
            var section = new StackPanel { Orientation = Orientation.Horizontal };
            parent.Children.Add(section);
            // A hidden native window runs the real WinUI load/unload/decoding
            // lifecycle while leaving the user's running player untouched.
            _window = new Window { Content = parent };
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            _window.AppWindow.Show(false);
            await Task.Delay(150); // Allow the hidden native tree to acquire its XamlRoot.
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ACCESS_NAVIGATION") == "1")
            {
                await AccessNavigationNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed."); Exit(); return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "personal-lists")
            {
                await PersonalListsNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed."); Exit(); return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "request-interactions")
            {
                _window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                _window.AppWindow.Show(false);
                await RequestsLifecycleNativeFixture.RunAsync(parent);
                await RequestInteractionNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "browse-parity")
            {
                await BrowseParityNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "browse-acceptance")
            {
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_WIZARD_PRESENTATION") == "1")
                {
                    await BrowseWizardPresentationNativeFixture.RunAsync(parent);
                    Program.Log("PASS: native regression run completed.");
                    return;
                }
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_LIBRARY_STATE") == "1")
                {
                    await BrowseLibraryStateNativeFixture.RunAsync(parent);
                    Program.Log("PASS: native regression run completed.");
                    return;
                }
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_BROWSE_CALLER_GRIDS") == "1")
                {
                    await BrowseCallerGridsNativeFixture.RunAsync(parent);
                    Program.Log("PASS: native regression run completed.");
                    return;
                }
                if (Environment.GetEnvironmentVariable("SILO_NATIVE_COLLECTIONS_ACCEPTANCE") == "1")
                {
                    await CollectionsAcceptanceNativeFixture.RunAsync(parent);
                    Program.Log("PASS: native regression run completed.");
                    return;
                }
                await BrowseAcceptanceNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "account-latest")
            {
                if (Environment.GetEnvironmentVariable("SILO_ACCOUNT_LATEST_CASE") == "network-login")
                {
                    await NetworkAuthNativeFixture.RunAsync(parent);
                    Program.Log("PASS: native regression run completed.");
                    return;
                }
                await AccountLatestNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "request-detail-parity")
            {
                await RequestDetailParityNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "shared-controls")
            {
                await SharedControlsNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "conditional-dialogs")
            {
                await ConditionalDialogsNativeFixture.RunAsync(parent);
                await ConditionalInspectionNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "media-parity")
            {
                await MediaParityNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "account-parity")
            {
                await AccountParityNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "account-coverage")
            {
                await AccountCoverageNativeFixture.RunAsync(parent);
                Program.Log("PASS: native regression run completed.");
                return;
            }
            await HomeRefreshPreservesCards(parent);
            Program.Log("PASS: Continue Watching refreshes preserve unchanged native cards.");
            await RenewedArtworkRetriesOnlyMissingImages(parent);
            Program.Log("PASS: renewed artwork retries failed images and preserves loaded images across Home card types.");
            await CalendarNavigatorRemainsVisible(parent);
            Program.Log("PASS: calendar week navigation remains visible while scrolling at narrow and desktop widths.");
            await AccountSettingsNativeFixture.RunAsync(parent);
            Program.Log("PASS: account password form and history import state/progress verified in actual SettingsPage controls.");
            // WebView2 needs a loaded native window. Show the isolated fixture
            // off-screen without activation so the user's app keeps focus.
            _window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            _window.AppWindow.Show(false);
            await Task.Delay(100);
            await ReaderInteropFixture.RunAsync(parent);
            _window.AppWindow.Show(false);
            Program.Log("PASS: local EPUB/PDF location interoperability verified in native WebView2.");
            await WatchPartyNativeFixture.RunAsync();
            Program.Log("PASS: published Watch Party coordinator and player transport integration.");
            await SharedAppearanceNativeFixture.RunAsync(parent);
            await SharedControlsNativeFixture.RunAsync(parent);
            Program.Log("PASS: shared appearance and accessibility verified in native resources and settings controls.");
            await RequestViewerNativeFixture.RunAsync(parent);
            await RequestsLifecycleNativeFixture.RunAsync(parent);
            await RequestInteractionNativeFixture.RunAsync(parent);
            await PersonalListsNativeFixture.RunAsync(parent);
            await DetailPresentationNativeFixture.RunAsync(parent);
            await MediaParityNativeFixture.RunAsync(parent);
            await ConditionalDialogsNativeFixture.RunAsync(parent);
            await ConditionalInspectionNativeFixture.RunAsync(parent);
            await BrowseParityNativeFixture.RunAsync(parent);
            await RequestDetailParityNativeFixture.RunAsync(parent);
            await AccountAccessNativeFixture.RunAsync(parent);
            await AccountParityNativeFixture.RunAsync(parent);
            await AccountCoverageNativeFixture.RunAsync(parent);
            await SubtitleDialogFixture.RunAsync(parent);
            Program.Log("PASS: native subtitle dialog preserves upload and gates online search for player/detail entry points.");
            await WatchedActionUpdatesAfterCompletionAndRevisit(parent);
            Program.Log("PASS: movie/episode completion updates the real watched button, including return navigation and manual state changes.");
            await EpisodeArtworkSurvivesSectionReattachment(parent, section);
            Program.Log("PASS: episode artwork survives reattachment and is released on real detach.");
            Program.Log("PASS: native regression run completed.");
        }
        catch (Exception ex)
        {
            Program.Log("FAIL: " + ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            _window?.Close();
            Exit();
        }
    }

    private static async Task CalendarNavigatorRemainsVisible(StackPanel parent)
    {
        foreach (var width in new[] { 600d, 1280d })
        {
            var page = new CalendarPage { Width = width, Height = 650 };
            parent.Children.Add(page);
            var rows = (StackPanel)page.FindName("DaysPanel");
            await Task.Delay(150);
            page.ViewModel.IsEmpty = true;
            foreach (var preset in new[] { "following", "trending", "everything" })
            {
                page.ViewModel.Filter = preset;
                await Task.Delay(50);
                foreach (var (name, value) in new[] { ("EmptyFollowingButton", "following"),
                    ("EmptyTrendingButton", "trending"), ("EmptyEverythingButton", "everything") })
                {
                    var button = (Button)page.FindName(name);
                    if (button.Visibility != (value == preset ? Visibility.Collapsed : Visibility.Visible))
                        throw new InvalidOperationException($"Calendar {preset} offers the wrong alternate preset {value}.");
                }
            }
            page.ViewModel.IsEmpty = false;
            rows.Children.Add(new Border { Height = 2400 });
            page.Measure(new Windows.Foundation.Size(width, 650));
            page.Arrange(new Windows.Foundation.Rect(0, 0, width, 650));
            page.UpdateLayout();
            await Task.Delay(150);
            var scroll = (ScrollViewer)page.FindName("ContentScrollViewer");
            var navigator = (Border)page.FindName("WeekNavigatorBorder");
            scroll.ChangeView(null, 700, null, true);
            await Task.Delay(150);
            page.UpdateLayout();
            var top = navigator.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y;
            if (scroll.VerticalOffset < 600 || top < 0 || top > 20 || navigator.ActualHeight < 40)
                throw new InvalidOperationException($"Calendar at width {width}: offset={scroll.VerticalOffset}, extent={scroll.ExtentHeight}, viewport={scroll.ViewportHeight}, rows={rows.Children.Count}, navigator top={top}, height={navigator.ActualHeight}");
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(150);
            page.UpdateLayout();
            if (navigator.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y < 50)
                throw new InvalidOperationException("Calendar navigator did not return below its heading");
            parent.Children.Remove(page);
        }
        Program.Log("PASS: native Calendar empty views offer only the other presets at narrow and wide sizes.");
    }

    private static async Task WatchedActionUpdatesAfterCompletionAndRevisit(StackPanel parent)
    {
        foreach (var type in new[] { "movie", "episode" })
        {
            var frame = new Frame { CacheSize = 2 };
            parent.Children.Add(frame);
            frame.Navigate(typeof(ItemDetailPage)); // No content parameter: no server loads.
            var detail = (ItemDetailPage)frame.Content;
            detail.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            detail.ViewModel.Item = new MediaItemDetail { ContentId = "watched-fixture", Type = type };
            var label = (TextBlock)detail.FindName("WatchedText");
            detail.ViewModel.Receive(new PlaybackProgressUpdated("watched-fixture", 99, 100, true));
            await DrainDispatcherAsync();
            if (label.Text != "Mark Unwatched")
                throw new InvalidOperationException($"{type} completion label: {label.Text}");

            frame.Navigate(typeof(Page));
            frame.GoBack();
            detail = (ItemDetailPage)frame.Content;
            detail.ViewModel.Item = new MediaItemDetail { ContentId = "watched-fixture", Type = type };
            label = (TextBlock)detail.FindName("WatchedText");
            detail.ViewModel.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "watched-fixture"));
            await DrainDispatcherAsync();
            if (label.Text != "Mark Unwatched") throw new InvalidOperationException("Revisited page did not observe watched state");
            detail.ViewModel.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedCleared, "watched-fixture"));
            await DrainDispatcherAsync();
            if (label.Text != "Mark Watched") throw new InvalidOperationException("Revisited page did not observe unwatched state");
            detail.ViewModel.Receive(new PlaybackProgressUpdated("watched-fixture", 99, 100, true));
            await DrainDispatcherAsync();
            if (label.Text != "Mark Unwatched") throw new InvalidOperationException("Revisited page did not observe completion");
            frame.Navigate(typeof(Page));
            parent.Children.Remove(frame);
        }
    }

    private static Task DrainDispatcherAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => drained.SetResult());
        return drained.Task;
    }

    private static async Task HomeRefreshPreservesCards(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "omitted-source", "renewed-artwork", "progress" })
        {
            HomeSectionWithItems Response(int iteration) => new()
            {
                Id = "continue-watching", SectionType = "continue_watching", Title = "Continue Watching",
                LoadCompleted = true, TotalCount = 3, ItemLimit = 20,
                Items = new(Enumerable.Range(0, 3).Select(i => new MediaItem
                {
                    ContentId = "episode-" + i, Type = "episode", Title = "Episode " + i,
                    ItemSource = scenario == "omitted-source" ? null : "continue_watching",
                    BackdropUrl = scenario == "renewed-artwork" ? "https://fixture.invalid/still.png?signature=" + iteration : null,
                    DurationSeconds = 1800, PositionSeconds = scenario == "progress" && i == 0 ? 120 + iteration : 120,
                })),
            };
            var mounted = Response(0);
            var row = new SectionRow { Section = mounted, Width = 1200 };
            parent.Children.Add(row);
            row.Measure(new Windows.Foundation.Size(1200, 500));
            row.Arrange(new Windows.Foundation.Rect(0, 0, 1200, 500));
            row.UpdateLayout();
            await Task.Delay(250);
            var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
            var original = Enumerable.Range(0, 3).Select(i => repeater.TryGetElement(i)).ToArray();
            if (original.Any(card => card == null)) throw new InvalidOperationException("Home fixture did not realize cards.");
            var prepared = 0;
            repeater.ElementPrepared += (_, _) => prepared++;
            for (var iteration = 1; iteration <= 3; iteration++)
            {
                HomeSectionReconciler.Apply(mounted, Response(iteration));
                await DrainDispatcherAsync();
                row.UpdateLayout();
                await Task.Delay(100);
            }
            Program.Log($"Home refresh {scenario}: {prepared} card preparations across 3 refreshes.");
            // Progress legitimately changes one card. Unchanged responses and
            // renewed signatures must leave the entire mounted row intact.
            if (prepared > (scenario == "progress" ? 3 : 0))
                failures.Add($"{scenario}: {prepared} card preparations");
            var firstStable = scenario == "progress" ? 1 : 0;
            for (var i = firstStable; i < 3; i++)
                if (!ReferenceEquals(original[i], repeater.TryGetElement(i)))
                    failures.Add($"{scenario}: unchanged card {i} was recreated");
            parent.Children.Remove(row);
            await DrainDispatcherAsync();
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    private static async Task RenewedArtworkRetriesOnlyMissingImages(StackPanel parent)
    {
        foreach (var kind in new[] { "landscape", "poster", "audiobook" })
        {
            MediaItem Item(string signature) => new()
            {
                ContentId = "renewal-" + kind, Type = kind == "audiobook" ? "audiobook" : "movie",
                Title = "Renewal fixture", PosterUrl = "https://fixture.invalid/still.png?signature=" + signature,
            };
            var mounted = Item("expired");
            var items = new System.Collections.ObjectModel.ObservableCollection<MediaItem> { mounted };
            FrameworkElement card = kind switch
            {
                "landscape" => new LandscapeCard { MediaItem = mounted },
                "poster" => new PosterCard { MediaItem = mounted },
                _ => new AudiobookSquareCard { MediaItem = mounted },
            };
            parent.Children.Add(card);
            var imageName = kind == "landscape" ? "BackdropImage" : kind == "poster" ? "PosterImage" : "CoverImage";
            var image = (Image)card.FindName(imageName);
            await Task.Delay(700);
            if (image.Source != null) throw new InvalidOperationException(kind + ": expired fixture unexpectedly loaded");
            MediaItemCollectionReconciler.Apply(items, new[] { Item("fresh") });
            for (var wait = 0; wait < 30 && image.Source is not BitmapImage { PixelWidth: > 0 }; wait++)
                await Task.Delay(100);
            if (image.Source is not BitmapImage { PixelWidth: > 0 })
                throw new InvalidOperationException(kind + ": renewed URL did not retry failed artwork");
            await Task.Delay(300);
            var decoded = image.Source;
            MediaItemCollectionReconciler.Apply(items, new[] { Item("newer") });
            await Task.Delay(400);
            if (!ReferenceEquals(decoded, image.Source) || image.Opacity != 1)
                throw new InvalidOperationException(kind + ": renewal cleared or faded an already loaded image");
            parent.Children.Remove(card);
            await Task.Delay(100);
            var observers = (Delegate?)typeof(MediaItem).GetField("ArtworkUrlsChanged", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mounted);
            if (observers?.GetInvocationList().Length > 0)
                throw new InvalidOperationException(kind + ": detached card retained artwork subscription");
            Program.Log($"Home artwork {kind}: failed load recovered; decoded image retained; subscription released.");
        }
    }

    private static async Task EpisodeArtworkSurvivesSectionReattachment(StackPanel parent, StackPanel section)
    {
        var staleUnloads = 0;
        var failures = new List<string>();
        var imageField = typeof(LandscapeCard).GetField(
            "BackdropImage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var cards = new List<LandscapeCard>();
        var images = new List<Image>();

        for (var iteration = 0; iteration < 12; iteration++)
        {
            section.Children.Clear();
            cards.Clear();
            images.Clear();
            for (var i = 0; i < 22; i++)
            {
                var card = new LandscapeCard
                {
                    MediaItem = new MediaItem
                    {
                        ContentId = "episode-" + i,
                        Title = "Episode " + (i + 1),
                        EpisodeNumber = i + 1,
                        ItemSource = "episode_carousel",
                        Type = "episode",
                        BackdropUrl = "https://fixture.invalid/still.png",
                    },
                };
                card.SetCardWidth(240);
                card.Unloaded += (_, _) => { if (card.IsLoaded) staleUnloads++; };
                cards.Add(card);
                images.Add((Image)imageField.GetValue(card)!);
                section.Children.Add(card);
            }

            // The detail page reorders its sections during episode enrichment.
            // Exercise this before, during and after the delayed image load.
            await Task.Delay(iteration % 4 * 150);
            parent.Children.Remove(section);
            parent.Children.Add(section);
            await Task.Delay(1200);

            var visible = images.Count(image => image.Source is BitmapImage { PixelWidth: > 0 } && image.Opacity == 1);
            Program.Log($"episode transition {iteration + 1}: {visible}/22 artwork images visible; stale unloads={staleUnloads}");
            if (visible != 22 || cards.Any(card => !card.IsLoaded))
                failures.Add($"transition {iteration + 1}: {visible}/22 visible artwork images");
        }

        parent.Children.Remove(section);
        await Task.Delay(300);
        if (cards.Any(card => card.IsLoaded) || images.Any(image => image.Source != null))
            failures.Add("a genuinely detached card retained its artwork");
        if (staleUnloads == 0)
            failures.Add("the native runtime did not exercise a deferred stale Unloaded event");
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join("; ", failures));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.GetLeftPart(UriPartial.Path) != "https://fixture.invalid/still.png")
                throw new InvalidOperationException("Unexpected fixture request.");
            if (request.RequestUri.Query.Contains("signature=expired", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jT1sAAAAASUVORK5CYII=")),
            });
        }
    }
}
