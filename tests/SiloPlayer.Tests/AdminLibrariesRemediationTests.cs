using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminLibrariesControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslationCapabilityFailureCanRetryReadOnlyAndBecomeAvailable(bool transportFailure)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var reads = 0;
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v2/capabilities/metadata-ai")
                {
                    if (Interlocked.Increment(ref reads) == 1)
                    {
                        if (transportFailure) throw new HttpRequestException("Synthetic capability read failure");
                        return AdminMonitorKit.Json("{\"type\":\"dependency_unavailable\",\"title\":\"Temporarily unavailable\"}", HttpStatusCode.ServiceUnavailable);
                    }
                    return AdminMonitorKit.Json("{\"state\":\"available\"}");
                }
                return request.RequestUri.AbsolutePath == "/api/v2/admin/libraries/1/metadata-translation/jobs"
                    ? AdminMonitorKit.Json("{\"jobs\":[]}") : null;
            };
            using var lease = controller.CaptureAction();
            using var translation = new AdminLibraryTranslationSession(controller, lease, "1", controller.WatchedTranslationJobs);
            await translation.OpenAsync();
            Assert.False(translation.Enabled);
            Assert.NotNull(translation.CapabilityError);
            Assert.Equal(transportFailure ? AdminFailureKind.Transport : AdminFailureKind.Server, translation.CapabilityFailure);
            Assert.False(translation.CapabilityLoading);
            Assert.DoesNotContain(kit.Http.Requests, request => request.Contains("metadata-translation/jobs", StringComparison.Ordinal));
            await translation.StartAsync("de");
            await translation.RetryCapabilityAsync();
            Assert.True(translation.Enabled);
            Assert.Null(translation.CapabilityError);
            Assert.Null(translation.CapabilityFailure);
            Assert.Equal(2, reads);
            Assert.Single(kit.Http.Requests.Where(request => request.Contains("metadata-translation/jobs", StringComparison.Ordinal)));
            Assert.DoesNotContain(kit.Http.Requests, request => !request.StartsWith("GET ", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslationCapabilityRetryCannotReadAfterEditorOrOwnerRetires(bool retireOwner)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/capabilities/metadata-ai"
                ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : null;
            using var lease = controller.CaptureAction();
            using var translation = new AdminLibraryTranslationSession(controller, lease, "1", controller.WatchedTranslationJobs);
            await translation.OpenAsync();
            Assert.NotNull(translation.CapabilityError);
            if (retireOwner) controller.Suspend(); else translation.Dispose();
            var before = kit.Http.Requests.ToArray();
            await translation.RetryCapabilityAsync();
            Assert.Equal(before, kit.Http.Requests.ToArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(AdminLibrariesApi.MaxPosterBytes)]
    public async Task PosterStreamEnforcesEmptyAndExactSizeBoundaries(int length)
    {
        using var stream = new GrowingPosterStream(length);
        if (length == 0)
            await Assert.ThrowsAsync<AdminLibraryPosterException>(() => AdminLibraryEditorSession.ReadPosterAsync(stream));
        else
        {
            var bytes = await AdminLibraryEditorSession.ReadPosterAsync(stream);
            Assert.Equal(length, bytes.Length);
            Assert.Equal((byte)7, bytes[0]);
            Assert.Equal((byte)7, bytes[^1]);
        }
        Assert.Equal(length, stream.BytesRead);
        Assert.InRange(stream.LargestRead, 1, 64 * 1024);
    }

    [Fact]
    public async Task PosterStreamRejectsGrowthPastTenMiBWithoutReadingTheRest()
    {
        using var stream = new GrowingPosterStream(AdminLibrariesApi.MaxPosterBytes + 1024 * 1024);
        Assert.Equal(1, stream.Length);
        await Assert.ThrowsAsync<AdminLibraryPosterException>(() => AdminLibraryEditorSession.ReadPosterAsync(stream));
        Assert.Equal(AdminLibrariesApi.MaxPosterBytes + 1, stream.BytesRead);
        Assert.InRange(stream.LargestRead, 1, 64 * 1024);
    }

    private sealed class GrowingPosterStream(int actualLength) : Stream
    {
        public int BytesRead { get; private set; }
        public int LargestRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LargestRead = Math.Max(LargestRead, buffer.Length);
            var count = Math.Min(buffer.Length, actualLength - BytesRead);
            buffer.Span[..count].Fill(7);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).Result;
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchRefreshResetsUnmatchedCursorsAndBothTotalsButKeepsClosedRowsLazy(bool open)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var matched = false;
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/libraries/unmatched-items") return null;
                var ignored = request.RequestUri.Query.Contains("ignored=true", StringComparison.Ordinal);
                var next = matched ? null : request.RequestUri.Query.Contains("cursor=old-next", StringComparison.Ordinal) ? "old-final" : "old-next";
                return AdminMonitorKit.Json(UnmatchedPage(0, 1, next, matched ? (ignored ? 2 : 3) : 30));
            };
            using var panel = new AdminUnmatchedPanel(controller);
            await panel.LoadTotalsAsync();
            await panel.SetOpenAsync(true);
            await panel.NextAsync();
            Assert.Equal(1, panel.State.PageIndex);
            await panel.SetOpenAsync(open);
            matched = true;
            var before = kit.Http.Requests.Count;
            await panel.RefreshAfterMatchAsync();
            var refreshReads = kit.Http.Requests.Skip(before).ToArray();
            Assert.Equal(open ? 3 : 2, refreshReads.Length);
            Assert.DoesNotContain(refreshReads, request => request.Contains("cursor=", StringComparison.Ordinal));
            Assert.Equal(3, panel.State.AwaitingTotal);
            Assert.Equal(2, panel.State.IgnoredTotal);
            Assert.Equal(0, panel.State.PageIndex);
            Assert.Equal(open ? 1 : 0, panel.State.Items.Count);
            if (!open)
            {
                await panel.SetOpenAsync(true);
                Assert.Single(panel.State.Items);
                Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
            }
        }
    }

    [Fact]
    public async Task RetainedRenderedOwnerCannotAdmitCommandsOrEditorsAfterReplacementFullyOpens()
    {
        var (kit, authority, controller, _, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var retainedOwner = controller.State.Owner!;
            var retainedOrder = controller.State.Libraries.Reverse().ToArray();
            controller.Suspend();
            controller.Resume();
            await controller.OpenAsync();
            Assert.NotSame(retainedOwner, controller.State.Owner);
            Assert.True(controller.State.Loaded);
            var before = kit.Http.Requests.ToArray();
            await controller.ScanOrStopAsync("1", retainedOwner);
            await controller.StartScanAsync("1", retainedOwner);
            await controller.CancelScansAsync("1", retainedOwner);
            Assert.False(await controller.RefreshMetadataAsync("1", "quick", retainedOwner));
            await controller.CancelJobAsync("j1", retainedOwner);
            await controller.CheckMountAsync("1", retainedOwner);
            await controller.ConfirmCleanupAsync("1", retainedOwner);
            await controller.DeleteAsync("1", retainedOwner);
            await controller.ScanAllAsync(retainedOwner);
            await controller.MoveAsync("1", 1, retainedOwner);
            await controller.ReorderAsync(retainedOrder, retainedOwner);
            Assert.Throws<MonitorAuthorityRetiredException>(() => AdminLibraryEditorSession.Begin(controller, controller.State.Libraries[0], retainedOwner));
            Assert.Equal(before, kit.Http.Requests.ToArray());
            await controller.StartScanAsync("1", controller.State.Owner);
            Assert.Contains("POST /api/v2/scan", kit.Http.Requests);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DraftEditsInvalidateProbesBeforeDebounceOrValidation(bool submit)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/admin/filesystem/browse") return null;
                entered.TrySetResult(); release.Task.GetAwaiter().GetResult();
                return AdminMonitorKit.Json("{\"path\":\"/A\",\"parent\":\"/\",\"items\":[],\"page\":{\"has_more\":false}}");
            };
            using var lease = controller.CaptureAction();
            using var browser = new AdminFolderBrowserSession(controller, lease, []);
            var pending = submit ? browser.SubmitDraftAsync("/A") : browser.TryFollowDraftAsync("/A");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            browser.DraftChanged();
            release.TrySetResult();
            Assert.Null(await pending);
            Assert.Equal("/", browser.State.CurrentPath);
            Assert.Null(browser.State.ValidationError);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderProbeCannotOvertakeANewerDraftOrExplicitNavigation(bool submit)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/admin/filesystem/browse") return null;
                var path = Uri.UnescapeDataString(request.RequestUri.Query.Split("path=")[1].Split('&')[0]);
                if (path == "/A" && request.RequestUri.Query.Contains("limit=1", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                }
                return AdminMonitorKit.Json("{\"path\":\"" + path + "\",\"parent\":\"/\",\"items\":[],\"page\":{\"has_more\":false}}");
            };
            using var lease = controller.CaptureAction();
            using var browser = new AdminFolderBrowserSession(controller, lease, []);
            await browser.OpenAsync();
            var first = submit ? browser.SubmitDraftAsync("/A") : browser.TryFollowDraftAsync("/A");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try { Assert.Equal("/B", await browser.TryFollowDraftAsync("/B")); }
            finally { release.TrySetResult(); }
            Assert.Null(await first);
            Assert.Equal("/B", browser.State.CurrentPath);
            Assert.Equal("/B", browser.State.ResolvedPath);
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            first = browser.TryFollowDraftAsync("/A");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try { await browser.NavigateAsync("/"); }
            finally { release.TrySetResult(); }
            Assert.Null(await first);
            Assert.Equal("/", browser.State.ResolvedPath);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FolderFirstPageLoadResetsPaginationAndCannotBeSupersededByLoadMore(bool refresh, bool fail)
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            var target = refresh ? "/A" : "/B";
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/admin/filesystem/browse") return null;
                var read = Interlocked.Increment(ref reads);
                if (read == 1) return AdminMonitorKit.Json(Page("/A", "old", "old-cursor"));
                if (read == 2)
                {
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                    if (fail) return AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable);
                }
                return AdminMonitorKit.Json(Page(target, read == 2 || fail && read == 3 ? "first" : "second",
                    read == 2 || fail && read == 3 ? "new-cursor" : null));
            };
            using var lease = controller.CaptureAction();
            using var browser = new AdminFolderBrowserSession(controller, lease, []);
            await browser.NavigateAsync("/A");
            Assert.True(browser.State.HasMore);
            Assert.Single(browser.State.Entries);
            var pending = refresh ? browser.RefreshAsync() : browser.NavigateAsync(target);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(browser.State.Fetching);
                Assert.True(browser.State.Loading);
                Assert.Empty(browser.State.Entries);
                Assert.False(browser.State.HasMore);
                Assert.False(browser.State.LoadingMore);
                await browser.LoadMoreAsync();
                Assert.Equal(2, Volatile.Read(ref reads));
            }
            finally { release.TrySetResult(); }
            await pending;
            Assert.False(browser.State.Fetching);
            Assert.False(browser.State.Loading);
            if (fail)
            {
                Assert.NotNull(browser.State.Error);
                Assert.Empty(browser.State.Entries);
                Assert.False(browser.State.HasMore);
                await browser.LoadMoreAsync();
                Assert.Equal(2, reads);
                await browser.RefreshAsync();
            }
            Assert.Null(browser.State.Error);
            Assert.Equal(target, browser.State.ResolvedPath);
            Assert.Equal(target + "/first", Assert.Single(browser.State.Entries).Path);
            await browser.LoadMoreAsync();
            Assert.Equal(new[] { target + "/first", target + "/second" }, browser.State.Entries.Select(entry => entry.Path));
            Assert.False(browser.State.HasMore);
            var browseRequests = kit.Http.Requests.Where(request => request.Contains("/admin/filesystem/browse", StringComparison.Ordinal)).ToArray();
            Assert.DoesNotContain(browseRequests, request => request.Contains("cursor=old-cursor", StringComparison.Ordinal));
            Assert.Contains("cursor=new-cursor", browseRequests[^1]);
        }

        static string Page(string path, string name, string? cursor) =>
            "{\"path\":\"" + path + "\",\"parent\":\"/\",\"items\":[{\"name\":\"" + name + "\",\"path\":\"" + path + "/" + name
            + "\"}],\"page\":{\"has_more\":" + (cursor != null ? "true,\"next_cursor\":\"" + cursor + "\"" : "false") + "}}";
    }

    [Fact]
    public async Task DisposedFolderBrowserRejectsALateProbe()
    {
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/v2/admin/filesystem/browse") return null;
                entered.TrySetResult(); release.Task.GetAwaiter().GetResult();
                return AdminMonitorKit.Json("{\"path\":\"/A\",\"parent\":\"/\",\"items\":[],\"page\":{\"has_more\":false}}");
            };
            using var lease = controller.CaptureAction();
            using var browser = new AdminFolderBrowserSession(controller, lease, []);
            var pending = browser.SubmitDraftAsync("/A");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            browser.Dispose(); release.TrySetResult();
            Assert.Null(await pending);
            Assert.Equal("/", browser.State.CurrentPath);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TranslationPollRecoversAfterOneReadFailureAndStopsWhenEditorOrOwnerRetires(bool retireOwner, bool retireDuringRetry)
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var reads = 0;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/capabilities/metadata-ai" => AdminMonitorKit.Json("{\"state\":\"available\"}"),
                "/api/v2/admin/libraries/1/metadata-translation/jobs" => Interlocked.Increment(ref reads) == 2
                    ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : AdminMonitorKit.Json("{\"jobs\":[{\"id\":\"t1\",\"status\":\"" + (reads >= 3 ? "completed" : "running") + "\",\"target_language\":\"de\"}]}"),
                _ => null,
            };
            using var lease = controller.CaptureAction();
            using var translation = new AdminLibraryTranslationSession(controller, lease, "1", controller.WatchedTranslationJobs);
            await translation.OpenAsync();
            Assert.True(translation.Busy);
            scheduler.Advance(AdminLibraryTranslationSession.PollInterval);
            await Until(() => Volatile.Read(ref reads) == 2);
            await Until(() => scheduler.Pending(AdminLibraryTranslationSession.PollInterval) > 0);
            if (retireDuringRetry)
            {
                if (retireOwner) controller.Suspend(); else translation.Dispose();
                scheduler.Advance(TimeSpan.FromMinutes(1));
                Assert.Equal(2, reads);
                return;
            }
            scheduler.Advance(AdminLibraryTranslationSession.PollInterval);
            await Until(() => !translation.Busy);
            Assert.Equal(3, reads);
            Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("POST ", StringComparison.Ordinal));
            if (retireOwner) controller.Suspend(); else translation.Dispose();
            scheduler.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(3, reads);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MatcherDetailReadRetriesWithoutRetryNowAndStopsOnCollapseOrRetirement(bool retireOwner, bool retireDuringRetry)
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var reads = 0;
            server.Override = request => request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/libraries/1/metadata-match-queue" => Interlocked.Increment(ref reads) == 2
                    ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"total_count\":1,\"page\":{\"has_more\":false}}"),
                _ => null,
            };
            using var panel = new AdminMatcherQueuesPanel(controller);
            panel.SetOpen(true);
            await panel.ToggleLibraryAsync("1");
            scheduler.Advance(AdminMatcherQueuesPanel.PollInterval);
            await Until(() => panel.State.DetailError != null);
            Assert.NotNull(panel.State.Detail);
            if (retireDuringRetry)
            {
                if (retireOwner) controller.Suspend(); else panel.SetOpen(false);
                scheduler.Advance(TimeSpan.FromMinutes(1));
                Assert.Equal(2, reads);
                return;
            }
            scheduler.Advance(AdminMatcherQueuesPanel.PollInterval);
            await Until(() => Volatile.Read(ref reads) == 3 && panel.State.DetailError == null && !panel.State.DetailFetching);
            Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("POST ", StringComparison.Ordinal));
            if (retireOwner) controller.Suspend(); else panel.SetOpen(false);
            scheduler.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(3, reads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SummaryPollingResumesWhenSummaryCompletesDuringDetailRead(bool detailFails)
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var summaryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var summaryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var detailEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var detailRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var summaryReads = 0;
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v2/libraries/metadata-match-queue")
                {
                    var read = Interlocked.Increment(ref summaryReads);
                    if (read == 1)
                    {
                        summaryEntered.TrySetResult();
                        summaryRelease.Task.GetAwaiter().GetResult();
                    }
                    return AdminMonitorKit.Json("{\"items\":[{\"library_id\":\"1\",\"total_count\":" + read + "}]}");
                }
                if (request.RequestUri.AbsolutePath == "/api/v2/libraries/1/metadata-match-queue")
                {
                    detailEntered.TrySetResult();
                    detailRelease.Task.GetAwaiter().GetResult();
                    return detailFails ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)
                        : AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"page\":{\"has_more\":false}}");
                }
                return null;
            };
            using var panel = new AdminMatcherQueuesPanel(controller);
            try
            {
                var summary = panel.LoadSummaryAsync();
                await summaryEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                panel.SetOpen(true);
                var detail = panel.ToggleLibraryAsync("1");
                await detailEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                summaryRelease.TrySetResult();
                await summary.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(panel.State.DetailFetching);
                Assert.Equal(1, panel.State.Total);

                detailRelease.TrySetResult();
                await detail.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(detailFails, panel.State.DetailError != null);
                scheduler.Advance(AdminLibrariesController.EventCoalescing);
                await Until(() => panel.State.Total == 2 && scheduler.Pending(AdminMatcherQueuesPanel.PollInterval) == 2);

                // Closing detail must leave the ordinary summary poll running.
                panel.SetOpen(false);
                scheduler.Advance(AdminMatcherQueuesPanel.PollInterval);
                await Until(() => panel.State.Total == 3);
                Assert.Equal(3, Volatile.Read(ref summaryReads));
            }
            finally
            {
                summaryRelease.TrySetResult();
                detailRelease.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task SummaryInvalidationsWaitForForegroundDetailAndResumeOnce()
    {
        var (kit, authority, controller, server, _, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var summaryReads = 0;
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v2/libraries/1/metadata-match-queue")
                {
                    entered.TrySetResult(); release.Task.GetAwaiter().GetResult();
                    return AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"total_count\":0,\"page\":{\"has_more\":false}}");
                }
                if (request.RequestUri.AbsolutePath == "/api/v2/libraries/metadata-match-queue")
                {
                    Interlocked.Increment(ref summaryReads);
                    return AdminMonitorKit.Json("{\"items\":[]}");
                }
                return null;
            };
            using var panel = new AdminMatcherQueuesPanel(controller);
            panel.SetOpen(true);
            var detail = panel.ToggleLibraryAsync("1");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                for (var index = 0; index < 25; index++) panel.InvalidateSummary();
                Assert.Equal(0, summaryReads);
            }
            finally { release.TrySetResult(); }
            await detail;
            scheduler.Advance(AdminLibrariesController.EventCoalescing);
            await Until(() => panel.State.Loaded);
            Assert.Equal(1, summaryReads);
        }
    }

    [Fact]
    public async Task ScanTerminalInvalidationsShareOneSummaryReadAndDoNotStarveForegroundDetail()
    {
        var (kit, authority, controller, server, channel, scheduler) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var summaryReads = 0;
            server.Override = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/v2/libraries/metadata-match-queue")
                {
                    Interlocked.Increment(ref summaryReads);
                    entered.TrySetResult(); release.Task.GetAwaiter().GetResult();
                    return AdminMonitorKit.Json("{\"items\":[]}");
                }
                if (request.RequestUri.AbsolutePath == "/api/v2/libraries/1/metadata-match-queue")
                    return AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"total_count\":0,\"page\":{\"has_more\":false}}");
                return null;
            };
            using var panel = new AdminMatcherQueuesPanel(controller);
            controller.DiagnosticsStale += panel.InvalidateSummary;
            var first = panel.LoadSummaryAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                for (var index = 0; index < 25; index++)
                    channel.Event("scans", "scan.completed", "{\"id\":\"s" + index + "\",\"library_id\":1,\"mode\":\"library\",\"status\":\"completed\"}");
                panel.SetOpen(true);
                await panel.ToggleLibraryAsync("1");
                Assert.NotNull(panel.State.Detail);
                Assert.Equal(1, Volatile.Read(ref summaryReads));
            }
            finally { release.TrySetResult(); }
            await first;
            scheduler.Advance(AdminLibrariesController.EventCoalescing);
            await Until(() => Volatile.Read(ref summaryReads) == 2);
            Assert.Equal(2, summaryReads);
            panel.Dispose();
            var stopped = summaryReads;
            scheduler.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(stopped, summaryReads);
        }
    }
}
