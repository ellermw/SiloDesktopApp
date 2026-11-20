using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One bounded cursor list as the page shows it.</summary>
public sealed record AdminMaintenanceList<T>(IReadOnlyList<T> Items, bool Loaded, bool Loading, bool LoadingMore, bool HasMore, AdminFailureKind? Failure)
{
    public static readonly AdminMaintenanceList<T> Empty = new([], false, false, false, false, null);
    public bool IsFetching => Loading || LoadingMore;
}

/// <summary>The five lists the page reads (the official page mounts all five queries).</summary>
public enum AdminMaintenanceListKind { Exports, Imports, History, BucketSources, LocalSources }

public enum AdminMaintenanceNoticeKind { Success, Warning, Error }

/// <summary>A toast for the page: a fixed localized message, never server or provider text.</summary>
public sealed record AdminMaintenanceNotice(AdminMaintenanceNoticeKind Kind, string Message);

/// <summary>An immutable snapshot of the Maintenance page.</summary>
public sealed record AdminMaintenanceState
{
    public MonitorAuthorityLease? Owner { get; init; }
    /// <summary>The failure that prevented the page from opening (not acting as admin, for example).</summary>
    public AdminFailureKind? OpenFailure { get; init; }
    public AdminMaintenanceList<AdminMaintenanceJob> Exports { get; init; } = AdminMaintenanceList<AdminMaintenanceJob>.Empty;
    public AdminMaintenanceList<AdminMaintenanceJob> Imports { get; init; } = AdminMaintenanceList<AdminMaintenanceJob>.Empty;
    public AdminMaintenanceList<AdminMaintenanceJob> History { get; init; } = AdminMaintenanceList<AdminMaintenanceJob>.Empty;
    public AdminMaintenanceList<AdminCatalogSource> BucketSources { get; init; } = AdminMaintenanceList<AdminCatalogSource>.Empty;
    public AdminMaintenanceList<AdminCatalogSource> LocalSources { get; init; } = AdminMaintenanceList<AdminCatalogSource>.Empty;
    public bool ExportPending { get; init; }
    public string? PublishingJobId { get; init; }
    public bool ImportPending { get; init; }
    public bool DialogOpen { get; init; }
    public AdminCatalogImportDraft Draft { get; init; } = AdminCatalogImportDraft.Initial();
    /// <summary>A safe localized reason the last import was refused; shown inside the dialog.</summary>
    public string? ImportError { get; init; }
    /// <summary>Source roots the server said need a rewrite (<c>path_rewrite_required</c> on <c>body.path_rewrites</c>).</summary>
    public IReadOnlyList<string> RequiredRewriteRoots { get; init; } = [];
    /// <summary>
    /// An import was sent and its answer could not be confirmed. It blocks another import until the
    /// administrator acknowledges having checked the catalog; refreshes and dialog close never clear it.
    /// </summary>
    public bool ImportOutcomeUnknown { get; init; }
    /// <summary>The active catalog job a 409 named, once read (never a guessed “latest” job).</summary>
    public AdminMaintenanceJob? ConflictJob { get; init; }

    public bool PublishPending => PublishingJobId != null;
    public bool AnyCommandPending => ExportPending || PublishPending || ImportPending;
    public IReadOnlyList<AdminMaintenanceJob> CompletedExports => Exports.Items.Where(job => AdminMaintenancePresentation.Status(job) == "completed").ToArray();
    public bool CanSubmitImport => Owner is { IsCurrent: true } && !ImportPending && !ImportOutcomeUnknown && Draft.CanSubmit;
}

