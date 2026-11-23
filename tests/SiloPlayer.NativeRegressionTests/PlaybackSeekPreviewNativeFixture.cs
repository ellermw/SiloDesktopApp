using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

/// <summary>
/// Seek-bar previews (trickplay) through the actual PlayerService, PlaybackManager, libmpv and
/// production OSC: a fake server publishes a synthetic manifest and generated JPEG sprite
/// sheets (every tile a distinct colour), the pointer hovers the real seek bar, and the mpv
/// window is captured. Official silo-server 3b2b32168 web/src/player/trickplay.ts.
/// </summary>
internal static class PlaybackSeekPreviewNativeFixture
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    // 60 thumbnails of 0.5 s for the 30 s clip, 3 x 3 per sheet: 7 sheets.
    private const int IntervalMs = 500, TileWidth = 160, TileHeight = 90, Columns = 3, Rows = 3, Count = 60;
    private static int SheetCount => (Count + Columns * Rows - 1) / (Columns * Rows);

    internal static async Task RunAsync()
    {
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer();
        using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://seek-preview.fixture.invalid");
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
            .AddSingleton<HomeApi>().AddSingleton<RequestsApi>().AddSingleton<NotificationsApi>().AddSingleton<PlaybackApi>().AddSingleton<HistoryImportApi>()
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
            window.AppWindow.Move(new(-20000, -20000)); window.AppWindow.ResizeClient(new(1440, 900)); window.AppWindow.Show(false);
            await Task.Delay(150);
            for (var sheet = 0; sheet < SheetCount; sheet++) wire.Sheets[$"/trickplay/sheet-{sheet}.jpg"] = await SheetJpeg(sheet);

            // Previews published: the manifest is read once, sheets only on hover.
            wire.HoldSheet0 = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await service.PlayAsync("movie-1", fromStart: true);
            await Loaded(service, "movie-1");
            await Until(() => wire.ManifestReads.Contains("movie-1|?file_id=42"), "The manifest was not read for the played file.");
            var mpv = service.Mpv!;
            await LoadObservedOsc(mpv);
            await Until(async () => { Call(service, "SyncSeekPreviews"); await Task.Delay(60); return (await Query(mpv)).GetProperty("key").GetString() is { Length: > 0 }; },
                "The OSC never received the manifest.");
            await Task.Delay(400);
            Check(wire.SheetReads.IsEmpty, "Sheets were downloaded before the seek bar was hovered.");
            Check(wire.ManifestReads.Count == 1, "The manifest was read more than once for one file.");

            // While sheet 0 is still downloading the tooltip is today's.
            await Hover(mpv, 1.25);
            await Until(() => wire.SheetReads.ContainsKey("/trickplay/sheet-0.jpg"), "Hovering did not request the sheet under the pointer.");
            await Task.Delay(250);
            Check((await Query(mpv)).GetProperty("signature").GetString() == "", "A preview was drawn before its sheet loaded.");
            await Shot(mpv, "seek-preview-loading.png");
            wire.HoldSheet0.TrySetResult();

            // Two times on sheet 0 (tiles 2 and 8), then one on sheet 1 (tile 10).
            var first = await ShowTile(mpv, 1.25, 2, "seek-preview-sheet0-tile2.png");
            var second = await ShowTile(mpv, 4.25, 8, "seek-preview-sheet0-tile8.png");
            Check(first.File == second.File, "Two tiles of one sheet used different decoded files.");
            var third = await ShowTile(mpv, 5.25, 10, "seek-preview-sheet1-tile10.png");
            Check(third.File != first.File, "The next sheet reused the first sheet's file.");
            // Pointer on sheet 1 loads sheet 2 (the next) and nothing further.
            await Until(() => wire.SheetReads.ContainsKey("/trickplay/sheet-2.jpg"), "The sheet after the pointer's sheet was not preloaded.");
            await Task.Delay(300);
            Check(wire.SheetReads.Keys.All(path => path is "/trickplay/sheet-0.jpg" or "/trickplay/sheet-1.jpg" or "/trickplay/sheet-2.jpg"),
                "Sheets beyond the pointer's and the next were downloaded: " + string.Join(",", wire.SheetReads.Keys));
            // The last thumbnail, clamped to the bar's end.
            var last = await ShowTile(mpv, 29.9, Count - 1, "seek-preview-bar-end.png");
            var layout = await Query(mpv);
            Check(last.X + last.Width <= (int)Math.Floor(layout.GetProperty("x2").GetDouble()) + 1, "The preview runs past the seek bar's end.");
            Check(wire.SheetReads.Values.All(count => count == 1), "A decoded sheet was downloaded again.");
            await Leave(mpv);
            await Until(async () => (await Query(mpv)).GetProperty("signature").GetString() == "", "The preview stayed after the pointer left the bar.");

            // Previews unavailable for the next file: no manifest read, today's tooltip.
            var directory = (string)typeof(PlayerService).GetMethod("PlaybackArtworkDirectory", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, ["SeekPreviews"])!;
            wire.TrickplayAvailable = false;
            await service.PlayAsync("movie-2", fromStart: true);
            await Loaded(service, "movie-2");
            await Task.Delay(300);
            Check(!wire.ManifestReads.Any(read => read.StartsWith("movie-2|", StringComparison.Ordinal)), "Previews were read for a version without them.");
            Check((await Query(mpv)).GetProperty("key").GetString() == "", "The previous file's previews stayed in the OSC.");
            await Hover(mpv, 4.25); await Task.Delay(400);
            Check((await Query(mpv)).GetProperty("signature").GetString() == "", "A preview was drawn without previews.");
            await Shot(mpv, "seek-preview-unavailable.png");
            await Leave(mpv);
            // The previous file's decoded sheets are deleted once its playback ended.
            await Until(() => !Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*.bgra", SearchOption.AllDirectories).Any(),
                "Decoded sheets of the previous file were not deleted.", seconds: 15);

            // Closing while a sheet is still downloading reaches Idle.
            wire.TrickplayAvailable = true;
            wire.HoldSheet0 = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.SheetReads.Clear();
            await service.PlayAsync("movie-3", fromStart: true);
            await Loaded(service, "movie-3");
            await Until(async () => (await Query(mpv)).GetProperty("key").GetString() is { Length: > 0 }, "The OSC never received the next file's manifest.");
            await Task.Delay(300);
            await Until(async () => { await Hover(mpv, 1.25); return wire.SheetReads.ContainsKey("/trickplay/sheet-0.jpg"); },
                "Hovering did not request the held sheet.");
            await service.CloseAsync();
            Check(service.State == PlayerState.Idle, "Closing while a sheet was downloading did not reach Idle.");
            Program.Log("PASS: SEEK_PREVIEWS manifest once per file, on-demand sheets (pointer + next), tiles 2/8 on sheet 0 and 10 on sheet 1, bar-end clamp, unavailable tooltip, sheet files deleted, close during a sheet download.");
        }
        finally
        {
            if (service.State != PlayerState.Idle) await service.CloseAsync();
            Call(service, "ResetFailedMpvInitialization");
            window?.Close(); await Task.Delay(100); main.SetValue(null, originalMain); slot.SetValue(null, original);
        }
    }

    private sealed record Shown(string File, int Offset, int X, int Y, int Width, int Height);

    /// <summary>Hovers a time, waits for its tile, captures the window and checks the tile's colour.</summary>
    private static async Task<Shown> ShowTile(MpvPlayer mpv, double seconds, int index, string name)
    {
        var cell = index % (Columns * Rows);
        var offset = cell / Columns * TileHeight * (Columns * TileWidth * 4) + cell % Columns * TileWidth * 4;
        Shown? shown = null;
        await Hover(mpv, seconds);
        await Until(async () =>
        {
            var parts = ((await Query(mpv)).GetProperty("signature").GetString() ?? "").Split('|');
            if (parts.Length != 6 || parts[1] != offset.ToString(System.Globalization.CultureInfo.InvariantCulture)) return false;
            shown = new(parts[0], int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]), int.Parse(parts[4]), int.Parse(parts[5]));
            return true;
        }, $"Tile {index} was not shown for {seconds}s.");
        await Task.Delay(120);
        var path = await Shot(mpv, name);
        var (r, g, b) = TileColor(index);
        var pixel = await ReadPixel(path, shown!.X + shown.Width / 2, shown.Y + shown.Height / 3);
        Check(Math.Abs(pixel.R - r) < 28 && Math.Abs(pixel.G - g) < 28 && Math.Abs(pixel.B - b) < 28,
            $"Capture {name} does not show tile {index}: got {pixel} expected {(r, g, b)}.");
        var data = await Query(mpv);
        Check(shown.X >= (int)Math.Floor(data.GetProperty("x1").GetDouble()) && shown.Y + shown.Height < data.GetProperty("y").GetDouble(),
            $"Preview for tile {index} is clipped at the bar start or reaches the seek bar.");
        return shown;
    }

    private static (int R, int G, int B) TileColor(int index) => (40 + index * 53 % 200, 40 + index * 97 % 200, 40 + index * 151 % 200);

    /// <summary>A full 3 x 3 sheet; each tile a solid colour with a white bar whose length is its cell.</summary>
    private static async Task<byte[]> SheetJpeg(int sheet)
    {
        int width = Columns * TileWidth, height = Rows * TileHeight;
        var pixels = new byte[width * height * 4];
        for (var cell = 0; cell < Columns * Rows; cell++)
        {
            var index = sheet * Columns * Rows + cell;
            // Black after the last thumbnail, as the contract says.
            var (r, g, b) = index < Count ? TileColor(index) : (0, 0, 0);
            var left = cell % Columns * TileWidth; var top = cell / Columns * TileHeight;
            for (var y = 0; y < TileHeight; y++)
            for (var x = 0; x < TileWidth; x++)
            {
                var bar = index < Count && y >= TileHeight - 14 && y < TileHeight - 6 && x >= 8 && x < 8 + (cell + 1) * 16;
                var i = ((top + y) * width + left + x) * 4;
                pixels[i] = (byte)(bar ? 255 : b); pixels[i + 1] = (byte)(bar ? 255 : g); pixels[i + 2] = (byte)(bar ? 255 : r); pixels[i + 3] = 255;
            }
        }
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size); reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task Loaded(PlayerService service, string id)
    {
        await Until(() => service.Mpv != null && !service.IsLoading && service.ContentId == id && (bool)Field(service, "_transportHasFirstOutput")!,
            "Actual PlayAsync did not load " + id + ".", seconds: 20);
        service.Mpv!.SetProperty("ao", "null"); service.Mpv.SetProperty("loop-file", "inf");
        Call(service, "StopPlaybackStallWatchdog");
        service.Mpv.SetProperty("pause", "yes");
    }

    private static async Task Hover(MpvPlayer mpv, double seconds)
    {
        var data = await Query(mpv);
        double x1 = data.GetProperty("x1").GetDouble(), x2 = data.GetProperty("x2").GetDouble(), duration = data.GetProperty("duration").GetDouble();
        Check(duration > 0, "The OSC has no duration to hover.");
        var x = x1 + (x2 - x1) * seconds / duration;
        mpv.SendScriptMessage("osc-set-visibility", "true");
        mpv.SendScriptMessage("osc-mouse-move", ((int)Math.Round(x)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)Math.Round(data.GetProperty("y").GetDouble())).ToString(System.Globalization.CultureInfo.InvariantCulture), "true");
        await Task.Delay(120);
    }

    private static async Task Leave(MpvPlayer mpv) { mpv.SendScriptMessage("osc-mouse-move", "40", "40", "true"); await Task.Delay(150); }

    /// <summary>The production OSC plus a read-only query of its seek bar and preview state.</summary>
    private static async Task LoadObservedOsc(MpvPlayer mpv)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args) { if (args.Length > 0 && args[0] == "silo-seekpreview-ready") ready.TrySetResult(); }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var path = Path.Combine(Program.ResultDirectory, "seek-preview-observer.lua");
            await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs/mpv/scripts/silo-osc.lua")) + """

                mp.register_script_message('seekpreview-query', function(id)
                    compute_layout()
                    local sb = state.layout.seek_bar
                    local signature = state.seek_preview_overlay_signature or ''
                    mp.commandv('script-message', 'silo-seekpreview-query', id, require('mp.utils').format_json({
                        x1 = sb.x1, x2 = sb.x2, y = sb.draw_y, duration = state.duration,
                        key = state.seek_preview and state.seek_preview.key or '', signature = signature }))
                end)
                mp.commandv('script-message', 'silo-seekpreview-ready')
                """);
            Check(typeof(MpvPlayer).GetMethod("Command", Flags)!.Invoke(mpv, [(object)new[] { "load-script", path }]) is true,
                "Published production OSC could not load in the actual service player.");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { mpv.ScriptMessageReceived -= Message; }
    }

    private static async Task<JsonElement> Query(MpvPlayer mpv)
    {
        var id = Guid.NewGuid().ToString("N");
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args)
        {
            if (args.Length == 3 && args[0] == "silo-seekpreview-query" && args[1] == id)
                reply.TrySetResult(JsonDocument.Parse(args[2]).RootElement.Clone());
        }
        mpv.ScriptMessageReceived += Message;
        try { mpv.SendScriptMessage("seekpreview-query", id); return await reply.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
        finally { mpv.ScriptMessageReceived -= Message; }
    }

    private static async Task<string> Shot(MpvPlayer mpv, string name)
    {
        var path = Path.Combine(Program.ResultDirectory, name);
        var command = typeof(MpvPlayer).GetMethod("Command", Flags)!;
        Check(command.Invoke(mpv, [(object)new[] { "screenshot-to-file", path, "window" }]) is true, "Native screenshot failed: " + name);
        await Until(() => File.Exists(path) && new FileInfo(path).Length > 0, "Screenshot was not written: " + name);
        await Task.Delay(50);
        return path;
    }

    private static async Task<(byte R, byte G, byte B)> ReadPixel(string path, int x, int y)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var pixels = (await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, new(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage)).DetachPixelData();
        x = Math.Clamp(x, 0, (int)decoder.PixelWidth - 1); y = Math.Clamp(y, 0, (int)decoder.PixelHeight - 1);
        var i = (y * (int)decoder.PixelWidth + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static async Task Until(Func<bool> predicate, string message, int seconds = 12)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(40);
        Check(predicate(), message);
    }

    private static async Task Until(Func<Task<bool>> predicate, string message, int seconds = 12)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until) { if (await predicate()) return; await Task.Delay(40); }
        Check(await predicate(), message);
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static object? Field(object target, string name) => target.GetType().GetField(name, Flags)!.GetValue(target);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);

    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal bool TrickplayAvailable = true;
        internal TaskCompletionSource? HoldSheet0;
        internal readonly Dictionary<string, byte[]> Sheets = [];
        internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> SheetReads = new();
        internal readonly System.Collections.Concurrent.ConcurrentBag<string> ManifestReads = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Check(request.RequestUri!.Host == "seek-preview.fixture.invalid", "Unexpected fixture origin.");
            var path = request.RequestUri.AbsolutePath;
            if (path.StartsWith("/trickplay/", StringComparison.Ordinal))
            {
                Check(request.Headers.Authorization == null, "A sheet URL was sent the account's credentials.");
                SheetReads.AddOrUpdate(path, 1, (_, count) => count + 1);
                if (path == "/trickplay/sheet-0.jpg" && HoldSheet0 is { } hold) await hold.Task.WaitAsync(ct);
                return Sheets.TryGetValue(path, out var jpeg) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(jpeg) } : new(HttpStatusCode.NotFound);
            }
            if (path.EndsWith("/trickplay", StringComparison.Ordinal) && path.Contains("/watch/"))
            {
                var id = path.Split('/')[^2];
                ManifestReads.Add(id + "|" + request.RequestUri.Query);
                return Json(new
                {
                    file_id = "42", interval_ms = IntervalMs, thumbnail_width = TileWidth, thumbnail_height = TileHeight,
                    tile_columns = Columns, tile_rows = Rows, thumbnail_count = Count,
                    expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("O"),
                    sheets = Enumerable.Range(0, SheetCount).Reverse().Select(i => new { index = i, url = $"/trickplay/sheet-{i}.jpg?sig=fixture" }),
                });
            }
            if (path == "/api/v2/settings/contract/capabilities") return Json(new { api_version = 1, manifest_revision = 17, supports_batched_effective = true });
            if (path == "/api/v2/settings/values/effective") return Json(new { items = Array.Empty<object>() });
            if (path.EndsWith("/capabilities") && path.Contains("/playback/")) return Json(new { allowed = true, state = "available", installation_id = "fixture-install", protocol_versions = new[] { 3 }, features = new[] { "fixed_media_file_v1" } });
            if (path.Contains("/watch/"))
            {
                var id = path.Split('/').Last();
                return Json(new
                {
                    content_id = id, type = "movie", title = "Seek preview movie",
                    versions = new[] { new { file_id = 42, duration = 30, container = "y4m", codec_video = "rawvideo", trickplay_available = TrickplayAvailable } },
                });
            }
            if (path.EndsWith("/progress")) return Json(new { outcome = "applied" });
            if (request.Method == HttpMethod.Delete) return Json(new { outcome = "stopped" });
            if (path.EndsWith("/start"))
                return Json(new { protocol_version = 3, outcome = "playable", session_id = "fixture-session", playback_plan = new { plan_id = "fixture-plan", plan_attempt_key = "fixture-key", session_id = "fixture-session", effective_media_file_id = 42, delivery = "original_http", active_quality = "original", stream = new { url = origin + "/video.y4m", protocol = "http_progressive" }, timeline = new { can_seek_anywhere = true }, source = new { bitrate_kbps = 1536, duration_seconds = 30 }, selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() } } });
            return Json(new { items = Array.Empty<object>(), settings = Array.Empty<object>(), profiles = Array.Empty<object>(), sections = Array.Empty<object>() });
        }

        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
