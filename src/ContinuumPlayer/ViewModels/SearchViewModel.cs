using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private CancellationTokenSource? _searchCts;

    public SearchViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public ObservableCollection<MediaItem> Results { get; } = [];

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

            var response = await _catalogApi.SearchAsync(Query, 40, ct);

            if (ct.IsCancellationRequested) return;

            // Filter client-side since the server search endpoint may not support text search yet
            Results.Clear();
            var queryLower = Query.ToLowerInvariant();
            var filtered = response.Items.Where(i =>
                i.Title.Contains(queryLower, StringComparison.OrdinalIgnoreCase) ||
                (i.Overview ?? "").Contains(queryLower, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var item in filtered)
            {
                Results.Add(item);
            }
            TotalCount = filtered.Count;

            if (filtered.Count == 0 && response.Items.Count > 0)
            {
                ErrorMessage = "No matches found. Server-side search may need configuration.";
            }
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
}
