using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

public partial class LibraryViewModel
{
    [ObservableProperty]
    private CatalogLettersResponse? _letterIndex;
    [ObservableProperty]
    private bool _isLetterIndexLoading;
    // The current query's index read failed (not unsupported, not cancelled). The page keeps
    // its rail visible and greyed with a Retry instead of silently dropping it.
    [ObservableProperty]
    private bool _letterIndexFailed;
    [ObservableProperty]
    private string? _pendingLetter;
    private string? _letterJumpError;
    public string? LetterJumpError
    {
        get
        {
            // Profile/language generations can change without a page reset.
            // Observing feedback must not expose Retry for its retired owner.
            if (_letterJumpError is not null) RetireStaleLetterFeedback();
            return _letterJumpError;
        }
        private set => SetProperty(ref _letterJumpError, value);
    }
    private string? _failedLetter;
    private string? _letterFeedbackKey;
    private bool _letterJumpRetiredScope;
    private sealed record LetterWindowSnapshot(int Start, IReadOnlyList<MediaItem> Items,
        (int Start, CatalogResponse Response, bool IncludesTotal)[] Windows, bool HasMore);
    private LetterWindowSnapshot? _letterRestoreWindow;
    public bool IsLetterJumpViewportBlocked => _letterJumpRetiredScope && WindowStartIndex < 0 && LetterJumpError is not null;
    private string? _letterIndexKey;
    private Task? _letterIndexTask;
    private long _letterIndexLoadVersion;
    private long _letterJumpVersion;
    private long _activeLetterJumpVersion;
    public bool IsLetterJumpInProgress => _activeLetterJumpVersion != 0;
    public long LetterJumpVersion => _letterJumpVersion;
    public bool IsCurrentLetterJump(long version) => version == _letterJumpVersion &&
        _letterFeedbackKey == LetterQueryKey();
    public event Action<int>? LetterJumpRequested;

    // Carries an explicit seek's authority into each incremental publication.
    // Transport cancellation alone cannot retire a late or partially read page.
    private sealed class LetterWindowLoad(Func<bool> isCurrent)
    {
        internal bool IsCurrent => isCurrent();
        internal bool Received, Failed, CursorRejected;
    }

    private string LetterQueryKey() => JsonSerializer.Serialize(new
    {
        _queryVersion, context = _catalogApi.CaptureContext(), language = _catalogApi.FilterCacheGeneration,
        library = Library?.Id, scope = LibraryCatalogScope(Library), SelectedSort, SelectedOrder, SelectedType, UseAdvancedRules,
        SelectedGenre, SelectedStudio, SelectedContentRating, SelectedCountry, SelectedResolution,
        SelectedAudioLanguage, SelectedYearMin, SelectedYearMax,
        rules = BuildExtraRules(), groups = BuildAdvancedGroups(), AdvancedRulesMatch,
    });

    public Task EnsureLetterIndexAsync() => EnsureLetterIndexCoreAsync();

    private async Task EnsureLetterIndexCoreAsync(bool force = false)
    {
        if (Library is not null) EnsureCatalogScope();
        // The page's viewport owns catch-up priority, not the start of a held window.
        // A successful catch-up will reread the retained rail in place.
        if (_catalogRefreshPending) return;
        if (Library is null || SelectedSort != "title")
        {
            ResetLetterIndex();
            return;
        }
        var key = LetterQueryKey();
        if (!force && _letterIndexKey == key) { await (_letterIndexTask ?? Task.CompletedTask); return; }
        LetterIndex = null;
        _letterIndexKey = key;
        LetterIndexFailed = false;
        IsLetterIndexLoading = true;
        await (_letterIndexTask = LoadLetterIndexAsync(key, ++_letterIndexLoadVersion, _catalogQueryCts.Token));
    }

