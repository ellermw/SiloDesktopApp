using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackPartSequence
{
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
