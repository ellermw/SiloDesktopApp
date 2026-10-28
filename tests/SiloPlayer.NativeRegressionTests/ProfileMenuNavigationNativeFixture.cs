using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class ProfileMenuNavigationNativeFixture
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync()
    {
        // The app owns one global MainWindow. Run each width in a fresh host
        // rather than constructing multiple application shells in one process.
        var width = int.TryParse(Environment.GetEnvironmentVariable("SILO_PROFILE_MENU_WIDTH"), out var requested) ? requested : 1920;
        await CheckWindow(width);
    }
    private static async Task CheckWindow(int width)
    {
        var servicesField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var mainProperty = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previous = servicesField.GetValue(null); var previousMain = mainProperty.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://profile-menu.fixture.invalid");
        // This AuthService has no credential store. It can only clear synthetic identity.
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("first", profile: new() { Id = "first", Name = "First", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "local-" + width));
        var prefs = local.Load(); prefs.SiloCastEnabled = false;
        prefs.TasteSeedDismissedProfileIds.AddRange(["first", "second"]); local.Save(prefs);
        var serviceCollection = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(local).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<ProfileSelectViewModel>().AddSingleton<ServerSelectViewModel>()
            .AddTransient<WatchTogetherJoinViewModel>()
            .AddSingleton<HomeApi>().AddSingleton<HomeViewModel>().AddTransient<LibraryViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "images-" + width)))
            .AddSingleton<ItemDetailPrefetchCache>();
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_UPDATES") == "1")
            AppUpdateNativeFixture.Configure(serviceCollection);
        await using var provider = serviceCollection.BuildServiceProvider();
        servicesField.SetValue(null, provider); MainWindow? window = null; var fixtureWindowClosed = false;
        var traced = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> layoutTrace = (_, args) =>
        {
            var stack = args.Exception.StackTrace ?? "";
            if (stack.Contains("SettingsPage.ReflowSettingsShell", StringComparison.Ordinal) && traced++ < 3)
                Program.Log("Settings layout failure: " + args.Exception.GetType().Name + "\n" + stack);
        };
        AppDomain.CurrentDomain.FirstChanceException += layoutTrace;
        try
        {
            window = new MainWindow(); mainProperty.SetValue(null, window);
            window.Closed += (_, _) => fixtureWindowClosed = true;
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Instance)!.CreateDelegate(typeof(RoutedEventHandler), window);
            var navigation = provider.GetRequiredService<NavigationService>();
            var height = int.TryParse(Environment.GetEnvironmentVariable("SILO_NATIVE_PARTY_HEIGHT"), out var partyHeight) ? partyHeight : 1080;
            var x = int.TryParse(Environment.GetEnvironmentVariable("SILO_NATIVE_WINDOW_X"), out var fixtureX) ? fixtureX : -20000;
            var y = int.TryParse(Environment.GetEnvironmentVariable("SILO_NATIVE_WINDOW_Y"), out var fixtureY) ? fixtureY : -20000;
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TRANSPARENT_ON_MONITOR") == "1") TransparentFixtureWindow.Configure(window);
            window.AppWindow.Move(new(x, y)); window.AppWindow.ResizeClient(new(width, height)); window.AppWindow.Show(false);
            await Task.Delay(160); window.RestoreMainPane(); navigation.Navigate<Page>();
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_UPDATES") == "1")
            {
                await AppUpdateNativeFixture.CheckAsync(window, provider);
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_VOLUME_PERSISTENCE") == "1")
            {
                await VolumePersistenceNativeFixture.RunAsync();
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_ACCESSIBILITY_NAVIGATION") == "1")
            {
                await AccessibilityNavigationNativeFixture.RunAsync(provider, navigation);
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_WATCH_PARTY_FREEZE") == "1")
            {
                await WatchPartyLayoutNativeFixture.RunAsync(window, navigation);
                return;
            }
            var footer = (Button)root.FindName("ProfileFooterButton");
            var flyout = (Flyout)root.FindName("ProfileFooterFlyout");
            async Task OpenMenu()
            {
                nav.IsPaneVisible = true; nav.IsPaneOpen = true;
                await Until(() => footer.IsLoaded && footer.XamlRoot != null);
                // Let the previous flyout close animation and deferred Hide,
                // plus sidebar reparenting, finish before opening it again.
                await Task.Delay(250); root.UpdateLayout();
                flyout.ShowAt(footer);
                await Until(() => flyout.Content is FrameworkElement { IsLoaded: true } &&
                    Descendants<Button>((DependencyObject)flyout.Content).Any(button => button.IsLoaded &&
                        Descendants<TextBlock>(button).Any(text => text.Text == "Settings")));
            }
            await OpenMenu();
            var settings = Descendants<Button>((DependencyObject)flyout.Content)
                .Single(button => Descendants<TextBlock>(button).Any(text => text.Text == "Settings"));
            Program.Log($"Before Settings click at {width}: Frame.Width={navigation.Frame!.Width}, Frame.ActualWidth={navigation.Frame.ActualWidth}.");
            Click(settings);
            await Until(() => navigation.Frame.Content is SettingsPage page && page.IsLoaded);
            var settingsPage = (SettingsPage)navigation.Frame.Content;
            var heading = Descendants<TextBlock>((DependencyObject)settingsPage.FindName("SettingsHeaderGrid")).First();
            if (!double.IsFinite(heading.FontSize)) throw new InvalidOperationException("Settings produced an invalid heading font size.");
            if (width >= 1024 && !ReferenceEquals(typeof(MainWindow).GetField("_settingsSidebarPage", Instance)!.GetValue(window), settingsPage))
                throw new InvalidOperationException("Settings failed to attach its shell sidebar.");
            // Leave Settings through real frame navigation. This detaches the page
            // during layout and exercises the second stack in the installed log.
            navigation.Navigate<LibraryPage>(new LibraryPage.NavigationArgs(new Library { Id = 901, Name = "Fixture library", Type = "movie" }, "Library"));
            await Until(() => navigation.Frame.Content is LibraryPage library && library.ViewModel.Items.Count == 1 && !library.ViewModel.IsLoading);
            navigation.Navigate<HomePage>();
            await Until(() => navigation.Frame.Content is HomePage home && home.IsLoaded && !home.ViewModel.IsLoading && home.ViewModel.Sections.Any(section => section.Items.Count > 0));
            Program.Log($"PASS: {width}px Settings opened and Library/Home loaded populated data after leaving Settings.");
            await OpenMenu(); Click((Button)root.FindName("ProfileSwitchButton"));
            await Until(() => navigation.Frame.Content is ProfileSelectPage profiles && profiles.ViewModel.Profiles.Count == 2 && !profiles.ViewModel.IsLoading);
            var profilesPage = (ProfileSelectPage)navigation.Frame.Content;
            await Until(() => profilesPage.IsLoaded && Descendants<Button>(profilesPage).Any(button => button.Tag is Profile profile && profile.Id == "second"));
            Click(Descendants<Button>(profilesPage).Single(button => button.Tag is Profile profile && profile.Id == "second"));
            await Until(() => auth.SelectedProfileId == "second" && navigation.Frame.Content is HomePage home && home.IsLoaded && !home.ViewModel.IsLoading && home.ViewModel.Sections.Any(section => section.Items.Count > 0));
            await OpenMenu(); Click((Button)root.FindName("ProfileLogoutButton"));
            await Until(() => navigation.Frame.Content is ServerSelectPage && !auth.IsLoggedIn && wire.Logouts == 1);
            Program.Log($"PASS: {width}px actual profile popup Settings, Library and Home return, Switch Profile/reselection and Logout all reach their destinations.");
        }
        finally
        {
            if (window != null) { if (!fixtureWindowClosed) window.Close(); await Task.Delay(250); }
            AppDomain.CurrentDomain.FirstChanceException -= layoutTrace;
            mainProperty.SetValue(null, previousMain); servicesField.SetValue(null, previous);
        }
    }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready)
    { for (var i = 0; i < 240 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new InvalidOperationException("Profile menu navigation failed to reach its destination."); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var next in Descendants<T>(child)) yield return next;
        }
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Logouts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "profile-menu.fixture.invalid") throw new InvalidOperationException("External network access blocked.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/auth/logout") Logouts++;
            var partyProbe = Environment.GetEnvironmentVariable("SILO_NATIVE_WATCH_PARTY_FREEZE") == "1";
            var itemCount = partyProbe ? 100 : 1;
            var items = Enumerable.Range(0, itemCount).Select(index => new
            {
                content_id = index == 0 ? "fixture-movie" : "fixture-movie-" + index, type = "movie", title = index == 0 ? "Fixture movie" : "Fixture movie " + index
            }).ToArray();
            object body = path switch
            {
                "/api/v2/auth/me" => new { id = "fixture", username = "Fixture", role = "user" },
                "/api/v2/profiles" => new { items = new object[] { new { id = "first", name = "First", is_primary = true }, new { id = "second", name = "Second", is_primary = false } } },
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 22, client_families = new[] { "desktop", "web" }, supports_batched_effective = true },
                "/api/v2/home/layout" => new { sections = Enumerable.Range(0, partyProbe ? 4 : 1).Select(index => new { id = index == 0 ? "menu-home" : "menu-home-" + index, title = "Fixture Home " + index, section_type = "recently_added", item_limit = partyProbe ? 100 : 20 }).ToArray() },
                "/api/v2/home/sections/menu-home/items" => new { id = "menu-home", title = "Fixture Home", section_type = "recently_added", items },
                var section when section.StartsWith("/api/v2/home/sections/") && section.EndsWith("/items") => new { id = section.Split('/')[5], title = "Fixture Home", section_type = "recently_added", items },
                "/api/v2/catalog" => new { items, total = itemCount, window_cursor = "fixture-cursor", page = new { has_more = false } },
                _ => new { items = Array.Empty<object>(), settings = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
