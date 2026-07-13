namespace SiloPlayer.Tests;

public class AdminRecommendationsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRecommendationsPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminRecommendationsPage.xaml.cs"));

    [Fact]
    public void PageUsesCurrentWebUiGeometryAndStatusCards()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Job status\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"Co-Watch Matrix\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddingConfigurationIncludesCurrentPresetsAndConnectionCheck()
    {
        Assert.Contains("BuildProviderPresets", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"Gemini\", \"Recommended\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"Ollama\", \"Local\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("\"OpenAI\", \"\"", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("CheckSettingsConnectionAsync(\"recommendations_embedding\"", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendationSectionsRetainCurrentLockScheduleAndAdvancedControls()
    {
        foreach (var text in new[] { "Embedding Lock", "General", "Embedding Configuration", "Schedule", "Advanced", "Diversity Lambda" })
            Assert.Contains(text, CodeBehind, StringComparison.Ordinal);
    }
}
