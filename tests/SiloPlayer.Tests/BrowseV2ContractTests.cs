using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Tests;

public sealed class BrowseV2ContractTests
{
    [Fact]
    public async Task CatalogWindowUsesSignedCursorSeekSortAndJsonRules()
    {
        using var fixture = new Fixture("list_catalog_items_ok");
        var api = new CatalogApi(fixture.Client);
        var first = await api.GetCatalogAsync(1, sort: "year", order: "desc", limit: 2,
            resolution: "2160p", includeTotal: false,
            extraRules: [new QueryRule { Field = "year", Op = "between", Value = new[] { 1990, 1999 } }]);
        Assert.Equal("movie:heat-1995", first.Items[0].ContentId);
        Assert.Equal(3, first.Total);
        Assert.True(first.TotalExact);
        Assert.True(first.HasMore);
        Assert.NotEmpty(first.Snapshot!);
        await api.GetCatalogAsync(1, sort: "year", order: "desc", limit: 2, offset: 40, snapshot: first.Snapshot);
        var query = QueryHelpers.ParseQuery(fixture.Requests[0].Query);
        Assert.Equal("/api/v2/catalog", fixture.Requests[0].AbsolutePath);
        Assert.Equal("-year", query["sort"]);
        Assert.Equal("true", query["skip_total"]);
        Assert.False(query.ContainsKey("offset"));
        Assert.False(query.ContainsKey("order"));
        Assert.False(query.ContainsKey("include_total"));
        Assert.False(query.ContainsKey("resolution"));
        using var groups = JsonDocument.Parse(query["groups"].ToString());
        Assert.Equal("resolution", groups.RootElement[0].GetProperty("rules")[0].GetProperty("field").GetString());
        Assert.Equal(1999, groups.RootElement[0].GetProperty("rules")[1].GetProperty("value")[1].GetInt32());
        var next = QueryHelpers.ParseQuery(fixture.Requests[1].Query);
        Assert.Equal("40", next["seek"]);
        Assert.Equal(first.Snapshot, next["cursor"].ToString());
    }

    [Fact]
    public async Task HomeAndLibrarySectionResourcesAreWrappedForExistingCallers()
    {
        using var fixture = new Fixture("get_home_section_items_ok");
        var home = await new HomeApi(fixture.Client).GetSectionItemsAsync("continue_watching");
        var library = await new CatalogApi(fixture.Client).GetLibrarySectionItemsAsync(2, "continue_watching");
        Assert.Equal("Continue Watching", home.Section!.Title);
        Assert.Equal(1200.5, home.Section.Items[0].PositionSeconds);
        Assert.True(home.Section.Items[0].UserState!.InWatchlist);
        Assert.Equal(home.Section.Id, library.Section!.Id);
        Assert.Equal("/api/v2/library/2/sections/continue_watching/items", fixture.Requests[1].AbsolutePath);
    }

    [Fact]
    public async Task TechnicalFacetsComeFromTechnicalObjectAndFiltersExcludeUnsupportedSearchQuery()
    {
        using var fixture = new Fixture("get_catalog_filters_ok");
        var filters = await new CatalogApi(fixture.Client).GetFiltersAsync(1, q: "Heat");
        Assert.Equal("2160p", Assert.Single(filters.Resolutions));
        Assert.Equal("en", Assert.Single(filters.SubtitleLanguages));
        Assert.False(QueryHelpers.ParseQuery(fixture.Requests[0].Query).ContainsKey("q"));
    }

    [Fact]
    public async Task ItemDetailPreservesStringFileIdsAndViewerState()
    {
        using var fixture = new Fixture("get_catalog_item_ok");
        var item = await new CatalogApi(fixture.Client).GetItemDetailAsync("movie:heat-1995");
        Assert.Equal(120, item.Versions[0].FileId);
        Assert.Equal(120, item.UserData!.LastFileId);
        Assert.Equal("7", item.Cast[0].PersonId);
        Assert.True(item.UserState!.IsFavorite);
    }

    [Fact]
    public async Task SeasonEpisodeAndVersionCollectionsUseItems()
    {
        using var seasons = new Fixture("list_series_seasons_ok");
        Assert.Equal(1, Assert.Single((await new CatalogApi(seasons.Client).GetSeasonsAsync("series:severance")).Seasons).SeasonNumber);
        using var episodes = new Fixture("list_catalog_item_episodes_ok");
        Assert.NotEmpty((await new CatalogApi(episodes.Client).GetItemEpisodesAsync("season:one")).Episodes);
        using var versions = new Fixture("list_catalog_item_versions_ok");
        Assert.Equal(120, Assert.Single(await new CatalogApi(versions.Client).GetItemVersionsAsync("movie:heat-1995")).FileId);
    }

    [Fact]
    public async Task PeopleSearchUsesBoundedCollectionWithoutOffset()
    {
        using var fixture = new Fixture("list_people_ok");
        var people = await new PeopleApi(fixture.Client).GetPeopleAsync("Al", 20);
        Assert.Equal("7", Assert.Single(people).Id);
        Assert.Equal("/api/v2/catalog/people", fixture.Requests[0].AbsolutePath);
        Assert.False(QueryHelpers.ParseQuery(fixture.Requests[0].Query).ContainsKey("offset"));
    }

