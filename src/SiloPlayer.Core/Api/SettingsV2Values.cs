using System.Text.Json;

namespace SiloPlayer.Core.Api;

// Pinned to official Silo c80c5169f8e58f354fba35551e0c2bbcabb70b8a:
// contracts/settings/v1/manifest.json and internal/settingsmigrate/plan.go.
// Converts the desktop's string-based editor values at the v2 boundary.
internal static class SettingsV2Values
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["subtitle_appearance"] = "playback.subtitle_appearance",
        ["player.next_up_prompt_seconds"] = "playback.next_up_prompt_seconds",
        ["ui_theme"] = "ui.theme",
        ["ui_text_scale"] = "ui.text_scale",
        ["ui_text_weight"] = "ui.text_weight",
        ["ui_high_contrast"] = "ui.high_contrast",
        ["ui_custom_theme_vars"] = "ui.custom_theme_vars",
        ["ui_custom_css"] = "ui.custom_css",
        ["card_overlays"] = "ui.card_overlays",
        ["next_up_mode"] = "ui.next_up_mode",
        ["sidebar_pins"] = "ui.sidebar_pins",
        ["disabled_library_ids"] = "ui.disabled_library_ids",
        ["library_order"] = "ui.library_order",
    };

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["playback.audio_language"] = "language_tag",
        ["playback.subtitle_language"] = "language_tag",
        ["playback.subtitle_mode"] = "enum",
        ["playback.show_forced_subtitles"] = "boolean",
        ["playback.subtitle_appearance"] = "object",
        ["playback.preferred_quality"] = "enum",
        ["playback.max_bitrate_kbps"] = "integer",
        ["playback.auto_skip_intro"] = "boolean",
        ["playback.intro_skip_mode"] = "enum",
        ["playback.auto_skip_credits"] = "boolean",
        ["playback.auto_skip_recap"] = "boolean",
        ["playback.auto_play_next"] = "boolean",
        ["playback.auto_play_next_preview"] = "boolean",
        ["playback.next_up_prompt_seconds"] = "integer",
        ["catalog.metadata_language"] = "language_tag",
        ["catalog.metadata_language_overrides"] = "object",
        ["player.hdr_enabled"] = "boolean",
        ["player.dolby_vision_enabled"] = "boolean",
        ["player.dv_profile7_hdr10_fallback"] = "boolean",
        ["player.seek_cache_enabled"] = "boolean",
        ["player.match_frame_rate"] = "boolean",
        ["player.playback_speed"] = "number",
        ["player.audio_sync_ms"] = "integer",
        ["player.subtitle_sync_ms"] = "integer",
        ["player.video_gravity"] = "enum",
        ["player.orientation_mode"] = "enum",
        ["player.sleep_timer_default_minutes"] = "integer",
        ["ui.theme"] = "enum",
        ["ui.text_scale"] = "enum",
        ["ui.text_weight"] = "enum",
        ["ui.high_contrast"] = "boolean",
        ["ui.custom_theme_vars"] = "object",
        ["ui.custom_css"] = "string",
        ["ui.date_format"] = "enum",
        ["ui.time_format"] = "enum",
        ["ui.library_page_state"] = "object",
        ["ui.remember_library_page_state"] = "boolean",
        ["search.media_scope"] = "enum",
        ["ui.card_overlays"] = "object",
        ["ui.card_overlays_enabled"] = "boolean",
        ["ui.card_quick_actions"] = "enum",
        ["ui.card_quick_actions_enabled"] = "boolean",
        ["ui.next_up_mode"] = "enum",
        ["ui.title_art"] = "boolean",
        ["nav.primary_menu"] = "object",
        ["nav.shortcuts"] = "object",
        ["ui.card_presentation"] = "object",
        ["ui.sidebar_pins"] = "object",
        ["ui.disabled_library_ids"] = "object",
        ["ui.library_order"] = "object",
    };

    public static string CanonicalKey(string key) => Aliases.GetValueOrDefault(key, key);

    public static object? Parse(string key, string value)
    {
        var canonical = CanonicalKey(key);
        if (!Types.TryGetValue(canonical, out var type))
            throw new ArgumentException($"The server settings contract does not define remote setting '{key}'.", nameof(key));
        if (type is "string" or "enum" or "language_tag") return value;
        // These nullable document editors historically use an empty string for reset.
        if (type == "object" && string.IsNullOrWhiteSpace(value)) return null;
        var parsed = JsonSerializer.Deserialize<JsonElement>(value);
        var valid = type switch
        {
            "boolean" => parsed.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null,
            "integer" => parsed.ValueKind == JsonValueKind.Null || parsed.ValueKind == JsonValueKind.Number && parsed.TryGetInt64(out _),
            "number" => parsed.ValueKind is JsonValueKind.Number or JsonValueKind.Null,
            "object" => parsed.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null,
            _ => false,
        };
        if (!valid) throw new ArgumentException($"Setting '{key}' requires a {type} value.", nameof(value));
        return parsed;
    }

    public static string Display(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => value.GetRawText(),
    };
}
