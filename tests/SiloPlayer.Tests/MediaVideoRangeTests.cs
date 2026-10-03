using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaVideoRangeTests
{
    [Fact]
    public void CanonicalLanguageCatalogMatchesCurrentWebUi()
    {
        Assert.Equal(46, MediaLanguageCatalog.All.Count);
        Assert.Contains(MediaLanguageCatalog.All, language => language.Code == "pt-BR" && language.Label == "Portuguese (Brazil)");
        Assert.Contains(MediaLanguageCatalog.All, language => language.Code == "en-GB" && language.Label == "English (United Kingdom)");
        Assert.Contains(MediaLanguageCatalog.All, language => language.Code == "zh-Hant" && language.Label == "Chinese (Traditional)");
        Assert.Equal("Dutch", MediaLanguageCatalog.Label("nld"));
        Assert.Equal("Portuguese", MediaLanguageCatalog.Label("pt-BR"));
        Assert.Equal("uk", MediaLanguageCatalog.Normalize("ukr"));
    }

    [Theory]
    [InlineData("DOVIWithHDR10", false, "DV HDR10")]
    [InlineData("DOVIWithHLG", false, "DV HLG")]
    [InlineData("HDR10", false, "HDR10")]
    [InlineData("HLG", false, "HLG")]
    public void LabelUsesCurrentWebUiDynamicRangeVocabulary(
        string rangeType,
        bool hdr10Plus,
        string expected)
    {
        var version = new FileVersion
        {
            VideoTracks =
            [
                new VersionVideoTrack
                {
                    VideoRangeType = rangeType,
                    Hdr10Plus = hdr10Plus,
                },
            ],
        };

        Assert.Equal(expected, MediaVideoRange.Label(version));
    }

    [Fact]
    public void LabelCombinesDolbyVisionAndHdr10Plus()
    {
        var version = new FileVersion
        {
            VideoTracks =
            [
                new VersionVideoTrack
                {
                    DvProfile = 8,
                    Hdr10Plus = true,
                },
            ],
        };

        Assert.Equal("DV HDR10+", MediaVideoRange.Label(version));
    }

    [Fact]
    public void LabelFallsBackToGenericHdrWhenTracksHaveNoSpecificMetadata()
    {
        Assert.Equal("HDR", MediaVideoRange.Label(new FileVersion { Hdr = true }));
        Assert.Equal("", MediaVideoRange.Label(new FileVersion()));
        Assert.Equal("", MediaVideoRange.Label(null));
    }
}
