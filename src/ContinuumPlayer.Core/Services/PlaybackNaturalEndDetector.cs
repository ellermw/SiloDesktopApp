namespace ContinuumPlayer.Core.Services;

public readonly record struct PlaybackNaturalEndDecision(bool ShouldComplete, string Reason, double Position, double Duration)
{
    public static PlaybackNaturalEndDecision None(double position, double duration) =>
        new(false, "", position, duration);
}

public sealed class PlaybackNaturalEndDetector
{
    private const double SignificantPositionAdvanceSeconds = 0.25;
    private readonly TimeSpan _completionDelay;

    private double _lastPosition = double.NaN;
    private DateTimeOffset? _nearEndSince;

    public PlaybackNaturalEndDetector(TimeSpan completionDelay)
    {
        if (completionDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(completionDelay));

        _completionDelay = completionDelay;
    }

    public void Reset()
    {
        _lastPosition = double.NaN;
        _nearEndSince = null;
    }

    public PlaybackNaturalEndDecision Observe(
        double position,
        double duration,
        bool isRecoveryInProgress,
        DateTimeOffset now)
    {
        if (duration <= 0 || position < 0 || isRecoveryInProgress)
        {
            Reset();
            return PlaybackNaturalEndDecision.None(position, duration);
        }

        var completionThreshold = duration - CompletionTolerance(duration);
        if (position < completionThreshold)
        {
            Reset();
            return PlaybackNaturalEndDecision.None(position, duration);
        }

        if (double.IsNaN(_lastPosition) ||
            Math.Abs(position - _lastPosition) > SignificantPositionAdvanceSeconds)
        {
            _lastPosition = position;
            _nearEndSince = now;
            return PlaybackNaturalEndDecision.None(position, duration);
        }

        _lastPosition = position;
        _nearEndSince ??= now;

        if (now - _nearEndSince.Value >= _completionDelay)
            return new PlaybackNaturalEndDecision(true, "logical-natural-end", position, duration);

        return PlaybackNaturalEndDecision.None(position, duration);
    }

    private static double CompletionTolerance(double duration)
    {
        var scaled = duration * 0.0005;
        return Math.Clamp(scaled, 0.75, 3.0);
    }
}
