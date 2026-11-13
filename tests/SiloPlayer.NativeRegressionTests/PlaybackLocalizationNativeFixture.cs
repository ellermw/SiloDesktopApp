using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

/// <summary>
/// Task 5 playback localization on actual controls: an open player switches
/// German → Japanese → Arabic → English while its production OSC, an open
/// native settings menu, the native Subtitles menu and the audiobook settings
/// panel re-render in place. Raw titles, menu ownership/leases, selection and
/// the left-to-right video/HUD geometry must survive every switch.
/// </summary>
internal static class PlaybackLocalizationNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    // Mixed-direction raw data: Arabic + Latin + ASS-significant braces.
    private const string RawTitle = "Shōgun – مرحبا بالعالم {Episode}";

    internal static async Task RunAsync(Window window)
    {
        var locales = LocaleService.Shared;
        var original = locales.Current;
        AppLocale Locale(string tag) => locales.Create(AppLocaleResolution.Resolve(tag, ["en-US"], "en-US"));
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(window));
        window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer(); using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://native-locale.fixture.invalid");
        client.SetProfile("synthetic"); client.SetDeviceMetadata("locale-desktop", "Fixture", "Windows");
        using var auth = new AuthService(client, new AuthApi(client));
        var playback = new PlaybackApi(client); var catalog = new CatalogApi(client);
        var settings = new SettingsService(Path.Combine(Program.ResultDirectory, "locale-settings"));
        using var player = new PlayerService(playback, catalog, auth, client, settings, new SettingsApi(client));
        using var manager = new PlaybackManager(playback, catalog, auth, client);
        var versions = new List<FileVersion> { new() { FileId = 52, Resolution = "1080p", CodecVideo = "hevc", Duration = 60 } };
        var detail = new WatchDetailResponse { ContentId = "synthetic-locale", Type = "episode", Title = RawTitle,
            SeriesTitle = "Series ✦ مسلسل", SeasonNumber = 2, EpisodeNumber = 5, Versions = versions };
        manager.UseWatchDetail(detail);
        await manager.StartSessionAsync(52);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
        foreground.Observe("locale-video", video.Hwnd, "video");
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv); mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(player, "_mpv", mpv); Set(player, "_videoWindow", video); Set(player, "_playbackManager", manager);
        Property(player, "State", PlayerState.Expanded); Property(player, "ContentId", "synthetic-locale"); Property(player, "Versions", versions);
        Call(player, "WireMpvEvents");
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acks = new ConcurrentQueue<string>();
        void Message(string[] args)
        {
            if (args.Length > 0 && args[0] == "silo-locale-fixture-ready") ready.TrySetResult();
            if (args.Length == 5 && args[0] == "silo-locale-applied") acks.Enqueue(string.Join("|", args.Skip(1)));
            if (args.Length == 3 && args[0] == "silo-locale-fixture-state" && queries.TryRemove(args[1], out var reply))
            { using var json = JsonDocument.Parse(args[2]); reply.TrySetResult(json.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        var host = new Grid();
        try
        {
            locales.Apply(Locale("en"));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(player, "StopPlaybackStallWatchdog"); player.SetPaused(true);
            await Until(() => mpv.IsPaused && !player.IsLoading, "Video did not settle for localization.");
            var script = Path.Combine(Program.ResultDirectory, "locale-observer.lua");
            await File.WriteAllTextAsync(script, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs/mpv/scripts/silo-osc.lua")) + """

                mp.register_script_message('locale-fixture-query', function(id)
                    show_osc(); compute_layout(); render_osc()
                    local L = state.layout
                    local function rect(r) return r and {x=r.x or 0,y=r.y or 0,w=r.w or 0,h=r.h or 0,cx=r.cx or 0,cy=r.cy or 0} or {} end
                    mp.commandv('script-message','silo-locale-fixture-state',id,require('mp.utils').format_json({
                        language=osc_locale.language, rtl=osc_locale.rtl, generation=osc_locale.generation,
                        exit=rect(L.btn_exit), play=rect(L.btn_play), seek=rect(L.seek_bar), quality=rect(L.btn_quality), cc=rect(L.btn_cc),
                        title=state.content_title, subtitle=state.content_subtitle,
                        hud=state.osc_overlay and state.osc_overlay.data or '', alpha=state.current_alpha}))
                end)
                mp.commandv('script-message','silo-locale-fixture-ready')
                """);
            Require(Call(mpv, "Command", (object)new[] { "load-script", script }) is true, "Localization observer did not load.");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Call(player, "SetTitleFromWatchDetail", detail);
            // The production load seam publishes HUD preferences and the locale payload.
            Call(player, "PublishPlaybackHudPreferences", mpv);
            Call(player, "SendTitleToOsc"); Call(player, "SendQualityInfoToOsc");
            var english = await QueryUntil(state => state.GetProperty("language").GetString() == "en", "English payload never reached the OSC.");
            var englishGeometry = Geometry(english);
            Require(english.GetProperty("title").GetString() == detail.SeriesTitle && english.GetProperty("subtitle").GetString()!.Contains(RawTitle, StringComparison.Ordinal),
                "Raw series/episode titles changed before localization.");
            Require(acks.LastOrDefault() is { } firstAck && firstAck.EndsWith("|0|0"), $"English payload was not accepted whole: {acks.LastOrDefault()}.");

            var menu = await OpenAt("quality", "Actual settings gear did not open native controls.");
            var list = (ListView)Get(menu, "_list")!;
            var selected = list.SelectedIndex; var ids = list.Items.Cast<ListViewItem>().Select(row => (string)row.Tag).ToArray();
            Require(Gate(player).CurrentCount == 0, "Open settings menu did not hold the shared native-menu lease.");

            foreach (var tag in new[] { "de", "ja", "ar" })
            {
                var locale = Locale(tag);
                locales.Apply(locale);
                await Until(() => ((TextBlock)Get(menu, "_heading")!).Text == locale.Get("web.player.quality.playbackSettings"),
                    $"Open native settings menu heading did not switch to {tag}.");
                Require(ReferenceEquals(Menu(player), menu) && Gate(player).CurrentCount == 0,
                    $"Switching to {tag} closed the open menu or released its lease.");
                Require(list.SelectedIndex == selected && list.Items.Cast<ListViewItem>().Select(row => (string)row.Tag).SequenceEqual(ids),
                    $"Switching to {tag} changed menu ids or selection.");
                var speedRow = list.Items.Cast<ListViewItem>().Single(row => (string)row.Tag == "speed");
                Require(AutomationProperties.GetName(speedRow).Contains(locale.Get("web.player.speed.heading"), StringComparison.Ordinal),
                    $"Speed row was not re-rendered in {tag}.");
                var root = (Grid)Get(menu, "_root")!;
                Require(root.Language == locale.Language && root.FlowDirection == (locale.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight),
                    $"Menu language/direction did not follow {tag}.");
                var state = await QueryUntil(value => value.GetProperty("language").GetString() == locale.Language,
                    $"Production OSC never received the {tag} payload.");
                Require(state.GetProperty("rtl").GetBoolean() == locale.IsRightToLeft, $"OSC direction flag differs for {tag}.");
                Require(state.GetProperty("hud").GetString()!.Contains(locale.Get("web.player.exit"), StringComparison.Ordinal),
                    $"Rendered HUD lacks the {tag} Exit label.");
                Require(state.GetProperty("title").GetString() == detail.SeriesTitle, $"Raw series title changed under {tag}.");
                var episodeLine = locale.Get("web.player.hud.episodeWithTitle", new Dictionary<string, object?>
                    { ["season"] = 2, ["episode"] = 5, ["title"] = RawTitle });
                Require(player.Subtitle == episodeLine && state.GetProperty("subtitle").GetString() == episodeLine,
                    $"HUD episode line was not re-rendered in {tag}: {player.Subtitle}");
                Require(Geometry(state) == englishGeometry, $"{tag} changed the left-to-right player geometry: {Geometry(state)} vs {englishGeometry}.");
                Require(acks.LastOrDefault()?.EndsWith("|0|0") == true, $"{tag} payload was not accepted whole: {acks.LastOrDefault()}.");
                await PlaybackAudioMenuNativeFixture.Capture(menu, "locale-settings-" + tag);
            }
            Call(menu, "Input", "cancel");
            await Until(() => Menu(player) == null && Gate(player).CurrentCount == 1, "Localized settings menu leaked its lease on close.");
            Program.Log("PASS: open native settings menu + production OSC switched de/ja/ar in place; raw mixed-direction title, ids, selection, lease and LTR geometry preserved.");

            // Subtitle route: the native Subtitles menu footer re-renders in place.
            var subtitles = await OpenAt("cc", "Actual captions button did not open the native Subtitles menu.");
            var german = Locale("de"); locales.Apply(german);
            await Until(() => ((TextBlock)Get(subtitles, "_heading")!).Text == german.Get("web.subtitles.menu.heading"),
                "Open Subtitles menu heading did not switch to German.");
            var footer = Descendants<Button>((Grid)Get(subtitles, "_root")!).Select(button => button.Content as string).ToArray();
            Require(footer.Contains(german.Get("web.subtitles.menu.addSubtitles")) && footer.Contains(german.Get("web.subtitles.menu.appearance")),
                "Subtitles footer actions were not re-rendered in German: " + string.Join(", ", footer));
            Require(ReferenceEquals(Menu(player), subtitles), "Language switch closed the Subtitles menu.");
            Call(subtitles, "Input", "cancel");
            await Until(() => Menu(player) == null && Gate(player).CurrentCount == 1, "Subtitles menu leaked its lease.");
            Program.Log("PASS: open native Subtitles menu heading/footer switched to German without closing.");

            // Audiobook route: the real settings panel hosted live switches language.
            // The production builder is internal to the app; invoke it unchanged.
            var flyout = (Flyout)typeof(AudiobookNowListening).Assembly.GetType("SiloPlayer.Controls.AudiobookSettingsFlyout")!
                .GetMethod("Create")!.Invoke(null, [player, settings])!;
            var panel = (StackPanel)flyout.Content; flyout.Content = null;
            host.Children.Add(panel); ((Panel)window.Content).Children.Add(host);
            await Until(() => panel.IsLoaded, "Audiobook settings panel did not load.");
            string Heading() => ((TextBlock)((StackPanel)panel.Children[0]).Children[0]).Text;
            Require(Heading() == german.Get("web.audiobooks.player.settingsSkipBack").ToUpper(german.FormatCulture),
                $"Audiobook settings opened with stale text: {Heading()}.");
            var japanese = Locale("ja"); locales.Apply(japanese);
            await Until(() => Heading() == japanese.Get("web.audiobooks.player.settingsSkipBack").ToUpper(japanese.FormatCulture),
                "Hosted audiobook settings did not switch to Japanese.");
            Require(Descendants<TextBlock>(panel).Any(text => text.Text == japanese.Get("web.audiobooks.player.smartRewind")),
                "Smart rewind label did not switch to Japanese.");
            Program.Log("PASS: hosted audiobook settings panel switched German → Japanese in place.");

            locales.Apply(Locale("en"));
            var back = await QueryUntil(state => state.GetProperty("language").GetString() == "en", "OSC did not return to English.");
            Require(Geometry(back) == englishGeometry && back.GetProperty("title").GetString() == detail.SeriesTitle && player.Subtitle!.Contains(RawTitle, StringComparison.Ordinal), "Returning to English changed geometry or the raw title.");
            Program.Log("PASS: playback-localization native group.");
        }
        finally
        {
            ((Panel)window.Content).Children.Remove(host);
            if (Menu(player) is { } current) Call(current, "Input", "cancel");
            locales.Apply(locales.Create(original.Resolution));
            Call(player, "StopPlaybackStallWatchdog"); Call(player, "DisconnectWebSocket"); mpv.ScriptMessageReceived -= Message;
            Set(player, "_videoWindow", null); Set(player, "_mpv", null); Set(player, "_playbackManager", null);
            video.Hide(); await Task.Delay(40); foreground.Complete();
        }

        string Geometry(JsonElement state) => string.Join(";", new[] { "exit", "play", "seek" }.Select(name =>
        {
            // The Exit button may widen to fit its label; its anchor and every
            // transport/seek position stay in the same left-to-right place.
            var r = state.GetProperty(name);
            return r.TryGetProperty("x", out var x) ? $"{name}={x}:{r.GetProperty("y")}" + (name == "exit" ? "" : $":{r.GetProperty("w")}") : name + "=none";
        }));
        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = reply; mpv.SendScriptMessage("locale-fixture-query", id);
            try { return await reply.Task.WaitAsync(TimeSpan.FromSeconds(3)); } finally { queries.TryRemove(id, out _); }
        }
        async Task<JsonElement> QueryUntil(Func<JsonElement, bool> ready, string message)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (true) { var state = await Query(); if (ready(state)) return state; if (DateTime.UtcNow > deadline) throw new InvalidOperationException(message); await Task.Delay(40); }
        }
        async Task<object> OpenAt(string control, string message)
        {
            mpv.SendScriptMessage("osc-show");
            var state = await QueryUntil(value => value.GetProperty("alpha").GetDouble() >= 0.1, "OSC never became visible.");
            var rect = state.GetProperty(control);
            Require(rect.TryGetProperty("w", out var w) && w.GetDouble() > 0, $"OSC {control} control is absent.");
            var x = rect.GetProperty("cx").ToString(); var y = rect.GetProperty("cy").ToString();
            mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            await Until(() => Menu(player) is { } current && Get(current, "_list") is ListView { XamlRoot: not null }, message);
            var result = Menu(player)!;
            var native = (Window)Get(result, "_window")!; foreground.WatchMenu(native, WinRT.Interop.WindowNative.GetWindowHandle(native));
            return result;
        }
    }

    private sealed class Wire(string origin) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "native-locale.fixture.invalid", "Localization fixture attempted external API networking.");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/settings/")) return Json(request.Method == HttpMethod.Get ? "{\"items\":[]}" : "{}");
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1","plan_active_quality_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start")) return Json("{\"outcome\":\"applied\"}");
            using var start = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var fileId = int.Parse(start.RootElement.GetProperty("file_id").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var sessionId = "locale-session-" + fileId;
            return Json(JsonSerializer.Serialize(new { protocol_version = 3, outcome = "playable", session_id = sessionId,
                server_features = new[] { "plan_active_quality_v1" }, playback_plan = new {
                    protocol_version = 3, plan_id = "locale-plan-" + fileId, plan_attempt_key = "synthetic", session_id = sessionId, effective_media_file_id = fileId,
                    delivery = "original_http", stream = new { url = origin + "/video.y4m" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { }, subtitle = new { mode = "off", inventory = Array.Empty<object>() }, active_quality = "original", available_qualities = Array.Empty<object>() } }));
        }
        private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };
    }

    private static object? Menu(PlayerService player) => Get(player, "_nativeMenu");
    private static SemaphoreSlim Gate(PlayerService player) => (SemaphoreSlim)Get(player, "_playbackDialogGate")!;
    private static object? Get(object target, string name) { for (var type = target.GetType(); type != null; type = type.BaseType) if (type.GetField(name, Private) is { } field) return field.GetValue(target); return null; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message) { var deadline = DateTime.UtcNow.AddSeconds(8); while (!ready()) { if (DateTime.UtcNow > deadline) throw new InvalidOperationException(message); await Task.Delay(20); } }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
