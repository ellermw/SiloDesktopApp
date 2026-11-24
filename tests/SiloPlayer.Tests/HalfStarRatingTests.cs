using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Tests;

/// <summary>The viewer's own rating as the server stores it, 1 to 10 (silo-server #4398): half stars on a five-star control.</summary>
public sealed class HalfStarRatingTests
{
    [Theory]
    [InlineData(1, 2)] [InlineData(2, 2)] [InlineData(3, 4)] [InlineData(4, 4)] [InlineData(5, 6)]
    [InlineData(6, 6)] [InlineData(7, 8)] [InlineData(8, 8)] [InlineData(9, 10)] [InlineData(10, 10)]
    public void EitherHalfOfAStarPicksTheWholeStar(int half, int whole)
    {
        Assert.Equal(whole, RatingScale.WholeStar(half));
        Assert.Equal(whole / 2, RatingScale.StarOf(half));
        Assert.Equal(whole, RatingScale.ToggleStar(half, null));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(7, true)] [InlineData(10, true)] [InlineData(11, false)] [InlineData(-3, false)]
    public void OnlyOneToTenIsAScore(int value, bool score) => Assert.Equal(score, RatingScale.IsScore(value));

    [Theory]
    [InlineData(1, 0.5)] [InlineData(2, 1.0)] [InlineData(7, 3.5)] [InlineData(10, 5.0)]
    public void ScoresReadAsStars(int score, double stars) => Assert.Equal((decimal)stars, RatingScale.Stars(score));

    [Theory]
    [InlineData(8, 7)] [InlineData(8, 8)] [InlineData(6, 5)] [InlineData(6, 6)] [InlineData(2, 1)] [InlineData(10, 9)]
    public void PickingEitherHalfOfTheStarThatIsSetClearsTheRating(int rating, int half)
    {
        Assert.True(RatingScale.Clears(half, rating));
        Assert.Null(RatingScale.ToggleStar(half, rating));
    }

    [Fact]
    public void AHalfStarRatingIsNotTheStarThatClears()
    {
        // 7 is three and a half stars. Neither half of star four is the star that is set, so picking it fills star four.
        Assert.False(RatingScale.Clears(7, 7));
        Assert.False(RatingScale.Clears(8, 7));
        Assert.Equal(8, RatingScale.ToggleStar(7, 7));
        Assert.Equal(8, RatingScale.ToggleStar(8, 7));
        // Star three's halves are not the rating either: a click there fills star three.
        Assert.Equal(6, RatingScale.ToggleStar(5, 7));
        Assert.Equal(6, RatingScale.ToggleStar(6, 7));
    }

    [Fact]
    public void ActivatingAHalfFromTheKeyboardSetsItsOwnScoreOrClearsItWhenItIsTheRating()
    {
        Assert.Equal(7, RatingScale.ToggleScore(7, null));
        Assert.Equal(7, RatingScale.ToggleScore(7, 8));
        Assert.Null(RatingScale.ToggleScore(7, 7));
    }

    [Theory]
    [InlineData(null, 1, 1)] [InlineData(null, -1, 1)] [InlineData(1, -1, 1)] [InlineData(1, 1, 2)] [InlineData(7, 1, 8)]
    [InlineData(7, -1, 6)] [InlineData(9, 1, 10)] [InlineData(10, 1, 10)] [InlineData(10, -1, 9)] [InlineData(2, -1, 1)]
    public void TheArrowKeysStepHalfAStarAndStopAtTheEnds(int? rating, int direction, int expected)
        => Assert.Equal(expected, RatingScale.Step(rating, direction));

    [Theory]
    [InlineData(null, 1, false)] [InlineData(3, 3, true)] [InlineData(3, 4, false)] [InlineData(7, 7, true)] [InlineData(7, 8, false)] [InlineData(10, 10, true)]
    public void AHalfIsFilledUpToTheShownScore(int? shown, int half, bool filled) => Assert.Equal(filled, RatingScale.IsFilled(half, shown));

    [Theory]
    [InlineData(null, 1)] [InlineData(1, 1)] [InlineData(7, 7)] [InlineData(10, 10)] [InlineData(0, 1)] [InlineData(14, 1)]
    public void OneHalfTakesTheTabStop(int? rating, int expected) => Assert.Equal(expected, RatingScale.TabbableScore(rating));

