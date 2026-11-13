using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;

// The client-local still-watching threshold (Silo Server #3917) on the real
// SettingsPage: 21 choices, default 3, saved on this device only and never
// sent through the server settings API.
internal static class PlaybackHudNov12SettingsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        using var handler = new Wire(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://still-watching-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
        var api = new SettingsApi(client);
        var directory = Path.Combine(Program.ResultDirectory, "still-watching-settings");
        var settings = new SettingsService(directory);
        var theme = new ThemeService(settings, api);
        var vm = new SettingsViewModel(api, new CatalogApi(client), new AuthApi(client), new HistoryImportApi(client), new WatchProvidersApi(client), auth, theme, settings, new AccessibilityService(settings, theme));
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(api).AddSingleton(settings).AddSingleton<PlaybackApi>()
            .AddSingleton(new AuthApi(client)).AddSingleton(vm).AddSingleton(new NavigationService()).AddSingleton(new ToastService())
            .AddSingleton(new CardOverlayService(api)).AddSingleton(new UICustomizationService(api))
            .AddSingleton(new SiloPlayer.Core.Localization.AppLanguagePreference(api))
            .AddSingleton(sp => new SiloPlayer.Core.Localization.AppLocaleCoordinator(
                sp.GetRequiredService<SiloPlayer.Core.Localization.AppLanguagePreference>(),
                SiloPlayer.Core.Localization.LocaleService.Shared, settings, () => ["en-US"], "en-US",
                AppLocalization.PostTo(parent.DispatcherQueue)))
            .BuildServiceProvider();
        field.SetValue(null, services);
        SettingsPage? page = null;
        try
        {
            page = new SettingsPage { Width = 1400, Height = 900 };
            parent.Children.Add(page); await LayoutAsync(page);
            Invoke(page, "ShowSettingsDetail", page.FindName("PlaybackTab"));
            await LayoutAsync(page);
            var combo = (ComboBox)page.FindName("StillWatchingPromptCombo");
            Require(combo.Items.Count == 21, $"Expected 21 still-watching choices, found {combo.Items.Count}.");
            Require(combo.SelectedIndex == 3, $"Default threshold is not 3: {combo.SelectedIndex}.");
            Require((string)combo.Items[0] == "Disabled" && (string)combo.Items[1] == "1 episode" && (string)combo.Items[20] == "20 episodes",
                "Still-watching choice labels are wrong.");
            combo.StartBringIntoView(); await LayoutAsync(page);
            await CaptureAsync(page, "still-watching-setting.png");
            var before = handler.Requests.Count;
            combo.SelectedIndex = 0; await LayoutAsync(page);
            Require(new SettingsService(directory).Load().StillWatchingThreshold == 0, "Disabled was not saved on this device.");
            combo.SelectedIndex = 20; await LayoutAsync(page);
            Require(new SettingsService(directory).Load().StillWatchingThreshold == 20, "Maximum was not saved on this device.");
            var after = handler.Requests.Skip(before).ToArray();
            Require(!after.Any(request => request.Method != "GET"), "Changing the client-local threshold wrote to the server: " + string.Join(", ", after.Select(r => r.Method + " " + r.Path)));
            Require(!handler.Requests.Any(request => request.Path.Contains("passout", StringComparison.OrdinalIgnoreCase) || request.Body.Contains("passout", StringComparison.OrdinalIgnoreCase)),
                "The client-local threshold reached the server API.");
            Program.Log("PASS: SettingsPage still-watching threshold: 21 choices, default 3, local-only save of 0 and 20, no server write.");
        }
        finally
        {
            if (page != null) parent.Children.Remove(page);
            field.SetValue(null, previous);
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static async Task LayoutAsync(FrameworkElement element) { element.UpdateLayout(); await Task.Delay(150); element.UpdateLayout(); }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Settings fixture was not laid out for capture.");
        var pixels = await bitmap.GetPixelsAsync();
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private sealed record Seen(string Method, string Path, string Body);

    private sealed class Wire : HttpMessageHandler
    {
        internal ConcurrentQueue<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "still-watching-fixture.invalid") throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Enqueue(new Seen(request.Method.Method, path + request.RequestUri.Query, body));
            object reply = new { items = Array.Empty<object>() };
            if (path == "/api/v2/settings/contract/capabilities")
                reply = new { api_version = 1, manifest_revision = 15, supports_batched_effective = true, supports_idempotent_writes = true, supports_atomic_shortcuts = true, client_families = new[] { "desktop", "web" } };
            else if (path == "/api/v2/settings/values/effective")
                reply = new { items = Array.Empty<object>(), revision = 15 };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply)) };
        }
    }
}
