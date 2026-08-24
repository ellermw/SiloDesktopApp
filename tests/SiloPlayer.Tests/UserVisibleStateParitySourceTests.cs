namespace SiloPlayer.Tests;

public sealed class UserVisibleStateParitySourceTests
{
    [Fact]
    public void LibraryUnavailableStateMatchesCurrentWebUiAndLinksToVisibilitySettings()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("This library is hidden or unavailable for your account.", xaml);
        Assert.Contains("Manage library visibility in Settings", xaml);
        Assert.Contains("LibraryUnavailableState.Visibility = library == null", code);
        Assert.Contains("Navigate<SettingsPage>(\"Libraries\")", code);
    }

    [Fact]
    public void CollectionEditorSeparatesLoadingAndMissingCollectionStates()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionEditorPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "CollectionEditorPage.xaml.cs");

        Assert.Contains("Loading collection editor...", xaml);
        Assert.Contains("Collection not found", xaml);
        Assert.Contains("The selected collection could not be loaded.", xaml);
        Assert.Contains("CollectionEditorScroll.Visibility = Visibility.Collapsed", code);
    }

    [Fact]
    public void SmallerUserStatesUseCurrentWebUiWordingAndHierarchy()
    {
        var requests = ReadRepoFile("src", "SiloPlayer", "Views", "RequestBrowsePage.xaml");
        var activate = ReadRepoFile("src", "SiloPlayer", "Views", "ActivateDevicePage.xaml");
        var roomMarkup = ReadRepoFile("src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml");
        var roomCode = ReadRepoFile("src", "SiloPlayer", "Views", "WatchTogetherRoomPage.xaml.cs");
        var person = ReadRepoFile("src", "SiloPlayer", "ViewModels", "PersonDetailViewModel.cs");

        Assert.Contains("Nothing matched. Try a different sort.", requests);
        Assert.Contains("Content=\"Sign in to approve\"", activate);
        Assert.Contains("x:Name=\"DrillDownSubtitle\"", roomMarkup);
        Assert.Contains("DrillDownSubtitle.Text = \"Pick a season\"", roomCode);
        Assert.Contains("ErrorMessage = \"Person not found.\"", person);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
