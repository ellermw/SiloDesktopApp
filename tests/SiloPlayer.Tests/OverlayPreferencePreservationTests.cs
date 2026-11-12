using System.Reflection;
using System.Text.Json;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

public sealed class OverlayPreferencePreservationTests
{
    [Fact]
    public void EditingKnownOverlayKeepsFutureItemsOrderAndAttributes()
    {
        const string original = """
        {"version":2,"preset":"classic","future_root":{"a":1},"order":["future_badge","year"],
         "items":{"future_badge":{"enabled":true,"position":"bottom-right","future_option":"opaque"},
         "year":{"enabled":false,"position":"top-left","future_option":"keep"}}}
        """;
        var parse = typeof(CardOverlayService).GetMethod("ParsePrefs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var serialize = typeof(CardOverlayService).GetMethod("SerializePrefs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var prefs = (CardOverlayPrefs)parse.Invoke(null, [original])!;
        prefs.Items["year"] = prefs.Items["year"] with { Enabled = true };
        var json = (string)serialize.Invoke(null, [prefs])!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("future_root").GetProperty("a").GetInt32());
        Assert.Equal("future_badge", doc.RootElement.GetProperty("order")[0].GetString());
        Assert.Equal("opaque", doc.RootElement.GetProperty("items").GetProperty("future_badge").GetProperty("future_option").GetString());
        Assert.Equal("keep", doc.RootElement.GetProperty("items").GetProperty("year").GetProperty("future_option").GetString());
        Assert.True(doc.RootElement.GetProperty("items").GetProperty("year").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void RepeatedValueImageKeyKeepsTheStoredDocumentThroughAnEdit()
    {
        const string original = """
        {"version":2,"preset":"plate","futureItem":{"a":1},
         "items":{"resolution":{"enabled":false,"valueImages":{"2160p":"a.webp","2160p":"b.webp","1080p":"c.webp"}}}}
        """;
        var parse = typeof(CardOverlayService).GetMethod("ParsePrefs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var serialize = typeof(CardOverlayService).GetMethod("SerializePrefs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var prefs = (CardOverlayPrefs)parse.Invoke(null, [original])!;
        Assert.NotNull(prefs);
        Assert.Equal("plate", prefs.Preset);
        Assert.False(prefs.Items["resolution"].Enabled);
        Assert.Equal("b.webp", prefs.Items["resolution"].ValueImages!["2160p"]);
        Assert.Equal("c.webp", prefs.Items["resolution"].ValueImages!["1080p"]);
        prefs.Items["year"] = prefs.Items["year"] with { Enabled = true };
        var json = (string)serialize.Invoke(null, [prefs])!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("plate", doc.RootElement.GetProperty("preset").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("futureItem").GetProperty("a").GetInt32());
        var resolution = doc.RootElement.GetProperty("items").GetProperty("resolution");
        Assert.False(resolution.GetProperty("enabled").GetBoolean());
        var images = resolution.GetProperty("valueImages");
        Assert.Equal(2, images.EnumerateObject().Count());
        Assert.Equal("b.webp", images.GetProperty("2160p").GetString());
        Assert.Equal("c.webp", images.GetProperty("1080p").GetString());
        Assert.True(doc.RootElement.GetProperty("items").GetProperty("year").GetProperty("enabled").GetBoolean());
    }
}
