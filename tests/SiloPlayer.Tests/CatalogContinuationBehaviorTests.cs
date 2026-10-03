using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;
public sealed class CatalogContinuationBehaviorTests
{
    [Fact]
    public async Task CompleteAdjacentWindowUsesNextCursorWithoutSeek()
    {
        using var wire = new Wire(); var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100); await model.LoadWindowAsync(100, 60);
        var next = wire.Requests.Last();
        Assert.Equal("next-100", next.GetValueOrDefault("cursor")); Assert.False(next.ContainsKey("seek")); Assert.Equal(2, wire.Requests.Count);
    }
    [Fact]
    public async Task DistantJumpSeeksDirectlyWithoutFetchingIntermediateWindows()
    {
        using var wire = new Wire(); var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100); await model.LoadWindowAsync(900, 60);
        Assert.Equal(2, wire.Requests.Count); Assert.Equal("900", wire.Requests.Last()["seek"]); Assert.Equal("root-window", wire.Requests.Last()["cursor"]);
    }
    [Fact]
    public async Task RefreshingBoundaryCannotSupplyItsOldNextCursor()
    {
        using var wire = new Wire(); var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100); wire.DelayNextRoot = true;
        var refresh = model.LoadWindowAsync(0, 100, force: true); await wire.Started.Task;
        try
        {
            await model.LoadWindowAsync(100, 60);
            Assert.Equal("root-window", wire.Requests.Last()["cursor"]); Assert.Equal("100", wire.Requests.Last()["seek"]);
        }
        finally { wire.Release.TrySetResult(); await refresh; }
        Assert.Equal("item-100", model.GetWindowItem(100)?.ContentId);
    }
    [Fact]
    public async Task IncompleteServerPageCannotSupplyItsNextCursor()
    {
        using var wire = new Wire { PartialFirst = true }; var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100);
        Assert.Equal("50", wire.Requests[1]["seek"]); Assert.Equal("root-window", wire.Requests[1]["cursor"]);
    }
    [Fact]
    public async Task ChangedSortOrAccessCannotReuseAnOldAdjacentCursor()
    {
        using var wire = new Wire(); var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100); model.SelectedSort = "year";
        await model.LoadWindowAsync(100, 60);
        Assert.True(wire.Requests.Last().ContainsKey("seek")); Assert.Equal("year", wire.Requests.Last()["sort"]);
        wire.Client.InvalidateAccessContext(); await model.LoadWindowAsync(160, 60);
        Assert.True(wire.Requests.Last().ContainsKey("seek"));
    }
    [Fact]
    public async Task FailedRefetchDoesNotLeaveItsPreviouslySuccessfulBoundaryUsable()
    {
        using var wire = new Wire(); var model = new LibraryViewModel(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = "movie" } };
        await model.LoadWindowAsync(0, 100); wire.FailNextRoot = true;
        await model.LoadWindowAsync(0, 100, force: true); Assert.NotNull(model.ErrorMessage);
        await model.LoadWindowAsync(100, 60);
        Assert.Equal("100", wire.Requests.Last()["seek"]); Assert.Equal("root-window", wire.Requests.Last()["cursor"]);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly SiloApiClient Client;
        internal readonly List<Dictionary<string, string>> Requests = [];
        internal bool DelayNextRoot, PartialFirst, FailNextRoot;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Wire() { Client = new SiloApiClient(new HttpClient(this)); Client.SetBaseUrl("https://continuation.invalid"); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(value => value.Split('=', 2)).ToDictionary(value => Uri.UnescapeDataString(value[0]), value => Uri.UnescapeDataString(value[1])); Requests.Add(query);
            var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : int.Parse(query["cursor"].Split('-').Last()); var limit = int.Parse(query["limit"]);
            if (DelayNextRoot && offset == 0) { DelayNextRoot = false; Started.TrySetResult(); await Release.Task; }
            if (FailNextRoot && offset == 0) { FailNextRoot = false; return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\",\"message\":\"retry\"}") }; }
            if (PartialFirst && offset == 0) { limit = 50; PartialFirst = false; }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = Enumerable.Range(offset, limit).Select(index => new { content_id = "item-" + index, title = "Item " + index, type = "movie" }), total = 1000, total_exact = true, window_cursor = "root-window", page = new { has_more = true, next_cursor = "next-" + (offset + limit) } })) };
        }
    }
}
