using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

/// <summary>Whether every rule can be represented by the current Guided controls.</summary>
public static class GuidedQuerySupport
{
    public const string UnavailableMessage = "These rules use options the Guided view can't show.";

    public static bool CanEdit(QueryDefinition query, string? scope = null)
    {
        if (query.Match != "all") return false;
        var all = query.Groups.Where(group => group.Match == "all").ToArray();
        var any = query.Groups.Where(group => group.Match == "any").ToArray();
        if (all.Length > 1 || any.Length > 1 || all.Length + any.Length != query.Groups.Count || query.Groups.Any(group => group.Rules.Count == 0)) return false;
        if (any.Length > 0 && (any[0].Rules.Count < 2 || any[0].Rules.Any(rule => rule.Field != "original_language" || rule.Op != "is"))) return false;
        var rules = query.Groups.SelectMany(group => group.Rules).ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var text = QueryRuleValues.Format(rule.Value);
            if (rule.AdditionalProperties?.Count > 0 || !keys.Add(rule.Field is "genre" or "original_language" ? rule.Field + ":" + text : rule.Field == "year" ? rule.Field + ":" + rule.Op : rule.Field)) return false;
            var supported = rule.Field switch
            {
                "genre" => rule.Op is "is" or "contains" && IsText(rule.Value),
                "year" => rule.Op is "gte" or "lte" && IsNumber(rule.Value),
                "rating_imdb" => rule.Op == "gte" && IsNumber(rule.Value),
                "actor" or "director" or "writer" or "producer" or "author" or "series" or "studio" or "network" or "country" or "status" or "content_rating" or "original_language" => rule.Op == "is" && IsText(rule.Value),
                "narrator" => (query.MediaScope ?? scope) is "audiobook" or "audiobooks" && rule.Op == "is" && IsText(rule.Value),
                "added_at" or "release_date" => rule.Op == "in_last" && IsText(rule.Value),
                "resolution" => rule.Op == "is" && text == "2160p",
                "hdr" or "dolby_vision" => rule.Op == "is" && Boolean(rule.Value) == true,
                "watched" or "in_progress" => rule.Op == "is" && Boolean(rule.Value) != null,
                _ => false,
            };
            if (!supported) return false;
        }
        var languages = rules.Where(rule => rule.Field == "original_language").ToArray();
        if (languages.Length > 1 && (any.Length != 1 || languages.Length != any[0].Rules.Count)) return false;
        var watched = rules.FirstOrDefault(rule => rule.Field == "watched");
        var progress = rules.FirstOrDefault(rule => rule.Field == "in_progress");
        return (watched, progress) switch
        {
            (null, null) => true,
            (not null, null) => Boolean(watched.Value) == true,
            (null, not null) => Boolean(progress.Value) == true,
            _ => Boolean(watched!.Value) == false && Boolean(progress!.Value) == false,
        };
    }

    private static bool IsText(object? value) => value switch
    {
        string text => !string.IsNullOrEmpty(text),
        JsonElement { ValueKind: JsonValueKind.String } json => !string.IsNullOrEmpty(json.GetString()),
        JsonElement { ValueKind: JsonValueKind.Number } => true,
        byte or short or int or long or float or double or decimal => true,
        _ => false,
    };
    private static bool IsNumber(object? value)
    {
        var text = QueryRuleValues.Format(value);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) &&
               text == number.ToString("G", CultureInfo.InvariantCulture);
    }
    private static bool? Boolean(object? value) => value switch
    {
        bool boolean => boolean,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };
}
