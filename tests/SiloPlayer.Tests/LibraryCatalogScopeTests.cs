using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// Library browse sends the plain catalog query. The server's catalog accepts only group=work
/// (silo-server internal/apiv2/catalog_items.go), so box-set libraries browse their movies
/// individually; box sets stay reachable through search and movie detail.
/// </summary>
public sealed class LibraryCatalogScopeTests
{
    [Theory]
    [InlineData("movie")]
    [InlineData("movies")]
    [InlineData("mixed")]
    public async Task BoxSetLibrariesBrowseWithoutAGroupParameter(string type)
    {
        foreach (var large in new[] { false, true })
        {
            using var wire = new Wire(); var model = wire.Create(type, boxSets: true);
            if (large)
            {
                model.UseAdvancedRules = true;
                model.AdvancedRules.Add(new QueryRule { Field = "title", Op = "contains", Value = new string('x', 34000) });
            }
            await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
            await model.JumpToLetterCommand.ExecuteAsync("M");
            await model.EnsureRangeLoadedAsync(800, 859); await model.PrefetchAroundAsync(800, 859, true);
            Assert.NotEmpty(wire.Catalog); Assert.NotEmpty(wire.Letters);
            Assert.All(wire.Catalog, request => { Assert.Null(request.Group); Assert.Equal(large ? "POST" : "GET", request.Method); });
            Assert.All(wire.Letters, body => Assert.False(body.TryGetProperty("group", out _)));
            Assert.Null(model.ErrorMessage);
            Assert.Equal(1000, model.DisplayTotalCount);
            Assert.Equal("movie", model.GetWindowItem(800)!.Type);
            model.CancelCatalogLoads();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("movie")]
    [InlineData("episode")]
    [InlineData("audiobook")]
    public async Task MixedLibraryBrowseTypeScopesCatalogCountsSeekAndLetters(string? type)
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
            Assert.All(wire.Catalog, request => { Assert.Equal(type, request.Type); Assert.Equal(large ? "POST" : "GET", request.Method); });
            Assert.All(wire.Letters, body =>
                Assert.Equal(string.IsNullOrEmpty(type) ? null : type, body.TryGetProperty("type", out var scope) ? scope.GetString() : null));
            Assert.Contains(wire.CatalogRanges, request => request.IncludeTotal);
            Assert.Equal(1000, model.DisplayTotalCount); Assert.NotNull(model.GetWindowItem(800));
            model.CancelCatalogLoads();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingBrowseTypeRetiresCardsCursorsAndLetters(bool applyFilter)
    {
        using var wire = new Wire(); var model = wire.Create("mixed");
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.NotNull(model.GetWindowItem(0)); Assert.NotNull(model.LetterIndex);
        foreach (var type in new[] { "episode", "movie" })
        {
            var before = wire.Catalog.Count;
            model.SelectedType = type;
            Assert.Null(model.GetWindowItem(0)); Assert.Null(model.LetterIndex);
            if (applyFilter) await model.ApplyFilterCommand.ExecuteAsync(null);
            else await model.LoadWindowAsync(0, 60);
            await model.EnsureLetterIndexAsync();
            Assert.Null(wire.CatalogRanges[before].Cursor);
            Assert.All(wire.Catalog.Skip(before), request => Assert.Equal(type, request.Type));
            Assert.Equal(type, wire.Letters.Last().GetProperty("type").GetString());
        }
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("type")]
    [InlineData("disable")]
    public async Task ChangedLibraryScopeDiscardsWindowsAndIndex(string change)
    {
        using var wire = new Wire(); var model = wire.Create(); await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var before = wire.Catalog.Count;
        model.Library = change == "type" ? new Library { Id = 9, Type = "mixed" } : new Library { Id = 9, Type = "movie", Enabled = false };
        Assert.Null(model.GetWindowItem(0)); Assert.Null(model.LetterIndex);
        await model.LoadWindowAsync(800, 60);
        Assert.Null(wire.CatalogRanges[before].Cursor);
        model.CancelCatalogLoads();
    }

    [Fact]
    public async Task TogglingBoxSetsKeepsTheLoadedCatalog()
    {
        using var wire = new Wire(); var model = wire.Create(boxSets: false);
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var item = model.GetWindowItem(0); var index = model.LetterIndex; var windows = wire.CatalogRanges.Count(read => !read.IncludeTotal);
        model.Library = new Library { Id = 9, Type = "movie", BoxSetsEnabled = true };
        await model.LoadWindowAsync(0, 60);
        Assert.Same(item, model.GetWindowItem(0)); Assert.Same(index, model.LetterIndex);
        Assert.Equal(windows, wire.CatalogRanges.Count(read => !read.IncludeTotal));
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("library")]
    [InlineData("disable")]
    [InlineData("profile")]
    [InlineData("query")]
    [InlineData("type")]
    [InlineData("navigation")]
    public async Task LatePageCannotPopulateRetiredScope(string change)
    {
        using var wire = new Wire(); var model = wire.Create(); await model.LoadCommand.ExecuteAsync(null);
        wire.HoldCatalog = true;
        var read = model.LoadWindowAsync(800, 60, force: true); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (change == "library") model.Library = new Library { Id = 10, Type = "movie" };
        if (change == "disable") model.Library = new Library { Id = 9, Type = "movie", Enabled = false };
        if (change == "profile") wire.Client!.SetProfile("replacement");
        if (change == "query") { model.SelectedGenre = "Drama"; model.CancelCatalogLoads(); }
        if (change == "type") model.SelectedType = "episode";
        if (change == "navigation") model.SuspendCatalogLoads();
        wire.Release.TrySetResult(); await read;
        Assert.Null(model.GetWindowItem(800)); model.CancelCatalogLoads();
    }

    internal sealed class Wire : HttpMessageHandler
    {
        public bool HoldCatalog;
        public const int CatalogTotal = 1000;
        public SiloApiClient? Client;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<(string? Group, string Method, string? Type)> Catalog = [];
        public readonly List<(int Start, string? Cursor, bool IncludeTotal)> CatalogRanges = [];
        public readonly List<JsonElement> Letters = [];
        public LibraryViewModel Create(string type = "movie", bool boxSets = true)
        {
            var client = Client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://scope.invalid"); client.SetProfile("synthetic");
            return new(new CatalogApi(client)) { Library = new Library { Id = 9, Type = type, BoxSetsEnabled = boxSets } };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("capabilities"))
                return Json(JsonSerializer.Serialize(new { box_sets = true, letter_index = true }));
            if (path.EndsWith("letters"))
            {
                Letters.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone());
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
                Catalog.Add((Value("group"), request.Method.Method, Value("type")));
                var includeTotal = Value("skip_total") != "true" && Value("skip_total") != "True";
                CatalogRanges.Add((start, Value("cursor"), includeTotal));
                if (HoldCatalog) { Started.TrySetResult(); await Release.Task; }
                var items = Enumerable.Range(start, Math.Min(limit, Math.Max(0, CatalogTotal - start)))
                    .Select(i => new { content_id = "movie-" + i, type = "movie", title = "Movie " + i });
                return Json(JsonSerializer.Serialize(new { items, total = CatalogTotal, total_exact = includeTotal, window_cursor = "root", page = new { has_more = start + limit < CatalogTotal, next_cursor = "next-" + (start + limit) } }));
            }
            return Json("{}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
