using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Views;
using SiloPlayer.Views.Admin;

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
        [typeof(SetupWizardPage)] = "Setup",
        [typeof(ActivateDevicePage)] = "Activate Device",

        // Admin
        [typeof(AdminShellPage)] = "Admin",
        [typeof(AdminRequestsPage)] = "Admin Requests",
        [typeof(AdminDashboardPage)] = "Admin · Dashboard",
        [typeof(AdminActivityPage)] = "Admin · Activity",
        [typeof(AdminTasksPage)] = "Admin · Tasks",
        [typeof(AdminTaskDetailPage)] = "Admin · Task",
        [typeof(AdminLogsPage)] = "Admin · Logs",
        [typeof(AdminNodesPage)] = "Admin · Nodes",
        [typeof(AdminPluginsPage)] = "Admin · Plugins",
        [typeof(AdminRecommendationsPage)] = "Admin · Recommendations",
        [typeof(AdminSectionsPage)] = "Admin · Home Sections",
        [typeof(AdminSubtitlesPage)] = "Admin · Subtitles",
        [typeof(AdminLibrariesPage)] = "Admin · Libraries",
        [typeof(AdminCollectionsPage)] = "Admin · Collections",
        [typeof(AdminMaintenancePage)] = "Admin · Catalog Maintenance",
        [typeof(AdminUsersPage)] = "Admin · Users",
        [typeof(AdminUserDetailPage)] = "Admin · User",
        [typeof(AdminAccessGroupsPage)] = "Admin · Access Groups",
        [typeof(AdminDevicesPage)] = "Admin · Devices",
        [typeof(AdminAutoscanPage)] = "Admin · Autoscan",
        [typeof(AdminPolicyPage)] = "Admin · Policy",
        [typeof(AdminApiKeysPage)] = "Admin · API Keys",
        [typeof(AdminInviteCodesPage)] = "Admin · Invite Codes",
        [typeof(AdminPlaybackHistoryPage)] = "Admin · Playback History",
        [typeof(AdminMarkerHistoryPage)] = "Admin · Marker History",
        [typeof(AdminHistoryImportPage)] = "Admin · History Import",
        [typeof(AdminSettingsDetailPage)] = "Admin · Settings",
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
            return $"{label}{Separator}{AppName}";
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
