using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Localization;

/// <summary>A profile's effective <c>ui.app_language</c> read for one captured request owner.</summary>
public sealed record AppLanguageRead(ApiRequestContext Context, bool Supported, string? Tag, string? Source);

/// <summary>The outcome of a picker save: the profile value was written; this device's own value may remain.</summary>
public sealed record AppLanguageSaveResult(bool DeviceValueCleared);

/// <summary>
/// The profile-owned interface language setting (contracts/settings/v1 <c>ui.app_language</c>):
/// a nullable language tag where null means "Match device", scopes <c>profile</c> and
/// <c>profile_device</c>, resolved profile_device → profile → default. Reads and
/// writes are bound to a captured request owner; a caller drops any result whose
/// owner is no longer current.
/// </summary>
public sealed class AppLanguagePreference(SettingsApi settings)
{
    public const string SettingKey = "ui.app_language";
    public const int IntroducedInRevision = 28;

    public static bool IsSupported(SettingsContractCapabilities? capabilities)
        => capabilities is { ApiVersion: 1, SupportsBatchedEffective: true } && capabilities.Revision >= IntroducedInRevision;

    public ApiRequestContext CaptureContext() => settings.CaptureContext();
    public SettingsIdentityContext CaptureSettingsOwner() => settings.CaptureSettingsIdentityContext();
    public bool IsCurrentContext(ApiRequestContext context) => settings.IsCurrentContext(context);

    /// <summary>
    /// The effective value for this device (the request carries the device id, so a
    /// per-device value wins). An older server without the setting reports
    /// <see cref="AppLanguageRead.Supported"/> false and no value.
    /// </summary>
    public async Task<AppLanguageRead> ReadEffectiveAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var capabilities = await settings.GetContractCapabilitiesAsync(context, ct).ConfigureAwait(false);
        if (!IsSupported(capabilities)) return new AppLanguageRead(context, false, null, null);
        var values = await settings.GetContractEffectiveSettingsAsync(context, [SettingKey], ct: ct).ConfigureAwait(false);
        var entry = values.Settings.FirstOrDefault(item => item.Key == SettingKey);
        return new AppLanguageRead(context, true, TagOf(entry?.Value), entry?.Source);
    }

    /// <summary>What the picker shows: whether the server knows the setting, and the profile's own value.</summary>
    public async Task<(bool Supported, string? ProfileTag)> ReadPickerAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        var capabilities = await settings.GetContractCapabilitiesAsync(context, ct).ConfigureAwait(false);
        if (!IsSupported(capabilities)) return (false, null);
        return (true, await ReadProfileValueAsync(context, ct).ConfigureAwait(false));
    }

    /// <summary>The profile's own stored value (what the picker edits); null when none is stored.</summary>
    public async Task<string?> ReadProfileValueAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        try
        {
            var entry = await settings.GetContractStoredSettingAsync(context, SettingKey, "profile", ct: ct).ConfigureAwait(false);
            return TagOf(entry.StoredValue ?? entry.Value);
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { return null; }
    }

    /// <summary>
    /// Writes the picker's choice to the profile (null is "Match device") and then
    /// deletes this device's own value so the choice applies here too. The delete
    /// is skipped when the owner changed after the write; a failed delete is
    /// reported, never retried silently.
    /// </summary>
    public async Task<AppLanguageSaveResult> SaveAsync(ApiRequestContext context, string? tag, CancellationToken ct = default)
    {
        if (tag != null && AppLanguageMatcher.Canonicalize(tag) == null) throw new ArgumentException("Not a language tag.", nameof(tag));
        var owner = settings.CaptureSettingsIdentityContext();
        await settings.SetContractSettingValueAsync(context, SettingKey, "profile", tag, ct: ct).ConfigureAwait(false);
        settings.NotifyLocalizedSettingChanged(SettingKey);
        if (!settings.IsCurrentContext(context)) return new AppLanguageSaveResult(false);
        return await ClearDeviceValueAsync(owner, ct).ConfigureAwait(false);
    }

    /// <summary>Retry only the unfinished device step; never overwrite a newer profile choice.</summary>
    public async Task<AppLanguageSaveResult> ClearDeviceValueAsync(SettingsIdentityContext owner, CancellationToken ct = default)
    {
        if (settings.CaptureSettingsContext(owner) is not { } context) return new(false);
        try
        {
            await settings.DeleteContractSettingValueAsync(context.Request, SettingKey, "profile_device", deviceId: context.DeviceId, ct: ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new AppLanguageSaveResult(false);
        }
        return new AppLanguageSaveResult(true);
    }

    /// <summary>Confirm the device row is absent; read failures do not imply it was cleared.</summary>
    public async Task<bool> HasDeviceValueAsync(ApiRequestContext context, CancellationToken ct = default)
    {
        try
        {
            await settings.GetContractStoredSettingAsync(context, SettingKey, "profile_device", ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode == 404) { return false; }
    }

    /// <summary>Whether a realtime settings event changes the selected profile's app language.</summary>
    public static bool IsRealtimeChange(string channel, string eventName, JsonElement data, string? selectedProfileId)
    {
        if (channel != "user_settings" || eventName != "user_settings.changed" || data.ValueKind != JsonValueKind.Object) return false;
        if (!data.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String || key.GetString() != SettingKey) return false;
        return !(data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
                 && profile.GetString() is { Length: > 0 } changed && changed != selectedProfileId);
    }

    private static string? TagOf(JsonElement? value)
        => value is { ValueKind: JsonValueKind.String } element && element.GetString() is { Length: > 0 } tag ? tag : null;
}
