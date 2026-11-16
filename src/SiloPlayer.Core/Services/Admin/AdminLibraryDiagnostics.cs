using System.Collections.Immutable;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A diagnostics panel shell shared by the five sections under the list.</summary>
public abstract class AdminLibraryPanel : IDisposable
{
    protected readonly AdminLibrariesController Owner;
    protected readonly MonitorAuthorityLease Lease;
    protected readonly object Gate = new();
    protected bool Disposed;

    protected AdminLibraryPanel(AdminLibrariesController owner)
    {
        Owner = owner;
        Lease = owner.CaptureAction();
    }

    /// <summary>Raised on any thread after a state change.</summary>
    public event Action? Changed;
    protected void RaiseChanged() { if (!Disposed) Changed?.Invoke(); }
    protected bool Current => !Disposed && Owner.IsActionCurrent(Lease);
    protected static bool Benign(Exception error, MonitorAuthorityLease lease) => AdminFailures.Classify(error, lease) is AdminFailureKind.Retired or AdminFailureKind.Cancelled;

    public virtual void Dispose()
    {
        lock (Gate) { if (Disposed) return; Disposed = true; }
        Lease.Dispose();
        Changed = null;
    }
}

/// <summary>State of an on-demand, append-paged diagnostics list (roots, skipped roots, stale IDs).</summary>
public sealed record AdminAppendListState<T>
{
    public bool Open { get; init; }
    public string Search { get; init; } = "";
    /// <summary>The first page for the current search has not arrived (the count is unknown, not zero).</summary>
    public bool Loading { get; init; }
    public bool LoadingMore { get; init; }
    public string? Error { get; init; }
    /// <summary>A failure before any page arrived (shown instead of rows); a later failure keeps what loaded.</summary>
    public bool LoadFailed { get; init; }
    public IReadOnlyList<T> Items { get; init; } = [];
    public long? Total { get; init; }
    public bool HasMore { get; init; }
    public bool Capped { get; init; }
    /// <summary>Local 10-row page over the loaded rows (WebUI <c>usePagination</c>).</summary>
    public int PageIndex { get; init; }
    public string? Scope { get; init; }
}

/// <summary>
/// One on-demand diagnostics list (WebUI <c>useInfiniteQuery</c> sections): the first page
/// loads when the section opens, "Load more" appends the next page, a new search or
/// scope restarts at the first page, and at most <see cref="MaxPages"/> pages (500 rows at
/// the 50-row page size) are retained, after which the list says to search instead.
/// </summary>
public sealed class AdminAppendList<T> : AdminLibraryPanel
{
    public const int MaxPages = 10;
    public const int LocalPageSize = 10;

    private readonly Func<MonitorAuthorityLease, string?, string, string?, CancellationToken, Task<AdminTotalCursorPage<T>>> _fetch;
    private AdminAppendListState<T> _state = new();
    private string? _next;
    private int _pages;
    private long _sequence;
    private CancellationTokenSource? _load;

    /// <param name="fetch">(lease, scope, search, cursor, token) → one validated page.</param>
    public AdminAppendList(AdminLibrariesController owner, Func<MonitorAuthorityLease, string?, string, string?, CancellationToken, Task<AdminTotalCursorPage<T>>> fetch, string? scope = null)
        : base(owner)
    {
        _fetch = fetch;
        _state = _state with { Scope = scope };
    }

    public AdminAppendListState<T> State { get { lock (Gate) return _state; } }

    public Task SetOpenAsync(bool open)
    {
        bool load;
        lock (Gate)
        {
            if (Disposed || _state.Open == open) return Task.CompletedTask;
            load = open && _pages == 0 && !_state.Loading;
            _state = _state with { Open = open };
        }
        RaiseChanged();
        return load ? LoadAsync(append: false) : Task.CompletedTask;
    }

