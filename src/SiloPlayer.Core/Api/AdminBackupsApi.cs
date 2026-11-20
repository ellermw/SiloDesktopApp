using System.Text.Json;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

/// <summary>
/// A command may have reached the server, but no valid acknowledgement arrived. It is
/// never resent; the caller reconciles by reading state. <see cref="JobId"/> is set only
/// when a restore's own <c>Location</c> named its job.
/// </summary>
public sealed class AdminBackupOutcomeUnknownException(string operation, string? jobId = null)
    : Exception("The " + operation + " outcome is unknown.")
{
    public string Operation { get; } = operation;
    public string? JobId { get; } = jobId;
}

/// <summary>The started restore job and the job id its <c>Location</c> header named.</summary>
public sealed record AdminBackupRestoreStarted(AdminBackupRestoreJob Job);

/// <summary>
/// <c>/api/v2/admin/backups/*</c> and the backup_restore admin jobs (official
/// internal/apiv2/admin_backup*.go at 92ee051). Every call is lease-bound and bounded by
/// a body-inclusive deadline. Commands carry no token refresh and are never replayed;
/// only the naturally idempotent part PUT may be authenticated again. Responses are
/// validated before they are returned.
/// </summary>
public sealed class AdminBackupsApi(SiloApiClient client)
{
    private readonly object _restoreGate = new();
    private readonly Dictionary<AdminBackupRestoreSourceRef, (Guid Id, bool Pending)> _restoreAttempts = [];
    private AdminMonitorAuthority? _restoreAuthority;
    private long _restoreEpoch;

    public bool HasUnconfirmedRestore(MonitorAuthorityLease lease, AdminBackupRestoreSourceRef source)
    {
        lock (_restoreGate) return ReferenceEquals(_restoreAuthority, lease.Owner) && _restoreEpoch == lease.Epoch && _restoreAttempts.ContainsKey(source);
    }

    public bool AcknowledgeRestoreOutcome(MonitorAuthorityLease lease, AdminBackupRestoreSourceRef source)
    {
        if (!lease.IsCurrent) return false;
        lock (_restoreGate)
        {
            if (!ReferenceEquals(_restoreAuthority, lease.Owner) || _restoreEpoch != lease.Epoch || !_restoreAttempts.TryGetValue(source, out var attempt)) return true;
            if (attempt.Pending) return false;
            _restoreAttempts.Remove(source);
            return true;
        }
    }

    private async Task<T> RestoreOnceAsync<T>(MonitorAuthorityLease lease, AdminBackupRestoreSourceRef source, Func<Task<T>> send)
    {
        lease.ThrowIfRetired();
        var attempt = Guid.NewGuid();
        lock (_restoreGate)
        {
            if (!ReferenceEquals(_restoreAuthority, lease.Owner) || _restoreEpoch != lease.Epoch)
            {
                _restoreAttempts.Clear();
                _restoreAuthority = lease.Owner;
                _restoreEpoch = lease.Epoch;
            }
            if (_restoreAttempts.ContainsKey(source)) throw new AdminBackupOutcomeUnknownException("restore");
            _restoreAttempts[source] = (attempt, true);
        }
        var settled = false;
        try
        {
            var result = await send().ConfigureAwait(false);
            settled = true;
            return result;
        }
        catch (ApiException refused) when (refused.StatusCode is < 500 and not 408)
        {
            settled = true;
            throw;
        }
        finally
        {
            lock (_restoreGate)
            {
                if (_restoreAttempts.TryGetValue(source, out var current) && current.Id == attempt)
                {
                    if (settled) _restoreAttempts.Remove(source);
                    else _restoreAttempts[source] = (attempt, false);
                }
            }
        }
    }

