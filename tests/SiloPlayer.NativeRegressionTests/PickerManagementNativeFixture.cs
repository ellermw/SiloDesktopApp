using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using SiloPlayer.Views.Dialogs;

internal static class PickerManagementNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window unused)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://picker-native.invalid");
        var api = new AuthApi(client); var settingsApi = new SettingsApi(client); var catalog = new CatalogApi(client);
        using var auth = new AuthService(client, api); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "account", Username = "Synthetic", Role = "user" });
        auth.SelectProfile("viewer", profile: new() { Id = "viewer", Name = "Viewer" });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")); local.Load().LastProfileId = "viewer";
        var vm = new ProfileSelectViewModel(api, settingsApi, catalog, auth, local);
        var navigation = new NavigationService();
        var theme = new ThemeService(local, settingsApi);
        var accessibility = new AccessibilityService(local, theme);
        var settingsVm = new SettingsViewModel(settingsApi, catalog, api, new HistoryImportApi(client), new WatchProvidersApi(client),
            auth, theme, local, accessibility);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(api).AddSingleton(settingsApi).AddSingleton(catalog)
            .AddSingleton(auth).AddSingleton(local).AddSingleton(vm).AddSingleton(navigation).AddSingleton(theme).AddSingleton(settingsVm)
            .AddSingleton(new ToastService()).AddSingleton(new UICustomizationService(settingsApi)).AddSingleton(new CardOverlayService(settingsApi))
            .AddSingleton(accessibility)
            .AddSingleton(((IServiceProvider)previous!).GetRequiredService<SiloPlayer.Services.SiloCast.SiloCastReceiverService>())
            .AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(new HistoryImportApi(client)).AddSingleton(new WatchProvidersApi(client))
            .AddSingleton(new RequestsApi(client)).AddSingleton(new RecommendationsApi(client)).AddSingleton(new CollectionsApi(client))
            .BuildServiceProvider();
        field.SetValue(null, services);
        var frame = new Frame { Width = 1920, Height = 1080 }; var window = new Window { Content = frame }; frame.Tag = window;
        navigation.Frame = frame;
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1920, 1080)); window.AppWindow.Show(false);
            Require(frame.Navigate(typeof(ProfileSelectPage)), "actual picker navigation");
            await Until(() => vm.Profiles.Count == 2 && !vm.IsLoading);
            var page = (ProfileSelectPage)frame.Content;
            await Task.Delay(150);
            Require(All<Button>(page).Any(b => b.Name == "AddProfileCard" && b.Visibility == Visibility.Visible), "Nonempty picker must render the actual Add profile card.");
            Require(page.FindName("ManageProfilesButton") is Button, "actual Manage button exists");
#if !PICKER_RED
            await RunCurrent(frame, window, page, vm, auth, client, local, wire, api, settingsVm);
#endif
        }
        finally
        {
            vm.CancelProfileLoad(); wire.Release.TrySetResult();
            foreach (var d in Dialogs(frame).ToArray()) d.Hide();
            frame.Content = null; await Task.Delay(100); window.Close(); await Task.Delay(80); field.SetValue(null, previous);
        }
    }

