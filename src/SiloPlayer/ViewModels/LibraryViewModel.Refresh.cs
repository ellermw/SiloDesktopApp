using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.ViewModels;

public partial class LibraryViewModel
{
    public enum CatalogRefreshResult { Completed, Deferred, Failed, NotLoaded }

    // Bounded like the window cache itself: never a fan-out across the library.
    internal const int MaxRefreshedItems = 1200;

    /// <summary>
    /// A same-user, same-server, same-profile context whose read generation moved on:
    /// the event channel reconnected (the server drops it every few minutes) or
    /// access was rechecked. The query itself is unchanged.
    /// </summary>
    private static bool IsSameCatalogIdentity(ApiRequestContext? previous, ApiRequestContext current) =>
        previous is { } before && before.AuthenticationGeneration == current.AuthenticationGeneration &&
        string.Equals(before.BaseUrl, current.BaseUrl, StringComparison.Ordinal) &&
        string.Equals(before.ProfileId, current.ProfileId, StringComparison.Ordinal);

    private bool HasRetainableCatalog => Library is not null && WindowStartIndex >= 0 && TotalCount > 0 &&
        _catalogLibraryScope == LibraryCatalogScope(Library);

    /// <summary>
    /// Moves a loaded catalog onto the current request context without clearing it.
    /// Cursors, boundaries and in-flight reads of the old generation retire; the
    /// extent, the loaded windows and the letter index stay as provisional content
    /// until <see cref="RefreshInPlaceAsync"/> replaces them. Only a changed library,
    /// sort or filter (<see cref="StartNewCatalogQuery"/>) empties the grid.
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
        _snapshot = null;
        // A retained letter index stays current for the rebased query (a background
        // read replaces it only if positions changed). An index read still in flight
        // belonged to the old generation; the next request reads it again.
        if (IsLetterIndexLoading) { _letterIndexKey = null; _letterIndexTask = null; }
        else if (_letterIndexKey is not null) { _letterIndexKey = LetterQueryKey(); _letterIndexTask = Task.CompletedTask; }
        _letterIndexLoadVersion++;
        IsLetterIndexLoading = false;
        IsLoading = false;
    }

    /// <summary>
    /// Background catch-up: rereads the held windows (visible range first) under the
    /// current request context in one fresh snapshot and merges the results into the
    /// same positions, so new, removed and changed titles appear without resetting
    /// the scroll position, the loaded windows or the letter rail.
    /// </summary>
    public async Task<CatalogRefreshResult> RefreshInPlaceAsync(int visibleStart, int visibleCount, CancellationToken ct = default)
    {
        if (!HasRetainableCatalog) return CatalogRefreshResult.NotLoaded;
        if (IsLetterJumpInProgress) return CatalogRefreshResult.Deferred;
        if (!IsSameCatalogIdentity(_catalogContext, _catalogApi.CaptureContext())) return CatalogRefreshResult.NotLoaded;
        RebaseCatalogQuery();
        var version = _queryVersion;
        var queryKey = CatalogQueryScopeKey();
        var jump = _letterJumpVersion;
        var chunks = PlanRefreshChunks(visibleStart, Math.Max(1, visibleCount));
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(ct, _catalogQueryCts.Token);
        bool Current() => !owner.IsCancellationRequested && version == _queryVersion &&
            queryKey == CatalogQueryScopeKey() && jump == _letterJumpVersion && !IsLetterJumpInProgress;
        string? snapshot = null;
        var refreshed = 0;
        try
        {
            foreach (var (start, count) in chunks)
            {
                if (!Current()) return CatalogRefreshResult.Deferred;
                CatalogResponse? replacement = null;
                await FetchWindowAsync(start, count, false, response => replacement = response, snapshot, owner.Token);
                if (!Current()) return CatalogRefreshResult.Deferred;
                if (replacement is null) return CatalogRefreshResult.Failed;
                // One snapshot for every held window and for later scrolling, so the
                // merged rows line up with each other and with what loads next.
                if (snapshot is null && !string.IsNullOrWhiteSpace(replacement.Snapshot))
                    _snapshot = snapshot = replacement.Snapshot;
                MergeHeldRange(start, replacement.Items.Take(count).ToList());
                if (!replacement.HasMore && replacement.Items.Count < count)
                    _catalogEndTotal = start + replacement.Items.Count;
                refreshed++;
                WindowLoaded?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer query, reconnect or letter seek owns the catalog now.
            return CatalogRefreshResult.Deferred;
        }
        catch (Exception ex)
        {
            // Keep the retained cards: a failed background read must not blank the grid.
            SiloPlayer.Core.Services.LocalLog.AppendException("catalog_errors.txt", ex,
                $"catalog_refresh_in_place refreshed_windows={refreshed} query_version={version}");
            return CatalogRefreshResult.Failed;
        }
        finally
        {
            SiloPlayer.Core.Services.LocalLog.AppendLine("catalog_queries.txt",
                $"catalog_refresh_in_place visible_start={visibleStart} windows={chunks.Count} refreshed={refreshed} query_version={version} read_generation={_catalogContext?.RequestContextGeneration}");
        }
        if (!Current()) return CatalogRefreshResult.Deferred;
        // Additions or removals change the extent; the count is read in the same snapshot.
        _exactCountQueryVersion = _queryVersion;
        _ = LoadExactCountAsync(_queryVersion, _snapshot, _catalogQueryCts.Token);
        RefreshLetterIndexInPlace();
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
        visibleCount = Math.Clamp(visibleCount, 1, Math.Max(1, TotalCount - visibleStart));

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

        var budget = MaxRefreshedItems;
        var bounded = new List<(int Start, int Count)>();
        foreach (var chunk in chunks)
        {
            if (budget <= 0) break;
            bounded.Add(chunk);
            budget -= chunk.Count;
        }
        return bounded;
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
