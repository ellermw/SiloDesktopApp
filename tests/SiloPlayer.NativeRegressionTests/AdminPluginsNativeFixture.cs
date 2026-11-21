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
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Drives the real Plugins page (board, catalog, sources, detail, configuration form) through the production registration and
// the real Admin shell against a synthetic server (official 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e). Every write has a
// refused, a conflicting and an unknown-outcome case. All values are synthetic.
internal static class AdminPluginsNativeFixture
{
    private enum Behave { Refuse, Conflict, Lost, Bad }

    private sealed class Row
    {
        public string Id = "", PluginId = "", Version = "1.0.0", Source = "silo", Repository = "2", Policy = "notify", Available = "", Name = "", Signature = "unsigned", State = "running", LastError = "";
        public bool Enabled = true, Resident = true, Managed, Updates = true, Setup, Auth, Check, Paused;
        public string? Pin;
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<Row> Installed = [];
        internal readonly List<(string Id, string PluginId, string Version, string Source, string Name)> Catalog = [];
        internal readonly List<(string Id, string Name, string Url, bool Enabled, bool Managed, string Kind)> Repos = [];
        internal bool FailInstalled, FailCatalog, FailRepos, FailProvisioning, Env, Community = true;
        internal string TaskState = "idle", ConfigValueHost = "fixture-host", ConfigMode = "basic";
        internal bool PasswordSaved = true, HostileCatalog;
        internal string[] ConfigTags = ["a"];
        // Names a plugin, its publisher or a repository could choose to forge a line or reorder text inside a confirmation (synthetic).
        internal static readonly string HostileName = "Acme\nVerified and signed by Silo. Requests no permissions." + (char)0x202E;
        internal static readonly string HostilePublisher = "Mallory\nVerified by Silo" + (char)0x2028 + "Trusted" + (char)0x202E;
        internal readonly List<(string Key, string Body)> Writes = [];
        internal readonly Dictionary<string, Behave> Next = new();
        internal TaskCompletionSource? Gate;
        internal string? IfMatch;
        internal int OpenedFiles;

        internal int Count(string key) => Writes.Count(w => w.Key == key);
        internal string Body(string key) => Writes.Last(w => w.Key == key).Body;

