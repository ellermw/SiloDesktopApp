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
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class NetworkAuthNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server);
        var api = new AuthApi(client); using var auth = new AuthService(client, api);
        var settingsApi = new SettingsApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "network-auth-settings"));
        var vm = new LoginViewModel(auth, api, settingsApi, client);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(api)
            .AddSingleton(vm).AddSingleton(new NavigationService()).AddSingleton(new ThemeService(settings, settingsApi)).BuildServiceProvider();
        field.SetValue(null, services);
        LoginPage? page = null;
        try
        {
            await vm.LoadAuthInfoCommand.ExecuteAsync(null);
            Check(!vm.ShowPasswordForm && !vm.ShouldAutoRedirect, "Network-only discovery incorrectly shows passwords or starts OAuth");
            page = new LoginPage { Width = 900, Height = 740 }; parent.Children.Add(page);
            await Task.Delay(100); page.UpdateLayout();
            Button NetworkButton() => All<Button>(page).Single(button => All<TextBlock>(button).Any(text => text.Text == "Continue as Fixture Owner"));
            Task Ready() => Until(() => All<Button>(page).Any(button => button.IsEnabled && All<TextBlock>(button).Any(text => text.Text == "Continue as Fixture Owner")));
            foreach (var width in new[] { 900, 460 })
            {
                page.Width = width; await Task.Delay(80); page.UpdateLayout();
                var button = NetworkButton();
                Check(All<TextBlock>(button).Any(text => text.Text == "via Fixture Network") && button.ActualWidth > 200,
                    "Network owner/via label or responsive button is missing");
                var card = (Border)page.FindName("LoginCard");
                Check(button.TransformToVisual(card).TransformPoint(new(0, 0)).X + button.ActualWidth <= card.ActualWidth + 1,
                    "Network button overflows the auth card");
                await (Task)typeof(AccountParityNativeFixture).GetMethod("CaptureAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [page, $"network-login-{width}.png"])!;
            }
            wire.Mixed = true; await vm.LoadAuthInfoCommand.ExecuteAsync(null);
            Check(!vm.ShouldAutoRedirect && !vm.ShowPasswordForm && vm.OAuthProviders.Count == 1,
                "Mixed network/OAuth discovery bypasses the network choice");
            wire.Mixed = false; await vm.LoadAuthInfoCommand.ExecuteAsync(null);
            await Ready();
            wire.Pending = NewGate(); Click(NetworkButton()); await Until(() => wire.Writes == 1);
            await Task.Delay(20); page.UpdateLayout();
            Check(vm.IsLoading && All<TextBlock>(page).Any(text => text.Text == "Signing in…") && !All<Button>(page).Single(button => All<TextBlock>(button).Any(text => text.Text == "Signing in…")).IsEnabled,
                "Network pending presentation permits duplicate submission");
            Check(wire.Body == "{}" && !wire.Authenticated && wire.RequestHost == new Uri(Wire.Server).Host, "Network login body/auth/target is incorrect");
            wire.Pending.TrySetResult(Reject("network_identity_required", HttpStatusCode.Forbidden));
            await Until(() => !vm.IsLoading); wire.Pending = null;
            await Ready();
            Program.Log("TRACE network refusal: " + vm.ErrorMessage + "; session applied=" + (auth.CurrentUser is not null));
            Check(vm.ErrorMessage == "Open this server at its Fixture Network address to sign in this way." && auth.CurrentUser is null,
                "Network refusal copy is missing or grants a session");
            wire.Pending = NewGate(); Click(NetworkButton()); await Until(() => wire.Writes == 2);
            var canceled = wire.Pending;
            vm.CancelAuthFlows(); await Ready();
            wire.Pending = NewGate(); Click(NetworkButton()); await Until(() => wire.Writes == 3);
            canceled.TrySetResult(Tokens()); await Task.Delay(80);
            Check(vm.IsLoading && All<TextBlock>(page).Any(text => text.Text == "Signing in…"), "Canceled earlier request repaints the current pending attempt");
            wire.Pending.TrySetResult(Reject("not_permitted", HttpStatusCode.Forbidden));
            await Until(() => !vm.IsLoading); wire.Pending = null;
            await Ready();
            Check(auth.CurrentUser is null && wire.MeReads == 0, "Canceled network sign-in applies tokens or fetches account");
            wire.Pending = NewGate(); Click(NetworkButton()); await Until(() => wire.Writes == 4);
            client.SetBaseUrl("https://replacement-network.invalid"); client.SetBaseUrl(Wire.Server);
            wire.Pending.TrySetResult(Tokens()); await Until(() => !vm.IsLoading); await Task.Delay(80); wire.Pending = null;
            await Ready();
            Check(auth.CurrentUser is null && wire.MeReads == 0, "Late response after server ABA grants a session");
            wire.Pending = NewGate(); Click(NetworkButton()); await Until(() => wire.Writes == 5);
            wire.Pending.TrySetResult(Reject("invalid_token", HttpStatusCode.Unauthorized)); await Until(() => !vm.IsLoading); wire.Pending = null;
            await Ready();
            Check(wire.Refreshes == 0 && wire.Writes == 5 && auth.CurrentUser is null, "Network login401 replays authentication");
            var succeeded = 0; vm.LoginSucceeded += () => ++succeeded;
            Click(NetworkButton()); await Until(() => succeeded == 1);
            Check(auth.CurrentUser?.Id == "network-user" && wire.Writes == 6 && wire.MeReads == 1,
                "Network retry does not complete the verified account session exactly once");
            Program.Log("PASS actual network login owner/via responsive choice, mixed OAuth routing, pending/refusal/retry, cancellation/server ABA, empty public POST/no auth replay and verified session");
        }
        finally { vm.CancelAuthFlows(); wire.Pending?.TrySetResult(Tokens()); if (page is not null) parent.Children.Remove(page); field.SetValue(null, previous); }
    }
    private static TaskCompletionSource<HttpResponseMessage> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static HttpResponseMessage Tokens() => Reply(new { access_token = "fixture-network-access", refresh_token = "fixture-network-refresh", expires_in = 86400 });
    private static HttpResponseMessage Reject(string code, HttpStatusCode status) => Reply(new { type = "https://silo.example/docs/api/v2/problems/" + code, detail = "Fixture refusal" }, status);
    private static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static async Task Until(Func<bool> condition) { var limit = DateTime.UtcNow.AddSeconds(4); while (!condition()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Network auth boundary did not complete"); await Task.Delay(15); } }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T own) yield return own; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) foreach (var child in All<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Wire : HttpMessageHandler
    {
        internal const string Server = "https://network-auth-fixture.invalid";
        internal bool Mixed, Authenticated; internal int Writes, MeReads, Refreshes; internal string Body = "", RequestHost = "";
        internal TaskCompletionSource<HttpResponseMessage>? Pending;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/auth/providers")
            {
                var providers = new List<object> { new { id = "network", display_name = "Fixture Network", mode = "network", installation_id = "5", network_identity = new { display_name = "Fixture Owner", username = "fixture-name" } } };
                if (Mixed) providers.Add(new { id = "oauth", display_name = "Fixture SSO", mode = "oauth", installation_id = "3", native_start_path = "/api/v2/auth/oauth/3/native/start" });
                return Reply(new { items = providers, password_login = false });
            }
            if (path == "/api/v2/auth/network/5/sign-in")
            {
                ++Writes; Body = await request.Content!.ReadAsStringAsync(ct); Authenticated = request.Headers.Authorization is not null; RequestHost = request.RequestUri.Host;
                // Deliberately ignore cancellation to exercise stale-response guards.
                return Pending is { } gate ? await gate.Task : Tokens();
            }
            if (path == "/api/v2/account/me") { ++MeReads; return Reply(new { id = "network-user", username = "fixture", role = "user" }); }
            if (path == "/api/v2/auth/refresh") { ++Refreshes; throw new InvalidOperationException("Unexpected login refresh"); }
            if (path == "/api/v2/theme/branding") return Reply(new { server_name = "Fixture Silo" });
            if (path == "/api/v2/capabilities/password-reset") return Reply(new { state = "unavailable" });
            if (path == "/api/v2/auth/signup") return Reply(new { enabled = false });
            return Reply(new { items = Array.Empty<object>() });
        }
    }
}
