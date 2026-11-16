using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminLibrariesControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SteadyEventsPublishSlowReadsAndCoalesceOneTrailingRead(bool jobs)
    {
        var (kit, authority, controller, server, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            var responses = Enumerable.Range(0, 3)
                .Select(_ => new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var reads = 0;
            var cancelled = 0;
            kit.Http.Respond = async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath != (jobs ? "/api/v2/admin/jobs" : "/api/v2/libraries"))
                    return server.Respond(request);
                var index = Interlocked.Increment(ref reads) - 1;
                try { return await responses[index].Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
            };
            void Burst()
            {
                // Each response remains pending through three separate 1 s coalescing windows.
                for (var i = 0; i < 3; i++)
                {
                    if (jobs) channel.Event("jobs", "job.updated", "{\"kind\":\"library_refresh\"}");
                    else channel.Event("scans", "scan.completed", "{\"id\":\"s1\",\"library_id\":1,\"mode\":\"file\",\"status\":\"completed\"}");
                    scheduler.Advance(AdminLibrariesController.EventCoalescing);
                }
            }
            HttpResponseMessage Response(int number) => AdminMonitorKit.Json(jobs
                ? "{\"items\":[{\"id\":\"j" + number + "\",\"kind\":\"library_refresh\",\"state\":\"running\",\"library_id\":\"1\"}],\"page\":{\"has_more\":false}}"
                : AdminLibrariesTests.Libraries(AdminLibrariesTests.Library("1", "Read " + number)));
            bool Published(int number) => jobs
                ? controller.State.RefreshJobs.SingleOrDefault()?.Id == "j" + number
                : controller.State.Loaded && controller.State.Libraries.SingleOrDefault()?.Name == "Read " + number;

            var opening = controller.OpenAsync();
            await Until(() => Volatile.Read(ref reads) == 1);
            Burst();
            Assert.Equal(1, Volatile.Read(ref reads));
            Assert.Equal(0, Volatile.Read(ref cancelled));

            responses[0].SetResult(Response(1));
            await opening;
            await Until(() => Volatile.Read(ref reads) == 2);
            Assert.True(Published(1)); // Loading ends before the event stream does.
            Burst();
            Assert.Equal(2, Volatile.Read(ref reads));
            Assert.Equal(0, Volatile.Read(ref cancelled));

            responses[1].SetResult(Response(2));
            await Until(() => Published(2) && Volatile.Read(ref reads) == 3);
            responses[2].SetResult(Response(3));
            await Until(() => Published(3) && controller.State.CanReorder);
            scheduler.Advance(AdminLibrariesController.EventCoalescing);
            Assert.Equal(3, Volatile.Read(ref reads));
            Assert.False(controller.State.Fetching);
            Assert.False(controller.State.RefreshJobsLoadingMore);
            Assert.Equal(0, Volatile.Read(ref cancelled));
        }
    }

    [Fact]
    public async Task JobInvalidationsWaitForAnOlderPageThenRefreshTheFirstPage()
    {
        var (kit, authority, controller, server, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            var older = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restarted = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            kit.Http.Respond = (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/admin/jobs") return Task.FromResult(server.Respond(request));
                var number = Interlocked.Increment(ref reads);
                if (number == 1) return Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"first\"}],\"page\":{\"has_more\":true,\"next_cursor\":\"older\"}}"));
                Assert.Equal(number == 2, request.RequestUri.Query.Contains("cursor=older", StringComparison.Ordinal));
                return (number == 2 ? older : restarted).Task.WaitAsync(ct);
            };
            await controller.OpenAsync();
            var loading = controller.LoadOlderJobsAsync();
            await Until(() => Volatile.Read(ref reads) == 2);
            for (var i = 0; i < 3; i++)
            {
                channel.Event("jobs", "job.updated", "{\"kind\":\"library_refresh\"}");
                scheduler.Advance(AdminLibrariesController.EventCoalescing);
            }
            Assert.Equal(2, Volatile.Read(ref reads));
            older.SetResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"older\"}],\"page\":{\"has_more\":false}}"));
            await loading;
            await Until(() => Volatile.Read(ref reads) == 3);
            Assert.Equal(["first", "older"], controller.State.RefreshJobs.Select(job => job.Id));
            restarted.SetResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"fresh\"}],\"page\":{\"has_more\":false}}"));
            await Until(() => controller.State.RefreshJobs is [{ Id: "fresh" }]);
            Assert.False(controller.State.RefreshJobsLoadingMore);
            Assert.False(controller.State.RefreshJobsHasMore);
        }
    }

    [Theory]
    [InlineData("add", false)]
    [InlineData("subscribe", false)]
    [InlineData("remove", false)]
    [InlineData("release", false)]
    [InlineData("remove", true)]
    [InlineData("release", true)]
    public async Task ChannelOperationsDoNotHoldTheControllerGateDuringAccessRetirement(string operation, bool dispose)
    {
        using var kit = AdminMonitorKit.Create();
        var server = new Server();
        kit.Http.Respond = (request, _) => Task.Run(() => server.Respond(request));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var channel = new RetirementLockChannel();
        using var controller = new AdminLibrariesController(new AdminLibrariesApi(kit.Client), authority, channel, new Scheduler());
        if (operation is "remove" or "release") await controller.OpenAsync();

        channel.ProbeOperation = operation;
        channel.RetireUnderLock = () =>
        {
            // Like the transport's access_changed path: a channel lock is held
            // while invalidation synchronously calls back into the controller.
            // Reading State also checks the gate when Dispose has detached authority.
            _ = controller.State;
            kit.Client.InvalidateAccessContext();
        };
        try
        {
            if (dispose) controller.Dispose();
            else await controller.OpenAsync();
        }
        finally
        {
            if (channel.Retirement is { } retirement) await retirement.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.NotNull(channel.Retirement);
        Assert.Null(controller.State.Owner);
        Assert.False(channel.HasListeners);
        Assert.Equal(channel.Subscriptions, channel.Released);
        Assert.Throws<MonitorAuthorityRetiredException>(() => controller.CaptureAction());
        if (!dispose)
        {
            // A subscription returned after retirement was released, not retained.
            await controller.OpenAsync();
            Assert.NotNull(controller.State.Owner);
            using var action = controller.CaptureAction(controller.State.Owner);
            Assert.True(controller.IsActionCurrent(action));
            Assert.Equal(channel.Released + 1, channel.Subscriptions);
        }
    }

    private sealed class RetirementLockChannel : IAdminRealtimeChannel
    {
        private readonly object _lock = new();
        private Action<string, JsonElement>? _snapshot;
        private Action<string, string, JsonElement>? _event;
        public string? ProbeOperation;
        public Action? RetireUnderLock;
        public Task? Retirement;
        public int Subscriptions, Released;
        public bool HasListeners => _snapshot != null || _event != null;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public event Action<string, JsonElement>? SnapshotReceived
        {
            add => Run("add", () => _snapshot += value);
            remove => Run("remove", () => _snapshot -= value);
        }
        public event Action<string, string, JsonElement>? EventReceived
        {
            add => Run("add", () => _event += value);
            remove => Run("remove", () => _event -= value);
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        public IDisposable Subscribe(params string[] channels)
        {
            Run("subscribe", () => Subscriptions++);
            return new ReleaseHandle(() => Run("release", () => Released++));
        }
        private void Run(string operation, Action action)
        {
            if (ProbeOperation == operation)
            {
                ProbeOperation = null;
                var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Retirement = Task.Factory.StartNew(() =>
                {
                    lock (_lock)
                    {
                        held.SetResult();
                        RetireUnderLock!();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Assert.True(held.Task.Wait(TimeSpan.FromSeconds(5)), "retirement did not acquire the channel lock");
            }
            // Bound a regression instead of hanging the entire test runner.
            if (!Monitor.TryEnter(_lock, TimeSpan.FromSeconds(5)))
                throw new TimeoutException("channel operation held the controller gate while retirement held the channel lock");
            try { action(); }
            finally { Monitor.Exit(_lock); }
        }
        private sealed class ReleaseHandle(Action release) : IDisposable
        {
            private Action? _release = release;
            public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    [Fact]
    public async Task LibraryTransportDeadlineCoversResponseBodiesForEveryTransport()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        using var safety = new CancellationTokenSource(TimeSpan.FromSeconds(36));
        kit.Http.Respond = (request, _) => Task.FromResult(new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.EndsWith("empty", StringComparison.Ordinal) ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new StreamContent(new LibraryStalledStream()),
        });
        var operations = new Func<Task>[]
        {
            () => kit.Client.AdminLibrarySendAsync<AdminLibrary>(lease, HttpMethod.Get, "/json", ct: safety.Token),
            () => kit.Client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete, "/empty", ct: safety.Token),
            () => kit.Client.AdminLibraryPutPosterAsync<AdminLibrary>(lease, "/poster", [1], "image/png", "poster.png", safety.Token),
        };
        await Task.WhenAll(operations.Select(async operation =>
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(operation);
            Assert.IsType<TimeoutException>(error.InnerException);
            Assert.True(AdminLibraryPresentation.IsUnknownOutcome(error));
        }));
        Assert.Equal(3, kit.Http.Requests.Count);
    }

    private sealed class LibraryStalledStream(byte[]? body = null) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (body != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(buffer.Length, body.Length - _offset);
                body.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("json", false)]
    [InlineData("empty", false)]
    [InlineData("poster", false)]
    [InlineData("json", true)]
    [InlineData("empty", true)]
    [InlineData("poster", true)]
    public async Task LibraryTransportBoundsFailureBodiesBeforeDecoding(string transport, bool chunked)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var body = System.Text.Encoding.UTF8.GetBytes("{\"detail\":\"" + new string('x', SiloApiClient.AdminLibraryJsonLimit + 1) + "\"}");
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = chunked ? new StreamContent(new LibraryStalledStream(body)) : new ByteArrayContent(body),
        });
        Func<Task> operation = transport switch
        {
            "empty" => () => kit.Client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete, "/empty"),
            "poster" => () => kit.Client.AdminLibraryPutPosterAsync<AdminLibrary>(lease, "/poster", [1], "image/png", "poster.png"),
            _ => () => kit.Client.AdminLibrarySendAsync<AdminLibrary>(lease, HttpMethod.Get, "/json"),
        };
        await Assert.ThrowsAsync<InvalidDataException>(operation);
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("https://other.test/poster.png")]
    [InlineData("http://silo.test/poster.png")]
    [InlineData("https://silo.test:444/poster.png")]
    [InlineData("//other.test/poster.png")]
    [InlineData("file:///local/poster.png")]
    [InlineData("ms-appx:///poster.png")]
    [InlineData("https://user@silo.test/poster.png")]
    public async Task PosterPreviewRejectsUntrustedOriginsBeforeSending(string url)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminLibrariesApi(kit.Client).GetPosterAsync(lease, url));
        Assert.Empty(kit.Http.Requests);
    }

    [Theory]
    [InlineData("https://[")]
    [InlineData("not a server URL")]
    [InlineData("file:///local")]
    public async Task PosterPreviewRejectsInvalidServerBasesWithoutSending(string baseUrl)
    {
        using var kit = AdminMonitorKit.Create();
        kit.Client.SetBaseUrl(baseUrl);
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => new AdminLibrariesApi(kit.Client).GetPosterAsync(lease, "/poster.png"));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task PosterPreviewDiscardsAResponseAfterItsLeaseRetires()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            kit.SetRole("user");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AdminLibrariesApi(kit.Client).GetPosterAsync(lease, "/poster.png"));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("/posters/1.png")]
    [InlineData("posters/1.png")]
    [InlineData("https://silo.test/posters/1.png")]
    public async Task PosterPreviewUsesTheAuthenticatedLeaseTransport(string url)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            Assert.Equal("https://silo.test/posters/1.png", request.RequestUri!.AbsoluteUri);
            Assert.Equal("synthetic-access", request.Headers.Authorization?.Parameter);
            Assert.Equal("p1", request.Headers.GetValues("X-Profile-Id").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        };
        Assert.Equal(new byte[] { 1, 2, 3 }, await new AdminLibrariesApi(kit.Client).GetPosterAsync(lease, url));
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PosterPreviewBoundsBothDeclaredAndChunkedBodies(bool chunked)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var bytes = new byte[AdminLibrariesApi.MaxPosterBytes + 1];
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = chunked ? new StreamContent(new LibraryStalledStream(bytes)) : new ByteArrayContent(bytes),
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminLibrariesApi(kit.Client).GetPosterAsync(lease, "/poster.png"));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task CreateAppliesEnabledAndAutoTranslateBeforeReportingSaved()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var created = AdminLibrariesTests.Library("9", "New");
            var updated = created.Replace("\"enabled\":true", "\"enabled\":false").Replace("\"auto_translate_metadata\":false", "\"auto_translate_metadata\":true");
            string? patch = null;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries" when request.Method == HttpMethod.Post => AdminMonitorKit.Json(created, HttpStatusCode.Created),
                "/api/v2/libraries/9" when request.Method == HttpMethod.Patch => (patch = request.Content!.ReadAsStringAsync().Result) == null ? null : AdminMonitorKit.Json(updated),
                _ => null,
            };
            using var editor = AdminLibraryEditorSession.Begin(controller, null);
            editor.SetName("New"); editor.UpdatePath(0, "/media/new");
            editor.SetEnabled(false); editor.SetAutoTranslate(true);
            await editor.SubmitAsync();
            Assert.True(editor.State.Saved);
            Assert.False(editor.State.Library!.Enabled);
            Assert.True(editor.State.Library.AutoTranslateMetadata);
            using var json = JsonDocument.Parse(patch!);
            Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
            Assert.True(json.RootElement.GetProperty("auto_translate_metadata").GetBoolean());
            Assert.Single(kit.Http.Requests.Where(request => request == "POST /api/v2/libraries"));
            Assert.Single(kit.Http.Requests.Where(request => request == "PATCH /api/v2/libraries/9"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateFollowUpPatchFailureRetainsCreatedIdAndNeverReplaysPost(bool lostAnswer)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var created = AdminLibrariesTests.Library("9", "New");
            var failing = true;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries" when request.Method == HttpMethod.Post => AdminMonitorKit.Json(created, HttpStatusCode.Created),
                "/api/v2/libraries/9" when request.Method == HttpMethod.Patch => failing
                    ? lostAnswer ? throw new HttpRequestException("reset") : AdminMonitorKit.Json("{}", HttpStatusCode.UnprocessableEntity)
                    : AdminMonitorKit.Json(created.Replace("\"enabled\":true", "\"enabled\":false")),
                _ => null,
            };
            using var editor = AdminLibraryEditorSession.Begin(controller, null);
            editor.SetName("New"); editor.UpdatePath(0, "/media/new"); editor.SetEnabled(false);
            await editor.SubmitAsync();
            Assert.False(editor.State.Saved);
            Assert.Equal("9", editor.State.Library?.Id);
            Assert.NotNull(editor.State.Failure);
            Assert.Equal(lostAnswer, editor.State.OutcomeUnknown);
            failing = false;
            await editor.SubmitAsync();
            Assert.Equal(!lostAnswer, editor.State.Saved);
            Assert.Single(kit.Http.Requests.Where(request => request == "POST /api/v2/libraries"));
            Assert.Equal(lostAnswer ? 1 : 2, kit.Http.Requests.Count(request => request == "PATCH /api/v2/libraries/9"));
        }
    }

    [Fact]
    public async Task EmptyCreatedLibraryIdentityCannotBecomeSavedOrReplayCreate()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/libraries"
                ? AdminMonitorKit.Json("{}", HttpStatusCode.Created) : null;
            using var editor = AdminLibraryEditorSession.Begin(controller, null);
            editor.SetName("New");
            editor.UpdatePath(0, "/media/new");
            await editor.SubmitAsync();
            Assert.False(editor.State.Saved);
            Assert.True(editor.State.OutcomeUnknown);
            Assert.Null(editor.State.Library);
            await editor.SubmitAsync();
            Assert.Single(kit.Http.Requests.Where(request => request == "POST /api/v2/libraries"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialUnmatchedTotalsFailureStaysVisibleAndKeepsTheOtherCount(bool bothFail)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var failing = true;
            server.Override = request => request.RequestUri!.AbsolutePath != "/api/v2/libraries/unmatched-items" ? null
                : failing && (bothFail || request.RequestUri.Query.Contains("ignored=true", StringComparison.Ordinal))
                    ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : AdminMonitorKit.Json(UnmatchedPage(0, 1, null, 1));
            using var panel = new AdminUnmatchedPanel(controller);
            await panel.LoadTotalsAsync();
            Assert.False(panel.State.Hidden);
            if (!bothFail) Assert.Equal(1, panel.State.AwaitingTotal);
            Assert.Null(panel.State.IgnoredTotal);
            failing = false;
            await panel.LoadTotalsAsync();
            Assert.Equal(1, panel.State.AwaitingTotal);
            Assert.Equal(1, panel.State.IgnoredTotal);
        }
    }

    [Fact]
    public async Task FirstTranslationJobsFailureCannotMasqueradeAsIdleAndRetriesReadOnly()
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var reads = 0;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/capabilities/metadata-ai" => AdminMonitorKit.Json("{\"state\":\"available\"}"),
                "/api/v2/admin/libraries/1/metadata-translation/jobs" => ++reads == 1
                    ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : AdminMonitorKit.Json("{\"jobs\":[]}"),
                _ => null,
            };
            using var lease = controller.CaptureAction();
            using var translation = new AdminLibraryTranslationSession(controller, lease, "1", controller.WatchedTranslationJobs);
            await translation.OpenAsync();
            Assert.True(translation.Busy);
            Assert.NotNull(translation.JobsError);
            Assert.False(translation.JobsLoaded);
            scheduler.Advance(AdminLibraryTranslationSession.PollInterval);
            await Until(() => reads == 2);
            await Until(() => !translation.Busy);
            Assert.True(translation.JobsLoaded);
            Assert.Null(translation.JobsError);
            Assert.DoesNotContain(kit.Http.Requests, request => !request.StartsWith("GET ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task InitialTranslationJobsRetryIsBoundedAndCanRecoverWithExplicitReadOnlyRetry()
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var reads = 0;
            var failing = true;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/capabilities/metadata-ai" => AdminMonitorKit.Json("{\"state\":\"available\"}"),
                "/api/v2/admin/libraries/1/metadata-translation/jobs" => ++reads > 0 && failing
                    ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : AdminMonitorKit.Json("{\"jobs\":[]}"),
                _ => null,
            };
            using var lease = controller.CaptureAction();
            using var translation = new AdminLibraryTranslationSession(controller, lease, "1", controller.WatchedTranslationJobs);
            await translation.OpenAsync();
            scheduler.Advance(AdminLibraryTranslationSession.PollInterval);
            await Until(() => reads == 2 && !translation.JobsLoading);
            scheduler.Advance(TimeSpan.FromSeconds(20));
            Assert.Equal(2, reads);
            Assert.True(translation.Busy);
            Assert.NotNull(translation.JobsError);
            await translation.StartAsync("de");
            failing = false;
            await translation.RetryJobsAsync();
            Assert.Equal(3, reads);
            Assert.Null(translation.JobsError);
            Assert.False(translation.Busy);
            Assert.DoesNotContain(kit.Http.Requests, request => !request.StartsWith("GET ", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendDiagnosticsRejectCrossPageIdentityOrCursorCycles(bool duplicateIdentity)
    {
        var (kit, authority, controller, _, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var page = 0;
            using var list = new AdminAppendList<AdminStaleMediaId>(controller, (_, _, _, _, _) => Task.FromResult(
                new AdminTotalCursorPage<AdminStaleMediaId>([new() { ContentId = duplicateIdentity && page == 2 ? "0" : page.ToString() }],
                    ++page == 3 ? duplicateIdentity ? "C" : "A" : page == 1 ? "A" : "B", 10)), item => item.ContentId);
            await list.SetOpenAsync(true);
            await list.LoadMoreAsync();
            await list.LoadMoreAsync();
            Assert.Equal(2, list.State.Items.Count);
            Assert.NotNull(list.State.Error);
            await list.SetSearchAsync("new scope");
            Assert.Null(list.State.Error);
            Assert.Single(list.State.Items);
        }
    }
}
