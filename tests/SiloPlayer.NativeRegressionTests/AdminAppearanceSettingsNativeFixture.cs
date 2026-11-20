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
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

internal static class AdminAppearanceSettingsNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            ["branding.accent_color"] = "", ["overlays.enabled"] = "true", ["defaults.card_overlays"] = "", ["defaults.ui.title_art"] = "true",
        };
        internal int Saves;
        internal int Uploads;
        internal int Deletes;
        internal int Version = 1;
        internal bool Conflict;
        internal bool CustomWordmark;
        internal string[] LastSavedKeys = [];
        internal string? LastIfMatch;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (request.Method == HttpMethod.Put && path == "/api/v2/admin/settings")
            {
                Saves++;
                LastIfMatch = request.Headers.IfMatch.ToString();
                if (Conflict) return Json(new { status = 412 }, HttpStatusCode.PreconditionFailed);
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
                LastSavedKeys = document.RootElement.GetProperty("values").EnumerateObject().Select(property => property.Name).ToArray();
                foreach (var property in document.RootElement.GetProperty("values").EnumerateObject()) Values[property.Name] = property.Value.GetString()!;
                Version++;
                body = new { values = Values, restart_required = false };
            }
            else if (request.Method == HttpMethod.Post && path == "/api/v2/admin/branding/assets/wordmark")
            {
                Uploads++;
                CustomWordmark = true;
                body = new { kind = "wordmark", @ref = "fixture-wordmark.webp", url = "/api/v2/branding/assets/wordmark?v=fixture-wordmark.webp" };
            }
            else if (request.Method == HttpMethod.Delete && path == "/api/v2/admin/branding/assets/wordmark")
            {
                Deletes++;
                CustomWordmark = false;
                return new(HttpStatusCode.NoContent);
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { available = true, display = "synthetic", build_number = 1 },
                "/api/v2/theme/branding" => new { server_name = "Synthetic", storage_available = true, wordmark_url = CustomWordmark ? "ms-appx:///Assets/silo-wordmark-sidebar.png" : null },
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 26, supports_overlay_images = true, supports_batched_effective = true, client_families = new[] { "desktop" } },
                "/api/v2/settings/overlay-config" => new { enabled = true, images = Array.Empty<object>() },
                _ => new { items = Array.Empty<object>(), revision = 1 },
            };
            return Json(body);
        }

        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"appearance-{Version}\"");
            return response;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        using var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Synthetic admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Program.ResultDirectory);
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(settings)
            .AddSingleton(settingsApi).AddSingleton(new CardOverlayService(settingsApi)).AddSingleton(new ThemeService(settings, settingsApi));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!
            .GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var pickerField = typeof(AdminAppearanceSettingsPage).GetField("ImagePickerOverride", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesField.GetValue(null);
        var previousPicker = pickerField.GetValue(null);
        var previousContent = window.Content;
        var outer = new Frame();
        AdminShellPage? shell = null;
        AdminAppearanceSettingsPage? page = null;
        try
        {
            Func<string, CancellationToken, Task<(byte[] Bytes, string MediaType, string Name)?>> picker = (_, _) =>
                Task.FromResult<(byte[], string, string)?>(
                    (Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aSWQAAAAASUVORK5CYII="), "image/png", "synthetic.png"));
            pickerField.SetValue(null, picker);
            servicesField.SetValue(null, services);
            window.Content = outer;
            navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/settings/appearance")?.Kind == AdminRouteResolutionKind.Route, "Appearance opens through production registration");
            await Until(() => outer.Content is AdminShellPage, "Admin shell");
            shell = (AdminShellPage)outer.Content!;
            var frame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => frame.Content is AdminAppearanceSettingsPage { IsLoaded: true, Controller.State.Loaded: true }, "loaded Appearance");
            page = (AdminAppearanceSettingsPage)frame.Content;
            var controller = page.Controller!;
            await Until(() => Descendants(page).OfType<ToggleSwitch>().Any(element => AutomationProperties.GetAutomationId(element) == "AppearanceTitleArt"), "Appearance controls");
            Check(controller.State.DirtyKeys.Count == 0 && server.Saves == 0, "loading displayed defaults does not save them");
            Check(new[] { "wordmark", "mark", "favicon", "login_bg" }.All(kind => Button(page, "AppearanceUpload" + kind).IsEnabled), "all four v2 image slots are available");
            await Click(page, "AppearanceUploadwordmark");
            await Until(() => server.Uploads == 1 && controller.State.AssetBusy == null, "wordmark upload acknowledged");
            await Until(() => Button(page, "AppearanceRemovewordmark").Visibility == Visibility.Visible, "uploaded wordmark remove control");
            await Click(page, "AppearanceRemovewordmark");
            await Until(() => server.Deletes == 1 && controller.State.AssetBusy == null, "wordmark removal acknowledged");
            Check(server.Uploads == 1 && server.Deletes == 1, "image assignments send exactly once");
            var titleArt = Descendants(page).OfType<ToggleSwitch>().Single(element => AutomationProperties.GetAutomationId(element) == "AppearanceTitleArt");
            titleArt.IsOn = false;
            await Until(() => controller.State.DirtyKeys.Contains("defaults.ui.title_art"), "title-art draft");
            await Click(page, "AppearanceSave");
            await Until(() => server.Saves == 1 && !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "settings save");
            Check(server.LastSavedKeys.SequenceEqual(new[] { "defaults.ui.title_art" }), "Save sends only the changed title-art default");
            Check(server.LastIfMatch == "\"appearance-1\"", "Save pins the displayed strong ETag");
            server.Conflict = true;
            titleArt.IsOn = true;
            await Click(page, "AppearanceSave");
            await Until(() => controller.State.Conflict && !controller.State.Saving, "conflict result");
            Check(controller.State.DirtyKeys.Contains("defaults.ui.title_art"), "conflict keeps the draft");
            Check(!Button(page, "AppearanceSave").IsEnabled, "conflict blocks another save");
            server.Conflict = false;
            await Click(page, "AppearanceReload");
            await Until(() => !controller.State.Loading && !controller.State.Conflict, "explicit reload");
            Check(controller.State.DirtyKeys.Contains("defaults.ui.title_art"), "reload preserves the unacknowledged draft");
            controller.Discard();
            foreach (var width in new[] { 1440, 800, 480 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000));
                await Task.Delay(120);
                await MediaParityNativeFixture.CaptureAsync(outer, $"appearance-loaded-{width}.png");
                Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.admin.", StringComparison.Ordinal)), $"{width}: official text keys resolve");
            }
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "Synthetic admin", Role = "user" });
            await Until(() => controller.State.Owner == null, "authority retirement");
            Check(controller.State.Values.Count == 0 && controller.State.DirtyKeys.Count == 0, "authority retirement clears the draft and values");
        }
        finally
        {
            var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (page is { IsLoaded: true }) page.Unloaded += (_, _) => pageUnloaded.TrySetResult(); else pageUnloaded.TrySetResult();
            if (shell is { IsLoaded: true }) shell.Unloaded += (_, _) => shellUnloaded.TrySetResult(); else shellUnloaded.TrySetResult();
            outer.BackStack.Clear();
            outer.ForwardStack.Clear();
            outer.Content = null;
            try
            {
                await Task.WhenAll(pageUnloaded.Task, shellUnloaded.Task).WaitAsync(TimeSpan.FromSeconds(6));
                Check(page?.Controller?.State.Owner == null, "page and shell unload barriers retire the controller");
            }
            finally
            {
                pickerField.SetValue(null, previousPicker);
                window.Content = previousContent;
                servicesField.SetValue(null, previousServices);
            }
        }
    }

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) throw new InvalidOperationException(label);
    }

    private static async Task Until(Func<bool> condition, string label)
    {
        for (var attempt = 0; attempt < 240 && !condition(); attempt++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }

    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(element => AutomationProperties.GetAutomationId(element) == id);

    private static async Task Click(DependencyObject page, string id)
    {
        await Until(() => Button(page, id).IsLoaded && Button(page, id).IsEnabled, id);
        ((IInvokeProvider)new ButtonAutomationPeer(Button(page, id)).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