#if !PICKER_RED
    private static async Task RunCurrent(Frame frame, Window window, ProfileSelectPage page, ProfileSelectViewModel vm,
        AuthService auth, SiloApiClient client, SettingsService local, Wire wire, AuthApi api, SettingsViewModel settingsVm)
    {
        if (Environment.GetEnvironmentVariable("SILO_PICKER_NATIVE_CASE") == "settings-use-pin")
        {
            var settingsPage = new SettingsPage(); frame.Content = settingsPage; await Task.Delay(100);
            var use = (Task)typeof(SettingsPage).GetMethod("UseProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(settingsPage, [new Profile { Id = "primary", Name = "Protected", HasPin = true }])!;
            await Task.Delay(150);
            Require(!use.IsFaulted && Dialogs(frame).Any(), "protected Settings Use profile opens actual PIN dialog");
            var dialog = Dialogs(frame).Single(); var box = All<PasswordBox>(dialog).Single();
            Require(box.InputScope.Names[0].NameValue == Microsoft.UI.Xaml.Input.InputScopeNameValue.NumericPin, "Settings PIN supports PasswordBox NumericPin");
            Click(Command(dialog, "CloseButton")); await use;
            Require(auth.SelectedProfileId == "viewer", "canceled protected Use preserves account selection");
            Program.Log("PASS: actual protected Settings Use-profile PIN opens and cancels."); return;
        }
        if (Environment.GetEnvironmentVariable("SILO_PICKER_NATIVE_CASE") == "avatar-retry")
        { await AvatarRetry(frame, vm, wire, api); return; }
        var add = Add(page); var manage = (Button)page.FindName("ManageProfilesButton");
        Require(AutomationProperties.GetName(add) == "Add profile" && AutomationProperties.GetName(manage) == "Manage profiles", "accessible Add and Manage labels");
        var protectedCard = All<Button>(page).Single(b => b.Tag is Profile p && p.IsPrimary);
        Require(AutomationProperties.GetName(protectedCard).Contains("PIN"), "protected profile label");
        Require(add.Focus(FocusState.Keyboard), "Add accepts keyboard focus");
        foreach (var size in new[] { (400, 740), (1920, 1080), (3440, 1440), (3840, 2160) })
        {
            frame.Width = size.Item1; frame.Height = size.Item2; window.AppWindow.ResizeClient(new(size.Item1, size.Item2));
            frame.UpdateLayout(); await Task.Delay(120);
            add = Add(page); var point = add.TransformToVisual(frame).TransformPoint(new());
            Require(add.ActualWidth == 148 && point.X >= 0 && point.X + add.ActualWidth <= frame.ActualWidth, "Add wraps within actual viewport");
            Require(manage.ActualWidth > 0, "Manage remains visible");
            await MediaParityNativeFixture.CaptureAsync(frame, $"picker-{size.Item1}.png");
        }
        frame.Width = 1080; frame.Height = 900; window.AppWindow.ResizeClient(new(1080, 900)); frame.UpdateLayout();

        // Real Add -> primary PIN, wrong/locked feedback, explicit retry, editor cancellation.
        Click(Add(page)); await Until(() => Dialogs(frame).Any());
        var pin = Dialogs(frame).Single(); var input = All<PasswordBox>(pin).Single();
        Require(All<TextBlock>((DependencyObject)pin.Content).Any(t => t.Text == "Adding a profile needs the primary profile's PIN."), "Add-specific PIN purpose");
        wire.PinStatus = "wrong"; input.Password = "1234"; await ConfirmPin(pin);
        await Until(() => vm.PinErrorMessage == "Incorrect PIN");
        Require(input.Password == "" && wire.Proofs == 1, "wrong PIN clears input and never auto retries");
        wire.PinStatus = "locked"; input.Password = "1234"; await ConfirmPin(pin);
        await Until(() => vm.PinErrorMessage?.StartsWith("Too many incorrect PINs.") == true);
        await MediaParityNativeFixture.CaptureAsync(pin, "picker-pin-locked.png");
        wire.HoldPin = true; wire.PinStatus = "ok"; input.Password = "1234"; await ConfirmPin(pin);
        await Until(() => wire.PinEntered);
        var pendingProofs = wire.Proofs; input.Password = "5678"; await Task.Delay(80);
        Require(!Command(pin, "PrimaryButton").IsEnabled && wire.Proofs == pendingProofs, "Editing PIN while proof is pending must keep duplicate Confirm disabled.");
        try { Click(Command(pin, "PrimaryButton")); } catch (System.Runtime.InteropServices.COMException) { }
        await Task.Delay(50); Require(wire.Proofs == pendingProofs, "Actual disabled Confirm cannot submit another proof attempt.");
        Click(Command(pin, "CloseButton")); await Until(() => !vm.IsPinRequired);
        wire.PinRelease.TrySetResult(); await Until(() => !Dialogs(frame).Any()); await Task.Delay(100); wire.HoldPin = false;
        Require(wire.Libraries == 0 && auth.SelectedProfileId == "viewer" && vm.AddSession == null, "actual PIN Cancel retires late valid approval");
        Click(Add(page)); await Until(() => Dialogs(frame).Any()); pin = Dialogs(frame).Single(); input = All<PasswordBox>(pin).Single();
        wire.PinStatus = "ok"; input.Password = "1234"; await ConfirmPin(pin);
        await Until(() => Dialogs(frame).Any(d => !ReferenceEquals(d, pin)));
        var editor = Dialogs(frame).Single();
        Require(auth.SelectedProfileId == "viewer" && client.ProfileId == "viewer" && local.Load().LastProfileId == "viewer", "actual Add opens primary editor without selecting it");
        Require(wire.Libraries == 1 && wire.PrimaryAuthorityValid, "actual editor loads captured primary libraries");
        await MediaParityNativeFixture.CaptureAsync(editor, "picker-editor-1080.png");
        Click(Command(editor, "CloseButton")); await Until(() => !vm.IsManagementBusy && !Dialogs(frame).Any());
        Require(wire.Creates == 0, "Cancel editor causes no create");

        // Pending libraries visibly disable picker; a timeout is visible and the actual Add action retries.
        wire.Protected = false; await vm.LoadProfilesCommand.ExecuteAsync(null); await Task.Delay(60);
        wire.HoldLibraries = true; Click(Add(page)); await Until(() => wire.Entered);
        Require(!Add(page).IsEnabled && !manage.IsEnabled, "pending library preparation disables native actions");
        await MediaParityNativeFixture.CaptureAsync(frame, "picker-preparing.png");
        wire.TimeoutLibraries = true; wire.Release.TrySetResult(); await Until(() => vm.ErrorMessage != null && !vm.IsManagementBusy);
        Require(!Dialogs(frame).Any(), "library timeout does not open editor");
        await MediaParityNativeFixture.CaptureAsync(frame, "picker-library-timeout.png");
        wire.HoldLibraries = false; wire.TimeoutLibraries = false;
        Click(Add(page)); await Until(() => Dialogs(frame).Any()); editor = Dialogs(frame).Single();
        var name = All<TextBox>(editor).Single(t => t.PlaceholderText == "Alex"); name.Text = "Created by fixture";
        Click(Command(editor, "PrimaryButton")); await Until(() => !vm.IsManagementBusy && !Dialogs(frame).Any());
        Require(wire.Creates == 1 && vm.Profiles.Any(p => p.Id == "created") && ReferenceEquals(frame.Content, page), "actual Save creates once, refreshes list and stays on picker");
        Require(auth.SelectedProfileId == "viewer" && local.Load().LastProfileId == "viewer", "actual Save preserves viewer and persisted owner");

        await CanceledCreate(frame, vm, wire, api);
        await AvatarRetry(frame, vm, wire, api);

        // Managed activation emits the dedicated destination, then the fixture drives
        // the actual Settings Profiles route without constructing the live shell/receiver.
        var handler = (Action)Delegate.CreateDelegate(typeof(Action), page, typeof(ProfileSelectPage).GetMethod("OnManageProfilesSelected", BindingFlags.Instance | BindingFlags.NonPublic)!);
        vm.ManageProfilesSelected -= handler;
        var destinations = 0; var librariesAtRoute = -1;
        vm.ManageProfilesSelected += () => { destinations++; librariesAtRoute = wire.Libraries; Require(frame.Navigate(typeof(SettingsPage), "Profiles"), "actual Profiles Settings route"); };
        var homes = 0; vm.ProfileSelected += () => homes++;
        wire.Protected = true; await vm.LoadProfilesCommand.ExecuteAsync(null); await Task.Delay(60);
        var librariesBeforeManage = wire.Libraries;
        Click(manage); await Until(() => Dialogs(frame).Any()); pin = Dialogs(frame).Single();
        Require(All<TextBlock>((DependencyObject)pin.Content).Any(t => t.Text == "Managing profiles needs the primary profile's PIN."), "Manage-specific primary PIN purpose");
        All<PasswordBox>(pin).Single().Password = "1234"; await ConfirmPin(pin);
        await Until(() => frame.Content is SettingsPage);
        await Until(() => (bool)Read(frame.Content, "_pageInitialized")!);
        Require(destinations == 1 && homes == 0 && auth.SelectedProfileId == "primary" && librariesAtRoute == librariesBeforeManage, "Manage deliberately selects verified primary and routes once without Home or Add library fetch");
        Require(((FrameworkElement)((SettingsPage)frame.Content).FindName("ProfilesPanel")).Visibility == Visibility.Visible, "actual Settings Profiles panel selected");
        await MediaParityNativeFixture.CaptureAsync(frame, "picker-manage-settings-profiles.png");
        Program.Log("PASS actual Add/Manage cards; four viewport wraps; keyboard/accessibility; primary PIN wrong/locked/retry; captured libraries; pending/current timeout; editor Cancel/Create staying on picker; accepted create/avatar-only Retry; deliberate primary activation and actual Settings Profiles navigation. Shell route call is reviewed separately.");
    }

    private static async Task AvatarRetry(Frame frame, ProfileSelectViewModel vm, Wire wire, AuthApi api)
    {
        // Same reused native form, injected synthetic upload bytes. Hold the
        // accepted create while editing a create-only field, then fail upload.
        wire.Protected = false; await vm.LoadProfilesCommand.ExecuteAsync(null);
        await vm.BeginManagementAsync(true); var session = vm.AddSession!;
        var form = typeof(ProfileEditorDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke([frame.XamlRoot, null, session.Libraries, true, api, false, false]);
        Set(form, "_addSession", session); Set(form, "_avatarFileBytes", new byte[] { 1, 2 }); Set(form, "_avatarFileName", "fixture.png"); Set(form, "_avatarContentType", "image/png");
        var nativeEditor = (ContentDialog)Read(form, "_dialog")!; var showing = nativeEditor.ShowAsync();
        await Until(() => CommandOrNull(nativeEditor, "PrimaryButton") != null);
        ((TextBox)Read(form, "_nameBox")!).Text = "Avatar fixture";
        wire.FailAvatar = true; wire.HoldCreate = true; var before = wire.Creates; Click(Command(nativeEditor, "PrimaryButton"));
        await Until(() => wire.CreateEntered);
        ((TextBox)Read(form, "_nameBox")!).Text = "";
        wire.CreateRelease.TrySetResult();
        await Until(() => nativeEditor.PrimaryButtonText == "Retry avatar");
        Require(session.CreatedProfile?.Id == "created" && wire.Creates == before + 1, "native form retains accepted identity on upload failure");
        await MediaParityNativeFixture.CaptureAsync(nativeEditor, "picker-avatar-retry.png");
        wire.FailAvatar = false; Click(Command(nativeEditor, "PrimaryButton"));
        await Task.Delay(120);
        if (((TextBlock)Read(form, "_validationText")!).Text == "Enter a profile name." && ReadProperty(form, "SavedProfile") == null)
            throw new InvalidOperationException("Avatar Retry incorrectly revalidates the edited empty create-only name after Create already succeeded.");
        await Until(() => ((Profile?)ReadProperty(form, "SavedProfile")) != null);
        await showing;
        Require(wire.Creates == before + 1 && wire.Avatars == 2 && ((Profile?)ReadProperty(form, "SavedProfile"))?.Id == "created", "native Retry uploads only once more, never duplicate Create");
        await vm.FinishAddAsync(session, true);
        Program.Log("PASS delayed native create; create-form edit during dispatch; failed avatar; Retry bypasses create-only validation and uses accepted identity without a second Create.");
    }

    private static async Task CanceledCreate(Frame frame, ProfileSelectViewModel vm, Wire wire, AuthApi api)
    {
        await vm.BeginManagementAsync(true); var session = vm.AddSession!;
        var form = typeof(ProfileEditorDialog).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
            .Invoke([frame.XamlRoot, null, session.Libraries, true, api, false, false]);
        Set(form, "_addSession", session); Set(form, "_avatarFileBytes", new byte[] { 1 }); Set(form, "_avatarFileName", "fixture.png");
        var dialog = (ContentDialog)Read(form, "_dialog")!; var showing = dialog.ShowAsync();
        await Until(() => CommandOrNull(dialog, "PrimaryButton") != null);
        ((TextBox)Read(form, "_nameBox")!).Text = "Canceled create";
        wire.HoldCreate = true; wire.CreateEntered = false; wire.CreateRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var avatars = wire.Avatars; Click(Command(dialog, "PrimaryButton")); await Until(() => wire.CreateEntered);
        Click(Command(dialog, "CloseButton"));
        await Until(() => !session.IsCurrent);
        wire.CreateRelease.TrySetResult(); await showing;
        await Until(() => !(bool)Read(form, "_saving")!);
        Require(ReadProperty(form, "SavedProfile") == null && wire.Avatars == avatars, "native editor Cancel retires held Create and starts no avatar write or result");
        await vm.FinishAddAsync(session, false);
        wire.HoldCreate = false; wire.CreateEntered = false; wire.CreateRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Program.Log("PASS actual native editor Cancel during held Create retires the request before completion and suppresses result/follow-up avatar.");
    }
#endif

    private static Button Add(ProfileSelectPage page) => All<Button>(page).Single(b => b.Name == "AddProfileCard" && b.Visibility == Visibility.Visible);
    private static async Task ConfirmPin(ContentDialog pin) { await Until(() => CommandOrNull(pin, "PrimaryButton")?.IsEnabled == true); Click(Command(pin, "PrimaryButton")); }
    private static Button Command(ContentDialog d, string name) => CommandOrNull(d, name)!;
    private static Button? CommandOrNull(ContentDialog d, string name) => All<Button>(d).FirstOrDefault(b => b.Name == name);
    private static IEnumerable<ContentDialog> Dialogs(Frame f) => VisualTreeHelper.GetOpenPopupsForXamlRoot(f.XamlRoot).SelectMany(p => All<ContentDialog>(p.Child));
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    { if (root is T item) yield return item; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static object? Read(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static object? ReadProperty(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(25); } throw new TimeoutException("Picker native state timed out."); }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Wire : HttpMessageHandler
    {
        public bool Protected = true, PrimaryAuthorityValid = true, HoldLibraries, TimeoutLibraries, Entered, FailAvatar;
        public string PinStatus = "ok"; public int Proofs, Libraries, Creates, Avatars;
        public bool HoldCreate, CreateEntered;
        public bool HoldPin, PinEntered;
        public TaskCompletionSource PinRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CreateRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path.EndsWith("verify-pin"))
            {
                Proofs++;
                if (HoldPin) { PinEntered = true; await PinRelease.Task; }
                if (PinStatus == "locked") { var response = Reply(new { error = "profile_pin_locked" }, HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromSeconds(61)); return response; }
                return Reply(new { valid = PinStatus == "ok", profile_token = PinStatus == "ok" ? "synthetic-proof" : "" });
            }
            if (path.EndsWith("libraries") || path.EndsWith("avatar") || path == "/api/v2/profiles" && r.Method == HttpMethod.Post)
            {
                PrimaryAuthorityValid &= r.Headers.TryGetValues("X-Profile-Id", out var ids) && ids.Single() == "primary"
                    && (!Protected || r.Headers.TryGetValues("X-Profile-Token", out var proofs) && proofs.Single() == "synthetic-proof");
            }
            if (path.EndsWith("libraries"))
            {
                Libraries++; if (HoldLibraries) { Entered = true; await Release.Task; }
                if (TimeoutLibraries) throw new OperationCanceledException("synthetic timeout");
                return Reply(new { items = new[] { new { id = "1", name = "Primary Movies" } } });
            }
            if (path.EndsWith("avatar")) { Avatars++; return FailAvatar ? Reply(new { detail = "Synthetic upload failure" }, HttpStatusCode.ServiceUnavailable) : Reply(new { id = "created", name = "Avatar fixture" }); }
            if (path == "/api/v2/profiles" && r.Method == HttpMethod.Post)
            { Creates++; if (HoldCreate) { CreateEntered = true; await CreateRelease.Task; } return Reply(new { id = "created", name = "Created by fixture" }); }
            if (path == "/api/v2/profiles")
            {
                var profiles = new List<object> { new { id = "primary", name = "Primary", is_primary = true, has_pin = Protected }, new { id = "viewer", name = "Viewer", is_primary = false, has_pin = false } };
                if (Creates > 0) profiles.Add(new { id = "created", name = "Created by fixture", is_primary = false, has_pin = false });
                return Reply(new { items = profiles, avatar_upload_enabled = true });
            }
            if (path == "/api/v2/settings/capabilities") return Reply(new { api_version = 1, revision = 17, scopes = new { }, keys = new { } });
            return Reply(new { items = Array.Empty<object>(), values = new { }, settings = new { } });
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
