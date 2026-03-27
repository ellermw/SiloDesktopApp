using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public LibraryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private Library? _library;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _selectedSort = "title";

    [ObservableProperty]
    private string? _selectedOrder = "asc";

    [ObservableProperty]
    private string? _selectedGenre;

    [ObservableProperty]
    private string? _selectedType;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasMore;

    private int _offset;
    private const int PageSize = 40;

    // Filter options loaded from server
    public ObservableCollection<string> Genres { get; } = [];
    public ObservableCollection<string> SortOptions { get; } = ["title", "year", "rating_imdb", "created_at", "added_at"];

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Library == null) return;

        _offset = 0;
        Items.Clear();
        await LoadPageAsync();
        await LoadFiltersAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading || Library == null) return;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        _offset = 0;
        Items.Clear();
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task JumpToLetterAsync(string letter)
    {
        if (Library == null) return;

        // Ensure sorted by title ascending for letter jump to make sense
        SelectedSort = "title";
        SelectedOrder = "asc";

        // Estimate offset based on letter position
        int letterIndex = letter == "#" ? 0 : (letter[0] - 'A' + 1);
        int estimatedOffset = (int)((letterIndex / 27.0) * TotalCount);

        // Round down to nearest page boundary
        estimatedOffset = (estimatedOffset / PageSize) * PageSize;

        _offset = estimatedOffset;
        Items.Clear();
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        if (Library == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _catalogApi.GetCatalogAsync(
                libraryId: Library.Id,
                sort: SelectedSort,
                order: SelectedOrder,
                genre: SelectedGenre,
                limit: PageSize,
                offset: _offset);

            foreach (var item in response.Items)
            {
                Items.Add(item);
            }

            TotalCount = response.Total;
            _offset += response.Items.Count;
            HasMore = response.HasMore;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFiltersAsync()
    {
        if (Library == null) return;

        try
        {
            var filters = await _catalogApi.GetFiltersAsync(Library.Id);
            Genres.Clear();
            Genres.Add(""); // All genres
            foreach (var genre in filters.Genres)
            {
                Genres.Add(genre);
            }
        }
        catch
        {
            // Filters are optional, don't block UI
        }
    }
}
