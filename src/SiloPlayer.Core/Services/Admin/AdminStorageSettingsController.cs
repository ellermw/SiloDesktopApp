using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>An open transition review. Edits replace the record; Queue captures one immutable command from it.</summary>
public sealed record AdminStorageReview(string Backend, string LocalPath, string Policy);

/// <summary>A transient message the page shows as a toast (official <c>toast.success</c>/<c>toast.error</c>).</summary>
public sealed record AdminStorageNotice(bool Success, string Key, string? JobId = null);

public sealed record AdminStorageSettingsState : IAdminStorageDraft
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    /// <summary>Credential presence/environment status could not be read: the page fails closed until a reload succeeds.</summary>
    public bool SensitiveStatusError { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> Configured { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();

    public AdminArtworkStorageStatus? ArtworkStorage { get; init; }
    /// <summary>A status response arrived or failed at least once for this owner.</summary>
    public bool StatusRead { get; init; }
    /// <summary>The latest status read failed (official <c>serverStatus.isError</c>).</summary>
    public bool StatusFailed { get; init; }
    public bool StatusLoading { get; init; }
    public bool ManagedTransitionsAvailable { get; init; }
    public AdminStorageSourceHealth? Recovery { get; init; }
    public AdminStorageTransitionJob? LatestTransition { get; init; }
    public string? DismissedTransitionId { get; init; }

    public AdminStorageReview? Review { get; init; }
    public AdminStorageSourceHealth? SourceHealth { get; init; }
    public bool SourceHealthFailed { get; init; }
    public bool SourceHealthFetching { get; init; }
    public bool Queuing { get; init; }
    public bool Cancelling { get; init; }
    /// <summary>Catalog key of an ambiguous transition/cancel result: shown until the next explicit command or reload.</summary>
    public string? CommandUnknownKey { get; init; }

    public bool CheckingPublic { get; init; }
    public bool CheckingPrivate { get; init; }
    public AdminSettingsConnectionResult? PublicCheck { get; init; }
    public AdminSettingsConnectionResult? PrivateCheck { get; init; }

    /// <summary>Official <c>storageStatusKnown</c>: read successfully, reported known, and the latest read did not fail.</summary>
    public bool StatusKnown => ArtworkStorage?.StatusKnown == true && !StatusFailed;
    /// <summary>Official full-page lock-status alert: no artwork_storage after a response or a failure.</summary>
    public bool StatusUnavailable => ArtworkStorage == null && StatusRead;
    public bool SourceHealthPending => SourceHealth == null && !SourceHealthFailed;

    public string Get(string key) => Values.GetValueOrDefault(key, "");
    public string GetPersisted(string key) => PersistedValues.GetValueOrDefault(key, "");
    public bool IsDirty(string key) => DirtyKeys.Contains(key);
    public override string ToString() => nameof(AdminStorageSettingsState);
}

/// <summary>
/// Page-owned Configure › Server › Storage state (official StorageSettings.tsx). Settings reuse the shared guarded
/// form/store/API; storage status, transition capabilities, source health and the latest transition job use the
/// separate <see cref="AdminStorageApi"/>. Reads are coalesced per kind and cancelled on retirement. Polling exists
/// only while <see cref="SetActive"/> is true: jobs every 2 s while the newest transition is queued or running, and
/// recovery health every 5 s while recovery is pending. Transitions and cancellations are single-shot: an ambiguous
/// result is reported and re-read, never replayed. Events may arrive on any thread.
/// </summary>
public sealed class AdminStorageSettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys => AdminStorageRules.Keys;
    public static IReadOnlyList<string> Secrets => AdminStorageRules.Secrets;
    public static readonly TimeSpan JobsPollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RecoveryPollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StatusStaleAfter = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan CapabilitiesStaleAfter = TimeSpan.FromSeconds(60);

    private enum ReadKind { Status, Capabilities, Recovery, Jobs, Probe }
    private sealed class Slot { public bool Running; public bool Pending; public DateTimeOffset? Last; }

    private readonly AdminServerSettingsApi _settings;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminStorageApi _storage;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly Func<bool> _deferBackground;
    private readonly object _gate = new();
    private readonly Dictionary<ReadKind, Slot> _slots = Enum.GetValues<ReadKind>().ToDictionary(kind => kind, _ => new Slot());
    private AdminSettingsForm? _form;
    private AdminStorageSettingsState _state = new();
    private string _policy = "preserve_uploads";
    private long _epoch, _revision;
    private ITimer? _jobsPoll, _recoveryPoll;
    private bool _active, _disposed, _reconcilePending;

    /// <param name="deferBackground">True while foreground navigation or playback start should not share the network with a background read.</param>
    public AdminStorageSettingsController(AdminServerSettingsApi settings, AdminServerSettingsStore store, AdminStorageApi storage,
        AdminMonitorAuthority authority, TimeProvider? time = null, Func<bool>? deferBackground = null)
    {
        _settings = settings; _store = store; _storage = storage; _time = time ?? TimeProvider.System;
        _deferBackground = deferBackground ?? (() => false);
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    /// <summary>Raised after a settings write, queue acknowledgement or transition commitment so the shell can refresh restart status.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    public event Action<AdminStorageNotice>? Notice;
    public AdminStorageSettingsState State { get { lock (_gate) return _state; } }
    public bool IsJobsPolling { get { lock (_gate) return _jobsPoll != null; } }
    public bool IsRecoveryPolling { get { lock (_gate) return _recoveryPoll != null; } }

    // ── Lifetime ─────────────────────────────────────────────

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        // Other pages or admin sessions may have changed values and the shared ETag.
        _store.Invalidate();
        lock (_gate)
        {
            _form?.Discard(); _form = null; _epoch++; _revision++; ResetSlotsLocked();
            _state = new() { Owner = owner, Loading = true, StatusLoading = true };
        }
        Changed?.Invoke();
        Start(ReadKind.Status, false); Start(ReadKind.Capabilities, false); Start(ReadKind.Jobs, false);
        await LoadSettingsAsync(owner, false, false).ConfigureAwait(false);
    }

    /// <summary>Page and window activity. Polling and focus refresh happen only while true.</summary>
    public void SetActive(bool active)
    {
        bool refresh;
        lock (_gate)
        {
            refresh = active && !_active && _state.Owner is { IsCurrent: true } && !_disposed;
            _active = active;
        }
        if (refresh)
        {
            // Official queries refetch stale data on focus: jobs/health are always stale, status after 15 s, capabilities after 60 s.
            Start(ReadKind.Jobs, true);
            if (State.ManagedTransitionsAvailable) Start(ReadKind.Recovery, true);
            if (IsStale(ReadKind.Status, StatusStaleAfter)) Start(ReadKind.Status, true);
            if (IsStale(ReadKind.Capabilities, CapabilitiesStaleAfter)) Start(ReadKind.Capabilities, true);
        }
        EvaluatePolls();
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); Changed = null; Saved = null; Notice = null;
    }

    private void Clear()
    {
        lock (_gate)
        {
            _form?.Discard(); _form = null; _epoch++; _revision++; ResetSlotsLocked(); StopPollsLocked();
            _state = new();
        }
        Changed?.Invoke();
    }

    // ── Settings snapshot ────────────────────────────────────

    /// <summary>Explicit reconciliation after a conflict, an unknown outcome, a failed read or a fail-closed status gap.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Saving || _state.Loading || _state.Queuing || _state.Review != null) return;
                _state = _state with { Loading = true, Error = null, CommandUnknownKey = null, PublicCheck = null, PrivateCheck = null };
            }
            _store.Invalidate();
            Changed?.Invoke();
            Start(ReadKind.Status, false); Start(ReadKind.Capabilities, false); Start(ReadKind.Jobs, false);
            await LoadSettingsAsync(action, true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadSettingsAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner);
        var sensitive = _store.GetSensitiveStatusAsync(owner);
        var restart = _store.GetRestartKeysAsync(owner);
        AdminSettingsSensitiveStatus? status = null;
        LoggingRestartKeys restartKeys = new();
        var sensitiveError = false;
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { sensitiveError = true; }
        try { restartKeys = await restart.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { /* Restart chips are optional: failure means none. */ }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(Keys, Secrets, snapshot.Values, snapshot.ETag);
                // Without presence the page cannot tell keep from clear: never keep a credential draft across that gap.
                if (sensitiveError) foreach (var key in Secrets) _form.Reset(key);
                // A formerly editable draft must not survive becoming environment-owned.
                foreach (var key in (status?.ManagedByEnv ?? []).Where(Keys.Contains)) _form.Reset(key);
                _revision++;
                _state = _state with
                {
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    SensitiveStatusError = sensitiveError,
                    Configured = Array.AsReadOnly((status?.Configured ?? []).Where(Secrets.Contains).ToArray()),
                    ManagedByEnv = Array.AsReadOnly((status?.ManagedByEnv ?? []).Where(Keys.Contains).ToArray()),
                    RestartKeys = restartKeys, PublicCheck = null, PrivateCheck = null,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner) });
        }
        TryStartReconciliation();
    }

    // Coalesce job-driven invalidations without adopting a snapshot over a pending write or review.
    private void TryStartReconciliation()
    {
        MonitorAuthorityLease action;
        lock (_gate)
        {
            if (!_reconcilePending || _disposed || _state.Owner is not { IsCurrent: true } ||
                _state.Loading || _state.Saving || _state.Queuing || _state.Review != null) return;
            try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
            _reconcilePending = false;
            _state = _state with { Loading = true, PublicCheck = null, PrivateCheck = null };
        }
        _ = ReconcileAsync(action);
    }

    private async Task ReconcileAsync(MonitorAuthorityLease action)
    {
        using (action)
        {
            try
            {
                action.ThrowIfRetired();
                _store.Invalidate();
                Changed?.Invoke();
                Start(ReadKind.Status, false);
                if (State.ManagedTransitionsAvailable) Start(ReadKind.Recovery, false);
                if (_lifetime.IsCurrent(action)) RaiseSaved(AdminStorageRules.TransitionKeys.ToArray());
                await LoadSettingsAsync(action, true, preserveDraft: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
        }
    }

    // ── Edits ────────────────────────────────────────────────

    private bool CanEdit(string key)
    {
        var s = _state;
        if (_form == null || !s.Loaded || s.Loading || s.SensitiveStatusError || s.Review != null || s.Queuing || s.ManagedByEnv.Contains(key)) return false;
        if (Secrets.Contains(key) && s.Saving) return false;
        if (AdminStorageRules.S3IdentityKeys.Contains(key) && (s.Saving || !s.StatusKnown)) return false;
        if (key == AdminStorageRules.LocalPath && (s.Saving || !s.StatusKnown || (s.ArtworkStorage!.Locked && AdminStorageRules.CurrentSourceIsS3(s.ArtworkStorage)))) return false;
        if (key == AdminStorageRules.Backend && !s.StatusKnown) return false;
        return true;
    }

    /// <summary>A plain setting edit. Backend changes go through <see cref="ChangeBackend"/>; credentials through the secret methods.</summary>
    public void Set(string key, string value)
    {
        if (key == AdminStorageRules.Backend) { ChangeBackend(value); return; }
        if (Secrets.Contains(key)) { SetSecret(key, value); return; }
        Edit(key, form => form.Set(key, value));
    }

    /// <summary>Typing stages a replacement; emptying the input keeps a saved credential (official SecretField).</summary>
    public void SetSecret(string key, string value) => Edit(RequireSecret(key), form =>
    {
        if (value.Length == 0 && _state.Configured.Contains(key)) form.Reset(key);
        else form.Set(key, value);
    });
    /// <summary>Explicitly stages erasing a saved credential.</summary>
    public void ClearSecret(string key) => Edit(RequireSecret(key), form => { if (_state.Configured.Contains(key)) form.Set(key, "", stage: true); });
    public void KeepSecret(string key) => Edit(RequireSecret(key), form => form.Reset(key));
    private static string RequireSecret(string key) => Secrets.Contains(key) ? key : throw new ArgumentException("Not a storage credential.", nameof(key));

    /// <summary>
    /// Official <c>handleArtworkBackendChange</c>: a locked store moving to S3 is staged so Public storage can be
    /// completed; a locked store moving to local disk opens its transition review at once.
    /// </summary>
    public void ChangeBackend(string value)
    {
        if (value is not ("auto" or "local" or "s3")) return;
        var owner = _lifetime.Owner;
        if (owner == null) return;
        AdminStorageNotice? notice = null;
        Update(owner, () =>
        {
            if (!CanEdit(AdminStorageRules.Backend)) return;
            var status = _state.ArtworkStorage!;
            var requested = AdminStorageRules.EffectiveBackend(value, _form!.Get("s3.public_bucket"));
            if (status.Locked && status.Backend.Length > 0 && requested != status.Backend)
            {
                if (!_state.ManagedTransitionsAvailable) { notice = new(false, "web.admin.settings.storage.transitionsUnavailable"); return; }
                if (requested == "s3") { _form.Set(AdminStorageRules.Backend, value); Edited(AdminStorageRules.Backend); return; }
                OpenReviewLocked(requested);
                return;
            }
            _form.Set(AdminStorageRules.Backend, value); Edited(AdminStorageRules.Backend);
        });
        Raise(notice);
        if (State.Review != null) StartProbeIfNeeded();
    }

    private void Edit(string key, Action<AdminSettingsForm> edit)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (CanEdit(key)) { edit(_form!); Edited(key); } });
    }

    private void Edited(string key)
    {
        _revision++;
        // A connection result describes the values it checked; any edit to that group hides it.
        if (AdminStorageRules.PublicS3Keys.Contains(key)) _state = _state with { PublicCheck = null };
        if (AdminStorageRules.PrivateS3Keys.Contains(key)) _state = _state with { PrivateCheck = null };
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Saving || _state.Queuing || _state.Review != null) return;
            _form?.Discard(); _revision++;
            _state = _state with { PublicCheck = null, PrivateCheck = null };
        });
    }

    // ── Save ─────────────────────────────────────────────────

    /// <summary>Official <c>handleSave</c>: a location change opens the transition review instead of writing it.</summary>
    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            IReadOnlyList<string>? only = null;
            string? reviewBackend = null;
            AdminStorageNotice? notice = null;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.Loaded || _state.Loading || _state.Saving || _state.Queuing || _reconcilePending ||
                    _state.Review != null || _state.Conflict || _state.OutcomeUnknown || _state.SensitiveStatusError || _state.ArtworkStorage == null) return;
                var dirty = _form.DirtyKeys.Where(key => !_state.ManagedByEnv.Contains(key)).ToArray();
                if (!_state.StatusKnown)
                {
                    only = AdminStorageRules.SafeKeysWhileStatusUnknown(dirty);
                    if (only.Count == 0) notice = new(false, "web.admin.settings.storage.lockUnknownSave");
                }
                else if (AdminStorageRules.LocationChangePending(new FormDraft(_form), _state.ArtworkStorage, _state.LatestTransition))
                {
                    if (!_state.ManagedTransitionsAvailable) notice = new(false, "web.admin.settings.storage.transitionsUnavailable");
                    else
                    {
                        only = dirty.Where(key => !AdminStorageRules.TransitionKeys.Contains(key)).ToArray();
                        reviewBackend = AdminStorageRules.DraftEffectiveBackend(new FormDraft(_form));
                    }
                }
            }
            if (notice != null) { Raise(notice); return; }
            if (reviewBackend == null || only!.Count > 0)
                if (!await SaveKeysAsync(action, only).ConfigureAwait(false)) return;
            if (reviewBackend == null) return;
            var opened = false;
            Update(action, () =>
            {
                if (_form == null || _state.Review != null || _state.Loading || _state.Saving || _reconcilePending ||
                    !_state.StatusKnown || !_state.ManagedTransitionsAvailable ||
                    !AdminStorageRules.LocationChangePending(new FormDraft(_form), _state.ArtworkStorage!, _state.LatestTransition)) return;
                // The backend may have changed while unrelated keys were being saved.
                OpenReviewLocked(AdminStorageRules.DraftEffectiveBackend(new FormDraft(_form)));
                opened = true;
            });
            if (opened) StartProbeIfNeeded();
        }
    }

    /// <summary>Writes the dirty draft (or only <paramref name="only"/>) once. Returns whether the write was acknowledged.</summary>
    private async Task<bool> SaveKeysAsync(MonitorAuthorityLease action, IReadOnlyCollection<string>? only)
    {
        AdminSettingsSaveIntent intent;
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(action) || _form == null || _state.Saving || _state.Loading || _state.Queuing || _reconcilePending || _state.Review != null) return false;
            var keep = only == null ? null : new HashSet<string>(only, StringComparer.Ordinal);
            var excluded = _form.DirtyKeys.Where(key => _state.ManagedByEnv.Contains(key) || (keep != null && !keep.Contains(key))).ToArray();
            intent = _form.CaptureSave(excluded);
            if (intent.Values.Count == 0) { _form.EndSave(intent); return true; }
            _revision++;
            _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null, PublicCheck = null, PrivateCheck = null };
        }
        Changed?.Invoke();
        string[]? savedKeys = null;
        try
        {
            var result = await _settings.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
            var keys = intent.Values.Keys.ToArray();
            var presence = intent.Values.Where(entry => Secrets.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value.Length > 0, StringComparer.Ordinal);
            if (_lifetime.IsCurrent(action)) _store.Invalidate();
            Update(action, () =>
            {
                _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                var configured = new HashSet<string>(_state.Configured, StringComparer.Ordinal);
                foreach (var (key, present) in presence) { if (present) configured.Add(key); else configured.Remove(key); }
                _state = _state with { Saving = false, Configured = Array.AsReadOnly(configured.ToArray()) };
            });
            if (_lifetime.IsCurrent(action)) savedKeys = keys;
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { return false; }
        catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); return false; }
        catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); return false; }
        catch (Exception error) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); return false; }
        finally { TryStartReconciliation(); }
        if (savedKeys == null) return false;
        // The saved settings can change the storage lock and restart state.
        Start(ReadKind.Status, false);
        RaiseSaved(savedKeys);
        return true;
    }

    // ── Connection checks ────────────────────────────────────

    /// <param name="scope">"public" (kind s3_public) or "private" (kind s3_private).</param>
    public async Task CheckConnectionAsync(string scope)
    {
        var isPublic = scope switch { "public" => true, "private" => false, _ => throw new ArgumentException("Unknown storage scope.", nameof(scope)) };
        var keys = isPublic ? AdminStorageRules.PublicS3Keys : AdminStorageRules.PrivateS3Keys;
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            Dictionary<string, string> values; string[] dirty; long revision;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.Loaded || _state.Loading || _state.Saving || _state.SensitiveStatusError ||
                    (isPublic ? _state.CheckingPublic : _state.CheckingPrivate)) return;
                dirty = _form.DirtyKeys.Where(keys.Contains).Where(key => !_state.ManagedByEnv.Contains(key)).ToArray();
                // The server merges only dirty keys over its saved values; a saved credential is never sent back.
                values = keys.Where(key => !Secrets.Contains(key) || dirty.Contains(key)).ToDictionary(key => key, _form.Get, StringComparer.Ordinal);
                revision = _revision;
                _state = isPublic ? _state with { CheckingPublic = true, PublicCheck = null } : _state with { CheckingPrivate = true, PrivateCheck = null };
            }
            Changed?.Invoke();
            AdminSettingsConnectionResult result;
            try { result = await _settings.CheckConnectionAsync(action, isPublic ? "s3_public" : "s3_private", values, dirty).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch { result = new(false, AdminText.T("web.admin.settings.storage.s3.checkFailed")); }
            finally { values.Clear(); }
            Update(action, () =>
            {
                var shown = revision == _revision ? result : null;
                _state = isPublic ? _state with { CheckingPublic = false, PublicCheck = shown } : _state with { CheckingPrivate = false, PrivateCheck = shown };
            });
        }
    }

    // ── Transition review ────────────────────────────────────

    private void OpenReviewLocked(string backend)
    {
        _state = _state with
        {
            Review = new(backend, _form!.Get(AdminStorageRules.LocalPath), _policy),
            SourceHealth = null, SourceHealthFailed = false, CommandUnknownKey = null,
        };
    }

    private void StartProbeIfNeeded()
    {
        bool probe;
        lock (_gate)
            probe = _state.Review != null && _state.ManagedTransitionsAvailable && _state.ArtworkStorage != null &&
                AdminStorageRules.CurrentSourceUsesS3(_state, _state.ArtworkStorage);
        if (probe) Start(ReadKind.Probe, false);
    }

    public void SetReviewPolicy(string policy)
    {
        if (!AdminStorageApi.Policies.Contains(policy)) return;
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Review is not { } review || _state.Queuing) return;
            _policy = policy;
            _state = _state with { Review = review with { Policy = policy } };
        });
    }

    public void SetReviewLocalPath(string path)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_state.Review is { } review && !_state.Queuing && !_state.ManagedByEnv.Contains(AdminStorageRules.LocalPath)) _state = _state with { Review = review with { LocalPath = path } }; });
    }

    public void CloseReview()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Queuing) _state = _state with { Review = null }; });
        TryStartReconciliation();
    }

    public void RetrySourceHealth() { if (!State.SourceHealthFetching) StartProbeIfNeeded(); }

    /// <summary>Whether Queue is enabled for the current review (official DialogFooter disabled condition).</summary>
    public static bool CanQueue(AdminStorageSettingsState state)
    {
        // The transition API requires an explicit backend value; never submit an environment-owned key.
        if (state.Review is not { } review || state.ArtworkStorage is not { } status || !state.StatusKnown ||
            state.Queuing || state.Loading || state.Saving || state.ManagedByEnv.Contains(AdminStorageRules.Backend)) return false;
        var plan = AdminStorageRules.Plan(state, status, review.Backend, state.SourceHealth, state.SourceHealthFailed);
        return !(AdminStorageRules.CurrentSourceUsesS3(state, status) && state.SourceHealthPending) &&
               !(plan.CopyPolicyUnavailable && review.Policy != "start_fresh") &&
               !(review.Backend == "local" && AdminStorageRules.JsTrim(review.LocalPath) == "") &&
               !plan.PrivateEndpointMissing;
    }

    /// <summary>
    /// Queues the reviewed transition once. The command (policy, target and values) is captured immutably under the
    /// current owner; a duplicate activation while queuing is ignored and an ambiguous result is re-read, never resent.
    /// </summary>
    public async Task QueueTransitionAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            string policy, backend; IReadOnlyDictionary<string, string> values; AdminArtworkStorageStatus status;
            AdminStorageNotice? notice = null;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Review is not { } review || _state.Queuing || _state.Saving) return;
                if (!_state.StatusKnown) notice = new(false, "web.admin.settings.storage.lockUnknownMove");
                else if (!_state.ManagedTransitionsAvailable) notice = new(false, "web.admin.settings.storage.transitionsUnavailable");
                if (notice == null && !CanQueue(_state)) return;
                policy = review.Policy; backend = review.Backend; status = _state.ArtworkStorage!;
                values = notice == null ? AdminStorageRules.TransitionValues(new FormDraft(_form), status, backend, review.LocalPath, _state.ManagedByEnv) : new Dictionary<string, string>();
                if (notice == null) _state = _state with { Queuing = true, CommandUnknownKey = null };
            }
            if (notice != null) { Raise(notice); return; }
            Changed?.Invoke();
            string jobId;
            try { jobId = await _storage.CreateTransitionAsync(action, policy, values).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminStorageCommandOutcomeUnknownException)
            {
                // Keep the draft, close the review and re-read: the server rejects a second active transition anyway.
                Update(action, () => _state = _state with { Queuing = false, Review = null, CommandUnknownKey = "configure.native.storage.transitionUnknown" });
                Start(ReadKind.Jobs, false); Start(ReadKind.Status, false);
                TryStartReconciliation();
                return;
            }
            catch (Exception)
            {
                Update(action, () => _state = _state with { Queuing = false });
                if (_lifetime.IsCurrent(action)) Raise(new(false, "web.admin.settings.storage.transitionQueueFailed"));
                return;
            }
            var sent = values.Keys.ToArray();
            Update(action, () =>
            {
                if (_form != null)
                    foreach (var key in AdminStorageRules.KeysResetAfterTransition(new FormDraft(_form), status, backend, sent)) _form.Reset(key);
                _revision++;
                // Publish the acknowledgement and settings-read barrier atomically.
                _state = _state with { Queuing = false, Review = null, Loading = true, PublicCheck = null, PrivateCheck = null };
            });
            if (!_lifetime.IsCurrent(action)) return;
            Raise(new(true, "web.admin.settings.storage.transitionQueued", jobId));
            // Official onSuccess invalidates settings, status and jobs.
            _store.Invalidate();
            Start(ReadKind.Jobs, false); Start(ReadKind.Status, false);
            RaiseSaved(sent);
            await LoadSettingsAsync(action, true, preserveDraft: true).ConfigureAwait(false);
        }
    }

    // ── Active transition ────────────────────────────────────

    public void RefreshTransitions() => Start(ReadKind.Jobs, false);
    public void RetryStatus() => Start(ReadKind.Status, false);

    /// <summary>Cancels exactly the captured active job, once. Duplicate activation while pending is ignored.</summary>
    public async Task CancelTransitionAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            string id;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Cancelling || _state.LatestTransition is not { IsActive: true } job) return;
                id = job.Id;
                _state = _state with { Cancelling = true, CommandUnknownKey = null };
            }
            Changed?.Invoke();
            AdminStorageNotice? notice;
            try
            {
                await _storage.CancelJobAsync(action, id).ConfigureAwait(false);
                notice = new(true, "web.admin.settings.storage.active.cancelRequested");
                Update(action, () => _state = _state with { Cancelling = false });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return; }
            catch (AdminStorageCommandOutcomeUnknownException)
            {
                notice = null;
                Update(action, () => _state = _state with { Cancelling = false, CommandUnknownKey = "configure.native.storage.cancelUnknown" });
            }
            catch (Exception)
            {
                notice = new(false, "web.admin.settings.storage.active.cancelFailed");
                Update(action, () => _state = _state with { Cancelling = false });
            }
            if (!_lifetime.IsCurrent(action)) return;
            Raise(notice);
            Start(ReadKind.Jobs, false);
        }
    }

    public void DismissTransition(string id)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_state.LatestTransition?.Id == id) _state = _state with { DismissedTransitionId = id }; });
    }

    // ── Reads and polling ────────────────────────────────────

    private bool IsStale(ReadKind kind, TimeSpan age)
    {
        lock (_gate) return _slots[kind].Last is not { } last || _time.GetUtcNow() - last >= age;
    }

    private void Start(ReadKind kind, bool background, long? expectedEpoch = null)
    {
        long epoch;
        var visible = kind is ReadKind.Status or ReadKind.Probe;
        lock (_gate)
        {
            var slot = _slots[kind];
            if (_disposed || (expectedEpoch.HasValue && expectedEpoch.Value != _epoch) || _state.Owner is not { IsCurrent: true }) return;
            if (background && (!_active || _deferBackground())) return;
            if (slot.Running)
            {
                if (!background) slot.Pending = true;
                return;
            }
            slot.Running = true; epoch = _epoch;
            if (kind == ReadKind.Status) _state = _state with { StatusLoading = true };
            if (kind == ReadKind.Probe) _state = _state with { SourceHealthFetching = true };
        }
        if (visible) Changed?.Invoke();
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { lock (_gate) if (epoch == _epoch) _slots[kind].Running = false; return; }
        _ = RunAsync(kind, action, epoch);
    }

    private async Task RunAsync(ReadKind kind, MonitorAuthorityLease action, long epoch)
    {
        await Task.Yield();
        var startRecovery = false;
        var followUp = false;
        using (action)
        {
            try
            {
                switch (kind)
                {
                    case ReadKind.Status:
                        var status = await _storage.GetArtworkStorageStatusAsync(action).ConfigureAwait(false);
                        Update(action, () => _state = _state with { ArtworkStorage = status, StatusRead = true, StatusFailed = false, StatusLoading = false });
                        break;
                    case ReadKind.Capabilities:
                        var capabilities = await _storage.GetTransitionCapabilitiesAsync(action).ConfigureAwait(false);
                        Update(action, () => _state = _state with { ManagedTransitionsAvailable = capabilities.ManagedTransitionsAvailable });
                        startRecovery = capabilities.ManagedTransitionsAvailable;
                        break;
                    case ReadKind.Recovery:
                        var recovery = await _storage.GetSourceHealthAsync(action, probe: false).ConfigureAwait(false);
                        Update(action, () => _state = _state with { Recovery = recovery });
                        break;
                    case ReadKind.Jobs:
                        var job = await _storage.GetLatestTransitionAsync(action).ConfigureAwait(false);
                        Update(action, () =>
                        {
                            var previous = _state.LatestTransition;
                            if (job != null && (job.Phase is "restart_pending" or "completed" || !job.IsActive || job.ManualRestartRequired) &&
                                (previous == null || previous.Id != job.Id || previous.Status != job.Status || previous.Phase != job.Phase || previous.ManualRestartRequired != job.ManualRestartRequired))
                                _reconcilePending = true;
                            _state = _state with { LatestTransition = job };
                        });
                        TryStartReconciliation();
                        break;
                    case ReadKind.Probe:
                        var health = await _storage.GetSourceHealthAsync(action, probe: true).ConfigureAwait(false);
                        Update(action, () => _state = _state with { SourceHealth = _state.Review != null ? health : null, SourceHealthFailed = false, SourceHealthFetching = false });
                        break;
                }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception)
            {
                // Failed polls keep the last good data (official query data survives an error); only status and probe report it.
                Update(action, () => _state = kind switch
                {
                    ReadKind.Status => _state with { StatusRead = true, StatusFailed = true, StatusLoading = false },
                    ReadKind.Probe => _state with { SourceHealthFailed = true, SourceHealthFetching = false },
                    _ => _state,
                });
            }
            finally
            {
                lock (_gate)
                {
                    if (epoch == _epoch)
                    {
                        var slot = _slots[kind];
                        slot.Running = false; slot.Last = _time.GetUtcNow();
                        followUp = slot.Pending; slot.Pending = false;
                        if (kind == ReadKind.Status && _state.StatusLoading) _state = _state with { StatusLoading = false };
                        if (kind == ReadKind.Probe && _state.SourceHealthFetching) _state = _state with { SourceHealthFetching = false };
                    }
                }
            }
        }
        if (followUp) Start(kind, false, epoch);
        if (startRecovery) Start(ReadKind.Recovery, false, epoch);
        EvaluatePolls();
    }

    private void EvaluatePolls()
    {
        lock (_gate)
        {
            var current = !_disposed && _active && _state.Owner is { IsCurrent: true };
            var jobs = current && _state.LatestTransition?.IsActive == true;
            var recovery = current && _state.ManagedTransitionsAvailable && _state.Recovery?.RecoveryPending == true;
            if (jobs && _jobsPoll == null) _jobsPoll = _time.CreateTimer(_ => Start(ReadKind.Jobs, true), null, JobsPollInterval, JobsPollInterval);
            else if (!jobs && _jobsPoll != null) { _jobsPoll.Dispose(); _jobsPoll = null; }
            if (recovery && _recoveryPoll == null) _recoveryPoll = _time.CreateTimer(_ => Start(ReadKind.Recovery, true), null, RecoveryPollInterval, RecoveryPollInterval);
            else if (!recovery && _recoveryPoll != null) { _recoveryPoll.Dispose(); _recoveryPoll = null; }
        }
    }

    private void StopPollsLocked()
    {
        _jobsPoll?.Dispose(); _jobsPoll = null;
        _recoveryPoll?.Dispose(); _recoveryPoll = null;
    }

    private void ResetSlotsLocked() { _reconcilePending = false; foreach (var slot in _slots.Values) { slot.Running = false; slot.Pending = false; slot.Last = null; } }

    // ── State publication ────────────────────────────────────

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { _form?.Discard(); _form = null; _state = new(); }
            else if (_form != null) _state = _state with
            {
                Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.Except(Secrets, StringComparer.Ordinal).ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()), RestartRequired = _form.RestartRequired,
            };
        }
        Changed?.Invoke();
    }

    private void Raise(AdminStorageNotice? notice)
    {
        if (notice == null || Notice is not { } handlers) return;
        foreach (Action<AdminStorageNotice> handler in handlers.GetInvocationList())
            try { handler(notice); } catch { /* A toast observer cannot change command state. */ }
    }

    private void RaiseSaved(IReadOnlyCollection<string> keys)
    {
        if (Saved is not { } handlers) return;
        foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
            try { handler(keys); } catch { /* An observer cannot change an acknowledged write. */ }
    }

    private sealed class FormDraft(AdminSettingsForm form) : IAdminStorageDraft
    {
        private readonly HashSet<string> _dirty = new(form.DirtyKeys, StringComparer.Ordinal);
        public string Get(string key) => form.Get(key);
        public string GetPersisted(string key) => form.GetPersisted(key);
        public bool IsDirty(string key) => _dirty.Contains(key);
    }
}
