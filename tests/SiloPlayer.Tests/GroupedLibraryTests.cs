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
        var refresh = typeof(LibraryViewModel).GetMethod("RefreshGroupedCatalogAsync"); Assert.NotNull(refresh);
        await (Task)refresh.Invoke(model, null)!;
        Assert.NotSame(old, model.GetWindowItem(800)); Assert.Equal(3, model.GetWindowItem(800)!.BoxSet!.PlayedCount);
        Assert.Equal("box_set", wire.Catalog.Last().Group); model.CancelCatalogLoads();
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

    internal sealed class Wire : HttpMessageHandler
    {
        public bool Supported = true, LetterSupport = true, HoldCatalog;
        public int PlayedCount = 1;
        public bool HoldCapabilities;
        public readonly TaskCompletionSource CapabilitiesStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseCapabilities = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient? Client;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<(string? Group, string Method, string? Order)> Catalog = [];
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
                CapabilitiesStarted.TrySetResult(); if (HoldCapabilities) await ReleaseCapabilities.Task;
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
                if (HoldCatalog) { Started.TrySetResult(); await Release.Task; }
                var items = Enumerable.Range(start, Math.Min(limit, 1000 - start)).Select(i => new { content_id = Value("group") == "box_set" ? "box_set:9:" + i : "movie-" + i, type = Value("group") == "box_set" ? "box_set" : "movie", title = "Set " + i, box_set = Value("group") == "box_set" ? new { id = i.ToString(), library_id = "9", title = "Set " + i, movie_count = 3, played_count = PlayedCount } : null });
                return Json(JsonSerializer.Serialize(new { items, total = 1000, total_exact = Value("skip_total") != "true" && Value("skip_total") != "True", window_cursor = "root", page = new { has_more = start + limit < 1000, next_cursor = "next-" + (start + limit) } }));
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
