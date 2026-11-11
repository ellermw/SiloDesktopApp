using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>The collection page hero's More actions policy: which entries exist for whom, and the words they use.</summary>
public sealed class CollectionHeroActionsTests
{
    private static Library[] Libraries(params (int Id, string Name)[] libraries) => libraries.Select(library => new Library { Id = library.Id, Name = library.Name }).ToArray();
    private static readonly Library[] Three = Libraries((1, "Movies"), (2, "Shows"), (3, "Kids"));

    [Fact]
    public void OwnerMenuHoldsSyncOnlyForASyncedListTheServerCanImportAndAlwaysDelete()
    {
        PersonalCollectionFacts Facts(bool synced, bool imports) => new("Mine", false, synced, imports);
        CollectionPageAccess Own(PersonalCollectionFacts facts) => new(CollectionPageAccessKind.Manage) { Personal = facts };
        Assert.Equal(new CollectionHeroMenu(true, true, []), CollectionHeroActions.Menu(Own(Facts(true, true))));
        Assert.Equal(new CollectionHeroMenu(false, true, []), CollectionHeroActions.Menu(Own(Facts(true, false))));
        Assert.Equal(new CollectionHeroMenu(false, true, []), CollectionHeroActions.Menu(Own(Facts(false, true))));
        // Without the collection (Manage but unread) there is nothing to delete or sync.
        Assert.False(CollectionHeroActions.Menu(new CollectionPageAccess(CollectionPageAccessKind.Manage)).HasEntries);
    }

    [Fact]
    public void ReadOnlyAndUnknownAccessNeverOfferSyncOrDelete()
    {
        var pages = new[] { new CollectionPageLibrary(1, "Movies") };
        var readOnly = CollectionHeroActions.Menu(new CollectionPageAccess(CollectionPageAccessKind.ReadOnly, "Alex") { LibraryPages = pages, Personal = new("Theirs", true, true, true) });
        Assert.Equal(new CollectionHeroMenu(false, false, pages), readOnly);
        Assert.False(CollectionHeroActions.Menu(new CollectionPageAccess(CollectionPageAccessKind.ReadOnly)).HasEntries);   // Add to my Home alone has no menu
        Assert.False(CollectionHeroActions.Menu(CollectionPageAccess.None).HasEntries);
        Assert.False(CollectionHeroActions.Menu(new CollectionPageAccess(CollectionPageAccessKind.None) { LibraryPages = pages }).HasEntries);
    }

    [Fact]
    public void SyncAppliesToTheSyncedListTypesOnly()
    {
        foreach (var type in new[] { "mdblist", "tmdb", "tmdb_list" }) Assert.True(CollectionHeroActions.CanSync(new Collection { CollectionType = type }), type);
        foreach (var type in new[] { "manual", "smart", "", "other" }) Assert.False(CollectionHeroActions.CanSync(new Collection { CollectionType = type }), type);
    }

    [Fact]
    public void PersonalCollectionOffersTheLibrariesItMatchesAndEveryLibraryWhenItMatchesNone()
    {
        Assert.Equal(["Movies", "Shows", "Kids"], CollectionHeroActions.LibraryPages(true, [], Three).Select(page => page.Name));
        Assert.Equal(["Movies", "Kids"], CollectionHeroActions.LibraryPages(true, [3, 1], Three).Select(page => page.Name));   // the viewer's order, not the rule's
        Assert.Empty(CollectionHeroActions.LibraryPages(true, [9], Three));   // matched only a library the viewer does not see
        Assert.Empty(CollectionHeroActions.LibraryPages(true, [], []));
    }

