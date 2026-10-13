using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class LibraryCursorScopeTests
{
    [Theory]
    [InlineData("movie", 159)]
    [InlineData("series", 215)]
    [InlineData("audiobook", 119)]
    public async Task LibraryTabScrollRetainsTheServerCursorScope(string type, int visibleEnd)
    {
        using var wire = new SignedCatalog();
        var model = Create(wire, type);
        await model.LoadCommand.ExecuteAsync(null);
        await model.EnsureRangeLoadedAsync(24, visibleEnd);
        Assert.Null(model.ErrorMessage);
        Assert.Equal(0, wire.InvalidCursors);
        Assert.All(Enumerable.Range(24, visibleEnd - 23), index => Assert.NotNull(model.GetWindowItem(index)));
        await model.PrefetchAroundAsync(24, visibleEnd, true);
        Assert.Equal(0, wire.InvalidCursors);
        Assert.Equal(1000, model.DisplayTotalCount); // The count request must share a valid scope too.
    }

    [Fact]
    public async Task EndOfLibraryAndDistantJumpUseTheSameScopeWithoutLosingItems()
    {
        using var wire = new SignedCatalog(); var model = Create(wire, "movie");
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadWindowAsync(900, 60);
        await model.EnsureRangeLoadedAsync(980, 999);
        Assert.Null(model.ErrorMessage); Assert.Equal(0, wire.InvalidCursors);
        Assert.All(Enumerable.Range(980, 20), index => Assert.NotNull(model.GetWindowItem(index)));
    }

    [Fact]
    public async Task LegacyLoadMoreAlsoSharesTheCatalogWindowScope()
    {
        using var wire = new SignedCatalog(); var model = Create(wire, "series");
        await model.LoadCommand.ExecuteAsync(null);
        await model.LoadMoreCommand.ExecuteAsync(null);
        Assert.Null(model.ErrorMessage); Assert.Equal(0, wire.InvalidCursors);
        Assert.NotNull(model.Items[0]);
    }

    [Fact]
    public async Task SortFilterAndProfileChangesStartTheirOwnCursorScope()
    {
        using var wire = new SignedCatalog(); var model = Create(wire, "movie");
        await model.LoadCommand.ExecuteAsync(null);
        model.SelectedSort = "year"; model.SelectedGenre = "Drama";
        await model.ApplyFilterCommand.ExecuteAsync(null);
        await model.EnsureRangeLoadedAsync(120, 279);
        wire.Client.SetProfile("another-profile");
        await model.LoadWindowAsync(0, 159);
        Assert.Null(model.ErrorMessage); Assert.Equal(0, wire.InvalidCursors);
        Assert.NotNull(model.GetWindowItem(159));
    }

    [Fact]
    public async Task LargeStructuredFilterAndPartialPageKeepTheSignedPostLimit()
    {
        using var wire = new SignedCatalog { PartialFirst = true }; var model = Create(wire, "movie");
        model.UseAdvancedRules = true;
        var group = new EditableQueryGroup(); group.Rules.Add(new QueryRule { Field = "actor", Op = "is", Value = new string('a', 33000) });
        model.AdvancedGroups.Add(group);
        await model.LoadCommand.ExecuteAsync(null);
        await model.EnsureRangeLoadedAsync(24, 215);
        Assert.Null(model.ErrorMessage); Assert.Equal(0, wire.InvalidCursors);
        Assert.All(Enumerable.Range(24, 192), index => Assert.NotNull(model.GetWindowItem(index)));
    }

    private static LibraryViewModel Create(SignedCatalog wire, string type)
        => new(new CatalogApi(wire.Client)) { Library = new Library { Id = 1, Type = type } };

    // Mirrors the official scope rule in catalog_items.go: limit is signed;
    // seek/cursor and include_total/skip_total are not. Cursors are opaque HMACs.
    private sealed class SignedCatalog : HttpMessageHandler
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("isolated-catalog-fixture-key");
        public SiloApiClient Client { get; }
        public int InvalidCursors;
        public bool PartialFirst;
        public SignedCatalog() { Client = new SiloApiClient(new HttpClient(this)); Client.SetBaseUrl("https://catalog-scope.invalid"); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Dictionary<string, string> values;
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                values = body.RootElement.EnumerateObject().ToDictionary(property => property.Name,
                    property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText());
            }
            else values = request.RequestUri!.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
            var scope = (request.Headers.TryGetValues("X-Profile-ID", out var profiles) ? profiles.First() : "no-profile") + "|" +
                string.Join('&', values.Where(pair => pair.Key is not ("seek" or "cursor" or "skip_total"))
                .OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value));
            var offset = values.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
            if (values.TryGetValue("cursor", out var cursor))
            {
                var parts = cursor.Split('.');
                if (parts.Length != 2 || parts[1] != Sign(scope, parts[0]))
                {
                    InvalidCursors++;
                    return await Reply(new { type = "https://fixture.invalid/problems/invalid_cursor", status = 400,
                        detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                }
                if (!values.ContainsKey("seek")) offset = int.Parse(parts[0]);
            }
            var count = Math.Min(int.Parse(values["limit"]), 1000 - offset);
            if (PartialFirst && offset == 0) { PartialFirst = false; count = 50; }
            return await Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = "item-" + index, title = "Title " + index, type = "movie" }),
                total = 1000, total_exact = !values.ContainsKey("skip_total"), window_cursor = "0." + Sign(scope, "0"),
                page = new { has_more = offset + count < 1000, next_cursor = (offset + count) + "." + Sign(scope, (offset + count).ToString()) } });
        }
        private static string Sign(string scope, string position) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(scope + "\0" + position)));
        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });
    }
}
