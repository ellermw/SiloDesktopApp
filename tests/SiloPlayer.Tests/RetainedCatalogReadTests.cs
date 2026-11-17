using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class RetainedCatalogReadTests
{
    [Theory]
    [InlineData("watched")]
    [InlineData("favorite-removed")]
    [InlineData("watchlist-removed")]
    public async Task MutationWhileALaterWindowIsPausedRejectsTheEntireOldSnapshot(string mutation)
    {
        var laterWindowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long revision = 0;
        var reads = new List<(int Offset, string? Snapshot)>();
        var mounted = new MediaItem { ContentId = "movie:a", UserState = new() { IsFavorite = true, InWatchlist = true } };
        var removed = false;
        async Task<CatalogResponse> Read(int offset, int limit, string? snapshot)
        {
            reads.Add((offset, snapshot));
            if (reads.Count <= 2)
            {
                var old = new CatalogResponse
                {
                    Items = [new() { ContentId = offset == 0 ? "movie:a" : "movie:b", Title = "old", UserState = new() { Played = false } }],
                    Total = 2, HasMore = offset == 0, Snapshot = "old-snapshot"
                };
                if (offset > 0)
                {
                    laterWindowStarted.SetResult();
                    await release.Task;
                }
                return old;
            }
            return new CatalogResponse
            {
                Items = [new()
                {
                    ContentId = removed || offset > 0 ? "movie:b" : "movie:a", Title = "fresh",
                    UserState = new() { Played = mounted.UserState!.Played }
                }],
                Total = removed ? 1 : 2, HasMore = !removed && offset == 0, Snapshot = "fresh-snapshot"
            };
        }

        var reading = RetainedCatalogRead.ReadAsync(Read, 2, 1, () => revision, CancellationToken.None);
        await laterWindowStarted.Task;
        switch (mutation)
        {
            case "watched": MediaItemStateUpdater.SetWatched(mounted, true); break;
            case "favorite-removed": MediaItemStateUpdater.SetFavorite(mounted, false); removed = true; break;
            case "watchlist-removed": MediaItemStateUpdater.SetWatchlist(mounted, false); removed = true; break;
        }
        revision++;
        release.SetResult();
        var response = await reading;

        Assert.Equal((0, (string?)null), reads[2]); // Retry starts from a new server snapshot.
        Assert.All(response.Items, item => Assert.Equal("fresh", item.Title));
        if (removed)
        {
            Assert.DoesNotContain(response.Items, item => item.ContentId == mounted.ContentId);
            Assert.Equal(1, response.Total);
            Assert.Equal(3, reads.Count);
        }
        else
        {
            Assert.True(response.Items[0].UserState!.Played);
            Assert.Equal((1, (string?)"fresh-snapshot"), reads[3]);
            Assert.Equal(4, reads.Count);
        }
    }

    [Fact]
    public async Task CancellationRetiresTheReadBeforeAnotherWindowCanStart()
    {
        using var cts = new CancellationTokenSource();
        var reads = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RetainedCatalogRead.ReadAsync((_, _, _) =>
        {
            reads++;
            cts.Cancel();
            return Task.FromResult(new CatalogResponse { Items = [new()], HasMore = true, Total = 2 });
        }, 2, 1, () => 0, cts.Token));
        Assert.Equal(1, reads);
    }
}
