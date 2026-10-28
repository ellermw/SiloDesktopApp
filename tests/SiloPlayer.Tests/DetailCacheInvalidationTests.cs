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
        DetailCacheInvalidation.Attach(cache, messenger);
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
