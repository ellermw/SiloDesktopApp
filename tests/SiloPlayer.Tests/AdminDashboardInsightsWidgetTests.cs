using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The six Insights dashboard widgets, the three longer windows and the reworked leaderboards, against the official registry, range
/// helpers and chart components at c59798116.
/// </summary>
public sealed class AdminDashboardInsightsWidgetTests
{
    private static readonly string[] InsightsIds =
        ["insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people", "insights-library-coverage"];

    // ── Ranges ───────────────────────────────────────────────

    [Fact]
    public void TheThreeLongerWindowsHaveTheOfficialNamesLabelsPhrasesTitlesAndEdges()
    {
        Assert.Equal("quarter", DashboardRanges.WireName(DashboardWidgetRange.Quarter));
        Assert.Equal("year", DashboardRanges.WireName(DashboardWidgetRange.Year));
        Assert.Equal("all", DashboardRanges.WireName(DashboardWidgetRange.All));
        Assert.Equal(["90d", "1y", "All"], new[] { DashboardWidgetRange.Quarter, DashboardWidgetRange.Year, DashboardWidgetRange.All }.Select(DashboardRanges.Label));
        Assert.Equal(["the last 90 days", "the last 365 days", "all recorded history"],
            new[] { DashboardWidgetRange.Quarter, DashboardWidgetRange.Year, DashboardWidgetRange.All }.Select(DashboardRanges.Phrase));
        Assert.Equal(["Show the last 90 days", "Show the last 365 days", "Show all recorded history"],
            new[] { DashboardWidgetRange.Quarter, DashboardWidgetRange.Year, DashboardWidgetRange.All }.Select(DashboardRanges.ShowLabel));
        // The no-break spaces keep the window together; only the space beside the widget name may wrap.
        Assert.Equal("Plays · last 90 d", DashboardRanges.Title("Plays", DashboardWidgetRange.Quarter));
        Assert.Equal("Plays · last 365 d", DashboardRanges.Title("Plays", DashboardWidgetRange.Year));
        Assert.Equal("Plays · all time", DashboardRanges.Title("Plays", DashboardWidgetRange.All));
        Assert.Equal(("90d ago", "now"), DashboardRanges.EdgeLabels(DashboardWidgetRange.Quarter));
        Assert.Equal(("1y ago", "now"), DashboardRanges.EdgeLabels(DashboardWidgetRange.Year));
        Assert.Equal(("First record", "now"), DashboardRanges.EdgeLabels(DashboardWidgetRange.All));
    }

    [Fact]
    public void SampledWidgetsNeverOfferTheLongerWindowsAndInsightsWidgetsOfferWeekToAll()
    {
        Assert.Equal([DashboardWidgetRange.Hour, DashboardWidgetRange.Day, DashboardWidgetRange.Week, DashboardWidgetRange.Month], DashboardRegistry.SampledRanges.Allowed);
        foreach (var id in new[] { "playback-24h", "concurrent-streams-24h", "egress-24h", "playback-reliability", "api-clients" })
            Assert.Equal(DashboardRegistry.SampledRanges, DashboardRegistry.Get(id).Ranges);

        DashboardWidgetRange[] five = [DashboardWidgetRange.Week, DashboardWidgetRange.Month, DashboardWidgetRange.Quarter, DashboardWidgetRange.Year, DashboardWidgetRange.All];
        Assert.Equal(five, DashboardRegistry.InsightsRanges.Allowed);
        Assert.Equal(DashboardWidgetRange.Month, DashboardRegistry.InsightsRanges.Default);
        // The leaderboards offer the same five and start on the week; day and hour are gone.
        Assert.Equal(five, DashboardRegistry.LeaderboardRanges.Allowed);
        Assert.Equal(DashboardWidgetRange.Week, DashboardRegistry.LeaderboardRanges.Default);
        Assert.DoesNotContain(DashboardWidgetRange.Day, DashboardRegistry.Get("top-titles").Ranges!.Allowed);
        // A sampled widget cannot be switched to a window it does not offer, whatever asks.
        var layout = DashboardRegistry.DefaultLayout;
        Assert.Same(layout, DashboardLayoutDocument.SetRange(layout, "playback-24h", DashboardWidgetRange.Quarter));
        Assert.Same(layout, DashboardLayoutDocument.SetRange(layout, "egress-24h", DashboardWidgetRange.All));
        Assert.Equal(DashboardWidgetRange.Quarter, DashboardLayoutDocument.SetRange(layout, "top-titles", DashboardWidgetRange.Quarter).Single(e => e.Id == "top-titles").Range);
    }

