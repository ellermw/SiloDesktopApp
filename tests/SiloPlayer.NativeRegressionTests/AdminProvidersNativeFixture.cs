using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Production registration/shell/Frame fixture for Configure > Providers. It drives the real page through loading, error, empty,
/// populated, failed, unknown-outcome and replaced-authority states, every dialog and every mutation, on synthetic data only.
/// </summary>
internal static class AdminProvidersNativeFixture
{
    private const string Marker = "ProvidersMarker";
    private const string MarkerId = "plugin_6_introdb";
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label) { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) Failures.Add(label); }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 8000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    /// <summary>A scriptable synthetic Silo server. Every credential in it is made up.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<string> _writes = [];
        private readonly List<string> _bodies = [];
        internal bool FailEffective, FailSensitiveStatus, FailSubtitles, FailMarkers, FailPlugins, EmptySubtitles, EmptyMarkers, EmptyPlugins, MdblistConfigured = true;
        internal int SettingsPut = 200, SubtitlePut = 200, MarkerPut = 200, MarkerValidateStatus = 200;
        internal bool MdblistCheckOk = true, SubtitleTestOk = true, MarkerValid = true, MarkerFetchEnabled = true;
        internal TaskCompletionSource? LoadGate, MarkerGate;
        internal int MarkerPriority = 10;
        internal double MarkerFetchConfidence = .4;
        private readonly Dictionary<string, (bool Enabled, bool HasKey, bool HasCredentials)> _subtitles = new()
        {
            ["opensubtitles"] = (true, false, true), ["subdl"] = (false, false, false), ["subsource"] = (false, true, false),
        };

        internal int Count(string prefix) { lock (_gate) return _writes.Count(write => write.StartsWith(prefix, StringComparison.Ordinal)); }
        internal int WriteCount { get { lock (_gate) return _writes.Count; } }
        internal string LastBody { get { lock (_gate) return _bodies.Count == 0 ? "" : _bodies[^1]; } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var method = request.Method;
            string body = "";
            if (method != HttpMethod.Get)
            {
                body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                lock (_gate) { _writes.Add(method + " " + path); _bodies.Add(body); }
            }
            if (method == HttpMethod.Get)
            {
                switch (path)
                {
                    case "/api/v2/admin/settings/effective":
                        if (LoadGate != null) await LoadGate.Task;
                        return FailEffective ? Status(503) : Json(new Dictionary<string, string> { ["markers.mode"] = "both" }, "\"providers-native-settings\"");
                    case "/api/v2/admin/settings/sensitive-status":
                        if (FailSensitiveStatus) return Status(503);
                        return Json(new { configured = MdblistConfigured ? new[] { "mdblist.api_key" } : Array.Empty<string>(), managed_by_env = Array.Empty<string>() });
                    case "/api/v2/admin/settings/restart-keys":
                        return Json(new { keys = new[] { "mdblist.api_key" }, prefixes = Array.Empty<string>() });
                    case "/api/v2/admin/subtitle-providers":
                        if (FailSubtitles) return Status(503);
                        return Json(new { providers = EmptySubtitles ? Array.Empty<object>() : _subtitles.Select(item => Subtitle(item.Key)).ToArray() });
                    case "/api/v2/admin/markers/providers":
                        if (FailMarkers) return Status(503);
                        return Json(new { providers = EmptyMarkers ? Array.Empty<object>() : new[] { MarkerProvider() } });
                    case "/api/v2/admin/plugins/installations":
                        if (FailPlugins) return Status(503);
                        return Json(new { items = EmptyPlugins ? Array.Empty<object>() : Plugins(), page = new { has_more = false } });
                    case "/api/v2/admin/server/status": return Json(new { restart_required = false, restart_mark_count = 0, restart_requested = false });
                    case "/api/v2/admin/system/build": return Json(new { display = "synthetic", available = true, build_number = 1 });
                }
                if (path.StartsWith("/api/v2/admin/subtitle-providers/", StringComparison.Ordinal))
                {
                    var name = path[(path.LastIndexOf('/') + 1)..];
                    return Json(Subtitle(name), "\"providers-native-subtitle-" + name + "\"");
                }
                return Json(new { });
            }
            if (path == "/api/v2/admin/settings" && method == HttpMethod.Put)
            {
                if (SettingsPut != 200) return Failure(SettingsPut);
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.GetProperty("values").TryGetProperty("mdblist.api_key", out var key)) MdblistConfigured = !string.IsNullOrEmpty(key.GetString());
                return Json(new { values = new Dictionary<string, string>(), restart_required = false }, "\"providers-native-settings-2\"");
            }
            if (path == "/api/v2/admin/settings/check/mdblist")
                return Json(new { success = MdblistCheckOk, message = MdblistCheckOk ? "MDBList responded." : "rejected key synthetic-mdblist-key" });
            if (path.StartsWith("/api/v2/admin/subtitle-providers/", StringComparison.Ordinal))
            {
                var parts = path.Split('/'); var name = parts[5];
                if (path.EndsWith("/test", StringComparison.Ordinal))
                    return Json(new { success = SubtitleTestOk, error = SubtitleTestOk ? null : "rejected synthetic-pass" });
                if (SubtitlePut != 200) return Failure(SubtitlePut);
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                var current = _subtitles[name];
                if (root.TryGetProperty("clear_credentials", out var clear) && clear.GetBoolean()) current = (false, false, false);
                else
                {
                    current.Enabled = root.GetProperty("enabled").GetBoolean();
                    if (root.TryGetProperty("api_key", out var apiKey) && !string.IsNullOrEmpty(apiKey.GetString())) current.HasKey = true;
                    if (root.TryGetProperty("password", out var password) && !string.IsNullOrEmpty(password.GetString())) current.HasCredentials = true;
                }
                _subtitles[name] = current;
                return Json(new { saved_revision = "5", local_apply = "applied", local_applied_revision = "5" });
            }
            if (path.StartsWith("/api/v2/admin/markers/providers/", StringComparison.Ordinal))
            {
                if (path.EndsWith("/validate", StringComparison.Ordinal))
                {
                    if (MarkerValidateStatus != 200) return Status(MarkerValidateStatus);
                    return MarkerValid
                        ? Json(new { valid = true, stats = new { total = 12, accepted = 9, pending = 2, rejected = 1, acceptance_rate = .75, current_streak = 3, best_streak = 5 } })
                        : Json(new { valid = false, error = "provider rejected the account" });
                }
                if (MarkerGate != null) await MarkerGate.Task;
                if (MarkerPut != 200) return Failure(MarkerPut);
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                if (root.TryGetProperty("fetch_priority", out var priority)) MarkerPriority = priority.GetInt32();
                if (root.TryGetProperty("fetch_min_confidence", out var confidence)) MarkerFetchConfidence = confidence.GetDouble();
                if (root.TryGetProperty("fetch_enabled", out var fetch)) MarkerFetchEnabled = fetch.GetBoolean();
                return Json(MarkerProvider());
            }
            return Json(new { });
        }

        private object Subtitle(string name) { var item = _subtitles[name]; return new { provider_name = name, enabled = item.Enabled, has_api_key = item.HasKey, has_credentials = item.HasCredentials, updated_at = "2026-11-17T00:00:00Z" }; }
        private object MarkerProvider() => new
        {
            provider = "plugin:6:introdb", display_name = "TheIntroDB", source_type = "plugin", plugin_id = "silo.introdb",
            plugin_installation_id = "6", capability_id = "markers", is_submitter = true, fetch_enabled = MarkerFetchEnabled, fetch_priority = MarkerPriority,
            fetch_min_confidence = MarkerFetchConfidence, contribute_enabled = true, contribute_auto_local = true, contribute_min_confidence = .95,
            default_contribute_enabled = true,
        };
        private static object[] Plugins() =>
        [
            new { id = 4, plugin_id = "silo.simkl", enabled = true, capabilities = new[] { new { id = "watch", type = "watch_sync_provider.v1", display_name = "Simkl", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
            new { id = 5, plugin_id = "silo.tmdb", enabled = true, capabilities = new[] { new { id = "metadata", type = "metadata_provider.v1", display_name = "TMDB", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
            new { id = 6, plugin_id = "silo.introdb", enabled = true, capabilities = new[] { new { id = "markers", type = "marker_provider.v1", display_name = "TheIntroDB", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
        ];
        private static HttpResponseMessage Failure(int status) => Json(status == 412 ? (object)new { type = "precondition_failed", status } : status == 400 ? new { type = "invalid_request", status } : new { }, null, (HttpStatusCode)status);
        private static HttpResponseMessage Status(int status) => Json(new { }, null, (HttpStatusCode)status);
        private static HttpResponseMessage Json(object body, string? etag = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            if (etag != null) response.Headers.ETag = new(etag);
            return response;
        }
    }

    private sealed class Env
    {
        internal required Server Server { get; init; }
        internal required AdminProvidersPage Page { get; init; }
        internal required AuthService Auth { get; init; }
        internal required AdminMonitorAuthority Authority { get; init; }
        internal required Window Window { get; init; }
        internal AdminProvidersController Controller => Page.Controller!;
    }

    internal static async Task RunAsync(Window window)
    {
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(new SiloApiClient(new HttpClient()))).ApplyTheme("midnight-cinema");
        await Scenario(window, "loading", new Server { LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }, LoadingAsync, waitLoaded: false);
        await Scenario(window, "load error", new Server { FailEffective = true }, LoadErrorAsync, waitLoaded: false);
        await Scenario(window, "empty", new Server { EmptySubtitles = true, EmptyMarkers = true, EmptyPlugins = true, MdblistConfigured = false }, EmptyAsync);
        await Scenario(window, "section failures", new Server { FailSubtitles = true, FailMarkers = true, FailPlugins = true }, SectionFailuresAsync);
        await Scenario(window, "credential status unavailable", new Server { FailSensitiveStatus = true }, CredentialStatusUnavailableAsync);
        await Scenario(window, "populated", new Server(), PopulatedAsync);
        await Scenario(window, "mdblist", new Server(), MdblistAsync);
        await Scenario(window, "disconnect after authority replacement", new Server(), DisconnectRaceAsync);
        await Scenario(window, "subtitles", new Server(), SubtitlesAsync);
        await Scenario(window, "markers", new Server(), MarkersAsync);
        if (Failures.Count > 0) throw new InvalidOperationException("Providers fixture failures: " + string.Join("; ", Failures));
    }

    private static async Task Scenario(Window window, string name, Server server, Func<Env, Task> body, bool waitLoaded = true)
    {
        Program.Log("SCENARIO: " + name);
        using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client))
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!; var previous = field.GetValue(null);
        var previousContent = window.Content; Frame? outer = null; AdminProvidersPage? page = null;
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 820));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/settings/providers") is { Kind: AdminRouteResolutionKind.Route }, name + ": Providers resolves through production registration");
            await Until(() => outer.Content is AdminShellPage, "Admin shell");
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminProvidersPage, "Providers page");
            page = (AdminProvidersPage)inner.Content;
            if (waitLoaded) await Until(() => page.Controller?.State.Loaded == true, "Providers loaded");
            outer.UpdateLayout();
            await body(new Env { Server = server, Page = page, Auth = auth, Authority = authority, Window = window });
        }
        finally
        {
            var closing = page == null ? null : Dialog(page);
            page?.SuspendAdminPage();
            try { await Until(() => (page == null || Dialog(page) == null) && (closing == null || !closing.IsLoaded), "dialogs closed before globals restore"); } catch (InvalidOperationException) { Failures.Add(name + ": dialog did not close"); }
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            try { await Until(() => page == null || !page.IsLoaded, "page unloaded before globals restore"); } catch (InvalidOperationException) { Failures.Add(name + ": page did not unload"); }
            window.Content = previousContent; await Task.Delay(100); field.SetValue(null, previous);
        }
    }

    // ---- loading / error / empty -------------------------------------------------------------------------------------

    private static async Task LoadingAsync(Env env)
    {
        var page = env.Page;
        await Until(() => page.Controller != null, "page activated");
        await Until(() => Descendants(page).OfType<StackPanel>().Any(panel => AutomationProperties.GetName(panel) == "Loading provider settings" && Shown(panel)), "loading skeleton");
        Check(env.Controller.State.Loaded == false && !HasText(page, "Subtitle providers"), "loading: skeletons replace the provider groups until the snapshot arrives");
        env.Server.LoadGate!.SetResult();
        await Until(() => env.Controller.State.Loaded, "loading released");
        Check(HasText(page, "Subtitle providers") && !Descendants(page).OfType<StackPanel>().Any(panel => AutomationProperties.GetName(panel) == "Loading provider settings" && Shown(panel)), "loading: groups replace the skeleton");
    }

    private static async Task LoadErrorAsync(Env env)
    {
        var page = env.Page;
        await Until(() => page.Controller?.State.Error != null, "load error");
        Check(Find<TextBlock>(page, "ProvidersError").Text == "Could not load provider settings. Try again." && Shown(Find<TextBlock>(page, "ProvidersError")), "error: the load failure is announced");
        Check(!HasText(page, "Subtitle providers"), "error: no stale or partial provider groups");
        Check(Shown(Find<Button>(page, "ProvidersReload")) && Find<Button>(page, "ProvidersReload").IsEnabled, "error: Reload is reachable");
        env.Server.FailEffective = false;
        await Click(page, "ProvidersReload");
        await Until(() => env.Controller.State.Loaded && env.Controller.State.Error == null, "recovered after reload");
        Check(HasText(page, "Subtitle providers") && !Shown(Find<TextBlock>(page, "ProvidersError")), "error: Reload recovers the page");
    }

    private static Task EmptyAsync(Env env)
    {
        var page = env.Page;
        Check(HasText(page, "No watch provider plugins are installed.") && HasText(page, "No subtitle providers are available."), "empty: watch and subtitle groups explain the empty state");
        Check(HasText(page, "No marker provider plugins are installed. Install one from the plugin catalog.") && Has(page, "ProvidersMarkerCatalog"), "empty: marker group links to the plugin catalog");
        Check(Find<Button>(page, "ProvidersMDBListOpen").Content?.ToString() == "Connect" && HasText(page, "Not connected"), "empty: MDBList is not connected");
        return Task.CompletedTask;
    }

    private static Task SectionFailuresAsync(Env env)
    {
        var page = env.Page;
        Check(HasText(page, "Could not load subtitle providers.") && HasText(page, "Could not load marker providers.") && HasText(page, "Could not load installed plugins."), "section failures: each independent source reports its own failure");
        Check(env.Controller.State.Loaded && Has(page, "ProvidersMDBListOpen"), "section failures: MDBList still renders");
        Check(Shown(Find<Button>(page, "ProvidersReload")), "section failures: Reload is reachable when a section did not load");
        return Task.CompletedTask;
    }

    private static async Task CredentialStatusUnavailableAsync(Env env)
    {
        var page = env.Page;
        Check(env.Controller.State.Loaded && env.Controller.State.SensitiveStatusError, "status unavailable: the page still loads");
        Check(Shown(Find<Button>(page, "ProvidersReload")) && HasText(page, "Status unavailable"), "status unavailable: the tile says so and Reload is reachable");
        await Click(page, "ProvidersMDBListOpen");
        await Until(() => Has(page, "ProvidersMDBListSave"), "status unavailable: MDBList tile opens");
        Check(Has(page, "ProvidersMDBListStatusUnavailable") && !Find<Button>(page, "ProvidersMDBListSave").IsEnabled && !Find<PasswordBox>(page, "ProvidersMDBListApiKey").IsEnabled,
            "status unavailable: the reason is shown and no key can be typed or saved");
        env.Server.FailSensitiveStatus = false;
        await Click(page, "ProvidersReload");
        await Until(() => !env.Controller.State.SensitiveStatusError && !env.Controller.State.Loading && !Has(page, "ProvidersMDBListStatusUnavailable"), "status unavailable: Reload recovers the credential status");
        Check(!Shown(Find<Button>(page, "ProvidersReload")), "status unavailable: Reload goes away once everything loaded");
    }

    // ---- populated ---------------------------------------------------------------------------------------------------

    private static async Task PopulatedAsync(Env env)
    {
        var page = env.Page; var window = env.Window;
        Check(AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.ProviderSettings), "Providers route is registered");
        foreach (var title in new[] { "Providers", "Watch providers", "Lists", "Subtitle providers", "Marker providers" })
            Check(HasText(page, title), "group '" + title + "' renders");
        Check(HasText(page, "Simkl") && Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "ProvidersMetadataNote" && text.Text.Contains("TMDB", StringComparison.Ordinal)),
            "watch status and metadata plugin ownership note render");
        Check(Has(page, "ProvidersPluginCatalog") && Has(page, "ProvidersMetadataOpen") && Has(page, "ProvidersMarkerSource"), "plugin catalog, metadata and Marker source links render");
        Check(HasText(page, "OpenSubtitles") && HasText(page, "SubDL") && HasText(page, "SubSource") && HasText(page, "TheIntroDB"), "subtitle and marker provider inventory renders");
        Check(HasText(page, "Online marker lookup is enabled."), "marker lookup note follows the saved marker mode");
        Check(HasText(page, "Connected") && HasText(page, "Connected · off"), "subtitle states distinguish connected and connected-off");
        await Click(page, "ProvidersMDBListOpen");
        Check(Find<PasswordBox>(page, "ProvidersMDBListApiKey").Password == "", "saved MDBList secret is represented only by status");
        Check(Has(page, "ProvidersMDBListKeyLink") && HasText(page, "A server restart may be required after this setting changes."), "MDBList setup link and pending-restart note render");
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");
        Check(!Has(page, "ProvidersMDBListClose"), "opening a subtitle provider collapses the MDBList editor");
        Check(Has(page, "ProvidersSubtitleUsername_opensubtitles") && Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles exposes username and write-only password");
        await Click(page, "ProvidersSubtitleClose_opensubtitles");
        await Click(page, Marker + "Open_" + MarkerId);
        foreach (var id in new[] { "Fetch", "Share", "Auto" })
            Check(Has(page, Marker + id + "_" + MarkerId), id + " marker toggle renders");
        await Click(page, Marker + "Advanced_" + MarkerId);
        foreach (var id in new[] { "Priority", "FetchConfidence", "ShareConfidence" })
            Check(Has(page, Marker + id + "_" + MarkerId), id + " renders under Advanced");
        var scroller = (ScrollViewer)typeof(AdminProvidersPage).GetField("_scroller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "Providers has no horizontal overflow at 1280 px");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(760, 820)); await Task.Delay(400); page.UpdateLayout();
        Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "Providers has no horizontal overflow at compact desktop width");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 820)); await Task.Delay(200);
        await Click(page, Marker + "Close_" + MarkerId);
        await Click(page, "ProvidersMDBListOpen");
        await Secret(Find<PasswordBox>(page, "ProvidersMDBListApiKey"), "synthetic-retired-draft");
        env.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Until(() => env.Controller.State.Owner == null, "authority retirement");
        await Until(() => !Descendants(page).OfType<PasswordBox>().Any(), "private controls removed");
        Check(env.Controller.State.Values.Count == 0, "authority retirement clears provider values, drafts and private controls");
    }

    // ---- MDBList -----------------------------------------------------------------------------------------------------

    private static async Task MdblistAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        await Click(page, "ProvidersMDBListOpen");
        await Secret(Find<PasswordBox>(page, "ProvidersMDBListApiKey"), "synthetic-mdblist-key");
        await Until(() => Find<Button>(page, "ProvidersMDBListSave").IsEnabled && Find<Button>(page, "ProvidersMDBListTest").IsEnabled, "typing a key enables Save and Test connection");

        server.MdblistCheckOk = false;
        await Click(page, "ProvidersMDBListTest");
        await Until(() => HasText(page, "rejected key"), "failed connection check");
        Program.Log("INFO: mdblist result texts: " + string.Join(" | ", Descendants(page).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Contains("rejected", StringComparison.Ordinal))));
        Check(!HasText(page, "synthetic-mdblist-key") && HasText(page, "<redacted>"), "a failed check never echoes the typed key");
        server.MdblistCheckOk = true;
        await Click(page, "ProvidersMDBListTest");
        await Until(() => HasText(page, "MDBList responded."), "successful connection check");
        Check(server.Count("PUT") == 0, "checking a connection writes nothing");

        await Click(page, "ProvidersMDBListSave");
        await Until(() => server.Count("PUT /api/v2/admin/settings") == 1 && env.Controller.State.ProviderFeedback == "MDBList API key saved.", "saved");
        await Until(() => Find<PasswordBox>(page, "ProvidersMDBListApiKey").Password == "", "secret field cleared after save");
        Check(Shown(Find<TextBlock>(page, "ProvidersFeedback")) && Find<TextBlock>(page, "ProvidersFeedback").Text == "MDBList API key saved.", "save success is announced");
        Check(server.LastBody.Contains("mdblist.api_key", StringComparison.Ordinal), "the one write carries only the MDBList key");

        server.SettingsPut = 400;
        await Secret(Find<PasswordBox>(page, "ProvidersMDBListApiKey"), "synthetic-second-key");
        await Click(page, "ProvidersMDBListSave");
        await Until(() => env.Controller.State.Error == "Failed to update MDBList settings.", "failed save");
        Check(server.Count("PUT /api/v2/admin/settings") == 2 && Shown(Find<TextBlock>(page, "ProvidersError")), "a rejected save is reported once and never replayed");
        Check(Find<PasswordBox>(page, "ProvidersMDBListApiKey").Password == "synthetic-second-key", "a rejected save keeps the typed key");

        server.SettingsPut = 503;
        await Click(page, "ProvidersMDBListSave");
        await Until(() => env.Controller.State.SettingsOutcomeUnknown, "unknown outcome");
        Check(server.Count("PUT /api/v2/admin/settings") == 3, "the unknown-outcome save was sent exactly once");
        Check(Shown(Find<Button>(page, "ProvidersReload")) && Find<Button>(page, "ProvidersReload").IsEnabled && !Find<Button>(page, "ProvidersMDBListSave").IsEnabled, "unknown outcome blocks Save and keeps Reload reachable");
        await Click(page, "ProvidersReload");
        await Until(() => !env.Controller.State.SettingsOutcomeUnknown && env.Controller.State.Loaded, "reloaded after unknown outcome");
        Check(server.Count("PUT /api/v2/admin/settings") == 3, "reload reconciles without replaying the write");

        server.SettingsPut = 200;
        await Click(page, "ProvidersMDBListDisconnect");
        await Until(() => Dialog(page) is { IsLoaded: true }, "disconnect confirmation");
        Check(Dialog(page)!.Title?.ToString() == "Clear MDBList credentials?", "MDBList disconnect asks for confirmation");
        Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "disconnect cancelled");
        Check(server.Count("PUT /api/v2/admin/settings") == 3, "cancelling the confirmation writes nothing");
        await Click(page, "ProvidersMDBListDisconnect");
        await Until(() => Dialog(page) is { IsLoaded: true }, "disconnect confirmation again");
        Confirm(Dialog(page)!);
        await Until(() => server.Count("PUT /api/v2/admin/settings") == 4 && env.Controller.State.ProviderFeedback == "MDBList credentials cleared.", "credentials cleared");
        await Until(() => !Has(page, "ProvidersMDBListDisconnect"), "Disconnect hidden once cleared");
        Check(server.LastBody.Contains("\"mdblist.api_key\":\"\"", StringComparison.Ordinal), "disconnect clears the key with one write");
    }

    private static async Task DisconnectRaceAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        var firstOwner = env.Controller.State.Owner;
        await Click(page, "ProvidersMDBListOpen");
        await Click(page, "ProvidersMDBListDisconnect");
        await Until(() => Dialog(page) is { IsLoaded: true }, "disconnect confirmation");
        // The authority is replaced while the confirmation is open, then the stale confirmation is accepted before the page reacts.
        env.Authority.Retire();
        Confirm(Dialog(page)!);
        await Until(() => Dialog(page) == null, "stale confirmation closed");
        await Until(() => env.Controller.State.Loaded && !ReferenceEquals(env.Controller.State.Owner, firstOwner) && env.Controller.State.Owner != null, "page reopened for the replacement authority");
        await Task.Delay(300);
        Check(server.WriteCount == 0, "a Disconnect confirmed for a replaced authority wrote nothing");
        Check(env.Controller.State.Configured.Contains("mdblist.api_key"), "the replacement authority still shows the key as configured");
    }

    // ---- subtitle providers ------------------------------------------------------------------------------------------

    private static async Task SubtitlesAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");
        Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");

        server.SubtitleTestOk = false;
        await Click(page, "ProvidersSubtitleTest_opensubtitles");
        await Until(() => HasText(page, "rejected"), "failed provider test");
        Program.Log("INFO: subtitle result texts: " + string.Join(" | ", Descendants(page).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Contains("rejected", StringComparison.Ordinal))));
        Check(!HasText(page, "synthetic-pass") && HasText(page, "<redacted>"), "a failed test never echoes the typed password");
        server.SubtitleTestOk = true;
        await Click(page, "ProvidersSubtitleTest_opensubtitles");
        await Until(() => HasText(page, "Connection successful."), "successful provider test");
        Check(server.Count("PUT") == 0, "testing a provider writes nothing");

        foreach (var (status, expected, label) in new[]
        {
            (400, "Provider settings were not saved", "rejected"),
            (503, "outcome is unknown", "unknown outcome"),
            (412, "changed on the server", "conflict"),
        })
        {
            server.SubtitlePut = status; var before = server.Count("PUT");
            await Click(page, "ProvidersSubtitleSave_opensubtitles");
            await Until(() => (env.Controller.State.ProviderFeedback ?? "").Contains(expected, StringComparison.Ordinal), label + " feedback");
            Check(server.Count("PUT") == before + 1 && !Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, label + ": sent once and Save stays blocked until the editor is reloaded");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "synthetic-user", label + ": the typed username is retained");
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, label + ": reload re-arms Save");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "synthetic-user", label + ": the draft survives the reload");
        }

        server.SubtitlePut = 200; var writes = server.Count("PUT");
        await Click(page, "ProvidersSubtitleDisconnect_opensubtitles");
        await Until(() => Dialog(page) is { IsLoaded: true }, "subtitle disconnect confirmation");
        Check(Dialog(page)!.Title?.ToString() == "Clear OpenSubtitles credentials?", "subtitle disconnect asks for confirmation");
        Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "subtitle disconnect cancelled");
        Check(server.Count("PUT") == writes, "cancelling the confirmation writes nothing");
        await Click(page, "ProvidersSubtitleDisconnect_opensubtitles");
        await Until(() => Dialog(page) is { IsLoaded: true }, "subtitle disconnect confirmation again");
        Confirm(Dialog(page)!);
        await Until(() => server.Count("PUT") == writes + 1, "credentials cleared");
        Check(server.LastBody.Contains("\"clear_credentials\":true", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":false", StringComparison.Ordinal), "disconnect saves the provider disabled with credentials cleared");
        await Until(() => Has(page, "ProvidersSubtitleOpen_opensubtitles") && HasText(page, "Provider settings saved and applied."), "saved and collapsed");

        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), "SubDL editor");
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl"), "synthetic-subdl-key");
        await Until(() => Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn, "typing the first key switches the provider on");
        await Click(page, "ProvidersSubtitleSave_subdl");
        await Until(() => server.Count("PUT") == writes + 2, "SubDL saved");
        Check(server.LastBody.Contains("\"api_key\":\"synthetic-subdl-key\"", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":true", StringComparison.Ordinal), "connecting SubDL saves the key with the provider on");
    }

    // ---- marker providers --------------------------------------------------------------------------------------------

    private static async Task MarkersAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        string Id(string name) => Marker + name + "_" + MarkerId;
        await Click(page, Id("Open"));
        await Click(page, Id("Advanced"));
        Find<TextBox>(page, Id("Priority")).Text = "5";
        await Until(() => Find<Button>(page, Id("Save")).IsEnabled, "Save enabled for an edit");

        server.MarkerPut = 400;
        await Click(page, Id("Save"));
        await Until(() => server.Count("PUT /api/v2/admin/markers") == 1 && env.Controller.State.BusyProvider == null && !string.IsNullOrEmpty(env.Controller.State.ProviderFeedback), "rejected marker save");
        await Until(() => Find<Button>(page, Id("Save")).IsEnabled, "Save available again");
        Check(!env.Controller.State.MarkerOutcomeUnknown && Find<TextBox>(page, Id("Priority")).Text == "5" && Find<Button>(page, Id("Save")).IsEnabled, "a rejected marker save keeps the draft and Save available");

        // Edits typed while a save is pending survive its acknowledgement.
        server.MarkerPut = 200; server.MarkerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Click(page, Id("Save"));
        await Until(() => server.Count("PUT /api/v2/admin/markers") == 2 && env.Controller.State.BusyProvider != null, "marker save pending");
        await Task.Delay(250);
        Find<TextBox>(page, Id("Priority")).Text = "10";
        Find<TextBox>(page, Id("FetchConfidence")).Text = "30";
        var sent = server.LastBody;
        server.MarkerGate.SetResult(); server.MarkerGate = null;
        await Until(() => env.Controller.State.BusyProvider == null && env.Controller.State.MarkerProviders[0].FetchPriority == 5, "marker save acknowledged");
        await Task.Delay(250);
        Check(sent.Contains("\"fetch_priority\":5", StringComparison.Ordinal) && !sent.Contains("fetch_min_confidence", StringComparison.Ordinal), "the pending save carried only the edit made before it was sent");
        Check(Has(page, Id("Save")), "the tile stays open because edits remain");
        Check(Find<TextBox>(page, Id("Priority")).Text == "10" && Find<TextBox>(page, Id("FetchConfidence")).Text == "30" && Find<Button>(page, Id("Save")).IsEnabled,
            "edits typed while the save was pending are kept, including one that returned to the old saved value");

        await Click(page, Id("Save"));
        await Until(() => server.Count("PUT /api/v2/admin/markers") == 3 && Has(page, Id("Open")), "remaining edits saved and tile closed");
        Check(server.LastBody.Contains("\"fetch_priority\":10", StringComparison.Ordinal) && server.LastBody.Contains("\"fetch_min_confidence\":0.3", StringComparison.Ordinal), "the follow-up save sends the remaining edits as changed fields only");

        // Unknown outcome: Save is blocked and a reload is reachable from the page and from the tile.
        await Click(page, Id("Open"));
        Find<ToggleSwitch>(page, Id("Fetch")).IsOn = false;
        await Until(() => Find<Button>(page, Id("Save")).IsEnabled, "Save enabled for the toggle");
        server.MarkerPut = 503;
        await Click(page, Id("Save"));
        await Until(() => env.Controller.State.MarkerOutcomeUnknown, "marker unknown outcome");
        await Until(() => Shown(Find<Button>(page, "ProvidersReload")) && Has(page, Id("Reload")), "reload reachable");
        Check(server.Count("PUT /api/v2/admin/markers") == 4 && !Find<Button>(page, Id("Save")).IsEnabled, "the unknown-outcome marker save was sent once and Save is blocked");
        Check(Find<Button>(page, "ProvidersReload").IsEnabled && Find<Button>(page, Id("Reload")).IsEnabled, "page and tile Reload are both enabled");
        await Click(page, Id("Reload"));
        await Until(() => !env.Controller.State.MarkerOutcomeUnknown && env.Controller.State.Loaded && !env.Controller.State.Loading, "marker outcome reconciled");
        await Until(() => Has(page, Id("Save")), "tile rendered again");
        Check(server.Count("PUT /api/v2/admin/markers") == 4 && !Find<ToggleSwitch>(page, Id("Fetch")).IsOn && Find<Button>(page, Id("Save")).IsEnabled, "reload reconciles without replaying and retains the edited draft");

        // Connection test (validate), failed test and unknown test outcome.
        await Click(page, Id("Test"));
        await Until(() => HasText(page, "Connection successful.") && HasText(page, "Total submissions 12"), "marker validation with statistics");
        server.MarkerValid = false;
        await Click(page, Id("Test"));
        await Until(() => HasText(page, "provider rejected the account"), "failed marker validation");
        server.MarkerValidateStatus = 503;
        await Click(page, Id("Test"));
        await Until(() => (env.Controller.State.ProviderFeedback ?? "").Contains("test outcome is unknown", StringComparison.Ordinal), "unknown test outcome");
        Check(server.Count("POST /api/v2/admin/markers") == 3, "each marker test was sent once and the unknown one was not repeated");
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------

    private static ContentDialog? Dialog(AdminProvidersPage page) => (ContentDialog?)typeof(AdminProvidersPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void Confirm(ContentDialog dialog) =>
        ((IInvokeProvider)new ButtonAutomationPeer(Descendants(dialog).OfType<Button>().First(button => button.Content?.ToString() == dialog.PrimaryButtonText)).GetPattern(PatternInterface.Invoke)).Invoke();
    private static bool Shown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return element.IsLoaded;
    }
    /// <summary>PasswordChanged is delivered after the assignment returns; give it time so the page draft has the value before a command reads it.</summary>
    private static async Task Secret(PasswordBox box, string value) { box.Password = value; await Task.Delay(300); }
    private static bool Has(DependencyObject root, string id) => Descendants(root).Any(item => AutomationProperties.GetAutomationId(item) == id);
    private static bool HasText(DependencyObject root, string text) => Descendants(root).OfType<TextBlock>().Any(item => item.Text.Contains(text, StringComparison.Ordinal) && Shown(item));
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().Single(item => AutomationProperties.GetAutomationId(item) == id);
    private static async Task Click(DependencyObject root, string id)
    {
        await Until(() => Descendants(root).OfType<Button>().Any(item => AutomationProperties.GetAutomationId(item) == id && item.IsEnabled && Shown(item)), id);
        var button = Descendants(root).OfType<Button>().Single(item => AutomationProperties.GetAutomationId(item) == id);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(120);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
