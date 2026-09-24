using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class RequestsTests
{
    [Fact]
    public async Task SearchPreservesMediaTypeQueryAndPage()
    {
        var handler = new CaptureHandler("{\"page\":3,\"total_pages\":8,\"total_results\":150,\"results\":[]}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).SearchAsync("series", "star wars", 3);

        Assert.Equal(3, result.Page);
        Assert.Equal(150, result.TotalResults);
        Assert.Equal("/api/v2/requests/search?q=star%20wars&media_type=series&page=3", handler.LastUri?.PathAndQuery);
    }

    [Fact]
    public async Task BrowseUsesCurrentKindSlugSortAndMediaContract()
    {
        var handler = new CaptureHandler("{\"kind\":\"genre\",\"slug\":\"science-fiction\",\"display_name\":\"Science Fiction\",\"media_type\":\"movie\",\"sort\":\"vote_average\",\"page\":2,\"total_pages\":4,\"results\":[]}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).BrowseDiscoverAsync("genre", "science-fiction", "movie", "vote_average", 2);

        Assert.Equal("Science Fiction", result.DisplayName);
        Assert.Equal("/api/v2/requests/discover/browse/genre/science-fiction?sort=vote_average&page=2&media_type=movie", handler.LastUri?.PathAndQuery);
    }

    [Fact]
    public async Task DetailDeserializesRequestAndLibraryState()
    {
        var handler = new CaptureHandler("""
        {"media_type":"movie","tmdb_id":42,"title":"Example","availability":"available","library_content_id":"content-42","request":{"status":"completed","requestable":false,"reason":"already_available","request_id":"req-1"}}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).GetDetailAsync("movie", 42);

        Assert.Equal("content-42", result.LibraryContentId);
        Assert.Equal("completed", result.Request.Status);
        Assert.False(result.Request.Requestable);
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
