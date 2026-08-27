namespace SiloPlayer.Tests;

public sealed class CurrentServerContractDeltaTests
{
    [Fact]
    public void PersonalCatalogPagesConsumeTheCurrentHasMorePaginationContract()
    {
        var model = Read("src", "SiloPlayer.Core", "Models", "Catalog", "ItemListResponse.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "CatalogApi.cs");
        var favorites = Read("src", "SiloPlayer", "ViewModels", "FavoritesViewModel.cs");
        var watchlist = Read("src", "SiloPlayer", "ViewModels", "WatchlistViewModel.cs");
        var favoritesPage = Read("src", "SiloPlayer", "Views", "FavoritesPage.xaml.cs");
        var watchlistPage = Read("src", "SiloPlayer", "Views", "WatchlistPage.xaml.cs");

        Assert.Contains("public bool HasMore", model);
        Assert.Contains("/api/v1/favorites?limit=", api);
        Assert.Contains("/api/v1/watchlist?limit=", api);
        Assert.Contains("response.HasMore", favorites);
        Assert.Contains("response.HasMore", watchlist);
        Assert.Contains("_offset += PageSize", favorites);
        Assert.Contains("_offset += PageSize", watchlist);
        Assert.Contains("LoadMoreCommand.ExecuteAsync", favoritesPage);
        Assert.Contains("LoadMoreCommand.ExecuteAsync", watchlistPage);
    }

    [Fact]
    public void PlaybackModelsCarryCurrentSubtitleInventoryIdentity()
    {
        var start = Read("src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartRequest.cs");
        var response = Read("src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartResponse.cs");
        var transcode = Read("src", "SiloPlayer.Core", "Models", "Playback", "TranscodeStartRequest.cs");
        var playbackApi = Read("src", "SiloPlayer.Core", "Api", "PlaybackApi.cs");
        Assert.Contains("SupportsBitmapSubtitleBurnIn", start);
        Assert.Contains("MediaFileId", response);
        Assert.Contains("SubtitleMediaFileId", transcode);
        Assert.Contains("public double? PlayerStartSeconds", playbackApi);
        Assert.Contains("public double? StreamOriginSeconds", playbackApi);
        Assert.Contains("public double? TimelineOffsetSeconds", playbackApi);
        Assert.Contains("public bool? CanSeekAnywhere", playbackApi);
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("SetBitmapSubtitleBurnInAsync", player);
        Assert.Contains("recipe.SubtitleMediaFileId = track?.MediaFileId ?? 0", player);
        Assert.Contains("Pgs deliberately does not use this", player, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginModelsCarryCurrentCatalogProvenanceAndPresentation()
    {
        var catalog = Read("src", "SiloPlayer.Core", "Models", "Plugins", "PluginCatalogEntry.cs");
        var installation = Read("src", "SiloPlayer.Core", "Models", "Plugins", "PluginInstallation.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "PluginsApi.cs");
        Assert.Contains("PluginPresentation", catalog);
        Assert.Contains("SourceKind", catalog);
        Assert.Contains("UpdatesPaused", installation);
        Assert.Contains("/api/v1/admin/plugins/catalog-settings", api);
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml.cs");
        Assert.Contains("Include approved community plugins", Read("src", "SiloPlayer", "Views", "Admin", "AdminPluginsPage.xaml"));
        Assert.Contains("Managed by Silo", page);
        Assert.Contains("Updates paused", page);
        Assert.Contains("SourceLabel", page);
    }

    [Fact]
    public void NotificationRelayUsesDedicatedCredentialFlowWithoutExposingSecret()
    {
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var models = Read("src", "SiloPlayer.Core", "Models", "Admin", "AdminSettingsCheck.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs");
        Assert.Contains("/api/v1/admin/notifications/push/relay/register", api);
        Assert.Contains("PushRelayRegisterResponse", models);
        Assert.Contains("notifications.push_relay_api_key", page);
        Assert.DoesNotContain("AddPasswordField(card, \"Relay", page);
        Assert.Contains("Privacy disclosure", page);
        Assert.Contains("Re-register relay", page);
    }

    [Fact]
    public void TextSubtitleSelectionClearsActiveBitmapBurnInFirst()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("postLoadSubtitleIndex: track.Index", player);
        Assert.Contains("_pendingInitialServerSubtitleIndex = postLoadSubtitleIndex", player);
        Assert.Contains("if (!transportReloadScheduled)", player);
        Assert.Contains("SelectSubtitleByServerIndexAsync", player);
        Assert.Contains("direct playback restored", player);
        Assert.Contains("_preBitmapBurnInPlan is { IsHls: false }", player);
    }

    [Fact]
    public void PerUserTranscodingPolicyMatchesCurrentServerContract()
    {
        var models = Read("src", "SiloPlayer.Core", "Models", "Admin", "AdminUser.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var users = Read("src", "SiloPlayer", "Views", "Admin", "AdminUsersPage.xaml.cs");
        var detail = Read("src", "SiloPlayer", "Views", "Admin", "AdminUserDetailPage.xaml.cs");
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        var playbackFailures = Read(
            "src", "SiloPlayer.Core", "Services", "PlaybackFailureDescription.cs");
        Assert.Contains("TranscodeAllowed", models);
        Assert.Contains("AudioTranscodeAllowed", models);
        Assert.Contains("transcode_allowed", api);
        Assert.Contains("audio_transcode_allowed", api);
        Assert.Contains("TranscodeAllowed", users);
        Assert.Contains("Audio transcodes", detail);
        Assert.Contains("transcoding_disabled", playbackFailures);
        Assert.Contains("audio_transcoding_disabled", playbackFailures);
        Assert.Contains("if (!transportReplaced)", player);
        Assert.Contains("DescribePlaybackError(ex)", player);
        Assert.Contains("ShowNotice(title, detail, \"error\")", player);
    }

    [Fact]
    public void AdminLibrariesUsesCurrentPluginProviderChainContract()
    {
        var model = Read("src", "SiloPlayer.Core", "Models", "Admin", "LibraryProviderChain.cs");
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs");
        Assert.Contains("PluginInstallationId", model);
        Assert.Contains("CapabilityId", model);
        Assert.DoesNotContain("public int ProviderId", model);
        Assert.Contains("/api/v1/libraries/provider-defaults", api);
        Assert.Contains("PluginInstallationId = e.PluginInstallationId", page);
        Assert.Contains("CapabilityId = e.CapabilityId", page);
    }

    [Fact]
    public void AdminLibrariesBrowsesRemoteServerAndSavesCurrentMetadataFields()
    {
        var api = Read("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs");
        Assert.Contains("/api/v1/admin/filesystem/browse", api);
        Assert.Contains("BrowseServerFolderAsync", page);
        Assert.DoesNotContain("new Windows.Storage.Pickers.FolderPicker", page);
        Assert.Contains("auto_translate_metadata", page);
        Assert.Contains("trailer_kinds", page);
        Assert.Contains("Tag = \"manga\"", page);
        Assert.Contains("Scan All Libraries", Read("src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml"));
    }

    [Fact]
    public void AdminLibrariesHydratesRealtimeScansAndInitialDiagnosticsBeforeRendering()
    {
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "Admin", "AdminLibrariesViewModel.cs");

        Assert.Contains("SnapshotReceived += OnSnapshotReceived", page);
        Assert.Contains("MoveLibraryToAsync", page);
        Assert.Contains("await Task.WhenAll(", viewModel);
        Assert.Contains("LoadUnmatchedItemsAsync()", viewModel);
        Assert.Contains("LoadSkippedRootsAsync()", viewModel);
        Assert.Contains("LoadStaleIdsAsync()", viewModel);
    }

    [Fact]
    public void AdminDashboardLoadsSectionsIncrementallyAndShowsCurrentTraktStats()
    {
        var model = Read("src", "SiloPlayer.Core", "Models", "Admin", "AdminStats.cs");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "Admin", "AdminDashboardViewModel.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml.cs");
        var xaml = Read("src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml");
        Assert.Contains("WatchProviderActivity", model);
        Assert.Contains("TotalMovieFiles", model);
        Assert.Contains("TotalShowFiles", model);
        Assert.Contains("LoadStatsSectionAsync", viewModel);
        Assert.Contains("LoadSessionsSectionAsync", viewModel);
        Assert.Contains("LoadLibrariesSectionAsync", viewModel);
        Assert.Contains("LoadUsersSectionAsync", viewModel);
        Assert.Contains("LoadDashboardProgressivelyAsync", page);
        Assert.Contains("BuildStatsError", page);
        Assert.Contains("BuildSessionsError", page);
        Assert.Contains("BuildLibrariesError", page);
        Assert.Contains("BuildUsersError", page);
        Assert.Contains("Failed to load activity.", page);
        Assert.Contains("StatsErrorPanel", xaml);
        Assert.Contains("BuildTraktActivity", page);
        Assert.Contains("TimeSpan.FromSeconds(60)", page);
    }

    [Fact]
    public void AdminActivityCarriesCurrentClientPlaybackAndTranscodeDetails()
    {
        var model = Read("src", "SiloPlayer.Core", "Models", "Admin", "AdminSession.cs");
        var viewModel = Read("src", "SiloPlayer", "ViewModels", "Admin", "AdminActivityViewModel.cs");
        var page = Read("src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml.cs");
        Assert.Contains("PositionSeconds", model);
        Assert.Contains("ClientLabel", model);
        Assert.Contains("ClientBuild", model);
        Assert.Contains("ClientChannel", model);
        Assert.Contains("ClientLabelFull", model);
        Assert.Contains("ClientUserAgent", model);
        Assert.Contains("IsJellyfinClient", model);
        Assert.Contains("IsNativeSiloClient", model);
        Assert.Contains("TranscodeHwAccel", model);
        Assert.Contains("GetSessionClientLabel", viewModel);
        Assert.Contains("GetSessionClientLabelFull", viewModel);
        Assert.Contains("FormatPlaybackPosition", viewModel);
        Assert.Contains("FormatTranscodeMode", viewModel);
        Assert.Contains("BuildPlaybackDetailsPanel", page);
        Assert.Contains("BuildPlaybackClientCard", page);
        Assert.Contains("RunIpLookupAsync", page);
        Assert.Contains("\"JF\"", page);
        Assert.Contains("Jellyfin client", page);
        Assert.Contains("\"Silo\"", page);
        Assert.Contains("Native Silo client", page);
        Assert.Contains("session.IsNativeSiloClient ? null : session.ClientIp", page);
    }

    [Fact]
    public void AutoQualityUsesTheServerResolverInsteadOfAliasingOriginal()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        var policy = Read("src", "SiloPlayer.Core", "Services", "TranscodeQualityPolicy.cs");
        Assert.Contains("if (tierId == \"original\")", player);
        Assert.Contains("var tier = TranscodeQualityPolicy.Find(tierId);", player);
        Assert.Contains("var resolution = tier?.Resolution ?? \"\";", player);
        Assert.Contains("var bitrate = tier?.BitrateKbps ?? 0;", player);
        Assert.DoesNotContain("new(\"auto\"", policy);
        Assert.DoesNotContain("tierId is \"auto\" or \"original\"", player);
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }
}
