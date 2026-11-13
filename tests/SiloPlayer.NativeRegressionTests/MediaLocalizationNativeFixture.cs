using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;

internal static partial class MediaLocalizationNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var before = LocaleService.Shared.Current;
        var raw = "Save فيلم · 東京";
        var model = new RecommendationsViewModel(SiloPlayer.App.Services.GetRequiredService<CatalogApi>(),
            new RecommendationsApi(SiloPlayer.App.Services.GetRequiredService<SiloApiClient>()));
        var mounted = new RecommendationRowDisplay { Label = raw, Type = "recommended", SectionKind = "genre", SectionKey = "42" };
        model.Rows.Add(mounted);
        var fresh = new RecommendationRowDisplay { Label = raw, Type = mounted.Type, SectionKind = mounted.SectionKind,
            SectionKey = mounted.SectionKey, TitleKey = "home.section.forYou", TitleArgs = new() { ["name"] = "Raw person" } };
        typeof(RecommendationsViewModel).GetMethod("ReconcileRows", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(model, new object[] { new[] { fresh } });
        Require(ReferenceEquals(model.Rows[0], mounted) && mounted.TitleKey == fresh.TitleKey
            && mounted.TitleArgs?["name"] == "Raw person", "retained recommendation discarded fresh title metadata");
        fresh.TitleArgs!["name"] = "changed";
        Require(mounted.TitleArgs!["name"] == "Raw person", "title arguments were not copied");
        var section = new HomeSectionWithItems { Id = "localized", Title = raw, TitleKey = "home.section.forYou", KeyedTitle = raw };
        var row = new SectionRow { Section = section, Width = 700, UseTitleViewLink = true, OnViewAll = () => { } };
        parent.Children.Add(row);
        try
        {
            Apply("de"); await Task.Delay(150); row.UpdateLayout();
            Require(((TextBlock)row.FindName("SectionTitle")).Text == AppStrings.Get("home.section.forYou"), "section title did not translate");
            Require(((TextBlock)row.FindName("TitleViewSuffix")).Text == AppStrings.Get("web.catalog.carousel.view"), "section action did not translate");
            Apply("ar"); await Task.Delay(150); row.UpdateLayout();
            Require(row.FlowDirection == FlowDirection.RightToLeft, "media row did not mirror navigation");
            Require(section.Title == raw, "locale change overwrote raw title");
            Program.Log("PASS: retained recommendation metadata, copied arguments, mounted section action and RTL preserve raw titles.");
            parent.Children.Remove(row);
            await VerifyOwnedPagesAsync(parent, raw, model);
            await VerifyDismissalMenusAsync(parent);
            await VerifyQuickActionLabelsAsync(parent);
            await VerifyCatalogHeadingAsync(raw);
        }
        finally
        {
            parent.Children.Remove(row);
            LocaleService.Shared.Apply(LocaleService.Shared.Create(before.Resolution));
        }
    }

    private static async Task VerifyQuickActionLabelsAsync(StackPanel parent)
    {
        static void Check(Button button, string key)
        {
            var expected = AppStrings.Get(key);
            Require(Equals(ToolTipService.GetToolTip(button), expected), "Quick-action tooltip lost its state: " + key);
            Require(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == expected,
                "Quick-action accessible name disagrees with its tooltip: " + key);
        }

        foreach (var type in new[] { "movie", "series" })
        {
            Apply("en");
            var poster = new PosterCard { SuppressImageLoading = true, DeferOverlayLoading = true };
            var landscape = new LandscapeCard();
            var library = new LibraryGridCard();
            var cards = new StackPanel();
            cards.Children.Add(poster); cards.Children.Add(landscape); cards.Children.Add(library);
            Button LibraryButton(string field) => (Button)typeof(LibraryGridCard)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(library)!;
            void Bind(bool active)
            {
                var item = new MediaItem { ContentId = "quick-action-localization", Title = "Raw فيلم · 東京", Type = type,
                    UserState = new() { Played = active, IsFavorite = active } };
                poster.MediaItem = item; landscape.MediaItem = item; library.Bind(item, null);
            }
            Bind(true);
            parent.Children.Add(cards);
            try
            {
                // Active on first Loaded, then recycle the same cards for the opposite state.
                foreach (var active in new[] { true, false })
                {
                    if (!active) Bind(false);
                    foreach (var language in new[] { "en", "de", "ar", "ja" })
                    {
                        Apply(language); await Task.Delay(150); cards.UpdateLayout();
                        Require(poster.IsLoaded && landscape.IsLoaded && library.IsLoaded, "Quick-action cards were not mounted");
                        var watchedKey = type == "series"
                            ? active ? "web.detail.actions.markSeriesUnwatched" : "web.detail.actions.markSeriesWatched"
                            : active ? "web.detail.actions.markUnwatched" : "web.detail.actions.markWatched";
                        var favoriteKey = active ? "web.detail.actions.removeFromFavorites" : "web.detail.actions.addToFavorites";
                        Check((Button)poster.FindName("QuickWatchedButton"), watchedKey);
                        Check((Button)poster.FindName("QuickFavoriteButton"), favoriteKey);
                        Check((Button)landscape.FindName("QuickWatchedButton"), watchedKey);
                        Check(LibraryButton("_quickWatchedButton"), watchedKey);
                        Check(LibraryButton("_quickFavoriteButton"), favoriteKey);
                    }
                }
            }
            finally { parent.Children.Remove(cards); await Task.Delay(40); }
        }
        Program.Log("PASS retained/recycled Poster, Landscape and Library quick-action tooltips and accessible names follow watched/favorite state through en/de/ar/ja.");
    }

    private static async Task VerifyDismissalMenusAsync(StackPanel parent)
    {
        foreach (var (type, surface, key) in new[] {
            ("movie", MediaItemMenu.Surface.ContinueWatching, "web.item.menu.removeFromContinueWatching"),
            ("audiobook", MediaItemMenu.Surface.ContinueWatching, "web.item.menu.removeFromContinueListening"),
            ("ebook", MediaItemMenu.Surface.ContinueWatching, "web.item.menu.removeFromContinueReading"),
            ("episode", MediaItemMenu.Surface.NextUp, "web.item.menu.removeFromNextUp") })
        {
            var anchor = new Button { Content = "Dismissal fixture" }; parent.Children.Add(anchor);
            var menu = MediaItemMenu.Build(new MediaItem { ContentId = "dismiss-fixture", Title = "Raw dismissal title", Type = type,
                SeriesId = "fixture-series", ProgressUpdatedAt = "2026-11-13T00:00:00Z" }, surface, showCollectionActions: false, owner: anchor);
            try
            {
                foreach (var language in new[] { "de", "ar", "ja" })
                {
                    Apply(language); menu.ShowAt(anchor); await Task.Delay(120);
                    Require(menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == AppStrings.Get(key) && Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(item) == item.Text), "Retained dismissal label did not translate: " + type);
                    menu.Hide();
                }
            }
            finally { menu.Hide(); parent.Children.Remove(anchor); }
        }
        Program.Log("PASS retained Continue Watching/Listening/Reading and Next Up dismissal menus through de/ar/ja.");
    }

    private static void Apply(string language) => LocaleService.Shared.Apply(LocaleService.Shared.Create(AppLocaleResolution.Resolve(language, ["en-US"], "en-US")));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static void CheckInlineChoiceLabels(FrameworkElement owner, params string[] names)
    {
        foreach (var name in names)
        {
            var combo = (ComboBox)owner.FindName(name);
            combo.ApplyTemplate();
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
                if (SiloPlayer.Services.Localized.GetContent(item) is { } key)
                    Require(Equals(item.Content, AppStrings.Get(key)), owner.GetType().Name + "." + name + ": unopened item did not translate: " + key);
            if (combo.SelectedItem is ComboBoxItem selected && SiloPlayer.Services.Localized.GetContent(selected) is { } selectedKey)
                Require(Equals(combo.SelectionBoxItem, AppStrings.Get(selectedKey)), owner.GetType().Name + "." + name + ": closed selection did not translate; actual=" + combo.SelectionBoxItem + "; expected=" + AppStrings.Get(selectedKey) + "; loaded=" + combo.IsLoaded + "; visibility=" + combo.Visibility + "; width=" + combo.ActualWidth);
        }
    }
}
