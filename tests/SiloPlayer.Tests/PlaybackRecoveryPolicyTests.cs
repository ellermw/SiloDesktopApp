using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackRecoveryPolicyTests
{
    [Theory]
    [InlineData("buffering-stalled")]
    [InlineData("position-stalled")]
    [InlineData("end-file")]
    [InlineData("progress-reporting-failed")]
    public void DirectProgressiveNetworkInterruptionsReloadInsideCurrentSession(string reason)
    {
        Assert.True(PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(
            PlaybackTransportKind.DirectProgressive,
            reason));
    }

    [Theory]
    [InlineData("file-load-timeout")]
    [InlineData("playback-error")]
    public void SessionOrDecoderFailuresUseReplacementSession(string reason)
    {
        Assert.False(PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(
            PlaybackTransportKind.DirectProgressive,
            reason));
    }

    [Theory]
    [InlineData(PlaybackTransportKind.RemuxProgressive)]
    [InlineData(PlaybackTransportKind.RemuxHls)]
    [InlineData(PlaybackTransportKind.TranscodeHls)]
    public void PreparedTransportsAlwaysUseReplacementSession(PlaybackTransportKind transportKind)
    {
        Assert.False(PlaybackRecoveryPolicy.CanReloadCurrentDirectSession(
            transportKind,
            "buffering-stalled"));
    }
}
