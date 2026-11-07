using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class WatchTogetherStopTests
{
    [Fact]
    public async Task StopPostsItsDistinctEndpointAndPreservesStagedItem()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        var api = new PlaybackApi(client);
        var stop = typeof(PlaybackApi).GetMethod("StopWatchTogetherRoomPlaybackAsync", [typeof(string), typeof(CancellationToken)]);
        Assert.NotNull(stop);
        var response = await (Task<SiloPlayer.Core.Models.Playback.WatchTogetherRoomResponse>)stop.Invoke(api, ["room/one", CancellationToken.None])!;
        Assert.Equal("POST /api/v2/watch-together/rooms/room%2Fone/playback/stop", Assert.Single(wire.Requests));
        Assert.Equal("lobby", response.Room.Phase);
        Assert.Equal("movie", response.Room.SelectedContentId);
        Assert.Equal(42, response.Room.SelectedFileId);
    }

    [Fact]
    public async Task CapturedStopCannotRebindToReplacementProfile()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        var context = client.CaptureContext(); client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlaybackApi(client).StopWatchTogetherRoomPlaybackAsync(context, "room"));
        Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task HeldStopCannotPublishForReplacementProfile()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        var stopping = new PlaybackApi(client).StopWatchTogetherRoomPlaybackAsync("room");
        await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("replacement"); wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
        Assert.Single(wire.Requests);
    }

    [Theory]
    [InlineData("stop", "POST /api/v2/watch-together/rooms/room%2Fone/playback/stop", null)]
    [InlineData("policy", "PATCH /api/v2/watch-together/rooms/room%2Fone/policy", "{\"guest_control_policy\":\"guest_play_pause\"}")]
    [InlineData("end", "DELETE /api/v2/watch-together/rooms/room%2Fone", null)]
    public async Task CapturedRoomMutationRefreshesExpiredTokenAndReplaysSameIntent(string action, string route, string? body)
    {
        using var wire = new Wire { RequireFreshToken = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        client.SetAccessToken("expired-token");
        var context = client.CaptureContext();
        var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; client.SetAccessToken("fresh-token"); return Task.FromResult(true); });

        await MutateAsync(new PlaybackApi(client), context, action);

        Assert.Equal(1, refreshes);
        Assert.Equal(new[] { route, route }, wire.Requests);
        Assert.Equal(new[] { "expired-token", "fresh-token" }, wire.Attempts.Select(attempt => attempt.Token));
        Assert.All(wire.Attempts, attempt => { Assert.Equal("host", attempt.Profile); Assert.Equal(body, attempt.Body); });
        Assert.True(client.IsCurrentContext(context));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("policy")]
    [InlineData("end")]
    public async Task RoomMutationDoesNotRetryAuthenticationAsAnotherContext(string action)
    {
        using var wire = new Wire { Status = HttpStatusCode.Unauthorized }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        client.SetAccessToken("expired-token");
        var context = client.CaptureContext();
        var refreshes = 0;
        client.SetTokenRefresher(_ =>
        {
            refreshes++; client.SetProfile("replacement"); client.SetAccessToken("fresh-token");
            return Task.FromResult(true);
        });
        var error = await Assert.ThrowsAsync<ApiException>(() => MutateAsync(new PlaybackApi(client), context, action));
        Assert.Equal(401, error.StatusCode);
        Assert.Single(wire.Requests); Assert.Equal(1, refreshes);
        Assert.Equal("host", Assert.Single(wire.Attempts).Profile);
    }

    private static Task MutateAsync(PlaybackApi api, ApiRequestContext context, string action) => action switch
    {
        "stop" => api.StopWatchTogetherRoomPlaybackAsync(context, "room/one"),
        "policy" => api.UpdateWatchTogetherRoomPolicyAsync(context, "room/one", "guest_play_pause"),
        "end" => api.CloseWatchTogetherRoomAsync(context, "room/one"),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<(string? Token, string? Profile, string? Body)> Attempts { get; } = [];
        public bool Hold;
        public bool RequireFreshToken;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            Attempts.Add((request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(ct)));
            if (Hold) { Entered.TrySetResult(); await Release.Task; }
            var status = RequireFreshToken && request.Headers.Authorization?.Parameter != "fresh-token"
                ? HttpStatusCode.Unauthorized : Status;
            if (status == HttpStatusCode.OK && request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
            return new(status) { Content = new StringContent(
                "{\"room\":{\"room_id\":\"room/one\",\"phase\":\"lobby\",\"selected_content_id\":\"movie\",\"selected_file_id\":42}}") };
        }
    }
}
