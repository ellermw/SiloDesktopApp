using System.Collections.ObjectModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminProvidersState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool SavingSettings { get; init; }
    public bool CheckingMdblist { get; init; }
    public bool SettingsConflict { get; init; }
    public bool SettingsOutcomeUnknown { get; init; }
    /// <summary>
    /// Subtitle provider tiles in the official "save not confirmed" state, by provider. Only reading that one provider's editor again
    /// clears its entry; neither another provider's read nor the page's list read does.
    /// </summary>
    public IReadOnlyDictionary<string, AdminSubtitleSaveFailure> SubtitleSaveFailures { get; init; } = new Dictionary<string, AdminSubtitleSaveFailure>();
    public bool SubtitleConflict => SubtitleSaveFailures.Values.Contains(AdminSubtitleSaveFailure.Conflict);
    public bool SubtitleOutcomeUnknown => SubtitleSaveFailures.Values.Contains(AdminSubtitleSaveFailure.OutcomeUnknown);
    public bool SubtitleSaveNotConfirmed(string provider) => SubtitleSaveFailures.ContainsKey(provider);
    /// <summary>
    /// Subtitle provider tiles whose last read failed, as the official tile's "Unable to load saved configuration. Reload before editing."
    /// Only a successful read of that one provider clears its entry.
    /// </summary>
    public IReadOnlyCollection<string> SubtitleLoadFailures { get; init; } = [];
    public bool SubtitleLoadFailed(string provider) => SubtitleLoadFailures.Contains(provider);
    /// <summary>The official success line of a subtitle tile after its save, by provider; it stays until that provider is read again.</summary>
    public IReadOnlyDictionary<string, string> SubtitleSaveMessages { get; init; } = new Dictionary<string, string>();
    public string? SubtitleSaveMessage(string provider) => SubtitleSaveMessages.GetValueOrDefault(provider);
    /// <summary>The provider whose editor is being read. Reading is not saving: the tile shows "Loading saved configuration...", never "Saving".</summary>
    public string? ReadingProvider { get; init; }
    public bool MarkerOutcomeUnknown { get; init; }
    public bool SensitiveStatusError { get; init; }
    public string? BusyProvider { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public AdminFailureKind? Failure { get; init; }
    public string? ProviderFeedback { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> Configured { get; init; } = [];
    public IReadOnlyCollection<string> ManagedByEnv { get; init; } = [];
    /// <summary>The keys the server's environment sets, with the variable for each (official <c>useSettingsForm.envVar</c>); a failed read keeps the last list.</summary>
    public AdminEnvManagedSettings EnvManaged { get; init; } = AdminEnvManagedSettings.None;
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public IReadOnlyList<AdminSubtitleProvider> SubtitleProviders { get; init; } = [];
    public IReadOnlyList<AdminMarkerProvider> MarkerProviders { get; init; } = [];
    public IReadOnlyList<AdminPluginProviderTile> PluginProviders { get; init; } = [];
    public bool SubtitleProvidersFailed { get; init; }
    public bool MarkerProvidersFailed { get; init; }
    public bool PluginProvidersFailed { get; init; }
    public AdminSettingsConnectionResult? MdblistTest { get; init; }
    public string? SubtitleTestProvider { get; init; }
    public AdminSubtitleProviderTestResult? SubtitleTest { get; init; }
    public string? MarkerTestProvider { get; init; }
    public AdminMarkerProviderValidation? MarkerTest { get; init; }
    /// <summary>True while a conflict or unknown outcome means only a canonical reload can say what the server holds.</summary>
    public bool ReloadRequired => SettingsConflict || SettingsOutcomeUnknown || SubtitleConflict || SubtitleOutcomeUnknown || MarkerOutcomeUnknown;
    public AdminSubtitleProviderTestResult? SubtitleTestFor(string provider) =>
        string.Equals(SubtitleTestProvider, provider, StringComparison.Ordinal) ? SubtitleTest : null;
    public AdminMarkerProviderValidation? MarkerTestFor(string provider) =>
        string.Equals(MarkerTestProvider, provider, StringComparison.Ordinal) ? MarkerTest : null;
    public override string ToString() => nameof(AdminProvidersState);
}

/// <summary>
/// Page-owned Configure > Providers state. MDBList uses the shared settings draft; subtitles use
/// one-shot strong-ETag editors; marker writes contain changed fields only. Plugin credentials are
/// inventory/status only and remain owned by the Plugins page.
/// </summary>
public sealed class AdminProvidersController : IDisposable
{
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(["mdblist.api_key", "markers.mode"]);
    public static IReadOnlyList<string> Secrets { get; } = Array.AsReadOnly(["mdblist.api_key"]);
    public static IReadOnlyList<string> ReadOnlyKeys { get; } = Array.AsReadOnly(["markers.mode"]);

    private readonly AdminProvidersApi _providers;
    private readonly AdminServerSettingsApi _settings;
    private readonly AdminServerSettingsStore _store;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminProvidersState _state = new();
    private long _revision;
    private long _subtitleTestRevision;
    private bool _disposed;

    public AdminProvidersController(AdminProvidersApi providers, AdminServerSettingsApi settings,
        AdminServerSettingsStore store, AdminMonitorAuthority authority)
    {
        _providers = providers; _settings = settings; _store = store;
        _lifetime = new(authority); _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    public event Action<IReadOnlyCollection<string>>? Saved;
    public AdminProvidersState State { get { lock (_gate) return _state; } }

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        _store.Invalidate();
        lock (_gate)
        {
            _form?.Discard(); _form = null; _revision++;
            _state = new() { Owner = owner, Loading = true };
        }
        Changed?.Invoke();
        await LoadAsync(owner, reload: false, preserveDraft: false).ConfigureAwait(false);
    }

    public async Task ReloadAsync(bool preserveDraft)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.Loading || _state.SavingSettings || _state.BusyProvider != null) return; }
            _store.Invalidate();
            Update(action, () => _state = _state with { Loading = true, Error = null, ProviderFeedback = null });
            await LoadAsync(action, reload: true, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool reload, bool preserveDraft)
    {
        var effectiveTask = _store.GetEffectiveAsync(owner);
        var sensitiveTask = _store.GetSensitiveStatusAsync(owner);
        var restartTask = _store.GetRestartKeysAsync(owner);
        var subtitlesTask = _providers.ListSubtitleProvidersAsync(owner);
        var markersTask = _providers.ListMarkerProvidersAsync(owner);
        var pluginsTask = _providers.ListPluginInstallationsAsync(owner);

        AdminSettingsSensitiveStatus? sensitive = null;
        LoggingRestartKeys restart = new();
        IReadOnlyList<AdminSubtitleProvider> subtitles = [];
        IReadOnlyList<AdminMarkerProvider> markers = [];
        IReadOnlyList<AdminPluginProviderTile> plugins = [];
        bool sensitiveFailed = false, subtitlesFailed = false, markersFailed = false, pluginsFailed = false;
        try { sensitive = await sensitiveTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { sensitiveFailed = true; }
        try { restart = await restartTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { }
        try { subtitles = await subtitlesTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { subtitlesFailed = true; }
        try { markers = await markersTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { markersFailed = true; }
        try { plugins = AdminProviderPresentation.PluginTiles(await pluginsTask.ConfigureAwait(false)); }
        catch (OperationCanceledException) when (!owner.IsCurrent) { return; }
        catch { pluginsFailed = true; }

        try
        {
            var snapshot = await effectiveTask.ConfigureAwait(false);
            Update(owner, () =>
            {
                if (reload && _form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(Keys, Secrets, snapshot.Values, snapshot.ETag);
                // A key the environment sets now loses its draft and is never sent. A read that failed after an answer keeps that list (the official form does);
                // the tile stays locked by the failed status either way.
                var environment = AdminEnvManagedSettings.Resolve(sensitive, _state.EnvManaged);
                _form.ApplyEnvironment(environment);
                _revision++;
                _state = _state with
                {
                    Loading = false, Loaded = true, SettingsConflict = false, SettingsOutcomeUnknown = false,
                    // A tile that is not confirmed stays so until its own provider is read again; the list only drops providers that no longer exist.
                    SubtitleSaveFailures = subtitlesFailed ? _state.SubtitleSaveFailures : KeepFailuresFor(_state.SubtitleSaveFailures, subtitles),
                    SubtitleLoadFailures = subtitlesFailed ? _state.SubtitleLoadFailures : _state.SubtitleLoadFailures.Where(name => IsListed(subtitles, name)).ToArray(),
                    SubtitleSaveMessages = subtitlesFailed ? _state.SubtitleSaveMessages : _state.SubtitleSaveMessages.Where(item => IsListed(subtitles, item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    MarkerOutcomeUnknown = markersFailed && _state.MarkerOutcomeUnknown,
                    Error = null, ErrorCode = null, Failure = null, ProviderFeedback = null,
                    SensitiveStatusError = sensitiveFailed,
                    Configured = sensitive?.Configured ?? [], ManagedByEnv = environment.Keys.Where(Keys.Contains).ToArray(), EnvManaged = environment, RestartKeys = restart,
                    SubtitleProviders = subtitles, MarkerProviders = markers, PluginProviders = plugins,
                    SubtitleProvidersFailed = subtitlesFailed, MarkerProvidersFailed = markersFailed, PluginProvidersFailed = pluginsFailed,
                };
            });
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { }
        catch (Exception error)
        {
            Update(owner, () => _state = _state with
            {
                Loading = false, Error = "Could not load provider settings. Try again.",
                ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, owner),
            });
        }
    }

    public void SetMdblistApiKey(string value)
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || _state.Loading || _state.SavingSettings || _state.SensitiveStatusError || _state.ManagedByEnv.Contains("mdblist.api_key")) return;
            // An erased box is not a draft: Test must then check the saved key, as the official page does.
            if (value.Trim().Length == 0) _form.Reset("mdblist.api_key"); else _form.Set("mdblist.api_key", value);
            _revision++;
            _state = _state with { MdblistTest = null, ProviderFeedback = null };
        });
    }

    public void DiscardMdblist()
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || _state.SavingSettings) return;
            _form.Reset("mdblist.api_key"); _revision++;
            _state = _state with { MdblistTest = null, ProviderFeedback = null };
        });
    }

    /// <summary>Page-local draft edits invalidate a displayed or in-flight check without forcing a control-tree rebuild.</summary>
    public void ClearMdblistTest()
    {
        var owner = _lifetime.Owner;
        lock (_gate)
        {
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return;
            _revision++;
            _state = _state with { MdblistTest = null, ProviderFeedback = null };
        }
    }

    public void ClearSubtitleTest(string provider)
    {
        var owner = _lifetime.Owner;
        lock (_gate)
        {
            if (_disposed || owner == null || !_lifetime.IsCurrent(owner)) return;
            _subtitleTestRevision++;
            _state = _state with { ProviderFeedback = null };
            if (_state.SubtitleTestProvider == provider || _state.BusyProvider == provider)
                _state = _state with { SubtitleTestProvider = null, SubtitleTest = null };
        }
    }

    /// <summary>Clears the key only for the authority that showed the confirmation; a replaced authority never inherits it.</summary>
    public Task ClearMdblistAsync(MonitorAuthorityLease confirmedOwner) => SaveMdblistAsync(clear: true, confirmedOwner);
    public Task SaveMdblistAsync() => SaveMdblistAsync(clear: false, null);

    private async Task SaveMdblistAsync(bool clear, MonitorAuthorityLease? confirmedOwner)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            if (confirmedOwner != null && (!ReferenceEquals(confirmedOwner, _lifetime.Owner) || !confirmedOwner.IsCurrent)) return;
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.SavingSettings || _state.SettingsConflict || _state.SettingsOutcomeUnknown ||
                    _state.SensitiveStatusError || _state.ManagedByEnv.Contains("mdblist.api_key")) return;
                if (clear) _form.Set("mdblist.api_key", "", stage: true);
                intent = _form.CaptureSave(ReadOnlyKeys);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                _state = _state with { SavingSettings = true, Error = null, ProviderFeedback = null, MdblistTest = null };
            }
            Changed?.Invoke();
            string[]? savedKeys = null;
            try
            {
                var result = await _settings.PutAsync(action, intent.Values, intent.ETag, Keys, Secrets).ConfigureAwait(false);
                var present = intent.Values.GetValueOrDefault("mdblist.api_key", "").Length > 0;
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    var configured = new HashSet<string>(_state.Configured, StringComparer.Ordinal);
                    if (present) configured.Add("mdblist.api_key"); else configured.Remove("mdblist.api_key");
                    _state = _state with { SavingSettings = false, Configured = Array.AsReadOnly(configured.ToArray()), ProviderFeedback = present ? "MDBList API key saved." : "MDBList credentials cleared." };
                });
                if (_lifetime.IsCurrent(action)) { _store.Invalidate(); savedKeys = ["mdblist.api_key"]; }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminSettingsConflictException) { Update(action, () => { _form?.EndSave(intent); DropFailedClear(clear); _state = _state with { SavingSettings = false, SettingsConflict = true, Error = "Settings changed on the server. Reload and review your MDBList draft." }; }); }
            catch (AdminSettingsOutcomeUnknownException) { Update(action, () => { _form?.EndSave(intent); DropFailedClear(clear); _state = _state with { SavingSettings = false, SettingsOutcomeUnknown = true, Error = "The save outcome is unknown. Reload and review your MDBList draft before saving again." }; }); }
            catch (Exception error) { Update(action, () => { _form?.EndSave(intent); DropFailedClear(clear); _state = _state with { SavingSettings = false, Error = "Failed to update MDBList settings.", ErrorCode = (error as ApiException)?.ErrorCode, Failure = AdminFailures.Classify(error, action) }; }); }
            if (savedKeys != null && Saved is { } handlers)
                foreach (Action<IReadOnlyCollection<string>> handler in handlers.GetInvocationList())
                    try { handler(savedKeys); } catch { }
        }
    }

    /// <summary>A Disconnect that did not complete is not a typed draft; it must not be sent by a later Test or prompt on leaving.</summary>
    private void DropFailedClear(bool clear) { if (clear && _form?.IsClearStaged("mdblist.api_key") == true) _form.Reset("mdblist.api_key"); }

    public async Task TestMdblistAsync()
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            Dictionary<string, string> values; string[] dirty; long revision;
            lock (_gate)
            {
                if (_form == null || _state.CheckingMdblist || _state.SavingSettings || _state.SensitiveStatusError) return;
                var managed = _state.ManagedByEnv.Contains("mdblist.api_key");
                dirty = !managed && _form.DirtyKeys.Contains("mdblist.api_key") && _form.Get("mdblist.api_key").Trim().Length > 0 ? ["mdblist.api_key"] : [];
                values = dirty.Length > 0 ? new(StringComparer.Ordinal) { ["mdblist.api_key"] = _form.Get("mdblist.api_key") } : [];
                revision = _revision; _state = _state with { CheckingMdblist = true, MdblistTest = null };
            }
            Changed?.Invoke();
            try
            {
                var result = await _settings.CheckConnectionAsync(action, "mdblist", values, dirty).ConfigureAwait(false);
                Update(action, () => _state = _state with { CheckingMdblist = false, MdblistTest = revision == _revision ? result : null });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch { Update(action, () => _state = _state with { CheckingMdblist = false, MdblistTest = revision == _revision ? new(false, "Connection check failed.") : null }); }
        }
    }

    public async Task<AdminSubtitleProviderEditor?> GetSubtitleEditorAsync(string provider)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return null; }
        using (action)
        {
            // A read never runs beside another provider action: finishing it would release that action's busy state.
            lock (_gate) { if (_state.BusyProvider != null) return null; }
            // The official loadEditor() starts by clearing the tile's feedback; the previous failure or success line of this provider ends here.
            Update(action, () => _state = _state with
            {
                BusyProvider = provider, ReadingProvider = provider, ProviderFeedback = null,
                SubtitleLoadFailures = WithoutName(_state.SubtitleLoadFailures, provider),
                SubtitleSaveMessages = WithoutMessage(_state.SubtitleSaveMessages, provider),
            });
            try
            {
                var editor = await _providers.GetSubtitleProviderAsync(action, provider).ConfigureAwait(false);
                // Reading this provider again is the official tile Reload: it ends this provider's "save not confirmed" state and no other.
                // A read that finishes after the server or account changed applies nothing and hands back no editor: its ETag belongs to the old authority.
                var applied = false;
                Update(action, () =>
                {
                    applied = true;
                    _state = _state with
                    {
                        SubtitleSaveFailures = WithoutFailure(_state.SubtitleSaveFailures, provider),
                        SubtitleProviders = Array.AsReadOnly(_state.SubtitleProviders.Select(item =>
                            item.ProviderName == provider
                                ? new AdminSubtitleProvider
                                {
                                    ProviderName = editor.Provider,
                                    Enabled = editor.Enabled,
                                    HasApiKey = editor.HasApiKey,
                                    HasCredentials = editor.HasCredentials,
                                    UpdatedAt = editor.UpdatedAt,
                                }
                                : item).ToArray()),
                    };
                });
                return applied ? editor : null;
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return null; }
            // The official tile drops its editor when a read fails: the tile reads "Unable to load saved configuration. Reload before editing."
            // and stays disabled until one read succeeds. The "not confirmed" state of this provider is left as it was.
            catch (Exception error)
            {
                Update(action, () => _state = _state with
                {
                    SubtitleLoadFailures = WithName(_state.SubtitleLoadFailures, provider),
                    ProviderFeedback = "Could not load provider configuration: " + SafeError(error),
                });
                return null;
            }
            finally { Update(action, () => _state = _state with { BusyProvider = null, ReadingProvider = null }); }
        }
    }

    /// <summary>A short page-level line from the page (for a refused click, or the first failed read of a page Reload); nothing is sent.</summary>
    public void NotifyProvider(string message)
    {
        var owner = _lifetime.Owner; if (owner == null) return;
        Update(owner, () => _state = _state with { ProviderFeedback = message });
    }

    public async Task<bool> SaveSubtitleAsync(AdminSubtitleProviderEditor editor, AdminSubtitleProviderChange change)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (action)
        {
            lock (_gate) { if (_state.BusyProvider != null || _state.SubtitleSaveFailures.ContainsKey(editor.Provider)) return false; }
            // The official tile clears a connection-test result only when the save succeeded, so a failed attempt leaves it standing.
            Update(action, () => _state = _state with { BusyProvider = editor.Provider, ProviderFeedback = null });
            try
            {
                var saved = await _providers.SaveSubtitleProviderAsync(action, editor, change).ConfigureAwait(false);
                // The official tile stays open on its success line and stays off until that provider is read again.
                Update(action, () => _state = _state with
                {
                    ProviderFeedback = null,
                    SubtitleSaveMessages = WithMessage(_state.SubtitleSaveMessages, editor.Provider, SubtitleSaveMessageFor(saved)),
                    SubtitleTestProvider = _state.SubtitleTestProvider == editor.Provider ? null : _state.SubtitleTestProvider,
                    SubtitleTest = _state.SubtitleTestProvider == editor.Provider ? null : _state.SubtitleTest,
                });
                try
                {
                    var list = await _providers.ListSubtitleProvidersAsync(action).ConfigureAwait(false);
                    Update(action, () => _state = _state with { SubtitleProviders = list });
                }
                catch (OperationCanceledException) when (!action.IsCurrent) { }
                catch { /* The acknowledged write stands; a later explicit reload reconciles the list. */ }
                return true;
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return false; }
            // An editor read under another server or account was refused before anything was sent: the tile still says so and Reload recovers it.
            catch (AdminProviderAuthorityChangedException) { Update(action, () => _state = _state with { SubtitleSaveFailures = WithFailure(_state.SubtitleSaveFailures, editor.Provider, AdminSubtitleSaveFailure.Refused), ProviderFeedback = "The server or account changed. Reopen the provider and try again." }); return false; }
            // Every failed save, whatever the answer, leaves the tile in "save not confirmed" as the official page does: the tile shows
            // it and Save stays off until that provider has been read again. Nothing is sent again meanwhile.
            catch (AdminProviderConflictException) { Update(action, () => _state = _state with { SubtitleSaveFailures = WithFailure(_state.SubtitleSaveFailures, editor.Provider, AdminSubtitleSaveFailure.Conflict) }); return false; }
            catch (AdminProviderOutcomeUnknownException) { Update(action, () => _state = _state with { SubtitleSaveFailures = WithFailure(_state.SubtitleSaveFailures, editor.Provider, AdminSubtitleSaveFailure.OutcomeUnknown) }); return false; }
            catch (Exception) { Update(action, () => _state = _state with { SubtitleSaveFailures = WithFailure(_state.SubtitleSaveFailures, editor.Provider, AdminSubtitleSaveFailure.Refused) }); return false; }
            finally { Update(action, () => _state = _state with { BusyProvider = null }); }
        }
    }

    public async Task TestSubtitleAsync(string provider, AdminSubtitleProviderChange change)
    {
        long revision;
        lock (_gate) revision = _subtitleTestRevision;
        await ProviderAction(provider, async action =>
        {
            var result = await _providers.TestSubtitleProviderAsync(action, provider, change).ConfigureAwait(false);
            Update(action, () =>
            {
                if (revision != _subtitleTestRevision) return;
                _state = _state with
                {
                    SubtitleTestProvider = provider, SubtitleTest = result,
                    ProviderFeedback = result.Success ? "Connection successful." : result.Error ?? "Connection failed.",
                };
            });
        }).ConfigureAwait(false);
    }

    public async Task<bool> SaveMarkerAsync(string provider, IReadOnlyDictionary<string, object?> patch)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return false; }
        using (action)
        {
            lock (_gate) { if (_state.BusyProvider != null || _state.MarkerOutcomeUnknown) return false; }
            Update(action, () => _state = _state with { BusyProvider = provider, ProviderFeedback = null });
            try
            {
                var updated = await _providers.UpdateMarkerProviderAsync(action, provider, patch).ConfigureAwait(false);
                Update(action, () => _state = _state with
                {
                    MarkerProviders = Array.AsReadOnly(_state.MarkerProviders.Select(item => item.Provider == provider ? updated : item).ToArray()),
                    ProviderFeedback = "Marker provider settings saved.", MarkerTestProvider = null, MarkerTest = null,
                });
                return true;
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { return false; }
            catch (AdminProviderOutcomeUnknownException) { Update(action, () => _state = _state with { MarkerOutcomeUnknown = true, ProviderFeedback = "The provider update outcome is unknown. Reload before saving again." }); return false; }
            catch (Exception error) { Update(action, () => _state = _state with { ProviderFeedback = SafeError(error) }); return false; }
            finally { Update(action, () => _state = _state with { BusyProvider = null }); }
        }
    }

    public async Task TestMarkerAsync(string provider)
    {
        await ProviderAction(provider, async action =>
        {
            var result = await _providers.ValidateMarkerProviderAsync(action, provider).ConfigureAwait(false);
            Update(action, () => _state = _state with
            {
                MarkerTestProvider = provider, MarkerTest = result,
                ProviderFeedback = result.Valid ? "Connection successful." : result.Error ?? "Connection test failed.",
            });
        }).ConfigureAwait(false);
    }

    private async Task ProviderAction(string provider, Func<MonitorAuthorityLease, Task> operation)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); } catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate) { if (_state.BusyProvider != null) return; }
            Update(action, () => _state = _state with { BusyProvider = provider, ProviderFeedback = null });
            try { await operation(action).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminProviderOutcomeUnknownException) { Update(action, () => _state = _state with { ProviderFeedback = "The provider update outcome is unknown. Reload before saving again." }); }
            catch (AdminProviderTestOutcomeUnknownException) { Update(action, () => _state = _state with { ProviderFeedback = "The connection test outcome is unknown. It was not repeated." }); }
            catch (Exception error) { Update(action, () => _state = _state with { ProviderFeedback = SafeError(error) }); }
            finally { Update(action, () => _state = _state with { BusyProvider = null }); }
        }
    }

    /// <summary>The official <c>providerSaveMessage</c> (web.common.subtitles.providers.*): what this server did with the saved revision.</summary>
    public static string SubtitleSaveMessageFor(AdminSubtitleProviderSaved saved) =>
        saved.LocalApply == AdminSubtitleLocalApply.Applied
            ? string.Equals(saved.LocalAppliedRevision, saved.SavedRevision, StringComparison.Ordinal)
                ? "Settings saved and applied on this server. Other servers may still use older settings."
                : "Settings saved. This server applied a different saved revision; reload to review the current configuration."
            : "Settings saved, but not applied on this server. Other servers may still use older settings.";

    private static bool IsListed(IReadOnlyList<AdminSubtitleProvider> listed, string provider) =>
        listed.Any(item => string.Equals(item.ProviderName, provider, StringComparison.Ordinal));

    private static IReadOnlyCollection<string> WithName(IReadOnlyCollection<string> current, string provider) =>
        current.Contains(provider) ? current : [.. current, provider];

    private static IReadOnlyCollection<string> WithoutName(IReadOnlyCollection<string> current, string provider) =>
        current.Contains(provider) ? current.Where(name => !string.Equals(name, provider, StringComparison.Ordinal)).ToArray() : current;

    private static IReadOnlyDictionary<string, string> WithMessage(IReadOnlyDictionary<string, string> current, string provider, string message) =>
        new Dictionary<string, string>(current, StringComparer.Ordinal) { [provider] = message };

    private static IReadOnlyDictionary<string, string> WithoutMessage(IReadOnlyDictionary<string, string> current, string provider)
    {
        if (!current.ContainsKey(provider)) return current;
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal); next.Remove(provider); return next;
    }

    private static IReadOnlyDictionary<string, AdminSubtitleSaveFailure> WithFailure(IReadOnlyDictionary<string, AdminSubtitleSaveFailure> current, string provider, AdminSubtitleSaveFailure kind) =>
        new Dictionary<string, AdminSubtitleSaveFailure>(current, StringComparer.Ordinal) { [provider] = kind };

    private static IReadOnlyDictionary<string, AdminSubtitleSaveFailure> WithoutFailure(IReadOnlyDictionary<string, AdminSubtitleSaveFailure> current, string provider)
    {
        if (!current.ContainsKey(provider)) return current;
        var next = new Dictionary<string, AdminSubtitleSaveFailure>(current, StringComparer.Ordinal); next.Remove(provider); return next;
    }

    private static IReadOnlyDictionary<string, AdminSubtitleSaveFailure> KeepFailuresFor(IReadOnlyDictionary<string, AdminSubtitleSaveFailure> current, IReadOnlyList<AdminSubtitleProvider> listed) =>
        current.Count == 0 ? current : current.Where(item => listed.Any(provider => string.Equals(provider.ProviderName, item.Key, StringComparison.Ordinal)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static string SafeError(Exception error) => error is ApiException api && api.ErrorCode != "unknown" ? api.ErrorCode.Replace('_', ' ') + "." : "Request failed.";

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (!owner.IsCurrent) { _form?.Discard(); _form = null; _state = new(); }
            else if (_form != null) _state = _state with
            {
                Values = new ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()),
            };
        }
        Changed?.Invoke();
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear() { lock (_gate) { _form?.Discard(); _form = null; _revision++; _state = new(); } Changed?.Invoke(); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; } _lifetime.Dispose(); Clear(); Changed = null; Saved = null; }
}
