using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>A collection page's description, backdrop and access come from the sources the pinned WebUI reads.</summary>
public sealed class CollectionPageHeaderTests
{
    private static ServerCollectionsLibrary Library(int id, int total, params string[] shown)
        => new() { LibraryId = id, LibraryName = "L" + id, TotalCount = total, Collections = shown.Select(c => new ServerCollectionSummary { Id = c }).ToList() };

    [Fact]
    public void ServerCollectionIsLookedUpWhereThePageWasOpenedElseWhereTheListShowsIt()
    {
        Assert.Equal([5], CollectionPageHeaderLoader.ServerLibraryIds("c", 5, null));
        Assert.Null(CollectionPageHeaderLoader.ServerLibraryIds("c", null, null));
        Assert.Null(CollectionPageHeaderLoader.ServerLibraryIds("c", 0, null));
        var libraries = new[] { Library(1, 2, "a", "b"), Library(2, 2, "c", "d") };
        Assert.Equal([2], CollectionPageHeaderLoader.ServerLibraryIds("c", null, libraries));
        // Past a library's teaser cap: every library whose teaser was cut short may list it.
        var capped = new[] { Library(1, 40, "a"), Library(2, 1, "x"), Library(3, 25, "y") };
        Assert.Equal([1, 3], CollectionPageHeaderLoader.ServerLibraryIds("zzz", null, capped));
        Assert.Empty(CollectionPageHeaderLoader.ServerLibraryIds("zzz", null, [Library(2, 1, "x")])!);
    }

    [Fact]
    public async Task ServerCollectionReadsDescriptionAndBackdropFromItsLibraryTab()
    {
        using var wire = new Wire();
        wire.Tab[7] = "{\"library_id\":7,\"collections\":[{\"id\":\"other\",\"title\":\"O\",\"description\":\"no\"},{\"id\":\"c1\",\"title\":\"One\",\"description\":\"  Line one\\nLine two  \",\"backdrop_url\":\"https://img.invalid/wide.jpg?sig=1\",\"backdrop_thumbhash\":\"HASH\"}],\"groups\":[]}";
        var header = await Load(wire, "c1", isUser: false, libraryId: 7);
        Assert.Equal("Line one\nLine two", header.Description);
        Assert.Equal("https://img.invalid/wide.jpg?sig=1", header.BackdropUrl);
        Assert.Equal("HASH", header.BackdropThumbhash);
        Assert.True(header.HasBackdrop);
        Assert.Equal(CollectionPageAccessKind.ReadOnly, header.Access.Kind); Assert.Null(header.Access.OwnerName);
        Assert.Equal(0, wire.ServerListReads);   // opened in a library: the aggregate list is not needed
    }

