using System.Net;
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

    private sealed class Wire : HttpMessageHandler
    {
        internal bool Supported = true, Enabled = true, Hold; internal int SearchReads; internal string? LastSearch;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://boxset-state.invalid"); client.SetProfile("alpha"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var body = "{}"; var status = HttpStatusCode.OK;
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