    private async Task LoadLetterIndexAsync(string key, long loadVersion, CancellationToken ct, bool preserveExisting = false)
    {
        var libraryId = Library!.Id;
        var order = SelectedOrder;
        var type = SelectedType;
        var groups = BuildAdvancedGroups();
        var queryFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)))[..16];
        try
        {
            var index = await _catalogApi.GetCatalogLettersAsync(libraryId, order,
                genre: UseAdvancedRules ? null : SelectedGenre,
                studio: UseAdvancedRules ? null : SelectedStudio,
                contentRating: UseAdvancedRules ? null : SelectedContentRating,
                country: UseAdvancedRules ? null : SelectedCountry,
                resolution: UseAdvancedRules ? null : SelectedResolution,
                audioLanguage: UseAdvancedRules ? null : SelectedAudioLanguage,
                yearMin: UseAdvancedRules ? null : SelectedYearMin,
                yearMax: UseAdvancedRules ? null : SelectedYearMax, type: type,
                extraRules: BuildExtraRules(), extraRulesMatch: AdvancedRulesMatch,
                queryGroups: groups, queryGroupsMatch: AdvancedRulesMatch, ct: ct);
            if (ct.IsCancellationRequested || key != LetterQueryKey() || loadVersion != _letterIndexLoadVersion) return;
            // A background reread keeps an unchanged index object, so the rail and
            // its chosen end letter stay exactly as they are.
            if (preserveExisting && SameLetterIndex(LetterIndex, index)) return;
            LetterIndex = index;
            if (index is not null)
            {
                // The index supplies a provisional extent, never authority
                // over an exact total returned by the catalog itself.
                ApplyLetterIndexTotal(index.Total);
                WindowLoaded?.Invoke();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (_letterIndexKey == key && loadVersion == _letterIndexLoadVersion) _letterIndexKey = null;
        }
        catch (Exception ex)
        {
            // Includes a transport timeout (TaskCanceledException without caller
            // cancellation): a failed read the rail offers to retry, not a cancel.
            SiloPlayer.Core.Services.LocalLog.AppendException("catalog_errors.txt", ex,
                $"catalog_letters library={libraryId} order={order} type={type ?? "all"} group_count={groups?.Count ?? 0} query_fingerprint={queryFingerprint}");
            // Keep browsing usable. Only explicit query/return/jump requests
            // retry this optional aid; publishing card windows must not retry it.
            if (_letterIndexKey == key && loadVersion == _letterIndexLoadVersion)
            {
                _letterIndexKey = null;
                _letterIndexTask = null;
                // A failed background reread keeps the retained rail usable.
                if (!preserveExisting) LetterIndex = null;
                LetterIndexFailed = LetterIndex is null;
            }
        }
        finally
        {
            if (loadVersion == _letterIndexLoadVersion) IsLetterIndexLoading = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task JumpToLetterAsync(string letter)
    {
        if (string.IsNullOrEmpty(letter) || SelectedSort != "title")
        {
            RetireLetterJump();
            return;
        }
        // Reset an unobserved API-context change before capturing this jump's
        // owner; the window loader must not retire the seek it is about to read.
        var contextChanged = _catalogRefreshPending ||
            (_catalogContext is not null && _catalogContext != _catalogApi.CaptureContext());
        if (contextChanged) StartNewCatalogQuery();
        var currentKey = LetterQueryKey();
        var knownIndex = (LetterIndex is not null && _letterIndexKey == currentKey) ||
            (_failedLetter is not null && _letterFeedbackKey == currentKey);
        var retiredScope = _letterJumpRetiredScope && _letterFeedbackKey == currentKey;
        var restoreWindow = IsLetterJumpInProgress && _letterFeedbackKey == currentKey ? _letterRestoreWindow : null;
        RetireLetterJump();
        _letterJumpRetiredScope = contextChanged || retiredScope;
        _letterRestoreWindow = restoreWindow;
        var jump = _letterJumpVersion;
        letter = letter.ToUpperInvariant();
        var key = _letterFeedbackKey = LetterQueryKey();
        PendingLetter = letter;
        _activeLetterJumpVersion = jump;
        bool IsCurrent() => jump == _letterJumpVersion && key == LetterQueryKey();
        async Task<bool> EnsureFreshViewportAsync()
        {
            if (!IsCurrent()) return false;
            if (!_letterJumpRetiredScope || WindowStartIndex >= 0) return true;
            // A missing optional target must not strand a reset catalog. Read
            // fresh cards under this owner, never restore the retired scope.
            var initialLoad = new LetterWindowLoad(IsCurrent);
            await LoadWindowForOwnerAsync(0, PageSize, force: true, initialLoad);
            if (!IsCurrent() || initialLoad.Failed || initialLoad.CursorRejected || !initialLoad.Received)
                return false;
            _letterJumpRetiredScope = false;
            return true;
        }
        try
        {
            // A changed API context or library scope starts a new query, which retires this owner.
            bool EnsureJumpScope()
            {
                if (!IsCurrent()) return false;
                EnsureCatalogScope();
                return IsCurrent();
            }
            for (var attempt = 0; attempt < 2 && EnsureJumpScope(); attempt++)
            {
                await EnsureLetterIndexAsync();
                var index = LetterIndex;
                if (!IsCurrent()) return;
                if (index is null)
                {
                    if (!await EnsureFreshViewportAsync())
                    {
                        if (IsCurrent()) SetLetterJumpFailure(letter);
                        return;
                    }
                    // A cached null means unsupported, not a failed refresh.
                    // Only failed refreshes of an offered choice retain Retry.
                    if (knownIndex && _letterIndexKey != key) SetLetterJumpFailure(letter);
                    return;
                }
                var entry = index.Letters.FirstOrDefault(x => x.Letter == letter);
                if (entry is null || entry.Position >= TotalCount)
                {
                    _letterIndexKey = null;
                    await EnsureFreshViewportAsync();
                    if (!IsCurrent()) return;
                    SetLetterJumpFailure(letter);
                    return;
                }
                // Superseding clicks share the last restorable viewport, not a
                // cache already cleared by the preceding forced target read.
                var previous = _letterRestoreWindow ??= new LetterWindowSnapshot(
                    WindowStartIndex, _windowItems, _recentWindows.ToArray(), HasMore);
                _activeLetterJumpVersion = jump;
                var loadVersion = jump;
                var loadKey = key;
                var load = new LetterWindowLoad(() => loadVersion == _letterJumpVersion && loadKey == LetterQueryKey());
                await LoadWindowForOwnerAsync(entry.Position, PageSize, force: true, load);
                if (!IsCurrent() || !ReferenceEquals(index, LetterIndex)) return;
                if (load.CursorRejected)
                {
                    // The signed scope was retired. Its cards, cursors AND index
                    // are no longer authoritative; only the current semantic choice
                    // may transfer into a fresh query and exact index read.
                    var extent = TotalCount;
                    StartNewCatalogQuery();
                    _letterJumpRetiredScope = true;
                    _estimatedTotalItems = extent;
                    TotalCount = extent;
                    Items.Reset(extent);
                    jump = _letterJumpVersion;
                    key = _letterFeedbackKey = LetterQueryKey();
                    PendingLetter = letter;
                    _activeLetterJumpVersion = jump;
                    IsLoading = false;
                    knownIndex = true;
                    if (attempt != 0) { SetLetterJumpFailure(letter); return; }
                    continue;
                }
                if (load.Failed || !load.Received || GetWindowItem(entry.Position) is null)
                {
                    // Only this operation may restore its still-visible viewport.
                    // Manual scrolling retires it before another range can publish.
                    _windowItems = previous.Items;
                    WindowStartIndex = previous.Start;
                    _recentWindows.Clear();
                    foreach (var window in previous.Windows) _recentWindows.AddLast(window);
                    HasMore = previous.HasMore;
                    if (!load.Failed || attempt != 0) _letterIndexKey = null;
                    if (!load.Failed)
                    {
                        await EnsureFreshViewportAsync();
                        if (!IsCurrent()) return;
                    }
                    SetLetterJumpFailure(letter);
                    WindowLoaded?.Invoke();
                    return;
                }
                _letterJumpRetiredScope = false;
                if (entry.Position < TotalCount)
                    LetterJumpRequested?.Invoke(entry.Position);
                return;
            }
        }
        finally
        {
            // An older completion must not release a newer jump's viewport hold.
            if (_activeLetterJumpVersion == jump) _activeLetterJumpVersion = 0;
            if (_letterJumpVersion == jump)
            {
                _letterRestoreWindow = null;
                PendingLetter = null;
            }
        }
    }

    private void SetLetterJumpFailure(string letter)
    {
        _failedLetter = letter;
        LetterJumpError = $"Couldn't load {letter} titles.";
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetryLetterJumpAsync()
    {
        RetireStaleLetterFeedback();
        return _failedLetter is { } letter ? JumpToLetterAsync(letter) : Task.CompletedTask;
    }

    private void RetireStaleLetterFeedback()
    {
        if (_letterFeedbackKey is not null && _letterFeedbackKey != LetterQueryKey())
            RetireLetterJump();
    }

    public void RetireLetterJump()
    {
        _letterJumpVersion++;
        if (IsLetterJumpInProgress)
        {
            CancelWindowLoad();
            IsLoading = false;
        }
        _activeLetterJumpVersion = 0;
        _letterFeedbackKey = null;
        _letterRestoreWindow = null;
        PendingLetter = null;
        LetterJumpError = null;
        _failedLetter = null;
        _letterJumpRetiredScope = false;
    }

    private void ResetLetterIndex()
    {
        RetireLetterJump();
        _letterIndexKey = null;
        _letterIndexTask = null;
        _letterIndexLoadVersion++;
        IsLetterIndexLoading = false;
        LetterIndexFailed = false;
        LetterIndex = null;
    }

    // The explicit request the failed-index rail offers; reads stay optional otherwise.
    [RelayCommand]
    private Task RetryLetterIndexAsync() =>
        LetterIndexFailed ? EnsureLetterIndexCoreAsync(force: true) : Task.CompletedTask;

    private void ApplyLetterIndexTotal(int total)
    {
        if (_hasExactTotal) return;
        // Do not carry a no-total runway estimate into the indexed extent.
        // A non-exact terminal page may be older than the index. Retain both
        // the index extent and any larger extent established by real cards.
        _estimatedTotalItems = Math.Max(total, Math.Max(_catalogEndTotal ?? 0, _observedCatalogEnd));
        TotalCount = _estimatedTotalItems;
        Items.SetCount(TotalCount);
        // DisplayTotalCount remains unknown until the catalog supplies a count.
    }

    private void ApplyExactCatalogTotal(int total)
    {
        // Catalog counts are authoritative even if an older index or estimate
        // was larger (for example after items are removed during a scan).
        _estimatedTotalItems = Math.Max(0, total);
        _hasExactTotal = true;
        TotalCount = DisplayTotalCount = _estimatedTotalItems;
        Items.SetCount(TotalCount);
    }
}
