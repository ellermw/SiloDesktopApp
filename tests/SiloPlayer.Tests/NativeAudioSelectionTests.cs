using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class NativeAudioSelectionTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void OriginalFileAppliesSourceOrdinalInsteadOfKeepingDefault(int sourceIndex, int expected)
    {
        var selected = 1; // Korean is the file's default, English is second.
        NativeAudioSelection.Apply(PlaybackTransportKind.DirectProgressive, sourceIndex,
            [1, 2], id => selected = id, () => selected);
        Assert.Equal(expected, selected);
    }

    [Fact]
    public void UsesActualMpvTrackIdsRatherThanAssumingOrdinalPlusOne()
    {
        var selected = 4;
        NativeAudioSelection.Apply(PlaybackTransportKind.DirectProgressive, 1,
            [4, 9], id => selected = id, () => selected);
        Assert.Equal(9, selected);
    }

    [Theory]
    [InlineData(PlaybackTransportKind.RemuxProgressive)]
    [InlineData(PlaybackTransportKind.RemuxHls)]
    [InlineData(PlaybackTransportKind.TranscodeHls)]
    public void PackagedStreamSelectsItsDeliveredTrackNotSourceOrdinal(PlaybackTransportKind transport)
    {
        var selected = 2; // Previous original-file selection must not leak.
        NativeAudioSelection.Apply(transport, 1, [1], id => selected = id, () => selected);
        Assert.Equal(1, selected);
    }

    [Fact]
    public void MissingSourceTrackDoesNotSilentlyPlayDefault()
    {
        var selected = 1;
        Assert.Throws<InvalidOperationException>(() => NativeAudioSelection.Apply(
            PlaybackTransportKind.DirectProgressive, 1, [1], id => selected = id, () => selected));
        Assert.Equal(1, selected);
    }

    [Fact]
    public void RejectedNativeSelectionIsNotReportedAsSuccess()
    {
        Assert.Throws<InvalidOperationException>(() => NativeAudioSelection.Apply(
            PlaybackTransportKind.DirectProgressive, 1, [1, 2], _ => { }, () => 1));
    }

    [Fact]
    public void VideoWithoutAudioDoesNotFail()
    {
        var selected = 0;
        NativeAudioSelection.Apply(PlaybackTransportKind.DirectProgressive, 0,
            [], id => selected = id, () => selected);
        Assert.Equal(0, selected);
    }
}
