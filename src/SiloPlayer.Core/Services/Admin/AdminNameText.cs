using System.Globalization;
using System.Text;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// How the account-resource pages show names that someone else wrote (user, profile, group, device and server names): the first
/// character of an avatar, and a bounded plain-text form for confirmations and dialog titles. Official <c>web/src/lib/text.ts</c>
/// (<c>getInitial</c>, <c>leadingGraphemes</c>) at a84b2551b2.
/// </summary>
public static class AdminNameText
{
    /// <summary>The longest name a confirmation or dialog title carries before it is cut with an ellipsis.</summary>
    public const int MaxLabelElements = 80;

    /// <summary>
    /// The first <paramref name="count"/> characters as a reader sees them: an emoji (including a joined one or a flag), or a letter with its
    /// combining marks, counts as one and is never cut in half, which a UTF-16 index does to anything outside the Basic Multilingual Plane.
    /// </summary>
    public static string LeadingGraphemes(string? text, int count)
    {
        if (string.IsNullOrEmpty(text) || count <= 0) return "";
        var builder = new StringBuilder(); var taken = 0; var elements = StringInfo.GetTextElementEnumerator(text);
        while (taken < count && elements.MoveNext()) { builder.Append(elements.GetTextElement()); taken++; }
        return builder.ToString();
    }

    /// <summary>
    /// The avatar initial: the name's first character, upper-cased by the interface language's rules (Turkish "i" becomes "İ", and "ß" becomes
    /// "SS" as JavaScript's toLocaleUpperCase does), or "" when the name has none. Callers show "?" for "".
    /// </summary>
    public static string Initial(string? name, CultureInfo? culture = null)
    {
        var first = LeadingGraphemes(name?.Trim(), 1);
        if (first.Length == 0) return "";
        culture ??= Culture();
        return FullUpper(first.ToUpper(culture), first);
    }

