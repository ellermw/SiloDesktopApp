using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services;

public static class AudioTrackDisplayText
{
    /// <summary>Source audio-track bitrates arrive from Silo in kilobits per second; shown in the display culture.</summary>
    public static string FormatBitrate(int? kbps) => kbps is > 0
        ? AppStrings.Get("web.common.format.bitrate.kbps",
            new Dictionary<string, object?> { ["rate"] = kbps.Value.ToString("0.#", AppStrings.FormatCulture) },
            fallback: kbps.Value.ToString("0.#", AppStrings.FormatCulture) + " kbps")
        : "";

    /// <summary>
    /// A media language code's name in the interface language (the catalog's
    /// <c>language.*</c> names), or <paramref name="english"/> when the catalog
    /// has none. Codes stay data; only the displayed name changes.
    /// </summary>
    public static string LanguageName(string? code, string english)
    {
        if (string.IsNullOrWhiteSpace(code)) return english;
        var normalized = MediaLanguageCatalog.Normalize(code);
        return normalized.Length is 2 or 3 && normalized.All(char.IsAsciiLetterLower)
            ? AppStrings.Get("language." + normalized, fallback: english)
            : english;
    }
}
