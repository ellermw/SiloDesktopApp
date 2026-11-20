using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official wording, row descriptions, kinds, variants and draft rules of the Home sections page (217ff25).</summary>
public sealed partial class AdminHomeSectionsPresentationTests
{
    private static JsonObject Config(string json) => JsonNode.Parse(json)!.AsObject();
    private static AdminHomeSection Row(string type, string config = "{}", bool enabled = true, string title = "Row", int limit = 20, string scope = "home") => new()
    { Id = "r", Scope = scope, LibraryId = scope == "home" ? null : "7", SectionType = type, Title = title, Enabled = enabled, ItemLimit = limit, Config = JsonDocument.Parse(config).RootElement.Clone() };
    private static string Text(AdminHomeSection row, bool home = true, Func<string, AdminHomeSectionCollectionLookup>? lookup = null) =>
        string.Concat(AdminHomeSectionsDescription.Describe(row, home, lookup).Select(part => part.Text));

    [GeneratedRegex("\"((?:web\\.|settingsManifest\\.|common\\.)[A-Za-z0-9_.]+[A-Za-z0-9_])\"")]
    private static partial Regex KeyLiteral();

    [Fact]
    public void EveryTextKeyThePageUsesHasOfficialText()
    {
        var missing = new List<string>();
        foreach (var key in AdminHomeSectionsKinds.TextKeys().Concat(AdminHomeSectionsDescription.SentenceKeys).Distinct()) if (!AdminHomeSectionsText.Has(key)) missing.Add(key);
        var files = new[] { "src/SiloPlayer/Views/Admin", "src/SiloPlayer.Core/Services/Admin", "src/SiloPlayer.Core/Api" }
            .SelectMany(folder => Directory.GetFiles(Path.Combine(TestRepository.Root, folder), "AdminHomeSections*.cs")).Where(file => !file.EndsWith("Text.cs", StringComparison.Ordinal));
        foreach (var file in files)
            foreach (Match match in KeyLiteral().Matches(File.ReadAllText(file)))
                if (!match.Value.EndsWith(".\"", StringComparison.Ordinal) && !AdminHomeSectionsText.Has(match.Groups[1].Value)) missing.Add(match.Groups[1].Value + " (" + Path.GetFileName(file) + ")");
        Assert.Empty(missing);
    }

    [Fact]
    public void PageTextUsesTheOfficialSentencesIncludingTheKeysTheInterfaceCatalogLacks()
    {
        Assert.Equal("Sections", AdminHomeSectionsText.T("web.admin.homeRows.page.title"));
        Assert.Equal("Restore defaults…", AdminHomeSectionsText.T("web.admin.homeRows.more.restore"));
        Assert.Equal("Turned on 1 row.", AdminHomeSectionsText.T("web.admin.homeRows.select.turnedOn", AdminHomeSectionsText.Count(1)));
        Assert.Equal("Turned off 2 of 3 rows.", AdminHomeSectionsText.T("web.admin.homeRows.select.turnedOffPartial", AdminHomeSectionsText.Count(3, ("done_text", 2))));
        Assert.Equal("A row of collections", AdminHomeSectionsText.T("web.home.rows.kinds.collectionPosters"));
        Assert.Equal("Deleted 1 of 3 sections", AdminHomeSectionsText.T("web.admin.common.sections.partiallyDeleted", AdminHomeSectionsText.Count(3, ("deleted", 1))));
        Assert.Equal("6 rows · 4 on", AdminHomeSectionsText.T("web.home.rows.page.summaryAdmin", new { row_count = 6L, shown_count = 4 }));
    }

