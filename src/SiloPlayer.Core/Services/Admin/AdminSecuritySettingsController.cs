using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminSecuritySavePhase { None, Settings, RateLimits, Reconciling }

/// <summary>Why rate saves are blocked until an explicit reload.</summary>
public enum AdminRateLimitBlock { None, Conflict, OutcomeUnknown, ReloadRequired }

/// <summary>The one message the Rate limiting group shows.</summary>
public enum AdminRateLimitMessage { None, LoadFailed, ReloadBeforeEditing, ChangedElsewhere, OutcomeUnknown, SaveFailed, Merged }

public sealed record AdminSecuritySettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    // Local network: the shared batched settings form.
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The same keys with the variable that sets each; drives the row note.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    // One Save across both writers.
    public bool Saving { get; init; }
    public AdminSecuritySavePhase SavePhase { get; init; }
    // Rate limiting: its own resource and validator.
    public bool RateLoading { get; init; }
    /// <summary>Last server read, including runtime hints. Hints come from here, never from the draft.</summary>
    public AdminRateLimitSnapshot? RateSnapshot { get; init; }
    public AdminRateLimitValues? RateValues { get; init; }
    public AdminRateLimitValues? RateBaseline { get; init; }
    public IReadOnlyList<string> RateDirtyPaths { get; init; } = [];
    public AdminRateLimitBlock RateBlock { get; init; }
    public AdminRateLimitMessage RateMessage { get; init; }
    public AdminFailureKind? RateFailure { get; init; }
    public string? RateErrorCode { get; init; }
    /// <summary>The server's validation sentence, bounded and redacted.</summary>
    public string? RateDetail { get; init; }
    public bool RateEditable { get; init; }
    public bool CanSave { get; init; }

    public bool RateLoaded => RateValues != null;
    public bool RateDirty => RateDirtyPaths.Count > 0;
    /// <summary>The batched keys plus one for the whole rate-limit draft (official SaveBar count).</summary>
    public int DirtyCount => DirtyKeys.Count + (RateDirty ? 1 : 0);
    public override string ToString() => nameof(AdminSecuritySettingsState);
}

