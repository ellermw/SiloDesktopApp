using System.Globalization;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Viewer-facing marker labels shared by the native and OSC surfaces.</summary>
public static class MarkerProvenance
{
    public static string? Label(TimeRange? range, bool showConfidence, bool showSource)
    {
        if (range is null) return null;
        var parts = new List<string>();
        if (showConfidence && range.Confidence is { } confidence && double.IsFinite(confidence))
            parts.Add(Math.Floor(Math.Clamp(confidence, 0, 1) * 100 + .5).ToString(CultureInfo.InvariantCulture) + "%");
        if (showSource && SourceLabel(range) is { Length: > 0 } source) parts.Add(source);
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string? SourceLabel(TimeRange range) => range.Source switch
    {
        "scanner" => "Detected",
        "manual" => "Edited",
        "plugin" or "online" => ProviderName(range.Provider),
        _ => string.IsNullOrEmpty(range.Provider) ? null : ProviderName(range.Provider),
    };

    private static string ProviderName(string? provider)
    {
        var name = provider?.Trim();
        if (string.IsNullOrEmpty(name)) return "Online";
        var qualified = Regex.Match(name, @"^plugin:[0-9]+:(.+)$", RegexOptions.CultureInvariant);
        if (qualified.Success) name = qualified.Groups[1].Value;
        return name switch { "introdb" => "TheIntroDB", "built_in" => "Detected", _ => name };
    }
}
