using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class BoxSetRoomNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var original = SiloPlayer.App.Services;
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://boxset-room.invalid"); client.SetProfile("fixture");
        var catalog = new CatalogApi(client); var playback = new PlaybackApi(client);
        Type? routeType = null;
        var navigation = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (type, _) => { routeType = type; return true; } };
        slot.SetValue(null, new Services(original, client, catalog, playback, navigation));
        var coordinator = original.GetRequiredService<WatchTogetherCoordinator>(); var player = original.GetRequiredService<PlayerService>();
        try
        {
            var page = new BoxSetPage(catalog, client) { Width = 800, Height = 600 }; parent.Children.Add(page);
            await page.ViewModel.LoadAsync("7", "12"); await Task.Delay(70);
            foreach (var mode in new[] { "host-lobby", "guest-lobby", "host-playing", "guest-playing", "host-vote" })
            {
                coordinator.ClearActiveRoom();
                var room = new WatchTogetherRoomViewModel(playback, client) { RoomId = "fixture-room", RoomToken = "fixture-proof", Room = new() { RoomId = "fixture-room", Phase = mode.EndsWith("playing") ? "playing" : "lobby", SelectedContentId = "room-existing", SelfCanManageRoom = mode.StartsWith("host"), SelectionMode = mode == "host-vote" ? "vote" : "host_pick" } };
                coordinator.SetActiveRoom(room); routeType = null; var stages = wire.Stages; var suggestions = wire.Suggestions; var currentContent = player.ContentId;
                var button = Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay");
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                for (var i = 0; i < 100 && (wire.Stages == stages && wire.Suggestions == suggestions || !button.IsEnabled); i++) await Task.Delay(10);
                Check(mode == "host-lobby" ? wire.Stages == stages + 1 && wire.Suggestions == suggestions : wire.Suggestions == suggestions + 1 && wire.Stages == stages, mode + " enters the room stage/suggestion route");
                Check(ReferenceEquals(coordinator.ActiveRoom, room) && player.ContentId == currentContent && wire.LocalStarts == 0, mode + " preserves active room and local playback authority");
                Check(routeType == typeof(WatchTogetherRoomPage), mode + " returns to the room destination");
            }
            foreach (var stage in new[] { true, false })
            {
                coordinator.ClearActiveRoom(); routeType = null;
                var room = new WatchTogetherRoomViewModel(playback, client) { RoomId = "fixture-room", RoomToken = "fixture-proof", Room = new() { RoomId = "fixture-room", Phase = "lobby", SelectedContentId = "before-access", SelfCanManageRoom = stage } };
                coordinator.SetActiveRoom(room); var snapshot = room.Room;
                wire.HoldStage = stage; wire.HoldSuggestion = !stage;
                wire.StageStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.ReleaseStage = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.SuggestionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.ReleaseSuggestion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var button = Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay");
                var stages = wire.Stages; var suggestions = wire.Suggestions;
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                try
                {
                    await (stage ? wire.StageStarted.Task : wire.SuggestionStarted.Task).WaitAsync(TimeSpan.FromSeconds(2));
                    client.InvalidateAccessContext();
                    await page.ViewModel.RefreshAsync();
                    Check(!button.IsEnabled && page.ViewModel.CanPlay, "access refresh retains the pending room intent");
                    wire.ReleaseStage.TrySetResult(); wire.ReleaseSuggestion.TrySetResult();
                    for (var i = 0; i < 100 && !button.IsEnabled; i++) await Task.Delay(10);
                    Check(button.IsEnabled && routeType == typeof(WatchTogetherRoomPage), "accepted stage/suggestion reaches room after access revision");
                    Check(ReferenceEquals(coordinator.ActiveRoom, room) && (stage ? room.Room?.SelectedContentId == "second" : ReferenceEquals(room.Room, snapshot)), "access revision publishes stage without replacing the room or suggestion snapshot");
                    Check(wire.Stages == stages + (stage ? 1 : 0) && wire.Suggestions == suggestions + (stage ? 0 : 1), "access revision never replays accepted room mutation");
                }
                finally { wire.HoldStage = wire.HoldSuggestion = false; wire.ReleaseStage.TrySetResult(); wire.ReleaseSuggestion.TrySetResult(); }
            }
            coordinator.ClearActiveRoom();
            var pendingRoom = new WatchTogetherRoomViewModel(playback, client) { RoomId = "fixture-room", RoomToken = "fixture-proof", Room = new() { RoomId = "fixture-room", Phase = "lobby", SelectedContentId = "before-pending", SelfCanManageRoom = true } };
            coordinator.SetActiveRoom(pendingRoom); var originalSnapshot = pendingRoom.Room; routeType = null;
            wire.HoldStage = true; wire.StageStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.ReleaseStage = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingButton = Descendants(page).OfType<Button>().Single(x => x.Name == "BoxSetPlay");
            ((IInvokeProvider)new ButtonAutomationPeer(pendingButton).GetPattern(PatternInterface.Invoke)).Invoke();
            await wire.StageStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var replacement = new WatchTogetherRoomViewModel(playback, client) { RoomId = "replacement", RoomToken = "replacement-proof", Room = new() { RoomId = "replacement", Phase = "lobby", SelectedContentId = "replacement-selection", SelfCanManageRoom = true } };
            coordinator.SetActiveRoom(replacement); wire.HoldStage = false; wire.ReleaseStage.TrySetResult();
            for (var i = 0; i < 100 && !pendingButton.IsEnabled; i++) await Task.Delay(10);
            Check(ReferenceEquals(coordinator.ActiveRoom, replacement) && ReferenceEquals(pendingRoom.Room, originalSnapshot) && routeType == null, "late stage cannot mutate/navigate a replaced room");
            coordinator.ClearActiveRoom();
            var helper = typeof(BoxSetPage).Assembly.GetType("SiloPlayer.Services.MoviePlaybackEntry")!.GetMethod("PlayAsync")!;
            var calls = 0; Func<string, int, ApiRequestContext, CancellationToken, Task> local = (id, library, owner, ct) => { calls++; Check(id == "second" && library == 12 && client.IsCurrentContext(owner) && !ct.IsCancellationRequested, "standalone normal movie owner/library"); return Task.CompletedTask; };
            var movie = new MediaItem { ContentId = "second", Type = "movie", Title = "Second" };
            await (Task)helper.Invoke(null, [movie, 12, client.CaptureContext(), CancellationToken.None, (Func<bool>)(() => true), local])!;
            Check(calls == 1, "standalone delegates to ordinary movie playback");
            async Task DeclinedAsync(MediaItem target, Func<bool> current, string message)
            {
                try
                {
                    await (Task)helper.Invoke(null, [target, 12, client.CaptureContext(), CancellationToken.None, current, local])!;
                }
                catch (InvalidOperationException ex) { Check(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), "declined entry gives actionable guidance"); return; }
                throw new InvalidOperationException("Box-set room: declined entry returned success without feedback.");
            }
            await DeclinedAsync(movie, () => false, "Reopen");
            await DeclinedAsync(new MediaItem { Type = "box_set", ContentId = "box_set:7" }, () => true, "Reopen");
            Check(calls == 1, "retired or invalid movie entry cannot start standalone playback");
            coordinator.SetActiveRoom(new WatchTogetherRoomViewModel(playback, client) { Room = new() { Phase = "lobby" } });
            var unreadyStages = wire.Stages; var unreadySuggestions = wire.Suggestions;
            await DeclinedAsync(movie, () => true, "Open the room");
            Check(wire.Stages == unreadyStages && wire.Suggestions == unreadySuggestions && calls == 1, "unready room declines without dispatching playback");
            Program.Log("PASS: actual box-set primary action stages host lobby, suggests guest/playing/vote intents, survives access revisions without replay, and reports declined entries; late stage preserves replaced room.");
        }
        finally { coordinator.ClearActiveRoom(); parent.Children.Clear(); await Task.Delay(100); slot.SetValue(null, original); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;} }
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException("Box-set room: " + message); }
    private sealed class Services(IServiceProvider original, SiloApiClient client, CatalogApi catalog, PlaybackApi playback, NavigationService navigation) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(SiloApiClient) ? client : type == typeof(CatalogApi) ? catalog : type == typeof(PlaybackApi) ? playback : type == typeof(NavigationService) ? navigation : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal int Stages, Suggestions, LocalStarts;
        internal bool HoldStage, HoldSuggestion;
        internal TaskCompletionSource StageStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseStage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SuggestionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseSuggestion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath; object body=new { items=Array.Empty<object>() };
            if(path.EndsWith("/catalog/search/capabilities"))body=new { box_sets=true };
            else if(path.EndsWith("/user/libraries"))body=new { items=new[] { new { id=12,box_sets_enabled=true } } };
            else if(path.Contains("/catalog/box-sets/"))body=new { id="7",library_id="12",title="Fixture set",movie_count=2,played_count=1,items=new object[] { new { content_id="first",type="movie",title="First",user_state=new { played=true } },new { content_id="second",type="movie",title="Second",user_state=new { played=false } } } };
            else if(path.EndsWith("/staged-selection"))
            {
                Stages++; var data=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                Check(data.GetProperty("content_id").GetString()=="second" && data.GetProperty("library_id").GetString()=="12", "stage normal movie with response library");
                StageStarted.TrySetResult(); if(HoldStage) await ReleaseStage.Task;
                body=new { room=new { room_id="fixture-room",phase="lobby",selected_content_id="second",self_can_manage_room=true } };
            }
            else if(path.EndsWith("/suggestions") && request.Method==HttpMethod.Post)
            {
                Suggestions++; var data=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                Check(data.GetProperty("content_id").GetString()=="second" && data.GetProperty("content_type").GetString()=="movie", "suggest normal movie rather than aggregate");
                SuggestionStarted.TrySetResult(); if(HoldSuggestion) await ReleaseSuggestion.Task;
            }
            else if(path.Contains("/playback/"))LocalStarts++;
            return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(body)) };
        }
    }
}
