using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminPlaybackSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public IReadOnlyDictionary<string, string> Errors { get; init; } = new Dictionary<string, string>();
    public override string ToString() => nameof(AdminPlaybackSettingsState);
}

/// <summary>Page-owned 33-key playback or separate five-key routing draft, sharing the existing strong-ETag store.</summary>
public sealed class AdminPlaybackSettingsController : IDisposable
{
    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminPlaybackSettingsState _state = new();
    private bool _disposed;
    public IReadOnlyList<string> Keys { get; }
    public AdminPlaybackSettingsController(AdminServerSettingsApi api, AdminServerSettingsStore store, AdminMonitorAuthority authority, bool routing)
    {
        _api = api; _store = store; _lifetime = new(authority); _lifetime.Retired += Clear;
        Keys = routing ? AdminPlaybackSettingsRules.RoutingKeys : AdminPlaybackSettingsRules.Keys;
    }
    public event Action? Changed;
    public AdminPlaybackSettingsState State { get { lock (_gate) return _state; } }
    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner; try { _lifetime.Resume(); owner = _lifetime.Open(); } catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _form?.Discard(); _form = null; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke(); _store.Invalidate(); await Load(owner, false).ConfigureAwait(false);
    }
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease owner; try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; _state = _state with { Loading = true }; }
            Changed?.Invoke(); _store.Invalidate(); await Load(owner, preserveDraft).ConfigureAwait(false);
        }
    }
    private async Task Load(MonitorAuthorityLease owner, bool preserveDraft)
    {
        var effective = _store.GetEffectiveAsync(owner); var restart = _store.GetRestartKeysAsync(owner); var status = _store.GetSensitiveStatusAsync(owner);
        LoggingRestartKeys restartKeys = new(); AdminSettingsSensitiveStatus? sensitive = null;
        try { restartKeys = await restart.ConfigureAwait(false); } catch { }
        try { sensitive = await status.ConfigureAwait(false); } catch { }
        try
        {
            var snapshot = await effective.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (_form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft); else _form = new(Keys, [], snapshot.Values, snapshot.ETag);
                _state = _state with { Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ManagedByEnv = Array.AsReadOnly((sensitive?.ManagedByEnv ?? []).Where(Keys.Contains).ToArray()), RestartKeys = restartKeys };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Reload before editing." }); }
    }
    public void Set(string key, string value)
    {
        if (!Keys.Contains(key)) throw new ArgumentException("This setting belongs to another page.");
        if (_lifetime.Owner is not { } owner) return;
        Update(owner, () =>
        {
            if (_form == null || !_state.Loaded || _state.Loading || _state.Saving || _state.ManagedByEnv.Contains(key)) return;
            _form.Set(key, value);
            if (key == "markers.online_storage" && value == "on_demand" && !_state.ManagedByEnv.Contains("markers.lazy_playback")) _form.Set("markers.lazy_playback", "true");
        });
    }
    public void ApplyPreset(string preset) { foreach (var (key, value) in AdminPlaybackSettingsRules.Preset(preset)) Set(key, value); }
    public void Discard() { if (_lifetime.Owner is { } owner) Update(owner, () => { if (!_state.Saving) _form?.Discard(); }); }
    public async Task SaveAsync()
    {
        MonitorAuthorityLease owner; try { owner = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (owner)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (_disposed || !_lifetime.IsCurrent(owner) || _form == null || !_state.Loaded || _state.Saving || _state.Loading || _state.Conflict || _state.OutcomeUnknown || _state.Errors.Count > 0) return;
                intent = _form.CaptureSave(_state.ManagedByEnv); if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, Error = null };
            }
            Changed?.Invoke();
            try
            {
                var result = await _api.PutAsync(owner, intent.Values, intent.ETag, Keys, []).ConfigureAwait(false);
                Update(owner, () => { _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired); _state = _state with { Saving = false }; });
                if (_lifetime.IsCurrent(owner)) _store.Invalidate();
            }
            catch (OperationCanceledException) when (!owner.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(owner, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(owner, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; }); }
            catch { Update(owner, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "The server refused these settings. Review permissions and values." }; }); }
        }
    }
    private void Update(MonitorAuthorityLease owner, Action change)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return; change();
            if (_form != null)
            {
                var values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get));
                var dirty = Array.AsReadOnly(_form.DirtyKeys.ToArray());
                _state = _state with { Values = values, PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.GetPersisted)), DirtyKeys = dirty, RestartRequired = _form.RestartRequired, Errors = AdminPlaybackSettingsRules.Validate(values, dirty) };
            }
        }
        Changed?.Invoke();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) _disposed = true; _lifetime.Dispose(); Clear(); Changed = null; }
}
