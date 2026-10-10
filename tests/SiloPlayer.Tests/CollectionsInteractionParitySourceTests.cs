namespace SiloPlayer.Tests;

public sealed class CollectionsInteractionParitySourceTests
{
    [Fact]
    public void CollectionAndTemplateCardsUseFocusableOpenActions()
    {
        var personal = Read("src", "SiloPlayer", "Views", "CollectionsPage.Posters.cs");
        var server = Read("src", "SiloPlayer", "Views", "CollectionsPage.Server.cs");
        var chooser = Read("src", "SiloPlayer", "Controls", "NewCollectionDialog.cs");
        var pick = Read("src", "SiloPlayer", "Controls", "CollectionPickRow.cs");
        Assert.Contains("AutomationProperties.SetName(open, \"Open \" + collection.Name)", personal);
        Assert.Contains("AutomationProperties.SetName(open, \"Open \" + collection.Title)", server);
        Assert.Contains("AutomationProperties.SetName(more, \"More for \" + collection.Name)", personal);
        Assert.Contains("more.Click += (_, _) => menu.ShowAt(more)", personal);
        Assert.DoesNotContain("ConfigurePosterHover(card, more)", personal);
        Assert.Contains("SelectedKind = kind; Hide();", chooser);
        Assert.Contains("AutomationProperties.SetName(button, label)", chooser);
        Assert.Contains("new RadioButton", pick);
        Assert.DoesNotContain("BuildTemplateCard", personal);
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
