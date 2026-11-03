using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class BoxSetSearchStateTests
{
    [Theory]
    [InlineData(false, true, true, "Film", null)]
    [InlineData(true, false, true, "Film", null)]
    [InlineData(true, true, false, "Film", null)]
    [InlineData(true, true, true, " ", null)]
    [InlineData(true, true, true, "Film", 99)]
    public async Task UnsupportedDisabledBlankAndNonMovieScopesNeverReadAggregates(bool supported, bool enabled, bool inScope, string query, int? library)
    {
        using var wire = new Wire { Supported = supported, Enabled = enabled }; var client = wire.Client();
        var state = new BoxSetSearchState(new CatalogApi(client));
        await state.SearchAsync(query, library, inScope);
        Assert.Empty(state.Items); Assert.False(state.IsLoading); Assert.Equal(0, wire.SearchReads);
    }

    [Fact]
    public async Task SearchUsesServerOrderAndLimitWithoutRequiringAnAdminSearchFlag()
    {
        using var wire = new Wire(); var state = new BoxSetSearchState(new CatalogApi(wire.Client()));
        await state.SearchAsync("  A&B  ", 12);
        Assert.Equal("?q=A%26B&limit=5&library_id=12", wire.LastSearch);
        Assert.Equal(new[] { "7", "4" }, state.Items.Select(x => x.Id));
        Assert.True(state.CanOpen(state.Items[0], state.Generation));
    }

    [Fact]
    public async Task FreshSameQueryReusesOnlyItsOwnedCacheAndFailedRefreshKeepsIt()
    {
        using var wire = new Wire(); var state = new BoxSetSearchState(new CatalogApi(wire.Client()));
        await state.SearchAsync("Film"); var retained = state.Items[0]; var generation = state.Generation;
        await state.SearchAsync(" Film "); Assert.Equal(1, wire.SearchReads); Assert.Same(retained, state.Items[0]);
        Assert.False(state.CanOpen(retained, generation));
        wire.Status = HttpStatusCode.ServiceUnavailable;
        await state.SearchAsync("Film", refresh: true);
        Assert.Equal(2, wire.SearchReads); Assert.Same(retained, state.Items[0]); Assert.True(state.CanOpen(retained, state.Generation)); Assert.False(state.IsLoading);
        await state.SearchAsync("Other"); Assert.Empty(state.Items); Assert.False(state.CanOpen(retained, state.Generation)); Assert.False(state.IsLoading);
    }

    [Theory]
    [InlineData("/api/v2/user/libraries", false)]
    [InlineData("/api/v2/user/libraries", true)]
    [InlineData("/api/v2/catalog/search/capabilities", false)]
    [InlineData("/api/v2/catalog/search/capabilities", true)]
    public async Task SameQueryPolicyFailureRetainsValidatedRowsAndCache(string path, bool timeout)
    {
        using var wire = new Wire(); var catalog = new CatalogApi(wire.Client()); var state = new BoxSetSearchState(catalog);
        await state.SearchAsync("Film"); var retained = state.Items.ToArray(); var generation = state.Generation;
        if (path.EndsWith("/capabilities")) ExpireCapabilities(catalog);
        wire.PolicyFailurePath = path; wire.PolicyTimeout = timeout;
        var publications = 0; state.Items.CollectionChanged += (_, _) => publications++;

        await state.SearchAsync("Film", refresh: true).WaitAsync(TimeSpan.FromSeconds(9));

        Assert.Equal(retained, state.Items.ToArray()); Assert.False(state.IsLoading);
        Assert.Equal(1, wire.SearchReads); Assert.True(publications > 0);
        Assert.False(state.CanOpen(retained[0], generation));
        Assert.True(state.CanOpen(retained[0], state.Generation));
        wire.PolicyFailurePath = null;
        await state.SearchAsync("Film");
        Assert.Same(retained[0], state.Items[0]); Assert.Equal(1, wire.SearchReads);
        wire.Enabled = false;
        await state.SearchAsync("Film");
        Assert.Empty(state.Items); Assert.False(state.CanOpen(retained[0], state.Generation));
    }

    [Theory]
    [InlineData("query")] [InlineData("library")] [InlineData("profile")]
    [InlineData("access")] [InlineData("language")] [InlineData("surface")]
    public async Task FailedPolicyCannotRetainRowsForADifferentOwner(string change)
    {
        using var wire = new Wire(); var client = wire.Client(); var state = new BoxSetSearchState(new CatalogApi(client));
        var active = true;
        await state.SearchAsync("Film", isCurrent: () => active); var retained = state.Items[0];
        wire.PolicyFailurePath = "/api/v2/user/libraries";
        if (change == "profile") client.SetProfile("other");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        if (change == "surface") active = false;
        await state.SearchAsync(change == "query" ? "Other" : "Film", change == "library" ? 12 : null,
            isCurrent: () => active, refresh: true);
        Assert.Empty(state.Items); Assert.False(state.IsLoading); Assert.False(state.CanOpen(retained, state.Generation));
    }

    [Theory]
    [InlineData(200)] [InlineData(404)]
    public async Task SuccessfulUnsupportedPolicyClearsRetainedRows(int status)
    {
        using var wire = new Wire(); var catalog = new CatalogApi(wire.Client()); var state = new BoxSetSearchState(catalog);
        await state.SearchAsync("Film"); var retained = state.Items[0];
        ExpireCapabilities(catalog); wire.Supported = false;
        if (status == 404) wire.PolicyFailurePath = "/api/v2/catalog/search/capabilities";
        wire.PolicyStatus = (HttpStatusCode)status;
        await state.SearchAsync("Film", refresh: true);
        Assert.Empty(state.Items); Assert.False(state.CanOpen(retained, state.Generation)); Assert.Equal(1, wire.SearchReads);
    }

    [Fact]
    public async Task LibraryDisableHidesFreshCachedSearchWithoutUnsupportedRead()
    {
        using var wire = new Wire(); var client = wire.Client(); var state = new BoxSetSearchState(new CatalogApi(client));
        await state.SearchAsync("Film"); var old = state.Items[0]; wire.Enabled = false;
        await state.SearchAsync("Film"); Assert.Empty(state.Items); Assert.False(state.CanOpen(old, state.Generation)); Assert.Equal(1, wire.SearchReads);
    }

    [Theory] [InlineData("query")] [InlineData("library")] [InlineData("profile")] [InlineData("language")] [InlineData("departure")] [InlineData("surface")]
    public async Task DelayedSearchCannotPublishOrOpenAfterItsOwnerRetires(string change)
    {
        using var wire = new Wire { Hold = true }; var client = wire.Client(); var state = new BoxSetSearchState(new CatalogApi(client));
        var active = true;
        var pending = state.SearchAsync("Old", isCurrent: () => active);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "query") await state.SearchAsync("New", enabled: false);
        if (change == "library") await state.SearchAsync("Old", 99);
        if (change == "profile") client.SetProfile("other");
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        if (change == "departure") state.Retire();
        if (change == "surface") active = false;
        wire.Release.TrySetResult(); await pending;
        Assert.Empty(state.Items); Assert.False(state.IsLoading);
    }

    [Fact]
    public async Task CachedAggregateCallbackAlsoRetiresWhenProfileOrQueryOwnerChanges()
    {
        using var wire = new Wire(); var client = wire.Client(); var state = new BoxSetSearchState(new CatalogApi(client));
        var active = true; await state.SearchAsync("Film", isCurrent: () => active); var row = state.Items[0];
        active = false; Assert.False(state.CanOpen(row, state.Generation));
        await state.SearchAsync("Film"); client.SetProfile("other"); Assert.False(state.CanOpen(row, state.Generation));
        await state.SearchAsync("Film"); Assert.Equal(2, wire.SearchReads);
    }

    [Fact]
    public async Task HungOptionalSearchSettlesInsteadOfHoldingNoMatchesForever()
    {
        using var wire = new Wire { Hold = true }; var state = new BoxSetSearchState(new CatalogApi(wire.Client()));
        await state.SearchAsync("Film").WaitAsync(TimeSpan.FromSeconds(9));
        Assert.False(state.IsLoading); Assert.Empty(state.Items); wire.Release.TrySetResult();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SameScopePublishedCallbacksStayOpenableDuringPolicyRevalidation(bool failPolicy)
    {
        using var wire = new Wire(); var state = new BoxSetSearchState(new CatalogApi(wire.Client()));
        await state.SearchAsync("Film"); var row = state.Items[0]; var generation = state.Generation;
        wire.HoldPolicy = true;
        if (failPolicy) wire.PolicyFailurePath = "/api/v2/user/libraries";
        var refresh = state.SearchAsync("Film", refresh: true);
        await wire.PolicyStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(state.IsLoading); Assert.Same(row, state.Items[0]);
        Assert.Equal(generation, state.Generation); Assert.True(state.CanOpen(row, generation));

        wire.PolicyRelease.TrySetResult(); await refresh;
        if (failPolicy) Assert.Same(row, state.Items[0]);
        Assert.False(state.CanOpen(row, generation));
        Assert.True(state.CanOpen(state.Items[0], state.Generation));
    }

    [Fact]
    public async Task SameScopePublishedCallbacksStayOpenableDuringFailedCatalogRefresh()
    {
        using var wire = new Wire(); var state = new BoxSetSearchState(new CatalogApi(wire.Client()));
        await state.SearchAsync("Film"); var row = state.Items[0];
        wire.Hold = true; wire.Status = HttpStatusCode.ServiceUnavailable;
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = state.SearchAsync("Film", refresh: true);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var generation = state.Generation;
        Assert.True(state.IsLoading); Assert.True(state.CanOpen(row, generation));
        wire.Release.TrySetResult(); await refresh;
        Assert.Equal(generation, state.Generation); Assert.True(state.CanOpen(row, generation));
    }

    [Theory]
    [InlineData("query")] [InlineData("library")] [InlineData("profile")] [InlineData("language")]
    public async Task SuspendedRowsRetireWhenTheirScopeChanges(string change)
    {
        using var wire = new Wire(); var client = wire.Client(); var state = new BoxSetSearchState(new CatalogApi(client));
        await state.SearchAsync("Film"); var row = state.Items[0]; var generation = state.Generation;
        state.Suspend();
        Assert.Same(row, state.Items[0]); Assert.False(state.CanOpen(row, generation));
        if (change == "profile") client.SetProfile("other");
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        wire.PolicyFailurePath = "/api/v2/user/libraries";
        await state.SearchAsync(change == "query" ? "Other" : "Film", change == "library" ? 12 : null);
        Assert.Empty(state.Items); Assert.False(state.CanOpen(row, generation));
    }

    private static void ExpireCapabilities(CatalogApi catalog)
    {
        var field = typeof(CatalogApi).GetField("_searchRuleCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var cached = field.GetValue(catalog)!; var type = cached.GetType();
        field.SetValue(catalog, Activator.CreateInstance(type, new object?[]
        {
            type.GetProperty("Context")!.GetValue(cached), DateTime.UtcNow.AddMinutes(-6), type.GetProperty("Value")!.GetValue(cached)
        }));
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool Supported = true, Enabled = true, Hold; internal int SearchReads; internal string? LastSearch;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string? PolicyFailurePath;
        internal bool PolicyTimeout, HoldPolicy;
        internal TaskCompletionSource PolicyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), PolicyRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HttpStatusCode PolicyStatus = HttpStatusCode.ServiceUnavailable;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://boxset-state.invalid"); client.SetProfile("alpha"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var body = "{}"; var status = HttpStatusCode.OK;
            if (path == "/api/v2/user/libraries" && HoldPolicy)
            {
                PolicyStarted.TrySetResult(); await PolicyRelease.Task.WaitAsync(ct);
            }
            if (path == PolicyFailurePath)
            {
                if (PolicyTimeout) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new HttpResponseMessage(PolicyStatus) { Content = new StringContent(body) };
            }
            if (path.EndsWith("/capabilities")) body = Supported ? "{\"box_sets\":true}" : "{}";
            if (path == "/api/v2/user/libraries") body = "{\"items\":[{\"id\":12,\"box_sets_enabled\":" + Enabled.ToString().ToLowerInvariant() + "}]}";
            if (path == "/api/v2/catalog/box-sets")
            {
                LastSearch = request.RequestUri.Query; SearchReads++; Started.TrySetResult(); if (Hold) await Release.Task;
                status = Status; body = "{\"items\":[{\"id\":\"7\",\"library_id\":\"12\",\"title\":\"Server best\",\"movie_count\":3},{\"id\":\"4\",\"library_id\":\"12\",\"title\":\"Server next\",\"movie_count\":2}]}";
            }
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
