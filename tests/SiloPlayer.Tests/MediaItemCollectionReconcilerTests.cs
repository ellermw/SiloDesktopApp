using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class MediaItemCollectionReconcilerTests
{
    [Fact]
    public void ChangedResultReplacesOnlyThatItemWithoutResettingTheCollection()
    {
        var unchanged = Item("movie-1", "Movie 1");
        var changed = Item("movie-2", "Movie 2");
        var current = new ObservableCollection<MediaItem>([unchanged, changed]);
        var incomingChanged = Item("movie-2", "Movie 2");
        incomingChanged.PositionSeconds = 120;

        var didChange = MediaItemCollectionReconciler.Apply(
            current,
            [Item("movie-1", "Movie 1"), incomingChanged]);

        Assert.True(didChange);
        Assert.Same(unchanged, current[0]);
        Assert.Same(incomingChanged, current[1]);
    }

    [Fact]
    public void IdenticalResultsKeepMountedItemInstances()
    {
        var first = Item("movie-1", "Movie 1");
        var second = Item("movie-2", "Movie 2");
        var current = new ObservableCollection<MediaItem>([first, second]);

        var didChange = MediaItemCollectionReconciler.Apply(
            current,
            [Item("movie-1", "Movie 1"), Item("movie-2", "Movie 2")]);

        Assert.False(didChange);
        Assert.Same(first, current[0]);
        Assert.Same(second, current[1]);
    }

    private static MediaItem Item(string id, string title) => new()
    {
        ContentId = id,
        Type = "movie",
        Title = title,
    };
}
