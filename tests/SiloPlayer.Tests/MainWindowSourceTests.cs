namespace SiloPlayer.Tests;

public sealed class MainWindowSourceTests
{
    [Fact]
    public void SharedNavigationSuppressesStockSlidesAndWholePageOpacityFlashes()
    {
        var root = FindRepositoryRoot();
        var navigation = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "NavigationService.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var transition = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "PageTransitionHelper.cs"));

        Assert.Contains("new SuppressNavigationTransitionInfo()", navigation);
        Assert.Contains("_currentPageType == pageType", navigation);
        Assert.Contains("OnNavigated_AnimatePageEntrance", shell);
        Assert.Contains("PageTransitionHelper.AnimateEntrance", shell);
        Assert.Contains("content.Opacity = 1", transition);
        Assert.Contains("content.RenderTransform = null", transition);
        Assert.DoesNotContain("Storyboard", transition);
        Assert.DoesNotContain("DoubleAnimation", transition);
        Assert.DoesNotContain("TranslateY = 6", transition);
        Assert.DoesNotContain("ScaleX = 0.985", transition);
    }

    [Fact]
    public void PlayingNextOverlayShowsDedicatedCountdownBadge()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("PlayingNextBackdrop", xaml);
        Assert.Contains("PlayingNextCloseButton", xaml);
        Assert.Contains("PlayingNextCountdownPanel", xaml);
        Assert.Contains("PlayingNextAutoplayToggle", xaml);
        Assert.Contains("PlayingNextCountdownText", xaml);
        Assert.Contains("PlayingNextCountdownText.Text = $\"{_playingNextRemaining}s\";", code);
        Assert.Contains("PlayingNextCountdownRing.Value = _playingNextRemaining;", code);
        Assert.Contains("PlayingNextPlayNowText.Text = \"Play Now\";", code);
        Assert.Contains("PlayingNextOnDeckSection", xaml);
        Assert.Contains("PlayingNextOnDeckRepeater", xaml);
        Assert.Contains("PlayingNextOnDeckPrev", xaml);
        Assert.Contains("PlayingNextOnDeckNext", xaml);
        Assert.Contains("PlayingNextFinishedPanel", xaml);
        Assert.Contains("There are no more episodes available", xaml);
        Assert.Contains("LoadPlayingNextOnDeckAsync", code);
        Assert.Contains("section.SectionType, \"continue_watching\"", code);
        Assert.Contains("PlayingNextOnDeck_Click", code);
        Assert.Contains("playback.auto_play_next", code);
        Assert.Contains("GetPlayingNextAutoPlayAsync", code);
        Assert.Contains("PutDeviceSettingAsync", code);
        Assert.Contains("_playerService.IsPostRollVideoEnded", code);
        Assert.Contains("_playerService.EnterPostRollPreview();", code);
        Assert.Contains("_playerService.FinishPostRollPreview();", code);
        Assert.Contains("if (playbackHasEnded && hasNextEpisode && _playingNextAutoPlay)", code);
        Assert.Contains("PlayingNextFinishedHeading.Text", code);
        Assert.Contains("PlayingNextOnDeckScroller.ChangeView", code);
        Assert.Contains("x:Name=\"PlayingNextHero\"", xaml);
        Assert.Contains("UpdatePlayingNextLayout(e.Size.Width, e.Size.Height)", code);
        Assert.Contains("RefreshPlayingNextAutoPlayAsync(presentationGeneration)", code);
        Assert.Contains("The effective-setting request must never hold", code);
        Assert.Contains("_playerService.IsPostRollVideoEnded &&", code);
        Assert.Contains("PlayingNextOverlay.Visibility != Visibility.Visible", code);
    }

    [Fact]
    public void ApiClientSendsSiloDeviceHeadersForDeviceScopedSettings()
    {
        var root = FindRepositoryRoot();
        var client = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "SiloApiClient.cs"));
        var settingsApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "SettingsApi.cs"));
        var appSettings = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "ServerConfig.cs"));

        Assert.Contains("SetDeviceMetadata", client);
        Assert.Contains("X-Silo-Device-Id", client);
        Assert.Contains("X-Silo-Device-Name", client);
        Assert.Contains("X-Silo-Device-Platform", client);
        Assert.DoesNotContain("X-" + "Cont" + "inuum-Device-", client);
        Assert.Contains("GetEffectiveSettingsAsync", settingsApi);
        Assert.Contains("PutDeviceSettingAsync", settingsApi);
        Assert.Contains("DeviceId", appSettings);
    }

    [Fact]
    public void SidebarMatchesCurrentCapabilityAndCompactPresentationContracts()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"SiloWordmarkImage\"", xaml);
        Assert.Contains("x:Name=\"SiloMarkImage\"", xaml);
        Assert.Contains("x:Name=\"RequestsNavItem\"", xaml);
        Assert.Contains("x:Name=\"NotificationsNavItem\"", xaml);
        Assert.Contains("x:Name=\"NotificationUnreadBadge\"", xaml);
        Assert.Contains("IsPaneOpen=\"True\"", xaml);
        Assert.Contains("PaneClosing=\"NavView_PaneClosing\"", xaml);
        Assert.Contains("CompactPaneLength=\"64\"", xaml);
        Assert.DoesNotContain("Content=\"Catalog\" Tag=\"Catalog\"", xaml);
        Assert.Contains("RefreshUserNavigationCapabilitiesAsync", code);
        Assert.Contains("requestStatus.RequestsEnabled", code);
        Assert.Contains("capability.InApp.Enabled", code);
        Assert.Contains("NotificationUnreadBadge.Value = isOpen ? _notificationUnreadCount : -1", code);
        Assert.Contains("UpdateSidebarPanePresentation", code);
        Assert.Contains("args.Cancel = true", code);
        Assert.Contains("_routeWantsCompactPane = IsDetailPage(pageType)", code);
        Assert.Contains("ApplyResponsiveShellLayout", code);
        Assert.Contains("_currentWindowWidth < 1024", code);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftMinimal", code);
        Assert.Contains("NavView.IsPaneToggleButtonVisible = isNarrow", code);
        Assert.Contains("var shouldOpen = !isNarrow && !_routeWantsCompactPane", code);
        Assert.Contains("CanExposeAuthenticatedNavigation && !_routeWantsCompactPane", code);
        Assert.Contains("ContentFrame.Content is Views.Admin.AdminShellPage", code);
        Assert.Contains("AdminShell owns both its sidebar and ServerActivity button", code);
    }

    [Fact]
    public void CachedTopLevelPagesKeepPopulatedContentVisibleDuringRefresh()
    {
        var root = FindRepositoryRoot();
        var collectionsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs"));
        var downloadsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "DownloadsPage.xaml.cs"));
        var favoritesPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "FavoritesPage.xaml.cs"));
        var historyPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "HistoryPage.xaml.cs"));
        var watchlistPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "WatchlistPage.xaml.cs"));
        var collectionsVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "CollectionsViewModel.cs"));
        var downloadsVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "DownloadsViewModel.cs"));
        var favoritesVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "FavoritesViewModel.cs"));
        var historyVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "HistoryViewModel.cs"));
        var watchlistVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "WatchlistViewModel.cs"));

        Assert.Contains("NavigationCacheMode.Required", collectionsPage);
        Assert.Contains("NavigationCacheMode.Required", downloadsPage);
        Assert.Contains("IsLoading = Collections.Count == 0 && Groups.Count == 0", collectionsVm);
        Assert.Contains("IsLoading = Downloads.Count == 0", downloadsVm);
        Assert.Contains("_loadInProgress", collectionsVm);
        Assert.Contains("_loadInProgress", downloadsVm);
        Assert.Contains("QueueDownloadBuild", downloadsPage);
        Assert.Contains("NavigationCacheMode.Required", favoritesPage);
        Assert.Contains("NavigationCacheMode.Required", historyPage);
        Assert.Contains("NavigationCacheMode.Required", watchlistPage);
        Assert.Contains("IsLoading = Items.Count == 0", favoritesVm);
        Assert.Contains("IsLoading = Items.Count == 0", historyVm);
        Assert.Contains("IsLoading = Items.Count == 0", watchlistVm);
        Assert.Contains("QueueCardBuild", historyPage);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