    [Fact]
    public void RowsDescribeWhatTheyShowOnHomeAndOnALibraryPage()
    {
        Assert.Equal("Newest movies and episodes from all libraries", Text(Row("recently_added")));
        Assert.Equal("Newest additions from 2 libraries", Text(Row("recently_added", """{"filter_library_ids":[1,2]}""")));
        Assert.Equal("Newest movies in this library", Text(Row("recently_added", """{"media_scope":"movie"}""", scope: "library"), home: false));
        Assert.Equal("Most played on this server in the last 7 days", Text(Row("trending_on_server", """{"window":"7d"}""")));
        Assert.Equal("Most played here lately", Text(Row("trending_on_server", "{}"), home: false));
        Assert.Equal("The next episode of every show each viewer follows", Text(Row("next_up")));
        Assert.Equal("Movies trending on TMDB today, from what you have", Text(Row("trending_discover", """{"filter_type":"movie","window":"day"}""")));
        Assert.Equal("Rated 7.5+ on TMDB with 100+ votes, and never watched", Text(Row("hidden_gems")));
        Assert.Equal("Rated 7.0+ on TMDB with 100+ votes, and not watched in the past year", Text(Row("forgotten_favorites")));
        Assert.Contains("400 days", Text(Row("forgotten_favorites", """{"lookback_days":400}""")));
        Assert.Equal("Movies of 95 minutes or less, rated 6.0+ on TMDB with 100+ votes", Text(Row("short_watches")));
        Assert.Equal("The latest 4K additions", Text(Row("format_showcase", """{"format":"4k","sort":"recent"}""")));
        Assert.Equal("A different director every week", Text(Row("editorial_spotlight", """{"subject_type":"director","auto_rotate":true}""")));
        Assert.Equal("Changes with the season", Text(Row("seasonal_themed", """{"enabled_themes":["halloween"]}""")));
        Assert.Equal("Family picks on Friday and Saturday evenings", Text(Row("seasonal_themed", """{"enabled_themes":["family_movie_night"]}""")));
        Assert.Equal("Row", Text(Row("something_new")));
    }

    [Fact]
    public void RuleRowsDescribeTheirScopeRulesAndOrder()
    {
        Assert.Equal("Movies matching 2 rules", Text(Row("custom_filter", """{"media_scope":"movie","groups":[{"match":"all","rules":[{"field":"genre"},{"field":"year"}]}]}""")));
        Assert.Equal("All shows", Text(Row("custom_filter", """{"media_scope":"series","groups":[]}""")));
        Assert.Equal("All movies, highest rated first", Text(Row("custom_filter", """{"filter_type":"movie","sort":"rating_imdb","order":"desc"}""")));
        Assert.Equal("Titles in a chosen order", Text(Row("custom_filter", """{"sort":{"field":"bitrate","order":"desc"}}""")));
        Assert.Equal("Highest rated first", AdminHomeSectionsDescription.RuleSortSummary(Config("""{"sort":{"field":"rating_tmdb","order":"desc"}}""")));
        Assert.Equal("Newest added first", AdminHomeSectionsDescription.RuleSortSummary(Config("{}")));
        Assert.Equal("Custom order", AdminHomeSectionsDescription.RuleSortSummary(Config("""{"sort":{"field":"runtime","order":"asc"}}""")));
    }

    [Fact]
    public void CollectionRowsNameTheirCollectionsAndWarnWhenOneIsGone()
    {
        AdminHomeSectionCollectionLookup Lookup(string id) => id switch
        { "a" => AdminHomeSectionCollectionLookup.Found("Marvel", "Manual"), "b" => AdminHomeSectionCollectionLookup.Found("Pixar", null), "c" => AdminHomeSectionCollectionLookup.Found("Ghibli", null),
          "d" => AdminHomeSectionCollectionLookup.Found("Mubi", null), "gone" => AdminHomeSectionCollectionLookup.Gone, _ => AdminHomeSectionCollectionLookup.Unknown };
        Assert.Equal("The Marvel collection (Manual)", Text(Row("collection", """{"library_collection_id":"a"}"""), lookup: Lookup));
        var parts = AdminHomeSectionsDescription.Describe(Row("collection", """{"library_collection_id":"gone"}"""), true, Lookup);
        Assert.Equal(AdminHomeSectionPartStyle.Warning, parts.Single().Style); Assert.Equal("Collection no longer available", parts.Single().Text);
        Assert.Equal("A collection", Text(Row("collection", """{"library_collection_id":"x"}"""), lookup: Lookup));
        Assert.Equal("A collection", Text(Row("collection", """{"library_collection_id":"a"}""")));
        Assert.Equal("3 collections: Marvel, Pixar and Ghibli", Text(Row("collection_posters", """{"collections":[{"library_collection_id":"a"},{"library_collection_id":"b"},{"library_collection_id":"c"}]}"""), lookup: Lookup));
        Assert.Equal("4 collections: Marvel, Pixar and 2 more", Text(Row("collection_posters", """{"collections":[{"library_collection_id":"a"},{"library_collection_id":"b"},{"library_collection_id":"c"},{"library_collection_id":"d"}]}"""), lookup: Lookup));
        Assert.Equal("1 collection: Marvel 1 collection no longer available", Text(Row("collection_posters", """{"collections":[{"library_collection_id":"a"},{"library_collection_id":"gone"}]}"""), lookup: Lookup));
    }

