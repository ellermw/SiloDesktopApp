using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class NativeSubtitleSelectionTests
{
    private static readonly NativeSubtitleTrack English = new(4, 7, "20", "subrip", "eng", "English");
    private static readonly NativeSubtitleTrack Japanese = new(9, 12, "41", "ass", "jpn", "Japanese");

    [Fact]
    public void ContainerIdentitySelectsSparseActualId() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese with { Id = 1 }, [English, Japanese], false));

    [Fact]
    public void StreamIdentitySelectsAbsoluteIndexRatherThanCombinedOrdinal() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese with { Id = 1, ContainerTrackId = null }, [English, Japanese], true));

    [Fact]
    public void BuiltinDemuxerCannotAttestApproximateFfmpegIndex() =>
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese with { ContainerTrackId = null }, [English, Japanese], false));

    [Fact]
    public void AbsentAndDuplicateIdentityCannotSelectAnotherTrack()
    {
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese with { ContainerTrackId = "99" }, [English, Japanese], false));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese, [Japanese, Japanese with { Id = 17 }], false));
    }

    [Fact]
    public void ConflictingIdentityCannotSelectAnotherTrack() =>
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese with { StreamIndex = 7 }, [English, Japanese], true));

    [Fact]
    public void OlderServerUsesUniqueLoadedMetadataRatherThanOrdinal() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese with { Id = 1, StreamIndex = null, ContainerTrackId = null }, [English, Japanese], false));

    [Fact]
    public void LavfIndexWorksWithoutSrcId() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese, [Japanese with { ContainerTrackId = null }], true));

    [Fact]
    public void LavfSrcIdInAnotherNamespaceCannotVetoFfmpegIndex() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese, [Japanese with { ContainerTrackId = "12" }],
            trustworthyStreamIndex: true, trustworthyContainerTrackId: false));

    [Fact]
    public void UnknownDemuxerCannotAttestContainerNumber() =>
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese, [Japanese],
            trustworthyStreamIndex: false, trustworthyContainerTrackId: false));

    [Theory]
    [InlineData("en", "eng")]
    [InlineData("", null)]
    public void LegacyMetadataNormalizesLanguageAndAllowsUntitledTracks(string language, string? loadedLanguage)
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = null, Language = language, Title = null };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English with { Language = loadedLanguage, Title = null }], false));
    }

    [Fact]
    public void DisplayLabelDoesNotVetoUniqueMetadataMatch()
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = null, Title = "Server display name" };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English with { Title = null }], false));
    }

    [Fact]
    public void RawTitleCanDisambiguateOtherwiseIdenticalMetadata()
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = null };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English, English with { Id = 17, Title = "Commentary" }], false));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Title = null },
            [English with { Title = null }, English with { Id = 17, Title = null }], false));
    }

    [Fact]
    public void AmbiguousLegacyMetadataUsesSidecar()
    {
        var old = Japanese with { StreamIndex = null, ContainerTrackId = null };
        Assert.Equal(0, NativeSubtitleSelection.Resolve(old, [Japanese, Japanese with { Id = 17 }], false));
    }
}
