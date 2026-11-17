using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// Official WebUI AudiobookGroupsView #4397 (749db96ac): one series cover that
/// falls through failed URLs, a run bar of the series' books and listening
/// text that lists every count the bar shows.
/// </summary>
public sealed class AudiobookSeriesPresentationTests
{
    [Fact]
    public void ListeningSaysAllFinishedOnlyWhenEveryBookIsFinished()
    {
        Assert.Equal(new[] { "web.audiobooks.groups.statAllFinished" }, Keys(4, 4, 0));
        // Server counts can disagree; all finished still wins once they match.
        Assert.Equal(new[] { "web.audiobooks.groups.statAllFinished" }, Keys(4, 4, 2));
        Assert.Empty(Keys(0, 0, 0));
        Assert.Empty(Keys(5, 0, 0));
    }

    [Fact]
    public void ListeningListsFinishedThenInProgressInsteadOfPreferringInProgress()
    {
        Assert.Equal(new[] { "web.audiobooks.groups.statFinished", "web.audiobooks.groups.statInProgress" }, Keys(10, 3, 2));
        Assert.Equal(new[] { "web.audiobooks.groups.statFinished" }, Keys(10, 3, 0));
        Assert.Equal(new[] { "web.audiobooks.groups.statInProgress" }, Keys(10, 0, 2));
        Assert.Equal(new[] { 3, 2 }, AudiobookSeriesPresentation.ListeningParts(10, 3, 2).Select(part => part.Count!.Value).ToArray());
    }

    [Fact]
    public void ListeningTextJoinsCountsWithAMiddleDotInTheInterfaceLanguage()
    {
        using (AppStrings.UseForCurrentFlow(Locale("en")))
        {
            Assert.Equal("3 finished · 2 in progress", AudiobookSeriesPresentation.Listening(Group(10, 3, 2)));
            Assert.Equal("1 in progress", AudiobookSeriesPresentation.Listening(Group(10, 0, 1)));
            Assert.Equal("all finished", AudiobookSeriesPresentation.Listening(Group(7, 7, 0)));
            Assert.Null(AudiobookSeriesPresentation.Listening(Group(7, 0, 0)));
            Assert.Equal("12 books", AudiobookSeriesPresentation.BooksText(Group(12, 0, 0)));
            Assert.Equal("1,204 finished", AudiobookSeriesPresentation.Listening(Group(2000, 1204, 0)));
        }
    }

    [Fact]
    public void RunClampsCountsAsUpstream()
    {
        Assert.Equal(new AudiobookSeriesRun(1, 0, 0), AudiobookSeriesPresentation.Run(0, 0, 0));
        Assert.Equal(new AudiobookSeriesRun(1, 1, 0), AudiobookSeriesPresentation.Run(0, 3, 1));
        Assert.Equal(new AudiobookSeriesRun(5, 5, 0), AudiobookSeriesPresentation.Run(5, 9, 2));
        Assert.Equal(new AudiobookSeriesRun(5, 3, 2), AudiobookSeriesPresentation.Run(5, 3, 4));
        Assert.Equal(new AudiobookSeriesRun(5, 0, 0), AudiobookSeriesPresentation.Run(5, -1, -2));
    }

    [Fact]
    public void RunDrawsASegmentPerBookUpToTwelveWithFinishedFullAndInProgressHalf()
    {
        var run = AudiobookSeriesPresentation.Run(6, 2, 3);
        Assert.False(run.IsContinuous);
        Assert.Equal(6, run.SegmentCount);
        Assert.Equal(new[] { 1d, 1d, 0.5d, 0.5d, 0.5d, 0d }, Enumerable.Range(0, run.SegmentCount).Select(index => run.SegmentFill(index)).ToArray());

        var twelve = AudiobookSeriesPresentation.Run(AudiobookSeriesPresentation.MaxRunSegments, 0, 0);
        Assert.False(twelve.IsContinuous);
        Assert.Equal(12, twelve.SegmentCount);

        var single = AudiobookSeriesPresentation.Run(1, 0, 1);
        Assert.Equal(1, single.SegmentCount);
        Assert.Equal(0.5d, single.SegmentFill(0));
    }

    [Fact]
    public void RunPastTwelveBooksIsOneContinuousBarWithInProgressAtHalfABook()
    {
        var run = AudiobookSeriesPresentation.Run(20, 5, 4);
        Assert.True(run.IsContinuous);
        Assert.Equal(1, run.SegmentCount);
        Assert.Equal(0.25d, run.FinishedFraction, 6);
        Assert.Equal(0.10d, run.InProgressFraction, 6);
        Assert.Equal(0.35d, run.ContinuousFill, 6);

        var done = AudiobookSeriesPresentation.Run(13, 13, 0);
        Assert.True(done.IsContinuous);
        Assert.Equal(1d, done.ContinuousFill, 6);
    }

    [Fact]
    public void CoverFallsThroughFailedUrlsAndRetriesNewUrlsAfterARefetch()
    {
        var failed = new HashSet<string>(StringComparer.Ordinal);
        string[] urls = ["https://a.invalid/1", "https://a.invalid/2", "https://a.invalid/3"];
        Assert.Equal("https://a.invalid/1", AudiobookSeriesPresentation.FirstUntriedCover(urls, failed));

        failed.Add("https://a.invalid/1");
        Assert.Equal("https://a.invalid/2", AudiobookSeriesPresentation.FirstUntriedCover(urls, failed));

        failed.Add("https://a.invalid/2");
        failed.Add("https://a.invalid/3");
        Assert.Null(AudiobookSeriesPresentation.FirstUntriedCover(urls, failed));

        // Failed URLs, not a count: re-signed URLs from a refetch are tried again.
        string[] refetched = ["https://a.invalid/1?sig=new", "https://a.invalid/2"];
        Assert.Equal("https://a.invalid/1?sig=new", AudiobookSeriesPresentation.FirstUntriedCover(refetched, failed));

        Assert.Null(AudiobookSeriesPresentation.FirstUntriedCover(Array.Empty<string>(), failed));
        Assert.Null(AudiobookSeriesPresentation.FirstUntriedCover(null, failed));
        Assert.Equal("https://a.invalid/9", AudiobookSeriesPresentation.FirstUntriedCover(new[] { "", "  ", "https://a.invalid/9" }, failed));
    }

    private static string[] Keys(int items, int finished, int inProgress)
        => AudiobookSeriesPresentation.ListeningParts(items, finished, inProgress).Select(part => part.Key).ToArray();

    private static AudiobookGroup Group(int items, int finished, int inProgress)
        => new() { Name = "Series", ItemCount = items, FinishedCount = finished, InProgressCount = inProgress };

    private static AppLocale Locale(string language) => new LocaleService().Create(AppLocaleResolution.Resolve(language, [], "en-US"));
}
