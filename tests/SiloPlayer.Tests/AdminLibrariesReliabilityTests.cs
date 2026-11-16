using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminLibrariesControllerTests
{
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
