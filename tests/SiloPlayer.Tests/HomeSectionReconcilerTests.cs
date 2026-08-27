using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class HomeSectionReconcilerTests
{
    [Fact]
    public void IdenticalPayloadKeepsTheMountedCollectionAndItemInstances()
    {
        var currentItem = Item("movie-1", "Movie", "https://images.test/poster.jpg?old-signature");
        var current = Section(currentItem);
        var incoming = Section(Item("movie-1", "Movie", "https://images.test/poster.jpg?new-signature"));
        var mountedItems = current.Items;

        var result = HomeSectionReconciler.Apply(current, incoming);

        Assert.Equal(HomeSectionChange.None, result);
        Assert.Same(mountedItems, current.Items);
        Assert.Same(currentItem, current.Items[0]);
    }

    [Fact]
    public void ChangedProgressReplacesOnlyTheChangedCard()
    {
        var first = Item("episode-1", "Episode 1");
        var second = Item("episode-2", "Episode 2");
        var current = Section(first, second);
        var incomingFirst = Item("episode-1", "Episode 1");
        incomingFirst.PositionSeconds = 420;
        incomingFirst.DurationSeconds = 1800;
        var incomingSecond = Item("episode-2", "Episode 2");

        var result = HomeSectionReconciler.Apply(current, Section(incomingFirst, incomingSecond));

        Assert.Equal(HomeSectionChange.Items, result);
        Assert.Same(incomingFirst, current.Items[0]);
        Assert.Same(second, current.Items[1]);
    }

    [Fact]
    public void NewlyScannedItemIsInsertedWithoutReplacingExistingCards()
    {
        var first = Item("movie-1", "Movie 1");
        var second = Item("movie-2", "Movie 2");
        var current = Section(first, second);
        var added = Item("movie-3", "Movie 3");

        var result = HomeSectionReconciler.Apply(
            current,
            Section(Item("movie-1", "Movie 1"), added, Item("movie-2", "Movie 2")));

        Assert.Equal(HomeSectionChange.Metadata | HomeSectionChange.Items, result);
        Assert.Same(first, current.Items[0]);
        Assert.Same(added, current.Items[1]);
        Assert.Same(second, current.Items[2]);
    }

    [Fact]
    public void SectionMetadataChangesAreReportedSeparately()
    {
        var current = Section(Item("movie-1", "Movie"));
        var incoming = Section(Item("movie-1", "Movie"));
        incoming.TotalCount = 42;

        var result = HomeSectionReconciler.Apply(current, incoming);

        Assert.Equal(HomeSectionChange.Metadata, result);
        Assert.Equal(42, current.TotalCount);
    }

    [Fact]
    public void HeroSnapshotBecomesStaleWhenAVisibleReconciledItemIsReplaced()
    {
        var first = Item("movie-1", "Movie 1");
        var changed = Item("movie-1", "Movie 1");
        changed.PositionSeconds = 120;
        var source = new[] { changed, Item("movie-2", "Movie 2") };
        var mountedSnapshot = new[] { first, source[1] };

        var isCurrent = HomeSectionReconciler.IsHeroSnapshotCurrent(
            mountedSnapshot,
            source,
            itemLimit: 2);

        Assert.False(isCurrent);
    }

    [Fact]
    public void HeroSnapshotIgnoresReconciledItemsBeyondItsVisibleLimit()
    {
        var first = Item("movie-1", "Movie 1");
        var second = Item("movie-2", "Movie 2");
        var mountedSnapshot = new[] { first, second };
        var source = new[] { first, second, Item("movie-3", "Movie 3") };

        var isCurrent = HomeSectionReconciler.IsHeroSnapshotCurrent(
            mountedSnapshot,
            source,
            itemLimit: 2);

        Assert.True(isCurrent);
    }

    private static HomeSectionWithItems Section(params MediaItem[] items) => new()
    {
        Id = "recently-added",
        SectionType = "recently_added",
        Title = "Recently Added",
        ItemLimit = 20,
        TotalCount = items.Length,
        LoadCompleted = true,
        Items = new ObservableCollection<MediaItem>(items),
    };

    private static MediaItem Item(string id, string title, string? posterUrl = null) => new()
    {
        ContentId = id,
        Type = id.StartsWith("episode", StringComparison.Ordinal) ? "episode" : "movie",
        Title = title,
        PosterUrl = posterUrl,
    };
}
