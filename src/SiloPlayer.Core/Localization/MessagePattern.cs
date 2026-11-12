using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// One catalog message, compiled from the generated compact form of the
/// catalog's precompiled ICU messages (see tools/SiloTranslationsImport). It
/// supports exactly what the generated catalogs contain: literals, named
/// arguments, cardinal plurals with exact (<c>=N</c>) and CLDR category
/// options and <c>#</c>, selects, plain numbers, and rich-text tags (whose
/// children print as plain text). Formatting never substitutes text into a
/// placeholder it cannot fill: a missing argument fails the whole message.
/// </summary>
public sealed class MessagePattern
{
    private const char FirstStrongIsolate = '⁨';
    private const char PopDirectionalIsolate = '⁩';
    private readonly Node[] _nodes;

    private MessagePattern(Node[] nodes, HashSet<string> printed, HashSet<string> selectors)
    {
        _nodes = nodes;
        PrintedArguments = printed;
        SelectorArguments = selectors;
    }

    /// <summary>Arguments printed as text.</summary>
    public IReadOnlySet<string> PrintedArguments { get; }

    /// <summary>Arguments a plural or select chooses on.</summary>
    public IReadOnlySet<string> SelectorArguments { get; }

    public static MessagePattern Parse(JsonElement element)
    {
        var printed = new HashSet<string>(StringComparer.Ordinal);
        var selectors = new HashSet<string>(StringComparer.Ordinal);
        var nodes = element.ValueKind == JsonValueKind.String
            ? [new Literal(element.GetString()!)]
            : ParseNodes(element, printed, selectors);
        return new MessagePattern(nodes, printed, selectors);
    }

    /// <summary>
    /// Formats the message. Returns false, leaving <paramref name="text"/> null,
    /// when an argument it uses is missing or unusable (a plural on a value
    /// that is not a number, or a choice with no matching option or other).
    /// </summary>
    public bool TryFormat(MessageFormatContext context, IReadOnlyDictionary<string, object?>? arguments, out string? text)
    {
        var builder = new StringBuilder();
        if (!Append(builder, _nodes, context, arguments, pound: null))
        {
            text = null;
            return false;
        }
        text = builder.ToString();
        return true;
    }

