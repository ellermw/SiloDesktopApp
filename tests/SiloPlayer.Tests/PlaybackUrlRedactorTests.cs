using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackUrlRedactorTests
{
    [Theory]
    [InlineData(
        "https://node.example/stream/transcode/signed.jwt.value/master.m3u8?token=user-token",
        "https://node.example/stream/transcode/<redacted>/master.m3u8")]
    [InlineData(
        "https://node.example/stream/direct/opaque-signature?st=secret",
        "https://node.example/stream/direct/<redacted>")]
    [InlineData(
        "https://silo.example/api/v1/stream/session-uuid/subtitles/2?token=secret",
        "https://silo.example/api/v1/stream/<redacted>/subtitles/2")]
    [InlineData(
        "/api/v1/playback/transcode/session-uuid/master.m3u8?access_token=secret",
        "/api/v1/playback/transcode/<redacted>/master.m3u8")]
    [InlineData(
        "http://127.0.0.1:4567/master.m3u8?k=loopback-secret#fragment",
        "http://127.0.0.1:4567/master.m3u8")]
    public void Redact_RemovesPlaybackCredentialsAndQueryValues(string input, string expected)
    {
        Assert.Equal(expected, PlaybackUrlRedactor.Redact(input));
    }

    [Fact]
    public void Redact_DoesNotExposeMalformedUrlQuery()
    {
        Assert.Equal("not a url", PlaybackUrlRedactor.Redact("not a url?token=secret"));
    }
}
