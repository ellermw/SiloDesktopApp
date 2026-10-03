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
}
