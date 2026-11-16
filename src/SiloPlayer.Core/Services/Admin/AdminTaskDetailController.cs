using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One task's detail view. Validators and cursors are private to the controller and never published.</summary>
public sealed record AdminTaskDetailState
{
    public static AdminTaskDetailState Closed { get; } = new();
    public MonitorAuthorityLease? Owner { get; init; }
    public bool IsOpen { get; init; }
    public string Key { get; init; } = "";
    /// <summary>No task has been read yet and the first read is running.</summary>
    public bool Loading { get; init; }
    public AdminTaskInfo? Task { get; init; }
    /// <summary>The server reports no such task. This outranks a previously read task.</summary>
    public bool NotFound { get; init; }
    /// <summary>The first read failed for another reason; the page offers Retry. A read task is kept through later failures.</summary>
    public AdminFailureKind? Failure { get; init; }
    public AdminTaskMetrics? Metrics { get; init; }
    public IReadOnlyList<AdminTaskExecution> History { get; init; } = [];
    public bool HistoryLoaded { get; init; }
    public bool HistoryHasMore { get; init; }
    public bool HistoryLoading { get; init; }
    public bool HistoryFailed { get; init; }
    public bool RunPending { get; init; }
    public bool CancelPending { get; init; }
    public bool Editing { get; init; }
    public AdminTaskScheduleDraft? Draft { get; init; }
    /// <summary>A schedule read for Edit Schedule or Review is running.</summary>
    public bool ScheduleBusy { get; init; }
    public bool Saving { get; init; }
    /// <summary>A save failed or its outcome is unknown: review the current schedule and adopt it before saving again.</summary>
    public bool NeedsReview { get; init; }
    /// <summary>The reviewed current schedule offered for adoption.</summary>
    public IReadOnlyList<AdminTaskTrigger>? Candidate { get; init; }
    public string? EditorError { get; init; }
    public bool Dirty { get; init; }
    public bool CanSave { get; init; }
    public long Revision { get; init; }
    public override string ToString() => $"{nameof(AdminTaskDetailState)} {{ Key = {Key}, Editing = {Editing} }}";
}

/// <summary>
/// Configure › Scheduled Tasks › task detail state owner (official <c>AdminTaskDetail.tsx</c>,
/// <c>TaskSchedulePanel</c>, <c>useTask</c>, <c>useTaskHistory</c>, <c>useTaskMetrics</c>, <c>useUpdateTriggers</c>).
/// <list type="bullet">
/// <item>The task is read directly by key, so hidden tasks open too. A 404 replaces any cached task;
/// other failures keep a read task and only an unread page offers Retry.</item>
/// <item>The schedule editor captures the persisted schedule and its strong validator when editing begins.
/// A failed or uncertain save keeps the draft and the original validator and requires an explicit review
/// and revision adoption before another save. Writes are never replayed.</item>
/// <item>History is read by cursor, one operation at a time; a restart during a read runs once afterwards.</item>
/// <item>Realtime frames and snapshots only invalidate. <c>refresh_metadata</c> metrics also tick every 30 s
/// while active, without queueing.</item>
/// <item>Suspension or an authority change clears the task, history, cursor, validator and draft.</item>
/// </list>
/// Events are raised on any thread; marshal before touching UI.
/// </summary>
public sealed class AdminTaskDetailController : IDisposable
{
    private readonly AdminTasksApi _api;
    private readonly IAdminRealtimeChannel? _channel;
    private readonly TimeProvider _time;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly AdminTaskReadSlot _taskRead = new();
    private readonly AdminTaskReadSlot _metricsRead = new();
    private readonly string _key;
    private AdminTaskDetailState _state = AdminTaskDetailState.Closed;
    private MonitorAuthorityLease? _lease;
    private CancellationTokenSource? _work;
    private AdminTasksRealtime? _realtime;
    private ITimer? _tick;
    private AdminTaskInfo? _task;
    private bool _notFound, _taskLoaded;
    private AdminFailureKind? _failure;
    private AdminTaskMetrics? _metrics;
    private List<AdminTaskExecution> _history = [];
    private string? _cursor;
    private bool _historyHasMore, _historyLoaded, _historyRunning, _historyRestartPending, _historyFailed;
    private Task _historyTask = Task.CompletedTask;
    private bool _runPending, _cancelPending;
    private bool _editing, _scheduleBusy, _saving, _needsReview;
    private AdminTaskScheduleDraft? _draft;
    private IReadOnlyList<AdminTaskTrigger> _baseline = [];
    private string? _etag;
    private IReadOnlyList<AdminTaskTrigger>? _candidate;
    private string? _candidateEtag;
    private string? _editorError;
    private bool _open, _active, _disposed;
    private long _generation, _revision;

