using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The stored values of the five appearance preferences (text size, text weight, high contrast, date format, time format),
/// normalized the way the Settings page shows them. A key an older server does not return reads as its default.
/// </summary>
public static class AppearancePreferenceValues
{
    public static readonly string[] AccessibilityKeys = ["ui_text_scale", "ui_text_weight", "ui_high_contrast"];

    public static string TextScale(string? value) => value is "large" or "x-large" ? value : "default";

    public static string TextWeight(string? value) => value == "strong" ? "strong" : "default";

    public static bool HighContrast(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public static string DateFormat(string? value) => value is "DD/MM/YYYY" or "MM/DD/YYYY" or "YYYY-MM-DD" ? value : "auto";

    public static string TimeFormat(string? value) => value is "12h" or "24h" ? value : "auto";

    /// <summary>Text size, text weight and high contrast from an effective-settings read.</summary>
    public static (string TextScale, string TextWeight, bool HighContrast) Accessibility(EffectiveSettingsResponse response)
    {
        string? Read(string key) => response.Settings.FirstOrDefault(entry => entry.Key == key)?.EffectiveValue;
        return (TextScale(Read("ui_text_scale")), TextWeight(Read("ui_text_weight")), HighContrast(Read("ui_high_contrast")));
    }
}
