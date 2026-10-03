using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Tests;
public class MatchSearchLimitTests
{
    [Fact]
    public async Task DefaultMatchSearchAsksForAllFiveHundredCandidates()
    {
        var handler = new Wire(); var client = new SiloApiClient(new HttpClient(handler)); client.SetBaseUrl("https://match-fixture.invalid");
        await new MediaMaintenanceApi(client).SearchMatchesAsync("movie:1", new ItemMatchSearchRequest { Title = "Test" });
        Assert.Equal(500, JsonDocument.Parse(handler.Body!).RootElement.GetProperty("limit").GetInt32());
    }
    private sealed class Wire : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Body = await request.Content!.ReadAsStringAsync(cancellationToken); return new(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[],\"truncated\":true}") }; }
    }
}
