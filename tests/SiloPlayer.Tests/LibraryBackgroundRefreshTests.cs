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

    [Fact]
    public async Task FailedCatchUpKeepsTheRetainedCards()
    {
        var (wire, model, totals) = Scrolled();
        using (wire)
        {
            wire.Fail = true;
            Reconnect(wire);
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Failed, await model.RefreshInPlaceAsync(500, 60));
            Assert.DoesNotContain(0, totals);
            Assert.Equal("item-500", model.GetWindowItem(500)?.ContentId);
            Assert.Null(model.ErrorMessage);
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

    [Fact]
    public async Task LaterChunkFailurePublishesNothingAndUncachedScrollingMustRetryTheWholeRefresh()
    {
        var (wire, model, _) = Scrolled();
        using (wire)
        {
            var held = Enumerable.Range(0, 1000).Where(index => model.GetWindowItem(index) is not null)
                .ToDictionary(index => index, index => model.GetWindowItem(index));
            wire.Titles.Insert(450, ("new-title", "Brand New"));
            wire.FailWindowAt = 560;
            Reconnect(wire);

            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Failed, await model.RefreshInPlaceAsync(500, 60));
            Assert.All(held, pair => Assert.Same(pair.Value, model.GetWindowItem(pair.Key)));
            wire.FailWindowAt = 500;
            await model.LoadWindowAsync(700, 60);
            Assert.Null(model.GetWindowItem(700));
            Assert.All(held, pair => Assert.Same(pair.Value, model.GetWindowItem(pair.Key)));

            wire.FailWindowAt = null;
            await model.LoadWindowAsync(700, 60);
            await wire.Idle();
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("item-699", model.GetWindowItem(700)?.ContentId);
            Assert.All(held.Keys, index => Assert.Equal(wire.Titles[index].Id, model.GetWindowItem(index)?.ContentId));
            Assert.Equal(1001, model.TotalCount);
        }
    }

    [Fact]
    public async Task CancelledLaterChunkKeepsOneOldSnapshotUntilARetryCompletes()
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
            stop.Cancel();
            wire.ReleaseChunk.TrySetResult();
            Assert.Equal(LibraryViewModel.CatalogRefreshResult.Deferred, await refresh);
            Assert.Same(item, model.GetWindowItem(500));

            wire.HoldWindowAt = null;
            await model.LoadWindowAsync(700, 60);
            Assert.Equal("item-499", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("item-699", model.GetWindowItem(700)?.ContentId);
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

    /// <summary>A snapshot-consistent catalog: a fresh read freezes the current titles; cursors keep reading that frozen copy.</summary>
    private sealed class Catalog : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal readonly List<(string Id, string Title)> Titles = Enumerable.Range(0, 1000).Select(index => ("item-" + index, "Item " + index)).ToList();
        internal readonly List<Dictionary<string, string>> Requests = [];
        private readonly Dictionary<string, List<(string Id, string Title)>> _snapshots = [];
        private int _inFlight;
        internal int LetterReads, LetterPosition = 500;
        internal bool Fail;
        internal int? FailWindowAt, HoldWindowAt;
        internal readonly TaskCompletionSource ChunkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseChunk = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                    query["window"] = snapshot;
                    lock (Requests) Requests.Add(query);
                }
                if (query.ContainsKey("skip_total") && offset == HoldWindowAt)
                {
                    ChunkStarted.TrySetResult();
                    await ReleaseChunk.Task;
                }
                if (query.ContainsKey("skip_total") && offset == FailWindowAt)
                    return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") };
                var frozen = _snapshots[snapshot];
                var page = frozen.Skip(offset).Take(limit).ToList();
                var more = offset + page.Count < frozen.Count;
                return Reply(new
                {
                    items = page.Select(title => new { content_id = title.Id, title = title.Title, type = "movie" }),
                    total = frozen.Count, total_exact = true, window_cursor = snapshot,
                    page = new { has_more = more, next_cursor = more ? $"next|{snapshot}|{offset + page.Count}" : null },
                });
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
