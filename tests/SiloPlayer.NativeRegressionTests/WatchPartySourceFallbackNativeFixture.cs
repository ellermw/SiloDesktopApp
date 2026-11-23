using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// Watch Party source fallback through the real MainWindow, PlayerService, WatchTogetherCoordinator and
/// room view model, against a fake server. Official reference: silo-server 3b2b32168 (WatchPage.tsx
/// fallbackKey/fallingBack, watchTogetherSourceFallback.ts). No real media plays: a refused or replaced
/// start ends at the server's answer.
/// </summary>
internal static class WatchPartySourceFallbackNativeFixture
{
    private const string FoundText = "Finding a compatible version for everyone...";
    private static bool _nativeResolverSet;

    internal static async Task RunAsync()
    {
        await using var scope = new Scope();
        await scope.ShowAsync();
        var failures = new List<string>();
        foreach (var (name, run) in new (string, Func<Task>)[]
        {
            ("refused room file sends one request and the replacement starts", () => ReplacedAsync(scope)),
            ("missing capability sends nothing and keeps the refusal", () => CapabilityMissingAsync(scope)),
            ("a refused request keeps the original refusal and is not repeated", () => RefusedAsync(scope)),
            ("a reply after the member left the room is dropped", () => LeftRoomAsync(scope)),
        })
        {
            try { await run(); Program.Log("PASS: watch party source fallback: " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); Program.Log("FAIL: watch party source fallback: " + name + ": " + ex); }
            finally { await scope.ResetAsync(); }
        }
        Check(failures.Count == 0, string.Join(" | ", failures));
    }

    private static async Task ReplacedAsync(Scope scope)
    {
        var wire = scope.Wire;
        wire.HoldFallback = true; wire.HoldReplacementStart = true;
        using var room = scope.JoinRoom();
        await Until(() => wire.Fallbacks.Count == 1 && wire.FallbackStarted.Task.IsCompleted, "fallback request");
        // The member sees the official wait text on the loading cover, not the refusal.
        await Until(() => scope.StatusVisible && scope.StatusText == FoundText && !scope.ErrorVisible && scope.OverlayVisible, "pending cover");
        Check(wire.Starts.Count == 1 && wire.Starts[0].GetProperty("file_id").GetString() == "7", "The refused start was not for the room's file 7.");
        await scope.CaptureAsync("watch-party-fallback-pending", 1440); await scope.CaptureAsync("watch-party-fallback-pending", 800);
        var request = wire.Fallbacks.Single();
        Check(request.Path == "/api/v2/watch-together/rooms/room/source-fallback" && request.Method == "POST", "Wrong source-fallback route: " + request.Method + " " + request.Path);
        Check(request.RoomToken == "fixture-proof", "The request did not carry the room's own proof in X-Room-Token.");
        Check(request.Profile == "fixture", "The request did not carry the member's profile.");
        var body = JsonDocument.Parse(request.Body).RootElement;
        Check(body.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(["failed_file_id", "reason", "selection_revision"]),
            "Unexpected source-fallback body fields.");
        Check(body.GetProperty("failed_file_id").ValueKind == JsonValueKind.String && body.GetProperty("failed_file_id").GetString() == "7",
            "failed_file_id was not the string \"7\".");
        Check(body.GetProperty("selection_revision").GetInt32() == 3 && body.GetProperty("reason").GetString() == "hdr_transcode_unsupported",
            "Wrong selection revision or reason.");
        Check(wire.CapabilityReads >= 1, "The server's playback capabilities were not read before sending.");

        wire.ReleaseFallback.TrySetResult();
        await Until(() => wire.Starts.Count == 2 && wire.ReplacementStarted.Task.IsCompleted, "replacement start");
        Check(wire.Starts[1].GetProperty("file_id").GetString() == "8", "The replacement start was not for the room's new file 8.");
        Check(Math.Abs(wire.Starts[1].GetProperty("start_position").GetDouble() - 91.5) < 0.01, "The replacement start lost the room's position anchor.");
        Check(room.Room?.SelectionRevision == 4 && room.Room.SelectedFileId == 8, "The returned room was not applied.");
        // The new start owns the cover now: the wait text is gone.
        await Until(() => !scope.StatusVisible && scope.OverlayVisible && !scope.ErrorVisible, "replacement cover");
        await scope.CaptureAsync("watch-party-fallback-replacing", 1440); await scope.CaptureAsync("watch-party-fallback-replacing", 800);
        wire.ReleaseReplacement.TrySetResult();
        await Until(() => scope.ErrorVisible, "replacement end");
        await Task.Delay(300);
        Check(wire.Fallbacks.Count == 1 && wire.Starts.Count == 2, "The fallback or the start repeated by itself.");
    }

