using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackHudCompletionTests
{
    [Theory]
    [InlineData("000000", "808080")]
    [InlineData("555555", "808080")]
    [InlineData("808080", "808080")]
    [InlineData("e8b86e", "E8B86E")]
    public void NativeHudUsesTheSameContrastAdjustmentAsTheOsc(string stored, string expected)
    {
        var type = typeof(LoadingCoverBitmap).Assembly.GetType("SiloPlayer.Core.Services.PlayerHudAccent");
        Assert.NotNull(type);
        Assert.Equal(expected, type.GetMethod("OnDark")!.Invoke(null, [stored]));
    }
    [Fact]
    public async Task WatchPreparationRequestsTheSuppliedDisplayWidthAlongsideItsScope()
    {
        var method = typeof(PlaybackApi).GetMethods().SingleOrDefault(m => m.Name == "GetWatchDetailAsync" && m.GetParameters().Any(p => p.Name == "backdropWidth"));
        Assert.NotNull(method);
        using var handler = new WatchHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        var task = (Task<WatchDetailResponse>)method.Invoke(new PlaybackApi(client), ["episode", 7, 42, CancellationToken.None, null, 2560])!;
        await task;
        Assert.Equal("/api/v2/watch/episode?library_id=7&file_id=42&backdrop_width=2560", handler.Path);
    }

    [Fact]
    public void CoverFadeBlendsIntoOneReusableBufferWithoutAllocatingPerFrame()
    {
        var method = typeof(LoadingCoverBitmap).GetMethod("BlendFrame", BindingFlags.Static | BindingFlags.Public);
        Assert.NotNull(method);
        byte[] previous = [10, 20, 30, 255], target = [50, 60, 70, 255], scratch = new byte[4];
        var blend = method.CreateDelegate<Action<byte[], byte[], byte[], double>>();
        blend(previous, target, scratch, 0); Assert.Equal(previous, scratch);
        blend(previous, target, scratch, 0.5); Assert.Equal(new byte[] { 30, 40, 50, 255 }, scratch);
        blend(previous, target, scratch, 1); Assert.Equal(target, scratch);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) blend(previous, target, scratch, i / 100d);
        Assert.Equal(allocated, GC.GetAllocatedBytesForCurrentThread());
    }

    private sealed class WatchHandler : HttpMessageHandler
    {
        internal string? Path;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"content_id\":\"episode\"}") });
        }
    }
}