    /// <summary>A new search restarts at the first page (only while open; a closed section reads on open).</summary>
    public Task SetSearchAsync(string search)
    {
        bool load;
        lock (Gate)
        {
            if (Disposed || _state.Search == search) return Task.CompletedTask;
            var sameQuery = (AdminLibrariesApi.Search(_state.Search) ?? "") == (AdminLibrariesApi.Search(search) ?? "");
            _state = _state with { Search = search, PageIndex = 0 };
            if (sameQuery) { load = false; }
            else { ResetLocked(); load = _state.Open; }
        }
        RaiseChanged();
        return load ? LoadAsync(append: false) : Task.CompletedTask;
    }

    /// <summary>Changes what the list reads (Ambiguous roots' library) and restarts.</summary>
    public Task SetScopeAsync(string? scope)
    {
        bool load;
        lock (Gate)
        {
            if (Disposed || _state.Scope == scope) return Task.CompletedTask;
            _state = _state with { Scope = scope, PageIndex = 0 };
            ResetLocked();
            load = _state.Open;
        }
        RaiseChanged();
        return load ? LoadAsync(append: false) : Task.CompletedTask;
    }

    public Task LoadMoreAsync()
    {
        lock (Gate) { if (Disposed || _next == null || _state.LoadingMore || _state.Loading) return Task.CompletedTask; }
        return LoadAsync(append: true);
    }

    /// <summary>Re-reads from the first page (Retry, or after an override write).</summary>
    public Task ReloadAsync()
    {
        lock (Gate) { if (Disposed) return Task.CompletedTask; ResetLocked(); if (!_state.Open) { RaiseChanged(); return Task.CompletedTask; } }
        return LoadAsync(append: false);
    }

    public void SetPage(int index)
    {
        lock (Gate)
        {
            var pages = Math.Max(1, (_state.Items.Count + LocalPageSize - 1) / LocalPageSize);
            var clamped = Math.Clamp(index, 0, pages - 1);
            if (clamped == _state.PageIndex) return;
            _state = _state with { PageIndex = clamped };
        }
        RaiseChanged();
    }

    private void ResetLocked()
    {
        _sequence++;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _next = null; _pages = 0;
        _state = _state with { Items = [], Total = null, HasMore = false, Capped = false, Loading = false, LoadingMore = false, Error = null, LoadFailed = false, PageIndex = 0 };
    }

    private async Task LoadAsync(bool append)
    {
        long sequence;
        string? cursor, scope;
        string search;
        CancellationToken token;
        lock (Gate)
        {
            if (Disposed || !Owner.IsActionCurrent(Lease)) return;
            if (append && _pages >= MaxPages) { _state = _state with { Capped = true, HasMore = false }; return; }
            sequence = ++_sequence;
            cursor = append ? _next : null;
            scope = _state.Scope;
            search = _state.Search;
            _load?.Cancel(); _load?.Dispose();
            _load = CancellationTokenSource.CreateLinkedTokenSource(Lease.Token);
            token = _load.Token;
            _state = append ? _state with { LoadingMore = true, Error = null } : _state with { Loading = true, Error = null, LoadFailed = false };
        }
        RaiseChanged();
        try
        {
            var page = await _fetch(Lease, scope, search, cursor, token).ConfigureAwait(false);
            lock (Gate)
            {
                if (Disposed || sequence != _sequence || !Owner.IsActionCurrent(Lease)) return;
                _pages = append ? _pages + 1 : 1;
                _next = page.NextCursor;
                var capped = page.HasMore && _pages >= MaxPages;
                _state = _state with
                {
                    Items = append ? _state.Items.Concat(page.Items).ToArray() : page.Items, Total = page.Total, HasMore = page.HasMore && !capped, Capped = capped,
                    Loading = false, LoadingMore = false, Error = null, LoadFailed = false,
                };
            }
        }
        catch (Exception error)
        {
            if (Benign(error, Lease)) return;
            lock (Gate)
            {
                if (Disposed || sequence != _sequence) return;
                var message = AdminLibrariesText.T("admin.native.libraries.panelFailed", ("error", AdminLibraryPresentation.Describe(error, "admin.native.libraries.transport")));
                _state = _state with { Loading = false, LoadingMore = false, Error = message, LoadFailed = _pages == 0 };
            }
        }
        RaiseChanged();
    }

