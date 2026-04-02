using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;
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

    /// <summary>Fired after each page is loaded so the UI can check if more content is needed to fill the viewport.</summary>
    public event Action? PageLoaded;

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
    private const int PageSize = 100;

    // Enhanced filter properties
    [ObservableProperty]
    private string? _selectedStudio;

    [ObservableProperty]
    private string? _selectedCountry;

    [ObservableProperty]
    private string? _selectedResolution;

    [ObservableProperty]
    private string? _selectedAudioLanguage;

    // Filter options loaded from server
    public ObservableCollection<string> Genres { get; } = [];
    public ObservableCollection<string> ContentRatings { get; } = [];
    public ObservableCollection<string> Studios { get; } = [];
    public ObservableCollection<string> Countries { get; } = [];
    public ObservableCollection<string> Resolutions { get; } = [];
    public ObservableCollection<string> AudioLanguages { get; } = [];
    public ObservableCollection<string> SortOptions { get; } = ["title", "year", "rating_imdb", "created_at", "added_at"];

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

    // Cached letter->offset mapping per library, built on first use
    private Dictionary<char, int>? _letterOffsets;
    private int _letterOffsetLibraryId;

    [RelayCommand]
    private async Task JumpToLetterAsync(string letter)
    {
        if (Library == null || TotalCount == 0) return;

        SelectedSort = "title";
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
                libraryId: Library!.Id, sort: "title", order: "asc", limit: 1, offset: offset);
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
                limit: PageSize,
                offset: _offset);

            foreach (var item in response.Items)
            {
                Items.Add(item);
            }

            TotalCount = response.Total;
            _offset += response.Items.Count;
            HasMore = response.HasMore;

            // Signal that a page was loaded (UI can check if more is needed)
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
            Genres.Clear();
            Genres.Add(""); // All genres
            foreach (var genre in filters.Genres)
                Genres.Add(genre);

            ContentRatings.Clear();
            ContentRatings.Add(""); // All ratings
            foreach (var rating in filters.ContentRatings)
                ContentRatings.Add(rating);

            Studios.Clear();
            Studios.Add(""); // All studios
            foreach (var studio in filters.Studios)
                Studios.Add(studio);

            Countries.Clear();
            Countries.Add(""); // All countries
            foreach (var country in filters.Countries)
                Countries.Add(country);

            Resolutions.Clear();
            Resolutions.Add(""); // All resolutions
            foreach (var res in filters.Resolutions)
                Resolutions.Add(res);

            AudioLanguages.Clear();
            AudioLanguages.Add(""); // All audio languages
            foreach (var lang in filters.AudioLanguages)
                AudioLanguages.Add(lang);
        }
        catch
        {
            // Filters are optional, don't block UI
        }
    }
}
