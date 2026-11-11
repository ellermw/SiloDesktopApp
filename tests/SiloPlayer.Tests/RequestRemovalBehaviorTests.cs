using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// Official removeFailedRequest (06bd3370): DELETE /api/v2/requests/{id} answers a bodyless 204 for the owner's
/// failed request that delivered nothing, and 409 once it was retried, completed or partly delivered.
/// </summary>
public sealed class RequestRemovalBehaviorTests
{
    [Theory]
    [InlineData("{\"requests_enabled\":true,\"remove_failed_supported\":true}", true)]
    [InlineData("{\"requests_enabled\":true,\"remove_failed_supported\":false}", false)]
    [InlineData("{\"requests_enabled\":true}", false)]
    public async Task StatusAdvertisesRemovalOnlyWhenTheServerSaysSo(string json, bool supported)
    {
        using var wire = new Wire { Status = json }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-remove.invalid");
        Assert.Equal(supported, (await new RequestsApi(client).GetStatusAsync()).RemoveFailedSupported);
    }

    [Theory]
    [InlineData("failed", null, true)]
    [InlineData("failed", "failed", true)]
    [InlineData("failed", "completed", false)]
    [InlineData("failed", "completed|failed", false)]
    [InlineData("active", null, false)]
    [InlineData("declined", null, false)]
    [InlineData("cancelled", null, false)]
    [InlineData("completed", null, false)]
    public void OnlyFailedRequestsThatDeliveredNothingAreRemovable(string outcome, string? targets, bool removable)
    {
        var request = new MediaRequest { Id = "r", Outcome = outcome, Targets = targets?.Split('|').Select(status => new RequestTarget { Status = status }).ToList() };
        Assert.Equal(removable, RequestViewerPolicy.CanRemove(request));
        Assert.Equal(removable, RequestViewerPolicy.CanRemove(new() { RemoveFailedSupported = true }, request));
        Assert.False(RequestViewerPolicy.CanRemove(new() { RemoveFailedSupported = false }, request));
        // Removal and cancellation are separate actions over disjoint outcomes.
        Assert.False(removable && RequestViewerPolicy.CanCancel(request));
    }

    [Fact]
    public async Task RemoveSendsABodylessDeleteAndNeverTheCancelEndpoint()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-remove.invalid"); client.SetProfile("profile-a");
        var api = new RequestsApi(client);

        await api.RemoveFailedAsync("req/1 a", api.CaptureIdentityContext());