    private static CultureInfo Culture()
    {
        try { return CultureInfo.GetCultureInfo(AppStrings.Language); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }

    // .NET upper-cases one UTF-16 unit at a time; a few letters expand to more than one in the full mapping JavaScript uses.
    private static string FullUpper(string upper, string original)
    {
        if (original.Length != 1 || upper != original) return upper;
        return original[0] switch
        {
            'ß' => "SS", 'ŉ' => "ʼN", 'ǰ' => "J̌", 'ﬀ' => "FF", 'ﬁ' => "FI", 'ﬂ' => "FL", 'ﬃ' => "FFI", 'ﬄ' => "FFL", 'ﬅ' or 'ﬆ' => "ST",
            _ => upper,
        };
    }

    /// <summary>
    /// Text as a web page lays it out in one line: each line break (CR, LF, NEL, a vertical tab or form feed, the line and paragraph separators)
    /// reads as a space, because HTML collapses them, whereas a native text block starts a new line at each. Nothing else changes, so a name
    /// keeps its own characters and direction in a list that cuts it with an ellipsis.
    /// </summary>
    public static string SingleLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == (char)0x0D && i + 1 < text.Length && text[i + 1] == (char)0x0A) i++;   // CR LF is one break
            builder.Append(ch is (char)0x0A or (char)0x0D or (char)0x0B or (char)0x0C or (char)0x85 or (char)0x2028 or (char)0x2029 ? ' ' : ch);
        }
        return builder.ToString();
    }

    /// <summary>
    /// A name as plain, single-line text of bounded length, for a confirmation, a dialog title or an accessible name. Control characters, line and
    /// paragraph separators, the invisible zero-width characters and the characters that change text direction (marks, embeddings, overrides, isolates) become a space, so a name
    /// cannot start a new line, hide text or flip what follows it; runs of white space collapse; a name longer than
    /// <paramref name="max"/> characters is cut at a whole character and ends with an ellipsis. The joiners that emoji and some scripts need stay.
    /// </summary>
    public static string Label(string? text, int max = MaxLabelElements)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = SpaceForInvisible(text);
        var builder = new StringBuilder(text.Length); var pendingSpace = false;
        foreach (var element in Elements(text))
        {
            if (IsNeutralized(element)) { pendingSpace = builder.Length > 0; continue; }
            if (IsWhiteSpace(element)) { pendingSpace = builder.Length > 0; continue; }
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(element);
        }
        var clean = builder.ToString();
        // Joiners only join: a name made of nothing else shows nothing, so it is empty (the page then says "unnamed").
        if (clean.All(ch => ch is (char)0x200C or (char)0x200D)) return "";
        if (max <= 0 || ElementCount(clean) <= max) return clean;
        return LeadingGraphemes(clean, max).TrimEnd() + "…";
    }

    /// <summary>
    /// Whether an email address cut short with an ellipsis should lay out left to right (official <c>emailDir</c>, web/src/lib/direction.ts). True unless
    /// the first character that sets direction in the part before the last "@" is from a right-to-left script; then the address keeps the page's
    /// direction. A Latin address keeps its start on a right-to-left page, and one written in Arabic keeps its own start there too. A name part with
    /// no such character reads left to right, unless it holds Arabic-Indic digits, which follow the domain instead.
    /// </summary>
    public static bool EmailIsLeftToRight(string? email)
    {
        if (string.IsNullOrEmpty(email)) return true;
        var at = email.LastIndexOf('@'); var name = at > 0 ? email[..at] : email;
        var first = FirstStrong(name);
        if (first == null && at > 0 && name.EnumerateRunes().Any(rune => IsArabicNumber(rune.Value))) first = FirstStrong(email[(at + 1)..]);
        return first is not { } strong || !IsRightToLeft(strong.Value);
    }

    private static Rune? FirstStrong(string text)
    {
        foreach (var rune in text.EnumerateRunes()) if (IsStrong(rune)) return rune;
        return null;
    }

    // Letters, the Hebrew maqaf, paseq, sof pasuq, nun hafukha, geresh and gershayim, and the N'Ko, Garay and Adlam digits set a line's direction; other digits and punctuation do not.
    private static bool IsStrong(Rune rune)
    {
        var v = rune.Value;
        return Rune.IsLetter(rune) || v is 0x05BE or 0x05C0 or 0x05C3 or 0x05C6 or 0x05F3 or 0x05F4 or (>= 0x07C0 and <= 0x07C9) or (>= 0x10D40 and <= 0x10D49) or (>= 0x1E950 and <= 0x1E959);
    }

    // The right-to-left scripts: U+0590 to U+08FF, their presentation forms, and the right-to-left blocks of the supplementary planes.
    private static bool IsRightToLeft(int v) => v is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF) or (>= 0x10800 and <= 0x10FFF) or (>= 0x1E800 and <= 0x1EFFF);

    // Arabic-Indic, Hanifi Rohingya and Rumi digits (bidi class AN), which join the right-to-left text around them on a left-to-right line.
    private static bool IsArabicNumber(int v) => v is (>= 0x0660 and <= 0x0669) or (>= 0x10D30 and <= 0x10D39) or (>= 0x10E60 and <= 0x10E7E);

    private static IEnumerable<string> Elements(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext()) yield return elements.GetTextElement();
    }

    private static int ElementCount(string text) => new StringInfo(text).LengthInTextElements;

    private static bool IsWhiteSpace(string element) => element.Length > 0 && element.All(char.IsWhiteSpace);

    /// <summary>
    /// Characters that draw nothing yet are not white space, so a name made of them looks empty or hides what a reader sees: the Hangul fillers
    /// (U+115F, U+1160, U+3164, U+FFA0), the blank braille pattern (U+2800), the Mongolian vowel separator (U+180E), the soft hyphen (U+00AD), the
    /// combining grapheme joiner (U+034F) and the tag characters (U+E0000 to U+E007F, which can carry a hidden message).
    /// </summary>
    internal static bool IsInvisible(Rune rune) =>
        rune.Value is 0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x2800 or 0x180E or 0x00AD or 0x034F or (>= 0xE0000 and <= 0xE007F);

    /// <summary>
    /// The text with each invisible character (<see cref="IsInvisible"/>) read as a space, as the other invisible characters are, so it separates what is around it
    /// instead of hiding or joining it, even when it is attached to the letter before it (a combining mark, a tag character). Character by character: a lone
    /// surrogate is copied as it is and never throws.
    /// </summary>
    private static string SpaceForInvisible(string text)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < text.Length;)
        {
            var valid = Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var used) == System.Buffers.OperationStatus.Done;
            if (!valid) used = 1;
            if (valid && IsInvisible(rune)) { builder ??= new StringBuilder(text.Length).Append(text, 0, i); builder.Append(' '); }
            else builder?.Append(text, i, used);
            i += used;
        }
        return builder?.ToString() ?? text;
    }

    private static bool IsNeutralized(string element)
    {
        foreach (var ch in element)
        {
            if (char.IsControl(ch)) return true;
            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) return true;
            // Invisible characters that take no room and can hide text or pad a name to look empty: the zero width space (U+200B), the word joiner and the
            // invisible operators (U+2060 to U+2064) and the byte order mark / zero width no-break space (U+FEFF). The zero width non-joiner and joiner
            // (U+200C, U+200D) stay: emoji sequences and some scripts (Persian, Indic) need them.
            if (ch is (char)0x200B or (>= (char)0x2060 and <= (char)0x2064) or (char)0xFEFF) return true;
            // Direction marks (LRM, RLM, ALM), embeddings and overrides (LRE..RLO, PDF) and isolates (LRI..PDI).
            if (ch is (char)0x200E or (char)0x200F or (char)0x061C or (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069)) return true;
        }
        return false;
    }
}
