using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// The wall may only say a view is empty once the read behind it succeeded, and the server's cards do not wait
/// for the profile's own collections (WebUI Collections.tsx: personalLoaded / profilesLoaded / server.isSuccess).
/// </summary>
public sealed class CollectionsWallLoadingTests
{
    [Fact]
    public async Task LoneLibraryRestWaitsForActualViewportOrExplicitAdmission()
    {
        using var wire = new Wire { ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1","title":"One"}]}]}""" };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(0, wire.LibraryTabReads);
        Assert.Equal(1, vm.NextServerLibrary?.LibraryId);
        await vm.LoadServerLibraryAsync(1);
        Assert.Equal(1, wire.LibraryTabReads);
    }

    private const string OneLibrary = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"Movies\",\"total_count\":1,\"collections\":[{\"id\":\"s1\",\"title\":\"Server one\"}]}]}";

    [Fact]
    public async Task HeldRestDeduplicatesAndSerializesLibraries()
    {
        using var wire = new Wire { ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]},{"library_id":2,"library_name":"Shows","total_count":2,"collections":[{"id":"t1"}]}]}""", HoldLibraryTab = true };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        var first = vm.LoadServerLibraryAsync(1);
        await Until(() => wire.LibraryTabReads == 1);
        await vm.LoadServerLibraryAsync(1);
        await vm.LoadServerLibraryAsync(2);
        Assert.Equal(1, wire.LibraryTabReads);
        wire.LibraryTab.SetResult(Wire.Ok("""{"library_id":1,"collections":[{"id":"s1"},{"id":"s2"},{"id":"s2"}]}"""));
        await first;
        Assert.Equal(["s1", "t1", "s2"], vm.GetServerCollectionCards().Select(card => card.Collection.Id));
        Assert.Equal(2, vm.NextServerLibrary?.LibraryId);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("profile")]
    [InlineData("access")]
    [InlineData("admission")]
    public async Task RetiredHeldRestCannotAppendOrKeepCancelledLibraryAdmitted(string retirement)
    {
        using var wire = new Wire { ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]}]}""", HoldLibraryTab = true };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        var admitted = true;
        var first = vm.LoadServerLibraryAsync(1, isCurrent: () => admitted);
        await Until(() => wire.LibraryTabReads == 1);
        if (retirement == "profile") client.SetProfile("new-owner");
        else if (retirement == "access") client.InvalidateAccessContext();
        else if (retirement == "admission") admitted = false;
        else vm.CancelServerCollectionLoads();
        wire.LibraryTab.SetResult(Wire.Ok("""{"library_id":1,"collections":[{"id":"stale"}]}"""));
        await first;
        Assert.False(vm.HasServerLibraryRest(1));
        Assert.False(vm.IsLoadingServerLibraryRest);   // a retired read does not leave the wall busy
        if (retirement == "cancel") Assert.Equal(1, vm.NextServerLibrary?.LibraryId);
        else if (retirement != "admission") Assert.False(vm.HasCurrentServerCollections);
    }

    [Fact]
    public async Task TimedOutLibraryRestReportsThatLibraryInsteadOfLeavingItToBeAdmittedAgain()
    {
        using var wire = new Wire { ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]},{"library_id":2,"library_name":"Shows","total_count":2,"collections":[{"id":"t1"}]}]}""", TimeOutLibraryTab = true };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        // The wall's own owner and token are not cancelled: the request alone gave up.
        await vm.LoadServerLibraryAsync(1, isCurrent: () => true);
        Assert.Equal(1, wire.LibraryTabReads);
        Assert.NotNull(vm.ServerLibraryError(1)); Assert.Null(vm.ServerLibraryError(2));
        Assert.False(vm.HasServerLibraryRest(1)); Assert.False(vm.IsLoadingServerLibraryRest);
        Assert.Equal(2, vm.NextServerLibrary?.LibraryId);   // the timed-out library is not the next automatic read
        Assert.Equal(["s1", "t1"], vm.GetServerCollectionCards().Select(card => card.Collection.Id));

        wire.TimeOutLibraryTab = false;
        await vm.LoadServerLibraryAsync(1);   // Retry
        Assert.Equal(2, wire.LibraryTabReads);
        Assert.Null(vm.ServerLibraryError(1)); Assert.True(vm.HasServerLibraryRest(1));
    }

    [Fact]
    public async Task MetadataLanguageChangeRetiresAHeldRestWithoutAnErrorAndLeavesTheLibraryAdmissible()
    {
        using var wire = new Wire { ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]}]}""", HoldLibraryTab = true };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        var first = vm.LoadServerLibraryAsync(1);
        await Until(() => wire.LibraryTabReads == 1);
        client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.LibraryTab.TrySetResult(Wire.Ok("""{"library_id":1,"collections":[{"id":"stale"}]}"""));
        await first;
        Assert.False(vm.HasServerLibraryRest(1)); Assert.False(vm.IsLoadingServerLibraryRest);
        Assert.Null(vm.ServerLibraryError(1));
        Assert.Equal(1, vm.NextServerLibrary?.LibraryId);
    }

    [Fact]
    public async Task RefreshDropsTheRestOfALibraryItsTeaserNowHoldsInFull()
    {
        using var wire = new Wire
        {
            ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]}]}""",
            LibraryTabBody = """{"library_id":1,"collections":[{"id":"s1"},{"id":"s2"},{"id":"s3"}]}""",
        };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        await vm.LoadServerLibraryAsync(1);
        Assert.Equal(["s1", "s2", "s3"], vm.GetServerCollectionCards().Select(card => card.Collection.Id));
        // The server now lists one collection, all of it in the teaser: the cards read past the old teaser are gone.
        wire.ServerBody = OneLibrary;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.False(vm.HasServerLibraryRest(1));
        Assert.Equal(["s1"], vm.GetServerCollectionCards().Select(card => card.Collection.Id));
        Assert.Null(vm.NextServerLibrary); Assert.Null(vm.ServerLibraryError(1));
        Assert.Equal(1, wire.LibraryTabReads);
    }

    [Fact]
    public async Task AllPillCountsTheWallOnceALibrarysRestHasLoaded()
    {
        using var wire = new Wire
        {
            ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"s1"}]}]}""",
            LibraryTabBody = """{"library_id":1,"collections":[{"id":"s1"},{"id":"own"},{"id":"s3"}]}""",
        };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        int? All() => CollectionsWall.Pills(vm.ServerLibraries.ToArray(), 1, 0, ["own"], vm.ServerLibraryRestIds).Single(pill => pill.Value == CollectionsWall.All).Count;
        Assert.Null(All());   // two cards are still unread and could be the personal one
        await vm.LoadServerLibraryAsync(1);
        Assert.Equal(3, All());   // s1, s3 and the personal collection the library also lists
    }

    [Fact]
    public async Task EveryReadSucceedingMarksTheViewLoadedEvenWhenEmpty()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = Vm(client);
        Assert.False(vm.PersonalLoaded); Assert.False(vm.ProfilesLoaded); Assert.False(vm.ServerLoaded);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.PersonalLoaded); Assert.True(vm.ProfilesLoaded); Assert.True(vm.ServerLoaded);
        Assert.Empty(vm.Collections); Assert.Empty(vm.ServerLibraries);
        Assert.False(vm.IsLoading); Assert.False(vm.IsLoadingServerCollections); Assert.False(vm.IsLoadingProfiles);
    }

    [Fact]
    public async Task FailedReadsNeverCountAsNone()
    {
        using var wire = new Wire { FailPersonal = true, FailProfiles = true, FailServer = true }; var client = wire.Client();
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.False(vm.PersonalLoaded); Assert.False(vm.ProfilesLoaded); Assert.False(vm.ServerLoaded);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsLoading); Assert.False(vm.IsLoadingServerCollections);
        Assert.Null(CollectionsWall.EmptyMessage("all", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, vm.Collections.Count, 0, CollectionsWall.ServerCount(vm.ServerLibraries.ToArray())));
        Assert.Null(CollectionsWall.EmptyMessage("mine", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, vm.Collections.Count, 0, 0));
    }

    [Fact]
    public async Task OneFailedReadLeavesTheOthersLoaded()
    {
        using var wire = new Wire { FailServer = true }; var client = wire.Client();
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.PersonalLoaded); Assert.True(vm.ProfilesLoaded); Assert.False(vm.ServerLoaded);
        Assert.Equal("You haven't made any collections yet.", CollectionsWall.EmptyMessage("mine", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, 0, 0, 0));
        Assert.Null(CollectionsWall.EmptyMessage("all", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, 0, 0, 0));
    }

    [Fact]
    public async Task FirstServerFailureHasAnIndependentErrorAndRetryClearsIt()
    {
        using var wire = new Wire { FailServer = true }; var client = wire.Client();
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.PersonalLoaded); Assert.True(vm.ProfilesLoaded); Assert.False(vm.ServerLoaded);
        Assert.Empty(vm.Collections); Assert.Empty(vm.ServerLibraries);
        Assert.Null(vm.ErrorMessage);
        Assert.StartsWith("Couldn't load server collections:", vm.ServerCollectionsErrorMessage);
        Assert.False(vm.IsLoadingServerCollections);
        Assert.Null(CollectionsWall.EmptyMessage("all", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, 0, 0, 0));

        wire.FailServer = false;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.ServerLoaded);
        Assert.Null(vm.ServerCollectionsErrorMessage);
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("all", vm.PersonalLoaded, vm.ProfilesLoaded, vm.ServerLoaded, 0, 0, 0));
    }

    [Fact]
    public async Task ARefreshThatFailsKeepsWhatAnEarlierReadLoaded()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        wire.FailPersonal = wire.FailProfiles = wire.FailServer = true;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.PersonalLoaded); Assert.True(vm.ProfilesLoaded); Assert.True(vm.ServerLoaded);
        // A refresh over a loaded empty account does not bring the skeleton back.
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task ServerCardsDoNotWaitForTheProfilesOwnCollections()
    {
        using var wire = new Wire { ServerBody = OneLibrary, HoldPersonal = true }; var client = wire.Client();
        var vm = Vm(client);
        var load = vm.LoadCollectionsCommand.ExecuteAsync(null);
        await wire.PersonalStarted.Task;
        await Until(() => vm.ServerLoaded);
        Assert.Single(vm.ServerLibraries);
        Assert.True(vm.IsLoading); Assert.False(vm.PersonalLoaded); Assert.False(vm.IsLoadingServerCollections);
        wire.Personal.SetResult(Wire.Ok("{\"items\":[],\"groups\":[]}"));
        await load;
        Assert.True(vm.PersonalLoaded); Assert.False(vm.IsLoading);
    }

    private const string OneOwn = "{\"items\":[{\"id\":\"own\",\"name\":\"Mine\"}],\"groups\":[]}";

    // The All pill as the page asks for it: what has been read so far, with the view model's loaded flags.
    private static int? AllCount(CollectionsViewModel vm)
        => CollectionsWall.Pills(vm.ServerLibraries.ToArray(), vm.Collections.Count, 0, vm.Collections.Select(collection => collection.Id).ToArray(),
            vm.ServerLibraryRestIds, vm.PersonalLoaded, vm.ServerLoaded).Single(pill => pill.Value == CollectionsWall.All).Count;

    [Fact]
    public async Task AllPillHasNoCountWhileTheProfilesOwnCollectionsAreStillOnTheirWay()
    {
        using var wire = new Wire { ServerBody = OneLibrary, HoldPersonal = true }; var client = wire.Client();
        var vm = Vm(client);
        var load = vm.LoadCollectionsCommand.ExecuteAsync(null);
        await wire.PersonalStarted.Task;
        await Until(() => vm.ServerLoaded);
        Assert.False(vm.PersonalLoaded);
        Assert.Null(AllCount(vm));   // the server's one card is not the whole wall while the profile's own are unread
        wire.Personal.SetResult(Wire.Ok(OneOwn));
        await load;
        Assert.Equal(2, AllCount(vm));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllPillHasNoCountWhenTheServerOrPersonalReadFailed(bool failServer)
    {
        using var wire = new Wire { ServerBody = OneLibrary, PersonalBody = OneOwn, FailServer = failServer, FailPersonal = !failServer }; var client = wire.Client();
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(!failServer, vm.ServerLoaded); Assert.Equal(failServer, vm.PersonalLoaded);
        Assert.Null(AllCount(vm));   // the half that was read is not the total, and stays unknown after the failure
        wire.FailServer = wire.FailPersonal = false;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(2, AllCount(vm));
    }

    [Fact]
    public async Task ProfilesLoadingIsTrackedSoASavedSharedFilterSurvivesIt()
    {
        using var wire = new Wire { HoldProfiles = true }; var client = wire.Client();
        var vm = Vm(client);
        var load = vm.LoadCollectionsCommand.ExecuteAsync(null);
        await wire.ProfilesStarted.Task;
        Assert.True(vm.IsLoadingProfiles); Assert.False(vm.ProfilesLoaded);
        wire.Profiles.SetResult(Wire.Ok("{\"items\":[{\"id\":\"a\",\"name\":\"A\"},{\"id\":\"b\",\"name\":\"B\"}]}"));
        await load;
        Assert.False(vm.IsLoadingProfiles); Assert.True(vm.ProfilesLoaded); Assert.Equal(2, vm.Profiles.Count);
    }

    [Fact]
    public async Task ASwitchedProfileForgetsWhatThePreviousOneLoaded()
    {
        using var wire = new Wire(); var client = wire.Client(); client.SetProfile("old");
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.True(vm.PersonalLoaded);
        wire.FailPersonal = wire.FailProfiles = wire.FailServer = true;
        client.SetProfile("current");
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        // The new profile's reads failed, so nothing it has is "loaded", and the old profile's data is gone.
        Assert.False(vm.PersonalLoaded); Assert.False(vm.ProfilesLoaded); Assert.False(vm.ServerLoaded);
        Assert.Empty(vm.Collections); Assert.Empty(vm.ServerLibraries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedCollectionsRemainAvailableWhileProfileNamesArePendingOrFailed(bool pending)
    {
        using var wire = new Wire
        {
            PersonalBody = """{"items":[{"id":"shared","creator_profile_id":"other","name":"Weekend picks","is_shared":true}],"groups":[]}""",
            HoldProfiles = pending,
            FailProfiles = !pending,
        };
        var client = wire.Client(); client.SetProfile("me");
        var vm = Vm(client);
        var load = vm.LoadCollectionsCommand.ExecuteAsync(null);
        await Until(() => vm.PersonalLoaded);
        if (!pending) await load;
        Assert.False(vm.ProfilesLoaded);
        Assert.Empty(vm.Profiles);
        var partition = PersonalCollectionOwnership.Split(vm.Collections, "me", vm.Profiles.ToArray());
        var shared = Assert.Single(partition.Shared);
        Assert.Equal("Another profile", shared.Name);
        Assert.Equal("Weekend picks", Assert.Single(shared.Collections).Name);
        var pills = CollectionsWall.Pills([], partition.Own.Count, shared.Collections.Count);
        Assert.Equal(1, pills.Single(pill => pill.Value == CollectionsWall.All).Count);
        Assert.Equal(1, pills.Single(pill => pill.Value == CollectionsWall.Shared).Count);
        if (pending)
        {
            wire.Profiles.SetResult(Wire.Ok("{\"items\":[{\"id\":\"me\",\"name\":\"Me\"},{\"id\":\"other\",\"name\":\"Alex\"}]}"));
            await load;
            Assert.Equal("Alex", Assert.Single(PersonalCollectionOwnership.Split(vm.Collections, "me", vm.Profiles.ToArray()).Shared).Name);
        }
    }

    [Fact]
    public async Task FailedLibraryRestKeepsTheFirstCardsAndReportsOnlyThatLibrary()
    {
        using var wire = new Wire { ServerBody = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"Movies\",\"total_count\":3,\"collections\":[{\"id\":\"s1\",\"title\":\"One\"}]},{\"library_id\":2,\"library_name\":\"Shows\",\"total_count\":1,\"collections\":[{\"id\":\"t1\",\"title\":\"Two\"}]}]}", FailLibraryTab = true };
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        await vm.LoadServerLibraryAsync(1);
        Assert.NotNull(vm.ServerLibraryError(1)); Assert.Null(vm.ServerLibraryError(2));
        Assert.Equal(["One", "Two"], vm.GetServerCollectionCards().Select(card => card.Collection.Title));
        Assert.Equal(1, wire.LibraryTabReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryRestIncludesGroupedAndUngroupedCollectionsInOrderWithoutDuplicates(bool includeFlat)
    {
        using var wire = new Wire
        {
            ServerBody = """{"libraries":[{"library_id":1,"library_name":"Movies","total_count":4,"collections":[{"id":"s1","title":"Teaser"}]},{"library_id":2,"library_name":"Shows","total_count":0,"collections":[]}]}""",
            LibraryTabBody = """
                {"library_id":1,"groups":[
                    {"id":"g1","collections":[{"id":"s1","title":"Repeated teaser"},{"id":"s2","title":"Repeated flat"},
                        {"id":"s3","title":"Grouped","poster_url":"https://images.invalid/group.jpg","poster_thumbhash":"GROUP","item_count":8,"featured":true}]},
                    {"id":"g2","collections":[{"id":"s3","title":"Repeated group"}]}],
                 "ungrouped":{"collections":[{"id":"s3","title":"Repeated grouped"},
                    {"id":"s4","title":"Ungrouped","poster_url":"https://images.invalid/other.jpg","poster_thumbhash":"OTHER","item_count":5,"featured":false}]}}
                """,
        };
        if (includeFlat)
            wire.LibraryTabBody = wire.LibraryTabBody.Replace("\"library_id\":1,", "\"library_id\":1,\"collections\":[{\"id\":\"s2\",\"title\":\"Flat\"}],");
        var client = wire.Client(); var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(["s1"], vm.GetServerCollectionCards().Select(card => card.Collection.Id));
        await vm.LoadServerLibraryAsync(1);
        Assert.Null(vm.ServerLibraryError(1)); Assert.True(vm.HasServerLibraryRest(1));
        var cards = vm.GetServerCollectionCards(1);
        Assert.Equal(["s1", "s2", "s3", "s4"], cards.Select(card => card.Collection.Id));
        Assert.Equal(["Teaser", includeFlat ? "Flat" : "Repeated flat", "Grouped", "Ungrouped"], cards.Select(card => card.Collection.Title));
        Assert.Equal(cards.Select(card => card.Collection.Id), vm.GetServerCollectionCards().Select(card => card.Collection.Id));
        Assert.All(cards, card => Assert.Equal(1, card.Library!.LibraryId));
        var grouped = cards[2].Collection;
        Assert.Equal("https://images.invalid/group.jpg", grouped.PosterUrl); Assert.Equal("GROUP", grouped.PosterThumbhash);
        Assert.Equal(8, grouped.ItemCount); Assert.True(grouped.Featured);
        var ungrouped = cards[3].Collection;
        Assert.Equal("https://images.invalid/other.jpg", ungrouped.PosterUrl); Assert.Equal("OTHER", ungrouped.PosterThumbhash);
        Assert.Equal(5, ungrouped.ItemCount); Assert.False(ungrouped.Featured);
        Assert.Equal(1, wire.LibraryTabReads);
    }

    [Fact]
    public async Task DeletingIncludedCollectionRemovesEveryCachedServerCopyOnlyAfterSuccess()
    {
        const string snapshot = """{"id":"c1","name":"Mine","creator_profile_id":"me","include_in_server_collections":true}""";
        using var wire = new Wire
        {
            PersonalBody = "{\"items\":[" + snapshot + "],\"groups\":[]}",
            PersonalSnapshotBody = snapshot,
            ServerBody = """
                {"libraries":[
                    {"library_id":1,"library_name":"Movies","total_count":3,"collections":[{"id":"c1","title":"Included"},{"id":"s1","title":"One"}]},
                    {"library_id":2,"library_name":"Shows","total_count":3,"collections":[{"id":"t1","title":"Two"}]}]}
                """,
            LibraryTabBody = """{"collections":[{"id":"c1","title":"Included"},{"id":"s2","title":"Rest"}]}""",
            FailDelete = true,
        };
        var client = wire.Client(); client.SetProfile("me");
        var vm = Vm(client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        await vm.LoadServerLibraryAsync(1);
        await vm.LoadServerLibraryAsync(2);
        Assert.Contains(vm.GetServerCollectionCards(1), card => card.Collection.Id == "c1");
        Assert.Contains(vm.GetServerCollectionCards(2), card => card.Collection.Id == "c1");
        var revision = vm.ServerCollectionRevision;

        await vm.DeleteCollectionCommand.ExecuteAsync("c1");
        Assert.NotNull(vm.ErrorMessage);
        Assert.Single(vm.Collections);
        Assert.All(vm.ServerLibraries, library => Assert.Equal(3, library.TotalCount));
        Assert.Equal(revision, vm.ServerCollectionRevision);
        Assert.Contains(vm.GetServerCollectionCards(1), card => card.Collection.Id == "c1");
        Assert.Contains(vm.GetServerCollectionCards(2), card => card.Collection.Id == "c1");

        wire.FailDelete = false;
        await vm.DeleteCollectionCommand.ExecuteAsync("c1");
        Assert.Equal(2, wire.DeleteRequests); // the matching wall baseline permits both conditional DELETEs
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(vm.Collections); Assert.True(vm.IsEmpty);
        Assert.True(vm.ServerCollectionRevision > revision);
        Assert.All(vm.ServerLibraries, library =>
        {
            Assert.Equal(2, library.TotalCount);
            Assert.DoesNotContain(library.Collections, collection => collection.Id == "c1");
            Assert.DoesNotContain(vm.GetServerCollectionCards(library.LibraryId), card => card.Collection.Id == "c1");
            Assert.True(vm.HasServerLibraryRest(library.LibraryId));
            Assert.False(vm.IsServerLibraryLoading(library.LibraryId));
            Assert.Null(vm.ServerLibraryError(library.LibraryId));
        });
        Assert.Equal(["s1", "s2"], vm.GetServerCollectionCards(1).Select(card => card.Collection.Id));
        Assert.Equal(["t1", "s2"], vm.GetServerCollectionCards(2).Select(card => card.Collection.Id));
        Assert.Null(vm.NextServerLibrary);
    }

    private static CollectionsViewModel Vm(SiloApiClient client)
        => new(new CollectionsApi(client), new CatalogApi(client), new AuthApi(client), client);

    private static async Task Until(Func<bool> done)
    {
        for (var attempt = 0; attempt < 200 && !done(); attempt++) await Task.Delay(10);
        Assert.True(done(), "state did not settle");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public string ServerBody = "{\"libraries\":[]}";
        public string PersonalBody = "{\"items\":[],\"groups\":[]}";
        public string LibraryTabBody = "{\"library_id\":1,\"collections\":[],\"groups\":[]}";
        public string PersonalSnapshotBody = "{}";
        public bool FailPersonal, FailProfiles, FailServer, FailLibraryTab, HoldPersonal, HoldProfiles, FailDelete;
        public bool HoldLibraryTab, TimeOutLibraryTab;
        public TaskCompletionSource<HttpResponseMessage> LibraryTab { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LibraryTabReads, DeleteRequests;
        public TaskCompletionSource PersonalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ProfilesStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Personal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Profiles { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient Client() { var result = new SiloApiClient(new HttpClient(this)); result.SetBaseUrl("https://collections-wall.invalid"); return result; }
        public static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
        private static HttpResponseMessage Fail() => new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"unavailable\"}") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/collections/server") return Task.FromResult(FailServer ? Fail() : Ok(ServerBody));
            if (path == "/api/v2/collections/capabilities") return Task.FromResult(Ok("{\"item_reorder\":true,\"imports\":true}"));
            if (path == "/api/v2/profiles")
            {
                ProfilesStarted.TrySetResult();
                if (HoldProfiles) return Profiles.Task;
                return Task.FromResult(FailProfiles ? Fail() : Ok("{\"items\":[]}"));
            }
            if (path.EndsWith("/collections") && path.StartsWith("/api/v2/library/"))
            {
                Interlocked.Increment(ref LibraryTabReads);
                if (HoldLibraryTab) return LibraryTab.Task;
                // What HttpClient throws once its own Timeout elapses: a cancellation no caller token asked for.
                if (TimeOutLibraryTab) return Task.FromException<HttpResponseMessage>(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException()));
                return Task.FromResult(FailLibraryTab ? Fail() : Ok(LibraryTabBody));
            }
            if (path == "/api/v2/collections")
            {
                PersonalStarted.TrySetResult();
                if (HoldPersonal) return Personal.Task;
                return Task.FromResult(FailPersonal ? Fail() : Ok(PersonalBody));
            }
            if (path == "/api/v2/collections/c1")
            {
                if (request.Method == HttpMethod.Delete)
                {
                    Assert.Equal("\"revision\"", request.Headers.IfMatch.Single().Tag);
                    DeleteRequests++;
                    return Task.FromResult(FailDelete ? Fail() : new HttpResponseMessage(HttpStatusCode.NoContent));
                }
                Assert.Equal(HttpMethod.Get, request.Method);
                var response = Ok(PersonalSnapshotBody);
                response.Headers.ETag = new("\"revision\"");
                return Task.FromResult(response);
            }
            return Task.FromResult(Ok("{}"));
        }
    }
}
