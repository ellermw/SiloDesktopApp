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

    internal static void Log(string message) =>
        File.AppendAllText(Path.Combine(ResultDirectory, "results.txt"), message + Environment.NewLine);
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
                .AddSingleton(imageService)
                .AddSingleton(http)
                .AddSingleton(new CatalogApi(api))
                .AddSingleton(new MediaMaintenanceApi(api))
                .AddSingleton(new AuthService(api, new AuthApi(api)))
                .AddSingleton(new ToastService())
                .AddSingleton(new UICustomizationService(new SettingsApi(api)))
                .AddSingleton(new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected detail fetch")))
                .AddTransient<ItemDetailViewModel>()
                .AddTransient(_ => new CalendarViewModel(new CatalogApi(api), new SettingsService(Program.ResultDirectory)) { HasLoaded = true })
                .AddSingleton(new PlayerService(new PlaybackApi(api), new CatalogApi(api),
                    new AuthService(api, new AuthApi(api)), api, new SettingsService(Program.ResultDirectory), new SettingsApi(api)))
                .AddSingleton(overlays)
                .AddAccountSettingsFixture()
                .BuildServiceProvider();
            typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, services);

            var parent = new StackPanel();
            var section = new StackPanel { Orientation = Orientation.Horizontal };
            parent.Children.Add(section);
            // A hidden native window runs the real WinUI load/unload/decoding
            // lifecycle while leaving the user's running player untouched.
            _window = new Window { Content = parent };
            await Task.Delay(150); // Allow the hidden native tree to acquire its XamlRoot.
            await CalendarNavigatorRemainsVisible(parent);
            Program.Log("PASS: calendar week navigation remains visible while scrolling at narrow and desktop widths.");
            await AccountSettingsNativeFixture.RunAsync(parent);
            Program.Log("PASS: account password form and history import state/progress verified in actual SettingsPage controls.");
            await SubtitleDialogFixture.RunAsync(parent);
            Program.Log("PASS: native subtitle dialog preserves upload and gates online search for player/detail entry points.");
            await WatchedActionUpdatesAfterCompletionAndRevisit(parent);
            Program.Log("PASS: movie/episode completion updates the real watched button, including return navigation and manual state changes.");
            await EpisodeArtworkSurvivesSectionReattachment(parent, section);
            Program.Log("PASS: episode artwork survives reattachment and is released on real detach.");
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
            if (request.RequestUri?.AbsoluteUri != "https://fixture.invalid/still.png")
                throw new InvalidOperationException("Unexpected fixture request.");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jT1sAAAAASUVORK5CYII=")),
            });
        }
    }
}
