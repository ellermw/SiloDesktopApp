namespace SiloPlayer.Tests;

public sealed class MainWindowSourceTests
{
    [Fact]
    public void UpdateStartupRecordsReadinessButRefusesMismatchedIdentityBeforeConfirmation()
    {
        var startup = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SiloPlayer", "MainWindow.UpdateStartup.cs"));
        var ready = startup.IndexOf("SideBySideInstallation.RecordReady(root);", StringComparison.Ordinal);
        var pipeReady = startup.IndexOf("await UpdateHostProtocol.WriteAsync(pipe,new(\"ready\",transaction)", StringComparison.Ordinal);
        var confirmed = startup.IndexOf("if(confirmed.Kind!=\"confirmed\" || confirmed.TransactionId!=transaction)return;", StringComparison.Ordinal);
        var identity = startup.IndexOf("ReleaseVersion.FromAssembly(typeof(App).Assembly)", StringComparison.Ordinal);
        var comparison = startup.IndexOf("installed.GetWindowsVersion().ToString(4) != version", StringComparison.Ordinal);
        var receipt = startup.IndexOf("SideBySideInstallation.GetBootReceiptDirectory(root)", StringComparison.Ordinal);
        Assert.True(ready >= 0 && identity > ready && comparison > identity);
        Assert.True(pipeReady > comparison && confirmed > pipeReady && receipt > confirmed,
            "Invalid stamped identity must retain the local readiness observation but prevent broker confirmation and success feedback.");
        Assert.Contains("throw new InvalidDataException", startup[comparison..pipeReady]);
    }