    [Fact]
    public void KindsGroupLabelAndOrderLikeThePicker()
    {
        Assert.Equal("Recently added", AdminHomeSectionsKinds.Label("recently_added")); Assert.Equal("Row", AdminHomeSectionsKinds.Label("nope")); Assert.Equal("Row", AdminHomeSectionsKinds.Name("nope"));
        Assert.Equal("collections", AdminHomeSectionsKinds.Group("custom_filter")); Assert.Equal("collections", AdminHomeSectionsKinds.Group("nope"));
        Assert.True(AdminHomeSectionsKinds.NotOfferedForNewRows.SetEquals(["admin_curated_list", "genre", "award_winners", "next_up"]));
        var catalog = new AdminHomeSectionCatalog
        {
            Categories = [new() { Category = "x", Recipes = [
                new() { Type = "custom_filter" }, new() { Type = "next_up", Presets = [new() { Key = "p" }] }, new() { Type = "mystery", Presets = [new() { Key = "p" }] },
                new() { Type = "favorites", Presets = [new() { Key = "p" }] }, new() { Type = "recently_added", Presets = [new() { Key = "p" }] }, new() { Type = "collection", Presets = [new() { Key = "p" }] } ] }]
        };
        var groups = AdminHomeSectionsKinds.PickerGroups(catalog);
        Assert.Equal(new[] { "keep", "new", "moods", "collections" }, groups.Select(group => group.Group));
        Assert.Equal(new[] { "favorites" }, groups[0].Cards.Select(card => card.Type)); Assert.Equal(new[] { "mystery" }, groups[2].Cards.Select(card => card.Type));
        Assert.Equal(new[] { "collection", "custom_filter" }, groups[3].Cards.Select(card => card.Type));
        Assert.Equal("Oscar Winners (no longer offered)", AdminHomeSectionsKinds.RetiredLabel("award_winners", Config("""{"award_type":"oscar"}""")));
        Assert.Equal("Trending on this server · 7 days", AdminHomeSectionsKinds.ShowsLabel("trending_on_server", Config("""{"window":"7d"}""")));
        Assert.Equal("Continue Reading", AdminHomeSectionsKinds.ShowsLabel("continue_watching", Config("""{"continue_type":"reading"}""")));
        Assert.Equal("Seasonal picks (Halloween only)", AdminHomeSectionsKinds.ShowsLabel("seasonal_themed", Config("""{"theme":"halloween"}""")));
        Assert.True(AdminHomeSectionsKinds.VariantLocked("continue_watching", Config("""{"continue_type":"reading"}""")));
    }

    [Fact]
    public void VariantsChangeOnlyTheirOwnKeys()
    {
        var window = AdminHomeSectionsKinds.ApplyVariant("trending_on_server", Config("""{"window":"24h","future":1,"filter_library_ids":[3]}"""), "tr_30d");
        Assert.Equal("30d", window["window"]!.GetValue<string>()); Assert.Equal(1, window["future"]!.GetValue<int>()); Assert.Equal("[3]", window["filter_library_ids"]!.ToJsonString());
        var family = AdminHomeSectionsKinds.ApplyVariant("seasonal_themed", Config("""{"enabled_themes":["halloween"],"theme":"x","custom_holidays":[{"name":"a"}],"theme_dates":{"halloween":{"start":"10-01","end":"10-31"}},"source":"lists"}"""), "se_family_movie_night");
        Assert.Equal("""["family_movie_night"]""", family["enabled_themes"]!.ToJsonString()); Assert.Null(family["theme"]); Assert.Null(family["custom_holidays"]); Assert.Null(family["theme_dates"]); Assert.Equal("lists", family["source"]!.GetValue<string>());
        var holidays = AdminHomeSectionsKinds.ApplyVariant("seasonal_themed", Config("""{"enabled_themes":["family_movie_night"],"custom_holidays":[{"name":"a"}]}"""), "se_auto");
        Assert.Equal(9, holidays["enabled_themes"]!.AsArray().Count); Assert.NotNull(holidays["custom_holidays"]);
        var era = AdminHomeSectionsKinds.ApplyVariant("editorial_spotlight", Config("""{"subject_type":"director","auto_rotate":true,"rotation_cadence":"daily","subject":"Nolan"}"""), "es_era_80s");
        Assert.Equal("era", era["subject_type"]!.GetValue<string>()); Assert.Equal("1980s", era["subject"]!.GetValue<string>()); Assert.Null(era["auto_rotate"]); Assert.Null(era["rotation_cadence"]);
        var person = AdminHomeSectionsKinds.ApplyVariant("editorial_spotlight", Config("""{"subject_type":"era","subject":"1980s"}"""), "es_actor");
        Assert.Equal("actor", person["subject_type"]!.GetValue<string>()); Assert.True(person["auto_rotate"]!.GetValue<bool>()); Assert.Equal("weekly", person["rotation_cadence"]!.GetValue<string>()); Assert.Null(person["subject"]);
        var rotating = AdminHomeSectionsKinds.ApplyVariant("editorial_spotlight", Config("""{"subject_type":"director","auto_rotate":true,"rotation_cadence":"daily"}"""), "es_studio");
        Assert.Equal("daily", rotating["rotation_cadence"]!.GetValue<string>());
        Assert.Equal("""{"a":1}""", AdminHomeSectionsKinds.ApplyVariant("recently_added", Config("""{"a":1}"""), "nope").ToJsonString());
    }

