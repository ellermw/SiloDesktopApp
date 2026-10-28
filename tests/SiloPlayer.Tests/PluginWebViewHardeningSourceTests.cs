namespace SiloPlayer.Tests;

public sealed class PluginWebViewHardeningSourceTests
{
    [Fact]
    public void PluginPagesCannotOpenUnfilteredPopupsOrGrantDevicePermissions()
    {
        var page = ReadRepoFile("src", "SiloPlayer", "Views", "PluginRoutePage.xaml.cs").ReplaceLineEndings("\n");

        // window.open would otherwise create a WebView2 popup in the same profile
        // without the same-origin navigation and cookie-free resource filters.
        Assert.Contains("NewWindowRequested += (_, eventArgs) => eventArgs.Handled = true;", page);
        Assert.Contains("eventArgs.State = CoreWebView2PermissionState.Deny;", page);
        Assert.Contains("Settings.AreHostObjectsAllowed = false;", page);
        Assert.Contains("Settings.AreDevToolsEnabled = false;", page);

        // Hardening is in place before the plugin document can load.
        Assert.True(page.IndexOf("NewWindowRequested +=", StringComparison.Ordinal) <
            page.IndexOf("PluginWebView.Source = new Uri(url);", StringComparison.Ordinal));
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