    [Fact]
    public async Task LibrariesCacheDoesNotCrossProfileBoundaries()
    {
        using var fixture = new Fixture("user_libraries", "{\"items\":[{\"id\":\"20\",\"name\":\"Other\"}]}");
        var api = new CatalogApi(fixture.Client);
        Assert.Equal(12, Assert.Single(await api.GetLibrariesAsync()).Id);
        Assert.Equal(12, Assert.Single(await api.GetLibrariesAsync()).Id);
        Assert.Single(fixture.Requests);
        fixture.Client.SetProfile("other");
        Assert.Equal(20, Assert.Single(await api.GetLibrariesAsync()).Id);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task MangaFileCollectionRetainsFilesAndFolderPaths()
    {
        using var fixture = new Fixture("list_catalog_item_manga_files_ok");
        var files = await new CatalogApi(fixture.Client).GetMangaSeriesFilesAsync("manga:berserk");
        Assert.Equal("c001.cbz", Assert.Single(files.Files).FileName);
        Assert.Equal("/media/manga/Berserk", Assert.Single(files.FolderPaths!));
    }

    [Fact]
    public async Task RecommendationsMapResourceAndCollectionShapesToExistingRows()
    {
        using var rows = new Fixture("list_for_you_rows_ok");
        var result = await new RecommendationsApi(rows.Client).GetForYouRowsAsync();
        Assert.Equal("Because you enjoy Crime", Assert.Single(result.Rows).Label);
        Assert.Equal("movie:heat-1995", result.Rows[0].Items[0].MediaItemId);
        Assert.Equal("Heat", result.Rows[0].Items[0].Title);
        using var main = new Fixture("get_for_you_main_ok");
        Assert.NotEmpty((await new RecommendationsApi(main.Client).GetForYouMainAsync()).Rows[0].Items);
        using var discover = new Fixture("get_discover_ok");
        Assert.NotEmpty((await new RecommendationsApi(discover.Client).GetDiscoverAsync()).Rows);
        using var section = new Fixture("get_recommendation_section_ok");
        var row = await new RecommendationsApi(section.Client).GetSectionAsync("genre", "Crime & Drama");
        Assert.NotEmpty(row.Label);
        Assert.Equal("Crime & Drama", QueryHelpers.ParseQuery(section.Requests[0].Query)["key"]);
        Assert.Equal("/api/v2/recommendations/section/genre", section.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task WatchTonightUsesRepeatedUnbracketedParametersAndItems()
    {
        using var fixture = new Fixture("list_watch_tonight_cards_ok");
        var result = await new RecommendationsApi(fixture.Client).GetWatchTonightCardsAsync("discover",
            ["Crime", "Drama"], ["movie:seen"], limit: 12);
        Assert.NotEmpty(result.Cards);
        var query = QueryHelpers.ParseQuery(fixture.Requests[0].Query);
        Assert.Equal(2, query["genres"].Count);
        Assert.Equal("movie:seen", query["exclude_ids"]);
        Assert.False(query.ContainsKey("genres[]"));
    }

    [Fact]
    public async Task FavoriteOffsetWalksOpaqueCursorsWithConstantPageSize()
    {
        using var fixture = new Fixture("list_favorites_ok", "{\"items\":[{\"content_id\":\"movie:next\"}],\"page\":{\"has_more\":false}}");
        var page = await new CatalogApi(fixture.Client).GetFavoritesAsync(1, 1);
        Assert.Equal("movie:next", Assert.Single(page.Items).ContentId);
        Assert.False(page.HasMore);
        Assert.Equal(2, fixture.Requests.Count);
        var query = QueryHelpers.ParseQuery(fixture.Requests[1].Query);
        Assert.NotEmpty(query["cursor"].ToString());
        Assert.Equal("1", query["limit"]);
        Assert.False(query.ContainsKey("offset"));
    }

    [Fact]
    public async Task EmptyCursorPageDoesNotLoopForever()
    {
        using var fixture = new Fixture("list_favorites_ok", "{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"repeat\"}}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CatalogApi(fixture.Client).GetFavoritesAsync(1, 2));
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task MetadataAiCapabilityHonorsAllowedState()
    {
        using var fixture = new Fixture("get_metadata_ai_capability_ok");
        var status = await new CatalogApi(fixture.Client).GetMetadataAiStatusAsync();
        Assert.True(status.Enabled);
        Assert.Equal("button", status.OnView);
    }

    private sealed class Fixture : HttpMessageHandler
    {
        private readonly string _first;
        private readonly string? _next;
        private readonly HttpClient _http;
        public List<Uri> Requests { get; } = [];
        public SiloApiClient Client { get; }
        public Fixture(string name, string? next = null)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "BrowseV2"))) root = root.Parent;
            _first = File.ReadAllText(Path.Combine(root!.FullName, "tests", "SiloPlayer.Tests", "Fixtures", "BrowseV2", name + ".json"));
            _next = next;
            _http = new HttpClient(this, disposeHandler: false);
            Client = new SiloApiClient(_http);
            Client.SetBaseUrl("https://fixture.invalid");
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Requests.Count > 1 && _next != null ? _next : _first) });
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
