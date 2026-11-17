using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

/// <summary>
/// Current WebUI book detail surfaces (Silo-Server main 17d5376):
/// web/src/pages/ItemDetail/AudiobookContent.tsx, EbookContent.tsx,
/// web/src/pages/audiobooks/{audiobooks.css,components/*}.
/// </summary>
public sealed class BookDetailParityTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));

    [Fact]
    public void AudiobookDetailUsesTheCurrentWebUiCoverColumnLayout()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var page = ReadPage();
        var presentation = Read("src", "SiloPlayer.Core", "Helpers", "AudiobookDetailPresentation.cs");

        // audiobook-tint: the cover colour washes the page; there is no backdrop.
        Assert.Contains("x:Name=\"AudiobookTint\"", xaml);
        Assert.Contains("ApplyAudiobookTint(item.PosterThumbhash)", page);
        Assert.Contains("ThumbhashDecoder.GetAmbientColor(thumbhash)", page);
        Assert.Contains("BackdropImage.Visibility = audiobook ? Visibility.Collapsed : Visibility.Visible", page);

        // audiobook-detail-side: square cover with the listening controls under it.
        foreach (var name in new[]
                 {
                     "AudiobookSidePanel", "AudiobookCover", "AudiobookCoverImage", "AudiobookActionsPanel",
                     "AudiobookListenButton", "AudiobookListenText", "AudiobookProgressPanel",
                     "AudiobookProgressFill", "AudiobookListenedText", "AudiobookTimeLeftText",
                     "AudiobookListenFromStartButton", "AudiobookCollectionButton", "AudiobookMoreButton",
                     // audiobook-detail-head: series link, title, author and narrator lines.
                     "AudiobookHeadPanel", "AudiobookSeriesText", "AudiobookTitleText",
                     // audiobook-detail-body: facts, overview, genres, chapters and rails.
                     "AudiobookBodyPanel", "AudiobookFactsPanel",
                 })
            Assert.Contains($"x:Name=\"{name}\"", xaml);

        // The retired narrator card, collapsed chapter toggle and sort menu are gone.
        Assert.DoesNotContain("AudiobookNarratorSection", xaml);
        Assert.DoesNotContain("AudiobookChapterSortButton", xaml);
        Assert.DoesNotContain("AudiobookNarratorSection", page);
        Assert.DoesNotContain("OrderByDescending(row => row.DurationSeconds)", page);

        Assert.Contains("web.audiobooks.detail.length", page);
        Assert.Contains("web.audiobooks.detail.released", page);
        Assert.Contains("web.audiobooks.detail.publisher", page);
        Assert.Contains("web.audiobooks.detail.seriesBook", presentation);
        Assert.Contains("web.detail.audiobook.resumeChapter", page);
        Assert.Contains("web.detail.audiobook.listenedProgress", presentation);
        Assert.Contains("web.detail.audiobook.listen", page);
        Assert.Contains("web.audiobooks.chapters.showAll", presentation);
        Assert.Contains("web.audiobooks.chapters.showFewer", presentation);
        Assert.Contains("web.audiobooks.chapters.leftInChapter", presentation);
        Assert.Contains("AudiobookDetailPresentation.ChapterWindow(", page);
        Assert.Contains("AudiobookDetailPresentation.RelatedStatus(", page);
    }

    [Fact]
    public void BookRelatedRailsUseWebUiRelatedRailCards()
    {
        var page = ReadPage();
        // RelatedRail: square audiobook covers, "Book N" kicker, highlighted
        // current entry and a progress bar for started books.
        Assert.Contains("BookRelatedCard(", page);
        Assert.Contains("AudiobookDetailPresentation.BookNumber(entry.SeriesIndex)", page);
        Assert.Contains("AudiobookDetailPresentation.RelatedCardWidth(", page);
        Assert.DoesNotContain("cards.Children.Add(new PosterCard", page);
    }

    [Fact]
    public void ChapterRowsFlattenFilesInServerOrderWithAbsoluteStarts()
    {
        var rows = AudiobookDetailPresentation.BuildChapterRows(
        [
            new FileVersion { FileId = 7, Duration = 600, Chapters = [Chapter(0, "Opening", 0, 300), Chapter(1, "", 300, 600)] },
            new FileVersion { FileId = 8, Duration = 400, Chapters = [Chapter(0, "Part Two", 0, 0)] },
        ]);

        Assert.Equal([1, 2, 3], rows.Select(row => row.PositionIndex));
        Assert.Equal([0d, 300d, 600d], rows.Select(row => row.AbsoluteStart));
        // A chapter without an end has no length (end_seconds ?? start_seconds).
        Assert.Equal([300d, 300d, 0d], rows.Select(row => row.DurationSeconds));
        Assert.Equal([7, 7, 8], rows.Select(row => row.FileId));
        using (AppStrings.UseForCurrentFlow(English))
            Assert.Equal("Chapter 2", rows[1].Label);
    }

    [Theory]
    // Short books list every chapter.
    [InlineData(10, 5, false, 0, 10)]
    // Long books show eight rows with three before the current chapter.
    [InlineData(14, 6, false, 3, 8)]
    [InlineData(14, -1, false, 0, 8)]
    [InlineData(14, 0, false, 0, 8)]
    // The window clamps at the end of the list.
    [InlineData(14, 13, false, 6, 8)]
    [InlineData(14, 6, true, 0, 14)]
    public void ChapterWindowMatchesChaptersSection(int rows, int current, bool expanded, int start, int count)
    {
        Assert.Equal((start, count), AudiobookDetailPresentation.ChapterWindow(rows, current, expanded));
        Assert.Equal(rows > 10, AudiobookDetailPresentation.IsCollapsible(rows));
    }

    [Fact]
    public void CurrentAndResumeChaptersFollowFindChapterAt()
    {
        var rows = AudiobookDetailPresentation.BuildChapterRows(
            [new FileVersion { FileId = 1, Duration = 900, Chapters = [Chapter(0, "A", 10, 300), Chapter(1, "B", 300, 900)] }]);

        Assert.Equal(-1, AudiobookDetailPresentation.CurrentChapterIndex(rows, null));
        Assert.Equal(-1, AudiobookDetailPresentation.CurrentChapterIndex(rows, 5));
        Assert.Equal(1, AudiobookDetailPresentation.CurrentChapterIndex(rows, 300));
        // The resume label falls back to the first chapter before its start.
        Assert.Equal("A", AudiobookDetailPresentation.ResumeChapter(rows, 5)!.Title);
        Assert.Equal("B", AudiobookDetailPresentation.ResumeChapter(rows, 450)!.Title);
    }

    [Fact]
    public void ListeningTimesUseTheWebUiFormats()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("27 hr 14 min", AudiobookDetailPresentation.FormatHoursMinutes(27 * 3600 + 14 * 60));
        Assert.Equal("3 hr", AudiobookDetailPresentation.FormatHoursMinutes(3 * 3600 + 10));
        Assert.Equal("1 min", AudiobookDetailPresentation.FormatHoursMinutes(20));
        Assert.Equal("0 min", AudiobookDetailPresentation.FormatHoursMinutes(0));
        Assert.Equal("1 hr 30 min left", AudiobookDetailPresentation.FormatTimeLeft(1800, 7200));
        Assert.Null(AudiobookDetailPresentation.FormatTimeLeft(10, 0));
        Assert.Equal("2h 05m listened · 42%", AudiobookDetailPresentation.ListenedProgress(2 * 3600 + 5 * 60, (2 * 3600 + 5 * 60) / .42));
        Assert.Equal("4m 09s", AudiobookDetailPresentation.FormatListened(249));
        Assert.Equal("12s", AudiobookDetailPresentation.FormatListened(12.9));
        Assert.Equal("1:02:03", AudiobookDetailPresentation.FormatChapterStart(3723));
        Assert.Equal("0:00:00", AudiobookDetailPresentation.FormatChapterStart(0));
        Assert.Equal("12m 04s", AudiobookDetailPresentation.FormatChapterDuration(724));
        Assert.Equal("", AudiobookDetailPresentation.FormatChapterDuration(0));
        Assert.Equal("12m 04s left in this chapter", AudiobookDetailPresentation.LeftInChapter(724));
        Assert.Equal("Show all 14", AudiobookDetailPresentation.ShowAllLabel(14));
        Assert.Equal("Show fewer", AudiobookDetailPresentation.ShowFewerLabel());
        Assert.Equal(100, AudiobookDetailPresentation.ShownPercent(9000, 7200));
    }

    [Fact]
    public void RelatedBooksShowFinishedTimeLeftLengthThenYear()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Finished", AudiobookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { Played = true, ProgressRatio = .5, DurationSeconds = 3600 }));
        Assert.Equal("30 min left", AudiobookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { ProgressRatio = .5, DurationSeconds = 3600 }));
        Assert.Equal("1 hr", AudiobookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { DurationSeconds = 3600, Year = 2020 }));
        Assert.Equal("2020", AudiobookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { Year = 2020 }));
        Assert.Null(AudiobookDetailPresentation.RelatedStatus(new AudiobookRelatedItem()));
        Assert.Null(AudiobookDetailPresentation.RelatedProgress(new AudiobookRelatedItem { Played = true, ProgressRatio = .5 }));
        Assert.Equal(.5, AudiobookDetailPresentation.RelatedProgress(new AudiobookRelatedItem { ProgressRatio = .5 }));
        Assert.Equal("The Saga, book 2", AudiobookDetailPresentation.SeriesLine("The Saga", 2));
        Assert.Equal("The Saga", AudiobookDetailPresentation.SeriesLine("The Saga", null));
        Assert.Equal("Book 2.5", AudiobookDetailPresentation.BookNumber(2.5));
    }

    [Fact]
    public void RelatedPayloadCarriesTheListeningFields()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<AudiobookRelatedItem>(
            """{"content_id":"b","title":"B","poster_thumbhash":"x","series_index":2,"duration_seconds":3600,"played":true,"progress_ratio":0.25}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower })!;
        Assert.Equal(("x", 2d, 3600d, true, .25), (entry.PosterThumbhash, entry.SeriesIndex!.Value, entry.DurationSeconds!.Value, entry.Played!.Value, entry.ProgressRatio!.Value));
    }

    [Theory]
    // Square audiobook covers draw one poster size smaller (squarePosterSize).
    [InlineData("standard", 1280, true, 160)]
    [InlineData("standard", 800, true, 140)]
    [InlineData("standard", 500, true, 120)]
    [InlineData("large", 1280, true, 185)]
    [InlineData("standard", 1280, false, 185)]
    [InlineData("compact", 700, false, 140)]
    public void RelatedCardWidthsFollowCarouselCardWidths(string size, double viewport, bool square, double width)
        => Assert.Equal(width, AudiobookDetailPresentation.RelatedCardWidth(size, viewport, square));

    private static VersionChapter Chapter(int index, string title, double start, double end)
        => new() { Index = index, Title = title, StartSeconds = start, EndSeconds = end };

    private static string ReadPage()
    {
        var folder = Path.Combine(FindRoot(), "src", "SiloPlayer", "Views");
        return string.Join("\n", Directory.GetFiles(folder, "ItemDetailPage*.cs").Order().Select(File.ReadAllText));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([FindRoot(), .. parts]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
