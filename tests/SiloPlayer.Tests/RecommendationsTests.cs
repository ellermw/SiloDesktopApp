using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class RecommendationsTests
{
    [Fact]
    public async Task TasteProfileUsesCurrentServerContract()
    {
        var handler = new CaptureHandler("""
        {
          "top_genres":["Drama","Science Fiction"],
          "favorite_directors":["Denis Villeneuve"],
          "signal_counts":{"rated_5":3,"watch_high":4},
          "updated_at":"2026-07-11T12:00:00Z"
        }
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var profile = await new RecommendationsApi(client).GetTasteProfileAsync();

        Assert.Equal(new[] { "Drama", "Science Fiction" }, profile.TopGenres);
        Assert.Equal(new[] { "Denis Villeneuve" }, profile.FavoriteDirectors);
        Assert.Equal(7, profile.SignalCounts.Values.Sum());
        Assert.Equal("/api/v2/recommendations/taste-profile", handler.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task DiscoverUsesCurrentEndpointAndSectionMetadata()
    {
        var handler = new CaptureHandler("""
        {"items":[{"type":"genre","title":"Popular in Drama","kind":"genre","key":"Drama","items":[]}]}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var response = await new RecommendationsApi(client).GetDiscoverAsync();

        Assert.Equal("genre", response.Rows[0].SectionKind);
        Assert.Equal("Drama", response.Rows[0].SectionKey);
        Assert.Equal("/api/v2/recommendations/discover", handler.LastUri?.AbsolutePath);
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}
