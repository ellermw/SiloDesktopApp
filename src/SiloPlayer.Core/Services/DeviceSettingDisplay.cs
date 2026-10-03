using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>Device definitions from official Silo manifest 8e2e840. Render metadata is never a write authority.</summary>
public sealed record DeviceSettingDefinition(JsonElement Data)
{
    public string Key => Data.GetProperty("key").GetString()!;
    public string Label => Data.GetProperty("label").GetString()!;
    public string Description => Data.GetProperty("description").GetString() ?? "";
    public string Type => Schema.GetProperty("type").GetString()!;
    public string Control => Data.TryGetProperty("recommended_control", out var control) ? control.GetString()! : Type == "boolean" ? "switch" : "text";
    public JsonElement Schema => Data.GetProperty("value_schema");
    public string Unit => Data.TryGetProperty("unit", out var unit) ? unit.GetString()! : "";
    public double Minimum => Schema.TryGetProperty("minimum", out var value) ? value.GetDouble() : 0;
    public double Maximum => Schema.TryGetProperty("maximum", out var value) ? value.GetDouble() : 100;
    public double Step => Schema.TryGetProperty("step", out var value) ? value.GetDouble() : Type == "integer" ? 1 : .05;
    public bool ProfileFirst => Data.TryGetProperty("resolution_order", out var order) && order.GetArrayLength() > 0 && order[0].GetString() == "profile";
    public string Default => Scalar(Data.GetProperty("default_value"));
    public static string Scalar(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    public (string Value, string Label)[] Options => Schema.TryGetProperty("values", out var values)
        ? values.EnumerateArray().Select(value => (Scalar(value.GetProperty("value")), value.TryGetProperty("label", out var label) ? label.GetString()! : Scalar(value.GetProperty("value")))).ToArray() : [];
    public string Group => Key.Contains("subtitle") || Key == "playback.show_forced_subtitles" ? "Subtitles"
        : Key.Contains("audio") || Key == "player.playback_speed" || Key.StartsWith("ui.theme_music") ? "Sound"
        : Key.Contains("skip") || Key.Contains("auto_play") || Key.Contains("next_up") || Key.Contains("sleep_timer") ? "Episodes" : "Picture";
    public bool AppliesTo(string platform)
    {
        var p = platform.ToLowerInvariant();
        var mapped = p.Contains("web") || p.Contains("browser") ? "web"
            : p.Contains("android-tv") || p.Contains("android_tv") || p.Contains("android tv") ? "android_tv"
            : p.Contains("tvos") || p.Contains("apple tv") ? "tvos"
            : p.Contains("ios") || p.Contains("iphone") || p.Contains("ipad") ? "ios"
            : p.Contains("macos") || p.Contains("mac os") ? "macos"
            : p.Contains("android") ? "android" : null;
        return mapped == null || !Data.TryGetProperty("platforms", out var platforms) || platforms.GetArrayLength() == 0
            || platforms.EnumerateArray().Any(value => value.GetString() == mapped);
    }
}

public static class DeviceSettingDisplay
{
    private static readonly HashSet<string> Hidden = ["ui.theme", "ui.text_scale", "ui.text_weight", "ui.high_contrast", "ui.library_page_state", "ui.remember_library_page_state", "nav.primary_menu", "ui.card_presentation"];
    private static readonly IReadOnlyList<DeviceSettingDefinition> Definitions = Load();
    private static IReadOnlyList<DeviceSettingDefinition> Load()
    {
        using var stream = typeof(DeviceSettingDisplay).Assembly.GetManifestResourceStream("SiloPlayer.Core.Models.Settings.device-settings.json")!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.EnumerateArray().Select(data => new DeviceSettingDefinition(data.Clone())).ToArray();
    }
    public static IReadOnlyList<DeviceSettingDefinition> ForRevision(int revision) => Definitions.Where(definition =>
        !Hidden.Contains(definition.Key) && definition.Data.GetProperty("introduced_in").GetInt32() <= revision
        && !(definition.Key == "playback.auto_skip_intro" && revision >= 7)).ToArray();
    public static bool CanWrite(ContractEffectiveSettingEntry entry) => entry.ConstraintKind != "locked";
    public static bool IsDormant(UserDevice device, DateTimeOffset now) => !device.IsCurrentDevice && device.ChangedCount == 0
        && (!DateTimeOffset.TryParse(device.LastSeenAt, out var seen) || now - seen > TimeSpan.FromDays(90));
}
