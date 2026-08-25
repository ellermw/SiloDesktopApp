using System.Text.Json;

namespace SiloPlayer.Core.Models.Settings;

/// <summary>
/// Shape of the persisted <c>subtitle_appearance</c> user setting. Matches
/// the webui <c>SubtitleAppearance</c> type in
/// <c>web/src/lib/subtitleAppearance.ts</c> so the same JSON is readable
/// from both clients (B55).
///
/// Field names use camelCase in both C# property form and in the persisted
/// JSON payload (see <c>SettingsViewModel.SaveSubtitleAppearance</c> — we
/// emit explicit camelCase keys instead of relying on a serializer policy
/// because the value is stored as a free-form JSON string on the server).
/// </summary>
public class SubtitleAppearance
{
    /// <summary>"small" | "medium" | "large" | "xlarge" | "xxlarge"</summary>
    public string FontSize { get; set; } = "large";

    /// <summary>"sans-serif" | "serif" | "monospace"</summary>
    public string FontFamily { get; set; } = "sans-serif";

    /// <summary>Hex color like "#ffffff".</summary>
    public string FontColor { get; set; } = "#ffffff";

    /// <summary>Hex color like "#000000".</summary>
    public string BackgroundColor { get; set; } = "#000000";

    /// <summary>"box" | "shadow" | "outline" | "none"</summary>
    public string BackgroundStyle { get; set; } = "shadow";

    /// <summary>Integer 0-100 (NOT a 0-1 float).</summary>
    public int BackgroundOpacity { get; set; } = 75;

    /// <summary>
    /// Whether a small text outline is drawn around subtitle glyphs for
    /// extra legibility. This is independent of <see cref="BackgroundStyle"/>.
    /// </summary>
    public bool TextOutline { get; set; } = false;

    /// <summary>Hex color used by both outline treatments.</summary>
    public string TextOutlineColor { get; set; } = "#000000";

    /// <summary>"bottom" | "lower-third" | "top"</summary>
    public string Position { get; set; } = "bottom";

    public SubtitleAppearance Clone() => new()
    {
        FontSize = FontSize,
        FontFamily = FontFamily,
        FontColor = FontColor,
        BackgroundColor = BackgroundColor,
        BackgroundStyle = BackgroundStyle,
        BackgroundOpacity = BackgroundOpacity,
        TextOutline = TextOutline,
        TextOutlineColor = TextOutlineColor,
        Position = Position,
    };

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["fontSize"] = FontSize,
        ["fontFamily"] = FontFamily,
        ["fontColor"] = FontColor,
        ["backgroundColor"] = BackgroundColor,
        ["backgroundStyle"] = BackgroundStyle,
        ["backgroundOpacity"] = BackgroundOpacity,
        ["textOutline"] = TextOutline,
        ["textOutlineColor"] = TextOutlineColor,
        ["position"] = Position,
    });

    public static SubtitleAppearance Parse(string? json)
    {
        var result = new SubtitleAppearance();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            result.FontSize = ValidString(root, "fontSize", ["small", "medium", "large", "xlarge", "xxlarge"], result.FontSize);
            result.FontFamily = ValidString(root, "fontFamily", ["sans-serif", "serif", "monospace"], result.FontFamily);
            result.FontColor = ValidColor(root, "fontColor", result.FontColor);
            result.BackgroundColor = ValidColor(root, "backgroundColor", result.BackgroundColor);
            result.BackgroundStyle = ValidString(root, "backgroundStyle", ["box", "shadow", "outline", "none"], result.BackgroundStyle);
            if (root.TryGetProperty("backgroundOpacity", out var opacity) && opacity.TryGetInt32(out var value) && value is >= 0 and <= 100)
                result.BackgroundOpacity = value;
            if (root.TryGetProperty("textOutline", out var outline) && outline.ValueKind is JsonValueKind.True or JsonValueKind.False)
                result.TextOutline = outline.GetBoolean();
            result.TextOutlineColor = ValidColor(root, "textOutlineColor", result.TextOutlineColor);
            result.Position = ValidString(root, "position", ["bottom", "lower-third", "top"], result.Position);
        }
        catch (JsonException) { }

        return result;
    }

    private static string ValidString(JsonElement root, string property, string[] allowed, string fallback)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return fallback;
        var text = value.GetString();
        return text != null && allowed.Contains(text, StringComparer.Ordinal) ? text : fallback;
    }

    private static string ValidColor(JsonElement root, string property, string fallback)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return fallback;
        var text = value.GetString();
        return text is { Length: 7 } && text[0] == '#' && text[1..].All(Uri.IsHexDigit) ? text : fallback;
    }
}
