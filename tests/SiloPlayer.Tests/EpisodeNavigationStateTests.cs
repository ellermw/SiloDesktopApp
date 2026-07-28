using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EpisodeNavigationStateTests
{
    private static EpisodeNavigationTarget Target(string contentId)
        => new(
            contentId,
            Title: "Next",
            SeriesTitle: "Series",
            PosterUrl: null,
            Overview: null,
            AirDate: null,
            RuntimeSeconds: 1800,
            SeasonNumber: 1,
            EpisodeNumber: 2);

    [Fact]
    public void PrepareFor_UnrelatedContent_DropsPreviousEpisodeState()
    {
        var state = new EpisodeNavigationState();
        state.PrepareFor("episode-1");
        Assert.True(state.TrySetResolved("episode-1", "episode-0", Target("episode-2")));

        state.PrepareFor("movie-1");

        Assert.Equal(EpisodeNavigationSnapshot.Empty, state.Snapshot);
        Assert.False(state.HasNextFor("movie-1"));
        Assert.False(state.HasNextFor("episode-1"));
    }

    [Fact]
    public void PrepareFor_MatchingPreloadedContent_RetainsHint()
    {
        var state = new EpisodeNavigationState();
        state.SetHint("episode-1", Target("episode-2"));

        state.PrepareFor("episode-1");

        Assert.True(state.HasNextFor("episode-1"));
        Assert.Equal("episode-2", state.Snapshot.Next?.ContentId);
    }

    [Fact]
    public void HasNextFor_RejectsStaleOwner()
    {
        var state = new EpisodeNavigationState();
        state.PrepareFor("episode-1");
        Assert.True(state.TrySetResolved("episode-1", previousContentId: null, Target("episode-2")));

        Assert.True(state.HasNextFor("episode-1"));
        Assert.False(state.HasNextFor("movie-1"));
        Assert.False(state.HasNextFor(null));
    }

    [Fact]
    public void NewResolvedState_ReplacesAllMetadataAtomically()
    {
        var state = new EpisodeNavigationState();
        state.PrepareFor("episode-1");
        Assert.True(state.TrySetResolved("episode-1", "episode-0", Target("episode-2")));

        state.PrepareFor("episode-9");
        Assert.True(state.TrySetResolved("episode-9", "episode-8", next: null));

        Assert.Equal("episode-9", state.Snapshot.OwnerContentId);
        Assert.Equal("episode-8", state.Snapshot.PreviousContentId);
        Assert.Null(state.Snapshot.Next);
        Assert.False(state.HasNextFor("episode-9"));
    }

    [Fact]
    public void LateResult_FromPreviousContent_IsRejected()
    {
        var state = new EpisodeNavigationState();
        state.PrepareFor("episode-1");
        state.PrepareFor("movie-1");

        var accepted = state.TrySetResolved("episode-1", null, Target("episode-2"));

        Assert.False(accepted);
        Assert.Equal(EpisodeNavigationSnapshot.Empty, state.Snapshot);
        Assert.False(state.HasNextFor("movie-1"));
    }
}
