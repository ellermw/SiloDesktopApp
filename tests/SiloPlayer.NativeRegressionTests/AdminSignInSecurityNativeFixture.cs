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

/// <summary>
/// Production registration → Admin shell → nested Frame → real pages, controls and dialogs for Sign-in, API Keys and Policy.
/// Only synthetic identities and secrets. Every mutation is counted so a replay would fail the run.
/// </summary>
internal static class AdminSignInSecurityNativeFixture
{
    private const string Etag = "\"synthetic-access-1\"";
    private const string Secret = "sa_SYNTHETIC_NATIVE_ONLY";

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<string> Writes = [];
        internal readonly Dictionary<string, string> Bodies = [];
        internal readonly Dictionary<string, string> Guards = [];
        internal readonly Dictionary<string, TaskCompletionSource> Gates = [];
        internal readonly Dictionary<string, int> Status = [];
        internal readonly HashSet<string> Reset = [];
        internal readonly List<string> Gets = [];
        // API keys
        internal bool KeysAvailable = true, KeysEmpty, Revoked, CreatedKeyVisibleToServer;
        internal string Tier = "standard";
        // Sign-in
        internal bool Local = true, Signup, LaterPageBreakGlass, ManyAccountPages, ExternalUnavailable;
        internal int PageSize = 25;
        // Policy
        internal bool PolicyOn = true;
        internal readonly List<(string Id, string Name, bool Enabled, string? Active)> Docs = [("9", "Fixture override", false, null)];
        internal string? RenamedNine;
        internal int Generation = 1;

