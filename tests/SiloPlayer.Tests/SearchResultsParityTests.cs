using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>Official silo-server 3b2b32168 (#4450): the search results page's view, overview, count, sort and request-paging rules.</summary>
public sealed class SearchResultsParityTests
{
    private static List<int> Items(int count) => Enumerable.Range(0, count).ToList();

    [Fact]
    public void OverviewShowsTwoRowsOfBestMatchesThenThreeRowsOfMore()
    {
        var split = SearchResultsPresentation.SplitOverview(Items(40), 6, serverHasMore: false);
        Assert.Equal(12, split.Best.Count);
        Assert.Equal(18, split.More.Count);
        Assert.Equal(12, split.More[0]);
        Assert.True(split.HasMoreThanShown);

        var all = SearchResultsPresentation.SplitOverview(Items(30), 6, serverHasMore: false);
        Assert.Equal(18, all.More.Count);
        Assert.False(all.HasMoreThanShown);

        var few = SearchResultsPresentation.SplitOverview(Items(5), 6, serverHasMore: false);
        Assert.Equal(5, few.Best.Count);
        Assert.Empty(few.More);
        Assert.False(few.HasMoreThanShown);
    }

    [Fact]
    public void OverviewOffersShowAllWhenTheServerHasMoreOrTheTitlesOutrunTheRows()
    {
        Assert.True(SearchResultsPresentation.SplitOverview(Items(3), 6, serverHasMore: true).HasMoreThanShown);
        Assert.True(SearchResultsPresentation.SplitOverview(Items(31), 6, serverHasMore: false).HasMoreThanShown);
        // An unmeasured grid counts as one column, so a row is never zero titles wide.
        var split = SearchResultsPresentation.SplitOverview(Items(10), 0, serverHasMore: false);
        Assert.Equal(2, split.Best.Count);
        Assert.Equal(3, split.More.Count);
        Assert.True(split.HasMoreThanShown);
    }

    [Theory]
    [InlineData(SearchResultsView.Requests, false, false, false, SearchResultsView.Overview)]
    [InlineData(SearchResultsView.Requests, false, true, false, SearchResultsView.Overview)]
    [InlineData(SearchResultsView.Requests, false, true, true, SearchResultsView.Requests)]
    // While discovery has not answered, a linked request view stays put.
    [InlineData(SearchResultsView.Requests, true, false, false, SearchResultsView.Requests)]
    [InlineData(SearchResultsView.Library, false, false, false, SearchResultsView.Library)]
    [InlineData(SearchResultsView.Overview, false, false, false, SearchResultsView.Overview)]
    public void RequestViewFallsBackToTheOverviewOnlyOnceDiscoveryHasAnswered(SearchResultsView requested, bool deciding, bool enabled, bool hasResults, SearchResultsView expected)
        => Assert.Equal(expected, SearchResultsPresentation.ShownView(requested, deciding, enabled, hasResults));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(3, 1, true)]
    // A later page can hold titles the first page's library matches hid.
    [InlineData(0, 2, true)]
    public void RequestResultsExistWhenTheFirstPageHasTitlesOrLaterPagesExist(int firstPage, int totalPages, bool expected)
        => Assert.Equal(expected, SearchResultsPresentation.HasRequestResults(firstPage, totalPages));

    [Fact]
    public void RequestTabNeedsDiscoveryForTheScopeAndSomethingToRequest()
    {
        Assert.True(SearchResultsPresentation.OffersRequestView(true, true));
        Assert.False(SearchResultsPresentation.OffersRequestView(false, true));
        Assert.False(SearchResultsPresentation.OffersRequestView(true, false));
    }

    [Fact]
    public void CountsReadAtLeastWhenTheTotalIsNotExact()
    {
        Assert.Equal("60+", SearchResultsPresentation.CountLabel(60, more: true));
        Assert.Equal("25", SearchResultsPresentation.CountLabel(25, more: false));
    }

    [Fact]
    public void SortedByLineNamesTheSortAndRelevanceIsTheDefault()
    {
        Assert.Equal("Sorted by Title", SearchResultsPresentation.SortedByText("Title"));
        Assert.True(SearchResultsPresentation.SortsByRelevance("relevance"));
        Assert.True(SearchResultsPresentation.SortsByRelevance(null));
        Assert.False(SearchResultsPresentation.SortsByRelevance("title"));
        Assert.False(SearchResultsPresentation.SortsByRelevance("added_at"));
    }

    [Theory]
    // A search ranks by relevance on its own, so the default sends nothing.
    [InlineData("relevance", false, "desc", null, null)]
    // Picking Relevance sends it, without an order of its own.
    [InlineData("relevance", true, "asc", "relevance", "desc")]
    [InlineData("title", false, "asc", "title", "asc")]
    // Date Added is only sent once the viewer picked it.
    [InlineData("added_at", false, "desc", "added_at", "desc")]
    public void SearchSortIsOnlySentWhenTheViewerChoseIt(string field, bool relevanceChosen, string order, string? sort, string? sentOrder)
        => Assert.Equal((sort, sentOrder), SearchResultsPresentation.RequestSort(field, relevanceChosen, order));

    [Fact]
    public void RequestSuggestionsDropTitlesTheLibraryHasAndStopAtTheGridLimit()
    {
        var results = Enumerable.Range(1, 30).Select(id => new RequestMediaResult { TmdbId = id, MediaType = "movie", Title = "T" + id, Availability = id % 5 == 0 ? "available" : "unavailable" }).ToList();
        var shown = SearchResultsPresentation.RequestSuggestions(results, SearchResultsPresentation.RequestPageLimit);
        Assert.Equal(20, shown.Count);
        Assert.DoesNotContain(shown, item => item.Availability == "available");
        Assert.Equal(new[] { 1, 2, 3, 4, 6 }, shown.Take(5).Select(item => item.TmdbId));
        Assert.Empty(SearchResultsPresentation.RequestSuggestions(null, 20));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-4, 0)]
    [InlineData(9, 9)]
    [InlineData(500, 500)]
    // TMDB serves at most 500 pages of any list, whatever total it reports.
    [InlineData(12000, 500)]
    public void RequestPageCountStopsAtWhatTmdbServes(int? reported, int expected)
        => Assert.Equal(expected, SearchResultsPresentation.TmdbPageCount(reported));

    [Fact]
    public void RequestPagerNamesTheRightPageAndOnlyOffersWhereToGo()
    {
        var first = SearchResultsPresentation.RequestPager(1, 9);
        Assert.False(first.HasPrevious); Assert.True(first.HasNext);
        Assert.Equal("Page 1 of 9", SearchResultsPresentation.RequestPagerPosition(first));

        var last = SearchResultsPresentation.RequestPager(9, 9);
        Assert.True(last.HasPrevious); Assert.False(last.HasNext);

        // A page that failed to load does not know the total: "Page 3", with a way back.
        var failed = SearchResultsPresentation.RequestPager(3, 0);
        Assert.True(failed.HasPrevious); Assert.False(failed.HasNext);
        Assert.Equal("Page 3", SearchResultsPresentation.RequestPagerPosition(failed));

        var single = SearchResultsPresentation.RequestPager(1, 1);
        Assert.False(single.HasPrevious || single.HasNext);
    }

    [Fact]
    public void NothingFoundNeedsEveryAnswerIn()
    {
        Assert.True(SearchResultsPresentation.NothingFound(true, true, false, 0, 0, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(false, true, false, 0, 0, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, false, false, 0, 0, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, true, true, 0, 0, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, true, false, 1, 0, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, true, false, 0, 2, false, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, true, false, 0, 0, true, false));
        Assert.False(SearchResultsPresentation.NothingFound(true, true, false, 0, 0, false, true));
    }

    // ---- the requests the search sends ----

    [Fact]
    public async Task ADefaultSearchSendsNoSortAndRelevanceOrAPickedSortIsSent()
    {
        using var wire = new Wire(); var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.DoesNotContain("sort=", wire.LastCatalog);
        Assert.True(vm.SortsByRelevance);

        vm.RelevanceChosen = true; vm.Query = "Fixture again";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains("sort=-relevance", wire.LastCatalog);

        vm.RelevanceChosen = false; vm.SortField = "title"; vm.SortOrder = "asc";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains("sort=title", wire.LastCatalog);
        Assert.False(vm.SortsByRelevance);

        // Date Added is a sort like any other once picked.
        vm.SortField = "added_at"; vm.SortOrder = "desc";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Contains("sort=-added_at", wire.LastCatalog);
    }

    [Fact]
    public async Task TheFirstRequestPageFeedsTheTabCountAndLaterPagesDoNotChangeIt()
    {
        using var wire = new Wire { RequestTotalPages = 4 }; var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null);
        await Until(() => vm.OutsideSettled);
        Assert.Equal(3, vm.OutsideFirstPageCount); // 4 results, one already in the library
        Assert.Equal(4, vm.OutsideTotalPages);
        Assert.True(vm.HasRequestResults);
        Assert.Contains("page=1", wire.Requests.Last(path => path.StartsWith("/api/v2/requests/search")));

        wire.RequestResults = 1;
        await vm.SetOutsidePageAsync(3);
        Assert.Contains("page=3", wire.Requests.Last(path => path.StartsWith("/api/v2/requests/search")));
        Assert.Equal(3, vm.OutsidePage);
        Assert.Single(vm.OutsideLibraryResults);
        Assert.Equal(3, vm.OutsideFirstPageCount);
    }

    [Fact]
    public async Task PagingPastTheLastPageIsClampedToWhatTmdbServes()
    {
        using var wire = new Wire { RequestTotalPages = 4 }; var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null); await Until(() => vm.OutsideSettled);
        await vm.SetOutsidePageAsync(99);
        Assert.Contains("page=4", wire.Requests.Last(path => path.StartsWith("/api/v2/requests/search")));
        await vm.SetOutsidePageAsync(0);
        Assert.Contains("page=1", wire.Requests.Last(path => path.StartsWith("/api/v2/requests/search")));
    }

    [Fact]
    public async Task AQueryOfOneCharacterNeverSearchesTmdbAndNeverWaitsForIt()
    {
        using var wire = new Wire(); var vm = wire.Model("a");
        await vm.SearchCommand.ExecuteAsync(null);
        await Until(() => vm.OutsideSettled);
        Assert.DoesNotContain(wire.Requests, path => path.StartsWith("/api/v2/requests/search"));
        Assert.False(vm.RequestsDeciding);
    }

    [Fact]
    public async Task ARequestPageThatFailsKeepsItsPageNumberAndOffersRetry()
    {
        using var wire = new Wire { RequestTotalPages = 4 }; var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null); await Until(() => vm.OutsideSettled);
        wire.FailRequestSearch = true;
        await vm.SetOutsidePageAsync(3);
        Assert.Equal("Couldn’t load page 3.", vm.OutsideError);
        Assert.Equal(3, vm.OutsideRequestedPage);
        Assert.False(vm.RequestsDeciding);
        wire.FailRequestSearch = false;
        await vm.SetOutsidePageAsync(vm.OutsideRequestedPage);
        Assert.Null(vm.OutsideError);
        Assert.Equal(3, vm.OutsidePage);
    }

    [Fact]
    public async Task AFailedLibrarySearchStillShowsWhatCanBeRequested()
    {
        using var wire = new Wire { FailCatalog = true }; var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null);
        await Until(() => vm.OutsideSettled);
        Assert.NotNull(vm.ErrorMessage);
        Assert.True(vm.SearchFailed);
        Assert.False(vm.LibraryResultsKnown);
        Assert.NotEmpty(vm.OutsideLibraryResults);
    }

    [Fact]
    public async Task AFailedLoadMoreKeepsTheLoadedTitlesAndIsNotAFailedSearch()
    {
        using var wire = new Wire { CatalogHasMore = true }; var vm = wire.Model("Fixture");
        await vm.SearchCommand.ExecuteAsync(null);
        wire.FailCatalog = true;
        await vm.LoadMoreAsync();
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.SearchFailed);
        Assert.True(vm.LibraryResultsKnown);
        Assert.Single(vm.Results);

        // The next window is read again from where it stopped, and a read that works leaves no notice.
        wire.FailCatalog = false;
        await vm.LoadMoreAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.Contains("seek=1", wire.LastCatalog);
        Assert.Single(vm.Results);
    }

    [Fact]
    public async Task ASearchCancelledByLeavingIsRunAgainOnBack()
    {
        using var wire = new Wire(); var vm = wire.Model("Slow query");
        wire.HoldQuery = "Slow";
        var search = vm.SearchCommand.ExecuteAsync(null);
        await wire.Held.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.CancelPendingSearch();
        await search;
        Assert.False(vm.IsLoading);
        Assert.False(vm.LibraryResultsKnown);

        wire.HoldQuery = null;
        vm.BeginSearchVisit();
        await vm.ResumeRetainedSearchAsync();
        Assert.True(vm.LibraryResultsKnown);
        Assert.Equal("Slow query result", Assert.Single(vm.Results).Title);
        await Until(() => vm.OutsideSettled);

        // An answered search is left alone.
        int CatalogReads() { lock (wire.Requests) return wire.Requests.Count(path => path.StartsWith("/api/v2/catalog?")); }
        var reads = CatalogReads();
        await vm.ResumeRetainedSearchAsync();
        Assert.Equal(reads, CatalogReads());
    }

    [Fact]
    public async Task AnAudiobookScopeHasNoRequestSearch()
    {
        using var wire = new Wire(); var vm = wire.Model("Fixture");
        await vm.SetMediaScopeAsync("audiobook");
        await Until(() => vm.OutsideSettled);
        Assert.False(vm.RequestsEnabled);
        Assert.False(vm.HasRequestResults);
        Assert.DoesNotContain(wire.Requests, path => path.StartsWith("/api/v2/requests/search"));
    }

    [Fact]
    public async Task ANewerSearchReplacesAnOlderOnesResults()
    {
        using var wire = new Wire(); var vm = wire.Model("Slow query");
        wire.HoldQuery = "Slow";
        var slow = vm.SearchCommand.ExecuteAsync(null);
        await wire.Held.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.Query = "Fast query";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal("Fast query result", Assert.Single(vm.Results).Title);
        wire.ReleaseHeld.TrySetResult();
        await slow;
        Assert.Equal("Fast query result", Assert.Single(vm.Results).Title);
        Assert.True(vm.LibraryResultsKnown);
    }

    [Fact]
    public async Task LibraryResultsAreKnownOnlyAfterTheLibraryAnswered()
    {
        using var wire = new Wire(); var vm = wire.Model("Fixture");
        Assert.False(vm.LibraryResultsKnown);
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.True(vm.LibraryResultsKnown);
        vm.Query = "Another";
        Assert.False(vm.LibraryResultsKnown);
    }

    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25);
        Assert.True(ready(), "search state did not settle");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int RequestTotalPages = 1, RequestResults = 4;
        public bool FailRequestSearch, FailCatalog, CatalogHasMore;
        public string? HoldQuery;
        public TaskCompletionSource Held = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Requests { get; } = [];
        public string LastCatalog => Requests.Last(path => path.StartsWith("/api/v2/catalog?"));

        public SearchViewModel Model(string query)
        {
            var api = new SiloApiClient(new HttpClient(this)); api.SetBaseUrl("https://search-parity.invalid");
            return new(new CatalogApi(api), new PeopleApi(api), new RequestsApi(api), new SettingsApi(api)) { Query = query };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.PathAndQuery; lock (Requests) Requests.Add(path);
            var route = request.RequestUri.AbsolutePath;
            var status = HttpStatusCode.OK; var body = "{}";
            if (route == "/api/v2/catalog")
            {
                var query = Uri.UnescapeDataString(request.RequestUri.Query);
                if (HoldQuery != null && query.Contains("q=" + HoldQuery)) { Held.TrySetResult(); await ReleaseHeld.Task.WaitAsync(ct); }
                var title = query.Contains("q=Fast query") ? "Fast query result" : query.Contains("q=Slow query") ? "Slow query result" : "Fixture film";
                if (FailCatalog) { status = HttpStatusCode.ServiceUnavailable; body = "{\"message\":\"down\"}"; }
                else body = "{\"items\":[{\"content_id\":\"" + title + "\",\"title\":\"" + title + "\",\"type\":\"movie\"}],\"total\":1,\"page\":{\"has_more\":" + (CatalogHasMore ? "true" : "false") + "}}";
            }
            else if (route == "/api/v2/catalog/people") body = "{\"items\":[]}";
            else if (route == "/api/v2/requests/status") body = "{\"requests_enabled\":true}";
            else if (route == "/api/v2/requests/search")
            {
                if (FailRequestSearch) { status = HttpStatusCode.BadGateway; body = "{\"message\":\"upstream\"}"; }
                else
                {
                    var results = Enumerable.Range(1, RequestResults).Select(id =>
                        "{\"tmdb_id\":" + id + ",\"media_type\":\"movie\",\"title\":\"Requested " + id + "\",\"availability\":\"" + (id == 4 ? "available" : "unavailable") + "\",\"request\":{\"requestable\":true}}");
                    body = "{\"page\":1,\"total_pages\":" + RequestTotalPages + ",\"total_results\":80,\"results\":[" + string.Join(",", results) + "]}";
                }
            }
            return new(status) { Content = new StringContent(body) };
        }
    }
}
