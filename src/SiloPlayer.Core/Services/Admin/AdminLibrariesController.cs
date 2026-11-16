using System.Collections.Immutable;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Immutable state of the Libraries list for one render.</summary>
public sealed record AdminLibrariesSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    /// <summary>The list has settled at least once for this owner (WebUI shows "Loading…" until then).</summary>
    public bool Loaded { get; init; }
    /// <summary>A list read is in flight (Move up/down wait, as the WebUI's <c>librariesFetching</c>).</summary>
    public bool Fetching { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? ErrorKind { get; init; }
    /// <summary>The shown order (optimistic while a reorder is saving).</summary>
    public IReadOnlyList<AdminLibrary> Libraries { get; init; } = [];
    /// <summary>The complete read the next whole-order write must be checked against; null when truncated or stale.</summary>
    public AdminCompleteSnapshot<AdminLibrary>? Complete { get; init; }
    public bool Truncated { get; init; }
    public AdminLibraryCapabilities? Capabilities { get; init; }
    public AdminLibraryRealtimeMonitoring? Monitoring { get; init; }
    public IReadOnlyDictionary<string, AdminTrickplayLibrary> Trickplay { get; init; } = ImmutableDictionary<string, AdminTrickplayLibrary>.Empty;
    public IReadOnlyList<AdminLibraryJob> RefreshJobs { get; init; } = [];
    public bool RefreshJobsHasMore { get; init; }
    public bool RefreshJobsLoadingMore { get; init; }
    public bool RefreshJobsFailed { get; init; }
    public bool RefreshJobsCapped { get; init; }
    public IReadOnlyList<AdminLibraryScanRun> Scans { get; init; } = [];
    public ImmutableHashSet<string> ScanStarting { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableHashSet<string> ScanCancelling { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableHashSet<string> MountChecking { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableDictionary<string, AdminLibraryMountCheck> MountResults { get; init; } = ImmutableDictionary<string, AdminLibraryMountCheck>.Empty;
    public ImmutableHashSet<string> CleanupPending { get; init; } = ImmutableHashSet<string>.Empty;
    public ImmutableHashSet<string> Deleting { get; init; } = ImmutableHashSet<string>.Empty;
    /// <summary>The library whose metadata refresh request is in flight (the dialog stays up until it settles).</summary>
    public string? RefreshStarting { get; init; }
    public string? JobCancelling { get; init; }
    public bool ScanAllPending { get; init; }
    public bool Reordering { get; init; }

    /// <summary>Whether the whole order can be written now: a complete, unchanged read and no move or read in flight.</summary>
    public bool CanReorder => Owner != null && Complete != null && !Reordering && !Fetching && !Truncated;
}

/// <summary>One library row's derived state (WebUI <c>LibraryRow</c>), shared by the table and the cards.</summary>
public sealed record AdminLibraryRow(
    AdminLibrary Library,
    IReadOnlyList<AdminLibraryScanRun> Scans,
    int RunningScans,
    int QueuedScans,
    AdminLibraryJob? ActiveRefresh,
    AdminLibraryMountCheck? MountResult,
    bool ScanStarting,
    bool ScanCancelling,
    bool MountChecking,
    bool CleanupPending,
    bool RefreshStarting,
    bool JobCancelling,
    AdminLibraryRealtimeMonitoringEntry? Monitoring,
    AdminTrickplayLibrary? Trickplay)
{
    public bool HasActiveWork => ActiveRefresh != null || Scans.Count > 0;
    public bool HasScanWarning => AdminLibraryPresentation.HasScanWarning(Library);
    public bool ScanStops => Scans.Count > 0;
    public bool ScanDisabled => ScanStops ? ScanCancelling : ScanStarting;
    public string ScanLabel => AdminLibrariesText.T(ScanStops ? "web.admin.libraries.list.stopScans" : "web.admin.libraries.list.scanLibrary");
    public bool RefreshStops => ActiveRefresh != null;
    public bool RefreshDisabled => ActiveRefresh != null ? JobCancelling : RefreshStarting;
    public string RefreshLabel => AdminLibrariesText.T(ActiveRefresh != null ? "web.admin.libraries.list.stopRefresh" : "web.admin.libraries.list.rescanMetadata");
    public string? MountMessage => MountResult == null ? null : AdminLibraryPresentation.MountCheckMessage(MountResult);
    public bool CanCleanup => AdminLibraryPresentation.CanConfirmCleanup(Library);
}

/// <summary>A scan-queue popover group (one library's active scans).</summary>
public sealed record AdminScanQueueGroup(string LibraryId, string Name, IReadOnlyList<AdminLibraryScanRun> Scans, int Running, int Queued);

/// <summary>
/// Manage › Libraries › Libraries page owner (WebUI <c>pages/AdminLibraries.tsx</c> with
/// <c>useLibraryRows</c>). Reads the library list (one unpaged, bounded response),
/// capabilities, realtime-monitoring status, seek-preview progress and recent refresh
/// jobs; tracks active scans from the shared <c>scans</c> channel (there is no REST
/// list) and job changes from <c>jobs</c>. Polls only while this page owns an
/// authority: monitoring every 30 s and seek previews every 60 s, as the WebUI.
/// <para>Every command captures an action lease bound to the current owner before
/// it is sent and rechecks it after each await; nothing is replayed. A 202 receipt
/// (scan, refresh, delete) is never shown as completion. A write whose answer was
/// lost is reported as unknown and the page reloads instead of claiming either
/// outcome. Reorder is a whole-order write: it requires the complete current
/// snapshot, runs one at a time, shows the new order at once, restores the previous
/// order on failure, and reloads the list before another move is allowed.</para>
/// </summary>
public sealed class AdminLibrariesController : IDisposable
{
    public static readonly TimeSpan MonitoringInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TrickplayInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MountFeedback = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EventCoalescing = TimeSpan.FromSeconds(1);
    /// <summary>Older refresh-job pages retained at most (500 jobs).</summary>
    public const int MaxRefreshJobPages = 10;

    private readonly AdminLibrariesApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly IAdminRealtimeChannel? _channel;
    private readonly ServerActivityStore? _activity;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private AdminLibrariesSnapshot _state = new();
    private readonly List<string?> _jobCursors = [];
    private string? _nextJobCursor;
    private long _listSequence, _jobSequence, _monitoringSequence, _trickplaySequence;
    private CancellationTokenSource? _listCancellation;
    private IDisposable? _subscription, _monitoringTimer, _trickplayTimer, _libraryEventTimer, _jobEventTimer;
    private readonly Dictionary<string, IDisposable> _mountTimers = new(StringComparer.Ordinal);
    private bool _reloadAfterReorder;
    private bool _disposed;

    public AdminLibrariesController(AdminLibrariesApi api, AdminMonitorAuthority authority, IAdminRealtimeChannel? channel = null, IAdminScheduler? scheduler = null,
        ServerActivityStore? activity = null)
    {
        _api = api;
        _channel = channel;
        _activity = activity;
        _scheduler = scheduler ?? new SystemAdminScheduler();
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
    }

    /// <summary>Raised on any thread after a state change; marshal and read <see cref="State"/>.</summary>
    public event Action? Changed;
    /// <summary>Raised (any thread) after authority retired and data cleared; reopen when admin is still available.</summary>
    public event Action? Retired;
    /// <summary>A command outcome to show as a toast (message, isError). Never raised for a retired owner.</summary>
    public event Action<string, bool>? Notice;
    /// <summary>A finished scan or a scan cancel: the matcher-queue summaries are stale (WebUI invalidates them).</summary>
    public event Action? DiagnosticsStale;

    public AdminLibrariesSnapshot State { get { lock (_gate) return _state; } }
    public AdminLibrariesApi Api => _api;
    public IAdminScheduler Scheduler => _scheduler;
    /// <summary>Translation prewarm jobs started or seen running, reported once when they finish (outlives one editor).</summary>
    public ISet<string> WatchedTranslationJobs { get; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsCurrent(AdminLibrariesSnapshot state)
    {
        lock (_gate) return !_disposed && ReferenceEquals(state, _state) && (state.Owner == null || _lifetime.IsCurrent(state.Owner));
    }

    /// <summary>A lease for one confirmation, write or editor session; it dies with the current owner.</summary>
    public MonitorAuthorityLease CaptureAction(MonitorAuthorityLease? expectedOwner = null)
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = expectedOwner ?? _state.Owner;
            if (!IsOwnerCurrent(owner)) throw new MonitorAuthorityRetiredException();
        }
        // Capture can synchronously retire authority and call back into this controller.
        var action = _lifetime.CaptureAction();
        lock (_gate)
        {
            if (IsOwnerCurrent(owner) && _lifetime.IsCurrent(action)) return action;
        }
        action.Dispose();
        throw new MonitorAuthorityRetiredException();
    }

    public bool IsOwnerCurrent(MonitorAuthorityLease? owner)
    {
        lock (_gate) return !_disposed && owner != null && ReferenceEquals(owner, _state.Owner) && _lifetime.IsCurrent(owner);
    }
    public bool IsActionCurrent(MonitorAuthorityLease? action) => !_disposed && _lifetime.IsCurrent(action);

    // ── Lifecycle ────────────────────────────────────────────────────────

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        IDisposable? previous;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            previous = ResetLocked();
            _state = new AdminLibrariesSnapshot { Owner = owner, Fetching = true };
        }
        // The transport can raise retirement while holding its own lock. Never enter
        // it (including event accessors and lease disposal) while holding _gate.
        previous?.Dispose();
        if (_channel != null)
        {
            var subscription = new RealtimeSubscription(_channel, this, owner);
            bool keep;
            lock (_gate)
            {
                keep = IsOwnerCurrent(owner);
                if (keep) _subscription = subscription;
            }
            if (!keep) { subscription.Dispose(); return; }
        }
        var activity = _activity?.Current;
        lock (_gate)
        {
            if (!IsOwnerCurrent(owner)) return;
            // The shared channel may already be subscribed and send no new snapshot.
            // Seed only from the authority-bound store, never the transport's untagged cache.
            if (activity is { IsActingAdmin: true } && activity.Epoch == owner.Epoch)
                _state = _state with
                {
                    Scans = activity.Scans.Select(scan => new AdminLibraryScanRun(scan.Id, scan.LibraryId, scan.Mode, scan.Path, scan.Status,
                        null, null, scan.Result?.TotalFiles, scan.Result?.FilesProcessed, scan.Result?.Message))
                        .Where(scan => scan.IsActive).Take(500).ToArray(),
                };
        }
        Changed?.Invoke();
        await Task.WhenAll(LoadLibrariesAsync(owner), LoadCapabilitiesAsync(owner), LoadMonitoringAsync(owner), LoadTrickplayAsync(owner),
            LoadRefreshJobsAsync(owner, append: false)).ConfigureAwait(false);
    }

    public void Resume() => _lifetime.Resume();

    /// <summary>Outer Admin departure: retire the owner and clear private data without requesting a reopen.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        Clear();
    }

    /// <summary>Re-reads the list (Retry, after a confirmed or unknown write, a finished scan or job).</summary>
    public Task ReloadAsync()
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return Task.CompletedTask;
            if (_state.Reordering) { _reloadAfterReorder = true; return Task.CompletedTask; }
        }
        return LoadLibrariesAsync(owner);
    }

    private async Task LoadLibrariesAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            sequence = ++_listSequence;
            _listCancellation?.Cancel(); _listCancellation?.Dispose();
            _listCancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            token = _listCancellation.Token;
            _state = _state with { Fetching = true };
        }
        Changed?.Invoke();
        try
        {
            var read = await _api.ListAsync(owner, token).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _listSequence) return;
                if (_state.Reordering) { _reloadAfterReorder = true; _state = _state with { Fetching = false }; return; }
                _state = _state with
                {
                    Loaded = true, Fetching = false, Error = null, ErrorKind = null, Libraries = read.Items, Complete = read.Snapshot,
                    Truncated = !read.IsComplete,
                };
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || !owner.IsCurrent) { }
        catch (Exception error)
        {
            var kind = AdminFailures.Classify(error, owner);
            if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(owner, () =>
            {
                if (sequence != _listSequence) return;
                // A failed refetch keeps the rows already shown but no longer authorizes a whole-order write.
                _state = _state with
                {
                    Loaded = true, Fetching = false, Complete = null,
                    Error = AdminLibrariesText.T("admin.native.libraries.loadFailed", ("error", AdminLibraryPresentation.Describe(error, "admin.native.libraries.transport"))),
                    ErrorKind = kind,
                };
            });
        }
    }

    private async Task LoadCapabilitiesAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var capabilities = await _api.GetCapabilitiesAsync(owner, owner.Token).ConfigureAwait(false);
            Update(owner, () => _state = _state with { Capabilities = capabilities });
        }
        catch (Exception)
        {
            // WebUI: retry false, no UI; the editor then falls back to the library rows' own support flags.
        }
    }

    private async Task LoadMonitoringAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; sequence = ++_monitoringSequence; }
        try
        {
            var monitoring = await _api.GetRealtimeMonitoringAsync(owner, owner.Token).ConfigureAwait(false);
            Update(owner, () => { if (sequence == _monitoringSequence) _state = _state with { Monitoring = monitoring }; });
        }
        catch (Exception) { /* Status badges are best effort; the next poll tries again. */ }
        finally { Arm(owner, ref _monitoringTimer, MonitoringInterval, () => _ = LoadMonitoringAsync(owner)); }
    }

    private async Task LoadTrickplayAsync(MonitorAuthorityLease owner)
    {
        long sequence;
        lock (_gate) { if (_disposed || !_lifetime.IsCurrent(owner)) return; sequence = ++_trickplaySequence; }
        try
        {
            var items = await _api.ListTrickplayAsync(owner, owner.Token).ConfigureAwait(false);
            var map = items.Where(item => AdminLibraryIds.IsId(item.LibraryId)).GroupBy(item => item.LibraryId).ToImmutableDictionary(group => group.Key, group => group.First());
            Update(owner, () => { if (sequence == _trickplaySequence) _state = _state with { Trickplay = map }; });
        }
        catch (Exception)
        {
            // A server without seek previews answers with an error: no badges (WebUI treats it as "no libraries").
            Update(owner, () => { if (sequence == _trickplaySequence) _state = _state with { Trickplay = ImmutableDictionary<string, AdminTrickplayLibrary>.Empty }; });
        }
        finally { Arm(owner, ref _trickplayTimer, TrickplayInterval, () => _ = LoadTrickplayAsync(owner)); }
    }

    private void Arm(MonitorAuthorityLease owner, ref IDisposable? slot, TimeSpan delay, Action callback)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            slot?.Dispose();
            slot = _scheduler.Schedule(delay, () => { if (_lifetime.IsCurrent(owner)) callback(); });
        }
    }

    // ── Refresh jobs (useLibraryRefreshJobs + JobPageControls) ───────────

    /// <summary>"Load older refresh jobs": the next bounded page.</summary>
    public Task LoadOlderJobsAsync()
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            owner = _state.Owner;
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner) || _nextJobCursor == null || _state.RefreshJobsLoadingMore) return Task.CompletedTask;
        }
        return LoadRefreshJobsAsync(owner, append: true);
    }

    /// <summary>"Restart jobs" after a failed page: reads the first page again.</summary>
    public Task RestartJobsAsync()
    {
        MonitorAuthorityLease? owner;
        lock (_gate) { owner = _state.Owner; if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return Task.CompletedTask; }
        return LoadRefreshJobsAsync(owner, append: false);
    }

    private async Task LoadRefreshJobsAsync(MonitorAuthorityLease owner, bool append)
    {
        long sequence;
        string? cursor;
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            if (append && _jobCursors.Count >= MaxRefreshJobPages) { _state = _state with { RefreshJobsCapped = true, RefreshJobsHasMore = false }; return; }
            sequence = ++_jobSequence;
            cursor = append ? _nextJobCursor : null;
            _state = _state with { RefreshJobsLoadingMore = append, RefreshJobsFailed = false };
        }
        Changed?.Invoke();
        try
        {
            var page = await _api.ListRefreshJobsAsync(owner, cursor, owner.Token).ConfigureAwait(false);
            Update(owner, () =>
            {
                if (sequence != _jobSequence) return;
                if (!append) _jobCursors.Clear();
                _jobCursors.Add(cursor);
                _nextJobCursor = page.NextCursor;
                var jobs = append ? _state.RefreshJobs.Concat(page.Items.Where(job => _state.RefreshJobs.All(existing => existing.Id != job.Id))).ToArray() : page.Items.ToArray();
                var capped = page.HasMore && _jobCursors.Count >= MaxRefreshJobPages;
                _state = _state with
                {
                    RefreshJobs = jobs, RefreshJobsHasMore = page.HasMore && !capped, RefreshJobsCapped = capped, RefreshJobsLoadingMore = false, RefreshJobsFailed = false,
                };
            });
        }
        catch (Exception error)
        {
            var kind = AdminFailures.Classify(error, owner);
            if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) return;
            Update(owner, () => { if (sequence == _jobSequence) _state = _state with { RefreshJobsLoadingMore = false, RefreshJobsFailed = true }; });
        }
    }

    // ── Realtime (scans, jobs) ───────────────────────────────────────────

    private void OnSnapshot(MonitorAuthorityLease owner, string channel, JsonElement data)
    {
        if (channel != "scans") return;
        var scans = data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(AdminLibraryPresentation.ParseScan).OfType<AdminLibraryScanRun>().Where(scan => scan.IsActive).Take(500).ToArray()
            : [];
        lock (_gate)
        {
            if (!IsOwnerCurrent(owner)) return;
            _state = _state with { Scans = scans };
        }
        Changed?.Invoke();
    }

    private void OnEvent(MonitorAuthorityLease owner, string channel, string name, JsonElement data)
    {
        if (channel == "scans")
        {
            if (AdminLibraryPresentation.ParseScan(data) is not { } scan) return;
            lock (_gate)
            {
                if (!IsOwnerCurrent(owner)) return;
                var scans = _state.Scans.ToList();
                var index = scans.FindIndex(entry => entry.Id == scan.Id);
                if (index >= 0 && !scan.IsActive) scans.RemoveAt(index);
                else if (index >= 0) scans[index] = scan;
                else if (scan.IsActive && scans.Count < 500) scans.Add(scan);
                _state = _state with { Scans = scans };
            }
            Changed?.Invoke();
            if (name is "scan.completed" or "scan.failed" or "scan.cancelled")
            {
                Coalesce(owner, ref _libraryEventTimer, () => _ = ReloadAsync());
                DiagnosticsStale?.Invoke();
            }
            return;
        }
        if (channel != "jobs" || data.ValueKind != JsonValueKind.Object) return;
        var kind = data.TryGetProperty("job_type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()
            : data.TryGetProperty("kind", out var v2) && v2.ValueKind == JsonValueKind.String ? v2.GetString() : null;
        if (kind is "library_refresh") Coalesce(owner, ref _jobEventTimer, () => _ = RestartJobsAsync());
        if (kind is "delete_library") Coalesce(owner, ref _libraryEventTimer, () => _ = ReloadAsync());
    }

    private void Coalesce(MonitorAuthorityLease owner, ref IDisposable? slot, Action action)
    {
        lock (_gate)
        {
            if (!IsOwnerCurrent(owner) || slot != null) return;
            IDisposable? scheduled = null;
            scheduled = _scheduler.Schedule(EventCoalescing, () =>
            {
                lock (_gate) { if (ReferenceEquals(_libraryEventTimer, scheduled)) _libraryEventTimer = null; if (ReferenceEquals(_jobEventTimer, scheduled)) _jobEventTimer = null; }
                scheduled?.Dispose();
                if (_lifetime.IsCurrent(owner)) action();
            });
            slot = scheduled;
        }
    }

    // ── Rows ─────────────────────────────────────────────────────────────

    /// <summary>The rows in shown order with each library's scans, active refresh, pending commands and status badges.</summary>
    public static IReadOnlyList<AdminLibraryRow> Rows(AdminLibrariesSnapshot state)
    {
        var scansByLibrary = state.Scans.GroupBy(scan => scan.LibraryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<AdminLibraryScanRun>)group.Order(Comparer<AdminLibraryScanRun>.Create(AdminLibraryPresentation.CompareScans)).ToArray(), StringComparer.Ordinal);
        var refreshByLibrary = new Dictionary<string, AdminLibraryJob>(StringComparer.Ordinal);
        foreach (var job in state.RefreshJobs)
            if (AdminLibraryPresentation.IsActiveJob(job) && AdminLibraryPresentation.JobLibraryId(job) is { } library) refreshByLibrary.TryAdd(library, job);
        var monitoring = (state.Monitoring?.Libraries ?? []).GroupBy(entry => entry.LibraryId).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return state.Libraries.Select(library =>
        {
            var scans = scansByLibrary.TryGetValue(library.Id, out var list) ? list : [];
            var running = scans.Count(scan => scan.IsRunning);
            var refresh = refreshByLibrary.GetValueOrDefault(library.Id);
            return new AdminLibraryRow(library, scans, running, scans.Count - running, refresh, state.MountResults.GetValueOrDefault(library.Id),
                state.ScanStarting.Contains(library.Id), state.ScanCancelling.Contains(library.Id), state.MountChecking.Contains(library.Id),
                state.CleanupPending.Contains(library.Id), state.RefreshStarting == library.Id, refresh != null && state.JobCancelling == refresh.Id,
                monitoring.GetValueOrDefault(library.Id), state.Trickplay.GetValueOrDefault(library.Id));
        }).ToArray();
    }

    /// <summary>The scan-queue popover groups: most running scans first, then by library name.</summary>
    public static IReadOnlyList<AdminScanQueueGroup> ScanGroups(AdminLibrariesSnapshot state) =>
        state.Scans.GroupBy(scan => scan.LibraryId, StringComparer.Ordinal).Select(group =>
        {
            var scans = group.Order(Comparer<AdminLibraryScanRun>.Create(AdminLibraryPresentation.CompareScans)).ToArray();
            var name = state.Libraries.FirstOrDefault(library => library.Id == group.Key)?.Name
                ?? AdminLibrariesText.T("web.admin.libraries.scanQueue.libraryFallback", ("id", group.Key));
            var running = scans.Count(scan => scan.IsRunning);
            return new AdminScanQueueGroup(group.Key, name, scans, running, scans.Length - running);
        }).OrderByDescending(group => group.Running).ThenBy(group => group.Name, StringComparer.CurrentCulture).ToArray();

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Scan the library, or stop its scans while any are queued or running.</summary>
    public Task ScanOrStopAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null)
    {
        var state = State;
        expectedOwner ??= state.Owner;
        if (!IsOwnerCurrent(expectedOwner)) return Task.CompletedTask;
        var stop = state.Scans.Any(scan => scan.LibraryId == libraryId);
        return stop ? CancelScansAsync(libraryId, expectedOwner) : StartScanAsync(libraryId, expectedOwner);
    }

    public Task StartScanAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null) => Command(libraryId,
        state => state.ScanStarting.Contains(libraryId), (state, on) => state with { ScanStarting = on ? state.ScanStarting.Add(libraryId) : state.ScanStarting.Remove(libraryId) },
        (lease, ct) => _api.StartScanAsync(lease, libraryId, ct),
        done => Announce(AppStringsOr("web.common.scans.fullIngestStarted", "Full ingest started"), false),
        error => Fail(error, "web.common.scans.scanFailed"), expectedOwner);

    public Task CancelScansAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null) => Command(libraryId,
        state => state.ScanCancelling.Contains(libraryId), (state, on) => state with { ScanCancelling = on ? state.ScanCancelling.Add(libraryId) : state.ScanCancelling.Remove(libraryId) },
        (lease, ct) => _api.CancelScansAsync(lease, libraryId, ct),
        done => { Announce(AppStringsOr("web.common.scans.cancelRequested", "Cancellation requested"), false); DiagnosticsStale?.Invoke(); },
        error => Fail(error, "web.common.scans.cancelFailed"), expectedOwner);

    /// <summary>Queues a quick or full metadata refresh; true when the server accepted it (the dialog then closes).</summary>
    public async Task<bool> RefreshMetadataAsync(string libraryId, string mode, MonitorAuthorityLease? expectedOwner = null)
    {
        var accepted = false;
        await Command(libraryId, state => state.RefreshStarting != null, (state, on) => state with { RefreshStarting = on ? libraryId : null },
            (lease, ct) => _api.RefreshMetadataAsync(lease, libraryId, mode, ct),
            done =>
            {
                accepted = true;
                Announce(AdminLibrariesText.T(mode == "full" ? "web.admin.libraries.toast.fullRefreshQueued" : "web.admin.libraries.toast.refreshQueued"), false);
                _ = RestartJobsAsync();
            },
            error =>
            {
                // A 409 means a refresh is already queued or running: show its message and pick the job up.
                if (error is ApiException { StatusCode: 409 }) { Announce(AdminLibraryPresentation.Describe(error, "web.admin.libraries.toast.refreshFailed"), true); _ = RestartJobsAsync(); return; }
                Fail(error, "web.admin.libraries.toast.refreshFailed");
                if (AdminLibraryPresentation.IsUnknownOutcome(error)) _ = RestartJobsAsync();
            }, expectedOwner).ConfigureAwait(false);
        return accepted;
    }

    public Task CancelJobAsync(string jobId, MonitorAuthorityLease? expectedOwner = null) => Command(jobId, state => state.JobCancelling != null, (state, on) => state with { JobCancelling = on ? jobId : null },
        (lease, ct) => _api.CancelJobAsync(lease, jobId, ct),
        job =>
        {
            Announce(AdminLibrariesText.T("web.admin.libraries.toast.cancellationRequested"), false);
            // The answer may still be running (canceling): show it until the job list says otherwise.
            MonitorAuthorityLease? owner;
            lock (_gate) owner = _state.Owner;
            if (owner != null)
                Update(owner, () => _state = _state with { RefreshJobs = _state.RefreshJobs.Select(existing => existing.Id == job.Id ? Merge(existing, job) : existing).ToArray() });
            _ = RestartJobsAsync();
        },
        error => { Fail(error, "web.admin.libraries.toast.cancelJobFailed"); _ = RestartJobsAsync(); }, expectedOwner);

    private static AdminLibraryJob Merge(AdminLibraryJob listed, AdminLibraryJob answer) => new()
    {
        Id = listed.Id, Kind = listed.Kind, State = answer.State, Terminal = answer.Terminal, Cancelable = answer.Cancelable, CreatedAt = listed.CreatedAt,
        StartedAt = answer.StartedAt ?? listed.StartedAt, FinishedAt = answer.FinishedAt, Progress = answer.Progress ?? listed.Progress, Failure = answer.Failure,
        LibraryId = listed.LibraryId, LibraryName = listed.LibraryName, LibraryIds = listed.LibraryIds, RefreshResult = answer.RefreshResult ?? listed.RefreshResult,
    };

    /// <summary>Checks the library's roots; the result shows on the row for five seconds.</summary>
    public Task CheckMountAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null) => Command(libraryId,
        state => state.MountChecking.Contains(libraryId), (state, on) => state with { MountChecking = on ? state.MountChecking.Add(libraryId) : state.MountChecking.Remove(libraryId) },
        (lease, ct) => _api.CheckMountAsync(lease, libraryId, ct),
        result =>
        {
            MonitorAuthorityLease? owner;
            lock (_gate) owner = _state.Owner;
            if (owner == null) return;
            Update(owner, () =>
            {
                _state = _state with { MountResults = _state.MountResults.SetItem(libraryId, result) };
                if (_mountTimers.Remove(libraryId, out var previous)) previous.Dispose();
                _mountTimers[libraryId] = _scheduler.Schedule(MountFeedback, () =>
                {
                    Update(owner, () =>
                    {
                        if (_mountTimers.Remove(libraryId, out var timer)) timer.Dispose();
                        _state = _state with { MountResults = _state.MountResults.Remove(libraryId) };
                    });
                });
            });
            Announce(AdminLibraryPresentation.MountCheckMessage(result), !result.Healthy);
            _ = ReloadAsync();
        },
        error => Fail(error, "web.admin.libraries.toast.mountCheckFailed"), expectedOwner);

    public Task ConfirmCleanupAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null) => Command(libraryId,
        state => state.CleanupPending.Contains(libraryId), (state, on) => state with { CleanupPending = on ? state.CleanupPending.Add(libraryId) : state.CleanupPending.Remove(libraryId) },
        (lease, ct) => _api.ConfirmEmptyRootCleanupAsync(lease, libraryId, ct),
        done => { Announce(AdminLibrariesText.T("web.admin.libraries.toast.cleanupConfirmed"), false); _ = ReloadAsync(); },
        error => { Fail(error, "web.admin.libraries.toast.cleanupConfirmFailed"); if (AdminLibraryPresentation.IsUnknownOutcome(error)) _ = ReloadAsync(); }, expectedOwner);

    /// <summary>Starts the deletion job. The library stays listed until the server removes it.</summary>
    public Task DeleteAsync(string libraryId, MonitorAuthorityLease? expectedOwner = null) => Command(libraryId,
        state => state.Deleting.Contains(libraryId), (state, on) => state with { Deleting = on ? state.Deleting.Add(libraryId) : state.Deleting.Remove(libraryId) },
        (lease, ct) => _api.DeleteAsync(lease, libraryId, ct),
        done => { Announce(AdminLibrariesText.T("web.admin.libraries.toast.deletionStarted"), false); _ = ReloadAsync(); },
        error => { Fail(error, "web.admin.libraries.toast.deleteFailed"); if (AdminLibraryPresentation.IsUnknownOutcome(error)) _ = ReloadAsync(); }, expectedOwner);

    public Task ScanAllAsync(MonitorAuthorityLease? expectedOwner = null) => Command("scan_libraries", state => state.ScanAllPending, (state, on) => state with { ScanAllPending = on },
        (lease, ct) => _api.ScanAllAsync(lease, ct),
        done => Announce(AdminLibrariesText.T("web.admin.libraries.toast.scanAllStarted"), false),
        error => Fail(error, "web.admin.libraries.toast.scanFailed"), expectedOwner);

    /// <summary>Moves one library a single place (cards' Move up/down).</summary>
    public Task MoveAsync(string libraryId, int direction, MonitorAuthorityLease? expectedOwner = null)
    {
        var state = State;
        expectedOwner ??= state.Owner;
        if (!IsOwnerCurrent(expectedOwner)) return Task.CompletedTask;
        var next = AdminLibraryPresentation.MoveByOne(state.Libraries, library => library.Id, libraryId, direction);
        return next == null ? Task.CompletedTask : ReorderAsync(next, expectedOwner);
    }

    /// <summary>
    /// Writes a new whole order. Refused unless the shown list is the complete,
    /// current read and nothing else is moving or reading; the new order shows at
    /// once and the previous order returns if the server refuses it.
    /// </summary>
    public async Task ReorderAsync(IReadOnlyList<AdminLibrary> order, MonitorAuthorityLease? expectedOwner = null)
    {
        MonitorAuthorityLease action;
        AdminCompleteSnapshot<AdminLibrary> snapshot;
        IReadOnlyList<AdminLibrary> previous;
        lock (_gate)
        {
            expectedOwner ??= _state.Owner;
            if (!IsOwnerCurrent(expectedOwner) || !_state.CanReorder) return;
            snapshot = _state.Complete!;
            previous = _state.Libraries;
        }
        try { action = CaptureAction(expectedOwner); }
        catch (OperationCanceledException) { return; }
        lock (_gate)
        {
            if (!IsOwnerCurrent(expectedOwner) || !_state.CanReorder || !ReferenceEquals(_state.Complete, snapshot) || !snapshot.IsCurrentFor(action, AdminLibrariesApi.LibraryScope))
            {
                action.Dispose();
                if (!_disposed && _state.Owner != null) Notice?.Invoke(AdminLibrariesText.T("admin.native.libraries.reorderUnavailable"), true);
                return;
            }
            _state = _state with { Libraries = order, Reordering = true };
        }
        Changed?.Invoke();
        var ids = order.Select(library => library.Id).ToArray();
        try
        {
            await _api.ReorderAsync(action, snapshot, ids, action.Token).ConfigureAwait(false);
            if (!_lifetime.IsCurrent(action)) return;
            // The saved order is the server's now; the old snapshot no longer authorizes another write.
            Update(action, () => _state = _state with { Reordering = false, Complete = null });
            _reloadAfterReorder = false;
            _ = ReloadAsync();
        }
        catch (Exception error)
        {
            if (AdminFailures.Classify(error, action) is AdminFailureKind.Retired || !_lifetime.IsCurrent(action)) return;
            var unknown = AdminLibraryPresentation.IsUnknownOutcome(error);
            Update(action, () => _state = _state with { Reordering = false, Libraries = unknown ? _state.Libraries : previous, Complete = unknown ? null : _state.Complete });
            Announce(unknown ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
                : AdminLibraryPresentation.Describe(error, "web.admin.libraries.toast.reorderFailed"), true);
            var reload = unknown || _reloadAfterReorder;
            _reloadAfterReorder = false;
            if (reload) _ = ReloadAsync();
        }
        finally { action.Dispose(); }
    }

    /// <summary>
    /// Runs one row command once under an action lease: refused while the same
    /// command is pending; the pending flag clears only when the request settles.
    /// </summary>
    private async Task Command<T>(string key, Func<AdminLibrariesSnapshot, bool> pending, Func<AdminLibrariesSnapshot, bool, AdminLibrariesSnapshot> mark,
        Func<MonitorAuthorityLease, CancellationToken, Task<T>> send, Action<T> succeeded, Action<Exception> failed, MonitorAuthorityLease? expectedOwner)
    {
        MonitorAuthorityLease action;
        lock (_gate)
        {
            expectedOwner ??= _state.Owner;
            if (!IsOwnerCurrent(expectedOwner) || pending(_state)) return;
        }
        try { action = CaptureAction(expectedOwner); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!IsOwnerCurrent(expectedOwner) || !_lifetime.IsCurrent(action) || pending(_state)) return;
                _state = mark(_state, true);
            }
            Changed?.Invoke();
            try
            {
                var result = await send(action, action.Token).ConfigureAwait(false);
                if (!_lifetime.IsCurrent(action)) return;
                Update(action, () => _state = mark(_state, false));
                succeeded(result);
            }
            catch (Exception error)
            {
                if (!_lifetime.IsCurrent(action) || AdminFailures.Classify(error, action) is AdminFailureKind.Retired) return;
                Update(action, () => _state = mark(_state, false));
                failed(error);
            }
            _ = key;
        }
    }

    private void Fail(Exception error, string fallbackKey) =>
        Announce(AdminLibraryPresentation.IsUnknownOutcome(error) ? AdminLibrariesText.T("admin.native.libraries.outcomeUnknown")
            : AdminLibraryPresentation.Describe(error, fallbackKey), true);

    private static string AppStringsOr(string key, string fallback) => Localization.AppStrings.Get(key, fallback: fallback);

    internal void Announce(string message, bool error)
    {
        MonitorAuthorityLease? owner;
        lock (_gate) owner = _state.Owner;
        if (!_disposed && owner != null && _lifetime.IsCurrent(owner)) Notice?.Invoke(message, error);
    }

    /// <summary>A confirmed editor save: the list (and, for seek previews, their status) is stale.</summary>
    public void AfterLibrarySaved(MonitorAuthorityLease action, bool trickplayChanged)
    {
        MonitorAuthorityLease? owner;
        lock (_gate) owner = _state.Owner;
        if (owner == null || !_lifetime.IsCurrent(action)) return;
        _ = ReloadAsync();
        if (trickplayChanged) _ = LoadTrickplayAsync(owner);
        _ = LoadMonitoringAsync(owner);
    }

    // ── Plumbing ─────────────────────────────────────────────────────────

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
        }
        Changed?.Invoke();
    }

    private void OnRetired()
    {
        Clear();
        Retired?.Invoke();
    }

    private IDisposable? ResetLocked()
    {
        _listSequence++; _jobSequence++; _monitoringSequence++; _trickplaySequence++;
        _listCancellation?.Cancel(); _listCancellation?.Dispose(); _listCancellation = null;
        foreach (var timer in new[] { _monitoringTimer, _trickplayTimer, _libraryEventTimer, _jobEventTimer }) timer?.Dispose();
        _monitoringTimer = _trickplayTimer = _libraryEventTimer = _jobEventTimer = null;
        foreach (var timer in _mountTimers.Values) timer.Dispose();
        _mountTimers.Clear();
        var subscription = _subscription;
        _subscription = null;
        _jobCursors.Clear(); _nextJobCursor = null; _reloadAfterReorder = false;
        lock (WatchedTranslationJobs) WatchedTranslationJobs.Clear();
        return subscription;
    }

    private void Clear()
    {
        IDisposable? subscription;
        lock (_gate)
        {
            subscription = ResetLocked();
            _state = new AdminLibrariesSnapshot();
        }
        subscription?.Dispose();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose();
        IDisposable? subscription;
        lock (_gate) { subscription = ResetLocked(); _state = new AdminLibrariesSnapshot(); }
        subscription?.Dispose();
        Changed = null; Retired = null; Notice = null; DiagnosticsStale = null;
    }

    /// <summary>One open owner's handlers and channel lease, acquired/released outside _gate.</summary>
    private sealed class RealtimeSubscription : IDisposable
    {
        private readonly IAdminRealtimeChannel _channel;
        private readonly Action<string, JsonElement> _snapshot;
        private readonly Action<string, string, JsonElement> _event;
        private IDisposable? _handle;
        private int _disposed;

        public RealtimeSubscription(IAdminRealtimeChannel channel, AdminLibrariesController controller, MonitorAuthorityLease owner)
        {
            _channel = channel;
            _snapshot = (name, data) => controller.OnSnapshot(owner, name, data);
            _event = (name, kind, data) => controller.OnEvent(owner, name, kind, data);
            try
            {
                channel.SnapshotReceived += _snapshot;
                channel.EventReceived += _event;
                _handle = channel.Subscribe("scans", "jobs");
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
