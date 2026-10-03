using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.ViewModels;

public partial class RequestsViewModel : ObservableObject
{
    private readonly RequestsApi _requestsApi;
    private DateTime _lastLoadedAt = DateTime.MinValue;
    private bool _loadInProgress;
    private bool _downloadRefreshInProgress;
    private int _mineReadRevision;
    private CancellationTokenSource? _searchCts;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public RequestsViewModel(RequestsApi requestsApi)
    {
        _requestsApi = requestsApi;
    }

    public ObservableCollection<RequestDiscoverySection> DiscoverySections { get; } = [];
    public ObservableCollection<RequestMediaResult> SearchResults { get; } = [];
    public ObservableCollection<MediaRequest> MyRequests { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Studios { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Networks { get; } = [];
    public ObservableCollection<DiscoverBrandCard> Genres { get; } = [];
    public RequestFeatureStatus Features { get; private set; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> BrandErrors { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _brandPending = new();
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
    private async Task LoadAsync()
    {
        if (_loadInProgress) return;
        if (DataVersion > 0 && DateTime.UtcNow - _lastLoadedAt < CacheDuration) return;

        _loadInProgress = true;
        IsLoading = DataVersion == 0;
        ErrorMessage = null;
        StatusMessage = "Loading requests...";

        try
        {
            var status = await _requestsApi.GetStatusAsync();
            Features = status;
            RequestsEnabled = status.RequestsEnabled;
            if (!RequestsEnabled)
            {
                DiscoverySections.Clear();
                MyRequests.Clear();
                StatusMessage = "Requests are disabled on this server.";
                _lastLoadedAt = DateTime.UtcNow;
                DataVersion++;
                return;
            }

            IsLoadingMine = IsLoadingDiscovery = true;
            var mineTask = LoadMineAsync();
            var discoveryTask = LoadDiscoveryAsync();
            var brandsTask = LoadBrandsAsync();
            // Each WebUI query owns its loading state. A slow discovery rail
            // must not hide completed account requests or brand results.
            IsLoading = false;
            await Task.WhenAll(mineTask, discoveryTask, brandsTask);

            StatusMessage = MyRequests.Count > 0 ? $"{MyRequests.Count:N0} request(s) loaded." : "";
            _lastLoadedAt = DateTime.UtcNow;
            DataVersion++;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load requests: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            _loadInProgress = false;
            IsLoading = false;
        }
    }

    private async Task LoadMineAsync()
    {
        IsLoadingMine = true;
        var revision = ++_mineReadRevision;
        MineError = null;
        try
        {
            var response = await _requestsApi.GetMineAsync(limit: 100);
            if (revision != _mineReadRevision) return;
            Replace(MyRequests, response.Requests);
        }
        catch
        {
            if (revision != _mineReadRevision) return;
            MineError = "Couldn't load your requests";
            MyRequests.Clear();
        }
        finally { IsLoadingMine = false; DataVersion++; }
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

    private async Task LoadDiscoveryAsync()
    {
        IsLoadingDiscovery = true;
        DiscoveryError = null;
        try
        {
            var discoverTask = _requestsApi.GetDiscoveryAsync();
            Replace(DiscoverySections, (await discoverTask).Sections);
        }
        catch
        {
            DiscoveryError = "Discovery is offline";
            DiscoverySections.Clear();
        }
        finally { IsLoadingDiscovery = false; DataVersion++; }
    }

    private async Task LoadBrandsAsync()
    {
        await Task.WhenAll(LoadBrandAsync("studio"), LoadBrandAsync("network"), LoadBrandAsync("genre"));
    }

    private async Task LoadBrandAsync(string kind)
    {
        var target = kind switch { "studio" => Studios, "network" => Networks, "genre" => Genres, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (!_brandPending.TryAdd(kind, 0)) return;
        BrandErrors.TryRemove(kind, out _);
        try
        {
            var cards = kind switch
            {
                "studio" => (await _requestsApi.GetDiscoverStudiosAsync()).Studios,
                "network" => (await _requestsApi.GetDiscoverNetworksAsync()).Networks,
                _ => (await _requestsApi.GetDiscoverGenresAsync()).Genres,
            };
            Replace(target, cards);
        }
        catch (OperationCanceledException) { }
        catch { BrandErrors[kind] = $"Couldn't load {(kind == "studio" ? "studios" : kind == "network" ? "networks" : "genres")}."; }
        finally { _brandPending.TryRemove(kind, out _); DataVersion++; }
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

    public void InvalidateCache() => _lastLoadedAt = DateTime.MinValue;
    public async Task RetryMineAsync() { await LoadMineAsync(); DataVersion++; }
    public async Task RetryDiscoveryAsync() { await LoadDiscoveryAsync(); DataVersion++; }
}
