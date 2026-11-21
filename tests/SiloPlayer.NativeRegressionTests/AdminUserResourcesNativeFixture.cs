using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Real registration, shell and Frame against a stateful synthetic server. It drives the three pages through loading, empty, failed, populated,
/// every dialog and every mutation, including refused, conflicting and lost-answer writes. Synthetic values only; current DPI only.
/// </summary>
internal static partial class AdminUserResourcesNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool value, string label)
    {
        Program.Log((value ? "PASS: " : "FAIL: ") + label);
        if (!value) throw new InvalidOperationException(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 12000)
    {
        bool Holds() { try { return condition(); } catch (InvalidOperationException) { return false; } }
        for (var elapsed = 0; elapsed < milliseconds && !Holds(); elapsed += 25) await Task.Delay(25);
        if (!Holds() && Application.Current is not null && Window.Current is null) LogStatus();
        Check(Holds(), label);
    }
    /// <summary>Buttons pressed and dialogs opened by <see cref="OpenDialogAsync"/> in this run: one press per dialog, so the two numbers are equal.</summary>
    private static int DialogClicks, DialogsOpened;
    /// <summary>
    /// Opens a dialog by pressing its button ONCE, after the previous dialog has left the screen. The press is never repeated: a dialog that does not
    /// open within the wait fails the scenario, so a dropped click is a failure and not something the fixture hides. (Before the page host waited for a
    /// closing dialog, a press made between the popup leaving the screen and its owner finishing was dropped without a sound.)
    /// </summary>
    private static async Task OpenDialogAsync(Context c, Func<Button> button, string label)
    {
        await Until(() => Dialog(c.Frame) == null, label + ": the previous dialog is gone");
        Click(button()); DialogClicks++;
        for (var waited = 0; waited < 6000; waited += 25)
        {
            // The popup exists before its content is in the visual tree: let one layout pass realize it.
            if (Dialog(c.Frame) is { } shown) { await Task.Delay(200); shown.UpdateLayout(); await Task.Delay(50); DialogsOpened++; Program.Log("INFO: clicks=1 " + label); Check(true, label); return; }
            await Task.Delay(25);
        }
        Program.Log("INFO: clicks=1, no dialog opened: " + label);
        Check(false, label + " (opened by the one click)");
    }
    /// <summary>The dialog has gone and its owner has finished with it too, so the next action is not refused as "already showing".</summary>
    private static async Task GoneAsync(Context c, string label) { await Until(() => Dialog(c.Frame) == null, label); await Task.Delay(250); }
    private static IEnumerable<DependencyObject> All(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in All(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static Frame? _statusFrame;
    private static void LogStatus()
    {
        try
        {
            if (_statusFrame == null) return;
            var status = All(_statusFrame).OfType<TextBlock>().FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == "ResourceStatus");
            Program.Log("STATUS: " + (status == null ? "(no status line)" : status.Text.Length == 0 ? "(empty)" : status.Text));
            foreach (var page in All(_statusFrame).OfType<Page>())
                if (page.GetType().GetProperty("Session")?.GetValue(page) is AdminUserResourceController session && session.LastException is { } last) Program.Log("CAUSE: " + last.GetType().Name + " " + last.Message + " @ " + string.Join(" | ", (last.StackTrace ?? "").Split('\n').Take(7).Select(x => x.Trim())));
        }
        catch (Exception error) { Program.Log("STATUS: unavailable " + error.Message); }
    }
    private static async Task SetTextAsync(TextBox box, string text) { box.Text = text; await Task.Delay(180); }
    private static async Task SetPasswordAsync(PasswordBox box, string text) { box.Password = text; await Task.Delay(180); }
    private static T? TryFind<T>(DependencyObject root, string id) where T : DependencyObject => All(root).OfType<T>().FirstOrDefault(x => AutomationProperties.GetAutomationId(x) == id);
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => TryFind<T>(root, id) ?? throw new InvalidOperationException("Missing control " + id);
    private static bool Has(DependencyObject root, string id) => TryFind<DependencyObject>(root, id) != null;
    private static bool HasText(DependencyObject root, string text) => All(root).OfType<TextBlock>().Any(x => x.Text.Contains(text, StringComparison.Ordinal));
    private static void Click(Button button)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("Button is disabled: " + AutomationProperties.GetAutomationId(button));
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static ContentDialog? Dialog(FrameworkElement root) => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(popup => All(popup.Child)).OfType<ContentDialog>().FirstOrDefault();
    private static Button Primary(ContentDialog dialog) => All(dialog).OfType<Button>().First(x => x.Name == "PrimaryButton");
    private static void SelectTag(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>().First(x => (string)x.Tag == tag);
    private static string T(string key, params (string Name, object? Value)[] args) => AdminUserResourceText.T(key, args);
    private static async Task CloseDialogAsync(FrameworkElement frame, string label)
    {
        if (Dialog(frame) is { } dialog) dialog.Hide();
        await Until(() => Dialog(frame) == null, label);
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(80);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private sealed class Context
    {
        internal required Window Window;
        internal required Frame Frame;
        internal required AdminUserResourcesFakeServer Server;
        internal required AdminMonitorAuthority Authority;
        internal AdminAccessGroupsPage Groups => All(Frame).OfType<AdminAccessGroupsPage>().Single();
        internal AdminDevicesPage Devices => All(Frame).OfType<AdminDevicesPage>().Single();
        internal AdminHistoryImportPage History => All(Frame).OfType<AdminHistoryImportPage>().Single();
        internal async Task OpenAsync(AdminRouteRequest request, string label)
        {
            // A different page first, so the target is always a freshly created page instance; an unsaved-changes prompt is answered with Discard.
            await Task.Delay(60);
            AdminShellPage.Open(new AdminRouteRequest(request.Route == AdminRoute.Devices ? AdminRoute.AccessGroups : AdminRoute.Devices));
            await PassLeavePromptAsync();
            Check(AdminShellPage.Open(request), label + " opens through real registration");
            await PassLeavePromptAsync();
        }
        private async Task PassLeavePromptAsync()
        {
            await Task.Delay(150);
            if (Dialog(Frame) is { } prompt && prompt.PrimaryButtonText == "Discard") { Click(Primary(prompt)); await Task.Delay(150); }
        }
    }
    internal static async Task RunAsync(Window window)
    {
        using var server = new AdminUserResourcesFakeServer(); using var http = new HttpClient(server);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var settingsApi = new SettingsApi(client); var settings = new SettingsService(Program.ResultDirectory);
        // The subtitle appearance dialog (the device override editor) takes the settings API and the player service from the container.
        using var player = new PlayerService(new PlaybackApi(client), new CatalogApi(client), auth, client, settings, settingsApi);
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "fixture-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService())
            .AddSingleton(settings).AddSingleton(settingsApi).AddSingleton(player).AddAdminUserResources().BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null); var previousContent = window.Content;
        var frame = new Frame(); _statusFrame = frame;
        field.SetValue(null, services); window.Content = frame; navigation.Frame = frame;
        var context = new Context { Window = window, Frame = frame, Server = server, Authority = authority };
        try
        {
            try { _ = AdminUserResourceText.T("common.save"); }
            catch (Exception error) { Program.Log("PROBE: " + (error.InnerException ?? error)); throw; }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            Check(!services.GetServices<AdminPageRegistration>().Any(x => x.Route == AdminRoute.Users), "family does not register or reimplement the Users page");
            Check(new[] { AdminRoute.AccessGroups, AdminRoute.Devices, AdminRoute.HistoryImport }.All(route => services.GetServices<AdminPageRegistration>().Any(x => x.Route == route)), "all three routes are registered");
            await AccessGroupsAsync(context);
            await DevicesAsync(context);
            await RefreshAsync(context);
            await ReviewFixAsync(context);
            await HistoryImportAsync(context);
            if (Failures.Count > 0) throw new InvalidOperationException(Failures.Count + " scenario(s) failed: " + string.Join("; ", Failures));
            Program.Log("INFO: dialog-opening button presses: " + DialogClicks + "; dialogs opened by them: " + DialogsOpened + " (one press each)");
            Program.Log("PASS: Admin user resources native fixture complete (synthetic data, current DPI only).");
        }
        finally
        {
            try
            {
                if (Dialog(frame) is { } open) open.Hide();
                var owned = All(frame).OfType<Page>().Where(page => page is IAdminPageSuspension).ToArray();
                foreach (var page in owned.OfType<IAdminPageSuspension>()) page.SuspendAdminPage();
                frame.Navigate(typeof(Page));
                await Until(() => owned.All(page => !page.IsLoaded) && Dialog(frame) == null, "family pages and dialogs fully departed");
            }
            finally { window.Content = previousContent; field.SetValue(null, previous); }
        }
    }

    private static async Task ScenarioAsync(Context context, string name, Func<Task> body)
    {
        Program.Log("SCENARIO: " + name);
        try { await body(); }
        catch (Exception error) { Failures.Add(name); Program.Log("FAIL: scenario " + name + ": " + error.Message); }
        finally
        {
            try
            {
                if (Dialog(context.Frame) is { } open) { open.Hide(); await Until(() => Dialog(context.Frame) == null, name + " dialog closed"); }
                context.Server.Scripts.Clear();
            }
            catch (Exception error) { Program.Log("WARN: " + name + " cleanup: " + error.Message); }
        }
    }
}