    public override void Dispose()
    {
        lock (Gate) { _load?.Cancel(); _load?.Dispose(); _load = null; }
        base.Dispose();
    }
}

/// <summary>State of the Unmatched items section.</summary>
public sealed record AdminUnmatchedState
{
    public bool Open { get; init; }
    public bool Ignored { get; init; }
    public string Search { get; init; } = "";
    public long? AwaitingTotal { get; init; }
    public long? IgnoredTotal { get; init; }
    public bool Loading { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<AdminUnmatchedItem> Items { get; init; } = [];
    public long Total { get; init; }
    public int PageIndex { get; init; }
    public bool HasNext { get; init; }
    public bool Walking { get; init; }
    /// <summary>"Last" stopped at the walk bound before reaching the end.</summary>
    public bool WalkCapped { get; init; }
    public ImmutableHashSet<string> Pending { get; init; } = ImmutableHashSet<string>.Empty;
    /// <summary>The last ignore/restore that can be undone (WebUI toast Undo action).</summary>
    public AdminUnmatchedChange? LastChange { get; init; }

    public int TotalPages => Math.Max(1, (int)Math.Min(int.MaxValue, (Total + AdminLibrariesApi.UnmatchedPageSize - 1) / AdminLibrariesApi.UnmatchedPageSize));
    public long RangeStart => Total == 0 ? 0 : PageIndex * (long)AdminLibrariesApi.UnmatchedPageSize + 1;
    public long RangeEnd => Math.Min((PageIndex + 1L) * AdminLibrariesApi.UnmatchedPageSize, Total);
    /// <summary>Hidden while closed and nothing is unmatched or ignored (WebUI).</summary>
    public bool Hidden => !Open && (AwaitingTotal ?? 0) == 0 && (IgnoredTotal ?? 0) == 0 && Search.Trim().Length == 0;
}

public sealed record AdminUnmatchedChange(string ContentId, string Title, bool Ignored, bool Undone);

/// <summary>
/// Unmatched items (WebUI <c>UnmatchedItemsSection</c>): a 10-row cursor page per view
/// (awaiting or ignored) and search, with First/Previous/Next/Last. Only the cursors are
/// retained (at most <see cref="MaxCursors"/>), never every page's rows; a page already
/// visited costs one request, and Last walks forward at most <see cref="MaxWalk"/>
/// requests. Ignore/Restore is one write per item with an Undo, and the listing and
/// both totals are re-read after either outcome (a 409 means the item was matched).
/// </summary>
public sealed class AdminUnmatchedPanel : AdminLibraryPanel
{
    public const int MaxCursors = 1000;
    public const int MaxWalk = 25;

    private AdminUnmatchedState _state = new();
    private readonly List<string?> _cursors = [null];
    private string? _next;
    private long _sequence, _totalsSequence;
    private CancellationTokenSource? _load;

    public AdminUnmatchedPanel(AdminLibrariesController owner) : base(owner) { }

    public AdminUnmatchedState State { get { lock (Gate) return _state; } }

    /// <summary>Reads both unfiltered totals (they decide whether the section shows).</summary>
    public async Task LoadTotalsAsync()
    {
        long sequence;
        lock (Gate) { if (Disposed) return; sequence = ++_totalsSequence; }
        try
        {
            var awaiting = Owner.Api.ListUnmatchedAsync(Lease, null, false, null, Lease.Token);
            var ignored = Owner.Api.ListUnmatchedAsync(Lease, null, true, null, Lease.Token);
            await Task.WhenAll(awaiting, ignored).ConfigureAwait(false);
            lock (Gate)
            {
                if (Disposed || sequence != _totalsSequence || !Current) return;
                _state = _state with { AwaitingTotal = awaiting.Result.Total, IgnoredTotal = ignored.Result.Total };
            }
            RaiseChanged();
        }
        catch (Exception)
        {
            // An unknown count is shown as "—", never as zero.
        }
    }

