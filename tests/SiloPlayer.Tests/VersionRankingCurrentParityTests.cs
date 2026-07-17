using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class VersionRankingCurrentParityTests
{
    [Fact]
    public void PlaybackVariantsPreferStandardBeforeTheatricalAndSpecialEditions()
    {
        var standard = Variant("standard", 1, Version(1, "1080p", "standard"));
        var theatrical = Variant("theatrical", 2, Version(2, "2160p", "theatrical"));
        var directors = Variant("directors_cut", 3, Version(3, "2160p", "directors_cut"));
        var versions = new[] { standard.Parts[0].Versions[0], theatrical.Parts[0].Versions[0], directors.Parts[0].Versions[0] };

        var selected = VersionRanking.SelectDefaultPlaybackVariantVersion(
            versions,
            [directors, theatrical, standard],
            userData: null,
            qualityPreference: null);

        Assert.Equal(1, selected?.FileId);
    }

    [Fact]
    public void EffectiveEditionKeyWinsAndUsesThatVariantsDefaultFile()
    {
        var standard = Variant("standard", 1, Version(1, "2160p", "standard"));
        var imax = Variant("imax", 7, Version(7, "1080p", "imax"));
        var versions = new[] { standard.Parts[0].Versions[0], imax.Parts[0].Versions[0] };

        var selected = VersionRanking.SelectDefaultPlaybackVariantVersion(
            versions,
            [standard, imax],
            userData: null,
            qualityPreference: null,
            preferredEditionKey: "imax");

        Assert.Equal(7, selected?.FileId);
    }

    [Fact]
    public void LastFileIsRestoredOnlyInsideTheSelectedEdition()
    {
        var standard = Variant("standard", 1, Version(1, "1080p", "standard"));
        var imax = Variant("imax", 7, Version(7, "2160p", "imax"), Version(8, "1080p", "imax"));
        var versions = standard.Parts[0].Versions.Concat(imax.Parts[0].Versions).ToList();

        var selected = VersionRanking.SelectDefaultPlaybackVariantVersion(
            versions,
            [standard, imax],
            new WatchUserData { LastFileId = 8, LastEditionKey = "imax" },
            qualityPreference: null);

        Assert.Equal(8, selected?.FileId);
    }

    [Fact]
    public void SelectedFileSummaryUsesItsHighestRankedAudioTrack()
    {
        var selected = new FileVersion
        {
            CodecAudio = "aac",
            AudioTracks =
            [
                new AudioTrackInfo { Codec = "aac" },
                new AudioTrackInfo { Codec = "truehd" },
            ],
        };

        var attributes = VersionRanking.PickBestAttributes([selected], qualityPreference: null);

        Assert.Equal("TrueHD", attributes?.AudioLabel);
    }

    private static PlaybackVariant Variant(string editionKey, int defaultFileId, params FileVersion[] versions) => new()
    {
        VariantId = editionKey,
        EditionKey = editionKey,
        PartCount = 1,
        DefaultFileId = defaultFileId,
        Parts =
        [
            new PlaybackVariantPart
            {
                PartIndex = 0,
                DefaultFileId = defaultFileId,
                Versions = versions.ToList(),
            },
        ],
    };

    private static FileVersion Version(int id, string resolution, string editionKey) => new()
    {
        FileId = id,
        Resolution = resolution,
        EditionKey = editionKey,
        CodecVideo = "hevc",
        CodecAudio = "truehd",
        Hdr = resolution == "2160p",
    };
}
