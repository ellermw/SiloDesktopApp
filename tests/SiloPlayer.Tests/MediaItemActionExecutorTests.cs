using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaItemActionExecutorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetClearsResumeFlagsAfterPersistenceWithoutChangingWatchedOrPersonalLists(bool played)
    {
        var item = CreateItem(played, favorite: true); item.Type = "episode";
        item.PositionSeconds = 42; item.DurationSeconds = 100; item.ProgressUpdatedAt = "original";
        item.UserState!.IsInProgress = true; item.UserState.InWatchlist = true;
        item.SortMetrics = new() { ProgressRatio = .42 };
        var state = item.UserState;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reset = MediaItemActionExecutor.ResetProgressAsync(item, () => pending.Task);
        Assert.True(state.IsInProgress); Assert.Equal(42, item.PositionSeconds);
        pending.SetResult(); await reset;
        Assert.Same(state, item.UserState); Assert.False(state.IsInProgress);
        Assert.Equal(played, state.Played); Assert.True(state.IsFavorite); Assert.True(state.InWatchlist);
        Assert.Equal(100, item.DurationSeconds); Assert.Equal(0, item.PositionSeconds);
        Assert.Null(item.ProgressUpdatedAt); Assert.Equal(0, item.SortMetrics.ProgressRatio);
        Assert.Equal(!played, EpisodeSpoilerPolicy.IsUnwatched(state.Played, state.IsInProgress, item.PositionSeconds));
    }

    [Fact]
    public async Task RejectedResetRetainsResumeFlagsAndPosition()
    {
        var item = CreateItem(false, true); item.PositionSeconds = 42; item.UserState!.IsInProgress = true;
        await Assert.ThrowsAsync<IOException>(() => MediaItemActionExecutor.ResetProgressAsync(item, () => throw new IOException()));
        Assert.True(item.UserState.IsInProgress); Assert.Equal(42, item.PositionSeconds);
    }

    [Fact]
    public async Task ToggleWatchedUpdatesImmediatelyAndPersistsTheNewValue()
    {
        var item = CreateItem(played: false, favorite: false);
        var persistStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePersist = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var toggle = MediaItemActionExecutor.ToggleWatchedAsync(item, async nextValue =>
        {
            persistStarted.SetResult(nextValue);
            await releasePersist.Task;
        });

        Assert.True(await persistStarted.Task);
        Assert.True(item.UserState!.Played);

        releasePersist.SetResult();
        Assert.True(await toggle);
    }

    [Fact]
    public async Task ToggleWatchedRestoresThePreviousValueWhenPersistenceFails()
    {
        var item = CreateItem(played: true, favorite: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaItemActionExecutor.ToggleWatchedAsync(
                item,
                _ => throw new InvalidOperationException("server rejected update")));

        Assert.True(item.UserState!.Played);
    }

    [Fact]
    public async Task ToggleFavoriteUpdatesImmediatelyAndRestoresOnFailure()
    {
        var item = CreateItem(played: false, favorite: false);
        var observedOptimisticState = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaItemActionExecutor.ToggleFavoriteAsync(item, nextValue =>
            {
                observedOptimisticState = nextValue && item.UserState!.IsFavorite;
                throw new InvalidOperationException("server rejected update");
            }));

        Assert.True(observedOptimisticState);
        Assert.False(item.UserState!.IsFavorite);
    }

    private static MediaItem CreateItem(bool played, bool favorite) => new()
    {
        ContentId = "movie-1",
        Type = "movie",
        Title = "Example",
        UserState = new UserState
        {
            Played = played,
            IsFavorite = favorite,
        },
    };
}