    internal static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);
    /// <summary>Official RESTORE_SET_TIMEOUT_MS: reading a large restore set decrypts and compares every account.</summary>
    internal static readonly TimeSpan RestoreSetDeadline = TimeSpan.FromSeconds(180);
    /// <summary>Official S3_TEST_TIMEOUT_MS.</summary>
    internal static readonly TimeSpan S3TestDeadline = TimeSpan.FromSeconds(60);
    /// <summary>The server reads a part for up to 2 minutes and answers within a minute after.</summary>
    internal static readonly TimeSpan PartDeadline = TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(15);
    /// <summary>A completion reads up to 20 minutes before it answers (backup.UploadCheckTimeout + slack).</summary>
    internal static readonly TimeSpan CompleteDeadline = TimeSpan.FromMinutes(22) + TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    // ── Reads ────────────────────────────────────────────────────────

    public async Task<AdminBackupCapabilities> GetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await Read<AdminBackupCapabilities>(lease, "/api/v2/admin/backups/capabilities", ReadDeadline, ct).ConfigureAwait(false);
        AdminBackupValidation.Require(body);
        return body;
    }

    public async Task<AdminBackupList> ListAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await Read<AdminBackupList>(lease, "/api/v2/admin/backups", ReadDeadline, ct, 32 * 1024 * 1024).ConfigureAwait(false);
        AdminBackupValidation.Require(body);
        return body;
    }

    public async Task<IReadOnlyList<AdminBackupUpload>> ListUploadsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await Read<ItemsOf<AdminBackupUpload>>(lease, "/api/v2/admin/backups/uploads", ReadDeadline, ct).ConfigureAwait(false);
        if (body.Items == null) throw new InvalidDataException("Invalid uploads list.");
        foreach (var upload in body.Items) AdminBackupValidation.Require(upload);
        return body.Items;
    }

    /// <summary>The backup task's schedule (official useTask("backup")); null when the server has no such task.</summary>
    public async Task<DateTimeOffset?> GetNextBackupRunAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        try
        {
            var body = await Read<TaskSchedule>(lease, "/api/v2/admin/tasks/backup", ReadDeadline, ct).ConfigureAwait(false);
            if (body.Key != "backup") throw new InvalidDataException("Invalid backup task.");
            return body.NextRunAt;
        }
        catch (ApiException error) when (error.StatusCode == 404) { return null; }
    }

    /// <summary>This server's libraries, for naming collections and sections while restoring.</summary>
    public async Task<IReadOnlyList<AdminBackupLocalLibrary>> GetLibrariesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await Read<JsonElement>(lease, "/api/v2/libraries", ReadDeadline, ct).ConfigureAwait(false);
        var items = body.ValueKind == JsonValueKind.Array ? body
            : body.ValueKind == JsonValueKind.Object && body.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array ? list
            : throw new InvalidDataException("Invalid libraries list.");
        var result = new List<AdminBackupLocalLibrary>();
        foreach (var library in items.EnumerateArray())
        {
            if (library.ValueKind != JsonValueKind.Object || !library.TryGetProperty("id", out var id) || !library.TryGetProperty("name", out var name) ||
                name.ValueKind != JsonValueKind.String) continue;
            var key = id.ValueKind switch { JsonValueKind.String => id.GetString(), JsonValueKind.Number => id.GetRawText(), _ => null };
            if (!string.IsNullOrEmpty(key)) result.Add(new(key, name.GetString()!));
        }
        return result;
    }

    public async Task<AdminBackupRestoreSet> GetRestoreSetAsync(MonitorAuthorityLease lease, AdminBackupRestoreSourceRef source, bool acceptUnauthenticated,
        CancellationToken ct = default)
    {
        var path = SourcePath(source) + "/restore-set" + (acceptUnauthenticated ? "?accept_unauthenticated=true" : "");
        var body = await Read<AdminBackupRestoreSet>(lease, path, RestoreSetDeadline, ct, 64 * 1024 * 1024).ConfigureAwait(false);
        AdminBackupValidation.Require(body, source);
        return body;
    }

    public async Task<AdminBackupRestoreJob> GetJobAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        if (!AdminBackupIdentifiers.IsJobId(jobId)) throw new ArgumentException("Invalid job id.", nameof(jobId));
        var body = await Read<AdminBackupRestoreJob>(lease, "/api/v2/admin/jobs/" + Uri.EscapeDataString(jobId), ReadDeadline, ct, 32 * 1024 * 1024).ConfigureAwait(false);
        AdminBackupValidation.Require(body);
        if (body.Id != jobId) throw new InvalidDataException("The job read answered for another job.");
        return body;
    }

    /// <summary>The newest backup_restore jobs, newest first (official listRestoreJobs).</summary>
    public async Task<IReadOnlyList<AdminBackupRestoreJob>> ListRestoreJobsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await Read<ItemsOf<AdminBackupRestoreJob>>(lease, "/api/v2/admin/jobs?kind=backup_restore&limit=20", ReadDeadline, ct, 32 * 1024 * 1024).ConfigureAwait(false);
        if (body.Items == null) throw new InvalidDataException("Invalid jobs list.");
        foreach (var job in body.Items) AdminBackupValidation.Require(job);
        return body.Items.Where(job => job.Kind == "backup_restore").ToArray();
    }

    // ── Commands ─────────────────────────────────────────────────────

    /// <summary>Back up now: 202 once. Never resent.</summary>
    public async Task<AdminBackupStarted> StartBackupAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var response = await Command(lease, HttpMethod.Post, "/api/v2/admin/backups", null, "backup", ct).ConfigureAwait(false);
        var body = Parse<AdminBackupStarted>(response, "backup");
        if (response.Status != 202 || !AdminBackupIdentifiers.IsBackupId(body.Id) || body.Kind != "manual" || body.Status != "in_progress")
            throw new AdminBackupOutcomeUnknownException("backup");
        return body;
    }

    /// <summary>Deletes a confirmed backup. A 404 means it is already gone.</summary>
    public async Task DeleteBackupAsync(MonitorAuthorityLease lease, string backupId, CancellationToken ct = default)
    {
        var path = BackupPath(backupId);
        try
        {
            var response = await Command(lease, HttpMethod.Delete, path, null, "delete", ct).ConfigureAwait(false);
            if (response.Status != 204) throw new AdminBackupOutcomeUnknownException("delete");
        }
        catch (ApiException error) when (error.StatusCode == 404) { }
    }

    /// <summary>Short-lived links (15 minutes) for <paramref name="scope"/> <c>full</c> or <c>restore-set</c>.</summary>
    public async Task<IReadOnlyList<AdminBackupDownloadLink>> CreateDownloadLinksAsync(MonitorAuthorityLease lease, string backupId, string scope, CancellationToken ct = default)
    {
        if (scope is not ("full" or "restore-set")) throw new ArgumentException("Invalid download scope.", nameof(scope));
        using var timeout = Deadline(lease, ct, CommandDeadline);
        var response = await client.AdminBackupSendAsync(lease, HttpMethod.Post, BackupPath(backupId) + "/download-links", new { scope }, timeout.Token).ConfigureAwait(false);
        var body = Parse<ItemsOf<AdminBackupDownloadLink>>(response, null);
        if (body.Items is not { Length: > 0 }) throw new InvalidDataException("No download links.");
        foreach (var link in body.Items) AdminBackupValidation.Require(link, scope);
        if (scope == "restore-set" && body.Items.Length != 1) throw new InvalidDataException("A restore set has one link.");
        if (body.Items.Select(link => link.Name).Distinct(StringComparer.Ordinal).Count() != body.Items.Length) throw new InvalidDataException("Duplicate download links.");
        return body.Items;
    }

    /// <summary>Tests a bucket. Saves nothing; a failed test is a 200 with ok false. The secret is sent only when typed.</summary>
    public async Task<AdminBackupS3TestResult> TestS3Async(MonitorAuthorityLease lease, AdminBackupS3Test test, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["endpoint"] = test.Endpoint.Trim(), ["bucket"] = test.Bucket.Trim(), ["region"] = test.Region.Trim(),
            ["key_prefix"] = test.KeyPrefix.Trim(), ["access_key"] = test.AccessKey.Trim(), ["path_style"] = test.PathStyle,
        };
        if (test.SecretKey.Length > 0) body["secret_key"] = test.SecretKey;
        using var timeout = Deadline(lease, ct, S3TestDeadline);
        var response = await client.AdminBackupSendAsync(lease, HttpMethod.Post, "/api/v2/admin/backups/s3/test", body, timeout.Token).ConfigureAwait(false);
        var result = Parse<S3TestResponse>(response, null);
        if (result.Ok is not { } ok || result.Message == null || result.Step is not (null or "endpoint" or "credentials" or "bucket" or "permission"))
            throw new InvalidDataException("Invalid S3 test result.");
        return new(ok, result.Step, AdminBackupValidation.SafeServerText(result.Message, test.SecretKey, test.AccessKey));
    }

    /// <summary>Confirms a copy of SECRET_KEY with <paramref name="keyCheck"/> is kept elsewhere; 409 when the key changed.</summary>
    public async Task AcknowledgeKeyAsync(MonitorAuthorityLease lease, string keyCheck, CancellationToken ct = default)
    {
        if (!AdminBackupIdentifiers.IsKeyCheck(keyCheck)) throw new ArgumentException("Invalid key check.", nameof(keyCheck));
        var response = await Command(lease, HttpMethod.Post, "/api/v2/admin/backups/s3/acknowledge-key", new { key_check = keyCheck }, "key confirmation", ct).ConfigureAwait(false);
        var body = Parse<KeyResponse>(response, "key confirmation");
        if (body.KeyCheck != keyCheck) throw new AdminBackupOutcomeUnknownException("key confirmation");
    }

    /// <summary>Opens an upload (201). Not idempotent: never resent.</summary>
    public async Task<AdminBackupUpload> CreateUploadAsync(MonitorAuthorityLease lease, long sizeBytes, long? partSize, string? sha256, CancellationToken ct = default)
    {
        if (sizeBytes < 1) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        if (partSize is { } size && (size < AdminBackupIdentifiers.MinPartSize || size > AdminBackupIdentifiers.MaxPartSize)) throw new ArgumentOutOfRangeException(nameof(partSize));
        if (sha256 != null && !AdminBackupIdentifiers.IsDigest(sha256)) throw new ArgumentException("Invalid SHA-256.", nameof(sha256));
        var response = await Command(lease, HttpMethod.Post, "/api/v2/admin/backups/uploads",
            new CreateUpload(sizeBytes, sha256, partSize), "upload", ct).ConfigureAwait(false);
        var upload = Parse<AdminBackupUpload>(response, "upload");
        if (response.Status != 201) throw new AdminBackupOutcomeUnknownException("upload");
        try { AdminBackupValidation.Require(upload); } catch (InvalidDataException) { throw new AdminBackupOutcomeUnknownException("upload"); }
        if (upload.SizeBytes != sizeBytes || upload.Status != "uploading" || (partSize is { } wanted && upload.PartSize != wanted))
            throw new AdminBackupOutcomeUnknownException("upload");
        return upload;
    }

    /// <summary>One part. Naturally idempotent: sending it again replaces it until the upload completes.</summary>
    public async Task<AdminBackupUpload> PutPartAsync(MonitorAuthorityLease lease, AdminBackupUpload upload, int part, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
    {
        var expected = AdminBackupUploadMath.PartLength(upload, part);
        if (bytes.Length != expected) throw new ArgumentException("The part has the wrong length.", nameof(bytes));
        using var timeout = Deadline(lease, ct, PartDeadline);
        var response = await client.AdminBackupPutPartAsync(lease, UploadPath(upload.Id!) + "/parts/" + part.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bytes, timeout.Token).ConfigureAwait(false);
        var result = Parse<AdminBackupUpload>(response, null);
        AdminBackupValidation.Require(result);
        if (result.Id != upload.Id || result.SizeBytes != upload.SizeBytes || result.PartSize != upload.PartSize || result.PartCount != upload.PartCount)
            throw new InvalidDataException("The part answer names another upload.");
        return result;
    }

    /// <summary>Checks a fully received upload; naturally idempotent (a finished upload answers as it is).</summary>
    public async Task<AdminBackupUpload> CompleteUploadAsync(MonitorAuthorityLease lease, string uploadId, CancellationToken ct = default)
    {
        using var timeout = Deadline(lease, ct, CompleteDeadline);
        var response = await client.AdminBackupSendAsync(lease, HttpMethod.Post, UploadPath(uploadId) + "/complete", null, timeout.Token).ConfigureAwait(false);
        var result = Parse<AdminBackupUpload>(response, null);
        AdminBackupValidation.Require(result);
        if (result.Id != uploadId) throw new InvalidDataException("The completion answers for another upload.");
        return result;
    }

    public async Task DeleteUploadAsync(MonitorAuthorityLease lease, string uploadId, CancellationToken ct = default)
    {
        try
        {
            var response = await Command(lease, HttpMethod.Delete, UploadPath(uploadId), null, "upload delete", ct).ConfigureAwait(false);
            if (response.Status != 204) throw new AdminBackupOutcomeUnknownException("upload delete");
        }
        catch (ApiException error) when (error.StatusCode == 404) { }
    }

    /// <summary>
    /// Queues a selective restore once with the reviewed digest. A valid 202 carries the job and a
    /// matching <c>Location</c>; anything else is an unknown outcome that names the job when it can.
    /// </summary>
    public async Task<AdminBackupRestoreStarted> StartRestoreAsync(MonitorAuthorityLease lease, AdminBackupRestoreSourceRef source, string digest,
        AdminBackupRestoreSelection selection, bool acceptUnauthenticated, CancellationToken ct = default)
    {
        if (!AdminBackupIdentifiers.IsDigest(digest)) throw new ArgumentException("Invalid digest.", nameof(digest));
        if (selection.IsEmpty) throw new ArgumentException("Nothing is chosen.", nameof(selection));
        var request = new Dictionary<string, object?> { ["digest"] = digest, ["selection"] = SelectionBody(selection) };
        if (acceptUnauthenticated) request["accept_unauthenticated"] = true;
        return await RestoreOnceAsync(lease, source, async () =>
        {
            var response = await Command(lease, HttpMethod.Post, SourcePath(source) + "/restore", request, "restore", ct).ConfigureAwait(false);
            var named = JobIdFromLocation(response.Location);
            AdminBackupRestoreJob job;
            try
            {
                job = Parse<AdminBackupRestoreJob>(response, "restore");
                AdminBackupValidation.Require(job);
            }
            catch (Exception error) when (error is InvalidDataException or AdminBackupOutcomeUnknownException) { throw new AdminBackupOutcomeUnknownException("restore", named); }
            if (response.Status != 202 || named == null || job.Id != named || job.Kind != "backup_restore" || !AdminBackupValidation.NamesSource(job, source))
                throw new AdminBackupOutcomeUnknownException("restore", named ?? (AdminBackupIdentifiers.IsJobId(job.Id) ? job.Id : null));
            return new AdminBackupRestoreStarted(job);
        }).ConfigureAwait(false);
    }

    /// <summary>Asks the displayed job to stop (200 or 202). Never resent.</summary>
    public async Task<AdminBackupRestoreJob> CancelJobAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        if (!AdminBackupIdentifiers.IsJobId(jobId)) throw new ArgumentException("Invalid job id.", nameof(jobId));
        var response = await Command(lease, HttpMethod.Post, "/api/v2/admin/jobs/" + Uri.EscapeDataString(jobId) + "/cancel", null, "stop", ct).ConfigureAwait(false);
        var job = Parse<AdminBackupRestoreJob>(response, "stop");
        try { AdminBackupValidation.Require(job); } catch (InvalidDataException) { throw new AdminBackupOutcomeUnknownException("stop"); }
        if (job.Id != jobId || response.Status is not (200 or 202)) throw new AdminBackupOutcomeUnknownException("stop");
        return job;
    }

    /// <summary>The job id at the end of a <c>Location: /api/v2/admin/jobs/{id}</c> header (official jobIdFromLocation).</summary>
    public static string? JobIdFromLocation(string? location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        var marker = location.IndexOf("/admin/jobs/", StringComparison.Ordinal);
        if (marker < 0) return null;
        var rest = location[(marker + "/admin/jobs/".Length)..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        if (end >= 0) rest = rest[..end];
        string id;
        try { id = Uri.UnescapeDataString(rest); } catch (UriFormatException) { return null; }
        return AdminBackupIdentifiers.IsJobId(id) ? id : null;
    }

    // ── Plumbing ─────────────────────────────────────────────────────

    private async Task<T> Read<T>(MonitorAuthorityLease lease, string path, TimeSpan deadline, CancellationToken ct, int maxBytes = 16 * 1024 * 1024)
    {
        using var timeout = Deadline(lease, ct, deadline);
        var response = await client.AdminBackupSendAsync(lease, HttpMethod.Get, path, null, timeout.Token, maxBytes, allowTokenRefresh: true).ConfigureAwait(false);
        return Parse<T>(response, null);
    }

    /// <summary>
    /// A non-idempotent (or delete) command: no token refresh, no resend. A transport failure, a
    /// timeout, 408 or a 5xx after sending means the outcome is unknown.
    /// </summary>
    private async Task<AdminBackupWireResponse> Command(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, string operation, CancellationToken ct)
    {
        lease.ThrowIfRetired();
        using var timeout = Deadline(lease, ct, CommandDeadline);
        try { return await client.AdminBackupSendAsync(lease, method, path, body, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new AdminBackupOutcomeUnknownException(operation); }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminBackupOutcomeUnknownException(operation); }
        catch (ApiException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException) { throw new AdminBackupOutcomeUnknownException(operation); }
    }

    private static T Parse<T>(AdminBackupWireResponse response, string? unknownOperation)
    {
        try
        {
            if (response.Body.Length == 0) throw new InvalidDataException("Empty Backups response.");
            return JsonSerializer.Deserialize<T>(response.Body, Json) ?? throw new InvalidDataException("Empty Backups response.");
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or NotSupportedException)
        {
            if (unknownOperation != null) throw new AdminBackupOutcomeUnknownException(unknownOperation);
            throw new InvalidDataException("Invalid Backups response.");
        }
    }

    private static CancellationTokenSource Deadline(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline);
        return linked;
    }

    internal static string BackupPath(string backupId) => AdminBackupIdentifiers.IsBackupId(backupId)
        ? "/api/v2/admin/backups/" + Uri.EscapeDataString(backupId) : throw new ArgumentException("Invalid backup id.", nameof(backupId));
    internal static string UploadPath(string uploadId) => AdminBackupIdentifiers.IsUploadId(uploadId)
        ? "/api/v2/admin/backups/uploads/" + uploadId : throw new ArgumentException("Invalid upload id.", nameof(uploadId));
    internal static string SourcePath(AdminBackupRestoreSourceRef source) => source.Upload ? UploadPath(source.Id) : BackupPath(source.Id);

    private static Dictionary<string, object?> SelectionBody(AdminBackupRestoreSelection selection)
    {
        var body = new Dictionary<string, object?>();
        void Add(string key, IReadOnlyList<string> values) { if (values.Count > 0) body[key] = values; }
        Add("libraries", selection.Libraries); Add("plugins", selection.Plugins); Add("settings", selection.Settings);
        Add("collections", selection.Collections); Add("home_sections", selection.HomeSections);
        if (selection.Users.Count > 0)
            body["users"] = selection.Users.Select(user => new Dictionary<string, object?>
            {
                ["account"] = user.Account, ["target"] = user.Target,
                ["profiles"] = user.Profiles.Select(profile => new Dictionary<string, object?>
                {
                    ["profile"] = profile.Profile, ["target"] = profile.Target, ["data_types"] = profile.DataTypes,
                }).ToArray(),
            }).ToArray();
        return body;
    }

    private sealed record ItemsOf<T> { public T[]? Items { get; init; } }
    private sealed record TaskSchedule { public string? Key { get; init; } public DateTimeOffset? NextRunAt { get; init; } }
    private sealed record S3TestResponse { public bool? Ok { get; init; } public string? Step { get; init; } public string? Message { get; init; } }
    private sealed record KeyResponse { public string? KeyCheck { get; init; } }
    private sealed record CreateUpload(long SizeBytes, string? Sha256, long? PartSize);
}

/// <summary>Part arithmetic the official handlers use: every part but the last is exactly <c>part_size</c>.</summary>
public static class AdminBackupUploadMath
{
    public static long PartCount(long size, long partSize) => partSize <= 0 ? 0 : (size + partSize - 1) / partSize;

    public static long PartOffset(AdminBackupUpload upload, int part) => part * upload.PartSize!.Value;

    public static int PartLength(AdminBackupUpload upload, int part)
    {
        if (part < 0 || part >= upload.PartCount) throw new ArgumentOutOfRangeException(nameof(part));
        var start = PartOffset(upload, part);
        return (int)Math.Min(upload.PartSize!.Value, upload.SizeBytes!.Value - start);
    }
}
