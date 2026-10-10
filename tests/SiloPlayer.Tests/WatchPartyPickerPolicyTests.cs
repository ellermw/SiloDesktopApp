using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class WatchPartyPickerPolicyTests
{
    [Fact]
    public void ExplicitShelfSeasonWinsFreshResumeAndResumeWinsFirstRegularSeason()
    {
        Season[] seasons = [new() { SeasonNumber = 0 }, new() { SeasonNumber = 1 }, new() { SeasonNumber = 6 }];
        Assert.Equal(1, WatchPartyPickerPolicy.ResolveSeason(seasons, 1, 6));
        Assert.Equal(6, WatchPartyPickerPolicy.ResolveSeason(seasons, null, 6));
        Assert.Equal(1, WatchPartyPickerPolicy.ResolveSeason(seasons, null, 99));
        Assert.Equal(0, WatchPartyPickerPolicy.ResolveSeason([new() { SeasonNumber = 0 }], null, null));
        Assert.Null(WatchPartyPickerPolicy.ResolveSeason([], null, null));
    }

    [Fact]
    public void EpisodeFilesDetermineEligibilityWithoutReorderingOrInferringFromRuntime()
    {
        Episode[] episodes = [new() { ContentId = "no-file", Runtime = 60 }, new() { ContentId = "playable", Files = [new() { FileId = 10 }] }, new() { ContentId = "next", Files = [new() { FileId = 11 }] }];
        Assert.Equal(["playable", "next"], WatchPartyPickerPolicy.PlayableEpisodes(episodes).Select(episode => episode.ContentId));
    }

    private static readonly WatchTogetherRoomMember[] Members = [new() { UserId = 1, ProfileId = "same", DisplayName = "Host" }, new() { UserId = 2, ProfileId = "same", DisplayName = "Guest" }];
    private static readonly Episode[] Episodes = [new() { ContentId = "e1", EpisodeNumber = 1 }, new() { ContentId = "e2", EpisodeNumber = 2 }, new() { ContentId = "e3", EpisodeNumber = 3 }];

    [Fact]
    public void SpoilersConcernEarlierEpisodeSomeMembersMissedNotTheUnseenPickItself()
    {
        var states = new Dictionary<string, WatchTogetherItemMemberState> { ["e1"] = State("e1", "watched", "unseen"), ["e2"] = State("e2", "unseen", "unseen") };
        Assert.Null(WatchPartyPickerPolicy.FindSpoilerRisk(Episodes[0], Episodes, Members, states));
        var risk = WatchPartyPickerPolicy.FindSpoilerRisk(Episodes[1], Episodes, Members, states);
        Assert.Equal(1, risk!.EpisodeNumber); Assert.Equal(["Guest"], risk.Names);
        states["e1"] = State("e1", "in_progress", "watched");
        Assert.Null(WatchPartyPickerPolicy.FindSpoilerRisk(Episodes[1], Episodes, Members, states));
    }

    [Fact]
    public void AllUnseenOrMissingStatesAndAnEmptyRoomDoNotCreateAheadOfOthersWarnings()
    {
        Assert.Null(WatchPartyPickerPolicy.FindSpoilerRisk(Episodes[2], Episodes, Members, new Dictionary<string, WatchTogetherItemMemberState>()));
        Assert.Null(WatchPartyPickerPolicy.FindSpoilerRisk(Episodes[2], Episodes, [], new Dictionary<string, WatchTogetherItemMemberState>()));
    }

    [Fact]
    public void NextUpClaimsEachMemberAtTheirEarliestUnfinishedEpisodeAndRetainsFirstTie()
    {
        var states = new Dictionary<string, WatchTogetherItemMemberState> { ["e1"] = State("e1", "watched", "in_progress"), ["e2"] = State("e2", "unseen", "unseen") };
        var next = WatchPartyPickerPolicy.NextUpForRoom(Episodes, Members, states);
        Assert.Equal("e1", next!.Episode.ContentId); Assert.Equal(1, next.Count);
        Assert.Equal(2, WatchPartyPickerPolicy.NextUpForRoom(Episodes, Members, new Dictionary<string, WatchTogetherItemMemberState>())!.Count);
        states = Episodes.ToDictionary(episode => episode.ContentId, episode => State(episode.ContentId, "watched", "watched"));
        Assert.Null(WatchPartyPickerPolicy.NextUpForRoom(Episodes, Members, states));
    }

    private static WatchTogetherItemMemberState State(string id, string host, string guest) => new()
    {
        ContentId = id, Members = [new() { UserId = 1, ProfileId = "same", State = host }, new() { UserId = 2, ProfileId = "same", State = guest }]
    };
}
