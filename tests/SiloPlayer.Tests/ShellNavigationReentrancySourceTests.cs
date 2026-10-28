namespace SiloPlayer.Tests;

public sealed class ShellNavigationReentrancySourceTests
{
    [Fact]
    public void NavigatedHandlerDefersSidebarMenuSwap()
    {
        var window = ReadRepoFile("src", "SiloPlayer", "MainWindow.xaml.cs").ReplaceLineEndings("\n");
        var handler = MethodBody(window, "private void OnNavigated_SynchronizeShellChrome(");

        // Navigated runs synchronously inside NavView.ItemInvoked; replacing
        // MenuItems there removes the container NavigationView is dispatching.
        Assert.Contains("QueueSettingsSidebarSynchronization();", handler);
        Assert.DoesNotContain("SynchronizeSettingsSidebar();", handler);
        Assert.DoesNotContain("RestoreAppSidebar();", handler);
        Assert.DoesNotContain("RebuildSettingsSidebar();", handler);

        var sidebar = ReadRepoFile("src", "SiloPlayer", "MainWindow.SettingsSidebar.cs").ReplaceLineEndings("\n");
        var queue = MethodBody(sidebar, "private void QueueSettingsSidebarSynchronization(");
        Assert.Contains("DispatcherQueue.TryEnqueue(", queue);
        Assert.Contains("if (_settingsSidebarSynchronizationQueued) return;", queue);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new InvalidOperationException("Unbalanced method body: " + signature);
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
