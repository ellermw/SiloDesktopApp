using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DetailVersionAutoTests
{
    [Theory]
    [InlineData("1080p", 2)]
    [InlineData("720p", 2)]
    [InlineData("auto", 1)]
    public void AutoRespectsCapInsteadOfRestoringHistory(string quality, int expected)
    {
        var files = new[] { File(1, "2160p"), File(2, "1080p") };
        Assert.Equal(expected, Select(files, null, new() { LastFileId = 1 }, quality)?.FileId);
    }

    [Fact]
    public void EmptyEditionIsAnExplicitChoice()
    {
        var files = new[] { File(1, "1080p"), File(2, "2160p", "extended") };
        Assert.Equal(1, Select(files, null, new() { LastEditionKey = "extended", LastFileId = 2 }, "auto", "")?.FileId);
    }

    [Fact]
    public void ActualFilesAreRankedWithoutCombiningAttributesAcrossFiles()
    {
        var first = File(1, "2160p"); first.CodecAudio = "aac";
        var second = File(2, "1080p"); second.Hdr = true;
        Assert.Equal(2, Select([first, second], null, null, "1080p")?.FileId);
    }

    [Theory]
    [InlineData("8k")]
    [InlineData("4320p")]
    public void EightKIsKnownAndAboveFourK(string resolution)
    {
        var files = new[] { File(1, "2160p"), File(2, resolution) };
        Assert.Equal(2, Select(files, null, null, "auto")?.FileId);
        Assert.Equal(1, Select(files, null, null, "2160p")?.FileId);
    }

    [Fact]
    public void UnknownResolutionIsFallbackOnly()
    {
        var files = new[] { File(1, ""), File(2, "2160p") };
        Assert.Equal(2, Select(files, null, null, "720p")?.FileId);
        Assert.Equal(1, Select([files[0]], null, null, "720p")?.FileId);
    }

    [Fact]
    public void HistoryInLaterPartSelectsEditionButStartsAtFirstPart()
    {
        var first = File(1, "1080p"); var later = File(2, "2160p");
        var variant = new PlaybackVariant { EditionKey = "", Parts = [
            new() { PartIndex = 1, Versions = [first], DefaultFileId = 1 },
            new() { PartIndex = 2, Versions = [later], DefaultFileId = 2 }] };
        Assert.Equal(1, Select([first, later], [variant], new() { LastFileId = 2 }, "auto")?.FileId);
    }

    [Fact]
    public void ServerDefaultDoesNotOverrideQualityCap()
    {
        var files = new[] { File(1, "2160p"), File(2, "1080p") };
        var variant = new PlaybackVariant { EditionKey = "", Parts = [new() { PartIndex = 1, Versions = files.ToList(), DefaultFileId = 1 }] };
        Assert.Equal(2, Select(files, [variant], null, "1080p")?.FileId);
    }

    [Fact]
    public void QualityUpdatesStayWithinEditionAndAutoCanReplaceManualPin()
    {
        var files = new[] { File(1, "2160p"), File(2, "1080p"), File(3, "8k", "extended") };
        var state = new DetailVersionSelection();
        state.SetQuality("1080p-high", settled: true);
        Assert.Equal(2, state.Resolve(files, null, null)?.FileId);
        Assert.Equal("", state.EditionKey);
        state.SetQuality("auto", settled: true);
        Assert.Equal(1, state.Resolve(files, null, null)?.FileId);
        state.SelectFile(files[1]);
        Assert.Equal(2, state.Resolve(files, null, null)?.FileId);
        state.SelectAuto();
        Assert.Null(state.ManualFileId);
        Assert.Equal(1, state.Resolve(files, null, null)?.FileId);
        state.SelectEdition("extended");
        Assert.Null(state.ManualFileId);
        Assert.Equal(3, state.Resolve(files, null, null)?.FileId);
    }

    [Fact]
    public void ProvisionalSettingsDoNotFixEditionBeforeSuccessfulRetry()
    {
        var files = new[] { File(1, "1080p"), File(2, "2160p", "extended") };
        var state = new DetailVersionSelection();
        state.SetQuality("1080p", settled: false);
        Assert.Equal(1, state.Resolve(files, null, null)?.FileId);
        Assert.Null(state.EditionKey);
        state.SetQuality("auto", settled: true);
        Assert.Equal(2, state.Resolve(files, null, null)?.FileId);
        Assert.Equal("extended", state.EditionKey);
        state.SetQuality("1080p", settled: true);
        Assert.Equal(2, state.Resolve(files, null, null)?.FileId);
    }

    [Fact]
    public void GroupedEditionsOnlyOfferFirstPartsButDownloadsKeepPartOrder()
    {
        var single = File(1, "720p"); var first = File(2, "1080p");
        var later = File(3, "720p"); var laterLarge = File(4, "2160p");
        var extended = File(5, "2160p", "extended");
        var variants = new List<PlaybackVariant>
        {
            new() { EditionKey = "", Parts = [new() { PartIndex = 0, Versions = [single] }] },
            new() { EditionKey = "", Parts = [new() { PartIndex = 2, Versions = [laterLarge, later] }, new() { PartIndex = 1, Versions = [first] }] },
            new() { EditionKey = "extended", Parts = [new() { PartIndex = 0, Versions = [extended] }] },
        };
        var editions = DetailVersionRanking.Editions([single, first, later, laterLarge, extended], variants);
        Assert.Equal(2, editions.Count);
        Assert.Equal(new[] { 1, 2 }, editions[0].Versions.Select(file => file.FileId));
        Assert.Equal(new[] { 2, 3 }, DetailVersionRanking.DownloadFiles(first, variants, "1080p").Select(file => file.FileId));
    }

    [Fact]
    public void CardResumeKeepsHistoryWhileDetailAutoRanksWithinItsEdition()
    {
        var files = new[] { File(1, "2160p"), File(2, "1080p") };
        var history = new WatchUserData { LastFileId = 2 };
        Assert.Equal(2, VersionRanking.SelectDefaultVersion(files, history, "auto")?.FileId);
        Assert.Equal(1, DetailVersionRanking.Select(files, null, history, "auto")?.FileId);
    }

    [Theory]
    [InlineData("hd", 2)]
    [InlineData("fhd", 3)]
    [InlineData("sd", 1)]
    [InlineData("480p-low", 1)]
    [InlineData("360p", 1)]
    public void ProfileAliasesAndLowTiersRemainCapped(string quality, int expected)
    {
        var state = new DetailVersionSelection();
        state.SetQuality(quality, false);
        Assert.Equal(expected, state.Resolve([File(1, "480p"), File(2, "720p"), File(3, "1080p")], null, null)?.FileId);
        Assert.Null(state.EditionKey);
    }

    [Fact]
    public void EmptyHistoryEditionAndClosestAboveCapAreRespected()
    {
        var files = new[] { File(1, "2160p"), File(2, "1080p"), File(3, "720p", "extended") };
        Assert.Equal(2, Select(files, null, new() { LastEditionKey = "" }, "720p")?.FileId);
    }

    private static FileVersion? Select(IReadOnlyList<FileVersion> files, IReadOnlyList<PlaybackVariant>? variants,
        WatchUserData? history, string quality, string? edition = null)
        => DetailVersionRanking.Select(files, variants, history, quality, edition);

    private static FileVersion File(int id, string resolution, string edition = "")
        => new() { FileId = id, Resolution = resolution, EditionKey = edition, CodecAudio = "truehd" };
}
