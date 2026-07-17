namespace SiloPlayer.Tests;

public class AdminSubtitlesParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string Markup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml"));

    private static string CodeBehind => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminSubtitlesPage.xaml.cs"));

    [Fact]
    public void PageMatchesCurrentHeadingStatsAndFilterHierarchy()
    {
        Assert.Contains("MaxWidth=\"1400\"", Markup, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"48\"", Markup, StringComparison.Ordinal);
        Assert.Contains("Search release name…", Markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Reset filters\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SubtitleStatsGrid\"", Markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SubtitlesTableScroll\"", Markup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1320\"", Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Refresh\"", Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TableExposesAllCurrentWebUiColumns()
    {
        foreach (var heading in new[] { "Media", "File", "Language", "Provider", "Release", "Format", "HI", "Uploader", "Added", "Actions" })
            Assert.Contains($"Text=\"{heading}\"", Markup, StringComparison.Ordinal);

        Assert.Contains("MakeIconButton(Symbol.Edit", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("MakeIconButton(Symbol.Download", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("MakeIconButton(Symbol.Delete", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(actions, 9)", CodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily = fontFamily == null ? null", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("root.PointerEntered", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TableUsesCurrentRelativeDatesAndItemNavigation()
    {
        Assert.Contains("FormatRelativeDate", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Navigate<ItemDetailPage>", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ProviderFilter_Click", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveLayout", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("width >= 1280", CodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyStateMutationsAndLatestFilterRequestMatchCurrentWebUiBehavior()
    {
        Assert.Contains("x:Name=\"SubtitlesEmptyState\"", Markup, StringComparison.Ordinal);
        Assert.Contains("No stored subtitles yet", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Try widening the provider, language, or uploader filters", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_loadCts?.Cancel()", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(LoadUsersAsync(), LoadSubtitlesAsync())", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonStyle = (Style)Application.Current.Resources[\"DestructiveButtonStyle\"]", CodeBehind, StringComparison.Ordinal);
        Assert.Contains("_toastService.Success(\"Subtitle downloaded\")", CodeBehind, StringComparison.Ordinal);
    }
}
