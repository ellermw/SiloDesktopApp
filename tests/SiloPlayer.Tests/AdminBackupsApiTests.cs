using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Auth;

namespace SiloPlayer.Tests;

/// <summary>Configure > Server > Backups wire contract (official internal/apiv2/admin_backup*.go at 92ee051) on synthetic data.</summary>
public sealed class AdminBackupsApiTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminBackupsApi Api;
        internal readonly MonitorAuthorityLease Lease;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Api = new AdminBackupsApi(Kit.Client);
            Lease = Authority.Capture();
        }
        internal void Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => Kit.Http.Respond = (request, _) => Task.FromResult(respond(request));
        internal string[] Requests => Kit.Http.Requests.ToArray();
        public void Dispose() { Lease.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task BackupHeadersCanOutwaitTheSharedTransportTimeoutWhileCancellationStillWorks()
    {
        using var configuredShared = SiloHttpClientFactory.CreateClient();
        using var configuredBackups = SiloHttpClientFactory.CreateBackupsClient();
        Assert.Equal(TimeSpan.FromSeconds(100), configuredShared.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, configuredBackups.Timeout);

        // Scale the shared timeout and delayed headers down so this regression does not wait 100 seconds.
        using var handler = new AdminMonitorHttp();
        using var shared = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMilliseconds(50) };
        using var backups = new HttpClient(handler, disposeHandler: false) { Timeout = configuredBackups.Timeout };
        var client = new SiloApiClient(shared, backups);
        client.SetBaseUrl("https://silo.test");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        using var lease = authority.Capture();
        var api = new AdminBackupsApi(client);
        handler.Respond = async (_, token) =>
        {
            await Task.Delay(200, token);
            return BackupJson.Json(BackupJson.RestoreSet(BackupJson.Section("settings", BackupJson.Item("general"))));
        };
        Assert.NotNull(await api.GetRestoreSetAsync(lease, AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId), false));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetBytesAsync("/ordinary"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetRestoreSetAsync(lease,
            AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId), false, cancel.Token));
    }

    [Fact]
    public async Task CapabilitiesAndListAreValidatedFailClosed()
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json(BackupJson.Capabilities()));
        var capabilities = await h.Api.GetCapabilitiesAsync(h.Lease);
        Assert.True(capabilities.Available);
        Assert.Equal(2L << 30, capabilities.MaxUploadBytes);
        h.Respond(_ => BackupJson.Json(BackupJson.Capabilities().Replace("\"max_upload_bytes\":2147483648", "\"max_upload_bytes\":4294967296")));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetCapabilitiesAsync(h.Lease));
        h.Respond(_ => BackupJson.Json(BackupJson.Capabilities(state: "sideways")));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetCapabilitiesAsync(h.Lease));
        h.Respond(_ => BackupJson.Json(BackupJson.List(BackupJson.Backup() + "," + BackupJson.Backup())));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.ListAsync(h.Lease)); // duplicate identity
        h.Respond(_ => BackupJson.Json(BackupJson.List(BackupJson.Backup(id: "../../etc"))));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.ListAsync(h.Lease));
        h.Respond(_ => BackupJson.Json(BackupJson.List(BackupJson.Backup(objects: BackupJson.Object("../db.dump", 1, BackupJson.Sha)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.ListAsync(h.Lease));
    }

    [Fact]
    public async Task AnExpiredTokenIsRefreshedForAReadOnly()
    {
        using var harness = new Harness();
        var refreshes = 0;
        var reads = 0;
        harness.Kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        harness.Respond(_ => ++reads == 1
            ? BackupJson.Json(BackupJson.Problem("authentication_required", 401), HttpStatusCode.Unauthorized)
            : BackupJson.Json(BackupJson.Capabilities()));
        var capabilities = await harness.Api.GetCapabilitiesAsync(harness.Lease);
        Assert.True(capabilities.Available);
        Assert.Equal(1, refreshes);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task BackUpNowIsSentOnceAndTellsTheTwoConflictsApart()
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json("""{"id":"20261116T120000Z-manual","kind":"manual","status":"in_progress"}""", HttpStatusCode.Accepted));
        var started = await h.Api.StartBackupAsync(h.Lease);
        Assert.Equal("20261116T120000Z-manual", started.Id);

        h.Respond(_ => BackupJson.Json(BackupJson.Problem("conflict", 409, "Another backup or restore is running."), HttpStatusCode.Conflict));
        var busy = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartBackupAsync(h.Lease));
        Assert.Equal("conflict", busy.ProblemType);
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("capability_not_configured", 409, "This server has no storage for backups."), HttpStatusCode.Conflict));
        var unconfigured = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartBackupAsync(h.Lease));
        Assert.Equal("capability_not_configured", unconfigured.ProblemType);
        Assert.Equal(3, h.Requests.Count(request => request == "POST /api/v2/admin/backups"));
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("500")]
    [InlineData("401")]
    [InlineData("malformed")]
    public async Task ACommandWithoutAnAcknowledgementIsNeverReplayed(string failure)
    {
        using var h = new Harness();
        h.Kit.Http.Respond = (_, _) => failure switch
        {
            "transport" => throw new HttpRequestException("synthetic transport failure"),
            "500" => Task.FromResult(BackupJson.Json(BackupJson.Problem("internal_error", 500), HttpStatusCode.InternalServerError)),
            "401" => Task.FromResult(BackupJson.Json(BackupJson.Problem("authentication_required", 401), HttpStatusCode.Unauthorized)),
            _ => Task.FromResult(BackupJson.Json("{\"id\":", HttpStatusCode.Accepted)),
        };
        if (failure == "401") await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartBackupAsync(h.Lease));
        else await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.StartBackupAsync(h.Lease));
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/backups");
    }

    [Theory]
    [InlineData(408, "[]")]
    [InlineData(500, "null")]
    [InlineData(502, "[]")]
    [InlineData(500, "\"failure\"")]
    [InlineData(502, "false")]
    [InlineData(500, "42")]
    [InlineData(502, "{invalid")]
    public async Task MalformedProblemBodiesDoNotLoseUnknownCommandOutcomes(int status, string body)
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json(body, (HttpStatusCode)status));
        await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.StartBackupAsync(h.Lease));
        Assert.Single(h.Requests, request => request == "POST /api/v2/admin/backups");

        var error = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.ListAsync(h.Lease));
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task DeleteTreatsAMissingBackupAsGoneAndRejectsForeignIdentifiers()
    {
        using var h = new Harness();
        h.Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(BackupJson.Problem("not_found", 404)) });
        await h.Api.DeleteBackupAsync(h.Lease, BackupJson.BackupId);
        h.Respond(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await h.Api.DeleteBackupAsync(h.Lease, BackupJson.BackupId);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.DeleteBackupAsync(h.Lease, "20261115T040000Z-nightly/../x"));
        Assert.All(h.Requests, request => Assert.Equal("DELETE /api/v2/admin/backups/" + BackupJson.BackupId, request));
    }

    [Fact]
    public async Task DownloadLinksValidateScopeNamesAndTheSingleRestoreSet()
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json("""{"items":[{"name":"db.dump","size_bytes":4,"url":"/api/v2/admin/backups/20261115T040000Z-nightly/objects/db.dump?exp=1&sig=abc","expires_at":"2026-11-16T12:15:00.000Z"}]}"""));
        var links = await h.Api.CreateDownloadLinksAsync(h.Lease, BackupJson.BackupId, "full");
        Assert.Equal("db.dump", Assert.Single(links).Name);
        Assert.Contains("\"scope\":\"full\"", h.Kit.Http.Bodies.Last());
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.CreateDownloadLinksAsync(h.Lease, BackupJson.BackupId, "restore-set")); // wrong name for the scope
        h.Respond(_ => BackupJson.Json("""{"items":[{"name":"evil.sh","size_bytes":4,"url":"/x","expires_at":"2026-11-16T12:15:00.000Z"}]}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.CreateDownloadLinksAsync(h.Lease, BackupJson.BackupId, "full"));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.CreateDownloadLinksAsync(h.Lease, BackupJson.BackupId, "everything"));
    }

    [Fact]
    public async Task S3TestSendsTheSecretOnlyWhenTypedAndRedactsItFromTheAnswer()
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json("""{"ok":false,"step":"credentials","message":"Key AKIASYNTHETIC with secret hunter2-secret was refused"}"""));
        var result = await h.Api.TestS3Async(h.Lease, new AdminBackupS3Test { Endpoint = "https://s3.example.invalid", Bucket = "b", AccessKey = "AKIASYNTHETIC" });
        Assert.DoesNotContain("secret_key", h.Kit.Http.Bodies.Last());
        Assert.DoesNotContain("AKIASYNTHETIC", result.Message);
        await h.Api.TestS3Async(h.Lease, new AdminBackupS3Test { Endpoint = "https://s3.example.invalid", Bucket = "b", SecretKey = "hunter2-secret" });
        Assert.Contains("\"secret_key\":\"hunter2-secret\"", h.Kit.Http.Bodies.Last());
        var second = await h.Api.TestS3Async(h.Lease, new AdminBackupS3Test { Endpoint = "https://s3.example.invalid", Bucket = "b", SecretKey = "hunter2-secret" });
        Assert.DoesNotContain("hunter2-secret", second.Message);
        Assert.False(second.Ok);
    }

    [Fact]
    public async Task KeyAcknowledgementNeedsTheOfficialShapeAndReportsAChangedKey()
    {
        using var h = new Harness();
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.AcknowledgeKeyAsync(h.Lease, "ABCD-EF01-2345-6789"));
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("conflict", 409), HttpStatusCode.Conflict));
        var changed = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.AcknowledgeKeyAsync(h.Lease, "abcd-ef01-2345-6789"));
        Assert.Equal(409, changed.StatusCode);
        h.Respond(_ => BackupJson.Json("""{"key_check":"abcd-ef01-2345-6789"}"""));
        await h.Api.AcknowledgeKeyAsync(h.Lease, "abcd-ef01-2345-6789");
    }

    [Fact]
    public async Task UnauthenticatedPreviewIsAskedForOnlyWithConsent()
    {
        using var h = new Harness();
        h.Respond(request => request.RequestUri!.Query.Contains("accept_unauthenticated=true")
            ? BackupJson.Json(BackupJson.RestoreSet(BackupJson.Section("settings", BackupJson.Item("general")), authenticated: false))
            : BackupJson.Json(BackupJson.Problem("backup_unauthenticated", 409), HttpStatusCode.Conflict));
        var source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);
        var refused = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.GetRestoreSetAsync(h.Lease, source, false));
        Assert.Equal("backup_unauthenticated", refused.ProblemType);
        var set = await h.Api.GetRestoreSetAsync(h.Lease, source, true);
        Assert.False(set.Authenticated);
        Assert.Equal(["GET /api/v2/admin/backups/" + BackupJson.BackupId + "/restore-set",
            "GET /api/v2/admin/backups/" + BackupJson.BackupId + "/restore-set?accept_unauthenticated=true"], h.Requests);
        // A preview for another source is never accepted for this page.
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetRestoreSetAsync(h.Lease, AdminBackupRestoreSourceRef.Backup(BackupJson.OtherBackupId), true));
    }

    private static AdminBackupRestoreSelection Settings() => new() { Settings = ["general"] };

    [Fact]
    public async Task RestoreStartNeedsItsOwnJobInTheLocationHeader()
    {
        using var h = new Harness();
        var source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);
        h.Respond(_ => BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-1"));
        var started = await h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false);
        Assert.Equal("job-1", started.Job.Id);
        var body = h.Kit.Http.Bodies.Last();
        Assert.Contains("\"digest\":\"" + BackupJson.Digest + "\"", body);
        Assert.DoesNotContain("accept_unauthenticated", body);

        h.Respond(_ => BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted));
        var missing = await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), true));
        Assert.Equal("job-1", missing.JobId);
        Assert.Contains("\"accept_unauthenticated\":true", h.Kit.Http.Bodies.Last());
        Assert.True(h.Api.AcknowledgeRestoreOutcome(h.Lease, source));

        h.Respond(_ => BackupJson.Json(BackupJson.Job(), HttpStatusCode.Accepted, "/api/v2/admin/jobs/job-9"));
        var mismatched = await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false));
        Assert.Equal("job-9", mismatched.JobId);
        Assert.True(h.Api.AcknowledgeRestoreOutcome(h.Lease, source));

        h.Kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic");
        var unknown = await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false));
        Assert.Null(unknown.JobId);
        Assert.Equal(4, h.Requests.Count(request => request.StartsWith("POST /api/v2/admin/backups/" + BackupJson.BackupId + "/restore", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UnconfirmedRestoresAreScopedToTheirSourceAndAuthorityEpoch()
    {
        using var harness = new Harness();
        var source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);
        harness.Kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic");
        await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => harness.Api.StartRestoreAsync(harness.Lease, source, BackupJson.Digest, Settings(), false));
        Assert.True(harness.Api.HasUnconfirmedRestore(harness.Lease, source));
        Assert.False(harness.Api.HasUnconfirmedRestore(harness.Lease, AdminBackupRestoreSourceRef.Backup(BackupJson.OtherBackupId)));
        harness.Authority.Retire();
        using var replacement = harness.Authority.Capture();
        Assert.False(harness.Api.HasUnconfirmedRestore(replacement, source));
    }

    [Fact]
    public async Task RestoreConflictsKeepTheirTypesAndTheRunningJob()
    {
        using var h = new Harness();
        var source = AdminBackupRestoreSourceRef.Backup(BackupJson.BackupId);
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("restore_set_changed", 409), HttpStatusCode.Conflict));
        var changed = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false));
        Assert.Equal("restore_set_changed", changed.ProblemType);
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("conflict", 409), HttpStatusCode.Conflict, "/api/v2/admin/jobs/job-7"));
        var running = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false));
        Assert.Equal("job-7", AdminBackupsApi.JobIdFromLocation(running.Location));
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("validation_failed", 422), HttpStatusCode.UnprocessableEntity));
        await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, Settings(), false));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.StartRestoreAsync(h.Lease, source, "not-a-digest", Settings(), false));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.StartRestoreAsync(h.Lease, source, BackupJson.Digest, new(), false));
    }

    [Theory]
    [InlineData("/api/v2/admin/jobs/job-1", "job-1")]
    [InlineData("https://silo.test/api/v2/admin/jobs/job%2D2?x=1", "job-2")]
    [InlineData("/api/v2/admin/jobs/", null)]
    [InlineData("/api/v2/admin/jobs/a%2Fb", null)]
    [InlineData("/elsewhere/7", null)]
    public void JobIdFromLocationAcceptsOnlyOfficialJobIds(string location, string? expected) => Assert.Equal(expected, AdminBackupsApi.JobIdFromLocation(location));

    [Fact]
    public async Task UploadCreateValidatesBoundsAndTheAnsweredShape()
    {
        using var h = new Harness();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Api.CreateUploadAsync(h.Lease, 0, null, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Api.CreateUploadAsync(h.Lease, 10, 512 * 1024, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Api.CreateUploadAsync(h.Lease, 10, 32L << 20, null));
        h.Respond(_ => BackupJson.Json(BackupJson.Upload(), HttpStatusCode.Created));
        var upload = await h.Api.CreateUploadAsync(h.Lease, 3L << 20, null, null);
        Assert.Equal(3, upload.PartCount);
        Assert.Equal("""{"size_bytes":3145728}""", h.Kit.Http.Bodies.Last());
        h.Respond(_ => BackupJson.Json(BackupJson.Problem("upload_limit_reached", 409), HttpStatusCode.Conflict));
        var limit = await Assert.ThrowsAsync<AdminBackupApiException>(() => h.Api.CreateUploadAsync(h.Lease, 3L << 20, null, null));
        Assert.Equal("upload_limit_reached", limit.ProblemType);
        h.Respond(_ => BackupJson.Json(BackupJson.Upload(size: 99), HttpStatusCode.Created));
        await Assert.ThrowsAsync<AdminBackupOutcomeUnknownException>(() => h.Api.CreateUploadAsync(h.Lease, 3L << 20, null, null)); // answers for another size
    }

    [Fact]
    public async Task PartsAreExactOctetStreamsOfTheirSlot()
    {
        using var h = new Harness();
        string? contentType = null;
        long? length = null;
        h.Kit.Http.Respond = (request, _) =>
        {
            contentType = request.Content?.Headers.ContentType?.MediaType;
            length = request.Content?.Headers.ContentLength;
            return Task.FromResult(BackupJson.Json(BackupJson.Upload(received: 1)));
        };
        var upload = new AdminBackupUpload { Id = BackupJson.UploadId, Status = "uploading", SizeBytes = 3L << 20, PartSize = 1L << 20, PartCount = 3 };
        await h.Api.PutPartAsync(h.Lease, upload, 2, new byte[1 << 20]);
        Assert.Equal("application/octet-stream", contentType);
        Assert.Equal(1L << 20, length);
        Assert.Equal("PUT /api/v2/admin/backups/uploads/" + BackupJson.UploadId + "/parts/2", h.Requests.Last());
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.PutPartAsync(h.Lease, upload, 2, new byte[10]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Api.PutPartAsync(h.Lease, upload, 3, new byte[1 << 20]));
    }

    [Fact]
    public async Task JobReadsMustAnswerForTheRequestedJob()
    {
        using var h = new Harness();
        h.Respond(_ => BackupJson.Json(BackupJson.Job(id: "job-2")));
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetJobAsync(h.Lease, "job-1"));
        h.Respond(_ => BackupJson.Json("{\"items\":[" + BackupJson.Job() + "," + BackupJson.Job(id: "job-x").Replace("backup_restore\"", "library_scan\"") + "]}"));
        var jobs = await h.Api.ListRestoreJobsAsync(h.Lease);
        Assert.Equal("job-1", Assert.Single(jobs).Id);
        Assert.Equal("GET /api/v2/admin/jobs?kind=backup_restore&limit=20", h.Requests.Last());
    }
}
