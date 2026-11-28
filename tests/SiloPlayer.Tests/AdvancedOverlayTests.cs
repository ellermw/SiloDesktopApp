using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdvancedOverlayTests
{
    [Theory]
    [InlineData("minimal", 9, 4, 0, 600)]
    [InlineData("classic", 10, 8, 2, 600)]
    [InlineData("vibrant", 10, 8, 2, 700)]
    [InlineData("pill", 10, 10, 4, 600)]
    [InlineData("square", 9, 6, 2, 700)]
    [InlineData("plate", 12, 7, 4, 800)]
    [InlineData("band", 12.5, 0, 1, 800)]
    public void RealEvaluatorMatchesOfficialPreset(string preset, double font, double horizontal, double vertical, int weight)
    {
        var look = OverlayAppearance.Resolve(preset, null, null);
        Assert.Equal(font, look.FontSize);
        Assert.Equal(horizontal, look.PaddingInline);
        Assert.Equal(vertical, look.PaddingBlock);
        Assert.Equal(weight, look.FontWeight);
        Assert.Equal(preset == "plate", look.SourceChips);
        Assert.Equal(preset == "band", look.Band);
    }

    [Fact]
    public void StyleFramesBareTextAndResolvesContrastWithoutErasingAccent()
    {
        var style = new OverlayStyle { Size = 2, Shape = "square", Background = "#ffffffff", BorderWidth = 3, FontWeight = "regular", Uppercase = false, Shadow = true };
        var look = OverlayAppearance.Resolve("band", style, "#ffff00");
        Assert.Equal(25, look.FontSize);
        Assert.Equal(15, look.PaddingInline);
        Assert.Equal(5, look.PaddingBlock);
        Assert.Equal(2, look.Radius);
        Assert.Equal(400, look.FontWeight);
        Assert.Equal("#000000ff", look.Foreground.Hex);
        Assert.False(look.TextShadow);
        Assert.True(look.BoxShadow);
        Assert.False(look.Uppercase);
        Assert.Equal(3, look.BorderWidth);
    }

    // Official style.test.ts (#5226): with Look -> Fill set to None, vibrant badges drew black text on the artwork.
    [Theory]
    [InlineData(null)]
    [InlineData("#f5c518")]
    public void ASeeThroughFillDrawsWhiteTextOnTheArtwork(string? accent)
    {
        var look = OverlayAppearance.Resolve("vibrant", new OverlayStyle { Background = "#00000000" }, accent);
        Assert.Equal("#ffffffff", look.Foreground.Hex);
    }

    [Fact]
    public void ASeeThroughFillLiftsItsTextOffTheArtworkWithAShadowUnlessShadowsAreOff()
    {
        Assert.True(OverlayAppearance.Resolve("vibrant", new OverlayStyle { Background = "#00000000" }, null).TextShadow);
        Assert.False(OverlayAppearance.Resolve("vibrant", new OverlayStyle { Background = "#00000000", Shadow = false }, null).TextShadow);
        // A preset that already has a text shadow keeps it when the viewer turns shadows off (the style only adds one).
        Assert.True(OverlayAppearance.Resolve("minimal", new OverlayStyle { Background = "#00000000", Shadow = false }, null).TextShadow);
    }

    [Fact]
    public void ALightSolidFillKeepsBlackTextAndNoShadow()
    {
        var look = OverlayAppearance.Resolve("vibrant", new OverlayStyle { Background = "#FFFFFFE6" }, null);
        Assert.Equal("#000000ff", look.Foreground.Hex);
        Assert.False(look.TextShadow);
    }

    [Theory]
    [InlineData("plate")]
    [InlineData("square")]
    public void APresetThatPaintsNoAccentFillKeepsItsOwnTextColourOnASeeThroughFill(string preset)
    {
        var plain = OverlayAppearance.Resolve(preset, null, null);
        var look = OverlayAppearance.Resolve(preset, new OverlayStyle { Background = "#00000000" }, null);
        Assert.Equal(plain.Foreground, look.Foreground);
    }

    [Fact]
    public void InvalidStyleInputCannotEscapeBounds()
    {
        var style = JsonSerializer.Deserialize<OverlayStyle>("""{"size":999,"borderWidth":-1,"background":"https://invalid","shape":"unknown"}""");
        var look = OverlayAppearance.Resolve("plate", style, null);
        Assert.Equal(12, look.FontSize);
        Assert.Equal(1, look.BorderWidth);
        Assert.Equal(6, look.Radius);
        Assert.Equal((byte)184, look.Background.Alpha);
    }

    [Theory]
    [InlineData("#ff000080", 128)]
    [InlineData("#112233", 255)]
    public void ColorsUseRgbaNotWinUiArgb(string color, int alpha)
    {
        Assert.Equal(alpha, OverlayColor.Parse(color)!.Value.Alpha);
    }

    [Fact]
    public void EditingOneStyleFieldPreservesOpaqueRepresentations()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse("""{"style":{"size":1,"uppercase":"future","futureLook":{"mode":3}}}""")!.AsObject();
        var style = OverlayStyle.Parse(JsonSerializer.SerializeToElement(root["style"]))!;
        (style with { Size = 1.5 }).WriteTo(root);
        Assert.Equal("future", root["style"]!["uppercase"]!.GetValue<string>());
        Assert.Equal(3, root["style"]!["futureLook"]!["mode"]!.GetValue<int>());
        Assert.Equal(1.5, root["style"]!["size"]!.GetValue<double>());
    }
}

