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
    public void AmbiguousLegacyMetadataUsesSidecar()
    {
        var old = Japanese with { StreamIndex = null, ContainerTrackId = null };
        Assert.Equal(0, NativeSubtitleSelection.Resolve(old, [Japanese, Japanese with { Id = 17 }], false));
    }
}
