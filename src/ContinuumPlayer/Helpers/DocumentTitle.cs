using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Views;
using ContinuumPlayer.Views.Admin;

namespace ContinuumPlayer.Helpers;

/// <summary>
/// F7: Window title management. Maps the currently-navigated page type to a
/// human-readable label and composes a window title string in the webui
/// convention "<Label> · Continuum" (U+00B7 middle dot separator).
///
/// Wired in MainWindow via NavigationService.Navigated so every navigation
/// updates AppWindow.Title.
/// </summary>
public static class DocumentTitle
{
    public const string AppName = "Continuum";
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
        [typeof(CalendarPage)] = "Calendar",
        [typeof(RecommendationsPage)] = "Recommendations",
        [typeof(FavoritesPage)] = "Favorites",
        [typeof(WatchlistPage)] = "Watchlist",
        [typeof(HistoryPage)] = "History",
        [typeof(CollectionsPage)] = "Collections",
        [typeof(CollectionBrowsePage)] = "Collection",
        [typeof(CollectionEditorPage)] = "Edit Collection",
        [typeof(DownloadsPage)] = "Downloads",
        [typeof(LibraryPage)] = "Library",
        [typeof(ItemDetailPage)] = "Details",
        [typeof(PersonDetailPage)] = "Person",
        [typeof(SettingsPage)] = "Settings",
        [typeof(WatchTogetherJoinPage)] = "Watch Together",
        [typeof(WatchTogetherRoomPage)] = "Watch Together",

        // Auth / setup
        [typeof(LoginPage)] = "Sign In",
        [typeof(SignupPage)] = "Sign Up",
        [typeof(ServerSelectPage)] = "Select Server",
        [typeof(ProfileSelectPage)] = "Select Profile",
        [typeof(SetupWizardPage)] = "Setup",
        [typeof(ActivateDevicePage)] = "Activate Device",

        // Admin
        [typeof(AdminShellPage)] = "Admin",
        [typeof(AdminDashboardPage)] = "Admin · Dashboard",
        [typeof(AdminActivityPage)] = "Admin · Activity",
        [typeof(AdminTasksPage)] = "Admin · Tasks",
        [typeof(AdminTaskDetailPage)] = "Admin · Task",
        [typeof(AdminLogsPage)] = "Admin · Logs",
        [typeof(AdminNodesPage)] = "Admin · Nodes",
        [typeof(AdminPluginsPage)] = "Admin · Plugins",
        [typeof(AdminProvidersPage)] = "Admin · Providers",
        [typeof(AdminSubtitleProvidersPage)] = "Admin · Subtitle Providers",
        [typeof(AdminRecommendationsPage)] = "Admin · Recommendations",
        [typeof(AdminSectionsPage)] = "Admin · Home Sections",
        [typeof(AdminLibrariesPage)] = "Admin · Libraries",
        [typeof(AdminCollectionsPage)] = "Admin · Collections",
        [typeof(AdminMaintenancePage)] = "Admin · Catalog Maintenance",
        [typeof(AdminUsersPage)] = "Admin · Users",
        [typeof(AdminUserDetailPage)] = "Admin · User",
        [typeof(AdminApiKeysPage)] = "Admin · API Keys",
        [typeof(AdminInviteCodesPage)] = "Admin · Invite Codes",
        [typeof(AdminPlaybackHistoryPage)] = "Admin · Playback History",
        [typeof(AdminHistoryImportPage)] = "Admin · History Import",
        [typeof(AdminSettingsDetailPage)] = "Admin · Settings",
    };

    /// <summary>
    /// Compose the full window title for the given page type.
    /// Pages not in the label map fall back to just "Continuum".
    /// </summary>
    public static string FromPageType(Type pageType)
    {
        if (_pageLabels.TryGetValue(pageType, out var label))
            return $"{label}{Separator}{AppName}";
        return AppName;
    }

    /// <summary>
    /// Compose a window title from an explicit label (for dynamic pages that
    /// want to override with e.g. "Breaking Bad · Continuum").
    /// </summary>
    public static string FromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return AppName;
        return $"{label}{Separator}{AppName}";
    }
}
