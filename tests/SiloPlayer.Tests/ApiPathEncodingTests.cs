using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class ApiPathEncodingTests
{
    [Fact]
    public async Task CatalogIdentifiersAreEncodedAsSinglePathSegments()
    {
        var handler = new CaptureHandler();
        var api = new CatalogApi(CreateClient(handler));

        await api.AddFavoriteAsync("movie/one two");

        Assert.Equal("/api/v2/favorites/movie%2Fone%20two", handler.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task PlaybackSessionIdentifiersAreEncodedAsSinglePathSegments()
    {
        var handler = new CaptureHandler();
        var api = new PlaybackApi(CreateClient(handler));

        await api.StartPlaybackV3Async(new SiloPlayer.Core.Models.Playback.PlaybackStartRequestV3 { FileId = 42 });
        await api.StopPlaybackAsync("session/one two");

        Assert.Equal("/api/v2/playback/session%2Fone%20two", handler.LastUri?.AbsolutePath);
    }

    [Fact]
    public async Task ProfileIdentifiersAreEncodedAsSinglePathSegments()
    {
        var handler = new CaptureHandler("{\"valid\":true}");
        var api = new AuthApi(CreateClient(handler));

        await api.VerifyPinAsync("profile/one two", "1234");

        Assert.Equal("/api/v2/profiles/profile%2Fone%20two/verify-pin", handler.LastUri?.AbsolutePath);
    }

    private static SiloApiClient CreateClient(HttpMessageHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return client;
    }

    private sealed class CaptureHandler(string responseJson = "{}") : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            if (LastUri!.AbsolutePath.StartsWith("/api/v2/playback/"))
            {
                var body = LastUri.AbsolutePath.EndsWith("/capabilities")
                    ? """{"allowed":true,"state":"available","installation_id":"installation","protocol_versions":[3]}"""
                    : LastUri.AbsolutePath.EndsWith("/start")
                        ? """{"outcome":"playable","session_id":"session/one two"}"""
                        : """{"outcome":"stopped"}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }
            return Task.FromResult(new HttpResponseMessage(
                request.Method == HttpMethod.Delete || request.Method == HttpMethod.Put
                    ? HttpStatusCode.NoContent
                    : HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}