    [Fact]
    public void PrivilegedWorkerChecksStampsBeforeActivationAndEveryConfirmationRoute()
    {
        var root = FindRepositoryRoot();
        var worker = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.UpdateHost", "UpdateWorker.cs"));
        foreach (var method in new[] { "public void ValidateCallerAndParent()", "public void AuthorizeNewClient(string executable)", "public bool MatchesReadyProcess(UpdateHostMessage message,string executable)" })
        {
            var body = worker[worker.IndexOf(method, StringComparison.Ordinal)..];
            body = body[..body.IndexOf("\n        }", StringComparison.Ordinal)];
            Assert.Contains("SideBySideInstallation.VerifyInstalledClientIdentity", body);
        }
        var runner = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "Updates", "UpdateTransactionRunner.cs"));
        Assert.True(runner.IndexOf("environment.ValidateCallerAndParent();", StringComparison.Ordinal)
            < runner.IndexOf("SideBySideInstallation.Confirm(request.InstallationRoot, pending);", StringComparison.Ordinal));
        Assert.True(runner.IndexOf("environment.AuthorizeNewClient(", StringComparison.Ordinal)
            < runner.IndexOf("SideBySideInstallation.Activate(staged);", StringComparison.Ordinal));
        Assert.True(runner.IndexOf("environment.MatchesReadyProcess(ready, image)", StringComparison.Ordinal)
            < runner.IndexOf("SideBySideInstallation.Confirm(request.InstallationRoot, staged.TransactionId);", StringComparison.Ordinal));
    }

    [Fact]
    public void PageChangesEaseInWithoutWholePageOpacityFlashes()
    {
        var root = FindRepositoryRoot();
        var navigation = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "NavigationService.cs"));
        var shell = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var transition = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "PageTransitionHelper.cs"));

        // New routes and Back ease in (Granary-like), on the compositor as one visual.
        Assert.Contains("return Frame.Navigate(pageType, parameter, PageTransitionHelper.ForPageChange());", navigation);
        Assert.Contains("Frame.GoBack(PageTransitionHelper.ForPageChange());", navigation);
        Assert.Contains("new EntranceNavigationTransitionInfo()", transition);
        // Reduced motion and an in-place refresh of the current route stay instant.
        Assert.Contains("ArtworkReveal.AnimationsEnabled", transition);
        Assert.Contains("new SuppressNavigationTransitionInfo()", transition);
        Assert.Contains("var accepted = frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());", navigation);
        Assert.Contains("_currentPageType == pageType", navigation);
        Assert.Contains("OnNavigated_AnimatePageEntrance", shell);
        Assert.Contains("PageTransitionHelper.AnimateEntrance", shell);
        // Only an instant swap is normalized; an easing entrance keeps its own opacity and offset.
        Assert.Contains("transition is not Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo", shell);
        Assert.True(shell.IndexOf("transition is not Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo", StringComparison.Ordinal)
            < shell.IndexOf("PageTransitionHelper.AnimateEntrance(content)", StringComparison.Ordinal));
        Assert.Contains("content.Opacity = 1", transition);
        Assert.Contains("content.RenderTransform = null", transition);
        Assert.DoesNotContain("Storyboard", transition);
        Assert.DoesNotContain("DoubleAnimation", transition);
        Assert.DoesNotContain("TranslateY = 6", transition);
        Assert.DoesNotContain("ScaleX = 0.985", transition);
    }

    [Fact]
    public void EndedPlayingNextShowsFinishingArtworkInsteadOfAnEmptySlot()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var art = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.PlayingNextFinishingArt.cs"));

        // The slot the native preview leaves at true EOF has a picture of its own.
        var slot = xaml[xaml.IndexOf("x:Name=\"PlayingNextFinishingSlot\"", StringComparison.Ordinal)..];
        slot = slot[..slot.IndexOf("</Border>", StringComparison.Ordinal)];
        Assert.Contains("x:Name=\"PlayingNextFinishingArt\"", slot);

        // Early post-roll reaching EOF, and Playing Next opening at EOF, both hand the slot over.
        var hidden = code.IndexOf("_playerService.FinishPostRollPreview();", StringComparison.Ordinal);
        var shown = code.IndexOf("ShowPlayingNextFinishingArtAtEnd();", StringComparison.Ordinal);
        Assert.True(hidden >= 0 && shown > hidden && shown < code.IndexOf("return;", hidden, StringComparison.Ordinal),
            "True EOF under an open early post-roll must show the finishing artwork where the preview was hidden.");
        var begun = code.IndexOf("BeginPlayingNextFinishingArt();", StringComparison.Ordinal);
        var visible = code.IndexOf("PlayingNextOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal);
        Assert.True(begun > shown && visible > begun &&
            code.IndexOf("UpdatePlayingNextFinishingArt();", visible, StringComparison.Ordinal) < code.IndexOf("PositionPlayingNextVideo();", visible, StringComparison.Ordinal),
            "A new presentation must settle its finishing slot in the frame it appears.");

        // Closing Playing Next, a newer playback and another profile each retire the picture.
        Assert.Contains("PlayingNextOverlay.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnPlayingNextOverlayVisibilityChanged);", code);
        Assert.Contains("if (PlayingNextOverlay.Visibility != Visibility.Visible) RetirePlayingNextFinishingArt();", art);
        Assert.Contains("if (generation != _finishingArtGeneration) return;", art);
        Assert.Contains("_apiClient.IsCurrentIdentityContext(art.Owner)", art);
        Assert.Contains("string.Equals(detail.ContentId, _playerService.ContentId, StringComparison.Ordinal)", art);

        // Only the finished title's backdrop, only once it has ended; otherwise no rectangle.
        Assert.Contains("detail.BackdropUrl", art);
        Assert.DoesNotContain("NextEpisode", art);
        Assert.Contains("if (!_playerService.IsPostRollVideoEnded)", art);
        Assert.Contains("Visibility.Collapsed", art);
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
        // ItemsRepeater sets no DataContext on an x:Bind template: On Deck art must use x:Bind.
        Assert.Contains("<Image Source=\"{x:Bind ThumbnailSource}\"", xaml);
        Assert.DoesNotContain("{Binding ThumbnailSource}", xaml);
        Assert.Contains("PlayingNextFinishedCountdownText", xaml);
        Assert.Contains("else if (playbackHasEnded && !hasNextEpisode)", code);
        Assert.Contains("StartFinishedAutoClose();", code);
        Assert.Contains("if (!autoPlay && !_playingNextFinishedCountdown)", code);
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
        var enterPreview = code.IndexOf("_playerService.EnterPostRollPreview();", StringComparison.Ordinal);
        var showOverlay = code.IndexOf("PlayingNextOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal);
        Assert.True(
            enterPreview >= 0 && showOverlay > enterPreview,
            "The native mpv HWND must move before the WinUI Playing Next surface is exposed.");
        // #3917: the ended countdown also waits past the still-watching limit.
        Assert.Contains("if (playbackHasEnded && hasNextEpisode && PlayingNextCountdownAllowed)", code);
        Assert.Contains("PlayingNextFinishedHeading.Text", code);
        Assert.Contains("PlayingNextOnDeckScroller.ChangeView", code);
        Assert.Contains("x:Name=\"PlayingNextHero\"", xaml);
        Assert.Contains("UpdatePlayingNextLayout(e.Size.Width, e.Size.Height)", code);
        // The hero keeps its final size before any picture, and the next picture loads during playback.
        Assert.Contains("PlayingNextHero.Width = availableWidth;", code);
        Assert.Contains("_playerService.PositionChanged += OnPlaybackPositionForPlayingNext;", code);
        Assert.Contains("BeginPlayingNextArt(hasNextEpisode);", code);
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
    public void ShufflePlayingNextOverviewFollowsEpisodeSpoilerPolicy()
    {
        var root = FindRepositoryRoot();
        var shuffle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.Shuffle.cs"));
        var spoilers = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.EpisodeSpoilers.cs"));

        // An unwatched shuffle episode must not republish the overview the policy hid.
        Assert.DoesNotContain("PlayingNextOverviewText.Text = _playerService.NextEpisodeOverview", shuffle);
        Assert.Contains("ShowPlayingNextOverview(spoilers.ShouldHideOverviews);", shuffle);
        Assert.Contains("_playerService.ShuffleNext?.Type == \"episode\"", spoilers);
        Assert.Contains("protectedEpisode && hideOverviews ? \"\" : _playerService.NextEpisodeOverview", spoilers);
    }

    [Fact]
    public void PlayingNextPreloadBacksOffAndSharesTheArtworkDecoder()
    {
        var root = FindRepositoryRoot();
        var artwork = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.PlayingNextArtwork.cs"));
        var spoilers = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.EpisodeSpoilers.cs"));
        var card = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "PosterCard.xaml.cs"));

        // The latch is released when the preload finishes, and a failing picture waits before its next attempt.
        Assert.Contains("DispatcherQueue.TryEnqueue(() => _ = PreloadPlayingNextArtAsync())", artwork);
        Assert.Contains("finally { Interlocked.Exchange(ref _playingNextPreloadQueued, 0); }", artwork);
        Assert.Contains("Environment.TickCount64 < _playingNextPreloadRetryAt", artwork);
        Assert.Contains("if (NotePlayingNextPreloadFailure(key) == 1)", artwork);
        // Playing Next bitmaps are created through the same decoder as library cards.
        Assert.Contains("ArtworkDecodeGate.BitmapCreateLock)", spoilers);
        Assert.Contains("s_bitmapCreateLock = ArtworkDecodeGate.BitmapCreateLock;", card);
        // A blurred spoiler still is decoded before assignment, so it never waits for an ImageOpened that may not come.
        Assert.Contains("contentId + \"\\n\" + url, Decoded: hide);", artwork);
        Assert.Contains("if (art.Decoded || image.PixelWidth > 0) { RevealPlayingNextArt(); return; }", artwork);
    }

    [Fact]
    public void PlayingNextArtDecodeFailureIsNeverCached()
    {
        var root = FindRepositoryRoot();
        var artwork = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.PlayingNextArtwork.cs"));
        var spoilers = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.EpisodeSpoilers.cs"));

        // The failure handler is attached before the bitmap gets its source, so an early decode failure is seen.
        Assert.DoesNotContain("UriSource = new Uri(path) }", spoilers);
        Assert.Contains("beforeSource?.Invoke(uriImage);\n            uriImage.UriSource = new Uri(path);", spoilers.ReplaceLineEndings("\n"));
        Assert.Contains("bitmap => bitmap.ImageFailed += Failed", artwork);
        // A failed picture is neither reused nor cached, so the next attempt reads it afresh.
        Assert.Contains("!art.Failed && art.ContentId", artwork);
        Assert.Contains("if (!art.Failed && contentId == _playerService.NextEpisodeContentId", artwork);
    }

    [Fact]
    public void ShufflePlayingNextStillFlagFollowsTheShownImage()
    {
        var root = FindRepositoryRoot();
        var player = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlayerService.cs"));

        // A shuffle episode without a poster shows its backdrop, so the backdrop's still flag decides the blur.
        Assert.Contains("ShuffleNext.PosterUrl is not null ? ShuffleNext.PosterIsEpisodeStill : ShuffleNext?.BackdropIsEpisodeStill", player);
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
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"43.5\" />", xaml);
        Assert.Contains("x:Name=\"SidebarBrandHost\" Height=\"96\"", xaml);
        Assert.Contains("Tapped=\"SidebarBrandHost_Tapped\"", xaml);
        Assert.Contains("KeyDown=\"SidebarBrandHost_KeyDown\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Go to home\"", xaml);
        Assert.Contains("private void SidebarBrandHost_Tapped", code);
        Assert.Contains("private void SidebarBrandHost_KeyDown", code);
        Assert.Contains("NavigateToHome();", code);
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
        Assert.Contains("_eventChannel.Subscribe(\"notifications\", \"user_settings\", \"user_state\")", code);
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
        Assert.Contains("SidebarFooterPanel.Width = isOpen ? NavView.OpenPaneLength : NavView.CompactPaneLength", code);
        Assert.Contains("SidebarFooterSeparator.Width = isOpen ? double.NaN : NavView.CompactPaneLength", code);
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
        Assert.Contains("if (NavView.IsPaneOpen != DesktopSidebarIsOpen)", code);
        Assert.DoesNotContain("_routeWantsCompactPane", code);
        Assert.Contains("ApplyResponsiveShellLayout", code);
        Assert.Contains("_currentWindowWidth < 1024", code);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftMinimal", code);
        Assert.Contains("NavigationViewPaneDisplayMode.LeftCompact", code);
        Assert.Contains("PaneDisplayMode=\"LeftCompact\"", xaml);
        Assert.Contains("var shouldShowDesktopToggle = !isNarrow", code);
        Assert.Contains("NavView.IsPaneToggleButtonVisible = shouldShowDesktopToggle", code);
        Assert.Contains("private readonly SidebarPresentationState _sidebarPresentation = new()", code);
        Assert.Contains("_settingsService.Load().DesktopSidebarOpen", code);
        Assert.Contains("RememberDesktopSidebarState(!NavView.IsPaneOpen)", code);
        Assert.Contains("settings.DesktopSidebarOpen = isOpen", code);
        Assert.Contains("if (NavView.IsPaneOpen != DesktopSidebarIsOpen)", code);
        Assert.Contains("NavView.IsPaneOpen = DesktopSidebarIsOpen", code);
        Assert.Contains("before revealing it so the wrong state cannot render for a frame", code);
        Assert.Contains("_sidebarPresentation.Navigate(e.SourcePageType == typeof(ItemDetailPage)", code);
        Assert.Contains("if (!_sidebarPresentation.Toggle(isOpen)) return", code);
        Assert.Contains("RememberDesktopSidebarState(!NavView.IsPaneOpen)", code);
        Assert.Contains("NavView.PaneOpening += NavView_PaneOpening", code);
        Assert.Contains("private void NavView_PaneOpening", code);
        Assert.Contains("ScheduleDesktopPaneSynchronization();", code);
        Assert.Contains("x:Name=\"MobileShellHeader\"", xaml);
        Assert.Contains("Click=\"MobileMenu_Click\"", xaml);
        Assert.Contains("Click=\"MobileSearch_Click\"", xaml);
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
        Assert.Contains("ProfileNameText.Opacity = isOpen ? 1 : 0", code);
        Assert.Contains("ProfileFooterContent.Spacing = isOpen ? 10 : 0", code);
        Assert.Contains("FlyoutPlacementMode.RightEdgeAlignedBottom", code);
        Assert.Contains("NotificationUnreadBadge.Margin = isOpen", code);
        Assert.Contains("ThemeDotsPanel.Visibility = Visibility.Collapsed", code);
        Assert.DoesNotContain("themeService.PreviewTheme(capturedId)", code);
        Assert.DoesNotContain("_settingsApi.PutSettingAsync(\"ui_theme\", capturedId)", code);
        Assert.Contains("ApplyResponsiveShellLayout();", code);
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
    public void SharedProfileChromeUsesServerAvatarWithInitialFallback()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));

        Assert.Contains("x:Name=\"ProfileAvatarImage\"", xaml);
        Assert.Contains("x:Name=\"ProfileDropdownAvatarImage\"", xaml);
        Assert.Contains("x:Name=\"MobileProfileInitialText\"", xaml);
        Assert.Contains("ApplyProfileAvatarAsync(profile.AvatarUrl)", code);
        Assert.Contains("GetImageDiskPathAsync", code);
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
        // Skeletons only until the first successful read (a loaded empty account does not flash them again).
        Assert.Contains("IsLoading = !PersonalLoaded", collectionsVm);
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
