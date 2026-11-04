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

    [Theory]
    [InlineData(false, "access", true)] [InlineData(true, "access", true)]
    [InlineData(false, "profile", true)] [InlineData(true, "profile", true)]
    [InlineData(false, "access", false)] [InlineData(true, "access", false)]
    public async Task RoomWriteUsesAcceptedIdentityOnlyWhenSupplied(bool suggestion, string change, bool identityBound)
    {
        using var handler = new RoomHandler { HoldMutations = true };
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test"); client.SetProfile("viewer");
        var api = new PlaybackApi(client);
        ApiIdentityContext? owner = identityBound ? client.CaptureIdentityContext() : null;
        Task pending = suggestion
            ? api.CreateWatchTogetherSuggestionAsync("room", "proof", "movie", "movie", "Film", expectedIdentityContext: owner)
            : api.StageWatchTogetherRoomItemAsync("room", "movie", libraryId: 12, expectedIdentityContext: owner);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "access") client.InvalidateAccessContext(); else client.SetProfile("replacement");
        }
        finally { handler.Release.TrySetResult(); }
        if (identityBound && change == "access") await pending;
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(handler.Requests.Where(r => r.Uri.AbsolutePath.EndsWith(suggestion ? "/suggestions" : "/staged-selection")
            && r.Body.TryGetProperty("content_id", out _)));
        // Successful suggestions query afresh, never resubmit the accepted mutation.
        Assert.Equal(identityBound && change == "access" && suggestion ? 3 : 1, handler.Requests.Count);
        if (suggestion) Assert.All(handler.Requests, request => Assert.Equal("proof", request.RoomProof));
    }

    [Theory] [InlineData("access")] [InlineData("profile")]
    public async Task SuggestionSubmissionCompletesAtAcceptanceWithoutAListRead(string change)
    {
        using var handler = new RoomHandler { HoldMutations = true, FailSuggestionReads = true };
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test"); client.SetProfile("viewer");
        var pending = new PlaybackApi(client).SubmitWatchTogetherSuggestionAsync("room", "proof", "movie", "movie", "Film", client.CaptureIdentityContext());
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (change == "access") client.InvalidateAccessContext(); else client.SetProfile("replacement");
        }
        finally { handler.Release.TrySetResult(); }
        if (change == "access") await pending.WaitAsync(TimeSpan.FromSeconds(2));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/suggestions", request.Uri.AbsolutePath);
        Assert.Equal("movie", request.Body.GetProperty("content_id").GetString());
        Assert.True(Guid.TryParse(request.Body.GetProperty("suggestion_id").GetString(), out _));
        Assert.Equal("proof", request.RoomProof);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RetiredRoomIntentCannotSendUnderReplacementViewer(bool suggestion)
    {
        using var handler = new RoomHandler(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test"); client.SetProfile("viewer");
        var owner = client.CaptureIdentityContext(); client.SetProfile("replacement");
        var api = new PlaybackApi(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => suggestion
            ? (Task)api.CreateWatchTogetherSuggestionAsync("room", "proof", "movie", "movie", "Film", expectedIdentityContext: owner)
            : api.StageWatchTogetherRoomItemAsync("room", "movie", expectedIdentityContext: owner));
        Assert.Empty(handler.Requests);
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
        internal bool HoldMutations, FailSuggestionReads;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var json = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((uri, request.Headers.TryGetValues("X-Room-Token", out var proof) ? proof.Single() : null,
                JsonDocument.Parse(json).RootElement.Clone()));
            if (HoldMutations && request.Method != HttpMethod.Get) { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (FailSuggestionReads && request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("/suggestions"))
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
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
