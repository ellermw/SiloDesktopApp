using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class MediaInteractionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var appServices = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)appServices.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); handler.Client = client; client.SetBaseUrl("https://media-interaction-fixture.invalid"); client.SetProfile("host");
        var api = new PlaybackApi(client); var auth = new AuthService(client, new AuthApi(client));
        using var fixture = new FixtureServices(original, client, api, auth);
        appServices.SetValue(null, fixture);
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_INTERACTION_CASE") is "recent-unknown")
            { await RecentAndPasteAsync(parent, handler, client, auth); Program.Log("PASS: MEDIA_RECENT_UNKNOWN_COMPLETED"); return; }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_INTERACTION_CASE") is "candidate")
            {
                await CandidateAsync(parent, handler);
                Program.Log("PASS: MEDIA_CANDIDATE_DIAGNOSTIC_COMPLETED");
                return;
            }
            await StartPartyAsync(parent, handler, client);
            await RecentAndPasteAsync(parent, handler, client, auth);
            await CandidateAsync(parent, handler);
            await TwoClientsAsync(api, client);
            Program.Log("PASS: MEDIA_INTERACTIONS_COMPLETED all four awaited acceptance groups.");
        }
        finally { appServices.SetValue(null, original); }
    }

    private static async Task StartPartyAsync(StackPanel parent, Handler handler, SiloApiClient client)
    {
        var page = new ItemDetailPage { Width = 900, Height = 720 }; parent.Children.Add(page);
        using var navigation = new CancellationTokenSource();
        try
        {
            page.ViewModel.Item = new MediaItemDetail { ContentId = "fixture-series", Type = "series", Title = "The Fixture Series" };
            page.ViewModel.Seasons.Clear(); page.ViewModel.Seasons.Add(new Season { SeasonNumber = 1, ContentId = "fixture-season" });
            Set(page, "_navigationCts", navigation); Set(page, "_currentContentId", "fixture-series"); Set(page, "_playableContentId", "fixture-episode-1");
            await LayoutAsync(page);
            var task = (Task)Invoke(page, "ShowStartPartyAsync")!;
            ContentDialog? dialog = null; await UntilAsync(() => (dialog = OpenDialog(parent)) != null);
            var episode = Descendants(dialog!).OfType<ComboBox>().Single(control => Equals(control.Header, "Episode"));
            if (episode.Items.Count != 2) throw new InvalidOperationException("Actual Start Party dialog does not load playable episode choices.");
            episode.SelectedIndex = 1;
            var pause = Descendants(dialog!).OfType<ToggleSwitch>().Single(); pause.IsOn = true;
            await MediaParityNativeFixture.CaptureAsync(dialog!, "media-start-party-selection.png");
            // Revoke only the fake context once staging completes. This additionally
            // proves a stale dialog cannot persist authority or navigate afterward.
            handler.RevokeAfterStage = true;
            ClickPrimary(dialog!); await UntilAsync(() => handler.Stages == 1);
            using var stage = JsonDocument.Parse(handler.StageBody!);
            if (stage.RootElement.GetProperty("content_id").GetString() != "fixture-episode-2" || handler.Policy != "guest_play_pause" || handler.StartCalls != 0)
                throw new InvalidOperationException("Start Party doesn't stage the selected episode and policy without starting playback.");
            dialog!.Hide(); await task; client.SetProfile("host"); handler.RevokeAfterStage = false;
            Program.Log("PASS: actual Start Party episode selection, guest-pause policy, staged payload without autoplay, and stale-context guard.");
        }
        finally { Set(page, "_navigationCts", null); parent.Children.Remove(page); }
    }

    private static async Task RecentAndPasteAsync(StackPanel parent, Handler handler, SiloApiClient client, AuthService auth)
    {
        var store = typeof(ThemeMusicService).Assembly.GetType("SiloPlayer.Services.RecentPartyStore")!;
        var directory = Path.Combine(Program.ResultDirectory, "recent-parties-fixture");
        typeof(AuthService).GetProperty("SelectedProfileId")!.SetValue(auth, "fixture-history-host");
        foreach (var id in new[] { "active", "ended", "forbidden", "unavailable" })
        {
            var response = new WatchTogetherRoomResponse { Room = new() { RoomId = id, Code = id.ToUpperInvariant() }, RoomAccessToken = "fixture-proof-" + id };
            await (Task)store.GetMethod("RememberAsync")!.Invoke(null, [client, auth, response, directory])!;
        }
        var read = (Task)store.GetMethod("ReadAsync")!.Invoke(null, [client, auth, directory])!; await read;
        var entries = read.GetType().GetProperty("Result")!.GetValue(read)!;
        typeof(AuthService).GetProperty("SelectedProfileId")!.SetValue(auth, "fixture-history-guest");
        var other = (Task)store.GetMethod("ReadAsync")!.Invoke(null, [client, auth, directory])!; await other;
        if (((System.Collections.ICollection)other.GetType().GetProperty("Result")!.GetValue(other)!).Count != 0)
            throw new InvalidOperationException("Recent party proof leaks to another profile.");
        typeof(AuthService).GetProperty("SelectedProfileId")!.SetValue(auth, "fixture-history-host");
        var defaultPath = (string)store.GetMethod("PathFor", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, auth, null])!;
        var defaultTimestamp = File.Exists(defaultPath) ? File.GetLastWriteTimeUtc(defaultPath) : (DateTime?)null;
        if (File.ReadAllBytes(Directory.GetFiles(directory).Single()).AsSpan().IndexOf(Encoding.UTF8.GetBytes("fixture-proof")) >= 0)
            throw new InvalidOperationException("Recent room persistence stores unencrypted room proof.");
        var hub = new WatchTogetherJoinPage { Width = 900, Height = 720 }; parent.Children.Add(hub);
        typeof(WatchTogetherJoinPage).GetProperty("RecentPartyDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hub, directory);
        try
        {
            await LayoutAsync(hub);
            await (Task)Invoke(hub, "LoadRecentRoomsAsync", entries)!;
            var rows = ((StackPanel)hub.FindName("RecentRoomsList")).Children.OfType<Button>().ToArray();
            if (!rows.Any(row => row.IsEnabled && ((string)row.Content).Contains("Rejoin")) || !rows.Any(row => ((string)row.Content).Contains("Ended")) || !rows.Any(row => ((string)row.Content).Contains("Status unavailable")) || !rows.Any(row => row.Visibility == Visibility.Collapsed))
                throw new InvalidOperationException("Recent parties don't preserve active/ended/forbidden/unavailable states after persistence reload.");
            if (!rows.Single(row => ((string)row.Content).Contains("Status unavailable")).IsEnabled)
                throw new InvalidOperationException("An unknown recent-room status incorrectly blocks Rejoin despite its retained room proof.");
            await MediaParityNativeFixture.CaptureAsync(hub, "media-recent-party-states.png");
            await (Task)Invoke(hub, "ApplyPastedTextAsync", " ab-c 123 ")!;
            if (hub.ViewModel.RoomCode != "ABC123" || !hub.ViewModel.CanJoin) throw new InvalidOperationException("Paste affordance doesn't normalize room codes.");
            await hub.ViewModel.JoinByCodeCommand.ExecuteAsync(null);
            if (handler.JoinCode != "ABC123") throw new InvalidOperationException("Manual Join submits a different normalized code.");
            await (Task)Invoke(hub, "ApplyPastedTextAsync", "https://fixture.invalid/watch-together/join?token=fixture-invite")!;
            if (handler.JoinToken != "fixture-invite") throw new InvalidOperationException("Paste invite path does not submit the parsed invite proof.");
            await Task.Delay(100); // flush queued UI work before the next page is constructed
            if (hub.ViewModel.LastResponse != null) throw new InvalidOperationException("Detached hub does not consume completed join response safely.");
            var currentDefaultTimestamp = File.Exists(defaultPath) ? File.GetLastWriteTimeUtc(defaultPath) : (DateTime?)null;
            if (currentDefaultTimestamp != defaultTimestamp) throw new InvalidOperationException("Isolated paste test touched the default recent-party directory.");
            Program.Log("PASS: encrypted recent proof reload and rejoin availability states; actual paste parser/manual Join and invite payload, without accessing the OS clipboard.");
        }
        finally { Invoke(hub, "UnsubscribeFromViewModel"); parent.Children.Remove(hub); }
    }

    private static async Task CandidateAsync(StackPanel parent, Handler handler)
    {
        Program.Log("TRACE: candidate fixture before room constructor.");
        var room = new WatchTogetherRoomPage { Width = 900, Height = 720 }; parent.Children.Add(room);
        Program.Log("TRACE: candidate room constructed and attached.");
        try
        {
            room.ViewModel.RoomId = "fixture-room"; room.ViewModel.RoomToken = "fixture-room-proof";
            Program.Log("TRACE: candidate before room snapshot/capabilities.");
            room.ViewModel.Room = Snapshot(); await room.ViewModel.LoadCapabilitiesAsync();
            Program.Log("TRACE: candidate before layout.");
            await LayoutAsync(room);
            var candidate = new SiloPlayer.Core.Models.Home.MediaItem { ContentId = "fixture-episode-2", Type = "episode", Title = "The episode ahead of the guest" };
            handler.DelayMemberState = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Program.Log("TRACE: candidate before spotlight/member classification.");
            Invoke(room, "ShowCandidateSpotlight", candidate);
            Program.Log("TRACE: candidate spotlight attached, delayed classification pending.");
            var action = (Button)room.FindName("CandidatePlayBtn");
            if (action.IsEnabled) throw new InvalidOperationException("Candidate action remains enabled before watch-state classification.");
            handler.DelayMemberState.SetResult(); await UntilAsync(() => action.IsEnabled);
            if (!((TextBlock)room.FindName("CandidateMemberState")).Text.Contains("ahead")) throw new InvalidOperationException("Unseen guest episode does not expose spoiler warning.");
            var stages = handler.Stages;
            Invoke(room, "CandidatePlay_Click", action, new RoutedEventArgs());
            ContentDialog? dialog = null; await UntilAsync(() => (dialog = OpenDialog(parent)) != null);
            await MediaParityNativeFixture.CaptureAsync(dialog!, "media-party-spoiler-confirmation.png");
            dialog!.Hide(); await Task.Delay(100);
            if (handler.Stages != stages) throw new InvalidOperationException("Cancelling spoiler confirmation stages the episode.");
            handler.FailStage = true;
            Invoke(room, "CandidatePlay_Click", action, new RoutedEventArgs()); await UntilAsync(() => (dialog = OpenDialog(parent)) != null); ClickPrimary(dialog!);
            await UntilAsync(() => !room.ViewModel.IsBusy && room.ViewModel.ErrorMessage?.Contains("Could not stage") == true);
            if (((FrameworkElement)room.FindName("CandidateSpotlight")).Visibility != Visibility.Visible || !action.IsEnabled)
                throw new InvalidOperationException("Rejected staging loses candidate or prevents retry.");
            await MediaParityNativeFixture.CaptureAsync(room, "media-party-candidate-rejected.png");
            handler.FailStage = false; handler.DelayMemberState = null;
            room.ViewModel.Room!.SelfCanManageRoom = false;
            Invoke(room, "UpdateRoomUi");
            Set(room, "_candidateSpoilerRisk", false); handler.FailSuggestion = true;
            Invoke(room, "CandidatePlay_Click", action, new RoutedEventArgs()); await UntilAsync(() => handler.SuggestionCalls == 1 && !room.ViewModel.IsBusy);
            if (((FrameworkElement)room.FindName("CandidateSpotlight")).Visibility != Visibility.Visible || !action.IsEnabled || room.ViewModel.ErrorMessage == null)
                throw new InvalidOperationException("Host-pick guest suggestion failure loses candidate or hides recovery.");
            Program.Log("PASS: delayed member classification, ahead-episode confirmation/cancel, rejected staging preserves candidate and guest suggestion rejection recovers.");
        }
        finally { room.ViewModel.Dispose(); parent.Children.Remove(room); }
    }

    private static async Task TwoClientsAsync(PlaybackApi api, SiloApiClient client)
    {
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start(); var number = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{number}/"); listener.Start();
        using var host = new WatchTogetherRoomViewModel(api, client); using var guest = new WatchTogetherRoomViewModel(api, client);
        using var hostSocket = new ClientWebSocket(); using var guestSocket = new ClientWebSocket();
        async Task<WebSocket> ConnectAsync(ClientWebSocket socket)
        {
            var connecting = socket.ConnectAsync(new Uri($"ws://127.0.0.1:{number}/fixture"), CancellationToken.None);
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var accepted = await context.AcceptWebSocketAsync(null); await connecting; return accepted.WebSocket;
        }
        using var hostServer = await ConnectAsync(hostSocket); using var guestServer = await ConnectAsync(guestSocket);
        foreach (var (vm, socket) in new[] { (host, hostSocket), (guest, guestSocket) })
        {
            vm.RoomId = "fixture-room"; vm.RoomToken = "fixture-proof"; vm.Room = Snapshot(); vm.ConnectionState = "connected";
            Set(vm, "_ws", socket); await vm.LoadCapabilitiesAsync();
        }
        guest.Room!.SelfCanManageRoom = false;
        await guest.SetLobbyReadyAsync(true);
        var ready = await ReceiveAsync(guestServer); using var readyBody = JsonDocument.Parse(ready);
        if (readyBody.RootElement.GetProperty("type").GetString() != "lobby_ready" || !readyBody.RootElement.GetProperty("ready").GetBoolean()) throw new InvalidOperationException("Guest readiness isn't sent over its actual socket.");
        var snapshot = Snapshot(); snapshot.Members[1].LobbyReady = true;
        await SendAsync(hostServer, JsonSerializer.Serialize(new { type = "snapshot", room = snapshot }, Json));
        Invoke(host, "HandleFrame", await ReceiveAsync(hostSocket));
        if (host.Room?.Members[1].LobbyReady != true) throw new InvalidOperationException("Host doesn't consume guest readiness from real socket snapshot.");
        host.Room!.Phase = "playing"; host.Room.SelfCanControlTransport = true; host.AttachSession("fixture-session"); await ReceiveAsync(hostServer);
        host.RequestTransport("pause", 123, false);
        using var request = JsonDocument.Parse(await ReceiveAsync(hostServer));
        if (request.RootElement.GetProperty("action").GetString() != "pause") throw new InvalidOperationException("Host native transport does not emit pause request.");
        string? applied = null; guest.TransportCommandReceived += command => applied = command.Action;
        await SendAsync(guestServer, "{\"type\":\"transport_command\",\"action\":\"pause\",\"position_seconds\":123,\"is_paused\":true}");
        Invoke(guest, "HandleFrame", await ReceiveAsync(guestSocket));
        if (applied != "pause") throw new InvalidOperationException("Guest does not apply host transport callback.");
        await SendAsync(guestServer, "{\"type\":\"error\",\"message\":\"Fixture permission rejection\"}"); Invoke(guest, "HandleFrame", await ReceiveAsync(guestSocket));
        if (guest.ErrorMessage != "Fixture permission rejection") throw new InvalidOperationException("Socket rejection isn't visible to the guest.");
        Set(host, "_ws", null); Set(guest, "_ws", null); listener.Stop();
        Program.Log("PASS: isolated host/guest actual ClientWebSocket exchange: readiness snapshot, host pause request, guest command callback and rejected command feedback.");
    }
    private static WatchTogetherRoomSnapshot Snapshot() => new() { RoomId = "fixture-room", Code = "ABC123", Phase = "lobby", SelectionMode = "host_pick", SelectedContentId = "fixture-episode-1", SelfCanManageRoom = true, MemberCount = 2, Members = [new() { UserId = 1, ProfileId = "host", DisplayName = "Alex Host", Connected = true, IsHost = true, IsSelf = true }, new() { UserId = 2, ProfileId = "guest", DisplayName = "Riley Guest", Connected = true }] };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static async Task<string> ReceiveAsync(WebSocket socket) { var buffer = new byte[16384]; using var timeout = new CancellationTokenSource(5000); var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token); return Encoding.UTF8.GetString(buffer, 0, received.Count); }
    private static Task SendAsync(WebSocket socket, string body) => socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(body)), WebSocketMessageType.Text, true, CancellationToken.None);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static ContentDialog? OpenDialog(StackPanel parent) => VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child))).OfType<ContentDialog>().FirstOrDefault();
    private static void ClickPrimary(ContentDialog dialog) { var button = Descendants(dialog).OfType<Button>().Single(button => (string?)button.Content == dialog.PrimaryButtonText); ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); }
    private static async Task UntilAsync(Func<bool> ready) { for (var count = 0; count < 200 && !ready(); count++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Media interaction fixture did not settle."); }
    private static async Task LayoutAsync(FrameworkElement control)
    {
        Program.Log($"TRACE: {control.GetType().Name} Measure begin.");
        control.Measure(new Windows.Foundation.Size(900, 720));
        Program.Log($"TRACE: {control.GetType().Name} Arrange begin.");
        control.Arrange(new Windows.Foundation.Rect(0, 0, 900, 720));
        Program.Log($"TRACE: {control.GetType().Name} UpdateLayout begin.");
        control.UpdateLayout();
        Program.Log($"TRACE: {control.GetType().Name} layout synchronous complete, dispatcher settling.");
        await Task.Delay(100);
        Program.Log($"TRACE: {control.GetType().Name} dispatcher settled.");
    }
    private sealed class FixtureServices(IServiceProvider original, SiloApiClient client, PlaybackApi api, AuthService auth) : IServiceProvider, IDisposable
    {
        private readonly List<WatchTogetherRoomViewModel> _rooms = [];
        public object? GetService(Type type)
        {
            if (type == typeof(SiloApiClient)) return client;
            if (type == typeof(PlaybackApi)) return api;
            if (type == typeof(CatalogApi)) return new CatalogApi(client);
            if (type == typeof(HomeApi)) return new HomeApi(client);
            if (type == typeof(AuthService)) return auth;
            if (type == typeof(WatchTogetherJoinViewModel)) return new WatchTogetherJoinViewModel(api);
            if (type == typeof(WatchTogetherRoomViewModel)) { var room = new WatchTogetherRoomViewModel(api, client); _rooms.Add(room); return room; }
            return original.GetService(type);
        }
        public void Dispose() { foreach (var room in _rooms) room.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal SiloApiClient Client = null!; internal bool RevokeAfterStage, FailStage, FailSuggestion; internal int Stages, StartCalls, SuggestionCalls; internal string? StageBody, Policy, JoinCode, JoinToken; internal TaskCompletionSource? DelayMemberState;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            if (path.EndsWith("/capabilities")) return Response(new WatchTogetherCapabilities { State = "available", Allowed = true, StagedSelection = true, LobbyReady = true, MemberState = true, Picker = true, SelectionModeSwitch = true });
            if (path.Contains("/catalog/series/") && path.EndsWith("/episodes")) return Response(new { items = new[] { new Episode { ContentId = "fixture-episode-1", EpisodeNumber = 1, Title = "First" }, new Episode { ContentId = "fixture-episode-2", EpisodeNumber = 2, Title = "Second" } } });
            if (path.EndsWith("/staged-selection")) { Stages++; StageBody = body; if (FailStage) return Failure(); if (RevokeAfterStage) Client.SetProfile("fixture-revoked"); return Response(new WatchTogetherRoomResponse { Room = Snapshot(), RoomAccessToken = "fixture-proof" }); }
            if (path.EndsWith("/policy")) { using var document = JsonDocument.Parse(body!); Policy = document.RootElement.GetProperty("guest_control_policy").GetString(); return Response(new WatchTogetherRoomResponse { Room = Snapshot() }); }
            if (path.EndsWith("/playback/start")) { StartCalls++; return Response(new WatchTogetherRoomResponse { Room = Snapshot() }); }
            if (path.EndsWith("/join")) { using var document = JsonDocument.Parse(body!); if (document.RootElement.TryGetProperty("code", out var code)) JoinCode = code.GetString(); if (document.RootElement.TryGetProperty("join_token", out var token)) JoinToken = token.GetString(); return Response(new WatchTogetherRoomResponse { Room = Snapshot(), RoomAccessToken = "fixture-proof" }); }
            if (path.EndsWith("/member-state")) { if (DelayMemberState != null) await DelayMemberState.Task.WaitAsync(ct); return Response(new WatchTogetherMemberStateResponse { Members = Snapshot().Members, Items = [new() { ContentId = "fixture-episode-2", Members = [new() { UserId = 2, ProfileId = "guest", State = "unseen" }] }] }); }
            if (path.EndsWith("/suggestions")) { SuggestionCalls++; if (FailSuggestion) return Failure(); return Response(new { items = Array.Empty<WatchTogetherSuggestion>() }); }
            if (path.EndsWith("/picker")) return Response(new WatchTogetherPickerResponse());
            if (path.Contains("/home/layout")) return Response(new { sections = Array.Empty<object>() });
            if (path == "/api/v2/catalog") return Response(new { items = Array.Empty<object>() });
            if (path == "/api/v2/watch-together/rooms") return Response(new WatchTogetherRoomResponse { Room = Snapshot(), RoomAccessToken = "fixture-proof" });
            if (path.Contains("/catalog/items/")) return Response(new MediaItemDetail { ContentId = "fixture-episode-1", Title = "The Fixture Episode", Type = "episode" });
            if (path.Contains("/rooms/")) { var id = path.Split('/').Last(); if (id == "forbidden") return Failure(HttpStatusCode.Forbidden); if (id == "unavailable") return Failure(HttpStatusCode.BadGateway); var room = Snapshot(); if (id == "ended") room.Phase = "ended"; return Response(new WatchTogetherRoomResponse { Room = room }); }
            throw new InvalidOperationException("Unexpected isolated media route " + path);
        }
        private static HttpResponseMessage Response(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Failure(HttpStatusCode status = HttpStatusCode.Conflict) => new(status) { Content = new StringContent("{\"title\":\"Fixture rejection\",\"detail\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") };
    }
}
