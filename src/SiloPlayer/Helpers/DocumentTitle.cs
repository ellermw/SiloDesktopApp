using SiloPlayer.Core.Localization;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Views;

namespace SiloPlayer.Helpers;

/// <summary>
/// F7: Window title management. Maps the currently-navigated page type to a
/// human-readable label and composes a window title string in the webui
/// convention "<Label> · Silo" (U+00B7 middle dot separator).
///
/// Wired in MainWindow via NavigationService.Navigated so every navigation
/// updates AppWindow.Title.
/// </summary>
public static class DocumentTitle
{
    public static string AppName { get; private set; } = "Silo";
    private const string Separator = " \u00B7 "; // " · "

    /// <summary>
    /// Map page Type → display label. Pages not in the map fall back to the
    /// bare <see cref="AppName"/>.
    /// </summary>
    private static readonly Dictionary<Type, string> _pageLabels = new()
    {
        // Shell / content
        [typeof(HomePage)] = "Home",
        [typeof(SearchPage)] = "Search",
        [typeof(CatalogPage)] = "Catalog",
        [typeof(CalendarPage)] = "Calendar",
        [typeof(RequestsPage)] = "Requests",
        [typeof(RequestDetailPage)] = "Request",
        [typeof(RequestBrowsePage)] = "Browse Requests",
        [typeof(EbookReaderPage)] = "Reader",
        [typeof(NotificationsPage)] = "Notifications",
        [typeof(RecommendationsPage)] = "Recommendations",
        [typeof(RecommendationSectionPage)] = "Recommendations",
        [typeof(FavoritesPage)] = "Favorites",
        [typeof(WatchlistPage)] = "Watchlist",
        [typeof(HistoryPage)] = "History",
        [typeof(CollectionsPage)] = "Collections",
        [typeof(CollectionBrowsePage)] = "Collection",
        [typeof(CollectionEditorPage)] = "Edit Collection",
        [typeof(SmartCollectionWizardPage)] = "New Collection",
        [typeof(DownloadsPage)] = "Downloads",
        [typeof(LibraryPage)] = "Library",
        [typeof(ItemDetailPage)] = "Details",
        [typeof(PersonDetailPage)] = "Person",
        [typeof(SettingsPage)] = "Settings",
        [typeof(TasteSeedPage)] = "Pick what you love",
        [typeof(WatchTogetherJoinPage)] = "Watch Party",
        [typeof(WatchTogetherRoomPage)] = "Watch Party",

        // Auth / setup
        [typeof(LoginPage)] = "Sign In",
        [typeof(SignupPage)] = "Sign Up",
        [typeof(ServerSelectPage)] = "Select Server",
        [typeof(ProfileSelectPage)] = "Select Profile",
        [typeof(ServerSetupRequiredPage)] = "Setup Required",
        [typeof(ActivateDevicePage)] = "Activate Device",
        [typeof(PasswordRecoveryPage)] = "Reset Password",
        [typeof(ChoosePasswordPage)] = "Choose a New Password",
        [typeof(InviteClaimPage)] = "Invitation",

    };

    private static readonly Dictionary<Type, string> PageKeys = new()
    {
        [typeof(HomePage)] = "web.shell.sidebar.home",
        [typeof(SearchPage)] = "web.shell.sidebar.search",
        [typeof(CatalogPage)] = "web.catalog.page.catalog",
        [typeof(CalendarPage)] = "web.shell.sidebar.calendar",
        [typeof(RequestsPage)] = "web.shell.sidebar.requests",
        [typeof(NotificationsPage)] = "web.notifications.inbox.title",
        [typeof(RecommendationsPage)] = "web.recommendations.fallbackDefault",
        [typeof(RecommendationSectionPage)] = "web.recommendations.fallbackDefault",
        [typeof(FavoritesPage)] = "web.shell.sidebar.favorites",
        [typeof(WatchlistPage)] = "web.shell.sidebar.watchlist",
        [typeof(HistoryPage)] = "web.shell.sidebar.history",
        [typeof(CollectionsPage)] = "web.shell.sidebar.collections",
        [typeof(DownloadsPage)] = "web.setup.features.downloads",
        [typeof(LibraryPage)] = "web.library.page.titleFallback",
        [typeof(PersonDetailPage)] = "web.person.fallbackTitle",
        [typeof(SettingsPage)] = "web.shell.sidebar.settings",
        [typeof(TasteSeedPage)] = "web.home.tasteSeed.title",
        [typeof(WatchTogetherJoinPage)] = "web.shell.sidebar.watchParty",
        [typeof(WatchTogetherRoomPage)] = "web.shell.sidebar.watchParty",
        [typeof(LoginPage)] = "web.auth.login.documentTitle",
        [typeof(ActivateDevicePage)] = "web.auth.activate.documentTitle",
        [typeof(ProfileSelectPage)] = "web.profiles.picker.documentTitle",
        [typeof(PasswordRecoveryPage)] = "web.auth.passwordReset.documentTitle",
        [typeof(ChoosePasswordPage)] = "web.auth.choosePassword.documentTitle",
    };

    public static void SetServerName(string? serverName)
    {
        AppName = string.IsNullOrWhiteSpace(serverName) ? "Silo" : serverName.Trim();
    }

    /// <summary>
    /// Compose the full window title for the given page type.
    /// Pages not in the label map fall back to just "Silo".
    /// </summary>
    public static string FromPageType(Type pageType)
    {
        if (_pageLabels.TryGetValue(pageType, out var label))
            return $"{(PageKeys.TryGetValue(pageType, out var key) ? AppStrings.Get(key, fallback: label) : label)}{Separator}{AppName}";
        return AppName;
    }

    /// <summary>
    /// Compose a window title from an explicit label (for dynamic pages that
    /// want to override with e.g. "Breaking Bad · Silo").
    /// </summary>
    public static string FromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return AppName;
        return $"{label}{Separator}{AppName}";
    }
}
