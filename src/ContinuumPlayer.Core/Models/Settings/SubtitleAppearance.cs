namespace ContinuumPlayer.Core.Models.Settings;

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
    /// <summary>"small" | "medium" | "large" | "xlarge"</summary>
    public string FontSize { get; set; } = "medium";

    /// <summary>"sans-serif" | "serif" | "monospace"</summary>
    public string FontFamily { get; set; } = "sans-serif";

    /// <summary>Hex color like "#ffffff".</summary>
    public string FontColor { get; set; } = "#ffffff";

    /// <summary>Hex color like "#000000".</summary>
    public string BackgroundColor { get; set; } = "#000000";

    /// <summary>"box" | "shadow" | "outline" | "none"</summary>
    public string BackgroundStyle { get; set; } = "box";

    /// <summary>Integer 0-100 (NOT a 0-1 float).</summary>
    public int BackgroundOpacity { get; set; } = 75;

    /// <summary>
    /// Whether a small text outline is drawn around subtitle glyphs for
    /// extra legibility. This is independent of <see cref="BackgroundStyle"/>.
    /// </summary>
    public bool TextOutline { get; set; } = false;

    /// <summary>"bottom" | "lower-third" | "top"</summary>
    public string Position { get; set; } = "bottom";
}
