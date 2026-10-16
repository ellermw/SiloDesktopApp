using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class NotificationSeriesTests
{
    [Fact]
    public async Task AdvertisedSeriesCapabilitySurvivesTheActualApiBoundary()
    {
        using var http = new HttpClient(new Wire(_ => Json("""{"series":{"available":true,"mute":true}}""")));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://series.invalid");
        var capability = await new NotificationsApi(client).GetCapabilityAsync();
        var serialized = JsonSerializer.SerializeToElement(capability, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.True(serialized.TryGetProperty("series", out var series), "The typed capability discarded the advertised Series operations.");
        Assert.True(series.GetProperty("available").GetBoolean());
        Assert.True(series.GetProperty("mute").GetBoolean());
    }

    [Fact]
    public async Task SeriesQueriesKeepFiltersAndNeverReplaceInboxReadCutoff()
    {
        var paths = new List<string>(); string? markBody = null;
        using var http = new HttpClient(new AsyncWire(async request =>
        {
            paths.Add(request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Post) { markBody = await request.Content!.ReadAsStringAsync(); return new(HttpStatusCode.NoContent); }
            if (request.RequestUri.AbsolutePath == "/api/v2/notifications") return Json("""{"items":[],"page":{"has_more":false},"read_cutoff":"inbox-boundary"}""");
            return Json("""{"items":[{"item":{"content_id":"s:1","type":"series","title":"Series"},"reasons":{"next_up":true},"active_reasons":{"next_up":true},"notifies":true,"next_up_after":{"season_number":2,"episode_number":8}}],"page":{"has_more":false}}""");
        }));
        var client = Client(http); var api = new NotificationsApi(client); var context = api.RequestContext;
        await api.GetNotificationsAsync();
        var options = new NotificationSeriesOptions(" bear & fox ", "next_up", "-year");
        var page = await api.GetSeriesAsync(context, "active", options);
        await api.GetSeriesAsync(context, "inactive", options, "opaque/+&");
        await api.MarkAllReadAsync();
        Assert.Contains("inbox-boundary", markBody);
        var row = Assert.Single(page.Items!); Assert.Equal("s:1", row.Item.ContentId); Assert.True(row.Reasons.NextUp); Assert.Equal(8, row.NextUpAfter!.EpisodeNumber);
        Assert.Contains("status=active&limit=50&image_size=small&q=bear%20%26%20fox&reason=next_up&sort=-year", paths[1]);
        Assert.Contains("status=inactive&limit=50&image_size=small&q=bear%20%26%20fox&reason=next_up&sort=-year&cursor=opaque%2F%2B%26", paths[2]);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"items\":[],\"page\":{}}")] [InlineData("{\"items\":[],\"page\":{\"has_more\":true}}")]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"same\"}}")]
    public async Task InvalidOrNonAdvancingSeriesPageHasVisibleFailure(string payload)
    {
        using var http = new HttpClient(new Wire(_ => Json(payload))); var client = Client(http); var api = new NotificationsApi(client);
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetSeriesAsync(api.RequestContext, "active", new(), "same"));
    }

    [Fact]
    public async Task MutesUseEscapedIdBodyless204AndRefuseStaleUndoBeforeDispatch()
    {
        var calls = new List<string>();
        using var http = new HttpClient(new Wire(r => { calls.Add(r.Method + " " + r.RequestUri!.AbsolutePath); return new(HttpStatusCode.NoContent); }));
        var client = Client(http); var api = new NotificationsApi(client); var context = api.RequestContext;
        await api.SetSeriesMutedAsync(context, "s:/&", true); await api.SetSeriesMutedAsync(context, "s:/&", false);
        Assert.Equal(new[] { "PUT /api/v2/notifications/series/s%3A%2F%26/mute", "DELETE /api/v2/notifications/series/s%3A%2F%26/mute" }, calls);
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SetSeriesMutedAsync(context, "s:/&", false)); Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task DualStreamsPageActiveBeforeInactiveAndEmptyPagesStillAdvance()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new Wire(r =>
        {
            var q = r.RequestUri!.Query; queries.Add(q);
            if (r.RequestUri.AbsolutePath.EndsWith("capabilities")) return Json("""{"series":{"available":true,"mute":true}}""");
            if (r.RequestUri.AbsolutePath.EndsWith("preferences")) return Json("""{"enabled":true}""");
            return Json(q.Contains("cursor=") ? """{"items":[],"page":{"has_more":false}}""" : """{"items":[],"page":{"has_more":true,"next_cursor":"next"}}""");
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        Assert.True(state.HasMore); Assert.False(state.IsEmpty); Assert.False(state.ShowInactive);
        await state.LoadMoreAsync(); Assert.Contains("status=active", queries[^1]); Assert.True(state.ShowInactive);
        await state.LoadMoreAsync(); Assert.Contains("status=inactive", queries[^1]); Assert.True(state.IsEmpty);
    }

    [Fact]
    public async Task AcceptedMuteSurvivesReloadFailureAndReadRetryDoesNotResubmitWrite()
    {
        var writes = 0; var failRead = false;
        using var http = new HttpClient(new Wire(r =>
        {
            if (r.Method != HttpMethod.Get) { writes++; failRead = true; return new(HttpStatusCode.NoContent); }
            if (r.RequestUri!.AbsolutePath.EndsWith("capabilities")) return Json("""{"series":{"available":true,"mute":true}}""");
            if (r.RequestUri.AbsolutePath.EndsWith("preferences")) return Json("""{"enabled":true}""");
            if (failRead) return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"detail\":\"fixture\"}") };
            return Json("""{"items":[{"item":{"content_id":"one","type":"series","title":"One"},"notifies":true,"reasons":{"favorite":true},"active_reasons":{"favorite":true}}],"page":{"has_more":false}}""");
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync(); var row = state.Active[0];
        Assert.True(await state.SetMutedAsync(row, true, state.Context!.Value));
        Assert.True(row.Muted); Assert.Null(state.MutationError); Assert.NotNull(state.Error); Assert.Equal(1, writes);
        await state.ReloadAsync(); Assert.Equal(1, writes); Assert.True(row.Muted);
    }

    [Fact]
    public async Task MissingSeriesCapabilityNeverRequestsListOrWrites()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Wire(r => { paths.Add(r.RequestUri!.AbsolutePath); return Json("{}"); }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        Assert.False(state.Available); Assert.False(state.CanMute); Assert.DoesNotContain("/api/v2/notifications/series", paths);
    }

    [Fact]
    public void ReasonsExplainNextUpAndMuteWithoutChangingRelationships()
    {
        var row = new NotificationSeries { Notifies = true, Reasons = new() { NextUp = true }, ActiveReasons = new() { NextUp = true }, NextUpAfter = new() { SeasonNumber = 2, EpisodeNumber = 8 } };
        Assert.Equal("Any episode after S02E08", row.Explanation(new() { Enabled = true }));
        row.Muted = true; row.Notifies = false; Assert.Equal("Muted", row.Explanation(new())); Assert.True(row.Reasons.NextUp);
        row.Muted = false; Assert.Equal("Notifications are off", row.Explanation(new()));
    }

    [Fact]
    public async Task FailedMuteRestoresFlagsAndLeavesRowAvailableForExplicitRetry()
    {
        var writes = 0; var fail = true;
        using var http = new HttpClient(new Wire(r =>
        {
            if (r.Method != HttpMethod.Get) { writes++; return new(fail ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent); }
            return SeriesResponse(r);
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync(); var row = state.Active[0];
        Assert.False(await state.SetMutedAsync(row, true, state.Context!.Value));
        Assert.False(row.Muted); Assert.True(row.Notifies); Assert.NotNull(state.MutationError); Assert.False(state.IsPending("one"));
        fail = false; Assert.True(await state.SetMutedAsync(row, true, state.Context.Value)); Assert.Equal(2, writes);
    }

    [Fact]
    public async Task ReplacedFilterDiscardsDelayedPriorPagesAndRetainsNewQueryForContinuation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncWire(async r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("/series") && r.RequestUri.Query.Contains("q=old")) { entered.TrySetResult(); await release.Task; }
            return SeriesResponse(r);
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        var old = state.SetOptionsAsync(new("old")); await entered.Task;
        await state.SetOptionsAsync(new("new", "favorite", "-year")); release.SetResult(); await old;
        Assert.Equal("new", state.Options.Q); Assert.False(state.IsLoading); Assert.Null(state.Error); Assert.Single(state.Active);
    }

    [Fact]
    public async Task LateMuteReceiptAfterProfileSwitchCannotPublishSuccessOrIssueReplacementReads()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
        using var http = new HttpClient(new AsyncWire(async r =>
        {
            if (r.Method != HttpMethod.Get) { entered.TrySetResult(); await release.Task; return new(HttpStatusCode.NoContent); }
            reads++; return SeriesResponse(r);
        }));
        var client = Client(http); var state = new NotificationSeriesState(new NotificationsApi(client)); await state.OpenAsync();
        var before = reads; var operation = state.SetMutedAsync(state.Active[0], true, state.Context!.Value); await entered.Task;
        client.SetProfile("replacement"); release.SetResult(); Assert.False(await operation); Assert.Equal(before, reads); Assert.Null(state.MutationError);
    }

    [Fact]
    public async Task UnmountedListCannotAcceptDelayedReadOrMutation()
    {
        using var http = new HttpClient(new Wire(SeriesResponse));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync(); var owner = state.Context!.Value; var row = state.Active[0]; state.Close();
        Assert.False(await state.SetMutedAsync(row, true, owner)); Assert.False(state.IsCurrent(owner));
    }

    [Fact]
    public async Task UnauthorizedMuteIsNotAutomaticallyReplayedThroughAuthenticationRefresh()
    {
        var writes = 0; var refresh = 0;
        using var http = new HttpClient(new Wire(_ => { writes++; return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }; }));
        var client = Client(http); client.SetTokenRefresher(_ => { refresh++; return Task.FromResult(true); });
        var api = new NotificationsApi(client);
        await Assert.ThrowsAsync<ApiException>(() => api.SetSeriesMutedAsync(api.RequestContext, "one", true)); Assert.Equal(1, writes); Assert.Equal(0, refresh);
    }

    [Theory]
    [InlineData("capability")] [InlineData("initial")] [InlineData("more")] [InlineData("mute")]
    public async Task CurrentOwnerHttpTimeoutRemainsAnActionableFailure(string stage)
    {
        var timeout = stage == "capability";
        using var http = new HttpClient(new Wire(r =>
        {
            if (timeout && (stage == "capability" || stage == "mute" && r.Method == HttpMethod.Put || stage is "initial" or "more" && r.RequestUri!.AbsolutePath.EndsWith("/series")))
                throw new TaskCanceledException("Fixture request timed out without owner cancellation.");
            if (stage == "more" && r.RequestUri!.AbsolutePath.EndsWith("/series")) return Json("""{"items":[],"page":{"has_more":true,"next_cursor":"next"}}""");
            return SeriesResponse(r);
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        timeout = true;
        if (stage == "initial") await state.ReloadAsync();
        if (stage == "more") await state.LoadMoreAsync();
        if (stage == "mute")
        {
            var row = state.Active[0]; Assert.False(await state.SetMutedAsync(row, true, state.Context!.Value));
            Assert.False(row.Muted); Assert.True(row.Notifies); Assert.NotNull(state.MutationError);
        }
        else Assert.NotNull(state.Error);
    }

    private static HttpResponseMessage SeriesResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("capabilities")) return Json("""{"series":{"available":true,"mute":true}}""");
        if (path.EndsWith("preferences")) return Json("""{"enabled":true,"notify_favorites":true}""");
        return Json("""{"items":[{"item":{"content_id":"one","title":"One","type":"series"},"notifies":true,"reasons":{"favorite":true},"active_reasons":{"favorite":true}}],"page":{"has_more":false}}""");
    }

    [Fact]
    public async Task CanceledSameProfileLifetimeCannotClearReplacementMutePendingState()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var writes = 0;
        using var http = new HttpClient(new AsyncWire(async r =>
        {
            if (r.Method != HttpMethod.Get)
            {
                if (++writes == 1) { firstEntered.TrySetResult(); await releaseFirst.Task; }
                else { secondEntered.TrySetResult(); await releaseSecond.Task; }
                return new(HttpStatusCode.NoContent);
            }
            return SeriesResponse(r);
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        var old = state.SetMutedAsync(state.Active[0], true, state.Context!.Value); await firstEntered.Task;
        state.Close(); await state.OpenAsync();
        var replacement = state.SetMutedAsync(state.Active[0], true, state.Context!.Value); await secondEntered.Task;
        releaseFirst.TrySetResult(); Assert.False(await old); Assert.True(state.IsPending("one")); Assert.Null(state.MutationError);
        releaseSecond.TrySetResult(); Assert.True(await replacement); Assert.False(state.IsPending("one"));
    }

    [Fact]
    public async Task CursorCycleLeavesRowsAndWaitsForExplicitRetry()
    {
        var calls = 0; var cursor = "a";
        using var http = new HttpClient(new Wire(r =>
        {
            if (!r.RequestUri!.AbsolutePath.EndsWith("/series")) return SeriesResponse(r);
            if (r.RequestUri.Query.Contains("status=inactive")) return Json("""{"items":[],"page":{"has_more":false}}""");
            calls++; return Json("{\"items\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"" + cursor + "\"}}");
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        cursor = "b"; await state.LoadMoreAsync(); cursor = "a"; await state.LoadMoreAsync(); Assert.NotNull(state.Error);
        var failedCalls = calls; await state.LoadMoreAsync(automatic: true); Assert.Equal(failedCalls, calls);
        cursor = "c"; await state.RetryAsync(); Assert.Null(state.Error); Assert.Equal(failedCalls + 1, calls);
    }

    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://series.invalid"); client.SetProfile("original"); return client; }
    private sealed class AsyncWire(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request); }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private sealed class Wire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
