using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>A storage transition or cancellation may have reached the server, but no usable acknowledgement arrived. It is never resent.</summary>
public sealed class AdminStorageCommandOutcomeUnknownException() : Exception("The storage command outcome is unknown.");

/// <summary>
/// Configure › Server › Storage wire operations beyond the shared settings snapshot (official
/// <c>internal/apiv2/admin_storage_transition.go</c>, <c>admin_server_status.go</c> and <c>admin_task_jobs.go</c>),
/// kept separate from the Monitor API. Every call is bound to a captured admin lease, has a finite deadline that
/// includes the response body, and a byte bound that also applies to problem bodies. There is no authentication
/// replay and no automatic retry: a transition or cancellation with an ambiguous result is reported unknown.
/// Settings values and credentials are never retained or echoed from here.
/// </summary>
public sealed class AdminStorageApi(SiloApiClient client)
{
    public const int MaxResponseBytes = 256 * 1024;
    public const int MaxTextLength = 500;
    public const int MaxIdLength = 128;
    /// <summary>Only the newest storage_transition job is shown (official <c>transitionJobs.data?.[0]</c>).</summary>
    public const int JobLimit = 1;
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);
    // A probe asks the server to reach the configured S3 stores; give it more room than a plain read.
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);
    public static IReadOnlyList<string> Policies { get; } = Array.AsReadOnly(new[] { "start_fresh", "preserve_uploads", "migrate_all" });

    public async Task<AdminArtworkStorageStatus?> GetArtworkStorageStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<ServerStatusWire>(lease, HttpMethod.Get, "/api/v2/admin/server/status", null, timeout.Token).ConfigureAwait(false);
        if (body.ArtworkStorage is not { } storage) return null;
        // A missing status_known is unknown, never "unlocked".
        return new(storage.StatusKnown == true, storage.Locked == true, storage.PrivateLocked == true, Backend(storage.Backend));
    }

    public async Task<AdminStorageTransitionCapabilities> GetTransitionCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<CapabilitiesWire>(lease, HttpMethod.Get, "/api/v2/admin/storage-transitions/capabilities", null, timeout.Token).ConfigureAwait(false);
        if (body.State is not { Length: > 0 and <= 64 } state) throw new InvalidDataException("Invalid storage transition capabilities.");
        return new(state, body.Allowed);
    }

    /// <param name="probe">True checks reachability of the current S3 source (transition review); false reads recovery state only.</param>
    public async Task<AdminStorageSourceHealth> GetSourceHealthAsync(MonitorAuthorityLease lease, bool probe, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, probe ? ProbeDeadline : ReadDeadline);
        var body = await ReadAsync<SourceHealthWire>(lease, HttpMethod.Get,
            "/api/v2/admin/storage-transitions/source-health?probe=" + (probe ? "true" : "false"), null, timeout.Token).ConfigureAwait(false);
        if (body.Reachable == null || body.RecoveryPending == null) throw new InvalidDataException("Invalid storage source health.");
        return new(Backend(body.CurrentBackend), body.Reachable.Value, body.PublicConfigured == true, body.PublicReachable == true,
            body.PrivateConfigured == true, body.PrivateReachable == true, body.ReachabilityProbed == true, Bound(body.Message),
            body.RecoveryPending.Value, Bound(body.RecoveryState), Bound(body.RecoveryError), Math.Clamp(body.RecoveryProgressPercent ?? 0, 0, 100));
    }

    /// <summary>The newest storage transition job, or null when none is retained.</summary>
    public async Task<AdminStorageTransitionJob?> GetLatestTransitionAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, ReadDeadline);
        var body = await ReadAsync<JobsWire>(lease, HttpMethod.Get, $"/api/v2/admin/jobs?kind=storage_transition&limit={JobLimit}", null, timeout.Token).ConfigureAwait(false);
        if (body.Items == null) throw new InvalidDataException("Invalid admin job list.");
        // The request asks for one item; a larger page cannot be shown truthfully as "the latest".
        if (body.Items.Length > JobLimit) throw new InvalidDataException("The admin job list exceeds its requested size.");
        return body.Items.Length == 0 ? null : Job(body.Items[0]);
    }

    /// <summary>
    /// Queues one managed transition with the reviewed policy and values. The values may carry staged credentials;
    /// they are sent once and never retained here. 202 must name the queued job, otherwise the outcome is unknown.
    /// </summary>
    public async Task<string> CreateTransitionAsync(MonitorAuthorityLease lease, string policy, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        if (!Policies.Contains(policy)) throw new ArgumentException("Unsupported storage transition policy.", nameof(policy));
        if (values.Count == 0 || !values.ContainsKey("artwork.storage_backend") || values.Keys.Any(key => !AdminStorageRules.TransitionKeys.Contains(key)))
            throw new ArgumentException("Invalid storage transition values.", nameof(values));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CommandDeadline);
        AcceptedWire body;
        try
        {
            body = (await client.MonitorLogsSendAsync<AcceptedWire>(lease, HttpMethod.Post, "/api/v2/admin/storage-transitions",
                new TransitionWire(policy, new Dictionary<string, string>(values, StringComparer.Ordinal)), ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminStorageCommandOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminStorageCommandOutcomeUnknownException(); }
        if (body.Job is not { } job || !IsId(job.Id) || !string.Equals(job.Kind, "storage_transition", StringComparison.Ordinal))
            throw new AdminStorageCommandOutcomeUnknownException();
        return job.Id!;
    }

    /// <summary>Requests cancellation of exactly the captured job. The answer must describe that job, otherwise the outcome is unknown.</summary>
    public async Task<AdminStorageTransitionJob> CancelJobAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        if (!IsId(jobId)) throw new ArgumentException("Invalid job.", nameof(jobId));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, CommandDeadline);
        JobWire body;
        try
        {
            body = (await client.MonitorLogsSendAsync<JobWire>(lease, HttpMethod.Post, "/api/v2/admin/jobs/" + Uri.EscapeDataString(jobId) + "/cancel",
                null, ct: timeout.Token, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body;
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminStorageCommandOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminStorageCommandOutcomeUnknownException(); }
        try
        {
            var job = Job(body);
            if (!string.Equals(job.Id, jobId, StringComparison.Ordinal)) throw new AdminStorageCommandOutcomeUnknownException();
            return job;
        }
        catch (InvalidDataException) { throw new AdminStorageCommandOutcomeUnknownException(); }
    }

    private static AdminStorageTransitionJob Job(JobWire? job)
    {
        if (job == null || !IsId(job.Id) || job.State is not { Length: > 0 and <= 32 } state || !string.Equals(job.Kind, "storage_transition", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid storage transition job.");
        var status = state switch { "succeeded" => "completed", "canceled" => "cancelled", "canceling" => "running", _ => state };
        var result = job.StorageTransitionResult;
        long? verified = result?.VerifiedObjects is { } count && count >= 0 ? count : null;
        return new(job.Id!, status, Bound(result?.Phase), verified, Bound(result?.FailureCategory), result?.ManualRestartRequired == true);
    }

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try { return (await client.MonitorLogsSendAsync<T>(lease, method, path, body, ct: ct, maxBytes: MaxResponseBytes).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error); }
    }

    private static bool IsId(string? id) => id is { Length: > 0 and <= MaxIdLength } && !id.Any(char.IsControl) && !string.IsNullOrWhiteSpace(id);
    private static string Backend(string? backend) => backend is "s3" or "local" ? backend : "";
    private static string? Bound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = LocalLog.RedactDiagnosticText(value.Trim());
        return text.Length <= MaxTextLength ? text : text[..MaxTextLength] + "…";
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    private static string SafeCode(string? code) => code is "permission_denied" or "profile_unverified" or "profile_verification_required"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "conflict" or "job_not_cancelable"
        or "capability_disabled" or "capability_not_configured" or "capability_unsupported" or "rate_limited" or "authentication_required"
        ? code : "unknown";
    // Problem detail can name endpoints or buckets; the page shows its own localized message instead.
    private static ApiException Sanitize(ApiException error) => new(SafeCode(error.ErrorCode), "Storage request failed.", error.StatusCode)
    {
        ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null
    };

    private sealed record TransitionWire(string Policy, Dictionary<string, string> Values)
    {
        // Carries staged credentials; never printed.
        public override string ToString() => nameof(TransitionWire);
    }
    private sealed record ServerStatusWire { public ArtworkStorageWire? ArtworkStorage { get; init; } }
    private sealed record ArtworkStorageWire
    {
        public string? Backend { get; init; }
        public bool? Locked { get; init; }
        public bool? StatusKnown { get; init; }
        public bool? PrivateLocked { get; init; }
    }
    private sealed record CapabilitiesWire { public string? State { get; init; } public bool? Allowed { get; init; } }
    private sealed record SourceHealthWire
    {
        public string? CurrentBackend { get; init; }
        public bool? Reachable { get; init; }
        public bool? PublicConfigured { get; init; }
        public bool? PublicReachable { get; init; }
        public bool? PrivateConfigured { get; init; }
        public bool? PrivateReachable { get; init; }
        public bool? ReachabilityProbed { get; init; }
        public string? Message { get; init; }
        public bool? RecoveryPending { get; init; }
        public string? RecoveryState { get; init; }
        public string? RecoveryError { get; init; }
        [JsonPropertyName("recovery_progress_percent")] public int? RecoveryProgressPercent { get; init; }
    }
    private sealed record JobsWire { public JobWire?[]? Items { get; init; } }
    private sealed record JobWire
    {
        public string? Id { get; init; }
        public string? Kind { get; init; }
        public string? State { get; init; }
        public TransitionResultWire? StorageTransitionResult { get; init; }
    }
    private sealed record TransitionResultWire
    {
        public string? Phase { get; init; }
        public long? VerifiedObjects { get; init; }
        public string? FailureCategory { get; init; }
        public bool? ManualRestartRequired { get; init; }
    }
    private sealed record AcceptedWire { public JobWire? Job { get; init; } }
}
