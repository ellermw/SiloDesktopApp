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
        var start = Read("src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartRequest.cs");
        var response = Read("src", "SiloPlayer.Core", "Models", "Playback", "PlaybackStartResponse.cs");
        Assert.Contains("SupportsBitmapSubtitleBurnIn", start);
        Assert.Contains("MediaFileId", response);
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("SetBitmapSubtitleBurnInAsync", player);
        Assert.Contains("manager.ReplanSubtitleAsync(track?.Index ?? postLoadSubtitleIndex, position, ct)", player);
        Assert.Contains("Pgs deliberately does not use this", player, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextSubtitleSelectionClearsActiveBitmapBurnInFirst()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        Assert.Contains("postLoadSubtitleIndex: track.Index", player);
        Assert.Contains("_pendingInitialServerSubtitleIndex = postLoadSubtitleIndex", player);
        Assert.Contains("if (!transportReloadScheduled)", player);
        Assert.Contains("SelectSubtitleByServerIndexAsync", player);
        // Burn-in is detected from the v3 plan's subtitle mode.
        Assert.Contains("manager?.CurrentSession?.IsSubtitleBurnedIn == true", player);
    }

    [Fact]
    public void AutoQualityUsesTheServerResolverInsteadOfAliasingOriginal()
    {
        var player = Read("src", "SiloPlayer", "Services", "PlayerService.cs");
        var policy = Read("src", "SiloPlayer.Core", "Services", "TranscodeQualityPolicy.cs");
        // Quality changes are server-resolved v3 replans.
        Assert.Contains("manager.ReplanQualityAsync(quality, currentPos, ct)", player);
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
