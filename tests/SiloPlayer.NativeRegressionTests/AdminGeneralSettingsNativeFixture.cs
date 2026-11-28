using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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

/// <summary>Production Configure registration and Frame entry using synthetic server data only.</summary>
internal static class AdminGeneralSettingsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 6000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }
    private sealed class Server : HttpMessageHandler
    {
        internal readonly Dictionary<string, string> Values = new()
        {
            ["branding.server_name"] = "Synthetic Silo", ["branding.login_subtitle"] = "", ["server.public_url"] = "",
            ["server.lan_discovery"] = "true", ["server.local_url"] = "", ["clientip.trusted_proxies"] = "",
            ["database.max_connections"] = "20", ["redis.db"] = "0",
        };
        internal int Saves, Checks, Version = 1;
        internal bool RedisConfigured, SensitiveFailure, Managed, Conflict, ReadForbidden;
        /// <summary>True makes the server refuse (412) a write whose If-Match is not its current version, as the real one does.</summary>
        internal bool EnforceVersion;
        internal int Refusals;
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal TaskCompletionSource? SaveGate;
        internal string? LastIfMatch;
        internal string[] LastSavedKeys = [];
        internal JsonElement LastCheckBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (ReadForbidden && path.EndsWith("/effective", StringComparison.Ordinal)) return Json(new { type = "permission_denied", title = "Forbidden", status = 403 }, HttpStatusCode.Forbidden);
            object body;
            if (request.Method == HttpMethod.Put)
            {
                Saves++; LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LastSavedKeys = data.RootElement.GetProperty("values").EnumerateObject().Select(entry => entry.Name).ToArray();
                if (SaveGate is { } pending) await pending.Task.WaitAsync(cancellationToken);
                if (Conflict) return Json(new { type = "precondition_failed", title = "Settings changed", status = 412 }, HttpStatusCode.PreconditionFailed);
                if (EnforceVersion && LastIfMatch != $"\"general-fixture-v{Version}\"") { Refusals++; return Json(new { type = "precondition_failed", title = "Settings changed", status = 412 }, HttpStatusCode.PreconditionFailed); }
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject())
                {
                    if (entry.Name == "redis.url") RedisConfigured = entry.Value.GetString()!.Length > 0;
                    else updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!;
                }
                Version++;
                body = new { values = updated, restart_required = false };
            }
            else if (path.EndsWith("/check/redis", StringComparison.Ordinal))
            {
                using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LastCheckBody = data.RootElement.Clone();
                Checks++; body = new { success = true, message = "Connection successful." };
            }
            else if (path.EndsWith("/sensitive-status", StringComparison.Ordinal))
            {
                if (SensitiveFailure) return Json(new { title = "Status unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                body = Managed && NamedVariables
                    ? new { configured = RedisConfigured ? new[] { "redis.url" } : [], managed_by_env = new[] { "redis.url", "clientip.trusted_proxies" },
                        managed_by_env_vars = new Dictionary<string, string> { ["redis.url"] = "REDIS_URL", ["redis.db"] = "REDIS_URL", ["clientip.trusted_proxies"] = "SILO_TRUSTED_PROXIES" } }
                    : (object)new { configured = RedisConfigured ? new[] { "redis.url" } : [], managed_by_env = Managed ? new[] { "redis.url", "clientip.trusted_proxies" } : [] };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/restart-keys" => new { keys = Array.Empty<string>(), prefixes = new[] { "redis." } },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(body);
        }
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"general-fixture-v{Version}\""); return response;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation)
            .AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory));
        // Invoke the same registration entry point as App, rather than registering a test-only page.
        var registration = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices");
        registration?.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        var previousAppName = DocumentTitle.AppName;
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/general");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("General is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("General did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content?.GetType().Name != "AdminGeneralSettingsPage"; i++) await Task.Delay(25);
            if (inner.Content?.GetType().Name != "AdminGeneralSettingsPage") throw new InvalidOperationException("General did not activate its production WinUI Page.");
            var page = (AdminGeneralSettingsPage)inner.Content;
            await Until(() => page.Controller?.State.Loaded == true && Input(page, "branding.server_name").Text == "Synthetic Silo", "General fields rendered");
            Check(true, "General activates through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal)), "rendered General and navigation labels have catalog text");
            Check(AdminNavigation.SearchGroups("redis", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Single().Route == AdminRoute.GeneralSettings,
                "registered General is searchable while other Configure leaves remain absent");
            var controller = page.Controller!;
            var toggle = Descendants(page).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t) == "GeneralRedisEnabled");
            toggle.IsOn = true; await Task.Delay(150);
            Check(toggle.IsOn, "Use Redis can be enabled before the first credential edit");
            if (toggle.IsOn)
            {
                var secret = Descendants(page).OfType<PasswordBox>().Single();
                secret.Password = "redis://fixture.invalid:6379";
                await Until(() => controller.State.DirtyKeys.Contains("redis.url"), "Redis edit staged");
                secret.Password = "";
                await Until(() => controller.State.Values.GetValueOrDefault("redis.url", "") == "", "new Redis URL erased");
                await Task.Delay(100);
                // Official #4816: erasing a URL typed with nothing saved takes its edit back (no empty replacement stays staged) while Use Redis stays on.
                Check(toggle.IsOn && secret.IsLoaded && secret.Visibility == Visibility.Visible && controller.State.DirtyKeys.Count == 0 && !Button(page, "GeneralSave").IsEnabled,
                    "erasing a Redis URL typed with nothing saved keeps Use Redis and the editor in view and takes its edit back (nothing staged, Save disabled)");
                secret.Password = "redis://fixture.invalid:6379";
                await Until(() => controller.State.Values.GetValueOrDefault("redis.url", "") != "", "Redis replacement restaged");
                await Click(page, "GeneralCheckRedis");
                await Until(() => server.Checks == 1 && controller.State.ConnectionResult?.Success == true, "Redis connection check");
                var checkValues = server.LastCheckBody.GetProperty("values");
                Check(checkValues.GetProperty("redis.url").GetString() == "redis://fixture.invalid:6379" &&
                    checkValues.GetProperty("redis.db").GetString() == "0" &&
                    server.LastCheckBody.GetProperty("dirty_keys").EnumerateArray().Select(key => key.GetString()).Order().SequenceEqual(new[] { "redis.db", "redis.url" }),
                    "published Check Connection wire body contains paired Redis values and dirty_keys");
                await Click(page, "GeneralSave"); await Until(() => !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "Redis saved");
                await Until(() => secret.Password == "" && toggle.IsEnabled, "acknowledged Redis editor rendered");
                Check(controller.State.Configured.Contains("redis.url") && secret.Password == "", "acknowledged Redis replacement clears draft and updates configured presence");
                Check(server.LastSavedKeys.Order().SequenceEqual(new[] { "redis.db", "redis.url" }), "published settings PUT wire body contains paired Redis values");
                if (!controller.State.Configured.Contains("redis.url")) { await controller.ReloadAsync(false); await Until(() => toggle.IsOn, "saved presence loaded for remaining checks"); }
                toggle.IsOn = false; await Task.Delay(100); await Click(page, "GeneralSave");
                await Until(() => !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "Redis clear saved");
                Check(!toggle.IsOn && !controller.State.Configured.Contains("redis.url"), "acknowledged Redis clear stays disabled without stale saved presence");
            }

            // Official #4696: switching Redis off with no saved URL drops what was typed, because the fields that showed it are about to hide; nothing unseen is saved.
            {
                toggle.IsOn = true; await Task.Delay(150);
                var typed = Descendants(page).OfType<PasswordBox>().Single();
                typed.Password = "redis://typed-then-off.invalid:6379";
                await Until(() => controller.State.DirtyKeys.Contains("redis.url"), "Redis URL typed before the switch-off");
                var savesBeforeOff = server.Saves;
                toggle.IsOn = false;
                await Until(() => controller.State.DirtyKeys.Count == 0 && !toggle.IsOn, "switching Redis off with no saved URL drops the typed URL");
                await Task.Delay(150);
                Check(typed.Password == "" && controller.State.Values.GetValueOrDefault("redis.url", "") == "" && server.Saves == savesBeforeOff,
                    "switching Redis off with no saved URL clears the typed URL from the box and the draft and sends nothing");
                Check(!EnvironmentNoteProbe.EffectivelyShown(typed, page), "the Redis URL and database rows are gone with the switch");
            }

            // Two activations in one turn: Off, then On again before the queued render. The second is the admin's change from the first, not an echo of the state shown.
            {
                toggle.IsOn = true; await Task.Delay(150);
                Check(toggle.IsOn && controller.State.DirtyKeys.Count == 0, "Use Redis is on before the quick double activation");
                toggle.IsOn = false; toggle.IsOn = true;
                await Task.Delay(300);
                Check(toggle.IsOn, "Off then On in one turn leaves Use Redis on (the second activation is not ignored and the switch does not return to Off)");
                toggle.IsOn = false;
                await Task.Delay(300);
                Check(!toggle.IsOn && controller.State.DirtyKeys.Count == 0, "switching it off again leaves Use Redis off with nothing staged");
            }

            // Official #4816 (RedisGroup.test.tsx): the Use Redis switch keeps its own choice until a Save or a Discard, not until the page runs out of unsaved changes.
            {
                var urlBox = Descendants(page).OfType<PasswordBox>().Single();
                var dbBox = Input(page, "redis.db");
                var publicUrl = Input(page, "server.public_url");
                async Task Reload(bool keep) { await controller.ReloadAsync(keep); await Until(() => controller.State.Loaded && !controller.State.Loading, "reloaded"); await Task.Delay(150); }
                bool UrlShown() => EnvironmentNoteProbe.EffectivelyShown(urlBox, page);
                // With no URL and no number saved, the snapshot holds neither: typing into the number, or another setting, and erasing it leaves Use Redis on.
                server.Values.Remove("redis.db"); await Reload(false);
                Check(!toggle.IsOn && controller.State.DirtyKeys.Count == 0, "Use Redis starts off with no URL saved");
                toggle.IsOn = true; await Task.Delay(150);
                dbBox.Text = "3"; await Until(() => controller.State.DirtyKeys.Contains("redis.db"), "database number typed");
                dbBox.Text = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "database number erased");
                await Task.Delay(150);
                Check(toggle.IsOn && UrlShown() && !Button(page, "GeneralSave").IsEnabled, "Use Redis stays on while the database number is typed into and erased (nothing staged, Save disabled)");
                publicUrl.Text = "https://typed.example.invalid"; await Until(() => controller.State.DirtyKeys.Contains("server.public_url"), "another setting typed");
                publicUrl.Text = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "another setting erased");
                await Task.Delay(150);
                Check(toggle.IsOn && UrlShown() && controller.State.Values["server.public_url"] == "" && !Button(page, "GeneralSave").IsEnabled,
                    "Use Redis stays on while another setting with nothing saved is typed into and erased, and shows no default in the box");
                // Save ends the switch's own choice, so it follows what is saved: nothing, so it reads off. Discard does the same.
                var nameBox = Input(page, "branding.server_name");
                nameBox.Text = "Switch Save"; await Until(() => controller.State.DirtyKeys.Contains("branding.server_name"), "name edited");
                var savesBeforeSwitchSave = server.Saves;
                await Click(page, "GeneralSave"); await Until(() => server.Saves == savesBeforeSwitchSave + 1 && !controller.State.Saving && controller.State.DirtyKeys.Count == 0, "name saved");
                await Until(() => !toggle.IsOn, "Use Redis follows what is saved after a Save");
                Check(!toggle.IsOn && !UrlShown(), "a Save ends the Use Redis switch's own choice");
                toggle.IsOn = true; await Task.Delay(150);
                nameBox.Text = "Switch Discard"; await Until(() => controller.State.DirtyKeys.Contains("branding.server_name"), "name edited again");
                await Click(page, "GeneralDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0 && !toggle.IsOn, "Use Redis follows what is saved after a Discard");
                Check(!toggle.IsOn && nameBox.Text == "Switch Save", "a Discard ends the Use Redis switch's own choice");
                // A URL saved elsewhere after Use Redis was turned on and back off still shows.
                toggle.IsOn = true; await Task.Delay(150);
                urlBox.Password = "redis://cache.invalid:6379"; await Until(() => controller.State.DirtyKeys.Contains("redis.url"), "URL typed");
                toggle.IsOn = false; await Until(() => controller.State.DirtyKeys.Count == 0 && !toggle.IsOn, "typed URL dropped with the switch-off");
                server.RedisConfigured = true; await Reload(true);
                Check(toggle.IsOn && UrlShown(), "a URL saved elsewhere shows after Use Redis was turned on and back off");
                // A saved URL removed elsewhere after Use Redis was turned off and back on: the switch follows what is saved again.
                toggle.IsOn = false; await Until(() => controller.State.DirtyKeys.Contains("redis.url") && !toggle.IsOn, "switch-off staged");
                toggle.IsOn = true; await Until(() => controller.State.DirtyKeys.Count == 0 && toggle.IsOn, "switch-off taken back");
                server.RedisConfigured = false; await Reload(true);
                Check(!toggle.IsOn && !UrlShown(), "a saved URL removed elsewhere hides the fields after Use Redis was turned off and back on");
                // A staged database number keeps the fields in view when the saved URL goes away.
                server.RedisConfigured = true; await Reload(false);
                toggle.IsOn = false; await Until(() => !toggle.IsOn && controller.State.DirtyKeys.Contains("redis.url"), "switch-off staged for the number case");
                toggle.IsOn = true; await Until(() => controller.State.DirtyKeys.Count == 0 && toggle.IsOn, "switch-off taken back for the number case");
                dbBox.Text = "3"; await Until(() => controller.State.DirtyKeys.SequenceEqual(["redis.db"]), "database number staged");
                server.RedisConfigured = false; await Reload(true);
                Check(toggle.IsOn && UrlShown() && dbBox.Text == "3" && controller.State.DirtyKeys.SequenceEqual(["redis.db"]),
                    "a staged database number stays in view when the saved URL is removed elsewhere");
                await Click(page, "GeneralDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "number discarded");
                server.Values["redis.db"] = "0"; await Reload(false);
                Check(!toggle.IsOn, "Use Redis is off again with nothing saved");

                // Official #4816: an edit undone in place leaves no baseline behind. The fixture server refuses (412) a write that is not made over its current version.
                server.EnforceVersion = true; await Reload(false);
                publicUrl.Text = "https://typed-then-erased.invalid"; await Until(() => controller.State.DirtyKeys.Contains("server.public_url"), "typed before the undo");
                publicUrl.Text = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "undone in place");
                server.Values["server.local_url"] = "http://another-admin.invalid:8080"; server.Version++; // another admin's write
                await Reload(true);
                nameBox.Text = "Saved after the undo"; await Until(() => controller.State.DirtyKeys.SequenceEqual(["branding.server_name"]), "name staged after the undo");
                var refusalsBefore = server.Refusals; var savesBeforeUndo = server.Saves;
                await Click(page, "GeneralSave"); await Until(() => server.Saves == savesBeforeUndo + 1 && !controller.State.Saving, "save after the undo");
                Check(server.Refusals == refusalsBefore && !controller.State.Conflict && controller.State.DirtyKeys.Count == 0 && server.LastSavedKeys.SequenceEqual(["branding.server_name"]),
                    "a save after an edit was undone in place and another admin's write is made over the current version (no 412) and sends only the edit");
                // A write the page never read is still refused, and keeps the draft for review.
                server.Version++; nameBox.Text = "Over an unseen write"; await Until(() => controller.State.DirtyKeys.Contains("branding.server_name"), "edit over an unseen write");
                var refusalsBeforeUnseen = server.Refusals;
                await Click(page, "GeneralSave"); await Until(() => controller.State.Conflict, "unseen write refused");
                Check(server.Refusals == refusalsBeforeUnseen + 1 && controller.State.DirtyKeys.Contains("branding.server_name"), "a save over a settings change the page never read is refused and the draft stays");
                server.EnforceVersion = false; await Reload(false);
                Check(controller.State.DirtyKeys.Count == 0 && !controller.State.Conflict, "reloading clears the conflict");
            }

            var name = Input(page, "branding.server_name");
            name.Text = "First saved name";
            await Until(() => controller.State.DirtyKeys.Count > 0, "identity dirty");
            server.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = server.Saves;
            await Click(page, "GeneralSave"); await Until(() => controller.State.Saving && server.Saves > saves, "save in flight");
            await Until(() => !toggle.IsEnabled && !Button(page, "GeneralSave").IsEnabled, "save state rendered in native controls");
            Check(!await page.CanLeaveAsync(), "navigation cannot discard or depart during a current save");
            Check(!toggle.IsEnabled, "Redis editor is frozen during save");
            name.Text = "Later unsaved name";
            await Until(() => controller.State.Values["branding.server_name"] == "Later unsaved name", "native edit reached the page controller");
            server.SaveGate.SetResult(); server.SaveGate = null;
            await Until(() => !controller.State.Saving && controller.State.DirtyKeys.Count > 0, "late edit retained");
            await Task.Delay(150);
            Check(name.Text == "Later unsaved name" && DocumentTitle.AppName == "First saved name", "late editor value stays dirty while branding uses acknowledged value");
            Check(server.LastIfMatch != null && server.Saves == saves + 1, "native Save sends one version-guarded write");

            shell.Navigate(new(AdminRoute.Insights));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            await CaptureAsync(dialog, "general-discard-dialog.png");
            var cancelButton = DialogButton(dialog, "Cancel");
            var discardButton = DialogButton(dialog, "Discard");
            foreach (var action in new[] { cancelButton, discardButton })
                Program.Log($"TRACE dialog {action.Name} parent={action.Parent?.GetType().Name} visualParent={VisualTreeHelper.GetParent(action)?.GetType().Name} width={action.ActualWidth} height={action.ActualHeight} column={Grid.GetColumn(action)}");
            Check(cancelButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X < discardButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X,
                "dirty dialog places Cancel before Discard");
            Check(cancelButton.ActualWidth < 140 && discardButton.ActualWidth < 140 && cancelButton.ActualHeight == 36 && discardButton.ActualHeight == 36,
                "dirty dialog actions remain content-sized with reference height");
            Check(cancelButton.FocusState != FocusState.Unfocused, "dirty dialog starts on the non-destructive Cancel action");
            Invoke(DialogButton(dialog, "Cancel")); await Task.Delay(150);
            Check(ReferenceEquals(inner.Content, page) && controller.State.DirtyKeys.Count > 0, "Cancel retains the General page and its draft");
            await Click(page, "GeneralDiscard");
            await Until(() => controller.State.DirtyKeys.Count == 0, "discard settled");
            await Until(() => name.Text == "First saved name", "discard rendered acknowledged identity");
            Check(name.Text == "First saved name", "Discard restores acknowledged identity");

            foreach (var (width, height) in new[] { (1440, 900), (1440, 600), (1920, 600), (800, 600) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(200); outer.UpdateLayout();
                var scroller = (ScrollViewer)Field(page, "_scroller")!;
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"General has no horizontal scroll overflow at {width}x{height}");
                var viewportLeft = scroller.TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
                var available = scroller.ActualWidth - 2 * gutter;
                var expectedLeft = viewportLeft + gutter + Math.Max(0, available - AdminShellPage.ContentMaxWidth) / 2;
                var bodyLeft = ((FrameworkElement)Field(page, "_body")!).TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var scrollContent = (FrameworkElement)scroller.Content;
                Program.Log($"TRACE settings width={width} viewport={scroller.ActualWidth} bodyLeft={bodyLeft} expectedLeft={expectedLeft} contentWidth={scrollContent.ActualWidth} desired={scrollContent.DesiredSize.Width} margin={scrollContent.Margin} alignment={scrollContent.HorizontalAlignment} presenter={scroller.HorizontalContentAlignment}");
                Check(Math.Abs(bodyLeft - expectedLeft) < 1.5, $"General settings column starts at the shared frame gutter at {width}x{height}");
                foreach (var edge in new[] { 8d, scroller.ActualWidth - 18 })
                {
                    var point = scroller.TransformToVisual(null).TransformPoint(new(edge, scroller.ActualHeight / 2));
                    DependencyObject? target = VisualTreeHelper.FindElementsInHostCoordinates(point, outer).FirstOrDefault();
                    while (target != null && target is not ScrollViewer) target = VisualTreeHelper.GetParent(target);
                    Check(ReferenceEquals(target, scroller), $"General side gutter has the page scrolling ancestor at {width}x{height}");
                }
                foreach (var control in Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("General_", StringComparison.Ordinal)))
                {
                    if (AutomationProperties.GetLabeledBy(control) is not FrameworkElement label) continue;
                    DependencyObject? row = VisualTreeHelper.GetParent(control);
                    while (row != null && row.GetType().Name != "AdminSettingsRow") row = VisualTreeHelper.GetParent(row);
                    if (row is UIElement fieldRow)
                        Check(label.TransformToVisual(fieldRow).TransformPoint(new(0, 0)).X <= 15, "General field labels stay on their row's left edge, including toggle rows");
                }
                foreach (var control in Descendants(page).OfType<TextBox>().Where(c => c.IsLoaded))
                {
                    var point = control.TransformToVisual(page).TransformPoint(new(0, 0));
                    Check(point.X >= -1 && point.X + control.ActualWidth <= page.ActualWidth + 1, "General text field stays inside the page viewport");
                    Check(AutomationProperties.GetName(control).Length > 0, "General input has an accessible name");
                    Check(control.BorderBrush is SolidColorBrush { Color.A: 64 }, "General input uses the official settings-row border opacity");
                }
                name.Text = "Layout draft"; await Task.Delay(100);
                await CaptureAsync(outer, $"general-{width}x{height}.png");
                await Click(page, "GeneralDiscard"); await Task.Delay(50);
            }

            toggle.IsOn = true; await Task.Delay(100);
            if (toggle.IsOn)
            {
                var action = Button(page, "GeneralCheckRedis");
                var controlScroller = (ScrollViewer)Field(page, "_scroller")!;
                outer.UpdateLayout(); controlScroller.ChangeView(null, controlScroller.ScrollableHeight, null, true); await Task.Delay(100);
                Check(action.TransformToVisual(page).TransformPoint(new(0, 0)).Y < page.ActualHeight, "connection button is on screen for visual state captures");
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                {
                    action.IsEnabled = state != "Disabled";
                    VisualStateManager.GoToState(action, state, false); await Task.Delay(50);
                    var border = Descendants(action).OfType<Border>().Single(b => b.Name == "SettingsButtonRoot");
                    Check(border.CornerRadius.TopLeft == 10 && border.ActualWidth > border.ActualHeight, $"connection button retains rectangular backdrop in {state}");
                    await CaptureAsync(outer, $"general-control-{state}.png");
                }
                action.IsEnabled = true; action.Focus(FocusState.Keyboard); await Task.Delay(50);
                Check(action.FocusState == FocusState.Keyboard && action.UseSystemFocusVisuals, "connection button exposes keyboard focus");
                await CaptureAsync(outer, "general-control-keyboard-focus.png");
            }
            // The switch was staged Off together with another edit; the environment then takes the Redis connection over. The locked switch must
            // follow the environment (On, URL and database rows shown), not the stale choice (official RedisGroup clears the override).
            server.Managed = false; server.RedisConfigured = true; await controller.ReloadAsync(false);
            await Until(() => toggle.IsOn && toggle.IsEnabled && controller.State.DirtyKeys.Count == 0, "Redis saved On and editable");
            toggle.IsOn = false;
            await Until(() => !toggle.IsOn && controller.State.DirtyKeys.Contains("redis.url"), "Redis switch-off staged");
            name.Text = "Edited alongside Redis off";
            await Until(() => controller.State.DirtyKeys.Contains("branding.server_name"), "second draft staged next to the Redis switch-off");
            server.Managed = true; await controller.ReloadAsync(true);
            var redisUrlBox = Descendants(page).OfType<PasswordBox>().Single();
            var redisDbBox = Input(page, "redis.db");
            await Until(() => !toggle.IsEnabled && toggle.IsOn && EnvironmentNoteProbe.EffectivelyShown(redisUrlBox, page) && EnvironmentNoteProbe.EffectivelyShown(redisDbBox, page),
                "the managed Redis switch reads On and the URL and database rows are shown");
            Check(controller.State.DirtyKeys.SequenceEqual(["branding.server_name"]), "only the unrelated draft survives the environment taking Redis over");
            server.Values["clientip.trusted_proxies"] = "10.0.0.0/8";
            server.Managed = true; server.RedisConfigured = true; await controller.ReloadAsync(false); await Task.Delay(100);
            Check(!toggle.IsEnabled && !Input(page, "clientip.trusted_proxies").IsEnabled, "environment-managed Redis and trusted proxies cannot be edited");
            Check(EnvironmentNoteProbe.Verify(page, Input(page, "clientip.trusted_proxies"), EnvironmentNoteProbe.Named("SILO_TRUSTED_PROXIES")) is null,
                "the managed trusted-proxies row shows the official note naming the variable and carries it in its help text");
            Check(EnvironmentNoteProbe.Verify(page, toggle, EnvironmentNoteProbe.Named("REDIS_URL")) is null, "the managed Redis switch shows the note naming REDIS_URL and carries it in its help text");
            Check(EnvironmentNoteProbe.VerifyEditable(Input(page, "branding.server_name")) is null, "an editable General row carries no environment note");
            server.NamedVariables = false; await controller.ReloadAsync(false);
            await Until(() => EnvironmentNoteProbe.Verify(page, Input(page, "clientip.trusted_proxies"), EnvironmentNoteProbe.Unnamed) is null, "the unnamed note is rendered for the older server");
            Check(EnvironmentNoteProbe.Verify(page, Input(page, "clientip.trusted_proxies"), EnvironmentNoteProbe.Unnamed) is null,
                "an older server that names no variable still locks the row and shows the official field message");
            server.NamedVariables = true; await controller.ReloadAsync(false);
            await Until(() => EnvironmentNoteProbe.Verify(page, Input(page, "clientip.trusted_proxies"), EnvironmentNoteProbe.Named("SILO_TRUSTED_PROXIES")) is null, "the named note is rendered again");
            server.SensitiveFailure = true; await controller.ReloadAsync(false); await Task.Delay(100);
            Check(controller.State.SensitiveStatusError && !toggle.IsEnabled, "protected-status failure holds Redis behind its alert");
            var proxies = Input(page, "clientip.trusted_proxies");
            Check(!proxies.IsEnabled && proxies.Text == "10.0.0.0/8" &&
                AutomationProperties.GetHelpText(proxies).StartsWith(AdminText.T("configure.native.settings.managedStatusUnavailable"), StringComparison.Ordinal) &&
                AutomationProperties.GetHelpText(proxies).Contains(EnvironmentNoteProbe.Named("SILO_TRUSTED_PROXIES"), StringComparison.Ordinal),
                "failed protected status keeps environment-supplied trusted proxies read-only with an unavailable notice and the last known note");
            controller.Set("clientip.trusted_proxies", "127.0.0.1");
            Check(!controller.State.DirtyKeys.Contains("clientip.trusted_proxies"), "unknown trusted-proxy management rejects retained edit actions");
            name.Text = "Non Redis change"; saves = server.Saves;
            await Until(() => Button(page, "GeneralSave").IsEnabled, "unrelated save enabled after field edit");
            await Click(page, "GeneralSave");
            await Until(() => server.Saves > saves && !controller.State.Saving, "unrelated save after status failure");
            Check(controller.State.DirtyKeys.Count == 0, "unrelated fields remain saveable when protected status fails");
            Check(server.LastSavedKeys.SequenceEqual(new[] { "branding.server_name" }) && server.Values["clientip.trusted_proxies"] == "10.0.0.0/8",
                "failed protected status excludes trusted proxies and Redis from the published settings PUT");
            await CaptureAsync(outer, "general-sensitive-unavailable.png");
            server.Managed = false; server.SensitiveFailure = false; await controller.ReloadAsync(false); await Task.Delay(100);
            Check(proxies.IsEnabled, "trusted proxies become editable only after management status is available");
            server.Conflict = true; name.Text = "Conflict draft"; await Click(page, "GeneralSave");
            await Until(() => controller.State.Conflict, "conflict shown"); await Task.Delay(100);
            Check(!Button(page, "GeneralSave").IsEnabled && name.Text == "Conflict draft", "stale version disables Save and retains the draft");
            server.Conflict = false; await Click(page, "GeneralReload");
            await Until(() => !controller.State.Conflict && !controller.State.Loading, "explicit reload");
            Check(controller.State.DirtyKeys.Count > 0, "explicit reload preserves draft for review");
            server.ReadForbidden = true; await controller.ReloadAsync(true); await Task.Delay(100);
            Check(!name.IsEnabled && !Button(page, "GeneralSave").IsEnabled && Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Permission to manage", StringComparison.Ordinal)),
                "permission denial has an actionable state and disables editing and saving");
            var reloadButton = Button(page, "GeneralReload");
            reloadButton.Focus(FocusState.Keyboard);
            await Click(page, "GeneralReload");
            await Until(() => !controller.State.Loading && controller.State.Failure == AdminFailureKind.Forbidden, "rejected reload complete");
            await Task.Delay(100);
            Check(ReferenceEquals(reloadButton, Button(page, "GeneralReload")) && reloadButton.FocusState == FocusState.Keyboard,
                "a repeated failed Reload preserves the native button and keyboard focus");
            var errorText = Descendants(page).OfType<TextBlock>().First(t => t.Text.StartsWith("Permission to manage", StringComparison.Ordinal));
            Check(AutomationProperties.GetLiveSetting(errorText) == AutomationLiveSetting.Assertive &&
                FrameworkElementAutomationPeer.CreatePeerForElement(errorText)?.GetName() == errorText.Text,
                "error text has a named assertive automation peer");
            server.ReadForbidden = false; await controller.ReloadAsync(true); await Task.Delay(100);
            Check(name.IsEnabled && name.Text == "Conflict draft", "restored access reloads without losing the retained draft");
            shell.Navigate(new(AdminRoute.Insights));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "discard navigation dialog");
            Invoke(DialogButton((ContentDialog)Field(page, "_dialog")!, "Discard"));
            await Until(() => inner.Content is AdminInsightsPage, "discard permitted navigation");
            Check(controller.State.Values.Count == 0, "accepting Discard navigates and retires the page controller");
            inner.GoBack(); await Until(() => inner.Content is AdminGeneralSettingsPage { Controller.State.Loaded: true }, "Back restored production General");
            page = (AdminGeneralSettingsPage)inner.Content; controller = page.Controller!; name = Input(page, "branding.server_name");
            await Until(() => name.Text.Length > 0, "Back fields rendered");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "General Back navigation keeps transitions suppressed");
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" }); await Task.Delay(150);
            Check(controller.State.Values.Count == 0 && name.Text.Length == 0, "authority retirement clears native private values and staged edits");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
            DocumentTitle.SetServerName(previousAppName);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("General fixture failures: " + string.Join("; ", Failures));
    }

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static TextBox Input(DependencyObject page, string key) => Descendants(page).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == "General_" + key);
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(c => c.Content?.ToString() == label || AutomationProperties.GetName(c) == label);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Click(DependencyObject page, string id)
    {
        var button = Button(page, id);
        await Until(() => button.IsEnabled && button.IsLoaded, id + " enabled");
        Invoke(button);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
