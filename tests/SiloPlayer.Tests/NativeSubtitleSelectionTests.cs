using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class NativeSubtitleSelectionTests
{
    [Theory]
    [InlineData("pt-BR", "pt-PT")]
    [InlineData("zh-Hant", "zh-Hans")]
    [InlineData("en-GB", "eng")]
    [InlineData("eng", "en-GB")]
    [InlineData("pt", "por-BR")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    public void LegacyMetadataCannotDiscardExplicitLanguageSubtags(string requestedLanguage, string loadedLanguage)
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = null, Language = requestedLanguage, Title = null };
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested, [English with { Language = loadedLanguage, Title = null }], false));
    }

    [Theory]
    [InlineData("eng-GB", "en-gb")]
    [InlineData("POR_br", "pt-BR")]
    [InlineData("zho-Hant-TW", "zh-hant-tw")]
    public void LegacyMetadataKeepsIsoAliasesWithinTheSameLanguageSubtags(string requestedLanguage, string loadedLanguage)
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = null, Language = requestedLanguage, Title = null };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English with { Language = loadedLanguage, Title = null }], false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoritativeIdentityDoesNotDependOnLanguageMetadata(bool useFfmpegIndex)
    {
        var requested = English with { Language = "en-GB" };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English with { Language = "eng" }], useFfmpegIndex));
    }

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
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese with { ContainerTrackId = null },
            [English, Japanese with { Language = "eng" }], false));

    [Fact]
    public void BuiltinContainerIdentityIgnoresApproximateFfmpegIndex() =>
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese with { StreamIndex = 7 }, [English, Japanese], false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnattestedStreamIndexUsesUniqueMetadataNotApproximateIndex(bool trustworthyContainerTrackId)
    {
        var requested = Japanese with { Id = 1, StreamIndex = 7, ContainerTrackId = null };
        Assert.Equal(9, NativeSubtitleSelection.Resolve(requested, [English, Japanese], false, trustworthyContainerTrackId));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested, [Japanese, Japanese with { Id = 17 }], false, trustworthyContainerTrackId));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Codec = null }, [Japanese], false, trustworthyContainerTrackId));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Language = "ja-JP" }, [Japanese], false, trustworthyContainerTrackId));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Forced = true }, [Japanese], false, trustworthyContainerTrackId));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { HearingImpaired = true }, [Japanese], false, trustworthyContainerTrackId));
    }

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
    public void UnknownDemuxerCannotAttestContainerNumber()
    {
        // Matching IDs cannot override mismatched or ambiguous metadata.
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese, [Japanese with { Language = "eng" }],
            trustworthyStreamIndex: false, trustworthyContainerTrackId: false));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(Japanese, [Japanese, Japanese with { Id = 17, ContainerTrackId = "99" }],
            trustworthyStreamIndex: false, trustworthyContainerTrackId: false));
        Assert.Equal(9, NativeSubtitleSelection.Resolve(Japanese with { ContainerTrackId = "999", StreamIndex = 7 }, [English, Japanese],
            trustworthyStreamIndex: false, trustworthyContainerTrackId: false));
    }

    [Fact]
    public void UnattestedContainerOnlyRowUsesUniqueMetadataNotSrcId()
    {
        var requested = English with { StreamIndex = null, ContainerTrackId = "999" };
        Assert.Equal(4, NativeSubtitleSelection.Resolve(requested, [English, Japanese],
            trustworthyStreamIndex: true, trustworthyContainerTrackId: false));
    }

    [Fact]
    public void UnattestedContainerNumberCannotDisambiguateDuplicateMetadata()
    {
        var requested = English with { StreamIndex = null, Title = null };
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested, [English, English with { Id = 17, ContainerTrackId = "99" }],
            trustworthyStreamIndex: true, trustworthyContainerTrackId: false));
    }

    [Fact]
    public void UnattestedContainerOnlyRowStillRequiresCompleteMatchingMetadata()
    {
        var requested = English with { StreamIndex = null };
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Codec = null }, [English], true, false));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Language = "en-GB" }, [English], true, false));
        Assert.Equal(0, NativeSubtitleSelection.Resolve(requested with { Forced = true }, [English], true, false));
    }

    [Theory]
    [InlineData("en", "eng")]
    [InlineData("", null)]
    [InlineData("und", null)]
    [InlineData(" UND ", "")]
    [InlineData("", "und")]
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
