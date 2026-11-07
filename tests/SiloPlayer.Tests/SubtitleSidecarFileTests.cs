using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleSidecarFileTests
{
    [Theory]
    [InlineData(null, "https://silo.example.test/api/subtitles/3", "hdmv_pgs_subtitle", ".sup")]
    [InlineData("application/octet-stream", "https://silo.example.test/movie.SUP", null, ".sup")]
    [InlineData(null, "https://silo.example.test/captions", "pgs", ".sup")]
    [InlineData(null, "https://silo.example.test/captions", "sup", ".sup")]
    [InlineData(null, "https://silo.example.test/eng.raw", null, ".sub")]
    [InlineData("application/x-subrip", "https://silo.example.test/captions", null, ".srt")]
    public void EverySidecarCanBeProbedFromABoundedLocalDownload(string? mediaType, string url, string? codec, string expected)
        => Assert.Equal(expected, SubtitleSidecarFile.DownloadExtension(mediaType, new Uri(url), codec));

    [Theory]
    [InlineData(".sup", 256 * 1024 * 1024)]
    [InlineData(".SUP", 256 * 1024 * 1024)]
    [InlineData(".srt", 16 * 1024 * 1024)]
    [InlineData(".ass", 16 * 1024 * 1024)]
    [InlineData(".sub", 16 * 1024 * 1024)]
    public void BitmapBudgetDoesNotRelaxTheTextOrUnknownFormatLimit(string extension, int expected)
        => Assert.Equal(expected, SubtitleSidecarFile.DownloadMaxBytes(extension));

    [Theory]
    [InlineData("text/vtt; charset=utf-8", "https://cdn.example.test/s/1", "ass", ".vtt")]
    [InlineData("text/x-ssa", "https://silo.example.test/api/v2/stream/s/subtitles/2", null, ".ass")]
    [InlineData("application/x-subrip", "https://silo.example.test/x", null, ".srt")]
    [InlineData("application/octet-stream", "https://silo.example.test/eng.srt", null, ".srt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/3", "subrip", ".srt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/4", "webvtt", ".vtt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "mov_text", ".txt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "ttml", ".ttml")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "subviewer", ".sub")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "pjs", ".pjs")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "mpl2", ".mpl2")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "jacosub", ".jss")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "realtext", ".rt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "text", ".txt")]
    [InlineData("application/ttml+xml", "https://silo.example.test/captions", null, ".ttml")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "hdmv_pgs_subtitle", null)]
    [InlineData("application/octet-stream", "https://silo.example.test/movie.sup", "pgs", null)]
    public void OnlyKnownTextSidecarsGetALocalFile(string? mediaType, string url, string? codec, string? expected)
        => Assert.Equal(expected, SubtitleSidecarFile.Extension(mediaType, new Uri(url), codec));
}
