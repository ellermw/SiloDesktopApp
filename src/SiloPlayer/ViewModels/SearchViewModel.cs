using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly PeopleApi _peopleApi;
    private readonly RequestsApi _requestsApi;
    private readonly SettingsApi _settingsApi;
    private CancellationTokenSource? _searchCts;

    public SearchViewModel(CatalogApi catalogApi, PeopleApi peopleApi, RequestsApi requestsApi, SettingsApi settingsApi)
    {
        _catalogApi = catalogApi;
        _peopleApi = peopleApi;
        _requestsApi = requestsApi;
        _settingsApi = settingsApi;
    }

    public ObservableCollection<MediaItem> Results { get; } = [];
    public ObservableCollection<Person> PeopleResults { get; } = [];
    public ObservableCollection<RequestMediaResult> OutsideLibraryResults { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _mediaScope = "video";

    [ObservableProperty]
    private string? _mediaType = "video";

    [ObservableProperty] private string _sortField = "added_at";
    [ObservableProperty] private string _sortOrder = "desc";
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _contentRating;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _country;

    public CatalogFiltersResponse? AvailableFilters { get; private set; }

    public async Task LoadFiltersAsync()
    {
        try { AvailableFilters = await _catalogApi.GetFiltersAsync(source: "query"); }
        catch { AvailableFilters = new CatalogFiltersResponse(); }
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
        try { await _settingsApi.PutSettingAsync("search.media_scope", MediaScope); } catch { }
        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync();
    }

    public async Task SetMediaTypeAsync(string? type)
    {
        MediaType = type is "video" or "movie" or "series" or "audiobook" ? type : null;
        MediaScope = MediaType switch
        {
            "audiobook" => "audiobook",
            "video" or "movie" or "series" => "video",
            _ => "all"
        };
        try { await _settingsApi.PutSettingAsync("search.media_scope", MediaScope); } catch { }
        if (!string.IsNullOrWhiteSpace(Query)) await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            PeopleResults.Clear();
            OutsideLibraryResults.Clear();
            TotalCount = 0;
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Small delay for debounce
            await Task.Delay(300, ct);
            if (ct.IsCancellationRequested) return;

            // Search catalog and people in parallel
            var catalogTask = _catalogApi.GetCatalogAsync(
                null,
                sort: SortField,
                order: SortOrder,
                genre: Genre,
                contentRating: ContentRating,
                resolution: Resolution,
                country: Country,
                q: Query,
                type: MediaType,
                limit: 60,
                source: "query",
                ct: ct);
            var includeVideoDiscovery = MediaScope is "all" or "video";
            // The dedicated Catalog page does not render people; global search
            // owns that surface. Avoid an invisible extra network request.
            var peopleTask = Task.FromResult(new List<Person>());
            var outsideTask = includeVideoDiscovery ? SearchOutsideLibraryAsync(Query, ct) : Task.FromResult(new List<RequestMediaResult>());

            await Task.WhenAll(catalogTask, peopleTask, outsideTask);

            if (ct.IsCancellationRequested) return;

            var response = await catalogTask;

            // Update people results
            PeopleResults.Clear();
            var people = await peopleTask;
            foreach (var person in people)
                PeopleResults.Add(person);

            OutsideLibraryResults.Clear();
            foreach (var item in await outsideTask)
                OutsideLibraryResults.Add(item);

            // Use server results directly — server handles text search
            Results.Clear();
            foreach (var item in response.Items)
                Results.Add(item);
            TotalCount = response.Total > 0 ? response.Total : response.Items.Count;
        }
        catch (OperationCanceledException)
        {
            // Expected on new search
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private async Task<List<RequestMediaResult>> SearchOutsideLibraryAsync(string query, CancellationToken ct)
    {
        try
        {
            var status = await _requestsApi.GetStatusAsync(ct);
            if (!status.RequestsEnabled) return [];
            var response = await _requestsApi.SearchAsync("all", query, 1, ct);
            return response.Results.Where(item => !string.Equals(item.Availability, "available", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch { return []; }
    }

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
