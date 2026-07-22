namespace SiloPlayer.Tests;

public class AdminTasksParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml.cs"));

    private static string DetailMarkup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTaskDetailPage.xaml"));

    private static string DetailCodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTaskDetailPage.xaml.cs"));

    [Fact]
    public void ScheduledTasksUsesCurrentWebUiHeadingAndContentWidth()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Scheduled Tasks\"", Markup, StringComparison.Ordinal);
        Assert.Contains("including whether a task runs on server startup", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TasksPageShell\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskRowsExposeCurrentStatusMetadataAndActions()
    {
        Assert.Contains("FormatDuration", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatTaskResultSummary", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"Stopping...\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("isRunning ? \"Stop\" : \"Run Now\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActualWidth < 720", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsAreAppliedAfterTheParallelLoadCompletes()
    {
        var metricsAssignment = CodeBehind.IndexOf("_refreshMetrics = await adminApi.GetTaskMetricsAsync", StringComparison.Ordinal);
        var rebuild = CodeBehind.IndexOf("RebuildTaskGroups();", metricsAssignment, StringComparison.Ordinal);

        Assert.True(metricsAssignment >= 0, "Expected refresh metrics to be captured.");
        Assert.True(rebuild > metricsAssignment, "Expected task groups to rebuild after metrics are available.");
    }

    [Fact]
    public void TaskDetailUsesServerPercentageScaleAndResponsiveHistoryTable()
    {
        Assert.Contains("x:Name=\"TaskHistoryTableScroll\"", DetailMarkup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"720\"", DetailMarkup, StringComparison.Ordinal);
        Assert.Contains("Math.Max(task.Progress, 2)", DetailCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("task.Progress * 100", DetailCodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", DetailCodeBehind, StringComparison.Ordinal);
    }
}
