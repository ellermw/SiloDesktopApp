using SiloPlayer.Core.Services;
using Location = SiloPlayer.Core.Services.UiStallLog.Location;

namespace SiloPlayer.Tests;

public sealed class UiStallLogTests
{
    private sealed class Clock { public long Now = 100_000; }

    [Fact]
    public void AStallLineSeparatesTheSampledPageFromTheLongestOperationsOwner()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var library = new Location("LibraryPage");
        log.NoteNavigation("navigate", "LibraryPage");
        clock.Now += 1_500;
        log.RecordOperation("library-card-bind", 120, library);
        log.RecordOperation("library-grid-render", 812, library);
        log.RecordOperation("navigation", 300, library, new("FavoritesPage"));
        log.SetBreadcrumb("library-grid-render");
        clock.Now += 40;

        var line = log.Stall(900, 650, 64, new("FavoritesPage"), "idle");

        Assert.Equal("sample=1 | delay_ms=900 | late_ms=650 | managed_mb=64 | page=FavoritesPage | admin=- | last=navigate:LibraryPage 1540ms ago | playback=idle | " +
            "longest_op=library-grid-render 812ms | op_owner=LibraryPage | breadcrumb=library-grid-render 40ms ago", line);
        clock.Now += UiStallLog.SummaryMilliseconds;
        Assert.EndsWith("most_late_time=LibraryPage:650ms", log.TakeSummary());
    }

    [Fact]
    public void AnAdminStallNamesTheAdminPageAndAnInputAction()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.NoteInput("click", "Button");
        clock.Now += 250;

        var line = log.Stall(1_200, 950, 80, new("AdminShellPage", "ServerLog"), "playing");

        Assert.Contains("| page=AdminShellPage | admin=ServerLog | last=click:Button 250ms ago | playback=playing | longest_op=none | breadcrumb=", line);
    }

    [Fact]
    public void WheelOnlyScrollingReplacesThePriorNavigationAndKeepsTheOriginalInputAge()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var library = new Location("LibraryPage");
        log.NoteNavigation("navigate", "LibraryPage");
        clock.Now += 60_000;
        log.RecordOperation("library-grid-render", 800, library);
        clock.Now += 800; // The root observes the wheel after the child has rendered.
        log.NoteInput("wheel", "ScrollViewer", ageMs: 800);

        Assert.Contains("last=wheel:ScrollViewer 800ms ago", log.Stall(1_000, 750, 10, library, "idle"));
    }

    [Fact]
    public void APreviewKeyKeepsItsAgeThroughASlowChildHandler()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.NoteInput("key", "HeroCarousel");
        clock.Now += 800;

        Assert.Contains("last=key:HeroCarousel 800ms ago", log.Stall(1_000, 750, 10, new("HomePage"), "idle"));
    }

    [Fact]
    public void BackKeepsItsStartTimeAndRanksTheAdminTransitionSeparately()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var source = new Location("AdminShellPage", "ServerLog");
        var target = new Location("AdminShellPage", "Dashboard");
        log.NoteInput("key", "Button");
        var started = log.Now;
        clock.Now += 800;
        log.NoteNavigation("back", "Dashboard", started);
        log.NoteInput("click", "Button", ageMs: 800); // An input delivered late must not supersede a commit for it.
        log.RecordOperation("navigation-back", 800, source, target);

        var line = log.Stall(1_000, 750, 10, target, "idle");
        Assert.Contains("last=back:Dashboard 800ms ago", line);
        Assert.Contains("op_owner=AdminShellPage/ServerLog | op_target=AdminShellPage/Dashboard", line);
        clock.Now += UiStallLog.SummaryMilliseconds;
        Assert.EndsWith("most_late_time=AdminShellPage/ServerLog->AdminShellPage/Dashboard:750ms", log.TakeSummary());
    }

    [Fact]
    public void RoundedBubblingPointerTimeDoesNotReplaceTheNavigationItTriggered()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var page = new Location("HomePage");
        var start = log.Now;
        clock.Now += 950;
        log.NoteNavigation("back", page.Page, start);
        log.NoteInput("click", "Button", ageMs: 939, timestampToleranceMs: 32);
        Assert.Contains("last=back:HomePage 950ms ago", log.Stall(950, 700, 10, page, "idle"));

        log.NoteInput("click", "Button", ageMs: 0, timestampToleranceMs: 32);
        Assert.Contains("last=click:Button 0ms ago", log.Stall(950, 700, 10, page, "idle"));
    }

    [Fact]
    public void ClearingASampleDropsItsOperationsButPreservesActionAndBreadcrumb()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var home = new Location("HomePage");
        log.NoteNavigation("navigate", "HomePage");
        log.SetBreadcrumb("page-loaded", "HomePage");
        log.RecordOperation("navigation", 400, new("LibraryPage"), home);
        log.Quiet();
        log.RecordOperation("catch-up", 90, home);

        Assert.Contains("longest_op=catch-up 90ms | op_owner=HomePage", log.Stall(800, 550, 10, home, "idle"));
        var next = log.Stall(800, 550, 10, home, "idle");
        Assert.Contains("longest_op=none", next);
        Assert.Contains("last=navigate:HomePage 0ms ago", next);
        Assert.Contains("breadcrumb=page-loaded:HomePage 0ms ago", next);
    }

    [Fact]
    public void AMeasuredOperationRecordsItsDurationAndPublishesTheBreadcrumb()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var search = new Location("SearchPage");
        var name = new string("synthetic-block".ToCharArray());
        using (log.Measure(name, search)) Thread.Sleep(30);

        var line = log.Stall(800, 550, 10, search, "idle");

        Assert.Matches(@"longest_op=synthetic-block (\d{2,})ms", line);
        Assert.Same(name, log.Breadcrumb);
        log.SetBreadcrumb("page-loaded", "SearchPage");
        Assert.Equal("page-loaded:SearchPage", log.Breadcrumb);
    }

    [Fact]
    public void AQuietTickAllocatesNothing()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var library = new Location("LibraryPage");
        log.NoteNavigation("navigate", "LibraryPage");
        for (var warm = 0; warm < 10; warm++) { log.Quiet(); _ = log.TakeSummary(); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 0; tick < 10_000; tick++)
        {
            log.RecordOperation("library-grid-render", tick % 50, library);
            log.Quiet();
            _ = log.TakeSummary();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void DispatcherLatenessExcludesTheIntervalAndDiscardsSuspendGaps()
    {
        Assert.Equal(0L, UiStallLog.TickLateness(250, 250));
        Assert.Equal(499L, UiStallLog.TickLateness(749, 749));
        Assert.Equal(UiStallLog.StallMilliseconds, UiStallLog.TickLateness(750, 750));
        Assert.Equal(650L, UiStallLog.TickLateness(900, 900));
        Assert.Equal(650L, UiStallLog.TickLateness(900, 915));
        Assert.Null(UiStallLog.TickLateness(3_600_900, 900));
        Assert.Null(UiStallLog.TickLateness(250, 950));
    }

    [Fact]
    public void ClockSamplingDiscardsAPauseBetweenBootAndAwakeReads()
    {
        var boot = 1_000L;
        Assert.Equal<(long Boot, long Awake)?>((1_000L, 1_000L), UiStallLog.ReadTickClocks(() => boot, () => 1_000L));
        boot = 1_250L;
        Assert.Null(UiStallLog.ReadTickClocks(() => boot, () => boot += 700));
        boot += 250L;
        Assert.Equal<(long Boot, long Awake)?>((2_200L, 2_200L), UiStallLog.ReadTickClocks(() => boot, () => 2_200L));
    }

    [Fact]
    public void ASummaryAfterTenMinutesRanksByLatenessNotRawGap()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        void Sample(long gap, Location location) => log.Stall(gap, gap - UiStallLog.TickMilliseconds, 10, location, "idle");
        Assert.False(log.SummaryDue);
        Sample(750, new("LibraryPage"));
        clock.Now += 60_000; Sample(750, new("LibraryPage"));
        clock.Now += 60_000; Sample(1_400, new("AdminShellPage", "ServerLog"));
        clock.Now += 60_000; Sample(950, new("SettingsPage"));
        clock.Now += 60_000; Sample(850, new("HomePage"));
        Assert.False(log.SummaryDue);
        Assert.Null(log.TakeSummary());

        clock.Now = 100_000 + UiStallLog.SummaryMilliseconds;
        Assert.True(log.SummaryDue);
        Assert.Equal("summary | minutes=10 | stalls=5 | worst_late_ms=1150 | most_late_time=AdminShellPage/ServerLog:1150ms,LibraryPage:1000ms,SettingsPage:700ms",
            log.TakeSummary());

        clock.Now += 3 * UiStallLog.SummaryMilliseconds;
        Assert.False(log.SummaryDue);
        Assert.Null(log.TakeSummary());

        Sample(800, new("HomePage"));
        clock.Now += UiStallLog.SummaryMilliseconds - 1;
        Assert.False(log.SummaryDue);
        clock.Now += 1;
        Assert.Equal("summary | minutes=10 | stalls=1 | worst_late_ms=550 | most_late_time=HomePage:550ms", log.TakeSummary());
    }

    [Fact]
    public void SamplesNumberEveryStallLine()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        var home = new Location("HomePage");
        Assert.StartsWith("sample=1 ", log.Stall(800, 550, 1, home, "idle"));
        Assert.StartsWith("sample=2 ", log.Stall(800, 550, 1, home, "idle"));
    }
}
