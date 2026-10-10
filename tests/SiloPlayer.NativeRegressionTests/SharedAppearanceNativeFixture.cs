using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
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

internal static class SharedAppearanceNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var handler = new AppearanceHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://appearance-fixture.invalid");
        client.SetProfile("fixture-profile");
        var api = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "appearance-settings"));
        var local = settings.Load();
        local.LastTheme = "cinema-light";
        local.ThemeOverrides = new() { ["primary"] = "#FF0000" };
        settings.Save(local);
        var theme = new ThemeService(settings, api);
        var accessibility = new AccessibilityService(settings, theme);
        theme.ApplySavedTheme();
        ColorIs("AppBackgroundBrush", "#FF141417");
        ColorIs("AccentBrush", "#FFE8E8EC");
        var originalBrush = Application.Current.Resources["AccentBrush"];
        handler.Vars = new() { ["primary"] = "#4f46e5", ["ring"] = "#123", ["sidebar-primary"] = "#abcdef", ["foreground"] = "var(--primary)", ["background"] = "#182030" };
        await theme.SyncFromServerAsync();
        ColorIs("AccentBrush", "#FF4F46E5");
        ColorIs("HyperlinkButtonForeground", "#FF4F46E5");
        ColorIs("RingBrush", "#FF112233");
        ColorIs("FocusVisualPrimaryBrush", "#FF112233");
        ColorIs("SidebarPrimaryBrush", "#FFABCDEF");
        ColorIs("PrimaryTextBrush", "#FFE8E8EC");
        if (!ReferenceEquals(originalBrush, Application.Current.Resources["AccentBrush"]))
            throw new InvalidOperationException("Shared appearance replaced a live brush instead of updating it.");

        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client),
            new HistoryImportApi(client), new WatchProvidersApi(client), new AuthService(client, new AuthApi(client)),
            theme, settings, accessibility);
        await vm.SetDateFormatAsync("YYYY-MM-DD");
        await vm.SetTimeFormatAsync("24h");
        await vm.SetAccessibilityAsync(textScale: "x-large", textWeight: "strong", highContrast: true);
        await theme.SyncFromServerAsync();
        ColorIs("PrimaryTextBrush", "#FFFFFFFF");
        ColorIs("BorderBrush", "#FF8091A7");
        ColorIs("AccentBrush", "#FF4F46E5");
        if (settings.Load().UiDateFormat != "YYYY-MM-DD" || settings.Load().UiTimeFormat != "24h")
            throw new InvalidOperationException("Shared theme refresh changed the date/time preferences.");

        // The controls and view model must use the same isolated services as the
        // brush assertions. Program's account fixture has a different HTTP client
        // and preference store, so borrowing its SettingsViewModel hides errors.
        using var services = new ServiceCollection()
            .AddSingleton(client)
            .AddSingleton(api)
            .AddSingleton(settings)
            .AddSingleton(theme)
            .AddSingleton(accessibility)
            .AddSingleton(vm)
            .AddSingleton(new CatalogApi(client))
            .AddSingleton(new AuthApi(client))
            .AddSingleton(new RequestsApi(client))
            .AddSingleton(new AuthService(client, new AuthApi(client)))
            .AddSingleton(new CardOverlayService(api))
            .AddSingleton(new UICustomizationService(api))
            .AddSingleton(new ToastService())
            .BuildServiceProvider();
        using var fixtureServices = new FixtureServices(services);
        var page = new SettingsPage { Width = 900, Height = 740 };
        if (!ReferenceEquals(page.ViewModel, vm))
            throw new InvalidOperationException("SettingsPage did not resolve the isolated appearance view model.");
        parent.Children.Add(page);
        using var mountedPage = new MountedPage(parent, page);
        await (Task)page.GetType().GetMethod("LoadRequestPreferenceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!;
        var requestToggle = (ToggleSwitch)page.FindName("WatchlistAutoRequestToggle");
        if (requestToggle.Visibility != Visibility.Visible || requestToggle.IsOn)
            throw new InvalidOperationException("Opted-out native request preference cannot be re-enabled.");
        requestToggle.IsOn = true;
        var requestDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!requestToggle.IsEnabled || handler.RequestOptOut)
        { if (DateTime.UtcNow > requestDeadline) throw new TimeoutException("Native request preference save did not finish."); await Task.Delay(30); }
        if (!handler.Requests.Any(r => r.StartsWith("DELETE ") && r.Contains("requests.watchlist_auto_request")))
            throw new InvalidOperationException("Native request preference did not clear its profile override.");
        Program.Log("PASS: native request settings re-enable an opted-out profile and clear its override.");
        await Task.Delay(100);
        Invoke(page, "BuildDateTimeFormatControls");
        Invoke(page, "BuildAccessibilityControls");
        Invoke(page, "ShowSettingsDetail", (Button)page.FindName("AccessibilityTab"));
        if (page.FindName("ThemeEditorTab") != null || page.FindName("AppearanceTab") != null)
            throw new InvalidOperationException("Retired theme navigation remains reachable.");
        foreach (var name in new[] { "DateFormatButtons", "TimeFormatButtons", "TextSizeButtons", "TextWeightButtons", "ContrastButtons" })
            if (((StackPanel)page.FindName(name)).Children.Count == 0)
                throw new InvalidOperationException($"Accessibility control {name} is empty.");
        await vm.SetAccessibilityAsync(textScale: "default", textWeight: "default", highContrast: false);
        Invoke(page, "BuildAccessibilityControls");
        ColorIs("AccentBrush", "#FF4F46E5");
        await CaptureAsync(page, "appearance-wide.png");
        page.Width = 460;
        page.UpdateLayout();
        await Task.Delay(80);
        await vm.SetAccessibilityAsync(textScale: "x-large", textWeight: "strong", highContrast: true);
        Invoke(page, "BuildAccessibilityControls");
        ColorIs("PrimaryTextBrush", "#FFFFFFFF");
        ColorIs("AccentBrush", "#FF4F46E5");
        page.UpdateLayout();
        foreach (var name in new[] { "DateFormatButtons", "TimeFormatButtons", "TextSizeButtons" })
            if (((StackPanel)page.FindName(name)).Orientation != Orientation.Vertical)
                throw new InvalidOperationException($"Narrow accessibility choices {name} overflow horizontally.");
        var text = new TextBlock { Text = "Readability fixture", FontSize = 20, LineHeight = 30 };
        accessibility.Apply("x-large", "strong", true, text);
        if (Math.Abs(text.FontSize - 25) > .01 || Math.Abs(text.LineHeight - 37.5) > .01 || text.FontWeight.Weight != 520)
            throw new InvalidOperationException("Shared appearance broke text scaling.");
        accessibility.Apply("large", "default", false, text);
        if (Math.Abs(text.FontSize - 22.5) > .01 || Math.Abs(text.LineHeight - 33.75) > .01 || text.FontWeight.Weight != 400)
            throw new InvalidOperationException("Shared appearance large scale compounded or failed to restore normal weight.");
        accessibility.Apply("default", "default", false, text);
        if (text.FontSize != 20 || text.LineHeight != 30)
            throw new InvalidOperationException("Shared appearance cannot restore original font and explicit line height.");
        var typography = new StackPanel();
        foreach (var weight in new ushort[] { 400, 500, 600, 700 })
            typography.Children.Add(new TextBlock { Text = "Weight role", FontWeight = new Windows.UI.Text.FontWeight { Weight = weight } });
        parent.Children.Add(typography); typography.UpdateLayout();
        try
        {
            accessibility.Apply("default", "strong", false, typography);
            if (!typography.Children.Cast<TextBlock>().Select(t => t.FontWeight.Weight).SequenceEqual(new ushort[] { 520, 600, 700, 800 }))
                throw new InvalidOperationException("Strong typography no longer follows body/medium/semibold/bold source roles.");
            var inherited = new TextBlock { Text = "Inherited font" };
            var button = new Button { Content = inherited, FontSize = 20 };
            typography.Children.Add(button); await Task.Delay(30); typography.UpdateLayout();
            var originalChildSize = inherited.FontSize;
            accessibility.Apply("x-large", "default", false, button);
            accessibility.Apply("x-large", "default", false, button);
            if (button.FontSize != 25 || Math.Abs(inherited.FontSize - originalChildSize * 1.25) > .01)
                throw new InvalidOperationException("Text descendants inherit or compound their parent accessibility scale twice.");
        }
        finally { parent.Children.Remove(typography); }
        accessibility.Apply("x-large", "strong", true, text);
        await CaptureAsync(page, "appearance-narrow-highcontrast.png");
        parent.Children.Remove(page);

        handler.Vars.Clear();
        await theme.SyncFromServerAsync();
        ColorIs("AccentBrush", "#FFE8E8EC");
        handler.Vars["primary"] = "#123456";
        await theme.SyncFromServerAsync();
        handler.Fail = true;
        await theme.SyncFromServerAsync();
        ColorIs("AccentBrush", "#FFE8E8EC");
        if (handler.Requests.Any(r => r.Contains("ui.theme") || r.Contains("ui.custom") || r.Contains("ui_theme") || r.Contains("ui_custom")))
            throw new InvalidOperationException("Appearance issued a retired profile theme request.");
        if (!handler.Requests.Any(r => r.Contains("ui.date_format")) || !handler.Requests.Any(r => r.Contains("ui.time_format")))
            throw new InvalidOperationException("Date/time controls did not persist their preferences.");
        accessibility.Apply("default", "default", false);
        theme.ResetSharedAppearance();
        Program.Log("Shared appearance: native live brushes, derived controls, fallback, accessibility, date/time writes, wide/narrow layout passed.");
    }

    private sealed class FixtureServices : IDisposable
    {
        private readonly FieldInfo _field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object? _previous;

        public FixtureServices(IServiceProvider services)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, services);
        }

        public void Dispose() => _field.SetValue(null, _previous);
    }

    private sealed class MountedPage(StackPanel parent, SettingsPage page) : IDisposable
    {
        public void Dispose()
        {
            if (parent.Children.Contains(page)) parent.Children.Remove(page);
        }
    }

    private static void ColorIs(string name, string expected)
    {
        var color = ((SolidColorBrush)Application.Current.Resources[name]).Color;
        var actual = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    private static void Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(80);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
            throw new InvalidOperationException("Appearance fixture was not laid out for capture.");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private sealed class AppearanceHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Vars = new();
        public List<string> Requests = [];
        public bool Fail;
        public bool RequestOptOut = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            var path = request.RequestUri.AbsolutePath;
            object? reply = null;
            if (path == "/api/v2/settings/contract/capabilities") reply = new { api_version = 1, manifest_revision = 15 };
            else if (path == "/api/v2/requests/status") reply = new { requests_enabled = true, allowed = true, watchlist_titles_supported = true, watchlist_requests = !RequestOptOut };
            else if (path.Contains("requests.watchlist_auto_request") && request.Method == HttpMethod.Delete) { RequestOptOut = false; reply = new { }; }
            else if (path == "/api/v2/settings/values/effective" && request.RequestUri.Query.Contains("requests.watchlist_auto_request"))
                reply = new { items = new[] { new { key = "requests.watchlist_auto_request", value = !RequestOptOut } } };
            if (reply != null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply)) });
            if (request.RequestUri.AbsolutePath == "/api/v2/theme/admin-css")
                return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(new { vars = JsonSerializer.Serialize(Vars), raw_css = "body { color: red; }" })) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