    [Theory]
    [InlineData(null, 7, 7)]       // a new rating
    [InlineData(5, 8, 8)]          // a different rating
    [InlineData(8, 8, null)]       // already the rating: nothing to send
    [InlineData(8, null, 8)]       // clearing hands the command the current score, which toggles it off
    [InlineData(null, null, null)] // clearing an unrated title: nothing
    [InlineData(null, 11, null)]   // outside the scale: nothing
    [InlineData(null, 0, null)]
    public void TheRatingCommandIsHandedOnlyWhatChangesTheRating(int? rating, int? picked, int? expected)
        => Assert.Equal(expected, RatingScale.CommandArgument(rating, picked));

    [Fact]
    public void LabelsFollowTheWebUiHalfStarWording()
    {
        using var locale = AppStrings.UseForCurrentFlow(LocaleService.Shared.Create(AppLocaleResolution.Resolve("en", ["en-US"], "en-US")));
        Assert.Equal("0.5 stars", RatingScale.Label(1));
        Assert.Equal("1 star", RatingScale.Label(2));
        Assert.Equal("1.5 stars", RatingScale.Label(3));
        Assert.Equal("3.5 stars", RatingScale.Label(7));
        Assert.Equal("5 stars", RatingScale.Label(10));
        Assert.Equal("Rating", AppStrings.Get("web.item.rating.label"));
        Assert.Equal("Remove rating", AppStrings.Get("web.item.rating.remove"));
    }

    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(10)]
    public async Task ASetRatingSendsTheScoreAsTheRatingField(int score)
    {
        var wire = new Wire(); using var http = new HttpClient(wire);
        await new CatalogApi(Client(http)).SetRatingAsync("movie:heat-1995", score);
        var request = Assert.Single(wire.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/api/v2/ratings/movie%3Aheat-1995", request.Target);
        Assert.Equal("{\"rating\":" + score + "}", request.Body);
    }

    [Theory]
    [InlineData(0)] [InlineData(11)] [InlineData(-1)] [InlineData(99)]
    public async Task ARatingOutsideOneToTenIsNeverSent(int score)
    {
        var wire = new Wire(); using var http = new HttpClient(wire);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new CatalogApi(Client(http)).SetRatingAsync("film", score));
        Assert.Empty(wire.Requests);
    }

    [Theory]
    [InlineData(7)] [InlineData(8)] [InlineData(1)]
    public async Task AStoredScoreIsReadBackUnchanged(int score)
    {
        var wire = new Wire { Reply = "{\"item_id\":\"film\",\"rating\":" + score + ",\"rated_at\":\"2026-11-23T10:00:00.000Z\"}" };
        using var http = new HttpClient(wire);
        Assert.Equal(score, await new CatalogApi(Client(http)).GetRatingAsync("film"));
    }

    [Fact]
    public async Task AnUnratedTitleReadsAsNoRating()
    {
        var wire = new Wire { Status = HttpStatusCode.NotFound, Reply = "{\"type\":\"https://silo.invalid/problems/not-found\",\"title\":\"Not Found\",\"status\":404}" };
        using var http = new HttpClient(wire);
        Assert.Null(await new CatalogApi(Client(http)).GetRatingAsync("film"));
    }

    [Fact]
    public async Task ADeleteClearsTheRating()
    {
        var wire = new Wire { Status = HttpStatusCode.NoContent }; using var http = new HttpClient(wire);
        await new CatalogApi(Client(http)).DeleteRatingAsync("film");
        var request = Assert.Single(wire.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/v2/ratings/film", request.Target);
    }

    [Fact]
    public void ADetailRatingIsTheSameScore()
    {
        var detail = System.Text.Json.JsonSerializer.Deserialize<MediaItemDetail>("{\"content_id\":\"film\",\"type\":\"movie\",\"user_rating\":7}",
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true });
        Assert.Equal(7, detail!.UserRating);
    }

    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://ratings.invalid"); client.SetProfile("alpha"); return client; }

    private sealed class Wire : HttpMessageHandler
    {
        internal sealed record Seen(HttpMethod Method, string Target, string Body);
        internal readonly List<Seen> Requests = [];
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Reply = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new(request.Method, request.RequestUri!.PathAndQuery, body));
            var status = request.Method == HttpMethod.Put && Status == HttpStatusCode.OK ? HttpStatusCode.NoContent : Status;
            var ok = status is HttpStatusCode.OK;
            return new HttpResponseMessage(status)
            { Content = new StringContent(status == HttpStatusCode.NoContent ? "" : Reply, Encoding.UTF8, ok ? "application/json" : "application/problem+json") };
        }
    }
}
