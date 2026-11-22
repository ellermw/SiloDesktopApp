using System.Text.Json;
using SiloPlayer.Core.Models.Sessions;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>Household streams text. Silo Server c7c155927 (#4561) renamed the live-session fields with no alias: media_item_id (was content_id),
/// series_title (was series_name), and media_title now carries the episode title (was episode_name).</summary>
public sealed class SessionDisplayTextTests
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static PlaybackSessionSummary Row(string json) => JsonSerializer.Deserialize<PlaybackSessionSummary>(json, SnakeCase)!;

    [Fact]
    public void CurrentNamesGiveTheEpisodeTitleItsSeriesLineAndItsItem()
    {
        var row = Row("""{"media_file_id":42,"media_item_id":"ep-9","media_title":"Pilot","media_type":"episode","series_title":"Example Show","season_number":1,"episode_number":3}""");
        Assert.Equal("Pilot", SessionDisplayText.GetTitle(row));
        Assert.Equal("S1E3 · Example Show", SessionDisplayText.GetSubtitle(row));
        Assert.Equal("ep-9", SessionDisplayText.GetItemId(row));
    }

    [Fact]
    public void OlderNamesStillGiveTheSameText()
    {
        var row = Row("""{"media_file_id":42,"content_id":"ep-9","media_title":"Example Show","media_type":"episode","series_name":"Example Show","episode_name":"Pilot","season_number":1,"episode_number":3}""");
        Assert.Equal("Pilot", SessionDisplayText.GetTitle(row));
        Assert.Equal("S1E3 · Example Show", SessionDisplayText.GetSubtitle(row));
        Assert.Equal("ep-9", SessionDisplayText.GetItemId(row));
    }

    [Fact]
    public void CurrentNamesWinWhenBothAreSent()
    {
        var row = Row("""{"media_file_id":42,"media_item_id":"new","content_id":"old","media_title":"New title","series_title":"New show","series_name":"Old show","episode_name":"Old title","season_number":2,"episode_number":5}""");
        Assert.Equal("New title", SessionDisplayText.GetTitle(row));
        Assert.Equal("S2E5 · New show", SessionDisplayText.GetSubtitle(row));
        Assert.Equal("new", SessionDisplayText.GetItemId(row));
    }

    [Theory]
    [InlineData("""{"media_file_id":42,"media_title":"","media_type":"episode","series_title":"Show","season_number":1,"episode_number":3}""", "S1E3", "S1E3 · Show")]
    [InlineData("""{"media_file_id":42,"media_title":"Loose episode","media_type":"episode","series_title":"","season_number":null,"episode_number":null}""", "Loose episode", "Series")]
    [InlineData("""{"media_file_id":42,"media_title":"A Show","media_type":"series"}""", "A Show", "Series")]
    [InlineData("""{"media_file_id":42,"media_title":"A Film","media_type":"movie","series_title":""}""", "A Film", "Movie")]
    [InlineData("""{"media_file_id":42,"media_title":"","media_type":""}""", "File #42", null)]
    public void TitleAndSubtitleFollowTheOfficialRules(string json, string title, string? subtitle)
    {
        // Official: web/src/pages/adminActivityPresentation.ts getPlaybackSessionTitle / getPlaybackSessionSubtitle at e130f262d.
        var row = Row(json);
        Assert.Equal(title, SessionDisplayText.GetTitle(row));
        Assert.Equal(subtitle, SessionDisplayText.GetSubtitle(row));
    }

    [Fact]
    public void NoItemIdMeansNoLink()
        => Assert.Null(SessionDisplayText.GetItemId(Row("""{"media_file_id":42,"media_item_id":"","content_id":" "}""")));
}
