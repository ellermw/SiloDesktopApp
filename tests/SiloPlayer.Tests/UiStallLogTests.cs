using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// ui_lag.txt lines name where each interface stall happened (page, Admin page, last navigation or
/// input and its age, playback, the longest measured operation, the breadcrumb), and a summary line
/// comes at most once per ten minutes with stalls. A quiet tick must allocate nothing.
/// </summary>
public sealed class UiStallLogTests
{
    private sealed class Clock { public long Now = 100_000; }

    [Fact]
    public void AStallLineNamesThePageTheLastNavigationPlaybackAndTheLongestOperation()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.NoteAction("navigate", "LibraryPage");
        clock.Now += 1_500;
        log.RecordOperation("library-card-bind", 120);
        log.RecordOperation("library-grid-render", 812);
        log.RecordOperation("navigation", 300);
        log.SetBreadcrumb("library-grid-render");
        clock.Now += 40;

        var line = log.Stall(900, 64, "LibraryPage", null, "idle");

        Assert.Equal("sample=1 | delay_ms=900 | managed_mb=64 | page=LibraryPage | admin=- | last=navigate:LibraryPage 1540ms ago | playback=idle | " +
            "longest_op=library-grid-render 812ms | breadcrumb=library-grid-render 40ms ago", line);
    }

    [Fact]
    public void AnAdminStallNamesTheAdminPageAndAnInputAction()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.NoteAction("click", "Button");
        clock.Now += 250;

        var line = log.Stall(1_200, 80, "AdminShellPage", "Logs", "playing");

        Assert.Contains("| page=AdminShellPage | admin=Logs | last=click:Button 250ms ago | playback=playing | longest_op=none | breadcrumb=", line);
    }

    [Fact]
    public void OperationsFromAQuietWindowAreNotReportedWithALaterStall()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.RecordOperation("navigation", 400);
        log.Quiet();
        log.RecordOperation("catch-up", 90);

        Assert.Contains("longest_op=catch-up 90ms", log.Stall(600, 10, "HomePage", null, "idle"));
        Assert.Contains("longest_op=none", log.Stall(600, 10, "HomePage", null, "idle"));
    }

    [Fact]
    public void AMeasuredOperationRecordsItsDurationAndSetsTheBreadcrumb()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        using (log.Measure("synthetic-block")) Thread.Sleep(30);

        var line = log.Stall(700, 10, "SearchPage", null, "idle");

        Assert.Matches(@"longest_op=synthetic-block (\d{2,})ms", line);
        Assert.Equal("synthetic-block", log.Breadcrumb);
    }

    [Fact]
    public void AQuietTickAllocatesNothing()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        log.NoteAction("navigate", "LibraryPage");
        for (var warm = 0; warm < 10; warm++) { log.Quiet(); _ = log.SummaryDue; }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 0; tick < 10_000; tick++)
        {
            log.RecordOperation("library-grid-render", tick % 50);
            log.Quiet();
            _ = log.SummaryDue;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ASummaryComesTenMinutesAfterTheFirstStallWithTheThreePagesWithTheMostStallTime()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        Assert.False(log.SummaryDue);
        log.Stall(1_000, 10, "LibraryPage", null, "idle");
        clock.Now += 60_000; log.Stall(1_000, 10, "LibraryPage", null, "idle");
        clock.Now += 60_000; log.Stall(700, 10, "SettingsPage", null, "idle");
        clock.Now += 60_000; log.Stall(600, 10, "HomePage", null, "idle");
        clock.Now += 60_000; log.Stall(2_400, 10, "AdminShellPage", "Logs", "idle");
        Assert.False(log.SummaryDue);
        Assert.Null(log.TakeSummary());

        clock.Now = 100_000 + UiStallLog.SummaryMilliseconds;
        Assert.True(log.SummaryDue);
        Assert.Equal("summary | minutes=10 | stalls=5 | worst_ms=2400 | most_stall_time=AdminShellPage:2400ms,LibraryPage:2000ms,SettingsPage:700ms",
            log.TakeSummary());

        // No stalls since: no summary, however long it is quiet.
        clock.Now += 3 * UiStallLog.SummaryMilliseconds;
        Assert.False(log.SummaryDue);
        Assert.Null(log.TakeSummary());

        // The next window starts at its own first stall.
        log.Stall(800, 10, "HomePage", null, "playing");
        clock.Now += UiStallLog.SummaryMilliseconds - 1;
        Assert.False(log.SummaryDue);
        clock.Now += 1;
        Assert.Equal("summary | minutes=10 | stalls=1 | worst_ms=800 | most_stall_time=HomePage:800ms", log.TakeSummary());
    }

    [Fact]
    public void SamplesNumberEveryStallLine()
    {
        var clock = new Clock(); var log = new UiStallLog(() => clock.Now);
        Assert.StartsWith("sample=1 ", log.Stall(600, 1, "HomePage", null, "idle"));
        Assert.StartsWith("sample=2 ", log.Stall(600, 1, "HomePage", null, "idle"));
    }
}
