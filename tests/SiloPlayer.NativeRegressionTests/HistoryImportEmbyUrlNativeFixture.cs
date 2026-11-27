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
            await CheckPairedFieldsAsync(page, vm);
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

    /// <summary>The colour drawn inside a box, just in from its left border, where no text or placeholder sits.</summary>
    private static async Task<string> FillAsync(FrameworkElement box)
    {
        box.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(box);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        var x = Math.Min(bitmap.PixelWidth - 1, (int)Math.Round(5 * bitmap.PixelWidth / Math.Max(1, box.ActualWidth)));
        var y = bitmap.PixelHeight / 2;
        var at = (y * bitmap.PixelWidth + x) * 4;
        return $"#{pixels[at + 2]:X2}{pixels[at + 1]:X2}{pixels[at]:X2}";
    }

    /// <summary>
    /// Fields drawn side by side in a form share their top and height and look the same (the label above each too); fields stacked at a narrow width share
    /// their left and right edges. Checked for Emby Connect, Server URL and Saved Server, and for Jellyfin, at 1440 and 560 px.
    /// </summary>
    private static async Task CheckPairedFieldsAsync(SettingsPage page, SettingsViewModel vm)
    {
        var problems = new List<string>();
        var shownSource = vm.ImportSourceType; var shownMode = vm.ImportEmbyMode;
        foreach (var (source, mode, left, right) in new[]
        {
            ("emby", "connect", "EmbyConnectUsernameBox", "EmbyConnectPasswordBox"), ("emby", "direct", "EmbyDirectUsernameBox", "EmbyDirectPasswordBox"),
            ("emby", "saved", "EmbySavedUsernameBox", "EmbySavedPasswordBox"), ("jellyfin", "connect", "JellyfinUsernameBox", "JellyfinPasswordBox"),
        })
        {
            vm.ImportSourceType = source; vm.ImportEmbyMode = mode;
            Invoke(page, "UpdateImportPanelVisibility");
            foreach (var width in new double[] { 1440, 560 })
            {
                page.Width = width; page.UpdateLayout(); await Task.Delay(250); page.UpdateLayout();
                var a = (FrameworkElement)page.FindName(left); var b = (FrameworkElement)page.FindName(right);
                var tag = $"[{width:0}] {left}/{right}";
                if (a.ActualHeight <= 0 || b.ActualHeight <= 0) { problems.Add(tag + " are not shown"); continue; }
                (double X, double Y) At(FrameworkElement element) { var p = element.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0, 0)); return (p.X, p.Y); }
                var (ax, ay) = At(a); var (bx, by) = At(b);
                var labelA = ((Panel)a.Parent).Children[0] as FrameworkElement; var labelB = ((Panel)b.Parent).Children[0] as FrameworkElement;
                if (labelA != null && labelB != null && Math.Abs(labelA.ActualHeight - labelB.ActualHeight) > 0.5) problems.Add($"{tag} labels differ in height ({labelA.ActualHeight:0.#} vs {labelB.ActualHeight:0.#}; {(labelA as TextBlock)?.FontSize}/{(labelA as TextBlock)?.LineHeight}/{(labelA as TextBlock)?.FontFamily?.Source}/{(labelA as TextBlock)?.Text?.Length} {(labelA as TextBlock)?.Text} vs {(labelB as TextBlock)?.FontSize}/{(labelB as TextBlock)?.LineHeight}/{(labelB as TextBlock)?.FontFamily?.Source}/{(labelB as TextBlock)?.Text?.Length}; w {labelA.ActualWidth:0.#} vs {labelB.ActualWidth:0.#})");
                if (width == 1440) { var fillA = await FillAsync(a); var fillB = await FillAsync(b); if (fillA != fillB) problems.Add($"{tag} are filled differently ({fillA} vs {fillB})"); }
                var sideBySide = Math.Abs(ax - bx) > 10;
                if (sideBySide)
                {
                    if (Math.Abs(ay - by) > 0.5 || Math.Abs(a.ActualHeight - b.ActualHeight) > 0.5) problems.Add($"{tag} differ in top/height ({ay:0.#}+{a.ActualHeight:0.#} vs {by:0.#}+{b.ActualHeight:0.#}; labels {(labelA == null ? -1 : At(labelA).Y):0.#}+{labelA?.ActualHeight:0.#} vs {(labelB == null ? -1 : At(labelB).Y):0.#}+{labelB?.ActualHeight:0.#}; margins {a.Margin} vs {b.Margin}; parents {((FrameworkElement)a.Parent).ActualHeight:0.#} vs {((FrameworkElement)b.Parent).ActualHeight:0.#})");
                    if (labelA != null && labelB != null && Math.Abs(At(labelA).Y - At(labelB).Y) > 0.5) problems.Add($"{tag} labels differ in top ({At(labelA).Y:0.#} vs {At(labelB).Y:0.#})");
                }
                else if (Math.Abs(ax - bx) > 0.5 || Math.Abs(ax + a.ActualWidth - (bx + b.ActualWidth)) > 0.5) problems.Add($"{tag} stacked fields differ in width ({ax:0.#}..{ax + a.ActualWidth:0.#} vs {bx:0.#}..{bx + b.ActualWidth:0.#})");
                static string Look(Control box) => $"{((box.Background as SolidColorBrush)?.Color.ToString() ?? "-")}/{((box.BorderBrush as SolidColorBrush)?.Color.ToString() ?? "-")}/{box.BorderThickness.Left}/{box.CornerRadius.TopLeft}/{box.FontSize}/{box.Padding.Left}";
                if (a is Control controlA && b is Control controlB && Look(controlA) != Look(controlB)) problems.Add($"{tag} are drawn differently ({Look(controlA)} vs {Look(controlB)})");
            }
        }
        page.Width = 1440; page.UpdateLayout();
        vm.ImportSourceType = shownSource; vm.ImportEmbyMode = shownMode;
        Invoke(page, "UpdateImportPanelVisibility");
        Check(problems.Count == 0, "paired fields share their top, height and look" + (problems.Count == 0 ? "" : ": " + string.Join("; ", problems)));
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
            if (path == "/api/v2/history-imports/sources")
                return Json("""{"items":[{"id":1,"name":"Home Emby","source_type":"emby","enabled":true}],"page":{"has_more":false}}""");
            if (path == "/api/v2/history-imports/runs" && request.Method == HttpMethod.Get)
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
