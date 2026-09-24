using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models;

namespace SiloPlayer.Tests;

public sealed class CalendarTests
{
    [Fact]
    public void CalendarDefaultsToCurrentFollowingPreset()
    {
        Assert.Equal("following", new AppSettings().CalendarPreset);
    }

    [Fact]
    public async Task CalendarApiSendsCurrentPresetAndLibraryScope()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCalendarAsync(
            "2026-07-06", "2026-07-12", "trending", libraryId: 4);

        Assert.Equal(
            "/api/v2/calendar?start=2026-07-06&end=2026-07-12&filter=trending&library_id=4",
            handler.LastUri?.PathAndQuery);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"events\":[]}")
            });
        }
    }
}
