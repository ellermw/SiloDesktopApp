namespace SiloPlayer.Tests;

public sealed class GlobalSearchInteractionParitySourceTests
{
    [Fact]
    public void PreviewAndRequestResultsUseFocusableActions()
    {
        var source = Read("src", "SiloPlayer", "Controls", "GlobalSearchDialog.xaml.cs");

        Assert.Contains("private Button BuildResultRow", source);
        Assert.Contains("private Button BuildRequestRow", source);
        Assert.Contains("AutomationProperties.SetName(row,", source);
        Assert.Contains("row.Click += (_, _) => PickResult(index)", source);
        Assert.DoesNotContain("row.Tapped += (_, _) => PickResult(index)", source);
    }

    [Fact]
    public void KeyboardSelectionTracksOnlyCatalogResultButtons()
    {
        var source = Read("src", "SiloPlayer", "Controls", "GlobalSearchDialog.xaml.cs");

        Assert.Contains("Button { Tag: int resultIndex, Content: Border surface }", source);
        Assert.Contains("resultIndex == _selectedIndex", source);
        Assert.DoesNotContain("ResultsPanel.Children[_selectedIndex]", source);
    }

    private static string Read(params string[] parts)
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
