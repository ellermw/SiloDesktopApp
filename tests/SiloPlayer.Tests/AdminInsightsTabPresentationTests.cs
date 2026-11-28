using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The Playback, Titles and Library tabs and the title panel shaped from reads (official <c>PlaybackTab.tsx</c>, <c>TitlesTab.tsx</c>,
/// <c>LibraryTab.tsx</c>, <c>TitleDetailSheet.tsx</c> at c59798116): KPI values and subtexts, every toggle, sort formatting, bucket and
/// coverage shaping and drilldown queries. Synthetic data only; text is the English catalog.
/// </summary>
public sealed class AdminInsightsTabPresentationTests
{
    public AdminInsightsTabPresentationTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    private static AdminInsightsActivity Activity() => Read<AdminInsightsActivity>(AdminInsightsWire.Activity);

    // ── Playback ──────────────────────────────────────────────

    [Fact]
    public void PlaybackKpisCarryTheOfficialValuesAndSubtexts()
    {
        var data = Activity();
        var kpis = AdminInsightsPlaybackPresentation.Kpis(data);
        Assert.Equal(["Plays", "Watch time", "Completion rate", "Peak concurrent streams"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("12", kpis[0].Value);
        Assert.Equal("12 movies · 12 episodes", kpis[0].Subtext);
        Assert.Equal(DashboardFormat.WatchTime(3600), kpis[1].Value);
        Assert.Equal("12 sessions", kpis[1].Subtext);
        Assert.Equal("75%", kpis[2].Value);
        Assert.Equal("12 of 12 completed", kpis[2].Subtext);
        Assert.Equal(ActivityTone.Success, kpis[2].Accent);
        Assert.Equal("12", kpis[3].Value);
        Assert.StartsWith("on ", kpis[3].Subtext);
        Assert.Equal(ActivityTone.Info, kpis[3].Accent);
        Assert.Null(kpis[0].Accent);
        Assert.Null(AdminInsightsPlaybackPresentation.WatchTimeNotice(data));
    }

    [Fact]
    public void APeakWithNoMomentSaysThereIsNoStreamDataYet()
    {
        var data = Read<AdminInsightsActivity>(AdminInsightsWire.ActivityNulls);
        Assert.Equal("No stream data yet", AdminInsightsPlaybackPresentation.Kpis(data)[3].Subtext);
    }

    [Fact]
    public void SessionsWithoutMeasuredWatchTimeShowTheBannerAndTheMeasuredSubtext()
    {
        var data = Activity();
        data.Coverage.MeasuredSessions = 5;
        data.Coverage.TotalSessions = 12;
        Assert.Equal("Exact watch time is available for 5 of 12 sessions; older sessions are estimated.", AdminInsightsPlaybackPresentation.WatchTimeNotice(data));
        Assert.Equal("Exact for 5 of 12 sessions", AdminInsightsPlaybackPresentation.Kpis(data)[1].Subtext);
        data.Coverage.TotalSessions = 1;
        data.Coverage.MeasuredSessions = 0;
        Assert.Equal("Exact watch time is available for 0 of 1 session; older sessions are estimated.", AdminInsightsPlaybackPresentation.WatchTimeNotice(data));
        data.Coverage.TotalSessions = 0;
        Assert.Null(AdminInsightsPlaybackPresentation.WatchTimeNotice(data));
    }

    [Fact]
    public void ThePlaybackVolumeTogglesChooseTheirChart()
    {
        using var _ = new CultureScope();
        Assert.Equal("method", AdminInsightsPlaybackPresentation.VolumeView(AdminInsightsPlaybackPresentation.VolumeMetric.Plays, AdminInsightsPlaybackPresentation.VolumeSplit.Method));
        Assert.Equal("media", AdminInsightsPlaybackPresentation.VolumeView(AdminInsightsPlaybackPresentation.VolumeMetric.Plays, AdminInsightsPlaybackPresentation.VolumeSplit.Media));
        // Watch time has no split: the split toggle is hidden and the view is watch time whichever it was left on.
        Assert.Equal("watch_time", AdminInsightsPlaybackPresentation.VolumeView(AdminInsightsPlaybackPresentation.VolumeMetric.WatchTime, AdminInsightsPlaybackPresentation.VolumeSplit.Media));
        var data = Activity();
        Assert.Equal(["Direct play", "Direct stream", "Transcode"], DashboardInsightsPresentation.PlaysColumns(data, "method")!.SeriesLabels);
        Assert.Equal(["Movies", "Episodes", "Other"], DashboardInsightsPresentation.PlaysColumns(data, "media")!.SeriesLabels);
        Assert.Equal(["Watch time"], DashboardInsightsPresentation.PlaysColumns(data, "watch_time")!.SeriesLabels);
        Assert.Equal("watch_time", AdminInsightsPlaybackPresentation.MetricView(true));
        Assert.Equal("plays", AdminInsightsPlaybackPresentation.MetricView(false));
    }

    private static AdminInsightsActivity WithPeaks()
    {
        var data = Activity();
        data.PeakStreams =
        [
            new() { Date = "2026-11-20", PeakDirect = 2, PeakRemux = 1, PeakTranscode = 0, PeakStreams = 3, PeakStreamsDirect = 2, PeakStreamsRemux = 1, PeakStreamsTranscode = 0 },
            new() { Date = "2026-11-22", PeakDirect = 1, PeakRemux = 0, PeakTranscode = 4, PeakStreams = 5, PeakStreamsDirect = 1, PeakStreamsRemux = 0, PeakStreamsTranscode = 4 },
        ];
        return data;
    }

    [Fact]
    public void PeakConcurrentStreamsLinesStackedAndTotalOnTheDailyUtcGrid()
    {
        var data = WithPeaks();
        var lines = AdminInsightsPlaybackPresentation.Peak(data, AdminInsightsPlaybackPresentation.PeakView.Lines)!;
        // Three days: the quiet day between is a gap (null), not a zero.
        Assert.Equal(new double?[] { 3, null, 5 }, lines.Points.Select(point => point.Value));
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero), lines.Points[1].T);
        Assert.True(lines.NeutralTotal);
        Assert.Equal("Total", lines.SeriesLabel);
        Assert.Equal(["Direct play", "Direct stream", "Transcode"], lines.Overlays.Select(overlay => overlay.Label));
        Assert.Equal([0, 1, 2], lines.Overlays.Select(overlay => overlay.Slot));
        Assert.Equal(new double?[] { 2, null, 1 }, lines.Overlays[0].Points.Select(point => point.Value));
        Assert.Equal(new double?[] { 0, null, 4 }, lines.Overlays[2].Points.Select(point => point.Value));
        Assert.Empty(lines.Bands);
        Assert.Equal(("Nov 20, 2026", "Nov 22, 2026"), lines.EdgeLabels);

        var stacked = AdminInsightsPlaybackPresentation.Peak(data, AdminInsightsPlaybackPresentation.PeakView.Stacked)!;
        Assert.Empty(stacked.Overlays);
        Assert.False(stacked.NeutralTotal);
        Assert.Equal("Streams", stacked.SeriesLabel);
        Assert.Equal([0, 1, 2], stacked.Bands.Select(band => band.SeriesSlot!.Value));
        Assert.Equal([4d], stacked.Bands[2].Points.Where(point => point.Value > 0).Select(point => point.Value!.Value));

        var total = AdminInsightsPlaybackPresentation.Peak(data, AdminInsightsPlaybackPresentation.PeakView.Total)!;
        Assert.Empty(total.Overlays);
        Assert.Empty(total.Bands);
        Assert.Equal("Streams", total.SeriesLabel);
        Assert.Equal("Nov 21, 2026", AdminInsightsPlaybackPresentation.PeakLabel(total.Points[1].T));
    }

    [Fact]
    public void NoPeakDataIsAnEmptyCardAndAFilterAddsTheScopeNotice()
    {
        var data = Activity();
        data.PeakStreams = [];
        Assert.Null(AdminInsightsPlaybackPresentation.Peak(data, AdminInsightsPlaybackPresentation.PeakView.Lines));
        var filters = AdminInsightsFilterState.Parse(new Dictionary<string, string>(), "UTC", new DateOnly(2026, 11, 26));
        Assert.False(AdminInsightsPlaybackPresentation.ShowsPeakScopeNotice(filters));
        Assert.True(AdminInsightsPlaybackPresentation.ShowsPeakScopeNotice(filters with { LibraryId = 3 }));
        Assert.True(AdminInsightsPlaybackPresentation.ShowsPeakScopeNotice(filters with { UserId = 9 }));
        Assert.Equal("Always shows the whole server — account and library filters don’t apply here.", AdminInsightsText.T("web.admin.insights.activity.peak.scopeNotice"));
    }

    [Fact]
    public void EndReasonsAreLabelledWithTheirShareAndUnknownOnesAreNotRecorded()
    {
        var rows = AdminInsightsPlaybackPresentation.EndReasons(
        [
            new() { Key = "stopped", Sessions = 6 }, new() { Key = "expired", Sessions = 3 }, new() { Key = "terminated", Sessions = 1 },
            new() { Key = "aborted", Sessions = 1 }, new() { Key = "replaced", Sessions = 1 }, new() { Key = "mystery", Sessions = 0 },
        ]);
        Assert.Equal(["Stopped", "Timed out", "Stopped by admin", "Ended by server", "Switched stream", "Not recorded"], rows.Select(row => row.Label));
        Assert.Equal(["50%", "25%", "8.3%", "8.3%", "8.3%", "0%"], rows.Select(row => row.Secondary));
        Assert.Equal([6d, 3d, 1d, 1d, 1d, 0d], rows.Select(row => row.Value));
        Assert.Empty(AdminInsightsPlaybackPresentation.EndReasons([]));
    }

    // ── Titles ────────────────────────────────────────────────

    private static AdminInsightsTitles Titles(string sort = "plays")
    {
        var data = Read<AdminInsightsTitles>(AdminInsightsWire.Titles);
        data.Sort = sort;
        data.Movies = [new() { ContentId = "m1", Kind = "movie", Title = "Arrival", Year = 2016, Plays = 12, Viewers = 4, WatchSeconds = 36000, CompletedPlays = 6 }];
        data.Shows = [new() { ContentId = "s1", Kind = "series", Title = "", Plays = 7, Viewers = 2, WatchSeconds = 1800, Episodes = 5, CompletedPlays = 7 }];
        data.LeastFinished = [new() { ContentId = "m2", Kind = "movie", Title = "Heat Haze", Year = null, Plays = 8, Viewers = 3, WatchSeconds = 900, CompletedPlays = 2 }];
        data.Libraries = [new() { LibraryId = "4", Name = "Movies", Plays = 19, Viewers = 5, WatchSeconds = 37800 }];
        return data;
    }

    [Theory]
    [InlineData("plays", "12 plays", "4 viewers · 10h")]
    [InlineData("watch_time", "10h", "12 plays · 4 viewers")]
    [InlineData("viewers", "4 viewers", "12 plays · 10h")]
    public void EachRankingShowsItsOwnMetricAndTheOtherTwoBeside(string sort, string value, string secondary)
    {
        var data = Titles(sort);
        var shown = AdminInsightsTitlesPresentation.ShownSort(data, AdminInsightsTitleSort.Plays);
        var row = AdminInsightsTitlesPresentation.TitleRows(data.Movies, shown)[0];
        Assert.Equal("Arrival (2016)", row.Label);
        Assert.Equal(value, AdminInsightsTitlesPresentation.FormatMetric(shown, row.Value));
        Assert.Equal(secondary, row.Secondary);
        Assert.Equal("m1", row.TitleId);
    }

    [Fact]
    public void WhileANewSortLoadsTheOldRowsKeepTheFormattingOfTheSortTheyWereRankedWith()
    {
        // The toggle says "viewers" but the response on screen is still the plays ranking.
        var onScreen = Titles("plays");
        var shown = AdminInsightsTitlesPresentation.ShownSort(onScreen, AdminInsightsTitleSort.Viewers);
        Assert.Equal(AdminInsightsTitleSort.Plays, shown);
        Assert.Equal("12 plays", AdminInsightsTitlesPresentation.FormatMetric(shown, AdminInsightsTitlesPresentation.TitleRows(onScreen.Movies, shown)[0].Value));
        // With nothing on screen the toggle decides; an unknown server value reads as plays.
        Assert.Equal(AdminInsightsTitleSort.Viewers, AdminInsightsTitlesPresentation.ShownSort(null, AdminInsightsTitleSort.Viewers));
        Assert.Equal(AdminInsightsTitleSort.Plays, AdminInsightsTitlesPresentation.ShownSort(Titles("future"), AdminInsightsTitleSort.WatchTime));
    }

    [Fact]
    public void ASeriesLeadsItsSecondaryLineWithItsEpisodesAndARemovedTitleIsWordedAsSuch()
    {
        var data = Titles("plays");
        var row = AdminInsightsTitlesPresentation.TitleRows(data.Shows, AdminInsightsTitleSort.Plays)[0];
        Assert.Equal("Removed title", row.Label);
        Assert.StartsWith("5 episodes · 2 viewers · ", row.Secondary);
        Assert.Equal("s1", row.TitleId);
        Assert.Equal("1 episode", AdminInsightsRules.EpisodeCount(1));
    }

    [Fact]
    public void ALibraryRowNarrowsThePageToThatLibrary()
    {
        var row = AdminInsightsTitlesPresentation.LibraryRows(Titles().Libraries, AdminInsightsTitleSort.WatchTime)[0];
        Assert.Equal("Movies", row.Label);
        Assert.Equal(4, row.LibraryId);
        Assert.Null(row.TitleId);
        Assert.Equal(37800d, row.Value);
        Assert.Equal("19 plays · 5 viewers", row.Secondary);
        var narrowed = AdminInsightsFilters.WithLibrary(new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string> { ["tab"] = "titles", ["title"] = "m1" }), row.LibraryId);
        Assert.Equal("4", narrowed.Get("library_id"));
        Assert.Equal("m1", narrowed.Get("title"));
    }

    [Fact]
    public void LeastFinishedRanksByTheShareLeftUnfinishedWhateverTheSort()
    {
        foreach (var sort in new[] { "plays", "watch_time", "viewers" })
        {
            var row = AdminInsightsTitlesPresentation.LeastFinishedRows(Titles(sort).LeastFinished)[0];
            Assert.Equal("Heat Haze", row.Label);
            Assert.Equal(75d, row.Value);
            Assert.Equal("2 of 8 completed", row.Secondary);
            Assert.Equal("75% unfinished", AdminInsightsTitlesPresentation.UnfinishedShare(row.Value));
        }
        var none = Titles().LeastFinished;
        none[0].Plays = 0;
        Assert.Equal(0d, AdminInsightsTitlesPresentation.LeastFinishedRows(none)[0].Value);
    }

    // ── Library ───────────────────────────────────────────────

    private static AdminInsightsLibrary Library()
    {
        var data = Read<AdminInsightsLibrary>(AdminInsightsWire.Library);
        data.Summary = new() { PlayedShare = 0.0625, PlayedTitles = 6, Titles = 96, NeverStarted = 80, AddedTitles = 12, AddedPlayed = 4, MedianDaysToFirstPlay = 3.5 };
        data.Libraries = [new() { LibraryId = "1", Name = "Movies", Titles = 1240, PlayedTitles = 806, NeverStarted = 310, PlayedShare = 0.6499 }];
        data.Genres = [new() { Key = "Drama", Plays = 40, Titles = 12 }, new() { Key = "unknown", Plays = 2, Titles = 1 }];
        data.Decades = [new() { Key = "1990", Plays = 5, Titles = 2 }, new() { Key = "2010", Plays = 9, Titles = 3 }, new() { Key = "unknown", Plays = 1, Titles = 1 }];
        data.TimeToFirstPlay = [new() { Key = "day", Sessions = 3 }, new() { Key = "week", Sessions = 2 }, new() { Key = "month", Sessions = 1 }, new() { Key = "later", Sessions = 1 }, new() { Key = "not_yet", Sessions = 5 }, new() { Key = "x", Sessions = 0 }];
        data.RecentlyAddedUnplayed = [new() { ContentId = "u1", Title = "Quiet Harbor", Year = 2025, LibraryName = "Movies", AddedAt = "2026-11-03T12:00:00Z" }];
        return data;
    }

    [Fact]
    public void LibraryKpisCarryTheOfficialValuesAndSubtexts()
    {
        var data = Library();
        var kpis = AdminInsightsLibraryPresentation.Kpis(data);
        Assert.Equal(["Titles played", "Never started", "Added this period", "Wait for a first play"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("6.3%", kpis[0].Value);
        Assert.Equal("6 of 96 titles", kpis[0].Subtext);
        Assert.Equal(ActivityTone.Success, kpis[0].Accent);
        Assert.Equal("80", kpis[1].Value);
        Assert.StartsWith("Since history began on ", kpis[1].Subtext);
        Assert.Equal("12", kpis[2].Value);
        Assert.Equal("4 played so far", kpis[2].Subtext);
        Assert.Equal(ActivityTone.Info, kpis[2].Accent);
        Assert.Equal("3.5 d", kpis[3].Value);
        Assert.Equal("Median, for titles added this period", kpis[3].Subtext);

        data.OldestHistoryAt = null;
        data.Summary.MedianDaysToFirstPlay = null;
        data.Summary.Titles = 1;
        data.Summary.PlayedTitles = 1;
        var again = AdminInsightsLibraryPresentation.Kpis(data);
        Assert.Null(again[1].Subtext);
        Assert.Equal("—", again[3].Value);
        Assert.Equal("1 of 1 title", again[0].Subtext);
    }

    [Fact]
    public void LibraryCoverageBarsAreThePlayedSharesAgainstAFixedHundred()
    {
        var row = AdminInsightsLibraryPresentation.Coverage(Library())[0];
        Assert.Equal(65d, row.Value);
        Assert.Equal("806 of 1,240 titles · 310 never started", row.Secondary);
        Assert.Equal(1, row.LibraryId);
        Assert.Equal("65% played", DashboardInsightsPresentation.CoverageValueText(row.Value));
        Assert.Equal("12.5% played", DashboardInsightsPresentation.CoverageValueText(12.5));
    }

    [Fact]
    public void GenreRatingFirstPlayAndDecadeRowsUseTheirLabels()
    {
        var data = Library();
        var genres = AdminInsightsLibraryPresentation.PlaysRows(data.Genres);
        Assert.Equal(["Drama", "Unknown"], genres.Select(row => row.Label));
        Assert.Equal(["12 titles", "1 title"], genres.Select(row => row.Secondary));
        Assert.Equal([40d, 2d], genres.Select(row => row.Value));
        Assert.Equal(["Within a day", "Within a week", "Within a month", "Later", "Not played yet", "Unknown"],
            AdminInsightsLibraryPresentation.FirstPlayRows(data.TimeToFirstPlay).Select(row => row.Label));
        Assert.Equal(["1990s", "2010s", "Unknown"], AdminInsightsLibraryPresentation.DecadeLabels(data.Decades));
        var columns = AdminInsightsLibraryPresentation.DecadeColumns(data.Decades)!;
        Assert.Equal([5d, 9d, 1d], columns.Columns.Select(column => column.Total));
        Assert.Equal(["Plays"], columns.SeriesLabels);
        Assert.Equal([0, 1, 2], columns.Columns.Select(column => DashboardInsightsPresentation.ViewingIndex(column.T)));
        Assert.Null(AdminInsightsLibraryPresentation.DecadeColumns([]));
    }

    [Fact]
    public void ARecentlyAddedTitleNobodyPlayedNamesItsLibraryAndDate()
    {
        var row = AdminInsightsLibraryPresentation.Unplayed(Library().RecentlyAddedUnplayed)[0];
        Assert.Equal("u1", row.ContentId);
        Assert.Equal("Quiet Harbor (2025)", row.Title);
        Assert.Equal("Movies · added Nov 3, 2026", row.Added);
    }

    // ── Title panel ───────────────────────────────────────────

    private static AdminInsightsTitle Title(string kind = "movie")
    {
        var data = Read<AdminInsightsTitle>(AdminInsightsWire.Title);
        data.Kind = kind;
        data.ContentId = kind == "series" ? "series-9" : "movie-7";
        data.Title = "Arrival";
        data.Year = 2016;
        data.Summary = new() { Plays = 4, DirectPlays = 2, RemuxPlays = 1, TranscodePlays = 1, CompletedPlays = 3, Viewers = 2, Sessions = 5, WatchSeconds = 7200 };
        return data;
    }

    [Fact]
    public void TheTitlePanelHeadingDescriptionAndKpisFollowTheOfficialSheet()
    {
        var data = Title();
        Assert.Equal("Arrival (2016)", AdminInsightsTitlePanelPresentation.Heading(data));
        Assert.Equal("Insights", AdminInsightsTitlePanelPresentation.Heading(null));
        Assert.Equal("Movie · Nov 20, 2026 – Nov 20, 2026", AdminInsightsTitlePanelPresentation.Description(data));
        Assert.StartsWith("Series · ", AdminInsightsTitlePanelPresentation.Description(Title("series")));
        Assert.Equal("", AdminInsightsTitlePanelPresentation.Description(null));
        var kpis = AdminInsightsTitlePanelPresentation.Kpis(data);
        Assert.Equal(["Plays", "Viewers", "Watch time", "Completion rate"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("4", kpis[0].Value);
        Assert.Equal("2 direct · 1 direct stream · 1 transcode", kpis[0].Subtext);
        Assert.Equal("2", kpis[1].Value);
        Assert.Null(kpis[1].Subtext);
        Assert.Equal(DashboardFormat.WatchTime(7200), kpis[2].Value);
        Assert.Equal("5 sessions", kpis[2].Subtext);
        Assert.Equal("75%", kpis[3].Value);
        Assert.Equal("3 of 4 completed", kpis[3].Subtext);
        data.Summary.Plays = 0;
        data.Summary.CompletedPlays = 0;
        Assert.Equal("0%", AdminInsightsTitlePanelPresentation.Kpis(data)[3].Value);
    }

    [Fact]
    public void ViewHistoryFiltersByTheMovieOrTheSeriesAndKeepsTheScope()
    {
        var filters = AdminInsightsFilterState.Parse(new Dictionary<string, string> { ["library_id"] = "4", ["user_id"] = "11" }, "UTC", new DateOnly(2026, 11, 26));
        var movie = AdminInsightsTitlePanelPresentation.History(Title(), filters, null);
        Assert.Equal(AdminRoute.PlaybackHistory, movie.Route);
        Assert.Equal("movie-7", movie.Get("media_item_id"));
        Assert.Null(movie.Get("series_id"));
        Assert.Equal("4", movie.Get("library_id"));
        Assert.Equal("11", movie.Get("user_id"));
        Assert.Equal("2026-11-20T10:30:00.000Z", movie.Get("started_after"));
        Assert.Equal("2026-11-20T10:30:00.000Z", movie.Get("started_before"));
        var series = AdminInsightsTitlePanelPresentation.History(Title("series"), filters, new AdminInsightsPersonScope(21, "p-9"));
        Assert.Equal("series-9", series.Get("series_id"));
        Assert.Null(series.Get("media_item_id"));
        // A person the panel is scoped to replaces the page's account and adds its profile.
        Assert.Equal("21", series.Get("user_id"));
        Assert.Equal("p-9", series.Get("profile_id"));
    }

    [Fact]
    public void ViewersShowWhereAMovieOrASeriesWasReached()
    {
        var movie = Title();
        var viewer = movie.Viewers[0];
        viewer.LastEpisode = null;
        viewer.FurthestPositionSeconds = 4320;
        movie.DurationSeconds = 7440;
        Assert.Equal("Reached 1h 12m of 2h 4m", AdminInsightsTitlePanelPresentation.ViewerProgress(viewer, movie));
        movie.DurationSeconds = null;
        Assert.Equal("Reached 1h 12m", AdminInsightsTitlePanelPresentation.ViewerProgress(viewer, movie));
        viewer.FurthestPositionSeconds = null;
        Assert.Equal("", AdminInsightsTitlePanelPresentation.ViewerProgress(viewer, movie));

        viewer.LastEpisode = new() { ContentId = "e", SeasonNumber = 2, EpisodeNumber = 4, Title = "The Gift" };
        Assert.Equal("Last: S02E04 · The Gift", AdminInsightsTitlePanelPresentation.ViewerProgress(viewer, Title("series")));

        var row = AdminInsightsTitlePanelPresentation.Viewers(movie)[0];
        Assert.Equal("profile name sample", row.Name);
        Assert.Equal("username sample · Last: S02E04 · The Gift", row.Detail);
        Assert.Equal("12 plays", row.Plays);
        Assert.StartsWith(DashboardFormat.WatchTime(3600) + " · ", row.Meta);
        Assert.Equal(new AdminInsightsPersonScope(7, "7"), row.Person);
    }

    [Fact]
    public void EpisodesAndThePlaysChartFillQuietBuckets()
    {
        var series = Title("series");
        series.Episodes = [new() { ContentId = "e1", SeasonNumber = 1, EpisodeNumber = 2, Title = "Pilot", Plays = 3, Viewers = 1 }];
        var row = AdminInsightsTitlePanelPresentation.Episodes(series)[0];
        Assert.Equal(("S01E02", "Pilot", "3 plays", "1 viewer"), (row.Code, row.Title, row.Plays, row.Viewers));

        var data = Title();
        data.From = "2026-11-18T00:00:00.000Z";
        data.To = "2026-11-21T00:00:00.000Z";
        data.Tz = "UTC";
        data.BucketUnit = "day";
        data.Buckets = [new() { LabelDate = "2026-11-19", Plays = 4 }];
        var columns = AdminInsightsTitlePanelPresentation.PlaysColumns(data)!;
        Assert.Equal([0d, 4d, 0d], columns.Columns.Select(column => column.Total));
        data.Buckets = [new() { LabelDate = "2026-11-19", Plays = 0 }];
        Assert.Null(AdminInsightsTitlePanelPresentation.PlaysColumns(data));
    }

    [Fact]
    public void ThePersonBannerNamesTheProfileOrTheAccount()
    {
        var account = new AdminInsightsPersonScope(9);
        var profile = new AdminInsightsPersonScope(9, "abcdef0123456789");
        Assert.Equal("Plays by 9", AdminInsightsTitlePanelPresentation.ScopedBanner(null, account));
        Assert.Equal("Plays by Kids", AdminInsightsTitlePanelPresentation.ScopedBanner("Kids", profile));
        Assert.Equal("viewer", AdminInsightsPersonNames.NameOf(account, "viewer", null));
        Assert.Null(AdminInsightsPersonNames.NameOf(account, null, null));
        Assert.Equal("Kids", AdminInsightsPersonNames.NameOf(profile, "viewer", [new AdminHistoryProfileOption("abcdef0123456789", "Kids")]));
        // A profile the account no longer lists keeps the People tab's label rather than reading as the account.
        Assert.Equal("Profile abcdef01", AdminInsightsPersonNames.NameOf(profile, "viewer", []));
        Assert.Equal("Profile abcdef01", AdminInsightsPersonNames.NameOf(profile, "viewer", null));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope() => CultureInfo.CurrentCulture = new CultureInfo("en-US");
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
