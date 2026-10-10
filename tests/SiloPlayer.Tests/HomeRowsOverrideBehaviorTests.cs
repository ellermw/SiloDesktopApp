using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class HomeRowsOverrideBehaviorTests
{
    private static SettingsSectionEntry Server(string id, string title, int position = 0) => new()
    { Id = id, Title = title, DefaultTitle = title, Position = position, SectionType = "recently_added", ItemLimit = 20,
        Config = new() { ["media_scope"] = "movie", ["future_server_field"] = "preserved" } };

    [Fact]
    public void UntouchedServerRowsKeepInheritingAllServerFields()
    {
        var baseline = new[] { Server("a", "Original"), Server("b", "Second", 1) };
        var draft = baseline.Select(HomeSectionWritePolicy.Snapshot).ToList();
        draft[0].Hidden = true;
        var only = Assert.Single(HomeSectionWritePolicy.Build(draft, [], [], [], baseline: baseline));
        Assert.Equal("a", only.SectionId); Assert.True(only.Hidden);
        Assert.Null(only.Title); Assert.Null(only.Config); Assert.Null(only.Featured); Assert.Null(only.ItemLimit); Assert.Null(only.Position);
    }

    [Fact]
    public void OriginalNameClearsOnlyTheRenameWhileSavedOtherFieldsRemainPinned()
    {
        var original = Server("a", "Original"); original.Title = "My name";
        var draft = HomeSectionWritePolicy.Snapshot(original); draft.Title = original.DefaultTitle!;
        var saved = new RawSectionOverride { Id = "saved-id", SectionId = "a", Title = "My name", Featured = true, ItemLimit = 12 };
        var only = Assert.Single(HomeSectionWritePolicy.Build([draft], [saved], [], [], baseline: [original]));
        Assert.Equal("saved-id", only.Id); Assert.Null(only.Title); Assert.True(only.Featured); Assert.Equal(12, only.ItemLimit);
    }

    [Fact]
    public void NewRowAppendDoesNotPinTheServerOrderButMovingItDoes()
    {
        var baseline = new[] { Server("a", "A"), Server("b", "B", 1) };
        var added = new SettingsSectionEntry { Id = "own", Title = "Mine", SectionType = "custom_filter", IsCustom = true, Position = 2, ItemLimit = 20 };
        var draft = baseline.Select(HomeSectionWritePolicy.Snapshot).Append(added).ToList();
        var appended = Assert.Single(HomeSectionWritePolicy.Build(draft, [], [], [], baseline: baseline));
        Assert.Equal("own", appended.Id); Assert.Equal(2, appended.Position);
        draft.Remove(added); draft.Insert(0, added);
        var moved = HomeSectionWritePolicy.Build(draft, [], [], [], baseline: baseline);
        Assert.Equal(new int?[] { 0, 1, 2 }, moved.Select(row => row.Position));
        Assert.All(moved.Where(row => row.SectionId != null), row => { Assert.Null(row.Title); Assert.Null(row.Config); });
    }

    [Fact]
    public void ConfigComparisonIgnoresObjectPropertyOrderAndSnapshotsAreIndependent()
    {
        var original = Server("a", "A");
        var reordered = new Dictionary<string, object> { ["future_server_field"] = "preserved", ["media_scope"] = "movie" };
        Assert.True(HomeSectionWritePolicy.EqualConfig(original.Config, reordered));
        var snapshot = HomeSectionWritePolicy.Snapshot(original);
        original.Config!["media_scope"] = "series";
        Assert.False(HomeSectionWritePolicy.EqualConfig(original.Config, snapshot.Config));
    }

    [Fact]
    public void RemovingCustomRowsNeverResurrectsTheirRawOverrides()
    {
        var removed = new RawSectionOverride { Id = "deleted-custom", IsUserAdded = true, SectionType = "collection", Title = "Deleted" };
        var rows = HomeSectionWritePolicy.Build([], [removed], ["server-row"], [], baseline: []);
        var tombstone = Assert.Single(rows); Assert.Equal("server-row", tombstone.SectionId); Assert.True(tombstone.Removed);
    }
}
