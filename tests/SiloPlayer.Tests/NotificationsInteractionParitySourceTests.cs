namespace SiloPlayer.Tests;

public sealed class NotificationsInteractionParitySourceTests
{
    [Fact]
    public void NotificationActionsRemainVisibleToKeyboardFocusAndAnnounceState()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "NotificationsPage.xaml");
        var code = Read("src", "SiloPlayer", "Views", "NotificationsPage.Inbox.cs");

        Assert.Contains("AutomationProperties.SetName(row.MarkRead", code);
        Assert.Contains("AutomationProperties.SetName(mark", code);
        Assert.DoesNotContain("Opacity = 0", code);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("unread > 0 ? FontWeights.SemiBold : FontWeights.Medium", code);
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