    public Task SetOpenAsync(bool open)
    {
        bool load;
        lock (Gate)
        {
            if (Disposed || _state.Open == open) return Task.CompletedTask;
            _state = _state with { Open = open };
            load = open;
        }
        RaiseChanged();
        return load ? ShowAsync(_state.PageIndex) : Task.CompletedTask;
    }

    public Task SetViewAsync(bool ignored)
    {
        lock (Gate)
        {
            if (Disposed || _state.Ignored == ignored) return Task.CompletedTask;
            _state = _state with { Ignored = ignored };
            RestartLocked();
        }
        RaiseChanged();
        return ShowAsync(0);
    }

    public Task SetSearchAsync(string search)
    {
        lock (Gate)
        {
            if (Disposed || _state.Search == search) return Task.CompletedTask;
            var same = (AdminLibrariesApi.Search(_state.Search) ?? "") == (AdminLibrariesApi.Search(search) ?? "");
            _state = _state with { Search = search };
            if (same) { RaiseChangedLater(); return Task.CompletedTask; }
            RestartLocked();
        }
        RaiseChanged();
        return _state.Open ? ShowAsync(0) : Task.CompletedTask;
    }

    private void RaiseChangedLater() => Task.Run(RaiseChanged);

    private void RestartLocked()
    {
        _sequence++;
        _load?.Cancel(); _load?.Dispose(); _load = null;
        _cursors.Clear(); _cursors.Add(null); _next = null;
        _state = _state with { PageIndex = 0, Items = [], Total = 0, HasNext = false, Error = null, WalkCapped = false };
    }

    public Task FirstAsync() => ShowAsync(0);
    public Task PreviousAsync() => ShowAsync(Math.Max(0, State.PageIndex - 1));
    public Task NextAsync() => ShowAsync(State.PageIndex + 1);
    public Task LastAsync() => ShowAsync(State.TotalPages - 1);
    public Task ReloadAsync() => ShowAsync(State.PageIndex);

    /// <summary>Shows page <paramref name="index"/>: a visited page is one request; a later page walks forward within <see cref="MaxWalk"/>.</summary>
    public async Task ShowAsync(int index)
    {
        long sequence;
        CancellationToken token;
        bool ignored;
        string search;
        lock (Gate)
        {
            if (Disposed || !Owner.IsActionCurrent(Lease)) return;
            sequence = ++_sequence;
            _load?.Cancel(); _load?.Dispose();
            _load = CancellationTokenSource.CreateLinkedTokenSource(Lease.Token);
            token = _load.Token;
            ignored = _state.Ignored;
            search = _state.Search;
            _state = _state with { Loading = true, Error = null, Walking = index >= _cursors.Count, WalkCapped = false };
        }
        RaiseChanged();
        try
        {
            int reached;
            string? cursor;
            lock (Gate) { reached = Math.Min(index, _cursors.Count - 1); cursor = _cursors[reached]; }
            var page = await Owner.Api.ListUnmatchedAsync(Lease, search, ignored, cursor, token).ConfigureAwait(false);
            var steps = 0;
            while (reached < index && page.NextCursor != null && steps < MaxWalk)
            {
                lock (Gate)
                {
                    if (sequence != _sequence) return;
                    if (_cursors.Count == reached + 1 && _cursors.Count < MaxCursors && !_cursors.Contains(page.NextCursor)) _cursors.Add(page.NextCursor);
                    else if (_cursors.Count <= reached + 1) break;
                }
                reached++;
                steps++;
                cursor = page.NextCursor;
                page = await Owner.Api.ListUnmatchedAsync(Lease, search, ignored, cursor, token).ConfigureAwait(false);
            }
            lock (Gate)
            {
                if (Disposed || sequence != _sequence || !Owner.IsActionCurrent(Lease)) return;
                _next = page.NextCursor;
                if (_next != null && _cursors.Count == reached + 1 && _cursors.Count < MaxCursors && !_cursors.Contains(_next)) _cursors.Add(_next);
                var unfiltered = AdminLibrariesApi.Search(search) == null;
                _state = _state with
                {
                    Loading = false, Walking = false, WalkCapped = reached < index && page.NextCursor != null,
                    PageIndex = reached, Items = page.Items, Total = page.Total, HasNext = page.NextCursor != null && _cursors.Count > reached + 1,
                    AwaitingTotal = unfiltered && !ignored ? page.Total : _state.AwaitingTotal, IgnoredTotal = unfiltered && ignored ? page.Total : _state.IgnoredTotal,
                };
            }
        }
        catch (Exception error)
        {
            if (Benign(error, Lease)) return;
            lock (Gate)
            {
                if (Disposed || sequence != _sequence) return;
                var stale = error is ApiException { ErrorCode: "invalid_cursor" } or ApiException { ProblemType: "invalid_cursor" } or AdminListInconsistentException;
                if (stale) { _cursors.Clear(); _cursors.Add(null); _next = null; }
                _state = _state with
                {
                    Loading = false, Walking = false, Items = stale ? [] : _state.Items, PageIndex = stale ? 0 : _state.PageIndex,
                    Error = AdminLibrariesText.T("admin.native.libraries.panelFailed", ("error", AdminLibraryPresentation.Describe(error, "admin.native.libraries.transport"))),
                };
            }
        }
        RaiseChanged();
    }

