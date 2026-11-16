using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminNetworkAccessCommandKind { Connect, Disconnect }
public enum AdminNetworkAccessCommandOutcome { Rejected, Unknown }

/// <summary>A command that did not take effect (or may not have). Holds no server or plugin text.</summary>
public sealed record AdminNetworkAccessCommandFeedback(string Provider, string HostId, AdminNetworkAccessCommandKind Kind,
    AdminNetworkAccessCommandOutcome Outcome, AdminFailureKind? Failure = null)
{
    /// <summary>The stable page owner that produced this feedback, retained through UI dispatch.</summary>
    public MonitorAuthorityLease? Owner { get; init; }
}

/// <summary>One installed provider's group: its status read and the hosts with a command in flight.</summary>
public sealed record AdminNetworkAccessProviderState(AdminNetworkAccessProvider Summary)
{
    /// <summary>No status has been read yet and a read is pending or running (official isLoading).</summary>
    public bool Loading { get; init; }
    public AdminNetworkAccessStatus? Status { get; init; }
    /// <summary>The latest read failed; the group shows its error in place of the hosts (official isError).</summary>
    public AdminFailureKind? Failure { get; init; }
    /// <summary>Set with <see cref="Failure"/> when the response exceeded a list bound.</summary>
    public int? Limit { get; init; }
    public IReadOnlyCollection<string> BusyHosts { get; init; } = [];
    public override string ToString() => $"{nameof(AdminNetworkAccessProviderState)} {{ Provider = {Summary.Provider} }}";
}

public sealed record AdminNetworkAccessState
{
    public static AdminNetworkAccessState Closed { get; } = new();
    public MonitorAuthorityLease? Owner { get; init; }
    public bool IsOpen { get; init; }
    public bool CapabilitiesLoading { get; init; }
    public bool CapabilitiesLoaded { get; init; }
    public AdminFailureKind? CapabilitiesFailure { get; init; }
    public int? CapabilitiesLimit { get; init; }
    public IReadOnlyList<AdminNetworkAccessProviderState> Providers { get; init; } = [];
    public long Revision { get; init; }
    public override string ToString() => nameof(AdminNetworkAccessState);
}

