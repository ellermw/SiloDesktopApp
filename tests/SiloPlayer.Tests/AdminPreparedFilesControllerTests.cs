using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Visit state, live updates and the write rules of the prepared-files page. Synthetic data only; Silo Server 66b836f82 contracts.</summary>
public sealed class AdminPreparedFilesControllerTests
{
    private const string Storage = "{\"locations\":[],\"cache_hours\":72,\"disk_ceiling_percent\":85,\"default_budget_bytes\":0,\"stale_device_days\":14,\"preparing_jobs\":0,\"freed_last_30_days_bytes\":0}";
    private const string Counts = "{\"running\":1,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0}";

    private static string File(string id, string state = "cached") => $$"""{"id":"{{id}}","state":"{{state}}","format":"transcode","location":"server","location_name":"Server","media_file_id":"42","title":"Title {{id}}","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","bytes":10,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0}""";

    private static string Job(string id, string state = "running") => $$"""{"id":"{{id}}","state":"{{state}}","format":"transcode","log_session_id":"log-{{id}}","media_file_id":"42","media_title":"Title {{id}}","media_type":"movie","attempts":1,"max_attempts":3,"created_at":"2026-10-08T09:30:00Z","progress_unavailable":false,"source":{"audio_tracks":[],"duration_seconds":100,"file_size":1000,"hdr":false},"output":{"all_audio_tracks":false,"audio_codec":"aac","container":"mp4","video_codec":"h264"},"requesters":[{"user_id":"7","username":"maya","profile_id":"p1","status":"preparing"}]}""";

    private static string Preparations(params string[] jobs) => "{\"counts\":" + Counts + ",\"items\":[" + string.Join(',', jobs) + "]}";

    private static string Pathname(HttpRequestMessage request) => request.RequestUri!.AbsolutePath;

