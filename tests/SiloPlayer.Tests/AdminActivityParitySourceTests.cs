namespace SiloPlayer.Tests;

public class AdminActivityParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml.cs"));

    private static string PageMarkup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml"));

    [Fact]
    public void ActivityTableMatchesCurrentWebUiColumnStructure()
    {
        Assert.Contains("Text=\"PLAYBACK\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("Text=\"ACTIONS\"", PageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"VIDEO\"", PageMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"AUDIO\"", PageMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackCellContainsContainerVideoAndAudioSummaryLines()
    {
        Assert.Contains("BuildPlaybackSummaryLine(\"Container\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildPlaybackSummaryLine(\"Video\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildPlaybackSummaryLine(\"Audio\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("BuildTranscodeModeBadge", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineActionsMatchCurrentWebUiHierarchy()
    {
        var inlineActions = PageSource[PageSource.IndexOf("var inlineTerminate", StringComparison.Ordinal)..];
        Assert.Contains("controlPanel.Children.Add(ffmpegToggle)", inlineActions, StringComparison.Ordinal);
        Assert.Contains("controlPanel.Children.Add(inlineTerminate)", inlineActions, StringComparison.Ordinal);
        Assert.Contains("controlPanel.Children.Add(actionBtn)", inlineActions, StringComparison.Ordinal);
        Assert.Contains("Text = \"View Logs\"", PageSource, StringComparison.Ordinal);
        Assert.Contains("Text = \"FFmpeg Logs\"", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyActivityDoesNotRenderAnEmptyTableShell()
    {
        Assert.Contains("x:Name=\"StreamsTable\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("StreamsTable.Visibility = Visibility.Collapsed;", PageSource, StringComparison.Ordinal);
        Assert.Contains("StreamsTable.Visibility = Visibility.Visible;", PageSource, StringComparison.Ordinal);

        var tableEnd = PageMarkup.IndexOf("WebUI renders the empty state", StringComparison.Ordinal);
        var emptyState = PageMarkup.IndexOf("x:Name=\"EmptyState\"", StringComparison.Ordinal);
        Assert.True(tableEnd >= 0 && emptyState > tableEnd);
    }
}
