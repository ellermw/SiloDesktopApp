using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Storage wire contracts (official admin_storage_transition.go, admin_server_status.go, admin_task_jobs.go at 8f93594c). Synthetic data only.</summary>
public sealed class AdminStorageApiTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly MonitorAuthorityLease Lease;
        internal readonly AdminStorageApi Api;
        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Lease = Authority.Capture();
            Api = new AdminStorageApi(Kit.Client);
        }
        internal void Reply(string json, HttpStatusCode status = HttpStatusCode.OK) => Kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json, status));
        internal string[] Requests => Kit.Http.Requests.ToArray();
        public void Dispose() { Lease.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string RunningJob = """{"items":[{"id":"job-1","kind":"storage_transition","state":"canceling","terminal":false,"cancelable":true,"created_at":"2026-11-16T00:00:00Z","storage_transition_result":{"phase":"copying","verified_objects":12,"manual_restart_required":false}}]}""";

    [Fact]
    public async Task StatusReadsOnlyArtworkStorageAndTreatsMissingKnownAsUnknown()
    {
        using var h = new Harness();
        h.Reply("""{"restart_required":true,"artwork_storage":{"backend":"s3","locked":true,"status_known":true,"private_locked":true}}""");
        var status = await h.Api.GetArtworkStorageStatusAsync(h.Lease);
        Assert.Equal(new(true, true, true, "s3"), status);
        Assert.Equal(["GET /api/v2/admin/server/status"], h.Requests);
        h.Reply("""{"artwork_storage":{"locked":false}}""");
        Assert.Equal(new(false, false, false, ""), await h.Api.GetArtworkStorageStatusAsync(h.Lease));
        h.Reply("""{"restart_required":false}""");
        Assert.Null(await h.Api.GetArtworkStorageStatusAsync(h.Lease));
    }

    [Fact]
    public async Task CapabilitiesRequireAStateAndRespectAllowed()
    {
        using var h = new Harness();
        h.Reply("""{"revision":"r","state":"available","start_fresh":true}""");
        Assert.True((await h.Api.GetTransitionCapabilitiesAsync(h.Lease)).ManagedTransitionsAvailable);
        h.Reply("""{"state":"available","allowed":false}""");
        Assert.False((await h.Api.GetTransitionCapabilitiesAsync(h.Lease)).ManagedTransitionsAvailable);
        h.Reply("""{"state":"not_configured"}""");
        Assert.False((await h.Api.GetTransitionCapabilitiesAsync(h.Lease)).ManagedTransitionsAvailable);
        h.Reply("""{}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetTransitionCapabilitiesAsync(h.Lease));
    }

    [Fact]
    public async Task SourceHealthSendsTheProbeFlagAndBoundsItsFields()
    {
        using var h = new Harness();
        h.Reply($$"""{"current_backend":"s3","reachable":false,"private_configured":true,"message":"{{new string('m', 900)}}","recovery_pending":true,"recovery_state":"blocked","recovery_error":"x","recovery_progress_percent":140}""");
        var health = await h.Api.GetSourceHealthAsync(h.Lease, probe: true);
        await h.Api.GetSourceHealthAsync(h.Lease, probe: false);
        Assert.Equal(["GET /api/v2/admin/storage-transitions/source-health?probe=true", "GET /api/v2/admin/storage-transitions/source-health?probe=false"], h.Requests);
        Assert.False(health.Reachable);
        Assert.True(health.PrivateConfigured);
        Assert.Equal(100, health.RecoveryProgressPercent);
        Assert.Equal(AdminStorageApi.MaxTextLength + 1, health.Message!.Length);
        h.Reply("""{"current_backend":"s3"}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetSourceHealthAsync(h.Lease, probe: false));
    }

    [Fact]
    public async Task LatestJobIsMappedLikeTheOfficialAdapterAndBounded()
    {
        using var h = new Harness();
        h.Reply(RunningJob);
        var job = await h.Api.GetLatestTransitionAsync(h.Lease);
        Assert.Equal(["GET /api/v2/admin/jobs?kind=storage_transition&limit=1"], h.Requests);
        Assert.Equal(new("job-1", "running", "copying", 12, null, false), job);
        h.Reply("""{"items":[{"id":"job-2","kind":"storage_transition","state":"canceled","storage_transition_result":{"phase":"canceled","verified_objects":-4}}]}""");
        Assert.Equal(new("job-2", "cancelled", "canceled", null, null, false), await h.Api.GetLatestTransitionAsync(h.Lease));
        h.Reply("""{"items":[]}""");
        Assert.Null(await h.Api.GetLatestTransitionAsync(h.Lease));
        h.Reply("""{"items":[{"id":"a","kind":"storage_transition","state":"queued"},{"id":"b","kind":"storage_transition","state":"queued"}]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetLatestTransitionAsync(h.Lease));
        h.Reply("""{"items":[{"id":"x","kind":"backup_restore","state":"queued"}]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => h.Api.GetLatestTransitionAsync(h.Lease));
    }

    [Fact]
    public async Task OversizedResponsesAreRejectedBeforeParsing()
    {
        using var h = new Harness();
        h.Reply("{\"items\":[],\"pad\":\"" + new string('x', AdminStorageApi.MaxResponseBytes) + "\"}");
        await Assert.ThrowsAnyAsync<Exception>(() => h.Api.GetLatestTransitionAsync(h.Lease));
        h.Reply("{\"title\":\"" + new string('x', AdminStorageApi.MaxResponseBytes) + "\"}", HttpStatusCode.BadRequest);
        await Assert.ThrowsAnyAsync<Exception>(() => h.Api.GetArtworkStorageStatusAsync(h.Lease));
    }

    [Fact]
    public async Task TransitionIsSentOnceWithTheExactReviewedCommand()
    {
        using var h = new Harness();
        h.Reply("""{"job":{"id":"job-9","kind":"storage_transition","state":"queued"},"preflight":{"current_backend":"local","target_backend":"s3","policy":"preserve_uploads","warnings":[]}}""", HttpStatusCode.Accepted);
        var values = new Dictionary<string, string> { ["artwork.storage_backend"] = "s3", ["s3.public_bucket"] = "media", ["s3.public_secret_key"] = "synthetic-secret" };
        Assert.Equal("job-9", await h.Api.CreateTransitionAsync(h.Lease, "preserve_uploads", values));
        Assert.Equal(["POST /api/v2/admin/storage-transitions"], h.Requests);
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Single());
        Assert.Equal("preserve_uploads", body.RootElement.GetProperty("policy").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("values").EnumerateObject().Count());
        Assert.Equal("media", body.RootElement.GetProperty("values").GetProperty("s3.public_bucket").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.GatewayTimeout, "{}")]
    [InlineData(HttpStatusCode.Accepted, "{}")]
    [InlineData(HttpStatusCode.Accepted, """{"job":{"id":"j","kind":"backup_restore","state":"queued"}}""")]
    public async Task AmbiguousTransitionResultsAreUnknownAndNeverResent(HttpStatusCode status, string json)
    {
        using var h = new Harness();
        h.Reply(json, status);
        await Assert.ThrowsAsync<AdminStorageCommandOutcomeUnknownException>(() =>
            h.Api.CreateTransitionAsync(h.Lease, "start_fresh", new Dictionary<string, string> { ["artwork.storage_backend"] = "local", ["artwork.local_path"] = "/srv" }));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task TransportFailureAfterSendIsUnknownWithoutEchoingTheCredential()
    {
        using var h = new Harness();
        h.Kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic-secret echoed"));
        var error = await Assert.ThrowsAsync<AdminStorageCommandOutcomeUnknownException>(() =>
            h.Api.CreateTransitionAsync(h.Lease, "migrate_all", new Dictionary<string, string> { ["artwork.storage_backend"] = "s3", ["s3.public_secret_key"] = "synthetic-secret" }));
        Assert.DoesNotContain("synthetic-secret", error.ToString());
    }

    [Fact]
    public async Task KnownRejectionsAreSanitizedAndInvalidCommandsAreNeverSent()
    {
        using var h = new Harness();
        h.Reply("""{"type":"https://silo.dev/problems/conflict","title":"Conflict","status":409,"detail":"bucket s3://private-name busy","code":"conflict"}""", HttpStatusCode.Conflict);
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            h.Api.CreateTransitionAsync(h.Lease, "start_fresh", new Dictionary<string, string> { ["artwork.storage_backend"] = "local", ["artwork.local_path"] = "/srv" }));
        Assert.Equal(409, error.StatusCode);
        Assert.DoesNotContain("private-name", error.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.CreateTransitionAsync(h.Lease, "copy_everything", new Dictionary<string, string> { ["artwork.storage_backend"] = "s3" }));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.CreateTransitionAsync(h.Lease, "start_fresh", new Dictionary<string, string> { ["artwork.storage_backend"] = "s3", ["metadata.cache_images"] = "false" }));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.CreateTransitionAsync(h.Lease, "start_fresh", new Dictionary<string, string> { ["s3.public_bucket"] = "x" }));
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task CancelTargetsTheCapturedJobAndRequiresItsAcknowledgement()
    {
        using var h = new Harness();
        h.Reply("""{"id":"job/1","kind":"storage_transition","state":"canceling"}""", HttpStatusCode.Accepted);
        var job = await h.Api.CancelJobAsync(h.Lease, "job/1");
        Assert.Equal("running", job.Status);
        Assert.Equal(["POST /api/v2/admin/jobs/job%2F1/cancel"], h.Requests);
        h.Reply("""{"id":"other","kind":"storage_transition","state":"canceled"}""");
        await Assert.ThrowsAsync<AdminStorageCommandOutcomeUnknownException>(() => h.Api.CancelJobAsync(h.Lease, "job/1"));
        h.Reply("""{"type":"https://silo.dev/problems/job_not_cancelable","status":409,"code":"job_not_cancelable"}""", HttpStatusCode.Conflict);
        await Assert.ThrowsAsync<ApiException>(() => h.Api.CancelJobAsync(h.Lease, "job/1"));
        await Assert.ThrowsAsync<ArgumentException>(() => h.Api.CancelJobAsync(h.Lease, ""));
        Assert.Equal(3, h.Requests.Length);
    }

    [Fact]
    public async Task RetiredLeaseSendsNothing()
    {
        using var h = new Harness();
        h.Authority.Retire();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Api.CreateTransitionAsync(h.Lease, "start_fresh", new Dictionary<string, string> { ["artwork.storage_backend"] = "local", ["artwork.local_path"] = "/srv" }));
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    public async Task S3ConnectionChecksRedactOverlappingCredentialsLongestFirst(string scope)
    {
        using var h = new Harness();
        h.Reply("""{"success":false,"message":"Denied for minio-long-password / minio"}""");
        var settings = new AdminServerSettingsApi(h.Kit.Client);
        var access = $"s3.{scope}_access_key";
        var secret = $"s3.{scope}_secret_key";
        var result = await settings.CheckConnectionAsync(h.Lease, "s3_" + scope,
            new Dictionary<string, string> { [access] = "minio", [secret] = "minio-long-password" }, [access, secret]);
        Assert.Equal("Denied for <redacted> / <redacted>", result.Message);
    }

    [Fact]
    public async Task S3ConnectionChecksUseTheirKindAndNeverEchoSubmittedCredentials()
    {
        using var h = new Harness();
        h.Reply("""{"success":false,"message":"denied for synthetic-access-id / synthetic-secret"}""");
        var settings = new AdminServerSettingsApi(h.Kit.Client);
        var result = await settings.CheckConnectionAsync(h.Lease, "s3_private",
            new Dictionary<string, string> { ["s3.private_bucket"] = "b", ["s3.private_access_key"] = "synthetic-access-id", ["s3.private_secret_key"] = "synthetic-secret" },
            ["s3.private_access_key", "s3.private_secret_key"]);
        Assert.Equal(["POST /api/v2/admin/settings/check/s3_private"], h.Requests);
        Assert.DoesNotContain("synthetic-secret", result.Message);
        Assert.DoesNotContain("synthetic-access-id", result.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => settings.CheckConnectionAsync(h.Lease, "s3_public", new Dictionary<string, string> { ["s3.private_bucket"] = "b" }, []));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.CheckConnectionAsync(h.Lease, "meilisearch", new Dictionary<string, string>(), []));
        Assert.Single(h.Requests);
    }
}
