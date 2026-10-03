namespace SiloPlayer.Tests;

public sealed class UserFacingKeyboardParitySourceTests
{
    [Fact]
    public void SubtitleAppearanceColorChoicesAreFocusableButtons()
    {
        var source = Read("src", "SiloPlayer", "Controls", "SubtitleAppearanceDialog.xaml.cs");

        Assert.Contains("AutomationProperties.SetName(button, sw.Label)", source);
        Assert.Contains("button.Click += (_, _) =>", source);
        Assert.Contains("Content: Ellipse e", source);
        Assert.DoesNotContain("ellipse.Tapped +=", source);
    }

    [Fact]
    public void SettingsSubtitleColorsAndImportRunsAreFocusable()
    {
        var source = Read("src", "SiloPlayer", "Views", "SettingsPage.xaml.cs");

        Assert.Contains("private Button BuildHistoryRunCard", source);
        Assert.Contains("Content: Border border", source);
        Assert.DoesNotContain("swatch.Tapped +=", source);
        Assert.DoesNotContain("card.Tapped += (_, _) =>", source);
    }

    [Fact]
    public void LibraryCollectionCardsExposeSeparateOpenAndPinActions()
    {
        var source = Read("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");

        Assert.Contains("AutomationProperties.SetName(openButton, $\"Open {collection.Title}\")", source);
        Assert.Contains("openButton.Click +=", source);
        Assert.DoesNotContain("card.Tapped += (s, _) =>", source);
    }

    [Fact]
    public void SharedMediaCardsExposeKeyboardActionsWithoutBubblingNestedButtons()
    {
        var audiobookXaml = Read("src", "SiloPlayer", "Controls", "AudiobookSquareCard.xaml");
        var audiobookCode = Read("src", "SiloPlayer", "Controls", "AudiobookSquareCard.xaml.cs");
        var landscapeXaml = Read("src", "SiloPlayer", "Controls", "LandscapeCard.xaml");
        var landscapeCode = Read("src", "SiloPlayer", "Controls", "LandscapeCard.xaml.cs");
        var posterCode = Read("src", "SiloPlayer", "Controls", "PosterCard.xaml.cs");

        Assert.Contains("IsTabStop=\"True\"", audiobookXaml);
        Assert.Contains("KeyDown=\"Card_KeyDown\"", audiobookXaml);
        Assert.Contains("ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this)", audiobookCode);
        Assert.Contains("x:Name=\"HeadingButton\"", landscapeXaml);
        Assert.Contains("x:Name=\"SubtitleButton\"", landscapeXaml);
        Assert.Contains("MediaItem.ItemSource != \"episode_carousel\"", landscapeCode);
        Assert.Contains("ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this)", landscapeCode);
        Assert.Contains("ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), this)", posterCode);
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
