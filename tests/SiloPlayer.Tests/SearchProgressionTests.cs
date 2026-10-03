using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SearchProgressionTests
{
    [Fact]
    public async Task DuplicateSearchKeepsCatalogWindowAndLoadMoreState()
    {
        using var wire = new Wire();
        var vm = wire.ViewModel(); vm.Query = "fixture"; vm.MediaScope = "audiobook"; vm.MediaType = "audiobook";
        await vm.SearchCommand.ExecuteAsync(null);
        await vm.SearchCommand.ExecuteAsync(null);
        await vm.LoadMoreAsync();
        Assert.Equal(new[] { "first", "second" }, vm.Results.Select(r => r.ContentId));
        Assert.Equal(2, wire.Paths.Count(p => p.StartsWith("/api/v2/catalog?")));
        Assert.Contains(wire.Paths, p => p.Contains("seek=1") && p.Contains("cursor=window"));
    }

    [Fact]
    public async Task SlowPeopleArriveIndependentlyWithoutHoldingPrimarySearch()
    {
        using var wire = new Wire { DelayPeople = true };
        var vm = wire.ViewModel(); vm.Query = "fixture"; vm.MediaScope = "audiobook";
        await vm.SearchCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(vm.Results); Assert.False(vm.IsLoading); Assert.Empty(vm.PeopleResults);
        wire.People.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"person:one\",\"name\":\"Fixture Person\"}]}") });
        await Until(() => vm.PeopleResults.Count == 1);
        Assert.Equal("person:one", vm.PeopleResults[0].Id);
    }

    [Fact]
    public async Task AdvancedGroupsKeepTheirBooleanStructureAcrossCatalogPages()
    {
        using var wire = new Wire(); var vm = wire.ViewModel(); vm.Query = "fixture"; vm.MediaScope = "audiobook";
        vm.AdvancedQuery.Match = "any";
        vm.AdvancedQuery.Groups.Add(new() { Match = "all", Rules = [new() { Field = "year", Op = "between", Value = new[] { 2020, 2026 } }, new() { Field = "hdr", Op = "is", Value = false }] });
        vm.AdvancedQuery.Groups.Add(new() { Match = "any", Rules = [new() { Field = "original_language", Op = "in", Value = new[] { "en", "de" } }] });
        await vm.SearchCommand.ExecuteAsync(null); await vm.LoadMoreAsync();
        var pages = wire.Paths.Where(p => p.StartsWith("/api/v2/catalog?")).ToList();
        Assert.Equal(2, pages.Count);
        foreach (var page in pages)
        {
            var decoded = Uri.UnescapeDataString(page); Assert.Contains("&match=any", decoded);
            Assert.Contains("\"match\":\"all\"", decoded); Assert.Contains("\"value\":[2020,2026]", decoded); Assert.Contains("\"value\":false", decoded);
        }
        vm.AdvancedQuery.Groups[0].Rules[0].Value = new[] { 2010, 2019 };
        await vm.SearchCommand.ExecuteAsync(null); Assert.Equal(3, wire.Paths.Count(p => p.StartsWith("/api/v2/catalog?")));
    }

    [Fact]
    public async Task PeopleSearchRequiresViewerScopeCapabilityAndUsesSelectedScope()
    {
        using var wire = new Wire(); var vm = wire.ViewModel(); vm.Query = "fixture"; vm.MediaScope = "audiobook";
        await vm.SearchCommand.ExecuteAsync(null); await Until(() => wire.Paths.Any(p => p.Contains("media_scope=audiobook")));
        wire.PeopleScoped = false; wire.Paths.Clear(); vm.Query = "different"; await vm.SearchCommand.ExecuteAsync(null);
        await Task.Delay(20);
        Assert.DoesNotContain(wire.Paths, p => p.StartsWith("/api/v2/catalog/people"));
    }

    [Fact]
    public async Task RequestPagesRespectScopeAndDoNotReplaceOrRefetchCatalogResults()
    {
        using var wire = new Wire();
        var vm = wire.ViewModel(); vm.Query = "fixture"; vm.MediaType = "movie";
        await vm.SearchCommand.ExecuteAsync(null);
        await Until(() => vm.OutsideLibraryResults.Count > 0);
        await vm.SetOutsidePageAsync(2);
        Assert.Single(vm.Results); Assert.Equal("first", vm.Results[0].ContentId);
        Assert.Equal(2, vm.OutsidePage);
        Assert.Contains(wire.Paths, p => p.Contains("media_type=movie") && p.Contains("page=2"));
        Assert.Single(wire.Paths.Where(p => p.StartsWith("/api/v2/catalog?")));
    }

    private static async Task Until(Func<bool> condition)
    { var deadline = DateTime.UtcNow.AddSeconds(2); while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(10); } }

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Paths = [];
        public bool DelayPeople;
        public bool PeopleScoped = true;
        public TaskCompletionSource<HttpResponseMessage> People = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SearchViewModel ViewModel()
        {
            var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://search-fixture.invalid");
            return new(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client));
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery; Paths.Add(path);
            if (path.StartsWith("/api/v2/catalog/people") && DelayPeople) return People.Task;
            var json = path.StartsWith("/api/v2/catalog?") ? path.Contains("seek=1")
                ? "{\"items\":[{\"content_id\":\"second\"}],\"page\":{\"has_more\":false}}"
                : "{\"items\":[{\"content_id\":\"first\"}],\"window_cursor\":\"window\",\"page\":{\"has_more\":true}}"
                : path.StartsWith("/api/v2/catalog/search/capabilities") ? $"{{\"people_media_scope\":{PeopleScoped.ToString().ToLowerInvariant()}}}"
                : path.StartsWith("/api/v2/requests/status") ? "{\"requests_enabled\":true}"
                : path.StartsWith("/api/v2/requests/search") ? "{\"page\":2,\"total_pages\":3,\"total_results\":42,\"results\":[{\"media_type\":\"movie\",\"tmdb_id\":42,\"availability\":\"missing\"}]}"
                : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
