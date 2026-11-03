using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class BoxSetSearchTests
{
    [Fact]
    public async Task SearchReaderTrimsAndEscapesQueryAndUsesReturnedLibraryIdentity()
    {
        using var wire = new Wire(); var client = wire.Client();
        var result = await new CatalogApi(client).SearchBoxSetsAsync("  A&B  ", 9);
        Assert.Equal("?q=A%26B&limit=5&library_id=9", wire.LastSearch);
        Assert.Equal("12", Assert.Single(result.Items).LibraryId);
    }

    [Fact]
    public async Task FullSearchStartsOptionalBoxSetsWithoutWaitingForThem()
    {
        using var wire = new Wire { Hold = true }; var client = wire.Client();
        var vm = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client)) { Query = "Film" };
        await vm.SearchCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(vm.Results); Assert.False(vm.IsLoading);
        Assert.Equal(1, wire.SearchReads);
        vm.CancelPendingSearch(); wire.Release.TrySetResult();
    }

    [Fact]
    public async Task AggregateSectionDoesNotChangeCatalogCountOrPaginationOffset()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = Model(client); vm.Query = "Film"; await vm.SearchCommand.ExecuteAsync(null);
        await Until(() => vm.BoxSets.Items.Count == 1);
        Assert.Equal(60, vm.TotalCount); Assert.Single(vm.Results);
        await vm.LoadMoreAsync();
        Assert.Contains("seek=1", wire.LastCatalog); Assert.Equal(60, vm.TotalCount); Assert.Single(vm.Results); Assert.Equal(1, wire.SearchReads);
        vm.CancelPendingSearch();
    }

    [Theory] [InlineData("series")] [InlineData("episode")] [InlineData("audiobook")] [InlineData("ebook")] [InlineData("manga")]
    public async Task FullSearchWithoutMovieScopeSkipsAggregateEndpoint(string type)
    {
        using var wire = new Wire(); var vm = Model(wire.Client()); vm.Query = "Film"; vm.MediaType = type;
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.Results); Assert.Empty(vm.BoxSets.Items); Assert.Equal(0, wire.SearchReads); vm.CancelPendingSearch();
    }

    [Fact]
    public async Task AdvancedMediaScopeWithoutMoviesAlsoSkipsAggregates()
    {
        using var wire = new Wire(); var vm = Model(wire.Client()); vm.Query = "Film"; vm.AdvancedQuery.MediaScope = "episode";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Single(vm.Results); Assert.Equal(0, wire.SearchReads); Assert.Empty(vm.BoxSets.Items); vm.CancelPendingSearch();
    }

    [Theory] [InlineData("query")] [InlineData("library")] [InlineData("profile")] [InlineData("page")]
    public async Task FullSearchOptionalCompletionCannotOutliveItsQueryScope(string change)
    {
        using var wire = new Wire { Hold = true }; var client = wire.Client(); var vm = Model(client); vm.Query = "Film";
        await vm.SearchCommand.ExecuteAsync(null); await Until(() => wire.SearchReads == 1);
        if (change == "query") vm.Query = "Other";
        if (change == "library") vm.LibraryId = 99;
        if (change == "profile") client.SetProfile("other");
        if (change == "page") vm.CancelPendingSearch();
        wire.Release.TrySetResult(); await Until(() => !vm.BoxSets.IsLoading);
        Assert.Empty(vm.BoxSets.Items); Assert.Single(vm.Results); vm.CancelPendingSearch();
    }

    [Fact]
    public async Task FullSearchLibraryScopeGatesAndScopesBothKindsOfReads()
    {
        using var wire = new Wire(); var vm = Model(wire.Client()); vm.Query = "Film"; vm.LibraryId = 12;
        await vm.SearchCommand.ExecuteAsync(null); await Until(() => vm.BoxSets.Items.Count == 1);
        Assert.Contains("library_id=12", wire.LastCatalog); Assert.EndsWith("&library_id=12", wire.LastSearch);
        vm.LibraryId = 99; await vm.SearchCommand.ExecuteAsync(null); await Until(() => !vm.BoxSets.IsLoading);
        Assert.Empty(vm.BoxSets.Items); Assert.Equal(1, wire.SearchReads); Assert.Contains("library_id=99", wire.LastCatalog); vm.CancelPendingSearch();
    }

    private static SearchViewModel Model(SiloApiClient client) => new(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client));
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(10); Assert.True(ready()); }

    private sealed class Wire : HttpMessageHandler
    {
        public string? LastSearch, LastCatalog; public int SearchReads; public bool Hold;
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://boxset-search.invalid"); client.SetProfile("alpha"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = "{\"items\":[],\"page\":{\"has_more\":false}}";
            if (path == "/api/v2/catalog/search/capabilities") body = "{\"box_sets\":true,\"people_media_scope\":true}";
            if (path == "/api/v2/user/libraries") body = "{\"items\":[{\"id\":12,\"enabled\":true,\"box_sets_enabled\":true}]}";
            if (path == "/api/v2/catalog") { LastCatalog = request.RequestUri.Query; body = "{\"items\":[{\"content_id\":\"film\",\"title\":\"Film\",\"type\":\"movie\"}],\"total\":60,\"page\":{\"has_more\":true}}"; }
            if (path == "/api/v2/requests/status") body = "{\"requests_enabled\":false}";
            if (path == "/api/v2/catalog/box-sets")
            {
                LastSearch = request.RequestUri.Query; SearchReads++;
                if (Hold) await Release.Task;
                body = "{\"items\":[{\"id\":\"7\",\"library_id\":\"12\",\"title\":\"Films Box Set\",\"movie_count\":3}]}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
