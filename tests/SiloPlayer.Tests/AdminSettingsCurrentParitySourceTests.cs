namespace SiloPlayer.Tests;

public class AdminSettingsCurrentParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static string Markup => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml"));
    private static string CodeBehind => File.ReadAllText(Path.Combine(RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSettingsDetailPage.xaml.cs"));

    [Fact]
    public void SettingsUsesCurrentTwentySectionGroupedRail()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        foreach (var group in new[] { "Server", "Media", "Connections", "Data" })
            Assert.Contains($"(\"{group}\",", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Branding\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("(\"Search\"", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void BrandingAndSearchAreIndependentCurrentSettingsSurfaces()
    {
        Assert.Contains("BuildBrandingTab", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("branding.accent_color", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("branding.default_theme", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("BuildSearchTab", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("catalog.search.meilisearch.semantic_enabled", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("catalog.search.meilisearch.binary_quantized", CodeBehind, StringComparison.Ordinal);
    }
}
