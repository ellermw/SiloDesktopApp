using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Why a restore did not start (official RestoreRefusal).</summary>
public sealed record AdminRestoreRefusal(string Message, string? RunningJobId, bool PreviewChanged);

public sealed record AdminBackupRestoreState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public AdminBackupRestoreSourceRef? Source { get; init; }
    public AdminBackupCapabilities? Capabilities { get; init; }
    public AdminBackupList? List { get; init; }
    public AdminBackupUpload? Upload { get; init; }
    public IReadOnlyList<AdminBackupLocalLibrary> Libraries { get; init; } = [];
    public IReadOnlyList<AdminBackupRestoreJob> Jobs { get; init; } = [];

    public AdminBackupRestoreSet? Preview { get; init; }
    public bool PreviewLoading { get; init; }
    public Exception? PreviewError { get; init; }
    /// <summary>The preview answered 409 backup_unauthenticated: nothing inside was read; opening it needs explicit consent.</summary>
    public bool Unauthenticated { get; init; }
    /// <summary>The admin chose "Open anyway"; only then is accept_unauthenticated sent.</summary>
    public bool OpenedUnverified { get; init; }

    public IReadOnlySet<string> Chosen { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DataTypes { get; init; } = AdminBackupRestoreModel.DefaultDataTypes;
    public string? Notice { get; init; }
    public bool Reviewing { get; init; }
    public bool AcceptUnauthenticated { get; init; }
    public bool Starting { get; init; }
    public AdminRestoreRefusal? Refusal { get; init; }
    /// <summary>The restore POST may have reached the server without an answer that names its job; Restore stays blocked.</summary>
    public bool OutcomeUnknown { get; init; }

    public string? JobId { get; init; }
    public AdminBackupRestoreJob? Job { get; init; }
    public Exception? JobError { get; init; }
    public bool Cancelling { get; init; }

    /// <summary>Choose, Review, Restore (official STEPS).</summary>
    public int Step => JobId != null ? 2 : Reviewing ? 1 : 0;
    public override string ToString() => nameof(AdminBackupRestoreState);
}

/// <summary>
/// Page-owned selective restore (official AdminBackupRestore.tsx, RestoreChoose/Review/Run at 92ee051).
/// The preview is read once per opening and only on explicit request after that; a restore sends the
/// digest of the preview the admin reviewed, the selection built from that same preview, and
/// <c>accept_unauthenticated</c> only after the admin's explicit consent. The POST is sent once: a
/// 409 restore_set_changed asks for a fresh preview and review, a 409 conflict offers the running job,
/// and an unknown outcome reconciles only by the job its own <c>Location</c> named, or a running job of
/// this exact source; otherwise Restore stays blocked until the preview is read and reviewed again.
/// The job is read every 2 s until terminal while the window is visible; Stop applies only to the
/// displayed job after confirmation. Events may arrive on any thread.
/// </summary>
public sealed class AdminBackupRestoreController : IDisposable
{
    internal static readonly TimeSpan JobInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan DeferRetry = TimeSpan.FromSeconds(1);
    private readonly AdminBackupsApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly IAdminScheduler _scheduler;
    private readonly object _gate = new();
    private AdminBackupRestoreState _state = new();
    private IReadOnlyDictionary<string, AdminRestoreEntry> _index = new Dictionary<string, AdminRestoreEntry>();
    private IDisposable? _jobTimer;
    private long _generation, _jobGeneration;
    private bool _jobInFlight, _jobAgain, _visible = true, _disposed;
    private readonly HashSet<string> _finishedJobs = new(StringComparer.Ordinal);

