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
    private static void Reconnect(Catalog wire) => Assert.True(wire.Client.TryInvalidateAccessContext(wire.Client.CaptureContext(), out _));

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
            Reconnect(wire);
            await model.LoadWindowAsync(700, 60);
            Assert.DoesNotContain(0, totals);
            Assert.Equal(1000, model.TotalCount);
            Assert.Equal("item-500", model.GetWindowItem(500)?.ContentId);
            Assert.Equal("item-700", model.GetWindowItem(700)?.ContentId);
            // The old generation's snapshot and continuation are not reused.
            var read = wire.Requests.Last(request => request.ContainsKey("skip_total"));
            Assert.True(read.ContainsKey("seek"));
            Assert.False(read.ContainsKey("cursor"));
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
