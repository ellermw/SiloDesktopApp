namespace SiloPlayer.Tests;

public class AdminMaintenanceCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminMaintenancePage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminMaintenancePage.xaml.cs"));
    private static string ImportMarkup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Controls", "CatalogImportDialog.xaml"));

    [Fact]
    public void MaintenanceUsesCurrentPageGeometrySectionsAndCounts()
    {
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        foreach (var section in new[] { "Catalog Import &amp; Export", "Recent Catalog Imports", "Recent Catalog Exports", "Job History" })
            Assert.Contains(section, Markup, StringComparison.Ordinal);
        foreach (var count in new[] { "ImportJobsCountText", "ExportJobsCountText", "AllJobsCountText" })
            Assert.Contains(count, Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MaintenanceSupportsCurrentImportSourcesAndJobTypes()
    {
        foreach (var source in new[] { "Local File", "Local Export Job", "Bucket Artifact", "Remote URL" })
            Assert.Contains(source, ImportMarkup, StringComparison.Ordinal);
        Assert.Contains("image_cache_cleanup", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("pipeline_failed", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("image_cleanup_queued", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Refresh job history", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CatalogMaintenanceActions\"", Markup, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", CodeBehind, StringComparison.Ordinal);
    }
}