    public AdminBackupRestoreController(AdminBackupsApi api, AdminMonitorAuthority authority, IAdminScheduler? scheduler = null)
    {
        _api = api; _scheduler = scheduler ?? new SystemAdminScheduler();
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    /// <summary>The job id the page should show in its address (<c>?job=</c>), or null to clear it.</summary>
    public event Action<string?>? JobChanged;
    /// <summary>A restore ended; restart status may have changed.</summary>
    public event Action? Finished;
    public event Action<AdminBackupNotice>? Notified;
    public Func<bool> DeferBackground { get; set; } = static () => false;
    public AdminBackupRestoreState State { get { lock (_gate) return _state; } }

    /// <summary>The effective selection: only what the current preview still offers.</summary>
    public IReadOnlySet<string> Selection { get { lock (_gate) return AdminBackupRestoreModel.Effective(_index, _state.Chosen); } }

    public async Task OpenAsync(AdminBackupRestoreSourceRef source, string? jobId)
    {
        if (source.Upload ? !AdminBackupIdentifiers.IsUploadId(source.Id) : !AdminBackupIdentifiers.IsBackupId(source.Id)) throw new ArgumentException("Invalid restore source.", nameof(source));
        if (jobId != null && !AdminBackupIdentifiers.IsJobId(jobId)) jobId = null;
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            RetireJobRead(); _generation++;
            _finishedJobs.Clear();
            _index = new Dictionary<string, AdminRestoreEntry>();
            _state = new() { Owner = owner, Source = source, JobId = jobId, PreviewLoading = jobId == null, OutcomeUnknown = _api.HasUnconfirmedRestore(owner, source) };
        }
        Changed?.Invoke();
        var reads = new List<Task> { ReadContextAsync(owner, source), ReadJobsAsync(owner, source, jobId == null) };
        if (jobId != null) reads.Add(ReadJobAsync(owner, background: false));
        else reads.Add(ReadPreviewAsync(owner, source, accept: false));
        await Task.WhenAll(reads).ConfigureAwait(false);
    }

    public void SetVisible(bool visible)
    {
        MonitorAuthorityLease? owner;
        lock (_gate)
        {
            if (_visible == visible) return;
            _visible = visible; owner = _lifetime.Owner;
            if (!visible) { _jobTimer?.Dispose(); _jobTimer = null; return; }
        }
        var state = State;
        if (owner is { IsCurrent: true } && state.JobId != null && state.Job?.Terminal != true) _ = ReadJobAsync(owner, background: false);
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }

    private void Clear()
    {
        lock (_gate)
        {
            RetireJobRead(); _generation++;
            _finishedJobs.Clear();
            _index = new Dictionary<string, AdminRestoreEntry>(); _state = new();
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); Changed = null; JobChanged = null; Finished = null; Notified = null;
    }

    // ── Reads ────────────────────────────────────────────────────────