    /// <summary>Ignores or restores one item; success offers Undo; the listing and totals reload after either outcome.</summary>
    public async Task SetIgnoredAsync(AdminUnmatchedItem item, bool ignored, bool undo = false)
    {
        lock (Gate)
        {
            if (Disposed || _state.Pending.Contains(item.ContentId)) return;
            _state = _state with { Pending = _state.Pending.Add(item.ContentId), LastChange = undo ? null : _state.LastChange };
        }
        RaiseChanged();
        try
        {
            await Owner.Api.SetUnmatchedIgnoredAsync(Lease, item.ContentId, ignored, Lease.Token).ConfigureAwait(false);
            if (!Current) return;
            if (!undo)
            {
                Owner.Announce(AdminLibrariesText.T(ignored ? "web.admin.libraries.toast.ignored" : "web.admin.libraries.toast.restored", ("title", item.Title)), false);
                lock (Gate) _state = _state with { LastChange = new AdminUnmatchedChange(item.ContentId, item.Title, ignored, false) };
            }
        }
        catch (Exception error)
        {
            if (Benign(error, Lease) || !Current) return;
            var key = undo ? "web.admin.libraries.toast.undoFailed" : ignored ? "web.admin.libraries.toast.ignoreFailed" : "web.admin.libraries.toast.restoreFailed";
            Owner.Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown") : AdminLibraryPresentation.Describe(error, key), true);
        }
        finally
        {
            lock (Gate) _state = _state with { Pending = _state.Pending.Remove(item.ContentId) };
            RaiseChanged();
        }
        if (!Current) return;
        await Task.WhenAll(ShowAsync(State.PageIndex), LoadTotalsAsync()).ConfigureAwait(false);
    }

    /// <summary>The toast's Undo: flips the last change back once.</summary>
    public Task UndoAsync()
    {
        AdminUnmatchedChange? change;
        lock (Gate) { change = _state.LastChange; if (change == null || change.Undone) return Task.CompletedTask; _state = _state with { LastChange = change with { Undone = true } }; }
        RaiseChanged();
        return SetIgnoredAsync(new AdminUnmatchedItem { ContentId = change.ContentId, Title = change.Title }, !change.Ignored, undo: true);
    }

    public void DismissChange()
    {
        lock (Gate) { if (_state.LastChange == null) return; _state = _state with { LastChange = null }; }
        RaiseChanged();
    }

