using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class SettingsLocalizationNativeFixture
{
    private static async Task ReviewTwelfth(StackPanel parent)
    {
        using var wire = new ReviewWatchlistFailureWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://watchlist-failure.fixture.invalid");
        var favorites = new FavoritesViewModel(new CatalogApi(client));
        await favorites.LoadCommand.ExecuteAsync(null);
        var reads = wire.Reads;
        var rules = new[] { "en", "ja", "unknown-raw-code" }.Select(code => new QueryRule { Field = "original_language", Op = "is", Value = code }).ToList();
        var group = new QueryGroup { Match = "all", Rules = rules };
        var badges = rules.Select(rule => CatalogFilterBadgeView.Build(new CatalogFilterBadges.Badge("raw fallback", group, [rule]), null, () => { })).ToArray();
        var page = new SettingsPage(); parent.Children.Add(page);
        foreach (var badge in badges) parent.Children.Add(badge);
        try
        {
            page.SelectSettingsSection("Playback"); await Settle(page);
            var panel = (DependencyObject)page.FindName("PlaybackPanel");
            var combos = Descendants(panel).OfType<ComboBox>().Where(combo => combo.ItemsSource is int[] values && values.SequenceEqual(SeekPreferences.Choices)).ToArray();
            Check(combos.Length == 4, "Expected four actual seek interval selectors.");
            foreach (var combo in combos) { combo.IsEnabled = true; combo.SelectedItem = 10; }
            foreach (var language in new[] { "de", "ar", "ja", "en" })
            {
                Apply(language); Invoke(favorites, "RefreshLocalization"); await Settle(page);
                var prefix = AppStrings.Get("web.common.ui.carousel.loadFailed", new Dictionary<string, object?> { ["title_lower"] = AppStrings.Get("web.catalog.page.favorites") });
                Check(favorites.ErrorMessage == prefix + " " + (language == "en" ? "Raw service failure" : AppStrings.Get("error.dependency_unavailable")), "Favorites failure did not translate completely: " + favorites.ErrorMessage);
                Check(wire.Reads == reads, "Favorites language change triggered another API read.");
                for (var index = 0; index < badges.Length; index++)
                {
                    var code = (string)rules[index].Value!;
                    var name = AppStrings.Get("language." + code, fallback: code);
                    var expected = AppStrings.Get("web.catalog.filters.badgeLanguage", new Dictionary<string, object?> { ["language"] = name });
                    Check(Descendants(badges[index]).OfType<TextBlock>().Any(text => text.Text == expected), "Language badge lost its name/raw fallback: " + code);
                    Check(Equals(rules[index].Value, code), "Badge mutated its wire code.");
                }
                foreach (var combo in combos)
                {
                    var expected = AppStrings.Get("web.settings.playback.seek.seconds", new Dictionary<string, object?> { ["count"] = 10, ["count_text"] = 10.ToString(AppStrings.FormatCulture) });
                    Check(Equals(combo.SelectedItem, 10), "Seek selection changed during locale refresh.");
                    Check(Descendants(combo).OfType<TextBlock>().Any(text => text.Text == expected), "Closed seek selector lost its localized seconds unit: " + language + "; loaded=" + combo.IsLoaded + "; size=" + combo.ActualWidth + "x" + combo.ActualHeight + "; template=" + (combo.SelectionBoxItemTemplate != null) + "; labels=" + string.Join("|", Descendants(combo).OfType<TextBlock>().Select(text => text.Text + " [" + text.DataContext + ";" + text.IsLoaded + "]")));
                    combo.IsDropDownOpen = true; await Settle(combo);
                    try
                    {
                        foreach (var seconds in SeekPreferences.Choices)
                        {
                            var container = combo.ContainerFromItem(seconds) as ComboBoxItem;
                            var label = AppStrings.Get("web.settings.playback.seek.seconds", new Dictionary<string, object?> { ["count"] = seconds, ["count_text"] = seconds.ToString(AppStrings.FormatCulture) });
                            Check(container != null && Descendants(container).OfType<TextBlock>().Any(text => text.Text == label), "Seek option lacks seconds: " + seconds + "/" + language);
                        }
                    }
                    finally { combo.IsDropDownOpen = false; }
                }
            }
        }
        finally { foreach (var badge in badges) parent.Children.Remove(badge); parent.Children.Remove(page); Apply("en"); await Settle(parent); }
        Program.Log("PASS twelfth review: retained Favorites API failure and language badges localize en/de/ar/ja without wire changes; all four seek selectors retain localized seconds and numeric values.");
    }
}
