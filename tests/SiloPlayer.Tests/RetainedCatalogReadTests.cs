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

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task NotInterestedMarkAndUndoRetireSnapshotsEvenForUnmountedItems(bool marked, bool mounted)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long revision = 0;
        var serverMarked = !marked;
        var card = mounted ? new MediaItem { ContentId = "movie:b", UserState = new() { NotInterested = !marked } } : null;
        var reads = new List<(int Offset, string? Snapshot)>();
        async Task<CatalogResponse> Read(int offset, int limit, string? snapshot)
        {
            reads.Add((offset, snapshot));
            var response = new CatalogResponse
            {
                Items = [new() { ContentId = offset == 0 ? "movie:a" : "movie:b", UserState = new() { NotInterested = serverMarked } }],
                Total = 2, HasMore = offset == 0, Snapshot = snapshot ?? (reads.Count == 1 ? "old" : "fresh")
            };
            if (reads.Count == 2)
            {
                started.SetResult();
                await release.Task;
            }
            return response;
        }
        var reading = RetainedCatalogRead.ReadAsync(Read, 2, 1, () => revision, CancellationToken.None);
        await started.Task;
        serverMarked = marked; // The preference write (or Undo) succeeded.
        revision++; // Accepted notification retires reads before mounted-item lookup.
        if (card != null) MediaItemStateUpdater.SetNotInterested(card, marked);
        release.SetResult();
        var fresh = await reading;
        Assert.Equal((0, (string?)null), reads[2]);
        Assert.Equal(4, reads.Count);
        Assert.Equal(marked, fresh.Items[1].UserState!.NotInterested);
        Assert.Equal("fresh", fresh.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgressReadsWaitForFinalPersistenceBeforeStartingOrRetrying(bool duringRead)
    {
        var writes = new PendingCatalogWrites();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long revision = 0;
        var reads = 0;
        var serverPosition = 10d;
        async Task<CatalogResponse> Read(int offset, int limit, string? snapshot)
        {
            reads++;
            var response = new CatalogResponse
            {
                Items = [new() { ContentId = "movie:a", PositionSeconds = serverPosition }], Total = 1
            };
            if (duringRead && reads == 1)
            {
                started.SetResult();
                await release.Task;
            }
            return response;
        }
        Task Wait(CancellationToken ct)
        {
            if (revision > 0) retryWaiting.TrySetResult();
            return writes.WaitAsync(ct);
        }
        if (!duringRead) { revision++; writes.Track(persisted.Task); }
        var reading = RetainedCatalogRead.ReadAsync(Read, 1, 60, () => revision, CancellationToken.None, Wait);
        if (duringRead)
        {
            await started.Task;
            revision++;
            writes.Track(persisted.Task);
            release.SetResult();
        }
        await retryWaiting.Task;
        Assert.False(reading.IsCompleted);
        Assert.Equal(duringRead ? 1 : 0, reads);
        serverPosition = 80;
        persisted.SetResult();
        Assert.Equal(80, Assert.Single((await reading).Items).PositionSeconds);
        Assert.Equal(duringRead ? 2 : 1, reads);
    }

    [Fact]
    public async Task Retained119RowsUseOneSignedLimitAndKeepTheExtraFetchedRow()
    {
        var requests = new List<(int Offset, int Limit, string? Snapshot)>();
        Task<CatalogResponse> Read(int offset, int limit, string? snapshot)
        {
            requests.Add((offset, limit, snapshot));
            // The server signs the request limit into its window cursor.
            Assert.Equal(60, limit);
            if (snapshot != null) Assert.Equal("signed-limit-60", snapshot);
            return Task.FromResult(new CatalogResponse
            {
                Items = Enumerable.Range(offset, 60).Select(index => new MediaItem { ContentId = "movie:" + index }).ToList(),
                Total = 180, HasMore = true, Snapshot = "signed-limit-60"
            });
        }
        var response = await RetainedCatalogRead.ReadAsync(Read, 119, 60, () => 0, CancellationToken.None);
        Assert.Equal(new[] { (0, 60, (string?)null), (60, 60, (string?)"signed-limit-60") }, requests);
        Assert.Equal(120, response.Items.Count);
        // CatalogPage continues from Items.Count, without skipping or duplicating the extra row.
        var next = await Read(response.Items.Count, 60, response.Snapshot);
        Assert.Equal("movie:119", response.Items[^1].ContentId);
        Assert.Equal("movie:120", next.Items[0].ContentId);
    }

    [Fact]
    public async Task MetadataRereadWaitsForAnAppLifetimeFinalSaveThatPredatesThePageVisit()
    {
        var cache = new ItemDetailPrefetchCache((string _, CancellationToken _) =>
            Task.FromResult(new MediaItemDetail()));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The app receives this before Catalog is mounted and has a local write registry.
        cache.TrackProgressSave(persisted.Task, "movie:a");
        var localWrites = new PendingCatalogWrites();
        var reads = 0;
        var serverPosition = 10d;
        async Task Wait(CancellationToken ct)
        {
            await localWrites.WaitAsync(ct);
            await cache.WaitForProgressSettlementAsync(ct);
        }
        var refresh = RetainedCatalogRead.ReadAsync((_, _, _) =>
        {
            reads++;
            return Task.FromResult(new CatalogResponse
            {
                Items = serverPosition < 100 ? [new() { ContentId = "movie:a", PositionSeconds = serverPosition }] : [],
                Total = serverPosition < 100 ? 1 : 0
            });
        }, 60, 60, () => cache.ProgressSaveRevision, CancellationToken.None, Wait);
        Assert.False(refresh.IsCompleted);
        Assert.Equal(0, reads);
        // The final save removes the title from Continue Watching before the new snapshot.
        serverPosition = 100;
        persisted.SetResult();
        Assert.Empty((await refresh).Items);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task CancellationWhilePersistenceIsPendingCannotStartARereadAfterNavigation()
    {
        var writes = new PendingCatalogWrites();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        writes.Track(persisted.Task);
        using var visit = new CancellationTokenSource();
        var reads = 0;
        var reading = RetainedCatalogRead.ReadAsync((_, _, _) =>
        {
            reads++;
            return Task.FromResult(new CatalogResponse());
        }, 1, 60, () => 0, visit.Token, writes.WaitAsync);
        visit.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        persisted.SetResult();
        Assert.Equal(0, reads);
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
