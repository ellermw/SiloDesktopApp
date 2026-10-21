using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class Oct20DetailCardPresentationTests
{
    [Theory]
    [InlineData(1, 2, " Pilot ", "S01E02 · Pilot", "Season 1, Episode 2, Pilot")]
    [InlineData(0, 5, " ", "S00E05", "Specials, Episode 5")]
    [InlineData(2021, 3, "", "S2021E03", "Season 2021, Episode 3")]
    public void EpisodeCaptionHasCompactVisualCodeAndSpokenSeason(int season, int number, string title,
        string visible, string spoken)
    {
        var caption = EpisodeCardPresentation.Caption(new() { SeasonNumber = season, EpisodeNumber = number, Title = title }, null, false);
        Assert.Equal(visible, caption.Title);
        Assert.Equal(spoken, caption.SpokenTitle);
    }

    [Theory]
    [InlineData("Feb 18, 2026", 52, "Feb 18, 2026\u00a0\u00a0·\u00a0\u00a052m")]
    [InlineData("Feb 18, 2026", 65, "Feb 18, 2026\u00a0\u00a0·\u00a0\u00a01h 5m")]
    [InlineData(null, 60, "1h 0m")]
    [InlineData("Feb 18, 2026", 0, "Feb 18, 2026")]
    [InlineData(null, 0, "")]
    public void CaptionMetadataPlacesStoredDayBeforeFormattedRuntime(string? date, int minutes, string expected)
        => Assert.Equal(expected, EpisodeCardPresentation.Caption(new() { Runtime = minutes }, date, false).Metadata);

    [Fact]
    public void SpoilerProtectionHidesOnlyOverviewAndItsTranslationLabel()
    {
        var episode = new Episode { SeasonNumber = 1, EpisodeNumber = 2, Title = "Pilot", Runtime = 52,
            Overview = "  A description.  ", MachineTranslatedFields = ["overview"] };
        var visible = EpisodeCardPresentation.Caption(episode, "Feb 18, 2026", false);
        var hidden = EpisodeCardPresentation.Caption(episode, "Feb 18, 2026", true);
        Assert.Equal("A description.", visible.Overview);
        Assert.True(visible.ShowTranslation);
        Assert.Equal("", hidden.Overview);
        Assert.False(hidden.ShowTranslation);
        Assert.Equal(visible.Title, hidden.Title);
        Assert.Equal(visible.Metadata, hidden.Metadata);
    }

    [Theory]
    [InlineData(320, 2)] [InlineData(639, 2)] [InlineData(640, 3)]
    [InlineData(1023, 3)] [InlineData(1024, 4)] [InlineData(1279, 4)] [InlineData(1280, 5)]
    public void ExtrasGridTracksUpstreamViewportBreakpoints(double width, int columns)
        => Assert.Equal(columns, ExtraCardPresentation.ColumnCount(width));

    [Fact]
    public void CurrentExtraArtworkSurvivesApiModelDecoding()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var item = JsonSerializer.Deserialize<MediaItemDetail>("""{"extras":[{"content_id":"extra","kind":"featurette","still_url":"https://fixture.invalid/still.png","still_thumbhash":"hash"}]}""", options)!;
        using var roundtrip = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(item.Extras), options));
        Assert.True(roundtrip.RootElement.TryGetProperty("still_url", out var url));
        Assert.Equal("https://fixture.invalid/still.png", url.GetString());
        Assert.True(roundtrip.RootElement.TryGetProperty("still_thumbhash", out var hash));
        Assert.Equal("hash", hash.GetString());
    }

    [Fact]
    public void DamagedNoticeRequiresEveryAdvertisedFileToBeUnreadable()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var damaged = JsonSerializer.Deserialize<Episode>("""{"files":[{"unreadable":true},{"unreadable":true}]}""", options)!;
        var mixed = JsonSerializer.Deserialize<Episode>("""{"files":[{"unreadable":true},{"unreadable":false}]}""", options)!;
        Assert.True(EpisodeCardPresentation.Caption(damaged, null, false).ShowUnreadable);
        Assert.False(EpisodeCardPresentation.Caption(mixed, null, false).ShowUnreadable);
        Assert.False(EpisodeCardPresentation.Caption(new(), null, false).ShowUnreadable);
    }
}
