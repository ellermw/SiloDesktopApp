using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Configure › Server › Maintenance (official WebUI <c>hooks/queries/admin/{libraries,taskJobs}.ts</c>,
/// server <c>internal/apiv2/admin_catalog_{transfer,sources}.go</c> and <c>admin_task_jobs.go</c>
/// at 92ee0518). Every operation is acting-admin and runs under the lease it is given. The
/// catalog commands are demo-restricted and non-retryable: each is sent once, never with a
/// token refresh, and any acknowledgement this client cannot prove is reported as
/// <see cref="AdminCatalogOutcomeUnknownException"/> for the caller to reconcile by reading.
/// Import sources are SERVER paths and object keys; nothing here inspects the local file system.
/// </summary>
public sealed class AdminMaintenanceApi(SiloApiClient client)
{
    private readonly object _importGate = new();
    private AdminMonitorAuthority? _importAuthority;
    private long _importEpoch;
    private Guid? _importAttempt;
    private bool _importPending;

    public bool HasUnconfirmedImport(MonitorAuthorityLease lease)
    {
        lock (_importGate) return ReferenceEquals(_importAuthority, lease.Owner) && _importEpoch == lease.Epoch && _importAttempt != null;
    }

    public bool AcknowledgeImportOutcome(MonitorAuthorityLease lease)
    {
        lease.ThrowIfRetired();
        lock (_importGate)
        {
            if (!ReferenceEquals(_importAuthority, lease.Owner) || _importEpoch != lease.Epoch || _importAttempt == null) return true;
            if (_importPending) return false;
            _importAttempt = null;
            return true;
        }
    }

    private async Task<T> ImportOnceAsync<T>(MonitorAuthorityLease lease, Func<Task<T>> send)
    {
        lease.ThrowIfRetired();
        var attempt = Guid.NewGuid();
        lock (_importGate)
        {
            if (ReferenceEquals(_importAuthority, lease.Owner) && _importEpoch == lease.Epoch && _importAttempt != null)
                throw new AdminCatalogOutcomeUnknownException(null);
            _importAuthority = lease.Owner;
            _importEpoch = lease.Epoch;
            _importAttempt = attempt;
            _importPending = true;
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
            lock (_importGate)
            {
                if (_importAttempt == attempt)
                {
                    _importPending = false;
                    if (settled) _importAttempt = null;
                }
            }
        }
    }

    public const string ExportKind = "catalog_export";
    public const string ImportKind = "catalog_import";
    /// <summary>WebUI <c>useCatalogExportJobs</c> / <c>useCatalogImportJobs</c> page size.</summary>
    public const int CatalogJobPageSize = 10;
    /// <summary>WebUI <c>useAllAdminJobs</c> page size.</summary>
    public const int HistoryPageSize = 30;
    /// <summary>WebUI <c>useCatalogSources</c> page size (server range 1–200).</summary>
    public const int SourcePageSize = 50;
    public const int MaxJobIdLength = 256;
    public const string JobLocationPrefix = "/api/v2/admin/jobs/";

    /// <summary>One newest-first page of retained jobs; <paramref name="kind"/> null or empty lists every kind.</summary>
    public async Task<AdminCursorPage<AdminMaintenanceJob>> ListJobsAsync(MonitorAuthorityLease lease, string? kind, int limit, string? cursor,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var path = AdminPaging.WithPage(string.IsNullOrEmpty(kind) ? "/api/v2/admin/jobs" : "/api/v2/admin/jobs?kind=" + Uri.EscapeDataString(kind), limit, cursor);
        var answer = await client.AdminMaintenanceSendAsync<AdminMaintenanceJobCollection>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        var page = AdminLibrariesApi.ValidatePage(answer.Body.Items, answer.Body.Page, limit, cursor, job => IsJobId(job?.Id) ? job!.Id : null);
        if (!string.IsNullOrEmpty(kind) && page.Items.Any(job => !string.Equals(job.Kind, kind, StringComparison.Ordinal)))
            throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
        return page;
    }

    /// <summary>Reads one retained job (a 202 receipt's or a 409 conflict's <c>Location</c>).</summary>
    public async Task<AdminMaintenanceJob> GetJobAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        RequireJobId(jobId);
        var answer = await client.AdminMaintenanceSendAsync<AdminMaintenanceJob>(lease, HttpMethod.Get, JobLocationPrefix + Uri.EscapeDataString(jobId), ct: ct).ConfigureAwait(false);
        if (!string.Equals(answer.Body.Id, jobId, StringComparison.Ordinal)) throw new InvalidDataException("The server answered with a different job.");
        return answer.Body;
    }

    /// <summary>One page of bucket (<paramref name="local"/> false) or server-local catalog seeds.</summary>
    public async Task<AdminCursorPage<AdminCatalogSource>> ListImportSourcesAsync(MonitorAuthorityLease lease, bool local, string? cursor,
        CancellationToken ct = default)
    {
        var path = AdminPaging.WithPage(local ? "/api/v2/admin/catalog/local-import-sources" : "/api/v2/admin/catalog/import-sources", SourcePageSize, cursor);
        var answer = await client.AdminMaintenanceSendAsync<AdminCatalogSourceCollection>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        return AdminLibrariesApi.ValidatePage(answer.Body.Items, answer.Body.Page, SourcePageSize, cursor,
            source => string.IsNullOrEmpty(source?.Key) || source.Key.Length > AdminCatalogImportDraft.MaxValueLength ? null : source.Key);
    }

    /// <summary><c>POST /admin/catalog/export-jobs</c> for every library (WebUI sends <c>{}</c>). 202 is acceptance only.</summary>
    public Task<AdminMaintenanceJob> CreateExportJobAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        AcceptedAsync(lease, "/api/v2/admin/catalog/export-jobs", new Dictionary<string, object?>(), ExportKind, ct);

    /// <summary><c>POST /admin/catalog/import-jobs</c>: a worker reads the source later. 202 is acceptance only.</summary>
    public Task<AdminMaintenanceJob> CreateImportJobAsync(MonitorAuthorityLease lease, AdminCatalogImportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.ToBody();
        return ImportOnceAsync(lease, () => AcceptedAsync(lease, "/api/v2/admin/catalog/import-jobs", body, ImportKind, ct));
    }

    /// <summary>
    /// <c>POST /admin/catalog/import</c>: success means the catalog transaction committed. A lost
    /// or unreadable answer may still have committed; it is never retried.
    /// </summary>
    public async Task<AdminCatalogImportResult> ImportAsync(MonitorAuthorityLease lease, AdminCatalogImportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.ToBody();
        return await ImportOnceAsync(lease, async () =>
        {
            var answer = await SendCommandAsync<AdminCatalogImportResult>(lease, "/api/v2/admin/catalog/import", body, ct).ConfigureAwait(false);
            if (answer.Status != 200 || answer.Body.ItemsCreated < 0 || answer.Body.FilesCreated < 0) throw new AdminCatalogOutcomeUnknownException(null);
            return answer.Body;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST /admin/catalog/export-jobs/{id}/publish</c>: saves a seven-day signed URL, or returns
    /// the existing one without renewing it. The returned URL is never logged or shown.
    /// </summary>
    public async Task<AdminCatalogPublished> PublishAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        RequireJobId(jobId);
        var answer = await SendCommandAsync<AdminCatalogPublished>(lease, "/api/v2/admin/catalog/export-jobs/" + Uri.EscapeDataString(jobId) + "/publish", null, ct).ConfigureAwait(false);
        if (answer.Status != 200 || !string.Equals(answer.Body.JobId, jobId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(answer.Body.Url))
            throw new AdminCatalogOutcomeUnknownException(jobId);
        return answer.Body;
    }

    /// <summary>The job id a server <c>Location</c> names, or null when it is not exactly <c>/api/v2/admin/jobs/{id}</c>.</summary>
    public static string? JobIdFromLocation(string? location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https") location = absolute.AbsolutePath;
        if (string.IsNullOrEmpty(location) || !location.StartsWith(JobLocationPrefix, StringComparison.Ordinal)) return null;
        var rest = location[JobLocationPrefix.Length..];
        if (rest.Contains('/') || rest.Contains('?') || rest.Contains('#')) return null;
        string id;
        try { id = Uri.UnescapeDataString(rest); }
        catch (UriFormatException) { return null; }
        return IsJobId(id) ? id : null;
    }

    public static bool IsJobId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= MaxJobIdLength && !id.Any(char.IsControl);

    private static void RequireJobId(string? id)
    {
        if (!IsJobId(id)) throw new ArgumentException("Invalid job ID.", nameof(id));
    }

    private async Task<AdminMaintenanceJob> AcceptedAsync(MonitorAuthorityLease lease, string path, object body, string kind, CancellationToken ct)
    {
        var answer = await SendCommandAsync<AdminMaintenanceJob>(lease, path, body, ct).ConfigureAwait(false);
        var job = answer.Body;
        // A receipt is trusted only when it is a 202 for a job of this kind that the Location names.
        if (answer.Status != 202 || !IsJobId(job.Id) || !string.Equals(job.Kind, kind, StringComparison.Ordinal)
            || !string.Equals(JobIdFromLocation(answer.Location), job.Id, StringComparison.Ordinal))
            throw new AdminCatalogOutcomeUnknownException(null);
        return job;
    }

    /// <summary>Sends one command; malformed or undecodable success bodies become an unknown outcome, never a resend.</summary>
    private async Task<AdminMaintenanceResponse<T>> SendCommandAsync<T>(MonitorAuthorityLease lease, string path, object? body, CancellationToken ct)
    {
        try { return await client.AdminMaintenanceSendAsync<T>(lease, HttpMethod.Post, path, body, ct).ConfigureAwait(false); }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
        {
            throw new AdminCatalogOutcomeUnknownException(null, error);
        }
    }
}

/// <summary>
/// A catalog command whose acceptance this client cannot prove: the server may have acted.
/// It is reconciled by reading, never by sending again.
/// </summary>
public sealed class AdminCatalogOutcomeUnknownException(string? jobId, Exception? inner = null)
    : IOException("The server's answer could not be confirmed. The command may have been applied.", inner)
{
    public string? JobId { get; } = jobId;
}