    [Fact]
    public void ServerCollectionOffersTheLibraryItWasOpenedInFirstAndThenTheOthers()
    {
        Assert.Equal(["Shows", "Movies", "Kids"], CollectionHeroActions.LibraryPages(false, [2], Three).Select(page => page.Name));
        Assert.Equal(["Movies", "Shows", "Kids"], CollectionHeroActions.LibraryPages(false, [], Three).Select(page => page.Name));
        // Opened in a library the viewer does not see: no bound page, every other library still listed.
        Assert.Equal(["Movies", "Shows", "Kids"], CollectionHeroActions.LibraryPages(false, [9], Three).Select(page => page.Name));
    }

    [Fact]
    public void PersonalLibraryIdsFollowTheSourceConfigurationThenTheRulesAndDropInvalidIds()
    {
        Collection Make(string type, string? sourceIds = null, params int[] rules)
            => new()
            {
                CollectionType = type,
                SourceConfig = sourceIds == null ? null : new() { ["library_ids"] = JsonDocument.Parse(sourceIds).RootElement.Clone() },
                QueryDefinition = new() { LibraryIds = [.. rules] },
            };
        Assert.Empty(CollectionHeroActions.PersonalLibraryIds(Make("manual", "[1,2]", 3)));   // a manual collection has none
        Assert.Equal([2, 5], CollectionHeroActions.PersonalLibraryIds(Make("mdblist", "[2,5,2,0,-1,\"x\"]", 7)));
        Assert.Empty(CollectionHeroActions.PersonalLibraryIds(Make("mdblist", "[]", 7)));   // an explicit empty list is "all"
        Assert.Equal([7, 8], CollectionHeroActions.PersonalLibraryIds(Make("smart", null, 7, 8, 7)));
        var plain = new Collection { CollectionType = "tmdb", SourceConfig = new() { ["library_ids"] = new List<object> { 4, 4L, 6.0, 0 } } };
        Assert.Equal([4, 6], CollectionHeroActions.PersonalLibraryIds(plain));
    }

    [Fact]
    public void WordsMatchThePinnedWebUiAndTheCardMenu()
    {
        Assert.Equal("Delete \"Weekend picks\"?", CollectionHeroActions.DeleteTitle("Weekend picks"));
        Assert.Equal("This can't be undone.", CollectionHeroActions.DeleteDescription(false));
        Assert.Equal("It's removed for you and every profile you share it with. This can't be undone.", CollectionHeroActions.DeleteDescription(true));
        Assert.Equal("Add to my library page", CollectionHeroActions.AddToLibraryPageGroup);
        Assert.Equal("Add to my Movies page", CollectionHeroActions.AddToLibraryPage("Movies"));
        Assert.Equal("Movies page", CollectionHeroActions.LibraryPage("Movies"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericStringLibraryIdsKeepPersonalLibraryPagesRestricted(bool materialized)
    {
        var collection = new Collection
        {
            CollectionType = "mdblist",
            SourceConfig = new()
            {
                ["library_ids"] = materialized
                    ? (object)new List<object> { "3", 3, "x", "0", "-1", "2147483648" }
                    : JsonDocument.Parse("""["3",3,"x","0","-1","2147483648"]""").RootElement.Clone(),
            },
        };
        var ids = CollectionHeroActions.PersonalLibraryIds(collection);
        Assert.Equal([3], ids);
        Assert.Equal(["Kids"], CollectionHeroActions.LibraryPages(true, ids, Three).Select(page => page.Name));
    }

    [Fact]
    public void SyncOutcomesReportFailureWarningsAndCounts()
    {
        Assert.Equal("Collection sync failed: no key", CollectionHeroActions.SyncFailure(new() { Status = "failed", Message = "no key" }));
        Assert.Null(CollectionHeroActions.SyncFailure(new() { Status = "success" }));
        Assert.Null(CollectionHeroActions.SyncFailure(new() { Status = "warning" }));
        Assert.Equal("Synced 12 titles", CollectionHeroActions.SyncSuccess(new() { Status = "success", ItemsMatched = 12 }));
        Assert.Equal("Synced 1 title with warnings", CollectionHeroActions.SyncSuccess(new() { Status = "warning", ItemsMatched = 1 }));
    }
}