    private async Task ReadContextAsync(MonitorAuthorityLease owner, AdminBackupRestoreSourceRef source)
    {
        long generation;
        lock (_gate) generation = _generation;
        try
        {
            var capabilities = await _api.GetCapabilitiesAsync(owner).ConfigureAwait(false);
            Update(owner, generation, () => _state = _state with { Capabilities = capabilities });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
        await ReadListAsync(owner).ConfigureAwait(false);
        if (source.Upload)
            try
            {
                var uploads = await _api.ListUploadsAsync(owner).ConfigureAwait(false);
                Update(owner, generation, () => _state = _state with { Upload = uploads.FirstOrDefault(upload => upload.Id == source.Id) });
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { }
            catch { }
    }

    private async Task ReadListAsync(MonitorAuthorityLease owner)
    {
        long generation;
        lock (_gate) generation = _generation;
        try
        {
            var list = await _api.ListAsync(owner).ConfigureAwait(false);
            Update(owner, generation, () => _state = _state with { List = list });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { /* The heading and recovery-point names fall back to what the preview or report carries. */ }
    }

    /// <summary>Recent jobs for the last-run line; a running restore of this source opens instead of the chooser.</summary>
    private async Task ReadJobsAsync(MonitorAuthorityLease owner, AdminBackupRestoreSourceRef source, bool openRunning)
    {
        long generation;
        lock (_gate) generation = _generation;
        IReadOnlyList<AdminBackupRestoreJob> jobs;
        try { jobs = await _api.ListRestoreJobsAsync(owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { return; }
        var mine = jobs.Where(job => AdminBackupValidation.NamesSource(job, source)).ToArray();
        string? running = null;
        Update(owner, generation, () =>
        {
            _state = _state with { Jobs = mine };
            if (openRunning && _state.JobId == null && !_state.Starting && mine.FirstOrDefault(job => job.Terminal != true) is { } active)
            {
                running = active.Id;
                RetireJobRead();
                _state = _state with { JobId = active.Id, Job = active, Reviewing = false, Refusal = null };
            }
        });
        if (running != null) { JobChanged?.Invoke(running); _ = ReadJobAsync(owner, background: false); }
    }

    private async Task ReadPreviewAsync(MonitorAuthorityLease owner, AdminBackupRestoreSourceRef source, bool accept)
    {
        long generation;
        lock (_gate) generation = _generation;
        var libraries = _api.GetLibrariesAsync(owner);
        try
        {
            var set = await _api.GetRestoreSetAsync(owner, source, accept).ConfigureAwait(false);
            IReadOnlyList<AdminBackupLocalLibrary> local = [];
            try { local = await libraries.ConfigureAwait(false); } catch (Exception error) when (error is not OperationCanceledException || owner.IsCurrent) { }
            Update(owner, generation, () =>
            {
                _index = AdminBackupRestoreModel.Index(set);
                _state = _state with
                {
                    Preview = set, PreviewLoading = false, PreviewError = null, Unauthenticated = false, Libraries = local,
                    // A reloaded preview can make earlier choices unavailable; only what it still offers stays chosen.
                    Chosen = AdminBackupRestoreModel.Effective(_index, _state.Chosen),
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (ApiException error) when (error.StatusCode == 409 && error.ProblemType == "backup_unauthenticated")
        {
            Update(owner, generation, () => _state = _state with { PreviewLoading = false, Unauthenticated = true, Preview = null, PreviewError = null });
        }
        catch (Exception error)
        {
            Update(owner, generation, () => _state = _state with { PreviewLoading = false, PreviewError = error, Preview = null });
        }
        finally { _ = libraries.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default); }
    }

    /// <summary>"Open anyway": the admin's explicit consent to read a backup this server cannot verify.</summary>
    public Task OpenUnverifiedAsync()
    {
        var owner = _lifetime.Owner;
        AdminBackupRestoreSourceRef? source;
        lock (_gate)
        {
            if (owner is not { IsCurrent: true } || !_state.Unauthenticated || _state.Source == null) return Task.CompletedTask;
            source = _state.Source;
            _state = _state with { OpenedUnverified = true, AcceptUnauthenticated = true, PreviewLoading = true, Unauthenticated = false };
        }
        Changed?.Invoke();
        return ReadPreviewAsync(owner, source, accept: true);
    }

    /// <summary>Read the preview again (Try again, Reload the preview after a change or an unknown outcome).</summary>
    public Task ReloadPreviewAsync()
    {
        var owner = _lifetime.Owner;
        AdminBackupRestoreSourceRef? source;
        bool accept;
        lock (_gate)
        {
            if (owner is not { IsCurrent: true } || _state.Source == null || _state.JobId != null || _state.Starting) return Task.CompletedTask;
            if (!_api.AcknowledgeRestoreOutcome(owner, _state.Source)) return Task.CompletedTask;
            source = _state.Source; accept = _state.OpenedUnverified;
            _state = _state with { Preview = null, PreviewLoading = true, Reviewing = false, Refusal = null, OutcomeUnknown = false };
        }
        Changed?.Invoke();
        return ReadPreviewAsync(owner, source, accept);
    }

    private async Task ReadJobAsync(MonitorAuthorityLease owner, bool background, long? expectedJobGeneration = null)
    {
        long generation, jobGeneration;
        string? jobId;
        lock (_gate)
        {
            jobId = _state.JobId;
            if (_disposed || jobId == null || !_lifetime.IsCurrent(owner) || expectedJobGeneration is { } expected && expected != _jobGeneration) return;
            if (_jobInFlight) { if (!background) _jobAgain = true; return; }
            jobGeneration = _jobGeneration;
            if (background && (!_visible || DeferBackground())) { ScheduleJob(owner, DeferRetry, jobGeneration); return; }
            _jobInFlight = true; _jobAgain = false; generation = _generation;
        }
        AdminBackupRestoreJob? job = null;
        Exception? failure = null;
        try { job = await _api.GetJobAsync(owner, jobId).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { failure = error; }
        var finished = false;
        bool again;
        lock (_gate)
        {
            if (_disposed || generation != _generation || jobGeneration != _jobGeneration || !_lifetime.IsCurrent(owner)) return;
            _jobInFlight = false;
            again = _jobAgain; _jobAgain = false;
            if (_state.JobId != jobId) return;
            if (job != null && _state.Source is { } source && !AdminBackupValidation.NamesSource(job, source))
            {
                // A job that is not this source's restore is never shown here.
                failure = new InvalidDataException("The job restores another source."); job = null;
            }
            if (job != null)
            {
                finished = AcceptJob(job);
            }
            else if (failure != null) _state = _state with { JobError = failure };
        }
        Changed?.Invoke();
        if (finished)
        {
            await CompleteJobAsync(owner, generation).ConfigureAwait(false);
            return;
        }
        if (again) _ = ReadJobAsync(owner, background: false, expectedJobGeneration: jobGeneration);
        else if (State.Job?.Terminal != true && failure is null or not (ApiException { StatusCode: 404 } or InvalidDataException)) ScheduleJob(owner, JobInterval, jobGeneration);
    }

    // Called under _gate by every path that receives a job snapshot.
    private bool AcceptJob(AdminBackupRestoreJob job)
    {
        // An older poll cannot undo a terminal acknowledgement from Stop.
        if (_state.Job is { Terminal: true } previous && previous.Id == job.Id && job.Terminal != true) return false;
        _state = _state with { Job = job, JobError = null };
        if (job.Terminal != true) return false;
        _jobTimer?.Dispose(); _jobTimer = null;
        return _finishedJobs.Add(job.Id!);
    }

    private async Task CompleteJobAsync(MonitorAuthorityLease owner, long generation)
    {
        await ReadListAsync(owner).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || generation != _generation || !_lifetime.IsCurrent(owner)) return;
        }
        try { Finished?.Invoke(); } catch { }
    }

    // Called under _gate when leaving a report or adopting a job. Old reads cannot release a new flight.
    private void RetireJobRead()
    {
        _jobTimer?.Dispose(); _jobTimer = null;
        _jobGeneration++; _jobInFlight = false; _jobAgain = false;
    }

    private void ScheduleJob(MonitorAuthorityLease owner, TimeSpan delay, long jobGeneration)
    {
        lock (_gate)
        {
            if (_disposed || !_visible || jobGeneration != _jobGeneration || _state.JobId == null || _state.Job?.Terminal == true || !_lifetime.IsCurrent(owner)) return;
            _jobTimer?.Dispose();
            _jobTimer = _scheduler.Schedule(delay, () => _ = ReadJobAsync(owner, background: true, expectedJobGeneration: jobGeneration));
        }
    }

    /// <summary>Try again on a failed job read.</summary>
    public Task RetryJobAsync()
    {
        var owner = _lifetime.Owner;
        return owner is { IsCurrent: true } ? ReadJobAsync(owner, background: false) : Task.CompletedTask;
    }

    // ── Choosing and reviewing ───────────────────────────────────────

    public void Toggle(IReadOnlyList<string> keys, bool chosen)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Preview == null || _state.Reviewing || _state.Starting) return;
            var current = AdminBackupRestoreModel.Effective(_index, _state.Chosen);
            var change = chosen ? AdminBackupRestoreModel.Choose(_index, current, keys) : AdminBackupRestoreModel.Unchoose(_index, current, keys);
            _state = _state with { Chosen = change.Selection, Notice = change.Notice };
        });
    }

    public void SetDataType(string id, bool on)
    {
        if (!AdminBackupRestoreModel.DataTypeIds.Contains(id)) throw new ArgumentException("Unknown data type.", nameof(id));
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Reviewing || _state.Starting) return;
            var next = new HashSet<string>(_state.DataTypes, StringComparer.Ordinal);
            if (on) next.Add(id); else next.Remove(id);
            _state = _state with { DataTypes = next };
        });
    }