    private static async Task CapabilityMissingAsync(Scope scope)
    {
        var wire = scope.Wire;
        wire.SupportsFallback = false;
        using var room = scope.JoinRoom();
        await Until(() => scope.ErrorVisible && wire.CapabilityReads >= 1, "original refusal");
        await Task.Delay(300);
        Check(wire.Fallbacks.Count == 0, "A fallback was sent although the server does not list watch_party_source_fallback_v1.");
        Check(scope.ErrorTitle == "This HDR format can't be converted" && !scope.StatusVisible,
            "The original refusal is not what the member sees: " + scope.ErrorTitle);
        Check(wire.Starts.Count == 1, "The start repeated.");
        await scope.CaptureAsync("watch-party-fallback-refusal", 1440); await scope.CaptureAsync("watch-party-fallback-refusal", 800);
    }

    private static async Task RefusedAsync(Scope scope)
    {
        var wire = scope.Wire;
        wire.FallbackStatus = HttpStatusCode.Conflict;
        using var room = scope.JoinRoom();
        await Until(() => scope.ErrorVisible && wire.Fallbacks.Count == 1, "original refusal after a refused request");
        await Task.Delay(500);
        Check(wire.Fallbacks.Count == 1 && wire.Starts.Count == 1, "A refused fallback was retried or restarted playback.");
        Check(scope.ErrorTitle == "This HDR format can't be converted" && !scope.StatusVisible, "The original refusal is not what the member sees: " + scope.ErrorTitle);
        Check(room.Room?.SelectionRevision == 3 && room.Room.SelectedFileId == 7, "A refused fallback changed the room.");
    }

