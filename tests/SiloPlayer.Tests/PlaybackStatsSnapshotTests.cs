using System.Text.Json;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Tests;

public sealed class PlaybackStatsSnapshotTests
{
    [Fact]
    public void PreservesDisplayedUnknownsAndUsesSemanticIdsAcrossOptionalRows()
    {
        var first = PlaybackStatsSnapshot.Parse("""[{"header":"PLAYER","rows":[{"label":"Protocol","value":"—"}]}]""")!;
        var next = PlaybackStatsSnapshot.Parse("""[{"header":"PLAYER","rows":[{"label":"Auto quality limit","value":"2 Mbps"},{"label":"Protocol","value":"—"}]}]""")!;
        Assert.Equal("—", first[0].Value);
        Assert.Equal(first[0].Id, next[1].Id);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[{\"header\":\"PLAYER\",\"rows\":null}]")]
    [InlineData("[{\"header\":\"PLAYER\",\"rows\":[{\"label\":\"Protocol\",\"value\":7}]}]")]
    [InlineData("[{\"header\":\"PLAYER\",\"rows\":[{\"label\":\"Protocol\",\"value\":\"a\"},{\"label\":\"Protocol\",\"value\":\"b\"}]}]")]
    public void RejectsMalformedOrAmbiguousDisplaySnapshots(string json)
        => Assert.Null(PlaybackStatsSnapshot.Parse(json));

    [Fact]
    public void BoundsTextRowsSectionsAndEntireMessages()
    {
        static object Section(int rows, string value) => new { header = "PLAYER", rows = Enumerable.Range(0, rows).Select(i => new { label = $"Row {i}", value }).ToArray() };
        Assert.Null(PlaybackStatsSnapshot.Parse(JsonSerializer.Serialize(new[] { Section(129, "x") })));
        Assert.Null(PlaybackStatsSnapshot.Parse(JsonSerializer.Serialize(new[] { Section(1, new string('x', 2049)) })));
        Assert.Null(PlaybackStatsSnapshot.Parse(JsonSerializer.Serialize(Enumerable.Range(0, 9).Select(_ => Section(0, "x")))));
        Assert.Null(PlaybackStatsSnapshot.Parse(new string(' ', 65537)));
    }
}