    public override void Dispose()
    {
        lock (Gate) { _load?.Cancel(); _load?.Dispose(); _load = null; }
        base.Dispose();
    }
}

/// <summary>State of the Metadata matcher queues section.</summary>
public sealed record AdminMatcherState
{
    public bool Open { get; init; }
    public bool Loaded { get; init; }
    public IReadOnlyList<AdminMatchQueueStatus> Queues { get; init; } = [];
    public string? Error { get; init; }
    public string? SelectedLibraryId { get; init; }
    public int DetailPageIndex { get; init; }
    public AdminMatchQueueDetail? Detail { get; init; }
    public bool DetailFetching { get; init; }
    public bool DetailHasNext { get; init; }
    public string? DetailError { get; init; }
    public ImmutableHashSet<string> Retrying { get; init; } = ImmutableHashSet<string>.Empty;

    public long Total => Queues.Sum(queue => queue.TotalCount);
}

/// <summary>
/// Metadata matcher queues (WebUI <c>MetadataMatcherQueuesSection</c>): the per-library
/// summary is polled every 10 s while this page owns its authority (the WebUI polls while
/// the page is active) and the section hides at zero; the selected library's detail is
/// one 10-row cursor page, polled only while the section is open and that library is
/// expanded. Retry now is one write per library.
/// </summary>
public sealed class AdminMatcherQueuesPanel : AdminLibraryPanel
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private AdminMatcherState _state = new();
    private readonly List<string?> _cursors = [null];
    private long _summarySequence, _detailSequence;
    private IDisposable? _summaryTimer, _detailTimer;
    private CancellationTokenSource? _detailLoad;

    public AdminMatcherQueuesPanel(AdminLibrariesController owner) : base(owner) { }

    public AdminMatcherState State { get { lock (Gate) return _state; } }

    public async Task LoadSummaryAsync()
    {
        long sequence;
        lock (Gate) { if (Disposed) return; sequence = ++_summarySequence; _summaryTimer?.Dispose(); _summaryTimer = null; }
        try
        {
            var queues = await Owner.Api.ListMatchQueuesAsync(Lease, Lease.Token).ConfigureAwait(false);
            lock (Gate)
            {
                if (Disposed || sequence != _summarySequence || !Current) return;
                _state = _state with { Queues = queues, Loaded = true, Error = null };
            }
        }
        catch (Exception error)
        {
            if (Benign(error, Lease)) return;
            lock (Gate)
            {
                if (Disposed || sequence != _summarySequence) return;
                _state = _state with { Loaded = true, Error = AdminLibrariesText.T("admin.native.libraries.panelFailed", ("error", AdminLibraryPresentation.Describe(error, "admin.native.libraries.transport"))) };
            }
        }
        lock (Gate)
        {
            if (!Disposed && sequence == _summarySequence && Owner.IsActionCurrent(Lease))
                _summaryTimer = Owner.Scheduler.Schedule(PollInterval, () => _ = LoadSummaryAsync());
        }
        RaiseChanged();
    }

    public void SetOpen(bool open)
    {
        lock (Gate)
        {
            if (Disposed || _state.Open == open) return;
            _state = open ? _state with { Open = true } : _state with { Open = false, SelectedLibraryId = null, DetailPageIndex = 0, Detail = null, DetailError = null };
            if (!open) StopDetailLocked();
        }
        RaiseChanged();
    }

    /// <summary>Expands a library's entries (or collapses the expanded one).</summary>
    public Task ToggleLibraryAsync(string libraryId)
    {
        lock (Gate)
        {
            if (Disposed) return Task.CompletedTask;
            StopDetailLocked();
            _cursors.Clear(); _cursors.Add(null);
            _state = _state with
            {
                SelectedLibraryId = _state.SelectedLibraryId == libraryId ? null : libraryId, DetailPageIndex = 0, Detail = null, DetailHasNext = false, DetailError = null,
            };
            if (_state.SelectedLibraryId == null) { RaiseChangedOutside(); return Task.CompletedTask; }
        }
        RaiseChanged();
        return LoadDetailAsync(0);
    }

