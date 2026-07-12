namespace SiloPlayer.Core.Helpers;

/// <summary>
/// Single source of truth for the user "max playback quality" preset/value
/// translation. WebUI exposes three preset radio buttons (Any / Standard / 4K)
/// that map to canonical resolution strings on the wire ("" / "1080p" / "2160p").
///
/// B54: Replaces duplicate copies of these methods previously inlined in
/// AdminUserDetailPage and AdminUsersPage.
/// </summary>
public static class PlaybackQuality
{
    /// <summary>
    /// The three presets users can pick in the admin UI, each with a label and
    /// description shown beneath the dropdown.
    /// </summary>
    public static readonly (string Value, string Label, string Description)[] Options =
    [
        ("any", "Any", "Allow all resolutions"),
        ("standard", "Standard", "Hide 4K and higher versions"),
        ("4k", "4K", "Allow 4K and lower versions"),
    ];

    /// <summary>
    /// Normalize a server-side quality string to its canonical form.
    /// Empty/unrecognized → ""; standard/480p/720p/1080p → "1080p"; 4k/uhd/2160p/4320p → "2160p".
    /// </summary>
    public static string Canonical(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        return v switch
        {
            "" or "any" => "",
            "standard" or "480p" or "720p" or "1080p" => "1080p",
            "4k" or "uhd" or "2160p" or "4320p" => "2160p",
            _ => "",
        };
    }

    /// <summary>Map a server-side quality value to the corresponding admin UI preset key.</summary>
    public static string PresetFromValue(string? value)
    {
        var canonical = Canonical(value);
        return canonical switch
        {
            "2160p" => "4k",
            "1080p" => "standard",
            _ => "any",
        };
    }

    /// <summary>Map an admin UI preset key back to the server-side quality value.</summary>
    public static string ValueFromPreset(string preset)
    {
        return preset switch
        {
            "standard" => "1080p",
            "4k" => "2160p",
            _ => "",
        };
    }

    /// <summary>Pretty label ("Any" / "Standard" / "4K") for displaying a server quality value.</summary>
    public static string FormatPreset(string? value)
    {
        var preset = PresetFromValue(value);
        return preset switch
        {
            "standard" => "Standard",
            "4k" => "4K",
            _ => "Any",
        };
    }
}
