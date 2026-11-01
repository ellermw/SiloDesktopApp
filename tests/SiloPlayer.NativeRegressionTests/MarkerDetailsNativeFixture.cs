using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class MarkerDetailsNativeFixture
{
    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://marker-details.invalid"); client.SetProfile("fixture");
        var settingsApi = new SettingsApi(client); var playbackApi = new PlaybackApi(client); var catalog = new CatalogApi(client);
        using var auth = new AuthService(client, new AuthApi(client));
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "marker-settings"));
        var theme = new ThemeService(local, settingsApi);
        var vm = new SettingsViewModel(settingsApi, catalog, new AuthApi(client), new HistoryImportApi(client),
            new WatchProvidersApi(client), auth, theme, local, new AccessibilityService(local, theme));
        using var player = new PlayerService(playbackApi, catalog, auth, client, local, settingsApi);
        using var manager = new PlaybackManager(playbackApi, catalog, auth, client);
        using var services = new ServiceCollection().AddSingleton(vm).AddSingleton(player).AddSingleton(client).AddSingleton(auth).BuildServiceProvider();
        slot.SetValue(null, new Services(services, previous));
        SettingsPage? page = null; PlayerOverlay? overlay = null;
        try
        {
            page = new SettingsPage { Width = 800, Height = 700 }; parent.Children.Add(page);
            await Until(() => page.IsLoaded);
            page.SelectSettingsSection("Playback");
            var confidence = (ToggleSwitch)page.FindName("MarkerConfidenceToggle");
            var source = (ToggleSwitch)page.FindName("MarkerSourceToggle");
            Check(!confidence.IsOn && !source.IsOn && !vm.MarkerDetailsSettingsAvailable, "Optional settings default off");
            Set(vm, "_markerDetailsSettingsContext", client.CaptureContext()); vm.MarkerDetailsSettingsAvailable = true;
            ((IToggleProvider)new ToggleSwitchAutomationPeer(confidence).GetPattern(PatternInterface.Toggle)).Toggle();
            await Until(() => wire.Writes.Count == 1 && wire.Deletes == 1);
            Check(vm.ShowMarkerConfidence && wire.Writes[0].Key == MarkerDetailsPreferences.ConfidenceKey && wire.Writes[0].Value,
                "Actual confidence toggle saves typed profile setting and removes device override");
            ((IToggleProvider)new ToggleSwitchAutomationPeer(source).GetPattern(PatternInterface.Toggle)).Toggle();
            await Until(() => wire.Writes.Count == 2 && wire.Deletes == 2);
            Check(vm.ShowMarkerSource, "Actual source toggle reaches view model");

            var version = new FileVersion { FileId = 7, Intro = new() { Start = 5, End = 30, Source = "plugin", Provider = "introdb", Confidence = .92 },
                MarkerSegments = [new() { Kind = "intro", StartSeconds = 5, EndSeconds = 30 }, new() { Kind = "intro", StartSeconds = 40, EndSeconds = 50 }] };
            var detail = new WatchDetailResponse { ContentId = "fixture", Intro = version.Intro, Versions = [version] };
            manager.UseWatchDetail(detail);
            typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, new PlaybackStartResponse { MediaFileId = 7 });
            Set(player, "_playbackManager", manager); typeof(PlayerService).GetProperty("Versions")!.SetValue(player, detail.Versions);
            overlay = new PlayerOverlay { Width = 800, Height = 500 }; parent.Children.Add(overlay);
            await player.RefreshMarkerDetailsPreferencesAsync();
            void Paint(double position) { typeof(PlayerService).GetProperty("Position")!.SetValue(player, position); Call(overlay, "UpdateSkipButtons", position); }
            Paint(10);
            var text = (TextBlock)overlay.FindName("SkipMarkerDetailsText");
            Check(text.Visibility == Visibility.Visible && text.Text == "92% · TheIntroDB", "Native skip control shows effective marker details");
            await MediaParityNativeFixture.CaptureAsync(overlay, "marker-details-overlay.png");
            Paint(45);
            Check(text.Text == "92% · TheIntroDB" && player.ActiveIntro?.Source == "plugin", "Repeated marker retains per-kind provenance");
            version.Intro = new() { Start = 5, End = 30, Source = "manual" }; Paint(10);
            Check(text.Text == "Edited", "Live manual replacement does not invent confidence");
            version.Intro = null; Paint(10);
            Check(text.Visibility == Visibility.Collapsed && text.Text == "", "Version without metadata never inherits primary-file provenance");
            version.Intro = detail.Intro;
            wire.HeldEffective = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = player.RefreshMarkerDetailsPreferencesAsync(); await Until(() => wire.HeldRequested);
            client.SetProfile("other"); wire.HeldEffective.SetResult(); await held;
            Paint(10);
            Check(text.Visibility == Visibility.Collapsed && !player.MarkerDetails.ShowSource, "Retired profile preference response cannot paint marker details");
            client.SetProfile("fixture"); Set(vm, "_markerDetailsSettingsContext", client.CaptureContext());

            // Two queued failures on one key must restore the last accepted
            // value, rather than the optimistic value preceding the last edit.
            wire.HeldEffective = null; wire.FailWrites = true;
            wire.HeldWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.ShowMarkerSource = false; await Until(() => wire.HeldWriteRequested);
            vm.ShowMarkerSource = true;
            wire.HeldWrite.SetResult();
            await Until(() => wire.FailedWrites == 2);
            await Task.Delay(100);
            Check(vm.ShowMarkerSource, "Queued failed toggles restore last committed setting");
            wire.HeldWriteRequested = false;
            wire.HeldWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.ShowMarkerSource = false; await Until(() => wire.HeldWriteRequested);
            vm.ShowMarkerConfidence = false;
            wire.HeldWrite.SetResult(); await Until(() => wire.FailedWrites == 4);
            await Task.Delay(100);
            Check(vm.ShowMarkerSource && vm.ShowMarkerConfidence, "An unrelated later toggle cannot suppress a failed setting rollback");
            Program.Log("PASS: marker toggles/profile persistence, native details, repeated/live/absent provenance and stale profile ownership.");
        }
        finally
        {
            wire.HeldEffective?.TrySetResult(); wire.HeldWrite?.TrySetResult();
            if (overlay is not null) parent.Children.Remove(overlay);
            if (page is not null) parent.Children.Remove(page);
            await Task.Delay(100); Set(player, "_playbackManager", null); slot.SetValue(null, previous);
        }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Check(ready(), "Marker fixture did not settle"); }
    private static void Set(object instance, string field, object? value) => instance.GetType().GetField(field, Fields)!.SetValue(instance, value);
    private static void Call(object instance, string method, params object[] args) => instance.GetType().GetMethod(method, Fields)!.Invoke(instance, args);
    private sealed class Services(IServiceProvider own, IServiceProvider fallback) : IServiceProvider
    { public object? GetService(Type type) => own.GetService(type) ?? fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly List<(string Key, bool Value)> Writes = [];
        internal int Deletes, FailedWrites;
        internal bool Confidence, Source, FailWrites, HeldRequested, HeldWriteRequested;
        internal TaskCompletionSource? HeldEffective, HeldWrite;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "marker-details.invalid", "Unexpected fixture origin");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Json("""{"api_version":1,"manifest_revision":24,"supports_batched_effective":true,"supports_idempotent_writes":true}""");
            if (path.EndsWith("/effective"))
            {
                if (HeldEffective is { } held) { HeldRequested = true; await held.Task; }
                return Json(JsonSerializer.Serialize(new { items = new[] { new { key = MarkerDetailsPreferences.ConfidenceKey, value = Confidence, source = "profile" }, new { key = MarkerDetailsPreferences.SourceKey, value = Source, source = "profile" } } }));
            }
            if (request.Method == HttpMethod.Put)
            {
                if (HeldWrite is { } held) { HeldWriteRequested = true; await held.Task; }
                if (FailWrites) { FailedWrites++; return new(HttpStatusCode.InternalServerError) { Content = new StringContent("fixture write failed") }; }
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var key = path.Contains(MarkerDetailsPreferences.ConfidenceKey) ? MarkerDetailsPreferences.ConfidenceKey : MarkerDetailsPreferences.SourceKey;
                var value = body.RootElement.GetProperty("value").GetBoolean(); Writes.Add((key, value));
                if (key == MarkerDetailsPreferences.ConfidenceKey) Confidence = value; else Source = value;
                return Json("{}");
            }
            if (request.Method == HttpMethod.Delete) { Deletes++; return new(HttpStatusCode.NoContent); }
            return Json("{}"); // unrelated SettingsPage read-only initialization
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
