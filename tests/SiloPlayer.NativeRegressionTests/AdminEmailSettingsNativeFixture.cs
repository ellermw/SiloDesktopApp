using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

/// <summary>Production Configure > Email registration and Frame entry using synthetic server data only.</summary>
internal static class AdminEmailSettingsNativeFixture
{
    private const string Password = "email.smtp_password";
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
            ["branding.server_name"] = "Synthetic Silo", ["redis.db"] = "0",
            ["email.enabled"] = "true", ["email.from_address"] = "silo@example.com", ["email.from_name"] = "Silo",
            ["email.smtp_host"] = "smtp.example.com", ["email.smtp_port"] = "587", ["email.smtp_security"] = "", ["email.smtp_username"] = "mailer",
        };
        internal int Saves, Tests, Version = 1;
        internal bool PasswordConfigured = true, SensitiveFailure, Managed, Conflict, ReadForbidden;
        /// <summary>False models an older server that sends the managed list but no managed_by_env_vars map.</summary>
        internal bool NamedVariables = true;
        internal TaskCompletionSource? SaveGate, TestGate;
        internal string TestReply = """{"ok":true,"duration_ms":7}""";
        internal string? LastIfMatch, LastPut, LastTest;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (ReadForbidden && path.EndsWith("/effective", StringComparison.Ordinal)) return Json(new { type = "permission_denied", title = "Forbidden", status = 403 }, HttpStatusCode.Forbidden);
            if (request.Method == HttpMethod.Post && path == "/api/v2/admin/email/test")
            {
                Tests++; LastTest = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (TestGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TestReply, Encoding.UTF8, "application/json") };
            }
            object body;
            if (request.Method == HttpMethod.Put)
            {
                Saves++; LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                LastPut = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var data = JsonDocument.Parse(LastPut);
                if (SaveGate is { } pending) await pending.Task.WaitAsync(cancellationToken);
                if (Conflict) return Json(new { type = "precondition_failed", title = "Settings changed", status = 412 }, HttpStatusCode.PreconditionFailed);
                var updated = new Dictionary<string, string>();
                foreach (var entry in data.RootElement.GetProperty("values").EnumerateObject())
                {
                    if (entry.Name == Password) PasswordConfigured = entry.Value.GetString()!.Length > 0;
                    else updated[entry.Name] = Values[entry.Name] = entry.Value.GetString()!;
                }
                Version++;
                body = new { values = updated, restart_required = false };
            }
            else if (path.EndsWith("/sensitive-status", StringComparison.Ordinal))
            {
                if (SensitiveFailure) return Json(new { title = "Status unavailable", status = 503 }, HttpStatusCode.ServiceUnavailable);
                body = Managed && NamedVariables
                    ? new { configured = PasswordConfigured ? new[] { Password } : [], managed_by_env = new[] { Password }, managed_by_env_vars = new Dictionary<string, string> { [Password] = "SILO_SMTP_PASSWORD" } }
                    : (object)new { configured = PasswordConfigured ? new[] { Password } : [], managed_by_env = Managed ? new[] { Password } : [] };
            }
            else body = path switch
            {
                "/api/v2/admin/settings/effective" => Values,
                "/api/v2/admin/settings/restart-keys" => new { keys = new[] { "email.smtp_host" }, prefixes = Array.Empty<string>() },
                "/api/v2/admin/server/status" => new { restart_required = false, restart_mark_count = 0, restart_requested = false },
                "/api/v2/admin/system/build" => new { display = "synthetic", available = true, build_number = 1 },
                _ => new { },
            };
            return Json(body);
        }
        private HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"email-fixture-v{Version}\""); return response;
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
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        Frame? outer = null;
        try
        {
            field.SetValue(null, services);
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            var resolved = AdminShellPage.TryOpenHref("/admin/settings/email");
            if (resolved is not { Kind: AdminRouteResolutionKind.Route })
                throw new InvalidOperationException("Email is not available through the production Configure registration.");
            for (var i = 0; i < 160 && outer.Content is not AdminShellPage; i++) await Task.Delay(25);
            if (outer.Content is not AdminShellPage shell) throw new InvalidOperationException("Email did not enter the production Admin shell.");
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            for (var i = 0; i < 160 && inner.Content?.GetType().Name != "AdminEmailSettingsPage"; i++) await Task.Delay(25);
            if (inner.Content is not AdminEmailSettingsPage page) throw new InvalidOperationException("Email did not activate its production WinUI Page.");
            await Until(() => page.Controller?.State.Loaded == true && Input(page, "email.from_address").Text == "silo@example.com", "Email fields rendered");
            var controller = page.Controller!;
            Check(true, "Email activates through production registration, shell and Frame");
            Check(!Descendants(outer).OfType<TextBlock>().Any(t => t.Text.StartsWith("web.", StringComparison.Ordinal) || t.Text.StartsWith("configure.", StringComparison.Ordinal)),
                "rendered Email and navigation labels have catalog text");
            var shellText = Descendants(shell).OfType<TextBlock>().Where(t => !IsInside(t, page)).Select(t => t.Text).ToArray();
            Check(shellText.Contains("Integrations") && shellText.Contains("Email"), "sidebar shows the Integrations topic with its Email leaf");
            Check(AdminNavigation.SearchGroups("smtp", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(g => g.Pages).Single().Route == AdminRoute.EmailSettings,
                "registered Email is searchable through its official keywords");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Email navigation keeps page transitions suppressed");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Mail server") &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Silo sends invitations, password resets, and notification emails through it."),
                "one Mail server group with its official description");
            var restartBadges = Descendants(page).OfType<Border>().Where(b => AutomationProperties.GetName(b) == "Restart" && b.Visibility == Visibility.Visible).ToArray();
            Check(restartBadges.Length == 1, "restart chip appears only on the server-marked restart field");

            // Enable toggle never hides any row.
            var toggle = Descendants(page).OfType<ToggleSwitch>().Single(t => AutomationProperties.GetAutomationId(t) == "Email_email.enabled");
            Check(AutomationProperties.GetName(toggle) == "Send email from this server", "enable toggle carries the official label");
            toggle.IsOn = false; await Until(() => controller.State.DirtyKeys.Contains("email.enabled"), "toggle off staged");
            await Task.Delay(100);
            var editors = Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Email_", StringComparison.Ordinal)).ToArray();
            Check(editors.Length == 8 && editors.All(c => c.Visibility == Visibility.Visible && c.IsLoaded && c.ActualHeight > 0),
                "all eight Email rows stay visible while sending is off");
            toggle.IsOn = true; await Until(() => controller.State.DirtyKeys.Count == 0, "toggle restored");

            // Encryption select: display-only STARTTLS default.
            var select = Descendants(page).OfType<ComboBox>().Single();
            Check((select.SelectedItem as ComboBoxItem)?.Content as string == "STARTTLS" && controller.State.DirtyKeys.Count == 0,
                "empty saved encryption displays STARTTLS without staging an edit");
            Check(select.Items.Cast<ComboBoxItem>().Select(i => i.Content as string).SequenceEqual(["STARTTLS", "TLS (implicit)", "None (insecure)"]),
                "encryption options use the official labels and order");
            select.IsDropDownOpen = true; await Task.Delay(300);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).FirstOrDefault();
            Check(popup?.Child != null, "encryption dropdown opens its popup");
            if (popup?.Child is FrameworkElement popupChild)
            {
                var items = Descendants(popupChild).OfType<ComboBoxItem>().ToArray();
                Check(items.Length == 3 && items.All(i => i.CornerRadius.TopLeft is > 0 and <= 8), "dropdown items keep bounded corner radii");
                if (items.Length == 3) { VisualStateManager.GoToState(items[1], "PointerOver", false); await Task.Delay(50); }
                var surface = Descendants(popupChild).OfType<Border>().FirstOrDefault(b => b.Name == "PopupBorder") ?? (FrameworkElement)VisualTreeHelper.GetParent(items[0]);
                Program.Log($"TRACE popup surface={surface.GetType().Name}/{surface.Name} size={surface.ActualWidth}x{surface.ActualHeight}");
                await CaptureAsync(surface, "email-select-popup.png");
            }
            select.IsDropDownOpen = false; await Task.Delay(150);
            await SelectItem(select, 1);
            await Until(() => controller.State.DirtyKeys.Contains("email.smtp_security"), "TLS staged");
            Check(controller.State.Values["email.smtp_security"] == "tls", "selecting TLS stages tls");
            await SelectItem(select, 0);
            await Until(() => controller.State.DirtyKeys.Count == 0, "STARTTLS reverts");
            Check(controller.State.Values["email.smtp_security"] == "", "returning to STARTTLS clears the edit without writing the default");

            // Saved password: mask, explicit clear/keep, replace and erase-to-keep.
            var secret = Descendants(page).OfType<PasswordBox>().Single();
            var action = Button(page, "EmailPasswordAction");
            Check(secret.PlaceholderText == "••••••••••••" && secret.Password == "" && AutomationProperties.GetName(secret) == "Password",
                "saved password shows only the masked placeholder");
            Check(action.Visibility == Visibility.Visible && AutomationProperties.GetName(action) == "Clear saved value",
                "saved password offers an explicit Clear saved value action");
            Check(action.TransformToVisual(page).TransformPoint(new(0, 0)).X < secret.TransformToVisual(page).TransformPoint(new(0, 0)).X,
                "secret action sits before the password input");
            Check(!Descendants(page).OfType<Button>().Any(b => (b.Content as string)?.Contains("Replace") == true), "there is no Replace step");
            action.Focus(FocusState.Keyboard);
            Invoke(action);
            await Until(() => controller.State.DirtyKeys.Contains(Password), "clear staged");
            await Task.Delay(100);
            Check(ReferenceEquals(action, Button(page, "EmailPasswordAction")) && AutomationProperties.GetName(action) == "Keep saved value" && action.FocusState == FocusState.Keyboard,
                "staged clear turns the same focused button into Keep saved value");
            Check(secret.PlaceholderText == "Will be cleared on save" &&
                Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Save clears the stored value; type to set a new one instead."),
                "staged clear explains what Save does");
            await CaptureAsync(outer, "email-password-clear-staged.png");
            Invoke(action);
            await Until(() => controller.State.DirtyKeys.Count == 0, "keep restored");
            await Task.Delay(100);
            Check(AutomationProperties.GetName(action) == "Clear saved value", "Keep saved value withdraws the staged clear");
            secret.Password = "synthetic-replacement";
            await Until(() => controller.State.DirtyKeys.Contains(Password), "replacement staged");
            await Task.Delay(100);
            Check(action.Visibility == Visibility.Collapsed, "a typed replacement hides the clear action");
            secret.Password = "";
            await Until(() => controller.State.DirtyKeys.Count == 0, "erasing keeps saved");
            await Task.Delay(100);
            Check(action.Visibility == Visibility.Visible && controller.State.PasswordConfigured, "erasing a replacement keeps the saved password");
            // Typing while a render is still queued: PasswordChanged arrives later, so the render must not erase it.
            Invoke(action); secret.Password = "raced-replacement";
            await Until(() => controller.State.Values[Password] == "raced-replacement", "typing during a queued render reaches the controller", 3000).ContinueWith(t => t.Exception == null);
            await Task.Delay(150);
            Check(secret.Password == "raced-replacement" && controller.State.Values[Password] == "raced-replacement", "a queued render does not overwrite password typing in flight");
            secret.Password = ""; await Until(() => controller.State.DirtyKeys.Count == 0, "erase after race keeps saved");
            await Task.Delay(100);
            Invoke(action); await Until(() => controller.State.DirtyKeys.Contains(Password), "clear for discard");
            await Click(page, "EmailDiscard");
            await Until(() => controller.State.DirtyKeys.Count == 0, "discard withdrew clear");
            Check(true, "Discard withdraws a staged password clear");

            // Guarded save with credential freeze and a late nonsecret edit.
            var name = Input(page, "email.from_name");
            secret.Password = "synthetic-new-pass";
            await Until(() => controller.State.DirtyKeys.Contains(Password), "password staged for save");
            name.Text = "First saved name";
            await Until(() => controller.State.DirtyKeys.Contains("email.from_name"), "name dirty");
            server.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var saves = server.Saves;
            await Click(page, "EmailSave"); await Until(() => controller.State.Saving && server.Saves > saves, "save in flight");
            await Until(() => !secret.IsEnabled && !Button(page, "EmailSave").IsEnabled, "save state rendered");
            Check(!secret.IsEnabled && name.IsEnabled, "password is frozen during save while other fields stay editable");
            Check(!await page.CanLeaveAsync(), "navigation cannot discard or depart during a current save");
            name.Text = "Later unsaved name";
            await Until(() => controller.State.Values["email.from_name"] == "Later unsaved name", "late edit reached controller");
            server.SaveGate.SetResult(); server.SaveGate = null;
            await Until(() => !controller.State.Saving, "save settled");
            await Task.Delay(150);
            Check(server.Saves == saves + 1 && server.LastIfMatch == "\"email-fixture-v1\"", "native Save sends one strong version-guarded write");
            Check(server.LastPut != null && server.LastPut.Contains(Password) && server.LastPut.Contains("email.from_name") && !server.LastPut.Contains("branding."),
                "the write carries only the dirty Email keys");
            Check(name.Text == "Later unsaved name" && controller.State.PersistedValues["email.from_name"] == "First saved name" && controller.State.DirtyKeys.Contains("email.from_name"),
                "late editor value stays dirty over the acknowledged canonical value");
            Check(secret.Password == "" && controller.State.PasswordConfigured && !controller.State.DirtyKeys.Contains(Password), "acknowledged password replacement clears its draft");

            // Test email through SAVED settings while a draft exists.
            var recipient = Input(page, "EmailTestRecipient", prefix: false);
            var send = Button(page, "EmailSendTest");
            Check(AutomationProperties.GetName(recipient) == "Test email recipient" && recipient.PlaceholderText == "you@example.com", "test recipient has its official name and hint");
            Check(!send.IsEnabled, "Send test is disabled for an empty recipient");
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Save your changes first; the test uses the saved settings." && t.Visibility == Visibility.Visible),
                "saved-settings hint is shown while a draft exists");
            recipient.Text = "admin@siloserver"; await Until(() => send.IsEnabled, "send enabled for nonempty text");
            Invoke(send); await Task.Delay(150);
            Check(server.Tests == 0 && TestResult(page).Text == "Enter a valid email address, like name@example.com", "malformed recipient reports the official message before any request");
            recipient.Text = "  admin@example.test  ";
            server.TestGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var testScroller = (ScrollViewer)Field(page, "_scroller")!;
            testScroller.ChangeView(null, testScroller.ScrollableHeight, null, true); await Task.Delay(150);
            send.Focus(FocusState.Keyboard);
            Invoke(send);
            await Until(() => server.Tests == 1 && controller.State.TestPending, "test pending");
            await Until(() => !send.IsEnabled, "send disabled while pending");
            Check(Descendants(send).OfType<Viewbox>().Any(v => v.RenderTransform is RotateTransform), "pending Send shows the spinner icon");
            await CaptureAsync(outer, "email-test-pending.png");
            await controller.SendTestAsync("admin@example.test");
            server.TestGate.SetResult(); server.TestGate = null;
            await Until(() => !controller.State.TestPending, "test settled");
            await Task.Delay(150);
            Check(server.Tests == 1 && server.LastTest == """{"to":"admin@example.test"}""", "one test command with only the trimmed recipient");
            Check(TestResult(page).Text == "Delivered to the mail server in 7ms." && TestResult(page).Foreground is SolidColorBrush { Color.G: 0xB9 },
                "success shows the official green duration text");
            Check(controller.State.DirtyKeys.Contains("email.from_name"), "the test leaves the staged draft untouched");
            Check(ReferenceEquals(send, Button(page, "EmailSendTest")), "Send test is not rebuilt across pending and result renders");
            server.TestReply = """{"ok":false,"duration_ms":30,"message":"535 synthetic authentication failed"}""";
            Invoke(send); await Until(() => server.Tests == 2 && !controller.State.TestPending, "rejected test");
            await Task.Delay(150);
            Check(TestResult(page).Text == "535 synthetic authentication failed" && TestResult(page).Foreground is SolidColorBrush { Color.R: 0xF5 },
                "ok:false shows the server message in the warning color");
            server.TestReply = """{"ok":false,"duration_ms":30}""";
            Invoke(send); await Until(() => server.Tests == 3 && !controller.State.TestPending, "rejected without message");
            await Task.Delay(150);
            Check(TestResult(page).Text == "Test failed.", "ok:false without a message uses the official fallback");
            server.TestReply = """{"ok":true}""";
            Invoke(send); await Until(() => server.Tests == 4 && !controller.State.TestPending, "malformed ack");
            await Task.Delay(300);
            Check(server.Tests == 4 && TestResult(page).Text.StartsWith("The test may or may not have been sent", StringComparison.Ordinal),
                "malformed acknowledgement is an unknown outcome and is not resent");
            await CaptureAsync(outer, "email-test-unknown.png");
            server.TestReply = """{"ok":true,"duration_ms":7}""";
            Invoke(send); await Until(() => server.Tests == 5 && !controller.State.TestPending, "fresh test");
            await Click(page, "EmailDiscard"); await Until(() => controller.State.DirtyKeys.Count == 0, "discard after tests");
            await Task.Delay(100);
            Check(Descendants(page).OfType<TextBlock>().Any(t => t.Text == "Save your changes first; the test uses the saved settings." && t.Visibility == Visibility.Visible),
                "saved-settings hint stays visible when the page is clean");

            // Dirty departure dialog.
            name.Text = "Dialog draft"; await Until(() => controller.State.DirtyKeys.Count > 0, "dialog draft");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "dirty leave dialog");
            var dialog = (ContentDialog)Field(page, "_dialog")!;
            await CaptureAsync(dialog, "email-discard-dialog.png");
            var cancelButton = DialogButton(dialog, "Cancel");
            var discardButton = DialogButton(dialog, "Discard");
            Check(cancelButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X < discardButton.TransformToVisual(dialog).TransformPoint(new(0, 0)).X,
                "dirty dialog places Cancel before Discard");
            Check(cancelButton.FocusState != FocusState.Unfocused, "dirty dialog starts on Cancel");
            Invoke(cancelButton); await Task.Delay(150);
            Check(ReferenceEquals(inner.Content, page) && controller.State.DirtyKeys.Count > 0, "Cancel retains the Email page and its draft");

            // Layout at the required viewports.
            foreach (var (width, height) in new[] { (1440, 900), (1440, 600), (1920, 600), (800, 600) })
            {
                window.AppWindow.ResizeClient(new(width, height)); await Task.Delay(250); outer.UpdateLayout();
                var scroller = (ScrollViewer)Field(page, "_scroller")!;
                scroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                var line = (FrameworkElement)VisualTreeHelper.GetParent(recipient);
                var testRow = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(line));
                Program.Log($"TRACE recipient width={width} line={line.ActualWidth} row={testRow.ActualWidth} input={recipient.ActualWidth}");
                Check(testRow is Border && Math.Abs(line.ActualWidth - Math.Min(448, testRow.ActualWidth)) < 2 && recipient.ActualWidth > 150,
                    $"test recipient row fills up to the official 448px width at {width}x{height}");
                Check(scroller.ExtentWidth <= scroller.ViewportWidth + 1, $"Email has no horizontal scroll overflow at {width}x{height}");
                var viewportLeft = scroller.TransformToVisual(null).TransformPoint(new(0, 0)).X;
                var gutter = width >= 1280 ? 40 : width >= 1024 ? 32 : width >= 640 ? 24 : 16;
                var available = scroller.ActualWidth - 2 * gutter;
                var expectedLeft = viewportLeft + gutter + Math.Max(0, available - AdminShellPage.ContentMaxWidth) / 2;
                var bodyLeft = ((FrameworkElement)Field(page, "_body")!).TransformToVisual(null).TransformPoint(new(0, 0)).X;
                Program.Log($"TRACE email width={width} viewport={scroller.ActualWidth} bodyLeft={bodyLeft} expectedLeft={expectedLeft}");
                Check(Math.Abs(bodyLeft - expectedLeft) < 1.5, $"Email settings column starts at the shared frame gutter at {width}x{height}");
                foreach (var edge in new[] { 8d, scroller.ActualWidth - 18 })
                {
                    var point = scroller.TransformToVisual(null).TransformPoint(new(edge, scroller.ActualHeight / 2));
                    DependencyObject? target = VisualTreeHelper.FindElementsInHostCoordinates(point, outer).FirstOrDefault();
                    while (target != null && target is not ScrollViewer) target = VisualTreeHelper.GetParent(target);
                    Check(ReferenceEquals(target, scroller), $"Email side gutter has the page scrolling ancestor at {width}x{height}");
                }
                foreach (var control in Descendants(page).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Email_", StringComparison.Ordinal)))
                {
                    if (AutomationProperties.GetLabeledBy(control) is not FrameworkElement label) continue;
                    DependencyObject? row = VisualTreeHelper.GetParent(control);
                    while (row != null && row.GetType().Name != "AdminSettingsRow") row = VisualTreeHelper.GetParent(row);
                    if (row is UIElement fieldRow)
                        Check(label.TransformToVisual(fieldRow).TransformPoint(new(0, 0)).X <= 15, $"Email label for {AutomationProperties.GetAutomationId(control)} stays on its row's left edge");
                }
                var reachable = Descendants(page).OfType<Control>().Where(c => c.IsLoaded && c.Visibility == Visibility.Visible && c.ActualWidth > 0 &&
                    (c is TextBox or PasswordBox or ComboBox or ToggleSwitch || (c is Button && AutomationProperties.GetAutomationId(c).StartsWith("Email", StringComparison.Ordinal)))).ToArray();
                foreach (var control in reachable)
                {
                    var point = control.TransformToVisual(page).TransformPoint(new(0, 0));
                    Check(point.X >= -1 && point.X + control.ActualWidth <= page.ActualWidth + 1, $"{AutomationProperties.GetAutomationId(control)} stays inside the viewport at {width}x{height}");
                    Check(AutomationProperties.GetName(control).Length > 0, $"{AutomationProperties.GetAutomationId(control)} has an accessible name");
                }
                foreach (var control in Descendants(page).OfType<Control>().Where(c => c is TextBox or PasswordBox or ComboBox && AutomationProperties.GetAutomationId(c).StartsWith("Email_", StringComparison.Ordinal)))
                    Check(control.BorderBrush is SolidColorBrush { Color.A: 64 }, $"{AutomationProperties.GetAutomationId(control)} uses the official settings-row border opacity");
                await CaptureAsync(outer, $"email-{width}x{height}.png");
                scroller.ChangeView(null, scroller.ScrollableHeight, null, true); await Task.Delay(150);
                await CaptureAsync(outer, $"email-{width}x{height}-bottom.png");
                scroller.ChangeView(null, 0, null, true); await Task.Delay(50);
            }
            window.AppWindow.ResizeClient(new(1440, 900)); await Task.Delay(250);

            // Control state shapes: Send test, Clear/Keep, SaveBar.
            var stateScroller = (ScrollViewer)Field(page, "_scroller")!;
            outer.UpdateLayout(); stateScroller.ChangeView(null, stateScroller.ScrollableHeight, null, true); await Task.Delay(150);
            foreach (var id in new[] { "EmailSendTest", "EmailPasswordAction", "EmailSave", "EmailDiscard" })
            {
                var button = Button(page, id);
                if (id == "EmailSendTest") recipient.Text = "admin@example.test";
                foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled" })
                {
                    var wasEnabled = button.IsEnabled;
                    button.IsEnabled = state != "Disabled";
                    VisualStateManager.GoToState(button, state, false); await Task.Delay(50);
                    var border = Descendants(button).OfType<Border>().Single(b => b.Name == "SettingsButtonRoot");
                    Check(border.CornerRadius.TopLeft is >= 10 and <= 25 && border.ActualWidth > border.ActualHeight, $"{id} keeps a bounded non-ellipse backdrop in {state}");
                    await CaptureAsync(outer, $"email-{id}-{state}.png");
                    button.IsEnabled = wasEnabled;
                }
                VisualStateManager.GoToState(button, "Normal", false);
                if (button.IsEnabled)
                {
                    button.Focus(FocusState.Keyboard); await Task.Delay(50);
                    Check(button.FocusState == FocusState.Keyboard && button.UseSystemFocusVisuals, $"{id} exposes keyboard focus");
                    await CaptureAsync(outer, $"email-{id}-keyboard-focus.png");
                }
            }
            select.Focus(FocusState.Keyboard); await Task.Delay(50);
            Check(select.FocusState == FocusState.Keyboard, "encryption select exposes keyboard focus");
            await CaptureAsync(outer, "email-select-keyboard-focus.png");
            stateScroller.ChangeView(null, 0, null, true);

            // Managed and protected-status states.
            // A typed password is dropped when the environment takes the key over; the note names the variable and is in the help text.
            secret.Password = "synthetic-typed-secret";
            await Until(() => controller.State.DirtyKeys.Contains(Password), "typed password staged before the environment takes over");
            server.Managed = true; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(!secret.IsEnabled && Button(page, "EmailPasswordAction").Visibility == Visibility.Collapsed, "environment-managed password cannot be edited or cleared");
            Check(EnvironmentNoteProbe.Verify(page, secret, EnvironmentNoteProbe.Named("SILO_SMTP_PASSWORD")) is null, "the managed password row is disabled, shows the official note naming the variable and carries it in its help text");
            Check(secret.Password == "" && !controller.State.DirtyKeys.Contains(Password) && controller.State.Values[Password] == "", "the typed password is neither shown nor kept nor staged once the environment sets it");
            server.NamedVariables = false; await controller.ReloadAsync(true);
            await Until(() => EnvironmentNoteProbe.Verify(page, secret, EnvironmentNoteProbe.Unnamed) is null, "the unnamed note is rendered for the older server");
            Check(EnvironmentNoteProbe.Verify(page, secret, EnvironmentNoteProbe.Unnamed) is null, "an older server that names no variable still locks the password and shows the official field message");
            server.NamedVariables = true;
            server.Managed = false; server.SensitiveFailure = true; await controller.ReloadAsync(true); await Task.Delay(150);
            var retry = Button(page, "EmailRetryPasswordStatus");
            Check(controller.State.SensitiveStatusError && !secret.IsEnabled && retry.Visibility == Visibility.Visible && retry.IsEnabled &&
                Button(page, "EmailPasswordAction").Visibility == Visibility.Collapsed, "failed password status keeps the credential protected with a retry");
            await CaptureAsync(outer, "email-password-status-unavailable.png");
            Check(Button(page, "EmailSave").IsEnabled, "unrelated draft stays saveable while password status is unavailable");
            server.SensitiveFailure = false; Invoke(retry);
            await Until(() => !controller.State.SensitiveStatusError && !controller.State.Loading, "status retry");
            await Task.Delay(100);
            Check(secret.IsEnabled && Button(page, "EmailPasswordAction").Visibility == Visibility.Visible && name.Text == "Dialog draft",
                "retry restores the credential editor and retains the draft");

            server.Conflict = true; await Click(page, "EmailSave");
            await Until(() => controller.State.Conflict, "conflict shown"); await Task.Delay(100);
            Check(!Button(page, "EmailSave").IsEnabled && name.Text == "Dialog draft", "stale version disables Save and retains the draft");
            server.Conflict = false; await Click(page, "EmailReload");
            await Until(() => !controller.State.Conflict && !controller.State.Loading, "explicit reload");
            Check(controller.State.DirtyKeys.Count > 0, "explicit reload preserves the draft for review");
            server.ReadForbidden = true; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(!name.IsEnabled && !Button(page, "EmailSave").IsEnabled && Descendants(page).OfType<TextBlock>().Any(t => t.Text.StartsWith("Permission to manage", StringComparison.Ordinal)),
                "permission denial disables editing and saving with an actionable message");
            server.ReadForbidden = false; await controller.ReloadAsync(true); await Task.Delay(150);
            Check(name.IsEnabled && name.Text == "Dialog draft", "restored access keeps the retained draft");

            // Departure cancels an owned pending test and clears private state.
            recipient.Text = "admin@example.test";
            server.TestGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var tests = server.Tests;
            await Click(page, "EmailSendTest"); await Until(() => server.Tests > tests && controller.State.TestPending, "pending before departure");
            shell.Navigate(new(AdminRoute.Activity));
            await Until(() => Field(page, "_dialog") is ContentDialog { IsLoaded: true }, "discard navigation dialog");
            Invoke(DialogButton((ContentDialog)Field(page, "_dialog")!, "Discard"));
            await Until(() => inner.Content is AdminActivityPage, "discard permitted navigation");
            server.TestGate.SetResult(); server.TestGate = null; await Task.Delay(150);
            Check(controller.State.Values.Count == 0 && !controller.State.TestPending && recipient.Text.Length == 0 && TestResult(page).Text.Length == 0,
                "departure retires the controller and clears recipient, result and drafts");
            inner.GoBack(); await Until(() => inner.Content is AdminEmailSettingsPage { Controller.State.Loaded: true }, "Back restored production Email");
            page = (AdminEmailSettingsPage)inner.Content; controller = page.Controller!; name = Input(page, "email.from_name");
            recipient = Input(page, "EmailTestRecipient", prefix: false);
            await Until(() => name.Text.Length > 0, "Back fields rendered");
            Check(recipient.Text.Length == 0 && TestResult(page).Text.Length == 0, "Back starts without the previous recipient or result");
            recipient.Text = "admin@example.test"; await Click(page, "EmailSendTest");
            await Until(() => !controller.State.TestPending && controller.State.TestResult != null, "post-back test");
            // PasswordChanged is deferred: retirement must clear typing even before the controller sees it.
            secret = Descendants(page).OfType<PasswordBox>().Single();
            secret.Password = "synthetic-retirement-race";
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic-admin" }); await Task.Delay(200);
            Check(controller.State.Values.Count == 0 && name.Text.Length == 0 && recipient.Text.Length == 0 && TestResult(page).Text.Length == 0,
                "authority retirement clears values, recipient and test result");
            Check(secret.Password.Length == 0, "authority retirement clears password typing awaiting PasswordChanged");
        }
        finally
        {
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            window.Content = previousContent;
            await Task.Delay(100);
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Email fixture failures: " + string.Join("; ", Failures));
    }

    private static bool IsInside(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current)) if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static TextBox Input(DependencyObject page, string key, bool prefix = true) => Descendants(page).OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == (prefix ? "Email_" + key : key));
    private static TextBlock TestResult(DependencyObject page) => Descendants(page).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "EmailTestResult");
    private static Button Button(DependencyObject page, string id) => Descendants(page).OfType<Button>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static Button DialogButton(ContentDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(c => c.Content?.ToString() == label || AutomationProperties.GetName(c) == label);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    /// <summary>Selects through the open dropdown's realized item, as a user choice does.</summary>
    private static async Task SelectItem(ComboBox select, int index)
    {
        select.IsDropDownOpen = true; await Task.Delay(250);
        var item = (ComboBoxItem)select.Items[index];
        var provider = FrameworkElementAutomationPeer.CreatePeerForElement(item)?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
        Program.Log("TRACE select path=" + (provider == null ? "SelectedItem" : "automation"));
        if (provider != null) provider.Select(); else select.SelectedItem = item;
        select.IsDropDownOpen = false; await Task.Delay(150);
    }
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
