using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class AccountLatestNativeFixture
{
    // The reused account capture helper asserts this exact native client height.
    private const int ClientHeight = 740;
    internal static async Task RunAsync(StackPanel parent)
    {
        var serviceField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = serviceField.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://account-latest-fixture.invalid");
        var authApi = new AuthApi(client); var settingsApi = new SettingsApi(client); var catalog = new CatalogApi(client);
        using var auth = new AuthService(client, authApi);
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "account-latest-settings"));
        var theme = new ThemeService(local, settingsApi); var accessibility = new AccessibilityService(local, theme);
        var history = new HistoryImportApi(client); var providers = new WatchProvidersApi(client);
        var vm = new SettingsViewModel(settingsApi, catalog, authApi, history, providers, auth, theme, local, accessibility);
        var navigation = new NavigationService();
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authApi)
            .AddSingleton(settingsApi).AddSingleton(catalog).AddSingleton(history).AddSingleton(providers)
            .AddSingleton(local).AddSingleton(theme).AddSingleton(accessibility).AddSingleton(vm).AddSingleton(navigation)
            .AddSingleton(new RequestsApi(client)).AddSingleton(new RecommendationsApi(client)).AddSingleton(new CollectionsApi(client))
            .AddSingleton(new CardOverlayService(settingsApi)).AddSingleton(new UICustomizationService(settingsApi)).AddSingleton(new ToastService()).BuildServiceProvider();
        var frame = new Frame { Width = 1400, Height = ClientHeight }; navigation.Frame = frame;
        var window = new Window { Content = frame }; frame.Tag = window; serviceField.SetValue(null, services);
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1400, ClientHeight)); window.AppWindow.Show(false); await Task.Delay(160);
            Check(frame.Navigate(typeof(SettingsPage), "Interface"), "Actual latest Settings navigation rejected");
            await Until(() => frame.Content is SettingsPage loaded && (bool)Read(loaded, "_pageInitialized")!);
            var page = (SettingsPage)frame.Content;
            var selected = Environment.GetEnvironmentVariable("SILO_ACCOUNT_LATEST_CASE");
            if (string.IsNullOrWhiteSpace(selected)) selected = null;
            if (selected is not (null or "title-art" or "identities" or "oauth" or "queued-user-refresh" or "network-link" or "shadowed" or "ratings"))
                throw new ArgumentException("Unknown latest-account case; refusing a run with no acceptance checks.");
            if (selected == "queued-user-refresh")
            {
                Click((Button)page.FindName("AccountPasswordTab")); await Until(() => (bool)Read(page, "_signInLoaded")!);
                auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
                await Call(page, "LoadAccountSignInAsync");
                await OAuth(page, auth, wire, frame);
            }
            if (selected is null or "title-art") await TitleArt(page, vm, wire, frame, window);
            if (selected is null or "identities") await Identities(page, auth, wire, frame, window);
            if (selected is null or "oauth") await OAuth(page, auth, wire, frame);
            if (selected is null or "network-link") await NetworkLink(page, auth, wire);
            if (selected is null or "shadowed") await Shadowed(page, wire, frame);
            if (selected is null or "ratings") await Ratings(page, catalog, wire, frame);
            Program.Log("PASS bounded latest account native cases: " + (selected ?? "all"));
        }
        finally
        {
            wire.Pending?.TrySetResult(Reply(new { })); wire.IdentityPending?.TrySetResult(Reply(new { server_id = "fixture-server" }));
            frame.Content = null; await Task.Delay(100); window.Close(); serviceField.SetValue(null, previous);
        }
    }

    private static async Task TitleArt(SettingsPage page, SettingsViewModel vm, Wire wire, Frame frame, Window window)
    {
        await vm.LoadTitleArtAsync(); await Task.Delay(40);
        var toggle = (ToggleSwitch)page.FindName("TitleArtToggle"); var all = (ToggleSwitch)page.FindName("TitleArtAllDevicesToggle");
        Check(vm.TitleArtSupported && !toggle.IsOn && !all.IsOn, "Effective false/device title-art source not represented");
        wire.Batched = false; await vm.LoadTitleArtAsync(); await Task.Delay(30);
        Check(!vm.TitleArtSupported && ((Border)page.FindName("TitleArtSettingsGroup")).Visibility == Visibility.Collapsed, "Title-art offered without batched effective support");
        wire.Batched = true; await vm.LoadTitleArtAsync(); await Task.Delay(30);
        ((Border)page.FindName("TitleArtSettingsGroup")).StartBringIntoView(); await Task.Delay(100);
        var notifications = 0; EventHandler changed = (_, _) => ++notifications; SettingsViewModel.TitleArtPreferenceChanged += changed;
        try
        {
            wire.Pending = NewGate(); toggle.IsOn = true; await Until(() => wire.TitleWrites.Count == 1 && vm.IsSavingTitleArt);
            Check(!toggle.IsEnabled && !all.IsEnabled, "Pending title-art controls permit duplicate writes");
            await Capture(frame, "latest-title-art-pending.png");
            wire.Pending.TrySetResult(Reject("validation_failed", HttpStatusCode.UnprocessableEntity));
            await Until(() => !vm.IsSavingTitleArt); wire.Pending = null;
            Check(!toggle.IsOn && !vm.ShowTitleArt && notifications == 0 && vm.TitleArtErrorMessage is not null, "Rejected title-art switch/event not rolled back");
            toggle.IsOn = true; await Until(() => wire.TitleWrites.Count == 2 && !vm.IsSavingTitleArt);
            Check(vm.ShowTitleArt && notifications == 1, "Title-art retry not authoritative");
            all.IsOn = true; await Until(() => wire.TitleWrites.Count == 3 && !vm.IsSavingTitleArt);
            Check(vm.TitleArtAllDevices && wire.ProfileTitle == true && wire.TitleWrites.Last().Scope == "profile", "All-devices on did not write current profile value");
            all.IsOn = false; await Until(() => wire.TitleWrites.Count == 5 && !vm.IsSavingTitleArt);
            var writes = wire.TitleWrites.TakeLast(2).ToArray();
            Check(writes[0].Method == "PUT" && writes[0].Scope == "profile_device" && writes[0].Value == true && writes[1].Method == "DELETE" && writes[1].Scope == "profile" && wire.ProfileTitle is null, "All-devices off must PUT this device then DELETE profile");
            await Pairs(frame, window, "latest-title-art", (FrameworkElement)page.FindName("TitleArtSettingsGroup"));
            Program.Log("PASS actual title-art capability false flag/pending422 rollback/retry/event/current-value profile enable/PUT-device then DELETE-profile");
        }
        finally { SettingsViewModel.TitleArtPreferenceChanged -= changed; }
    }

    private static async Task Identities(SettingsPage page, AuthService auth, Wire wire, Frame frame, Window window)
    {
        Click((Button)page.FindName("AccountPasswordTab")); await Until(() => (bool)Read(page, "_signInLoaded")!);
        var body = (StackPanel)page.FindName("AccountSignInBody");
        Check(All<TextBlock>(body).Any(text => text.Text == "Sign-in provider (turned off)"), "Disabled linked provider row missing");
        wire.CanUnlink = false; await Call(page, "LoadAccountSignInAsync");
        Check(!Buttons(body).Any(button => Equals(button.Content, "Disconnect")) && All<TextBlock>(body).Any(text => text.Text.Contains("so it stays connected")), "Last sign-in method offered unlink");
        wire.CanUnlink = true; await Call(page, "LoadAccountSignInAsync");
        Click(Buttons(body).Single(button => Equals(button.Content, "Connect Fixture Directory")));
        var password = All<PasswordBox>(body).Single(box => Equals(box.Header, "Silo password"));
        var directory = All<PasswordBox>(body).Single(box => Equals(box.Header, "Fixture Directory password"));
        var username = All<TextBox>(body).Single(box => Equals(box.Header, "Fixture Directory username"));
        password.Password = "fixture-local"; directory.Password = "fixture-directory"; username.Text = "fixture-name";
        var submit = Buttons(body).Single(button => Equals(button.Content, "Connect"));
        foreach (var location in new[] { "body.password", "body.directory_password" })
        {
            wire.Pending = NewGate(); var before = wire.CredentialsWrites; Click(submit); await Until(() => wire.CredentialsWrites == before + 1);
            Check(!submit.IsEnabled && !Buttons(body).Single(button => Equals(button.Content, "Cancel")).IsEnabled && password.IsEnabled && username.IsEnabled, "Directory pending duplicate/draft controls incorrect");
            wire.Pending.TrySetResult(Reject("validation_failed", HttpStatusCode.UnprocessableEntity, location)); await Until(() => submit.IsEnabled); wire.Pending = null;
            Check(password.Password == "fixture-local" && directory.Password == "fixture-directory", "Identity rejection lost editable draft");
            Check(All<TextBlock>(body).Any(text => text.Text == (location == "body.password" ? "That isn't your current Silo password." : "Fixture Directory didn't accept that username and password.")), "Wrong field-specific identity rejection copy");
        }
        Click(submit); await Until(() => wire.CredentialsWrites == 3 && (bool)Read(page, "_signInLoaded")! && !Buttons(body).Any(button => Equals(button.Content, "Connect Fixture Directory")));
        Check(wire.CredentialsBody.GetProperty("username").GetString() == "fixture-name" && wire.RefreshRequests == 0, "Credentials payload or forbidden auth retry incorrect");
        await Pairs(frame, window, "latest-identities", (FrameworkElement)page.FindName("AccountSignInGroup"));
        Click(Buttons(body).First(button => Equals(button.Content, "Disconnect"))); await Until(() => Read(page, "_signInDialog") is ContentDialog);
        var dialog = (ContentDialog)Read(page, "_signInDialog")!; await Until(() => All<Button>(dialog).Any(button => button.Name == "PrimaryButton"));
        wire.Pending = NewGate(); Click(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.Unlinks == 1);
        Check(!dialog.IsPrimaryButtonEnabled && dialog.CloseButtonText == "", "Unlink pending allows duplicate/cancel");
        wire.Pending.TrySetResult(Reject("last_sign_in_method", HttpStatusCode.Conflict)); await Until(() => dialog.IsPrimaryButtonEnabled); wire.Pending = null;
        Check(All<TextBlock>(dialog).Any(text => text.Text.Contains("only way to sign in")), "Unlink rejection discarded dialog/error");
        Click(All<Button>(dialog).Single(button => button.Name == "PrimaryButton")); await Until(() => wire.Unlinks == 2 && Read(page, "_signInDialog") is null);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Impersonation = new() { Active = true } });
        await Until(() => All<TextBlock>(body).Any(text => text.Text.Contains("not while you view")));
        Check(!Buttons(body).Any(button => Equals(button.Content, "Disconnect") || button.Content?.ToString()?.StartsWith("Connect ", StringComparison.Ordinal) == true), "Impersonating session offered mutation");
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); await Call(page, "LoadAccountSignInAsync");
        Program.Log("PASS actual identities/list/last-method/credentials pending local+directory422 retained draft/retry/typed payload/no refresh/unlink rejection retry/impersonation");
    }

    private static async Task Shadowed(SettingsPage page, Wire wire, Frame frame)
    {
        wire.ProfileTitle = true; wire.DeviceTitle = false;
        Click((Button)page.FindName("DevicesTab")); await Until(() => !((ProgressRing)page.FindName("DevicesLoadingRing")).IsActive);
        var device = new UserDevice { DeviceId = "fixture-device", DeviceName = "Fixture device", DevicePlatform = "Windows", ProfileId = "fixture", ProfileName = "Primary", IsCurrentDevice = true, ChangedCount = 1 };
        var host = (StackPanel)page.FindName("DevicesContentHost"); await Call(page, "LoadDeviceDetailAsync", device, host);
        frame.UpdateLayout(); await Task.Delay(100);
        var row = All<Grid>(host).Single(grid => grid.Children.OfType<StackPanel>().Any(panel => panel.Children.OfType<TextBlock>().Any(text => text.Text == "Show title art")));
        Check(All<TextBlock>(row).Any(text => text.Text.Contains("own choice (Off)")) && All<TextBlock>(row).Any(text => text.Text == "Changed here"), "Retained shadowed device choice/Changed here missing");
        var toggle = All<ToggleSwitch>(row).Single(); Check(toggle.IsOn && !toggle.IsEnabled, "Profile-first row displayed stored value or allowed edits");
        row.StartBringIntoView(); await Task.Delay(80); Check(toggle.ActualWidth > 0, "Shadowed effective toggle was not rendered");
        await Capture(frame, "latest-shadowed-device-retained.png");
        var reset = Buttons(row).Single(button => button.Content?.ToString()?.StartsWith("Use ", StringComparison.Ordinal) == true);
        var before = wire.TitleWrites.Count; wire.Pending = NewGate(); Click(reset); await Until(() => wire.TitleWrites.Count == before + 1);
        Check(!reset.IsEnabled, "Pending retained override reset allows duplicates");
        wire.Pending.TrySetResult(Reject("validation_failed", HttpStatusCode.UnprocessableEntity)); await Until(() => reset.IsEnabled); wire.Pending = null;
        Check(wire.DeviceTitle == false && wire.ProfileTitle == true, "Rejected reset changed stored/profile preference");
        Click(reset); await Until(() => wire.TitleWrites.Count == before + 2 && wire.DeviceTitle is null);
        var last = wire.TitleWrites.Last(); Check(last.Method == "DELETE" && last.Scope == "profile_device" && last.Device == "fixture-device" && wire.ProfileTitle == true, "Reset removed profile preference or targeted wrong device");
        await Capture(frame, "latest-shadowed-device-reset.png");
        Program.Log("PASS actual profile-first effective true/retained false/read-only main/Changed here/reset pending rejection retry exact device DELETE");
    }

    private static async Task OAuth(SettingsPage page, AuthService auth, Wire wire, Frame frame)
    {
        Click((Button)page.FindName("AccountPasswordTab")); await Call(page, "LoadAccountSignInAsync");
        var body = (StackPanel)page.FindName("AccountSignInBody"); Uri? start = null;
        typeof(SettingsPage).GetField("_launchAccountSignIn", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page,
            (Func<Uri, Task<bool>>)(uri => { start = uri; Program.Log("TRACE latest OAuth recording browser boundary reached; saved native path=" + uri.AbsolutePath); return Task.FromResult(true); }));
        Click(Buttons(body).Single(button => Equals(button.Content, "Connect Fixture SSO")));
        var password = All<PasswordBox>(body).Single(box => Equals(box.Header, "Silo password")); password.Password = "clicked-password";
        var submittedRevision = Read(page, "_signInRevision");
        wire.IdentityPending = NewGate(); Click(Buttons(body).Single(button => Equals(button.Content, "Continue to Fixture SSO")));
        await Until(() => wire.IdentityReads == 1); password.Password = "later-editable-draft";
        Program.Log($"TRACE latest OAuth identity pending: revision={submittedRevision}, current={Read(page, "_signInRevision")}, busy={Read(page, "_signInBusy")}, ticketWrites={wire.TicketWrites}");
        wire.IdentityPending.TrySetResult(Reply(new { server_id = "fixture-server" })); wire.IdentityPending = null;
        try { await Until(() => start is not null || !(bool)Read(page, "_signInBusy")!); }
        finally
        {
            var errors = All<TextBlock>(body).Where(text => ReferenceEquals(text.Foreground, Application.Current.Resources["ErrorBrush"]) && text.Text.Length != 0).Select(text => text.Text);
            Program.Log($"TRACE latest OAuth after identity: identityReads={wire.IdentityReads}, ticketWrites={wire.TicketWrites}, browserReached={start is not null}, revision={Read(page, "_signInRevision")}, busy={Read(page, "_signInBusy")}, handshake={Read(page, "_signInHandshake") is not null}, visibleError={string.Join(" | ", errors)}");
        }
        Check(start is not null, "OAuth failed before recorded browser boundary; inspect sanitized identity/ticket/revision/error trace");
        Check(wire.TicketPassword == "clicked-password" && start!.AbsolutePath == "/api/v2/auth/oauth/3/native/start" && Query(start, "code_challenge_method") == "S256", "OAuth native start used later draft or omitted PKCE");
        string Callback(Uri uri) => "org.siloserver.silo:/auth/callback?iss=https%3A%2F%2Faccount-latest-fixture.invalid&server=fixture-server&state=" + Query(uri, "app_state") + "&link=1&code=fixture-link-code";
        var old = Callback(start!);
        Check(!await NativeOAuthCallbacks.TryHandleAsync(old.Replace("fixture-server", "foreign-server")) && wire.CompleteWrites == 0, "Foreign OAuth identity callback redeemed code");
        Click(Buttons(body).Single(button => Equals(button.Content, "Cancel sign-in")));
        Check(!await NativeOAuthCallbacks.TryHandleAsync(old) && wire.CompleteWrites == 0, "Canceled OAuth identity callback redeemed code");
        Click(Buttons(body).Single(button => Equals(button.Content, "Connect Fixture SSO")));
        password = All<PasswordBox>(body).Single(box => Equals(box.Header, "Silo password")); password.Password = "fixture-local"; start = null;
        Click(Buttons(body).Single(button => Equals(button.Content, "Continue to Fixture SSO"))); await Until(() => start is not null);
        var callback = Callback(start!); var generation = auth.SessionGeneration;
        wire.Pending = NewGate(); var completing = NativeOAuthCallbacks.TryHandleAsync(callback);
        await Until(() => wire.CompleteWrites == 1);
        Check(!Buttons(body).Single(button => Equals(button.Content, "Connecting…")).IsEnabled, "Pending OAuth completion permits duplicate submit");
        wire.Pending.TrySetResult(Reject("rate_limit_exceeded", HttpStatusCode.TooManyRequests)); await completing; wire.Pending = null;
        Check(All<TextBlock>(body).Any(text => text.Text == "Too many attempts. Wait a minute and try again.") && password.Password == "fixture-local", "OAuth429 lost draft or retry copy");
        start = null; Click(Buttons(body).Single(button => Equals(button.Content, "Continue to Fixture SSO"))); await Until(() => start is not null);
        callback = Callback(start!); Check(await NativeOAuthCallbacks.TryHandleAsync(callback), "Valid OAuth link callback rejected");
        await Until(() => wire.CompleteWrites == 2 && (bool)Read(page, "_signInLoaded")!);
        Check(auth.SessionGeneration == generation && !await NativeOAuthCallbacks.TryHandleAsync(callback) && wire.CompleteWrites == 2 && wire.CompletionVerifier.Length >= 43,
            "OAuth link changed login session, allowed replay or omitted verifier");
        await Capture(frame, "latest-identities-oauth-linked.png");
        wire.ResetOAuthIdentity();
        await Call(page, "LoadAccountSignInAsync");
        foreach (var revoke in new Action[]
        {
            () => auth.SetCurrentUser(new() { Id = "other-account", Role = "user" }),
            () => auth.SetCurrentUser(new() { Id = "fixture", Role = "user", Impersonation = new() { Active = true } })
        })
        {
            Click(Buttons(body).Single(button => Equals(button.Content, "Connect Fixture SSO")));
            All<PasswordBox>(body).Single(box => Equals(box.Header, "Silo password")).Password = "fixture-local";
            start = null;
            Click(Buttons(body).Single(button => Equals(button.Content, "Continue to Fixture SSO")));
            await Until(() => start is not null);
            var revokedCallback = Callback(start!);
            revoke();
            await Until(() => Read(page, "_signInHandshake") is null && (bool)Read(page, "_signInLoaded")!);
            Check(!await NativeOAuthCallbacks.TryHandleAsync(revokedCallback) && wire.CompleteWrites == 2,
                "A current account/impersonation change failed to revoke the provider callback");
            auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            await Call(page, "LoadAccountSignInAsync");
        }
        Program.Log("PASS current account and impersonation changes still revoke the native sign-in flow");
        Program.Log("PASS actual native OAuth link recorded browser/password snapshot/PKCE/cancel/foreign callback/pending429 retained draft/retry/one-use callback/no session replacement");
    }

    private static async Task Ratings(SettingsPage page, CatalogApi catalog, Wire wire, Frame frame)
    {
        wire.ShownRatings = ["imdb"]; catalog.InvalidateRatingCapability(); Click((Button)page.FindName("CardOverlaysTab")); await Call(page, "LoadCardOverlaySettingsAsync");
        var controls = (DependencyObject)page.FindName("CardOverlayControlsContainer");
        Check(All<TextBlock>(controls).Any(text => text.Text == "IMDb Rating") && !All<TextBlock>(controls).Any(text => text.Text is "TMDB Rating" or "RT Critics" or "RT Audience"), "Unavailable rating overlay controls offered");
        var draft = (CardOverlayPrefs)Read(page, "_cardOverlayDraft")!; Check(draft.Items.ContainsKey("rating_rt"), "Hidden rating preference removed from saved draft");
        await Capture(frame, "latest-rating-overlay-availability.png"); Program.Log("PASS actual rating controls availability and preserved hidden preference");
    }

    private static async Task NetworkLink(SettingsPage page, AuthService auth, Wire wire)
    {
        wire.Network = true;
        Click((Button)page.FindName("AccountPasswordTab")); await Call(page, "LoadAccountSignInAsync");
        var body = (StackPanel)page.FindName("AccountSignInBody");
        Check(Buttons(body).Any(button => Equals(button.Content, "Connect Fixture Network")), "Settings filters out the current network sign-in provider");
        Click(Buttons(body).Single(button => Equals(button.Content, "Connect Fixture Network")));
        Check(All<TextBlock>(body).Any(text => text.Text.Contains("Fixture Owner") && text.Text.Contains("Fixture Network")), "Network connect form omits the device owner");
        var password = All<PasswordBox>(body).Single(); password.Password = "submitted-password";
        wire.Pending = NewGate(); Click(Buttons(body).Single(button => Equals(button.Content, "Connect")));
        await Until(() => wire.NetworkWrites == 1); password.Password = "retained-draft";
        Check(!Buttons(body).Single(button => Equals(button.Content, "Connecting…")).IsEnabled && wire.NetworkBody.GetProperty("installation_id").GetString() == "5" && wire.NetworkBody.GetProperty("password").GetString() == "submitted-password" && wire.NetworkBody.EnumerateObject().Count() == 2,
            "Network link changes submitted password, includes directory credentials or permits duplicate submit");
        wire.Pending.TrySetResult(Reject("network_identity_required", HttpStatusCode.Forbidden));
        await Until(() => !(bool)Read(page, "_signInBusy")!); wire.Pending = null;
        Check(All<TextBlock>(body).Any(text => text.Text == "Open this server at its Fixture Network address to connect Fixture Network.") && password.Password == "retained-draft", "Network link refusal loses draft or network-address feedback");
        wire.Pending = NewGate(); Click(Buttons(body).Single(button => Equals(button.Content, "Connect")));
        await Until(() => wire.NetworkWrites == 2);
        wire.Pending.TrySetResult(Reject("validation_failed", HttpStatusCode.UnprocessableEntity, "body.password"));
        await Until(() => !(bool)Read(page, "_signInBusy")!); wire.Pending = null;
        Check(All<TextBlock>(body).Any(text => text.Text == "That isn't your current Silo password."), "Network link wrong password misreported");
        var generation = auth.SessionGeneration;
        Click(Buttons(body).Single(button => Equals(button.Content, "Connect")));
        await Until(() => wire.NetworkWrites == 3 && (bool)Read(page, "_signInLoaded")! && All<TextBlock>(body).Any(text => text.Text == "Fixture Network"));
        Check(auth.SessionGeneration == generation && wire.RefreshRequests == 0 && !Buttons(body).Any(button => Equals(button.Content, "Connect Fixture Network")), "Network link replaces login or retains connect action");
        Program.Log("PASS actual network identity linking owner copy/local password snapshot/pending/no replay/refusal422 retry/linked identity/session retained");
    }

    private static TaskCompletionSource<HttpResponseMessage> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Reject(string code, HttpStatusCode status, string? location = null) => Reply(new { type = "https://silo.example/docs/api/v2/problems/" + code, detail = "Fixture rejection", errors = location is null ? Array.Empty<object>() : new object[] { new { location } } }, status);
    private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
    private static string Query(Uri uri, string key) => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).Where(parts => parts.Length == 2 && parts[0] == key).Select(parts => Uri.UnescapeDataString(parts[1])).Single();
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static object? Read(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static Task Call(object instance, string name, params object[] args) => (Task)instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args)!;
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T value) yield return value; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static IEnumerable<Button> Buttons(DependencyObject root) => All<Button>(root);
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> predicate) { for (int i = 0; i < 500; ++i) { if (predicate()) return; await Task.Delay(25); } throw new TimeoutException("Latest account actual state did not settle"); }
    private static Task Capture(Frame frame, string name) => (Task)typeof(AccountCoverageNativeFixture).GetMethod("CaptureAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [frame, name])!;
    private static async Task Pairs(Frame frame, Window window, string name, FrameworkElement target) { foreach (var width in new[] { 1400, 460 }) { frame.Width = width; frame.Height = ClientHeight; window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, ClientHeight)); await Task.Delay(120); frame.UpdateLayout(); target.StartBringIntoView(); await Task.Delay(100); await Capture(frame, $"{name}-{width}.png"); } }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _fallback;
        private readonly Dictionary<string, JsonElement> _defaults;
        internal bool Batched = true, CanUnlink = true;
        internal bool? DeviceTitle = false, ProfileTitle;
        internal string[] ShownRatings = ["imdb", "tmdb", "rt_critic", "rt_audience"];
        internal List<(string Method, string Scope, bool? Value, string? Device)> TitleWrites = [];
        internal int CredentialsWrites, Unlinks, RefreshRequests, IdentityReads, CompleteWrites, TicketWrites;
        internal bool Network; internal int NetworkWrites; internal JsonElement NetworkBody;
        internal string? TicketPassword;
        internal string CompletionVerifier = "";
        internal JsonElement CredentialsBody;
        internal TaskCompletionSource<HttpResponseMessage>? Pending;
        internal TaskCompletionSource<HttpResponseMessage>? IdentityPending;
        private readonly List<object> _linked = [new { id = "old", installation_id = "2", provider_name = "", username = "Fixture linked", email = "", linked_at = "2026-10-01T00:00:00Z" }];
        internal void ResetOAuthIdentity() => _linked.RemoveAll(row => JsonSerializer.SerializeToElement(row).GetProperty("installation_id").GetString() == "3");
        internal Wire()
        {
            _fallback = new((HttpMessageHandler)Activator.CreateInstance(typeof(AccountCoverageNativeFixture).GetNestedType("Wire", BindingFlags.NonPublic)!, nonPublic: true)!);
            using var stream = typeof(DeviceSettingDisplay).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Settings.device-settings.json")!;
            using var document = JsonDocument.Parse(stream); _defaults = document.RootElement.EnumerateArray().ToDictionary(row => row.GetProperty("key").GetString()!, row => row.GetProperty("default_value").Clone());
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var method = request.Method.Method;
            if (path == "/api/v2/auth/refresh") ++RefreshRequests;
            if (path == "/api/v2/settings/contract/capabilities") return Reply(new { api_version = 1, manifest_revision = 16, supports_batched_effective = Batched, supports_idempotent_writes = true, supports_atomic_shortcuts = true, client_families = new[] { "web", "desktop" } });
            if (path == "/api/v2/settings/values/effective")
            {
                var keys = request.RequestUri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("keys=")).Select(part => Uri.UnescapeDataString(part[5..]));
                return Reply(new { items = keys.Select(key => key == "ui.title_art" ? (object)new { key, value = ProfileTitle ?? DeviceTitle ?? true, source = ProfileTitle.HasValue ? "profile" : DeviceTitle.HasValue ? "profile_device" : "default", scope = ProfileTitle.HasValue ? "profile" : "profile_device", constrained = false } : new { key, value = _defaults.GetValueOrDefault(key, JsonSerializer.SerializeToElement<object?>(null)), source = "default", constrained = false }).ToArray(), revision = 16 });
            }
            if (path == "/api/v2/settings/values/ui.title_art")
            {
                var scope = Query(request.RequestUri, "scope"); var device = request.RequestUri.Query.Contains("device_id=") ? Query(request.RequestUri, "device_id") : null;
                if (method == "GET") return DeviceTitle.HasValue ? Reply(new { key = "ui.title_art", value = DeviceTitle.Value, source = "profile_device", scope = "profile_device" }) : Reply(new { }, HttpStatusCode.NotFound);
                bool? value = null; if (method == "PUT") { using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); value = doc.RootElement.GetProperty("value").GetBoolean(); }
                TitleWrites.Add((method, scope, value, device)); var response = Pending is { } gate ? await gate.Task.WaitAsync(ct) : Reply(new { key = "ui.title_art", value });
                if (response.IsSuccessStatusCode) { if (scope == "profile") ProfileTitle = method == "DELETE" ? null : value; else DeviceTitle = method == "DELETE" ? null : value; } return response;
            }
            if (path == "/api/v2/auth/providers")
            {
                var providers = new List<object> { new { id = "local", display_name = "Silo password", mode = "credentials" }, new { id = "ldap", display_name = "Fixture Directory", mode = "credentials", installation_id = "1" }, new { id = "oidc", display_name = "Fixture SSO", mode = "oauth", installation_id = "3", native_start_path = "/api/v2/auth/oauth/3/native/start" } };
                if (Network) providers.Add(new { id = "network", display_name = "Fixture Network", mode = "network", installation_id = "5", network_identity = new { display_name = "Fixture Owner", username = "fixture-name" } });
                return Reply(new { items = providers });
            }
            if (path == "/api/v2/account/identities/link-network")
            {
                ++NetworkWrites; using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); NetworkBody = doc.RootElement.Clone();
                var response = Pending is { } gate ? await gate.Task.WaitAsync(ct) : Reply(new { id = "network", installation_id = "5", provider_name = "Fixture Network" }, HttpStatusCode.Created);
                if (response.IsSuccessStatusCode) _linked.Add(new { id = "network", installation_id = "5", provider_name = "Fixture Network" });
                return response;
            }
            if (path == "/api/v2/account/identities") return Reply(new { items = _linked.ToArray(), can_unlink = CanUnlink });
            if (path == "/api/v2/account/identities/link-credentials") { ++CredentialsWrites; using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); CredentialsBody = doc.RootElement.Clone(); var response = Pending is { } gate ? await gate.Task.WaitAsync(ct) : Reply(new { id = "directory", installation_id = "1", provider_name = "Fixture Directory" }); if (response.IsSuccessStatusCode) _linked.Add(new { id = "directory", installation_id = "1", provider_name = "Fixture Directory", username = "fixture-name" }); return response; }
            if (path == "/api/v2/system/identity") { ++IdentityReads; Program.Log("TRACE latest OAuth wire identity read; pending=" + (IdentityPending is not null)); return IdentityPending is { } gate ? await gate.Task.WaitAsync(ct) : Reply(new { server_id = "fixture-server" }); }
            if (path == "/api/v2/account/identities/link-ticket") { ++TicketWrites; Program.Log("TRACE latest OAuth wire link-ticket request (body omitted)"); using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); TicketPassword = doc.RootElement.GetProperty("password").GetString(); return Reply(new { ticket = "fixture-link-ticket", expires_at = "2030-10-01T00:00:00Z" }); }
            if (path == "/api/v2/account/identities/link-complete") { ++CompleteWrites; using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); CompletionVerifier = doc.RootElement.GetProperty("code_verifier").GetString()!; var response = Pending is { } gate ? await gate.Task.WaitAsync(ct) : new(HttpStatusCode.NoContent); if (response.IsSuccessStatusCode) _linked.Add(new { id = "oauth", installation_id = "3", provider_name = "Fixture SSO" }); return response; }
            if (path.StartsWith("/api/v2/account/identities/") && method == "DELETE") { ++Unlinks; var response = Pending is { } gate ? await gate.Task.WaitAsync(ct) : new(HttpStatusCode.NoContent); if (response.IsSuccessStatusCode) _linked.RemoveAll(identity => JsonSerializer.SerializeToElement(identity).GetProperty("id").GetString() == path.Split('/').Last()); return response; }
            if (path == "/api/v2/capabilities/ratings") return Reply(new { state = "available", sources = ShownRatings.Select(source => new { source }).ToArray() });
            return await _fallback.SendAsync(request, ct);
        }
        protected override void Dispose(bool disposing) { if (disposing) _fallback.Dispose(); base.Dispose(disposing); }
    }
}
