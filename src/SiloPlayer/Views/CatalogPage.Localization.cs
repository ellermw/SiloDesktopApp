using SiloPlayer.Core.Localization;

namespace SiloPlayer.Views;

public sealed partial class CatalogPage
{
    private string? _titleOverride;
    private string? _subtitleOverride;
    private bool _headingActive;

    private void RefreshCatalogHeading()
    {
        PageTitleText.Text = _titleOverride ?? AppStrings.Get(_source switch
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
}
