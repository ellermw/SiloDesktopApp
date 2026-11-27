using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Official #5382 on the real Settings page: the third Emby mode, Server URL. Shown only when the server's import capability says
/// emby_custom_server, falls back to Connect when that goes away, sends emby_base_url with the trimmed address and name and the password
/// as typed, scopes its start error to the form, and clears the password after a started run. Dispatch: SILO_NATIVE_TEST_ONLY=history-import-emby-url.
/// </summary>
internal static class HistoryImportEmbyUrlNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://emby-url-fixture.invalid");
        var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-token", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("primary-fixture", profile: new() { Id = "primary-fixture", Name = "Primary", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "emby-url-settings"));
        var api = new SettingsApi(client);
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client),
            auth, theme, settings, new AccessibilityService(settings, theme));
        using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(api).AddSingleton(settings).AddSingleton(theme).AddSingleton(vm)
            .AddSingleton(new CatalogApi(client)).AddSingleton(new AuthApi(client)).AddSingleton(new RequestsApi(client)).AddSingleton(auth)
            .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api)).AddSingleton(new ToastService())
            .AddSingleton(new AccessibilityService(settings, theme))
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        field.SetValue(null, services);
        var page = new SettingsPage { Width = 1440, Height = 1500 };
        parent.Children.Add(page);
        try
        {
            await Task.Delay(100);
            Invoke(page, "BuildSettingsOverview");
            Invoke(page, "ShowSettingsDetail", (Button)page.FindName("ImportTab"));
            vm.ImportProfileId = "primary-fixture";

            // An older server (no emby_custom_server member): Connect and Saved only.
            handler.Capability = """{"state":"available","allowed":true,"revision":2,"plex_connection_fallback":true,"max_plex_connections":8}""";
            await Reload(vm);
            Invoke(page, "UpdateImportPanelVisibility");
            var direct = (Button)page.FindName("ImportEmbyDirectModeBtn");
            Check(direct.Visibility == Visibility.Collapsed && !vm.SupportsEmbyDirect, "a server without emby_custom_server does not offer Server URL");

            // A server that supports it: the third mode appears between Connect and Saved.
            handler.Capability = """{"state":"available","allowed":true,"revision":3,"plex_connection_fallback":true,"max_plex_connections":8,"emby_custom_server":true}""";
            await Reload(vm);
            Invoke(page, "UpdateImportPanelVisibility");
            Check(direct.Visibility == Visibility.Visible && vm.SupportsEmbyDirect, "a server that supports it offers Server URL");
            var selector = (StackPanel)page.FindName("ImportEmbyModeSelector");
            Check(selector.Children.OfType<Button>().Select(button => button.Name).SequenceEqual(["ImportEmbyConnectModeBtn", "ImportEmbyDirectModeBtn", "ImportEmbySavedModeBtn"]),
                "the modes are Connect, Server URL, Saved in that order");
            Check(vm.ImportEmbyMode == "connect", "Connect stays the default mode");
            Check(((Button)page.FindName("ImportEmbyConnectModeBtn")).Content?.ToString() == "Emby Connect" && direct.Content?.ToString() == "Server URL"
                && ((Button)page.FindName("ImportEmbySavedModeBtn")).Content?.ToString() == "Saved Server", "the mode labels read Emby Connect, Server URL and Saved Server");

            Invoke(page, "ImportEmbyMode_Click", direct, new RoutedEventArgs());
            var panel = (FrameworkElement)page.FindName("ImportEmbyDirectPanel");
            var url = (TextBox)page.FindName("EmbyServerUrlBox");
            var user = (TextBox)page.FindName("EmbyDirectUsernameBox");
            var password = (PasswordBox)page.FindName("EmbyDirectPasswordBox");
            var start = (Button)page.FindName("StartImportButton");
            Check(panel.Visibility == Visibility.Visible && ((FrameworkElement)page.FindName("ImportEmbyConnectPanel")).Visibility == Visibility.Collapsed, "Server URL shows its own form");
            Check(url.PlaceholderText == "https://emby.example.com", "the address field shows the example address");
            Check(password.PlaceholderText == "Leave blank if the account has none", "the password field says it may be left blank");
            var texts = Descendants(panel).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Check(texts.Contains("Emby Server URL") && texts.Contains("Emby Username") && texts.Contains("Emby Password")
                && texts.Contains("Enter the base URL for the Emby server you want to import from."), "the form uses the official labels and help text");
            Check(!start.IsEnabled, "Start waits for an address and a username");
            url.Text = "  https://emby.example.com/emby  "; await Task.Delay(50);
            Check(!start.IsEnabled, "an address alone does not start an import");
            user.Text = "  alice  "; await Task.Delay(50);
            Invoke(page, "UpdateImportPanelVisibility");
            Check(start.IsEnabled, "an address and a username start an import with no password");
            await Capture(page, "emby-url-form-1440.png");
            page.Width = 560; page.UpdateLayout(); await Task.Delay(100);
            await Capture(page, "emby-url-form-560.png");
            page.Width = 1440; page.UpdateLayout();

            // The server refuses: the error shows at this form, the address is flagged when it is not a usable one, the password stays for correction.
            url.Text = "emby.example.com"; password.Password = "pw fixture "; await Task.Delay(50);
            handler.RunStatus = HttpStatusCode.UnprocessableEntity; handler.RunDetail = "Emby server address must be http or https.";
            Invoke(page, "StartImport_Click", start, new RoutedEventArgs());
            await Until(() => !vm.IsImporting);
            Invoke(page, "UpdateImportPanelVisibility");
            var errorPanel = (FrameworkElement)page.FindName("ImportStartErrorPanel");
            Check(errorPanel.Visibility == Visibility.Visible && ((TextBlock)page.FindName("ImportStartErrorText")).Text.Contains("http or https", StringComparison.Ordinal), "a refused start shows the server's reason at the form");
            Check(vm.EmbyServerUrlInvalid, "an address that is not http or https is flagged");
            Check(password.Password == "pw fixture " && vm.EmbyDirectPassword == "pw fixture ", "a refused start keeps the password for correction");
            await Capture(page, "emby-url-error-1440.png");
            page.Width = 560; page.UpdateLayout(); await Task.Delay(100);
            await Capture(page, "emby-url-error-560.png");
            page.Width = 1440; page.UpdateLayout();
            Invoke(page, "ImportEmbyMode_Click", (Button)page.FindName("ImportEmbyConnectModeBtn"), new RoutedEventArgs());
            Check(((FrameworkElement)page.FindName("ImportStartErrorPanel")).Visibility == Visibility.Collapsed, "switching mode clears the settled error");
            Invoke(page, "ImportEmbyMode_Click", direct, new RoutedEventArgs());
            Check(((FrameworkElement)page.FindName("ImportStartErrorPanel")).Visibility == Visibility.Collapsed, "coming back does not bring the old error back");

            // A started run: emby_base_url, the trimmed address and name, the password exactly as typed, no Connect session and no saved source.
            url.Text = "  https://emby.example.com/emby  "; await Task.Delay(50);
            handler.RunStatus = HttpStatusCode.Created;
            Invoke(page, "StartImport_Click", start, new RoutedEventArgs());
            await Until(() => !vm.IsImporting);
            using (var body = JsonDocument.Parse(handler.RunBody!))
            {
                var root = body.RootElement;
                Check(root.GetProperty("emby_base_url").GetString() == "https://emby.example.com/emby" && root.GetProperty("username").GetString() == "alice"
                    && root.GetProperty("password").GetString() == "pw fixture " && root.GetProperty("source").GetString() == "emby",
                    "the run sends the trimmed address and name and the password as typed");
                Check(!root.TryGetProperty("connect_session_id", out _) && !root.TryGetProperty("source_id", out _) && !root.TryGetProperty("server_id", out _),
                    "the run sends no Connect session and no saved source");
            }
            Check(password.Password.Length == 0 && vm.EmbyDirectPassword.Length == 0, "the password is cleared after a started run");
            Check(vm.EmbyServerUrl.Length > 0 && vm.EmbyDirectUsername.Length > 0, "the address and name stay for the next run");

            // An empty password is sent as an empty password.
            handler.RunBody = null;
            Invoke(page, "StartImport_Click", start, new RoutedEventArgs());
            await Until(() => !vm.IsImporting);
            using (var body = JsonDocument.Parse(handler.RunBody!))
                Check(body.RootElement.GetProperty("password").GetString() == "", "an account with no password sends an empty one");

            // The server stops supporting it while Server URL is chosen: the page falls back to Connect.
            handler.Capability = """{"state":"available","allowed":true,"revision":4,"plex_connection_fallback":true,"max_plex_connections":8,"emby_custom_server":false}""";
            await Reload(vm);
            Invoke(page, "UpdateImportPanelVisibility");
            Check(vm.EffectiveImportEmbyMode == "connect" && panel.Visibility == Visibility.Collapsed && direct.Visibility == Visibility.Collapsed
                && ((FrameworkElement)page.FindName("ImportEmbyConnectPanel")).Visibility == Visibility.Visible, "when support goes away the page falls back to Connect");
            var find = (Button)page.FindName("EmbyConnectLoginButton");
            Check(!find.IsEnabled, "Find Servers waits for a username and a password");
            await Capture(page, "emby-url-fallback-1440.png");
            vm.EmbyConnectUsername = "alice"; ((PasswordBox)page.FindName("EmbyConnectPasswordBox")).Password = "pw"; await Task.Delay(150);
            Check(find.IsEnabled, "Find Servers is available once both are typed");
            Program.Log("History import Server URL: capability, modes, form, request, error scope, cleared password and fallback passed.");
        }
        finally
        {
            parent.Children.Remove(page);
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("History import Server URL failed: " + message);
        Program.Log("PASS: " + message);
    }

    private static async Task Reload(SettingsViewModel vm)
    {
        await Until(() => !vm.IsLoadingImportSources);
        await vm.LoadImportTabAsync();
        await Until(() => !vm.IsLoadingImportSources);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 150 && !done(); i++) await Task.Delay(20);
        if (!done()) throw new TimeoutException("History import Server URL fixture did not settle.");
    }

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement root)
    {
        yield return root;
        var children = root switch
        {
            Panel panel => panel.Children.OfType<FrameworkElement>(),
            Border { Child: FrameworkElement child } => new[] { child },
            _ => Enumerable.Empty<FrameworkElement>(),
        };
        foreach (var child in children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(120);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private sealed class ImportHandler : HttpMessageHandler
    {
        public string Capability = "{}";
        public HttpStatusCode RunStatus = HttpStatusCode.Created;
        public string RunDetail = "";
        public string? RunBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/history-imports/capability") return Json(Capability);
            if (path == "/api/v2/history-imports/sources" || path == "/api/v2/history-imports/runs" && request.Method == HttpMethod.Get)
                return Json("""{"items":[],"page":{"has_more":false}}""");
            if (path == "/api/v2/history-imports/runs" && request.Method == HttpMethod.Post)
            {
                RunBody = await request.Content!.ReadAsStringAsync(ct);
                return RunStatus == HttpStatusCode.Created
                    ? Json("""{"id":"run-fixture","source_type":"emby","status":"queued"}""", RunStatus)
                    : Json(JsonSerializer.Serialize(new { title = "Unprocessable", status = (int)RunStatus, detail = RunDetail }), RunStatus);
            }
            if (path.EndsWith("/profiles", StringComparison.Ordinal))
                return Json("""{"items":[{"id":"primary-fixture","name":"Primary","is_primary":true}],"profiles":[{"id":"primary-fixture","name":"Primary","is_primary":true}]}""");
            return Json("{}");
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
