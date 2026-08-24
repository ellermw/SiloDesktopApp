namespace SiloPlayer.Tests;

public sealed class CurrentSettingsParitySourceTests
{
    [Fact]
    public void SettingsShellMatchesCurrentWideSearchableFiveGroupDirectory()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("MaxWidth=\"1424\"", xaml);
        Assert.Contains("x:Name=\"SettingsSearchBox\"", xaml);
        Assert.Contains("17 settings sections", xaml);
        Assert.Contains("x:Name=\"SettingsOverviewPanel\"", xaml);
        Assert.Contains("Home & Discovery", code);
        Assert.Contains("Connections", code);
        Assert.Contains("Account", code);
        Assert.Contains("x:Name=\"AppearanceNavGroup\"", xaml);
        Assert.Matches("AppearanceNavGroup[\\s\\S]+InterfaceTab[\\s\\S]+CardOverlaysTab[\\s\\S]+AccessibilityTab[\\s\\S]+ThemeEditorTab[\\s\\S]+HomeDiscoveryNavGroup[\\s\\S]+PersonalizeTab[\\s\\S]+ConnectionsNavGroup[\\s\\S]+WebhookSyncTab[\\s\\S]+ImportTab[\\s\\S]+AccountNavGroup", xaml);
        Assert.Contains("SettingsSearchBox_TextChanged", code);
        Assert.Contains("SettingsPage_SizeChanged", code);
        Assert.Contains("e.NewSize.Width < 1024", code);
        Assert.Contains("SettingsNavigationGroups.Orientation", code);
        Assert.Contains("SettingsContentPanel.MaxWidth", code);
        Assert.Contains("x:Name=\"DevicesTab\"", xaml);
        Assert.Contains("x:Name=\"ConnectAppsTab\"", xaml);
    }

    [Fact]
    public void PlaybackAndSubtitlePanelsUseCurrentWebGroupingAndCopy()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");

        Assert.Contains("Choose the defaults Silo should use when playback starts.", xaml);
        Assert.Contains("Text=\"Defaults\"", xaml);
        Assert.Contains("Text=\"Auto-skip intros\"", xaml);
        Assert.Contains("Text=\"Start next at preview\"", xaml);
        Assert.Contains("Text=\"Background &amp; Position\"", xaml);
        Assert.Contains("This sample reflects the current subtitle appearance.", xaml);
        Assert.Matches("SubtitlePreviewBg2[\\s\\S]+Save Appearance[\\s\\S]+Text group", xaml);
    }

    [Fact]
    public void SettingsNavigationIncludesCurrentCardOverlayAndPersonalizeRoutes()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("x:Name=\"CardOverlaysTab\"", xaml);
        Assert.Contains("x:Name=\"PersonalizeTab\"", xaml);
        Assert.Contains("x:Name=\"CardOverlaysPanel\"", xaml);
        Assert.Contains("x:Name=\"PersonalizePanel\"", xaml);
        Assert.Contains("LoadCardOverlaySettingsAsync", code);
        Assert.Contains("LoadPersonalizeSummaryAsync", code);
    }

    [Fact]
    public void PersonalizeSettingsCanReplayTheCurrentServerDrivenFeatureTour()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var dialog = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "FeatureTourDialog.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "SettingsApi.cs");

        Assert.Contains("Replay the feature tour", xaml);
        Assert.Contains("Start the tour", xaml);
        Assert.Contains("GetOnboardingFlowAsync(\"web\")", page);
        Assert.Contains("KnownKinds", dialog);
        Assert.Contains("setting_choice", dialog);
        Assert.Contains("Skip tour", dialog);
        Assert.Contains("ReportOnboardingProgressAsync", dialog);
        Assert.Contains("/api/v1/onboarding/flow?surface=", api);
        Assert.Contains("/api/v1/onboarding/progress", api);
    }

    [Fact]
    public void ConnectAppsWithholdsInvalidCompatCredentialsLikeCurrentWebUi()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.Account.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "SettingsApi.cs");

        Assert.Contains("PasswordLoginAvailable == false", page);
        Assert.Contains("PendingRestart", page);
        Assert.Contains("IsLoopbackUrl", page);
        Assert.Contains("contains a #, which Jellyfin apps can't sign in with", page);
        Assert.Contains("Every profile at a glance", page);
        Assert.Contains("A Jellyfin app says my username or password is wrong", page);
        Assert.Contains("public bool? PasswordLoginAvailable", api);
    }

    [Fact]
    public void DeviceSettingsRespectHouseholdPermissionsAndProfileOwnership()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.Account.cs");

        Assert.Contains("if (_canManageProfiles)", page);
        Assert.Contains("PlaceholderText = \"All profiles\"", page);
        Assert.Contains("selectedProfileId", page);
        Assert.Contains("You're changing {device.ProfileName}'s settings, not your own.", page);
        Assert.Contains("will see these changes on this device", page);
        Assert.Contains("Use {ownerLabel} setting", page);
        Assert.Contains("This device picks up the changes the next time it's used.", page);
    }

    [Fact]
    public void CardOverlaySettingsPersistFullCurrentV2Document()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "CardOverlayService.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var poster = ReadRepoFile("src", "SiloPlayer", "Controls", "PosterCard.xaml.cs");

        Assert.Contains("string? AccentColor", service);
        Assert.Contains("bool? ShowIcon", service);
        Assert.Contains("[\"accentColor\"]", service);
        Assert.Contains("[\"showIcon\"]", service);
        Assert.Contains("public async Task SaveAsync(CardOverlayPrefs", service);
        Assert.Contains("minimal\" or \"classic\" or \"vibrant\" or \"pill\" or \"square", service);
        Assert.Contains("OverlayAccentOptions", page);
        Assert.Contains("BuildCardOverlayPresetCards", page);
        Assert.Contains("BuildCardOverlayAccentFlyout", page);
        Assert.Contains("case \"minimal\"", poster);
        Assert.Contains("case \"vibrant\"", poster);
        Assert.Contains("case \"pill\"", poster);
        Assert.Contains("case \"square\"", poster);
    }

    [Fact]
    public void LibrarySettingsUseSharedVisibilityAndOrderContracts()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs");
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");

        Assert.Contains("disabled_library_ids", viewModel);
        Assert.Contains("library_order", viewModel);
        Assert.Contains("PutSettingAsync(\"disabled_library_ids\"", viewModel);
        Assert.Contains("PutSettingAsync(\"library_order\"", viewModel);
        Assert.Contains("MoveLibraryAsync", viewModel);
        Assert.Contains("Show all", xaml);
        Assert.Contains("Hide all", xaml);
        Assert.Contains("libraries visible for this profile", xaml);
    }

    [Fact]
    public void SubtitleAppearanceUsesCurrentDeviceOverrideContract()
    {
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Settings", "SubtitleAppearance.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs");
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");

        Assert.Contains("TextOutlineColor", model);
        Assert.Contains("xxlarge", model);
        Assert.Contains("SetContractSettingValueAsync(\"playback.subtitle_appearance\", \"profile_device\"", viewModel);
        Assert.Contains("DeleteContractSettingValueAsync(\"playback.subtitle_appearance\", \"profile_device\"", viewModel);
        Assert.Contains("DiscardSubtitleAppearance", viewModel);
        Assert.Contains("Lower Third", xaml);
        Assert.Contains("XX-Large", xaml);
    }

    [Fact]
    public void AccessibilitySettingsPersistAndApplyAcrossNativeNavigation()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "AccessibilityService.cs");
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");

        Assert.Contains("ui_text_scale", viewModel);
        Assert.Contains("ui_text_weight", viewModel);
        Assert.Contains("ui_high_contrast", viewModel);
        Assert.Contains("BuildAccessibilityControls", page);
        Assert.Contains("Extra Large", page);
        Assert.Contains("High Contrast", page);
        Assert.Contains("ApplyTypography", service);
        Assert.Contains("OnNavigated_ApplyAccessibility", window);
    }

    [Fact]
    public void NativeThemeEditorSupportsSharedOverridesPortableFilesAndCatalog()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "SettingsApi.cs");

        Assert.Contains("ui_custom_theme_vars", service);
        Assert.Contains("SetThemeOverride", service);
        Assert.Contains("ResetThemeOverrides", service);
        Assert.Contains("ThemeExport_Click", page);
        Assert.Contains("ThemeImport_Click", page);
        Assert.Contains("LoadThemeCatalogAsync", page);
        Assert.Contains("DownloadThemeAsync", api);
        Assert.Contains("/api/v1/theme/catalog", api);
        Assert.Contains("x:Name=\"ThemeCustomCssBox\"", ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        Assert.Contains("PutSettingAsync(\"ui_custom_css\"", page);
        Assert.Contains("SanitizeThemeCss", page);
        Assert.Contains("ThemeEditorSectionTab_Click", page);
    }

    [Fact]
    public void LibrariesRememberPageStateAndHomeSectionsAutoSaveLikeCurrentWebUi()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("Remember library pages", xaml);
        Assert.Contains("ui.remember_library_page_state", page);
        Assert.Contains("ui.library_page_state", page);
        Assert.Contains("HomeSectionsCountText", xaml);
        Assert.DoesNotContain("Click=\"HomeSectionsSave_Click\"", xaml);
        Assert.Contains("ViewModel.HomeSections.Move(oldIndex, newIndex)", page);
        Assert.Matches("HomeSections\\.Move\\(oldIndex, newIndex\\);[\\s\\S]{0,160}SaveHomeSectionsCommand", page);
        Assert.Matches("ToggleSectionVisibility\\(section\\);[\\s\\S]{0,160}SaveHomeSectionsCommand", page);
    }

    [Fact]
    public void ProfileThemeIsSynchronizedOnlyAfterAuthenticatedNavigationSucceeds()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs");
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");
        var profiles = ReadRepoFile("src", "SiloPlayer", "ViewModels", "ProfileSelectViewModel.cs");

        Assert.Contains("SyncFromServerAsync", service);
        Assert.Contains("GetSettingAsync(\"ui_theme\"", service);
        Assert.Contains("GetServerBrandingAsync", service);
        Assert.Contains("GetSettingAsync(\"ui_custom_theme_vars\"", service);
        var transitionStart = window.IndexOf("public bool TryEnterAuthenticatedPage", StringComparison.Ordinal);
        var transitionEnd = window.IndexOf("public void UpdateLibraryNavItems", transitionStart, StringComparison.Ordinal);
        var transition = window[transitionStart..transitionEnd];
        var shellStart = window.IndexOf("public void ShowMainNavigation()", StringComparison.Ordinal);
        var shellEnd = window.IndexOf("private async Task LoadShellNavigationAsync", shellStart, StringComparison.Ordinal);
        var shell = window[shellStart..shellEnd];
        Assert.Contains("ShowMainNavigation();", transition);
        Assert.DoesNotContain("RunShellWorkAsync(\"theme_sync\"", transition);
        Assert.Contains("GetAuthenticatedShellKey()", shell);
        Assert.Contains("shouldHydrateShell", shell);
        Assert.Contains("RunShellWorkAsync(\"theme_sync\"", shell);
        Assert.DoesNotContain("SyncFromServerAsync", profiles);
    }

    [Fact]
    public void TasteSeedPickerMatchesCurrentPaginationAndSubmissionFlow()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "RecommendationsApi.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "TasteSeedViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "TasteSeedPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "TasteSeedPage.xaml.cs");

        Assert.Contains("/api/v1/recommendations/taste-seed/items", api);
        Assert.Contains("/api/v1/recommendations/taste-seed", api);
        Assert.Contains("MinimumPicks = 3", viewModel);
        Assert.Contains("!item.WasFavorite", viewModel);
        Assert.Contains("ItemsScrollViewer_ViewChanged", page);
        Assert.Contains("LoadMoreAsync", code);
        Assert.Contains("Item.PosterUrl", page);
        Assert.Contains("MaximumRowsOrColumns=\"7\"", page);
        Assert.DoesNotContain("<controls:PosterCard", page);
    }

    [Fact]
    public void NotificationSettingsExposeEveryCurrentDeliveryChannel()
    {
        var settings = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var control = ReadRepoFile("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml.cs");
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "NotificationSettingsViewModel.cs");
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "NotificationsApi.cs");

        Assert.Contains("x:Name=\"NotificationsSettingsTab\"", settings);
        Assert.Contains("New Episode Notifications", control);
        Assert.Contains("Email Notifications", control);
        Assert.Contains("Discord Notifications", control);
        Assert.Contains("Browser Notifications", control);
        Assert.Contains("Webhooks", control);
        Assert.Contains("OpenWebhookDialogAsync", code);
        Assert.Contains("RotateWebhookSecretAsync", viewModel);
        Assert.Contains("DeleteWebPushSubscriptionAsync", viewModel);
        Assert.Contains("/api/v1/notifications/email-preferences", api);
        Assert.Contains("/api/v1/notifications/discord-preferences", api);
        Assert.Contains("/api/v1/notifications/webhooks", api);
        Assert.Contains("/api/v1/notifications/web-push/subscriptions", api);
    }

    [Fact]
    public void NotificationSettingsMatchCurrentWebUiOrderAndImmediateSaveLifecycle()
    {
        var control = ReadRepoFile("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml.cs");

        var episode = control.IndexOf("New Episode Notifications", StringComparison.Ordinal);
        var browser = control.IndexOf("Browser Notifications", StringComparison.Ordinal);
        var email = control.IndexOf("Email Notifications", StringComparison.Ordinal);
        var discord = control.IndexOf("Discord Notifications", StringComparison.Ordinal);
        var webhooks = control.IndexOf("Webhooks", StringComparison.Ordinal);
        Assert.True(episode < browser && browser < email && email < discord && discord < webhooks);

        Assert.Contains("Email this profile's notifications", control);
        Assert.Contains("Link your Discord account", control);
        Assert.Contains("Master switch for this profile", control);
        Assert.Contains("NotificationPreference_Toggled", control);
        Assert.Contains("await ViewModel.SavePreferencesAsync()", code);
        Assert.Contains("await ViewModel.SaveEmailModeAsync()", code);
        Assert.Contains("await ViewModel.SaveDiscordModeAsync()", code);
        Assert.DoesNotContain("Save preferences", control);
        Assert.DoesNotContain("Save frequency", control);
    }

    [Fact]
    public void HomeSectionRecipeFlowExposesCurrentGalleryAndConfigurationFields()
    {
        var settings = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var dialog = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.cs");

        Assert.Contains("RecipeGalleryDialog.ShowAsync", settings);
        Assert.Contains("Search recipes...", dialog);
        Assert.Contains("Library staples", dialog);
        Assert.Contains("Hand-picked", dialog);
        Assert.Contains("Back to gallery", dialog);
        Assert.Contains("Show as featured hero", dialog);
        Assert.Contains("continue_type", dialog);
        Assert.Contains("filter_library_ids", dialog);
        Assert.Contains("enabled_themes", dialog);
        Assert.Contains("rotation_cadence", dialog);
        Assert.Contains("anchor_item_id", dialog);
        Assert.Contains("user_collection_id", dialog);
        Assert.Contains("Choose a synced collection before adding this section.", dialog);
    }

    [Fact]
    public void WebhookSigningSecretUsesCurrentOneTimeRevealDialog()
    {
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "NotificationSettingsControl.xaml.cs");

        Assert.Contains("Save your signing secret", code);
        Assert.Contains("It is shown only once", code);
        Assert.Contains("Clipboard.SetContent", code);
        Assert.Contains("I've saved it", code);
    }

    [Fact]
    public void HistoryImportSummaryIncludesCurrentWatchlistAndFavoriteCounters()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "HistoryImport", "HistoryImportRun.cs");

        Assert.Contains("run.WatchlistAdded", page);
        Assert.Contains("run.FavoritesImported", page);
        Assert.Contains("\"Watchlist\"", page);
        Assert.Contains("\"Favorites\"", page);
        Assert.Contains("FavoritesImported", model);
    }

    [Fact]
    public void SettingsSidebarDoesNotExposeStalePluginAndSessionRoutes()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        Assert.Contains("x:Name=\"PluginsTab\"", xaml);
        Assert.Contains("x:Name=\"SessionsTab\"", xaml);
        Assert.Matches("x:Name=\"PluginsTab\"[\\s\\S]{0,300}Visibility=\"Collapsed\"", xaml);
        Assert.Matches("x:Name=\"SessionsTab\"[\\s\\S]{0,300}Visibility=\"Collapsed\"", xaml);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
