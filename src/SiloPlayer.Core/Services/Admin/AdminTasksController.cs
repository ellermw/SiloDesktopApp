using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One list section: a category in official order, or the separate manual-only group.</summary>
public sealed record AdminTaskGroup(string Id, string Label, string? Description, IReadOnlyList<AdminTaskInfo> Tasks);

public sealed record AdminTasksState
{
    public static AdminTasksState Closed { get; } = new();
    public MonitorAuthorityLease? Owner { get; init; }
    public bool IsOpen { get; init; }
    /// <summary>No list has been read yet and a read is running (official isLoading).</summary>
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    /// <summary>The latest list read failed. A loaded list is kept; only an unread page shows the failure.</summary>
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyList<AdminTaskInfo> Tasks { get; init; } = [];
    public IReadOnlyList<AdminTaskGroup> Groups { get; init; } = [];
    /// <summary>Last good <c>refresh_metadata</c> queue metrics, while that task is listed.</summary>
    public AdminTaskMetrics? Metrics { get; init; }
    /// <summary>The relative-time clock; it advances every 30 s only while the page is active.</summary>
    public DateTimeOffset Now { get; init; }
    /// <summary>Tasks whose Run Now feedback is showing (at least one second, official RUN_BUTTON_MIN_VISIBLE_MS).</summary>
    public IReadOnlyCollection<string> Starting { get; init; } = [];
    /// <summary>Tasks with a cancel request in flight.</summary>
    public IReadOnlyCollection<string> Stopping { get; init; } = [];
    public long Revision { get; init; }
    public override string ToString() => nameof(AdminTasksState);
}

