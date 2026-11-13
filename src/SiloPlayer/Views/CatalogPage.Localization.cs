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
        }
        finally { _initializing = previous; }
    }
}
