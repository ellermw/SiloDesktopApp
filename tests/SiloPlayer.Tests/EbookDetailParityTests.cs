using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Tests;

/// <summary>
/// Current WebUI ebook detail page (Silo-Server 3b2b32168, #4645:
/// web/src/pages/ItemDetail/EbookContent.tsx on the audiobook layout).
/// </summary>
public sealed class EbookDetailParityTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));

    [Theory]
    // Read, nothing saved.
    [InlineData(false, true, 0.0, false, true, false, false, false, 0)]
    // Continue shows the bar at the saved position.
    [InlineData(false, true, 0.42, false, true, true, false, true, 42)]
    // A finished book offers Read again with a full green bar.
    [InlineData(true, true, 0.42, false, true, false, false, true, 100)]
    // An unstarted or finished saved position never advertises Continue.
    [InlineData(false, true, 1.0, false, true, false, false, false, 0)]
    [InlineData(false, true, double.NaN, false, true, false, false, false, 0)]
    // No file opens in the reader: Download takes the primary slot when allowed.
    [InlineData(false, false, 0.5, true, false, false, true, false, 0)]
    [InlineData(false, false, 0.0, false, false, false, false, false, 0)]
    // Both: Download joins Mark read.
    [InlineData(false, true, 0.0, true, true, false, false, false, 0)]
    public void ActionsFollowEbookContent(bool isRead, bool canRead, double progress, bool canDownload,
        bool showRead, bool continueLabel, bool primaryDownload, bool showProgress, int percent)
    {
        var state = EbookDetailPresentation.BuildActions(isRead, canRead, progress, canDownload);
        Assert.Equal(showRead, state.ShowRead);
        Assert.Equal(continueLabel, state.ContinueLabel);
        Assert.Equal(primaryDownload, state.ShowPrimaryDownload);
        Assert.Equal(canRead && canDownload, state.ShowSecondaryDownload);
        Assert.Equal(showProgress, state.ShowProgress);
        Assert.Equal(percent, state.ProgressPercent);
        Assert.Equal(isRead, state.IsRead);
    }

    [Theory]
    [InlineData(0.001, 1)]
    [InlineData(0.004, 1)]
    [InlineData(0.425, 43)]
    [InlineData(0.5, 50)]
    [InlineData(0.996, 99)]
    public void InProgressPercentStaysBetweenOneAndNinetyNine(double ratio, int percent)
        => Assert.Equal(percent, EbookDetailPresentation.ClampInProgressPercent(ratio));

    [Fact]
    public void FactsAreFormatsSizeYearAndPublisherWithEmptyOnesLeftOut()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        var epub = new FileVersion { FileId = 1, FileName = "book.epub", Container = "zip", FileSize = 2_500_000 };
        var pdf = new FileVersion { FileId = 2, FilePath = "/books/book.PDF", Container = "pdf", FileSize = 9_000_000 };
        var noName = new FileVersion { FileId = 3, Container = ".mobi" };
        var duplicate = new FileVersion { FileId = 4, FileName = "second.epub" };

        var facts = EbookDetailPresentation.Facts([epub, pdf, noName, duplicate], epub, 2020, "Fixture Press");
        Assert.Equal(
            [
                ("web.detail.mediaInfo.row.format", "EPUB, PDF, MOBI"),
                ("web.detail.mediaInfo.row.fileSize", "2.4 MB"),
                ("web.audiobooks.detail.released", "2020"),
                ("web.audiobooks.detail.publisher", "Fixture Press"),
            ],
            facts.Select(fact => (fact.LabelKey, fact.Value)));

        // A book with no files, year or publisher has no facts row at all.
        Assert.Empty(EbookDetailPresentation.Facts([], null, 0, null));
        Assert.Empty(EbookDetailPresentation.Facts([], null, 0, "  "));
        // The size is the file Read opens; a file of unknown size adds none.
        Assert.DoesNotContain(EbookDetailPresentation.Facts([noName], noName, 0, null), fact => fact.LabelKey.EndsWith("fileSize", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024 + 300_000_000, "3.3 GB")]
    public void FileSizeUsesTheWebUiFormat(long bytes, string expected)
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal(expected, EbookDetailPresentation.FormatFileSize(bytes));
    }

    [Fact]
    public void PublisherFallsBackToTheFirstStudio()
    {
        Assert.Equal("Ext Press", EbookDetailPresentation.Publisher("Ext Press", ["Studio"]));
        Assert.Equal("Studio", EbookDetailPresentation.Publisher(" ", ["Studio", "Other"]));
        Assert.Null(EbookDetailPresentation.Publisher(null, []));
        Assert.Null(EbookDetailPresentation.Publisher(null, null));
    }

    [Fact]
    public void RelatedBooksShowReadPercentReadThenYear()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Read", EbookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { Played = true, ProgressRatio = .5, Year = 2020 }));
        Assert.Equal("50% read", EbookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { ProgressRatio = .5, Year = 2020 }));
        Assert.Equal("99% read", EbookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { ProgressRatio = 1 }));
        Assert.Equal("2020", EbookDetailPresentation.RelatedStatus(new AudiobookRelatedItem { Year = 2020 }));
        Assert.Null(EbookDetailPresentation.RelatedStatus(new AudiobookRelatedItem()));
        Assert.Null(EbookDetailPresentation.RelatedProgress(new AudiobookRelatedItem { Played = true, ProgressRatio = .5 }));
        Assert.Equal(.5, EbookDetailPresentation.RelatedProgress(new AudiobookRelatedItem { ProgressRatio = .5 }));
        Assert.Null(EbookDetailPresentation.RelatedProgress(new AudiobookRelatedItem()));
    }

    [Fact]
    public void EbookDetailUsesTheAudiobookLayoutPieces()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        var layout = Read("src", "SiloPlayer", "Views", "ItemDetailPage.BookLayout.cs");
        var ebook = Read("src", "SiloPlayer", "Views", "ItemDetailPage.EbookDetail.cs");
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");

        // The ebook page is the audiobook page's side column, head and body.
        Assert.Contains("ApplyAudiobookLayout(audiobook || ebook, ebook);", page);
        Assert.Contains("ConfigureEbookDetail(item);", page);
        // Side column from md for ebooks (13.5rem), lg for audiobooks (16.25rem); a 2:3 cover.
        Assert.Contains("var twoColumn = large || ebook && width >= 768;", layout);
        Assert.Contains("var cover = large ? 260d : middle ? 216d : small ? 176d : 112d;", layout);
        Assert.Contains("AudiobookCover.Height = ebook ? cover * 1.5 : cover;", layout);
        Assert.Contains("middle ? 40 : small ? 32 : 16", layout);
        Assert.Contains("middle ? 28 : small ? 24 : 20", layout);
        // Facts, the ratings row, the overview, genres, the rails, then Files.
        Assert.Contains("new FrameworkElement[] { BookRelatedSection, MediaLocationsSection }", layout);
        Assert.Contains("ContentRatingBadge, AdvisoryAgeBadge, ScoresPanel", layout);
        foreach (var name in new[]
                 {
                     "EbookReadButton", "EbookPrimaryDownloadButton", "EbookProgressPanel", "EbookProgressFill",
                     "EbookSecondaryActions", "EbookWatchedButton", "EbookDownloadButton", "EbookBadgesRow",
                 })
            Assert.Contains($"x:Name=\"{name}\"", xaml);

        // Every action the ebook page had keeps its request: the reader, Mark read, Download.
        Assert.Contains("Click=\"PlayButton_Click\"", xaml[xaml.IndexOf("x:Name=\"EbookReadButton\"", StringComparison.Ordinal)..]);
        Assert.Contains("ViewModel.ToggleWatchedCommand.ExecuteAsync(null)", ebook);
        Assert.Contains("DownloadButton_Click", xaml[xaml.IndexOf("x:Name=\"EbookDownloadButton\"", StringComparison.Ordinal)..]);
        Assert.Contains("var canDownload = item.Versions.Count > 0 && CanCurrentUserDownload();", ebook);
        // EbookContent has no favorite, rating or more menu; official leaves the hero controls hidden.
        Assert.Contains("FavoriteButton.Visibility = StarRatingContainer.Visibility = MoreButton.Visibility = Visibility.Collapsed;", ebook);

        // The related rails: Book N kicker, status line, progress bar, current book ringed.
        Assert.Contains("EbookDetailPresentation.RelatedStatus(entry)", layout);
        Assert.Contains("kicker: () => AudiobookDetailPresentation.BookNumber(entry.SeriesIndex)", layout);
        Assert.Contains("if (AudiobookDetailPresentation.RelatedProgress(entry) is { } progress)", layout);
        Assert.Contains("startIndex: series.Entries.FindIndex(entry => entry.ContentId == item.ContentId)", layout);
        // Four overview lines, no translate action, and the loading placeholder.
        Assert.Contains("OverviewText.CollapsedLines = 4;", ebook);
        Assert.Contains("item.Type.Equals(\"ebook\", StringComparison.OrdinalIgnoreCase)) return;",
            Read("src", "SiloPlayer", "Views", "ItemDetailPage.Overview.cs"));
        Assert.Contains("ShowBookSkeleton(route?.LibraryId);", page);
        Assert.Contains("if (_currentContentId == contentId) HideBookSkeleton();", page);
        // Like the audiobook page, the ebook page has no hero, so it takes no hero minimum height.
        Assert.Contains("|| string.Equals(ViewModel.Item?.Type, \"ebook\", StringComparison.OrdinalIgnoreCase))", page);
        // The ebook page collapses the audiobook's secondary row; the audiobook page shows it again on a retained page.
        Assert.Contains("AudiobookSecondaryActions.Visibility = Visibility.Collapsed;", ebook);
        Assert.Contains("AudiobookSecondaryActions.Visibility = Visibility.Visible;", layout);
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
