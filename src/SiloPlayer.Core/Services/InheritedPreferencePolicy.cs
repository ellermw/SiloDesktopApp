using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

public readonly record struct InheritedPreferenceChange(string Key, bool NeedsSettlingRead);

/// <summary>Server-default inheritance for title art and Home preferences.</summary>
public sealed class InheritedPreferencePolicy(SettingsApi api)
{
    public static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromSeconds(11);
    public static bool IsSupportedKey(string? key) => key is "ui.title_art" or "ui.next_up_mode" or "ui.next_up_new_releases_first";

    public async Task ResetAsync(ApiRequestContext context, string key, string source, CancellationToken ct = default)
    {
        if (!IsSupportedKey(key)) throw new ArgumentOutOfRangeException(nameof(key));
        var scopes = key switch
        {
            "ui.title_art" when source == "profile" => new[] { "profile_device", "profile" },
            "ui.title_art" => ["profile_device"],
            "ui.next_up_mode" => ["profile", "profile_device"],
            _ => ["profile"],
        };
        foreach (var scope in scopes)
        {
            ct.ThrowIfCancellationRequested();
            if (!api.IsCurrentContext(context)) throw new OperationCanceledException("Preference owner changed.", ct);
            try { await api.DeleteContractSettingValueAsync(context, key, scope, ct: ct); }
            catch (ApiException ex) when (ex.StatusCode == 404) { }
        }
        ct.ThrowIfCancellationRequested();
        if (!api.IsCurrentContext(context)) throw new OperationCanceledException("Preference owner changed.", ct);
    }

    public static bool TryReadRealtimeChange(string channel, string eventName, JsonElement data, string? selectedProfileId,
        out InheritedPreferenceChange change)
    {
        change = default;
        if (channel != "user_settings" || eventName != "user_settings.changed" || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("key", out var keyValue) || keyValue.ValueKind != JsonValueKind.String
            || !IsSupportedKey(keyValue.GetString())) return false;
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(profile.GetString()) && profile.GetString() != selectedProfileId) return false;
        var isDefault = data.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.String && scope.GetString() == "default";
        change = new(keyValue.GetString()!, isDefault);
        return true;
    }
}
