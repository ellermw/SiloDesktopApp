using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminDownloadSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool RestartRequired { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    /// <summary>Canonical acknowledged values, separate from newer staged edits.</summary>
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    /// <summary>The saved <c>playback.transcode_dir</c> the blank prepared-file directory derives from (read-only here).</summary>
    public string TranscodeDirectory { get; init; } = "";
    /// <summary>Inline validation failures for edited fields; any entry blocks Save.</summary>
    public IReadOnlyDictionary<string, AdminDownloadFieldError> Errors { get; init; } = new Dictionary<string, AdminDownloadFieldError>();
    /// <summary>Where a blank prepared-file directory resolves to for the saved transcode directory.</summary>
    public string DerivedArtifactDirectory => AdminDownloadSettingsRules.EffectiveArtifactDirectory("", TranscodeDirectory);
    public override string ToString() => nameof(AdminDownloadSettingsState);
}

/// <summary>
/// Page-owned Manage > Downloads > Settings state (official <c>DownloadsSettings.tsx</c>). Reuses the shared guarded
/// settings API, snapshot store and draft form. This is server configuration only: it never reads or writes the
/// signed-in user's own offline-download folder or device settings. Events may arrive on any thread.
/// </summary>
public sealed class AdminDownloadSettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys => AdminDownloadSettingsRules.Keys;
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(Array.Empty<string>());
    /// <summary>Keys read for display by the shared snapshot store but never staged or written by this page.</summary>
    public static IReadOnlyList<string> ReadOnlyKeys { get; } = Array.AsReadOnly([AdminDownloadSettingsRules.TranscodeDir]);

    private readonly AdminServerSettingsApi _api;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminDownloadSettingsState _state = new();
    private bool _disposed;

    public AdminDownloadSettingsController(AdminServerSettingsApi api, AdminServerSettingsStore store, AdminMonitorAuthority authority)
    {
        _api = api; _store = store; _lifetime = new(authority); _lifetime.Retired += Clear;
    }
    public event Action? Changed;
    /// <summary>Raised after an acknowledged save so the shell can refresh restart status.</summary>
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminDownloadSettingsState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate) { _form?.Discard(); _form = null; _state = new() { Owner = owner, Loading = true }; }
        Changed?.Invoke();
        _store.Invalidate();
        await LoadAsync(owner, false, false).ConfigureAwait(false);
    }

    /// <summary>Explicit reconciliation after a conflict, an unknown outcome or a failed read.</summary>
    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with { Loading = true, Error = null });
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
        // Environment management and restart hints are advisory: the official page edits without them.
        try { status = await sensitive.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
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
                _state = _state with
                {
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false, Error = null, ErrorCode = null, Failure = null,
                    ManagedByEnv = Array.AsReadOnly((status?.ManagedByEnv ?? []).Where(Keys.Contains).ToArray()), RestartKeys = restartKeys,
                    TranscodeDirectory = snapshot.Values.GetValueOrDefault(AdminDownloadSettingsRules.TranscodeDir, ""),
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error) { Update(owner, () => _state = _state with { Loading = false, Error = "Could not load settings. Try again.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner) }); }
    }

    private bool CanEdit(string key) => _form != null && _state.Loaded && !_state.Loading && !_state.ManagedByEnv.Contains(key);

    /// <summary>Stages a value; a value equal to the saved one withdraws the edit (official form behavior).</summary>
    public void Set(string key, string value)
    {
        if (!Keys.Contains(key)) throw new ArgumentException("This setting is not owned by the Downloads page.", nameof(key));
        Edit(key, form => form.Set(key, value));
    }

    /// <summary>Stages a blank prepared-file directory, restoring the server's derived default.</summary>
    public void ResetArtifactDirectory() => Set(AdminDownloadSettingsRules.ArtifactDir, "");

    private void Edit(string key, Action<AdminSettingsForm> edit)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (CanEdit(key)) edit(_form!); });
    }

    public void Discard()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => { if (!_state.Saving) _form?.Discard(); });
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
                // The server would reject these; keep the draft and let the inline errors explain.
                if (_state.Errors.Keys.Any(key => !_state.ManagedByEnv.Contains(key))) return;
                intent = _form.CaptureSave(_state.ManagedByEnv);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _api.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false };
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

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { _form?.Discard(); _form = null; _state = new(); }
            else if (_form != null)
            {
                var values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal));
                var dirty = Array.AsReadOnly(_form.DirtyKeys.ToArray());
                _state = _state with
                {
                    Values = values,
                    PersistedValues = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                    DirtyKeys = dirty, RestartRequired = _form.RestartRequired,
                    Errors = AdminDownloadSettingsRules.Validate(values, dirty),
                };
            }
        }
        Changed?.Invoke();
    }
    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
