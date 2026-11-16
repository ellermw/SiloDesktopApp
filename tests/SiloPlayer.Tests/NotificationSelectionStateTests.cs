using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Notifications;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class NotificationSelectionStateTests
{
    [Fact]
    public async Task MutedFilterIsASingleServerStreamWhenBulkIsAdvertised()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new Wire(request => { queries.Add(request.RequestUri!.Query); return Task.FromResult(Response(request, true)); }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        state.ToggleLoadedSelection(); Assert.Single(state.SelectedIds);
        await state.SetOptionsAsync(new("bear", Sort: "-year", Status: "muted"));
        Assert.Empty(state.SelectedIds);
        Assert.Contains("status=muted", queries[^1]); Assert.Contains("q=bear", queries[^1]); Assert.Contains("sort=-year", queries[^1]);
        Assert.DoesNotContain(queries, query => query.Contains("status=active") || query.Contains("status=inactive"));
    }

    [Fact]
    public async Task ContextRetiredDuringPostMutationReloadCannotPublishTheReceipt()
    {
        SiloApiClient? client = null; var written = false;
        using var http = new HttpClient(new Wire(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                written = true;
                return Task.FromResult(Json(new { results = new[] { new { series_id = "one", outcome = "applied" } } }));
            }
            if (written) client!.SetProfile("replacement");
            return Task.FromResult(Response(request, true));
        }));
        client = Client(http); var state = new NotificationSeriesState(new NotificationsApi(client)); await state.OpenAsync();
        Assert.Null(await state.SetManyMutedAsync(["one"], true, state.Context!.Value));
        Assert.Null(state.MutationError);
    }

    [Fact]
    public async Task MutedFilterAndSelectionRequireBulkCapability()
    {
        var queries = new List<string>();
        using var http = new HttpClient(new Wire(request =>
        {
            queries.Add(request.RequestUri!.Query);
            return Task.FromResult(Response(request, false));
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http)));
        await state.OpenAsync();
        Assert.True(state.CanMute); Assert.False(state.CanBulkMute);
        state.ToggleSelected("one"); await state.SelectAllMatchingAsync();
        await state.SetOptionsAsync(new(Status: "muted"));
        Assert.Empty(state.SelectedIds); Assert.Equal("all", state.Options.Status);
        Assert.DoesNotContain(queries, query => query.Contains("status=muted"));
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("query")]
    [InlineData("sort")]
    [InlineData("muted")]
    public async Task SelectionResetCancelsDelayedSelectAllMatching(string reset)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Wire(async request =>
        {
            if (request.RequestUri!.Query.Contains("cursor=next"))
            {
                entered.TrySetResult(); await release.Task;
                return Json(new { items = new[] { Row("late") }, page = new { has_more = false } });
            }
            return Response(request, true, true);
        }));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        state.ToggleSelected("one"); Assert.Single(state.SelectedIds);
        var selecting = state.SelectAllMatchingAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(state.IsSelectingAll);
        Assert.Equal("one", Assert.Single(state.SelectedIds));
        Assert.Null(await state.SetManyMutedAsync(["one"], true, state.Context!.Value));
        Assert.True(state.IsSelectingAll);
        if (reset == "clear") state.ClearSelection();
        else await state.SetOptionsAsync(reset switch
        {
            "query" => new("replacement"), "sort" => new(Sort: "-year"), _ => new(Status: "muted")
        });
        release.TrySetResult(); await selecting;
        Assert.Empty(state.SelectedIds); Assert.False(state.IsSelectingAll);
        Assert.DoesNotContain(state.Rows, row => row.Item.ContentId == "late");
    }

    [Fact]
    public async Task SelectAllMatchingLoadsEveryPageAndSelectsNothingOnFailure()
    {
        var fail = true;
        using var http = new HttpClient(new Wire(request => Task.FromResult(request.RequestUri!.Query.Contains("cursor=next")
            ? fail ? new HttpResponseMessage(HttpStatusCode.BadRequest) : Json(new { items = new[] { Row("two") }, page = new { has_more = false } })
            : Response(request, true, true))));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        await state.SelectAllMatchingAsync();
        Assert.Empty(state.SelectedIds); Assert.NotNull(state.Error); Assert.False(state.IsSelectingAll);
        fail = false; await state.RetryAsync(); await state.SelectAllMatchingAsync();
        Assert.Equal(new[] { "one", "two" }, state.SelectedIds.OrderBy(id => id));
    }

    [Fact]
    public async Task BulkReceiptCountsOnlyVisibleOutcomesAndUndoOnlyAppliedIds()
    {
        using var http = new HttpClient(new Wire(request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(new { results = new[] { new { series_id = "one", outcome = "applied" }, new { series_id = "two", outcome = "unchanged" }, new { series_id = "missing", outcome = "not_found" } } })
            : Response(request, true))));
        var state = new NotificationSeriesState(new NotificationsApi(Client(http))); await state.OpenAsync();
        var receipt = await state.SetManyMutedAsync(["one", "two", "missing"], true, state.Context!.Value);
        Assert.NotNull(receipt); Assert.Equal(2, receipt.Count); Assert.Equal(new[] { "one" }, receipt.AppliedIds);
        Assert.False(state.IsBulkMutating);
    }

    [Fact]
    public async Task PerNotificationReadsAreDeduplicatedAndNeverExceedSixInFlight()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object(); var active = 0; var maximum = 0; var calls = 0;
        using var http = new HttpClient(new Wire(async request =>
        {
            Assert.EndsWith("/read", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            lock (gate) { calls++; active++; maximum = Math.Max(maximum, active); if (active == 6) entered.TrySetResult(); }
            await release.Task;
            lock (gate) active--;
            return new(HttpStatusCode.NoContent);
        }));
        var api = new NotificationsApi(Client(http));
        var ids = Enumerable.Range(0, 13).Select(index => index.ToString()).ToArray();
        var marking = api.MarkManyReadAsync(api.RequestContext, ids.Concat(ids));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(6, calls); release.TrySetResult(); await marking;
        Assert.Equal(13, calls); Assert.Equal(6, maximum);
    }

    [Fact]
    public async Task InboxMuteDeselectsSeriesEntriesButRetainsRequests()
    {
        using var http = new HttpClient(new Wire(request => Task.FromResult(request.Method == HttpMethod.Post
            ? Json(new { results = new[] { new { series_id = "one", outcome = "applied" } } })
            : request.RequestUri!.AbsolutePath.EndsWith("/notifications")
                ? Json(new { items = new[] { new AppNotification { Id = "episode", Type = "episode.available", SeriesId = "one", CreatedAt = "2026-11-16T12:00:00Z" }, new AppNotification { Id = "request", Type = "request.fulfilled", SeriesId = "one", CreatedAt = "2026-11-16T12:00:00Z" } }, page = new { has_more = false } })
                : Response(request, true))));
        var api = new NotificationsApi(Client(http)); var vm = new NotificationsViewModel(api); await vm.LoadCommand.ExecuteAsync(null);
        vm.SetBulkMuteCapability(api.RequestContext, true); vm.SetInboxSelecting(true);
        foreach (var entry in vm.InboxSections.SelectMany(section => section.Entries)) vm.ToggleInboxSelection(entry.Key);
        Assert.Equal(2, vm.SelectedInboxEntries.Length);
        Assert.NotNull(await vm.SetInboxSeriesMutedAsync(["one"], true, api.RequestContext));
        Assert.Equal("request", Assert.Single(vm.SelectedInboxEntries).Latest.Id);
    }

    [Fact]
    public async Task InboxSelectedReadWritesOnlyUnreadIdsAndReconcilesAuthoritatively()
    {
        var read = new HashSet<string>(); var paths = new List<string>();
        using var http = new HttpClient(new Wire(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                paths.Add(path); read.Add("unread"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(path.EndsWith("/notifications")
                ? Json(new { items = new[]
                {
                    new AppNotification { Id = "unread", Type = "episode.available", SeriesId = "one", ReadAt = read.Contains("unread") ? "2026-11-16T13:00:00Z" : null },
                    new AppNotification { Id = "read", Type = "episode.available", SeriesId = "one", ReadAt = "2026-11-16T12:00:00Z" }
                }, page = new { has_more = false } })
                : path.EndsWith("unread-count") ? Json(new { count = read.Count == 0 ? 1 : 0 }) : Response(request, true));
        }));
        var vm = new NotificationsViewModel(new NotificationsApi(Client(http))); await vm.LoadCommand.ExecuteAsync(null);
        await vm.MarkInboxReadAsync(vm.Notifications.ToArray());
        Assert.Equal(new[] { "/api/v2/notifications/unread/read" }, paths);
        Assert.All(vm.Notifications, notification => Assert.False(notification.IsUnread));
        Assert.Null(vm.SelectionErrorMessage); Assert.False(vm.IsMarkingSelectedRead);
    }

    private static object Row(string id) => new { item = new { content_id = id, title = id, type = "series" }, notifies = true, reasons = new { favorite = true }, active_reasons = new { favorite = true } };
    private static HttpResponseMessage Response(HttpRequestMessage request, bool bulk, bool more = false)
    {
        var path = request.RequestUri!.AbsolutePath;
        return path.EndsWith("capabilities") ? Json(new { series = new { available = true, mute = true, bulk_mute = bulk } })
            : path.EndsWith("preferences") ? Json(new { enabled = true })
            : path.EndsWith("unread-count") ? Json(new { count = 2 })
            : Json(new { items = new[] { Row("one") }, page = new { has_more = more, next_cursor = more ? "next" : null } });
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://notification-selection.invalid"); client.SetProfile("original"); return client; }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })) };
    private sealed class Wire(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
