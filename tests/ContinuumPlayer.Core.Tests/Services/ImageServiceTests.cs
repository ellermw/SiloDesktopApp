using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class ImageServiceTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly ImageService _service;

    public ImageServiceTests()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "ContinuumImgTest_" + Guid.NewGuid().ToString("N"));
        _service = new ImageService(_cacheDir, maxMemoryCacheBytes: 1024 * 1024);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, true);
    }

    [Fact]
    public async Task GetImageAsync_CachesOnDisk()
    {
        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var handler = new MockHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeImageBytes)
        });
        var http = new HttpClient(handler);

        var result = await _service.GetImageAsync("content-123", "poster", "https://example.com/img.jpg", http);
        Assert.Equal(fakeImageBytes, result);

        // Second call should hit disk cache, not HTTP
        var handler2 = new MockHttpHandler(_ => throw new Exception("Should not call HTTP"));
        var http2 = new HttpClient(handler2);
        var cached = await _service.GetImageAsync("content-123", "poster", "https://example.com/different-url.jpg", http2);
        Assert.Equal(fakeImageBytes, cached);
    }

    [Fact]
    public void DecodeThumbhash_ReturnsBytes()
    {
        var result = ThumbhashDecoder.Decode("iBgGDQAbpriMVqroOGBLh5zfiPda");
        Assert.NotNull(result);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(handler(request));
    }
}
