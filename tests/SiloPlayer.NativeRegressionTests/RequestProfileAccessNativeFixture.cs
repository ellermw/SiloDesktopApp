using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class RequestProfileAccessNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync()
    {
        var servicesField = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var windowProperty = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var previousServices = servicesField.GetValue(null);
        var previousWindow = windowProperty.GetValue(null);
        using var wire = new Wire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-profile-access.invalid");
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SetTokens("fixture-access", "fixture-refresh", 3600);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture-profile", profile: new Profile { Id = "fixture-profile", Name = "Fixture", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "request-profile-access-settings"));
        var preferences = settings.Load(); preferences.SiloCastEnabled = false; settings.Save(preferences);
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(http).AddSingleton(settings)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<HomeApi>().AddSingleton<CollectionsApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>()
            .AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<WatchlistViewModel>().AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>()
            .AddSingleton<ToastService>().AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "request-profile-access-images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        servicesField.SetValue(null, services);
        MainWindow? window = null;
        try
        {
            try { window = new MainWindow(); }
            catch (Exception ex) { throw new InvalidOperationException("INFRA: actual Requests access shell construction failed.", ex); }
            windowProperty.SetValue(null, window);
            var shell = (FrameworkElement)window.Content;
            var nav = (NavigationView)shell.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Private)!
                .CreateDelegate(typeof(RoutedEventHandler), window);
            nav.IsPaneVisible = true;
            Set(window, "_navInitialized", true);
            Set(window, "_playerPrewarmed", true);
            var shellKey = (string)Call(window, "GetAuthenticatedShellKey")!;
            Set(window, "_hydratedShellKey", shellKey);
            using var hydration = new CancellationTokenSource();
            Set(window, "_shellHydrationCts", hydration);
            window.AppWindow.Move(new(-20000, -20000));
            window.AppWindow.ResizeClient(new(1100, 800));
            window.AppWindow.Show(false);
            await (Task)Call(window, "UpdateProfileDisplayAsync", shellKey, hydration.Token)!;
            await UntilAsync(() => wire.ProfileReads > 0 && ((TextBlock)shell.FindName("ProfileNameText")).Text == "Fixture",
                "normal shell profile read did not complete");
            var api = services.GetRequiredService<RequestsApi>();
            if (!api.ProfileRequestsAllowed || auth.SelectedProfileId != "fixture-profile")
                throw new InvalidOperationException("INFRA: remote profile setup is not the allowed selected profile.");
            Program.Log("PASS: real shell profile display reads the allowed selected profile before access invalidation.");
            var identity = client.CaptureIdentityContext();
            await ChangeAccessAsync(false);
            if (auth.SelectedProfile?.RequestsAllowed != false || api.ProfileRequestsAllowed || api.DiscoveryAllowed)
                throw new InvalidOperationException("Remote profile block is not applied by the real access-change refresh.");
            Program.Log("PASS: remote profile block updates selected metadata and Requests discovery through the real access-change refresh.");
            await ChangeAccessAsync(true);
            if (auth.SelectedProfile?.RequestsAllowed != true || !api.ProfileRequestsAllowed || !api.DiscoveryAllowed)
                throw new InvalidOperationException("Remote profile unblock is not applied by the real access-change refresh.");
            if (auth.SelectedProfileId != "fixture-profile" || !client.IsCurrentIdentityContext(identity))
                throw new InvalidOperationException("Profile metadata refresh changed the selected identity.");
            Program.Log("PASS: remote profile unblock restores Requests without reselecting the profile or replacing its identity.");

            async Task ChangeAccessAsync(bool allowed)
            {
                wire.RequestsAllowed = allowed;
                var reads = wire.LibraryReads;
                client.InvalidateAccessContext();
                Call(window, "OnAccessChanged", client.CaptureContext());
                await UntilAsync(() => wire.LibraryReads > reads && !services.GetRequiredService<MainViewModel>().IsLoadingLibraries,
                    "real access-change shell refresh did not reach and settle its library read");
                // Let profile-display dispatcher work queued by this same refresh settle.
                for (var i = 0; i < 100 && auth.SelectedProfile?.RequestsAllowed != allowed; i++) await Task.Delay(10);
            }
        }
        finally
        {
            if (window != null)
            {
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Closed(object sender, WindowEventArgs args) => closed.TrySetResult();
                window.Closed += Closed;
                try
                {
                    await RequestFixtureLifecycle.DetachAsync((FrameworkElement)window.Content, () => window.Close());
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Program.Log("PASS: Requests profile fixture lifecycle barrier completed actual Window.Closed.");
                }
                finally { window.Closed -= Closed; }
            }
            windowProperty.SetValue(null, previousWindow);
            servicesField.SetValue(null, previousServices);
        }
    }

    private static void Set(object target, string name, object value) => typeof(MainWindow).GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(target, args);
    private static async Task UntilAsync(Func<bool> ready, string failure)
    {
        for (var i = 0; i < 300; i++) { if (ready()) return; await Task.Delay(20); }
        throw new InvalidOperationException("INFRA: " + failure);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool RequestsAllowed = true;
        internal int ProfileReads, LibraryReads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host != "request-profile-access.invalid")
                throw new InvalidOperationException("INFRA: unexpected external profile fixture request.");
            var path = request.RequestUri.AbsolutePath;
            string json;
            if (path == "/api/v2/profiles")
            {
                ProfileReads++;
                json = JsonSerializer.Serialize(new { items = new[] { new { id = "fixture-profile", name = "Fixture", is_primary = true, requests_allowed = RequestsAllowed } }, requests_allowed_supported = true });
            }
            else if (path == "/api/v2/requests/status")
                json = JsonSerializer.Serialize(new { requests_enabled = true, allowed = RequestsAllowed, watchlist_titles_supported = RequestsAllowed });
            else
            {
                if (path == "/api/v2/user/libraries") LibraryReads++;
                json = "{\"items\":[],\"page\":{\"has_more\":false},\"settings\":{}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