    private static AdminHomeSectionCatalog Catalog() => new()
    {
        Categories = [new() { Category = "x", Recipes = [
            new() { Type = "trending_on_server", Presets = [new() { Key = "tr_24h", DisplayName = "Trending 24h" }, new() { Key = "tr_7d", DisplayName = "Trending 7d" }] },
            new() { Type = "recently_added", Presets = [new() { DisplayName = "Recently added" }] }, new() { Type = "custom_filter", Presets = [new() { DisplayName = "Rules", DefaultParams = Config("""{"filter_type":"movie","sort":"title","order":"asc"}""") }] },
            new() { Type = "collection", Presets = [new() { Key = "collection", DisplayName = "A collection" }] } ] }]
    };

    [Fact]
    public void AddRowStartsEachPickedKindFromItsOwnDraftAndOnlyChangeKeepsTheRowsChoices()
    {
        var catalog = Catalog(); AdminHomeRecipe Recipe(string type) => AdminHomeSectionsDraftFactory.FindRecipe(catalog, type)!;
        var recent = Recipe("recently_added"); var trending = Recipe("trending_on_server");
        var first = AdminHomeSectionsDraftFactory.ForPreset(recent, recent.Presets[0]);
        AdminHomeSectionsDraftFactory.WithTitle(first, "Mine"); first.ItemLimit = 50; first.Featured = true; first.WebOnly = true;
        // Add row: back to All rows and another kind keeps nothing from the first pick.
        var added = AdminHomeSectionsDraftFactory.ForPick(trending, trending.Presets[0], first, editing: false);
        Assert.Equal("Trending 24h", added.Title); Assert.True(added.TitleFollowsVariant); Assert.Equal(20, added.ItemLimit); Assert.False(added.Featured); Assert.False(added.WebOnly);
        // Edit row > Change keeps the name, the limit and the hero choice.
        var changed = AdminHomeSectionsDraftFactory.ForPick(trending, trending.Presets[0], first, editing: true);
        Assert.Equal("Mine", changed.Title); Assert.False(changed.TitleFollowsVariant); Assert.Equal(50, changed.ItemLimit); Assert.True(changed.Featured); Assert.True(changed.WebOnly);
        Assert.Equal("Trending 24h", AdminHomeSectionsDraftFactory.ForPick(trending, trending.Presets[0], null, editing: true).Title);
    }

