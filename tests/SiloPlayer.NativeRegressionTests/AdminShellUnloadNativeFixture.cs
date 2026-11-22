using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminMonitorShellNativeFixture
{
    /// <summary>A field that may not exist on the page under test: its absence is a failed check, not an exception.</summary>
    private static object? Optional(object target, string name) => target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);

    /// <summary>
    /// Regression for the exit crash of the native groups whose Admin shell left the window without a Frame navigation:
    /// the shell stayed subscribed to the singleton authority, an account change ran into the unloaded page, and (at the
    /// desktop width) a new sidebar with a new bitmap was built on a page that is no longer in the tree. The native host
    /// then died with 0xC000027B (RO_E_CLOSED) after every check had passed. An unloaded shell must hold no
    /// subscription and build nothing, and the same page back in the tree takes its subscriptions up again and shows a
    /// live page.
    /// </summary>
    private static async Task ShellUnloadAsync(Window window, AuthService auth)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        var outer = new Frame();
        window.Content = outer;
        outer.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)outer.Content;
        var host = (Grid)Field(shell, "_desktopSidebarHost")!;
        await Until(() => shell.IsLoaded && host.Children.Count > 0 && Field(shell, "_narrow") is false);
        Check(shell.IsLoaded && Field(shell, "_narrow") is false && host.Children.Count > 0, "shell unload case starts at the desktop width with its sidebar built");
        Check(Field(shell, "_active") is true && Optional(shell, "_attached") is true && Field(shell, "_lease") != null, "an open shell holds its subscriptions and lease");
        var sidebar = host.Children[0];
        var inner = (Frame)Field(shell, "_frame")!;
        await Until(() => inner.Content is AdminActivityPage { IsLoaded: true });
        var firstPage = inner.Content as AdminActivityPage;
        Check(firstPage != null && Field(firstPage, "_active") is true, "the Activity page under the shell is live before the unload");
        var backBefore = inner.BackStack.Count;

        // The shell leaves the tree without a Frame navigation, as when the window content is replaced or the window closes.
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.Unloaded += (_, _) => unloaded.TrySetResult();
        window.Content = new Grid();
        await Task.WhenAny(unloaded.Task, Task.Delay(4000));
        Check(unloaded.Task.IsCompleted, "the shell unloads when the window content is replaced");
        await Until(() => Optional(shell, "_attached") is false);
        Check(Field(shell, "_active") is false && Optional(shell, "_attached") is false && Field(shell, "_lease") == null,
            "an unloaded shell is retired: no subscription and no lease");

        // The account changes while the page is detached. Nothing may run into it.
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Task.Delay(400);
        Check(host.Children.Count > 0 && ReferenceEquals(host.Children[0], sidebar),
            "an account change after the unload builds no new sidebar on the detached page");
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        await Task.Delay(200);

        // The same page back in the tree, still the Frame's content, takes its subscriptions and lease up again.
        window.Content = outer;
        await Until(() => shell.IsLoaded && Optional(shell, "_attached") is true);
        Check(Field(shell, "_active") is true && Optional(shell, "_attached") is true && Field(shell, "_lease") != null && host.Children.Count > 0,
            "the same shell loaded again holds its subscriptions and lease and shows a sidebar");
        // The page under the shell was suspended by the unload and does not resume by itself: the location is opened again.
        await Until(() => inner.Content is AdminActivityPage { IsLoaded: true } page && !ReferenceEquals(page, firstPage) && Field(page, "_active") is true);
        Check(inner.Content is AdminActivityPage { IsLoaded: true } reopened && !ReferenceEquals(reopened, firstPage) && Field(reopened, "_active") is true
            && firstPage != null && Field(firstPage, "_active") is false,
            "after the reload the shell shows a live Activity page (a fresh one; the suspended one stays retired)");
        Check(shell.CurrentRequest.Route == AdminRoute.Activity && inner.BackStack.Count == backBefore,
            $"the reopened page keeps the location and adds no Back entry ({inner.BackStack.Count} vs {backBefore})");

        // Leaving by navigation retires it once; the unload that follows has nothing left to release.
        Check(outer.Navigate(typeof(Page)), "the shell is left by a Frame navigation");
        await Until(() => !shell.IsLoaded);
        await Task.Delay(200);
        Check(Field(shell, "_active") is false && Optional(shell, "_attached") is false && Field(shell, "_lease") == null,
            "a shell left by navigation is retired and stays retired after its unload");
        var after = host.Children.Count > 0 ? host.Children[0] : null;
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Task.Delay(300);
        Check(ReferenceEquals(host.Children.Count > 0 ? host.Children[0] : null, after), "an account change after a navigation builds nothing on the departed shell");
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        window.Content = new Grid();
    }
}
