using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly PeopleApi _peopleApi;
    private readonly RequestsApi _requestsApi;
    private readonly SettingsApi _settingsApi;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _filtersCts;
    private string? _snapshot;
    private string? _lastAppliedSearchKey;
    private string? _loadedFiltersKey;
    private bool _hasMore;

    public SearchViewModel(CatalogApi catalogApi, PeopleApi peopleApi, RequestsApi requestsApi, SettingsApi settingsApi)
    {
        _catalogApi = catalogApi;
        _peopleApi = peopleApi;
        _requestsApi = requestsApi;
        _settingsApi = settingsApi;
    }

    public BulkObservableCollection<MediaItem> Results { get; } = [];
    public ObservableCollection<Person> PeopleResults { get; } = [];
    public ObservableCollection<RequestMediaResult> OutsideLibraryResults { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _mediaScope = "video";

    [ObservableProperty]
    private string? _mediaType;

    [ObservableProperty] private string _sortField = "relevance";
    [ObservableProperty] private string _sortOrder = "desc";
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _contentRating;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _country;

    public CatalogFiltersResponse? AvailableFilters { get; private set; }

    public async Task LoadFiltersAsync(string? query = null, string? mediaType = null)
    {
        var normalizedQuery = query?.Trim() ?? "";
        var normalizedType = mediaType?.Trim() ?? "";
        var filtersKey = $"{normalizedQuery}\u001F{normalizedType}";
        if (AvailableFilters != null && string.Equals(_loadedFiltersKey, filtersKey, StringComparison.Ordinal))
            return;

        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _filtersCts, owner);
        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        var timer = Stopwatch.StartNew();
        try
        {
            var filters = await _catalogApi.GetFiltersAsync(
                source: "query",
                q: normalizedQuery,
                type: normalizedType,
                ct: owner.Token);
            if (!ReferenceEquals(_filtersCts, owner) || owner.IsCancellationRequested)
                return;

            AvailableFilters = filters;
            _loadedFiltersKey = filtersKey;
            OnPropertyChanged(nameof(AvailableFilters));
            LocalLog.AppendLine(
                "search_timing.txt",
                $"filters_complete | elapsed_ms={timer.ElapsedMilliseconds} | query_length={normalizedQuery.Length} | type={normalizedType}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_filtersCts, owner))
            {
                AvailableFilters = new CatalogFiltersResponse();
                _loadedFiltersKey = filtersKey;
                OnPropertyChanged(nameof(AvailableFilters));
            }
            LocalLog.AppendLine(
                "search_timing.txt",
                $"filters_failed | elapsed_ms={timer.ElapsedMilliseconds} | type={ex.GetType().Name} | message={ex.Message}");
        }
        finally
        {
            Interlocked.CompareExchange(ref _filtersCts, null, owner);
            owner.Dispose();
        }
    }

    public async Task LoadMediaScopeAsync()
    {
        try
        {
            var value = (await _settingsApi.GetSettingAsync("search.media_scope")).Value;
            MediaScope = value is "all" or "video" or "audiobook" ? value : "video";
            MediaType = MediaScope == "all" ? null : MediaScope;
        }
        catch { MediaScope = "video"; MediaType = "video"; }
    }

    public async Task SetMediaScopeAsync(string scope)
    {
        MediaScope = scope is "all" or "video" or "audiobook" ? scope : "video";
        MediaType = MediaScope == "all" ? null : MediaScope;
        _ = SaveMediaScopePreferenceAsync(MediaScope);
        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync();
    }

    public async Task SetMediaTypeAsync(string? type)
    {
        var normalized = type is "video" or "movie" or "series" or "episode" or "audiobook" or "ebook" or "manga"
            ? type
            : null;
        MediaType = normalized;
        MediaScope = type switch
        {
            "video" => "video",
            "all" => "all",
            "movie" or "series" or "episode" => "video",
            "audiobook" => "audiobook",
            "ebook" or "manga" => "all",
            _ => "all"
        };
        _ = SaveMediaScopePreferenceAsync(MediaScope);
        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync();
    }

    private async Task SaveMediaScopePreferenceAsync(string mediaScope)
    {
        try { await _settingsApi.PutSettingAsync("search.media_scope", mediaScope); } catch { }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        CancelPendingSearch();
        var searchCts = new CancellationTokenSource();
        _searchCts = searchCts;
        var ct = searchCts.Token;
        var querySnapshot = Query.Trim();
        var searchKey = BuildSearchKey(querySnapshot);
        _snapshot = null;
        _hasMore = false;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            PeopleResults.Clear();
            OutsideLibraryResults.Clear();
            TotalCount = 0;
            _lastAppliedSearchKey = null;
            return;
        }

        // Exact duplicate searches can arrive after the empty/results search
        // box handoff. If the visible grid already belongs to the same
        // query/filter/sort key, ignore the duplicate instead of clearing and
        // rebuilding the same cards seconds later.
        if (string.Equals(_lastAppliedSearchKey, searchKey, StringComparison.Ordinal) &&
            Results.Count > 0 &&
            PeopleResults.Count == 0 &&
            ErrorMessage == null &&
            !IsLoading &&
            !IsLoadingMore)
        {
            return;
        }

        CancelPendingFilters();
        IsLoading = true;
        ErrorMessage = null;
        var shouldResetVisibleResults = !string.Equals(_lastAppliedSearchKey, searchKey, StringComparison.Ordinal);
        if (shouldResetVisibleResults)
        {
            Results.Clear();
            PeopleResults.Clear();
            OutsideLibraryResults.Clear();
            TotalCount = 0;
        }

        try
        {
            // Local catalog results own the primary search surface. Optional
            // request-provider discovery must never hold those results behind
            // a slow plugin or network timeout.
            var catalogTask = FetchCatalogPageAsync(0, null, ct, querySnapshot);
            // The dedicated Catalog page does not render people; global search
            // owns that surface. Avoid an invisible extra network request.
            var peopleTask = Task.FromResult(new List<Person>());

            var primaryTimer = Stopwatch.StartNew();
            var response = await catalogTask;
            if (!IsCurrentSearchOwner(searchCts, querySnapshot)) return;
            LocalLog.AppendLine(
                "search_timing.txt",
                $"catalog_complete | elapsed_ms={primaryTimer.ElapsedMilliseconds} | query_length={querySnapshot.Length} | scope={MediaScope} | type={MediaType ?? "all"} | sort={SortField} | count={response.Items.Count} | has_more={response.HasMore}");

            // Update people results
            var people = await peopleTask;
            if (!IsCurrentSearchOwner(searchCts, querySnapshot)) return;
            ReplacePeopleResults(people);

            // Use server results directly — server handles text search
            ReplaceMediaResults(response.Items);
            TotalCount = response.Total > 0 ? response.Total : response.Items.Count;
            _snapshot = response.Snapshot;
            _hasMore = response.HasMore || (response.Items.Count == 60 && (response.Total <= 0 || response.Items.Count < response.Total));
            _lastAppliedSearchKey = searchKey;

            // Match the WebUI's secondary discovery cadence without allowing
            // provider/plugin work to compete with the local catalog request.
            // Local results are already visible before this task begins.
            if (MediaScope is "all" or "video")
            {
                _ = PublishOutsideLibraryResultsAsync(
                    SearchOutsideLibraryAsync(querySnapshot, ct),
                    querySnapshot,
                    searchCts,
                    ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on new search
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            LocalLog.AppendLine(
                "search_timing.txt",
                $"catalog_failed | query_length={querySnapshot.Length} | type={ex.GetType().Name} | message={ex.Message}");
        }
        finally
        {
            if (IsCurrentSearchOwner(searchCts, querySnapshot))
                IsLoading = false;
        }
    }

    public void CancelPendingSearch()
    {
        var cts = _searchCts;
        _searchCts = null;
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        IsLoading = false;
        IsLoadingMore = false;
        CancelPendingFilters();
    }

    private void CancelPendingFilters()
    {
        var cts = Interlocked.Exchange(ref _filtersCts, null);
        if (cts == null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    public async Task LoadMoreAsync()
    {
        if (IsLoading || IsLoadingMore || !_hasMore || string.IsNullOrWhiteSpace(Query)) return;
        IsLoadingMore = true;
        var ct = _searchCts?.Token ?? CancellationToken.None;
        var querySnapshot = Query.Trim();
        try
        {
            var response = await FetchCatalogPageAsync(Results.Count, _snapshot, ct);
            if (ct.IsCancellationRequested || !IsCurrentSearchQuery(querySnapshot)) return;
            var existingIds = Results.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            Results.AddRange(response.Items.Where(item => existingIds.Add(item.ContentId)));
            if (response.Total > 0) TotalCount = response.Total;
            _snapshot ??= response.Snapshot;
            _hasMore = response.HasMore || (response.Items.Count > 0 && (response.Total <= 0 || Results.Count < response.Total));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load more search results: {ex.Message}";
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private bool IsCurrentSearchQuery(string querySnapshot)
        => string.Equals(Query.Trim(), querySnapshot, StringComparison.Ordinal);

    private bool IsCurrentSearchOwner(CancellationTokenSource owner, string querySnapshot)
        => !owner.IsCancellationRequested &&
           ReferenceEquals(_searchCts, owner) &&
           IsCurrentSearchQuery(querySnapshot);

    private string BuildSearchKey(string query)
        => string.Join(
            "\u001F",
            query,
            MediaScope,
            MediaType ?? "",
            SortField,
            SortOrder,
            Genre ?? "",
            ContentRating ?? "",
            Resolution ?? "",
            Country ?? "");

    private void ReplaceMediaResults(IReadOnlyList<MediaItem> items)
    {
        if (Results.Count == items.Count &&
            Results.Select(item => item.ContentId).SequenceEqual(items.Select(item => item.ContentId)))
        {
            return;
        }

        Results.Clear();
        Results.AddRange(items);
    }

    private void ReplacePeopleResults(IReadOnlyList<Person> people)
    {
        if (PeopleResults.Count == people.Count &&
            PeopleResults.Select(person => person.Id).SequenceEqual(people.Select(person => person.Id)))
        {
            return;
        }

        PeopleResults.Clear();
        foreach (var person in people)
            PeopleResults.Add(person);
    }

    private Task<CatalogResponse> FetchCatalogPageAsync(
        int offset,
        string? snapshot,
        CancellationToken ct,
        string? query = null)
    {
        return _catalogApi.GetCatalogAsync(
            null,
            sort: SortField,
            order: SortOrder,
            genre: Genre,
            contentRating: ContentRating,
            resolution: Resolution,
            country: Country,
            q: query ?? Query,
            type: MediaType,
            limit: 60,
            offset: offset,
            includeTotal: false,
            snapshot: snapshot,
            source: "query",
            ct: ct);
    }

    private async Task<List<RequestMediaResult>> SearchOutsideLibraryAsync(string query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var status = await _requestsApi.GetStatusAsync(timeout.Token);
            if (!status.RequestsEnabled) return [];
            var response = await _requestsApi.SearchAsync("all", query, 1, timeout.Token);
            return response.Results.Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch { return []; }
    }

    private async Task PublishOutsideLibraryResultsAsync(
        Task<List<RequestMediaResult>> outsideTask,
        string querySnapshot,
        CancellationTokenSource owner,
        CancellationToken ct)
    {
        try
        {
            var outside = await outsideTask;
            if (ct.IsCancellationRequested ||
                !ReferenceEquals(_searchCts, owner) ||
                !string.Equals(Query.Trim(), querySnapshot, StringComparison.Ordinal))
            {
                return;
            }

            if (OutsideLibraryResults.Select(StableRequestResultKey).SequenceEqual(outside.Select(StableRequestResultKey)))
                return;

            if (outside.Count == 0 && OutsideLibraryResults.Count == 0)
                return;

            OutsideLibraryResults.Clear();
            foreach (var item in outside)
                OutsideLibraryResults.Add(item);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Discovery is optional and must not fail primary catalog search.
        }
    }

    private static string StableRequestResultKey(RequestMediaResult item)
        => $"{item.MediaType}:{item.TmdbId}:{item.Title}:{item.YearText}";

    private async Task<List<Person>> SearchPeopleAsync(string query, CancellationToken ct)
    {
        try
        {
            return await _peopleApi.GetPeopleAsync(query, 10, 0, ct);
        }
        catch
        {
            // People search failure is non-fatal
            return [];
        }
    }
}
