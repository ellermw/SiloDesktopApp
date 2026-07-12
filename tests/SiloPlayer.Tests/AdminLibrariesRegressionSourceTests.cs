namespace SiloPlayer.Tests;

public class AdminLibrariesRegressionSourceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    [Fact]
    public void StaleIdRoutesMatchCurrentLibrariesContract()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer.Core", "Api", "AdminApi.cs"));

        Assert.Contains("/api/v1/libraries/stale-ids", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/v1/admin/libraries/stale-ids", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnteringAdminHidesGlobalActivityControl()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var adminClick = source[source.IndexOf("private void Admin_Click", StringComparison.Ordinal)..];

        Assert.Contains("MainServerActivityButton.Visibility = Visibility.Collapsed", adminClick, StringComparison.Ordinal);
    }

    [Fact]
    public void LibrariesShowsSkeletonBeforePrimaryRequestCompletes()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var loaded = source[source.IndexOf("private async void Page_Loaded", StringComparison.Ordinal)..];

        Assert.True(
            loaded.IndexOf("BuildLibraryLoadingRows();", StringComparison.Ordinal)
            < loaded.IndexOf("await ViewModel.LoadLibrariesAsync();", StringComparison.Ordinal));
    }

    [Fact]
    public void LibraryEditorPlacesContentInSecondGridColumn()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));

        Assert.Contains("Grid.SetColumn(sectionBorder, 1);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryEditorUsesExistingAccentButtonStyle()
    {
        var pageSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Views", "Admin", "AdminLibrariesPage.xaml.cs"));
        var themeSource = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiloPlayer", "Themes", "DarkTheme.xaml"));

        Assert.Contains("Resources[\"AccentButtonStyle\"]", pageSource, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"AccentButtonStyle\"", themeSource, StringComparison.Ordinal);
    }
}
