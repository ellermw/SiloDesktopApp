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
                .AddSingleton(overlays)
                .BuildServiceProvider();
            typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, services);

            var parent = new StackPanel();
            var section = new StackPanel { Orientation = Orientation.Horizontal };
            parent.Children.Add(section);
            // A hidden native window runs the real WinUI load/unload/decoding
            // lifecycle while leaving the user's running player untouched.
            _window = new Window { Content = parent };
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
