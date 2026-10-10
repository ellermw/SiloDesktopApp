using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class CollectionHomeRowUsageBehaviorTests
{
    [Fact]
    public async Task LookupReadsOnlyVisiblePagesAndIncludesHiddenRowsWithoutMutations()
    {
        using var handler = new Wire(); var client = Client(handler);
        var usage = await new CollectionHomeRowUsageService(new(client), new(client)).GetAsync("mine");
        Assert.Equal(2, usage.Count);
        Assert.Contains(usage, row => row.Scope == "home" && row.SectionId == "home-row" && row.RowTitle == "Renamed" && !row.Hidden);
        Assert.Contains(usage, row => row.LibraryId == 7 && row.PageName == "Movies" && row.Hidden);
        Assert.DoesNotContain(handler.Reads, uri => uri.Query.Contains("include_hidden"));
        Assert.Equal(3, handler.Reads.Count);
        Assert.Equal(0, handler.Mutations);
    }

    [Fact]
    public async Task ProfileSwitchDuringReadRejectsAllOldProfileUsage()
    {
        using var handler = new Wire { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var client = Client(handler);
        var lookup = new CollectionHomeRowUsageService(new(client), new(client)).GetAsync("mine");
        await handler.Started.Task;
        client.SetProfile("other"); handler.Gate.SetResult(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        Assert.Equal(0, handler.Mutations);
    }

    [Fact]
    public async Task CancellationDoesNotReturnAPartialPageList()
    {
        using var handler = new Wire { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var client = Client(handler);
        using var cancellation = new CancellationTokenSource();
        var lookup = new CollectionHomeRowUsageService(new(client), new(client)).GetAsync("mine", cancellation.Token);
        await handler.Started.Task; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup);
        Assert.Equal(0, handler.Mutations);
    }

    private static SiloApiClient Client(Wire wire)
    { var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://usage-fixture.invalid"); client.SetAccessToken("fixture-only"); client.SetProfile("own"); return client; }
    private sealed class Wire : HttpMessageHandler
    {
        public List<Uri> Reads { get; } = [];
        public int Mutations;
        public TaskCompletionSource<bool>? Gate;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) { Mutations++; throw new InvalidOperationException("Usage lookup must be read-only."); }
            lock (Reads) Reads.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/api/v2/user/libraries")
                return Reply(new { items = new[] { new { id = 7, name = "Movies", type = "movie" } } });
            Started.TrySetResult(true); if (Gate != null) await Gate.Task.WaitAsync(ct);
            var home = request.RequestUri.Query.Contains("scope=home");
            return Reply(new { items = new[]
            {
                new { id = home ? "home-row" : "library-row", title = "Renamed", section_type = "collection", hidden = !home, config = new { user_collection_id = "mine" } },
                new { id = "other", title = "Other collection", section_type = "collection", hidden = false, config = new { user_collection_id = "someone-else" } }
            } });
        }
        private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    }
}
