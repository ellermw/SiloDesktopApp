using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class ProfileRequestsPermissionTests
{
    [Fact]
    public void OldProfilesDefaultToAllowedAndCapabilityDefaultsToUnsupported()
    {
        var profile = JsonSerializer.Deserialize<Profile>("{}", V2Json.Options)!;
        Assert.True(profile.RequestsAllowed);
        Assert.False(new ProfilesResponse().RequestsAllowedSupported);
        profile = JsonSerializer.Deserialize<Profile>("{\"requests_allowed\":false}", V2Json.Options)!;
        Assert.False(profile.RequestsAllowed);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task ProfileWritesIncludePermissionOnlyWithAdvertisedSupport(bool supported, bool update)
    {
        using var wire = new Wire(); var client = Client(wire);
        var draft = new CreateProfileRequest { Name = "Child" };
        draft.RequestsAllowedSupported = supported; draft.RequestsAllowed = false;
        var api = new AuthApi(client);
        if (update) await api.UpdateProfileAsync("child", draft); else await api.CreateProfileAsync(draft);
        Assert.Equal(supported, wire.Body.TryGetProperty("requests_allowed", out var allowed));
        if (supported) Assert.False(allowed.GetBoolean());
    }

    [Fact]
    public async Task BlockedHubLoadsOwnHistoryAndRemovalButNeverDiscoveryOrSearchIncludingRetries()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        var profile = new Profile { Id = "child" }; profile.RequestsAllowed = false;
        auth.SelectProfile(profile.Id, profile: profile);
        var api = new RequestsApi(client, auth);
        var vm = new RequestsViewModel(api);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.MyRequests); Assert.True(vm.CanRemoveRequest(vm.MyRequests[0]));
        Assert.Empty(vm.DiscoveryRows); Assert.Empty(vm.DiscoverySections);
        await vm.RetryDiscoveryAsync(); await vm.RetryBrandAsync("studio");
        vm.SearchQuery = "retained"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.DoesNotContain(wire.Paths, p => p.Contains("discover") || p.Contains("search"));
        await vm.RemoveFailedRequestAsync(vm.MyRequests[0], vm.CaptureRemovalOwner());
        Assert.Contains("DELETE /api/v2/requests/failed", wire.Paths);
        Assert.DoesNotContain(wire.Paths, p => p.Contains("discover") || p.Contains("search"));
    }

    [Fact]
    public async Task RetainedApiRejectsDiscoveryAndNewMutationsAfterProfileBlockButKeepsOwnReadAndCancel()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child" });
        var api = new RequestsApi(client, auth);
        var blocked = new Profile { Id = "child" }; blocked.RequestsAllowed = false;
        auth.RefreshSelectedProfile(blocked);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SearchAsync("movie", "retained"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetDetailAsync("movie", 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.CreateAsync(new()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.FollowAsync("movie", 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.AddWatchlistTitleAsync("movie", 1));
        Assert.Empty(wire.Paths);
        await api.GetAsync("own"); await api.CancelAsync("own");
        Assert.Equal(2, wire.Paths.Count);
    }

    [Fact]
    public async Task AccountSubmissionDenialStillAllowsDiscoveryForAnUnblockedProfile()
    {
        using var wire = new Wire(); var client = Client(wire);
        var api = new RequestsApi(client);
        await api.GetStatusAsync();
        await api.SearchAsync("movie", "allowed discovery");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.CreateAsync(new()));
        Assert.Contains(wire.Paths, path => path.Contains("/search?"));
        Assert.DoesNotContain("POST /api/v2/requests", wire.Paths);
    }

    [Fact]
    public async Task BlockRetiresPendingDiscoveryAndCachedHubWithoutRetiringOwnMutationIdentity()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child" });
        var api = new RequestsApi(client, auth);
        var vm = new RequestsViewModel(api); await vm.LoadCommand.ExecuteAsync(null);
        var identity = api.CaptureIdentityContext(); var context = api.CaptureContext();
        wire.PendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = api.SearchAsync("movie", "held");
        var blocked = new Profile { Id = "child" }; blocked.RequestsAllowed = false;
        auth.RefreshSelectedProfile(blocked);
        Assert.False(api.IsCurrentContext(context)); Assert.True(api.IsCurrentIdentityContext(identity));
        wire.PendingSearch.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[{\"tmdb_id\":12}]}") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        wire.Paths.Clear(); await vm.RefreshAsync();
        Assert.Single(vm.MyRequests); Assert.Empty(vm.DiscoveryRows);
        Assert.DoesNotContain(wire.Paths, path => path.Contains("discover"));
        // A metadata-only refresh does not needlessly retire authority again.
        context = api.CaptureContext(); auth.RefreshSelectedProfile(blocked);
        Assert.True(api.IsCurrentContext(context));
    }

    [Fact]
    public async Task BlockedOptionalSearchAndWatchlistLoadNoExternalTitles()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = false });
        var api = new RequestsApi(client, auth);
        var search = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), api, new SettingsApi(client)) { Query = "movie" };
        await search.SetOutsidePageAsync(1);
        Assert.Empty(search.OutsideLibraryResults); Assert.Null(search.OutsideError); Assert.False(search.OutsideWatchlistTitlesSupported);
        var watchlist = new WatchlistViewModel(new CatalogApi(client), api);
        await watchlist.LoadExternalTitlesAsync();
        Assert.Empty(watchlist.ExternalTitles); Assert.False(watchlist.ExternalTitlesSupported);
        Assert.All(wire.Paths, path => Assert.Equal("GET /api/v2/requests/status", path));
    }

    [Fact]
    public async Task ConfirmedCancelSurvivesPermissionBlockButCannotBeReauthoredAfterProfileSwitch()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child" });
        var api = new RequestsApi(client, auth); var vm = new RequestsViewModel(api);
        var owner = api.CaptureIdentityContext();
        auth.RefreshSelectedProfile(new Profile { Id = "child", RequestsAllowed = false });
        var row = new SiloPlayer.Core.Models.Requests.MediaRequest { Id = "own", Status = "pending", Outcome = "active" };
        await vm.CancelOwnRequestAsync(row, owner);
        Assert.Contains("POST /api/v2/requests/own/cancel", wire.Paths);
        Assert.DoesNotContain(wire.Paths, path => path.Contains("discover"));
        wire.Paths.Clear(); auth.SelectProfile("replacement", profile: new Profile { Id = "replacement" });
        await vm.CancelOwnRequestAsync(row, owner);
        Assert.Empty(wire.Paths);
    }

    [Fact]
    public async Task LateCancelRefusalDoesNotWriteFeedbackIntoReplacementProfile()
    {
        using var wire = new Wire(); var client = Client(wire); client.SetProfile("child");
        var api = new RequestsApi(client); var vm = new RequestsViewModel(api);
        wire.PendingCancel = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelling = vm.CancelOwnRequestAsync(new() { Id = "own", Status = "pending", Outcome = "active" }, api.CaptureIdentityContext());
        client.SetProfile("replacement");
        wire.PendingCancel.SetResult(new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"type\":\"https://silo.invalid/docs/api/v2/problems/forbidden\",\"detail\":\"Old request was refused\"}") });
        await cancelling;
        Assert.Null(vm.ErrorMessage); Assert.Empty(vm.StatusMessage);
    }

    [Fact]
    public async Task ReviewCachedAccountDenialSurvivesTransportCatchUp()
    {
        using var wire = new Wire(); var client = Client(wire); var api = new RequestsApi(client);
        await api.GetStatusAsync(); Assert.False(api.SubmissionAllowed);
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        Assert.True(api.DiscoveryAllowed); Assert.False(api.SubmissionAllowed);
    }

    [Fact]
    public async Task ReviewDiscoveryRowCapabilitySurvivesTransportCatchUp()
    {
        using var wire = new Wire { DiscoverRows = true }; var client = Client(wire); var api = new RequestsApi(client);
        await api.GetStatusAsync();
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        await api.GetDiscoverySectionAsync("latest");
        Assert.Contains(wire.Paths, path => path.Contains("/discover/rows/latest", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReviewUnfollowRemainsAvailableAfterPermissionOrFeatureLoss(bool blocked, bool disabled)
    {
        using var wire = new Wire { RequestsEnabled = !disabled }; var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = !blocked });
        var api = new RequestsApi(client, auth); await api.GetStatusAsync();
        await api.UnfollowAsync("movie", 42);
        Assert.Contains("DELETE /api/v2/requests/follows/movie/42", wire.Paths);
    }

    [Fact]
    public void ReviewDiscoveryOwnerSurvivesCatchUpButRetiresOnAccessAndProfileChanges()
    {
        using var wire = new Wire(); var client = Client(wire); client.SetProfile("first"); var api = new RequestsApi(client);
        var owner = api.CaptureDiscoveryOwner();
        Assert.True(api.IsCurrentDiscoveryOwner(owner));
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        Assert.True(api.IsCurrentDiscoveryOwner(owner));
        client.InvalidateAccessContext(); Assert.False(api.IsCurrentDiscoveryOwner(owner));
        owner = api.CaptureDiscoveryOwner(); client.SetProfile("second"); Assert.False(api.IsCurrentDiscoveryOwner(owner));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task ReviewBlockedNavigationChecksOnlyOneOwnRequest(bool blocked, bool hasHistory, bool expected)
    {
        using var wire = new Wire { EmptyMine = !hasHistory }; var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = !blocked });
        var api = new RequestsApi(client, auth); var status = await api.GetStatusAsync();
        Assert.Equal(expected, await api.ShouldShowNavigationAsync(status));
        var reads = wire.Paths.Where(path => path.Contains("/requests/mine", StringComparison.Ordinal)).ToArray();
        if (blocked) { Assert.Single(reads); Assert.Contains("limit=1", reads[0]); }
        else Assert.Empty(reads);
    }

    [Fact]
    public async Task DisabledNavigationNeverProbesHistory()
    {
        using var wire = new Wire { RequestsEnabled = false }; var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child", RequestsAllowed = false });
        var api = new RequestsApi(client, auth);
        Assert.False(await api.ShouldShowNavigationAsync(await api.GetStatusAsync()));
        Assert.Single(wire.Paths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CachedFeatureDenialSurvivesCatchUpWithoutAnotherStatusRead(bool enabled)
    {
        using var wire = new Wire { RequestsEnabled = enabled, RejectFollow = true };
        var client = Client(wire); var api = new RequestsApi(client);
        await api.GetStatusAsync();
        var owner = api.CaptureDiscoveryOwner();
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        Assert.Equal(enabled, api.DiscoveryAllowed);
        Assert.False(api.SubmissionAllowed);
        Assert.Equal(enabled, api.IsCurrentDiscoveryOwner(owner));
        if (enabled)
        {
            await Assert.ThrowsAsync<ApiException>(() => api.FollowAsync("movie", 42));
            Assert.Contains("PUT /api/v2/requests/follows/movie/42", wire.Paths);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.FollowAsync("movie", 42));
            Assert.Single(wire.Paths);
        }
    }

    [Fact]
    public async Task UnfollowCannotBeReauthoredByAReplacementProfile()
    {
        using var wire = new Wire(); var client = Client(wire); client.SetProfile("first");
        var api = new RequestsApi(client); var owner = api.CaptureIdentityContext();
        client.InvalidateAccessContext();
        await api.UnfollowAsync("movie", 42, owner);
        wire.Paths.Clear(); client.SetProfile("second");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.UnfollowAsync("movie", 42, owner));
        Assert.Empty(wire.Paths);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void FollowToggleRemainsVisibleUnderAccountDenial(bool following, bool allowed, bool visible)
    {
        var features = new RequestFeatureStatus { FollowSupported = true, Allowed = allowed };
        var request = new RequestState { Reason = "already_requested", Status = "pending", RequestedByViewer = false, Following = following };
        Assert.Equal(visible, RequestViewerPolicy.ShowFollowAction(features, request, allowed));
    }

    [Fact]
    public async Task DiscoveryReadKeepsItsInitiatingOwnerAcrossCatchUpButNotTrueAccessLoss()
    {
        using var wire = new Wire(); var client = Client(wire); var api = new RequestsApi(client);
        foreach (var catchUp in new[] { true, false })
        {
            wire.PendingSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = api.SearchAsync("movie", "held");
            Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: catchUp));
            wire.PendingSearch.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") });
            if (catchUp) await reading;
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        }
    }

    [Fact]
    public async Task RetainedSearchReusesCatchUpOwnerButRefreshesAfterRealRetirement()
    {
        using var wire = new Wire(); var client = Client(wire); var api = new RequestsApi(client);
        var search = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), api, new SettingsApi(client)) { Query = "retained" };
        await search.SetOutsidePageAsync(1);
        var owner = search.OutsideDiscoveryOwner;
        Assert.NotNull(owner);
        var reads = wire.Paths.Count;
        Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
        await search.RefreshRetainedOutsideAsync();
        Assert.Equal(owner, search.OutsideDiscoveryOwner); Assert.Equal(reads, wire.Paths.Count);
        client.InvalidateAccessContext();
        await search.RefreshRetainedOutsideAsync();
        Assert.NotEqual(owner, search.OutsideDiscoveryOwner);
        Assert.True(api.IsCurrentDiscoveryOwner(search.OutsideDiscoveryOwner!.Value));
        Assert.Equal(reads + 2, wire.Paths.Count); Assert.Equal("retained", search.Query); Assert.Equal(1, search.OutsideRequestedPage);
    }

    [Fact]
    public async Task RetainedSearchClearsBlockedExternalAuthorityWithoutReadingAgain()
    {
        using var wire = new Wire(); var client = Client(wire);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SelectProfile("child", profile: new Profile { Id = "child" });
        var api = new RequestsApi(client, auth);
        var search = new SearchViewModel(new CatalogApi(client), new PeopleApi(client), api, new SettingsApi(client)) { Query = "retained" };
        await search.SetOutsidePageAsync(1); Assert.NotNull(search.OutsideDiscoveryOwner);
        var reads = wire.Paths.Count;
        auth.RefreshSelectedProfile(new Profile { Id = "child", RequestsAllowed = false });
        await search.RefreshRetainedOutsideAsync();
        Assert.Null(search.OutsideDiscoveryOwner); Assert.Empty(search.OutsideLibraryResults);
        Assert.Equal(reads, wire.Paths.Count); Assert.Equal("retained", search.Query);
    }

    [Fact]
    public async Task OnlyCurrentStatusPublishesTheDiscoveryAccessChange()
    {
        using var wire = new Wire(); var client = Client(wire); var api = new RequestsApi(client);
        var changes = 0; api.StatusChanged += () => changes++;
        await api.GetStatusAsync(); Assert.Equal(1, changes);
        wire.RequestsEnabled = false;
        client.InvalidateAccessContext();
        await api.GetStatusAsync(); Assert.False(api.DiscoveryAllowed); Assert.Equal(2, changes);
        wire.PendingStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        client.InvalidateAccessContext();
        var pending = api.GetStatusAsync(); client.SetProfile("replacement");
        wire.PendingStatus.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{\"requests_enabled\":true}") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(2, changes);
    }

    private static SiloApiClient Client(Wire wire)
    {
        var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://profile-requests.fixture.invalid"); return client;
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal List<string> Paths = []; internal JsonElement Body;
        internal bool RequestsEnabled = true, DiscoverRows, EmptyMine, RejectFollow;
        internal TaskCompletionSource<HttpResponseMessage>? PendingStatus;
        internal TaskCompletionSource<HttpResponseMessage>? PendingSearch;
        internal TaskCompletionSource<HttpResponseMessage>? PendingCancel;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.RequestUri.AbsolutePath.EndsWith("/status") && PendingStatus != null) return await PendingStatus.Task;
            if (request.RequestUri.AbsolutePath.Contains("/requests/follows/") && request.Method == HttpMethod.Put && RejectFollow)
                return new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"code\":\"forbidden\",\"message\":\"Requests are blocked.\"}") };
            if (request.RequestUri.AbsolutePath.EndsWith("/search") && PendingSearch != null) return await PendingSearch.Task;
            if (request.RequestUri.AbsolutePath.EndsWith("/cancel") && PendingCancel != null) return await PendingCancel.Task;
            if (request.Content != null) Body = JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(ct));
            var json = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/requests/status" => JsonSerializer.Serialize(new{requests_enabled=RequestsEnabled,allowed=false,remove_failed_supported=true,discover_rows_supported=DiscoverRows}),
                "/api/v2/requests/mine" => EmptyMine ? "{\"items\":[]}" : "{\"items\":[{\"id\":\"failed\",\"outcome\":\"failed\",\"status\":\"approved\"}]}",
                _ => "{}"
            };
            return new(request.Method == HttpMethod.Delete ? HttpStatusCode.NoContent : HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
