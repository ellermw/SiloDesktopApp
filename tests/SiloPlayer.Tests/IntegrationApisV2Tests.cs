using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class IntegrationApisV2Tests
{
    [Fact]
    public async Task ApiKeysFollowsCursorAndReadsStringIdentifiers()
    {
        var calls = 0;
        var client = Client(request =>
        {
            Assert.Equal("/api/v2/api-keys", request.RequestUri!.AbsolutePath);
            if (++calls == 1) return Json("""{"items":[{"id":"1","label":"first"}],"page":{"has_more":true,"next_cursor":"next/+"}}""");
            Assert.Contains("cursor=next%2F%2B", request.RequestUri.Query);
            return Json("""{"items":[{"id":"2","label":"second"}],"page":{"has_more":false}}""");
        });
        Assert.Equal(2, (await new ApiKeysApi(client).GetApiKeysAsync()).Count);
    }

    [Fact]
    public async Task HistoryImportWritesStringSourceIdAndNumericFieldsStayNumeric()
    {
        string? sent = null;
        var client = Client(async request =>
        {
            Assert.Equal("/api/v2/history-imports/runs", request.RequestUri!.AbsolutePath);
            sent = await request.Content!.ReadAsStringAsync();
            return Json("""{"id":"run"}""");
        });
        await new HistoryImportApi(client).CreateImportRunAsync(new() { ProfileId = "p", Source = "plex", SourceId = 42 });
        using var json = JsonDocument.Parse(sent!);
        Assert.Equal("42", json.RootElement.GetProperty("source_id").GetString());
    }

    [Fact]
    public async Task WatchProviderEditUsesSettingsRevisionAndReloadsFullConnection()
    {
        var calls = 0;
        var client = Client(request =>
        {
            calls++;
            if (calls is 1 or 4)
            {
                Assert.Equal("/api/v2/watch-providers/trakt/connection", request.RequestUri!.AbsolutePath);
                return Json("""{"provider":"trakt","connected":true}""");
            }
            if (calls is 2 or 5)
            {
                Assert.EndsWith("/connection/settings", request.RequestUri!.AbsolutePath);
                var response = Json("""{"import_watched_enabled":true}""");
                response.Headers.ETag = new("\"settings-1\"");
                return response;
            }
            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("\"settings-1\"", request.Headers.IfMatch.Single().Tag);
            return Json("""{"import_watched_enabled":true}""");
        });
        var api = new WatchProvidersApi(client);
        await api.GetConnectionAsync("trakt");
        var result = await api.UpdateConnectionAsync("trakt", new Dictionary<string, object?> { ["import_watched_enabled"] = true });
        Assert.True(result.Connected);
        Assert.True(result.ImportWatchedEnabled);
        Assert.Equal("trakt", result.Provider);
        Assert.Equal(5, calls);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static SiloApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> handle) => Client(request => Task.FromResult(handle(request)));
    private static SiloApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle)
    {
        var client = new SiloApiClient(new HttpClient(new Handler(handle)));
        client.SetBaseUrl("https://fixture.invalid");
        return client;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
}
