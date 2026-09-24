using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackMarkerRangeTests
{
    [Fact]
    public void SeparatedRangesNeverSkipInterveningContent()
    {
        var segments = new List<PlaybackMarkerSegment>
        {
            new() { Kind = "intro", StartSeconds = 0, EndSeconds = 20 },
            new() { Kind = "intro", StartSeconds = 60, EndSeconds = 80 },
        };
        Assert.Equal(20, PlaybackMarkerRanges.Active(segments, "intro", 10)?.End);
        Assert.Null(PlaybackMarkerRanges.Active(segments, "intro", 30));
        Assert.Equal(80, PlaybackMarkerRanges.Active(segments, "intro", 60)?.End);
        Assert.Null(PlaybackMarkerRanges.Active(segments, "intro", 80));
    }
}
