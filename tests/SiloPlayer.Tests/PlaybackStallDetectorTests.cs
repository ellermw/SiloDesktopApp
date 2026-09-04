using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class PlaybackStallDetectorTests
{
    [Fact]
    public void DirectPlaybackPolicyLetsTheRelayReconnectBeforeEscalatingTheRoute()
    {
        var detector = DirectPlaybackRecoveryPolicy.CreateStallDetector();

        detector.Observe(
            position: 1200,
            duration: 5400,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch);

        var duringRelayIdleWindow = detector.Observe(
            position: 1200,
            duration: 5400,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(21));
        var duringReconnectGrace = detector.Observe(
            position: 1200,
            duration: 5400,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(26));
        var afterReconnectGrace = detector.Observe(
            position: 1200,
            duration: 5400,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(31));

        Assert.False(duringRelayIdleWindow.ShouldRecover);
        Assert.False(duringReconnectGrace.ShouldRecover);
        Assert.True(afterReconnectGrace.ShouldRecover);
        Assert.Equal("buffering-stalled", afterReconnectGrace.Reason);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(15, false)]
    [InlineData(60, false)]
    public void DirectPlaybackPolicyBoundsRelayRecoveryDeferral(
        double recoverySeconds,
        bool expectedDeferral)
    {
        var shouldDefer = DirectPlaybackRecoveryPolicy.ShouldDeferRouteEscalation(
            TimeSpan.FromSeconds(recoverySeconds));

        Assert.Equal(expectedDeferral, shouldDefer);
    }

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
    public void RequestsRecoveryWhenOpeningSecondsRemainBufferStalled()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));

        detector.Observe(
            position: 2.5,
            duration: 7200,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch);

        var decision = detector.Observe(
            position: 2.5,
            duration: 7200,
            isPaused: true,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            now: DateTimeOffset.UnixEpoch.AddSeconds(21));

        Assert.True(decision.ShouldRecover);
        Assert.Equal("buffering-stalled", decision.Reason);
        Assert.Equal(2.5, decision.Position);
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

    [Fact]
    public void CopyHlsResume_UsesCanonicalMediaTimelineForStallRecovery()
    {
        var detector = new PlaybackStallDetector(
            bufferingTimeout: TimeSpan.FromSeconds(20),
            silentPlaybackTimeout: TimeSpan.FromSeconds(45));
        var mediaPosition = PlaybackTimeline.ToMediaTime(
            playerSeconds: 0,
            timelineOffsetSeconds: 600);
        var mediaDuration = PlaybackTimeline.ResolveMediaDuration(
            playerDurationSeconds: 3000,
            timelineOffsetSeconds: 600,
            authoritativeDurationSeconds: 3600);

        detector.Observe(
            mediaPosition,
            mediaDuration,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            DateTimeOffset.UnixEpoch);
        var decision = detector.Observe(
            mediaPosition,
            mediaDuration,
            isPaused: false,
            isBufferingForCache: true,
            isRecoveryInProgress: false,
            DateTimeOffset.UnixEpoch.AddSeconds(21));

        Assert.True(decision.ShouldRecover);
        Assert.Equal(600, decision.Position);
    }
}
