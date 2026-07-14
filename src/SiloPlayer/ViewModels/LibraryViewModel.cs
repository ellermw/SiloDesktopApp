using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public sealed class EditableQueryGroup
{
    public string Match { get; set; } = "all";
    public ObservableCollection<QueryRule> Rules { get; } = [];
}

public partial class LibraryViewModel : ObservableObject
{
    private const int PageSize = 60;
    private const int MaxInFlightCatalogPages = 3;
    private readonly CatalogApi _catalogApi;
    private readonly HashSet<int> _loadingPages = [];
    private readonly Dictionary<int, CatalogResponse> _pageResponses = [];
    private string? _snapshot;
    private int _estimatedTotalItems;
    private bool _hasExactTotal;
    private int _lastVisibleEndIndex = PageSize - 1;
    private int _queryVersion;
    private int _windowLoadVersion;
    private CancellationTokenSource _catalogQueryCts = new();
    private CancellationTokenSource? _windowLoadCts;
    private IReadOnlyList<MediaItem> _windowItems = [];

    public LibraryViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        AdvancedGroups.Add(CreateEmptyAdvancedGroup());
    }

    public VirtualCatalogItems Items { get; } = new(PageSize);

    /// <summary>Fired after each page is loaded so the UI can check if more content is needed to fill the viewport.</summary>
    public event Action? PageLoaded;

    /// <summary>Fired when the active catalog window is replaced.</summary>
    public event Action? WindowLoaded;

    public int WindowStartIndex { get; private set; } = -1;
    public int WindowEndIndex => WindowStartIndex < 0 || _windowItems.Count == 0 ? -1 : WindowStartIndex + _windowItems.Count - 1;

    [ObservableProperty]
    private Library? _library;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Current WebUI query sort field.
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
    private int _displayTotalCount;

    [ObservableProperty]
    private bool _hasMore;

    // Enhanced filter properties
    [ObservableProperty]
    private string? _selectedStudio;

    [ObservableProperty]
    private string? _selectedCountry;

    [ObservableProperty]
    private string? _selectedResolution;

    [ObservableProperty]
    private string? _selectedAudioLanguage;

    [ObservableProperty] private string? _selectedMinimumRating;
    [ObservableProperty] private string? _selectedOriginalLanguage;
    [ObservableProperty] private string? _selectedActor;
    [ObservableProperty] private string? _selectedDirector;
    [ObservableProperty] private string? _selectedWriter;
    [ObservableProperty] private string? _selectedProducer;
    [ObservableProperty] private string? _selectedAuthor;
    [ObservableProperty] private string? _selectedNarrator;
    [ObservableProperty] private string? _selectedSeries;
    [ObservableProperty] private string? _selectedNetwork;
    [ObservableProperty] private string? _selectedMatchStatus;
    [ObservableProperty] private string? _selectedWatchStatus;
    [ObservableProperty] private string? _selectedAddedInLast;
    [ObservableProperty] private string? _selectedReleasedInLast;
    [ObservableProperty] private bool _selectedFourK;
    [ObservableProperty] private bool _selectedHdr;
    [ObservableProperty] private bool _selectedDolbyVision;

    // Filter options loaded from server — BulkObservableCollection fires ONE event
    // per AddRange instead of per-item, preventing hundreds of redundant ComboBox rebuilds
    public BulkObservableCollection<string> Genres { get; } = [];
    public BulkObservableCollection<string> ContentRatings { get; } = [];
    public BulkObservableCollection<string> Studios { get; } = [];
    public BulkObservableCollection<string> Countries { get; } = [];
    public BulkObservableCollection<string> Resolutions { get; } = [];
    public BulkObservableCollection<string> AudioLanguages { get; } = [];
    public BulkObservableCollection<string> OriginalLanguages { get; } = [];
    public BulkObservableCollection<string> Networks { get; } = [];
    public ObservableCollection<EditableQueryGroup> AdvancedGroups { get; } = [];
    public ObservableCollection<QueryRule> AdvancedRules => AdvancedGroups[0].Rules;

    public bool UseAdvancedRules { get; set; }
    public string AdvancedRulesMatch { get; set; } = "all";
    public ObservableCollection<string> SortOptions { get; } = ["title", "added_at", "release_date", "year", "rating_imdb"];

    // Collections
    public ObservableCollection<LibraryCollection> Collections { get; } = [];
    public ObservableCollection<LibraryTabSection> CollectionSections { get; } = [];

    [ObservableProperty]
    private bool _isCollectionsLoading;

    [ObservableProperty]
    private bool _collectionsLoaded;

    [RelayCommand]
    private async Task LoadCollectionsAsync()
    {
        if (Library == null || IsCollectionsLoading) return;

        IsCollectionsLoading = true;
        Collections.Clear();
        CollectionSections.Clear();
        try
        {
            var response = await _catalogApi.GetLibraryCollectionsAsync(Library.Id);
            foreach (var c in response.Collections)
                Collections.Add(c);

            foreach (var section in BuildCollectionSections(response))
                CollectionSections.Add(section);

            CollectionsLoaded = true;
        }
        catch
        {
            // Collections load failure is non-fatal
            CollectionsLoaded = false;
        }
        finally
        {
            IsCollectionsLoading = false;
        }
    }

    private static IEnumerable<LibraryTabSection> BuildCollectionSections(LibraryTabResponse response)
    {
        var sections = new List<LibraryTabSection>();

        foreach (var group in response.Groups.Where(g => g.Collections.Count > 0))
        {
            var section = new LibraryTabSection
            {
                Title = group.Name,
                Kind = group.Kind,
                SortOrder = group.SortOrder,
                IsUngrouped = false,
            };

            foreach (var collection in group.Collections)
                section.Collections.Add(ToDisplayCollection(collection, group.Kind));

            sections.Add(section);
        }

        if (response.Ungrouped?.Collections.Count > 0)
        {
            var section = new LibraryTabSection
            {
                Kind = "regular",
                SortOrder = response.Ungrouped.SortOrder,
                IsUngrouped = true,
            };

            foreach (var collection in response.Ungrouped.Collections)
                section.Collections.Add(ToDisplayCollection(collection, "regular"));

            sections.Add(section);
        }

        // Older server builds returned only the flat admin collection list.
        // Keep that path working while the grouped tab contract rolls out.
        if (sections.Count == 0 && response.Collections.Count > 0)
        {
            var section = new LibraryTabSection
            {
                Kind = "regular",
                SortOrder = 9999,
                IsUngrouped = true,
            };

            foreach (var collection in response.Collections.OrderBy(c => c.SortOrder).ThenBy(c => c.Title))
                section.Collections.Add(ToDisplayCollection(collection));

            sections.Add(section);
        }

        return sections
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
    }

    private static LibraryTabCollectionDisplay ToDisplayCollection(LibraryTabCollection collection, string groupKind)
    {
        var isUserCollection = string.Equals(groupKind, "user_collections", StringComparison.OrdinalIgnoreCase);
        return new LibraryTabCollectionDisplay
        {
            Id = collection.Id,
            Title = collection.Title,
            PosterUrl = collection.PosterUrl,
            PosterThumbhash = collection.PosterThumbhash,
            ItemCount = collection.ItemCount,
            Featured = collection.Featured,
            IsUserCollection = isUserCollection,
            TypeLabel = isUserCollection ? "USER" : collection.Featured ? "FEATURED" : "COLLECTION",
        };
    }

    private static LibraryTabCollectionDisplay ToDisplayCollection(LibraryCollection collection)
        => new()
        {
            Id = collection.Id,
            Title = collection.Title,
            PosterUrl = collection.PosterUrl,
            PosterThumbhash = collection.PosterThumbhash,
            ItemCount = collection.ItemCount,
            Featured = collection.Featured,
            IsUserCollection = false,
            TypeLabel = string.IsNullOrWhiteSpace(collection.CollectionType)
                ? "COLLECTION"
                : collection.CollectionType.ToUpperInvariant(),
        };

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (Library == null) return;

        var version = StartNewCatalogQuery();
        var ct = _catalogQueryCts.Token;
        Items.Clear();
        // Load items and filters in parallel — independent operations
        await Task.WhenAll(LoadWindowAsync(0, PageSize, force: true), LoadFiltersAsync(version, ct));
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Library == null || TotalCount <= 0) return;

        var pageCount = (TotalCount + PageSize - 1) / PageSize;
        for (int page = 0; page < pageCount; page++)
        {
            if (!Items.IsPageLoaded(page))
            {
                await LoadPagesAsync(page, page, _queryVersion, _catalogQueryCts.Token);
                return;
            }
        }
    }

    [RelayCommand]
    private async Task ApplyFilterAsync()
    {
        StartNewCatalogQuery();
        Items.Clear();
        await LoadWindowAsync(0, PageSize, force: true);
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
            StartNewCatalogQuery();
            Items.Clear();
            await LoadWindowAsync(0, PageSize, force: true);
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
            int targetOffset;
            if (_letterOffsets != null && _letterOffsets.TryGetValue(target, out int cachedOffset))
            {
                targetOffset = (cachedOffset / PageSize) * PageSize;
            }
            else
            {
                // Fallback: rough estimate
                int letterIndex = target - 'A' + 1;
                targetOffset = (int)((letterIndex / 27.0) * TotalCount);
                targetOffset = (targetOffset / PageSize) * PageSize;
            }

            await LoadWindowAsync(targetOffset, PageSize, force: true);
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

    public async Task EnsureRangeLoadedAsync(int startIndex, int endIndex)
    {
        if (Library == null || TotalCount <= 0 || startIndex > endIndex) return;

        startIndex = Math.Clamp(startIndex, 0, TotalCount - 1);
        endIndex = Math.Clamp(endIndex, startIndex, TotalCount - 1);
        _lastVisibleEndIndex = endIndex;

        await LoadWindowAsync(startIndex, endIndex - startIndex + 1);
    }

    public bool HasWindow(int startIndex, int endIndex)
    {
        return startIndex >= 0 &&
            endIndex >= startIndex &&
            WindowStartIndex >= 0 &&
            startIndex >= WindowStartIndex &&
            endIndex <= WindowEndIndex;
    }

    public MediaItem? GetWindowItem(int absoluteIndex)
    {
        if (absoluteIndex < WindowStartIndex || absoluteIndex > WindowEndIndex)
            return null;

        var relativeIndex = absoluteIndex - WindowStartIndex;
        if (relativeIndex < 0 || relativeIndex >= _windowItems.Count)
            return null;

        return _windowItems[relativeIndex];
    }

    public async Task LoadWindowAsync(int startIndex, int itemCount, bool force = false)
    {
        if (Library == null || itemCount <= 0)
            return;

        if (TotalCount > 0)
        {
            startIndex = Math.Clamp(startIndex, 0, TotalCount - 1);
            itemCount = Math.Clamp(itemCount, 1, TotalCount - startIndex);
        }
        else
        {
            startIndex = 0;
            itemCount = Math.Max(1, itemCount);
        }

        var endIndex = startIndex + itemCount - 1;
        if (!force && HasWindow(startIndex, endIndex))
            return;

        CancelWindowLoad();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_catalogQueryCts.Token);
        _windowLoadCts = linkedCts;
        var ct = linkedCts.Token;
        var windowVersion = ++_windowLoadVersion;
        var queryVersion = _queryVersion;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var includeTotal = !_hasExactTotal || TotalCount <= 0;
            var response = await FetchWindowAsync(startIndex, itemCount, includeTotal, _snapshot, ct);
            if (ct.IsCancellationRequested || queryVersion != _queryVersion || windowVersion != _windowLoadVersion)
                return;

            ApplyWindowResponse(startIndex, response, includeTotal);
            WindowLoaded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (queryVersion == _queryVersion && windowVersion == _windowLoadVersion)
                ErrorMessage = $"Failed to load catalog: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_windowLoadCts, linkedCts))
            {
                _windowLoadCts = null;
                linkedCts.Dispose();
            }

            if (queryVersion == _queryVersion && windowVersion == _windowLoadVersion)
                IsLoading = false;
        }
    }

    private int StartNewCatalogQuery()
    {
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _loadingPages.Clear();
        _pageResponses.Clear();
        _snapshot = null;
        _estimatedTotalItems = 0;
        _hasExactTotal = false;
        _lastVisibleEndIndex = PageSize - 1;
        TotalCount = 0;
        DisplayTotalCount = 0;
        HasMore = false;
        ErrorMessage = null;
        ClearActiveWindow(notify: true);
        return _queryVersion;
    }

    public void CancelCatalogLoads()
    {
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _loadingPages.Clear();
        _pageResponses.Clear();
        ClearActiveWindow(notify: true);
        IsLoading = false;
    }

    private void CancelCurrentCatalogQuery()
    {
        try { _catalogQueryCts.Cancel(); } catch { }
        _catalogQueryCts.Dispose();
    }

    private void CancelWindowLoad()
    {
        var cts = _windowLoadCts;
        if (cts == null)
            return;

        try { cts.Cancel(); } catch { }
        cts.Dispose();
        _windowLoadCts = null;
        _windowLoadVersion++;
    }

    private void ClearActiveWindow(bool notify)
    {
        _windowItems = [];
        WindowStartIndex = -1;
        if (notify)
            WindowLoaded?.Invoke();
    }

    private async Task LoadInitialPageAsync(int version, CancellationToken ct, bool includeTotal)
    {
        if (Library == null) return;

        IsLoading = true;
        ErrorMessage = null;
        _loadingPages.Add(0);

        try
        {
            var response = await FetchPageAsync(0, includeTotal: includeTotal, ct: ct);
            if (version != _queryVersion) return;

            _snapshot = response.Snapshot;
            _pageResponses[0] = response;
            RecomputeVirtualTotal();
            Items.SetPage(0, response.Items);

            PageLoaded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == _queryVersion)
                ErrorMessage = $"Failed to load catalog: {ex.Message}";
        }
        finally
        {
            if (version == _queryVersion)
            {
                _loadingPages.Remove(0);
                if (_loadingPages.Count == 0)
                    IsLoading = false;
            }
        }
    }

    private async Task LoadPagesAsync(int startPage, int endPage, int version, CancellationToken ct)
    {
        if (Library == null || version != _queryVersion) return;

        var pagesToLoad = new List<int>();
        var availableSlots = Math.Max(0, MaxInFlightCatalogPages - _loadingPages.Count);
        if (availableSlots == 0) return;

        for (int page = startPage; page <= endPage; page++)
        {
            if (Items.IsPageLoaded(page) || !_loadingPages.Add(page)) continue;
            pagesToLoad.Add(page);
            if (pagesToLoad.Count >= availableSlots) break;
        }

        if (pagesToLoad.Count == 0) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var tasks = pagesToLoad.Select(async page => (Page: page, Response: await FetchPageAsync(page, includeTotal: false, snapshot: _snapshot, ct: ct)));
            var results = await Task.WhenAll(tasks);
            if (version != _queryVersion) return;

            foreach (var result in results.OrderBy(r => r.Page))
            {
                _pageResponses[result.Page] = result.Response;
                if (string.IsNullOrWhiteSpace(_snapshot))
                    _snapshot = result.Response.Snapshot;
            }

            RecomputeVirtualTotal();

            foreach (var result in results.OrderBy(r => r.Page))
                Items.SetPage(result.Page, result.Response.Items);
            PageLoaded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == _queryVersion)
                ErrorMessage = $"Failed to load catalog: {ex.Message}";
        }
        finally
        {
            if (version == _queryVersion)
            {
                foreach (var page in pagesToLoad)
                    _loadingPages.Remove(page);

                if (_loadingPages.Count == 0)
                    IsLoading = false;
            }
        }
    }

    private Task<CatalogResponse> FetchPageAsync(int pageIndex, bool includeTotal, string? snapshot = null, CancellationToken ct = default)
        => _catalogApi.GetCatalogAsync(
            libraryId: Library!.Id,
            sort: SelectedSort,
            order: SelectedOrder,
            genre: UseAdvancedRules ? null : SelectedGenre,
            studio: UseAdvancedRules ? null : SelectedStudio,
            contentRating: UseAdvancedRules ? null : SelectedContentRating,
            country: UseAdvancedRules ? null : SelectedCountry,
            resolution: UseAdvancedRules ? null : SelectedResolution,
            audioLanguage: UseAdvancedRules ? null : SelectedAudioLanguage,
            yearMin: UseAdvancedRules ? null : SelectedYearMin,
            yearMax: UseAdvancedRules ? null : SelectedYearMax,
            type: UseAdvancedRules ? null : SelectedType,
            extraRules: BuildExtraRules(),
            extraRulesMatch: AdvancedRulesMatch,
            queryGroups: BuildAdvancedGroups(),
            queryGroupsMatch: AdvancedRulesMatch,
            limit: PageSize,
            offset: pageIndex * PageSize,
            includeTotal: includeTotal,
            snapshot: snapshot,
            ct: ct);

    private Task<CatalogResponse> FetchWindowAsync(int startIndex, int itemCount, bool includeTotal, string? snapshot = null, CancellationToken ct = default)
        => _catalogApi.GetCatalogAsync(
            libraryId: Library!.Id,
            sort: SelectedSort,
            order: SelectedOrder,
            genre: UseAdvancedRules ? null : SelectedGenre,
            studio: UseAdvancedRules ? null : SelectedStudio,
            contentRating: UseAdvancedRules ? null : SelectedContentRating,
            country: UseAdvancedRules ? null : SelectedCountry,
            resolution: UseAdvancedRules ? null : SelectedResolution,
            audioLanguage: UseAdvancedRules ? null : SelectedAudioLanguage,
            yearMin: UseAdvancedRules ? null : SelectedYearMin,
            yearMax: UseAdvancedRules ? null : SelectedYearMax,
            type: UseAdvancedRules ? null : SelectedType,
            extraRules: BuildExtraRules(),
            extraRulesMatch: AdvancedRulesMatch,
            queryGroups: BuildAdvancedGroups(),
            queryGroupsMatch: AdvancedRulesMatch,
            limit: itemCount,
            offset: startIndex,
            includeTotal: includeTotal,
            snapshot: snapshot,
            ct: ct);

    private IReadOnlyList<QueryRule> BuildExtraRules()
    {
        if (UseAdvancedRules) return [];

        return BuildGuidedExtraRules();
    }

    private IReadOnlyList<QueryRule> BuildGuidedExtraRules()
    {

        var rules = new List<QueryRule>();
        void Add(string field, string op, object? value)
        {
            if (value is string text && string.IsNullOrWhiteSpace(text)) return;
            if (value != null) rules.Add(new QueryRule { Field = field, Op = op, Value = value });
        }

        if (double.TryParse(SelectedMinimumRating, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var minimumRating))
            Add("rating_imdb", "gte", minimumRating);
        Add("original_language", "is", SelectedOriginalLanguage);
        Add("actor", "is", SelectedActor);
        Add("director", "is", SelectedDirector);
        Add("writer", "is", SelectedWriter);
        Add("producer", "is", SelectedProducer);
        Add("author", "is", SelectedAuthor);
        Add("narrator", "is", SelectedNarrator);
        Add("series", "is", SelectedSeries);
        Add("network", "is", SelectedNetwork);
        Add("status", "is", SelectedMatchStatus);

        switch (SelectedWatchStatus)
        {
            case "watched": Add("watched", "is", true); break;
            case "in_progress": Add("in_progress", "is", true); break;
            case "unwatched":
                Add("watched", "is", false);
                Add("in_progress", "is", false);
                break;
        }

        Add("added_at", "in_last", SelectedAddedInLast);
        Add("release_date", "in_last", SelectedReleasedInLast);
        if (SelectedFourK) Add("resolution", "is", "2160p");
        if (SelectedHdr) Add("hdr", "is", true);
        if (SelectedDolbyVision) Add("dolby_vision", "is", true);
        return rules;
    }

    public void SeedAdvancedRulesFromGuided()
    {
        AdvancedGroups.Clear();
        var group = new EditableQueryGroup();
        AdvancedGroups.Add(group);
        void Add(string field, string op, object? value)
        {
            if (value is string text && string.IsNullOrWhiteSpace(text)) return;
            if (value != null) group.Rules.Add(new QueryRule { Field = field, Op = op, Value = value });
        }

        Add("genre", "is", SelectedGenre);
        Add("type", "is", SelectedType);
        Add("content_rating", "is", SelectedContentRating);
        Add("studio", "is", SelectedStudio);
        Add("country", "is", SelectedCountry);
        Add("resolution", "is", SelectedResolution);
        Add("audio_language", "is", SelectedAudioLanguage);
        if (int.TryParse(SelectedYearMin, out var yearMin)) Add("year", "gte", yearMin);
        if (int.TryParse(SelectedYearMax, out var yearMax)) Add("year", "lte", yearMax);
        foreach (var rule in BuildGuidedExtraRules())
            Add(rule.Field, rule.Op, rule.Value);
        if (group.Rules.Count == 0)
            group.Rules.Add(new QueryRule { Field = "genre", Op = "contains", Value = "" });
        AdvancedRulesMatch = "all";
    }

    private IReadOnlyList<QueryGroup>? BuildAdvancedGroups()
    {
        if (!UseAdvancedRules) return null;

        return AdvancedGroups
            .Select(group => new QueryGroup
            {
                Match = group.Match == "any" ? "any" : "all",
                Rules = group.Rules
                    .Where(rule => !string.IsNullOrWhiteSpace(rule.Field) &&
                        !string.IsNullOrWhiteSpace(rule.Op) &&
                        HasAdvancedRuleValue(rule.Value))
                    .Select(rule => new QueryRule { Field = rule.Field, Op = rule.Op, Value = rule.Value })
                    .ToList()
            })
            .Where(group => group.Rules.Count > 0)
            .ToList();
    }

    private static bool HasAdvancedRuleValue(object? value)
    {
        if (value is null) return false;
        if (value is string text) return !string.IsNullOrWhiteSpace(text);
        if (value is System.Collections.IEnumerable values)
            return values.Cast<object?>().All(HasAdvancedRuleValue);
        return true;
    }

    public static EditableQueryGroup CreateEmptyAdvancedGroup()
    {
        var group = new EditableQueryGroup();
        group.Rules.Add(new QueryRule { Field = "genre", Op = "contains", Value = "" });
        return group;
    }

    private void ApplyWindowResponse(int startIndex, CatalogResponse response, bool includeTotal)
    {
        if (!string.IsNullOrWhiteSpace(response.Snapshot))
            _snapshot = response.Snapshot;

        _windowItems = response.Items;
        WindowStartIndex = response.Items.Count == 0 ? -1 : startIndex;
        HasMore = response.HasMore;

        var hasTrustworthyExactTotal = response.TotalExact &&
            (includeTotal || response.Total > 0 || (!_hasExactTotal && response.Items.Count == 0 && !response.HasMore));
        if (hasTrustworthyExactTotal)
        {
            _hasExactTotal = true;
            _estimatedTotalItems = response.Total;
            DisplayTotalCount = response.Total;
            TotalCount = response.Total;
            Items.SetCount(response.Total);
            return;
        }

        if (_hasExactTotal && _estimatedTotalItems > 0)
        {
            TotalCount = _estimatedTotalItems;
            Items.SetCount(_estimatedTotalItems);
            return;
        }

        var loadedEnd = startIndex + response.Items.Count;
        if (response.Total > 0)
            _estimatedTotalItems = Math.Max(response.Total, loadedEnd);
        else if (!response.HasMore)
            _estimatedTotalItems = loadedEnd;
        else
            _estimatedTotalItems = Math.Max(_estimatedTotalItems, loadedEnd + PageSize);

        TotalCount = _estimatedTotalItems;
        Items.SetCount(_estimatedTotalItems);
    }

    private void RecomputeVirtualTotal()
    {
        if (_pageResponses.Count == 0)
        {
            TotalCount = 0;
            Items.SetCount(0);
            HasMore = false;
            return;
        }

        if (_hasExactTotal && _estimatedTotalItems > 0)
        {
            TotalCount = _estimatedTotalItems;
            Items.SetCount(_estimatedTotalItems);
            HasMore = _pageResponses.Values.Any(r => r.HasMore);
            return;
        }

        var exact = _pageResponses.Values.FirstOrDefault(r => r.TotalExact);
        if (exact != null)
        {
            _hasExactTotal = true;
            _estimatedTotalItems = exact.Total;
            DisplayTotalCount = exact.Total;
            TotalCount = exact.Total;
            Items.SetCount(exact.Total);
            HasMore = exact.HasMore || _pageResponses.Keys.Any(page => !Items.IsPageLoaded(page));
            return;
        }

        int maxLoadedEnd = 0;
        int highestPageIndex = -1;
        bool highestPageHasMore = false;

        foreach (var (pageIndex, response) in _pageResponses)
        {
            maxLoadedEnd = Math.Max(maxLoadedEnd, pageIndex * PageSize + response.Items.Count);
            if (pageIndex >= highestPageIndex)
            {
                highestPageIndex = pageIndex;
                highestPageHasMore = response.HasMore;
            }
        }

        if (maxLoadedEnd == 0)
        {
            TotalCount = 0;
            Items.SetCount(0);
            HasMore = false;
            return;
        }

        if (!highestPageHasMore)
        {
            _estimatedTotalItems = maxLoadedEnd;
        }
        else
        {
            const int EstimatePages = 5;
            var estimateStep = PageSize * EstimatePages;
            var seededEstimate = _estimatedTotalItems > 0
                ? _estimatedTotalItems
                : maxLoadedEnd + estimateStep;
            var needsMoreRunway = _lastVisibleEndIndex >= seededEstimate - PageSize * 2;
            _estimatedTotalItems = Math.Max(
                needsMoreRunway ? seededEstimate + estimateStep : seededEstimate,
                maxLoadedEnd);
        }

        TotalCount = _estimatedTotalItems;
        Items.SetCount(_estimatedTotalItems);
        HasMore = highestPageHasMore;
    }

    private async Task LoadFiltersAsync(int version, CancellationToken ct)
    {
        if (Library == null) return;

        try
        {
            var filters = await _catalogApi.GetFiltersAsync(Library.Id, ct);
            if (version != _queryVersion) return;

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

              OriginalLanguages.Clear();
              OriginalLanguages.AddRange(filters.OriginalLanguages.Prepend(""));

              Networks.Clear();
              Networks.AddRange(filters.Networks.Prepend(""));
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Filters are optional, don't block UI
        }
    }
}

public sealed class LibraryTabSection
{
    public string Title { get; init; } = "";
    public string Kind { get; init; } = "regular";
    public int SortOrder { get; init; }
    public bool IsUngrouped { get; init; }
    public ObservableCollection<LibraryTabCollectionDisplay> Collections { get; } = [];

    public bool HasTitle => !IsUngrouped && !string.IsNullOrWhiteSpace(Title);
}

public sealed class LibraryTabCollectionDisplay
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? PosterUrl { get; init; }
    public string? PosterThumbhash { get; init; }
    public int ItemCount { get; init; }
    public bool Featured { get; init; }
    public bool IsUserCollection { get; init; }
    public string TypeLabel { get; init; } = "COLLECTION";
}
