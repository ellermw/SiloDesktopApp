namespace SiloPlayer.Tests;

public class AdminAutoscanParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml"));

    private static string ViewModel => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminAutoscanViewModel.cs"));

    [Fact]
    public void AutoscanUsesCurrentLineTabsAndIconControls()
    {
        Assert.Contains("MaxWidth=\"1640\"", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.HasFeedback", Markup, StringComparison.Ordinal);
        Assert.Contains("Loading sources", Markup, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"3\"><Button x:Name=\"SettingsTab\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RunNowText\" Text=\"Run now\"", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"▶", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"＋", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SourcesExposeCurrentConnectionIntervalAndNestedSettingsHierarchy()
    {
        Assert.Contains("SourceConnectionOptions", Markup, StringComparison.Ordinal);
        Assert.Contains("Not needed — Sonarr/Radarr deliver directly", Markup, StringComparison.Ordinal);
        Assert.Contains("PollIntervalText", Markup, StringComparison.Ordinal);
        Assert.Contains("Custom label (optional)", Markup, StringComparison.Ordinal);
        Assert.Contains("PathRewriteDisplay", Markup, StringComparison.Ordinal);
        Assert.Contains("CephFS paths &amp; ignores", Markup, StringComparison.Ordinal);
        Assert.Contains("Use configured libraries", Markup, StringComparison.Ordinal);
        Assert.Contains("SourceEnabled_Toggled", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityIncludesQueueRunningPollsAndBothHistoryViews()
    {
        Assert.Contains("Autoscan queue", Markup, StringComparison.Ordinal);
        Assert.Contains("Polling now", Markup, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Events", Markup, StringComparison.Ordinal);
        Assert.Contains("ActiveScans", ViewModel, StringComparison.Ordinal);
        Assert.Contains("HasRunningPolls", ViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoscanUsesTheSharedResponsiveAdminCanvas()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminAutoscanPage.xaml.cs"));
        Assert.Contains("x:Name=\"AdminPageContent\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageHeaderActions\"", Markup, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\"", Markup, StringComparison.Ordinal);
        Assert.Contains("width >= 1280 ? 40", source, StringComparison.Ordinal);
        Assert.Contains("contentWidth < 820", source, StringComparison.Ordinal);
    }
}
