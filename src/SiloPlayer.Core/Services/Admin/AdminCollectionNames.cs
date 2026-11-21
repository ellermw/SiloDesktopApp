using System.Buffers;
using System.Globalization;
using System.Text;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// A collection, row, shelf or library name written into a confirmation or a dialog title: one plain, bounded line.
/// A name is data from the server or another administrator, so it must not break the line, reorder the text around it,
/// hide behind invisible characters or make the title arbitrarily long. Lists and cards keep the whole name and cut it with
/// trimming at display time; only text that is written into a sentence goes through here.
/// </summary>
public static class AdminCollectionNames
{
    /// <summary>The longest name, in user-perceived characters, that a sentence carries before it is cut.</summary>
    public const int DefaultLimit = 80;
    private const int UnitsPerCharacter = 16; // room for a long emoji sequence; a character with thousands of marks does not fit
    private const string Ellipsis = "\u2026";

    /// <summary>What a name that shows nothing is called in a sentence, so a count and the names beside it always agree. Native wording: the official catalog has no untitled collection.</summary>
    public const string UntitledCollection = "Untitled collection";
    public const string UntitledLibrary = "Untitled library";
    /// <summary>"Delete the Untitled shelf?" reads right; "Untitled shelf shelf" does not.</summary>
    public const string UntitledShelf = "Untitled";
    /// <summary>The official wording (<c>web.home.addRow.preview.untitled</c>).</summary>
    public const string UntitledRow = "Untitled row";
    /// <summary>The most tag characters of one emoji tag sequence (a subdivision flag such as England is five).</summary>
    private const int MaxTags = 6;

    private enum Kind { Letter, Space, Joiner }

    /// <summary>
    /// The name as one line: control characters, line and paragraph separators and every space become one space; direction
    /// characters, invisible fillers and other format characters are removed; a zero width joiner or non-joiner stays only
    /// between two visible characters, where it joins them. The result is trimmed and cut at <paramref name="limit"/>
    /// user-perceived characters (never inside one) with an ellipsis. An empty or invisible name, or one made only of
    /// combining marks, gives "". A valid emoji tag sequence (a subdivision flag) stays whole; any other tag character is removed.
    /// </summary>
    public static string Plain(string? name, int limit = DefaultLimit)
    {
        if (string.IsNullOrEmpty(name) || limit < 1) return "";
        var runes = new List<(Rune Rune, Kind Kind)>(name.Length);
        for (var index = 0; index < name.Length;)
        {
            var status = Rune.DecodeFromUtf16(name.AsSpan(index), out var rune, out var consumed);
            index += Math.Max(consumed, 1);
            if (status != OperationStatus.Done) continue; // a lone surrogate. A real U+FFFD decodes as itself and stays.
            if (rune.Value == 0x1F3F4 && TagSequence(name, index) is { Count: > 0 } tags)
            {
                runes.Add((rune, Kind.Letter));
                foreach (var tag in tags) { runes.Add((tag, Kind.Letter)); index += tag.Utf16SequenceLength; }
                continue;
            }
            var kind = Classify(rune);
            if (kind is null) continue;
            if (kind == Kind.Space && runes.Count > 0 && runes[^1].Kind == Kind.Space) continue;
            runes.Add((rune, kind.Value));
        }
        var text = new StringBuilder(runes.Count);
        for (var i = 0; i < runes.Count; i++)
        {
            var (rune, kind) = runes[i];
            // A joiner removed between two spaces leaves them side by side: keep one.
            if (kind == Kind.Space) { if (text.Length > 0 && text[^1] != ' ' && i < runes.Count - 1) text.Append(' '); continue; }
            if (kind == Kind.Joiner)
            {
                // Between two characters it joins them (Persian, Indic and emoji sequences). Anywhere else it has nothing to join.
                var before = i > 0 && runes[i - 1].Kind == Kind.Letter;
                var after = i < runes.Count - 1 && runes[i + 1].Kind == Kind.Letter;
                if (!before || !after) continue;
            }
            text.Append(rune.ToString());
        }
        // Combining marks with nothing to combine with show nothing a reader can use.
        if (!runes.Any(r => r.Kind == Kind.Letter && !IsMark(r.Rune))) return "";
        return Bound(text.ToString().Trim(), limit);
    }

    private static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// The tag characters and the cancel tag after a waving black flag at <paramref name="index"/>, when they form a valid emoji tag
    /// sequence (one to <see cref="MaxTags"/> letters or digits, then U+E007F); an empty list otherwise.
    /// </summary>
    private static List<Rune> TagSequence(string name, int index)
    {
        var found = new List<Rune>();
        for (var i = index; i < name.Length;)
        {
            if (Rune.DecodeFromUtf16(name.AsSpan(i), out var next, out var consumed) != OperationStatus.Done) return [];
            i += consumed; found.Add(next);
            if (next.Value == 0xE007F) return found.Count >= 2 ? found : [];
            var tag = next.Value is >= 0xE0030 and <= 0xE0039 or >= 0xE0061 and <= 0xE007A;
            if (!tag || found.Count > MaxTags) return [];
        }
        return [];
    }

    /// <summary>The plain name, or <paramref name="placeholder"/> when it shows nothing. A count and the names written beside it always agree.</summary>
    public static string PlainOr(string? name, string placeholder, int limit = DefaultLimit)
    {
        var plain = Plain(name, limit);
        return plain.Length == 0 ? placeholder : plain;
    }

    /// <summary>"Delete {name}?" with the name as one plain line, or the fallback when the name shows nothing.</summary>
    public static string Titled(string? name, Func<string, string> title, string fallback)
    {
        var plain = Plain(name);
        return plain.Length == 0 ? fallback : title(plain);
    }

    /// <summary>"A", "A and B", "A, B and C": names written into a sentence, each as one plain line.</summary>
    public static string JoinNames(IEnumerable<string?> names, string placeholder = UntitledCollection) => JoinPlain(names.Select(n => PlainOr(n, placeholder)));

    /// <summary>The same list for entries that are already plain: nothing is cut or dropped again.</summary>
    public static string JoinPlain(IEnumerable<string> plain)
    {
        var list = plain.ToArray();
        return list.Length switch
        {
            0 => "",
            1 => list[0],
            2 => list[0] + " and " + list[1],
            _ => string.Join(", ", list.Take(list.Length - 1)) + " and " + list[^1],
        };
    }

    private static Kind? Classify(Rune rune)
    {
        var value = rune.Value;
        if (value is 0x200C or 0x200D) return Kind.Joiner;
        var category = Rune.GetUnicodeCategory(rune);
        switch (category)
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.SpaceSeparator:
                // U+0085 is a control that other software treats as a line break; every one of these separates words.
                return Kind.Space;
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
                return null; // direction characters, soft hyphen, zero width space, word joiner, byte order mark, tags and the like
        }
        // Invisible fillers that are letters, marks or symbols: they take space and show nothing.
        return value switch
        {
            0x034F or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or 0x180E or 0x2800 or 0x3164 or 0xFFA0 => null,
            >= 0xFE00 and <= 0xFE0F => Kind.Letter, // variation selectors belong to the character before them
            _ => Kind.Letter,
        };
    }

    private static string Bound(string text, int limit)
    {
        if (text.Length == 0) return text;
        var maxUnits = limit * UnitsPerCharacter;
        var result = new StringBuilder();
        var count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (count >= limit || result.Length + element.Length > maxUnits)
            {
                // Cut before this character; the cut never splits one and never leaves a space before the ellipsis.
                return result.ToString().TrimEnd() + Ellipsis;
            }
            result.Append(element); count++;
        }
        return result.ToString();
    }
}
