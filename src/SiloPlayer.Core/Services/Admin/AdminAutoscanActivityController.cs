using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// One history view's rows for the page the admin is on (88b825d57 <c>ActivityPanel</c> offset pages). The server
/// pages by signed continuation, so a page is reached by walking the earlier pages' cursors, as the WebUI does.
/// </summary>
public sealed record AdminAutoscanHistoryState<T>
{
    public AdminAutoscanActivityFilter Filter { get; init; } = new();
    public IReadOnlyList<T> Items { get; init; } = [];
    public long Total { get; init; }
    public int PageIndex { get; init; }
    public bool Loaded { get; init; }
    public bool Fetching { get; init; }
    public AdminFailureKind? Failure { get; init; }
    /// <summary>
    /// The rows still shown belong to the page or page size the admin just left; they stay on screen (with their pager) while the
    /// next read loads, so the control the admin used is not rebuilt away. A failed read then clears them.
    /// </summary>
    public bool Stale { get; init; }
    /// <summary>Pages the page offers: the WebUI refuses an offset of 100 pages or more, so the native list stops there too.</summary>
    public int PageCount => (int)Math.Min(AdminAutoscanActivityApi.MaxHistoryPages, Math.Max(1, (Total + Filter.Limit - 1) / Filter.Limit));
    /// <summary>More matching rows exist than the 100 reachable pages hold.</summary>
    public bool Capped => Total > (long)Filter.Limit * AdminAutoscanActivityApi.MaxHistoryPages;
}

public sealed record AdminAutoscanActivityState
{
    public bool IsOpen { get; init; }
    public AdminAutoscanHistory Selected { get; init; }
    /// <summary>The search text shared by both views, as sent (trimmed).</summary>
    public string Query { get; init; } = "";
    public int PageSize { get; init; } = AdminAutoscanActivityDisplay.DefaultHistoryPageSize;
    public string ScanStatus { get; init; } = "";
    public string PollStatus { get; init; } = "";
    public AdminAutoscanHistoryState<AdminAutoscanActivityScan> Scans { get; init; } = new();
    public AdminAutoscanHistoryState<AdminAutoscanActivityEvent> Polls { get; init; } = new();
    public AdminAutoscanActivityStatus? Status { get; init; }
    public AdminFailureKind? StatusFailure { get; init; }
    public bool StatusFetching { get; init; }
    public AdminAutoscanActivityLabels Labels { get; init; } = AdminAutoscanActivityLabels.Empty;
    public bool LabelsFailed { get; init; }
    public IReadOnlyList<AdminLibraryScanRun> Queue { get; init; } = [];
    public bool QueueCapped { get; init; }
    public string? Cancelling { get; init; }
    public string? Reconciling { get; init; }
    public IReadOnlyDictionary<string, string> CancelReview { get; init; } = new Dictionary<string, string>();
    public string? Message { get; init; }
    public long Revision { get; init; }
    /// <summary>The official refresh button is disabled while the status or the shown history is being read.</summary>
    public bool Refreshing => StatusFetching || Scans.Fetching || Polls.Fetching;
    public string SelectedStatus => Selected == AdminAutoscanHistory.Scans ? ScanStatus : PollStatus;
    public bool HasHistoryFilters => Query.Length > 0 || SelectedStatus.Length > 0;
}

public sealed class AdminAutoscanActivityController : IDisposable
{
    private readonly AdminAutoscanActivityApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly IAdminRealtimeChannel? _channel;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private AdminAutoscanActivityState _state = new();
    private MonitorAuthorityLease? _owner;
    private HistoryCursors _scanCursors = new(""), _pollCursors = new("");
    private CancellationTokenSource? _historyRead;
    private CancellationTokenSource? _readLifetime;
    private Task _historyTask = Task.CompletedTask, _statusTask = Task.CompletedTask, _labelsTask = Task.CompletedTask;
    private IDisposable? _timer, _subscription;
    private long _sequence, _statusSequence, _refreshEpoch, _reviewSequence;
    private bool _disposed;
    private bool _visible = true;
    public event Action? Changed;
    public event Action? AuthorityRetired;

