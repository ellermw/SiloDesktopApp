using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public class LibraryWindowLoadingTests
{
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
        public List<int> Limits = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (Hold is not null)
            {
                try { await Hold.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Cancellations++; throw; }
            }
            var query = request.RequestUri!.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => parts[1]);
            var offset = int.Parse(query["offset"]);
            var limit = int.Parse(query["limit"]);
            Limits.Add(limit);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = Enumerable.Range(offset, limit).Select(i => new { content_id = i.ToString(), title = $"Movie {i}", type = "movie" }),
                    total = 100000, total_exact = true, has_more = true, snapshot = "fixture-snapshot",
                })),
            };
        }
    }
}
