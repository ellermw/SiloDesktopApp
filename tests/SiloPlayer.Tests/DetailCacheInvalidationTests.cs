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
    public void FinalSaveCompletingDuringTheFirstClearStillGetsTheSecondClear()
    {
        var finalSave = new TaskCompletionSource();
        var clears = 0;
        DetailCacheInvalidation.ClearNowAndAfter(finalSave.Task, () =>
        {
            if (++clears == 1) finalSave.SetResult(); // completes between clear and registration
        });
        Assert.Equal(2, clears);

        var alreadyDone = 0;
        DetailCacheInvalidation.ClearNowAndAfter(Task.CompletedTask, () => alreadyDone++);
        Assert.Equal(1, alreadyDone);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CardProgressResetAfterHoverDoesNotResumeFromThePrefetchedPosition(bool viaProgressMessage)
    {
        // Hover prefetched watch detail at 20:00; the card then reset progress.
        var (freshness, messenger, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 1200 };
        var prefetched = await freshness.ReadAsync("episode:a", server.Read("episode:a"), CancellationToken.None);
        server.Position = 0;

        if (viaProgressMessage)
            messenger.Send(new PlaybackProgressUpdated("episode:a", 0, 5400, completed: false));
        else
            messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "episode:a"));

        Assert.False(freshness.IsCurrent(prefetched));
        // Play consumes through the cache: the stale entry is evicted and read again.
        WatchDetailResponse? cached = prefetched;
        var played = await freshness.GetCurrentAsync(
            ct => cached is { } entry ? Task.FromResult(entry) : freshness.ReadAsync("episode:a", server.Read("episode:a"), ct),
            _ => cached = null, CancellationToken.None);
        Assert.Equal(0, played.UserData!.PositionSeconds);
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task SnapshotHeldByThePageIsRejectedAfterResetProgress()
    {
        // ItemDetailPage keeps its /watch response and passes it to PlayAsync.
        var (freshness, messenger, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 1200 };
        var held = await freshness.ReadAsync("episode:a", server.Read("episode:a"), CancellationToken.None);
        Assert.True(freshness.IsCurrent(held));

        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "episode:a")); // Reset Progress

        Assert.False(freshness.IsCurrent(held)); // PlayCoreAsync refetches instead of resuming at 20:00
        Assert.False(freshness.IsCurrent(new WatchDetailResponse { ContentId = "episode:a" })); // unstamped is never trusted
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task SeasonOrSeriesMutationReachesItsEpisodesButNotOtherTitles()
    {
        var (freshness, messenger, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 10 };
        var episode = await freshness.ReadAsync("episode:show-s01e01", server.Read("episode:show-s01e01", "series:show"), CancellationToken.None);
        var movie = await freshness.ReadAsync("movie:other", server.Read("movie:other"), CancellationToken.None);

        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "series:show-S01", "series:show"));
        Assert.False(freshness.IsCurrent(episode));
        Assert.True(freshness.IsCurrent(movie));

        // A season id without its series cannot be matched, so it invalidates everything.
        var again = await freshness.ReadAsync("movie:other", server.Read("movie:other"), CancellationToken.None);
        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "series:show-S01"));
        Assert.False(freshness.IsCurrent(again));
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task ReplayRightAfterCloseReadsTheServerOnlyAfterTheFinalSave()
    {
        var (freshness, _, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 300 }; // before the closing session's final write
        var finalSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        freshness.TrackFinalSave("episode:a", finalSave.Task);

        var read = freshness.ReadAsync("episode:a", server.Read("episode:a"), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(read.IsCompleted);
        server.Position = 1200; // the final write lands
        finalSave.SetResult();

        Assert.Equal(1200, (await read).UserData!.PositionSeconds);
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task SwitchingToTheNextItemDoesNotWaitForTheOutgoingStopOrDropItsSnapshot()
    {
        // Next Episode: A's stop is still pending; B was prefetched before the switch.
        var (freshness, messenger, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 0 };
        var prefetchedB = await freshness.ReadAsync("episode:b", server.Read("episode:b"), CancellationToken.None);
        var stopA = new TaskCompletionSource(); // never completes during the test
        freshness.TrackFinalSave("episode:a", stopA.Task);
        messenger.Send(new PlaybackProgressUpdated("episode:a", 60, 1300, completed: false) { Persisted = stopA.Task });

        Assert.True(freshness.IsCurrent(prefetchedB));
        var started = System.Diagnostics.Stopwatch.StartNew();
        await freshness.ReadAsync("episode:b", server.Read("episode:b"), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(started.ElapsedMilliseconds < 500, $"B waited {started.ElapsedMilliseconds} ms on A's stop");

        var replayA = freshness.ReadAsync("episode:a", server.Read("episode:a"), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(replayA.IsCompleted); // only a replay of A waits for A's final write
        stopA.SetResult();
        await replayA.WaitAsync(TimeSpan.FromSeconds(5));
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task AStuckFinalSaveDelaysOnlyItsOwnReadUntilTheBarrier()
    {
        var revision = new UserStateRevision();
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(revision, TimeSpan.FromMilliseconds(100),
            detail => detail.ContentId, detail => detail.SeriesId);
        freshness.TrackFinalSave("episode:a", new TaskCompletionSource().Task);
        var server = new FakeWatch();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var detail = await freshness.ReadAsync("episode:a", server.Read("episode:a"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(started.ElapsedMilliseconds >= 90);
        Assert.True(freshness.IsCurrent(detail));
    }

    [Fact]
    public async Task ResultThatWentStaleDuringItsReadIsFetchedAgainBeforePlayback()
    {
        // The read begins at revision 0; Reset Progress lands while the old
        // position-300 response is still on the wire.
        var (freshness, messenger, revision) = CreateFreshness();
        var release = new TaskCompletionSource();
        var server = new FakeWatch { Position = 300 };
        var reads = 0;
        Task<WatchDetailResponse> Get(CancellationToken ct) => freshness.ReadAsync("episode:a", async token =>
        {
            if (++reads == 1) { var stale = server.Snapshot("episode:a"); await release.Task; return stale; }
            return server.Snapshot("episode:a");
        }, ct);

        var playback = freshness.GetCurrentAsync(Get, _ => { }, CancellationToken.None);
        messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "episode:a"));
        server.Position = 0;
        release.SetResult();

        var detail = await playback.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, detail.UserData!.PositionSeconds);
        Assert.Equal(2, reads);
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task FinalSaveLandingDuringAReadAfterTheBarrierStillForcesARefetch()
    {
        // The bounded barrier expired; the save then completes while the
        // read's response is in flight, so that response is stale.
        var messenger = new WeakReferenceMessenger();
        var revision = DetailCacheInvalidation.AttachUserStateRevision(messenger);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(revision, TimeSpan.FromMilliseconds(50),
            detail => detail.ContentId, detail => detail.SeriesId);
        var finalSave = new TaskCompletionSource();
        freshness.TrackFinalSave("episode:a", finalSave.Task);
        messenger.Send(new PlaybackProgressUpdated("episode:a", 1200, 1300, completed: false) { Persisted = finalSave.Task });
        var server = new FakeWatch { Position = 300 };
        var release = new TaskCompletionSource();
        var reads = 0;
        Task<WatchDetailResponse> Get(CancellationToken ct) => freshness.ReadAsync("episode:a", async token =>
        {
            if (++reads == 1) { var stale = server.Snapshot("episode:a"); await release.Task; return stale; }
            return server.Snapshot("episode:a");
        }, ct);

        var playback = freshness.GetCurrentAsync(Get, _ => { }, CancellationToken.None);
        await Task.Delay(150); // past the barrier; first read is in flight
        server.Position = 1200;
        finalSave.SetResult(); // advances the revision for episode:a
        release.SetResult();

        Assert.Equal(1200, (await playback.WaitAsync(TimeSpan.FromSeconds(5))).UserData!.PositionSeconds);
        Assert.Equal(2, reads);
        GC.KeepAlive(revision);
    }

    [Fact]
    public async Task SwitchingDirectlyToAnotherItemInvalidatesDetailsLikeClosing()
    {
        // PlayCoreAsync publishes the retiring session's progress with its stop task.
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

    [Fact]
    public async Task DetailNavigationAwaitingAPrefetchDoesNotApplyTheInvalidatedResponse()
    {
        // Hover prefetch → navigation awaits it → Favorite added while vm.Item
        // is still null → the pre-mutation response is released.
        const string id = "movie:detail-race-fixture";
        var first = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var cache = new ItemDetailPrefetchCache((contentId, _) => Interlocked.Increment(ref reads) == 1
            ? first.Task
            : Task.FromResult(Detail(contentId, favorite: true)));
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);
        var client = new SiloPlayer.Core.Api.SiloApiClient(new HttpClient(new RejectingHandler()));
        client.SetBaseUrl("https://detail-race.invalid");
        var vm = new SiloPlayer.ViewModels.ItemDetailViewModel(new SiloPlayer.Core.Api.CatalogApi(client), cache);
        try
        {
            cache.Prefetch(id);
            var navigation = vm.LoadInLibraryAsync(id, null);
            await Task.Delay(20);
            Assert.Null(vm.Item);
            messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.FavoriteAdded, id));
            first.SetResult(Detail(id, favorite: false));
            await navigation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(vm.IsFavorite);
            Assert.True(vm.Item!.UserState!.IsFavorite);
            Assert.Equal(2, reads);
        }
        finally { WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Fact]
    public async Task DetailReadThatKeepsRacingMutationsFailsRetryablyInsteadOfReturningStaleState()
    {
        var reads = 0;
        ItemDetailPrefetchCache? cache = null;
        cache = new ItemDetailPrefetchCache(async (contentId, _) =>
        {
            Interlocked.Increment(ref reads);
            await Task.Yield();
            cache!.Invalidate(contentId); // a mutation lands during every read
            return Detail(contentId, favorite: false);
        });

        var error = await Assert.ThrowsAsync<UserStateChangedException>(() => cache.GetAsync("movie:busy", CancellationToken.None));
        Assert.Equal("movie:busy", error.ContentId);
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task WatchRetryExhaustionFailsRetryablyInsteadOfReturningAStalePosition()
    {
        var (freshness, messenger, revision) = CreateFreshness();
        var server = new FakeWatch { Position = 300 };
        var reads = 0;
        var evictions = 0;
        Task<WatchDetailResponse> Get(CancellationToken ct) => freshness.ReadAsync("episode:a", async token =>
        {
            reads++;
            var response = server.Snapshot("episode:a");
            await Task.Yield();
            // The same item changes after every read.
            messenger.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, "episode:a"));
            return response;
        }, ct);

        var error = await Assert.ThrowsAsync<UserStateChangedException>(
            () => freshness.GetCurrentAsync(Get, _ => evictions++, CancellationToken.None));
        Assert.Equal("episode:a", error.ContentId);
        Assert.Equal(3, reads);
        Assert.Equal(3, evictions); // the last stale result is not left in the cache either
        GC.KeepAlive(revision);
    }

    private static MediaItemDetail Detail(string contentId, bool favorite) => new()
    {
        ContentId = contentId,
        Type = "movie",
        Title = "Fixture",
        UserState = new MediaItemUserState { IsFavorite = favorite },
    };

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Network access is not available in this fixture.");
    }

    private static (WatchDetailFreshness<WatchDetailResponse> Freshness, WeakReferenceMessenger Messenger, UserStateRevision Revision) CreateFreshness()
    {
        var messenger = new WeakReferenceMessenger();
        var revision = DetailCacheInvalidation.AttachUserStateRevision(messenger);
        var freshness = new WatchDetailFreshness<WatchDetailResponse>(revision, TimeSpan.FromSeconds(5),
            detail => detail.ContentId, detail => detail.SeriesId);
        return (freshness, messenger, revision);
    }

    private sealed class FakeWatch
    {
        public double Position { get; set; }
        public WatchDetailResponse Snapshot(string contentId, string? seriesId = null) => new()
        {
            ContentId = contentId,
            SeriesId = seriesId,
            UserData = new WatchUserData { PositionSeconds = Position },
        };
        public Func<CancellationToken, Task<WatchDetailResponse>> Read(string contentId, string? seriesId = null)
            => _ => Task.FromResult(Snapshot(contentId, seriesId));
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
