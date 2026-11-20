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
    /// <summary>A settings save whose outcome is unknown; Save stays blocked until a reload.</summary>
    public bool OutcomeUnknown { get; init; }
    /// <summary>An image write whose outcome is unknown, or whose result could not be re-read; image writes stay blocked until a reload.</summary>
    public bool AssetsUnknown { get; init; }
    public bool S3Configured { get; init; }
    /// <summary>The branding slot or "overlay-images" an image write is running for.</summary>
    public string? AssetBusy { get; init; }
    public string? Error { get; init; }
    public AdminFailureKind? Failure { get; init; }
    /// <summary>The server's own words for a refused image write (problem detail or field error).</summary>
    public string? AssetError { get; init; }
    public AdminAppearanceBranding? Branding { get; init; }
    public OverlayConfigResponse? OverlayConfig { get; init; }
    /// <summary>The settings manifest revision, or null while unknown (official useSettingsCapabilities).</summary>
    public int? ManifestRevision { get; init; }
    public IReadOnlySet<string> ShownRatingSources { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>The administrator's own ui.card_presentation poster size, which the preview draws at.</summary>
    public string PosterSize { get; init; } = "standard";
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> DirtyKeys { get; init; } = [];
    public LoggingRestartKeys RestartKeys { get; init; } = new();
    /// <summary>Revision 25 accepts style, the plate and band presets and badge images (official supportsOverlayStyles).</summary>
    public bool SupportsStyles => ManifestRevision >= 25;
    public bool CanSave => Owner?.IsCurrent == true && Loaded && !Loading && !Saving && AssetBusy == null && !Conflict && !OutcomeUnknown;
    public bool CanWriteImages => Owner?.IsCurrent == true && Loaded && !Loading && !Saving && AssetBusy == null && !AssetsUnknown;
    public override string ToString() => nameof(AdminAppearanceSettingsState);
}

/// <summary>
/// Page-owned state of Admin Configure &gt; Appearance (official AppearanceSettings.tsx at
/// Silo Server 66b836f82). The four appearance keys save as one batch; branding images
/// and badge images keep their own single-shot writes. Events may arrive on any thread.
/// </summary>
public sealed class AdminAppearanceSettingsController : IDisposable
{
    public const string AccentKey = "branding.accent_color";
    public const string OverlaysEnabledKey = "overlays.enabled";
    public const string OverlayDefaultsKey = "defaults.card_overlays";
    public const string TitleArtDefaultKey = "defaults.ui.title_art";
    public const string OverlayImagesKind = "overlay-images";
    public static IReadOnlyList<string> Keys { get; } = Array.AsReadOnly(new[] { AccentKey, OverlaysEnabledKey, OverlayDefaultsKey, TitleArtDefaultKey });
    private const string S3PublicBucketKey = "s3.public_bucket";
    private readonly AdminAppearanceApi _api;
    private readonly AdminServerSettingsApi _settings;
    private readonly SettingsApi? _contract;
    private readonly CatalogApi? _catalog;
    private readonly AdminAuthorityLifetime _lifetime;
    private readonly object _gate = new();
    private AdminSettingsForm? _form;
    private AdminAppearanceSettingsState _state = new();
    private bool _disposed;

    public AdminAppearanceSettingsController(AdminAppearanceApi api, AdminServerSettingsApi settings, AdminMonitorAuthority authority,
        SettingsApi? contract = null, CatalogApi? catalog = null)
    {
        _api = api;
        _settings = settings;
        _contract = contract;
        _catalog = catalog;
        _lifetime = new(authority);
        _lifetime.Retired += Clear;
    }

    public event Action? Changed;
    /// <summary>Raised after an acknowledged settings save or image change so the app can refresh its theme and cards.</summary>
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

