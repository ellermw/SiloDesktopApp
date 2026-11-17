using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class NotificationBulkMuteTests
{
    [Fact]
    public async Task DeduplicatesChunksAt500AndReturnsReceiptsInRequestOrder()
    {
        var batches = new List<string[]>();
        using var http = new HttpClient(new Wire(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/notifications/series/mute", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var ids = body.RootElement.GetProperty("series_ids").EnumerateArray().Select(value => value.GetString()!).ToArray();
            batches.Add(ids);
            return Json(new { results = Enumerable.Reverse(ids).Select(id => new { series_id = id, outcome = id == "0" ? "not_found" : id == "1" ? "unchanged" : "applied" }) });
        }));
        var api = new NotificationsApi(Client(http));
        var ids = Enumerable.Range(0, 1001).Select(index => index.ToString()).ToArray();
        var result = await api.SetManySeriesMutedAsync(api.RequestContext, ids.Concat(ids.Take(2)), true);
        Assert.Equal(new[] { 500, 500, 1 }, batches.Select(batch => batch.Length));
        Assert.Equal(ids, result.Select(receipt => receipt.SeriesId));
        Assert.Equal(1000, result.Count(receipt => receipt.Outcome != "not_found"));
        Assert.DoesNotContain("1", result.Where(receipt => receipt.Outcome == "applied").Select(receipt => receipt.SeriesId));
        await api.SetManySeriesMutedAsync(api.RequestContext, [], true);
        Assert.Equal(3, batches.Count);
    }

    [Fact]
    public async Task UnauthorizedBulkWriteNeverRefreshesOrReplays()
    {
        var calls = 0; var refresh = 0;
        using var http = new HttpClient(new Wire(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }); }));
        var client = Client(http);
        client.SetTokenRefresher(_ => { refresh++; return Task.FromResult(true); });
        var api = new NotificationsApi(client);
        await Assert.ThrowsAsync<ApiException>(() => api.SetManySeriesMutedAsync(api.RequestContext, ["one"], false));
        Assert.Equal(1, calls); Assert.Equal(0, refresh);
    }

    [Fact]
    public async Task ContextChangeBetweenChunksDiscardsReceiptAndStopsDispatch()
    {
        var calls = 0; SiloApiClient? client = null;
        using var http = new HttpClient(new Wire(async request =>
        {
            calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var results = body.RootElement.GetProperty("series_ids").EnumerateArray().Select(value => new { series_id = value.GetString(), outcome = "applied" }).ToArray();
            client!.SetProfile("replacement");
            return Json(new { results });
        }));
        client = Client(http); var api = new NotificationsApi(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SetManySeriesMutedAsync(api.RequestContext, Enumerable.Range(0, 501).Select(index => index.ToString()), true));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"results\":[]}")]
    [InlineData("{\"results\":[{\"series_id\":\"one\",\"outcome\":\"unknown\"}]}")]
    [InlineData("{\"results\":[{\"series_id\":\"other\",\"outcome\":\"applied\"}]}")]
    public async Task InvalidReceiptsCannotPublishSuccess(string payload)
    {
        using var http = new HttpClient(new Wire(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) })));
        var api = new NotificationsApi(Client(http));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.SetManySeriesMutedAsync(api.RequestContext, ["one"], true));
    }

    private static SiloApiClient Client(HttpClient http)
    {
        var client = new SiloApiClient(http); client.SetBaseUrl("https://notifications.invalid"); client.SetProfile("original"); return client;
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private sealed class Wire(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