/// <summary>
/// Configure › Maintenance state (official <c>AdminCatalogMaintenance.tsx</c> and
/// <c>AdminJobHistory.tsx</c> at 92ee0518). Reads are foreground only: the page mounts its
/// five lists and changes them on Refresh, Restart, Load more and command reconciliation —
/// there is no polling timer (WebUI <c>useAdminTaskJobs</c> polls only when asked, and this
/// page never asks). Each list has a generation: a refresh supersedes an in-flight Load more,
/// a refresh requested while one runs is coalesced into one more pass, and late results of a
/// superseded generation or retired owner are dropped. Catalog commands are sent once; an
/// unproven acknowledgement is reconciled by reading, never by resending.
/// </summary>
public sealed class AdminMaintenanceController : IDisposable
{
    private sealed class Chain<T>(AdminMaintenanceListKind kind, Func<MonitorAuthorityLease, string?, CancellationToken, Task<AdminCursorPage<T>>> read, Func<T, string> identity)
    {
        public readonly AdminMaintenanceListKind Kind = kind;
        public readonly Func<MonitorAuthorityLease, string?, CancellationToken, Task<AdminCursorPage<T>>> Read = read;
        public readonly Func<T, string> Identity = identity;
        public IReadOnlyList<T> Items = [];
        public HashSet<string> Cursors = new(StringComparer.Ordinal);
        public string? Next;
        public int Pages;
        public bool Loaded, Loading, LoadingMore, RefreshQueued;
        public AdminFailureKind? Failure;
        public long Generation;
        public CancellationTokenSource? Work;
        public Task? Running;

        public AdminMaintenanceList<T> Snapshot() => new(Items, Loaded, Loading, LoadingMore, Next != null, Failure);

        public void Reset()
        {
            Generation++;
            Work?.Cancel(); Work?.Dispose(); Work = null;
            Items = []; Cursors = new(StringComparer.Ordinal); Next = null; Pages = 0;
            Loaded = Loading = LoadingMore = RefreshQueued = false; Failure = null; Running = null;
        }
    }

    private readonly AdminMaintenanceApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private readonly Chain<AdminMaintenanceJob> _exports, _imports, _history;
    private readonly Chain<AdminCatalogSource> _bucket, _local;
    private AdminMaintenanceState _state = new();
    private MonitorAuthorityLease? _owner;
    private bool _disposed;

    public AdminMaintenanceController(AdminMaintenanceApi api, AdminMonitorAuthority authority)
    {
        _api = api;
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
        _exports = new(AdminMaintenanceListKind.Exports, (lease, cursor, ct) => _api.ListJobsAsync(lease, AdminMaintenanceApi.ExportKind, AdminMaintenanceApi.CatalogJobPageSize, cursor, ct), job => job.Id);
        _imports = new(AdminMaintenanceListKind.Imports, (lease, cursor, ct) => _api.ListJobsAsync(lease, AdminMaintenanceApi.ImportKind, AdminMaintenanceApi.CatalogJobPageSize, cursor, ct), job => job.Id);
        _history = new(AdminMaintenanceListKind.History, (lease, cursor, ct) => _api.ListJobsAsync(lease, null, AdminMaintenanceApi.HistoryPageSize, cursor, ct), job => job.Id);
        _bucket = new(AdminMaintenanceListKind.BucketSources, (lease, cursor, ct) => _api.ListImportSourcesAsync(lease, local: false, cursor, ct), source => source.Key);
        _local = new(AdminMaintenanceListKind.LocalSources, (lease, cursor, ct) => _api.ListImportSourcesAsync(lease, local: true, cursor, ct), source => source.Key);
    }

    public AdminMaintenanceApi Api => _api;
    public AdminMaintenanceState State { get { lock (_gate) return _state; } }

    /// <summary>Raised on any thread after <see cref="State"/> changes; marshal before touching UI.</summary>
    public event Action? Changed;
    public event Action<AdminMaintenanceNotice>? Notice;
    /// <summary>Raised when the submitted import succeeded and its draft was reset; the dialog closes.</summary>
    public event Action? ImportCompleted;

    /// <summary>The leave guard's condition: a command in flight, an open dirty draft, or a draft kept after a refused import.</summary>
    public bool RequiresLeaveConfirmation
    {
        get
        {
            var state = State;
            return state.Owner is { IsCurrent: true } && (state.AnyCommandPending || state.Draft.IsDirty);
        }
    }

