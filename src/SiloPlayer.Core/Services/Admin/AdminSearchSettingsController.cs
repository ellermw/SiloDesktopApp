using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminSearchSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Checking { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    /// <summary>Which fields the environment sets could not be read; provider, URL and API key stay protected until a retry succeeds.</summary>
    public bool SensitiveStatusError { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Canonical acknowledged nonsecret values, separate from newer staged edits.</summary>
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public bool ApiKeyConfigured { get; init; }
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The same keys with the variable that sets each; drives the row notes.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public AdminSettingsConnectionResult? ConnectionResult { get; init; }
    public AdminSearchStatus? Status { get; init; }
    public bool StatusLoading { get; init; }
    /// <summary>The last status read failed; the previous <see cref="Status"/> (if any) is kept for the summary line.</summary>
    public AdminFailureKind? StatusFailure { get; init; }
    public bool MeilisearchSelected => AdminSearchSettingsRules.IsMeilisearch(Values);
    public override string ToString() => nameof(AdminSearchSettingsState);
}

/// <summary>
/// Page-owned Search settings state (official <c>SearchSettings.tsx</c>). Reuses the shared guarded settings API, snapshot
/// store and draft form, and adds the runtime search status (read only while the page is visible and its data is wanted,
/// polled every two seconds only while an index rebuild is required) and one Meilisearch connection check.
/// Events may arrive on any thread.
/// </summary>
public sealed class AdminSearchSettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys => AdminSearchSettingsRules.Keys;
    public static IReadOnlyList<string> Secrets => AdminSearchSettingsRules.Secrets;
    private const string ApiKey = AdminSearchSettingsRules.MeilisearchApiKey;

    private readonly AdminServerSettingsApi _api;
    private readonly AdminSearchApi _search;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly Func<bool> _deferBackground;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminSearchSettingsState _state = new();
    private ITimer? _poll;
    private object? _statusFlight;
    private bool _statusRefreshPending;
    private DateTimeOffset _statusAt = DateTimeOffset.MinValue;
    private long _revision;
    private bool _active, _advancedOpen, _disposed;

    /// <param name="deferBackground">True while foreground navigation or playback start should not share the network with a poll.</param>
    public AdminSearchSettingsController(AdminServerSettingsApi api, AdminSearchApi search, AdminServerSettingsStore store,
        AdminMonitorAuthority authority, TimeProvider? time = null, Func<bool>? deferBackground = null)
    {
        _api = api; _search = search; _store = store; _time = time ?? TimeProvider.System;
        _deferBackground = deferBackground ?? (() => false);
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    /// <summary>Raised after an acknowledged save so the shell can refresh restart status and affected caches.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminSearchSettingsState State { get { lock (_gate) return _state; } }
    public bool IsPolling { get { lock (_gate) return _poll != null; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        // Other Configure pages share this store; page entry must revalidate values and credential presence.
        _store.Invalidate();
        lock (_gate) { ResetLocked(); _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        await LoadAsync(owner, false, false).ConfigureAwait(false);
    }

    /// <summary>Explicit reconciliation; also the retry for a failed environment-status read.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () =>
            {
                _revision++;
                _state = _state with { Loading = true, Error = null, ConnectionResult = null };
            });
            await LoadAsync(action, true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner);
        var sensitive = _store.GetSensitiveStatusAsync(owner);
        var restart = _store.GetRestartKeysAsync(owner);
        AdminSettingsSensitiveStatus? status = null;
        LoggingRestartKeys restartKeys = new();
        var sensitiveError = false;
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch
        {
            sensitiveError = true;
            // Fail closed even if the effective settings read also fails.
            Update(owner, () =>
            {
                _form?.Reset(ApiKey);
                _revision++;
                _state = _state with { SensitiveStatusError = true, ConnectionResult = null };
            });
        }
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
                // The form needs the saved-key presence to tell an emptied key with nothing saved (nothing staged) from a clear.
                _form.ApplyConfigured(status?.Configured);
                // Without presence the page cannot tell keep from clear; never keep a credential draft across that gap.
                if (sensitiveError) _form.Reset(ApiKey);
                // A key the environment sets now loses its draft (a typed API key included), and is never sent.
                var environment = AdminEnvManagedSettings.Resolve(status, _state.EnvManaged);
                _form.ApplyEnvironment(environment);
                _revision++;
                _state = _state with
                {
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    SensitiveStatusError = sensitiveError, ApiKeyConfigured = status?.Configured.Contains(ApiKey) == true, ConnectionResult = null,
                    ManagedByEnv = environment.Keys, EnvManaged = environment, RestartKeys = restartKeys,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch (Exception error)
        {
            Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner) });
            return;
        }
        // A reload also refreshes the status the page shows; an open reads it for the first time.
        AfterChange(force: reload);
    }

    // ---------------------------------------------------------------- editing

    private bool CanEdit(string key) => _form != null && _state.Loaded && !_state.Loading && !_state.ManagedByEnv.Contains(key) &&
        (key is not (AdminSearchSettingsRules.Provider or AdminSearchSettingsRules.MeilisearchUrl or ApiKey) || !_state.SensitiveStatusError) &&
        (key != ApiKey || !_state.Saving);

    /// <summary>Stages a value; a value equal to the saved one withdraws the edit (official form behavior). The API key uses <see cref="SetApiKey"/>.</summary>
    public void Set(string key, string value)
    {
        if (!Keys.Contains(key)) throw new ArgumentException("This setting is not owned by the Search page.", nameof(key));
        if (key == ApiKey) { SetApiKey(value); return; }
        Edit(key, form => form.Set(key, value));
    }

    /// <summary>Typing stages a replacement. Emptying the input goes back to what is saved (official SecretField): it keeps a saved key, and with none saved leaves nothing staged.</summary>
    public void SetApiKey(string value) => Edit(ApiKey, form => form.SetSecret(ApiKey, value));
    /// <summary>Explicitly stages erasing the saved key (a Meilisearch instance without a master key needs it empty).</summary>
    public void ClearApiKey() => Edit(ApiKey, form => { if (_state.ApiKeyConfigured) form.Set(ApiKey, "", stage: true); });
    public void KeepApiKey() => Edit(ApiKey, form => form.Reset(ApiKey));

    private void Edit(string key, Action<AdminSettingsForm> edit)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (!CanEdit(key)) return;
            edit(_form!);
            // Any change to what a check would send hides its result (the result belongs to the tested values).
            if (key == AdminSearchSettingsRules.Provider || AdminSearchSettingsRules.MeilisearchKeys.Contains(key)) { _revision++; _state = _state with { ConnectionResult = null }; }
        });
        AfterChange(force: false);
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Saving) { _form?.Discard(); _revision++; _state = _state with { ConnectionResult = null }; } });
        AfterChange(force: false);
    }

    // ---------------------------------------------------------------- save

    public async Task SaveAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.Saving || _state.Conflict || _state.OutcomeUnknown) return;
                var excluded = _state.SensitiveStatusError ? _state.ManagedByEnv.Concat([ApiKey, AdminSearchSettingsRules.Provider, AdminSearchSettingsRules.MeilisearchUrl]) : _state.ManagedByEnv;
                intent = _form.CaptureSave(excluded);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _api.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                bool? keyPresent = intent.Values.TryGetValue(ApiKey, out var submitted) ? submitted.Length > 0 : null;
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false, ApiKeyConfigured = keyPresent ?? _state.ApiKeyConfigured };
                });
                if (_lifetime.IsCurrent(action)) { _store.Invalidate(); savedKeys = keys; }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); }
            catch (Exception error) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); }
            if (savedKeys != null && _lifetime.IsCurrent(action))
            {
                // Saved search settings change what the status describes (provider, URL, semantic mode): read it again.
                lock (_gate) { _statusAt = DateTimeOffset.MinValue; }
                AfterChange(force: true);
                if (Saved is { } handlers)
                    foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                        try { handler(savedKeys); } catch { /* An observer cannot change an acknowledged write. */ }
            }
        }
    }

    // ---------------------------------------------------------------- connection check

    /// <summary>One synchronous Meilisearch check of the staged values. Disabled for Postgres; the result is dropped when the draft changes.</summary>
    public async Task CheckConnectionAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            Dictionary<string, string> values; string[] dirty; long revision;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.Loaded || _state.Loading || _state.Saving || _state.Checking || !_state.MeilisearchSelected) return;
                values = AdminSearchSettingsRules.MeilisearchKeys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal);
                dirty = _form.DirtyKeys.Where(AdminSearchSettingsRules.MeilisearchKeys.Contains).Where(key => !_state.ManagedByEnv.Contains(key)).ToArray();
                revision = _revision;
                _state = _state with { Checking = true, ConnectionResult = null };
            }
            Changed?.Invoke();
            try
            {
                var result = await _search.CheckMeilisearchAsync(action, values, dirty).ConfigureAwait(false);
                Update(action, () => _state = _state with { Checking = false, ConnectionResult = revision == _revision ? result : null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch { Update(action, () => _state = _state with { Checking = false, ConnectionResult = revision == _revision ? new(false, "Connection check failed.") : null }); }
        }
    }

    // ---------------------------------------------------------------- status

    /// <summary>
    /// Whether the page and its window are in front. Inactive: no timer and no new reads. Activation reads again only
    /// stale status (official <c>staleTime</c>).
    /// </summary>
    public void SetActive(bool active)
    {
        lock (_gate) { if (_disposed || _active == active) return; _active = active; }
        AfterChange(force: false);
    }

    /// <summary>Explicit Retry for a failed status read; reads once now when the status is wanted.</summary>
    public void RefreshStatus() => BeginStatusRead(force: true);

    /// <summary>The Advanced disclosure shows the status panel, so an open disclosure wants status even for Postgres.</summary>
    public void SetAdvancedOpen(bool open)
    {
        lock (_gate) { if (_disposed || _advancedOpen == open) return; _advancedOpen = open; }
        AfterChange(force: false);
    }

    private bool DemandLocked => !_disposed && _active && _form != null && _state.Loaded && (_state.MeilisearchSelected || _advancedOpen);

    private void AfterChange(bool force)
    {
        BeginStatusRead(force);
        ReconcilePoll();
    }

    private void BeginStatusRead(bool force, bool queueIfBusy = true)
    {
        MonitorAuthorityLease action;
        object flight;
        lock (_gate)
        {
            if (force && queueIfBusy && !_disposed && _form != null && _state.Loaded) _statusRefreshPending = true;
            if (_statusFlight != null || !DemandLocked) return;
            // A recent attempt, successful or not, is reused: a failing endpoint is not hit again on every keystroke.
            if (!force && !_statusRefreshPending && _statusAt != DateTimeOffset.MinValue && _time.GetUtcNow() - _statusAt < AdminSearchSettingsRules.StatusStaleAfter) return;
            try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
            _statusFlight = flight = new object();
            _statusRefreshPending = false;
            _state = _state with { StatusLoading = _state.Status == null };
        }
        Changed?.Invoke();
        _ = ReadStatusAsync(action, flight);
    }

    private async Task ReadStatusAsync(MonitorAuthorityLease action, object flight)
    {
        var followUp = false;
        using (action)
        {
            try
            {
                var status = await _search.GetStatusAsync(action).ConfigureAwait(false);
                Update(action, () => _state = _state with { Status = status, StatusLoading = false, StatusFailure = null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error) { Update(action, () => _state = _state with { StatusLoading = false, StatusFailure = AdminFailures.Classify(error, action) }); }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_statusFlight, flight))
                    {
                        _statusFlight = null;
                        _statusAt = _time.GetUtcNow();
                        followUp = _statusRefreshPending;
                    }
                }
            }
        }
        if (followUp) BeginStatusRead(force: false);
        ReconcilePoll();
    }

    private void ReconcilePoll()
    {
        lock (_gate)
        {
            var wanted = DemandLocked && _state.Status?.Index.RebuildRequired == true;
            if (wanted && _poll == null) _poll = _time.CreateTimer(_ => OnPoll(), null, AdminSearchSettingsRules.PollInterval, AdminSearchSettingsRules.PollInterval);
            else if (!wanted && _poll != null) { _poll.Dispose(); _poll = null; }
        }
    }

    private void OnPoll()
    {
        lock (_gate)
        {
            if (_poll == null || !DemandLocked || _state.Status?.Index.RebuildRequired != true) return;
            // Foreground navigation or playback start keeps the network; the next tick tries again.
            if (_deferBackground()) return;
        }
        BeginStatusRead(force: true, queueIfBusy: false);
    }

    // ---------------------------------------------------------------- lifetime

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { ResetLocked(); }
            else if (_form != null) _state = _state with
            {
                Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.Except(Secrets, StringComparer.Ordinal).ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()), RestartRequired = _form.RestartRequired,
            };
        }
        Changed?.Invoke();
    }

    private void ResetLocked()
    {
        _form?.Discard(); _form = null; _state = new();
        _poll?.Dispose(); _poll = null; _statusFlight = null; _statusRefreshPending = false; _statusAt = DateTimeOffset.MinValue;
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) ResetLocked(); Changed?.Invoke(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); Changed = null; Saved = null;
    }
}
