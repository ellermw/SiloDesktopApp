using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using Windows.Graphics;

internal static partial class PlaybackAudioMenuNativeFixture
{
    internal static async Task RunPartyAsync(Window owner, string mode)
    {
        using var foreground = new ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(owner));
        using var windows = new PartyWindowTrace(foreground);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new MediaServer(); using var wire = new PartyWire(media.Origin); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://native-audio.fixture.invalid"); client.SetProfile("synthetic");
        using var auth = new AuthService(client, new AuthApi(client));
        var playback = new PlaybackApi(client); var catalog = new CatalogApi(client);
        using var player = new PlayerService(playback, catalog, auth, client,
            new SettingsService(Path.Combine(Program.ResultDirectory, "party-menu-settings")), new SettingsApi(client));
        using var manager = new PlaybackManager(playback, catalog, auth, client);
        manager.UseWatchDetail(new() { ContentId = "synthetic-video", Type = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        foreground.Observe("party-video", video.Hwnd, "video");
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv); mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(player, "_mpv", mpv); Set(player, "_videoWindow", video); Set(player, "_playbackManager", manager);
        Property(player, "State", PlayerState.Expanded); Property(player, "ContentId", "synthetic-video");
        Property(player, "Versions", manager.WatchDetail!.Versions); Call(player, "WireMpvEvents");
        var slot = typeof(SiloPlayer.App).GetField("_services", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var originalServices = slot.GetValue(null);
        using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(playback)
            .AddSingleton(player).AddSingleton<WatchTogetherCoordinator>().BuildServiceProvider();
        slot.SetValue(null, services);
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args)
        {
            if (args.Length > 0 && args[0] == "silo-party-fixture-ready") ready.TrySetResult();
            if (args.Length > 2 && args[0] == "silo-party-fixture-state" && queries.TryRemove(args[1], out var receiver))
            { using var doc = JsonDocument.Parse(args[2]); receiver.TrySetResult(doc.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        WatchTogetherCoordinator? coordinator = null;
        using var room = new WatchTogetherRoomViewModel(playback, client) { RoomId = "synthetic-room", RoomToken = "synthetic-proof",
            Room = PartySnapshot(), ConnectionState = "connected" };
        try
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(player, "StopPlaybackStallWatchdog"); player.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Party synthetic video did not settle.");
            var observer = Path.Combine(Program.ResultDirectory, "party-observer.lua");
            await File.WriteAllTextAsync(observer, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs/mpv/scripts/silo-osc.lua")) + """

                mp.register_script_message('osc-party-fixture-query', function(id)
                    compute_layout()
                    local r=state.layout.btn_party or {}
                    local request=state.native_menu_request or {}
                    mp.commandv('script-message','silo-party-fixture-state',id,require('mp.utils').format_json({
                        x=r.cx or 0,y=r.cy or 0,w=r.w or 0,scope=state.native_party_scope or '',sequence=request.sequence or 0}))
                end)
                mp.commandv('script-message','silo-party-fixture-ready')
                """);
            Command(mpv, "load-script", observer); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator = services.GetRequiredService<WatchTogetherCoordinator>(); coordinator.SetActiveRoom(room);
            ((DispatcherTimer)Get(coordinator, "_timer")!).Stop();
            Call(player, "PublishNativeMenuScopes");
            await Until(async () => (await Query()).GetProperty("scope").GetString()?.Length == 32, "Native Party scope was not published.");
            var position = mpv.Position;
            var menu = await Open(); var list = (ListView)Get(menu, "_list")!;
            Require(list.Items.Count == 4 && Row("stop").IsEnabled && Row("end").IsEnabled, "Host actions are missing or unavailable.");
            Require(GetWindow(MenuHwnd(menu), 4) == video.Hwnd, "Party surface lost its native video owner.");
            await Capture(menu, "party-host.png");
            foreach (var size in new[] { (460, 700), (900, 540), (1280, 720) })
            {
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2));
                video.SynchronizePosition(); await Task.Delay(150);
                Require(GetWindowRect(video.Hwnd, out var vr) && GetWindowRect(MenuHwnd(menu), out var mr)
                    && mr.Left >= vr.Left && mr.Right <= vr.Right && mr.Top >= vr.Top && mr.Bottom <= vr.Bottom,
                    "Party host escaped its small/large video bounds.");
                var end = Row("end");
                var endBottom = end.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, end.ActualHeight)).Y;
                Require(endBottom <= list.ActualHeight + 1, "Viewer footer crowds the End action out of the initial native action viewport.");
                await Capture(menu, $"party-{size.Item1}.png");
            }
            Call(menu, "Input", "end"); var selection = list.SelectedIndex;
            var update = PartySnapshot(generation: 3); update.Members[0].DisplayName = "Updated viewer"; room.Room = update;
            await Task.Delay(80);
            Require(list.SelectedIndex == selection && Descendants<TextBlock>((Grid)Get(menu, "_root")!).Any(t => t.Text.Contains("Updated viewer")),
                "Member update lost native selection or failed to refresh the real label.");
            Require(mpv.IsPaused && Math.Abs(mpv.Position - position) < 0.2 && wire.PartyCalls == 0,
                "Opening, scrolling, resizing or refreshing Party issued playback/room commands.");
            var guest = PartySnapshot(generation: 3); guest.SelfCanManageRoom = false; guest.SelfCanControlTransport = true;
            room.Room = guest; await Task.Delay(50);
            Require(list.Items.Count == 1 && !((ListViewItem)list.Items[0]).IsEnabled, "Guest inherited host native actions.");
            Call(menu, "Input", "activate"); Require(wire.PartyCalls == 0, "Disabled guest status dispatched a room command.");
            room.Room = PartySnapshot(generation: 3); await Task.Delay(50);
            if (mode == "stale")
            {
                Set(player, "_playbackTerminalState", Activator.CreateInstance(typeof(PlayerService).GetNestedType("PlaybackTerminalState",
                    System.Reflection.BindingFlags.NonPublic)!, [0d, "synthetic-failure", true, true, null]));
                InvokeRow("stop");
                Require(wire.PartyCalls == 0, "Retired Party intent dispatched Stop while an unrelated playback failure owned the surface.");
                await SendOpenGesture(); await Task.Delay(120);
                Require(Menu(player) == null && ((SemaphoreSlim)Get(player, "_playbackDialogGate")!).CurrentCount == 1,
                    "An initial Party request during playback failure retained a closed native menu or shared lease.");
                Set(player, "_playbackTerminalState", null);
                menu = await Open(); list = (ListView)Get(menu, "_list")!;
                InvokeRow("end"); await Until(() => Get(player, "_nativePartyEndDialog") is ContentDialog { XamlRoot: not null }, "End confirmation was not opened.");
                using var replacement = new WatchTogetherRoomViewModel(playback, client) { RoomId = "replacement", RoomToken = "replacement-proof",
                    Room = PartySnapshot("replacement"), ConnectionState = "connected" };
                coordinator.SetActiveRoom(replacement); ((DispatcherTimer)Get(coordinator, "_timer")!).Stop();
                await Until(() => Get(player, "_nativePartyEndDialog") == null, "Replacement room did not retire old confirmation.");
                Require(wire.PartyCalls == 0 && ((SemaphoreSlim)Get(player, "_playbackDialogGate")!).CurrentCount == 1,
                    "Old confirmation dispatched or leaked the shared lease after room replacement.");
                Program.Log("PASS: actual native Party room replacement retires menu/confirmation without action, focus restore or lease leak.");
                return;
            }
            // Actual control activation, with only WinUI's explicit UIA
            // foreground request isolated on the interactive development desktop.
            wire.HoldPolicy = true; InvokeRow("toggle-policy");
            await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(room.IsBusy && !Row("stop").IsEnabled && AutomationProperties.GetName(Row("toggle-policy")).Contains("Pending"),
                "Real policy action lacks pending/disabled state.");
            wire.Release.TrySetResult(); await Until(() => !room.IsBusy, "Policy mutation did not settle.");
            Require(room.Room!.GuestControlPolicy == "guest_play_pause", "Policy action did not use actual guarded room mutation.");
            InvokeRow("end"); await Until(() => Get(player, "_nativePartyEndDialog") is ContentDialog { XamlRoot: not null }, "End confirmation did not show.");
            windows.Phase("end-dialog-shown");
            Require(Menu(player) == null && ((SemaphoreSlim)Get(player, "_playbackDialogGate")!).CurrentCount == 0,
                "End confirmation overlapped native menu or failed to acquire released shared lease.");
            var dialog = (ContentDialog)Get(player, "_nativePartyEndDialog")!;
            await MediaParityNativeFixture.CaptureAsync(dialog, "party-end-confirmation.png");
            Require(dialog.IsEnabled && dialog.IsPrimaryButtonEnabled && dialog.DefaultButton == ContentDialogButton.Close
                && Descendants<Button>(dialog).Where(button => button.Name is "PrimaryButton" or "CloseButton").All(button => button.IsEnabled && button.IsTabStop),
                "Background initial-focus suppression left confirmation controls or safe default unusable.");
            Require(wire.Ends == 0, "Opening End confirmation issued DELETE.");
            if (mode == "end")
            {
                wire.FailEnd = true; PressPrimary(dialog);
                await Until(() => wire.Ends == 1 && !room.IsBusy && dialog.IsPrimaryButtonEnabled,
                    "Rejected End did not expose a retryable confirmation.");
                Require(room.ClosedReason == null && Descendants<TextBlock>(dialog).Any(text => text.Text.Contains("Failed to close room")),
                    "Rejected End closed the room or lost inline error feedback.");
                wire.FailEnd = false; wire.HoldEnd = true;
                PressPrimary(dialog);
                await wire.EndEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Require(!dialog.IsPrimaryButtonEnabled && dialog.PrimaryButtonText == "Ending…", "Pending End still permits duplicate confirmation.");
                wire.EndRelease.TrySetResult();
                await Until(() => wire.Ends == 2 && room.ClosedReason == "host_left", "Confirmed End did not reach real room DELETE.");
                await Until(() => Get(player, "_nativePartyEndDialog") == null, "Accepted End confirmation did not close.");
                Require(wire.Stops == 0, "End was confused with Stop.");
                Program.Log("PASS: actual native Party End confirmation sends distinct DELETE and retires matching playback.");
                return;
            }
            windows.Phase("end-dialog-cancel");
            dialog.Hide(); await Until(() => Get(player, "_nativePartyEndDialog") == null, "Cancel did not close confirmation.");
            windows.Phase("end-dialog-cancelled");
            Require(wire.Ends == 0 && ((SemaphoreSlim)Get(player, "_playbackDialogGate")!).CurrentCount == 1, "Cancel dispatched End or leaked gate.");
            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            wire.FailStop = true; InvokeRow("stop"); await Until(() => !room.IsBusy && room.ErrorMessage != null, "Rejected Stop lost retry feedback.");
            Require(room.Room!.Phase == "playing" && Menu(player) != null && Descendants<TextBlock>((Grid)Get(menu, "_root")!).Any(t => t.Text.Contains("Could not stop")),
                "Rejected Stop lost accepted room phase or inline native error.");
            wire.FailStop = false; InvokeRow("stop");
            await Until(() => room.Room?.Phase == "lobby" && player.State == PlayerState.Idle, "Actual Stop did not return to lobby and retire matching playback.");
            Require(ReferenceEquals(coordinator.ActiveRoom, room) && room.ClosedReason == null && room.Room!.SelectedContentId == "synthetic-video"
                && wire.Ends == 0 && wire.Stops == 2, "Stop ended/left the room or lost its staged item.");
            Program.Log("PASS: actual OSC→native Party: owned bounded host, members/status, stable selection, isolated UIA policy pending, End cancel/lease handoff, Stop failure/retry and matching-session lobby transition.");

            ListViewItem Row(string id) => list.Items.Cast<ListViewItem>().Single(row => (string)row.Tag == id);
            void InvokeRow(string id)
            {
                var row = Row(id); list.SelectedItem = row; list.ScrollIntoView(row); list.UpdateLayout();
                if (id == "end" && mode == "direct")
                {
                    windows.Phase("direct-end-begin"); Call(menu, "Input", "activate"); windows.Phase("direct-end-returned");
                    if (Get(player, "_nativePartyEndDialog") is ContentDialog direct) windows.WatchDialog(direct, PartyOwnedDialogWindow(video.Hwnd));
                    return;
                }
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(row) ?? new ListViewItemAutomationPeer(row);
                using var veto = new OwnMenuActivationVeto(MenuHwnd(menu));
                windows.Phase("uia-row-" + id + "-begin");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                windows.Phase("uia-row-" + id + "-returned");
                if (id == "end" && Get(player, "_nativePartyEndDialog") is ContentDialog pending)
                    windows.WatchDialog(pending, PartyOwnedDialogWindow(video.Hwnd));
            }
            void PressPrimary(ContentDialog target)
            {
                target.UpdateLayout();
                var primary = Descendants<Button>(target).Single(button => button.Name == "PrimaryButton");
                var host = PartyOwnedDialogWindow(video.Hwnd);
                using var veto = new OwnMenuActivationVeto(host);
                windows.Phase("uia-primary-begin");
                ((IInvokeProvider)new ButtonAutomationPeer(primary).GetPattern(PatternInterface.Invoke)).Invoke();
                windows.Phase("uia-primary-returned");
            }
        }
        finally
        {
            wire.Release.TrySetResult();
            wire.EndRelease.TrySetResult();
            if (Get(player, "_nativePartyEndDialog") is ContentDialog dialog) dialog.Hide();
            coordinator?.ClearActiveRoom(); Call(player, "RetireNativeMenus");
            mpv.ScriptMessageReceived -= Message;
            Set(player, "_videoWindow", null); Set(player, "_mpv", null); Set(player, "_playbackManager", null);
            slot.SetValue(null, originalServices); video.Hide();
            windows.Phase("cleanup");
            await Task.Delay(40); foreground.Complete();
        }
        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = completion; mpv.SendScriptMessage("osc-party-fixture-query", id);
            try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(3)); } finally { queries.TryRemove(id, out _); }
        }
        async Task SendOpenGesture()
        {
            var state = await Query(); Require(state.GetProperty("w").GetDouble() > 0, "Party opener geometry is absent.");
            var x = state.GetProperty("x").ToString(); var y = state.GetProperty("y").ToString();
            mpv.SendScriptMessage("osc-show"); mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
        }
        async Task<object> Open()
        {
            await SendOpenGesture();
            await Until(() => Menu(player) is { } current && ((ListView)Get(current, "_list")!).XamlRoot != null, "Actual Party opener did not show native controls.");
            var result = Menu(player)!; foreground.WatchMenu((Window)Get(result, "_window")!, MenuHwnd(result)); return result;
        }
    }

    private static async Task Until(Func<Task<bool>> ready, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!await ready()) { if (DateTime.UtcNow > deadline) throw new InvalidOperationException(message); await Task.Delay(25); }
    }
    private static WatchTogetherRoomSnapshot PartySnapshot(string roomId = "synthetic-room", int generation = 1) => new()
    {
        RoomId = roomId, Code = "TEST1234", Phase = "playing", PlaybackState = "paused", IsPaused = true, Generation = generation,
        SelectedContentId = "synthetic-video", SelectedFileId = 42, MemberCount = 40, SelfCanManageRoom = true, SelfCanControlTransport = true,
        Members = Enumerable.Range(0, 40).Select(index => new WatchTogetherRoomMember { UserId = index, ProfileId = "viewer-" + index,
            DisplayName = index == 39 ? "Long viewer 日本語 " + new string('x', 120) : "Viewer " + index,
            IsSelf = index == 0, IsHost = index == 0, Connected = index != 39,
            IsBuffering = index == 1, IsSyncing = index == 2, IsReady = index == 3 }).ToList(),
    };
    private delegate bool PartyEnumWindow(IntPtr hwnd, IntPtr parameter);

    // Diagnostic only: records creation/activation of this fixture UI thread's
    // windows. It never changes or suppresses activation, including destroyed HWNDs.
    private sealed class PartyWindowTrace : IDisposable
    {
        private readonly PartyCbtCallback _callback;
        private readonly List<object> _events = [];
        private readonly ForegroundOwnershipProbe _foreground;
        private IntPtr _hook;
        private string _phase = "fixture-setup";
        internal PartyWindowTrace(ForegroundOwnershipProbe foreground)
        {
            _foreground = foreground;
            _callback = (code, window, data) =>
            {
                if (code is 3 or 4 or 5 or 9)
                {
                    if (code == 3) foreground.RegisterWindow(window, "party-thread-created-window");
                    _events.Add(new { tick = Environment.TickCount64, code, hwnd = window.ToInt64(),
                        owner = GetWindow(window, 4).ToInt64(), phase = _phase, stack = code == 5 ? Environment.StackTrace : null });
                }
                return PartyCallNextHookEx(_hook, code, window, data);
            };
            _hook = PartySetWindowsHookExW(5, _callback, IntPtr.Zero, GetCurrentThreadId());
            Require(_hook != IntPtr.Zero, "Cannot record fixture window activation trace.");
        }
        internal void Phase(string phase) { _phase = phase; _foreground.Observe(phase); }
        internal void WatchDialog(ContentDialog dialog, IntPtr hwnd)
        {
            _foreground.RegisterWindow(hwnd, "party-confirmation-host");
            dialog.Loaded += (_, _) => Phase("dialog-loaded");
            dialog.Opened += (_, _) => Phase("dialog-opened");
            dialog.GettingFocus += (_, args) =>
            {
                _events.Add(new { tick = Environment.TickCount64, phase = "dialog-getting-focus", focusState = args.FocusState.ToString(),
                    newElement = args.NewFocusedElement?.GetType().Name, oldElement = args.OldFocusedElement?.GetType().Name });
                Phase("dialog-getting-focus");
            };
            dialog.GotFocus += (_, _) => Phase("dialog-got-focus");
        }
        public void Dispose()
        {
            PartyUnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; GC.KeepAlive(_callback);
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "party-window-trace.json"), JsonSerializer.Serialize(_events,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr PartyCbtCallback(int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] private static extern IntPtr PartySetWindowsHookExW(int id, PartyCbtCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll", EntryPoint = "CallNextHookEx")] private static extern IntPtr PartyCallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")] private static extern bool PartyUnhookWindowsHookEx(IntPtr hook);
    }
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, PartyEnumWindow callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static IntPtr PartyOwnedDialogWindow(IntPtr video)
    {
        var result = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) => { if (GetWindow(hwnd, 4) == video && IsWindowVisible(hwnd)) result = hwnd; return true; }, IntPtr.Zero);
        Require(result != IntPtr.Zero, "Owned confirmation host HWND not found."); return result;
    }
    private sealed class PartyWire(string origin) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _playback = new(new Wire(origin, "/video.y4m"));
        public int PartyCalls, Stops, Ends;
        public bool HoldPolicy, FailStop, FailEnd, HoldEnd;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EndRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.Contains("/watch-together/rooms/")) return await _playback.SendAsync(request, ct);
            Interlocked.Increment(ref PartyCalls);
            if (request.Method == HttpMethod.Delete)
            {
                Interlocked.Increment(ref Ends);
                if (HoldEnd) { EndEntered.TrySetResult(); await EndRelease.Task; }
                return new(FailEnd ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent) { Content = new StringContent("{}") };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/policy"))
            {
                if (HoldPolicy) { Entered.TrySetResult(); await Release.Task; }
                var snapshot = PartySnapshot(generation: 4); snapshot.GuestControlPolicy = "guest_play_pause";
                return Json(snapshot);
            }
            Require(request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/playback/stop"), "Party emitted an invented endpoint.");
            Interlocked.Increment(ref Stops);
            if (FailStop) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
            var lobby = PartySnapshot(generation: 5); lobby.Phase = "lobby"; lobby.PlaybackState = "idle"; return Json(lobby);
        }
        private static HttpResponseMessage Json(WatchTogetherRoomSnapshot room) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { room }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })),
        };
        protected override void Dispose(bool disposing) { if (disposing) _playback.Dispose(); base.Dispose(disposing); }
    }
}