    [Theory]
    [InlineData("hour", "7d")]
    [InlineData("day", "7d")]
    [InlineData("week", "7d")]
    [InlineData("month", "30d")]
    [InlineData("quarter", "90d")]
    [InlineData("year", "1y")]
    [InlineData("all", "all")]
    public void EveryWindowMapsToTheInsightsPresetItOpensAndReads(string widgetRange, string insightsRange)
    {
        var range = DashboardRanges.Parse(widgetRange)!.Value;
        Assert.Equal(insightsRange, AdminInsightsRules.WidgetRangeToInsightsRange(range).Wire());
        Assert.Equal("/admin/insights?tab=playback&range=" + insightsRange, DashboardRanges.InsightsHref(AdminInsightsTab.Playback, range));
    }

    // ── Registry ─────────────────────────────────────────────

    [Fact]
    public void TheSixInsightsWidgetsAreRegisteredAfterTopProfilesWithTheOfficialBoxesWindowsAndViews()
    {
        var ids = DashboardRegistry.Widgets.Select(widget => widget.Id).ToList();
        var first = ids.IndexOf("top-profiles") + 1;
        Assert.Equal(InsightsIds, ids.Skip(first).Take(6));
        Assert.Equal("watch-providers", ids[first + 6]);

        // id: min/max/default span, min/max/default rows (registry.tsx at c59798116).
        var expected = new Dictionary<string, int[]>
        {
            ["insights-plays"] = [6, 12, 6, 2, 5, 3],
            ["insights-viewing-hours"] = [6, 12, 6, 3, 4, 3],
            ["insights-weekday"] = [4, 8, 4, 2, 4, 2],
            ["insights-hour-of-day"] = [4, 12, 6, 2, 4, 2],
            ["insights-active-people"] = [4, 12, 6, 2, 5, 3],
            ["insights-library-coverage"] = [4, 8, 6, 2, 6, 3],
        };
        foreach (var (id, box) in expected)
        {
            var widget = DashboardRegistry.Get(id);
            Assert.Equal(box, [widget.MinSpan, widget.MaxSpan, widget.DefaultSpan, widget.MinRows, widget.MaxRows, widget.DefaultRows]);
            Assert.Equal(DashboardRegistry.InsightsRanges, widget.Ranges);
            Assert.Null(widget.CollapsedRows);
        }
        Assert.Equal(["method", "media", "watch_time"], DashboardRegistry.Get("insights-plays").Views!.Allowed);
        Assert.Equal("method", DashboardRegistry.Get("insights-plays").Views!.Default);
        foreach (var id in new[] { "insights-weekday", "insights-hour-of-day" })
        {
            Assert.Equal(["plays", "watch_time"], DashboardRegistry.Get(id).Views!.Allowed);
            Assert.Equal("plays", DashboardRegistry.Get(id).Views!.Default);
        }
        Assert.Null(DashboardRegistry.Get("insights-viewing-hours").Views);
        Assert.Null(DashboardRegistry.Get("insights-active-people").Views);
        Assert.Null(DashboardRegistry.Get("insights-library-coverage").Views);
    }

    [Fact]
    public void CatalogTitlesAndDescriptionsAreTheOfficialWords()
    {
        string[] titles = ["Playback over time", "Viewing hours", "Plays by day of week", "Plays by hour of day", "Active people", "Library coverage"];
        Assert.Equal(titles, InsightsIds.Select(id => DashboardRegistry.Get(id).Title));
        Assert.Equal("When people start watching, by day of the week and hour", DashboardRegistry.Get("insights-viewing-hours").Description);
        Assert.Equal("How much of each movie and TV library was played over the chosen window", DashboardRegistry.Get("insights-library-coverage").Description);
        Assert.All(InsightsIds, id => Assert.NotEqual(DashboardRegistry.Get(id).TitleKey, DashboardRegistry.Get(id).Title));
    }

