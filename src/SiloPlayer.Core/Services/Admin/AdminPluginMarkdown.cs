using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminPluginInlineKind { Text, Strong, Emphasis, Code, Link }

/// <summary>One inline run of plugin Markdown. <see cref="Href"/> is raw and untrusted; open it only through <see cref="AdminPluginPresentation.SafeLink"/>.</summary>
public sealed record AdminPluginInline(AdminPluginInlineKind Kind, string Text, string? Href = null);

/// <summary>A paragraph (one run list) or a list (one run list per item).</summary>
public sealed record AdminPluginBlock(bool IsList, bool Ordered, IReadOnlyList<IReadOnlyList<AdminPluginInline>> Items);

/// <summary>
/// The small Markdown reader the official page uses for setup and description text (web/src/lib/pluginMarkdown.ts at
/// 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e): paragraphs, bullet and numbered lists, inline strong, emphasis, code and
/// links. Anything else, HTML included, stays literal text, and the result is plain data, never markup.
/// </summary>
public static class AdminPluginMarkdown
{
    public const int MaxSourceLength = 20_000;
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(250);
    private static readonly Regex Bullet = new(@"^\s*[-*+]\s+(.*)$", RegexOptions.ECMAScript, Limit);
    private static readonly Regex Numbered = new(@"^\s*[0-9]+[.)]\s+(.*)$", RegexOptions.ECMAScript, Limit);
    private static readonly Regex Paragraphs = new(@"\n\s*\n", RegexOptions.None, Limit);
    // Underscore emphasis is left out: it would mangle snake_case identifiers that setup text mentions.
    private static readonly Regex Inlines = new(@"\*\*([^*]+)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)|\*([^*\s][^*]*)\*", RegexOptions.None, Limit);

    public static IReadOnlyList<AdminPluginBlock> Parse(string? source)
    {
        var raw = source ?? "";
        // README text keeps its own line breaks, but never a direction control, which could reorder what follows it.
        var text = AdminPluginPresentation.StripDirectionControls(raw.Length > MaxSourceLength ? raw[..MaxSourceLength] : raw)
            .Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u2028', '\n').Replace('\u2029', '\n');
        text = new string(text.Select(c => char.IsControl(c) && c is not ('\n' or '\t') ? ' ' : c).ToArray()).Trim();
        var blocks = new List<AdminPluginBlock>();
        if (text.Length == 0) return blocks;
        try
        {
            foreach (var chunk in Paragraphs.Split(text))
            {
                var lines = chunk.Split('\n').Where(line => line.Trim().Length > 0).ToArray();
                if (lines.Length == 0) continue;
                var paragraph = new List<string>();
                List<string>? list = null; var ordered = false;
                void FlushParagraph()
                {
                    if (paragraph.Count == 0) return;
                    blocks.Add(new(false, false, [ParseInlines(string.Join(' ', paragraph))])); paragraph.Clear();
                }
                void FlushList()
                {
                    if (list == null) return;
                    blocks.Add(new(true, ordered, list.Select(ParseInlines).ToArray())); list = null;
                }
                foreach (var line in lines)
                {
                    var bullet = Bullet.Match(line);
                    var numbered = bullet.Success ? Match.Empty : Numbered.Match(line);
                    var item = bullet.Success ? bullet : numbered;
                    if (item.Success)
                    {
                        var isOrdered = numbered.Success;
                        FlushParagraph();
                        if (list != null && ordered != isOrdered) FlushList();
                        if (list == null) { list = []; ordered = isOrdered; }
                        list.Add(item.Groups[1].Value);
                    }
                    else if (list != null && char.IsWhiteSpace(line[0])) list[^1] += " " + line.Trim();
                    else { FlushList(); paragraph.Add(line.Trim()); }
                }
                FlushParagraph(); FlushList();
            }
        }
        catch (RegexMatchTimeoutException) { return [new(false, false, [[new(AdminPluginInlineKind.Text, text)]])]; }
        return blocks;
    }

    public static IReadOnlyList<AdminPluginInline> ParseInlines(string text)
    {
        var runs = new List<AdminPluginInline>(); var last = 0;
        foreach (Match match in Inlines.Matches(text))
        {
            if (match.Index > last) runs.Add(new(AdminPluginInlineKind.Text, text[last..match.Index]));
            if (match.Groups[1].Success && match.Groups[1].Length > 0) runs.Add(new(AdminPluginInlineKind.Strong, match.Groups[1].Value));
            else if (match.Groups[2].Success && match.Groups[2].Length > 0) runs.Add(new(AdminPluginInlineKind.Code, match.Groups[2].Value));
            else if (match.Groups[3].Success && match.Groups[4].Success) runs.Add(new(AdminPluginInlineKind.Link, match.Groups[3].Value, match.Groups[4].Value));
            else runs.Add(new(AdminPluginInlineKind.Emphasis, match.Groups[5].Value));
            last = match.Index + match.Length;
        }
        if (last < text.Length) runs.Add(new(AdminPluginInlineKind.Text, text[last..]));
        return runs;
    }
}
