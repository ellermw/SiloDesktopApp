using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class Oct24ShellNativeFixture
{
    internal static async Task RunAsync()
    {
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
        var servicesField = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var mainProperty = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previous = servicesField.GetValue(null);
        var previousMain = mainProperty.GetValue(null);
        using var http = new HttpClient(new Wire());
        var api = new SiloApiClient(http); api.SetBaseUrl("https://shell-fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "shell-settings"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; local.Save(prefs);
        await using var provider = new ServiceCollection()
            .AddSingleton(api).AddSingleton(auth).AddSingleton(local).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "shell-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, provider);
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            mainProperty.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            T Named<T>(string name) => (T)root.FindName(name);
            var nav = Named<NavigationView>("NavView");
            var loaded = (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", instance)!.CreateDelegate(typeof(RoutedEventHandler), window);
            nav.Loaded -= loaded; // Never run the installed-account startup path in this fixture.
            nav.IsPaneVisible = true; // The fixture identity is already authenticated.
            window.AppWindow.Move(new(-20000, -20000));
            window.AppWindow.ResizeClient(new(1920, 1080)); window.AppWindow.Show(false);
            await Task.Delay(180);
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.Navigate(typeof(Page));
            // Reproduce the exact callback boundary from the logged failure:
            // PaneOpening must never synchronously mutate PaneDisplayMode.
            // Deliberately mismatch the native mode and requested preference,
            // invoke that handler directly, then allow its queued repair.
            var presentation = (SidebarPresentationState)typeof(MainWindow).GetField("_sidebarPresentation", instance)!.GetValue(window)!;
            presentation.BrowsingOpen = true;
            nav.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
            var beforeOpening = nav.PaneDisplayMode;
            typeof(MainWindow).GetMethod("NavView_PaneOpening", instance)!.Invoke(window, [nav, null]);
            Check(nav.PaneDisplayMode == beforeOpening, "PaneOpening synchronously mutated native pane display mode.");
            await Task.Delay(60);
            Check(nav.PaneDisplayMode == NavigationViewPaneDisplayMode.Left && nav.IsPaneOpen,
                "Queued PaneOpening repair did not reconcile the explicit pane preference.");
            for (var i = 0; i < 12; i++)
            {
                var state = (SidebarPresentationState)typeof(MainWindow).GetField("_sidebarPresentation", instance)!.GetValue(window)!;
                state.BrowsingOpen = i % 2 == 0;
                window.RestoreMainPane();
                navigation.NavigateImmediately(typeof(Page), i);
                window.AppWindow.ResizeClient(new(i % 3 == 0 ? 600 : 1920, 1080));
                await Task.Delay(35);
                window.AppWindow.ResizeClient(new(1920, 1080));
                window.RestoreMainPane();
                await Task.Delay(35);
                Check(nav.IsPaneOpen == state.IsOpen, "Frame navigation lost explicit desktop pane preference.");
                Check(nav.PaneDisplayMode == (state.IsOpen ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact), "Frame navigation stranded pane display mode.");
            }
            Program.Log("PASS: direct PaneOpening callback defers mode mutation; authenticated shell navigation and repeated compact/expanded/responsive pane transitions.");
        }
        finally
        {
            if (window != null) { window.Close(); await Task.Delay(120); }
            mainProperty.SetValue(null, previousMain);
            servicesField.SetValue(null, previous);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body = path switch
            {
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 17, supports_idempotent_writes = true, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_device_rename = true },
                "/api/v2/settings/values/effective" => new { items = new[] {
                    new { key = "catalog.hide_unwatched_episode_images", value = true, source = "profile" },
                    new { key = "catalog.hide_unwatched_episode_overviews", value = true, source = "profile" } } },
                "/api/v2/auth/me" => new { id = "fixture", username = "Fixture", role = "user" },
                _ => new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
