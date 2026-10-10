using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CurrentFilterBadgeBehaviorTests
{
    [Fact]
    public void ComplexRulesHaveNoMisleadingGuidedBadges()
    {
        var query = new QueryDefinition { Groups = [new() { Rules = [new() { Field = "genre", Op = "is_not", Value = "Crime" }, new() { Field = "fixture_unknown", Op = "fixture_op", Value = "Retain me" }] }] };
        Assert.Empty(CatalogFilterBadges.Create(query));
        Assert.Equal(2, CatalogFilterBadges.ActiveCount(query));
        Assert.Equal(2, query.Groups[0].Rules.Count);
    }

    [Fact]
    public void UnwatchedIsOneBadgeWhoseRemovalClearsBothPredicates()
    {
        var query = new QueryDefinition { Groups = [new() { Rules = [new() { Field = "watched", Op = "is", Value = false }, new() { Field = "in_progress", Op = "is", Value = false }] }] };
        var badge = Assert.Single(CatalogFilterBadges.Create(query));
        Assert.Equal(1, CatalogFilterBadges.ActiveCount(query));
        badge.Remove(); Assert.Empty(query.Groups);
    }

    [Fact]
    public void RemovingOneOfTwoAlternativeLanguagesKeepsTheOtherInGuided()
    {
        var query = new QueryDefinition { Groups = [new() { Rules = [new() { Field = "genre", Op = "is", Value = "Crime" }] }, new() { Match = "any", Rules = [new() { Field = "original_language", Op = "is", Value = "en" }, new() { Field = "original_language", Op = "is", Value = "fr" }] }] };
        CatalogFilterBadges.Create(query).Single(badge => badge.Label == "Language: English").Remove();
        Assert.Single(query.Groups);
        Assert.Equal(new[] { "genre", "original_language" }, query.Groups[0].Rules.Select(rule => rule.Field));
        Assert.Equal("fr", query.Groups[0].Rules[1].Value);
    }
}
