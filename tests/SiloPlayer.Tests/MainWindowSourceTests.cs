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
        Assert.Contains("Canvas.ZIndex=\"10\"", xaml);
        Assert.Contains("PointerPressed=\"PlayingNextClose_PointerPressed\"", xaml);
        Assert.Contains("Click=\"PlayingNextCancel_Click\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Close Playing Next\"", xaml);
        Assert.Contains("PlayingNextClose_PointerPressed(", code);
        Assert.Contains("Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)", code);
        Assert.Contains("Properties.IsLeftButtonPressed", code);
        Assert.Contains("PlayingNextCancel_Click(object sender, RoutedEventArgs e)", code);
        Assert.Contains("=> DismissPlayingNext();", code);
        Assert.Contains("_playerService.CancelPlayingNext();", code);
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
        Assert.True(
            code.IndexOf("_playerService.EnterPostRollPreview();", StringComparison.Ordinal) <
            code.IndexOf("PlayingNextOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal),
            "The native mpv HWND must move before the WinUI Playing Next surface is exposed.");
        Assert.Contains("if (playbackHasEnded && hasNextEpisode && _playingNextAutoPlay)", code);
        Assert.Contains("PlayingNextFinishedHeading.Text", code);
        Assert.Contains("PlayingNextOnDeckScroller.ChangeView", code);
        Assert.Contains("x:Name=\"PlayingNextHero\"", xaml);
        Assert.Contains("UpdatePlayingNextLayout(e.Size.Width, e.Size.Height)", code);
        Assert.Contains("RefreshPlayingNextAutoPlayAsync(presentationGeneration)", code);
        Assert.Contains("The effective-setting request must never hold", code);
        Assert.Contains("_playerService.IsPostRollVideoEnded &&", code);
        Assert.Contains("PlayingNextOverlay.Visibility != Visibility.Visible", code);
        Assert.Contains("this.Activated += OnWindowActivated;", code);
        Assert.Contains("var shouldFocusOverlay = _isWindowActive || _playerService.IsPlaybackSurfaceForeground;", code);
        Assert.Contains("if (shouldFocusOverlay)", code);
        Assert.Contains("Never request focus from the background", code);
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
        Assert.Contains("Background=\"{StaticResource AccentBrush}\"", xaml);
        Assert.Contains("Foreground=\"{StaticResource AccentForegroundBrush}\"", xaml);
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
        Assert.Contains("NavView.IsPaneToggleButtonVisible = false", code);
        Assert.Contains("x:Name=\"MobileShellHeader\"", xaml);
        Assert.Contains("Click=\"MobileMenu_Click\"", xaml);
        Assert.Contains("Click=\"MobileSearch_Click\"", xaml);
        Assert.Contains("x:Name=\"MobileServerActivityButton\"", xaml);
        Assert.Contains("Click=\"MobileProfile_Click\"", xaml);
        Assert.Contains("(!_routeWantsCompactPane || _sidebarHoverExpanded || _profileFooterFlyoutOpen)", code);
        Assert.Contains("NavView.PointerMoved += NavView_PointerMoved", code);
        Assert.Contains("Interval = TimeSpan.FromMilliseconds(150)", code);
        Assert.Contains("pointerX <= paneWidth", code);
        Assert.Contains("CollapseImmersiveSidebarAfterPointerExit", code);
        Assert.Contains("x:Name=\"ProfileFooterFlyout\"", xaml);
        Assert.Contains("Opened=\"ProfileFooterFlyout_Opened\"", xaml);
        Assert.Contains("Closed=\"ProfileFooterFlyout_Closed\"", xaml);
        Assert.Contains("x:Name=\"ProfileFooterContent\"", xaml);
        Assert.Contains("ProfileFooterContent.Spacing = isOpen ? 10 : 0", code);
        Assert.Contains("ApplyResponsiveShellLayout();", code);
        Assert.Contains("ContentFrame.Content is Views.Admin.AdminShellPage", code);
        Assert.Contains("AdminShell owns both its sidebar and ServerActivity button", code);
        Assert.Contains("SynchronizeSelectedNavigationItem(e.SourcePageType, e.Parameter)", code);
        Assert.Contains("ResynchronizeSelectedNavigationItem();", code);
        Assert.Contains("FindLibraryNavigationItem", code);
        Assert.Contains("FindCatalogNavigationItem", code);
        Assert.Contains("FindCollectionNavigationItem", code);
        Assert.Contains("FindPluginNavigationItem", code);
        Assert.Contains("CloseMobileNavigationPane();", code);
        Assert.Contains("Modifiers=\"Menu\" Key=\"Left\"", xaml);
        Assert.DoesNotContain("Key=\"GamepadB\"", xaml);
        Assert.Contains("RootGrid.AddHandler(", code);
        Assert.Contains("Windows.System.VirtualKey.GamepadB", code);
        Assert.Contains("PointerPressed=\"RootGrid_PointerPressed\"", xaml);
        Assert.Contains("point.Properties.IsXButton1Pressed", code);
        Assert.Contains("_navigationService.GoBack();", code);
    }

    [Fact]
    public void SharedProfileChromeUsesServerAvatarWithInitialFallback()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"ProfileAvatarImage\"", xaml);
        Assert.Contains("x:Name=\"ProfileDropdownAvatarImage\"", xaml);
        Assert.Contains("x:Name=\"MobileProfileInitialText\"", xaml);
        Assert.Contains("ApplyProfileAvatar(profile.AvatarUrl)", code);
        Assert.Contains("MobileProfileInitialText.Text = ProfileInitialText.Text", code);
        Assert.Contains("_navigationService.Navigate<SettingsPage>();", code);
        Assert.Contains("string.Equals(username, profile.Name, StringComparison.Ordinal)", code);
    }

    [Fact]
    public void SelectedNavigationUsesSidebarForegroundInsteadOfAccentColoredText()
    {
        var theme = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Themes",
            "DarkTheme.xaml"));

        Assert.Contains(
            "x:Key=\"NavigationViewItemForegroundSelected\" Color=\"{StaticResource PrimaryTextColor}\"",
            theme);
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
