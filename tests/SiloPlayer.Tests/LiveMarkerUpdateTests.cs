using System.Text.Json;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class LiveMarkerUpdateTests
{
    [Fact]
    public void Withdrawal_clears_segments_and_legacy_range_without_keeping_a_cached_skip()
    {
        var version = new FileVersion { MarkerSegments = [new() { Kind = "intro", StartSeconds = 0, EndSeconds = 30 }], Intro = new() { Start = 0, End = 30 } };
        PlaybackMarkerUpdate.Apply(version, JsonDocument.Parse("""{"marker_segments":[],"intro":null}""").RootElement);
        Assert.Empty(version.MarkerSegments!); Assert.Null(version.Intro);
        Assert.Null(PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 10));
    }
    [Fact]
    public void Partial_update_does_not_remove_an_unmentioned_kind()
    {
        var version = new FileVersion { Recap = new() { Start = 1, End = 9 } };
        PlaybackMarkerUpdate.Apply(version, JsonDocument.Parse("""{"intro":{"start":2,"end":10}}""").RootElement);
        Assert.Equal(10, version.Intro!.End); Assert.Equal(9, version.Recap!.End);
    }
    [Theory]
    [InlineData(1)] [InlineData(50)] [InlineData(100)]
    public void Subtitle_text_opacity_survives_clone_and_wire_round_trip(int opacity)
    {
        var appearance = new SiloPlayer.Core.Models.Settings.SubtitleAppearance { TextOpacity = opacity, FontColor = "#9ca3af" };
        var read = SiloPlayer.Core.Models.Settings.SubtitleAppearance.Parse(appearance.Clone().ToJson());
        Assert.Equal(opacity, read.TextOpacity); Assert.Equal("#9ca3af", read.FontColor);
    }
}
