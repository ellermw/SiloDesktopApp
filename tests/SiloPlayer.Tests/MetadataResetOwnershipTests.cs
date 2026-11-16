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

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task AcceptedResetWithUnreadableAdminJobReturnsQueuedAndReconciles(int status)
    {
        using var wire = new Wire { JobStatus = (HttpStatusCode)status }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var api = new MediaMaintenanceApi(client); var completed = true;
        wire.Detail.SetResult(true);
        await api.PendingWrites.RunAsync(client.CaptureIdentityContext(), "item",
            async ct => { completed = await api.ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], ct); },
            ct => new CatalogApi(client).GetItemDetailAsync("item", ct), default);
        Assert.False(completed);
        Assert.True(wire.DetailRequested.Task.IsCompleted);
        Assert.Equal(1, wire.Posts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedResetWithJobReadFailureReturnsQueuedAndReconciles(bool timeout)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var api = new MediaMaintenanceApi(client); var completed = true;
        wire.Job.SetException(timeout ? new TaskCanceledException("Job read timed out") : new HttpRequestException("Job connection dropped"));
        wire.Detail.SetResult(true);
        await api.PendingWrites.RunAsync(client.CaptureIdentityContext(), "item",
            async ct => { completed = await api.ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], ct); },
            ct => new CatalogApi(client).GetItemDetailAsync("item", ct), default);
        Assert.False(completed);
        Assert.True(wire.DetailRequested.Task.IsCompleted);
        Assert.Equal(1, wire.Posts);
    }

    [Theory]
    [InlineData("{\"state\":\"queued\"}")]
    [InlineData("{\"id\":\"job\"}")]
    [InlineData("{\"id\":\"job\",\"state\":\"unknown\"}")]
    public async Task AcceptedReceiptWithUnknownJobReturnsQueued(string receipt)
    {
        using var wire = new Wire { Receipt = receipt }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        Assert.False(await new MediaMaintenanceApi(client).ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], default));
        Assert.False(wire.JobRequested.Task.IsCompleted);
        Assert.Equal(1, wire.Posts);
    }

    [Fact]
    public async Task UnknownPolledJobStateReturnsQueued()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        wire.Job.SetResult("");
        Assert.False(await new MediaMaintenanceApi(client).ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], default));
        Assert.Equal(1, wire.Posts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredAcceptedJobWaitReturnsQueuedWithoutReplayingPost(bool longRetryAfter)
    {
        using var wire = new Wire { JobStatus = longRetryAfter ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var api = new MediaMaintenanceApi(client); using var budget = new CancellationTokenSource();
        var identity = client.CaptureIdentityContext(); var completed = true;
        var running = api.PendingWrites.RunAsync(identity, "item", async ct =>
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            completed = await api.ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], wait.Token);
        }, ct => new CatalogApi(client).GetItemDetailAsync("item", ct), default);
        await wire.JobRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var observer = api.PendingWrites.WaitForIdleAsync(identity, "item", default);
        budget.Cancel();
        await wire.DetailRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completed);
        Assert.False(observer.IsCompleted);
        wire.Detail.SetResult(true);
        await running;
        Assert.NotNull(await observer);
        Assert.Equal(1, wire.Posts);
    }

    [Fact]
    public async Task RetiredIdentityDuringAcceptedJobIsNotQueuedSuccess()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire); using var budget = new CancellationTokenSource();
        var client = new SiloApiClient(http); client.SetBaseUrl("https://metadata.invalid");
        var running = new MediaMaintenanceApi(client).ResetToProviderAndWaitAsync(new(client.CaptureContext(), true), "item", [], budget.Token);
        await wire.JobRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.SetBaseUrl("https://other.invalid"); budget.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
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
        public string Receipt = "{\"id\":\"job\",\"state\":\"queued\"}";
        public HttpStatusCode JobStatus = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/refresh-metadata"))
            {
                Posts++;
                return Json(Receipt);
            }
            if (path == "/api/v2/admin/jobs/job")
            {
                JobToken = ct; JobRequested.TrySetResult(true);
                if (JobStatus != HttpStatusCode.OK)
                {
                    var response = new HttpResponseMessage(JobStatus) { Content = new StringContent("{}") };
                    if (JobStatus == HttpStatusCode.ServiceUnavailable) response.Headers.TryAddWithoutValidation("Retry-After", "121");
                    return response;
                }
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
