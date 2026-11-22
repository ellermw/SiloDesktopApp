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

    /// <summary>Why the startup settings (or the key alone) cannot be copied; <see cref="None"/> when they can.</summary>
    public enum CopyRefusal { None, AddressMissing, AddressInvisible, AddressNotPlain, KeyMissing, KeyInvisible }

    /// <summary>
    /// Whether the key and the core address may be copied as they are shown. A copy carries no invisible character and the address is a plain HTTP(S) address
    /// (no user name, password, query or fragment). The key is checked first: a key that holds an invisible character cannot be copied by itself either.
    /// </summary>
    public static CopyRefusal CheckCopy(string coreUrl, string key, bool addressNeeded = true)
    {
        if (string.IsNullOrEmpty(key)) return CopyRefusal.KeyMissing;
        if (HasInvisible(key)) return CopyRefusal.KeyInvisible;
        if (!addressNeeded) return CopyRefusal.None;
        var url = (coreUrl ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) return CopyRefusal.AddressMissing;
        if (HasInvisible(url)) return CopyRefusal.AddressInvisible;
        return AdminPlaybackNodeEditor.ValidUrl(url) ? CopyRefusal.None : CopyRefusal.AddressNotPlain;
    }

    /// <summary>
    /// The node's two startup settings exactly as the admin sees them (the core URL without trailing slashes, then the key).
    /// Null when <see cref="CheckCopy"/> refuses either value: nothing is copied then.
    /// </summary>
    public static string? KeySettings(string coreUrl, string key)
        => CheckCopy(coreUrl, key) != CopyRefusal.None ? null : "SILO_CORE_URL=" + (coreUrl ?? "").Trim().TrimEnd('/') + "\nSILO_NODE_KEY=" + key;

    /// <summary>Removes format characters (zero-width and similar marks): invisible on screen, but they can split a URL or token so that a redaction pattern no longer matches it.</summary>
    private static string WithoutFormatCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes()) if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) builder.Append(rune.ToString());
        return builder.ToString();
    }

    /// <summary>
    /// A refusal's reason: the server's own words (plain, credential-redacted, at most 300 characters), or <paramref name="fallback"/> when it sent none.
    /// The text is cleaned first and redacted second: a direction mark inside a URL or a control character inside a token would otherwise hide it from the redaction
    /// and be removed afterwards, leaving the joined credential on screen. The cut to 300 comes last, so a credential that straddles it was already redacted.
    /// </summary>
    public static string Reason(ApiException error, string fallback)
    {
        var detail = error.FieldErrors.Select(field => field.Detail).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? error.ProblemDetail ?? error.ProblemTitle;
        if (string.IsNullOrWhiteSpace(detail)) return fallback;
        var clean = Plain(WithoutFormatCharacters(detail), 2000);
        if (clean.Length == 0) return fallback;
        var plain = Plain(AdminBackupValidation.SafeServerText(clean), 300);
        return plain.Length == 0 ? fallback : plain;
    }
}
