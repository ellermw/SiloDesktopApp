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

    [ObservableProperty] private bool _isLoading;
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

            var mineTask = LoadMineAsync();
            var discoveryTask = LoadDiscoveryAsync();
            var brandsTask = LoadBrandsAsync();
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
        MineError = null;
        try
        {
            var response = await _requestsApi.GetMineAsync(limit: 100);
            Replace(MyRequests, response.Requests);
        }
        catch
        {
            MineError = "Couldn't load your requests";
            MyRequests.Clear();
        }
    }

    private async Task LoadDiscoveryAsync()
    {
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
    }

    private async Task LoadBrandsAsync()
    {
        async Task<IReadOnlyList<T>> TryLoad<T>(Func<Task<IReadOnlyList<T>>> load)
        {
            try { return await load(); }
            catch { return []; }
        }

        var studiosTask = TryLoad(async () => (IReadOnlyList<DiscoverBrandCard>)(await _requestsApi.GetDiscoverStudiosAsync()).Studios);
        var networksTask = TryLoad(async () => (IReadOnlyList<DiscoverBrandCard>)(await _requestsApi.GetDiscoverNetworksAsync()).Networks);
        var genresTask = TryLoad(async () => (IReadOnlyList<DiscoverBrandCard>)(await _requestsApi.GetDiscoverGenresAsync()).Genres);
        await Task.WhenAll(studiosTask, networksTask, genresTask);
        Replace(Studios, studiosTask.Result);
        Replace(Networks, networksTask.Result);
        Replace(Genres, genresTask.Result);
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

        IsSearching = true;
        ErrorMessage = null;
        StatusMessage = "Searching...";
        SearchResults.Clear();
        SearchTotalPages = 0;
        SearchTotalResults = 0;

        try
        {
            var response = await _requestsApi.SearchAsync(SelectedMediaType, query, SearchPage);
            SearchResults.Clear();
            foreach (var item in response.Results)
                SearchResults.Add(item);

            SearchTotalPages = response.TotalPages;
            SearchTotalResults = response.TotalResults;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
            StatusMessage = "";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public async Task SetSearchPageAsync(int page)
    {
        SearchPage = Math.Clamp(page, 1, Math.Max(1, SearchTotalPages));
        await SearchAsync();
    }

    public void ClearSearch()
    {
        SearchQuery = "";
        SearchPage = 1;
        SearchTotalPages = 0;
        SearchTotalResults = 0;
        SearchResults.Clear();
        ErrorMessage = null;
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task SubmitRequestAsync(RequestMediaResult result)
    {
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
}
