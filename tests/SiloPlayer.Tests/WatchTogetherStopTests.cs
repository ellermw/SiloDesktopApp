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

    [Fact]
    public async Task StopDoesNotRetryAuthenticationAsAnotherContext()
    {
        using var wire = new Wire { Status = HttpStatusCode.Unauthorized }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://party.invalid"); client.SetProfile("host");
        var refreshes = 0; client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new PlaybackApi(client).StopWatchTogetherRoomPlaybackAsync("room"));
        Assert.Single(wire.Requests); Assert.Equal(0, refreshes);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Hold;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            if (Hold) { Entered.TrySetResult(); await Release.Task; }
            return new(Status) { Content = new StringContent(
                "{\"room\":{\"room_id\":\"room/one\",\"phase\":\"lobby\",\"selected_content_id\":\"movie\",\"selected_file_id\":42}}") };
        }
    }
}
