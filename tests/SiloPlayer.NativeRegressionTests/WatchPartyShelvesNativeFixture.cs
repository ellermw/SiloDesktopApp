using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class WatchPartyShelvesNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)field.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party-shelves-fixture.invalid"); client.SetProfile("fixture-host");
        using var services = new FixtureServices(original, client);
        field.SetValue(null, services);
        WatchTogetherRoomPage? room = null;
        try
        {
            room = new WatchTogetherRoomPage { Width = 1280, Height = 720 };
            room.ViewModel.RoomId = "fixture-room"; room.ViewModel.RoomToken = "fixture-proof";
            room.ViewModel.GetType().GetProperty("Capabilities")!.SetValue(room.ViewModel, new WatchTogetherCapabilities { Allowed = true, State = "available", Picker = true, MemberState = true });
            room.ViewModel.Room = new WatchTogetherRoomSnapshot { Phase = "lobby", SelectionMode = "host_pick", SelfCanManageRoom = true };
            Invoke(room, "UpdateRoomUi"); parent.Children.Add(room); room.UpdateLayout(); await Task.Delay(100);
            await (Task)Invoke(room, "RunHostSearchAsync")!;
            var shelves = ((StackPanel)room.FindName("BrowseShelfRows")).Children.OfType<StackPanel>().Select(shelf => ((TextBlock)shelf.Children[0]).Text).ToArray();
            if (!shelves.Contains("Continue together") || !shelves.Contains("Room watchlists") || !shelves.Contains("Recently added series") || shelves.Contains("Recently added movies"))
                throw new InvalidOperationException("Shared picker or independently successful recent series shelf is lost after movie failure.");
            if (!shelves.Contains("Discovery 1") || shelves.Contains("Discovery 2") || !shelves.Contains("Discovery 3") || !shelves.Contains("Discovery 4") || shelves.Contains("Discovery 5") || handler.PersonalRequests != 0)
                throw new InvalidOperationException("Discovery shelves do not exclude personal rows, cap four, and preserve successful rows after one failure.");
            if (!((TextBlock)room.FindName("SearchEmptyText")).Text.Contains("Some shelves are unavailable"))
                throw new InvalidOperationException("Partial shelf failure has no retry feedback.");
            var nextUp = (System.Collections.IDictionary)typeof(WatchTogetherRoomPage).GetField("_nextUp", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(room)!;
            if (nextUp["continue-series"] is not WatchTogetherPickerNextUp { SeasonNumber: 3 }) throw new InvalidOperationException("Shared next-up season context isn't retained for drilldown.");
            await MediaParityNativeFixture.CaptureAsync(room, "media-party-shelves-partial-failure.png");
            var initialBatches = handler.MemberBatchSizes.Count;
            var revision = (long)typeof(WatchTogetherRoomPage).GetField("_browseRevision", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(room)!;
            await (Task)Invoke(room, "LoadMemberStatesAsync", Enumerable.Range(0, 451).Select(index => "batch-" + index), revision, CancellationToken.None)!;
            if (!handler.MemberBatchSizes.Skip(initialBatches).SequenceEqual(new[] { 200, 200, 51 }))
                throw new InvalidOperationException("Visible member-state classification isn't bounded to batches of 200.");

            var box = (TextBox)room.FindName("HostSearchBox");
            typeof(WatchTogetherRoomPage).GetField("_suppressSearchTextChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(room, true);
            box.Text = "first";
            var first = (Task)Invoke(room, "RunHostSearchAsync")!; await handler.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            box.Text = "latest"; await (Task)Invoke(room, "RunHostSearchAsync")!; await first;
            var results = ((ItemsRepeater)room.FindName("HostSearchResults")).ItemsSource as IEnumerable<MediaItem>;
            if (results?.Single().ContentId != "latest-result" || !handler.FirstSearchCancelled || handler.SearchLimits.Any(limit => limit != "30"))
                throw new InvalidOperationException("Superseded search isn't cancelled, results are stale, or search exceeds its result bound.");
            Program.Log("PASS: WATCH_PARTY_SHELVES_COMPLETED actual shared progress/watchlists, next-up context, capped nonpersonal discovery, partial failure retention/retry, 200-item member batching and cancelled newest-only search.");
        }
        finally
        {
            if (room != null)
            {
                room.ViewModel.Dispose(); parent.Children.Remove(room);
                // WinUI queues recycled card Unloaded callbacks. Keep the
                // service authority they subscribed through until it drains.
                await Task.Delay(100);
            }
            field.SetValue(null, original);
        }
    }
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private sealed class FixtureServices(IServiceProvider original, SiloApiClient client) : IServiceProvider, IDisposable
    {
        private readonly PlaybackApi _api = new(client);
        public object? GetService(Type type) => type == typeof(PlaybackApi) ? _api : type == typeof(CatalogApi) ? new CatalogApi(client) : type == typeof(HomeApi) ? new HomeApi(client) : type == typeof(SiloApiClient) ? client : type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(_api, client) : original.GetService(type);
        public void Dispose() { }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<int> MemberBatchSizes = [];
        internal readonly List<string> SearchLimits = [];
        internal readonly TaskCompletionSource FirstSearchStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FirstSearchCancelled; internal int PersonalRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!; var path = uri.AbsolutePath;
            if (path.EndsWith("/picker")) return Reply(new WatchTogetherPickerResponse
            {
                ContinueTogether = [new() { Item = Item("continue-series", "series"), NextUp = new() { ContentId = "continue-episode", SeasonNumber = 3, EpisodeNumber = 4 } }],
                WatchlistUnion = [new() { Item = Item("watchlist-movie", "movie") }]
            });
            if (path.EndsWith("/member-state"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = body.RootElement.GetProperty("content_ids").EnumerateArray().Select(id => id.GetString()!).ToArray(); MemberBatchSizes.Add(ids.Length);
                return Reply(new WatchTogetherMemberStateResponse { Items = ids.Select(id => new WatchTogetherItemMemberState { ContentId = id }).ToList() });
            }
            if (path.EndsWith("/home/layout")) return Reply(new HomeLayoutResponse { Sections = new[] { new HomeSection { Id = "personal", SectionType = "recommended_for_you" } }.Concat(Enumerable.Range(1, 5).Select(index => new HomeSection { Id = "discovery-" + index, SectionType = "popular", Title = "Discovery " + index })).ToList() });
            if (path.Contains("/home/sections/"))
            {
                if (path.Contains("personal")) PersonalRequests++;
                var index = path.Split('/')[5].Split('-').Last();
                if (index == "2") return Failure();
                return Reply(new { title = "Discovery " + index, items = new[] { Item("discovery-movie-" + index, "movie") } });
            }
            if (path == "/api/v2/catalog")
            {
                var query = new Windows.Foundation.WwwFormUrlDecoder(uri.Query);
                string? Value(string name) => query.FirstOrDefault(pair => pair.Name == name)?.Value;
                if (Value("q") is { } search)
                {
                    SearchLimits.Add(Value("limit")!);
                    if (search == "first")
                    {
                        FirstSearchStarted.TrySetResult();
                        try { await Task.Delay(Timeout.Infinite, ct); }
                        catch (OperationCanceledException) { FirstSearchCancelled = true; throw; }
                    }
                    return Reply(new { items = new[] { Item("latest-result", "movie") } });
                }
                if (Value("type") == "movie") return Failure();
                return Reply(new { items = new[] { Item("recent-series", "series") } });
            }
            throw new InvalidOperationException("Unexpected isolated shelves route " + path);
        }
        private static MediaItem Item(string id, string type) => new() { ContentId = id, Type = type, Title = id };
        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Failure() => new(HttpStatusCode.BadGateway) { Content = new StringContent("{\"title\":\"Fixture shelf unavailable\"}", Encoding.UTF8, "application/problem+json") };
    }
}
