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
    // Both window and continuation cursors are signed over the wire limit.
    // Viewport size, count-only reads and prefetch must not change that scope.
    private const int CatalogWindowPageSize = 100;
    private const int MaxInFlightCatalogPages = 3;
    private readonly CatalogApi _catalogApi;
    private readonly HashSet<int> _loadingPages = [];
    private readonly Dictionary<int, CatalogResponse> _pageResponses = [];
    private string? _catalogLibraryScope;
    private string? _snapshot;
    private ApiRequestContext? _catalogContext;
    private int _estimatedTotalItems;
    private int _observedCatalogEnd;
    private int? _catalogEndTotal;
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
    private long _exactCountLoadVersion;

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
        var cards = LoadWindowAsync(0, PageSize, force: true);
        _ = EnsureLetterIndexAsync();
        await cards;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || Library == null || TotalCount <= 0) return;
        EnsureCatalogScope();
        // Legacy sparse-page loading must not start catch-up ahead of the viewport.
        if (_catalogRefreshPending) return;

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
        var cards = LoadWindowAsync(0, PageSize, force: true);
        _ = EnsureLetterIndexAsync();
        await cards;
    }

    public async Task EnsureRangeLoadedAsync(int startIndex, int endIndex)
    {
        if (Library == null || startIndex > endIndex) return;
        if (TotalCount <= 0)
        {
            // Retiring a jump after a context reset can leave no extent yet.
            // Bootstrap ordinary browsing, but never re-read a known empty result
            // or bypass the scoped Retry that still owns a failed reset load.
            if (!_hasExactTotal && _catalogEndTotal is null && !IsLetterJumpViewportBlocked)
                await LoadWindowAsync(0, PageSize);
            return;
        }

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
        if (_hasExactTotal && absoluteIndex >= TotalCount) return null;
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
        if (Library == null || TotalCount <= 0 || startIndex < 0 || endIndex < startIndex) return [];
        // A changed library scope or access starts a new, empty query: nothing to prefetch.
        EnsureCatalogScope();
        if (TotalCount <= 0) return [];
        if (_catalogRefreshPending && await RefreshInPlaceAsync(startIndex, endIndex - startIndex + 1, ct) != CatalogRefreshResult.Completed)
            return [];
        // A failed catch-up keeps only the extent. Let a foreground window
        // establish its fresh snapshot before speculative reads populate caches.
        if (ct.IsCancellationRequested || WindowStartIndex < 0) return [];

        var (first, indices) = Neighborhood(startIndex, endIndex, forward);
        var version = _queryVersion;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _catalogQueryCts.Token);
        var token = linked.Token;
        try
        {
            foreach (var index in indices)
            {
                token.ThrowIfCancellationRequested();
                if (GetWindowItem(index) != null) continue;
                var fetchStart = first + (index - first) / 100 * 100;
                var response = await FetchWindowPageAsync(fetchStart,
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

    /// <summary>
    /// The current query's items already held around a range but outside it, in
    /// <see cref="PrefetchAroundAsync"/>'s order, without any request. Artwork read-ahead works from
    /// them; the range's own cards load theirs.
    /// </summary>
    public IReadOnlyList<MediaItem> CachedNeighborhood(int startIndex, int endIndex, bool forward)
    {
        if (Library == null || TotalCount <= 0 || startIndex < 0 || endIndex < startIndex) return [];
        return Neighborhood(startIndex, endIndex, forward).Indices
            .Where(index => index < startIndex || index > endIndex)
            .Select(GetWindowItem).OfType<MediaItem>().ToArray();
    }

    /// <summary>Changes with every new query, so read-ahead queued for an earlier one can be skipped.</summary>
    internal int QueryVersion => _queryVersion;

    // A bounded neighborhood: one screen behind and three ahead in the direction of travel, worked
    // nearest-first, then behind. Never one request per wheel tick or a fan-out across the library.
    private (int First, IEnumerable<int> Indices) Neighborhood(int startIndex, int endIndex, bool forward)
    {
        var screen = Math.Clamp(endIndex - startIndex + 1, 1, 100);
        var first = Math.Max(0, startIndex - screen * (forward ? 1 : 3));
        var last = Math.Min(TotalCount - 1, endIndex + screen * (forward ? 3 : 1));
        var indices = forward
            ? Enumerable.Range(startIndex, Math.Max(0, last - startIndex + 1))
                .Concat(Enumerable.Range(first, startIndex - first).Reverse())
            : Enumerable.Range(first, Math.Max(0, endIndex - first + 1)).Reverse()
                .Concat(Enumerable.Range(endIndex + 1, Math.Max(0, last - endIndex)));
        return (first, indices);
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
            .Concat(_confirmedRefreshViewportVersion == _queryVersion && _confirmedRefreshViewport is { } viewport
                ? viewport.Response.Items : Enumerable.Empty<MediaItem>())
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

    private static string LibraryCatalogScope(Library? library) => library is null ? "none"
        : $"{library.Id}:{library.Type}:{library.Enabled}";

    partial void OnLibraryChanged(Library? oldValue, Library? newValue)
    {
        if (oldValue is not null && LibraryCatalogScope(oldValue) != LibraryCatalogScope(newValue))
        {
            var extent = TotalCount;
            StartNewCatalogQuery();
            // Keep a provisional viewport extent while replacing every cached card/cursor.
            // The new scope's catalog count will establish the authoritative total.
            TotalCount = extent; Items.Reset(extent);
        }
    }

    partial void OnSelectedTypeChanged(string? value)
    {
        // Type is part of the catalog shape. Retire cards, cursors and
        // alphabet positions before another type's scope can reuse them.
        StartNewCatalogQuery();
        Items.Clear();
    }

    /// <summary>Starts a new query when the API context or library scope changed since the current one began.</summary>
    private void EnsureCatalogScope()
    {
        if (_catalogContext is null && _catalogLibraryScope is null)
        {
            _catalogContext = _catalogApi.CaptureContext(); _catalogLibraryScope = LibraryCatalogScope(Library);
        }
        else if (_catalogLibraryScope != LibraryCatalogScope(Library))
            StartNewCatalogQuery();
        else if (_catalogContext != _catalogApi.CaptureContext())
        {
            // Only a transport generation may keep the grid. An access-generation
            // change retires every previously authorized row and alphabet position.
            if (HasRetainableCatalog && IsSameCatalogIdentity(_catalogContext, _catalogApi.CaptureContext())) RebaseCatalogQuery();
            else StartNewCatalogQuery();
        }
    }

    public Task LoadWindowAsync(int startIndex, int itemCount, bool force = false)
        => LoadWindowForOwnerAsync(startIndex, itemCount, force, null);

    private async Task LoadWindowForOwnerAsync(int startIndex, int itemCount, bool force, LetterWindowLoad? letterLoad)
    {
        if (Library is null) return;
        EnsureCatalogScope();
        if (_catalogRefreshPending && await RefreshInPlaceAsync(startIndex, itemCount) is var refresh &&
            refresh != CatalogRefreshResult.Completed &&
            // A catch-up that handed back its gate (churn, oversized held set) under
            // this query leaves the viewport to the ordinary read, not to the next scroll.
            !(refresh == CatalogRefreshResult.Deferred && !RequiresCatalogRefresh &&
              _catalogRefreshReleasedVersion == _queryVersion))
            return;
        if (letterLoad is { IsCurrent: false }) return;
        await LoadPreparedWindowAsync(startIndex, itemCount, force, letterLoad);
    }

    private Task LoadPreparedWindowAsync(int startIndex, int itemCount, bool force = false, LetterWindowLoad? letterLoad = null)
    {
        if (Library == null || itemCount <= 0) return Task.CompletedTask;
        if (_catalogContext != null) EnsureCatalogScope();
        _catalogContext = _catalogApi.CaptureContext();
        // The still-visible viewport must not cancel an explicit letter seek.
        // The page retries its latest range once the jump has completed.
        if (!force && IsLetterJumpInProgress) return Task.CompletedTask;
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
            SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
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
        return _pendingWindowTask = LoadWindowCoreAsync(startIndex, itemCount, force, letterLoad: letterLoad);
    }

    private async Task CompleteOverlappingRangeAsync(Task pending, int startIndex, int itemCount,
        int viewportVersion, int queryVersion)
    {
        await pending;
        if (viewportVersion != _viewportRequestVersion || queryVersion != _queryVersion)
            return;
        await LoadWindowAsync(startIndex, itemCount);
    }

    private async Task LoadWindowCoreAsync(int startIndex, int itemCount, bool force, bool allowCursorRecovery = true,
        LetterWindowLoad? letterLoad = null)
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
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);

        try
        {
            const bool includeTotal = false;
            await FetchWindowAsync(startIndex, itemCount, includeTotal, response =>
            {
                if (ct.IsCancellationRequested || queryVersion != _queryVersion || windowVersion != _windowLoadVersion ||
                    letterLoad is { IsCurrent: false })
                    return;
                // A count may have landed while this speculative window was
                // in flight. Even total_exact on a skip-total window cannot
                // enlarge that count or replace the still-usable active window.
                if (_hasExactTotal && startIndex >= TotalCount)
                    return;

                ApplyWindowResponse(startIndex, response, includeTotal, preserveAdjacent: !force);
                RememberWindow(startIndex, response, includeTotal);
                if (letterLoad is not null) letterLoad.Received = true;
                WindowLoaded?.Invoke();
                EnsureExactCountRequested();
            }, _snapshot, ct);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (allowCursorRecovery && ex.StatusCode == 400 && ex.ErrorCode == "invalid_cursor"
            && !string.IsNullOrWhiteSpace(_snapshot) && queryVersion == _queryVersion && windowVersion == _windowLoadVersion
            && _catalogContext == _catalogApi.CaptureContext())
        {
            if (letterLoad is not null)
            {
                // Generic viewport recovery cannot retain a semantic letter:
                // a policy change may have moved it to a different position.
                if (letterLoad.IsCurrent) letterLoad.CursorRejected = true;
                return;
            }
            // The server can retire a signed scope after a visibility-policy or
            // signing change even when our local query/profile is unchanged.
            // Discard its cards and cursors together, then retry this viewport
            // once without a cursor. Never mix old-scope rows into the new view.
            var estimatedTotal = TotalCount;
            StartNewCatalogQuery();
            _estimatedTotalItems = estimatedTotal;
            TotalCount = estimatedTotal;
            DisplayTotalCount = estimatedTotal;
            Items.Reset(estimatedTotal);
            SiloPlayer.Core.Services.LocalLog.AppendLine("catalog_errors.txt",
                $"catalog_cursor_recovery start={startIndex} count={itemCount} query_version={_queryVersion} prior_scope_retired=true retry_without_cursor=true");
            _pendingWindowStart = startIndex;
            _pendingWindowEnd = startIndex + itemCount - 1;
            var recoveryQueryVersion = _queryVersion;
            IsLoading = false;
            await (_pendingWindowTask = LoadWindowCoreAsync(startIndex, itemCount, force: true, allowCursorRecovery: false));
            // Only restore the optional rail after fresh cards succeed. A
            // failed cursor recovery must not trigger another capability read.
            if (recoveryQueryVersion == _queryVersion && ErrorMessage is null && WindowStartIndex >= 0)
                _ = EnsureLetterIndexAsync();
        }
        catch (Exception ex)
        {
            if (queryVersion == _queryVersion && windowVersion == _windowLoadVersion)
            {
                if (letterLoad is { IsCurrent: true }) letterLoad.Failed = true;
                else if (letterLoad is null) SetLocalizedMessage(nameof(ErrorMessage), () => $"Failed to load catalog: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
            }
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
        ResetLetterIndex();
        _catalogLibraryScope = LibraryCatalogScope(Library);
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
        _catalogRefreshPending = false;
        _estimatedTotalItems = 0;
        _observedCatalogEnd = 0;
        _catalogEndTotal = null;
        _hasExactTotal = false;
        _lastVisibleEndIndex = PageSize - 1;
        TotalCount = 0;
        DisplayTotalCount = 0;
        HasMore = false;
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);
        ClearActiveWindow(notify: true);
        // Sorting or jumping to a letter cancels the query token. Retry any
        // filter load that was already requested but had not finished yet.
        if (_filtersTask != null && _loadedFiltersLibraryId != Library?.Id)
            _ = EnsureFiltersLoadedAsync();
        return _queryVersion;
    }

    public void CancelCatalogLoads()
    {
        _snapshot = null;
        _catalogRefreshPending = false;
        ResetLetterIndex();
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
        ResetLetterIndex();
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
        SetLocalizedMessage(nameof(ErrorMessage), () => null, value => ErrorMessage = value);

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
                SetLocalizedMessage(nameof(ErrorMessage), () => $"Failed to load catalog: {(ex is SiloPlayer.Core.Api.ApiException problem ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem) : ex.Message)}", value => ErrorMessage = value);
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

    private async Task<CatalogResponse> FetchPageAsync(int pageIndex, bool includeTotal, string? snapshot = null, CancellationToken ct = default)
    {
        var response = await FetchWindowPageAsync(pageIndex * PageSize, includeTotal, snapshot, ct);
        if (response.Items.Count > PageSize)
        {
            // The legacy sparse list still uses 60-slot UI pages. Its network
            // requests share the same 100-item cursor scope as the window view.
            response.Items = response.Items.Take(PageSize).ToList();
            if (response.Page is not null) response.Page.HasMore = true;
            response.HasMore = true;
        }
        return response;
    }

    private async Task FetchWindowAsync(int startIndex, int itemCount, bool includeTotal,
        Action<CatalogResponse> publishPage, string? snapshot = null, CancellationToken ct = default, bool publishState = true)
    {
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure(
            includeTotal ? "catalog-request-with-total" : "catalog-request-window");
        var response = await FetchWindowPageAsync(startIndex, includeTotal, snapshot, ct, publishState);
        ct.ThrowIfCancellationRequested();
        publishPage(response);
        while (response.Items.Count < itemCount && response.HasMore)
        {
            ct.ThrowIfCancellationRequested();
            var next = await FetchWindowPageAsync(startIndex + response.Items.Count,
                false, response.Snapshot ?? snapshot, ct, publishState);
            ct.ThrowIfCancellationRequested();
            response.HasMore = next.HasMore;
            if (response.Page is not null) response.Page.HasMore = next.HasMore;
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
        var loadVersion = ++_exactCountLoadVersion;
        using var timing = SiloPlayer.Core.Services.LibraryPerformanceTrace.Measure("catalog-exact-count");
        try
        {
            var result = await FetchWindowPageAsync(0, true, snapshot, ct);
            if (ct.IsCancellationRequested || version != _queryVersion || loadVersion != _exactCountLoadVersion || !result.TotalExact) return;
            ApplyExactCatalogTotal(result.Total);
            WindowLoaded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Keep the estimated extent and usable cards if the optional
            // count fails; a later navigation/query can retry it.
        }
    }

    internal string CatalogQueryScopeKey() => System.Text.Json.JsonSerializer.Serialize(new
    {
        library = Library?.Id, SelectedSort, SelectedOrder, UseAdvancedRules, SelectedGenre,
        SelectedStudio, SelectedContentRating, SelectedCountry, SelectedResolution, SelectedAudioLanguage,
        SelectedYearMin, SelectedYearMax, SelectedType, rules = BuildExtraRules(),
        groups = BuildAdvancedGroups(), AdvancedRulesMatch,
    });

    private async Task<CatalogResponse> FetchWindowPageAsync(int startIndex, bool includeTotal, string? snapshot, CancellationToken ct, bool publishState = true)
    {
        using var operation = SiloPlayer.Core.Services.LocalLog.BeginOperation("catalog-" + Guid.NewGuid().ToString("N"));
        var context = _catalogApi.CaptureContext();
        var version = _queryVersion;
        var queryKey = CatalogQueryScopeKey();
        var nextCursor = _completedBoundaries.TryGetValue(startIndex, out var boundary)
            && boundary.QueryVersion == version && boundary.Context == context && boundary.QueryKey == queryKey
            && boundary.Snapshot == snapshot ? boundary.NextCursor : null;
        static string Fingerprint(string? text) => string.IsNullOrEmpty(text) ? "none"
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
        var queryFingerprint = Fingerprint(queryKey);
        var cursorFingerprint = Fingerprint(nextCursor ?? snapshot);
        // A refetch retires the boundary supplied by that page immediately;
        // failed or canceled replacements cannot leave an older cursor usable.
        foreach (var end in _completedBoundaries.Where(pair => pair.Value.Start == startIndex).Select(pair => pair.Key).ToArray())
            _completedBoundaries.Remove(end);
        CatalogResponse response;
        try
        {
        response = await _catalogApi.GetCatalogAsync(
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
            limit: CatalogWindowPageSize,
            offset: startIndex,
            includeTotal: includeTotal,
            snapshot: snapshot,
            ct: ct,
            nextCursor: nextCursor);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            SiloPlayer.Core.Services.LocalLog.AppendException("catalog_errors.txt", ex,
                $"catalog_window start={startIndex} limit={CatalogWindowPageSize} totals={includeTotal} cursor_kind={(nextCursor is not null ? "next" : snapshot is not null ? "window" : "none")} query_version={version} read_generation={context.RequestContextGeneration} query_fingerprint={queryFingerprint} cursor_fingerprint={cursorFingerprint}");
            throw;
        }
        ct.ThrowIfCancellationRequested();
        if (version != _queryVersion || context != _catalogApi.CaptureContext() || _catalogLibraryScope != LibraryCatalogScope(Library))
            throw new OperationCanceledException("Catalog continuation authority changed.", ct);
        SiloPlayer.Core.Services.LocalLog.AppendLine("catalog_queries.txt",
            $"action={SiloPlayer.Core.Services.LocalLog.CurrentOperationId} start={startIndex} limit={CatalogWindowPageSize} query_version={version} read_generation={context.RequestContextGeneration} query_fingerprint={queryFingerprint} input_cursor={cursorFingerprint} window_cursor={Fingerprint(response.Snapshot)} next_cursor={Fingerprint(response.Page?.NextCursor)} items={response.Items.Count} totals={includeTotal}");
        // An unpublished catch-up read addresses each chunk by position in its
        // snapshot; it must not leave continuation cursors for later reads.
        if (publishState && response.Items.Count == CatalogWindowPageSize && response.HasMore
            && !string.IsNullOrWhiteSpace(response.Snapshot) && !string.IsNullOrWhiteSpace(response.Page?.NextCursor))
        {
            _completedBoundaries[startIndex + CatalogWindowPageSize] = new(startIndex, version, context, queryKey, response.Snapshot, response.Page.NextCursor);
            while (_completedBoundaries.Count > 16) _completedBoundaries.Remove(_completedBoundaries.Keys.First());
        }
        if (response.Items.Count > 0)
            _observedCatalogEnd = Math.Max(_observedCatalogEnd, startIndex + response.Items.Count);
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

    private bool HasTrustworthyExactTotal(CatalogResponse response, bool includeTotal) => response.TotalExact &&
        (includeTotal || !_hasExactTotal || response.Total <= TotalCount) &&
        (includeTotal || response.Total > 0 || (!_hasExactTotal && response.Items.Count == 0 && !response.HasMore));

    private void ApplyWindowResponse(int startIndex, CatalogResponse response, bool includeTotal, bool preserveAdjacent = false)
    {
        // Count requests publish separately in LoadExactCountAsync; cached
        // windows must not restore an extent that an exact count retired.
        if (_hasExactTotal && startIndex >= TotalCount)
            return;
        if (!string.IsNullOrWhiteSpace(response.Snapshot))
            _snapshot = response.Snapshot;

        // Do not mutate the response: FetchWindowAsync may still be appending
        // pages to it. Publish only the part inside the authoritative extent.
        var items = _hasExactTotal
            ? response.Items.Take(TotalCount - startIndex).ToList() : response.Items;
        if (preserveAdjacent && items.Count > 0 && WindowStartIndex >= 0 &&
            startIndex <= WindowEndIndex + 1 && startIndex + items.Count >= WindowStartIndex)
        {
            // Small edge fetches must not fragment the active viewport across
            // more cache entries than the eviction limit. Keep a bounded,
            // contiguous active window and retain the existing item objects.
            var first = Math.Min(WindowStartIndex, startIndex);
            var last = Math.Max(WindowEndIndex, startIndex + items.Count - 1);
            if (_hasExactTotal) last = Math.Min(last, TotalCount - 1);
            if (last - first + 1 > 1200)
            {
                if (startIndex < WindowStartIndex) last = first + 1199;
                else first = last - 1199;
            }
            var merged = new MediaItem[last - first + 1];
            for (var i = first; i <= last; i++)
                merged[i - first] = i >= startIndex && i < startIndex + items.Count
                    ? items[i - startIndex]
                    : _windowItems[i - WindowStartIndex];
            _windowItems = merged;
            WindowStartIndex = first;
        }
        else
        {
            _windowItems = items;
            WindowStartIndex = items.Count == 0 ? -1 : startIndex;
        }
        HasMore = response.HasMore && (!_hasExactTotal || startIndex + items.Count < TotalCount);

        if (HasTrustworthyExactTotal(response, includeTotal))
        {
            ApplyExactCatalogTotal(response.Total);
            return;
        }

        if (_hasExactTotal)
        {
            ApplyExactCatalogTotal(_estimatedTotalItems);
            return;
        }

        var loadedEnd = startIndex + response.Items.Count;
        if (!response.HasMore)
            _catalogEndTotal = loadedEnd;
        else if (_catalogEndTotal is int knownEnd && loadedEnd >= knownEnd)
            _catalogEndTotal = null;

        if (LetterIndex is not null)
        {
            // An index replaces the speculative runway, not the observed cards.
            // A non-exact terminal page cannot invalidate the index extent.
            ApplyLetterIndexTotal(LetterIndex.Total);
            return;
        }
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

        if (!highestPageHasMore) _catalogEndTotal = maxLoadedEnd;
        else if (_catalogEndTotal is int knownEnd && maxLoadedEnd >= knownEnd) _catalogEndTotal = null;

        if (LetterIndex is not null)
        {
            ApplyLetterIndexTotal(LetterIndex.Total);
            HasMore = highestPageHasMore;
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