        internal int Count(string write) => Writes.Count(w => w == write);
        internal string Body(string write) => Bodies.GetValueOrDefault(write, "");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var key = request.Method + " " + path;
            var write = request.Method != HttpMethod.Get;
            if (write) { Writes.Add(key); Bodies[key] = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct); }
            else Gets.Add(key + request.RequestUri.Query);
            if (request.Headers.TryGetValues("If-Match", out var guard)) Guards[key] = guard.Single();
            if (Gates.TryGetValue(key, out var gate)) await gate.Task;
            if (Reset.Contains(key)) throw new HttpRequestException("synthetic transport reset");
            if (Status.TryGetValue(key, out var status))
                return new((HttpStatusCode)status) { Content = new StringContent("{\"type\":\"https://siloserver.org/docs/api/v2/problems/precondition_failed\",\"status\":" + status + "}", Encoding.UTF8, "application/problem+json") };
            if (write && path.EndsWith("/config"))
            {
                using var config = JsonDocument.Parse(Bodies[key]);
                if (config.RootElement.GetProperty("key").GetString() == "limits" && config.RootElement.GetProperty("value").TryGetProperty("page_size", out var size)) PageSize = (int)size.GetDouble();
                return new(HttpStatusCode.NoContent);
            }
            if (write && path.EndsWith("/auth-binding")) return new(HttpStatusCode.NoContent);
            object data; var created = false;
            if (path == "/api/v2/admin/api-keys" && request.Method == HttpMethod.Post)
            { created = true; CreatedKeyVisibleToServer = true; data = new { id = "8", user_id = "1", label = "Created from fixture", key = Secret, rate_tier = "standard", scopes = new[] { "catalog:read" }, created_at = "2026-11-17T00:00:00Z" }; }
            else if (path.StartsWith("/api/v2/admin/api-keys/") && request.Method == HttpMethod.Delete) { Revoked = true; return new(HttpStatusCode.NoContent); }
            else if (path.EndsWith("/tier") && write) { using var body = JsonDocument.Parse(Bodies[key]); Tier = body.RootElement.GetProperty("rate_tier").GetString()!; data = Key("7", "Fixture integration"); }
            else if (path == "/api/v2/admin/settings" && write)
            {
                using var body = JsonDocument.Parse(Bodies[key]); var values = body.RootElement.GetProperty("values");
                if (values.TryGetProperty("auth.local_password_login", out var local)) Local = local.GetString() != "false";
                if (values.TryGetProperty("signup.enabled", out var signup)) Signup = signup.GetString() == "true";
                data = new { values = JsonSerializer.Deserialize<Dictionary<string, string>>(values.GetRawText()), restart_required = false };
            }
            else if (path == "/api/v2/admin/policy/documents" && request.Method == HttpMethod.Post)
            {
                created = true; using var body = JsonDocument.Parse(Bodies[key]); Docs.Add(("10", body.RootElement.GetProperty("name").GetString()!, false, null)); data = Document("10");
            }
            else if (path.StartsWith("/api/v2/admin/policy/documents/") && request.Method == HttpMethod.Delete) { Docs.RemoveAll(d => d.Id == path.Split('/')[^1]); return new(HttpStatusCode.NoContent); }
            else if (path.StartsWith("/api/v2/admin/policy/documents/") && request.Method == HttpMethod.Patch)
            {
                using var body = JsonDocument.Parse(Bodies[key]); var id = path.Split('/')[^1]; var index = Docs.FindIndex(d => d.Id == id);
                Docs[index] = Docs[index] with { Enabled = body.RootElement.GetProperty("enabled").GetBoolean() }; Generation++; data = Applied(id);
            }
            else if (path.EndsWith("/active-version")) { var id = path.Split('/')[^2]; var index = Docs.FindIndex(d => d.Id == id); Docs[index] = Docs[index] with { Active = "11" }; Generation++; data = Applied(id); }
            else if (path.EndsWith("/versions") && write)
            {
                created = true; using var body = JsonDocument.Parse(Bodies[key]); data = Version(body.RootElement.GetProperty("source").GetString());
            }
            else data = path switch
            {
                "/api/v2/admin/api-keys/capabilities" => new { available = KeysAvailable, guarded_configuration = true, scopes = new[] { new { name = "catalog:read", description = "Synthetic catalog reads", audience = "user" } }, rate_tiers = new[] { "standard", "elevated" } },
                "/api/v2/admin/api-keys" => new { items = Revoked || KeysEmpty && !CreatedKeyVisibleToServer ? Array.Empty<object>() : CreatedKeyVisibleToServer ? [Key("7", "Fixture integration"), Key("8", "Created from fixture")] : new[] { Key("7", "Fixture integration") }, page = new { has_more = false } },
                "/api/v2/admin/api-keys/7" => Key("7", "Fixture integration"),
                "/api/v2/admin/rate-limits/config" => new { tiers = new { standard = new { requests_per_second = 20, requests_per_minute = 1200 }, elevated = new { requests_per_second = 100, requests_per_minute = 6000 } } },
                // With LaterPageBreakGlass the only break-glass admin is on the second account page.
                // With ManyAccountPages the list is one account per page, so the page cap leaves a cursor; the break-glass admin is on the page after it.
                "/api/v2/admin/users" when ManyAccountPages => ManyAccountsPage(request.RequestUri.Query),
                "/api/v2/admin/users" when LaterPageBreakGlass && !request.RequestUri.Query.Contains("cursor=") => new { items = new[] { OwnerWithoutBreakGlass() }, page = new { has_more = true, next_cursor = "page-2" } },
                "/api/v2/admin/users" when LaterPageBreakGlass => new { items = new[] { LaterBreakGlassAdmin() }, page = new { has_more = false } },
                "/api/v2/admin/users" => new { items = new[] { Account() }, page = new { has_more = false } },
                "/api/v2/admin/users/1" => LaterPageBreakGlass ? OwnerWithoutBreakGlass() : Account(),
                "/api/v2/admin/users/2" => LaterBreakGlassAdmin(),
                "/api/v2/auth/external-sign-in/capabilities" => new { available = !ExternalUnavailable, break_glass = true, connection_test = true, live_provider_changes = true },
                "/api/v2/admin/plugins/installations" => new { items = new[] { Provider() } },
                "/api/v2/admin/plugins/installations/3/auth-binding/test" => new { ok = true, steps = new[] { new { id = "groups", label = "Allowed groups", ok = true, message = "Synthetic directory group verified" } } },
                "/api/v2/admin/settings/effective" => new Dictionary<string, string> { ["auth.local_password_login"] = Local ? "true" : "false", ["signup.enabled"] = Signup ? "true" : "false", ["password_reset.self_service_enabled"] = "false", ["auth.email_auto_match"] = "false", ["auth.user_api_keys"] = "false", ["server.public_url"] = "https://fixture.invalid" },
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/policy/capability" => new { enabled = PolicyOn, editor_available = PolicyOn, decision_types = new[] { "permission", "scope", "action" }, generation = Generation, degraded = false, eval_timeouts = 0 },
                "/api/v2/admin/policy/documents" => new { items = Docs.Select(d => Document(d.Id)).ToArray(), page = new { has_more = false } },
                "/api/v2/admin/policy/documents/9" or "/api/v2/admin/policy/documents/10" => Document(path.Split('/')[^1]),
                "/api/v2/admin/policy/documents/9/versions" => new { items = new[] { Version(null) }, page = new { has_more = false } },
                "/api/v2/admin/policy/documents/10/versions" => new { items = Array.Empty<object>(), page = new { has_more = false } },
                "/api/v2/admin/policy/documents/9/versions/11" => Version("package silo_custom.permission\n"),
                "/api/v2/admin/policy/validate" => new { compiled_ok = true, errors = Array.Empty<object>() },
                "/api/v2/admin/policy/simulate" => new { decision = new { allowed = false, reason = "synthetic" }, eval_time_ns = 12, generation = Generation },
                "/api/v2/admin/policy/vendor" => new { items = new[] { new { path = "permission.rego", source = "package silo.permission\nimport rego.v1\ndefault allowed := false" } } },
                "/api/v2/admin/policy/decisions" => new { items = new[] { new { id = "12", timestamp = "2026-11-17T00:00:00Z", decision_name = "silo.permission.decision", policy_generation = 1, allowed = false, eval_time_ns = 12, input_digest = "synthetic" } }, page = new { has_more = false } },
                "/api/v2/admin/policy/decisions/12" => new { id = "12", timestamp = "2026-11-17T00:00:00Z", decision_name = "silo.permission.decision", policy_generation = 1, allowed = false, eval_time_ns = 12, input_digest = "synthetic", input_sample = new { fixture = true }, result_sample = new { allowed = false } },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            var response = new HttpResponseMessage(created ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new(Etag); return response;
        }
        private object Key(string id, string label) => new { id, user_id = "1", label, username = "synthetic-admin", key_prefix = "fixture", rate_tier = id == "7" ? Tier : "standard", scopes = new[] { "catalog:read" }, created_at = "2026-11-17T00:00:00Z" };
        private static object OwnerWithoutBreakGlass() => new { id = "1", username = "synthetic-admin", role = "admin", enabled = true, is_owner = true, break_glass = false, password_login = true };
        private static object LaterBreakGlassAdmin() => new { id = "2", username = "synthetic-break-glass", role = "admin", enabled = true, is_owner = false, break_glass = true, password_login = true };
        private static object ManyAccountsPage(string query)
        {
            var match = System.Text.RegularExpressions.Regex.Match(query, "cursor=p(\\d+)"); var page = match.Success ? int.Parse(match.Groups[1].Value) : 1;
            if (page > 20) return new { items = new[] { LaterBreakGlassAdmin() }, page = new { has_more = false } };
            return new { items = new[] { new { id = (100 + page).ToString(), username = "synthetic-member-" + page, role = "user", enabled = true, is_owner = false, break_glass = false, password_login = true } }, page = new { has_more = true, next_cursor = "p" + (page + 1) } };
        }
        private static object Account() => new { id = "1", username = "synthetic-admin", role = "admin", enabled = true, is_owner = true, break_glass = true, password_login = true };
        private object Document(string id)
        {
            var doc = Docs.First(d => d.Id == id);
            return new { id, domain = "permission", name = id == "9" && RenamedNine != null ? RenamedNine : doc.Name, enabled = doc.Enabled, active_version_id = doc.Active, created_at = "2026-11-17T00:00:00Z", updated_at = "2026-11-17T00:00:00Z" };
        }
        private object Applied(string id) => new { persisted = true, document = Document(id), persisted_generation = Generation, application = new { local_applied = true, loaded_generation = Generation, publication_failed = false } };
        private static object Version(string? source)
        {
            var version = new Dictionary<string, object?> { ["id"] = "11", ["document_id"] = "9", ["version_number"] = 1, ["source_sha256"] = "synthetic", ["compiled_ok"] = true, ["created_at"] = "2026-11-17T00:00:00Z" };
            if (source != null) version["source"] = source;
            return version;
        }
        private object Provider() => new
        {
            id = "3", plugin_id = "fixture-ldap", enabled = true,
            capabilities = new[] { new { id = "ldap", type = "auth_provider.v1", display_name = "LDAP", sign_in_mode = "credentials", metadata = new { connection_test = true } } },
            auth_bindings = new[] { new { capability_id = "ldap", enabled = true, display_order = 1, auto_provision = true, default_login = false } },
            global_config_schema = new object[]
            {
                new { key = "ldap", title = "Directory", description = "Synthetic directory", required = true, json_schema = "{\"type\":\"object\",\"properties\":{\"allowed_groups\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"bind_password\":{\"type\":\"string\"}}}", admin_form = new { fields = new object[] { new { key = "allowed_groups", label = "Allowed groups", control = "TEXTAREA", required = false, secret = false }, new { key = "bind_password", label = "Bind password", control = "PASSWORD", required = false, secret = true } }, sections = Array.Empty<object>() } },
                new { key = "limits", title = "Limits", description = "Synthetic limits", required = false, json_schema = "{\"type\":\"object\",\"properties\":{\"timeout\":{\"type\":\"number\"},\"page_size\":{\"type\":\"integer\"}}}", admin_form = new { fields = new object[] { new { key = "timeout", label = "Timeout seconds", control = "NUMBER", required = false, secret = false, default_value = 30 }, new { key = "page_size", label = "Page size", control = "NUMBER", required = false, secret = false } }, sections = Array.Empty<object>() } },
            },
            global_configs = new object[]
            {
                new { key = "ldap", value = new { allowed_groups = new[] { "fixture-readers" } }, configured_secrets = new[] { "bind_password" } },
                new { key = "limits", value = new Dictionary<string, object?> { ["timeout"] = null, ["page_size"] = PageSize }, configured_secrets = Array.Empty<string>() },
            },
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
            outer.NavigationFailed += (_, e) => Program.Log("NAVFAIL " + e.SourcePageType.Name + ": " + e.Exception);
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");

            // ── API Keys: loading → populated, create (show once), canonical reconcile, edit rate limit, revoke ──
            var keysGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.Gates["GET /api/v2/admin/api-keys"] = keysGate;
            var keys = await Open(outer, "/admin/api-keys", "AdminApiKeysPage", visited);
            await Until(() => HasText(keys, "Loading…"));
            Check(!Buttons(keys).Any(b => Id(b).StartsWith("ApiKeyRevoke_")), "keys loading state offers no row actions");
            server.Gates.Remove("GET /api/v2/admin/api-keys"); keysGate.SetResult();
            await Until(() => HasText(keys, "Fixture integration"));
            Check(HasText(keys, "API Keys") && HasText(keys, "Create and manage credentials for integrations and automation."), "official API Keys title and subtitle");
            await Capture(outer, "access-api-keys.png");
            Click(keys, "ApiKeysCreate"); var create = await Dialog(keys);
            Check(HasText(create, "Create API Key") || create.Title?.ToString() == "Create API Key", "create dialog title");
            Click(create, "ApiKeyCreateSubmit"); // an empty label is rejected before any request
            await Task.Delay(200); Check(server.Count("POST /api/v2/admin/api-keys") == 0, "empty label never sends a create");
            Input(create, "ApiKeyLabel").Text = "Created from fixture";
            await ClickWhenReady(create, "ApiKeyCreateSubmit");
            await Until(() => HasText(create, Secret));
            Check(server.Count("POST /api/v2/admin/api-keys") == 1, "create sent exactly once");
            Check(HasText(create, "Copy your API key now. You cannot retrieve the full key again."), "show-once notice");
            create.Hide(); await Until(() => !create.IsLoaded);
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.Contains(Secret)), "secret cleared when the dialog closes");
            await Until(() => HasText(keys, "Created from fixture"));
            Check(server.Gets.Count(g => g.StartsWith("GET /api/v2/admin/api-keys?limit=50")) >= 2, "list re-read from the canonical server page after create");
            var edit = await ClickForDialog(keys, "ApiKeyTier_7", create);
            var tier = Descendants(edit).OfType<ComboBox>().Single(t => Id(t) == "ApiKeyTier");
            Check(tier.Items.OfType<ComboBoxItem>().Any(i => i.Content?.ToString()?.Contains("20 requests/s, 1200/min") == true), "rate hints from the limiter config");
            tier.SelectedItem = tier.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == "elevated");
            Primary(edit); await Until(() => !edit.IsLoaded && server.Tier == "elevated");
            Check(server.Count("PUT /api/v2/admin/api-keys/7/tier") == 1 && server.Guards["PUT /api/v2/admin/api-keys/7/tier"] == Etag, "tier saved once with the reviewed strong validator");
            // conflict: the guarded write is refused, the draft is kept and reload is offered; nothing is resent
            server.Status["PUT /api/v2/admin/api-keys/7/tier"] = 412;
            var conflict = await ClickForDialog(keys, "ApiKeyTier_7", edit); Primary(conflict);
            await Until(() => HasText(conflict, "This resource changed or its revision is unavailable. Reload and review before saving again.") || Descendants(conflict).OfType<Button>().Any(b => Id(b) == "ApiKeyReloadCurrent" && b.Visibility == Visibility.Visible));
            Check(server.Count("PUT /api/v2/admin/api-keys/7/tier") == 2, "refused tier write is not replayed");
            server.Status.Remove("PUT /api/v2/admin/api-keys/7/tier");
            conflict.Hide(); await Until(() => !conflict.IsLoaded);
            var revoke = await ClickForDialog(keys, "ApiKeyRevoke_7", conflict); Primary(revoke);
            await Until(() => !revoke.IsLoaded && server.Revoked);
            Check(server.Count("DELETE /api/v2/admin/api-keys/7") == 1 && server.Guards["DELETE /api/v2/admin/api-keys/7"] == Etag, "confirmed revoke sent once with the reviewed validator");

            // ── Sign-in: populated, connection test, staged save with explicit numeric null, secrets ──
            var signIn = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            Check(HasText(signIn, "Allowed groups") && HasText(signIn, "Page size"), "manifest groups and numeric fields rendered");
            Check(HasText(signIn, "synthetic-admin") || Descendants(signIn).OfType<TextBlock>().Any(t => t.Text.Contains("synthetic-admin")), "break-glass administrator listed");
            Check(Input(signIn, "SignIn_3_limits_timeout").Text == "", "explicit null number shows empty");
            await ClickWhenReady(signIn, "SignInTest_3"); await Until(() => HasTextContaining(signIn, "Synthetic directory group verified"));
            Check(!server.Writes.Any(w => w.EndsWith("/config")), "connection test never saves configuration");
            await Capture(outer, "access-sign-in.png");
            Scroll(signIn, 650);
            await Task.Delay(100); await Capture(outer, "access-sign-in-provider.png");
            Scroll(signIn, 0);
            Toggle(signIn, "SignIn_signup.enabled", true);
            Input(signIn, "SignIn_3_limits_page_size").Text = "40";
            await Until(() => Buttons(signIn).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            Click(signIn, "SignInSave"); var saveDialog = await Dialog(signIn); Primary(saveDialog);
            await Until(() => !saveDialog.IsLoaded && server.Count("PUT /api/v2/admin/settings") == 1);
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1, "provider configuration saved once");
            using (var config = JsonDocument.Parse(server.Body("PUT /api/v2/admin/plugins/installations/3/config")))
            {
                var value = config.RootElement.GetProperty("value");
                Check(config.RootElement.GetProperty("key").GetString() == "limits" && value.GetProperty("page_size").GetDouble() == 40, "changed number saved");
                Check(value.TryGetProperty("timeout", out var timeout) && timeout.ValueKind == JsonValueKind.Null, "explicit numeric null round-trips as null, not the default or zero");
                Check(!server.Body("PUT /api/v2/admin/plugins/installations/3/config").Contains("bind_password"), "blank secret is not sent");
            }
            Check(server.Guards["PUT /api/v2/admin/settings"] == Etag, "settings saved with the strong validator");

            // ── Keys: empty, unavailable, list failure, unknown-outcome create ──
            server.KeysEmpty = true; server.CreatedKeyVisibleToServer = false; server.Revoked = false;
            keys = await Open(outer, "/admin/api-keys?state=empty", "AdminApiKeysPage", visited);
            await Until(() => HasText(keys, "No API keys yet."));
            server.Writes.Clear();
            Click(keys, "ApiKeysCreate"); var unknown = await Dialog(keys);
            server.Reset.Add("POST /api/v2/admin/api-keys");
            Input(unknown, "ApiKeyLabel").Text = "Uncertain key"; Click(unknown, "ApiKeyCreateSubmit");
            await Until(() => HasTextContaining(unknown, "The outcome is unknown"));
            Check(server.Count("POST /api/v2/admin/api-keys") == 1, "unknown-outcome create is not resent");
            Check(!Buttons(unknown).Single(b => Id(b) == "ApiKeyCreateSubmit").IsEnabled, "a create with an unknown outcome cannot be repeated blindly");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.Contains(Secret)), "no secret after an unknown outcome");
            unknown.Hide(); await Until(() => !unknown.IsLoaded);
            server.Reset.Clear(); server.KeysEmpty = false;
            Click(keys, "AccessReload"); await Until(() => HasText(keys, "Fixture integration"));
            Check(Buttons(keys).Single(b => Id(b) == "ApiKeysCreate").IsEnabled, "reload re-enables creation");
            // After an unknown create the row actions are off too (opening/cancelling an editor would otherwise lift the block) until the page reload.
            server.Writes.Clear();
            Click(keys, "ApiKeysCreate"); var unknownRows = await Dialog(keys);
            server.Reset.Add("POST /api/v2/admin/api-keys");
            Input(unknownRows, "ApiKeyLabel").Text = "Uncertain key with rows"; Click(unknownRows, "ApiKeyCreateSubmit");
            await Until(() => HasTextContaining(unknownRows, "The outcome is unknown"));
            unknownRows.Hide(); await Until(() => !unknownRows.IsLoaded);
            server.Reset.Clear();
            Check(Buttons(keys).Any(b => Id(b) == "ApiKeyTier_7") && Buttons(keys).Where(b => Id(b) is "ApiKeyTier_7" or "ApiKeyRevoke_7").All(b => !b.IsEnabled), "row actions stay off after an unknown create");
            Check(!Buttons(keys).Single(b => Id(b) == "ApiKeysCreate").IsEnabled, "create stays off after an unknown create");
            Click(keys, "AccessReload"); await Until(() => Buttons(keys).Any(b => Id(b) == "ApiKeyTier_7" && b.IsEnabled));
            Check(Buttons(keys).Single(b => Id(b) == "ApiKeysCreate").IsEnabled && server.Count("POST /api/v2/admin/api-keys") == 1, "the page reload re-enables rows and creation without a resend");
            server.Status["GET /api/v2/admin/api-keys"] = 500;
            Click(keys, "AccessReload"); await Until(() => HasTextContaining(keys, "could not be completed"));
            server.Status.Remove("GET /api/v2/admin/api-keys");
            Click(keys, "AccessReload"); await Until(() => HasText(keys, "Fixture integration"));
            server.KeysAvailable = false;
            Click(keys, "AccessReload"); await Until(() => HasText(keys, "API key management is unavailable."));
            server.KeysAvailable = true;

            // ── Sign-in: a break-glass admin on a later account page still allows turning password sign-in off ──
            server.LaterPageBreakGlass = true;
            signIn = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            Check(HasTextContaining(signIn, "synthetic-break-glass"), "break-glass administrator from a later account page is listed");
            Toggle(signIn, "SignIn_auth.local_password_login", false);
            await Until(() => Buttons(signIn).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            await Task.Delay(150);
            Check(!Descendants(signIn).OfType<ToggleSwitch>().Single(t => Id(t) == "SignIn_auth.local_password_login").IsOn, "password sign-in can be staged off when the break-glass admin is on a later account page");
            // Typing key by key: the box is not rewritten while its text still means the staged value, so a list keeps its line break and space and a number its decimal point.
            var groups = Input(signIn, "SignIn_3_ldap_allowed_groups"); var seconds = Input(signIn, "SignIn_3_limits_timeout");
            foreach (var typed in new[] { "fixture-readers\r", "fixture-readers\rSilo ", "fixture-readers\rSilo Admins" })
            {
                groups.Text = typed; var held = groups.Text; await Task.Delay(150);
                Check(groups.Text == held, "typed group list is kept as typed (" + typed.Length + " characters)");
            }
            Check(groups.Text.Replace("\r\n", "\n").Replace('\r', '\n') == "fixture-readers\nSilo Admins", "a second group with a space can be typed on its own line");
            foreach (var typed in new[] { "1", "1.", "1.5" })
            {
                seconds.Text = typed; await Task.Delay(150);
                Check(seconds.Text == typed, "typed decimal is kept as typed: " + typed);
            }
            server.LaterPageBreakGlass = false;
            Click(signIn, "AccessReload"); var discardLater = await Dialog(signIn); Primary(discardLater);
            // The reload is finished only when the first-page-only account list is shown again.
            await Until(() => !discardLater.IsLoaded && HasTextContaining(signIn, "Break-glass admin: synthetic-admin") && !HasTextContaining(signIn, "synthetic-break-glass") && Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));

            // ── Sign-in: a changed account list updates its own parts in place and never replaces the controls the admin is using ──
            server.ManyAccountPages = true;
            Click(signIn, "AccessReload"); // nothing is staged here, so the reload asks nothing
            await Until(() => Buttons(signIn).Any(b => Id(b) == "SignInMoreAccounts") && Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            Check(Buttons(signIn).Any(b => Id(b) == "SignInMoreAccounts") && !HasTextContaining(signIn, "Break-glass admin"), "a list longer than the page cap offers more accounts and leaves the break-glass status to the server");
            var kept = Input(signIn, "SignIn_3_ldap_allowed_groups");
            await Task.Delay(500); // several rounds of the form rebuild that an echoed provider selection used to cause
            Check(ReferenceEquals(kept, Input(signIn, "SignIn_3_ldap_allowed_groups")), "an idle page does not rebuild its form");
            kept.Text = "fixture-readers\rSilo "; var keptText = kept.Text; await Task.Delay(150);
            Check(ReferenceEquals(kept, Input(signIn, "SignIn_3_ldap_allowed_groups")) && kept.Text == keptText, "a typed list stays in the same box");
            var moreGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.Gates["GET /api/v2/admin/users"] = moreGate;
            Click(signIn, "SignInMoreAccounts");
            await Until(() => ((AdminSignInSettingsPage)signIn).Controller.Busy);
            server.Gates.Remove("GET /api/v2/admin/users"); moreGate.SetResult();
            await Until(() => !((AdminSignInSettingsPage)signIn).Controller.Busy && HasTextContaining(signIn, "Break-glass admin: synthetic-break-glass") && !Buttons(signIn).Any(b => Id(b) == "SignInMoreAccounts"));
            await Task.Delay(150);
            Check(ReferenceEquals(kept, Input(signIn, "SignIn_3_ldap_allowed_groups")) && kept.IsLoaded && kept.Text == keptText, "an account re-read updates the status line and the button in place: the same box keeps its typed text");
            server.ManyAccountPages = false;
            Click(signIn, "AccessReload"); var discardMany = await Dialog(signIn); Primary(discardMany);
            await Until(() => !discardMany.IsLoaded && HasTextContaining(signIn, "Break-glass admin: synthetic-admin") && Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));

            // ── Sign-in: an external sign-in read that fails, or reports it unavailable, answers after the form is first shown and is still explained ──
            // Both leave the provider list the same empty list the form was first built with, so only the notice and the email-match row can show the answer.
            const string capsRead = "GET /api/v2/auth/external-sign-in/capabilities"; var signInState = ((AdminSignInSettingsPage)signIn).Controller;
            var capsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.Gates[capsRead] = capsGate; server.Status[capsRead] = 500;
            Click(signIn, "AccessReload"); // nothing is staged here, so the reload asks nothing
            await Until(() => signInState.Busy && signInState.Loaded); await Task.Delay(150); // the settings are shown while the external sign-in read is still out
            server.Gates.Remove(capsRead); capsGate.SetResult();
            // The account list is published last: its status line is back only when everything before it has been shown.
            await Until(() => !signInState.Busy && HasTextContaining(signIn, "Break-glass admin: synthetic-admin"));
            Check(HasTextContaining(signIn, "External sign-in could not be loaded"), "a failed external sign-in read is explained after the page has loaded");
            Check(Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.email_auto_match"), "the email-match setting stays shown while the provider list is unknown");
            server.Status.Remove(capsRead); server.ExternalUnavailable = true;
            capsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.Gates[capsRead] = capsGate;
            await ClickWhenReady(signIn, "AccessReload");
            await Until(() => signInState.Busy && signInState.Loaded); await Task.Delay(150);
            server.Gates.Remove(capsRead); capsGate.SetResult();
            await Until(() => !signInState.Busy && HasTextContaining(signIn, "Break-glass admin: synthetic-admin"));
            Check(HasTextContaining(signIn, "so only Silo passwords sign in") && !HasTextContaining(signIn, "External sign-in could not be loaded"), "unavailable external sign-in is explained after the page has loaded, and the earlier failure is no longer shown");
            Check(!Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.email_auto_match"), "the email-match setting is hidden when external sign-in is unavailable");
            server.ExternalUnavailable = false;
            await ClickWhenReady(signIn, "AccessReload");
            await Until(() => !HasTextContaining(signIn, "so only Silo passwords sign in") && Descendants(signIn).OfType<TextBox>().Any(t => Id(t) == "SignIn_3_ldap_allowed_groups") && Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));

            // ── Sign-in: failed and unknown-outcome saves block further writes until reload ──
            signIn = await Open(outer, "/admin/settings/sign-in", "AdminSignInSettingsPage", visited);
            await Until(() => Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            server.Writes.Clear(); server.Reset.Add("PUT /api/v2/admin/plugins/installations/3/config");
            Input(signIn, "SignIn_3_limits_page_size").Text = "55";
            await Until(() => Buttons(signIn).Any(b => Id(b) == "SignInSave" && b.IsEnabled));
            Click(signIn, "SignInSave"); var failing = await Dialog(signIn); Primary(failing);
            await Until(() => !failing.IsLoaded && Buttons(signIn).Any(b => Id(b) == "SignInSave" && !b.IsEnabled));
            Check(server.Count("PUT /api/v2/admin/plugins/installations/3/config") == 1 && server.Count("PUT /api/v2/admin/settings") == 0, "unknown-outcome save is sent once and blocks later writes");
            Check(Descendants(signIn).OfType<TextBlock>().Any(t => t.Text.Contains("outcome is unknown")), "unknown outcome is explained");
            server.Reset.Clear();
            Click(signIn, "AccessReload"); var discard = await Dialog(signIn); Primary(discard);
            await Until(() => !discard.IsLoaded && Descendants(signIn).OfType<ToggleSwitch>().Any(t => Id(t) == "SignIn_auth.local_password_login" && t.IsEnabled));
            Check(Input(signIn, "SignIn_3_limits_page_size").Text == "40", "reload shows the canonical value, not the abandoned draft");
            await ClickWhenReady(signIn, "SignInBinding_3"); var off = await Dialog(signIn); Primary(off);
            await Until(() => !off.IsLoaded && server.Count("PUT /api/v2/admin/plugins/installations/3/auth-binding") == 1);

            // ── Policy: capability gate, list, create + reconcile, row toggle, delete ──
            server.PolicyOn = false;
            var policy = await Open(outer, "/admin/policy", "AdminPolicyPage", visited);
            await Until(() => HasText(policy, "Policy workspace unavailable"));
            server.PolicyOn = true;
            Click(policy, "AccessReload"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyNew_permission"));
            Check(HasText(policy, "Admin & permissions") && HasText(policy, "Library visibility") && HasText(policy, "Downloads & playback"), "domain cards");
            Check(HasTextContaining(policy, "No overrides for this domain are shown on the loaded pages."), "empty domain state");
            await Capture(outer, "access-policy-overrides.png");
            server.Writes.Clear();
            Click(policy, "PolicyNew_permission"); var newDialog = await Dialog(policy);
            Primary(newDialog); await Until(() => HasText(newDialog, "Name is required."));
            Check(server.Count("POST /api/v2/admin/policy/documents") == 0, "an empty name never creates");
            Input(newDialog, "PolicyName").Text = "Weekend curation"; Primary(newDialog);
            await Until(() => !newDialog.IsLoaded && Descendants(policy).OfType<TextBox>().Any(t => Id(t) == "PolicySource"));
            Check(server.Count("POST /api/v2/admin/policy/documents") == 1, "override created once");
            Click(policy, "PolicyAllOverrides");
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyDocument_10"));
            Check(Buttons(policy).Any(b => Id(b) == "PolicyDocument_9"), "list is reconciled with the server's canonical page");
            await ClickWhenReady(policy, "PolicyRowEnabled_10"); var rowDialog = await Dialog(policy);
            Check(rowDialog.Title?.ToString() == "Enable override", "row enable confirmation");
            Primary(rowDialog); await Until(() => !rowDialog.IsLoaded && server.Count("PATCH /api/v2/admin/policy/documents/10") == 1);
            Check(server.Guards["PATCH /api/v2/admin/policy/documents/10"] == Etag, "enable uses the freshly read strong validator");
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyRowEnabled_10" && b.Content?.ToString() == "Disable override"));
            await ClickWhenReady(policy, "PolicyDocument_10");
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyDelete"));
            Click(policy, "PolicyDelete"); var delete = await Dialog(policy); Primary(delete);
            await Until(() => !delete.IsLoaded);
            Check(server.Count("DELETE /api/v2/admin/policy/documents/10") == 1 && server.Guards["DELETE /api/v2/admin/policy/documents/10"] == Etag, "delete sent once with the reviewed validator");
            await Until(() => !Buttons(policy).Any(b => Id(b) == "PolicyDocument_10") && Buttons(policy).Any(b => Id(b) == "PolicyDocument_9"));

            // An enable done inside the editor shows in "All overrides" at once, and the row toggles are usable again.
            await ClickWhenReady(policy, "PolicyDocument_9"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyEnabled" && b.IsEnabled));
            Click(policy, "PolicyEnabled"); var editorEnable = await Dialog(policy); Primary(editorEnable);
            await Until(() => !editorEnable.IsLoaded && server.Count("PATCH /api/v2/admin/policy/documents/9") == 1);
            Click(policy, "PolicyAllOverrides");
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyRowEnabled_9" && b.IsEnabled && b.Content?.ToString() == "Disable override"));
            Check(Buttons(policy).Single(b => Id(b) == "PolicyNew_permission").IsEnabled, "list shows the server state after an editor-side enable and writes are available again");
            await ClickWhenReady(policy, "PolicyRowEnabled_9"); var undoEnable = await Dialog(policy); Primary(undoEnable);
            await Until(() => !undoEnable.IsLoaded && server.Count("PATCH /api/v2/admin/policy/documents/9") == 2);
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyRowEnabled_9" && b.Content?.ToString() == "Enable override"));

            // An unknown create keeps New override and the row toggles off, even after opening an override and coming back; only the page reload lifts it.
            server.Reset.Add("POST /api/v2/admin/policy/documents");
            Click(policy, "PolicyNew_permission"); var unknownNew = await Dialog(policy);
            Input(unknownNew, "PolicyName").Text = "Unknown outcome"; Primary(unknownNew);
            await Until(() => HasTextContaining(unknownNew, "The outcome is unknown"));
            unknownNew.Hide(); await Until(() => !unknownNew.IsLoaded);
            server.Reset.Clear();
            Check(server.Count("POST /api/v2/admin/policy/documents") == 2, "unknown-outcome override create is sent once");
            await ClickWhenReady(policy, "PolicyDocument_9"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyAllOverrides"));
            Click(policy, "PolicyAllOverrides"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyNew_permission"));
            Check(!Buttons(policy).Single(b => Id(b) == "PolicyNew_permission").IsEnabled && !Buttons(policy).Single(b => Id(b) == "PolicyRowEnabled_9").IsEnabled, "opening an override and returning does not lift the block after an unknown create");
            Click(policy, "AccessReload"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyNew_permission" && b.IsEnabled));
            Check(Buttons(policy).Single(b => Id(b) == "PolicyRowEnabled_9").IsEnabled && server.Count("POST /api/v2/admin/policy/documents") == 2, "the page reload lifts the block without a resend");

            // ── Policy editor: review/adopt, validate, simulate, save (unknown then success), activate ──
            policy = await Open(outer, "/admin/policy?document=9", "AdminPolicyPage", visited);
            await Until(() => Descendants(policy).OfType<TextBox>().Any(t => Id(t) == "PolicySource"));
            Check(Input(policy, "PolicySource").Text.Contains("package silo_custom.permission"), "editor seeded from the latest saved version");
            server.RenamedNine = "Renamed on another server"; server.Docs[0] = server.Docs[0] with { Enabled = true };
            Click(policy, "PolicyReview");
            await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyAdopt"));
            Click(policy, "PolicyAdopt");
            await Until(() => HasText(policy, "Renamed on another server") && Buttons(policy).Any(b => Id(b) == "PolicyEnabled" && b.Content?.ToString() == "Disable override"));
            Check(true, "adopted snapshot updates the title and toggle label");
            Click(policy, "PolicyEnabled"); var enableDialog = await Dialog(policy);
            Check(enableDialog.Title?.ToString() == "Disable override", "toggle confirmation describes the adopted snapshot target");
            enableDialog.Hide(); await Until(() => !enableDialog.IsLoaded);
            Click(policy, "PolicyValidate"); await Until(() => HasTextContaining(policy, "Validation passed for the submitted source."));
            Check(HasTextContaining(policy, "✓ Validated"), "lifecycle rail reaches Validated");
            Click(policy, "PolicySimulate"); await Until(() => HasTextContaining(policy, "synthetic"));
            await Capture(outer, "access-policy-editor.png");
            Scroll(policy, 950);
            await Task.Delay(100); await Capture(outer, "access-policy-test.png");
            server.Writes.Clear(); server.Reset.Add("POST /api/v2/admin/policy/documents/9/versions");
            Click(policy, "PolicySave"); var saveFail = await Dialog(policy); Primary(saveFail);
            await Until(() => !saveFail.IsLoaded && HasTextContaining(policy, "outcome is unknown"));
            Check(server.Count("POST /api/v2/admin/policy/documents/9/versions") == 1, "unknown-outcome save is not resent");
            Check(!Buttons(policy).Single(b => Id(b) == "PolicySave").IsEnabled, "further writes are blocked until the canonical state is re-read");
            server.Reset.Clear();
            Click(policy, "AccessReload"); await Until(() => Descendants(policy).OfType<TextBox>().Any(t => Id(t) == "PolicySource") && Buttons(policy).Any(b => Id(b) == "PolicyValidate" && b.IsEnabled));
            Click(policy, "PolicyValidate"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicySave" && b.IsEnabled));
            Click(policy, "PolicySave"); var saveOk = await Dialog(policy); Primary(saveOk);
            await Until(() => server.Count("POST /api/v2/admin/policy/documents/9/versions") == 2 && !saveOk.IsLoaded && HasTextContaining(policy, "Saved v1"));
            Check(server.Count("POST /api/v2/admin/policy/documents/9/versions") == 2, "a re-read allows exactly one new save");
            Click(policy, "PolicyReview"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyAdopt")); Click(policy, "PolicyAdopt");
            await ClickWhenReady(policy, "PolicyActivate"); var activate = await Dialog(policy); Primary(activate);
            await Until(() => !activate.IsLoaded && server.Count("PUT /api/v2/admin/policy/documents/9/active-version") == 1);
            Check(server.Guards["PUT /api/v2/admin/policy/documents/9/active-version"] == Etag, "activation uses the reviewed strong validator");
            Click(policy, "PolicyTab_vendor"); await Until(() => HasText(policy, "permission.rego"));
            Scroll(policy, 0);
            await Capture(outer, "access-policy-baseline.png");
            Click(policy, "PolicyTab_decisions"); await Until(() => Buttons(policy).Any(b => Id(b) == "PolicyDecision_12"));
            Select(policy, "PolicyAllowed", "false"); Click(policy, "PolicyApplyFilters");
            await Until(() => server.Gets.Any(g => g.Contains("/decisions?") && g.Contains("allowed=false")));
            await ClickWhenReady(policy, "PolicyDecision_12"); await Until(() => HasTextContaining(policy, "fixture"));
            await Capture(outer, "access-policy-decisions.png");

            // ── Authority retirement clears the editor and any show-once material ──
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user" });
            await Until(() => !Descendants(policy).OfType<TextBox>().Any(t => t.Text.Contains("package silo")));
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.Contains(Secret)), "retired pages contain no show-once key");
        }
        finally
        {
            if (outer.XamlRoot != null) foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>()) dialog.Hide();
            outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null;
            await Until(() => visited.All(page => !page.IsLoaded));
            if (outer.XamlRoot != null) await Until(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().Any());
            window.Content = priorContent; globals.SetValue(null, priorServices);
        }
    }

    private static FrameworkElement? Scope;
    private static void Scroll(FrameworkElement page, double offset)
    {
        DependencyObject? node = (DependencyObject?)page.GetType().GetProperty("ScrollContent")!.GetValue(page);
        while (node != null && node is not ScrollViewer) node = VisualTreeHelper.GetParent(node);
        Check(node is ScrollViewer, "page scroller found"); ((ScrollViewer)node!).ChangeView(null, offset, null, true);
    }
    private static string Id(DependencyObject element) => AutomationProperties.GetAutomationId(element);
    private static IEnumerable<Button> Buttons(DependencyObject root) => Descendants(root).OfType<Button>();
    private static bool HasText(DependencyObject root, string text) => Descendants(root).OfType<TextBlock>().Any(t => t.Text == text);
    private static bool HasTextContaining(DependencyObject root, string text) => Descendants(root).OfType<TextBlock>().Any(t => t.Text.Contains(text, StringComparison.Ordinal));
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
    /// <summary>Clicks until a dialog other than <paramref name="previous"/> is open: a click that lands while the page is still closing the previous dialog is dropped by the page's own guard.</summary>
    private static async Task<ContentDialog> ClickForDialog(FrameworkElement page, string id, ContentDialog? previous = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await ClickWhenReady(page, id);
            for (var i = 0; i < 20; i++)
            {
                var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p => Descendants(p.Child)).OfType<ContentDialog>().FirstOrDefault(d => !ReferenceEquals(d, previous));
                if (dialog != null) return dialog;
                await Task.Delay(25);
            }
        }
        throw new TimeoutException("Sign-in & Security fixture: no dialog opened for " + id);
    }
    private static TextBox Input(DependencyObject root, string id) => Descendants(root).OfType<TextBox>().Single(t => Id(t) == id);
    private static void Toggle(DependencyObject root, string id, bool on) => Descendants(root).OfType<ToggleSwitch>().Single(t => Id(t) == id).IsOn = on;
    private static void Select(DependencyObject root, string id, string value)
    {
        var box = Descendants(root).OfType<ComboBox>().Single(t => Id(t) == id); box.SelectedItem = box.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == value);
    }
    private static void Click(DependencyObject root, string id)
    {
        var button = Buttons(root).Single(t => Id(t) == id);
        Check(button.IsEnabled, id + " enabled"); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static async Task ClickWhenReady(DependencyObject root, string id)
    {
        await Until(() => Buttons(root).Any(t => Id(t) == id && t.IsEnabled)); Click(root, id);
    }
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
        throw new TimeoutException($"Sign-in & Security fixture readiness at line {line}: {texts}");
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
