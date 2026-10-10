using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NowListeningPresentationTests
{
    [Fact]
    public void DefaultChapterNamesDoNotRepeatAndTheFirstMarkOwnsTheOpeningPosition()
    {
        var chapters = NowListeningPresentation.BuildChapters([
            new() { Duration = 60, Chapters = [new() { Index = 0, StartSeconds = 10 }] },
            new() { Duration = 120, Chapters = [new() { Index = 0, StartSeconds = 5, Title = "The next file" }] },
        ]);
        Assert.Equal("Chapter 1 of 2", NowListeningPresentation.ChapterLine(chapters, 0, 180));
        Assert.Equal("Chapter 2 of 2 · The next file", NowListeningPresentation.ChapterLine(chapters, 65, 180));
        Assert.Equal("Chapter 1", chapters[0].Label);
    }

    [Fact]
    public void SourceFileOrderAndPerFileChapterFallbackNamesArePreserved()
    {
        var chapters = NowListeningPresentation.BuildChapters([
            new() { Duration = 100, PresentationPartIndex = 2, Chapters = [new() { Index = 2, StartSeconds = 0 }] },
            new() { Duration = 50, PresentationPartIndex = 1, Chapters = [new() { Index = 0, StartSeconds = 0 }] },
        ]);
        Assert.Equal("Chapter 1 of 2 · Chapter 3", NowListeningPresentation.ChapterLine(chapters, 0, 150));
        Assert.Equal("Chapter 2 of 2 · Chapter 1", NowListeningPresentation.ChapterLine(chapters, 100, 150));
    }

    [Theory]
    [InlineData(0, 7200, 300, 7200)]
    [InlineData(0, 0, 300, 300)]
    [InlineData(3600, 7200, 300, 3600)]
    public void MissingDetailDurationFallsThroughToFilesThenTheDeck(double detail, double files, double deck, double expected)
        => Assert.Equal(expected, NowListeningPresentation.ResolveDuration(detail, files, deck));

    [Theory]
    [InlineData(29, "1 min")]
    [InlineData(90, "2 min")]
    [InlineData(3600, "1 hr")]
    [InlineData(3660, "1 hr 1 min")]
    public void DurationsUseTheSourceHourMinuteRounding(double seconds, string expected)
        => Assert.Equal(expected, NowListeningPresentation.FormatDuration(seconds));

    [Fact]
    public void NoChaptersShowsTotalDurationAndUnknownDurationHasNoTimeLeft()
    {
        Assert.Equal("2 hr", NowListeningPresentation.ChapterLine([], 1200, 7200));
        Assert.Equal("", NowListeningPresentation.ChapterLine([], 0, 0));
        Assert.Null(NowListeningPresentation.TimeLeft(0, 0));
        Assert.Equal("0 min left", NowListeningPresentation.TimeLeft(8000, 7200));
    }
}
