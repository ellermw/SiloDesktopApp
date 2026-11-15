using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class SubtitleStoredIdentity
{
    public static bool HasIdentity(SubtitleTrackInfo track)
        => track.Id is > 0 || !string.IsNullOrWhiteSpace(track.SyncKey);

    public static SubtitleTrackInfo? Resolve(SubtitleTrackInfo requested, IEnumerable<SubtitleTrackInfo> inventory)
    {
        if (!HasIdentity(requested)) return null;
        var key = StoredKey(requested);
        var matches = inventory.Where(track => requested.Id is > 0 && track.Id == requested.Id ||
            key != null && StoredKey(track) == key).Take(2).ToArray();
        if (matches.Length != 1) return null;
        var match = matches[0];
        return !match.Unplayable && string.Equals(match.Source, requested.Source, StringComparison.OrdinalIgnoreCase) &&
            !(requested.Id is > 0 && match.Id is > 0 && requested.Id != match.Id) &&
            !(!string.IsNullOrWhiteSpace(requested.SyncKey) && !string.IsNullOrWhiteSpace(match.SyncKey) &&
                requested.SyncKey != match.SyncKey) ? match : null;
    }

    private static string? StoredKey(SubtitleTrackInfo track)
        => !string.IsNullOrWhiteSpace(track.SyncKey) ? track.SyncKey : track.Id is > 0 ? $"stored-{track.Id}" : null;
}