    [Fact]
    public async Task ServerCollectionWithoutLibraryFindsItsLibraryFromTheServerList()
    {
        using var wire = new Wire { ServerList = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"A\",\"total_count\":1,\"collections\":[{\"id\":\"x\",\"title\":\"X\"}]},{\"library_id\":2,\"library_name\":\"B\",\"total_count\":1,\"collections\":[{\"id\":\"c1\",\"title\":\"One\"}]}]}" };
        wire.Tab[2] = "{\"library_id\":2,\"collections\":[{\"id\":\"c1\",\"title\":\"One\",\"description\":\"From B\"}],\"groups\":[]}";
        var header = await Load(wire, "c1", false, null);
        Assert.Equal("From B", header.Description);
        Assert.False(header.HasBackdrop);
        Assert.Equal([2], wire.TabReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServerCollectionReadsHeaderFromGroupedAndUngroupedTabs(bool inGroup)
    {
        using var wire = new Wire();
        wire.Tab[7] = inGroup
            ? """{"library_id":7,"collections":[{"id":"other"}],"groups":[{"id":"g1","collections":[{"id":"c1","title":"One","description":"  Grouped description  ","backdrop_url":"https://img.invalid/grouped.jpg","backdrop_thumbhash":"HASH"}]}]}"""
            : """{"library_id":7,"groups":[],"ungrouped":{"collections":[{"id":"c1","title":"One","description":"  Grouped description  ","backdrop_url":"https://img.invalid/grouped.jpg","backdrop_thumbhash":"HASH"}]}}""";
        var header = await Load(wire, "c1", false, 7);
        Assert.Equal("Grouped description", header.Description);
        Assert.Equal("https://img.invalid/grouped.jpg", header.BackdropUrl);
        Assert.Equal("HASH", header.BackdropThumbhash);
        Assert.True(header.HasBackdrop);
        Assert.Equal(CollectionPageAccessKind.ReadOnly, header.Access.Kind);
    }

    [Fact]
    public async Task ServerCollectionPastTheTeaserSearchesEveryCutShortLibrary()
    {
        using var wire = new Wire { ServerList = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"A\",\"total_count\":30,\"collections\":[{\"id\":\"x\",\"title\":\"X\"}]},{\"library_id\":2,\"library_name\":\"B\",\"total_count\":1,\"collections\":[{\"id\":\"y\",\"title\":\"Y\"}]},{\"library_id\":3,\"library_name\":\"C\",\"total_count\":30,\"collections\":[{\"id\":\"z\",\"title\":\"Z\"}]}]}" };
        wire.Tab[1] = "{\"library_id\":1,\"collections\":[],\"groups\":[]}";
        wire.Tab[3] = "{\"library_id\":3,\"collections\":[{\"id\":\"late\",\"title\":\"Late\",\"backdrop_thumbhash\":\"TH\"}],\"groups\":[]}";
        var header = await Load(wire, "late", false, null);
        Assert.Equal("TH", header.BackdropThumbhash); Assert.Null(header.BackdropUrl); Assert.True(header.HasBackdrop);
        Assert.Equal([1, 3], wire.TabReads);
    }

    [Fact]
    public async Task UnreadableLibrarySourcesKeepThePlainHeader()
    {
        using var wire = new Wire { FailTabs = true };
        var failed = await Load(wire, "c1", false, 7);
        Assert.Null(failed.Description); Assert.False(failed.HasBackdrop);
        Assert.Equal(CollectionPageAccessKind.ReadOnly, failed.Access.Kind);

        using var noList = new Wire { FailServerList = true };
        var plain = await Load(noList, "c1", false, null);
        Assert.Null(plain.Description); Assert.False(plain.HasBackdrop);
        Assert.Empty(noList.TabReads);

        using var absent = new Wire(); absent.Tab[7] = "{\"library_id\":7,\"collections\":[{\"id\":\"c1\",\"title\":\"One\",\"description\":\"   \",\"backdrop_url\":\"\"}],\"groups\":[]}";
        var blank = await Load(absent, "c1", false, 7);
        Assert.Null(blank.Description); Assert.False(blank.HasBackdrop);
    }

    [Fact]
    public async Task OwnPersonalCollectionIsManagedAndShowsItsDescription()
    {
        using var wire = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"profile_id\":\"me\",\"name\":\"Mine\",\"description\":\" Mine, all mine \",\"collection_type\":\"manual\"}" };
        var header = await Load(wire, "p1", true, null, profile: "me");
        Assert.Equal("Mine, all mine", header.Description);
        Assert.Equal(CollectionPageAccessKind.Manage, header.Access.Kind);
        Assert.False(header.HasBackdrop);   // personal collections have no backdrop
        Assert.Equal(0, wire.ProfileReads);   // names are only needed for someone else's byline
    }

    [Fact]
    public async Task SharedPersonalCollectionIsReadOnlyAndNamesItsOwner()
    {
        using var wire = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"owner\",\"name\":\"Theirs\",\"is_shared\":true}", Profiles = "{\"items\":[{\"id\":\"me\",\"name\":\"Me\"},{\"id\":\"owner\",\"name\":\"Alex\"}]}" };
        var header = await Load(wire, "p1", true, null, profile: "me");
        Assert.Equal(CollectionPageAccessKind.ReadOnly, header.Access.Kind);
        Assert.Equal("Alex", header.Access.OwnerName);
        Assert.Null(header.Description);

        using var unknown = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"gone\",\"name\":\"Theirs\"}", Profiles = "{\"items\":[{\"id\":\"me\",\"name\":\"Me\"}]}" };
        Assert.Equal("Another profile", (await Load(unknown, "p1", true, null, profile: "me")).Access.OwnerName);

