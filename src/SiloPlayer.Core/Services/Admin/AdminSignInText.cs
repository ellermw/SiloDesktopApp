using System.Globalization;
using System.Text;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How a text control lays its text out (official <c>textDirection</c>): not set, left to right, right to left, or from the first letter.</summary>
public enum AdminSignInDirection { None, LeftToRight, RightToLeft, Auto }

/// <summary>
/// Plain-text and direction rules for the Sign-in page. Provider names, issuer URLs, account names and server messages are third-party text: shown as
/// plain text only, and neutralized and bounded wherever they sit in a confirmation or a dialog title.
/// </summary>
public static class AdminSignInText
{
    // Written as numbers so the invisible characters are visible in the source.
    private const char Zwj = (char)0x200D, Zwnj = (char)0x200C, Replacement = (char)0xFFFD;

    /// <summary>
    /// Third-party text for a title, a sentence or a label: control characters, line and paragraph separators and white space become single spaces;
    /// invisible fillers and direction characters (marks, embeddings, overrides, isolates) are removed; zero-width joiners stay only between two
    /// characters they join (an emoji sequence, a Persian word), never first, last or beside a space; the result is trimmed and bounded to
    /// <paramref name="maxLength"/> text elements, with an ellipsis when cut.
    /// </summary>
    public static string Plain(string? value, int maxLength = 80)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0) return "";
        var cleaned = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var v = rune.Value;
            if (IsDirectionControl(v) || IsInvisibleFiller(v)) continue;
            if (v is Zwj or Zwnj) { cleaned.Append((char)v); continue; }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator) { cleaned.Append(' '); continue; }
            if (category is UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate) { cleaned.Append(Replacement); continue; }
            cleaned.Append(rune.ToString());
        }
        // A joiner stays only between two characters that are not spaces; runs of spaces collapse.
        var joined = new StringBuilder(cleaned.Length);
        var text = cleaned.ToString();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == ' ')
            {
                if (joined.Length > 0 && joined[^1] != ' ') joined.Append(' ');
                continue;
            }
            if (ch is Zwj or Zwnj)
            {
                var next = i + 1 < text.Length ? text[i + 1] : ' ';
                if (joined.Length > 0 && joined[^1] != ' ' && joined[^1] is not (Zwj or Zwnj) && next is not (' ' or Zwj or Zwnj)) joined.Append(ch);
                continue;
            }
            joined.Append(ch);
        }
        var result = joined.ToString().Trim();
        var info = new StringInfo(result);
        return info.LengthInTextElements <= maxLength ? result : info.SubstringByTextElements(0, Math.Max(0, maxLength - 1)).TrimEnd() + "…";
    }

    private static bool IsDirectionControl(int rune) =>
        rune is 0x200E or 0x200F or 0x061C or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);

    private static bool IsInvisibleFiller(int rune) =>
        rune is 0x00AD or 0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or (>= 0x180B and <= 0x180F) or 0x200B or (>= 0x2060 and <= 0x2064) or 0x3164 or 0xFEFF or 0xFFA0 or 0x2800
            or (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0000 and <= 0xE007F) or (>= 0xE0100 and <= 0xE01EF);

    /// <summary>Whether the first character that sets a direction is a right-to-left letter or mark (official <c>startsRightToLeft</c>); digits, spaces and punctuation do not count.</summary>
    public static bool StartsRightToLeft(string? text)
    {
        foreach (var rune in (text ?? "").EnumerateRunes())
        {
            var v = rune.Value;
            if (v is 0x200F or 0x061C) return true;
            if (v == 0x200E) return false;
            if (!Rune.IsLetter(rune)) continue;
            return v is (>= 0x0590 and <= 0x08FF) or (>= 0xFB1D and <= 0xFDFF) or (>= 0xFE70 and <= 0xFEFF) or (>= 0x10800 and <= 0x10FFF) or (>= 0x1E800 and <= 0x1EFFF);
        }
        return false;
    }

    /// <summary>
    /// The direction of a sign-in setup control (official <c>textDirection</c>): none on a left-to-right page; on a right-to-left page an empty control takes its
    /// placeholder's direction, a masked or URL control is always left to right, and other text follows its first letter.
    /// </summary>
    public static AdminSignInDirection DirectionFor(bool pageRightToLeft, string? text, string? placeholder, bool alwaysLeftToRight)
    {
        if (!pageRightToLeft) return AdminSignInDirection.None;
        if (string.IsNullOrEmpty(text)) return StartsRightToLeft(placeholder) ? AdminSignInDirection.RightToLeft : AdminSignInDirection.LeftToRight;
        return alwaysLeftToRight ? AdminSignInDirection.LeftToRight : AdminSignInDirection.Auto;
    }
}
