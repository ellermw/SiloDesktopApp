using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>A deterministic clock: timers fire only when the test advances time.</summary>
internal sealed class DashboardManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_timers) next = _timers.Where(t => t.Due != null && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
            if (next == null) break;
            _now = next.Due!.Value;
            next.Fire();
        }
        _now = target;
    }

    private sealed class ManualTimer(DashboardManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._timers)
            {
                _period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                if (!owner._timers.Contains(this)) owner._timers.Add(this);
            }
            return true;
        }

        public void Fire()
        {
            lock (owner._timers) Due = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan ? owner._now + _period : null;
            callback(state);
        }

        public void Dispose()
        {
            lock (owner._timers) { Due = null; owner._timers.Remove(this); }
        }

        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/// <summary>Scripted layout server: records writes and lets a test hold or fail each one.</summary>
internal sealed class DashboardFakeLayoutServer : IDashboardLayoutServer
{
    public readonly List<(string Document, string ETag)> Saves = [];
    public int Resets;
    public int Reads;
    public Func<int, Task<DashboardLayoutRead>> OnRead { get; set; } = _ => Task.FromResult(Layout("\"A\"", null));
    public Func<int, Task<string>> OnSave { get; set; }
    public Func<Task> OnReset { get; set; } = () => Task.CompletedTask;
    private int _etag;

    public DashboardFakeLayoutServer()
    {
        OnSave = _ => Task.FromResult($"\"S{++_etag}\"");
    }

    public static DashboardLayoutRead Layout(string etag, string? json)
    {
        JsonElement? layout = json == null ? null : JsonDocument.Parse(json).RootElement.Clone();
        return new DashboardLayoutRead(false, new DashboardServerLayout(layout, json == null ? null : DateTimeOffset.UnixEpoch, etag));
    }

    public Task<DashboardLayoutRead> ReadAsync(MonitorAuthorityLease lease, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        return OnRead(++Reads);
    }

    public Task<string> SaveAsync(MonitorAuthorityLease lease, string document, string expectedETag, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        Saves.Add((document, expectedETag));
        return OnSave(Saves.Count);
    }

    public Task ResetAsync(MonitorAuthorityLease lease, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        Resets++;
        return OnReset();
    }
}

/// <summary>A hand-driven stand-in for the shared ServerActivityStore.</summary>
internal sealed class DashboardFakeFeed : IDashboardActivityFeed
{
    public DashboardActivityFeedSnapshot Current { get; set; } = new(false, null, [], false, []);
    public event Action? Changed;
    public int Acquired;
    public int Released;
    public int Refreshes;

    public IDisposable AcquireReads()
    {
        Acquired++;
        return new Release(this);
    }

    public Task RefreshSessionsAsync()
    {
        Refreshes++;
        return Task.CompletedTask;
    }

    public void Publish(DashboardActivityFeedSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke();
    }

    private sealed class Release(DashboardFakeFeed feed) : IDisposable
    {
        public void Dispose() => feed.Released++;
    }
}

internal static class DashboardFixtures
{
    public static DashboardSession Session(string id, string method = "direct", bool paused = false)
        => new() { SessionId = id, UserId = "1", Username = "synthetic", MediaTitle = "Synthetic " + id, EffectivePlayMethod = method, IsPaused = paused };

    public static DashboardActiveScan Scan(string id, string library, string status = "running")
        => new(id, library, "library", null, "manual", status, null, 10, 5, null);
}
