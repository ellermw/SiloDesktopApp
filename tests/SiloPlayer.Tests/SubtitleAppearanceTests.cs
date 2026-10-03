using System.Text.Json;
using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Tests;

public sealed class SubtitleAppearanceTests
{
    [Fact]
    public void DefaultsMatchCurrentWebUiContract()
    {
        var appearance = new SubtitleAppearance();

        Assert.Equal("large", appearance.FontSize);
        Assert.Equal("sans-serif", appearance.FontFamily);
        Assert.Equal("#ffffff", appearance.FontColor);
        Assert.Equal("#000000", appearance.BackgroundColor);
        Assert.Equal("box", appearance.BackgroundStyle);
        Assert.Equal(75, appearance.BackgroundOpacity);
        Assert.False(appearance.TextOutline);
        Assert.Equal("#000000", appearance.TextOutlineColor);
        Assert.Equal("bottom", appearance.Position);
    }

    [Fact]
    public void JsonRoundTripPreservesEveryWebUiField()
    {
        var expected = new SubtitleAppearance
        {
            FontSize = "xxlarge",
            FontFamily = "monospace",
            FontColor = "#facc15",
            BackgroundColor = "#1e3a5f",
            BackgroundStyle = "outline",
            BackgroundOpacity = 40,
            TextOutline = true,
            TextOutlineColor = "#3b82f6",
            Position = "lower-third",
        };

        var actual = SubtitleAppearance.Parse(expected.ToJson());

        Assert.Equal(expected.ToJson(), actual.ToJson());
        using var json = JsonDocument.Parse(actual.ToJson());
        Assert.True(json.RootElement.TryGetProperty("textOutlineColor", out _));
    }

    [Fact]
    public void ParserFallsBackPerFieldForMalformedOrUnsupportedValues()
    {
        var appearance = SubtitleAppearance.Parse("""
            {"fontSize":"enormous","fontFamily":"comic","fontColor":"red","backgroundStyle":"glass","backgroundOpacity":101,"textOutline":true,"textOutlineColor":"#ef4444","position":"middle"}
            """);

        Assert.Equal("large", appearance.FontSize);
        Assert.Equal("sans-serif", appearance.FontFamily);
        Assert.Equal("#ffffff", appearance.FontColor);
        Assert.Equal("box", appearance.BackgroundStyle);
        Assert.Equal(75, appearance.BackgroundOpacity);
        Assert.True(appearance.TextOutline);
        Assert.Equal("#ef4444", appearance.TextOutlineColor);
        Assert.Equal("bottom", appearance.Position);
    }
}
