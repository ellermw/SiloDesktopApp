using ContinuumPlayer.Core.Models.Playback;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.Core.Tests.Services;

public class VersionRankingTests
{
    // ─── mapAudioLabel ───────────────────────────────────────────────────

    [Theory]
    [InlineData("dts-hd ma", "DTS-HD")]
    [InlineData("dts:x", "DTS-HD")]
    [InlineData("dts",    "DTS")]
    [InlineData("atmos",  "Atmos")]
    [InlineData("truehd", "TrueHD")]
    [InlineData("eac3",   "EAC3")]
    [InlineData("e-ac-3", "EAC3")]
    [InlineData("aac",    "AAC")]
    [InlineData("flac",   "FLAC")]
    [InlineData("opus",   "OPUS")] // Unknown codecs pass through uppercase.
    public void MapAudioLabel_MapsKnownCodecs(string codec, string expected)
    {
        Assert.Equal(expected, VersionRanking.MapAudioLabel(codec));
    }

    [Fact]
    public void MapAudioLabel_EmptyReturnsEmpty()
    {
        Assert.Equal("", VersionRanking.MapAudioLabel(""));
        Assert.Equal("", VersionRanking.MapAudioLabel(null));
    }

    // ─── scoring ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2160p", 4)]
    [InlineData("4k",    4)]
    [InlineData("1080p", 2)]
    [InlineData("480p",  0)]
    [InlineData("bogus", -1)]
    public void ResolutionScore_ReturnsRank(string res, int expected)
    {
        Assert.Equal(expected, VersionRanking.ResolutionScore(res));
    }

    [Theory]
    [InlineData("atmos",  6)]
    [InlineData("truehd", 5)]
    [InlineData("dts-hd ma", 4)]
    [InlineData("dts",    3)]
    [InlineData("aac",    0)]
    [InlineData("opus",   -1)]
    public void AudioScore_ReturnsRank(string codec, int expected)
    {
        Assert.Equal(expected, VersionRanking.AudioScore(codec));
    }

    // ─── selectDefaultVersion ────────────────────────────────────────────

    [Fact]
    public void SelectDefaultVersion_PrefersLastWatchedFile()
    {
        var versions = new List<FileVersion>
        {
            Version(1, "1080p", "hevc", "aac"),
            Version(2, "2160p", "hevc", "truehd"),
            Version(3, "720p",  "h264", "aac"),
        };
        var userData = new WatchUserData { LastFileId = 3 };

        var picked = VersionRanking.SelectDefaultVersion(versions, userData, null);

        Assert.NotNull(picked);
        Assert.Equal(3, picked!.FileId);
    }

    [Fact]
    public void SelectDefaultVersion_FallsBackToBestAttributes_WhenNoLastFileId()
    {
        var versions = new List<FileVersion>
        {
            Version(1, "1080p", "hevc", "aac"),
            Version(2, "2160p", "hevc", "truehd", hdr: true),
            Version(3, "720p",  "h264", "aac"),
        };

        var picked = VersionRanking.SelectDefaultVersion(versions, userData: null, qualityPreference: null);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.FileId); // highest res + hdr + best audio
    }

    [Fact]
    public void SelectDefaultVersion_QualityPreference_CapsResolution()
    {
        var versions = new List<FileVersion>
        {
            Version(1, "2160p", "hevc", "truehd"),
            Version(2, "1080p", "hevc", "aac"),
            Version(3, "720p",  "h264", "aac"),
        };

        var picked = VersionRanking.SelectDefaultVersion(versions, userData: null, qualityPreference: "1080p");

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.FileId); // 2160p filtered out by preference cap
    }

    [Fact]
    public void SelectDefaultVersion_SingleVersion_AlwaysReturnsIt()
    {
        var versions = new List<FileVersion> { Version(99, "720p", "h264", "aac") };

        var picked = VersionRanking.SelectDefaultVersion(versions, userData: null, qualityPreference: null);

        Assert.NotNull(picked);
        Assert.Equal(99, picked!.FileId);
    }

    [Fact]
    public void SelectDefaultVersion_EmptyList_ReturnsNull()
    {
        var picked = VersionRanking.SelectDefaultVersion(new List<FileVersion>(), null, null);
        Assert.Null(picked);
    }

    [Fact]
    public void SelectDefaultVersion_LastFileIdNotInList_FallsBackToBest()
    {
        var versions = new List<FileVersion>
        {
            Version(1, "1080p", "hevc", "aac"),
            Version(2, "2160p", "hevc", "truehd"),
        };
        var userData = new WatchUserData { LastFileId = 999 };

        var picked = VersionRanking.SelectDefaultVersion(versions, userData, null);

        Assert.NotNull(picked);
        Assert.Equal(2, picked!.FileId);
    }

    private static FileVersion Version(int id, string res, string codecV, string codecA, bool hdr = false)
        => new()
        {
            FileId = id,
            Resolution = res,
            CodecVideo = codecV,
            CodecAudio = codecA,
            Hdr = hdr,
        };
}
