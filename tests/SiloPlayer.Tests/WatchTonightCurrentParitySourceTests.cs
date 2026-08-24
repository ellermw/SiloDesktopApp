namespace SiloPlayer.Tests;

public sealed class WatchTonightCurrentParitySourceTests
{
    [Fact]
    public void PlayActionsStartPlayableCardsInsteadOfOpeningTheirDetailPage()
    {
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "WatchTonightDialog.xaml.cs");

        Assert.Contains("case \"movie\":", code);
        Assert.Contains("case \"episode\":", code);
        Assert.Contains("case \"audiobook\":", code);
        Assert.Contains(".PlayAsync(card.ContentId)", code);
        Assert.Contains("Navigate<EbookReaderPage>", code);
    }

    [Fact]
    public void DialogUsesWebStyleHeaderCloseAndResetsOnClose()
    {
        var markup = ReadRepoFile("src", "SiloPlayer", "Controls", "WatchTonightDialog.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Controls", "WatchTonightDialog.xaml.cs");

        Assert.DoesNotContain("CloseButtonText=", markup);
        Assert.Contains("Click=\"CloseButton_Click\"", markup);
        Assert.Contains("AutomationProperties.Name=\"Close Watch Tonight\"", markup);
        Assert.Contains("this.Closed += OnClosed", code);
        Assert.Contains("_selectedGenres.Clear()", code);
        Assert.Contains("_cards.Clear()", code);
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
