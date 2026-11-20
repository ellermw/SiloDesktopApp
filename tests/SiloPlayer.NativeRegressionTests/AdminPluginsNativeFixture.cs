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

// Authored, NOT executed in the source-only implementation lane. All responses are synthetic.
internal static class AdminPluginsNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal bool Enabled = true, Community = true, Conflict;
        internal int Commands, ConfigSaves, Installs;
        internal string? IfMatch;
        internal JsonElement ConfigBody;
        internal TaskCompletionSource? HeldRead;
        private object Plugin(bool catalog = false, int index = 0) => new
        {
            id = (7 + index).ToString(), plugin_id = index == 0 ? "fixture.plugin" : "fixture." + index, repository_id = "2", version = "1.0.0", source_kind = "silo", repository_name = "Fixture catalog", enabled = Enabled,
            update_policy = "notify", available_version = "2.0.0", updates_paused = false,
            presentation = new { display_name = index == 0 ? "Fixture Plugin" : "Fixture Plugin " + index.ToString("D2"), summary = "Synthetic plugin description", setup_markdown = "## Setup\nChoose your preferences.", license_spdx = "MIT" },
            runtime = new { resident = true, state = "running", restart_count = 0 }, signature = new { state = "unsigned" },
            capabilities = new[] { new { type = "scheduled_task.v1", id = "refresh", display_name = "Refresh fixture", description = "Synthetic scheduled task", subscriptions = Array.Empty<string>() } },
            global_config_schema = new[] { new { key = "connection", title = "Connection", description = "Synthetic settings", required = true,
                json_schema = """{"type":"object","properties":{"host":{"type":"string"},"password":{"type":"string","writeOnly":true}},"required":["host"]}""" } },
            global_configs = new[] { new { key = "connection", value = new { host = "fixture-host" }, configured_secrets = new[] { "password" } } },
            auth_bindings = Array.Empty<object>(), task_bindings = Array.Empty<object>(),
        };
        private static object Page(object[] items) => new { items, page = new { has_more = false } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                if (HeldRead is { } held && path.EndsWith("/installations")) await held.Task;
                return path switch
                {
                    "/api/v2/admin/plugins/installations" => Json(Page(Enumerable.Range(0, 28).Select(i => Plugin(index: i)).ToArray())),
                    "/api/v2/admin/plugins/catalog" => Json(Page([Plugin(catalog: true)])),
                    "/api/v2/admin/plugins/repositories" => Json(Page([new { id = "2", display_name = "Synthetic official", url = "https://fixture.invalid/index.json", enabled = true, source_kind = "silo", managed = true }])),
                    "/api/v2/admin/plugins/catalog-settings" => Json(new { include_approved_community_plugins = Community }, etag: "\"fixture-catalog-v1\""),
                    "/api/v2/admin/plugins/catalog-status" => Json(new { approved_community_plugin_count = 3, installed_community_plugin_count = 1, migrated_plugin_count = 0, community_updates_paused = !Community }),
                    "/api/v2/admin/tasks/check_plugin_updates" => Json(new { key = "check_plugin_updates", state = "idle" }),
                    _ => Json(new { }),
                };
            }
            Commands++;
            if (path.EndsWith("/catalog-settings"))
            {
                IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                if (Conflict) return Json(new { status = 412 }, HttpStatusCode.PreconditionFailed);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Community = body.RootElement.GetProperty("include_approved_community_plugins").GetBoolean();
                return Json(new { include_approved_community_plugins = Community }, etag: "\"fixture-catalog-v2\"");
            }
            if (path.EndsWith("/config"))
            {
                ConfigSaves++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); ConfigBody = body.RootElement.Clone(); return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/installations")) { Installs++; return Json(Plugin(), HttpStatusCode.Created); }
            if (request.Method == HttpMethod.Put && path.EndsWith("/7"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); if (body.RootElement.TryGetProperty("enabled", out var value)) Enabled = value.GetBoolean();
            }
            return Json(Plugin());
        }
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK, string? etag = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
            if (etag != null) response.Headers.ETag = new(etag); return response;
        }
    }
    private static void Check(bool condition, string message)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + message); if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25);
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
        collection.AddAdminPlugins(); // Same production registration as App.
        await using var services = collection.BuildServiceProvider();
        var serviceField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldServices = serviceField.GetValue(null); var oldContent = window.Content; var oldName = DocumentTitle.AppName;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null; Frame? inner = null; AdminPluginsPage? page = null; ContentDialog? dialog = null;
        try
        {
            serviceField.SetValue(null, services); outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 960));
            Check(AdminShellPage.TryOpenHref("/admin/plugins") is { Kind: AdminRouteResolutionKind.Route }, "Plugins resolves through production registration");
            await Until(() => outer.Content is AdminShellPage, "Plugins entered real Admin shell");
            var shell = (AdminShellPage)outer.Content; inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminPluginsPage { Controller.State.Loaded: true }, "XAML-backed Plugins activates through Frame"); page = (AdminPluginsPage)inner.Content;
            await Until(() => Find<Button>(page, "PluginsNext") != null, "Installed paging rendered");
            Check(page.Controller!.State.Installations.Count == 28, "Installed inventory retains every bounded row");
            await Click(page, "PluginsNext"); Check(Find<Button>(page, "PluginsNext")?.IsEnabled == false, "Installed next page terminates");
            var search = Find<TextBox>(page, "PluginsSearch")!; search.Text = "missing fixture";
            await Until(() => Texts(page).Contains("No installed plugins match"), "No-match state is distinct from empty inventory"); search.Text = "";
            await Capture(outer, "plugins-installed-wide.png");
            shell.Navigate(new(AdminRoute.Plugins) { Segments = ["fixture.plugin"] });
            await Until(() => inner.Content is AdminPluginsPage p && p != page && p.Controller?.State.Loaded == true, "Plugin management detail navigates through shell"); page = (AdminPluginsPage)inner.Content;
            await Until(() => Find<PasswordBox>(page, "PluginConfigField_connection_password") != null, "Schema-derived write-only field rendered");
            var secret = Find<PasswordBox>(page, "PluginConfigField_connection_password")!; Check(secret.Password.Length == 0 && secret.PasswordRevealMode == PasswordRevealMode.Hidden, "Saved secret is never populated or revealed");
            var host = Find<TextBox>(page, "PluginConfigField_connection_host")!; host.Text = "new-fixture-host";
            await Click(page, "PluginConfigSave_connection"); await Until(() => server.ConfigSaves == 1 && page.Controller?.State.Busy == false, "Config command completed and reconciled");
            Check(server.ConfigBody.GetProperty("value").GetProperty("host").GetString() == "new-fixture-host" && !server.ConfigBody.GetProperty("value").TryGetProperty("password", out _), "Blank secret is omitted while ordinary edits reach the real transport");
            await Until(() => Find<ToggleSwitch>(page, "PluginEnabled")?.IsEnabled == true, "Detail controls restored after reconciliation");
            Find<ToggleSwitch>(page, "PluginEnabled")!.IsOn = false;
            await Until(() => !server.Enabled && page.Controller?.State.Busy == false, "Enable/disable uses installation assignment");
            await Capture(outer, "plugins-detail-wide.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(680, 900)); await Task.Delay(100); await Capture(outer, "plugins-detail-compact.png");
            await Click(page, "PluginUninstall");
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "Uninstall requires its concrete confirmation"); dialog = (ContentDialog)Field(page, "_dialog")!;
            var commands = server.Commands; dialog.Hide(); await Until(() => !dialog.IsLoaded, "Cancelled confirmation fully unloaded"); Check(server.Commands == commands, "Cancel sends no uninstall request"); dialog = null;
            shell.Navigate(new(AdminRoute.Plugins, new Dictionary<string, string> { ["tab"] = "catalog" }));
            await Until(() => inner.Content is AdminPluginsPage p && p != page && p.Controller?.State.Loaded == true, "Catalog navigated normally"); page = (AdminPluginsPage)inner.Content;
            await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity") != null, "Catalog control and repositories rendered");
            Check(Find<Button>(page, "PluginRepositoryDelete_2") == null && Texts(page).Contains("Managed by Silo"), "Managed repositories have no destructive controls");
            server.Conflict = true; Find<ToggleSwitch>(page, "PluginsCommunity")!.IsOn = false;
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "Hiding community catalog requires review"); dialog = (ContentDialog)Field(page, "_dialog")!;
            Invoke(Descendants(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Hide and pause updates"));
            await Until(() => page.Controller?.State.CatalogReviewRequired == true && page.Controller.State.Busy == false, "Stale catalog validator surfaces explicit review");
            await Until(() => !dialog.IsLoaded, "Catalog confirmation unloaded"); dialog = null;
            await Until(() => Find<ToggleSwitch>(page, "PluginsCommunity")?.IsEnabled == false, "Catalog conflict disables editing until explicit review");
            Check(server.IfMatch == "\"fixture-catalog-v1\"", "Exact reviewed strong ETag reached catalog PUT");
            await Capture(outer, "plugins-catalog-conflict.png");
            var retainedPage = page; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); server.HeldRead = gate;
            var read = page.Controller!.ReloadAsync(); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" }); gate.SetResult(); await read;
            await Until(() => retainedPage.Controller?.State.Installations.Count is null or 0 && !Descendants(retainedPage).OfType<PasswordBox>().Any(p => p.Password.Length > 0), "Authority retirement discards held results and clears secrets");
        }
        finally
        {
            server.HeldRead?.TrySetResult(); page?.SuspendAdminPage(); dialog?.Hide();
            if (inner?.Content is IAdminPageSuspension suspension) suspension.SuspendAdminPage();
            if (inner != null) { inner.Content = null; inner.BackStack.Clear(); inner.ForwardStack.Clear(); }
            if (outer != null) { outer.Content = null; outer.BackStack.Clear(); outer.ForwardStack.Clear(); }
            await Until(() => (dialog == null || !dialog.IsLoaded) && (page == null || !page.IsLoaded), "Pages and dialogs unload before fixture services restore");
            window.Content = oldContent; serviceField.SetValue(null, oldServices); DocumentTitle.SetServerName(oldName);
        }
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T? Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static string[] Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToArray();
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Click(DependencyObject root, string id) { await Until(() => Find<Button>(root, id) is { IsLoaded: true, IsEnabled: true }, id + " ready"); Invoke(Find<Button>(root, id)!); }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
