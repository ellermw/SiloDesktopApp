using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

public sealed class DecimalMessageFormattingTests
{
    [Theory]
    [InlineData("0.0")]
    [InlineData("0.00")]
    public void ScaledZeroUsesTheCatalogExactZeroMessage(string value)
    {
        var locales = new LocaleService();
        var english = locales.Create(AppLocaleResolution.Resolve("en", ["en-US"], "en-US"));
        var args = new Dictionary<string, object?> { ["count"] = decimal.Parse(value, CultureInfo.InvariantCulture), ["count_text"] = "0" };
        Assert.Equal("Not in a collection yet", english.Get("web.collections.copy.inCollections", args));
    }

    [Theory]
    [InlineData("en", "1.0", "one")]
    [InlineData("de", "1.00", "one")]
    [InlineData("ar", "0.0", "zero")]
    [InlineData("ar", "2.00", "two")]
    [InlineData("ar", "3.0", "few")]
    [InlineData("ar", "11.00", "many")]
    [InlineData("es", "1000000.0", "many")]
    [InlineData("en", "1.50", "other")]
    [InlineData("ar", "2.10", "other")]
    public void CardinalSelectionUsesNumericValueRatherThanDecimalStorageScale(string language, string value, string expected)
        => Assert.Equal(expected, PluralRules.Category(language, decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Fact]
    public void ExactFractionalSelectorsCompareNumbersBeforeCardinalCategories()
    {
        using var json = JsonDocument.Parse("""[{"p":"value","o":{"=1.50":["exact"],"other":["other"]}}]""");
        var message = MessagePattern.Parse(json.RootElement);
        var context = new MessageFormatContext("en", CultureInfo.InvariantCulture.NumberFormat, CultureInfo.InvariantCulture, false);
        Assert.True(message.TryFormat(context, new Dictionary<string, object?> { ["value"] = 1.500m }, out var text));
        Assert.Equal("exact", text);
    }
}
