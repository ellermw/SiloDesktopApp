using System.Collections.ObjectModel;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Services.Admin;

public sealed record AdminAppearanceSettingsState
{
    public MonitorAuthorityLease? Owner { get; init; }
    public bool Loading { get; init; }
    public bool Loaded { get; init; }
    public bool Saving { get; init; }
    public bool Conflict { get; init; }
    public bool OutcomeUnknown { get; init; }
    public bool StatusUnavailable { get; init; }
    public bool S3Configured { get; init; }
    public string? AssetBusy { get; init; }
    public string? Error { get; init; }
    public AdminAppearanceBranding? Branding { get; init; }
    public OverlayConfigResponse? OverlayConfig { get; init; }
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public IReadOnlyCollection<string> ManagedKeys { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    public override string ToString() => nameof(AdminAppearanceSettingsState);
}

public sealed class AdminAppearanceSettingsController : IDisposable
{
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[]
    {
        "branding.accent_color", "overlays.enabled", "defaults.card_overlays", "defaults.ui.title_art",
    });
    private readonly AdminAppearanceApi _api;
    private readonly AdminServerSettingsApi _settings;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminAppearanceSettingsState _state = new();
    private bool _disposed;

    public AdminAppearanceSettingsController(AdminAppearanceApi api, AdminServerSettingsApi settings, AdminMonitorAuthority authority)
    {
        _api = api;
        _settings = settings;
        _lifetime = new(authority);
        _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    public event Action? Saved;
    public AdminAppearanceSettingsState State { get { lock (_gate) return _state; } }
    public MonitorAuthorityLease CaptureAction() => _lifetime.CaptureAction();

    public async Task OpenAsync()
    {
        MonitorAuthorityLease owner;
        try { _lifetime.Resume(); owner = _lifetime.Open(); }
        catch (OperationCanceledException) { Clear(); return; }
        lock (_gate)
        {
            _form?.Discard();
            _form = null;
            _state = new() { Owner = owner, Loading = true };
        }
        Changed?.Invoke();
        await LoadAsync(owner, preserveDraft: false).ConfigureAwait(false);
    }

    public async Task ReloadAsync(bool preserveDraft = true)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (_state.Saving || _state.Loading || _state.AssetBusy != null) return;
            }
            Update(action, () => _state = _state with { Loading = true, Error = null });
            await LoadAsync(action, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool preserveDraft)
    {
        var settings = _settings.GetEffectiveAsync(owner, Keys.Concat(["s3.public_bucket"]), []);
        var sensitive = _settings.GetSensitiveStatusAsync(owner);
        var restart = _settings.GetRestartKeysAsync(owner);
        var branding = _api.GetBrandingAsync(owner);
        var overlays = _api.GetOverlayConfigAsync(owner);
        AdminSettingsSnapshot? snapshot = null;
        AdminSettingsSensitiveStatus? status = null;
        LoggingRestartKeys restartKeys = new();
        AdminAppearanceBranding? currentBranding = null;
        OverlayConfigResponse? currentOverlays = null;
        var failed = false;
        try { snapshot = await settings.ConfigureAwait(false); }
        catch { failed = true; }
        try { status = await sensitive.ConfigureAwait(false); }
        catch { }
        try { restartKeys = await restart.ConfigureAwait(false); }
        catch { }
        try { currentBranding = await branding.ConfigureAwait(false); }
        catch { }
        try { currentOverlays = await overlays.ConfigureAwait(false); }
        catch { }
        Update(owner, () =>
        {
            if (snapshot != null)
            {
                if (_form != null) _form.Reload(snapshot.Values, snapshot.ETag, preserveDraft);
                else _form = new(Keys, [], snapshot.Values, snapshot.ETag);
            }
            _state = _state with
            {
                Loading = false,
                Loaded = snapshot != null,
                Conflict = failed && _state.Conflict,
                OutcomeUnknown = failed && _state.OutcomeUnknown,
                StatusUnavailable = status == null,
                S3Configured = snapshot?.Values.GetValueOrDefault("s3.public_bucket", "").Length > 0,
                ManagedKeys = status?.ManagedByEnv ?? [],
                Branding = currentBranding,
                OverlayConfig = currentOverlays,
                RestartKeys = restartKeys,
                Error = failed ? "Could not load Appearance settings. Try again."
                    : currentBranding == null ? "Could not read image storage status. Reload to enable image changes."
                    : status == null ? "Could not read managed settings status. Reload before editing."
                    : null,
            };
        });
    }

