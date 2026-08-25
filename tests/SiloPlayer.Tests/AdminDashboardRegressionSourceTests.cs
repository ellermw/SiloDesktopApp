namespace SiloPlayer.Tests;

public class AdminDashboardRegressionSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string DashboardSource => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml.cs"));

    private static string DashboardMarkup => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminDashboardPage.xaml"));

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
        Assert.Contains("Full ingest scan started for all libraries", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("Scan cancellation requested", DashboardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardPaintsLoadingStateBeforeNetworkRequests()
    {
        var marker = DashboardSource.IndexOf("private async void Page_Loaded", StringComparison.Ordinal);
        Assert.True(marker >= 0, "Page_Loaded marker was not found.");
        var loaded = DashboardSource[marker..];
        var loadingState = loaded.IndexOf("BuildLoadingState();", StringComparison.Ordinal);
        var networkLoad = loaded.IndexOf("await LoadDashboardProgressivelyAsync", StringComparison.Ordinal);
        Assert.True(
            loadingState >= 0 && networkLoad > loadingState,
            "The loading state must be rendered before dashboard network loading starts.");
    }

    [Fact]
    public void DashboardConstrainsItsAdminShellToTheViewport()
    {
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment=\"Center\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1640\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdminPageContent\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"ContentScrollViewer_SizeChanged\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("AdminPageContent.Width = Math.Min(1640", DashboardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardMatchesTheWebUiResponsiveGridBreakpoints()
    {
        Assert.Contains("ApplyResponsiveLayout(width)", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("contentWidth >= 1024 ? 5 : contentWidth >= 640 ? 3 : 2", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("contentWidth >= 1024 ? 2 : 1", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("contentWidth >= 1280", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LibraryUsersGrid\"", DashboardMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PageHeaderActions\"", DashboardMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryContractCarriesServerSortOrder()
    {
        var model = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Models", "Catalog", "Library.cs"));
        Assert.Contains("[JsonPropertyName(\"sort_order\")]", model, StringComparison.Ordinal);
        Assert.Contains("public int SortOrder", model, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardReusesItsLastSnapshotWhileRefreshing()
    {
        var appSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "App.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "ViewModels", "Admin", "AdminDashboardViewModel.cs"));

        Assert.Contains("AddSingleton<SiloPlayer.ViewModels.Admin.AdminDashboardViewModel>()", appSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AddTransient<SiloPlayer.ViewModels.Admin.AdminDashboardViewModel>()", appSource, StringComparison.Ordinal);
        Assert.Contains("public bool HasCachedData", viewModel, StringComparison.Ordinal);
        Assert.Contains("if (ViewModel.HasCachedData)", DashboardSource, StringComparison.Ordinal);
        Assert.Contains("BuildContent();", DashboardSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminShellConstrainsEveryRouteToTheWebUiCanvas()
    {
        var shellMarkup = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml"));
        var shellSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs"));

        Assert.Contains("x:Name=\"AdminContentHost\"", shellMarkup, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"AdminContentHost_SizeChanged\"", shellMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AdminContentFrame\"", shellMarkup, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"1640\"", shellMarkup, StringComparison.Ordinal);
        Assert.Contains("AdminContentFrame.Width = Math.Min(1640", shellSource, StringComparison.Ordinal);
    }
}
