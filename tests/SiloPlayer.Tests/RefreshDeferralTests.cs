using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// An automatic refresh of the current route (reconnect catch-up, access change, language change) is not a departure: a page that holds the viewer's unsaved edits
/// is neither refreshed nor asked about; the refresh is owed and runs once the edits are gone. The native checks drive the same rule through the real Frame
/// (<c>access-navigation</c>).
/// </summary>
public sealed class RefreshDeferralTests
{
    [Fact]
    public void APageWithoutEditsIsRefreshedAtOnceAndNothingIsOwed()
    {
        var holds = false;
        var deferral = new RefreshDeferral { HoldsEdits = () => holds };
        Assert.False(deferral.ShouldDefer());
        Assert.False(deferral.Owed);
        Assert.False(deferral.TakeDue());
    }

    [Fact]
    public void ANavigationServiceWithNoGuardRefreshesAsBefore()
    {
        var deferral = new RefreshDeferral();
        Assert.False(deferral.ShouldDefer());
        Assert.False(deferral.Owed);
    }

    [Fact]
    public void APageWithADraftDefersTheRefreshAndItIsDueOnlyOnceTheDraftIsGone()
    {
        var holds = true;
        var deferral = new RefreshDeferral { HoldsEdits = () => holds };
        Assert.True(deferral.ShouldDefer());
        Assert.True(deferral.Owed);
        // Still holding edits (a second request, a poll): still owed, never due.
        Assert.True(deferral.ShouldDefer());
        Assert.False(deferral.TakeDue());
        Assert.True(deferral.Owed);
        // Saved, or discarded by the admin.
        holds = false;
        Assert.True(deferral.TakeDue());
        Assert.False(deferral.Owed);
        // It is performed once.
        Assert.False(deferral.TakeDue());
    }

    [Fact]
    public void LeavingThePageMakesTheOwedRefreshMoot()
    {
        var holds = true;
        var deferral = new RefreshDeferral { HoldsEdits = () => holds };
        Assert.True(deferral.ShouldDefer());
        deferral.Clear();
        holds = false;
        Assert.False(deferral.Owed);
        Assert.False(deferral.TakeDue());
    }

    [Fact]
    public void AnAccessChangeThatRemovesTheAdminsRightIsNotDeferred()
    {
        // The shell reports edits only for a current owner of an available admin authority: once that is gone the guard answers false, so the refresh that takes
        // the admin away runs at once, whatever the draft holds, and nothing stays owed.
        var authorityAvailable = true;
        var draft = true;
        var deferral = new RefreshDeferral { HoldsEdits = () => authorityAvailable && draft };
        Assert.True(deferral.ShouldDefer());
        authorityAvailable = false;
        Assert.False(deferral.ShouldDefer());
        Assert.False(deferral.Owed);
    }

    [Fact]
    public void ARefreshRequestedWithoutEditsAfterOneWasOwedSupersedesIt()
    {
        var holds = true;
        var deferral = new RefreshDeferral { HoldsEdits = () => holds };
        Assert.True(deferral.ShouldDefer());
        holds = false;
        Assert.False(deferral.ShouldDefer());
        Assert.False(deferral.Owed);
    }

    // The shell is not instantiated by the native fixtures, so its wiring is checked in the source: the refresh and the departure guard share one condition.
    [Fact]
    public void TheShellGivesTheRefreshTheSameConditionAsTheDepartureGuardAndNavigationServiceDefersOnIt()
    {
        var shell = ReadRepoFile("src", "SiloPlayer", "MainWindow.AdminMonitor.cs").ReplaceLineEndings();
        Assert.Contains("private bool AdminPageHoldsUnsavedEdits => ContentFrame.Content is AdminShellPage { RequiresLeaveConfirmation: true } && _adminAuthority?.IsAvailable == true;", shell);
        Assert.Contains("_navigationService.CurrentPageHoldsUnsavedEdits = () => AdminPageHoldsUnsavedEdits;", shell);
        Assert.Contains("if (_adminNavigationApproved || !AdminPageHoldsUnsavedEdits) return;", shell);
        Assert.Contains("_navigationService.CurrentPageHoldsUnsavedEdits = null;", shell);
        var navigation = ReadRepoFile("src", "SiloPlayer", "Helpers", "NavigationService.cs").ReplaceLineEndings();
        var refresh = navigation[navigation.IndexOf("public bool RefreshCurrentEntry()", StringComparison.Ordinal)..];
        refresh = refresh[..refresh.IndexOf("public bool NavigateReplacingCurrentEntry", StringComparison.Ordinal)];
        Assert.Contains("if (_refreshDeferral.ShouldDefer()) { StartOwedRefreshTimer(frame); return false; }", refresh);
        Assert.Contains("if (_committedNavigations == committedBefore)", refresh);
        // The cancelled refresh is no longer recognised by an HRESULT first: the stacks are not touched when nothing was committed.
        Assert.True(refresh.IndexOf("_committedNavigations == committedBefore", StringComparison.Ordinal) < refresh.IndexOf("frame.ForwardStack.Clear()", StringComparison.Ordinal));
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
