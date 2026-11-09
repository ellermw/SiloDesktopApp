using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

public partial class LibraryViewModel
{
    public enum CatalogRefreshResult { Completed, Deferred, Failed, NotLoaded }

    // Active window (1200), recent windows (1200), and at most one visible page.
    // Refresh the entire held set, not just a budgeted subset of its positions.
    internal const int MaxRefreshedItems = 2500;
    private bool _catalogRefreshPending;
    private Task<CatalogRefreshResult>? _catalogRefreshTask;
    private int _catalogRefreshVersion = -1;

    // Retained rows have not been authorized by a read under the new context yet.
    public bool RequiresCatalogRefresh => _catalogRefreshPending ||
        (_catalogContext is not null && _catalogContext != _catalogApi.CaptureContext());

    /// <summary>Only transport recovery may retain rows; an access change retires them.</summary>
    private static bool IsSameCatalogIdentity(ApiRequestContext? previous, ApiRequestContext current) =>
        previous is { } before && before.AuthenticationGeneration == current.AuthenticationGeneration &&
        before.AccessGeneration == current.AccessGeneration &&
        string.Equals(before.BaseUrl, current.BaseUrl, StringComparison.Ordinal) &&
        string.Equals(before.ProfileId, current.ProfileId, StringComparison.Ordinal);

    private bool HasRetainableCatalog => Library is not null && WindowStartIndex >= 0 && TotalCount > 0 &&
        _catalogLibraryScope == LibraryCatalogScope(Library);

    /// <summary>
    /// Moves a loaded catalog onto the current request context without clearing it.
    /// Cursors, boundaries and in-flight reads of the old generation retire; the
    /// snapshot, extent and loaded windows stay together until an atomic refresh
    /// succeeds, or a failure retires the unconfirmed rows without losing the extent.
    /// Ordinary reads cannot mix snapshots while that refresh is pending.
    /// </summary>
    private void RebaseCatalogQuery()
    {
        _catalogContext = _catalogApi.CaptureContext();
        _catalogLibraryScope = LibraryCatalogScope(Library);
        CancelWindowLoad();
        CancelCurrentCatalogQuery();
        _catalogQueryCts = new CancellationTokenSource();
        _queryVersion++;
        _completedBoundaries.Clear();
        _loadingPages.Clear();
        _catalogRefreshPending = true;
        // Keep the displayed rail, but never label its old positions as current.
        // A jump/index request must await a fresh index under this query.
        _letterIndexKey = null;
        _letterIndexTask = null;
        _letterIndexLoadVersion++;
        IsLetterIndexLoading = false;
        IsLoading = false;
        WindowLoaded?.Invoke();
    }

    /// <summary>
    /// Background catch-up: rereads the held windows (visible range first) under the
    /// current request context in one fresh snapshot and merges the results into the
    /// same positions, so new, removed and changed titles appear without resetting
    /// the scroll position, the loaded windows or the letter rail.
    /// </summary>
    public Task<CatalogRefreshResult> RefreshInPlaceAsync(int visibleStart, int visibleCount, CancellationToken ct = default)
    {
        if (_catalogRefreshTask is { IsCompleted: false } && _catalogRefreshVersion == _queryVersion &&
            _catalogContext == _catalogApi.CaptureContext())
            return _catalogRefreshTask;
        return _catalogRefreshTask = RefreshInPlaceCoreAsync(visibleStart, visibleCount, ct);
    }

