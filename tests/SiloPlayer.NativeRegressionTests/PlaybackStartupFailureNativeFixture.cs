using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class PlaybackStartupFailureNativeFixture
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync()
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_STARTUP_FAILURE_MODE") ?? "initial";
        using var media = new IgnoredRangeServer { ValidMedia = mode != "initial" };
        using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://startup-failure.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api));
        auth.SetTokens("fixture-access", "fixture-refresh", 86400);
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture", IsPrimary = true });
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "local"));
        var local = settings.Load(); local.SiloCastEnabled = false; settings.Save(local);
        var slot = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var main = typeof(App).GetProperty(nameof(App.MainWindowInstance))!;
        var original = slot.GetValue(null); var originalMain = main.GetValue(null);
        await using var provider = new ServiceCollection().AddSingleton(api).AddSingleton(auth).AddSingleton(settings).AddSingleton(http)
            .AddSingleton<CredentialStore>().AddSingleton<AuthApi>().AddSingleton<SettingsApi>().AddSingleton<CatalogApi>()
            .AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
            .AddSingleton<WatchProvidersApi>().AddSingleton<RecommendationsApi>().AddSingleton<CollectionsApi>()
            .AddSingleton<EventChannelClient>().AddSingleton<ThemeService>().AddSingleton<AccessibilityService>()
            .AddSingleton<NavigationService>().AddSingleton<MainViewModel>().AddSingleton<SettingsViewModel>()
            .AddSingleton<CardOverlayService>().AddSingleton<UICustomizationService>().AddSingleton<ToastService>()
            .AddSingleton<EpisodeSpoilerPreferences>().AddSingleton<PlayerService>()
            .AddSingleton<SiloPlayer.Services.SiloCast.SiloCastReceiverService>()
            .AddSingleton(new ImageService(Path.Combine(Program.ResultDirectory, "images")))
            .AddSingleton<ItemDetailPrefetchCache>().BuildServiceProvider();
        slot.SetValue(null, provider);
        var service = provider.GetRequiredService<PlayerService>();
        using var manager = new PlaybackManager(provider.GetRequiredService<PlaybackApi>(), provider.GetRequiredService<CatalogApi>(), auth, api);
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); main.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Flags)!.CreateDelegate(typeof(RoutedEventHandler), window);
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1280, 720)); window.AppWindow.Show(false);
            await Task.Delay(120);
            manager.UseWatchDetail(new() { ContentId = "fixture-episode", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200, Bitrate = 1536, Container = "mkv", CodecAudio = "aac", AudioTracks = [new() { Language = "eng", Default = true }] }] });
            await manager.StartSessionAsync(42, 649.47);
            manager.UpdatePosition(649.47, true);
            Call(service, "EnsureMpvInitialized");
            var mpv = (MpvPlayer)Field(service, "_mpv")!;
            mpv.SetProperty("ao", "null");
            Set(service, "_playbackManager", manager);
            Property(service, "ContentId", "fixture-episode"); Property(service, "Versions", manager.WatchDetail!.Versions);
            service.SetState(PlayerState.Expanded);
            var preparedType = typeof(PlayerService).GetNestedType("PreparedPlaybackTransport", BindingFlags.NonPublic)!;
            var url = media.Origin + "/stream/direct/fixture";
            var prepared = Activator.CreateInstance(preparedType, Flags | BindingFlags.Public, null,
                [PlaybackTransportPlanner.Plan("direct", url), url, mode == "initial" ? 649.47d : 0d, 0d, 0d, (double?)1200d, true], null)!;
            if (mode == "stale")
            {
                // Queue the old attempt's terminal UI action, then synchronously
                // accept a newer native load before the dispatcher can run it.
                Call(service, "EnterPlaybackTerminalState", new InvalidOperationException("Fixture old attempt"), 0d, "fixture-old", false);
            }
            Call(service, "ApplyPreparedTransport", prepared); Call(service, "BeginMpvLoad", prepared, false);
            if (mode != "initial")
            {
                await Until(() => (bool)Field(service, "_transportHasFirstOutput")!, 8, "Valid native media never produced output.");
                if (mode == "post-output")
                {
                    Call(service, "EnterPlaybackTerminalState", new InvalidOperationException("Fixture interrupted output"), 1d, "fixture-late", false);
                    await Task.Delay(150);
                    Check(Field(service, "_playbackTerminalState") != null, "Post-output failure lost its Retry/Exit state.");
                }
                var native = (MpvVideoWindow)Field(service, "_videoWindow")!;
                Check(service.State == PlayerState.Expanded && IsWindowVisible(native.Hwnd) && ((FrameworkElement)root.FindName("ErrorState")).Visibility != Visibility.Visible && wire.Stops == 0,
                    "Startup failure cleanup hid or retired usable/newer native playback.");
                Program.Log($"PASS: actual native first output retained for {mode}; no stale XAML error or session retirement.");
                return;
            }
            await Until(() => media.Requests > 0 &&
                (service.ErrorMessage != null || ((FrameworkElement)root.FindName("ErrorState")).Visibility == Visibility.Visible),
                12, "Real HTTP200 range rejection did not reach the playback failure path.");
            await Task.Delay(500);
            var video = (MpvVideoWindow)Field(service, "_videoWindow")!;
            var error = (FrameworkElement)root.FindName("ErrorState");
            Program.Log($"TRACE: HTTP200 ignored ranges={media.Requests}; firstOutput={Field(service, "_transportHasFirstOutput")}; state={service.State}; nativeVisible={IsWindowVisible(video.Hwnd)}; errorVisible={error.Visibility == Visibility.Visible}; stops={wire.Stops}; reopens={wire.Starts}.");
            Check(service.State == PlayerState.Idle && !IsWindowVisible(video.Hwnd) && error.Visibility == Visibility.Visible,
                "Startup failure stranded the shell behind a visible native video window without a usable XAML error.");
            await Until(() => wire.Stops > 0, 4, "Failed startup did not release its playback session.");
            Check(wire.Progress.Count > 0 && wire.Progress.All(p => p >= 647), "Failed resumed startup overwrote watch progress with zero.");
            var dismiss = FindButton(error);
            ((IInvokeProvider)new ButtonAutomationPeer(dismiss).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => ((FrameworkElement)root.FindName("LoadingOverlay")).Visibility == Visibility.Collapsed, 2, "Go Back failed to dismiss the startup error.");
            var frame = (Frame)root.FindName("ContentFrame");
            Check(frame.Navigate(typeof(SiloPlayer.Views.SettingsPage)), "Shell navigation rejected Settings after failed startup."); await Task.Delay(150);
            Check(frame.Content is SiloPlayer.Views.SettingsPage, "Shell navigation remained unusable after failed startup.");
            Program.Log("PASS: real native initial range failure exits the video popup, presents/dismisses a usable error, releases the session, preserves resume progress and leaves shell navigation working.");
        }
        finally
        {
            if (service.State != PlayerState.Idle) await service.CloseAsync();
            Call(service, "ResetFailedMpvInitialization");
            window?.Close(); await Task.Delay(100); main.SetValue(null, originalMain); slot.SetValue(null, original);
        }
    }
    private static Button FindButton(DependencyObject node)
    {
        if (node is Button b) return b;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            try { return FindButton(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i)); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("No dismiss button.");
    }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate, int seconds, string message) { var watch = Stopwatch.StartNew(); while (!predicate() && watch.Elapsed.TotalSeconds < seconds) await Task.Delay(50); Check(predicate(), message); }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    private sealed class IgnoredRangeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        internal string Origin { get; }
        internal int Requests;
        internal bool ValidMedia;
        private readonly byte[] _wave = Wave();
        internal IgnoredRangeServer()
        {
            var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
            Origin = "http://127.0.0.1:" + number; _listener.Prefixes.Add(Origin + "/"); _listener.Start(); _ = Serve();
        }
        private async Task Serve()
        {
            try
            {
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync(); Interlocked.Increment(ref Requests);
                    if (ValidMedia)
                    {
                        var start = long.Parse((context.Request.Headers["Range"] ?? "bytes=0-").Split('=')[1].Split('-')[0]);
                        if (start >= _wave.Length) { context.Response.StatusCode = 416; context.Response.Close(); continue; }
                        context.Response.StatusCode = 206; context.Response.ContentLength64 = _wave.Length - start;
                        context.Response.Headers["Content-Range"] = $"bytes {start}-{_wave.Length - 1}/{_wave.Length}";
                        await context.Response.OutputStream.WriteAsync(_wave.AsMemory((int)start)); context.Response.Close(); continue;
                    }
                    context.Response.StatusCode = 200; context.Response.ContentLength64 = 1267398497;
                    // The reader must reject these headers, never consume the full body.
                    await context.Response.OutputStream.WriteAsync(new byte[1]); context.Response.Abort();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException) { }
        }
        private static byte[] Wave()
        {
            const int rate = 48000, size = rate * 4 * 20;
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size); writer.Write(new byte[size]); return stream.ToArray();
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal int Stops, Starts;
        internal List<double> Progress = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "startup-failure.fixture.invalid", "Unexpected fixture origin.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/capabilities") && path.Contains("/playback/")) return Json(new { allowed = true, state = "available", installation_id = "fixture-install", protocol_versions = new[] { 3 }, features = new[] { "fixed_media_file_v1" } });
            if (path.EndsWith("/progress"))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Progress.Add(json.RootElement.GetProperty("position").GetDouble()); return Json(new { outcome = "applied" });
            }
            if (request.Method == HttpMethod.Delete) { Interlocked.Increment(ref Stops); return Json(new { outcome = "stopped" }); }
            if (path.EndsWith("/start"))
            {
                ++Starts;
                return Json(new { protocol_version = 3, outcome = "playable", session_id = "fixture-session", playback_plan = new { plan_id = "fixture-plan", plan_attempt_key = "fixture-key", session_id = "fixture-session", effective_media_file_id = 42, delivery = "original_http", active_quality = "original", stream = new { url = origin + "/stream/direct/fixture", protocol = "http_progressive" }, timeline = new { can_seek_anywhere = true }, source = new { bitrate_kbps = 1536, duration_seconds = 1200 }, selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() } } });
            }
            return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
