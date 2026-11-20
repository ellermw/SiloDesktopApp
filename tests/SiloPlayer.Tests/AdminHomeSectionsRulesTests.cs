using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Ports of the official rowLinks, sectionLibraryFilter and bulkCopy tests (217ff25).</summary>
public sealed class AdminHomeSectionsRulesTests
{
    private static Dictionary<string, string> Query(string query) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);

    [Fact]
    public void ALinkReadsAServerCollectionToAddThePageAndTheRowToEdit()
    {
        var links = AdminHomeSectionsLinks.Read(Query("page=7&add=collection:library:lib-1"))!;
        Assert.Equal(new AdminHomeSectionsLinkCollection("library", "lib-1"), links.Add); Assert.False(links.AddInvalid); Assert.Null(links.Edit); Assert.Null(links.ReturnTo);
        var edit = AdminHomeSectionsLinks.Read(Query("edit=row-1&return=/admin/collections/abc/edit"))!;
        Assert.Equal("row-1", edit.Edit); Assert.Equal("/admin/collections/abc/edit", edit.ReturnTo); Assert.Null(edit.Add);
    }

    [Theory]
    [InlineData("add=collection:user:mine-1")]
    [InlineData("add=trending_on_server")]
    [InlineData("add=collection:library:")]
    [InlineData("add=collection:shelf:x")]
    [InlineData("add=")]
    public void APersonalCollectionOrAnythingElseIsInvalidOnTheAdminPage(string query)
    {
        var links = AdminHomeSectionsLinks.Read(Query(query))!;
        Assert.True(links.AddInvalid); Assert.Null(links.Add);
    }

    [Fact]
    public void NoLinkParametersMeansNoLinks()
    {
        Assert.Null(AdminHomeSectionsLinks.Read(Query("page=7"))); Assert.Null(AdminHomeSectionsLinks.Read(null));
        Assert.Equal("", AdminHomeSectionsLinks.Key(Query("page=7")));
        Assert.Equal("c\n\n", AdminHomeSectionsLinks.Key(Query("add=c")));
        Assert.Equal(new Dictionary<string, string> { ["page"] = "7" }, AdminHomeSectionsLinks.WithoutLinks(Query("page=7&add=x&edit=y&return=z")));
        Assert.Null(AdminHomeSectionsLinks.WithoutLinks(Query("add=x")));
    }

    [Theory]
    [InlineData("/collections/abc/edit")]
    [InlineData("/admin/collections/abc/edit")]
    [InlineData("/admin/collections/abc/edit?focus=where")]
    public void AReturnPathOnACollectionPageIsHonoured(string path) => Assert.Equal(path, AdminHomeSectionsLinks.SafeReturnPath(path));

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example/collections/")]
    [InlineData("/\\evil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/collections/%5cevil.example")]
    [InlineData("/collections/\tx")]
    [InlineData("/collections/%09x")]
    [InlineData("/collections/%0Ax")]
    [InlineData("/collections/\nx")]
    [InlineData("/collections//evil.example")]
    [InlineData("/collections/%2F%2Fevil.example")]
    [InlineData("/settings")]
    [InlineData("/collectionsx/abc")]
    [InlineData("/collections")]
    [InlineData("/collections/../settings")]
    [InlineData("/collections/%2E%2E/settings")]
    [InlineData("/collections/%E0%A4%A")]
    [InlineData("/collections/%")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherReturnPathIsIgnored(string? path) => Assert.Null(AdminHomeSectionsLinks.SafeReturnPath(path));

    private static JsonObject Config(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void LibraryFilterReadsTheFlatKeysFirstThenTheQueryShape()
    {
        Assert.Equal(new long[] { 3, 5 }, AdminHomeSectionsRules.LibraryFilterIds(Config("""{"filter_library_ids":[3,5,3,"x",0],"library_ids":[9]}""")));
        Assert.Equal(new long[] { 4 }, AdminHomeSectionsRules.LibraryFilterIds(Config("""{"filter_library_id":4}""")));
        Assert.Equal(new long[] { 9 }, AdminHomeSectionsRules.LibraryFilterIds(Config("""{"library_ids":[9]}""")));
        Assert.Empty(AdminHomeSectionsRules.LibraryFilterIds(Config("{}")));
        // Like the backend, any non-empty filter_library_ids array counts as set, even with no usable id.
        Assert.Empty(AdminHomeSectionsRules.LibraryFilterIds(Config("""{"filter_library_ids":["x"],"library_ids":[9]}""")));
    }

    [Fact]
    public void WritingTheLibraryFilterDropsTheLegacySingleKeyAndKeepsTheQueryShape()
    {
        var flat = AdminHomeSectionsRules.WithLibraryFilterIds(Config("""{"filter_library_id":4,"keep":1}"""), [7, 8]);
        Assert.Equal("[7,8]", flat["filter_library_ids"]!.ToJsonString()); Assert.Null(flat["filter_library_id"]); Assert.Equal(1, flat["keep"]!.GetValue<int>());
        var query = AdminHomeSectionsRules.WithLibraryFilterIds(Config("""{"media_scope":"movie","library_ids":[1]}"""), [2]);
        Assert.Equal("[2]", query["library_ids"]!.ToJsonString()); Assert.Equal("movie", query["media_scope"]!.GetValue<string>()); Assert.Null(query["filter_library_ids"]);
        var cleared = AdminHomeSectionsRules.WithLibraryFilterIds(Config("""{"filter_library_ids":[1],"library_ids":[1]}"""), []);
        Assert.Null(cleared["filter_library_ids"]); Assert.Null(cleared["library_ids"]);
    }

    [Fact]
    public void AGeneratedRowIsDetachedOnlyWhenItsSavedSelectionExcludesItsLibrary()
    {
        var kept = AdminHomeSectionsRules.FinalizeLibraryFilter(Config("""{"generated_source":"x","generated_library_id":5,"filter_library_ids":[5,6]}"""));
        Assert.Equal(5, kept["generated_library_id"]!.GetValue<int>());
        var detached = AdminHomeSectionsRules.FinalizeLibraryFilter(Config("""{"generated_source":"x","generated_library_id":5,"filter_library_ids":[6]}"""));
        Assert.Null(detached["generated_library_id"]); Assert.True(detached.ContainsKey("generated_library_id"));
        Assert.Equal(5, AdminHomeSectionsRules.FinalizeLibraryFilter(Config("""{"generated_source":"x","generated_library_id":5}"""))["generated_library_id"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("collection", "{}", false)]
    [InlineData("collection_posters", "{}", false)]
    [InlineData("custom_filter", "{}", false)]
    [InlineData("admin_curated_list", "{}", false)]
    [InlineData("genre", "{}", false)]
    [InlineData("award_winners", "{}", false)]
    [InlineData("recently_added", """{"filter_library_ids":[1]}""", false)]
    [InlineData("recently_added", """{"generated_library_id":3}""", false)]
    [InlineData("favorites", """{"library_id":3}""", false)]
    [InlineData("recently_added", "{}", true)]
    [InlineData("trending_on_server", """{"window":"7d"}""", true)]
    public void OnlyReadyMadeKindsThatNameNoLibraryCanBeCopied(string type, string config, bool expected) =>
        Assert.Equal(expected, AdminHomeSectionsRules.CanCopyToLibraries(type, Config(config)));

    private static readonly AdminHomeSectionLibraryPage[] Pages =
        [new("1", "Movies", "movies"), new("2", "TV", "series"), new("3", "More movies", "movie"), new("4", "Books", "audiobooks"), new("5", "E-books", "ebooks")];

    [Fact]
    public void ARowThatShowsOneKindOnlyGoesToPagesOfThatKind()
    {
        string[] Ids(IReadOnlyList<AdminHomeSectionLibraryPage> pages) => pages.Select(page => page.Id).ToArray();
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, Ids(AdminHomeSectionsRules.CopyTargetPages("recently_added", Config("{}"), Pages, "1")));
        Assert.Equal(new[] { "1", "3" }, Ids(AdminHomeSectionsRules.CopyTargetPages("recently_added", Config("""{"media_scope":"movie"}"""), Pages, "1")));
        Assert.Equal(new[] { "1", "4" }, Ids(AdminHomeSectionsRules.CopyTargetPages("next_in_series", Config("{}"), Pages, "1")));
        Assert.Equal(new[] { "4" }, Ids(AdminHomeSectionsRules.CopyTargetPages("next_in_series", Config("{}"), Pages, "4")));
        Assert.Equal(new[] { "1", "5" }, Ids(AdminHomeSectionsRules.CopyTargetPages("continue_watching", Config("""{"continue_type":"reading"}"""), Pages, "1")));
        Assert.Equal(new[] { "1", "2" }, Ids(AdminHomeSectionsRules.CopyTargetPages("trending_discover", Config("""{"filter_type":"series"}"""), Pages, "1")));
    }

    [Fact]
    public void AnAddRowDraftCopiesOnlyToPagesItFitsNeverToHomeAHeroOrItsOwnPage()
    {
        var draft = new AdminHomeSectionDraft { SectionType = "recently_added", Title = "x" };
        Assert.Equal(new[] { "2", "3" }, AdminHomeSectionsRules.LibraryCopyIds(draft, new("library", "1"), Pages, ["1", "2", "3", "3", "99"]));
        Assert.Empty(AdminHomeSectionsRules.LibraryCopyIds(draft, AdminHomeSectionScope.Home, Pages, ["2"]));
        draft.Featured = true; Assert.Empty(AdminHomeSectionsRules.LibraryCopyIds(draft, new("library", "1"), Pages, ["2"]));
        draft.Featured = false; draft.SectionType = "custom_filter"; Assert.Empty(AdminHomeSectionsRules.LibraryCopyIds(draft, new("library", "1"), Pages, ["2"]));
    }

    [Fact]
    public void ARowsVariantIsInferredStrictlyAndNeverFallsBackToTheFirstPreset()
    {
        Assert.Equal("tr_7d", AdminHomeSectionsRules.VariantOf("trending_on_server", Config("""{"window":"7d","other":1}""")));
        Assert.Null(AdminHomeSectionsRules.VariantOf("trending_on_server", Config("""{"window":"90d"}""")));
        Assert.Equal("se_auto", AdminHomeSectionsRules.VariantOf("seasonal_themed", Config("""{"enabled_themes":["halloween"]}""")));
        Assert.Equal("se_family_movie_night", AdminHomeSectionsRules.VariantOf("seasonal_themed", Config("""{"enabled_themes":["family_movie_night"]}""")));
        Assert.Equal("se_family_movie_night", AdminHomeSectionsRules.VariantOf("seasonal_themed", Config("""{"theme":"family_movie_night"}""")));
        Assert.Null(AdminHomeSectionsRules.VariantOf("seasonal_themed", Config("""{"theme":"halloween"}""")));
        Assert.Equal("es_era_80s", AdminHomeSectionsRules.VariantOf("editorial_spotlight", Config("""{"subject_type":"era","subject":"1980s"}""")));
        Assert.Null(AdminHomeSectionsRules.VariantOf("editorial_spotlight", Config("""{"subject_type":"era","subject":"1990s"}""")));
        Assert.Null(AdminHomeSectionsRules.VariantOf("recently_added", Config("{}")));
    }

    [Fact]
    public void UpdateConfigFinalizesLibraryFilterKindsAndLeavesRuleAndCollectionRowsAsDrafted()
    {
        var generated = Config("""{"generated_source":"x","generated_library_id":5,"filter_library_ids":[6]}""");
        Assert.Null(AdminHomeSectionsRules.UpdateConfig("recently_added", generated)["generated_library_id"]);
        Assert.Equal(5, AdminHomeSectionsRules.UpdateConfig("custom_filter", generated)["generated_library_id"]!.GetValue<int>());
        Assert.Equal(5, generated["generated_library_id"]!.GetValue<int>());
    }
}
