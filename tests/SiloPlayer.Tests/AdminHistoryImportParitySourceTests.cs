namespace SiloPlayer.Tests;

public class AdminHistoryImportParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminHistoryImportPage.xaml.cs"));

    private static string Model => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer.Core", "Models", "HistoryImport", "HistoryImportSource.cs"));

    [Fact]
    public void HistoryImportUsesCurrentHeadingAndSourceBar()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("external servers into Silo user profiles", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SourceUrlText\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Set API key\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Edit server\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoryImportUsesRealApiKeyStatusAndCurrentMappingEmptyState()
    {
        Assert.Contains("public bool HasAdminToken", Model, StringComparison.Ordinal);
        Assert.Contains("_selectedSource.HasAdminToken", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("API key configured", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("No user mappings yet. Discover users on the server to create mappings.", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Discover users\"", Markup, StringComparison.Ordinal);
    }
}