    public void Set(string key, string value)
    {
        if (!Keys.Contains(key, StringComparer.Ordinal)) throw new ArgumentException("Unknown Appearance setting.", nameof(key));
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || _state.Loading || _state.StatusUnavailable || _state.ManagedKeys.Contains(key)) return;
            _form.Set(key, value);
        });
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
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            AdminSettingsSaveIntent intent;
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _form == null || _state.Loading || _state.Saving ||
                    _state.AssetBusy != null || _state.StatusUnavailable || _state.Conflict || _state.OutcomeUnknown) return;
                intent = _form.CaptureSave(_state.ManagedKeys);
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                if (!ValidSettings(intent.Values))
                {
                    _form.EndSave(intent);
                    _state = _state with { Error = "Review the invalid Appearance settings before saving." };
                }
                else _state = _state with { Saving = true, Error = null };
            }
            Changed?.Invoke();
            if (!State.Saving) return;
            var saved = false;
            try
            {
                var result = await _settings.PutAsync(action, intent.Values, intent.ETag, Keys, []).ConfigureAwait(false);
                Update(action, () =>
                {
                    _form!.Acknowledge(intent, result.Values, result.ETag, result.RestartRequired);
                    _state = _state with { Saving = false };
                    saved = true;
                });
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
            catch
            {
                Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "The server refused the Appearance settings change." }; });
            }
            if (saved && _lifetime.IsCurrent(action)) NotifySaved();
        }
    }

    public Task UploadAssetAsync(string kind, byte[] bytes, string mediaType)
        => ChangeAssetAsync(kind, action => _api.UploadAssetAsync(action, kind, bytes, mediaType), overlayUpload: true);

    public Task DeleteAssetAsync(string kind)
        => ChangeAssetAsync(kind, action => _api.DeleteAssetAsync(action, kind));

    public Task ChangeOverlayImagesAsync(Func<MonitorAuthorityLease, Task> change, bool forUpload = false)
        => ChangeAssetAsync("overlay-images", change, forUpload);

    private async Task ChangeAssetAsync(string kind, Func<MonitorAuthorityLease, Task> change, bool overlayUpload = false)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                var storageAllowed = kind == "overlay-images"
                    ? !overlayUpload || _state.OverlayConfig?.ImageUploadsAvailable == true
                    : !overlayUpload || _state.Branding?.StorageAvailable == true;
                if (!_lifetime.IsCurrent(action) || !_state.Loaded || _state.Loading || _state.Saving || _state.AssetBusy != null ||
                    _state.OutcomeUnknown || !storageAllowed) return;
                _state = _state with { AssetBusy = kind, Error = null };
            }
            Changed?.Invoke();
            var saved = false;
            var acknowledged = false;
            try
            {
                await change(action).ConfigureAwait(false);
                acknowledged = true;
                var branding = await _api.GetBrandingAsync(action).ConfigureAwait(false);
                var snapshot = await _settings.GetEffectiveAsync(action, Keys, []).ConfigureAwait(false);
                var overlays = kind == "overlay-images" ? await _api.GetOverlayConfigAsync(action).ConfigureAwait(false) : null;
                Update(action, () =>
                {
                    if (_form != null && _form.DirtyKeys.Any(key => _form.GetPersisted(key) != snapshot.Values.GetValueOrDefault(key, "")))
                        _state = _state with { Conflict = true, Error = "Settings changed on the server. Reload and review your changes." };
                    else if (!_state.Conflict)
                        _form?.Reload(snapshot.Values, snapshot.ETag, preserveDraft: true);
                    _state = _state with { Branding = branding, OverlayConfig = overlays ?? _state.OverlayConfig };
                    saved = true;
                });
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminAppearanceOutcomeUnknownException)
            {
                Update(action, () => _state = _state with { OutcomeUnknown = true, Error = "The image change outcome is unknown. Reload and review before trying again." });
                if (kind == "overlay-images")
                    try
                    {
                        var overlays = await _api.GetOverlayConfigAsync(action).ConfigureAwait(false);
                        Update(action, () => _state = _state with { OverlayConfig = overlays });
                    }
                    catch { }
            }
            catch
            {
                Update(action, () => _state = _state with { OutcomeUnknown = acknowledged,
                    Error = "Could not confirm the image change. Reload to check the current image." });
            }
            finally { Update(action, () => _state = _state with { AssetBusy = null }); }
            if (saved && _lifetime.IsCurrent(action)) NotifySaved();
        }
    }

    private static bool ValidSettings(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            if (key == "branding.accent_color" && value != "" &&
                (value.Length != 7 || value[0] != '#' || !value[1..].All(Uri.IsHexDigit))) return false;
            if (key is "overlays.enabled" or "defaults.ui.title_art" && value is not ("" or "true" or "false")) return false;
            if (key != "defaults.card_overlays" || value == "") continue;
            if (value.Length > 256 * 1024) return false;
            try
            {
                using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            }
            catch (JsonException) { return false; }
        }
        return true;
    }

    private void Update(MonitorAuthorityLease owner, Action update)
    {
        lock (_gate)
        {
            if (_disposed || !_lifetime.IsCurrent(owner)) return;
            update();
            if (_form != null)
                _state = _state with
                {
                    Values = new ReadOnlyDictionary<string, string>(Keys.ToDictionary(key => key, _form.Get, StringComparer.Ordinal)),
                    DirtyKeys = Array.AsReadOnly(_form.DirtyKeys.ToArray()),
                };
        }
        Changed?.Invoke();
    }

    private void NotifySaved()
    {
        if (Saved is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch { }
    }

    public void Suspend() { _lifetime.Retire(); Clear(); }
    private void Clear()
    {
        lock (_gate) { _form?.Discard(); _form = null; _state = new(); }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _lifetime.Dispose();
        Clear();
        Changed = null;
        Saved = null;
    }
}
