using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Tests;

public sealed class PlaybackNaturalEndDetectorTests
{
    [Fact]
    public void Observe_CompletesWhenPlaybackStallsAtLogicalEnd()
    {
        var now = DateTimeOffset.UtcNow;
        var detector = new PlaybackNaturalEndDetector(TimeSpan.FromSeconds(4));

        var first = detector.Observe(
            position: 3599.6,
            duration: 3600,
            isRecoveryInProgress: false,
            now: now);
        var second = detector.Observe(
            position: 3599.7,
            duration: 3600,
            isRecoveryInProgress: false,
            now: now.AddSeconds(5));

        Assert.False(first.ShouldComplete);
        Assert.True(second.ShouldComplete);
    }

    [Fact]
    public void Observe_DoesNotCompleteWhileStillAdvancingNearEnd()
    {
        var now = DateTimeOffset.UtcNow;
        var detector = new PlaybackNaturalEndDetector(TimeSpan.FromSeconds(4));

        detector.Observe(3597.5, 3600, false, now);
        var advanced = detector.Observe(3599.4, 3600, false, now.AddSeconds(5));

        Assert.False(advanced.ShouldComplete);
    }

    [Fact]
    public void Observe_DoesNotCompleteDuringRecoveryOrAwayFromEnd()
    {
        var now = DateTimeOffset.UtcNow;
        var detector = new PlaybackNaturalEndDetector(TimeSpan.FromSeconds(4));

        var awayFromEnd = detector.Observe(3000, 3600, false, now.AddSeconds(5));
        var duringRecovery = detector.Observe(3599.6, 3600, true, now.AddSeconds(10));

        Assert.False(awayFromEnd.ShouldComplete);
        Assert.False(duringRecovery.ShouldComplete);
    }
}
