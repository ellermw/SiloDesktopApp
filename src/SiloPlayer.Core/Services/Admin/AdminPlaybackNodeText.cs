using System.Globalization;
using System.Text;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Third-party text of the Nodes page (node names, hosts, URLs, server messages) is plain text.
/// These helpers neutralize what could reorder, hide or split it, and compose the one value the page copies.
/// </summary>
public static class AdminPlaybackNodeText
{
    public const int DefaultLimit = 200;

    /// <summary>Direction marks, embeddings, overrides and isolates: they reorder text and are invisible, so a copy would carry them.</summary>
    public static bool IsDirectionCharacter(int value) =>
        value is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    /// <summary>
    /// One line of bounded plain text: control characters, line and paragraph separators and spaces collapse to one space,
    /// direction characters are removed, and the result is cut at <paramref name="limit"/> characters with an ellipsis.
    /// </summary>
    public static string Plain(string? value, int limit = DefaultLimit)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(Math.Min(value.Length, limit + 1)); var pendingSpace = false; var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (IsDirectionCharacter(rune.Value)) continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator || rune.Value == 0x85)
            {
                pendingSpace = builder.Length > 0; continue;
            }
            if (count >= limit) { builder.Append('…'); return builder.ToString(); }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; count++; if (count >= limit) { builder.Append('…'); return builder.ToString(); } }
            builder.Append(rune.ToString()); count++;
        }
        return builder.ToString();
    }

    /// <summary>True when the text holds a control, format or direction character that a copy would carry without showing it.</summary>
    public static bool HasInvisible(string value)
    {
        foreach (var rune in value.EnumerateRunes())
            if (IsDirectionCharacter(rune.Value) || Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) return true;
        return false;
    }

    /// <summary>
    /// The node's two startup settings exactly as the admin sees them (the core URL without trailing slashes, then the key).
    /// Null when the core address is not a plain HTTP(S) address or either value carries an invisible character: nothing is copied then.
    /// </summary>
    public static string? KeySettings(string coreUrl, string key)
    {
        var url = (coreUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0 || string.IsNullOrEmpty(key) || HasInvisible(url) || HasInvisible(key) || !AdminPlaybackNodeEditor.ValidUrl(url)) return null;
        return "SILO_CORE_URL=" + url + "\nSILO_NODE_KEY=" + key;
    }

    /// <summary>A refusal's reason: the server's own words (bounded, plain, credential-redacted), or <paramref name="fallback"/> when it sent none.</summary>
    public static string Reason(ApiException error, string fallback)
    {
        var detail = error.FieldErrors.Select(field => field.Detail).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? error.ProblemDetail ?? error.ProblemTitle;
        if (string.IsNullOrWhiteSpace(detail)) return fallback;
        var plain = Plain(AdminBackupValidation.SafeServerText(detail), 300);
        return plain.Length == 0 ? fallback : plain;
    }
}
