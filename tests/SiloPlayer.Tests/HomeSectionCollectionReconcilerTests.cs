using System.Collections.ObjectModel;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class HomeSectionCollectionReconcilerTests
{
    [Fact]
    public void ApplyReusesMountedSectionsAndItemsWhileUpdatingTheirState()
    {
        var mountedItem = Item("movie-1", favorite: false);
        var mountedSection = Section("recent", "Recently Added", mountedItem);
        var mounted = new ObservableCollection<HomeSectionWithItems> { mountedSection };

        var changed = HomeSectionCollectionReconciler.Apply(
            mounted,
            [Section("recent", "Recently Added", Item("movie-1", favorite: true))]);

        Assert.True(changed);
        Assert.Same(mountedSection, mounted[0]);
        Assert.NotSame(mountedItem, mounted[0].Items[0]);
        Assert.True(mounted[0].Items[0].UserState!.IsFavorite);
    }

    [Fact]
    public void ApplyMovesExistingSectionsAndRemovesRowsMissingFromRefresh()
    {
        var first = Section("first", "First", Item("one"));
        var removed = Section("removed", "Removed", Item("two"));
        var last = Section("last", "Last", Item("three"));
        var mounted = new ObservableCollection<HomeSectionWithItems> { first, removed, last };

        var changed = HomeSectionCollectionReconciler.Apply(
            mounted,
            [Section("last", "Last", Item("three")), Section("first", "First", Item("one"))]);

        Assert.True(changed);
        Assert.Equal(["last", "first"], mounted.Select(section => section.Id));
        Assert.Same(last, mounted[0]);
        Assert.Same(first, mounted[1]);
        Assert.DoesNotContain(removed, mounted);
    }

    [Fact]
    public void ApplyInsertsNewSectionsAtTheServerDefinedPosition()
    {
        var first = Section("first", "First", Item("one"));
        var last = Section("last", "Last", Item("three"));
        var mounted = new ObservableCollection<HomeSectionWithItems> { first, last };

        var changed = HomeSectionCollectionReconciler.Apply(
            mounted,
            [first, Section("middle", "Middle", Item("two")), last]);

        Assert.True(changed);
        Assert.Equal(["first", "middle", "last"], mounted.Select(section => section.Id));
        Assert.Same(first, mounted[0]);
        Assert.Same(last, mounted[2]);
    }

    private static HomeSectionWithItems Section(
        string id,
        string title,
        params MediaItem[] items) => new()
    {
        Id = id,
        SectionType = "recently_added",
        Title = title,
        ItemLimit = 20,
        TotalCount = items.Length,
        LoadCompleted = true,
        Items = new ObservableCollection<MediaItem>(items),
    };

    private static MediaItem Item(string id, bool favorite = false) => new()
    {
        ContentId = id,
        Type = "movie",
        Title = id,
        UserState = new UserState { IsFavorite = favorite },
    };
}
