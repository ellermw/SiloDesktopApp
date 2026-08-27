using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaItemActionExecutorTests
{
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
