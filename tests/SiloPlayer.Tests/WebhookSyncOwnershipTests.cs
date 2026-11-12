using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WebhookSyncOwnershipTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("rotate")]
    [InlineData("mappings")]
    [InlineData("save-mappings")]
    [InlineData("events")]
    [InlineData("delete")]
    public async Task RetiredOwnerCannotReturnSecretBearingDataOrCompletion(string operation)
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire));
        client.SetBaseUrl("https://webhook.fixture.invalid"); client.SetAccessToken("fixture-only"); client.SetProfile("primary");
        wire.OnRequest = () => client.SetProfile("secondary");
        var api = new WebhookSyncApi(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            switch (operation)
            {
                case "list": await api.GetConnectionsAsync(); break;
                case "create": await api.CreateConnectionAsync(new()); break;
                case "update": await api.UpdateConnectionAsync("c1", new()); break;
                case "rotate": await api.RotateWebhookAsync("c1"); break;
                case "mappings": await api.GetProfileMappingsAsync("c1"); break;
                case "save-mappings": await api.UpdateProfileMappingsAsync("c1", new { mappings = Array.Empty<object>() }); break;
                case "events": await api.GetEventsAsync("c1"); break;
                case "delete": await api.DeleteConnectionAsync("c1"); break;
            }
        });
        Assert.Equal(1, wire.Requests);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal Action? OnRequest;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; OnRequest?.Invoke();
            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.OK)
            { Content = new StringContent("""{"items":[],"page":{"has_more":false},"connection":{"id":"c1","webhook_url":"/receiver"},"webhook_url":"/receiver"}""") });
        }
    }
}
