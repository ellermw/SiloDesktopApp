namespace SiloPlayer.Tests;

public sealed class CurrentSettingsParitySourceTests
{
    [Fact]
    public void SettingsShellMatchesCurrentWideSearchableFourGroupNavigation()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("MaxWidth=\"1424\"", xaml);
        Assert.Contains("x:Name=\"SettingsSearchBox\"", xaml);
        Assert.Contains("14 settings sections", xaml);
        Assert.Contains("x:Name=\"AppearanceNavGroup\"", xaml);
        Assert.Matches("AppearanceNavGroup[\\s\\S]+CardOverlaysTab[\\s\\S]+PersonalizeTab[\\s\\S]+LibraryDataNavGroup", xaml);
        Assert.Contains("SettingsSearchBox_TextChanged", code);
        Assert.Contains("SettingsPage_SizeChanged", code);
        Assert.Contains("e.NewSize.Width < 1024", code);
        Assert.Contains("SettingsNavigationGroups.Orientation", code);
        Assert.Contains("SettingsContentPanel.MaxWidth", code);
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
        Assert.Contains("CardOverlayPresetCombo_SelectionChanged", page);
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
        Assert.Contains("PutDeviceSettingAsync(\"subtitle_appearance\"", viewModel);
        Assert.Contains("DeleteDeviceSettingAsync(\"subtitle_appearance\"", viewModel);
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
        Assert.Matches("MoveSectionUp\\(section\\);[\\s\\S]{0,160}SaveHomeSectionsCommand", page);
        Assert.Matches("ToggleSectionVisibility\\(section\\);[\\s\\S]{0,160}SaveHomeSectionsCommand", page);
    }

    [Fact]
    public void ProfileThemeIsSynchronizedBeforeTheAuthenticatedUiAppears()
    {
        var service = ReadRepoFile("src", "SiloPlayer", "Services", "ThemeService.cs");
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs");
        var profiles = ReadRepoFile("src", "SiloPlayer", "ViewModels", "ProfileSelectViewModel.cs");

        Assert.Contains("SyncFromServerAsync", service);
        Assert.Contains("GetSettingAsync(\"ui_theme\"", service);
        Assert.Contains("GetServerBrandingAsync", service);
        Assert.Contains("GetSettingAsync(\"ui_custom_theme_vars\"", service);
        Assert.Contains("await _themeService.SyncFromServerAsync(cancellationToken);", window);
        Assert.True(
            window.IndexOf("await _themeService.SyncFromServerAsync(cancellationToken);", StringComparison.Ordinal) <
            window.IndexOf("ShowMainNavigation();", StringComparison.Ordinal));
        Assert.Contains("await _themeService.SyncFromServerAsync();", profiles);
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
        Assert.Contains("PosterCard", page);
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
