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

internal static class ProtectedProfileShellNativeFixture
{
    internal static async Task RunAsync()
    {
        const BindingFlags instance = BindingFlags.NonPublic | BindingFlags.Instance;
        var servicesField = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var mainProperty = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previous = servicesField.GetValue(null); var previousMain = mainProperty.GetValue(null);
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://protected-shell.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-account-access", "fixture-account-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "shell-settings"));
        var prefs = local.Load(); prefs.SiloCastEnabled = false; prefs.TasteSeedDismissedProfileIds.Add("protected"); local.Save(prefs);
        await using var provider = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(local).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<ProfileSelectViewModel>().AddSingleton<HomeApi>().AddSingleton<HomeViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "shell-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, provider);
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); mainProperty.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            var loaded = (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", instance)!.CreateDelegate(typeof(RoutedEventHandler), window);
            nav.Loaded -= loaded; // Synthetic authenticated account only; never restore the installed account.
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1080, 900)); window.AppWindow.Show(false);
            var navigation = provider.GetRequiredService<NavigationService>();
            navigation.Navigate<ProfileSelectPage>();
            var page = (ProfileSelectPage)navigation.Frame!.Content;
            await Until(() => page.ViewModel.Profiles.Count == 2 && !page.ViewModel.IsLoading);
            await Task.Delay(120);
            Click(All<Button>(page).Single(button => button.Tag is Profile p && p.Id == "protected"));
            await Until(() => Dialogs(root).Any());
            var dialog = Dialogs(root).Single(); var input = All<PasswordBox>(dialog).Single();
            input.Password = "1234"; await Task.Delay(30);
            Click(All<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await Until(() => navigation.Frame.Content is HomePage && !Dialogs(root).Any());
            await Until(() => wire.AuthorizedReads > 0);
            Require(auth.SelectedProfileId == "protected" && client.ProfileToken == "fixture-profile-proof", "Profile proof must reach real shell/Home.");
            Require(wire.Verifications == 1 && wire.RefusedReads == 0, "Real shell must use issued proof without repeated PIN or rejected reads.");
            Require(nav.IsPaneVisible, "Successful native PIN login must reveal authenticated navigation.");
            Program.Log("PASS: frozen286 actual profile card/PIN dialog -> original OnProfileSelected -> MainWindow authenticated gate -> actual HomePage and shell; issued proof authorizes subsequent reads, with one verification and no rejected reads.");
            await MediaParityNativeFixture.CaptureAsync(root, "issue8-authenticated-home.png");
            navigation.Frame.Content = null; await Task.Delay(100);
        }
        finally
        {
            if (window != null) { window.Close(); await Task.Delay(100); }
            mainProperty.SetValue(null, previousMain); servicesField.SetValue(null, previous);
        }
    }
    private static IEnumerable<ContentDialog> Dialogs(FrameworkElement root) => Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(popup => All<ContentDialog>(popup.Child));
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 160 && !ready(); i++) await Task.Delay(25); Require(ready(), "Protected shell did not settle."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Wire : HttpMessageHandler
    {
        public int Verifications, AuthorizedReads, RefusedReads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "protected-shell.fixture.invalid") throw new InvalidOperationException("Unexpected network.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("verify-pin"))
            {
                Verifications++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Require(body.RootElement.EnumerateObject().Count() == 1 && body.RootElement.GetProperty("pin").GetString() == "1234", "PIN must be a separate native verification request.");
                return Reply(new { valid = true, profile_token = "fixture-profile-proof", expires_at = (string?)null });
            }
            if (path == "/api/v2/profiles") return Reply(new { items = new object[] {
                new { id = "protected", name = "Protected", has_pin = true, is_primary = true },
                new { id = "unprotected", name = "Unprotected", has_pin = false, is_primary = false } } });
            if (request.Headers.TryGetValues("X-Profile-Id", out var ids) && ids.Single() == "protected")
            {
                if (!request.Headers.TryGetValues("X-Profile-Token", out var proofs) || proofs.Single() != "fixture-profile-proof")
                { RefusedReads++; return new(HttpStatusCode.Forbidden) { Content = new StringContent("""{"type":"/docs/api/v2/problems/profile_verification_required","detail":"Synthetic profile proof required"}""") }; }
                AuthorizedReads++;
            }
            return Reply(path switch
            {
                "/api/v2/settings/contract/capabilities" => new { api_version = 1, manifest_revision = 22, supports_idempotent_writes = true, client_families = new[] { "desktop", "web" }, supports_batched_effective = true },
                _ => (object)new { items = Array.Empty<object>(), settings = Array.Empty<object>(), sections = Array.Empty<object>(), devices = Array.Empty<object>(), providers = Array.Empty<object>() },
            });
        }
        private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
