using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>The Add row collection pickers' library filter and list text, and the English that changed after the generated catalog (1a93371).</summary>
public sealed class AdminHomeSectionsCollectionsTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));
    private static readonly AppLocale French = new LocaleService().Create(AppLocaleResolution.Resolve("fr", [], "fr-FR"));

    private static AdminHomeSectionChoice Library(string id, string name) => new() { Id = id, Name = name };
    private static AdminHomeSectionChoice Collection(string id, string title, string type, string library, int? items = null, params string[] libraries) =>
        new() { Id = id, Title = title, CollectionType = type, LibraryId = library, LibraryIds = libraries.ToList(), ItemCount = items };

    private static readonly AdminHomeSectionChoice[] Libraries = [Library("1", "Shows"), Library("2", "Movies"), Library("3", "Music")];

    [Fact]
    public void LibrariesAreTheNamedOnesThatShowACollectionSortedByName()
    {
        var options = new[] { Collection("a", "Pixar", "manual", "1", libraries: ["1", "2"]), Collection("b", "Noir", "smart", "2"), Collection("c", "Lost", "manual", "9") };
        var libraries = AdminHomeSectionsCollections.LibrariesOf(options, Libraries);
        // Library 3 shows none, library 9 has no name: neither is offered. A collection with no library list falls back to its one library.
        Assert.Equal(["Movies", "Shows"], libraries.Select(library => library.Name));
    }

    [Fact]
    public void ThePickedLibraryAppliesOnlyWhileItAndAnotherLibraryRemain()
    {
        var two = AdminHomeSectionsCollections.LibrariesOf([Collection("a", "A", "manual", "1"), Collection("b", "B", "manual", "2")], Libraries);
        Assert.Equal("1", AdminHomeSectionsCollections.EffectiveLibrary(two, "1"));
        Assert.Null(AdminHomeSectionsCollections.EffectiveLibrary(two, "3"));
        Assert.Null(AdminHomeSectionsCollections.EffectiveLibrary(two, null));
        var one = AdminHomeSectionsCollections.LibrariesOf([Collection("a", "A", "manual", "1")], Libraries);
        Assert.Null(AdminHomeSectionsCollections.EffectiveLibrary(one, "1"));
    }

    [Fact]
    public void SearchTypeAndLibraryFiltersCombine()
    {
        var options = new[]
        {
            Collection("a", "Pixar", "manual", "1", 4, "1", "2"), Collection("b", "Noir", "smart", "2", 9), Collection("c", "Top charts", "tmdb_list", "2", 20), Collection("d", "Pixar shorts", "smart", "1"),
        };
        Assert.Equal(["a", "d"], AdminHomeSectionsCollections.Filter(options, "pixar", "all", null).Select(option => option.Id));
        Assert.Equal(["d"], AdminHomeSectionsCollections.Filter(options, "pixar", "smart", null).Select(option => option.Id));
        Assert.Equal(["c"], AdminHomeSectionsCollections.Filter(options, "", "synced", null).Select(option => option.Id));
        Assert.Equal(["a", "b", "c"], AdminHomeSectionsCollections.Filter(options, "", "all", "2").Select(option => option.Id));
        Assert.Equal(["a"], AdminHomeSectionsCollections.Filter(options, "  PIXAR ", "manual", "2").Select(option => option.Id));
        Assert.Empty(AdminHomeSectionsCollections.Filter(options, "pixar", "synced", "1"));
    }

    [Fact]
    public void ACollectionReadsWithItsLibrariesTypeAndTitleCount()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Shows, Movies · Manual · 4 titles", AdminHomeSectionsCollections.Meta(Collection("a", "Pixar", "manual", "1", 4, "1", "2"), Libraries));
        Assert.Equal("Movies · Smart", AdminHomeSectionsCollections.Meta(Collection("b", "Noir", "smart", "2"), Libraries));
        Assert.Equal("Synced list · 1 title", AdminHomeSectionsCollections.Meta(Collection("c", "Charts", "tmdb_list", "", 1), Libraries));
    }

    [Fact]
    public void AnEmptyListSaysWhyIsEmpty()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Loading collections…", AdminHomeSectionsCollections.EmptyText(true, false, 0, "", false));
        Assert.Equal("No collections of this type in this library.", AdminHomeSectionsCollections.EmptyText(false, false, 5, "", true));
        Assert.Equal("No collections of this type.", AdminHomeSectionsCollections.EmptyText(false, false, 5, "", false));
        Assert.Equal("No collections match \"noir\".", AdminHomeSectionsCollections.EmptyText(false, false, 5, " noir ", true));
        Assert.Equal("Library", AdminHomeSectionsText.T("web.home.addRow.collection.library"));
    }

    [Fact]
    public void RuleRowsAreSmartRowsInEnglishAndKeepTheCatalogTranslationElsewhere()
    {
        using (AppStrings.UseForCurrentFlow(English))
        {
            Assert.Equal("Smart row", AdminHomeSectionsKinds.Label("custom_filter"));
            Assert.Equal("Collections & smart rows", AdminHomeSectionsKinds.GroupLabel("collections"));
            Assert.Equal("Like a smart collection, but only a row. Pick titles with rules like genre and decade.", AdminHomeSectionsKinds.Sentence("custom_filter"));
        }
        using (AppStrings.UseForCurrentFlow(French))
        {
            Assert.NotEqual("Smart row", AdminHomeSectionsKinds.Label("custom_filter"));
            Assert.False(string.IsNullOrWhiteSpace(AdminHomeSectionsKinds.Label("custom_filter")));
        }
        // The name a new rule row is saved with is stored data, not the display label.
        Assert.Equal("Titles matching rules", AdminHomeSectionsKinds.Name("custom_filter"));
    }
}
