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

internal static class UpstreamShellNativeFixture
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
            var originalRows = nav.MenuItems.ToArray();
            navigation.Navigate<SettingsPage>(); await Task.Delay(300);
            var page = (SettingsPage)navigation.Frame!.Content;
            Check(Named<FrameworkElement>("SettingsSidebarHeader").Visibility == Visibility.Visible, "Wide Settings did not replace app sidebar");
            Check(page.ActiveSettingsSection == "Playback", "Wide Settings did not default to Playback");
            var search = Named<TextBox>("SettingsSidebarSearch");
            var searchRight = search.TransformToVisual(root).TransformPoint(new(search.ActualWidth, 0)).X;
            Check(searchRight <= nav.OpenPaneLength - 12, "Settings search box extends into the clipped pane edge");
            Check(!nav.MenuItems.Contains(originalRows[0]), "App rows remained beside Settings navigation");
            page.SelectSettingsSection("TitlePages");
            Check(page.ActiveSettingsSection == "TitlePages", "Settings selection did not open Title Pages");
            await MediaParityNativeFixture.CaptureAsync(root, "shell-title-pages-1920.png");
            Named<TextBox>("SettingsSidebarSearch").Text = "metadata language";
            Check(nav.MenuItems.OfType<NavigationViewItem>().Any(item => item.Content as string == "Language & Subtitles"), "Settings search lost control keywords");
            Check(window.FocusSettingsSidebarSearch(), "Ctrl K could not target hosted search");
            Named<TextBox>("SettingsSidebarSearch").Text = "";
            window.AppWindow.ResizeClient(new(1080, 1080)); await Task.Delay(140);
            Check(Named<FrameworkElement>("SettingsSidebarHeader").Visibility == Visibility.Visible, "1080 viewport incorrectly used narrow Settings");
            await MediaParityNativeFixture.CaptureAsync(root, "shell-title-pages-1080.png");
            window.AppWindow.ResizeClient(new(600, 900)); await Task.Delay(140);
            Check(Named<FrameworkElement>("SettingsSidebarHeader").Visibility == Visibility.Collapsed, "Narrow Settings did not restore app sidebar");
            Check(nav.MenuItems.Contains(originalRows[0]), "Original app navigation row identity was lost");
            await MediaParityNativeFixture.CaptureAsync(root, "shell-settings-600.png");
            window.AppWindow.ResizeClient(new(1920, 1080)); await Task.Delay(140);
            navigation.GoBack(); await Task.Delay(100);
            Check(Named<FrameworkElement>("SettingsSidebarHeader").Visibility == Visibility.Collapsed && nav.MenuItems.Contains(originalRows[0]), "Back stranded Settings navigation");

            var player = provider.GetRequiredService<PlayerService>();
            var spoilerPreferences = provider.GetRequiredService<EpisodeSpoilerPreferences>();
            await spoilerPreferences.EnsureLoadedAsync();
            await Task.Delay(80); // Flush preference-change presentation callbacks before opening post-roll.
            Check(spoilerPreferences.HideOverviews, "Fixture did not resolve protected overviews");
            var episode = (EpisodeNavigationState)typeof(PlayerService).GetField("_episodeNavigation", instance)!.GetValue(player)!;
            episode.PrepareFor("current");
            player.SetNextEpisodeHint("current", "next", "Next", "Series", null, "Secret description", played: false);
            Named<FrameworkElement>("PlayingNextOverlay").Visibility = Visibility.Visible;
            typeof(MainWindow).GetField("_playingNextPresentationGeneration", instance)!.SetValue(window, 7L);
            await (Task)typeof(MainWindow).GetMethod("UpdatePlayingNextOverviewAsync", instance)!.Invoke(window, [7L])!;
            Check(Named<TextBlock>("PlayingNextOverviewText").Visibility == Visibility.Collapsed, "Playing Next disclosed protected description");
            player.SetNextEpisodeHint("current", "next", "Next", "Series", null, "Secret description", played: true);
            await (Task)typeof(MainWindow).GetMethod("UpdatePlayingNextOverviewAsync", instance)!.Invoke(window, [7L])!;
            Check(Named<TextBlock>("PlayingNextOverviewText").Text == "Secret description", "Playing Next kept a watched description hidden");
            Named<FrameworkElement>("PlayingNextOverlay").Visibility = Visibility.Collapsed;
            Program.Log("PASS: actual MainWindow Settings sidebar/default/keywords/1080/narrow/Back row preservation and Playing Next spoiler/watched visibility.");
            navigation.Frame.Content = null;
            await Task.Delay(120);
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
