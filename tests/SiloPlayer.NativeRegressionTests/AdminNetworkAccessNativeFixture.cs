using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Production Configure › Network access registration and Frame entry with synthetic provider data only.
/// The page's launcher is replaced before any data arrives, so synthetic enrollment URLs are captured, never opened.
/// </summary>
internal static class AdminNetworkAccessNativeFixture
{
    private const string Enrollment = "https://login.example.test/a/fixture-enrollment";
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        bool Met() { try { return condition(); } catch (InvalidOperationException) { return false; } }
        for (var i = 0; i < milliseconds / 25 && !Met(); i++) await Task.Delay(25);
        if (!Met()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<string> Bodies = new();
        internal TaskCompletionSource? CapabilitiesGate, CommandGate, StatusGate;
        internal bool NetbirdFails = true, CommandServerError, CapabilitiesFail = true, NoProviders, StatusFails;
        internal string TailscaleDisplayName = "Tailscale";
        internal readonly Dictionary<string, string> States = new(StringComparer.Ordinal)
        {
            ["api"] = "connected", ["node:10"] = "disconnected", ["node:12"] = "awaiting_authorization",
            ["node:14"] = "connecting", ["node:16"] = "error", ["node:18"] = "unavailable",
        };
        internal int Count(string request) => Requests.Count(r => r == request);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue($"{request.Method} {path}");
            if (request.Content != null) Bodies.Enqueue(await request.Content.ReadAsStringAsync(cancellationToken));
            switch (path)
            {
                case "/api/v2/network-access/capabilities":
                    if (CapabilitiesGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                    if (CapabilitiesFail) return Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                    if (NoProviders) return Json(new { revision = "fixture", state = "not_configured", allowed = false, providers = Array.Empty<object>() });
                    return Json(new
                    {
                        revision = "fixture", state = "available", allowed = true,
                        providers = new[]
                        {
                            new { provider = "tailscale", display_name = TailscaleDisplayName, installation_id = "7" },
                            new { provider = "netbird", display_name = "NetBird", installation_id = "8" },
                            new { provider = "headscale", display_name = "Headscale", installation_id = "9" },
                        },
                    });
                case "/api/v2/admin/network-access/tailscale/status":
                    if (StatusGate is { } statusGate) await statusGate.Task.WaitAsync(cancellationToken);
                    return StatusFails ? Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable) : Json(Status());
                case "/api/v2/admin/network-access/netbird/status":
                    return NetbirdFails ? Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable) : Json(new { provider = "netbird", hosts = Array.Empty<object>() });
                case "/api/v2/admin/network-access/headscale/status": return Json(new { provider = "headscale", hosts = Array.Empty<object>() });
                case "/api/v2/admin/network-access/tailscale/connect" or "/api/v2/admin/network-access/tailscale/disconnect":
                {
                    if (CommandGate is { } commandGate) await commandGate.Task.WaitAsync(cancellationToken);
                    if (CommandServerError) return Json(new { title = "Failed", status = 500 }, HttpStatusCode.InternalServerError);
                    using var body = JsonDocument.Parse(Bodies.Last());
                    var host = body.RootElement.GetProperty("hosts")[0].GetString()!;
                    States[host] = path.EndsWith("/connect", StringComparison.Ordinal) ? "awaiting_authorization" : "disconnected";
                    return Json(Status(), HttpStatusCode.Accepted);
                }
                case "/api/v2/admin/server/status": return Json(new { restart_required = false, restart_mark_count = 0, restart_requested = false });
                case "/api/v2/admin/system/build": return Json(new { display = "synthetic", available = true, build_number = 1 });
                default: return Json(new { });
            }
        }

        private object Status()
        {
            var heard = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);
            object Host(string id, string role, string name, string? origin = null, string? hostname = null, string[]? addresses = null,
                string? version = null, string? error = null, string? raw = null) => new Dictionary<string, object?>
            {
                ["host"] = new { id, role, name }, ["state"] = States[id], ["raw_state"] = raw, ["origin"] = origin, ["hostname"] = hostname,
                ["addresses"] = addresses ?? [], ["provider_version"] = version, ["error"] = error,
                ["auth_url"] = States[id] == "awaiting_authorization" ? Enrollment + "/" + id.Replace(':', '-') : null,
                ["updated_at"] = States[id] == "unavailable" ? null : heard,
            };
            return new
            {
                provider = "tailscale",
                hosts = new[]
                {
                    Host("api", "api", "Living Room", "https://silo.tail1234.ts.net", null, ["100.64.0.1", "fd7a:115c:a1e0::1"], "tailscale 1.80.2"),
                    Host("node:10", "proxy", "Basement proxy node with an unusually long descriptive name for wrapping checks",
                        "https://basement-proxy-node-with-a-very-long-overlay-hostname.tail1234.ts.net:8443",
                        "basement-proxy-node-with-a-very-long-overlay-hostname.tail1234.ts.net", ["100.64.0.10"], "tailscale 1.80.2"),
                    Host("node:12", "proxy", "Office", hostname: "office.tail1234.ts.net"),
                    Host("node:14", "proxy", "Garage", "ftp://garage.tail1234.ts.net"),
                    Host("node:16", "proxy", "", error: "provider reported: node key expired", raw: "needs_attention"),
                    Host("node:18", "proxy", "Attic", error: "plugin process is backoff: plugin process exited"),
                },
            };
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server { CapabilitiesGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        // The same registration entry point as App, rather than a test-only page registration.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        var launched = new ConcurrentQueue<Uri>();
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/network-access");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Network access is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Network access did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content is not AdminNetworkAccessSettingsPage; i++) await Task.Delay(25);
            if (inner.Content is not AdminNetworkAccessSettingsPage page) throw new InvalidOperationException("Network access did not activate its production WinUI Page.");
            page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            Check(true, "Network access activates through production registration, shell and Frame");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Network access navigation keeps page transitions suppressed");

            // Loading: header, intro and the capability skeleton while capabilities are in flight.
            await Until(() => Field<StackPanel>(page, "_loading").Visibility == Visibility.Visible, "capability skeleton");
            Check(Texts(page).Contains("Network Access") && Texts(page).Contains("Overlay-network providers such as Tailscale that reach this server without port forwarding."),
                "header shows the official title and settings description");
            Check(Texts(page).Any(t => t.StartsWith("Network access providers give this server an identity", StringComparison.Ordinal)), "intro paragraph uses the official text");
            await Task.Delay(100);
            Check(Math.Abs(((FrameworkElement)Field<StackPanel>(page, "_loading").Children[1]).ActualWidth - Field<StackPanel>(page, "_body").ActualWidth) < 1.5 && Field<StackPanel>(page, "_body").ActualWidth > 1100,
                "capability skeleton spans the full settings column");
            await CaptureAsync(outer, "network-loading.png");
            server.CapabilitiesGate.SetResult(); server.CapabilitiesGate = null;
            await Until(() => Field<StackPanel>(page, "_alert").Visibility == Visibility.Visible, "capabilities failure");
            Check(Text(page, "NetworkAccessProvidersError").Text == "Could not read network access providers." && Button(page, "NetworkAccessRetry").IsEnabled,
                "a failed capability read shows the official alert with Retry");
            Check(AutomationProperties.GetLiveSetting(Text(page, "NetworkAccessProvidersError")) == AutomationLiveSetting.Assertive, "capability alert is an assertive live region");
            await CaptureAsync(outer, "network-capabilities-error.png");
            server.CapabilitiesFail = false;
            Invoke(Button(page, "NetworkAccessRetry"));
            await Until(() => page.Controller?.State.Providers.Count == 3 && page.Controller.State.Providers.All(p => !p.Loading), "providers loaded");
            await Task.Delay(200);
            var controller = page.Controller!;
            Check(Field<StackPanel>(page, "_loading").Visibility == Visibility.Collapsed, "capability skeleton leaves after load");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered page and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(t => !IsInside(t, page)).Select(t => t.Text).ToArray();
            Check(shellText.Contains("Server") && shellText.Contains("Network Access"), "sidebar shows the Server topic with its Network Access leaf");
            Check(AdminNavigation.SearchGroups("tailscale", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Any(p => p.Route == AdminRoute.NetworkAccessSettings),
                "registered Network access is searchable through its official keywords");

            // Provider groups: isolated failure and empty provider.
            Check(Texts(page).Contains("Tailscale") && Texts(page).Contains("Access Silo through your Tailscale network without port forwarding."), "provider group uses display name and official description");
            Check(Text(page, "NetworkAccessStatusError_netbird").Text == "Could not read provider status." && Text(page, "NetworkAccessStatusError_netbird").Visibility == Visibility.Visible,
                "a failed provider shows its own status error");
            Check(Text(page, "NetworkAccessNoHosts_headscale").Visibility == Visibility.Visible && Text(page, "NetworkAccessNoHosts_headscale").Text == "No host runs this provider yet.",
                "a provider with no hosts shows the official empty message");
            Check(controller.State.Providers.Single(p => p.Summary.Provider == "tailscale").Status!.Hosts.Count == 6, "the failed provider leaves the others intact");

            // Every published host state, label, dot and action.
            var expected = new (string Id, string Label, string State, bool Disconnect, bool Enabled)[]
            {
                ("api", "Living Room · API server", "Connected", true, true),
                ("node:10", "Basement proxy node with an unusually long descriptive name for wrapping checks · Proxy node", "Disconnected", false, true),
                ("node:12", "Office · Proxy node", "Waiting for authorization", true, true),
                ("node:14", "Garage · Proxy node", "Connecting", true, true),
                ("node:16", "Proxy node", "Error", false, true),
                ("node:18", "Attic · Proxy node", "Plugin not running", false, false),
            };
            foreach (var (id, label, state, disconnect, enabled) in expected)
            {
                var row = Row(page, id);
                var texts = Descendants(row).OfType<TextBlock>().Where(t => t.Visibility == Visibility.Visible && Visible(t)).Select(t => t.Text).ToArray();
                Check(texts.Contains(label) && texts.Contains(state), $"{id} shows '{label}' and state '{state}'");
                var action = Button(page, (disconnect ? "NetworkAccessDisconnect_tailscale_" : "NetworkAccessConnect_tailscale_") + id);
                var other = Button(page, (disconnect ? "NetworkAccessConnect_tailscale_" : "NetworkAccessDisconnect_tailscale_") + id);
                Check(action.Visibility == Visibility.Visible && other.Visibility == Visibility.Collapsed && action.IsEnabled == enabled,
                    $"{id} offers {(disconnect ? "Disconnect" : "Connect")} ({(enabled ? "enabled" : "disabled")})");
                Check(AutomationProperties.GetName(action).StartsWith(disconnect ? "Disconnect " : "Connect ", StringComparison.Ordinal) && AutomationProperties.GetName(action).Contains(label, StringComparison.Ordinal),
                    $"{id} action has a host-specific accessible name");
                var dot = Descendants(row).OfType<Border>().First(b => b.Width == 8 && b.Height == 8);
                Check(dot.CornerRadius.TopLeft == 4 && dot.ActualWidth == 8, $"{id} state dot is an 8 DIP circle, not a button");
            }
            Check(!Descendants(Row(page, "node:16")).OfType<TextBlock>().Any(t => t.Text == "needs_attention"), "raw provider state stays out of the label");
            var attic = Button(page, "NetworkAccessConnect_tailscale_node:18");
            Check(AutomationProperties.GetHelpText(attic) == "Start the plugin from the Plugins page first." &&
                ToolTipService.GetToolTip((DependencyObject)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(attic))) as string == "Start the plugin from the Plugins page first.",
                "unavailable Connect is disabled with the official start-plugin hint");
            var atticTexts = Descendants(Row(page, "node:18")).OfType<TextBlock>().Select(t => t.Text).ToArray();
            Check(atticTexts.Contains("plugin process is backoff: plugin process exited") && !atticTexts.Any(t => t == "unavailable"), "unavailable host explains the plugin error in plain wording");
            Check(AutomationProperties.GetLiveSetting(Text(page, "NetworkAccessHostError_tailscale_node:18")) == AutomationLiveSetting.Polite, "host error is a polite status region");
            var api = Row(page, "api");
            var apiTexts = Descendants(api).OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToArray();
            Check(apiTexts.Contains("tailscale 1.80.2"), "provider version appears as a badge");
            var badge = Descendants(api).OfType<Border>().First(b => b.Child is TextBlock { Text: "tailscale 1.80.2" });
            Check(badge.CornerRadius.TopLeft == 10 && badge.BorderThickness.Left == 1 && badge.ActualWidth > badge.ActualHeight, "version badge is a bounded outline badge");
            Check(apiTexts.Contains("100.64.0.1, fd7a:115c:a1e0::1"), "addresses are joined by comma");
            Check(apiTexts.Any(t => t.StartsWith("Last heard from ", StringComparison.Ordinal) && t.Contains(", ", StringComparison.Ordinal)), "last-heard time is formatted as date, time");
            var reach = ReachText(api);
            Check(reach != null && Plain(reach) == "Clients reach this host at https://silo.tail1234.ts.net", "reach line links the origin and omits a duplicate hostname");
            var longReach = ReachText(Row(page, "node:10"));
            Check(longReach != null && Plain(longReach).EndsWith("(basement-proxy-node-with-a-very-long-overlay-hostname.tail1234.ts.net)", StringComparison.Ordinal),
                "a distinct hostname follows the origin link");
            var unsafeReach = ReachText(Row(page, "node:14"));
            Check(unsafeReach != null && Plain(unsafeReach).EndsWith("ftp://garage.tail1234.ts.net", StringComparison.Ordinal) && !unsafeReach.Inlines.OfType<Hyperlink>().Any(),
                "a non-web origin is shown as text, never as a link");
            Check(Descendants(Row(page, "node:12")).OfType<TextBlock>().Any(t => t.Text == "office.tail1234.ts.net" || Plain(t) == "office.tail1234.ts.net"), "hostname alone shows without a link");
            var authorize = AuthorizeLink(Row(page, "node:12"));
            Check(authorize != null && AutomationProperties.GetName(authorize) == "Open the authorization page", "waiting host shows the authorization link with an accessible name");
            Check(expected.Where(e => e.Id != "node:12").All(e => AuthorizeLink(Row(page, e.Id)) == null), "authorization link appears only while awaiting authorization");
            Check(!Texts(page).Any(t => t.Contains("fixture-enrollment", StringComparison.Ordinal)) && !Descendants(page).OfType<TextBlock>().Any(t => Plain(t).Contains("fixture-enrollment", StringComparison.Ordinal)),
                "the enrollment URL is never rendered as text");
            await CaptureAsync(outer, "network-rows-1440x900.png");

            // Links: deliberate activation through the page's guarded launcher.
            var originLink = reach!.Inlines.OfType<Hyperlink>().Single();
            Check(await ActivateAsync(page, originLink, "tailscale", "api", "Origin", launched) && launched.TryDequeue(out var originUri) && originUri.ToString() == "https://silo.tail1234.ts.net/",
                "origin link opens the safe overlay origin through the launcher");
            Check(await ActivateAsync(page, authorize, "tailscale", "node:12", "Enrollment", launched) && launched.TryDequeue(out var enrollUri) && enrollUri.Host == "login.example.test",
                "authorization link opens the captured enrollment URL through the launcher (fixture launcher, nothing opened)");

            // Keyboard: links and actions are reachable in reading order.
            authorize!.Focus(FocusState.Keyboard); await Task.Delay(60);
            var next = FocusManager.FindNextElement(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = page.XamlRoot.Content });
            Check(ReferenceEquals(next, Button(page, "NetworkAccessDisconnect_tailscale_node:12")), "Tab moves from the authorization link to that host's action");
            await CaptureAsync(outer, "network-authorize-keyboard-focus.png");

            // Command: Connect one host; only that host turns busy; exactly that host is sent once.
            server.CommandGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var statusReads = server.Count("GET /api/v2/admin/network-access/tailscale/status");
            var connect = Button(page, "NetworkAccessConnect_tailscale_node:10");
            connect.Focus(FocusState.Keyboard); await Task.Delay(40);
            Invoke(connect);
            await Until(() => !connect.IsEnabled, "connect busy");
            await Task.Delay(100);
            Check(server.Count("POST /api/v2/admin/network-access/tailscale/connect") == 1 && server.Bodies.Last() == """{"hosts":["node:10"]}""", "Connect posts exactly the clicked host once");
            Check(Descendants(connect).OfType<Viewbox>().Any(), "busy Connect shows its spinner");
            Check(Button(page, "NetworkAccessDisconnect_tailscale_api").IsEnabled && Button(page, "NetworkAccessDisconnect_tailscale_node:12").IsEnabled,
                "other hosts' actions stay enabled while one command is pending");
            await CaptureAsync(outer, "network-connect-busy.png");
            // A status poll may change the visible action while the original request is still pending.
            server.States["node:10"] = "connecting";
            typeof(AdminNetworkAccessController).GetMethod("OnPoll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
            await Until(() => Button(page, "NetworkAccessDisconnect_tailscale_node:10").Visibility == Visibility.Visible, "pending Connect switches to Disconnect");
            var switchedDisconnect = Button(page, "NetworkAccessDisconnect_tailscale_node:10");
            Check(!switchedDisconnect.IsEnabled && Descendants(switchedDisconnect).OfType<Viewbox>().Any(), "pending Connect spinner follows the visible Disconnect action after a poll");
            Check(!Descendants(connect).OfType<Viewbox>().Any(), "the collapsed Connect action releases its spinner");
            await CaptureAsync(outer, "network-connect-busy-transition.png");
            server.CommandGate.SetResult(); server.CommandGate = null;
            var disconnect10 = Button(page, "NetworkAccessDisconnect_tailscale_node:10");
            await Until(() => disconnect10.Visibility == Visibility.Visible && disconnect10.IsEnabled, "acknowledged state applied");
            await Until(() => server.Count("GET /api/v2/admin/network-access/tailscale/status") > statusReads, "post-command refresh");
            Check(AuthorizeLink(Row(page, "node:10")) != null, "acknowledgement shows the new authorization step for that host");
            await Task.Delay(150);
            Check(disconnect10.FocusState != FocusState.Unfocused, "focus returns to the host's action after its command");

            // Disconnect, and an unknown outcome that reconciles without a resend.
            server.CommandGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(Button(page, "NetworkAccessDisconnect_tailscale_api"));
            await Until(() => !Button(page, "NetworkAccessDisconnect_tailscale_api").IsEnabled, "Disconnect pending");
            server.States["api"] = "disconnected";
            typeof(AdminNetworkAccessController).GetMethod("OnPoll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
            await Until(() => Button(page, "NetworkAccessConnect_tailscale_api").Visibility == Visibility.Visible, "pending Disconnect switches to Connect");
            var switchedConnect = Button(page, "NetworkAccessConnect_tailscale_api");
            Check(!switchedConnect.IsEnabled && Descendants(switchedConnect).OfType<Viewbox>().Any(), "pending Disconnect spinner follows the visible Connect action after a poll");
            Check(!Descendants(Button(page, "NetworkAccessDisconnect_tailscale_api")).OfType<Viewbox>().Any(), "the collapsed Disconnect action releases its spinner");
            await CaptureAsync(outer, "network-disconnect-busy-transition.png");
            server.CommandGate.SetResult(); server.CommandGate = null;
            await Until(() => Button(page, "NetworkAccessConnect_tailscale_api").Visibility == Visibility.Visible && Button(page, "NetworkAccessConnect_tailscale_api").IsEnabled, "disconnect applied and command settled");
            Check(server.Bodies.Last() == """{"hosts":["api"]}""" || server.Bodies.Contains("""{"hosts":["api"]}"""), "Disconnect posts exactly the clicked host");
            AdminNetworkAccessCommandFeedback? feedback = null;
            controller.CommandFailed += value => feedback = value;
            server.CommandServerError = true;
            server.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            statusReads = server.Count("GET /api/v2/admin/network-access/tailscale/status");
            var posts = server.Count("POST /api/v2/admin/network-access/tailscale/connect");
            Invoke(Button(page, "NetworkAccessConnect_tailscale_api"));
            await Until(() => feedback != null, "unknown outcome");
            await Until(() => server.Count("GET /api/v2/admin/network-access/tailscale/status") > statusReads, "reconciliation read");
            Check(feedback?.Outcome == AdminNetworkAccessCommandOutcome.Unknown && server.Count("POST /api/v2/admin/network-access/tailscale/connect") == posts + 1,
                "an ambiguous failure reconciles with a status read and is not resent");
            const string requestedRefresh = "The connect request may or may not have reached the provider. A status refresh was requested; check the result before trying again.";
            Check(server.StatusGate?.Task.IsCompleted == false,
                "unknown command feedback arrives while its reconciliation read is held");
            Check(AdminNetworkAccessSettingsPage_CommandMessage(feedback!) == requestedRefresh, "unknown outcome reports only a requested status refresh while held");
            server.StatusFails = true;
            server.StatusGate.SetResult(); server.StatusGate = null;
            await Until(() => controller.State.Providers.Single(provider => provider.Summary.Provider == "tailscale").Failure != null, "reconciliation read failed");
            Check(AdminNetworkAccessSettingsPage_CommandMessage(feedback!) == requestedRefresh, "unknown outcome does not claim a completed status refresh after read failure");
            Check(AdminNetworkAccessSettingsPage_CommandMessage(new("tailscale", "api", AdminNetworkAccessCommandKind.Disconnect, AdminNetworkAccessCommandOutcome.Unknown)) ==
                "The disconnect request may or may not have reached the provider. A status refresh was requested; check the result before trying again.", "disconnect unknown outcome has complete truthful feedback");
            Check(AdminNetworkAccessSettingsPage_CommandMessage(new("tailscale", "api", AdminNetworkAccessCommandKind.Connect, AdminNetworkAccessCommandOutcome.Rejected, AdminFailureKind.Validation)) ==
                "This host is no longer known to the provider. A status refresh was requested; check the result before trying again.", "unknown host rejection has complete truthful feedback");
            server.CommandServerError = false; server.StatusFails = false;
            typeof(AdminNetworkAccessController).GetMethod("OnPoll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
            await Until(() => controller.State.Providers.Single(provider => provider.Summary.Provider == "tailscale") is { Loading: false, Failure: null }, "status read recovered");
            await Until(() => Button(page, "NetworkAccessConnect_tailscale_api").IsEnabled, "api action released");

            // Control states: rectangular template in every state, visible focus, no oval backdrop.
            foreach (var id in new[] { "NetworkAccessConnect_tailscale_api", "NetworkAccessDisconnect_tailscale_node:12" })
            {
                var button = Button(page, id);
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                {
                    var wasEnabled = button.IsEnabled;
                    button.IsEnabled = state != "Disabled";
                    VisualStateManager.GoToState(button, state, false); await Task.Delay(50);
                    var border = Descendants(button).OfType<Border>().Single(b => b.Name == "SettingsButtonRoot");
                    Check(border.CornerRadius.TopLeft == 10 && border.ActualWidth > border.ActualHeight && border.ActualHeight == 32, $"{id} keeps the 32 DIP radius-10 rectangle in {state}");
                    if (state == "Disabled") Check(border.Opacity == .5, $"{id} disabled state uses 50% opacity");
                    await CaptureAsync(Row(page, id.Split('_').Last()), $"network-{id.Replace(':', '-')}-{state}.png");
                    button.IsEnabled = wasEnabled;
                }
                VisualStateManager.GoToState(button, "Normal", false);
                button.Focus(FocusState.Keyboard); await Task.Delay(50);
                Check(button.FocusState == FocusState.Keyboard && button.UseSystemFocusVisuals && button.FocusVisualPrimaryThickness.Left == 3, $"{id} exposes a 3 DIP keyboard focus ring");
                await CaptureAsync(Row(page, id.Split('_').Last()), $"network-{id.Replace(':', '-')}-keyboard-focus.png");
            }

            // Polling keeps rows and focus; a hidden or deactivated window never polls; activation refreshes stale data only.
            var focusRow = Row(page, "node:12");
            var focusButton = Button(page, "NetworkAccessDisconnect_tailscale_node:12");
            focusButton.Focus(FocusState.Keyboard); await Task.Delay(50);
            statusReads = server.Count("GET /api/v2/admin/network-access/tailscale/status");
            Check(controller.IsPolling, "visible active page polls");
            await Until(() => server.Count("GET /api/v2/admin/network-access/tailscale/status") > statusReads, "15 s poll", 20000);
            await Task.Delay(200);
            Check(ReferenceEquals(Row(page, "node:12"), focusRow) && focusButton.FocusState == FocusState.Keyboard, "a poll keeps the row element and keyboard focus");
            var retainedProvider = Descendants(page).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "NetworkAccessProvider_tailscale");
            server.TailscaleDisplayName = "Renamed Overlay";
            var capabilityReads = server.Count("GET /api/v2/network-access/capabilities");
            page.SetHostActivity(visible: true, focused: false);
            Check(!controller.IsPolling, "deactivated window stops the poll timer");
            statusReads = server.Count("GET /api/v2/admin/network-access/tailscale/status");
            await Task.Delay(16500);
            Check(server.Count("GET /api/v2/admin/network-access/tailscale/status") == statusReads, "no status read while the window is deactivated");
            page.SetHostActivity(visible: true, focused: true);
            await Until(() => server.Count("GET /api/v2/admin/network-access/tailscale/status") > statusReads, "stale activation refresh");
            Check(controller.IsPolling && server.Count("GET /api/v2/network-access/capabilities") == capabilityReads + 1, "activation refreshes stale statuses and capabilities once");
            await Until(() => !controller.State.CapabilitiesLoading && controller.State.Providers.Any(p => p.Summary.DisplayName == server.TailscaleDisplayName), "provider display name refresh settled");
            await Task.Delay(100);
            Check(Texts(retainedProvider).Contains(server.TailscaleDisplayName) && AutomationProperties.GetName(retainedProvider) == server.TailscaleDisplayName,
                "capability refresh updates the retained provider heading and automation name");
            Check(ReferenceEquals(retainedProvider, Descendants(page).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "NetworkAccessProvider_tailscale")) &&
                ReferenceEquals(focusRow, Row(page, "node:12")), "provider display name refresh preserves the group and host row");
            page.SetHostActivity(visible: false, focused: true);
            Check(!controller.IsPolling, "hidden window stops the poll timer");
            page.SetHostActivity(visible: true, focused: true);
            server.NetbirdFails = false;

            // Layout at the required viewports: details shrink beside actions, with no overflow and shared gutters.
            foreach (var (width, height) in new[] { (1440, 900), (1440, 600), (1920, 600), (800, 600) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(300); outer.UpdateLayout();
                var scroller = Field<ScrollViewer>(page, "_scroller");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"no horizontal scroll overflow at {width}x{height}");
                var viewportLeft = scroller.TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
                var available = scroller.ActualWidth - 2 * gutter;
                var expectedLeft = viewportLeft + gutter + Math.Max(0, available - AdminShellPage.ContentMaxWidth) / 2;
                var bodyLeft = Field<StackPanel>(page, "_body").TransformToVisual(null).TransformPoint(new(0, 0)).X;
                Check(Math.Abs(bodyLeft - expectedLeft) < 1.5, $"settings column starts at the shared frame gutter at {width}x{height}");
                Check(Math.Abs(Field<StackPanel>(page, "_body").ActualWidth - Math.Min(1120, Math.Min(AdminShellPage.ContentMaxWidth, available))) < 1.5, $"settings column fills min(1120, available) at {width}x{height}");
                foreach (var edge in new[] { 8d, scroller.ActualWidth - 18 })
                {
                    var point = scroller.TransformToVisual(null).TransformPoint(new(edge, scroller.ActualHeight / 2));
                    DependencyObject? target = VisualTreeHelper.FindElementsInHostCoordinates(point, outer).FirstOrDefault();
                    while (target != null && target is not ScrollViewer) target = VisualTreeHelper.GetParent(target);
                    Check(ReferenceEquals(target, scroller), $"side gutter has the page scrolling ancestor at {width}x{height}");
                }
                foreach (var row in Descendants(page).OfType<Grid>().Where(g => AutomationProperties.GetAutomationId(g).StartsWith("NetworkAccessHost_", StringComparison.Ordinal)))
                {
                    var origin = row.TransformToVisual(page).TransformPoint(new(0, 0));
                    Check(origin.X >= -1 && origin.X + row.ActualWidth <= page.ActualWidth + 1, $"{AutomationProperties.GetAutomationId(row)} fits the viewport at {width}x{height}");
                    foreach (var button in Descendants(row).OfType<Button>().Where(b => b.Visibility == Visibility.Visible))
                    {
                        var at = button.TransformToVisual(page).TransformPoint(new(0, 0));
                        Check(at.X >= -1 && at.X + button.ActualWidth <= page.ActualWidth + 1 && button.ActualWidth > 60, $"{AutomationProperties.GetAutomationId(button)} stays visible at {width}x{height}");
                    }
                    foreach (var text in Descendants(row).OfType<TextBlock>().Where(t => Visible(t) && t.ActualWidth > 0))
                    {
                        var at = text.TransformToVisual(page).TransformPoint(new(0, 0));
                        Check(at.X + text.ActualWidth <= page.ActualWidth + 1, $"text in {AutomationProperties.GetAutomationId(row)} wraps inside the viewport at {width}x{height}");
                    }
                }
                var flex = Descendants(Row(page, "node:10")).First(d => d.GetType().Name == "NetworkAccessFlexRow");
                var wrapped = (bool)flex.GetType().GetProperty("IsWrapped", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flex)!;
                Program.Log($"TRACE network width={width} longRowWrapped={wrapped} body={Field<StackPanel>(page, "_body").ActualWidth}");
                var layout = (Panel)flex;
                var details = (FrameworkElement)layout.Children[0];
                var actions = (FrameworkElement)layout.Children[1];
                Check(actions.ActualWidth + 24 < layout.ActualWidth, $"host action plus gap fits at {width}x{height}");
                var actionOrigin = actions.TransformToVisual(layout).TransformPoint(new(0, 0));
                Check(!wrapped && actionOrigin.X >= details.ActualWidth + 23 && Math.Abs(actionOrigin.Y) < 1,
                    $"long host details shrink beside the action at {width}x{height}");
                await CaptureAsync(outer, $"network-{width}x{height}.png");
                scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                await CaptureAsync(outer, $"network-{width}x{height}-bottom.png");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(50);
            }
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(250);
            await Until(() => Text(page, "NetworkAccessNoHosts_netbird").Visibility == Visibility.Visible, "netbird recovered on a later poll", 20000);
            Check(Text(page, "NetworkAccessStatusError_netbird").Visibility == Visibility.Collapsed, "a recovered provider replaces its error");

            // Departure: navigating away retires the controller; Back restores a fresh owner.
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => inner.Content is AdminActivityPage, "navigate away");
            Check(page.Controller == null && Field<StackPanel>(page, "_groups").Children.Count == 0, "departure retires the controller and clears rows and links");
            Check(!await ActivateAsync(page, authorize, "tailscale", "node:12", "Enrollment", launched) && launched.IsEmpty, "a retained link from a departed page cannot launch");
            inner.GoBack(); await Until(() => inner.Content is AdminNetworkAccessSettingsPage { Controller.State.CapabilitiesLoaded: true }, "Back restored production page");
            page = (AdminNetworkAccessSettingsPage)inner.Content; page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            await Until(() => page.Controller!.State.Providers.All(p => !p.Loading), "Back loaded statuses");
            Check(Row(page, "node:12") != null, "Back renders the rows for the new owner");

            // No installed providers: the official empty group.
            server.NoProviders = true;
            shell.Navigate(new(AdminRoute.Activity)); await Until(() => inner.Content is AdminActivityPage, "navigate away again");
            inner.GoBack(); await Until(() => inner.Content is AdminNetworkAccessSettingsPage { Controller.State.CapabilitiesLoaded: true }, "Back to empty providers");
            page = (AdminNetworkAccessSettingsPage)inner.Content; page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            await Task.Delay(200);
            Check(Texts(page).Contains("No providers installed") && Text(page, "NetworkAccessNoProviders").Text.StartsWith("No installed plugin provides network access.", StringComparison.Ordinal),
                "no installed providers shows the official empty group");
            await CaptureAsync(outer, "network-no-providers.png");
            server.NoProviders = false;
            shell.Navigate(new(AdminRoute.Activity)); await Until(() => inner.Content is AdminActivityPage, "navigate away for rows");
            inner.GoBack(); await Until(() => inner.Content is AdminNetworkAccessSettingsPage { Controller.State.CapabilitiesLoaded: true }, "Back to providers");
            page = (AdminNetworkAccessSettingsPage)inner.Content; page.Launcher = uri => { launched.Enqueue(uri); return Task.FromResult(true); };
            await Until(() => page.Controller!.State.Providers.All(p => !p.Loading) && page.Controller.State.Providers.Count == 3, "providers again");
            await Task.Delay(150);

            // Capture the actual row callback; retaining a detached link must not redirect to a replacement owner's URL.
            var retainedClick = Field<Delegate>(Row(page, "node:12"), "_link");
            server.CommandServerError = true;
            AdminNetworkAccessCommandFeedback? queuedFeedback = null;
            page.Controller!.CommandFailed += value => queuedFeedback = value;
            await page.Controller.ConnectAsync("tailscale", "api");
            await Until(() => queuedFeedback != null, "old owner failure feedback captured");
            var toast = new SiloPlayer.Controls.ToastContainer();
            var toastService = services.GetRequiredService<ToastService>();
            toastService.Register(toast, page.DispatcherQueue);
            var toastStack = (StackPanel)toast.FindName("ToastStack");
            // Queue the old failure, then replace the admin and reopen before the UI dispatcher delivers it.
            typeof(AdminNetworkAccessSettingsPage).GetMethod("OnCommandFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [queuedFeedback]);
            auth.SetCurrentUser(new UserInfo { Id = "2", Role = "admin", Username = "replacement-admin" });
            typeof(AdminNetworkAccessSettingsPage).GetMethod("OpenController", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            await Until(() => page.Controller?.State.Owner?.AccountId == "2" && page.Controller.State.Providers.Count == 3 && page.Controller.State.Providers.All(p => !p.Loading), "replacement admin fully loaded");
            await Task.Delay(150);
            Check(toastStack.Children.Count == 0, "queued command feedback from a retired owner is not shown in the replacement session");
            var beforeRetained = launched.Count;
            var linkType = typeof(AdminNetworkAccessSettingsPage).Assembly.GetType("SiloPlayer.Views.Admin.NetworkAccessLink")!;
            retainedClick.DynamicInvoke("tailscale", "node:12", Enum.Parse(linkType, "Enrollment"));
            await Task.Delay(150);
            Check(launched.Count == beforeRetained, "a retained row callback cannot launch the replacement admin's enrollment URL");
            await page.Controller!.ConnectAsync("tailscale", "api");
            await Until(() => toastStack.Children.Count > 0, "replacement owner's own feedback still displays");
            Check(true, "current owner command feedback remains visible");
            toastService.Unregister(); server.CommandServerError = false;

            // Authority retirement clears rows and enrollment links; a retained link cannot launch for a replacement owner.
            var retainedLink = AuthorizeLink(Row(page, "node:12"))!;
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" });
            await Task.Delay(300);
            Check(Field<StackPanel>(page, "_groups").Children.Count == 0 && (page.Controller?.State.Providers.Count ?? 0) == 0, "authority retirement clears provider rows");
            Check(!await ActivateAsync(page, retainedLink, "tailscale", "node:12", "Enrollment", launched) && launched.IsEmpty, "a retained enrollment link cannot launch after retirement");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Network access fixture failures: " + string.Join("; ", Failures));
    }

    private static string AdminNetworkAccessSettingsPage_CommandMessage(AdminNetworkAccessCommandFeedback feedback) =>
        (string)typeof(AdminNetworkAccessSettingsPage).GetMethod("CommandMessage", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [feedback])!;

    /// <summary>
    /// Activates a link as a user does: the TextBlock's UI Automation Invoke when WinUI exposes it, otherwise the page's
    /// own click handler with the same identity. Returns whether the fixture launcher was called.
    /// </summary>
    private static async Task<bool> ActivateAsync(AdminNetworkAccessSettingsPage page, Hyperlink? link, string provider, string hostId, string kind, ConcurrentQueue<Uri> launched)
    {
        var before = launched.Count;
        var invoked = false;
        TextBlock? host = null;
        // A link whose row was cleared is detached; it can only be reached through the retained handler.
        try { host = link?.ElementStart?.Parent as TextBlock; } catch (System.Runtime.InteropServices.COMException) { }
        if (host != null && FrameworkElementAutomationPeer.FromElement(host) is { } peer)
        {
            foreach (var child in peer.GetChildren() ?? [])
                if (child.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke && child.GetName() == AutomationProperties.GetName(link!)) { invoke.Invoke(); invoked = true; break; }
        }
        if (!invoked)
        {
            var linkType = typeof(AdminNetworkAccessSettingsPage).Assembly.GetType("SiloPlayer.Views.Admin.NetworkAccessLink")!;
            var handler = typeof(AdminNetworkAccessSettingsPage).GetMethod("OnLink", BindingFlags.Instance | BindingFlags.NonPublic)!;
            handler.Invoke(page, handler.GetParameters().Length == 4
                ? [page.Controller?.State.Owner, provider, hostId, Enum.Parse(linkType, kind)]
                : [provider, hostId, Enum.Parse(linkType, kind)]);
        }
        Program.Log("TRACE link activation path=" + (invoked ? "automation" : "page handler"));
        await Task.Delay(150);
        return launched.Count > before;
    }

    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static bool Visible(TextBlock text) { for (DependencyObject? e = text; e != null; e = VisualTreeHelper.GetParent(e)) if (e is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToArray();
    private static string Plain(TextBlock text) => text.Inlines.Count == 0 ? text.Text : string.Concat(text.Inlines.Select(InlineText));
    private static string InlineText(Inline inline) => inline switch { Run run => run.Text, Span span => string.Concat(span.Inlines.Select(InlineText)), _ => "" };
    private static TextBlock? ReachText(DependencyObject row) => Descendants(row).OfType<TextBlock>().FirstOrDefault(t => Visible(t) && Plain(t).StartsWith("Clients reach this host at ", StringComparison.Ordinal));
    private static Hyperlink? AuthorizeLink(DependencyObject row) => Descendants(row).OfType<TextBlock>().Where(Visible)
        .SelectMany(t => t.Inlines.OfType<Hyperlink>()).FirstOrDefault(link => AutomationProperties.GetName(link) == "Open the authorization page");
    private static Grid Row(DependencyObject page, string id) => Descendants(page).OfType<Grid>().Single(g => AutomationProperties.GetAutomationId(g) == "NetworkAccessHost_tailscale_" + id);
    private static TextBlock Text(DependencyObject page, string id) => Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == id);
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
