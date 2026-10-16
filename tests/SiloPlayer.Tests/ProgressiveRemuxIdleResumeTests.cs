using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ProgressiveRemuxIdleResumeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T12:00:00Z");
    [Theory]
    [InlineData(59.999, false)]
    [InlineData(60, true)]
    [InlineData(180, true)]
    public void OnlyLongUserPauseReopens(double seconds, bool expected)
    {
        var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, false, Now);
        Assert.Equal(expected, state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(seconds)));
    }
    [Theory]
    [InlineData(PlaybackTransportKind.DirectProgressive, true, false)]
    [InlineData(PlaybackTransportKind.RemuxHls, false, false)]
    [InlineData(PlaybackTransportKind.TranscodeHls, false, false)]
    [InlineData(PlaybackTransportKind.RemuxProgressive, true, false)]
    [InlineData(PlaybackTransportKind.RemuxProgressive, false, true)]
    public void DirectHlsSeekableAndRoomPlaybackKeepTheirPausePaths(PlaybackTransportKind kind, bool seekable, bool room)
    {
        var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, false, Now);
        Assert.False(state.TryBeginResume(kind, seekable, room, Now.AddMinutes(4)));
    }
    [Fact]
    public void InternalLoadPauseDoesNotStartIdleClockAndRepeatedPausesDoNotResetIt()
    {
        var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, true, Now);
        Assert.False(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddMinutes(2)));
        state.ObservePause(true, false, Now); state.ObservePause(true, false, Now.AddSeconds(55));
        Assert.True(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(60)));
    }
    [Fact]
    public void ResumeReopensOnceAndLaterUserPauseIntentSurvivesPendingReplan()
    {
        var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, false, Now);
        Assert.True(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(65)));
        Assert.False(state.DesiredPaused);
        state.SetPauseIntent(true); Assert.True(state.DesiredPaused);
        state.ObservePause(true, true, Now.AddSeconds(66));
        Assert.True(state.DesiredPaused);
        Assert.False(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(67)));
        state.Reset(); Assert.False(state.IsReopening);
        Assert.False(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(200)));
    }

    [Fact]
    public void ConcurrentResumeCallbacksChooseOneReplan()
    {
        for (var round = 0; round < 200; round++)
        {
            var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, false, Now);
            var winners = 0;
            Parallel.For(0, 32, _ =>
            {
                if (state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(65)))
                    Interlocked.Increment(ref winners);
            });
            Assert.Equal(1, winners);
        }
    }

    [Fact]
    public void CompletingReopenAtomicallyReturnsLatestIntentAndRetiresPauseClock()
    {
        var state = new ProgressiveRemuxIdleResume(); state.ObservePause(true, false, Now);
        Assert.True(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, Now.AddSeconds(65)));
        state.SetPauseIntent(true);
        Assert.True(state.Complete());
        Assert.False(state.IsReopening);
        Assert.Null(state.Complete());
    }

    [Fact]
    public void CompletingPausedReopenStartsAnotherIdleWindowWithoutANativePauseEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ProgressiveRemuxIdleResume();
        state.ObservePause(true, false, now.AddSeconds(-65));
        Assert.True(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, now));
        state.SetPauseIntent(true);
        Assert.True(state.Complete());
        // Native mpv remains paused through the load, so no new pause event is
        // guaranteed. The committed paused load must own the next idle window.
        Assert.False(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, now.AddSeconds(59)));
        Assert.True(state.TryBeginResume(PlaybackTransportKind.RemuxProgressive, false, false, now.AddSeconds(61)));
    }
}
