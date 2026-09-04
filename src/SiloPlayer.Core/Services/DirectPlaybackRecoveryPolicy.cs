namespace SiloPlayer.Core.Services;

/// <summary>
/// Coordinates direct-stream relay timeouts with player-level route recovery.
/// </summary>
public static class DirectPlaybackRecoveryPolicy
{
    /// <summary>Maximum time the relay waits for upstream media bytes before reconnecting.</summary>
    public static TimeSpan UpstreamIdleTimeout { get; } = TimeSpan.FromSeconds(20);

    /// <summary>Creates the stall detector used by the native playback service.</summary>
    public static PlaybackStallDetector CreateStallDetector() => new(
        bufferingTimeout: TimeSpan.FromSeconds(30),
        silentPlaybackTimeout: TimeSpan.FromSeconds(30));

    /// <summary>
    /// Returns whether an active relay reconnect still owns the current recovery attempt.
    /// </summary>
    public static bool ShouldDeferRouteEscalation(TimeSpan? relayRecoveryElapsed) =>
        relayRecoveryElapsed is { } elapsed &&
        elapsed >= TimeSpan.Zero &&
        elapsed < TimeSpan.FromSeconds(15);
}
