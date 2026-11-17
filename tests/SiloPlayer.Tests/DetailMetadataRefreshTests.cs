using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.MediaMaintenance;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Viewer audit 6c/6d: remote catalog item changes reach the detail cache and an
/// open detail page (official #4357 context), and Refresh Metadata follows its
/// job (official #4355, #4187).
/// </summary>
public sealed class DetailMetadataRefreshTests
{
    // ===== 6c: remote catalog item changes =====

    [Fact]
    public void OpenItemEventReloadsItsVisitOnceAfterTheCoalescingWindow() => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay, TimeSpan.FromMilliseconds(250));
        var reloads = new ReloadRecorder();
        using var watch = hub.Watch("movie-1", () => true, reloads.Reload);

        Assert.True(Send(hub, "catalog", "catalog.item.changed",
            """{"library_id":3,"content_id":"movie-1","change":"metadata_updated"}"""));
        Assert.Equal(0, reloads.Calls);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250) }, delay.Requested);

        delay.ReleaseNext();

        Assert.Equal(1, reloads.Calls);
        Assert.Equal(new[] { false }, reloads.Structural);
        Assert.Equal(0, delay.Pending);
    });

    [Fact]
    public async Task CachedItemEventDropsEveryScopeOfOnlyThatItem()
    {
        var reads = new Dictionary<string, int>(StringComparer.Ordinal);
        var cache = new ItemDetailPrefetchCache((string id, int? library, CancellationToken ct) =>
        {
            lock (reads) reads[id] = reads.GetValueOrDefault(id) + 1;
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });
        await cache.GetAsync("movie-2", CancellationToken.None);
        await cache.GetAsync("movie-2", 4, CancellationToken.None);
        await cache.GetAsync("movie-3", CancellationToken.None);
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(cache, delay.Delay);
        var reloads = new ReloadRecorder();
        using var open = hub.Watch("movie-1", () => true, reloads.Reload);

        Assert.True(Send(hub, "catalog", "metadata.updated", """{"content_id":"movie-2"}"""));
        await cache.GetAsync("movie-2", CancellationToken.None);
        await cache.GetAsync("movie-2", 4, CancellationToken.None);
        await cache.GetAsync("movie-3", CancellationToken.None);

        Assert.Equal(4, reads["movie-2"]);
        Assert.Equal(1, reads["movie-3"]);
        // A cached item's event is not a change to the open page's item.
        Assert.Equal(0, delay.Pending);
        Assert.Equal(0, reloads.Calls);
    }

    [Fact]
    public async Task UnrelatedEventsLeaveTheCacheAndOpenPageAlone()
    {
        var reads = 0;
        var cache = new ItemDetailPrefetchCache((string id, CancellationToken ct) =>
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(new MediaItemDetail { ContentId = id });
        });
        await cache.GetAsync("movie-1", CancellationToken.None);
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(cache, delay.Delay);
        var reloads = new ReloadRecorder();
        using var open = hub.Watch("movie-1", () => true, reloads.Reload);

        Assert.False(Send(hub, "catalog", "catalog.library.changed", """{"library_id":3}"""));
        Assert.False(Send(hub, "catalog", "catalog.item.changed", """{"library_id":3,"change":"added"}"""));
        Assert.False(Send(hub, "user_state", "user_state.changed", """{"content_id":"movie-1","change":"watched"}"""));
        Assert.True(Send(hub, "catalog", "catalog.item.changed", """{"content_id":"movie-9","change":"files_changed"}"""));
        await cache.GetAsync("movie-1", CancellationToken.None);

        Assert.Equal(1, reads);
        Assert.Equal(0, delay.Pending);
        Assert.Equal(0, reloads.Calls);
        // An uncached item records nothing, so a scan's event stream stays bounded.
        Assert.False(cache.InvalidateCached("movie-9"));
    }

    [Fact]
    public void BurstOfEventsCoalescesToOneReadAndOneTrailingRead() => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var reloads = new ReloadRecorder();
        using var watch = hub.Watch("movie-1", () => true, reloads.Reload);

        for (var i = 0; i < 25; i++)
            Send(hub, "catalog", "catalog.item.changed", """{"content_id":"movie-1","change":"metadata_updated"}""");
        Assert.Single(delay.Requested);
        delay.ReleaseNext();
        Assert.Equal(1, reloads.Calls);

        // Changes during an in-flight read coalesce into one read after it.
        var inFlight = new TaskCompletionSource<bool>();
        reloads.Next = () => inFlight.Task;
        hub.NotifyItemChanged("movie-1");
        delay.ReleaseNext();
        Assert.Equal(2, reloads.Calls);
        for (var i = 0; i < 10; i++) hub.NotifyItemChanged("movie-1");
        Assert.Equal(0, delay.Pending);

        reloads.Next = () => Task.FromResult(true);
        inFlight.SetResult(true);
        Assert.Equal(1, delay.Pending);
        delay.ReleaseNext();

        Assert.Equal(3, reloads.Calls);
        Assert.Equal(0, delay.Pending);
    });

    [Fact]
    public void IdentityChangeDropsPendingWork() => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var reloads = new ReloadRecorder();
        var current = true;
        using var watch = hub.Watch("movie-1", () => current, reloads.Reload);

        hub.NotifyItemChanged("movie-1");
        current = false; // sign-out or profile switch before the window ends
        delay.ReleaseNext();

        Assert.Equal(0, reloads.Calls);
        Assert.False(watch.IsActive);
        hub.NotifyItemChanged("movie-1");
        Assert.Equal(0, delay.Pending);

        // The next visit under the new identity watches afresh.
        using var next = hub.Watch("movie-1", () => true, reloads.Reload);
        hub.NotifyItemChanged("movie-1");
        delay.ReleaseNext();
        Assert.Equal(1, reloads.Calls);
    });

    [Fact]
    public void HeldReloadWaitsForTheEditorToCloseAndKeepsItsStructuralChange() => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var reloads = new ReloadRecorder { Next = () => Task.FromResult(false) }; // editor or write pending
        using var watch = hub.Watch("movie-1", () => true, reloads.Reload);

        watch.Signal(structural: true);
        delay.ReleaseNext();
        Assert.Equal(1, reloads.Calls);
        Assert.True(watch.IsHeld);
        Assert.Equal(0, delay.Pending); // nothing polls while held

        reloads.Next = () => Task.FromResult(true);
        watch.Resume();
        delay.ReleaseNext();

        Assert.Equal(2, reloads.Calls);
        Assert.Equal(new[] { true, true }, reloads.Structural);
        Assert.False(watch.IsHeld);
        watch.Resume();
        Assert.Equal(0, delay.Pending);
    });

    [Fact]
    public void NavigationAwayCancelsAPendingReload()
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var reloads = new ReloadRecorder();
        var watch = hub.Watch("movie-1", () => true, reloads.Reload);

        hub.NotifyItemChanged("movie-1");
        watch.Dispose();
        hub.NotifyItemChanged("movie-1");

        Assert.Equal(0, delay.Pending);
        Assert.Equal(0, reloads.Calls);
        Assert.Equal(0, hub.WatchCount);
    }

    [Fact]
    public void FailedReloadStaysPendingWithItsStructuralSignal() => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var reloads = new ReloadRecorder { Next = () => throw new HttpRequestException("offline") };
        using var watch = hub.Watch("movie-1", () => true, reloads.Reload);

        watch.Signal(structural: true);
        delay.ReleaseNext();
        Assert.True(watch.IsHeld);
        Assert.Equal(0, delay.Pending);

        reloads.Next = () => Task.FromResult(true);
        watch.Resume();
        delay.ReleaseNext();
        Assert.Equal(new[] { true, true }, reloads.Structural);
        Assert.False(watch.IsHeld);
    });

    [Fact]
    public async Task ServerMetadataReloadUpdatesReaderChaptersAndBookExtensionsInPlace()
    {
        using var scope = new DetailViewModelTestScope();
        var fresh = new MediaItemDetail
        {
            ContentId = "reader-1", Type = "manga",
            Manga = new() { Chapters = [new() { ContentId = "chapter-2", Title = "New chapter" }] },
            Audiobook = new(), Ebook = new()
        };
        var vm = scope.CreateWithCatalog((_, _) => Task.FromResult(fresh));
        var mounted = new MediaItemDetail
        {
            ContentId = fresh.ContentId, Type = fresh.Type,
            Manga = new() { Chapters = [new() { ContentId = "chapter-1" }] },
            Audiobook = new(), Ebook = new(), UserState = new() { IsFavorite = true }
        };
        vm.Item = mounted;
        var audiobook = mounted.Audiobook;
        var ebook = mounted.Ebook;
        var viewerState = mounted.UserState;

        Assert.True(await vm.RefreshServerMetadataAsync(mounted.ContentId));

        Assert.Same(mounted, vm.Item);
        Assert.Equal("chapter-2", Assert.Single(mounted.Manga!.Chapters).ContentId);
        Assert.NotNull(mounted.Audiobook);
        Assert.NotNull(mounted.Ebook);
        Assert.NotSame(audiobook, mounted.Audiobook);
        Assert.NotSame(ebook, mounted.Ebook);
        Assert.Same(viewerState, mounted.UserState);
        fresh.Manga = null; fresh.Audiobook = null; fresh.Ebook = null;
        Assert.True(await vm.RefreshServerMetadataAsync(mounted.ContentId));
        Assert.Null(mounted.Manga);
        Assert.Null(mounted.Audiobook);
        Assert.Null(mounted.Ebook);
    }

    [Fact]
    public async Task FailedMetadataReadDoesNotAcknowledgeTheChange()
    {
        using var scope = new DetailViewModelTestScope();
        var vm = scope.CreateWithCatalog((_, _) => throw new ApiException("forbidden", "read failed", 403));
        var mounted = new MediaItemDetail { ContentId = "movie-1", Type = "movie", Title = "Mounted" };
        vm.Item = mounted;

        Assert.False(await vm.RefreshServerMetadataAsync(mounted.ContentId));

        Assert.Same(mounted, vm.Item);
        Assert.Equal("Mounted", mounted.Title);
        Assert.NotNull(vm.MetadataRefreshErrorMessage);
        Assert.False(vm.IsMetadataRefreshing);
    }

    [Fact]
    public async Task SupersededMetadataReadDoesNotAcknowledgeTheChange()
    {
        using var scope = new DetailViewModelTestScope();
        var blocked = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var vm = scope.CreateWithCatalog((id, _) => ++reads == 1 ? blocked.Task
            : Task.FromResult(new MediaItemDetail { ContentId = id, Type = "movie", Title = "Newer" }));
        vm.Item = new() { ContentId = "movie-1", Type = "movie" };
        var first = vm.RefreshServerMetadataAsync("movie-1");
        Assert.True(await vm.RefreshServerMetadataAsync("movie-1"));
        blocked.SetResult(new() { ContentId = "movie-1", Type = "movie", Title = "Older" });

        Assert.False(await first);
        Assert.Equal("Newer", vm.Item.Title);
    }

    // ===== 6d: Refresh Metadata follows its job =====

    [Fact]
    public async Task TransientServiceUnavailableHonorsRetryAfterAndKeepsWaiting()
    {
        var handler = new ScriptedHandler();
        // A proxy's non-JSON 503 still carries Retry-After.
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "<html>busy</html>", TimeSpan.FromSeconds(7), "text/html");
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"), TimeSpan.FromSeconds(5));
        handler.Enqueue(HttpStatusCode.OK, JobJson("succeeded", newFiles: 2));
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var time = new FakeTime();

        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5) }, time.Waits);
        Assert.Equal(3, handler.Paths.Count);
        Assert.All(handler.Paths, path => Assert.Equal("/api/v2/admin/jobs/job-1", path));
        Assert.Equal(2, result.Job!.ItemResult!.NewFiles);
        Assert.Equal("movie-1", result.Job.ItemResult.DetailContentId);
    }

    [Fact]
    public async Task LongRetryAfterEndsTheBoundedWaitWithoutAnEarlyRead()
    {
        foreach (var seconds in new[] { 3600d, 1e15, double.PositiveInfinity })
        {
            var reads = 0;
            var time = new FakeTime();
            var follower = new MaintenanceJobFollower(_ =>
                {
                    reads++;
                    throw new ApiException("unavailable", "busy", 503, retryAfterSeconds: seconds);
                },
                () => true, time.Delay, () => time.Now) { MaxWait = TimeSpan.FromMinutes(10) };

            var result = await follower.WaitAsync();

            Assert.Equal(MaintenanceJobWaitOutcome.TimedOut, result.Outcome);
            Assert.Equal(1, reads);
            Assert.Empty(time.Waits);
            Assert.IsType<ApiException>(result.Error);
        }
    }

    [Fact]
    public async Task RetryAfterWithinTheWaitIsHonoredBeyondTheBackoffCap()
    {
        var attempts = 0;
        var time = new FakeTime();
        var follower = new MaintenanceJobFollower(
            _ => attempts++ == 0
                ? throw new ApiException("rate_limited", "slow down", 429, retryAfterSeconds: 120)
                : Task.FromResult(new MaintenanceJobRead(Job("succeeded"), null)),
            () => true, time.Delay, () => time.Now);

        var result = await follower.WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { TimeSpan.FromSeconds(120) }, time.Waits);
    }

    [Fact]
    public async Task RepeatedNetworkFailuresBackOffToThirtySeconds()
    {
        var attempts = 0;
        var time = new FakeTime();
        var follower = new MaintenanceJobFollower(
            _ => attempts++ < 6
                ? throw new HttpRequestException("offline")
                : Task.FromResult(new MaintenanceJobRead(Job("succeeded"), null)),
            () => true, time.Delay, () => time.Now);

        var result = await follower.WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { 2d, 4, 8, 16, 30, 30 }, time.Waits.Select(wait => wait.TotalSeconds));
    }

    [Fact]
    public async Task SignOutMidWaitEndsQuietlyWithoutAnotherRead()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"), TimeSpan.FromSeconds(5));
        var (client, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var time = new FakeTime { OnWait = client.ClearAuth };

        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Stale, result.Outcome);
        Assert.Single(handler.Paths);
        Assert.Equal(MetadataRefreshFollowAction.None,
            MetadataRefreshFollow.Decide(result, "movie-1", "quick", null).Action);
        Assert.Empty(MetadataRefreshFollow.InvalidatedIds(result, "movie-1", "movie", null));
    }

    [Fact]
    public async Task FailedReadAfterSignOutIsStaleNotAFailure()
    {
        var current = true;
        var time = new FakeTime();
        var follower = new MaintenanceJobFollower(_ =>
            {
                current = false;
                throw new ApiException("unauthorized", "expired", 401);
            },
            () => current, time.Delay, () => time.Now);

        var result = await follower.WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Stale, result.Outcome);
        Assert.Empty(time.Waits);
    }

    [Fact]
    public async Task SocketRebindBetweenReadsDoesNotEndTheWait()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"));
        handler.Enqueue(HttpStatusCode.OK, JobJson("succeeded"));
        var (client, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        // A reconnect catch-up retires request-context reads, not the identity.
        var time = new FakeTime { OnWait = () => client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true) };

        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, handler.Paths.Count);
        Assert.Equal(new[] { MaintenanceJobFollower.DefaultPollInterval }, time.Waits);

        // A read retired mid-flight while the identity holds is retried, not reported.
        var attempts = 0;
        var retired = await new MaintenanceJobFollower(
            _ => attempts++ == 0
                ? throw new OperationCanceledException("API context changed.")
                : Task.FromResult(new MaintenanceJobRead(Job("succeeded"), null)),
            () => true, new FakeTime().Delay).WaitAsync();
        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, retired.Outcome);
    }

    [Fact]
    public async Task ServerAnswersAndFinishedJobsEndTheWait()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.NotFound, """{"type":"https://example.test/docs/api/v2/problems/not_found","title":"Not Found","status":404}""",
            mediaType: "application/problem+json");
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var notFound = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => true, new FakeTime().Delay).WaitAsync();
        Assert.Equal(MaintenanceJobWaitOutcome.Failed, notFound.Outcome);
        Assert.Equal(404, Assert.IsType<ApiException>(notFound.Error).StatusCode);
        Assert.Single(handler.Paths);

        var failed = await new MaintenanceJobFollower(_ => Task.FromResult(new MaintenanceJobRead(Job("failed"), null)),
            () => true, new FakeTime().Delay).WaitAsync();
        Assert.Equal(MaintenanceJobWaitOutcome.Failed, failed.Outcome);
        Assert.Equal("provider down", failed.Job!.Failure!.Detail);

        var canceled = await new MaintenanceJobFollower(_ => Task.FromResult(new MaintenanceJobRead(Job("canceled"), null)),
            () => true, new FakeTime().Delay).WaitAsync();
        Assert.Equal(MaintenanceJobWaitOutcome.Canceled, canceled.Outcome);
    }

    [Fact]
    public void NavigationAwayNeverReloadsAnotherPage()
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var starting = new ReloadRecorder();
        var foreign = new ReloadRecorder();
        var visit = hub.Watch("movie-1", () => true, starting.Reload);
        var success = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded"));

        var stillOpen = MetadataRefreshFollow.Decide(success, "movie-1", "quick", visit);
        Assert.Equal(MetadataRefreshFollowAction.Reload, stillOpen.Action);
        Assert.False(stillOpen.Structural);

        visit.Dispose(); // navigated to another item, then back to this one
        using var other = hub.Watch("movie-2", () => true, foreign.Reload);
        using var revisit = hub.Watch("movie-1", () => true, foreign.Reload);

        Assert.Equal(MetadataRefreshFollowAction.None,
            MetadataRefreshFollow.Decide(success, "movie-1", "quick", visit).Action);
        var replaced = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded", detailId: "movie-1b"));
        Assert.Equal(MetadataRefreshFollowAction.None,
            MetadataRefreshFollow.Decide(replaced, "movie-1", "complete", visit).Action);
        Assert.Equal(0, delay.Pending);
        Assert.Equal(0, foreign.Calls);
        // The cache still drops the refreshed item for whichever page reads it next.
        Assert.Equal(new[] { "movie-1" }, MetadataRefreshFollow.InvalidatedIds(success, "movie-1", "movie", null));
    }

    [Fact]
    public void FinishedRefreshReloadsOrFollowsOnlyTheStartingVisit()
    {
        var hub = new DetailMetadataChangeHub(null, new ManualDelay().Delay);
        using var visit = hub.Watch("episode-1", () => true, new ReloadRecorder().Reload);

        var complete = MetadataRefreshFollow.Decide(
            new(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded")), "episode-1", "complete", visit);
        Assert.Equal(MetadataRefreshFollowAction.Reload, complete.Action);
        Assert.True(complete.Structural);
        Assert.True(MetadataRefreshFollow.Decide(
            new(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded", newFiles: 1)), "episode-1", "quick", visit).Structural);

        var rebuilt = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded,
            Job("succeeded", detailId: "episode-1b", refreshId: "episode-1r"));
        var follow = MetadataRefreshFollow.Decide(rebuilt, "episode-1", "complete", visit);
        Assert.Equal(MetadataRefreshFollowAction.FollowReplacement, follow.Action);
        Assert.Equal("episode-1b", follow.ReplacementContentId);
        Assert.Equal(new[] { "episode-1", "episode-1r", "episode-1b", "series-1" },
            MetadataRefreshFollow.InvalidatedIds(rebuilt, "episode-1", "episode", "series-1"));

        foreach (var outcome in new[] { MaintenanceJobWaitOutcome.Failed, MaintenanceJobWaitOutcome.Canceled, MaintenanceJobWaitOutcome.Stale })
            Assert.Equal(MetadataRefreshFollowAction.None,
                MetadataRefreshFollow.Decide(new(outcome, Job("running")), "episode-1", "quick", visit).Action);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(200)]
    public async Task UnreadableJobReloadsOnlyTheStartingVisitWithoutClaimingJobFailure(int status)
    {
        var handler = new ScriptedHandler();
        handler.Enqueue((HttpStatusCode)status, status == 200 ? "<html>proxy</html>"
            : """{"type":"forbidden","title":"Job unavailable","status":403}""");
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => true, new FakeTime().Delay).WaitAsync();
        Assert.NotNull(result.Error);
        Assert.Single(handler.Paths);
        var hub = new DetailMetadataChangeHub(null, new ManualDelay().Delay);
        using var visit = hub.Watch("movie-1", () => true, new ReloadRecorder().Reload);

        var decision = MetadataRefreshFollow.Decide(result, "movie-1", "complete", visit);

        Assert.Equal(MetadataRefreshFollowAction.Reload, decision.Action);
        Assert.True(decision.Structural);
        Assert.Null(decision.ReplacementContentId);
        Assert.Equal(new[] { "movie-1" }, MetadataRefreshFollow.InvalidatedIds(result, "movie-1", "movie", null));
        visit.Dispose();
        Assert.Equal(MetadataRefreshFollowAction.None,
            MetadataRefreshFollow.Decide(result, "movie-1", "complete", visit).Action);
    }

    [Fact]
    public void TimedOutJobReloadsWithoutFollowingAnUnconfirmedReplacement()
    {
        var hub = new DetailMetadataChangeHub(null, new ManualDelay().Delay);
        using var visit = hub.Watch("movie-1", () => true, new ReloadRecorder().Reload);
        var result = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.TimedOut,
            new MaintenanceJobStatus { State = "running", ItemResult = new() { DetailContentId = "other" } });

        var quick = MetadataRefreshFollow.Decide(result, "movie-1", "quick", visit);
        Assert.Equal(MetadataRefreshFollowAction.Reload, quick.Action);
        Assert.False(quick.Structural);
        Assert.Null(quick.ReplacementContentId);
        Assert.True(MetadataRefreshFollow.Decide(result, "movie-1", "complete", visit).Structural);
    }

    [Fact]
    public void DetailPageUsesLocalizedRefreshToastsAndTheShellRoutesCatalogEvents()
    {
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var refresh = Read("src", "SiloPlayer", "Views", "ItemDetailPage.ServerRefresh.cs");
        var flow = Read("src", "SiloPlayer", "Services", "MetadataRefreshFlow.cs");
        var toasts = Read("src", "SiloPlayer.Core", "Services", "MetadataRefreshFollow.cs");
        var shell = Read("src", "SiloPlayer", "MainWindow.xaml.cs");
        var english = Read("src", "SiloPlayer.Core", "Localization", "Generated", "en.json");

        Assert.DoesNotContain("refresh queued", page + refresh, StringComparison.OrdinalIgnoreCase);
        foreach (var key in new[]
                 {
                     "web.item.toast.refreshRunningQuick", "web.item.toast.refreshRunningComplete",
                     "web.item.toast.refreshed", "web.item.toast.completeRefreshFinished",
                     "web.item.toast.refreshedNewFiles", "web.item.toast.refreshArtworkWarning",
                     "web.item.toast.refreshFailed",
                 })
        {
            Assert.Contains($"\"{key}\"", toasts);
            Assert.Contains($"\"{key}\":", english);
        }
        Assert.Contains("api.GetJobAsync(identity, receipt.Id, token)", flow);
        Assert.Contains("BeginServerMetadataWatch(contentId);", page);
        Assert.Contains("EndServerMetadataWatch();", page);
        Assert.Contains("Subscribe(\"notifications\", \"user_settings\", \"user_state\", \"catalog\")", shell);
        Assert.Contains("GetService<DetailMetadataChangeHub>()?.Receive(channel, eventName, data)", shell);
    }

    // ===== 6d: the card menu follows the same job =====

    [Fact]
    public void RunningAndFinishToastsAreLocalizedAndMatchTheOfficialHook()
    {
        Assert.Equal(new MetadataRefreshToast(MetadataRefreshToastKind.Info, "web.item.toast.refreshRunningQuick"),
            MetadataRefreshFollow.RunningToast("quick"));
        Assert.Equal(new MetadataRefreshToast(MetadataRefreshToastKind.Info, "web.item.toast.refreshRunningComplete"),
            MetadataRefreshFollow.RunningToast("complete"));

        MetadataRefreshToast Finish(MaintenanceJobWaitOutcome outcome, MaintenanceJobStatus? job, string mode = "quick")
            => MetadataRefreshFollow.FinishToast(new(outcome, job), mode);

        var plain = Finish(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded"));
        Assert.Equal((MetadataRefreshToastKind.Success, "web.item.toast.refreshed"), (plain.Kind, plain.Key));
        var complete = Finish(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded"), "complete");
        Assert.Equal((MetadataRefreshToastKind.Success, "web.item.toast.completeRefreshFinished"), (complete.Kind, complete.Key));
        var added = Finish(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded", newFiles: 3));
        Assert.Equal((MetadataRefreshToastKind.Success, "web.item.toast.refreshedNewFiles"), (added.Kind, added.Key));
        Assert.Equal(3L, added.Arguments!["count"]);
        Assert.Equal(3L, added.Arguments["new_files"]);

        var artwork = Job("succeeded");
        artwork.ItemResult!.ArtworkCacheIncomplete = true;
        foreach (var mode in new[] { "quick", "complete" })
        {
            var warning = Finish(MaintenanceJobWaitOutcome.Succeeded, artwork, mode);
            Assert.Equal((MetadataRefreshToastKind.Warning, "web.item.toast.refreshArtworkWarning"), (warning.Kind, warning.Key));
        }

        foreach (var outcome in new[] { MaintenanceJobWaitOutcome.Failed, MaintenanceJobWaitOutcome.Canceled })
        {
            var failed = Finish(outcome, Job("failed"));
            Assert.Equal((MetadataRefreshToastKind.Error, "web.item.toast.refreshFailed"), (failed.Kind, failed.Key));
        }
        // A wait that ran out or went stale says nothing: the job may still be running, or the account changed.
        foreach (var outcome in new[] { MaintenanceJobWaitOutcome.TimedOut, MaintenanceJobWaitOutcome.Stale })
            Assert.Equal(MetadataRefreshToastKind.None, Finish(outcome, Job("running")).Kind);
        // A failed read of the job is not a failed refresh: completion is unknown, so nothing is said.
        Assert.Equal(MetadataRefreshToastKind.None,
            MetadataRefreshFollow.FinishToast(new(MaintenanceJobWaitOutcome.Failed, null, new HttpRequestException("job read")), "quick").Kind);
    }

    [Fact]
    public void FinishedCardRefreshNotifiesEveryTouchedItemAndRefreshDestinationsStructurally()
    {
        var rebuilt = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded,
            Job("succeeded", detailId: "episode-1b", refreshId: "episode-1r"));

        var complete = MetadataRefreshFollow.ChangedItems(rebuilt, "episode-1", "complete", "episode", "series-1");
        Assert.Equal(new[]
        {
            new MetadataRefreshChange("episode-1", true), new MetadataRefreshChange("episode-1r", true),
            new MetadataRefreshChange("episode-1b", true), new MetadataRefreshChange("series-1", false),
        }, complete);

        // A quick refresh is structural only when it found files.
        var quick = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded"));
        Assert.Equal(new[] { new MetadataRefreshChange("movie-1", false) },
            MetadataRefreshFollow.ChangedItems(quick, "movie-1", "quick", "movie", null));
        var found = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded", newFiles: 1));
        Assert.Equal(new[] { new MetadataRefreshChange("movie-1", true) },
            MetadataRefreshFollow.ChangedItems(found, "movie-1", "quick", "movie", null));

        // A server-reported failure or cancellation, or a stale wait, changed nothing to reread.
        foreach (var outcome in new[] { MaintenanceJobWaitOutcome.Failed, MaintenanceJobWaitOutcome.Canceled, MaintenanceJobWaitOutcome.Stale })
            Assert.Empty(MetadataRefreshFollow.ChangedItems(new(outcome, Job("running")), "movie-1", "complete", "movie", null));
        // An unknown completion rereads only the item itself, as Decide reloads it, trusting no job data.
        Assert.Equal(new[] { new MetadataRefreshChange("movie-1", true) },
            MetadataRefreshFollow.ChangedItems(new(MaintenanceJobWaitOutcome.TimedOut, Job("running", detailId: "movie-1b")), "movie-1", "complete", "movie", null));
        Assert.Equal(new[] { new MetadataRefreshChange("movie-1", false) },
            MetadataRefreshFollow.ChangedItems(new(MaintenanceJobWaitOutcome.Failed, null, new HttpRequestException("job read")), "movie-1", "quick", "movie", null));
    }

    [Fact]
    public async Task CardRefreshPollsTheJobThenRereadsOpenDetailPagesOfTheItem()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"), TimeSpan.FromSeconds(4));
        handler.Enqueue(HttpStatusCode.OK, JobJson("succeeded", newFiles: 1));
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var time = new FakeTime();

        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, time.Waits);
        Assert.Equal(2, handler.Paths.Count);

        // The same notification the card menu sends once the job finished: the open page rereads
        // once, structurally because the refresh found a file, and a page of another item does not.
        OnThreadPool(() =>
        {
            var delay = new ManualDelay();
            var hub = new DetailMetadataChangeHub(null, delay.Delay);
            var open = new ReloadRecorder();
            var other = new ReloadRecorder();
            using var openWatch = hub.Watch("movie-1", () => true, open.Reload);
            using var otherWatch = hub.Watch("movie-2", () => true, other.Reload);
            foreach (var change in MetadataRefreshFollow.ChangedItems(result, "movie-1", "quick", "movie", null))
                hub.NotifyItemChanged(change.ContentId, change.Structural);
            Assert.Equal(0, open.Calls);
            Assert.Equal(1, delay.Pending);

            delay.ReleaseNext();

            Assert.Equal(new[] { true }, open.Structural);
            Assert.Equal(0, other.Calls);
        });
    }

    [Theory]
    [InlineData("complete", 0)]
    [InlineData("quick", 1)]
    public void CardRefreshRereadsAnAlreadyMountedReplacementStructurally(string mode, int newFiles) => OnThreadPool(() =>
    {
        var delay = new ManualDelay();
        var hub = new DetailMetadataChangeHub(null, delay.Delay);
        var destination = new ReloadRecorder();
        var ancestor = new ReloadRecorder();
        using var destinationWatch = hub.Watch("episode-1b", () => true, destination.Reload);
        using var ancestorWatch = hub.Watch("series-1", () => true, ancestor.Reload);
        var result = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded,
            Job("succeeded", detailId: "episode-1b", refreshId: "episode-1r", newFiles: newFiles));

        foreach (var change in MetadataRefreshFollow.ChangedItems(result, "episode-1", mode, "episode", "series-1"))
            hub.NotifyItemChanged(change.ContentId, change.Structural);
        while (delay.Pending > 0) delay.ReleaseNext();

        Assert.Equal(new[] { true }, destination.Structural);
        Assert.Equal(new[] { false }, ancestor.Structural);
    });

    [Fact]
    public async Task CardCompletionReconcilesAnOpenEbooksInventoryWithoutReplacingViewerState()
    {
        using var scope = new DetailViewModelTestScope();
        var fresh = new MediaItemDetail
        {
            ContentId = "ebook-1", Type = "ebook",
            Versions = [new() { FileId = 2, FileName = "replacement.epub" }]
        };
        var vm = scope.CreateWithCatalog((_, _) => Task.FromResult(fresh));
        var mounted = new MediaItemDetail
        {
            ContentId = fresh.ContentId, Type = fresh.Type,
            Versions = [new() { FileId = 1, FileName = "removed.epub" }],
            UserState = new() { IsFavorite = true }, UserData = new() { LastFileId = 1 }
        };
        vm.Item = mounted;
        var viewerState = mounted.UserState;
        var progress = mounted.UserData;
        // A metadata-only update must not take over the file inventory.
        Assert.True(await vm.RefreshServerMetadataAsync(mounted.ContentId));
        Assert.Equal(1, Assert.Single(mounted.Versions).FileId);

        var result = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded"));
        var change = Assert.Single(MetadataRefreshFollow.ChangedItems(result, mounted.ContentId, "complete", "ebook", null));
        Assert.True(await vm.RefreshServerMetadataAsync(change.ContentId, change.Structural));
        Assert.Same(mounted, vm.Item);
        Assert.Equal(2, Assert.Single(mounted.Versions).FileId);
        Assert.Same(viewerState, mounted.UserState);
        Assert.Same(progress, mounted.UserData);

        // Removing the final file must also retire Read/download availability.
        fresh.Versions.Clear();
        Assert.True(await vm.RefreshServerMetadataAsync(change.ContentId, change.Structural));
        Assert.Empty(mounted.Versions);
        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var refresh = Read("src", "SiloPlayer", "Views", "ItemDetailPage.ServerRefresh.cs");
        Assert.Contains("ViewModel.RefreshServerMetadataAsync(contentId, structural)", refresh);
        Assert.Contains("UpdateUI();", refresh);
        Assert.Contains("ConfigureBookDetail(item);", page);
        Assert.Contains("_readerTargetFileId = _selectedVersion?.FileId;", page);
        Assert.Contains("BookDownloadButton.Visibility = item.Versions.Count > 0", page);
        Assert.Contains("BuildMediaLocationsSection(canCurateMetadata, item.Versions);", page);
        Assert.DoesNotContain("_readerTargetFileId = progress.FileId;", page);
    }

    [Theory]
    [InlineData("complete", 0)]
    [InlineData("quick", 1)]
    public async Task CardCompletionRebuildsAudiobookChaptersFromFreshFilesWithoutReplacingViewerState(string mode, int newFiles)
    {
        using var scope = new DetailViewModelTestScope();
        var fresh = new MediaItemDetail
        {
            ContentId = "audiobook-1", Type = "audiobook",
            Versions =
            [
                new() { FileId = 2, Duration = 60, Chapters = [new() { Index = 0, Title = "Replacement", StartSeconds = 0, EndSeconds = 60 }] },
                new() { FileId = 3, Duration = 90, Chapters = [new() { Index = 0, Title = "Added", StartSeconds = 0, EndSeconds = 90 }] }
            ]
        };
        var vm = scope.CreateWithCatalog((_, _) => Task.FromResult(fresh));
        var mounted = new MediaItemDetail
        {
            ContentId = fresh.ContentId, Type = fresh.Type,
            Versions = [new() { FileId = 1, Chapters = [new() { Index = 0, Title = "Removed" }] }],
            UserState = new() { IsFavorite = true }, UserData = new() { PositionSeconds = 30, LastFileId = 1 }
        };
        vm.Item = mounted;
        var state = mounted.UserState;
        var progress = mounted.UserData;
        Assert.True(await vm.RefreshServerMetadataAsync(mounted.ContentId));
        Assert.Equal(1, Assert.Single(mounted.Versions).FileId);

        var result = new MaintenanceJobWaitResult(MaintenanceJobWaitOutcome.Succeeded, Job("succeeded", newFiles: newFiles));
        var change = Assert.Single(MetadataRefreshFollow.ChangedItems(result, mounted.ContentId, mode, mounted.Type, null));
        Assert.True(await vm.RefreshServerMetadataAsync(change.ContentId, change.Structural));
        Assert.Same(mounted, vm.Item);
        Assert.Same(state, mounted.UserState);
        Assert.Same(progress, mounted.UserData);
        var rows = SiloPlayer.Core.Helpers.AudiobookDetailPresentation.BuildChapterRows(mounted.Versions);
        Assert.Equal(new[] { 2, 3 }, rows.Select(row => row.FileId));
        Assert.Equal(new[] { "Replacement", "Added" }, rows.Select(row => row.Title));
        Assert.Equal(60, rows[1].AbsoluteStart);
        fresh.Versions.Clear();
        Assert.True(await vm.RefreshServerMetadataAsync(change.ContentId, change.Structural));
        Assert.Empty(SiloPlayer.Core.Helpers.AudiobookDetailPresentation.BuildChapterRows(mounted.Versions));

        var page = Read("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        var layout = Read("src", "SiloPlayer", "Views", "ItemDetailPage.BookLayout.cs");
        Assert.Contains("ConfigureAudiobookDetail(item);", page);
        Assert.Contains("_audiobookChapterRows = AudiobookDetailPresentation.BuildChapterRows(item.Versions);", layout);
        Assert.Contains("if (!ReferenceEquals(_audiobookChapterOwner, item)) _audiobookChaptersExpanded = false;", layout);
        Assert.Contains("_audiobookHasFiles = item.Versions.Count > 0;", layout);
    }

    [Fact]
    public async Task PolledCardCompletionQueuesOwnedCatalogRereadsWithoutASocketEvent()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"));
        handler.Enqueue(HttpStatusCode.OK, JobJson("succeeded"));
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var time = new FakeTime();
        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();
        Assert.Equal(MaintenanceJobWaitOutcome.Succeeded, result.Outcome);
        Assert.False(MetadataRefreshFollow.IsDefiniteFailure(result));
        Assert.Equal(2, handler.Paths.Count);
        // No catalog event is delivered. Guard the WinUI publication/receiver
        // wiring that bridges this HTTP completion to the owned grid rereads.
        var library = Read("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var catalog = Read("src", "SiloPlayer", "Views", "CatalogPage.xaml.cs");
        var catchUp = Read("src", "SiloPlayer", "Views", "LibraryPage.CatchUp.cs");
        var menu = Read("src", "SiloPlayer", "Controls", "MediaItemMenu.cs");
        var libraryReceiver = library[library.IndexOf("public void Receive(MediaSurfaceChanged message)", StringComparison.Ordinal)..];
        libraryReceiver = libraryReceiver[..libraryReceiver.IndexOf("public void Receive(PlaybackProgressUpdated", StringComparison.Ordinal)];
        Assert.Contains("var context = message.Context ?? _stateSettings.CaptureContext();", libraryReceiver);
        Assert.Contains("visit != _libraryVisit || context != _stateSettings.CaptureContext()", libraryReceiver);
        Assert.Contains("if (message.Kind == MediaSurfaceChangeKind.ItemMetadataRefreshed)\n                QueueCatalogContentRefresh(context, libraryId: null);", libraryReceiver);
        Assert.Contains("LibraryViewModel.CatalogRefreshKind.ContentChanged", catchUp);
        Assert.Contains("context: maintenanceApi.CaptureContext()", menu);
        var catalogReceiver = catalog[catalog.IndexOf("public void Receive(MediaSurfaceChanged message)", StringComparison.Ordinal)..];
        catalogReceiver = catalogReceiver[..catalogReceiver.IndexOf("public void Receive(HomeDismissalApplied", StringComparison.Ordinal)];
        Assert.Contains("if (message.Kind == MediaSurfaceChangeKind.ItemMetadataRefreshed)", catalogReceiver);
        Assert.Contains("visit != _navigationCts", catalogReceiver);
        Assert.Contains("!_api.IsCurrentContext(context)", catalogReceiver);
        Assert.Contains("_debounce?.Stop();", catalogReceiver);
        Assert.Contains("await LoadCatalogAsync(true, Math.Max(PageSize, _items.Count), refreshMetadata: true)", catalogReceiver);
        Assert.Contains("if (refreshMetadata) _items[index] = item;", catalog);
        Assert.Contains("RetainedCatalogRead.ReadAsync(ReadWindowAsync, windowSize, PageSize,", catalog);
        Assert.Contains("() => refreshMetadata ? _cardMutationRevision : 0, loadToken)", catalog);
        Assert.Contains("_cardMutationRevision++;", catalogReceiver);
        Assert.True(catalogReceiver.IndexOf("_cardMutationRevision++;", StringComparison.Ordinal)
            < catalogReceiver.IndexOf("if (item == null) return;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CardRefreshEndsQuietlyWhenTheAccountChangesWhileTheJobRuns()
    {
        var handler = new ScriptedHandler();
        handler.Enqueue(HttpStatusCode.OK, JobJson("running"));
        var (_, api) = CreateApi(handler);
        var identity = api.CaptureIdentityContext();
        var current = true;
        var time = new FakeTime { OnWait = () => current = false };

        var result = await new MaintenanceJobFollower(ct => api.GetJobAsync(identity, "job-1", ct),
            () => current && api.IsCurrentIdentityContext(identity), time.Delay, () => time.Now).WaitAsync();

        Assert.Equal(MaintenanceJobWaitOutcome.Stale, result.Outcome);
        Assert.Equal(MetadataRefreshToastKind.None, MetadataRefreshFollow.FinishToast(result, "quick").Kind);
        Assert.Empty(MetadataRefreshFollow.ChangedItems(result, "movie-1", "quick", "movie", null));
        Assert.Empty(MetadataRefreshFollow.InvalidatedIds(result, "movie-1", "movie", null));
    }

    [Fact]
    public void CardMenuFollowsTheJobThroughTheSharedFlowAndNoLongerSaysQueued()
    {
        var menu = Read("src", "SiloPlayer", "Controls", "MediaItemMenu.cs");
        var flow = Read("src", "SiloPlayer", "Services", "MetadataRefreshFlow.cs");
        var detail = Read("src", "SiloPlayer", "Views", "ItemDetailPage.ServerRefresh.cs");
        var english = Read("src", "SiloPlayer.Core", "Localization", "Generated", "en.json");

        Assert.DoesNotContain("queued", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toast.Success(mode", menu, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFlow.StartAsync(maintenanceApi, toast, item.ContentId, mode)", menu, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFlow.FollowAsync(", menu, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFollow.ChangedItems(result, item.ContentId, mode, item.Type, item.SeriesId)", menu, StringComparison.Ordinal);
        Assert.Contains("hub?.NotifyItemChanged(change.ContentId, change.Structural)", menu, StringComparison.Ordinal);
        // A definite failure stops; the surfaces refresh after the job, not when it was queued.
        var finished = menu.IndexOf("MetadataRefreshFollow.IsDefiniteFailure(result)", StringComparison.Ordinal);
        var publication = menu.IndexOf("MediaSurfaceChangeKind.ItemMetadataRefreshed, change.ContentId", StringComparison.Ordinal);
        Assert.InRange(finished, 1, publication);
        Assert.InRange(publication, 1, menu.IndexOf("hub?.NotifyItemChanged(change.ContentId, change.Structural)", StringComparison.Ordinal));

        // The flow owns the bounded, identity-bound follow, the cache drop and every toast.
        Assert.Contains("new MaintenanceJobFollower(", flow, StringComparison.Ordinal);
        Assert.Contains("api.GetJobAsync(identity, receipt.Id, token)", flow, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFollow.InvalidatedIds(result, contentId, type, seriesId)", flow, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFollow.RunningToast(mode)", flow, StringComparison.Ordinal);
        Assert.Contains("MetadataRefreshFollow.FinishToast(result, mode)", flow, StringComparison.Ordinal);
        foreach (var key in new[] { "refreshRunningQuick", "refreshRunningComplete", "refreshed", "completeRefreshFinished",
                     "refreshedNewFiles", "refreshArtworkWarning", "refreshFailed" })
            Assert.Contains($"\"web.item.toast.{key}\":", english);

        // The detail page and the card share the flow instead of each following the job.
        Assert.Contains("Services.MetadataRefreshFlow.StartAsync(api, toast, item.ContentId, mode)", detail, StringComparison.Ordinal);
        Assert.Contains("Services.MetadataRefreshFlow.FollowAsync(", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("new MaintenanceJobFollower(", detail, StringComparison.Ordinal);
    }

    // ===== helpers =====

    private static bool Send(DetailMetadataChangeHub hub, string channel, string eventName, string json)
    {
        using var document = JsonDocument.Parse(json);
        return hub.Receive(channel, eventName, document.RootElement);
    }

    private static MaintenanceJobStatus Job(string state, long newFiles = 0, string? detailId = null, string? refreshId = null) => new()
    {
        Id = "job-1",
        Kind = "item_refresh",
        State = state,
        Terminal = state is "succeeded" or "failed" or "canceled",
        Failure = state == "failed" ? new MaintenanceJobFailure { Title = "Refresh failed", Detail = "provider down" } : null,
        ItemResult = state == "succeeded"
            ? new MaintenanceJobItemResult { NewFiles = newFiles, DetailContentId = detailId, RefreshContentId = refreshId }
            : null,
    };

    private static string JobJson(string state, long newFiles = 0) => state == "succeeded"
        ? $$$"""{"id":"job-1","kind":"item_refresh","state":"succeeded","terminal":true,"library_ids":[],"artifact_size_bytes":0,"public_link_supported":false,"cancelable":false,"created_at":"2026-11-16T00:00:00.000Z","item_result":{"requested_content_id":"movie-1","refresh_content_id":"movie-1","detail_content_id":"movie-1","new_files":{{{newFiles}}},"matched_files":1,"artwork_cache_incomplete":false}}"""
        : $$"""{"id":"job-1","kind":"item_refresh","state":"{{state}}","terminal":false,"library_ids":[],"artifact_size_bytes":0,"public_link_supported":false,"cancelable":true,"created_at":"2026-11-16T00:00:00.000Z"}""";

    private static (SiloApiClient Client, MediaMaintenanceApi Api) CreateApi(ScriptedHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return (client, new MediaMaintenanceApi(client));
    }

    private static string Read(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln")))
            dir = Directory.GetParent(dir)?.FullName ?? "";
        if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException();
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }

    /// <summary>
    /// Holds each coalescing delay until released. Its continuations run inline,
    /// so releasing one runs the watch's pump through to its next wait.
    /// </summary>
    // These tests step the pump synchronously: releasing a ManualDelay gate runs the pump's
    // ConfigureAwait(false) continuation inline. The test runner's SynchronizationContext would
    // queue that continuation instead, so the body runs on a thread-pool thread, which has none.
    private static void OnThreadPool(Action body) => Task.Run(body).GetAwaiter().GetResult();

    private sealed class ManualDelay
    {
        private readonly List<TaskCompletionSource> _gates = [];
        public List<TimeSpan> Requested { get; } = [];
        public int Pending { get { lock (_gates) return _gates.Count(gate => !gate.Task.IsCompleted); } }

        public Task Delay(TimeSpan wait, CancellationToken ct)
        {
            var gate = new TaskCompletionSource();
            lock (_gates)
            {
                _gates.Add(gate);
                Requested.Add(wait);
            }
            ct.Register(() => gate.TrySetCanceled(ct));
            return gate.Task;
        }

        public void ReleaseNext()
        {
            TaskCompletionSource next;
            lock (_gates) next = _gates.First(gate => !gate.Task.IsCompleted);
            next.TrySetResult();
        }
    }

    private sealed class ReloadRecorder
    {
        public int Calls;
        public List<bool> Structural { get; } = [];
        public Func<Task<bool>> Next { get; set; } = () => Task.FromResult(true);

        public Task<bool> Reload(bool structural, CancellationToken ct)
        {
            Calls++;
            Structural.Add(structural);
            return Next();
        }
    }

    private sealed class FakeTime
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Waits { get; } = [];
        public Action? OnWait { get; init; }

        public Task Delay(TimeSpan wait, CancellationToken ct)
        {
            Waits.Add(wait);
            Now += wait;
            OnWait?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public List<string> Paths { get; } = [];

        public void Enqueue(HttpStatusCode status, string body, TimeSpan? retryAfter = null, string mediaType = "application/json")
            => _responses.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
                if (retryAfter is { } delta) response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
                return response;
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