/// <summary>
/// Configure › Scheduled Tasks list state owner (official <c>AdminTasks.tsx</c> with <c>useTasks</c>,
/// <c>useTaskMetrics</c>, <c>useRunTask</c>, <c>useCancelTask</c> and the realtime <c>tasks</c> channel).
/// <list type="bullet">
/// <item>The list is read on open and again only when the realtime channel invalidates it; it is never polled.
/// An invalidation during a read queues exactly one trailing read.</item>
/// <item>While the page and window are active a 30 s tick advances the relative-time clock and refreshes
/// <c>refresh_metadata</c> metrics; a tick never queues behind a running read.</item>
/// <item>Run and Stop capture an action lease, send once without authentication replay, then invalidate.
/// An unknown outcome is reconciled with one list read and never resent.</item>
/// <item>Suspension or any authority change cancels reads and commands, releases the channel, stops the
/// tick, clears all data and discards late results.</item>
/// </list>
/// Events are raised on any thread; marshal before touching UI.
/// </summary>
public sealed class AdminTasksController : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RunFeedbackMinimum = TimeSpan.FromSeconds(1);
    public const string OnDemandGroup = "on-demand";

    private readonly AdminTasksApi _api;
    private readonly IAdminRealtimeChannel? _channel;
    private readonly TimeProvider _time;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly AdminTaskReadSlot _listRead = new();
    private readonly AdminTaskReadSlot _metricsRead = new();
    private readonly HashSet<string> _starting = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stopping = new(StringComparer.Ordinal);
    private AdminTasksState _state = AdminTasksState.Closed;
    private MonitorAuthorityLease? _lease;
    private CancellationTokenSource? _work;
    private AdminTasksRealtime? _realtime;
    private ITimer? _tick;
    private IReadOnlyList<AdminTaskInfo> _tasks = [];
    private AdminTaskMetrics? _metrics;
    private AdminFailureKind? _failure;
    private DateTimeOffset _now;
    private bool _loaded, _open, _active, _disposed;
    private long _generation, _revision;

    public AdminTasksController(AdminTasksApi api, AdminMonitorAuthority authority, IAdminRealtimeChannel? channel = null, TimeProvider? time = null)
    {
        _api = api;
        _channel = channel;
        _time = time ?? TimeProvider.System;
        _now = _time.GetUtcNow();
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    public event Action? Changed;
    /// <summary>The admin authority changed while open; everything was cleared. Marshal and call <see cref="Reopen"/> if still allowed.</summary>
    public event Action? AuthorityRetired;
    public event Action<AdminTaskNotice>? Notice;

    public AdminTasksState State { get { lock (_gate) return _state; } }
    public bool IsTicking { get { lock (_gate) return _tick != null; } }

    /// <summary>Official grouping: library, metadata, system for scheduled tasks, then "On demand"; empty groups are omitted.</summary>
    public static IReadOnlyList<AdminTaskGroup> Group(IReadOnlyList<AdminTaskInfo> tasks)
    {
        var groups = AdminTasksText.CategoryOrder
            .Select(category => new AdminTaskGroup(category, AdminTasksText.Category(category), null,
                tasks.Where(task => !task.ManualOnly && task.Category == category).ToArray()))
            .Append(new AdminTaskGroup(OnDemandGroup, AdminTasksText.Message("web.admin.tasks.onDemand.label"), AdminTasksText.Message("web.admin.tasks.onDemand.description"),
                tasks.Where(task => task.ManualOnly).ToArray()));
        return groups.Where(group => group.Tasks.Count > 0).ToArray();
    }

    /// <summary>Captures the page owner, leases the tasks channel and reads the list. False when not acting as admin.</summary>
    public bool Open()
    {
        MonitorAuthorityLease lease;
        try { lease = _lifetime.Open(); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
        AdminTasksRealtime? previous;
        long generation;
        lock (_gate)
        {
            if (_disposed) return false;
            previous = ResetLocked();
            _lease = lease; _open = true; _work = new CancellationTokenSource();
            _now = _time.GetUtcNow();
            generation = _generation;
            if (_active) StartTickLocked();
            PublishLocked();
        }
        previous?.Dispose();
        var realtime = new AdminTasksRealtime(_channel, () => Invalidate(generation, metrics: true), signal => OnUpdated(generation, signal));
        lock (_gate)
        {
            if (generation == _generation && _open) _realtime = realtime;
            else realtime.Dispose();
        }
        RequestList(foreground: true);
        Raise();
        return true;
    }

    public bool Reopen() { lock (_gate) if (_disposed) return false; return Open(); }

    /// <summary>Explicit page activation after <see cref="Suspend"/>, before <see cref="Open"/>.</summary>
    public void Resume() { lock (_gate) if (_disposed) return; _lifetime.Resume(); }

    /// <summary>Page departure or outer Admin suspension: retire the owner and clear everything.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        AdminTasksRealtime? realtime;
        lock (_gate) { realtime = ResetLocked(); PublishLocked(); }
        realtime?.Dispose();
        Raise();
    }

    /// <summary>Whether the page and its window are in front (official <c>canApplyRealtimeUpdates</c>). Only then does the clock tick.</summary>
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (!active) StopTickLocked();
            else if (_open)
            {
                _now = _time.GetUtcNow();
                StartTickLocked();
            }
            PublishLocked();
        }
        Raise();
    }

    /// <summary>Explicit retry after a failed first read.</summary>
    public Task RetryAsync()
    {
        RequestList(foreground: true);
        return Task.CompletedTask;
    }

    public Task RunAsync(string key, MonitorAuthorityLease? owner) => CommandAsync(key, owner, run: true);
    public Task StopAsync(string key, MonitorAuthorityLease? owner) => CommandAsync(key, owner, run: false);

    private async Task CommandAsync(string key, MonitorAuthorityLease? owner, bool run)
    {
        MonitorAuthorityLease action;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!_open || _lease is not { IsCurrent: true } lease || !ReferenceEquals(owner, lease) || _work == null) return;
            if (run ? _starting.Contains(key) : _stopping.Contains(key)) return;
            if (!_tasks.Any(task => task.Key == key)) return;
            generation = _generation; token = _work.Token;
        }
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        var started = _time.GetUtcNow();
        lock (_gate)
        {
            if (generation != _generation) { action.Dispose(); return; }
            (run ? _starting : _stopping).Add(key);
            PublishLocked();
        }
        Raise();
        AdminTaskNotice? notice = null;
        var reconcile = false;
        try
        {
            if (run) await _api.RunAsync(action, key, token).ConfigureAwait(false);
            else await _api.CancelAsync(action, key, token).ConfigureAwait(false);
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
            notice = new(AdminTaskNoticeKind.Failed, AdminTasksText.Failure(AdminFailures.Classify(error, action), run, !run));
        }
        catch (Exception) { }
        finally { action.Dispose(); }

        var hold = run ? HoldAsync(started, token) : Task.CompletedTask;
        bool current;
        lock (_gate) current = generation == _generation && _open;
        if (current && notice != null) Notice?.Invoke(notice with { Owner = owner });
        if (current && reconcile) RequestList(foreground: true);
        await hold.ConfigureAwait(false);
        lock (_gate)
        {
            if (generation != _generation) return;
            (run ? _starting : _stopping).Remove(key);
            PublishLocked();
        }
        Raise();
    }

    private async Task HoldAsync(DateTimeOffset started, CancellationToken token)
    {
        var remaining = RunFeedbackMinimum - (_time.GetUtcNow() - started);
        if (remaining <= TimeSpan.Zero) return;
        try { await Task.Delay(remaining, _time, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }


    private void OnUpdated(long generation, AdminTaskSignal signal)
    {
        Invalidate(generation, metrics: signal.Key == null || (signal.Key == AdminTasksApi.MetricsTaskKey && signal.State == AdminTaskStates.Idle));
    }

    private void Invalidate(long generation, bool metrics)
    {
        lock (_gate) if (generation != _generation || !_open) return;
        RequestList(foreground: true);
        if (metrics) RequestMetrics(foreground: true);
    }

    private void RequestList(bool foreground)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!_open || _lease is not { IsCurrent: true } current || _work == null || !_listRead.Request(foreground)) return;
            lease = current; token = _work.Token; generation = _generation;
            if (!_loaded) { PublishLocked(); }
        }
        Raise();
        _ = ReadListAsync(lease, token, generation);
    }

    private async Task ReadListAsync(MonitorAuthorityLease lease, CancellationToken token, long generation)
    {
        while (true)
        {
            IReadOnlyList<AdminTaskInfo>? tasks = null;
            AdminFailureKind? failure = null;
            try { tasks = await _api.ListAsync(lease, token).ConfigureAwait(false); }
            catch (Exception error) { failure = AdminFailures.Classify(error, lease); }
            bool again, metrics = false;
            lock (_gate)
            {
                if (generation != _generation || !lease.IsCurrent) return;
                if (tasks != null)
                {
                    _tasks = tasks; _loaded = true; _failure = null;
                    var listed = tasks.Any(task => task.Key == AdminTasksApi.MetricsTaskKey);
                    if (!listed) _metrics = null;
                    metrics = listed && _metrics == null;
                }
                else if (failure is not AdminFailureKind.Retired) _failure = failure;
                again = _listRead.Complete();
                PublishLocked();
            }
            Raise();
            if (metrics) RequestMetrics(foreground: true);
            if (!again) return;
        }
    }

    private void RequestMetrics(bool foreground)
    {
        MonitorAuthorityLease lease;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (!_open || _lease is not { IsCurrent: true } current || _work == null) return;
            if (!_tasks.Any(task => task.Key == AdminTasksApi.MetricsTaskKey)) return;
            if (!_metricsRead.Request(foreground)) return;
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
            try { metrics = await _api.GetMetricsAsync(lease, AdminTasksApi.MetricsTaskKey, token).ConfigureAwait(false); }
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
        if (_tick != null || !_open) return;
        var generation = _generation;
        _tick = _time.CreateTimer(_ => OnTick(generation), null, TickInterval, TickInterval);
    }

    private void StopTickLocked()
    {
        _tick?.Dispose();
        _tick = null;
    }

    private void OnTick(long generation)
    {
        lock (_gate)
        {
            if (generation != _generation || !_open || !_active) return;
            _now = _time.GetUtcNow();
            PublishLocked();
        }
        Raise();
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

    /// <summary>Retires every read, command, timer and value of the previous owner. Returns the realtime lease to release outside the lock.</summary>
    private AdminTasksRealtime? ResetLocked()
    {
        _generation++;
        _work?.Cancel(); _work?.Dispose(); _work = null;
        StopTickLocked();
        _listRead.Reset(); _metricsRead.Reset();
        _starting.Clear(); _stopping.Clear();
        _tasks = []; _metrics = null; _failure = null; _loaded = false;
        _lease = null; _open = false;
        var realtime = _realtime; _realtime = null;
        return realtime;
    }

    private void PublishLocked()
    {
        var current = _open && _lease is { IsCurrent: true };
        _state = new AdminTasksState
        {
            Owner = _open ? _lease : null,
            IsOpen = _open,
            Loading = current && !_loaded && _listRead.Running,
            Loaded = current && _loaded,
            Failure = current ? _failure : null,
            Tasks = current ? _tasks : [],
            Groups = current ? Group(_tasks) : [],
            Metrics = current ? _metrics : null,
            Now = _now,
            Starting = current ? _starting.ToArray() : [],
            Stopping = current ? _stopping.ToArray() : [],
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
