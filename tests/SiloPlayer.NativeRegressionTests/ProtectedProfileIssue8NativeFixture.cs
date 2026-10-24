using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class ProtectedProfileIssue8NativeFixture
{
    internal static async Task RunAsync()
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = servicesField.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://protected-profile-fixture.invalid");
        var api = new AuthApi(client); var settingsApi = new SettingsApi(client); var catalog = new CatalogApi(client);
        using var auth = new AuthService(client, api);
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "account", Username = "Synthetic", Role = "user" });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "profile-settings"));
        local.Load().TasteSeedDismissedProfileIds.Add("protected");
        var vm = new ProfileSelectViewModel(api, settingsApi, catalog, auth, local);
        using var services = new ServiceCollection().AddSingleton(vm).AddSingleton(auth).AddSingleton(local).BuildServiceProvider();
        servicesField.SetValue(null, services);
        var frame = new Frame { Width = 1080, Height = 900 };
        var window = new Window { Content = frame };
        try
        {
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1080, 900)); window.AppWindow.Show(false);
            Require(frame.Navigate(typeof(ProfileSelectPage)), "Native profile picker must navigate.");
            await Until(() => vm.Profiles.Count == 2 && !vm.IsLoading);
            var page = (ProfileSelectPage)frame.Content;
            // Use the real picker/dialog/ViewModel/AuthService. Replace only shell
            // entry, since the isolated host deliberately does not construct App's
            // live MainWindow, receiver or Home services. Actual Frame departure
            // still runs ProfileSelectPage.OnNavigatedFrom and hides the PIN dialog.
            var original = (Action)Delegate.CreateDelegate(typeof(Action), page,
                typeof(ProfileSelectPage).GetMethod("OnProfileSelected", BindingFlags.NonPublic | BindingFlags.Instance)!);
            vm.ProfileSelected -= original;
            var activations = 0;
            vm.ProfileSelected += () => { activations++; Require(frame.Navigate(typeof(Page)), "Accepted selection must leave picker."); };
            await Task.Delay(150);
            var protectedCard = All<Button>(page).Single(b => b.Tag is Profile p && p.Id == "protected");
            if (Environment.GetEnvironmentVariable("SILO_ISSUE8_NATIVE_CASE") == "legacy-number")
            {
                // Exact pre-Oct16 published source initializer, isolated from the
                // current production method to test native PasswordBox behavior.
                try
                {
                    var legacy = new PasswordBox
                    {
                        PlaceholderText = "Enter 4-digit PIN", MaxLength = 4,
                        PasswordRevealMode = PasswordRevealMode.Hidden,
                        InputScope = new Microsoft.UI.Xaml.Input.InputScope
                        { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } },
                    };
                    Program.Log("OBSERVATION: legacy Number PasswordBox initializer succeeded.");
                }
                catch (Exception exception)
                {
                    Program.Log($"OBSERVATION: exact legacy Number PasswordBox initializer threw {exception.GetType().FullName}, HRESULT 0x{exception.HResult:X8}: {exception.Message}");
                }
            }
            Click(protectedCard); await Until(() => Dialogs(frame).Any());
            var dialog = Dialogs(frame).Single(); var input = All<PasswordBox>(dialog).Single();
            Require(input.MaxLength == 4, "Published picker PIN limit is four digits.");
            if (Environment.GetEnvironmentVariable("SILO_ISSUE8_NATIVE_CASE") == "cancel")
            {
                wire.HoldProof = true; input.Password = "1234"; await Confirm(dialog);
                await Until(() => wire.Proofs == 1);
                Click(All<Button>(dialog).Single(b => b.Name == "CloseButton"));
                await Until(() => !Dialogs(frame).Any()); wire.ReleaseProof.TrySetResult(); await Task.Delay(150);
                Require(activations == 0 && auth.SelectedProfileId == null && client.ProfileId == null,
                    "Cancel retires late accepted PIN and remains unauthenticated.");
                Require(ReferenceEquals(frame.Content, page) && !vm.IsPinRequired && !vm.IsLoading,
                    "Cancel leaves reusable picker visible without pending state.");
                Program.Log("PASS: actual frozen286 PIN dialog Cancel during held valid verification rejects late response without selecting profile.");
                return;
            }
            input.Password = "9999"; await Confirm(dialog);
            await Until(() => vm.PinErrorMessage == "Incorrect PIN");
            Require(activations == 0 && auth.SelectedProfileId == null && input.Password == "", "Wrong PIN remains on picker and clears input.");
            input.Password = "1234"; await Confirm(dialog);
            await Until(() => activations == 1 && frame.Content is not ProfileSelectPage && !Dialogs(frame).Any());
            await Task.Delay(150);
            Require(auth.SelectedProfileId == "protected" && client.ProfileId == "protected", "Correct PIN selects protected identity.");
            Require(local.Load().LastProfileId == "protected" && wire.Proofs == 2, "Accepted PIN persists selected profile exactly once.");
            Program.Log("PASS: frozen native picker wrong PIN then correct four-digit PIN accepts separate PIN and leaves picker; actual native departure closes dialog without error. Shell destination alone is replaced by an inert Frame page.");
        }
        finally
        {
            vm.CancelProfileLoad();
            foreach (var dialog in Dialogs(frame).ToArray()) dialog.Hide();
            frame.Content = null; await Task.Delay(80); window.Close(); servicesField.SetValue(null, previous);
        }
    }

    private static async Task Confirm(ContentDialog dialog)
    { await Until(() => All<Button>(dialog).Any(b => b.Name == "PrimaryButton" && b.IsEnabled)); Click(All<Button>(dialog).Single(b => b.Name == "PrimaryButton")); }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static IEnumerable<ContentDialog> Dialogs(Frame frame) => VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).SelectMany(p => All<ContentDialog>(p.Child));
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    { if (root is T item) yield return item; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static async Task Until(Func<bool> predicate)
    { for (var i = 0; i < 200; i++) { if (predicate()) return; await Task.Delay(25); } throw new TimeoutException("Protected native picker timed out."); }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private sealed class Wire : HttpMessageHandler
    {
        internal int Proofs;
        internal bool HoldProof;
        internal TaskCompletionSource ReleaseProof = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("verify-pin"))
            {
                Proofs++;
                if (HoldProof) await ReleaseProof.Task;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var valid = body.RootElement.GetProperty("pin").GetString() == "1234";
                return Reply(new { valid, profile_token = valid ? "synthetic-proof" : "" });
            }
            if (path == "/api/v2/profiles") return Reply(new { items = new object[] {
                new { id = "protected", name = "Protected", has_pin = true, is_primary = Environment.GetEnvironmentVariable("SILO_ISSUE8_NATIVE_CASE") != "secondary" },
                new { id = "unprotected", name = "Unprotected", has_pin = false, is_primary = false } } });
            return Reply(new { items = Array.Empty<object>(), values = new { }, settings = new { } });
        }
        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