/// <summary>
/// Configure › Network access state owner (official <c>hooks/queries/admin/networkAccess.ts</c>).
/// <list type="bullet">
/// <item>Capabilities are read on open and refreshed only when stale (30 s) on activation; they are never polled.</item>
/// <item>Provider statuses are read through a queue that runs at most <see cref="MaxConcurrentStatusReads"/> reads at once,
/// coalesces a provider already queued or running, and polls every 15 s only while <see cref="SetActive"/> is true.
/// Activation refreshes only stale statuses; deactivation drops the timer and every queued read.</item>
/// <item>Connect/disconnect capture the action's authority and one named host, send once without authentication replay,
/// write a validated acknowledgement straight into the provider's status and then refresh it. An unknown outcome is
/// reconciled with a status read, never resent.</item>
/// <item>Any authority change clears everything, including enrollment URLs, and late results are discarded.</item>
/// </list>
/// Events are raised on any thread; marshal before touching UI.
/// </summary>
public sealed class AdminNetworkAccessController : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan CapabilitiesStaleTime = TimeSpan.FromSeconds(30);
    public const int MaxConcurrentStatusReads = 2;

    private sealed class Slot(AdminNetworkAccessProvider summary)
    {
        public AdminNetworkAccessProvider Summary = summary;
        public AdminNetworkAccessStatus? Status;
        public AdminFailureKind? Failure;
        public int? Limit;
        public bool Queued, Running, Background;
        public CancellationTokenSource? Read;
        public long Sequence;
        public DateTimeOffset LastRead = DateTimeOffset.MinValue;
        public readonly Dictionary<string, object> Busy = new(StringComparer.Ordinal);
    }

    private readonly AdminNetworkAccessApi _api;
    private readonly TimeProvider _time;
    private readonly Func<bool> _deferBackground;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly List<Slot> _slots = [];
    private readonly LinkedList<Slot> _queue = new();
    private AdminNetworkAccessState _state = AdminNetworkAccessState.Closed;
    private MonitorAuthorityLease? _lease;
    private CancellationTokenSource? _capabilitiesRead;
    private long _capabilitiesSequence;
    private DateTimeOffset _capabilitiesReadAt = DateTimeOffset.MinValue;
    private bool _capabilitiesLoading, _capabilitiesLoaded;
    private AdminFailureKind? _capabilitiesFailure;
    private int? _capabilitiesLimit;
    private int _running;
    private ITimer? _poll;
    private bool _active, _open, _disposed, _capabilitiesRefreshPending;
    private long _revision;

    /// <param name="deferBackground">True while foreground navigation or playback start should not share the network with a poll.</param>
    public AdminNetworkAccessController(AdminNetworkAccessApi api, AdminMonitorAuthority authority, TimeProvider? time = null, Func<bool>? deferBackground = null)
    {
        _api = api;
        _time = time ?? TimeProvider.System;
        _deferBackground = deferBackground ?? (() => false);
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnAuthorityRetired;
    }

    public event Action? Changed;
    /// <summary>The admin authority changed while open; everything was cleared. Marshal and call <see cref="Reopen"/> if still allowed.</summary>
    public event Action? AuthorityRetired;
    /// <summary>A command was rejected or its outcome is unknown, for the current owner only.</summary>
    public event Action<AdminNetworkAccessCommandFeedback>? CommandFailed;

    public AdminNetworkAccessState State { get { lock (_gate) return _state; } }
    public bool IsPolling { get { lock (_gate) return _poll != null; } }

    /// <summary>Captures the page owner and reads capabilities, then every provider's status. False when not acting as admin.</summary>
    public bool Open()
    {
        MonitorAuthorityLease lease;
        try { lease = _lifetime.Open(); }
        catch (OperationCanceledException) { return false; }
        catch (ObjectDisposedException) { return false; }
        lock (_gate)
        {
            if (_disposed) return false;
            ResetLocked();
            _lease = lease; _open = true;
            StartCapabilitiesLocked(lease);
            if (_active) StartPollLocked();
            PublishLocked();
        }
        Raise();
        return true;
    }

    public bool Reopen() { lock (_gate) if (_disposed) return false; return Open(); }

    /// <summary>Explicit page activation after <see cref="Suspend"/>, before <see cref="Open"/>.</summary>
    public void Resume() { lock (_gate) if (_disposed) return; _lifetime.Resume(); }

    /// <summary>Page departure or outer Admin suspension: retire the owner, cancel every read and command, clear all data.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        lock (_gate) { ResetLocked(); PublishLocked(); }
        Raise();
    }

    /// <summary>
    /// Whether the page and its window are in front (official canApplyRealtimeUpdates, plus window activation and the
    /// shell's own suspension). Inactive: no timer and no queued reads. Activation refreshes only stale data.
    /// </summary>
    public void SetActive(bool active)
    {
        var start = new List<(Slot Slot, long Sequence, CancellationTokenSource Cts)>();
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (!active)
            {
                _poll?.Dispose(); _poll = null;
                foreach (var slot in _queue) slot.Queued = false;
                _queue.Clear();
                _capabilitiesRefreshPending = false;
                PublishLocked();
            }
            else if (_open && _lease is { } lease)
            {
                StartPollLocked();
                var now = _time.GetUtcNow();
                if (!_capabilitiesLoading && now - _capabilitiesReadAt >= CapabilitiesStaleTime) _capabilitiesRefreshPending = true;
                foreach (var slot in _slots.Where(slot => !slot.Running && now - slot.LastRead >= PollInterval)) EnqueueLocked(slot, background: true);
                PumpLocked(lease, start);
                PublishLocked();
            }
        }
        Launch(start);
        Raise();
    }

    /// <summary>Explicit retry after a failed capability read (capabilities are never polled).</summary>
    public Task RetryCapabilitiesAsync()
    {
        lock (_gate)
        {
            if (_lease is not { IsCurrent: true } lease || _capabilitiesLoading) return Task.CompletedTask;
            StartCapabilitiesLocked(lease);
            PublishLocked();
        }
        Raise();
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string provider, string hostId, MonitorAuthorityLease? owner = null) => CommandAsync(provider, hostId, AdminNetworkAccessCommandKind.Connect, owner);
    public Task DisconnectAsync(string provider, string hostId, MonitorAuthorityLease? owner = null) => CommandAsync(provider, hostId, AdminNetworkAccessCommandKind.Disconnect, owner);

    /// <summary>The enrollment page for a host the current owner sees waiting for authorization, if it is a safe web URL.</summary>
    public bool TryGetEnrollmentUri(string provider, string hostId, out Uri uri)
    {
        uri = null!;
        var host = CurrentHost(provider, hostId);
        return host is { State: AdminNetworkAccessHostStatus.AwaitingAuthorization, AuthUrl: { } url } && ExternalBrowserUrlPolicy.TryGetSafeUri(url, out uri);
    }

    /// <summary>The overlay origin of a host the current owner sees, if it is a safe web URL.</summary>
    public bool TryGetOriginUri(string provider, string hostId, out Uri uri)
    {
        uri = null!;
        return CurrentHost(provider, hostId)?.Origin is { } origin && ExternalBrowserUrlPolicy.TryGetSafeUri(origin, out uri);
    }

    private AdminNetworkAccessHostStatus? CurrentHost(string provider, string hostId)
    {
        lock (_gate)
        {
            if (_disposed || !_open || !_lifetime.IsCurrent(_lease)) return null;
            return _slots.FirstOrDefault(slot => slot.Summary.Provider == provider)?.Status?.Hosts.FirstOrDefault(host => host.Host.Id == hostId);
        }
    }

    // ── Commands ─────────────────────────────────────────────

    private async Task CommandAsync(string provider, string hostId, AdminNetworkAccessCommandKind kind, MonitorAuthorityLease? expectedOwner)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            Slot commandSlot;
            MonitorAuthorityLease commandOwner;
            var operation = new object();
            lock (_gate)
            {
                var slot = _open && _lifetime.IsCurrent(action) && (expectedOwner == null || ReferenceEquals(_lease, expectedOwner)) ? Find(provider) : null;
                // Only a host the owner can see, once at a time; a stale click for another host list is ignored.
                if (slot?.Status?.Hosts.Any(host => host.Host.Id == hostId) != true || !slot.Busy.TryAdd(hostId, operation)) return;
                commandSlot = slot; commandOwner = _lease!;
                PublishLocked();
            }
            Raise();
            AdminNetworkAccessCommandFeedback? feedback = null;
            var start = new List<(Slot Slot, long Sequence, CancellationTokenSource Cts)>();
            try
            {
                var result = kind == AdminNetworkAccessCommandKind.Connect
                    ? await _api.ConnectAsync(action, provider, hostId).ConfigureAwait(false)
                    : await _api.DisconnectAsync(action, provider, hostId).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!Current(action) || !ReferenceEquals(Find(provider), commandSlot)) return;
                    var slot = commandSlot;
                    slot.Busy.Remove(hostId);
                    // The acknowledgement is newer than any read already running; that read is discarded.
                    CancelReadLocked(slot);
                    slot.Status = result; slot.Failure = null; slot.Limit = null; slot.LastRead = _time.GetUtcNow();
                    EnqueueLocked(slot);
                    PumpLocked(_lease!, start);
                    PublishLocked();
                }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminNetworkAccessOutcomeUnknownException)
            {
                lock (_gate)
                {
                    if (!Current(action) || !ReferenceEquals(Find(provider), commandSlot)) return;
                    var slot = commandSlot;
                    slot.Busy.Remove(hostId);
                    CancelReadLocked(slot);
                    EnqueueLocked(slot);
                    PumpLocked(_lease!, start);
                    PublishLocked();
                    feedback = new(provider, hostId, kind, AdminNetworkAccessCommandOutcome.Unknown) { Owner = commandOwner };
                }
            }
            catch (Exception error)
            {
                var failure = AdminFailures.Classify(error, action);
                lock (_gate)
                {
                    if (!Current(action) || !ReferenceEquals(Find(provider), commandSlot)) return;
                    var slot = commandSlot;
                    slot.Busy.Remove(hostId);
                    // An unknown host or provider means this list is stale: re-read it (a read, never a resend).
                    if (failure is AdminFailureKind.Validation or AdminFailureKind.NotFound) { EnqueueLocked(slot); PumpLocked(_lease!, start); }
                    PublishLocked();
                    feedback = new(provider, hostId, kind, AdminNetworkAccessCommandOutcome.Rejected, failure) { Owner = commandOwner };
                }
            }
            finally
            {
                // Release only this operation: a replacement owner or a later command may already use the same host.
                lock (_gate)
                {
                    if (commandSlot.Busy.TryGetValue(hostId, out var pending) && ReferenceEquals(pending, operation))
                    {
                        commandSlot.Busy.Remove(hostId);
                        if (ReferenceEquals(_lease, commandOwner) && _slots.Contains(commandSlot)) PublishLocked();
                    }
                }
            }
            Launch(start);
            Raise();
            if (feedback != null && Current(action)) CommandFailed?.Invoke(feedback);
        }
    }

    /// <summary>A read cancelled while its owner is still current ran out of time: a transport failure, not a user cancellation.</summary>
    private static AdminFailureKind Classify(Exception error, MonitorAuthorityLease lease) =>
        error is OperationCanceledException && lease.IsCurrent ? AdminFailureKind.Transport : AdminFailures.Classify(error, lease);

    private bool Current(MonitorAuthorityLease action) { lock (_gate) return !_disposed && _open && _lifetime.IsCurrent(action) && _lease != null; }
    private Slot? Find(string provider) => _slots.FirstOrDefault(slot => slot.Summary.Provider == provider);

    // ── Reads ────────────────────────────────────────────────

    private void StartCapabilitiesLocked(MonitorAuthorityLease lease, bool background = false)
    {
        try { _capabilitiesRead?.Cancel(); } catch (ObjectDisposedException) { }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
        _capabilitiesRead = cts;
        _capabilitiesRefreshPending = false;
        var sequence = ++_capabilitiesSequence;
        _capabilitiesLoading = true;
        _capabilitiesFailure = null; _capabilitiesLimit = null;
        _ = Task.Run(() => RunCapabilitiesAsync(lease, sequence, cts, background));
    }

    private async Task RunCapabilitiesAsync(MonitorAuthorityLease lease, long sequence, CancellationTokenSource cts, bool background)
    {
        AdminNetworkAccessCapabilities? result = null;
        Exception? failure = null;
        try { result = await _api.GetCapabilitiesAsync(lease, cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        var start = new List<(Slot Slot, long Sequence, CancellationTokenSource Cts)>();
        lock (_gate)
        {
            cts.Dispose();
            if (sequence != _capabilitiesSequence || !ReferenceEquals(_lease, lease) || !_lifetime.IsCurrent(lease)) return;
            _capabilitiesRead = null;
            _capabilitiesLoading = false;
            if (result == null)
            {
                _capabilitiesFailure = Classify(failure!, lease);
                _capabilitiesLimit = (failure as AdminNetworkAccessLimitException)?.Limit;
                PublishLocked();
            }
            else
            {
                _capabilitiesLoaded = true;
                _capabilitiesReadAt = _time.GetUtcNow();
                // Keep existing groups (and their rows) for providers that are still installed.
                var next = new List<Slot>();
                foreach (var provider in result.Providers)
                {
                    var slot = Find(provider.Provider);
                    if (slot == null) { slot = new Slot(provider); EnqueueLocked(slot, background); }
                    else slot.Summary = provider;
                    next.Add(slot);
                }
                foreach (var removed in _slots.Except(next).ToArray()) { CancelReadLocked(removed); _queue.Remove(removed); removed.Queued = false; }
                _slots.Clear(); _slots.AddRange(next);
                PumpLocked(lease, start);
                PublishLocked();
            }
        }
        Launch(start);
        Raise();
    }

    private void EnqueueLocked(Slot slot, bool background = false)
    {
        if (slot.Running) return;
        if (slot.Queued)
        {
            // Explicit command reconciliation can promote a queued background refresh.
            if (!background) slot.Background = false;
            return;
        }
        slot.Queued = true; slot.Background = background;
        _queue.AddLast(slot);
    }

    private void CancelReadLocked(Slot slot)
    {
        slot.Sequence++;
        if (slot.Read != null) { try { slot.Read.Cancel(); } catch (ObjectDisposedException) { } slot.Read = null; }
        slot.Running = false;
    }

    /// <summary>Starts queued reads up to the concurrency bound. Cancelled reads still count until they return.</summary>
    private void PumpLocked(MonitorAuthorityLease lease, List<(Slot Slot, long Sequence, CancellationTokenSource Cts)> start)
    {
        // A stale activation is admitted once foreground work finishes, rather than polling capabilities.
        if (_active && _capabilitiesRefreshPending && !_capabilitiesLoading && !_deferBackground()) StartCapabilitiesLocked(lease, background: true);
        while (_running < MaxConcurrentStatusReads)
        {
            var next = _queue.First;
            while (next != null && next.Value.Background && (!_active || _deferBackground())) next = next.Next;
            if (next == null) break;
            var slot = next.Value;
            _queue.Remove(next);
            slot.Queued = false;
            if (!_slots.Contains(slot)) continue;
            slot.Running = true;
            slot.Read = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            _running++;
            start.Add((slot, ++slot.Sequence, slot.Read));
        }
    }

    private void Launch(List<(Slot Slot, long Sequence, CancellationTokenSource Cts)> start)
    {
        MonitorAuthorityLease? lease;
        lock (_gate) lease = _lease;
        foreach (var (slot, sequence, cts) in start)
        {
            if (lease == null) { lock (_gate) _running--; continue; }
            _ = Task.Run(() => RunStatusAsync(lease, slot, sequence, cts));
        }
    }

    private async Task RunStatusAsync(MonitorAuthorityLease lease, Slot slot, long sequence, CancellationTokenSource cts)
    {
        AdminNetworkAccessStatus? result = null;
        Exception? failure = null;
        try { result = await _api.GetStatusAsync(lease, slot.Summary.Provider, cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        var start = new List<(Slot Slot, long Sequence, CancellationTokenSource Cts)>();
        var changed = false;
        lock (_gate)
        {
            _running = Math.Max(0, _running - 1);
            cts.Dispose();
            if (ReferenceEquals(slot.Read, cts)) slot.Read = null;
            var current = sequence == slot.Sequence && ReferenceEquals(_lease, lease) && _lifetime.IsCurrent(lease) && _slots.Contains(slot);
            if (current)
            {
                slot.Running = false;
                if (result != null) { slot.Status = result; slot.Failure = null; slot.Limit = null; slot.LastRead = _time.GetUtcNow(); changed = true; }
                else
                {
                    slot.Failure = Classify(failure!, lease);
                    slot.Limit = (failure as AdminNetworkAccessLimitException)?.Limit;
                    slot.LastRead = _time.GetUtcNow();
                    changed = true;
                }
            }
            if (_lease is { } owner && _lifetime.IsCurrent(owner)) PumpLocked(owner, start);
            if (changed || start.Count > 0) PublishLocked();
        }
        Launch(start);
        if (changed || start.Count > 0) Raise();
    }

    // ── Polling ──────────────────────────────────────────────

    private void StartPollLocked()
    {
        _poll?.Dispose();
        _poll = _time.CreateTimer(_ => OnPoll(), null, PollInterval, PollInterval);
    }

    private void OnPoll()
    {
        var start = new List<(Slot Slot, long Sequence, CancellationTokenSource Cts)>();
        lock (_gate)
        {
            if (_disposed || !_active || !_open || _lease is not { } lease || !_lifetime.IsCurrent(lease)) return;
            // Foreground navigation or playback start keeps the network; the next tick tries again.
            if (_deferBackground()) return;
            foreach (var slot in _slots) EnqueueLocked(slot, background: true);
            PumpLocked(lease, start);
            if (start.Count > 0) PublishLocked();
        }
        Launch(start);
        if (start.Count > 0) Raise();
    }

    // ── Lifetime ─────────────────────────────────────────────

    private void OnAuthorityRetired()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ResetLocked();
            PublishLocked();
        }
        Raise();
        AuthorityRetired?.Invoke();
    }

    private void ResetLocked()
    {
        _poll?.Dispose(); _poll = null;
        _capabilitiesSequence++;
        try { _capabilitiesRead?.Cancel(); } catch (ObjectDisposedException) { }
        _capabilitiesRead = null;
        foreach (var slot in _slots) CancelReadLocked(slot);
        _slots.Clear(); _queue.Clear();
        _capabilitiesLoading = _capabilitiesLoaded = false;
        _capabilitiesFailure = null; _capabilitiesLimit = null;
        _capabilitiesReadAt = DateTimeOffset.MinValue;
        _lease = null; _open = false; _capabilitiesRefreshPending = false;
    }

    private void PublishLocked()
    {
        var owner = _lease;
        var loading = _open && _capabilitiesLoading && !_capabilitiesLoaded;
        _state = new AdminNetworkAccessState
        {
            Owner = owner, IsOpen = _open,
            CapabilitiesLoading = loading, CapabilitiesLoaded = _open && _capabilitiesLoaded && _capabilitiesFailure == null,
            CapabilitiesFailure = _open ? _capabilitiesFailure : null, CapabilitiesLimit = _open ? _capabilitiesLimit : null,
            Providers = _open && _capabilitiesFailure == null ? Array.AsReadOnly(_slots.Select(slot => new AdminNetworkAccessProviderState(slot.Summary)
            {
                Loading = slot.Status == null && slot.Failure == null,
                Status = slot.Status, Failure = slot.Failure, Limit = slot.Limit,
                BusyHosts = Array.AsReadOnly(slot.Busy.Keys.ToArray()),
            }).ToArray()) : [],
            Revision = ++_revision,
        };
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; ResetLocked(); _state = AdminNetworkAccessState.Closed; }
        _lifetime.Dispose();
        Changed = null; AuthorityRetired = null; CommandFailed = null;
    }
}
