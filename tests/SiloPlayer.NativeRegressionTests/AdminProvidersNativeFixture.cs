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
    // Official en.json at a84b2551b: web.admin.settings.providers.subtitles.* and web.common.subtitles.providers.*.
    private const string SavedApplied = "Settings saved and applied on this server. Other servers may still use older settings.";
    private const string LoadFailedText = "Unable to load saved configuration. Reload before editing.";
    private const string LoadingText = "Loading saved configuration...";
    private const string ReloadLabel = "Reload saved configuration (discard draft)";
    private const string NotConfirmedLine = "Save not confirmed. Your draft is retained. Reload saved configuration to review it before making another change.";
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
        private readonly List<string> _reads = [];
        internal bool SubtitleLost;
        /// <summary>Provider names whose editor read fails (a 503, or a lost answer when <see cref="SubtitleReadLost"/>).</summary>
        internal volatile string[] FailingReads = [];
        internal bool SubtitleReadLost;
        /// <summary>While set, every provider editor read waits for it.</summary>
        internal TaskCompletionSource? SubtitleReadGate;
        internal bool OddProvider;
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

        internal const string OddName = "odd\u2028na\u202Eme";
        /// <summary>GET requests whose path is exactly <paramref name="path"/> (the request log the Reload checks read).</summary>
        internal int ReadCount(string path) { lock (_gate) return _reads.Count(read => read == path); }
        /// <summary>Every request the server has seen, reads and writes.</summary>
        internal int RequestCount { get { lock (_gate) return _reads.Count + _writes.Count; } }
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
            if (method == HttpMethod.Get) lock (_gate) _reads.Add(Uri.UnescapeDataString(path));
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
                    var name = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
                    if (SubtitleReadGate != null) await SubtitleReadGate.Task;
                    if (FailingReads.Contains(name))
                    {
                        if (SubtitleReadLost) throw new HttpRequestException("synthetic lost answer");
                        return Status(503);
                    }
                    return Json(Subtitle(name), "\"providers-native-subtitle-" + name.Length + "\"");
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
                var parts = path.Split('/'); var name = Uri.UnescapeDataString(parts[5]);
                if (path.EndsWith("/test", StringComparison.Ordinal))
                    return Json(new { success = SubtitleTestOk, error = SubtitleTestOk ? null : "rejected synthetic-pass" });
                if (SubtitleLost) throw new HttpRequestException("synthetic lost answer");
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

        internal Server WithOddProvider() { _subtitles[OddName] = (true, true, false); OddProvider = true; return this; }
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
        internal static Server Odd() => new Server().WithOddProvider();
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
        await Scenario(window, "subtitle page reload", new Server(), SubtitlePageReloadAsync);
        await Scenario(window, "subtitle second tile after a conflict", new Server(), env => SubtitleSecondTileAsync(env, 412, lost: false, "412 conflict"));
        await Scenario(window, "subtitle second tile after a server error", new Server(), env => SubtitleSecondTileAsync(env, 503, lost: false, "503 unknown outcome"));
        await Scenario(window, "subtitle second tile after a lost answer", new Server(), env => SubtitleSecondTileAsync(env, 0, lost: true, "lost answer"));
        await Scenario(window, "subtitle reload failure", new Server(), SubtitleReloadFailureAsync);
        await Scenario(window, "subtitle page reload failure", new Server(), SubtitlePageReloadFailureAsync);
        await Scenario(window, "subtitle dialog text", Server.Odd(), SubtitleDialogTextAsync);
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
            if (waitLoaded)
            {
                await Until(() => page.Controller?.State.Loaded == true, "Providers loaded");
                // Loaded is the controller state; the page paints it from a queued render. Every loaded scenario shows the MDBList tile.
                await Until(() => Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "ProvidersMDBListOpen" && Shown(button)), "Providers painted");
            }
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
        // The controller sets its state first and the page paints it from a queued render: wait for what the admin sees.
        await Until(() => page.Controller?.State.Error != null && Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "ProvidersError" && text.Text.Length > 0 && Shown(text))
            && Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "ProvidersReload" && Shown(button)), "load error shown");
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
        await DestructiveStyleAsync(Dialog(page)!, "MDBList disconnect");
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
        var writesBefore = server.WriteCount;
        await Task.Delay(300);
        Check(writesBefore == 0 && server.WriteCount == 0, "a Disconnect confirmed for a replaced authority wrote nothing (" + writesBefore + " writes before and " + server.WriteCount + " after the wait)");
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

        const string NotConfirmed = "Save not confirmed. Your draft is retained. Reload saved configuration to review it before making another change.";
        const string Provider = "/api/v2/admin/subtitle-providers/opensubtitles";
        // Owner decision (official tile): after ANY failed save the tile says so and its fields and Save stay off; the tile's own Reload,
        // pressed once, re-reads only that provider, discards the drafts and re-arms Save.
        foreach (var (status, lost, label) in new[] { (400, false, "rejected (400)"), (412, false, "conflict (412)"), (503, false, "server error (503)"), (0, true, "lost answer") })
        {
            server.SubtitlePut = status; server.SubtitleLost = lost; var before = server.Count("PUT");
            // The server holds the provider on. The admin turns it off: a failed save must not leave that choice looking like the server's value.
            Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn = false;
            if (status == 400)
            {
                // A fresh test result is on screen when the save fails; the official tile clears it on success only.
                await Click(page, "ProvidersSubtitleTest_opensubtitles");
                await Until(() => HasText(page, "Connection successful."), "a connection-test result before the failed save");
            }
            await Click(page, "ProvidersSubtitleSave_opensubtitles");
            await Until(() => Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles"), label + ": the tile shows save not confirmed");
            Check(server.Count("PUT") == before + 1, label + ": the save was sent exactly once");
            Check(server.LastBody.Contains("\"enabled\":false", StringComparison.Ordinal), label + ": the save carried the admin's switch choice (off)");
            Check(!Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn, label + ": the unsaved choice stays visible, disabled, until Reload");
            if (status == 400) Check(HasText(page, "Connection successful."), label + ": a failed save leaves the connection-test result standing (the official tile clears it on success only)");
            Check(HasText(page, NotConfirmed), label + ": the official 'save not confirmed' text is shown");
            Check(!Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && !Find<Button>(page, "ProvidersSubtitleTest_opensubtitles").IsEnabled &&
                !Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").IsEnabled && !Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").IsEnabled &&
                !Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsEnabled, label + ": the fields, Test and Save are disabled");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "synthetic-user" && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "synthetic-pass",
                label + ": the draft is retained and shown disabled, as the official tile does");
            Check(Find<Button>(page, "ProvidersSubtitleReload_opensubtitles").IsEnabled, label + ": the tile's own Reload is reachable");

            var providerReads = server.ReadCount(Provider); var listReads = server.ReadCount("/api/v2/admin/subtitle-providers"); var writesBefore = server.WriteCount;
            server.SubtitlePut = 200; server.SubtitleLost = false;
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, label + ": one press of the tile's Reload re-arms Save");
            Check(!Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles"), label + ": the not-confirmed state is gone");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "" && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "",
                label + ": the username and password boxes are empty after Reload");
            Check(Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn, label + ": the enable switch is back on the server's value (on) after the admin had turned it off");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").IsEnabled && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").IsEnabled && Find<Button>(page, "ProvidersSubtitleTest_opensubtitles").IsEnabled,
                label + ": the fields and Test are enabled again");
            Check(server.ReadCount(Provider) == providerReads + 1 && server.ReadCount("/api/v2/admin/subtitle-providers") == listReads, label + ": Reload made one read of that provider and no list read");
            Check(server.WriteCount == writesBefore, label + ": Reload sent no write (no PUT or POST)");
            Check(env.Controller.State.ProviderFeedback is null or "", label + ": no stale page feedback remains");
            var requestsBefore = server.RequestCount;
            await Task.Delay(300);
            Check(server.RequestCount == requestsBefore, label + ": nothing was requested while the page settled (" + requestsBefore + " requests before and after)");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "" && !Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles") && Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled,
                label + ": clearing the boxes was not read back as an edit");
            // Type again for the next failure kind.
            Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
            await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
        }
        server.SubtitlePut = 200; server.SubtitleLost = false;

        server.SubtitlePut = 200; var writes = server.Count("PUT");
        await Click(page, "ProvidersSubtitleDisconnect_opensubtitles");
        await Until(() => Dialog(page) is { IsLoaded: true }, "subtitle disconnect confirmation");
        Check(Dialog(page)!.Title?.ToString() == "Clear OpenSubtitles credentials?", "subtitle disconnect asks for confirmation");
        await DestructiveStyleAsync(Dialog(page)!, "subtitle disconnect");
        Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "subtitle disconnect cancelled");
        Check(server.Count("PUT") == writes, "cancelling the confirmation writes nothing");
        await Click(page, "ProvidersSubtitleDisconnect_opensubtitles");
        await Until(() => Dialog(page) is { IsLoaded: true }, "subtitle disconnect confirmation again");
        Confirm(Dialog(page)!);
        await Until(() => server.Count("PUT") == writes + 1, "credentials cleared");
        Check(server.LastBody.Contains("\"clear_credentials\":true", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":false", StringComparison.Ordinal), "disconnect saves the provider disabled with credentials cleared");
        // The official tile stays open on its success line and stays off until Reload re-reads the provider.
        await Until(() => HasText(page, SavedApplied), "the tile shows the official success line");
        Check(Has(page, "ProvidersSubtitleClose_opensubtitles") && !Has(page, "ProvidersSubtitleOpen_opensubtitles"), "a successful save leaves the tile open");
        Check(!HasText(page, "Connection successful.") && !Shown(Find<TextBlock>(page, "ProvidersFeedback")), "the save success cleared the test result and shows no page line");
        Check(!Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && !Find<Button>(page, "ProvidersSubtitleTest_opensubtitles").IsEnabled &&
            !Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").IsEnabled && !Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").IsEnabled &&
            !Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsEnabled, "after a save the fields, Test and Save stay off until Reload");
        Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "" && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "" &&
            !Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn, "the typed credentials were cleared and the switch shows the value that was saved (off)");
        var readsBeforeReload = server.ReadCount(Provider); var writesBeforeReload = server.WriteCount;
        await Click(page, "ProvidersSubtitleReload_opensubtitles");
        await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, "Reload re-arms the saved tile");
        Check(!HasText(page, SavedApplied) && server.ReadCount(Provider) == readsBeforeReload + 1 && server.WriteCount == writesBeforeReload, "Reload ended the success line with one read of that provider and no write");
        Check(!Has(page, "ProvidersSubtitleDisconnect_opensubtitles"), "after Reload the cleared provider is no longer offered for Disconnect");

        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), "SubDL editor");
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl"), "synthetic-subdl-key");
        await Until(() => Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn, "typing the first key switches the provider on");
        await Click(page, "ProvidersSubtitleSave_subdl");
        await Until(() => server.Count("PUT") == writes + 2, "SubDL saved");
        Check(server.LastBody.Contains("\"api_key\":\"synthetic-subdl-key\"", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":true", StringComparison.Ordinal), "connecting SubDL saves the key with the provider on");
    }

    /// <summary>Another tile is not confirmed; opening a second provider leaves it so, and one page Reload press recovers every tile.</summary>
    private static async Task SubtitlePageReloadAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");
        Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
        server.SubtitlePut = 503;
        await Click(page, "ProvidersSubtitleSave_opensubtitles");
        await Until(() => env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles"), "OpenSubtitles not confirmed");
        await Click(page, "ProvidersSubtitleClose_opensubtitles");

        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), "SubDL editor");
        Check(env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles") && !env.Controller.State.SubtitleSaveNotConfirmed("subdl"), "opening another provider leaves the first tile not confirmed");
        Check(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl").IsEnabled && !Has(page, "ProvidersSubtitleNotConfirmed_subdl"), "the other tile is untouched and editable");
        Check(Shown(Find<Button>(page, "ProvidersReload")), "the page Reload is reachable while a tile is not confirmed");

        server.SubtitlePut = 200; var writes = server.WriteCount;
        await Click(page, "ProvidersReload");
        await Until(() => !env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles") && !env.Controller.State.Loading, "page Reload recovers the tile");
        Check(server.WriteCount == writes, "the page Reload wrote nothing");
        await Click(page, "ProvidersSubtitleClose_subdl");
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitleSave_opensubtitles"), "OpenSubtitles editor reopened");
        Check(Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "" &&
            Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "", "after the page Reload the tile has Save armed and empty drafts");
    }

    /// <summary>
    /// Provider A is "save not confirmed" (after a 412, a 503 or a lost answer). Provider B must still be saved, and provider C disconnected,
    /// through their own controls: a control that is drawn enabled must act or say why, and each save is sent exactly once to its own provider.
    /// </summary>
    private static async Task SubtitleSecondTileAsync(Env env, int failure, bool lost, string kind)
    {
        var page = env.Page; var server = env.Server; var prefix = kind + ": ";
        const string ProviderPath = "PUT /api/v2/admin/subtitle-providers/";
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), prefix + "OpenSubtitles editor");
        Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
        server.SubtitlePut = failure; server.SubtitleLost = lost;
        await Click(page, "ProvidersSubtitleSave_opensubtitles");
        await Until(() => env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles"), prefix + "OpenSubtitles not confirmed");
        server.SubtitlePut = 200; server.SubtitleLost = false;
        var openSubtitlesPuts = server.Count(ProviderPath + "opensubtitles");
        Check(openSubtitlesPuts == 1, prefix + "the failed save was sent once");

        // B: SubDL, saved through its own controls while A is not confirmed.
        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), prefix + "SubDL editor");
        Check(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl").IsEnabled && Find<Button>(page, "ProvidersSubtitleSave_subdl").IsEnabled &&
            Find<Button>(page, "ProvidersSubtitleTest_subdl").IsEnabled && Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsEnabled &&
            !Has(page, "ProvidersSubtitleNotConfirmed_subdl"), prefix + "another provider's tile is drawn editable while OpenSubtitles is not confirmed");
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl"), "synthetic-subdl-key");
        await Click(page, "ProvidersSubtitleTest_subdl");
        await Until(() => HasText(page, "Connection successful."), prefix + "SubDL test result");
        var before = server.Count("PUT");
        await ClickActs(env, "ProvidersSubtitleSave_subdl", prefix + "SubDL Save");
        await Until(() => server.Count(ProviderPath + "subdl") == 1, prefix + "SubDL saved");
        await Until(() => HasText(page, SavedApplied), prefix + "SubDL shows the official success line");
        Check(server.Count("PUT") == before + 1 && server.Count(ProviderPath + "opensubtitles") == openSubtitlesPuts, prefix + "exactly one PUT was sent, to SubDL, and OpenSubtitles was not sent again");
        Check(server.LastBody.Contains("\"api_key\":\"synthetic-subdl-key\"", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":true", StringComparison.Ordinal), prefix + "the SubDL write carries its key and the provider on");
        Check(!HasText(page, "Connection successful."), prefix + "the successful save cleared the SubDL test result");
        Check(Has(page, "ProvidersSubtitleClose_subdl") && !Find<Button>(page, "ProvidersSubtitleSave_subdl").IsEnabled && !Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl").IsEnabled,
            prefix + "SubDL stays open and off until Reload");
        Check(env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles") && !env.Controller.State.SubtitleSaveNotConfirmed("subdl"), prefix + "OpenSubtitles is still not confirmed and SubDL is not");

        // C: SubSource is configured; its Disconnect works through its own control and confirmation.
        await Click(page, "ProvidersSubtitleOpen_subsource");
        await Until(() => Has(page, "ProvidersSubtitleDisconnect_subsource"), prefix + "SubSource editor");
        Check(Find<Button>(page, "ProvidersSubtitleDisconnect_subsource").IsEnabled, prefix + "SubSource Disconnect is drawn enabled");
        before = server.Count("PUT");
        await ClickActs(env, "ProvidersSubtitleDisconnect_subsource", prefix + "SubSource Disconnect", confirm: true);
        await Until(() => server.Count(ProviderPath + "subsource") == 1, prefix + "SubSource disconnected");
        Check(server.Count("PUT") == before + 1 && server.LastBody.Contains("\"clear_credentials\":true", StringComparison.Ordinal) && server.LastBody.Contains("\"enabled\":false", StringComparison.Ordinal),
            prefix + "exactly one PUT, to SubSource, disabling it and clearing its credentials");
        await Until(() => HasText(page, SavedApplied), prefix + "SubSource shows the official success line");

        // A is recovered by its own Reload, one press, with no other write.
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles"), prefix + "OpenSubtitles reopens as not confirmed");
        var writes = server.WriteCount;
        await Click(page, "ProvidersSubtitleReload_opensubtitles");
        await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, prefix + "OpenSubtitles Reload re-arms Save");
        Check(server.WriteCount == writes && !env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles"), prefix + "Reload wrote nothing and ended the not-confirmed state");
    }

    /// <summary>
    /// A failed Reload of one tile, as the official tile: the editor is dropped, the whole tile is off, it says it could not be loaded, and
    /// Reload stays reachable until one read succeeds. Also: the switch follows the server after Reload (both directions), and a Reload in
    /// progress reads "Loading saved configuration...", never "Saving".
    /// </summary>
    private static async Task SubtitleReloadFailureAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        const string Provider = "/api/v2/admin/subtitle-providers/opensubtitles";

        // A provider that is off on the server: turning it on and failing the save, then Reload, shows it off again.
        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), "SubDL editor");
        Check(!Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn, "SubDL is off on the server and the switch shows it");
        Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn = true;
        server.SubtitlePut = 503;
        await Click(page, "ProvidersSubtitleSave_subdl");
        await Until(() => Has(page, "ProvidersSubtitleNotConfirmed_subdl"), "SubDL not confirmed");
        Check(Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn && !Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsEnabled, "SubDL: the unsaved choice (on) stays visible, disabled");
        server.SubtitlePut = 200;
        await Click(page, "ProvidersSubtitleReload_subdl");
        await Until(() => Find<Button>(page, "ProvidersSubtitleSave_subdl").IsEnabled, "SubDL Reload re-arms Save");
        Check(!Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_subdl").IsOn, "SubDL: after Reload the switch is back on the server's value (off) after the admin had turned it on");

        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");

        // Reading is not saving.
        server.SubtitleReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Click(page, "ProvidersSubtitleReload_opensubtitles");
        await Until(() => HasText(page, LoadingText), "the tile says it is loading");
        Check(Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").Content?.ToString() == "Save" && !HasText(page, "Saving"), "while a tile is read its Save reads 'Save', never 'Saving…'");
        Check(!Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && !Find<Button>(page, "ProvidersSubtitleReload_opensubtitles").IsEnabled, "Save and Reload are off while the read is pending");
        server.SubtitleReadGate.SetResult(); server.SubtitleReadGate = null;
        await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && !HasText(page, LoadingText), "the read finished");

        foreach (var (lost, kind) in new[] { (false, "503"), (true, "lost answer") })
        {
            // A tile that was only being edited, never saved.
            Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
            await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
            Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn = false;
            server.FailingReads = ["opensubtitles"]; server.SubtitleReadLost = lost;
            var reads = server.ReadCount(Provider); var writes = server.WriteCount;
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Has(page, "ProvidersSubtitleLoadFailed_opensubtitles"), kind + ": the tile says it could not be loaded");
            Check(HasText(page, LoadFailedText), kind + ": the official 'Unable to load saved configuration' text is shown in the tile");
            Check(!Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled && !Find<Button>(page, "ProvidersSubtitleTest_opensubtitles").IsEnabled &&
                !Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").IsEnabled && !Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").IsEnabled &&
                !Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsEnabled, kind + ": the whole tile is off (fields, Test, Save, switch)");
            Check(!Has(page, "ProvidersSubtitleDisconnect_opensubtitles") || !Find<Button>(page, "ProvidersSubtitleDisconnect_opensubtitles").IsEnabled, kind + ": Disconnect is off");
            Check(Find<Button>(page, "ProvidersSubtitleReload_opensubtitles").IsEnabled && Find<Button>(page, "ProvidersSubtitleReload_opensubtitles").Content?.ToString() == ReloadLabel, kind + ": Reload is reachable and carries the official label");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "synthetic-user" && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "synthetic-pass",
                kind + ": the typed values stay visible and disabled, as the official tile keeps them on a failed read");
            Check(server.ReadCount(Provider) == reads + 1 && server.WriteCount == writes, kind + ": the failed Reload made one read and sent no write");
            Check(!HasText(page, "Saving"), kind + ": a failed read is not shown as saving");

            // Reload again, still failing, then healthy.
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => server.ReadCount(Provider) == reads + 2 && !env.Controller.State.Loading && env.Controller.State.BusyProvider == null, kind + ": second failing Reload");
            Check(Has(page, "ProvidersSubtitleLoadFailed_opensubtitles") && !Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, kind + ": still off after a second failed Reload");
            server.FailingReads = []; server.SubtitleReadLost = false;
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, kind + ": a successful Reload re-arms the tile");
            Check(!Has(page, "ProvidersSubtitleLoadFailed_opensubtitles") && server.ReadCount(Provider) == reads + 3, kind + ": the notice is gone after one successful read");
            Check(Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text == "" && Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles").Password == "" &&
                Find<ToggleSwitch>(page, "ProvidersSubtitleEnabled_opensubtitles").IsOn && Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").IsEnabled,
                kind + ": the drafts were discarded, the switch follows the server and the fields are on");
            Check(server.WriteCount == writes, kind + ": no request other than reads was sent");

            // A failed save, then a failed Reload: the tile reads "could not be loaded" (one line, as the official tile), then recovers.
            Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
            await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
            server.SubtitlePut = 503; var puts = server.Count("PUT");
            await Click(page, "ProvidersSubtitleSave_opensubtitles");
            await Until(() => Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles"), kind + ": a failed save is not confirmed");
            server.SubtitlePut = 200; server.FailingReads = ["opensubtitles"]; server.SubtitleReadLost = lost;
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Has(page, "ProvidersSubtitleLoadFailed_opensubtitles"), kind + ": the failed Reload after a failed save");
            Check(!HasText(page, NotConfirmedLine) && HasText(page, LoadFailedText) && !Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, kind + ": one line at a time, and the tile stays off");
            Check(env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles"), kind + ": a failed read does not settle the not-confirmed state");
            server.FailingReads = []; server.SubtitleReadLost = false;
            await Click(page, "ProvidersSubtitleReload_opensubtitles");
            await Until(() => Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, kind + ": the next Reload re-arms the tile");
            Check(!env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles") && !Has(page, "ProvidersSubtitleNotConfirmed_opensubtitles") && server.Count("PUT") == puts + 1,
                kind + ": recovered, and the one failed save was never sent again");
        }

        // A tile whose very first read fails has nothing to edit; it says so, can be closed, and recovers on Reload.
        server.FailingReads = ["subsource"];
        await Click(page, "ProvidersSubtitleOpen_subsource");
        await Until(() => Has(page, "ProvidersSubtitleLoadFailed_subsource"), "SubSource could not be loaded");
        Check(HasText(page, LoadFailedText) && Find<Button>(page, "ProvidersSubtitleReload_subsource").Content?.ToString() == ReloadLabel && Has(page, "ProvidersSubtitleClose_subsource") && !Has(page, "ProvidersSubtitleSave_subsource"),
            "a tile that never loaded shows the official text, the official Reload label and Close, and offers no Save");
        server.FailingReads = [];
        await Click(page, "ProvidersSubtitleReload_subsource");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subsource") && Find<Button>(page, "ProvidersSubtitleSave_subsource").IsEnabled, "SubSource recovered on Reload");
    }

    /// <summary>
    /// The page Reload re-reads the opened tiles that wait for a read. When the first tile's read fails and the next tile's read succeeds,
    /// the failure is still on the page (the next read starts by clearing the page line) and in its own tile.
    /// </summary>
    private static async Task SubtitlePageReloadFailureAsync(Env env)
    {
        var page = env.Page; var server = env.Server;
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");
        Find<TextBox>(page, "ProvidersSubtitleUsername_opensubtitles").Text = "synthetic-user";
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitlePassword_opensubtitles"), "synthetic-pass");
        server.SubtitlePut = 503;
        await Click(page, "ProvidersSubtitleSave_opensubtitles");
        await Until(() => env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles"), "OpenSubtitles not confirmed");
        await Click(page, "ProvidersSubtitleOpen_subdl");
        await Until(() => Has(page, "ProvidersSubtitleApiKey_subdl"), "SubDL editor");
        await Secret(Find<PasswordBox>(page, "ProvidersSubtitleApiKey_subdl"), "synthetic-subdl-key");
        await Click(page, "ProvidersSubtitleSave_subdl");
        await Until(() => env.Controller.State.SubtitleSaveNotConfirmed("subdl"), "SubDL not confirmed");

        // OpenSubtitles (first in the list) cannot be read; SubDL (second) can.
        server.SubtitlePut = 200; server.FailingReads = ["opensubtitles"];
        var writes = server.WriteCount;
        await Click(page, "ProvidersReload");
        await Until(() => env.Controller.State.SubtitleLoadFailed("opensubtitles") && !env.Controller.State.SubtitleSaveNotConfirmed("subdl") &&
            !env.Controller.State.Loading && env.Controller.State.BusyProvider == null, "the page Reload read both tiles");
        await Until(() => Shown(Find<TextBlock>(page, "ProvidersFeedback")) && Find<TextBlock>(page, "ProvidersFeedback").Text.StartsWith("Could not load provider configuration", StringComparison.Ordinal),
            "the first failed read stays on the page although the next tile's read succeeded");
        Check(server.WriteCount == writes, "the page Reload wrote nothing");
        await Click(page, "ProvidersSubtitleClose_subdl");
        await Click(page, "ProvidersSubtitleOpen_opensubtitles");
        await Until(() => Has(page, "ProvidersSubtitleLoadFailed_opensubtitles"), "the failed tile says so when it is opened");
        Check(HasText(page, LoadFailedText) && !Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, "the first tile keeps its own failure and stays off after the next tile's read");

        server.FailingReads = [];
        await Click(page, "ProvidersReload");
        await Until(() => !env.Controller.State.SubtitleLoadFailed("opensubtitles") && !env.Controller.State.SubtitleSaveNotConfirmed("opensubtitles") &&
            !env.Controller.State.Loading && env.Controller.State.BusyProvider == null, "the page Reload recovered the failed tile");
        await Until(() => Has(page, "ProvidersSubtitlePassword_opensubtitles") && Find<Button>(page, "ProvidersSubtitleSave_opensubtitles").IsEnabled, "the recovered tile is armed");
    }

    /// <summary>A third-party provider name never reaches a dialog title with control, line-separator or direction characters.</summary>
    private static async Task SubtitleDialogTextAsync(Env env)
    {
        var page = env.Page;
        await Click(page, "ProvidersSubtitleOpen_odd_na_me");
        await Until(() => Has(page, "ProvidersSubtitleDisconnect_odd_na_me"), "odd provider editor");
        await Click(page, "ProvidersSubtitleDisconnect_odd_na_me");
        await Until(() => Dialog(page) is { IsLoaded: true }, "odd provider confirmation");
        Check(Dialog(page)!.Title?.ToString() == "Clear odd name credentials?", "a provider name is neutralized in the confirmation title");
        Check(!(Dialog(page)!.Content?.ToString() ?? "").Any(character => character is '\u2028' or '\u202E'), "the confirmation text carries no separator or direction character");
        Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "odd provider confirmation closed");
    }

    /// <summary>The confirmation's action is drawn in the official destructive fill by a page-local style, darker on hover.</summary>
    private static async Task DestructiveStyleAsync(ContentDialog dialog, string label)
    {
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) { Program.Log("INFO: " + label + ": contrast theme active; destructive fill not asserted"); return; }
        var primary = Descendants(dialog).OfType<Button>().First(button => button.Content?.ToString() == dialog.PrimaryButtonText);
        Check(primary.Background is SolidColorBrush { Color: { A: 0xFF, R: 0xDC, G: 0x26, B: 0x26 } }, label + ": the action uses the official destructive fill #dc2626");
        Check(!ReferenceEquals(primary.Style, Application.Current.Resources["DestructiveButtonStyle"]), label + ": the page-local style is used, not the shared one");
        var root = Descendants(primary).OfType<Border>().FirstOrDefault(border => border.Name == "ProvidersDestructiveRoot");
        Check(root != null, label + ": the page-local template is applied");
        if (root == null) return;
        static bool Full(Border border) => border.Background is SolidColorBrush { Color: { A: 0xFF, R: 0xDC, G: 0x26, B: 0x26 } } && border.Opacity == 1;
        static bool Ninety(Border border) => border.Background is SolidColorBrush { Color: { R: 0xDC, G: 0x26, B: 0x26, A: >= 0xE5 and <= 0xE6 } };
        async Task<bool> Reaches(Func<bool> condition)
        {
            for (var i = 0; i < 80 && !condition(); i++) await Task.Delay(25);
            return condition();
        }
        VisualStateManager.GoToState(primary, "PointerOver", false);
        Check(await Reaches(() => Ninety(root)), label + ": hover draws the fill at 90%");
        VisualStateManager.GoToState(primary, "Pressed", false);
        Check(await Reaches(() => Ninety(root)), label + ": pressed draws the fill at 90%");
        VisualStateManager.GoToState(primary, "Disabled", false);
        Check(await Reaches(() => root.Opacity == 0.5) && root.Background is SolidColorBrush { Color: { A: 0xFF, R: 0xDC, G: 0x26, B: 0x26 } }, label + ": disabled keeps the destructive fill at half opacity");
        VisualStateManager.GoToState(primary, "Normal", false);
        Check(await Reaches(() => Full(root)), label + ": normal restores the full fill and opacity");
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

    /// <summary>Requests seen plus every visible page text: a control that is drawn enabled must change this (send a request or say why not).</summary>
    private static string Snapshot(Env env) => env.Server.RequestCount + "|" + string.Join("\u00A6", Descendants(env.Page).OfType<TextBlock>().Where(Shown).Select(text => text.Text));

    /// <summary>
    /// Clicks an enabled control, answers its confirmation when asked, and fails the check when nothing observable followed: no request and
    /// no message. This is the page-wide rule that an enabled control must act or say why it did not.
    /// </summary>
    private static async Task ClickActs(Env env, string id, string label, bool confirm = false)
    {
        var before = Snapshot(env);
        await Click(env.Page, id);
        if (confirm)
        {
            await Until(() => Dialog(env.Page) is { IsLoaded: true }, label + ": confirmation");
            Confirm(Dialog(env.Page)!);
        }
        for (var i = 0; i < 200 && Snapshot(env) == before; i++) await Task.Delay(25);
        Check(Snapshot(env) != before, label + ": the enabled control acted or said why (a request or a message followed)");
    }

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
