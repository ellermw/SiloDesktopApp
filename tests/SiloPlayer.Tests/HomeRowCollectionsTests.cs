using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// The viewer's Home rows as the official WebUI has them (silo-server 3b2b32168): a row of collections (#4410), the library filter of the Add row collection
/// pickers (#4471) and the Server / Yours tags (#4702).
/// </summary>
public sealed class HomeRowCollectionsTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));
    private static readonly AppLocale French = new LocaleService().Create(AppLocaleResolution.Resolve("fr", [], "fr-FR"));
    private static readonly JsonSerializerOptions Wire = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

    private const string Row = """
        {"id":"row-1","section_type":"collection_posters","title":"Collections","featured":false,"item_limit":20,"total_count":2,"is_custom":true,"customized":false,"items":[],
         "collections":[
           {"id":"c-server","source":"library","title":"Marvel","poster_url":"https://img.test/marvel.jpg","poster_thumbhash":"abc","poster_is_collage":false,"item_count":12,"library_id":"7"},
           {"id":"c-mine","source":"user","title":"Weekend picks","poster_url":"","poster_is_collage":true,"item_count":3,"creator_profile_id":"p-me"},
           {"id":"c-shared","source":"user","title":"Anime nights","poster_url":"https://img.test/anime.jpg","poster_is_collage":false,"item_count":1234,"creator_profile_id":"p-other"}]}
        """;

    // ── The section read ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ASectionReadCarriesItsCollectionCardsAndNoItems()
    {
        var row = JsonSerializer.Deserialize<HomeSectionWithItems>(Row, Wire)!;
        Assert.True(row.HoldsCollections);
        Assert.Empty(row.Items);
        Assert.Equal(["library:c-server", "user:c-mine", "user:c-shared"], row.Collections.Select(card => card.Key));
        var server = row.Collections[0];
        Assert.Equal((7, "Marvel", 12, "abc", false), (server.LibraryId, server.Title, server.ItemCount, server.PosterThumbhash, server.PosterIsCollage));
        Assert.Equal("https://img.test/marvel.jpg", server.PosterUrl);
        Assert.Null(server.CreatorProfileId);
        Assert.True(row.Collections[1].PosterIsCollage);
        Assert.Null(row.Collections[1].LibraryId);
        Assert.Equal("p-other", row.Collections[2].CreatorProfileId);
    }

    [Theory]
    [InlineData("""{"id":"row","items":[]}""")]
    [InlineData("""{"id":"row","items":[],"collections":[]}""")]
    [InlineData("""{"id":"row","items":[],"collections":null}""")]
    public void ARowWithoutCollectionCardsIsAnOrdinaryRow(string json)
    {
        var row = JsonSerializer.Deserialize<HomeSectionWithItems>(json, Wire)!;
        Assert.False(row.HoldsCollections);
        Assert.Empty(row.Collections);
    }

    [Fact]
    public async Task TheHomeApiReadsARowOfCollectionsThroughTheRowsOwnRequest()
    {
        using var http = new HttpClient(new Handler(Row));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-collections.test"); client.SetProfile("p-me");
        var response = await new HomeApi(client).GetSectionItemsAsync("row-1");
        Assert.Equal(3, response.Section!.Collections.Count);
        Assert.Empty(response.Section.Items);
        Assert.Equal("/api/v2/home/sections/row-1/items", Handler.Requested.Single());
    }

    [Fact]
    public void ASnapshotAndAReconciliationKeepTheCollectionCards()
    {
        var incoming = JsonSerializer.Deserialize<HomeSectionWithItems>(Row, Wire)!;
        var snapshot = HomeSectionDedupe.Snapshot(incoming);
        Assert.Equal(3, snapshot.Collections.Count);
        Assert.NotSame(incoming.Collections, snapshot.Collections);
        var mounted = new HomeSectionWithItems { Id = "row-1" };
        var change = HomeSectionReconciler.Apply(mounted, snapshot);
        Assert.True(change.HasFlag(HomeSectionChange.Metadata));
        Assert.Equal(3, mounted.Collections.Count);
        // The same cards again are no change, even though the presigned poster address is new on every read.
        var again = JsonSerializer.Deserialize<HomeSectionWithItems>(Row.Replace("img.test", "img2.test"), Wire)!;
        Assert.Equal(HomeSectionChange.None, HomeSectionReconciler.Apply(mounted, again));
        // A renamed collection or a new count is a change.
        var renamed = JsonSerializer.Deserialize<HomeSectionWithItems>(Row.Replace("Marvel", "Marvel Studios"), Wire)!;
        Assert.True(HomeSectionReconciler.Apply(mounted, renamed).HasFlag(HomeSectionChange.Metadata));
        Assert.Equal("Marvel Studios", mounted.Collections[0].Title);
        var shorter = JsonSerializer.Deserialize<HomeSectionWithItems>(Row, Wire)!; shorter.Collections.RemoveAt(2);
        Assert.NotEqual(HomeSectionChange.None, HomeSectionReconciler.Apply(mounted, shorter));
        Assert.Equal(2, mounted.Collections.Count);
    }

    [Fact]
    public void ARefreshRenewsThePosterAddressOfTheMountedCards()
    {
        var mounted = new HomeSectionWithItems { Id = "row-1" };
        HomeSectionReconciler.Apply(mounted, JsonSerializer.Deserialize<HomeSectionWithItems>(Row, Wire)!);
        var cards = mounted.Collections.ToList();
        var renewed = new List<string>();
        foreach (var card in cards) card.PosterUrlChanged += (sender, _) => renewed.Add(((SectionCollectionCard)sender!).Key);
        // The presigned address is new on every read and the earlier one expires: the row is unchanged and keeps its card objects, which take the new address.
        var again = JsonSerializer.Deserialize<HomeSectionWithItems>(Row.Replace("img.test", "img2.test"), Wire)!;
        Assert.Equal(HomeSectionChange.None, HomeSectionReconciler.Apply(mounted, again));
        Assert.Equal(3, mounted.Collections.Count);
        for (var index = 0; index < cards.Count; index++) Assert.Same(cards[index], mounted.Collections[index]);
        Assert.Equal("https://img2.test/marvel.jpg", cards[0].PosterUrl);
        Assert.Equal("", cards[1].PosterUrl);
        Assert.Equal("https://img2.test/anime.jpg", cards[2].PosterUrl);
        // Only a card whose address changed is told, so a poster that never arrived can be read again.
        Assert.Equal(["library:c-server", "user:c-shared"], renewed);
        // The same addresses again renew nothing.
        var same = JsonSerializer.Deserialize<HomeSectionWithItems>(Row.Replace("img.test", "img2.test"), Wire)!;
        Assert.Equal(HomeSectionChange.None, HomeSectionReconciler.Apply(mounted, same));
        Assert.Equal(2, renewed.Count);
    }

    [Fact]
    public void OnlyAnotherProfilesSharedCollectionNamesItsOwner()
    {
        var cards = JsonSerializer.Deserialize<HomeSectionWithItems>(Row, Wire)!.Collections;
        Assert.False(cards[0].IsAnotherProfilesCollection("p-me"));
        Assert.False(cards[1].IsAnotherProfilesCollection("p-me"));
        Assert.True(cards[2].IsAnotherProfilesCollection("p-me"));
        Assert.False(cards[2].IsAnotherProfilesCollection(null));
        Assert.False(new SectionCollectionCard { Source = "user", Id = "x" }.IsAnotherProfilesCollection("p-me"));
    }

    // ── The row's rules ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AConfigListsItsCollectionsInOrderBySourceAndSkipsWhatItCannotRead()
    {
        var config = new Dictionary<string, object>
        {
            ["collections"] = JsonSerializer.Deserialize<JsonElement>("""[{"library_collection_id":"a"},{"user_collection_id":"b"},{"library_collection_id":""},{"other":"c"},"x",{"library_collection_id":"d"}]"""),
        };
        Assert.Equal(["library:a", "user:b", "library:d"], HomeRowCollections.RefsOf(config).Select(entry => entry.Key));
        Assert.Equal(["a", "d"], HomeRowCollections.LibraryCollectionIdsOf(config));
        Assert.Empty(HomeRowCollections.RefsOf(null));
        Assert.Empty(HomeRowCollections.RefsOf(new Dictionary<string, object>()));
        Assert.Empty(HomeRowCollections.RefsOf(new Dictionary<string, object> { ["collections"] = "nope" }));
    }

    [Fact]
    public void TheListIsWrittenInTheShapeTheServerReadsAndKeepsEveryOtherKey()
    {
        var config = new Dictionary<string, object> { ["keep"] = 5, ["collections"] = new List<object>() };
        var written = HomeRowCollections.WithRefs(config, [new("library", "a"), new("user", "b")]);
        Assert.Equal("""{"keep":5,"collections":[{"library_collection_id":"a"},{"user_collection_id":"b"}]}""", JsonSerializer.Serialize(written));
        Assert.Equal(["library:a", "user:b"], HomeRowCollections.RefsOf(written).Select(entry => entry.Key));
        // The config handed in is not changed.
        Assert.Empty(HomeRowCollections.RefsOf(config));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void ARowTakesOneToThirtyCollections(int count, bool saves)
    {
        var refs = Enumerable.Range(0, count).Select(index => new HomeRowCollectionRef("library", "c" + index)).ToList();
        Assert.Equal(saves, HomeRowCollections.CanSave(refs));
        Assert.Equal(count >= 30, HomeRowCollections.IsFull(count));
    }

    [Fact]
    public void AddingStopsAtTheLimitButAChosenCollectionCanStillBeTakenOut()
    {
        var refs = Enumerable.Range(0, 30).Select(index => new HomeRowCollectionRef("library", "c" + index)).ToList();
        Assert.Equal(30, HomeRowCollections.Toggle(refs, "library", "extra").Count);
        Assert.DoesNotContain(HomeRowCollections.Toggle(refs, "library", "extra"), entry => entry.Id == "extra");
        var fewer = HomeRowCollections.Toggle(refs, "library", "c3");
        Assert.Equal(29, fewer.Count);
        Assert.DoesNotContain(fewer, entry => entry.Id == "c3");
        // A server and a personal collection may share an id: they are two entries.
        var both = HomeRowCollections.Toggle(HomeRowCollections.Toggle([], "library", "same"), "user", "same");
        Assert.Equal(["library:same", "user:same"], both.Select(entry => entry.Key));
        Assert.Equal(["user:same"], HomeRowCollections.Toggle(both, "library", "same").Select(entry => entry.Key));
        Assert.Equal(HomeRowCollections.MaxCollections, 30);
    }

    [Fact]
    public void MovingOneStepReordersAndAnEdgeMoveChangesNothing()
    {
        var refs = new List<HomeRowCollectionRef> { new("library", "a"), new("library", "b"), new("user", "c") };
        Assert.Equal(["library:b", "library:a", "user:c"], HomeRowCollections.Move(refs, 0, 1).Select(entry => entry.Key));
        Assert.Equal(["user:c", "library:a", "library:b"], HomeRowCollections.Move(refs, 2, 0).Select(entry => entry.Key));
        Assert.Equal(["library:a", "library:b", "user:c"], HomeRowCollections.Move(refs, 0, -1).Select(entry => entry.Key));
        Assert.Equal(["library:a", "library:b", "user:c"], HomeRowCollections.Move(refs, 2, 3).Select(entry => entry.Key));
        Assert.Equal(["library:a", "library:b", "user:c"], HomeRowCollections.Move(refs, 1, 1).Select(entry => entry.Key));
    }

    [Fact]
    public void ARowOfCollectionsHasNoTitleLimitAndItsLineCountsItsCollections()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.True(HomeRowCollections.Holds("collection_posters"));
        Assert.False(HomeRowCollections.Holds("collection"));
        Assert.False(HomeRowCollections.ShowsTitleLimit("collection_posters"));
        Assert.True(HomeRowCollections.ShowsTitleLimit("collection"));
        var row = new SettingsSectionEntry { SectionType = "collection_posters", Title = "Collections", Config = new() { ["collections"] = new List<object> { new Dictionary<string, string> { ["library_collection_id"] = "a" } } } };
        Assert.Equal("1 collection", HomeRecipePresentation.Describe(row));
        row.Config = HomeRowCollections.WithRefs(row.Config, [new("library", "a"), new("user", "b"), new("user", "c")]);
        Assert.Equal("3 collections", HomeRecipePresentation.Describe(row));
        Assert.Equal("12 collections", HomeRowCollections.CountText(12));
    }

    [Fact]
    public void ARowOfCollectionsStartsAsCollectionsAndSaysWhatItNeeds()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Collections", HomeRowCollections.DefaultRowName);
        Assert.Equal("Add a collection to see the row.", HomeRowText.T("web.home.rows.draft.addCollection"));
        Assert.Equal("A row holds up to 30 collections.", HomeRowCollections.LimitText());
        Assert.Equal("1 collection, shown in this order", HomeRowCollections.ChosenCountText(1));
        Assert.Equal("3 collections, shown in this order", HomeRowCollections.ChosenCountText(3));
    }

    // ── The collections the pickers offer ──────────────────────────────────────────────────────────────────────────

    private static readonly Library[] Libraries = [new() { Id = 1, Name = "Shows" }, new() { Id = 2, Name = "Movies" }, new() { Id = 3, Name = "Music" }];
    private static LibraryCollection Library(string id, string title, string type = "manual", int items = 0) => new() { Id = id, Title = title, CollectionType = type, ItemCount = items, PosterUrl = "https://img.test/" + id };

    private static List<HomeRowCollectionOption> Options() => HomeRowCollections.BuildOptions(Libraries,
        [new Collection { Id = "u1", Name = "Weekend picks", CollectionType = "manual", ItemCount = 3, CreatorProfileId = "p-me" },
         new Collection { Id = "u2", Name = "Anime nights", CollectionType = "smart", ItemCount = 8, CreatorProfileId = "p-other" }],
        [new[] { Library("pixar", "Pixar", "manual", 4), Library("noir", "Noir", "smart", 9) }, new[] { Library("pixar", "Pixar", "manual", 4), Library("top", "Top charts", "tmdb_list", 20) }, []]);

    [Fact]
    public void ACollectionInSeveralLibrariesIsOneOptionNamingEachAndPersonalOnesComeFirst()
    {
        var options = Options();
        Assert.Equal(["user:u1", "user:u2", "library:pixar", "library:noir", "library:top"], options.Select(option => option.Key));
        var pixar = options.Single(option => option.Id == "pixar");
        Assert.Equal(["Shows", "Movies"], pixar.Libraries.Select(library => library.Name));
        Assert.Equal("Shows, Movies", pixar.LibraryName);
        Assert.Empty(options[0].Libraries);
        Assert.Equal("p-other", options[1].CreatorProfileId);
    }

    [Fact]
    public void LibrariesAreTheNamedOnesThatShowAnOptionSortedByNameAndOfferedOnlyForTwoOrMore()
    {
        var libraries = HomeRowCollections.LibrariesOf(Options());
        Assert.Equal(["Movies", "Shows"], libraries.Select(library => library.Name));
        Assert.Equal(2, HomeRowCollections.EffectiveLibrary(libraries, 2));
        Assert.Null(HomeRowCollections.EffectiveLibrary(libraries, 3));
        Assert.Null(HomeRowCollections.EffectiveLibrary(libraries, null));
        // With one library left the filter is not offered, and an earlier pick stops hiding collections.
        var one = HomeRowCollections.LibrariesOf(Options().Where(option => option.Id is "noir"));
        Assert.Null(HomeRowCollections.EffectiveLibrary(one, 1));
        // A library whose name has not loaded cannot be offered by name.
        Assert.Empty(HomeRowCollections.LibrariesOf([new HomeRowCollectionOption { Id = "x", Libraries = [new(9, "")] }]));
    }

    [Fact]
    public void SearchTypeAndLibraryFiltersCombine()
    {
        var options = Options();
        Assert.Equal(["user:u1", "user:u2", "library:pixar", "library:noir", "library:top"], HomeRowCollections.Filter(options, "", "all", null).Select(option => option.Key));
        Assert.Equal(["user:u2", "library:noir"], HomeRowCollections.Filter(options, "", "smart", null).Select(option => option.Key));
        Assert.Equal(["library:top"], HomeRowCollections.Filter(options, "", "synced", null).Select(option => option.Key));
        Assert.Equal(["library:pixar", "library:noir"], HomeRowCollections.Filter(options, "", "all", 1).Select(option => option.Key));
        Assert.Equal(["library:pixar", "library:top"], HomeRowCollections.Filter(options, "", "all", 2).Select(option => option.Key));
        Assert.Equal(["library:pixar"], HomeRowCollections.Filter(options, "  PIXAR ", "manual", 2).Select(option => option.Key));
        Assert.Empty(HomeRowCollections.Filter(options, "pixar", "synced", 1));
        // A personal collection is in no library: a library pick leaves it out.
        Assert.DoesNotContain(HomeRowCollections.Filter(options, "", "all", 1), option => option.Source == "user");
    }

    [Fact]
    public void ACollectionReadsWithItsLibraryTypeTitleCountAndOwner()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        var options = Options();
        Assert.Equal("Shows, Movies · Manual · 4 titles", HomeRowCollections.Meta(options.Single(option => option.Id == "pixar")));
        Assert.Equal("Movies · Synced list · 20 titles", HomeRowCollections.Meta(options.Single(option => option.Id == "top")));
        Assert.Equal("Manual · 3 titles", HomeRowCollections.Meta(options[0]));
        Assert.Equal("Yours", HomeRowCollections.OwnerLine(options[0], "p-me", id => "Name"));
        Assert.Equal("by Sam", HomeRowCollections.OwnerLine(options[1], "p-me", id => id == "p-other" ? "Sam" : "?"));
        Assert.Null(HomeRowCollections.OwnerLine(options.Single(option => option.Id == "pixar"), "p-me", id => "Name"));
        Assert.Equal("Manual · 3 titles · Yours", HomeRowCollections.MetaWithOwner(options[0], "Yours"));
        Assert.Equal("Manual · 3 titles", HomeRowCollections.MetaWithOwner(options[0], null));
    }

    [Fact]
    public void AnEmptyListSaysWhyItIsEmpty()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Loading collections…", HomeRowCollections.EmptyText(true, false, 0, "", false));
        Assert.Equal("Collections didn't load. Close this and try again.", HomeRowCollections.EmptyText(false, true, 0, "", false));
        Assert.Equal("No collections yet. Make one in Collections first.", HomeRowCollections.EmptyText(false, false, 0, "", false));
        Assert.Equal("No collections match \"noir\".", HomeRowCollections.EmptyText(false, false, 5, " noir ", true));
        Assert.Equal("No collections of this type in this library.", HomeRowCollections.EmptyText(false, false, 5, "", true));
        Assert.Equal("No collections of this type.", HomeRowCollections.EmptyText(false, false, 5, "", false));
        Assert.Equal("Library", HomeRowText.T("web.home.addRow.collection.library"));
        Assert.Equal("All libraries", HomeRowText.T("web.home.addRow.params.allLibraries"));
    }

    // ── Server rows and your own ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARowTheProfileAddedIsYoursAndEveryOtherRowIsTheServers()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal(RowOrigin.Yours, HomeRowCollections.OriginOf(true));
        Assert.Equal(RowOrigin.Server, HomeRowCollections.OriginOf(false));
        Assert.Equal("Server", HomeRowText.T("web.home.rows.line.server"));
        Assert.Equal("Yours", AppStrings.Get("web.home.rows.line.yours"));
        Assert.Equal("Managed by this server. What it shows can’t be changed here; add a row of your own to choose what it shows.", HomeRowText.T("web.home.addRow.shows.serverNote"));
    }

    [Fact]
    public void TheNewTextIsEnglishInEveryLanguageUntilTheCatalogCarriesIt()
    {
        foreach (var language in new[] { English, French })
        {
            using var _ = AppStrings.UseForCurrentFlow(language);
            Assert.Equal("Server", HomeRowText.T("web.home.rows.line.server"));
            Assert.Equal("Move Marvel up", HomeRowText.T("web.home.addRow.collections.moveUp", new { collection = "Marvel" }));
        }
    }

    // ── Names ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheKindsAndGroupsUseTheirCurrentOfficialNamesInEnglish()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("Collections & smart rows", HomeRowText.T("web.home.rows.groups.collections"));
        Assert.Equal("Your collections, or smart rows built from rules", HomeRowText.T("web.home.rows.groups.collectionsBlurb"));
        Assert.Equal("Smart row", HomeRowText.T("web.home.rows.kinds.customFilter"));
        Assert.Equal("Like a smart collection, but only a row. Pick titles with rules like genre and decade.", HomeRowText.T("web.home.rows.kindSentences.customFilter"));
        Assert.Equal("A row of collections", HomeRowText.T("web.home.rows.kinds.collectionPosters"));
        Assert.Equal("Show several collections as posters. Each one opens its collection.", HomeRowText.T("web.home.rows.kindSentences.collectionPosters"));
    }

    [Fact]
    public void TheGalleryAndTheCardsUseTheCollectionRowSources()
    {
        var gallery = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.cs");
        var localization = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.Localization.cs");
        var editor = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.CollectionRow.cs");
        var sectionRow = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml.cs");
        var template = ReadRepoFile("src", "SiloPlayer", "Controls", "SectionRow.xaml");
        Assert.Contains("(\"collections\", \"Collections & smart rows\")", gallery);
        Assert.DoesNotContain("Collections & rules", gallery);
        Assert.Contains("\"collection_posters\" or \"custom_filter\" => \"collections\"", gallery);
        Assert.Contains("case \"collection_posters\":", gallery);
        Assert.Contains("DisplayName = \"Smart row\"", gallery);
        Assert.Contains("HomeRowText.T(\"web.home.rows.groups.collections\")", localization);
        Assert.Contains("MaxCollections", ReadRepoFile("src", "SiloPlayer.Core", "Services", "HomeRowCollections.cs"));
        Assert.Contains("HomeRowCollections.Toggle", editor);
        Assert.Contains("HomeRowCollections.Move", editor);
        Assert.Contains("web.home.addRow.collections.unavailable", editor);
        Assert.Contains("CollectionPosterCard Card=\"{x:Bind}\"", template);
        Assert.Contains("section.HoldsCollections", sectionRow);
        Assert.Contains("CollectionBrowsePage.NavArgs", ReadRepoFile("src", "SiloPlayer", "Controls", "CollectionPosterCard.cs"));
    }

    [Fact]
    public void OnlyTheEditorsOwnCancellationLeavesTheCollectionPickerLoading()
    {
        // A read that timed out, or that the client cancelled, is a failed read: the chooser says so instead of staying on "Loading collections…".
        var editor = ReadRepoFile("src", "SiloPlayer", "Views", "Dialogs", "RecipeGalleryDialog.CollectionRow.cs");
        Assert.Contains("catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }", editor);
        Assert.DoesNotContain("catch (OperationCanceledException) { return; }", editor);
        Assert.DoesNotContain("catch (OperationCanceledException) { throw; }", editor);
        Assert.Contains("timeout.CancelAfter(CollectionReadTimeout)", editor);
        // A mounted collection card takes a renewed poster address from its row's card.
        Assert.Contains("PosterUrlChanged += OnPosterUrlChanged", ReadRepoFile("src", "SiloPlayer", "Controls", "CollectionPosterCard.cs"));
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")) && !File.Exists(Path.Combine(dir, "SiloPlayer.slnx")) && !Directory.Exists(Path.Combine(dir, "src", "SiloPlayer.Core")))
            dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine([dir!, .. parts]));
    }

    private sealed class Handler(string section) : HttpMessageHandler
    {
        public static readonly List<string> Requested = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requested) { if (!path.EndsWith("/capabilities")) Requested.Add(path); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(path.EndsWith("/capabilities") ? "{}" : section) });
        }
    }
}