    public AdminTaskDetailController(AdminTasksApi api, AdminMonitorAuthority authority, string key, IAdminRealtimeChannel? channel = null, TimeProvider? time = null)
    {
        _api = api;
        _key = key ?? "";
        _channel = channel;
        _time = time ?? TimeProvider.System;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    public event Action? Changed;
    public event Action? AuthorityRetired;
    public event Action<AdminTaskNotice>? Notice;

    public string Key => _key;
    public AdminTaskDetailState State { get { lock (_gate) return _state; } }
    public bool IsTicking { get { lock (_gate) return _tick != null; } }

    public bool Open()
    {
        MonitorAuthorityLease lease;
        try { lease = _lifetime.Open(); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
        AdminTasksRealtime? previous;
        long generation;
        var safe = AdminTasksApi.IsTaskKey(_key);
        lock (_gate)
        {
            if (_disposed) return false;
            previous = ResetLocked();
            _lease = lease; _open = true; _work = new CancellationTokenSource();
            _notFound = !safe;
            generation = _generation;
            if (_active && safe) StartTickLocked();
            PublishLocked();
        }
        previous?.Dispose();
        if (safe)
        {
            var realtime = new AdminTasksRealtime(_channel, () => Invalidate(generation, everything: true), signal => OnUpdated(generation, signal));
            lock (_gate)
            {
                if (generation == _generation && _open) _realtime = realtime;
                else realtime.Dispose();
            }
            RequestTask(foreground: true);
            RequestHistory(restart: true);
            RequestMetrics(foreground: true);
        }
        Raise();
        return true;
    }

    public bool Reopen() { lock (_gate) if (_disposed) return false; return Open(); }

    public void Resume() { lock (_gate) if (_disposed) return; _lifetime.Resume(); }

    public void Suspend()
    {
        _lifetime.Retire();
        AdminTasksRealtime? realtime;
        lock (_gate) { realtime = ResetLocked(); PublishLocked(); }
        realtime?.Dispose();
        Raise();
    }

    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (!active) StopTickLocked();
            else if (_open && !_notFound) StartTickLocked();
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Explicit Retry after a failed first read.</summary>
    public Task RetryAsync() => RequestTask(foreground: true);

    public Task LoadOlderAsync() => RequestHistory(restart: false);

    /// <summary>Official "Restart history": read the first page again; loaded pages never silently expand.</summary>
    public Task RestartHistoryAsync() => RequestHistory(restart: true);


    public Task RunAsync(MonitorAuthorityLease? owner) => CommandAsync(owner, run: true);
    public Task CancelAsync(MonitorAuthorityLease? owner) => CommandAsync(owner, run: false);

    private async Task CommandAsync(MonitorAuthorityLease? owner, bool run)
    {
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!IsOwnerLocked(owner) || _task == null || _work == null || (run ? _runPending : _cancelPending)) return;
            token = _work.Token; generation = _generation;
        }
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        lock (_gate)
        {
            if (generation != _generation) { action.Dispose(); return; }
            if (run) _runPending = true; else _cancelPending = true;
            PublishLocked();
        }
        Raise();
        AdminTaskNotice? notice = null;
        var reconcile = false;
        try
        {
            if (run) await _api.RunAsync(action, _key, token).ConfigureAwait(false);
            else await _api.CancelAsync(action, _key, token).ConfigureAwait(false);
            notice = new(run ? AdminTaskNoticeKind.Started : AdminTaskNoticeKind.CancelRequested,
                AdminTasksText.Message(run ? "web.admin.tasks.hooks.tasks.started" : "web.admin.tasks.hooks.tasks.cancelRequested"));
            reconcile = true;
        }
        catch (AdminTaskOutcomeUnknownException)
        {
            notice = new(AdminTaskNoticeKind.OutcomeUnknown, AdminTasksText.Message("web.admin.tasks.hooks.tasks.outcomeUnknown"));
            reconcile = true;
        }
        catch (Exception error) when (AdminFailures.Classify(error, action) is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled) || action.IsCurrent)
        {
            var kind = AdminFailures.Classify(error, action);
            notice = new(AdminTaskNoticeKind.Failed, AdminTasksText.Failure(kind, run, !run));
            reconcile = kind is AdminFailureKind.NotFound or AdminFailureKind.Conflict;
        }
        catch (Exception) { }
        finally { action.Dispose(); }
        bool current;
        lock (_gate)
        {
            current = generation == _generation && _open;
            if (current) { if (run) _runPending = false; else _cancelPending = false; PublishLocked(); }
        }
        if (!current) return;
        Raise();
        if (notice != null) Notice?.Invoke(notice with { Owner = owner });
        if (reconcile) _ = RequestTask(foreground: true);
    }


    /// <summary>Edit Schedule: capture the persisted schedule and its strong validator. Manual-only tasks have no editor.</summary>
    public async Task BeginEditAsync(MonitorAuthorityLease? owner)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!IsOwnerLocked(owner) || _task is not { ManualOnly: false } || _editing || _scheduleBusy || _work == null) return;
            lease = _lease!; token = _work.Token; generation = _generation;
            _scheduleBusy = true; _editorError = null;
            PublishLocked();
        }
        Raise();
        AdminTaskSchedule? schedule = null;
        string? error = null;
        try { schedule = await _api.GetScheduleAsync(lease, _key, token).ConfigureAwait(false); }
        catch (Exception failure) { error = ScheduleReadError(failure, lease); }
        lock (_gate)
        {
            if (generation != _generation || !lease.IsCurrent) return;
            _scheduleBusy = false;
            if (schedule != null)
            {
                _editing = true; _baseline = schedule.Triggers; _etag = schedule.ETag;
                _draft = AdminTaskScheduleDraft.From(schedule.Triggers);
                _needsReview = false; _candidate = null; _candidateEtag = null; _editorError = null;
            }
            else _editorError = error;
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Applies one draft edit while editing and not busy.</summary>
    public void UpdateDraft(Func<AdminTaskScheduleDraft, AdminTaskScheduleDraft> change)
    {
        lock (_gate)
        {
            if (!_open || !_editing || _draft == null || _saving || _scheduleBusy) return;
            _draft = change(_draft);
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Sends the draft once with the validator captured when editing began (or explicitly adopted).</summary>
    public async Task SaveAsync(MonitorAuthorityLease? owner)
    {
        IReadOnlyList<AdminTaskTrigger> triggers;
        string etag;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!IsOwnerLocked(owner) || !CanSaveLocked() || _work == null) return;
            triggers = _draft!.ToTriggers(); etag = _etag!; token = _work.Token; generation = _generation;
        }
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        lock (_gate)
        {
            if (generation != _generation) { action.Dispose(); return; }
            _saving = true; _editorError = null;
            PublishLocked();
        }
        Raise();
        AdminTaskSchedule? saved = null;
        string? error = null;
        var retired = false;
        try { saved = await _api.UpdateScheduleAsync(action, _key, triggers, etag, token).ConfigureAwait(false); }
        catch (AdminTaskScheduleConflictException) { error = AdminTasksText.Message("web.admin.tasks.hooks.tasks.scheduleChanged"); }
        catch (AdminTaskOutcomeUnknownException) { error = AdminTasksText.Message("web.admin.tasks.hooks.tasks.outcomeUnknown"); }
        catch (Exception failure)
        {
            var kind = AdminFailures.Classify(failure, action);
            retired = kind is AdminFailureKind.Retired || (kind is AdminFailureKind.Cancelled && !action.IsCurrent);
            error = AdminTasksText.Failure(kind);
        }
        finally { action.Dispose(); }
        lock (_gate)
        {
            if (generation != _generation || retired) return;
            _saving = false;
            if (saved != null)
            {
                ClearEditorLocked();
            }
            else
            {
                _needsReview = true; _editorError = error;
            }
            PublishLocked();
        }
        Raise();
        Notice?.Invoke(saved != null
            ? new AdminTaskNotice(AdminTaskNoticeKind.ScheduleSaved, AdminTasksText.Message("web.admin.tasks.hooks.tasks.scheduleSaved")) { Owner = owner }
            : new AdminTaskNotice(AdminTaskNoticeKind.Failed, error ?? AdminTasksText.Failure(null)) { Owner = owner });
        if (saved != null) _ = RequestTask(foreground: true);
    }

    /// <summary>Review current schedule: read it (with its validator) for explicit adoption.</summary>
    public async Task ReviewAsync(MonitorAuthorityLease? owner)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!IsOwnerLocked(owner) || !_editing || !_needsReview || _scheduleBusy || _saving || _work == null) return;
            lease = _lease!; token = _work.Token; generation = _generation;
            _scheduleBusy = true; _candidate = null; _candidateEtag = null; _editorError = null;
            PublishLocked();
        }
        Raise();
        AdminTaskSchedule? schedule = null;
        string? error = null;
        try { schedule = await _api.GetScheduleAsync(lease, _key, token).ConfigureAwait(false); }
        catch (Exception failure) { error = ScheduleReadError(failure, lease); }
        lock (_gate)
        {
            if (generation != _generation || !lease.IsCurrent) return;
            _scheduleBusy = false;
            if (schedule != null) { _candidate = schedule.Triggers; _candidateEtag = schedule.ETag; }
            else _editorError = error;
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Use this revision and keep my draft: the reviewed validator becomes the guard; the draft is untouched.</summary>
    public void AdoptRevision(MonitorAuthorityLease? owner)
    {
        lock (_gate)
        {
            if (!IsOwnerLocked(owner) || !_editing || _candidate == null || _candidateEtag == null || _scheduleBusy || _saving) return;
            _baseline = _candidate; _etag = _candidateEtag;
            _candidate = null; _candidateEtag = null; _needsReview = false; _editorError = null;
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Cancel: leave editing, dropping the draft and every captured validator.</summary>
    public void CancelEdit()
    {
        lock (_gate)
        {
            if (!_editing && _editorError == null) return;
            if (_saving) return;
            ClearEditorLocked();
            PublishLocked();
        }
        Raise();
    }

    private static string ScheduleReadError(Exception failure, MonitorAuthorityLease lease) => failure is AdminTaskScheduleRevisionException
        ? AdminTasksText.Message("web.admin.errors.scheduleRevisionMissing")
        : AdminTasksText.Failure(AdminFailures.Classify(failure, lease));

    private void ClearEditorLocked()
    {
        _editing = false; _draft = null; _baseline = []; _etag = null;
        _candidate = null; _candidateEtag = null; _needsReview = false; _editorError = null; _scheduleBusy = false;
    }

    private bool CanSaveLocked() => _open && _lease is { IsCurrent: true } && _editing && _draft is { IsValid: true } && _etag != null &&
        !_needsReview && !_scheduleBusy && !_saving;

    private bool IsOwnerLocked(MonitorAuthorityLease? owner) => _open && _lease is { IsCurrent: true } lease && ReferenceEquals(owner, lease);


    private void OnUpdated(long generation, AdminTaskSignal signal)
    {
        if (signal.Key != null && signal.Key != _key) return;
        Invalidate(generation, everything: signal.Key == null || signal.State == AdminTaskStates.Idle);
    }

    private void Invalidate(long generation, bool everything)
    {
        lock (_gate) if (generation != _generation || !_open) return;
        _ = RequestTask(foreground: true);
        if (!everything) return;
        _ = RequestHistory(restart: true);
        RequestMetrics(foreground: true);
    }

    private Task RequestTask(bool foreground)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!_open || _lease is not { IsCurrent: true } current || _work == null || !AdminTasksApi.IsTaskKey(_key) || !_taskRead.Request(foreground))
                return Task.CompletedTask;
            lease = current; token = _work.Token; generation = _generation;
            PublishLocked();
        }
        Raise();
        return ReadTaskAsync(lease, token, generation);
    }

    private async Task ReadTaskAsync(MonitorAuthorityLease lease, CancellationToken token, long generation)
    {
        while (true)
        {
            AdminTaskInfo? task = null;
            AdminFailureKind? failure = null;
            try { task = await _api.GetAsync(lease, _key, token).ConfigureAwait(false); }
            catch (Exception error) { failure = AdminFailures.Classify(error, lease); }
            bool again;
            lock (_gate)
            {
                if (generation != _generation || !lease.IsCurrent) return;
                if (task != null) { _task = task; _taskLoaded = true; _notFound = false; _failure = null; }
                else if (failure == AdminFailureKind.NotFound)
                {
                    _task = null; _notFound = true; _failure = null; ClearEditorLocked();
                }
                else if (failure is not AdminFailureKind.Retired && _task == null) _failure = failure;
                again = _taskRead.Complete();
                PublishLocked();
            }
            Raise();
            if (!again) return;
        }
    }

    private Task RequestHistory(bool restart)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        string? cursor;
        lock (_gate)
        {
            if (!_open || _lease is not { IsCurrent: true } current || _work == null || !AdminTasksApi.IsTaskKey(_key)) return Task.CompletedTask;
            if (_historyRunning)
            {
                if (restart) _historyRestartPending = true;
                return _historyTask;
            }
            if (!restart && (!_historyHasMore || _cursor == null)) return Task.CompletedTask;
            _historyRunning = true;
            cursor = restart ? null : _cursor;
            lease = current; token = _work.Token; generation = _generation;
            _historyTask = ReadHistoryAsync(lease, token, generation, cursor);
            PublishLocked();
        }
        Raise();
        lock (_gate) return _historyTask;
    }

    private async Task ReadHistoryAsync(MonitorAuthorityLease lease, CancellationToken token, long generation, string? cursor)
    {
        await Task.Yield();
        while (true)
        {
            AdminTaskHistoryPage? page = null;
            AdminFailureKind? failure = null;
            try { page = await _api.GetHistoryAsync(lease, _key, cursor, AdminTasksApi.HistoryPageSize, token).ConfigureAwait(false); }
            catch (Exception error) { failure = AdminFailures.Classify(error, lease); }
            lock (_gate)
            {
                if (generation != _generation || !lease.IsCurrent) return;
                if (page != null && cursor == null)
                {
                    _history = page.Items.ToList(); _cursor = page.NextCursor; _historyHasMore = page.HasMore; _historyLoaded = true; _historyFailed = false;
                }
                else if (page != null)
                {
                    var known = _history.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                    if (page.Items.Any(item => known.Contains(item.Id)))
                    {
                        _historyFailed = true; _historyHasMore = false; _cursor = null;
                    }
                    else { _history.AddRange(page.Items); _cursor = page.NextCursor; _historyHasMore = page.HasMore; _historyFailed = false; }
                }
                else if (failure is not AdminFailureKind.Retired) _historyFailed = true;
                if (_historyRestartPending) { _historyRestartPending = false; cursor = null; }
                else { _historyRunning = false; PublishLocked(); break; }
                PublishLocked();
            }
            Raise();
        }
        Raise();
    }

    private void RequestMetrics(bool foreground)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (_key != AdminTasksApi.MetricsTaskKey || !_open || _lease is not { IsCurrent: true } current || _work == null || _notFound || !_metricsRead.Request(foreground)) return;
            lease = current; token = _work.Token; generation = _generation;
        }
        _ = ReadMetricsAsync(lease, token, generation);
    }

    private async Task ReadMetricsAsync(MonitorAuthorityLease lease, CancellationToken token, long generation)
    {
        while (true)
        {
            AdminTaskMetrics? metrics = null;
            var notFound = false;
            try { metrics = await _api.GetMetricsAsync(lease, _key, token).ConfigureAwait(false); }
            catch (Exception error) { notFound = AdminFailures.Classify(error, lease) == AdminFailureKind.NotFound; }
            bool again;
            lock (_gate)
            {
                if (generation != _generation || !lease.IsCurrent) return;
                if (metrics != null) _metrics = metrics;
                else if (notFound) _metrics = null;
                again = _metricsRead.Complete();
                PublishLocked();
            }
            Raise();
            if (!again) return;
        }
    }

    private void StartTickLocked()
    {
        if (_tick != null || !_open || _key != AdminTasksApi.MetricsTaskKey) return;
        var generation = _generation;
        _tick = _time.CreateTimer(_ => OnTick(generation), null, AdminTasksController.TickInterval, AdminTasksController.TickInterval);
    }

    private void StopTickLocked() { _tick?.Dispose(); _tick = null; }

    private void OnTick(long generation)
    {
        lock (_gate) if (generation != _generation || !_open || !_active) return;
        RequestMetrics(foreground: false);
    }


    private void OnAuthorityRetired()
    {
        AdminTasksRealtime? realtime;
        bool wasOpen;
        lock (_gate) { wasOpen = _open; realtime = ResetLocked(); PublishLocked(); }
        realtime?.Dispose();
        Raise();
        if (wasOpen) AuthorityRetired?.Invoke();
    }

    private AdminTasksRealtime? ResetLocked()
    {
        _generation++;
        _work?.Cancel(); _work?.Dispose(); _work = null;
        StopTickLocked();
        _taskRead.Reset(); _metricsRead.Reset();
        _task = null; _taskLoaded = false; _notFound = false; _failure = null; _metrics = null;
        _history = []; _cursor = null; _historyHasMore = false; _historyLoaded = false; _historyRunning = false; _historyRestartPending = false; _historyFailed = false;
        _historyTask = Task.CompletedTask;
        _runPending = false; _cancelPending = false; _saving = false;
        ClearEditorLocked();
        _lease = null; _open = false;
        var realtime = _realtime; _realtime = null;
        return realtime;
    }

    private void PublishLocked()
    {
        var current = _open && _lease is { IsCurrent: true };
        var editing = current && _editing && _draft != null;
        _state = new AdminTaskDetailState
        {
            Owner = _open ? _lease : null,
            IsOpen = _open,
            Key = _key,
            Loading = current && !_taskLoaded && !_notFound && _taskRead.Running,
            Task = current ? _task : null,
            NotFound = current && _notFound,
            Failure = current && _task == null && !_notFound ? _failure : null,
            Metrics = current && !_notFound ? _metrics : null,
            History = current ? _history.ToArray() : [],
            HistoryLoaded = current && _historyLoaded,
            HistoryHasMore = current && _historyHasMore,
            HistoryLoading = current && _historyRunning,
            HistoryFailed = current && _historyFailed,
            RunPending = current && _runPending,
            CancelPending = current && _cancelPending,
            Editing = editing,
            Draft = editing ? _draft : null,
            ScheduleBusy = current && _scheduleBusy,
            Saving = current && _saving,
            NeedsReview = editing && _needsReview,
            Candidate = editing ? _candidate : null,
            EditorError = current ? _editorError : null,
            Dirty = editing && (_needsReview || !_draft!.Matches(_baseline)),
            CanSave = current && CanSaveLocked(),
            Revision = ++_revision,
        };
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        AdminTasksRealtime? realtime;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            realtime = ResetLocked();
            PublishLocked();
        }
        realtime?.Dispose();
        _lifetime.Retired -= OnAuthorityRetired;
        _lifetime.Dispose();
        Changed = null; AuthorityRetired = null; Notice = null;
    }
}