        private static string Json(object value) => JsonSerializer.Serialize(value);
        private static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK, string? etag = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(Json(value), Encoding.UTF8, "application/json") };
            if (etag != null) response.Headers.ETag = new(etag); return response;
        }
        private static HttpResponseMessage Problem(HttpStatusCode status, string detail) =>
            Reply(new { type = "urn:silo:problem:" + (status == HttpStatusCode.Conflict ? "conflict" : "validation_failed"), title = status.ToString(), status = (int)status, detail }, status);

        private object Capability(string type, string id, string name, string description, string[]? envManaged = null) => new { type, id, display_name = name, description, subscriptions = Array.Empty<string>(), env_managed = envManaged };

        private static object[] Fields() =>
        [
            new { key = "host", label = "Host", description = "Server host", control = "TEXT", placeholder = "", required = true, secret = false, multiline = false, rows = 0, dynamic_options = false, options = Array.Empty<object>(), show_when = Array.Empty<object>() },
            new { key = "password", label = "Password", description = "Account password", control = "PASSWORD", placeholder = "", required = false, secret = true, multiline = false, rows = 0, dynamic_options = false, options = Array.Empty<object>(), show_when = Array.Empty<object>() },
            new { key = "mode", label = "Mode", description = "", control = "SELECT", placeholder = "", required = false, secret = false, multiline = false, rows = 0, dynamic_options = false,
                options = new object[] { new { value = "basic", label = "Basic" }, new { value = "advanced", label = "Advanced" } }, show_when = Array.Empty<object>() },
            new { key = "endpoint", label = "Endpoint", description = "", control = "TEXT", placeholder = "", required = true, secret = false, multiline = false, rows = 0, dynamic_options = false, options = Array.Empty<object>(),
                show_when = new object[] { new { field = "mode", equals = new[] { "advanced" } } } },
            new { key = "port", label = "Port", description = "", control = "NUMBER", placeholder = "", required = false, secret = false, multiline = false, rows = 0, dynamic_options = false, options = Array.Empty<object>(), show_when = Array.Empty<object>(),
                validation = new { has_min = true, min = 1, has_max = true, max = 65535, pattern = "", min_length = 0, max_length = 0 } },
            new { key = "verbose", label = "Verbose logging", description = "", control = "SWITCH", placeholder = "", required = false, secret = false, multiline = false, rows = 0, dynamic_options = false, options = Array.Empty<object>(), show_when = Array.Empty<object>() },
            new { key = "tags", label = "Tags", description = "", control = "MULTI_SELECT", placeholder = "", required = false, secret = false, multiline = false, rows = 0, dynamic_options = false,
                options = new object[] { new { value = "a", label = "Alpha" }, new { value = "b", label = "Beta" } }, show_when = Array.Empty<object>() },
        ];

        private object Schema(bool required) => new
        {
            key = "connection", title = "Connection", description = "Synthetic connection settings", required,
            json_schema = """{"type":"object","required":["host"],"properties":{"host":{"type":"string"},"password":{"type":"string","writeOnly":true},"mode":{"type":"string"},"endpoint":{"type":"string"},"port":{"type":"integer"},"verbose":{"type":"boolean"},"tags":{"type":"array","items":{"type":"string"}}}}""",
            admin_form = new { fields = Fields(), sections = new object[] { new { key = "advanced", title = "Advanced", description = "Rarely needed", collapsible = true, collapsed_default = true, field_keys = new[] { "port", "verbose" }, show_when = Array.Empty<object>() } }, submit_label = (string?)null },
        };

        private object Presentation(Row row) => new
        {
            display_name = row.Name, summary = "Synthetic plugin summary", description_markdown = "About **bold** text and [a link](https://fixture.invalid/about) and [bad](javascript:alert(1)).",
            setup_markdown = "Setup <script>alert(1)</script> steps\n\n- first\n- second", publisher_name = row.Name == HostileName ? HostilePublisher : "Synthetic Labs", publisher_url = "https://fixture.invalid/publisher", license_spdx = "MIT", homepage_url = "",
            source_url = "https://fixture.invalid/source", support_url = "javascript:alert(1)", changelog_url = "https://fixture.invalid/changelog",
        };

        private object RowJson(Row row)
        {
            var capabilities = new List<object> { Capability("metadata_provider.v1", "metadata", "Metadata", "Synthetic metadata") };
            var routes = new List<object>(); var auth = new List<object>(); var tasks = new List<object>();
            if (row.Auth)
            {
                capabilities.Add(Capability("auth_provider.v1", "oidc", "Fixture sign-in", "Synthetic sign-in provider"));
                capabilities.Add(new { type = "scheduled_task.v1", id = "refresh", display_name = "Refresh fixture", description = "Synthetic task", subscriptions = Array.Empty<string>(), default_schedule = new { type = "daily", time_of_day = "03:30" } });
                routes.Add(new { id = "page", path = "/page", method = "GET", access = "admin", navigable = true, navigation_kind = "admin", navigation_label = "Fixture page", static_asset = false });
            }
            var schemas = row.Check || row.Managed || row.Setup ? new[] { Schema(row.Setup) } : [];
            var configs = row.Check || row.Managed ? new[] { new { key = "connection", value = new Dictionary<string, object> { ["host"] = ConfigValueHost, ["mode"] = ConfigMode, ["tags"] = ConfigTags }, configured_secrets = PasswordSaved ? new[] { "password" } : [] } } : [];
            object? env = row.Managed ? new { version_pin = row.Pin ?? "", config_fields = new Dictionary<string, string[]> { ["connection"] = ["host"] }, binding_fields = new Dictionary<string, string[]>() } : null;
            return new
            {
                id = row.Id, plugin_id = row.PluginId, version = row.Version, repository_id = row.Source == "unverified" ? null : row.Repository, kind = "binary", enabled = row.Enabled, update_policy = row.Policy,
                available_version = row.Available.Length > 0 ? row.Available : null, updates_paused = row.Paused, connection_check_supported = row.Check,
                source_kind = row.Source == "unverified" ? "external" : row.Source, repository_name = row.Source == "external" ? "Synthetic repo" : "", repo_url = "https://fixture.invalid/repo", install_path = "",
                presentation = row.Name.Length > 0 ? Presentation(row) : null, env_managed = env,
                runtime = new { resident = row.Resident, state = row.State, restart_count = row.State == "failed" ? 4 : 0, last_error = row.LastError, last_started_at = "2026-11-20T10:00:00.000Z" },
                signature = row.Signature == "verified" ? new { state = "verified", source_repo = "https://github.com/owner/name", verified_at = "2026-11-20T10:00:00.000Z" } : new { state = row.Signature, source_repo = (string?)null, verified_at = (string?)null },
                update_failure = row.Signature == "invalid" ? new { version = "1.5.0", reason = "The download did not match its checksum.", failed_at = "2026-11-20T09:00:00.000Z" } : null,
                capabilities, global_config_schema = schemas, user_config_schema = Array.Empty<object>(), global_configs = configs, auth_bindings = auth, task_bindings = tasks, routes,
                assets = Array.Empty<object>(), metadata = new { }, created_at = "2026-11-01T10:00:00.000Z", updated_at = "2026-11-20T10:00:00.000Z",
            };
        }

        private object CatalogJson((string Id, string PluginId, string Version, string Source, string Name) entry) => new
        {
            repository_id = entry.Id, plugin_id = entry.PluginId, version = entry.Version, source_kind = entry.Source, repository_name = entry.Source == "external" ? "Synthetic repo" : "", repo_url = "https://fixture.invalid/repo", archive_url = "https://fixture.invalid/a.zip",
            presentation = Presentation(new Row { Name = entry.Name }), capabilities = new object[] { Capability("marker_provider.v1", "markers", "Markers", "Synthetic markers") },
            global_config_schema = new[] { Schema(true) }, user_config_schema = Array.Empty<object>(), routes = Array.Empty<object>(), assets = Array.Empty<object>(), metadata = new { },
        };

        private static object Page(IEnumerable<object> items) => new { items = items.ToArray(), page = new { has_more = false } };

        internal Row? Find(string id) => Installed.FirstOrDefault(r => r.Id == id);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; const string root = "/api/v2/admin/plugins";
            if (request.Method == HttpMethod.Get)
            {
                if (Gate is { } gate && path.EndsWith("/installations")) await gate.Task;
                if (FailInstalled && path == root + "/installations" || FailCatalog && path == root + "/catalog" || FailRepos && path == root + "/repositories" || FailProvisioning && path == root + "/provisioning-status")
                    return Reply(new { status = 500 }, HttpStatusCode.InternalServerError);
                return path switch
                {
                    root + "/installations" => Reply(Page(Installed.Select(RowJson))),
                    root + "/catalog" => Reply(Page((HostileCatalog ? Catalog.Append(("2", "fixture.hostile", "1.0.0", "external", HostileName)) : Catalog).Select(CatalogJson))),
                    root + "/repositories" => Reply(Page(Repos.Select(r => (object)new { id = r.Id, display_name = r.Name, url = r.Url, enabled = r.Enabled, source_kind = r.Kind, managed = r.Managed }))),
                    root + "/catalog-settings" => Reply(new { include_approved_community_plugins = Community }, etag: "\"fixture-settings-v" + Count("PUT /catalog-settings") + "\""),
                    root + "/catalog-status" => Reply(new { approved_community_plugin_count = 3, installed_community_plugin_count = 1, migrated_plugin_count = 2, community_updates_paused = false }),
                    root + "/capabilities" => Reply(new { state = "available", allowed = true, revision = "r", env_declarations = Env }),
                    root + "/provisioning-status" => Reply(Page([new { plugin_id = "fixture.pending", version = "3.0.0", state = "pending", detail = "Waiting for the package source." }, new { plugin_id = "fixture.managed", version = "1.0.0", state = "applied", detail = "" }])),
                    "/api/v2/admin/tasks/check_plugin_updates" => Reply(Task()),
                    _ => Reply(new { }),
                };
            }
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Method + " " + path[root.Length..].TrimStart('/').Insert(0, "/"); if (path.StartsWith("/api/v2/admin/tasks")) key = request.Method + " " + path["/api/v2/admin".Length..];
            if (path.EndsWith("/uploads")) OpenedFiles++;
            Writes.Add((key, body)); if (path.EndsWith("/catalog-settings")) IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            // Behaviours are matched by method and the path with its numeric id removed.
            var generic = System.Text.RegularExpressions.Regex.Replace(key, "/[0-9]+", "/{id}");
            if (Next.Remove(key, out var behave) || Next.Remove(generic, out behave))
            {
                if (behave == Behave.Lost) throw new HttpRequestException("synthetic lost answer");
                if (behave == Behave.Bad) return Reply(new { status = 502 }, HttpStatusCode.BadGateway);
                if (behave == Behave.Conflict) return Problem(path.EndsWith("/catalog-settings") ? HttpStatusCode.PreconditionFailed : HttpStatusCode.Conflict, "Synthetic conflict: the plugin changed.");
                return Problem(HttpStatusCode.UnprocessableEntity, "Synthetic refusal: the server rejected this change.");
            }
            using var document = body.Length > 0 && request.Content!.Headers.ContentType?.MediaType == "application/json" ? JsonDocument.Parse(body) : null; var json = document?.RootElement;
            var segments = path[root.Length..].Trim('/').Split('/');
            if (path.EndsWith("/catalog-settings")) { Community = json!.Value.GetProperty("include_approved_community_plugins").GetBoolean(); return Reply(new { include_approved_community_plugins = Community }, etag: "\"fixture-settings-next\""); }
            if (path.StartsWith("/api/v2/admin/tasks")) { TaskState = "running"; return Reply(Task()); }
            if (segments[0] == "installations" && segments.Length == 1 && request.Method == HttpMethod.Post)
            {
                var entry = Catalog.First(c => c.PluginId == json!.Value.GetProperty("plugin_id").GetString());
                var row = new Row { Id = (100 + Installed.Count).ToString(), PluginId = entry.PluginId, Version = entry.Version, Source = entry.Source, Name = entry.Name }; Installed.Add(row); return Reply(RowJson(row), HttpStatusCode.Created);
            }
            if (segments[0] == "uploads") { var row = new Row { Id = "300", PluginId = "fixture.uploaded", Source = "unverified", Name = "Fixture Uploaded" }; Installed.Add(row); return Reply(RowJson(row), HttpStatusCode.Created); }
            if (segments[0] == "repositories")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var repo = (Id: "50", Name: json!.Value.GetProperty("display_name").GetString()!, Url: json.Value.GetProperty("url").GetString()!, Enabled: true, Managed: false, Kind: "external"); Repos.Add(repo);
                    return Reply(new { id = repo.Id, display_name = repo.Name, url = repo.Url, enabled = true, source_kind = "external", managed = false }, HttpStatusCode.Created);
                }
                var index = Repos.FindIndex(r => r.Id == segments[1]);
                if (request.Method == HttpMethod.Delete) { Repos.RemoveAt(index); return new(HttpStatusCode.NoContent); }
                var current = Repos[index]; Repos[index] = current with { Enabled = json!.Value.GetProperty("enabled").GetBoolean() };
                return Reply(new { id = current.Id, display_name = current.Name, url = current.Url, enabled = Repos[index].Enabled, source_kind = current.Kind, managed = false });
            }
            var target = Find(segments[1]);
            if (segments.Length == 2 && request.Method == HttpMethod.Delete) { Installed.Remove(target!); return new(HttpStatusCode.NoContent); }
            if (segments.Length == 2)
            {
                if (json!.Value.TryGetProperty("enabled", out var enabled)) target!.Enabled = enabled.GetBoolean();
                if (json.Value.TryGetProperty("update_policy", out var policy)) target!.Policy = policy.GetString()!;
                return Reply(RowJson(target!));
            }
            switch (segments[2])
            {
                case "update": target!.Version = target.Available; target.Available = ""; return Reply(RowJson(target));
                case "restart": target!.State = "running"; target.LastError = ""; return Reply(RowJson(target));
                case "config": if (segments.Length == 3) { if (json!.Value.GetProperty("value").TryGetProperty("host", out var host)) ConfigValueHost = host.GetString() ?? ""; return new(HttpStatusCode.NoContent); } return Reply(new { success = true, message = "Synthetic connection ok" });
                case "auth-binding": return new(HttpStatusCode.NoContent);
                case "task-bindings": return Reply(new { restart_required = false });
            }
            return Reply(new { });
        }

        private object Task() => new { key = "check_plugin_updates", name = "Check plugin updates", description = "d", category = "system", state = TaskState, progress = 0, manual_only = false, triggers = Array.Empty<object>(), execution_scope = "process" };

        internal void Seed(int fillers)
        {
            Installed.Clear(); Catalog.Clear(); Repos.Clear();
            Installed.Add(new Row { Id = "7", PluginId = "fixture.plugin", Name = "Fixture Plugin", Available = "2.0.0", Check = true, Signature = "verified" });
            Installed.Add(new Row { Id = "8", PluginId = "fixture.failed", Name = "Fixture Failed", State = "failed", LastError = "synthetic failure token=synthetic-secret" });
            Installed.Add(new Row { Id = "9", PluginId = "fixture.setup", Name = "Fixture Setup", Setup = true });
            Installed.Add(new Row { Id = "10", PluginId = "fixture.off", Name = "Fixture Off", Enabled = false });
            Installed.Add(new Row { Id = "11", PluginId = "fixture.unverified", Name = "Fixture Unverified", Source = "unverified", Signature = "invalid" });
            Installed.Add(new Row { Id = "12", PluginId = "fixture.managed", Name = "Fixture Managed", Managed = true, Pin = "1.0.0", Check = true });
            Installed.Add(new Row { Id = "13", PluginId = "fixture.auth", Name = "Fixture Auth", Auth = true, Source = "approved_community" });
            Installed.Add(new Row { Id = "14", PluginId = "fixture.paused", Name = "Fixture Paused", Paused = true, Source = "external", Available = "9.0.0" });
            for (var i = 0; i < fillers; i++) Installed.Add(new Row { Id = (1000 + i).ToString(), PluginId = "fixture.filler" + i, Name = "Fixture Zfill " + i.ToString("D3") });
            Catalog.Add(("2", "fixture.catalog", "2.0.0", "external", "Fixture Catalog"));
            Catalog.Add(("2", "fixture.second", "1.0.0", "external", "Fixture Second"));
            Catalog.Add(("1", "fixture.silo", "1.0.0", "silo", "Fixture Silo"));
            Catalog.Add(("3", "fixture.community", "1.0.0", "approved_community", "Fixture Community"));
            Catalog.Add(("1", "fixture.plugin", "1.0.0", "silo", "Fixture Plugin"));
            Repos.Add(("1", "Silo", "", true, true, "silo")); Repos.Add(("3", "Community", "", true, true, "approved_community")); Repos.Add(("2", "Synthetic repo", "https://fixture.invalid/index.json", true, false, "external"));
        }
    }

    private static void Check(bool condition, string message)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + message); if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(25);
        Check(condition(), message);
    }

    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" }); auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        collection.AddAdminPlugins(); // The same production registration as App.
        await using var services = collection.BuildServiceProvider();
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldServices = serviceField.GetValue(null); var oldContent = window.Content; var oldName = DocumentTitle.AppName;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null; Frame? inner = null; AdminPluginsPage? page = null;
        try
        {
            serviceField.SetValue(null, services); outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 960));
            var launched = new List<Uri>();

            // 1. A failed read is shown as failed with a reachable Retry, never as an empty inventory.
            server.Seed(62); server.FailInstalled = true;
            Check(AdminShellPage.TryOpenHref("/admin/plugins") is { Kind: AdminRouteResolutionKind.Route }, "Plugins resolves through production registration, not an unsupported list");
            await Until(() => outer.Content is AdminShellPage, "Plugins entered the real Admin shell");
            var shell = (AdminShellPage)outer.Content; inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminPluginsPage { Controller.State.Failures.Count: > 0 }, "XAML-backed Plugins activates through the Frame and reports a failed read"); page = (AdminPluginsPage)inner.Content;
            await Until(() => Find<Button>(page, "PluginsRetry") != null, "Reload is reachable after a failed read");
            Check(AllText(page).Contains("Couldn't load plugins") && !AllText(page).Contains("No plugins installed") && Find<StackPanel>(page, "PluginsTiles") == null, "A failed read is not shown as an empty inventory");
            Check(server.Writes.Count == 0, "Opening the page sent nothing");
            server.FailInstalled = false; await Click(page, "PluginsRetry");
            await Until(() => Find<StackPanel>(page, "PluginsTiles") != null, "Retry loads the board");

            // 2. The board: tiles with status, version, badges; filters, search, bounded paging; provisioning status.
            server.Env = true; await page.Controller!.ReloadAsync();
            await Until(() => AllText(page).Contains("Provisioning status") && AllText(page).Contains("fixture.pending"), "Provisioning status is shown");
            await Until(() => AllText(page).Contains("Fixture Plugin"), "Installed tiles render");
            var text = AllText(page);
            Check(text.Contains("Needs setup") && text.Contains("Failed") && text.Contains("Off") && text.Contains("2.0.0 available") && text.Contains("Signature failed") && text.Contains("Managed by the environment") && text.Contains("Updates paused"), "Tiles show status, update, signature and managed badges");
            Check(text.Contains("70 plugins") && Find<Button>(page, "PluginsShowMore") != null, "Result count is complete while tiles are bounded behind Show more");
            var tiles = Descendants(page).OfType<Button>().Count(b => (AutomationProperties.GetAutomationId(b) ?? "").StartsWith("PluginOpen_"));
            Check(tiles == 60, "Only the first page of tiles exists");
            await Click(page, "PluginsShowMore"); await Until(() => Descendants(page).OfType<Button>().Count(b => (AutomationProperties.GetAutomationId(b) ?? "").StartsWith("PluginOpen_")) == 70, "Show more adds the rest");
            await Click(page, "PluginsChip_attention");
            await Until(() => Descendants(page).OfType<Button>().Count(b => (AutomationProperties.GetAutomationId(b) ?? "").StartsWith("PluginOpen_")) >= 3, "Needs attention filters the board");
            var search = Find<TextBox>(page, "PluginsSearch")!; search.Text = "no such plugin anywhere";
            await Until(() => AllText(page).Contains("No installed plugins match"), "No-match state is distinct from empty");
            await Click(page, "PluginsClearFilters"); await Until(() => AllText(page).Contains("70 plugins"), "Clear search and filters restores the board");
            await Capture(outer, "plugins-board.png");

            // 3. Update check: one run, bounded polling, lists read again when it ends.
            await Click(page, "PluginsCheckUpdates");
            await Until(() => server.Count("POST /tasks/check_plugin_updates/run") == 1 && Find<Button>(page, "PluginsCheckUpdates") is { IsEnabled: false }, "Check for updates runs once and disables itself while running");
            server.TaskState = "idle"; await Until(() => Find<Button>(page, "PluginsCheckUpdates") is { IsEnabled: true }, "The finished check enables the button and reads the lists again");

            // 4. Catalog: community control, conflicting change, install confirmations.
            page = await Goto(shell, inner, new(AdminRoute.Plugins, new Dictionary<string, string> { ["tab"] = "catalog" }), page);
            await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity") != null && AllText(page).Contains("Fixture Catalog"), "Catalog tab renders the community control and groups");
            Check(AllText(page).Contains("Made by Silo") && AllText(page).Contains("Approved community") && AllText(page).Contains("Other sources") && AllText(page).Contains("2 existing installations were moved here without changing configuration."), "Catalog groups and the migrated count use the official text");
            Check(Find<Button>(page, "PluginRepositoryToggle_1") == null && AllText(page).Contains("Managed by Silo"), "Managed repositories have no controls");
            Find<ToggleSwitch>(page, "PluginsCommunity")!.IsOn = false;
            await WaitDialog(page); Check(AllText((ContentDialog)Field(page, "_dialog")!).Contains("Hide approved community plugins?"), "Hiding the community catalog asks first");
            await CloseDialog(page, null); Check(server.Count("PUT /catalog-settings") == 0, "Cancel sends nothing"); await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity")!.IsOn, "The control follows the saved value after cancel");
            server.Next["PUT /catalog-settings"] = Behave.Conflict; Find<ToggleSwitch>(page, "PluginsCommunity")!.IsOn = false; await WaitDialog(page); await CloseDialog(page, "Hide and pause updates");
            await Until(() => page.Controller!.State.CatalogReviewRequired && !page.Controller.State.Busy, "A conflicting catalog change needs review");
            Check(server.IfMatch == "\"fixture-settings-v0\"" && server.Count("PUT /catalog-settings") == 1, "The change was sent once with the reviewed strong validator and not replayed");
            await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity") is { IsEnabled: false } && Find<Button>(page, "PluginsSettingsReload") != null, "Editing waits for an explicit reload");
            await Click(page, "PluginsSettingsReload"); await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity") is { IsEnabled: true }, "Reload re-enables the control");

            var install = "PluginInstall_fixture.catalog";
            await Click(page, install); await WaitDialog(page);
            var dialogText = AllText((ContentDialog)Field(page, "_dialog")!);
            Check(dialogText.Contains("Install Fixture Catalog?") && dialogText.Contains("External source. Silo has not reviewed this plugin.") && dialogText.Contains("Version 2.0.0") && dialogText.Contains("Synthetic Labs"), "Install shows the source, notice, publisher and version");
            await CloseDialog(page, null); Check(server.Count("POST /installations") == 0, "Cancelling an install sends nothing");
            server.Next["POST /installations"] = Behave.Refuse; await Click(page, install); await WaitDialog(page); await CloseDialog(page, "Install plugin");
            await Until(() => page.Controller!.State.NoticeIsError && page.Controller.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "A refused install shows the server's reason");
            Check(page.Controller.State.Unconfirmed.Count == 0 && Find<Button>(page, install) is { IsEnabled: true }, "A refusal stays retryable");
            await Click(page, install); await WaitDialog(page); await CloseDialog(page, "Install plugin");
            await Until(() => server.Installed.Any(r => r.PluginId == "fixture.catalog") && !page.Controller!.State.Busy, "The retry installs");
            Check(server.Count("POST /installations") == 2 && server.Body("POST /installations").Contains("\"repository_id\":\"2\"") && server.Body("POST /installations").Contains("\"version\":\"2.0.0\""), "The install body names the confirmed repository, plugin and version");
            await Until(() => Find<Button>(page, install) == null && AllText(page).Contains("Installed"), "The installed entry no longer offers Install");

            // Text a plugin or its publisher chooses cannot add a line or reorder text inside the confirmation: one gate makes it one line, and
            // the plugin ID and version stand on their own lines from the app's own data.
            server.HostileCatalog = true; await page.Controller!.ReloadAsync();
            await Until(() => Find<Button>(page, "PluginInstall_fixture.hostile") != null, "A catalog entry with a hostile name and publisher renders");
            var hostileTile = Descendants(Find<Border>(page, "PluginTile_fixture.hostile")!).OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(hostileTile.Contains("Acme Verified and signed by Silo. Requests no permissions.") && !hostileTile.Any(l => l.Any(IsHidden)), "The hostile name shows as one line on its tile");
            await Click(page, "PluginInstall_fixture.hostile"); await WaitDialog(page);
            var hostileDialog = (ContentDialog)Field(page, "_dialog")!; var hostileLines = Descendants(hostileDialog).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToList();
            Check((hostileDialog.Title as string ?? "") == "Install Acme Verified and signed by Silo. Requests no permissions.?" && !hostileLines.Any(l => l.Any(IsHidden)), "The install confirmation holds no line break and no direction control");
            Check(hostileLines.Contains("Plugin ID: fixture.hostile") && hostileLines.Contains("Version 1.0.0") && hostileLines.IndexOf("Plugin ID: fixture.hostile") < hostileLines.FindIndex(l => l.StartsWith("by ")), "The plugin ID and version stand on their own lines, before anything the plugin says");
            Check(!hostileLines.Any(l => l.StartsWith("Verified")) && hostileLines.Contains("by Mallory Verified by Silo Trusted") && hostileLines.Count(l => l.Contains("Requests no permissions")) == 0, "Hostile text adds no line of its own: the name is only in the title and the publisher stays one line");
            await CloseDialog(page, null); Check(server.Count("POST /installations") == 2, "Cancelling the hostile install sends nothing"); server.HostileCatalog = false; await page.Controller!.ReloadAsync();

            // An install whose answer was lost is never replayed; the page blocks that plugin until it is read back.
            var second = "PluginInstall_fixture.second";
            server.Next["POST /installations"] = Behave.Lost; server.FailInstalled = true; await Click(page, second); await WaitDialog(page); await CloseDialog(page, "Install plugin");
            await Until(() => page.Controller!.State.Unconfirmed.Contains("fixture.second") && !page.Controller.State.Busy, "A lost install answer is an unknown outcome");
            Check(page.Controller.State.Notice.Contains("could not be confirmed"), "The unknown outcome is reported as such");
            var writes = server.Writes.Count; await Click(page, second); await WaitDialog(page); await CloseDialog(page, "Install plugin");
            await Until(() => !page.Controller.State.Busy, "The blocked attempt ended"); Check(server.Writes.Count == writes, "Nothing is sent for a plugin whose last write is unconfirmed");
            server.FailInstalled = false; await Until(() => Find<Button>(page, "PluginsInstalledReload") != null, "A reachable Retry reads the installations back");
            await Click(page, "PluginsInstalledReload"); await Until(() => page.Controller.State.Unconfirmed.Count == 0, "Reading the installations back clears the block");

            // The list changing while the confirmation is open sends nothing.
            await Click(page, "PluginInstall_fixture.silo"); await WaitDialog(page);
            server.Catalog[2] = ("1", "fixture.silo", "1.1.0", "silo", "Fixture Silo"); await page.Controller.ReloadAsync();
            writes = server.Writes.Count; await CloseDialog(page, "Install plugin");
            await Until(() => page.Controller.State.Notice.Contains("changed"), "A changed list is reported"); Check(server.Writes.Count == writes, "An install pinned to a listing that changed sends nothing");

            // 5. Upload: size and type are checked before the file is read; one confirmation; nothing kept.
            var oversized = new AdminPluginUploadChoice("big.zip", 300L * 1024 * 1024, () => { server.OpenedFiles += 1000; return Task.FromResult<Stream>(new MemoryStream()); });
            page.FilePicker = () => Task.FromResult<AdminPluginUploadChoice?>(oversized);
            await Click(page, "PluginsChooseFile"); await Until(() => AllText(page).Contains("larger than 256 MiB"), "An oversized file is refused before it is read");
            Check(server.OpenedFiles < 1000 && Find<Button>(page, "PluginsUpload") is { IsEnabled: false }, "The oversized file was never opened and cannot be uploaded");
            var opened = 0; var content = new byte[2048];
            page.FilePicker = () => Task.FromResult<AdminPluginUploadChoice?>(new("fixture.zip", content.Length, () => { opened++; return Task.FromResult<Stream>(new MemoryStream(content)); }));
            await Click(page, "PluginsChooseFile"); await Until(() => Find<Button>(page, "PluginsUpload") is { IsEnabled: true } && AllText(page).Contains("fixture.zip"), "A valid file is chosen"); Check(opened == 0, "Choosing a file reads nothing");
            await Click(page, "PluginsUpload"); await WaitDialog(page);
            Check(AllText((ContentDialog)Field(page, "_dialog")!).Contains("It isn't linked to any of your catalogs"), "Upload shows the unverified-source notice before anything is sent");
            await CloseDialog(page, null); Check(opened == 0 && server.Count("POST /uploads") == 0, "Cancelling an upload sends and reads nothing");
            server.Next["POST /uploads"] = Behave.Refuse; await Click(page, "PluginsUpload"); await WaitDialog(page); await CloseDialog(page, "Upload");
            await Until(() => page.Controller.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "A refused upload shows the server's reason");
            Check(Find<Button>(page, "PluginsUpload") is { IsEnabled: false } && AllText(page).Contains("Choose plugin file..."), "Nothing about the file is kept after the upload ends");
            await Click(page, "PluginsChooseFile"); await Until(() => Find<Button>(page, "PluginsUpload") is { IsEnabled: true }, "The file can be chosen again");
            await Click(page, "PluginsUpload"); await WaitDialog(page); await CloseDialog(page, "Upload");
            await Until(() => server.Installed.Any(r => r.PluginId == "fixture.uploaded") && !page.Controller.State.Busy, "The upload is sent once and installs");
            Check(server.Count("POST /uploads") == 2 && server.Body("POST /uploads").Contains("name=archive") && opened == 2, "Each upload sent one multipart archive");
            server.Next["POST /uploads"] = Behave.Lost; server.FailInstalled = true; await Click(page, "PluginsChooseFile"); await Until(() => Find<Button>(page, "PluginsUpload") is { IsEnabled: true }, "Chosen for the lost case");
            await Click(page, "PluginsUpload"); await WaitDialog(page); await CloseDialog(page, "Upload");
            await Until(() => page.Controller.State.Unconfirmed.Contains("*") && !page.Controller.State.Busy, "A lost upload answer blocks lifecycle writes everywhere");
            server.FailInstalled = false; await page.Controller.ReloadAsync(); await Until(() => page.Controller.State.Unconfirmed.Count == 0, "Reading the installations back clears the upload block");

            // 6. Repositories: add (refused keeps what was typed), disable, remove.
            await Click(page, "PluginsRepositoryAdd"); await Until(() => Find<TextBox>(page, "PluginsRepositoryName") != null, "The add form opens");
            Find<TextBox>(page, "PluginsRepositoryName")!.Text = "Synthetic extra"; Find<TextBox>(page, "PluginsRepositoryUrl")!.Text = "https://fixture.invalid/extra.json";
            server.Next["POST /repositories"] = Behave.Refuse; await Click(page, "PluginsRepositorySubmit");
            await Until(() => page.Controller.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "A refused repository shows the server's reason");
            Check(Find<TextBox>(page, "PluginsRepositoryName")?.Text == "Synthetic extra" && Find<TextBox>(page, "PluginsRepositoryUrl")?.Text == "https://fixture.invalid/extra.json", "What was typed is kept until the server accepts it");
            await Click(page, "PluginsRepositorySubmit"); await Until(() => server.Repos.Any(r => r.Name == "Synthetic extra") && Find<TextBox>(page, "PluginsRepositoryName") == null, "The repository is added and the form closes");
            Check(server.Body("POST /repositories").Contains("\"display_name\":\"Synthetic extra\"") && server.Body("POST /repositories").Contains("\"enabled\":true"), "The add body carries the name, URL and enabled flag");
            await Click(page, "PluginRepositoryToggle_50"); await Until(() => server.Count("PUT /repositories/50") == 1 && server.Body("PUT /repositories/50") == "{\"enabled\":false}", "Disabling sends only the enabled flag");
            await Until(() => !page.Controller.State.Busy, "Disable finished");
            server.Next["DELETE /repositories/{id}"] = Behave.Lost; server.FailRepos = true; await Click(page, "PluginRepositoryRemove_50");
            await Until(() => page.Controller.State.Unconfirmed.Contains("repositories") && !page.Controller.State.Busy, "A lost removal is an unknown outcome");
            server.FailRepos = false; await Until(() => Find<Button>(page, "PluginsRepositoriesReload") != null, "Repositories can be read back"); await Click(page, "PluginsRepositoriesReload");
            await Until(() => page.Controller.State.Unconfirmed.Count == 0, "Reading the repositories back clears the block");
            await Click(page, "PluginRepositoryRemove_50"); await Until(() => !server.Repos.Any(r => r.Id == "50"), "Removal is sent once"); await Capture(outer, "plugins-catalog.png");

            // 7. One plugin's page: banners, settings form, secrets, managed fields, writes.
            var beforeDetail = server.Writes.Count;
            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.plugin"] }, page);
            await Until(() => Find<PasswordBox>(page, "PluginConfigField_connection_password") != null, "The schema-driven form renders");
            Check(server.Writes.Count == beforeDetail, "Opening the plugin page sent nothing");
            var before = server.Writes.Count;
            var secret = Find<PasswordBox>(page, "PluginConfigField_connection_password")!; Check(secret.Password.Length == 0 && secret.PasswordRevealMode == PasswordRevealMode.Hidden, "A saved secret is never filled in or revealed");
            Check(AllText(page).Contains("Password: saved") && Find<Button>(page, "PluginConfigClear_connection_password") != null, "A saved secret can be kept or cleared explicitly");
            Check(!IsVisible(Find<TextBox>(page, "PluginConfigField_connection_endpoint")!), "A conditionally required field is hidden while its condition is unmet");
            Check(!IsVisible(Find<TextBox>(page, "PluginConfigField_connection_port")!), "A field inside a collapsed section is hidden");
            Check(Find<Button>(page, "PluginConfigSave_connection") is { IsEnabled: true }, "A hidden required field does not block Save");
            Check(page.RequiresLeaveConfirmation == false, "Opening the form did not mark it edited");
            await Click(page, "PluginConfigSection_connection_advanced"); await Until(() => AllText(page).Contains("Hide"), "A collapsible section opens");
            // Save once: a blank secret is left out, the hidden fields are not sent, an emptied field is cleared explicitly.
            Find<TextBox>(page, "PluginConfigField_connection_host")!.Text = "new-fixture-host";
            await Until(() => page.RequiresLeaveConfirmation, "An edit marks the form as edited");
            secret.Password = "typed-secret-value"; await Task.Delay(200); // the control reports its change one dispatch later
            server.Next["PUT /installations/{id}/config"] = Behave.Refuse; await Click(page, "PluginConfigSave_connection");
            await Until(() => page.Controller!.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "A refused save shows the server's reason");
            Check(!page.Controller.State.Notice.Contains("typed-secret-value") && Find<PasswordBox>(page, "PluginConfigField_connection_password")!.Password == "typed-secret-value" && Find<TextBox>(page, "PluginConfigField_connection_host")!.Text == "new-fixture-host", "A typed secret and the edits survive a refused save without being echoed");
            await Click(page, "PluginConfigSave_connection"); await Until(() => server.Count("PUT /installations/7/config") == 2 && !page.Controller.State.Busy, "The corrected save is sent once more");
            using (var save = JsonDocument.Parse(server.Body("PUT /installations/7/config")))
            {
                var value = save.RootElement.GetProperty("value");
                Check(save.RootElement.GetProperty("key").GetString() == "connection" && value.GetProperty("host").GetString() == "new-fixture-host" && value.GetProperty("password").GetString() == "typed-secret-value" && value.GetProperty("mode").GetString() == "basic", "The save body carries the edited fields and the typed secret");
                Check(!value.TryGetProperty("endpoint", out _) && value.TryGetProperty("verbose", out var verbose) && verbose.ValueKind == JsonValueKind.False && value.GetProperty("port").ValueKind == JsonValueKind.Null && value.GetProperty("tags")[0].GetString() == "a", "Hidden fields are not sent; an emptied number is an explicit null; an untouched list goes back as saved");
                Check(save.RootElement.GetProperty("clear_secrets").GetArrayLength() == 0, "Nothing is cleared that was not chosen");
            }
            await Until(() => Find<PasswordBox>(page, "PluginConfigField_connection_password")?.Password == "" && !page.RequiresLeaveConfirmation, "A saved form is rebuilt from the server: the typed secret is gone");
            await Click(page, "PluginConfigClear_connection_password"); await Click(page, "PluginConfigSave_connection");
            await Until(() => server.Count("PUT /installations/7/config") == 3 && !page.Controller.State.Busy, "Clearing a secret is an explicit save");
            using (var cleared = JsonDocument.Parse(server.Body("PUT /installations/7/config"))) Check(cleared.RootElement.GetProperty("clear_secrets")[0].GetString() == "password" && !cleared.RootElement.GetProperty("value").TryGetProperty("password", out _), "The clear is explicit and no secret is sent");

            // Typed secrets after a save, four outcomes. The server never changes the stored values or the saved secret here, so the page reads back
            // exactly what it showed before; the old form must not be kept just because nothing it was drawn from changed.
            async Task<PasswordBox> PasswordField() { await Until(() => Find<PasswordBox>(page, "PluginConfigField_connection_password") != null, "The form is drawn"); return Find<PasswordBox>(page, "PluginConfigField_connection_password")!; }
            async Task TypeSecret(string text) { var box = await PasswordField(); box.Password = text; await Until(() => page.RequiresLeaveConfirmation, "A typed secret marks the form as edited"); }
            bool NoTypedSecret() => !Descendants(page).OfType<PasswordBox>().Any(p => p.Password.Length > 0);
            int ConfigWrites() => server.Count("PUT /installations/7/config");
            bool LastWriteSends(string text) => server.Body("PUT /installations/7/config").Contains(text);
            // Accepted (204), re-read identical.
            var sent = ConfigWrites(); await TypeSecret("typed-secret-accepted"); await Click(page, "PluginConfigSave_connection");
            await Until(() => ConfigWrites() == sent + 1 && !page.Controller!.State.Busy, "The accepted save is sent once");
            Check(LastWriteSends("typed-secret-accepted"), "The accepted save carried the typed secret");
            await Until(() => NoTypedSecret() && !page.RequiresLeaveConfirmation, "After an accepted save the typed secret is gone from the password box and the form is not edited, though the stored values are the same");
            await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 2 && !page.Controller!.State.Busy, "A later Save is sent");
            Check(!LastWriteSends("typed-secret-accepted"), "A later Save does not send the secret again");
            // Refused: what was typed stays, and the corrected retry carries it.
            await TypeSecret("typed-secret-refused"); server.Next["PUT /installations/{id}/config"] = Behave.Refuse; await Click(page, "PluginConfigSave_connection");
            await Until(() => ConfigWrites() == sent + 3 && page.Controller!.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "The refused save shows the server's reason");
            Check((await PasswordField()).Password == "typed-secret-refused" && page.RequiresLeaveConfirmation, "After a definite refusal the typed secret stays");
            await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 4 && !page.Controller!.State.Busy, "The corrected retry is sent");
            Check(LastWriteSends("typed-secret-refused"), "The corrected retry carries the secret that stayed");
            await Until(() => NoTypedSecret() && !page.RequiresLeaveConfirmation, "After the retry was accepted the typed secret is gone");
            // Unknown (lost answer), re-read identical: the secret is gone, and a later Save does not send it.
            await TypeSecret("typed-secret-unknown"); server.Next["PUT /installations/{id}/config"] = Behave.Lost; await Click(page, "PluginConfigSave_connection");
            await Until(() => ConfigWrites() == sent + 5 && !page.Controller!.State.Busy, "The save whose answer was lost is sent once");
            await Until(() => NoTypedSecret() && !page.RequiresLeaveConfirmation, "After an unknown outcome the typed secret is gone and the form is not edited");
            await Until(() => page.Controller!.State.Unconfirmed.Count == 0, "The read-back after the unknown outcome cleared the block");
            await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 6 && !page.Controller!.State.Busy, "A later Save is sent");
            Check(!LastWriteSends("typed-secret-unknown"), "A later Save does not send the secret an unknown outcome erased");
            // Unknown with the read-back failing, then a save that is never sent: the secret typed for it stays until the block is cleared.
            await TypeSecret("typed-secret-lost-again"); server.Next["PUT /installations/{id}/config"] = Behave.Lost; server.FailInstalled = true; await Click(page, "PluginConfigSave_connection");
            await Until(() => ConfigWrites() == sent + 7 && page.Controller!.State.Unconfirmed.Contains("fixture.plugin") && !page.Controller.State.Busy, "The second lost answer blocks the plugin");
            await Until(() => NoTypedSecret() && !page.RequiresLeaveConfirmation, "After the second unknown outcome the typed secret is gone");
            await TypeSecret("typed-secret-not-sent"); await Click(page, "PluginConfigSave_connection");
            await Until(() => page.Controller!.State.Notice.Contains("could not be confirmed") && !page.Controller.State.Busy, "A save for an unconfirmed plugin is not sent");
            Check(ConfigWrites() == sent + 7 && (await PasswordField()).Password == "typed-secret-not-sent" && page.RequiresLeaveConfirmation, "A save that was never sent keeps what was typed");
            server.FailInstalled = false; await Until(() => Find<Button>(page, "PluginDetailReload") != null, "The page can be read back"); await Click(page, "PluginDetailReload");
            await Until(() => page.Controller!.State.Unconfirmed.Count == 0, "Reading the plugin back unblocks it");
            Check((await PasswordField()).Password == "typed-secret-not-sent", "The read-back keeps the form whose save was never sent");
            await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 8 && !page.Controller!.State.Busy, "The retry is sent once");
            Check(LastWriteSends("typed-secret-not-sent"), "The retry carries the secret that stayed");
            await Until(() => NoTypedSecret() && !page.RequiresLeaveConfirmation, "After the accepted retry the typed secret is gone");

            // A multi-select adds or removes only the clicked value; a stored value the options do not list stays, in its stored order.
            server.ConfigTags = ["a", "legacy"]; await page.Controller!.ReloadAsync();
            CheckBox Tag(string label) => Descendants(page).OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == label);
            await Until(() => Descendants(page).OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Beta") && Tag("Alpha").IsChecked == true, "The stored tags show ticked and the options render");
            Tag("Beta").IsChecked = true; await Until(() => page.RequiresLeaveConfirmation, "Ticking an option edits the form");
            sent = ConfigWrites(); await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 1 && !page.Controller!.State.Busy, "The tag change is sent once");
            using (var tags = JsonDocument.Parse(server.Body("PUT /installations/7/config"))) Check(string.Join(",", tags.RootElement.GetProperty("value").GetProperty("tags").EnumerateArray().Select(t => t.GetString())) == "a,legacy,b", "Ticking Beta adds only Beta: the stored value the options do not list stays, in its order");
            await Until(() => !page.RequiresLeaveConfirmation && Descendants(page).OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Alpha"), "The form is drawn again after the save");
            Tag("Alpha").IsChecked = false; await Until(() => page.RequiresLeaveConfirmation, "Unticking an option edits the form");
            sent = ConfigWrites(); await Click(page, "PluginConfigSave_connection"); await Until(() => ConfigWrites() == sent + 1 && !page.Controller!.State.Busy, "The second tag change is sent once");
            using (var tags = JsonDocument.Parse(server.Body("PUT /installations/7/config"))) Check(string.Join(",", tags.RootElement.GetProperty("value").GetProperty("tags").EnumerateArray().Select(t => t.GetString())) == "legacy", "Unticking Alpha removes only Alpha");
            server.ConfigTags = ["a"]; await page.Controller!.ReloadAsync(); await Until(() => Descendants(page).OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Alpha" && c.IsChecked == true), "The tags are back to the fixture's start");
            server.Next["PUT /installations/{id}/config"] = Behave.Lost; server.FailInstalled = true; Find<TextBox>(page, "PluginConfigField_connection_host")!.Text = "lost-host"; await Click(page, "PluginConfigSave_connection");
            await Until(() => page.Controller.State.Unconfirmed.Contains("fixture.plugin") && !page.Controller.State.Busy, "A lost save answer is an unknown outcome");
            server.FailInstalled = false; await Until(() => Find<Button>(page, "PluginDetailReload") != null, "The page can be read back"); await Click(page, "PluginDetailReload");
            await Until(() => page.Controller.State.Unconfirmed.Count == 0, "Reading the plugin back unblocks it");
            // The connection check.
            Find<TextBox>(page, "PluginConfigField_connection_host")!.Text = "check-host"; await Click(page, "PluginConfigTest_connection");
            await Until(() => AllText(page).Contains("Synthetic connection ok") && server.Count("POST /installations/7/config/test") == 1, "The connection check is sent once and its result shown");
            Find<TextBox>(page, "PluginConfigField_connection_host")!.Text = "check-host-2"; await Until(() => !AllText(page).Contains("Synthetic connection ok"), "Editing a tested value drops its result");

            // Lifecycle from the header: enable/disable, policy, update, restart.
            Find<ToggleSwitch>(page, "PluginEnabled")!.IsOn = false; await Until(() => server.Count("PUT /installations/7") >= 1 && !page.Controller.State.Busy, "Disabling sends one assignment");
            Check(server.Body("PUT /installations/7") == "{\"enabled\":false}" && !server.Find("7")!.Enabled, "The disable body is only the enabled flag");
            await Until(() => Find<ToggleSwitch>(page, "PluginEnabled") is { IsOn: false, IsEnabled: true }, "The toggle shows the saved state"); await Task.Delay(300);
            server.Next["PUT /installations/{id}"] = Behave.Refuse; Find<ToggleSwitch>(page, "PluginEnabled")!.IsOn = true;
            await Until(() => page.Controller.State.Notice.Contains("Synthetic refusal") && !page.Controller.State.Busy, "A refused toggle shows the server's reason");
            await Until(() => Find<ToggleSwitch>(page, "PluginEnabled") is { IsOn: false, IsEnabled: true }, "The toggle follows the saved value after a refusal");
            var policy = Find<ComboBox>(page, "PluginUpdatePolicy")!; policy.SelectedItem = policy.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "off");
            await Until(() => server.Count("PUT /installations/7") >= 3 && !page.Controller.State.Busy, "The policy change is sent"); Check(server.Body("PUT /installations/7") == "{\"update_policy\":\"off\"}", "Only the policy is sent");
            await InvokeMenu(page, "PluginMore", "PluginUpdate"); await WaitDialog(page);
            Check(AllText((ContentDialog)Field(page, "_dialog")!).Contains("Version 1.0.0") && AllText((ContentDialog)Field(page, "_dialog")!).Contains("Version 2.0.0"), "Update names the version it will install");
            await CloseDialog(page, null); Check(server.Count("POST /installations/7/update") == 0, "Cancelling an update sends nothing");
            // The same for the update and uninstall confirmations of a plugin whose name is hostile.
            server.Find("7")!.Name = Server.HostileName; await page.Controller!.ReloadAsync();
            await Until(() => AllText(page).Contains("Acme Verified and signed by Silo. Requests no permissions."), "The installed plugin shows its hostile name as one line");
            await InvokeMenu(page, "PluginMore", "PluginUpdate"); await WaitDialog(page);
            var updateLines = Descendants((ContentDialog)Field(page, "_dialog")!).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToList();
            Check(!updateLines.Any(l => l.Any(IsHidden)) && updateLines.Contains("Plugin ID: fixture.plugin") && updateLines.Contains("Version 1.0.0 → Version 2.0.0") && !updateLines.Any(l => l.StartsWith("Verified")), "The update confirmation has the plugin ID and both versions on their own lines and no forged line");
            await CloseDialog(page, null);
            await InvokeMenu(page, "PluginMore", "PluginUninstall"); await WaitDialog(page);
            var removeDialog = (ContentDialog)Field(page, "_dialog")!; var removeLines = Descendants(removeDialog).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToList();
            Check(!(removeDialog.Title as string ?? "").Any(IsHidden) && !removeLines.Any(l => l.Any(IsHidden)) && removeLines.Contains("Plugin ID: fixture.plugin") && removeLines.Contains("Version 1.0.0") && !removeLines.Any(l => l.StartsWith("Verified")), "The uninstall confirmation has the plugin ID and version on their own lines and no forged line");
            await CloseDialog(page, null); Check(server.Count("DELETE /installations/7") == 0, "Cancelling an uninstall sends nothing");
            server.Find("7")!.Name = "Fixture Plugin"; await page.Controller!.ReloadAsync(); await Until(() => !AllText(page).Contains("Acme Verified"), "The name is back");
            server.Next["POST /installations/{id}/update"] = Behave.Lost; server.FailInstalled = true; await InvokeMenu(page, "PluginMore", "PluginUpdate"); await WaitDialog(page); await CloseDialog(page, "Update to 2.0.0");
            await Until(() => page.Controller.State.Unconfirmed.Contains("fixture.plugin") && !page.Controller.State.Busy, "A lost update answer is an unknown outcome");
            writes = server.Writes.Count; await InvokeMenu(page, "PluginMore", "PluginUpdate"); await WaitDialog(page); await CloseDialog(page, "Update to 2.0.0"); await Until(() => !page.Controller.State.Busy, "The blocked update ended");
            Check(server.Writes.Count == writes, "An update is never replayed while its outcome is unknown");
            server.FailInstalled = false; await page.Controller.ReloadAsync(); await Until(() => page.Controller.State.Unconfirmed.Count == 0, "Reading it back unblocks the plugin");
            await InvokeMenu(page, "PluginMore", "PluginUpdate"); await WaitDialog(page); await CloseDialog(page, "Update to 2.0.0");
            await Until(() => server.Find("7")!.Version == "2.0.0" && !page.Controller.State.Busy, "The update applies once the outcome is known");
            await Capture(outer, "plugins-detail.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(680, 900)); await Task.Delay(120); await Capture(outer, "plugins-detail-compact.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 960));
            // Links: only safe http(s) links exist, and they open through the launcher.
            page.Launcher = uri => { launched.Add(uri); return Task.FromResult(true); };
            Check(Find<HyperlinkButton>(page, "PluginLink_Source code") != null && Find<HyperlinkButton>(page, "PluginLink_Support") == null, "An unsafe support link is not offered");
            InvokeAny(Find<HyperlinkButton>(page, "PluginLink_Source code")!); await Until(() => launched.Count == 1 && launched[0].AbsoluteUri == "https://fixture.invalid/source", "A safe link opens through the launcher");
            Check(AllText(page).Contains("<script>alert(1)</script>"), "Plugin text is shown as plain text, never as markup");
            // Uninstall and restart.
            await InvokeMenu(page, "PluginMore", "PluginUninstall"); await WaitDialog(page);
            Check(AllText((ContentDialog)Field(page, "_dialog")!).Contains("Silo will stop the plugin, then remove its installation, configuration, and installed files. This cannot be undone."), "Uninstall states what it removes");
            await CloseDialog(page, null); Check(server.Count("DELETE /installations/7") == 0, "Cancelling an uninstall sends nothing");
            await InvokeMenu(page, "PluginMore", "PluginUninstall"); await WaitDialog(page); await CloseDialog(page, "Uninstall plugin");
            await Until(() => server.Installed.All(r => r.Id != "7"), "Uninstall is sent once and leaves the page");
            await Until(() => inner.Content is AdminPluginsPage p && p.Controller?.State.Ready(AdminPluginsSection.Installed) == true, "The board shows after an uninstall"); page = (AdminPluginsPage)inner.Content;

            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.failed"] }, page);
            await Until(() => AllText(page).Contains("keeps stopping") || AllText(page).Contains("stopped and won't restart"), "A failing plugin explains itself");
            Check(!AllText(page).Contains("synthetic-secret") && AllText(page).Contains("[redacted]"), "A runtime error is shown with credentials removed");
            await Click(page, "PluginRestartNow"); await Until(() => server.Count("POST /installations/8/restart") == 1 && !page.Controller.State.Busy, "Restart is sent once");
            await Until(() => !AllText(page).Contains("stopped and won't restart"), "The failure banner goes once the plugin runs");

            // Managed plugin: fields and uninstall are locked, the pin is shown. The deployment's host value is empty, which the form's own
            // rule (required) would reject: a locked field the admin cannot correct must not block Save of the fields they can edit.
            server.ConfigValueHost = "";
            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.managed"] }, page);
            await Until(() => Find<TextBox>(page, "PluginConfigField_connection_host") != null, "A managed plugin renders its form");
            Find<ComboBox>(page, "PluginConfigField_connection_mode")!.SelectedItem = Find<ComboBox>(page, "PluginConfigField_connection_mode")!.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "advanced");
            await Until(() => page.RequiresLeaveConfirmation, "Editing an unlocked field edits the form");
            Check(Find<Button>(page, "PluginConfigSave_connection") is { IsEnabled: true } && !AllText(page).Contains("Host is required"), "A locked required field that is empty shows no error and does not block Save");
            sent = server.Count("PUT /installations/12/config"); Find<TextBox>(page, "PluginConfigField_connection_endpoint")!.Text = "https://fixture.invalid/endpoint";
            await Click(page, "PluginConfigSave_connection"); await Until(() => server.Count("PUT /installations/12/config") == sent + 1 && !page.Controller!.State.Busy, "Save is sent although a locked field fails its rule");
            using (var managedSave = JsonDocument.Parse(server.Body("PUT /installations/12/config"))) Check(!managedSave.RootElement.GetProperty("value").TryGetProperty("host", out _) && managedSave.RootElement.GetProperty("value").GetProperty("mode").GetString() == "advanced", "The locked field is not sent and the edited one is");
            server.ConfigValueHost = "fixture-host"; await page.Controller!.ReloadAsync();
            await Until(() => Find<TextBox>(page, "PluginConfigField_connection_host") != null, "The managed form is drawn again");
            Check(Find<TextBox>(page, "PluginConfigField_connection_host") is { IsEnabled: false } && AllText(page).Contains("Managed by the environment") && AllText(page).Contains("The deployment pins this plugin to 1.0.0."), "Managed fields are locked and the pin is shown");
            await InvokeMenu(page, "PluginMore", null); Check(((MenuFlyout)Find<Button>(page, "PluginMore")!.Flyout).Items.OfType<MenuFlyoutItem>().First(i => AutomationProperties.GetAutomationId(i) == "PluginUninstall").IsEnabled == false, "Uninstall is disabled for a managed plugin");
            ((MenuFlyout)Find<Button>(page, "PluginMore")!.Flyout).Hide();
            Check(Find<ComboBox>(page, "PluginUpdatePolicy") == null, "A pinned plugin offers no update policy");

            // Sign-in and scheduled-task bindings; plugin-contributed pages stay disabled.
            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.auth"] }, page);
            await Until(() => Find<ToggleSwitch>(page, "PluginAuth_oidc") != null && Find<ToggleSwitch>(page, "PluginTask_refresh") != null, "Sign-in and scheduled task panels render");
            Check(AllText(page).Contains("Default schedule: Daily at 03:30") && Find<Button>(page, "PluginTaskHistory_refresh") != null, "The task shows its manifest schedule and links to its history");
            Find<ToggleSwitch>(page, "PluginAuth_oidc")!.IsOn = true; await Until(() => server.Count("PUT /installations/13/auth-binding") == 1 && !page.Controller.State.Busy, "The sign-in toggle writes once");
            using (var binding = JsonDocument.Parse(server.Body("PUT /installations/13/auth-binding"))) Check(binding.RootElement.GetProperty("capability_id").GetString() == "oidc" && binding.RootElement.GetProperty("enabled").GetBoolean() && !binding.RootElement.TryGetProperty("callback_mode", out _), "The binding body carries only the official fields");
            Find<ToggleSwitch>(page, "PluginTask_refresh")!.IsOn = false; await Until(() => server.Count("PUT /installations/13/task-bindings/refresh") == 1 && !page.Controller.State.Busy, "The task toggle writes once");
            await InvokeMenu(page, "PluginMore", null); Check(((MenuFlyout)Find<Button>(page, "PluginMore")!.Flyout).Items.OfType<MenuFlyoutItem>().First(i => AutomationProperties.GetAutomationId(i) == "PluginRoute_page").IsEnabled == false, "A plugin-contributed page is a real entry that stays disabled");
            ((MenuFlyout)Find<Button>(page, "PluginMore")!.Flyout).Hide();

            // Paused updates and the unverified notice.
            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.paused"] }, page);
            await Until(() => AllText(page).Contains("Update checks are paused."), "A paused plugin explains why"); Check(AllText(page).Contains("External source."), "The external source notice is shown");
            page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.unverified"] }, page);
            await Until(() => AllText(page).Contains("Updated by upload"), "An uploaded plugin offers no update policy"); Check(AllText(page).Contains("The release signature check failed for Fixture Unverified."), "A failed signature is a warning");

            // 8. A catalog plugin that is not installed, the failure states, and a plugin nobody knows.
            page = await Goto(shell, inner, new AdminRouteRequest(AdminRoute.Plugins) { Segments = ["fixture.second"] }.WithQuery("repository", "2").WithQuery("version", "1.0.0"), page);
            await Until(() => Find<Button>(page, "PluginDetailInstall") != null && AllText(page).Contains("Not installed"), "A catalog plugin previews with an Install button");
            Check(AllText(page).Contains("You can change these after installing.") && AllText(page).Contains("Connection"), "The preview lists the settings it will ask for");
            await Click(page, "PluginDetailInstall"); await WaitDialog(page); await CloseDialog(page, null); Check(server.Count("POST /installations") == 3, "Cancelling the preview install sends nothing more");
            server.FailCatalog = true; page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.nobody"] }, page);
            await Until(() => Find<Button>(page, "PluginDetailRetry") != null && AllText(page).Contains("Couldn't load the plugin catalog"), "A failed catalog read is reachable");
            server.FailCatalog = false; await Click(page, "PluginDetailRetry"); await Until(() => AllText(page).Contains("Plugin not found"), "An unknown plugin is reported as not found");

            // 9. Empty states.
            server.Installed.Clear(); server.Catalog.Clear(); server.Repos.Clear();
            page = await Goto(shell, inner, new(AdminRoute.Plugins), page); await Until(() => AllText(page).Contains("No plugins installed"), "An empty inventory says so");
            page = await Goto(shell, inner, new(AdminRoute.Plugins, new Dictionary<string, string> { ["tab"] = "catalog" }), page);
            await Until(() => AllText(page).Contains("No plugins available") && AllText(page).Contains("No repositories configured. Add one to browse available plugins."), "An empty catalog says so");

            // 10. Authority retirement erases what was typed and sends nothing.
            server.Seed(0); page = await Goto(shell, inner, new(AdminRoute.Plugins) { Segments = ["fixture.plugin"] }, page);
            await Until(() => Find<PasswordBox>(page, "PluginConfigField_connection_password") != null, "The form renders again");
            var retained = page; Find<PasswordBox>(page, "PluginConfigField_connection_password")!.Password = "typed-secret-value"; before = server.Writes.Count;
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" });
            await Until(() => retained.Controller?.State.Installations.Count is null or 0 && !Descendants(retained).OfType<PasswordBox>().Any(p => p.Password.Length > 0), "Authority retirement clears the data and every typed secret");
            Check(server.Writes.Count == before, "Retirement sent nothing");
        }
        finally
        {
            server.Gate?.TrySetResult(); page?.SuspendAdminPage();
            if (inner?.Content is IAdminPageSuspension suspension) suspension.SuspendAdminPage();
            if (inner != null) { inner.Content = null; inner.BackStack.Clear(); inner.ForwardStack.Clear(); }
            if (outer != null) { outer.Content = null; outer.BackStack.Clear(); outer.ForwardStack.Clear(); }
            await Until(() => page == null || !page.IsLoaded, "Pages and dialogs unload before fixture services restore");
            window.Content = oldContent; serviceField.SetValue(null, oldServices); DocumentTitle.SetServerName(oldName);
        }
    }

    private static async Task<AdminPluginsPage> Goto(AdminShellPage shell, Frame inner, AdminRouteRequest route, AdminPluginsPage? previous)
    {
        if (previous != null) await Until(() => Field(previous, "_dialog") == null, "No dialog is still closing before navigating");
        shell.Navigate(route);
        await Until(() => inner.Content is AdminPluginsPage p && p != previous && p.IsLoaded && p.Controller?.State is { Loading: false } s && (s.Ready(AdminPluginsSection.Installed) || s.Failures.ContainsKey(AdminPluginsSection.Installed)), "Page for " + route.Href + " loaded");
        // Elements in a re-created ScrollViewer enter the visual tree one layout pass later.
        await Task.Delay(200); return (AdminPluginsPage)inner.Content;
    }

    private static async Task WaitDialog(AdminPluginsPage page) => await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "A confirmation is open");

    /// <summary>Presses the primary button (or Cancel when null) and waits for the dialog to be gone, so the next click is not lost to a closing dialog.</summary>
    private static async Task CloseDialog(AdminPluginsPage page, string? primary)
    {
        var dialog = (ContentDialog)Field(page, "_dialog")!;
        var button = primary == null ? Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Cancel") : Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == primary);
        InvokeAny(button);
        await Until(() => !dialog.IsLoaded && Field(page, "_dialog") == null, "The confirmation closed");
    }

    private static async Task InvokeMenu(AdminPluginsPage page, string buttonId, string? itemId)
    {
        // The page may redraw (a read finishing) between finding the button and opening its menu, so a menu that did not open is tried again on the current button.
        MenuFlyout? flyout = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Until(() => Find<Button>(page, buttonId) is { IsLoaded: true, IsEnabled: true }, buttonId + " ready");
            var more = Find<Button>(page, buttonId)!; flyout = (MenuFlyout)more.Flyout; flyout.ShowAt(more);
            for (var wait = 0; wait < 80 && !flyout.Items.OfType<MenuFlyoutItem>().All(i => i.IsLoaded); wait++) await Task.Delay(25);
            if (flyout.Items.OfType<MenuFlyoutItem>().All(i => i.IsLoaded)) break;
            flyout.Hide(); await Task.Delay(150);
        }
        Check(flyout != null && flyout.Items.OfType<MenuFlyoutItem>().All(i => i.IsLoaded), "The menu is open");
        if (itemId == null) return;
        var item = flyout.Items.OfType<MenuFlyoutItem>().First(i => AutomationProperties.GetAutomationId(i) == itemId);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    /// <summary>A line break, a control character or a direction control: none may appear inside one line the page shows from a plugin.</summary>
    private static bool IsHidden(char c) => char.IsControl(c) || c is '\u2028' or '\u2029' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T? Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static string AllText(DependencyObject root) => string.Join("\n", Descendants(root).Select(d => d switch { TextBlock t => t.Text, Button b when b.Content is string s => s, ToggleSwitch w => w.OnContent?.ToString() ?? "", _ => "" }).Where(t => t.Length > 0));
    private static bool IsVisible(FrameworkElement element) { for (DependencyObject? e = element; e != null; e = VisualTreeHelper.GetParent(e)) if (e is UIElement { Visibility: Visibility.Collapsed }) return false; return true; }
    private static void InvokeAny(UIElement element) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(element).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Click(DependencyObject root, string id) { await Until(() => Find<Button>(root, id) is { IsLoaded: true, IsEnabled: true }, id + " ready"); InvokeAny(Find<Button>(root, id)!); }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
