using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackDeliveryUrlTests
{
    [Theory]
    [InlineData("/stream/session?st=a%2Fb%2Bc", "https://server.test/api/v2/stream/session?st=a%2Fb%2Bc")]
    [InlineData("/api/v1/stream/session/subtitles/0?st=a%2Fb", "https://server.test/api/v2/stream/session/subtitles/0?st=a%2Fb")]
    [InlineData("/api/v2/stream/session", "https://server.test/api/v2/stream/session")]
    [InlineData("/api/v1/plugins/example/proxy/stream?st=raw%2Fquery", "https://server.test/api/v1/plugins/example/proxy/stream?st=raw%2Fquery")]
    [InlineData("https://node.test/api/v1/stream/session?st=a%2fb%2Bc", "https://node.test/api/v1/stream/session?st=a%2fb%2Bc")]
    public void MapsOnlyLocalDeliveryAndPreservesSignedQueries(string path, string expected)
        => Assert.Equal(expected, PlaybackDeliveryUrl.Resolve("https://server.test", path));
}
