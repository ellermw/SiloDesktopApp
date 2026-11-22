using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The playing session's title on Activity and the dashboard's Now Playing (official silo-server c90e406db: #5106 leads an episode with its show,
/// #5105 drops the movie subtitle, #5109 fixes the test). The first three cases are web/src/pages/adminActivityPresentation.test.ts one for one
/// ("playback session title"); the rest are the same rule at its edges. Every case runs for an Activity session and for a dashboard session,
/// which the Now Playing tile builds from the same row, so the two cannot drift apart.
/// </summary>
public sealed class AdminActivityShowTitleTests
{
    private sealed record Case(string? MediaType, string? SeriesTitle, int? Season, int? Episode, string MediaTitle);

    // makeSession(): media_file_id 42, media_title "8:00 A.M." unless overridden.
    private static Case Make(string? mediaType = null, string? series = null, int? season = null, int? episode = null, string mediaTitle = "8:00 A.M.")
        => new(mediaType, series, season, episode, mediaTitle);

    private static AdminActivitySession Activity(Case c) => new()
    {
        SessionId = "s1", UserId = "1", Username = "viewer", ProfileId = "p1", MediaFileId = "42", RequestedMediaFileId = "42",
        MediaTitle = c.MediaTitle, MediaType = c.MediaType ?? "", SeriesTitle = c.SeriesTitle, SeasonNumber = c.Season, EpisodeNumber = c.Episode,
    };

    private static DashboardSession Tile(Case c) => new()
    {
        MediaFileId = "42", MediaTitle = c.MediaTitle, MediaType = c.MediaType ?? "", SeriesTitle = c.SeriesTitle, SeasonNumber = c.Season, EpisodeNumber = c.Episode,
    };

    private static void Expect(Case c, string title, string? subtitle)
    {
        Assert.Equal(title, P.GetPlaybackSessionTitle(Activity(c)));
        Assert.Equal(subtitle, P.GetPlaybackSessionSubtitle(Activity(c)));
        Assert.Equal(title, DashboardPresentation.NowPlayingTitle(Tile(c)));
        Assert.Equal(subtitle, P.GetPlaybackSessionSubtitle(Tile(c)));
    }

    // ---- web/src/pages/adminActivityPresentation.test.ts, one for one ----

    private static Case Episode(string mediaTitle = "8:00 A.M.") => Make("episode", "The Pitt", 1, 2, mediaTitle);

    [Fact]
    public void LeadsAnEpisodeWithItsShowAndPutsTheEpisodeUnderneath()
        => Expect(Episode(), "The Pitt", "S1E2 · 8:00 A.M.");

    [Fact]
    public void ShowsOnlyTheEpisodeCodeWhenTheEpisodeHasNoTitle()
        => Expect(Episode(""), "The Pitt", "S1E2");

    [Fact]
    public void KeepsAMoviesOwnTitleAndGivesItNoSubtitle()
        => Expect(Make("movie", mediaTitle: "Heat"), "Heat", null);

    // ---- the rest of the rule ----

    [Fact]
    public void AnEpisodeNeedsTheShowAndBothNumbersToLeadWithTheShow()
    {
        Assert.True(P.IsEpisodeSession(Activity(Episode())));
        Assert.True(P.IsEpisodeSession(Tile(Episode())));
        // Season 0 (specials) and episode 0 are numbers.
        Assert.True(P.IsEpisodeSession(Activity(Make("episode", "The Pitt", 0, 0))));
        // A missing show, season or episode number: the episode keeps its own title and reads "Series" under it.
        foreach (var loose in new[]
        {
            Make("episode", null, 1, 2), Make("episode", "", 1, 2), Make("episode", "The Pitt", null, 2), Make("episode", "The Pitt", 1, null),
        })
        {
            Assert.False(P.IsEpisodeSession(Activity(loose)));
            Assert.False(P.IsEpisodeSession(Tile(loose)));
            Expect(loose, "8:00 A.M.", "Series");
        }
    }

    [Fact]
    public void ASessionWithNumbersButNotMarkedAnEpisodeStillLeadsWithItsShow()
        // isEpisodeSession looks at the show and the numbers only, not at media_type.
        => Expect(Make("movie", "The Pitt", 1, 2), "The Pitt", "S1E2 · 8:00 A.M.");

    [Theory]
    [InlineData("series")]
    [InlineData("episode")]
    public void ASeriesLevelRowKeepsItsTitleAndReadsSeries(string mediaType)
        => Expect(Make(mediaType, mediaTitle: "The Pitt"), "The Pitt", "Series");

    [Theory]
    [InlineData("track")]
    [InlineData("audiobook")]
    [InlineData("audiobook_chapter")]
    [InlineData("")]
    [InlineData(null)]
    public void ATrackAChapterOrAnythingElseKeepsItsTitleAndHasNoSubtitle(string? mediaType)
        => Expect(Make(mediaType, mediaTitle: "Intro"), "Intro", null);

    [Fact]
    public void ASessionWithNoTitleIsNamedByItsFile()
    {
        Expect(Make("movie", mediaTitle: ""), "File #42", null);
        Expect(Make(null, mediaTitle: ""), "File #42", null);
    }

    [Fact]
    public void TheSubtitleKeepsTheEpisodeCodeThenTheEpisodeTitleWithTheSeparatorOnlyBetweenThem()
    {
        Expect(Make("episode", "Show", 10, 120, "A · B"), "Show", "S10E120 · A · B");
        Expect(Make("episode", "Show", 3, 13, " "), "Show", "S3E13");
    }

    [Fact]
    public void AMovieNoLongerReadsMovie()
    {
        Assert.Null(P.GetPlaybackSessionSubtitle(Activity(Make("movie", mediaTitle: "Heat"))));
        Assert.Null(P.GetPlaybackSessionSubtitle(Tile(Make("movie", mediaTitle: "Heat"))));
    }

    // ---- plain text, one line ----

    [Fact]
    public void ATitleFromTheServerIsOnePlainLine()
    {
        // Line breaks and tabs become one space; direction and invisible characters are removed.
        Expect(Make("movie", mediaTitle: "Line one\r\nLine\ttwo‮ gnp.exe⁩"), "Line one Line two gnp.exe", null);
        Expect(Make("episode", "The\nPitt‎", 1, 2, "8:00\r\nA.M."), "The Pitt", "S1E2 · 8:00 A.M.");
        // A title made only of invisible characters shows nothing, so the file fallback names it.
        Expect(Make("movie", mediaTitle: "​‮"), "File #42", null);
        // A show name that shows nothing does not lead: the episode keeps its own title.
        Assert.Equal("8:00 A.M.", P.GetPlaybackSessionTitle(Activity(Make("episode", "​", 1, 2))));
    }

    [Fact]
    public void ALongTitleIsKeptWholeForTheDisplayToTruncateAndOnlyAnAbsurdOneIsCut()
    {
        var long120 = string.Concat(Enumerable.Repeat("Long title ", 12)).Trim();
        Expect(Make("movie", mediaTitle: long120), long120, null);
        Expect(Make("episode", long120, 1, 2, long120), long120, "S1E2 · " + long120);
        var absurd = new string('x', 5000);
        var cut = P.GetPlaybackSessionTitle(Activity(Make("movie", mediaTitle: absurd)));
        Assert.True(cut.Length < 400 && cut.EndsWith('…'), cut.Length.ToString());
    }

    [Fact]
    public void SortingByTheStreamColumnGroupsAShowsEpisodes()
    {
        AdminActivitySession Row(string id, Case c) { var s = Activity(c); s.SessionId = id; return s; }
        var rows = new[]
        {
            Row("e1", Make("episode", "The Pitt", 1, 1, "Zebra")), Row("m", Make("movie", mediaTitle: "Heat")),
            Row("e2", Make("episode", "The Pitt", 1, 2, "Aardvark")), Row("a", Make("movie", mediaTitle: "Alien")),
        };
        var state = new AdminActivityListState();
        state.ToggleSort(ActivitySortField.Media);
        Assert.Equal(["Alien", "Heat", "The Pitt", "The Pitt"], state.Apply(rows).Select(s => P.GetPlaybackSessionTitle(s)));
    }
}
