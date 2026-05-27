using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Tests;

public sealed class ServerContractSourceTests
{
    [Fact]
    public void FileVersionExposesPerVersionMarkers()
    {
        Assert.NotNull(typeof(FileVersion).GetProperty("Intro"));
        Assert.NotNull(typeof(FileVersion).GetProperty("Credits"));
    }

    [Fact]
    public void LibraryExposesIntroDetectionFlag()
    {
        Assert.NotNull(typeof(Library).GetProperty("IntroDetectionEnabled"));
    }

    [Fact]
    public void AdminLibrariesEditorSavesIntroDetectionFlag()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Views",
            "Admin",
            "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("Detect intro markers", source);
        Assert.Contains("intro_detection_enabled", source);
    }

    [Fact]
    public void RecommendationsUseEnrichedDiscoverEndpoint()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "RecommendationsApi.cs"));
        var models = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Catalog", "RecommendationsResponse.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "RecommendationsViewModel.cs"));

        Assert.Contains("/api/v1/recommendations/discover", api);
        Assert.Contains("/api/v1/recommendations/section/", api);
        Assert.Contains("DiscoverResponse", models);
        Assert.Contains("SectionKind", models);
        Assert.Contains("LoadDiscoverRowsAsync", viewModel);
        Assert.Contains("GetDiscoverAsync", viewModel);
    }

    [Fact]
    public void RecommendationsExposeDedicatedSectionPage()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "App.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Helpers", "DocumentTitle.cs"));
        var recommendationsPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "RecommendationsPage.xaml.cs"));
        var sectionPage = Path.Combine(root, "src", "ContinuumPlayer", "Views", "RecommendationSectionPage.xaml.cs");
        var sectionViewModel = Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "RecommendationSectionViewModel.cs");

        Assert.True(File.Exists(sectionPage));
        Assert.True(File.Exists(sectionViewModel));
        Assert.Contains("RecommendationSectionViewModel", app);
        Assert.Contains("RecommendationSectionPage", documentTitle);
        Assert.Contains("Navigate<RecommendationSectionPage>", recommendationsPage);
        Assert.Contains("SectionKind", recommendationsPage);
    }

    [Fact]
    public void LibraryAndSectionFiltersExposeEpisodeScope()
    {
        var root = FindRepositoryRoot();
        var libraryXaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "LibraryPage.xaml"));
        var libraryPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "LibraryPage.xaml.cs"));
        var adminSections = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminSectionsPage.xaml.cs"));

        Assert.Contains("Tag=\"episode\"", libraryXaml);
        Assert.Contains("\"episode\" => 3", libraryPage);
        Assert.Contains("Episodes", adminSections);
        Assert.Contains("Tag = \"episode\"", adminSections);
    }

    [Fact]
    public void AuthProvidersExposeOAuthPluginMetadataAndCompletionFlow()
    {
        var root = FindRepositoryRoot();
        var providerModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Auth", "AuthProvider.cs"));
        var authApi = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "AuthApi.cs"));
        var loginViewModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "LoginViewModel.cs"));
        var loginPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "LoginPage.xaml.cs"));

        Assert.Contains("IconUrl", providerModel);
        Assert.Contains("InstallationId", providerModel);
        Assert.Contains("List<AuthProvider>", authApi);
        Assert.Contains("/api/v1/auth/oauth/", authApi);
        Assert.Contains("/init", authApi);
        Assert.Contains("/api/v1/auth/oauth/complete", authApi);
        Assert.Contains("CompleteOAuthAsync", loginViewModel);
        Assert.Contains("WebView2", loginPage);
        Assert.DoesNotContain("/api/v1/auth/providers/{Uri.EscapeDataString(providerId)}/authorize", loginPage);
    }

    [Fact]
    public void PluginGlobalConfigUsesCurrentServerEndpoint()
    {
        var api = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer.Core",
            "Api",
            "PluginsApi.cs"));

        Assert.Contains("/api/v1/admin/plugins/installations/{installationId}/config", api);
        Assert.DoesNotContain("/config/{Uri.EscapeDataString(request.Key)}", api);
    }

    [Fact]
    public void SubtitleSearchCarriesServerMetadataThroughDownload()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "PlaybackApi.cs"));
        var dialog = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Controls", "SubtitleSearchDialog.xaml.cs"));

        Assert.Contains("public List<string> Warnings { get; set; }", api);
        Assert.Contains("public int Downloads { get; set; }", api);
        Assert.Contains("DownloadSubtitleAsync(int mediaFileId, SubtitleSearchResult result", api);
        Assert.Contains("""["release_name"] = result.ReleaseName""", api);
        Assert.Contains("""["score"] = result.Score""", api);
        Assert.Contains("""["hearing_impaired"] = result.HearingImpaired""", api);
        Assert.Contains("SplitReleaseNames", dialog);
        Assert.Contains("Downloads", dialog);
    }

    [Fact]
    public void SubtitleUploadAndAdminManagementMatchCurrentSiloSurface()
    {
        var root = FindRepositoryRoot();
        var playbackApi = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "PlaybackApi.cs"));
        var adminApi = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "AdminApi.cs"));
        var dialogXaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Controls", "SubtitleSearchDialog.xaml"));
        var dialogCode = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Controls", "SubtitleSearchDialog.xaml.cs"));
        var adminShellXaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var adminShellCode = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminShellPage.xaml.cs"));
        var itemDetail = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Helpers", "DocumentTitle.cs"));

        Assert.Contains("/api/v1/subtitles/upload", playbackApi);
        Assert.Contains("/api/v1/subtitles/detect-language", playbackApi);
        Assert.Contains("UploadSubtitleAsync", playbackApi);
        Assert.Contains("DetectSubtitleLanguageAsync", playbackApi);
        Assert.Contains("Upload subtitle", dialogXaml);
        Assert.Contains("BrowseUploadButton_Click", dialogCode);
        Assert.Contains("UploadButton_Click", dialogCode);
        Assert.Contains("Add subtitles...", itemDetail);

        Assert.True(File.Exists(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml.cs")));
        Assert.Contains("/api/v1/admin/subtitles", adminApi);
        Assert.Contains("GetDownloadedSubtitlesAsync", adminApi);
        Assert.Contains("UpdateDownloadedSubtitleAsync", adminApi);
        Assert.Contains("DownloadDownloadedSubtitleAsync", adminApi);
        Assert.Contains("DeleteDownloadedSubtitleAsync", adminApi);
        Assert.Contains("NavSubtitles", adminShellXaml);
        Assert.Contains("AdminSubtitlesPage", adminShellCode);
        Assert.Contains("Admin · Subtitles", documentTitle);
    }

    [Fact]
    public void SiloRebrandIsAppliedToUserVisibleShell()
    {
        var root = FindRepositoryRoot();
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Helpers", "DocumentTitle.cs"));
        var login = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "LoginPage.xaml"));
        var serverSelect = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "ServerSelectPage.xaml"));
        var setup = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SetupWizardPage.xaml"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Package.appxmanifest"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "ContinuumDesktopPlayer.iss"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("AppName = \"Silo\"", documentTitle);
        Assert.Contains("Text=\"Silo\"", login);
        Assert.Contains("Text=\"Silo\"", serverSelect);
        Assert.Contains("Text=\"Silo\"", setup);
        Assert.Contains("<DisplayName>Silo</DisplayName>", manifest);
        Assert.Contains("DisplayName=\"Silo\"", manifest);
        Assert.Contains("#define MyAppName \"Silo Desktop Player\"", installer);
        Assert.Contains("# Silo Desktop Player", readme);
    }

    [Fact]
    public void DesktopBrandingUsesSiloLogoAssets()
    {
        var root = FindRepositoryRoot();
        var assets = Path.Combine(root, "src", "ContinuumPlayer", "Assets");
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "MainWindow.xaml"));
        var adminShell = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var project = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ContinuumPlayer.csproj"));

        Assert.True(File.Exists(Path.Combine(assets, "silo-icon-1024.png")));
        Assert.True(File.Exists(Path.Combine(assets, "silo-wordmark-sidebar.png")));
        Assert.True(File.Exists(Path.Combine(assets, "silo-mark-transparent.png")));
        Assert.True(new FileInfo(Path.Combine(assets, "app.ico")).Length > 10000);
        Assert.Contains("silo-wordmark-sidebar.png", mainWindow);
        Assert.Contains("silo-wordmark-sidebar.png", adminShell);
        Assert.DoesNotContain("&#x25B6;", mainWindow);
        Assert.DoesNotContain("&#x25B6;", adminShell);
        Assert.Contains(@"Assets\silo-icon-1024.png", project);
        Assert.Contains(@"Assets\silo-wordmark-sidebar.png", project);
        Assert.Contains(@"Assets\silo-mark-transparent.png", project);
    }

    [Fact]
    public void CatalogModelsExposeExpandedOverlayAndShowMetadata()
    {
        var root = FindRepositoryRoot();
        var homeModels = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs"));
        var detailModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs"));

        Assert.Contains("public int Runtime", homeModels);
        Assert.Contains("public List<string> Studios", homeModels);
        Assert.Contains("public List<string> Networks", homeModels);
        Assert.Contains("public string? ContentRating", homeModels);
        Assert.Contains("public string? ShowStatus", homeModels);
        Assert.Contains("public string AudioChannels", homeModels);
        Assert.Contains("public string VideoCodec", homeModels);
        Assert.Contains("public string Container", homeModels);
        Assert.Contains("public string AspectRatio", homeModels);
        Assert.Contains("public string Edition", homeModels);
        Assert.Contains("public bool MultiAudio", homeModels);
        Assert.Contains("public bool MultiSub", homeModels);
        Assert.Contains("public string? ShowStatus", detailModel);
    }

    [Fact]
    public void CardOverlayServiceSupportsServerV2PrefsAndNewOverlayTypes()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Services", "CardOverlayService.cs"));
        var adminSettings = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("CardOverlayPrefs", service);
        Assert.Contains("version", service);
        Assert.Contains("items", service);
        Assert.Contains("resolution_hdr", service);
        Assert.Contains("audio_channels", service);
        Assert.Contains("video_codec", service);
        Assert.Contains("content_rating", service);
        Assert.Contains("show_status", service);
        Assert.Contains("SuppressesStandaloneOverlays", service);

        Assert.Contains("resolution_hdr", adminSettings);
        Assert.Contains("audio_channels", adminSettings);
        Assert.Contains("video_codec", adminSettings);
        Assert.Contains("content_rating", adminSettings);
        Assert.Contains("show_status", adminSettings);
        Assert.Contains("\"version\":2", adminSettings);
    }

    [Fact]
    public void ProfilesSettingsExposeHouseholdActiveStreams()
    {
        var root = FindRepositoryRoot();
        var authApi = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Api", "AuthApi.cs"));
        var settingsPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml"));
        var adminSession = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer.Core", "Models", "Admin", "AdminSession.cs"));

        Assert.Contains("/api/v1/profiles/household/sessions", authApi);
        Assert.Contains("LoadHouseholdSessionsAsync", settingsPage);
        Assert.Contains("HouseholdStreamsPanel", settingsXaml);
        Assert.Contains("EpisodeName", adminSession);
        Assert.Contains("HasPlaybackControl", adminSession);
    }

    [Fact]
    public void PreferredAudioLanguageMatchesCurrentSiloSettingsSurface()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml"));
        var settingsCode = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("Preferred audio language", settingsXaml);
        Assert.Contains("Choose which audio track language to prefer when a file offers more than one.", settingsXaml);
        Assert.Contains("Content=\"No preference\" Tag=\"\"", settingsXaml);
        Assert.Contains("Content=\"Original\" Tag=\"original\"", settingsXaml);
        Assert.Contains("(\"original\", \"Original\")", settingsCode);
        Assert.Contains("SaveAudioLanguageCommand", settingsCode);
        Assert.Contains("playback.audio_language", settingsViewModel);
        Assert.Contains("PutDeviceSettingAsync(PlaybackAudioLanguageSettingKey", settingsViewModel);
        Assert.DoesNotContain("Spoken language", settingsXaml + settingsCode);
        Assert.DoesNotContain("Original Language", settingsXaml + settingsCode + settingsViewModel);
    }

    [Fact]
    public void AdminPlaybackHistoryRowsLinkToMediaUsersAndProfileFilters()
    {
        var root = FindRepositoryRoot();
        var historyPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminPlaybackHistoryPage.xaml.cs"));
        var userDetailPage = File.ReadAllText(Path.Combine(root, "src", "ContinuumPlayer", "Views", "Admin", "AdminUserDetailPage.xaml.cs"));

        Assert.Contains("AdminPlaybackHistoryFilter", historyPage);
        Assert.Contains("NavigateToItem", historyPage);
        Assert.Contains("NavigateToUser", historyPage);
        Assert.Contains("NavigateToProfileHistory", historyPage);
        Assert.Contains("AdminPlaybackHistoryFilter", userDetailPage);
        Assert.Contains("NavigateToItem", userDetailPage);
        Assert.Contains("NavigateToProfileHistory", userDetailPage);
    }

    [Fact]
    public void PlaybackErrorsUseSiloAndHandleStreamLimits()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ContinuumPlayer",
            "Services",
            "PlayerService.cs"));

        Assert.DoesNotContain("Continuum could not start playback", source);
        Assert.DoesNotContain("Continuum could not find", source);
        Assert.Contains("Silo could not start playback", source);
        Assert.Contains("too_many_streams", source);
        Assert.Contains("too_many_transcodes", source);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "ContinuumPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
