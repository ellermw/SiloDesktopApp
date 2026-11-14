using System.Globalization;
using System.Text;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One styled piece of a rich message: plain text, or text inside a named tag such as &lt;user&gt;.</summary>
public readonly record struct DashboardTextRun(string Text, string? Tag);

/// <summary>
/// Dashboard strings by their WebUI catalog key, formatted with the ICU
/// subset the dashboard messages use (arguments, plural with <c>#</c>,
/// select, quoted braces and rich tags). Beta has no localization foundation,
/// so <see cref="Resolver"/> is the single seam a later foundation plugs into;
/// until then every key resolves to the pinned English catalog.
/// </summary>
public static class DashboardText
{
    /// <summary>Optional localized pattern lookup; null or a null result falls back to English.</summary>
    public static Func<string, string?>? Resolver { get; set; }

    public static bool Has(string key) => DashboardTextCatalog.English.ContainsKey(key);

    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)DashboardTextCatalog.English.Keys;

    public static string T(string key, params (string Name, object? Value)[] values)
    {
        var runs = Rich(key, values);
        if (runs.Count == 1) return runs[0].Text;
        var builder = new StringBuilder();
        foreach (var run in runs) builder.Append(run.Text);
        return builder.ToString();
    }

    public static IReadOnlyList<DashboardTextRun> Rich(string key, params (string Name, object? Value)[] values)
    {
        var pattern = Resolver?.Invoke(key) ?? (DashboardTextCatalog.English.TryGetValue(key, out var english) ? english : key);
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in values) map[name] = value;
        var runs = new List<DashboardTextRun>();
        try
        {
            new Formatter(pattern, map, runs).Run();
        }
        catch (FormatException)
        {
            runs.Clear();
            runs.Add(new DashboardTextRun(pattern, null));
        }
        return Merge(runs);
    }

    /// <summary>Count and its locale-formatted text, the pair the WebUI passes as count/count_text.</summary>
    public static (string, object?)[] Count(long count, params (string Name, object? Value)[] extra)
    {
        var list = new List<(string, object?)> { ("count", count), ("count_text", FormatInteger(count)) };
        list.AddRange(extra);
        return [.. list];
    }

    public static string FormatInteger(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static IReadOnlyList<DashboardTextRun> Merge(List<DashboardTextRun> runs)
    {
        var merged = new List<DashboardTextRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].Tag == run.Tag)
                merged[^1] = new DashboardTextRun(merged[^1].Text + run.Text, run.Tag);
            else merged.Add(run);
        }
        if (merged.Count == 0) merged.Add(new DashboardTextRun("", null));
        return merged;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        string text => text,
        long or int or short => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.CurrentCulture),
        double number when Math.Abs(number % 1) < double.Epsilon => ((long)number).ToString("N0", CultureInfo.CurrentCulture),
        double number => number.ToString("0.##", CultureInfo.CurrentCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    private static double? AsNumber(object? value) => value switch
    {
        long l => l,
        int i => i,
        double d => d,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private sealed class Formatter(string pattern, IReadOnlyDictionary<string, object?> values, List<DashboardTextRun> output)
    {
        private int _index;

        public void Run()
        {
            Emit(ParseMessage(endOnBrace: false), null, null);
            if (_index != pattern.Length) throw new FormatException();
        }

        private sealed record Node(string Kind, string Text, List<Node>? Children = null, string? Name = null,
            Dictionary<string, List<Node>>? Options = null);

        private List<Node> ParseMessage(bool endOnBrace, string? endTag = null)
        {
            var nodes = new List<Node>();
            var text = new StringBuilder();
            void Flush()
            {
                if (text.Length > 0) { nodes.Add(new Node("text", text.ToString())); text.Clear(); }
            }
            while (_index < pattern.Length)
            {
                var c = pattern[_index];
                if (c == '\'' && _index + 2 < pattern.Length && (pattern[_index + 1] == '{' || pattern[_index + 1] == '}') && pattern[_index + 2] == '\'')
                {
                    text.Append(pattern[_index + 1]);
                    _index += 3;
                    continue;
                }
                if (c == '}' && endOnBrace) break;
                if (c == '<' && endTag != null && Peek("</" + endTag + ">")) break;
                if (c == '<' && TryReadOpenTag(out var tag))
                {
                    Flush();
                    var children = ParseMessage(endOnBrace, tag);
                    if (!Peek("</" + tag + ">")) throw new FormatException();
                    _index += tag.Length + 3;
                    nodes.Add(new Node("tag", "", children, tag));
                    continue;
                }
                if (c == '#') { Flush(); nodes.Add(new Node("hash", "")); _index++; continue; }
                if (c == '{') { Flush(); nodes.Add(ParseArgument()); continue; }
                text.Append(c);
                _index++;
            }
            Flush();
            return nodes;
        }

        private bool Peek(string literal) => string.CompareOrdinal(pattern, _index, literal, 0, literal.Length) == 0;

        private bool TryReadOpenTag(out string tag)
        {
            tag = "";
            var end = pattern.IndexOf('>', _index);
            if (end <= _index + 1) return false;
            var name = pattern.Substring(_index + 1, end - _index - 1);
            if (name.Length == 0 || !name.All(char.IsLetter)) return false;
            tag = name;
            _index = end + 1;
            return true;
        }

        private Node ParseArgument()
        {
            _index++; // {
            var name = ReadToken();
            SkipSpace();
            if (_index < pattern.Length && pattern[_index] == '}') { _index++; return new Node("arg", "", Name: name); }
            if (_index >= pattern.Length || pattern[_index] != ',') throw new FormatException();
            _index++;
            var kind = ReadToken();
            SkipSpace();
            if (_index >= pattern.Length || pattern[_index] != ',') throw new FormatException();
            _index++;
            var options = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
            while (true)
            {
                SkipSpace();
                if (_index >= pattern.Length) throw new FormatException();
                if (pattern[_index] == '}') { _index++; break; }
                var selector = ReadToken();
                SkipSpace();
                if (_index >= pattern.Length || pattern[_index] != '{') throw new FormatException();
                _index++;
                options[selector] = ParseMessage(endOnBrace: true);
                if (_index >= pattern.Length || pattern[_index] != '}') throw new FormatException();
                _index++;
            }
            return new Node(kind == "plural" ? "plural" : kind == "select" ? "select" : throw new FormatException(), "", Name: name, Options: options);
        }

        private string ReadToken()
        {
            SkipSpace();
            var start = _index;
            while (_index < pattern.Length && !char.IsWhiteSpace(pattern[_index]) && pattern[_index] is not (',' or '{' or '}'))
                _index++;
            if (_index == start) throw new FormatException();
            return pattern[start.._index];
        }

        private void SkipSpace()
        {
            while (_index < pattern.Length && char.IsWhiteSpace(pattern[_index])) _index++;
        }

        private void Emit(List<Node> nodes, string? tag, double? pluralValue)
        {
            foreach (var node in nodes)
            {
                switch (node.Kind)
                {
                    case "text":
                        output.Add(new DashboardTextRun(node.Text, tag));
                        break;
                    case "hash":
                        output.Add(new DashboardTextRun(pluralValue is { } n ? FormatValue(n) : "#", tag));
                        break;
                    case "arg":
                        output.Add(new DashboardTextRun(values.TryGetValue(node.Name!, out var value) ? FormatValue(value) : "{" + node.Name + "}", tag));
                        break;
                    case "tag":
                        Emit(node.Children!, node.Name, pluralValue);
                        break;
                    case "plural":
                    {
                        var number = values.TryGetValue(node.Name!, out var raw) ? AsNumber(raw) : null;
                        var options = node.Options!;
                        List<Node>? chosen = null;
                        if (number is { } exact && options.TryGetValue("=" + exact.ToString(CultureInfo.InvariantCulture), out var exactNodes))
                            chosen = exactNodes;
                        chosen ??= number == 1 && options.TryGetValue("one", out var one) ? one : null;
                        chosen ??= options.TryGetValue("other", out var other) ? other : null;
                        if (chosen != null) Emit(chosen, tag, number);
                        break;
                    }
                    case "select":
                    {
                        var key = values.TryGetValue(node.Name!, out var raw) ? FormatValue(raw) : "";
                        var options = node.Options!;
                        if (options.TryGetValue(key, out var chosen) || options.TryGetValue("other", out chosen))
                            Emit(chosen, tag, pluralValue);
                        break;
                    }
                }
            }
        }
    }
}
