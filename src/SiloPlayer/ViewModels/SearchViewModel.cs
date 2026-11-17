using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly PeopleApi _peopleApi;
    private readonly RequestsApi _requestsApi;
    private readonly SettingsApi _settingsApi;
    private readonly ItemDetailPrefetchCache? _detailPrefetchCache;
    private PendingCatalogWrites _pendingProgressSaves = new();
    private ApiIdentityContext? _progressSaveIdentity;
    private long _cardMutationRevision;

    public void RecordCatalogMutation(Task? persisted = null)
    {
        _cardMutationRevision++;
        if (persisted != null)
        {
            var identity = _catalogApi.CaptureIdentityContext();
            if (_progressSaveIdentity != identity)
            {
                _pendingProgressSaves = new PendingCatalogWrites();
                _progressSaveIdentity = identity;
            }
            _pendingProgressSaves.Track(persisted);
        }
    }

    private long CatalogMutationRevision => _cardMutationRevision + (_detailPrefetchCache?.ProgressSaveRevision ?? 0);

    private async Task WaitForCatalogWritesAsync(CancellationToken ct)
    {
        if (_progressSaveIdentity == _catalogApi.CaptureIdentityContext())
            await _pendingProgressSaves.WaitAsync(ct);
        if (_detailPrefetchCache != null) await _detailPrefetchCache.WaitForProgressSettlementAsync(ct);
    }
    public RequestDiscoveryOwner? OutsideDiscoveryOwner { get; private set; }
    public bool OutsideDiscoveryAllowed => _requestsApi.DiscoveryAllowed;
    public Task RefreshRetainedOutsideAsync()
    {
        if (!OutsideDiscoveryAllowed) { RetireOutsideDiscovery(); return Task.CompletedTask; }
        if (OutsideDiscoveryOwner is { } owner && _requestsApi.IsCurrentDiscoveryOwner(owner)) return Task.CompletedTask;
        return SetOutsidePageAsync(OutsideRequestedPage);
    }
    public void RetireOutsideDiscovery()
    {
        if (OutsideDiscoveryAllowed) return;
        Interlocked.Exchange(ref _outsideCts, null)?.Cancel();
        OutsideDiscoveryOwner = null;
        OutsideLibraryResults.Clear(); OutsideTotalPages = OutsideTotalResults = 0;
        OutsideWatchlistTitlesSupported = false; IsOutsideLoading = false;
        SetLocalizedMessage(nameof(OutsideError), () => null, value => OutsideError = value);
    }
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _filtersCts;
    private string? _snapshot;
    private string? _lastAppliedSearchKey;
    private string? _loadedFiltersKey;
    private ApiRequestContext? _loadedFiltersContext;
    private int _loadedFiltersGeneration = -1;
    private bool _hasMore;
    private CancellationTokenSource? _outsideCts;
    private long _queryGeneration;
    [ObservableProperty] private int _outsidePage = 1;
    public int OutsideRequestedPage { get; private set; } = 1;
    [ObservableProperty] private int _outsideTotalPages;
    [ObservableProperty] private int _outsideTotalResults;
    [ObservableProperty] private bool _isOutsideLoading;
    [ObservableProperty] private string? _outsideError;
    [ObservableProperty] private bool _outsideWatchlistTitlesSupported;
    private CancellationTokenSource _externalActionsCts = new();
    private readonly HashSet<(string Kind, string Type, int Id)> _externalPending = [];
    public async Task RequestOutsideTitleAsync(RequestMediaResult item)
    {
        var key = ("request", item.MediaType, item.TmdbId);
        if (!_requestsApi.SubmissionAllowed || !OutsideLibraryResults.Contains(item) || !item.Request.Requestable || !_externalPending.Add(key)) return;
        var token = _externalActionsCts.Token; var generation = _queryGeneration; var context = _settingsApi.CaptureContext();
        try
        {
            var created = await _requestsApi.CreateAsync(new() { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title,
                Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, token);
            if (token.IsCancellationRequested || generation != _queryGeneration || !_settingsApi.IsCurrentContext(context)) return;
            item.Request = new() { Status = string.IsNullOrWhiteSpace(created.Status) ? "pending" : created.Status, Requestable = false, RequestId = created.Id };
        }
        finally { if (generation == _queryGeneration) _externalPending.Remove(key); }
    }
    public async Task ToggleOutsideWatchlistAsync(RequestMediaResult item)
    {
        var key = ("watchlist", item.MediaType, item.TmdbId);
        if (!_requestsApi.DiscoveryAllowed || !OutsideLibraryResults.Contains(item) || !OutsideWatchlistTitlesSupported || !_externalPending.Add(key)) return;
        var token = _externalActionsCts.Token; var generation = _queryGeneration; var context = _settingsApi.CaptureContext();
        var wasSaved = item.InWatchlist == true;
        try
        {
            if (wasSaved) await _requestsApi.RemoveWatchlistTitleAsync(item.MediaType, item.TmdbId, token);
            else await _requestsApi.AddWatchlistTitleAsync(item.MediaType, item.TmdbId, token);
            if (!token.IsCancellationRequested && generation == _queryGeneration && _settingsApi.IsCurrentContext(context)) item.InWatchlist = !wasSaved;
        }
        finally { if (generation == _queryGeneration) _externalPending.Remove(key); }
    }
    [ObservableProperty] private string? _peopleError;
    [ObservableProperty] private bool _isPeopleLoading;
    private CancellationTokenSource? _peopleRetryCts;
    public async Task RetryPeopleAsync()
    {
        if (IsPeopleLoading || string.IsNullOrWhiteSpace(Query)) return;
        var query = Query.Trim(); var generation = _queryGeneration;
        var key = BuildSearchKey(query); var owner = new CancellationTokenSource();
        Interlocked.Exchange(ref _peopleRetryCts, owner)?.Cancel();
        IsPeopleLoading = true; SetLocalizedMessage(nameof(PeopleError), () => null, value => PeopleError = value);
        try
        {
            var people = await SearchPeopleAsync(query, owner.Token);
            if (_peopleRetryCts == owner && !owner.IsCancellationRequested && generation == _queryGeneration && key == BuildSearchKey(Query.Trim())) ReplacePeopleResults(people);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception ex) { if (_peopleRetryCts == owner && generation == _queryGeneration && key == BuildSearchKey(Query.Trim())) SetLocalizedMessage(nameof(PeopleError), () => $"Could not load people: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => PeopleError = value); }
        finally { if (_peopleRetryCts == owner) { _peopleRetryCts = null; IsPeopleLoading = false; } owner.Dispose(); }
    }

    public SearchViewModel(CatalogApi catalogApi, PeopleApi peopleApi, RequestsApi requestsApi, SettingsApi settingsApi,
        ItemDetailPrefetchCache? detailPrefetchCache = null)
    {
        _catalogApi = catalogApi;
        _peopleApi = peopleApi;
        _requestsApi = requestsApi;
        _settingsApi = settingsApi;
        _detailPrefetchCache = detailPrefetchCache;
        BoxSets = new BoxSetSearchState(catalogApi);
        WeakReferenceMessenger.Default.Register<NotInterestedChange>(this, (_, change) =>
        {
            if (!_catalogApi.IsCurrentContext(change.Context) || !change.Marked.HasValue) return;
            RecordCatalogMutation();
            foreach (var item in Results.Where(item => item.ContentId == change.ItemId)) MediaItemStateUpdater.SetNotInterested(item, change.Marked.Value);
        });
    }

    public BulkObservableCollection<MediaItem> Results { get; } = [];
    public ObservableCollection<Person> PeopleResults { get; } = [];
    public ObservableCollection<RequestMediaResult> OutsideLibraryResults { get; } = [];
    public BoxSetSearchState BoxSets { get; }
    private bool _boxSetSearchActive;
    [ObservableProperty] private int? _libraryId;
    partial void OnQueryChanged(string value) => BoxSets.Retire();
    partial void OnLibraryIdChanged(int? value) => BoxSets.Retire();
    partial void OnMediaTypeChanged(string? value) => BoxSets.Retire();

    public void BeginSearchVisit() => _boxSetSearchActive = true;
    private string? BoxSetMediaScope => string.IsNullOrWhiteSpace(AdvancedQuery.MediaScope) ? MediaType : AdvancedQuery.MediaScope;
    public Task RefreshBoxSetSearchAsync(bool refresh = true)
    {
        if (!_boxSetSearchActive) return Task.CompletedTask;
        var query = Query.Trim(); var library = LibraryId; var type = BoxSetMediaScope;
        return BoxSets.SearchAsync(query, library, type is null or "video" or "movie",
            () => _boxSetSearchActive && Query.Trim() == query && LibraryId == library && BoxSetMediaScope == type, refresh);
    }

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

    [ObservableProperty] private string _sortField = "added_at";
    [ObservableProperty] private string _sortOrder = "desc";
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _contentRating;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _country;

    public CatalogFiltersResponse? AvailableFilters { get; private set; }
    public QueryDefinition AdvancedQuery { get; } = new();

    public async Task LoadFiltersAsync(string? query = null, string? mediaType = null)
    {
        var normalizedQuery = query?.Trim() ?? "";
        var normalizedType = mediaType?.Trim() ?? "";
        var filtersKey = normalizedType;
        var context = _catalogApi.CaptureContext();
        var generation = _catalogApi.FilterCacheGeneration;
        if (AvailableFilters != null && _loadedFiltersContext == context && _loadedFiltersGeneration == generation
            && string.Equals(_loadedFiltersKey, filtersKey, StringComparison.Ordinal))
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
            if (!ReferenceEquals(_filtersCts, owner) || owner.IsCancellationRequested
                || _catalogApi.CaptureContext() != context || _catalogApi.FilterCacheGeneration != generation)
                return;

            AvailableFilters = filters;
            _loadedFiltersKey = filtersKey;
            _loadedFiltersContext = context;
            _loadedFiltersGeneration = generation;
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
                _loadedFiltersKey = null;
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
        QueryEditing.SetMediaScope(AdvancedQuery, MediaType);
        _ = SaveMediaScopePreferenceAsync(MediaScope);
        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync();
    }

    public async Task SetMediaTypeAsync(string? type)
    {
        var normalized = type is "video" or "movie" or "series" or "episode" or "audiobook" or "ebook" or "manga"
            ? type
            : null;
        MediaType = normalized;
        QueryEditing.SetMediaScope(AdvancedQuery, normalized);
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
        _boxSetSearchActive = true;
        var querySnapshot = Query.Trim();
        var searchKey = BuildSearchKey(querySnapshot);
        // Preserve both the window cursor and the outstanding optional reads
        // when the same visible search is submitted again.
        if (string.Equals(_lastAppliedSearchKey, searchKey, StringComparison.Ordinal) &&
            (Results.Count > 0 || PeopleResults.Count > 0 || OutsideLibraryResults.Count > 0) &&
            ErrorMessage == null && !IsLoading && !IsLoadingMore)
        { _ = RefreshBoxSetSearchAsync(false); return; }
        CancelPendingSearch();
        _boxSetSearchActive = true;
        var searchCts = new CancellationTokenSource();
        _searchCts = searchCts;
        var ct = searchCts.Token;
        _snapshot = null;
        _hasMore = false;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            PeopleResults.Clear();
            OutsideLibraryResults.Clear();
            TotalCount = 0;
            _lastAppliedSearchKey = null;
            OutsidePage = 1; OutsideTotalPages = 0; OutsideTotalResults = 0;
            return;
        }

        CancelPendingFilters();
        IsLoading = true;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        var shouldResetVisibleResults = !string.Equals(_lastAppliedSearchKey, searchKey, StringComparison.Ordinal);
        if (shouldResetVisibleResults)
        {
            Results.Clear();
            PeopleResults.Clear();
            OutsideLibraryResults.Clear();
            OutsidePage = 1; OutsideTotalPages = 0; OutsideTotalResults = 0; SetLocalizedMessage(nameof(OutsideError), () => null, value => OutsideError = value);
            TotalCount = 0;
        }

        try
        {
            // Local catalog results own the primary search surface. Optional
            // request-provider discovery must never hold those results behind
            // a slow plugin or network timeout.
            var catalogTask = FetchCatalogPageAsync(0, null, ct, querySnapshot);
            var peopleTask = SearchPeopleAsync(querySnapshot, ct);
            _ = RefreshBoxSetSearchAsync(false);

            var primaryTimer = Stopwatch.StartNew();
            var response = await catalogTask;
            if (!IsCurrentSearchOwner(searchCts, querySnapshot)) return;
            LocalLog.AppendLine(
                "search_timing.txt",
                $"catalog_complete | elapsed_ms={primaryTimer.ElapsedMilliseconds} | query_length={querySnapshot.Length} | scope={MediaScope} | type={MediaType ?? "all"} | sort={SortField} | request_sort={GetRequestSortField() ?? "server_default"} | count={response.Items.Count} | has_more={response.HasMore}");

            _ = PublishPeopleAsync(peopleTask, querySnapshot, searchCts);

            // Use server results directly — server handles text search
            ReplaceMediaResults(response.Items);
            TotalCount = response.Total > 0 ? response.Total : response.Items.Count;
            _snapshot = response.Snapshot;
            _hasMore = response.HasMore || (response.Items.Count == 60 && (response.Total <= 0 || response.Items.Count < response.Total));
            _lastAppliedSearchKey = searchKey;

            // Match the WebUI's secondary discovery cadence without allowing
            // provider/plugin work to compete with the local catalog request.
            // Local results are already visible before this task begins.
            if (RequestSearchType() != null)
            {
                _ = SetOutsidePageAsync(1);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on new search
        }
        catch (Exception ex)
        {
            if (!IsCurrentSearchOwner(searchCts, querySnapshot)) return;
            SetLocalizedMessage(nameof(ErrorMessage), () => $"Search failed: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
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

    /// <summary>Rereads the mounted query window without resetting scroll or optional discovery.</summary>
    public async Task RefreshCatalogMetadataAsync()
    {
        // Invalidate same-query reuse even when the retained read fails.
        _lastAppliedSearchKey = null;
        RecordCatalogMutation();
        if (!_boxSetSearchActive || string.IsNullOrWhiteSpace(Query)) return;
        var query = Query.Trim();
        var key = BuildSearchKey(query);
        var context = _catalogApi.CaptureContext();
        var target = Math.Max(60, Results.Count);
        var owner = new CancellationTokenSource();
        var previous = _searchCts;
        _searchCts = owner;
        previous?.Cancel(); previous?.Dispose();
        var ct = owner.Token;
        bool Current() => IsCurrentSearchOwner(owner, query) && _boxSetSearchActive
            && _catalogApi.IsCurrentContext(context) && key == BuildSearchKey(Query.Trim());
        IsLoading = true;
        IsLoadingMore = false;
        _hasMore = false; // A failed reread must not append using the retired snapshot.
        _snapshot = null;
        try
        {
            async Task<CatalogResponse> ReadWindowAsync(int offset, int limit, string? snapshot)
            {
                if (!Current()) throw new OperationCanceledException();
                var response = await FetchCatalogPageAsync(offset, snapshot, ct, query);
                if (!Current()) throw new OperationCanceledException();
                return response;
            }
            var response = await RetainedCatalogRead.ReadAsync(ReadWindowAsync, target, 60,
                () => CatalogMutationRevision, ct, WaitForCatalogWritesAsync);
            if (!Current()) return;
            MediaItemCollectionReconciler.Apply(Results, response.Items);
            TotalCount = response.Total > 0 ? response.Total : Results.Count;
            _snapshot = response.Snapshot;
            _hasMore = response.HasMore || (response.Items.Count > 0 && Results.Count < response.Total);
            _lastAppliedSearchKey = key;
            SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (Current()) SetLocalizedMessage(nameof(ErrorMessage), () => $"Could not refresh search results: {(ex is ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
        }
        finally { if (ReferenceEquals(_searchCts, owner)) IsLoading = false; }
    }

    public void CancelPendingSearch()
    {
        _boxSetSearchActive = false;
        BoxSets.Suspend();
        var previousActions = _externalActionsCts; _externalActionsCts = new(); previousActions.Cancel(); previousActions.Dispose(); _externalPending.Clear();
        OutsideWatchlistTitlesSupported = false;
        Interlocked.Exchange(ref _peopleRetryCts, null)?.Cancel();
        IsPeopleLoading = false; SetLocalizedMessage(nameof(PeopleError), () => null, value => PeopleError = value);
        _queryGeneration++;
        Interlocked.Exchange(ref _outsideCts, null)?.Cancel();
        IsOutsideLoading = false;
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
        var generation = _queryGeneration;
        try
        {
            var response = await FetchCatalogPageAsync(Results.Count, _snapshot, ct);
            if (ct.IsCancellationRequested || generation != _queryGeneration || !IsCurrentSearchQuery(querySnapshot)) return;
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
            if (generation != _queryGeneration || ct.IsCancellationRequested) return;
            SetLocalizedMessage(nameof(ErrorMessage), () => $"Could not load more search results: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
        }
        finally
        {
            if (generation == _queryGeneration) IsLoadingMore = false;
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
            LibraryId?.ToString() ?? "",
            _catalogApi.CaptureContext().ToString(),
            SortField,
            SortOrder,
            Genre ?? "",
            ContentRating ?? "",
            Resolution ?? "",
            Country ?? "",
            System.Text.Json.JsonSerializer.Serialize(AdvancedQuery));

    private void ReplaceMediaResults(IReadOnlyList<MediaItem> items)
    {
        if (Results.Count == items.Count &&
            Results.Select(item => item.ContentId).SequenceEqual(items.Select(item => item.ContentId)))
        {
            MediaItemCollectionReconciler.Apply(Results, items);
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
        var requestSort = GetRequestSortField();
        return _catalogApi.GetCatalogAsync(
            LibraryId,
            // Current WebUI displays Date Added as the default query sort but
            // intentionally omits it from an unscoped query request. The
            // server then applies its optimized default query ordering.
            sort: requestSort,
            order: requestSort == null ? null : SortOrder,
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
            queryGroups: AdvancedQuery.Groups.Count > 0 ? QueryEditing.PopulatedGroups(AdvancedQuery) : null,
            queryGroupsMatch: AdvancedQuery.Match,
            ct: ct);
    }

    private string? GetRequestSortField()
        => string.Equals(SortField, "added_at", StringComparison.Ordinal)
            ? null
            : SortField;

    private string? RequestSearchType() => MediaType switch
    { "movie" => "movie", "series" or "episode" => "series", null or "video" => MediaScope is "all" or "video" ? "all" : null, _ => null };

    private async Task PublishPeopleAsync(Task<List<Person>> task, string query, CancellationTokenSource owner)
    {
        try { var people = await task; if (IsCurrentSearchOwner(owner, query)) { ReplacePeopleResults(people); SetLocalizedMessage(nameof(PeopleError), () => null, value => PeopleError = value); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (IsCurrentSearchOwner(owner, query)) SetLocalizedMessage(nameof(PeopleError), () => $"Could not load people: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => PeopleError = value); }
    }

    public async Task SetOutsidePageAsync(int page)
    {
        var type = RequestSearchType();
        if (type == null || string.IsNullOrWhiteSpace(Query)) return;
        var owner = new CancellationTokenSource();
        Interlocked.Exchange(ref _outsideCts, owner)?.Cancel();
        var query = Query.Trim(); var key = BuildSearchKey(query); var generation = _queryGeneration;
        var discoveryOwner = _requestsApi.CaptureDiscoveryOwner();
        var selectedPage = Math.Clamp(page, 1, OutsideTotalPages > 0 ? OutsideTotalPages : 500);
        OutsideRequestedPage = selectedPage;
        IsOutsideLoading = true; SetLocalizedMessage(nameof(OutsideError), () => null, value => OutsideError = value);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var status = await _requestsApi.GetStatusAsync(timeout.Token);
            if (_requestsApi.CaptureDiscoveryOwner() != discoveryOwner) return;
            var response = status.RequestsEnabled && _requestsApi.DiscoveryAllowed ? await _requestsApi.SearchAsync(type, query, selectedPage, timeout.Token) : new RequestMediaPage();
            if (_outsideCts != owner || owner.IsCancellationRequested || generation != _queryGeneration || key != BuildSearchKey(Query.Trim())) return;
            if (_requestsApi.CaptureDiscoveryOwner() != discoveryOwner) return;
            OutsideDiscoveryOwner = discoveryOwner;
            OutsideWatchlistTitlesSupported = _requestsApi.DiscoveryAllowed && status.WatchlistTitlesSupported;
            var outside = response.Results
                .Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .ToList();
            OutsidePage = selectedPage; OutsideTotalPages = Math.Clamp(response.TotalPages, 0, 500); OutsideTotalResults = response.TotalResults;
            OutsideLibraryResults.Clear();
            foreach (var item in outside)
                OutsideLibraryResults.Add(item);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_outsideCts == owner && generation == _queryGeneration) SetLocalizedMessage(nameof(OutsideError), () => $"Couldn't load page {selectedPage} of titles outside your library. Try this page again.", value => OutsideError = value);
        }
        finally { if (_outsideCts == owner) { _outsideCts = null; IsOutsideLoading = false; } owner.Dispose(); }
    }

    private Task<List<Person>> SearchPeopleAsync(string query, CancellationToken ct)
        => _peopleApi.SearchScopedAsync(query, MediaScope == "all" ? null : MediaType ?? MediaScope, 20, ct);
}
