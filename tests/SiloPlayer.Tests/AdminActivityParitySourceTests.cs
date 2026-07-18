namespace SiloPlayer.Tests;

public class AdminActivityParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml.cs"));

    private static string PageMarkup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminActivityPage.xaml"));

    private static string ViewModelSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminActivityViewModel.cs"));

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

    [Fact]
    public void ActivityUsesCurrentAdminBreakpointsAndProtectsItsWideTable()
    {
        Assert.Contains("x:Name=\"PageHeaderActions\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollMode=\"Auto\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"1100\"", PageMarkup, StringComparison.Ordinal);
        Assert.Contains("width >= 1280 ? 40", PageSource, StringComparison.Ordinal);
        Assert.Contains("contentWidth < 760", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EpisodeIdentityAndExpansionMatchCurrentWebUiBehavior()
    {
        Assert.Contains("? session.EpisodeName", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("return $\"{ep} \\u00b7 {session.SeriesName}\";", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("bool detailsOpen = false;", PageSource, StringComparison.Ordinal);
        Assert.Contains("bool ffmpegOpen = false;", PageSource, StringComparison.Ordinal);
        Assert.Contains("if (ffmpegOpen) detailsOpen = true;", PageSource, StringComparison.Ordinal);
        Assert.Contains("ffmpegOpen = false;", PageSource, StringComparison.Ordinal);
        Assert.Contains("Attr(entry, \"ffmpeg_line\")", PageSource, StringComparison.Ordinal);
        Assert.Contains("Attr(entry, \"ffmpeg_event\")", PageSource, StringComparison.Ordinal);
        Assert.Contains("NormalizeContainerDecision(session.PlayMethod)", PageSource, StringComparison.Ordinal);
        Assert.Contains("NormalizeStreamDecision(session.VideoDecision ?? session.PlayMethod)", PageSource, StringComparison.Ordinal);
        Assert.Contains("\"copy\" or \"remux\" => \"copy\"", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (decision == \"copy\") return \"Video stream copied\";", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("if (decision == \"copy\") return \"Audio stream copied\";", ViewModelSource, StringComparison.Ordinal);
        Assert.Contains("label = \"Copy\";", PageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionActionsRefreshWithoutFlashingTheWholePage()
    {
        var start = PageSource.IndexOf("private FrameworkElement BuildStreamRow", StringComparison.Ordinal);
        var end = PageSource.IndexOf("private static string GetSessionCommandToast", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var row = PageSource[start..end];

        Assert.Contains("GetSessionCommandToast(\"Pause\", response)", row, StringComparison.Ordinal);
        Assert.Contains("GetSessionCommandToast(\"Resume\", response)", row, StringComparison.Ordinal);
        Assert.Contains("GetSessionCommandToast(\"Stop\", response)", row, StringComparison.Ordinal);
        Assert.Contains("await ViewModel.RefreshSilentAsync();", row, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.LoadCommand.ExecuteAsync", row, StringComparison.Ordinal);
    }
}