    /// <summary>
    /// The signed continuations seen for one query scope: the cursor that requests each page. A walk to a page starts at the
    /// nearest known cursor, never repeats one, and never goes past the 100-page bound.
    /// </summary>
    private sealed class HistoryCursors(string scope)
    {
        private readonly Dictionary<int, string> _byPage = [];
        public string Scope { get; } = scope;
        public void Clear() { lock (_byPage) _byPage.Clear(); }
        public int KnownAtOrBefore(int page)
        {
            lock (_byPage) { for (var index = page; index > 0; index--) if (_byPage.ContainsKey(index)) return index; return 0; }
        }
        public string? CursorFor(int page)
        {
            lock (_byPage) return page == 0 ? null : _byPage.GetValueOrDefault(page);
        }
        /// <summary>Records the cursor that requests <paramref name="page"/>; false for an unusable or repeated continuation.</summary>
        public bool Record(int page, string? cursor)
        {
            if (!AdminPaging.IsValidCursor(cursor) || page < 1 || page >= AdminAutoscanActivityApi.MaxHistoryPages) return false;
            lock (_byPage)
            {
                if (_byPage.Any(entry => entry.Key != page && entry.Value == cursor)) return false;
                _byPage[page] = cursor!;
                return true;
            }
        }
    }

    public AdminAutoscanActivityController(AdminAutoscanActivityApi api, AdminMonitorAuthority authority,
        IAdminRealtimeChannel? channel = null, IAdminScheduler? scheduler = null)
    {
        _api = api; _authority = authority; _channel = channel; _scheduler = scheduler ?? new SystemAdminScheduler();
        _authority.AvailabilityChanged += AuthorityChanged;
    }

    public AdminAutoscanActivityState State { get { lock (_gate) return Current ? _state : new(); } }
    public bool HasOwner { get { lock (_gate) return _owner != null; } }
    private bool Current => !_disposed && _owner is { IsCurrent: true } && _state.IsOpen;
    private bool Owns(MonitorAuthorityLease owner) => Current && ReferenceEquals(owner, _owner);
    private void Notify() => Changed?.Invoke();
    private AdminAutoscanActivityFilter FilterOf(AdminAutoscanHistory history) =>
        new(_state.Query, history == AdminAutoscanHistory.Scans ? _state.ScanStatus : _state.PollStatus, _state.PageSize);
    private HistoryCursors CursorsOf(AdminAutoscanHistory history) => history == AdminAutoscanHistory.Scans ? _scanCursors : _pollCursors;
    private int PageIndexOf(AdminAutoscanHistory history) => history == AdminAutoscanHistory.Scans ? _state.Scans.PageIndex : _state.Polls.PageIndex;

    private string ScopeOf(AdminAutoscanHistory history)
    {
        var filter = FilterOf(history);
        return AdminCursorPager.ScopeOf(_owner!, filter.Limit, history.ToString(), filter.Query, filter.Status);
    }

    /// <summary>A view that starts over; with <paramref name="keep"/> the shown rows stay (stale) until the next read replaces them.</summary>
    private static AdminAutoscanHistoryState<T> Fresh<T>(AdminAutoscanActivityFilter filter, AdminAutoscanHistoryState<T>? keep = null) =>
        keep is { Loaded: true, Items.Count: > 0 }
            ? new() { Filter = filter, Items = keep.Items, Total = keep.Total, Loaded = true, Stale = true }
            : new() { Filter = filter, Total = keep?.Total ?? 0 };

