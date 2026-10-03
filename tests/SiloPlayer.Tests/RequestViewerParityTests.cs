using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class RequestViewerParityTests
{
    [Fact]
    public async Task DetailKeepsSeasonsViewerStateAndDownloadWithoutPrivateFields()
    {
        using var wire = new Wire("""
        {"media_type":"series","tmdb_id":42,"title":"Example","availability":"available","in_watchlist":true,
         "seasons":[{"season_number":1,"availability":"available","episode_count":8,"requested":false},
                    {"season_number":2,"availability":"partial","episode_count":10,"requested":true}],
         "request":{"requestable":false,"reason":"already_requested","requested_by_viewer":false,"following":true,
         "status":"downloading","state":"partially_available","download":{"phase":"import_blocked","percent":100,"downloads":1}}}
        """);
        var item = await new RequestsApi(wire.Client).GetDetailAsync("series", 42);
        Assert.True(item.InWatchlist);
        Assert.Equal(2, item.Seasons!.Count);
        Assert.Equal("partially_available", item.Request.State);
        Assert.True(item.Request.Following);
        Assert.Equal("Waiting for import", RequestViewerPolicy.DownloadLabel(item.Request.Download));
    }

    [Theory]
    [InlineData("pending", "active", 0, true)]
    [InlineData("approved", "active", 0, true)]
    [InlineData("approved", "active", 1, false)]
    [InlineData("queued", "active", 0, false)]
    [InlineData("pending", "cancelled", 0, false)]
    public void CancelMatchesServerWithdrawalRule(string status, string outcome, int targets, bool expected)
        => Assert.Equal(expected, RequestViewerPolicy.CanCancel(new MediaRequest
        { Status = status, Outcome = outcome, Targets = Enumerable.Range(0, targets).Select(_ => new RequestTarget()).ToList() }));

    [Fact]
    public void AiredMissingSeasonsDefaultWhileAvailableAndAlreadyRequestedStayDisabled()
    {
        var seasons = new[]
        {
            new RequestMediaSeason { SeasonNumber = 1, AirDate = "2025-01-01", Availability = "available", EpisodeCount = 8 },
            new RequestMediaSeason { SeasonNumber = 2, AirDate = "2025-02-01", Availability = "partial", EpisodeCount = 8 },
            new RequestMediaSeason { SeasonNumber = 3, AirDate = "2027-01-01", Availability = "missing" },
            new RequestMediaSeason { SeasonNumber = 4, Availability = "missing", Requested = true },
        };
        Assert.Equal(new[] { 2 }, RequestViewerPolicy.DefaultSeasons(seasons, new DateOnly(2026, 9, 30)));
        Assert.True(RequestViewerPolicy.SeasonRequestable(seasons[2]));
        Assert.False(RequestViewerPolicy.SeasonRequestable(seasons[3]));
        Assert.Equal("Cancelled", RequestViewerPolicy.Label("completed", "cancelled", null));
        Assert.Equal("Partially available", RequestViewerPolicy.Label("completed", "active", "partially_available"));
    }

    [Fact]
    public void OptedOutProfileCanReenableAutomaticRequestsAndUndatedEmptySeasonsNeverDefault()
    {
        var features = new RequestFeatureStatus { RequestsEnabled = true, Allowed = true, WatchlistTitlesSupported = true, WatchlistRequests = false };
        Assert.True(RequestViewerPolicy.ShowAutoRequestControl(features, false));
        Assert.False(RequestViewerPolicy.ShowAutoRequestControl(features, true));
        features.Allowed = false;
        Assert.False(RequestViewerPolicy.ShowAutoRequestControl(features, false));
        Assert.Empty(RequestViewerPolicy.DefaultSeasons([new() { SeasonNumber = 2, AirDate = "2025-01-01", EpisodeCount = 0 }], new(2026, 9, 30)));
    }

    [Fact]
    public void DownloadEstimatesRequireFreshObservationAndUnknownPhasesDontInventProgress()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var download = new RequestDownload { Phase = "downloading", Percent = 43, EstimatedCompletionAt = now.AddMinutes(12).ToString("O"), UpdatedAt = now.AddMinutes(-11).ToString("O") };
        Assert.Equal("Downloading · 43%", RequestViewerPolicy.DownloadLabel(download, now));
        download.UpdatedAt = now.ToString("O");
        Assert.Contains("12 min", RequestViewerPolicy.DownloadLabel(download, now));
        download.Phase = "future_phase";
        Assert.Equal("Downloading", RequestViewerPolicy.DownloadLabel(download, now));
        Assert.Null(RequestViewerPolicy.DownloadPercent(download));
    }

    [Fact]
    public void ExternalTitleShowsReleaseAndReviewStateWithoutInventingARequest()
    {
        var title = new WatchlistTitle { ReleaseDate = "2027-01-02", Request = new() { Status = "pending", State = "pending" } };
        var presentation = RequestViewerPolicy.WatchlistPresentation(title, new(2026, 9, 30));
        Assert.Contains("2027", presentation.Badge);
        Assert.Contains("awaiting approval", presentation.Caption);
        title.Status = "needs_review";
        Assert.Equal("Needs attention", RequestViewerPolicy.WatchlistPresentation(title).Badge);
        Assert.False(RequestViewerPolicy.WatchlistPresentation(title).Requestable);
        title.Status = "future_state"; title.Request = new() { Requestable = true };
        Assert.Equal("Not requested", RequestViewerPolicy.WatchlistPresentation(title).Badge);
        Assert.True(RequestViewerPolicy.WatchlistPresentation(title).Requestable);
    }

    [Fact]
    public async Task CreateCarriesExplicitSeasonSelectionAndCapabilitiesRemainOptional()
    {
        using var wire = new Wire("{\"id\":\"request\"}", "{\"requests_enabled\":true}");
        var api = new RequestsApi(wire.Client);
        await api.CreateAsync(new() { MediaType = "series", TmdbId = 42, Seasons = [2, 3] });
        using var body = JsonDocument.Parse(wire.Bodies[0]!);
        Assert.Equal(new[] { 2, 3 }, body.RootElement.GetProperty("seasons").EnumerateArray().Select(x => x.GetInt32()));
        var capability = await api.GetStatusAsync();
        Assert.True(capability.RequestsEnabled);
        Assert.False(capability.SeasonRequestsSupported);
        Assert.False(capability.WatchlistTitlesSupported);
    }

    [Fact]
    public async Task ExternalWatchlistReadsAllPagesAndUsesCanonicalTitleIdentityForActions()
    {
        using var wire = new Wire(
            "{\"items\":[{\"tmdb_id\":42,\"media_type\":\"series\",\"title\":\"First\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"second page\"}}",
            "{\"items\":[{\"tmdb_id\":43,\"media_type\":\"movie\",\"title\":\"Second\",\"status\":\"needs_review\"}]}",
            "{\"tmdb_id\":42,\"media_type\":\"series\",\"item_id\":\"catalog:item\"}", "{}", "{}", "{}");
        var api = new RequestsApi(wire.Client);
        Assert.Equal(2, (await api.GetWatchlistTitlesAsync()).Count);
        Assert.Equal("/api/v2/watchlist/titles?limit=200&cursor=second%20page", wire.Paths[1]);
        Assert.Equal("catalog:item", (await api.AddWatchlistTitleAsync("series", 42)).ItemId);
        await api.RemoveWatchlistTitleAsync("series", 42);
        await api.FollowAsync("series", 42);
        await api.UnfollowAsync("series", 42);
        Assert.Equal(new[] { "GET", "GET", "PUT", "DELETE", "PUT", "DELETE" }, wire.Methods);
    }

    [Fact]
    public async Task ExternalWatchlistRejectsRepeatedCursorInsteadOfPresentingPartialListAsComplete()
    {
        const string page = "{\"items\":[{\"tmdb_id\":42}],\"page\":{\"has_more\":true,\"next_cursor\":\"same\"}}";
        using var wire = new Wire(page, page);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RequestsApi(wire.Client).GetWatchlistTitlesAsync());
    }

    [Fact]
    public async Task ExploreHonorsFilteredNextPageAndTmdbCeiling()
    {
        using var wire = new Wire("{\"page\":1,\"total_pages\":1000,\"next_page\":4,\"title\":\"Trending\",\"results\":[]}");
        var page = await new RequestsApi(wire.Client).GetDiscoverySectionAsync("trending-movies");
        Assert.Equal(4, RequestDiscoveryPaging.Next(page, 1));
        page.NextPage = 501; Assert.Null(RequestDiscoveryPaging.Next(page, 499));
        page.NextPage = 499; Assert.Null(RequestDiscoveryPaging.Next(page, 499));
        Assert.Contains("/discover/trending-movies?page=1", wire.Paths.Single());
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly Queue<string> _replies;
        public SiloApiClient Client { get; }
        public List<string> Paths { get; } = [];
        public List<string> Methods { get; } = [];
        public List<string?> Bodies { get; } = [];
        public Wire(params string[] replies)
        {
            _replies = new(replies);
            Client = new(new HttpClient(this));
            Client.SetBaseUrl("https://example.test");
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery); Methods.Add(request.Method.Method);
            Bodies.Add(request.Content == null ? null : await request.Content.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent(_replies.Dequeue()) };
        }
    }
}
