using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class ServerContractSourceTests
{
    [Fact]
    public void TrackedFileAndDirectoryNamesUseSiloBranding()
    {
        var root = FindRepositoryRoot();
        var startInfo = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        var brandedPaths = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Split(['/', '\\']).Any(segment =>
                segment.Contains("continuum", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(brandedPaths);
    }

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
            "SiloPlayer",
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
        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "RecommendationsApi.cs"));
        var models = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Catalog", "RecommendationsResponse.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "RecommendationsViewModel.cs"));

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
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var recommendationsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "RecommendationsPage.xaml.cs"));
        var sectionPage = Path.Combine(root, "src", "SiloPlayer", "Views", "RecommendationSectionPage.xaml.cs");
        var sectionViewModel = Path.Combine(root, "src", "SiloPlayer", "ViewModels", "RecommendationSectionViewModel.cs");

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
        var libraryXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LibraryPage.xaml"));
        var libraryPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LibraryPage.xaml.cs"));
        var adminSections = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSectionsPage.xaml.cs"));

        Assert.Contains("Tag=\"episode\"", libraryXaml);
        Assert.Contains("\"episode\" => 3", libraryPage);
        Assert.Contains("Episodes", adminSections);
        Assert.Contains("Tag = \"episode\"", adminSections);
    }

    [Fact]
    public void AuthProvidersExposeOAuthPluginMetadataAndCompletionFlow()
    {
        var root = FindRepositoryRoot();
        var providerModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Auth", "AuthProvider.cs"));
        var authApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AuthApi.cs"));
        var loginViewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "LoginViewModel.cs"));
        var loginPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LoginPage.xaml.cs"));

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
    public void LoginSupportsCurrentSiloCredentialAndOAuthProviderSurface()
    {
        var root = FindRepositoryRoot();
        var loginRequest = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Auth", "LoginRequest.cs"));
        var authApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AuthApi.cs"));
        var authService = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "AuthService.cs"));
        var loginViewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "LoginViewModel.cs"));
        var loginXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LoginPage.xaml"));

        Assert.Contains("public string? Provider", loginRequest);
        Assert.Contains("LoginAsync(string username, string password, string? provider", authApi);
        Assert.Contains("LoginAsync(string username, string password, string? provider", authService);
        Assert.Contains("CredentialProviders", loginViewModel);
        Assert.Contains("SelectedCredentialProvider", loginViewModel);
        Assert.Contains("SelectedCredentialProvider?.Id", loginViewModel);
        Assert.Contains("OAuthProviders", loginViewModel);
        Assert.Contains("HasOAuthProviders", loginViewModel);
        Assert.Contains("CredentialProviderComboBox", loginXaml);
        Assert.Contains("IconUrl", loginXaml);
    }

    [Fact]
    public void DesktopExposesCurrentSiloMediaRequestsSurface()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var mainWindowCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var adminShell = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var adminShellCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var requestsApi = Path.Combine(root, "src", "SiloPlayer.Core", "Api", "RequestsApi.cs");
        var requestModels = Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Requests", "MediaRequests.cs");

        Assert.True(File.Exists(requestsApi));
        Assert.True(File.Exists(requestModels));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "RequestsViewModel.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "RequestsPage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminRequestsPage.xaml")));

        var api = File.ReadAllText(requestsApi);
        var models = File.ReadAllText(requestModels);
        Assert.Contains("/api/v1/requests/status", api);
        Assert.Contains("/api/v1/requests/discover", api);
        Assert.Contains("/api/v1/requests/search", api);
        Assert.Contains("/api/v1/requests/mine", api);
        Assert.Contains("/api/v1/admin/requests", api);
        Assert.Contains("RequestFeatureStatus", models);
        Assert.Contains("RequestMediaResult", models);
        Assert.Contains("CreateMediaRequestInput", models);
        Assert.Contains("MediaRequest", models);

        Assert.Contains("RequestsApi", app);
        Assert.Contains("RequestsViewModel", app);
        Assert.Contains("AdminRequestsViewModel", app);
        Assert.Contains("Content=\"Requests\"", mainWindow);
        Assert.Contains("Tag=\"Requests\"", mainWindow);
        Assert.Contains("Navigate<RequestsPage>", mainWindowCode);
        Assert.Contains("NavRequests", adminShell);
        Assert.Contains("AdminRequestsPage", adminShellCode);
        Assert.Contains("AdminRequestsPage", documentTitle);
        Assert.Contains("RequestsPage", documentTitle);
    }

    [Fact]
    public void DesktopTracksCurrentSiloPlaybackAndSettingsContracts()
    {
        var root = FindRepositoryRoot();
        var playbackWebSocket = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));
        var playbackRequest = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartRequest.cs"));
        var playbackResponse = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartResponse.cs"));
        var playbackManager = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "PlaybackManager.cs"));
        var adminApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
        var adminSettingsVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "Admin", "AdminSettingsDetailViewModel.cs"));
        var adminSettingsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("/api/v1/playback/sessions/", playbackWebSocket);
        Assert.Contains("/control/ws", playbackWebSocket);
        Assert.DoesNotContain("/api/v1/playback/ws/", playbackWebSocket);

        Assert.Contains("PreserveDirectAudioSelection", playbackRequest);
        Assert.Contains("AudioPassthroughCapabilities", playbackRequest);
        Assert.Contains("HdrCapabilityDetails", playbackRequest);
        Assert.Contains("PreserveDirectAudioSelection = true", playbackManager);
        Assert.Contains("FontBundleUrl", playbackResponse);

        Assert.Contains("Task<AdminSettingUpdateResponse> UpdateAdminSettingAsync", adminApi);
        Assert.Contains("RestartRequired", adminApi);
        Assert.Contains("LastSaveRequiresRestart", adminSettingsVm);
        Assert.DoesNotContain("RestartRequiredKeys", adminSettingsPage);
    }

    [Fact]
    public void DesktopModelsExposeCurrentSiloMediaTypesAndProfileFlags()
    {
        var root = FindRepositoryRoot();
        var profileModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Auth", "ProfilesResponse.cs"));
        var watchDetail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "WatchDetailResponse.cs"));
        var homeModels = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs"));
        var detailModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs"));
        var adminLibraries = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        var settingsVm = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("AutoSkipRecap", profileModel);
        Assert.Contains("AutoPlayNextPreview", profileModel);
        Assert.Contains("Recap", watchDetail);
        Assert.Contains("Preview", watchDetail);
        Assert.Contains("Audiobook", detailModel);
        Assert.Contains("Ebook", detailModel);
        Assert.Contains("Audiobook", homeModels);
        Assert.Contains("Ebook", homeModels);
        Assert.Contains("Tag = \"audiobooks\"", adminLibraries);
        Assert.Contains("Tag = \"ebooks\"", adminLibraries);
        Assert.Contains("Tag = \"podcasts\"", adminLibraries);
        Assert.Contains("AutoSkipRecapToggle", settingsXaml);
        Assert.Contains("AutoPlayNextPreviewToggle", settingsXaml);
        Assert.Contains("auto_skip_recap", settingsVm);
        Assert.Contains("auto_play_next_preview", settingsVm);
    }

    [Fact]
    public void DesktopRequestsAndNodesExposeCurrentSiloFields()
    {
        var root = FindRepositoryRoot();
        var requestModels = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Requests", "MediaRequests.cs"));
        var requestsApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "RequestsApi.cs"));
        var nodeModels = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Admin", "AdminNode.cs"));
        var nodesPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminNodesPage.xaml.cs"));

        Assert.Contains("DiscoverBrandCard", requestModels);
        Assert.Contains("RequestTarget", requestModels);
        Assert.Contains("RequestIntegration", requestModels);
        Assert.Contains("GetDiscoverStudiosAsync", requestsApi);
        Assert.Contains("GetAdminRequestSettingsAsync", requestsApi);
        Assert.Contains("Group", nodeModels);
        Assert.Contains("MaxJobs", nodeModels);
        Assert.Contains("MaxBandwidthKbps", nodeModels);
        Assert.Contains("EgressKbps", nodeModels);
        Assert.Contains("NodeFormResult", nodesPage);
    }

    [Fact]
    public void DesktopTracksCurrentSiloNotificationsSurface()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var mainWindowCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var eventChannel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "EventChannelClient.cs"));
        var notificationApi = Path.Combine(root, "src", "SiloPlayer.Core", "Api", "NotificationsApi.cs");
        var notificationModels = Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Notifications", "NotificationsModels.cs");

        Assert.True(File.Exists(notificationApi));
        Assert.True(File.Exists(notificationModels));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "NotificationsViewModel.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "NotificationsPage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "NotificationsPage.xaml.cs")));

        var api = File.ReadAllText(notificationApi);
        var models = File.ReadAllText(notificationModels);

        Assert.Contains("/api/v1/notifications?", api);
        Assert.Contains("/api/v1/notifications/unread-count", api);
        Assert.Contains("/api/v1/notifications/preferences", api);
        Assert.Contains("/api/v1/notifications/email-preferences", api);
        Assert.Contains("/api/v1/notifications/discord-preferences", api);
        Assert.Contains("/api/v1/notifications/webhooks", api);
        Assert.Contains("/api/v1/admin/notifications/server-channels", api);
        Assert.Contains("AppNotification", models);
        Assert.Contains("NotificationPreferences", models);
        Assert.Contains("NotificationCapability", models);
        Assert.Contains("NotificationWebhook", models);
        Assert.Contains("ServerNotificationChannel", models);

        Assert.Contains("NotificationsApi", app);
        Assert.Contains("NotificationsViewModel", app);
        Assert.Contains("Content=\"Notifications\"", mainWindow);
        Assert.Contains("Tag=\"Notifications\"", mainWindow);
        Assert.Contains("Navigate<NotificationsPage>", mainWindowCode);
        Assert.Contains("NotificationsPage", documentTitle);
        Assert.Contains("/api/v1/events/ws-ticket", eventChannel);
        Assert.Contains("ticket=", eventChannel);
    }

    [Fact]
    public void AdminSettingsTracksCurrentSiloWebUiSections()
    {
        var root = FindRepositoryRoot();
        var adminSettingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml"));
        var adminSettings = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));

        Assert.Contains("SettingsSearchBox", adminSettingsXaml);
        Assert.Contains("SettingsSearchBox_TextChanged", adminSettings);
        Assert.Contains("\"Intro Markers\"", adminSettings);
        Assert.Contains("\"Subtitles\"", adminSettings);
        Assert.Contains("\"AI Services\"", adminSettings);
        Assert.Contains("\"Watch Providers\"", adminSettings);
        Assert.Contains("\"Email\"", adminSettings);
        Assert.Contains("\"Notifications\"", adminSettings);

        Assert.Contains("BuildIntroMarkersTab", adminSettings);
        Assert.Contains("BuildSubtitlesTab", adminSettings);
        Assert.Contains("BuildAIServicesTab", adminSettings);
        Assert.Contains("BuildWatchProvidersTab", adminSettings);
        Assert.Contains("BuildEmailTab", adminSettings);
        Assert.Contains("BuildNotificationsAdminTab", adminSettings);

        Assert.Contains("markers.mode", adminSettings);
        Assert.Contains("email.smtp_host", adminSettings);
        Assert.Contains("ai.base_url", adminSettings);
        Assert.Contains("subtitle_ai.transcribe_enabled", adminSettings);
        Assert.Contains("watchsync.trakt.client_id", adminSettings);
        Assert.Contains("notifications.release_events_enabled", adminSettings);
        Assert.Contains("notifications.server_channels_enabled", adminSettings);
    }

    [Fact]
    public void EpisodeCardsUseFullWidthStillArtwork()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer",
            "Views",
            "ItemDetailPage.xaml.cs"));

        Assert.Contains("EpisodeStillDecodeWidth", source);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Stretch", source);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Stretch", source);
        Assert.DoesNotContain("Width = 160", source);
        Assert.DoesNotContain("Height = 90", source);
    }

    [Fact]
    public void ReleaseArtifactsUseSiloPlayerNames()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "SiloPlayer.csproj"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "SiloInstaller.iss"));
        var build = File.ReadAllText(Path.Combine(root, "installer", "build.ps1"));

        Assert.Contains("<AssemblyName>SiloPlayer</AssemblyName>", project);
        Assert.Contains("#define MyAppExeName \"SiloPlayer.exe\"", installer);
        Assert.Contains("OutputBaseFilename=SiloInstaller-", installer);
        var legacyInstallerPrefix = "Cont" + "inuumDesktopPlayer";
        Assert.DoesNotContain(legacyInstallerPrefix, installer);
        Assert.DoesNotContain(legacyInstallerPrefix + ".iss", build);
        Assert.Contains("SiloInstaller.iss", build);
    }

    [Fact]
    public void PluginGlobalConfigUsesCurrentServerEndpoint()
    {
        var api = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Core",
            "Api",
            "PluginsApi.cs"));

        Assert.Contains("/api/v1/admin/plugins/installations/{installationId}/config", api);
        Assert.DoesNotContain("/config/{Uri.EscapeDataString(request.Key)}", api);
    }

    [Fact]
    public void SubtitleSearchCarriesServerMetadataThroughDownload()
    {
        var root = FindRepositoryRoot();
        var api = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "PlaybackApi.cs"));
        var dialog = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "SubtitleSearchDialog.xaml.cs"));

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
        var playbackApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "PlaybackApi.cs"));
        var adminApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));
        var dialogXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "SubtitleSearchDialog.xaml"));
        var dialogCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Controls", "SubtitleSearchDialog.xaml.cs"));
        var adminShellXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var adminShellCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs"));
        var itemDetail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs"));
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));

        Assert.Contains("/api/v1/subtitles/upload", playbackApi);
        Assert.Contains("/api/v1/subtitles/detect-language", playbackApi);
        Assert.Contains("UploadSubtitleAsync", playbackApi);
        Assert.Contains("DetectSubtitleLanguageAsync", playbackApi);
        Assert.Contains("Upload subtitle", dialogXaml);
        Assert.Contains("BrowseUploadButton_Click", dialogCode);
        Assert.Contains("UploadButton_Click", dialogCode);
        Assert.Contains("Add subtitles...", itemDetail);

        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml.cs")));
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
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var login = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LoginPage.xaml"));
        var serverSelect = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ServerSelectPage.xaml"));
        var setup = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SetupWizardPage.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var adminHistoryImport = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml"));
        var adminHistoryImportCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml.cs"));
        var adminPlugins = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml"));
        var adminSettings = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));
        var playbackWebSocket = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Package.appxmanifest"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "SiloInstaller.iss"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("AppName { get; private set; } = \"Silo\"", documentTitle);
        Assert.Contains("SetServerName", documentTitle);
        Assert.Contains("Text=\"Silo\"", login);
        Assert.Contains("Text=\"Silo\"", serverSelect);
        Assert.Contains("Text=\"Silo\"", setup);
        Assert.Contains("silo-desktop-", app);
        Assert.DoesNotContain("cont" + "inuum-desktop-", app);
        Assert.Contains("PlaceholderText=\"silo/prod\"", setup);
        Assert.Contains("PlaceholderText=\"silo/internal\"", setup);
        Assert.Contains("Silo user profiles", adminHistoryImport);
        Assert.Contains("Silo User", adminHistoryImport);
        Assert.Contains("Silo User", adminHistoryImportCode);
        Assert.Contains("Extend Silo with", adminPlugins);
        Assert.Contains("Stores non-public Silo objects", adminSettings);
        Assert.Contains("[\"name\"] = \"silo-desktop\"", playbackWebSocket);
        Assert.DoesNotContain("[\"name\"] = \"" + "cont" + "inuum-desktop\"", playbackWebSocket);
        Assert.Contains("<DisplayName>Silo</DisplayName>", manifest);
        Assert.Contains("DisplayName=\"Silo\"", manifest);
        Assert.Contains("#define MyAppName \"Silo Desktop Player\"", installer);
        Assert.Contains("# Silo Desktop Player", readme);
    }

    [Fact]
    public void DesktopBrandingUsesSiloLogoAssets()
    {
        var root = FindRepositoryRoot();
        var assets = Path.Combine(root, "src", "SiloPlayer", "Assets");
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml"));
        var adminShell = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var project = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "SiloPlayer.csproj"));

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
        var homeModels = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs"));
        var detailModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs"));

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
        var service = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "CardOverlayService.cs"));
        var adminSettings = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));

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
        var authApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AuthApi.cs"));
        var settingsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        var adminSession = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Admin", "AdminSession.cs"));

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
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        var settingsCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("Preferred audio language", settingsXaml);
        Assert.Contains("Choose which audio track language to prefer when a file offers more than one.", settingsXaml);
        Assert.Contains("Content=\"No preference\" Tag=\"\"", settingsXaml);
        Assert.Contains("Content=\"Original\" Tag=\"original\"", settingsXaml);
        Assert.Contains("(\"original\", \"Original\")", settingsCode);
        Assert.Contains("SaveAudioLanguageCommand", settingsCode);
        Assert.Contains("SaveProfileFieldAsync(\"language\", AudioLanguage)", settingsViewModel);
        Assert.Contains("preferred_metadata_language", settingsViewModel);
        Assert.Contains("Metadata language", settingsXaml);
        Assert.DoesNotContain("Spoken language", settingsXaml + settingsCode);
        Assert.DoesNotContain("Original Language", settingsXaml + settingsCode + settingsViewModel);
    }

    [Fact]
    public void AdminPlaybackHistoryRowsLinkToMediaUsersAndProfileFilters()
    {
        var root = FindRepositoryRoot();
        var historyPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminPlaybackHistoryPage.xaml.cs"));
        var userDetailPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminUserDetailPage.xaml.cs"));

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
            "SiloPlayer",
            "Services",
            "PlayerService.cs"));

        var legacyBrand = "Cont" + "inuum";
        Assert.DoesNotContain(legacyBrand + " could not start playback", source);
        Assert.DoesNotContain(legacyBrand + " could not find", source);
        Assert.Contains("Silo could not start playback", source);
        Assert.Contains("too_many_streams", source);
        Assert.Contains("too_many_transcodes", source);
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