    /// <summary>
    /// Both views start again from their first page with the filters as they now stand; caller holds the gate. A change of page size alone
    /// (<paramref name="keepRows"/>) keeps the shown view's rows on screen until the new page arrives.
    /// </summary>
    private void ResetViewsLocked(AdminAutoscanHistory? only = null, bool keepRows = false)
    {
        if (only is null or AdminAutoscanHistory.Scans)
        {
            _state = _state with { Scans = Fresh(FilterOf(AdminAutoscanHistory.Scans), keepRows && _state.Selected == AdminAutoscanHistory.Scans ? _state.Scans : null) };
            _scanCursors = new(ScopeOf(AdminAutoscanHistory.Scans));
        }
        if (only is null or AdminAutoscanHistory.Polls)
        {
            _state = _state with { Polls = Fresh(FilterOf(AdminAutoscanHistory.Polls), keepRows && _state.Selected == AdminAutoscanHistory.Polls ? _state.Polls : null) };
            _pollCursors = new(ScopeOf(AdminAutoscanHistory.Polls));
        }
    }

    public Task OpenAsync()
    {
        Detached previous;
        lock (_gate)
        {
            if (_disposed || Current) return Task.CompletedTask;
            previous = RetireLocked();
        }
        // The transport can raise retirement while holding its own lock, and a capture can retire authority and run other
        // pages' handlers. Never enter either (including event accessors and lease disposal) while holding _gate.
        previous.Release();
        MonitorAuthorityLease owner;
        try { owner = _authority.Capture(); }
        catch (MonitorAuthorityRetiredException) { return Task.CompletedTask; }
        bool adopted;
        lock (_gate)
        {
            // A lease retired since the capture already raised its retirement, so nothing would clear it later; another open may also have won.
            adopted = !_disposed && _owner == null && owner.IsCurrent;
            if (adopted)
            {
                _owner = owner;
                _state = new() { IsOpen = true, Revision = _state.Revision + 1 };
                ResetViewsLocked();
                _readLifetime = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            }
        }
        if (!adopted) { owner.Dispose(); return Task.CompletedTask; }
        if (_channel != null)
        {
            var subscription = new RealtimeSubscription(_channel, this, owner);
            bool keep;
            lock (_gate)
            {
                keep = Owns(owner);
                if (keep) _subscription = subscription;
            }
            if (!keep) { subscription.Dispose(); return Task.CompletedTask; }
            if (_channel.TryGetLatestSnapshot("scans", out var data)) OnSnapshot(owner, "scans", data);
        }
        return RefreshAsync();
    }

