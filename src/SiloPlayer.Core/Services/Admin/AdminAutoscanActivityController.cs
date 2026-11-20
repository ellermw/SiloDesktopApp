using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminAutoscanHistoryState<T>
{
    public AdminAutoscanActivityFilter Filter { get; init; } = new();
    public IReadOnlyList<T> Items { get; init; } = [];
    public long Total { get; init; }
    public string? NextCursor { get; init; }
    public int PageIndex { get; init; }
    public bool Loaded { get; init; }
    public bool Fetching { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public bool Capped { get; init; }
}

public sealed record AdminAutoscanActivityState
{
    public bool IsOpen { get; init; }
    public AdminAutoscanHistory Selected { get; init; }
    public AdminAutoscanHistoryState<AdminAutoscanActivityScan> Scans { get; init; } = new();
    public AdminAutoscanHistoryState<AdminAutoscanActivityEvent> Polls { get; init; } = new();
    public AdminAutoscanActivityStatus? Status { get; init; }
    public AdminFailureKind? StatusFailure { get; init; }
    public AdminAutoscanActivityLabels Labels { get; init; } = AdminAutoscanActivityLabels.Empty;
    public bool LabelsFailed { get; init; }
    public IReadOnlyList<AdminLibraryScanRun> Queue { get; init; } = [];
    public bool QueueCapped { get; init; }
    public string? Cancelling { get; init; }
    public string? Reconciling { get; init; }
    public IReadOnlyDictionary<string, string> CancelReview { get; init; } = new Dictionary<string, string>();
    public string? Message { get; init; }
    public long Revision { get; init; }
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
    private AdminCursorPager _scans = new(99), _polls = new(99);
    private CancellationTokenSource? _historyRead;
    private CancellationTokenSource? _readLifetime;
    private Task _historyTask = Task.CompletedTask, _statusTask = Task.CompletedTask, _labelsTask = Task.CompletedTask;
    private IDisposable? _timer, _subscription;
    private Action<string, JsonElement>? _snapshot;
    private Action<string, string, JsonElement>? _event;
    private long _sequence, _scanObservation, _refreshEpoch, _reviewSequence;
    private bool _disposed;
    private bool _visible = true;
    private bool _labelsDirty;
    private readonly Dictionary<string, long> _uncertainAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terminalReviewEvidence = new(StringComparer.Ordinal);
    public event Action? Changed;
    public event Action? AuthorityRetired;

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
    private AdminCursorPager Pager => _state.Selected == AdminAutoscanHistory.Scans ? _scans : _polls;
    private AdminAutoscanActivityFilter Filter => _state.Selected == AdminAutoscanHistory.Scans ? _state.Scans.Filter : _state.Polls.Filter;
    private void Notify() => Changed?.Invoke();

    public Task OpenAsync()
    {
        lock (_gate)
        {
            if (_disposed || Current) return Task.CompletedTask;
            RetireLocked();
            try { _owner = _authority.Capture(); }
            catch (MonitorAuthorityRetiredException) { return Task.CompletedTask; }
            _state = new() { IsOpen = true, Revision = _state.Revision + 1 };
            var owner = _owner;
            _readLifetime = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            if (_channel != null)
            {
                _snapshot = (name, data) => OnSnapshot(owner, name, data);
                _event = (name, kind, data) => OnEvent(owner, name, data);
                _channel.SnapshotReceived += _snapshot;
                _channel.EventReceived += _event;
                _subscription = _channel.Subscribe("scans");
                if (_channel.TryGetLatestSnapshot("scans", out var data)) OnSnapshot(owner, "scans", data);
            }
        }
        return RefreshAsync();
    }

    public Task SelectAsync(AdminAutoscanHistory history)
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            CancelHistoryLocked();
            _state = _state with { Selected = history, Scans = _state.Scans with { Fetching = false }, Polls = _state.Polls with { Fetching = false } };
        }
        return RefreshAsync();
    }

    public Task FilterAsync(string query, string status, int limit)
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            var filter = new AdminAutoscanActivityFilter(query.Trim(), status, limit);
            _ = AdminAutoscanActivityApi.HistoryPath(_state.Selected, filter);
            if (filter == Filter) return Task.CompletedTask;
            CancelHistoryLocked();
            Pager.SetScope(AdminCursorPager.ScopeOf(_owner!, limit, _state.Selected.ToString(), filter.Query, status));
            _continuations.Remove(Pager);
            _state = _state.Selected == AdminAutoscanHistory.Scans
                ? _state with { Scans = new() { Filter = filter } }
                : _state with { Polls = new() { Filter = filter } };
        }
        return RefreshAsync();
    }

    public Task NextAsync()
    {
        lock (_gate)
        {
            if (!Current || !_historyTask.IsCompleted) return Task.CompletedTask;
            var next = _state.Selected == AdminAutoscanHistory.Scans ? _state.Scans.NextCursor : _state.Polls.NextCursor;
            if (!Pager.TryAdvance(Pager.Capture(), next)) return Task.CompletedTask;
            ClearRowsLocked();
        }
        return RefreshAsync();
    }

    public Task PreviousAsync()
    {
        lock (_gate)
        {
            if (!Current || !_historyTask.IsCompleted || !Pager.Back()) return Task.CompletedTask;
            ClearRowsLocked();
        }
        return RefreshAsync();
    }

    public Task ReloadHistoryAsync()
    {
        lock (_gate)
        {
            if (!Current) return Task.CompletedTask;
            CancelHistoryLocked();
            Pager.SetScope(Guid.NewGuid().ToString("N"));
            _continuations.Remove(Pager);
            ClearRowsLocked();
        }
        return RefreshAsync();
    }

    private void ClearRowsLocked()
    {
        _state = _state.Selected == AdminAutoscanHistory.Scans
            ? _state with { Scans = new() { Filter = _state.Scans.Filter, PageIndex = Pager.PageIndex } }
            : _state with { Polls = new() { Filter = _state.Polls.Filter, PageIndex = Pager.PageIndex } };
    }

    public Task RefreshAsync(bool labels = false)
    {
        Task work;
        lock (_gate)
        {
            if (!Current || !_visible) return Task.CompletedTask;
            _timer?.Dispose(); _timer = null;
            var owner = _owner!;
            if (_historyTask.IsCompleted)
            {
                var filter = Filter; var history = _state.Selected; var pager = Pager;
                if (pager.Scope == null) pager.SetScope(AdminCursorPager.ScopeOf(owner, filter.Limit, history.ToString(), filter.Query, filter.Status));
                var position = pager.Capture(); var seq = ++_sequence;
                _historyRead = CancellationTokenSource.CreateLinkedTokenSource(_readLifetime!.Token);
                _state = history == AdminAutoscanHistory.Scans ? _state with { Scans = _state.Scans with { Fetching = true } }
                    : _state with { Polls = _state.Polls with { Fetching = true } };
                _historyTask = ReadHistoryAsync(owner, history, filter, pager, position, seq, _historyRead);
            }
            if (_statusTask.IsCompleted) _statusTask = ReadStatusAsync(owner, _readLifetime!.Token);
            if (_labelsTask.IsCompleted && (labels || _labelsDirty || _state.Labels == AdminAutoscanActivityLabels.Empty || _state.LabelsFailed))
            {
                _labelsDirty = false;
                _labelsTask = ReadLabelsAsync(owner, _readLifetime!.Token);
            }
            work = Task.WhenAll(_historyTask, _statusTask, _labelsTask);
            _ = ScheduleAfterAsync(owner, work, ++_refreshEpoch);
            _state = _state with { Revision = _state.Revision + 1 };
        }
        Notify();
        return work;
    }

    private async Task ReadHistoryAsync(MonitorAuthorityLease owner, AdminAutoscanHistory history, AdminAutoscanActivityFilter filter,
        AdminCursorPager pager, AdminCursorPosition position, long seq, CancellationTokenSource read)
    {
        await Task.Yield();
        var shrink = false;
        try
        {
            var scans = history == AdminAutoscanHistory.Scans ? await _api.ReadScansAsync(owner, filter, position.Cursor, read.Token).ConfigureAwait(false) : null;
            var polls = history == AdminAutoscanHistory.Polls ? await _api.ReadEventsAsync(owner, filter, position.Cursor, read.Token).ConfigureAwait(false) : null;
            lock (_gate)
            {
                if (!Owns(owner) || _sequence != seq || pager.Capture() != position) return;
                var next = scans?.NextCursor ?? polls?.NextCursor;
                if (next != null && SeenEarlier(pager, next)) throw new InvalidDataException("Repeated autoscan continuation.");
                if (scans != null) _state = _state with { Scans = _state.Scans with { Items = scans.Items, Total = scans.Total, NextCursor = next,
                    Loaded = true, Fetching = false, Failure = null, PageIndex = pager.PageIndex, Capped = next != null && pager.PageIndex >= 99 } };
                if (polls != null) _state = _state with { Polls = _state.Polls with { Items = polls.Items, Total = polls.Total, NextCursor = next,
                    Loaded = true, Fetching = false, Failure = null, PageIndex = pager.PageIndex, Capped = next != null && pager.PageIndex >= 99 } };
                var total = scans?.Total ?? polls!.Total;
                var lastPage = (int)Math.Min(99, Math.Max(0, (total - 1) / filter.Limit));
                if (pager.PageIndex > lastPage)
                {
                    while (pager.PageIndex > lastPage) pager.Back();
                    ClearRowsLocked();
                    shrink = true;
                }
                _state = _state with { Revision = _state.Revision + 1 };
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (!Owns(owner) || _sequence != seq) return;
                var failure = AdminFailures.Classify(error, owner);
                _state = history == AdminAutoscanHistory.Scans ? _state with { Scans = _state.Scans with { Fetching = false, Failure = failure } }
                    : _state with { Polls = _state.Polls with { Fetching = false, Failure = failure } };
                _state = _state with { Revision = _state.Revision + 1 };
            }
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_historyRead, read)) _historyRead = null; read.Dispose(); }
        }
        Notify();
        if (shrink) _ = RefreshAfterShrinkAsync(owner, _historyTask);
    }

    private async Task RefreshAfterShrinkAsync(MonitorAuthorityLease owner, Task previous)
    {
        await previous.ConfigureAwait(false);
        lock (_gate) { if (!Owns(owner) || !_visible) return; }
        await RefreshAsync().ConfigureAwait(false);
    }

    private readonly Dictionary<AdminCursorPager, Dictionary<int, string>> _continuations = new();
    private bool SeenEarlier(AdminCursorPager pager, string next)
    {
        if (!_continuations.TryGetValue(pager, out var seen)) _continuations[pager] = seen = new();
        if (seen.Any(entry => entry.Key < pager.PageIndex && entry.Value == next)) return true;
        foreach (var index in seen.Keys.Where(index => index > pager.PageIndex).ToArray()) seen.Remove(index);
        seen[pager.PageIndex] = next; return false;
    }

    private async Task ReadStatusAsync(MonitorAuthorityLease owner, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            var status = await _api.ReadStatusAsync(owner, token).ConfigureAwait(false);
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested) return; _state = _state with { Status = status, StatusFailure = null, Revision = _state.Revision + 1 }; }
        }
        catch (Exception error)
        {
            lock (_gate) { if (!Owns(owner) || token.IsCancellationRequested) return; _state = _state with { StatusFailure = AdminFailures.Classify(error, owner), Revision = _state.Revision + 1 }; }
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
            _scanObservation++;
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
            if (scan.IsActive) _terminalReviewEvidence.Remove(scan.LibraryId);
            if (scan.Status is "completed" or "failed" or "cancelled" && _state.CancelReview.ContainsKey(scan.LibraryId) &&
                !_state.QueueCapped && !scans.Any(row => row.LibraryId == scan.LibraryId)) _terminalReviewEvidence.Add(scan.LibraryId);
            _state = _state with { Queue = SortQueue(scans), QueueCapped = _state.QueueCapped || overflow, Revision = _state.Revision + 1 };
            if (scan.Status is "completed" or "failed" or "cancelled")
            {
                _labelsDirty = true;
                if (_visible && _labelsTask.IsCompleted)
                {
                    _labelsDirty = false;
                    _labelsTask = ReadLabelsAsync(owner, _readLifetime!.Token);
                }
            }
        }
        Notify();
    }

    private static AdminLibraryScanRun[] SortQueue(IEnumerable<AdminLibraryScanRun> scans) => scans.OrderBy(scan => scan.IsRunning ? 0 : 1)
        .ThenBy(scan => scan.Mode switch { "library" => 0, "file" => 1, "subtree" => 2, _ => 3 })
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
            owner = _owner!; _uncertainAt[libraryId] = _scanObservation; _terminalReviewEvidence.Remove(libraryId);
            _state = _state with { Cancelling = libraryId, Message = null, Revision = _state.Revision + 1 };
        }
        Notify();
        string message;
        try
        {
            await _api.CancelAsync(owner, libraryId).ConfigureAwait(false);
            lock (_gate) { if (!Owns(owner)) return; _uncertainAt.Remove(libraryId); }
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
                    _uncertainAt.Remove(libraryId);
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

    public async Task ReconcileCancelAsync(string libraryId)
    {
        MonitorAuthorityLease owner;
        long observation;
        bool terminal;
        long sequence;
        CancellationToken token;
        lock (_gate)
        {
            if (!Current || !_visible || _state.Reconciling != null || !_state.CancelReview.ContainsKey(libraryId)) return;
            owner = _owner!; observation = _scanObservation; terminal = _terminalReviewEvidence.Contains(libraryId);
            sequence = ++_reviewSequence; token = _readLifetime!.Token;
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
                _state = _state with { Reconciling = null, Message = AdminAutoscanActivityText.T("native.reconcileFailed"), Revision = _state.Revision + 1 };
            }
            Notify(); return;
        }
        lock (_gate)
        {
            if (!Owns(owner) || sequence != _reviewSequence || token.IsCancellationRequested) return;
            _state = _state with { Reconciling = null, Status = status, StatusFailure = null };
            if (status.ActiveScans == 0 || !_state.QueueCapped &&
                (terminal && !_state.Queue.Any(scan => scan.LibraryId == libraryId) || observation > _uncertainAt.GetValueOrDefault(libraryId)))
            {
                var reviews = _state.CancelReview.ToDictionary(entry => entry.Key, entry => entry.Value); reviews.Remove(libraryId); _uncertainAt.Remove(libraryId);
                _terminalReviewEvidence.Remove(libraryId);
                _state = _state with { CancelReview = reviews, Message = AdminAutoscanActivityText.T("native.cancelReviewed"), Revision = _state.Revision + 1 };
            }
            else _state = _state with { Message = AdminAutoscanActivityText.T("native.waitSnapshot"), Revision = _state.Revision + 1 };
        }
        Notify();
    }

    private void CancelHistoryLocked()
    {
        _sequence++; _historyRead?.Cancel(); _historyRead = null; _historyTask = Task.CompletedTask;
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
            _state = _state with { Reconciling = null, Scans = _state.Scans with { Fetching = false }, Polls = _state.Polls with { Fetching = false } };
            _statusTask = _labelsTask = Task.CompletedTask;
            _readLifetime = visible ? CancellationTokenSource.CreateLinkedTokenSource(_owner!.Token) : null;
        }
        Notify();
        return visible ? RefreshAsync() : Task.CompletedTask;
    }

    private void RetireLocked()
    {
        _state = new() { Revision = _state.Revision + 1 };
        CancelHistoryLocked(); _timer?.Dispose(); _timer = null;
        _readLifetime?.Cancel(); _readLifetime?.Dispose(); _readLifetime = null;
        if (_channel != null) { _channel.SnapshotReceived -= _snapshot; _channel.EventReceived -= _event; }
        _subscription?.Dispose(); _subscription = null;
        _owner?.Dispose(); _owner = null; _statusTask = _labelsTask = Task.CompletedTask;
        _scans = new(99); _polls = new(99); _continuations.Clear(); _uncertainAt.Clear(); _terminalReviewEvidence.Clear();
        _scanObservation = 0; _refreshEpoch++; _reviewSequence++; _visible = true; _labelsDirty = false;
    }

    private void AuthorityChanged() { Suspend(); AuthorityRetired?.Invoke(); }
    public void Suspend() { lock (_gate) RetireLocked(); Notify(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; RetireLocked(); }
        _authority.AvailabilityChanged -= AuthorityChanged;
    }
}