        var sent = Assert.Single(wire.Writes);
        Assert.Equal(HttpMethod.Delete, sent.Method);
        Assert.Equal("/api/v2/requests/req%2F1%20a", sent.RawPath);
        Assert.Null(sent.Body);
        Assert.False(sent.HadContent);
        Assert.DoesNotContain(wire.Writes, write => write.RawPath.EndsWith("/cancel"));
    }

    [Fact]
    public async Task ServerRefusalSurfacesAsAnApiExceptionWithItsStatus()
    {
        using var wire = new Wire { DeleteStatus = HttpStatusCode.Conflict }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-remove.invalid"); client.SetProfile("profile-a");
        var api = new RequestsApi(client);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.RemoveFailedAsync("r1", api.CaptureIdentityContext()));
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task ProfileChangedBeforeSendNeverDeletesUnderTheNewProfile()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-remove.invalid"); client.SetProfile("profile-a");
        var api = new RequestsApi(client); var owner = api.CaptureIdentityContext();
        client.SetProfile("profile-b");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.RemoveFailedAsync("r1", owner));
        Assert.Empty(wire.Writes);
    }

    [Fact]
    public async Task RemovingRefreshesAccountListDiscoveryAndActiveSearchFromTheServer()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        vm.SearchQuery = "arrival";
        Assert.True(vm.CanRemoveRequest(vm.MyRequests.Single(row => row.Id == "failed")));
        var before = wire.Reads.Count;

        var result = await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());

        Assert.Equal(RequestRemovalOutcome.Removed, result.Outcome);
        Assert.Equal(["failed"], wire.Writes.Select(write => write.RawPath.Split('/').Last()));
        Assert.Equal(["partial", "retryable"], vm.MyRequests.Select(row => row.Id).Order());
        var after = wire.Reads.Skip(before).ToArray();
        Assert.Contains("/api/v2/requests/mine", after);
        Assert.Contains("/api/v2/requests/discover", after);
        Assert.Contains("/api/v2/requests/search", after);
        Assert.False(vm.IsRemovingRequest("failed"));
    }

    [Fact]
    public async Task ServerAuthorityOnARetryRaceKeepsTheRowAndStopsOfferingRemoval()
    {
        using var wire = new Wire { DeleteStatus = HttpStatusCode.Conflict }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        var row = vm.MyRequests.Single(request => request.Id == "failed");
        // An admin retried the request between the list read and the DELETE: it is active again.
        wire.Mine = wire.Mine.Replace("{\"id\":\"failed\",\"title\":\"Failed\",\"status\":\"approved\",\"outcome\":\"failed\"}",
            "{\"id\":\"failed\",\"title\":\"Failed\",\"status\":\"approved\",\"outcome\":\"active\"}");

        var result = await vm.RemoveFailedRequestAsync(row, vm.CaptureRemovalOwner());

        Assert.Equal(RequestRemovalOutcome.Refused, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Single(wire.Writes);
        var refreshed = vm.MyRequests.Single(request => request.Id == "failed");
        Assert.Equal("active", refreshed.Outcome);
        Assert.False(vm.CanRemoveRequest(refreshed));
        Assert.True(RequestViewerPolicy.CanCancel(refreshed));
    }

    [Fact]
    public async Task TransientFailureKeepsTheRequestRecoverableAndRetryWorks()
    {
        using var wire = new Wire { DeleteStatus = HttpStatusCode.ServiceUnavailable }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));

        var failed = await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());

        Assert.Equal(RequestRemovalOutcome.Failed, failed.Outcome);
        Assert.Contains("failed", vm.MyRequests.Select(row => row.Id));
        Assert.False(vm.IsRemovingRequest("failed"));
        wire.DeleteStatus = HttpStatusCode.NoContent;
        var retry = await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());
        Assert.Equal(RequestRemovalOutcome.Removed, retry.Outcome);
        Assert.DoesNotContain("failed", vm.MyRequests.Select(row => row.Id));
        Assert.Equal(2, wire.Writes.Count);
    }

    [Fact]
    public async Task PartlyDeliveredAndUnsupportedRequestsNeverReachTheWire()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        Assert.Equal(RequestRemovalOutcome.Skipped, (await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "partial"), vm.CaptureRemovalOwner())).Outcome);
        Assert.Equal(RequestRemovalOutcome.Skipped, (await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "retryable"), vm.CaptureRemovalOwner())).Outcome);
        Assert.Empty(wire.Writes);

        using var oldWire = new Wire { Status = "{\"requests_enabled\":true}" }; using var oldHttp = new HttpClient(oldWire);
        var older = await LoadedAsync(NewClient(oldHttp));
        Assert.False(older.CanRemoveRequest(older.MyRequests.Single(row => row.Id == "failed")));
        Assert.Equal(RequestRemovalOutcome.Skipped, (await older.RemoveFailedRequestAsync(older.MyRequests.Single(row => row.Id == "failed"), older.CaptureRemovalOwner())).Outcome);
        Assert.Empty(oldWire.Writes);
    }

    [Fact]
    public async Task DoubleClickWhilePendingSendsOneDeleteAndShowsPendingState()
    {
        using var wire = new Wire { HoldDelete = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        var row = vm.MyRequests.Single(request => request.Id == "failed"); var owner = vm.CaptureRemovalOwner();

        var first = vm.RemoveFailedRequestAsync(row, owner);
        await UntilAsync(() => wire.Writes.Count == 1);
        Assert.True(vm.IsRemovingRequest("failed"));
        Assert.Equal(RequestRemovalOutcome.Skipped, (await vm.RemoveFailedRequestAsync(row, owner)).Outcome);
        wire.HoldDelete.SetResult();

        Assert.Equal(RequestRemovalOutcome.Removed, (await first).Outcome);
        Assert.Single(wire.Writes);
        Assert.False(vm.IsRemovingRequest("failed"));
    }

    [Fact]
    public async Task ProfileSwitchWhileDeleteIsPendingReportsAndRefreshesNothingForTheOldOwner()
    {
        using var wire = new Wire { HoldDelete = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var client = NewClient(http); var vm = await LoadedAsync(client);
        var removal = vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());
        await UntilAsync(() => wire.Writes.Count == 1);
        var reads = wire.Reads.Count;
        client.SetProfile("profile-b");
        wire.HoldDelete.SetResult();

        Assert.Equal(RequestRemovalOutcome.Retired, (await removal).Outcome);
        Assert.Equal(reads, wire.Reads.Count);
    }

    [Fact]
    public async Task ConfirmationFromAnotherProfileIsNotAppliedToTheCurrentOne()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = NewClient(http); var vm = await LoadedAsync(client);
        var row = vm.MyRequests.Single(request => request.Id == "failed"); var owner = vm.CaptureRemovalOwner();
        client.SetProfile("profile-b");
        Assert.Equal(RequestRemovalOutcome.Skipped, (await vm.RemoveFailedRequestAsync(row, owner)).Outcome);
        Assert.Empty(wire.Writes);
    }

    [Fact]
    public async Task PendingRemovalOwnsTheIdUntilItsAuthoritativeRefreshSettles()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        var row = vm.MyRequests.Single(request => request.Id == "failed");
        var owner = vm.CaptureRemovalOwner();
        var reads = wire.Reads.Count;
        wire.HoldStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var removal = vm.RemoveFailedRequestAsync(row, owner);
        try
        {
            await UntilAsync(() => wire.Reads.Skip(reads).Contains("/api/v2/requests/status"));
            Assert.True(vm.IsRemovingRequest(row.Id));
            Assert.Equal(RequestRemovalOutcome.Skipped, (await vm.RemoveFailedRequestAsync(row, owner)).Outcome);
            Assert.Single(wire.Writes);
        }
        finally { wire.HoldStatus.TrySetResult(); }
        Assert.Equal(RequestRemovalOutcome.Removed, (await removal).Outcome);
        Assert.False(vm.IsRemovingRequest(row.Id));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("visit")]
    [InlineData("access")]
    public async Task RetiringRemovalDuringRefreshSuppressesFeedbackAndFollowUpReads(string retirement)
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = NewClient(http); var vm = await LoadedAsync(client);
        using var visit = new CancellationTokenSource();
        vm.SearchQuery = "arrival";
        var reads = wire.Reads.Count;
        wire.HoldStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var removal = vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner(), visit.Token);
        await UntilAsync(() => wire.Reads.Skip(reads).Contains("/api/v2/requests/status"));
        if (retirement == "profile") client.SetProfile("profile-b");
        else if (retirement == "visit") visit.Cancel();
        else client.InvalidateAccessContext();
        wire.HoldStatus.TrySetResult();
        Assert.Equal(RequestRemovalOutcome.Retired, (await removal).Outcome);
        Assert.DoesNotContain("/api/v2/requests/search", wire.Reads.Skip(reads));
        Assert.DoesNotContain("/api/v2/requests/mine", wire.Reads.Skip(reads));
        Assert.False(vm.IsRemovingRequest("failed"));
    }

    [Fact]
    public async Task TransportTimeoutWhileTheOwnerIsCurrentFailsVisiblyAndRefreshes()
    {
        using var wire = new Wire { DeleteThrows = new TaskCanceledException("The request timed out.") }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        var reads = wire.Reads.Count;

        var result = await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());

        Assert.Equal(RequestRemovalOutcome.Failed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Contains("/api/v2/requests/mine", wire.Reads.Skip(reads));
        Assert.False(vm.IsRemovingRequest("failed"));
    }

    [Fact]
    public async Task LeavingDuringTheDeleteDoesNotAbortItAndTheNextVisitReadsTheListAgain()
    {
        using var wire = new Wire { HoldDelete = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        using var visit = new CancellationTokenSource();
        var removal = vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner(), visit.Token);
        await UntilAsync(() => wire.Writes.Count == 1);
        visit.Cancel();
        wire.HoldDelete.SetResult();

        Assert.Equal(RequestRemovalOutcome.Retired, (await removal).Outcome);
        Assert.DoesNotContain("failed", RemainingIds(wire.Mine));
        var reads = wire.Reads.Count;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("/api/v2/requests/mine", wire.Reads.Skip(reads));
        Assert.DoesNotContain("failed", vm.MyRequests.Select(row => row.Id));
    }

    [Fact]
    public async Task ReturningBeforeTheDeleteSettlesReconcilesTheCachedAccountList()
    {
        using var wire = new Wire { HoldDelete = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        using var visit = new CancellationTokenSource();
        var removal = vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner(), visit.Token);
        var reads = 0;
        try
        {
            await UntilAsync(() => wire.Writes.Count == 1);
            visit.Cancel();
            await vm.LoadCommand.ExecuteAsync(null);
            Assert.Contains("failed", vm.MyRequests.Select(row => row.Id));
            Assert.True(vm.IsRemovingRequest("failed"));
            reads = wire.Reads.Count;
        }
        finally { wire.HoldDelete.TrySetResult(); }

        Assert.Equal(RequestRemovalOutcome.Retired, (await removal).Outcome);
        Assert.Contains("/api/v2/requests/mine", wire.Reads.Skip(reads));
        Assert.DoesNotContain("failed", vm.MyRequests.Select(row => row.Id));
        Assert.False(vm.IsRemovingRequest("failed"));
        Assert.Single(wire.Writes);
    }

    [Fact]
    public async Task ReturningVisitIsNotSwallowedByARefreshItsPreviousVisitAbandoned()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        using var visit = new CancellationTokenSource();
        var reads = wire.Reads.Count;
        wire.HoldStatus = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var removal = vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner(), visit.Token);
        await UntilAsync(() => wire.Reads.Skip(reads).Contains("/api/v2/requests/status"));
        visit.Cancel();
        var returning = vm.LoadCommand.ExecuteAsync(null);
        wire.HoldStatus.TrySetResult();

        Assert.Equal(RequestRemovalOutcome.Retired, (await removal).Outcome);
        await returning;
        Assert.Contains("/api/v2/requests/mine", wire.Reads.Skip(reads));
        Assert.DoesNotContain("failed", vm.MyRequests.Select(row => row.Id));
    }

    [Fact]
    public async Task StatusReadFailureAfterRemovalStillReadsTheAccountListAgain()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var vm = await LoadedAsync(NewClient(http));
        wire.FailStatus = true;

        var result = await vm.RemoveFailedRequestAsync(vm.MyRequests.Single(row => row.Id == "failed"), vm.CaptureRemovalOwner());

        Assert.Equal(RequestRemovalOutcome.Removed, result.Outcome);
        Assert.Equal(["partial", "retryable"], vm.MyRequests.Select(row => row.Id).Order());
    }

    private static IEnumerable<string?> RemainingIds(string mine)
    {
        using var document = System.Text.Json.JsonDocument.Parse(mine);
        return document.RootElement.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString()).ToArray();
    }

    private static SiloApiClient NewClient(HttpClient http)
    {
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-remove.invalid"); client.SetProfile("profile-a");
        return client;
    }

    private static async Task<RequestsViewModel> LoadedAsync(SiloApiClient client)
    {
        var vm = new RequestsViewModel(new RequestsApi(client));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.MyRequests.Count);
        return vm;
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(10);
        Assert.True(ready());
    }

    private sealed record Write(HttpMethod Method, string RawPath, string? Body, bool HadContent);

    private sealed class Wire : HttpMessageHandler
    {
        public string Status = "{\"requests_enabled\":true,\"remove_failed_supported\":true}";
        public string Mine = "{\"items\":[" +
            "{\"id\":\"failed\",\"title\":\"Failed\",\"status\":\"approved\",\"outcome\":\"failed\"}," +
            "{\"id\":\"partial\",\"title\":\"Partial\",\"status\":\"approved\",\"outcome\":\"failed\",\"targets\":[{\"quality\":\"1080p\",\"status\":\"completed\"},{\"quality\":\"2160p\",\"status\":\"failed\"}]}," +
            "{\"id\":\"retryable\",\"title\":\"Retryable\",\"status\":\"pending\",\"outcome\":\"active\"}]}";
        public HttpStatusCode DeleteStatus = HttpStatusCode.NoContent;
        public TaskCompletionSource? HoldDelete;
        public TaskCompletionSource? HoldStatus;
        public Exception? DeleteThrows;
        public bool FailStatus;
        public readonly List<string> Reads = [];
        public readonly List<Write> Writes = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
                Writes.Add(new(request.Method, request.RequestUri.OriginalString.Split('?')[0][(request.RequestUri.GetLeftPart(UriPartial.Authority).Length)..], body, request.Content != null));
                if (HoldDelete is { } hold) await hold.Task.WaitAsync(ct);
                if (DeleteThrows != null) throw DeleteThrows;
                if (DeleteStatus == HttpStatusCode.NoContent)
                {
                    var id = Uri.UnescapeDataString(path.Split('/').Last());
                    Mine = RemoveRow(Mine, id);
                    return new(HttpStatusCode.NoContent);
                }
                return new(DeleteStatus) { Content = new StringContent("{\"title\":\"Not removable\",\"detail\":\"This request is no longer failed.\"}", Encoding.UTF8, "application/problem+json") };
            }
            Reads.Add(path);
            if (path.EndsWith("/status") && HoldStatus is { } statusGate) await statusGate.Task.WaitAsync(ct);
            if (path.EndsWith("/status") && FailStatus)
                return new(HttpStatusCode.BadGateway) { Content = new StringContent("{\"title\":\"Bad gateway\",\"detail\":\"Upstream unavailable.\"}", Encoding.UTF8, "application/problem+json") };
            var json = path.EndsWith("/status") ? Status : path.EndsWith("/mine") ? Mine : path.EndsWith("/search") ? "{\"results\":[],\"page\":1,\"total_pages\":1,\"total_results\":0}"
                : path.EndsWith("/genres") || path.EndsWith("/studios") || path.EndsWith("/networks") ? "{\"items\":[]}" : "{\"items\":[]}";
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string RemoveRow(string mine, string id)
        {
            using var document = System.Text.Json.JsonDocument.Parse(mine);
            var rows = document.RootElement.GetProperty("items").EnumerateArray().Where(row => row.GetProperty("id").GetString() != id).Select(row => row.GetRawText());
            return "{\"items\":[" + string.Join(",", rows) + "]}";
        }
    }
}
