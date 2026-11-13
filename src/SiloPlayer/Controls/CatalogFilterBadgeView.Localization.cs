using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

public static partial class CatalogFilterBadgeView
{
    public static Border Build(CatalogFilterBadges.Badge badge, string? scope, Action remove)
        => Build(() => DisplayLabel(badge, scope), remove);

    internal static string DisplayLabel(CatalogFilterBadges.Badge badge, string? scope)
    {
        var rule = badge.Rules[0];
        var value = QueryRuleValues.Format(rule.Value);
        if (rule.Field == "year" && badge.Rules.Count == 2)
            return AppStrings.Get("web.catalog.filters.badgeYearRange", new Dictionary<string, object?>
            { ["from"] = QueryRuleValues.Format(badge.Rules.First(bound => bound.Op == "gte").Value),
                ["to"] = QueryRuleValues.Format(badge.Rules.First(bound => bound.Op == "lte").Value) }, badge.Label);
        if (rule.Field == "year" && rule.Op == "between")
        {
            var bounds = value.Split(',', StringSplitOptions.TrimEntries);
            if (bounds.Length == 2) return AppStrings.Get("web.catalog.filters.badgeYearRange",
                new Dictionary<string, object?> { ["from"] = bounds[0], ["to"] = bounds[1] }, badge.Label);
        }
        if (rule.Op == "is" && rule.Field is "watched" or "in_progress")
        {
            if (rule.Field == "in_progress" && value != "true") return badge.Label;
            var key = scope is "audiobook" or "audiobooks" ? "web.catalog.filters.badgeListening"
                : scope is "ebook" or "ebooks" or "manga" ? "web.catalog.filters.badgeRead" : "web.catalog.filters.badgeWatch";
            return AppStrings.Get(key, new Dictionary<string, object?>
                { ["status"] = rule.Field == "in_progress" ? "in_progress" : value == "true" ? "watched" : "unwatched" }, badge.Label);
        }
        var keyByRule = (rule.Field, rule.Op) switch
        {
            ("genre", "is") => "web.catalog.filters.badgeGenre",
            ("content_rating", "is") => "web.catalog.filters.badgeContentRating",
            ("studio", "is") => "web.catalog.filters.badgeStudio",
            ("country", "is") => "web.catalog.filters.badgeCountry",
            ("original_language", "is") => "web.catalog.filters.badgeLanguage",
            ("actor", "is") => "web.catalog.filters.badgeActor",
            ("director", "is") => "web.catalog.filters.badgeDirector",
            ("writer", "is") => "web.catalog.filters.badgeWriter",
            ("producer", "is") => "web.catalog.filters.badgeProducer",
            ("author", "is") => "web.catalog.filters.badgeAuthor",
            ("narrator", "is") => "web.catalog.filters.badgeNarrator",
            ("series", "is") => "web.catalog.filters.badgeSeries",
            ("network", "is") => "web.catalog.filters.badgeNetwork",
            ("status", "is") => "web.catalog.filters.badgeMatch",
            ("year", "gte") => "web.catalog.filters.badgeYearFrom",
            ("year", "lte") => "web.catalog.filters.badgeYearTo",
            ("rating_imdb", "gte") => "web.catalog.filters.badgeMinImdb",
            ("added_at", "in_last") => "web.catalog.filters.badgeAddedInLast",
            ("release_date", "in_last") => "web.catalog.filters.badgeReleasedInLast",
            _ => null
        };
        return keyByRule == null ? badge.Label : ValueLabel(keyByRule, value, badge.Label);
    }

    internal static string ValueLabel(string key, string value, string? fallback = null)
        => AppStrings.Get(key, new Dictionary<string, object?> { ["name"] = value, ["genre"] = value,
            ["rating"] = value, ["country"] = value, ["language"] = value, ["status"] = value,
            ["year"] = value, ["span"] = value }, fallback);
}
