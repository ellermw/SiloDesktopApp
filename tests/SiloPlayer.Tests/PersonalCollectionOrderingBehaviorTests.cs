using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PersonalCollectionOrderingBehaviorTests
{
    [Fact]
    public async Task PosterMoveSendsFullOwnOrderAcrossRetiredGroups()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://personal-order.invalid"); client.SetProfile("mine");
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(await vm.DropCollectionAsync("b", "a", "old-a"));
        Assert.Equal(HttpMethod.Put, wire.MutationMethod);
        Assert.Equal(new[] { "b", "a" }, wire.Order);
        Assert.Equal("\"profile-order\"", wire.Validator);
    }
    [Fact]
    public async Task ForeignPosterCannotBeMovedIntoOwnCollections()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://personal-order.invalid"); client.SetProfile("mine");
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.False(await vm.DropCollectionAsync("foreign", "a", "old-a"));
        Assert.Null(wire.MutationMethod);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public HttpMethod? MutationMethod; public string[]? Order; public string? Validator;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get)
            {
                MutationMethod = request.Method; Validator = request.Headers.IfMatch.SingleOrDefault()?.ToString();
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (body.RootElement.TryGetProperty("ordered_ids", out var order)) Order = order.EnumerateArray().Select(value => value.GetString()!).ToArray();
            }
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/collections" => """{"items":[{"id":"a","creator_profile_id":"mine","group_id":"old-a"},{"id":"b","creator_profile_id":"mine","group_id":"old-b"},{"id":"foreign","creator_profile_id":"other"}]}""",
                "/api/v2/collections/order" => """{"ordered_ids":["a","b"]}""",
                "/api/v2/collections/capabilities" => """{"item_reorder":true}""",
                _ => "{}"
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }; response.Headers.ETag = new("\"profile-order\""); return response;
        }
    }
}