    private async Task<CatalogRefreshResult> RefreshInPlaceCoreAsync(int visibleStart, int visibleCount, CancellationToken ct)
    {
        if (!HasRetainableCatalog) return CatalogRefreshResult.NotLoaded;
        if (!IsSameCatalogIdentity(_catalogContext, _catalogApi.CaptureContext()))
        {
            EnsureCatalogScope();
            return CatalogRefreshResult.NotLoaded;
        }
        if (IsLetterJumpInProgress) return CatalogRefreshResult.Deferred;
        RebaseCatalogQuery();
        var version = _catalogRefreshVersion = _queryVersion;
        var queryKey = CatalogQueryScopeKey();
        var jump = _letterJumpVersion;
        var chunks = PlanRefreshChunks(visibleStart, Math.Max(1, visibleCount));
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(ct, _catalogQueryCts.Token);
        bool Current() => !owner.IsCancellationRequested && version == _queryVersion &&
            _catalogContext == _catalogApi.CaptureContext() && queryKey == CatalogQueryScopeKey() &&
            jump == _letterJumpVersion && !IsLetterJumpInProgress;
        string? snapshot = null;
        var replacements = new List<(int Start, int Count, CatalogResponse Response)>();
        var refreshed = 0;
        CatalogRefreshResult FailRefresh(string message)
        {
            if (!Current()) return CatalogRefreshResult.Deferred;
            // No staged rows were published. Retire the unconfirmed old rows as
            // a unit, but keep the extent/scroll while ordinary viewport loads
            // retry without depending on an unreadable, distant held window.
            _pageResponses.Clear();
            _recentWindows.Clear();
            _completedBoundaries.Clear();
            Items.Reset(TotalCount);
            _snapshot = null;
            _hasExactTotal = false;
            _estimatedTotalItems = TotalCount;
            _observedCatalogEnd = 0;
            _catalogEndTotal = null;
            _exactCountQueryVersion = -1;
            ResetLetterIndex();
            _catalogRefreshPending = false;
            ErrorMessage = $"Failed to refresh catalog: {message}";
            ClearActiveWindow(notify: true);
            return CatalogRefreshResult.Failed;
        }
        try
        {
            foreach (var (start, count) in chunks)
            {
                if (!Current()) return CatalogRefreshResult.Deferred;
                CatalogResponse? replacement = null;
                await FetchWindowAsync(start, count, false, response => replacement = response, snapshot, owner.Token,
                    publishState: false);
                if (!Current()) return CatalogRefreshResult.Deferred;
                if (replacement is null || string.IsNullOrWhiteSpace(replacement.Snapshot))
                    return FailRefresh("The server did not return a catalog snapshot.");
                // Stage all chunks without changing live rows, cursors, counts or the
                // old snapshot. Deferred reads retain them; failed reads retire them as a unit.
                snapshot ??= replacement.Snapshot;
                if (replacement.Snapshot != snapshot)
                    return FailRefresh("The catalog snapshot changed during refresh.");
                replacements.Add((start, count, replacement));
                refreshed++;
            }
        }
        catch (OperationCanceledException) when (!Current())
        {
            // Caller cancellation or a newer query/reconnect/letter seek owns the catalog.
            // A transport timeout without cancellation instead takes the failure path.
            return CatalogRefreshResult.Deferred;
        }
        catch (Exception ex)
        {
            SiloPlayer.Core.Services.LocalLog.AppendException("catalog_errors.txt", ex,
                $"catalog_refresh_in_place refreshed_windows={refreshed} query_version={version}");
            return FailRefresh(ex.Message);
        }
        finally
        {
            SiloPlayer.Core.Services.LocalLog.AppendLine("catalog_queries.txt",
                $"catalog_refresh_in_place visible_start={visibleStart} windows={chunks.Count} refreshed={refreshed} query_version={version} read_generation={_catalogContext?.RequestContextGeneration}");
        }
        if (!Current()) return CatalogRefreshResult.Deferred;
        // Commit without yielding. Legacy sparse pages were not part of the held
        // window plan, so retire them rather than retaining unrefreshed rows.
        _pageResponses.Clear();
        Items.Reset(TotalCount);
        _snapshot = snapshot;
        _hasExactTotal = false;
        _observedCatalogEnd = 0;
        _catalogEndTotal = null;
        foreach (var (start, count, response) in replacements)
        {
            MergeHeldRange(start, response.Items.Take(count).ToList());
            _observedCatalogEnd = Math.Max(_observedCatalogEnd, start + response.Items.Count);
            if (!response.HasMore)
                _catalogEndTotal = Math.Min(_catalogEndTotal ?? int.MaxValue, start + response.Items.Count);
        }
        // A shorter catalog must not leave the unfilled tail of an old window alive.
        if (_catalogEndTotal is int end)
        {
            _windowItems = _windowItems.Take(Math.Max(0, end - WindowStartIndex)).ToArray();
            if (_windowItems.Count == 0) WindowStartIndex = -1;
            foreach (var window in _recentWindows)
                window.Response.Items = window.Response.Items.Take(Math.Max(0, end - window.Start)).ToList();
        }
        foreach (var window in _recentWindows)
        {
            window.Response.Snapshot = snapshot;
            window.Response.TotalExact = false;
            window.Response.Total = 0;
        }
        _catalogRefreshPending = false;
        ErrorMessage = null;
        // These chunks used skip_total: omitted totals can deserialize as exact
        // zero. Keep the extent until the explicit count read in this snapshot.
        _exactCountQueryVersion = _queryVersion;
        _ = LoadExactCountAsync(_queryVersion, _snapshot, _catalogQueryCts.Token);
        RefreshLetterIndexInPlace();
        WindowLoaded?.Invoke();
        return CatalogRefreshResult.Completed;
    }

