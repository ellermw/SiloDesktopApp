using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

/// <summary>Actual Lua opener, owned native chapter rows, cache decode, seek and retirement against synthetic media only.</summary>
internal static class PlaybackChapterMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static Task RunAsync(Window owner) => RunAsync(owner, false);
    internal static Task RunMoreAsync(Window owner) => RunAsync(owner, true);
    private static async Task RunAsync(Window owner, bool more)
    {
        var pointerSelection = Environment.GetEnvironmentVariable("SILO_NATIVE_MENU_POINTER_SELECTION") == "1";
        var previousCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE");
        Environment.SetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE", (more ? "more" : "chapters") + (pointerSelection ? "-pointer" : "-keyboard"));
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var appServices = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldServices = appServices.GetValue(null);
        using var wire = new ChapterWire();
        wire.PreviewBytes = await PreviewBytes();
        using var http = new HttpClient(wire);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "chapter-images"));
        using var services = new ServiceCollection().AddSingleton(images).AddSingleton(http).BuildServiceProvider();
        appServices.SetValue(null, services);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-chapters.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api);
        var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "chapter-settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = new FileVersion { FileId = 42, Duration = 30, Chapters = Enumerable.Range(0, 72)
            .Select(i => new VersionChapter { Index = 4 + i * 13, Title = i == 71 ? "A long chapter 日本語 " + new string('x', 160) : $"Chapter source {i}",
                StartSeconds = i / 3d, EndSeconds = (i + 1) / 3d }).ToList() };
        manager.UseWatchDetail(new() { ContentId = "synthetic-chapters", Type = "movie", Versions = [version] });
        await manager.StartSessionAsync(42);
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var video = new MpvVideoWindow();
        using var mpv = new MpvPlayer();
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var scriptReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args)
        {
            if (args.Length == 1 && args[0] == "silo-chapters-fixture-ready") scriptReady.TrySetResult();
            if (args.Length > 2 && args[0] == "silo-chapters-fixture-state" && queries.TryRemove(args[1], out var completion))
            {
                using var json = JsonDocument.Parse(args[2]); completion.TrySetResult(json.RootElement.Clone());
            }
        }
        try
        {
            video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
            mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
            mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
            Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager); Set(service, "_videoWindow", video);
            Set(service, "_activeTransportPlan", new PlaybackTransportPlan(PlaybackSemanticMethod.Direct, PlaybackTransportKind.DirectProgressive, false));
            Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-chapters");
            Property(service, "Versions", manager.WatchDetail!.Versions);
            Call(service, "WireMpvEvents");
            mpv.ScriptMessageReceived += Message;
            var script = Path.Combine(Program.ResultDirectory, "chapter-observer.lua");
            await File.WriteAllTextAsync(script, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua")) + """

                mp.register_script_message('osc-chapters-fixture-query', function(id, kind)
                    compute_layout()
                    local r = kind == 'more' and state.layout.btn_more or state.layout.btn_chapters or state.layout.btn_more or {}
                    mp.commandv('script-message','silo-chapters-fixture-state',id,require('mp.utils').format_json({
                        x=r.cx or 0,y=r.cy or 0,width=state.osd_width,height=state.osd_height,
                        more_available=state.layout.btn_more ~= nil,
                        scope_present=state['native_' .. kind .. '_scope'] ~= nil,
                        scope=state.native_chapters_scope or '',
                        sequence=state.native_menu_request and state.native_menu_request.sequence or 0,
                        legacy=state.chapter_menu_visible or false}))
                end)
                mp.commandv('script-message','silo-chapters-fixture-ready')
                """);
            Command(mpv, "load-script", script);
            await scriptReady.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var path = Path.Combine(Program.ResultDirectory, "chapter-video.y4m");
            await File.WriteAllBytesAsync(path, Clip());
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult();
            mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(path); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog");
            service.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Synthetic video did not settle.");
            Call(service, "SendChapterListToOsc");
            CallIfPresent(service, "PublishNativeMenuScopes");
            mpv.SendScriptMessage("osc-show");
            await Task.Delay(150);

            if (more) { owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 460, 700)); video.SynchronizePosition(); }
            var menu = await Open();
            var list = (ListView)Get(menu, "_list")!;
            if (more)
            {
                Require(string.Join(",", list.Items.OfType<ListViewItem>().Select(row => (string)row.Tag)) ==
                    "audio,chapters,stats,pip,fill,volume-down,volume-up,mute", "Native More changed existing action order or omitted controls.");
                Require(!((ListViewItem)list.Items[0]).IsEnabled && ((ListViewItem)list.Items[1]).IsEnabled,
                    "More audio/chapter availability differs from current source inventory.");
                Require(list.SelectedIndex == 1, "More initially focuses an unavailable row instead of its first available action.");
                var disabledAudio = (ListViewItem)list.Items[0];
                Require(((FrameworkElement)disabledAudio.Content).Opacity < .8 &&
                    Descendants<TextBlock>(disabledAudio).Any(text => text.Text == "No alternate audio tracks."),
                    "Unavailable More choices lack visible disabled treatment or reason.");
                Require(mpv.IsPaused, "Opening More changed genuine pause.");
                Call(menu, "Input", "end");
                Call(menu, "Input", "up");
                Require(list.SelectedIndex == 5, "More stopped skipping its unavailable maximum-volume action.");
                Call(menu, "Input", "home");
                Require(list.SelectedIndex == 1, "More stopped skipping unavailable Audio at Home.");
                await Capture(menu, "more-compact.png");
                async Task Choose(int index)
                {
                    Call(menu, "Input", "home");
                    // Home skips the explicitly unavailable Audio row.
                    for (var i = list.SelectedIndex; i < index; i++) Call(menu, "Input", "down");
                    Require(list.SelectedIndex == index, "More keyboard navigation did not reach the selected action.");
                    Activate(menu);
                    await Until(() => !ReferenceEquals(Menu(service), menu), "More selection did not release its presentation.");
                }
                await Choose(1);
                await Until(() => Menu(service) is { } child && ((ListView)Get(child, "_list")!).Items.Count == 72,
                    "More did not hand off to real native Chapters.");
                var childMenu = Menu(service)!;
                Call(menu, "Input", "cancel");
                Require(ReferenceEquals(Menu(service), childMenu), "Retired More dismissal retired the child menu.");
                Call(childMenu, "Input", "cancel");
                await Until(() => Menu(service) == null, "Child menu did not close.");
                mpv.SetVolume(50);
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                await Choose(6);
                await Until(() => Math.Abs(mpv.GetPropertyDouble("volume") - 60) < .1 && Math.Abs(service.Volume - 60) < .1,
                    "More Volume + bypassed existing actual-volume and saved service policy.");
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                await Choose(5);
                await Until(() => Math.Abs(mpv.GetPropertyDouble("volume") - 50) < .1, "More Volume - did not clamp/change actual volume.");
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                await Choose(4);
                await Until(() => mpv.GetPropertyDouble("panscan") > 0, "More Fill action did not use existing panscan policy.");
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                Require(AutomationProperties.GetName((ListViewItem)list.Items[4]).Contains("Contain", StringComparison.Ordinal),
                    "More did not show accepted fill state.");
                await Choose(7);
                await Until(mpv.GetMute, "More mute did not change actual native state.");
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                // The real PiP route positions its video on the monitor. Keep
                // only this owned fixture HWND visually empty while still
                // exercising the service command and native mode transition.
                var region = CreateRectRgn(0, 0, 0, 0);
                Require(region != IntPtr.Zero, "Cannot isolate the synthetic PiP surface.");
                if (SetWindowRgn(video.Hwnd, region, false) == 0)
                {
                    DeleteObject(region);
                    throw new InvalidOperationException("Cannot isolate the synthetic PiP surface.");
                }
                await Choose(3);
                await Until(() => service.State == PlayerState.PictureInPicture, "More PiP bypassed existing native mode transition.");
                Require(mpv.IsPaused, "More utility actions changed genuine pause.");
                Program.Log("PASS: actual native More order, availability, child handoff, volume/save, fill/contain, mute and PiP.");
                return;
            }
            Require(list.Items.Count == 72, "Native Chapters omitted sparse server indices.");
            Require((string)((ListViewItem)list.Items[0]).Tag == "chapter:4", "Native chapter identity was replaced by list position.");
            Require(mpv.IsPaused, "Opening Chapters changed genuine pause.");
            foreach (var size in new[] { (460, 700), (1920, 1080), (1280, 720) })
            {
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2));
                video.SynchronizePosition();
                await Until(() => GetWindowRect(video.Hwnd, out var surface) && GetWindowRect(Hwnd(menu), out var bounds)
                    && bounds.Left >= surface.Left && bounds.Right <= surface.Right && bounds.Top >= surface.Top && bounds.Bottom <= surface.Bottom,
                    "Native Chapters escaped resized owner bounds.");
            }
            Call(menu, "Input", "end");
            Require(list.SelectedIndex == 71 && Descendants<ScrollViewer>(list).Any(s => s.VerticalOffset > 0), "Native chapter endpoint is unreachable.");
            await Capture(menu, "chapters-end.png");
            Call(menu, "Input", "home");
            Require(list.SelectedIndex == 0, "Native chapter Home did not reach first row.");
            Require(Descendants<Border>((DependencyObject)list.Items[0]).Any(b => AutomationProperties.GetName(b).StartsWith("Chapter preview:", StringComparison.Ordinal)),
                "Chapter row has no native preview/fallback slot.");
            using (var patch = JsonDocument.Parse("""{"file_id":42,"chapter_index":4,"thumbnail_url":"https://native-chapters.fixture.invalid/preview.png"}"""))
                Call(service, "ApplyRealtimeChapterThumbnail", patch.RootElement);
            await Until(() => Descendants<Image>(list).Any(i => i.Source is BitmapImage { PixelWidth: > 0 }), "Ready thumbnail did not decode from the existing cache.");
            Require(ReferenceEquals(Menu(service), menu) && list.SelectedIndex == 0 && mpv.IsPaused, "Ready patch replaced the menu, lost focus or changed pause.");
            var retainedImage = Descendants<Image>((DependencyObject)list.Items[0]).Single().Source;
            using (var patch = JsonDocument.Parse("""{"file_id":42,"chapter_index":17,"thumbnail_url":"https://native-chapters.fixture.invalid/another.png"}"""))
                Call(service, "ApplyRealtimeChapterThumbnail", patch.RootElement);
            await Task.Delay(100);
            Require(ReferenceEquals(Descendants<Image>((DependencyObject)list.Items[0]).Single().Source, retainedImage),
                "A different chapter's ready patch discarded an accepted preview.");
            await Capture(menu, "chapters-ready.png");
            Call(menu, "Input", "cancel");
            await Until(() => Menu(service) == null, "Chapter dismissal stranded menu input.");
            Require(((SemaphoreSlim)Get(service, "_playbackDialogGate")!).CurrentCount == 1, "Chapter close leaked dialog ownership.");

            // Actual caller selects the sparse server index's canonical time.
            menu = await Open();
            Call(menu, "Input", "end"); Activate(menu);
            await Until(() => Math.Abs(mpv.Position - 71 / 3d) < 1.5, "Native chapter selected row index instead of server chapter time.");
            Require(mpv.IsPaused, "Selecting a chapter resumed genuinely paused playback.");
            service.SetPaused(false);
            await Until(() => !mpv.IsPaused, "Generated playback did not resume.");
            menu = await Open(); Call(menu, "Input", "home"); Activate(menu);
            await Until(() => mpv.Position < 3 && !mpv.IsPaused, "Chapter seek did not retain playing intent.");

            service.SetPaused(true);
            menu = await Open();
            var old = menu;
            Set(service, "_fileLoadGeneration", (long)Get(service, "_fileLoadGeneration")! + 1);
            await Until(() => Menu(service) == null, "File replacement left native chapters usable.");
            var before = mpv.Position;
            Call(old, "Input", "end"); Call(old, "Input", "activate");
            await Task.Delay(100);
            Require(Math.Abs(mpv.Position - before) < .1 && mpv.IsPaused, "Retired chapter callback issued seek or resume.");

            // Late image work must not revive a retired presentation.
            CallIfPresent(service, "PublishNativeMenuScopes");
            version.Chapters[0].ThumbnailUrl = "https://native-chapters.fixture.invalid/delayed.png";
            menu = await Open();
            await wire.PreviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Call(menu, "Input", "cancel");
            wire.PreviewReleased.TrySetResult();
            await Task.Delay(150);
            Require(Menu(service) == null, "Late thumbnail revived a dismissed menu.");

            // Guests get explicit unavailability; room seek authority stays downstream.
            service.SetWatchTogetherPlayback(true);
            CallIfPresent(service, "PublishNativeMenuScopes");
            menu = await Open();
            list = (ListView)Get(menu, "_list")!;
            Require(list.Items.OfType<ListViewItem>().All(row => !row.IsEnabled), "Guest chapter rows grant room seek authority.");
            var roomRequests = 0;
            service.RoomTransportRequested += (_, _, _) => roomRequests++;
            var guestPosition = mpv.Position;
            var guestForeground = GetForegroundWindow();
            Call(menu, "Input", "end");
            await Task.Delay(80);
            var guestEnd = list.SelectedIndex;
            var endVisible = SelectedRowVisible(list);
            var endOffset = Descendants<ScrollViewer>(list).First(view => view.ViewportHeight > 0).VerticalOffset;
            Call(menu, "Input", "home");
            await Task.Delay(80);
            var guestHome = list.SelectedIndex;
            var homeVisible = SelectedRowVisible(list);
            mpv.SendScriptMessage("osc-controller-nav", "down");
            await Task.Delay(100);
            var guestDown = list.SelectedIndex;
            var downVisible = SelectedRowVisible(list);
            mpv.SendScriptMessage("osc-controller-activate");
            if (pointerSelection) Activate(menu);
            await Task.Delay(80);
            var guestInert = roomRequests == 0 && mpv.IsPaused && Math.Abs(mpv.Position - guestPosition) < .1;
            Program.Log($"Guest Chapters navigation: End={guestEnd}, visible={endVisible}, scroll={endOffset:0.#}; Home={guestHome}, visible={homeVisible}; controller Down={guestDown}, visible={downVisible}; inert={guestInert}.");
            Require(guestEnd == 71 && endVisible && endOffset > 0, "Guest End cannot browse the disabled chapter inventory endpoint.");
            Require(guestHome == 0 && homeVisible, "Guest Home cannot return to the first disabled chapter.");
            Require(guestDown == 1 && downVisible, "Guest controller Down cannot browse disabled chapters.");
            Require(guestInert && list.Items.OfType<ListViewItem>().All(row => !row.IsEnabled),
                "Guest browsing granted seek activation or changed real playback.");
            Require(list.IsEnabled && list.IsTabStop, "Read-only Chapters has no enabled keyboard receiver.");
            Require(GetForegroundWindow() != Hwnd(menu) || guestForeground == Hwnd(menu),
                "Guest inventory navigation acquired foreground from another application.");
            await Capture(menu, "chapters-guest-browsing.png");
            Call(menu, "Input", "cancel");
            Program.Log("PASS: actual native Chapters sparse identity, long list, compact bounds, ready images, pause/playing seek, stale owner and guest permissions.");
        }
        finally
        {
            wire.PreviewReleased.TrySetResult();
            CallIfPresent(service, "RetireNativeMenus");
            CallIfPresent(service, "RetireNativeAudioMenu");
            Call(service, "StopPlaybackStallWatchdog");
            mpv.ScriptMessageReceived -= Message;
            Set(service, "_videoWindow", null); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            appServices.SetValue(null, oldServices);
            video.Dispose(); mpv.Dispose();
            await Task.Delay(40);
            try { foreground.Complete(); }
            finally { Environment.SetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE", previousCase); }
        }
        void Activate(object target)
        {
            if (!pointerSelection) { Call(target, "Input", "activate"); return; }
            var selectedRow = ((ListView)Get(target, "_list")!).SelectedItem as ListViewItem;
            Require(selectedRow?.Tag is string, "The pointer selection fixture has no current native row.");
            Program.Log("Fixture invokes actual shared pointer-selection route: " + selectedRow!.Tag);
            Call(target, "Select", (string)selectedRow.Tag, true);
        }
        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = completion;
            try { mpv.SendScriptMessage("osc-chapters-fixture-query", id, more ? "more" : "chapters"); return await completion.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { queries.TryRemove(id, out _); }
        }
        async Task<object> Open()
        {
            mpv.SendScriptMessage("osc-show");
            var state = await Query();
            // Native owner resize and mpv's published OSD dimensions settle
            // on separate pumps. Drive the visible compact opener only after
            // it exists; never issue/replay a click at the previous layout.
            var geometryWait = Stopwatch.StartNew();
            while (more && (!state.GetProperty("more_available").GetBoolean() || !state.GetProperty("scope_present").GetBoolean()))
            {
                if (geometryWait.Elapsed > TimeSpan.FromSeconds(8))
                    throw new InvalidOperationException("The compact More opener did not become available after owner resize.");
                await Task.Delay(20);
                state = await Query();
            }
            Program.Log($"Native {(more ? "More" : "Chapters")} opener: {state.GetProperty("width")}x{state.GetProperty("height")}, x={state.GetProperty("x")}, y={state.GetProperty("y")}, More={state.GetProperty("more_available")}, scope={state.GetProperty("scope_present")}.");
            mpv.SendScriptMessage("osc-mouse-move", state.GetProperty("x").ToString(), state.GetProperty("y").ToString());
            mpv.SendScriptMessage("osc-mouse-down", state.GetProperty("x").ToString(), state.GetProperty("y").ToString());
            mpv.SendScriptMessage("osc-mouse-up", state.GetProperty("x").ToString(), state.GetProperty("y").ToString());
            mpv.SendScriptMessage("osc-video-click", state.GetProperty("x").ToString(), state.GetProperty("y").ToString());
            try
            {
                await Until(() => Menu(service) is { } m && ((ListView)Get(m, "_list")!).XamlRoot != null,
                    "Actual OSC " + (more ? "More" : "Chapters") + " opener produced no native menu.");
            }
            catch
            {
                var after = await Query();
                Program.Log($"Native opener after failure: {after.GetProperty("width")}x{after.GetProperty("height")}, x={after.GetProperty("x")}, y={after.GetProperty("y")}, More={after.GetProperty("more_available")}, scope={after.GetProperty("scope_present")}.");
                throw;
            }
                var opened = Menu(service)!;
                foreground.WatchMenu((Window)Get(opened, "_window")!, Hwnd(opened));
                return opened;
        }
    }

    private static object? Menu(PlayerService service) => typeof(PlayerService).GetField("_nativeMenu", Private)?.GetValue(service);
    private static object? Get(object target, string field) => target.GetType().GetField(field, Private)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void CallIfPresent(object target, string method) => target.GetType().GetMethod(method, Private)?.Invoke(target, null);
    private static IntPtr Hwnd(object menu) => (IntPtr)menu.GetType().GetProperty("Hwnd", Private)!.GetValue(menu)!;
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Native fixture command failed.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(message); await Task.Delay(20); }
    }
    private static bool SelectedRowVisible(ListView list)
    {
        if (list.SelectedItem is not FrameworkElement row || row.ActualHeight <= 0) return false;
        var viewport = Descendants<ScrollViewer>(list).First(view => view.ViewportHeight > 0);
        var origin = row.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(0, 0));
        return origin.Y >= -1 && origin.Y + row.ActualHeight <= viewport.ActualHeight + 1;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task Capture(object menu, string name)
    {
        var root = (Grid)Get(menu, "_root")!;
        await Task.Delay(80);
        var target = new RenderTargetBitmap(); await target.RenderAsync(root);
        var pixels = await target.GetPixelsAsync();
        var path = Path.Combine(Program.ResultDirectory, name);
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var reader = DataReader.FromBuffer(pixels); var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private static byte[] Clip()
    {
        using var clip = new MemoryStream();
        clip.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
        var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128);
        for (var frame = 0; frame < 900; frame++)
        {
            Array.Fill(pixels, (byte)(32 + frame % 100), 0, 160 * 90);
            clip.Write(Encoding.ASCII.GetBytes("FRAME\n")); clip.Write(pixels);
        }
        return clip.ToArray();
    }
    private static async Task<byte[]> PreviewBytes()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = Enumerable.Range(0, 160 * 96).SelectMany(_ => new byte[] { 40, 140, 240, 255 }).ToArray();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 160, 96, 96, 96, pixels);
        await encoder.FlushAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size]; reader.ReadBytes(bytes);
        return bytes;
    }
    private sealed class ChapterWire : HttpMessageHandler
    {
        internal byte[] PreviewBytes = [];
        internal readonly TaskCompletionSource PreviewStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource PreviewReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri!.Host == "native-chapters.fixture.invalid", "Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith(".png"))
            {
                if (path.EndsWith("delayed.png")) { PreviewStarted.TrySetResult(); await PreviewReleased.Task; }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(PreviewBytes) };
            }
            if (path.Contains("/settings/")) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start")) return Json("""{"outcome":"applied"}""");
            return Json("""
                {"protocol_version":3,"outcome":"playable","session_id":"native-chapter-fixture","playback_plan":{
                "plan_id":"chapter-plan","plan_attempt_key":"synthetic","session_id":"native-chapter-fixture","effective_media_file_id":42,
                "delivery":"original_http","stream":{"url":"https://native-chapters.fixture.invalid/unused.y4m"},
                "timeline":{"can_seek_anywhere":true},"source":{"duration_seconds":30},
                "selected_tracks":{"audio":null,"subtitle":null},"subtitle":{"mode":"off","inventory":[]}}}
                """);
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
}

