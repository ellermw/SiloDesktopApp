namespace SiloPlayer.Tests;

/// <summary>
/// An automatic refresh of the current route (reconnect catch-up, access change, language change) is not a departure: while an Admin page holds unsaved edits it
/// is skipped, with no dialog and nothing run later. The behaviour is driven through the real Frame by the native checks (<c>access-navigation</c>,
/// <c>admin-configure-storage</c>); the shell is not instantiated there, so its wiring is checked in the source here.
/// </summary>
public sealed class NavigationRefreshSkipTests
{
    [Fact]
    public void TheShellGivesTheRefreshTheSameConditionAsTheDepartureGuardAndNavigationServiceSkipsOnIt()
    {
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.AdminMonitor.cs").ReplaceLineEndings();
        Assert.Contains("private bool AdminPageHoldsUnsavedEdits => ContentFrame.Content is AdminShellPage { RequiresLeaveConfirmation: true } && _adminAuthority?.IsAvailable == true;", shell);
        Assert.Contains("_navigationService.CurrentPageHoldsUnsavedEdits = () => AdminPageHoldsUnsavedEdits;", shell);
        Assert.Contains("if (_adminNavigationApproved || !AdminPageHoldsUnsavedEdits) return;", shell);
        Assert.Contains("_navigationService.CurrentPageHoldsUnsavedEdits = null;", shell);
        var navigation = ReadRepoFile("src", "SiloPlayer", "Helpers", "NavigationService.cs").ReplaceLineEndings();
        var refresh = navigation[navigation.IndexOf("public bool RefreshCurrentEntry()", StringComparison.Ordinal)..];
        refresh = refresh[..refresh.IndexOf("public bool NavigateReplacingCurrentEntry", StringComparison.Ordinal)];
        Assert.Contains("if (CurrentPageHoldsUnsavedEdits?.Invoke() == true) return false;", refresh);
        // The skip comes before anything navigates.
        Assert.True(refresh.IndexOf("CurrentPageHoldsUnsavedEdits?.Invoke()", StringComparison.Ordinal) < refresh.IndexOf("frame.Navigate(", StringComparison.Ordinal));
        Assert.Contains("if (_committedNavigations == committedBefore)", refresh);
        // A cancelled refresh is recognised before the stacks are touched, not by an HRESULT afterwards.
        Assert.True(refresh.IndexOf("_committedNavigations == committedBefore", StringComparison.Ordinal) < refresh.IndexOf("frame.ForwardStack.Clear()", StringComparison.Ordinal));
    }

    // A skipped refresh is not remembered: nothing in the navigation service can navigate later by itself (an earlier version used a timer, which could remount
    // the Admin shell onto another page and was not stopped when the window closed).
    [Fact]
    public void NavigationServiceHasNoTimerAndRemembersNoSkippedRefresh()
    {
        var navigation = ReadRepoFile("src", "SiloPlayer", "Helpers", "NavigationService.cs");
        Assert.DoesNotContain("Timer", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Owed", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("Deferral", navigation, StringComparison.Ordinal);
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
