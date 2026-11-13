using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer;

public sealed partial class MainWindow
{
    private bool _dynamicShellTitle;

    private void RefreshShellLanguage()
    {
        if (!_dynamicShellTitle && ContentFrame.CurrentSourcePageType is { } page) AppWindow.Title = DocumentTitle.FromPageType(page);
        // NavigationView can relocate its header content. Refresh these owned
        // labels from the retained shell instead of depending on child lifetimes.
        ShellLibrariesLabel.Text = AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.libraries"));
        ShellDiscoverHeader.Content = AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.discover"));
        ShellYourStuffHeader.Content = AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.yourStuff"));
        AppsHeader.Content = AppStrings.Uppercase(AppStrings.Get("web.shell.sidebar.apps"));
        ShellThemeLabel.Text = AppStrings.Uppercase(AppStrings.Get("settingsManifest.ui.theme.label"));
        AppLocalization.ApplyDirection(ProfileMenuScroller);
        AutomationProperties.SetHelpText(LibrariesHeader, AppStrings.Get(
            _librariesExpanded ? "web.shell.sidebar.collapse" : "web.shell.sidebar.expand",
            new Dictionary<string, object?> { ["name"] = AppStrings.Get("web.shell.sidebar.libraries") },
            _librariesExpanded ? "Collapse libraries" : "Expand libraries"));
        QueueSidebarPublication();
        RebuildSettingsSidebar();
        RefreshUpdateDialog();
        if (PlayingNextOverlay.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            EnsurePlayingNextTextKeys();
            PlayingNextLabelText.Text = PlayingNextLabel(_playerService.HasNextEpisodeForCurrentPlayback);
            PlayingNextFinishedHeading.Text = PlayingNextFinishedHeadingText(_playerService.WatchDetail?.SeriesTitle);
            PlayingNextCountdownText.Text = PlayingNextCountdown(_playingNextRemaining);
            PlayingNextAutoplayToggleText.Text = PlayingNextAutoplayText(_playingNextAutoPlay);
        }
    }
}
