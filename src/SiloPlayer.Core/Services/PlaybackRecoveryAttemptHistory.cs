namespace SiloPlayer.Core.Services;

public sealed record PlaybackRecoveryAttempt(
    IReadOnlyList<string> AttemptedPlanKeys,
    int AttemptCount);

/// <summary>
/// Tracks only the routes the server already asked this playback attempt to
/// try. Route selection remains entirely server-owned.
/// </summary>
public sealed class PlaybackRecoveryAttemptHistory
{
    public const int MaxAttemptedPlanKeys = 16;
    public const int MaxAttemptCount = 8;

    private readonly List<string> _attemptedPlanKeys = [];
    private int _nextAttemptCount = 1;

    public bool CanAttemptRecovery => _nextAttemptCount <= MaxAttemptCount;

    public PlaybackRecoveryAttempt PrepareFailure(string planAttemptKey)
    {
        if (!CanAttemptRecovery)
        {
            throw new PlaybackPlanTerminalException(
                "recovery_exhausted",
                "Playback failed after repeated recovery attempts.",
                retryable: false);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(planAttemptKey);
        var keys = NormalizePlanKeys(_attemptedPlanKeys.Append(planAttemptKey));
        return new PlaybackRecoveryAttempt(keys, _nextAttemptCount);
    }

    public void CommitFailure(PlaybackRecoveryAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        _attemptedPlanKeys.Clear();
        _attemptedPlanKeys.AddRange(NormalizePlanKeys(attempt.AttemptedPlanKeys));
        _nextAttemptCount = Math.Min(attempt.AttemptCount + 1, MaxAttemptCount + 1);
    }

    public void Reset()
    {
        _attemptedPlanKeys.Clear();
        _nextAttemptCount = 1;
    }

    private static string[] NormalizePlanKeys(IEnumerable<string> planKeys)
        => planKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Reverse()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxAttemptedPlanKeys)
            .Reverse()
            .ToArray();
}
