using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class GroupedLibraryTests
{
    [Theory]
    [InlineData("movie", true, true, true)]
    [InlineData("movies", true, true, true)]
    [InlineData("mixed", true, true, true)]
    [InlineData("movie", false, true, false)]
    [InlineData("movie", true, false, false)]
    [InlineData("series", true, true, false)]
    public async Task BrowseCountsSeekAndIndexUseIdenticalAdvertisedGroup(string type, bool enabled, bool supported, bool grouped)
    {
        using var wire = new Wire { Supported = supported }; var model = wire.Create(type, enabled);
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.JumpToLetterCommand.ExecuteAsync("M");
        await model.EnsureRangeLoadedAsync(800, 859); await model.PrefetchAroundAsync(800, 859, true);
        Assert.NotEmpty(wire.Catalog);
        Assert.All(wire.Catalog, request => Assert.Equal(grouped ? "box_set" : null, request.Group));
        Assert.All(wire.Letters, body => Assert.Equal(grouped ? "box_set" : null, body.TryGetProperty("group", out var group) ? group.GetString() : null));
        Assert.Equal(1000, model.DisplayTotalCount); Assert.NotNull(model.GetWindowItem(800));
        if (grouped) Assert.Equal("9", model.GetWindowItem(800)!.BoxSet!.LibraryId);
        else { Assert.Equal("movie", model.GetWindowItem(800)!.Type); Assert.Null(model.GetWindowItem(800)!.BoxSet); }
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("movie", true)]
    [InlineData("video", true)]
    [InlineData("episode", false)]
    [InlineData("series", false)]
    [InlineData("audiobook", false)]
    [InlineData("ebook", false)]
    [InlineData("manga", false)]
    public async Task MixedLibraryBrowseTypeScopesCatalogCountsSeekAndLetters(string? type, bool grouped)
    {
        foreach (var large in new[] { false, true })
        {
            using var wire = new Wire(); var model = wire.Create("mixed");
            model.SelectedType = type;
            if (large)
            {
                model.UseAdvancedRules = true;
                model.AdvancedRules.Add(new QueryRule { Field = "title", Op = "contains", Value = new string('x', 34000) });
            }
            await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
            await model.JumpToLetterCommand.ExecuteAsync("M");
            await model.EnsureRangeLoadedAsync(800, 859); await model.PrefetchAroundAsync(800, 859, true);
            Assert.NotEmpty(wire.Catalog); Assert.NotEmpty(wire.Letters);
            Assert.All(wire.Catalog, request =>
            {
                Assert.Equal(type, request.Type);
                Assert.Equal(grouped ? "box_set" : null, request.Group);
                Assert.Equal(large ? "POST" : "GET", request.Method);
            });
            Assert.All(wire.Letters, body =>
            {
                Assert.Equal(string.IsNullOrEmpty(type) ? null : type, body.TryGetProperty("type", out var scope) ? scope.GetString() : null);
                Assert.Equal(grouped ? "box_set" : null, body.TryGetProperty("group", out var group) ? group.GetString() : null);
            });
            Assert.Contains(wire.CatalogRanges, request => request.IncludeTotal);
            Assert.Equal(1000, model.DisplayTotalCount); Assert.NotNull(model.GetWindowItem(800));
            model.CancelCatalogLoads();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingBrowseTypeRetiresGroupedCardsCursorsAndLetters(bool applyFilter)
    {
        using var wire = new Wire(); var model = wire.Create("mixed");
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.True(model.IsGroupedCatalog); Assert.NotNull(model.GetWindowItem(0)!.BoxSet);
        foreach (var type in new[] { "episode", "movie" })
        {
            var before = wire.Catalog.Count;
            model.SelectedType = type;
            Assert.False(model.IsGroupedCatalog); Assert.Null(model.GetWindowItem(0)); Assert.Null(model.LetterIndex);
            if (applyFilter) await model.ApplyFilterCommand.ExecuteAsync(null);
            else await model.LoadWindowAsync(0, 60);
            await model.EnsureLetterIndexAsync();
            Assert.Equal(type == "movie", model.IsGroupedCatalog);
            Assert.Null(wire.CatalogRanges[before].Cursor);
            Assert.All(wire.Catalog.Skip(before), request =>
            {
                Assert.Equal(type, request.Type);
                Assert.Equal(type == "movie" ? "box_set" : null, request.Group);
            });
            Assert.Equal(type, wire.Letters.Last().GetProperty("type").GetString());
            Assert.Equal(type == "movie", wire.Letters.Last().TryGetProperty("group", out _));
        }
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task BrowseTypeChangeDuringCapabilityReadRetiresTheGroupedPreflight()
    {
        using var wire = new Wire { HoldCapabilities = true }; var model = wire.Create("mixed");
        var old = model.LoadCommand.ExecuteAsync(null);
        await wire.CapabilitiesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        model.SelectedType = "episode";
        wire.ReleaseCapabilities.TrySetResult(); await old;
        await model.LoadWindowAsync(0, 60); await model.EnsureLetterIndexAsync();
        Assert.NotEmpty(wire.Catalog);
        Assert.All(wire.Catalog, request => { Assert.Equal("episode", request.Type); Assert.Null(request.Group); });
        Assert.All(wire.Letters, body => { Assert.Equal("episode", body.GetProperty("type").GetString()); Assert.False(body.TryGetProperty("group", out _)); });
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task LargePostAndDescendingFilteredAlphabetKeepTheGroupedScope()
    {
        using var wire = new Wire(); var model = wire.Create();
        model.SelectedOrder = "desc"; model.UseAdvancedRules = true;
        model.AdvancedRules.Add(new QueryRule { Field = "title", Op = "contains", Value = new string('x', 34000) });
        await model.LoadCommand.ExecuteAsync(null); await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.All(wire.Catalog, request => { Assert.Equal("box_set", request.Group); Assert.Equal("POST", request.Method); Assert.Equal("desc", request.Order); });
        Assert.All(wire.Letters, body => { Assert.Equal("box_set", body.GetProperty("group").GetString()); Assert.Equal("desc", body.GetProperty("order").GetString()); Assert.Equal(34000, body.GetProperty("groups")[0].GetProperty("rules")[0].GetProperty("value").GetString()!.Length); });
        Assert.NotNull(model.GetWindowItem(742)); model.CancelCatalogLoads();
    }

    [Fact]
    public async Task DisablingLibraryDiscardsAggregateWindowsAndIndex()
    {
        using var wire = new Wire(); var model = wire.Create(); await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        model.Library = new Library { Id = 9, Type = "movie", BoxSetsEnabled = false };
        await model.LoadWindowAsync(800, 60);
        Assert.Null(model.GetWindowItem(0));
        Assert.Null(wire.Catalog.Last().Group);
        await model.EnsureLetterIndexAsync(); Assert.False(wire.Letters.Last().TryGetProperty("group", out _));
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task SameLibraryDisableDuringCapabilityReadCannotDispatchGroupedRequests()
    {
        using var wire = new Wire { HoldCapabilities = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await wire.CapabilitiesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        model.Library!.BoxSetsEnabled = false;
        wire.ReleaseCapabilities.TrySetResult(); await load; await model.EnsureLetterIndexAsync();
        Assert.DoesNotContain(wire.Catalog, request => request.Group == "box_set");
        Assert.DoesNotContain(wire.Letters, body => body.TryGetProperty("group", out _));
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task GroupedRefreshRetiresCursorAndUpdatesVisibleCounts()
    {
        using var wire = new Wire(); var model = wire.Create(); await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(800, 60); var old = model.GetWindowItem(800);
        wire.PlayedCount = 3;
        await model.RefreshGroupedCatalogStateAsync(800, 60);
        Assert.NotSame(old, model.GetWindowItem(800)); Assert.Equal(3, model.GetWindowItem(800)!.BoxSet!.PlayedCount);
        Assert.Equal("box_set", wire.Catalog.Last().Group); model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("title")]
    [InlineData("year")]
    public async Task GroupedRefreshReloadsViewportInsideMergedWindowWithoutShrinkingExtent(string sort)
    {
        using var wire = new Wire { OmitWindowTotals = true };
        var model = wire.Create(); model.SelectedSort = sort;
        await model.LoadCommand.ExecuteAsync(null);
        for (var start = 60; start <= 780; start += 60)
            await model.EnsureRangeLoadedAsync(start, start + 59);
        Assert.Equal(0, model.WindowStartIndex);
        Assert.True(model.WindowEndIndex >= 839);
        Assert.Equal(1000, model.TotalCount);
        var old = model.GetWindowItem(700);
        var oldStart = model.WindowStartIndex;
        var oldEnd = model.WindowEndIndex;
        var first = model.GetWindowItem(oldStart);
        var last = model.GetWindowItem(oldEnd);
        await model.EnsureLetterIndexAsync();
        var letterIndex = model.LetterIndex;
        var totals = new List<int>();
        var displayedTotals = new List<int>();
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.TotalCount)) totals.Add(model.TotalCount);
            if (args.PropertyName == nameof(model.DisplayTotalCount)) displayedTotals.Add(model.DisplayTotalCount);
        };
        var before = wire.CatalogRanges.Count;
        wire.PlayedCount = 3; wire.HoldCatalog = true;
        var refresh = model.RefreshGroupedCatalogStateAsync(660, 180);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1000, model.TotalCount);
        Assert.Equal(1000, model.DisplayTotalCount);
        Assert.Equal(1000, model.Items.Count);
        Assert.Same(letterIndex, model.LetterIndex);
        Assert.Same(old, model.GetWindowItem(700));
        wire.Release.TrySetResult(); await refresh;
        Assert.All(Enumerable.Range(660, 180), index => Assert.Equal(3, model.GetWindowItem(index)!.BoxSet!.PlayedCount));
        Assert.NotSame(old, model.GetWindowItem(700));
        Assert.Equal(oldStart, model.WindowStartIndex);
        Assert.Equal(oldEnd, model.WindowEndIndex);
        Assert.Same(first, model.GetWindowItem(oldStart));
        Assert.Same(last, model.GetWindowItem(oldEnd));
        var firstWindow = wire.CatalogRanges.Skip(before).First(request => !request.IncludeTotal);
        Assert.Equal(660, firstWindow.Start); Assert.Equal("root", firstWindow.Cursor);
        Assert.All(totals, total => Assert.Equal(1000, total));
        Assert.All(displayedTotals, total => Assert.Equal(1000, total));
        Assert.Equal(1000, model.TotalCount);
        var reads = wire.Catalog.Count;
        await model.EnsureRangeLoadedAsync(0, 59);
        Assert.Equal(reads, wire.Catalog.Count);
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task GroupedRefreshPreservesSparsePagesAndUpdatesOnlyOverlappingSlots()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadMoreCommand.ExecuteAsync(null);
        await model.LoadMoreCommand.ExecuteAsync(null);
        var first = model.Items[0]; var refreshed = model.Items[70];
        Assert.NotNull(first); Assert.NotNull(refreshed);
        var resets = 0;
        model.Items.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        wire.PlayedCount = 3;
        Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed, await model.RefreshGroupedCatalogStateAsync(60, 20));
        Assert.Same(first, model.Items[0]);
        Assert.NotSame(refreshed, model.Items[70]);
        Assert.Equal(3, model.Items[70]!.BoxSet!.PlayedCount);
        Assert.True(model.Items.IsPageLoaded(0)); Assert.True(model.Items.IsPageLoaded(1));
        Assert.Equal(0, resets);
        var before = wire.CatalogRanges.Count;
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(120, wire.CatalogRanges[before].Start);
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task GroupedRefreshKeepsUnrelatedScrollLoadAlive()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeCatalogResponse = async (start, includeTotal, ct) =>
        {
            if (start != 100 || includeTotal) return;
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var scroll = model.LoadWindowAsync(100, 100);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            wire.PlayedCount = 3;
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed, await model.RefreshGroupedCatalogStateAsync(0, 60));
            Assert.False(scroll.IsCompleted);
            Assert.True(model.IsLoading);
            Assert.Equal(3, model.GetWindowItem(0)!.BoxSet!.PlayedCount);
        }
        finally { release.TrySetResult(); await scroll; }
        Assert.NotNull(model.GetWindowItem(199));
        Assert.NotNull(model.GetWindowItem(0));
        Assert.False(model.IsLoading);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("window")]
    [InlineData("sparse")]
    [InlineData("prefetch")]
    [InlineData("window-error")]
    public async Task GroupedRefreshRejectsOlderOverlappingRead(string reader)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        var start = reader == "sparse" ? 0 : reader == "prefetch" ? 40 : 100;
        var refreshedStart = reader == "sparse" ? 20 : 120;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = false;
        wire.AfterCatalogSnapshot = async (index, includeTotal, response) =>
        {
            if (held || index != start || includeTotal) return;
            if (reader == "window-error")
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                response.Content = new StringContent("""{"type":"https://fixture.invalid/docs/api/v2/problems/invalid_cursor","status":400,"detail":"Retired cursor."}""");
            }
            held = true; started.TrySetResult();
            await release.Task; // Freeze the original body, even if its owner is retired.
        };
        Task oldRead = reader switch
        {
            "sparse" => model.LoadMoreCommand.ExecuteAsync(null),
            "prefetch" => model.PrefetchAroundAsync(100, 159, true),
            _ => model.LoadWindowAsync(100, 100),
        };
        SiloPlayer.Core.Models.Home.MediaItem? fresh = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            wire.PlayedCount = 3;
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed,
                await model.RefreshGroupedCatalogStateAsync(refreshedStart, 20));
            Assert.Equal(3, model.GetWindowItem(refreshedStart)!.BoxSet!.PlayedCount);
            fresh = model.GetWindowItem(refreshedStart);
        }
        finally { release.TrySetResult(); await oldRead; }
        if (reader == "prefetch") await model.LoadWindowAsync(800, 100);
        Assert.Equal(3, model.GetWindowItem(refreshedStart)!.BoxSet!.PlayedCount);
        Assert.Same(fresh, model.GetWindowItem(refreshedStart));
        if (reader == "sparse") Assert.False(model.Items.IsPageLoaded(0));
        Assert.Null(model.ErrorMessage);
        Assert.False(model.IsLoading);
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task GroupedRefreshReloadsUncoveredSlotsFromRetiredPendingWindow()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(200, 100, force: true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = false;
        wire.AfterCatalogSnapshot = async (start, includeTotal, _) =>
        {
            if (held || start != 120 || includeTotal) return;
            held = true; started.TrySetResult();
            await release.Task; // An old response may arrive even after cancellation.
        };
        // Cached slots 200..299 trim this read to 120..199, but its wire
        // page still overlaps the replacement at 200..219.
        var oldRead = model.EnsureRangeLoadedAsync(120, 199);
        var published = false;
        model.WindowLoaded += () => published |= model.GetWindowItem(120) is not null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            wire.PlayedCount = 3;
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed,
                await model.RefreshGroupedCatalogStateAsync(200, 20));
            Assert.Null(model.GetWindowItem(120));
            await model.EnsureRangeLoadedAsync(120, 199).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(published);
            Assert.Equal(2, wire.CatalogRanges.Count(request => request.Start == 120 && !request.IncludeTotal));
            Assert.All(Enumerable.Range(120, 180), index =>
                Assert.Equal(3, model.GetWindowItem(index)!.BoxSet!.PlayedCount));
        }
        finally { release.TrySetResult(); await oldRead; model.CancelCatalogLoads(); }
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("remove")]
    [InlineData("reorder")]
    public async Task StructuralRefreshRetiresEarlierOffsetsBeforeServingAnotherWindow(string change)
    {
        using var wire = new Wire(); var model = wire.Create(); model.SelectedSort = "year";
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(100, 100);
        wire.CatalogIndex = change switch
        {
            "insert" => index => index - 1,
            "remove" => index => index + 1,
            _ => index => 999 - index,
        };
        Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed, await model.RefreshGroupedCatalogAsync(120, 20));
        Assert.Equal("box_set:9:" + wire.CatalogIndex(120), model.GetWindowItem(120)!.ContentId);
        var reads = wire.Catalog.Count;
        await model.EnsureRangeLoadedAsync(0, 59);
        Assert.True(wire.Catalog.Count > reads, "A structural change must not serve the earlier offset from the old cache.");
        Assert.Equal("box_set:9:" + wire.CatalogIndex(0), model.GetWindowItem(0)!.ContentId);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuralRefreshKeepsPublishedAlphabetUntilReplacementArrives(bool stateOnly)
    {
        using var wire = new Wire(); var model = wire.Create();
        if (stateOnly) model.SelectedWatchStatus = "watched"; // State now affects offsets too.
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var index = model.LetterIndex;
        Assert.NotNull(index);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.BeforeLettersResponse = async ct =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var cleared = false;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.LetterIndex) && model.LetterIndex is null) cleared = true;
        };
        try
        {
            var result = stateOnly ? await model.RefreshGroupedCatalogStateAsync(120, 20)
                : await model.RefreshGroupedCatalogAsync(120, 20);
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed, result);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(model.IsLetterIndexLoading);
            Assert.Same(index, model.LetterIndex);
            Assert.False(cleared);
            release.TrySetResult();
            await model.EnsureLetterIndexAsync();
            Assert.NotNull(model.LetterIndex);
            Assert.NotSame(index, model.LetterIndex);
            Assert.False(cleared);
        }
        finally { release.TrySetResult(); model.CancelCatalogLoads(); }
    }

    [Fact]
    public async Task StructuralRefreshRetiresSparsePages()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.True(model.Items.IsPageLoaded(0));
        wire.CatalogIndex = index => index - 1;
        await model.RefreshGroupedCatalogAsync(120, 20);
        Assert.False(model.Items.IsPageLoaded(0));
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal("box_set:9:-1", model.Items[0]!.ContentId);
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task StructuralRefreshRetiresDisjointOldRead()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AfterCatalogSnapshot = async (start, total, _) =>
        {
            if (start != 100 || total) return;
            started.TrySetResult(); await release.Task;
        };
        var old = model.LoadWindowAsync(100, 100);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            wire.CatalogIndex = index => index - 1;
            await model.RefreshGroupedCatalogAsync(800, 20);
        }
        finally { release.TrySetResult(); await old; }
        Assert.Null(model.GetWindowItem(100));
        Assert.Equal("box_set:9:799", model.GetWindowItem(800)!.ContentId);
        Assert.Null(model.ErrorMessage);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("watched")]
    [InlineData("advanced")]
    [InlineData("progress")]
    [InlineData("unknown-sort")]
    public async Task StateChangeRetiresOffsetsWhenTheQueryMayDependOnViewerState(string shape)
    {
        using var wire = new Wire(); var model = wire.Create();
        if (shape == "watched") model.SelectedWatchStatus = "watched";
        if (shape == "advanced") model.UseAdvancedRules = true;
        if (shape is "progress" or "unknown-sort") model.SelectedSort = shape;
        await model.LoadCommand.ExecuteAsync(null); await model.LoadWindowAsync(100, 100);
        wire.CatalogIndex = index => index - 1;
        await model.RefreshGroupedCatalogStateAsync(120, 20);
        var reads = wire.Catalog.Count;
        await model.EnsureRangeLoadedAsync(0, 59);
        Assert.True(wire.Catalog.Count > reads);
        Assert.Equal("box_set:9:-1", model.GetWindowItem(0)!.ContentId);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(1001, 999)]
    [InlineData(1001, 1000)]
    [InlineData(999, 980)]
    public async Task StructuralRefreshReplacesThePreviousExactExtentWhileFreshCountIsHeld(int total, int start)
    {
        using var wire = new Wire(); var model = wire.Create(); model.SelectedSort = "year";
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1000, model.DisplayTotalCount);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AfterCatalogSnapshot = async (_, includeTotal, _) =>
        {
            if (!includeTotal) return;
            entered.TrySetResult(); await release.Task;
        };
        wire.CatalogTotal = total;
        try
        {
            await model.RefreshGroupedCatalogAsync(start, 20);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(release.Task.IsCompleted);
            Assert.Equal("box_set:9:" + (total - 1), model.GetWindowItem(total - 1)?.ContentId);
            Assert.Equal(total, model.TotalCount);
        }
        finally { release.TrySetResult(); }
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("library")]
    [InlineData("disable")]
    [InlineData("profile")]
    [InlineData("query")]
    [InlineData("type")]
    [InlineData("navigation")]
    public async Task LateGroupedPageCannotPopulateRetiredScope(string change)
    {
        using var wire = new Wire(); var model = wire.Create(); await model.LoadCommand.ExecuteAsync(null);
        wire.HoldCatalog = true;
        var read = model.LoadWindowAsync(800, 60, force: true); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (change == "library") model.Library = new Library { Id = 10, Type = "movie", BoxSetsEnabled = true };
        if (change == "disable") model.Library = new Library { Id = 9, Type = "movie", BoxSetsEnabled = false };
        if (change == "profile") wire.Client!.SetProfile("replacement");
        if (change == "query") { model.SelectedGenre = "Drama"; model.CancelCatalogLoads(); }
        if (change == "type") model.SelectedType = "episode";
        if (change == "navigation") model.SuspendCatalogLoads();
        wire.Release.TrySetResult(); await read;
        Assert.Null(model.GetWindowItem(800)); model.CancelCatalogLoads();
    }

    [Fact]
    public async Task GroupedIndexIsHiddenWithoutAdvertisedLetterSupport()
    {
        using var wire = new Wire { LetterSupport = false }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Empty(wire.Letters); Assert.Null(model.LetterIndex); Assert.Equal("box_set", wire.Catalog[0].Group); model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(760)]
    public async Task FailedBackgroundRefreshKeepsLiveCardsTotalsAndAlphabetAndCanRetry(int failFromStart)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(660, 180);
        var item = model.GetWindowItem(700); var index = model.LetterIndex;
        wire.CatalogStatus = HttpStatusCode.ServiceUnavailable; wire.FailWindowFromStart = failFromStart;
        Assert.Equal(LibraryViewModel.GroupedRefreshResult.Failed, await model.RefreshGroupedCatalogAsync(660, 180));
        Assert.Same(item, model.GetWindowItem(700)); Assert.Same(index, model.LetterIndex);
        Assert.Equal(1000, model.TotalCount); Assert.Equal(1000, model.DisplayTotalCount);
        Assert.Null(model.ErrorMessage);
        wire.CatalogStatus = HttpStatusCode.OK; wire.PlayedCount = 3;
        await model.RefreshGroupedCatalogAsync(660, 180);
        Assert.Equal(3, model.GetWindowItem(700)!.BoxSet!.PlayedCount);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupedRefreshDefersToAUserLetterJump(bool jumpStartsDuringRefresh)
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var index = model.LetterIndex; int? requested = null;
        model.LetterJumpRequested += position => requested = position;
        wire.HoldCatalog = true;
        var refresh = jumpStartsDuringRefresh ? model.RefreshGroupedCatalogAsync(0, 60) : null;
        if (refresh is not null) await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var jump = model.JumpToLetterCommand.ExecuteAsync("M");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (refresh is null)
        {
            var reads = wire.Catalog.Count;
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Deferred, await model.RefreshGroupedCatalogAsync(0, 60));
            Assert.Equal(reads, wire.Catalog.Count);
        }
        wire.Release.TrySetResult(); await jump;
        if (refresh is not null) Assert.Equal(LibraryViewModel.GroupedRefreshResult.Deferred, await refresh);
        Assert.Equal(742, requested); Assert.Same(index, model.LetterIndex);
        Assert.NotNull(model.GetWindowItem(742)); Assert.False(model.IsLetterJumpInProgress);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("timeout")]
    [InlineData("connection")]
    public async Task CapabilityFailureBrowsesUngroupedUntilANewQueryRetries(string failure)
    {
        using var wire = new Wire { CapabilityFailure = failure }; var model = wire.Create();
        try
        {
            await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
            Assert.NotEmpty(wire.Catalog); Assert.False(model.IsGroupedCatalog);
            Assert.Equal(1000, model.TotalCount); Assert.Equal(1000, model.DisplayTotalCount);
            Assert.Equal("movie", model.GetWindowItem(0)!.Type); Assert.Null(model.ErrorMessage);
            Assert.Contains(wire.CatalogRanges, request => request.IncludeTotal);
            wire.CapabilityFailure = null;
            await model.LoadWindowAsync(800, 60, force: true); await model.EnsureLetterIndexAsync();
            // Recovery cannot change the shape under already issued cursors.
            Assert.NotNull(model.GetWindowItem(800)); Assert.NotEmpty(wire.Letters);
            Assert.All(wire.Catalog, request => Assert.Null(request.Group));
            Assert.All(wire.Letters, body => Assert.False(body.TryGetProperty("group", out _)));
            var reads = wire.Catalog.Count; var indexes = wire.Letters.Count;
            await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
            Assert.True(model.IsGroupedCatalog); Assert.NotNull(model.GetWindowItem(0)!.BoxSet);
            Assert.True(wire.Catalog.Count > reads); Assert.True(wire.Letters.Count > indexes);
            Assert.All(wire.Catalog.Skip(reads), request => Assert.Equal("box_set", request.Group));
            Assert.All(wire.Letters.Skip(indexes), body => Assert.Equal("box_set", body.GetProperty("group").GetString()));
            Assert.Null(model.ErrorMessage);
        }
        finally { model.CancelCatalogLoads(); }
    }

    [Fact]
    public async Task SuspendedPopulatedQueryKeepsItsResolvedGrouping()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        var reads = wire.CapabilityReads;
        model.SuspendCatalogLoads(); wire.CapabilityStatus = HttpStatusCode.ServiceUnavailable;
        await model.LoadWindowAsync(800, 60);
        Assert.Equal(reads, wire.CapabilityReads);
        Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Group));
        Assert.NotNull(model.GetWindowItem(800)); model.CancelCatalogLoads();
    }

    internal sealed class Wire : HttpMessageHandler
    {
        public bool Supported = true, LetterSupport = true, HoldCatalog, OmitWindowTotals;
        public int PlayedCount = 1;
        public int CatalogTotal = 1000;
        public Func<int, int>? CatalogIndex;
        public int CapabilityReads, FailWindowFromStart;
        public HttpStatusCode CapabilityStatus = HttpStatusCode.OK, CatalogStatus = HttpStatusCode.OK;
        public bool HoldCapabilities;
        public string? CapabilityFailure;
        public Func<int, bool, CancellationToken, Task>? BeforeCatalogResponse;
        public Func<CancellationToken, Task>? BeforeLettersResponse;
        public Func<int, bool, HttpResponseMessage, Task>? AfterCatalogSnapshot;
        public readonly TaskCompletionSource CapabilitiesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseCapabilities = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient? Client;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<(string? Group, string Method, string? Order, string? Type)> Catalog = [];
        public readonly List<(int Start, string? Cursor, bool IncludeTotal)> CatalogRanges = [];
        public readonly List<JsonElement> Letters = [];
        public LibraryViewModel Create(string type = "movie", bool enabled = true)
        {
            var client = Client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://grouped.invalid"); client.SetProfile("synthetic");
            return new(new CatalogApi(client)) { Library = new Library { Id = 9, Type = type, BoxSetsEnabled = enabled } };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("capabilities"))
            {
                CapabilityReads++;
                CapabilitiesStarted.TrySetResult(); if (HoldCapabilities) await ReleaseCapabilities.Task;
                if (CapabilityFailure == "timeout") throw new TaskCanceledException("Synthetic capability timeout without caller cancellation.");
                if (CapabilityFailure == "connection") throw new HttpRequestException("Synthetic capability connection reset.");
                if (CapabilityFailure == "status") return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
                if (CapabilityStatus != HttpStatusCode.OK) return new(CapabilityStatus) { Content = new StringContent("{}") };
                return Json(JsonSerializer.Serialize(new { box_sets = Supported, letter_index = LetterSupport }));
            }
            if (path.EndsWith("letters"))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); Letters.Add(body);
                if (BeforeLettersResponse is not null) await BeforeLettersResponse(ct);
                return Json("""{"total":1000,"letters":[{"letter":"A","count":742,"position":0},{"letter":"M","count":258,"position":742}],"runs":[{"letter":"A","count":742,"position":0},{"letter":"M","count":258,"position":742}]}""");
            }
            if (path is "/api/v2/catalog" or "/api/v2/catalog/query")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => x.Length > 1 ? Uri.UnescapeDataString(x[1]) : "");
                JsonElement body = default;
                if (request.Content != null) body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
                string? Value(string key) => body.ValueKind == JsonValueKind.Object && body.TryGetProperty(key, out var member) ? member.ToString() : query.GetValueOrDefault(key);
                var start = int.TryParse(Value("seek"), out var seek) ? seek : int.TryParse(Value("cursor")?.Replace("next-", ""), out var next) ? next : 0;
                var limit = int.TryParse(Value("limit"), out var size) ? size : 100;
                Catalog.Add((Value("group"), request.Method.Method, Value("order") ?? (Value("sort")?.StartsWith('-') == true ? "desc" : "asc"), Value("type")));
                var includeTotal = Value("skip_total") != "true" && Value("skip_total") != "True";
                CatalogRanges.Add((start, Value("cursor"), includeTotal));
                if (BeforeCatalogResponse is not null) await BeforeCatalogResponse(start, includeTotal, ct);
                if (HoldCatalog) { Started.TrySetResult(); await Release.Task; }
                if (CatalogStatus != HttpStatusCode.OK && start >= FailWindowFromStart)
                    return new(CatalogStatus) { Content = new StringContent("{}") };
                var items = Enumerable.Range(start, Math.Min(limit, Math.Max(0, CatalogTotal - start))).Select(index => CatalogIndex?.Invoke(index) ?? index).Select(i => new { content_id = Value("group") == "box_set" ? "box_set:9:" + i : "movie-" + i, type = Value("group") == "box_set" ? "box_set" : "movie", title = "Set " + i, box_set = Value("group") == "box_set" ? new { id = i.ToString(), library_id = "9", title = "Set " + i, movie_count = 3, played_count = PlayedCount } : null });
                var response = Json(JsonSerializer.Serialize(new { items, total = OmitWindowTotals && !includeTotal ? 0 : CatalogTotal, total_exact = includeTotal, window_cursor = "root", page = new { has_more = start + limit < CatalogTotal, next_cursor = "next-" + (start + limit) } }));
                if (AfterCatalogSnapshot is not null) await AfterCatalogSnapshot(start, includeTotal, response);
                return response;
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
