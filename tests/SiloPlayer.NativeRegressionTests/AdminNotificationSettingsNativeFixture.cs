using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
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

/// <summary>Authored source-only. Runs the production registration, shell, Frame, controls and dialogs.</summary>
internal static class AdminNotificationSettingsNativeFixture
{
    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            ["notifications.ui_enabled"] = "true", ["discord.client_id"] = "synthetic-app",
            ["notifications.apple_push_delivery_enabled"] = "true", ["notifications.android_push_delivery_enabled"] = "true",
            ["notifications.discord_enabled"] = "false", ["notifications.email.digest_hour"] = "8",
        };
        internal int Writes, Version = 1;
        internal bool Conflict, FailChannels;
        internal TaskCompletionSource? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (Gate is { } gate) await gate.Task.WaitAsync(ct);
                if (request.Method == HttpMethod.Put && path.EndsWith("/settings"))
                {
                    if (Conflict) return Json(new { status = 412, title = "Changed", type = "precondition_failed" }, HttpStatusCode.PreconditionFailed);
                    using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    foreach (var p in doc.RootElement.GetProperty("values").EnumerateObject()) Values[p.Name] = p.Value.GetString()!;
                    Version++; body = new { values = Values, restart_required = false };
                }
                else if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
                else if (path.EndsWith("/test")) body = new { ok = false, duration_ms = 9, http_status = 503, message = "Synthetic delivery rejected" };
                else if (path.EndsWith("/register")) body = new { relay_url = "https://relay.example.test", api_key_configured = true, deployment_id = "fixture", key_prefix = "safe-prefix", expires_at = "2027-01-01T00:00:00Z" };
                else body = new { id = "created", name = "Synthetic channel", type = "generic", url_host = "example.test", signing_secret = "synthetic-one-time" };
            }
            else if (path.EndsWith("/server-channels"))
            {
                if (FailChannels) return Json(new { status = 503 }, HttpStatusCode.ServiceUnavailable);
                body = new { items = new[] { new { id = "c1", name = "Synthetic community", type = "generic", url_host = "example.test", enabled = true,
                    notify_new_movies = true, notify_new_episodes = true, notify_new_audiobooks = true, notify_new_ebooks = true,
                    notify_request_submitted = false, notify_request_approved = false, notify_request_declined = false, notify_request_fulfilled = false,
                    consecutive_failures = 1, last_failure_at = "2026-11-17T12:00:00Z", last_failure_status = 503 } }, page = new { has_more = false } };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => new { configured = new[] { "discord.bot_token", "discord.client_secret" }, managed_by_env = Array.Empty<string>() },
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "notifications.ui_enabled" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(body);
        }
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"notifications-{Version}\""); return response;
        }
    }
    internal static async Task RunAsync(Window window)
    {
        var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var nav = new NavigationService(); var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(nav).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!
            .GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null); var previousContent = window.Content; Frame? outer = null; AdminNotificationSettingsPage? page = null;
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame(); nav.Frame = outer; window.Content = outer;
            Check(AdminShellPage.TryOpenHref("/admin/settings/notifications") is { Kind: AdminRouteResolutionKind.Route }, "Notifications route registered");
            await Until(() => outer.Content is AdminShellPage, "shell"); var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminNotificationSettingsPage { Controller.State.Loaded: true }, "production page loaded");
            page = (AdminNotificationSettingsPage)inner.Content; var controller = page.Controller!;
            await Until(() => Desc(page).OfType<ToggleSwitch>().Any(), "fields rendered");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "navigation has no slide");
            Check(Desc(page).OfType<FrameworkElement>().Count(e => AutomationProperties.GetAutomationId(e).StartsWith("NotificationChannel_")) == 7, "seven delivery channels");
            Check(!Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal)), "official catalog labels resolved");
            await Capture(outer, "notifications-channels.png");
            await Expand(page, "NotificationChannel_relay"); await Expand(page, "NotificationsRelayAdvanced");
            await Until(() => Find<TextBox>(page, "NotificationsRelayUrl").IsLoaded, "relay editor");
            Find<TextBox>(page, "NotificationsRelayUrl").Text = "https://relay.example.test";
            Invoke(Find<Button>(page, "NotificationsRelayRegister")); await Until(() => !controller.State.OperationPending && server.Writes == 1, "relay registration");
            await Capture(outer, "notifications-relay-wide.png");
            await Expand(page, "NotificationChannel_discord");
            var secret = Find<PasswordBox>(page, "Notifications_discord.bot_token"); Check(secret.Password == "", "saved secrets never loaded");
            secret.Password = "synthetic-new"; await Until(() => controller.State.DirtyKeys.Contains("discord.bot_token") && !Find<Button>(page, "NotificationsDiscordTest").IsEnabled, "secret staged");
            Check(!Find<Button>(page, "NotificationsDiscordTest").IsEnabled, "unsaved Discord credentials cannot be tested");
            Invoke(Find<Button>(page, "NotificationsDiscordSave")); await Until(() => !controller.State.Saving && !controller.State.DirtyKeys.Contains("discord.bot_token"), "Discord saved");
            await Until(() => Find<Button>(page, "NotificationsDiscordTest").IsEnabled && secret.Password.Length == 0, "saved Discord controls rendered");
            Invoke(Find<Button>(page, "NotificationsDiscordTest")); await Until(() => controller.State.TestResult != null, "Discord failure result");
            Check(controller.State.TestResult?.Ok == false, "delivery rejection surfaced");
            await Expand(page, "NotificationChannel_server"); await Until(() => Desc(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NotificationsChannelEdit_c1"), "server channel card");
            Find<Button>(page, "NotificationsChannelAdd").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
            await Capture(outer, "notifications-server-channels-wide.png");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "channel editor dialog");
            Check(Desc(Dialog(page)!).OfType<ToggleSwitch>().Count() == 8, "all eight event controls");
            await Capture(Dialog(page)!, "notifications-channel-edit.png"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "editor closed");
            Invoke(Find<Button>(page, "NotificationsChannelDelete_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "delete confirmation");
            Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "delete cancelled");
            Invoke(Find<Button>(page, "NotificationsChannelAdd")); await Until(() => Dialog(page) is { IsLoaded: true }, "new channel dialog");
            var createDialog = Dialog(page)!;
            var editorUnloaded = false;
            createDialog.Unloaded += (_, _) => editorUnloaded = true;
            Find<TextBox>(createDialog, "NotificationsChannelName").Text = "Synthetic channel";
            Find<TextBox>(createDialog, "NotificationsChannelUrl").Text = "https://example.test/synthetic-webhook";
            await Until(() => createDialog.IsPrimaryButtonEnabled, "create enabled");
            Invoke(Desc(createDialog).OfType<Button>().First(b => b.Content?.ToString() == createDialog.PrimaryButtonText));
            await Until(() => Dialog(page) is { IsLoaded: true, Title: "Save your signing secret" }, "one-time signing secret");
            var secretDialog = Dialog(page)!; var oneTime = Desc(secretDialog).OfType<TextBox>().Single();
            Check(editorUnloaded && !createDialog.IsLoaded, "editor actually unloads before the one-time signing-secret dialog opens");
            Check(oneTime.IsReadOnly && oneTime.Text == "synthetic-one-time", "newly minted secret shown once in its own dialog");
            secretDialog.Hide(); await Until(() => Dialog(page) == null && oneTime.Text.Length == 0, "one-time secret cleared on close");
            controller.Set("notifications.ui_enabled", "false"); server.Conflict = true; await controller.SaveAsync();
            await Until(() => controller.State.Conflict && !Find<Button>(page, "NotificationsSave").IsEnabled, "conflict disabled Save");
            server.Conflict = false; await controller.ReloadAsync(true); Check(controller.State.DirtyKeys.Contains("notifications.ui_enabled"), "reload retains draft for review");
            server.FailChannels = true; await controller.ReloadAsync(true); await Until(() => controller.State.ChannelsError != null, "channels failure");
            Check(Find<Button>(page, "NotificationsChannelRetry").IsEnabled, "channel read failure has explicit retry");
            server.FailChannels = false; await controller.ReloadAsync(true); controller.Discard();
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(720, 900));
            Desc(page).OfType<ScrollViewer>().Single(scroll => AutomationProperties.GetName(scroll) == "Notifications").ChangeView(null, 0, null, true);
            await Capture(outer, "notifications-narrow.png");
            Find<Button>(page, "NotificationsChannelAdd").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
            await Capture(outer, "notifications-server-channels-narrow.png");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "editor before host hiding");
            await Capture(Dialog(page)!, "notifications-channel-edit-narrow.png");
            var hiddenDialog = Dialog(page)!;
            var hiddenUrl = Find<TextBox>(hiddenDialog, "NotificationsChannelUrl"); hiddenUrl.Text = "https://example.test/synthetic-hidden";
            secret.Password = "synthetic-hidden";
            var hiddenOwner = controller.State.Owner!;
            server.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var hiddenOperation = controller.TestChannelAsync("c1");
            await Until(() => controller.State.OperationPending, "held operation before host hiding");
            window.AppWindow.Hide();
            try
            {
                await Until(() => !hiddenOwner.IsCurrent && controller.State.Values.Count == 0 && secret.Password.Length == 0, "hidden host retires authority and clears credential state without unloading page");
                Check(page.IsLoaded && !page.XamlRoot.IsHostVisible, "hidden page remains mounted");
                await Until(() => Dialog(page) == null && !hiddenDialog.IsLoaded && hiddenUrl.Text.Length == 0, "hidden host clears and unloads the private dialog");
                server.Gate.TrySetResult(); await hiddenOperation;
                Check(controller.State.TestResult == null && controller.State.Channels.Count == 0, "held results cannot revive hidden page state");
            }
            finally
            {
                server.Gate?.TrySetResult(); server.Gate = null;
                window.AppWindow.Show(); window.Activate();
            }
            await Until(() => page.Controller?.State is { Loaded: true, ChannelsLoaded: true, Owner.IsCurrent: true }, "visible host reopens with fresh authority");
            controller = page.Controller!;
            Check(!ReferenceEquals(hiddenOwner, controller.State.Owner), "visibility recovery does not reuse retired authority");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "editor before retirement");
            var privateUrl = Find<TextBox>(Dialog(page)!, "NotificationsChannelUrl"); privateUrl.Text = "https://example.test/synthetic-retirement";
            server.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously); var pending = controller.TestChannelAsync("c1");
            await Until(() => controller.State.OperationPending, "pending test"); secret.Password = "synthetic-retirement";
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" }); server.Gate.SetResult(); await pending;
            await Until(() => controller.State.Values.Count == 0 && secret.Password.Length == 0, "retirement clears deferred secret and state");
            await Until(() => Dialog(page) == null && privateUrl.Text.Length == 0, "retirement closes dialog and clears destination draft");
            Check(controller.State.TestResult == null && controller.State.Channels.Count == 0, "late results discarded");
        }
        finally
        {
            var closingDialog = page == null ? null : Dialog(page);
            page?.SuspendAdminPage();
            await Until(() => (page == null || Dialog(page) == null) && (closingDialog == null || !closingDialog.IsLoaded), "dialogs closed and unloaded before globals restore");
            if (outer != null) { outer.Content = null; outer.BackStack.Clear(); outer.ForwardStack.Clear(); }
            await Until(() => page == null || !page.IsLoaded, "page unloaded before globals restore");
            window.Content = previousContent; field.SetValue(null, previous);
        }
    }
    private static ContentDialog? Dialog(AdminNotificationSettingsPage page) => (ContentDialog?)typeof(AdminNotificationSettingsPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void Check(bool ok, string label) { Program.Log((ok ? "PASS: " : "FAIL: ") + label); if (!ok) throw new InvalidOperationException(label); }
    private static async Task Until(Func<bool> condition, string label) { for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25); if (!condition()) throw new InvalidOperationException("Timed out: " + label); }
    private static IEnumerable<DependencyObject> Desc(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var c in Desc(VisualTreeHelper.GetChild(root, i))) yield return c; }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Desc(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static async Task Expand(DependencyObject page, string id)
    {
        await Until(() => Desc(page).OfType<Expander>().Any(e => AutomationProperties.GetAutomationId(e) == id && e.IsLoaded), id + " realized");
        Find<Expander>(page, id).IsExpanded = true; await Task.Delay(100);
    }
    private static void Invoke(Button b) { Check(b.IsEnabled, AutomationProperties.GetAutomationId(b) + " enabled"); ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke(); }
    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); await Task.Delay(350); root.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
