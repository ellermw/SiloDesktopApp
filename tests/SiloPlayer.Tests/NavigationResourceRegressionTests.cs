namespace SiloPlayer.Tests;

public sealed class NavigationResourceRegressionTests
{
    [Fact]
    public void TopLevelBrowsePagesDoNotReferenceRemovedPrimaryButtonStyle()
    {
        var root = FindRepositoryRoot();
        var views = Path.Combine(root, "src", "SiloPlayer", "Views");
        foreach (var page in new[]
                 {
                     "SearchPage.xaml",
                     "CatalogPage.xaml",
                     "CollectionBrowsePage.xaml",
                     "LibraryPage.xaml",
                 })
        {
            var markup = File.ReadAllText(Path.Combine(views, page));
            Assert.DoesNotContain("{StaticResource PrimaryButtonStyle}", markup, StringComparison.Ordinal);
        }

        var libraryCode = File.ReadAllText(Path.Combine(views, "LibraryPage.xaml.cs"));
        Assert.DoesNotContain("Resources[\"PrimaryButtonStyle\"]", libraryCode, StringComparison.Ordinal);

        var theme = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Themes", "DarkTheme.xaml"));
        Assert.Contains("x:Key=\"AccentButtonStyle\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarNavigationFailureIsContainedInsteadOfPoisoningTheUiThread()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void NavView_ItemInvoked", StringComparison.Ordinal);
        Assert.True(start >= 0, "NavView_ItemInvoked was not found in MainWindow.xaml.cs.");
        var end = source.IndexOf("private void SwitchProfile_Click", start, StringComparison.Ordinal);
        Assert.True(end > start, "SwitchProfile_Click was not found after NavView_ItemInvoked.");

        var method = source[start..end];
        Assert.Contains("catch (Exception ex)", method, StringComparison.Ordinal);
        Assert.Contains("LogNavigationFailure(\"sidebar_navigation\", ex)", method, StringComparison.Ordinal);
        Assert.Contains("Page failed to open", method, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the SiloPlayer repository root.");
    }
}
