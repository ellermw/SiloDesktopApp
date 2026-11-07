using System.Globalization;
using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AudioTrackDisplayTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void MissingOrInvalidBitrateOmitsTheMenuDetail(int? kbps)
        => Assert.Equal("", AudioTrackDisplayText.FormatBitrate(kbps));

    [Theory]
    [InlineData(640)]
    [InlineData(6144)]
    [InlineData(int.MaxValue)]
    public void SuppliedKilobitsStayKilobits(int kbps)
        => Assert.Equal(kbps.ToString("0.#", CultureInfo.CurrentCulture) + " kbps", AudioTrackDisplayText.FormatBitrate(kbps));

    [Fact]
    public async Task ActualWatchResponsePreservesSourceAudioBitrateForMenuFormatting()
    {
        using var http = new HttpClient(new WatchWire());
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://audio-bitrate.fixture.invalid");
        client.SetProfile("synthetic");
        var detail = await new PlaybackApi(client).GetWatchDetailAsync("synthetic-video");
        var track = Assert.Single(Assert.Single(detail.Versions).AudioTracks!);
        Assert.Equal(640, track.Bitrate);
        Assert.Equal("640 kbps", AudioTrackDisplayText.FormatBitrate(track.Bitrate));
        Assert.Equal(48000, track.SampleRate);
    }

    private sealed class WatchWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("audio-bitrate.fixture.invalid", request.RequestUri!.Host);
            Assert.Equal("/api/v2/watch/synthetic-video", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"content_id":"synthetic-video","type":"movie","versions":[{"file_id":42,"audio_tracks":[{"codec":"ac3","bitrate":640,"sample_rate":48000}]}]}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
