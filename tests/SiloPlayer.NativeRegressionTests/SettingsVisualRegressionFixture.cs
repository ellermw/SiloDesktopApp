using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views;
using SiloPlayer.ViewModels;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using System.Net;

internal static class SettingsVisualRegressionFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        parent.RequestedTheme = ElementTheme.Dark;
        if (Environment.GetEnvironmentVariable("SILO_NATIVE_VISUAL_CASE") == "settings-entry")
        {
            var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
            var previous = (IServiceProvider)servicesField.GetValue(null)!;
            using var wire = new SlowRequests();
            using var settingsHttp = new HttpClient(wire);
            var client = new SiloApiClient(settingsHttp); client.SetBaseUrl("https://fixture.invalid");
            using var fixtureServices = new Services(previous, new RequestsApi(client));
            servicesField.SetValue(null, fixtureServices);
            SettingsPage? page = null;
            Frame? frame = null;
            try
            {
                page = new SettingsPage { Width = 900, Height = 700 };
                parent.Children.Add(page); await Task.Delay(80);
                if (((FrameworkElement)page.FindName("SettingsLayoutGrid")).Visibility != Visibility.Collapsed ||
                    ((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Settings must present its overview before any asynchronous data load, rather than flash the raw detail list.");
                parent.Children.Remove(page);
                Program.Log("PASS: Settings starts with its intended overview before network initialization.");
                parent.Children.Add(page);
                await SiloCastSettingsFixture.RunAsync(page);
                parent.Children.Remove(page); await Task.Delay(60);
                foreach (var width in new[] { 600, 1080, 1920, 3440, 3840 })
                {
                    window.AppWindow.ResizeClient(new(width, 800));
                    wire.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var prior = wire.Reads;
                    frame = new Frame { Width = width, Height = 800 };
                    parent.Children.Add(frame);
                    if (!frame.Navigate(typeof(SettingsPage))) throw new InvalidOperationException("Settings route failed.");
                    await Until(() => wire.Reads > prior);
                    page = (SettingsPage)frame.Content;
                    if (width >= 1024 && page.ActiveSettingsSection != "Playback" || width < 1024 && ((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Visible)
                        throw new InvalidOperationException("Wide settings must default to Playback and narrow settings to the grouped index.");
                    VerifyGrouping(page);
                    page.SetShellSidebarHosted(true);
                    if (((FrameworkElement)page.FindName("SettingsNavigationScroller")).Visibility != Visibility.Collapsed || ((Grid)page.FindName("SettingsLayoutGrid")).ColumnDefinitions[0].Width.Value != 0)
                        throw new InvalidOperationException("Shell hosted Settings retained the nested navigation rail.");
                    if (width >= 1024 && (((FrameworkElement)page.FindName("SettingsHeaderGrid")).Visibility != Visibility.Collapsed || ((FrameworkElement)page.FindName("SettingsBackButton")).Visibility != Visibility.Collapsed || ((Grid)page.FindName("SettingsLayoutGrid")).BorderThickness != new Thickness(0) || ((Grid)page.FindName("SettingsLayoutGrid")).Margin.Top != 0))
                        throw new InvalidOperationException("Hosted desktop settings retained the duplicated header, back button, or outer card.");
                    page.SelectSettingsSection("TitlePages");
                    if (((FrameworkElement)page.FindName("TitlePagesPanel")).Visibility != Visibility.Visible || page.ActiveSettingsSection != "TitlePages")
                        throw new InvalidOperationException("Title Pages sidebar route did not open.");
                    page.SetShellSidebarHosted(false);
                    typeof(SettingsPage).GetMethod("ShowSettingsDetail", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(page, [page.FindName("SubtitlesTab")]);
                    wire.Pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"requests_enabled\":true}", System.Text.Encoding.UTF8, "application/json") });
                    await Task.Delay(200);
                    if (((FrameworkElement)page.FindName("SubtitlesPanel")).Visibility != Visibility.Visible ||
                        ((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Late settings load reset the user's selected tab.");
                    if (((ComboBox)page.FindName("SubtitleModeComboBox")).SelectedItem is not ComboBoxItem { Tag: "auto" } || !page.ViewModel.ShowForcedSubtitles)
                        throw new InvalidOperationException("Resolved subtitle defaults must show Auto and enabled forced subtitles when profile DTO omits legacy fields.");
                    page.SetShellSidebarHosted(width >= 1024);
                    await CaptureSettings(frame, $"settings-language-{width}.png");
                    page.SelectSettingsSection("TitlePages");
                    await Task.Delay(100);
                    await CaptureSettings(frame, $"settings-title-pages-{width}.png");
                    Program.Log($"PASS: settings grouping, hosted shell, default/slow-load navigation at {width}px; native PNGs captured.");
                    parent.Children.Remove(frame); frame.Content = null; frame = null; page = null;
                    await Task.Delay(60); // Deliver Unloaded while fixture services are still alive.
                }
                return;
            }
            finally
            {
                wire.Pending?.TrySetCanceled();
                if (frame is not null) { parent.Children.Remove(frame); frame.Content = null; }
                if (page is not null) parent.Children.Remove(page);
                await Task.Delay(100);
                servicesField.SetValue(null, previous);
            }
        }
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "toggle-settings"));
        // These app-missing states otherwise come from the system theme. A
        // light system accent can supply a white thumb under the app's light
        // primary track. Simulate that only for keys the app does not own.
        var appTheme = Application.Current.Resources.MergedDictionaries.Last();
        foreach (var key in new[] { "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed" })
            if (!appTheme.Keys.Any(entry => Equals(entry, key))) Application.Current.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.White);
        using var http = new HttpClient();
        var theme = new ThemeService(settings, new SettingsApi(new SiloApiClient(http)));
        theme.ApplySavedTheme();
        Program.Log("Default colors primary=" + ((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color + "; foreground=" + ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color);
        foreach (var key in new[] { "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed" })
            Program.Log(key + "=" + (Application.Current.Resources[key] is SolidColorBrush brush ? brush.Color.ToString() : "missing"));
        var toggle = new ToggleSwitch { IsOn = true };
        parent.Children.Add(toggle); await Task.Delay(100); toggle.UpdateLayout();
        foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Normal" })
        {
            if (!VisualStateManager.GoToState(toggle, state, false)) throw new InvalidOperationException("Toggle visual state missing: " + state);
            await Task.Delay(600);
            var knob = Find(toggle, "SwitchKnobOn") as Border ?? throw new InvalidOperationException("Enabled thumb missing.");
            var track = Find(toggle, "SwitchKnobBounds") as Microsoft.UI.Xaml.Shapes.Rectangle;
            Program.Log($"Toggle {state}: thumb={((SolidColorBrush)knob.Background).Color}, track={((SolidColorBrush)track!.Fill).Color}, opacity={knob.Opacity}");
            var raster = new RenderTargetBitmap(); await raster.RenderAsync(toggle);
            var pixels = (await raster.GetPixelsAsync()).ToArray();
            var file = await (await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory))).CreateFileAsync("toggle-" + state + ".png", CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
            }
            var location = knob.TransformToVisual(toggle).TransformPoint(new(0, 0));
            Program.Log($"Thumb actual={knob.ActualWidth}x{knob.ActualHeight} at {location.X},{location.Y}; raster={raster.PixelWidth}x{raster.PixelHeight}");
            var center = ((int)(location.Y + knob.ActualHeight / 2) * raster.PixelWidth + (int)(location.X + knob.ActualWidth / 2)) * 4;
            if (center < 0 || center + 3 >= pixels.Length || pixels[center] > 64 || pixels[center + 1] > 64 || pixels[center + 2] > 64 || pixels[center + 3] < 240)
                throw new InvalidOperationException($"Enabled thumb has no contrasting pixels in {state} despite its brush and opacity.");
            if (knob.Background is not SolidColorBrush fill || track?.Fill is not SolidColorBrush rail ||
                fill.Color == rail.Color || knob.Opacity < .9)
                throw new InvalidOperationException($"Enabled toggle thumb is invisible in {state}.");
        }
        parent.Children.Remove(toggle);
        Program.Log("PASS: enabled toggle thumb remains visible in normal, hover and pressed states.");
    }
    private static async Task CaptureSettings(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var raster = new RenderTargetBitmap(); await raster.RenderAsync(element);
        if (raster.PixelWidth == 0 || raster.PixelHeight == 0) throw new InvalidOperationException("Settings capture has no pixels.");
        var pixels = (await raster.GetPixelsAsync()).ToArray();
        var file = await (await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory)).CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }

    private static void VerifyGrouping(SettingsPage page)
    {
        static bool Within(FrameworkElement child, FrameworkElement target)
        {
            if (ReferenceEquals(child, target)) return true;
            return target switch
            {
                Panel panel => panel.Children.OfType<FrameworkElement>().Any(element => Within(child, element)),
                Border border when border.Child is FrameworkElement element => Within(child, element),
                ContentControl content when content.Content is FrameworkElement element => Within(child, element),
                _ => false,
            };
        }
        foreach (var (control, panel) in new[] { ("TitleArtSettingsGroup", "TitlePagesPanel"), ("SpokenLanguageComboBox", "SubtitlesPanel"), ("MetadataLanguageComboBox", "SubtitlesPanel"), ("NextUpModeComboBox", "HomeScreenPanel"), ("CardOverlaysPanel", "InterfacePanel") })
            if (!Within((FrameworkElement)page.FindName(control), (FrameworkElement)page.FindName(panel)))
                throw new InvalidOperationException(control + " is not grouped under " + panel);
        if (!page.SidebarEntries.Any(entry => entry.Section == "TitlePages") || page.SidebarEntries.Any(entry => entry.Section == "CardOverlays"))
            throw new InvalidOperationException("Settings shell entries retained the old appearance routes.");
    }

    private sealed class Services : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider _fallback;
        private readonly HttpClient _http;
        private readonly Dictionary<Type, object> _services;
        public Services(IServiceProvider fallback, RequestsApi requests)
        {
            _fallback = fallback;
            _http = new HttpClient(new SettingsDataHandler());
            var apiClient = new SiloApiClient(_http); apiClient.SetBaseUrl("https://settings-visual-fixture.invalid");
            var auth = (AuthService)fallback.GetService(typeof(AuthService))!; apiClient.SetProfile(auth.SelectedProfileId);
            var local = (SettingsService)fallback.GetService(typeof(SettingsService))!;
            var api = new SettingsApi(apiClient); var catalog = new CatalogApi(apiClient); var authApi = new AuthApi(apiClient);
            var theme = new ThemeService(local, api); var accessibility = new AccessibilityService(local, theme);
            var history = new HistoryImportApi(apiClient); var providers = new WatchProvidersApi(apiClient);
            _services = new()
            {
                [typeof(SiloApiClient)] = apiClient, [typeof(SettingsApi)] = api, [typeof(CatalogApi)] = catalog,
                [typeof(AuthApi)] = authApi, [typeof(RequestsApi)] = requests, [typeof(ThemeService)] = theme,
                [typeof(AccessibilityService)] = accessibility,
                [typeof(SettingsViewModel)] = new SettingsViewModel(api, catalog, authApi, history, providers, auth, theme, local, accessibility),
                [typeof(CardOverlayService)] = new CardOverlayService(api), [typeof(UICustomizationService)] = new UICustomizationService(api),
            };
        }
        public object? GetService(Type type) => _services.GetValueOrDefault(type) ?? _fallback.GetService(type);
        public void Dispose() => _http.Dispose();
    }
    private sealed class SettingsDataHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _fallback = new((HttpMessageHandler)Activator.CreateInstance(typeof(AccountCoverageNativeFixture).GetNestedType("Wire", BindingFlags.NonPublic)!, nonPublic: true)!);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/settings/contract/capabilities")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { api_version = 1, manifest_revision = 17, supports_batched_effective = true, supports_idempotent_writes = true, supports_device_rename = true, client_families = new[] { "desktop", "web" } })) });
            return _fallback.SendAsync(request, ct);
        }
        protected override void Dispose(bool disposing) { if (disposing) _fallback.Dispose(); base.Dispose(disposing); }
    }
    private sealed class SlowRequests : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage>? Pending;
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Reads); return Pending!.Task.WaitAsync(ct); }
    }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(20); } throw new TimeoutException("Settings loading fixture timed out."); }
    private static DependencyObject? Find(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && element.Name == name) return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (Find(VisualTreeHelper.GetChild(root, index), name) is { } found) return found;
        return null;
    }
}
