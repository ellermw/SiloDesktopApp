using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly PeopleApi _peopleApi;
    private CancellationTokenSource? _searchCts;

    public SearchViewModel(CatalogApi catalogApi, PeopleApi peopleApi)
    {
        _catalogApi = catalogApi;
        _peopleApi = peopleApi;
    }

    public ObservableCollection<MediaItem> Results { get; } = [];
    public ObservableCollection<Person> PeopleResults { get; } = [];

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _totalCount;

    [RelayCommand]
    private async Task SearchAsync()
    {
        // Cancel previous search
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            PeopleResults.Clear();
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
            var catalogTask = _catalogApi.SearchAsync(Query, 40, ct);
            var peopleTask = SearchPeopleAsync(Query, ct);

            await Task.WhenAll(catalogTask, peopleTask);

            if (ct.IsCancellationRequested) return;

            var response = await catalogTask;

            // Update people results
            PeopleResults.Clear();
            var people = await peopleTask;
            foreach (var person in people)
                PeopleResults.Add(person);

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
