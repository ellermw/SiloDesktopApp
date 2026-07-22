namespace SiloPlayer.Tests;

public class AdminPlaybackHistoryParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPlaybackHistoryPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminPlaybackHistoryPage.xaml.cs"));

    [Fact]
    public void PlaybackHistoryMatchesCurrentHeaderFiltersAndStats()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        foreach (var text in new[] { "All users", "All profiles", "All attempts", "Reset", "Visible Rows", "Completed", "Partial" })
            Assert.Contains(text, Markup, StringComparison.Ordinal);
        Assert.Contains("RefreshButton_Click", Markup, StringComparison.Ordinal);
        Assert.Contains("Symbol=\"Refresh\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Radius2XL", Markup, StringComparison.Ordinal);
        Assert.Contains("Item filter active", Markup, StringComparison.Ordinal);
        Assert.Contains("No playback history matches the current filters", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackHistoryKeepsCurrentTableAndLogNavigation()
    {
        foreach (var heading in new[] { "Media", "User", "Profile", "Method", "Watch Time", "Status", "Ended", "Logs" })
            Assert.Contains($"Text=\"{heading}\"", Markup, StringComparison.Ordinal);
        Assert.Contains("NavigateToLogs", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("RebuildAll();", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(30)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("DetachPageHandlers", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(1)", CodeBehind, StringComparison.Ordinal);
    }
}
