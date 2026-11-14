using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The shared realtime transport the store leases admin channels on (adapter over <see cref="EventChannelClient"/>).</summary>
public interface IAdminRealtimeChannel
{
    IDisposable Subscribe(params string[] channels);
    event Action<string, JsonElement>? SnapshotReceived;
    event Action<string, string, JsonElement>? EventReceived;
    event Action<WebSocketState>? StateChanged;
    WebSocketState CurrentState { get; }
    bool TryGetLatestSnapshot(string channel, out JsonElement snapshot);
}

/// <summary>Uses the one shared <see cref="EventChannelClient"/>; never stops it, only releases its own channel lease.</summary>
public sealed class EventChannelAdminRealtime(EventChannelClient events) : IAdminRealtimeChannel
{
    public IDisposable Subscribe(params string[] channels) => events.Subscribe(channels);
    public event Action<string, JsonElement>? SnapshotReceived { add => events.SnapshotReceived += value; remove => events.SnapshotReceived -= value; }
    public event Action<string, string, JsonElement>? EventReceived { add => events.EventReceived += value; remove => events.EventReceived -= value; }
    public event Action<WebSocketState>? StateChanged { add => events.StateChanged += value; remove => events.StateChanged -= value; }
    public WebSocketState CurrentState => events.CurrentState;
    public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) => events.TryGetLatestSnapshot(channel, out snapshot);
}

/// <summary>Clock and one-shot timers, replaceable in tests.</summary>
public interface IAdminScheduler
{
    DateTimeOffset UtcNow { get; }
    IDisposable Schedule(TimeSpan delay, Action callback);
}

public sealed class SystemAdminScheduler : IAdminScheduler
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new Timer(_ => callback());
        timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        return timer;
    }
}

/// <summary>Immutable view of the shared admin activity data. Sessions are also the Activity page's list.</summary>
public sealed record ServerActivitySnapshot(
    long Epoch,
    long Version,
    bool IsActingAdmin,
    IReadOnlyList<AdminActivitySession> Sessions,
    bool SessionsLoaded,
    string? SessionsError,
    IReadOnlyList<AdminIndicatorTask> Tasks,
    IReadOnlyList<AdminIndicatorScanRun> Scans,
    bool ScansLoaded,
    IReadOnlyDictionary<string, string> LibraryNames,
    AdminIndicatorPreparationList? Preparations,
    AdminRealtimeConnectionState Connection,
    bool ConnectionProblem)
{
    public static ServerActivitySnapshot Empty { get; } = new(0, 0, false, [], false, null, [], [], false,
        new Dictionary<string, string>(), null, AdminRealtimeConnectionState.Connecting, false);
}

/// <summary>
/// The app-lifetime owner of admin activity data, shared by every indicator
/// instance, the admin sidebar's live badge and the Activity page (as the
/// WebUI shares one query cache). While the principal acts as admin it leases
/// the admin channels <c>jobs, sessions, tasks, scans, settings,
/// download_preparations</c> once on the shared event client (ordinary
/// channels are untouched) and keeps them until authority is lost.
/// <para>Reads are demand-driven (<see cref="AcquireReads"/>): sessions, hidden-
/// inclusive tasks, libraries and the preparation queue. Events refresh with a
/// leading+trailing 5 s coalescing window, scans hydrate from the channel only,
/// preparation progress patches running rows in place, a fixed 60 s maintenance
/// read runs while preparations are active (not reset by patches) plus one read
/// when the oldest failure ages out of 24 h (+1 s; 60 s if already expired).
/// A non-live connection shows the problem dot only after 4 continuous seconds.</para>
/// <para>Every read runs under a <see cref="MonitorAuthorityLease"/> and a per-
/// resource version; retirement clears all data, timers and the channel lease,
/// and a late completion from a retired or superseded read is dropped.</para>
/// </summary>
public sealed class ServerActivityStore : IDisposable
{
    public static readonly string[] AdminChannels = ["jobs", "sessions", "tasks", "scans", "settings", "download_preparations"];
    public static readonly TimeSpan SessionsStaleAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LibrariesStaleAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PreparationsStaleAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan EventCoalescing = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PreparationMaintenance = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan PreparationFailedWindow = TimeSpan.FromHours(24);

