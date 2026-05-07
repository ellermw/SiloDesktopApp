using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

public class PlaybackStallDetectorTests
{
    [Fact]
    public void RequestsRecoveryWhenBufferedPlaybackStopsAdvancingPastThreshold()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(
            position: 1762.8,
            duration: 3180,
            isPaused: false,
            isBufferingForCache: false,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch);

        detector.Observe(
            position: 1762.8,
            duration: 3180,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(1));

        var decision = detector.Observe(
            position: 1762.8,
            duration: 3180,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(22));

        Assert.True(decision.ShouldRecover);
        Assert.Equal("buffering-stalled", decision.Reason);
        Assert.Equal(1762.8, decision.Position);
    }

    [Fact]
    public void DoesNotRecoverWhenUserPausedWithoutCacheBuffering()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(120, 3000, isPaused: false, isBufferingForCache: false, isRecoveryInProgress: false, DateTimeOffset.UnixEpoch);

        var decision = detector.Observe(
            position: 120,
            duration: 3000,
            isPaused: true,
            isBufferingForCache: false,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddMinutes(5));

        Assert.False(decision.ShouldRecover);
    }

    [Fact]
    public void PositionAdvanceResetsTheStallTimer()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(300, 3000, isPaused: false, isBufferingForCache: true, isRecoveryInProgress: false, DateTimeOffset.UnixEpoch);
        detector.Observe(305, 3000, isPaused: false, isBufferingForCache: true, isRecoveryInProgress: false, DateTimeOffset.UnixEpoch.AddSeconds(19));

        var decision = detector.Observe(
            position: 305,
            duration: 3000,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(25));

        Assert.False(decision.ShouldRecover);
    }

    [Fact]
    public void RequestsRecoveryWhenPlaybackClaimsPlayingButPositionDoesNotAdvance()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(600, 3000, isPaused: false, isBufferingForCache: false, isRecoveryInProgress: false, DateTimeOffset.UnixEpoch);

        var decision = detector.Observe(
            position: 600,
            duration: 3000,
            isPaused: false,
            isBufferingForCache: false,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(46));

        Assert.True(decision.ShouldRecover);
        Assert.Equal("position-stalled", decision.Reason);
        Assert.Equal(600, decision.Position);
    }

    [Fact]
    public void RequestsRecoveryNearEndWhenMediaHasNotActuallyEnded()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(
            position: 2530,
            duration: 2590,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch);

        var decision = detector.Observe(
            position: 2530,
            duration: 2590,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(21));

        Assert.True(decision.ShouldRecover);
        Assert.Equal("buffering-stalled", decision.Reason);
    }

    [Fact]
    public void DoesNotRecoverInsideStrictMediaEndTolerance()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(
            position: 2589.5,
            duration: 2590,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch);

        var decision = detector.Observe(
            position: 2589.5,
            duration: 2590,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(21));

        Assert.False(decision.ShouldRecover);
    }
}
