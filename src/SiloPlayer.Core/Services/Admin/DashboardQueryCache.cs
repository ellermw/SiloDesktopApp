using System.Runtime.CompilerServices;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One bounded, in-memory dashboard query cache per admin authority.</summary>
internal sealed class DashboardQueryCache
{
    internal static readonly TimeSpan UnusedLifetime = TimeSpan.FromMinutes(10);
    private const int MaximumQueries = 64;
    private static readonly ConditionalWeakTable<AdminMonitorAuthority, DashboardQueryCache> Caches = new();
    private readonly object _gate = new();
    private readonly AdminMonitorAuthority _authority;
    private readonly TimeProvider _time;
    private Dictionary<string, Snapshot> _queries = new(StringComparer.Ordinal);
    private ITimer? _expiry;
    private long _epoch;
    private long _visit;

    internal sealed record Snapshot(object? Data, DateTimeOffset? UpdatedAt, bool Invalidated, DateTimeOffset ExpiresAt,
        DateTimeOffset? LastStarted, bool InsightsUnsupported);

    private DashboardQueryCache(AdminMonitorAuthority authority, TimeProvider time)
    {
        _authority = authority;
        _time = time;
        _authority.AvailabilityChanged += OnAuthorityChanged;
    }

    internal static DashboardQueryCache For(MonitorAuthorityLease lease, TimeProvider time)
        => Caches.GetValue(lease.Owner, authority => new DashboardQueryCache(authority, time));

    internal (long Visit, IReadOnlyDictionary<string, Snapshot> Queries) Restore(MonitorAuthorityLease lease)
    {
        lock (_gate)
        {
            if (!lease.IsCurrent) return (0, new Dictionary<string, Snapshot>());
            if (_epoch != lease.Epoch)
            {
                Clear();
                _epoch = lease.Epoch;
            }
            PruneExpired();
            return (++_visit, new Dictionary<string, Snapshot>(_queries, StringComparer.Ordinal));
        }
    }

    internal void Retain(MonitorAuthorityLease lease, long visit, IEnumerable<KeyValuePair<string, Snapshot>> queries)
    {
        lock (_gate)
        {
            if (visit != _visit || _epoch != lease.Epoch || !lease.IsCurrent) return;
            _queries = queries.Where(pair => pair.Value.ExpiresAt > _time.GetUtcNow())
                .OrderByDescending(pair => pair.Value.ExpiresAt).ThenByDescending(pair => pair.Value.UpdatedAt)
                .Take(MaximumQueries).ToDictionary(StringComparer.Ordinal);
            ArmExpiry();
        }
    }

    private void OnAuthorityChanged()
    {
        lock (_gate)
            if (_epoch != _authority.Epoch || !_authority.IsAvailable) Clear();
    }

    private void Clear()
    {
        _queries.Clear();
        _expiry?.Dispose();
        _expiry = null;
    }

    private void PruneExpired()
    {
        var now = _time.GetUtcNow();
        foreach (var key in _queries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            _queries.Remove(key);
    }

    private void ArmExpiry()
    {
        _expiry?.Dispose();
        _expiry = null;
        if (_queries.Count == 0) return;
        var delay = _queries.Values.Min(query => query.ExpiresAt) - _time.GetUtcNow();
        _expiry = _time.CreateTimer(_ =>
        {
            lock (_gate)
            {
                PruneExpired();
                ArmExpiry();
            }
        }, null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }
}
