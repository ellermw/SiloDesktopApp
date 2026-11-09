using System.ComponentModel;
using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// The server closes the event channel every five minutes. Each reconnect bumps the
/// request generation ("Transport catch-up"), which used to remount the library and
/// restart its catalog from item 0. These tests reproduce that catch-up and prove the
/// loaded grid survives while server-side changes are merged in place.
/// </summary>
public sealed class LibraryBackgroundRefreshTests
{
    private static (Catalog Wire, LibraryViewModel Model, List<int> Totals) Scrolled(int start = 500)
    {
        var wire = new Catalog();
        var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        var totals = new List<int>();
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LibraryViewModel.TotalCount)) lock (totals) totals.Add(model.TotalCount); };
        model.LoadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        // Scroll: the user is far down a large library with several windows loaded.
        model.LoadWindowAsync(start - 100, 100).GetAwaiter().GetResult();
        model.LoadWindowAsync(start, 100).GetAwaiter().GetResult();
        lock (totals) totals.Clear();
        return (wire, model, totals);
    }

    // The event channel's catch-up after every reconnect (EventChannelClient.PublishAccessInvalidation).
    private static void Reconnect(Catalog wire) => Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _, transportCatchUp: true));

    [Fact]
    public async Task ReconnectCatchUpMergesAddedRemovedAndChangedTitlesWithoutResettingTheGrid()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            Assert.Equal(1000, model.TotalCount);
            var windowStart = model.WindowStartIndex;
            wire.Titles.Insert(450, ("new-title", "Brand New"));        // added while disconnected
            wire.Titles.RemoveAll(title => title.Id == "item-520");      // removed
            var changed = wire.Titles.FindIndex(title => title.Id == "item-510");
            wire.Titles[changed] = ("item-510", "Renamed");               // metadata changed
            var requests = wire.Requests.Count;

            Reconnect(wire);
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(500, 60));
            await wire.Idle();

            // The extent never collapsed, so the canvas height and scroll offset stand.
            Assert.DoesNotContain(0, totals);
            Assert.Equal(1000, model.TotalCount);
            Assert.True(model.WindowStartIndex >= 0 && model.WindowStartIndex <= 500);
            Assert.Equal(windowStart, model.WindowStartIndex);
            // Changes are merged into the same positions.
            Assert.Equal("new-title", model.GetWindowItem(450)?.ContentId);
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("Renamed", model.GetWindowItem(511)?.Title);
            Assert.Equal("item-521", model.GetWindowItem(521)?.ContentId);
            Assert.DoesNotContain(Enumerable.Range(400, 200).Select(model.GetWindowItem), item => item?.ContentId == "item-520");
            // Every loaded window is still held: scrolling back needs no request.
            Assert.All(new[] { 0, 99, 400, 599 }, index => Assert.NotNull(model.GetWindowItem(index)));
            // The visible range is reread first, fresh (no pre-reconnect cursor), and
            // the other windows share its snapshot; nothing restarted from item 0 as a load.
            var refresh = wire.Requests.Skip(requests).Where(request => request.ContainsKey("skip_total")).ToList();
            Assert.Equal("500", refresh[0]["seek"]);
            Assert.False(refresh[0].ContainsKey("cursor"));
            Assert.All(refresh.Skip(1), request => Assert.Equal(wire.SnapshotOf(refresh[0]), wire.SnapshotOf(request)));
        }
    }

    [Fact]
    public async Task ScrollingAfterAReconnectKeepsTheLoadedGridInsteadOfStartingOver()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            await model.EnsureLetterIndexAsync();
            var letterReads = wire.LetterReads;
            wire.LetterPosition = 501;
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            var requests = wire.Requests.Count;
            Reconnect(wire);
            // No shell catch-up has run (for example while playback covers the route).
            // A scroll must refresh held rows before loading an uncached range.
            await model.LoadWindowAsync(700, 60);
            await wire.Idle();
            Assert.DoesNotContain(0, totals);
            Assert.Equal(1001, model.TotalCount);
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("item-699", model.GetWindowItem(700)?.ContentId);
            Assert.True(wire.LetterReads > letterReads);
            Assert.Equal(501, model.LetterIndex!.Letters.Single(letter => letter.Letter == "M").Position);
            var reads = wire.Requests.Skip(requests).Where(request => request.ContainsKey("skip_total")).ToList();
            Assert.Equal("700", reads[0]["seek"]);
            Assert.False(reads[0].ContainsKey("cursor"));
            Assert.All(reads.Skip(1), read => Assert.Equal(wire.SnapshotOf(reads[0]), wire.SnapshotOf(read)));
        }
    }

    [Fact]
    public async Task OptionalLoadersLeaveCatchUpPriorityToTheActualViewport()
    {
        using var wire = new Catalog();
        wire.Titles.AddRange(Enumerable.Range(1000, 1200).Select(index => ("item-" + index, "Item " + index)));
        var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(0, 1200);
        await model.EnsureLetterIndexAsync();
        await wire.Idle();
        var letterIndex = model.LetterIndex;
        var requests = wire.Requests.Count;
        Reconnect(wire);

        // Tab activation/return asks for the optional rail before filling the viewport.
        await model.EnsureLetterIndexAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await model.LoadMoreCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(requests, wire.Requests.Count);
        Assert.Same(letterIndex, model.LetterIndex);
        Assert.True(model.RequiresCatalogRefresh);
        Assert.Equal(0, model.WindowStartIndex);

        wire.HoldWindowAt = 1160;
        var viewport = model.LoadWindowAsync(1100, 60);
        try
        {
            await wire.ChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("1100", wire.Requests[requests]["seek"]);
            Assert.False(viewport.IsCompleted);
            Assert.NotNull(model.GetRenderableWindowItem(1100));
            Assert.NotNull(model.GetRenderableWindowItem(1159));
            Assert.Null(model.GetRenderableWindowItem(0));
        }
        finally { wire.ReleaseChunk.TrySetResult(); }
        await viewport;
        Assert.False(model.RequiresCatalogRefresh);
    }

    [Theory]
    [InlineData(620, 100)]
    [InlineData(720, 200)]
    [InlineData(820, 300)]
    [InlineData(500, 330)] // The nearest held range is read only after the whole viewport.
    public async Task LargeViewportChunksBecomeRenderableBeforeDistantHeldRows(int heldRead, int confirmedCount)
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            await wire.Idle();
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.HoldWindowAt = heldRead;
            var requests = wire.Requests.Count;
            Reconnect(wire);
            var refresh = model.RefreshInPlaceAsync(520, 330);
            try
            {
                await wire.ChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(refresh.IsCompleted);
                Assert.True(model.RequiresCatalogRefresh);
                Assert.All(Enumerable.Range(520, confirmedCount), index =>
                    Assert.Equal(wire.Titles[index].Id, model.GetRenderableWindowItem(index)?.ContentId));
                Assert.All(Enumerable.Range(520 + confirmedCount, 330 - confirmedCount), index =>
                    Assert.Null(model.GetRenderableWindowItem(index)));
                Assert.Null(model.GetRenderableWindowItem(0));
                Assert.Equal("item-520", model.GetWindowItem(520)?.ContentId); // Held rows are not committed yet.
                var reads = wire.Requests.Skip(requests).ToList();
                var confirmedChunks = (confirmedCount + 99) / 100;
                Assert.Equal(Enumerable.Range(0, confirmedChunks).Select(page => (520 + page * 100).ToString()),
                    reads.Take(confirmedChunks).Select(read => read["seek"]));
                Assert.False(reads[0].ContainsKey("cursor"));
                Assert.All(reads.Skip(1), read => Assert.Equal(wire.SnapshotOf(reads[0]), wire.SnapshotOf(read)));
            }
            finally { wire.ReleaseChunk.TrySetResult(); }
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await refresh.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.All(Enumerable.Range(520, 330), index =>
                Assert.Equal(wire.Titles[index].Id, model.GetRenderableWindowItem(index)?.ContentId));
            Assert.False(model.RequiresCatalogRefresh);
        }
    }

    [Fact]
    public async Task RepeatedFiveMinuteReconnectsNeverRestartTheCatalog()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            for (var reconnect = 0; reconnect < 6; reconnect++)
            {
                Reconnect(wire);
                Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(500, 60));
                await model.LoadWindowAsync(500, 60);
            }
            await wire.Idle();
            Assert.DoesNotContain(0, totals);
            Assert.Equal("item-500", model.GetWindowItem(500)?.ContentId);
            Assert.NotNull(model.GetWindowItem(0));
        }
    }

    [Fact]
    public async Task LetterRailSurvivesTheCatchUpAndIsOnlyReplacedWhenPositionsMoved()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            await model.EnsureLetterIndexAsync();
            var index = model.LetterIndex;
            Assert.NotNull(index);
            var changes = new List<CatalogLettersResponse?>();
            model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LibraryViewModel.LetterIndex)) changes.Add(model.LetterIndex); };
            var letterReads = wire.LetterReads;

            Reconnect(wire);
            await model.RefreshInPlaceAsync(500, 60);
            await wire.Idle();
            Assert.True(wire.LetterReads > letterReads);
            Assert.Same(index, model.LetterIndex);
            Assert.Empty(changes);
            Assert.False(model.IsLetterIndexLoading);

            wire.LetterPosition = 510;
            Reconnect(wire);
            await model.RefreshInPlaceAsync(500, 60);
            await wire.Idle();
            Assert.NotSame(index, model.LetterIndex);
            Assert.Equal(510, model.LetterIndex!.Letters.Single(letter => letter.Letter == "M").Position);
            Assert.DoesNotContain(null, changes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipTotalChunksCannotCollapseTheRetainedExtentBeforeTheCountArrives(bool omitTotals)
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            await wire.Idle();
            wire.OmitWindowTotals = omitTotals;
            wire.ZeroWindowTotal = !omitTotals;
            wire.HoldCount = true;
            Reconnect(wire);
            Assert.True(model.RequiresCatalogRefresh);
            try
            {
                Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(500, 60));
                await wire.CountStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(model.RequiresCatalogRefresh);
                Assert.Equal(1000, model.TotalCount);
                Assert.Equal(1000, model.DisplayTotalCount);
                Assert.DoesNotContain(0, totals);
                Assert.All(new[] { 0, 99, 400, 599 }, index => Assert.NotNull(model.GetWindowItem(index)));
            }
            finally { wire.ReleaseCount.TrySetResult(); }
            await wire.Idle();
            Assert.Equal(1000, model.TotalCount);
        }
    }

    [Fact]
    public async Task FailedCatchUpRetiresUnconfirmedCardsWithoutCollapsingTheExtent()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            wire.Fail = true;
            Reconnect(wire);
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Failed, await model.RefreshInPlaceAsync(500, 60));
            Assert.DoesNotContain(0, totals);
            Assert.Equal(1000, model.TotalCount);
            Assert.Equal(1000, model.DisplayTotalCount);
            Assert.Null(model.GetWindowItem(500));
            Assert.Null(model.GetWindowItem(0));
            Assert.Contains("Failed to refresh catalog:", model.ErrorMessage);
            Assert.False(model.RequiresCatalogRefresh);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessRevokedWhileDisconnectedCannotLeaveOldRowsExposed(bool failRefresh)
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            wire.Titles.Clear(); // No access_changed frame or 4001 close was received.
            wire.Fail = failRefresh;
            Reconnect(wire);
            Assert.True(model.RequiresCatalogRefresh);
            Assert.Null(model.GetRenderableWindowItem(500));
            var result = await model.RefreshInPlaceAsync(500, 60);
            await wire.Idle();
            Assert.Equal(failRefresh ? LibraryViewModel.CatalogRefreshResult.Failed :
                LibraryViewModel.CatalogRefreshResult.Completed, result);
            Assert.Null(model.GetWindowItem(0));
            Assert.Null(model.GetWindowItem(500));
            if (failRefresh) Assert.NotNull(model.ErrorMessage);
            else Assert.Equal(0, model.TotalCount);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AccessInvalidationRereadsHeldRowsEvenIfTransportReconnectsAfterward(bool conditional, bool reconnectAfterAccess)
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            await model.EnsureLetterIndexAsync();
            Assert.NotNull(model.LetterIndex);
            var requests = wire.Requests.Count;
            wire.Titles.Clear(); // The profile no longer has access to any title.
            if (conditional) Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _));
            else wire.Client.InvalidateAccessContext();
            if (reconnectAfterAccess) Reconnect(wire);

            await model.LoadWindowAsync(500, 60);
            await wire.Idle();

            Assert.True(wire.Requests.Count > requests);
            Assert.False(wire.Requests[requests].ContainsKey("cursor"));
            Assert.Null(model.GetWindowItem(500));
            Assert.Null(model.GetWindowItem(0));
            Assert.Null(model.LetterIndex);
            Assert.Equal(0, model.TotalCount);
            Assert.Equal(0, model.DisplayTotalCount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterChunkFailureRetiresOldRowsAndDoesNotGateUncachedScrolling(bool timeout)
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            var held = Enumerable.Range(0, 1000).Where(index => model.GetWindowItem(index) is not null).ToArray();
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.FailWindowAt = 560;
            wire.TimeoutWindow = timeout;
            Reconnect(wire);

            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Failed, await model.RefreshInPlaceAsync(500, 60));
            // Unconfirmed held rows retire, but the independently confirmed viewport survives.
            Assert.All(held, index => Assert.Null(model.GetWindowItem(index)));
            Assert.Equal("item-499", model.GetRenderableWindowItem(500)?.ContentId);
            Assert.Null(model.GetRenderableWindowItem(0));
            Assert.NotNull(model.ErrorMessage);
            Assert.Equal(1000, model.TotalCount);
            var reads = wire.Requests.Count;
            Assert.Empty(await model.PrefetchAroundAsync(700, 759, true));
            Assert.Equal(reads, wire.Requests.Count); // no speculative snapshot before the foreground retry
            // The failed held offset remains broken, but no longer gates a new viewport.
            await model.LoadWindowAsync(700, 60);
            await wire.Idle();
            Assert.Equal(560, wire.FailWindowAt);
            Assert.Equal("item-699", model.GetWindowItem(700)?.ContentId);
            Assert.Null(model.GetWindowItem(500));
            Assert.Null(model.GetWindowItem(0));
            Assert.Null(model.ErrorMessage);
            Assert.Equal(1001, model.TotalCount);
            Assert.False(wire.Requests[reads].ContainsKey("cursor"));
            Assert.DoesNotContain(wire.Requests.Skip(reads), read => read.ContainsKey("skip_total") && read.GetValueOrDefault("seek") == "560");
        }
    }

    [Fact]
    public async Task CancelledWaiterKeepsTheSharedRefreshAndJoinedViewportAlive()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        using (var stop = new CancellationTokenSource())
        {
            var item = model.GetWindowItem(500);
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.HoldWindowAt = 560;
            Reconnect(wire);
            var refresh = model.RefreshInPlaceAsync(500, 60, stop.Token);
            await wire.ChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Same(item, model.GetWindowItem(500));
            Assert.True(model.RequiresCatalogRefresh); // ordinary reads still await the atomic held-set merge
            Assert.Equal("item-499", model.GetRenderableWindowItem(500)?.ContentId);
            Assert.Null(model.GetRenderableWindowItem(0)); // unconfirmed held windows stay hidden
            var viewport = model.LoadWindowAsync(700, 60);
            try
            {
                stop.Cancel();
                Assert.Equal(LibraryViewModel.CatalogRefreshResult.Deferred, await refresh.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.False(viewport.IsCompleted);
                Assert.Same(item, model.GetWindowItem(500));
                Assert.True(model.RequiresCatalogRefresh);
            }
            finally { wire.ReleaseChunk.TrySetResult(); }

            await viewport;
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("item-699", model.GetWindowItem(700)?.ContentId);
            Assert.False(model.RequiresCatalogRefresh);
        }
    }

    [Fact]
    public async Task ScrollingToAnUnheldRangeDuringCatchUpRendersItBeforeDistantHeldRows()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            await wire.Idle();
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.HoldWindowAt = 560;       // the nearest held chunk, read right after the viewport
            wire.HoldSecondWindowAt = 400; // a farther held chunk
            var requests = wire.Requests.Count;
            Reconnect(wire);
            var refresh = model.RefreshInPlaceAsync(500, 60);
            Task? viewport = null;
            try
            {
                await wire.ChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                // The user scrolls to a range outside the held set before the catch-up finishes.
                viewport = model.LoadWindowAsync(800, 60);
                Assert.Null(model.GetRenderableWindowItem(800));
            }
            finally { wire.ReleaseChunk.TrySetResult(); }
            try
            {
                await wire.SecondChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.False(refresh.IsCompleted);
                Assert.False(viewport!.IsCompleted);
                Assert.True(model.RequiresCatalogRefresh);
                Assert.All(Enumerable.Range(800, 60), index =>
                    Assert.Equal(wire.Titles[index].Id, model.GetRenderableWindowItem(index)?.ContentId));
                var reads = wire.Requests.Skip(requests).Where(read => read.ContainsKey("skip_total")).ToList();
                Assert.Equal(new[] { "500", "560", "800", "400" }, reads.Select(read => read["seek"]));
                Assert.False(reads[0].ContainsKey("cursor"));
                Assert.All(reads.Skip(1), read => Assert.Equal(wire.SnapshotOf(reads[0]), wire.SnapshotOf(read)));
            }
            finally { wire.ReleaseSecondChunk.TrySetResult(); }
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await refresh.WaitAsync(TimeSpan.FromSeconds(3)));
            await viewport!.WaitAsync(TimeSpan.FromSeconds(3));
            await wire.Idle();
            Assert.Equal("item-799", model.GetWindowItem(800)?.ContentId);
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.False(model.RequiresCatalogRefresh);
        }
    }

    [Fact]
    public async Task CancelledArtworkReadAheadCannotAbortCatchUpForAJoinedVisibleRange()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        using (var stop = new CancellationTokenSource())
        {
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            Reconnect(wire);
            // Read-ahead itself starts the pending catch-up after the reconnect.
            wire.HoldWindowAt = 560;
            var readAhead = model.PrefetchAroundAsync(500, 559, true, stop.Token);
            await wire.ChunkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var viewport = model.LoadWindowAsync(700, 60);
            try
            {
                stop.Cancel(); // the next scroll retires only the speculative caller
                Assert.Empty(await readAhead.WaitAsync(TimeSpan.FromSeconds(3)));
                Assert.False(viewport.IsCompleted);
                Assert.True(model.RequiresCatalogRefresh);
            }
            finally { wire.ReleaseChunk.TrySetResult(); }
            await viewport;
            Assert.Equal("item-699", model.GetRenderableWindowItem(700)?.ContentId);
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.False(model.RequiresCatalogRefresh);
        }
    }

    [Fact]
    public async Task CatchUpRefreshesTheEntireActiveAndReadAheadSetBeyondTheOldBudget()
    {
        using var wire = new Catalog();
        wire.Titles.AddRange(Enumerable.Range(1000, 1200).Select(index => ("item-" + index, "Item " + index)));
        var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(0, 1200);
        // Read-ahead caches distant windows without replacing the 1200-row active window.
        await model.PrefetchAroundAsync(1600, 1659, true);
        await model.PrefetchAroundAsync(1800, 1859, true);
        var held = Enumerable.Range(0, 2200).Where(index => model.GetWindowItem(index) is not null).ToArray();
        Assert.True(held.Length > 1200);
        var chunks = model.PlanRefreshChunks(520, 60);
        var covered = chunks.SelectMany(chunk => Enumerable.Range(chunk.Start, chunk.Count)).ToHashSet();
        Assert.All(held, index => Assert.Contains(index, covered));
        Assert.InRange(chunks.Sum(chunk => chunk.Count), held.Length, LibraryViewModel.MaxRefreshedItems);

        wire.Titles.Insert(50, ("new-title", "Brand New"));
        Reconnect(wire);
        Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(520, 60));
        await wire.Idle();
        Assert.All(held, index => Assert.Equal(wire.Titles[index].Id, model.GetWindowItem(index)?.ContentId));
        Assert.Equal(held.Length, held.Select(index => model.GetWindowItem(index)!.ContentId).Distinct().Count());
        Assert.Equal(2201, model.TotalCount);
    }

    [Fact]
    public async Task CatchUpRetiresSparsePagesThatWereNotInTheHeldWindowPlan()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            await model.LoadMoreCommand.ExecuteAsync(null);
            Assert.True(model.Items.IsPageLoaded(0));
            wire.Titles.Insert(0, ("new-title", "Brand New"));
            Reconnect(wire);
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(500, 60));
            Assert.False(model.Items.IsPageLoaded(0));
            await model.LoadMoreCommand.ExecuteAsync(null);
            Assert.Equal("new-title", model.Items[0]?.ContentId);
        }
    }

    [Fact]
    public async Task UserSortChangeAndProfileSwitchStillStartOver()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            model.SelectedSort = "year";
            await model.ApplyFilterCommand.ExecuteAsync(null);
            Assert.Contains(0, totals);
            Assert.Null(model.GetWindowItem(500));

            await model.LoadWindowAsync(500, 100);
            lock (totals) totals.Clear();
            wire.Client.SetProfile("someone-else");
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.NotLoaded, await model.RefreshInPlaceAsync(500, 60));
            await model.LoadWindowAsync(500, 60);
            // Another profile's catalog is never merged into this one's positions.
            Assert.Contains(0, totals);
            Assert.Null(model.GetWindowItem(500));
        }
    }

    [Fact]
    public async Task CatchUpNeverEvictsAWindowItRefreshes()
    {
        using var wire = new Catalog();
        var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadCommand.ExecuteAsync(null);
        // A steady scroll leaves eight adjacent cached windows, the cache's limit.
        for (var start = 100; start < 800; start += 100) await model.LoadWindowAsync(start, 100);
        var held = Enumerable.Range(0, 1000).Where(index => model.GetWindowItem(index) is not null).ToArray();
        Assert.Equal(800, held.Length);

        Reconnect(wire);
        // The viewport splits a window, so later reads span two cached windows each.
        Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(520, 60));
        await wire.Idle();
        Assert.All(held, index => Assert.NotNull(model.GetWindowItem(index)));
    }

    [Fact]
    public async Task PartiallyHeldViewportCannotEvictFreshRowsAtThe1200RowCacheCap()
    {
        using var wire = new Catalog();
        wire.Titles.AddRange(Enumerable.Range(1000, 2000).Select(index => ("item-" + index, "Item " + index)));
        var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadCommand.ExecuteAsync(null);
        await wire.Idle();
        for (var start = 0; start < 1000; start += 200) await model.LoadWindowAsync(start, 200);
        await model.LoadWindowAsync(2000, 100);
        await model.LoadWindowAsync(2300, 100); // 1200 cached rows; oldest is outside the active window
        var held = Enumerable.Range(0, 3000).Where(index => model.GetWindowItem(index) is not null).ToArray();
        Assert.Equal(1200, held.Length);
        Assert.NotNull(model.GetWindowItem(0));
        Assert.Null(model.GetWindowItem(1000));
        wire.Titles.Insert(0, ("new-title", "Brand New"));

        Reconnect(wire);
        Assert.Equal(LibraryViewModel.CatalogRefreshResult.Completed, await model.RefreshInPlaceAsync(990, 60));
        await wire.Idle();
        Assert.All(held, index => Assert.Equal(wire.Titles[index].Id, model.GetWindowItem(index)?.ContentId));
        Assert.Equal(wire.Titles[1049].Id, model.GetRenderableWindowItem(1049)?.ContentId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SnapshotChurnRetriesThenHandsBackToOrdinaryReadsWithoutBlankingTheViewportOrShowingAnError(int changes)
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            var held = Enumerable.Range(0, 1000).Where(index => model.GetWindowItem(index) is not null).ToArray();
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.ChangeSnapshotAt = 560;
            wire.SnapshotChangesRemaining = changes;
            var requests = wire.Requests.Count;
            Reconnect(wire);
            Assert.Equal(changes == 1 ? LibraryViewModel.CatalogRefreshResult.Completed : LibraryViewModel.CatalogRefreshResult.Deferred,
                await model.RefreshInPlaceAsync(500, 60));
            var freshViewportReads = wire.Requests.Skip(requests)
                .Where(read => read.ContainsKey("skip_total") && read.GetValueOrDefault("seek") == "500").ToList();
            Assert.Equal(2, freshViewportReads.Count);
            Assert.All(freshViewportReads, read => Assert.False(read.ContainsKey("cursor")));
            Assert.Null(model.ErrorMessage);
            Assert.DoesNotContain(0, totals);
            Assert.Equal(1000, model.TotalCount);
            if (changes == 1)
            {
                Assert.All(held, index => Assert.NotNull(model.GetWindowItem(index)));
                Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            }
            else
            {
                // Continued churn no longer gates ordinary reads or blanks the grid:
                // the viewport read under the new context stays renderable, and only
                // the unconfirmed held rows retire.
                Assert.False(model.RequiresCatalogRefresh);
                Assert.All(Enumerable.Range(500, 60), index =>
                    Assert.Equal(wire.Titles[index].Id, model.GetRenderableWindowItem(index)?.ContentId));
                Assert.All(held.Where(index => index < 500 || index >= 560), index =>
                    Assert.Null(model.GetRenderableWindowItem(index)));
                // An ordinary foreground read establishes the fresh snapshot.
                await model.LoadWindowAsync(500, 60);
                await wire.Idle();
                Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
                Assert.False(model.RequiresCatalogRefresh);
            }
        }
    }

    [Fact]
    public void ReadsAreBoundedAndStartAtTheViewport()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            var chunks = model.PlanRefreshChunks(520, 40);
            Assert.Equal((520, 40), chunks[0]);
            Assert.True(chunks.Sum(chunk => chunk.Count) <= LibraryViewModel.MaxRefreshedItems);
            Assert.All(chunks, chunk => Assert.InRange(chunk.Count, 1, 100));
            // Every held row is covered exactly once.
            var covered = chunks.SelectMany(chunk => Enumerable.Range(chunk.Start, chunk.Count)).ToList();
            Assert.Equal(covered.Count, covered.Distinct().Count());
            Assert.All(new[] { 0, 99, 400, 599 }, index => Assert.Contains(index, covered));
        }
    }

    [Theory]
    [InlineData(112)]
    [InlineData(330)]
    public void LargeViewportIsPlannedInFullBeforeOtherHeldRanges(int visibleCount)
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            var held = Enumerable.Range(0, model.TotalCount).Where(index => model.GetWindowItem(index) is not null).ToArray();
            var chunks = model.PlanRefreshChunks(520, visibleCount);
            var viewportChunks = (visibleCount + 99) / 100;
            Assert.Equal(Enumerable.Range(0, viewportChunks)
                    .Select(page => (520 + page * 100, Math.Min(100, visibleCount - page * 100))),
                chunks.Take(viewportChunks));
            Assert.All(chunks, chunk => Assert.InRange(chunk.Count, 1, 100));
            var covered = chunks.SelectMany(chunk => Enumerable.Range(chunk.Start, chunk.Count)).ToList();
            Assert.Equal(covered.Count, covered.Distinct().Count());
            Assert.All(held.Concat(Enumerable.Range(520, visibleCount)), index => Assert.Contains(index, covered));
            Assert.InRange(covered.Count, held.Length, LibraryViewModel.MaxRefreshedItems);
        }
    }

    [Fact]
    public void ShellCatchUpKeepsAHealthyLibraryInsteadOfRemountingIt()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "MainWindow.xaml.cs")).ReplaceLineEndings("\n");
        var catchUp = source[source.IndexOf("private void OnReadCatchUpRequested(", StringComparison.Ordinal)..];
        catchUp = catchUp[..catchUp.IndexOf("private void OnAccountAuthorityChanged(", StringComparison.Ordinal)];
        var library = catchUp[catchUp.IndexOf("if (ContentFrame.Content is LibraryPage library)", StringComparison.Ordinal)..];
        Assert.Contains("_pendingDetailCatchUpRequiresRemount |= !library.CanPreserveOnReconnect;", library);
        Assert.True(library.IndexOf("return;", StringComparison.Ordinal) < library.IndexOf("_pendingAccessRefresh = true;", StringComparison.Ordinal));

        var apply = source[source.IndexOf("private void ApplyPendingAccessRefresh(", StringComparison.Ordinal)..];
        apply = apply[..apply.IndexOf("public void ShowMainNavigation(", StringComparison.Ordinal)];
        Assert.Contains("ContentFrame.Content is LibraryPage retainedLibrary && retainedLibrary.CanPreserveOnReconnect", apply);
        Assert.Contains("catchUpLibrary.CatchUpAfterReconnectAsync()", apply);
        Assert.Contains("if (!preserveDetail && ContentFrame.Content is not BoxSetPage)", apply);

        var page = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "LibraryPage.CatchUp.cs"));
        Assert.Contains("ViewModel.RefreshInPlaceAsync(", page);
        Assert.DoesNotContain("RefreshCurrentEntry", page);
        Assert.DoesNotContain("ClearVirtualCards", page);
        Assert.DoesNotContain("QueueLibraryOffset(0)", page);
    }

    [Fact]
    public void UnconfirmedCardsAreHiddenBeforePlaybackCanDeferCatchUpAndReboundBeforeExposure()
    {
        var root = Path.Combine(TestRepository.Root, "src", "SiloPlayer");
        var shell = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        var catchUp = shell[shell.IndexOf("private void OnReadCatchUpRequested(", StringComparison.Ordinal)..];
        var library = catchUp[catchUp.IndexOf("if (ContentFrame.Content is LibraryPage library)", StringComparison.Ordinal)..];
        library = library[..library.IndexOf("return;", StringComparison.Ordinal)];
        Assert.True(library.IndexOf("library.HideUnconfirmedCatalogRows();", StringComparison.Ordinal) <
            library.IndexOf("ApplyPendingAccessRefresh();", StringComparison.Ordinal));
        Assert.Contains("library.HideUnconfirmedCatalogRows();", library);

        var page = File.ReadAllText(Path.Combine(root, "Views", "LibraryPage.xaml.cs"));
        Assert.Contains("if (ViewModel.RequiresCatalogRefresh) HideUnconfirmedCatalogRows();", page);
        var render = page[page.IndexOf("private void RenderVirtualGrid(", StringComparison.Ordinal)..];
        render = render[..render.IndexOf("private void EnsureVirtualCardSlots(", StringComparison.Ordinal)];
        Assert.Contains("if (ViewModel.RequiresCatalogRefresh)", render);
        var pendingGate = render[render.IndexOf("if (ViewModel.RequiresCatalogRefresh)", StringComparison.Ordinal)..];
        pendingGate = pendingGate[..pendingGate.IndexOf("var layout =", StringComparison.Ordinal)];
        Assert.DoesNotContain("return;", pendingGate); // the first confirmed chunk must paint before the held-set merge
        Assert.Contains("ViewModel.GetRenderableWindowItem(index)", render);
        Assert.True(render.IndexOf("HideUnconfirmedCatalogRows();", StringComparison.Ordinal) <
            render.IndexOf("range.StartIndex == _lastRenderedStartIndex", StringComparison.Ordinal));
        Assert.Contains("else if (_catalogCardsAwaitingConfirmation)", render);
        Assert.True(render.IndexOf("card.Bind(item, ViewModel.SelectedSort);", StringComparison.Ordinal) <
            render.IndexOf("card.Visibility = Visibility.Visible;", StringComparison.Ordinal));

        var refresh = File.ReadAllText(Path.Combine(root, "Views", "LibraryPage.CatchUp.cs"));
        var hide = refresh[refresh.IndexOf("public void HideUnconfirmedCatalogRows()", StringComparison.Ordinal)..];
        hide = hide[..hide.IndexOf("public async Task CatchUpAfterReconnectAsync()", StringComparison.Ordinal)];
        Assert.Contains("card.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;", hide);
        Assert.DoesNotContain("VirtualGridCanvas.Height", hide);
        Assert.DoesNotContain("ClearVirtualCards", hide);
    }

    /// <summary>A snapshot-consistent catalog: a fresh read freezes the current titles; cursors keep reading that frozen copy.</summary>
    private sealed class Catalog : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal readonly List<(string Id, string Title)> Titles = Enumerable.Range(0, 1000).Select(index => ("item-" + index, "Item " + index)).ToList();
        internal readonly List<Dictionary<string, string>> Requests = [];
        private readonly Dictionary<string, List<(string Id, string Title)>> _snapshots = [];
        private int _inFlight;
        internal int LetterReads, LetterPosition = 500;
        internal bool Fail, OmitWindowTotals, ZeroWindowTotal, HoldCount, TimeoutWindow;
        internal readonly TaskCompletionSource CountStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseCount = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int? FailWindowAt, HoldWindowAt, HoldSecondWindowAt, ChangeSnapshotAt;
        internal int SnapshotChangesRemaining;
        internal readonly TaskCompletionSource ChunkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseChunk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondChunkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSecondChunk = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Catalog() { Client = new SiloApiClient(new HttpClient(this)); Client.SetBaseUrl("https://background-refresh.invalid"); }

        internal string? SnapshotOf(Dictionary<string, string> request) => request.GetValueOrDefault("window");

        internal async Task Idle()
        {
            for (var spin = 0; spin < 200 && Volatile.Read(ref _inFlight) > 0; spin++) await Task.Delay(10);
            await Task.Delay(20);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _inFlight);
            try
            {
                await Task.Yield();
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/search/capabilities")) return Reply(new { letter_index = true });
                if (path.EndsWith("/catalog/letters"))
                {
                    Interlocked.Increment(ref LetterReads);
                    var position = LetterPosition;
                    return Reply(new
                    {
                        total = 1000,
                        letters = new[] { new { letter = "A", count = position, position = 0 }, new { letter = "M", count = 1000 - position, position } },
                        runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position } },
                    });
                }
                var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Split('=', 2))
                    .ToDictionary(value => Uri.UnescapeDataString(value[0]), value => value.Length > 1 ? Uri.UnescapeDataString(value[1]) : "");
                if (Fail) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") };
                var limit = int.Parse(query["limit"]);
                string snapshot; int offset;
                lock (_snapshots)
                {
                    if (query.TryGetValue("cursor", out var cursor) && cursor.StartsWith("next|", StringComparison.Ordinal))
                    {
                        var parts = cursor.Split('|');
                        snapshot = parts[1]; offset = int.Parse(parts[2]);
                    }
                    else
                    {
                        offset = int.Parse(query["seek"]);
                        if (query.TryGetValue("cursor", out var window) && _snapshots.ContainsKey(window)) snapshot = window;
                        else { snapshot = "snap-" + _snapshots.Count; _snapshots[snapshot] = Titles.ToList(); }
                    }
                    if (query.ContainsKey("skip_total") && offset == ChangeSnapshotAt && SnapshotChangesRemaining > 0)
                    {
                        SnapshotChangesRemaining--;
                        snapshot = "snap-" + _snapshots.Count;
                        _snapshots[snapshot] = Titles.ToList();
                    }
                    query["window"] = snapshot;
                    lock (Requests) Requests.Add(query);
                }
                if (!query.ContainsKey("skip_total") && HoldCount)
                {
                    CountStarted.TrySetResult();
                    await ReleaseCount.Task.WaitAsync(ct);
                }
                if (query.ContainsKey("skip_total") && offset == HoldWindowAt)
                {
                    ChunkStarted.TrySetResult();
                    await ReleaseChunk.Task;
                }
                if (query.ContainsKey("skip_total") && offset == HoldSecondWindowAt)
                {
                    SecondChunkStarted.TrySetResult();
                    await ReleaseSecondChunk.Task;
                }
                if (query.ContainsKey("skip_total") && offset == FailWindowAt)
                {
                    if (TimeoutWindow) throw new TaskCanceledException("Synthetic timeout without caller cancellation.");
                    return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") };
                }
                var frozen = _snapshots[snapshot];
                var page = frozen.Skip(offset).Take(limit).ToList();
                var more = offset + page.Count < frozen.Count;
                var body = new Dictionary<string, object?>
                {
                    ["items"] = page.Select(title => new { content_id = title.Id, title = title.Title, type = "movie" }),
                    ["window_cursor"] = snapshot,
                    ["page"] = new { has_more = more, next_cursor = more ? $"next|{snapshot}|{offset + page.Count}" : null },
                };
                if (!query.ContainsKey("skip_total") || !OmitWindowTotals)
                {
                    body["total"] = query.ContainsKey("skip_total") && ZeroWindowTotal ? 0 : frozen.Count;
                    body["total_exact"] = true;
                }
                return Reply(body);
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
