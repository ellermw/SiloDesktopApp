using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DetailAutoServerOrderTests
{
    [Fact]
    public void LargerFileWinsWithinCutDespitePrimaryAudioCodec()
    {
        var small = File(1); small.FileSize = 10; small.CodecAudio = "truehd";
        var large = File(2); large.FileSize = 20; large.CodecAudio = "aac";
        Assert.Same(large, DetailVersionRanking.Rank([small, large], "auto"));
    }

    [Fact]
    public void FirstVisitComparesCutsWithoutAudioOrFileSize()
    {
        var special = File(1, "extended"); special.FileSize = 100; special.CodecAudio = "truehd";
        var standard = File(2); standard.FileSize = 10; standard.CodecAudio = "aac";
        Assert.Same(standard, DetailVersionRanking.Select([special, standard], null, null, "auto"));
        special.Resolution = "720p";
        Assert.Same(special, DetailVersionRanking.Select([special, standard], null, null, "720p"));
    }

    [Fact]
    public void MergedPresentationsKeepTopLevelOrderForCompleteTies()
    {
        var first = File(1); var second = File(2);
        var variants = new[] { Variant(second), Variant(first) };
        var edition = Assert.Single(DetailVersionRanking.Editions([first, second], variants));
        Assert.Equal(new[] { 1, 2 }, edition.Versions.Select(file => file.FileId));
    }

    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(0, true, 1)]
    [InlineData(42, false, 2)]
    [InlineData(42, true, 2)]
    public void LaterPartIsKeptOnlyWithSavedPosition(double position, bool played, int expected)
    {
        var first = File(1); var later = File(2);
        var multipart = Variant(first);
        multipart.Parts.Add(new() { PartIndex = 2, Versions = [later] });
        var selected = DetailVersionRanking.Select([first, later], [multipart],
            new() { LastFileId = 2, PositionSeconds = position, Played = played }, "auto");
        Assert.Equal(expected, selected?.FileId);
        Assert.Equal(new[] { 1, 2 }, DetailVersionRanking.DownloadFiles(later, [multipart], "auto").Select(file => file.FileId));
        Assert.Same(first, DetailVersionRanking.StartVersion(later, [multipart], "auto"));
    }

    [Fact]
    public void RestartDropsFileSpecificTracksButResumeRetainsThem()
    {
        var first = File(1); var later = File(2);
        var multipart = Variant(first); multipart.Parts.Add(new() { PartIndex = 2, Versions = [later] });
        var subtitle = new SubtitleTrackSignature { Source = "downloaded", Language = "es", Codec = "srt", Label = "part-two-only", Forced = true };
        var resume = DetailPlaybackIntent.Create(later, [multipart], "auto", false, 7, 4, subtitle);
        Assert.Equal(2, resume.File?.FileId); Assert.Equal(7, resume.AudioTrackIndex); Assert.Same(subtitle, resume.SubtitleSignature);
        var restart = DetailPlaybackIntent.Create(later, [multipart], "auto", true, 7, 4, subtitle);
        Assert.Equal(1, restart.File?.FileId); Assert.Null(restart.AudioTrackIndex); Assert.Null(restart.SubtitleIndex);
        Assert.Equal("es", restart.SubtitleSignature?.Language); Assert.True(restart.SubtitleSignature?.Forced);
        Assert.Null(restart.SubtitleSignature?.Label); Assert.Null(restart.SubtitleSignature?.Codec);
        Assert.Equal(-1, DetailPlaybackIntent.Create(later, [multipart], "auto", true, null, -1, null).SubtitleIndex);
    }

    private static PlaybackVariant Variant(FileVersion file) => new()
    {
        EditionKey = file.EditionKey,
        Parts = [new() { PartIndex = 1, Versions = [file] }],
    };

    private static FileVersion File(int id, string edition = "") => new()
    {
        FileId = id, EditionKey = edition, Resolution = "1080p", FileSize = 10,
    };
}
