using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Display rules copied from official taskTrigger.ts, taskText.ts, taskProgress.ts, AdminTasks.tsx and TaskStatusBadge.tsx.</summary>
public sealed class AdminTasksTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static AdminTaskInfo Task(string key, string name = "Server name", string description = "Server description") =>
        new(key, name, description, "system", AdminTaskStates.Idle, 0, false, null, [], null, null);

    private static AdminTaskExecution Run(string status, string key = "fixture", AdminTaskMarkerResult? result = null, params AdminTaskStep[] steps) =>
        new("1", key, Now, Now, status, 0, null, steps, result);

    [Theory]
    [InlineData("interval", 10_800_000L, false, "Every 3 hour(s)")]
    [InlineData("interval", 10_800_000L, true, "Every 3h")]
    [InlineData("interval", 3_600_000L, false, "Every 1 hour")]
    [InlineData("interval", 172_800_000L, false, "Every 48 hour(s)")]
    [InlineData("interval", 172_800_000L, true, "Every 2d")]
    [InlineData("interval", 90_000L, true, "Every 90s")]
    [InlineData("interval", 999L, false, "Every 999 millisecond(s)")]
    [InlineData("interval", 0L, true, "Every 0s")]
    [InlineData("startup", null, false, "On server startup")]
    [InlineData("startup", null, true, "On startup")]
    public void TriggerTextMatchesTheOfficialForms(string type, long? interval, bool shortStyle, string expected) =>
        Assert.Equal(expected, AdminTasksText.DescribeTrigger(new(type, interval, null, null, null), shortStyle));

    [Fact]
    public void DailyAndWeeklyTriggersNameTheirTimeAndDay()
    {
        Assert.Equal("Daily at 05:00", AdminTasksText.DescribeTrigger(new("daily", null, "05:00", null, null)));
        Assert.Equal("Sunday at 00:00", AdminTasksText.DescribeTrigger(new("weekly", null, null, null, null)));
        Assert.Equal("Sat at 23:30", AdminTasksText.DescribeTrigger(new("weekly", null, "23:30", 6, null), shortStyle: true));
        Assert.Equal("custom", AdminTasksText.DescribeTrigger(new("custom", null, null, null, null)));
    }

    [Fact]
    public void ScheduleSummaryJoinsShortTriggers()
    {
        Assert.Null(AdminTasksText.DescribeSchedule([]));
        Assert.Equal("Every 3h and On startup", AdminTasksText.DescribeSchedule([new("interval", 10_800_000, null, null, null), new("startup", null, null, null, null)]));
        Assert.Equal("a, b, and c", AdminTasksText.JoinList(["a", "b", "c"]));
    }

    [Theory]
    [InlineData(0L, "<1ms")]
    [InlineData(250L, "250ms")]
    [InlineData(59_999L, "59s")]
    [InlineData(61_000L, "1m 1s")]
    [InlineData(3_660_000L, "1h 1m")]
    public void ListDurationsUseTheListTiers(long milliseconds, string expected) => Assert.Equal(expected, AdminTasksText.ListDuration(milliseconds));

    [Theory]
    [InlineData(0L, "0ms")]
    [InlineData(3_660_000L, "61m 0s")]
    public void DetailDurationsHaveNoHourTier(long milliseconds, string expected) => Assert.Equal(expected, AdminTasksText.DetailDuration(milliseconds));

    [Fact]
    public void NextRunIsFlooredAndOverdueWhenPast()
    {
        Assert.Equal("in 5m", AdminTasksText.NextRun(Now.AddMinutes(5.9), Now));
        Assert.Equal("in 2h", AdminTasksText.NextRun(Now.AddMinutes(150), Now));
        Assert.Equal("in 3d", AdminTasksText.NextRun(Now.AddDays(3.5), Now));
        Assert.Equal("overdue", AdminTasksText.NextRun(Now.AddSeconds(-1), Now));
        Assert.True(AdminTasksText.IsOverdue(Now.AddSeconds(-1), Now));
        Assert.False(AdminTasksText.IsOverdue(Now, Now));
    }

    [Theory]
    [InlineData(42, "42%")]
    [InlineData(42.46, "42.5%")]
    [InlineData(99.97, "99.9%")]
    [InlineData(150, "100%")]
    [InlineData(-5, "0%")]
    [InlineData(double.NaN, "0%")]
    public void ProgressIsClampedAndNeverLooksFinishedEarly(double progress, string expected) => Assert.Equal(expected, AdminTasksText.Progress(progress));

    [Fact]
    public void ProgressBarStartsVisible() => Assert.Equal(2, AdminTasksText.ProgressBarPercent(0));

    [Fact]
    public void TaskNamesUseTheCatalogOnlyForSilosOwnKeys()
    {
        Assert.Equal("Queue Media Library Scans", AdminTasksText.TaskName(Task("scan_libraries")));
        Assert.Equal("Rebuild Catalog Search Index", AdminTasksText.TaskName(Task("rebuild_catalog_search_index")));
        Assert.Equal("Server name", AdminTasksText.TaskName(Task("plugin:7:feed")));
        Assert.Equal("Server name", AdminTasksText.TaskName(Task("not_in_catalog")));
        Assert.Equal("Server description", AdminTasksText.TaskDescription(Task("plugin:7:feed")));
    }

    [Fact]
    public void StatusBadgeFlagsMarkerContributionFailures()
    {
        Assert.Equal("completed", AdminTasksText.StatusLabel(Run("completed")));
        Assert.Equal("Completed with errors", AdminTasksText.StatusLabel(Run("completed", "contribute_markers", new(1, 0, 0, 2, 0))));
        Assert.Equal("completed", AdminTasksText.StatusLabel(Run("completed", "contribute_markers", new(1, 0, 0, 0, 0))));
        Assert.Equal("paused", AdminTasksText.StatusLabel(Run("paused")));
    }

    [Fact]
    public void HistoryErrorPrefersFailedSteps()
    {
        Assert.Equal("Failed steps: Activity log and Notifications", AdminTasksText.HistoryError(Run("failed", "database_maintenance", null,
            new("activity", "Activity log", "failed"), new("tasks", "Task history", "completed"), new("notify", "Notifications", "failed"))));
        Assert.Equal(AdminTasksText.EmDash, AdminTasksText.HistoryError(Run("completed")));
        Assert.Null(AdminTasksText.ResultText(Run("completed")));
        Assert.Contains("\"failed\": 2", AdminTasksText.ResultText(Run("completed", "contribute_markers", new(1, 0, 0, 2, 30))));
    }

    [Fact]
    public void RichMessagesSplitTheirSingleTag()
    {
        var runs = AdminTasksText.Rich("web.admin.tasks.detail.history.loadError");
        Assert.Equal([new AdminTextRun("", "History could not load. "), new AdminTextRun("restart", "Restart history")], runs);
        var note = AdminTasksText.Rich("web.admin.tasks.detail.scanLibrariesNote");
        Assert.Equal("link", note[1].Tag);
        Assert.Equal("Admin Libraries", note[1].Text);
    }

    [Fact]
    public void ReasonsAndCategoriesFallBackToTheServerValue()
    {
        Assert.Equal("Refresh failure", AdminTasksText.Reason("refresh_failure"));
        Assert.Equal("new_reason", AdminTasksText.Reason("new_reason"));
        Assert.Equal("Metadata", AdminTasksText.Category("metadata"));
    }
}
