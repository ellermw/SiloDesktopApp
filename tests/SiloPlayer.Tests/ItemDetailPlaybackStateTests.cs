using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public class ItemDetailPlaybackStateTests
{
    [Theory]
    [InlineData("movie", false)]
    [InlineData("movie", true)]
    [InlineData("episode", false)]
    [InlineData("episode", true)]
    public void CompletionUpdatesObservableWatchedAction(string type, bool missingUserData)
    {
        var vm = Create(type, missingUserData);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        vm.Receive(new PlaybackProgressUpdated("current", 99, 100, completed: true));

        Assert.True(vm.IsWatched);
        Assert.True(vm.Item!.UserData!.Played);
        Assert.Equal(99, vm.Item.UserData.PositionSeconds);
        Assert.Contains(nameof(vm.IsWatched), notifications);
    }

    [Fact]
    public void PartialRewatchDoesNotClearWatchedAndOtherContentDoesNotChangeDetail()
    {
        var vm = Create("movie", false);
        vm.IsWatched = true;
        vm.Item!.UserData!.Played = true;
        vm.Receive(new PlaybackProgressUpdated("current", 20, 100, completed: false));
        Assert.True(vm.IsWatched);
        Assert.True(vm.Item.UserData.Played);
        vm.Receive(new PlaybackProgressUpdated("other", 99, 100, completed: true));
        Assert.Equal(20, vm.Item.UserData.PositionSeconds);
    }

    [Theory]
    [InlineData("series", false)]
    [InlineData("series", true)]
    [InlineData("season", false)]
    [InlineData("season", true)]
    [InlineData("episode", true)]
    [InlineData("movie", true)]
    public async Task ContainerUsesFreshAggregateAfterFinalProgressSave(string type, bool allWatched)
    {
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetches = 0;
        var cache = new ItemDetailPrefetchCache((id, _) =>
        {
            fetches++;
            return Task.FromResult(new MediaItemDetail
            {
                ContentId = id, Type = type,
                UserData = new ItemDetailUserData { Played = saved.Task.IsCompleted && allWatched }
            });
        });
        var vm = new ItemDetailViewModel(null!, cache) { Item = await cache.GetAsync("parent", CancellationToken.None) };
        var mountedItem = vm.Item;
        vm.Receive(new PlaybackProgressUpdated("child-episode", 99, 100, true));
        Assert.False(vm.IsWatched); // One child cannot mark the entire parent watched.

        var refresh = vm.RefreshWatchedStateAsync(saved.Task, CancellationToken.None);
        Assert.False(refresh.IsCompleted);
        Assert.Equal(1, fetches);
        saved.SetResult();
        await refresh;
        Assert.Equal(2, fetches); // Must invalidate the prefetched pre-play state.
        Assert.Equal(allWatched, vm.IsWatched);
        Assert.Same(mountedItem, vm.Item); // No full-page skeleton/rebuild.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldContainerResponseCannotUpdateNewOrCanceledPage(bool cancel)
    {
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ItemDetailViewModel(null!, new ItemDetailPrefetchCache((_, _) => response.Task))
        {
            Item = new MediaItemDetail { ContentId = "old", Type = "series" }
        };
        using var cts = new CancellationTokenSource();
        var refresh = vm.RefreshWatchedStateAsync(Task.CompletedTask, cts.Token);
        if (cancel) cts.Cancel();
        else vm.Item = new MediaItemDetail { ContentId = "new", Type = "movie" };
        response.SetResult(new MediaItemDetail { UserData = new ItemDetailUserData { Played = true } });
        await refresh;
        Assert.False(vm.IsWatched);
        Assert.Null(vm.Item!.UserData);
    }

    [Fact]
    public async Task FailedRefreshKeepsExistingWatchedState()
    {
        var vm = new ItemDetailViewModel(null!, new ItemDetailPrefetchCache((_, _) =>
            Task.FromException<MediaItemDetail>(new HttpRequestException("Unavailable"))))
        {
            Item = new MediaItemDetail { ContentId = "series", Type = "series", UserData = new() { Played = true } },
            IsWatched = true
        };
        await vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);
        Assert.True(vm.IsWatched);
        Assert.True(vm.Item.UserData.Played);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedRefreshCannotOverwriteNewerWatchedMutation(bool initiallyWatched)
    {
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ItemDetailViewModel(null!, new ItemDetailPrefetchCache((_, _) => response.Task))
        {
            Item = new MediaItemDetail { ContentId = "current", Type = "series" },
            IsWatched = initiallyWatched
        };
        var refresh = vm.RefreshWatchedStateAsync(Task.CompletedTask, CancellationToken.None);
        vm.Receive(new MediaSurfaceChanged(initiallyWatched
            ? MediaSurfaceChangeKind.WatchedCleared : MediaSurfaceChangeKind.WatchedMarked, "current"));
        response.SetResult(new MediaItemDetail { UserData = new() { Played = initiallyWatched } });
        await refresh;
        Assert.Equal(!initiallyWatched, vm.IsWatched);
    }

    [Fact]
    public async Task ReturningFromSuccessorRefreshesOriginalEpisodeAfterItsSave()
    {
        var retiring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new ItemDetailViewModel(null!, new ItemDetailPrefetchCache((id, _) =>
            Task.FromResult(new MediaItemDetail { ContentId = id, UserData = new() { Played = true } })))
        {
            Item = new MediaItemDetail { ContentId = "episode-a", Type = "episode" }
        };
        vm.Receive(new PlaybackProgressUpdated("episode-b", 20, 100, false));
        var refresh = vm.RefreshWatchedStateAsync(Task.WhenAll(retiring.Task, closing.Task), CancellationToken.None);
        closing.SetResult();
        Assert.False(refresh.IsCompleted);
        Assert.False(vm.IsWatched);
        retiring.SetResult();
        await refresh;
        Assert.True(vm.IsWatched);
    }

    private static ItemDetailViewModel Create(string type, bool missingUserData) => new(
        null!, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("No fetch expected")))
    {
        Item = new MediaItemDetail
        {
            ContentId = "current", Type = type,
            UserData = missingUserData ? null : new ItemDetailUserData()
        }
    };
}