    /// <summary>Answers reads from the supplied scripts and every write with <paramref name="write"/>.</summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Server(Func<HttpRequestMessage, Task<HttpResponseMessage>> write,
        string files = "{\"items\":[],\"page\":{\"has_more\":false}}", string preparations = "")
    {
        return (request, _) =>
        {
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put) return write(request);
            var path = Pathname(request);
            if (path.EndsWith("/storage", StringComparison.Ordinal)) return Task.FromResult(AdminMonitorKit.Json(Storage));
            if (path.EndsWith("/preparations", StringComparison.Ordinal)) return Task.FromResult(AdminMonitorKit.Json(preparations.Length == 0 ? Preparations() : preparations));
            return Task.FromResult(AdminMonitorKit.Json(files));
        };
    }

    private sealed class Channel : IAdminRealtimeChannel
    {
        public int Leases;
        public string[] Subscribed = [];
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels)
        {
            Subscribed = channels;
            Interlocked.Increment(ref Leases);
            return new Release(() => Interlocked.Decrement(ref Leases));
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        public void Snapshot(string channel) => SnapshotReceived?.Invoke(channel, default);
        public void Raise(string channel, string name, string json = "{}") => EventReceived?.Invoke(channel, name, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    [Fact]
    public async Task ChangedFiltersRetireOldReadsAndRestartWithoutTheirCursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, token) => request.RequestUri!.Query.Contains("q=old") ? slow.Task
            : Server(_ => throw new InvalidOperationException(), "{\"items\":[" + File("new") + "],\"page\":{\"has_more\":false}}")(request, token);
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);

        var old = controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "old" });
        await controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "new" });
        slow.SetResult(AdminMonitorKit.Json("{\"items\":[" + File("old") + "],\"page\":{\"has_more\":false}}"));
        await old;

        Assert.Equal("new", Assert.Single(controller.State.Files.Items).Id);
        Assert.Equal("new", controller.State.FileQuery.Search);
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task ASupersededReadsLateFailureRaisesNoError()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, token) => request.RequestUri!.Query.Contains("q=old") ? slow.Task
            : Server(_ => throw new InvalidOperationException(), "{\"items\":[" + File("new") + "],\"page\":{\"has_more\":false}}")(request, token);
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var old = controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "old" });
        await controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "new" });

        slow.SetException(new IOException("late transport failure"));
        await old;

        Assert.Null(controller.State.Files.Error);
        Assert.Equal("new", Assert.Single(controller.State.Files.Items).Id);
    }

    [Fact]
    public async Task LoadMoreIsBoundedAndDoesNotWalkAWholeServerCollection()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var page = 0;
        kit.Http.Respond = (request, token) =>
        {
            if (Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal))
            {
                page++;
                return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + File(page.ToString()) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"c" + page + "\"}}"));
            }
            return Server(_ => throw new InvalidOperationException())(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        for (var i = 0; i < AdminPreparedFilesController.MaxRetainedPages + 3; i++) await controller.LoadMoreFilesAsync();

        Assert.Equal(AdminPreparedFilesController.MaxRetainedPages, page);
        Assert.Equal(AdminPreparedFilesController.MaxRetainedPages, controller.State.Files.Items.Count);
        Assert.True(controller.State.Files.Truncated);
    }

    [Fact]
    public async Task CursorCycleAcrossSeveralPagesFailsClosed()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var page = 0;
        kit.Http.Respond = (request, token) =>
        {
            if (Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal))
            {
                page++;
                var next = page == 1 ? "c1" : page == 2 ? "c2" : "c1";
                return Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + File(page.ToString()) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"" + next + "\"}}"));
            }
            return Server(_ => throw new InvalidOperationException())(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);

        await controller.OpenAsync("files", null);
        await controller.LoadMoreFilesAsync();
        await controller.LoadMoreFilesAsync();

        Assert.Equal(2, controller.State.Files.Items.Count);
        Assert.Equal(AdminFailureKind.InvalidResponse, controller.State.Files.ErrorKind);
        Assert.NotNull(controller.State.Files.Error);
    }

    [Fact]
    public async Task AFailedBackgroundRefreshKeepsTheRowsAndTheNextReadRecovers()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var fail = false;
        kit.Http.Respond = (request, token) => fail && Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal)
            ? throw new IOException("offline")
            : Server(_ => throw new InvalidOperationException(), "{\"items\":[" + File("one") + "],\"page\":{\"has_more\":false}}")(request, token);
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);

        fail = true;
        await controller.ReloadFilesAsync();
        Assert.NotNull(controller.State.Files.Error);
        Assert.Equal("one", Assert.Single(controller.State.Files.Items).Id);

        fail = false;
        await controller.ReloadFilesAsync();
        Assert.Null(controller.State.Files.Error);
    }

    [Fact]
    public async Task AnUncertainDeleteIsSentOnceBlocksDestructiveActionsAndIsLiftedByReadingAgain()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var writes = 0;
        var failReads = true;
        kit.Http.Respond = (request, token) =>
        {
            if (failReads && request.Method == HttpMethod.Get && Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal) && writes > 0)
                throw new IOException("still down");
            return Server(_ =>
            {
                writes++;
                throw new IOException("connection closed after dispatch");
            }, "{\"items\":[" + File("one") + "],\"page\":{\"has_more\":false}}")(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var row = controller.State.Files.Items[0];

        await controller.DeleteFilesAsync([row], includeInUse: false);

        Assert.Equal(1, writes);
        Assert.Equal(AdminActionOutcome.Unknown, controller.State.LastAction?.Outcome);
        Assert.True(controller.State.DestructiveBlocked);
        await controller.DeleteFilesAsync([row], includeInUse: false);
        Assert.Equal(1, writes);

        failReads = false;
        await controller.ReloadBlockedAsync();
        Assert.False(controller.State.DestructiveBlocked);
    }

    [Fact]
    public async Task ARefusedDeleteShowsTheServersProblemAndDoesNotBlockARetry()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var writes = 0;
        kit.Http.Respond = Server(_ =>
        {
            writes++;
            return Task.FromResult(AdminMonitorKit.Json("{\"type\":\"https://silo.test/problems/forbidden\",\"title\":\"Forbidden\",\"status\":403,\"detail\":\"Synthetic refusal detail.\"}", HttpStatusCode.Forbidden));
        }, "{\"items\":[" + File("one") + "],\"page\":{\"has_more\":false}}");
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);

        await controller.DeleteFilesAsync([controller.State.Files.Items[0]], includeInUse: false);

        Assert.Equal(AdminActionOutcome.Failed, controller.State.LastAction?.Outcome);
        Assert.False(controller.State.DestructiveBlocked);
        Assert.Contains("Synthetic refusal detail.", controller.State.LastAction?.Message);
        await controller.DeleteFilesAsync([controller.State.Files.Items[0]], includeInUse: false);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task DeleteResultsAreReportedAsTheOfficialToastWouldAndDeleteOnlyTheConfirmedRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var body = "";
        kit.Http.Respond = Server(request =>
        {
            body = kit.Http.Bodies.Last();
            return Task.FromResult(AdminMonitorKit.Json("{\"results\":[{\"id\":\"a\",\"outcome\":\"deleted\",\"bytes\":10},{\"id\":\"b\",\"outcome\":\"in_use\",\"bytes\":0}]}"));
        }, "{\"items\":[" + File("a") + "," + File("b", "in_use") + "," + File("c") + "],\"page\":{\"has_more\":false}}");
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var rows = controller.State.Files.Items;

        await controller.DeleteFilesAsync([rows[0], rows[1]], includeInUse: false);

        Assert.Equal("{\"ids\":[\"a\",\"b\"],\"include_in_use\":false}", body);
        Assert.Equal(AdminActionOutcome.Partial, controller.State.LastAction?.Outcome);
        Assert.Equal("Deleted 1 prepared file (10 B); 1 skipped", controller.State.LastAction?.Message);
    }

    [Fact]
    public async Task CancelActsOnExactlyTheConfirmedJobsOnceAndASuccessReadsTheQueueAgain()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var cancels = 0;
        string? body = null;
        kit.Http.Respond = Server(request =>
        {
            cancels++;
            body = kit.Http.Bodies.Last();
            return Task.FromResult(AdminMonitorKit.Json("{\"results\":[{\"id\":\"j1\",\"outcome\":\"applied\"}]}"));
        }, preparations: Preparations(Job("j1"), Job("j2")));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("preparation", null);
        var reads = kit.Http.Requests.Count(r => r.EndsWith("/preparations?limit=200", StringComparison.Ordinal));

        await controller.RunPreparationActionAsync("cancel", [controller.State.Preparations[0]]);

        Assert.Equal(1, cancels);
        Assert.Equal("{\"ids\":[\"j1\"]}", body);
        Assert.Equal(AdminActionOutcome.Applied, controller.State.LastAction?.Outcome);
        Assert.True(kit.Http.Requests.Count(r => r.EndsWith("/preparations?limit=200", StringComparison.Ordinal)) > reads);
    }

    [Fact]
    public async Task AnUnconfirmedUntrackedListingFailureIsAnAnswerNotAnUnknownOutcome()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"https://silo.test/problems/dependency_unavailable\",\"title\":\"Unavailable\",\"status\":503}", HttpStatusCode.ServiceUnavailable)));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("storage", null);

        await controller.DeleteUntrackedAsync(new AdminDownloadStorageLocation { Key = "node:9", Name = "gpu-1" });

        Assert.Equal(AdminActionOutcome.Failed, controller.State.LastAction?.Outcome);
        Assert.Equal("Couldn't list gpu-1's directory. Try again later.", controller.State.LastAction?.Message);
        Assert.False(controller.State.DestructiveBlocked);
    }

    [Fact]
    public async Task AuthorityRetirementClearsEveryTabAndDiscardsLateResults()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        var open = controller.OpenAsync("storage", null);
        kit.SetRole("user");
        pending.SetResult(AdminMonitorKit.Json(Storage));
        await open;

        Assert.Null(controller.State.Owner);
        Assert.Null(controller.State.Storage.Value);
        Assert.Empty(controller.State.Files.Items);
        Assert.Empty(controller.State.Devices.Items);
        Assert.Empty(controller.State.History.Items);
        Assert.Empty(controller.State.Preparations);
        Assert.Throws<MonitorAuthorityRetiredException>(() => controller.CaptureAction());
    }

    // ---- Live updates -----------------------------------------------------------------------------

    [Fact]
    public async Task TheVisitLeasesBothOfficialChannelsAndReleasesThemWhenItEnds()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException());
        var channel = new Channel();
        var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, channel, new BackupScheduler());

        await controller.OpenAsync("storage", null);
        Assert.Equal(["download_preparations", "downloads"], channel.Subscribed);
        Assert.Equal(1, channel.Leases);

        controller.Dispose();
        Assert.Equal(0, channel.Leases);
    }

    [Fact]
    public async Task EventsAndSnapshotsRefreshTheRightViewsAndProgressPatchesRunningRowsInPlace()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), "{\"items\":[" + File("a") + "],\"page\":{\"has_more\":false}}", Preparations(Job("j1"), Job("j2", "queued")));
        var channel = new Channel();
        var scheduler = new BackupScheduler();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, channel, scheduler);
        await controller.OpenAsync("files", null);
        int Count(string suffix) => kit.Http.Requests.Count(r => r.Contains(suffix, StringComparison.Ordinal));
        var preparationReads = Count("/preparations?");
        var fileReads = Count("/storage/files?");

        channel.Raise("download_preparations", "download_preparation.progress", "{\"id\":\"j1\",\"progress\":{\"encoded_seconds\":50,\"duration_seconds\":100,\"speed\":2,\"updated_at\":\"2026-10-08T09:31:00Z\"}}");
        Assert.Equal(50, controller.State.Preparations.Single(x => x.Id == "j1").Progress!.EncodedSeconds);
        Assert.Equal(preparationReads, Count("/preparations?"));

        // A job that is not running (or not listed) cannot be patched, so the list is read again.
        channel.Raise("download_preparations", "download_preparation.progress", "{\"id\":\"j2\",\"progress\":{\"encoded_seconds\":1,\"duration_seconds\":100,\"speed\":1,\"updated_at\":\"2026-10-08T09:31:00Z\"}}");
        await Eventually.True(() => Count("/preparations?") > preparationReads, "a progress event for a queued job re-reads the queue");

        scheduler.Advance(TimeSpan.FromSeconds(3));
        channel.Raise("download_preparations", "download_storage.changed");
        await Eventually.True(() => Count("/storage/files?") > fileReads, "a storage event re-reads the active list");
        scheduler.Advance(TimeSpan.FromSeconds(3));
        var again = Count("/storage/files?");
        channel.Raise("downloads", "download.changed");
        await Eventually.True(() => Count("/storage/files?") > again, "a downloads event re-reads the storage views");
    }

    [Fact]
    public async Task ABurstOfEventsCostsAtMostOneLeadingAndOneTrailingRead()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), preparations: Preparations(Job("j1")));
        var channel = new Channel();
        var scheduler = new BackupScheduler();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, channel, scheduler);
        await controller.OpenAsync("preparation", null);
        int Reads() => kit.Http.Requests.Count(r => r.Contains("/preparations?", StringComparison.Ordinal));
        var before = Reads();

        for (var i = 0; i < 20; i++) channel.Raise("download_preparations", "download_preparation.queued");
        await Eventually.True(() => Reads() == before + 1, "the leading read");
        scheduler.Advance(AdminPreparedFilesController.EventCoalescing);
        await Eventually.True(() => Reads() == before + 2, "the trailing read");
        await Task.Delay(50);
        Assert.Equal(before + 2, Reads());
    }

    [Fact]
    public async Task PollsRunOnTheOfficialCadencesAndStopWhenTheVisitEnds()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), preparations: Preparations(Job("j1")));
        var scheduler = new BackupScheduler();
        var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, new Channel(), scheduler);
        await controller.OpenAsync("storage", null);
        int Reads(string suffix) => kit.Http.Requests.Count(r => r.Contains(suffix, StringComparison.Ordinal));
        var storage = Reads("/downloads/storage");
        var preparations = Reads("/preparations?");

        Assert.Equal(2, scheduler.Pending.Count(x => x == AdminPreparedFilesController.StoragePoll));
        scheduler.Advance(TimeSpan.FromSeconds(60));
        await Eventually.True(() => Reads("/downloads/storage") > storage && Reads("/preparations?") > preparations, "both polls ran");

        controller.Dispose();
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task NoPreparationPollRunsWhileNothingIsInFlightButTheFailureExpiryTimerIsArmed()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var failed = Job("f1", "failed").Replace("\"attempts\":1", "\"attempts\":3,\"failed_at\":\"2026-11-16T10:00:00Z\"");
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), preparations: "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":1},\"items\":[" + failed + "]}");
        var scheduler = new BackupScheduler();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, new Channel(), scheduler);
        await controller.OpenAsync("preparation", null);

        // The 60 s timer left is the storage poll; the preparation maintenance poll runs only while jobs are in flight.
        Assert.Single(scheduler.Pending, x => x == AdminPreparedFilesController.PreparationMaintenance);
        // failed_at + 24 h + 1 s, measured from the scheduler's clock (2026-11-16T12:00:00Z): 22 h 0 m 1 s.
        Assert.Contains(TimeSpan.FromHours(22) + TimeSpan.FromSeconds(1), scheduler.Pending);
    }

    [Fact]
    public async Task AFailureTimestampFarInTheFutureNeverBreaksTheRealSchedulerAndFallsBackToTheMaintenanceCadence()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var failed = Job("f1", "failed").Replace("\"attempts\":1", "\"attempts\":3,\"failed_at\":\"2099-10-08T09:30:00Z\"");
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), preparations: "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":1},\"items\":[" + failed + "]}");
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, null, new SystemAdminScheduler());

        await controller.OpenAsync("preparation", null);

        Assert.True(controller.State.PreparationsLoaded);
        Assert.Null(controller.State.PreparationsError);
    }

    // ---- Review findings of November 20 ------------------------------------------------------------

    private static string DeviceJson(string deviceId) => "{\"user_id\":\"7\",\"username\":\"maya\",\"profile_id\":\"p1\",\"profile_name\":\"Maya\",\"device_id\":\"" + deviceId + "\",\"device_name\":\"" + deviceId + "\",\"platform\":\"android\",\"stale\":false,\"copies\":1,\"finished\":1,\"waiting\":0,\"failed\":0,\"revoked\":0,\"bytes_on_device\":10,\"monitors\":0}";

    private static string EntryJson(string id, string deviceId) => "{\"id\":\"" + id + "\",\"profile_id\":\"p1\",\"device_id\":\"" + deviceId + "\",\"content_id\":\"movie:dune\",\"title\":\"Dune\",\"media_type\":\"movie\",\"status\":\"completed\",\"quality\":\"original\",\"effective_quality\":\"original\",\"delivery_format\":\"remux\",\"target_bitrate_kbps\":0,\"file_size\":10,\"created_at\":\"2026-10-08T09:30:00Z\",\"updated_at\":\"2026-10-08T09:30:00Z\",\"user_id\":\"7\",\"username\":\"maya\",\"device_name\":\"" + deviceId + "\"}";

    private static string Page(string items, string? next = null) => "{\"items\":[" + items + "],\"page\":{\"has_more\":" + (next != null ? "true" : "false") + (next != null ? ",\"next_cursor\":\"" + next + "\"" : "") + "}}";

    [Fact]
    public async Task LeavingAFilteredTabResetsItsQueryWithoutWaitingOnOrStartingAReadOfIt()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var stall = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallFiles = false;
        kit.Http.Respond = (request, token) => stallFiles && Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal)
            ? stall.Task : Server(_ => throw new InvalidOperationException(), Page(File("one")))(request, token);
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        await controller.SetFileQueryAsync(controller.State.FileQuery with { Search = "dune" });
        int Reads(string suffix) => kit.Http.Requests.Count(r => r.Contains(suffix, StringComparison.Ordinal));
        var fileReads = Reads("/storage/files?");
        stallFiles = true;

        // Even with the file list unreadable, leaving it neither starts a read of it nor waits for one.
        await controller.ActivateTabAsync("history", new(), new(), new()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("history", controller.State.ActiveTab);
        Assert.Equal(new AdminPreparedFileQuery(), controller.State.FileQuery);
        Assert.Empty(controller.State.Files.Items);
        Assert.False(controller.State.Files.Loaded);
        Assert.Equal(fileReads, Reads("/storage/files?"));
        Assert.True(controller.State.History.Loaded);
        Assert.Equal(1, Reads("/storage/events?"));
    }

    [Fact]
    public async Task ActivatingATabWithAQueryReadsOnlyThatTabOnce()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), Page(File("one")));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("storage", null);
        int Reads(string suffix) => kit.Http.Requests.Count(r => r.Contains(suffix, StringComparison.Ordinal));

        await controller.ActivateTabAsync("files", new AdminPreparedFileQuery { Location = "server" }, new(), new());

        Assert.Equal("server", controller.State.FileQuery.Location);
        Assert.Equal(1, Reads("/storage/files?"));
        Assert.Equal(0, Reads("/downloads/devices"));
        Assert.Equal(0, Reads("/storage/events?"));
        Assert.Equal("one", Assert.Single(controller.State.Files.Items).Id);
    }

    [Fact]
    public async Task AProgressReadingRaisesChangedOnlyWhileThePreparationTabIsShown()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), Page(File("a")), Preparations(Job("j1")));
        var channel = new Channel();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, channel, new BackupScheduler());
        await controller.OpenAsync("files", null);
        var raised = 0;
        controller.Changed += () => Interlocked.Increment(ref raised);
        string Progress(int seconds) => "{\"id\":\"j1\",\"progress\":{\"encoded_seconds\":" + seconds + ",\"duration_seconds\":100,\"speed\":2,\"updated_at\":\"2026-10-08T09:31:00Z\"}}";

        channel.Raise("download_preparations", "download_preparation.progress", Progress(10));
        Assert.Equal(0, raised);
        Assert.Equal(10, controller.State.Preparations.Single().Progress!.EncodedSeconds);

        await controller.ActivateTabAsync("preparation", new(), new(), new());
        raised = 0;
        channel.Raise("download_preparations", "download_preparation.progress", Progress(20));
        Assert.Equal(1, raised);
        Assert.Equal(20, controller.State.Preparations.Single().Progress!.EncodedSeconds);
    }

    [Fact]
    public async Task EachDevicesDownloadsHaveTheirOwnReadSoOneDeviceNeverCancelsAnother()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var slowPhone = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, token) =>
        {
            var path = Pathname(request);
            if (path.EndsWith("/devices", StringComparison.Ordinal)) return Task.FromResult(AdminMonitorKit.Json(Page(DeviceJson("phone") + "," + DeviceJson("tablet"))));
            if (path.EndsWith("/entries", StringComparison.Ordinal))
                return request.RequestUri!.Query.Contains("device_id=phone", StringComparison.Ordinal) ? slowPhone.Task
                    : Task.FromResult(AdminMonitorKit.Json(Page(EntryJson("dl-tablet", "tablet"))));
            return Server(_ => throw new InvalidOperationException())(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("devices", null);
        var phone = controller.State.Devices.Items.Single(x => x.DeviceId == "phone");
        var tablet = controller.State.Devices.Items.Single(x => x.DeviceId == "tablet");

        // Expand the phone (its read is slow), then the tablet, as a downloads event would reload both at once.
        var phoneRead = controller.LoadEntriesAsync(phone);
        await controller.LoadEntriesAsync(tablet);
        slowPhone.SetResult(AdminMonitorKit.Json(Page(EntryJson("dl-phone", "phone"))));
        await phoneRead;

        var phoneEntries = controller.State.Entries[AdminPreparedFilesController.DeviceKey(phone)];
        var tabletEntries = controller.State.Entries[AdminPreparedFilesController.DeviceKey(tablet)];
        Assert.False(phoneEntries.Loading);
        Assert.Equal("dl-phone", Assert.Single(phoneEntries.Items).Id);
        Assert.False(tabletEntries.Loading);
        Assert.Equal("dl-tablet", Assert.Single(tabletEntries.Items).Id);
    }

    [Fact]
    public async Task ALiveRefreshReReadsEveryLoadedPageAndRowsThatAreGoneDropOut()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var secondPage = "b";
        kit.Http.Respond = (request, token) =>
        {
            if (!Pathname(request).EndsWith("/storage/files", StringComparison.Ordinal)) return Server(_ => throw new InvalidOperationException())(request, token);
            var query = request.RequestUri!.Query;
            return Task.FromResult(AdminMonitorKit.Json(query.Contains("cursor=c2", StringComparison.Ordinal) ? Page(File("c"))
                : query.Contains("cursor=c1", StringComparison.Ordinal) ? Page(File(secondPage), "c2") : Page(File("a"), "c1")));
        };
        var channel = new Channel();
        var scheduler = new BackupScheduler();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, channel, scheduler);
        await controller.OpenAsync("files", null);
        await controller.LoadMoreFilesAsync();
        await controller.LoadMoreFilesAsync();
        Assert.Equal(["a", "b", "c"], controller.State.Files.Items.Select(x => x.Id));
        int Reads(string cursor) => kit.Http.Requests.Count(r => r.Contains("/storage/files?", StringComparison.Ordinal) && r.Contains(cursor, StringComparison.Ordinal));
        var thirdPageReads = Reads("cursor=c2");

        secondPage = "b2"; // "b" is gone on the server
        channel.Raise("downloads", "download.changed");
        await Eventually.True(() => Reads("cursor=c2") > thirdPageReads && !controller.State.Files.Loading, "the refresh walked all three pages");

        Assert.Equal(["a", "b2", "c"], controller.State.Files.Items.Select(x => x.Id));
        Assert.Equal(3, controller.State.Files.PageCount);
        Assert.Null(controller.State.Files.NextCursor);
        await controller.ReloadFilesAsync();
        Assert.Equal(3, controller.State.Files.Items.Count);
    }

    [Fact]
    public async Task ARereadOfADevicesDownloadsKeepsItsLoadedPages()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, token) =>
        {
            var path = Pathname(request);
            if (path.EndsWith("/devices", StringComparison.Ordinal)) return Task.FromResult(AdminMonitorKit.Json(Page(DeviceJson("phone"))));
            if (path.EndsWith("/entries", StringComparison.Ordinal))
                return Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.Query.Contains("cursor=e1", StringComparison.Ordinal) ? Page(EntryJson("two", "phone")) : Page(EntryJson("one", "phone"), "e1")));
            return Server(_ => throw new InvalidOperationException())(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("devices", null);
        var device = controller.State.Devices.Items.Single();
        await controller.LoadEntriesAsync(device);
        await controller.LoadEntriesAsync(device, append: true);
        Assert.Equal(["one", "two"], controller.State.Entries[AdminPreparedFilesController.DeviceKey(device)].Items.Select(x => x.Id));

        await controller.ReloadDevicesAsync();
        await controller.LoadEntriesAsync(device);

        Assert.Equal(["one", "two"], controller.State.Entries[AdminPreparedFilesController.DeviceKey(device)].Items.Select(x => x.Id));
    }

    [Fact]
    public async Task AFailureAmongDeletedRowsIsAnErrorNoticeWhileASkippedRowIsNot()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var reply = "";
        kit.Http.Respond = Server(_ => Task.FromResult(AdminMonitorKit.Json(reply)), Page(File("a") + "," + File("b") + "," + File("c")));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var rows = controller.State.Files.Items;

        reply = "{\"results\":[{\"id\":\"a\",\"outcome\":\"deleted\",\"bytes\":10},{\"id\":\"b\",\"outcome\":\"failed\",\"bytes\":0}]}";
        await controller.DeleteFilesAsync([rows[0], rows[1]], includeInUse: false);
        Assert.Equal(AdminActionOutcome.Partial, controller.State.LastAction!.Outcome);
        Assert.True(controller.State.LastAction.IsError);
        Assert.Contains("could not be deleted", controller.State.LastAction.Message);

        reply = "{\"results\":[{\"id\":\"b\",\"outcome\":\"failed\",\"bytes\":0}]}";
        await controller.DeleteFilesAsync([rows[1]], includeInUse: false);
        Assert.Equal(AdminActionOutcome.Failed, controller.State.LastAction!.Outcome);
        Assert.True(controller.State.LastAction.IsError);

        reply = "{\"results\":[{\"id\":\"a\",\"outcome\":\"deleted\",\"bytes\":10},{\"id\":\"c\",\"outcome\":\"in_use\",\"bytes\":0}]}";
        await controller.DeleteFilesAsync([rows[0], rows[2]], includeInUse: false);
        Assert.Equal(AdminActionOutcome.Partial, controller.State.LastAction!.Outcome);
        Assert.False(controller.State.LastAction.IsError);

        reply = "{\"files\":2,\"bytes\":3000,\"failed_files\":1}";
        await controller.DeleteUntrackedAsync(new AdminDownloadStorageLocation { Key = "node:9", Name = "gpu-1" });
        Assert.Equal(AdminActionOutcome.Partial, controller.State.LastAction!.Outcome);
        Assert.True(controller.State.LastAction.IsError);
    }

    [Theory]
    [InlineData("9999-12-31T22:00:00Z")]
    [InlineData("9999-12-31T23:59:59Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("2099-10-08T09:30:00Z")]
    public async Task NoFailureTimestampCanBreakOpeningThePageOrItsTimers(string failedAt)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var failed = Job("f1", "failed").Replace("\"attempts\":1", "\"attempts\":3,\"failed_at\":\"" + failedAt + "\"");
        kit.Http.Respond = Server(_ => throw new InvalidOperationException(), preparations: "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":1},\"items\":[" + failed + "]}");
        var scheduler = new BackupScheduler();
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority, new Channel(), scheduler);

        await controller.OpenAsync("preparation", null);

        Assert.True(controller.State.PreparationsLoaded);
        Assert.Null(controller.State.PreparationsError);
        // Nothing is scheduled below the maintenance cadence: no delay a clock could turn into a tight loop.
        Assert.All(scheduler.Pending, delay => Assert.True(delay >= TimeSpan.FromSeconds(1)));
    }

    private const string Nodes = "{\"items\":[{\"id\":\"9\",\"name\":\"gpu-1\",\"type\":\"transcode\",\"url\":\"https://node\",\"enabled\":true,\"healthy\":true,\"active_jobs\":0,\"egress_kbps\":0,\"created_at\":\"2026-01-01T00:00:00Z\",\"config_etag\":\"\\\"v1\\\"\"}],\"page\":{\"has_more\":false}}";

    [Fact]
    public async Task ASaveAbortedAfterItWasSentIsAnUnknownOutcomeThatBlocksWritesUntilTheNodesAreReadAgain()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deletes = 0;
        kit.Http.Respond = async (request, token) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                sent.TrySetResult();
                await Task.Delay(Timeout.Infinite, token); // the editor is dismissed: the request is aborted
            }
            if (request.Method == HttpMethod.Get && Pathname(request).EndsWith("/api/v2/admin/nodes", StringComparison.Ordinal)) return AdminMonitorKit.Json(Nodes);
            if (request.Method == HttpMethod.Post) { deletes++; return AdminMonitorKit.Json("{\"results\":[{\"id\":\"a\",\"outcome\":\"deleted\",\"bytes\":1}]}"); }
            return await Server(_ => throw new InvalidOperationException(), Page(File("a")))(request, token);
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var node = new AdminPreparedFilesNode { Id = "9", ConfigETag = "\"v1\"" };

        AdminNodeSaveResult result;
        using (var editor = controller.CaptureAction())
        {
            var saving = controller.SaveNodeLocationAsync(editor, node, new AdminNodeLocationChange(true, "/prepared", false, null));
            await sent.Task;
            editor.Dispose(); // dismissing the editor ends its lease while the save is in flight
            result = await saving.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(AdminActionOutcome.Unknown, result.Outcome);
        Assert.False(controller.State.ActionPending);
        Assert.True(controller.State.DestructiveBlocked);
        Assert.Equal(AdminActionOutcome.Unknown, controller.State.LastAction?.Outcome);

        // Writes stay off until the nodes are read again, and reading them again lifts the block: the page is never stuck.
        await controller.DeleteFilesAsync([controller.State.Files.Items[0]], includeInUse: false);
        Assert.Equal(0, deletes);
        await controller.ReloadBlockedAsync();
        Assert.False(controller.State.DestructiveBlocked);
        await controller.DeleteFilesAsync([controller.State.Files.Items[0]], includeInUse: false);
        Assert.Equal(1, deletes);
    }

    [Fact]
    public async Task ASaveDismissedBeforeItsAnswerNeverLeavesThePageBusy()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, token) =>
        {
            if (request.Method != HttpMethod.Put) return Server(_ => throw new InvalidOperationException(), Page(File("a")))(request, token);
            sent.TrySetResult();
            return answer.Task;
        };
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var node = new AdminPreparedFilesNode { Id = "9", ConfigETag = "\"v1\"" };
        using var editor = controller.CaptureAction();
        var saving = controller.SaveNodeLocationAsync(editor, node, new AdminNodeLocationChange(true, "/prepared", false, null));
        await sent.Task;

        editor.Dispose();
        var reply = AdminMonitorKit.Json("{\"id\":\"9\",\"name\":\"gpu-1\",\"type\":\"transcode\",\"url\":\"https://node\",\"enabled\":true,\"healthy\":true,\"active_jobs\":0,\"egress_kbps\":0,\"created_at\":\"2026-01-01T00:00:00Z\",\"config_etag\":\"\\\"v2\\\"\"}");
        reply.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v2\"");
        answer.SetResult(reply);
        await saving.WaitAsync(TimeSpan.FromSeconds(5));

        // Either the answer was applied and reported, or the abort was an unknown outcome; the page is never left busy.
        Assert.False(controller.State.ActionPending);
        Assert.NotNull(controller.State.LastAction);
    }

    [Fact]
    public async Task EveryConfirmationSendsNothingUnderAnAuthorityThatIsNoLongerCurrent()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var writes = 0;
        kit.Http.Respond = Server(request =>
        {
            writes++;
            return Task.FromResult(AdminMonitorKit.Json(Pathname(request).EndsWith("/revoke", StringComparison.Ordinal)
                ? "{\"revoked\":1,\"bytes\":2,\"paused_monitors\":0,\"download_ids\":[\"d1\"]}" : "{\"results\":[{\"id\":\"one\",\"outcome\":\"deleted\",\"bytes\":1}]}"));
        }, Page(File("one")), Preparations(Job("one")));
        using var controller = new AdminPreparedFilesController(new AdminPreparedFilesApi(kit.Client), authority);
        await controller.OpenAsync("files", null);
        var row = controller.State.Files.Items[0];
        var job = controller.State.Preparations[0];
        var location = new AdminDownloadStorageLocation { Key = "node:9", Name = "gpu-1" };

        // The dialog opened under this lease; the page is then reopened (or the account changes) before the administrator confirms.
        using var stale = controller.CaptureAction();
        await controller.OpenAsync("files", null);

        await controller.DeleteFilesAsync([row], includeInUse: false, stale);
        await controller.RunPreparationActionAsync("cancel", [job], stale);
        await controller.RevokeAsync(AdminDownloadRevokeTarget.ForIds(["d1"]), "", "Pixel", stale);
        await controller.DeleteUntrackedAsync(location, stale);
        await controller.CleanupLocationAsync(location, stale);

        Assert.Equal(0, writes);
        Assert.False(controller.State.ActionPending);
        Assert.Null(controller.State.LastAction);

        using var current = controller.CaptureAction();
        await controller.DeleteFilesAsync([row], includeInUse: false, current);
        Assert.Equal(1, writes);

        kit.SetRole("user");
        await controller.DeleteFilesAsync([row], includeInUse: false, current);
        Assert.Equal(1, writes);
    }
}
