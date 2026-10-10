using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaMarkerParityTests
{
    [Fact]
    public void AbsentServerMarkersRemainAbsentWithoutAnEdit()
    {
        var original = JsonSerializer.Deserialize<JsonElement>("{\"intro\":{\"start_seconds\":null,\"end_seconds\":null}}");
        Assert.Empty(MarkerEditPlan.Build(original, new Dictionary<string, (string, string)> { ["intro"] = ("", "") }));
    }
    [Fact]
    public void UntouchedFractionalMarkersAreNotResavedAsManual()
    {
        var original = JsonSerializer.Deserialize<JsonElement>("{\"intro\":{\"start_seconds\":90.4,\"end_seconds\":110.5}}");
        var plan = MarkerEditPlan.Build(original, new Dictionary<string, (string, string)> { ["intro"] = ("1:30", "1:51") });
        Assert.Empty(plan);
    }
    [Fact]
    public void ClearIsExplicitAndNewMarkerUsesCurrentWireNames()
    {
        var original = JsonSerializer.Deserialize<JsonElement>("{\"intro\":{\"start_seconds\":1,\"end_seconds\":2}}");
        var plan = MarkerEditPlan.Build(original, new Dictionary<string, (string, string)> { ["intro"] = ("", ""), ["credits"] = ("1:02:03", "1:03:04") });
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(plan));
        Assert.Equal(JsonValueKind.Null, wire.RootElement.GetProperty("intro").ValueKind);
        Assert.Equal(3723, wire.RootElement.GetProperty("credits").GetProperty("start_seconds").GetDouble());
        Assert.Equal(3784, wire.RootElement.GetProperty("credits").GetProperty("end_seconds").GetDouble());
        Assert.False(wire.RootElement.TryGetProperty("recap", out _));
    }
    [Theory]
    [InlineData("1:20", "")]
    [InlineData("NaN", "2:00")]
    [InlineData("-1", "2:00")]
    [InlineData("1:30", "1:30")]
    [InlineData("1:2:3:4", "400")]
    public void InvalidRangeCannotBecomeAMutation(string start, string end)
        => Assert.Throws<ArgumentException>(() => MarkerEditPlan.Build(default, new Dictionary<string, (string, string)> { ["intro"] = (start, end) }));

    [Fact]
    public async Task MarkerWriteBindsContextAndDoesNotReplayAfterUnauthorized()
    {
        using var handler = new Wire(HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler); var client = Client(http); var context = client.CaptureContext(); var refreshes = 0;
        client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new MediaMarkerApi(client).SetMarkersAsync(context, "episode/one", new() { ["intro"] = null }));
        Assert.Equal(0, refreshes); Assert.Single(handler.Paths); Assert.Equal("/api/v2/markers/items/episode%2Fone", handler.Paths[0]);
        client.SetProfile("different-fixture-profile");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MediaMarkerApi(client).SetMarkersAsync(context, "episode/one", new()));
        Assert.Single(handler.Paths);
    }
    [Theory]
    [InlineData("episode", "intro", 404, 2)]
    [InlineData("episode", "credits", 404, 1)]
    [InlineData("movie", "intro", 404, 1)]
    [InlineData("episode", "intro", 405, 1)]
    public async Task LegacyFallbackIsEpisodeIntroOnlyOnMissingOperation(string type, string kind, int status, int count)
    {
        using var handler = new Wire((HttpStatusCode)status); using var http = new HttpClient(handler); var client = Client(http);
        await Assert.ThrowsAsync<ApiException>(() => new MediaMarkerApi(client).RedetectAsync(client.CaptureContext(), "fixture", type, kind));
        Assert.Equal(count, handler.Paths.Count);
        if (count == 2) Assert.EndsWith("/redetect-intro", handler.Paths[1]);
    }
    [Fact]
    public async Task SeekRegenerationNeverRefreshesAndReplays()
    {
        using var handler = new Wire(HttpStatusCode.Unauthorized); using var http = new HttpClient(handler); var client = Client(http); var refreshed = false;
        client.SetTokenRefresher(_ => { refreshed = true; return Task.FromResult(true); });
        await Assert.ThrowsAsync<ApiException>(() => new MediaMarkerApi(client).RegeneratePreviewsAsync(client.CaptureContext(), "fixture"));
        Assert.False(refreshed); Assert.Single(handler.Paths);
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://example.test"); client.SetAccessToken("fixture-only"); return client; }
    private sealed class Wire(HttpStatusCode code) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Paths.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{\"error\":\"fixture_error\",\"message\":\"fixture rejection\"}") }); }
    }
}