    [Fact]
    public void AddingAnInsightsWidgetPlacesItAtItsOwnDefaultsAndStoresNoView()
    {
        var added = DashboardLayoutDocument.Add(DashboardRegistry.DefaultLayout, "insights-weekday");
        var entry = added.Single(e => e.Id == "insights-weekday");
        Assert.Equal(new DashboardLayoutEntry("insights-weekday", 4, 2, DashboardWidgetRange.Month), entry);
        Assert.Null(entry.View);
        Assert.Equal("plays", DashboardLayoutDocument.EffectiveView(entry));
        Assert.DoesNotContain(DashboardLayoutDocument.Hidden(added), widget => widget.Id == "insights-weekday");
    }

    // ── Layout round trip ────────────────────────────────────

    /// <summary>A layout as the WebUI writes it: every new widget at a non-default box, window and view, and both leaderboards on the new windows.</summary>
    private const string WebUiLayout = """
        {"version":1,"entries":[
        {"id":"insights-plays","span":12,"rows":5,"range":"all","view":"watch_time"},
        {"id":"insights-viewing-hours","span":8,"rows":4,"range":"year"},
        {"id":"insights-weekday","span":8,"rows":3,"range":"quarter","view":"watch_time"},
        {"id":"insights-hour-of-day","span":12,"rows":4,"range":"week","view":"plays"},
        {"id":"insights-active-people","span":4,"rows":2,"range":"all"},
        {"id":"insights-library-coverage","span":8,"rows":6,"range":"quarter"},
        {"id":"top-titles","span":8,"rows":5,"range":"year"},
        {"id":"top-profiles","span":4,"rows":2,"range":"all"},
        {"id":"insights-plays","span":6,"rows":3,"range":"month","view":"media"},
        {"id":"users","span":5,"rows":4}
        ]}
        """;

