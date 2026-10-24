using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
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

internal static class SettingsVisualOct24NativeFixture
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
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "visual-settings"));
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
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "visual-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, provider);
        provider.GetRequiredService<ThemeService>().ApplyTheme("midnight-cinema");
        var family = ((Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["ThemeFontFamily"]).Source;
        Program.Log("Actual Settings theme font: " + family);
        Check(family.Contains("PlusJakartaSans", StringComparison.Ordinal), "Current runtime Settings font is not the official Plus Jakarta Sans asset.");
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_PUBLISHED_APP_DIRECTORY") is { Length: > 0 } published)
        {
            const string relativeFont = "Assets/Fonts/PlusJakartaSans-VariableFont_wght.ttf";
            var shippedFont = Path.Combine(published, relativeFont);
            Check(File.Exists(shippedFont), "Published installer payload omits the font file referenced by its PRI.");
            var publishedHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(shippedFont));
            var nativeHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Program.AppDirectory, relativeFont)));
            Check(publishedHash.SequenceEqual(nativeHash), "Native runtime font differs from the actual published payload font.");
            Program.Log("Published/native font SHA256: " + Convert.ToHexString(publishedHash));
        }
        MainWindow? window = null;
        try
        {
            window = new MainWindow();
            mainProperty.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            root.RequestedTheme = ElementTheme.Dark;
            T Named<T>(string name) => (T)root.FindName(name);
            var nav = Named<NavigationView>("NavView");
            var loaded = (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", instance)!.CreateDelegate(typeof(RoutedEventHandler), window);
            nav.Loaded -= loaded; // Never run the installed-account startup path in this fixture.
            nav.IsPaneVisible = true; // The fixture identity is already authenticated.
            window.AppWindow.Move(new(-20000, -20000));
            window.AppWindow.ResizeClient(new(1920, 1080)); window.AppWindow.Show(false);
            await Task.Delay(180);
            var footer = Named<Button>("ProfileFooterButton");
            var avatar = Named<TextBlock>("ProfileInitialText");
            var update = typeof(MainWindow).GetMethod("UpdateSidebarPanePresentation", instance)!;
            var failures = new List<string>();
            foreach (var open in new[] { false, true, false })
            {
                var state = (SidebarPresentationState)typeof(MainWindow).GetField("_sidebarPresentation", instance)!.GetValue(window)!;
                state.BrowsingOpen = open;
                window.RestoreMainPane();
                update.Invoke(window, [open]);
                await Task.Delay(120); root.UpdateLayout(); footer.ApplyTemplate();
                Check(nav.IsPaneOpen == open, "Fixture did not realize the requested actual pane state.");
                VisualStateManager.GoToState(footer, "PointerOver", false);
                await Task.Delay(60);
                var center = avatar.TransformToVisual(footer).TransformPoint(new(avatar.ActualWidth / 2, avatar.ActualHeight / 2));
                Program.Log($"Profile open={open}: hit={footer.ActualWidth}x{footer.ActualHeight}, avatar center={center.X},{center.Y}");
                if (!open && (Math.Abs(center.X - footer.ActualWidth / 2) > 1 || Math.Abs(center.Y - footer.ActualHeight / 2) > 1))
                    failures.Add("Compact profile hover backdrop is off-center from avatar.");
                await MediaParityNativeFixture.CaptureAsync(footer, $"profile-{open}.png");
                var avatarDiameter = await PaintedAvatarDiameter(footer);
                Program.Log($"Profile open={open}: painted avatar diameter={avatarDiameter:0.##} DIP");
                if (avatarDiameter < 26) failures.Add("Profile avatar is horizontally clipped instead of painting its complete28px circle.");
            }
            var frame = Named<Frame>("ContentFrame");
            var browsingState = (SidebarPresentationState)typeof(MainWindow).GetField("_sidebarPresentation", instance)!.GetValue(window)!;
            browsingState.BrowsingOpen = false;
            window.RestoreMainPane();
            var page = new SettingsPage();
            // Seed the same read-only default choices as the source-rendered
            // WebUI fixture; no real account or settings write is involved.
            var defaults = provider.GetRequiredService<SettingsViewModel>();
            defaults.QualityPreference = "auto";
            defaults.SubtitleMode = "auto";
            defaults.NextUpMode = "combined";
            typeof(SettingsPage).GetMethod("SyncComboBoxes", instance)!.Invoke(page, null);
            typeof(SettingsPage).GetMethod("BuildAccessibilityControls", instance)!.Invoke(page, null);
            typeof(SettingsPage).GetMethod("BuildDateTimeFormatControls", instance)!.Invoke(page, null);
            var bitrate = (ComboBox)page.FindName("MaxBitrateComboBox");
            Program.Log($"Unset bitrate: selected='{(bitrate.SelectedItem as ComboBoxItem)?.Content}', first tag type={((bitrate.Items[0] as ComboBoxItem)?.Tag)?.GetType().Name ?? "null"}");
            if (bitrate.SelectedItem is not ComboBoxItem { Content: "No limit" }) failures.Add("Unset bitrate has a blank select instead of No limit.");
            frame.Content = page;
            window.RestoreMainPane();
            await Task.Delay(120);
            if (!nav.IsPaneOpen || browsingState.BrowsingOpen)
            {
                failures.Add("Entering Settings from the compact rail must expand its navigation without changing the browsing preference.");
                Program.Log("FAIL reproduction: Settings entered with compact browsing preference; replacement navigation is not expanded.");
            }
            page.SetShellSidebarHosted(true);
            page.SelectSettingsSection("Playback");
            typeof(MainWindow).GetMethod("SynchronizeSettingsSidebar", instance)!.Invoke(window, null);
            await Task.Delay(160); root.UpdateLayout();
            var settingsVm = provider.GetRequiredService<SettingsViewModel>();
            Check(settingsVm.ConnectionSpeedSupported && settingsVm.BufferingQualitySupported,
                "Actual Settings page did not expose the fixture server's available Connection/buffering controls.");
            foreach (var width in new[] { 3440, 3840, 1920, 1280, 900, 600 })
            {
                window.AppWindow.ResizeClient(new(width, 1000));
                await Task.Delay(180); root.UpdateLayout();
                var content = (StackPanel)page.FindName("SettingsContentPanel");
                var playback = (StackPanel)page.FindName("PlaybackPanel");
                var group = playback.Children.OfType<Border>().First();
                Program.Log($"Viewport root={root.ActualWidth}, frame={frame.ActualWidth}, page={page.ActualWidth}, desired={page.DesiredSize}");
                Program.Log($"Settings width={width}: content={content.ActualWidth}, group={group.ActualWidth}, radius={group.CornerRadius}, padding={group.Padding}");
                foreach (var name in new[] { "SettingsPageShell", "SettingsLayoutGrid", "SettingsContentPanel", "PlaybackTitleText" })
                {
                    var element = (FrameworkElement)page.FindName(name);
                    var origin = element.TransformToVisual(page).TransformPoint(new());
                    Program.Log($"{name}: {element.ActualWidth}x{element.ActualHeight}, origin={origin.X},{origin.Y}");
                }
                if (group.CornerRadius.TopLeft != 0 || group.BorderThickness.Left != 0 || group.Padding.Left != 0)
                    failures.Add($"Settings {width}: section still draws a padded rounded card.");
                if (width == 1920 && content.ActualWidth < 1000)
                    failures.Add("Desktop Settings retains obsolete816px reading cap.");
                if (width == 1920 && content.TransformToVisual(page).TransformPoint(new()).X > 64)
                    failures.Add("Wide Settings centers a narrow desired size instead of anchoring the PageColumn at the page gutter.");
                if (width >= 1920)
                {
                    var shell = (FrameworkElement)page.FindName("SettingsPageShell");
                    var shellX = shell.TransformToVisual(page).TransformPoint(new()).X;
                    var contentX = content.TransformToVisual(page).TransformPoint(new()).X;
                    if (Math.Abs(shell.ActualWidth - 1560) > 1 || Math.Abs(content.ActualWidth - 1120) > 1
                        || Math.Abs(shellX - (page.ActualWidth - 1560) / 2) > 2 || Math.Abs(contentX - shellX) > 1)
                        failures.Add($"Settings {width}: centered1560px frame/left1120px column cap contract failed.");
                    var title = (TextBlock)page.FindName("PlaybackTitleText");
                    if (title.ActualHeight + .5 < title.LineHeight)
                        failures.Add($"Settings {width}: native page title clips its line box.");
                }
                var quality = (ComboBox)page.FindName("QualityComboBox");
                var intro = (ComboBox)page.FindName("IntroSkipModeCombo");
                Check(Descendants<TextBlock>(intro).Any(text => text.Text == "Ask to skip"), "Intro mode displays its wire value instead of the official label.");
                var recaps = (ToggleSwitch)page.FindName("AutoSkipRecapToggle");
                var credits = (ToggleSwitch)page.FindName("AutoSkipCreditsToggle");
                Check(recaps.TransformToVisual(page).TransformPoint(new()).Y < credits.TransformToVisual(page).TransformPoint(new()).Y,
                    "Skipping rows must put recaps before credits.");
                if (width >= 900 && Grid.GetRow(quality) != 0)
                    failures.Add("Settings rows stack above current768px breakpoint.");
                if (width == 600 && Math.Abs(quality.ActualWidth - content.ActualWidth) > 2)
                    failures.Add("Compact Settings select does not fill its stacked row.");
                await MediaParityNativeFixture.CaptureAsync(page, $"settings-playback-{width}.png");
            }
            window.AppWindow.ResizeClient(new(1280, 1000));
            await Task.Delay(150); root.UpdateLayout();
            var sidebar = Named<StackPanel>("SettingsSidebarHeader");
            Check(sidebar.Visibility == Visibility.Visible, "Personal Settings sidebar was not mounted in the actual shell.");
            var selectedNavigation = (NavigationViewItem)nav.SelectedItem;
            var navigationPeer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(selectedNavigation);
            Check(navigationPeer.GetName() == "Playback", "Settings navigation lost its accessible page name.");
            foreach (var header in nav.MenuItems.OfType<NavigationViewItemHeader>())
            {
                var paintedHeading = Descendants<TextBlock>(header).FirstOrDefault(text =>
                    text.Text is "PLAYBACK" or "APPEARANCE" or "HOME & DISCOVERY" or "CONNECTIONS" or "ACCOUNT");
                if (paintedHeading is null || paintedHeading.ActualWidth < 1 || paintedHeading.ActualHeight < 10
                    || paintedHeading.Visibility != Visibility.Visible || paintedHeading.Opacity < .9)
                    failures.Add("A Settings navigation group heading has no visible native text.");
            }
            var sidebarOrigin = sidebar.TransformToVisual(root).TransformPoint(new());
            Program.Log($"Settings sidebar header origin={sidebarOrigin.X},{sidebarOrigin.Y}; toggle={nav.IsPaneToggleButtonVisible}");
            if (sidebarOrigin.X > 16) failures.Add("Settings sidebar header retains the native hamburger inset and clips its search field.");
            if (Environment.GetEnvironmentVariable("SILO_SETTINGS_HEADER_DIAGNOSTIC") == "1")
            {
                nav.IsPaneToggleButtonVisible = false;
                await Task.Delay(120); root.UpdateLayout();
                var withoutToggle = sidebar.TransformToVisual(root).TransformPoint(new());
                Program.Log($"Diagnostic hidden toggle: header origin={withoutToggle.X},{withoutToggle.Y}");
            }
            var profile = Named<Button>("SettingsSidebarProfile");
            var search = Named<TextBox>("SettingsSidebarSearch");
            Check(profile.TransformToVisual(sidebar).TransformPoint(new()).Y < search.TransformToVisual(sidebar).TransformPoint(new()).Y,
                "Current profile must precede Settings search.");
            await MediaParityNativeFixture.CaptureAsync(root, "settings-shell-1280.png");
            foreach (var section in new[] { "Interface", "TitlePages", "Subtitles", "Accessibility", "Libraries", "HomeScreen" })
            {
                page.SelectSettingsSection(section);
                await Task.Delay(180); root.UpdateLayout();
                if (section == "TitlePages")
                {
                    var themeMusic = Descendants<ToggleSwitch>((DependencyObject)page.FindName("TitlePagesPanel"))
                        .Single(toggle => Equals(toggle.Header, "Theme music") || Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(toggle) == "Theme music");
                    Check(themeMusic.Parent is Grid && themeMusic.Header is null,
                        "Theme music still stacks its label above the toggle instead of using the current Settings row.");
                }
                if (section == "HomeScreen")
                {
                    var heading = Descendants<TextBlock>((DependencyObject)page.FindName("HomeScreenPanel")).First(text => text.Text == "Home screen");
                    if (heading.FontSize != 32) failures.Add("Home Screen ContentControl missed the shared current page-header layout.");
                    var home = (Border)page.FindName("HomePreferencesGroup");
                    if (home.Child is not Grid) failures.Add("Home Screen ContentControl missed shared section-column layout.");
                }
                if (section == "Interface")
                {
                    var radio = Descendants<RadioButton>((DependencyObject)page.FindName("InterfacePanel")).First(button => button.IsChecked == true);
                    radio.ApplyTemplate();
                    VisualStateManager.GoToState(radio, "Normal", false);
                    await Task.Delay(60);
                    var ring = Descendants<Microsoft.UI.Xaml.Shapes.Ellipse>(radio).Single(ellipse => ellipse.Name == "CheckOuterEllipse");
                    var dot = Descendants<Microsoft.UI.Xaml.Shapes.Ellipse>(radio).Single(ellipse => ellipse.Name == "CheckGlyph");
                    var expected = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]).Color;
                    if (ring.Stroke is not Microsoft.UI.Xaml.Media.SolidColorBrush stroke || stroke.Color != expected
                        || dot.Fill is not Microsoft.UI.Xaml.Media.SolidColorBrush fill || fill.Color != expected)
                        failures.Add("Settings checked radio ring/dot still use the Windows accent instead of current theme foreground.");
                }
                await MediaParityNativeFixture.CaptureAsync(page, $"settings-{section}-1280.png");
            }
            window.AppWindow.ResizeClient(new(600, 1000));
            await Task.Delay(120);
            typeof(SettingsPage).GetMethod("ShowSettingsOverview", instance)!.Invoke(page, null);
            await Task.Delay(120); root.UpdateLayout();
            var currentProfile = (Button)page.FindName("SettingsCurrentProfileButton");
            var indexSearch = (StackPanel)page.FindName("SettingsSearchPanel");
            Check(currentProfile.TransformToVisual(page).TransformPoint(new()).Y < indexSearch.TransformToVisual(page).TransformPoint(new()).Y,
                "Compact Settings index must put profile before search.");
            Check(Math.Abs(currentProfile.ActualWidth - ((FrameworkElement)page.FindName("SettingsPageShell")).ActualWidth) <= 1,
                "Compact current-profile tile must fill the directory width.");
            await MediaParityNativeFixture.CaptureAsync(page, "settings-index-600.png");
            Check(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Program.Log("PASS: actual compact/expanded profile state and responsive Settings section geometry.");
        }
        finally
        {
            if (window != null) { window.Close(); await Task.Delay(120); }
            mainProperty.SetValue(null, previousMain);
            servicesField.SetValue(null, previous);
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<double> PaintedAvatarDiameter(Button footer)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(footer);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var scale = bitmap.PixelWidth / footer.ActualWidth;
        var diameter = 0;
        for (var y = (int)(10 * scale); y < Math.Min(bitmap.PixelHeight, (int)(42 * scale)); y++)
        {
            var first = int.MaxValue; var last = -1;
            for (var x = (int)(6 * scale); x < Math.Min(bitmap.PixelWidth, (int)(38 * scale)); x++)
            {
                var pixel = (y * bitmap.PixelWidth + x) * 4;
                if (pixels[pixel] > 180 && pixels[pixel + 1] > 180 && pixels[pixel + 2] > 180)
                { first = Math.Min(first, x); last = x; }
            }
            if (last >= first) diameter = Math.Max(diameter, last - first + 1);
        }
        return diameter / scale;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class Wire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body = path switch
            {
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 17, supports_idempotent_writes = true, client_families = new[] { "desktop", "web" }, supports_batched_effective = true, supports_device_rename = true },
                "/api/v2/playback/capabilities" => new { api_version = 3, features = new[] { "buffering_quality_v1", "connection_speed_v1" }, bandwidth_probe_automatic = false },
                "/api/v2/playback/connection" => new { connection = "local", auto_budget_kbps = 80000,
                    probe = new { kbps = 100000, measured_at = DateTimeOffset.UtcNow, transport = "ethernet", lower_bound = false }, learned_limit = (object?)null },
                "/api/v2/settings/values/effective" => new { items = new object[] {
                    new { key = "catalog.hide_unwatched_episode_images", value = true, source = "profile" },
                    new { key = "catalog.hide_unwatched_episode_overviews", value = true, source = "profile" },
                    new { key = "playback.buffering_quality_mode", value = "always", source = "profile" } } },
                "/api/v2/auth/me" => new { id = "fixture", username = "Fixture", role = "user" },
                _ => new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}

