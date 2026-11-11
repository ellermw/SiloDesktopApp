using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class RequestsViewModel : ObservableObject
{
    private readonly RequestsApi _requestsApi;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private ApiRequestContext? _loadedContext;
    private ApiRequestContext? _loadingContext;
    private Func<bool> _loadingVisit = () => true;
    private int _loadRevision;
    private int _discoveryRevision;
    private ApiRequestContext? _configuredDiscoveryContext;
    private long _brandReadRevision;
    private bool _downloadRefreshInProgress;
    private int _mineReadRevision;
    private CancellationTokenSource? _searchCts;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public RequestsViewModel(RequestsApi requestsApi)
    {
        _requestsApi = requestsApi;
    }

    public ObservableCollection<RequestDiscoverySection> DiscoverySections { get; } = [];
    public ObservableCollection<RequestDiscoverRow> DiscoveryRows { get; } = [];
    public ObservableCollection<RequestMediaResult> SearchResults { get; } = [];
    public ObservableCollection<MediaRequest> MyRequests { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Studios { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Networks { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Genres { get; } = [];
    public RequestFeatureStatus Features { get; private set; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> BrandErrors { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _brandPending = new();
    public async Task RetryBrandAsync(string kind) { await LoadBrandAsync(kind); DataVersion++; }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMine;
    [ObservableProperty] private bool _isLoadingDiscovery;
    public bool IsBrandLoading(string kind) => _brandPending.ContainsKey(kind);
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _requestsEnabled = true;
    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedMediaType = "all";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _discoveryError;
    [ObservableProperty] private string? _mineError;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private int _searchPage = 1;
    [ObservableProperty] private int _searchTotalPages;
    [ObservableProperty] private int _searchTotalResults;
    [ObservableProperty] private int _dataVersion;

    [RelayCommand]
    private Task LoadAsync() => LoadForVisitAsync(() => true);

    private async Task LoadForVisitAsync(Func<bool> isCurrentVisit)
    {
        var context = _requestsApi.CaptureContext();
        // A load owned by a visit that has since ended will discard its result, so it must not swallow this one.
        if (_loadInProgress && _loadingContext == context && _loadingVisit()) return;
        if (_loadedContext == context && DataVersion > 0 && DateTime.UtcNow - _lastLoadedAt < CacheDuration) return;
        if (_loadedContext != context)
        {
            DiscoverySections.Clear(); DiscoveryRows.Clear(); Studios.Clear(); Networks.Clear(); Genres.Clear();
            MyRequests.Clear(); SearchResults.Clear(); BrandErrors.Clear(); _brandPending.Clear();
        }
        var revision = ++_loadRevision;
        _loadingContext = context;
        _loadingVisit = isCurrentVisit;

        _loadInProgress = true;
        IsLoading = DataVersion == 0 || _loadedContext != context;
        ErrorMessage = null;
        StatusMessage = "Loading requests...";

        try
        {
            var status = await _requestsApi.GetStatusAsync();
            if (!isCurrentVisit() || revision != _loadRevision || !_requestsApi.IsCurrentContext(context)) return;
            Features = status;
            RequestsEnabled = status.RequestsEnabled;
            if (!RequestsEnabled)
            {
                DiscoverySections.Clear();
                DiscoveryRows.Clear(); Studios.Clear(); Networks.Clear(); Genres.Clear();
                MyRequests.Clear();
                StatusMessage = "Requests are disabled on this server.";
                _lastLoadedAt = DateTime.UtcNow;
                _loadedContext = context;
                DataVersion++;
                return;
            }

            IsLoadingMine = IsLoadingDiscovery = true;
            if (!status.DiscoverRowsSupported)
                Replace(DiscoveryRows, RequestDiscoveryLayout.VisibleRows(new()));
            var mineTask = LoadMineAsync();
            var discoveryTask = LoadDiscoveryAsync(brandsPreloaded: !status.DiscoverRowsSupported);
            var brandsTask = status.DiscoverRowsSupported ? Task.CompletedTask : LoadBrandsAsync();
            // Each WebUI query owns its loading state. A slow discovery rail
            // must not hide completed account requests or brand results.
            IsLoading = false;
            await Task.WhenAll(mineTask, discoveryTask, brandsTask);
            if (!isCurrentVisit() || revision != _loadRevision || !_requestsApi.IsCurrentContext(context)) return;

            StatusMessage = MyRequests.Count > 0 ? $"{MyRequests.Count:N0} request(s) loaded." : "";
            _lastLoadedAt = DateTime.UtcNow;
            _loadedContext = context;
            DataVersion++;
        }
        catch (Exception ex)
        {
            if (!isCurrentVisit() || revision != _loadRevision || !_requestsApi.IsCurrentContext(context)) return;
            ErrorMessage = $"Failed to load requests: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            if (revision == _loadRevision) { _loadInProgress = false; IsLoading = false; }
        }
    }

    private async Task LoadMineAsync()
    {
        var context = _requestsApi.CaptureContext();
        IsLoadingMine = true;
        var revision = ++_mineReadRevision;
        MineError = null;
        try
        {
            var response = await _requestsApi.GetMineAsync(limit: 100);
            if (revision != _mineReadRevision || !_requestsApi.IsCurrentContext(context)) return;
            Replace(MyRequests, response.Requests);
        }
        catch
        {
            if (revision != _mineReadRevision || !_requestsApi.IsCurrentContext(context)) return;
            MineError = "Couldn't load your requests";
            MyRequests.Clear();
        }
        finally { if (revision == _mineReadRevision && _requestsApi.IsCurrentContext(context)) { IsLoadingMine = false; DataVersion++; } }
    }

    public bool HasDownloadingRequests => MyRequests.Any(request =>
        request.Download != null || request.Targets?.Any(target => target.Download != null) == true);

    // The mounted Requests surface follows the WebUI's 30-second download
    // refresh. It never enters the initial loading state or replaces retained
    // rows when a background read fails, overlaps, or loses navigation ownership.
    public async Task RefreshDownloadsAsync(CancellationToken ct)
    {
        if (IsLoadingMine || _downloadRefreshInProgress || !RequestsEnabled || !HasDownloadingRequests) return;
        _downloadRefreshInProgress = true;
        var revision = ++_mineReadRevision;
        try
        {
            var response = await _requestsApi.GetMineAsync(limit: 100, ct: ct);
            ct.ThrowIfCancellationRequested();
            if (revision != _mineReadRevision) return;
            if (System.Text.Json.JsonSerializer.Serialize(MyRequests) ==
                System.Text.Json.JsonSerializer.Serialize(response.Requests)) return;
            Replace(MyRequests, response.Requests);
            DataVersion++;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch { /* Keep the last successful list; the next mounted interval retries. */ }
        finally { _downloadRefreshInProgress = false; }
    }

    private async Task LoadDiscoveryAsync(bool brandsPreloaded = false)
    {
        var context = _requestsApi.CaptureContext();
        var revision = ++_discoveryRevision;
        IsLoadingDiscovery = true;
        DiscoveryError = null;
        try
        {
            var response = await _requestsApi.GetDiscoveryAsync();
            if (revision != _discoveryRevision || !_requestsApi.IsCurrentContext(context)) return;
            if (response.RowOrder != null) _configuredDiscoveryContext = context;
            var rows = RequestDiscoveryLayout.VisibleRows(response);
            Replace(DiscoveryRows, rows);
            var keys = rows.Where(row => RequestDiscoveryLayout.BrandKind(row.Type) == null).Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
            Replace(DiscoverySections, response.Sections.Where(section => keys.Contains(section.Key)));
            IsLoadingDiscovery = false; DataVersion++;
            if (!brandsPreloaded || response.RowOrder != null)
            {
                Studios.Clear(); Networks.Clear(); Genres.Clear(); BrandErrors.Clear();
                await LoadBrandsAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (revision != _discoveryRevision || !_requestsApi.IsCurrentContext(context)) return;
            DiscoveryError = "Discovery is offline";
            DiscoverySections.Clear();
            // A legacy title query is independent of its three fixed brand
            // queries. A configured layout has no safe default to invent.
            Replace(DiscoveryRows, Features.DiscoverRowsSupported || _configuredDiscoveryContext == context
                ? [] : RequestDiscoveryLayout.VisibleRows(new()));
        }
        finally { if (revision == _discoveryRevision && _requestsApi.IsCurrentContext(context)) { IsLoadingDiscovery = false; DataVersion++; } }
    }

    private async Task LoadBrandsAsync()
    {
        await Task.WhenAll(DiscoveryRows.Select(row => RequestDiscoveryLayout.BrandKind(row.Type))
            .OfType<string>().Distinct().Select(LoadBrandAsync));
    }

    private async Task LoadBrandAsync(string kind)
    {
        if (!DiscoveryRows.Any(row => RequestDiscoveryLayout.BrandKind(row.Type) == kind)) return;
        var context = _requestsApi.CaptureContext();
        var target = kind switch { "studio" => Studios, "network" => Networks, "genre" => Genres, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        var revision = Interlocked.Increment(ref _brandReadRevision);
        if (!_brandPending.TryAdd(kind, revision)) return;
        BrandErrors.TryRemove(kind, out _);
        try
        {
            var cards = kind switch
            {
                "studio" => (await _requestsApi.GetDiscoverStudiosAsync()).Studios,
                "network" => (await _requestsApi.GetDiscoverNetworksAsync()).Networks,
                _ => (await _requestsApi.GetDiscoverGenresAsync()).Genres,
            };
            if (_requestsApi.IsCurrentContext(context) && DiscoveryRows.Any(row => RequestDiscoveryLayout.BrandKind(row.Type) == kind)) Replace(target, cards);
        }
        catch (OperationCanceledException) { }
        catch { if (_requestsApi.IsCurrentContext(context)) BrandErrors[kind] = $"Couldn't load {(kind == "studio" ? "studios" : kind == "network" ? "networks" : "genres")}."; }
        finally { ((ICollection<KeyValuePair<string, long>>)_brandPending).Remove(new(kind, revision)); DataVersion++; }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (query.Length < 2)
        {
            SearchResults.Clear();
            StatusMessage = "Enter at least 2 characters to search.";
            return;
        }

        var owner = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _searchCts, owner);
        previous?.Cancel();
        var requestedMediaType = SelectedMediaType;
        var requestedPage = SearchPage;

        IsSearching = true;
        ErrorMessage = null;
        StatusMessage = "Searching...";

        try
        {
            var response = await _requestsApi.SearchAsync(requestedMediaType, query, requestedPage, owner.Token);
            owner.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Volatile.Read(ref _searchCts), owner)) return;

            SearchResults.Clear();
            foreach (var item in response.Results)
                SearchResults.Add(item);

            SearchTotalPages = response.TotalPages;
            SearchTotalResults = response.TotalResults;
            StatusMessage = "";
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // A newer query or navigation owns the search surface.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _searchCts, null, owner), owner))
                IsSearching = false;
            owner.Dispose();
        }
    }

    public async Task SetSearchPageAsync(int page)
    {
        SearchPage = Math.Clamp(page, 1, Math.Max(1, SearchTotalPages));
        await SearchAsync();
    }

    public void ClearSearch()
    {
        CancelSearch();
        SearchQuery = "";
        SearchPage = 1;
        SearchTotalPages = 0;
        SearchTotalResults = 0;
        SearchResults.Clear();
        ErrorMessage = null;
        StatusMessage = "";
    }

    public void CancelSearch()
    {
        Interlocked.Exchange(ref _searchCts, null)?.Cancel();
        IsSearching = false;
    }

    [RelayCommand]
    private async Task SubmitRequestAsync(RequestMediaResult result)
    {
        ErrorMessage = null;
        try
        {
            IsSubmitting = true;
            StatusMessage = $"Submitting {result.Title}...";
            await _requestsApi.CreateAsync(new CreateMediaRequestInput
            {
                MediaType = result.MediaType,
                TmdbId = result.TmdbId,
                Title = result.Title,
                Year = result.Year,
                Overview = result.Overview,
                PosterPath = result.PosterPath,
                BackdropPath = result.BackdropPath,
            });
            InvalidateCache();
            await LoadAsync();
            if (!string.IsNullOrWhiteSpace(SearchQuery))
                await SearchAsync();
            StatusMessage = "Request submitted.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to submit request: {ex.Message}";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private async Task CancelRequestAsync(MediaRequest request)
    {
        if (!SiloPlayer.Core.Services.RequestViewerPolicy.CanCancel(request)) return;
        try
        {
            await _requestsApi.CancelAsync(request.Id);
            InvalidateCache();
            await LoadAsync();
            StatusMessage = "Request cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to cancel request: {ex.Message}";
        }
    }

    private readonly HashSet<string> _removingRequests = new(StringComparer.Ordinal);
    public bool IsRemovingRequest(string id) => _removingRequests.Contains(id);

    /// <summary>Removal is offered for the viewer's failed, undelivered requests when the server advertises it.</summary>
    public bool CanRemoveRequest(MediaRequest request) => RequestViewerPolicy.CanRemove(Features, request);

    /// <summary>The account/profile that sees a row owns its removal, even if a confirmation outlives a profile change.</summary>
    public ApiIdentityContext CaptureRemovalOwner() => _requestsApi.CaptureIdentityContext();

    /// <summary>
    /// Deletes a failed request that delivered nothing. This is not cancellation. The server's guarded DELETE
    /// stays authoritative: a retry or library completion that wins the race answers 409, and either way the
    /// account list, title discovery state and any active search are read again so they show the outcome.
    /// </summary>
    public async Task<RequestRemovalResult> RemoveFailedRequestAsync(MediaRequest confirmed, ApiIdentityContext owner, CancellationToken visit = default)
    {
        var readOwner = _requestsApi.CaptureContext();
        bool IsCurrentOwner() => _requestsApi.IsCurrentIdentityContext(owner) && _requestsApi.IsCurrentContext(readOwner);
        bool IsCurrent() => !visit.IsCancellationRequested && IsCurrentOwner();
        // The viewer confirmed a row that may have been replaced or changed since: act on the current account row.
        var request = MyRequests.FirstOrDefault(row => row.Id == confirmed.Id);
        if (request == null || !IsCurrent() || !CanRemoveRequest(request) ||
            !_removingRequests.Add(request.Id)) return new(RequestRemovalOutcome.Skipped);
        DataVersion++;
        try
        {
            RequestRemovalResult result;
            try
            {
                // A confirmed deletion is not abandoned with the page visit; the identity binding alone stops a
                // switched profile from re-authoring it, and the visit only suppresses feedback.
                await _requestsApi.RemoveFailedAsync(request.Id, owner);
                result = new(RequestRemovalOutcome.Removed);
            }
            catch (OperationCanceledException) when (!IsCurrent()) { result = new(RequestRemovalOutcome.Retired); }
            catch (ApiException ex) when (ex.StatusCode is 404 or 409) { result = new(RequestRemovalOutcome.Refused, ex.Message); }
            catch (Exception ex) { result = new(RequestRemovalOutcome.Failed, ex.Message); }
            // The DELETE may have reached the server: a later visit must read the list again rather than reuse it.
            if (!IsCurrent())
            {
                InvalidateCache();
                // A returning cached page may already have loaded before the DELETE settled. Reconcile its
                // account rows even though the initiating visit retired, but never read for a different owner.
                if (IsCurrentOwner()) await LoadMineAsync();
                return new(RequestRemovalOutcome.Retired);
            }
            await RefreshAfterRequestChangeAsync(IsCurrent);
            return IsCurrent() ? result : new(RequestRemovalOutcome.Retired);
        }
        finally
        {
            _removingRequests.Remove(request.Id);
            if (_requestsApi.IsCurrentIdentityContext(owner)) DataVersion++;
        }
    }

    // A refused or failed removal still refreshes (the official WebUI's onSettled): an admin may have retried the
    // request, and the row should show what happened. A load already in flight predates the mutation.
    private async Task RefreshAfterRequestChangeAsync(Func<bool> isCurrent)
    {
        var loadWasRunning = _loadInProgress;
        var mineRead = _mineReadRevision;
        InvalidateCache();
        await LoadForVisitAsync(isCurrent);
        if (!isCurrent()) return;
        if (loadWasRunning) await Task.WhenAll(LoadMineAsync(), LoadDiscoveryAsync(brandsPreloaded: true));
        // The account list must not depend on the status read: a failed status read still re-reads it.
        else if (mineRead == _mineReadRevision && RequestsEnabled) await LoadMineAsync();
        if (!isCurrent()) return;
        if (!string.IsNullOrWhiteSpace(SearchQuery)) await SearchAsync();
        if (!isCurrent()) return;
        DataVersion++;
    }

    public void InvalidateCache() => _lastLoadedAt = DateTime.MinValue;
    public async Task RetryMineAsync() { await LoadMineAsync(); DataVersion++; }
    public async Task RetryDiscoveryAsync() { await LoadDiscoveryAsync(); DataVersion++; }
}

public enum RequestRemovalOutcome
{
    /// <summary>The server deleted the request (204).</summary>
    Removed,
    /// <summary>The server refused (409: retried, completed or delivered; 404: already gone). Lists were refreshed.</summary>
    Refused,
    /// <summary>A transport or server failure. The request is still listed and removal can be retried.</summary>
    Failed,
    /// <summary>Not eligible, unsupported by the server, or already pending.</summary>
    Skipped,
    /// <summary>The initiating account/profile or page visit retired; feedback is suppressed, only a still-current owner's account list may refresh, and the next load re-reads the lists.</summary>
    Retired,
}

public sealed record RequestRemovalResult(RequestRemovalOutcome Outcome, string? Error = null);
