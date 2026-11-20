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

/// <summary>Production registration/Frame fixture for Configure > Providers. Uses synthetic credentials only.</summary>
internal static class AdminProvidersNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label) { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) Failures.Add(label); }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private sealed class Server : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body = path switch
            {
                "/api/v2/admin/settings/effective" => new Dictionary<string, string> { ["markers.mode"] = "both" },
                "/api/v2/admin/settings/sensitive-status" => new { configured = new[] { "mdblist.api_key" }, managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "mdblist.api_key" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/subtitle-providers" => new { providers = new[]
                {
                    new { provider_name = "opensubtitles", enabled = true, has_api_key = false, has_credentials = true, updated_at = "2026-11-17T00:00:00Z" },
                    new { provider_name = "subdl", enabled = false, has_api_key = false, has_credentials = false, updated_at = "2026-11-17T00:00:00Z" },
                    new { provider_name = "subsource", enabled = true, has_api_key = true, has_credentials = false, updated_at = "2026-11-17T00:00:00Z" },
                } },
                "/api/v2/admin/markers/providers" => new { providers = new[] { new
                {
                    provider = "plugin:6:introdb", display_name = "TheIntroDB", source_type = "plugin", plugin_id = "silo.introdb",
                    plugin_installation_id = "6", capability_id = "markers", is_submitter = true, fetch_enabled = true, fetch_priority = 10,
                    fetch_min_confidence = .4, contribute_enabled = true, contribute_auto_local = true, contribute_min_confidence = .95,
                    default_contribute_enabled = true,
                } } },
                "/api/v2/admin/plugins/installations" => new { items = new object[]
                {
                    new { id = 4, plugin_id = "silo.simkl", enabled = true, capabilities = new[] { new { id = "watch", type = "watch_sync_provider.v1", display_name = "Simkl", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
                    new { id = 5, plugin_id = "silo.tmdb", enabled = true, capabilities = new[] { new { id = "metadata", type = "metadata_provider.v1", display_name = "TMDB", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
                    new { id = 6, plugin_id = "silo.introdb", enabled = true, capabilities = new[] { new { id = "markers", type = "marker_provider.v1", display_name = "TheIntroDB", description = "", config_schema = Array.Empty<object>() } }, global_config_schema = Array.Empty<object>(), global_configs = Array.Empty<object>() },
                }, page = new { has_more = false } },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ when path.StartsWith("/api/v2/admin/subtitle-providers/", StringComparison.Ordinal) => Subtitle(path[(path.LastIndexOf('/') + 1)..]),
                _ => new { },
            };
            var response = Json(body);
            if (path == "/api/v2/admin/settings/effective" || path.StartsWith("/api/v2/admin/subtitle-providers/", StringComparison.Ordinal))
                response.Headers.ETag = new("\"providers-native-v1\"");
            return Task.FromResult(response);
        }
        private static object Subtitle(string provider) => new { provider_name = provider, enabled = provider != "subdl", has_api_key = provider == "subsource", has_credentials = provider == "opensubtitles", updated_at = "2026-11-17T00:00:00Z" };
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
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
        var previousContent = window.Content; Frame? outer = null;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 820));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/settings/providers") is { Kind: AdminRouteResolutionKind.Route }, "Providers resolves through production registration");
            await Until(() => outer.Content is AdminShellPage, "Admin shell");
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminProvidersPage { Controller.State.Loaded: true }, "Providers page");
            var page = (AdminProvidersPage)inner.Content; outer.UpdateLayout();
            Check(AdminShellPage.CreateRegistry().IsRegistered(AdminRoute.ProviderSettings), "Providers route is registered");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "Simkl") &&
                Descendants(page).OfType<TextBlock>().Any(text => AutomationProperties.GetAutomationId(text) == "ProvidersMetadataNote" && text.Text.Contains("TMDB", StringComparison.Ordinal)),
                "watch status and metadata plugin ownership note render");
            Check(Descendants(page).OfType<HyperlinkButton>().Any(link => AutomationProperties.GetAutomationId(link) == "ProvidersPluginCatalog") &&
                Descendants(page).OfType<HyperlinkButton>().Any(link => AutomationProperties.GetAutomationId(link) == "ProvidersMetadataOpen"),
                "plugin catalog and metadata handoff links render");
            Check(Descendants(page).OfType<TextBlock>().Any(text => text.Text == "OpenSubtitles") && Descendants(page).OfType<TextBlock>().Any(text => text.Text == "TheIntroDB"), "subtitle and marker provider inventory renders");
            await Click(page, "ProvidersMDBListOpen");
            Check(Descendants(page).OfType<PasswordBox>().Single(box => AutomationProperties.GetAutomationId(box) == "ProvidersMDBListApiKey").Password == "", "saved MDBList secret is represented only by status");
            Check(Descendants(page).OfType<HyperlinkButton>().Any(link => AutomationProperties.GetAutomationId(link) == "ProvidersMDBListKeyLink"), "MDBList setup link renders");
            await Click(page, "ProvidersSubtitleOpen_opensubtitles");
            await Until(() => Descendants(page).OfType<PasswordBox>().Any(box => AutomationProperties.GetAutomationId(box) == "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles editor");
            Check(!Descendants(page).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "ProvidersMDBListClose"), "opening a subtitle provider collapses the MDBList editor");
            Check(Descendants(page).OfType<TextBox>().Any(box => AutomationProperties.GetAutomationId(box) == "ProvidersSubtitleUsername_opensubtitles") &&
                Descendants(page).OfType<PasswordBox>().Any(box => AutomationProperties.GetAutomationId(box) == "ProvidersSubtitlePassword_opensubtitles"), "OpenSubtitles exposes username and write-only password");
            await Click(page, "ProvidersSubtitleClose_opensubtitles");
            await Click(page, "ProvidersMarkerOpen_plugin_6_introdb");
            foreach (var id in new[] { "ProvidersMarkerFetch_plugin_6_introdb", "ProvidersMarkerShare_plugin_6_introdb", "ProvidersMarkerAuto_plugin_6_introdb" })
                Check(Descendants(page).OfType<Control>().Any(control => AutomationProperties.GetAutomationId(control) == id), id + " renders");
            await Click(page, "ProvidersMarkerAdvanced_plugin_6_introdb");
            foreach (var id in new[] { "ProvidersMarkerPriority_plugin_6_introdb", "ProvidersMarkerFetchConfidence_plugin_6_introdb", "ProvidersMarkerShareConfidence_plugin_6_introdb" })
                Check(Descendants(page).OfType<Control>().Any(control => AutomationProperties.GetAutomationId(control) == id), id + " renders under Advanced");
            var scroller = (ScrollViewer)typeof(AdminProvidersPage).GetField("_scroller", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, "Providers has no horizontal overflow at compact desktop width");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); await Task.Delay(150);
            Check(page.Controller.State.Values.Count == 0, "authority retirement clears provider values and drafts");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent; await Task.Delay(100); field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Providers fixture failures: " + string.Join("; ", Failures));
    }

    private static async Task Click(DependencyObject root, string id)
    {
        var button = Descendants(root).OfType<Button>().Single(control => AutomationProperties.GetAutomationId(control) == id);
        await Until(() => button.IsEnabled && button.IsLoaded, id); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(80);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
