using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

public sealed class ServerContractSourceTests
{
    [Fact]
    public void TrackedFileAndDirectoryNamesUseSiloBranding()
    {
        var root = FindRepositoryRoot();
        var output = TryListTrackedFiles(root)
            ?? string.Join('\n', EnumerateProjectSourcePaths(root));

        var brandedPaths = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Split(['/', '\\']).Any(segment =>
                segment.Contains("continuum", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(brandedPaths);
    }

    private static string? TryListTrackedFiles(string root)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start git.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
                return null;
            }
            Task.WaitAll(outputTask, errorTask);
            return process.ExitCode == 0 ? outputTask.Result : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateProjectSourcePaths(string root)
    {
        string[] topLevelDirectories = ["src", "tests", "installer", "libs"];
        foreach (var directory in topLevelDirectories.Select(name => Path.Combine(root, name)).Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => segment is "bin" or "obj" or "output" or "publish"))
                    continue;
                yield return relative;
            }
        }

        foreach (var file in new[] { "README.md", "LICENSE", "SiloPlayer.sln", ".gitignore" })
        {
            if (File.Exists(Path.Combine(root, file)))
                yield return file;
        }
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
        Assert.Contains("GetDiscoverRowsAsync", viewModel);
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
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var requestsApi = Path.Combine(root, "src", "SiloPlayer.Core", "Api", "RequestsApi.cs");
        var requestModels = Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Requests", "MediaRequests.cs");

        Assert.True(File.Exists(requestsApi));
        Assert.True(File.Exists(requestModels));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "RequestsViewModel.cs")));
        Assert.True(File.Exists(Path.Combine(root, "src", "SiloPlayer", "Views", "RequestsPage.xaml")));

        var api = File.ReadAllText(requestsApi);
        var models = File.ReadAllText(requestModels);
        Assert.Contains("/api/v1/requests/status", api);
        Assert.Contains("/api/v1/requests/discover", api);
        Assert.Contains("/api/v1/requests/search", api);
        Assert.Contains("/api/v1/requests/mine", api);
        Assert.DoesNotContain("/api/v1/admin/requests", api);
        Assert.Contains("RequestFeatureStatus", models);
        Assert.Contains("RequestMediaResult", models);
        Assert.Contains("CreateMediaRequestInput", models);
        Assert.Contains("MediaRequest", models);

        Assert.Contains("RequestsApi", app);
        Assert.Contains("RequestsViewModel", app);
        Assert.DoesNotContain("AdminRequestsViewModel", app);
        Assert.Contains("Content=\"Requests\"", mainWindow);
        Assert.Contains("Tag=\"Requests\"", mainWindow);
        Assert.Contains("Navigate<RequestsPage>", mainWindowCode);
        Assert.Contains("RequestsPage", documentTitle);
    }

    [Fact]
    public void DesktopTracksCurrentSiloPlaybackAndSettingsContracts()
    {
        var root = FindRepositoryRoot();
        var playbackWebSocket = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));
        var playbackRequest = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartRequest.cs"));
        var playbackV3 = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "PlaybackProtocolV3.cs"));
        var playbackResponse = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartResponse.cs"));
        var playbackManager = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Services", "PlaybackManager.cs"));

        Assert.Contains("/api/v1/playback/sessions/", playbackWebSocket);
        Assert.Contains("/control/ws", playbackWebSocket);
        Assert.DoesNotContain("/api/v1/playback/ws/", playbackWebSocket);

        Assert.Contains("PreserveDirectAudioSelection", playbackRequest);
        Assert.Contains("AudioPassthroughCapabilities", playbackRequest);
        Assert.Contains("HdrCapabilityDetails", playbackRequest);
        Assert.Contains("public sealed class PlaybackStartRequestV3", playbackV3);
        Assert.Contains("public int ProtocolVersion { get; set; } = 3", playbackV3);
        Assert.Contains("public string? AudioTrackId", playbackV3);
        Assert.Contains("public int? AudioTrackIndex", playbackV3);
        Assert.Contains("MpvNativePlaybackCapabilities.CreateProtocolV3Profile", playbackManager);
        Assert.Contains("StartPlaybackV3Async(request", playbackManager);
        Assert.DoesNotContain("PreserveDirectAudioSelection = true", playbackManager);
        Assert.Contains("FontBundleUrl", playbackResponse);

        Assert.False(File.Exists(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AdminApi.cs")));
    }

    [Fact]
    public void DesktopModelsExposeCurrentSiloMediaTypesAndProfileFlags()
    {
        var root = FindRepositoryRoot();
        var profileModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Auth", "ProfilesResponse.cs"));
        var watchDetail = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Playback", "WatchDetailResponse.cs"));
        var homeModels = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Home", "HomeSectionsResponse.cs"));
        var detailModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Catalog", "MediaItemDetail.cs"));
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
        Assert.Contains("AutoSkipRecapToggle", settingsXaml);
        Assert.Contains("AutoPlayNextPreviewToggle", settingsXaml);
        Assert.Contains("auto_skip_recap", settingsVm);
        Assert.Contains("auto_play_next_preview", settingsVm);
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
        Assert.DoesNotContain("/api/v1/admin/notifications/server-channels", api);
        Assert.Contains("AppNotification", models);
        Assert.Contains("NotificationPreferences", models);
        Assert.Contains("NotificationCapability", models);
        Assert.Contains("NotificationWebhook", models);

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
        Assert.Contains("Action<int?>? SubtitleDownloaded", dialog);
        Assert.Contains("GetDownloadedSubtitleId(response)", dialog);
        Assert.Contains("Downloads", dialog);
        Assert.Contains("_downloadInProgress", dialog);
        Assert.Contains("ResultsList.IsEnabled = false", dialog);
        Assert.Contains("_lifetimeCts.Token", dialog);
        Assert.Contains("OperationCanceledException", dialog);
    }

    [Fact]
    public void SiloRebrandIsAppliedToUserVisibleShell()
    {
        var root = FindRepositoryRoot();
        var documentTitle = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Helpers", "DocumentTitle.cs"));
        var login = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "LoginPage.xaml"));
        var serverSelect = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ServerSelectPage.xaml"));
        var setup = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "ServerSetupRequiredPage.xaml"));
        var app = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs"));
        var playbackWebSocket = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "PlaybackWebSocket.cs"));
        var manifest = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Package.appxmanifest"));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "SiloInstaller.iss"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("AppName { get; private set; } = \"Silo\"", documentTitle);
        Assert.Contains("SetServerName", documentTitle);
        Assert.Contains("ViewModel.ServerName", login);
        Assert.Contains("Text=\"Silo\"", serverSelect);
        Assert.Contains("Open Silo WebUI", setup);
        Assert.Contains("silo-desktop-", app);
        Assert.DoesNotContain("cont" + "inuum-desktop-", app);
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
        var project = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "SiloPlayer.csproj"));

        Assert.True(File.Exists(Path.Combine(assets, "silo-icon-1024.png")));
        Assert.True(File.Exists(Path.Combine(assets, "silo-wordmark-sidebar.png")));
        Assert.True(File.Exists(Path.Combine(assets, "silo-mark-transparent.png")));
        Assert.True(new FileInfo(Path.Combine(assets, "app.ico")).Length > 10000);
        Assert.Contains("silo-wordmark-sidebar.png", mainWindow);
        Assert.DoesNotContain("&#x25B6;", mainWindow);
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

        Assert.Contains("CardOverlayPrefs", service);
        Assert.Contains("version", service);
        Assert.Contains("items", service);
        Assert.Contains("resolution_hdr", service);
        Assert.Contains("audio_channels", service);
        Assert.Contains("video_codec", service);
        Assert.Contains("content_rating", service);
        Assert.Contains("show_status", service);
        Assert.Contains("SuppressesStandaloneOverlays", service);

    }

    [Fact]
    public void ProfilesSettingsExposeHouseholdActiveStreams()
    {
        var root = FindRepositoryRoot();
        var authApi = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Api", "AuthApi.cs"));
        var settingsPage = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        var session = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Models", "Sessions", "PlaybackSessionSummary.cs"));

        Assert.Contains("/api/v1/profiles/household/sessions", authApi);
        Assert.Contains("LoadHouseholdSessionsAsync", settingsPage);
        Assert.Contains("HouseholdStreamsPanel", settingsXaml);
        Assert.Contains("EpisodeName", session);
        Assert.Contains("HasPlaybackControl", session);
    }

    [Fact]
    public void SpokenLanguageMatchesCurrentSiloSettingsSurface()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml"));
        var settingsCode = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.xaml.cs"));
        var settingsViewModel = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "ViewModels", "SettingsViewModel.cs"));

        Assert.Contains("Spoken language", settingsXaml);
        Assert.Contains("Prefer a spoken language for this profile when multiple tracks are available.", settingsXaml);
        Assert.Contains("PopulateLanguageCombo(SpokenLanguageComboBox, \"No preference\")", settingsCode);
        Assert.Contains("MediaLanguageCatalog.All", settingsCode);
        Assert.Contains("MediaLanguageCatalog.Label(code)", settingsViewModel);
        Assert.DoesNotContain("private static readonly Dictionary<string, string> LanguageNames", settingsViewModel);
        Assert.Contains("(\"original\", \"Original Language\")", settingsCode);
        Assert.Contains("SaveAudioLanguageCommand", settingsCode);
        Assert.Contains("SaveContractProfileSettingAsync(PlaybackAudioLanguageSettingKey", settingsViewModel);
        Assert.Contains("catalog.metadata_language", settingsViewModel);
        Assert.Contains("catalog.metadata_language_overrides", settingsViewModel);
        Assert.Contains("Metadata language", settingsXaml);
        Assert.Contains("Preferred audio language", File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "SettingsPage.Account.cs")));
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
        var failureDescriptions = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SiloPlayer.Core",
            "Services",
            "PlaybackFailureDescription.cs"));

        var legacyBrand = "Cont" + "inuum";
        Assert.DoesNotContain(legacyBrand + " could not start playback", source + failureDescriptions);
        Assert.DoesNotContain(legacyBrand + " could not find", source + failureDescriptions);
        Assert.Contains("Silo could not start playback", failureDescriptions);
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
