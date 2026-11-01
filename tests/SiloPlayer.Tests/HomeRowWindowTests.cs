using System.Text.Json;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class HomeRowWindowTests
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public void PartialRowSurvivesRawSnapshotAndReconciliation()
    {
        var incoming = JsonSerializer.Deserialize<HomeSectionWithItems>("""{"id":"row","has_more":true,"items":[{"content_id":"one"}]}""", Wire)!;
        var mounted = new HomeSectionWithItems();
        HomeSectionReconciler.Apply(mounted, HomeSectionDedupe.Snapshot(incoming));
        var json = JsonSerializer.SerializeToElement(mounted, Wire);
        Assert.True(json.TryGetProperty("has_more", out var partial) && partial.GetBoolean());
    }

    [Fact]
    public void EveryRowsInitialCardsWinBeforeEarlierRowsExpansionCards()
    {
        var early = JsonSerializer.Deserialize<HomeSectionWithItems>("""{"id":"early","section_type":"recipe","items":[{"content_id":"first"},{"content_id":"shared"}]}""", Wire)!;
        var later = JsonSerializer.Deserialize<HomeSectionWithItems>("""{"id":"later","section_type":"recipe","items":[{"content_id":"shared"},{"content_id":"last"}]}""", Wire)!;
        early.ShownCount = 1;
        early.CompletedOrder = 1;
        var result = HomeSectionDedupe.Project([new() { Id = "early" }, new() { Id = "later" }],
            new Dictionary<string, HomeSectionWithItems> { ["early"] = early, ["later"] = later }, new HashSet<string> { "recipe" });
        Assert.Equal(["first"], result["early"].Items.Select(i => i.ContentId));
        Assert.Equal(["shared", "last"], result["later"].Items.Select(i => i.ContentId));
    }

    [Fact]
    public void CompletionPreservesShownOrderExcludesRemovalsAndDeduplicatesWholeResult()
    {
        var shown = Row("one", "removed", "two"); shown.HasMore = true;
        var current = Row("one", "two");
        var whole = Row("two", "removed", "three", "three", "one", "four"); whole.ItemLimit = 5;
        var completed = HomeRowWindow.Complete(shown, whole, current, 7);
        Assert.Equal(["one", "two", "three", "four"], completed.Items.Select(i => i.ContentId));
        Assert.False(completed.HasMore); Assert.Equal(2, completed.ShownCount); Assert.Equal(7, completed.CompletedOrder);
        Assert.True(HomeRowWindow.IsExpanded(completed));
    }

    [Fact]
    public void CompletionHonorsWholeRowsOwnLimitWhenItDrawsDifferentCards()
    {
        var shown = Row("one", "two"); var whole = Row("three", "four", "five"); whole.ItemLimit = 3;
        Assert.Equal(["one", "two", "three"], HomeRowWindow.Complete(shown, whole, shown, 1).Items.Select(i => i.ContentId));
    }

    [Fact]
    public void RemovingAnInitialCardDoesNotPromoteTailAboveOtherRowsInitialCards()
    {
        var early = Row("one", "two", "shared"); early.Id = "early"; early.ShownCount = 2; early.CompletedOrder = 1;
        var later = Row("shared"); later.Id = "later";
        HomeRowWindow.RemoveAt(early, 0);
        var rows = HomeSectionDedupe.Project([new() { Id = "early" }, new() { Id = "later" }],
            new Dictionary<string, HomeSectionWithItems> { ["early"] = early, ["later"] = later }, new HashSet<string> { "recipe" });
        Assert.Equal(["two"], rows["early"].Items.Select(i => i.ContentId));
        Assert.Equal(["shared"], rows["later"].Items.Select(i => i.ContentId));
    }

    [Fact]
    public void RefreshResponseCannotRestoreCardsRemovedSinceItsReadBegan()
    {
        var shown = Row("one", "removed"); var current = Row("one"); var incoming = Row("removed", "one", "new");
        HomeRowWindow.FilterRemoved(shown, current, incoming);
        Assert.Equal(["one", "new"], incoming.Items.Select(i => i.ContentId));
    }

    [Fact]
    public void ExpansionTailPriorityUsesCompletionOrderAcrossRepeatedProjection()
    {
        var a = Row("a", "shared"); a.Id = "a"; a.ShownCount = 1; a.CompletedOrder = 2;
        var b = Row("b", "shared"); b.Id = "b"; b.ShownCount = 1; b.CompletedOrder = 1;
        var raw = new Dictionary<string, HomeSectionWithItems> { ["a"] = a, ["b"] = b };
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var rows = HomeSectionDedupe.Project([new() { Id = "a" }, new() { Id = "b" }], raw, new HashSet<string> { "recipe" });
            Assert.Equal(["a"], rows["a"].Items.Select(i => i.ContentId));
            Assert.Equal(["b", "shared"], rows["b"].Items.Select(i => i.ContentId));
        }
    }

    private static HomeSectionWithItems Row(params string[] ids) => new()
        { Id = "row", SectionType = "recipe", ItemLimit = 50, Items = new(ids.Select(id => new MediaItem { ContentId = id })) };

    [Fact]
    public void DismissalUndoRestoresTheInitialPrefixBoundary()
    {
        var row = Row("one", "two", "tail"); row.SectionType = "continue_watching"; row.ShownCount = 2;
        var snapshot = HomeDismissalSnapshot.Capture([row], "two", null);
        HomeRowWindow.RemoveAt(row, 1); snapshot.Restore([row]);
        Assert.Equal(2, row.ShownCount);
        Assert.Equal(["one", "two", "tail"], row.Items.Select(item => item.ContentId));
        var anchor = Row("one"); anchor.Id = "anchor";
        var projected = HomeSectionDedupe.Project([new() { Id = "anchor" }, new() { Id = "row" }],
            new Dictionary<string, HomeSectionWithItems> { ["anchor"] = anchor, ["row"] = row }, new HashSet<string> { "continue_watching" });
        Assert.Equal(1, projected["row"].ShownCount);
    }
}
