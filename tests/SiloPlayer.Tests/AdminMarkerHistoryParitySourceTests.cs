namespace SiloPlayer.Tests;

public sealed class AdminMarkerHistoryParitySourceTests
{
    [Fact]
    public void MarkerHistoryUsesCurrentServerContract()
    {
        var api = ReadRepoFile("src", "SiloPlayer.Core", "Api", "AdminApi.cs");
        var model = ReadRepoFile("src", "SiloPlayer.Core", "Models", "Admin", "MarkerHistory.cs");

        Assert.Contains("/api/v1/admin/markers/history?limit=", api);
        Assert.Contains("Math.Clamp(limit, 1, 100)", api);
        Assert.Contains("public List<MarkerHistoryEntry> History", model);
        Assert.Contains("public MarkerHistorySegment? Before", model);
        Assert.Contains("public MarkerHistorySegment? After", model);
        Assert.Contains("public string? ImpersonatorUsername", model);
    }

    [Fact]
    public void MarkerHistoryScreenIsRegisteredAndNavigable()
    {
        var app = ReadRepoFile("src", "SiloPlayer", "App.xaml.cs");
        var shell = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml");
        var shellCode = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminShellPage.xaml.cs");
        var titles = ReadRepoFile("src", "SiloPlayer", "Helpers", "DocumentTitle.cs");

        Assert.Contains("AdminMarkerHistoryViewModel", app);
        Assert.Contains("x:Name=\"NavMarkerHistory\"", shell);
        Assert.Contains("typeof(AdminMarkerHistoryPage)", shellCode);
        Assert.Contains("NavMarkerHistory_Click", shellCode);
        Assert.Contains("Admin · Marker History", titles);
    }

    [Fact]
    public void MarkerHistoryScreenKeepsLoadingErrorEmptyAndTableStatesExclusive()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "Admin", "AdminMarkerHistoryViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminMarkerHistoryPage.xaml");

        Assert.Contains("ShowEmptyState = false", viewModel);
        Assert.Contains("ShowEmptyState = Rows.Count == 0", viewModel);
        Assert.Contains("ViewModel.ShowEmptyState", page);
        Assert.Contains("ViewModel.HasRows", page);
        Assert.DoesNotContain("<Grid Padding=\"16,12\" ColumnSpacing=\"16\" BorderBrush=", page);
    }

    [Fact]
    public void MarkerHistoryRowsMatchCurrentWebUiPresentation()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "Admin", "AdminMarkerHistoryViewModel.cs");

        Assert.Contains("Unknown user", viewModel);
        Assert.Contains("No request id", viewModel);
        Assert.Contains("Credits / Outro", viewModel);
        Assert.Contains("FormatRange(entry.Before)", viewModel);
        Assert.Contains("FormatRange(entry.After)", viewModel);
        Assert.Contains("Math.Floor(Math.Max(0, seconds) + 0.5)", viewModel);
    }

    [Fact]
    public void MarkerHistoryUsesCurrentWebUiPageGeometryAndControls()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminMarkerHistoryPage.xaml");

        Assert.Contains("MaxWidth=\"1400\"", page);
        Assert.Contains("HorizontalScrollMode=\"Enabled\"", page);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", page);
        Assert.Contains("Text=\"Marker History\"", page);
        Assert.Contains("FontSize=\"48\"", page);
        Assert.Contains("<SymbolIcon Symbol=\"Refresh\"", page);
        Assert.Contains("ViewModel.IsLoading", page);
        Assert.Contains("Height=\"40\" Background=\"{StaticResource SurfaceHoverBrush}\"", page);
        Assert.Contains("Text=\"When\"", page);
        Assert.DoesNotContain("Text=\"WHEN\"", page);
    }

    [Fact]
    public void MarkerHistoryRefreshAndHeaderRemainStableAndResponsive()
    {
        var viewModel = ReadRepoFile("src", "SiloPlayer", "ViewModels", "Admin", "AdminMarkerHistoryViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminMarkerHistoryPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "Admin", "AdminMarkerHistoryPage.xaml.cs");

        Assert.Contains("x:Name=\"MarkerHistoryPageShell\"", page);
        Assert.Contains("ViewModel.CanRefresh", page);
        Assert.Contains("var hasCachedRows = HasRows && Rows.Count > 0", viewModel);
        Assert.Contains("CanRefresh = false", viewModel);
        Assert.Contains("ApplyResponsiveLayout(ActualWidth)", code);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var pathParts = new string[parts.Length + 1];
        pathParts[0] = FindRepositoryRoot();
        Array.Copy(parts, 0, pathParts, 1, parts.Length);
        return File.ReadAllText(Path.Combine(pathParts));
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
