using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Official silo-server #5180 and #5225 on the real shell, PlayerService and libmpv: a video left paused answers its
/// periodic progress report with 410 playback_session_paused_timeout, and the player leaves at once from the full player
/// and from the mini player, with the official notice, no fresh attempt, no stop sent to the ended session and no further
/// reports. Synthetic server only. Dispatch SILO_NATIVE_TEST_ONLY=paused-playback-timeout.
/// </summary>
internal static class PausedPlaybackTimeoutNativeFixture
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Notice = "Playback stopped because it was paused for too long.";

    internal static async Task RunAsync()
    {
        using var media = new MediaServer();
        using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://paused-timeout.fixture.invalid");
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
        MainWindow? window = null;
        try
        {
            window = new MainWindow(); main.SetValue(null, window);
            var root = (FrameworkElement)window.Content;
            var nav = (NavigationView)root.FindName("NavView");
            nav.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("NavView_Loaded", Flags)!.CreateDelegate(typeof(RoutedEventHandler), window);
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1280, 720)); window.AppWindow.Show(false);
            await Task.Delay(120);

            await LeaveAsync(service, provider, auth, api, wire, media, root, minimized: false, "paused-timeout-full");
            await LeaveAsync(service, provider, auth, api, wire, media, root, minimized: true, "paused-timeout-minimized");
            Program.Log("PASS: a paused video's 410 playback_session_paused_timeout leaves the full player and the mini player at once, with the official notice, no fresh attempt, no stop and no further reports.");
        }
        finally
        {
            if (service.State != PlayerState.Idle) await service.CloseAsync();
            Call(service, "ResetFailedMpvInitialization");
            window?.Close(); await Task.Delay(100); main.SetValue(null, originalMain); slot.SetValue(null, original);
        }
    }

    private static async Task LeaveAsync(PlayerService service, IServiceProvider provider, AuthService auth, SiloApiClient api, Wire wire, MediaServer media,
        FrameworkElement root, bool minimized, string name)
    {
        wire.PausedTimeout = false;
        var manager = new PlaybackManager(provider.GetRequiredService<PlaybackApi>(), provider.GetRequiredService<CatalogApi>(), auth, api);
        manager.UseWatchDetail(new() { ContentId = "fixture-episode", Type = "episode", Versions = [new() { FileId = 42, Duration = 1200, Bitrate = 1536, Container = "mkv", CodecVideo = "h264", CodecAudio = "aac", AudioTracks = [new() { Language = "eng", Default = true }] }] });
        manager.LeavesOnPausedTimeout = true;
        await manager.StartSessionAsync(42, 0, forceStartPosition: true);
        var sessionId = manager.SessionId!;
        Check(wire.LastStartBody?.Contains("\"paused_timeout_v1\"", StringComparison.Ordinal) == true, name + ": the video start did not advertise paused_timeout_v1.");
        // PlayCoreAsync subscribes the same handler; this fixture installs the manager directly, as the startup-failure fixture does.
        manager.SessionEnded += (Action<string, PlaybackSessionEndReason>)Delegate.CreateDelegate(typeof(Action<string, PlaybackSessionEndReason>), service,
            typeof(PlayerService).GetMethod("OnPlaybackSessionEnded", Flags)!);
        Call(service, "EnsureMpvInitialized");
        var mpv = (MpvPlayer)Field(service, "_mpv")!;
        mpv.SetProperty("ao", "null");
        Set(service, "_playbackManager", manager);
        Property(service, "ContentId", "fixture-episode"); Property(service, "Versions", manager.WatchDetail!.Versions);
        service.SetState(PlayerState.Expanded);
        var preparedType = typeof(PlayerService).GetNestedType("PreparedPlaybackTransport", BindingFlags.NonPublic)!;
        var url = media.Origin + "/stream/direct/fixture";
        var prepared = Activator.CreateInstance(preparedType, Flags | BindingFlags.Public, null,
            [PlaybackTransportPlanner.Plan("direct", url), url, 0d, 0d, 0d, (double?)1200d, true], null)!;
        Call(service, "ApplyPreparedTransport", prepared); Call(service, "BeginMpvLoad", prepared, false);
        await Until(() => (bool)Field(service, "_transportHasFirstOutput")!, 10, name + ": the native media never produced output.");

        var mini = (FrameworkElement)root.FindName("MiniPlayerBarControl");
        if (minimized)
        {
            service.Minimize();
            await Until(() => service.State == PlayerState.Minimized && mini.Visibility == Visibility.Visible, 3, name + ": the player did not minimize to the mini player.");
        }
        mpv.Pause();
        await Until(() => service.IsPaused && mpv.IsPaused, 3, name + ": the player did not pause.");
        await Task.Delay(400);
        if (minimized) await Capture(root, name + "-before-1280.png");

        // The server's sweep ends the paused session: its next periodic report answers 410 with the paused timeout's type.
        var starts = wire.Starts; var stops = wire.Stops;
        wire.PausedTimeout = true;
        await Until(() => wire.TimeoutAnswers > 0, 10, name + ": no progress report reached the ended session.");
        await Until(() => service.State == PlayerState.Idle, 5, name + ": the paused timeout's 410 did not leave the player.");
        var toast = (FrameworkElement)root.FindName("ToastHost");
        await Until(() => Descendants(toast).OfType<TextBlock>().Any(text => text.Text == Notice), 3, name + ": the paused timeout's notice was not shown.");
        await service.CloseAsync();
        var video = (MpvVideoWindow)Field(service, "_videoWindow")!;
        Check(!IsWindowVisible(video.Hwnd), name + ": the native video window stayed visible after the session ended.");
        Check(mini.Visibility == Visibility.Collapsed, name + ": the mini player stayed after the session ended.");
        Check(!Descendants(toast).OfType<TextBlock>().Any(text => text.Text == "An administrator ended this playback session."), name + ": the paused timeout was announced as an administrator's end.");
        Check(wire.Starts == starts, name + ": the ended session started a fresh attempt.");
        Check(wire.Stops == stops, name + ": a stop was sent to the session the server had already ended.");
        Check(manager.SessionId == null && Field(manager, "_progressTimer") == null, name + ": the ended session kept reporting.");
        var answers = wire.TimeoutAnswers;
        await Task.Delay(1500);
        Check(wire.TimeoutAnswers == answers && wire.Starts == starts, name + ": requests continued after the player left.");
        Program.Log($"PASS: {name}: session {sessionId} left from {(minimized ? "the mini player" : "the full player")} after {answers} paused-timeout answer(s); notice shown.");
        await Capture(root, name + "-1280.png");
        // Let the notice leave before the next case so each capture shows one.
        await Until(() => !Descendants(toast).OfType<TextBlock>().Any(text => text.Text == Notice), 8, name + ": the notice did not time out.");
    }

    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); await Task.Delay(150);
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root); Check(bitmap.PixelWidth > 0, name + " did not render.");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate, int seconds, string message) { var watch = Stopwatch.StartNew(); while (!predicate() && watch.Elapsed.TotalSeconds < seconds) await Task.Delay(50); Check(predicate(), message); }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);

    /// <summary>Twenty seconds of silent PCM over byte ranges: enough for first output and a pause.</summary>
    private sealed class MediaServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _wave = Wave();
        internal string Origin { get; }
        internal MediaServer()
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
                    var context = await _listener.GetContextAsync();
                    var start = long.Parse((context.Request.Headers["Range"] ?? "bytes=0-").Split('=')[1].Split('-')[0]);
                    if (start >= _wave.Length) { context.Response.StatusCode = 416; context.Response.Close(); continue; }
                    context.Response.StatusCode = 206; context.Response.ContentLength64 = _wave.Length - start;
                    context.Response.Headers["Content-Range"] = $"bytes {start}-{_wave.Length - 1}/{_wave.Length}";
                    await context.Response.OutputStream.WriteAsync(_wave.AsMemory((int)start)); context.Response.Close();
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

    /// <summary>The synthetic API: protocol v3 start, sequenced progress, and the paused timeout's 410 once it applies.</summary>
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal volatile bool PausedTimeout;
        internal int Starts, Stops, TimeoutAnswers;
        internal string? LastStartBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "paused-timeout.fixture.invalid", "Unexpected fixture origin.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/capabilities") && path.Contains("/playback/"))
                return Json(new { allowed = true, state = "available", installation_id = "fixture-install", protocol_versions = new[] { 3 }, features = new[] { "fixed_media_file_v1", "paused_timeout_v1" } });
            if (path.EndsWith("/progress"))
            {
                if (PausedTimeout)
                {
                    Interlocked.Increment(ref TimeoutAnswers);
                    return new HttpResponseMessage(HttpStatusCode.Gone)
                    {
                        Content = new StringContent("{\"type\":\"https://siloserver.org/docs/api/v2/problems/playback_session_paused_timeout\",\"title\":\"Playback session paused too long\",\"status\":410,\"detail\":\"Playback stopped because it was paused for too long.\",\"instance\":\"urn:silo:request:1\"}",
                            Encoding.UTF8, "application/problem+json"),
                    };
                }
                return Json(new { outcome = "applied" });
            }
            if (request.Method == HttpMethod.Delete) { Interlocked.Increment(ref Stops); return Json(new { outcome = "stopped" }); }
            if (path.EndsWith("/start"))
            {
                LastStartBody = await request.Content!.ReadAsStringAsync(ct);
                var session = "fixture-session-" + Interlocked.Increment(ref Starts);
                return Json(new { protocol_version = 3, outcome = "playable", session_id = session, playback_plan = new { plan_id = "fixture-plan", plan_attempt_key = "fixture-key", session_id = session, effective_media_file_id = 42, delivery = "original_http", active_quality = "original", stream = new { url = origin + "/stream/direct/fixture", protocol = "http_progressive" }, timeline = new { can_seek_anywhere = true }, source = new { bitrate_kbps = 1536, duration_seconds = 1200 }, selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() } } });
            }
            return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>() });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
