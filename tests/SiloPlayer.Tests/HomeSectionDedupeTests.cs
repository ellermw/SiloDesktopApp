using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class HomeSectionDedupeTests
{
    [Fact]
    public void LaterResponseIsSuppressedWhenEarlierLayoutRowArrivesAndCanBeRestored()
    {
        var early = Row("early", "featured", "a");
        var late = Row("late", "recipe", "a", "b");
        late.TotalCount = 10;
        HomeSection[] layout = [new() { Id = "early" }, new() { Id = "late" }];
        var raw = new Dictionary<string, HomeSectionWithItems> { [late.Id] = late };
        var alone = HomeSectionDedupe.Project(layout, raw, new HashSet<string> { "recipe" });
        Assert.Same(late, alone["late"]);
        raw[early.Id] = early;
        var projected = HomeSectionDedupe.Project(layout, raw, new HashSet<string> { "recipe" });
        Assert.Equal(["b"], projected["late"].Items.Select(i => i.ContentId));
        Assert.Equal(9, projected["late"].TotalCount);
        Assert.Equal(["a", "b"], raw["late"].Items.Select(i => i.ContentId));
        raw[early.Id] = Row("early", "featured", "c");
        Assert.Same(late, HomeSectionDedupe.Project(layout, raw, new HashSet<string> { "recipe" })["late"]);
    }

    [Fact]
    public void LayoutOrderRatherThanMapInsertionOrFeaturedStatusWins()
    {
        var first = Row("first", "recipe", "a");
        var second = Row("second", "recipe", "a", "b"); second.Featured = true;
        HomeSection[] layout = [new() { Id = "first" }, new() { Id = "second" }];
        var raw = new Dictionary<string, HomeSectionWithItems> { ["second"] = second, ["first"] = first };
        var result = HomeSectionDedupe.Project(layout, raw, new HashSet<string> { "recipe" });
        Assert.Same(first, result["first"]);
        Assert.Equal(["b"], result["second"].Items.Select(i => i.ContentId));
    }

    [Fact]
    public void OnlyConfiguredTypesDropDuplicatesAndUnidentifiedItemsAreKept()
    {
        var first = Row("first", "plain", "a");
        var second = Row("second", "plain", "a", "b");
        var third = Row("third", "recipe", "a", "b", "", "c", "c");
        HomeSection[] layout = [new() { Id = "first" }, new() { Id = "second" }, new() { Id = "third" }];
        var raw = new Dictionary<string, HomeSectionWithItems> { ["first"] = first, ["second"] = second, ["third"] = third };
        var result = HomeSectionDedupe.Project(layout, raw, new HashSet<string> { "recipe" });
        Assert.Same(second, result["second"]);
        Assert.Equal(["", "c", "c"], result["third"].Items.Select(i => i.ContentId));
        Assert.Equal(3, result["third"].TotalCount);
        Assert.Same(third.Items[3], result["third"].Items[1]);
    }

    [Fact]
    public void RepeatedProjectionKeepsMountedRowsCollectionsAndCardsStable()
    {
        var a = Row("a", "plain", "shared"); var b = Row("b", "recipe", "shared", "unique");
        HomeSection[] layout = [new() { Id = "a" }, new() { Id = "b" }];
        var raw = new Dictionary<string, HomeSectionWithItems> { ["a"] = a, ["b"] = b };
        var mounted = new System.Collections.ObjectModel.ObservableCollection<HomeSectionWithItems>();
        var avoids = new HashSet<string> { "recipe" };
        HomeSectionCollectionReconciler.Apply(mounted, HomeSectionDedupe.Project(layout, raw, avoids).Values.ToList());
        var row = mounted[1]; var cards = row.Items; var card = cards[0];
        Assert.False(HomeSectionCollectionReconciler.Apply(mounted, HomeSectionDedupe.Project(layout, raw, avoids).Values.ToList()));
        Assert.Same(row, mounted[1]); Assert.Same(cards, row.Items); Assert.Same(card, row.Items[0]);
        Assert.Equal(2, raw["b"].Items.Count);
    }

    private static HomeSectionWithItems Row(string id, string type, params string[] items) => new()
    {
        Id = id, SectionType = type, LoadCompleted = true,
        Items = new(items.Select(id => new MediaItem { ContentId = id })),
    };
}
