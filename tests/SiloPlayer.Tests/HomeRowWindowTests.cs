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
    public void CompletionCapsAnOversizedResponseAtTheRowsOwnLimit()
    {
        var shown = Row(Enumerable.Range(1, 20).Select(i => $"card{i}").ToArray());
        var whole = Row(Enumerable.Range(1, 35).Select(i => $"card{i}").ToArray());
        whole.ItemLimit = 20;
        var completed = HomeRowWindow.Complete(shown, whole, shown, 1);
        Assert.Equal(20, completed.Items.Count);
        Assert.Equal(shown.Items.Select(i => i.ContentId), completed.Items.Select(i => i.ContentId));
    }

    [Fact]
    public void CompletionNeverDropsTheShownPrefixWhenTheRowsLimitDecreases()
    {
        var shown = Row("one", "two", "three");
        var whole = Row("four", "five"); whole.ItemLimit = 2;
        Assert.Equal(["one", "two", "three"], HomeRowWindow.Complete(shown, whole, shown, 1).Items.Select(i => i.ContentId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CompletionWithoutAPositiveLimitKeepsAllUnseenCards(int limit)
    {
        var shown = Row("one"); var whole = Row("two", "three"); whole.ItemLimit = limit;
        Assert.Equal(["one", "two", "three"], HomeRowWindow.Complete(shown, whole, shown, 1).Items.Select(i => i.ContentId));
    }

    [Fact]
    public void TruncatedCompletionStaysPartialAndFurtherCompletionRetainsInitialPriority()
    {
        var shown = Row("one", "two"); shown.HasMore = true;
        var partial = Row("one", "two", "three"); partial.HasMore = true;
        var first = HomeRowWindow.Complete(shown, partial, shown, 7);
        Assert.True(first.HasMore);
        Assert.True(first.HasReadWhole);
        Assert.False(first.CanComplete);
        Assert.True(HomeRowWindow.IsExpanded(first));
        Assert.Equal(2, first.ShownCount);
        var unchanged = HomeRowWindow.Complete(first, partial, first, 8);
        Assert.Equal(HomeSectionChange.None, HomeSectionReconciler.Apply(first, unchanged));
        var completed = HomeRowWindow.Complete(first, Row("one", "two", "three", "four"), first, 9);
        Assert.False(completed.HasMore);
        Assert.Equal(["one", "two", "three", "four"], completed.Items.Select(i => i.ContentId));
        Assert.Equal(2, completed.ShownCount);
        Assert.Equal(7, completed.CompletedOrder);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public void LanguageChangeReplacesAvailableTranslationsWithoutDroppingShownCards(int limit)
    {
        var current = Row("one", "omitted", "tail");
        current.ReadLanguageGeneration = 1; current.ShownCount = 2; current.CompletedOrder = 7;
        var whole = Row("tail", "one", "new");
        whole.ReadLanguageGeneration = 2; whole.HasMore = true; whole.ItemLimit = limit;
        whole.Items[0].Title = "Translated tail"; whole.Items[1].Title = "Translated one";

        var completed = HomeRowWindow.Complete(HomeSectionDedupe.Snapshot(current), whole, current, 8);

        Assert.Equal(limit == 2 ? new[] { "one", "omitted", "tail" } : new[] { "one", "omitted", "tail", "new" },
            completed.Items.Select(item => item.ContentId));
        Assert.True(completed.Items.Count >= current.Items.Count);
        Assert.Same(whole.Items[1], completed.Items[0]);
        Assert.Same(current.Items[1], completed.Items[1]);
        Assert.Same(whole.Items[0], completed.Items[2]);
        Assert.Equal(2, completed.ShownCount); Assert.Equal(7, completed.CompletedOrder);
        Assert.Equal(whole.ReadLanguageGeneration, completed.ReadLanguageGeneration);
        Assert.True(completed.HasMore); Assert.False(completed.CanComplete);
    }

    [Fact]
    public void WholeReadStateSurvivesSnapshotProjectionAndReconciliationEvenWhenAllCardsAreHidden()
    {
        var row = Row("shared"); row.HasMore = true; row.HasReadWhole = true;
        Assert.True(HomeRowWindow.IsExpanded(row));
        var anchor = Row("shared"); anchor.Id = "anchor";
        var projected = HomeSectionDedupe.Project([new() { Id = "anchor" }, new() { Id = "row" }],
            new Dictionary<string, HomeSectionWithItems> { ["anchor"] = anchor, ["row"] = HomeSectionDedupe.Snapshot(row) },
            new HashSet<string> { "recipe" });
        var mounted = Row("shared"); mounted.HasMore = true;
        var change = HomeSectionReconciler.Apply(mounted, projected["row"]);
        Assert.True(change.HasFlag(HomeSectionChange.Metadata));
        Assert.Empty(mounted.Items);
        Assert.True(mounted.HasMore); Assert.True(mounted.HasReadWhole); Assert.False(mounted.CanComplete);
        var json = JsonSerializer.SerializeToElement(mounted, Wire);
        Assert.True(json.GetProperty("has_more").GetBoolean());
        Assert.False(json.TryGetProperty("has_read_whole", out _));
        Assert.False(json.TryGetProperty("can_complete", out _));
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
