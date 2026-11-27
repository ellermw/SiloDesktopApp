using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// The detail page's studio and network links (official #5234), the audio tracks flyout leaving a format-only title out (#4589) and a
/// completed season announced as Watched (#5123), with synthetic data at 1440 and 560 wide. Selector: detail-studio-audio-season.
/// </summary>
internal static class DetailStudioAudioSeasonNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://studio-audio-season.invalid"); client.SetProfile("me");
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "studio-audio-season-images"));
        var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("me");
        var frame = new Frame();
        var navigation = new NavigationService { Frame = frame };
        slot.SetValue(null, new LocalServices(original, client, http, images, auth, navigation));
        var differences = new List<string>();
        try
        {
            var owner = new Grid(); owner.Children.Add(frame);
            var window = new Window { Content = owner };
            try
            {
                window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); window.AppWindow.Show(false); await Task.Delay(100);
                var scale = owner.XamlRoot.RasterizationScale;
                foreach (var width in new[] { 1440d, 560d })
                {
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(900 * scale))); await Task.Delay(100);
                    await StudioLinksAsync(owner, frame, width, differences);
                    await AudioFlyoutAsync(owner, width, differences);
                    await SeasonsAsync(owner, width, differences);
                }
            }
            finally { window.Close(); }
        }
        finally { navigation.Frame = null; slot.SetValue(null, original); }
        if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
        Program.Log("PASS: DETAIL_STUDIO_AUDIO_SEASON_COMPLETED studio and network links, the audio tracks flyout and the Watched season name at 1440 and 560 wide.");
    }

    private static ItemDetailPage NewPage(Grid owner, double width)
    {
        var page = new ItemDetailPage { Width = width, Height = 900 };
        owner.Children.Add(page);
        return page;
    }

    private static async Task Show(ItemDetailPage page, double width, string content)
    {
        Call(page, "UpdateUI"); Call(page, "ArrangeCurrentWebUiContentOrder", content);
        Call(page, "UpdateResponsiveLayout", width); page.UpdateLayout(); await Task.Delay(250); page.UpdateLayout();
    }

    // ----- The studio and the network ---------------------------------------------------------------------------------------

    private static async Task StudioLinksAsync(Grid owner, Frame frame, double width, List<string> differences)
    {
        var visits = new List<(Type Page, object? Parameter)>();
        void OnNavigating(object sender, NavigatingCancelEventArgs e) { visits.Add((e.SourcePageType, e.Parameter)); e.Cancel = true; }
        frame.Navigating += OnNavigating;
        try
        {
            foreach (var (type, label, studio, network, publisher, field, scope) in new (string, string, string?, string?, string?, string?, string?)[]
            {
                ("movie", "movie", "Marvel Studios", null, null, "studio", "movie"),
                ("series", "series", null, "Warner Bros. Television Distribution Entertainment Network", null, "network", "series"),
                ("ebook", "ebook", null, null, "Tor Books", null, null),
            })
            {
                var page = NewPage(owner, width);
                try
                {
                    page.ViewModel.Item = new MediaItemDetail
                    {
                        ContentId = type + "-fixture", Type = type, Title = type == "movie" ? "Thor: Ragnarok" : type == "series" ? "Foundation" : "The Way of Kings", Year = 2017,
                        Overview = "A short synthetic overview for the page.", UserState = new(),
                        Studios = studio is null ? [] : [studio], Networks = network is null ? [] : [network],
                        Ebook = publisher is null ? null : new() { Publisher = publisher },
                    };
                    await Show(page, width, type);
                    var link = (Button)page.FindName("StudioKickerLink");
                    var plain = (TextBlock)page.FindName("StudioKickerText");
                    var linkText = (TextBlock)page.FindName("StudioKickerLinkText");
                    var shown = studio ?? network ?? publisher!;
                    if (field is null)
                    {
                        if (link.Visibility != Visibility.Collapsed || plain.Visibility != Visibility.Visible || plain.Text != shown.ToUpperInvariant())
                            differences.Add($"{width}: an ebook's publisher is plain text with no link ({link.Visibility}, {plain.Visibility}, '{plain.Text}').");
                    }
                    else
                    {
                        if (link.Visibility != Visibility.Visible || plain.Visibility != Visibility.Collapsed || linkText.Text != shown.ToUpperInvariant())
                            differences.Add($"{width}: the {type}'s {field} must be a link with the eyebrow text ({link.Visibility}, {plain.Visibility}, '{linkText.Text}').");
                        if (AutomationProperties.GetName(link) != shown) differences.Add($"{width}: the {field} link is named '{AutomationProperties.GetName(link)}', expected '{shown}'.");
                        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(link);
                        if (peer.GetName() != shown || peer.GetPattern(PatternInterface.Invoke) is null) differences.Add($"{width}: the {field} link must expose its name and Invoke to assistive technology (name '{peer.GetName()}').");
                        if (!link.IsTabStop || Math.Abs(link.Opacity - 0.85) > 0.001) differences.Add($"{width}: the {field} link is a tab stop at 85% until hover (tab stop {link.IsTabStop}, opacity {link.Opacity}).");
                        var eyebrow = (FrameworkElement)page.FindName("HeroEyebrow");
                        if (eyebrow.Visibility != Visibility.Visible) differences.Add($"{width}: the eyebrow row must show.");
                        visits.Clear();
                        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke(); await Task.Delay(60);
                        // No library hosts the fixture page (the main window is absent), so the link opens a catalog query across the libraries.
                        if (visits.Count != 1 || visits[0].Page != typeof(CatalogPage) || visits[0].Parameter is not CatalogNavigation nav
                            || nav.FilterField != field || nav.FilterValue != shown || nav.Scope != scope || nav.Title != shown)
                            differences.Add($"{width}: the {field} link must open the catalog filtered to {field} '{shown}' for {scope}; it opened [{string.Join("|", visits.Select(v => v.Page.Name + ":" + v.Parameter))}].");
                    }
                    await MediaParityNativeFixture.CaptureAsync(owner, $"detail-{label}-eyebrow-{width:0}.png");
                    if (field is not null)
                    {
                        link.Opacity = 1; // what the pointer-enter handler sets
                        await MediaParityNativeFixture.CaptureAsync(owner, $"detail-{label}-eyebrow-hover-{width:0}.png");
                    }
                }
                finally { page.ViewModel.CancelPendingLoads(); owner.Children.Remove(page); }
            }
        }
        finally { frame.Navigating -= OnNavigating; }
    }

    // ----- The audio tracks flyout ------------------------------------------------------------------------------------------

    private static async Task AudioFlyoutAsync(Grid owner, double width, List<string> differences)
    {
        var page = NewPage(owner, width);
        try
        {
            var version = new FileVersion
            {
                FileId = 42, EffectiveAudioTrackIndex = 0,
                AudioTracks =
                [
                    new() { Language = "en", Title = "ATSC A/52B (AC-3, E-AC-3)", Codec = "ac3", Channels = 6, Layout = "5.1", Bitrate = 640, Default = true },
                    new() { Language = "ja", Title = "Director's commentary", Codec = "aac", Channels = 2, Layout = "stereo" },
                    new() { Language = "fr", Title = "AAC (Advanced Audio Coding)", Codec = "aac", Channels = 2, Layout = "stereo" },
                    new() { Language = "", Title = "1", Codec = "aac", Channels = 2, Layout = "stereo" },
                ],
            };
            page.ViewModel.Item = new MediaItemDetail { ContentId = "film", Type = "movie", Title = "Thor: Ragnarok", Year = 2017, UserState = new() };
            Set(page, "_selectedVersion", version);
            Set(page, "_watchDetail", new WatchDetailResponse { ContentId = "film", Versions = [version] });
            await Show(page, width, "movie");
            Call(page, "BuildAudioTracksFlyout", version);
            var panel = (StackPanel)page.FindName("AudioTracksFlyoutContent");
            var flyout = (Flyout)page.FindName("AudioTracksFlyout");
            flyout.ShowAt((FrameworkElement)page.FindName("AudioTracksButton")); await Task.Delay(200);
            var rows = panel.Children.OfType<Button>().ToArray();
            var all = rows.Select(row => string.Join("|", Texts(row))).ToArray();
            if (rows.Length != 5) differences.Add($"{width}: the flyout lists Auto and four tracks; it lists {rows.Length}.");
            else
            {
                if (all.Any(text => text.Contains("ATSC A/52B") || text.Contains("Advanced Audio Coding")))
                    differences.Add($"{width}: a title that only names the format must not show on a track row: [{string.Join(" // ", all)}].");
                if (!all[2].Contains("Director's commentary")) differences.Add($"{width}: a real title stays on its row: [{all[2]}].");
                if (!all[4].Contains("1")) differences.Add($"{width}: a title of numbers alone is a real title: [{all[4]}].");
                if (!all[1].StartsWith("English", StringComparison.Ordinal)) differences.Add($"{width}: the language still leads the row: [{all[1]}].");
            }
            await MediaParityNativeFixture.CaptureAsync(owner, $"detail-audio-flyout-{width:0}.png");
            await MediaParityNativeFixture.CaptureAsync(panel, $"detail-audio-flyout-rows-{width:0}.png");
            flyout.Hide();
        }
        finally { page.ViewModel.CancelPendingLoads(); owner.Children.Remove(page); }
    }

    // ----- Seasons ----------------------------------------------------------------------------------------------------------

    private static async Task SeasonsAsync(Grid owner, double width, List<string> differences)
    {
        var page = NewPage(owner, width);
        try
        {
            var vm = page.ViewModel;
            vm.Item = new MediaItemDetail { ContentId = "series-fixture", Type = "series", Title = "Foundation", EpisodeCount = 30, UserData = new() { UnplayedCount = 20 }, UserState = new(), PlayContentId = "ep-1" };
            vm.IsSeries = true; vm.SelectedSeasonNumber = 1;
            vm.Seasons.Add(new Season { ContentId = "s1", SeasonNumber = 1, Title = "Season 1", EpisodeCount = 10, UserData = new() { Played = true, WatchedCount = 10 } });
            vm.Seasons.Add(new Season { ContentId = "s2", SeasonNumber = 2, Title = "Season 2", EpisodeCount = 10, UserData = new() { WatchedCount = 3 } });
            vm.Seasons.Add(new Season { ContentId = "s3", SeasonNumber = 3, Title = "Season 3", EpisodeCount = 10, UserData = new() { UnplayedCount = 10 } });
            Set(page, "_currentContentId", vm.Item.ContentId);
            page.UpdateLayout(); await Task.Delay(100);
            ((UIElement)page.FindName("ContentScroll")).Visibility = Visibility.Visible;
            ((UIElement)page.FindName("SeasonsSection")).Visibility = Visibility.Visible;
            ((UIElement)page.FindName("SeasonsScrollViewer")).Visibility = Visibility.Visible;
            ((UIElement)page.FindName("SeasonsLoadingSkeleton")).Visibility = Visibility.Collapsed;
            Call(page, "BuildSeasonCards"); Call(page, "UpdateResponsiveLayout", width); page.UpdateLayout(); await Task.Delay(300); page.UpdateLayout();
            var cards = ((Panel)page.FindName("SeasonsPanel")).Children.OfType<Button>().ToArray();
            if (cards.Length != 3) { differences.Add($"{width}: expected three season cards, found {cards.Length}."); return; }
            var names = cards.Select(card => FrameworkElementAutomationPeer.CreatePeerForElement(card).GetName()).ToArray();
            if (!names[0].EndsWith(", Watched", StringComparison.Ordinal) || !names[0].StartsWith("Season 1, ", StringComparison.Ordinal))
                differences.Add($"{width}: the completed season must be announced as Watched; it is '{names[0]}'.");
            if (names[1].Contains("Watched") || names[2].Contains("Watched")) differences.Add($"{width}: a season not completed is not announced as Watched ('{names[1]}', '{names[2]}').");
            var badges = Descendants<Border>(cards[0]).Where(border => border.Child is FontIcon { Glyph: "" }).ToArray();
            if (badges.Length != 1) differences.Add($"{width}: the completed season has one check badge; found {badges.Length}.");
            else
            {
                var badge = FrameworkElementAutomationPeer.CreatePeerForElement(badges[0]);
                if (badge.GetName() != "Watched") differences.Add($"{width}: the check badge is named '{badge.GetName()}', expected 'Watched'.");
                if (AutomationProperties.GetAccessibilityView((FontIcon)badges[0].Child) != AccessibilityView.Raw) differences.Add($"{width}: the check glyph inside the badge is decoration (Raw view).");
            }
            if (Descendants<Border>(cards[1]).Any(border => border.Child is FontIcon { Glyph: "" })) differences.Add($"{width}: an unfinished season has no check badge.");
            await ScrollToAsync(page, (FrameworkElement)page.FindName("SeasonsSection"));
            await MediaParityNativeFixture.CaptureAsync(owner, $"detail-seasons-watched-{width:0}.png");
        }
        finally { page.ViewModel.CancelPendingLoads(); owner.Children.Remove(page); }
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Texts(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T own) yield return own;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }

    private static async Task ScrollToAsync(ItemDetailPage page, FrameworkElement target)
    {
        DependencyObject? node = (DependencyObject)page.FindName("DetailContentPanel");
        while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        if (node is not ScrollViewer scroller) throw new InvalidOperationException("The detail page has no scroller.");
        page.UpdateLayout();
        var offset = target.TransformToVisual((UIElement)scroller.Content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y - 24;
        scroller.ChangeView(null, Math.Max(0, offset), null, true);
        await Task.Delay(250); page.UpdateLayout();
    }

    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

    private static object? Call(object value, string name, params object[] args)
        => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);

    private sealed class LocalServices(IServiceProvider original, SiloApiClient client, HttpClient http, ImageService images, AuthService auth, NavigationService navigation) : IServiceProvider
    {
        public object? GetService(Type type)
            => type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((id, ct) => new CatalogApi(client).GetItemDetailAsync(id, ct)))
            : type == typeof(SiloApiClient) ? client : type == typeof(SettingsApi) ? new SettingsApi(client) : type == typeof(CatalogApi) ? new CatalogApi(client)
            : type == typeof(PlaybackApi) ? new PlaybackApi(client) : type == typeof(NavigationService) ? navigation
            : type == typeof(AuthApi) ? new AuthApi(client) : type == typeof(AuthService) ? auth
            : type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images : original.GetService(type);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/profiles") return Task.FromResult(Json("{\"items\":[{\"id\":\"me\",\"name\":\"Alex\"}]}"));
            // The page's own background reads (collections, similar, markers, ...) find nothing: this fixture checks what the page draws from the item it is given.
            if (request.Method == HttpMethod.Get) return Task.FromResult(Json("{\"items\":[],\"preview_items\":[],\"seasons\":[]}"));
            throw new InvalidOperationException("Unexpected detail studio / audio / season fixture request: " + request.Method + " " + path);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
