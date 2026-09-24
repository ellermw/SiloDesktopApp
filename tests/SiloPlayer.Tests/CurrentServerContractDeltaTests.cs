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
