using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public class WatchTogetherLobbyContractTests
{
    [Fact]
    public async Task StageAndStartAreDistinctAndRetainSelectedVersion()
    {
        var handler = new Handler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        var api = new PlaybackApi(client);
        var stage = typeof(PlaybackApi).GetMethod("StageWatchTogetherRoomItemAsync");
        Assert.NotNull(stage);
        await (Task)stage.Invoke(api, ["room/one", "movie", 42, 7, CancellationToken.None])!;
        Assert.EndsWith("/room%2Fone/staged-selection", handler.Path);
        Assert.Equal("42", handler.Body.GetProperty("file_id").GetString());
        Assert.Equal("7", handler.Body.GetProperty("library_id").GetString());
        Assert.Equal(HttpMethod.Put, handler.Method);
        var start = typeof(PlaybackApi).GetMethod("StartWatchTogetherRoomPlaybackAsync");
        Assert.NotNull(start);
        await (Task)start.Invoke(api, ["room/one", CancellationToken.None])!;
        Assert.EndsWith("/room%2Fone/playback/start", handler.Path);
        Assert.Equal(HttpMethod.Post, handler.Method);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Path = "";
        public HttpMethod? Method;
        public JsonElement Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Method = request.Method;
            Body = JsonDocument.Parse(request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"room\":{\"room_id\":\"room\",\"phase\":\"lobby\",\"selected_content_id\":\"movie\"}}", Encoding.UTF8, "application/json") };
        }
    }
}
