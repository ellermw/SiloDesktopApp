using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WatchTogetherV2Tests
{
    [Fact]
    public async Task RoomAndSuggestionCreationMintStableResourceIdsAndSendRoomProofOnlyInHeaders()
    {
        var handler = new RoomHandler();
        var api = CreateApi(handler);
        await api.CreateWatchTogetherRoomAsync();
        await api.CreateWatchTogetherSuggestionAsync("room/one", "private-room-proof", "movie-1", "movie", "Film");
        var room = handler.Requests[0];
        Assert.True(Guid.TryParse(room.Body.GetProperty("room_id").GetString(), out _));
        var suggestion = handler.Requests[1];
        Assert.True(Guid.TryParse(suggestion.Body.GetProperty("suggestion_id").GetString(), out _));
        Assert.Equal("private-room-proof", suggestion.RoomProof);
        Assert.All(handler.Requests, r => Assert.DoesNotContain("private-room-proof", r.Uri.ToString()));
    }

    [Fact]
    public async Task VoteConsumesBodylessReceiptThenTraversesSuggestionsWithRoomProof()
    {
        var handler = new RoomHandler();
        var api = CreateApi(handler);
        var response = await api.VoteWatchTogetherSuggestionAsync("room/one", "proof", "suggestion/one");
        Assert.Equal(2, response.Suggestions.Count);
        Assert.True(response.Suggestions[0].VotedByMe);
        Assert.Contains("/room%2Fone/suggestions/suggestion%2Fone/vote", handler.Requests[0].Uri.AbsolutePath);
        Assert.All(handler.Requests, r => Assert.Equal("proof", r.RoomProof));
        Assert.Contains("cursor=next%2Fpage", handler.Requests.Last().Uri.Query);
    }

    [Fact]
    public async Task EachRoomSocketConnectionRequiresAFreshTicket()
    {
        var handler = new RoomHandler();
        var api = CreateApi(handler);
        var first = await api.CreateRoomControlTicketAsync("room", "proof");
        var second = await api.CreateRoomControlTicketAsync("room", "proof");
        Assert.NotEqual(first.Ticket, second.Ticket);
        Assert.All(handler.Requests, r => Assert.Equal("proof", r.RoomProof));
        Assert.Equal("silo.room.v2", second.Protocol);
    }

    private static PlaybackApi CreateApi(HttpMessageHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return new PlaybackApi(client);
    }

    private sealed class RoomHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? RoomProof, JsonElement Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var json = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((uri, request.Headers.TryGetValues("X-Room-Token", out var proof) ? proof.Single() : null,
                JsonDocument.Parse(json).RootElement.Clone()));
            if (uri.AbsolutePath.EndsWith("/vote")) return new(HttpStatusCode.NoContent);
            if (uri.AbsolutePath.EndsWith("/ws-ticket")) return Json($$"""{"ticket":"ticket-{{Requests.Count}}","protocol":"silo.room.v2","expires_in":30,"max_connection_seconds":300}""");
            if (request.Method == HttpMethod.Get)
                return Json(uri.Query.Contains("cursor=")
                    ? """{"items":[{"id":"two","voted_by_me":false}],"page":{"has_more":false}}"""
                    : """{"items":[{"id":"one","voted_by_me":true}],"page":{"has_more":true,"next_cursor":"next/page"}}""");
            return Json("""{"room":{"room_id":"room"},"room_access_token":"proof"}""");
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
