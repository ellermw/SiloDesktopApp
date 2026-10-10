using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

public static class PersonalCollectionOwnership
{
    public sealed record OwnerGroup(string Id, string Name, IReadOnlyList<Collection> Collections);
    public sealed record Partition(IReadOnlyList<Collection> Own, IReadOnlyList<OwnerGroup> Shared);
    public static bool IsOwn(Collection collection, string? profileId)
        => !string.IsNullOrEmpty(profileId) && collection.CreatorProfileId == profileId;
    public static Partition Split(IEnumerable<Collection> collections, string? profileId, IReadOnlyList<Profile> profiles)
    {
        var items = collections.ToArray();
        var ownerOrder = profiles.Select((profile, index) => (profile.Id, index)).ToDictionary(entry => entry.Id, entry => entry.index);
        return new(items.Where(item => IsOwn(item, profileId)).ToArray(), items.Where(item => !IsOwn(item, profileId))
            .GroupBy(item => item.CreatorProfileId).OrderBy(group => ownerOrder.GetValueOrDefault(group.Key, profiles.Count))
            .Select(group => new OwnerGroup(group.Key, profiles.FirstOrDefault(profile => profile.Id == group.Key)?.Name ?? "Another profile", group.ToArray())).ToArray());
    }
}
