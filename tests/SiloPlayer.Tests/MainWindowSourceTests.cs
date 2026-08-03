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
        var navigation = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "NavigationService.cs"));
        var mainViewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("x:Name=\"SiloWordmarkImage\"", xaml);
        Assert.Contains("x:Name=\"SiloMarkImage\"", xaml);
        Assert.Contains("x:Name=\"RequestsNavItem\"", xaml);
        Assert.Contains("x:Name=\"SearchNavItem\"", xaml);
        Assert.Contains("Text=\"Ctrl K\"", xaml);
        Assert.Contains("FontSize=\"13\"", xaml);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"18\" />", xaml);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"42\" />", xaml);
        Assert.Contains("x:Name=\"SidebarBrandHost\" Height=\"96\"", xaml);
        Assert.Contains("Margin=\"14,30,0,30\"", xaml);
        Assert.Contains("<ScalarTransition Duration=\"0:0:0.3\" />", xaml);
        Assert.Contains("x:Name=\"LibrariesCompactDividerIcon\"", xaml);
        Assert.Contains("x:Name=\"NotificationsNavItem\"", xaml);
        Assert.Contains("x:Name=\"NotificationUnreadBadge\"", xaml);
        Assert.Contains("Margin=\"0,2,8,0\"", xaml);
        Assert.Contains("Background=\"{StaticResource AccentBrush}\"", xaml);
        Assert.Contains("Foreground=\"{StaticResource AccentForegroundBrush}\"", xaml);
        Assert.Contains("IsPaneOpen=\"True\"", xaml);
        Assert.Contains("PaneClosing=\"NavView_PaneClosing\"", xaml);
        Assert.Contains("CompactPaneLength=\"64\"", xaml);
        Assert.Contains("IsPaneToggleButtonVisible=\"True\"", xaml);
        Assert.Contains("NavView.Resources[\"NavigationViewDefaultPaneBackground\"] = sidebarBackground", code);
        Assert.Contains("NavView.Resources[\"NavigationViewExpandedPaneBackground\"] = sidebarBackground", code);
        Assert.Contains("Application.Current.Resources[\"SidebarBackgroundBrush\"]", code);
        Assert.DoesNotContain("Content=\"Catalog\" Tag=\"Catalog\"", xaml);
        Assert.Contains("RefreshUserNavigationCapabilitiesAsync", code);
        Assert.Contains("requestStatus.RequestsEnabled", code);
        Assert.Contains("capability.InApp.Enabled", code);
        Assert.Contains("NotificationUnreadBadge.Value = isOpen ? _notificationUnreadCount : -1", code);
        Assert.Contains("_notificationUnreadCount = Math.Max(_notificationUnreadCount, unread)", code);
        Assert.Contains("_eventChannel.Subscribe(\"notifications\")", code);
        Assert.Contains("notification.created", code);
        Assert.Contains("notification.read", code);
        Assert.Contains("NotificationBelongsToSelectedProfile", code);
        Assert.Contains("if (unread >= 25)", code);
        Assert.Contains("RefreshExactNotificationCountAsync", code);
        Assert.Contains("ReleaseNotificationSubscription();", code);
        Assert.Contains("CancelShellHydration();", code);
        Assert.Contains("IsCurrentShellHydration", code);
        Assert.Contains("_notificationSubscription == null", code);
        Assert.Contains("ReloadLibrariesAsync(cancellationToken)", code);
        Assert.Contains("_libraryLoadGeneration", mainViewModel);
        Assert.Contains("loadCts.IsCancellationRequested", mainViewModel);
        Assert.Contains("SidebarFooterPanel.Width = isOpen ? double.NaN : NavView.CompactPaneLength", code);
        Assert.Contains("SidebarFooterSeparator.Width = isOpen ? double.NaN : NavView.CompactPaneLength", code);
        Assert.Contains("AdminButton.Margin = isOpen", code);
        Assert.Contains("ProfileFooterButton.Margin = isOpen", code);
        Assert.Contains("UpdateLibraryNavigationVisibility(isOpen)", code);
        Assert.Contains("if (!NavView.IsPaneOpen)", code);
        Assert.Contains("var visible = !isOpen || _librariesExpanded", code);
        Assert.Contains("UpdateSidebarPanePresentation", code);
        Assert.Contains("? NavigationViewPaneDisplayMode.Left", code);
        Assert.Contains(": NavigationViewPaneDisplayMode.LeftCompact", code);
        Assert.DoesNotContain("_preserveOpenDesktopPaneDuringNavigation", code);
        Assert.Contains("private bool _synchronizingDesktopPaneState", code);
        Assert.Contains("args.Cancel = true", code);
        Assert.Contains("IsPaneToggleInputSource", code);
        Assert.Contains("UIElement.PointerPressedEvent", code);
        Assert.Contains("UIElement.KeyDownEvent", code);
        Assert.Contains("name.Contains(\"TogglePane\"", code);
        Assert.Contains("name.Contains(\"PaneToggle\"", code);
        Assert.Contains("RegisterPropertyChangedCallback(", code);
        Assert.Contains("NavigationView.IsPaneOpenProperty", code);
        Assert.Contains("OnNavViewIsPaneOpenChanged", code);
        Assert.Contains("SynchronizeDesktopPaneState", code);
        Assert.Contains("if (NavView.IsPaneOpen != _desktopSidebarOpen)", code);
        Assert.DoesNotContain("_routeWantsCompactPane", code);
        Assert.Contains("ApplyResponsiveShellLayout", code);
        Assert.Contains("_currentWindowWidth < 1024", code);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftMinimal", code);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftCompact", code);
        Assert.Contains("PaneDisplayMode=\"LeftCompact\"", xaml);
        Assert.Contains("var shouldShowDesktopToggle = !isNarrow", code);
        Assert.Contains("NavView.IsPaneToggleButtonVisible = shouldShowDesktopToggle", code);
        Assert.Contains("private bool _desktopSidebarOpen = true", code);
        Assert.Contains("_settingsService.Load().DesktopSidebarOpen", code);
        Assert.Contains("RememberDesktopSidebarState(!NavView.IsPaneOpen)", code);
        Assert.Contains("settings.DesktopSidebarOpen = isOpen", code);
        Assert.Contains("if (NavView.IsPaneOpen != _desktopSidebarOpen)", code);
        Assert.Contains("NavView.IsPaneOpen = _desktopSidebarOpen", code);
        Assert.Contains("before revealing it so the wrong state cannot render for a frame", code);
        Assert.Contains("_desktopSidebarOpen = true", code);
        Assert.Contains("RememberDesktopSidebarState(!NavView.IsPaneOpen)", code);
        Assert.Contains("NavView.PaneOpening += NavView_PaneOpening", code);
        Assert.Contains("private void NavView_PaneOpening", code);
        Assert.Contains("before WinUI renders the", code);
        Assert.Contains("x:Name=\"MobileShellHeader\"", xaml);
        Assert.Contains("Click=\"MobileMenu_Click\"", xaml);
        Assert.Contains("Click=\"MobileSearch_Click\"", xaml);
        Assert.Contains("x:Name=\"MobileServerActivityButton\"", xaml);
        Assert.Contains("Click=\"MobileProfile_Click\"", xaml);
        Assert.DoesNotContain("NavView.PointerMoved += NavView_PointerMoved", code);
        Assert.DoesNotContain("NavView_PointerExited", code);
        Assert.DoesNotContain("SidebarHoverTimer", code);
        Assert.DoesNotContain("CollapseImmersiveSidebarAfterPointerExit", code);
        Assert.Contains("x:Name=\"ProfileFooterFlyout\"", xaml);
        Assert.Contains("Opening=\"ProfileFooterFlyout_Opening\"", xaml);
        Assert.DoesNotContain("Opened=\"ProfileFooterFlyout_Opened\"", xaml);
        Assert.DoesNotContain("Closed=\"ProfileFooterFlyout_Closed\"", xaml);
        Assert.Contains("x:Name=\"ProfileFooterContent\"", xaml);
        Assert.Contains("SiloWordmarkImage.Opacity = isOpen ? 1 : 0", code);
        Assert.Contains("SiloMarkImage.Opacity = isOpen ? 0 : 1", code);
        Assert.Contains("AdminButtonLabel.Opacity = isOpen ? 1 : 0", code);
        Assert.Contains("ProfileNameText.Opacity = isOpen ? 1 : 0", code);
        Assert.Contains("ProfileFooterContent.Spacing = 10", code);
        Assert.Contains("FlyoutPlacementMode.RightEdgeAlignedBottom", code);
        Assert.Contains("NotificationUnreadBadge.Margin = isOpen", code);
        Assert.Contains("var dot = new Button", code);
        Assert.Contains("themeService.PreviewTheme(capturedId)", code);
        Assert.Contains("_settingsApi.PutSettingAsync(\"ui_theme\", capturedId)", code);
        Assert.Contains("ApplyResponsiveShellLayout();", code);
        Assert.Contains("ContentFrame.Content is Views.Admin.AdminShellPage", code);
        Assert.Contains("AdminShell owns both its sidebar and ServerActivity button", code);
        Assert.Contains("SynchronizeSelectedNavigationItem(e.SourcePageType, e.Parameter)", code);
        Assert.Contains("ResynchronizeSelectedNavigationItem();", code);
        Assert.Contains("FindLibraryNavigationItem", code);
        Assert.Contains("FindCatalogNavigationItem", code);
        Assert.Contains("FindCollectionNavigationItem", code);
        Assert.Contains("FindPluginNavigationItem", code);
        Assert.Contains("BuildPinnedSidebarContent", code);
        Assert.Contains("ToolTipService.SetToolTip(unpinButton, \"Unpin\")", code);
        Assert.Contains("AutomationProperties.SetName(unpinButton, $\"Unpin {pin.Label}\")", code);
        Assert.Contains("RemoveSidebarPinAsync", code);
        Assert.Contains("CloseMobileNavigationPane();", code);
        Assert.Contains("Modifiers=\"Menu\" Key=\"Left\"", xaml);
        Assert.DoesNotContain("Key=\"GamepadB\"", xaml);
        Assert.Contains("RootGrid.AddHandler(", code);
        Assert.Contains("Windows.System.VirtualKey.GamepadB", code);
        Assert.Contains("PointerPressed=\"RootGrid_PointerPressed\"", xaml);
        Assert.Contains("point.Properties.IsXButton1Pressed", code);
        Assert.Contains("_navigationService.GoBack();", code);
        Assert.DoesNotContain("_navigationService.NavigationRequestHandler = TryStageShellNavigation", code);
        Assert.DoesNotContain("_navigationService.BackNavigationRequestHandler = TryStageBackNavigation", code);
        Assert.Contains("NavView.IsPaneOpen = false;", code);
        Assert.DoesNotContain("Interval = TimeSpan.FromMilliseconds(380)", code);
        Assert.DoesNotContain("PendingShellNavigation", code);
        Assert.Contains("public Func<Type, object?, bool>? NavigationRequestHandler", navigation);
        Assert.Contains("public Func<bool>? BackNavigationRequestHandler", navigation);
        Assert.Contains("public bool NavigateImmediately(Type pageType, object? parameter = null)", navigation);
        Assert.Contains("public void GoBackImmediately()", navigation);
    }

    [Fact]
    public void ServerActivityTriggerHasClippingSafeGeometryAndDynamicAccessibleName()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SiloPlayer",
            "Controls",
            "ServerActivityButton.xaml"));
        var code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "SiloPlayer",
            "Controls",
            "ServerActivityButton.xaml.cs"));

        Assert.Contains("<Grid Width=\"44\" Height=\"44\">", xaml);
        Assert.Contains("Width=\"36\"", xaml);
        Assert.Contains("Height=\"36\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Server activity\"", xaml);
        Assert.Contains("CountBadgeText.Text = total > 99 ? \"99+\" : total.ToString()", code);
        Assert.Contains("$\"Server activity: {total} active\"", code);
        Assert.Contains("ActivityIcon.Stroke = (SolidColorBrush)Application.Current.Resources[\"AccentBrush\"]", code);
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
        Assert.Contains("IsLoading = replace && Items.Count == 0", favoritesVm);
        Assert.Contains("IsLoading = Items.Count == 0", historyVm);
        Assert.Contains("IsLoading = replace && Items.Count == 0", watchlistVm);
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
