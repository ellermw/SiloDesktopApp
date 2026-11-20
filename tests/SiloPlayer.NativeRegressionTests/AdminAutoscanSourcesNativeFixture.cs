using System.Reflection;
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
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Authored source-only. Real AddAdminAutoscanSources → Admin shell → nested Frame and controls.
/// Synthetic server only. Dispatch SILO_NATIVE_TEST_ONLY=admin-autoscan-sources.</summary>
internal static partial class AdminAutoscanSourcesNativeFixture
{
    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var monitor = new AdminMonitorApi(client); var realtime = new Realtime(); using var store = new ServerActivityStore(monitor, authority, realtime);
        var navigation = new NavigationService();
        await using var provider = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitor).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new MediaMaintenanceApi(client)).AddAdminAutoscanSources().BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousServices = servicesField.GetValue(null); var previousContent = window.Content;
        AdminAutoscanSourcesPage? page = null; Frame? outer = null;
        servicesField.SetValue(null, provider);
        try
        {
            window.AppWindow.ResizeClient(new(1440, 900)); outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.CanOpen(new(AdminRoute.AutoscanSources)), "production registration exposes Sources");
            Check(!AdminShellPage.CanOpen(new(AdminRoute.AutoscanActivity)) && !AdminShellPage.CanOpen(new(AdminRoute.AutoscanSettings)), "Sources registration does not expose queued Activity or Settings");
            Check(AdminShellPage.Open(new(AdminRoute.AutoscanSources)), "Sources enters the real Admin shell");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminAutoscanSourcesPage { Controller.State.Loaded: true, Controller.State.Loading: false });
            page = (AdminAutoscanSourcesPage)inner.Content;
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Sources navigation has no slide transition");
            Check(Find<TextBlock>(shell, "AutoscanFeedback") != null && Texts(shell).Contains("Autoscan"), "Sources title and feedback are rendered");
            Check(Find<Button>(shell, "AutoscanRunNow")?.Visibility == Visibility.Visible, "Run now is visible for an enabled polling source");
            await Capture(shell, "autoscan-sources-wide.png");
            var toggle = Need<ToggleSwitch>(shell, "AutoscanEnabled"); toggle.IsOn = false;
            await Settled(page); Check(!server.Enabled && server.DefaultInterval == 600 && server.Debounce == 60, "global enable preserves both polling defaults");
            Check(Need<Button>(shell, "AutoscanRunNow").Visibility == Visibility.Collapsed, "Run now hides when Autoscan is off");
            Need<ToggleSwitch>(shell, "AutoscanEnabled").IsOn = true; await Settled(page);
            await Invoke(Need<Button>(shell, "AutoscanRunNow")); await Settled(page);
            Check(page.Controller!.State.Notice?.Contains("process") == true && page.Controller.State.Notice?.Contains("completed") != true, "Run now reports a process-level start, never scan completion");
            // A poll already running is a known state (409), not an unknown outcome: writes stay open.
            server.TriggerConflict = true; await Invoke(Need<Button>(shell, "AutoscanRunNow")); await Settled(page);
            Check(page.Controller.State.Error?.Contains("already running") == true && !page.Controller.State.UnknownOutcome && Need<Button>(shell, "AutoscanRunNow").IsEnabled, "Run now answered with a conflict says a poll is already running and stays usable");
            server.TriggerConflict = false;

            // Full polling create: descriptor, inline manual connection, test, mappings and source receipt.
            await Invoke(Need<Button>(shell, "AutoscanAdd"));
            await Invoke(Need<Button>(shell, "AutoscanPlugin_fixture.poll_poll"));
            var connections = Need<ComboBox>(shell, "AutoscanConnection"); connections.SelectedItem = connections.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "__add__");
            await Until(() => Find<TextBox>(shell, "AutoscanConnectionName")?.IsLoaded == true);
            Need<TextBox>(shell, "AutoscanConnectionName").Text = "Fixture Radarr";
            Need<TextBox>(shell, "AutoscanConnectionUrl").Text = "https://radarr.fixture.invalid";
            Need<PasswordBox>(shell, "AutoscanConnectionKey").Password = "synthetic-write-only-key";
            await Invoke(Need<Button>(shell, "AutoscanConnectionTest"));
            await Until(() => Need<TextBlock>(shell, "AutoscanConnectionTestResult").Text == "Connected (vsynthetic)");
            await Invoke(Need<Button>(shell, "AutoscanConnectionCreate")); await Settled(page);
            Check(server.ConnectionWrites == 1 && server.LastConnectionBody?.Contains("api_key_ref") == true, "inline server creation uses the write-only credential contract once");
            Need<ToggleSwitch>(shell, "ProviderField_flag").IsOn = true;
            await Invoke(Need<Button>(shell, "AutoscanFill_roots"));
            Check(Need<ToggleSwitch>(shell, "ProviderField_flag").IsOn && Need<TextBox>(shell, "ProviderField_roots").Text == "/media/movies", "schema controls retain typed values while fill-from uses enabled library paths");
            Need<TextBox>(shell, "AutoscanInterval").Text = "42"; Need<TextBox>(shell, "AutoscanLabel").Text = "New poll source";
            await Invoke(Need<Button>(shell, "AutoscanMappingAdd")); Need<TextBox>(shell, "AutoscanMappingFrom_0").Text = "/remote/movies"; Need<TextBox>(shell, "AutoscanMappingTo_0").Text = "/media/movies";
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null); await Settled(page);
            var poll = server.Sources.Single(s => s.Label == "New poll source"); Check(poll.Enabled && poll.PollIntervalSeconds == 42 && poll.ConnectionId == "connection-new", "created poll source is enabled with explicit interval and bound server");
            Check(poll.SourceConfig.GetValueOrDefault("flag") == "true" && poll.SourceConfig.GetValueOrDefault("roots") == "/media/movies", "source creation serializes typed descriptor values once");
            await Invoke(Need<Button>(shell, "AutoscanEdit_" + poll.Id));
            await Invoke(Need<Button>(shell, "AutoscanTab_paths")); await Invoke(Need<Button>(shell, "AutoscanMappingsSync"));
            await Until(() => Find<Button>(shell, "AutoscanMappingsApply") != null); await Invoke(Need<Button>(shell, "AutoscanMappingsApply"));
            await Invoke(Need<Button>(shell, "AutoscanTab_general")); Need<TextBox>(shell, "AutoscanLabel").Text = "Edited poll source";
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null); await Settled(page);
            Check(server.Sources.Single(s => s.Id == poll.Id).PathRewrites.Count == 2, "selected mapping preview is saved with the complete source");

            // Webhook creation remains mounted through the empty/nonempty list transition.
            var savedPollSources = server.Sources.ToArray(); server.Sources.Clear(); await page.Controller!.ReloadAsync(); await Settled(page);
            Check(Texts(shell).Any(t => t.StartsWith("No scan sources yet.")), "empty list offers the real Add source workflow");
            await Invoke(Need<Button>(shell, "AutoscanAdd")); await Invoke(Need<Button>(shell, "AutoscanPlugin_fixture.webhook_arr"));
            Check(Find<ComboBox>(shell, "AutoscanConnection") == null, "webhook source does not ask for a connection");
            Need<TextBox>(shell, "AutoscanMappingFrom_0").Text = "/arr/movies";
            Need<TextBox>(shell, "AutoscanLabel").Text = "Webhook fixture";
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Settled(page);
            await Until(() => Find<Button>(shell, "AutoscanWebhookCopy") is { IsEnabled: true });
            Check(Find<Grid>(shell, "AutoscanEditor") != null && Texts(shell).Contains("Almost done — connect your service"), "created webhook remains in Connect it flow");
            Check(Need<PasswordBox>(shell, "AutoscanWebhookUrl").PasswordRevealMode == PasswordRevealMode.Hidden, "webhook URL stays masked");
            Check(Texts(shell).Contains("On File Import") && Texts(shell).Contains("On File Upgrade"), "webhook setup shows supported service triggers");
            await Capture(Need<Grid>(shell, "AutoscanEditor"), "autoscan-webhook-connect.png");
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null);
            var webhook = server.Sources.Single(s => s.Label == "Webhook fixture");
            await Invoke(Need<Button>(shell, "AutoscanEdit_" + webhook.Id));
            var rotations = server.Rotations; await Invoke(Need<Button>(shell, "AutoscanWebhookRotate")); await Until(() => Dialog(window) != null);
            Check(server.Rotations == rotations, "rotation waits for destructive confirmation");
            await Invoke(Need<Button>(Dialog(window)!, "PrimaryButton")); await Until(() => Dialog(window) == null); await Until(() => server.Rotations == rotations + 1); await Settled(page);
            Check(server.Rotations == rotations + 1, "rotation sends exactly one request");
            await OpenDialog(window, Need<Button>(shell, "AutoscanEditorDelete"));
            await Invoke(Need<Button>(Dialog(window)!, "CloseButton")); await Until(() => Dialog(window) == null);
            Check(server.Sources.Any(s => s.Id == webhook.Id), "cancelled deletion preserves the source");
            await OpenDialog(window, Need<Button>(shell, "AutoscanEditorDelete")); await Invoke(Need<Button>(Dialog(window)!, "PrimaryButton"));
            await Until(() => Dialog(window) == null && Find<Grid>(shell, "AutoscanEditor") == null); await Settled(page);
            Check(server.Sources.All(s => s.Id != webhook.Id), "confirmed delete removes only the selected source");
            // A webhook source whose endpoint generation fails stays in the Connect flow with a retry; the create is not repeated.
            var createsBefore = server.Requests.Count(r => r == "POST /api/v2/admin/autoscan/sources");
            server.FailWebhookCreates = 1;
            await Invoke(Need<Button>(shell, "AutoscanAdd")); await Invoke(Need<Button>(shell, "AutoscanPlugin_fixture.webhook_arr"));
            Need<TextBox>(shell, "AutoscanMappingFrom_0").Text = "/arr/movies"; Need<TextBox>(shell, "AutoscanLabel").Text = "Webhook retry";
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Settled(page);
            await Until(() => Find<Button>(shell, "AutoscanWebhookGenerate") is { Visibility: Visibility.Visible, IsEnabled: true });
            Check(Texts(shell).Contains("Almost done — connect your service") && Need<TextBlock>(shell, "AutoscanEditorError").Text.Length > 0 && Need<Button>(shell, "AutoscanWebhookCopy").IsEnabled == false, "failed endpoint generation is reported and offers a retry");
            await Invoke(Need<Button>(shell, "AutoscanWebhookGenerate")); await Settled(page);
            await Until(() => Find<Button>(shell, "AutoscanWebhookCopy") is { IsEnabled: true });
            Check(server.Requests.Count(r => r == "POST /api/v2/admin/autoscan/sources") == createsBefore + 1, "retrying endpoint generation never repeats the source creation");
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null);
            server.Sources.Clear();
            server.Sources.AddRange(savedPollSources); await page.Controller!.ReloadAsync(); await Settled(page);

            // A source write the server refuses (422): the editor stays open with the reason and the typed draft, and sends again once corrected.
            await Invoke(Need<Button>(shell, "AutoscanEdit_original")); await Invoke(Need<Button>(shell, "AutoscanTab_general"));
            Need<TextBox>(shell, "AutoscanLabel").Text = "Refused then corrected"; server.RefuseSourceWrites = 1;
            var putsBefore = server.Requests.Count(r => r.StartsWith("PUT /api/v2/admin/autoscan/sources/original"));
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Settled(page);
            Check(Find<Grid>(shell, "AutoscanEditor") != null && Need<TextBlock>(shell, "AutoscanEditorError").Text.Contains("Invalid source configuration") && Need<TextBox>(shell, "AutoscanLabel").Text == "Refused then corrected", "a refused source write shows the server reason and keeps the draft editable");
            Check(Need<Button>(shell, "AutoscanEditorSave").IsEnabled && !page.Controller!.State.UnknownOutcome && server.Sources.Single(s => s.Id == "original").Label == "Original source", "a refusal changes nothing and does not lock writes");
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null); await Settled(page);
            Check(server.Sources.Single(s => s.Id == "original").Label == "Refused then corrected" && server.Requests.Count(r => r.StartsWith("PUT /api/v2/admin/autoscan/sources/original")) == putsBefore + 2, "the corrected source is sent once more, not replayed");

            // An unknown outcome (transport loss after sending): the editor locks, the canonical state is re-read, and nothing is replayed.
            await Invoke(Need<Button>(shell, "AutoscanEdit_original")); await Invoke(Need<Button>(shell, "AutoscanTab_general"));
            Need<TextBox>(shell, "AutoscanLabel").Text = "Lost in transit"; server.DropSourceResponses = 1;
            putsBefore = server.Requests.Count(r => r.StartsWith("PUT /api/v2/admin/autoscan/sources/original"));
            await Invoke(Need<Button>(shell, "AutoscanEditorSave")); await Settled(page);
            Check(Need<TextBlock>(shell, "AutoscanEditorError").Text.Contains("could not be confirmed") && !Need<Button>(shell, "AutoscanEditorSave").IsEnabled, "an unknown write outcome is reported and locks the editor");
            Check(server.Requests.Count(r => r.StartsWith("PUT /api/v2/admin/autoscan/sources/original")) == putsBefore + 1 && server.Sources.Single(s => s.Id == "original").Label == "Refused then corrected", "an unknown write is sent once and never replayed");
            await Invoke(Need<Button>(shell, "AutoscanEditorCancel")); await Until(() => Find<Grid>(shell, "AutoscanEditor") == null); await Settled(page);
            Check(!page.Controller!.State.UnknownOutcome && !page.Controller.State.Stale, "the canonical state was read again after the unknown outcome");

            // A row switch whose write is lost follows the saved value again once the state is re-read.
            server.DropSourceResponses = 1; Need<ToggleSwitch>(shell, "AutoscanOn_original").IsOn = false; await Settled(page);
            await Until(() => Find<ToggleSwitch>(shell, "AutoscanOn_original") is { IsOn: true, IsEnabled: true });
            Check(server.Sources.Single(s => s.Id == "original").Enabled && page.Controller.State.Error?.Contains("could not be confirmed") == true, "a lost enable switch write reports the unknown outcome and the switch follows the saved value");

            server.FailSources = true; await page.Controller!.ReloadAsync(); await Until(() => Find<Button>(shell, "AutoscanRetry")?.Visibility == Visibility.Visible);
            Check(Find<FrameworkElement>(shell, "AutoscanSource_original") != null && !Need<Button>(shell, "AutoscanEdit_original").IsEnabled, "failed refresh retains stale rows and disables replacement writes");
            server.FailSources = false; await Invoke(Need<Button>(shell, "AutoscanRetry")); await Settled(page);
            window.AppWindow.ResizeClient(new(800, 600)); await Task.Delay(150); await Capture(shell, "autoscan-sources-compact.png");
            await Invoke(Need<Button>(shell, "AutoscanAdd")); await Invoke(Need<Button>(shell, "AutoscanPlugin_fixture.poll_poll"));
            Need<TextBox>(shell, "AutoscanLabel").Text = "Unsaved fixture";
            var leave = page.CanLeaveAsync(); await Until(() => Dialog(window) != null); await Invoke(Need<Button>(Dialog(window)!, "CloseButton"));
            Check(!await leave && Need<TextBox>(shell, "AutoscanLabel").Text == "Unsaved fixture", "cancelled navigation preserves the editor draft"); await Until(() => Dialog(window) == null);
            leave = page.CanLeaveAsync(); await Until(() => Dialog(window) != null); await Invoke(Need<Button>(Dialog(window)!, "PrimaryButton"));
            Check(await leave, "confirmed navigation discards the editor"); await Until(() => Dialog(window) == null && Find<Grid>(shell, "AutoscanEditor") == null);
            await Invoke(Need<Button>(shell, "AutoscanAdd")); await Invoke(Need<Button>(shell, "AutoscanPlugin_fixture.poll_poll"));
            auth.SelectProfile("secondary", null, new Profile { Id = "secondary", IsPrimary = false });
            await Until(() => page.Controller?.State.Owner == null && Find<Grid>(shell, "AutoscanEditor") == null && Dialog(window) == null);
            Check(page.Controller?.State.Sources.Count == 0, "authority retirement closes the editor and clears private source data");
            Check(!server.Requests.Any(r => r.Contains("/scan/all") || r.Contains("/items") || r.Contains("/api/v1/")), "Sources never sends whole-library scans, catalog walks or legacy endpoints");
            Program.Log("PASS: Autoscan Sources native fixture completed; synthetic server, current DPI only.");
        }
        finally
        {
            page?.SuspendAdminPage();
            // Frame navigation runs the shell's real departure cleanup before
            // the service-provider global can be restored.
            if (outer?.Content is AdminShellPage) outer.Navigate(typeof(Page));
            else if (outer != null) outer.Content = null;
            await Until(() => page?.IsLoaded != true && Dialog(window) == null);
            window.Content = previousContent;
            servicesField.SetValue(null, previousServices);
        }
    }
    private static void Check(bool condition, string label)
    { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) throw new InvalidOperationException(label); }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25); if (!condition()) throw new TimeoutException("Autoscan fixture prerequisite did not settle."); }
    // A confirmation that is still finishing closing ignores a new request; invoke again until the next one opens.
    private static async Task OpenDialog(Window window, ButtonBase opener)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await Invoke(opener);
            for (var i = 0; i < 40 && Dialog(window) == null; i++) await Task.Delay(25);
            if (Dialog(window) != null) return;
        }
        throw new TimeoutException("The confirmation dialog did not open.");
    }
    private static async Task Settled(AdminAutoscanSourcesPage page)
    { await Until(() => page.Controller is { State.Loading: false, State.Busy: false }); await Task.Delay(60); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    { yield return node; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child; }
    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id || e.Name == id);
    private static T Need<T>(DependencyObject root, string id) where T : FrameworkElement => Find<T>(root, id) ?? throw new InvalidOperationException("Missing control: " + id);
    private static HashSet<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToHashSet();
    private static ContentDialog? Dialog(Window window) => window.Content?.XamlRoot is { } root ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Where(p => p.Child != null).SelectMany(p => Descendants(p.Child!)).OfType<ContentDialog>().FirstOrDefault() : null;
    private static async Task Invoke(ButtonBase button)
    {
        button.StartBringIntoView(); await Task.Delay(60);
        Check(button.IsLoaded && button.IsEnabled, AutomationProperties.GetAutomationId(button) + " is loaded and enabled");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke) throw new InvalidOperationException("Missing native invocation provider.");
        invoke.Invoke(); await Task.Delay(60);
    }
    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); await Task.Delay(100); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " renders pixels");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