    private enum Resource { Sessions, Tasks, Libraries, Preparations }

    private sealed class ResourceState
    {
        public long Version;
        public DateTimeOffset? FetchedAt;
        public bool Stale = true;
        public Task? InFlight;
        public DateTimeOffset? LastEventRefresh;
        public IDisposable? Trailing;
    }

    private readonly AdminMonitorApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly IAdminRealtimeChannel _channel;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private readonly Dictionary<Resource, ResourceState> _resources = Enum.GetValues<Resource>().ToDictionary(r => r, _ => new ResourceState());
    private MonitorAuthorityLease? _lease;
    private IDisposable? _subscription;
    private int _demand;
    private bool _disposed;
    private long _version;

    private IReadOnlyList<AdminActivitySession> _sessions = [];
    private bool _sessionsLoaded;
    private string? _sessionsError;
    private IReadOnlyList<AdminIndicatorTask> _tasks = [];
    private List<AdminIndicatorScanRun> _scans = [];
    private bool _scansLoaded;
    private IReadOnlyDictionary<string, string> _libraries = new Dictionary<string, string>();
    private AdminIndicatorPreparationList? _preparations;
    private AdminRealtimeConnectionState _connection = AdminRealtimeConnectionState.Connecting;
    private bool _connectionProblem;
    private IDisposable? _problemTimer;
    private IDisposable? _maintenanceTimer;
    private IDisposable? _failureTimer;

    public ServerActivityStore(AdminMonitorApi api, AdminMonitorAuthority authority, IAdminRealtimeChannel channel, IAdminScheduler? scheduler = null)
    {
        _api = api;
        _authority = authority;
        _channel = channel;
        _scheduler = scheduler ?? new SystemAdminScheduler();
        _authority.AvailabilityChanged += OnAvailabilityChanged;
        _channel.SnapshotReceived += OnSnapshot;
        _channel.EventReceived += OnEvent;
        _channel.StateChanged += OnStateChanged;
        Synchronize();
    }

    /// <summary>Raised after every published change, on any thread. UI marshals to its dispatcher.</summary>
    public event Action<ServerActivitySnapshot>? Changed;

    public ServerActivitySnapshot Current { get { lock (_gate) return SnapshotNoLock(); } }

    /// <summary>
    /// A consumer that needs the data (an indicator host whose trigger may be
    /// hidden, the admin sidebar, the Activity page). Dispose to release.
    /// </summary>
    public IDisposable AcquireReads()
    {
        bool first;
        lock (_gate)
        {
            if (_disposed) return new Release(null);
            first = ++_demand == 1;
        }
        if (first) RefreshStale();
        return new Release(this);
    }