    private static bool Append(StringBuilder builder, Node[] nodes, MessageFormatContext context,
        IReadOnlyDictionary<string, object?>? arguments, decimal? pound)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case Literal literal:
                    builder.Append(literal.Text);
                    break;
                case Argument argument:
                    if (!TryArgument(arguments, argument.Name, out var value)) return false;
                    builder.Append(PrintArgument(value, context));
                    break;
                case NumberArgument number:
                    if (!TryArgument(arguments, number.Name, out var numberValue) || !TryNumber(numberValue, out var numeric)) return false;
                    builder.Append(FormatNumber(numeric, context));
                    break;
                case Pound:
                    if (pound is not { } count) return false;
                    builder.Append(FormatNumber(count, context));
                    break;
                case Tag tag:
                    if (!Append(builder, tag.Children, context, arguments, pound)) return false;
                    break;
                case Plural plural:
                {
                    if (!TryArgument(arguments, plural.Name, out var pluralValue) || !TryNumber(pluralValue, out var amount)) return false;
                    if (!TryExactPluralOption(plural.Options, amount, out var chosen)
                        && !plural.Options.TryGetValue(PluralRules.Category(context.PluralLanguage, amount), out chosen)
                        && !plural.Options.TryGetValue("other", out chosen))
                        return false;
                    if (!Append(builder, chosen, context, arguments, amount)) return false;
                    break;
                }
                case Select select:
                {
                    if (!TryArgument(arguments, select.Name, out var selectValue)) return false;
                    var key = Convert.ToString(selectValue, CultureInfo.InvariantCulture) ?? "";
                    if (!select.Options.TryGetValue(key, out var chosen) && !select.Options.TryGetValue("other", out chosen))
                        return false;
                    if (!Append(builder, chosen, context, arguments, pound)) return false;
                    break;
                }
            }
        }
        return true;
    }

    private static bool TryArgument(IReadOnlyDictionary<string, object?>? arguments, string name, out object value)
    {
        value = null!;
        if (arguments == null || !arguments.TryGetValue(name, out var found) || found == null) return false;
        value = found;
        return true;
    }

    private static bool TryExactPluralOption(Dictionary<string, Node[]> options, decimal amount, out Node[] chosen)
    {
        // ICU exact selectors compare numbers. Decimal storage scale must not
        // distinguish an argument of 1.500 from the selector =1.50.
        foreach (var (selector, nodes) in options)
        {
            if (selector.StartsWith('=')
                && decimal.TryParse(selector.AsSpan(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
                && exact == amount)
            {
                chosen = nodes;
                return true;
            }
        }
        chosen = null!;
        return false;
    }

    private static string PrintArgument(object value, MessageFormatContext context)
    {
        var text = value switch
        {
            string s => s,
            // As ICU's {name} prints a number: no grouping, invariant digits.
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            IFormattable formattable => formattable.ToString(null, context.FormatCulture),
            _ => value.ToString() ?? "",
        };
        // A right-to-left interface isolates printed values so a left-to-right
        // title keeps its punctuation in place. A bare number is not isolated:
        // it takes its direction from the surrounding letters.
        if (context.IsolateArguments && !IsBareNumber(text))
            return FirstStrongIsolate + text + PopDirectionalIsolate;
        return text;
    }

    private static bool IsBareNumber(string text)
    {
        if (text.Length == 0) return false;
        foreach (var ch in text)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.DecimalDigitNumber) return false;
        return true;
    }

    private static bool TryNumber(object value, out decimal number)
    {
        switch (value)
        {
            case byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case float or double:
                var real = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsFinite(real) && Math.Abs(real) < 7.9e27)
                {
                    number = (decimal)real;
                    return true;
                }
                break;
            case string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number):
                return true;
        }
        number = 0;
        return false;
    }

    // ICU prints # and {n, number} with the message locale's grouping.
    private static string FormatNumber(decimal value, MessageFormatContext context)
        => value == decimal.Truncate(value)
            ? value.ToString("#,##0", context.NumberFormat)
            : value.ToString("#,##0.###", context.NumberFormat);

    private static Node[] ParseNodes(JsonElement array, HashSet<string> printed, HashSet<string> selectors)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new FormatException("A compiled message must be a string or an array.");
        var nodes = new List<Node>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                nodes.Add(new Literal(item.GetString()!));
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object) throw new FormatException("Unexpected compiled message element.");
            if (item.TryGetProperty("a", out var argument))
            {
                printed.Add(argument.GetString()!);
                nodes.Add(new Argument(argument.GetString()!));
            }
            else if (item.TryGetProperty("n", out var number))
            {
                printed.Add(number.GetString()!);
                nodes.Add(new NumberArgument(number.GetString()!));
            }
            else if (item.TryGetProperty("h", out _)) nodes.Add(new Pound());
            else if (item.TryGetProperty("g", out var tag))
                nodes.Add(new Tag(tag.GetString()!, ParseNodes(item.GetProperty("c"), printed, selectors)));
            else if (item.TryGetProperty("p", out var plural))
            {
                selectors.Add(plural.GetString()!);
                nodes.Add(new Plural(plural.GetString()!, ParseOptions(item.GetProperty("o"), printed, selectors)));
            }
            else if (item.TryGetProperty("s", out var select))
            {
                selectors.Add(select.GetString()!);
                nodes.Add(new Select(select.GetString()!, ParseOptions(item.GetProperty("o"), printed, selectors)));
            }
            else throw new FormatException("Unknown compiled message element.");
        }
        return [.. nodes];
    }

    private static Dictionary<string, Node[]> ParseOptions(JsonElement options, HashSet<string> printed, HashSet<string> selectors)
    {
        var result = new Dictionary<string, Node[]>(StringComparer.Ordinal);
        foreach (var option in options.EnumerateObject()) result[option.Name] = ParseNodes(option.Value, printed, selectors);
        return result;
    }

    private abstract record Node;
    private sealed record Literal(string Text) : Node;
    private sealed record Argument(string Name) : Node;
    private sealed record NumberArgument(string Name) : Node;
    private sealed record Pound : Node;
    private sealed record Tag(string Name, Node[] Children) : Node;
    private sealed record Plural(string Name, Dictionary<string, Node[]> Options) : Node;
    private sealed record Select(string Name, Dictionary<string, Node[]> Options) : Node;
}

/// <summary>How a message is formatted: plural language, number symbols, value culture and isolation.</summary>
public sealed record MessageFormatContext(string PluralLanguage, NumberFormatInfo NumberFormat, CultureInfo FormatCulture, bool IsolateArguments);
