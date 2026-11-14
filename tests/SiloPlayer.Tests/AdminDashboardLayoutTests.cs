using System.Globalization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminDashboardLayoutTests
{
    private static readonly string[] AllIds =
    [
        "stat-active-streams", "stat-egress-now", "stat-transcode-share", "stat-profiles-active", "stat-movies", "stat-shows",
        "stat-advisory-coverage", "stat-users", "stat-storage", "health-strip", "server-resources", "playback-24h",
        "concurrent-streams-24h", "egress-24h", "playback-reliability", "top-titles", "top-profiles", "watch-providers",
        "now-playing", "transcode-nodes", "scanner", "libraries", "users", "downloads", "scan-activity", "recent-errors",
        "recent-activity", "api-clients",
    ];

    [Fact]
    public void RegistryHasExactlyTheTwentyEightWebUiWidgetsInOrder()
    {
        Assert.Equal(AllIds, DashboardRegistry.Widgets.Select(widget => widget.Id));
        foreach (var widget in DashboardRegistry.Widgets)
        {
            Assert.True(DashboardText.Has(widget.TitleKey), widget.TitleKey);
            Assert.True(DashboardText.Has(widget.DescriptionKey), widget.DescriptionKey);
            Assert.InRange(widget.DefaultSpan, widget.MinSpan, widget.MaxSpan);
            Assert.InRange(widget.DefaultRows, widget.MinRows, widget.MaxRows);
        }
    }

    [Fact]
    public void DefaultLayoutIsTheFifteenWidgetsAtTheirDefaults()
    {
        Assert.Equal(
            ["stat-active-streams", "stat-egress-now", "stat-profiles-active", "stat-movies", "stat-shows", "stat-storage", "health-strip",
             "now-playing", "playback-24h", "egress-24h", "top-titles", "top-profiles", "libraries", "users", "recent-activity"],
            DashboardRegistry.DefaultLayout.Select(entry => entry.Id));
        Assert.Equal(new DashboardLayoutEntry("playback-24h", 6, 3, DashboardWidgetRange.Day), DashboardRegistry.DefaultLayout.Single(e => e.Id == "playback-24h"));
        Assert.Equal(new DashboardLayoutEntry("top-titles", 6, 3, DashboardWidgetRange.Week), DashboardRegistry.DefaultLayout.Single(e => e.Id == "top-titles"));
        Assert.Equal(new DashboardLayoutEntry("libraries", 7, 4), DashboardRegistry.DefaultLayout.Single(e => e.Id == "libraries"));
        Assert.Equal(13, DashboardLayoutDocument.Hidden(DashboardRegistry.DefaultLayout).Count);
        Assert.Equal(1, DashboardRegistry.Get("now-playing").CollapsedRows);
    }

    [Fact]
    public void RangesMapToWebUiWindowsAndMonthIsSevenHundredTwentyHours()
    {
        Assert.Equal([1, 24, 168, 720], DashboardRanges.Order.Select(DashboardRanges.Hours));
        Assert.Equal([1, 1, 7, 30], DashboardRanges.Order.Select(DashboardRanges.Days));
        Assert.Equal(["1h", "24h", "7d", "30d"], DashboardRanges.Order.Select(DashboardRanges.Label));
        Assert.Equal("Egress · last 30 d", DashboardRanges.Title("Egress", DashboardWidgetRange.Month));
        Assert.Equal(("7d ago", "now"), DashboardRanges.EdgeLabels(DashboardWidgetRange.Week));
        Assert.Equal(DashboardRegistry.LeaderboardRanges, DashboardRegistry.Get("top-profiles").Ranges);
        Assert.Equal(DashboardRegistry.SampledRanges, DashboardRegistry.Get("api-clients").Ranges);
        Assert.Null(DashboardRegistry.Get("users").Ranges);
    }

    public static TheoryData<string, string, string> SanitizeCases => new()
    {
        { "drops unknown ids", """[{"id":"libraries","span":7},{"id":"not-a-widget","span":6},{"id":"users","span":5}]""", "libraries:7x4;users:5x4" },
        { "clamps spans", """[{"id":"stat-movies","span":1,"rows":1},{"id":"now-playing","span":40,"rows":4},{"id":"users","span":"wide","rows":4}]""", "stat-movies:2x1;now-playing:12x4;users:5x4" },
        { "fills missing rows", """[{"id":"libraries","span":7},{"id":"watch-providers","span":9},{"id":"stat-movies","span":3}]""", "libraries:7x4;watch-providers:9x1;stat-movies:3x1" },
        { "resolves renamed id", """[{"id":"libraries","span":7,"rows":4},{"id":"trakt-sync","span":9,"rows":1}]""", "libraries:7x4;watch-providers:9x1" },
        { "collapses renamed duplicate", """[{"id":"trakt-sync","span":9,"rows":1},{"id":"watch-providers","span":12,"rows":2}]""", "watch-providers:9x1" },
        { "fills default windows", """[{"id":"egress-24h","span":6,"rows":3},{"id":"top-titles","span":6,"rows":3},{"id":"users","span":5,"rows":4}]""", "egress-24h:6x3@Day;top-titles:6x3@Week;users:5x4" },
        { "keeps allowed windows", """[{"id":"egress-24h","span":6,"rows":3,"range":"month"},{"id":"top-titles","span":6,"rows":3,"range":"day"}]""", "egress-24h:6x3@Month;top-titles:6x3@Day" },
        { "replaces disallowed window and drops unranged", """[{"id":"top-titles","span":6,"rows":3,"range":"hour"},{"id":"users","span":5,"rows":4,"range":"week"}]""", "top-titles:6x3@Week;users:5x4" },
    };

    [Theory]
    [MemberData(nameof(SanitizeCases))]
    public void SanitizesDocumentsLikeTheWebUi(string name, string entries, string expected)
    {
        var result = DashboardLayoutDocument.Sanitize("{\"version\":1,\"entries\":" + entries + "}");
        Assert.NotNull(result);
        Assert.True(expected == Describe(result!), name + ": " + Describe(result!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{not json")]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("[]")]
    public void NonDocumentsAreNoLayoutRatherThanEmpty(string? json)
        => Assert.Null(DashboardLayoutDocument.Sanitize(json));

    [Fact]
    public void SerializeRoundTripsAndWritesRangeOnlyForRangedWidgets()
    {
        var json = DashboardLayoutDocument.Serialize(DashboardRegistry.DefaultLayout);
        Assert.StartsWith("{\"version\":1,\"entries\":[{\"id\":\"stat-active-streams\",\"span\":2,\"rows\":1}", json);
        Assert.Contains("{\"id\":\"playback-24h\",\"span\":6,\"rows\":3,\"range\":\"day\"}", json);
        Assert.True(DashboardLayoutDocument.SameArrangement(DashboardRegistry.DefaultLayout, DashboardLayoutDocument.Sanitize(json)!));
        Assert.True(json.Length < 16 * 1024);
        var all = DashboardRegistry.Widgets.Select(DashboardRegistry.DefaultEntry).ToArray();
        Assert.True(DashboardLayoutDocument.Serialize(all).Length < 16 * 1024);
    }

    [Fact]
    public void EditsMatchTheWebUiLayoutHook()
    {
        var layout = DashboardRegistry.DefaultLayout;
        var moved = DashboardLayoutDocument.Move(layout, "users", "stat-active-streams");
        Assert.Equal("users", moved[0].Id);
        Assert.Equal("users", DashboardLayoutDocument.Move(layout, "users", null)[^1].Id);
        Assert.Same(layout, DashboardLayoutDocument.Move(layout, "users", "users"));
        Assert.Same(layout, DashboardLayoutDocument.Move(layout, "api-clients", null));

        var resized = DashboardLayoutDocument.Resize(layout, "libraries", 40, null);
        Assert.Equal((12, 4), (resized.Single(e => e.Id == "libraries").Span, resized.Single(e => e.Id == "libraries").Rows));
        var rowsOnly = DashboardLayoutDocument.Resize(layout, "libraries", null, 1);
        Assert.Equal((7, 2), (rowsOnly.Single(e => e.Id == "libraries").Span, rowsOnly.Single(e => e.Id == "libraries").Rows));
        Assert.Same(layout, DashboardLayoutDocument.Resize(layout, "libraries", 7, 4));

        Assert.Equal(DashboardWidgetRange.Month, DashboardLayoutDocument.SetRange(layout, "egress-24h", DashboardWidgetRange.Month).Single(e => e.Id == "egress-24h").Range);
        Assert.Same(layout, DashboardLayoutDocument.SetRange(layout, "top-titles", DashboardWidgetRange.Hour));
        Assert.Same(layout, DashboardLayoutDocument.SetRange(layout, "users", DashboardWidgetRange.Week));

        var removed = DashboardLayoutDocument.Remove(layout, "users");
        Assert.DoesNotContain(removed, entry => entry.Id == "users");
        Assert.Equal("users", DashboardLayoutDocument.Hidden(removed).First(w => w.Id == "users").Id);

        var added = DashboardLayoutDocument.Add(layout, "api-clients", "now-playing");
        Assert.Equal(new DashboardLayoutEntry("api-clients", 6, 3, DashboardWidgetRange.Day), added[added.ToList().FindIndex(e => e.Id == "now-playing") - 1]);
        Assert.Equal("scanner", DashboardLayoutDocument.Add(layout, "scanner", "not-placed")[^1].Id);
        Assert.Same(layout, DashboardLayoutDocument.Add(layout, "users"));
    }

    [Theory]
    [InlineData(639, DashboardGridMode.Single)]
    [InlineData(640, DashboardGridMode.Two)]
    [InlineData(800, DashboardGridMode.Two)]
    [InlineData(1023, DashboardGridMode.Two)]
    [InlineData(1024, DashboardGridMode.Twelve)]
    [InlineData(1440, DashboardGridMode.Twelve)]
    public void BreakpointsFollowTheWebUiMediaQueries(double width, DashboardGridMode mode)
        => Assert.Equal(mode, DashboardGridPlacement.ModeFor(width));

    [Fact]
    public void DefaultLayoutPlacesLikeCssSparseAutoPlacementOnTwelveColumns()
    {
        var cells = DashboardGridPlacement.Place(DashboardGridMode.Twelve,
            DashboardRegistry.DefaultLayout.Select(e => new DashboardGridRequest(e.Id, e.Span, e.Rows)).ToList());
        string At(string id) { var c = cells.Single(x => x.Id == id); return $"{c.Row},{c.Column},{c.ColumnSpan}x{c.RowSpan}"; }
        Assert.Equal("0,0,2x1", At("stat-active-streams"));
        Assert.Equal("0,10,2x1", At("stat-storage"));
        Assert.Equal("1,0,12x1", At("health-strip"));
        Assert.Equal("2,0,12x3", At("now-playing"));
        Assert.Equal("5,0,6x3", At("playback-24h"));
        Assert.Equal("5,6,6x3", At("egress-24h"));
        Assert.Equal("8,0,6x3", At("top-titles"));
        Assert.Equal("11,0,7x4", At("libraries"));
        Assert.Equal("11,7,5x4", At("users"));
        Assert.Equal("15,0,12x4", At("recent-activity"));
    }

    [Fact]
    public void SparsePlacementNeverBackfillsAHole()
    {
        var cells = DashboardGridPlacement.Place(DashboardGridMode.Twelve,
            [new("a", 8, 1), new("b", 6, 1), new("c", 4, 1)]);
        Assert.Equal((0, 0), (cells[0].Row, cells[0].Column));
        Assert.Equal((1, 0), (cells[1].Row, cells[1].Column));
        Assert.Equal((1, 6), (cells[2].Row, cells[2].Column));
        var tall = DashboardGridPlacement.Place(DashboardGridMode.Twelve, [new("t", 6, 3), new("u", 6, 1), new("v", 6, 1), new("w", 12, 1)]);
        Assert.Equal((1, 6), (tall[2].Row, tall[2].Column));
        Assert.Equal(3, tall[3].Row);
    }

    [Fact]
    public void TabletUsesTwoColumnsWithWideWidgetsOnTheirOwnRowAndPhoneOneColumn()
    {
        var two = DashboardGridPlacement.Place(DashboardGridMode.Two, [new("s1", 2, 1), new("s2", 2, 1), new("wide", 6, 3), new("s3", 4, 1)]);
        Assert.Equal([(0, 0, 1), (0, 1, 1), (1, 0, 2), (2, 0, 1)], two.Select(c => (c.Row, c.Column, c.ColumnSpan)));
        Assert.All(two, c => Assert.Equal(1, c.RowSpan));
        var one = DashboardGridPlacement.Place(DashboardGridMode.Single, [new("a", 12, 4), new("b", 2, 1)]);
        Assert.Equal([(0, 0, 1), (1, 0, 1)], one.Select(c => (c.Row, c.Column, c.ColumnSpan)));
    }

    [Fact]
    public void ResizeMathClampsEachAxisAndFollowsColumnPlusGapUnits()
    {
        var libraries = DashboardRegistry.Get("libraries");
        Assert.Equal(214 + 0, DashboardGridPlacement.FixedHeight(2));
        // 1214px grid: one column plus gap is (1214 + 14) / 12 = 102.33px.
        Assert.Equal((8, 5), DashboardGridPlacement.ResizeFromDrag(libraries, 7, 4, 103, 114, 1214));
        Assert.Equal((6, 2), DashboardGridPlacement.ResizeFromDrag(libraries, 7, 4, -5000, -5000, 1214));
        Assert.Equal((8, 4), DashboardGridPlacement.ResizeFromDrag(libraries, 7, 4, -103, 0, 1214, rightToLeft: true));
        Assert.Equal((12, 8), DashboardGridPlacement.ResizeFromKey(libraries, 12, 8, 1, 1));
        Assert.Equal((6, 4), DashboardGridPlacement.ResizeFromKey(libraries, 7, 4, -1, 0));
        Assert.False(DashboardRegistry.Widgets.Any(widget => !widget.CanResize));
    }

    [Fact]
    public void DragAutoScrollRampsInsideEdgeZonesOnly()
    {
        Assert.Equal(0, DashboardDragAutoScroll.EdgeScrollSpeed(300, 0, 600));
        Assert.Equal(0, DashboardDragAutoScroll.EdgeScrollSpeed(80, 0, 600));
        Assert.Equal(-24, DashboardDragAutoScroll.EdgeScrollSpeed(0, 0, 600));
        Assert.Equal(24, DashboardDragAutoScroll.EdgeScrollSpeed(650, 0, 600));
        Assert.True(Math.Abs(DashboardDragAutoScroll.EdgeScrollSpeed(10, 0, 600)) > Math.Abs(DashboardDragAutoScroll.EdgeScrollSpeed(70, 0, 600)));
        Assert.Equal(0, DashboardDragAutoScroll.EdgeScrollSpeed(60, 0, 120));
        Assert.Equal(0, DashboardDragAutoScroll.EdgeScrollSpeed(0, 100, 100));
    }

    private static string Describe(IReadOnlyList<DashboardLayoutEntry> entries)
        => string.Join(";", entries.Select(e => $"{e.Id}:{e.Span}x{e.Rows}" + (e.Range is { } r ? "@" + r : "")));
}

public sealed class AdminDashboardFormattingTests
{
    public AdminDashboardFormattingTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    [Fact]
    public void DataTotalMatchesTheWebUiFormatTests()
    {
        Assert.Equal("—", DashboardFormat.DataTotal(null));
        Assert.Equal("—", DashboardFormat.DataTotal(-1));
        Assert.Equal("0 B", DashboardFormat.DataTotal(0));
        Assert.Equal("999 B", DashboardFormat.DataTotal(999));
        Assert.Equal("1.5 KB", DashboardFormat.DataTotal(1_500));
        Assert.Equal("123 MB", DashboardFormat.DataTotal(123_456_789));
        Assert.Equal("1.0 MB", DashboardFormat.DataTotal(999_999));
        Assert.Equal("100 GB", DashboardFormat.DataTotal(99_960_000_000));
        Assert.Equal("1,500 PB", DashboardFormat.DataTotal(1.5e18));
    }

    [Fact]
    public void RatesTimesAndCountsUseWebUiRules()
    {
        Assert.Equal("0m", DashboardFormat.WatchTime(0));
        Assert.Equal("1m", DashboardFormat.WatchTime(20));
        Assert.Equal("59m", DashboardFormat.WatchTime(3_540));
        Assert.Equal("1.5h", DashboardFormat.WatchTime(5_400));
        Assert.Equal("2.5 Mbps", DashboardFormat.Mbps(2.5));
        Assert.Equal("0 Mbps", DashboardFormat.Mbps(0));
        Assert.Equal("12 Mbps", DashboardFormat.Mbps(12.4));
        Assert.Equal("—", DashboardFormat.Mbps(double.NaN));
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("3d 4h", DashboardFormat.Uptime(now.AddHours(-76), now));
        Assert.Equal("5m 3s", DashboardFormat.Uptime(now.AddSeconds(-303), now));
        Assert.Equal("1h 2m", DashboardFormat.ScanDuration(now.AddMinutes(-62), now, now.AddHours(1)));
        Assert.Equal("—", DashboardFormat.ScanDuration(now, now.AddMinutes(-1), now));
        Assert.Equal("0.42 ms", DashboardFormat.Latency(0.4249));
        Assert.Equal("12 ms", DashboardFormat.Latency(12.4));
        Assert.Equal("1.0 TB", DashboardFormat.Storage(1024L * 1024 * 1024 * 1024));
        Assert.Equal("512 GB", DashboardFormat.Storage(512L * 1024 * 1024 * 1024));
        Assert.Equal("1,234 files", DashboardFormat.FileCount(1234));
        Assert.Equal("1 file", DashboardFormat.FileCount(1));
        Assert.Equal("—", DashboardFormat.FileCount(null));
        Assert.Equal("Updated less than 1 minute ago", DashboardFormat.UpdatedLabel(now, now.AddSeconds(-59)));
        Assert.Equal("Updated 2 minutes ago", DashboardFormat.UpdatedLabel(now, now.AddSeconds(-150)));
        Assert.Equal("Just now", DashboardFormat.RelativeTime(now.AddSeconds(-30), now, floor: true, "Just now"));
        Assert.Equal("5m ago", DashboardFormat.RelativeTime(now.AddMinutes(-5.9), now, floor: true));
    }

    [Fact]
    public void MessagesFormatPluralsSelectsAndRichTags()
    {
        Assert.Equal("1 session", DashboardText.T("web.admin.dashboard.widgets.activeStreams.sessions", DashboardText.Count(1)));
        Assert.Equal("1,200 sessions", DashboardText.T("web.admin.dashboard.widgets.activeStreams.sessions", DashboardText.Count(1200)));
        Assert.Equal("Libraries resized to 8 of 12 columns × 2 rows", DashboardText.T("web.admin.dashboard.grid.resized",
            ("title", "Libraries"), ("span", 8L), ("columns", 12L), ("rows", 2L)));
        Assert.Equal("owner", DashboardText.T("web.admin.common.accounts.role", ("role", "owner")));
        var runs = DashboardText.Rich("web.admin.dashboard.widgets.recentActivity.startedWatchingWithProfile", ("username", "ana"), ("profile", "Kids"), ("title", "Movie"));
        Assert.Equal([("ana", "user"), (" ", null), ("Kids", "pill"), (" started watching ", null), ("Movie", "link")],
            runs.Select(run => (run.Text, run.Tag)));
        Assert.Equal("scratch — 91% full, carried over from an earlier pass", DashboardText.T("web.admin.nodes.disk.line", ("path", "scratch"), ("percent", "91"), ("stale", "yes")));
        Assert.Equal("Drag a widget to move it · drag its corner to resize · × removes it", DashboardText.T("web.admin.dashboard.customize.hint"));
    }
}

public sealed class AdminDashboardChartTests
{
    [Fact]
    public void NiceTicksCoverTheRangeWithHumanSteps()
    {
        Assert.Equal([0, 0.5, 1], DashboardChartMath.NiceTicks(0, 0, 3));
        Assert.Equal([0, 10, 20], DashboardChartMath.NiceTicks(0, 13, 3));
        Assert.Equal([0, 5, 10, 15], DashboardChartMath.NiceTicks(0, 13, 4));
        Assert.Equal([0, 1, 2], DashboardChartMath.NiceTicks(0, 1.2, 3, minStep: 1));
        Assert.Equal([0, 1, 2], DashboardChartMath.NiceTicks(0, 1.2, 3));
        Assert.Equal([0, 0.5, 1, 1.5], DashboardChartMath.NiceTicks(0, 1.2, 4));
    }

    [Fact]
    public void TimeseriesZeroFillsMissingMinutesAsGapsNotZeros()
    {
        var from = new DateTimeOffset(2026, 11, 14, 12, 0, 30, TimeSpan.Zero);
        var series = new DashboardTimeseries
        {
            ResolutionSeconds = 60, From = from, To = from.AddMinutes(4),
            Points = [new() { T = from.AddSeconds(-30), Streams = 2 }, new() { T = from.AddMinutes(2).AddSeconds(-30), Streams = 0 }],
        };
        var points = DashboardChartMath.TimeseriesPoints(series, point => point.Streams);
        Assert.Equal([2d, null, 0d, null, null], points.Select(p => p.Value));
        Assert.Null(DashboardChartMath.LatestFreshPoint(series, TimeSpan.FromMinutes(2), from.AddMinutes(5)));
        Assert.NotNull(DashboardChartMath.LatestFreshPoint(series, TimeSpan.FromMinutes(2), from.AddMinutes(2)));
    }

    [Fact]
    public void PlaybackColumnsSnapToTheServerBucketGrid()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 30, 0, TimeSpan.Zero);
        var columns = DashboardChartMath.PlaybackColumns([new() { Hour = now.AddMinutes(-50), Direct = 1, Transcode = 2 }], 1, 3600, now);
        Assert.Equal(2, columns.Count);
        Assert.Equal(now.AddMinutes(-90), columns[0].T);
        Assert.Equal(3, columns[0].Total);
        var daily = DashboardChartMath.PlaybackColumns([], 720, 86_400, now);
        Assert.Equal(31, daily.Count);
        Assert.Equal([1d, 0d, 2d], columns[0].Segments);
    }

    [Fact]
    public void EgressBandsNeedANodeAndCarryOtherAsTheRemainder()
    {
        var t = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var apiOnly = new DashboardTimeseries { From = t, To = t, ResolutionSeconds = 60, EgressSources = [new() { Key = "api", Kind = "api", Name = "API" }], Points = [] };
        Assert.Null(DashboardChartMath.EgressBands(apiOnly));
        var series = new DashboardTimeseries
        {
            From = t, To = t, ResolutionSeconds = 60,
            EgressSources = [new() { Key = "api", Kind = "api", Name = "API", EgressBytes = 5 }, new() { Key = "node:7", Kind = "node", Name = "edge", NodeId = "7", EgressBytes = 9 }],
            Points = [new() { T = t, EgressKbps = 10_000, EgressBySource = new() { ["api"] = 2_000, ["node:7"] = 6_000 } }],
        };
        var bands = DashboardChartMath.EgressBands(series)!;
        Assert.Equal(["other", "api", "node:7"], bands.Select(b => b.Key));
        Assert.Equal(2d, bands[0].Points[0].Value);
        Assert.Equal(0, bands[1].SeriesSlot);
        Assert.Equal(4, bands[2].SeriesSlot);
    }

    [Fact]
    public void StackSegmentsTreatNegativeAndMissingAsZero()
    {
        var segments = DashboardChartMath.StackSegments([2, -1, double.NaN, 3]);
        Assert.Equal([(0d, 2d), (2d, 2d), (2d, 2d), (2d, 5d)], segments.Select(s => (s.Start, s.End)));
    }
}