    [Fact]
    public void NewRowDraftsFollowTheOfficialStartingPoints()
    {
        var catalog = Catalog(); AdminHomeRecipe Recipe(string type) => AdminHomeSectionsDraftFactory.FindRecipe(catalog, type)!;
        var trending = AdminHomeSectionsDraftFactory.ForPreset(Recipe("trending_on_server"), Recipe("trending_on_server").Presets[0]);
        Assert.Equal("Trending 24h", trending.Title); Assert.True(trending.TitleFollowsVariant); Assert.Equal(20, trending.ItemLimit); Assert.False(trending.Featured);
        AdminHomeSectionsDraftFactory.WithVariant(trending, catalog, "tr_7d");
        Assert.Equal("Trending 7d", trending.Title); Assert.Equal("7d", trending.Config["window"]!.GetValue<string>());
        AdminHomeSectionsDraftFactory.WithTitle(trending, "Mine"); AdminHomeSectionsDraftFactory.WithVariant(trending, catalog, "tr_24h");
        Assert.Equal("Mine", trending.Title); Assert.False(trending.TitleFollowsVariant);
        var recent = AdminHomeSectionsDraftFactory.ForPreset(Recipe("recently_added"), Recipe("recently_added").Presets[0]);
        Assert.False(recent.TitleFollowsVariant);
        var rules = AdminHomeSectionsDraftFactory.ForPreset(Recipe("custom_filter"), Recipe("custom_filter").Presets[0]);
        Assert.Equal("movie", rules.Config["media_scope"]!.GetValue<string>()); Assert.Null(rules.Config["filter_type"]); Assert.Null(rules.Config["order"]);
        Assert.Equal("""{"field":"title","order":"asc"}""", rules.Config["sort"]!.ToJsonString()); Assert.Equal("all", rules.Config["match"]!.GetValue<string>()); Assert.Equal("[]", rules.Config["groups"]!.ToJsonString());
        Assert.Equal("[]", rules.Config["library_ids"]!.ToJsonString());
    }

    [Fact]
    public void ACollectionRowIsNamedAfterItsCollectionWhileTheNameFollows()
    {
        var catalog = Catalog(); var recipe = AdminHomeSectionsDraftFactory.FindRecipe(catalog, "collection")!;
        var draft = AdminHomeSectionsDraftFactory.ForPreset(recipe, recipe.Presets[0]);
        Assert.Equal("", draft.Title); Assert.True(draft.TitleFollowsVariant);
        draft.Config["user_collection_id"] = "legacy"; draft.Config["tuning"] = 1;
        AdminHomeSectionsDraftFactory.WithCollection(draft, "c1", "Marvel");
        Assert.Equal("Marvel", draft.Title); Assert.Equal("c1", draft.Config["library_collection_id"]!.GetValue<string>()); Assert.Null(draft.Config["user_collection_id"]); Assert.Equal(1, draft.Config["tuning"]!.GetValue<int>());
        AdminHomeSectionsDraftFactory.WithTitle(draft, "My row");
        AdminHomeSectionsDraftFactory.WithCollection(draft, "c2", "Pixar", "Marvel");
        Assert.Equal("My row", draft.Title);
        draft.Title = "Marvel"; draft.TitleFollowsVariant = false;
        AdminHomeSectionsDraftFactory.WithCollection(draft, "c3", "Ghibli", "Marvel");
        Assert.Equal("Ghibli", draft.Title);
    }

    [Fact]
    public void ABlankNameFallsBackToTheCollectionTheVariantOrTheKindNeverARawTypeKey()
    {
        var catalog = Catalog();
        string Saved(string type, string title, string config = "{}", string? fallback = null) => AdminHomeSectionsDraftFactory.SavedTitle(new() { SectionType = type, Title = title, Config = Config(config) }, catalog, fallback);
        Assert.Equal("Typed", Saved("recently_added", "  Typed  ")); Assert.Equal("Marvel", Saved("collection", " ", fallback: "Marvel"));
        Assert.Equal("Trending 7d", Saved("trending_on_server", "", """{"window":"7d"}""")); Assert.Equal("Recently added", Saved("recently_added", ""));
        Assert.Equal("New releases", Saved("recently_released", "")); Assert.Equal("Row", Saved("mystery", ""));
    }

    [Fact]
    public void AnExistingRowOpensWithItsRawValuesAndAReloadMergesFieldByField()
    {
        var catalog = Catalog(); var row = Row("trending_on_server", """{"window":"7d","future":{"x":1}}""", title: "Trending 7d", limit: 0);
        var original = AdminHomeSectionsDraftFactory.FromRow(row, catalog);
        Assert.Equal(0, original.ItemLimit); Assert.True(original.TitleFollowsVariant); Assert.Equal("Trending 7d", original.Title);
        var edited = AdminHomeSectionsDraftFactory.FromRow(row, catalog); edited.Title = "My title"; edited.ItemLimit = 12;
        var upstream = AdminHomeSectionsDraftFactory.FromRow(Row("trending_on_server", """{"window":"30d","future":{"x":1}}""", title: "Server title", limit: 0), catalog);
        upstream.Featured = true;
        var changed = AdminHomeSectionsDraftFactory.MergeReloaded(original, edited, upstream);
        Assert.Equal("My title", edited.Title); Assert.Equal(12, edited.ItemLimit); Assert.Equal("30d", edited.Config["window"]!.GetValue<string>()); Assert.True(edited.Featured);
        Assert.Equal(new[] { "title", "shows", "hero" }, changed);
        Assert.Equal("""{"a":1,"b":[{"c":2,"d":3}]}""", AdminHomeSectionsDraftFactory.StableJson(Config("""{"b":[{"d":3,"c":2}],"a":1}""")));
    }

