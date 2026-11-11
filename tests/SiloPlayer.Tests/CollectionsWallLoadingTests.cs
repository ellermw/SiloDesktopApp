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
    private const string OneLibrary = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"Movies\",\"total_count\":1,\"collections\":[{\"id\":\"s1\",\"title\":\"Server one\"}]}]}";

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
        public bool FailPersonal, FailProfiles, FailServer, FailLibraryTab, HoldPersonal, HoldProfiles;
        public int LibraryTabReads;
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
                return Task.FromResult(FailLibraryTab ? Fail() : Ok("{\"library_id\":1,\"collections\":[],\"groups\":[]}"));
            }
            if (path == "/api/v2/collections")
            {
                PersonalStarted.TrySetResult();
                if (HoldPersonal) return Personal.Task;
                return Task.FromResult(FailPersonal ? Fail() : Ok(PersonalBody));
            }
            return Task.FromResult(Ok("{}"));
        }
    }
}
