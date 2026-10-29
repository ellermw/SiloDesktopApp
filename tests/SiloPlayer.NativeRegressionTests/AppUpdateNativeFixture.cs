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
        services.AddSingleton<SafeStagedInstaller>();
        services.AddSingleton<IAppUpdateInstaller>(provider => (Environment.GetEnvironmentVariable("SILO_NATIVE_UPDATE_EXIT") ?? "").StartsWith("staged-drain-", StringComparison.Ordinal)
            ? provider.GetRequiredService<SafeStagedInstaller>() : provider.GetRequiredService<SafeInstaller>());
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
        if (scenario == "content-fit")
            services.GetRequiredService<UpdateWire>().Notes = "## Synthetic changelog\n" + string.Join("\n\n", Enumerable.Repeat(
                "Long release notes describe the update in readable text. Preferences and update controls must remain reachable when this content exceeds the available window height.", 30));
        if (scenario == "background")
        {
            using var stop = new CancellationTokenSource();
            var loop = (Task)typeof(MainWindow).GetMethod("RunUpdateChecksAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [stop.Token])!;
            try
            {
                var until = DateTime.UtcNow.AddSeconds(10);
                while (updates.State.Release is null && DateTime.UtcNow < until) await Task.Delay(25);
                await Until(() => ((Button)root.FindName("UpdateAvailableButton")).Visibility == Visibility.Visible
                    && Descendants<TextBlock>(root).Any(text => text.Text == "Silo 1.2.999 is available."));
                await Task.Delay(400); // Allow the toast's260ms entry animation to finish before capture.
                DependencyObject toast = Descendants<TextBlock>(root).Single(text => text.Text == "Silo 1.2.999 is available.");
                while (toast is not Border) toast = VisualTreeHelper.GetParent(toast)
                    ?? throw new InvalidOperationException("Update toast is detached.");
                if (((Border)toast).Opacity < .9) throw new InvalidOperationException("Automatic update toast remains transparent.");
                if (services.GetRequiredService<UpdateWire>().Downloads != 0 || services.GetRequiredService<SafeInstaller>().Calls != 0)
                    throw new InvalidOperationException("Notification-only background discovery downloaded or installed without consent.");
                await CaptureAsync(root, "automatic-update-notice.png");
                Program.Log("PASS: actual background scheduler discovers the update and renders the green persistent indicator and actionable toast without a manual check, download or installation.");
            }
            finally { stop.Cancel(); await loop; }
            return;
        }
        await updates.CheckAsync();
        if (scenario.StartsWith("staged-drain-", StringComparison.Ordinal))
        {
            await CheckStagedDrainAsync(window, services, updates, scenario, closed.Task);
            return;
        }
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
        if (scenario == "content-fit")
        {
            await CheckDialogContentFitAsync(window, root, dialog, updates, services.GetRequiredService<UpdateWire>());
            if (services.GetRequiredService<UpdateWire>().Downloads != 0 || services.GetRequiredService<SafeInstaller>().Calls != 0)
                throw new InvalidOperationException("Dialog fit verification downloaded or installed an update.");
            dialog.Hide(); await Until(() => field.GetValue(window) is null);
            return;
        }
        if (scenario == "host-centering")
        {
            dialog.Hide(); await Until(() => field.GetValue(window) is null);
            await CheckHostWindowAsync(window);
            if (services.GetRequiredService<UpdateWire>().Downloads != 0 || services.GetRequiredService<SafeInstaller>().Calls != 0)
                throw new InvalidOperationException("Progress view verification downloaded or installed an update.");
            return;
        }
        if (scenario == "centering")
        {
            await CheckDialogCenteringAsync(window, root, nav, dialog);
            if (services.GetRequiredService<UpdateWire>().Downloads != 0 || services.GetRequiredService<SafeInstaller>().Calls != 0)
                throw new InvalidOperationException("Dialog geometry verification downloaded or installed an update.");
            dialog.Hide(); await Until(() => field.GetValue(window) is null);
            Program.Log("PASS: actual Silo updates surface stays centered over the complete app XamlRoot across narrow/wide resize, navigation pane changes, translated owner and maximized owner without any download or installation.");
            return;
        }
        var mode = Descendants<ComboBox>((DependencyObject)dialog.Content).Single(x => x.Name == "AppUpdateModeCombo");
        if (mode.SelectedIndex != 0 || services.GetRequiredService<UpdateWire>().Downloads != 0)
            throw new InvalidOperationException("Notification-only mode downloaded automatically.");
        if (scenario == "rate-limit")
        {
            var wire = services.GetRequiredService<UpdateWire>(); wire.LimitMetadata = true;
            var check = Descendants<Button>(dialog).Single(button => button.Content is string text && text == "Check for updates");
            Click(check);
            await Until(() => updates.State.Error?.Contains("limiting update checks", StringComparison.Ordinal) == true
                && Descendants<TextBlock>((DependencyObject)dialog.Content).Any(text => text.Text.Contains("limiting update checks", StringComparison.Ordinal)));
            var count = wire.MetadataRequests; Click(check); await Task.Delay(150);
            if (wire.MetadataRequests != count || !dialog.IsPrimaryButtonEnabled)
                throw new InvalidOperationException("Rate-limit cooldown allowed another metadata request or disabled the cached manual update.");
            Click(Descendants<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Until(() => updates.State.Error?.Contains("Could not download the update", StringComparison.Ordinal) == true);
            await Task.Delay(100);
            var safe = services.GetRequiredService<SafeInstaller>();
            if (wire.Downloads != 0 || safe.Calls != 0 || safe.ConfirmedCloses != 0 || closed.Task.IsCompleted || wire.MetadataRequests != count)
                throw new InvalidOperationException("GitHub cooldown allowed a download/installation or closed Silo.");
            await Until(() => Descendants<TextBlock>((DependencyObject)dialog.Content).Any(text => text.Text.Contains("limiting update checks", StringComparison.Ordinal)));
            await CaptureAsync(dialog, "rate-limited-update-dialog.png");
            dialog.Hide(); await Until(() => field.GetValue(window) is null);
            Program.Log("PASS: actual update dialog explains the absolute rate limit, suppresses repeated checks and manual downloads, and keeps Silo open without scheduling setup.");
            return;
        }
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

    private static async Task CheckStagedDrainAsync(MainWindow window, IServiceProvider services, AppUpdateService updates, string scenario, Task closed)
    {
        // A completed synthetic stage exercises the actual window-close/save/handoff
        // integration. Neither payload file is an executable, and no helper runs.
        var release = updates.State.Release!;
        var folder = Path.Combine(Program.ResultDirectory, "prepared");
        Directory.CreateDirectory(Path.Combine(folder, "client"));
        var files = new List<UpdateFileEntry>();
        foreach (var name in new[] { "SiloPlayer.exe", "SiloPlayer.dll" })
        {
            var bytes = Encoding.UTF8.GetBytes("Synthetic " + name + "; never executed");
            File.WriteAllBytes(Path.Combine(folder, "client", name), bytes);
            files.Add(new(name, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), files.Count, 1, new string('a', 64)));
        }
        var manifest = new UpdateManifest(1, release.Version.ToString(4), 2, new string('b', 64), files);
        var manifestPath = Path.Combine(folder, UpdateManifestValidation.ManifestName);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest); File.WriteAllBytes(manifestPath, manifestBytes);
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        var package = new PreparedUpdatePackage(folder, manifestPath, manifestHash, manifest);
        var prefix = "https://github.com/ellermw/SiloDesktopApp/releases/download/" + release.Tag + "/";
        release = release with { Incremental = new(new(prefix + UpdateManifestValidation.ManifestName), manifestHash, manifestBytes.Length,
            new(prefix + UpdateManifestValidation.PackName), manifest.PackSha256, manifest.PackSize) };
        typeof(AppUpdateService).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(updates,
            updates.State with { Release = release, PreparedPackage = package, ReadyInstaller = null });
        if (!await updates.ReverifyReadyAsync()) throw new InvalidOperationException("The synthetic prepared payload did not verify.");
        var installer = services.GetRequiredService<SafeStagedInstaller>();
        await installer.PrepareAsync(release, package, true, 0, CancellationToken.None);
        typeof(MainWindow).GetField("_manualUpdateRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        var heldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = scenario == "staged-drain-held" ? heldSave.Task : scenario == "staged-drain-faulted"
            ? Task.FromException(new IOException("Synthetic progress save failed.")) : Task.CompletedTask;
        typeof(PlayerService).GetProperty(nameof(PlayerService.PendingProgressSave))!.SetValue(services.GetRequiredService<PlayerService>(), save);
        try
        {
            SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(window), 0x10, IntPtr.Zero, IntPtr.Zero);
            if (scenario == "staged-drain-complete")
            {
                await closed.WaitAsync(TimeSpan.FromSeconds(9));
                if (installer.Arms != 1 || installer.PreparedCloses != 1 || installer.Cancels != 0)
                    throw new InvalidOperationException("A completed playback save did not authorize exactly one normal staged close.");
            }
            else
            {
                await installer.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(9)); await Task.Delay(100);
                if (installer.Arms != 0 || installer.PreparedCloses != 0 || installer.Cancels == 0 || closed.IsCompleted)
                    throw new InvalidOperationException($"Failed playback save closed Silo or authorized activation: arms={installer.Arms}, preparedCloses={installer.PreparedCloses}, cancels={installer.Cancels}, closed={closed.IsCompleted}.");
                var lifetime = (CancellationTokenSource)typeof(MainWindow).GetField("_appUpdateLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                if (lifetime.IsCancellationRequested || (bool)typeof(MainWindow).GetField("_receiverShutdownStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!)
                    throw new InvalidOperationException("A failed manual update left the still-open app's lifetime or close state stopped.");
            }
            Program.Log($"PASS: actual MainWindow {scenario} preserves the completed-stage playback-save gate: prepares={installer.Prepares}, arms={installer.Arms}, preparedCloses={installer.PreparedCloses}, cancels={installer.Cancels}; no helper/installer executed.");
        }
        finally { heldSave.TrySetResult(); }
    }

    private static async Task CheckHostWindowAsync(MainWindow owner)
    {
        var path = Path.Combine(Program.AppDirectory, "Updater", "SiloPlayer.UpdateHost.dll");
        var type = Assembly.LoadFrom(path).GetType("SiloPlayer.UpdateHost.UpdateProgressWindow", throwOnError: true)!;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        var progressWindow = (Window)Activator.CreateInstance(type, true, hwnd.ToInt64())!;
        TransparentFixtureWindow.Configure(progressWindow);
        var apply = type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The progress view has no production presentation entry point.");
        // The app embeds a compatibility copy of Core types. Construct frames in
        // the helper's own assembly identity rather than passing the app copy.
        var messageType = apply.GetParameters().Single().ParameterType;
        object Message(string kind, string text, long completed = 0, long total = 0)
            => Activator.CreateInstance(messageType, kind, null, text, completed, total, 0, 0L, null)!;
        var status = (TextBlock)type.GetProperty("Status")!.GetValue(progressWindow)!;
        var bar = (ProgressBar)type.GetProperty("Progress")!.GetValue(progressWindow)!;
        var cancel = (Button)type.GetProperty("Cancel")!.GetValue(progressWindow)!;
        try
        {
            progressWindow.AppWindow.Show(false);
            var progressHwnd = WinRT.Interop.WindowNative.GetWindowHandle(progressWindow);
            foreach (var size in new nint[] { 0, 1 })
                if (TaskbarIdentityNativeFixture.IconFingerprint(progressHwnd, size) != TaskbarIdentityNativeFixture.IconFingerprint(hwnd, size))
                    throw new InvalidOperationException("Updater title-bar/taskbar icon differs from the bundled Silo icon.");
            Program.Log("PASS: actual updater window uses bundled Silo pixels for small title-bar and large taskbar icons.");
            foreach (var point in new[] { (X: 140, Y: 120, Width: 1000, Height: 800), (X: 260, Y: 160, Width: 620, Height: 700), (X: -220, Y: -150, Width: 700, Height: 650) })
            {
                owner.AppWindow.Move(new(point.X, point.Y)); owner.AppWindow.ResizeClient(new(point.Width, point.Height));
                await Task.Delay(650); AssertHostBounds();
            }
            apply.Invoke(progressWindow, [Message("progress", "Installing alongside current version", 50, 100)]);
            await Task.Delay(100);
            if (status.Text != "Installing alongside current version" || bar.IsIndeterminate || Math.Abs(bar.Value - 50) > .01)
                throw new InvalidOperationException("Actual progress view did not render deterministic installation progress.");
            await CaptureAsync((FrameworkElement)progressWindow.Content, "update-host-installing.png");
            apply.Invoke(progressWindow, [Message("failed", "Silo has not confirmed startup yet. Your previous version is preserved; launch Silo again to recover if the new version exited. No running app will be closed.")]);
            await Task.Delay(100);
            var content = (FrameworkElement)progressWindow.Content; content.UpdateLayout();
            var cancelBounds = cancel.TransformToVisual(content).TransformBounds(new Windows.Foundation.Rect(0, 0, cancel.ActualWidth, cancel.ActualHeight));
            if (cancelBounds.Y < 0 || cancelBounds.Bottom > content.ActualHeight + 1)
                throw new InvalidOperationException("Updater failure status clips its actionable Close control.");
            await CaptureAsync(content, "update-host-error.png");
            if (owner.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.Maximize(); await Task.Delay(650); AssertHostBounds(); presenter.Restore();
            }
            Program.Log("PASS: actual finite updater view follows owner client bounds, remains in monitor work area, renders real progress/error states and preserves its actionable control without any worker/elevation.");
        }
        finally { progressWindow.Close(); }

        void AssertHostBounds()
        {
            if (!GetClientRect(hwnd, out var ownerRect)) throw new System.ComponentModel.Win32Exception();
            var origin = new NativePoint(); if (!ClientToScreen(hwnd, ref origin)) throw new System.ComponentModel.Win32Exception();
            var info = new NativeMonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) throw new System.ComponentModel.Win32Exception();
            var hostHwnd = WinRT.Interop.WindowNative.GetWindowHandle(progressWindow);
            if (!GetWindowRect(hostHwnd, out var actual)) throw new System.ComponentModel.Win32Exception();
            var width = actual.Right - actual.Left; var height = actual.Bottom - actual.Top;
            var expectedX = Math.Clamp(origin.X + (ownerRect.Right - width) / 2, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
            var expectedY = Math.Clamp(origin.Y + (ownerRect.Bottom - height) / 2, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
            Program.Log($"Updater window: ownerClient={origin.X},{origin.Y},{ownerRect.Right},{ownerRect.Bottom}; work={info.Work.Left},{info.Work.Top},{info.Work.Right},{info.Work.Bottom}; actual={actual.Left},{actual.Top},{width},{height}; expected={expectedX},{expectedY}.");
            if (Math.Abs(actual.Left - expectedX) > 2 || Math.Abs(actual.Top - expectedY) > 2)
                throw new InvalidOperationException("Finite updater window is offset from owner client center or outside work area.");
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeMonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

    private static async Task CheckDialogCenteringAsync(MainWindow window, FrameworkElement root, NavigationView nav, ContentDialog dialog)
    {
        var surface = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement");
        var cases = new[]
        {
            (Width: 460, Height: 760, Open: false, Visible: true, X: -20000, Y: -20000),
            (Width: 900, Height: 700, Open: true, Visible: true, X: -20000, Y: -20000),
            (Width: 1280, Height: 900, Open: true, Visible: true, X: -15000, Y: -13000),
            (Width: 1280, Height: 900, Open: false, Visible: true, X: -15000, Y: -13000),
            (Width: 1280, Height: 900, Open: false, Visible: false, X: 120, Y: 90)
        };
        var index = 0;
        foreach (var item in cases)
        {
            window.AppWindow.Move(new(item.X, item.Y));
            window.AppWindow.ResizeClient(new(item.Width, item.Height));
            nav.IsPaneVisible = item.Visible; nav.IsPaneOpen = item.Open;
            await Task.Delay(450); root.UpdateLayout(); dialog.UpdateLayout();
            AssertCentered("case" + ++index);
            if (index is 1 or 3 or 5) await CaptureAsync(dialog, "update-centered-" + index + ".png");
        }
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Maximize();
            await Task.Delay(450); root.UpdateLayout(); dialog.UpdateLayout();
            AssertCentered("maximized"); await CaptureAsync(dialog, "update-centered-maximized.png");
            presenter.Restore();
        }

        void AssertCentered(string label)
        {
            var owner = root.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, root.ActualWidth, root.ActualHeight));
            var bounds = surface.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
            var dx = bounds.X + bounds.Width / 2 - (owner.X + owner.Width / 2);
            var dy = bounds.Y + bounds.Height / 2 - (owner.Y + owner.Height / 2);
            Program.Log($"Update dialog {label}: root={owner}; surface={bounds}; centerDelta={dx:R},{dy:R}; rasterScale={root.XamlRoot.RasterizationScale:R}; pane={nav.IsPaneVisible}/{nav.IsPaneOpen}.");
            if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2)
                throw new InvalidOperationException($"Silo updates is offset from the complete app center in {label}: {dx:R},{dy:R}.");
            if (bounds.X < owner.X - 1 || bounds.Y < owner.Y - 1 || bounds.Right > owner.Right + 1 || bounds.Bottom > owner.Bottom + 1)
                throw new InvalidOperationException("The centered update surface is clipped in " + label);
        }
    }

    private static async Task CheckDialogContentFitAsync(MainWindow window, FrameworkElement root, ContentDialog dialog, AppUpdateService updates, UpdateWire wire)
    {
        var surface = Descendants<Border>(dialog).Single(x => x.Name == "BackgroundElement");
        var check = Descendants<Button>(dialog).Single(x => x.Content as string == "Check for updates");
        var primary = Descendants<Button>(dialog).Single(x => x.Name == "PrimaryButton");
        var later = Descendants<Button>(dialog).Single(x => x.Name == "CloseButton");
        var mode = Descendants<ComboBox>(dialog).Single(x => x.Name == "AppUpdateModeCombo");
        var sizes = new[] { (3840, 2160), (1920, 1080), (1280, 720), (900, 540), (460, 700) };
        foreach (var (width, height) in sizes)
        {
            window.AppWindow.ResizeClient(new(width, height));
            await Task.Delay(180); root.UpdateLayout(); dialog.UpdateLayout();
            AssertVisible(check); AssertVisible(primary); AssertVisible(later);
            var before = Box(check);
            var scroll = Descendants<ScrollViewer>(dialog.Content as DependencyObject ?? dialog)
                .SingleOrDefault(x => x.Content is StackPanel panel && Descendants<ComboBox>(panel).Contains(mode));
            if (scroll is null || scroll.ScrollableHeight <= 0)
                throw new InvalidOperationException("Long update content has no bounded body scroller containing its preferences.");
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
            await Task.Delay(100);
            if (Math.Abs(Box(check).Y - before.Y) > 1)
                throw new InvalidOperationException("Check for updates moves out of reach when the body scrolls.");
            mode.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Task.Delay(100); AssertVisible(mode);
            check.Focus(FocusState.Programmatic);
            await Task.Delay(50); AssertVisible(check);
            scroll.ChangeView(null, 0, null, true);
            if (width is 3840 or 900 or 460) await CaptureAsync(dialog, $"update-content-fit-{width}.png");
            Program.Log($"PASS: long update content {width}x{height}, raster={root.XamlRoot.RasterizationScale:R}; Check and footer fully visible, preferences scroll/focus into view.");
        }
        foreach (var text in Descendants<TextBlock>(dialog.Content as DependencyObject ?? dialog)) text.FontSize *= 1.5;
        window.AppWindow.ResizeClient(new(900, 600));
        await Task.Delay(200); dialog.UpdateLayout();
        AssertVisible(check); AssertVisible(primary); AssertVisible(later);
        mode.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(100); AssertVisible(mode);
        wire.Notes = "## Synthetic changelog after recheck\nA short release summary.";
        var requests = wire.MetadataRequests; Click(check);
        await Until(() => wire.MetadataRequests > requests && !updates.State.IsChecking);
        await Until(() => Descendants<TextBlock>(dialog).Any(x => x.Name == "AppUpdateReleaseNotes" && x.Text.Contains("after recheck")));
        AssertVisible(check); AssertVisible(primary); AssertVisible(later);
        await CaptureAsync(dialog, "update-content-fit-large-text-refreshed.png");
        Program.Log("PASS: enlarged body text and live metadata recheck preserve complete Check/Update/Later controls and refresh the release notes without closing the dialog.");

        Windows.Foundation.Rect Box(FrameworkElement item) => item.TransformToVisual(null).TransformBounds(new(0, 0, item.ActualWidth, item.ActualHeight));
        void AssertVisible(FrameworkElement item)
        {
            var bounds = Box(item); var outer = Box(surface);
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < outer.X - 1 || bounds.Y < outer.Y - 1 || bounds.Right > outer.Right + 1 || bounds.Bottom > outer.Bottom + 1)
                throw new InvalidOperationException($"Update control {item.Name}/{(item as Button)?.Content} exceeds the dialog: {bounds}; surface={outer}.");
            for (DependencyObject? parent = VisualTreeHelper.GetParent(item); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is FrameworkElement clip && (parent is ScrollViewer || parent.GetType().Name == "ScrollContentPresenter"))
                {
                    var viewport = Box(clip);
                    if (bounds.Y < viewport.Y - 1 || bounds.Bottom > viewport.Bottom + 1)
                        throw new InvalidOperationException($"Update control {item.Name}/{(item as Button)?.Content} is clipped by {clip.Name}/{parent.GetType().Name}: {bounds}; viewport={viewport}.");
                }
        }
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
    private sealed class SafeStagedInstaller : IAppUpdateInstaller, IStagedAppUpdateInstaller
    {
        public bool IsSupported => true;
        public DateTimeOffset? AuthorityCooldownUntil => null;
        public bool HasPreparedHandoff { get; private set; }
        public int Prepares, Arms, Cancels, PreparedCloses;
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> PrepareAsync(AppRelease release, PreparedUpdatePackage package, bool restart, long ownerWindow, CancellationToken ct)
        { Prepares++; HasPreparedHandoff = true; return Task.FromResult(true); }
        public void ArmNormalClose() { if (!HasPreparedHandoff) throw new InvalidOperationException("No prepared transaction."); Arms++; }
        public void CancelPreparedHandoff() { if (HasPreparedHandoff) Cancels++; HasPreparedHandoff = false; Canceled.TrySetResult(); }
        public Task<bool> ScheduleAsync(AppRelease release, string installerPath, bool restart, CancellationToken ct)
            => throw new InvalidOperationException("A prepared transaction must not schedule a legacy installer.");
        public void ConfirmNormalClose() { if (HasPreparedHandoff) PreparedCloses++; }
    }
    private sealed class UpdateWire : HttpMessageHandler
    {
        private readonly byte[] _package = "Synthetic update, never executable"u8.ToArray();
        public int Downloads;
        public bool HoldDownload;
        public bool HoldMetadata;
        public bool LimitMetadata;
        public int MetadataRequests;
        public string Notes = "## Synthetic changelog\nFixed playback.\n- UI updates";
        public TaskCompletionSource MetadataEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMetadata = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DownloadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                MetadataRequests++;
                if (LimitMetadata)
                {
                    var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(45));
                    return limited;
                }
                if (HoldMetadata) { MetadataEntered.TrySetResult(); await ReleaseMetadata.Task.WaitAsync(ct); }
                var json = JsonSerializer.Serialize(new { tag_name = "v1.2.999", draft = false, prerelease = false, body = Notes, published_at = "2026-10-28T12:00:00Z",
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
