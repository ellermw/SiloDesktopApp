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
        await model.RefreshGroupedCatalogAsync(800, 60);
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
        var refresh = model.RefreshGroupedCatalogAsync(660, 180);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1000, model.TotalCount);
        Assert.Equal(1000, model.DisplayTotalCount);
        Assert.Equal(1000, model.Items.Count);
        Assert.Same(letterIndex, model.LetterIndex);
        Assert.Same(old, model.GetWindowItem(700));
        wire.Release.TrySetResult(); await refresh;
        Assert.All(Enumerable.Range(660, 180), index => Assert.Equal(3, model.GetWindowItem(index)!.BoxSet!.PlayedCount));
        Assert.NotSame(old, model.GetWindowItem(700));
        var firstWindow = wire.CatalogRanges.Skip(before).First(request => !request.IncludeTotal);
        Assert.Equal(660, firstWindow.Start); Assert.Null(firstWindow.Cursor);
        Assert.All(totals, total => Assert.Equal(1000, total));
        Assert.All(displayedTotals, total => Assert.Equal(1000, total));
        Assert.Equal(1000, model.TotalCount);
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("library")]
    [InlineData("disable")]
    [InlineData("profile")]
    [InlineData("query")]
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

    [Fact]
    public async Task CapabilityFailureCannotFlattenTheQueryAndTheNextReadRetries()
    {
        using var wire = new Wire { CapabilityStatus = HttpStatusCode.ServiceUnavailable }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Empty(wire.Catalog); Assert.NotNull(model.ErrorMessage);
        wire.CapabilityStatus = HttpStatusCode.OK;
        await model.LoadWindowAsync(0, 60, force: true);
        Assert.NotEmpty(wire.Catalog); Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Group));
        Assert.Null(model.ErrorMessage); model.CancelCatalogLoads();
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
        public int CapabilityReads, FailWindowFromStart;
        public HttpStatusCode CapabilityStatus = HttpStatusCode.OK, CatalogStatus = HttpStatusCode.OK;
        public bool HoldCapabilities;
        public readonly TaskCompletionSource CapabilitiesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseCapabilities = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient? Client;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<(string? Group, string Method, string? Order)> Catalog = [];
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
                if (CapabilityStatus != HttpStatusCode.OK) return new(CapabilityStatus) { Content = new StringContent("{}") };
                return Json(JsonSerializer.Serialize(new { box_sets = Supported, letter_index = LetterSupport }));
            }
            if (path.EndsWith("letters"))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); Letters.Add(body);
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
                Catalog.Add((Value("group"), request.Method.Method, Value("order") ?? (Value("sort")?.StartsWith('-') == true ? "desc" : "asc")));
                var includeTotal = Value("skip_total") != "true" && Value("skip_total") != "True";
                CatalogRanges.Add((start, Value("cursor"), includeTotal));
                if (HoldCatalog) { Started.TrySetResult(); await Release.Task; }
                if (CatalogStatus != HttpStatusCode.OK && start >= FailWindowFromStart)
                    return new(CatalogStatus) { Content = new StringContent("{}") };
                var items = Enumerable.Range(start, Math.Min(limit, 1000 - start)).Select(i => new { content_id = Value("group") == "box_set" ? "box_set:9:" + i : "movie-" + i, type = Value("group") == "box_set" ? "box_set" : "movie", title = "Set " + i, box_set = Value("group") == "box_set" ? new { id = i.ToString(), library_id = "9", title = "Set " + i, movie_count = 3, played_count = PlayedCount } : null });
                return Json(JsonSerializer.Serialize(new { items, total = OmitWindowTotals && !includeTotal ? 0 : 1000, total_exact = includeTotal, window_cursor = "root", page = new { has_more = start + limit < 1000, next_cursor = "next-" + (start + limit) } }));
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