    /// <summary>Scans or Polls. The official view change returns to the first page and keeps the search.</summary>
    public Task SelectAsync(AdminAutoscanHistory history)
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            CancelHistoryLocked();
            _state = _state with { Selected = history };
            ResetViewsLocked(history);
        }
        // Like the official view change, only the history query is read again; the status and the names are not.
        return StartHistoryAsync();
    }

    /// <summary>Applies the search text, the shown view's status and the page size; any change returns to the first page.</summary>
    public Task FilterAsync(string query, string status, int limit)
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            var filter = new AdminAutoscanActivityFilter(query.Trim(), status, limit);
            _ = AdminAutoscanActivityApi.HistoryPath(_state.Selected, filter);
            var shown = FilterOf(_state.Selected);
            if (filter == shown) return Task.CompletedTask;
            CancelHistoryLocked();
            _state = _state with { Query = filter.Query, PageSize = limit };
            _state = _state.Selected == AdminAutoscanHistory.Scans ? _state with { ScanStatus = status } : _state with { PollStatus = status };
            ResetViewsLocked(keepRows: filter.Query == shown.Query && filter.Status == shown.Status);
        }
        return StartHistoryAsync();
    }

    /// <summary>The official Reset: clears the search and both views' status filters (page size stays).</summary>
    public Task ResetFiltersAsync()
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            CancelHistoryLocked();
            _state = _state with { Query = "", ScanStatus = "", PollStatus = "" };
            ResetViewsLocked();
        }
        return StartHistoryAsync();
    }

    /// <summary>Moves to a page of the shown view; an out-of-range index is clamped into the reachable pages.</summary>
    public Task GoToPageAsync(int page)
    {
        lock (_gate)
        {
            if (!Current || !_visible) return Task.CompletedTask;
            var scans = _state.Selected == AdminAutoscanHistory.Scans;
            var count = scans ? _state.Scans.PageCount : _state.Polls.PageCount;
            var target = Math.Min(Math.Max(page, 0), count - 1);
            if (target == PageIndexOf(_state.Selected) && (scans ? _state.Scans.Loaded : _state.Polls.Loaded) &&
                (scans ? _state.Scans.Failure : _state.Polls.Failure) == null) return Task.CompletedTask;
            CancelHistoryLocked();
            var filter = FilterOf(_state.Selected);
            // The rows of the page being left stay on screen, with the pager the admin used, until the target page arrives.
            _state = scans ? _state with { Scans = Fresh(filter, _state.Scans) with { PageIndex = target, Total = _state.Scans.Total } }
                : _state with { Polls = Fresh(filter, _state.Polls) with { PageIndex = target, Total = _state.Polls.Total } };
        }
        return StartHistoryAsync();
    }

    public Task NextAsync() => GoToPageAsync(PageIndexOf(State.Selected) + 1);
    public Task PreviousAsync() => GoToPageAsync(PageIndexOf(State.Selected) - 1);

    /// <summary>
    /// The official refresh and "Try again": reads the status and the shown page again, walking the earlier pages'
    /// continuations from the first page rather than trusting the cached ones.
    /// </summary>
    public Task ReloadHistoryAsync() => RefreshAsync(explicitReload: true);

    /// <summary>
    /// The page's periodic refresh, its open and the visible-again read: the status, the shown history page and, only when the names
    /// have not been read or the last lookup failed, the names. A page, filter or view change reads the history alone
    /// (<see cref="StartHistoryAsync"/>), and the names are never re-read because a scan finished.
    /// </summary>
    public Task RefreshAsync(bool explicitReload = false)
    {
        Task work;
        lock (_gate)
        {
            if (!Current || !_visible) return Task.CompletedTask;
            _timer?.Dispose(); _timer = null;
            var owner = _owner!;
            if (explicitReload) { CancelHistoryLocked(); CancelStatusLocked(); }
            if (_historyTask.IsCompleted) StartHistoryLocked(owner, explicitReload);
            if (_statusTask.IsCompleted)
            {
                _state = _state with { StatusFetching = true };
                _statusTask = ReadStatusAsync(owner, _readLifetime!.Token, ++_statusSequence);
            }
            if (_labelsTask.IsCompleted && (_state.Labels == AdminAutoscanActivityLabels.Empty || _state.LabelsFailed))
                _labelsTask = ReadLabelsAsync(owner, _readLifetime!.Token);
            work = Task.WhenAll(_historyTask, _statusTask, _labelsTask);
            _ = ScheduleAfterAsync(owner, work, ++_refreshEpoch);
            _state = _state with { Revision = _state.Revision + 1 };
        }
        Notify();
        return work;
    }

    /// <summary>Reads the shown view's current page and nothing else (the official page change refetches only the history query).</summary>
    private Task StartHistoryAsync()
    {
        Task work;
        lock (_gate)
        {
            if (!Current || !_visible) return Task.CompletedTask;
            if (_historyTask.IsCompleted) StartHistoryLocked(_owner!, false);
            work = _historyTask;
            _state = _state with { Revision = _state.Revision + 1 };
        }
        Notify();
        return work;
    }

    /// <summary>Starts the read of the shown view's current page (walking continuations when needed); caller holds the gate.</summary>
    private void StartHistoryLocked(MonitorAuthorityLease owner, bool rewalk)
    {
        var history = _state.Selected; var filter = FilterOf(history); var seq = ++_sequence;
        var target = PageIndexOf(history); var cursors = CursorsOf(history);
        _historyRead = CancellationTokenSource.CreateLinkedTokenSource(_readLifetime!.Token);
        _state = history == AdminAutoscanHistory.Scans ? _state with { Scans = _state.Scans with { Fetching = true } }
            : _state with { Polls = _state.Polls with { Fetching = true } };
        _historyTask = history == AdminAutoscanHistory.Scans
            ? ReadHistoryAsync(owner, history, cursors, filter, target, rewalk, seq, _historyRead, (cursor, onTarget, token) => _api.ReadScansAsync(owner, filter, cursor, token, onTarget),
                (window, index, total) => _state with { Scans = _state.Scans with { Items = window, Total = total, PageIndex = index, Loaded = true, Fetching = false, Failure = null, Stale = false } },
                failure => _state with { Scans = FailedHistory(_state.Scans, failure) })
            : ReadHistoryAsync(owner, history, cursors, filter, target, rewalk, seq, _historyRead, (cursor, onTarget, token) => _api.ReadEventsAsync(owner, filter, cursor, token, onTarget),
                (window, index, total) => _state with { Polls = _state.Polls with { Items = window, Total = total, PageIndex = index, Loaded = true, Fetching = false, Failure = null, Stale = false } },
                failure => _state with { Polls = FailedHistory(_state.Polls, failure) });
    }

    /// <summary>A failed read keeps the rows of a background refresh, but the rows of a page the admin already left are dropped for the error panel.</summary>
    private static AdminAutoscanHistoryState<T> FailedHistory<T>(AdminAutoscanHistoryState<T> view, AdminFailureKind failure) =>
        view.Stale ? view with { Items = [], Loaded = false, Fetching = false, Failure = failure, Stale = false } : view with { Fetching = false, Failure = failure };

    private async Task ReadHistoryAsync<T>(MonitorAuthorityLease owner, AdminAutoscanHistory history, HistoryCursors cursors, AdminAutoscanActivityFilter filter,
        int target, bool rewalk, long seq, CancellationTokenSource read,
        Func<string?, bool, CancellationToken, Task<AdminAutoscanActivityWindow<T>>> readPage,
        Func<IReadOnlyList<T>, int, long, AdminAutoscanActivityState> apply, Func<AdminFailureKind, AdminAutoscanActivityState> fail)
    {
        await Task.Yield();
        try
        {
            if (rewalk) cursors.Clear();
            IReadOnlyList<T> items = []; long total = 0;
            // A shrinking result set can leave the page past the end; the official view then moves to the last page that has rows.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var index = cursors.KnownAtOrBefore(target); var reached = false;
                while (true)
                {
                    // Only the page being shown has its rows validated; the pages walked to reach it just yield their continuation.
                    var window = await readPage(cursors.CursorFor(index), index == target, read.Token).ConfigureAwait(false);
                    total = window.Total;
                    if (index == target)
                    {
                        items = window.Items; reached = true;
                        if (window.NextCursor != null && target + 1 < AdminAutoscanActivityApi.MaxHistoryPages && !cursors.Record(target + 1, window.NextCursor))
                            throw new InvalidDataException("Repeated autoscan continuation.");
                        break;
                    }
                    if (window.NextCursor == null) break;
                    if (!cursors.Record(index + 1, window.NextCursor)) throw new InvalidDataException("Repeated autoscan continuation.");
                    index++;
                }
                if (!reached) items = [];
                var last = (int)Math.Min(AdminAutoscanActivityApi.MaxHistoryPages - 1, Math.Max(0, (total - 1) / filter.Limit));
                if (target <= last) break;
                target = last;
            }
            lock (_gate)
            {
                if (!Owns(owner) || _sequence != seq) return;
                _state = apply(items, target, total) with { Revision = _state.Revision + 1 };
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!Owns(owner) || _sequence != seq) return;
                _state = fail(AdminFailures.Classify(error, owner)) with { Revision = _state.Revision + 1 };
            }
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_historyRead, read)) _historyRead = null; read.Dispose(); }
        }
        Notify();
    }

    private async Task ReadStatusAsync(MonitorAuthorityLease owner, CancellationToken token, long seq)
    {
        await Task.Yield();
        try
        {
            var status = await _api.ReadStatusAsync(owner, token).ConfigureAwait(false);
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested || _statusSequence != seq) return; _state = _state with { Status = status, StatusFailure = null, StatusFetching = false, Revision = _state.Revision + 1 }; }
        }
        catch (Exception error)
        {
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested || _statusSequence != seq) return; _state = _state with { StatusFailure = AdminFailures.Classify(error, owner), StatusFetching = false, Revision = _state.Revision + 1 }; }
        }
        Notify();
    }

    private async Task ReadLabelsAsync(MonitorAuthorityLease owner, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            var labels = await _api.ReadLabelsAsync(owner, token).ConfigureAwait(false);
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested) return; _state = _state with { Labels = labels, LabelsFailed = false, Revision = _state.Revision + 1 }; }
        }
        catch
        {
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested) return; _state = _state with { LabelsFailed = true, Revision = _state.Revision + 1 }; }
        }
        Notify();
    }

    private async Task ScheduleAfterAsync(MonitorAuthorityLease owner, Task work, long epoch)
    {
        await work.ConfigureAwait(false);
        lock (_gate)
        {
            if (!Owns(owner) || !_visible || _refreshEpoch != epoch || _timer != null) return;
            _timer = _scheduler.Schedule(AdminAutoscanActivityApi.RefreshInterval, () =>
            {
                lock (_gate) { if (!Owns(owner) || !_visible || _refreshEpoch != epoch) return; _timer?.Dispose(); _timer = null; }
                _ = RefreshAsync();
            });
        }
    }

    private void OnSnapshot(MonitorAuthorityLease owner, string channel, JsonElement data)
    {
        if (channel != "scans" || data.ValueKind != JsonValueKind.Array) return;
        lock (_gate)
        {
            if (!Owns(owner)) return;
            var scans = data.EnumerateArray().Select(AdminLibraryPresentation.ParseScan)
                .OfType<AdminLibraryScanRun>().Where(scan => scan.IsActive && scan.Trigger == "autoscan" && AdminAutoscanActivityApi.NumericId(scan.LibraryId))
                .DistinctBy(scan => scan.Id).Take(AdminAutoscanActivityApi.MaxActiveScans + 1).ToArray();
            _state = _state with { Queue = SortQueue(scans.Take(AdminAutoscanActivityApi.MaxActiveScans)), QueueCapped = scans.Length > AdminAutoscanActivityApi.MaxActiveScans, Revision = _state.Revision + 1 };
        }
        Notify();
    }

    private void OnEvent(MonitorAuthorityLease owner, string channel, JsonElement data)
    {
        if (channel != "scans" || AdminLibraryPresentation.ParseScan(data) is not { } scan || !AdminAutoscanActivityApi.NumericId(scan.LibraryId)) return;
        lock (_gate)
        {
            if (!Owns(owner)) return;
            var scans = _state.Queue.Where(row => row.Id != scan.Id).ToList();
            var overflow = scan.IsActive && scan.Trigger == "autoscan" && scans.Count >= AdminAutoscanActivityApi.MaxActiveScans;
            if (scan.IsActive && scan.Trigger == "autoscan" && !overflow) scans.Add(scan);
            // A scan event only updates the live queue. The official page never re-reads library, source or connection names because
            // a scan finished, so a stream of events cannot become a stream of lookup requests.
            _state = _state with { Queue = SortQueue(scans), QueueCapped = _state.QueueCapped || overflow, Revision = _state.Revision + 1 };
        }
        Notify();
    }

    private static AdminLibraryScanRun[] SortQueue(IEnumerable<AdminLibraryScanRun> scans) => scans.OrderBy(scan => scan.IsRunning ? 0 : 1)
        .ThenBy(scan => AdminAutoscanActivityDisplay.ModeRank(scan.Mode))
        .ThenBy(scan => scan.Mode, StringComparer.CurrentCulture).ThenBy(scan => scan.Path, StringComparer.CurrentCulture).ToArray();

    /// <summary>A definite refusal: the server answered 4xx (other than 408), so the scan write was not applied.</summary>
    internal static bool IsRefusal(Exception error) => error is ApiException { StatusCode: >= 400 and < 500 and not 408 };

    public async Task CancelAsync(string libraryId)
    {
        MonitorAuthorityLease owner;
        lock (_gate)
        {
            if (!Current || !_visible || _state.Cancelling != null || _state.CancelReview.Count >= AdminAutoscanActivityApi.MaxActiveScans ||
                _state.CancelReview.ContainsKey(libraryId) || !_state.Queue.Any(scan => scan.LibraryId == libraryId)) return;
            owner = _owner!;
            _state = _state with { Cancelling = libraryId, Message = null, Revision = _state.Revision + 1 };
        }
        Notify();
        string message;
        try
        {
            await _api.CancelAsync(owner, libraryId).ConfigureAwait(false);
            message = AdminAutoscanActivityText.T("web.common.scans.cancelRequested");
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!Owns(owner)) return;
                if (IsRefusal(error))
                {
                    // A definite 4xx answer: nothing was applied, so the admin can read the reason and try again.
                    message = error is ApiException api ? ApiProblemText.Display(api, null) : AdminAutoscanActivityText.T("web.common.scans.cancelFailed");
                }
                else
                {
                    var reviews = _state.CancelReview.ToDictionary(entry => entry.Key, entry => entry.Value);
                    reviews[libraryId] = AdminAutoscanActivityText.T("native.cancelUnknown");
                    _state = _state with { CancelReview = reviews };
                    message = AdminAutoscanActivityText.T("web.common.scans.cancelFailed");
                }
            }
        }
        lock (_gate) { if (!Owns(owner)) return; _state = _state with { Cancelling = null, Message = message, Revision = _state.Revision + 1 }; }
        Notify();
    }

    /// <summary>
    /// The admin's explicit way out of an unknown cancellation. The cancel is never replayed here: this only re-reads the live status.
    /// Rule: a status read started by this action (so after the unknown outcome was recorded) that completes successfully is the
    /// canonical re-read, and it lifts the block for that library whatever it shows. The scan is then either gone, or still listed
    /// and visibly running, and a further Cancel is a new, deliberate request that the admin makes, not a blind repeat. A failed read
    /// keeps the block. It does not depend on a realtime snapshot ever arriving.
    /// </summary>
    public async Task ReconcileCancelAsync(string libraryId)
    {
        MonitorAuthorityLease owner;
        long sequence, statusSequence;
        CancellationToken token;
        lock (_gate)
        {
            if (!Current || !_visible || _state.Reconciling != null || !_state.CancelReview.ContainsKey(libraryId)) return;
            owner = _owner!; sequence = ++_reviewSequence; token = _readLifetime!.Token;
            // This read is the newest status read; an older periodic read still in flight must not overwrite it, and it must not overwrite a newer one.
            statusSequence = ++_statusSequence;
            _state = _state with { Reconciling = libraryId, Revision = _state.Revision + 1 };
        }
        Notify();
        AdminAutoscanActivityStatus status;
        try { status = await _api.ReadStatusAsync(owner, token).ConfigureAwait(false); }
        catch
        {
            lock (_gate)
            {
                if (!Owns(owner) || sequence != _reviewSequence || token.IsCancellationRequested) return;
                _state = _state with { Reconciling = null, StatusFetching = _statusSequence == statusSequence ? false : _state.StatusFetching,
                    Message = AdminAutoscanActivityText.T("native.reconcileFailed"), Revision = _state.Revision + 1 };
            }
            Notify(); return;
        }
        lock (_gate)
        {
            if (!Owns(owner) || sequence != _reviewSequence || token.IsCancellationRequested) return;
            // The counts are shown only while no newer status read has been started since this one.
            if (_statusSequence == statusSequence) _state = _state with { Status = status, StatusFailure = null, StatusFetching = false };
            var reviews = _state.CancelReview.ToDictionary(entry => entry.Key, entry => entry.Value); reviews.Remove(libraryId);
            var listed = !_state.QueueCapped && status.ActiveScans > 0 && _state.Queue.Any(scan => scan.LibraryId == libraryId);
            _state = _state with
            {
                Reconciling = null, CancelReview = reviews, Revision = _state.Revision + 1,
                Message = AdminAutoscanActivityText.T(listed ? "native.cancelStillListed" : "native.cancelReviewed")
            };
        }
        Notify();
    }

    private void CancelHistoryLocked()
    {
        _sequence++; _historyRead?.Cancel(); _historyRead = null; _historyTask = Task.CompletedTask;
        _state = _state with { Scans = _state.Scans with { Fetching = false }, Polls = _state.Polls with { Fetching = false } };
    }

    /// <summary>A superseded status read must not reappear as the newest state; it is cancelled and never applied.</summary>
    private void CancelStatusLocked()
    {
        _statusSequence++; _statusTask = Task.CompletedTask; _state = _state with { StatusFetching = false };
    }

    public Task SetVisibleAsync(bool visible)
    {
        lock (_gate)
        {
            if (_visible == visible || !Current) return Task.CompletedTask;
            _visible = visible;
            _refreshEpoch++; _reviewSequence++;
            _timer?.Dispose(); _timer = null;
            CancelHistoryLocked(); _readLifetime?.Cancel(); _readLifetime?.Dispose();
            _state = _state with { Reconciling = null, StatusFetching = false };
            _statusTask = _labelsTask = Task.CompletedTask;
            _readLifetime = visible ? CancellationTokenSource.CreateLinkedTokenSource(_owner!.Token) : null;
        }
        Notify();
        return visible ? RefreshAsync() : Task.CompletedTask;
    }

    /// <summary>Clears the page; caller holds the gate and releases what this returns only after leaving it.</summary>
    private Detached RetireLocked()
    {
        _state = new() { Revision = _state.Revision + 1 };
        CancelHistoryLocked(); _timer?.Dispose(); _timer = null;
        _readLifetime?.Cancel(); _readLifetime?.Dispose(); _readLifetime = null;
        var detached = new Detached(_subscription, _owner);
        _subscription = null; _owner = null; _statusTask = _labelsTask = Task.CompletedTask;
        _scanCursors = new(""); _pollCursors = new("");
        _refreshEpoch++; _reviewSequence++; _visible = true;
        return detached;
    }

    private void AuthorityChanged() { Suspend(); AuthorityRetired?.Invoke(); }
    public void Suspend()
    {
        Detached detached;
        lock (_gate) detached = RetireLocked();
        detached.Release(); Notify();
    }
    public void Dispose()
    {
        Detached detached;
        lock (_gate) { if (_disposed) return; _disposed = true; detached = RetireLocked(); }
        detached.Release();
        _authority.AvailabilityChanged -= AuthorityChanged;
    }

    /// <summary>The channel handlers and leases a retirement took out of the page under _gate; they are released outside it.</summary>
    private readonly record struct Detached(IDisposable? Subscription, MonitorAuthorityLease? Owner)
    {
        public void Release() { Subscription?.Dispose(); Owner?.Dispose(); }
    }

    /// <summary>One open owner's handlers and channel lease, acquired/released outside _gate.</summary>
    private sealed class RealtimeSubscription : IDisposable
    {
        private readonly IAdminRealtimeChannel _channel;
        private readonly Action<string, JsonElement> _snapshot;
        private readonly Action<string, string, JsonElement> _event;
        private IDisposable? _handle;
        private int _disposed;

        public RealtimeSubscription(IAdminRealtimeChannel channel, AdminAutoscanActivityController controller, MonitorAuthorityLease owner)
        {
            _channel = channel;
            _snapshot = (name, data) => controller.OnSnapshot(owner, name, data);
            _event = (name, kind, data) => controller.OnEvent(owner, name, data);
            try
            {
                channel.SnapshotReceived += _snapshot;
                channel.EventReceived += _event;
                _handle = channel.Subscribe("scans");
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _channel.SnapshotReceived -= _snapshot;
            _channel.EventReceived -= _event;
            _handle?.Dispose();
        }
    }
}
