using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminMonitorShellNativeFixture
{
    // Ported deliberately from the stopped writer's test-only candidate. These
    // assertions run through the registered shell/Frame, against production DLLs.
    private static async Task ActivityCorrectionAsync(Window window, Server server, ServerActivityStore store)
    {
        MonitorSourceProof.Verify();
        server.SessionCount = 3;
        await store.RefreshSessionsAsync();
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)frame.Content;
        var page = (AdminActivityPage)((Frame)Field(shell, "_frame")!).Content;
        await Until(() => page.RenderedSessionIds.Count == 3);
        Check(page.RenderedSessionIds.Count == 3, "correction fixture loaded real Activity rows");
        foreach (var width in new[] { 1800, 1600, 1440, 1120, 800 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000));
            await Task.Delay(250);
            var table = (StackPanel)Field(page, "_tableHost")!;
            var scroller = (ScrollViewer)Field(page, "_scroller")!;
            table.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            await Task.Delay(100);
            Program.Log($"TRACE correction width={width} table={table.ActualWidth} wide={page.IsWideTable} scale={frame.XamlRoot.RasterizationScale}");
            var actions = Descendants(table).OfType<Button>().Where(button => AutomationProperties.GetName(button) == AdminText.T("web.admin.sessions.playback.menu.ariaLabel")).ToArray();
            Check(actions.Length == page.RenderedSessionIds.Count, $"{width}: each session has its canonical actions menu");
            Check(!Descendants(table).OfType<Button>().Any(button => AutomationProperties.GetName(button) == AdminText.T("web.admin.sessions.playback.action.terminate")), $"{width}: no inline destructive Terminate shortcut");
            Check(actions.All(button => button.Flyout is MenuFlyout menu && menu.Items.OfType<MenuFlyoutItem>().Count(item => item.Text == AdminText.T("web.admin.sessions.playback.action.terminate")) == 1), $"{width}: Terminate is reachable once through each actions menu");
            foreach (var text in Descendants(table).OfType<TextBlock>().Where(t => t.ActualWidth > 0 && t.ActualHeight > 0))
                Check(text.ActualHeight + 0.1 >= text.FontSize, $"{width}: {text.Text} has full text line height ({text.ActualHeight:0.0}/{text.FontSize:0.0})");
            await CaptureAsync(frame, $"activity-correction-{width}.png");
            if (page.IsWideTable)
                Check(Descendants(table).OfType<TextBlock>().Any(t => t.Text == "BITRATE" || t.Text == "Bitrate"), $"{width}: current Activity has Bitrate column");
            Check(Descendants(table).OfType<TextBlock>().Any(t => t.Text == "Video codec not supported"), $"{width}: server adaptation reason is visible");
            Check(!Descendants(table).OfType<TextBlock>().Any(t => t.Text.StartsWith("Maximum bitrate setting", StringComparison.Ordinal)), $"{width}: later reasons stay in the collapsed details");
            var more = Descendants(table).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b).Contains("more reason", StringComparison.OrdinalIgnoreCase));
            Check(more != null, $"{width}: additional reasons have a keyboard reachable details control");
            if (more != null)
            {
                Check(more.Focus(FocusState.Keyboard), $"{width}: more reasons accepts keyboard focus");
                var focusId = AutomationProperties.GetAutomationId(more);
                Invoke(more);
                await Task.Delay(100);
                Check(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(frame.XamlRoot) is Button focused && AutomationProperties.GetAutomationId(focused) == focusId,
                    $"{width}: expanding reasons preserves the keyboard target");
                Check(Descendants(table).OfType<TextBlock>().Any(t => t.Text.StartsWith("Maximum bitrate setting", StringComparison.Ordinal)), $"{width}: expanded panel shows all known reasons");
                Check(!Descendants(table).OfType<TextBlock>().Any(t => t.Text.Contains("future_reason")), $"{width}: unknown reason is omitted like WebUI");
                foreach (var label in new[] { "Source", "Delivered", "Client", "Session" })
                    Check(Descendants(table).OfType<TextBlock>().Any(t => string.Equals(t.Text, label, StringComparison.OrdinalIgnoreCase)), $"{width}: expanded panel includes {label}");
                foreach (var text in Descendants(table).OfType<TextBlock>().Where(t => t.ActualWidth > 0 && t.ActualHeight > 0))
                    Check(text.ActualHeight + 0.1 >= text.FontSize, $"{width}: expanded {text.Text} has full text line height");
                await CaptureAsync(frame, $"activity-correction-reasons-{width}.png");
                var details = Descendants(table).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == AdminText.T("web.admin.activity.row.details") && AutomationProperties.GetItemStatus(b) == "expanded");
                if (details != null) Invoke(details);
                await Task.Delay(80);
            }
            scroller.ChangeView(null, 0, null, true);
        }
        server.UniformRoutes = true;
        await store.RefreshSessionsAsync();
        foreach (var width in new[] { 1312, 1311 })
        {
            window.AppWindow.ResizeClient(new(width, 1000));
            await Task.Delay(200);
            Check(page.IsWideTable == (width == 1312), $"{width}: uniform routes use the 62rem container boundary");
            if (page.IsWideTable) Check(!Descendants((StackPanel)Field(page, "_tableHost")!).OfType<TextBlock>().Any(t => t.Text == "Route"), "uniform server routes omit the redundant column");
        }
        window.AppWindow.ResizeClient(new(800, 1000));
        await Task.Delay(120);
        var header = (StackPanel)Field(page, "_header")!;
        var lookupButton = Descendants(header).OfType<Button>().Single(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "IP Lookup"));
        Invoke(lookupButton);
        await Until(() => Field(page, "_ipDialog") is ContentDialog { IsLoaded: true });
        var dialog = (ContentDialog)Field(page, "_ipDialog")!;
        var ipBox = (TextBox)Field(page, "_ipBox")!;
        Button LookupSubmit() => Descendants(dialog).OfType<Button>().Single(b => Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Lookup"));
        var submit = LookupSubmit();
        var form = ipBox.Parent;
        var lookup = (AdminActivityIpLookup)Field(page, "_ipLookup")!;
        string? previousUsername = null;
        async Task SubmitLookupAsync(string ip, string username, bool enter)
        {
            server.IpLookupUsername = username;
            ipBox.Text = ip;
            await Until(() => submit.IsEnabled);
            Check(submit.IsEnabled, $"{ip}: IP Lookup becomes enabled after TextChanged");
            if (!submit.IsEnabled) throw new InvalidOperationException("IP Lookup did not enable for the entered address.");
            Check(ipBox.Focus(FocusState.Keyboard), $"{ip}: retained IP input accepts keyboard focus");
            if (enter) SendIpLookupEnter();
            else Invoke(submit);
            await Until(() => lookup.ActiveIp == ip && !lookup.IsLoading && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == username));
            Check(lookup.ActiveIp == ip && !lookup.IsLoading && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == username), $"{ip}: {(enter ? "Enter" : "Lookup")} updates the real bounded lookup results");
            Check(previousUsername == null || !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == previousUsername), $"{ip}: prior IP result is replaced");
            Check(ReferenceEquals(Field(page, "_ipDialog"), dialog) && dialog.IsLoaded, $"{ip}: the same IP Lookup dialog stays open");
            Check(ReferenceEquals(Field(page, "_ipBox"), ipBox) && Descendants(dialog).Contains(ipBox) && ipBox.IsLoaded && ReferenceEquals(ipBox.Parent, form), $"{ip}: the same TextBox stays mounted");
            Check(ReferenceEquals(LookupSubmit(), submit) && ReferenceEquals(Field(page, "_ipSubmit"), submit) && submit.IsLoaded && ReferenceEquals(submit.Parent, form), $"{ip}: the same Lookup Button stays mounted");
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot), ipBox), $"{ip}: lookup rendering retains active input focus");
            previousUsername = username;
        }
        await SubmitLookupAsync("203.0.113.11", "synthetic-viewer", enter: false);
        await SubmitLookupAsync("203.0.113.12", "synthetic-second-viewer", enter: false);
        await SubmitLookupAsync("203.0.113.13", "synthetic-keyboard-viewer", enter: true);
        await CaptureAsync(dialog, "activity-ip-lookup-800.png");
        dialog.Hide();
        await Until(() => Field(page, "_ipDialog") == null);
        Check(((TextBox)Field(page, "_ipBox")!).Text.Length == 0, "closing IP Lookup clears its draft");
    }

    private static void SendIpLookupEnter()
    {
        var target = GetFocus();
        GetWindowThreadProcessId(target, out var process);
        if (target == 0 || process != (uint)Environment.ProcessId) throw new InvalidOperationException("IP Lookup key target is not an owned focused HWND.");
        if (!PostMessage(target, 0x0100, 0x0D, 1) || !PostMessage(target, 0x0101, 0x0D, unchecked((nint)0xC0000001u)))
            throw new InvalidOperationException("Could not deliver Enter to the owned IP Lookup HWND.");
    }

    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint target, out uint process);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint target, uint message, nuint wParam, nint lParam);
}
