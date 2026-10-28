using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Card hover prefetches item detail into an app-wide cache. A card action or
/// a closed player must make the next detail open read fresh state even when
/// no detail page (and so no detail view model) is alive to invalidate it.
/// </summary>
public sealed class DetailCacheInvalidationTests
{
    [Theory]
    [InlineData(MediaSurfaceChangeKind.WatchedMarked)]
    [InlineData(MediaSurfaceChangeKind.WatchedCleared)]
    [InlineData(MediaSurfaceChangeKind.FavoriteAdded)]
    [InlineData(MediaSurfaceChangeKind.WatchlistRemoved)]
    [InlineData(MediaSurfaceChangeKind.RatingChanged)]
    public async Task CardMutationWithoutADetailPageRefetchesTheNextDetail(MediaSurfaceChangeKind kind)
    {
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);

        var hovered = await cache.GetAsync("episode-1", CancellationToken.None);
        messenger.Send(new MediaSurfaceChanged(kind, "episode-1", "series-1"));
        var opened = await cache.GetAsync("episode-1", CancellationToken.None);

        Assert.Equal(2, fetches.Count(id => id == "episode-1"));
        Assert.NotSame(hovered, opened);
    }

    [Fact]
    public async Task ClosingThePlayerRefreshesTheEpisodeAndItsPrefetchedSeries()
    {
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);

        await cache.GetAsync("episode-1", CancellationToken.None);
        await cache.GetAsync("series-1", CancellationToken.None);
        messenger.Send(new PlaybackProgressUpdated("episode-1", 1200, 1300, completed: true));
        await cache.GetAsync("episode-1", CancellationToken.None);
        await cache.GetAsync("series-1", CancellationToken.None);

        Assert.Equal(2, fetches.Count(id => id == "episode-1"));
        // The series' next-up and progress depend on the episode that just played.
        Assert.Equal(2, fetches.Count(id => id == "series-1"));
    }

    [Fact]
    public async Task UnrelatedTrafficKeepsTheCacheWarm()
    {
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);

        await cache.GetAsync("movie-1", CancellationToken.None);
        await cache.GetAsync("movie-1", CancellationToken.None);

        Assert.Single(fetches);
    }

    [Fact]
    public async Task RegistrationLivesAsLongAsTheCache()
    {
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        await cache.GetAsync("movie-1", CancellationToken.None);
        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "movie-1"));
        await cache.GetAsync("movie-1", CancellationToken.None);

        Assert.Equal(2, fetches.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CardProgressResetAfterHoverDoesNotResumeFromThePrefetchedPosition(bool viaProgressMessage)
    {
        // Hover prefetched watch detail at 20:00; the card then reset progress.
        var cache = new WatchDetailPrefetchCache<WatchDetailResponse>(8, TimeSpan.FromMinutes(2));
        var messenger = new WeakReferenceMessenger();
        var revision = DetailCacheInvalidation.Attach(cache, messenger);
        var now = DateTimeOffset.UtcNow;
        cache.Store("movie-1", "profile", Task.FromResult(new WatchDetailResponse
        {
            UserData = new WatchUserData { PositionSeconds = 1200 },
        }), now);

        if (viaProgressMessage)
            messenger.Send(new PlaybackProgressUpdated("movie-1", 0, 5400, completed: false));
        else
            messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "movie-1"));

        Assert.Null(await cache.TryGetAsync("movie-1", "profile", now));
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task DetailReadDuringASlowFinalSaveIsDroppedOnceTheSaveCompletes()
    {
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);
        // Synchronous completion runs the invalidation continuation inline.
        var finalSave = new TaskCompletionSource();

        await cache.GetAsync("episode-1", CancellationToken.None);
        messenger.Send(new PlaybackProgressUpdated("episode-1", 1200, 1300, completed: true) { Persisted = finalSave.Task });
        await cache.GetAsync("episode-1", CancellationToken.None); // opened while the write is in flight
        finalSave.SetResult();

        await cache.GetAsync("episode-1", CancellationToken.None);
        Assert.Equal(3, fetches.Count(id => id == "episode-1"));
    }

    [Fact]
    public async Task UserStateRevisionAdvancesOnMutationAndAgainWhenTheFinalSaveLands()
    {
        var cache = new WatchDetailPrefetchCache<WatchDetailResponse>(8, TimeSpan.FromMinutes(2));
        var messenger = new WeakReferenceMessenger();
        var revision = DetailCacheInvalidation.Attach(cache, messenger);
        var finalSave = new TaskCompletionSource();

        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "movie-1"));
        Assert.Equal(1, revision.Current);
        messenger.Send(new PlaybackProgressUpdated("movie-1", 10, 5400, completed: false) { Persisted = finalSave.Task });
        Assert.Equal(2, revision.Current);
        finalSave.SetResult();
        Assert.Equal(3, revision.Current);
        GC.KeepAlive(revision);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SnapshotHeldByThePageIsRejectedAfterResetProgress()
    {
        // ItemDetailPage keeps its /watch response and passes it to PlayAsync.
        var revision = new UserStateRevision(() => { });
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(revision, TimeSpan.FromSeconds(5));
        var held = await freshness.ReadAsync(Task.CompletedTask,
            _ => Task.FromResult(new WatchDetailResponse { UserData = new WatchUserData { PositionSeconds = 1200 } }),
            CancellationToken.None);
        Assert.True(freshness.IsCurrent(held));

        var messenger = new WeakReferenceMessenger();
        var attached = DetailCacheInvalidation.Attach(new WatchDetailPrefetchCache<WatchDetailResponse>(8, TimeSpan.FromMinutes(2)), messenger);
        freshness = new WatchDetailFreshness<WatchDetailResponse>(attached, TimeSpan.FromSeconds(5));
        held = await freshness.ReadAsync(Task.CompletedTask,
            _ => Task.FromResult(new WatchDetailResponse { UserData = new WatchUserData { PositionSeconds = 1200 } }),
            CancellationToken.None);
        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "movie-1")); // Reset Progress

        Assert.False(freshness.IsCurrent(held)); // Play must refetch instead of resuming at 20:00
        Assert.False(freshness.IsCurrent(new WatchDetailResponse())); // unstamped snapshots are never trusted
    }

    [Fact]
    public async Task ReplayRightAfterCloseReadsTheServerOnlyAfterTheFinalSave()
    {
        var serverPosition = 300.0; // before the closing session's final write
        var finalSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(new UserStateRevision(() => { }), TimeSpan.FromSeconds(5));

        var read = freshness.ReadAsync(finalSave.Task,
            _ => Task.FromResult(new WatchDetailResponse { UserData = new WatchUserData { PositionSeconds = serverPosition } }),
            CancellationToken.None);
        await Task.Delay(50);
        Assert.False(read.IsCompleted);
        serverPosition = 1200; // the final write lands
        finalSave.SetResult();

        Assert.Equal(1200, (await read).UserData!.PositionSeconds);
    }

    [Fact]
    public async Task AStuckFinalSaveDelaysTheReadOnlyUntilTheBarrier()
    {
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(new UserStateRevision(() => { }), TimeSpan.FromMilliseconds(100));
        var never = new TaskCompletionSource().Task;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var detail = await freshness.ReadAsync(never, _ => Task.FromResult(new WatchDetailResponse()), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(started.ElapsedMilliseconds >= 90);
        Assert.True(freshness.IsCurrent(detail));
    }

    [Fact]
    public async Task SwitchingDirectlyToAnotherItemInvalidatesLikeClosing()
    {
        // PlayCoreAsync now publishes the retiring session's progress with its stop task.
        var (cache, fetches) = CreateCache();
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);
        var retiring = new TaskCompletionSource();

        await cache.GetAsync("episode-1", CancellationToken.None);
        messenger.Send(new PlaybackProgressUpdated("episode-1", 60, 1300, completed: false) { Persisted = retiring.Task });
        await cache.GetAsync("episode-1", CancellationToken.None);
        retiring.SetResult();
        await cache.GetAsync("episode-1", CancellationToken.None);

        Assert.Equal(3, fetches.Count(id => id == "episode-1"));
    }


    private static (ItemDetailPrefetchCache Cache, List<string> Fetches) CreateCache()
    {
        var fetches = new List<string>();
        var cache = new ItemDetailPrefetchCache((id, _) =>
        {
            lock (fetches) fetches.Add(id);
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });
        return (cache, fetches);
    }
}
