using System.Globalization;
using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AudioTrackDisplayTextTests
{
    [Theory]
    [InlineData("ATSC A/52B (AC-3, E-AC-3)", true)]
    [InlineData("AAC (Advanced Audio Coding)", true)]
    [InlineData("MP2 (MPEG audio layer 2)", true)]
    [InlineData("PCM 32-bit floating point little-endian", true)]
    [InlineData("DTS-HD MA 5.1", true)]
    [InlineData("1", false)]
    [InlineData("French (France)", false)]
    [InlineData("Point of View", false)]
    [InlineData("", false)]
    public void DetectsFormatOnlyTitles(string title, bool expected) =>
        Assert.Equal(expected, AudioTrackDisplayText.TitleOnlyNamesFormat(title));

    [Fact]
    public void ALongRunOfLayoutTokensWithOneOtherWordIsNotAFormatTitle() =>
        Assert.False(AudioTrackDisplayText.TitleOnlyNamesFormat(string.Concat(Enumerable.Repeat("5.1 ", 5_000)) + "x"));

    [Theory]
    [InlineData("ATSC A/52B (AC-3, E-AC-3)", null, "fr", "Français", "Français")]
    [InlineData("", "PCM 32-bit floating point little-endian", "ja", "Japanese", "Japanese")]
    [InlineData("DTS-HD MA · 5.1", "Commentary", "ja", "Japanese", "Japanese")]
    [InlineData("AAC", null, null, "", "AAC")]
    [InlineData(null, "  Director's commentary (AAC)  ", "en", "English", "Director's commentary (AAC)")]
    [InlineData("1", null, "en", "English", "1")]
    [InlineData("French (France)", null, "fr", "French", "French (France)")]
    [InlineData(null, "", "en", "English", "English")]
    [InlineData(" ", null, null, "", "Track 3")]
    public void AudioMenuNamesTheTrack(string? title, string? embedded, string? language, string languageName, string expected)
    {
        var track = new AudioTrackInfo { Title = title, EmbeddedTitle = embedded, Language = language };
        Assert.Equal(expected, AudioTrackDisplayText.MenuTitle(track, languageName, 3));
    }

    // Official mediaFormat.test.ts (#4589): FFprobe's long names name a format; a numeric, real or format-containing title is kept.
    [Theory]
    [InlineData("ATSC A/52B (AC-3, E-AC-3)", true)]
    [InlineData("AAC (Advanced Audio Coding)", true)]
    [InlineData("MP2 (MPEG audio layer 2)", true)]
    [InlineData("PCM 32-bit floating point little-endian", true)]
    [InlineData("DTS-HD MA 5.1", true)]
    [InlineData("1", false)]
    [InlineData("French (France)", false)]
    [InlineData("Point of View", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ATitleThatOnlyNamesAnAudioFormatIsTold(string? title, bool expected)
        => Assert.Equal(expected, AudioTrackDisplayText.TitleOnlyNamesAudioFormat(title));

    [Fact]
    public void ALongTitleOfRepeatedLayoutsIsCheckedWithoutBacktracking()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(AudioTrackDisplayText.TitleOnlyNamesAudioFormat(string.Concat(Enumerable.Repeat("5.1", 5000)) + "x"));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1));
    }

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
