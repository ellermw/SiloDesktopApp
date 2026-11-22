using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Settings → Sign-in against official 02a9d1d3e1 (#4602): sign-in providers the deployment provisions, each provider state, the break-glass sentence, left-to-right values, copy,
/// refused and unknown-outcome writes, a doubled activation, and a form that is not rebuilt while idle or typed in. Runs after the main Sign-in &amp; Security block in its own
/// block with its own synthetic server: only synthetic names and secrets, and every mutation is counted.
/// </summary>
internal static class AdminSignInProvisioningNativeFixture
{
    private const string Etag = "\"synthetic-provisioning-1\"";
    private const string Typed = "synthetic-typed-secret-NATIVE-ONLY";
    private const string Evil = "Evil Name Co"; // what the page may show of the saved name below

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Writes = [];
        internal readonly Dictionary<string, string> Bodies = [];
        internal readonly Dictionary<string, TaskCompletionSource> Gates = [];
        internal readonly HashSet<string> Reset = [];
        internal int ConfigRefusal;
        /// <summary>Whether a refused configuration write names a field the deployment owns (the page is out of date) or is a plain validation refusal (nothing applied).</summary>
        internal bool ConfigRefusalManaged = true, FailInstallations, NoPlugins, Net8Enabled = true, Net8Auto = true;
        internal readonly Dictionary<string, int> Reads = [];
        internal bool NoBreakGlass;
        internal int Count(string write) => Writes.Count(w => w == write);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var key = request.Method + " " + path; var write = request.Method != HttpMethod.Get;
            if (write) { Writes.Add(key); Bodies[key] = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct); }
            else Reads[key] = Reads.GetValueOrDefault(key) + 1;
            if (Gates.TryGetValue(key, out var gate)) await gate.Task;
            if (Reset.Contains(key)) throw new HttpRequestException("synthetic transport reset");
            if (!write && FailInstallations && path == "/api/v2/admin/plugins/installations") return new(HttpStatusCode.BadRequest) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            if (write && path.EndsWith("/config") && ConfigRefusal != 0)
                return new((HttpStatusCode)ConfigRefusal) { Content = new StringContent("{\"type\":\"https://siloserver.org/docs/api/v2/problems/validation_failed\",\"status\":" + ConfigRefusal + ",\"errors\":[{\"location\":\"body.config.oidc." + (ConfigRefusalManaged ? "issuer\",\"code\":\"managed_by_environment" : "note\",\"code\":\"invalid") + "\",\"detail\":\"synthetic server text\"}]}", Encoding.UTF8, "application/problem+json") };
            if (write && (path.EndsWith("/config") || path.EndsWith("/auth-binding"))) return new(HttpStatusCode.NoContent);
            object data = path switch
            {
                "/api/v2/auth/external-sign-in/capabilities" => new { available = true, break_glass = true, connection_test = true, live_provider_changes = true },
                "/api/v2/admin/plugins/installations" => new { items = NoPlugins ? Array.Empty<object>() : Installations() },
                "/api/v2/admin/plugins/installations/3/auth-binding/test" => new { ok = false, steps = new[] { new { id = "discovery", label = "Discovery document", ok = true, message = "Synthetic discovery found" }, new { id = "groups", label = "Allowed groups", ok = false, message = "Synthetic group missing" } } },
                "/api/v2/admin/users" => new { items = new[] { new { id = "1", username = "synthetic-admin", role = "admin", enabled = true, is_owner = true, break_glass = !NoBreakGlass, password_login = true } }, page = new { has_more = false } },
                "/api/v2/admin/users/1" => new { id = "1", username = "synthetic-admin", role = "admin", enabled = true, is_owner = true, break_glass = !NoBreakGlass, password_login = true },
                "/api/v2/admin/settings/effective" => new Dictionary<string, string> { ["auth.local_password_login"] = "true", ["signup.enabled"] = "false", ["password_reset.self_service_enabled"] = "false", ["auth.email_auto_match"] = "false", ["auth.user_api_keys"] = "false", ["server.public_url"] = "https://fixture.invalid" },
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new(Etag); return response;
        }

        private static JsonObject FormField(string key, string label, string control, bool required = false, bool secret = false, bool multiline = false, string placeholder = "") =>
            new() { ["key"] = key, ["label"] = label, ["control"] = control, ["required"] = required, ["secret"] = secret, ["multiline"] = multiline, ["placeholder"] = placeholder, ["description"] = label + " help", ["options"] = new JsonArray(), ["rows"] = 0, ["show_when"] = new JsonArray() };
        private static JsonObject Schema(string key, string title, bool required, string properties, params JsonObject[] fields) =>
            new() { ["key"] = key, ["title"] = title, ["description"] = title + " entry", ["required"] = required, ["json_schema"] = "{\"type\":\"object\",\"properties\":" + properties + "}", ["admin_form"] = new JsonObject { ["fields"] = new JsonArray(fields.Select(f => (JsonNode)f).ToArray()), ["sections"] = new JsonArray() } };
        private static JsonObject Install(string id, string pluginId, bool enabled, string mode, string name, JsonArray schemas, JsonArray configs, bool bindingEnabled, bool callbackPending = false, bool connectionTest = false, string? testUser = null, string[]? managedBinding = null, JsonObject? ownership = null, string? displayName = null, bool autoProvision = true)
        {
            var metadata = new JsonObject(); if (connectionTest) metadata["connection_test"] = true; if (testUser != null) metadata["connection_test_username_field"] = testUser;
            var capability = new JsonObject { ["id"] = "cap" + id, ["type"] = "auth_provider.v1", ["sign_in_mode"] = mode, ["display_name"] = name, ["metadata"] = metadata, ["callback_url"] = mode == "oauth" ? "https://fixture.invalid/api/v2/auth/oauth/" + id + "/callback" : null, ["post_logout_redirect_url"] = mode == "oauth" ? "https://fixture.invalid/login" : null };
            if (managedBinding != null) capability["env_managed"] = new JsonArray(managedBinding.Select(m => (JsonNode)m).ToArray());
            var binding = new JsonObject { ["capability_id"] = "cap" + id, ["enabled"] = bindingEnabled, ["display_order"] = 1, ["auto_provision"] = autoProvision, ["default_login"] = false, ["callback_url"] = "", ["post_logout_redirect_url"] = "", ["callback_mode"] = "installation", ["callback_pending"] = callbackPending };
            if (managedBinding != null) binding["env_managed"] = new JsonArray(managedBinding.Select(m => (JsonNode)m).ToArray());
            if (displayName != null)
            {
                schemas.Add(Schema("display_name", "Login name", false, "{\"value\":{\"type\":\"string\"}}", FormField("value", "Name", "TEXT", placeholder: "My Provider")));
                configs.Add(new JsonObject { ["key"] = "display_name", ["value"] = new JsonObject { ["value"] = displayName }, ["configured_secrets"] = new JsonArray() });
            }
            var install = new JsonObject { ["id"] = id, ["plugin_id"] = pluginId, ["enabled"] = enabled, ["capabilities"] = new JsonArray(capability), ["auth_bindings"] = new JsonArray(binding), ["global_config_schema"] = schemas, ["global_configs"] = configs };
            if (ownership != null) install["env_managed"] = ownership;
            return install;
        }
        private object Installations()
        {
            var ownership = new JsonObject { ["config_fields"] = new JsonObject { ["oidc"] = new JsonArray("issuer", "lookup_user") }, ["binding_fields"] = new JsonObject() };
            var oidc = Schema("oidc", "Provider", true, "{\"issuer\":{\"type\":\"string\"},\"client_secret\":{\"type\":\"string\",\"writeOnly\":true},\"note\":{\"type\":\"string\"},\"lookup_user\":{\"type\":\"string\"}}",
                FormField("issuer", "Issuer URL", "TEXT", required: true, placeholder: "https://issuer.example"), FormField("client_secret", "Client secret", "PASSWORD", secret: true, multiline: true), FormField("note", "Note", "TEXT"), FormField("lookup_user", "Look-up user", "TEXT"));
            var oidcConfig = new JsonObject { ["key"] = "oidc", ["value"] = new JsonObject { ["issuer"] = "https://issuer.invalid", ["note"] = "n", ["lookup_user"] = "managed-user" }, ["configured_secrets"] = new JsonArray("client_secret") };
            var ldap = Schema("ldap", "Directory", true, "{\"bind_dn\":{\"type\":\"string\"}}", FormField("bind_dn", "Bind DN", "TEXT", required: true));
            JsonObject Plain() => Schema("plain", "Settings", false, "{\"label\":{\"type\":\"string\"}}", FormField("label", "Label", "TEXT"));
            JsonObject Tail() => Schema("tailnet", "Tailnet", false, "{\"tag\":{\"type\":\"string\"}}", FormField("tag", "Tag", "TEXT"));
            return new[]
            {
                // 3: provisioned by the deployment (enabled is owned; issuer and look-up user are owned; a secret that is also multiline).
                Install("3", "prov-oidc", true, "oauth", "Provisioned OIDC", new JsonArray(oidc), new JsonArray(oidcConfig), false, connectionTest: true, testUser: "oidc.lookup_user", managedBinding: ["enabled"], ownership: ownership),
                // 4: needs setup (a required field has no value). 5: on, and waiting for the public URL, under a hostile saved name. 6: its plugin is off.
                Install("4", "needs-ldap", true, "credentials", "Needs LDAP", new JsonArray(ldap), new JsonArray(), false),
                Install("5", "pending-oidc", true, "oauth", "Pending OIDC", new JsonArray(Plain()), new JsonArray(new JsonObject { ["key"] = "plain", ["value"] = new JsonObject { ["label"] = "x" }, ["configured_secrets"] = new JsonArray() }), true, callbackPending: true, displayName: "Evil" + Chars(0x202E) + " Name" + Chars(0x2028) + "Co"),
                Install("6", "off-plugin", false, "credentials", "Off Plugin", new JsonArray(Plain()), new JsonArray(), false),
                // 7: a network sign-in whose account creation is owned by the deployment.
                Install("7", "tailnet", true, "network", "Tailnet", new JsonArray(Tail()), new JsonArray(), true, managedBinding: ["auto_provision"]),
                // 8: a network sign-in nothing owns, whose saved state another session can change while the page is open.
                Install("8", "tailnet-two", true, "network", "Tailnet Two", new JsonArray(Tail()), new JsonArray(), Net8Enabled, autoProvision: Net8Auto),
            };
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server(); using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" }); auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        (typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Views.Admin.AdminSignInSecurityRegistration") ?? throw new InvalidOperationException("Missing registration")).GetMethod("AddAdminSignInSecurity")!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var globals = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var priorServices = globals.GetValue(null); var priorContent = window.Content; var outer = new Frame(); var visited = new List<FrameworkElement>();
        try
        {
            globals.SetValue(null, services); window.Content = outer; navigation.Frame = outer; window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer.NavigationFailed += (_, e) => Program.Log("NAVFAIL " + e.SourcePageType.Name + ": " + e.Exception);
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
            Program.Log("BLOCK provisioned sign-in providers");

            // ── Loading, every provider state, and the break-glass sentence ──
            server.NoBreakGlass = true;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.Gates["GET /api/v2/admin/plugins/installations"] = gate;
            var page = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Controller(page).Loaded && HasText(page, "Silo passwords"));
            Check(Controller(page).Busy && !Descendants(page).OfType<RadioButtons>().Any(), "while the provider read is out the provider choice is not drawn");
            server.Gates.Remove("GET /api/v2/admin/plugins/installations"); gate.SetResult();
            await Until(() => Descendants(page).OfType<RadioButtons>().Any() && Descendants(page).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            var breakGlass = Descendants(page).OfType<TextBlock>().Single(t => Id(t) == "SignInBreakGlass");
            Check(breakGlass.Text == "No break-glass admin yet. Turn on Break-glass account on an admin's user page.", "the no-break-glass warning is one sentence in one text element");
            Check(Descendants(page).OfType<TextBlock>().Count(t => t.Text.Contains("No break-glass admin yet")) == 1 && !Descendants(page).OfType<TextBlock>().Any(t => t.Text == "user page" || t.Text == "."), "the warning's link and full stop are not separate pieces of the layout");
            await Until(() => Descendants(page).OfType<RadioButton>().Count() == 5); // four providers and None; the cards enter the tree one layout pass after their list
            var cards = string.Join(" | ", Descendants(page).OfType<RadioButton>().Select(r => AutomationProperties.GetName(r)));
            Check(cards.Contains("Provisioned OIDC, Off") && cards.Contains("Needs LDAP, Needs setup") && cards.Contains("Pending OIDC, Pending") && cards.Contains("Off Plugin, Plugin turned off") && cards.EndsWith("None"), "each provider state is named on its card: " + cards);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Tailnet sign-in" || t.Text == "Sign in with Tailnet"), "the network sign-in section is drawn");
            await Capture(outer, "provisioning-overview-1440.png");

            // ── The provider the deployment provisions ──
            await Pick(page, 0);
            Check(HasText(page, "Managed by the environment") && Descendants(page).OfType<TextBlock>().Any(t => Id(t) == "SignInManaged_3"), "a provisioned binding says the environment manages it");
            Check(!Buttons(page).Single(b => Id(b) == "SignInBinding_3").IsEnabled, "Turn on is not offered for a provisioned binding");
            var stepNames = Descendants(page).OfType<TextBlock>().Select(t => AutomationProperties.GetName(t)).Where(n => n.StartsWith("Step ")).ToArray();
            Check(stepNames.SequenceEqual(["Step 1: Register Silo at your provider", "Step 2: Provider", "Step 3: Test the connection"]), "the setup steps are numbered in the official order: " + string.Join(" | ", stepNames));
            var issuer = Input(page, "SignIn_3_oidc_issuer");
            Check(!issuer.IsEnabled && issuer.Text == "https://issuer.invalid", "a managed field shows its saved value and cannot be edited");
            Check(Descendants(page).OfType<PasswordBox>().Any(p => Id(p) == "SignIn_3_oidc_client_secret"), "a secret field that is also marked multiline is a masked box");
            Check(!Descendants(page).OfType<TextBox>().Any(t => Id(t) == "SignIn_3_oidc_client_secret"), "no plain text box for the secret");
            Check(Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").PlaceholderText == "Saved. Type to replace." && Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").Password == "", "a saved secret starts empty and says it is saved");
            var note = Input(page, "SignIn_3_oidc_note"); var noteKept = note; var builds = Builds(page);
            // An idle page is watched for a bounded 500 ms against the page's own count of how often the form was drawn (the defect being guarded drew it about 20 times a second).
            Check(await Quiet(page, 500) && ReferenceEquals(noteKept, Input(page, "SignIn_3_oidc_note")), "an idle page does not rebuild its form (drawn " + Builds(page) + " times, still the same box)");
            await Type(page, "SignIn_3_oidc_note", "changed note", ("3", "oidc", "note"));
            Check(ReferenceEquals(noteKept, Input(page, "SignIn_3_oidc_note")) && note.Text == "changed note" && Builds(page) == builds, "typing in a box does not rebuild the form or replace the box");
            await TypeSecret(page, "SignIn_3_oidc_client_secret", Typed, ("3", "oidc", "client_secret"));
            await ClickWhenReady(page, "SignInTest_3");
            await Until(() => HasTextContaining(page, "1 of 2 checks failed."));
            Check(HasText(page, "Allowed groups") && HasTextContaining(page, "Synthetic group missing") && HasText(page, "Discovery document"), "a failing check shows each step with its result");
            using (var test = JsonDocument.Parse(server.Bodies["POST /api/v2/admin/plugins/installations/3/auth-binding/test"]))
            {
                var sent = test.RootElement.GetProperty("config").EnumerateArray().Select(entry => entry.GetProperty("value")).ToArray();
                Check(sent.All(v => !v.TryGetProperty("issuer", out _) && !v.TryGetProperty("lookup_user", out _)), "a connection test sends no field the deployment owns");
                Check(sent.Any(v => v.TryGetProperty("client_secret", out var s) && s.GetString() == Typed), "a typed secret is staged for the test only");
            }
            Check(!server.Writes.Any(w => w.EndsWith("/config") || w.EndsWith("/auth-binding")), "a connection test never saves");
            await Until(() => Buttons(page).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            Click(page, "SignInSave"); var save = await Dialog(page); Primary(save);
            await Until(() => !save.IsLoaded && server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1);
            using (var body = JsonDocument.Parse(server.Bodies["PUT /api/v2/admin/plugins/installations/3/config"]))
            {
                var value = body.RootElement.GetProperty("value");
                Check(value.GetProperty("note").GetString() == "changed note" && !value.TryGetProperty("issuer", out _) && !value.TryGetProperty("lookup_user", out _), "a save never carries a field the deployment owns");
                Check(value.TryGetProperty("client_secret", out var secret) && secret.GetString() == Typed, "a typed secret is sent when saved");
            }
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/auth-binding") == 0, "a provisioned binding is never written");
            await Until(() => !Controller(page).Busy);
            Check(Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").Password == "" && !HasTextContaining(page, Typed), "a typed secret is erased after an accepted save");
            Scroll(page, 560); await Task.Delay(250); await Capture(outer, "provisioning-provisioned-1440.png");
            Scroll(page, 1250); await Task.Delay(250); await Capture(outer, "provisioning-provisioned-test-1440.png"); Scroll(page, 0);

            // ── Other states of the slot: needs setup, pending, plugin off, none ──
            await Pick(page, 1);
            Check(HasTextContaining(page, "Needs setup: fill in Bind DN and save to turn it on."), "a provider that needs setup says what is missing");
            Check(!Buttons(page).Single(b => Id(b) == "SignInBinding_4").IsEnabled, "Turn on waits for the setup");
            await Pick(page, 2);
            Check(Descendants(page).OfType<TextBlock>().Any(t => Id(t) == "SignInState_5" && t.Text == "Pending"), "an enabled binding waiting for the public URL is pending");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text.Contains("The redirect URI needs the server's public URL")), "a pending provider explains the missing public URL");
            await Pick(page, 3);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text.Contains("The plugin is turned off. Turn it on from its plugin page first.")) && !Buttons(page).Single(b => Id(b) == "SignInBinding_6").IsEnabled, "a provider whose plugin is off says so and cannot be turned on");
            await Pick(page, 4);
            Check(HasTextContaining(page, "is on. Turn it off to sign in with Silo passwords only."), "choosing none while a provider is on offers to turn it off");
            await Pick(page, 2);

            // ── Third-party text in a confirmation, the destructive button, and a doubled activation ──
            server.Writes.Clear();
            var turnOff = await ClickForDialog(page, "SignInBinding_5");
            var title = turnOff.Title?.ToString() ?? "";
            Check(title == "Turn off " + Evil + "?", "a hostile provider name is neutralized in the dialog title: " + title);
            Check(!title.Any(c => c == (char)0x202E || c == (char)0x2028) && !Descendants(turnOff).OfType<TextBlock>().Any(t => t.Text.Any(c => c == (char)0x202E || c == (char)0x2028)), "no direction or line-separator character reaches the confirmation");
            var primary = Buttons(turnOff).First(b => b.Name == "PrimaryButton");
            if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            {
                Check(primary.Background is SolidColorBrush { Color: { R: 0xDC, G: 0x26, B: 0x26 } }, "the destructive confirm button uses the official destructive fill");
                Check(primary.Foreground is SolidColorBrush { Color: { R: 255, G: 255, B: 255 } }, "its text is white");
            }
            // A doubled activation of the confirm button sends one request.
            var invoke = (IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke);
            invoke.Invoke(); try { invoke.Invoke(); } catch { }
            // The second activation arrives while the first is in flight or after it: both are over once the write has completed and the page has read the result.
            await Until(() => !turnOff.IsLoaded && server.Count("PUT /api/v2/admin/plugins/installations/5/auth-binding") >= 1 && !Controller(page).Busy && Controller(page).LastChange != null);
            Check(server.Count("PUT /api/v2/admin/plugins/installations/5/auth-binding") == 1, "a doubled activation sends one binding write");
            using (var body = JsonDocument.Parse(server.Bodies["PUT /api/v2/admin/plugins/installations/5/auth-binding"]))
                Check(!body.RootElement.GetProperty("enabled").GetBoolean() && body.RootElement.GetProperty("capability_id").GetString() == "cap5" && !body.RootElement.TryGetProperty("callback_mode", out _), "the binding write is the saved row with only the change, and no callback mode");
            await Until(() => !Controller(page).Busy && Controller(page).LastChange != null);
            Check(Descendants(page).OfType<TextBlock>().Any(t => Id(t) == "SignInDone_5" && t.Text.Contains("is off. Only Silo passwords sign in now.")), "the provider says in its own words that it is off");

            // ── The network sign-in: an owned switch is read-only; the other switch asks first and is sent once ──
            var autoNetwork = Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkAuto_7");
            Check(!autoNetwork.IsEnabled && Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Managed by the environment"), "a network sign-in setting the deployment owns is read-only and says so");
            var networkEnabled = Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkEnabled_7");
            server.Writes.Clear(); server.Reset.Add("PUT /api/v2/admin/plugins/installations/7/auth-binding");
            networkEnabled.IsOn = false; var networkOff = await Dialog(page); Primary(networkOff);
            await Until(() => !networkOff.IsLoaded && server.Count("PUT /api/v2/admin/plugins/installations/7/auth-binding") == 1);
            await Until(() => HasTextContaining(page, "The outcome is unknown"));
            using (var body = JsonDocument.Parse(server.Bodies["PUT /api/v2/admin/plugins/installations/7/auth-binding"]))
                Check(body.RootElement.GetProperty("default_login").ValueKind == JsonValueKind.False && body.RootElement.GetProperty("auto_provision").GetBoolean(), "a network binding write carries the saved row");
            Check(!Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkEnabled_7").IsEnabled, "an unknown outcome blocks further writes until the state is read again");
            server.Reset.Clear();
            Click(page, "AccessReload"); await Until(() => Descendants(page).OfType<ToggleSwitch>().Any(t => Id(t) == "SignInNetworkEnabled_7" && t.IsEnabled));
            Check(server.Count("PUT /api/v2/admin/plugins/installations/7/auth-binding") == 1, "the unknown outcome was not resent");

            // ── An unknown outcome erases a typed secret and keeps the visible draft; nothing is resent ──
            await Pick(page, 0);
            server.Writes.Clear(); server.Reset.Add("PUT /api/v2/admin/plugins/installations/3/config");
            var secretBox = Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret"); secretBox.Password = Typed;
            Input(page, "SignIn_3_oidc_note").Text = "unknown draft";
            await Until(() => Buttons(page).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            Click(page, "SignInSave"); var unknown = await Dialog(page); Primary(unknown);
            await Until(() => !unknown.IsLoaded && HasTextContaining(page, "The outcome is unknown") && !Controller(page).Busy);
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1, "an unknown-outcome save is sent once");
            Check(Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").Password == "" && !HasTextContaining(page, Typed), "a typed secret is erased after an unknown outcome");
            Check(Input(page, "SignIn_3_oidc_note").Text == "unknown draft", "the visible draft stays until the admin reloads");
            Check(!Buttons(page).Single(b => Id(b) == "SignInSave").IsEnabled, "writes are blocked until the state is read again");
            server.Reset.Clear(); Click(page, "AccessReload"); var unknownDiscard = await Dialog(page); Primary(unknownDiscard);
            await Until(() => !unknownDiscard.IsLoaded && Descendants(page).OfType<RadioButtons>().Any() && Buttons(page).Any(b => Id(b) == "AccessReload" && b.IsEnabled));

            // ── A refusal that means the page is out of date (the deployment now owns the field): the visible draft stays, the typed secret is erased, and the page waits for a reload ──
            await Pick(page, 0);
            server.Writes.Clear(); server.ConfigRefusal = 422; server.ConfigRefusalManaged = true;
            await Type(page, "SignIn_3_oidc_note", "refused draft", ("3", "oidc", "note"));
            await TypeSecret(page, "SignIn_3_oidc_client_secret", Typed, ("3", "oidc", "client_secret"));
            Check(Input(page, "SignIn_3_oidc_note").Focus(FocusState.Programmatic), "focus leaves the masked box, as it does when the admin goes to Save");
            await Until(() => Buttons(page).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            var refuse = await ClickForDialog(page, "SignInSave"); Primary(refuse);
            await Until(() => !refuse.IsLoaded && HasTextContaining(page, "Set by the environment. Change it there and restart the affected service.") && !Controller(page).Busy);
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1 && !HasTextContaining(page, "synthetic server text"), "a refused write is sent once and shows the reason without the server's own text");
            Check(HasTextContaining(page, "Reload the page to read the current state."), "the message says that a reload is needed and why");
            Check(Input(page, "SignIn_3_oidc_note").Text == "refused draft", "a refused write keeps the visible draft");
            var lockedSecret = Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret");
            Check(lockedSecret.Password == "" && !HasTextContaining(page, Typed), "the typed secret is erased when the page locks (the masked box itself is empty)");
            Check(!Input(page, "SignIn_3_oidc_note").IsEnabled && !Buttons(page).Single(b => Id(b) == "SignInSave").IsEnabled, "the page waits for a reload: the fields and Save are off");
            server.ConfigRefusal = 0; Click(page, "AccessReload"); var discard = await Dialog(page); Primary(discard);
            await Until(() => !discard.IsLoaded && Descendants(page).OfType<RadioButtons>().Any() && Buttons(page).Any(b => Id(b) == "AccessReload" && b.IsEnabled));

            // ── A plain refusal (422, nothing applied): the page stays editable, the draft and the masked secret stay, a corrected save is accepted and then erases the secret ──
            await Pick(page, 0);
            server.Writes.Clear(); server.ConfigRefusal = 422; server.ConfigRefusalManaged = false;
            await Type(page, "SignIn_3_oidc_note", "plain refused draft", ("3", "oidc", "note"));
            await TypeSecret(page, "SignIn_3_oidc_client_secret", Typed, ("3", "oidc", "client_secret"));
            Check(Input(page, "SignIn_3_oidc_note").Focus(FocusState.Programmatic), "focus leaves the masked box, as it does when the admin goes to Save");
            await Until(() => Buttons(page).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            var plain = await ClickForDialog(page, "SignInSave"); Primary(plain);
            await Until(() => !plain.IsLoaded && HasTextContaining(page, "The server rejected these values.") && !Controller(page).Busy);
            var plainNote = Input(page, "SignIn_3_oidc_note"); var plainSecret = Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret");
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1 && !HasTextContaining(page, "synthetic server text"), "a plain refusal is sent once and shows the reason without the server's own text");
            Check(!Controller(page).ReviewRequired && plainNote.IsEnabled && plainSecret.IsEnabled, "a plain refusal leaves the fields enabled");
            Check(plainNote.Text == "plain refused draft" && plainSecret.Password == Typed, "the typed value and the masked secret stay after a plain refusal");
            Check(!HasTextContaining(page, "Reload the page to read the current state.") && Buttons(page).Single(b => Id(b) == "SignInSave").IsEnabled, "a plain refusal does not ask for a reload and Save is offered again");
            server.ConfigRefusal = 0;
            await Type(page, "SignIn_3_oidc_note", "corrected draft", ("3", "oidc", "note"));
            await Until(() => Buttons(page).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            var corrected = await ClickForDialog(page, "SignInSave"); Primary(corrected);
            await Until(() => !corrected.IsLoaded && server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 2 && !Controller(page).Busy);
            using (var body = JsonDocument.Parse(server.Bodies["PUT /api/v2/admin/plugins/installations/3/config"]))
            {
                var value = body.RootElement.GetProperty("value");
                Check(value.GetProperty("note").GetString() == "corrected draft" && value.TryGetProperty("client_secret", out var kept) && kept.GetString() == Typed, "a corrected save is accepted and carries the corrected value and the secret typed before the refusal");
            }
            Check(Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").Password == "" && !HasTextContaining(page, Typed), "the accepted save erases the typed secret");

            // ── Retry after a failed plugin read asks before discarding staged edits; declining keeps them ──
            server.FailInstallations = true;
            page = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Controller(page).Loaded && Controller(page).ProviderError != null && Buttons(page).Any(b => Id(b) == "SignInRetry" && b.IsEnabled));
            Check(HasTextContaining(page, "Couldn't read the installed plugins."), "a failed plugin read says so and offers Retry");
            Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignIn_signup.enabled").IsOn = true;
            await Until(() => Controller(page).DirtyCount == 1);
            var readsBefore = server.Reads.GetValueOrDefault("GET /api/v2/admin/plugins/installations");
            var ask = await ClickForDialog(page, "SignInRetry");
            Check((ask.Title?.ToString() ?? "") == "Unsaved changes", "Retry asks before discarding staged edits");
            Decline(ask); await Until(() => !ask.IsLoaded);
            Check(Controller(page).DirtyCount == 1 && Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignIn_signup.enabled").IsOn && server.Reads.GetValueOrDefault("GET /api/v2/admin/plugins/installations") == readsBefore && Buttons(page).Any(b => Id(b) == "SignInRetry"), "declining keeps the staged edit and reads nothing");
            server.FailInstallations = false;
            var accept = await ClickForDialog(page, "SignInRetry"); Primary(accept);
            await Until(() => !accept.IsLoaded && Descendants(page).OfType<RadioButtons>().Any() && !Controller(page).Busy);
            Check(Controller(page).DirtyCount == 0 && Controller(page).ProviderError == null && !Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignIn_signup.enabled").IsOn, "accepting discards the staged edit and reads the plugins again");

            // ── No sign-in plugin installed: one sentence, the two plugin names bold, the catalog a link ──
            server.NoPlugins = true;
            page = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(page).OfType<TextBlock>().Any(t => Id(t) == "SignInPluginCatalog"));
            var noPlugins = Descendants(page).OfType<TextBlock>().Single(t => Id(t) == "SignInPluginCatalog"); var noRuns = noPlugins.Inlines.OfType<Run>().ToArray();
            Check(noPlugins.Text.StartsWith("No sign-in plugin is installed. Install OpenID Connect Sign-in or LDAP Sign-in from the plugin catalog"), "the no-plugin sentence is one text element: " + noPlugins.Text);
            Check(noRuns.Any(r => r.Text == "OpenID Connect Sign-in" && r.FontWeight.Weight >= 500) && noRuns.Any(r => r.Text == "LDAP Sign-in" && r.FontWeight.Weight >= 500) && noPlugins.Inlines.OfType<Hyperlink>().Count() == 1, "the two plugin names are bold runs and the catalog is one link");
            server.NoPlugins = false;
            page = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(page).OfType<RadioButtons>().Any() && !Controller(page).Busy);

            // ── The live preview follows the login-name box while it is typed and never shows another field ──
            await Pick(page, 2);
            var preview = Descendants(page).OfType<TextBlock>().Single(t => Id(t) == "SignInPreview_5");
            Check(preview.Text == "Sign in with " + Evil, "the preview starts from the saved name, neutralized: " + preview.Text);
            await Type(page, "SignIn_5_display_name_value", "Acme Cloud", ("5", "display_name", "value"), typed => preview.Text == "Sign in with " + (typed.Trim().Length == 0 ? "My Provider" : typed.Trim()));
            Check(preview.Text == "Sign in with Acme Cloud", "the preview shows the typed name: " + preview.Text);
            var previewKept = preview.Text;
            await Type(page, "SignIn_5_plain_label", "OTHER-FIELD-VALUE", ("5", "plain", "label"));
            Check(preview.Text == previewKept && !HasTextContaining(page, "Sign in with OTHER"), "typing in another field never reaches the preview");
            Click(page, "SignInDiscard"); await Until(() => Controller(page).DirtyCount == 0);

            // ── Another session changed network sign-in while this page was open: the account-creation switch writes nothing, says so and shows the current state ──
            server.Writes.Clear();
            await Until(() => Descendants(page).OfType<ToggleSwitch>().Any(t => Id(t) == "SignInNetworkAuto_8" && t.IsEnabled));
            var enabled8 = Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkEnabled_8"); var auto8 = Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkAuto_8");
            Check(enabled8.IsOn && auto8.IsOn, "network sign-in starts on and creating accounts");
            server.Net8Enabled = false; // another session turned network sign-in off
            auto8.IsOn = false; var changed = await Dialog(page); Primary(changed);
            await Until(() => !changed.IsLoaded && HasTextContaining(page, "changed from somewhere else") && !Controller(page).Busy);
            Check(server.Count("PUT /api/v2/admin/plugins/installations/8/auth-binding") == 0, "the account-creation switch wrote nothing for a network sign-in that was changed elsewhere");
            await Until(() => !Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkEnabled_8").IsOn);
            Check(Descendants(page).OfType<ToggleSwitch>().Single(t => Id(t) == "SignInNetworkAuto_8").IsOn, "the page now shows the current state: sign-in off, account creation as saved");
            server.Net8Enabled = true;

            // ── Copy puts exactly the visible address on the clipboard ──
            await Pick(page, 0);
            var url = Input(page, "SignInUrl_Redirect");
            Check(url.Text == "https://fixture.invalid/api/v2/auth/oauth/3/callback" && url.FlowDirection == FlowDirection.LeftToRight && url.IsReadOnly, "the redirect address is read-only text, left to right");
            Click(page, "SignInCopy_Redirect");
            await Until(() => HasText(page, "Copied") || HasTextContaining(page, "Couldn't copy"));
            Check(HasText(page, "Copied"), "copy says it copied");
            var copied = await Clipboard.GetContent().GetTextAsync();
            Check(copied == url.Text, "the clipboard holds exactly the visible address");
            Clipboard.Clear();

            // ── Layout at narrow widths: captures ──
            await Pick(page, 0);
            foreach (var width in new[] { 1024, 800, 560 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); await Task.Delay(400);
                Scroll(page, 0); await Task.Delay(150); await Capture(outer, "provisioning-top-" + width + ".png");
                Scroll(page, width >= 1024 ? 560 : 900); await Task.Delay(250); await Capture(outer, "provisioning-provisioned-" + width + ".png"); Scroll(page, 0);
            }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(300);

            // ── Left to right values on a right-to-left page ──
            outer.FlowDirection = FlowDirection.RightToLeft;
            page = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(page).OfType<RadioButtons>().Any() && Descendants(page).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            await Pick(page, 0);
            Check(Input(page, "SignInUrl_Redirect").FlowDirection == FlowDirection.LeftToRight && Input(page, "SignIn_3_oidc_issuer").FlowDirection == FlowDirection.LeftToRight && Input(page, "SignInLookup_3").FlowDirection == FlowDirection.LeftToRight, "setup values and addresses read left to right on a right-to-left page");
            Check(Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == "SignIn_3_oidc_client_secret").FlowDirection == FlowDirection.LeftToRight, "a masked value reads left to right");
            outer.FlowDirection = FlowDirection.LeftToRight;
            Program.Log("BLOCK end provisioned sign-in providers");
        }
        finally
        {
            if (outer.XamlRoot != null) foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>()) dialog.Hide();
            outer.FlowDirection = FlowDirection.LeftToRight; outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null;
            await Until(() => visited.All(page => !page.IsLoaded));
            if (outer.XamlRoot != null) await Until(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().Any());
            window.Content = priorContent; globals.SetValue(null, priorServices);
        }
    }

    private static int Builds(FrameworkElement page) => ((AdminSignInSettingsPage)page).FormBuildCount;
    /// <summary>Watches for a bounded time that the form is not drawn again; false as soon as it is.</summary>
    private static async Task<bool> Quiet(FrameworkElement page, int milliseconds)
    {
        var before = Builds(page);
        for (var waited = 0; waited < milliseconds; waited += 25) { if (Builds(page) != before) return false; await Task.Delay(25); }
        return Builds(page) == before;
    }
    /// <summary>Completes once the UI thread has run everything that was queued before this call (a re-render scheduled by an edit has then happened).</summary>
    private static Task FlushUi(FrameworkElement page)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!page.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.TrySetResult())) done.TrySetResult();
        return done.Task;
    }
    private static string Staged(FrameworkElement page, (string Provider, string Schema, string Field) at)
    {
        var controller = Controller(page); var provider = controller.Providers.First(p => p.Id == at.Provider); var schema = provider.GlobalConfigSchema.First(s => s.Key == at.Schema);
        return controller.Values(provider, schema)[at.Field]?.ToString() ?? "";
    }
    /// <summary>
    /// Types into a FOCUSED text box one character at a time at the caret (SelectedText inserts like typing: the caret moves on and the box keeps focus), and after each character waits for the page
    /// to have staged it and re-rendered. The box must stay the same box, keep its text, its caret at the end and keyboard focus, and the form must not be drawn again. Global keyboard input is not used:
    /// it would reach the owner's desktop.
    /// </summary>
    private static async Task Type(FrameworkElement page, string id, string text, (string Provider, string Schema, string Field) at, Func<string, bool>? shown = null)
    {
        var box = Input(page, id); var builds = Builds(page); string? failure = null;
        Check(box.Focus(FocusState.Programmatic) && box.FocusState != FocusState.Unfocused, id + " takes keyboard focus");
        box.SelectAll(); box.SelectedText = ""; await FlushUi(page);
        if (shown != null) await Until(() => shown(""));
        var expected = "";
        foreach (var ch in text)
        {
            box.Select(box.Text.Length, 0); box.SelectedText = ch.ToString(); expected += ch;
            await UntilStaged(page, at, expected, id); if (shown != null) await Until(() => shown(expected)); await FlushUi(page);
            if (!ReferenceEquals(box, Input(page, id))) failure ??= "the box was replaced after '" + expected + "'";
            else if (box.Text != expected) failure ??= "the text changed to '" + box.Text + "' after '" + expected + "'";
            else if (box.SelectionStart + box.SelectionLength != expected.Length) failure ??= "the caret moved to " + box.SelectionStart + "+" + box.SelectionLength + " after '" + expected + "'";
            else if (box.FocusState == FocusState.Unfocused) failure ??= "focus was lost after '" + expected + "'";
            else if (Builds(page) != builds) failure ??= "the form was drawn again after '" + expected + "'";
        }
        Check(failure == null, "typing into " + id + " keeps the box, its text, the caret and focus" + (failure == null ? "" : ": " + failure));
    }
    /// <summary>
    /// The masked box cannot take key presses without global input, and the test host ignores a Password assigned while the box has focus: its password is extended one character at a time
    /// while it is not focused. The same box must keep its value and the form must not be drawn again; afterwards the box takes focus and must keep it, and its value, while the page stays idle.
    /// </summary>
    private static async Task TypeSecret(FrameworkElement page, string id, string text, (string Provider, string Schema, string Field) at)
    {
        PasswordBox Box() => Descendants(page).OfType<PasswordBox>().Single(p => Id(p) == id);
        var box = Box(); var builds = Builds(page); string? failure = null;
        var expected = "";
        foreach (var ch in text)
        {
            expected += ch; box.Password = expected;
            await UntilStaged(page, at, expected, id); await FlushUi(page);
            if (!ReferenceEquals(box, Box())) failure ??= "the secret box was replaced";
            else if (box.Password != expected) failure ??= "the masked value changed";
            else if (Builds(page) != builds) failure ??= "the form was drawn again";
        }
        Check(box.Focus(FocusState.Programmatic) && box.FocusState != FocusState.Unfocused, id + " takes keyboard focus");
        Check(await Quiet(page, 200), "the form is not drawn again after the secret box takes focus");
        await FlushUi(page);
        if (!ReferenceEquals(box, Box()) || box.Password != expected || box.FocusState == FocusState.Unfocused) failure ??= "the box lost its value or focus once focused";
        Check(failure == null, "typing into the masked " + id + " keeps the box, its value and focus" + (failure == null ? "" : ": " + failure));
    }
    /// <summary>Waits until the page has staged exactly what was typed; on a timeout says what the page and the control held.</summary>
    private static async Task UntilStaged(FrameworkElement page, (string Provider, string Schema, string Field) at, string expected, string id)
    {
        for (var i = 0; i < 240; i++) { if (Staged(page, at) == expected) return; await Task.Delay(25); }
        var control = Descendants(page).FirstOrDefault(d => d is Control c && Id(c) == id) as Control; var controller = Controller(page);
        throw new TimeoutException($"Provisioning fixture: {id} never staged '{expected}' (staged length {Staged(page, at).Length}, control {(control is PasswordBox p ? "password length " + p.Password.Length : control is TextBox b ? "text length " + b.Text.Length : "missing")}, enabled {control?.IsEnabled}, busy {controller.Busy}, review {controller.ReviewRequired}, dirty {controller.DirtyCount})");
    }
    private static void Decline(ContentDialog dialog)
    {
        var button = Buttons(dialog).First(b => b.Name == "CloseButton");
        Check(button.IsEnabled, "dialog close action enabled"); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static string Chars(params int[] codes) => string.Concat(codes.Select(char.ConvertFromUtf32));
    private static void Scroll(FrameworkElement page, double offset)
    {
        DependencyObject? node = (DependencyObject?)page.GetType().GetProperty("ScrollContent")!.GetValue(page);
        while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        Check(node is ScrollViewer, "page scroller found"); ((ScrollViewer)node!).ChangeView(null, offset, null, true);
    }
    private static FrameworkElement? Scope;
    private static SiloPlayer.Core.Services.Admin.AdminSignInController Controller(FrameworkElement page) => ((AdminSignInSettingsPage)page).Controller;
    private static string Id(DependencyObject element) => AutomationProperties.GetAutomationId(element);
    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants(root).OfType<Button>();
    private static bool HasText(DependencyObject root, string text) => Descendants(root).OfType<TextBlock>().Any(t => t.Text == text);
    private static bool HasTextContaining(DependencyObject root, string text) => Descendants(root).OfType<TextBlock>().Any(t => t.Text.Contains(text, StringComparison.Ordinal));
    private static async Task Pick(FrameworkElement page, int index)
    {
        await Until(() => Descendants(page).OfType<RadioButton>().Count() == 5); await Task.Delay(100); // realized, and past the control's own re-assertion of its selection
        var choices = Descendants(page).OfType<RadioButtons>().Single();
        if (choices.SelectedIndex == index) return;
        var before = Descendants(page).OfType<Button>().Select(b => b).FirstOrDefault(b => Id(b).StartsWith("SignInBinding_"));
        choices.SelectedIndex = index;
        await Until(() => !ReferenceEquals(before, Descendants(page).OfType<Button>().FirstOrDefault(b => Id(b).StartsWith("SignInBinding_"))) || index == 4);
        await Task.Delay(150);
    }
    private static async Task<FrameworkElement> Open(Frame outer, string href, string pageName, List<FrameworkElement> visited)
    {
        var resolved = AdminShellPage.TryOpenHref(href);
        Check(resolved?.Kind == SiloPlayer.Core.Services.Admin.AdminRouteResolutionKind.Route, "production route " + href);
        await Until(() => outer.Content is AdminShellPage);
        var shell = (AdminShellPage)outer.Content; var frame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        await Until(() => frame.Content?.GetType().Name == pageName && ((FrameworkElement)frame.Content).IsLoaded);
        Check(frame.ContentTransitions == null || frame.ContentTransitions.Count == 0, "no navigation slide");
        Scope = (FrameworkElement)frame.Content; visited.Add(Scope); return Scope;
    }
    private static async Task<ContentDialog> Dialog(FrameworkElement page)
    {
        ContentDialog? dialog = null; await Until(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault()) != null); return dialog!;
    }
    /// <summary>Presses once and waits for the dialog: a click that lands while the page is still closing a dialog is dropped, so a press is repeated only if no dialog opened.</summary>
    private static async Task<ContentDialog> ClickForDialog(FrameworkElement page, string id)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await ClickWhenReady(page, id);
            for (var i = 0; i < 20; i++)
            {
                var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault();
                if (dialog != null) return dialog;
                await Task.Delay(25);
            }
        }
        throw new TimeoutException("Provisioning fixture: no dialog opened for " + id);
    }
    private static TextBox Input(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().Single(t => Id(t) == id);
    private static void Click(DependencyObject root, string id)
    {
        var button = Buttons(root).Single(t => Id(t) == id);
        Check(button.IsEnabled, id + " enabled"); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static async Task ClickWhenReady(DependencyObject root, string id) { await Until(() => Buttons(root).Any(t => Id(t) == id && t.IsEnabled)); Click(root, id); }
    private static void Primary(ContentDialog dialog)
    {
        var button = Buttons(dialog).First(b => b.Name == "PrimaryButton");
        Check(button.IsEnabled, "dialog primary action enabled"); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Until(Func<bool> predicate, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 240; i++) { if (predicate()) return; await Task.Delay(25); }
        var texts = Scope == null ? "" : string.Join(" | ", Descendants(Scope).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).Take(120));
        throw new TimeoutException($"Provisioning fixture readiness at line {line}: {texts}");
    }
    private static void Check(bool value, string name) { Program.Log((value ? "PASS " : "FAIL ") + name); if (!value) throw new InvalidOperationException(name); }
    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
