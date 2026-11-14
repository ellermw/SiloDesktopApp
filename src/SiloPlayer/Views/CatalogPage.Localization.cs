using SiloPlayer.Core.Localization;

namespace SiloPlayer.Views;

public sealed partial class CatalogPage
{
    private string? _titleOverride;
    private string? _titleKey;
    private Dictionary<string, string>? _titleArgs;
    private string? _keyedTitle;
    private string? _subtitleOverride;
    private bool _headingActive;

    private void RefreshCatalogHeading()
    {
        RefreshInlineChoices();
        PageTitleText.Text = _titleOverride != null
            ? SectionTitles.Display(_titleOverride, _titleKey, _titleArgs, _keyedTitle)
            : AppStrings.Get(_source switch
        {
            "favorites" => "web.catalog.page.favorites",
            "watchlist" => "web.catalog.page.watchlist",
            "history" => "web.catalog.page.history",
            _ => "web.catalog.page.catalog"
        });
        PageSubtitleText.Text = _subtitleOverride ?? AppStrings.Get(_source switch
        {
            "favorites" => "web.catalog.page.favoritesSubtitle",
            "watchlist" => "web.catalog.page.watchlistSubtitle",
            "history" => "web.catalog.page.historySubtitle",
            _ => "web.catalog.page.catalogSubtitle"
        });
        if (_headingActive) App.MainWindowInstance?.SetDynamicTitle(PageTitleText.Text);
    }

    private void RefreshInlineChoices()
    {
        var previous = _initializing;
        _initializing = true;
        try
        {
            SiloPlayer.Services.Localized.RefreshItems(TypeCombo);
            SiloPlayer.Services.Localized.RefreshItems(SortCombo);
            SiloPlayer.Services.Localized.RefreshItems(OrderCombo);
            SiloPlayer.Services.Localized.RefreshItems(WatchStatusCombo);
            SiloPlayer.Services.Localized.RefreshItems(AddedInLastCombo);
            SiloPlayer.Services.Localized.RefreshItems(ReleasedInLastCombo);
            foreach (var combo in new[] { AddedInLastCombo, ReleasedInLastCombo })
            {
                var selected = combo.SelectedItem;
                var index = combo.SelectedIndex;
                foreach (var item in combo.Items.OfType<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                {
                    if (item.Tag is not string span || span is not ("90d" or "1y" or "5y")) continue;
                    var unit = AppStrings.Get(span.EndsWith("d") ? "web.catalog.rules.unitDays" : "web.catalog.rules.unitYears");
                    // Unit-first labels avoid imposing English plural/word-order rules on catalog unit names.
                    item.Content = $"{unit}: {int.Parse(span[..^1], System.Globalization.CultureInfo.InvariantCulture).ToString(AppStrings.FormatCulture)}";
                }
                if (selected != null) { combo.SelectedIndex = -1; combo.SelectedIndex = index; }
            }
            foreach (var row in AdvancedRulesPanel.Children.OfType<Microsoft.UI.Xaml.Controls.Grid>())
                foreach (var combo in row.Children.OfType<Microsoft.UI.Xaml.Controls.ComboBox>())
                    SiloPlayer.Services.Localized.RefreshItems(combo);
        }
        finally { _initializing = previous; }
    }
}
