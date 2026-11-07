using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class ArtworkMotionPolicyTests
{
    [Fact]
    public async Task Fast_motion_defers_until_quiet_then_releases_current_work()
    {
        var clock = new Clock(); var policy = new ArtworkMotionPolicy(clock);
        clock.Advance(20); policy.Observe(0, 300);
        var pending = policy.WaitAsync(default);
        Assert.False(pending.IsCompleted);
        clock.Advance(119); Assert.False(pending.IsCompleted);
        clock.Advance(1); await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(policy.IsDeferred);
    }

    [Fact]
    public async Task Slow_motion_releases_without_waiting_for_the_old_settle_timer()
    {
        var clock = new Clock(); var policy = new ArtworkMotionPolicy(clock);
        clock.Advance(20); policy.Observe(0, 300);
        var pending = policy.WaitAsync(default);
        clock.Advance(40); policy.Observe(0, 301);
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(policy.WaitAsync(default).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Superseded_callback_and_zero_delta_completion_do_not_release_new_burst()
    {
        var clock = new Clock(); var policy = new ArtworkMotionPolicy(clock);
        clock.Advance(20); policy.Observe(300, 0);
        var old = clock.Timers.Last();
        clock.Advance(80); policy.Observe(600, 0); policy.Observe(600, 0);
        var pending = policy.WaitAsync(default);
        old.FireEvenIfDisposed(); Assert.False(pending.IsCompleted);
        clock.Advance(119); Assert.False(pending.IsCompleted);
        clock.Advance(1); await pending.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Cancellation_and_other_viewports_cannot_release_the_current_viewport()
    {
        var clock = new Clock(); var first = new ArtworkMotionPolicy(clock); var second = new ArtworkMotionPolicy(clock);
        clock.Advance(20); first.Observe(0, 300);
        using var canceled = new CancellationTokenSource();
        var retired = first.WaitAsync(canceled.Token); var retained = first.WaitAsync(default);
        canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retired);
        Assert.True(second.WaitAsync(default).IsCompletedSuccessfully);
        second.Reset(); Assert.False(retained.IsCompleted);
        first.Reset(); await retained.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(first.WaitAsync(default).IsCompletedSuccessfully);
    }

    [Fact]
    public void First_large_displacement_after_idle_is_fast_but_small_movement_is_immediate()
    {
        var clock = new Clock(); var policy = new ArtworkMotionPolicy(clock);
        clock.Advance(30000); policy.Observe(0, 400); Assert.True(policy.IsDeferred);
        clock.Advance(80); policy.Observe(0, 405); Assert.False(policy.IsDeferred);
    }

    private sealed class Clock : TimeProvider
    {
        private long _milliseconds;
        public List<Pending> Timers { get; } = [];
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Pending(callback, state, _milliseconds + (long)dueTime.TotalMilliseconds);
            Timers.Add(timer); return timer;
        }
        public void Advance(int milliseconds)
        {
            _milliseconds += milliseconds;
            foreach (var timer in Timers.ToArray()) if (!timer.Disposed && timer.At <= _milliseconds) timer.FireEvenIfDisposed();
        }
        public sealed class Pending(TimerCallback callback, object? state, long at) : ITimer
        {
            public long At => at;
            public bool Disposed;
            public void FireEvenIfDisposed() { Disposed = true; callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