    private static string Compact(string json) => System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonDocument.Parse(json).RootElement);

    [Fact]
    public void AWebUiLayoutWithEveryNewWidgetViewAndWindowSurvivesADesktopReadAndSave()
    {
        // The duplicate insights-plays at the end is dropped as the WebUI drops a duplicate; everything else is kept as stored.
        var entries = DashboardLayoutDocument.Sanitize(WebUiLayout)!;
        Assert.Equal(
            ["insights-plays", "insights-viewing-hours", "insights-weekday", "insights-hour-of-day", "insights-active-people",
             "insights-library-coverage", "top-titles", "top-profiles", "users"],
            entries.Select(entry => entry.Id));
        Assert.Equal(new DashboardLayoutEntry("insights-plays", 12, 5, DashboardWidgetRange.All, "watch_time"), entries[0]);
        Assert.Equal(new DashboardLayoutEntry("insights-viewing-hours", 8, 4, DashboardWidgetRange.Year), entries[1]);
        Assert.Equal(new DashboardLayoutEntry("insights-weekday", 8, 3, DashboardWidgetRange.Quarter, "watch_time"), entries[2]);
        Assert.Equal(new DashboardLayoutEntry("insights-hour-of-day", 12, 4, DashboardWidgetRange.Week, "plays"), entries[3]);
        Assert.Equal(new DashboardLayoutEntry("insights-active-people", 4, 2, DashboardWidgetRange.All), entries[4]);
        Assert.Equal(new DashboardLayoutEntry("insights-library-coverage", 8, 6, DashboardWidgetRange.Quarter), entries[5]);
        Assert.Equal(new DashboardLayoutEntry("top-titles", 8, 5, DashboardWidgetRange.Year), entries[6]);
        Assert.Equal(new DashboardLayoutEntry("top-profiles", 4, 2, DashboardWidgetRange.All), entries[7]);

        var saved = DashboardLayoutDocument.Serialize(entries);
        var wanted = Compact(WebUiLayout);
        // What the desktop saves is the document it read, minus the duplicate it collapsed.
        Assert.Equal(wanted.Replace(""",{"id":"insights-plays","span":6,"rows":3,"range":"month","view":"media"}""", ""), saved);
        Assert.Equal<DashboardLayoutEntry>(entries, DashboardLayoutDocument.Sanitize(saved)!);
    }

    [Fact]
    public void EditingAnotherWidgetKeepsEveryInsightsEntryUnchangedThroughTheSave()
    {
        var entries = DashboardLayoutDocument.Sanitize(WebUiLayout)!;
        var edited = DashboardLayoutDocument.Resize(entries, "users", 7, 5);
        edited = DashboardLayoutDocument.Move(edited, "users", "insights-plays");
        edited = DashboardLayoutDocument.SetView(edited, "insights-weekday", "watch_time");
        var reread = DashboardLayoutDocument.Sanitize(DashboardLayoutDocument.Serialize(edited))!;
        foreach (var kept in entries.Where(entry => entry.Id != "users"))
            Assert.Contains(kept, reread);
        Assert.Equal("users", reread[0].Id);
        Assert.Equal(new DashboardLayoutEntry("users", 7, 5), reread[0]);
    }

    [Fact]
    public void ViewsAreKeptOnlyWhileTheWidgetOffersThemAndNeverSubstitutedByTheDefault()
    {
        var layout = DashboardLayoutDocument.Sanitize(
            """{"version":1,"entries":[{"id":"insights-plays","span":6,"rows":3,"range":"month","view":"total"},{"id":"insights-weekday","span":4,"rows":2,"range":"month","view":"media"},{"id":"insights-viewing-hours","span":6,"rows":3,"range":"month","view":"method"}]}""")!;
        Assert.All(layout, entry => Assert.Null(entry.View));
        Assert.Equal("method", DashboardLayoutDocument.EffectiveView(layout[0]));
        Assert.Equal("plays", DashboardLayoutDocument.EffectiveView(layout[1]));
        Assert.Null(DashboardLayoutDocument.EffectiveView(layout[2]));
    }

    [Fact]
    public void AStoredDayOrHourOnALeaderboardFallsBackToWeekAndAnInsightsWidgetFallsBackToMonth()
    {
        var layout = DashboardLayoutDocument.Sanitize(
            """{"version":1,"entries":[{"id":"top-titles","span":6,"rows":3,"range":"day"},{"id":"top-profiles","span":6,"rows":3,"range":"hour"},{"id":"insights-plays","span":6,"rows":3,"range":"day"},{"id":"insights-active-people","span":6,"rows":3,"range":"bogus"},{"id":"insights-library-coverage","span":6,"rows":3}]}""")!;
        Assert.Equal(
            [DashboardWidgetRange.Week, DashboardWidgetRange.Week, DashboardWidgetRange.Month, DashboardWidgetRange.Month, DashboardWidgetRange.Month],
            layout.Select(entry => entry.Range!.Value));
    }

    // ── Insights page link ───────────────────────────────────

    [Fact]
    public void TheInsightsLinkOpensOnlyWhenThePageIsRegistered()
    {
        var href = DashboardRanges.InsightsHref(AdminInsightsTab.Titles, DashboardWidgetRange.Quarter);
        Assert.Equal("/admin/insights?tab=titles&range=90d", href);
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRoute.Insights, resolution.Request!.Route);

        // Today no Insights page is registered, so the shell cannot open it and the cards render no link; once it is, the same href opens.
        var without = new AdminPageRegistry([new AdminPageRegistration(AdminRoute.Dashboard, typeof(object))]);
        var with = new AdminPageRegistry([new AdminPageRegistration(AdminRoute.Dashboard, typeof(object)), new AdminPageRegistration(AdminRoute.Insights, typeof(object))]);
        Assert.False(without.CanOpen(resolution.Request));
        Assert.True(with.CanOpen(resolution.Request));
    }

    // ── Chart shaping ────────────────────────────────────────

    private static AdminInsightsActivityBucket Bucket(string date, long plays, long direct = 0, long remux = 0, long transcode = 0, long movies = 0,
        long episodes = 0, long other = 0, long watch = 0, long? profiles = null, long? accounts = null)
        => new()
        {
            LabelDate = date, BucketStart = date + "T00:00:00Z", Plays = plays, Direct = direct, Remux = remux, Transcode = transcode,
            Movies = movies, Episodes = episodes, OtherMedia = other, WatchSeconds = watch, ActiveProfiles = profiles, ActiveAccounts = accounts,
        };

    private static AdminInsightsActivity Activity(string unit, string from, string to, params AdminInsightsActivityBucket[] buckets)
        => new() { BucketUnit = unit, From = from, To = to, Tz = "UTC", Buckets = [.. buckets] };

    [Fact]
    public void PlaysOverTimeFillsQuietDaysAndSplitsByMethodMediaOrWatchTime()
    {
        var data = Activity("day", "2026-11-01T00:00:00Z", "2026-11-04T00:00:00Z",
            Bucket("2026-11-01", 5, direct: 3, remux: 1, transcode: 1, movies: 2, episodes: 2, other: 1, watch: 600),
            Bucket("2026-11-03", 2, direct: 2, movies: 0, episodes: 2, watch: 90));

        var method = DashboardInsightsPresentation.PlaysColumns(data, "method")!;
        Assert.Equal(["Direct play", "Direct stream", "Transcode"], method.SeriesLabels);
        Assert.Equal(["2026-11-01", "2026-11-02", "2026-11-03"], method.Columns.Select(c => c.T.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.Equal([[3d, 1d, 1d], [0d, 0d, 0d], [2d, 0d, 0d]], method.Columns.Select(c => c.Segments.ToArray()));
        Assert.Equal([5d, 0d, 2d], method.Columns.Select(c => c.Total));

        var media = DashboardInsightsPresentation.PlaysColumns(data, "media")!;
        Assert.Equal(["Movies", "Episodes", "Other"], media.SeriesLabels);
        Assert.Equal([2d, 2d, 1d], media.Columns[0].Segments);

        var watch = DashboardInsightsPresentation.PlaysColumns(data, "watch_time")!;
        Assert.Equal(["Watch time"], watch.SeriesLabels);
        Assert.Equal([600d, 0d, 90d], watch.Columns.Select(c => c.Total));
        // An unknown view reads as the method split.
        Assert.Equal(method.SeriesLabels, DashboardInsightsPresentation.PlaysColumns(data, "sideways")!.SeriesLabels);
    }

    [Fact]
    public void PlaysOverTimeIsEmptyWhenThereAreSessionsButNoPlaysOrWatchTime()
    {
        var quiet = Activity("day", "2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z", Bucket("2026-11-01", 0, watch: 0));
        Assert.Null(DashboardInsightsPresentation.PlaysColumns(quiet, "method"));
        Assert.Null(DashboardInsightsPresentation.PlaysColumns(quiet, "watch_time"));
        Assert.Null(DashboardInsightsPresentation.PlaysColumns(Activity("day", "2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z"), "method"));
        // Plays with no measured watch time still draw as plays but not as watch time.
        var unmeasured = Activity("day", "2026-11-01T00:00:00Z", "2026-11-03T00:00:00Z", Bucket("2026-11-01", 4, direct: 4));
        Assert.NotNull(DashboardInsightsPresentation.PlaysColumns(unmeasured, "method"));
        Assert.Null(DashboardInsightsPresentation.PlaysColumns(unmeasured, "watch_time"));
    }

    [Fact]
    public void AxisLabelsFollowTheBucketUnitAndNeverShiftWithTheViewersZone()
    {
        var month = DashboardInsightsPresentation.BucketTime("2026-11-01")!.Value;
        Assert.Equal("Nov 2026", DashboardInsightsPresentation.BucketLabel(month, "month"));
        Assert.Equal("Nov 1", DashboardInsightsPresentation.BucketLabel(month, "day"));
        Assert.Equal("Nov 1", DashboardInsightsPresentation.BucketLabel(month, "week"));
        Assert.Null(DashboardInsightsPresentation.BucketTime("not a date"));
    }

    private static AdminInsightsHeatmapCell Cell(int day, int hour, long plays, long watch = 0) => new() { DayOfWeek = day, Hour = hour, Plays = plays, WatchSeconds = watch };

    [Fact]
    public void WeekdayAndHourChartsTotalTheHeatmapMondayFirstAndSkipEmptyWindows()
    {
        AdminInsightsHeatmapCell[] cells = [Cell(0, 9, 3, 100), Cell(0, 21, 2, 50), Cell(6, 9, 4, 300), Cell(9, 99, 50, 5000)];

        var weekday = DashboardInsightsPresentation.ViewingColumns(cells, DashboardInsightsPresentation.ViewingAxis.Weekday, "plays")!;
        Assert.Equal(["Plays"], weekday.SeriesLabels);
        Assert.Equal([5d, 0d, 0d, 0d, 0d, 0d, 4d], weekday.Columns.Select(c => c.Total));
        Assert.Equal(Enumerable.Range(0, 7), weekday.Columns.Select(c => DashboardInsightsPresentation.ViewingIndex(c.T)));

        var hour = DashboardInsightsPresentation.ViewingColumns(cells, DashboardInsightsPresentation.ViewingAxis.Hour, "watch_time")!;
        Assert.Equal(["Watch time"], hour.SeriesLabels);
        Assert.Equal(24, hour.Columns.Count);
        Assert.Equal(400d, hour.Columns[9].Total);
        Assert.Equal(50d, hour.Columns[21].Total);

        Assert.Null(DashboardInsightsPresentation.ViewingColumns([], DashboardInsightsPresentation.ViewingAxis.Weekday, "plays"));
        Assert.Null(DashboardInsightsPresentation.ViewingColumns([Cell(0, 0, 0, 0)], DashboardInsightsPresentation.ViewingAxis.Hour, "plays"));
        // A watch-time total of zero is empty even when there are plays.
        Assert.Null(DashboardInsightsPresentation.ViewingColumns([Cell(1, 1, 5, 0)], DashboardInsightsPresentation.ViewingAxis.Weekday, "watch_time"));
    }

    [Fact]
    public void ViewingLabelsAreShortWeekdaysMondayFirstAndHoursOnTheCulturesClock()
    {
        var us = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("Mon", DashboardInsightsPresentation.ViewingLabel(DashboardInsightsPresentation.ViewingAxis.Weekday, 0, us));
        Assert.Equal("Sun", DashboardInsightsPresentation.ViewingLabel(DashboardInsightsPresentation.ViewingAxis.Weekday, 6, us));
        Assert.Equal("12 AM", DashboardInsightsPresentation.ViewingLabel(DashboardInsightsPresentation.ViewingAxis.Hour, 0, us));
        Assert.Equal("6 PM", DashboardInsightsPresentation.ViewingLabel(DashboardInsightsPresentation.ViewingAxis.Hour, 18, us));
        Assert.Equal("", DashboardInsightsPresentation.ViewingLabel(DashboardInsightsPresentation.ViewingAxis.Hour, 24, us));
    }

    [Fact]
    public void ActivePeopleDrawsProfilesAsTheLineAndAccountsAsTheOverlayWithQuietDaysAsRealZeros()
    {
        var data = Activity("day", "2026-11-01T00:00:00Z", "2026-11-04T00:00:00Z",
            Bucket("2026-11-01", 3, profiles: 4, accounts: 2), Bucket("2026-11-03", 1, profiles: 1, accounts: null));
        var series = DashboardInsightsPresentation.ActivePeople(data)!;
        Assert.Equal([4d, 0d, 1d], series.Profiles.Select(p => p.Value!.Value));
        Assert.Equal([2d, 0d, 0d], series.Accounts.Select(p => p.Value!.Value));
        Assert.Equal(("Nov 1", "Nov 3"), (series.StartLabel, series.EndLabel));
        // No active profile anywhere is the empty state, not a flat zero line.
        Assert.Null(DashboardInsightsPresentation.ActivePeople(Activity("day", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", Bucket("2026-11-01", 3, profiles: 0))));
        Assert.Null(DashboardInsightsPresentation.ActivePeople(Activity("day", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z", Bucket("2026-11-01", 3, profiles: null))));
    }

    private static AdminInsightsTitleRow Title(string id, string title, long plays, long watch, string kind = "movie")
        => new() { ContentId = id, Title = title, Plays = plays, WatchSeconds = watch, Kind = kind };

    [Fact]
    public void TopTitlesMergesMoviesAndShowsByPlaysThenWatchTimeAndKeepsEight()
    {
        var data = new AdminInsightsTitles
        {
            Movies = [Title("m1", "Arrival", 9, 100), Title("m2", "Heat", 5, 900), Title("m3", "", 4, 10), .. Enumerable.Range(0, 6).Select(i => Title("x" + i, "Filler " + i, 1, i))],
            Shows = [Title("s1", "Severance", 9, 5000, "series"), Title("s2", "Andor", 5, 100, "series")],
        };
        var rows = DashboardInsightsPresentation.TopTitles(data);
        Assert.Equal(8, rows.Count);
        Assert.Equal(["Severance", "Arrival", "Heat", "Andor", "Removed title"], rows.Take(5).Select(r => r.Label));
        Assert.Equal([9d, 9d, 5d, 5d, 4d], rows.Take(5).Select(r => r.Value));
        Assert.Equal("1.4h", rows[0].Secondary);
        // A removed title keeps its row but has nowhere to go; the others open their catalog item.
        Assert.Null(rows[4].ItemId);
        Assert.Equal("s1", rows[0].ItemId);
        Assert.Empty(DashboardInsightsPresentation.TopTitles(null));
    }

    [Fact]
    public void TopProfilesShowProfileAndAccountAndLinkToThatProfilesHistory()
    {
        var data = new AdminInsightsPeople
        {
            TopProfiles =
            [
                new() { UserId = "7", Username = "ana", ProfileId = "p 1", ProfileName = "Kids", Plays = 12, WatchSeconds = 7200 },
                new() { UserId = "8", Username = "ben", ProfileId = "", ProfileName = "", Plays = 3, WatchSeconds = 60 },
                new() { UserId = "9", Username = "", ProfileId = "p2", ProfileName = "", Plays = 2, WatchSeconds = 30 },
                new() { UserId = "10", Username = "cy", ProfileId = "p3", ProfileName = "cy", Plays = 1, WatchSeconds = 0 },
                .. Enumerable.Range(0, 6).Select(i => new AdminInsightsTopProfile { UserId = "2" + i, Username = "u" + i, Plays = 0 }),
            ],
        };
        var rows = DashboardInsightsPresentation.TopProfiles(data);
        Assert.Equal(8, rows.Count);
        Assert.Equal(["Kids · ana", "ben", "p2 · User #9", "cy"], rows.Take(4).Select(r => r.Label));
        Assert.Equal("/admin/history?user_id=7&profile_id=p%201", rows[0].HistoryHref);
        Assert.Equal("/admin/history?user_id=8", rows[1].HistoryHref);
        Assert.Equal("7:p 1", rows[0].Id);
        Assert.Equal("2h", rows[0].Secondary);
        Assert.Empty(DashboardInsightsPresentation.TopProfiles(null));
    }

    [Fact]
    public void LibraryCoverageIsThePlayedShareAgainstAFixedHundredWithItsCountsBeside()
    {
        var data = new AdminInsightsLibrary
        {
            Libraries =
            [
                new() { LibraryId = "1", Name = "Movies", Titles = 1200, PlayedTitles = 305, NeverStarted = 895, PlayedShare = 0.2542 },
                new() { LibraryId = "2", Name = "Shows", Titles = 1, PlayedTitles = 1, NeverStarted = 0, PlayedShare = 1 },
            ],
        };
        var rows = DashboardInsightsPresentation.LibraryCoverage(data);
        Assert.Equal([25.4, 100], rows.Select(r => r.Value));
        Assert.Equal("305 of 1,200 titles · 895 never started", rows[0].Secondary);
        Assert.Equal("1 of 1 title · 0 never started", rows[1].Secondary);
        Assert.Equal("25.4% played", DashboardInsightsPresentation.CoverageValueText(rows[0].Value));
        Assert.Equal("100% played", DashboardInsightsPresentation.CoverageValueText(100));
        Assert.Equal("0% played", DashboardInsightsPresentation.CoverageValueText(0));
        Assert.Empty(DashboardInsightsPresentation.LibraryCoverage(null));
    }
}
