namespace SiloPlayer.Tests;

public class AdminApiKeysCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminApiKeysPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminApiKeysPage.xaml.cs"));

    [Fact]
    public void ApiKeysUsesCurrentPageGeometryAndTableColumns()
    {
        Assert.Contains("Text=\"API Keys\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ApiKeysTableScroll\"", Markup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1080\"", Markup, StringComparison.Ordinal);
        foreach (var heading in new[] { "Label", "User", "Key", "Tier", "Created", "Last Used", "Actions" })
            Assert.Contains($"Text=\"{heading}\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeysExposeCopyTierCreateRevealAndRevokeFlows()
    {
        Assert.Contains("CopyToClipboard(capturedKey.Key)", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("UpdateTierCommand.ExecuteAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenCreateDialogAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowKeyRevealDialogAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("OpenDeleteDialogAsync", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Content = \"Previous\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Content = \"Next\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ActualWidth < 720", CodeBehind, StringComparison.Ordinal);
    }
}
