namespace SiloPlayer.Tests;

public sealed class CalendarInteractionParitySourceTests
{
    [Fact]
    public void CalendarDayAndEventSurfacesSupportKeyboardAndControllerActivation()
    {
        var source = Read("src", "SiloPlayer", "Views", "CalendarPage.xaml.cs");

        Assert.Contains("var cell = new Button", source);
        Assert.Contains("cell.Click += (_, _) => SelectDay", source);
        Assert.Contains("var cardButton = new Button", source);
        Assert.Contains("AutomationProperties.SetName(cardButton", source);
        Assert.Contains("cardButton.Click +=", source);
        Assert.DoesNotContain("root.Tapped +=", source);
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
