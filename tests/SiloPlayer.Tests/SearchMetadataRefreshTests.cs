using System.Net;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SearchMetadataRefreshTests
{
    [Fact]
    public async Task CompletionReplacesMetadataAndMembershipAcrossTheRetainedWindowAndKeepsPaging()
    {
        using var wire = new Wire();
        var model = wire.Model();
        await model.SearchCommand.ExecuteAsync(null);
        await model.LoadMoreAsync();
        Assert.Equal(120, model.Results.Count);
        wire.Rows.RemoveAt(0);
        wire.Rows[0] = new() { ContentId = "replacement", Title = "Refreshed title", PosterUrl = "/fresh.jpg" };
        await model.RefreshCatalogMetadataAsync();
        Assert.Equal(120, model.Results.Count);
        Assert.DoesNotContain(model.Results, item => item.ContentId == "item-0" || item.ContentId == "item-1");
        Assert.Equal("replacement", model.Results[0].ContentId);
        Assert.Equal("Refreshed title", model.Results[0].Title);
        Assert.Equal("/fresh.jpg", model.Results[0].PosterUrl);
        Assert.Equal("item-120", model.Results[^1].ContentId);
        Assert.All(wire.Reads, read => Assert.Equal(60, read.Limit));
        var reads = wire.Reads.Count;
        await model.LoadMoreAsync();
        Assert.Equal(180, model.Results.Count);
        Assert.Equal("item-180", model.Results[^1].ContentId);
        Assert.Equal(180, model.Results.Select(item => item.ContentId).Distinct().Count());
        await model.SearchCommand.ExecuteAsync(null);
        Assert.Equal(reads + 1, wire.Reads.Count); // reuse the refreshed window, not its old cursor
    }

    [Fact]
    public async Task FailedCompletionReadInvalidatesSameQueryReuse()
    {
        using var wire = new Wire();
        var model = wire.Model();
        await model.SearchCommand.ExecuteAsync(null);
        wire.FailNext = true;
        await model.RefreshCatalogMetadataAsync();
        Assert.Equal("Old 0", model.Results[0].Title);
        Assert.NotNull(model.ErrorMessage);
        wire.Rows[0].Title = "Fresh";
        await model.SearchCommand.ExecuteAsync(null);
        Assert.Equal("Fresh", model.Results[0].Title);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task LaterWindowCannotCommitAProgressSnapshotOlderThanTheAppLifetimeSave()
    {
        using var wire = new Wire();
        var cache = new ItemDetailPrefetchCache((string _, CancellationToken _) =>
            Task.FromResult(new SiloPlayer.Core.Models.Catalog.MediaItemDetail()));
        var messenger = new WeakReferenceMessenger();
        DetailCacheInvalidation.Attach(cache, messenger);
        var model = wire.Model(cache);
        await model.SearchCommand.ExecuteAsync(null);
        await model.LoadMoreAsync();
        wire.HoldOffset = 60;
        var refresh = model.RefreshCatalogMetadataAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Only the app-lifetime recipient observes the final save, not SearchPage.
        messenger.Send(new PlaybackProgressUpdated("item-0", 80, 100, false) { Persisted = persisted.Task });
        wire.Rows[0].PositionSeconds = 80;
        wire.Rows[0].Title = "Fresh";
        wire.Release.SetResult();
        Assert.False(refresh.IsCompleted);
        persisted.SetResult();
        await refresh;
        Assert.Equal(80, model.Results[0].PositionSeconds);
        Assert.Equal("Fresh", model.Results[0].Title);
        Assert.Equal(120, model.Results.Count);
        Assert.Equal(2, wire.Reads.Skip(2).Count(read => read.Offset == 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredVisitOrQueryCannotPublishARetainedResponse(bool newQuery)
    {
        using var wire = new Wire();
        var model = wire.Model();
        await model.SearchCommand.ExecuteAsync(null);
        wire.Rows[0].Title = "Must not publish";
        wire.HoldOffset = 0;
        var refresh = model.RefreshCatalogMetadataAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (newQuery) model.Query = "new query";
        else model.CancelPendingSearch();
        wire.Release.SetResult();
        await refresh;
        Assert.Equal("Old 0", model.Results[0].Title);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionRestartsUnfinishedPeopleAndStartsDiscoveryEvenDuringTheInitialCatalogRead(bool initialCatalogPending)
    {
        using var wire = new Wire { DelayPeople = true };
        if (initialCatalogPending) wire.HoldOffset = 0;
        var model = wire.Model();
        model.MediaScope = "video"; model.MediaType = "movie";
        var search = model.SearchCommand.ExecuteAsync(null);
        if (initialCatalogPending) await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        else await search;
        await wire.PeopleStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        wire.Rows[0].Title = "Fresh";

        Assert.True(await model.RefreshCatalogMetadataAsync());
        await Until(() => wire.PeopleReads == 2);
        wire.ReleasePeople.SetResult();
        wire.Release.TrySetResult();
        await search;
        await Until(() => model.PeopleResults.Count == 1 && model.OutsideLibraryResults.Count == 1);
        Assert.Equal("person:one", model.PeopleResults[0].Id);
        Assert.Equal("Fresh", model.Results[0].Title);
        Assert.Null(model.PeopleError);
        var catalogReads = wire.Reads.Count;
        await model.SearchCommand.ExecuteAsync(null);
        Assert.Equal(catalogReads, wire.Reads.Count);
    }

    [Fact]
    public async Task CanceledMetadataReadCanResumeTheRetainedQueryOnBack()
    {
        using var wire = new Wire();
        var model = wire.Model();
        await model.SearchCommand.ExecuteAsync(null);
        wire.Rows[0].Title = "Fresh";
        wire.HoldOffset = 0;
        var refresh = model.RefreshCatalogMetadataAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        model.CancelPendingSearch();
        wire.Release.SetResult();
        Assert.False(await refresh);
        Assert.Equal("Old 0", model.Results[0].Title);
        model.BeginSearchVisit();
        Assert.True(await model.RefreshCatalogMetadataAsync());
        Assert.Equal("Fresh", model.Results[0].Title);
        await model.LoadMoreAsync();
        Assert.Equal(120, model.Results.Count);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public List<MediaItem> Rows { get; } = Enumerable.Range(0, 181)
            .Select(index => new MediaItem { ContentId = "item-" + index, Title = "Old " + index }).ToList();
        public List<(int Offset, int Limit)> Reads { get; } = [];
        public int? HoldOffset;
        public bool FailNext;
        public bool DelayPeople;
        public int PeopleReads;
        public TaskCompletionSource PeopleStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePeople { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, List<MediaItem>> _snapshots = [];

        public SearchViewModel Model(ItemDetailPrefetchCache? cache = null)
        {
            var client = new SiloApiClient(new HttpClient(this));
            client.SetBaseUrl("https://search-metadata.invalid");
            return new(new CatalogApi(client), new PeopleApi(client), new RequestsApi(client), new SettingsApi(client), cache)
            { Query = "fixture", MediaScope = "audiobook", MediaType = "audiobook" };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog/search/capabilities") return Reply("{\"people_media_scope\":true}");
            if (path.StartsWith("/api/v2/catalog/people") && DelayPeople)
            {
                Interlocked.Increment(ref PeopleReads);
                PeopleStarted.TrySetResult();
                await ReleasePeople.Task; // Ignore cancellation; the retired owner must not publish.
                return Reply("{\"items\":[{\"id\":\"person:one\",\"name\":\"Fixture Person\"}]}");
            }
            if (path == "/api/v2/requests/status") return Reply("{\"requests_enabled\":true}");
            if (path == "/api/v2/requests/search") return Reply("{\"page\":1,\"total_pages\":1,\"results\":[{\"media_type\":\"movie\",\"tmdb_id\":42,\"availability\":\"missing\"}]}");
            if (path != "/api/v2/catalog") return Reply("{\"items\":[]}");
            if (FailNext)
            {
                FailNext = false;
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"fixture\"}") };
            }
            var query = request.RequestUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]),
                    part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : "");
            var offset = int.Parse(query["seek"]);
            var limit = int.Parse(query["limit"]);
            Reads.Add((offset, limit));
            if (!query.TryGetValue("cursor", out var snapshot))
            {
                snapshot = "snapshot-" + _snapshots.Count;
                _snapshots[snapshot] = Rows.Select(row => new MediaItem
                {
                    ContentId = row.ContentId, Title = row.Title, PosterUrl = row.PosterUrl,
                    PositionSeconds = row.PositionSeconds
                }).ToList();
            }
            var rows = _snapshots[snapshot];
            var json = JsonSerializer.Serialize(new
            {
                items = rows.Skip(offset).Take(limit).Select(row => new
                {
                    content_id = row.ContentId, title = row.Title, poster_url = row.PosterUrl,
                    position_seconds = row.PositionSeconds
                }),
                total = rows.Count, window_cursor = snapshot,
                page = new { has_more = offset + limit < rows.Count }
            });
            if (HoldOffset == offset)
            {
                HoldOffset = null;
                Started.SetResult();
                // Deliberately ignore cancellation to exercise the consumer's ownership checks.
                await Release.Task;
            }
            return Reply(json);
        }

        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
