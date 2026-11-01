using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackMarkerRangeTests
{
    [Fact]
    public void NullAndInvalidWatchSegmentsDoNotHideLaterOccurrences()
    {
        var version = System.Text.Json.JsonSerializer.Deserialize<FileVersion>("""{"MarkerSegments":[null,{"kind":"intro","start_seconds":-1,"end_seconds":20},{"kind":"intro","start_seconds":0,"end_seconds":20},null,{"kind":"intro","start_seconds":60,"end_seconds":80},null]}""")!;
        var provenance = new TimeRange { Source = "plugin", Provider = "introdb", Confidence = .92 };
        Assert.Equal(20, PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 10, provenance)?.End);
        Assert.Null(PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 30, provenance));
        var later = PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 60, provenance);
        Assert.Equal(80, later?.End);
        Assert.Equal("92% · TheIntroDB", MarkerProvenance.Label(later, true, true));
        Assert.Null(PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 80, provenance));
    }

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
