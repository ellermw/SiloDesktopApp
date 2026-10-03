using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class QueryRuleValueTests
{
    [Fact]
    public void EditingUsesFieldTypesAndInvariantRanges()
    {
        Assert.Equal("00123", QueryRuleValues.Parse("title", "is", "00123"));
        Assert.Equal(false, QueryRuleValues.Parse("hdr", "is", "false"));
        Assert.Equal(2020d, QueryRuleValues.Parse("year", "gte", "2020"));
        var range = Assert.IsType<double[]>(QueryRuleValues.Parse("rating_imdb", "between", "7.5, 9"));
        Assert.Equal(new[] { 7.5, 9d }, range);
        Assert.Equal(new[] { "en", "de" }, QueryRuleValues.Parse("original_language", "in", "en,de"));
        Assert.Throws<FormatException>(() => QueryRuleValues.Parse("year", "between", "2020,no"));
    }

    [Fact]
    public void LoadedArraysAndFalseRemainTypedAndValid()
    {
        using var json = JsonDocument.Parse("[2020,2026]");
        Assert.Equal("2020, 2026", QueryRuleValues.Format(json.RootElement));
        Assert.True(QueryRuleValues.HasValue(false)); Assert.True(QueryRuleValues.HasValue(json.RootElement));
        Assert.False(QueryRuleValues.HasValue(""));
    }
}
