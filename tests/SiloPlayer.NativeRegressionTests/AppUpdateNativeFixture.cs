using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Updates;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Views;

internal static class AppUpdateNativeFixture
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<UpdateWire>();
        services.AddSingleton(provider => new AppUpdateService(new HttpClient(provider.GetRequiredService<UpdateWire>()),
            Path.Combine(Program.ResultDirectory, "updates"), typeof(App).Assembly.GetName().Version!));
        services.AddSingleton<SafeInstaller>();
        services.AddSingleton<IAppUpdateInstaller>(provider => provider.GetRequiredService<SafeInstaller>());
    }

    internal static async Task CheckAsync(MainWindow window, IServiceProvider services)
    {
        var root = (FrameworkElement)window.Content;
        var scenario = Environment.GetEnvironmentVariable("SILO_NATIVE_UPDATE_EXIT") ?? "automatic";
        var navigation = services.GetRequiredService<NavigationService>(); navigation.Navigate<HomePage>();
        await Until(() => navigation.Frame?.Content is HomePage { IsLoaded: true });
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        var nav = (NavigationView)root.FindName("NavView"); nav.IsPaneVisible = true; nav.IsPaneOpen = true;
        var updates = services.GetRequiredService<AppUpdateService>();
        await updates.CheckAsync();
        var indicator = root.FindName("UpdateAvailableButton") as Button
            ?? throw new InvalidOperationException("The persistent update indicator is missing.");
        await Until(() => indicator.Visibility == Visibility.Visible);
        Program.Log("Update fixture: green indicator available.");
        if (indicator.Foreground is not SolidColorBrush color || color.Color.G <= color.Color.R)
            throw new InvalidOperationException("Update availability is not visibly green.");
        Click(indicator);
        var field = typeof(MainWindow).GetField("_appUpdateDialog", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await Until(() => field.GetValue(window) is ContentDialog { IsLoaded: true });
        var dialog = (ContentDialog)field.GetValue(window)!;
        var notes = Descendants<TextBlock>((DependencyObject)dialog.Content).Single(x => x.Name == "AppUpdateReleaseNotes");
        if (!notes.Text.Contains("Synthetic changelog", StringComparison.Ordinal)) throw new InvalidOperationException("In-app changelog is missing.");
        await CaptureAsync((FrameworkElement)dialog.Content, "update-dialog.png");
        var mode = Descendants<ComboBox>((DependencyObject)dialog.Content).Single(x => x.Name == "AppUpdateModeCombo");
        if (mode.SelectedIndex != 0 || services.GetRequiredService<UpdateWire>().Downloads != 0)
            throw new InvalidOperationException("Notification-only mode downloaded automatically.");
        if (scenario == "overlap")
        {
            var wire = services.GetRequiredService<UpdateWire>(); wire.HoldMetadata = true;
            Click(Descendants<Button>(dialog).Single(button => button.Content is string text && text == "Check for updates"));
            await wire.MetadataEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Click(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Task.Delay(100);
            if (closed.Task.IsCompleted || wire.Downloads != 0) throw new InvalidOperationException("Update did not wait for the held check.");
            wire.ReleaseMetadata.TrySetResult();
            try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException) { throw new InvalidOperationException("Update Now was dropped while Check for updates was running."); }
            var safe = services.GetRequiredService<SafeInstaller>();
            if (wire.Downloads != 1 || safe.Calls != 1 || safe.ConfirmedCloses != 1 || !safe.Restart)
                throw new InvalidOperationException("Overlapping Update Now did not download, verify and schedule exactly once.");
            Program.Log("PASS: actual Check for updates/Update Now overlap downloads, verifies and schedules exactly once without executing setup.");
            return;
        }
        if (scenario == "later")
        {
            var wire = services.GetRequiredService<UpdateWire>(); wire.HoldDownload = true;
            Click(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await wire.DownloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Click(Descendants<Button>(dialog).Single(button => button.Name == "CloseButton"));
            await Until(() => field.GetValue(window) is null);
            wire.ReleaseDownload.TrySetResult();
            await Until(() => !updates.State.IsDownloading); await Task.Delay(500);
            if (closed.Task.IsCompleted || services.GetRequiredService<SafeInstaller>().Calls != 0)
                throw new InvalidOperationException("Later failed to revoke pending manual installation.");
            updates.SetMode(AppUpdateMode.NotifyOnly);
            SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x10, IntPtr.Zero, IntPtr.Zero);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Program.Log("PASS: Later revokes pending manual installation without closing the app.");
            return;
        }
        mode.SelectedIndex = 1;
        await Until(() => updates.State.ReadyInstaller is not null);
        Program.Log("Update fixture: automatic mode downloaded the verified synthetic package.");
        if (services.GetRequiredService<SettingsService>().Load().UpdateMode != AppUpdateMode.Automatic || !dialog.IsPrimaryButtonEnabled)
            throw new InvalidOperationException("Automatic mode hid manual update or failed to persist.");
        mode.SelectedIndex = 0;
        if (updates.ShouldInstallOnExit) throw new InvalidOperationException("Notification-only selection failed to disarm automatic installation.");
        mode.SelectedIndex = 1;
        if (!dialog.IsPrimaryButtonEnabled) throw new InvalidOperationException("Manual update is unavailable in automatic mode.");
        dialog.Hide(); await Task.Delay(100);
        navigation.Navigate<SettingsPage>();
        await Until(() => navigation.Frame?.Content is SettingsPage { IsLoaded: true });
        var settings = (SettingsPage)navigation.Frame!.Content;
        settings.SelectSettingsSection("playback");
        var settingsMode = (ComboBox)settings.FindName("AppUpdateSettingsModeCombo");
        if (settingsMode.SelectedIndex != 1) throw new InvalidOperationException("Settings did not reflect the automatic preference.");
        settingsMode.SelectedIndex = 0;
        if (updates.Mode != AppUpdateMode.NotifyOnly) throw new InvalidOperationException("Settings failed to change update mode.");
        settingsMode.SelectedIndex = 1;
        navigation.Navigate<HomePage>();
        await Until(() => navigation.Frame?.Content is HomePage { IsLoaded: true });
        foreach (var size in new[] { (900, 650), (1920, 1080), (3440, 1440), (3840, 2160) })
        {
            window.AppWindow.ResizeClient(new(size.Item1, size.Item2)); await Task.Delay(350); root.UpdateLayout();
            if (indicator.Visibility == Visibility.Visible)
            {
                var bounds = indicator.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, indicator.ActualWidth, indicator.ActualHeight));
                if (bounds.X < 0 || bounds.Right > root.ActualWidth + 1 || bounds.Bottom > root.ActualHeight + 1)
                    throw new InvalidOperationException("Update indicator is clipped at " + size);
            }
            if (size.Item1 == 1920)
            {
                await CaptureAsync(root, "update-home-1080p.png");
                var badgeBounds = indicator.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, indicator.ActualWidth, indicator.ActualHeight));
                Program.Log($"Update footer: open={nav.OpenPaneLength} root={root.ActualWidth} badge={badgeBounds}.");
                for (var ancestor = VisualTreeHelper.GetParent(indicator); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                {
                    if (ancestor is not FrameworkElement element || element.ActualWidth <= 0) continue;
                    var ancestorBounds = element.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                    Program.Log($"Update footer ancestor: {element.GetType().Name}/{element.Name} {ancestorBounds}.");
                    if (badgeBounds.Right > ancestorBounds.Right + 1)
                        throw new InvalidOperationException("Update label extends outside its sidebar ancestor " + element.Name);
                }
            }
        }
        var installer = services.GetRequiredService<SafeInstaller>();
        if (installer.Calls != 0) throw new InvalidOperationException("Opening update details unexpectedly installed an update.");
        if (scenario == "notify") updates.SetMode(AppUpdateMode.NotifyOnly);
        if (scenario == "session") SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x11, IntPtr.Zero, IntPtr.Zero);
        if (scenario == "canceled-session")
        {
            SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x11, IntPtr.Zero, IntPtr.Zero);
            SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x16, IntPtr.Zero, IntPtr.Zero);
        }
        if (scenario == "manual")
        {
            Click(indicator);
            await Until(() => field.GetValue(window) is ContentDialog { IsLoaded: true });
            var active = (ContentDialog)field.GetValue(window)!;
            var primary = Descendants<Button>(active).Single(button => button.Name == "PrimaryButton");
            Click(primary);
        }
        else SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x10, IntPtr.Zero, IntPtr.Zero);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var expectedCalls = scenario is "notify" or "session" ? 0 : 1;
        if (installer.Calls != expectedCalls || installer.ConfirmedCloses != expectedCalls)
            throw new InvalidOperationException($"Exit case {scenario}: scheduled={installer.Calls}, confirmed={installer.ConfirmedCloses}.");
        if (installer.Restart != (scenario == "manual")) throw new InvalidOperationException("Update handoff restart policy was wrong.");
        Program.Log("PASS: update exit case " + scenario + " preserves explicit/automatic consent and restart policy.");
        Program.Log("PASS: native green update indicator, changelog, notify-only/automatic preferences, persistent manual action, 900/1080p/ultrawide/4K bounds and safe automatic exit handoff.");
    }

    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Update UI condition did not settle."); await Task.Delay(20); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i); if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); var raster = new RenderTargetBitmap(); await raster.RenderAsync(element);
        if (raster.PixelWidth == 0 || raster.PixelHeight == 0) throw new InvalidOperationException("Update capture has no pixels.");
        var pixels = (await raster.GetPixelsAsync()).ToArray();
        var file = await (await StorageFolder.GetFolderFromPathAsync(Program.ResultDirectory)).CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    private sealed class SafeInstaller : IAppUpdateInstaller
    {
        public bool IsSupported => true;
        public int Calls; public bool Restart; public int ConfirmedCloses;
        public Task<bool> ScheduleAsync(AppRelease release, string path, bool restart, CancellationToken ct)
        { if (!File.Exists(path)) throw new InvalidOperationException("Missing synthetic staged package."); Calls++; Restart = restart; return Task.FromResult(true); }
        public void ConfirmNormalClose() { if (Calls != 0) ConfirmedCloses++; }
    }
    private sealed class UpdateWire : HttpMessageHandler
    {
        private readonly byte[] _package = "Synthetic update, never executable"u8.ToArray();
        public int Downloads;
        public bool HoldDownload;
        public bool HoldMetadata;
        public TaskCompletionSource MetadataEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMetadata = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DownloadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                if (HoldMetadata) { MetadataEntered.TrySetResult(); await ReleaseMetadata.Task.WaitAsync(ct); }
                var json = JsonSerializer.Serialize(new { tag_name = "v1.2.999", draft = false, prerelease = false, body = "## Synthetic changelog\nFixed playback.\n- UI updates", published_at = "2026-10-28T12:00:00Z",
                    assets = new[] { new { name = "SiloInstaller-Windows-x64.exe", state = "uploaded", size = _package.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(_package)), browser_download_url = "https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.999/SiloInstaller-Windows-x64.exe" } } });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
            if (request.RequestUri.Host != "github.com") throw new InvalidOperationException("Native update fixture attempted remote networking.");
            Downloads++; DownloadEntered.TrySetResult();
            if (HoldDownload) await ReleaseDownload.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_package) };
        }
    }
}
