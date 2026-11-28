using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class EpisodeAirDateLabelTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(null, "2026-11-20", "S2026E296")]
    [InlineData("air_date", "2026-11-20", "Nov 20, 2026")]
    [InlineData("air_date", null, "S2026E296")]
    [InlineData("air_date", "2026-02-30", "S2026E296")]
    [InlineData("future_mode", "2026-11-20", "S2026E296")]
    public void EpisodeLabelMatchesOfficialCardCases(string? mode, string? date, string expected)
        => Assert.Equal(expected, EpisodeLabels.Label(mode, date, 2026, 296,
            day => DisplayDateFormatting.Date(day, null, English, false, medium: true)));

    [Theory]
    [InlineData("YYYY-MM-DD", "en-US", false, "2026-11-20")]
    [InlineData("DD/MM/YYYY", "en-US", false, "20 Nov 2026")]
    [InlineData("MM/DD/YYYY", "en-US", false, "Nov 20, 2026")]
    [InlineData("DD/MM/YYYY", "de-DE", true, "20. Nov. 2026")]
    public void ViewerPreferenceAndLocaleFormatTheCalendarDay(string preference, string culture, bool localized, string expected)
        => Assert.Equal(expected, EpisodeLabels.AirDate("air_date", "2026-11-20",
            day => DisplayDateFormatting.Date(day, preference, CultureInfo.GetCultureInfo(culture), localized, medium: true)));

    [Fact]
    public void PartySubtitleKeepsEnglishEvenWhenViewerUsesDayFirst()
    {
        var local = EpisodeLabels.AirDate("air_date", "2026-11-20",
            day => DisplayDateFormatting.Date(day, "DD/MM/YYYY", English, false, medium: true));

        Assert.Equal("20 Nov 2026", local);
        Assert.Equal("Nov 20, 2026", EpisodeLabels.PartySubtitle("air_date", "2026-11-20", 2026, 296));
        Assert.Equal("S2026 E296", EpisodeLabels.PartySubtitle("air_date", null, 2026, 296));
    }

    [Theory]
    [InlineData(null, "Nov 20, 2026")]
    [InlineData("", "Nov 20, 2026")]
    [InlineData("Bulls vs Wizards", "Nov 20, 2026 •\u00a0Bulls vs Wizards")]
    public void DateAndTitleUseOneSeparator(string? title, string expected)
        => Assert.Equal(expected, EpisodeLabels.WithTitle("Nov 20, 2026", title));

    [Fact]
    public void SeriesCaptionUsesTitleFirstEvenWithoutADate()
    {
        var episode = new Episode { Title = " Bulls vs Wizards ", SeasonNumber = 2026, EpisodeNumber = 296 };
        var numbered = EpisodeCardPresentation.Caption(episode, null, false);
        var dated = EpisodeCardPresentation.Caption(episode, null, false, "air_date", "Nov 20, 2026");
        var undated = EpisodeCardPresentation.Caption(episode, null, false, "air_date");
        Assert.Equal("S2026E296 · Bulls vs Wizards", numbered.Title);
        Assert.Equal("Bulls vs Wizards", dated.Title);
        Assert.Equal("Bulls vs Wizards", dated.SpokenTitle);
        Assert.Equal("Nov 20, 2026", dated.Metadata);
        Assert.Equal("Bulls vs Wizards", undated.Title);

        episode.Title = "";
        Assert.Equal("Nov 20, 2026", EpisodeCardPresentation.Caption(episode, null, false, "air_date", "Nov 20, 2026").Title);
        Assert.Equal("S2026E296", EpisodeCardPresentation.Caption(episode, null, false, "air_date").Title);
    }

    [Theory]
    [InlineData("{}", null, null)]
    [InlineData("""{"episode_labels":"air_date","air_date":null}""", "air_date", null)]
    [InlineData("""{"episode_labels":"air_date","air_date":"2026-11-20"}""", "air_date", "2026-11-20")]
    [InlineData("""{"episode_labels":"future_mode","air_date":"2026-11-20"}""", "future_mode", "2026-11-20")]
    public void ResponsesPreserveTheOptionalWireFields(string json, string? mode, string? date)
    {
        var card = JsonSerializer.Deserialize<MediaItem>(json)!;
        var detail = JsonSerializer.Deserialize<MediaItemDetail>(json)!;
        var watch = JsonSerializer.Deserialize<WatchDetailResponse>(json)!;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var shuffle = JsonSerializer.Deserialize<Shuffle>($$"""{"next":{{json}}}""", options)!;
        var party = JsonSerializer.Deserialize<WatchTogetherPickerEntry>($$"""{"item":{{json}}}""", options)!;
        var tonight = JsonSerializer.Deserialize<WatchTonightItem>(json)!;
        var recommendation = JsonSerializer.Deserialize<RecommendationItem>(json)!;

        Assert.Equal((mode, date), (card.EpisodeLabels, card.AirDate));
        Assert.Equal((mode, date), (detail.EpisodeLabels, detail.AirDate));
        Assert.Equal((mode, date), (watch.EpisodeLabels, watch.AirDate));
        Assert.Equal((mode, date), (shuffle.Next.EpisodeLabels, shuffle.Next.AirDate));
        Assert.Equal((mode, date), (party.Item.EpisodeLabels, party.Item.AirDate));
        Assert.Equal((mode, date), (tonight.EpisodeLabels, tonight.AirDate));
        Assert.Equal((mode, date), (recommendation.EpisodeLabels, recommendation.AirDate));
    }
}
