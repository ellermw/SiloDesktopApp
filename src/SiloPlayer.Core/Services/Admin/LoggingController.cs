using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public sealed record LoggingPageSnapshot
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? Notice { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public bool ServerAdvancedOpen { get; init; }
    public bool RetentionAdvancedOpen { get; init; }
}

public sealed class LoggingController : IDisposable
{
    private readonly AdminMonitorApi _api;
    private readonly LogsAuthorityLifetime _lifetime;
    private readonly LoggingDraft _draft;
    private readonly object _gate = new();
    private LoggingPageSnapshot _state = new();
    private LoggingSettingsSnapshot? _reconciliation;
    private bool _disposed;
    public LoggingController(AdminMonitorApi api, AdminMonitorAuthority authority, LoggingDisclosurePreferences? preferences = null)
    {
        _draft = new(preferences);
        _api = api; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    public LoggingPageSnapshot State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();
    public bool IsCurrent(LoggingPageSnapshot snapshot) { lock (_gate) return !_disposed && ReferenceEquals(snapshot, _state) && (snapshot.Owner == null || _lifetime.IsCurrent(snapshot.Owner)); }
    public bool IsActionCurrent(MonitorAuthorityLease action) => !_disposed && _lifetime.IsCurrent(action);

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _draft.Clear(); _reconciliation = null; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        await Task.WhenAll(LoadSettingsAsync(owner), LoadRestartAsync(owner)).ConfigureAwait(false);
    }

    private async Task LoadSettingsAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var settings = await _api.LoggingGetSettingsAsync(owner).ConfigureAwait(false);
            Update(owner, () => { _draft.Load(settings); _state = _state with { Loading = false, Error = null }; });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Error = error is InvalidDataException ? error.Message : LogsText.T("logs.native.logging.loadFailed") }); }
    }

    private async Task LoadRestartAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var keys = await _api.LoggingGetRestartKeysAsync(owner).ConfigureAwait(false);
            Update(owner, () => { _draft.SetRestartKeys(keys); _state = _state with { RestartKeys = keys }; });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception) { Update(owner, () => { _draft.SetRestartKeys(new()); _state = _state with { RestartKeys = new() }; }); }
    }

    public void Set(string key, string value)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { _draft.Set(key, value); _state = _state with { Notice = null, Error = _state.Conflict ? _state.Error : null }; });
    }
    public void ToggleServerAdvanced() { var owner = _lifetime.Owner; if (owner != null) Update(owner, _draft.ToggleServerAdvanced); }
    public void ToggleRetentionAdvanced() { var owner = _lifetime.Owner; if (owner != null) Update(owner, _draft.ToggleRetentionAdvanced); }
    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner != null) Update(owner, () => { _draft.Discard(); _state = _state with { Notice = null }; });
    }

    public async Task SaveAsync(MonitorAuthorityLease action)
    {
        LoggingSaveIntent intent;
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(action) || _disposed || _state.Saving || _state.Conflict || _draft.DirtyKeys.Count == 0) return;
            try { intent = _draft.BeginSave(); }
            catch (ArgumentException error) { _state = _state with { Error = error.Message }; Changed?.Invoke(); return; }
            _state = _state with { Saving = true, Error = null, Notice = null };
        }
        Changed?.Invoke();
        try
        {
            var result = await _api.LoggingSaveAsync(action, intent).ConfigureAwait(false);
            Update(action, () => { _draft.Acknowledge(intent, result); _state = _state with { Saving = false, Notice = LogsText.T("logs.native.logging.saved") }; });
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { }
        catch (LoggingSettingsConflictException error) { Update(action, () => _state = _state with { Saving = false, Conflict = true, Error = error.Message }); }
        catch (Exception error) { Update(action, () => _state = _state with { Saving = false, Error = error is ApiException ? error.Message : LogsText.T("logs.native.logging.saveFailed") }); }
    }

    public async Task<LoggingSettingsSnapshot?> PrepareReconciliationAsync(MonitorAuthorityLease action)
    {
        lock (_gate) { if (_disposed || !_lifetime.IsCurrent(action) || _state.Saving) return null; }
        try
        {
            var latest = await _api.LoggingGetSettingsAsync(action).ConfigureAwait(false);
            lock (_gate) { if (_disposed || !_lifetime.IsCurrent(action)) return null; _reconciliation = latest; return latest; }
        }
        catch (OperationCanceledException) when (!action.IsCurrent) { return null; }
        catch (Exception) { Update(action, () => _state = _state with { Error = LogsText.T("logs.native.logging.reloadFailed") }); return null; }
    }

    public void AcceptReconciliation(MonitorAuthorityLease action, LoggingSettingsSnapshot latest, bool preserveDraft)
    {
        Update(action, () =>
        {
            if (!ReferenceEquals(latest, _reconciliation) || _state.Saving) return;
            _draft.Load(latest, preserveDraft);
            _reconciliation = null;
            _state = _state with { Loading = false, Conflict = false, Error = null, Notice = preserveDraft ? LogsText.T("logs.native.logging.reloaded") : null };
        });
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { _draft.Clear(); _reconciliation = null; _state = new(); }
            else _state = _state with { Loaded = _draft.Loaded, Values = _draft.Values, DirtyKeys = _draft.DirtyKeys, RestartRequired = _draft.RestartRequired, ServerAdvancedOpen = _draft.ServerAdvancedOpen, RetentionAdvancedOpen = _draft.RetentionAdvancedOpen };
        }
        Changed?.Invoke();
    }

    private void Clear()
    {
        lock (_gate) { _draft.Clear(); _reconciliation = null; _state = new(); }
        Changed?.Invoke();
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Changed = null;
    }
}
