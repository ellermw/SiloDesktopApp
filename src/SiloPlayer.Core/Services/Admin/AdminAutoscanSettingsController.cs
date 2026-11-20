using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using T = SiloPlayer.Core.Services.Admin.AdminAutoscanSettingsText;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Autoscan › Settings state (Silo-Server/silo-server 66b836f82 AutoscanSettings.tsx and ConnectionsPanel.tsx).
/// Every read and write belongs to one authority lease; retirement clears every draft and secret. Writes are
/// sent once. A definite refusal (4xx) keeps the draft, including a typed key, so the corrected retry still sends
/// it; a timeout, transport loss, 5xx or malformed acknowledgement blocks further writes until Reload re-reads.
/// </summary>
public sealed class AdminAutoscanSettingsController : IDisposable
{
    private readonly AdminAutoscanSettingsApi _api;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private MonitorAuthorityLease? _testLease;
    private AdminAutoscanSettingsState _state = new();
    public AdminAutoscanSettingsState State { get { lock (_gate) return _state; } }
    public event Action? Changed;

    public AdminAutoscanSettingsController(AdminAutoscanSettingsApi api, AdminMonitorAuthority authority)
    {
        _api = api; _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            if (!_lifetime.IsCurrent(owner)) return;
            _testLease?.Dispose(); _testLease = null;
            _state = new() { Owner = owner, Loading = true };
        }
        Changed?.Invoke();
        await LoadAsync(owner).ConfigureAwait(false);
    }

    public async Task ReloadAsync()
    {
        lock (_gate) { if (_state.Busy || _state.Testing || _state.Loading) return; }
        await OpenAsync().ConfigureAwait(false);
    }

    private async Task LoadAsync(MonitorAuthorityLease owner)
    {
        var settings = LoadSettingsAsync(owner);
        var connections = LoadConnectionsAsync(owner);
        var servers = LoadServersAsync(owner);
        await Task.WhenAll(settings, connections, servers).ConfigureAwait(false);
        Update(owner, state => state with { Loading = false });
    }

    private async Task LoadSettingsAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var saved = await _api.GetSettingsAsync(owner).ConfigureAwait(false);
            Update(owner, state => state with { Saved = saved, Interval = Number(saved.DefaultPollIntervalSeconds), Debounce = Number(saved.DebounceSeconds), SettingsError = null });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { Update(owner, state => state with { SettingsError = T.T("loadError") }); }
    }
    private async Task LoadConnectionsAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var rows = await _api.GetConnectionsAsync(owner).ConfigureAwait(false);
            Update(owner, state => state with { Connections = rows, ConnectionsLoaded = true, ConnectionsError = null });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { Update(owner, state => state with { Connections = [], ConnectionsLoaded = false, ConnectionsError = T.T("connectionsLoadError") }); }
    }
    private async Task LoadServersAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var rows = await _api.GetRequestServersAsync(owner).ConfigureAwait(false);
            Update(owner, state => state with { Servers = rows, ServersLoaded = true, ServersError = null });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { Update(owner, state => state with { Servers = [], ServersLoaded = false, ServersError = T.T("serversLoadError") }); }
    }

    public void EditDefaults(string interval, string debounce) => Edit(state => state.Saved == null ? state : state with
        { Interval = interval, Debounce = debounce, Error = null, Notice = null, RescheduleState = null });
    public void Discard() => Edit(state => state.Saved is not { } saved ? state : state with
        { Interval = Number(saved.DefaultPollIntervalSeconds), Debounce = Number(saved.DebounceSeconds), Error = null });

    public async Task SaveDefaultsAsync()
    {
        AdminAutoscanSettingsState captured;
        MonitorAuthorityLease? action;
        lock (_gate)
        {
            if (!_state.CanWrite || !_state.Dirty || _state.Saved == null) return;
            if (!int.TryParse(_state.Interval, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) || interval <= 0
                || !int.TryParse(_state.Debounce, NumberStyles.None, CultureInfo.InvariantCulture, out var debounce) || debounce < 0)
            { _state = _state with { Error = T.T("invalidDefaults") }; action = null; captured = _state; }
            else { captured = _state; action = Capture(); if (action == null) return; _state = _state with { Busy = true, Error = null, Notice = null }; }
        }
        Changed?.Invoke(); if (action == null) return;
        using (action)
        {
            var submitted = false;
            try
            {
                // The server has no revision precondition for this write. Read the newest enabled switch (Sources owns it)
                // and refuse to overwrite polling defaults somebody else changed since this page loaded them.
                var latest = await _api.GetSettingsAsync(action).ConfigureAwait(false);
                if (latest.DefaultPollIntervalSeconds != captured.Saved!.DefaultPollIntervalSeconds || latest.DebounceSeconds != captured.Saved.DebounceSeconds)
                { Conflict(action, T.T("defaultsChanged")); return; }
                action.ThrowIfRetired(); submitted = true;
                var result = await _api.SaveSettingsAsync(action, new(latest.Enabled, int.Parse(captured.Interval, CultureInfo.InvariantCulture), int.Parse(captured.Debounce, CultureInfo.InvariantCulture))).ConfigureAwait(false);
                Update(action, state => state with { Saved = result.Settings, Interval = Number(result.Settings.DefaultPollIntervalSeconds), Debounce = Number(result.Settings.DebounceSeconds),
                    RescheduleState = result.RescheduleState, Notice = T.T("saved"), Error = null, SettingsError = null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error) { Failed(action, error, submitted, T.T("settingsUnconfirmed")); }
            finally { Update(action, state => state with { Busy = false }); }
        }
    }

    public void OpenEditor(string? id = null) => Edit(state =>
    {
        if (!state.ConnectionsLoaded || state.Editor != null || state.DeleteTarget != null) return state;
        var saved = id == null ? null : state.Connections.FirstOrDefault(row => row.Id == id);
        if (id != null && saved == null) return state;
        var first = state.Servers.FirstOrDefault();
        // Official: reuse is the default for a new connection when a Requests Sonarr/Radarr server exists; an edit starts from the raw saved values.
        return state with { Editor = saved == null ? new() { Reuse = first != null, IntegrationId = first?.Id ?? "" }
            : new() { Id = saved.Id, Name = saved.Name, Kind = saved.Kind, BaseUrl = saved.BaseUrl ?? "", HasApiKey = saved.HasApiKey,
                Reuse = saved.RequestIntegrationId != null, IntegrationId = saved.RequestIntegrationId ?? "" }, TestResult = null, TestError = null, Error = null, Notice = null, RescheduleState = null };
    });

    public void EditConnection(AdminAutoscanConnectionEditor editor)
    {
        lock (_gate)
        {
            if (_state.Owner?.IsCurrent != true || _state.Busy || _state.NeedsReview || _state.Editor is not { } current
                || editor.Id != current.Id || editor.HasApiKey != current.HasApiKey || current.Id != null && editor.Reuse != current.Reuse) return;
            if (editor.Name.Length > AdminAutoscanLimits.Name || editor.BaseUrl.Length > AdminAutoscanLimits.BaseUrl || editor.ApiKey.Length > AdminAutoscanLimits.ApiKey
                || editor.Kind.Length > AdminAutoscanLimits.Kind || editor.IntegrationId.Length > AdminAutoscanLimits.Id) return;
            if (editor.Reuse != current.Reuse) editor = editor with { ApiKey = "" };
            if (editor == current) return;
            _testLease?.Dispose(); _testLease = null;
            // An edit after a refusal starts a new attempt: the stale refusal text goes, the typed values stay.
            _state = _state with { Editor = editor, Testing = false, TestResult = null, TestError = null, Error = null };
        }
        Changed?.Invoke();
    }
    public void CloseEditor()
    {
        lock (_gate)
        {
            if (_state.Busy) return;
            _testLease?.Dispose(); _testLease = null;
            _state = _state with { Editor = null, DeleteTarget = null, Testing = false, TestResult = null, TestError = null };
        }
        Changed?.Invoke();
    }
    public void RequestDelete(string id) => Edit(state => !state.ConnectionsLoaded || state.Editor != null ? state : state with
        { DeleteTarget = state.Connections.FirstOrDefault(row => row.Id == id), Error = null, Notice = null, RescheduleState = null });

    public async Task SaveConnectionAsync()
    {
        AdminAutoscanConnectionEditor editor;
        Dictionary<string, string> body;
        MonitorAuthorityLease? action;
        lock (_gate)
        {
            if (!_state.CanWrite || !_state.ConnectionsLoaded || _state.Editor is not { CanSave: true } draft || draft.Reuse && !_state.ServersLoaded) return;
            editor = draft;
            try { body = editor.SaveBody(_state.Servers); }
            catch (ArgumentException error) { _state = _state with { Error = error.Message }; Changed?.Invoke(); return; }
            action = Capture(); if (action == null) return;
            _state = _state with { Busy = true, Error = null };
        }
        Changed?.Invoke();
        using (action)
        {
            var submitted = false;
            try
            {
                if (editor.Id != null && !await CheckTargetAsync(action, editor.Id).ConfigureAwait(false)) return;
                action.ThrowIfRetired(); submitted = true;
                var saved = await _api.SaveConnectionAsync(action, editor.Id, body).ConfigureAwait(false);
                Update(action, state => state with { Editor = null, TestResult = null, TestError = null, Error = null,
                    Connections = Sorted(state.Connections.Where(row => row.Id != saved.Id).Append(saved)), Notice = T.T(editor.Id == null ? "created" : "updated"), RescheduleState = null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error) { Failed(action, error, submitted, T.T(editor.Id == null ? "createUnconfirmed" : "updateUnconfirmed")); }
            // A refusal keeps the draft and its typed key for the corrected retry; success closed the editor and an unknown outcome cleared the key.
            finally { body.Clear(); Update(action, state => state with { Busy = false }); }
        }
    }

    public async Task DeleteConnectionAsync()
    {
        AdminAutoscanSavedConnection target; MonitorAuthorityLease? action;
        lock (_gate)
        {
            if (!_state.CanWrite || _state.DeleteTarget is not { } saved) return;
            target = saved; action = Capture(); if (action == null) return; _state = _state with { Busy = true, Error = null };
        }
        Changed?.Invoke();
        using (action)
        {
            var submitted = false;
            try
            {
                if (!await CheckTargetAsync(action, target.Id).ConfigureAwait(false)) return;
                action.ThrowIfRetired(); submitted = true;
                await _api.DeleteConnectionAsync(action, target.Id).ConfigureAwait(false);
                Update(action, state => state with { DeleteTarget = null, Error = null, Connections = Array.AsReadOnly(state.Connections.Where(row => row.Id != target.Id).ToArray()), Notice = T.T("deleted"), RescheduleState = null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error) { Failed(action, error, submitted, T.T("deleteUnconfirmed")); }
            finally { Update(action, state => state with { Busy = false }); }
        }
    }

    private async Task<bool> CheckTargetAsync(MonitorAuthorityLease action, string id)
    {
        AdminAutoscanSavedConnection? expected;
        lock (_gate) expected = _state.Connections.FirstOrDefault(row => row.Id == id);
        var rows = await _api.GetConnectionsAsync(action).ConfigureAwait(false);
        var actual = rows.FirstOrDefault(row => row.Id == id);
        if (expected == null || actual != expected)
        { Conflict(action, T.T("targetChanged")); return false; }
        return true;
    }

    public async Task TestConnectionAsync()
    {
        AdminAutoscanConnectionEditor editor; Dictionary<string, string> body; MonitorAuthorityLease? action;
        lock (_gate)
        {
            if (!_state.CanWrite || _state.Editor is not { CanTest: true } draft) return;
            editor = draft;
            try { body = editor.TestBody(); }
            catch (ArgumentException error) { _state = _state with { TestError = error.Message }; Changed?.Invoke(); return; }
            action = Capture(); if (action == null) return; _testLease = action;
            _state = _state with { Testing = true, TestResult = null, TestError = null };
        }
        Changed?.Invoke();
        using (action)
        {
            try
            {
                var result = await _api.TestConnectionAsync(action, body).ConfigureAwait(false);
                Update(action, state => ReferenceEquals(state.Editor, editor) ? state with { TestResult = result } : state);
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (Exception error)
            {
                // Advisory only: a refusal shows the server's reason, anything else the official "Connection test failed".
                var text = error is AdminAutoscanRefusedException refused ? refused.Message : T.T("testRequestFailed");
                Update(action, state => ReferenceEquals(state.Editor, editor) ? state with { TestError = text } : state);
            }
            finally
            {
                body.Clear();
                lock (_gate) { if (ReferenceEquals(_testLease, action)) _testLease = null; }
                Update(action, state => state with { Testing = false });
            }
        }
    }

    private MonitorAuthorityLease? Capture() { try { return _lifetime.CaptureAction(); } catch (OperationCanceledException) { return null; } }
    /// <summary>The write was not sent: its target changed. Blocks writes until Reload; any typed key is cleared.</summary>
    private void Conflict(MonitorAuthorityLease owner, string message) => Update(owner, state => state with { NeedsReview = true, Error = message, Editor = state.Editor is { } editor ? editor with { ApiKey = "" } : null });
    private void Failed(MonitorAuthorityLease owner, Exception error, bool submitted, string unknown)
    {
        if (submitted && error is AdminAutoscanRefusedException refused)
        {
            // A definite refusal: the server applied nothing. Show its reason; the draft, typed key and the writes stay available.
            Update(owner, state => state with { Error = refused.Message });
            return;
        }
        Update(owner, state => state with
        {
            NeedsReview = true, OutcomeUnknown = submitted, Error = submitted ? unknown : T.T("notSent"),
            Editor = state.Editor is { } editor ? editor with { ApiKey = "" } : null,
        });
    }
    private static IReadOnlyList<AdminAutoscanSavedConnection> Sorted(IEnumerable<AdminAutoscanSavedConnection> rows) =>
        Array.AsReadOnly(rows.OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray());
    private void Edit(Func<AdminAutoscanSettingsState, AdminAutoscanSettingsState> change)
    {
        lock (_gate) { if (!_state.CanWrite) return; _state = change(_state); }
        Changed?.Invoke();
    }
    private void Update(MonitorAuthorityLease owner, Func<AdminAutoscanSettingsState, AdminAutoscanSettingsState> change)
    {
        lock (_gate) { if (!_lifetime.IsCurrent(owner)) return; _state = change(_state); }
        Changed?.Invoke();
    }
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private void Clear()
    {
        lock (_gate) { _testLease?.Dispose(); _testLease = null; _state = new(); }
        Changed?.Invoke();
    }
    public void Dispose() { _lifetime.Retired -= Clear; _lifetime.Dispose(); Clear(); Changed = null; }
}
