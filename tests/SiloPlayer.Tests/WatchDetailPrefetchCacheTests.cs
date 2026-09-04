using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class WatchDetailPrefetchCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvalidateRemovesOnlyTheRequestedContent()
    {
        var cache = new WatchDetailPrefetchCache<string>(8, TimeSpan.FromMinutes(2));
        cache.Store("movie-1", "profile-1", Task.FromResult("stale"), Now);
        cache.Store("movie-2", "profile-1", Task.FromResult("keep"), Now);

        cache.Invalidate("movie-1");

        Assert.Null(await cache.TryTakeAsync("movie-1", "profile-1", Now));
        Assert.Equal("keep", await cache.TryTakeAsync("movie-2", "profile-1", Now));
    }

    [Fact]
    public async Task EntriesAreProfileScoped()
    {
        var cache = new WatchDetailPrefetchCache<string>(8, TimeSpan.FromMinutes(2));
        cache.Store("movie-1", "profile-1", Task.FromResult("private"), Now);

        Assert.Null(await cache.TryTakeAsync("movie-1", "profile-2", Now));
        Assert.Null(await cache.TryTakeAsync("movie-1", "profile-1", Now));
    }

    [Fact]
    public async Task ExpiredEntriesCannotBeConsumed()
    {
        var cache = new WatchDetailPrefetchCache<string>(8, TimeSpan.FromMinutes(2));
        cache.Store("movie-1", "profile-1", Task.FromResult("stale"), Now);

        var result = await cache.TryTakeAsync(
            "movie-1",
            "profile-1",
            Now.AddMinutes(2));

        Assert.Null(result);
    }

    [Fact]
    public async Task CapacityEvictsTheOldestEntry()
    {
        var cache = new WatchDetailPrefetchCache<string>(2, TimeSpan.FromMinutes(2));
        cache.Store("movie-1", "profile-1", Task.FromResult("one"), Now);
        cache.Store("movie-2", "profile-1", Task.FromResult("two"), Now.AddSeconds(1));
        cache.Store("movie-3", "profile-1", Task.FromResult("three"), Now.AddSeconds(2));

        Assert.Null(await cache.TryTakeAsync("movie-1", "profile-1", Now.AddSeconds(2)));
        Assert.Equal("two", await cache.TryTakeAsync("movie-2", "profile-1", Now.AddSeconds(2)));
        Assert.Equal("three", await cache.TryTakeAsync("movie-3", "profile-1", Now.AddSeconds(2)));
    }
}
