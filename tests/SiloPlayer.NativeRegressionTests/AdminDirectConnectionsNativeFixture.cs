using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
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
/// Production Configure › Network access, Direct connections section, with synthetic data only (host names such as
/// stun.fixture.invalid): its existence rule, every readiness state, Run check (refused, unknown, stale), the advanced
/// settings with the STUN list, save and discard, a failed read with its reload, and the layout at six page widths.
/// </summary>
internal static class AdminDirectConnectionsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + "direct connections: " + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        bool Met() { try { return condition(); } catch (Exception error) when (error is InvalidOperationException or NullReferenceException) { return false; } }
        for (var i = 0; i < milliseconds / 25 && !Met(); i++) await Task.Delay(25);
        if (!Met()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Requests = new();
        internal readonly ConcurrentQueue<string> Bodies = new();
        internal string Enabled = "false", Port = "true", Stun = "stun.fixture.invalid:3478";
        internal int Etag = 1;
        internal bool SettingsFail, StatusFails;
        internal string[] Managed = [];
        internal string Put = "ok", Refresh = "ok", Marker = "first";
        internal TaskCompletionSource? StatusGate, RefreshGate;
        internal int Count(string request) => Requests.Count(r => r == request);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue($"{request.Method} {request.RequestUri.PathAndQuery}");
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Content != null) Bodies.Enqueue(body);
            switch (path)
            {
                case "/api/v2/network-access/capabilities": return Json(new { revision = "fixture", state = "not_configured", allowed = false, providers = Array.Empty<object>() });
                case "/api/v2/admin/settings/effective":
                {
                    if (SettingsFail) return Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                    var response = Json(new Dictionary<string, string> { ["direct_connections.enabled"] = Enabled, ["direct_connections.port_mapping"] = Port, ["direct_connections.stun_servers"] = Stun });
                    response.Headers.ETag = new($"\"fixture-{Etag}\"");
                    return response;
                }
                case "/api/v2/admin/settings/sensitive-status":
                    return Json(new { configured = Array.Empty<string>(), managed_by_env = Managed, managed_by_env_vars = Managed.ToDictionary(key => key, key => "SILO_FIXTURE_" + key.Split('.').Last().ToUpperInvariant()) });
                case "/api/v2/admin/settings/restart-keys": return Json(new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() });
                case "/api/v2/admin/settings" when request.Method == HttpMethod.Put:
                {
                    switch (Put)
                    {
                        case "refuse": return Problem(HttpStatusCode.BadRequest, "validation_failed", "direct_connections.stun_servers requires host:port entries: address fixture: missing port in address");
                        case "unknown": return Json(new { title = "Gateway timeout", status = 504 }, HttpStatusCode.GatewayTimeout);
                        case "conflict": return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                    }
                    using var document = JsonDocument.Parse(body);
                    var values = document.RootElement.GetProperty("values");
                    foreach (var value in values.EnumerateObject())
                    {
                        if (value.Name.EndsWith("enabled")) Enabled = value.Value.GetString()!;
                        else if (value.Name.EndsWith("port_mapping")) Port = value.Value.GetString()!;
                        else Stun = value.Value.GetString()!.Trim().ToLowerInvariant();
                    }
                    Etag++;
                    var canonical = new Dictionary<string, string>();
                    foreach (var value in values.EnumerateObject()) canonical[value.Name] = value.Name.EndsWith("enabled") ? Enabled : value.Name.EndsWith("port_mapping") ? Port : Stun;
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { values = canonical, restart_required = false }), Encoding.UTF8, "application/json") };
                    response.Headers.ETag = new($"\"fixture-{Etag}\"");
                    return response;
                }
                case "/api/v2/admin/direct-connections/status":
                    if (request.RequestUri.Query.Contains("refresh=true"))
                    {
                        if (RefreshGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                        if (Refresh == "refuse") return Problem(HttpStatusCode.Forbidden, "permission_denied", "Only the acting administrator can run a direct connection check.");
                        if (Refresh == "unknown") return Json(new { title = "Gateway timeout", status = 504 }, HttpStatusCode.GatewayTimeout);
                        return Json(Status("checked"));
                    }
                    if (StatusGate is { } statusGate) await statusGate.Task.WaitAsync(cancellationToken);
                    return StatusFails ? Json(new { title = "Unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable) : Json(Status(Marker));
                case "/api/v2/admin/server/status": return Json(new { restart_required = false, restart_mark_count = 0, restart_requested = false });
                case "/api/v2/admin/system/build": return Json(new { display = "synthetic", available = true, build_number = 1 });
                default: return Json(new { });
            }
        }

        /// <summary>Every state the section words: features, listeners and tuning, with text that must wrap at the narrowest width.</summary>
        private static object Status(string marker)
        {
            object F(string feature, string state, string? reason = null, string? requested = null, string? granted = null, string? detail = null) => new Dictionary<string, object?>
            {
                ["feature"] = feature, ["kind"] = "local", ["state"] = state, ["reason"] = reason, ["requested"] = requested, ["granted"] = granted, ["detail"] = detail,
            };
            object L(string name, string requested, string granted, string state, string reason, string? errno, object buffer) =>
                new { listener = name, requested, granted, state, reason, errno, send_buffer = buffer };
            return new
            {
                enabled = true,
                hosts = new[]
                {
                    new
                    {
                        host = new { id = "api", name = "API", role = "api" },
                        checked_at = marker == "checked" ? "2026-11-22T10:05:00.000Z" : "2026-11-22T10:00:00.000Z",
                        features = new[]
                        {
                            F("direct", marker == "checked" ? "degraded" : "on"),
                            F("engine", "off", "replica_lease_held"),
                            F("nat_mapping", "on", detail: "192.0.2.10:40000"),
                            F("nat_hairpin", "unknown", "not_measured"),
                            F("ipv4", "on", detail: "192.0.2.10"),
                            F("ipv6", "off", "not_available"),
                            F("port_mapping", "on", detail: "upnp,pmp"),
                            F("reachability", "unknown", "not_measured", detail: "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff"),
                            F("socket_receive_buffer", "capped", "limit", "33554432", "212992"),
                            F("socket_send_buffer", "capped", "no_cap_net_admin", "33554432", "212992"),
                            F("gso", "on"),
                            F("gro", "off", "kernel_unsupported"),
                            F("pmtu_fallback", "fallback", "granted_below_request", detail: "a long detail from the server " + new string('x', 160)),
                            F("cap_net_admin", "off", "no_cap_net_admin"),
                        },
                        listeners = new[]
                        {
                            L("api", "bbr", "bbr", "on", "", null, new { state = "on", reason = "", requested = 33554432L, granted = 33554432L }),
                            L("jellyfin", "bbr", "off", "fallback", "module_missing", "ENOENT", new { state = "capped", reason = "no_net_admin", requested = 33554432L, granted = 212992L, errno = "EPERM" }),
                            L("abs", "off", "off", "off", "disabled", null, new { state = "off", reason = "not_needed", requested = 0L, granted = 0L }),
                            L("proxy", "bbr", "bbr", "on", "", null, new { state = "on", reason = "", requested = 0L, granted = 0L }),
                        },
                        knobs = new object[]
                        {
                            new { env = "SILO_NET_TCP_CONGESTION", @default = "bbr", effective = "bbr", allowed = new[] { "bbr", "off" } },
                            new { env = "SILO_NET_TCP_SEND_BUFFER", @default = "33554432", effective = "off", requested = "lots", reason = "invalid" },
                            new { env = "SILO_NET_DIRECT_UDP_PORT", @default = "8094", effective = "8094", min = "1", max = "65535" },
                            new { env = "SILO_NET_DIRECT_GSO", @default = "on", effective = "off", allowed = new[] { "on", "off" } },
                            new { env = "SILO_NET_DIRECT_BBR_LOSS", @default = "both", effective = "off", allowed = new[] { "both", "no_lower", "loss_thresh", "off" } },
                            new { env = "SILO_NET_DIRECT_RECEIVE_WINDOW", @default = "128", effective = "4", requested = "2", min = "4", max = "128", reason = "clamped" },
                            new { env = "SILO_NET_DIRECT_ACK_DECIMATION", @default = "on", effective = "on" },
                            new { env = "SILO_NET_FUTURE_KNOB_WITH_A_VERY_LONG_NAME_THAT_MUST_WRAP_INSIDE_THE_ROW", @default = "a", effective = "a" },
                        },
                    },
                },
            };
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail }), Encoding.UTF8, "application/problem+json") };
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
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
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new LoggingDisclosurePreferences(Path.Combine(Program.ResultDirectory, "disclosure")));
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            if (AdminShellPage.TryOpenHref("/admin/settings/network-access") is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Network access is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Network access did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content is not AdminNetworkAccessSettingsPage; i++) await Task.Delay(25);
            if (inner.Content is not AdminNetworkAccessSettingsPage page) throw new InvalidOperationException("Network access did not activate its production WinUI Page.");

            async Task<AdminNetworkAccessSettingsPage> Reopen()
            {
                shell.Navigate(new(AdminRoute.Activity));
                await Until(() => inner.Content is AdminActivityPage, "navigate away");
                inner.GoBack();
                await Until(() => inner.Content is AdminNetworkAccessSettingsPage { DirectController.State.Loading: false }, "Back to Network access");
                var reopened = (AdminNetworkAccessSettingsPage)inner.Content;
                await Task.Delay(150);
                return reopened;
            }
            AdminDirectConnectionsController Controller() => page.DirectController!;
            void Poll() => typeof(AdminDirectConnectionsController).GetMethod("OnPoll", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Controller(), null);

            // ── Existence: saved off and nothing staged ──────────────────
            await Until(() => Controller().State.Loaded, "settings loaded");
            await Task.Delay(200);
            Check(!Present(page, "NetworkAccessDirectGroup"), "the section does not exist while saved off and nothing is staged");
            Check(server.Count("GET /api/v2/admin/direct-connections/status") == 0, "no status request is made while the section does not exist");
            Check(Controller().State.Polling, "the page is polling-ready but reads nothing");
            await CaptureAsync(outer, "direct-absent-1440.png");

            // ── Loading, then the full status ────────────────────────────
            server.Enabled = "true"; server.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            page = await Reopen();
            await Until(() => Present(page, "NetworkAccessDirectGroup") && Descendants(page).OfType<StackPanel>().Any(p => AutomationProperties.GetName(p) == "Loading direct connection status" && p.Visibility == Visibility.Visible), "status skeleton");
            Check(Texts(page).Contains("Direct connections") && Texts(page).Contains("Check this server’s readiness for direct connections."), "group title and description are the official text");
            Check(Texts(page).Contains("Readiness checks do not start direct playback.") && Button(page, "NetworkAccessDirectRunCheck").IsEnabled, "the readiness note and an enabled Run check show while loading");
            await CaptureAsync(outer, "direct-loading-1440.png");
            server.StatusGate.SetResult(); server.StatusGate = null;
            await Until(() => Controller().State.Status != null, "status shown");
            await Task.Delay(200);
            var rendered = Texts(page);
            Check(rendered.Contains("API server") && rendered.Any(t => t.StartsWith("Last checked ", StringComparison.Ordinal)), "the host shows its heading and last-checked time");
            Check(Value(page, "Feature_direct") == "On" && Value(page, "Feature_ipv4") == "On" && Value(page, "Feature_ipv6") == "Off" && Value(page, "Feature_nat_hairpin") == "Unknown" &&
                  Value(page, "Feature_socket_receive_buffer") == "Capped" && Value(page, "Feature_pmtu_fallback") == "Fallback", "each feature reads its state in plain words");
            Check(Lines(page, "Feature_ipv6").SequenceEqual(["Not available"]) && Lines(page, "Feature_nat_hairpin").SequenceEqual(["Not measured"]) && Lines(page, "Feature_gro").SequenceEqual(["Not supported by this kernel"]), "reasons read in plain words");
            Check(Lines(page, "Feature_nat_mapping").SequenceEqual(["Observed public address: 192.0.2.10:40000"]) && Lines(page, "Feature_port_mapping").SequenceEqual(["UPnP, NAT-PMP"]), "detail lines read as the official section words them");
            Check(Lines(page, "Feature_socket_receive_buffer").SequenceEqual(["Host buffer limit reached", "Requested: 32.0 MB; granted: 208.0 KB"]), "requested and granted bytes show as sizes");
            Check(Value(page, "Feature_cap_net_admin") == "Off" && Lines(page, "Feature_cap_net_admin").Count == 0 && Lines(page, "Feature_socket_send_buffer").Contains("NET_ADMIN capability missing"),
                "the NET_ADMIN row stays Off when a buffer needed it and the reason sits on the buffer");
            Check(Texts(page).Contains("Listeners") && Texts(page).Contains("API") && Texts(page).Contains("Jellyfin") && Texts(page).Contains("Audiobookshelf") && Texts(page).Contains("Proxy"), "listeners appear by name");
            Check(Value(page, "Listener_api") == "Congestion control: On" && Value(page, "Listener_jellyfin") == "Congestion control: Fallback" && Value(page, "Listener_abs") == "Congestion control: Off", "listener congestion control states");
            Check(Lines(page, "Listener_jellyfin").SequenceEqual(["Algorithm: requested BBR; active Off", "BBR is not available (ENOENT)", "Send buffer: Capped", "Requested: 32.0 MB; granted: 208.0 KB", "NET_ADMIN capability missing (EPERM)"]), "a listener shows algorithm, reason with errno and its send buffer");
            Check(Lines(page, "Listener_abs").SequenceEqual(["Disabled", "Send buffer: Not needed"]), "a send buffer that needs no increase reads Not needed");
            Check(Texts(page).Contains("Host tuning") && Texts(page).Contains("TCP congestion control") && Texts(page).Contains("TCP send buffer") && Texts(page).Contains("SILO_NET_TCP_CONGESTION"), "host tuning labels carry the variable name underneath");
            Check(Value(page, "Knob_SILO_NET_TCP_CONGESTION") == "Effective: BBR" && Lines(page, "Knob_SILO_NET_TCP_CONGESTION").SequenceEqual(["Default: BBR", "Allowed: BBR, Off"]), "an algorithm reads in capitals");
            Check(Value(page, "Knob_SILO_NET_TCP_SEND_BUFFER") == "Effective: Off" && Lines(page, "Knob_SILO_NET_TCP_SEND_BUFFER").SequenceEqual(["Default: 32.0 MB", "Requested: lots", "Invalid value; using off"]), "off reads Off, a size reads as a size, an unparsed request stays as sent");
            Check(Texts(page).Contains("SILO_NET_FUTURE_KNOB_WITH_A_VERY_LONG_NAME_THAT_MUST_WRAP_INSIDE_THE_ROW"), "a variable with no plain name shows by its name alone");
            Check(!Texts(page).Any(t => t.StartsWith("web.", StringComparison.Ordinal) || t.StartsWith("configure.", StringComparison.Ordinal)), "no catalog key shows as text");
            Check(!Descendants(page).OfType<TextBlock>().Where(Visible).Any(t => t.FlowDirection != FlowDirection.LeftToRight && Descendants(page).OfType<Grid>().Any(g => AutomationProperties.GetAutomationId(g).StartsWith("NetworkAccessDirect", StringComparison.Ordinal) && IsInside(t, g))),
                "server text is shown left to right");
            Check(server.Count("GET /api/v2/admin/direct-connections/status") == 1 && server.Count("GET /api/v2/admin/direct-connections/status?refresh=true") == 0, "opening reads the status once, without a refresh");
            Check(CornerShapeChecks.OvalCorners(page).Count == 0, "no element of the loaded section has oval corners: " + string.Join("; ", CornerShapeChecks.OvalCorners(page)));
            await CaptureAsync(outer, "direct-loaded-1440.png");
            Check(Value(page, "Feature_engine") == "Off" && Lines(page, "Feature_engine").SequenceEqual(["Another API server hosts direct connections"]), "the supplied engine stays visible when another API holds the lease");
            Check(Lines(page, "Knob_SILO_NET_DIRECT_UDP_PORT").Contains("Range: 1 to 65535") &&
                Lines(page, "Knob_SILO_NET_DIRECT_RECEIVE_WINDOW").Contains("Range: 4.0 MB to 128.0 MB"), "UDP and receive-window bounds are formatted from the wire response");
            foreach (var width in new[] { 1440, 720 })
            {
                window.AppWindow.ResizeClient(new(width, 900));
                await Task.Delay(200);
                DiagnosticRow(page, "Feature_engine").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .2 });
                await Task.Delay(250);
                await CaptureAsync(outer, $"direct-engine-{width}.png");
                DiagnosticRow(page, "Knob_SILO_NET_DIRECT_BBR_LOSS").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .45 });
                await Task.Delay(250);
                await CaptureAsync(outer, $"direct-knobs-{width}.png");
            }
            window.AppWindow.ResizeClient(new(1440, 900));
            Field<ScrollViewer>(page, "_scroller").ChangeView(null, 0, null, true);

            // ── Polling: a failed poll keeps the data, a later one recovers ───
            server.StatusFails = true;
            Poll();
            await Until(() => Present(page, "NetworkAccessDirectStatusError"), "failed poll shows");
            Check(Value(page, "Feature_direct") == "On" && Button(page, "NetworkAccessDirectRetry").IsEnabled, "a failed poll keeps the data on screen and offers Retry");
            Check(Text(page, "NetworkAccessDirectStatusError").Text == "Could not refresh direct connection status. The last result is shown.", "the failed poll says the last result is shown");
            await CaptureAsync(outer, "direct-poll-failed-1440.png");
            server.StatusFails = false; server.Marker = "second";
            Invoke(Button(page, "NetworkAccessDirectRetry"));
            await Until(() => !Present(page, "NetworkAccessDirectStatusError"), "retry recovers");
            Check(Controller().State.Polling, "polling continues after a failure");

            // ── Run check: running, refused, unknown, stale ──────────────
            server.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = Button(page, "NetworkAccessDirectRunCheck");
            Invoke(run);
            await Until(() => !run.IsEnabled, "Run check busy");
            Check(Descendants(run).OfType<Viewbox>().Any(), "a running check shows the spinner");
            Check(server.Count("GET /api/v2/admin/direct-connections/status?refresh=true") == 1, "Run check requests the refresh once");
            await CaptureAsync(outer, "direct-checking-1440.png");
            server.RefreshGate.SetResult(); server.RefreshGate = null;
            await Until(() => run.IsEnabled && Value(page, "Feature_direct") == "Degraded", "check result replaces the status");
            Check(!Descendants(run).OfType<Viewbox>().Any() && Texts(page).Any(t => t.StartsWith("Last checked ", StringComparison.Ordinal)), "the spinner leaves and the new result shows");
            server.Refresh = "refuse";
            Invoke(run);
            await Until(() => Text(page, "NetworkAccessDirectCheckMessage").Visibility == Visibility.Visible, "refused check message");
            Check(Text(page, "NetworkAccessDirectCheckMessage").Text == "Only the acting administrator can run a direct connection check." && Value(page, "Feature_direct") == "Degraded", "a refused check shows the server's detail and keeps the status");
            await CaptureAsync(outer, "direct-check-refused-1440.png");
            server.Refresh = "unknown";
            var refreshes = server.Count("GET /api/v2/admin/direct-connections/status?refresh=true");
            var reads = server.Count("GET /api/v2/admin/direct-connections/status");
            Invoke(run);
            await Until(() => Text(page, "NetworkAccessDirectCheckMessage").Text.StartsWith("The check request did not finish", StringComparison.Ordinal), "unknown check message");
            await Until(() => server.Count("GET /api/v2/admin/direct-connections/status") > reads, "the state is read again");
            Check(server.Count("GET /api/v2/admin/direct-connections/status?refresh=true") == refreshes + 1, "an unknown outcome is not sent again");
            await CaptureAsync(outer, "direct-check-unknown-1440.png");
            server.Refresh = "ok";
            // A stale answer: the check's answer arrives after the page left.
            server.RefreshGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(run);
            await Until(() => !run.IsEnabled, "stale check running");
            var staleController = Controller();
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => inner.Content is AdminActivityPage, "navigate away during a check");
            server.RefreshGate.SetResult(); server.RefreshGate = null;
            await Task.Delay(200);
            Check(staleController.State.Status == null && !staleController.State.Checking, "a check answered after the page left publishes nothing");
            inner.GoBack();
            await Until(() => inner.Content is AdminNetworkAccessSettingsPage { DirectController.State.Status: not null }, "Back restored the section");
            page = (AdminNetworkAccessSettingsPage)inner.Content;
            await Task.Delay(150);

            // ── Settings: advanced disclosure, toggles, STUN editor ──────
            var advanced = Button(page, "NetworkAccessDirectAdvanced");
            Check(AutomationProperties.GetName(advanced) == "Advanced · 2 settings" && AutomationProperties.GetItemStatus(advanced) == "collapsed", "the advanced row is collapsed and names its two settings");
            Check(!Present(page, "NetworkAccessDirectStun") || !Visible(TextBox(page, "NetworkAccessDirectStun")), "collapsed advanced hides its settings");
            Check(Toggle(page, "NetworkAccessDirectEnabled").IsOn, "the enable switch reflects the saved value");
            Invoke(advanced);
            await Task.Delay(150);
            Check(Visible(TextBox(page, "NetworkAccessDirectStun")) && AutomationProperties.GetItemStatus(advanced) == "expanded", "opening advanced shows Port mapping and STUN servers");
            Check(Texts(page).Contains("Port mapping") && Texts(page).Contains("Check the router for UPnP, NAT-PMP, or PCP support.") && Texts(page).Contains("STUN servers") &&
                  Texts(page).Contains("A comma-separated list of up to eight host:port entries. Leave it empty to use the defaults."), "advanced settings use the official labels and descriptions");
            Check(TextBox(page, "NetworkAccessDirectStun").Text == "stun.fixture.invalid:3478" && Toggle(page, "NetworkAccessDirectPortMapping").IsOn, "the fields start from the saved values");
            await CaptureAsync(outer, "direct-advanced-1440.png");
            Invoke(advanced); await Task.Delay(100);
            Check(!Visible(TextBox(page, "NetworkAccessDirectStun")), "advanced collapses again");

            Toggle(page, "NetworkAccessDirectPortMapping").IsOn = false;
            await Until(() => Controller().State.DirtyKeys.Count == 1, "port mapping staged");
            await Task.Delay(250);
            Check(Visible(TextBox(page, "NetworkAccessDirectStun")) && AutomationProperties.GetItemStatus(advanced) == "expanded", "a staged advanced setting forces the section open");
            Check(Button(page, "NetworkAccessSave").IsEnabled && Texts(page).Any(t => t.StartsWith("1 unsaved change", StringComparison.Ordinal)), "the save bar counts the staged change and offers Save");
            TextBox(page, "NetworkAccessDirectStun").Text = "stun.fixture.invalid";
            await Until(() => !Controller().State.Stun.IsValid, "invalid STUN draft");
            await Task.Delay(100);
            Check(Text(page, "NetworkAccessDirectStunError").Visibility == Visibility.Visible && Text(page, "NetworkAccessDirectStunError").Text.StartsWith("direct_connections.stun_servers requires host:port entries", StringComparison.Ordinal),
                "an invalid STUN list shows the server's message under the field");
            Check(!Button(page, "NetworkAccessSave").IsEnabled && TextBox(page, "NetworkAccessDirectStun").Text == "stun.fixture.invalid", "an invalid STUN list blocks Save and keeps the typed text");
            { var s = Field<ScrollViewer>(page, "_scroller"); s.ChangeView(null, s.ScrollableHeight, null, true); await Task.Delay(200); await CaptureAsync(outer, "direct-stun-invalid-1440.png"); s.ChangeView(null, 0, null, true); await Task.Delay(100); }
            TextBox(page, "NetworkAccessDirectStun").Text = string.Join(",", Enumerable.Range(1, 9).Select(i => $"s{i}.fixture.invalid:3478"));
            await Until(() => Controller().State.Stun.Problem == AdminStunProblem.TooMany, "too many entries");
            Check(Text(page, "NetworkAccessDirectStunError").Text == "direct_connections.stun_servers accepts at most 8 entries", "nine entries exceed the limit of eight");
            TextBox(page, "NetworkAccessDirectStun").Text = string.Join(",", Enumerable.Range(1, 8).Select(i => $"s{i}.fixture.invalid:3478"));
            await Until(() => Controller().State.Stun.IsValid, "eight entries valid");
            Check(Text(page, "NetworkAccessDirectStunError").Visibility == Visibility.Collapsed && Button(page, "NetworkAccessSave").IsEnabled, "eight entries are accepted");

            Invoke(Button(page, "NetworkAccessDiscard"));
            await Until(() => Controller().State.DirtyKeys.Count == 0, "discard");
            await Task.Delay(250);
            Check(TextBox(page, "NetworkAccessDirectStun").Text == "stun.fixture.invalid:3478" && Toggle(page, "NetworkAccessDirectPortMapping").IsOn, "Discard restores the saved values");

            // After Discard the forced-open reason is gone; open the disclosure as a person would before editing inside it.
            if (AutomationProperties.GetItemStatus(Button(page, "NetworkAccessDirectAdvanced")) != "expanded") { Invoke(Button(page, "NetworkAccessDirectAdvanced")); await Task.Delay(150); }
            // Save sends only the staged keys.
            Toggle(page, "NetworkAccessDirectPortMapping").IsOn = false;
            await Until(() => Controller().State.DirtyKeys.Count == 1, "port mapping staged again"); await Task.Delay(200);
            TextBox(page, "NetworkAccessDirectStun").Text = " STUN2.fixture.invalid:3478 ";
            await Until(() => Controller().State.DirtyKeys.Count == 2, "two staged");
            Invoke(Button(page, "NetworkAccessSave"));
            await Until(() => Controller().State.DirtyKeys.Count == 0 && !Controller().State.Saving, "saved");
            await Task.Delay(250);
            var sent = JsonDocument.Parse(server.Bodies.Last()).RootElement.GetProperty("values");
            Check(sent.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(["direct_connections.port_mapping", "direct_connections.stun_servers"]) &&
                  sent.GetProperty("direct_connections.port_mapping").GetString() == "false" && sent.GetProperty("direct_connections.stun_servers").GetString() == " STUN2.fixture.invalid:3478 ",
                "Save sends exactly the two staged keys, as typed");
            Check(TextBox(page, "NetworkAccessDirectStun").Text == "stun2.fixture.invalid:3478", "the field shows the server's canonical list after Save");

            // Refused, unknown and stale saves.
            server.Put = "refuse";
            TextBox(page, "NetworkAccessDirectStun").Text = "x";
            await Until(() => Controller().State.Stun.IsValid == false, "invalid before refusal");
            TextBox(page, "NetworkAccessDirectStun").Text = "stun3.fixture.invalid:3478";
            await Until(() => Controller().State.CanSave, "can save");
            Invoke(Button(page, "NetworkAccessSave"));
            await Until(() => Present(page, "NetworkAccessDirectSaveError"), "refusal shown"); await Task.Delay(250);
            Check(Text(page, "NetworkAccessDirectSaveError").Text.StartsWith("direct_connections.stun_servers requires host:port entries", StringComparison.Ordinal) &&
                  TextBox(page, "NetworkAccessDirectStun").Text == "stun3.fixture.invalid:3478" && Button(page, "NetworkAccessSave").IsEnabled,
                "a refused save shows the server's message and keeps the draft ready to correct and retry");
            { var s = Field<ScrollViewer>(page, "_scroller"); s.ChangeView(null, s.ScrollableHeight, null, true); await Task.Delay(200); await CaptureAsync(outer, "direct-save-refused-1440.png"); s.ChangeView(null, 0, null, true); await Task.Delay(100); }
            server.Put = "unknown";
            Invoke(Button(page, "NetworkAccessSave"));
            await Until(() => Controller().State.OutcomeUnknown, "unknown save"); await Task.Delay(250);
            Check(Text(page, "NetworkAccessDirectSaveError").Text == "The save outcome is unknown. Reload and review before saving again." && !Button(page, "NetworkAccessSave").IsEnabled && Button(page, "NetworkAccessDirectSaveReload").IsEnabled,
                "an unknown save outcome blocks further writes and offers Reload");
            var writes = server.Count("PUT /api/v2/admin/settings");
            { var s = Field<ScrollViewer>(page, "_scroller"); s.ChangeView(null, s.ScrollableHeight, null, true); await Task.Delay(200); await CaptureAsync(outer, "direct-save-unknown-1440.png"); s.ChangeView(null, 0, null, true); await Task.Delay(100); }
            server.Put = "ok";
            Invoke(Button(page, "NetworkAccessDirectSaveReload"));
            await Until(() => !Controller().State.OutcomeUnknown && !Controller().State.Loading, "reloaded"); await Task.Delay(250);
            Check(server.Count("PUT /api/v2/admin/settings") == writes && Controller().State.DirtyKeys.Count == 1, "Reload does not write and keeps the draft");
            server.Put = "conflict";
            Invoke(Button(page, "NetworkAccessSave"));
            await Until(() => Controller().State.Conflict, "conflict"); await Task.Delay(250);
            Check(Text(page, "NetworkAccessDirectSaveError").Text == "Settings changed on the server. Reload and review your changes.", "a stale validator reports a conflict");
            server.Put = "ok";
            Invoke(Button(page, "NetworkAccessDiscard"));
            await Until(() => Controller().State.DirtyKeys.Count == 0, "discard after conflict"); await Task.Delay(250);
            Invoke(Button(page, "NetworkAccessDirectSaveReload"));
            await Until(() => !Controller().State.Conflict, "conflict cleared by reload");

            // Environment-managed settings are read-only with their note and are never sent.
            server.Managed = ["direct_connections.enabled"];
            page = await Reopen();
            await Until(() => Controller().State.Status != null, "section after environment change");
            Check(!Toggle(page, "NetworkAccessDirectEnabled").IsEnabled && Texts(page).Contains("Set by SILO_FIXTURE_ENABLED in the server’s environment."), "a setting the environment sets is read-only and says which variable sets it");
            server.Managed = [];

            // Saving off removes the section and stops the reads.
            page = await Reopen();
            await Until(() => Controller().State.Status != null, "section before switching off");
            Toggle(page, "NetworkAccessDirectEnabled").IsOn = false;
            await Until(() => Controller().State.DirtyKeys.Count == 1, "enabled staged off"); await Task.Delay(250);
            Check(Present(page, "NetworkAccessDirectGroup") && Visible(Button(page, "NetworkAccessDirectRunCheck")), "the section stays while the unsaved draft exists");
            Invoke(Button(page, "NetworkAccessSave"));
            await Until(() => !Controller().State.SectionVisible, "section removed");
            var after = server.Count("GET /api/v2/admin/direct-connections/status");
            await Task.Delay(300);
            Check(!Visible(Descendants(page).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "NetworkAccessDirectGroup")) && server.Count("GET /api/v2/admin/direct-connections/status") == after,
                "saving the switch off removes the section and stops reading");

            // ── A failed FIRST settings read shows nothing, as official: the feature is hidden until an administrator enables it ──
            server.SettingsFail = true;
            page = await Reopen();
            await Until(() => Controller().State.LoadFailure != null, "settings failure");
            await Task.Delay(200);
            Check(!Present(page, "NetworkAccessDirectLoadError") && !Present(page, "NetworkAccessDirectGroup"),
                "a failed first settings read shows neither the section nor an error naming a feature the admin may never have turned on");

            // ── A failed re-read, with the section on screen, is visible and has a reload ───────
            server.SettingsFail = false; server.Enabled = "true";
            page = await Reopen();
            await Until(() => Controller().State.Status != null && Controller().State.LoadFailure == null && Present(page, "NetworkAccessDirectGroup"), "section shown");
            server.SettingsFail = true;
            await Controller().ReloadAsync(preserveDraft: true);
            await Until(() => Controller().State.LoadFailure != null && Present(page, "NetworkAccessDirectLoadError"), "re-read failure shown");
            Check(Text(page, "NetworkAccessDirectLoadError").Text == "Could not read the direct connection settings.", "a failed re-read of the settings shows its error");
            server.SettingsFail = false;
            Invoke(Button(page, "NetworkAccessDirectLoadReload"));
            await Until(() => Controller().State.Status != null && Controller().State.LoadFailure == null, "reload recovers");
            Check(!Present(page, "NetworkAccessDirectLoadError") && Present(page, "NetworkAccessDirectGroup"), "Reload brings the section back");

            // ── Layout at the six widths ─────────────────────────────────
            Invoke(Button(page, "NetworkAccessDirectAdvanced"));
            if (AutomationProperties.GetItemStatus(Button(page, "NetworkAccessDirectAdvanced")) != "expanded") Invoke(Button(page, "NetworkAccessDirectAdvanced"));
            foreach (var (width, height) in new[] { (1440, 900), (1280, 800), (1024, 768), (800, 600), (640, 800), (560, 800) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(350); outer.UpdateLayout();
                var scroller = Field<ScrollViewer>(page, "_scroller");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"no horizontal scroll overflow at {width}");
                var group = Descendants(page).OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "NetworkAccessDirectGroup");
                var left = group.TransformToVisual(page).TransformPoint(new(0, 0)).X;
                var pageWidth = page.ActualWidth;
                foreach (var text in Descendants(group).OfType<TextBlock>().Where(Visible).Where(t => t.ActualWidth > 0))
                {
                    var at = text.TransformToVisual(page).TransformPoint(new(0, 0));
                    var clipped = at.X < -1 || at.X + text.ActualWidth > pageWidth + 1;
                    if (clipped) Check(false, $"text '{Trim(text.Text)}' stays inside the page at {width} ({at.X:0}..{at.X + text.ActualWidth:0} of {pageWidth:0})");
                }
                foreach (var control in Descendants(group).OfType<Control>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0))
                {
                    var at = control.TransformToVisual(page).TransformPoint(new(0, 0));
                    if (at.X < -1 || at.X + control.ActualWidth > pageWidth + 1) Check(false, $"{control.GetType().Name} '{AutomationProperties.GetAutomationId(control)}' stays inside the page at {width}");
                }
                Check(true, $"every text and control of the section is inside the page at {width}");
                var runButton = Button(page, "NetworkAccessDirectRunCheck");
                Check(runButton.ActualWidth > 60 && runButton.ActualHeight == 32, $"Run check keeps its 32 DIP height and its label at {width}");
                foreach (var row in Descendants(group).OfType<Grid>().Where(g => AutomationProperties.GetAutomationId(g).StartsWith("NetworkAccessDirectFeature_", StringComparison.Ordinal) || AutomationProperties.GetAutomationId(g).StartsWith("NetworkAccessDirectKnob_", StringComparison.Ordinal) || AutomationProperties.GetAutomationId(g).StartsWith("NetworkAccessDirectListener_", StringComparison.Ordinal)))
                {
                    var value = Descendants(row).OfType<TextBlock>().First(t => Visible(t) && t.TextAlignment == TextAlignment.Right && t.Text.Length > 0);
                    Check(value.ActualHeight < 60 || value.Text.Length > 30, $"{AutomationProperties.GetAutomationId(row)} keeps its state on the label line at {width}");
                    var labelBox = row.Children.OfType<StackPanel>().First();
                    Check(labelBox.ActualWidth >= 40 || labelBox.Children.Count == 0, $"{AutomationProperties.GetAutomationId(row)} label is not squeezed at {width}");
                    if (row.Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().FirstOrDefault(t => t.FontFamily?.Source == "Consolas") is { } variable)
                    {
                        var probe = new TextBlock { Text = variable.Text, FontFamily = variable.FontFamily, FontSize = variable.FontSize }; probe.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                        var lines = Math.Max(1, Math.Ceiling(probe.DesiredSize.Width / Math.Max(1, variable.ActualWidth) - .1));
                        Check(variable.ActualHeight >= lines * 15 - 1, $"{AutomationProperties.GetAutomationId(row)} variable name wraps instead of being cut off at {width} ({lines} lines needed, {variable.ActualHeight:0} high)");
                    }
                }
                var advancedTop = Button(page, "NetworkAccessDirectAdvanced").TransformToVisual(page).TransformPoint(new(0, 0)).Y;
                var enabledTop = Toggle(page, "NetworkAccessDirectEnabled").TransformToVisual(page).TransformPoint(new(0, 0)).Y;
                var runTop = runButton.TransformToVisual(page).TransformPoint(new(0, 0)).Y;
                Check(enabledTop < runTop && runTop < advancedTop, $"the enable row, the diagnostics and the advanced row are in official order at {width}");
                var oval = CornerShapeChecks.OvalCorners(page);
                Check(oval.Count == 0, $"no oval corners at {width}" + (oval.Count > 0 ? ": " + string.Join("; ", oval) : ""));
                if (width is 1440 or 1024 or 800 or 560)
                {
                    await CaptureAsync(outer, $"direct-section-{width}.png");
                    scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                    await CaptureAsync(outer, $"direct-section-{width}-bottom.png");
                    scroller.ChangeView(null, scroller.ScrollableHeight / 2, null, true); await Task.Delay(150);
                    await CaptureAsync(outer, $"direct-section-{width}-middle.png");
                    scroller.ChangeView(null, 0, null, true); await Task.Delay(50);
                }
            }
            // The error and check states at the narrowest width.
            server.Refresh = "refuse"; server.StatusFails = true;
            Invoke(Button(page, "NetworkAccessDirectRunCheck"));
            await Until(() => Text(page, "NetworkAccessDirectCheckMessage").Visibility == Visibility.Visible, "refusal at 560");
            Poll();
            await Until(() => Present(page, "NetworkAccessDirectStatusError"), "failed poll at 560");
            await Task.Delay(150);
            await CaptureAsync(outer, "direct-errors-560.png");
            Check(CornerShapeChecks.OvalCorners(page).Count == 0, "no oval corners with the error states shown");
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(250);
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Direct connections fixture failures: " + string.Join("; ", Failures));
    }

    private static string Trim(string text) => text.Length > 40 ? text[..40] + "…" : text;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static bool Visible(UIElement element) { for (DependencyObject? e = element; e != null; e = VisualTreeHelper.GetParent(e)) if (e is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToArray();
    private static bool Present(DependencyObject page, string id) => Descendants(page).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) == id && Visible(e));
    private static Grid DiagnosticRow(DependencyObject page, string id) => Descendants(page).OfType<Grid>().Single(g => AutomationProperties.GetAutomationId(g) == "NetworkAccessDirect" + id);
    /// <summary>The state or value shown on a row's label line (the first right-aligned text).</summary>
    private static string Value(DependencyObject page, string id) => Descendants(DiagnosticRow(page, id)).OfType<TextBlock>().First(t => t.TextAlignment == TextAlignment.Right).Text;
    /// <summary>The detail lines under a row's value, in order.</summary>
    private static List<string> Lines(DependencyObject page, string id) => Descendants(DiagnosticRow(page, id)).OfType<StackPanel>().Where(p => p.Children.Count > 0 && p.Children.All(c => c is TextBlock { TextAlignment: TextAlignment.Right }))
        .SelectMany(p => p.Children.OfType<TextBlock>()).Select(t => t.Text).ToList();
    private static TextBlock Text(DependencyObject page, string id) => Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == id);
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static ToggleSwitch Toggle(DependencyObject page, string id) => Descendants(page).OfType<ToggleSwitch>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static TextBox TextBox(DependencyObject page, string id) => Descendants(page).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == id);
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
