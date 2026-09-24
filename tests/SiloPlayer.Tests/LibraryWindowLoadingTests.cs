using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public class LibraryWindowLoadingTests
{
    [Fact]
    public async Task CardsAndDenseViewportLoadBeforeExactCountAndWithoutUnusedFilters()
    {
        var (vm, handler) = Create();
        handler.CountHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.OmitTotalUnlessRequested = true;
        var countApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.DisplayTotalCount) && vm.DisplayTotalCount == 100000)
                countApplied.TrySetResult();
        };
        var load = vm.LoadCommand.ExecuteAsync(null);
        try
        {
            Assert.True(load.IsCompletedSuccessfully, "The exact-count query must not delay cards.");
            Assert.NotNull(vm.GetWindowItem(0));
            Assert.Equal(0, handler.FilterRequests);
            await vm.EnsureRangeLoadedAsync(0, 215);
            Assert.NotNull(vm.GetWindowItem(215));
            Assert.Equal(1, handler.CountRequests);
        }
        finally
        {
            handler.CountHold.TrySetResult();
            await load;
            await countApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task RepeatedSmallDenseScrollsKeepEveryVisibleCardAndBoundRetainedItems()
    {
        var (vm, _) = Create();
        await vm.EnsureRangeLoadedAsync(0, 215);
        for (var start = 24; start <= 2400; start += 24)
        {
            await vm.EnsureRangeLoadedAsync(start, start + 215);
            Assert.All(Enumerable.Range(start, 216), index => Assert.NotNull(vm.GetWindowItem(index)));
        }
        Assert.InRange(Enumerable.Range(0, 3000).Count(index => vm.GetWindowItem(index) != null), 216, 1200);
    }

    [Fact]
    public async Task DensePendingExtensionCannotReviveAnOldQueryAfterNavigation()
    {
        var (vm, handler) = Create();
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = vm.EnsureRangeLoadedAsync(0, 215);
        var extension = vm.EnsureRangeLoadedAsync(24, 239);
        vm.SuspendCatalogLoads();
        handler.Hold.TrySetResult();
        await Task.WhenAll(first, extension);
        Assert.Equal(1, handler.Requests);
        Assert.Null(vm.GetWindowItem(239));
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task JumpPastAnOverlappingPendingRangeDoesNotFetchTheOldExtension()
    {
        var (vm, handler) = Create();
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = vm.EnsureRangeLoadedAsync(0, 215);
        var extension = vm.EnsureRangeLoadedAsync(24, 239);
        var jump = vm.EnsureRangeLoadedAsync(1000, 1215);
        handler.Hold.TrySetResult();
        await Task.WhenAll(first, extension, jump);
        Assert.Equal(4, handler.Requests);
        Assert.Null(vm.GetWindowItem(239));
        Assert.Equal("1215", vm.GetWindowItem(1215)?.ContentId);
    }

    [Fact]
    public async Task DenseScrollingReusesOverlappingRequestAndLoadsOnlyTheLatestMissingTail()
    {
        var (vm, handler) = Create();
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<Task> { vm.EnsureRangeLoadedAsync(0, 215) };
        for (var row = 1; row <= 5; row++)
            requests.Add(vm.EnsureRangeLoadedAsync(row * 24, row * 24 + 215));
        try
        {
            Assert.Equal(1, handler.Requests);
            Assert.Equal(0, handler.Cancellations);
        }
        finally
        {
            handler.Hold.TrySetResult();
            await Task.WhenAll(requests);
        }
        Assert.Equal("335", vm.GetWindowItem(335)?.ContentId);
        Assert.All(Enumerable.Range(120, 216), index => Assert.NotNull(vm.GetWindowItem(index)));
        Assert.Equal(5, handler.Requests); // 3 original pages, then 2 for the missing tail.
        Assert.Equal(0, handler.Cancellations);
    }

    [Fact]
    public async Task ExtendingDenseCachedViewportDoesNotDownloadItsExistingCardsAgain()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 215);
        var original = vm.GetWindowItem(100);
        await vm.EnsureRangeLoadedAsync(24, 239);
        Assert.Equal(4, handler.Requests);
        Assert.Same(original, vm.GetWindowItem(100));
        Assert.Equal("239", vm.GetWindowItem(239)?.ContentId);
    }

    [Fact]
    public async Task SlowFiltersDoNotDelayLoadingTheRestOfTheViewport()
    {
        var (vm, handler) = Create();
        handler.FilterHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var filtersApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Genres.CollectionChanged += (_, _) => { if (vm.Genres.Contains("Drama")) filtersApplied.TrySetResult(); };
        var initial = vm.LoadCommand.ExecuteAsync(null);
        _ = vm.EnsureFiltersLoadedAsync(); // User opens the filter sheet while cards load.
        try
        {
            Assert.True(initial.IsCompletedSuccessfully, "Catalog readiness must not wait for optional filters.");
            Assert.NotNull(vm.GetWindowItem(0));
            await vm.EnsureRangeLoadedAsync(0, 159);
            Assert.NotNull(vm.GetWindowItem(159));
            Assert.Empty(vm.Genres);
        }
        finally
        {
            handler.FilterHold.TrySetResult();
            await initial;
            await filtersApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ChangingSortWhileFiltersLoadStillPopulatesFilterOptions()
    {
        var (vm, handler) = Create();
        handler.FilterHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var filtersApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.Genres.CollectionChanged += (_, _) => { if (vm.Genres.Contains("Drama")) filtersApplied.TrySetResult(); };
        await vm.LoadCommand.ExecuteAsync(null);
        _ = vm.EnsureFiltersLoadedAsync();
        vm.SelectedSort = "year";
        await vm.ApplyFilterCommand.ExecuteAsync(null);
        handler.FilterHold.TrySetResult();
        await filtersApplied.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CachedReturnRestartsCancelledFiltersWithoutReloadingCards()
    {
        var (vm, handler) = Create();
        handler.FilterHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await vm.LoadCommand.ExecuteAsync(null);
        var original = vm.GetWindowItem(0);
        var requests = handler.Requests;
        vm.SuspendCatalogLoads();
        var filters = vm.EnsureFiltersLoadedAsync();
        handler.FilterHold.TrySetResult();
        await filters;
        Assert.Contains("Drama", vm.Genres);
        Assert.Same(original, vm.GetWindowItem(0));
        Assert.Equal(requests, handler.Requests);
    }

    [Fact]
    public async Task CancellingDenseLoadDiscardsItsPartialWindowAndLateResponse()
    {
        var (vm, handler) = Create();
        var nextPageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextPageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeCatalogResponse = async (offset, _) =>
        {
            if (offset != 100) return;
            nextPageStarted.TrySetResult();
            // Deliberately emulate a transport that completes after cancellation.
            await nextPageRelease.Task;
        };
        var pending = vm.EnsureRangeLoadedAsync(0, 159);
        await nextPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.CancelCatalogLoads();
        nextPageRelease.TrySetResult();
        await pending;
        Assert.Null(vm.GetWindowItem(0));
        Assert.Null(vm.GetWindowItem(159));
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task DenseViewportPublishesFirstPageWhileTheNextPageIsStillLoading()
    {
        var (vm, handler) = Create();
        var nextPageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextPageRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeCatalogResponse = async (offset, ct) =>
        {
            if (offset != 100) return;
            nextPageStarted.TrySetResult();
            await nextPageRelease.Task.WaitAsync(ct);
        };
        var notifications = 0;
        vm.WindowLoaded += () => notifications++;
        var pending = vm.EnsureRangeLoadedAsync(0, 159);
        try
        {
            await nextPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("0", vm.GetWindowItem(0)?.ContentId);
            Assert.Equal("99", vm.GetWindowItem(99)?.ContentId);
            Assert.Null(vm.GetWindowItem(159));
            Assert.True(notifications > 0);
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            nextPageRelease.TrySetResult();
            await pending;
        }
        Assert.Equal("159", vm.GetWindowItem(159)?.ContentId);
    }

    [Fact]
    public async Task FailedLaterPageKeepsAlreadyLoadedCardsVisible()
    {
        var (vm, handler) = Create();
        handler.BeforeCatalogResponse = (offset, _) => offset == 100
            ? Task.FromException(new HttpRequestException("Fixture second-page failure"))
            : Task.CompletedTask;
        await vm.EnsureRangeLoadedAsync(0, 159);
        Assert.Equal("0", vm.GetWindowItem(0)?.ContentId);
        Assert.Equal("99", vm.GetWindowItem(99)?.ContentId);
        Assert.Null(vm.GetWindowItem(159));
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }

    [Theory]
    [InlineData(true, 360, 559)]
    [InlineData(false, 280, 479)]
    public async Task IdleReadAheadLoadsNeighborsWithoutReplacingVisibleWindow(bool forward, int first, int last)
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(400, 439);
        var visible = vm.GetWindowItem(400);
        var windowStart = vm.WindowStartIndex;
        var redraws = 0;
        vm.WindowLoaded += () => redraws++;
        await vm.PrefetchAroundAsync(400, 439, forward);
        Assert.NotNull(vm.GetWindowItem(first));
        Assert.NotNull(vm.GetWindowItem(last));
        Assert.Same(visible, vm.GetWindowItem(400));
        Assert.Equal(windowStart, vm.WindowStartIndex);
        Assert.Equal(0, redraws);
        var requests = handler.Requests;
        await vm.PrefetchAroundAsync(400, 439, forward);
        Assert.Equal(requests, handler.Requests);
        Assert.All(handler.Limits, limit => Assert.InRange(limit, 1, 100));
    }

    [Fact]
    public async Task ReadAheadAtEndOfLibraryNeverRequestsBeyondTheCatalog()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(99960, 99999);
        var items = await vm.PrefetchAroundAsync(99960, 99999, true);
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.InRange(int.Parse(item.ContentId), 99920, 99999));
        Assert.InRange(handler.Requests, 1, 3);
    }

    [Fact]
    public async Task RepeatedReadAheadKeepsABoundedCacheAndDoesNotRefetchIdleBuffer()
    {
        var (vm, handler) = Create();
        for (var start = 0; start < 10000; start += 1000)
        {
            await vm.EnsureRangeLoadedAsync(start, start + 99);
            await vm.PrefetchAroundAsync(start, start + 99, true);
        }
        Assert.Null(vm.GetWindowItem(0));
        Assert.NotNull(vm.GetWindowItem(8999));
        Assert.NotNull(vm.GetWindowItem(9399));
        var requests = handler.Requests;
        await vm.PrefetchAroundAsync(9000, 9099, true);
        Assert.Equal(requests, handler.Requests);
        Assert.InRange(Enumerable.Range(0, 10000).Count(i => vm.GetWindowItem(i) != null), 500, 1200);
    }

    [Fact]
    public async Task CancelledReadAheadCannotPopulateAnOldQuery()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(400, 439);
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.PrefetchAroundAsync(400, 439, true);
        vm.CancelCatalogLoads();
        handler.Hold.SetResult();
        await pending;
        Assert.Null(vm.GetWindowItem(559));
        Assert.Null(vm.GetWindowItem(400));
    }

    [Fact]
    public async Task NeighboringCachedItemsRemainAvailableWithoutSwitchingWindows()
    {
        var (vm, _) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        var original = vm.GetWindowItem(39);
        await vm.EnsureRangeLoadedAsync(100, 139);
        Assert.Same(original, vm.GetWindowItem(39));
        Assert.NotNull(vm.GetWindowItem(139));
    }

    [Fact]
    public async Task RangeSpanningCachedWindowsDoesNotRefetchOrClearCards()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        await vm.EnsureRangeLoadedAsync(100, 139);
        await vm.EnsureRangeLoadedAsync(40, 139);
        Assert.Equal(2, handler.Requests);
        Assert.NotNull(vm.GetWindowItem(40));
        Assert.NotNull(vm.GetWindowItem(139));
    }

    [Fact]
    public async Task ScrollingNearbyDoesNotFetchEveryNewRow()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        await vm.EnsureRangeLoadedAsync(8, 47);
        await vm.EnsureRangeLoadedAsync(40, 79);
        Assert.Equal(1, handler.Requests);
        Assert.Equal("79", vm.GetWindowItem(79)?.ContentId);
    }

    [Fact]
    public async Task ReversingDirectionReusesRecentlyLoadedItems()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        var original = vm.GetWindowItem(0);
        await vm.EnsureRangeLoadedAsync(400, 439);
        await vm.EnsureRangeLoadedAsync(0, 39);
        Assert.Equal(2, handler.Requests);
        Assert.Same(original, vm.GetWindowItem(0));
    }

    [Fact]
    public async Task MovingWithinPendingWindowDoesNotCancelAndRestartIt()
    {
        var (vm, handler) = Create();
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = vm.EnsureRangeLoadedAsync(0, 39);
        var second = vm.EnsureRangeLoadedAsync(8, 47);
        handler.Hold.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, handler.Cancellations);
        Assert.Equal("47", vm.GetWindowItem(47)?.ContentId);
    }

    [Fact]
    public async Task QueryResetDoesNotReusePreviousLibraryItems()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        vm.CancelCatalogLoads();
        vm.Library = new Library { Id = 2, Type = "movie" };
        await vm.EnsureRangeLoadedAsync(0, 39);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task LongScrollEvictsOldWindowsInsteadOfGrowingForever()
    {
        var (vm, handler) = Create();
        for (var i = 0; i < 12; i++)
            await vm.EnsureRangeLoadedAsync(i * 200, i * 200 + 39);
        await vm.EnsureRangeLoadedAsync(0, 39);
        Assert.Equal(13, handler.Requests);
    }

    [Fact]
    public async Task DenseViewportLoadsAllItemsWithoutExceedingServerPageLimit()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 159);
        Assert.Equal("159", vm.GetWindowItem(159)?.ContentId);
        Assert.All(handler.Limits, limit => Assert.InRange(limit, 1, 100));
    }

    [Fact]
    public async Task CachedReturnCancelsDistantRequestWithoutReplacingCurrentItems()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        await vm.EnsureRangeLoadedAsync(400, 439);
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.EnsureRangeLoadedAsync(800, 839);
        await vm.EnsureRangeLoadedAsync(0, 39);
        handler.Hold.SetResult();
        await pending;
        Assert.Equal("0", vm.GetWindowItem(0)?.ContentId);
        Assert.Null(vm.GetWindowItem(800));
    }

    [Fact]
    public async Task QueryResetDoesNotJoinCancelledPendingRequest()
    {
        var (vm, handler) = Create();
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.EnsureRangeLoadedAsync(0, 39);
        vm.CancelCatalogLoads();
        var next = vm.EnsureRangeLoadedAsync(0, 39);
        handler.Hold.SetResult();
        await Task.WhenAll(pending, next);
        Assert.Equal(2, handler.Requests);
        Assert.Equal("0", vm.GetWindowItem(0)?.ContentId);
    }

    [Fact]
    public async Task ReturningToActiveWindowCancelsPendingOffscreenLoad()
    {
        var (vm, handler) = Create();
        await vm.EnsureRangeLoadedAsync(0, 39);
        handler.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.EnsureRangeLoadedAsync(800, 839);
        await vm.EnsureRangeLoadedAsync(0, 39);
        handler.Hold.SetResult();
        await pending;
        Assert.Equal("0", vm.GetWindowItem(0)?.ContentId);
        Assert.False(vm.IsLoading);
    }

    private static (LibraryViewModel, CatalogHandler) Create()
    {
        var handler = new CatalogHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return (new LibraryViewModel(new CatalogApi(client))
        {
            Library = new Library { Id = 1, Type = "movie" }, TotalCount = 100000,
        }, handler);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        public int Requests;
        public int Cancellations;
        public TaskCompletionSource? Hold;
        public TaskCompletionSource? FilterHold;
        public TaskCompletionSource? CountHold;
        public bool OmitTotalUnlessRequested;
        public int CountRequests;
        public int FilterRequests;
        public Func<int, CancellationToken, Task>? BeforeCatalogResponse;
        public List<int> Limits = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/filters"))
            {
                FilterRequests++;
                if (FilterHold != null) await FilterHold.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"genres\":[\"Drama\"]}") };
            }
            Requests++;
            if (Hold is not null)
            {
                try { await Hold.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Cancellations++; throw; }
            }
            var query = request.RequestUri!.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);
            var offset = int.Parse(query["seek"]);
            var limit = int.Parse(query["limit"]);
            var includeTotal = !query.TryGetValue("skip_total", out var skip) || skip != "true";
            if (includeTotal)
            {
                CountRequests++;
                if (CountHold != null) await CountHold.Task.WaitAsync(ct);
            }
            if (BeforeCatalogResponse != null) await BeforeCatalogResponse(offset, ct);
            Limits.Add(limit);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = Enumerable.Range(offset, limit).Select(i => new { content_id = i.ToString(), title = $"Movie {i}", type = "movie" }),
                    total = OmitTotalUnlessRequested && !includeTotal ? 0 : 100000,
                    total_exact = !OmitTotalUnlessRequested || includeTotal,
                    page = new { has_more = true }, window_cursor = "fixture-snapshot",
                })),
            };
        }
    }
}
