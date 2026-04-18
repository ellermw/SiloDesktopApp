using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Helpers;

namespace ContinuumPlayer.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public LibraryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public BulkObservableCollection<MediaItem> Items { get; } = [];

    /// <summary>Fired after each page is loaded so the UI can check if more content is needed to fill the viewport.</summary>
    public event Action? PageLoaded;

    [ObservableProperty]
    private Library? _library;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // B43: align with WebUI catalog sort param.
    [ObservableProperty]
    private string? _selectedSort = "sort_title";

    [ObservableProperty]
    private string? _selectedOrder = "asc";

    [ObservableProperty]
    private string? _selectedGenre;

    [ObservableProperty]
    private string? _selectedType;

    [ObservableProperty]
    private string? _selectedContentRating;

    [ObservableProperty]
    private string? _selectedYearMin;

    [ObservableProperty]
    private string? _selectedYearMax;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _hasMore;

    private int _offset;
    private const int PageSize = 40;

    // Enhanced filter properties
    [ObservableProperty]
    private string? _selectedStudio;

    [ObservableProperty]
    private string? _selectedCountry;

    [ObservableProperty]
    private string? _selectedResolution;

    [ObservableProperty]
    private string? _selectedAudioLanguage;

    // Filter options loaded from server — BulkObservableCollection fires ONE event
    // per AddRange instead of per-item, preventing hundreds of redundant ComboBox rebuilds
    public BulkObservableCollection<string> Genres { get; } = [];
    public BulkObservableCollection<string> ContentRatings { get; } = [];
    public BulkObservableCollection<string> Studios { get; } = [];
    public BulkObservableCollection<string> Countries { get; } = [];
    public BulkObservableCollection<string> Resolutions { get; } = [];
    public BulkObservableCollection<string> AudioLanguages { get; } = [];
    // B43: tags must match the WebUI catalog API contract: sort_title / recently_added /
    // year / rating_imdb. Saved filters from web don't bridge if these don't match.
    public ObservableCollection<string> SortOptions { get; } = ["sort_title", "recently_added", "year", "rating_imdb"];

    // Collections
    public ObservableCollection<LibraryCollection> Collections { get; } = [];

    [ObservableProperty]
    private bool _isCollectionsLoading;

    [ObservableProperty]
    private bool _collectionsLoaded;

    [RelayCommand]
    private async Task LoadCollectionsAsync()
    {
        if (Library == null || IsCollectionsLoading) return;

        IsCollectionsLoading = true;
        try
        {
            var response = await _catalogApi.GetLibraryCollectionsAsync(Library.Id);
            Collections.Clear();
            foreach (var c in response.Collections)
                Collections.Add(c);
            CollectionsLoaded = true;
        }
        catch
        {
            // Collections load failure is non-fatal
        }
        finally
        {
            IsCollectionsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Library == null) return;

        _offset = 0;
        Items.Clear();
        // Load items and filters in parallel — independent operations
        await Task.WhenAll(LoadPageAsync(), LoadFiltersAsync());
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

    // Cached letter->offset mapping per library, built on first use
    private Dictionary<char, int>? _letterOffsets;
    private int _letterOffsetLibraryId;

    [RelayCommand]
    private async Task JumpToLetterAsync(string letter)
    {
        if (Library == null || TotalCount == 0) return;

        // B43: aligned with WebUI sort_title.
        SelectedSort = "sort_title";
        SelectedOrder = "asc";

        if (letter == "#")
        {
            _offset = 0;
            Items.Clear();
            await LoadPageAsync();
            return;
        }

        IsLoading = true;

        try
        {
            // Build letter offset cache on first use (or if library changed)
            if (_letterOffsets == null || _letterOffsetLibraryId != Library.Id)
            {
                await BuildLetterOffsetsAsync();
            }

            char target = char.ToUpper(letter[0]);
            if (_letterOffsets != null && _letterOffsets.TryGetValue(target, out int cachedOffset))
            {
                _offset = (cachedOffset / PageSize) * PageSize;
            }
            else
            {
                // Fallback: rough estimate
                int letterIndex = target - 'A' + 1;
                _offset = (int)((letterIndex / 27.0) * TotalCount);
                _offset = (_offset / PageSize) * PageSize;
            }

            Items.Clear();
            await LoadPageAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task BuildLetterOffsetsAsync()
    {
        if (Library == null) return;

        _letterOffsets = new Dictionary<char, int>();
        _letterOffsetLibraryId = Library.Id;

        // Sample ~20 evenly spaced points across the catalog to map letter positions
        int sampleCount = 20;
        int step = Math.Max(1, TotalCount / sampleCount);
        var samples = new List<(int offset, char letter)>();

        var tasks = new List<Task<(int offset, char letter)>>();
        for (int i = 0; i < sampleCount && i * step < TotalCount; i++)
        {
            int offset = i * step;
            tasks.Add(ProbeSingleAsync(offset));
        }

        var results = await Task.WhenAll(tasks);
        samples.AddRange(results.Where(r => r.letter != '\0'));
        samples.Sort((a, b) => a.offset.CompareTo(b.offset));

        // For each letter A-Z, find the lowest offset where that letter first appears
        for (char c = 'A'; c <= 'Z'; c++)
        {
            int bestOffset = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].letter < c)
                    bestOffset = samples[i].offset;
                else if (samples[i].letter == c)
                {
                    bestOffset = samples[i].offset;
                    break;
                }
                else
                    break;
            }
            _letterOffsets[c] = bestOffset;
        }
    }

    private async Task<(int offset, char letter)> ProbeSingleAsync(int offset)
    {
        try
        {
            var probe = await _catalogApi.GetCatalogAsync(
                libraryId: Library!.Id, sort: "sort_title", order: "asc", limit: 1, offset: offset);
            if (probe.Items.Count > 0)
            {
                var title = probe.Items[0].Title.TrimStart();
                if (title.Length > 0)
                {
                    char c = char.ToUpper(title[0]);
                    if (c >= 'A' && c <= 'Z')
                        return (offset, c);
                }
            }
        }
        catch { }
        return (offset, '\0');
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
                studio: SelectedStudio,
                contentRating: SelectedContentRating,
                country: SelectedCountry,
                resolution: SelectedResolution,
                audioLanguage: SelectedAudioLanguage,
                yearMin: SelectedYearMin,
                yearMax: SelectedYearMax,
                type: SelectedType,
                limit: PageSize,
                offset: _offset);

            Items.AddRange(response.Items);

            TotalCount = response.Total;
            _offset += response.Items.Count;
            HasMore = response.HasMore;

            PageLoaded?.Invoke();
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

            // Single AddRange per filter → one CollectionChanged event → one ComboBox rebuild.
            // Previously each .Add() fired CollectionChanged, causing ~192 redundant ComboBox
            // rebuilds that froze the UI thread.
            Genres.Clear();
            Genres.AddRange(filters.Genres.Prepend(""));

            ContentRatings.Clear();
            ContentRatings.AddRange(filters.ContentRatings.Prepend(""));

            Studios.Clear();
            Studios.AddRange(filters.Studios.Prepend(""));

            Countries.Clear();
            Countries.AddRange(filters.Countries.Prepend(""));

            Resolutions.Clear();
            Resolutions.AddRange(filters.Resolutions.Prepend(""));

            AudioLanguages.Clear();
            AudioLanguages.AddRange(filters.AudioLanguages.Prepend(""));
        }
        catch
        {
            // Filters are optional, don't block UI
        }
    }
}
