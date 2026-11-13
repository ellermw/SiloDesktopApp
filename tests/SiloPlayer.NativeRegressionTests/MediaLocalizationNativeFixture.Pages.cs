using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class MediaLocalizationNativeFixture
{
    private static async Task VerifyOwnedPagesAsync(StackPanel parent, string raw, RecommendationsViewModel recommendationsModel)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)servicesField.GetValue(null)!;
        using var scoped = new LocalizedServices(original, recommendationsModel);
        servicesField.SetValue(null, scoped);
        var controls = new List<FrameworkElement>();
        var previousSort = PosterCard.CurrentSortKey;
        var stage = new Grid { Width = 1100, Height = 760 };
        parent.Children.Add(stage);
        try
        {
            var home = new HomePage { Width = 1100, Height = 760 };
            var library = new LibraryPage { Width = 1100, Height = 760 };
            var detail = new ItemDetailPage { Width = 1100, Height = 760 };
            var bookDetail = new ItemDetailPage { Width = 1100, Height = 760 };
            var requestDetail = new RequestDetailPage { Width = 1100, Height = 760 };
            var search = new SearchPage { Width = 1100, Height = 760 };
            var calendar = new CalendarPage { Width = 1100, Height = 760 };
            var requests = new RequestsPage { Width = 1100, Height = 760 };
            var recommendations = new RecommendationsPage { Width = 1100, Height = 760 };
            var seriesFilters = new NotificationSeriesControl();
            var favorites = new FavoritesPage { Width = 1100, Height = 760 };
            var watchlist = new WatchlistPage { Width = 1100, Height = 760 };
            var browse = new RequestBrowsePage { Width = 1100, Height = 760 };
            var subtitleCard = new PosterCard { Width = 185, MediaItem = new MediaItem { ContentId = "subtitle-raw", Title = raw, Type = "series" } };
            var hero = new HeroCarousel { Width = 760, Height = 430, ItemsSource = new List<MediaItem>
            { new() { ContentId = "raw-title", Title = raw, Type = "audiobook", UserState = new() { Played = true } } } };
            controls.AddRange([home, library, detail, bookDetail, requestDetail, search, calendar, requests, recommendations, browse, hero, subtitleCard, seriesFilters, favorites, watchlist]);
            foreach (var control in controls) stage.Children.Add(control);
            library.ViewModel.Library = new Library { Id = 7001, Name = raw, Type = "movie" };
            Invoke(library, "UpdateShuffleHeader");
            scoped.Search.Query = raw;
            Invoke(search, "UpdateResultsState");
            Set(browse, "_sectionTitle", scoped.Section);
            var browseState = Get<RequestBrowseSession>(browse, "_browse");
            await browseState.ResetAsync((_, _) => Task.FromResult((new DiscoverBrowseResponse { DisplayName = raw }, (int?)null)));
            await Task.Delay(180);
            Invoke(detail, "BuildCast", new List<CastMember> { new() { Name = raw } });
            Invoke(detail, "UpdatePlayButtonFromItemData", new MediaItemDetail { ContentId = "detail-raw", Title = raw, Type = "episode" });
            Invoke(bookDetail, "UpdatePlayButtonFromItemData", new MediaItemDetail { ContentId = "book-raw", Title = raw, Type = "ebook" });
            var requested = new RequestMediaDetail { TmdbId = 31, MediaType = "series", Title = raw, VoteAverage = 8, VoteCount = 1234, NumberOfSeasons = 2 };
            Set(requestDetail, "_item", requested); Invoke(requestDetail, "Render", requested);
            Invoke(recommendations, "BuildRows");
            var retainedRecommendation = ((StackPanel)recommendations.FindName("RowsPanel")).Children.OfType<SectionRow>().Single();
            var updated = new RecommendationRowDisplay { Label = raw + " updated", Type = recommendationsModel.Rows[0].Type, SectionKind = recommendationsModel.Rows[0].SectionKind, SectionKey = recommendationsModel.Rows[0].SectionKey, TitleKey = "home.section.becauseYouEnjoy", TitleArgs = new() { ["genres"] = raw } };
            Invoke(recommendationsModel, "ReconcileRows", new object[] { new[] { updated } });
            await Task.Delay(160);
            Require(ReferenceEquals(retainedRecommendation, ((StackPanel)recommendations.FindName("RowsPanel")).Children.OfType<SectionRow>().Single()), "Mounted recommendation row was replaced");
            Require(retainedRecommendation.Section!.Title == updated.Label && retainedRecommendation.Section.TitleKey == updated.TitleKey && retainedRecommendation.Section.TitleArgs?["genres"] == raw, "Mounted recommendation skipped fresh title metadata");
            var menuItem = new MediaItem { ContentId = "menu-raw", Title = raw, Type = "movie", UserState = new() };
            var menu = MediaItemMenu.Build(menuItem, showCollectionActions: false, owner: hero);
            var anchor = new Button { Content = "fixture menu", Flyout = menu, Width = 180 };
            controls.Add(anchor); stage.Children.Add(anchor);
            for (var pass = 0; pass < 3; pass++)
            {
                var language = new[] { "de", "ar", "ja" }[pass];
                PosterCard.CurrentSortKey = "content_rating";
                Apply(language); await Task.Delay(200);
                foreach (var control in controls) { control.Measure(new Windows.Foundation.Size(1100, 760)); control.Arrange(new Windows.Foundation.Rect(0, 0, 1100, 760)); control.UpdateLayout(); }
                CheckInlineChoiceLabels(seriesFilters, "ReasonCombo", "SortCombo");
                CheckInlineChoiceLabels(favorites, "SortComboBox");
                CheckInlineChoiceLabels(watchlist, "WatchlistScope", "SortComboBox");
                CheckInlineChoiceLabels(search, "ResultTypeCombo", "ResultSortCombo", "ResultOrderCombo");
                CheckInlineChoiceLabels(library, "OrderComboBox", "MediaTypeComboBox", "MatchStatusComboBox", "WatchStatusComboBox");
                CheckInlineChoiceLabels(browse, "SortCombo");
                Require(Text(home, "TasteSeedTitle") == AppStrings.Get("web.home.tasteSeed.bannerTitle"), "Home retained title did not refresh");
                var shuffle = (Button)library.FindName("LibraryShuffleButton");
                Require(shuffle.Content is StackPanel && Descendants(shuffle).OfType<TextBlock>().Any(text => text.Text == AppStrings.Get("web.library.header.shuffle")), "Library retained templated shuffle action did not refresh");
                Require(Text(retainedRecommendation, "SectionTitle") == AppStrings.ServerText(updated.TitleKey, updated.TitleArgs, updated.Label), "Mounted recommendation retained a stale title after language switch");
                Require(Text(subtitleCard, "SubtitleText") == AppStrings.Get("web.item.card.series"), "Retained card fallback used a raw English series label");
                Require(Text(calendar, "CalendarTitle") == AppStrings.Get("web.calendar.title"), "Calendar retained title did not refresh");
                Require(Text(search, "ResultsTitle").Contains(raw, StringComparison.Ordinal), "Search localized query label lost raw data");
                Require(Text(requests, "PageTitleText") == AppStrings.Get("web.requests.title"), "Requests retained title did not refresh");
                Require(Descendants(requests).OfType<TextBlock>().Any(text => text.Text == scoped.Row.DisplayTitle), "Requests Discover title metadata did not refresh");
                Require(Text(browse, "TitleText") == scoped.Section.DisplayTitle, "Discover browse title metadata did not refresh");
                Require(Text(detail, "CastHeader") == AppStrings.Get("web.detail.castCrew.cast"), "Detail retained section text did not refresh");
                Require(Text(detail, "PlayButtonText") == AppStrings.Get("web.detail.actions.playEpisode"), "Detail retained primary action did not refresh");
                Require(Text(bookDetail, "PlayButtonText") == AppStrings.Get("web.detail.books.read"), "Detail retained reader action did not refresh");
                var votes = AppStrings.Get("web.detail.overview.tmdbVotes", new Dictionary<string, object?> { ["count"] = 1234, ["count_text"] = 1234.ToString("N0", AppStrings.FormatCulture) });
                Require(Descendants(requestDetail).OfType<TextBlock>().Any(text => text.Text == votes), "Request detail retained votes did not refresh");
                var heroAction = (TextBlock)hero.FindName("PlayButtonText");
                Require(heroAction.Text == AppStrings.Get("web.home.hero.listenAgain"), "Hero retained action did not refresh");
                Require(heroAction.ActualWidth > 0 && heroAction.ActualHeight > 0 && heroAction.DesiredSize.Width <= hero.ActualWidth && heroAction.DesiredSize.Height <= hero.ActualHeight, "Long translated hero action collapsed");
                Require(home.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Home direction did not refresh");
                Require(Descendants(hero).OfType<Image>().Any() && Descendants(hero).OfType<Image>().All(image => image.FlowDirection == FlowDirection.LeftToRight), "Hero artwork inherited RTL mirroring");
                menu.ShowAt(anchor); await Task.Delay(80);
                Require(menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == MediaItemCardActions.GetWatchedActionLabel("movie", false)), "Opened retained media menu did not localize");
                Require(menu.Items.OfType<MenuFlyoutItem>().All(item => item.FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)), "Detached menu direction did not refresh");
                Require(menu.Items.OfType<MenuFlyoutItem>().All(item => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(item) == item.Text), "Retained menu accessible names diverged from translated actions");
                menu.Hide();
                Require(scoped.Section.Title == raw && scoped.Row.Title == raw && hero.ItemsSource![0].Title == raw && menuItem.Title == raw, "A display path overwrote a raw title");
            }
            var wire = JsonSerializer.Serialize(scoped.Section, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var persistence = Path.Combine(Program.ResultDirectory, "raw-discover-title.json");
            await File.WriteAllTextAsync(persistence, wire);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(persistence));
            Require(saved.RootElement.GetProperty("title").GetString() == raw && !saved.RootElement.TryGetProperty("display_title", out _), "Persisted discovery title was localized");
            Program.Log("PASS: actual Home/library/detail/search/calendar/Requests/Discover/hero/open-menu retained German, Arabic and Japanese controls; artwork, mixed titles and raw persistence.");
        }
        finally
        {
            stage.Children.Clear();
            parent.Children.Remove(stage);
            PosterCard.CurrentSortKey = previousSort;
            servicesField.SetValue(null, original);
        }
    }

    private static string Text(FrameworkElement owner, string name) => ((TextBlock)owner.FindName(name)).Text;
    private static object? Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class LocalizedServices : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider _original;
        private readonly EventChannelClient _events;
        private readonly Dictionary<Type, object> _services = [];
        internal SearchViewModel Search { get; }
        internal RequestDiscoverySection Section { get; }
        internal RequestDiscoverRow Row { get; }
        internal LocalizedServices(IServiceProvider original, RecommendationsViewModel recommendations)
        {
            _original = original;
            var api = original.GetRequiredService<SiloApiClient>();
            var catalog = original.GetRequiredService<CatalogApi>();
            var auth = original.GetRequiredService<AuthService>();
            var settings = new SettingsApi(api);
            var requests = new RequestsApi(api);
            _events = new(api, auth);
            var home = new HomeViewModel(new HomeApi(api), auth, settings);
            Set(home, "_hasLoadedLayout", true); Set(home, "_lastLoadedAt", DateTime.UtcNow);
            Set(home, "_loadedContext", api.CaptureContext()); Set(home, "_loadedProfileId", auth.SelectedProfileId);
            var calendar = new CalendarViewModel(catalog, original.GetRequiredService<SettingsService>()) { HasLoaded = true };
            Set(calendar, "_context", catalog.CaptureContext());
            var requestsModel = new RequestsViewModel(requests) { RequestsEnabled = true };
            Set(requestsModel, "_loadedContext", requests.CaptureContext());
            Set(requestsModel, "_lastLoadedAt", DateTime.UtcNow); requestsModel.DataVersion = 1;
            const string rawTitle = "Save فيلم · 東京";
            Section = new() { Key = "localized", Title = rawTitle, TitleKey = "home.section.forYou", Results = [new() { MediaType = "movie", TmdbId = 20, Title = rawTitle }] };
            Row = new() { Key = Section.Key, Type = "tmdb_chart", Title = rawTitle, TitleKey = Section.TitleKey };
            requestsModel.DiscoverySections.Add(Section); requestsModel.DiscoveryRows.Add(Row);
            Search = new(catalog, new PeopleApi(api), requests, settings);
            Set(recommendations, "_loadedContext", catalog.CaptureContext()); Set(recommendations, "_lastLoadedAt", DateTime.UtcNow); Set(recommendations, "_hasLoaded", true);
            _services[typeof(RecommendationsViewModel)] = recommendations;
            _services[typeof(HomeViewModel)] = home; _services[typeof(LibraryViewModel)] = new LibraryViewModel(catalog);
            _services[typeof(SearchViewModel)] = Search; _services[typeof(RequestsViewModel)] = requestsModel;
            _services[typeof(CalendarViewModel)] = calendar; _services[typeof(EventChannelClient)] = _events;
            _services[typeof(SettingsApi)] = settings; _services[typeof(RequestsApi)] = requests;
            _services[typeof(NotificationsApi)] = new NotificationsApi(api);
            _services[typeof(FavoritesViewModel)] = new FavoritesViewModel(catalog);
            _services[typeof(WatchlistViewModel)] = new WatchlistViewModel(catalog, requests);
            _services[typeof(NavigationService)] = new NavigationService();
        }
        public object? GetService(Type type) => _services.GetValueOrDefault(type) ?? _original.GetService(type);
        public void Dispose() => _events.Dispose();
    }
}
