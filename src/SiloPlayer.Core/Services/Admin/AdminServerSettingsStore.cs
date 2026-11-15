using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Lazy, authority-scoped, single-flight settings reads. No polling or background refresh.</summary>
public sealed class AdminServerSettingsStore : IDisposable
{
    private readonly AdminServerSettingsApi _api;
    private readonly AdminMonitorAuthority _authority;
    private readonly string[] _keys;
    private readonly string[] _secrets;
    private readonly object _gate = new();
    private readonly Cache<AdminSettingsSnapshot> _effective = new();
    private readonly Cache<AdminSettingsSensitiveStatus> _sensitive = new();
    private readonly Cache<LoggingRestartKeys> _restart = new();
    private CancellationTokenSource _generation = new();
    private bool _disposed;

    public AdminServerSettingsStore(AdminServerSettingsApi api, AdminMonitorAuthority authority, IEnumerable<string> keys, IEnumerable<string> secrets)
    {
        _api = api; _authority = authority; _keys = keys.Distinct(StringComparer.Ordinal).ToArray(); _secrets = secrets.Distinct(StringComparer.Ordinal).ToArray();
        authority.AvailabilityChanged += Invalidate;
    }

    public Task<AdminSettingsSnapshot> GetEffectiveAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        Read(_effective, lease, TimeSpan.FromSeconds(30), (owner, token) => _api.GetEffectiveAsync(owner, _keys, _secrets, token), ct);
    public Task<AdminSettingsSensitiveStatus> GetSensitiveStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        Read(_sensitive, lease, TimeSpan.FromSeconds(30), async (owner, token) =>
        {
            var status = await _api.GetSensitiveStatusAsync(owner, token).ConfigureAwait(false);
            return new(Array.AsReadOnly(status.Configured.Where(_keys.Contains).ToArray()), Array.AsReadOnly(status.ManagedByEnv.Where(_keys.Contains).ToArray()));
        }, ct);
    public async Task<LoggingRestartKeys> GetRestartKeysAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var keys = await Read(_restart, lease, TimeSpan.FromMinutes(5), _api.GetRestartKeysAsync, ct).ConfigureAwait(false);
        return new() { Keys = keys.Keys.ToArray(), Prefixes = keys.Prefixes.ToArray() };
    }

    private Task<T> Read<T>(Cache<T> cache, MonitorAuthorityLease lease, TimeSpan age,
        Func<MonitorAuthorityLease, CancellationToken, Task<T>> fetch, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lease.ThrowIfRetired();
        if (!ReferenceEquals(lease.Owner, _authority)) throw new ArgumentException("Lease belongs to another authority.", nameof(lease));
        Flight<T> flight;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (cache.HasValue && cache.Epoch == lease.Epoch && DateTimeOffset.UtcNow - cache.StoredAt < age)
                return Task.FromResult(cache.Value!);
            if (cache.Pending == null)
            {
                var requestOwner = _authority.Capture();
                if (requestOwner.Epoch != lease.Epoch) { requestOwner.Dispose(); throw new MonitorAuthorityRetiredException(); }
                flight = new Flight<T>(CancellationTokenSource.CreateLinkedTokenSource(_generation.Token));
                cache.Pending = flight;
                flight.Task = Fetch(cache, flight, requestOwner, fetch);
            }
            else flight = cache.Pending;
            flight.Waiters++;
        }
        return Wait(cache, flight, lease, ct);
    }

    private async Task<T> Wait<T>(Cache<T> cache, Flight<T> flight, MonitorAuthorityLease lease, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        try
        {
            var value = await flight.Task!.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            lease.ThrowIfRetired();
            return value;
        }
        finally
        {
            bool cancel = false;
            lock (_gate)
            {
                flight.Waiters--;
                if (flight.Waiters == 0 && ReferenceEquals(cache.Pending, flight))
                {
                    cache.Pending = null;
                    cancel = true;
                }
            }
            if (cancel) try { flight.Cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task<T> Fetch<T>(Cache<T> cache, Flight<T> flight, MonitorAuthorityLease lease,
        Func<MonitorAuthorityLease, CancellationToken, Task<T>> fetch)
    {
        await Task.Yield(); // publish the flight before a synchronous response can complete
        try
        {
            var value = await fetch(lease, flight.Cancellation.Token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            flight.Cancellation.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_disposed && ReferenceEquals(cache.Pending, flight) && !flight.Cancellation.IsCancellationRequested)
                {
                    cache.Value = value; cache.HasValue = true; cache.Epoch = lease.Epoch; cache.StoredAt = DateTimeOffset.UtcNow;
                }
            }
            return value;
        }
        finally
        {
            lock (_gate) { if (ReferenceEquals(cache.Pending, flight)) cache.Pending = null; }
            lease.Dispose(); flight.Cancellation.Dispose();
        }
    }

    /// <summary>Drop snapshots after a write or before explicit reconciliation.</summary>
    public void Invalidate()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            if (_disposed) return;
            old = _generation; _generation = new();
            _effective.Clear(); _sensitive.Clear(); _restart.Clear();
        }
        old.Cancel(); old.Dispose();
    }

    public void Dispose()
    {
        _authority.AvailabilityChanged -= Invalidate;
        CancellationTokenSource old;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; old = _generation;
            _effective.Clear(); _sensitive.Clear(); _restart.Clear();
        }
        old.Cancel(); old.Dispose();
    }

    private sealed class Cache<T>
    {
        public T? Value; public bool HasValue; public long Epoch; public DateTimeOffset StoredAt; public Flight<T>? Pending;
        public void Clear() { Value = default; HasValue = false; Pending = null; }
    }
    private sealed class Flight<T>(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<T>? Task; public int Waiters;
    }
}
