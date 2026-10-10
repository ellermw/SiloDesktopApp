using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class PlaybackPartSequenceTests
{
    [Fact]
    public void PartsFollowDeclaredOrderInsideTheMatchingEditionAndPreferItsDefaultVersion()
    {
        var detail = new WatchDetailResponse { PlaybackVariants =
        [
            new() { Parts = [new() { PartIndex = 0, Versions = [new() { FileId = 99 }] }, new() { PartIndex = 1, DefaultFileId = 100 }] },
            new() { Parts = [new() { PartIndex = 2, DefaultFileId = 42, Versions = [new() { FileId = 41 }, new() { FileId = 42 }] }, new() { PartIndex = 1, Versions = [new() { FileId = 10 }, new() { FileId = 11 }] }] }
        ] };
        Assert.Equal(42, PlaybackPartSequence.NextFileId(detail, 11));
        Assert.Null(PlaybackPartSequence.NextFileId(detail, 42));
        Assert.Null(PlaybackPartSequence.NextFileId(detail, 500));
    }

    [Fact]
    public void MissingDefaultUsesFirstNextPartVersionAndAnEmptyNextPartStopsSequence()
    {
        var detail = new WatchDetailResponse { PlaybackVariants = [new() { Parts =
            [new() { PartIndex = 0, Versions = [new() { FileId = 10 }] }, new() { PartIndex = 1, Versions = [new() { FileId = 20 }] }, new() { PartIndex = 2 }, new() { PartIndex = 3, DefaultFileId = 40 }] }] };
        Assert.Equal(20, PlaybackPartSequence.NextFileId(detail, 10));
        Assert.Null(PlaybackPartSequence.NextFileId(detail, 20));
        Assert.Null(PlaybackPartSequence.NextFileId(detail, null));
    }
}
