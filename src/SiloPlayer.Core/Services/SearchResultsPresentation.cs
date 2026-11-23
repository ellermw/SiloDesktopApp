using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

/// <summary>
/// What a search results page shows: the overview (a few rows of each result
/// kind, so requestable titles stay near the top whatever the library returns),
/// or the full list of one kind. Official: web/src/pages/searchResultsView.ts.
/// </summary>
public enum SearchResultsView { Overview, Library, Requests }

/// <summary>The overview's rows: two rows of best matches, then up to three rows of more.</summary>
public sealed record SearchOverviewSplit<T>(IReadOnlyList<T> Best, IReadOnlyList<T> More, bool HasMoreThanShown);

/// <summary>The request grid's Previous / page / Next footer.</summary>
public sealed record RequestPagerState(bool HasPrevious, bool HasNext, string PositionKey, int Page, int TotalPages);

/// <summary>
/// The rules of the official search results page (silo-server 3b2b32168, #4450)
/// that do not need a control: view choice, overview rows, counts, sort wording
/// and the request paging footer.
/// </summary>
public static class SearchResultsPresentation
{
    public const int BestMatchRows = 2;
    public const int MoreResultRows = 3;
    /// <summary>Titles one request-search page shows (REQUEST_SHELF_LIMIT / GRID_LIMIT).</summary>
    public const int RequestPageLimit = 20;
    /// <summary>TMDB serves at most 500 pages of any list, whatever total it reports.</summary>
    public const int TmdbMaxPage = 500;

    /// <summary>The sort a text search starts on, and the one "Back to relevance" returns to.</summary>
    public const string RelevanceSort = "relevance";

    public static SearchOverviewSplit<T> SplitOverview<T>(IReadOnlyList<T> items, int columns, bool serverHasMore)
    {
        var perRow = Math.Max(1, columns);
        var bestEnd = perRow * BestMatchRows;
        var moreEnd = bestEnd + perRow * MoreResultRows;
        return new(
            items.Take(bestEnd).ToList(),
            items.Skip(bestEnd).Take(moreEnd - bestEnd).ToList(),
            serverHasMore || items.Count > moreEnd);
    }

    /// <summary>The full request list needs discovery for this scope; otherwise the overview shows.</summary>
    public static SearchResultsView ShownView(SearchResultsView requested, bool requestsDeciding, bool requestsEnabled, bool hasRequestResults)
        => requested == SearchResultsView.Requests && !requestsDeciding && !(requestsEnabled && hasRequestResults)
            ? SearchResultsView.Overview
            : requested;

    /// <summary>Whether the Request to add view is offered at all.</summary>
    public static bool OffersRequestView(bool requestsEnabled, bool hasRequestResults) => requestsEnabled && hasRequestResults;

    /// <summary>A later page can hold titles the first page's library matches hid.</summary>
    public static bool HasRequestResults(int requestableOnFirstPage, int totalPages) => requestableOnFirstPage > 0 || totalPages > 1;

    /// <summary>
    /// "Best matches" only means something in relevance order. Relevance is the sort of a search until the
    /// viewer picks another, and picking it again is still Relevance.
    /// </summary>
    public static bool SortsByRelevance(string? sortField) => string.IsNullOrEmpty(sortField) || sortField == RelevanceSort;

    /// <summary>
    /// The sort a search request carries. A search ranks by relevance on its own, so the default sends none; picking
    /// Relevance sends it, and any other field the viewer picked is sent, Date Added included.
    /// </summary>
    public static (string? Sort, string? Order) RequestSort(string? sortField, bool relevanceChosen, string? order)
    {
        if (SortsByRelevance(sortField)) return relevanceChosen ? (RelevanceSort, "desc") : (null, null);
        return (sortField, order);
    }

    /// <summary>Meilisearch reports no exact total, so past the loaded titles a count is "60+".</summary>
    public static string CountLabel(int count, bool more)
    {
        var number = count.ToString("N0", AppStrings.FormatCulture);
        return more
            ? AppStrings.Get("web.search.results.atLeastCount", new Dictionary<string, object?> { ["count"] = number }, fallback: number + "+")
            : number;
    }

    public static string SortedByText(string sortName)
        => AppStrings.Get("web.search.results.sortedBy", new Dictionary<string, object?> { ["sort"] = sortName }, fallback: "Sorted by " + sortName);

    /// <summary>Titles worth requesting: what TMDB found, minus what the library already has.</summary>
    public static List<RequestMediaResult> RequestSuggestions(IEnumerable<RequestMediaResult>? results, int limit)
        => (results ?? [])
            .Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

    /// <summary>The number of pages a TMDB list can actually be read to.</summary>
    public static int TmdbPageCount(int? totalPages) => Math.Min(Math.Max(totalPages ?? 0, 0), TmdbMaxPage);

    /// <summary>Previous / Next and the position text of the request grid; nothing shows when there is nowhere to go.</summary>
    public static RequestPagerState RequestPager(int page, int totalPages)
        => new(page > 1, page < totalPages, totalPages > 0 ? "web.requests.toAdd.pageOf" : "web.requests.toAdd.page", page, totalPages);

    public static string RequestPagerPosition(RequestPagerState state)
        => state.TotalPages > 0
            ? AppStrings.Get(state.PositionKey, new Dictionary<string, object?> { ["page"] = state.Page, ["total"] = state.TotalPages }, fallback: $"Page {state.Page} of {state.TotalPages}")
            : AppStrings.Get(state.PositionKey, new Dictionary<string, object?> { ["page"] = state.Page }, fallback: $"Page {state.Page}");

    /// <summary>
    /// Nothing in the library and, once discovery has answered, nothing to request, no box set and no person either.
    /// </summary>
    public static bool NothingFound(bool libraryEmpty, bool requestsSettled, bool hasRequestResults, int boxSets, int people, bool peopleLoading, bool peopleFailed)
        => libraryEmpty && requestsSettled && !hasRequestResults && boxSets == 0 && people == 0 && !peopleLoading && !peopleFailed;
}
