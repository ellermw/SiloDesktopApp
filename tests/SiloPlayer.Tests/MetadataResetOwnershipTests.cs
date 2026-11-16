using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class MetadataResetOwnershipTests
{
    [Fact]
    public async Task ResetOwnsQueuedJobAndRefetchAfterDialogCloses()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var api = new MediaMaintenanceApi(client); var catalog = new CatalogApi(client);
        using var dialog = new CancellationTokenSource();
        var identity = client.CaptureIdentityContext();
        var running = api.PendingWrites.RunAsync(identity, "item",
            ct => api.ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], ct),
            ct => catalog.GetItemDetailAsync("item", ct), dialog.Token);
        await wire.JobRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        dialog.Cancel();
        var reopened = api.PendingWrites.WaitForIdleAsync(identity, "item", default);
        Assert.True(api.PendingWrites.IsPending(identity, "item"));
        Assert.False(wire.JobToken.IsCancellationRequested);
        wire.Job.SetResult("succeeded");
        await wire.DetailRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reopened.IsCompleted);
        Assert.True(api.PendingWrites.IsPending(identity, "item"));
        wire.Detail.SetResult(true);
        await running;
        Assert.Equal("Provider title", (await reopened)!.Title);
        Assert.False(api.PendingWrites.IsPending(identity, "item"));
        Assert.Equal(1, wire.Posts);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("canceled")]
    public async Task FailedJobIsNotReplayedAndStillRefetches(string state)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var api = new MediaMaintenanceApi(client);
        wire.Job.SetResult(state); wire.Detail.SetResult(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.PendingWrites.RunAsync(client.CaptureIdentityContext(), "item",
            ct => api.ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], ct),
            ct => new CatalogApi(client).GetItemDetailAsync("item", ct), default));
        Assert.True(wire.DetailRequested.Task.IsCompleted);
        Assert.Equal(1, wire.Posts);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public TaskCompletionSource<bool> JobRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DetailRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Job { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Detail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken JobToken;
        public int Posts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/refresh-metadata"))
            {
                Posts++;
                return Json("{\"id\":\"job\",\"state\":\"queued\"}");
            }
            if (path == "/api/v2/admin/jobs/job")
            {
                JobToken = ct; JobRequested.TrySetResult(true);
                return Json("{\"id\":\"job\",\"state\":\"" + await Job.Task.WaitAsync(ct) + "\"}");
            }
            if (path == "/api/v2/catalog/items/item")
            {
                DetailRequested.TrySetResult(true); await Detail.Task.WaitAsync(ct);
                return Json("{\"content_id\":\"item\",\"type\":\"movie\",\"title\":\"Provider title\",\"locked_fields\":[]}");
            }
            throw new InvalidOperationException("Unexpected fixture request: " + path);
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
