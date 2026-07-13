namespace SiloPlayer.Tests;

public class AdminTasksParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminTasksPage.xaml.cs"));

    [Fact]
    public void ScheduledTasksUsesCurrentWebUiHeadingAndContentWidth()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Scheduled Tasks\"", Markup, StringComparison.Ordinal);
        Assert.Contains("including whether a task runs on server startup", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskRowsExposeCurrentStatusMetadataAndActions()
    {
        Assert.Contains("FormatDuration", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatTaskResultSummary", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"Stopping...\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("isRunning ? \"Stop\" : \"Run Now\"", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MetricsAreAppliedAfterTheParallelLoadCompletes()
    {
        var metricsAssignment = CodeBehind.IndexOf("_refreshMetrics = await adminApi.GetTaskMetricsAsync", StringComparison.Ordinal);
        var rebuild = CodeBehind.IndexOf("RebuildTaskGroups();", metricsAssignment, StringComparison.Ordinal);

        Assert.True(metricsAssignment >= 0, "Expected refresh metrics to be captured.");
        Assert.True(rebuild > metricsAssignment, "Expected task groups to rebuild after metrics are available.");
    }
}