    private void RaiseChangedOutside() => Task.Run(RaiseChanged);

    public Task NextDetailAsync() => LoadDetailAsync(State.DetailPageIndex + 1);
    public Task PreviousDetailAsync() => LoadDetailAsync(Math.Max(0, State.DetailPageIndex - 1));

    private async Task LoadDetailAsync(int index)
    {
        long sequence;
        string libraryId;
        string? cursor;
        CancellationToken token;
        lock (Gate)
        {
            if (Disposed || !_state.Open || _state.SelectedLibraryId == null || index >= _cursors.Count) return;
            sequence = ++_detailSequence;
            libraryId = _state.SelectedLibraryId;
            cursor = _cursors[index];
            _detailTimer?.Dispose(); _detailTimer = null;
            _detailLoad?.Cancel(); _detailLoad?.Dispose();
            _detailLoad = CancellationTokenSource.CreateLinkedTokenSource(Lease.Token);
            token = _detailLoad.Token;
            _state = _state with { DetailFetching = true };
        }
        RaiseChanged();
        try
        {
            var page = await Owner.Api.GetMatchQueueDetailAsync(Lease, libraryId, cursor, token).ConfigureAwait(false);
            lock (Gate)
            {
                if (Disposed || sequence != _detailSequence || _state.SelectedLibraryId != libraryId || !Current) return;
                if (page.NextCursor != null && _cursors.Count == index + 1 && _cursors.Count < 1000 && !_cursors.Contains(page.NextCursor)) _cursors.Add(page.NextCursor);
                _state = _state with
                {
                    Detail = page.Detail, DetailPageIndex = index, DetailFetching = false, DetailError = null,
                    DetailHasNext = page.NextCursor != null && _cursors.Count > index + 1,
                };
                _detailTimer = Owner.Scheduler.Schedule(PollInterval, () => _ = LoadDetailAsync(State.DetailPageIndex));
            }
        }
        catch (Exception error)
        {
            if (Benign(error, Lease)) return;
            lock (Gate)
            {
                if (Disposed || sequence != _detailSequence) return;
                _state = _state with { DetailFetching = false, DetailError = AdminLibrariesText.T("admin.native.libraries.panelFailed", ("error", AdminLibraryPresentation.Describe(error, "admin.native.libraries.transport"))) };
            }
        }
        RaiseChanged();
    }

    public async Task RetryAsync(string libraryId)
    {
        lock (Gate)
        {
            if (Disposed || _state.Retrying.Contains(libraryId)) return;
            _state = _state with { Retrying = _state.Retrying.Add(libraryId) };
        }
        RaiseChanged();
        try
        {
            await Owner.Api.RetryMatchQueueAsync(Lease, libraryId, Lease.Token).ConfigureAwait(false);
            if (Current) Owner.Announce(AdminLibrariesText.T("web.admin.libraries.toast.matcherBacklogQueued"), false);
        }
        catch (Exception error)
        {
            if (!Benign(error, Lease) && Current)
                Owner.Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
                    : AdminLibraryPresentation.Describe(error, "web.admin.libraries.toast.matcherBacklogFailed"), true);
        }
        finally
        {
            lock (Gate) _state = _state with { Retrying = _state.Retrying.Remove(libraryId) };
            RaiseChanged();
        }
        if (!Current) return;
        _ = LoadSummaryAsync();
        if (State.SelectedLibraryId == libraryId) _ = LoadDetailAsync(State.DetailPageIndex);
    }

    private void StopDetailLocked()
    {
        _detailSequence++;
        _detailTimer?.Dispose(); _detailTimer = null;
        _detailLoad?.Cancel(); _detailLoad?.Dispose(); _detailLoad = null;
    }

    public override void Dispose()
    {
        lock (Gate) { StopDetailLocked(); _summaryTimer?.Dispose(); _summaryTimer = null; _summarySequence++; }
        base.Dispose();
    }
}
