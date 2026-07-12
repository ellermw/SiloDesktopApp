namespace SiloPlayer.Tests;

public class AdminDashboardRegressionSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string DashboardSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml.cs"));

    [Fact]
    public void DashboardUsesEpisodeNameAndSeriesSubtitleLikeWebUi()
    {
        Assert.Contains("session.EpisodeName", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("subtitleText += $\" \\u2014 {session.SeriesName}\"", DashboardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardShowsClientLabelAndScanPhaseMessage()
    {
        Assert.Contains("AdminActivityViewModel.GetSessionClientLabel(session)", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("result.Message", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("FormatDashboardLibraryScanProgress", DashboardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardPaintsLoadingStateBeforeNetworkRequests()
    {
        var loaded = DashboardSource[DashboardSource.IndexOf("private async void Page_Loaded", StringComparison.Ordinal)..];
        Assert.True(
            loaded.IndexOf("BuildLoadingState();", StringComparison.Ordinal)
            < loaded.IndexOf("await LoadDashboardProgressivelyAsync", StringComparison.Ordinal));
    }
}
