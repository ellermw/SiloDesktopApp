using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class MediaLocalizationNativeFixture
{
    internal static async Task RunCatalogAsync()
    {
        var before = LocaleService.Shared.Current;
        try { await VerifyCatalogHeadingAsync("Save فيلم · 東京"); }
        finally { LocaleService.Shared.Apply(LocaleService.Shared.Create(before.Resolution)); }
    }

    private static async Task VerifyCatalogHeadingAsync(string raw)
    {
        var servicesField = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var mainProperty = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previousServices = servicesField.GetValue(null);
        var previousWindow = mainProperty.GetValue(null);
        var wire = new CatalogHeadingWire();
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://catalog-heading.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "catalog-heading-settings"));
        var preferences = settings.Load(); preferences.SiloCastEnabled = false; settings.Save(preferences);
        await using var services = new ServiceCollection()
            .AddSingleton(api).AddSingleton(auth).AddSingleton(http).AddSingleton(settings)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<HomeApi>().AddSingleton<CollectionsApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>()
            .AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<WatchlistViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "catalog-heading-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, services);
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); mainProperty.SetValue(null, window);
            var originalRoot = (FrameworkElement)window.Content;
            var nav = (NavigationView)originalRoot.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate(typeof(RoutedEventHandler), window);
            var frame = new Frame(); window.Content = frame;
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1100, 800)); window.AppWindow.Show(false);
            foreach (var source in new[] { "favorites", "watchlist", "history" })
            {
                Apply("de");
                var route = new CatalogNavigation(Source: source);
                frame.Navigate(typeof(CatalogPage), route); await Task.Delay(250);
                var page = (CatalogPage)frame.Content; page.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
                await CheckHeadingAsync(page, window, source, null, null);
                Apply("ar"); await Task.Delay(120);
                await CheckHeadingAsync(page, window, source, null, null);
                frame.Navigate(typeof(Page)); window.AppWindow.Title = "other fixture page";
                frame.GoBack(); await Task.Delay(250);
                await CheckHeadingAsync((CatalogPage)frame.Content, window, source, null, null);
                Require(route.Title == null && route.Subtitle == null, "Built-in catalog persisted display headings into navigation");
            }
            Apply("de");
            var custom = new CatalogNavigation(Source: "favorites", Title: raw, Subtitle: raw + " raw subtitle");
            frame.Navigate(typeof(CatalogPage), custom); await Task.Delay(200);
            var customPage = (CatalogPage)frame.Content; customPage.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            Apply("ar"); await Task.Delay(120); await CheckHeadingAsync(customPage, window, "favorites", custom.Title, custom.Subtitle);
            frame.Navigate(typeof(Page)); frame.GoBack(); await Task.Delay(200);
            await CheckHeadingAsync((CatalogPage)frame.Content, window, "favorites", custom.Title, custom.Subtitle);
            Require(custom.Title == raw && custom.Subtitle == raw + " raw subtitle", "Explicit custom headings were rewritten");
            var keyed = new CatalogNavigation(Source: "section", Title: "Because you enjoy " + raw,
                SectionId: "localized-section", TitleKey: "home.section.becauseYouEnjoy",
                TitleArgs: new() { ["genres"] = raw }, KeyedTitle: "Because you enjoy " + raw);
            Apply("de"); frame.Navigate(typeof(CatalogPage), keyed); await Task.Delay(200);
            var keyedPage = (CatalogPage)frame.Content;
            keyedPage.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            foreach (var language in new[] { "de", "ar", "ja" })
            {
                Apply(language); await Task.Delay(120);
                var expectedTitle = SectionTitles.Display(keyed.Title!, keyed.TitleKey, keyed.TitleArgs, keyed.KeyedTitle);
                Require(expectedTitle != keyed.Title, "Section title fixture did not resolve its catalog key");
                await CheckHeadingAsync(keyedPage, window, "section", expectedTitle, null);
                var seasons = new List<SiloPlayer.Core.Models.Catalog.Season>
                {
                    new() { EpisodeCount = 6, UserData = new() { InProgressCount = 1, UnplayedCount = 5 } }
                };
                var legacyLabel = (string)typeof(ItemDetailPage).GetMethod("LegacySeriesActionLabel", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { seasons })!;
                Require(legacyLabel == AppStrings.Get("web.detail.actions.playNext") && legacyLabel != "Play Latest",
                    "Legacy progressed-series action did not use the active language");
            }
            frame.Navigate(typeof(Page)); frame.GoBack(); await Task.Delay(200);
            await CheckHeadingAsync((CatalogPage)frame.Content, window, "section",
                SectionTitles.Display(keyed.Title!, keyed.TitleKey, keyed.TitleArgs, keyed.KeyedTitle), null);
            var renamed = keyed with { Title = raw };
            frame.Navigate(typeof(CatalogPage), renamed); await Task.Delay(200);
            await CheckHeadingAsync((CatalogPage)frame.Content, window, "section", raw, null);
            Require(keyed.TitleArgs!["genres"] == raw, "Section navigation rewrote raw title arguments");
            Program.Log("PASS: keyed section catalog and window headings refresh de/ar/ja and Back; renamed titles stay raw; legacy progressed-series action translates.");
            wire.FailCollections = true;
            var dialog = new AddToCollectionDialog("fixture-title", raw) { XamlRoot = frame.XamlRoot };
            var pending = dialog.ShowAsync();
            try
            {
                var failure = (TextBlock)dialog.FindName("EmptyText");
                for (var attempt = 0; attempt < 200 && (failure.Visibility != Visibility.Visible ||
                    Localized.GetText(failure) != "web.collections.addToCollection.loadFailed"); attempt++) await Task.Delay(25);
                Require(failure.Visibility == Visibility.Visible && Localized.GetText(failure) == "web.collections.addToCollection.loadFailed", "Collection fixture did not reach its rejected-load state");
                foreach (var language in new[] { "de", "ar" })
                {
                    Apply(language); await Task.Delay(100);
                    Require(dialog.Title?.ToString() == AppStrings.Get("web.collections.addToCollection.title"), "Open collection dialog title did not refresh");
                    Require(Text(dialog, "EmptyText") == AppStrings.Get("web.collections.addToCollection.loadFailed"), "Open collection dialog failure did not refresh: language=" + language + "; actual=" + Text(dialog, "EmptyText") + "; expected=" + AppStrings.Get("web.collections.addToCollection.loadFailed") + "; loaded=" + failure.IsLoaded);
                    Require(dialog.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Open collection dialog direction did not refresh");
                }
            }
            finally { dialog.Hide(); await pending; }
            frame.Content = null;
            Program.Log("PASS: actual retained favorites/watchlist/history catalog title, subtitle and MainWindow title switch de/ar and Back; explicit mixed-direction custom headings remain raw.");
        }
        finally
        {
            window?.Close();
            mainProperty.SetValue(null, previousWindow); servicesField.SetValue(null, previousServices);
        }
    }

    private static async Task CheckHeadingAsync(CatalogPage page, MainWindow window, string source, string? rawTitle, string? rawSubtitle)
    {
        var headingSource = source is "favorites" or "watchlist" or "history" ? source : "catalog";
        var title = rawTitle ?? AppStrings.Get("web.catalog.page." + headingSource);
        var subtitle = rawSubtitle ?? AppStrings.Get("web.catalog.page." + headingSource + "Subtitle");
        Require(Text(page, "PageTitleText") == title, source + ": retained catalog title did not refresh");
        Require(Text(page, "PageSubtitleText") == subtitle, source + ": retained catalog subtitle did not refresh");
        Require(window.AppWindow.Title == DocumentTitle.FromLabel(title), source + ": actual window title did not refresh");
        if (source is not ("favorites" or "watchlist" or "history")) return;
        var sort = (ComboBox)page.FindName("SortCombo");
        sort.IsDropDownOpen = true; await Task.Delay(100);
        try
        {
            Require(sort.Items.OfType<ComboBoxItem>().Any(option => option.Tag is "" && option.Content?.ToString() ==
                AppStrings.Get(source == "history" ? "catalog.sort.added" : "web.catalog.sort.listOrder")), source + ": personal default sort option or raw order tag changed");
        }
        finally { sort.IsDropDownOpen = false; }
    }

    private sealed class CatalogHeadingWire : HttpMessageHandler
    {
        internal bool FailCollections;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "catalog-heading.invalid") throw new InvalidOperationException("External catalog fixture request refused");
            if (FailCollections && request.RequestUri.AbsolutePath.Contains("/collections", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"message\":\"Fixture collection failure\"}") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"items\":[],\"page\":{\"has_more\":false},\"settings\":{}}") });
        }
    }
}
