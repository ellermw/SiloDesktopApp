using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackPartSequence
{
    /// <summary>Zero-based index and count of the parts of the variant holding <paramref name="currentFileId"/>; null when unknown.</summary>
    public static (int Index, int Count)? PartOf(WatchDetailResponse? detail, int? currentFileId)
    {
        if (detail == null || !currentFileId.HasValue) return null;
        foreach (var variant in detail.PlaybackVariants)
        {
            var parts = variant.Parts.OrderBy(part => part.PartIndex).ToList();
            var index = parts.FindIndex(part => part.Versions.Any(version => version.FileId == currentFileId));
            if (index >= 0) return (index, parts.Count);
        }
        return null;
    }

    public static int? NextFileId(WatchDetailResponse? detail, int? currentFileId)
    {
        if (detail == null || !currentFileId.HasValue) return null;
        foreach (var variant in detail.PlaybackVariants)
        {
            var parts = variant.Parts.OrderBy(part => part.PartIndex).ToList();
            var index = parts.FindIndex(part => part.Versions.Any(version => version.FileId == currentFileId));
            if (index < 0) continue;
            var next = parts.ElementAtOrDefault(index + 1);
            return next?.DefaultFileId ?? next?.Versions.FirstOrDefault()?.FileId;
        }
        return null;
    }
}
