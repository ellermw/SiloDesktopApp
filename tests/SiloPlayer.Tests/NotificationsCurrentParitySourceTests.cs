namespace SiloPlayer.Tests;

public sealed class NotificationsCurrentParitySourceTests
{
    [Fact]
    public void InboxLoadDoesNotDependOnOptionalPreferenceOrUnreadQueries()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "NotificationsViewModel.cs");

        Assert.Contains("var inbox = await inboxTask", source);
        Assert.Contains("TryLoadUnreadCountAsync()", source);
        Assert.Contains("TryLoadPreferencesAsync()", source);
        Assert.Contains("if (preferencesResult.Value != null)", source);
        Assert.DoesNotContain("await Task.WhenAll(inboxTask, unreadTask, prefsTask)", source);
    }

    [Fact]
    public void PreferenceMutationsAreSnapshottedAndLatestResponseWins()
    {
        var source = ReadRepoFile("src", "SiloPlayer", "ViewModels", "NotificationsViewModel.cs");
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "NotificationsPage.xaml.cs");

        Assert.Contains("ClonePreferences(Preferences)", source);
        Assert.Contains("Interlocked.Increment(ref _preferencesSaveVersion)", source);
        Assert.Contains("version == Volatile.Read(ref _preferencesSaveVersion)", source);
        Assert.Contains("Preferences = await _notificationsApi.GetPreferencesAsync()", source);
        Assert.Contains("ViewModel.PreferencesErrorMessage", page);
        Assert.Contains("SyncPreferenceControls();", page);
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
