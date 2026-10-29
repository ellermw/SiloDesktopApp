using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SubtitleSidecarFileTests
{
    [Theory]
    [InlineData("text/vtt; charset=utf-8", "https://cdn.example.test/s/1", "ass", ".vtt")]
    [InlineData("text/x-ssa", "https://silo.example.test/api/v2/stream/s/subtitles/2", null, ".ass")]
    [InlineData("application/x-subrip", "https://silo.example.test/x", null, ".srt")]
    [InlineData("application/octet-stream", "https://silo.example.test/eng.srt", null, ".srt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/3", "subrip", ".srt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/4", "webvtt", ".vtt")]
    [InlineData(null, "https://silo.example.test/api/v2/stream/s/subtitles/5", "hdmv_pgs_subtitle", null)]
    [InlineData("application/octet-stream", "https://silo.example.test/movie.sup", "pgs", null)]
    public void OnlyKnownTextSidecarsGetALocalFile(string? mediaType, string url, string? codec, string? expected)
        => Assert.Equal(expected, SubtitleSidecarFile.Extension(mediaType, new Uri(url), codec));
}
