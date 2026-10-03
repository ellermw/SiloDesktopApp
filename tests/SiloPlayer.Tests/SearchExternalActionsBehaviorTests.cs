using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SearchExternalActionsBehaviorTests
{
    [Fact]
    public async Task SeriesRequestFromSearchSubmitsWholeSeriesAndUpdatesCardAfterSuccess()
    {
        using var wire = new Wire(); var vm = wire.Model(); await vm.SetOutsidePageAsync(1);
        var title = Assert.Single(vm.OutsideLibraryResults);
        await vm.RequestOutsideTitleAsync(title);
        Assert.Equal(1, wire.RequestWrites);
        using var body = JsonDocument.Parse(wire.RequestBody!);
        Assert.Equal("series", body.RootElement.GetProperty("media_type").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("tmdb_id").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("season_numbers", out _));
        Assert.False(title.Request.Requestable);
        Assert.Equal("fixture-created", title.Request.RequestId);
    }

    [Fact]
    public async Task WatchlistCapabilityGatesActionAndSavedTitleIsDeletedInsteadOfAdded()
    {
        using var wire = new Wire(); var vm = wire.Model();
        Assert.False(vm.OutsideWatchlistTitlesSupported);
        await vm.SetOutsidePageAsync(1);
        Assert.True(vm.OutsideWatchlistTitlesSupported);
        var title = Assert.Single(vm.OutsideLibraryResults);
        await vm.ToggleOutsideWatchlistAsync(title);
        Assert.Equal("DELETE /api/v2/watchlist/titles/series/7", Assert.Single(wire.BookmarkWrites));
        Assert.False(title.InWatchlist);
        wire.WatchlistSupported = false; await vm.SetOutsidePageAsync(1);
        Assert.False(vm.OutsideWatchlistTitlesSupported);
        await vm.ToggleOutsideWatchlistAsync(Assert.Single(vm.OutsideLibraryResults));
        Assert.Single(wire.BookmarkWrites);
    }

    [Fact]
    public async Task RejectedRequestPreservesRequestableStateAndAllowsRetry()
    {
        using var wire = new Wire { RejectRequest = true }; var vm = wire.Model(); await vm.SetOutsidePageAsync(1);
        var title = Assert.Single(vm.OutsideLibraryResults);
        await Assert.ThrowsAsync<ApiException>(() => vm.RequestOutsideTitleAsync(title));
        Assert.True(title.Request.Requestable);
        Assert.True(string.IsNullOrEmpty(title.Request.RequestId));
        wire.RejectRequest = false; await vm.RequestOutsideTitleAsync(title);
        Assert.Equal(2, wire.RequestWrites);
        Assert.False(title.Request.Requestable);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool WatchlistSupported = true;
        public bool RejectRequest;
        public int RequestWrites;
        public string? RequestBody;
        public List<string> BookmarkWrites { get; } = [];
        public SearchViewModel Model()
        {
            var api = new SiloApiClient(new HttpClient(this)); api.SetBaseUrl("https://search-actions.invalid");
            return new(new CatalogApi(api), new PeopleApi(api), new RequestsApi(api), new SettingsApi(api)) { Query = "Fixture" };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var status = HttpStatusCode.OK; var body = "{}";
            if (path == "/api/v2/requests/status") body = $"{{\"requests_enabled\":true,\"watchlist_titles_supported\":{WatchlistSupported.ToString().ToLowerInvariant()}}}";
            else if (path.StartsWith("/api/v2/requests/search")) body = "{\"page\":1,\"total_pages\":1,\"results\":[{\"tmdb_id\":7,\"media_type\":\"series\",\"title\":\"Fixture series\",\"in_watchlist\":true,\"request\":{\"requestable\":true}}]}";
            else if (path == "/api/v2/requests" && request.Method == HttpMethod.Post)
            {
                RequestWrites++; RequestBody = await request.Content!.ReadAsStringAsync(ct);
                status = RejectRequest ? HttpStatusCode.Conflict : HttpStatusCode.Created;
                body = RejectRequest ? "{\"message\":\"Fixture rejected\"}" : "{\"id\":\"fixture-created\",\"status\":\"pending\"}";
            }
            else if (path.StartsWith("/api/v2/watchlist/titles/")) BookmarkWrites.Add($"{request.Method} {path}");
            return new(status) { Content = new StringContent(body) };
        }
    }
}
