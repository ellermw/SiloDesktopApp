using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaItemStateUpdaterTests
{
    [Fact]
    public void StateToggles_CreateMissingUserState()
    {
        var item = new MediaItem();

        Assert.True(MediaItemStateUpdater.SetFavorite(item, true));
        Assert.True(MediaItemStateUpdater.SetWatchlist(item, true));
        Assert.True(MediaItemStateUpdater.SetWatched(item, true));

        Assert.NotNull(item.UserState);
        Assert.True(item.UserState!.IsFavorite);
        Assert.True(item.UserState.InWatchlist);
        Assert.True(item.UserState.Played);
    }

    [Fact]
    public void PlaybackProgress_UpdatesProgressSortAndCompletion()
    {
        var updatedAt = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var item = new MediaItem { SortMetrics = new BrowseItemSortMetrics() };

        Assert.True(MediaItemStateUpdater.SetPlaybackProgress(item, 25, 100, false, updatedAt));
        Assert.Equal(25, item.PositionSeconds);
        Assert.Equal(100, item.DurationSeconds);
        Assert.Equal(0.25, item.SortMetrics.ProgressRatio);
        Assert.False(item.UserState!.Played);

        Assert.True(MediaItemStateUpdater.SetPlaybackProgress(item, 100, 100, true, updatedAt));
        Assert.True(item.UserState.Played);
        Assert.Equal(1, item.SortMetrics.ProgressRatio);
    }
}