    /// <summary>The held index ranges as reads of at most 100 items, the viewport first, then nearest it.</summary>
    internal List<(int Start, int Count)> PlanRefreshChunks(int visibleStart, int visibleCount)
    {
        var held = new SortedSet<int>();
        void Hold(int start, int count)
        {
            for (var index = Math.Max(0, start); index < start + count; index++) held.Add(index);
        }
        if (WindowStartIndex >= 0) Hold(WindowStartIndex, _windowItems.Count);
        foreach (var window in _recentWindows) Hold(window.Start, window.Response.Items.Count);
        visibleStart = Math.Clamp(visibleStart, 0, Math.Max(0, TotalCount - 1));
        visibleCount = Math.Clamp(visibleCount, 1, Math.Min(CatalogWindowPageSize, Math.Max(1, TotalCount - visibleStart)));

        var chunks = new List<(int Start, int Count)>();
        for (var start = visibleStart; start < visibleStart + visibleCount; start += CatalogWindowPageSize)
            chunks.Add((start, Math.Min(CatalogWindowPageSize, visibleStart + visibleCount - start)));
        held.ExceptWith(Enumerable.Range(visibleStart, visibleCount));

        var rest = new List<(int Start, int Count)>();
        var segmentStart = -1;
        var previous = -2;
        void Close()
        {
            for (var start = segmentStart; start <= previous; start += CatalogWindowPageSize)
                rest.Add((start, Math.Min(CatalogWindowPageSize, previous - start + 1)));
        }
        foreach (var index in held)
        {
            if (index != previous + 1)
            {
                if (segmentStart >= 0) Close();
                segmentStart = index;
            }
            previous = index;
        }
        if (segmentStart >= 0) Close();
        chunks.AddRange(rest.OrderBy(chunk => Math.Abs(chunk.Start - visibleStart)));

        return chunks;
    }

    /// <summary>
    /// Replaces every held copy of an index range with fresh rows (an insertion or
    /// removal shifts titles into their new positions). Cards keep their slots; the
    /// page rebinds them and keeps artwork that did not change.
    /// </summary>
    private void MergeHeldRange(int start, IReadOnlyList<MediaItem> replacement)
    {
        if (replacement.Count == 0) return;
        var end = start + replacement.Count;
        // Rows nothing held yet (a visible edge) need a window of their own. A range
        // spread over several held windows must not add one: that would evict another.
        var unheld = Enumerable.Range(start, replacement.Count).Any(index => GetWindowItem(index) is null);
        if (WindowStartIndex >= 0 && start < WindowStartIndex + _windowItems.Count && end > WindowStartIndex)
        {
            // A new array: the old list may still be shared with a cached window.
            var active = _windowItems.ToArray();
            for (var index = Math.Max(start, WindowStartIndex); index < Math.Min(end, WindowStartIndex + active.Length); index++)
                active[index - WindowStartIndex] = replacement[index - start];
            _windowItems = active;
        }
        foreach (var window in _recentWindows)
        {
            var items = window.Response.Items;
            for (var index = Math.Max(start, window.Start); index < Math.Min(end, window.Start + items.Count); index++)
                items[index - window.Start] = replacement[index - start];
        }
        foreach (var (page, response) in _pageResponses)
        {
            var pageStart = page * PageSize;
            if (pageStart >= end || pageStart + response.Items.Count <= start) continue;
            for (var index = Math.Max(start, pageStart); index < Math.Min(end, pageStart + response.Items.Count); index++)
                response.Items[index - pageStart] = replacement[index - start];
            Items.SetPage(page, response.Items);
        }
        if (unheld) RememberWindow(start, new CatalogResponse { Items = replacement.ToList(), Snapshot = _snapshot }, false);
    }

    private static bool SameLetterIndex(CatalogLettersResponse? current, CatalogLettersResponse? next) =>
        current is not null && next is not null && current.Total == next.Total &&
        current.Letters.Select(letter => (letter.Letter, letter.Count, letter.Position))
            .SequenceEqual(next.Letters.Select(letter => (letter.Letter, letter.Count, letter.Position))) &&
        current.Runs.Select(run => (run.Letter, run.Position)).SequenceEqual(next.Runs.Select(run => (run.Letter, run.Position)));

    /// <summary>Rereads the alphabet without greying or emptying the rail; an equal index keeps its object.</summary>
    private void RefreshLetterIndexInPlace()
    {
        if (Library is null || SelectedSort != "title" || LetterIndexFailed) return;
        if (LetterIndex is null)
        {
            // Only an index read the rebase retired is requested again here.
            if (_letterIndexKey is null) _ = EnsureLetterIndexAsync();
            return;
        }
        var key = _letterIndexKey = LetterQueryKey();
        _letterIndexTask = LoadLetterIndexAsync(key, ++_letterIndexLoadVersion, _catalogQueryCts.Token, preserveExisting: true);
    }
}
