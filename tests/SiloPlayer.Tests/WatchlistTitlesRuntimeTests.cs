using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class WatchlistTitlesRuntimeTests
{
    [Fact]
    public async Task DisabledRequestsCannotExposeOrFetchExternalTitles()
    {
        var wire = new Wire(false); var client = wire.Client();
        var vm = new WatchlistViewModel(new CatalogApi(client), new RequestsApi(client));
        await vm.LoadExternalTitlesAsync();
        Assert.False(vm.ExternalTitlesSupported);
        Assert.Equal(0, wire.TitleReads);
        Assert.Empty(vm.ExternalTitles);
    }

    [Fact]
    public async Task ExternalTitlesAreRankedSoonestFirstRatherThanTransportOrder()
    {
        var wire = new Wire(true); var client = wire.Client();
        var vm = new WatchlistViewModel(new CatalogApi(client), new RequestsApi(client));
        await vm.LoadExternalTitlesAsync();
        Assert.Null(vm.ExternalTitlesError);
        Assert.Equal(new[] { 3, 2, 1 }, vm.ExternalTitles.Select(t => t.TmdbId));
    }

    private sealed class Wire(bool enabled) : HttpMessageHandler
    {
        public int TitleReads;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://watchlist-fixture.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("/status"))
                json = "{\"requests_enabled\":" + enabled.ToString().ToLowerInvariant() + ",\"watchlist_titles_supported\":true}";
            else
            {
                TitleReads++;
                json = """{"items":[{"tmdb_id":1,"media_type":"movie","status":"needs_review"},{"tmdb_id":2,"media_type":"series","release_date":"2027-01-01","request":{"status":"approved"}},{"tmdb_id":3,"media_type":"movie","request":{"status":"processing","download":{"phase":"downloading","percent":25}}}],"page":{"has_more":false}}""";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
