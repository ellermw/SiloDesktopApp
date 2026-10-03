using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class WatchPartyActionsNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party-actions-fixture.invalid"); client.SetProfile("host");
        var api = new PlaybackApi(client); using var auth = new AuthService(client, new AuthApi(client));
        using var player = new PlayerService(api, new CatalogApi(client), auth, client, new SettingsService(Path.Combine(Program.ResultDirectory, "party-actions-settings")), new SettingsApi(client));
        using var room = new WatchTogetherRoomViewModel(api, client);
        var services = new Services(original, client, api, auth, player, room); field.SetValue(null, services);
        var frame = new Frame { Width = 900, Height = 720 }; parent.Children.Add(frame);
        Frame? historyFrame = null;
        try
        {
            room.RoomId = "fixture-room"; room.RoomToken = "fixture-proof"; room.Room = Snapshot(); room.ConnectionState = "connected";
            await room.LoadCapabilitiesAsync();
            if (!frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = "fixture-room", RoomAccessToken = "fixture-proof" })) throw new InvalidOperationException("Actual isolated room navigation failed.");
            var page = (WatchTogetherRoomPage)frame.Content; page.UpdateLayout(); await Task.Delay(150);
            Invoke(page, "UpdateRoomUi");
            void Press(string name) => Click((Button)page.FindName(name));
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "guest-mode")
            {
                var guest = Snapshot(); guest.SelfCanManageRoom = false; guest.SelfRole = "guest";
                guest.Members[0].IsSelf = false;
                guest.Members.Add(new() { UserId = 2, ProfileId = "guest", IsSelf = true, Connected = true, DisplayName = "Fixture guest" });
                room.Room = guest; Invoke(page, "UpdateRoomUi");
                frame.Width = 500; page.Width = 500; page.Height = 720;
                page.Measure(new Windows.Foundation.Size(500, 720)); page.Arrange(new Windows.Foundation.Rect(0, 0, 500, 720)); page.UpdateLayout();
                await Task.Delay(100);
                bool Visible(FrameworkElement element)
                {
                    for (DependencyObject? current = element; current != null && current != page; current = VisualTreeHelper.GetParent(current))
                        if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false;
                    return true;
                }
                var summary = (FrameworkElement)page.FindName("RoomHeaderSummary");
                if (!Descendants(summary).OfType<TextBlock>().Any(text => text.Text == "Host picks" && Visible(text)) ||
                    ((Button)page.FindName("ModeButton")).Visibility == Visibility.Visible)
                    throw new InvalidOperationException("Actual guest status strip loses the read-only current selection-mode chip.");
                await MediaParityNativeFixture.CaptureAsync(page, "media-party-guest-readonly-mode-500.png");
                Program.Log("PASS: WATCH_PARTY_GUEST_MODE_COMPLETED guest sees actual read-only mode without a mutable host trigger.");
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "room-sheet")
            {
                frame.Width = 500; page.Width = 500; page.Height = 720;
                page.Measure(new Windows.Foundation.Size(500, 720)); page.Arrange(new Windows.Foundation.Rect(0, 0, 500, 720)); page.UpdateLayout();
                await Task.Delay(100); ((StackPanel)page.FindName("ActivityList")).Children.Clear(); Press("RailSheetButton");
                ContentDialog? sheet = null;
                await UntilAsync(() => (sheet = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child))).OfType<ContentDialog>().FirstOrDefault()) != null);
                try
                {
                    var activity = Descendants(sheet!).OfType<Button>().FirstOrDefault(button => (button.Content as string) == "Activity" || Descendants(button).OfType<TextBlock>().Any(text => text.Text == "Activity"));
                    var people = Descendants(sheet!).OfType<Button>().FirstOrDefault(button => (button.Content as string)?.StartsWith("People") == true || Descendants(button).OfType<TextBlock>().Any(text => text.Text.StartsWith("People")));
                    if (activity == null || people == null) throw new InvalidOperationException("Actual narrow People/Activity sheet has no switchable tabs.");
                    bool Visible(FrameworkElement element) { for (DependencyObject? current = element; current != null && current != sheet; current = VisualTreeHelper.GetParent(current)) if (current is UIElement ui && ui.Visibility != Visibility.Visible) return false; return true; }
                    if (!Descendants(sheet!).OfType<TextBlock>().Any(text => text.Text.Contains("Fixture host") && Visible(text))) throw new InvalidOperationException("People tab does not initially show the actual room member.");
                    await MediaParityNativeFixture.CaptureAsync(sheet!, "media-party-sheet-people.png");
                    Click(activity); await Task.Delay(50);
                    if (Descendants(sheet!).OfType<TextBlock>().Any(text => text.Text.Contains("Fixture host") && Visible(text))) throw new InvalidOperationException("Activity tab retains the People panel instead of exclusively switching content.");
                    if (!Descendants(sheet!).OfType<TextBlock>().Any(text => text.Text == "Things people do in the room show up here." && Visible(text))) throw new InvalidOperationException("Actual empty activity sheet loses the current official empty state.");
                    await MediaParityNativeFixture.CaptureAsync(sheet!, "media-party-sheet-activity.png");
                    Click(people); await Task.Delay(50);
                    if (!Descendants(sheet!).OfType<TextBlock>().Any(text => text.Text.Contains("Fixture host") && Visible(text))) throw new InvalidOperationException("People tab cannot recover its members after Activity.");
                    Program.Log("PASS: WATCH_PARTY_ROOM_SHEET_COMPLETED actual narrow tab invocation, exclusive members/activity/empty-state and recovery.");
                }
                finally { sheet!.Hide(); await Task.Delay(100); }
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_MEDIA_ACTION_CASE") == "start-pending")
            {
                handler.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    Press("StartStagedButton"); await UntilAsync(() => room.IsBusy); await Task.Delay(100);
                    if (((Button)page.FindName("StartStagedButton")).IsEnabled || ((Button)page.FindName("ChangeStagedButton")).IsEnabled)
                        throw new InvalidOperationException("Actual staged Start and Change remain enabled while the start mutation is pending.");
                    Program.Log("PASS: WATCH_PARTY_START_PENDING_COMPLETED actual Start and Change controls disabled during pending mutation.");
                }
                finally { handler.Delay.TrySetResult(); await UntilAsync(() => !room.IsBusy); }
                return;
            }
            async Task ChooseModeAsync()
            {
                var before = handler.ModeCalls;
                Press("ModeButton");
                MenuFlyoutItem? current = null, other = null;
                for (var attempt = 0; attempt < 40 && other == null && handler.ModeCalls == before; attempt++)
                {
                    var choices = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().ToArray();
                    current = choices.FirstOrDefault(choice => choice.Text == (room.IsVoteMode ? "Everyone votes" : "Host picks"));
                    other = choices.FirstOrDefault(choice => choice.Text == (room.IsVoteMode ? "Host picks" : "Everyone votes"));
                    if (other == null) await Task.Delay(25);
                }
                if (handler.ModeCalls != before || current == null || other == null || current.IsEnabled || !other.IsEnabled)
                    throw new InvalidOperationException("Actual mode chip must open both current official choices with its current choice disabled before any mutation.");
                var menuRoot = VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Single(popup => Descendants(popup.Child).Contains(other)).Child;
                await MediaParityNativeFixture.CaptureAsync((FrameworkElement)menuRoot, "media-party-mode-menu.png");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(other).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(parent.XamlRoot).Any(popup => Descendants(popup.Child).Contains(other)));
            }
            handler.Reject = true; await ChooseModeAsync(); await UntilAsync(() => handler.ModeCalls == 1 && !room.IsBusy);
            if (room.IsVoteMode || room.ErrorMessage?.Contains("Could not change selection mode") != true) throw new InvalidOperationException("Rejected actual mode action changes the lobby selection or loses feedback.");
            handler.Reject = false; handler.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously); await ChooseModeAsync(); await UntilAsync(() => room.IsBusy);
            Press("ModeButton"); await Task.Delay(75);
            if (handler.ModeCalls != 2) throw new InvalidOperationException("Actual mode action duplicates its pending mutation.");
            handler.Delay.SetResult(); await UntilAsync(() => room.IsVoteMode && !room.IsBusy);
            if (handler.Mode != "vote") throw new InvalidOperationException("Actual mode button submits the wrong mode.");
            handler.Delay = null; handler.Capabilities = false; await room.LoadCapabilitiesAsync(); Invoke(page, "UpdateRoomUi");
            if (((Button)page.FindName("ModeButton")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Unavailable mode capability leaves a mutation affordance visible.");
            await room.ChangeSelectionModeAsync("host_pick"); if (handler.ModeCalls != 2) throw new InvalidOperationException("Unavailable capability permits a mode mutation.");
            handler.Capabilities = true; await room.LoadCapabilitiesAsync(); Invoke(page, "UpdateRoomUi");
            handler.Reject = true; Press("StartStagedButton"); await UntilAsync(() => handler.Starts == 1 && !room.IsBusy && ((Button)page.FindName("StartStagedButton")).IsEnabled);
            if (room.Room?.Phase != "lobby" || room.Room.SelectedContentId != "fixture-selected" || room.ErrorMessage?.Contains("Could not start playback") != true) throw new InvalidOperationException("Rejected actual Start discards the staged selection or prevents recovery.");
            handler.Reject = false; handler.Delay = new(TaskCreationOptions.RunContinuationsAsynchronously); Press("StartStagedButton"); await UntilAsync(() => room.IsBusy);
            await Task.Delay(75);
            if (((Button)page.FindName("StartStagedButton")).IsEnabled || ((Button)page.FindName("ChangeStagedButton")).IsEnabled) throw new InvalidOperationException("Actual staged Start and Change don't disable while pending.");
            await room.StartStagedAsync(); if (handler.Starts != 2) throw new InvalidOperationException("Pending actual Start is submitted twice.");
            handler.Delay.SetResult(); await UntilAsync(() => room.Room?.Phase == "playing" && !room.IsBusy);
            room.Room = Snapshot(); room.AcknowledgePlaybackStart(); handler.Delay = null;
            frame.Navigate(typeof(Page)); await Task.Delay(100);
            if (!ReferenceEquals(services.Coordinator.ActiveRoom, room) || room.ConnectionState != "connected") throw new InvalidOperationException("Ordinary native navigation clears live room authority.");
            frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = "fixture-room", RoomAccessToken = "fixture-proof" });
            page = (WatchTogetherRoomPage)frame.Content; await Task.Delay(100); Invoke(page, "UpdateRoomUi");
            frame.Navigating += (_, e) => { if (e.SourcePageType == typeof(WatchTogetherJoinPage)) e.Cancel = true; };
            Click((Button)page.FindName("LeaveButton")); await UntilAsync(() => services.Coordinator.ActiveRoom == null);
            if (room.AttachedSessionId != null) throw new InvalidOperationException("Explicit Leave retains the room playback attachment.");

            using var replacement = new WatchTogetherRoomViewModel(api, client) { RoomId = "replacement", RoomToken = "replacement-proof", Room = Snapshot(), ConnectionState = "connected" };
            services.Coordinator.SetActiveRoom(replacement); auth.SelectProfile("other-profile");
            try { await UntilAsync(() => services.Coordinator.ActiveRoom == null); }
            catch (TimeoutException) { throw new InvalidOperationException("Actual SelectProfile retains the previous room authority without a manufactured UserChanged event."); }

            var historyDirectory = Path.Combine(Program.ResultDirectory, "party-actions-history");
            var store = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.RecentPartyStore")!;
            await (Task)store.GetMethod("RememberAsync")!.Invoke(null, [client, auth, new WatchTogetherRoomResponse { Room = Snapshot(), RoomAccessToken = "history-proof" }, historyDirectory])!;
            var defaultPath = (string)store.GetMethod("PathFor", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [client, auth, null])!;
            if (File.Exists(defaultPath)) throw new InvalidOperationException("Recent navigation fixture requires absent default fake-authority history; it may not inspect stored records.");
            // The canceled Leave navigation prevents a hub construction. Remove
            // that temporary cancellation handler by using a separate real Frame.
            historyFrame = new Frame { Width = 900, Height = 720 }; parent.Children.Remove(frame); parent.Children.Add(historyFrame);
            historyFrame.Navigate(typeof(WatchTogetherJoinPage)); var hub = (WatchTogetherJoinPage)historyFrame.Content;
            hub.GetType().GetProperty("RecentPartyDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(hub, historyDirectory);
            await Task.Delay(100);
            await (Task)Invoke(hub, "LoadRecentRoomsAsync", [null])!;
            Type? destination = null; WatchTogetherRoomNavigationArgs? args = null;
            historyFrame.Navigating += (_, e) => { if (e.SourcePageType == typeof(WatchTogetherRoomPage)) { destination = e.SourcePageType; args = e.Parameter as WatchTogetherRoomNavigationArgs; e.Cancel = true; } };
            var rejoin = ((StackPanel)hub.FindName("RecentRoomsList")).Children.OfType<Button>().Single(b => b.IsEnabled && ((string)b.Content).Contains("Rejoin"));
            Click(rejoin); await UntilAsync(() => destination != null);
            if (args?.RoomId != "fixture-room" || args.RoomAccessToken != "history-proof") throw new InvalidOperationException("Actual Recent Rejoin loses persisted room proof or destination.");
            historyFrame.Content = null; parent.Children.Remove(historyFrame);
            if (File.Exists(defaultPath)) throw new InvalidOperationException("Recent navigation created default fake-authority history outside the isolated directory.");
            Program.Log("PASS: WATCH_PARTY_ACTIONS_COMPLETED actual mode rejection/retry/pending/capability gate, staged Start rejection/retry/deduplication, ordinary navigation retention, Leave/profile revocation and Recent Rejoin navigation proof.");
        }
        finally { services.Coordinator.ClearActiveRoom(); frame.Content = null; if (historyFrame != null) { historyFrame.Content = null; parent.Children.Remove(historyFrame); } parent.Children.Remove(frame); await Task.Delay(150); field.SetValue(null, original); }
    }
    private static WatchTogetherRoomSnapshot Snapshot() => new() { RoomId = "fixture-room", Code = "ABC123", Phase = "lobby", SelectionMode = "host_pick", GuestControlPolicy = "host_only", SelectedContentId = "fixture-selected", SelfCanManageRoom = true, Members = [new() { UserId = 1, ProfileId = "host", IsHost = true, IsSelf = true, Connected = true, DisplayName = "Fixture host" }] };
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; } }
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task UntilAsync(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(25); if (!ready()) throw new TimeoutException("Party action did not settle."); }
    private sealed class Services(IServiceProvider original, SiloApiClient client, PlaybackApi api, AuthService auth, PlayerService player, WatchTogetherRoomViewModel room) : IServiceProvider
    {
        private WatchTogetherCoordinator? _coordinator;
        internal WatchTogetherCoordinator Coordinator => _coordinator ??= new(player);
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(PlaybackApi) ? api : type == typeof(CatalogApi) ? new CatalogApi(client) : type == typeof(HomeApi) ? new HomeApi(client) : type == typeof(AuthService) ? auth : type == typeof(PlayerService) ? player : type == typeof(WatchTogetherCoordinator) ? Coordinator : type == typeof(WatchTogetherRoomViewModel) ? room : type == typeof(WatchTogetherJoinViewModel) ? new WatchTogetherJoinViewModel(api) : original.GetService(type);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal bool Reject, Capabilities = true; internal int ModeCalls, Starts; internal string? Mode; internal TaskCompletionSource? Delay;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Json(new WatchTogetherCapabilities { State = "available", Allowed = true, StagedSelection = true, SelectionModeSwitch = Capabilities });
            if (path.EndsWith("/selection-mode")) { ModeCalls++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Mode = body.RootElement.GetProperty("selection_mode").GetString(); }
            else if (path.EndsWith("/playback/start")) Starts++;
            else if (path.Contains("/catalog/items/")) return Json(new { content_id = "fixture-selected", title = "Fixture selected", type = "movie" });
            else if (path.EndsWith("/member-state")) return Json(new WatchTogetherMemberStateResponse());
            else if (path.EndsWith("/picker")) return Json(new WatchTogetherPickerResponse());
            else if (path == "/api/v2/home/layout") return Json(new { sections = Array.Empty<object>() });
            else if (path == "/api/v2/catalog") return Json(new { items = Array.Empty<object>() });
            else if (request.Method == HttpMethod.Get && path.Contains("/rooms/")) return Json(new WatchTogetherRoomResponse { Room = Snapshot() });
            else throw new InvalidOperationException("Unexpected party action route " + path);
            if (Delay != null) await Delay.Task.WaitAsync(ct);
            if (Reject) return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"title\":\"Fixture rejection\"}", Encoding.UTF8, "application/problem+json") };
            var snapshot = Snapshot(); if (path.EndsWith("/selection-mode")) snapshot.SelectionMode = Mode!; else snapshot.Phase = "playing";
            return Json(new WatchTogetherRoomResponse { Room = snapshot });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    }
}
