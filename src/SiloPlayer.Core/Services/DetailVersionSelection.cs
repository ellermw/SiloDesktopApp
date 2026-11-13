using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Detail-page Auto intent. Playback/Resume history keeps its separate policy.</summary>
public sealed class DetailVersionSelection
{
    public string? EditionKey { get; private set; }
    public int? ManualFileId { get; private set; }
    public string Quality { get; private set; } = "auto";
    public bool QualitySettled { get; private set; }

    public void SetQuality(string? quality, bool settled)
    {
        Quality = PlaybackQualityPreferences.Normalize(quality);
        QualitySettled = settled;
    }

    public void SelectEdition(string key) { EditionKey = key; ManualFileId = null; }
    public void SelectFile(FileVersion file) { EditionKey = file.EditionKey ?? EditionKey ?? ""; ManualFileId = file.FileId; }
    public void SelectAuto() => ManualFileId = null;

    public FileVersion? Resolve(IReadOnlyList<FileVersion> versions, IReadOnlyList<PlaybackVariant>? variants, WatchUserData? history)
    {
        if (ManualFileId is int id && versions.FirstOrDefault(file => file.FileId == id) is { } pinned)
            return pinned;
        ManualFileId = null;
        var selected = DetailVersionRanking.Select(versions, variants, history, Quality, EditionKey);
        if (QualitySettled && selected != null)
            EditionKey = DetailVersionRanking.Editions(versions, variants)
                .FirstOrDefault(edition => edition.AllVersions.Any(file => file.FileId == selected.FileId))?.Key
                ?? selected.EditionKey ?? "";
        return selected;
    }
}

/// <summary>Web detail Auto ranking: edition first, actual-file quality second, first parts only.</summary>
public static class DetailVersionRanking
{
    public sealed record Edition(string Key, string? Raw, List<FileVersion> Versions, List<FileVersion> AllVersions);

    public static int ResolutionScore(string? value) => value?.ToLowerInvariant() switch
    {
        "8k" or "4320p" => 6,
        "360p" => 0,
        _ => VersionRanking.ResolutionScore(value) is var score && score >= 0 ? score + 1 : -1,
    };

    public static List<Edition> Editions(IReadOnlyList<FileVersion> files, IReadOnlyList<PlaybackVariant>? variants)
    {
        var editions = new List<Edition>();
        void Add(string? key, string? raw, IEnumerable<FileVersion> first, IEnumerable<FileVersion> all)
        {
            var edition = editions.FirstOrDefault(e => Same(e.Key, key));
            if (edition == null) { edition = new(key ?? "", raw, [], []); editions.Add(edition); }
            edition.Versions.AddRange(first);
            edition.AllVersions.AddRange(all);
        }
        if (variants is { Count: > 0 })
        {
            foreach (var variant in variants.OrderBy(VersionRanking.PlaybackVariantEditionPreference))
            {
                var parts = variant.Parts.OrderBy(part => part.PartIndex).ToList();
                if (parts.FirstOrDefault() is not { Versions.Count: > 0 } first) continue;
                Add(variant.EditionKey, variant.EditionRaw, first.Versions, parts.SelectMany(part => part.Versions));
            }
        }
        else foreach (var file in files) Add(file.EditionKey, file.EditionRaw, [file], [file]);
        return editions;
    }

    public static FileVersion? Select(IReadOnlyList<FileVersion> files, IReadOnlyList<PlaybackVariant>? variants,
        WatchUserData? history, string? quality, string? preferredEditionKey = null)
    {
        var editions = Editions(files, variants);
        var edition = (preferredEditionKey == null ? null : editions.FirstOrDefault(e => Same(e.Key, preferredEditionKey)))
            ?? (history?.LastEditionKey == null ? null : editions.FirstOrDefault(e => Same(e.Key, history.LastEditionKey)))
            ?? editions.FirstOrDefault(e => e.AllVersions.Any(file => file.FileId == history?.LastFileId));
        return Rank(edition?.Versions ?? editions.SelectMany(e => e.Versions).ToList(), quality);
    }

    public static FileVersion? Rank(IReadOnlyList<FileVersion> files, string? quality)
    {
        IEnumerable<FileVersion> candidates = files;
        var known = files.Where(file => ResolutionScore(file.Resolution) >= 0).ToList();
        if (known.Count > 0) candidates = known;
        // Native quality tiers (1080p-high, etc.) share the same resolution cap.
        var cap = ResolutionScore(quality?.Split('-')[0]);
        var aboveCap = false;
        if (cap >= 0)
        {
            var within = candidates.Where(file => ResolutionScore(file.Resolution) is var score && score >= 0 && score <= cap).ToList();
            aboveCap = within.Count == 0;
            if (!aboveCap) candidates = within;
        }
        return candidates.OrderBy(file => (aboveCap ? 1 : -1) * ResolutionScore(file.Resolution))
            .ThenByDescending(file => file.Hdr).ThenByDescending(file => VersionRanking.AudioScore(file.CodecAudio)).FirstOrDefault();
    }

    public static List<FileVersion> DownloadFiles(FileVersion? selected, IReadOnlyList<PlaybackVariant>? variants, string? quality)
    {
        if (selected == null) return [];
        var variant = variants?.FirstOrDefault(v => v.Parts.Any(p => p.Versions.Any(file => file.FileId == selected.FileId)));
        return new[] { selected }.Concat(variant?.Parts.OrderBy(p => p.PartIndex)
            .Where(p => !p.Versions.Any(file => file.FileId == selected.FileId))
            .Select(p => Rank(p.Versions, quality)).OfType<FileVersion>() ?? []).ToList();
    }

    private static bool Same(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
}
