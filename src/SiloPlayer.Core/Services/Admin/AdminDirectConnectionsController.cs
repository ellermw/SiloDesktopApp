using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsRules;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>What the last Run check ended in when it did not publish a result.</summary>
public enum AdminDirectConnectionsCheckOutcome { None, Refused, Unknown }

public sealed record AdminDirectConnectionsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    // The settings form (the three direct_connections.* keys).
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    /// <summary>The latest settings read failed; the page shows it with Reload. A loaded draft stays.</summary>
    public AdminFailureKind? LoadFailure { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    /// <summary>The last save was refused (a 4xx other than a stale validator) or failed definitely.</summary>
    public AdminFailureKind? SaveFailure { get; init; }
    /// <summary>The server's one-line refusal, bounded and redacted; plain text.</summary>
    public string? SaveDetail { get; init; }
    public bool Saving { get; init; }
    public bool CanSave { get; init; }
    public bool RestartRequired { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    /// <summary>The STUN draft checked against the server's rules; only a draft the admin edited is checked.</summary>
    public AdminStunCheck Stun { get; init; } = AdminStunCheck.Valid;
    /// <summary>Whether the section exists (saved enabled, or any key staged).</summary>
    public bool SectionVisible { get; init; }
    // Status.
    /// <summary>No status has been read yet and a read is pending or running (official isLoading).</summary>
    public bool StatusLoading { get; init; }
    public AdminDirectConnectionsStatus? Status { get; init; }
    /// <summary>The latest read failed. The last good status, if any, stays shown with it.</summary>
    public AdminFailureKind? StatusFailure { get; init; }
    public int? StatusLimit { get; init; }
    public bool Checking { get; init; }
    public AdminDirectConnectionsCheckOutcome CheckOutcome { get; init; }
    public AdminFailureKind? CheckFailure { get; init; }
    public string? CheckDetail { get; init; }
    public bool Polling { get; init; }
    public long Revision { get; init; }
    public override string ToString() => nameof(AdminDirectConnectionsState);
}

/// <summary>
/// Configure › Network access › Direct connections state owner (official <c>DirectConnectionsSection.tsx</c> with
/// <c>useSettingsForm</c> and <c>directConnections.ts</c>).
/// <list type="bullet">
/// <item>The three settings are one batched form read and written through the shared settings API; an untouched form sends
/// nothing, a draft starts from the raw saved values, a key the environment sets is never sent, and the STUN list is checked
/// with the server's rules before Save.</item>
/// <item>Status reads begin when the section appears (saved enabled, or a staged edit) and stop when it disappears. They poll
/// every 15 s only while <see cref="SetActive"/> is true and playback is not loading; a failed poll keeps the data and keeps polling.</item>
/// <item>Request order decides: a poll that started before a Run check result was published is dropped; a check result always
/// replaces the status; a result is applied only for the owner, section and request that asked.</item>
/// <item>Run check sends one refresh with no replay. A refusal shows the server's detail; a timeout or transport failure is an
/// unknown outcome and the state is re-read, not re-sent.</item>
/// </list>
/// Events may arrive on any thread; marshal before touching UI.
/// </summary>
public sealed class AdminDirectConnectionsController : IDisposable
{
    private readonly AdminServerSettingsApi _settings;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminDirectConnectionsApi _direct;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly Func<bool> _deferBackground;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminDirectConnectionsState _state = new();
    private MonitorAuthorityLease? _owner;
    private ITimer? _poll;
    private CancellationTokenSource? _statusRead;
    private AdminDirectConnectionsStatus? _status;
    private AdminFailureKind? _statusFailure;
    private int? _statusLimit;
    private DateTimeOffset _statusReadAt = DateTimeOffset.MinValue;
    private long _statusSeq, _checkSeq, _publications, _revision;
    private bool _statusRunning, _checking, _active, _disposed;
    private AdminDirectConnectionsCheckOutcome _checkOutcome;
    private AdminFailureKind? _checkFailure;
    private string? _checkDetail;
    private AdminFailureKind? _loadFailure;
    private bool _conflict, _outcomeUnknown, _saving;
    private AdminFailureKind? _saveFailure;
    private string? _saveDetail;
    private AdminEnvManagedSettings _environment = AdminEnvManagedSettings.None;
    private LoggingRestartKeys _restartKeys = new();
    private bool _loading;

    /// <param name="deferBackground">True while foreground navigation or playback start should not share the network with a poll.</param>
    public AdminDirectConnectionsController(AdminServerSettingsApi settings, AdminServerSettingsStore store, AdminDirectConnectionsApi direct,
        AdminMonitorAuthority authority, TimeProvider? time = null, Func<bool>? deferBackground = null)
    {
        _settings = settings; _store = store; _direct = direct;
        _time = time ?? TimeProvider.System;
        _deferBackground = deferBackground ?? (() => false);
        _lifetime = new AdminAuthorityLifetime(authority);
        _lifetime.Retired += OnRetired;
    }

    public event Action? Changed;
    /// <summary>The admin authority changed while open; everything was cleared. Marshal and call <see cref="OpenAsync"/> if still allowed.</summary>
    public event Action? AuthorityRetired;
    /// <summary>The batched PUT was acknowledged with these keys; refresh the shell restart status.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;

    public AdminDirectConnectionsState State { get { lock (_gate) return _state; } }
    public bool IsPolling { get { lock (_gate) return _poll != null; } }

    // ── Opening and reading the settings ─────────────────────────────────────────

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            if (_disposed) return;
            ResetLocked();
            _owner = owner; _loading = true;
            if (_active) StartPollLocked();
            Derive();
        }
        Raise();
        _store.Invalidate();
        await LoadSettingsAsync(owner, reload: false, preserveDraft: false).ConfigureAwait(false);
    }

    /// <summary>Explicit reload of the settings (and, through them, the section). A draft survives when <paramref name="preserveDraft"/> is set.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_saving || _loading || !_lifetime.IsCurrent(action)) return; _loading = true; Derive(); }
            Raise();
            _store.Invalidate();
            await LoadSettingsAsync(action, reload: true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadSettingsAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner);
        var sensitive = _store.GetSensitiveStatusAsync(owner);
        var restart = _store.GetRestartKeysAsync(owner);
        AdminSettingsSensitiveStatus? status = null;
        LoggingRestartKeys restartKeys = new();
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { /* The environment list is optional for a non-secret key; the server still refuses a managed write. */ }
        try { restartKeys = await restart.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(R.Keys, R.Secrets, snapshot.Values, snapshot.ETag);
                _environment = AdminEnvManagedSettings.Resolve(status, _environment).Restrict(R.Keys);
                _form.ApplyEnvironment(_environment);
                _restartKeys = restartKeys;
                _loading = false; _loadFailure = null; _conflict = false; _outcomeUnknown = false; _saveFailure = null; _saveDetail = null;
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () => { _loading = false; _loadFailure = ClassifyRead(error, owner); });
        }
    }

    // ── Editing and saving ───────────────────────────────────────────────────────

    private bool CanEdit(string key) => _form != null && !_loading && !_saving && _state.Loaded && !_environment.IsManaged(key) && !AccessBlocked(_loadFailure);

    private static bool AccessBlocked(AdminFailureKind? failure) => failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden
        or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;

    /// <summary>Stages a value. A value equal to the saved one undoes the edit. Ignored while saving, loading or for a key the environment sets.</summary>
    public bool Set(string key, string value)
    {
        var owner = _lifetime.Owner;
        if (owner == null || !R.Keys.Contains(key)) return false;
        var accepted = false;
        Update(owner, () =>
        {
            if (!CanEdit(key)) return;
            _form!.Set(key, value);
            _saveFailure = null; _saveDetail = null;
            accepted = true;
        });
        return accepted;
    }

    public bool SetEnabled(bool on) => Set(R.Enabled, on ? "true" : "false");
    public bool SetPortMapping(bool on) => Set(R.PortMapping, on ? "true" : "false");
    public bool SetStunServers(string value) => Set(R.StunServers, value);

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_saving) return;
            _form?.Discard();
            _saveFailure = null; _saveDetail = null;
        });
    }

    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.CanSave) return;
                intent = _form.CaptureSave(_environment.Keys);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _saving = true; _saveFailure = null; _saveDetail = null;
                Derive();
            }
            Raise();
            var settled = false;
            try
            {
                var result = await _settings.PutAsync(action, intent.Values, intent.ETag, R.Keys, R.Secrets, keepRefusalDetail: true).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                Update(action, () => { _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired); _saving = false; });
                settled = true;
                if (!_lifetime.IsCurrent(action)) return;
                _store.Invalidate();
                if (Saved is { } handlers)
                    foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                        try { handler(keys); } catch { /* An observer cannot change an acknowledged write. */ }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { settled = true; }
            catch (AdminSettingsConflictException)
            {
                settled = true;
                Update(action, () => { _form?.EndSave(intent); _saving = false; _conflict = true; });
            }
            catch (AdminSettingsOutcomeUnknownException)
            {
                settled = true;
                Update(action, () => { _form?.EndSave(intent); _saving = false; _outcomeUnknown = true; });
            }
            catch (Exception error)
            {
                settled = true;
                var failure = AdminFailures.Classify(error, action);
                Update(action, () =>
                {
                    _form?.EndSave(intent); _saving = false;
                    _saveFailure = failure; _saveDetail = (error as ApiException)?.ProblemDetail;
                });
                // A refusal can mean a restart left one of these keys set by the environment: read the list again so the draft drops.
                if (failure == AdminFailureKind.Validation) await RefreshEnvironmentAsync(action).ConfigureAwait(false);
            }
            finally
            {
                if (!settled) Update(action, () => { _form?.EndSave(intent); _saving = false; });
            }
        }
    }

    private async Task RefreshEnvironmentAsync(MonitorAuthorityLease owner)
    {
        try
        {
            _store.Invalidate();
            var status = await _store.GetSensitiveStatusAsync(owner).ConfigureAwait(false);
            Update(owner, () => { _environment = AdminEnvManagedSettings.Resolve(status, _environment).Restrict(R.Keys); _form?.ApplyEnvironment(_environment); });
        }
        catch { /* The next explicit reload reads it again. */ }
    }

    // ── Status reads, polling and Run check ──────────────────────────────────────

    /// <summary>
    /// Whether the page and its window are in front (official canApplyRealtimeUpdates, plus window activation and the shell's own
    /// suspension). Inactive: no timer. Activation reads again only when the last read is a full interval old.
    /// </summary>
    public void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_disposed || _active == active) return;
            _active = active;
            if (!active) { _poll?.Dispose(); _poll = null; }
            else if (_owner is { } owner && _lifetime.IsCurrent(owner))
            {
                StartPollLocked();
                if (_state.SectionVisible && !_statusRunning && R.PollDue(_statusReadAt, _time.GetUtcNow()) && !_deferBackground()) StartReadLocked(owner);
            }
            Derive();
        }
        Raise();
    }

    /// <summary>Explicit retry after a failed read: a plain read of the last completed check, never a new probe.</summary>
    public void RetryStatus()
    {
        lock (_gate)
        {
            if (_owner is not { } owner || !_lifetime.IsCurrent(owner) || !_state.SectionVisible || _statusRunning) return;
            StartReadLocked(owner);
            Derive();
        }
        Raise();
    }

    /// <summary>Run check: one refresh, never resent. A poll in flight is superseded; the result always replaces the status.</summary>
    public async Task RunCheckAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            long mine;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || !_state.SectionVisible || _checking) return;
                _checking = true; _checkOutcome = AdminDirectConnectionsCheckOutcome.None; _checkFailure = null; _checkDetail = null;
                mine = ++_checkSeq;
                CancelReadLocked();
                Derive();
            }
            Raise();
            try
            {
                var result = await _direct.RefreshAsync(action).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_lifetime.IsCurrent(action) || mine != _checkSeq || !_state.SectionVisible) return;
                    _checking = false; _status = result; _statusFailure = null; _statusLimit = null;
                    _statusReadAt = _time.GetUtcNow(); _publications++;
                    Derive();
                }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminDirectConnectionsOutcomeUnknownException)
            {
                lock (_gate)
                {
                    if (!_lifetime.IsCurrent(action) || mine != _checkSeq) return;
                    _checking = false; _checkOutcome = AdminDirectConnectionsCheckOutcome.Unknown;
                    // The check may have run: read what the server holds now instead of sending it again.
                    if (_owner is { } owner && _state.SectionVisible) StartReadLocked(owner);
                    Derive();
                }
            }
            catch (Exception error)
            {
                var failure = AdminFailures.Classify(error, action);
                lock (_gate)
                {
                    if (!_lifetime.IsCurrent(action) || mine != _checkSeq) return;
                    _checking = false; _checkOutcome = AdminDirectConnectionsCheckOutcome.Refused; _checkFailure = failure;
                    _checkDetail = (error as ApiException)?.ProblemDetail;
                    Derive();
                }
            }
            Raise();
        }
    }

    private void StartReadLocked(MonitorAuthorityLease owner)
    {
        CancelReadLocked();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
        _statusRead = cts; _statusRunning = true;
        var sequence = ++_statusSeq;
        var published = _publications;
        _ = Task.Run(() => RunReadAsync(owner, sequence, published, cts));
    }

    private void CancelReadLocked()
    {
        _statusSeq++;
        if (_statusRead != null) { try { _statusRead.Cancel(); } catch (ObjectDisposedException) { } _statusRead = null; }
        _statusRunning = false;
    }

    private async Task RunReadAsync(MonitorAuthorityLease owner, long sequence, long published, CancellationTokenSource cts)
    {
        AdminDirectConnectionsStatus? result = null;
        Exception? failure = null;
        try { result = await _direct.GetStatusAsync(owner, cts.Token).ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        lock (_gate)
        {
            cts.Dispose();
            if (ReferenceEquals(_statusRead, cts)) _statusRead = null;
            // A superseded, retired or hidden read raises nothing: not even its late failure.
            if (sequence != _statusSeq || _disposed || !ReferenceEquals(_owner, owner) || !_lifetime.IsCurrent(owner) || !_state.SectionVisible) return;
            _statusRunning = false;
            if (result != null)
            {
                // Request order decides: a Run check result published after this read started is newer than it.
                if (R.PollIsCurrent(published, _publications)) { _status = result; _statusFailure = null; _statusLimit = null; _statusReadAt = _time.GetUtcNow(); }
            }
            else
            {
                _statusFailure = ClassifyRead(failure!, owner);
                _statusLimit = (failure as AdminNetworkAccessLimitException)?.Limit;
                _statusReadAt = _time.GetUtcNow();
            }
            Derive();
        }
        Raise();
    }

    private void StartPollLocked()
    {
        _poll?.Dispose();
        _poll = _time.CreateTimer(_ => OnPoll(), null, R.PollInterval, R.PollInterval);
    }

    private void OnPoll()
    {
        lock (_gate)
        {
            if (_disposed || !_active || _owner is not { } owner || !_lifetime.IsCurrent(owner) || !_state.SectionVisible) return;
            // Foreground navigation or playback start keeps the network; the next tick tries again. One read at a time.
            if (_deferBackground() || _statusRunning) return;
            StartReadLocked(owner);
            Derive();
        }
        Raise();
    }

    private static AdminFailureKind ClassifyRead(Exception error, MonitorAuthorityLease lease) =>
        error is OperationCanceledException && lease.IsCurrent ? AdminFailureKind.Transport : AdminFailures.Classify(error, lease);

    // ── State ────────────────────────────────────────────────────────────────────

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            Derive();
        }
        Raise();
    }

    /// <summary>Publishes the state and keeps the status reads in step with the section's existence. Caller holds the gate.</summary>
    private void Derive()
    {
        var state = new AdminDirectConnectionsState { Owner = _owner };
        if (_form != null)
        {
            var values = R.Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal);
            var saved = R.Keys.ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal);
            var dirty = _form.DirtyKeys.ToArray();
            var stunEdited = dirty.Contains(R.StunServers) && !_environment.IsManaged(R.StunServers);
            state = state with
            {
                Loaded = true, Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values),
                PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(saved),
                DirtyKeys = Array.AsReadOnly(dirty), RestartRequired = _form.RestartRequired,
                Stun = stunEdited ? R.CheckStunServers(values[R.StunServers]) : AdminStunCheck.Valid,
            };
        }
        var visible = state.Loaded && R.SectionVisible(state.PersistedValues.GetValueOrDefault(R.Enabled), state.DirtyKeys.Count);
        SyncStatusLocked(visible);
        var settingsDirty = state.DirtyKeys.Any(key => !_environment.IsManaged(key));
        state = state with
        {
            Loading = _loading, LoadFailure = _loadFailure, Conflict = _conflict, OutcomeUnknown = _outcomeUnknown,
            SaveFailure = _saveFailure, SaveDetail = _saveDetail, Saving = _saving,
            EnvManaged = _environment, RestartKeys = _restartKeys, SectionVisible = visible,
            CanSave = settingsDirty && state.Stun.IsValid && !_conflict && !_outcomeUnknown && !AccessBlocked(_loadFailure) && state.Loaded && !_loading && !_saving &&
                _owner is { IsCurrent: true },
            StatusLoading = visible && _status == null && _statusFailure == null,
            Status = visible ? _status : null, StatusFailure = visible ? _statusFailure : null, StatusLimit = visible ? _statusLimit : null,
            Checking = visible && _checking, CheckOutcome = visible ? _checkOutcome : AdminDirectConnectionsCheckOutcome.None,
            CheckFailure = visible ? _checkFailure : null, CheckDetail = visible ? _checkDetail : null,
            Polling = _poll != null, Revision = ++_revision,
        };
        _state = state;
    }

    /// <summary>The section's existence starts and ends the status reads (official: the Diagnostics query mounts with the section).</summary>
    private void SyncStatusLocked(bool visible)
    {
        if (!visible)
        {
            if (_statusRunning || _status != null || _statusFailure != null || _checking || _checkOutcome != AdminDirectConnectionsCheckOutcome.None) ClearStatusLocked();
            return;
        }
        if (_status == null && _statusFailure == null && !_statusRunning && _owner is { } owner && _lifetime.IsCurrent(owner)) StartReadLocked(owner);
    }

    private void ClearStatusLocked()
    {
        CancelReadLocked();
        _checkSeq++;
        _status = null; _statusFailure = null; _statusLimit = null; _statusReadAt = DateTimeOffset.MinValue;
        _checking = false; _checkOutcome = AdminDirectConnectionsCheckOutcome.None; _checkFailure = null; _checkDetail = null;
    }

    private void ResetLocked()
    {
        _poll?.Dispose(); _poll = null;
        ClearStatusLocked();
        _form?.Discard(); _form = null; _owner = null;
        _loading = false; _loadFailure = null; _conflict = false; _outcomeUnknown = false; _saving = false; _saveFailure = null; _saveDetail = null;
        _environment = AdminEnvManagedSettings.None; _restartKeys = new();
        _publications = 0;
        _state = new() { Revision = ++_revision };
    }

    private void OnRetired()
    {
        lock (_gate) { if (_disposed) return; ResetLocked(); }
        Raise();
        AuthorityRetired?.Invoke();
    }

    private void Raise() => Changed?.Invoke();

    /// <summary>Page departure or outer Admin suspension: retire the owner, cancel every read and write, clear all data.</summary>
    public void Suspend()
    {
        _lifetime.Retire();
        Clear();
    }

    private void Clear()
    {
        lock (_gate) ResetLocked();
        Raise();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; ResetLocked(); }
        _lifetime.Dispose();
        Changed = null; AuthorityRetired = null; Saved = null;
    }
}
