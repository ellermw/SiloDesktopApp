using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminGeneralSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Checking { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool SensitiveStatusError { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Canonical acknowledged nonsecret values, separate from newer staged edits.</summary>
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> Configured { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The same keys with the variable that sets each; drives the row notes.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public AdminSettingsConnectionResult? ConnectionResult { get; init; }
    public override string ToString() => nameof(AdminGeneralSettingsState);
}

/// <summary>Page-owned General settings state. Events may arrive on any thread.</summary>
public sealed class AdminGeneralSettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { "branding.server_name", "branding.login_subtitle", "server.public_url", "server.lan_discovery", "server.local_url", "clientip.trusted_proxies", "database.max_connections", "redis.url", "redis.db" });
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(new[] { "redis.url" });
    private static readonly string[] Redis = ["redis.url", "redis.db"];
    // General keys whose credential or environment-managed state requires sensitive-status.
    public static IReadOnlyList<string> SensitiveStatusKeys { get; } = Array.AsReadOnly(new[] { "redis.url", "redis.db", "clientip.trusted_proxies" });
    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminGeneralSettingsState _state = new();
    private long _revision;
    private bool _disposed;

    public AdminGeneralSettingsController(AdminServerSettingsApi api, AdminServerSettingsStore store, AdminMonitorAuthority authority)
    {
        _api = api; _store = store; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    /// <summary>Raised after an acknowledged save so the shell can refresh status and affected caches.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminGeneralSettingsState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        // Other pages or admin sessions may have changed values and the shared ETag.
        _store.Invalidate();
        lock (_gate) { _form?.Discard(); _form = null; _revision++; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        await LoadAsync(owner, false, false).ConfigureAwait(false);
    }

    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with { Loading = true, Error = null, ConnectionResult = null });
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
        bool sensitiveError = false;
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { sensitiveError = true; }
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
                _revision++;
                _state = _state with { Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    SensitiveStatusError = sensitiveError, Configured = status?.Configured ?? [], ManagedByEnv = environment.Keys, EnvManaged = environment, RestartKeys = restartKeys };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner) }); }
    }

    public void Set(string key, string value, bool stage = false)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || !CanEdit(key)) return;
            _form.Set(key, value, stage); _revision++;
            _state = _state with { ConnectionResult = null };
        });
    }
    public void StageRedisUrl(string value)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_form == null || !CanEdit("redis.url")) return; _form.StageRedisUrl(value); _revision++; _state = _state with { ConnectionResult = null }; });
    }
    public void KeepSavedRedisUrl()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_form == null || !CanEdit("redis.url")) return; _form.KeepSavedRedisUrl(); _revision++; _state = _state with { ConnectionResult = null }; });
    }
    public void EnableRedis()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_form == null || !CanEdit("redis.url")) return; _form.Reset("redis.url"); _form.Reset("redis.db"); _revision++; _state = _state with { ConnectionResult = null }; });
    }
    /// <summary>
    /// The Use Redis switch turned off (official RedisGroup.tsx, #4696). With a saved URL it stages the empty URL, and the shown database number with it, as the
    /// switch-off. With none saved there is nothing to switch off: what was typed goes, URL and number, because the fields that showed it are about to hide and an
    /// edit the admin cannot see must not be saved.
    /// </summary>
    public void DisableRedis()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || !CanEdit("redis.url")) return;
            if (_state.Configured.Contains("redis.url")) _form.StageRedisUrl("");
            else { _form.Reset("redis.url"); _form.Reset("redis.db"); }
            _revision++; _state = _state with { ConnectionResult = null };
        });
    }
    private bool CanEdit(string key) => !_state.Loading && !(_state.Saving && Redis.Contains(key)) &&
        !(_state.SensitiveStatusError && SensitiveStatusKeys.Contains(key)) &&
        !(_state.ManagedByEnv.Contains(key) || (Redis.Contains(key) && Redis.Any(_state.ManagedByEnv.Contains)));

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (_state.Saving) return; _form?.Discard(); _revision++; _state = _state with { ConnectionResult = null }; });
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
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.Saving || _state.Conflict || _state.OutcomeUnknown) return;
                // An emptied Redis database number is the one write the server still accepts under REDIS_URL (see AdminSettingsForm).
                var managed = _state.ManagedByEnv.Where(key => !(key == "redis.db" && _form.Get(key).Trim().Length == 0)).ToArray();
                IEnumerable<string> excluded = _state.SensitiveStatusError ? SensitiveStatusKeys : Redis.Any(managed.Contains) ? Redis.Where(key => !(key == "redis.db" && _state.ManagedByEnv.Contains(key) && _form.Get(key).Trim().Length == 0)) : [];
                intent = _form.CaptureSave(excluded.Concat(managed));
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _revision++;
                _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null, ConnectionResult = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _api.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                var secretPresence = intent.Values.Where(entry => Secrets.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value.Length > 0, StringComparer.Ordinal);
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    var configured = new HashSet<string>(_state.Configured, StringComparer.Ordinal);
                    foreach (var (key, present) in secretPresence) { if (present) configured.Add(key); else configured.Remove(key); }
                    _state = _state with { Saving = false, Configured = Array.AsReadOnly(configured.ToArray()) };
                });
                if (_lifetime.IsCurrent(action)) { _store.Invalidate(); savedKeys = keys; }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); }
            catch (Exception error) { Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); }
            if (savedKeys != null && _lifetime.IsCurrent(action) && Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                    try { handler(savedKeys); } catch { /* An observer cannot change an acknowledged write. */ }
        }
    }

    public async Task CheckRedisAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            Dictionary<string, string> values; string[] dirty; long revision;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Saving || _state.Checking || _state.SensitiveStatusError) return;
                var managed = Redis.Any(_state.ManagedByEnv.Contains);
                dirty = managed ? [] : _form.DirtyKeys.Where(Redis.Contains).ToArray();
                values = new(StringComparer.Ordinal);
                if (!managed) { values["redis.db"] = _form.Get("redis.db"); if (dirty.Contains("redis.url")) values["redis.url"] = _form.Get("redis.url"); }
                revision = _revision;
                _state = _state with { Checking = true, ConnectionResult = null };
            }
            Changed?.Invoke();
            try
            {
                var result = await _api.CheckConnectionAsync(action, "redis", values, dirty).ConfigureAwait(false);
                Update(action, () => _state = _state with { Checking = false, ConnectionResult = revision == _revision ? result : null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch { Update(action, () => _state = _state with { Checking = false, ConnectionResult = revision == _revision ? new(false, "Connection check failed.") : null }); }
        }
    }

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
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _revision++; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