    private sealed class Release(ServerActivityStore? owner) : IDisposable
    {
        private ServerActivityStore? _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            lock (owner._gate) owner._demand = Math.Max(0, owner._demand - 1);
        }
    }

    /// <summary>Re-reads sessions now (Activity Refresh); completes when the read has published or failed.</summary>
    public Task RefreshSessionsAsync() => StartRead(Resource.Sessions, force: true);

    /// <summary>Re-reads every resource, as returning to a visible Activity page does.</summary>
    public Task RefreshAllAsync() => Task.WhenAll(Enum.GetValues<Resource>().Select(resource => StartRead(resource, force: true)));

    private void OnAvailabilityChanged() => Synchronize();

    /// <summary>Retires everything owned under the previous authority, then re-leases if still acting admin.</summary>
    private void Synchronize()
    {
        IDisposable? oldSubscription;
        MonitorAuthorityLease? oldLease;
        List<IDisposable?> timers;
        lock (_gate)
        {
            if (_disposed) return;
            oldSubscription = _subscription;
            oldLease = _lease;
            _subscription = null;
            _lease = null;
            timers = [_problemTimer, _maintenanceTimer, _failureTimer, .. _resources.Values.Select(r => r.Trailing)];
            _problemTimer = _maintenanceTimer = _failureTimer = null;
            foreach (var resource in _resources.Values)
            {
                resource.Version++;
                resource.FetchedAt = null;
                resource.Stale = true;
                resource.InFlight = null;
                resource.LastEventRefresh = null;
                resource.Trailing = null;
            }
            _sessions = [];
            _sessionsLoaded = false;
            _sessionsError = null;
            _tasks = [];
            _scans = [];
            _scansLoaded = false;
            _libraries = new Dictionary<string, string>();
            _preparations = null;
            _connectionProblem = false;
            _connection = AdminRealtimeConnectionState.Connecting;
            _version++;
        }
        foreach (var timer in timers) timer?.Dispose();
        oldLease?.Dispose();

        MonitorAuthorityLease? lease = null;
        if (_authority.IsAvailable)
        {
            try { lease = _authority.Capture(); }
            catch (MonitorAuthorityRetiredException) { }
            catch (ObjectDisposedException) { }
        }
        if (lease != null)
        {
            var subscription = _channel.Subscribe(AdminChannels);
            bool keep;
            lock (_gate)
            {
                // A nested retirement during Capture may already have re-leased.
                keep = !_disposed && lease.IsCurrent && _lease == null;
                if (keep)
                {
                    _lease = lease;
                    _subscription = subscription;
                    _connection = MapState(_channel.CurrentState);
                    // Cached transport snapshots have no authority identity. Wait for a fresh delivery.
                    _version++;
                }
            }
            if (!keep)
            {
                subscription.Dispose();
                lease.Dispose();
            }
            else
            {
                if (_connection != AdminRealtimeConnectionState.Live) ArmProblemTimer();
                int demand;
                lock (_gate) demand = _demand;
                if (demand > 0) RefreshStale();
            }
        }
        // Released after the new lease subscribed, so a same-authority refresh does not drop the channels.
        oldSubscription?.Dispose();
        Publish();
    }

    private static AdminRealtimeConnectionState MapState(WebSocketState state) => state switch
    {
        WebSocketState.Open => AdminRealtimeConnectionState.Live,
        WebSocketState.Connecting or WebSocketState.None => AdminRealtimeConnectionState.Connecting,
        _ => AdminRealtimeConnectionState.Disconnected,
    };

    private void OnStateChanged(WebSocketState state)
    {
        var mapped = MapState(state);
        bool arm, changed;
        lock (_gate)
        {
            if (_lease == null || _disposed) return;
            var wasNonLive = _connection != AdminRealtimeConnectionState.Live;
            changed = _connection != mapped;
            _connection = mapped;
            if (mapped == AdminRealtimeConnectionState.Live)
            {
                _problemTimer?.Dispose();
                _problemTimer = null;
                changed |= _connectionProblem;
                _connectionProblem = false;
                arm = false;
            }
            else
            {
                // Only the transition into non-live starts the 4 s clock; staying non-live keeps it.
                arm = !wasNonLive || (_problemTimer == null && !_connectionProblem);
            }
            if (changed) _version++;
        }
        if (arm) ArmProblemTimer();
        if (changed) Publish();
    }

    private void ArmProblemTimer()
    {
        MonitorAuthorityLease? lease;
        lock (_gate) lease = _lease;
        if (lease == null) return;
        var timer = _scheduler.Schedule(ServerActivityPresentation.ConnectionProblemDelay, () =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_lease, lease) || _connection == AdminRealtimeConnectionState.Live || _connectionProblem) return;
                _connectionProblem = true;
                _version++;
            }
            Publish();
        });
        lock (_gate)
        {
            if (!ReferenceEquals(_lease, lease)) { timer.Dispose(); return; }
            _problemTimer?.Dispose();
            _problemTimer = timer;
        }
    }

    private void OnSnapshot(string channel, JsonElement data)
    {
        switch (channel)
        {
            case "sessions": EventRefresh(Resource.Sessions); break;
            case "tasks": EventRefresh(Resource.Tasks); break;
            case "download_preparations": EventRefresh(Resource.Preparations); break;
            case "scans":
                lock (_gate)
                {
                    if (_lease == null) return;
                    _scans = AdminMonitorApi.ParseIndicatorScanRuns(data).ToList();
                    _scansLoaded = true;
                    _version++;
                }
                Publish();
                break;
        }
    }

    private void OnEvent(string channel, string name, JsonElement data)
    {
        switch (channel)
        {
            case "sessions" when name == "sessions.replaced":
                EventRefresh(Resource.Sessions);
                break;
            case "tasks" when name == "task.updated":
                EventRefresh(Resource.Tasks);
                break;
            case "scans":
                if (AdminMonitorApi.ParseIndicatorScanRun(data) is not { } scan) return;
                lock (_gate)
                {
                    if (_lease == null) return;
                    var index = _scans.FindIndex(entry => entry.Id == scan.Id);
                    var active = ServerActivityPresentation.IsActiveScan(scan);
                    if (index >= 0 && !active) _scans.RemoveAt(index);
                    else if (index >= 0) _scans[index] = scan;
                    else if (active) _scans.Add(scan);
                    _version++;
                }
                Publish();
                if (name is "scan.completed" or "scan.failed" or "scan.cancelled") EventRefresh(Resource.Libraries);
                break;
            case "download_preparations":
                if (name == "download_storage.changed") return;
                if (name == "download_preparation.progress" && TryPatchProgress(data)) return;
                EventRefresh(Resource.Preparations);
                break;
        }
    }

    private bool TryPatchProgress(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String || !data.TryGetProperty("progress", out var progressElement) ||
            progressElement.ValueKind != JsonValueKind.Object)
            return false;
        AdminIndicatorPreparationProgress? progress;
        try
        {
            if (!progressElement.TryGetProperty("encoded_seconds", out var encoded) || encoded.ValueKind != JsonValueKind.Number ||
                !progressElement.TryGetProperty("duration_seconds", out var duration) || duration.ValueKind != JsonValueKind.Number ||
                !progressElement.TryGetProperty("speed", out var speed) || speed.ValueKind != JsonValueKind.Number ||
                !progressElement.TryGetProperty("updated_at", out var updated) || updated.ValueKind != JsonValueKind.String)
                return false;
            progress = new AdminIndicatorPreparationProgress
            {
                EncodedSeconds = encoded.GetDouble(),
                DurationSeconds = duration.GetDouble(),
                Speed = speed.GetDouble(),
                UpdatedAt = updated.GetString() ?? "",
            };
        }
        catch (FormatException) { return false; }
        var id = idElement.GetString();
        lock (_gate)
        {
            if (_lease == null || _preparations == null) return false;
            var current = _preparations.Items.FirstOrDefault(item => item.Id == id);
            if (current == null || current.State != "running") return false;
            var items = _preparations.Items.Select(item => item.Id == id ? CloneWithProgress(item, progress) : item).ToList();
            _preparations = new AdminIndicatorPreparationList { Counts = _preparations.Counts, Items = items };
            _version++;
        }
        Publish();
        return true;
    }

    private static AdminIndicatorPreparation CloneWithProgress(AdminIndicatorPreparation p, AdminIndicatorPreparationProgress progress) => new()
    {
        Id = p.Id, State = p.State, Format = p.Format, MediaFileId = p.MediaFileId, MediaTitle = p.MediaTitle, MediaType = p.MediaType,
        SeriesName = p.SeriesName, EpisodeName = p.EpisodeName, SeasonNumber = p.SeasonNumber, EpisodeNumber = p.EpisodeNumber,
        FailedAt = p.FailedAt, Output = p.Output, Progress = progress, ProgressUnavailable = false, Worker = p.Worker,
    };

    /// <summary>Leading + trailing 5 s coalescing; with no consumer the resource is only marked stale.</summary>
    private void EventRefresh(Resource resource)
    {
        bool startNow = false;
        TimeSpan? trailingDelay = null;
        lock (_gate)
        {
            if (_lease == null || _disposed) return;
            var state = _resources[resource];
            state.Stale = true;
            if (_demand == 0) return;
            var now = _scheduler.UtcNow;
            if (state.LastEventRefresh is not { } last || now - last >= EventCoalescing)
            {
                state.LastEventRefresh = now;
                startNow = true;
            }
            else if (state.Trailing == null)
            {
                trailingDelay = EventCoalescing - (now - last);
            }
        }
        if (startNow) _ = StartRead(resource, force: true);
        else if (trailingDelay is { } delay) ScheduleTrailing(resource, delay);
    }

    private void ScheduleTrailing(Resource resource, TimeSpan delay)
    {
        lock (_gate)
        {
            var lease = _lease;
            var state = _resources[resource];
            if (lease == null || _disposed || state.Trailing != null) return;
            // Publish the handle before a concurrent callback can take the gate.
            // A scheduler may also invoke inline; never retain that fired handle.
            var fired = false;
            var timer = _scheduler.Schedule(delay, () =>
            {
                lock (_gate)
                {
                    fired = true;
                    if (!ReferenceEquals(_lease, lease)) return;
                    state.Trailing?.Dispose();
                    state.Trailing = null;
                    state.LastEventRefresh = _scheduler.UtcNow;
                }
                _ = StartRead(resource, force: true);
            });
            if (fired) timer.Dispose();
            else state.Trailing = timer;
        }
    }

    private void RefreshStale()
    {
        foreach (var resource in Enum.GetValues<Resource>()) _ = StartRead(resource, force: false);
    }

    private bool IsFreshNoLock(Resource resource)
    {
        var state = _resources[resource];
        if (state.Stale || state.FetchedAt is not { } fetched) return false;
        var age = _scheduler.UtcNow - fetched;
        return resource switch
        {
            Resource.Sessions => age < SessionsStaleAfter,
            Resource.Libraries => age < LibrariesStaleAfter,
            Resource.Preparations => age < PreparationsStaleAfter,
            _ => false, // tasks: stale immediately, as staleTime 0
        };
    }

    private Task StartRead(Resource resource, bool force)
    {
        MonitorAuthorityLease? lease;
        long version;
        lock (_gate)
        {
            lease = _lease;
            if (lease == null || _disposed) return Task.CompletedTask;
            var state = _resources[resource];
            if (!force && IsFreshNoLock(resource)) return Task.CompletedTask;
            // A newer read supersedes an older one; the older result is dropped.
            if (!force && state.InFlight is { IsCompleted: false } running) return running;
            version = ++state.Version;
            var task = ReadAsync(resource, lease, version);
            state.InFlight = task;
            return task;
        }
    }

    private async Task ReadAsync(Resource resource, MonitorAuthorityLease lease, long version)
    {
        await Task.Yield();
        try
        {
            switch (resource)
            {
                case Resource.Sessions:
                {
                    var sessions = await _api.ListActivitySessionsAsync(lease).ConfigureAwait(false);
                    Apply(resource, lease, version, () => { _sessions = sessions; _sessionsLoaded = true; _sessionsError = null; });
                    break;
                }
                case Resource.Tasks:
                {
                    var tasks = await _api.ListIndicatorTasksAsync(lease).ConfigureAwait(false);
                    Apply(resource, lease, version, () => _tasks = tasks);
                    break;
                }
                case Resource.Libraries:
                {
                    var libraries = await _api.ListIndicatorLibrariesAsync(lease).ConfigureAwait(false);
                    var names = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var library in libraries) names[library.Id] = library.Name;
                    Apply(resource, lease, version, () => _libraries = names);
                    break;
                }
                case Resource.Preparations:
                {
                    var list = await _api.ListIndicatorPreparationsAsync(lease).ConfigureAwait(false);
                    if (Apply(resource, lease, version, () => _preparations = list)) ArmPreparationTimers(lease);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!lease.IsCurrent)
        {
            // Retired authority: silently dropped, never an error.
        }
        catch (Exception error)
        {
            var applied = Apply(resource, lease, version, () =>
            {
                if (resource == Resource.Sessions) { _sessionsLoaded = true; _sessionsError = error.Message; }
            }, markFresh: false);
            if (applied && resource == Resource.Preparations) ArmPreparationTimers(lease);
            LocalLog.AppendLine("admin_monitor.txt", $"activity read {resource} failed: {error.GetType().Name}");
        }
    }

    /// <summary>Publishes a result only for the current lease and the newest read of that resource.</summary>
    private bool Apply(Resource resource, MonitorAuthorityLease lease, long version, Action apply, bool markFresh = true)
    {
        lock (_gate)
        {
            var state = _resources[resource];
            if (_disposed || !ReferenceEquals(_lease, lease) || !lease.IsCurrent || state.Version != version) return false;
            apply();
            if (markFresh)
            {
                state.FetchedAt = _scheduler.UtcNow;
                state.Stale = false;
            }
            _version++;
        }
        Publish();
        return true;
    }

    /// <summary>
    /// The fixed 60 s maintenance read while running+queued+retrying &gt; 0 (created on
    /// the transition, never restarted by progress patches), and the failure-expiry read.
    /// </summary>
    private void ArmPreparationTimers(MonitorAuthorityLease lease)
    {
        AdminIndicatorPreparationList? list;
        bool needMaintenance, haveMaintenance;
        lock (_gate)
        {
            if (!ReferenceEquals(_lease, lease)) return;
            list = _preparations;
            needMaintenance = ServerActivityPresentation.ActivePreparationCount(list?.Counts) > 0;
            haveMaintenance = _maintenanceTimer != null;
            if (!needMaintenance && haveMaintenance)
            {
                _maintenanceTimer!.Dispose();
                _maintenanceTimer = null;
            }
            _failureTimer?.Dispose();
            _failureTimer = null;
        }
        if (needMaintenance && !haveMaintenance) ScheduleMaintenance(lease);

        DateTimeOffset? next = null;
        foreach (var item in list?.Items ?? [])
        {
            if (item.State != "failed" || !DateTimeOffset.TryParse(item.FailedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var failedAt)) continue;
            var expiry = failedAt + PreparationFailedWindow;
            if (next == null || expiry < next) next = expiry;
        }
        if (next is not { } expiryAt) return;
        var remaining = expiryAt - _scheduler.UtcNow;
        var delay = remaining > TimeSpan.Zero ? remaining + TimeSpan.FromSeconds(1) : PreparationMaintenance;
        var timer = _scheduler.Schedule(delay, () =>
        {
            lock (_gate) if (!ReferenceEquals(_lease, lease)) return;
            _ = StartRead(Resource.Preparations, force: true);
        });
        lock (_gate)
        {
            if (!ReferenceEquals(_lease, lease)) { timer.Dispose(); return; }
            _failureTimer?.Dispose();
            _failureTimer = timer;
        }
    }

    private void ScheduleMaintenance(MonitorAuthorityLease lease)
    {
        var timer = _scheduler.Schedule(PreparationMaintenance, () =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_lease, lease)) return;
                _maintenanceTimer?.Dispose();
                _maintenanceTimer = null;
                if (ServerActivityPresentation.ActivePreparationCount(_preparations?.Counts) == 0) return;
            }
            ScheduleMaintenance(lease);
            _ = StartRead(Resource.Preparations, force: true);
        });
        lock (_gate)
        {
            if (!ReferenceEquals(_lease, lease) || _maintenanceTimer != null) { timer.Dispose(); return; }
            _maintenanceTimer = timer;
        }
    }

    private ServerActivitySnapshot SnapshotNoLock() => _lease?.IsCurrent != true
        ? ServerActivitySnapshot.Empty with { Epoch = _authority.Epoch, Version = _version }
        : new(
        _lease?.Epoch ?? _authority.Epoch,
        _version,
        _lease != null,
        _sessions,
        _sessionsLoaded,
        _sessionsError,
        _tasks,
        _scans.ToArray(),
        _scansLoaded,
        _libraries,
        _preparations,
        _connection,
        _connectionProblem);

    private void Publish()
    {
        ServerActivitySnapshot snapshot;
        lock (_gate)
        {
            if (_disposed) return;
            snapshot = SnapshotNoLock();
        }
        foreach (var handler in Changed?.GetInvocationList() ?? [])
        {
            try { ((Action<ServerActivitySnapshot>)handler)(snapshot); }
            catch (Exception ex) { LocalLog.AppendLine("admin_monitor.txt", $"activity subscriber failed: {ex.GetType().Name}"); }
        }
    }

    public void Dispose()
    {
        IDisposable? subscription;
        MonitorAuthorityLease? lease;
        List<IDisposable?> timers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            subscription = _subscription;
            lease = _lease;
            _subscription = null;
            _lease = null;
            timers = [_problemTimer, _maintenanceTimer, _failureTimer, .. _resources.Values.Select(r => r.Trailing)];
        }
        _authority.AvailabilityChanged -= OnAvailabilityChanged;
        _channel.SnapshotReceived -= OnSnapshot;
        _channel.EventReceived -= OnEvent;
        _channel.StateChanged -= OnStateChanged;
        foreach (var timer in timers) timer?.Dispose();
        lease?.Dispose();
        subscription?.Dispose();
        Changed = null;
    }
}
