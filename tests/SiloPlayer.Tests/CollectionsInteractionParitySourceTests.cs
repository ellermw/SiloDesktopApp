namespace SiloPlayer.Tests;

public sealed class CollectionsInteractionParitySourceTests
{
    [Fact]
    public void CollectionAndTemplateCardsUseFocusableOpenActions()
    {
        var source = Read("src", "SiloPlayer", "Views", "CollectionsPage.xaml.cs");

        Assert.Contains("private Button BuildTemplateCard", source);
        Assert.Contains("AutomationProperties.SetName(card, $\"Use {template.Title} collection template\")", source);
        Assert.Contains("AutomationProperties.SetName(openButton, $\"Open {collection.Name}\")", source);
        Assert.Contains("AutomationProperties.SetName(openButton, $\"Open {collection.Title}\")", source);
        Assert.DoesNotContain("card.Tapped += (_, _) =>\n        {\n            ShowTemplateConfigInGallery", source);
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
