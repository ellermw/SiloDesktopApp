namespace SiloPlayer.Core.Services;

/// <summary>One viewport's short artwork pause, independent of card and cache ownership.</summary>
public sealed class ArtworkMotionPolicy
{
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private long _lastSample, _generation;
    private double _x, _y;
    private ITimer? _settle;
    private TaskCompletionSource? _ready;

    public ArtworkMotionPolicy(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastSample = _clock.GetTimestamp();
    }

    public bool IsDeferred { get { lock (_sync) return _ready != null; } }

    public void Observe(double x, double y)
    {
        lock (_sync)
        {
            var distance = Math.Max(Math.Abs(x - _x), Math.Abs(y - _y));
            // Native completion/extent notifications can repeat the same offset.
            if (distance < .1) return;
            var now = _clock.GetTimestamp();
            var elapsed = Math.Clamp(_clock.GetElapsedTime(_lastSample, now).TotalMilliseconds, 1, 100);
            _x = x; _y = y; _lastSample = now;
            RetireTimer();
            if (distance / elapsed <= 1.2) { Release(); return; }
            _ready ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            var generation = _generation;
            _settle = _clock.CreateTimer(_ =>
            {
                lock (_sync)
                {
                    if (_generation != generation) return;
                    RetireTimer(); Release();
                }
            }, null, TimeSpan.FromMilliseconds(120), Timeout.InfiniteTimeSpan);
        }
    }

    public Task WaitAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync) return _ready?.Task.WaitAsync(ct) ?? Task.CompletedTask;
    }

    public void Reset(double x = 0, double y = 0)
    {
        lock (_sync)
        {
            _x = x; _y = y; _lastSample = _clock.GetTimestamp();
            RetireTimer(); Release();
        }
    }

    private void RetireTimer() { _generation++; _settle?.Dispose(); _settle = null; }
    private void Release() { var ready = _ready; _ready = null; ready?.TrySetResult(); }
}
