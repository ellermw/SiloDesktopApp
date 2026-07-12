namespace SiloPlayer.Core.Services;

/// <summary>
/// Maps a transport-local player timeline onto the canonical media timeline
/// used by progress, markers, watch-together, and the UI.
/// </summary>
public static class PlaybackTimeline
{
    public static double ToMediaTime(double playerSeconds, double timelineOffsetSeconds)
        => Math.Max(0, playerSeconds) + Math.Max(0, timelineOffsetSeconds);

    public static double ToPlayerTime(double mediaSeconds, double timelineOffsetSeconds)
        => Math.Max(0, Math.Max(0, mediaSeconds) - Math.Max(0, timelineOffsetSeconds));

    public static double ResolveMediaDuration(
        double playerDurationSeconds,
        double timelineOffsetSeconds,
        double? authoritativeDurationSeconds)
    {
        if (authoritativeDurationSeconds is > 0)
            return authoritativeDurationSeconds.Value;
        return playerDurationSeconds > 0
            ? playerDurationSeconds + Math.Max(0, timelineOffsetSeconds)
            : 0;
    }

    public static bool IsInsideExposedWindow(
        double mediaSeconds,
        double timelineOffsetSeconds,
        double playerWindowDurationSeconds)
    {
        if (playerWindowDurationSeconds <= 0 || mediaSeconds < timelineOffsetSeconds)
            return false;
        return ToPlayerTime(mediaSeconds, timelineOffsetSeconds) <= playerWindowDurationSeconds;
    }
}
