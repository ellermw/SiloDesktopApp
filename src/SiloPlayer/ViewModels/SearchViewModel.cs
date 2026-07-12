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

    public async Task LoadMediaScopeAsync()
    {
        try
        {
            var value = (await _settingsApi.GetSettingAsync("search.media_scope")).Value;
            MediaScope = value is "all" or "video" or "audiobook" ? value : "video";
        }
        catch { MediaScope = "video"; }
    }

    public async Task SetMediaScopeAsync(string scope)
    {
        MediaScope = scope is "all" or "video" or "audiobook" ? scope : "video";
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
            var catalogTask = _catalogApi.SearchAsync(Query, 40, MediaScope, ct);
            var includeVideoDiscovery = MediaScope is "all" or "video";
            var peopleTask = includeVideoDiscovery ? SearchPeopleAsync(Query, ct) : Task.FromResult(new List<Person>());
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
            TotalCount = response.Items.Count;
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
