using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class MediaSplitParityTests
{
    [Fact]
    public async Task FilesAreCompleteAcrossCurrentCursorPages()
    {
        using var wire = new Wire((_, n) => n == 1 ? "{\"items\":[{\"id\":\"1\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"next\"}}" : "{\"items\":[{\"id\":\"2\"}],\"page\":{\"has_more\":false}}");
        using var http = new HttpClient(wire); var client = Client(http);
        var files = await new MediaSplitApi(client).GetFilesAsync(client.CaptureContext(), "movie/one");
        Assert.Equal(2, files.Count); Assert.Contains("limit=200&cursor=next", wire.Paths[1]); Assert.Contains("movie%2Fone", wire.Paths[0]);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("same")]
    public async Task IncompleteOrRepeatedCursorNeverReturnsPartialFileList(string? cursor)
    {
        using var wire = new Wire((_, _) => JsonSerializer.Serialize(new { items = new[] { new { id = "1" } }, page = new { has_more = true, next_cursor = cursor } }));
        using var http = new HttpClient(wire); var client = Client(http);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MediaSplitApi(client).GetFilesAsync(client.CaptureContext(), "fixture"));
        Assert.InRange(wire.Paths.Count, 1, 2);
    }
    [Fact]
    public async Task RevokedAuthorityBetweenPagesStopsTheRead()
    {
        using var wire = new Wire((_, _) => "{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"next\"}}");
        using var http = new HttpClient(wire); var client = Client(http); wire.After = () => client.SetProfile("changed");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaSplitApi(client).GetFilesAsync(client.CaptureContext(), "fixture")); Assert.Single(wire.Paths);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewAndCommitCannotReplayAfterUnauthorized(bool dryRun)
    {
        using var wire = new Wire((_, _) => "{}") { Status = HttpStatusCode.Unauthorized }; using var http = new HttpClient(wire); var client = Client(http); var refreshed = false;
        client.SetTokenRefresher(_ => { refreshed = true; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new MediaSplitApi(client).SplitAsync(client.CaptureContext(), "fixture", new { file_ids = new[] { "1" }, target = new { unmatched = true }, history_mode = "evidence", dry_run = dryRun }));
        Assert.False(refreshed); Assert.Single(wire.Paths); using var body = JsonDocument.Parse(wire.Body!); Assert.Equal(dryRun, body.RootElement.GetProperty("dry_run").GetBoolean());
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://split-fixture.invalid"); client.SetAccessToken("fixture"); return client; }
    private sealed class Wire(Func<HttpRequestMessage, int, string> response) : HttpMessageHandler
    {
        public List<string> Paths = []; public string? Body; public Action? After; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Paths.Add(request.RequestUri!.PathAndQuery); Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct); var result = new HttpResponseMessage(Status) { Content = new StringContent(response(request, Paths.Count)) }; After?.Invoke(); return result; }
    }
}
