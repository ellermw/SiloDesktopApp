using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminIntegrationSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnvironment { get; init; } = [];
    /// <summary>The keys the server's environment sets, with the variable that sets each; drives the field notes. Empty when the status could not be read (the fields then stay locked through <see cref="ManagedByEnvironment"/> without a note).</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
}

/// <summary>Small, authority-scoped controller shared by the three integration leaves.</summary>
public sealed class AdminIntegrationSettingsController : IDisposable
{
    private readonly IReadOnlyList<string> _keys;
    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminIntegrationsApi _integrations;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminIntegrationSettingsState _state = new();
    private bool _disposed;

    public AdminIntegrationSettingsController(IEnumerable<string> keys, AdminServerSettingsApi api, AdminServerSettingsStore store, AdminMonitorAuthority authority, AdminIntegrationsApi integrations)
    {
        _keys = keys.Distinct(StringComparer.Ordinal).ToArray();
        if (_keys.Count == 0 || _keys.Any(key => !AdminIntegrationsSettings.AllKeys.Contains(key))) throw new ArgumentException("Invalid integration setting keys.", nameof(keys));
        _api = api; _store = store; _integrations = integrations; _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminIntegrationSettingsState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); } catch (OperationCanceledException) { Clear(); return; }
        _store.Invalidate();
        lock (_gate) { _form?.Discard(); _form = null; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke(); await LoadAsync(owner, false, false).ConfigureAwait(false);
    }

    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Loading || _state.Saving) return; }
            _store.Invalidate(); Update(action, () => _state = _state with { Loading = true, Error = null });
            await LoadAsync(action, true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner); var sensitive = _store.GetSensitiveStatusAsync(owner); var restart = _store.GetRestartKeysAsync(owner);
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            AdminSettingsSensitiveStatus status; var statusKnown = true;
            try { status = await sensitive.ConfigureAwait(false); } catch { status = new([], _keys.ToArray()); statusKnown = false; }
            LoggingRestartKeys restartKeys;
            try { restartKeys = await restart.ConfigureAwait(false); } catch { restartKeys = new(); }
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(_keys, [], snapshot.Values, snapshot.ETag);
                // A key the environment sets now loses its draft, and is never sent.
                var environment = statusKnown ? status.Environment : AdminEnvManagedSettings.None;
                _form.ApplyEnvironment(environment);
                _state = _state with { Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, Failure = null,
                    ManagedByEnvironment = status.ManagedByEnv.Where(_keys.Contains).ToArray(), EnvManaged = environment, RestartKeys = restartKeys };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", Failure = AdminFailures.Classify(error, owner) }); }
    }

    public void Set(string key, string value)
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () => { if (_form == null || _state.Loading || AdminIntegrationsSettings.ReadOnlyKeys.Contains(key) || _state.ManagedByEnvironment.Contains(key)) return; _form.Set(key, value); });
    }
    public void Discard()
    {
        var owner = _lifetime.Owner; if (owner != null) Update(owner, () => { if (!_state.Saving) _form?.Discard(); });
    }

    public Task SaveAsync() => SaveFieldsAsync(_keys);

    public async Task SaveFieldsAsync(IReadOnlyCollection<string> keys)
    {
        if (keys.Any(key => !_keys.Contains(key))) throw new ArgumentException("Invalid integration field.", nameof(keys));
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.Saving || _state.Conflict || _state.OutcomeUnknown) return;
                if (!_state.DirtyKeys.Intersect(keys).Except(_state.ManagedByEnvironment).Any()) return;
                _state = _state with { Saving = true, Error = null, Failure = null };
            }
            Changed?.Invoke();
            var saved = new List<string>();
            var groups = new[] { keys.Where(AdminIntegrationsSettings.CompatibilityPatchKeys.Contains).ToArray(), keys.Where(key => !AdminIntegrationsSettings.CompatibilityPatchKeys.Contains(key)).ToArray() };
            foreach (var group in groups.Where(group => group.Length > 0))
            {
                AdminSettingsSaveIntent intent;
                lock (_gate)
                {
                    if (!_lifetime.IsCurrent(action) || _form == null) return;
                    intent = _form.CaptureSave(_state.ManagedByEnvironment.Concat(_keys.Except(group)));
                    if (intent.Values.Count == 0) { _form.EndSave(intent); continue; }
                }
                try
                {
                    AdminSettingsSaveResult result;
                    if (AdminIntegrationsSettings.CompatibilityPatchKeys.Contains(group[0]))
                    {
                        var baseline = State.PersistedValues;
                        await _integrations.PatchCompatibilityAsync(action, intent.Values, intent.ETag).ConfigureAwait(false);
                        try
                        {
                            var snapshot = await _api.GetEffectiveAsync(action, _keys, []).ConfigureAwait(false);
                            if (_keys.Except(intent.Values.Keys).Except(AdminIntegrationsSettings.ReadOnlyKeys).Any(key => snapshot.Values.GetValueOrDefault(key, "") != baseline.GetValueOrDefault(key, ""))) throw new AdminSettingsConflictException();
                            result = new(snapshot.Values, snapshot.ETag, false);
                        }
                        catch (OperationCanceledException) when (!action.IsCurrent) { throw; }
                        catch (AdminSettingsConflictException) { throw; }
                        catch { throw new AdminIntegrationsOutcomeUnknownException(); }
                    }
                    else result = await _api.PutAsync(action, intent.Values, intent.ETag, _keys, []).ConfigureAwait(false);
                    var completed = intent.Values.Keys.ToArray();
                    Update(action, () => { _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired); _state = _state with { RestartRequired = _state.RestartRequired || result.RestartRequired }; });
                    saved.AddRange(completed);
                    if (_lifetime.IsCurrent(action)) _store.Invalidate();
                }
                catch (OperationCanceledException) when (!action.IsCurrent) { return; }
                catch (AdminSettingsConflictException) { FailSave(action, intent, conflict: true); break; }
                catch (Exception error) when (error is AdminSettingsOutcomeUnknownException or AdminIntegrationsOutcomeUnknownException) { FailSave(action, intent, unknown: true); break; }
                catch (Exception error) { FailSave(action, intent, error: error); break; }
            }
            Update(action, () => _state = _state with { Saving = false });
            if (saved.Count > 0 && _lifetime.IsCurrent(action) && Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList()) try { handler(saved); } catch { }
        }
    }

    private void FailSave(MonitorAuthorityLease action, AdminSettingsSaveIntent intent, bool conflict = false, bool unknown = false, Exception? error = null) => Update(action, () =>
    {
        _form?.EndSave(intent);
        _state = _state with { Saving = false, Conflict = conflict, OutcomeUnknown = unknown,
            Error = conflict ? "Settings changed on the server. Reload and review your changes." : unknown ? "The save outcome is unknown. Reload and review before saving again." : "Failed to update settings.",
            Failure = error == null ? null : AdminFailures.Classify(error, action) };
    });

    private void Update(MonitorAuthorityLease owner, Action mutation)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            mutation();
            if (!owner.IsCurrent) { _form?.Discard(); _form = null; _state = new(); }
            else if (_form != null) _state = _state with
            {
                Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(_keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(_keys.ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                DirtyKeys = _form.DirtyKeys.ToArray()
            };
        }
        Changed?.Invoke();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