    /// <summary>Captures the owner and mounts the five lists. A non-admin authority leaves the page closed with its failure.</summary>
    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.Open(); }
        catch (Exception error) when (error is MonitorAuthorityRetiredException or ObjectDisposedException)
        {
            Update(() => _state = new AdminMaintenanceState { OpenFailure = AdminFailureKind.Forbidden });
            return;
        }
        Update(() =>
        {
            _owner = owner;
            foreach (var reset in AllResets()) reset();
            _state = new AdminMaintenanceState { Owner = owner, ImportOutcomeUnknown = _api.HasUnconfirmedImport(owner) };
            Publish();
        });
        await Task.WhenAll(RefreshAsync(AdminMaintenanceListKind.Exports), RefreshAsync(AdminMaintenanceListKind.Imports),
            RefreshAsync(AdminMaintenanceListKind.History), RefreshAsync(AdminMaintenanceListKind.BucketSources),
            RefreshAsync(AdminMaintenanceListKind.LocalSources)).ConfigureAwait(false);
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    /// <summary>Refetches every loaded page of a list (React Query <c>refetch</c>); a refresh already running is coalesced into one more pass.</summary>
    public Task RefreshAsync(AdminMaintenanceListKind kind) => kind switch
    {
        AdminMaintenanceListKind.Exports => RefreshAsync(_exports),
        AdminMaintenanceListKind.Imports => RefreshAsync(_imports),
        AdminMaintenanceListKind.History => RefreshAsync(_history),
        AdminMaintenanceListKind.BucketSources => RefreshAsync(_bucket),
        _ => RefreshAsync(_local),
    };

    /// <summary>Drops the list and reads its first page again (WebUI <c>restart</c> / Retry).</summary>
    public Task RestartAsync(AdminMaintenanceListKind kind)
    {
        Update(() => { foreach (var reset in ResetsOf(kind)) reset(); Publish(); });
        return RefreshAsync(kind);
    }

    /// <summary>Reads the next page (Load older / Load more). Ignored while the list refreshes or has no continuation.</summary>
    public Task LoadMoreAsync(AdminMaintenanceListKind kind) => kind switch
    {
        AdminMaintenanceListKind.Exports => LoadMoreAsync(_exports),
        AdminMaintenanceListKind.Imports => LoadMoreAsync(_imports),
        AdminMaintenanceListKind.History => LoadMoreAsync(_history),
        AdminMaintenanceListKind.BucketSources => LoadMoreAsync(_bucket),
        _ => LoadMoreAsync(_local),
    };

    private Task RefreshAsync<T>(Chain<T> chain)
    {
        lock (_gate)
        {
            if (_owner is not { IsCurrent: true }) return Task.CompletedTask;
            if (chain.Running != null) { chain.RefreshQueued = true; return chain.Running; }
            chain.Running = RefreshLoopAsync(chain, _owner);
            return chain.Running;
        }
    }

    private async Task RefreshLoopAsync<T>(Chain<T> chain, MonitorAuthorityLease owner)
    {
        await Task.Yield();
        while (true)
        {
            long generation;
            int pages;
            CancellationToken token;
            lock (_gate)
            {
                if (!IsOwner(owner)) return;
                chain.RefreshQueued = false;
                chain.Work?.Cancel(); chain.Work?.Dispose();
                chain.Work = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
                token = chain.Work.Token;
                generation = ++chain.Generation;
                chain.Loading = true; chain.LoadingMore = false;
                pages = Math.Max(1, chain.Pages);
                Publish();
            }
            Changed?.Invoke();
            var items = new List<T>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            var read = 0;
            AdminFailureKind? failure = null;
            try
            {
                while (read < pages)
                {
                    var page = await ReadPageAsync(chain, owner, cursor, token).ConfigureAwait(false);
                    Accept(chain, page, ids, cursors, items);
                    read++;
                    cursor = page.NextCursor;
                    if (cursor == null) break;
                }
            }
            catch (Exception error) { failure = AdminFailures.Classify(error, owner); }
            bool again;
            lock (_gate)
            {
                if (!IsOwner(owner) || generation != chain.Generation) return;
                chain.Loading = false;
                if (failure is { } kind)
                {
                    if (kind is AdminFailureKind.Retired or AdminFailureKind.Cancelled) { chain.Running = null; Publish(); return; }
                    chain.Failure = kind;
                }
                else
                {
                    chain.Items = items; chain.Cursors = cursors; chain.Next = cursor; chain.Pages = read;
                    chain.Loaded = true; chain.Failure = null;
                }
                again = chain.RefreshQueued;
                if (!again) chain.Running = null;
                Publish();
            }
            Changed?.Invoke();
            if (!again) return;
        }
    }

    private async Task LoadMoreAsync<T>(Chain<T> chain)
    {
        MonitorAuthorityLease owner;
        long generation;
        string cursor;
        CancellationToken token;
        lock (_gate)
        {
            if (_owner is not { IsCurrent: true } current || chain.Loading || chain.LoadingMore || chain.Next == null) return;
            owner = current;
            generation = chain.Generation;
            cursor = chain.Next;
            chain.Work?.Dispose();
            chain.Work = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            token = chain.Work.Token;
            chain.LoadingMore = true;
            Publish();
        }
        Changed?.Invoke();
        AdminCursorPage<T>? page = null;
        AdminFailureKind? failure = null;
        try { page = await ReadPageAsync(chain, owner, cursor, token).ConfigureAwait(false); }
        catch (Exception error) { failure = AdminFailures.Classify(error, owner); }
        lock (_gate)
        {
            if (!IsOwner(owner) || generation != chain.Generation) return;
            chain.LoadingMore = false;
            if (failure is { } kind)
            {
                if (kind is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled)) chain.Failure = kind;
            }
            else
            {
                var ids = chain.Items.Select(chain.Identity).ToHashSet(StringComparer.Ordinal);
                var cursors = new HashSet<string>(chain.Cursors, StringComparer.Ordinal);
                var items = chain.Items.ToList();
                try
                {
                    Accept(chain, page!, ids, cursors, items);
                    chain.Items = items; chain.Cursors = cursors; chain.Next = page!.NextCursor; chain.Pages++; chain.Failure = null;
                }
                catch (AdminListInconsistentException) { chain.Failure = AdminFailureKind.InvalidResponse; }
            }
            Publish();
        }
        Changed?.Invoke();
    }

    /// <summary>One page read, retried once for a server or non-timeout transport failure (WebUI query retry).</summary>
    private static async Task<AdminCursorPage<T>> ReadPageAsync<T>(Chain<T> chain, MonitorAuthorityLease owner, string? cursor, CancellationToken token)
    {
        try { return await chain.Read(owner, cursor, token).ConfigureAwait(false); }
        catch (Exception error) when (IsRetryableRead(error, owner))
        {
            return await chain.Read(owner, cursor, token).ConfigureAwait(false);
        }
    }

    private static bool IsRetryableRead(Exception error, MonitorAuthorityLease owner) =>
        owner.IsCurrent && error is not OperationCanceledException && AdminFailures.Classify(error, owner) is AdminFailureKind.Server or AdminFailureKind.Transport;

    /// <summary>Adds one page, refusing identities already shown and continuations already followed (cursor cycles).</summary>
    private static void Accept<T>(Chain<T> chain, AdminCursorPage<T> page, HashSet<string> ids, HashSet<string> cursors, List<T> items)
    {
        foreach (var item in page.Items)
            if (!ids.Add(chain.Identity(item))) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
        if (page.NextCursor != null && !cursors.Add(page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
        items.AddRange(page.Items);
    }

    // ── Commands ──────────────────────────────────────────────────────────

    /// <summary>Start Export: one POST; a 202 queues the export, it does not complete it.</summary>
    public async Task StartExportAsync()
    {
        var owner = BeginCommand(state => !state.ExportPending, state => state with { ExportPending = true });
        if (owner == null) return;
        try
        {
            using var lease = _lifetime.CaptureAction();
            try
            {
                await _api.CreateExportJobAsync(lease, lease.Token).ConfigureAwait(false);
                Raise(owner, AdminMaintenanceNoticeKind.Success, T("web.admin.libraries.toast.exportQueued"));
                await RefreshAsync(_exports).ConfigureAwait(false);
            }
            catch (AdminCatalogConflictException conflict)
            {
                Raise(owner, AdminMaintenanceNoticeKind.Error, T("admin.native.maintenance.conflict"));
                await AdoptConflictAsync(owner, conflict.JobLocation, _exports).ConfigureAwait(false);
            }
            catch (Exception error) when (IsPossiblyAccepted(error, lease))
            {
                Raise(owner, AdminMaintenanceNoticeKind.Warning, T("admin.native.maintenance.exportUnknown"));
                await RefreshAsync(_exports).ConfigureAwait(false);
            }
            catch (Exception error) when (AdminFailures.Classify(error, lease) is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled))
            {
                Raise(owner, AdminMaintenanceNoticeKind.Error, FailureText(error, "web.admin.libraries.toast.exportQueueFailed"));
            }
        }
        catch (Exception error) when (error is MonitorAuthorityRetiredException or OperationCanceledException) { }
        finally { EndCommand(owner, state => state with { ExportPending = false }); }
    }

    /// <summary>Create seven-day link for a completed, unpublished export. An existing link is returned unrenewed by the server.</summary>
    public async Task PublishAsync(string jobId)
    {
        var owner = BeginCommand(state => !state.PublishPending && state.Exports.Items.Any(job => job.Id == jobId && AdminMaintenancePresentation.CanCreateLink(job)),
            state => state with { PublishingJobId = jobId });
        if (owner == null) return;
        try
        {
            using var lease = _lifetime.CaptureAction();
            try
            {
                await _api.PublishAsync(lease, jobId, lease.Token).ConfigureAwait(false);
                Raise(owner, AdminMaintenanceNoticeKind.Success, T("web.admin.libraries.toast.exportLinkSaved"));
                await RefreshAsync(_exports).ConfigureAwait(false);
            }
            catch (Exception error) when (IsPossiblyAccepted(error, lease))
            {
                Raise(owner, AdminMaintenanceNoticeKind.Warning, T("admin.native.maintenance.publishUnknown"));
                await RefreshAsync(_exports).ConfigureAwait(false);
            }
            catch (Exception error) when (AdminFailures.Classify(error, lease) is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled))
            {
                Raise(owner, AdminMaintenanceNoticeKind.Error, FailureText(error, "web.admin.libraries.toast.exportPublishFailed"));
            }
        }
        catch (Exception error) when (error is MonitorAuthorityRetiredException or OperationCanceledException) { }
        finally { EndCommand(owner, state => state with { PublishingJobId = null }); }
    }

    /// <summary>
    /// Import Catalog: a background job (202) or import-and-wait (200 means committed). Success
    /// resets only the draft that was submitted. A refusal keeps the draft with a safe reason; an
    /// unconfirmed answer raises the unknown-outcome block and is never resent.
    /// </summary>
    public async Task SubmitImportAsync()
    {
        AdminCatalogImportDraft? submitted = null;
        AdminCatalogImportRequest? request = null;
        var owner = BeginCommand(state => state.CanSubmitImport, state =>
        {
            submitted = state.Draft; request = state.Draft.ToRequest();
            return state with { ImportPending = true, ImportError = null, RequiredRewriteRoots = [] };
        });
        if (owner == null || submitted == null || request == null) return;
        try
        {
            using var lease = _lifetime.CaptureAction();
            try
            {
                string message;
                if (request.Execution == AdminCatalogImportExecution.Queued)
                {
                    await _api.CreateImportJobAsync(lease, request, lease.Token).ConfigureAwait(false);
                    message = T("web.admin.libraries.toast.importQueued");
                }
                else
                {
                    var result = await _api.ImportAsync(lease, request, lease.Token).ConfigureAwait(false);
                    message = T("web.admin.libraries.toast.imported",
                        ("items", T("web.admin.libraries.toast.importedItems", AdminLibrariesText.Count(result.ItemsCreated))),
                        ("files", T("web.admin.libraries.toast.importedFiles", AdminLibrariesText.Count(result.FilesCreated))));
                }
                var completed = false;
                Update(() =>
                {
                    if (!IsOwner(owner)) return;
                    if (ReferenceEquals(_state.Draft, submitted)) { _state = _state with { Draft = AdminCatalogImportDraft.Initial(), DialogOpen = false }; completed = true; }
                });
                Raise(owner, AdminMaintenanceNoticeKind.Success, message);
                if (completed && IsCurrentOwner(owner)) ImportCompleted?.Invoke();
                if (request.Execution == AdminCatalogImportExecution.Queued) await RefreshAsync(_imports).ConfigureAwait(false);
            }
            catch (AdminCatalogConflictException conflict)
            {
                Refuse(owner, T("admin.native.maintenance.conflict"), []);
                await AdoptConflictAsync(owner, conflict.JobLocation, _imports).ConfigureAwait(false);
            }
            catch (ApiException refused) when (AdminMaintenancePresentation.RequiredRewriteRoots(refused.FieldErrors) is { Count: > 0 } roots && refused.StatusCode == 422)
            {
                Refuse(owner, T("admin.native.maintenance.rewriteRequired"), roots);
            }
            catch (Exception error) when (IsPossiblyAccepted(error, lease))
            {
                Update(() => { if (IsOwner(owner)) _state = _state with { ImportOutcomeUnknown = true }; });
                Raise(owner, AdminMaintenanceNoticeKind.Warning, T("admin.native.maintenance.importUnknown.body"));
                if (request.Execution == AdminCatalogImportExecution.Queued) await RefreshAsync(_imports).ConfigureAwait(false);
            }
            catch (Exception error) when (AdminFailures.Classify(error, lease) is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled))
            {
                Refuse(owner, FailureText(error, "web.admin.libraries.toast.importFailed"), []);
            }
        }
        catch (Exception error) when (error is MonitorAuthorityRetiredException or OperationCanceledException) { }
        finally { EndCommand(owner, state => state with { ImportPending = false }); }
    }

    /// <summary>The administrator has checked the catalog after an unconfirmed import; importing is allowed again.</summary>
    public void AcknowledgeImportOutcome() => Update(() =>
    {
        if (_owner is { IsCurrent: true } owner && _api.AcknowledgeImportOutcome(owner))
            _state = _state with { ImportOutcomeUnknown = false };
    });

    /// <summary>Edits the dialog draft. Refused while an import is in flight; any edit clears the last refusal reason.</summary>
    public void EditDraft(Func<AdminCatalogImportDraft, AdminCatalogImportDraft> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Update(() =>
        {
            if (_owner is not { IsCurrent: true } || _state.ImportPending) return;
            var draft = edit(_state.Draft);
            if (ReferenceEquals(draft, _state.Draft)) return;
            var rewritesChanged = !ReferenceEquals(draft.Rewrites, _state.Draft.Rewrites);
            _state = _state with { Draft = draft, ImportError = null, RequiredRewriteRoots = rewritesChanged ? [] : _state.RequiredRewriteRoots };
        });
    }

    /// <summary>
    /// The dialog opened or closed. Closing resets the draft (WebUI <c>resetImportState</c>) unless an
    /// import is in flight, whose outcome still decides whether its draft is kept or reset.
    /// </summary>
    public void SetDialogOpen(bool open) => Update(() =>
    {
        if (_owner is not { IsCurrent: true }) return;
        _state = open || _state.ImportPending
            ? _state with { DialogOpen = open }
            : _state with { DialogOpen = false, Draft = AdminCatalogImportDraft.Initial(), ImportError = null, RequiredRewriteRoots = [] };
    });

    /// <summary>Discards the draft for the leave guard.</summary>
    public void DiscardDraft() => Update(() =>
    {
        if (_owner is { IsCurrent: true } && !_state.ImportPending)
            _state = _state with { Draft = AdminCatalogImportDraft.Initial(), ImportError = null, RequiredRewriteRoots = [] };
    });

    // ── Signed links ──────────────────────────────────────────────────────

    /// <summary>
    /// The export's download address for the current owner only: a relative signed route resolves
    /// against the connected server; an absolute one must be http(s) without user info. It is a
    /// self-authorizing capability, opened without account credentials, and never logged.
    /// </summary>
    public Uri? DownloadUri(MonitorAuthorityLease owner, string jobId)
    {
        if (!IsCurrentOwner(owner)) return null;
        var job = State.Exports.Items.FirstOrDefault(candidate => candidate.Id == jobId);
        if (string.IsNullOrWhiteSpace(job?.DownloadUrl)) return null;
        if (!Uri.TryCreate(owner.Context.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin)) return null;
        if (!Uri.TryCreate(origin, job.DownloadUrl, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0) return null;
        return uri;
    }

    /// <summary>The published seven-day link for Copy URL, for the current owner only.</summary>
    public string? PublicUrl(MonitorAuthorityLease owner, string jobId)
    {
        if (!IsCurrentOwner(owner)) return null;
        var url = State.Exports.Items.FirstOrDefault(candidate => candidate.Id == jobId)?.PublicUrl;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? url : null;
    }

    public bool IsCurrentOwner(MonitorAuthorityLease? owner)
    {
        lock (_gate) return IsOwner(owner);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string T(string key, params (string Name, object? Value)[] values) => AdminMaintenanceText.T(key, values);

    /// <summary>Whether a command error leaves the server state unknown: an unproven receipt, 408, any 5xx, a transport failure or a deadline.</summary>
    internal static bool IsPossiblyAccepted(Exception error, MonitorAuthorityLease lease) => error switch
    {
        AdminCatalogOutcomeUnknownException => true,
        ApiException api => api.StatusCode is 408 or >= 500,
        OperationCanceledException when error.InnerException is TimeoutException => true,
        OperationCanceledException => false,
        HttpRequestException or IOException => lease.IsCurrent,
        _ => false,
    };

    /// <summary>A safe localized reason for a definite refusal; server problem detail is never shown.</summary>
    internal static string FailureText(Exception error, string fallbackKey) => AdminFailures.Classify(error) switch
    {
        AdminFailureKind.Unauthorized => T("admin.native.maintenance.error.sessionExpired"),
        AdminFailureKind.DemoRestricted => T("admin.native.maintenance.error.demo"),
        AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired => T("admin.native.maintenance.error.forbidden"),
        AdminFailureKind.NotFound => T("admin.native.maintenance.error.notFound"),
        AdminFailureKind.RateLimited => T("admin.native.maintenance.error.rateLimited"),
        AdminFailureKind.CapabilityUnavailable => T("admin.native.maintenance.error.storageUnavailable"),
        AdminFailureKind.Validation when error is ApiException { StatusCode: 400 } => T("admin.native.maintenance.error.sourceUnavailable"),
        AdminFailureKind.Validation => T("admin.native.maintenance.error.invalidImport"),
        AdminFailureKind.InvalidResponse => T("admin.native.maintenance.error.invalidResponse"),
        _ => T(fallbackKey),
    };

    private void Refuse(MonitorAuthorityLease owner, string reason, IReadOnlyList<string> roots) =>
        Update(() => { if (IsOwner(owner)) _state = _state with { ImportError = reason, RequiredRewriteRoots = roots }; });

    /// <summary>Reads the job a 409 named and refreshes only that job's catalog list; without a usable Location, the command's own list.</summary>
    private async Task AdoptConflictAsync(MonitorAuthorityLease owner, string? location, Chain<AdminMaintenanceJob> fallback)
    {
        var jobId = AdminMaintenanceApi.JobIdFromLocation(location);
        if (jobId == null) { await RefreshAsync(fallback).ConfigureAwait(false); return; }
        AdminMaintenanceJob job;
        try { job = await _api.GetJobAsync(owner, jobId, owner.Token).ConfigureAwait(false); }
        catch (Exception error) when (AdminFailures.Classify(error, owner) is not (AdminFailureKind.Retired or AdminFailureKind.Cancelled))
        {
            await RefreshAsync(fallback).ConfigureAwait(false);
            return;
        }
        catch (Exception) { return; }
        var chain = job.Kind switch { AdminMaintenanceApi.ExportKind => _exports, AdminMaintenanceApi.ImportKind => _imports, _ => null };
        Update(() => { if (IsOwner(owner) && chain != null) _state = _state with { ConflictJob = job }; });
        if (chain != null) await RefreshAsync(chain).ConfigureAwait(false);
    }

    private MonitorAuthorityLease? BeginCommand(Func<AdminMaintenanceState, bool> allowed, Func<AdminMaintenanceState, AdminMaintenanceState> mark)
    {
        MonitorAuthorityLease? owner = null;
        Update(() =>
        {
            if (_owner is not { IsCurrent: true } current || !allowed(_state)) return;
            _state = mark(_state);
            owner = current;
        });
        return owner;
    }

    private void EndCommand(MonitorAuthorityLease owner, Func<AdminMaintenanceState, AdminMaintenanceState> clear) =>
        Update(() => { if (IsOwner(owner)) _state = clear(_state); });

    private void Raise(MonitorAuthorityLease owner, AdminMaintenanceNoticeKind kind, string message)
    {
        if (IsCurrentOwner(owner)) Notice?.Invoke(new AdminMaintenanceNotice(kind, message));
    }

    private bool IsOwner(MonitorAuthorityLease? owner) => !_disposed && owner != null && ReferenceEquals(owner, _owner) && owner.IsCurrent;

    private IEnumerable<Action> AllResets() => [_exports.Reset, _imports.Reset, _history.Reset, _bucket.Reset, _local.Reset];

    private IEnumerable<Action> ResetsOf(AdminMaintenanceListKind kind) => kind switch
    {
        AdminMaintenanceListKind.Exports => [_exports.Reset],
        AdminMaintenanceListKind.Imports => [_imports.Reset],
        AdminMaintenanceListKind.History => [_history.Reset],
        AdminMaintenanceListKind.BucketSources => [_bucket.Reset],
        _ => [_local.Reset],
    };

    /// <summary>Rebuilds the list snapshots into the state. Call under the gate.</summary>
    private void Publish() => _state = _state with
    {
        Exports = _exports.Snapshot(), Imports = _imports.Snapshot(), History = _history.Snapshot(),
        BucketSources = _bucket.Snapshot(), LocalSources = _local.Snapshot(),
    };

    private void Update(Action change)
    {
        AdminMaintenanceState before, after;
        lock (_gate)
        {
            if (_disposed) return;
            before = _state;
            change();
            after = _state;
        }
        if (!ReferenceEquals(before, after)) Changed?.Invoke();
    }

    /// <summary>Authority loss: drop every list, cursor, draft, pending marker and unknown-outcome block of the old authority.</summary>
    private void OnRetired()
    {
        Update(() =>
        {
            _owner = null;
            foreach (var reset in AllResets()) reset();
            _state = new AdminMaintenanceState();
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var reset in AllResets()) reset();
            _owner = null;
            _state = new AdminMaintenanceState();
            _disposed = true;
        }
        _lifetime.Retired -= OnRetired;
        _lifetime.Dispose();
        Changed = null; Notice = null; ImportCompleted = null;
    }
}