    /// <summary>Re-reads everything, keeping staged edits. Also clears a conflict or an unknown outcome.</summary>
    public async Task ReloadAsync(bool preserveDraft = true)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return; }
        using (action)
        {
            lock (_gate)
            {
                if (!_lifetime.IsCurrent(action) || _state.Saving || _state.Loading || _state.AssetBusy != null) return;
                _state = _state with { Loading = true };
            }
            Changed?.Invoke();
            await LoadAsync(action, preserveDraft).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(MonitorAuthorityLease owner, bool preserveDraft)
    {
        // Every read starts at once. Only the settings snapshot is required to edit; the
        // rest degrade the way the official hooks do, and each failure says so with Reload.
        var settings = Attempt(() => _settings.GetEffectiveAsync(owner, Keys.Append(S3PublicBucketKey), [], owner.Token));
        // The official form waits for the sensitive status; no Appearance key is env-managed, so its failure only ends the wait.
        var sensitive = Attempt(() => _settings.GetSensitiveStatusAsync(owner, owner.Token));
        var restart = Attempt(() => _settings.GetRestartKeysAsync(owner, owner.Token));
        var branding = Attempt(() => _api.GetBrandingAsync(owner, owner.Token));
        var overlays = Attempt(() => _api.GetOverlayConfigAsync(owner, owner.Token));
        var capabilities = _contract is null ? Task.FromResult<(SettingsContractCapabilities?, Exception?)>((null, null))
            : Attempt(() => _contract.GetContractCapabilitiesAsync(owner.Context, owner.Token));
        var ratings = _catalog is null ? Task.FromResult<(IReadOnlySet<string>?, Exception?)>((new HashSet<string>(StringComparer.Ordinal), null))
            : Attempt(() => _catalog.GetShownRatingSourcesAsync(owner.Token));
        var presentation = _contract is null ? Task.FromResult<(ContractEffectiveSettingsResponse?, Exception?)>((null, null))
            : Attempt(() => _contract.GetContractEffectiveSettingsAsync(owner.Context, ["ui.card_presentation"], ct: owner.Token));
        var (snapshot, settingsError) = await settings.ConfigureAwait(false);
        await sensitive.ConfigureAwait(false);
        var (restartKeys, restartError) = await restart.ConfigureAwait(false);
        var (currentBranding, brandingError) = await branding.ConfigureAwait(false);
        var (currentOverlays, overlaysError) = await overlays.ConfigureAwait(false);
        var (currentCapabilities, capabilitiesError) = await capabilities.ConfigureAwait(false);
        var (shownRatings, ratingsError) = await ratings.ConfigureAwait(false);
        var (effective, _) = await presentation.ConfigureAwait(false);
        var partial = restartError != null || brandingError != null || overlaysError != null || capabilitiesError != null || ratingsError != null;
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
                Loaded = _form != null,
                Conflict = snapshot == null && _state.Conflict,
                OutcomeUnknown = snapshot == null && _state.OutcomeUnknown,
                AssetsUnknown = (brandingError != null || overlaysError != null) && _state.AssetsUnknown,
                S3Configured = snapshot != null ? snapshot.Values.GetValueOrDefault(S3PublicBucketKey, "").Length > 0 : _state.S3Configured,
                Branding = currentBranding ?? (brandingError == null ? _state.Branding : null),
                OverlayConfig = currentOverlays ?? (overlaysError == null ? _state.OverlayConfig : null),
                ManifestRevision = currentCapabilities is { ApiVersion: 1 } ? currentCapabilities.Revision : null,
                ShownRatingSources = shownRatings ?? new HashSet<string>(StringComparer.Ordinal),
                PosterSize = PosterSize(effective),
                RestartKeys = restartKeys ?? new(),
                AssetError = null,
                Failure = settingsError != null ? AdminFailures.Classify(settingsError, owner) : null,
                Error = settingsError != null ? "Could not load Appearance settings. Try again."
                    : partial ? "Some Appearance details could not be loaded. Reload to try again."
                    : null,
            };
        });
    }

    private static async Task<(T? Value, Exception? Error)> Attempt<T>(Func<Task<T>> read) where T : class
    {
        try { return (await read().ConfigureAwait(false), null); }
        catch (Exception error) { return (null, error); }
    }

    private static string PosterSize(ContractEffectiveSettingsResponse? effective)
    {
        var value = effective?.Settings.FirstOrDefault(setting => setting.Key == "ui.card_presentation")?.Value;
        return value is { ValueKind: JsonValueKind.Object } presentation && presentation.TryGetProperty("poster_size", out var size) &&
            size.ValueKind == JsonValueKind.String && size.GetString() is "compact" or "standard" or "large" ? size.GetString()! : "standard";
    }

    /// <summary>Stages one edit. Setting the saved value back clears its dirty mark.</summary>
    public void Set(string key, string value)
    {
        if (!Keys.Contains(key, StringComparer.Ordinal)) throw new ArgumentException("Unknown Appearance setting.", nameof(key));
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () =>
        {
            if (_form == null || _state.Loading || _form.Get(key) == value) return;
            _form.Set(key, value);
        });
    }

    /// <summary>Shows (or clears) why a picked image was refused before anything was sent.</summary>
    public void ShowAssetError(string? message)
    {
        var owner = _lifetime.Owner;
        if (owner == null) return;
        Update(owner, () => _state = _state with { AssetError = message });
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
                if (!_lifetime.IsCurrent(action) || _form == null || !_state.CanSave) return;
                intent = _form.CaptureSave();
                if (intent.Values.Count == 0) { _form.EndSave(intent); return; }
                if (!ValidSettings(intent.Values))
                {
                    _form.EndSave(intent);
                    _state = _state with { Error = "Review the invalid Appearance settings before saving.", Failure = null };
                }
                else _state = _state with { Saving = true, Error = null, Failure = null };
            }
            Changed?.Invoke();
            if (!State.Saving) return;
            var saved = false;
            try
            {
                var result = await _settings.PutAsync(action, intent.Values, intent.ETag, Keys, [], action.Token).ConfigureAwait(false);
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
            catch (Exception error)
            {
                // A definite refusal: the draft stays for the admin to correct and retry.
                Update(action, () => { _form?.EndSave(intent); _state = _state with { Saving = false, Error = "Failed to update settings.", Failure = AdminFailures.Classify(error, action) }; });
            }
            if (saved && _lifetime.IsCurrent(action)) NotifySaved();
        }
    }

    public Task<bool> UploadAssetAsync(string kind, byte[] bytes, string mediaType)
        => ChangeImagesAsync(kind, upload: true, action => _api.UploadAssetAsync(action, kind, bytes, mediaType, action.Token));

    public Task<bool> DeleteAssetAsync(string kind)
        => ChangeImagesAsync(kind, upload: false, action => _api.DeleteAssetAsync(action, kind, action.Token));

    public Task<bool> UploadOverlayImageAsync(byte[] bytes, string mediaType, string name)
        => ChangeImagesAsync(OverlayImagesKind, upload: true, action => _api.UploadOverlayImageAsync(action, bytes, mediaType, name, action.Token));

    public Task<bool> DeleteOverlayImageAsync(string reference)
        => ChangeImagesAsync(OverlayImagesKind, upload: false, action => _api.DeleteOverlayImageAsync(action, reference, action.Token));

    /// <summary>
    /// One image write, sent once and never replayed. Afterwards the images and the settings
    /// snapshot (whose ETag a branding write changes) are re-read; a draft whose saved value
    /// changed meanwhile becomes a conflict rather than overwriting the newer value.
    /// Returns true only when the server acknowledged the write.
    /// </summary>
    private async Task<bool> ChangeImagesAsync(string kind, bool upload, Func<MonitorAuthorityLease, Task> change)
    {
        MonitorAuthorityLease action;
        try { action = _lifetime.CaptureAction(); }
        catch (OperationCanceledException) { return false; }
        using (action)
        {
            lock (_gate)
            {
                var overlay = kind == OverlayImagesKind;
                // A read that failed leaves no state to act on; uploads also need storage.
                var available = overlay
                    ? _state.OverlayConfig is { } config && (!upload || config.ImageUploadsAvailable)
                    : _state.Branding is { } branding && (!upload || branding.StorageAvailable);
                if (!_lifetime.IsCurrent(action) || !_state.CanWriteImages || !available) return false;
                _state = _state with { AssetBusy = kind, AssetError = null };
            }
            Changed?.Invoke();
            var saved = false;
            var acknowledged = false;
            try
            {
                await change(action).ConfigureAwait(false);
                acknowledged = true;
                saved = _lifetime.IsCurrent(action);
                if (kind == OverlayImagesKind)
                {
                    var overlays = await _api.GetOverlayConfigAsync(action, action.Token).ConfigureAwait(false);
                    Update(action, () => _state = _state with { OverlayConfig = overlays });
                }
                else
                {
                    var branding = await _api.GetBrandingAsync(action, action.Token).ConfigureAwait(false);
                    var snapshot = await _settings.GetEffectiveAsync(action, Keys.Append(S3PublicBucketKey), [], action.Token).ConfigureAwait(false);
                    Update(action, () =>
                    {
                        if (_form != null && _form.DirtyKeys.Any(key => _form.GetPersisted(key) != snapshot.Values.GetValueOrDefault(key, "")))
                            _state = _state with { Conflict = true, Error = "Settings changed on the server. Reload and review your changes." };
                        else if (!_state.Conflict)
                            _form?.Reload(snapshot.Values, snapshot.ETag, preserveDraft: true);
                        _state = _state with { Branding = branding };
                    });
                }
            }
            catch (OperationCanceledException) when (!action.IsCurrent) { }
            catch (AdminAppearanceOutcomeUnknownException)
            {
                Update(action, () => _state = _state with { AssetsUnknown = true, Error = "The image change outcome is unknown. Reload and review before trying again." });
            }
            catch (ApiException error) when (!acknowledged)
            {
                // A definite refusal: nothing changed, and the admin may correct and retry.
                Update(action, () => _state = _state with { AssetError = AdminAppearanceApi.RefusalText(error, upload), Failure = AdminFailures.Classify(error, action) });
            }
            catch (ArgumentException) when (!acknowledged)
            {
                // Refused locally before anything was sent.
                Update(action, () => _state = _state with { AssetError = AdminAppearanceText.T(upload ? "web.admin.settings.hooks.branding.uploadFailed" : "web.admin.settings.hooks.branding.removeFailed") });
            }
            catch (Exception) when (acknowledged)
            {
                Update(action, () => _state = _state with { AssetsUnknown = true, Error = "The image changed, but the page could not refresh. Reload to see the current images." });
            }
            catch (Exception)
            {
                Update(action, () => _state = _state with { AssetsUnknown = true, Error = "The image change outcome is unknown. Reload and review before trying again." });
            }
            finally { Update(action, () => _state = _state with { AssetBusy = null }); }
            if (saved && _lifetime.IsCurrent(action)) NotifySaved();
            return acknowledged;
        }
    }

    private static bool ValidSettings(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            if (key == AccentKey && value != "" &&
                (value.Length != 7 || value[0] != '#' || !value[1..].All(Uri.IsHexDigit))) return false;
            if (key is OverlaysEnabledKey or TitleArtDefaultKey && value is not ("" or "true" or "false")) return false;
            if (key != OverlayDefaultsKey || value == "") continue;
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
            try { handler(); } catch { /* An observer cannot change an acknowledged write. */ }
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
