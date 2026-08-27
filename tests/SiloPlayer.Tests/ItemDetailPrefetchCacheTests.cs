using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ItemDetailPrefetchCacheTests
{
    [Fact]
    public async Task PrefetchAndNavigationShareOneInFlightDetailRequest()
    {
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new ItemDetailPrefetchCache(async (contentId, _) =>
        {
            Interlocked.Increment(ref calls);
            fetchStarted.SetResult();
            return await response.Task;
        });

        var prefetch = cache.PrefetchAsync("movie-1");
        await fetchStarted.Task;
        var navigation = cache.GetAsync("movie-1", CancellationToken.None);
        response.SetResult(new MediaItemDetail { ContentId = "movie-1", Title = "Movie" });

        await prefetch;
        var detail = await navigation;

        Assert.Equal(1, calls);
        Assert.Equal("Movie", detail.Title);
    }

    [Fact]
    public async Task CancelledNavigationDoesNotCancelSharedPrefetch()
    {
        var response = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ItemDetailPrefetchCache((_, _) => response.Task);
        var prefetch = cache.PrefetchAsync("episode-1");
        using var navigationCancellation = new CancellationTokenSource();
        var navigation = cache.GetAsync("episode-1", navigationCancellation.Token);

        navigationCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => navigation);
        response.SetResult(new MediaItemDetail { ContentId = "episode-1" });

        await prefetch;
        Assert.Equal("episode-1", (await cache.GetAsync("episode-1", CancellationToken.None)).ContentId);
    }

    [Fact]
    public async Task FailedPrefetchIsRemovedSoNavigationCanRetry()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((contentId, _) =>
        {
            calls++;
            return calls == 1
                ? Task.FromException<MediaItemDetail>(new InvalidOperationException("offline"))
                : Task.FromResult(new MediaItemDetail { ContentId = contentId });
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.PrefetchAsync("movie-2"));
        var detail = await cache.GetAsync("movie-2", CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal("movie-2", detail.ContentId);
    }

    [Fact]
    public async Task ServerOrProfileContextChangeCannotReusePriorDetail()
    {
        var calls = 0;
        var context = "server-a/profile-1";
        var cache = new ItemDetailPrefetchCache(
            (contentId, _) => Task.FromResult(new MediaItemDetail
            {
                ContentId = contentId,
                Title = $"result-{++calls}",
            }),
            contextKey: () => context);

        var first = await cache.GetAsync("shared-id", CancellationToken.None);
        context = "server-b/profile-1";
        var second = await cache.GetAsync("shared-id", CancellationToken.None);
        context = "server-b/profile-2";
        var third = await cache.GetAsync("shared-id", CancellationToken.None);

        Assert.Equal("result-1", first.Title);
        Assert.Equal("result-2", second.Title);
        Assert.Equal("result-3", third.Title);
        Assert.Equal(3, calls);
    }
}
