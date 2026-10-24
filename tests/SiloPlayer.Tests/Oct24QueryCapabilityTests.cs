using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class Oct24QueryCapabilityTests
{
    [Theory]
    [InlineData("{\"show_title_rules\":true,\"extended_query_rules\":false}", true)]
    [InlineData("{\"extended_query_rules\":true}", false)]
    [InlineData("{}", false)]
    [InlineData(null, false)]
    public async Task EpisodeParentTitleUsesItsOwnCurrentServerCapability(string? body, bool expected)
    {
        using var wire = new Wire(body); var client = wire.Client();
        var capabilities = await new CatalogApi(client).GetQueryEditorCapabilitiesAsync();
        Assert.Equal(expected, capabilities.ShowTitleRules);
    }
    private sealed class Wire(string? body) : HttpMessageHandler
    {
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://query-oct24.fixture.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath.EndsWith("/search/capabilities") && body == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("/search/capabilities") ? body ?? "{}" : "{\"items\":[],\"settings\":[]}") });
    }
}
