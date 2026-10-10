using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class PartyPickerNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = (IServiceProvider)servicesField.GetValue(null)!;
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://party-picker-fixture.invalid"); client.SetProfile("fixture-host");
        servicesField.SetValue(null, new FixtureServices(original, client));
        WatchTogetherRoomPage? page = null;
        try
        {
            page = new WatchTogetherRoomPage { Width = 1280, Height = 720 };
            page.ViewModel.RoomId = "fixture-room"; page.ViewModel.RoomToken = "fixture-proof";
            page.ViewModel.GetType().GetProperty("Capabilities")!.SetValue(page.ViewModel,
                new WatchTogetherCapabilities { Allowed = true, State = "available", Picker = true, MemberState = true });
            page.ViewModel.Room = new WatchTogetherRoomSnapshot
                { Phase = "lobby", SelectionMode = "host_pick", SelfCanManageRoom = true, Members = Handler.Members.ToList() };
            Invoke(page, "UpdateRoomUi"); parent.Children.Add(page); page.UpdateLayout(); await Task.Delay(100);

            // A next-up value for this same series must only apply to its shared
            // shelf selection, never to an ordinary search/recent selection.
            var cache = (System.Collections.IDictionary)Field(page, "_nextUp")!;
            cache["resume-series"] = new WatchTogetherPickerNextUp { SeasonNumber = 1 };
            handler.HoldDetail("resume-series");
            var selection = Select(page, "resume-series");
            await handler.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var loading = Named<StackPanel>(page, "DrillDownLoading");
            if (loading.Visibility != Visibility.Visible || loading.Children.OfType<Border>().Count(border => border.Height == 80) != 5
                || Named<TextBlock>(page, "DrillDownEmptyText").Visibility != Visibility.Collapsed || handler.EpisodeRoutes.Count != 0)
                throw new InvalidOperationException("Fresh detail did not retain five 80px pending rows before choosing a season.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-party-picker-pending-1280.png");
            handler.ReleaseDetail.TrySetResult(); await selection;
            var items = Items(page);
            if (handler.EpisodeRoutes.Last() != ("resume-series", 6) || !items.Select(item => item.ContentId).SequenceEqual(new[] { "resume-series-first", "resume-series-pick" })
                || items[1].Title != "Second episode" || items[1].SeasonNumber != 6 || items[1].EpisodeNumber != 2
                || items[1].SeriesTitle != "resume-series" || items[1].Runtime != 27
                || Named<ItemsRepeater>(page, "DrillDownItems").Layout is not StackLayout { Orientation: Orientation.Vertical, Spacing: 4 })
                throw new InvalidOperationException("Resume season, file eligibility, original title/episode context or vertical row geometry differs from the picker contract.");
            if (!Named<TextBlock>(page, "DrillDownSubtitle").Text.Contains("Next up for 1 of you"))
                throw new InvalidOperationException("Room next-up does not claim each member at their earliest unfinished episode.");
            page.UpdateLayout(); await Task.Delay(100);
            if (Named<TextBlock>(page, "PickerSeriesTitle").Text != "resume-series"
                || Named<TextBlock>(page, "PickerSeriesMeta").Text != "2024 · TV-14 · Comedy, Drama · 3 seasons"
                || Named<TextBlock>(page, "PickerSeriesOverview").Text != "Fresh series overview"
                || Named<StackPanel>(page, "PickerSeasons").Children.Count != 3
                || Named<StackPanel>(page, "PickerSeasons").Orientation != Orientation.Vertical
                || Named<ScrollViewer>(page, "PickerEpisodeScroll").MaxHeight >= Named<Grid>(page, "DrillDownPanel").MaxHeight)
                throw new InvalidOperationException("Series metadata, persistent season rail or bounded episode scroll is missing.");
            var nextCaptions = (Dictionary<string, TextBlock>)Field(page, "_pickerNextCaptions")!;
            if (!nextCaptions.TryGetValue("resume-series-first", out var caption) || caption.Visibility != Visibility.Visible || caption.Text != "NEXT UP FOR 1 OF YOU")
                throw new InvalidOperationException("Next up was not presented on its actual episode row.");
            Invoke(page, "ShowSeriesEpisodePick", items[1]);
            await (Task)Invoke(page, "LoadCandidateMemberStateAsync", items[1])!;
            if (!Named<TextBlock>(page, "CandidateMemberState").Text.Contains("Ahead of Guest")
                || !Named<TextBlock>(page, "CandidateMemberState").Text.Contains("episode 1")
                || Named<TextBlock>(page, "CandidateMeta").Text != "resume-series · S6 E2")
                throw new InvalidOperationException("An earlier partially watched episode did not produce the correct identity-aware warning and candidate breadcrumb.");
            if (Named<Grid>(page, "DrillDownPanel").Visibility != Visibility.Visible || Named<Border>(page, "CandidateSpotlight").Visibility != Visibility.Collapsed
                || Named<Grid>(page, "PickerConfirmation").Visibility != Visibility.Visible
                || Named<TextBlock>(page, "PickerPickTitle").Text != "S6 E2 “Second episode”"
                || Named<TextBlock>(page, "PickerPickRisk").Text != "Guest hasn't seen E1. Heads up."
                || !Named<Button>(page, "PickerConfirmButton").IsEnabled)
                throw new InvalidOperationException("Series pick left the panel or omitted its inline confirmation and earlier-episode warning.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-party-picker-confirm-1280.png");
            page.Width = 500; page.UpdateLayout(); await Task.Delay(100);
            if (Named<StackPanel>(page, "PickerSeasons").Orientation != Orientation.Horizontal
                || Named<Border>(page, "PickerSeriesPosterBorder").Width != 64
                || Named<TextBlock>(page, "PickerDotsLegend").Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Narrow series picker did not use season chips, its small poster and compact legend.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-party-picker-confirm-500.png");
            page.Width = 1280; page.UpdateLayout();

            handler.AllWatched = true;
            await (Task)Invoke(page, "RefreshPickerMembersAsync")!;
            if (!ReferenceEquals(Named<Button>(page, "CandidatePlayBtn").Tag, items[1])
                || Named<Grid>(page, "PickerConfirmation").Visibility != Visibility.Visible
                || (bool)Field(page, "_candidateSpoilerRisk")!)
                throw new InvalidOperationException("Membership refresh cleared the selected candidate or retained obsolete earlier-episode risk.");
            handler.AllWatched = false;

            await Select(page, "resume-series", 1);
            if (handler.EpisodeRoutes.Last() != ("resume-series", 1) || Items(page).Any(item => item.SeasonNumber != 1))
                throw new InvalidOperationException("An explicit shared-shelf season did not win the fresh resume target.");
            await Select(page, "failed-detail");
            if (handler.EpisodeRoutes.Last() != ("failed-detail", 1))
                throw new InvalidOperationException("Optional detail failure did not fall back to the first regular season.");

            // Episode rows can be clicked before their earlier-episode batch
            // returns. The action stays disabled until that evidence arrives.
            handler.HoldMembers();
            selection = Select(page, "member-pending");
            await handler.MembersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            items = Items(page); Invoke(page, "ShowSeriesEpisodePick", items[1]);
            if (Named<Button>(page, "CandidatePlayBtn").IsEnabled || Named<Button>(page, "PickerConfirmButton").IsEnabled)
                throw new InvalidOperationException("Candidate confirmation became enabled while earlier-episode member state was pending.");
            handler.ReleaseMembers.TrySetResult(); await selection;
            await (Task)Invoke(page, "LoadCandidateMemberStateAsync", items[1])!;
            if (!Named<Button>(page, "CandidatePlayBtn").IsEnabled || !(bool)Field(page, "_candidateSpoilerRisk")!)
                throw new InvalidOperationException("Resolved member evidence did not restore confirmation and the earlier-episode warning.");

            await Select(page, "long-series");
            page.UpdateLayout(); await Task.Delay(250);
            Invoke(page, "UpdatePickerNextUp"); page.UpdateLayout(); await Task.Delay(100);
            if (Named<ScrollViewer>(page, "PickerEpisodeScroll").VerticalOffset <= 0
                || (string?)Field(page, "_pickerScrolledNextId") != "long-series-30")
                throw new InvalidOperationException("A long season did not center the room's next-up episode in the bounded inner scroll.");

            handler.HoldDetail("superseded");
            var stale = Select(page, "superseded"); await handler.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Select(page, "latest"); handler.ReleaseDetail.TrySetResult(); await stale;
            if (Items(page).Any(item => item.SeriesId != "latest"))
                throw new InvalidOperationException("A superseded series detail repainted the latest picker.");
            handler.HoldDetail("old-authority");
            stale = Select(page, "old-authority"); await handler.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            client.SetProfile("fixture-other-profile"); page.ViewModel.RoomId = "fixture-other-room"; page.ViewModel.RoomToken = "fixture-other-proof";
            await Select(page, "new-authority"); handler.ReleaseDetail.TrySetResult(); await stale;
            if (Items(page).Any(item => item.SeriesId != "new-authority"))
                throw new InvalidOperationException("Old profile/room evidence repainted the new picker authority.");
            await MediaParityNativeFixture.CaptureAsync(page, "media-party-picker-resolved-1280.png");
            Program.Log("PASS: WATCH_PARTY_PICKER_COMPLETED fresh pending/resume, explicit shelf season, optional detail fallback, playable vertical rows, identity-aware earlier risk/next-up, pending action, selected-member refresh and stale series/profile/room authority.");
        }
        finally
        {
            handler.ReleaseDetail.TrySetResult(); handler.ReleaseMembers.TrySetResult();
            if (page != null) { page.ViewModel.Dispose(); parent.Children.Remove(page); await Task.Delay(100); }
            servicesField.SetValue(null, original);
        }
    }

    private static Task Select(WatchTogetherRoomPage page, string series, int? season = null)
        => (Task)Invoke(page, "SelectBrowseItemAsync", new MediaItem { ContentId = series, Type = "series", Title = series }, season)!;
    private static List<MediaItem> Items(WatchTogetherRoomPage page)
        => ((IEnumerable<MediaItem>)Named<ItemsRepeater>(page, "DrillDownItems").ItemsSource).ToList();
    private static T Named<T>(WatchTogetherRoomPage page, string name) => (T)page.FindName(name);
    private static object? Field(object page, string name) => page.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page);
    private static object? Invoke(object page, string name, params object?[] args)
        => page.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, args);

    private sealed class FixtureServices(IServiceProvider original, SiloApiClient client) : IServiceProvider
    {
        private readonly PlaybackApi _api = new(client);
        public object? GetService(Type type) => type == typeof(PlaybackApi) ? _api
            : type == typeof(CatalogApi) ? new CatalogApi(client)
            : type == typeof(HomeApi) ? new HomeApi(client)
            : type == typeof(SiloApiClient) ? client
            : type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(_api, client)
            : original.GetService(type);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal static readonly WatchTogetherRoomMember[] Members =
            [new() { UserId = 1, ProfileId = "same-profile", DisplayName = "Host" }, new() { UserId = 2, ProfileId = "same-profile", DisplayName = "Guest" }];
        internal readonly List<(string Series, int Season)> EpisodeRoutes = [];
        internal TaskCompletionSource DetailStarted = NewSignal(), ReleaseDetail = NewSignal(), MembersStarted = NewSignal(), ReleaseMembers = NewSignal();
        internal bool AllWatched;
        private string? _heldDetail;
        private bool _heldMembers;
        internal void HoldDetail(string id) { _heldDetail = id; DetailStarted = NewSignal(); ReleaseDetail = NewSignal(); }
        internal void HoldMembers() { _heldMembers = true; MembersStarted = NewSignal(); ReleaseMembers = NewSignal(); }
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/catalog/items/"))
            {
                var id = path.Split('/').Last();
                if (id == _heldDetail)
                {
                    _heldDetail = null; var release = ReleaseDetail;
                    DetailStarted.TrySetResult(); await release.Task; // Deliberately allow late, cancellation-ignoring evidence.
                }
                if (id == "failed-detail") return new(HttpStatusCode.BadGateway)
                    { Content = new StringContent("{\"title\":\"Optional fixture detail unavailable\"}", Encoding.UTF8, "application/problem+json") };
                return Reply(new { content_id = id, type = "series", title = id, play_season_number = 6,
                    year = 2024, content_rating = "TV-14", genres = new[] { "Comedy", "Drama", "Other" }, overview = "Fresh series overview" });
            }
            if (path.EndsWith("/seasons")) return Reply(new { items = new[]
                { new { content_id = "specials", season_number = 0 }, new { content_id = "season-one", season_number = 1 }, new { content_id = "season-six", season_number = 6 } } });
            if (path.EndsWith("/episodes"))
            {
                var parts = path.Split('/'); var series = parts[5]; var season = int.Parse(parts[7]); EpisodeRoutes.Add((series, season));
                if (series == "long-series") return Reply(new { items = Enumerable.Range(1, 30).Select(number => new
                    { content_id = $"long-series-{number}", season_number = season, episode_number = number, title = $"Episode {number}", runtime = 25,
                        overview = "Two lines of episode description for the bounded scrolling fixture.", files = new[] { new { file_id = number } } }).ToArray() });
                return Reply(new { items = new[]
                {
                    new { content_id = series + "-unavailable", season_number = season, episode_number = 0, title = "Unavailable", runtime = 42, files = Array.Empty<object>() },
                    new { content_id = series + "-first", season_number = season, episode_number = 1, title = "First episode", runtime = 25, files = new object[] { new { file_id = 1 } } },
                    new { content_id = series + "-pick", season_number = season, episode_number = 2, title = "Second episode", runtime = 27, files = new object[] { new { file_id = 2 } } }
                } });
            }
            if (path.EndsWith("/member-state"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = body.RootElement.GetProperty("content_ids").EnumerateArray().Select(id => id.GetString()!).ToArray();
                if (_heldMembers) { _heldMembers = false; var release = ReleaseMembers; MembersStarted.TrySetResult(); await release.Task; }
                return Reply(new WatchTogetherMemberStateResponse
                {
                    Members = Members.ToList(), Items = ids.Select(id => new WatchTogetherItemMemberState
                    {
                        ContentId = id, Members = Members.Select(member => new WatchTogetherMemberWatchState
                        {
                            UserId = member.UserId, ProfileId = member.ProfileId,
                            State = AllWatched || (id.StartsWith("long-series-") && id != "long-series-30") || (id.EndsWith("-first") && member.UserId == 1) ? "watched" : "unseen"
                        }).ToList()
                    }).ToList()
                });
            }
            throw new InvalidOperationException("Unexpected isolated party picker route " + path);
        }
        private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    }
}
