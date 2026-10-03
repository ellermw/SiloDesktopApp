using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleSourceLabelsTests
{
    [Fact]
    public void MeaningfulTitlesAndAccessibilitySurviveWhileFormatOnlyTitlesDoNotRepeat()
    {
        var tracks = new[] {
            new SubtitleTrackInfo { Index = 0, Language = "en", Label = "Director commentary", Forced = true, Source = "embedded" },
            new SubtitleTrackInfo { Index = 1, Language = "en", Label = "SRT", HearingImpaired = true, Source = "downloaded" } };
        var labels = SubtitleSourceLabels.Build(tracks, _ => "English");
        Assert.Equal("English · Director commentary · Forced · embedded", labels[tracks[0]]);
        Assert.Equal("English · SDH · downloaded", labels[tracks[1]]);
    }

    [Fact]
    public void DuplicateTracksReceiveUniqueLabelsEvenWhenIndexesCollide()
    {
        var tracks = new[] {
            new SubtitleTrackInfo { Index = 0, Language = "en", Label = "English SDH", HearingImpaired = true },
            new SubtitleTrackInfo { Index = 0, Language = "en", Label = "English SDH", HearingImpaired = true } };
        var labels = SubtitleSourceLabels.Build(tracks, _ => "English");
        Assert.Equal(2, labels.Values.Distinct().Count());
        Assert.All(labels.Values, text => Assert.Equal(1, text.Split("SDH").Length - 1));
    }
}
