using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Real production registration → shell → Frame → controls/dialogs. Only synthetic identities/secrets.</summary>
internal static class AdminSignInSecurityNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Writes = [];
        internal bool Local = true, Revoked, PolicyEnabled;
        internal string Tier = "standard";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var write = request.Method != HttpMethod.Get;
            if (write) Writes.Add(request.Method + " " + path);
            if (write && (path.EndsWith("/config") || path.EndsWith("/auth-binding"))) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/tier") && write)
            {
                Check(request.Headers.GetValues("If-Match").Single() == "\"synthetic-access-1\"", "tier uses captured strong validator");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Tier = body.RootElement.GetProperty("rate_tier").GetString()!;
            }
            object data;
            if (path == "/api/v2/admin/api-keys" && request.Method == HttpMethod.Post)
                data = new { id = "7", user_id = "1", label = "Fixture integration", key = "sa_SYNTHETIC_NATIVE_ONLY", rate_tier = "standard", scopes = new[] { "catalog:read" }, created_at = "2026-11-17T00:00:00Z" };
            else if (request.Method == HttpMethod.Delete) { Check(request.Headers.GetValues("If-Match").Single() == "\"synthetic-access-1\"", "revoke uses captured strong validator"); Revoked = true; return new(HttpStatusCode.NoContent); }
            else if (path == "/api/v2/admin/settings" && write)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var values = body.RootElement.GetProperty("values");
                if (values.TryGetProperty("auth.local_password_login", out var local)) Local = local.GetString() != "false";
                data = new { values = JsonSerializer.Deserialize<Dictionary<string, string>>(values.GetRawText()), restart_required = false };
            }
            else data = path switch
            {
                "/api/v2/admin/api-keys/capabilities" => new { available = true, guarded_configuration = true, scopes = new[] { new { name = "catalog:read", description = "Synthetic catalog reads", audience = "user" } }, rate_tiers = new[] { "standard", "elevated" } },
                "/api/v2/admin/api-keys" => new { items = Revoked ? Array.Empty<object>() : new[] { Key() }, page = new { has_more = false } },
                "/api/v2/admin/api-keys/7" or "/api/v2/admin/api-keys/7/tier" => Key(),
                "/api/v2/admin/users" => new { items = new[] { Account() }, page = new { has_more = false } },
                "/api/v2/admin/users/1" => Account(),
                "/api/v2/auth/external-sign-in/capabilities" => new { available = true, break_glass = true, connection_test = true, live_provider_changes = true },
                "/api/v2/admin/plugins/installations" => new { items = new[] { Provider() } },
                "/api/v2/admin/plugins/installations/3/auth-binding/test" => new { ok = true, steps = new[] { new { id = "groups", label = "Allowed groups", ok = true, message = "Synthetic directory group verified" } } },
                "/api/v2/admin/settings/effective" => new Dictionary<string, string> { ["auth.local_password_login"] = Local ? "true" : "false", ["signup.enabled"] = "false", ["password_reset.self_service_enabled"] = "false", ["auth.email_auto_match"] = "false", ["auth.user_api_keys"] = "false", ["server.public_url"] = "https://fixture.invalid" },
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/policy/capability" => new { enabled = true, editor_available = true, decision_types = new[] { "permission", "scope", "action" }, generation = 1, degraded = false, eval_timeouts = 0 },
                "/api/v2/admin/policy/documents" => new { items = new[] { Document() }, page = new { has_more = false } },
                "/api/v2/admin/policy/documents/9" => Document(),
                "/api/v2/admin/policy/documents/9/versions" => write ? Version(true) : new { items = new[] { Version(false) }, page = new { has_more = false } },
                "/api/v2/admin/policy/documents/9/versions/11" => Version(true),
                "/api/v2/admin/policy/validate" => new { compiled_ok = true, errors = Array.Empty<object>() },
                "/api/v2/admin/policy/simulate" => new { decision = new { allowed = false, reason = "synthetic" }, eval_time_ns = 12, generation = 1 },
                "/api/v2/admin/policy/vendor" => new { items = new[] { new { path = "permission.rego", source = "package silo.permission\nimport rego.v1\ndefault allowed := false" } } },
                "/api/v2/admin/policy/decisions" => new { items = new[] { new { id = "12", timestamp = "2026-11-17T00:00:00Z", decision_name = "silo.permission.decision", policy_generation = 1, allowed = false, eval_time_ns = 12, input_digest = "synthetic" } }, page = new { has_more = false } },
                "/api/v2/admin/policy/decisions/12" => new { id = "12", timestamp = "2026-11-17T00:00:00Z", decision_name = "silo.permission.decision", policy_generation = 1, allowed = false, eval_time_ns = 12, input_digest = "synthetic", input_sample = new { fixture = true }, result_sample = new { allowed = false } },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            var response = new HttpResponseMessage(write && request.Method == HttpMethod.Post && !path.EndsWith("test") && !path.EndsWith("validate") && !path.EndsWith("simulate") ? HttpStatusCode.Created : HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"synthetic-access-1\""); return response;
        }
        private object Key() => new { id = "7", user_id = "1", label = "Fixture integration", username = "synthetic-admin", key_prefix = "fixture", rate_tier = Tier, scopes = new[] { "catalog:read" }, created_at = "2026-11-17T00:00:00Z" };
        private static object Account() => new { id = "1", username = "synthetic-admin", role = "admin", enabled = true, is_owner = true, break_glass = true, password_login = true };
        private object Document() => new { id = "9", domain = "permission", name = "Fixture override", enabled = PolicyEnabled, active_version_id = (string?)null, created_at = "2026-11-17T00:00:00Z", updated_at = "2026-11-17T00:00:00Z" };
        private static object Version(bool source)
        {
            var version = new Dictionary<string, object?> { ["id"] = "11", ["document_id"] = "9", ["version_number"] = 1, ["source_sha256"] = "synthetic", ["compiled_ok"] = true, ["created_at"] = "2026-11-17T00:00:00Z" };
            if (source) version["source"] = "package silo_custom.permission\n\nimport rego.v1\n\noverride(base_decision, input) := base_decision if {\n  input.schema_version == 1\n}\n";
            return version;
        }
        private static object Provider() => new
        {
            id = "3", plugin_id = "fixture-ldap", enabled = true,
            capabilities = new[] { new { id = "ldap", type = "auth_provider.v1", display_name = "LDAP", sign_in_mode = "credentials", metadata = new { connection_test = true } } },
            auth_bindings = new[] { new { capability_id = "ldap", enabled = true, display_order = 1, auto_provision = true, default_login = false } },
            global_config_schema = new[] { new { key = "ldap", title = "Directory", description = "Synthetic directory", required = true, json_schema = "{\"type\":\"object\",\"properties\":{\"allowed_groups\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"bind_password\":{\"type\":\"string\"}}}", admin_form = new { fields = new object[] { new { key = "allowed_groups", label = "Allowed groups", control = "TEXTAREA", required = false, secret = false }, new { key = "bind_password", label = "Bind password", control = "PASSWORD", required = false, secret = true } }, sections = Array.Empty<object>() } } },
            global_configs = new[] { new { key = "ldap", value = new { allowed_groups = new[] { "fixture-readers" } }, configured_secrets = new[] { "bind_password" } } },
        };
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
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Views.Admin.AdminSignInSecurityRegistration") ?? throw new InvalidOperationException("Missing production Sign-in & Security registration");
        registration.GetMethod("AddAdminSignInSecurity")!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var globals = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var priorServices = globals.GetValue(null); var priorContent = window.Content; var outer = new Frame();
        var visited = new List<FrameworkElement>();
        try
        {
            globals.SetValue(null, services); window.Content = outer; navigation.Frame = outer; window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
            var keys = await Open(outer, "/admin/api-keys", "AdminApiKeysPage");
            visited.Add(keys);
            await Until(() => Descendants(keys).OfType<TextBlock>().Any(t => t.Text == "Fixture integration"));
            await Capture(outer, "access-api-keys.png");
            Click(keys, "ApiKeysCreate"); var create = await Dialog(keys);
            Input(create, "ApiKeyLabel").Text = "Fixture integration";
            Click(create, "ApiKeyCreateSubmit");
            await Until(() => Descendants(create).OfType<TextBlock>().Any(t => t.Text == "sa_SYNTHETIC_NATIVE_ONLY"));
            Check(server.Writes.Count(w => w == "POST /api/v2/admin/api-keys") == 1, "create sent once and disclosed in active dialog");
            create.Hide(); await Until(() => !create.IsLoaded);
            Click(keys, "ApiKeyTier_7"); var edit = await Dialog(keys);
            var tier = Descendants(edit).OfType<ComboBox>().Single(t => AutomationProperties.GetAutomationId(t) == "ApiKeyTier");
            tier.SelectedItem = tier.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == "elevated");
            Primary(edit, "Save rate limit"); await Until(() => !edit.IsLoaded && server.Tier == "elevated");
            Check(server.Writes.Count(w => w == "PUT /api/v2/admin/api-keys/7/tier") == 1, "reviewed tier saved once");
            Click(keys, "ApiKeyRevoke_7"); var revoke = await Dialog(keys); Primary(revoke, "Revoke");
            await Until(() => !revoke.IsLoaded && server.Revoked); Check(server.Writes.Count(w => w == "DELETE /api/v2/admin/api-keys/7") == 1, "confirmed revoke sent once");
            var signIn = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage");
            visited.Add(signIn);
            await Until(() => Descendants(signIn).OfType<ToggleSwitch>().Any(t => AutomationProperties.GetAutomationId(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            Check(Descendants(signIn).OfType<TextBlock>().Any(t => t.Text == "Allowed groups"), "manifest group restriction rendered");
            Click(signIn, "SignInTest_3"); await Until(() => Descendants(signIn).OfType<TextBlock>().Any(t => t.Text.Contains("Synthetic directory group verified")));
            Check(!server.Writes.Any(w => w.EndsWith("/config")), "connection test never saves config");
            await Capture(outer, "access-sign-in.png");
            Descendants(signIn).OfType<ScrollViewer>().First(s => AutomationProperties.GetName(s) == "Sign-in").ChangeView(null, 650, null, true);
            await Task.Delay(100); await Capture(outer, "access-sign-in-provider.png");
            var policy = await Open(outer, "/admin/policy?document=9", "AdminPolicyPage");
            visited.Add(policy);
            await Until(() => Descendants(policy).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == "PolicySource"));
            server.PolicyEnabled = true;
            Click(policy, "PolicyReview");
            await Until(() => Descendants(policy).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "PolicyAdopt"));
            Click(policy, "PolicyAdopt");
            await Until(() => Descendants(policy).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "PolicyEnabled" && button.Content?.ToString() == "Disable override"));
            Check(true, "adopted enabled state refreshes the toggle label");
            Click(policy, "PolicyEnabled");
            var enableDialog = await Dialog(policy);
            Check(enableDialog.Title?.ToString() == "Disable override", "toggle confirmation describes the current snapshot target");
            enableDialog.Hide();
            await Until(() => !enableDialog.IsLoaded);
            Click(policy, "PolicyValidate"); await Until(() => Descendants(policy).OfType<TextBlock>().Any(t => t.Text.StartsWith("Validation passed for the submitted source.", StringComparison.Ordinal)));
            Click(policy, "PolicySimulate"); await Until(() => Descendants(policy).OfType<TextBlock>().Any(t => t.Text.Contains("synthetic")));
            await Capture(outer, "access-policy-editor.png");
            Descendants(policy).OfType<ScrollViewer>().First(s => AutomationProperties.GetName(s) == "Policy").ChangeView(null, 950, null, true);
            await Task.Delay(100); await Capture(outer, "access-policy-test.png");
            Click(policy, "PolicyTab_vendor"); await Until(() => Descendants(policy).OfType<TextBlock>().Any(t => t.Text == "permission.rego"));
            Descendants(policy).OfType<ScrollViewer>().First(s => AutomationProperties.GetName(s) == "Policy").ChangeView(null, 0, null, true);
            await Capture(outer, "access-policy-baseline.png");
            Click(policy, "PolicyTab_decisions"); await Until(() => Descendants(policy).OfType<Button>().Any(t => AutomationProperties.GetAutomationId(t) == "PolicyDecision_12"));
            Click(policy, "PolicyDecision_12"); await Until(() => Descendants(policy).OfType<TextBlock>().Any(t => t.Text.Contains("fixture")));
            await Capture(outer, "access-policy-decisions.png");
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user" });
            await Until(() => !Descendants(policy).OfType<TextBox>().Any(t => t.Text.Contains("package silo")));
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.Contains("sa_SYNTHETIC_NATIVE_ONLY")), "retired page contains no show-once key");
        }
        finally
        {
            foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>()) dialog.Hide();
            outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null;
            await Until(() => visited.All(page => !page.IsLoaded));
            await Until(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().Any());
            window.Content = priorContent; globals.SetValue(null, priorServices);
        }
    }
    private static async Task<FrameworkElement> Open(Frame outer, string href, string pageName)
    {
        outer.NavigationFailed += (_, e) => Program.Log("NAVFAIL " + e.SourcePageType.Name + ": " + e.Exception);
        var resolved = AdminShellPage.TryOpenHref(href); Program.Log("resolution " + resolved?.Kind);
        Check(resolved?.Kind == SiloPlayer.Core.Services.Admin.AdminRouteResolutionKind.Route, "production route " + href);
        await Until(() => outer.Content is AdminShellPage);
        var shell = (AdminShellPage)outer.Content; var frame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        await Until(() => frame.Content?.GetType().Name == pageName && ((FrameworkElement)frame.Content).IsLoaded);
        Check(frame.ContentTransitions == null || frame.ContentTransitions.Count == 0, "no navigation slide"); return (FrameworkElement)frame.Content;
    }
    private static async Task<ContentDialog> Dialog(FrameworkElement page)
    {
        ContentDialog? dialog = null; await Until(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault()) != null); return dialog!;
    }
    private static TextBox Input(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().Single(t => AutomationProperties.GetAutomationId(t) == id);
    private static void Click(DependencyObject root, string id)
    {
        var button = Descendants(root).OfType<Button>().Single(t => AutomationProperties.GetAutomationId(t) == id);
        Check(button.IsEnabled, id + " enabled"); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static void Primary(ContentDialog dialog, string label)
    {
        var button = Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == label);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Until(Func<bool> predicate) { for (var i = 0; i < 240; i++) { if (predicate()) return; await Task.Delay(25); } throw new TimeoutException("Sign-in & Security fixture readiness"); }
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
