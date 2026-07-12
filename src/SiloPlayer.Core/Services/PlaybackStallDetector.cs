namespace SiloPlayer.Core.Services;

public readonly record struct PlaybackStallDecision(bool ShouldRecover, string Reason, double Position)
{
    public static PlaybackStallDecision None(double position) => new(false, "", position);
}

public sealed class PlaybackStallDetector
{
    private const double SignificantPositionAdvanceSeconds = 0.5;

    private readonly TimeSpan _bufferingTimeout;
    private readonly TimeSpan _silentPlaybackTimeout;

    private double _lastPosition = double.NaN;
    private DateTimeOffset _lastAdvanceAt;
    private DateTimeOffset? _bufferingStartedAt;

    public PlaybackStallDetector(TimeSpan bufferingTimeout, TimeSpan silentPlaybackTimeout)
    {
        if (bufferingTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(bufferingTimeout));
        if (silentPlaybackTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(silentPlaybackTimeout));

        _bufferingTimeout = bufferingTimeout;
        _silentPlaybackTimeout = silentPlaybackTimeout;
    }

    public void Reset(double position = 0, DateTimeOffset? now = null)
    {
        _lastPosition = position;
        _lastAdvanceAt = now ?? DateTimeOffset.UtcNow;
        _bufferingStartedAt = null;
    }

    public PlaybackStallDecision Observe(
        double position,
        double duration,
        bool isPaused,
        bool isBufferingForCache,
        bool isRecoveryInProgress,
        DateTimeOffset now)
    {
        if (duration <= 0 ||
            position < 0 ||
            IsAtMediaEnd(position, duration) ||
            isRecoveryInProgress)
        {
            Reset(position, now);
            return PlaybackStallDecision.None(position);
        }

        if (double.IsNaN(_lastPosition))
        {
            Reset(position, now);
            if (isBufferingForCache)
                _bufferingStartedAt = now;
            return PlaybackStallDecision.None(position);
        }

        if (position > _lastPosition + SignificantPositionAdvanceSeconds ||
            position < _lastPosition - SignificantPositionAdvanceSeconds)
        {
            _lastPosition = position;
            _lastAdvanceAt = now;
            _bufferingStartedAt = isBufferingForCache ? now : null;
            return PlaybackStallDecision.None(position);
        }

        _lastPosition = position;

        if (isPaused && !isBufferingForCache)
        {
            _lastAdvanceAt = now;
            _bufferingStartedAt = null;
            return PlaybackStallDecision.None(position);
        }

        if (isBufferingForCache)
        {
            _bufferingStartedAt ??= now;
            if (now - _bufferingStartedAt.Value >= _bufferingTimeout)
                return new PlaybackStallDecision(true, "buffering-stalled", position);
        }

        if (!isPaused && now - _lastAdvanceAt >= _silentPlaybackTimeout)
            return new PlaybackStallDecision(true, "position-stalled", position);

        return PlaybackStallDecision.None(position);
    }

    private static bool IsAtMediaEnd(double position, double duration)
    {
        if (duration <= 0 || position < 0)
            return false;

        var tolerance = Math.Clamp(duration * 0.0005, 0.75, 3.0);
        return position >= duration - tolerance;
    }
}
