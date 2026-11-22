using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Tests;

/// <summary>Official UploadBackupDialog/uploadModel behaviour at 92ee051 on a synthetic server.</summary>
public sealed class AdminBackupUploadSessionTests
{
    private const long Size = 3L << 20;

    /// <summary>A tar whose first entry is restore-set.json, padded to <paramref name="size"/>.</summary>
    private static MemoryStream RestoreSet(long size = Size)
    {
        var bytes = new byte[size];
        System.Text.Encoding.ASCII.GetBytes("restore-set.json").CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("ustar").CopyTo(bytes, 257);
        return new MemoryStream(bytes, writable: false);
    }

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminBackupUploadSession Session;
        internal readonly List<int> Parts = [];
        internal readonly List<TimeSpan> Waits = [];
        internal Func<int, int, HttpResponseMessage>? Part;
        internal Func<HttpResponseMessage>? Complete;
        private int _received;
        internal Harness(long max = 2L << 30)
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Session = new AdminBackupUploadSession(new AdminBackupsApi(Kit.Client), Authority.Capture(), max, (delay, _) => { Waits.Add(delay); return Task.CompletedTask; });
            var attempts = new Dictionary<int, int>();
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method == HttpMethod.Post && path == "/api/v2/admin/backups/uploads")
                    return Task.FromResult(BackupJson.Json(BackupJson.Upload(), HttpStatusCode.Created));
                if (request.Method == HttpMethod.Put)
                {
                    var part = int.Parse(path[(path.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                    attempts[part] = attempts.GetValueOrDefault(part) + 1;
                    lock (Parts) Parts.Add(part);
                    if (Part?.Invoke(part, attempts[part]) is { } custom) return Task.FromResult(custom);
                    _received = Math.Max(_received, part + 1);
                    return Task.FromResult(BackupJson.Json(BackupJson.Upload(received: _received)));
                }
                if (path.EndsWith("/complete", StringComparison.Ordinal))
                    return Task.FromResult(Complete?.Invoke() ?? BackupJson.Json(BackupJson.Upload("complete", received: 3,
                        extra: ",\"digest\":\"" + BackupJson.Digest + "\",\"source\":{\"server_id\":\"s-2\",\"server_name\":\"Den\",\"silo_version\":\"1.0.0\",\"created_at\":\"2026-11-15T04:00:00.000Z\",\"format_version\":1}")));
                if (request.Method == HttpMethod.Delete) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                return Task.FromResult(BackupJson.Json("{}", HttpStatusCode.NotFound));
            };
        }
        internal string[] Requests => Kit.Http.Requests.ToArray();
        public void Dispose() { Session.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task PartsGoInOrderAndTheCheckedUploadOpens()
    {
        using var h = new Harness();
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        Assert.Equal(AdminBackupUploadStep.Opened, h.Session.Phase.Step);
        Assert.Equal([0, 1, 2], h.Parts);
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/backups/uploads");
        Assert.Single(h.Requests, request => request.EndsWith("/complete", StringComparison.Ordinal));
        Assert.DoesNotContain("web.admin", Present.OpenedSentence(h.Session.Phase.Upload!));
    }

    [Fact]
    public async Task ARetryablePartIsResentAtMostThreeTimesThenTryAgainContinuesFromIt()
    {
        using var h = new Harness { Part = (part, attempt) => part == 1 && attempt <= 3 ? BackupJson.Json(BackupJson.Problem("conflict", 409), HttpStatusCode.Conflict) : null };
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        var phase = h.Session.Phase;
        Assert.Equal(AdminBackupUploadStep.Failed, phase.Step);
        Assert.Equal("part_failed", phase.Failure!.Kind);
        Assert.Equal(1, phase.From);
        Assert.Equal([0, 1, 1, 1], h.Parts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], h.Waits);
        Assert.True(AdminBackupUploadSession.CopyOf(phase.Failure).Resumable);
        await h.Session.ResumeAsync();
        Assert.Equal(AdminBackupUploadStep.Opened, h.Session.Phase.Step);
        Assert.Equal([0, 1, 1, 1, 1, 2], h.Parts);
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/backups/uploads"); // creating is never resent
    }

    [Fact]
    public async Task ARejectedPartIsNotResent()
    {
        using var h = new Harness { Part = (part, _) => part == 0 ? BackupJson.Json(BackupJson.Problem("validation_failed", 422), HttpStatusCode.UnprocessableEntity) : null };
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        Assert.Equal("error", h.Session.Phase.Failure!.Kind);
        Assert.Equal([0], h.Parts);
        Assert.False(AdminBackupUploadSession.CopyOf(h.Session.Phase.Failure).Resumable);
    }

    [Fact]
    public async Task ARejectedRestoreSetShowsTheServerReasonAndAnInterruptedCheckIsCheckedAgain()
    {
        using var h = new Harness { Complete = () => BackupJson.Json(BackupJson.Upload("rejected", received: 3,
            extra: ",\"rejection\":{\"reason\":\"newer_silo\",\"message\":\"Written by Silo 9.\"}")) };
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        var failure = h.Session.Phase.Failure!;
        Assert.Equal(("rejected", "newer_silo"), (failure.Kind, failure.Reason));
        Assert.Equal("Written by Silo 9.", AdminBackupUploadSession.CopyOf(failure).Detail);

        using var interrupted = new Harness { Complete = () => BackupJson.Json(BackupJson.Problem("internal_error", 500), HttpStatusCode.InternalServerError) };
        await interrupted.Session.StartAsync("restore-set.tar", RestoreSet());
        Assert.Equal("check_failed", interrupted.Session.Phase.Failure!.Kind);
        interrupted.Complete = null;
        await interrupted.Session.ResumeAsync();
        Assert.Equal(AdminBackupUploadStep.Opened, interrupted.Session.Phase.Step);
        Assert.Equal([0, 1, 2], interrupted.Parts); // checking again sends no part
        Assert.Equal(2, interrupted.Requests.Count(request => request.EndsWith("/complete", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TooLargeAndNonRestoreSetFilesStopBeforeAnythingIsSent()
    {
        using var tooLarge = new Harness(max: 1L << 20);
        await tooLarge.Session.StartAsync("restore-set.tar", RestoreSet());
        Assert.Equal("too_large", tooLarge.Session.Phase.Failure!.Kind);
        Assert.Empty(tooLarge.Requests);

        using var wrong = new Harness();
        await wrong.Session.StartAsync("photo.jpg", new MemoryStream(new byte[2048]));
        Assert.Equal(("rejected", "not_a_restore_set"), (wrong.Session.Phase.Failure!.Kind, wrong.Session.Phase.Failure.Reason));
        Assert.Empty(wrong.Requests);
    }

    [Fact]
    public async Task UploadLimitReachedIsNotResumable()
    {
        using var h = new Harness();
        h.Kit.Http.Respond = (_, _) => Task.FromResult(BackupJson.Json(BackupJson.Problem("upload_limit_reached", 409), HttpStatusCode.Conflict));
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        Assert.Equal("too_many", h.Session.Phase.Failure!.Kind);
        Assert.False(AdminBackupUploadSession.CopyOf(h.Session.Phase.Failure).Resumable);
    }

    [Fact]
    public async Task DiscardDeletesAnUnfinishedUploadButKeepsACheckedOne()
    {
        using var h = new Harness { Part = (part, attempt) => part == 1 ? BackupJson.Json(BackupJson.Problem("validation_failed", 422), HttpStatusCode.UnprocessableEntity) : null };
        await h.Session.StartAsync("restore-set.tar", RestoreSet());
        await h.Session.DiscardAsync();
        Assert.Single(h.Requests, request => request == "DELETE /api/v2/admin/backups/uploads/" + BackupJson.UploadId);

        using var done = new Harness();
        await done.Session.StartAsync("restore-set.tar", RestoreSet());
        await done.Session.DiscardAsync();
        Assert.DoesNotContain(done.Requests, request => request.StartsWith("DELETE", StringComparison.Ordinal));
    }

    private sealed class DisposedReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        internal readonly TaskCompletionSource Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) > 1)
            {
                Reading.TrySetResult();
                await _release.Task;
                ObjectDisposedException.ThrowIf(!CanRead, this);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _release.TrySetResult();
        }
    }

    [Fact]
    public async Task DisposingDuringAFileReadDoesNotEscapeTheUploadTask()
    {
        using var harness = new Harness();
        using var fixture = RestoreSet();
        var file = new DisposedReadStream(fixture.ToArray());
        var pending = harness.Session.StartAsync("restore-set.tar", file);
        await file.Reading.Task.WaitAsync(TimeSpan.FromSeconds(30));
        harness.Session.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(harness.Requests, request => request.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("age-encryption.org/v1\n", "encrypted")]
    [InlineData("-----BEGIN AGE ENCRYPTED FILE-----", "encrypted")]
    public void SniffingRecognizesAgeFiles(string head, string expected) =>
        Assert.Equal(expected, Present.SniffRestoreSet(System.Text.Encoding.ASCII.GetBytes(head)));
}
