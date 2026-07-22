namespace SiloPlayer.Tests;

public class AdminLogsParitySourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string PageSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLogsPage.xaml.cs"));

    [Fact]
    public void PlaybackSessionFilterRestartsLiveStreamAsTextChanges()
    {
        Assert.Contains("PlaybackSessionBox.TextChanged += PlaybackSessionBox_TextChanged", PageSource, StringComparison.Ordinal);
        var handler = PageSource[PageSource.IndexOf("private void PlaybackSessionBox_TextChanged", StringComparison.Ordinal)..];
        Assert.Contains("UpdatePlaybackSessionTag();", handler, StringComparison.Ordinal);
        Assert.Contains("RestartFilterDebounce();", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void FfmpegToggleDoesNotMixRestReloadWithLiveStream()
    {
        var start = PageSource.IndexOf("private async void BtnFilterFfmpeg_Click", StringComparison.Ordinal);
        var end = PageSource.IndexOf("private void PlaybackSessionBox_TextChanged", start, StringComparison.Ordinal);
        var handler = PageSource[start..end];
        Assert.Contains("await RestartStreamAsync();", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadAppLogsCommand", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamRestartsAreSerializedAndCannotReopenAfterNavigation()
    {
        Assert.Contains("SemaphoreSlim _streamRestartGate", PageSource, StringComparison.Ordinal);
        Assert.Contains("if (_isNavigatedAway) return;", PageSource, StringComparison.Ordinal);
        Assert.Contains("_isNavigatedAway = true;", PageSource, StringComparison.Ordinal);
    }
}
