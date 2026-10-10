using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public sealed class EditableQueryGroup
{
    public string Match { get; set; } = "all";
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalProperties { get; set; }
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
    private ApiRequestContext? _catalogContext;
    private int _estimatedTotalItems;
    private bool _hasExactTotal;
    private int _lastVisibleEndIndex = PageSize - 1;
    private int _queryVersion;
    private int _windowLoadVersion;
    private CancellationTokenSource _catalogQueryCts = new();
    private CancellationTokenSource? _windowLoadCts;
    private IReadOnlyList<MediaItem> _windowItems = [];
    private readonly LinkedList<(int Start, CatalogResponse Response, bool IncludesTotal)> _recentWindows = new();
    private sealed record CompletedBoundary(int Start, int QueryVersion, ApiRequestContext Context, string QueryKey, string Snapshot, string NextCursor);
    private readonly Dictionary<int, CompletedBoundary> _completedBoundaries = [];
    private Task? _pendingWindowTask;
    private int _pendingWindowStart = -1;
    private int _pendingWindowEnd = -1;
    private int _viewportRequestVersion;
    private Task? _filtersTask;
    private int _filtersQueryVersion = -1;
    private int? _filtersLibraryId;
    private ApiRequestContext? _filtersContext;
    private int _filtersGeneration = -1;
    private int? _loadedFiltersLibraryId;
    private ApiRequestContext? _loadedFiltersContext;
    private int _loadedFiltersGeneration = -1;
    private int _exactCountQueryVersion = -1;

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

    public IReadOnlyList<string> SelectedGenres { get; set; } = [];

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
    public IReadOnlyList<string> SelectedOriginalLanguages { get; set; } = [];
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
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure("catalog-initial-load");
        if (Library == null) return;

        StartNewCatalogQuery();
        Items.Clear();
        // Cards load immediately; optional facets are requested when the
        // filter sheet is opened, matching the WebUI's browsing path.
        await LoadWindowAsync(0, PageSize, force: true);
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

        var sortChanged = !string.Equals(SelectedSort, "title", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(SelectedOrder, "asc", StringComparison.OrdinalIgnoreCase);
        var knownTotal = TotalCount;
        var knownTotalIsExact = _hasExactTotal;
        SelectedSort = "title";
        SelectedOrder = "asc";

        if (sortChanged)
        {
            StartNewCatalogQuery();
            // Sorting does not change membership, so retain the known total while
            // clearing every page/window/snapshot tied to the old sort order.
            TotalCount = knownTotal;
            DisplayTotalCount = knownTotal;
            _estimatedTotalItems = knownTotal;
            _hasExactTotal = knownTotalIsExact;
            HasMore = knownTotal > PageSize;
            Items.Clear();
        }

        if (letter == "#")
        {
            if (!sortChanged)
            {
                StartNewCatalogQuery();
                Items.Clear();
            }
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
        var relativeIndex = absoluteIndex - WindowStartIndex;
        if (WindowStartIndex >= 0 && relativeIndex >= 0 && relativeIndex < _windowItems.Count)
            return _windowItems[relativeIndex];

        foreach (var cached in _recentWindows)
        {
            relativeIndex = absoluteIndex - cached.Start;
            if (relativeIndex >= 0 && relativeIndex < cached.Response.Items.Count)
                return cached.Response.Items[relativeIndex];
        }
        return null;
    }

    /// <summary>Fills a bounded neighborhood without replacing or notifying the visible window.</summary>
    public async Task<IReadOnlyList<MediaItem>> PrefetchAroundAsync(
        int startIndex, int endIndex, bool forward, CancellationToken ct = default)
    {
        if (Library == null || TotalCount <= 0 || startIndex < 0 || endIndex < startIndex
            || _catalogContext != _catalogApi.CaptureContext())
            return [];

        var screen = Math.Clamp(endIndex - startIndex + 1, 1, 100);
        var first = Math.Max(0, startIndex - screen * (forward ? 1 : 3));
        var last = Math.Min(TotalCount - 1, endIndex + screen * (forward ? 3 : 1));
        var version = _queryVersion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _catalogQueryCts.Token);
        var token = linked.Token;
        // Work nearest-first in the direction of travel, then behind. Never
        // launch one request per wheel tick or fan out across the whole library.
        var indices = forward
            ? Enumerable.Range(startIndex, Math.Max(0, last - startIndex + 1))
                .Concat(Enumerable.Range(first, startIndex - first).Reverse())
            : Enumerable.Range(first, Math.Max(0, endIndex - first + 1)).Reverse()
                .Concat(Enumerable.Range(endIndex + 1, Math.Max(0, last - endIndex)));
        try
        {
            foreach (var index in indices)
            {
                token.ThrowIfCancellationRequested();
                if (GetWindowItem(index) != null) continue;
                var fetchStart = first + (index - first) / 100 * 100;
                var fetchEnd = Math.Min(last, fetchStart + 99);
                var response = await FetchWindowPageAsync(fetchStart, fetchEnd - fetchStart + 1,
                    false, _snapshot, token);
                if (token.IsCancellationRequested || version != _queryVersion) return [];
                RememberWindow(fetchStart, response, false);
                if (response.Items.Count == 0) break;
            }
            return indices.Select(GetWindowItem).OfType<MediaItem>().ToArray();
        }
        catch (OperationCanceledException) { return []; }
        // Speculative loading must not replace the visible catalog with an error.
        catch { return []; }
    }

    private void RememberWindow(int startIndex, CatalogResponse response, bool includeTotal)
    {
        // A large visible window is published one server page at a time.
        // Replace its earlier partial entry instead of counting it twice.
        for (var node = _recentWindows.First; node != null;)
        {
            var next = node.Next;
            if (node.Value.Start == startIndex && ReferenceEquals(node.Value.Response, response))
                _recentWindows.Remove(node);
            node = next;
        }
        _recentWindows.AddFirst((startIndex, response, includeTotal));
        while (_recentWindows.Count > 8 || _recentWindows.Sum(window => window.Response.Items.Count) > 1200)
            _recentWindows.RemoveLast();
    }

    private bool HasCachedRange(int startIndex, int endIndex)
    {
        for (var index = startIndex; index <= endIndex; index++)
            if (GetWindowItem(index) == null) return false;
        return true;
    }

    /// <summary>
    /// Mutates every cached representation of an item without resetting the
    /// virtual list. A browse item can exist in both the active window and a
    /// previously loaded sparse page, so references are de-duplicated first.
    /// </summary>
    public int UpdateCachedItems(string contentId, Func<MediaItem, bool> update)
    {
        if (string.IsNullOrWhiteSpace(contentId)) return 0;

        var matches = _windowItems
            .Concat(_recentWindows.SelectMany(window => window.Response.Items))
            .Concat(_pageResponses.Values.SelectMany(response => response.Items))
            .Where(item => string.Equals(item.ContentId, contentId, StringComparison.OrdinalIgnoreCase))
            .Distinct<MediaItem>(ReferenceEqualityComparer.Instance)
            .ToList();

        var changed = 0;
        foreach (var item in matches)
        {
            if (update(item)) changed++;
        }
        return changed;
    }

    public Task LoadWindowAsync(int startIndex, int itemCount, bool force = false)
    {
        if (Library == null || itemCount <= 0) return Task.CompletedTask;
        var context = _catalogApi.CaptureContext();
        if (_catalogContext != null && _catalogContext != context) StartNewCatalogQuery();
        _catalogContext = context;
        var viewportVersion = ++_viewportRequestVersion;
        EnsureExactCountRequested();
        startIndex = TotalCount > 0 ? Math.Clamp(startIndex, 0, TotalCount - 1) : 0;
        itemCount = TotalCount > 0 ? Math.Min(itemCount, TotalCount - startIndex) : itemCount;
        var endIndex = startIndex + itemCount - 1;
        if (!force && HasWindow(startIndex, endIndex))
        {
            CancelWindowLoad();
            IsLoading = false;
            return Task.CompletedTask;
        }

        if (!force && _pendingWindowTask is { IsCompleted: false } &&
            startIndex >= _pendingWindowStart && endIndex <= _pendingWindowEnd)
            return _pendingWindowTask;

        // A dense viewport spans multiple API pages. Keep an overlapping
        // fetch alive as the viewport moves, then satisfy only the latest
        // outstanding range instead of restarting on every new row.
        if (!force && _pendingWindowTask is { IsCompleted: false } pending &&
            startIndex <= _pendingWindowEnd && endIndex >= _pendingWindowStart)
            return CompleteOverlappingRangeAsync(pending, startIndex, itemCount, viewportVersion, _queryVersion);

        if (force)
        {
            _recentWindows.Clear();
            _completedBoundaries.Clear();
        }
        if (!force && HasCachedRange(startIndex, endIndex))
        {
            CancelWindowLoad();
            IsLoading = false;
            return Task.CompletedTask;
        }
        for (var node = _recentWindows.First; node != null; node = node.Next)
        {
            var cached = node.Value;
            if (startIndex < cached.Start || endIndex >= cached.Start + cached.Response.Items.Count) continue;
            CancelWindowLoad();
            _recentWindows.Remove(node);
            _recentWindows.AddFirst(cached);
            ApplyWindowResponse(cached.Start, cached.Response, cached.IncludesTotal);
            IsLoading = false;
            ErrorMessage = null;
            WindowLoaded?.Invoke();
            return Task.CompletedTask;
        }

        // Keep a small buffer around the viewport without requesting the whole
        // library. Stable boundaries allow nearby scroll events to share work.
        if (TotalCount > 0 && itemCount <= 100)
        {
            var alignedStart = startIndex / PageSize * PageSize;
            if (endIndex < alignedStart + 100) startIndex = alignedStart;
            itemCount = Math.Min(100, TotalCount - startIndex);
        }
        if (!force)
        {
            // Keep already displayed objects/artwork when only an edge of
            // the viewport is missing. Apply the buffer before trimming so
            // alignment never expands a request back over cached cards.
            while (itemCount > 0 && GetWindowItem(startIndex) != null)
            {
                startIndex++;
                itemCount--;
            }
            while (itemCount > 0 && GetWindowItem(startIndex + itemCount - 1) != null)
                itemCount--;
            if (itemCount == 0) return Task.CompletedTask;
        }
        _pendingWindowStart = startIndex;
        _pendingWindowEnd = startIndex + itemCount - 1;
        return _pendingWindowTask = LoadWindowCoreAsync(startIndex, itemCount, force);
    }

    private async Task CompleteOverlappingRangeAsync(Task pending, int startIndex, int itemCount,
        int viewportVersion, int queryVersion)
    {
        await pending;
        if (viewportVersion != _viewportRequestVersion || queryVersion != _queryVersion)
            return;
        await LoadWindowAsync(startIndex, itemCount);
    }

    private async Task LoadWindowCoreAsync(int startIndex, int itemCount, bool force)
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
            const bool includeTotal = false;
            await FetchWindowAsync(startIndex, itemCount, includeTotal, response =>
            {
                if (ct.IsCancellationRequested || queryVersion != _queryVersion || windowVersion != _windowLoadVersion)
                    return;

                ApplyWindowResponse(startIndex, response, includeTotal, preserveAdjacent: !force);
                RememberWindow(startIndex, response, includeTotal);
                WindowLoaded?.Invoke();
                EnsureExactCountRequested();
            }, _snapshot, ct);
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
        _catalogContext = _catalogApi.CaptureContext();
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _completedBoundaries.Clear();
        _loadingPages.Clear();
        _pageResponses.Clear();
        _recentWindows.Clear();
        _snapshot = null;
        _estimatedTotalItems = 0;
        _hasExactTotal = false;
        _lastVisibleEndIndex = PageSize - 1;
        TotalCount = 0;
        DisplayTotalCount = 0;
        HasMore = false;
        ErrorMessage = null;
        ClearActiveWindow(notify: true);
        // Sorting or jumping to a letter cancels the query token. Retry any
        // filter load that was already requested but had not finished yet.
        if (_filtersTask != null && _loadedFiltersLibraryId != Library?.Id)
            _ = EnsureFiltersLoadedAsync();
        return _queryVersion;
    }

    public void CancelCatalogLoads()
    {
        _loadedFiltersLibraryId = null;
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _completedBoundaries.Clear();
        _loadingPages.Clear();
        _pageResponses.Clear();
        _recentWindows.Clear();
        ClearActiveWindow(notify: true);
        IsLoading = false;
    }

    /// <summary>
    /// Stops requests owned by a page that is leaving the frame while retaining
    /// the populated catalog window. A cached LibraryPage can therefore return
    /// immediately without flashing an empty grid or re-fetching the same items.
    /// </summary>
    public void SuspendCatalogLoads()
    {
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _completedBoundaries.Clear();
        _loadingPages.Clear();
        IsLoading = false;
    }

    private void CancelCurrentCatalogQuery()
    {
        try { _catalogQueryCts.Cancel(); } catch { }
        _catalogQueryCts.Dispose();
    }

    private void CancelWindowLoad()
    {
        _pendingWindowTask = null;
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
            type: SelectedType,
            extraRules: BuildExtraRules(),
            extraRulesMatch: AdvancedRulesMatch,
            queryGroups: BuildAdvancedGroups(),
            queryGroupsMatch: AdvancedRulesMatch,
            limit: PageSize,
            offset: pageIndex * PageSize,
            includeTotal: includeTotal,
            snapshot: snapshot,
            ct: ct);

    private async Task FetchWindowAsync(int startIndex, int itemCount, bool includeTotal,
        Action<CatalogResponse> publishPage, string? snapshot = null, CancellationToken ct = default)
    {
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure(
            includeTotal ? "catalog-request-with-total" : "catalog-request-window");
        var response = await FetchWindowPageAsync(startIndex, Math.Min(100, itemCount), includeTotal, snapshot, ct);
        ct.ThrowIfCancellationRequested();
        publishPage(response);
        while (response.Items.Count < itemCount && response.HasMore)
        {
            ct.ThrowIfCancellationRequested();
            var next = await FetchWindowPageAsync(startIndex + response.Items.Count,
                Math.Min(100, itemCount - response.Items.Count), false, response.Snapshot ?? snapshot, ct);
            ct.ThrowIfCancellationRequested();
            response.HasMore = next.HasMore;
            if (next.Items.Count == 0) break;
            response.Items.AddRange(next.Items);
            publishPage(response);
        }
    }

    private void EnsureExactCountRequested()
    {
        if (_hasExactTotal || WindowStartIndex < 0 || Library == null || _exactCountQueryVersion == _queryVersion)
            return;
        _exactCountQueryVersion = _queryVersion;
        _ = LoadExactCountAsync(_queryVersion, _snapshot, _catalogQueryCts.Token);
    }

    private async Task LoadExactCountAsync(int version, string? snapshot, CancellationToken ct)
    {
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure("catalog-exact-count");
        try
        {
            var result = await FetchWindowPageAsync(0, 1, true, snapshot, ct);
            if (ct.IsCancellationRequested || version != _queryVersion || !result.TotalExact) return;
            _hasExactTotal = true;
            _estimatedTotalItems = Math.Max(0, result.Total);
            DisplayTotalCount = _estimatedTotalItems;
            TotalCount = _estimatedTotalItems;
            Items.SetCount(_estimatedTotalItems);
            WindowLoaded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Keep the estimated extent and usable cards if the optional
            // count fails; a later navigation/query can retry it.
        }
    }

    private async Task<CatalogResponse> FetchWindowPageAsync(int startIndex, int itemCount, bool includeTotal, string? snapshot, CancellationToken ct)
    {
        var context = _catalogApi.CaptureContext();
        var version = _queryVersion;
        var queryKey = System.Text.Json.JsonSerializer.Serialize(new
        {
            library = Library!.Id, SelectedSort, SelectedOrder, UseAdvancedRules, SelectedGenre,
            SelectedStudio, SelectedContentRating, SelectedCountry, SelectedResolution, SelectedAudioLanguage,
            SelectedYearMin, SelectedYearMax, SelectedType, rules = BuildExtraRules(),
            groups = BuildAdvancedGroups(), AdvancedRulesMatch,
        });
        var nextCursor = _completedBoundaries.TryGetValue(startIndex, out var boundary)
            && boundary.QueryVersion == version && boundary.Context == context && boundary.QueryKey == queryKey
            && boundary.Snapshot == snapshot ? boundary.NextCursor : null;
        // A refetch retires the boundary supplied by that page immediately;
        // failed or canceled replacements cannot leave an older cursor usable.
        foreach (var end in _completedBoundaries.Where(pair => pair.Value.Start == startIndex).Select(pair => pair.Key).ToArray())
            _completedBoundaries.Remove(end);
        var response = await _catalogApi.GetCatalogAsync(
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
            type: SelectedType,
            extraRules: BuildExtraRules(),
            extraRulesMatch: AdvancedRulesMatch,
            queryGroups: BuildAdvancedGroups(),
            queryGroupsMatch: AdvancedRulesMatch,
            limit: itemCount,
            offset: startIndex,
            includeTotal: includeTotal,
            snapshot: snapshot,
            ct: ct,
            nextCursor: nextCursor);
        ct.ThrowIfCancellationRequested();
        if (version != _queryVersion || context != _catalogApi.CaptureContext())
            throw new OperationCanceledException("Catalog continuation authority changed.", ct);
        if (response.Items.Count == itemCount && response.HasMore
            && !string.IsNullOrWhiteSpace(response.Snapshot) && !string.IsNullOrWhiteSpace(response.Page?.NextCursor))
        {
            _completedBoundaries[startIndex + itemCount] = new(startIndex, version, context, queryKey, response.Snapshot, response.Page.NextCursor);
            while (_completedBoundaries.Count > 16) _completedBoundaries.Remove(_completedBoundaries.Keys.First());
        }
        return response;
    }

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
        foreach (var genre in SelectedGenres)
            Add("genre", "is", genre);
        if (SelectedOriginalLanguages.Count <= 1)
            Add("original_language", "is", SelectedOriginalLanguages.FirstOrDefault() ?? SelectedOriginalLanguage);
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

        if (SelectedGenres.Count == 0) Add("genre", "is", SelectedGenre);
        Add("content_rating", "is", SelectedContentRating);
        Add("studio", "is", SelectedStudio);
        Add("country", "is", SelectedCountry);
        Add("resolution", "is", SelectedResolution);
        Add("audio_language", "is", SelectedAudioLanguage);
        if (int.TryParse(SelectedYearMin, out var yearMin)) Add("year", "gte", yearMin);
        if (int.TryParse(SelectedYearMax, out var yearMax)) Add("year", "lte", yearMax);
        foreach (var rule in BuildGuidedExtraRules())
            Add(rule.Field, rule.Op, rule.Value);
        if (group.Rules.Count == 0) AdvancedGroups.Remove(group);
        if (SelectedOriginalLanguages.Count > 1)
        {
            var languages = new EditableQueryGroup { Match = "any" };
            foreach (var language in SelectedOriginalLanguages)
                languages.Rules.Add(new() { Field = "original_language", Op = "is", Value = language });
            AdvancedGroups.Add(languages);
        }
        AdvancedRulesMatch = "all";
    }

    private IReadOnlyList<QueryGroup>? BuildAdvancedGroups()
    {
        if (!UseAdvancedRules)
        {
            if (SelectedOriginalLanguages.Count <= 1) return null;
            return
            [
                new QueryGroup
                {
                    Match = "any",
                    Rules = SelectedOriginalLanguages
                        .Select(language => new QueryRule { Field = "original_language", Op = "is", Value = language })
                        .ToList(),
                },
            ];
        }

        return AdvancedGroups
            .Select(group => new QueryGroup
            {
                Match = group.Match == "any" ? "any" : "all",
                AdditionalProperties = group.AdditionalProperties,
                Rules = group.Rules
                    .Where(rule => !string.IsNullOrWhiteSpace(rule.Field) &&
                        !string.IsNullOrWhiteSpace(rule.Op) &&
                        (rule.Value is not string text || !string.IsNullOrWhiteSpace(text)))
                    .ToList()
            })
            .Where(group => group.Rules.Count > 0)
            .ToList();
    }

    public static EditableQueryGroup CreateEmptyAdvancedGroup()
    {
        var group = new EditableQueryGroup();
        group.Rules.Add(new QueryRule { Field = "genre", Op = "is", Value = "" });
        return group;
    }

    private void ApplyWindowResponse(int startIndex, CatalogResponse response, bool includeTotal, bool preserveAdjacent = false)
    {
        if (!string.IsNullOrWhiteSpace(response.Snapshot))
            _snapshot = response.Snapshot;

        if (preserveAdjacent && response.Items.Count > 0 && WindowStartIndex >= 0 &&
            startIndex <= WindowEndIndex + 1 && startIndex + response.Items.Count >= WindowStartIndex)
        {
            // Small edge fetches must not fragment the active viewport across
            // more cache entries than the eviction limit. Keep a bounded,
            // contiguous active window and retain the existing item objects.
            var first = Math.Min(WindowStartIndex, startIndex);
            var last = Math.Max(WindowEndIndex, startIndex + response.Items.Count - 1);
            if (last - first + 1 > 1200)
            {
                if (startIndex < WindowStartIndex) last = first + 1199;
                else first = last - 1199;
            }
            var merged = new MediaItem[last - first + 1];
            for (var i = first; i <= last; i++)
                merged[i - first] = i >= startIndex && i < startIndex + response.Items.Count
                    ? response.Items[i - startIndex]
                    : _windowItems[i - WindowStartIndex];
            _windowItems = merged;
            WindowStartIndex = first;
        }
        else
        {
            _windowItems = response.Items;
            WindowStartIndex = response.Items.Count == 0 ? -1 : startIndex;
        }
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
            _estimatedTotalItems = Math.Max(_estimatedTotalItems, loadedEnd + PageSize * 5);

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

    public Task EnsureFiltersLoadedAsync()
    {
        if (Library == null || (_loadedFiltersLibraryId == Library.Id
            && _loadedFiltersContext == _catalogApi.CaptureContext()
            && _loadedFiltersGeneration == _catalogApi.FilterCacheGeneration))
            return Task.CompletedTask;
        if (_filtersTask is { IsCompleted: false } && _filtersQueryVersion == _queryVersion
            && _filtersLibraryId == Library.Id && _filtersContext == _catalogApi.CaptureContext()
            && _filtersGeneration == _catalogApi.FilterCacheGeneration)
            return _filtersTask;

        _filtersQueryVersion = _queryVersion;
        _filtersLibraryId = Library.Id;
        _filtersContext = _catalogApi.CaptureContext();
        _filtersGeneration = _catalogApi.FilterCacheGeneration;
        return _filtersTask = LoadFiltersAsync(Library.Id, _queryVersion, _catalogQueryCts.Token);
    }

    private async Task LoadFiltersAsync(int libraryId, int version, CancellationToken ct)
    {
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure("catalog-filters");
        var context = _catalogApi.CaptureContext();
        var generation = _catalogApi.FilterCacheGeneration;
        try
        {
            var filters = await _catalogApi.GetFiltersAsync(libraryId, ct);
            if (ct.IsCancellationRequested || version != _queryVersion || Library?.Id != libraryId
                || context != _catalogApi.CaptureContext() || generation != _catalogApi.FilterCacheGeneration) return;

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
            _loadedFiltersLibraryId = libraryId;
            _loadedFiltersContext = context;
            _loadedFiltersGeneration = generation;
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
