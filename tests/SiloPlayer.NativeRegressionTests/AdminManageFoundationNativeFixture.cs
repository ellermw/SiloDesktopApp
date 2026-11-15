using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// G0 Admin Manage foundation in the real shell. Synthetic admin only; no live
/// server, account or user data; no screenshots (no Manage page exists yet).
/// Proves: no Manage section or search entry without a registration; an
/// unregistered route opens nothing and never falls back to another page;
/// a page's in-place URL replacement survives Back; navigation has no
/// transition; Monitor pages are unchanged. Program.cs dispatch
/// (SILO_NATIVE_TEST_ONLY=admin-manage-foundation) is wired by the lead.
/// </summary>
internal static class AdminManageFoundationNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 4000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse("[]").RootElement.Clone();
            return channel == "scans";
        }
        private sealed class Release : IDisposable { public void Dispose() { } }
        internal void Silence() { SnapshotReceived?.Invoke("", default); EventReceived?.Invoke("", "", default); StateChanged?.Invoke(CurrentState); }
    }

    private sealed class Server : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/sessions" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/diagnostics/reports" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[]}",
                "/api/v2/libraries" => "{\"items\":[]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false,\"restart_mark_count\":0,\"restart_requested\":false}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    internal static async Task RunAsync(Window window)
    {
        using var http = new HttpClient(new Server());
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var api = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(api, authority, new Realtime());
        var navigation = new NavigationService();
        var serviceCollection = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        await using var services = serviceCollection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        field.SetValue(null, services);
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            Check(!AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Libraries)), "an unregistered Manage route does not enter Admin");
            Check(outer.Content == null, "refused entry leaves the app frame unchanged");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Activity)), "a Monitor route enters Admin");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminActivityPage && shell.IsLoaded);
            Check(inner.Content is AdminActivityPage && shell.CurrentRoute == AdminRoute.Activity, "Activity is shown");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "admin page frame has no content transition");

            // No Manage section, topic or label without a registration.
            var manageHeading = AdminText.T("web.admin.shell.nav.section.manage");
            var sidebar = (Grid)Field(shell, "_desktopSidebarHost")!;
            await Until(() => sidebar.Children.Count > 0);
            Check(sidebar.Children.Count > 0, "desktop sidebar is shown at 1440px");
            var texts = Descendants(sidebar).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(!texts.Any(text => text.Equals(manageHeading, StringComparison.OrdinalIgnoreCase)), "no Manage section without registered pages");
            Check(!texts.Any(text => text.StartsWith("web.admin.", StringComparison.Ordinal) || text.StartsWith("WEB.ADMIN.", StringComparison.Ordinal)),
                "no message key renders as sidebar text");
            foreach (var label in new[] { "Dashboard", "Activity", "Logs" })
                Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == label), $"Monitor sidebar keeps {label}");
            Check(!Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) is "Libraries" or "Users" or "Requests"),
                "no Manage topic button is offered");

            // An unregistered route opens nothing, prompts nothing and never shows another page.
            var shown = inner.Content;
            shell.Navigate(new AdminRouteRequest(AdminRoute.Users));
            shell.Navigate(AdminRouteRequest.UserDetail("7"));
            await Task.Delay(200);
            Check(ReferenceEquals(inner.Content, shown) && shell.CurrentRoute == AdminRoute.Activity, "unregistered routes are ignored without a Dashboard fallback");
            Check(!inner.CanGoBack, "ignored routes add no history");
            Check(AdminShellPage.TryOpenHref("/admin/maintenance") is { Kind: AdminRouteResolutionKind.Unsupported }, "a Configure link reports unsupported");
            Check(AdminShellPage.TryOpenHref("/admin/users?tab=invitations") is { Kind: AdminRouteResolutionKind.Unsupported, Request: null }

                && ReferenceEquals(inner.Content, shown), "a Manage link resolves without opening a missing page");

            Check(AdminShellPage.TryOpenHref("/admin/newer-feature") is { Kind: AdminRouteResolutionKind.Unsupported }
                && ReferenceEquals(inner.Content, shown), "unknown server cross-links report unavailable without changing pages");

            // In-place URL replacement: same page instance, Back returns to the replaced location.
            var activity = (AdminActivityPage)inner.Content;
            var replaced = shell.CurrentRequest.WithQuery("user", "7");
            Check(!shell.ReplaceRequest(activity, new AdminRouteRequest(AdminRoute.ServerLog)), "a page cannot replace another route");
            Check(!shell.ReplaceRequest(new Page(), replaced), "only the shown page may replace its location");
            Check(shell.ReplaceRequest(activity, replaced), "the shown page replaces its URL state");
            Check(ReferenceEquals(inner.Content, activity) && shell.CurrentRequest.Equals(replaced), "replacement keeps the page mounted");
            Check(!inner.CanGoBack, "replacement adds no history entry");
            shell.Navigate(new AdminRouteRequest(AdminRoute.ClientDiagnostics));
            await Until(() => inner.Content is AdminDiagnosticsPage);
            Check(inner.Content is AdminDiagnosticsPage && shell.CurrentRoute == AdminRoute.ClientDiagnostics, "Diagnostics opens");
            Check(shell.TryGoBack(), "Back is handled inside Admin");
            await Until(() => inner.Content is AdminActivityPage);
            Check(inner.Content is AdminActivityPage && shell.CurrentRequest.Get("user") == "7", "Back restores the replaced Activity location");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Back swaps in place without a transition");

            // Search lists Monitor pages only.
            var search = (Task)typeof(AdminShellPage).GetMethod("ShowSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
            await Until(() => Field(shell, "_searchOverlay") != null);
            var overlay = (Grid)Field(shell, "_searchOverlay")!;
            var query = Descendants(overlay).OfType<TextBox>().Single();
            query.Text = "sonarr";
            await Until(() => Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")));
            Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")),
                "search never offers an unregistered Manage page");
            query.Text = "";
            await Until(() => Descendants(overlay).OfType<Button>().Count() >= 5);
            Check(Descendants(overlay).OfType<Button>().Count(button => AutomationProperties.GetName(button) is "Dashboard" or "Activity" or "Server Log" or "Client Diagnostics" or "Settings") == 5,
                "empty search lists exactly the five Monitor pages");
            Program.Log("Starting foundation search close");
            ((Action)Field(shell, "_closeSearch")!).Invoke();
            Program.Log("Closed foundation search overlay");
            await search;
            await Task.Delay(200);
            Program.Log("Settled foundation search close");

            // A test-only registered page exercises the actual shell guard and suspension.
            Program.Log("Starting unregistered shell departure");
            outer.Navigate(typeof(Page));
            await Task.Delay(200);
            Program.Log("Departed unregistered shell");
            serviceCollection.AddSingleton(new AdminPageRegistration(AdminRoute.RequestQueue, typeof(AdminActivityPage)));
            serviceCollection.AddSingleton<IAdminNavBadgeSource>(new TestBadge());
            await using var registered = serviceCollection.BuildServiceProvider();
            field.SetValue(null, registered);
            Program.Log("Opening test-registered Manage route");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.RequestQueue)), "registered Manage page enters Admin");
            await Until(() => outer.Content is AdminShellPage);
            var managedShell = (AdminShellPage)outer.Content;
            var managedFrame = (Frame)Field(managedShell, "_frame")!;
            await Until(() => managedFrame.Content is AdminActivityPage && managedShell.IsLoaded);
            var managedPage = new AdminManageTestPage();
            managedFrame.Content = managedPage;
            Check(Descendants((Grid)Field(managedShell, "_desktopSidebarHost")!).OfType<Border>()
                .Any(element => AutomationProperties.GetName(element) == "3 pending requests"),
                "registered request badge is rendered in the real sidebar");
            Check(managedShell.RequiresLeaveConfirmation, "generic dirty guard reaches the real shell");
            managedShell.Navigate(new AdminRouteRequest(AdminRoute.Activity));
            await Until(() => managedPage.LeaveChecks > 0);
            Check(ReferenceEquals(managedFrame.Content, managedPage), "generic guard refusal keeps the Manage page");
            managedPage.AllowLeave = true;
            managedShell.Navigate(new AdminRouteRequest(AdminRoute.Activity));
            await Until(() => managedFrame.Content is AdminActivityPage);
            Check(managedFrame.Content is AdminActivityPage, "generic guard acceptance permits Monitor navigation");
            managedShell.Navigate(new AdminRouteRequest(AdminRoute.RequestQueue));
            await Until(() => managedFrame.Content is AdminActivityPage);
            var departing = new AdminManageTestPage();
            managedFrame.Content = departing;
            outer.Navigate(typeof(Page));
            await Until(() => departing.Suspensions > 0);
            Check(departing.Suspensions == 1, "outer departure suspends the registered Manage page once");
        }
        finally
        {
            window.Content = previousContent;
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Manage foundation fixture failed: {string.Join("; ", Failures)}");
        Program.Log("PASS: Admin Manage foundation native fixture completed; no Manage page, live server or parity certification.");
    }

    private sealed class TestBadge : IAdminNavBadgeSource
    {
        public AdminRoute Route => AdminRoute.RequestQueue;
        public AdminNavBadge? Current => new(AdminNavBadgeKind.Count, 3, "3 pending requests");
        public event Action? Changed { add { } remove { } }
    }

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}

public sealed class AdminManageTestPage : Page, IAdminNavigationGuard, IAdminPageSuspension
{
    public AdminManageTestPage() { Content = new TextBlock { Text = "Synthetic Manage guard page" }; }
    public bool AllowLeave { get; set; }
    public int LeaveChecks { get; private set; }
    public int Suspensions { get; private set; }
    public bool RequiresLeaveConfirmation => !AllowLeave;
    public Task<bool> CanLeaveAsync() { LeaveChecks++; return Task.FromResult(AllowLeave); }
    public void SuspendAdminPage() => Suspensions++;
}
