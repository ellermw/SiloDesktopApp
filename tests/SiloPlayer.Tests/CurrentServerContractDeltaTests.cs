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
        Assert.Contains("/api/v2/favorites", api);
        Assert.Contains("/api/v2/watchlist", api);
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
        var capabilities = Read("src", "SiloPlayer.Core", "Services", "MpvNativePlaybackCapabilities.cs");
        var response = Read("src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartResponse.cs");
        // Protocol v3 declares bitmap subtitle support in the playback context.
        Assert.Contains("EmbeddedBitmap = true", capabilities);
        Assert.Contains("MediaFileId", response);
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("SetBitmapSubtitleBurnInAsync", player);
        Assert.Contains("int? requestedIndex = track?.Index;", player);
        Assert.Contains("manager.ReplanCurrentSubtitleAsync(position, ct)", player);
        var manager = Read("src", "SiloPlayer.Core", "Services", "PlaybackManager.cs");
        var start = manager.IndexOf("public async Task<PlaybackStartResponse?> ReplanCurrentSubtitleAsync(", StringComparison.Ordinal);
        var end = manager.IndexOf("public Task<PlaybackStartResponse> ReplanSeekAsync(", start, StringComparison.Ordinal);
        var replan = manager[start..end];
        Assert.Contains("MatchCurrentSubtitle() == null", replan);
        Assert.Contains("ReplanCoreForOwnerAsync(\"track_change\"", replan);
        Assert.Contains("ReplanCurrentSubtitleForOwnerCoreAsync(positionSeconds, ct, null, null)", replan);
        Assert.DoesNotContain("RecordLocalSubtitleSelection(", replan); // Preserve Auto/viewer provenance and retained identity.
        Assert.Contains("Pgs deliberately does not use this", player, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextSubtitleSelectionClearsActiveBitmapBurnInFirst()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Matches(@"if \(manager\?\.CurrentSession\?\.IsSubtitleBurnedIn == true\)\s*\{\s*var result = await SetBitmapSubtitleBurnInForOwnerAsync\(track, currentOwner\);", player);
        Assert.Contains("=> await SetBitmapSubtitleBurnInForOwnerAsync(track, null);", player);
        Assert.Contains("var resolvedIndex = manager.SubtitleSelectionForLoad;", player);
        Assert.Contains("_pendingInitialServerSubtitleIndex = resolvedIndex;", player);
        Assert.Contains("candidate.Index == resolvedIndex", player);
        Assert.DoesNotContain("_pendingInitialServerSubtitleIndex = postLoadSubtitleIndex", player);
        Assert.Contains("if (transportReloadScheduled || _switchingContent || IsLoading ||", player);
        Assert.Contains("SelectSubtitleByServerIndexAsync", player);
        // Burn-in is detected from the v3 plan's subtitle mode.
        Assert.Contains("manager?.CurrentSession?.IsSubtitleBurnedIn == true", player);
    }

    [Fact]
    public void AutoQualityUsesTheServerResolverInsteadOfAliasingOriginal()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        // Quality changes are server-resolved v3 replans.
        Assert.Contains("manager.ReplanQualityAsync(quality, currentPos, ct)", player);
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