    [Fact]
    public void PreviewWaitsForTheChoicesTheServerWouldRefuseWithout()
    {
        Assert.NotNull(AdminHomeSectionsDraftFactory.PreviewWaitText(new() { SectionType = "collection" }));
        Assert.Null(AdminHomeSectionsDraftFactory.PreviewWaitText(new() { SectionType = "collection", Config = Config("""{"library_collection_id":"c"}""") }));
        Assert.NotNull(AdminHomeSectionsDraftFactory.PreviewWaitText(new() { SectionType = "collection_posters" }));
        Assert.NotNull(AdminHomeSectionsDraftFactory.PreviewWaitText(new() { SectionType = "admin_curated_list" }));
        Assert.Null(AdminHomeSectionsDraftFactory.PreviewWaitText(new() { SectionType = "recently_added" }));
    }

    [Fact]
    public void SeasonalHolidayProblemsNameTheirCauseLikeTheOfficialForm()
    {
        Assert.Equal("Enter both dates as MM-DD, or clear them to use the usual dates.", AdminHomeSectionsRules.HolidayDatesProblem("10-01", ""));
        Assert.Null(AdminHomeSectionsRules.HolidayDatesProblem("02-29", "03-01")); Assert.NotNull(AdminHomeSectionsRules.HolidayDatesProblem("13-01", "03-01"));
        Assert.Equal("Give the holiday a name.", AdminHomeSectionsRules.CustomHolidayProblem(Config("""{"name":" ","start":"01-01","end":"01-02","list_url":"https://mdblist.com/lists/a/b"}""")));
        Assert.Equal("Enter the start and end as MM-DD, such as 07-04.", AdminHomeSectionsRules.CustomHolidayProblem(Config("""{"name":"x","start":"7-4","end":"01-02","list_url":"https://mdblist.com/lists/a/b"}""")));
        Assert.Equal("Use a public MDBList list link (https://mdblist.com/lists/…).", AdminHomeSectionsRules.CustomHolidayProblem(Config("""{"name":"x","start":"01-01","end":"01-02","list_url":"https://@mdblist.com/lists/a/b"}""")));
        Assert.Null(AdminHomeSectionsRules.CustomHolidayProblem(Config("""{"name":"x","start":"12-24","end":"01-02","list_url":" https://www.mdblist.com./lists/a/b "}""")));
        Assert.False(AdminHomeSectionsRules.IsMdbListList("https://mdblist.com:8443/lists/a")); Assert.False(AdminHomeSectionsRules.IsMdbListList("https://mdblist.com/other"));
    }

    [Fact]
    public void ARuleRowsConfigFoldsLegacyKeysInPlaceAndKeepsRecipeMetadata()
    {
        var config = Config("""{"filter_type":"series","filter_library_ids":[2,3],"filter_library_id":3,"order":"asc","sort":"title","keep":{"x":1},"groups":[{"rules":[{"field":"genre","op":"is","value":"Drama"}]}]}""");
        var groups = config["groups"]; AdminHomeSectionsRules.NormalizeQueryInPlace(config);
        Assert.Same(groups, config["groups"]); Assert.Equal("series", config["media_scope"]!.GetValue<string>()); Assert.Equal("[2,3]", config["library_ids"]!.ToJsonString());
        Assert.Equal("""{"field":"title","order":"asc"}""", config["sort"]!.ToJsonString()); Assert.Equal("all", config["groups"]![0]!["match"]!.GetValue<string>()); Assert.Equal(1, config["keep"]!["x"]!.GetValue<int>());
        Assert.Null(config["filter_type"]); Assert.Null(config["filter_library_ids"]); Assert.Null(config["filter_library_id"]); Assert.Null(config["order"]);
    }
}
