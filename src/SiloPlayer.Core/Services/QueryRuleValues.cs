using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

public static class QueryRuleValues
{
    public static bool IsBoolean(string field) => field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision";
    public static bool IsNumeric(string field) => field is "year" or "decade" or "rating" or "rating_imdb" or "rating_tmdb" or "rating_rt_critic" or "rating_rt_audience" or "rating_rotten_tomatoes" or "bitrate" or "duration" or "runtime" or "user_rating";
    public static bool IsDate(string field) => QueryFieldCatalog.Get(field)?.Kind == "date";

    // Apply only to an explicit condition change. Loading saved rules must be lossless.
    public static object? ForOperator(string field, string op, object? value)
    {
        object?[]? range = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } json => json.EnumerateArray().Select(element => (object?)element.Clone()).ToArray(),
            System.Collections.IEnumerable values when value is not string => values.Cast<object?>().ToArray(),
            _ => null
        };
        if (op == "between" && (IsNumeric(field) || IsDate(field)))
            return range?.Length == 2 ? value : new[] { "", "" };
        if (range != null) value = range.FirstOrDefault() ?? "";
        if (IsBoolean(field)) return Format(value) == "true";
        if (IsDate(field))
        {
            var text = Format(value);
            var pattern = op is "in_last" or "not_in_last" ? @"^\s*\d+\s*[hdwmy]\s*$" : @"^\d{4}-\d{2}-\d{2}$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? text : "";
        }
        return value;
    }

    public static object? Parse(string field, string op, string text)
    {
        if (op is "exists" or "not_exists") return null;
        if (op is "between" or "in" or "not_in")
        {
            var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (op == "between" && parts.Length != 2) throw new FormatException("Enter two values separated by a comma.");
            if (parts.Length == 0) throw new FormatException("Enter at least one value.");
            return IsNumeric(field) ? parts.Select(Number).ToArray() : parts;
        }
        if (IsBoolean(field)) return bool.TryParse(text, out var flag) ? flag : throw new FormatException("Choose True or False.");
        if (IsNumeric(field)) return Number(text);
        return text.Trim();
    }

    private static double Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
        ? number : throw new FormatException("Enter a valid number (use a dot for decimals).");

    public static string Format(object? value) => value switch
    {
        null => "",
        JsonElement { ValueKind: JsonValueKind.Array } json => string.Join(", ", json.EnumerateArray().Select(element => Format(element))),
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? "",
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        JsonElement json => json.ToString(),
        bool flag => flag ? "true" : "false",
        System.Collections.IEnumerable values when value is not string => string.Join(", ", values.Cast<object?>().Select(Format)),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static bool HasValue(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => false,
        JsonElement { ValueKind: JsonValueKind.String } json => !string.IsNullOrWhiteSpace(json.GetString()),
        JsonElement { ValueKind: JsonValueKind.Array } json => json.GetArrayLength() > 0 && json.EnumerateArray().All(element => HasValue(element)),
        System.Collections.IEnumerable values when value is not string => values.Cast<object?>().Any() && values.Cast<object?>().All(HasValue),
        _ => true
    };
}