/// <summary>
/// Configure > Sign-in &amp; Security > Security (official SecurityAccessSettings.tsx). The local-network
/// key uses the shared settings form/store/API; rate limits use their own draft and
/// <see cref="AdminRateLimitApi"/> and never pass through the batched PUT. One Save runs the PUT, then the
/// PATCH captured before anything was awaited; a failed PUT skips the PATCH, and no write is replayed.
/// Events may arrive on any thread.
/// </summary>
public sealed class AdminSecuritySettingsController : IDisposable
{
    public const string LocalNetwork = "media_servers.allow_private_destinations";
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { LocalNetwork });
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(Array.Empty<string>());
    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminRateLimitApi _rateApi;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminRateLimitDraft? _rate;
    private AdminRateLimitIntent? _unknownIntent;
    private AdminSecuritySettingsState _state = new();
    private bool _disposed;

    public AdminSecuritySettingsController(AdminServerSettingsApi api, AdminServerSettingsStore store, AdminRateLimitApi rateApi, AdminMonitorAuthority authority)
    {
        _api = api; _store = store; _rateApi = rateApi; _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    /// <summary>The batched PUT was acknowledged; refresh the shell restart status.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    /// <summary>The PATCH was acknowledged (or an unknown outcome reconciled as applied, with null restart).</summary>
    public event Action<bool?>? RateLimitsSaved;
    public AdminSecuritySettingsState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            _form?.Discard(); _form = null; _rate = null; _unknownIntent = null;
            _state = new() { Owner = owner, Loading = true, RateLoading = true };
        }
        Changed?.Invoke();
        _store.Invalidate();
        await Task.WhenAll(LoadSettingsAsync(owner, false, false), LoadRateAsync(owner)).ConfigureAwait(false);
    }

    /// <summary>Explicit reconciliation of the batched settings only.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with { Loading = true, Error = null });
            await LoadSettingsAsync(action, true, preserveDraft).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Explicit Retry/Reload for the rate group. After a 412 or a failed save only the changed visible fields
    /// are rebased; after an unknown outcome the fresh read decides whether the intent was applied.
    /// </summary>
    public async Task ReloadRateAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Saving || _state.RateLoading) return;
                _state = _state with { RateLoading = true };
            }
            Update(action, () => { });
            await LoadRateAsync(action).ConfigureAwait(false);
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
        catch { /* Managed state is optional for a non-secret key; the server still rejects managed writes. */ }
        try { restartKeys = await restart.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(Keys, Secrets, snapshot.Values, snapshot.ETag);
                // A key the environment sets now loses its draft, and is never sent.
                var environment = AdminEnvManagedSettings.Resolve(status, _state.EnvManaged);
                _form.ApplyEnvironment(environment);
                _state = _state with
                {
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    ManagedByEnv = environment.Keys, EnvManaged = environment, RestartKeys = restartKeys,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () => _state = _state with
            {
                Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner),
            });
        }
    }

    private async Task LoadRateAsync(MonitorAuthorityLease owner)
    {
        AdminRateLimitSnapshot snapshot;
        try { snapshot = await _rateApi.GetAsync(owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch (Exception error)
        {
            // Never invent an editable baseline: without a validated read the group shows only the failure and Retry.
            Update(owner, () => _state = _state with
            {
                RateLoading = false, RateMessage = error is AdminRateLimitValidatorException ? AdminRateLimitMessage.ReloadBeforeEditing : AdminRateLimitMessage.LoadFailed,
                RateFailure = AdminFailures.Classify(error, owner), RateErrorCode = (error as ApiException)?.ProblemType ?? (error as ApiException)?.ErrorCode, RateDetail = null,
            });
            return;
        }
        var applied = false;
        Update(owner, () =>
        {
            var message = AdminRateLimitMessage.None;
            if (_rate == null) _rate = new(snapshot.Config, snapshot.ETag);
            else if (_state.RateBlock == AdminRateLimitBlock.ReloadRequired) _rate.Replace(snapshot);
            else if (_state.RateBlock == AdminRateLimitBlock.OutcomeUnknown && _unknownIntent is { } intent)
            {
                if (snapshot.Config.Equals(intent.Values)) { _rate.Replace(snapshot); applied = true; }
                else if (snapshot.Config.Equals(intent.Baseline)) _rate.Rebase(snapshot, intent.Baseline);
                else { _rate.Rebase(snapshot, intent.Baseline); message = AdminRateLimitMessage.Merged; }
            }
            else
            {
                var concurrent = _rate.Baseline.Differences(snapshot.Config).Count > 0;
                _rate.Rebase(snapshot);
                if (concurrent && _rate.IsDirty) message = AdminRateLimitMessage.Merged;
            }
            _unknownIntent = null;
            _state = _state with
            {
                RateLoading = false, RateSnapshot = snapshot, RateBlock = AdminRateLimitBlock.None, RateMessage = message,
                RateFailure = null, RateErrorCode = null, RateDetail = null,
            };
        });
        if (applied && _lifetime.IsCurrent(owner)) RaiseRateSaved(null);
    }

    private bool CanEditLocal(string key) => _form != null && _state.Loaded && !_state.Loading && !_state.ManagedByEnv.Contains(key) && _state.Failure is not
        (AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable);

    /// <summary>Stages the local-network switch. It stays editable during a save; later edits remain dirty.</summary>
    public void SetLocalNetwork(bool allowed)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (CanEditLocal(LocalNetwork)) _form!.Set(LocalNetwork, allowed ? "true" : "false"); });
    }

    /// <summary>Stages a toggle or select value. Returns false when the group is frozen or the value is not allowed.</summary>
    public bool SetRate(string path, object value)
    {
        var owner = _lifetime.Owner;
        var field = AdminRateLimitFields.Get(path);
        if (owner == null) return false;
        var accepted = false;
        Update(owner, () =>
        {
            if (!_state.RateEditable || _rate == null) return;
            var allowed = field.Kind switch
            {
                AdminRateLimitFieldKind.Toggle => value is bool,
                AdminRateLimitFieldKind.Choice when path == AdminRateLimitFields.Backend =>
                    value is string backend && AdminRateLimitFields.Backends.Contains(backend) && (backend != "redis" || _state.RateSnapshot?.RedisSelectable != false),
                AdminRateLimitFieldKind.Choice => value is string mode && AdminRateLimitFields.QuotaModes.Contains(mode),
                _ => value is double number && number >= 1 && number <= field.Max && Math.Floor(number) == number && !QuotaSizeLocked(path),
            };
            if (!allowed) return;
            _rate.Set(path, value);
            accepted = true;
        });
        return accepted;
    }

    /// <summary>Applies typed number text when it is a valid value for the field; otherwise the draft is unchanged.</summary>
    public bool TrySetRateNumber(string path, string raw)
    {
        var field = AdminRateLimitFields.Get(path);
        return field.IsNumber && AdminRateLimitNumber.TryParse(raw, field.Max, out var value) && SetRate(path, value);
    }

    /// <summary>The quota size boxes are disabled while the draft mode is off; their values are kept.</summary>
    public bool QuotaSizeLocked(string path)
    {
        lock (_gate) return path is AdminRateLimitFields.QuotaPerHour or AdminRateLimitFields.QuotaBurst && _rate?.Values.Text(AdminRateLimitFields.QuotaMode) == "off";
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_state.Saving) return;
            _form?.Discard(); _rate?.Discard();
            if (_state.RateMessage == AdminRateLimitMessage.Merged) _state = _state with { RateMessage = AdminRateLimitMessage.None };
        });
    }

    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent? settings = null;
            AdminRateLimitIntent? rate;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.CanSave) return;
                // Both intents are captured before anything is awaited: the PATCH carries this draft and validator.
                rate = _rate is { IsDirty: true } draft ? draft.Capture() : null;
                if (_form.DirtyKeys.Any(key => !_state.ManagedByEnv.Contains(key)))
                {
                    settings = _form.CaptureSave(_state.ManagedByEnv);
                    if (settings.Values.Count == 0) { _form.EndSave(settings); settings = null; }
                }
                if (settings == null && rate == null) return;
                _state = _state with
                {
                    Saving = true, SavePhase = settings != null ? AdminSecuritySavePhase.Settings : AdminSecuritySavePhase.RateLimits,
                    Error = null, ErrorCode = null, Failure = null,
                };
                // A local-only save must keep the rate group's message and Reload affordance.
                if (rate != null) _state = _state with
                {
                    RateMessage = AdminRateLimitMessage.None, RateFailure = null, RateErrorCode = null, RateDetail = null,
                };
                Derive();
            }
            Changed?.Invoke();
            try
            {
                if (settings != null && !await SaveSettingsAsync(action, settings).ConfigureAwait(false)) return;
                if (rate != null) await SaveRateAsync(action, rate).ConfigureAwait(false);
            }
            finally { Update(action, () => _state = _state with { Saving = false, SavePhase = AdminSecuritySavePhase.None }); }
        }
    }

    /// <summary>The batched PUT. Any failure stops the sequence so the PATCH is never judged against unsaved settings.</summary>
    private async Task<bool> SaveSettingsAsync(MonitorAuthorityLease action, AdminSettingsSaveIntent intent)
    {
        try
        {
            var result = await _api.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
            var keys = intent.Values.Keys.ToArray();
            Update(action, () => _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired));
            if (!_lifetime.IsCurrent(action)) return false;
            _store.Invalidate();
            if (Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                    try { handler(keys); } catch { /* An observer cannot change an acknowledged write. */ }
            return true;
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { }
        catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); }
        catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); }
        catch (Exception error) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); }
        return false;
    }

    private async Task SaveRateAsync(MonitorAuthorityLease action, AdminRateLimitIntent intent)
    {
        Update(action, () => _state = _state with { SavePhase = AdminSecuritySavePhase.RateLimits });
        bool restart;
        try { restart = await _rateApi.PatchAsync(action, intent).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!action.IsCurrent) { return; }
        catch (AdminRateLimitConflictException)
        {
            Update(action, () => _state = _state with { RateBlock = AdminRateLimitBlock.Conflict, RateMessage = AdminRateLimitMessage.ChangedElsewhere });
            return;
        }
        catch (AdminRateLimitOutcomeUnknownException)
        {
            Update(action, () => { _unknownIntent = intent; _state = _state with { RateBlock = AdminRateLimitBlock.OutcomeUnknown, RateMessage = AdminRateLimitMessage.OutcomeUnknown }; });
            return;
        }
        catch (Exception error)
        {
            var api = error as ApiException;
            Update(action, () => _state = _state with
            {
                RateMessage = AdminRateLimitMessage.SaveFailed, RateFailure = AdminFailures.Classify(error, action),
                RateErrorCode = api?.ProblemType ?? api?.ErrorCode, RateDetail = api?.ProblemDetail,
            });
            return;
        }
        // Acknowledged: the receipt has no validator, so re-read before another rate write is allowed.
        Update(action, () => { _rate?.Acknowledge(intent); _state = _state with { SavePhase = AdminSecuritySavePhase.Reconciling }; });
        if (!_lifetime.IsCurrent(action)) return;
        RaiseRateSaved(restart);
        try
        {
            var snapshot = await _rateApi.GetAsync(action).ConfigureAwait(false);
            Update(action, () => { _rate?.Replace(snapshot); _state = _state with { RateSnapshot = snapshot }; });
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { }
        catch (Exception error)
        {
            Update(action, () => _state = _state with
            {
                RateBlock = AdminRateLimitBlock.ReloadRequired, RateMessage = AdminRateLimitMessage.ReloadBeforeEditing, RateFailure = AdminFailures.Classify(error, action),
            });
        }
    }

    private void RaiseRateSaved(bool? restart)
    {
        if (RateLimitsSaved is not { } handlers) return;
        foreach (Action<bool?> handler in handlers.GetInvocationList())
            try { handler(restart); } catch { /* An observer cannot change an acknowledged write. */ }
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { Reset(); }
            else Derive();
        }
        Changed?.Invoke();
    }

    /// <summary>Recomputes the projected values and the editability/save rules. Caller holds the gate.</summary>
    private void Derive()
    {
        var state = _state;
        if (_form != null) state = state with
        {
            Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
            PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
            DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()), RestartRequired = _form.RestartRequired,
        };
        state = state with
        {
            RateValues = _rate?.Values, RateBaseline = _rate?.Baseline, RateDirtyPaths = _rate?.DirtyPaths ?? [],
            RateEditable = _rate != null && state.Loaded && !state.Loading && !state.Saving && !state.RateLoading && state.RateBlock is not (AdminRateLimitBlock.OutcomeUnknown or AdminRateLimitBlock.ReloadRequired),
        };
        var settingsDirty = state.DirtyKeys.Any(key => !state.ManagedByEnv.Contains(key));
        var accessBlocked = state.Failure is AdminFailureKind.Unauthorized or AdminFailureKind.Forbidden or AdminFailureKind.ProfileVerificationRequired
            or AdminFailureKind.DemoRestricted or AdminFailureKind.CapabilityUnavailable;
        var settingsReady = !settingsDirty || (!state.Conflict && !state.OutcomeUnknown && !accessBlocked);
        var rateReady = !state.RateDirty || (state.RateBlock == AdminRateLimitBlock.None && _rate?.ETag != null);
        _state = state with
        {
            CanSave = (settingsDirty || state.RateDirty) && settingsReady && rateReady && _form != null && state.Loaded &&
                !state.Loading && !state.RateLoading && !state.Saving && state.Owner is { IsCurrent: true },
        };
    }

    private void Reset() { _form?.Discard(); _form = null; _rate = null; _unknownIntent = null; _state = new(); }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) Reset(); Changed?.Invoke(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); Changed = null; Saved = null; RateLimitsSaved = null;
    }
}
