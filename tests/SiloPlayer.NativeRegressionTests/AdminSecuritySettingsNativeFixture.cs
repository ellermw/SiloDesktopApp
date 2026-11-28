using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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
using F = SiloPlayer.Core.Models.Admin.AdminRateLimitFields;

/// <summary>Production Configure > Security registration and Frame entry using synthetic server data only.</summary>
internal static class AdminSecuritySettingsNativeFixture
{
    private const string Local = "media_servers.allow_private_destinations";
    private const string InitialConfig = """
        {"enabled":true,"backend":"memory","global_requests_per_second":1000,
         "tiers":{"standard":{"requests_per_second":20,"requests_per_minute":1200,"burst":20},"elevated":{"requests_per_second":100,"requests_per_minute":6000,"burst":100}},
         "ip_requests_per_second":120,"ip_requests_per_minute":6000,"ip_burst":120,
         "auth_endpoints":{"login":{"requests_per_minute":20,"burst":10},"signup":{"requests_per_minute":10,"burst":6},"setup":{"requests_per_minute":10,"burst":6},
           "password_change":{"requests_per_minute":10,"burst":5},"device_start":{"requests_per_minute":20,"burst":10},"device_lookup":{"requests_per_minute":60,"burst":20},
           "device_poll":{"requests_per_minute":120,"burst":30},"autoscan_webhook":{"requests_per_minute":60,"burst":30},
           "invitation":{"requests_per_minute":20,"burst":10},"password_reset":{"requests_per_minute":20,"burst":10},"password_reset_request":{"requests_per_minute":5,"burst":3},
           "apple_push_retire":{"requests_per_minute":20,"burst":10},"android_push_retire":{"requests_per_minute":20,"burst":10}},
         "session_quota":{"mode":"log","requests_per_hour":10000,"burst":30000}}
        """;
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new() { ["branding.server_name"] = "Synthetic Silo", [Local] = "false", ["redis.db"] = "0" };
        internal JsonObject Rate = JsonNode.Parse(InitialConfig)!.AsObject();
        internal bool Active = true, ConfigFail, StatusFail, PutFail;
        internal string[] ManagedKeys = [];
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal string? ActiveBackend = "memory";
        internal bool? RedisAvailable = true;
        internal string PatchMode = "ok";
        internal int Version = 1, RateVersion = 1, Puts, Patches, StatusReads;
        internal TaskCompletionSource? PutGate, PatchGate, StatusGate;
        internal string? LastPatchIfMatch, LastPatchBody;
        internal readonly List<string> Log = [];
        internal string[] Writes { get { lock (Log) return Log.Where(entry => !entry.StartsWith("GET", StringComparison.Ordinal)).ToArray(); } }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Log) Log.Add(request.Method + " " + path);
            if (path == "/api/v2/admin/rate-limits/config" && request.Method == HttpMethod.Get)
            {
                if (ConfigFail) return Json(new { title = "down", status = 503 }, HttpStatusCode.ServiceUnavailable);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Rate.ToJsonString(), Encoding.UTF8, "application/json") };
                response.Headers.ETag = new($"\"rate-v{RateVersion}\"");
                return response;
            }
            if (path == "/api/v2/admin/rate-limits/status")
            {
                if (StatusGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                if (StatusFail) return Json(new { title = "down", status = 503 }, HttpStatusCode.ServiceUnavailable);
                var status = new JsonObject { ["active"] = Active };
                if (ActiveBackend != null) status["active_backend"] = ActiveBackend;
                if (RedisAvailable != null) status["redis_available"] = RedisAvailable;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(status.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Patch)
            {
                Patches++; LastPatchIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPatchBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (PatchGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                switch (PatchMode)
                {
                    case "conflict":
                        var conflict = Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", null);
                        conflict.Headers.ETag = new($"\"rate-v{RateVersion}\""); return conflict;
                    case "unknown": return Problem(HttpStatusCode.GatewayTimeout, "dependency_unavailable", null);
                    case "validation": return Problem(HttpStatusCode.BadRequest, "validation_failed", "session_quota.burst must refill within a week: at most 168 times requests_per_hour");
                }
                Merge(Rate, JsonNode.Parse(LastPatchBody)!.AsObject()); RateVersion++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"status":"ok","restart_required":{{(PatchMode == "restart" ? "true" : "false")}}}""", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Put)
            {
                Puts++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (PutGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                if (PutFail) return Problem(HttpStatusCode.BadRequest, "validation_failed", "invalid");
                using var data = JsonDocument.Parse(body);
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject()) updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!;
                Version++;
                return Json(new { values = updated, restart_required = false });
            }
            object result = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => ManagedKeys.Length > 0 && NamedVariables
                    ? new { configured = Array.Empty<string>(), managed_by_env = ManagedKeys, managed_by_env_vars = ManagedKeys.ToDictionary(key => key, key => "SILO_" + key.ToUpperInvariant().Replace('.', '_')) }
                    : (object)new { configured = Array.Empty<string>(), managed_by_env = ManagedKeys },
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "ratelimit.enabled", "ratelimit.backend" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => ServerStatus(),
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(result);
        }
        private object ServerStatus() { StatusReads++; return new { restart_required = false, restart_mark_count = 0, restart_requested = false }; }
        private static void Merge(JsonObject target, JsonObject patch)
        {
            foreach (var (name, value) in patch.ToArray())
                if (value is JsonObject child && target[name] is JsonObject existing) Merge(existing, child);
                else target[name] = value?.DeepClone();
        }
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"security-fixture-v{Version}\""); return response;
        }
        private static HttpResponseMessage Problem(HttpStatusCode status, string type, string? detail) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(new { type = "https://siloserver.org/docs/api/v2/problems/" + type, title = "Problem", status = (int)status, detail }), Encoding.UTF8, "application/problem+json") };
    }

    private static readonly string[] NumberPaths =
    [
        F.Global, F.IpPerSecond, F.IpPerMinute, F.IpBurst, F.QuotaPerHour, F.QuotaBurst,
        .. F.Tiers.SelectMany(tier => new[] { F.Tier(tier, "requests_per_second"), F.Tier(tier, "requests_per_minute"), F.Tier(tier, "burst") }),
        .. F.Endpoints.SelectMany(endpoint => new[] { F.Endpoint(endpoint, "requests_per_minute"), F.Endpoint(endpoint, "burst") }),
    ];

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
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        // Invoke the same registration entry point as App, rather than registering a test-only page.
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
            // The skeleton stays until both the settings form and the rate read have settled.
            server.StatusGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var resolved = AdminShellPage.TryOpenHref("/admin/settings?tab=rate-limiting");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route, Request.Route: AdminRoute.SecuritySettings })
                throw new InvalidOperationException("Security is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Security did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content?.GetType().Name != "AdminSecuritySettingsPage"; i++) await Task.Delay(25);
            if (inner.Content is not AdminSecuritySettingsPage page) throw new InvalidOperationException("Security did not activate its production WinUI Page.");
            await Until(() => page.Controller?.State.Loaded == true, "settings form loaded");
            await Task.Delay(150);
            var loading = (FrameworkElement)Field(page, "_loading")!;
            Check(loading.Visibility == Visibility.Visible && AutomationProperties.GetName(loading) == "Loading settings" && ((FrameworkElement)Field(page, "_fields")!).Visibility == Visibility.Collapsed,
                "the loading skeleton stays until the rate read settles, even after the settings form loaded");
            await CaptureAsync(outer, "security-loading.png");
            server.StatusGate.SetResult(); server.StatusGate = null;
            await Until(() => page.Controller?.State.RateLoaded == true && Number(page, F.Global).Text == "1000", "Security fields rendered");
            await Task.Delay(100);
            var controller = page.Controller!;
            Check(true, "Security activates through the legacy rate-limiting link, production registration, shell and Frame");
            Check(loading.Visibility == Visibility.Collapsed, "skeleton clears once both reads settle");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered Security and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(t => !IsInside(t, page)).Select(t => t.Text).ToArray();
            Check(shellText.Contains("Sign-in & Security") && shellText.Contains("Security"), "sidebar shows Sign-in & Security with its Security leaf");
            Check(AdminNavigation.SearchGroups("where counters", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Any(p => p.Route == AdminRoute.SecuritySettings),
                "registered Security is searchable by its official setting names");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Security navigation keeps page transitions suppressed");
            var scrollerRegion = (ScrollViewer)Field(page, "_scroller")!;
            Check(AutomationProperties.GetName(scrollerRegion) == "Security settings", "the scroll region is named Security settings");

            // A draft for a key the environment takes over is dropped; the row is disabled, shows the official note and carries it in its help text.
            var localSwitch = Descendants(page).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t) == "Security_" + AdminSecuritySettingsController.LocalNetwork);
            var localSaved = localSwitch.IsOn;
            controller.SetLocalNetwork(!localSaved);
            await Until(() => controller.State.DirtyKeys.Contains(AdminSecuritySettingsController.LocalNetwork), "local-network draft before the environment takes over");
            server.ManagedKeys = [AdminSecuritySettingsController.LocalNetwork]; await controller.ReloadAsync(preserveDraft: true);
            await Until(() => controller.State.ManagedByEnv.Contains(AdminSecuritySettingsController.LocalNetwork) && !localSwitch.IsEnabled, "managed local-network switch locked");
            await Until(() => !controller.State.DirtyKeys.Contains(AdminSecuritySettingsController.LocalNetwork) && localSwitch.IsOn == localSaved, "the dropped draft is rendered back to the saved value");
            Check(!controller.State.DirtyKeys.Contains(AdminSecuritySettingsController.LocalNetwork) && localSwitch.IsOn == localSaved, "the draft of the key that became managed is dropped");
            Check(EnvironmentNoteProbe.Verify(page, localSwitch, EnvironmentNoteProbe.Named("SILO_MEDIA_SERVERS_ALLOW_PRIVATE_DESTINATIONS")) is null, "the managed Security row is disabled, shows the official note naming the variable and carries it in its help text");
            server.NamedVariables = false; await controller.ReloadAsync(preserveDraft: true);
            await Until(() => EnvironmentNoteProbe.Verify(page, localSwitch, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the row and shows the official field message");
            server.NamedVariables = true; server.ManagedKeys = []; await controller.ReloadAsync(preserveDraft: false);
            await Until(() => controller.State.ManagedByEnv.Count == 0 && localSwitch.IsEnabled, "managed lock released");

            // Structure: 2 groups, 17 rows, 3 subheadings, 32 controls with distinct names.
            var groupTitles = Descendants(page).OfType<TextBlock>().Where(t => AutomationProperties.GetHeadingLevel(t) == AutomationHeadingLevel.Level2).Select(t => t.Text).ToArray();
            Check(groupTitles.SequenceEqual(["Local network", "Rate limiting"]), "two groups in official order: Local network, Rate limiting");
            var rows = Rows(page).Where(r => r.Visibility == Visibility.Visible).ToArray();
            Check(rows.Length == 17, $"seventeen visible setting rows (found {rows.Length})");
            var subheadings = Descendants(page).OfType<TextBlock>().Where(t => AutomationProperties.GetHeadingLevel(t) == AutomationHeadingLevel.Level4).Select(t => t.Text).ToArray();
            Check(subheadings.SequenceEqual(["PER SIGN-IN SESSION", "API KEY TIERS", "SIGN-IN AND WEBHOOK ENDPOINTS"]) &&
                Descendants(page).OfType<TextBlock>().Where(t => AutomationProperties.GetHeadingLevel(t) == AutomationHeadingLevel.Level4).All(t => t.FontSize == 12 && t.FontWeight.Weight == 600),
                "three 12px semibold uppercase level-4 subheadings in official order");
            var editors = Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Security_", StringComparison.Ordinal)).ToArray();
            Check(editors.OfType<ToggleSwitch>().Count() == 2 && editors.OfType<ComboBox>().Count() == 2 && editors.OfType<TextBox>().Count() == 28 && editors.Length == 32,
                "thirty-two editable controls: two toggles, two selects and twenty-eight number boxes");
            var names = editors.Select(AutomationProperties.GetName).ToArray();
            Check(names.Distinct(StringComparer.Ordinal).Count() == 32 && names.All(n => n.Length > 0), "every Security control has a distinct accessible name");
            foreach (var (path, name) in new[]
            {
                (F.Global, "Whole-server limit"), (F.IpPerSecond, "Per address and account Per second"), (F.QuotaBurst, "Quota size Burst"),
                (F.Tier("standard", "requests_per_second"), "Standard API keys Per second"), (F.Tier("elevated", "burst"), "Elevated API keys Burst"),
                (F.Endpoint("login", "requests_per_minute"), "Sign in Per minute"), (F.Endpoint("device_poll", "burst"), "TV sign-in: waiting for approval Burst"),
            })
                Check(AutomationProperties.GetName(Number(page, path)) == name, $"{path} is named '{name}'");
            Check(AutomationProperties.GetName(Toggle(page, F.Enabled)) == "Enable rate limiting" && AutomationProperties.GetName(Select(page, F.Backend)) == "Where counters are kept" &&
                AutomationProperties.GetName(Toggle(page, Local)) == "Local servers for every account" && AutomationProperties.GetName(Select(page, F.QuotaMode)) == "Hourly request quota",
                "toggles and selects carry their row labels");
            Check(!Descendants(page).OfType<Border>().Any(b => AutomationProperties.GetName(b) == "Restart" && b.Visibility == Visibility.Visible) &&
                !Descendants(page).OfType<Border>().Any(b => b.Width == 6 && b.Height == 6 && b.Visibility == Visibility.Visible),
                "Security rows and groups show no restart chips or dirty dots, as in the official page");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "per second" && t.Visibility == Visibility.Visible), "the whole-server limit shows its per second unit");
            Check(editors.OfType<TextBox>().All(b => b.BorderBrush is SolidColorBrush { Color: { A: 255, R: 0x6A, G: 0x6A, B: 0x74 } }) &&
                editors.OfType<ComboBox>().All(b => b.BorderBrush is SolidColorBrush { Color: { A: 255, R: 0x6A, G: 0x6A, B: 0x74 } }),
                "Security number boxes and selects use the global 3:1 input edge (ordinary Input/SelectTrigger)");
            Check(editors.OfType<TextBox>().Where(b => AutomationProperties.GetAutomationId(b) != "Security_" + F.Global).All(b => b.Width == 96) && Number(page, F.Global).Width == 160 &&
                Select(page, F.Backend).Width == 240 && Select(page, F.QuotaMode).Width == 240, "RateBoxes are 96 wide, the global box 160 and both selects 240");

            // Local network: draft-driven warning, hairline kept.
            var localToggle = Toggle(page, Local);
            var warning = (Border)Field(page, "_warning")!;
            var localRow = (Grid)Field(page, "_localRow")!;
            Check(warning.Visibility == Visibility.Collapsed && localRow.BorderThickness.Bottom == 0, "no warning and no trailing rule while local servers are off");
            localToggle.IsOn = true;
            await Until(() => controller.State.DirtyCount == 1, "local network staged");
            await Task.Delay(100);
            Check(warning.Visibility == Visibility.Visible && localRow.BorderThickness.Bottom == 1 && warning.BorderThickness.Bottom == 0 &&
                Descendants(warning).OfType<TextBlock>().Any(t => t.Text.StartsWith("Anyone who can sign in", StringComparison.Ordinal) && t.FontSize == 12),
                "staging local servers shows the 12px warning below a toggle row that keeps its hairline");
            Check(SaveBarText(page) == "1 unsaved change", "the save bar counts the staged local switch");
            await CaptureAsync(outer, "security-local-warning.png");
            localToggle.IsOn = false;
            await Until(() => controller.State.DirtyCount == 0, "local network returned");
            await Task.Delay(100);
            Check(warning.Visibility == Visibility.Collapsed && SaveBar(page).Visibility == Visibility.Collapsed, "returning the switch hides the warning and the save bar (#4309)");

            // Number entry: whole draft is one change; returning clears; invalid text reverts on blur.
            var global = Number(page, F.Global);
            var inputTrace = new Queue<string>();
            var assigningInvalid = false;
            var inputSequence = 0;
            void TraceInput(string reason)
            {
                inputTrace.Enqueue($"{++inputSequence}@{DateTime.UtcNow:O} {reason}: fixtureAssignment={assigningInvalid}, focus={global.FocusState}, text='{global.Text}', selection={global.SelectionStart}/{global.SelectionLength}, value={controller.State.RateValues?.Number(F.Global)}");
                while (inputTrace.Count > 80) inputTrace.Dequeue();
            }
            global.GotFocus += (_, _) => TraceInput("GotFocus");
            global.LostFocus += (_, _) => TraceInput("LostFocus");
            global.TextChanging += (_, _) => TraceInput("TextChanging");
            global.TextChanged += (_, _) => TraceInput("TextChanged");
            global.TextCompositionStarted += (_, _) => TraceInput("CompositionStarted");
            global.TextCompositionChanged += (_, _) => TraceInput("CompositionChanged");
            global.TextCompositionEnded += (_, _) => TraceInput("CompositionEnded");
            global.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
                TraceInput($"KeyDown key={args.Key} scan={args.KeyStatus.ScanCode} repeat={args.KeyStatus.RepeatCount} handled={args.Handled}")), true);
            global.AddHandler(UIElement.KeyUpEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
                TraceInput($"KeyUp key={args.Key} scan={args.KeyStatus.ScanCode} handled={args.Handled}")), true);
            global.CharacterReceived += (_, args) => TraceInput($"CharacterReceived U+{args.Character:X4} handled={args.Handled}");
            global.Focus(FocusState.Keyboard); global.Text = "1001";
            await Until(() => controller.State.RateDirty, "global edited");
            await Task.Delay(100);
            Check(SaveBarText(page) == "1 unsaved change", "a typed rate value makes one unsaved change");
            Number(page, F.IpBurst).Text = "121";
            await Task.Delay(100);
            Check(SaveBarText(page) == "1 unsaved change" && controller.State.RateDirtyPaths.Count == 2, "the whole rate-limit draft still counts as one change");
            Number(page, F.IpBurst).Text = "120";
            global.Text = "1000";
            await Until(() => !controller.State.RateDirty, "typed back");
            await Task.Delay(100);
            Check(SaveBar(page).Visibility == Visibility.Collapsed, "typing the saved values back hides the save bar (#4309)");
            foreach (var invalid in new[] { "12abc", "", "0", "-5", "１２", "1.9", "35791395" })
            {
                global.Focus(FocusState.Keyboard);
                TraceInput($"FixtureSetBefore expected='{invalid}'");
                assigningInvalid = true;
                try { global.Text = invalid; }
                finally { assigningInvalid = false; }
                TraceInput($"FixtureSetAfter expected='{invalid}'");
                await Task.Delay(40);
                var keptInvalid = controller.State.RateValues!.Number(F.Global) == 1000 && global.Text == invalid;
                if (!keptInvalid)
                {
                    TraceInput("InvalidCheck");
                    Program.Log("SECURITY-INPUT-TRACE: " + string.Join(" | ", inputTrace));
                }
                Check(keptInvalid, $"invalid '{invalid}' leaves the draft unchanged while focused");
                Toggle(page, F.Enabled).Focus(FocusState.Keyboard); await Task.Delay(80);
                Check(global.Text == "1000", $"invalid '{invalid}' reverts on blur");
            }
            // IME composition: the real TextChanging/LostFocus handlers ignore text while composition is active.
            var composing = (HashSet<TextBox>)Field(page, "_composing")!;
            global.Focus(FocusState.Keyboard); composing.Add(global); global.Text = "1777"; await Task.Delay(60);
            Toggle(page, F.Enabled).Focus(FocusState.Keyboard); await Task.Delay(60);
            var keptComposition = controller.State.RateValues!.Number(F.Global) == 1000 && global.Text == "1777";
            if (!keptComposition)
            {
                TraceInput($"CompositionCheck simulated={composing.Contains(global)}");
                Program.Log("SECURITY-INPUT-TRACE: " + string.Join(" | ", inputTrace));
            }
            Check(keptComposition, "text typed during an active composition neither commits nor reverts");
            composing.Remove(global); global.Text = "1000"; await Task.Delay(60);
            Check(!controller.State.RateDirty, "after composition ends normal entry resumes");

            // Every number box reaches its own draft path.
            var wiring = true;
            foreach (var path in NumberPaths)
            {
                var box = Number(page, path);
                var before = controller.State.RateValues!.Number(path);
                box.Text = (before + 1).ToString(System.Globalization.CultureInfo.InvariantCulture); await Task.Delay(20);
                wiring &= controller.State.RateDirtyPaths.SequenceEqual([path]);
                box.Text = before.ToString(System.Globalization.CultureInfo.InvariantCulture); await Task.Delay(20);
                wiring &= !controller.State.RateDirty;
            }
            Check(wiring, "each of the twenty-eight boxes edits only its own field and returns clean");

            // Selects and toggle.
            var backend = Select(page, F.Backend);
            Check(backend.Items.Cast<ComboBoxItem>().Select(i => i.Content as string).SequenceEqual(["This server only", "Shared via Redis"]), "backend options use the official labels");
            var quota = Select(page, F.QuotaMode);
            Check(quota.Items.Cast<ComboBoxItem>().Select(i => i.Content as string).SequenceEqual(["Off", "Log only", "Enforce"]) && (quota.SelectedItem as ComboBoxItem)?.Tag as string == "log",
                "quota options use the official labels with Log only selected");
            await SelectItem(backend, 1);
            await Until(() => controller.State.RateDirty, "redis staged");
            Check(controller.State.RateValues!.Text(F.Backend) == "redis", "choosing Shared via Redis stages redis");
            await SelectItem(backend, 0);
            await Until(() => !controller.State.RateDirty, "memory restored");
            await SelectItem(quota, 0);
            await Until(() => controller.State.RateValues!.Text(F.QuotaMode) == "off", "quota off");
            await Task.Delay(100);
            Check(!Number(page, F.QuotaPerHour).IsEnabled && !Number(page, F.QuotaBurst).IsEnabled && Number(page, F.QuotaPerHour).Opacity == .5 &&
                Number(page, F.QuotaPerHour).Text == "10000" && Number(page, F.IpBurst).IsEnabled, "quota Off disables the size boxes at 50% while keeping their values");
            await CaptureAsync(outer, "security-quota-off.png");
            await SelectItem(quota, 1);
            await Until(() => !controller.State.RateDirty, "quota log restored");
            await Task.Delay(80);
            Check(Number(page, F.QuotaPerHour).IsEnabled, "Log only re-enables the size boxes");
            var enabledToggle = Toggle(page, F.Enabled);
            enabledToggle.IsOn = false; await Until(() => controller.State.RateDirty, "enabled off");
            enabledToggle.IsOn = true; await Until(() => !controller.State.RateDirty, "enabled on");
            Check(true, "the rate-limiting switch stages and returns clean");

            // Server-snapshot hints: Redis unavailable, drift, not running.
            server.RedisAvailable = false; await controller.ReloadRateAsync(); await Task.Delay(150);
            var redisItem = (ComboBoxItem)backend.Items[1];
            Check(!redisItem.IsEnabled && (FrameworkElementAutomationPeer.CreatePeerForElement(redisItem)?.IsEnabled() == false) &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Redis shares counters across servers, after a restart. Set up Redis on the General page first."),
                "unconfigured Redis disables its option (automation IsEnabled false) and points at General");
            backend.IsDropDownOpen = true; await Task.Delay(300);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).FirstOrDefault();
            var realized = popup?.Child is FrameworkElement popupChild ? Descendants(popupChild).OfType<ComboBoxItem>().ToArray() : [];
            Check(realized.Length == 2 && !realized[1].IsEnabled && realized[0].IsEnabled, "the open backend dropdown realizes Redis as a disabled item");
            backend.IsDropDownOpen = false; await Task.Delay(150);
            server.RedisAvailable = null; await controller.ReloadRateAsync(); await Task.Delay(150);
            Check(redisItem.IsEnabled, "an older status without redis_available leaves Redis selectable");
            server.Rate["backend"] = "redis"; server.RedisAvailable = true; server.RateVersion++;
            await controller.ReloadRateAsync(); await Task.Delay(150);
            var drift = (Grid)Field(page, "_drift")!;
            Check(drift.Visibility == Visibility.Visible && Descendants(drift).OfType<TextBlock>().Single().Text ==
                "The running limiter is using in-memory counters, not the saved Redis backend. If a restart does not fix it, check that the saved backend is reachable.",
                "drift states the running in-memory limiter against the saved Redis backend");
            await SelectItem(backend, 0); await Until(() => controller.State.RateDirty, "draft memory");
            await Task.Delay(100);
            Check(drift.Visibility == Visibility.Visible, "the drift hint follows the saved backend, not the draft");
            await Click(page, "SecurityDiscard"); await Until(() => !controller.State.RateDirty, "discard draft");
            server.Active = false; server.ActiveBackend = null; await controller.ReloadRateAsync(); await Task.Delay(150);
            var notRunning = (Grid)Field(page, "_notRunning")!;
            Check(notRunning.Visibility == Visibility.Visible && drift.Visibility == Visibility.Collapsed &&
                Descendants(notRunning).OfType<TextBlock>().Single().Text.StartsWith("Enabled, but no limiter is running", StringComparison.Ordinal), "an enabled limiter that is not running is reported");
            await CaptureAsync(outer, "security-hints.png");
            server.Rate["backend"] = "memory"; server.Active = true; server.ActiveBackend = "memory"; server.RateVersion++;
            await controller.ReloadRateAsync(); await Task.Delay(150);
            Check(notRunning.Visibility == Visibility.Collapsed && drift.Visibility == Visibility.Collapsed, "hints clear when the limiter matches");

            // One Save, ordered writers, captured validator, frozen rate editors.
            server.PatchMode = "restart";
            var statusReads = server.StatusReads;
            localToggle.IsOn = true; await Until(() => controller.State.DirtyKeys.Count == 1, "local dirty");
            global.Text = "1500"; await Until(() => controller.State.RateDirty, "rate dirty");
            await Task.Delay(100);
            Check(SaveBarText(page) == "2 unsaved changes", "the save bar counts one batched key plus one rate draft");
            server.PutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var expectedIfMatch = $"\"rate-v{server.RateVersion}\"";
            var writes = server.Writes.Length;
            await Click(page, "SecuritySave");
            await Until(() => controller.State.Saving && server.Puts > 0, "PUT pending");
            await Task.Delay(120);
            Check(!global.IsEnabled && !Toggle(page, F.Enabled).IsEnabled && !backend.IsEnabled && !Button(page, "SecuritySave").IsEnabled && localToggle.IsEnabled,
                "rate editors freeze from the Save click while the local switch stays editable");
            Check(AutomationProperties.GetName(Button(page, "SecuritySave")) == "Saving...", "the save button reads Saving... while either writer runs");
            Check(!await page.CanLeaveAsync(), "navigation cannot depart during a current save");
            await CaptureAsync(outer, "security-saving.png");
            server.PutGate.SetResult(); server.PutGate = null;
            await Until(() => !controller.State.Saving, "save settled", 8000);
            await Task.Delay(200);
            var newWrites = server.Writes.Skip(writes).ToArray();
            Check(newWrites.SequenceEqual(["PUT /api/v2/admin/settings", "PATCH /api/v2/admin/rate-limits/config"]), "Save sends the batched PUT, then one PATCH");
            Check(server.LastPatchIfMatch == expectedIfMatch, "the PATCH carries the captured strong config validator");
            Check(server.LastPatchBody != null && !server.LastPatchBody.Contains("null") && !server.LastPatchBody.Contains("ratelimit.") &&
                JsonNode.Parse(server.LastPatchBody)!["auth_endpoints"]!.AsObject().Count == 8, "the PATCH has no nulls, no batched keys and only the eight visible endpoints");
            Check(server.Rate["auth_endpoints"]!["invitation"]!["burst"]!.GetValue<int>() == 10, "hidden endpoints survive through the server merge");
            Check(controller.State.DirtyCount == 0 && global.IsEnabled && global.Text == "1500" && controller.State.RateSnapshot!.ETag == $"\"rate-v{server.RateVersion}\"",
                "after the acknowledged PATCH the re-read config becomes a clean editable baseline");
            await Until(() => server.StatusReads > statusReads, "shell restart status refreshed");
            Check(server.StatusReads > statusReads, "acknowledged writes refresh the shell restart status");

            // 412: explicit Reload rebases only the edited field.
            server.PatchMode = "conflict";
            global.Text = "1600"; await Until(() => controller.State.RateDirty, "draft for conflict");
            await Click(page, "SecuritySave");
            await Until(() => controller.State.RateBlock == AdminRateLimitBlock.Conflict, "conflict");
            await Task.Delay(120);
            var rateError = Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "SecurityRateErrorText");
            var rateReload = Button(page, "SecurityRateReload");
            Check(rateError.Text == "Rate limit settings changed. Reload and review them before saving again." && rateReload.Visibility == Visibility.Visible &&
                !Button(page, "SecuritySave").IsEnabled && global.Text == "1600" && global.IsEnabled, "a 412 keeps the draft, disables Save and offers Reload");
            await CaptureAsync(outer, "security-conflict.png");
            var patches = server.Patches;
            server.Rate["ip_burst"] = 222; server.RateVersion++; server.PatchMode = "ok";
            Invoke(rateReload);
            await Until(() => controller.State.RateBlock == AdminRateLimitBlock.None && !controller.State.RateLoading, "conflict reloaded");
            await Task.Delay(120);
            Check(global.Text == "1600" && Number(page, F.IpBurst).Text == "222" && controller.State.RateDirtyPaths.SequenceEqual([F.Global]) &&
                Descendants(page).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "SecurityRateNotice" && t.Visibility == Visibility.Visible),
                "Reload keeps the edited field, adopts the concurrent change and says so");
            Check(server.Patches == patches, "no PATCH is replayed automatically");
            await Click(page, "SecuritySave");
            await Until(() => !controller.State.Saving && !controller.State.RateDirty, "save after rebase");
            Check(server.LastPatchIfMatch == $"\"rate-v{server.RateVersion - 1}\"", "the user Save after Reload uses the refreshed validator");

            // Unknown outcome: no replay, frozen editors, reload reconciles as applied.
            server.PatchMode = "unknown";
            global.Text = "1700"; await Until(() => controller.State.RateDirty, "draft for unknown");
            patches = server.Patches;
            await Click(page, "SecuritySave");
            await Until(() => controller.State.RateBlock == AdminRateLimitBlock.OutcomeUnknown, "unknown");
            await Task.Delay(150);
            Check(rateError.Text.StartsWith("The rate limit save outcome is unknown", StringComparison.Ordinal) && !global.IsEnabled && !Button(page, "SecuritySave").IsEnabled && server.Patches == patches + 1,
                "an unknown outcome is sent once, freezes rate editors and requires Reload");
            await CaptureAsync(outer, "security-unknown.png");
            server.Rate["global_requests_per_second"] = 1700; server.RateVersion++; server.PatchMode = "ok";
            Invoke(rateReload);
            await Until(() => controller.State.RateBlock == AdminRateLimitBlock.None && !controller.State.RateLoading, "unknown reconciled");
            await Task.Delay(120);
            Check(!controller.State.RateDirty && global.IsEnabled && global.Text == "1700" && server.Patches == patches + 1, "a reload that finds the intent applied settles it without a resend");

            // 400 detail and partial success.
            server.PatchMode = "validation";
            localToggle.IsOn = false; await Until(() => controller.State.DirtyKeys.Count == 1, "local back");
            Number(page, F.QuotaBurst).Text = "9999999"; await Until(() => controller.State.RateDirty, "quota burst");
            await Click(page, "SecuritySave");
            await Until(() => !controller.State.Saving && controller.State.RateMessage == AdminRateLimitMessage.SaveFailed, "validation");
            await Task.Delay(120);
            Check(rateError.Text == "session_quota.burst must refill within a week: at most 168 times requests_per_hour", "the server's validation sentence is shown inline");
            Check(controller.State.DirtyKeys.Count == 0 && controller.State.RateDirty && SaveBarText(page) == "1 unsaved change" && Button(page, "SecuritySave").IsEnabled,
                "partial success settles the local switch and keeps the rate draft saveable");
            await CaptureAsync(outer, "security-validation.png");
            server.PatchMode = "ok";
            await Click(page, "SecurityDiscard"); await Until(() => controller.State.DirtyCount == 0, "discard");
            await Until(() => Number(page, F.QuotaBurst).Text == "30000", "discard render", 2000).ContinueWith(t => t.Exception == null);
            Check(Number(page, F.QuotaBurst).Text == "30000", "Discard restores the hydrated rate baseline");

            // PUT failure skips the PATCH.
            server.PutFail = true; patches = server.Patches;
            localToggle.IsOn = true; global.Text = "1800";
            await Until(() => controller.State.DirtyCount == 2, "two changes");
            await Click(page, "SecuritySave");
            await Until(() => !controller.State.Saving && controller.State.Error != null, "put failed");
            Check(server.Patches == patches && controller.State.DirtyCount == 2, "a failed batched write skips the PATCH and keeps both drafts");
            server.PutFail = false;
            await Click(page, "SecurityDiscard"); await Until(() => controller.State.DirtyCount == 0, "discard after put failure");

            // Dirty departure dialog: Cancel keeps, Discard leaves.
            global.Text = "1900"; await Until(() => controller.State.RateDirty, "dialog draft");
            shell.Navigate(new(AdminRoute.Insights));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            await CaptureAsync(dialog, "security-discard-dialog.png");
            Invoke(DialogButton(dialog, "Cancel")); await Task.Delay(150);
            Check(ReferenceEquals(inner.Content, page) && controller.State.RateDirty, "Cancel keeps the Security page and its rate draft");

            // Layout at the required viewports.
            foreach (var (width, height) in new[] { (1440, 900), (1440, 600), (1920, 600), (800, 600) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(250); outer.UpdateLayout();
                var scroller = (ScrollViewer)Field(page, "_scroller")!;
                scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"Security has no horizontal scroll overflow at {width}x{height}");
                var viewportLeft = scroller.TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
                var available = scroller.ActualWidth - 2 * gutter;
                var expectedLeft = viewportLeft + gutter + Math.Max(0, available - AdminShellPage.ContentMaxWidth) / 2;
                var bodyLeft = ((FrameworkElement)Field(page, "_body")!).TransformToVisual(null).TransformPoint(new(0, 0)).X;
                Check(Math.Abs(bodyLeft - expectedLeft) < 1.5, $"Security settings column starts at the shared frame gutter at {width}x{height}");
                foreach (var edge in new[] { 8d, scroller.ActualWidth - 18 })
                {
                    var point = scroller.TransformToVisual(null).TransformPoint(new(edge, scroller.ActualHeight / 2));
                    DependencyObject? target = VisualTreeHelper.FindElementsInHostCoordinates(point, outer).FirstOrDefault();
                    while (target != null && target is not ScrollViewer) target = VisualTreeHelper.GetParent(target);
                    Check(ReferenceEquals(target, scroller), $"Security side gutter has the page scrolling ancestor at {width}x{height}");
                }
                var globalRight = global.TransformToVisual(page).TransformPoint(new(global.ActualWidth, 0)).X;
                var boxRights = NumberPaths.Skip(1).Select(path => Number(page, path)).Select(b => b.TransformToVisual(page).TransformPoint(new(b.ActualWidth, 0)).X).ToArray();
                Program.Log($"TRACE security width={width} globalRight={globalRight:F1} boxRightMax={boxRights.Max():F1} boxRightMin={boxRights.Min():F1}");
                var lastBoxes = new[] { F.IpBurst, F.QuotaBurst, F.Tier("standard", "burst"), F.Tier("elevated", "burst") }
                    .Concat(F.Endpoints.Select(e => F.Endpoint(e, "burst"))).Select(path => Number(page, path)).ToArray();
                double RowRight(FrameworkElement control) { var row = Rows(page).First(r => IsInside(control, r)); return row.TransformToVisual(page).TransformPoint(new(row.ActualWidth, 0)).X; }
                double Right(FrameworkElement control) => control.TransformToVisual(page).TransformPoint(new(control.ActualWidth, 0)).X;
                var globalRow = Rows(page).First(r => IsInside(global, r));
                var wideRows = globalRow.ActualWidth >= 520;
                Program.Log($"TRACE security width={width} row={globalRow.ActualWidth:F1} wide={wideRows}");
                if (wideRows)
                    Check(lastBoxes.All(b => Math.Abs(Right(b) - globalRight) < 1.5 && Math.Abs(RowRight(b) - 72 - Right(b)) < 1.5) && Math.Abs(RowRight(global) - 72 - globalRight) < 1.5,
                        $"every row's last box ends on the global input's edge before the reserved 64px unit slot at {width}x{height}");
                else
                    // Stacked rows: as the official stacked row, boxes end at the row edge and the unit follows the global input on its line.
                    Check(lastBoxes.All(b => Math.Abs(RowRight(b) - Right(b)) < 1.5) && globalRight < RowRight(global) - 8 &&
                        Descendants(globalRow).OfType<TextBlock>().Single(t => t.Text == "per second").TransformToVisual(page).TransformPoint(new(0, 0)).X >= globalRight + 7,
                        $"stacked rows end their boxes at the row edge and keep the unit after the global input at {width}x{height}");
                var reachable = editors.Where(c => c.IsLoaded && c.Visibility == Visibility.Visible && c.ActualWidth > 0).Append(Button(page, "SecuritySave")).Append(Button(page, "SecurityDiscard"));
                foreach (var control in reachable)
                {
                    var point = control.TransformToVisual(page).TransformPoint(new(0, 0));
                    Check(point.X >= -1 && point.X + control.ActualWidth <= page.ActualWidth + 1, $"{AutomationProperties.GetAutomationId(control)} stays inside the viewport at {width}x{height}");
                }
                foreach (var row in Rows(page).Where(r => r.Visibility == Visibility.Visible))
                {
                    var label = Descendants(row).OfType<FrameworkElement>().First(e => e.GetType().Name == "AdminSettingsLabel");
                    Check(label.TransformToVisual(row).TransformPoint(new(0, 0)).X <= 1, $"row label stays on its row's left edge at {width}x{height}");
                }
                var flows = Descendants(page).OfType<FrameworkElement>().Where(e => e.GetType().Name == "AdminSettingsRateFlow").ToArray();
                Check(flows.Length == 12 && flows.All(f => f.ActualWidth <= ((FrameworkElement)VisualTreeHelper.GetParent(f)).ActualWidth + 1), $"all twelve RateBox lines fit their rows at {width}x{height}");
                await CaptureAsync(outer, $"security-{width}x{height}.png");
                scroller.ChangeView(null, scroller.ScrollableHeight / 2, null, true); await Task.Delay(150);
                await CaptureAsync(outer, $"security-{width}x{height}-middle.png");
                scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                await CaptureAsync(outer, $"security-{width}x{height}-bottom.png");
                var saveButton = Button(page, "SecuritySave");
                var savePoint = saveButton.TransformToVisual(page).TransformPoint(new(0, 0));
                Check(saveButton.IsLoaded && savePoint.Y >= 0 && savePoint.Y + saveButton.ActualHeight <= page.ActualHeight + 1, $"the save bar stays reachable at {width}x{height}");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(50);
            }
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(300); outer.UpdateLayout();
            var body = (FrameworkElement)Field(page, "_body")!;
            Check(Rows(page).Where(r => r.Visibility == Visibility.Visible).All(r => r.ActualWidth >= 520) && Math.Abs(body.ActualWidth - 1120) < 1.5,
                "returning from 800 to 1440 restores the full 1120 column and wide rows");
            var unitText = Descendants(page).OfType<TextBlock>().Single(t => t.Text == "per second");
            Check(VisualTreeHelper.GetParent(unitText) is Canvas { Width: 64 } && unitText.ActualWidth > 0 && unitText.Clip == null,
                "the per second unit renders unclipped from its reserved 64px slot");

            // Control states: RateBox, select, toggle and the save bar.
            var stateBox = Number(page, F.Tier("standard", "requests_per_minute"));
            stateBox.Focus(FocusState.Keyboard); await Task.Delay(60);
            Check(stateBox.FocusState == FocusState.Keyboard, "a RateBox takes keyboard focus");
            await CaptureAsync(outer, "security-ratebox-keyboard-focus.png");
            foreach (var state in new[] { "PointerOver", "Disabled" })
            {
                stateBox.IsEnabled = state != "Disabled";
                VisualStateManager.GoToState(stateBox, state, false); await Task.Delay(60);
                Check(stateBox.Opacity == (state == "Disabled" ? .5 : 1), $"RateBox {state} uses the official opacity");
                await CaptureAsync(outer, $"security-ratebox-{state}.png");
            }
            stateBox.IsEnabled = true; VisualStateManager.GoToState(stateBox, "Normal", false);
            backend.Focus(FocusState.Keyboard); await Task.Delay(60);
            Check(backend.FocusState == FocusState.Keyboard, "the backend select takes keyboard focus");
            await CaptureAsync(outer, "security-select-keyboard-focus.png");
            enabledToggle.Focus(FocusState.Keyboard); await Task.Delay(60);
            Check(enabledToggle.FocusState == FocusState.Keyboard, "the rate-limiting switch takes keyboard focus");
            await CaptureAsync(outer, "security-toggle-keyboard-focus.png");
            foreach (var id in new[] { "SecuritySave", "SecurityDiscard" })
            {
                var button = Button(page, id);
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                {
                    var wasEnabled = button.IsEnabled;
                    button.IsEnabled = state != "Disabled";
                    VisualStateManager.GoToState(button, state, false); await Task.Delay(50);
                    var border = Descendants(button).OfType<Border>().Single(b => b.Name == "SettingsButtonRoot");
                    Check(border.CornerRadius.TopLeft is >= 10 and <= 25 && border.ActualWidth > border.ActualHeight, $"{id} keeps a bounded non-ellipse backdrop in {state}");
                    await CaptureAsync(outer, $"security-{id}-{state}.png");
                    button.IsEnabled = wasEnabled;
                }
                VisualStateManager.GoToState(button, "Normal", false);
            }

            // Discard departure, initial rate read failure, Retry and Back.
            shell.Navigate(new(AdminRoute.Insights));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "discard navigation dialog");
            Invoke(DialogButton((ContentDialog)Field(page, "_dialog")!, "Discard"));
            await Until(() => inner.Content is AdminInsightsPage, "discard permitted navigation");
            Check(controller.State.Values.Count == 0 && controller.State.RateValues == null, "departure retires the controller and clears both drafts");
            server.StatusFail = true;
            inner.GoBack();
            await Until(() => inner.Content is AdminSecuritySettingsPage { Controller.State.Loaded: true, Controller.State.RateLoading: false }, "Back restored production Security");
            page = (AdminSecuritySettingsPage)inner.Content; controller = page.Controller!;
            await Task.Delay(150);
            var retry = Button(page, "SecurityRateReload");
            Check(controller.State.RateValues == null && retry.Visibility == Visibility.Visible && AutomationProperties.GetName(retry) == "Retry" &&
                !Rows(page).Any(r => r.Visibility == Visibility.Visible && IsInside(r, (FrameworkElement)Field(page, "_rateGroup")!)),
                "a failed rate read shows an inline error with Retry and no invented editable defaults");
            Check(Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "SecurityRateErrorText").Text == "Server settings are temporarily unavailable. Try reloading again shortly.",
                "the rate read failure is described truthfully");
            localToggle = Toggle(page, Local);
            Check(localToggle.IsEnabled, "the Local network group stays usable while the rate read failed");
            Check(Math.Abs(((FrameworkElement)Field(page, "_body")!).ActualWidth - 1120) < 1.5, "an error-only rate group keeps the full settings column");
            await CaptureAsync(outer, "security-rate-read-failed.png");
            localToggle.IsOn = true; await Until(() => controller.State.DirtyCount == 1, "local staged without rates");
            await Click(page, "SecuritySave"); await Until(() => controller.State.DirtyCount == 0 && !controller.State.Saving, "local saved alone");
            Check(server.Values[Local] == "true", "Local network saves independently of the failed rate group");
            server.StatusFail = false;
            Invoke(retry);
            await Until(() => controller.State.RateLoaded, "retry loaded");
            await Task.Delay(120);
            Check(Number(page, F.Global).Text == "1700" && Number(page, F.Global).IsEnabled && retry.Visibility == Visibility.Collapsed, "Retry loads the rate group");

            // Authority replacement clears private values.
            global = Number(page, F.Global);
            global.Text = "1950"; await Until(() => controller.State.RateDirty, "draft before retirement");
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" }); await Task.Delay(250);
            Check(controller.State.Values.Count == 0 && controller.State.RateValues == null && global.Text.Length == 0 && !Toggle(page, Local).IsOn,
                "authority retirement clears both drafts and every value");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Security fixture failures: " + string.Join("; ", Failures));
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static TextBox Number(DependencyObject page, string path) => Descendants(page).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == "Security_" + path);
    private static ComboBox Select(DependencyObject page, string path) => Descendants(page).OfType<ComboBox>().Single(c => AutomationProperties.GetAutomationId(c) == "Security_" + path);
    private static ToggleSwitch Toggle(DependencyObject page, string path) => Descendants(page).OfType<ToggleSwitch>().Single(c => AutomationProperties.GetAutomationId(c) == "Security_" + path);
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static IEnumerable<Grid> Rows(DependencyObject page) => Descendants(page).OfType<Grid>().Where(g => g.GetType().Name == "AdminSettingsRow");
    private static FrameworkElement SaveBar(AdminSecuritySettingsPage page) => (FrameworkElement)Field(page, "_saveBar")!;
    private static string SaveBarText(AdminSecuritySettingsPage page) =>
        SaveBar(page).Visibility == Visibility.Visible ? Descendants(SaveBar(page)).OfType<TextBlock>().First().Text : "";
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(c => c.Content?.ToString() == label || AutomationProperties.GetName(c) == label);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task SelectItem(ComboBox select, int index)
    {
        select.IsDropDownOpen = true; await Task.Delay(250);
        var item = (ComboBoxItem)select.Items[index];
        var provider = FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
        if (provider != null) provider.Select(); else select.SelectedItem = item;
        select.IsDropDownOpen = false; await Task.Delay(150);
    }
    private static async Task Click(DependencyObject page, string id)
    {
        var button = Button(page, id);
        await Until(() => button.IsEnabled && button.IsLoaded, id + " enabled");
        Invoke(button);
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
