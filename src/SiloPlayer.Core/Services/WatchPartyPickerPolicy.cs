using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Current room picker policies, independent of playback epoch readiness.</summary>
public static class WatchPartyPickerPolicy
{
    public static int? ResolveSeason(IReadOnlyList<Season> seasons, int? explicitSeason, int? playSeason)
    {
        if (explicitSeason.HasValue) return explicitSeason;
        return seasons.FirstOrDefault(season => season.SeasonNumber == playSeason)?.SeasonNumber
            ?? seasons.FirstOrDefault(season => season.SeasonNumber != 0)?.SeasonNumber
            ?? seasons.FirstOrDefault()?.SeasonNumber;
    }

    public static List<Episode> PlayableEpisodes(IEnumerable<Episode> episodes)
        => episodes.Where(episode => episode.Files.Count > 0).ToList();

    public sealed record SpoilerRisk(IReadOnlyList<string> Names, int EpisodeNumber);

    public static SpoilerRisk? FindSpoilerRisk(Episode pick, IReadOnlyList<Episode> episodes,
        IReadOnlyList<WatchTogetherRoomMember> members, IReadOnlyDictionary<string, WatchTogetherItemMemberState> states)
    {
        foreach (var earlier in episodes.Where(episode => episode.EpisodeNumber < pick.EpisodeNumber))
        {
            var names = members.Where(member => State(earlier.ContentId, member, states) == "unseen")
                .Select(member => member.DisplayName).ToArray();
            if (names.Length > 0 && names.Length < members.Count) return new(names, earlier.EpisodeNumber);
        }
        return null;
    }

    public sealed record NextUp(Episode Episode, int Count);

    public static NextUp? NextUpForRoom(IReadOnlyList<Episode> episodes, IReadOnlyList<WatchTogetherRoomMember> members,
        IReadOnlyDictionary<string, WatchTogetherItemMemberState> states)
    {
        NextUp? best = null;
        var claimed = new HashSet<(int UserId, string ProfileId)>();
        foreach (var episode in episodes)
        {
            var count = 0;
            foreach (var member in members)
            {
                var key = (member.UserId, member.ProfileId);
                if (claimed.Contains(key) || State(episode.ContentId, member, states) == "watched") continue;
                count++; claimed.Add(key);
            }
            if (count > 0 && (best == null || count > best.Count)) best = new(episode, count);
            if (claimed.Count == members.Count) break;
        }
        return best;
    }

    private static string State(string contentId, WatchTogetherRoomMember member,
        IReadOnlyDictionary<string, WatchTogetherItemMemberState> states)
        => states.TryGetValue(contentId, out var state)
            ? state.Members.FirstOrDefault(value => value.UserId == member.UserId && value.ProfileId == member.ProfileId)?.State ?? "unseen"
            : "unseen";
}
