using System.Collections.ObjectModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminLibrarySettingsState
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
    public IReadOnlyDictionary<string, string> PersistedValues { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public IReadOnlyDictionary<string, AdminLibrarySettingError> Errors { get; init; } = new Dictionary<string, AdminLibrarySettingError>();
    public bool RatingSourcesLoading { get; init; }
    public bool RatingSourcesSupported { get; init; }
    public bool RatingSourcesLoaded { get; init; }
    public bool RatingSourcesError { get; init; }
    public IReadOnlyList<AdminRatingSource> RatingSources { get; init; } = [];
    /// <summary>True only after a successful bounded node list read.</summary>
    public bool NodesKnown { get; init; }
    public bool TranscodeNodeAvailable { get; init; }
    public override string ToString() => nameof(AdminLibrarySettingsState);
}

/// <summary>
/// Page-owned guarded settings state for Manage &gt; Libraries &gt; Settings. The one shared
/// <see cref="AdminSettingsForm"/> supplies strong-ETag writes, conflict review, late-edit preservation and retirement.
/// Rating capabilities and node availability are advisory reads and never block the settings form itself.
/// </summary>
public sealed class AdminLibrarySettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys => AdminLibrarySettingsRules.Keys;
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(Array.Empty<string>());

    private readonly AdminServerSettingsApi _settingsApi;
    private readonly AdminLibrarySettingsApi _libraryApi;
    private readonly AdminMonitorApi _monitorApi;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminLibrarySettingsState _state = new();
    private bool _disposed;

    public AdminLibrarySettingsController(AdminServerSettingsApi settingsApi, AdminLibrarySettingsApi libraryApi,
        AdminMonitorApi monitorApi, AdminServerSettingsStore store, AdminMonitorAuthority authority)
    {
        _settingsApi = settingsApi; _libraryApi = libraryApi; _monitorApi = monitorApi; _store = store;
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminLibrarySettingsState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            _form?.Discard(); _form = null;
            _state = new() { Owner = owner, Loading = true, RatingSourcesLoading = true };
        }
        Changed?.Invoke();
        _store.Invalidate();
        await Task.WhenAll(LoadSettingsAsync(owner, false, false), LoadRatingSourcesAsync(owner), LoadNodesAsync(owner)).ConfigureAwait(false);
    }

    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Saving || _state.Loading) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with
            {
                Loading = true, Error = null, RatingSourcesLoading = true, RatingSourcesError = false, NodesKnown = false,
            });
            await Task.WhenAll(LoadSettingsAsync(action, true, preserveDraft), LoadRatingSourcesAsync(action), LoadNodesAsync(action)).ConfigureAwait(false);
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
                    Loading = false, Loaded = true, Conflict = false, OutcomeUnknown = false,
                    Error = null, ErrorCode = null, Failure = null,
                    ManagedByEnv = Array.AsReadOnly((status?.ManagedByEnv ?? []).Where(Keys.Contains).ToArray()),
                    RestartKeys = restartKeys,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () => _state = _state with
            {
                Loading = false, Error = "Could not load settings. Try again.",
                ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner),
            });
        }
    }

    private async Task LoadRatingSourcesAsync(MonitorAuthorityLease owner)
    {
        bool supported;
        try { supported = await _libraryApi.SupportsPluginRatingSourcesAsync(owner).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch
        {
            // The WebUI treats a failed capability read like an older server: no Ratings group.
            Update(owner, () => _state = _state with
            {
                RatingSourcesLoading = false, RatingSourcesSupported = false, RatingSourcesLoaded = false,
                RatingSourcesError = false, RatingSources = [],
            });
            return;
        }
        if (!supported)
        {
            Update(owner, () => _state = _state with
            {
                RatingSourcesLoading = false, RatingSourcesSupported = false, RatingSourcesLoaded = false,
                RatingSourcesError = false, RatingSources = [],
            });
            return;
        }
        Update(owner, () => _state = _state with { RatingSourcesSupported = true });
        try
        {
            var sources = await _libraryApi.ListRatingSourcesAsync(owner).ConfigureAwait(false);
            Update(owner, () => _state = _state with
            {
                RatingSourcesLoading = false, RatingSourcesLoaded = true, RatingSourcesError = false, RatingSources = sources,
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch
        {
            Update(owner, () => _state = _state with
            {
                RatingSourcesLoading = false, RatingSourcesLoaded = false, RatingSourcesError = true, RatingSources = [],
            });
        }
    }

    private async Task LoadNodesAsync(MonitorAuthorityLease owner)
    {
        try
        {
            var nodes = await _monitorApi.ListDashboardNodesAsync(owner).ConfigureAwait(false);
            Update(owner, () => _state = _state with
            {
                NodesKnown = true,
                TranscodeNodeAvailable = nodes.Any(node => node.Type == "transcode" && node.Enabled && node.Healthy),
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch { Update(owner, () => _state = _state with { NodesKnown = false, TranscodeNodeAvailable = false }); }
    }

    private bool CanEdit(string key) => _form != null && _state.Loaded && !_state.Loading && !_state.ManagedByEnv.Contains(key);

    public void Set(string key, string value)
    {
        if (!Keys.Contains(key)) throw new ArgumentException("This setting is not owned by the Library Settings page.", nameof(key));
        Edit(key, form => form.Set(key, value));
    }

    public void SetRatingSource(string source, bool on) => Edit(AdminLibrarySettingsRules.ExtraRatingSources, form =>
        form.Set(AdminLibrarySettingsRules.ExtraRatingSources,
            AdminLibrarySettingsRules.ToggleRatingSource(form.Get(AdminLibrarySettingsRules.ExtraRatingSources), source, on)));

    public void RestoreWorkerDefaults()
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            foreach (var (key, value) in AdminLibrarySettingsRules.WorkerDefaults)
                if (CanEdit(key) && _form!.Get(key) != value) _form.Set(key, value);
        });
    }

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
                if (_state.Errors.Keys.Any(key => !_state.ManagedByEnv.Contains(key))) return;
                intent = _form.CaptureSave(_state.ManagedByEnv);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { Saving = true, Error = null, ErrorCode = null, Failure = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _settingsApi.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var keys = intent.Values.Keys.ToArray();
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false };
                });
                if (_lifetime.IsCurrent(action)) { _store.Invalidate(); savedKeys = keys; }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException)
            {
                Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Conflict = true, Error = "Settings changed on the server. Reload and review your changes." }; });
            }
            catch (AdminSettingsOutcomeUnknownException)
            {
                Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, OutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review before saving again." }; });
            }
            catch (Exception error)
            {
                Update(action, () =>
                {
                    _form?.EndSave(intent);
                    _state = _state with { Saving = false, Error = "Failed to update settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) };
                });
            }
            if (savedKeys != null && _lifetime.IsCurrent(action) && Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                    try { handler(savedKeys); } catch { /* An observer cannot alter an acknowledged write. */ }
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
                var values = new ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal));
                var dirty = Array.AsReadOnly(_form.DirtyKeys.ToArray());
                _state = _state with
                {
                    Values = values,
                    PersistedValues = new ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.GetPersisted, StringComparer.Ordinal)),
                    DirtyKeys = dirty,
                    RestartRequired = _form.RestartRequired,
                    Errors = AdminLibrarySettingsRules.Validate(values, dirty),
                };
            }
        }
        Changed?.Invoke();
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _state = new(); } Changed?.Invoke(); }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose(); Clear(); Changed = null; Saved = null;
    }
}
