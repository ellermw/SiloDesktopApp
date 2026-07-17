using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MpvNetworkBufferSizingTests
{
    [Theory]
    [InlineData(0, 256, 64, 30, 1)]
    [InlineData(29_999, 256, 64, 30, 1)]
    [InlineData(30_000, 384, 96, 40, 2)]
    [InlineData(79_999, 384, 96, 40, 2)]
    [InlineData(80_000, 512, 128, 45, 4)]
    [InlineData(125_000, 512, 128, 45, 4)]
    public void SelectsBoundedProfileForSourceBitrate(
        int bitrateKbps,
        int maxMiB,
        int backMiB,
        int readAheadSeconds,
        int streamMiB)
    {
        var profile = MpvNetworkBufferSizing.ForBitrateKbps(bitrateKbps);

        Assert.Equal(maxMiB, profile.MaxMiB);
        Assert.Equal(backMiB, profile.BackMiB);
        Assert.Equal(readAheadSeconds, profile.ReadAheadSeconds);
        Assert.Equal(streamMiB, profile.StreamMiB);
    }
}