        using var noNames = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"owner\",\"name\":\"Theirs\"}", FailProfiles = true };
        var unsigned = await Load(noNames, "p1", true, null, profile: "me");
        Assert.Equal(CollectionPageAccessKind.ReadOnly, unsigned.Access.Kind); Assert.Null(unsigned.Access.OwnerName);
    }

    [Fact]
    public async Task HeaderLoadThenServerChangeAllowsDeleteRetryWithTheCurrentRevision()
    {
        using var wire = new Wire
        {
            Personal = """{"id":"p1","creator_profile_id":"me","name":"Weekly","collection_type":"mdblist","updated_at":"A"}""",
            PersonalETag = "\"A\"",
            Capabilities = "{\"imports\":true}",
        };
        var client = wire.Client();
        var collections = new CollectionsApi(client);
        var header = await CollectionPageHeaderLoader.LoadAsync(collections, new CatalogApi(client), new AuthApi(client),
            "p1", true, null, "me", CancellationToken.None);
        Assert.Equal(CollectionPageAccessKind.Manage, header.Access.Kind);

        // A sync (including one from another device) changes both the ETag and the saved baseline.
        wire.Personal = """{"id":"p1","creator_profile_id":"me","name":"Weekly","collection_type":"mdblist","updated_at":"B"}""";
        wire.PersonalETag = "\"B\"";
        var conflict = await Assert.ThrowsAsync<ApiException>(() => collections.DeleteCollectionAsync("p1"));
        Assert.Equal(412, conflict.StatusCode);
        Assert.Equal(["\"A\""], wire.DeleteTags);   // never automatically retry a destructive action

        await collections.DeleteCollectionAsync("p1");
        Assert.Equal(["\"A\"", "\"B\""], wire.DeleteTags);
        Assert.Equal(2, wire.PersonalReads);   // the dialog's retry reads a fresh revision without reopening the page
    }

    [Fact]
    public async Task HeaderLoadThenServerChangeAllowsSharingRetryWithTheCurrentRevision()
    {
        using var wire = new Wire
        {
            Personal = """{"id":"p1","creator_profile_id":"me","name":"Weekly","collection_type":"mdblist","updated_at":"A"}""",
            PersonalETag = "\"A\"",
            Capabilities = "{\"imports\":true}",
        };
        var client = wire.Client();
        var collections = new CollectionsApi(client);
        var header = await CollectionPageHeaderLoader.LoadAsync(collections, new CatalogApi(client), new AuthApi(client),
            "p1", true, null, "me", CancellationToken.None);
        Assert.Equal(CollectionPageAccessKind.Manage, header.Access.Kind);

        wire.Personal = """{"id":"p1","creator_profile_id":"me","name":"Weekly","collection_type":"mdblist","updated_at":"B"}""";
        wire.PersonalETag = "\"B\"";
        var conflict = await Assert.ThrowsAsync<ApiException>(() => collections.UpdateCollectionAsync("p1", new() { IsShared = true }));
        Assert.Equal(412, conflict.StatusCode);
        Assert.Equal(["\"A\""], wire.PatchTags);   // the conflict stays visible; there is no automatic retry

        await collections.UpdateCollectionAsync("p1", new() { IsShared = true });
        Assert.Equal(["\"A\"", "\"B\""], wire.PatchTags);
        Assert.Equal(2, wire.PersonalReads);   // retry retires both the header's revision and its old baseline
    }

    private const string ThreeLibraries = "{\"items\":[{\"id\":1,\"name\":\"Movies\"},{\"id\":2,\"name\":\"Shows\"},{\"id\":3,\"name\":\"Kids\"}]}";

    [Fact]
    public async Task OwnerOfASyncedListGetsSyncOnlyWhenTheServerCanImport()
    {
        const string synced = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"name\":\"Weekly\",\"collection_type\":\"mdblist\",\"is_shared\":true}";
        using var can = new Wire { Personal = synced, Capabilities = "{\"imports\":true}" };
        var header = await Load(can, "p1", true, null, profile: "me");
        Assert.Equal(new PersonalCollectionFacts("Weekly", true, true, true), header.Access.Personal);
        Assert.Equal(new CollectionHeroMenu(true, true, []), CollectionHeroActions.Menu(header.Access));

        using var cannot = new Wire { Personal = synced, Capabilities = "{\"imports\":false}" };
        Assert.False((await Load(cannot, "p1", true, null, profile: "me")).Access.Personal!.ImportsAvailable);
        using var unknown = new Wire { Personal = synced, Capabilities = "{}" };
        Assert.False((await Load(unknown, "p1", true, null, profile: "me")).Access.Personal!.ImportsAvailable);   // imports not advertised
        using var unreadable = new Wire { Personal = synced, FailCapabilities = true };
        var failedCapabilities = await Load(unreadable, "p1", true, null, profile: "me");
        Assert.False(failedCapabilities.Access.Personal!.ImportsAvailable);
        Assert.Equal(CollectionPageAccessKind.Manage, failedCapabilities.Access.Kind);   // Delete… stays
        Assert.Equal(new CollectionHeroMenu(false, true, []), CollectionHeroActions.Menu(failedCapabilities.Access));
    }

    [Fact]
    public async Task OwnerOfAManualOrSmartCollectionNeverReadsCapabilitiesAndOffersDeleteOnly()
    {
        using var manual = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"name\":\"Mine\",\"collection_type\":\"manual\"}", Capabilities = "{\"imports\":true}" };
        var header = await Load(manual, "p1", true, null, profile: "me");
        Assert.Equal(0, manual.CapabilityReads); Assert.Equal(0, manual.LibraryReads);
        Assert.False(header.Access.Personal!.IsSynced);
        Assert.Equal(new CollectionHeroMenu(false, true, []), CollectionHeroActions.Menu(header.Access));
        using var smart = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"name\":\"Smart\",\"collection_type\":\"smart\"}", Capabilities = "{\"imports\":true}" };
        Assert.False(CollectionHeroActions.Menu((await Load(smart, "p1", true, null, profile: "me")).Access).ShowSync);
    }

    [Fact]
    public async Task SharedPersonalCollectionOffersTheLibraryPagesItMatches()
    {
        using var everyLibrary = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"owner\",\"name\":\"Theirs\",\"collection_type\":\"manual\"}", Libraries = ThreeLibraries };
        var all = await Load(everyLibrary, "p1", true, null, profile: "me");
        Assert.Equal(["Movies", "Shows", "Kids"], all.Access.LibraryPages.Select(page => page.Name));
        Assert.Equal(3, CollectionHeroActions.Menu(all.Access).LibraryPages.Count);
        Assert.False(CollectionHeroActions.Menu(all.Access).ShowDelete);   // never Delete or Sync on someone else's collection
        Assert.False(CollectionHeroActions.Menu(all.Access).ShowSync);

        using var restricted = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"owner\",\"name\":\"Theirs\",\"collection_type\":\"mdblist\",\"source_config\":{\"library_ids\":[3,99]}}", Libraries = ThreeLibraries };
        var one = await Load(restricted, "p1", true, null, profile: "me");
        Assert.Equal([3], one.Access.LibraryPages.Select(page => page.Id));   // a library the viewer cannot see is not offered

        using var unreadable = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"owner\",\"name\":\"Theirs\"}", FailLibraries = true };
        var none = await Load(unreadable, "p1", true, null, profile: "me");
        Assert.Equal(CollectionPageAccessKind.ReadOnly, none.Access.Kind);   // Add to my Home still works
        Assert.Empty(none.Access.LibraryPages);
        Assert.False(CollectionHeroActions.Menu(none.Access).HasEntries);
    }

    [Fact]
    public async Task ServerCollectionOffersTheLibraryItWasOpenedInFirstThenTheOthers()
    {
        using var opened = new Wire { Libraries = ThreeLibraries };
        opened.Tab[2] = "{\"library_id\":2,\"collections\":[{\"id\":\"c1\",\"title\":\"One\"}],\"groups\":[]}";
        var header = await Load(opened, "c1", false, 2);
        Assert.Equal(["Shows", "Movies", "Kids"], header.Access.LibraryPages.Select(page => page.Name));
        Assert.Equal(CollectionPageAccessKind.ReadOnly, header.Access.Kind);   // no Edit, Sync or Delete: Desktop has no administrator access
        Assert.False(CollectionHeroActions.Menu(header.Access).ShowDelete);

        // Opened without a library: every page counts as bound, in the viewer's own order.
        using var unpinned = new Wire { Libraries = ThreeLibraries, ServerList = "{\"libraries\":[{\"library_id\":2,\"library_name\":\"Shows\",\"total_count\":1,\"collections\":[{\"id\":\"c1\",\"title\":\"One\"}]}]}" };
        Assert.Equal(["Movies", "Shows", "Kids"], (await Load(unpinned, "c1", false, null)).Access.LibraryPages.Select(page => page.Name));

        using var unreadable = new Wire { FailLibraries = true };
        Assert.Empty((await Load(unreadable, "c1", false, 2)).Access.LibraryPages);
    }

    [Fact]
    public async Task UnreadablePersonalCollectionOrUnknownProfileOffersNothing()
    {
        using var failed = new Wire { FailPersonal = true };
        var header = await Load(failed, "p1", true, null, profile: "me");
        Assert.Equal(CollectionPageAccessKind.None, header.Access.Kind); Assert.Null(header.Description);

        using var noProfile = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"name\":\"Mine\",\"description\":\"d\"}" };
        var unknown = await Load(noProfile, "p1", true, null, profile: null);
        Assert.Equal(CollectionPageAccessKind.None, unknown.Access.Kind); Assert.Equal("d", unknown.Description);
    }

    [Fact]
    public async Task CancelledLoadStopsInsteadOfPretendingToBeEmpty()
    {
        using var wire = new Wire { Personal = "{\"id\":\"p1\",\"creator_profile_id\":\"me\",\"name\":\"Mine\"}" };
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var client = wire.Client();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectionPageHeaderLoader.LoadAsync(new CollectionsApi(client), new CatalogApi(client), new AuthApi(client), "p1", true, null, "me", cancelled.Token));
    }

    private static Task<CollectionPageHeader> Load(Wire wire, string id, bool isUser, int? libraryId, string? profile = null)
    {
        var client = wire.Client();
        return CollectionPageHeaderLoader.LoadAsync(new CollectionsApi(client), new CatalogApi(client), new AuthApi(client), id, isUser, libraryId, profile, CancellationToken.None);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public string? Personal, Profiles, ServerList, Libraries, Capabilities;
        public string? PersonalETag;
        public int PersonalReads;
        public readonly List<string> DeleteTags = [];
        public readonly List<string> PatchTags = [];
        public bool FailPersonal, FailProfiles, FailTabs, FailServerList, FailLibraries, FailCapabilities;
        public int LibraryReads, CapabilityReads;
        public readonly Dictionary<int, string> Tab = [];
        public readonly List<int> TabReads = [];
        public int ServerListReads, ProfileReads;
        public SiloApiClient Client() { var result = new SiloApiClient(new HttpClient(this)); result.SetBaseUrl("https://collection-header.invalid"); return result; }
        private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
        private static HttpResponseMessage Fail() => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"unavailable\"}") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/collections/server") { Interlocked.Increment(ref ServerListReads); return Task.FromResult(FailServerList ? Fail() : Ok(ServerList ?? "{\"libraries\":[]}")); }
            if (path == "/api/v2/profiles") { Interlocked.Increment(ref ProfileReads); return Task.FromResult(FailProfiles ? Fail() : Ok(Profiles ?? "{\"items\":[]}")); }
            if (path.StartsWith("/api/v2/library/") && path.EndsWith("/collections"))
            {
                var id = int.Parse(path["/api/v2/library/".Length..^"/collections".Length]);
                lock (TabReads) TabReads.Add(id);
                return Task.FromResult(FailTabs ? Fail() : Ok(Tab.GetValueOrDefault(id, "{\"library_id\":" + id + ",\"collections\":[],\"groups\":[]}")));
            }
            if (path == "/api/v2/user/libraries") { Interlocked.Increment(ref LibraryReads); return Task.FromResult(FailLibraries ? Fail() : Ok(Libraries ?? "{\"items\":[]}")); }
            if (path == "/api/v2/collections/capabilities") { Interlocked.Increment(ref CapabilityReads); return Task.FromResult(FailCapabilities ? Fail() : Ok(Capabilities ?? "{}")); }
            if (path.StartsWith("/api/v2/collections/"))
            {
                if (request.Method == HttpMethod.Delete && PersonalETag != null)
                {
                    var tag = request.Headers.IfMatch.Single().Tag;
                    DeleteTags.Add(tag);
                    return Task.FromResult(tag == PersonalETag
                        ? new HttpResponseMessage(HttpStatusCode.NoContent)
                        : new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{\"status\":412,\"detail\":\"Changed elsewhere\"}") });
                }
                if (request.Method == HttpMethod.Patch && PersonalETag != null)
                {
                    var tag = request.Headers.IfMatch.Single().Tag;
                    PatchTags.Add(tag);
                    if (tag != PersonalETag)
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{\"status\":412,\"detail\":\"Changed elsewhere\"}") });
                }
                if (request.Method == HttpMethod.Get) Interlocked.Increment(ref PersonalReads);
                var response = FailPersonal ? Fail() : Ok(Personal ?? "{}");
                if (PersonalETag != null) response.Headers.ETag = new(PersonalETag);
                return Task.FromResult(response);
            }
            return Task.FromResult(Ok("{}"));
        }
    }
}
