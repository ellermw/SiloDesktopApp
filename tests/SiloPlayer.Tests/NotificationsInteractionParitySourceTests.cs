namespace SiloPlayer.Tests;

public sealed class NotificationsInteractionParitySourceTests
{
    [Fact]
    public void NotificationActionsRemainVisibleToKeyboardFocusAndAnnounceState()
    {
        var xaml = Read("src", "SiloPlayer", "Views", "NotificationsPage.xaml");
        var code = Read("src", "SiloPlayer", "Views", "NotificationsPage.xaml.cs");

        Assert.Contains("GotFocus=\"InlineMarkReadButton_GotFocus\"", xaml);
        Assert.Contains("LostFocus=\"InlineMarkReadButton_LostFocus\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Mark as read\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("notification.IsUnread ? FontWeights.SemiBold : FontWeights.Medium", code);
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
