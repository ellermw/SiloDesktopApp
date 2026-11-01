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
    [InlineData("start", "end", "\"5\"", "\"30\"")]
    [InlineData("start_seconds", "end_seconds", "\"5\"", "\"30\"")]
    [InlineData("start", "end", "5", "\"30\"")]
    [InlineData("start_seconds", "end_seconds", "\"5\"", "30")]
    public void String_encoded_timing_matches_watch_ranges(string startKey, string endKey, string start, string end)
    {
        var original = new TimeRange { Start = 5, End = 30, Source = "scanner", Confidence = .8 };
        var version = new FileVersion { Intro = original };
        using var payload = JsonDocument.Parse($$$"""{"intro":{"{{{startKey}}}":{{{start}}},"{{{endKey}}}":{{{end}}}}}""");
        PlaybackMarkerUpdate.Apply(version, payload.RootElement);
        Assert.Same(original, version.Intro);
        Assert.Equal(5, version.Intro!.Start); Assert.Equal(30, version.Intro.End);
        Assert.Equal(.8, version.Intro.Confidence);
    }
    [Theory]
    [InlineData("confidence", "\"0.9\"", .9)]
    [InlineData("confidence", "\"invalid\"", null)]
    [InlineData("confidence", "{}", null)]
    [InlineData("confidence", "false", null)]
    [InlineData("source", "5", .8)]
    [InlineData("provider", "[]", .8)]
    [InlineData("algorithm", "true", .8)]
    public void Optional_provenance_types_do_not_interrupt_live_timing_updates(string field, string value, double? confidence)
    {
        var version = new FileVersion { Intro = new() { Start = 1, End = 4, Source = "plugin", Provider = "introdb", Confidence = .8, Algorithm = "v4" } };
        using var payload = JsonDocument.Parse($$$"""{"marker_segments":[{"kind":"intro","start_seconds":5,"end_seconds":30}],"intro":{"start":5,"end":30,"{{{field}}}":{{{value}}}},"credits":{"start":90,"end":100},"recap":{"start":1,"end":4},"preview":{"start":70,"end":75}}""");
        PlaybackMarkerUpdate.Apply(version, payload.RootElement);
        Assert.Single(version.MarkerSegments!);
        Assert.Equal(5, version.Intro!.Start); Assert.Equal(30, version.Intro.End);
        Assert.Equal(confidence, version.Intro.Confidence);
        Assert.Equal(field == "source" ? null : "plugin", version.Intro.Source);
        Assert.Equal(field == "provider" ? null : "introdb", version.Intro.Provider);
        Assert.Equal(field == "algorithm" ? null : "v4", version.Intro.Algorithm);
        Assert.Equal(100, version.Credits!.End); Assert.Equal(4, version.Recap!.End); Assert.Equal(75, version.Preview!.End);
    }
    [Theory]
    [InlineData("\"5\"", "\"30\"")]
    [InlineData("5", "\"30\"")]
    [InlineData("\"5\"", "30")]
    public void String_encoded_segment_timing_applies_alongside_singular_ranges(string start, string end)
    {
        var version = new FileVersion();
        using var payload = JsonDocument.Parse($$$"""{"marker_segments":[{"kind":"intro","start_seconds":{{{start}}},"end_seconds":{{{end}}}}],"intro":{"start":5,"end":30},"credits":{"start":90,"end":100}}""");
        PlaybackMarkerUpdate.Apply(version, payload.RootElement);
        var segment = Assert.Single(version.MarkerSegments!);
        Assert.Equal("intro", segment.Kind);
        Assert.Equal(5, segment.StartSeconds); Assert.Equal(30, segment.EndSeconds);
        Assert.Equal(30, version.Intro!.End); Assert.Equal(100, version.Credits!.End);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("5")]
    [InlineData("\"invalid\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"kind\":false,\"start_seconds\":5,\"end_seconds\":30}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":null,\"end_seconds\":30}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":\"invalid\",\"end_seconds\":30}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":\"NaN\",\"end_seconds\":30}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":5,\"end_seconds\":\"Infinity\"}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":-1,\"end_seconds\":30}")]
    [InlineData("{\"kind\":\"intro\",\"start_seconds\":5,\"end_seconds\":5}")]
    public void Malformed_segments_do_not_discard_valid_occurrences_or_singular_ranges(string malformed)
    {
        var version = new FileVersion();
        using var payload = JsonDocument.Parse($$$"""{"marker_segments":[{{{malformed}}},{"kind":"intro","start_seconds":5,"end_seconds":30},{{{malformed}}},{"kind":"intro","start_seconds":40,"end_seconds":50},{{{malformed}}}],"intro":{"start":5,"end":30},"credits":{"start":90,"end":100},"recap":{"start":1,"end":4},"preview":{"start":70,"end":75}}""");
        PlaybackMarkerUpdate.Apply(version, payload.RootElement);
        Assert.Equal(2, version.MarkerSegments!.Count);
        Assert.Equal(30, PlaybackMarkerRanges.Active(version.MarkerSegments, "intro", 10)?.End);
        Assert.Equal(50, PlaybackMarkerRanges.Active(version.MarkerSegments, "intro", 45)?.End);
        Assert.Equal(30, version.Intro!.End); Assert.Equal(100, version.Credits!.End);
        Assert.Equal(4, version.Recap!.End); Assert.Equal(75, version.Preview!.End);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("\"invalid\"")]
    public void Non_array_segments_leave_accepted_occurrences_unchanged(string segments)
    {
        var version = new FileVersion { MarkerSegments = [new() { Kind = "intro", StartSeconds = 0, EndSeconds = 30 }] };
        var original = version.MarkerSegments;
        using var payload = JsonDocument.Parse($$$"""{"marker_segments":{{{segments}}},"credits":{"start":90,"end":100}}""");
        PlaybackMarkerUpdate.Apply(version, payload.RootElement);
        Assert.Same(original, version.MarkerSegments);
        Assert.Equal(30, PlaybackMarkerRanges.Active(version.MarkerSegments!, "intro", 10)?.End);
        Assert.Equal(100, version.Credits!.End);

        var withoutSegments = new FileVersion();
        PlaybackMarkerUpdate.Apply(withoutSegments, payload.RootElement);
        Assert.Null(withoutSegments.MarkerSegments);
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