    public void Review()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Preview != null && !_state.PreviewLoading && !_state.OutcomeUnknown && AdminBackupRestoreModel.Effective(_index, _state.Chosen).Count > 0)
                _state = _state with { Reviewing = true, Refusal = null };
        });
    }

    public void LeaveReview()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Starting) _state = _state with { Reviewing = false, Refusal = null }; });
    }

    public void SetAcceptUnauthenticated(bool accept)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Starting) _state = _state with { AcceptUnauthenticated = accept }; });
    }

    /// <summary>Whether Restore may be pressed now (official disabled rule plus the unknown-outcome block).</summary>
    public static bool CanRestore(AdminBackupRestoreState state, IReadOnlySet<string> selection) =>
        state is { Reviewing: true, Starting: false, PreviewLoading: false, OutcomeUnknown: false, JobId: null, Preview: { } set } && selection.Count > 0 &&
        (set.Authenticated == true || state.AcceptUnauthenticated);

    /// <summary>Queues the reviewed restore once.</summary>
    public async Task StartAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminBackupRestoreSet reviewed;
            AdminBackupRestoreSelection request;
            AdminBackupRestoreSourceRef source;
            bool accept;
            lock (_gate)
            {
                var selection = AdminBackupRestoreModel.Effective(_index, _state.Chosen);
                if (!_lifetime.IsCurrent(action) || !CanRestore(_state, selection)) return;
                reviewed = _state.Preview!; source = _state.Source!;
                request = AdminBackupRestoreModel.SelectionRequest(reviewed, selection, _state.DataTypes);
                // Consent applies only to the preview the admin opened unverified and reviewed.
                accept = reviewed.Authenticated != true && _state.AcceptUnauthenticated;
                _state = _state with { Starting = true, Refusal = null };
            }
            Changed?.Invoke();
            try
            {
                var started = await _api.StartRestoreAsync(action, source, reviewed.Digest!, request, accept).ConfigureAwait(false);
                AdoptJob(action, started.Job.Id!, started.Job);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminBackupOutcomeUnknownException unknown) when (unknown.JobId != null)
            {
                // The answer named its own job: reconcile that exact job, never re-POST.
                AdoptJob(action, unknown.JobId, null);
            }
            catch (AdminBackupOutcomeUnknownException)
            {
                Update(action, () => _state = _state with { Starting = false, Reviewing = false, OutcomeUnknown = true });
                await ReconcileUnknownAsync(action, source).ConfigureAwait(false);
            }
            catch (AdminBackupApiException error) when (error.StatusCode == 409)
            {
                var running = AdminBackupsApi.JobIdFromLocation(error.Location);
                if (running != null)
                {
                    try
                    {
                        var conflict = await _api.GetJobAsync(action, running).ConfigureAwait(false);
                        if (!AdminBackupValidation.NamesSource(conflict, source)) running = null;
                    }
                    catch { running = null; }
                }
                Update(action, () => _state = _state with
                {
                    Starting = false,
                    Refusal = new(AdminBackupPresentation.ErrorMessage(error), error.ProblemType == "restore_set_changed" ? null : running, error.ProblemType == "restore_set_changed"),
                });
            }
            catch (Exception error)
            {
                Update(action, () => _state = _state with { Starting = false, Refusal = new(AdminBackupPresentation.ErrorMessage(error), null, false) });
            }
        }
    }

    private void AdoptJob(MonitorAuthorityLease action, string jobId, AdminBackupRestoreJob? job)
    {
        var adopted = false;
        var finished = false;
        long generation = 0, jobGeneration = 0;
        Update(action, () =>
        {
            RetireJobRead();
            jobGeneration = _jobGeneration;
            _state = _state with { Starting = false, Reviewing = false, Refusal = null, OutcomeUnknown = false, JobId = jobId, Job = null, JobError = null };
            if (job != null) finished = AcceptJob(job);
            generation = _generation;
            adopted = true;
        });
        if (!adopted) return;
        JobChanged?.Invoke(jobId);
        if (_lifetime.Owner is { IsCurrent: true } owner)
        {
            if (finished) _ = CompleteJobAsync(owner, generation);
            else if (job?.Terminal != true) ScheduleJob(owner, job == null ? TimeSpan.Zero : JobInterval, jobGeneration);
        }
    }

    /// <summary>
    /// After an unknown outcome with no job named: only a running restore of this exact source is adopted.
    /// Otherwise Restore stays blocked until the preview is read again and reviewed.
    /// </summary>
    public async Task ReconcileUnknownAsync(MonitorAuthorityLease? lease = null, AdminBackupRestoreSourceRef? source = null)
    {
        var owner = lease ?? _lifetime.Owner;
        source ??= State.Source;
        if (owner is not { IsCurrent: true } || source == null || !State.OutcomeUnknown) return;
        IReadOnlyList<AdminBackupRestoreJob> jobs;
        try { jobs = await _api.ListRestoreJobsAsync(owner).ConfigureAwait(false); }
        catch { return; }
        var running = jobs.FirstOrDefault(job => job.Terminal != true && AdminBackupValidation.NamesSource(job, source));
        if (running != null && State.OutcomeUnknown) AdoptJob(owner, running.Id!, running);
    }

    /// <summary>Stops the displayed job only (confirmed by the page). Sent once.</summary>
    public async Task CancelAsync(string displayedJobId)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Cancelling || _state.JobId != displayedJobId || _state.Job is not { Terminal: false, Cancelable: true }) return;
                _state = _state with { Cancelling = true };
            }
            Changed?.Invoke();
            try
            {
                var job = await _api.CancelJobAsync(action, displayedJobId).ConfigureAwait(false);
                var finished = false;
                long generation = 0;
                Update(action, () =>
                {
                    if (_state.JobId == displayedJobId) finished = AcceptJob(job);
                    generation = _generation;
                });
                if (finished && _lifetime.Owner is { IsCurrent: true } readOwner)
                    await CompleteJobAsync(readOwner, generation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error)
            {
                if (_lifetime.IsCurrent(action))
                    Notified?.Invoke(new(AdminText.T("web.admin.backups.restore.stopFailed"),
                        error is AdminBackupOutcomeUnknownException ? AdminText.T("configure.native.backups.unknownOutcome") : AdminBackupPresentation.ErrorMessage(error), true));
            }
            finally { Update(action, () => _state = _state with { Cancelling = false }); }
            if (_lifetime.Owner is { IsCurrent: true } owner) _ = ReadJobAsync(owner, background: false);
        }
    }

    /// <summary>"Choose again": leaves the report for a fresh chooser.</summary>
    public Task ChooseAgainAsync()
    {
        var owner = _lifetime.Owner;
        AdminBackupRestoreSourceRef? source;
        bool accept;
        lock (_gate)
        {
            if (owner is not { IsCurrent: true } || _state.Source == null || _state.Job?.Terminal != true) return Task.CompletedTask;
            if (!_api.AcknowledgeRestoreOutcome(owner, _state.Source)) return Task.CompletedTask;
            RetireJobRead();
            source = _state.Source; accept = _state.OpenedUnverified;
            _index = new Dictionary<string, AdminRestoreEntry>();
            _state = _state with { JobId = null, Job = null, JobError = null, Preview = null, PreviewLoading = true, Chosen = new HashSet<string>(), Reviewing = false, Notice = null };
        }
        Changed?.Invoke();
        JobChanged?.Invoke(null);
        return Task.WhenAll(ReadPreviewAsync(owner, source, accept), ReadJobsAsync(owner, source, openRunning: false));
    }

    // ── Plumbing ─────────────────────────────────────────────────────

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        long generation;
        lock (_gate) generation = _generation;
        Update(owner, generation, update);
    }

    private void Update(MonitorAuthorityLease owner, long generation, Action update)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || !_lifetime.IsCurrent(owner)) return;
            update();
        }
        Changed?.Invoke();
    }
}
