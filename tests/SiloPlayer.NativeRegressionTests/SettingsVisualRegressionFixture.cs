using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views;
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
            servicesField.SetValue(null, new Services(previous, new RequestsApi(client)));
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
                foreach (var width in new[] { 600, 1920, 3440, 3840 })
                {
                    window.AppWindow.ResizeClient(new(width, 800));
                    wire.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var prior = wire.Reads;
                    frame = new Frame { Width = width, Height = 800 };
                    parent.Children.Add(frame);
                    if (!frame.Navigate(typeof(SettingsPage), "Playback")) throw new InvalidOperationException("Settings route failed.");
                    await Until(() => wire.Reads > prior);
                    page = (SettingsPage)frame.Content;
                    if (((FrameworkElement)page.FindName("PlaybackPanel")).Visibility != Visibility.Visible ||
                        ((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Requested Playback tab must appear before the slow requests status finishes.");
                    typeof(SettingsPage).GetMethod("ShowSettingsDetail", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(page, [page.FindName("SubtitlesTab")]);
                    wire.Pending.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"requests_enabled\":true}", System.Text.Encoding.UTF8, "application/json") });
                    await Task.Delay(200);
                    if (((FrameworkElement)page.FindName("SubtitlesPanel")).Visibility != Visibility.Visible ||
                        ((FrameworkElement)page.FindName("SettingsOverviewPanel")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Late settings load reset the user's selected tab.");
                    Program.Log($"PASS: settings route stable during slow load and subsequent tab selection at {width}px.");
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
            var file = await (await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory)).CreateFileAsync("toggle-" + state + ".png", CreationCollisionOption.ReplaceExisting);
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
    private sealed class Services(IServiceProvider fallback, RequestsApi requests) : IServiceProvider
    {
        private readonly ThemeService _theme = new((SettingsService)fallback.GetService(typeof(SettingsService))!, new SettingsApi((SiloApiClient)fallback.GetService(typeof(SiloApiClient))!));
        public object? GetService(Type type) => type == typeof(AuthApi)
            ? new AuthApi((SiloApiClient)fallback.GetService(typeof(SiloApiClient))!)
            : type == typeof(RequestsApi) ? requests
            : type == typeof(ThemeService) ? _theme
            : type == typeof(AccessibilityService) ? new AccessibilityService((SettingsService)fallback.GetService(typeof(SettingsService))!, _theme)
            : fallback.GetService(type);
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
