using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class ViewerLocalizationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var services = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = services.GetValue(null);
        var previousLocale = LocaleService.Shared.Current;
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://viewer-localization.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "settings"));
        using var provider = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(http)
            .AddSingleton(settings).AddSingleton<CatalogApi>().AddSingleton<PlaybackApi>().AddSingleton<PlayerService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "images")))
            .AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<LoginViewModel>().BuildServiceProvider();
        services.SetValue(null, provider);
        var host = new Grid { Width = 900, Height = 740 };
        parent.Children.Add(host);
        try
        {
            Apply("en");
            var page = new LoginPage();
            var login = page.ViewModel;
            var oauth = new AuthProvider { Id = "oidc", InstallationId = 41, DisplayName = "OpenID Connect", Mode = "oauth", IconUrl = "https://viewer-localization.invalid/icon.png" };
            login.OAuthProviders.Add(oauth);
            login.HasOAuthProviders = true;
            login.ProvidersReady = true;
            login.ShowPasswordForm = true;
            var network = new NetworkSignInOption(new AuthProvider { Id = "network", InstallationId = 42, DisplayName = "Network ID", Mode = "network", IconUrl = oauth.IconUrl });
            login.NetworkProviders.Add(network);
            login.HasNetworkProviders = true;
            host.Children.Add(page);
            await Settle(host);
            var button = All<Button>(page).Single(candidate => ReferenceEquals(candidate.Tag, oauth));
            var label = All<TextBlock>(button).Single();
            var icon = All<Image>(button).Single();
            var iconSource = icon.Source;
            var iconLoad = UrlToImageSourceConverter.GetLoadOutcome(iconSource);
            Require(iconLoad != null && await iconLoad.WaitAsync(TimeSpan.FromSeconds(5)), "synthetic provider icon decodes before label checks");
            var initialRequests = wire.Requests;
            foreach (var language in new[] { "en", "pl", "ru", "ko", "zh-Hant", "ar", "en" })
            {
                Apply(language);
                await Settle(host);
                var expected = language == "en" ? "Sign in with OpenID Connect"
                    : AppStrings.Get("signIn.withProvider", new Dictionary<string, object?> { ["provider"] = oauth.DisplayName });
                Require(label.Text == expected && expected != "signIn.withProvider", language + " OAuth visible label");
                Require(AutomationProperties.GetName(button) == expected, language + " OAuth explicit accessible name");
                Require(new ButtonAutomationPeer(button).GetName() == expected, language + " OAuth UIA peer name");
                Require(page.Language == language && page.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), language + " root font fallback/direction");
                Require(ReferenceEquals(icon, All<Image>(button).Single()) && ReferenceEquals(icon.Source, iconSource), language + " retained provider icon");
                Require(icon.Visibility == Visibility.Visible && icon.Width == 20 && icon.Height == 20, language + " provider icon geometry");
                Require(oauth.DisplayName == "OpenID Connect", language + " raw provider data unchanged");
                var networkButton = All<Button>(page).Single(candidate => ReferenceEquals(candidate.Tag, network.Provider));
                Require(All<TextBlock>(networkButton).Any(text => text.Text == network.Label), language + " unchanged network label contract");
                Require(ReferenceEquals(All<Image>(networkButton).Single().Source, iconSource), language + " unchanged network icon binding");
                RequireProviderFits(button, label, icon, language);
                if (language is "pl" or "ru" or "ko" or "zh-Hant") await Capture(host, "login-" + language + ".png");
            }
            host.Children.Clear();
            await Settle(host);
            Require(!page.IsLoaded && !button.IsLoaded, "actual provider page detaches before reload");
            Apply("pl");
            host.Children.Add(page);
            await Settle(host);
            RequireProviderText(button, label, "Zaloguj się przez usługę OpenID Connect", "detached page catches locale change on reload");
            var currentProvider = new AuthProvider { Id = "current-oidc", InstallationId = 43, DisplayName = "Current OpenID Connect", Mode = "oauth", IconUrl = oauth.IconUrl };
            button.DataContext = currentProvider;
            await Settle(host);
            Require(ReferenceEquals(button.Tag, currentProvider), "retained button selection uses the actual typed provider");
            Apply("ru");
            await Settle(host);
            RequireProviderText(button, label, "Войти через Current OpenID Connect", "retained locale refresh reads the current provider label");
            host.Children.Clear();
            await Settle(host);
            Require(!page.IsLoaded && !button.IsLoaded, "provider page actually unloads at the retained locale");
            currentProvider.DisplayName = "Reloaded OpenID Connect";
            host.Children.Add(page);
            await Settle(host);
            Require(page.IsLoaded && button.IsLoaded && ReferenceEquals(button.Tag, currentProvider), "reloaded button retains the current typed provider");
            RequireProviderText(button, label, "Войти через Reloaded OpenID Connect", "same-locale reload reads the current provider label");
            Require(oauth.DisplayName == "OpenID Connect", "retained selection never rewrites the previous provider model");
            Require(ReferenceEquals(icon.Source, iconSource), "retained selection and reload preserve provider icon binding");
            // Exercise full translated sentences at normal and enlarged accessibility text sizes.
            currentProvider.DisplayName = "Microsoft Entra ID";
            var originalFontSize = label.FontSize;
            foreach (var language in new[] { "fi", "pl" })
            foreach (var width in new[] { 900d, 448d })
            {
                host.Width = width;
                Apply(language);
                await Settle(host);
                var expected = AppStrings.Get("signIn.withProvider", new Dictionary<string, object?> { ["provider"] = currentProvider.DisplayName });
                RequireProviderText(button, label, expected, language + " long provider sentence");
                foreach (var fontSize in new[] { originalFontSize, originalFontSize * 2 })
                {
                    label.FontSize = fontSize;
                    await Settle(host);
                    RequireProviderFits(button, label, icon, $"{language} at width {width}, font {fontSize}");
                    if (fontSize > originalFontSize)
                        Require(label.ActualHeight > fontSize * 2, language + " enlarged sentence wraps to multiple lines");
                }
            }
            label.FontSize = originalFontSize;
            host.Width = 900;
            Require(wire.Requests == initialRequests, "locale refresh makes no network requests");
            host.Children.Clear();
            var hero = new NowListeningHero();
            host.Children.Add(hero);
            // After the #182 audiobook redesign an untitled chapter row uses the surviving
            // web.audiobooks.chapter.fallbackTitle key (the retired chapterNumber key is gone from rows).
            var row = new SiloPlayer.Core.Helpers.AudiobookDetailPresentation.ChapterRow(7, 6, "", 0d, 60d, 1);
            foreach (var language in new[] { "en", "pl", "ru", "ko" })
            {
                Apply(language);
                await Settle(host);
                var expectedChapter = AppStrings.Get("web.audiobooks.chapter.fallbackTitle", new Dictionary<string, object?> { ["number"] = 7 });
                Require(row.Label == expectedChapter && (language != "en" || row.Label == "Chapter 7"), language + " untitled chapter row uses the surviving localized chapter title");
                Require(((TextBlock)hero.FindName("EyebrowText")).Text == AppStrings.Uppercase(AppStrings.Get("web.audiobooks.hero.ariaLabel")), language + " actual hero uses surviving localized key");
            }
            host.Children.Clear();
            var player = provider.GetRequiredService<PlayerService>();
            // Seed playback state without starting a native media session.
            typeof(PlayerService).GetProperty(nameof(PlayerService.IsAudiobook))!.SetValue(player, true);
            typeof(PlayerService).GetProperty(nameof(PlayerService.Versions))!.SetValue(player, new List<FileVersion>
            {
                new() { FileId = 1, Duration = 420, Chapters = Enumerable.Range(0, 7).Select(index =>
                    new VersionChapter { Index = index, StartSeconds = index * 60, EndSeconds = (index + 1) * 60 }).ToList() }
            });
            typeof(PlayerService).GetProperty(nameof(PlayerService.Position))!.SetValue(player, 365d);
            typeof(PlayerService).GetProperty(nameof(PlayerService.Duration))!.SetValue(player, 420d);
            var listening = new AudiobookNowListening();
            host.Children.Add(listening);
            await Settle(host);
            try
            {
                listening.Activate();
                foreach (var (language, expected) in new[] { ("en", "CHAPTER 7"), ("de", "KAPITEL 7"), ("es", "CAPÍTULO 7"), ("en", "CHAPTER 7") })
                {
                    Apply(language);
                    await Settle(host);
                    Require(((TextBlock)listening.FindName("ChapterNumberText")).Text == expected,
                        language + " actual Now Listening chapter number stays localized");
                    Require(player.CurrentAudiobookChapter?.Index == 6 && player.Position == 365,
                        language + " locale change preserves the current playback chapter");
                }
            }
            finally { listening.Deactivate(); }
        }
        finally
        {
            parent.Children.Remove(host);
            services.SetValue(null, previousServices);
            LocaleService.Shared.Apply(previousLocale);
        }
    }

    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static void RequireProviderText(Button button, TextBlock label, string expected, string message)
    {
        Require(label.Text == expected, message + " visible text");
        Require(AutomationProperties.GetName(button) == expected, message + " explicit accessible name");
        Require(new ButtonAutomationPeer(button).GetName() == expected, message + " UIA peer name");
    }
    private static void RequireProviderFits(Button button, TextBlock label, Image icon, string message)
    {
        Require(label.TextWrapping == TextWrapping.Wrap && !label.IsTextTrimmed && label.ActualWidth > 0,
            message + " provider label wraps without trimming");
        var left = button.Padding.Left + button.BorderThickness.Left;
        var top = button.Padding.Top + button.BorderThickness.Top;
        var right = button.ActualWidth - button.Padding.Right - button.BorderThickness.Right;
        var bottom = button.ActualHeight - button.Padding.Bottom - button.BorderThickness.Bottom;
        foreach (var element in new FrameworkElement[] { label, icon })
        {
            var bounds = element.TransformToVisual(button).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            Require(bounds.Left >= left - 1 && bounds.Right <= right + 1 && bounds.Top >= top - 1 && bounds.Bottom <= bottom + 1,
                message + " rendered provider content stays inside button padding");
        }
        var labelBounds = label.TransformToVisual(button).TransformBounds(new Windows.Foundation.Rect(0, 0, label.ActualWidth, label.ActualHeight));
        var iconBounds = icon.TransformToVisual(button).TransformBounds(new Windows.Foundation.Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
        Require(labelBounds.Left >= iconBounds.Right + 7 || iconBounds.Left >= labelBounds.Right + 7,
            message + " provider icon and text do not overlap");
    }
    private static async Task Settle(FrameworkElement element) { await Task.Delay(150); element.UpdateLayout(); await Task.Delay(100); }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Program.Log("PASS: " + message);
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in All<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static async Task Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        Program.Log($"TRACE: capture {name} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath == "/icon.png" ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            {
                Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="))
            });
        }
    }
}