    private static async Task LeftRoomAsync(Scope scope)
    {
        var wire = scope.Wire;
        wire.HoldFallback = true;
        using var room = scope.JoinRoom();
        await Until(() => wire.Fallbacks.Count == 1 && scope.StatusVisible, "pending fallback");
        scope.Coordinator.ClearActiveRoom();
        wire.ReleaseFallback.TrySetResult();
        await Until(() => !scope.OverlayVisible, "cover released after leaving");
        await Task.Delay(400);
        Check(wire.Starts.Count == 1 && wire.Fallbacks.Count == 1, "A reply after the member left the room started playback or sent again.");
        Check(room.Room?.SelectionRevision == 3 && room.Room.SelectedFileId == 7, "A reply after the member left the room was applied.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static async Task Until(Func<bool> done, string what)
    {
        for (var i = 0; i < 400 && !done(); i++) await Task.Delay(20);
        Check(done(), "Timed out waiting for: " + what);
    }

    private static void EnsureNativeResolver()
    {
        if (_nativeResolverSet) return;
        _nativeResolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(MpvPlayer).Assembly, (name, _, _) => name == "libmpv-2.dll"
            ? NativeLibrary.Load(Path.Combine(Program.AppDirectory, name)) : IntPtr.Zero);
    }

    private static WatchTogetherRoomSnapshot Snapshot(int revision, int file, int generation, double anchor) => new()
    {
        RoomId = "room", Code = "ABCD", Phase = "playing", PlaybackState = "waiting", SelectionMode = "host_pick",
        SelectionRevision = revision, SelectedContentId = "movie", SelectedFileId = file, Generation = generation,
        AnchorPositionSeconds = anchor, SelfRole = "guest", MemberCount = 2,
        Members =
        [
            new() { UserId = 1, ProfileId = "host", DisplayName = "Host", IsHost = true, Connected = true },
            new() { UserId = 2, ProfileId = "fixture", DisplayName = "Fixture", IsSelf = true, Connected = true },
        ],
    };

    private sealed class Scope : IAsyncDisposable
    {
        private readonly FieldInfo _slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previousServices;
        private readonly object? _previousWindow;
        internal Wire Wire { get; } = new();
        internal SiloApiClient Client { get; }
        internal PlaybackApi Playback { get; }
        internal PlayerService Player { get; }
        internal MainWindow Window { get; }
        internal ServiceProvider Services { get; }
        internal WatchTogetherCoordinator Coordinator => Services.GetRequiredService<WatchTogetherCoordinator>();
        private FrameworkElement Root => (FrameworkElement)Window.Content;
        internal bool OverlayVisible => Root.FindName("LoadingOverlay") is UIElement { Visibility: Visibility.Visible };
        internal bool StatusVisible => ((UIElement)Root.FindName("LoadingStatusText")).Visibility == Visibility.Visible;
        internal string StatusText => ((TextBlock)Root.FindName("LoadingStatusText")).Text;
        internal bool ErrorVisible => ((UIElement)Root.FindName("ErrorState")).Visibility == Visibility.Visible && OverlayVisible;
        internal string ErrorTitle => ((TextBlock)Root.FindName("ErrorTitle")).Text;

        internal Scope()
        {
            _previousServices = _slot.GetValue(null);
            _previousWindow = typeof(App).GetProperty(nameof(App.MainWindowInstance))!.GetValue(null);
            var http = new HttpClient(Wire); Client = new(http); Client.SetBaseUrl("https://wtfallback.fixture.invalid");
            var auth = new AuthService(Client, new AuthApi(Client), credentialStore: null); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
            auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
            Playback = new(Client); var catalog = new CatalogApi(Client);
            var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "wtfallback-" + Guid.NewGuid().ToString("N")));
            var preferences = settings.Load(); preferences.SiloCastEnabled = false; settings.Save(preferences);
            Player = new(Playback, catalog, auth, Client, settings, new SettingsApi(Client));
            Services = new ServiceCollection().AddSingleton(Client).AddSingleton(auth).AddSingleton(http).AddSingleton(settings)
                .AddSingleton(Playback).AddSingleton(catalog).AddSingleton(Player)
                .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<RequestsApi>().AddSingleton<MediaMaintenanceApi>()
                .AddSingleton<NotificationsApi>().AddSingleton<HistoryImportApi>().AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>()
                .AddSingleton<CollectionsApi>().AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
                .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>().AddSingleton<CardOverlayService>()
                .AddSingleton<UICustomizationService>().AddSingleton<ToastService>().AddSingleton<EpisodeSpoilerPreferences>()
                .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>().AddSingleton<WatchTogetherCoordinator>()
                .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "wtfallback-images-" + Guid.NewGuid().ToString("N"))))
                .AddSingleton<ItemDetailPrefetchCache>().AddTransient<ItemDetailViewModel>().AddTransient<WatchTogetherRoomViewModel>().BuildServiceProvider();
            _slot.SetValue(null, Services); Window = new(); typeof(App).GetProperty(nameof(App.MainWindowInstance))!.SetValue(null, Window);
            var nav = (NavigationView)Root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate(typeof(RoutedEventHandler), Window);
        }

        internal async Task ShowAsync()
        {
            Window.AppWindow.Move(new(-20000, -20000)); Window.AppWindow.ResizeClient(new(1440, 900)); Window.AppWindow.Show(false);
            await Task.Delay(80);
            await Services.GetRequiredService<EpisodeSpoilerPreferences>().EnsureLoadedAsync();
            await Services.GetRequiredService<UICustomizationService>().EnsureLoadedAsync();
            EnsureNativeResolver();
            var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null");
            typeof(PlayerService).GetField("_mpv", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Player, mpv);
            _ = Coordinator;
        }

        /// <summary>A guest in a playing room whose selected file is 7; adopting it starts that file like any room start.</summary>
        internal WatchTogetherRoomViewModel JoinRoom()
        {
            var room = new WatchTogetherRoomViewModel(Playback, Client)
            {
                RoomId = "room", RoomToken = "fixture-proof", Room = Snapshot(3, 7, 5, 91.5), ConnectionState = "connected",
            };
            Coordinator.SetActiveRoom(room);
            return room;
        }

        internal async Task CaptureAsync(string name, int width)
        {
            Window.AppWindow.ResizeClient(new(width, 900)); await Task.Delay(250);
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)Root.FindName("LoadingOverlay"), $"{name}-{width}.png");
        }

        internal async Task ResetAsync()
        {
            Wire.ReleaseFallback.TrySetResult(); Wire.ReleaseReplacement.TrySetResult();
            Coordinator.ClearActiveRoom();
            await Player.CloseAsync();
            Wire.Reset();
            Window.AppWindow.ResizeClient(new(1440, 900));
            Window.HideLoadingOverlay(typeof(MainWindow).GetField("_loadingOverlayOwner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Window));
            await Task.Delay(60);
        }

        public async ValueTask DisposeAsync()
        {
            Wire.ReleaseFallback.TrySetResult(); Wire.ReleaseReplacement.TrySetResult();
            Coordinator.ClearActiveRoom();
            await Player.CloseAsync();
            await Services.GetRequiredService<SiloPlayer.Services.SiloCast.SiloCastReceiverService>().ShutdownAsync();
            await Task.Delay(100);
            typeof(MainWindow).GetField("_receiverShutdownComplete", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Window, true);
            Window.Close(); await Task.Delay(100);
            await Services.DisposeAsync();
            typeof(App).GetProperty(nameof(App.MainWindowInstance))!.SetValue(null, _previousWindow); _slot.SetValue(null, _previousServices);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal sealed record Seen(string Method, string Path, string? RoomToken, string? Profile, string Body);
        internal List<JsonElement> Starts { get; } = [];
        internal List<Seen> Fallbacks { get; } = [];
        internal int CapabilityReads;
        internal bool SupportsFallback = true, HoldFallback, HoldReplacementStart;
        internal HttpStatusCode FallbackStatus = HttpStatusCode.OK;
        internal TaskCompletionSource FallbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseFallback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReplacementStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseReplacement = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Reset()
        {
            Starts.Clear(); Fallbacks.Clear(); CapabilityReads = 0;
            SupportsFallback = true; HoldFallback = HoldReplacementStart = false; FallbackStatus = HttpStatusCode.OK;
            FallbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); ReleaseFallback = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ReplacementStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); ReleaseReplacement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "wtfallback.fixture.invalid", "External fixture request refused.");
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v2/settings/contract/capabilities")
                return Json("""{"api_version":1,"manifest_revision":17,"supports_batched_effective":true}""");
            if (path == "/api/v2/playback/capabilities")
            {
                ++CapabilityReads;
                return Json("""{"allowed":true,"state":"available","installation_id":"fixture-install","protocol_versions":[3],"features":["fixed_media_file_v1"FALLBACK]}"""
                    .Replace("FALLBACK", SupportsFallback ? ",\"watch_party_source_fallback_v1\"" : ""));
            }
            if (path.StartsWith("/api/v2/watch/", StringComparison.Ordinal))
                return Json("""{"content_id":"movie","type":"movie","title":"Fixture movie","versions":[{"file_id":7,"duration":1200,"resolution":"2160p","container":"mkv","codec_video":"hevc","codec_audio":"eac3"},{"file_id":8,"duration":1200,"resolution":"1080p","container":"mkv","codec_video":"h264","codec_audio":"aac"}],"user_data":{"position_seconds":0,"duration_seconds":1200}}""");
            if (path == "/api/v2/playback/start")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var start = json.RootElement.Clone();
                Starts.Add(start);
                if (start.GetProperty("file_id").GetString() == "7")
                    return Json("""{"protocol_version":3,"outcome":"unavailable","terminal":{"reason":"hdr_transcode_unsupported","message":"This HDR file can't be converted for this device.","retryable":false}}""");
                ReplacementStarted.TrySetResult();
                if (HoldReplacementStart) await ReleaseReplacement.Task.WaitAsync(ct);
                return Json("""{"protocol_version":3,"outcome":"unavailable","terminal":{"reason":"source_unavailable","retryable":false}}""");
            }
            if (path == "/api/v2/watch-together/rooms/room/source-fallback")
            {
                Fallbacks.Add(new(request.Method.Method, path, request.Headers.TryGetValues("X-Room-Token", out var proof) ? proof.Single() : null,
                    request.Headers.TryGetValues("X-Profile-Id", out var profile) ? profile.Single() : null, await request.Content!.ReadAsStringAsync(ct)));
                FallbackStarted.TrySetResult();
                if (HoldFallback) await ReleaseFallback.Task.WaitAsync(ct);
                if (FallbackStatus != HttpStatusCode.OK)
                    return new(FallbackStatus) { Content = new StringContent("""{"title":"Conflict","status":409,"detail":"No lower-ranked version"}""", Encoding.UTF8, "application/problem+json") };
                return Json("""{"room":{"room_id":"room","code":"ABCD","phase":"playing","playback_state":"waiting","selection_mode":"host_pick","guest_control_policy":"host_only","self_role":"guest","selection_revision":4,"selected_content_id":"movie","selected_file_id":"8","generation":6,"anchor_position_seconds":91.5,"anchor_updated_at":"2026-01-01T00:00:00Z","member_count":2,"members":[{"user_id":1,"profile_id":"host","display_name":"Host","is_host":true,"connected":true},{"user_id":2,"profile_id":"fixture","display_name":"Fixture","is_self":true,"connected":true}]},"room_access_token":"renewed-proof"}""");
            }
            return Json("""{"items":[],"settings":[],"sections":[],"profiles":[]}""");
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
