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
            // An older server still answers with the seven Processing settings that official 4a1ee058e removed; the page must neither show nor send them.
            ["notifications.release_events_enabled"] = "false", ["notifications.fanout_enabled"] = "false", ["notifications.fanout.settle_seconds"] = "45",
            ["notifications.fanout.max_event_age_hours"] = "12", ["notifications.retention.read_days"] = "7", ["notifications.retention.unread_days"] = "8", ["notifications.retention.event_days"] = "9",
        };
        internal int Writes, Version = 1;
        internal bool Conflict, FailChannels;
        internal string[] ManagedKeys = [];
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal TaskCompletionSource? Gate;
        internal readonly List<string> Writing = [], Bodies = [];
        internal int DeleteStatus = 204;
        internal bool NoChannels, FailSettings;
        internal int PutStatus;
        internal string[] Managed = [];
        internal string? RenameC1;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body;
            if (request.Method != HttpMethod.Get)
            {
                Writes++; Writing.Add(request.Method + " " + path); Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
                if (Gate is { } gate) await gate.Task.WaitAsync(ct);
                if (request.Method == HttpMethod.Put && path.EndsWith("/settings"))
                {
                    if (Conflict) return Json(new { status = 412, title = "Changed", type = "precondition_failed" }, HttpStatusCode.PreconditionFailed);
                    if (PutStatus != 0) return Json(new { status = PutStatus, title = "Unavailable" }, (HttpStatusCode)PutStatus);
                    using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    foreach (var p in doc.RootElement.GetProperty("values").EnumerateObject()) Values[p.Name] = p.Value.GetString()!;
                    Version++; body = new { values = Values, restart_required = false };
                }
                else if (request.Method == HttpMethod.Delete) return new((HttpStatusCode)DeleteStatus);
                else if (path.EndsWith("/test")) body = new { ok = false, duration_ms = 9, http_status = 503, message = "Synthetic delivery rejected" };
                else if (path.EndsWith("/register"))
                {
                    Values["notifications.push_relay_deployment_id"] = "fixture";
                    body = new { relay_url = "https://relay.example.test", api_key_configured = true, deployment_id = "fixture", key_prefix = "safe-prefix", expires_at = "2027-01-01T00:00:00Z" };
                }
                else if (request.Method == HttpMethod.Put) body = new { id = path.Split('/').Last(), name = "Synthetic", type = "generic", url_host = "example.test" };
                else body = new { id = "created", name = "Synthetic channel", type = "generic", url_host = "example.test", signing_secret = "synthetic-one-time" };
            }
            else if (path.EndsWith("/server-channels"))
            {
                if (FailChannels) return Json(new { status = 503 }, HttpStatusCode.ServiceUnavailable);
                var rows = new List<object>();
                if (!NoChannels)
                {
                    rows.Add(new { id = "c1", name = RenameC1 ?? "Synthetic community", type = "generic", url_host = "example.test", enabled = true,
                        notify_new_movies = true, notify_new_episodes = true, notify_new_audiobooks = true, notify_new_ebooks = true,
                        notify_request_submitted = false, notify_request_approved = false, notify_request_declined = false, notify_request_fulfilled = false,
                        consecutive_failures = 1, last_failure_at = "2026-11-17T12:00:00Z", last_failure_status = 503, last_failure_message = "Synthetic\u202E rejected" });
                    // Third-party text: a bidirectional override and a line separator inside the name.
                    rows.Add(new { id = "c2", name = "Gate\u202Egnp.exe\u2028line", type = "discord", url_host = "discord.example.test\u202E", enabled = true,
                        notify_new_movies = true, notify_new_episodes = false, notify_new_audiobooks = false, notify_new_ebooks = false,
                        notify_request_submitted = false, notify_request_approved = false, notify_request_declined = false, notify_request_fulfilled = false,
                        consecutive_failures = 0, last_success_at = "2026-11-17T12:00:00Z" });
                }
                body = new { items = rows, page = new { has_more = false } };
            }
            else if (Managed.Length > 0 && path.EndsWith("/settings/sensitive-status")) return Json(new { configured = new[] { "discord.bot_token", "discord.client_secret" }, managed_by_env = Managed });
            else if (FailSettings && path.EndsWith("/settings/effective")) return Json(new { status = 503 }, HttpStatusCode.ServiceUnavailable);
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/sensitive-status" => ManagedKeys.Length > 0 && NamedVariables
                    ? new { configured = new[] { "discord.bot_token", "discord.client_secret" }, managed_by_env = ManagedKeys, managed_by_env_vars = ManagedKeys.ToDictionary(key => key, key => "SILO_" + key.ToUpperInvariant().Replace('.', '_')) }
                    : (object)new { configured = new[] { "discord.bot_token", "discord.client_secret" }, managed_by_env = ManagedKeys },
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
        var toastService = new ToastService(); var toasts = new SiloPlayer.Controls.ToastContainer();
        var nav = new NavigationService(); var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(nav).AddSingleton(toastService).AddSingleton(new SettingsService(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!
            .GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null); var previousContent = window.Content; Frame? outer = null; AdminNotificationSettingsPage? page = null;
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame(); nav.Frame = outer;
            // The toasts the page shows are read from a real host, so a message that must not appear can be checked as absent.
            var host = new Grid(); host.Children.Add(outer); host.Children.Add(toasts); window.Content = host; toastService.Register(toasts, outer.DispatcherQueue);
            Check(AdminShellPage.TryOpenHref("/admin/settings/notifications") is { Kind: AdminRouteResolutionKind.Route }, "Notifications route registered");
            await Until(() => outer.Content is AdminShellPage, "shell"); var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminNotificationSettingsPage { Controller.State.Loaded: true }, "production page loaded");
            page = (AdminNotificationSettingsPage)inner.Content; var controller = page.Controller!;
            await Until(() => Desc(page).OfType<ToggleSwitch>().Any(), "fields rendered");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "navigation has no slide");
            Check(Desc(page).OfType<FrameworkElement>().Count(e => AutomationProperties.GetAutomationId(e).StartsWith("NotificationChannel_")) == 7, "seven delivery channels");
            Check(!Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal)), "official catalog labels resolved");
            // f42b93271: the Processing section, its retired settings and the official paused banner (it pointed at that section) are gone from the page.
            Check(!Desc(page).OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) is { } id && (id.Contains("Processing") || id.Contains("fanout") || id.Contains("retention") || id.Contains("release_events"))), "no Processing section or retired setting control");
            Check(!Desc(page).OfType<TextBlock>().Any(t => t.Text is "Processing" or "Pipeline" or "Retention" or "Notice new content" or "Settle window" || t.Text.Contains("under Processing")), "no Processing text and no official paused banner");
            // This server answers like an older one with both pipeline switches off. The app is updated separately from the server, so a paused
            // pipeline is still said: read-only, without pointing at the removed section, and with no control for the retired switches.
            await Until(() => Desc(page).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "NotificationsDeliveryPaused" && t.Visibility == Visibility.Visible
                && t.Text.StartsWith("This server reports that new content is not being recorded", StringComparison.Ordinal)), "an older server's paused pipeline is reported in a read-only notice");
            await Until(() => Desc(page).OfType<TextBlock>().Any(t => t.Text == "Summary at 08:00" && t.Parent is Border) && Desc(page).OfType<TextBlock>().Any(t => t.Text == "Mail server not set up" && t.Parent is Border), "the email status pills are separate pills as officially");
            // Pills must be drawn without a resize: visible, with a real size.
            foreach (var pillText in new[] { "Registers on first send", "Mail server not set up", "Summary at 08:00", "Discord app connected", "2 destinations" })
            {
                try { await Until(() => PillDrawn(page, pillText), "status pill '" + pillText + "' is drawn at its first layout"); }
                catch (InvalidOperationException)
                {
                    var described = DescribePill(page, pillText); string render;
                    try { typeof(AdminNotificationSettingsPage).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [controller.State]); render = "a fresh render completes"; }
                    catch (TargetInvocationException error) { render = "render throws " + error.InnerException?.GetType().Name + ": " + error.InnerException?.Message + " at " + error.InnerException?.StackTrace?.Split('\n').FirstOrDefault()?.Trim(); }
                    throw new InvalidOperationException("Timed out: status pill '" + pillText + "' is drawn at its first layout; " + described + "; " + render);
                }
            }
            // An empty pill takes no space and is out of the automation tree; one that gets its text later is drawn with a size.
            page.UpdateLayout();
            var emptyPills = Desc(page).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("NotificationsPill_", StringComparison.Ordinal) && t.Text.Length == 0).ToList();
            Check(emptyPills.Count > 0, "some status pills are empty at first");
            var badPills = emptyPills.Where(t => t.Parent is not Border { Visibility: Visibility.Collapsed, ActualWidth: < 1 }).Select(t => AutomationProperties.GetAutomationId(t) + " parent=" + (t.Parent?.GetType().Name ?? "null") + " width=" + (t.Parent as Border)?.ActualWidth).ToList();
            Check(badPills.Count == 0, "an empty status pill is collapsed: no width and no automation presence" + (badPills.Count == 0 ? "" : " (" + string.Join("; ", badPills) + ")"));
            Check(PillRowsHaveNoSpareSpace(page), "no pill row reserves space for an empty pill");
            controller.Set("notifications.webhooks.allow_private_destinations", "true");
            await Until(() => PillDrawn(page, "Private destinations allowed"), "a pill that gets its text later (collapsed before, inside an Expander header) is drawn with a size");
            controller.Discard();
            await Until(() => { page.UpdateLayout(); return Desc(page).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "NotificationsPill_webhooks" && t.Text.Length == 0 && t.Parent is Border { Visibility: Visibility.Collapsed }); }, "the pill is empty again and collapsed (a collapsed element is not laid out and has no automation presence)");
            Check(PillRowsHaveNoSpareSpace(page), "no pill row reserves space after a pill went empty");
            Check(server.Values.ContainsKey("notifications.fanout_enabled") && server.Values.ContainsKey("notifications.retention.event_days"), "the synthetic server answers like an older server that still holds the retired Processing settings");
            await Capture(outer, "notifications-channels.png");
            await Expand(page, "NotificationChannel_relay"); await Expand(page, "NotificationsRelayAdvanced");
            await Until(() => Find<TextBox>(page, "NotificationsRelayUrl").IsLoaded, "relay editor");
            Check(Find<TextBox>(page, "NotificationsRelayUrl").FlowDirection == FlowDirection.LeftToRight, "the relay URL reads left to right");
            Find<TextBox>(page, "NotificationsRelayUrl").Text = "https://relay.example.test";
            Invoke(Find<Button>(page, "NotificationsRelayRegister")); await Until(() => !controller.State.OperationPending && server.Writes == 1, "relay registration");
            await Capture(outer, "notifications-relay-wide.png");
            await Until(() => Find<Button>(page, "NotificationsRelayClear") is { Visibility: Visibility.Visible, IsEnabled: true }, "relay clear offered after registration");
            Invoke(Find<Button>(page, "NotificationsRelayClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "relay clear confirmation");
            await CheckDestructive(Dialog(page)!, "relay clear"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "relay clear cancelled");
            Check(server.Writing.Count == 1, "a cancelled relay clear sent nothing");
            // The server accepts the clear but the settings cannot be read back: the confirmation closes (the command was sent and accepted),
            // the page shows its own load failure with Reload, and nothing says the clear failed.
            var acceptedBefore = server.Writing.Count;
            Invoke(Find<Button>(page, "NotificationsRelayClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "relay clear confirmation before an unreadable result");
            var acceptedDialog = Dialog(page)!; server.FailSettings = true; Invoke(Primary(acceptedDialog));
            await Until(() => Dialog(page) == null && controller.State is { Loaded: false, Error: not null, OperationPending: false }, "an accepted relay clear closed its confirmation and left the failed read");
            Check(server.Writing.Count == acceptedBefore + 1 && server.Writing[^1] == "DELETE /api/v2/admin/notifications/push/relay", "the accepted relay clear was sent once");
            await Until(() => ToastShown(toasts, "The change was accepted, but the settings could not be read again"), "the toast says the change was accepted");
            Check(Find<Button>(page, "NotificationsReload") is { Visibility: Visibility.Visible, IsEnabled: true } && Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Couldn't load notification settings", StringComparison.Ordinal)), "the page shows its load failure with Reload");
            server.FailSettings = false; Invoke(Find<Button>(page, "NotificationsReload")); await Until(() => controller.State is { Loaded: true, ChannelsLoaded: true, Error: null }, "Reload reads the settings again");
            await Until(() => Find<Button>(page, "NotificationsRelayClear") is { Visibility: Visibility.Visible, IsEnabled: true }, "relay controls back after Reload");
            await Expand(page, "NotificationChannel_discord");
            var secret = Find<PasswordBox>(page, "Notifications_discord.bot_token"); Check(secret.Password == "", "saved secrets never loaded");
            secret.Password = "synthetic-new"; await Until(() => controller.State.DirtyKeys.Contains("discord.bot_token") && !Find<Button>(page, "NotificationsDiscordTest").IsEnabled, "secret staged");
            Check(!Find<Button>(page, "NotificationsDiscordTest").IsEnabled, "unsaved Discord credentials cannot be tested");
            Invoke(Find<Button>(page, "NotificationsDiscordSave")); await Until(() => !controller.State.Saving && !controller.State.DirtyKeys.Contains("discord.bot_token"), "Discord saved");
            await Until(() => ToastShown(toasts, "Discord credentials saved"), "an acknowledged Discord save shows its toast");
            await Until(() => Find<Button>(page, "NotificationsDiscordTest").IsEnabled && secret.Password.Length == 0, "saved Discord controls rendered");
            using (var saved = JsonDocument.Parse(server.Bodies[server.Writing.FindLastIndex(w => w == "PUT /api/v2/admin/settings")]))
                Check(saved.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).SequenceEqual(["discord.bot_token"]), "the Discord save sends only the typed credential");
            Check(!Leaks(page, "synthetic-new") && !Leaks(toasts, "synthetic-new"), "the typed credential is not echoed after the save in any text, text box, password box, automation name, help text, tooltip or toast");
            var writesBeforeClear = server.Writing.Count;
            Invoke(Find<Button>(page, "NotificationsDiscordClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "Discord clear confirmation");
            await CheckDestructive(Dialog(page)!, "Discord clear"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "Discord clear cancelled");
            Check(server.Writing.Count == writesBeforeClear, "a cancelled Discord clear sent nothing");
            controller.Set("notifications.ui_enabled", "false"); await Until(() => ButtonEnabled(page, "NotificationsSave"), "page save offered");
            Invoke(Find<Button>(page, "NotificationsSave")); await Until(() => !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "page saved");
            using (var pageSave = JsonDocument.Parse(server.Bodies[server.Writing.FindLastIndex(w => w == "PUT /api/v2/admin/settings")]))
                Check(pageSave.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name).SequenceEqual(["notifications.ui_enabled"]), "the page save sends only the edited setting");
            controller.Set("notifications.ui_enabled", "true"); await Until(() => ButtonEnabled(page, "NotificationsSave"), "page save offered again");
            Invoke(Find<Button>(page, "NotificationsSave")); await Until(() => !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "page restored");
            // Official #4816: a number field shows what it holds, empty included, and an emptied field with nothing saved leaves nothing staged.
            {
                await Expand(page, "NotificationChannel_webhooks"); await Expand(page, "NotificationsWebhooksAdvanced"); await Expand(page, "NotificationChannel_email"); await Expand(page, "NotificationsEmailAdvanced");
                var perPerson = Find<TextBox>(page, "Notifications_notifications.webhooks.max_per_profile");
                var digestHour = Find<TextBox>(page, "Notifications_notifications.email.digest_hour");
                Check(perPerson.Text == "" && perPerson.PlaceholderText == "" && digestHour.Text == "8", "a number with nothing saved shows nothing (no default), a saved one shows its value");
                perPerson.Text = "20"; await Until(() => controller.State.DirtyKeys.Contains("notifications.webhooks.max_per_profile") && ButtonEnabled(page, "NotificationsSave"), "number typed with nothing saved");
                perPerson.Text = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "number erased with nothing saved");
                await Task.Delay(100);
                Check(perPerson.Text == "" && controller.State.DirtyKeys.Count == 0, "erasing a number typed with nothing saved stages nothing and shows no default");
                // The save bar is offered only while something is staged.
                bool SaveBarShown() { DependencyObject? node = Find<Button>(page, "NotificationsSave"); while (node != null) { if (node is UIElement { Visibility: Visibility.Collapsed }) return false; node = VisualTreeHelper.GetParent(node); } return true; }
                await Until(() => !SaveBarShown(), "save bar hidden after the undo");
                Check(!SaveBarShown(), "the save bar is not offered with nothing staged");
                digestHour.Text = ""; await Until(() => controller.State.DirtyKeys.Contains("notifications.email.digest_hour"), "saved number erased");
                await Task.Delay(100);
                Check(digestHour.Text == "" && controller.State.Values["notifications.email.digest_hour"] == "", "an emptied saved number stays empty and shows no default of 8");
                controller.DiscardPage(); await Until(() => controller.State.DirtyKeys.Count == 0 && digestHour.Text == "8", "discard restores the saved number");
                // A replacement typed over a saved Discord secret then erased keeps the saved secret: nothing staged, no clear.
                var savedSecret = Find<PasswordBox>(page, "Notifications_discord.bot_token");
                savedSecret.Password = "synthetic-replacement"; await Until(() => controller.State.DirtyKeys.Contains("discord.bot_token"), "replacement typed over a saved secret");
                savedSecret.Password = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "replacement erased");
                Check(controller.State.Configured.Contains("discord.bot_token") && !controller.State.DirtyKeys.Contains("discord.bot_token"), "erasing a replacement keeps the saved secret and stages no clear");
            }

            Check(!server.Bodies.Any(b => b.Contains("fanout") || b.Contains("retention") || b.Contains("release_events")), "no request body ever carries a retired Processing key");
            Invoke(Find<Button>(page, "NotificationsDiscordTest")); await Until(() => controller.State.TestResult != null, "Discord failure result");
            Check(controller.State.TestResult?.Ok == false, "delivery rejection surfaced");
            await DiscordClearScenarios(page, controller, server, toasts);
            await Expand(page, "NotificationChannel_server"); await Until(() => Desc(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NotificationsChannelEdit_c1"), "server channel card");
            await ChannelCardChecks(page, controller, server, toasts);
            Find<Button>(page, "NotificationsChannelAdd").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
            await Capture(outer, "notifications-server-channels-wide.png");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "channel editor dialog");
            Check(Desc(Dialog(page)!).OfType<ToggleSwitch>().Count() == 8, "all eight event controls");
            await Capture(Dialog(page)!, "notifications-channel-edit.png"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "editor closed");
            Invoke(Find<Button>(page, "NotificationsChannelDelete_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "delete confirmation");
            Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "delete cancelled");
            Check(!server.Writing.Any(w => w.StartsWith("DELETE") && w.EndsWith("/c1")), "a cancelled delete sent nothing");
            Invoke(Find<Button>(page, "NotificationsChannelDelete_c2")); await Until(() => Dialog(page) is { IsLoaded: true }, "hostile-name delete confirmation");
            var del = Dialog(page)!;
            Check(del.Title is TextBlock { TextWrapping: TextWrapping.Wrap, Text: "Delete \"Gate gnp.exe line\"?" }, "the confirmation title neutralizes the override and separator characters and wraps");
            await CheckDestructive(del, "channel delete");
            var deleteButton = Desc(del).OfType<Button>().First(b => b.Content?.ToString() == del.PrimaryButtonText); var before = server.Writing.Count;
            server.DeleteStatus = 409; Invoke(deleteButton); await Until(() => server.Writing.Count == before + 1 && !controller.State.OperationPending && controller.State.Error != null, "refused delete answered");
            Check(server.Writing[^1] == "DELETE /api/v2/admin/notifications/server-channels/c2" && ReferenceEquals(Dialog(page), del) && deleteButton.IsEnabled && !controller.State.OperationUnknown, "a refused delete stays open on the confirmed channel and may be corrected");
            server.DeleteStatus = 503; Invoke(deleteButton); await Until(() => controller.State.OperationUnknown, "unknown delete outcome");
            Check(server.Writing.Count == before + 2 && !deleteButton.IsEnabled, "an unknown delete outcome blocks a repeat");
            del.Hide(); await Until(() => Dialog(page) == null, "unknown delete closed");
            server.DeleteStatus = 204; await controller.ReloadAsync(true); await Until(() => !controller.State.OperationUnknown && controller.State.ChannelsLoaded, "state read again");
            Invoke(Find<Button>(page, "NotificationsChannelDelete_c2")); await Until(() => Dialog(page) is { IsLoaded: true }, "second delete confirmation");
            var del2 = Dialog(page)!; Invoke(Desc(del2).OfType<Button>().First(b => b.Content?.ToString() == del2.PrimaryButtonText)); await Until(() => Dialog(page) == null, "accepted delete closed");
            Check(server.Writing.Count(w => w == "DELETE /api/v2/admin/notifications/server-channels/c2") == 3 && !server.Writing.Any(w => w.StartsWith("DELETE") && w.EndsWith("/c1")), "each confirmation sent one delete, to the confirmed channel only");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "editor for a name-only edit");
            var edit = Dialog(page)!; Find<TextBox>(edit, "NotificationsChannelName").Text = "Edited synthetic"; await Until(() => edit.IsPrimaryButtonEnabled, "edit save enabled"); before = server.Writing.Count;
            Invoke(Desc(edit).OfType<Button>().First(b => b.Content?.ToString() == edit.PrimaryButtonText)); await Until(() => Dialog(page) == null && server.Writing.Count == before + 1, "name-only edit sent once");
            using (var editBody = JsonDocument.Parse(server.Bodies[^1]))
                Check(server.Writing[^1] == "PUT /api/v2/admin/notifications/server-channels/c1" && !editBody.RootElement.TryGetProperty("url", out _) && editBody.RootElement.GetProperty("name").GetString() == "Edited synthetic" && editBody.RootElement.EnumerateObject().Count() == 9,
                    "an untouched destination URL is not sent while the name and all eight events are");
            Invoke(Find<Button>(page, "NotificationsChannelAdd")); await Until(() => Dialog(page) is { IsLoaded: true }, "new channel dialog");
            var createDialog = Dialog(page)!;
            var editorUnloaded = false;
            createDialog.Unloaded += (_, _) => editorUnloaded = true;
            Find<TextBox>(createDialog, "NotificationsChannelName").Text = "Synthetic channel";
            Find<TextBox>(createDialog, "NotificationsChannelUrl").Text = "https://example.test/synthetic-webhook";
            Check(Find<TextBox>(createDialog, "NotificationsChannelUrl").FlowDirection == FlowDirection.LeftToRight, "the destination URL reads left to right");
            await Until(() => createDialog.IsPrimaryButtonEnabled, "create enabled");
            Invoke(Desc(createDialog).OfType<Button>().First(b => b.Content?.ToString() == createDialog.PrimaryButtonText));
            await Until(() => Dialog(page) is { IsLoaded: true, Title: "Save your signing secret" }, "one-time signing secret");
            var secretDialog = Dialog(page)!; var oneTime = Desc(secretDialog).OfType<TextBox>().Single();
            Check(editorUnloaded && !createDialog.IsLoaded, "editor actually unloads before the one-time signing-secret dialog opens");
            Check(oneTime.IsReadOnly && oneTime.Text == "synthetic-one-time", "newly minted secret shown once in its own dialog");
            secretDialog.Hide(); await Until(() => Dialog(page) == null && oneTime.Text.Length == 0, "one-time secret cleared on close");
            Check(!Leaks(page, "synthetic-webhook") && !Leaks(toasts, "synthetic-webhook") && !Leaks(page, "synthetic-one-time") && !Leaks(toasts, "synthetic-one-time"), "neither the destination URL nor the signing secret is echoed after creating a channel (text, automation name, help text, tooltip, toast)");
            using (var created = JsonDocument.Parse(server.Bodies[server.Writing.FindLastIndex(w => w == "POST /api/v2/admin/notifications/server-channels")]))
                Check(created.RootElement.EnumerateObject().Count() == 10 && created.RootElement.GetProperty("name").GetString() == "Synthetic channel" && created.RootElement.GetProperty("url").GetString() == "https://example.test/synthetic-webhook", "create sends the name, the destination and all eight events once");
            await CaptureStates(window, outer, page, server, controller);
            controller.Set("notifications.ui_enabled", "false"); server.Conflict = true; await controller.SaveAsync();
            await Until(() => controller.State.Conflict && !Find<Button>(page, "NotificationsSave").IsEnabled, "conflict disabled Save");
            server.Conflict = false; await controller.ReloadAsync(true); Check(controller.State.DirtyKeys.Contains("notifications.ui_enabled"), "reload retains draft for review");
            server.FailChannels = true; await controller.ReloadAsync(true); await Until(() => controller.State.ChannelsError != null, "channels failure");
            Check(Find<Button>(page, "NotificationsChannelRetry").IsEnabled, "channel read failure has explicit retry");
            server.FailChannels = false; await controller.ReloadAsync(true); controller.Discard();
            // A typed Discord secret is dropped when the environment takes it over; the note names the variable and is in the help text.
            secret.Password = "synthetic-typed-before-takeover"; await Until(() => controller.State.DirtyKeys.Contains("discord.bot_token"), "typed secret staged before the environment takes over");
            server.ManagedKeys = ["discord.bot_token"]; await controller.ReloadAsync(true);
            await Until(() => controller.State.ManagedByEnv.Contains("discord.bot_token") && !secret.IsEnabled, "managed Discord secret locked");
            Check(secret.Password == "" && !controller.State.DirtyKeys.Contains("discord.bot_token") && controller.State.Values["discord.bot_token"] == "", "the typed secret is neither shown nor kept nor staged once the environment sets it");
            Check(EnvironmentNoteProbe.Verify(page, secret, EnvironmentNoteProbe.Named("SILO_DISCORD_BOT_TOKEN")) is null, "the managed secret row is disabled, shows the official note naming the variable and carries it in its help text");
            Check(EnvironmentNoteProbe.VerifyEditable(Find<TextBox>(page, "Notifications_discord.client_id")) is null, "an editable Notifications row carries no environment note");
            server.NamedVariables = false; await controller.ReloadAsync(true);
            await Until(() => EnvironmentNoteProbe.Verify(page, secret, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the secret and shows the official field message");
            server.NamedVariables = true; server.ManagedKeys = []; await controller.ReloadAsync(true);
            await Until(() => secret.IsEnabled && controller.State.ManagedByEnv.Count == 0, "managed lock released");
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
            await Until(() => controller.State is { Error: not null, OperationUnknown: false, OutcomeUnknown: false } && ChannelEdit(page, true), "restore reports the cleared credential typing and a dropped delivery test blocks nothing");
            hiddenOwner = controller.State.Owner!;
            server.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var hiddenWrite = controller.RotateChannelAsync("c1");
            await Until(() => controller.State.OperationPending, "held write before host hiding");
            window.AppWindow.Hide();
            try
            {
                await Until(() => HideDeferred(page), "hidden host defers retirement for a pending write");
                Check(page.IsLoaded && !page.XamlRoot.IsHostVisible && hiddenOwner.IsCurrent && ReferenceEquals(page.Controller, controller) && controller.State.OperationPending, "hidden host keeps the owner of a pending write");
                server.Gate.TrySetResult(); await hiddenWrite;
                await Task.Delay(100);
                Check(hiddenOwner.IsCurrent && !controller.State.OperationPending && controller.HasSigningSecret && Dialog(page) == null, "secret minted while hidden waits for the visible host");
            }
            finally
            {
                server.Gate?.TrySetResult(); server.Gate = null;
                window.AppWindow.Show(); window.Activate();
            }
            await Until(() => Dialog(page) is { IsLoaded: true, Title: "Save your signing secret" }, "visible host shows the secret minted while hidden");
            var keptDialog = Dialog(page)!; var keptSecret = Desc(keptDialog).OfType<TextBox>().Single();
            Check(hiddenOwner.IsCurrent && ReferenceEquals(page.Controller, controller) && keptSecret.Text == "synthetic-one-time", "kept owner delivers the write's one-time signing secret");
            Check(controller.State is { OperationUnknown: false, OutcomeUnknown: false, Error: null }, "answered write is not carried as an unknown outcome");
            keptDialog.Hide(); await Until(() => Dialog(page) == null && keptSecret.Text.Length == 0, "kept secret cleared on close");
            await Until(() => ChannelEdit(page, true), "answered write leaves channel commands available");
            await Until(() => !shell.RequiresLeaveConfirmation, "idle page asks nothing before app departure");
            server.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var leavingWrite = controller.RotateChannelAsync("c1");
            try
            {
                await Until(() => controller.State.OperationPending, "held write before app departure");
                Check(shell.RequiresLeaveConfirmation && !await shell.CanLeaveAsync(), "Back to App and window close are refused while a write is pending");
                Check(ReferenceEquals(page.Controller, controller) && controller.State is { OperationPending: true, Owner.IsCurrent: true }, "refused departure keeps the owner of the pending write");
            }
            finally { server.Gate?.TrySetResult(); server.Gate = null; }
            await leavingWrite;
            await Until(() => Dialog(page) is { IsLoaded: true, Title: "Save your signing secret" }, "refused departure still delivers the one-time signing secret");
            var leavingDialog = Dialog(page)!; var leavingSecret = Desc(leavingDialog).OfType<TextBox>().Single();
            Check(leavingSecret.Text == "synthetic-one-time" && shell.RequiresLeaveConfirmation && !await shell.CanLeaveAsync(), "departure is refused while the signing secret is shown");
            leavingDialog.Hide(); await Until(() => Dialog(page) == null && leavingSecret.Text.Length == 0, "departure secret cleared on close");
            await Until(() => !shell.RequiresLeaveConfirmation && ChannelEdit(page, true), "dismissed secret frees departure and channel commands");
            var otherDialog = new ContentDialog { XamlRoot = page.XamlRoot, Title = "Synthetic other dialog", CloseButtonText = "Close" };
            _ = otherDialog.ShowAsync();
            try
            {
                await Until(() => otherDialog.IsLoaded, "another dialog open before a secret arrives");
                await controller.RotateChannelAsync("c1");
                await Task.Delay(100);
                Check(!controller.State.OperationPending && controller.HasSigningSecret && Dialog(page) == null && shell.RequiresLeaveConfirmation, "secret answered behind another dialog stays with the controller");
            }
            finally { otherDialog.Hide(); }
            await Until(() => Dialog(page) is { IsLoaded: true, Title: "Save your signing secret" }, "waiting secret is shown once the other dialog has unloaded");
            var waitedDialog = Dialog(page)!; var waitedSecret = Desc(waitedDialog).OfType<TextBox>().Single();
            Check(waitedSecret.Text == "synthetic-one-time" && !controller.HasSigningSecret, "the secret is taken only by the dialog that opened");
            waitedDialog.Hide(); await Until(() => Dialog(page) == null && waitedSecret.Text.Length == 0, "waited secret cleared on close");
            await Until(() => !shell.RequiresLeaveConfirmation && ChannelEdit(page, true), "waited secret frees departure and channel commands");
            await RetiredDiscordSave(page, controller, server, toasts, secret);
            await Until(() => ChannelEdit(page, true), "channel commands available after the page was reopened");
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
            toastService.Unregister(); window.Content = previousContent; field.SetValue(null, previous);
        }
    }
    private static async Task CheckDestructive(ContentDialog dialog, string what)
    {
        var primary = Desc(dialog).OfType<Button>().First(b => b.Content?.ToString() == dialog.PrimaryButtonText);
        Check(primary.Background is SolidColorBrush { Color: { R: 0xDC, G: 0x26, B: 0x26 } } && primary.Foreground is SolidColorBrush { Color: { R: 255, G: 255, B: 255 } }, what + " confirm is the official destructive fill #dc2626 under white text");
        Check(primary.UseSystemFocusVisuals, what + " confirm keeps the system keyboard focus rectangle");
        var root = Desc(primary).OfType<Border>().First(b => b.Name == "DestructiveRoot");
        VisualStateManager.GoToState(primary, "PointerOver", false);
        await Until(() => root.Background is SolidColorBrush { Color: { A: 0xE6, R: 0xDC, G: 0x26, B: 0x26 } }, what + " confirm hover fill applied");
        Check(root.Background is SolidColorBrush { Color: { A: 0xE6, R: 0xDC, G: 0x26, B: 0x26 } }, what + " confirm hover is the official hover:bg-destructive-fill/90 (the red at 90% opacity)");
        VisualStateManager.GoToState(primary, "Pressed", false);
        await Until(() => root.Background is SolidColorBrush { Color: { R: 0x99, G: 0x1B, B: 0x1B } }, what + " confirm pressed fill applied");
        Check(root.Background is SolidColorBrush { Color: { R: 0x99, G: 0x1B, B: 0x1B } }, what + " confirm pressed is the native-only darker fill (the official button has no pressed state)");
        VisualStateManager.GoToState(primary, "Normal", false);
        await Until(() => root.Background is SolidColorBrush { Color: { A: 0xFF, R: 0xDC, G: 0x26, B: 0x26 } }, what + " confirm normal fill applied");
        Check(root.Background is SolidColorBrush { Color: { R: 0xDC, G: 0x26, B: 0x26 } }, what + " confirm returns to the fill");
    }
    /// <summary>The main states at 1440, 1024, 800 and 560 px: settings, channel list, editor, confirmation, empty list, failed channel read and failed settings read.</summary>
    private static async Task CaptureStates(Window window, Frame outer, AdminNotificationSettingsPage page, Server server, AdminNotificationSettingsController controller)
    {
        foreach (var width in new[] { 1440, 1024, 800, 560 })
        {
            await ResizeAsync(window, outer, width);
            Desc(page).OfType<ScrollViewer>().First(scroll => AutomationProperties.GetName(scroll) == "Notifications").ChangeView(null, 0, null, true);
            await Capture(outer, $"notifications-settings-{width}.png");
            void ShowAdd() => Find<Button>(page, "NotificationsChannelAdd").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0.05 });
            ShowAdd(); await Capture(outer, $"notifications-channels-{width}.png");
            Invoke(Find<Button>(page, "NotificationsChannelEdit_c1")); await Until(() => Dialog(page) is { IsLoaded: true }, "editor at " + width);
            await Capture(Dialog(page)!, $"notifications-editor-{width}.png"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "editor closed at " + width);
            Invoke(Find<Button>(page, "NotificationsChannelDelete_c2")); await Until(() => Dialog(page) is { IsLoaded: true }, "confirmation at " + width);
            await Capture(Dialog(page)!, $"notifications-confirm-{width}.png"); Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "confirmation closed at " + width);
            server.NoChannels = true; await controller.ReloadAsync(true); await Until(() => controller.State is { ChannelsLoaded: true, Channels.Count: 0 }, "empty channel list at " + width);
            ShowAdd(); await Capture(outer, $"notifications-empty-{width}.png");
            Check(Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("No server channels yet") && t.Visibility == Visibility.Visible), "an empty channel list says so at " + width);
            server.NoChannels = false; server.FailChannels = true; await controller.ReloadAsync(true); await Until(() => controller.State.ChannelsError != null, "failed channel read at " + width);
            ShowAdd(); await Capture(outer, $"notifications-channels-failed-{width}.png");
            Check(!Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("No server channels yet") && t.Visibility == Visibility.Visible) && Find<Button>(page, "NotificationsChannelRetry").Visibility == Visibility.Visible, "a failed channel read says it failed and offers Retry instead of claiming the list is empty at " + width);
            server.FailChannels = false; await controller.ReloadAsync(true); await Until(() => controller.State is { ChannelsLoaded: true, Channels.Count: 2 }, "channels restored at " + width);
            server.FailSettings = true; await controller.ReloadAsync(true); await Until(() => controller.State is { Loaded: false, Error: not null }, "failed settings read at " + width);
            await Until(() => Desc(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Couldn't load notification settings") && t.Text.Contains("Reload to try again.")), "official load-failure title and description at " + width);
            await Capture(outer, $"notifications-load-failed-{width}.png");
            server.FailSettings = false; Invoke(Find<Button>(page, "NotificationsReload")); await Until(() => controller.State is { Loaded: true, ChannelsLoaded: true, Error: null }, "Retry reloads the page at " + width);
        }
        await ResizeAsync(window, outer, 1440);
    }
    /// <summary>Resizes the client area and waits until the page host has the new width, then lays it out.</summary>
    private static async Task ResizeAsync(Window window, FrameworkElement host, int width)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
        await Until(() => host.XamlRoot is { } root && Math.Abs(host.ActualWidth * root.RasterizationScale - width) <= 2, "window client resized to " + width);
        host.UpdateLayout();
    }
    private static IEnumerable<FrameworkElement> Chain(FrameworkElement start) { for (DependencyObject? node = start; node != null; node = VisualTreeHelper.GetParent(node)) if (node is FrameworkElement element) yield return element; }
    private static bool PillDrawn(DependencyObject page, string text)
    {
        (page as FrameworkElement)?.UpdateLayout();
        return PillShown(page, text);
    }
    private static bool PillShown(DependencyObject page, string text) => Desc(page).OfType<TextBlock>().FirstOrDefault(t => t.Text == text) is { Parent: Border { Visibility: Visibility.Visible, ActualWidth: > 20, ActualHeight: > 10 } };
    private static string DescribePill(DependencyObject page, string text) => Desc(page).OfType<TextBlock>().FirstOrDefault(t => t.Text == text) is { } block
        ? "text found, chain " + string.Join(" < ", Chain(block).Take(9).Select(e => $"{e.GetType().Name}[{e.Visibility},{e.ActualWidth:0}x{e.ActualHeight:0}]"))
        : "text not found among " + string.Join(" | ", Desc(page).OfType<TextBlock>().Where(t => t.Parent is Border && t.Text.Length > 0 && t.Text.Length < 40).Select(t => t.Text));
    private static Button Primary(ContentDialog dialog) => Desc(dialog).OfType<Button>().First(b => b.Content?.ToString() == dialog.PrimaryButtonText);
    private static string[] ToastTexts(DependencyObject toasts) => Desc(toasts).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).ToArray();
    private static bool ToastShown(DependencyObject toasts, string text) => ToastTexts(toasts).Any(t => t.Contains(text, StringComparison.Ordinal));
    private static void SetActive(AdminNotificationSettingsPage page, bool value) => typeof(AdminNotificationSettingsPage).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    /// <summary>Every string a screen reader, a tooltip or the screen can surface from this tree.</summary>
    private static IEnumerable<string> Surfaces(DependencyObject root)
    {
        foreach (var node in Desc(root))
        {
            if (node is TextBlock block) yield return block.Text;
            if (node is TextBox box) { yield return box.Text; yield return box.PlaceholderText; if (box.Header is string header) yield return header; }
            if (node is PasswordBox password) { yield return password.Password; yield return password.PlaceholderText; }
            if (node is ContentControl { Content: string content }) yield return content;
            if (node is FrameworkElement element)
            {
                yield return AutomationProperties.GetName(element); yield return AutomationProperties.GetHelpText(element);
                yield return AutomationProperties.GetFullDescription(element); yield return AutomationProperties.GetItemStatus(element);
                if (ToolTipService.GetToolTip(element) is { } tip) yield return tip is ToolTip { Content: { } inner } ? inner.ToString() ?? "" : tip.ToString() ?? "";
            }
        }
    }
    private static bool Leaks(DependencyObject root, string needle) => Surfaces(root).Any(text => text != null && text.Contains(needle, StringComparison.Ordinal));
    private static bool PillRowsHaveNoSpareSpace(DependencyObject page) => Desc(page).OfType<StackPanel>()
        .Where(p => p.Orientation == Orientation.Horizontal && p.Children.OfType<Border>().Any(b => b.Child is TextBlock t && AutomationProperties.GetAutomationId(t).StartsWith("NotificationsPill_", StringComparison.Ordinal)))
        .All(row =>
        {
            // The row's desired width ends at the right edge of the last pill that has a size; an empty trailing pill adds nothing.
            var sized = row.Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible && c.ActualWidth > 0).ToList();
            var edge = sized.Count == 0 ? 0 : sized.Max(c => c.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(c.ActualWidth, 0)).X);
            return Math.Abs(row.DesiredSize.Width - edge) < 1.5;
        });
    /// <summary>Discord Clear: the editable credentials only while the environment manages one, no repeat after a conflict or unknown outcome, contrast theme.</summary>
    private static async Task DiscordClearScenarios(AdminNotificationSettingsPage page, AdminNotificationSettingsController controller, Server server, DependencyObject toasts)
    {
        Check(Find<Button>(page, "NotificationsDiscordClear") is { Visibility: Visibility.Visible, IsEnabled: true }, "Discord Clear is offered while no key is environment-managed");
        // Conflict: the button comes back only when a repeat could be sent, and the message says what happened.
        var writesBefore = server.Writing.Count;
        Invoke(Find<Button>(page, "NotificationsDiscordClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "Discord clear confirmation for a conflict");
        var conflictDialog = Dialog(page)!; var conflictButton = Primary(conflictDialog); server.Conflict = true; Invoke(conflictButton);
        await Until(() => controller.State is { Conflict: true, Saving: false }, "Discord clear answered with a conflict");
        Check(ReferenceEquals(Dialog(page), conflictDialog) && !conflictButton.IsEnabled && server.Writing.Count == writesBefore + 1, "a Discord clear that ended in a conflict cannot be pressed again");
        Check(Desc(conflictDialog).OfType<TextBlock>().Any(t => t.Text.Length > 0 && t.Text == controller.State.Error) && !Desc(conflictDialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("Nothing was sent", StringComparison.Ordinal)), "the dialog shows the conflict message, not a claim that a repeat was refused");
        conflictDialog.Hide(); await Until(() => Dialog(page) == null, "conflict dialog closed"); server.Conflict = false;
        await controller.ReloadAsync(true); await Until(() => controller.State is { Conflict: false, Loaded: true, ChannelsLoaded: true }, "conflict cleared by Reload");
        // Unknown outcome (a transport-class failure on the save): same rule.
        Invoke(Find<Button>(page, "NotificationsDiscordClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "Discord clear confirmation for an unknown outcome");
        var unknownDialog = Dialog(page)!; var unknownButton = Primary(unknownDialog); server.PutStatus = 503; Invoke(unknownButton);
        await Until(() => controller.State is { OutcomeUnknown: true, Saving: false }, "Discord clear with an unknown outcome");
        Check(ReferenceEquals(Dialog(page), unknownDialog) && !unknownButton.IsEnabled && server.Writing.Count == writesBefore + 2, "a Discord clear with an unknown outcome cannot be pressed again");
        unknownDialog.Hide(); await Until(() => Dialog(page) == null, "unknown-outcome dialog closed"); server.PutStatus = 0;
        await controller.ReloadAsync(true); await Until(() => controller.State is { OutcomeUnknown: false, Loaded: true, ChannelsLoaded: true }, "unknown outcome cleared by Reload");
        // A contrast theme leaves the dialog's own button (system colors) instead of the fixed red. The probe stands in for the theme: a contrast theme cannot be switched on here.
        var probe = typeof(AdminNotificationSettingsPage).GetProperty("HighContrastProbe", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = probe.GetValue(null); probe.SetValue(null, new Func<bool>(() => true));
        try
        {
            Invoke(Find<Button>(page, "NotificationsDiscordClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "Discord clear confirmation in a contrast theme");
            var contrast = Dialog(page)!; var contrastButton = Primary(contrast);
            Check(!Desc(contrastButton).OfType<Border>().Any(b => b.Name == "DestructiveRoot") && contrastButton.Background is not SolidColorBrush { Color: { R: 0xDC, G: 0x26, B: 0x26 } }, "in a contrast theme the confirm is the default button, not the fixed red");
            contrast.Hide(); await Until(() => Dialog(page) == null, "contrast confirmation closed");
        }
        finally { probe.SetValue(null, original); }
        // One key managed by the environment: Clear stays offered and clears the editable keys only (official clearAll).
        server.Managed = ["discord.client_secret"]; await controller.ReloadAsync(true);
        await Until(() => controller.State is { Loaded: true, ChannelsLoaded: true } && controller.State.ManagedByEnv.Contains("discord.client_secret"), "one managed key read");
        await Until(() => Find<Button>(page, "NotificationsDiscordClear") is { Visibility: Visibility.Visible, IsEnabled: true }, "Clear offered with one managed key");
        var beforeManaged = server.Writing.Count;
        Invoke(Find<Button>(page, "NotificationsDiscordClear")); await Until(() => Dialog(page) is { IsLoaded: true }, "Discord clear confirmation with one managed key");
        var managed = Dialog(page)!;
        Check(managed.Title is TextBlock { Text: "Clear Discord app credentials?" } && Desc(managed).OfType<TextBlock>().Any(t => t.Text == "Account linking and Discord direct messages stop working immediately."), "the confirmation uses the official title and description");
        Invoke(Primary(managed)); await Until(() => Dialog(page) == null && server.Writing.Count == beforeManaged + 1, "the clear with one managed key was sent once");
        using (var body = JsonDocument.Parse(server.Bodies[^1]))
        {
            var values = body.RootElement.GetProperty("values").EnumerateObject().ToArray();
            Check(values.Select(p => p.Name).Order().SequenceEqual(["discord.bot_token", "discord.client_id"]) && values.All(p => p.Value.GetString() == ""), "the clear sends only the editable keys, emptied");
            Check(!server.Bodies[^1].Contains("client_secret"), "the environment-managed key is never in the body");
        }
        await Until(() => ToastShown(toasts, "Discord credentials cleared"), "an acknowledged clear shows its toast");
        await Until(() => Find<Button>(page, "NotificationsDiscordClear").Visibility == Visibility.Collapsed, "Clear is not offered once no editable credential is stored");
        // Back to the unmanaged state of the fixture.
        server.Values["discord.client_id"] = "synthetic-app"; server.Managed = []; await controller.ReloadAsync(true);
        await Until(() => controller.State is { Loaded: true, ChannelsLoaded: true, DiscordReady: true } && Find<Button>(page, "NotificationsDiscordClear") is { Visibility: Visibility.Visible, IsEnabled: true }, "unmanaged Discord state restored");
    }
    /// <summary>Card buttons named for their channel, third-party text neutralized, and a refused Test or Rotate that says so and reloads.</summary>
    private static async Task ChannelCardChecks(AdminNotificationSettingsPage page, AdminNotificationSettingsController controller, Server server, DependencyObject toasts)
    {
        Check(AutomationProperties.GetName(Find<Button>(page, "NotificationsChannelTest_c1")) == "Test Synthetic community" && Find<Button>(page, "NotificationsChannelTest_c1").Content?.ToString() == "Test", "Test is named for its channel and keeps its visible label");
        Check(AutomationProperties.GetName(Find<Button>(page, "NotificationsChannelEdit_c1")) == "Edit Synthetic community" && AutomationProperties.GetName(Find<Button>(page, "NotificationsChannelRotate_c1")) == "Rotate secret Synthetic community", "Edit and Rotate secret are named for their channel");
        Check(AutomationProperties.GetName(Find<Button>(page, "NotificationsChannelDelete_c1")) == "Delete Synthetic community" && AutomationProperties.GetName(Find<Button>(page, "NotificationsChannelDelete_c2")) == "Delete Gate gnp.exe line", "Delete is named for its channel, with the name neutralized");
        Check(!Desc(page).OfType<TextBlock>().Any(t => t.Text.Contains('\u202E') || t.Text.Contains('\u2028')), "no direction override or line separator reaches the page text (names, hosts, server failure text)");
        Check(Desc(page).OfType<TextBlock>().Any(t => t.Text == "discord · discord.example.test") && Desc(page).OfType<TextBlock>().Any(t => t.Text.Contains("Synthetic rejected")), "the host and the server's failure text are shown neutralized");
        Invoke(Find<Button>(page, "NotificationsChannelEdit_c2")); await Until(() => Dialog(page) is { IsLoaded: true }, "editor for the placeholder check");
        var editor = Dialog(page)!; var placeholder = Find<TextBox>(editor, "NotificationsChannelUrl").PlaceholderText;
        Check(placeholder.Contains("discord.example.test") && !placeholder.Contains('\u202E'), "the editor's current-host hint shows the host neutralized");
        editor.Hide(); await Until(() => Dialog(page) == null, "placeholder editor closed");
        // A Test or Rotate whose card no longer matches the listed channel sends nothing, says so and reloads. Rendering is held so the card stays as it was shown.
        var writes = server.Writing.Count; SetActive(page, false);
        try
        {
            server.RenameC1 = "Renamed synthetic"; await controller.ReloadAsync(true);
            await Until(() => controller.State.Channels.Any(c => c.Id == "c1" && c.Name == "Renamed synthetic"), "the renamed channel was read");
            Invoke(Find<Button>(page, "NotificationsChannelTest_c1"));
            await Until(() => ToastTexts(toasts).Any(t => t.StartsWith("Nothing was sent. This channel changed on the server", StringComparison.Ordinal)), "a refused Test says nothing was sent");
            var shownBefore = ToastTexts(toasts).Count(t => t.StartsWith("Nothing was sent. This channel changed", StringComparison.Ordinal));
            // The refusal reloads the list; a second command during that reload would be refused as busy instead.
            await Until(() => controller.State is { Loading: false, ChannelsLoading: false, Loaded: true, ChannelsLoaded: true }, "the reload after the refusal finished");
            Invoke(Find<Button>(page, "NotificationsChannelRotate_c1"));
            await Until(() => ToastTexts(toasts).Count(t => t.StartsWith("Nothing was sent. This channel changed", StringComparison.Ordinal)) > shownBefore, "a refused Rotate says nothing was sent");
        }
        finally { SetActive(page, true); }
        Check(server.Writing.Count == writes && !controller.HasSigningSecret, "a refused Test and a refused Rotate sent nothing");
        await controller.ReloadAsync(true);
        await Until(() => Desc(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NotificationsChannelTest_c1" && AutomationProperties.GetName(b) == "Test Renamed synthetic"), "the card shows the current channel after the refusal");
        server.RenameC1 = null; await controller.ReloadAsync(true);
        await Until(() => Desc(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NotificationsChannelTest_c1" && AutomationProperties.GetName(b) == "Test Synthetic community") && ChannelEdit(page, true), "the card is restored");
    }
    /// <summary>A Discord save whose owner is retired while the write is in flight shows no "saved" toast: nothing observed it succeed.</summary>
    private static async Task RetiredDiscordSave(AdminNotificationSettingsPage page, AdminNotificationSettingsController controller, Server server, DependencyObject toasts, PasswordBox secret)
    {
        var shownBefore = ToastTexts(toasts).Count(t => t.Contains("Discord credentials saved", StringComparison.Ordinal)); var writes = server.Writing.Count;
        secret.Password = "synthetic-retired-save";
        await Until(() => controller.State.DirtyKeys.Contains("discord.bot_token") && ButtonEnabled(page, "NotificationsDiscordSave"), "a credential is staged for the save whose owner will be retired");
        server.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(Find<Button>(page, "NotificationsDiscordSave"));
        await Until(() => controller.State.Saving && server.Writing.Count == writes + 1, "the Discord save is held on the wire");
        var retired = controller.State.Owner!;
        controller.Suspend(); server.Gate.TrySetResult(); server.Gate = null;
        await Until(() => !retired.IsCurrent && controller.State.Owner == null, "the owner was retired while the save was in flight");
        // Nothing is raised when an outcome is dropped, so there is no event to wait for; the toast the old check would have shown is posted
        // right after the save returns, well inside this wait. The positive case above shows the same path does show the toast.
        await Task.Delay(750);
        Check(ToastTexts(toasts).Count(t => t.Contains("Discord credentials saved", StringComparison.Ordinal)) == shownBefore, "no 'Discord credentials saved' toast for a save whose owner was retired in flight");
        await controller.OpenAsync();
        await Until(() => controller.State is { Loaded: true, ChannelsLoaded: true, Owner.IsCurrent: true }, "the page reopened with a fresh owner");
        Check(ToastTexts(toasts).Count(t => t.Contains("Discord credentials saved", StringComparison.Ordinal)) == shownBefore && !controller.State.DirtyKeys.Contains("discord.bot_token"), "the reopened page shows no saved toast and holds no staged credential");
    }
    private static bool ButtonEnabled(DependencyObject root, string id) => Desc(root).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == id && b.IsEnabled);
    private static bool ChannelEdit(DependencyObject page, bool enabled) => Desc(page).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NotificationsChannelEdit_c1" && b.IsEnabled == enabled);
    private static ContentDialog? Dialog(AdminNotificationSettingsPage page) => (ContentDialog?)typeof(AdminNotificationSettingsPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static bool HideDeferred(AdminNotificationSettingsPage page) => (bool)typeof(AdminNotificationSettingsPage).GetField("_hideDeferred", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
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
