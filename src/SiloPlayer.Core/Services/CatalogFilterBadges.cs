using System.Globalization;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

/// <summary>Readable secondary-filter badges, retaining the rules each remove action owns.</summary>
public static class CatalogFilterBadges
{
    public sealed record Badge(string Label, QueryGroup Group, IReadOnlyList<QueryRule> Rules, QueryDefinition? Query = null)
    {
        public void Remove()
        {
            foreach (var rule in Rules) Group.Rules.Remove(rule);
            if (Query == null) return;
            if (Group.Match == "any" && Group.Rules is [{ Field: "original_language" }] remaining)
            {
                var all = Query.Groups.FirstOrDefault(group => group.Match == "all");
                if (all == null) Group.Match = "all";
                else { all.Rules.Add(remaining[0]); Group.Rules.Clear(); }
            }
            Query.Groups.RemoveAll(group => group.Rules.Count == 0);
        }
    }

    public static int ActiveCount(QueryDefinition query, string? mediaScope = null) => GuidedQuerySupport.CanEdit(query, mediaScope)
        ? Create(query, mediaScope).Count : query.Groups.Sum(group => group.Rules.Count);

    public static IReadOnlyList<Badge> Create(QueryDefinition query, string? mediaScope = null)
    {
        var badges = new List<Badge>();
        if (!GuidedQuerySupport.CanEdit(query, mediaScope)) return badges;
        foreach (var group in query.Groups)
        {
            // Bounds in an OR group are independent alternatives, not a range.
            var from = group.Match == "all" ? group.Rules.FirstOrDefault(rule => rule.Field == "year" && rule.Op == "gte") : null;
            var to = group.Match == "all" ? group.Rules.FirstOrDefault(rule => rule.Field == "year" && rule.Op == "lte") : null;
            var combinedYear = false;
            var watched = group.Rules.FirstOrDefault(rule => rule.Field == "watched");
            var progress = group.Rules.FirstOrDefault(rule => rule.Field == "in_progress");
            foreach (var rule in group.Rules)
            {
                if (watched != null && progress != null && Value(watched) == "false" && Value(progress) == "false" &&
                    (ReferenceEquals(rule, watched) || ReferenceEquals(rule, progress)))
                {
                    if (ReferenceEquals(rule, watched)) badges.Add(new(Label(watched, mediaScope), group, [watched, progress], query));
                    continue;
                }
                if (from != null && to != null && (ReferenceEquals(rule, from) || ReferenceEquals(rule, to)))
                {
                    if (!combinedYear) badges.Add(new($"Year: {Value(from)}–{Value(to)}", group, [from, to], query));
                    combinedYear = true;
                    continue;
                }
                badges.Add(new(Label(rule, mediaScope), group, [rule], query));
            }
        }
        return badges;
    }

    private static string Value(QueryRule rule) => QueryRuleValues.Format(rule.Value);

    private static string Label(QueryRule rule, string? scope)
    {
        var value = Value(rule);
        if (rule.Op == "is")
        {
            if (rule.Field == "resolution" && value is "4k" or "2160p") return "4K";
            if (rule.Field == "hdr" && value == "true") return "HDR";
            if (rule.Field == "dolby_vision" && value == "true") return "DOVI";
            if (rule.Field == "original_language") return $"Language: {Language(value)}";
            if (rule.Field is "watched" or "in_progress")
            {
                var audiobook = scope is "audiobook" or "audiobooks";
                var verb = audiobook ? "Listening" : scope is "ebook" or "ebooks" or "manga" ? "Read" : "Watch";
                var state = rule.Field == "in_progress" ? value == "true" ? "in progress" : "not in progress" : audiobook ? value == "true" ? "listened" : "unlistened" : value == "true" ? "watched" : "unwatched";
                return $"{verb}: {state}";
            }
        }
        if (rule.Field == "year" && rule.Op == "between")
        {
            var bounds = value.Split(',', StringSplitOptions.TrimEntries);
            if (bounds.Length == 2) return $"Year: {bounds[0]}–{bounds[1]}";
        }
        if (rule.Op == "in_last" && rule.Field is "added_at" or "release_date")
            return $"{(rule.Field == "added_at" ? "Added" : "Released")} in last: {value}";
        var field = rule.Field switch
        {
            "rating_imdb" => "IMDb", "content_rating" => "Rated", "status" => "Match",
            "original_language" => "Language", "dolby_vision" => "Dolby Vision", "hdr" => "HDR",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rule.Field.Replace('_', ' ')),
        };
        var op = rule.Op switch
        {
            "is" => "", "gte" => ">= ", "lte" => "<= ", "gt" => "> ", "lt" => "< ",
            "is_not" => "not ", "contains" => "contains ", "not_contains" => "does not contain ",
            _ => rule.Op.Replace('_', ' ') + " ",
        };
        return $"{field}: {op}{value}";
    }

    private static string Language(string value)
    {
        try { return CultureInfo.GetCultureInfo(value).EnglishName; }
        catch (CultureNotFoundException) { return value; }
    }
}
